using System.Reflection;
using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Battle;
using Reloaded.Hooks.Definitions;

internal static class Steam2026BattleRendererIngressTests
{
    private const ulong ModuleImageSize = 0x02100000;
    private static readonly DateTime Timestamp =
        new(2026, 7, 20, 13, 0, 0, DateTimeKind.Utc);

    public static void Run(
        Steam2026FingerprintResult supportedRuntime,
        Steam2026FingerprintResult unsupportedRuntime)
    {
        CatalogAndDelegatesExposeTheExactBattleCohort();
        NativeBattleCallbacksExcludeSemanticSpeechDispatch();
        FourPerFrameBattleUpdateStaysWithinBoundedNativeReadBudget(supportedRuntime);
        ResultsUpdateStaysWithinBoundedNativeReadBudget(supportedRuntime);
        ContractCapturesTheStableGuestRendererState(
            supportedRuntime,
            unsupportedRuntime);
        ActiveLeaseCohortDisableFailsTheRateLimitedHealthProbe(supportedRuntime);
        ActiveLeaseAllowsBattlePagesToMapAfterStartup(supportedRuntime);
        RawCaptureRejectsAndRecoversFromMidReadPageRemap(supportedRuntime);
        HealthyNoActionCaptureDoesNotPoisonTheActiveLease(supportedRuntime);
        HookSetPollsLeaseHealthOutsideTheNativeCallbackPath();
        IngressCapturesBeforeOriginalAndInvokesItExactlyOnce(supportedRuntime);
        BattleUpdateIngressCapturesEnemyActionBeforeAndAfterOriginal(supportedRuntime);
        LifecycleIngressCapturesVictoryAndResultsAroundOriginal(supportedRuntime);
        DamageIngressCopiesThePopupBeforeTheOriginalRetiresIt(supportedRuntime);
        ActionTextIngressCopiesTheVisibleCommitBeforeOriginal(supportedRuntime);
        IngressPublishesTheSixCallbackCohortWithCheckedGuestArguments(supportedRuntime);
        ConcurrentCallbacksPublishMonotonicQueueSequences(supportedRuntime);
        IngressContainsOriginalAndQueueFailures(supportedRuntime);
        BattleMenuWorkerStaysWithinTranslatedReadBudget(supportedRuntime);
        WorkerProducesFramesAndRetryableNativeMenuSpeech(supportedRuntime);
        WorkerFailsClosedForUnsupportedUnreadableAndTornState(supportedRuntime);
        StaleWorkerReadDoesNotRearmUnchangedMenuSpeech(supportedRuntime);
        HookSetOwnsTheExactBattleCallbackCohort();
    }

    private static void BattleMenuWorkerStaysWithinTranslatedReadBudget(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        var memory = new CountingNativeMemoryReader(fixture.Battle.Native);
        var coordinator = new Steam2026BattleMenuCoordinator(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            memory,
            CreateResolvers(commandId => commandId == 1 ? "Attack" : null));
        memory.Reset();

        var update = coordinator.Observe(CreateIngressSnapshot(1, 1));

        Equal(RuntimeDomainUpdateKind.Present, update.Kind, "bounded battle-menu frame update");
        Equal(
            true,
            memory.ReadOperations <= 200,
            $"battle-menu worker translated read budget: {memory.ReadOperations}");
        Equal(
            true,
            memory.QueryOperations <= 140,
            $"battle-menu worker translated query budget: {memory.QueryOperations}");
    }

    private static void FourPerFrameBattleUpdateStaysWithinBoundedNativeReadBudget(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        const byte crossingEventIndex = 54;
        var crossingEventAddress = BattleStateReader.AddressAnimationEventQueue
                                   + crossingEventIndex * BattleStateReader.AnimationEventSize;
        fixture.Battle.Write(
            (uint)crossingEventAddress,
            new byte[BattleStateReader.AnimationEventSize]);
        fixture.Battle.WriteByte(
            BattleStateReader.AddressAnimationEventIndex,
            crossingEventIndex);
        fixture.Battle.WriteByte(
            crossingEventAddress + BattleStateReader.AnimationEventAttackerOffset,
            4);
        fixture.Battle.WriteByte(
            crossingEventAddress + BattleStateReader.AnimationEventKindOffset,
            BattleStateReader.ActionAnimationEventKind);
        fixture.Battle.WriteByte(
            crossingEventAddress + BattleStateReader.AnimationEventCommandOffset,
            BattleStateReader.EnemyActionCommandId);
        fixture.Battle.WriteUInt16(
            crossingEventAddress + BattleStateReader.AnimationEventActionOffset,
            2);
        var memory = new CountingNativeMemoryReader(fixture.Battle.Native);
        var contract = new Steam2026BattleRendererCallbackContract(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            ModuleImageSize,
            memory);
        var queue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(8);
        var originalCalls = 0;
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            contract,
            () => { },
            () => originalCalls++,
            () => { },
            () => { },
            () => { },
            () => { },
            () => Timestamp,
            queue);
        contract.ActivateHookLease(() => true);
        memory.Reset();

        for (var index = 0; index < 4; index++)
        {
            ingress.OnBattleUpdate();
        }

        Equal(4, originalCalls, "four translated battle update originals invoked");
        for (var index = 0; index < 4; index++)
        {
            Equal(true, queue.TryDequeue(out var snapshot), $"battle update {index} published");
            Equal(true, snapshot.EnemyActionBefore.Raw.IsCoherent, $"battle update {index} pre-action coherent");
            Equal(true, snapshot.EnemyActionAfter.Raw.IsCoherent, $"battle update {index} post-action coherent");
            Equal(crossingEventIndex, snapshot.EnemyActionBefore.Raw.EventIndex, $"battle update {index} pre-action event index");
            Equal(crossingEventIndex, snapshot.EnemyActionAfter.Raw.EventIndex, $"battle update {index} post-action event index");
            Equal((byte)4, snapshot.EnemyActionBefore.Raw.AttackerActorIndex, $"battle update {index} pre-action attacker");
            Equal((ushort)2, snapshot.EnemyActionAfter.Raw.SceneAttackIndex, $"battle update {index} post-action scene action");
        }

