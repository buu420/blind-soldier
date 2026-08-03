using System.Collections.Immutable;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Battle;

internal static class Steam2026BattleAccessibilityCoordinatorTests
{
    private static readonly DateTime Timestamp =
        new(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);

    public static void Run(Steam2026FingerprintResult supportedRuntime)
    {
        CheckedWorkerProcessesEveryBattleDomainWithoutLeakingUnsensedEnemyState(
            supportedRuntime);
        BattleUpdateUsesTheProvenX86EnemyActionPipeline(supportedRuntime);
        BattleUpdatePreservesSightEquivalentActionDescriptions(supportedRuntime);
        FourPerFrameUpdatesUseOneSemanticWorkerObservation(supportedRuntime);
        ExactRawBattleUpdateStaysWithinTranslatedWorkerBudget(supportedRuntime);
        TransientEnemyActionResolutionFailureRetriesIdenticalRawCapture(supportedRuntime);
        ActionTextCommitDoesNotReplaceTheProvenX86Pipeline(supportedRuntime);
        CoalescesRendererBurstsAndPreservesPerEventInterruptPolicy(supportedRuntime);
        ChronologicalCombatSpeechPreservesNativeOrder(supportedRuntime);
        ActionAndDamageRemainAudibleAndOrdered(supportedRuntime);
        VisibleDamageConfirmsUnsensedEnemyDefeatWithoutLeakingPrivateStats(supportedRuntime);
        OwnershipLossResetsInteractionTrackersButResultsRemainReadable(supportedRuntime);
        BattleUpdateQueuesVictoryWithoutACompleteBattleTrackerSnapshot(supportedRuntime);
        CapturedLifecycleSurvivesWorkerSideMutation(supportedRuntime);
        TifaSlotsSpeakOnlyAfterTheAlignedSymbolWasRendered(supportedRuntime);
        SessionOptionsAndOwnershipCoverEveryBattleDomain();
    }

    private static void TifaSlotsSpeakOnlyAfterTheAlignedSymbolWasRendered(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var coordinator = CreateCoordinator(fixture, supportedRuntime);
        var spinning = new TifaSlotResultSnapshot(
            true,
            [new TifaSlotReelSnapshot(0, 4, false, true, TifaSlotSymbol.Hit)]);
        var settled = new TifaSlotResultSnapshot(
            true,
            [new TifaSlotReelSnapshot(0, 4, true, true, TifaSlotSymbol.Hit)]);

        coordinator.ProcessBatch(
        [
            Snapshot(
                1,
                Steam2026BattleRendererCallbackKind.MenuRenderer,
                guestValue: 0x1B,
                tifaSlotsBefore: spinning,
                tifaSlotsAfter: settled)
        ]);
        Equal(0, Drain(coordinator).Count, "new stop flag must wait until its symbol was rendered");

        coordinator.ProcessBatch(
        [
            Snapshot(
                2,
                Steam2026BattleRendererCallbackKind.MenuRenderer,
                guestValue: 0x1B,
                tifaSlotsBefore: settled,
                tifaSlotsAfter: settled)
        ]);
        var speech = Single(Drain(coordinator), "confirmed Tifa slot result");
        Equal(Steam2026BattleSpeechDomain.Message, speech.Domain, "Tifa slot speech domain");
        Equal("Hit", speech.Text, "Tifa slot result");

        coordinator.ProcessBatch(
        [
            Snapshot(
                3,
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                tifaSlotsCommittedAfter: new TifaSlotCommittedResultSnapshot(
                    true,
                    [TifaSlotSymbol.Hit, TifaSlotSymbol.Yeah, TifaSlotSymbol.Miss]))
        ]);
        var committedSpeech = Drain(coordinator);
        Contains(
            committedSpeech,
            Steam2026BattleSpeechDomain.Message,
            "Yeah!, Miss");

        coordinator.ProcessBatch(
        [
            Snapshot(
                4,
                Steam2026BattleRendererCallbackKind.MenuRenderer,
                guestValue: 0x1B,
                tifaSlotsBefore: settled,
                tifaSlotsAfter: settled)
        ]);
        Equal(0, Drain(coordinator).Count, "confirmed Tifa slot results do not repeat");
    }

    private static void ExactRawBattleUpdateStaysWithinTranslatedWorkerBudget(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var memory = new CountingNativeMemoryReader(fixture.Native);
        var coordinator = CreateCoordinator(fixture, supportedRuntime, memory);
        var raw = new Steam2026BattleEnemyActionIngressSnapshot(
            true,
            BattleEnemyActionSnapshot.Invalid,
            default,
            new Steam2026BattleRawEnemyActionIngressSnapshot(
                true,
                0,
                4,
                BattleStateReader.ActionAnimationEventKind,
                BattleStateReader.EnemyActionCommandId,
                2));
        memory.Reset();

        coordinator.ProcessBatch(
        [
            Snapshot(
                1,
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                enemyActionBefore: raw,
                enemyActionAfter: raw)
        ]);

        Contains(
            Drain(coordinator),
            Steam2026BattleSpeechDomain.Action,
            "Grunt used Rifle.");
        Equal(
            true,
            memory.ReadOperations <= 500,
            $"exact-raw battle worker translated read budget: {memory.ReadOperations}");
        Equal(
            true,
            memory.QueryOperations <= 340,
            $"exact-raw battle worker translated query budget: {memory.QueryOperations}");
    }

