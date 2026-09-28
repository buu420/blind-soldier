using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Battle;

/// <summary>
/// The drain, lethal-row and drawn-status paths through the Steam 2026 guest
/// translation: the same guest bytes read through the translated page table
/// and directly must agree, the popup's provenance must be captured on the
/// guest thread before the original retires it, and the worker must speak the
/// drawn result rather than the calculated one.
/// </summary>
internal static class Steam2026BattleVisibleFeedbackTests
{
    private const int Cloud = 0;
    private const int Grunt = 4;
    private const ulong ModuleImageSize = 0x02100000;
    private static readonly DateTime Timestamp = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    public static void Run(Steam2026FingerprintResult supportedRuntime)
    {
        TranslatedReaderFollowsTheDrainLikeTheDirectOne(supportedRuntime);
        GuestThreadCaptureKeepsTheProvenanceTheOriginalRetires(supportedRuntime);
        WorkerSpeaksTheCapturedDrainAndLethalRow(supportedRuntime);
        WorkerWaitsForTheDrawnStatus(supportedRuntime);
    }

    private static void TranslatedReaderFollowsTheDrainLikeTheDirectOne(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var battle = new GuestBattle(fixture);
        var popup = battle.QueueGruntDrainOnCloud();

        var translated = new BattleVisibleResultReader(
            ValidatedTranslatedX86AddressSpaceFactory.Create(
                supportedRuntime,
                BattleObservationFixture.ModuleBase,
                fixture.Native)).Read();
        var direct = new BattleVisibleResultReader(fixture.Direct).Read();
        Equal(direct, translated, "translated and direct guest reads agree");
        Equal(popup, translated.EffectIndex, "the drainer's popup");
        Equal(true, translated.IsDrainRecovery, "FUN_005da380's event read through the page table");
        Equal((ushort)(1 << Cloud), translated.DrainSourceMask, "Cloud is the drain source");
    }

    private static void GuestThreadCaptureKeepsTheProvenanceTheOriginalRetires(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleRendererIngressFixture.Create();
        var battle = new GuestBattle(fixture.Battle);
        var popup = battle.QueueGruntDrainOnCloud();
        var queue = new BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>(2);
        using var ingress = new Steam2026BattleRendererDetourIngressCoordinator(
            new Steam2026BattleRendererCallbackContract(
                supportedRuntime,
                BattleObservationFixture.ModuleBase,
                ModuleImageSize,
                fixture.Battle.Native),
            () => { },
            () => { },
            () => { },
            () => { },
            () => battle.RetireFirstFrame(popup),
            () => { },
            () => Timestamp,
            queue);

        ingress.OnDamageDisplay();

        Equal(true, queue.TryDequeue(out var captured), "damage ingress published");
        Equal(true, captured.CapturedResult.IsDrainRecovery, "provenance captured before the original ran");
        Equal((ushort)(1 << Cloud), captured.CapturedResult.DrainSourceMask, "captured drain source");
        Equal(60, captured.CapturedDamage.Value, "the legacy popup shape is still captured");
        Equal(false, new BattleVisibleResultReader(fixture.Battle.Direct).Read().IsValid, "the original retired the popup");
    }

    private static void WorkerSpeaksTheCapturedDrainAndLethalRow(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var battle = new GuestBattle(fixture);
        var coordinator = CreateCoordinator(fixture, supportedRuntime);
        coordinator.ProcessBatch([Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate)]);
        _ = Drain(coordinator);

        battle.QueueGruntDrainOnCloud();
        var drain = new BattleVisibleResultReader(fixture.Direct).Read();
        coordinator.ProcessBatch([Snapshot(2, Steam2026BattleRendererCallbackKind.DamageDisplay, drain)]);
        var drainSpeech = Drain(coordinator);
        Equal(1, drainSpeech.Count, "one line for the drain popup");
        Equal(Steam2026BattleSpeechDomain.Damage, drainSpeech[0].Domain, "drain domain");
        Equal("Grunt drained 60 HP from Cloud.", drainSpeech[0].Text, "drain recipient and source");