        Equal(false, queue.TryDequeue(out _), "exactly four battle updates published");
        Equal(
            true,
            memory.ReadOperations <= 100,
            $"four-update native read budget is bounded: {memory.ReadOperations}");
        Equal(
            true,
            memory.QueryOperations <= 16,
            $"four-update native query budget is bounded: {memory.QueryOperations}");
        contract.RevokeHookLease();
    }

    private static void ActiveLeaseCohortDisableFailsTheRateLimitedHealthProbe(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        var contract = CreateExactContract(fixture, supportedRuntime);
        var cohortEnabled = true;
        contract.ActivateHookLease(_ => cohortEnabled);

        Equal(true, ProbeActiveLeaseHealth(contract, 0), "initial enabled battle cohort");
        cohortEnabled = false;
        Equal(
            true,
            ProbeActiveLeaseHealth(contract, 999),
            "disabled battle cohort waits for the next health interval");
        Equal(
            false,
            ProbeActiveLeaseHealth(contract, 1000),
            "disabled battle cohort poisons the active lease");

        cohortEnabled = true;
        Equal(
            false,
            ProbeActiveLeaseHealth(contract, 2000),
            "poisoned battle lease health remains sticky");
        contract.RevokeHookLease();
    }

    private static void ResultsUpdateStaysWithinBoundedNativeReadBudget(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        fixture.Battle.SwitchToResultsModule();
        var memory = new CountingNativeMemoryReader(fixture.Battle.Native);
        var contract = new Steam2026BattleRendererCallbackContract(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            ModuleImageSize,
            memory);
        var queue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(2);
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            contract,
            () => { },
            () => { },
            () => { },
            () => { },
            () => { },
            () => { },
            () => Timestamp,
            queue);
        memory.Reset();

        ingress.OnResultsUpdate();

        Equal(true, queue.TryDequeue(out var snapshot), "results boundary published");
        Equal(true, snapshot.ResultsBefore.WasCaptured, "pre-original results boundary captured");
        Equal(true, snapshot.ResultsAfter.WasCaptured, "post-original results boundary captured");
        Equal(
            true,
            memory.ReadOperations <= 450,
            $"results native read budget is bounded: {memory.ReadOperations}");
        Equal(
            true,
            memory.QueryOperations <= 300,
            $"results native query budget is bounded: {memory.QueryOperations}");
    }

    private static void ActiveLeaseAllowsBattlePagesToMapAfterStartup(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        var contract = CreateExactContract(fixture, supportedRuntime);
        Equal(
            true,
            contract.TryValidateCaptureIdentity(
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                out var updateIdentity),
            "battle update identity before lifecycle page transition");

        var actionPageAddresses = new[]
            {
                (uint)BattleStateReader.AddressAnimationEventIndex,
                (uint)BattleStateReader.AddressAnimationEventQueue,
                checked(
                    (uint)BattleStateReader.AddressAnimationEventQueue
                    + (uint)(BattleStateReader.AnimationEventCount
                             * BattleStateReader.AnimationEventSize)
                    - 1)
            }
            .DistinctBy(address => address >> 12)
            .Select(address => (
                Address: address,
                HostPage: fixture.Battle.GetHostAddress(address)
                          - (address & (TranslatedX86AddressSpace.PageSize - 1))))
            .ToArray();
        foreach (var page in actionPageAddresses)
        {
            fixture.Battle.UnmapGuestPage(page.Address);
        }

        contract.ActivateHookLease(_ => true);
        Equal(
            true,
            ProbeActiveLeaseHealth(contract, 0),
            "battle hook lease is healthy before battle-only guest pages exist");
        Equal(
            false,
            contract.TryCaptureRawEnemyAction(updateIdentity, out _),
            "unmapped battle action state produces no guessed capture");

        foreach (var page in actionPageAddresses)
        {
            fixture.Battle.MapGuestPage(page.Address, page.HostPage);
        }

        Equal(
            true,
            contract.TryCaptureRawEnemyAction(updateIdentity, out var captured),
            "battle action capture starts when the battle module maps its pages");
        Equal(true, captured.Raw.IsCoherent, "late-mapped battle action is coherent");
        Equal((byte)0, captured.Raw.EventIndex, "late-mapped battle action event index");

        foreach (var page in actionPageAddresses)
        {
            fixture.Battle.UnmapGuestPage(page.Address);
        }

        Equal(
            false,
            contract.TryCaptureRawEnemyAction(updateIdentity, out _),
            "battle exit unmaps action state without using stale host pages");
        foreach (var page in actionPageAddresses)
        {
            fixture.Battle.MapGuestPage(page.Address, page.HostPage);
        }

        Equal(
            true,
            contract.TryCaptureRawEnemyAction(updateIdentity, out var recaptured),
            "later battle remaps action state without reinstalling hooks");
        Equal(true, recaptured.Raw.IsCoherent, "remapped later-battle action is coherent");
        Equal(
            true,
            ProbeActiveLeaseHealth(contract, 1000),
            "normal battle page mapping does not poison hook identity");

        contract.RevokeHookLease();
        Equal(
            false,
            ProbeActiveLeaseHealth(contract, 2000),
            "unexpected revoked battle lease is structurally unhealthy");
    }

    private static void RawCaptureRejectsAndRecoversFromMidReadPageRemap(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        var indexAddress = (uint)BattleStateReader.AddressAnimationEventIndex;
        var indexHostPage = fixture.Battle.GetHostAddress(indexAddress)
                            - (indexAddress & (TranslatedX86AddressSpace.PageSize - 1));
        var remappingMemory = new RemappingNativeMemoryReader(
            fixture.Battle.Native,
            fixture.Battle.GetPageTableEntryAddress(indexAddress),
            triggerRead: 2,
            () => fixture.Battle.UnmapGuestPage(indexAddress));
        var contract = new Steam2026BattleRendererCallbackContract(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            ModuleImageSize,
            remappingMemory);
        Equal(
            true,
            contract.TryValidateCaptureIdentity(
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                out var updateIdentity),
            "battle update identity before torn page read");
        contract.ActivateHookLease(_ => true);

        Equal(
            false,
            contract.TryCaptureRawEnemyAction(updateIdentity, out _),
            "page remap between guest data and page-entry bookend is rejected");

        fixture.Battle.MapGuestPage(indexAddress, indexHostPage);
        Equal(
            true,
            contract.TryCaptureRawEnemyAction(updateIdentity, out var recovered),
            "raw capture recovers after a coherent page remap");
        Equal(true, recovered.Raw.IsCoherent, "recovered action capture is coherent");
        Equal(
            true,
            ProbeActiveLeaseHealth(contract, 0),
            "transient guest-page remap does not poison hook identity");
        contract.RevokeHookLease();
    }

    private static void HealthyNoActionCaptureDoesNotPoisonTheActiveLease(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        var contract = CreateExactContract(fixture, supportedRuntime);
        Equal(
            true,
            contract.TryValidateCaptureIdentity(
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                out var identity),
            "healthy no-action battle identity");
        contract.ActivateHookLease(_ => true);
        fixture.Battle.WriteByte(
            BattleStateReader.AddressAnimationEventIndex,
            byte.MaxValue);

        Equal(
            true,
            contract.TryCaptureRawEnemyAction(identity, out var snapshot),
            "inactive battle action state remains a successful raw capture");
        Equal(true, snapshot.WasCaptured, "inactive battle action was captured");
        Equal(true, snapshot.Raw.IsCoherent, "inactive battle action is coherent");
        Equal(false, snapshot.Raw.IsActionCandidate, "inactive battle action is not speakable");
        Equal(
            true,
            ProbeActiveLeaseHealth(contract, 0),
            "inactive battle action does not poison lease health");
        contract.RevokeHookLease();
    }

    private static void HookSetPollsLeaseHealthOutsideTheNativeCallbackPath()
    {
        var prototypeRoot = FindPrototypeRoot();
        var projectRoot = Path.Combine(
            prototypeRoot,
            "reloaded",
            "Ff7.Accessibility.Steam2026X64");
        var hookSetSource = File.ReadAllText(Path.Combine(
            projectRoot,
            "Runtime",
            "Battle",
            "Steam2026BattleRendererHookSet.cs"));
        var ingressSource = File.ReadAllText(Path.Combine(
            projectRoot,
            "Runtime",
            "Battle",
            "Steam2026BattleRendererDetourIngressCoordinator.cs"));

        Equal(
            true,
            hookSetSource.Contains(
                "IsActiveHookLeaseHealthy(Environment.TickCount64)",
                StringComparison.Ordinal),
            "battle hook owner polls the rate-limited lease health probe");
        Equal(
            false,
            ingressSource.Contains("IsActiveHookLeaseHealthy", StringComparison.Ordinal),
            "battle native callback ingress performs no full lease-health validation");
    }

    private static void NativeBattleCallbacksExcludeSemanticSpeechDispatch()
    {
        var prototypeRoot = FindPrototypeRoot();
        var projectRoot = Path.Combine(
            prototypeRoot,
            "reloaded",
            "Ff7.Accessibility.Steam2026X64");
        var hookSetSource = File.ReadAllText(Path.Combine(
            projectRoot,
            "Runtime",
            "Battle",
            "Steam2026BattleRendererHookSet.cs"));
        foreach (var forbidden in new[]
                 {
                     "ImmediateBattleIngressQueue",
                     "immediateDispatch",
                     "DispatchesImmediately"
                 })
        {
            Equal(
                false,
                hookSetSource.Contains(forbidden, StringComparison.Ordinal),
                $"native battle hook excludes callback-thread semantic dispatch surface {forbidden}");
        }

        var sessionSource = File.ReadAllText(Path.Combine(
            projectRoot,
            "Runtime",
            "Steam2026ResearchSession.cs"));
        Equal(
            false,
            sessionSource.Contains(
                "Immediate native battle dispatch",
                StringComparison.Ordinal),
            "research session never decodes or speaks battle state on the game callback thread");
    }

    private static void BattleUpdateIngressCapturesEnemyActionBeforeAndAfterOriginal(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        var queue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(2);
        var originalCalls = 0;
        var contract = CreateExactContract(fixture, supportedRuntime);
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            contract,
            () => { },
            () =>
            {
                originalCalls++;
                const byte nextEventIndex = 1;
                var nextEvent = BattleStateReader.AddressAnimationEventQueue
                                + nextEventIndex * BattleStateReader.AnimationEventSize;
                fixture.Battle.Write(
                    (uint)nextEvent,
                    new byte[BattleStateReader.AnimationEventSize]);
                fixture.Battle.WriteByte(
                    BattleStateReader.AddressAnimationEventIndex,
                    nextEventIndex);
                fixture.Battle.WriteByte(
                    nextEvent + BattleStateReader.AnimationEventAttackerOffset,
                    4);
                fixture.Battle.WriteByte(
                    nextEvent + BattleStateReader.AnimationEventKindOffset,
                    BattleStateReader.ActionAnimationEventKind);
                fixture.Battle.WriteByte(
                    nextEvent + BattleStateReader.AnimationEventCommandOffset,
                    BattleStateReader.EnemyActionCommandId);
                fixture.Battle.WriteUInt16(
                    nextEvent + BattleStateReader.AnimationEventActionOffset,
                    2);
            },
            () => { },
            () => { },
            () => { },
            () => { },
            () => Timestamp,
            queue);
        contract.ActivateHookLease(() => true);

        ingress.OnBattleUpdate();

        Equal(1, originalCalls, "battle update original invoked exactly once");
        Equal(true, queue.TryDequeue(out var snapshot), "battle update ingress published");
        Equal(true, snapshot.EnemyActionBefore.WasCaptured, "pre-update action captured");
        Equal(true, snapshot.EnemyActionBefore.Raw.IsCoherent, "pre-update raw action coherent");
        Equal((byte)0, snapshot.EnemyActionBefore.Raw.EventIndex, "pre-update event index");
        Equal((byte)4, snapshot.EnemyActionBefore.Raw.AttackerActorIndex, "pre-update attacker index");
        Equal((ushort)2, snapshot.EnemyActionBefore.Raw.SceneAttackIndex, "pre-update scene action");
        Equal(false, snapshot.EnemyActionBefore.Action.IsValid, "pre-update callback defers semantic action decoding");
        Equal(true, snapshot.EnemyActionAfter.WasCaptured, "post-update action captured");
        Equal(true, snapshot.EnemyActionAfter.Raw.IsCoherent, "post-update raw action coherent");
        Equal((byte)1, snapshot.EnemyActionAfter.Raw.EventIndex, "post-update event index");
        Equal((ushort)2, snapshot.EnemyActionAfter.Raw.SceneAttackIndex, "post-update scene action");
        Equal(false, snapshot.EnemyActionAfter.Action.IsValid, "post-update callback defers semantic action decoding");

        var workerReader = fixture.Battle.CreateTranslatedReader();
        Equal(
            true,
            workerReader.TryResolveCapturedEnemyAction(
                snapshot.EnemyActionBefore,
                out var resolvedBefore,
                out var attackerBefore),
            "worker resolves pre-update raw action");
        Equal("Rifle", resolvedBefore.ActionName, "worker resolves pre-update action name");
        Equal("Grunt", attackerBefore.Name, "worker resolves pre-update attacker");
        Equal(
            true,
            workerReader.TryResolveCapturedEnemyAction(
                snapshot.EnemyActionAfter,
                out var resolvedAfter,
                out var attackerAfter),
            "worker resolves post-update raw action");
        Equal("Rifle", resolvedAfter.ActionName, "worker resolves post-update action name");
        Equal("Grunt", attackerAfter.Name, "worker resolves post-update attacker");
        contract.RevokeHookLease();
    }

    private static void CatalogAndDelegatesExposeTheExactBattleCohort()
    {
        Equal(
            true,
            Enum.TryParse<Steam2026BattleRendererCallbackKind>(
                "ActionTextCommit",
                out _),
            "battle callback cohort includes the native action-text commit");

        var expected = new Dictionary<Steam2026BattleRendererCallbackKind, (
            uint LegacyVirtualAddress,
            ulong MappingRecordRva,
            ulong HostRva,
            string Prefix,
            Type DelegateType)>
        {
            [Steam2026BattleRendererCallbackKind.MenuRenderer] = (
                0x006D797C,
                0x016F47B0,
                0x010ACA10,
                "48895C2408574883EC208B0DA8CBF800",
                typeof(TranslatedBattleRendererCallbackOriginal)),
            [Steam2026BattleRendererCallbackKind.BattleUpdate] = (
                0x006CE8B3,
                0x016F4580,
                0x0107CF00,
                "48895C2408574883EC20B908000000E8",
                typeof(TranslatedBattleUpdateCallbackOriginal)),
            [Steam2026BattleRendererCallbackKind.TextActivation] = (
                0x006D721C,
                0x016F4790,
                0x010AAE10,
                "40534883EC208B0DACE7F8008B1DAAE7",
                typeof(TranslatedBattleTextActivationCallbackOriginal)),
            [Steam2026BattleRendererCallbackKind.ResultsUpdate] = (
                0x006C9543,
                0x016F3F50,
                0x010623D0,
                "48895C24084889742410574883EC208B",
                typeof(TranslatedBattleResultsUpdateCallbackOriginal)),
            [Steam2026BattleRendererCallbackKind.DamageDisplay] = (
                0x005BB410,
                0x016E5910,
                0x009D7970,
                "40574883EC308B0D4C1C660183C1FC48",
                typeof(TranslatedBattleDamageDisplayCallbackOriginal)),
            [Steam2026BattleRendererCallbackKind.ActionTextCommit] = (
                0x006D71FA,
                0x016F4780,
                0x010AAD30,
                "48895C2408574883EC208B0D88E8F800",
                typeof(TranslatedBattleActionTextCommitCallbackOriginal))
        };

        Equal(
            expected.Count,
            Enum.GetValues<Steam2026BattleRendererCallbackKind>().Length,
            "battle callback cohort kind count");
        Equal(
            expected.Count,
            expected.Values.Select(value => value.HostRva).Distinct().Count(),
            "battle callback cohort host targets are unique");

        foreach (var (kind, identity) in expected)
        {
            var metadata = Steam2026BattleRendererCallbackCatalog.GetMetadata(kind);
            Equal(kind, metadata.Kind, $"{kind} callback kind");
            Equal(
                identity.LegacyVirtualAddress,
                metadata.FunctionMap.LegacyVirtualAddress,
                $"{kind} legacy VA");
            Equal(
                identity.MappingRecordRva,
                metadata.FunctionMap.MappingRecordRva,
                $"{kind} map-record RVA");
            Equal(identity.HostRva, metadata.FunctionMap.HostRva, $"{kind} host RVA");
            Equal(
                identity.Prefix,
                metadata.FunctionMap.ExpectedPrefixHex,
                $"{kind} exact translated prefix");
            Equal(
                TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments,
                metadata.HostAbi,
                $"{kind} translated host ABI");

            var unmanaged = identity.DelegateType
                .GetCustomAttribute<UnmanagedFunctionPointerAttribute>()
                ?? throw new InvalidOperationException(
                    $"{kind} delegate lacks unmanaged ABI metadata.");
            Equal(CallingConvention.Winapi, unmanaged.CallingConvention, $"{kind} Windows ABI");
            var invoke = identity.DelegateType.GetMethod("Invoke")
                         ?? throw new InvalidOperationException($"{kind} delegate lacks Invoke.");
            Equal(typeof(void), invoke.ReturnType, $"{kind} delegate return type");
            Equal(0, invoke.GetParameters().Length, $"{kind} delegate parameter count");
        }
    }

    private static void ContractCapturesTheStableGuestRendererState(
        Steam2026FingerprintResult supportedRuntime,
        Steam2026FingerprintResult unsupportedRuntime)
    {
        var unsupportedFixture = BattleRendererIngressFixture.Create();
        Equal(
            true,
            Throws<ArgumentException>(() => _ = new Steam2026BattleRendererCallbackContract(
                unsupportedRuntime,
                BattleObservationFixture.ModuleBase,
                ModuleImageSize,
                unsupportedFixture.Battle.Native)),
            "battle renderer contract rejects unsupported fingerprint");

        var fixture = BattleRendererIngressFixture.Create();
        fixture.WriteRendererState(1);
        var contract = CreateExactContract(fixture, supportedRuntime);
        Equal(
            true,
            contract.TryValidateCaptureIdentity(out var identity),
            "battle renderer pristine callback identity");
        Equal(
            true,
            contract.TryCaptureRendererState(identity, out var rendererState),
            "battle renderer checked guest argument");
        Equal((short)1, rendererState, "battle renderer guest ESP plus eight argument");

        fixture.Battle.Native.Write(identity.HostAddress, [0xE9]);
        contract.ActivateHookLease(() => true);
        fixture.WriteRendererState(0x18);
        Equal(
            true,
            contract.TryCaptureRendererState(identity, out rendererState),
            "battle renderer capture survives owning patched hook lease");
        Equal((short)0x18, rendererState, "battle renderer signed low sixteen argument");

        contract.RevokeHookLease();
        Equal(
            false,
            contract.TryCaptureRendererState(identity, out _),
            "battle renderer patched prefix rejected without hook lease");

        var tornFixture = BattleRendererIngressFixture.Create();
        tornFixture.WriteRendererState(1);
        var espAddress = BattleObservationFixture.ModuleBase
                         + TranslatedX86CallFrameReader.EspRva;
        var tearing = new TearingNativeMemoryReader(
            tornFixture.Battle.Native,
            espAddress,
            triggerRead: 2,
            () => tornFixture.Battle.Native.Write(
                espAddress,
                BitConverter.GetBytes(BattleRendererIngressFixture.GuestEsp + 0x1000)));
        var tornContract = new Steam2026BattleRendererCallbackContract(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            ModuleImageSize,
            tearing);
        Equal(
            true,
            tornContract.TryValidateCaptureIdentity(out var tornIdentity),
            "torn battle renderer fixture identity");
        Equal(
            false,
            tornContract.TryCaptureRendererState(tornIdentity, out _),
            "torn virtual ESP rejects battle renderer argument");
    }

    private static void IngressCapturesBeforeOriginalAndInvokesItExactlyOnce(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        fixture.WriteRendererState(1);
        var contract = CreateExactContract(fixture, supportedRuntime);
        var queue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(4);
        var originalCalls = 0;
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            contract,
            () =>
            {
                originalCalls++;
                fixture.WriteRendererState(5);
            },
            () => Timestamp,
            queue);

        ingress.OnMenuRenderer();
        Equal(1, originalCalls, "battle renderer original invoked exactly once");
        Equal(
            true,
            queue.TryDequeue(out var snapshot),
            "battle renderer callback published after original");
        Equal(1L, snapshot.Sequence, "battle renderer callback sequence");
        Equal(Timestamp, snapshot.TimestampUtc, "battle renderer callback UTC timestamp");
        Equal(
            (short)1,
            snapshot.RendererState,
            "battle renderer callback preserves pre-original guest argument");
        Equal(false, queue.TryDequeue(out _), "battle renderer publishes one callback");

        fixture.WriteRendererState(0);
        ingress.OnMenuRenderer();
        Equal(2, originalCalls, "unsupported renderer state still calls original once");
        Equal(
            false,
            queue.TryDequeue(out _),
            "unsupported renderer state fails closed without publication");

        var unreadableFixture = BattleRendererIngressFixture.Create();
        unreadableFixture.WriteRendererState(1);
        var unreadableContract = CreateExactContract(unreadableFixture, supportedRuntime);
        var unreadableQueue =
            new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(2);
        var unreadableOriginalCalls = 0;
        using var unreadableIngress = new Steam2026BattleRendererDetourIngressCoordinator(
            unreadableContract,
            () => unreadableOriginalCalls++,
            () => Timestamp,
            unreadableQueue);
        unreadableFixture.Battle.UnmapGuestPage(BattleRendererIngressFixture.GuestEsp);
        unreadableIngress.OnMenuRenderer();
        Equal(
            1,
            unreadableOriginalCalls,
            "unreadable battle renderer stack still calls original once");
        Equal(
            false,
            unreadableQueue.TryDequeue(out _),
            "unreadable battle renderer stack publishes nothing");
        Equal(
            false,
            unreadableIngress.IsFatallyDegraded,
            "one unreadable guest frame remains a silent transient miss");
    }

    private static void IngressContainsOriginalAndQueueFailures(
        Steam2026FingerprintResult supportedRuntime)
    {
        var originalFixture = BattleRendererIngressFixture.Create();
        originalFixture.WriteRendererState(1);
        var originalQueue =
            new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(2);
        var originalCalls = 0;
        using var originalFailure = new Steam2026BattleRendererDetourIngressCoordinator(
            CreateExactContract(originalFixture, supportedRuntime),
            () =>
            {
                originalCalls++;
                throw new InvalidOperationException("translated renderer failure");
            },
            () => Timestamp,
            originalQueue);
        originalFailure.OnMenuRenderer();
        originalFailure.OnMenuRenderer();
        Equal(
            2,
            originalCalls,
            "degraded battle ingress keeps every native original callable");
        Equal(
            true,
            originalFailure.IsFatallyDegraded,
            "battle renderer original failure permanently degrades ingress");
        Equal(
            false,
            originalQueue.TryDequeue(out _),
            "failed battle renderer original publishes nothing");

        var queueFixture = BattleRendererIngressFixture.Create();
        queueFixture.WriteRendererState(1);
        var rejectingQueue = new RejectingIngressQueue();
        var queueOriginalCalls = 0;
        using var queueFailure = new Steam2026BattleRendererDetourIngressCoordinator(
            CreateExactContract(queueFixture, supportedRuntime),
            () => queueOriginalCalls++,
            () => Timestamp,
            rejectingQueue);
        queueFailure.OnMenuRenderer();
        Equal(1, queueOriginalCalls, "battle original runs before rejected publication");
        Equal(1, rejectingQueue.Attempts, "battle snapshot gets one queue attempt");
        Equal(
            true,
            queueFailure.IsFatallyDegraded,
            "battle renderer queue rejection permanently degrades ingress");
    }

    private static void DamageIngressCopiesThePopupBeforeTheOriginalRetiresIt(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        const int effectIndex = 5;
        var record = BattleDamagePopupReader.AddressEffectData
                     + effectIndex * BattleDamagePopupReader.EffectRecordSize;
        fixture.Battle.WriteByte(record + BattleDamagePopupReader.StateOffset, 0);
        fixture.Battle.WriteUInt16(record + BattleDamagePopupReader.ValueOffset, 12);
        fixture.Battle.WriteInt32(record + BattleDamagePopupReader.TargetActorOffset, 0);
        fixture.Battle.WriteInt32(record + BattleDamagePopupReader.FlagsOffset, 0);

        var originalCalls = 0;
        var queue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(2);
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            CreateExactContract(fixture, supportedRuntime),
            () => { },
            () => { },
            () => { },
            () => { },
            () =>
            {
                originalCalls++;
                fixture.Battle.WriteByte(record + BattleDamagePopupReader.StateOffset, 1);
                fixture.Battle.WriteUInt16(record + BattleDamagePopupReader.ValueOffset, 99);
            },
            () => { },
            () => Timestamp,
            queue);

        ingress.OnDamageDisplay();

        Equal(1, originalCalls, "damage original invoked exactly once");
        Equal(true, queue.TryDequeue(out var ingressSnapshot), "damage ingress published");
        Equal(true, ingressSnapshot.CapturedDamage.IsValid, "captured pre-original popup is valid");
        Equal(effectIndex, ingressSnapshot.CapturedDamage.EffectIndex, "captured effect index");
        Equal(0, ingressSnapshot.CapturedDamage.TargetActor, "captured damage target");
        Equal(12, ingressSnapshot.CapturedDamage.Value, "captured pre-original damage value");
    }

    private static void LifecycleIngressCapturesVictoryAndResultsAroundOriginal(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        fixture.Battle.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 0);
        var queue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(4);
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            CreateExactContract(fixture, supportedRuntime),
            () => { },
            () => fixture.Battle.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 1),
            () => { },
            () =>
            {
                fixture.Battle.WriteInt32(BattleResultsReader.AddressResultsState, 2);
                fixture.Battle.WriteInt32(BattleResultsReader.AddressGil, 0);
            },
            () => { },
            () => { },
            () => Timestamp,
            queue);

        ingress.OnBattleUpdate();
        fixture.Battle.SwitchToResultsModule();
        ingress.OnResultsUpdate();
        fixture.Battle.WriteByte(BattleStateReader.AddressCurrentModule, 1);

        Equal(true, queue.TryDequeue(out var battleUpdate), "captured lifecycle battle update");
        var victory = GetProperty(battleUpdate, "VictoryAfter");
        Equal(true, GetProperty<bool>(victory, "WasCaptured"), "post-original victory captured");
        Equal(true, GetProperty<bool>(victory, "IsVictory"), "post-original victory outcome retained");

        Equal(true, queue.TryDequeue(out var resultsUpdate), "captured lifecycle results update");
        var before = GetProperty(resultsUpdate, "ResultsBefore");
        var after = GetProperty(resultsUpdate, "ResultsAfter");
        Equal(true, GetProperty<bool>(before, "WasCaptured"), "pre-original results captured");
        Equal(96, GetProperty<int>(before, "Gil"), "pre-original gil retained");
        Equal(0, GetProperty<int>(before, "State"), "pre-original results page retained");
        Equal(true, GetProperty<bool>(after, "WasCaptured"), "post-original results captured");
        Equal(0, GetProperty<int>(after, "Gil"), "post-original gil captured");
        Equal(2, GetProperty<int>(after, "State"), "post-original results page captured");
    }

    private static void ActionTextIngressCopiesTheVisibleCommitBeforeOriginal(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        fixture.WriteActionTextCommitFrame(0, 1, 0, 30, effectIndex: 3);
        var originalCalls = 0;
        var clockValue = Timestamp;
        var queue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(2);
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            CreateExactContract(fixture, supportedRuntime),
            () => { },
            () => { },
            () => { },
            () => { },
            () => { },
            () =>
            {
                originalCalls++;
                clockValue = Timestamp.AddSeconds(1);
                fixture.WriteActionTextCommitFrame(4, 0x20, 2, 29, effectIndex: 3);
            },
            () => clockValue,
            queue);

        ingress.OnActionTextCommit();

        Equal(1, originalCalls, "action-text original invoked exactly once");
        Equal(true, queue.TryDequeue(out var ingressSnapshot), "action-text ingress published");
        Equal(Timestamp, ingressSnapshot.TimestampUtc, "action-text timestamp captured at callback entry");
        Equal(
            Steam2026BattleRendererCallbackKind.ActionTextCommit,
            ingressSnapshot.Kind,
            "action-text ingress kind");
        Equal(true, ingressSnapshot.CapturedAction.IsValid, "captured action-text commit is valid");
        Equal((ushort)3, ingressSnapshot.CapturedAction.EffectIndex, "captured action effect slot");
        Equal((byte)0, ingressSnapshot.CapturedAction.ActorIndex, "captured pre-original actor");
        Equal((byte)1, ingressSnapshot.CapturedAction.CommandId, "captured pre-original command");
        Equal((ushort)0, ingressSnapshot.CapturedAction.ActionId, "captured pre-original action id");
        Equal((short)30, ingressSnapshot.CapturedAction.RemainingFrames, "captured pre-original frames");

        fixture.WriteActionTextCommitFrame(3, 1, 0, 30, effectIndex: 4);
        ingress.OnActionTextCommit();
        Equal(2, originalCalls, "invalid actor still invokes action-text original");
        Equal(false, queue.TryDequeue(out _), "non-visible actor slot publishes no action commit");
    }

    private static void IngressPublishesTheSixCallbackCohortWithCheckedGuestArguments(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        fixture.WriteRendererState(1);
        var queue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(8);
        var calls = new int[6];
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            CreateExactContract(fixture, supportedRuntime),
            () =>
            {
                calls[0]++;
                fixture.WriteRendererState(5);
            },
            () => calls[1]++,
            () =>
            {
                calls[2]++;
                fixture.WriteTextBufferIndex(-1);
            },
            () => calls[3]++,
            () => calls[4]++,
            () => calls[5]++,
            () => Timestamp,
            queue);

        ingress.OnMenuRenderer();
        ingress.OnBattleUpdate();
        fixture.WriteTextBufferIndex(7);
        ingress.OnTextActivation();
        ingress.OnResultsUpdate();
        ingress.OnDamageDisplay();
        fixture.WriteActionTextCommitFrame(0, 1, 0, 30);
        ingress.OnActionTextCommit();

        Equal(true, calls.SequenceEqual([1, 1, 1, 1, 1, 1]), "every native original runs exactly once");
        var expected = new[]
        {
            (Steam2026BattleRendererCallbackKind.MenuRenderer, (short)1),
            (Steam2026BattleRendererCallbackKind.BattleUpdate, (short)0),
            (Steam2026BattleRendererCallbackKind.TextActivation, (short)7),
            (Steam2026BattleRendererCallbackKind.ResultsUpdate, (short)0),
            (Steam2026BattleRendererCallbackKind.DamageDisplay, (short)0),
            (Steam2026BattleRendererCallbackKind.ActionTextCommit, (short)0)
        };
        for (var index = 0; index < expected.Length; index++)
        {
            Equal(true, queue.TryDequeue(out var snapshot), $"callback {index} publication");
            Equal(index + 1L, snapshot.Sequence, $"callback {index} global sequence");
            Equal(expected[index].Item1, snapshot.Kind, $"callback {index} kind");
            Equal(expected[index].Item2, snapshot.GuestValue, $"callback {index} guest value");
        }

        Equal(false, queue.TryDequeue(out _), "callback cohort publishes one record per original");
    }

    private static void ConcurrentCallbacksPublishMonotonicQueueSequences(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        fixture.WriteRendererState(1);
        var queue = new FirstPublicationDelayingIngressQueue(capacity: 4);
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            CreateExactContract(fixture, supportedRuntime),
            () => { },
            () => { },
            () => { },
            () => { },
            () => { },
            () => { },
            () => Timestamp,
            queue);

        var firstCallback = Task.Run(ingress.OnResultsUpdate);
        try
        {
            Equal(
                true,
                queue.WaitUntilFirstPublication(TimeSpan.FromSeconds(5)),
                "first concurrent callback reaches publication");
            ingress.OnMenuRenderer();
        }
        finally
        {
            queue.ReleaseFirstPublication();
        }

        Equal(
            true,
            firstCallback.Wait(TimeSpan.FromSeconds(5)),
            "first concurrent callback completes");
        Equal(true, queue.TryDequeue(out var first), "first concurrent callback dequeued");
        Equal(true, queue.TryDequeue(out var second), "second concurrent callback dequeued");
        Equal(1L, first.Sequence, "first queue reservation receives sequence one");
        Equal(2L, second.Sequence, "second queue reservation receives sequence two");
        Equal(
            Steam2026BattleRendererCallbackKind.MenuRenderer,
            first.Kind,
            "callback that reserves first remains first");
        Equal(
            Steam2026BattleRendererCallbackKind.ResultsUpdate,
            second.Kind,
            "delayed callback reserves and follows second");
    }

    private static void WorkerProducesFramesAndRetryableNativeMenuSpeech(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        var commandResolverCalls = 0;
        var coordinator = new Steam2026BattleMenuCoordinator(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            fixture.Battle.Native,
            CreateResolvers(commandId =>
            {
                commandResolverCalls++;
                return commandId switch
                {
                    1 => "Attack",
                    2 => "Magic",
                    _ => null
                };
            }));

        var firstUpdate = coordinator.Observe(CreateIngressSnapshot(1, 1));
        Equal(RuntimeDomainUpdateKind.Present, firstUpdate.Kind, "first battle frame update");
        Equal(true, firstUpdate.Value!.IsActive, "first battle frame active");
        Equal(1, firstUpdate.Value.Revision, "first battle frame revision");
        Equal(1, firstUpdate.Value.CommandId, "first native battle command id");
        Equal(true, commandResolverCalls > 0, "battle reader loads command names through supplied resolver");

        var speechAttempts = 0;
        bool TrySpeak(string text)
        {
            speechAttempts++;
            Equal("Cloud. Attack", text, "first native battle menu speech");
            if (speechAttempts == 1)
            {
                throw new InvalidOperationException("Prism unavailable");
            }

            return true;
        }

        Equal(
            false,
            coordinator.TrySpeakPending(TrySpeak, out _),
            "thrown battle menu output remains pending");
        Equal(
            true,
            coordinator.TrySpeakPending(TrySpeak, out var spoken),
            "battle menu speech retries until accepted");
        Equal("Cloud. Attack", spoken, "accepted first battle menu speech");
        Equal(2, speechAttempts, "battle menu speech attempt count");

        var duplicate = coordinator.Observe(CreateIngressSnapshot(2, 1));
        Equal(RuntimeDomainUpdateKind.Unchanged, duplicate.Kind, "unchanged battle frame deduplicated");
        Equal(
            false,
            coordinator.TrySpeakPending(_ => true, out _),
            "unchanged native battle selection stays silent");

        fixture.Battle.WriteByte(BattleStateReader.AddressRootCommandRecords, 2);
        var changed = coordinator.Observe(CreateIngressSnapshot(3, 1));
        Equal(RuntimeDomainUpdateKind.Present, changed.Kind, "changed battle frame update");
        Equal(2, changed.Value!.Revision, "changed battle frame revision");
        Equal(2, changed.Value.CommandId, "changed native battle command id");
        Equal(
            true,
            coordinator.TrySpeakPending(_ => true, out spoken),
            "changed native battle selection speaks");
        Equal("Magic", spoken, "same actor is not redundantly prefixed");

        coordinator.Reset();
        fixture.Battle.WriteByte(BattleStateReader.AddressRootCommandRecords, 1);
        var reset = coordinator.Observe(CreateIngressSnapshot(1, 1));
        Equal(RuntimeDomainUpdateKind.Present, reset.Kind, "reset battle frame update");
        Equal(1, reset.Value!.Revision, "reset battle frame revision restarts");
        Equal(
            true,
            coordinator.TrySpeakPending(_ => true, out spoken),
            "reset battle selection speaks again");
        Equal("Cloud. Attack", spoken, "reset restores ready-actor prefix");
    }

    private static void WorkerFailsClosedForUnsupportedUnreadableAndTornState(
        Steam2026FingerprintResult supportedRuntime)
    {
        var invalidFixture = BattleRendererIngressFixture.Create();
        var invalid = new Steam2026BattleMenuCoordinator(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            invalidFixture.Battle.Native,
            CreateResolvers());
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            invalid.Observe(CreateIngressSnapshot(1, 0)).Kind,
            "unsupported battle renderer state rejected");
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            invalid.Observe(CreateIngressSnapshot(0, 1)).Kind,
            "invalid battle renderer sequence rejected");
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            invalid.Observe(CreateIngressSnapshot(2, 1) with
            {
                TimestampUtc = DateTime.SpecifyKind(Timestamp, DateTimeKind.Local)
            }).Kind,
            "non-UTC battle renderer callback rejected");
        Equal(
            false,
            invalid.TrySpeakPending(_ => true, out _),
            "invalid battle callbacks cannot speak");

        var unreadableFixture = BattleRendererIngressFixture.Create();
        var unreadable = new Steam2026BattleMenuCoordinator(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            unreadableFixture.Battle.Native,
            CreateResolvers());
        unreadableFixture.Battle.UnmapGuestPage(
            (uint)BattleStateReader.AddressCurrentModule);
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            unreadable.Observe(CreateIngressSnapshot(1, 1)).Kind,
            "unreadable checked battle snapshot rejected");
        Equal(
            false,
            unreadable.TrySpeakPending(_ => true, out _),
            "unreadable battle snapshot cannot speak");

        var tornFixture = BattleRendererIngressFixture.Create();
        var watchedModule = tornFixture.Battle.GetHostAddress(
            (uint)BattleStateReader.AddressCurrentModule);
        var tearing = new TearingNativeMemoryReader(
            tornFixture.Battle.Native,
            watchedModule,
            triggerRead: 2,
            () => tornFixture.Battle.WriteByte(
                BattleStateReader.AddressCurrentModule,
                FieldPositionReader.FieldModule));
        var torn = new Steam2026BattleMenuCoordinator(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            tearing,
            CreateResolvers());
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            torn.Observe(CreateIngressSnapshot(1, 1)).Kind,
            "torn checked battle snapshot rejected");
        Equal(
            false,
            torn.TrySpeakPending(_ => true, out _),
            "torn battle snapshot cannot speak");

        var blankNameFixture = BattleRendererIngressFixture.Create();
        var blankName = new Steam2026BattleMenuCoordinator(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            blankNameFixture.Battle.Native,
            CreateResolvers(_ => null));
        Equal(
            RuntimeDomainUpdateKind.Unchanged,
            blankName.Observe(CreateIngressSnapshot(1, 1)).Kind,
            "unresolved native command name rejected");
        Equal(
            false,
            blankName.TrySpeakPending(_ => true, out _),
            "unresolved native command cannot speak");
    }

    private static void HookSetOwnsTheExactBattleCallbackCohort()
    {
        var hookFields = typeof(Steam2026BattleRendererHookSet)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType.IsGenericType)
            .Where(field => field.FieldType.GetGenericTypeDefinition() == typeof(IHook<>))
            .ToArray();
        var delegateTypes = hookFields
            .Select(field => field.FieldType.GetGenericArguments()[0])
            .ToHashSet();
        Equal(6, hookFields.Length, "battle hook set owns six native hooks");
        Equal(true, delegateTypes.Contains(typeof(TranslatedBattleRendererCallbackOriginal)), "menu hook");
        Equal(true, delegateTypes.Contains(typeof(TranslatedBattleUpdateCallbackOriginal)), "update hook");
        Equal(
            true,
            delegateTypes.Contains(typeof(TranslatedBattleTextActivationCallbackOriginal)),
            "text hook");
        Equal(
            true,
            delegateTypes.Contains(typeof(TranslatedBattleResultsUpdateCallbackOriginal)),
            "results hook");
        Equal(
            true,
            delegateTypes.Contains(typeof(TranslatedBattleDamageDisplayCallbackOriginal)),
            "damage hook");
        Equal(
            true,
            delegateTypes.Contains(typeof(TranslatedBattleActionTextCommitCallbackOriginal)),
            "action-text commit hook");
    }

    private static void StaleWorkerReadDoesNotRearmUnchangedMenuSpeech(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        var coordinator = new Steam2026BattleMenuCoordinator(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            fixture.Battle.Native,
            CreateResolvers());

        _ = coordinator.Observe(CreateIngressSnapshot(1, 1));
        Equal(
            true,
            coordinator.TrySpeakPending(_ => true, out var first),
            "initial coherent selection speaks");
        Equal("Cloud. Attack", first, "initial coherent selection text");

        var moduleAddress = (uint)BattleStateReader.AddressCurrentModule;
        var moduleHostPage = fixture.Battle.GetHostAddress(moduleAddress) & ~0xFFFul;
        fixture.Battle.UnmapGuestPage(moduleAddress);
        _ = coordinator.Observe(CreateIngressSnapshot(2, 1));
        Equal(
            false,
            coordinator.TrySpeakPending(_ => true, out _),
            "stale unreadable callback stays silent");

        fixture.Battle.MapGuestPage(moduleAddress, moduleHostPage);
        _ = coordinator.Observe(CreateIngressSnapshot(3, 1));
        Equal(
            false,
            coordinator.TrySpeakPending(_ => true, out _),
            "same selection after stale read remains deduplicated");
    }

    private static Steam2026BattleRendererCallbackContract CreateExactContract(
        BattleRendererIngressFixture fixture,
        Steam2026FingerprintResult supportedRuntime) =>
        new(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            ModuleImageSize,
            fixture.Battle.Native);

    private static bool ProbeActiveLeaseHealth(
        Steam2026BattleRendererCallbackContract contract,
        long monotonicMilliseconds)
    {
        var method = typeof(Steam2026BattleRendererCallbackContract).GetMethod(
            "IsActiveHookLeaseHealthy",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Equal(true, method is not null, "battle worker lease-health probe exists");
        return (bool)(method!.Invoke(contract, [monotonicMilliseconds])
                      ?? throw new InvalidOperationException(
                          "Battle worker lease-health probe returned no result."));
    }

    private static Steam2026BattleRendererIngressSnapshot CreateIngressSnapshot(
        long sequence,
        short rendererState) =>
        new(sequence, Timestamp, rendererState);

    private static Steam2026BattleTextResolvers CreateResolvers(
        Func<int, string?>? resolveCommandName = null) =>
        new(
            abilityId => abilityId == 27 ? "Fire" : null,
            abilityId => abilityId == 27 ? "Fire damage" : null,
            itemId => itemId == 7 ? "Phoenix Down" : null,
            itemId => itemId == 7 ? "Restores life" : null,
            resolveCommandName ?? (commandId => commandId switch
            {
                1 => "Attack",
                2 => "Magic",
                18 => "Change",
                19 => "Defend",
                _ => null
            }),
            objectId => objectId == 7 ? "Phoenix Down" : null);

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return true;
        }

        return false;
    }

    private static string FindPrototypeRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "analysis", "dual_runtime"))
                && Directory.Exists(Path.Combine(current.FullName, "reloaded")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Unable to locate accessibility_prototype root.");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static object GetProperty(object source, string name) =>
        source.GetType().GetProperty(name)?.GetValue(source)
        ?? throw new InvalidOperationException($"Missing lifecycle capture property: {name}.");

    private static T GetProperty<T>(object source, string name) =>
        GetProperty(source, name) is T value
            ? value
            : throw new InvalidOperationException($"Lifecycle capture property has wrong type: {name}.");

    private sealed class RejectingIngressQueue :
        ISequencedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>
    {
        public int Attempts { get; private set; }

        public bool TryEnqueue(Steam2026BattleRendererIngressSnapshot item)
        {
            Attempts++;
            return false;
        }

        public bool TryEnqueueSequenced(
            Steam2026BattleRendererIngressSnapshot item,
            NativeIngressSequenceAssigner<Steam2026BattleRendererIngressSnapshot> assignSequence) =>
            TryEnqueue(item);
    }

    private sealed class FirstPublicationDelayingIngressQueue :
        ISequencedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>
    {
        private readonly BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot> inner;
        private readonly ManualResetEventSlim firstPublicationEntered = new(false);
        private readonly ManualResetEventSlim releaseFirstPublication = new(false);
        private int publicationAttempts;

        internal FirstPublicationDelayingIngressQueue(int capacity)
        {
            inner = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(capacity);
        }

        public bool TryEnqueue(Steam2026BattleRendererIngressSnapshot item)
        {
            DelayFirstPublication();
            return inner.TryEnqueue(item);
        }

        public bool TryEnqueueSequenced(
            Steam2026BattleRendererIngressSnapshot item,
            NativeIngressSequenceAssigner<Steam2026BattleRendererIngressSnapshot> assignSequence)
        {
            DelayFirstPublication();
            return inner.TryEnqueueSequenced(item, assignSequence);
        }

        private void DelayFirstPublication()
        {
            if (Interlocked.Increment(ref publicationAttempts) == 1)
            {
                firstPublicationEntered.Set();
                if (!releaseFirstPublication.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out delaying the first battle publication.");
                }
            }
        }

        internal bool WaitUntilFirstPublication(TimeSpan timeout) =>
            firstPublicationEntered.Wait(timeout);

        internal void ReleaseFirstPublication() => releaseFirstPublication.Set();

        internal bool TryDequeue(out Steam2026BattleRendererIngressSnapshot snapshot) =>
            inner.TryDequeue(out snapshot);
    }

}