    private static void FourPerFrameUpdatesUseOneSemanticWorkerObservation(
        Steam2026FingerprintResult supportedRuntime)
    {
        static (int Reads, int Queries) Measure(
            Steam2026FingerprintResult runtime,
            int updateCount)
        {
            var fixture = BattleObservationFixture.CreatePopulated();
            var memory = new CountingNativeMemoryReader(fixture.Native);
            var coordinator = CreateCoordinator(fixture, runtime, memory);
            var raw = new Steam2026BattleEnemyActionIngressSnapshot(
                true,
                BattleEnemyActionSnapshot.Invalid,
                default,
                new Steam2026BattleRawEnemyActionIngressSnapshot(
                    true,
                    0,
                    4,
                    BattleStateReader.ActionAnimationEventKind,
                    BattleStateReader.EnemyActionCommandId,
                    2));
            var updates = Enumerable.Range(1, updateCount)
                .Select(sequence => Snapshot(
                    sequence,
                    Steam2026BattleRendererCallbackKind.BattleUpdate,
                    enemyActionBefore: raw,
                    enemyActionAfter: raw))
                .ToArray();
            memory.Reset();

            coordinator.ProcessBatch(updates);

            Contains(
                Drain(coordinator),
                Steam2026BattleSpeechDomain.Action,
                "Grunt used Rifle.");
            return (memory.ReadOperations, memory.QueryOperations);
        }

        var single = Measure(supportedRuntime, 1);
        var four = Measure(supportedRuntime, 4);
        Equal(
            true,
            four.Reads <= single.Reads + 40,
            $"four per-frame update ticks share one semantic read: one={single.Reads}, four={four.Reads}");
        Equal(
            true,
            four.Queries <= single.Queries + 12,
            $"four per-frame update ticks share one semantic query set: one={single.Queries}, four={four.Queries}");
    }

    private static void TransientEnemyActionResolutionFailureRetriesIdenticalRawCapture(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var actionNameAddress = (uint)(BattleStateReader.AddressSceneAttackNames
                                      + 2 * BattleStateReader.SceneAttackNameLength);
        var actionNameHostPage = fixture.GetHostAddress(actionNameAddress) & ~0xFFFUL;
        var coordinator = CreateCoordinator(fixture, supportedRuntime);
        var raw = new Steam2026BattleEnemyActionIngressSnapshot(
            true,
            BattleEnemyActionSnapshot.Invalid,
            default,
            new Steam2026BattleRawEnemyActionIngressSnapshot(
                true,
                0,
                4,
                BattleStateReader.ActionAnimationEventKind,
                BattleStateReader.EnemyActionCommandId,
                2));

        fixture.UnmapGuestPage(actionNameAddress);
        coordinator.ProcessBatch(
        [
            Snapshot(
                1,
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                enemyActionBefore: raw)
        ]);
        Equal(
            false,
            Drain(coordinator).Any(item => item.Domain == Steam2026BattleSpeechDomain.Action),
            "transient action-name read failure emits no guessed action speech");

        fixture.MapGuestPage(actionNameAddress, actionNameHostPage);
        coordinator.ProcessBatch(
        [
            Snapshot(
                2,
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                enemyActionBefore: raw)
        ]);

        Contains(
            Drain(coordinator),
            Steam2026BattleSpeechDomain.Action,
            "Grunt used Rifle.");
    }

    private static void BattleUpdatePreservesSightEquivalentActionDescriptions(
        Steam2026FingerprintResult supportedRuntime)
    {
        foreach (var expected in new[]
                 {
                     (SceneSlot: (ushort)7, ActionId: (ushort)0x011F,
                         Speech: "Guard Scorpion raises its tail."),
                     (SceneSlot: (ushort)8, ActionId: (ushort)0x0120,
                         Speech: "Guard Scorpion lowers its tail.")
                 })
        {
            var fixture = BattleObservationFixture.CreatePopulated();
            fixture.WriteUInt16(BattleStateReader.AddressBattleFormationId, 324);
            fixture.WriteUInt16(
                BattleStateReader.AddressSceneAttackIds
                + expected.SceneSlot * BattleStateReader.SceneAttackIdSize,
                expected.ActionId);
            fixture.Write(
                (uint)(BattleStateReader.AddressSceneAttackNames
                       + expected.SceneSlot * BattleStateReader.SceneAttackNameLength),
                Enumerable.Repeat((byte)0xFF, BattleStateReader.SceneAttackNameLength).ToArray());
            var coordinator = CreateCoordinator(fixture, supportedRuntime);
            var raw = new Steam2026BattleEnemyActionIngressSnapshot(
                true,
                BattleEnemyActionSnapshot.Invalid,
                default,
                new Steam2026BattleRawEnemyActionIngressSnapshot(
                    true,
                    0,
                    4,
                    BattleStateReader.ActionAnimationEventKind,
                    BattleStateReader.EnemyActionCommandId,
                    expected.SceneSlot));

            coordinator.ProcessBatch(
            [
                Snapshot(
                    1,
                    Steam2026BattleRendererCallbackKind.BattleUpdate,
                    enemyActionBefore: raw)
            ]);

            Contains(
                Drain(coordinator),
                Steam2026BattleSpeechDomain.Action,
                expected.Speech);
        }
    }

