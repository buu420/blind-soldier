using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class CondorBattleInitializationTests
{
    internal static void Run()
    {
        RejectsThePreLoadModuleNineSnapshot();
        ConfirmsSetupAcrossTwoSamplesOneReadIntervalApart();
        RestartsConfirmationWhenSetupInvariantsChange();
        RestartsConfirmationAfterAnUnreadableSample();
        AcceptsAnAlreadyRunningBattleImmediately();
        RejectsAnInvalidInteractionModeEvenAfterSetup();
        NeverRegatesAConfirmedBattle();
        ResetRequiresASetupBattleToConfirmAgain();
        PreservesVisibleStateOnTheFirstAcceptedSnapshot();
        PrimesTheOpeningCursorUntilItsPositionChanges();
        KeepsCoordinatesAndUnitDetailsWhenPrimingAnOccupiedCursor();
        BanksAStatusRequestUntilAConfirmedSnapshotExists();
    }

    /// <summary>
    /// Catches the reader treating a zero collision-record count as a complete
    /// battle. Module 9 is observable before its initializer has copied and
    /// loaded all player-visible state; accepting this snapshot is what spoke
    /// zero gil and cursor 0,0 on 2026-08-22.
    /// </summary>
    private static void RejectsThePreLoadModuleNineSnapshot()
    {
        var reader = new CondorBattleStateReader(new ReadableZeroedAddressSpace());

        if (reader.TryRead() is not null)
        {
            throw new InvalidOperationException(
                "A module 9 snapshot without loaded battlefield geometry must not be spoken.");
        }
    }

    /// <summary>
    /// Geometry is loaded before FUN_005F7979 has finished putting the setup
    /// state together. One readable phase-one sample is therefore a candidate,
    /// not permission to announce the battle.
    /// </summary>
    private static void ConfirmsSetupAcrossTwoSamplesOneReadIntervalApart()
    {
        var clock = new ManualTimeProvider();
        var reader = new CondorBattleStateReader(new InitializedSetupAddressSpace(), clock);

        if (reader.TryRead() is not null)
        {
            throw new InvalidOperationException(
                "The first initialized phase-one sample must be held for confirmation.");
        }

        clock.Advance(CondorBattleStateReader.ReadInterval - TimeSpan.FromMilliseconds(1));
        if (reader.TryRead() is not null)
        {
            throw new InvalidOperationException(
                "A matching setup sample less than one read interval later is still too early.");
        }

        clock.Advance(TimeSpan.FromMilliseconds(1));
        if (reader.TryRead() is null)
        {
            throw new InvalidOperationException(
                "A matching phase-one sample one read interval later must be accepted.");
        }
    }

    private static void RestartsConfirmationWhenSetupInvariantsChange()
    {
        var clock = new ManualTimeProvider();
        var memory = new InitializedSetupAddressSpace();
        var reader = new CondorBattleStateReader(memory, clock);

        AssertNull(reader.TryRead(), "first setup candidate");
        clock.Advance(CondorBattleStateReader.ReadInterval);
        memory.WriteInt32(InitializedSetupAddressSpace.InteractionMode, 2);
        AssertNull(reader.TryRead(), "changed interaction mode starts a new candidate");

        clock.Advance(CondorBattleStateReader.ReadInterval - TimeSpan.FromMilliseconds(1));
        AssertNull(reader.TryRead(), "changed candidate before a full interval");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        AssertNotNull(reader.TryRead(), "changed candidate after a full interval");
    }

    private static void RestartsConfirmationAfterAnUnreadableSample()
    {
        var clock = new ManualTimeProvider();
        var memory = new InitializedSetupAddressSpace();
        var reader = new CondorBattleStateReader(memory, clock);

        AssertNull(reader.TryRead(), "first setup candidate before a failed read");
        clock.Advance(CondorBattleStateReader.ReadInterval);
        memory.Unreadable.Add(InitializedSetupAddressSpace.Gil);
        AssertNull(reader.TryRead(), "unreadable setup sample");

        memory.Unreadable.Clear();
        clock.Advance(CondorBattleStateReader.ReadInterval);
        AssertNull(reader.TryRead(), "first candidate after the failed read");
        clock.Advance(CondorBattleStateReader.ReadInterval);
        AssertNotNull(reader.TryRead(), "confirmed candidate after the failed read");
    }

    private static void AcceptsAnAlreadyRunningBattleImmediately()
    {
        var memory = new InitializedSetupAddressSpace();
        memory.WriteInt32(InitializedSetupAddressSpace.Phase, 2);
        var reader = new CondorBattleStateReader(memory, new ManualTimeProvider());

        AssertNotNull(
            reader.TryRead(),
            "a battle first observed after setup must not remain silent");
    }

    private static void RejectsAnInvalidInteractionModeEvenAfterSetup()
    {
        var memory = new InitializedSetupAddressSpace();
        memory.WriteInt32(InitializedSetupAddressSpace.Phase, 2);
        memory.WriteInt32(InitializedSetupAddressSpace.InteractionMode, 0);
        var reader = new CondorBattleStateReader(memory, new ManualTimeProvider());

        AssertNull(reader.TryRead(), "a zero interaction mode is still pre-initialization");
        memory.WriteInt32(InitializedSetupAddressSpace.InteractionMode, 3);
        AssertNotNull(reader.TryRead(), "mode three is a valid already-running battle mode");
    }

    private static void NeverRegatesAConfirmedBattle()
    {
        var clock = new ManualTimeProvider();
        var memory = new InitializedSetupAddressSpace();
        var reader = new CondorBattleStateReader(memory, clock);

        AssertNull(reader.TryRead(), "first candidate before permanent confirmation");
        clock.Advance(CondorBattleStateReader.ReadInterval);
        AssertNotNull(reader.TryRead(), "permanently confirmed battle");

        memory.WriteInt32(InitializedSetupAddressSpace.InteractionMode, 0);
        AssertNotNull(
            reader.TryRead(),
            "a transient late mode value must not silence a confirmed result snapshot");
    }

    private static void ResetRequiresASetupBattleToConfirmAgain()
    {
        var clock = new ManualTimeProvider();
        var memory = new InitializedSetupAddressSpace();
        var reader = new CondorBattleStateReader(memory, clock);

        AssertNull(reader.TryRead(), "first setup candidate before reset");
        clock.Advance(CondorBattleStateReader.ReadInterval);
        AssertNotNull(reader.TryRead(), "confirmed setup before reset");

        reader.Reset();
        clock.Advance(CondorBattleStateReader.ReadInterval);
        AssertNull(reader.TryRead(), "the next battle needs its own confirmation");
    }

    /// <summary>
    /// The confirmation delay must not turn the first accepted reading into a
    /// silent baseline. Its banner, result and hire screen are already visible
    /// to a sighted player and have to be spoken from that same reading.
    /// </summary>
    private static void PreservesVisibleStateOnTheFirstAcceptedSnapshot()
    {
        var menuTracker = new CondorBattleSpeechTracker();
        var menuSnapshot = Snapshot(
            messageId: 12,
            modalState: CondorBattleSnapshot.SettingMenuModalState,
            availableTypeIds: [1],
            gil: 500);
        var opening = menuTracker.Observe(menuSnapshot);

        Equal(4, opening.Count, "first accepted menu line count");
        Equal(menuTracker.DescribeStatus(menuSnapshot), opening[0], "opening status");
        Equal("Set units.", opening[1], "opening banner");
        Equal("Setting menu. 500 gil.", opening[2], "opening Setting Menu");
        Equal(
            "Fighter. 400 gil. HP 200. Attack 30. Speed 224. Regular unit.",
            opening[3],
            "opening highlighted hire row");
        Equal(0, menuTracker.Observe(menuSnapshot).Count, "opening menu state is not repeated");

        var resultTracker = new CondorBattleSpeechTracker();
        var resultSnapshot = Snapshot(messageId: 7, outcome: 2);
        var result = resultTracker.Observe(resultSnapshot);
        Equal(2, result.Count, "first accepted result line count");
        Equal(resultTracker.DescribeStatus(resultSnapshot), result[0], "result opening status");
        Equal(
            "Enemy invasion. They reached the fort. Battle lost.",
            result[1],
            "opening result is not swallowed or duplicated");
        Equal(
            0,
            resultTracker.Observe(resultSnapshot)
                .Count(line => line.Contains("Battle lost", StringComparison.Ordinal)),
            "opening result remains a one-time announcement");
    }

    /// <summary>
    /// The opening status already describes the accepted cursor position and
    /// its placement answer. An unchanged next sample must not repeat that
    /// readout, while a real move must still be announced once it lands.
    /// </summary>
    /// <remarks>
    /// This asserted immediate announcement until 2026-08-22, when speaking every
    /// sample the cursor had moved in turned out to queue faster than a screen
    /// reader can talk: the game's held-key repeat carries the cursor about twenty
    /// units per reading, and the backlog ran on after the key was released. What
    /// must survive is that a real move is never swallowed - only that it is
    /// announced where the cursor stops rather than for every row it crosses.
    /// </remarks>
    private static void PrimesTheOpeningCursorUntilItsPositionChanges()
    {
        var tracker = new CondorBattleSpeechTracker();
        var entry = Snapshot(messageId: 12, gil: 9436) with
        {
            CursorX = 248,
            CursorY = 96
        };

        var opening = tracker.Observe(entry);
        Equal(
            "9436 gil. 0 units. 0 enemies. cursor at 248, 96. Cannot place. " +
            "game speed 2 of 4. no enemy advance.",
            opening[0],
            "opening status carries the cursor position and placement answer");
        Equal("Set units.", opening[1], "opening setup banner");
        Equal(0, tracker.Observe(entry).Count, "unchanged opening cursor is not repeated");

        // Travelling: the cursor has left 96 but has not arrived anywhere yet.
        var travelling = entry with { CursorY = 112 };
        Equal(0, tracker.Observe(travelling).Count, "a row crossed in transit is not announced");

        // Arrived, and said - the move is delayed by one reading, never lost.
        var moved = tracker.Observe(travelling);
        Equal(1, moved.Count, "first real cursor move line count");
        Equal("248, 112. Cannot place.", moved[0], "first real cursor move is not swallowed");
        Equal(
            true,
            tracker.LastObservationSupersedesSpeech,
            "the cursor readout replaces speech rather than queueing behind it");
    }

    /// <summary>
    /// Priming must not trade the duplicate for missing information. The
    /// opening status names both where the cursor is and the unit under it.
    /// </summary>
    private static void KeepsCoordinatesAndUnitDetailsWhenPrimingAnOccupiedCursor()
    {
        var unit = new CondorBattleUnit(
            Slot: 0,
            IsEnemy: false,
            TypeId: 1,
            CurrentHp: 200,
            MaximumHp: 200,
            Attack: 30,
            X: 248,
            Y: 96,
            IsDying: false,
            Width: 28,
            HeightAbove: 10);
        var entry = Snapshot(gil: 9436) with
        {
            CursorX = 248,
            CursorY = 96,
            UnitUnderCursorSlot = unit.Slot,
            Units = [unit],
            AlliedCount = 1
        };
        var tracker = new CondorBattleSpeechTracker();

        var opening = tracker.Observe(entry);
        Equal(
            "9436 gil. 1 unit. 0 enemies. cursor at 248, 96. on Fighter, 200 of 200. " +
            "game speed 2 of 4. no enemy advance.",
            opening[0],
            "occupied opening status carries coordinates and unit details");
        Equal(0, tracker.Observe(entry).Count, "unchanged occupied cursor is not repeated");
    }

    private static void BanksAStatusRequestUntilAConfirmedSnapshotExists()
    {
        var snapshot = Snapshot(gil: 9436);
        var tracker = new CondorBattleSpeechTracker();

        tracker.RequestStatus();
        Equal(true, tracker.HasPendingStatusRequest, "K is banked before a snapshot exists");
        Equal(
            tracker.DescribeStatus(snapshot),
            tracker.ConsumeRequestedStatus(snapshot, openingStatusWillBeSpoken: false),
            "banked K is answered by the next accepted snapshot");
        Equal(false, tracker.HasPendingStatusRequest, "answered K is consumed");

        // On entry Observe itself starts with the same status. The banked key is
        // consumed but does not produce a duplicate interrupting copy.
        var entering = new CondorBattleSpeechTracker();
        entering.RequestStatus();
        var opening = entering.Observe(snapshot);
        Equal(entering.DescribeStatus(snapshot), opening[0], "automatic opening status");
        Equal(
            null,
            entering.ConsumeRequestedStatus(snapshot, openingStatusWillBeSpoken: true),
            "opening status answers K without duplication");
        Equal(false, entering.HasPendingStatusRequest, "opening K is consumed");

        tracker.RequestStatus();
        tracker.Reset();
        Equal(false, tracker.HasPendingStatusRequest, "a battle exit drops its banked K");
    }

    private static CondorBattleSnapshot Snapshot(
        int messageId = -1,
        int outcome = 0,
        int modalState = 0,
        IReadOnlyList<int>? availableTypeIds = null,
        int gil = 1000) =>
        new(
            InteractionMode: CondorBattleSnapshot.CursorInteractionMode,
            ModalState: modalState,
            SettingMenuRow: 0,
            SettingMenuRotation: 0,
            AvailableTypeIds: availableTypeIds ?? [],
            Gil: gil,
            CursorX: 0,
            CursorY: 0,
            CursorPlacementLegal: false,
            UnitUnderCursorSlot: -1,
            Units: [],
            AlliedCount: 0,
            EnemyCount: 0,
            Outcome: outcome,
            MessageId: messageId,
            Phase: CondorPlacementRegion.SetupPhase,
            ReportState: 0,
            DeploymentFrontierY: 0,
            EnemyAdvance: 0,
            CollisionTriangles: []);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void AssertNull(object? value, string label)
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"{label}: expected null, got a snapshot.");
        }
    }

    private static void AssertNotNull(object? value, string label)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"{label}: expected a snapshot, got null.");
        }
    }

    /// <summary>
    /// Every address is readable but still in its pre-initialization zero state.
    /// This is the exact distinction the regression protects: a successful
    /// memory read is not necessarily a finished native battle state.
    /// </summary>
    private sealed class ReadableZeroedAddressSpace : ILegacyAddressSpace
    {
        public bool TryRead(uint address, Span<byte> destination)
        {
            destination.Clear();
            return true;
        }
    }

    private sealed class InitializedSetupAddressSpace : ILegacyAddressSpace
    {
        internal const uint InteractionMode = 0x00C74C50;
        internal const uint Phase = 0x00C625D4;
        private const uint CollisionCount = 0x00C60AA4;
        private const uint UnitUnderCursor = 0x00C6097C;
        private const uint GameSpeed = 0x00C752B4;
        internal const uint Gil = 0x00CBC7E0;
        private readonly Dictionary<uint, byte> bytes = [];

        internal HashSet<uint> Unreadable { get; } = [];

        internal InitializedSetupAddressSpace()
        {
            WriteInt32(InteractionMode, CondorBattleSnapshot.CursorInteractionMode);
            WriteInt32(Phase, CondorPlacementRegion.SetupPhase);
            WriteInt32(CollisionCount, 1);
            WriteInt16(UnitUnderCursor, -1);
            WriteInt16(GameSpeed, 2);
        }

        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var offset = 0; offset < destination.Length; offset++)
            {
                if (Unreadable.Contains(address + (uint)offset))
                {
                    destination.Clear();
                    return false;
                }

                destination[offset] = bytes.GetValueOrDefault(address + (uint)offset);
            }

            return true;
        }

        internal void WriteInt32(uint address, int value) =>
            Store(address, BitConverter.GetBytes(value));

        private void WriteInt16(uint address, short value) =>
            Store(address, BitConverter.GetBytes(value));

        private void Store(uint address, IReadOnlyList<byte> value)
        {
            for (var offset = 0; offset < value.Count; offset++)
            {
                bytes[address + (uint)offset] = value[offset];
            }
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        internal void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }
}