        var lethal = new BattleVisibleResultSnapshot(
            true,
            7,
            Grunt,
            42,
            0,
            HasProvenance: true,
            AttackerActor: Cloud,
            ResultRow: 5,
            ResultFlags: BattleVisibleResultSnapshot.LethalResultFlag | 1);
        coordinator.ProcessBatch([Snapshot(3, Steam2026BattleRendererCallbackKind.DamageDisplay, lethal)]);
        var lethalSpeech = Drain(coordinator);
        Equal(2, lethalSpeech.Count, "number then defeat");
        Equal("Grunt took 42 damage.", lethalSpeech[0].Text, "the drawn number");
        Equal(Steam2026BattleSpeechDomain.Status, lethalSpeech[1].Domain, "defeat domain");
        Equal("Grunt was defeated.", lethalSpeech[1].Text, "defeat from the lethal row");
    }

    private static void WorkerWaitsForTheDrawnStatus(Steam2026FingerprintResult supportedRuntime)
    {
        const int haste = 1 << 8;
        var fixture = BattleObservationFixture.CreatePopulated();
        var battle = new GuestBattle(fixture);
        var coordinator = CreateCoordinator(fixture, supportedRuntime);
        coordinator.ProcessBatch([Snapshot(1, Steam2026BattleRendererCallbackKind.BattleUpdate)]);
        _ = Drain(coordinator);

        fixture.WriteInt32(BattleStateReader.AddressBattleActors + BattleStateReader.ActorStatusMaskOffset, haste);
        coordinator.ProcessBatch([Snapshot(2, Steam2026BattleRendererCallbackKind.BattleUpdate)]);
        Equal(
            false,
            Drain(coordinator).Any(speech => speech.Domain == Steam2026BattleSpeechDomain.Status),
            "a calculated Haste is not spoken before it is drawn");

        battle.Display(Cloud, haste);
        coordinator.ProcessBatch([Snapshot(3, Steam2026BattleRendererCallbackKind.BattleUpdate)]);
        var drawn = Drain(coordinator).Where(speech => speech.Domain == Steam2026BattleSpeechDomain.Status).ToArray();
        Equal(1, drawn.Length, "one drawn status line");
        Equal("Cloud gained Haste.", drawn[0].Text, "spoken once drawn");
    }

    private static Steam2026BattleAccessibilityCoordinator CreateCoordinator(
        BattleObservationFixture fixture,
        Steam2026FingerprintResult supportedRuntime) =>
        new(
            supportedRuntime,
            BattleObservationFixture.ModuleBase,
            fixture.Native,
            Steam2026BattleObservationTests.Resolvers,
            Steam2026BattleAccessibilityOptions.AllEnabled);

    private static Steam2026BattleRendererIngressSnapshot Snapshot(
        long sequence,
        Steam2026BattleRendererCallbackKind kind,
        BattleVisibleResultSnapshot result = default) =>
        new(
            sequence,
            Timestamp.AddMilliseconds(sequence),
            kind,
            0,
            CapturedDamage: result.ToPopupSnapshot(),
            CapturedResult: result);

    private static List<Steam2026BattleSpeech> Drain(Steam2026BattleAccessibilityCoordinator coordinator)
    {
        var result = new List<Steam2026BattleSpeech>();
        while (coordinator.TrySpeakPending(_ => true, out var speech))
        {
            result.Add(speech);
        }

        return result;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Steam 2026 battle visible feedback: {message}. Expected <{expected}>, got <{actual}>.");
        }
    }

    /// <summary>
    /// Writes the engine's own structures into the fixture's guest memory, which
    /// the fixture mirrors into translated host pages.
    /// </summary>
    private sealed class GuestBattle
    {
        private readonly BattleObservationFixture fixture;

        public GuestBattle(BattleObservationFixture fixture)
        {
            this.fixture = fixture;
            Zero(BattleVisibleResultReader.AddressEffectFunctions, BattleVisibleResultReader.EffectCount * 4);
            Zero(
                BattleVisibleResultReader.AddressEffectData,
                BattleVisibleResultReader.EffectCount * BattleVisibleResultReader.EffectRecordSize);
            Zero(
                BattleVisibleResultReader.AddressAnimationEvents,
                BattleVisibleResultReader.AnimationEventCapacity * BattleVisibleResultReader.AnimationEventSize);
            Zero(
                BattleVisibleResultReader.AddressResultRows,
                BattleVisibleResultReader.ResultRowCapacity * BattleVisibleResultReader.ResultRowSize);
            Zero(
                BattleVisibleResultReader.AddressDamageRows,
                BattleVisibleResultReader.DamageRowCapacity * BattleVisibleResultReader.DamageRowSize);
            Zero(
                BattleDisplayedStatusReader.AddressDisplayedStatus,
                BattleDisplayedStatusReader.ActorCount * BattleDisplayedStatusReader.DisplayedStateSize);
            fixture.WriteInt32(BattleVisibleResultReader.AddressAnimationEventCount, 0);
            fixture.WriteInt32(BattleVisibleResultReader.AddressResultRowCount, 0);
        }

        // Grunt's Drain on Cloud, as FUN_005d9940 queues it: the action event
        // and Cloud's row, then FUN_005da380's recovery event and Grunt's row.
        // Both events have started (FUN_0042cbf9 left each cursor on its
        // terminator) and the recovery hit allocated the popup and its sibling.
        public int QueueGruntDrainOnCloud()
        {
            WriteEvent(0, attacker: Grunt, effect: 3, command: 0x20, script: 0x0B, action: 2, camera: 0x0010, cursor: 1);
            WriteRow(0, target: Cloud, attacker: Grunt, hit: 0x05, damage: 0);
            WriteDamage(0, Cloud, 60, 0);
            WriteRow(1, target: 0xFF, attacker: 0, hit: 0, damage: 0xFF);
            WriteEvent(1, attacker: Grunt, effect: 0, command: 0, script: 0x2E, action: 0, camera: 0xFFFF, cursor: 3);
            WriteRow(2, target: Grunt, attacker: Grunt, hit: 0x2E, damage: 1);
            WriteDamage(1, Grunt, 60, BattleVisibleResultSnapshot.RecoveryFlag);
            WriteRow(3, target: 0xFF, attacker: 0, hit: 0, damage: 0xFF);
            fixture.WriteInt32(BattleVisibleResultReader.AddressAnimationEventCount, 2);
            fixture.WriteInt32(BattleVisibleResultReader.AddressResultRowCount, 4);

            const int popup = 2;
            const int sibling = 3;
            WriteFunction(popup, BattleVisibleResultReader.DamagePopupFunction);
            var popupRecord = (int)BattleVisibleResultReader.EffectRecordAddress(popup);
            fixture.WriteUInt16(popupRecord + BattleVisibleResultReader.PopupValueOffset, 60);
            fixture.WriteInt32(popupRecord + BattleVisibleResultReader.PopupTargetOffset, Grunt);
            fixture.WriteInt32(popupRecord + BattleVisibleResultReader.PopupFlagsOffset, BattleVisibleResultSnapshot.RecoveryFlag);
            WriteFunction(sibling, BattleVisibleResultReader.DamageCommitFunction);
            var siblingRecord = (int)BattleVisibleResultReader.EffectRecordAddress(sibling);
            fixture.WriteUInt16(siblingRecord + BattleVisibleResultReader.CommitDamageRowOffset, 1);
            fixture.WriteUInt16(siblingRecord + BattleVisibleResultReader.CommitResultRowOffset, 2);
            fixture.WriteUInt16(BattleVisibleResultReader.AddressCurrentEffectIndex, popup);
            return popup;
        }

        // FUN_005bb410 and FUN_00425e5f leave state 0 on their first frame.
        public void RetireFirstFrame(int popup)
        {
            fixture.WriteByte((int)BattleVisibleResultReader.EffectRecordAddress(popup) + BattleVisibleResultReader.EffectStateOffset, 1);
            fixture.WriteByte((int)BattleVisibleResultReader.EffectRecordAddress(popup + 1) + BattleVisibleResultReader.EffectStateOffset, 1);
        }

        public void Display(int actor, int mask) =>
            fixture.WriteInt32(
                BattleDisplayedStatusReader.AddressDisplayedStatus + actor * BattleDisplayedStatusReader.DisplayedStateSize,
                mask);

        private void WriteEvent(int index, int attacker, int effect, int command, int script, int action, int camera, int cursor)
        {
            var address = BattleVisibleResultReader.AddressAnimationEvents + index * BattleVisibleResultReader.AnimationEventSize;
            fixture.WriteByte(address, (byte)attacker);
            fixture.WriteByte(address + 1, 1);
            fixture.WriteByte(address + 2, (byte)effect);
            fixture.WriteByte(address + 3, (byte)command);
            fixture.WriteByte(address + 4, 0);
            fixture.WriteByte(address + 5, (byte)script);
            fixture.WriteUInt16(address + 6, action);
            fixture.WriteUInt16(address + 8, camera);
            fixture.WriteUInt16(address + 0x0A, cursor);
        }

        private void WriteRow(int row, int target, int attacker, int hit, int damage)
        {
            var address = BattleVisibleResultReader.AddressResultRows + row * BattleVisibleResultReader.ResultRowSize;
            fixture.WriteByte(address, (byte)target);
            fixture.WriteByte(address + 1, (byte)attacker);
            fixture.WriteByte(address + 2, (byte)hit);
            fixture.WriteByte(address + 3, (byte)damage);
            fixture.WriteUInt16(address + 4, target == 0xFF ? 0 : 1);
        }

        private void WriteDamage(int row, int target, int value, int flags)
        {
            var address = BattleVisibleResultReader.AddressDamageRows + row * BattleVisibleResultReader.DamageRowSize;
            fixture.WriteUInt16(address, target);
            fixture.WriteUInt16(address + 2, value);
            fixture.WriteUInt16(address + 4, flags);
        }

        private void WriteFunction(int slot, uint function) =>
            fixture.WriteInt32(BattleVisibleResultReader.AddressEffectFunctions + slot * 4, unchecked((int)function));

        private void Zero(int address, int length) => fixture.Write((uint)address, new byte[length]);
    }
}