    private static void BattleUpdateUsesTheProvenX86EnemyActionPipeline(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var coordinator = CreateCoordinator(fixture, supportedRuntime);

        coordinator.ProcessBatch(
        [
            Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate)
        ]);
        var first = Drain(coordinator);
        Contains(first, Steam2026BattleSpeechDomain.Action, "Grunt used Rifle.");

        coordinator.ProcessBatch(
        [
            Snapshot(2, Steam2026BattleRendererCallbackKind.BattleUpdate)
        ]);
        Equal(
            false,
            Drain(coordinator).Any(item => item.Domain == Steam2026BattleSpeechDomain.Action),
            "the same x86 animation event is deduplicated");

        const byte nextEventIndex = 1;
        var nextEvent = BattleStateReader.AddressAnimationEventQueue
                        + nextEventIndex * BattleStateReader.AnimationEventSize;
        fixture.WriteByte(BattleStateReader.AddressAnimationEventIndex, nextEventIndex);
        fixture.WriteByte(
            nextEvent + BattleStateReader.AnimationEventAttackerOffset,
            4);
        fixture.WriteByte(
            nextEvent + BattleStateReader.AnimationEventKindOffset,
            BattleStateReader.ActionAnimationEventKind);
        fixture.WriteByte(
            nextEvent + BattleStateReader.AnimationEventCommandOffset,
            BattleStateReader.EnemyActionCommandId);
        fixture.WriteUInt16(
            nextEvent + BattleStateReader.AnimationEventActionOffset,
            2);
        fixture.WriteUInt16(BattleStateReader.AddressBattleActionTargetMask, 0);
        fixture.WriteInt32(
            BattleStateReader.AddressBattleActors
            + BattleStateReader.ActorCurrentHpOffset,
            302);