internal sealed class BattleRendererIngressFixture
{
    public const uint GuestEsp = 0x0012F000;

    private BattleRendererIngressFixture(BattleObservationFixture battle)
    {
        Battle = battle;
    }

    public BattleObservationFixture Battle { get; }

    public static BattleRendererIngressFixture Create()
    {
        var battle = BattleObservationFixture.CreatePopulated();
        var fixture = new BattleRendererIngressFixture(battle);
        foreach (var kind in Enum.GetValues<Steam2026BattleRendererCallbackKind>())
        {
            var metadata = Steam2026BattleRendererCallbackCatalog.GetMetadata(kind);
            battle.Native.MapRegion(
                BattleObservationFixture.ModuleBase + metadata.FunctionMap.HostRva,
                0x1000,
                BattleObservationFixture.ModuleBase,
                isCommitted: true,
                isExecutable: true);
            battle.Native.Write(
                BattleObservationFixture.ModuleBase + metadata.FunctionMap.MappingRecordRva,
                BitConverter.GetBytes((ulong)metadata.FunctionMap.LegacyVirtualAddress));
            battle.Native.Write(
                BattleObservationFixture.ModuleBase + metadata.FunctionMap.MappingRecordRva + sizeof(ulong),
                BitConverter.GetBytes(
                    BattleObservationFixture.ModuleBase + metadata.FunctionMap.HostRva));
            battle.Native.Write(
                BattleObservationFixture.ModuleBase + metadata.FunctionMap.HostRva,
                Convert.FromHexString(metadata.FunctionMap.ExpectedPrefixHex));
        }

        fixture.WriteRendererState(1);
        return fixture;
    }