        coordinator.ProcessBatch(
        [
            Snapshot(3, Steam2026BattleRendererCallbackKind.BattleUpdate),
            Snapshot(
                4,
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                capturedDamage: new BattleDamagePopupSnapshot(true, 5, 0, 12, 0))
        ]);
        var second = Drain(coordinator);
        var urgent = second
            .Where(item => item.Domain is Steam2026BattleSpeechDomain.Action
                or Steam2026BattleSpeechDomain.Damage)
            .ToArray();
        Equal(
            2,
            urgent.Length,
            "new enemy action and its visible damage both speak; got "
            + string.Join(", ", second.Select(item => $"{item.Domain}:{item.Text}")));
        Equal(Steam2026BattleSpeechDomain.Action, urgent[0].Domain, "action precedes damage");
        Equal("Grunt used Rifle.", urgent[0].Text, "next x86 animation event speaks");
        Equal(Steam2026BattleSpeechDomain.Damage, urgent[1].Domain, "damage follows action");
        Equal("Cloud took 12 damage.", urgent[1].Text, "visible damage remains immediate");
    }

    private static void VisibleDamageConfirmsUnsensedEnemyDefeatWithoutLeakingPrivateStats(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var coordinator = CreateCoordinator(fixture, supportedRuntime);
        coordinator.ProcessBatch(
        [
            Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate)
        ]);
        _ = Drain(coordinator);

        const int enemyActor = 4;
        const int effectIndex = 5;
        var enemyAddress = BattleStateReader.AddressBattleActors
                           + enemyActor * BattleStateReader.BattleActorSize;
        fixture.WriteInt32(
            enemyAddress + BattleStateReader.ActorStatusMaskOffset,
            (1 << 3) | 1);
        fixture.WriteInt32(enemyAddress + BattleStateReader.ActorCurrentHpOffset, 0);
        var effectAddress = BattleDamagePopupReader.AddressEffectData
                            + effectIndex * BattleDamagePopupReader.EffectRecordSize;
        fixture.WriteUInt16(effectAddress + BattleDamagePopupReader.ValueOffset, 42);
        fixture.WriteInt32(effectAddress + BattleDamagePopupReader.TargetActorOffset, enemyActor);

        coordinator.ProcessBatch(
        [
            Snapshot(
                2,
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                capturedDamage: new BattleDamagePopupSnapshot(
                    true,
                    effectIndex,
                    enemyActor,
                    42,
                    0))
        ]);
        var speech = Drain(coordinator);
        Contains(speech, Steam2026BattleSpeechDomain.Damage, "Grunt took 42 damage.");
        Contains(speech, Steam2026BattleSpeechDomain.Status, "Grunt was defeated.");
        Equal(
            false,
            speech.Any(item => item.Text.Contains("HP", StringComparison.Ordinal)),
            "visible defeat correlation never exposes unsensed enemy HP");
    }

    private static void SessionOptionsAndOwnershipCoverEveryBattleDomain()
    {
        var config = new AccessibilityConfig
        {
            EnableBattleMenuSpeech = false,
            EnableBattleTargetSpeech = false,
            EnableBattleMessageSpeech = false,
            EnableBattleResultsSpeech = false,
            EnableBattleDamageSpeech = false,
            EnableBattleEncounterSpeech = false,
            EnableBattleEnemyActionSpeech = false,
            EnableBattleStatusSpeech = true
        };
        var statusOnly = Steam2026ResearchSession.CreateBattleOptions(config);
        Equal(true, statusOnly.AnyEnabled, "status-only config activates battle cohort");
        Equal(true, statusOnly.Status, "status-only config maps status domain");
        Equal(false, statusOnly.Menu, "status-only config does not force menu domain");

        config.EnableBattleStatusSpeech = false;
        config.EnableBattleResultsSpeech = true;
        var resultsOnly = Steam2026ResearchSession.CreateBattleOptions(config);
        Equal(true, resultsOnly.AnyEnabled, "results-only config activates battle cohort");
        Equal(true, resultsOnly.Results, "results-only config maps results domain");

        Equal(
            true,
            Steam2026ResearchSession.IsBattleAccessibilityModule(BattleStateReader.BattleModule),
            "battle module ownership");
        Equal(
            true,
            Steam2026ResearchSession.IsBattleAccessibilityModule(BattleResultsReader.ResultsModule),
            "results module ownership");
        Equal(
            false,
            Steam2026ResearchSession.IsBattleAccessibilityModule(1),
            "field module is not battle ownership");
    }

    private static void ActionTextCommitDoesNotReplaceTheProvenX86Pipeline(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var coordinator = CreateCoordinator(fixture, supportedRuntime);
        coordinator.ProcessBatch(
        [
            Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate)
        ]);
        Contains(
            Drain(coordinator),
            Steam2026BattleSpeechDomain.Action,
            "Grunt used Rifle.");

        coordinator.ProcessBatch(
        [
            Snapshot(
                2,
                Steam2026BattleRendererCallbackKind.ActionTextCommit,
                capturedAction: Action(0, 1, 0, 30, effectIndex: 3))
        ]);
        Equal(
            0,
            Drain(coordinator).Count,
            "the supplemental action-text helper does not invent player action speech");

        coordinator.ProcessBatch(
        [
            Snapshot(
                3,
                Steam2026BattleRendererCallbackKind.ActionTextCommit,
                capturedAction: Action(4, 0x20, 2, 30, effectIndex: 4))
        ]);
        Equal(
            0,
            Drain(coordinator).Count,
            "the action-text helper cannot duplicate BattleUpdate enemy speech");
    }

    private static void CheckedWorkerProcessesEveryBattleDomainWithoutLeakingUnsensedEnemyState(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var coordinator = CreateCoordinator(fixture, supportedRuntime);

        coordinator.ProcessBatch(
        [
            Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate),
            Snapshot(2, Steam2026BattleRendererCallbackKind.MenuRenderer, 1)
        ]);
        var initial = Drain(coordinator);
        Contains(initial, Steam2026BattleSpeechDomain.Encounter, "Back attack. Enemies: Grunt.");
        Contains(initial, Steam2026BattleSpeechDomain.Action, "Grunt used Rifle.");
        Contains(initial, Steam2026BattleSpeechDomain.Target, "All enemies");
        Contains(initial, Steam2026BattleSpeechDomain.Menu, "Cloud. Attack");
        Equal(
            false,
            initial.Any(item => item.Text.Contains("Grunt. HP", StringComparison.Ordinal)),
            "unsensed enemy HP never reaches worker speech");

        coordinator.ProcessBatch(
        [
            Snapshot(3, Steam2026BattleRendererCallbackKind.TextActivation, 7)
        ]);
        var message = Single(Drain(coordinator), "battle text activation");
        Equal(Steam2026BattleSpeechDomain.Message, message.Domain, "battle message domain");
        Equal("Limit break ready.", message.Text, "resolved native battle message");
        Equal(true, message.Interrupt, "battle message interrupt policy");

        fixture.WriteInt32(
            BattleStateReader.AddressBattleActors + BattleStateReader.ActorCurrentHpOffset,
            302);
        fixture.WriteByte(
            BattleDamagePopupReader.AddressEffectData
            + 5 * BattleDamagePopupReader.EffectRecordSize
            + BattleDamagePopupReader.StateOffset,
            1);
        coordinator.ProcessBatch(
        [
            Snapshot(
                4,
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                capturedDamage: new BattleDamagePopupSnapshot(true, 5, 0, 12, 0))
        ]);
        var damage = Single(Drain(coordinator), "battle damage callback");
        Equal(Steam2026BattleSpeechDomain.Damage, damage.Domain, "battle damage domain");
        Equal("Cloud took 12 damage.", damage.Text, "checked correlated battle damage");
        Equal(
            false,
            damage.Interrupt,
            "damage remains ordered behind the just-announced enemy action");

        fixture.SwitchToResultsModule();
        coordinator.ProcessBatch(
        [
            Snapshot(5, Steam2026BattleRendererCallbackKind.ResultsUpdate, resultsBefore: CaptureResults(fixture))
        ]);
        var victory = Single(Drain(coordinator), "battle results experience page");
        Equal(Steam2026BattleSpeechDomain.Results, victory.Domain, "battle results domain");
        Equal("125 experience. 8 AP.", victory.Text, "checked results experience page");
        Equal(false, victory.Interrupt, "results do not interrupt active speech");

        fixture.WriteInt32(BattleResultsReader.AddressResultsState, 2);
        coordinator.ProcessBatch(
        [
            Snapshot(6, Steam2026BattleRendererCallbackKind.ResultsUpdate, resultsBefore: CaptureResults(fixture))
        ]);
        var rewards = Drain(coordinator);
        Equal(1, rewards.Count, "battle results reward page and focus are coalesced");
        Equal(
            "96 gil. Items available: Phoenix Down x2. Items selected: none. Take everything.",
            rewards[0].Text,
            "checked native result reward panes");
        Equal(false, rewards[0].Interrupt, "initial result page preserves ordered narration");

        fixture.WriteInt32(BattleResultsReader.AddressRewardSelection, 1);
        fixture.WriteInt32(BattleResultsReader.AddressInputEdges, 0x4000);
        coordinator.ProcessBatch(
        [
            Snapshot(7, Steam2026BattleRendererCallbackKind.ResultsUpdate, resultsBefore: CaptureResults(fixture))
        ]);
        var rowFocus = Single(Drain(coordinator), "battle reward row focus");
        Equal(
            "Phoenix Down x2. Not selected.",
            rowFocus.Text,
            "checked live reward cursor");
        Equal(true, rowFocus.Interrupt, "live reward cursor replaces stale result focus");

        fixture.WriteUInt16(
            BattleResultsReader.AddressRewardItems + BattleResultsReader.RewardSelectedOffset,
            1);
        coordinator.ProcessBatch(
        [
            Snapshot(8, Steam2026BattleRendererCallbackKind.ResultsUpdate, resultsBefore: CaptureResults(fixture))
        ]);
        var selectionState = Single(Drain(coordinator), "battle reward selection state");
        Equal(
            "Phoenix Down x2. Selected to take.",
            selectionState.Text,
            "checked live selected pane");
        Equal(true, selectionState.Interrupt, "live selected pane replaces stale state");

        fixture.WriteInt32(BattleResultsReader.AddressRewardSelection, 5);
        coordinator.ProcessBatch(
        [
            Snapshot(9, Steam2026BattleRendererCallbackKind.ResultsUpdate, resultsBefore: CaptureResults(fixture))
        ]);
        var exitFocus = Single(Drain(coordinator), "battle reward Exit focus");
        Equal(
            "Exit.",
            exitFocus.Text,
            "checked native reward exit row");
        Equal(true, exitFocus.Interrupt, "live Exit focus replaces stale row speech");
    }

    private static void CoalescesRendererBurstsAndPreservesPerEventInterruptPolicy(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var coordinator = CreateCoordinator(fixture, supportedRuntime);

        coordinator.ProcessBatch(
        [
            Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate),
            Snapshot(2, Steam2026BattleRendererCallbackKind.MenuRenderer, 1),
            Snapshot(3, Steam2026BattleRendererCallbackKind.MenuRenderer, 1),
            Snapshot(4, Steam2026BattleRendererCallbackKind.MenuRenderer, 1)
        ]);
        var speech = Drain(coordinator);
        Equal(
            1,
            speech.Count(item => item.Domain == Steam2026BattleSpeechDomain.Menu),
            "renderer burst coalesces to one menu announcement");
        Equal(
            true,
            speech.Single(item => item.Domain == Steam2026BattleSpeechDomain.Encounter).Interrupt,
            "encounter retains its x86 interrupt policy");
        Equal(
            true,
            speech.Single(item => item.Domain == Steam2026BattleSpeechDomain.Target).Interrupt,
            "target retains its x86 interrupt policy in the same worker batch");
        Equal(
            true,
            speech.Single(item => item.Domain == Steam2026BattleSpeechDomain.Menu).Interrupt,
            "menu retains its x86 interrupt policy in the same worker batch");

        coordinator.ProcessBatch(
        [
            Snapshot(5, Steam2026BattleRendererCallbackKind.MenuRenderer, 1),
            Snapshot(6, Steam2026BattleRendererCallbackKind.MenuRenderer, 1)
        ]);
        Equal(0, Drain(coordinator).Count, "unchanged renderer burst stays silent");
    }

    private static void ChronologicalCombatSpeechPreservesNativeOrder(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var coordinator = CreateCoordinator(fixture, supportedRuntime);
        coordinator.ProcessBatch(
        [
            Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate),
            Snapshot(2, Steam2026BattleRendererCallbackKind.MenuRenderer, 1)
        ]);
        fixture.WriteInt32(
            BattleStateReader.AddressBattleActors + BattleStateReader.ActorCurrentHpOffset,
            302);
        coordinator.ProcessBatch(
        [
            Snapshot(
                3,
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                capturedDamage: new BattleDamagePopupSnapshot(true, 5, 0, 12, 0))
        ]);

        var speech = Drain(coordinator);
        var expected = new[]
        {
            Steam2026BattleSpeechDomain.Encounter,
            Steam2026BattleSpeechDomain.Action,
            Steam2026BattleSpeechDomain.Target,
            Steam2026BattleSpeechDomain.Menu,
            Steam2026BattleSpeechDomain.Damage
        };
        Equal(
            true,
            speech.Select(item => item.Domain).SequenceEqual(expected),
            "battle speech preserves native callback chronology");
        Equal("Grunt used Rifle.", speech[1].Text, "enemy action text");
        Equal(false, speech[1].Interrupt, "enemy action matches x86 noninterrupt policy");
        Equal("Cloud took 12 damage.", speech[4].Text, "damage text");
        Equal(false, speech[4].Interrupt, "damage matches x86 noninterrupt policy");
    }

    private static void ActionAndDamageRemainAudibleAndOrdered(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var coordinator = CreateCoordinator(fixture, supportedRuntime);
        coordinator.ProcessBatch(
        [
            Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate)
        ]);
        _ = Drain(coordinator);

        WriteEnemyActionEvent(fixture, eventIndex: 1);

        coordinator.ProcessBatch(
        [
            Snapshot(
                2,
                Steam2026BattleRendererCallbackKind.BattleUpdate),
            Snapshot(
                3,
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                capturedDamage: new BattleDamagePopupSnapshot(true, 21, 4, 12, 0)),
            Snapshot(
                4,
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                capturedDamage: new BattleDamagePopupSnapshot(true, 22, 4, 7, 0))
        ]);
        var firstBurst = Drain(coordinator);
        Equal(3, firstBurst.Count, "action and multi-target damage burst count");
        Equal("Grunt used Rifle.", firstBurst[0].Text, "burst action order");
        Equal("Grunt took 12 damage.", firstBurst[1].Text, "first burst damage order");
        Equal("Grunt took 7 damage.", firstBurst[2].Text, "second burst damage order");
        Equal(false, firstBurst[0].Interrupt, "action matches x86 noninterrupt policy");
        Equal(false, firstBurst[1].Interrupt, "first damage queues behind its action");
        Equal(false, firstBurst[2].Interrupt, "following damage cannot cut off prior damage");

        coordinator.ProcessBatch(
        [
            Snapshot(
                5,
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                capturedDamage: new BattleDamagePopupSnapshot(true, 23, 4, 3, 0))
        ]);
        Equal(
            false,
            Single(Drain(coordinator), "continued damage sequence").Interrupt,
            "a continued damage callback remains noninterrupting");

        WriteEnemyActionEvent(fixture, eventIndex: 2);
        coordinator.ProcessBatch(
        [
            Snapshot(
                6,
                Steam2026BattleRendererCallbackKind.BattleUpdate)
        ]);
        Equal(
            false,
            Single(Drain(coordinator), "next action").Interrupt,
            "the next distinct action remains noninterrupting like x86");

        coordinator.ProcessBatch(
        [
            Snapshot(
                7,
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                capturedDamage: new BattleDamagePopupSnapshot(true, 25, 4, 2, 0))
        ]);
        Equal(
            false,
            Single(Drain(coordinator), "damage after next action").Interrupt,
            "damage remains queued behind its new action");

        coordinator.ProcessBatch(
        [
            Snapshot(
                2008,
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                capturedDamage: new BattleDamagePopupSnapshot(true, 26, 4, 1, 0))
        ]);
        Equal(
            false,
            Single(Drain(coordinator), "damage after quiet gap").Interrupt,
            "damage remains noninterrupting after a quiet gap like x86");
    }

    private static void WriteEnemyActionEvent(
        BattleObservationFixture fixture,
        byte eventIndex)
    {
        fixture.WriteByte(BattleStateReader.AddressAnimationEventIndex, eventIndex);
        var address = BattleStateReader.AddressAnimationEventQueue
                      + eventIndex * BattleStateReader.AnimationEventSize;
        fixture.WriteByte(address + BattleStateReader.AnimationEventAttackerOffset, 4);
        fixture.WriteByte(
            address + BattleStateReader.AnimationEventKindOffset,
            BattleStateReader.ActionAnimationEventKind);
        fixture.WriteByte(
            address + BattleStateReader.AnimationEventCommandOffset,
            BattleStateReader.EnemyActionCommandId);
        fixture.WriteUInt16(
            address + BattleStateReader.AnimationEventActionOffset,
            2);
    }

    private static void OwnershipLossResetsInteractionTrackersButResultsRemainReadable(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var coordinator = CreateCoordinator(fixture, supportedRuntime);
        coordinator.ProcessBatch(
        [
            Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate),
            Snapshot(2, Steam2026BattleRendererCallbackKind.MenuRenderer, 1)
        ]);
        _ = Drain(coordinator);

        fixture.SwitchToResultsModule();
        coordinator.ProcessBatch(
        [
            Snapshot(3, Steam2026BattleRendererCallbackKind.ResultsUpdate, resultsBefore: CaptureResults(fixture))
        ]);
        Contains(
            Drain(coordinator),
            Steam2026BattleSpeechDomain.Results,
            "125 experience. 8 AP.");

        fixture.WriteByte(BattleStateReader.AddressCurrentModule, 1);
        coordinator.ProcessBatch(
        [
            Snapshot(4, Steam2026BattleRendererCallbackKind.BattleUpdate)
        ]);
        Equal(0, Drain(coordinator).Count, "non-battle ownership produces no speech");

        fixture.WriteByte(BattleStateReader.AddressCurrentModule, BattleStateReader.BattleModule);
        coordinator.ProcessBatch(
        [
            Snapshot(5, Steam2026BattleRendererCallbackKind.BattleUpdate),
            Snapshot(6, Steam2026BattleRendererCallbackKind.MenuRenderer, 1)
        ]);
        var reentered = Drain(coordinator);
        Contains(reentered, Steam2026BattleSpeechDomain.Encounter, "Back attack. Enemies: Grunt.");
        Contains(reentered, Steam2026BattleSpeechDomain.Menu, "Cloud. Attack");
    }

    private static void BattleUpdateQueuesVictoryWithoutACompleteBattleTrackerSnapshot(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        fixture.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 1);
        fixture.UnmapGuestPage((uint)BattleStateReader.AddressBattleActors);
        var coordinator = CreateCoordinator(fixture, supportedRuntime);

        coordinator.ProcessBatch(
        [
            Snapshot(
                1,
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                victoryAfter: new Steam2026BattleVictoryIngressSnapshot(true, true)),
            Snapshot(2, Steam2026BattleRendererCallbackKind.TextActivation, 7)
        ]);
        var victory = Single(Drain(coordinator), "battle-update victory edge");
        Equal(Steam2026BattleSpeechDomain.Results, victory.Domain, "victory uses results domain");
        Equal(
            "Victory. The party strikes victory poses.",
            victory.Text,
            "coherent native victory edge describes the visible celebration");
        Equal(true, victory.Interrupt, "victory interrupts stale battle speech");

        coordinator.ProcessBatch(
        [
            Snapshot(
                3,
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                victoryAfter: new Steam2026BattleVictoryIngressSnapshot(true, true))
        ]);
        Equal(0, Drain(coordinator).Count, "steady native victory signal is not repeated");

        fixture.SwitchToResultsModule();
        coordinator.ProcessBatch(
        [
            Snapshot(4, Steam2026BattleRendererCallbackKind.ResultsUpdate, resultsBefore: CaptureResults(fixture))
        ]);
        Contains(
            Drain(coordinator),
            Steam2026BattleSpeechDomain.Results,
            "125 experience. 8 AP.");
    }

    private static void CapturedLifecycleSurvivesWorkerSideMutation(
        Steam2026FingerprintResult supportedRuntime)
    {
        var victoryFixture = BattleRendererIngressFixture.Create();
        victoryFixture.Battle.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 0);
        var victoryQueue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(2);
        using var victoryIngress = new Steam2026BattleRendererDetourIngressCoordinator(
            new Steam2026BattleRendererCallbackContract(
                supportedRuntime,
                BattleObservationFixture.ModuleBase,
                0x02100000,
                victoryFixture.Battle.Native),
            () => { },
            () => victoryFixture.Battle.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 1),
            () => { },
            () => { },
            () => { },
            () => { },
            () => Timestamp,
            victoryQueue);
        victoryIngress.OnBattleUpdate();
        victoryFixture.Battle.SwitchToResultsModule();
        Equal(true, victoryQueue.TryDequeue(out var capturedVictory), "captured victory ingress");
        var victoryCoordinator = CreateCoordinator(victoryFixture.Battle, supportedRuntime);
        victoryCoordinator.ProcessBatch(
        [
            capturedVictory
        ]);
        Contains(
            Drain(victoryCoordinator),
            Steam2026BattleSpeechDomain.Results,
            "Victory. The party strikes victory poses.");

        var resultsFixture = BattleRendererIngressFixture.Create();
        resultsFixture.Battle.SwitchToResultsModule();
        var resultsQueue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(2);
        using var resultsIngress = new Steam2026BattleRendererDetourIngressCoordinator(
            new Steam2026BattleRendererCallbackContract(
                supportedRuntime,
                BattleObservationFixture.ModuleBase,
                0x02100000,
                resultsFixture.Battle.Native),
            () => { },
            () => { },
            () => { },
            () =>
            {
                resultsFixture.Battle.WriteInt32(BattleResultsReader.AddressResultsState, 2);
                resultsFixture.Battle.WriteInt32(BattleResultsReader.AddressGil, 0);
            },
            () => { },
            () => { },
            () => Timestamp,
            resultsQueue);
        resultsIngress.OnResultsUpdate();
        resultsFixture.Battle.WriteByte(BattleStateReader.AddressCurrentModule, 1);
        Equal(true, resultsQueue.TryDequeue(out var capturedResults), "captured results ingress");
        var resultsCoordinator = CreateCoordinator(resultsFixture.Battle, supportedRuntime);
        resultsCoordinator.ProcessBatch(
        [
            capturedResults
        ]);
        var speech = Drain(resultsCoordinator);
        Contains(speech, Steam2026BattleSpeechDomain.Results, "125 experience. 8 AP.");
        Contains(
            speech,
            Steam2026BattleSpeechDomain.Results,
            "96 gil. Items available: Phoenix Down x2. Items selected: none. Take everything.");
    }

    private static Steam2026BattleAccessibilityCoordinator CreateCoordinator(
        BattleObservationFixture fixture,
        Steam2026FingerprintResult supportedRuntime,
        INativeMemoryReader? memory = null)
    {
        var resolvers = new Steam2026BattleTextResolvers(
            Steam2026BattleObservationTests.Resolvers.ResolveAbilityName,
            Steam2026BattleObservationTests.Resolvers.ResolveAbilityDescription,
            Steam2026BattleObservationTests.Resolvers.ResolveItemName,
            Steam2026BattleObservationTests.Resolvers.ResolveItemDescription,
            Steam2026BattleObservationTests.Resolvers.ResolveCommandName,
            Steam2026BattleObservationTests.Resolvers.ResolveInventoryObjectName,
            bufferIndex => bufferIndex == 7 ? "Limit break ready." : null);
        return CreateCoordinator(fixture, supportedRuntime, resolvers, memory);
    }

    private static Steam2026BattleAccessibilityCoordinator CreateCoordinator(
        BattleObservationFixture fixture,
        Steam2026FingerprintResult supportedRuntime,
        Steam2026BattleTextResolvers resolvers,
        INativeMemoryReader? memory = null)
    {
        return new Steam2026BattleAccessibilityCoordinator(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            memory ?? fixture.Native,
            resolvers,
            Steam2026BattleAccessibilityOptions.AllEnabled);
    }

    private static Steam2026BattleRendererIngressSnapshot Snapshot(
        long sequence,
        Steam2026BattleRendererCallbackKind kind,
        short guestValue = 0,
        BattleDamagePopupSnapshot capturedDamage = default,
        Steam2026BattleActionTextCommitSnapshot capturedAction = default,
        Steam2026BattleEnemyActionIngressSnapshot enemyActionBefore = default,
        Steam2026BattleEnemyActionIngressSnapshot enemyActionAfter = default,
        Steam2026BattleVictoryIngressSnapshot victoryAfter = default,
        Steam2026BattleResultsIngressSnapshot resultsBefore = default,
        Steam2026BattleResultsIngressSnapshot resultsAfter = default,
        TifaSlotResultSnapshot tifaSlotsBefore = default,
        TifaSlotResultSnapshot tifaSlotsAfter = default,
        TifaSlotCommittedResultSnapshot tifaSlotsCommittedAfter = default) =>
        new(
            sequence,
            Timestamp.AddMilliseconds(sequence),
            kind,
            guestValue,
            capturedDamage,
            capturedAction,
            enemyActionBefore,
            enemyActionAfter,
            victoryAfter,
            resultsBefore,
            resultsAfter,
            tifaSlotsBefore,
            tifaSlotsAfter,
            tifaSlotsCommittedAfter);

    private static Steam2026BattleResultsIngressSnapshot CaptureResults(
        BattleObservationFixture fixture)
    {
        var results = new BattleResultsReader(
            fixture.Direct,
            Steam2026BattleObservationTests.Resolvers.ResolveInventoryObjectName).Read();
        var stateReader = new BattleStateReader(
            fixture.Direct,
            new SavemapPartyReader(fixture.Direct));
        Equal(true, results.IsValid, "test results boundary source");
        Equal(true, stateReader.TryReadPartyProgress(out var progress), "test progress boundary source");
        var rewards = Enumerable.Range(0, BattleResultsReader.RewardItemCount)
            .Select(slot => results.Items.FirstOrDefault(item => item.PhysicalSlot == slot))
            .Select(item => item.Name is null
                ? new Steam2026BattleRewardIngressSnapshot(ushort.MaxValue, 0, 0)
                : new Steam2026BattleRewardIngressSnapshot(
                    checked((ushort)item.ItemId),
                    checked((ushort)item.Quantity),
                    item.IsSelectedToTake ? (ushort)1 : (ushort)0))
            .ToImmutableArray();
        return new Steam2026BattleResultsIngressSnapshot(
            true,
            results.State,
            results.Experience,
            results.Ap,
            results.Gil,
            results.IsPageReady,
            results.HasRewardItems,
            results.RewardSelection,
            results.RewardTransition,
            results.InputEdges,
            results.InputRepeat,
            results.HeldInput,
            rewards,
            progress.ToImmutableArray());
    }

    private static Steam2026BattleActionTextCommitSnapshot Action(
        byte actorIndex,
        byte commandId,
        ushort actionId,
        short remainingFrames,
        ushort effectIndex) =>
        new(
            true,
            effectIndex,
            actorIndex,
            commandId,
            actionId,
            remainingFrames);

    private static List<Steam2026BattleSpeech> Drain(
        Steam2026BattleAccessibilityCoordinator coordinator)
    {
        var result = new List<Steam2026BattleSpeech>();
        while (coordinator.TrySpeakPending(_ => true, out var speech))
        {
            result.Add(speech);
        }

        return result;
    }

    private static Steam2026BattleSpeech Single(
        IReadOnlyList<Steam2026BattleSpeech> items,
        string label)
    {
        Equal(1, items.Count, $"{label} count");
        return items[0];
    }

    private static void Contains(
        IEnumerable<Steam2026BattleSpeech> items,
        Steam2026BattleSpeechDomain domain,
        string text)
    {
        if (!items.Any(item => item.Domain == domain && string.Equals(item.Text, text, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Missing {domain} speech: {text}");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