    public void WriteRendererState(short rendererState)
    {
        Battle.Native.Write(
            BattleObservationFixture.ModuleBase + TranslatedX86CallFrameReader.EspRva,
            BitConverter.GetBytes(GuestEsp));
        Battle.Write(GuestEsp, BitConverter.GetBytes(0x006D0000u));
        Battle.Write(GuestEsp + sizeof(uint), BitConverter.GetBytes(0x00DC0000u));
        Battle.Write(
            GuestEsp + (2 * sizeof(uint)),
            BitConverter.GetBytes(unchecked((uint)(ushort)rendererState)));
    }

    public void WriteTextBufferIndex(short bufferIndex)
    {
        Battle.Native.Write(
            BattleObservationFixture.ModuleBase + TranslatedX86CallFrameReader.EspRva,
            BitConverter.GetBytes(GuestEsp));
        Battle.Write(GuestEsp, BitConverter.GetBytes(0x006D0000u));
        Battle.Write(
            GuestEsp + sizeof(uint),
            BitConverter.GetBytes(unchecked((uint)(ushort)bufferIndex)));
    }

    public void WriteActionTextCommitFrame(
        byte actorIndex,
        byte commandId,
        ushort actionId,
        short remainingFrames,
        ushort effectIndex = 0)
    {
        Battle.Native.Write(
            BattleObservationFixture.ModuleBase + TranslatedX86CallFrameReader.EspRva,
            BitConverter.GetBytes(GuestEsp));
        Battle.Write(GuestEsp, BitConverter.GetBytes(0x004278A6u));
        Battle.Write(GuestEsp + sizeof(uint), BitConverter.GetBytes((uint)commandId));
        Battle.Write(GuestEsp + (2 * sizeof(uint)), BitConverter.GetBytes((uint)actionId));
        Battle.WriteByte((int)Steam2026BattleActionTextMemory.AddressActiveActor, actorIndex);
        Battle.WriteByte(
            checked((int)Steam2026BattleActionTextMemory.AddressBattleModelCommand)
            + actorIndex * Steam2026BattleActionTextMemory.BattleModelStateSize,
            commandId);
        Battle.WriteUInt16(
            checked((int)Steam2026BattleActionTextMemory.AddressSmallBattleModelAction)
            + actorIndex * Steam2026BattleActionTextMemory.SmallBattleModelStateSize,
            actionId);
        Battle.WriteUInt16(
            checked((int)Steam2026BattleActionTextMemory.AddressEffectIndex),
            effectIndex);
        Battle.WriteUInt16(
            checked((int)Steam2026BattleActionTextMemory.AddressEffectData)
            + effectIndex * Steam2026BattleActionTextMemory.EffectRecordSize
            + Steam2026BattleActionTextMemory.RemainingFramesOffset,
            unchecked((ushort)remainingFrames));
    }
}
