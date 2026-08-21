using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using System.Buffers.Binary;

internal static class CondorBattleReaderTests
{
    internal static void Run()
    {
        ReadsBothSidesOfTheLiveUnitArray();
        FailsClosedWhenAnyPartOfTheStateIsUnreadable();
        TreatsAUnitOutOfHpAsDyingRatherThanGone();
        ResolvesTheHighlightedHireRowThroughTheListRotation();
        AnnouncesTheBannerMessagesTheGameDrawsAsPictures();
        AnnouncesTheResultEvenIfTheBannerDoesNot();
        SpeaksTheHireListWithAffordability();
        SpeaksTheUnitUnderTheCursorAndWhenItClears();
        DoesNotNarrateMovementAcrossOpenGround();
        SaysNothingWhileThePlacementFlagContradictsItself();
        StatusAnswersWhatASightedPlayerSeesAtAGlance();
        NamesOnlyUnitTypesThatHaveBeenProved();
    }

    private static void ReadsBothSidesOfTheLiveUnitArray()
    {
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 2, currentHp: 180, maximumHp: 180, attack: 25, x: 240, y: 500);
        memory.WriteUnit(slot: 20, typeId: 10, currentHp: 60, maximumHp: 200, attack: 30, x: 240, y: 900);
        memory.WriteInt32(CondorMemory.AlliedCount, 1);
        memory.WriteInt32(CondorMemory.EnemyCount, 1);

        var snapshot = new CondorBattleStateReader(memory).TryRead();
        AssertNotNull(snapshot, "snapshot from a readable battle");
        Equal(2, snapshot!.Units.Count, "live unit count");

        var allied = snapshot.Units[0];
        Equal(false, allied.IsEnemy, "slot 0 side");
        Equal("Attacker", allied.Name, "slot 0 name");
        Equal("Attacker, 180 of 180", allied.Describe(), "slot 0 description");

        // Slots 20 and up are the enemy's. The split is the array's own, not a
        // flag inside the record, so it is worth pinning.
        var enemy = snapshot.Units[1];
        Equal(true, enemy.IsEnemy, "slot 20 side");
        Equal(20, enemy.Slot, "slot 20 index");
        Equal(60, enemy.CurrentHp, "slot 20 current HP");
    }

    private static void FailsClosedWhenAnyPartOfTheStateIsUnreadable()
    {
        // A missing read must not become a zero. Reporting a healthy unit as dead,
        // or an occupied square as free, is worse than reporting nothing, because
        // the player has no way to check it against the screen.
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 1, currentHp: 200, maximumHp: 200, attack: 30, x: 100, y: 100);
        memory.Unreadable.Add(CondorMemory.Gil);

        AssertNull(new CondorBattleStateReader(memory).TryRead(), "snapshot with unreadable funds");

        var tornUnits = new CondorMemory();
        tornUnits.Unreadable.Add(CondorMemory.LiveUnits + (17 * CondorMemory.UnitStride));
        AssertNull(new CondorBattleStateReader(tornUnits).TryRead(), "snapshot with an unreadable unit slot");
    }

    private static void TreatsAUnitOutOfHpAsDyingRatherThanGone()
    {
        // The allocated flag is cleared several frames after death, so a reader
        // that trusted it alone would keep a corpse in the list and announce it
        // as a live obstacle.
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 21, typeId: 10, currentHp: 0, maximumHp: 200, attack: 30, x: 300, y: 700);
        memory.WriteUnit(slot: 22, typeId: 10, currentHp: 40, maximumHp: 200, attack: 30, x: 320, y: 700, removalState: -1);
        memory.WriteUnit(slot: 23, typeId: 10, currentHp: 40, maximumHp: 200, attack: 30, x: 340, y: 700);

        var snapshot = new CondorBattleStateReader(memory).TryRead();
        AssertNotNull(snapshot, "snapshot with dying units");
        Equal(true, snapshot!.Units[0].IsDying, "unit at zero HP is dying");
        Equal(true, snapshot.Units[1].IsDying, "unit in its removal animation is dying");
        Equal(false, snapshot.Units[2].IsDying, "healthy unit is not dying");

        // The nearest enemy is what the player is judging placement against, so a
        // dying one must not be offered as the answer.
        Equal(23, snapshot.NearestEnemy!.Slot, "nearest living enemy skips the dying ones");
    }

    private static void ResolvesTheHighlightedHireRowThroughTheListRotation()
    {
        // The eight-entry list the game builds for the first tier, read back
        // through the reader so the count and the array are covered too. It is the
        // list a live battle produced on 2026-08-21.
        var ids = new[] { 1, 2, 3, 4, 12, 13, 5, 7 };
        var memory = new CondorMemory();
        memory.WriteTypeIds(ids);
        memory.WriteInt32(CondorMemory.ModalState, CondorBattleSnapshot.SettingMenuModalState);
        var reader = new CondorBattleStateReader(memory);

        var snapshot = reader.TryRead();
        AssertNotNull(snapshot, "snapshot with a built hire list");
        Equal(8, snapshot!.AvailableTypeIds.Count, "available unit count");
        Equal(1, snapshot.HighlightedTypeId, "highlighted id at the first row");

        // The row is relative to a window that rotates over the available list, so
        // the row alone names the wrong unit as soon as the list has scrolled.
        memory.WriteInt16(CondorMemory.SettingMenuRow, 2);
        Equal(3, reader.TryRead()!.HighlightedTypeId, "highlighted id with no rotation");

        memory.WriteInt16(CondorMemory.SettingMenuRotation, 2);
        Equal(12, reader.TryRead()!.HighlightedTypeId, "highlighted id with rotation");

        memory.WriteInt16(CondorMemory.SettingMenuRow, 1);
        memory.WriteInt16(CondorMemory.SettingMenuRotation, 7);
        Equal(1, reader.TryRead()!.HighlightedTypeId, "highlighted id wrapping past the end");

        // Outside the hire screen the count is whatever the last build left, so an
        // unbuilt list has to read as no list rather than as row zero of stale bytes.
        memory.WriteInt16(CondorMemory.SettingMenuCount, 0);
        AssertNull(reader.TryRead()!.HighlightedTypeId, "highlighted id with no list built");

        memory.WriteInt16(CondorMemory.SettingMenuCount, 99);
        Equal(0, reader.TryRead()!.AvailableTypeIds.Count, "available list with an impossible count");
    }

    private static void AnnouncesTheBannerMessagesTheGameDrawsAsPictures()
    {
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Battle(messageId: 12));

        Equal(
            "Encountered enemy.",
            Single(tracker.Observe(Battle(messageId: 0))),
            "banner message on change");

        // The same identifier still standing is the same picture still on screen.
        Equal(0, tracker.Observe(Battle(messageId: 0)).Count, "banner message repeated");
        Equal("Enemy destroyed.", Single(tracker.Observe(Battle(messageId: 10))), "later banner message");
    }

    private static void AnnouncesTheResultEvenIfTheBannerDoesNot()
    {
        var withBanner = new CondorBattleSpeechTracker();
        withBanner.Observe(Battle());
        Equal(
            "Halted enemy attack!",
            Single(withBanner.Observe(Battle(messageId: 2, outcome: 1))),
            "victory announced once when the banner agrees");

        var withoutBanner = new CondorBattleSpeechTracker();
        withoutBanner.Observe(Battle());
        Equal(
            "Enemy invasion.",
            Single(withoutBanner.Observe(Battle(outcome: 2))),
            "defeat announced from the outcome alone");
    }

    private static void SpeaksTheHireListWithAffordability()
    {
        var tracker = new CondorBattleSpeechTracker();
        var ids = new[] { 1, 2, 3, 4, 12, 13, 5, 7 };
        tracker.Observe(Battle(ids, row: 0, modalState: 0, gil: 500));

        var opened = tracker.Observe(Battle(ids, row: 0, modalState: 7, gil: 500));
        Equal(2, opened.Count, "lines when the hire list opens");
        Equal("Setting menu. 500 gil.", opened[0], "hire list opening line");
        Equal(
            "Fighter. 400 gil. HP 200. Attack 30. Speed 224. Regular unit.",
            opened[1],
            "highlighted hire line");

        // The price is drawn against the funds counter, so a sighted player can
        // see what they cannot afford before pressing anything.
        Equal(
            "Shooter. 520 gil, not affordable. HP 160. Attack 20. Speed 212. " +
            "Can shoot from afar. Beats Wyvern. Loses to Beast.",
            Single(tracker.Observe(Battle(ids, row: 3, modalState: 7, gil: 500))),
            "unaffordable hire line");
    }

    private static void SpeaksTheUnitUnderTheCursorAndWhenItClears()
    {
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 3, currentHp: 140, maximumHp: 220, attack: 35, x: 200, y: 400);
        var reader = new CondorBattleStateReader(memory);
        var tracker = new CondorBattleSpeechTracker();

        memory.WriteUInt16(CondorMemory.CursorPlacementLegal, 1);
        memory.WriteInt16(CondorMemory.UnitUnderCursor, -1);
        tracker.Observe(reader.TryRead()!);

        memory.WriteInt16(CondorMemory.UnitUnderCursor, 0);
        Equal(
            "Defender, 140 of 220.",
            Single(tracker.Observe(reader.TryRead()!)),
            "unit under the cursor");

        // The native stat panel clears when the cursor leaves the unit. Saying so
        // stops the last unit from standing as the player's picture of where they
        // are.
        memory.WriteInt16(CondorMemory.UnitUnderCursor, -1);
        Equal("Clear.", Single(tracker.Observe(reader.TryRead()!)), "cursor leaving a unit");
    }

    private static void DoesNotNarrateMovementAcrossOpenGround()
    {
        var tracker = new CondorBattleSpeechTracker();
        Settle(tracker, cursorX: 100, cursorY: 100, placementLegal: true);

        // A sighted player crossing open ground is shown nothing new. A running
        // commentary of coordinates would bury the events that do matter.
        Equal(0, tracker.Observe(Battle(cursorX: 140, cursorY: 100, placementLegal: true)).Count, "cursor moved over legal ground");
        Equal(0, tracker.Observe(Battle(cursorX: 180, cursorY: 160, placementLegal: true)).Count, "cursor moved again");

        // Whether the ground can take a unit decides whether confirm opens the
        // hire list at all, so a settled change in it is worth saying.
        Equal(
            "Blocked.",
            Single(Settle(tracker, cursorX: 180, cursorY: 200, placementLegal: false)),
            "cursor resting on ground that cannot take a unit");
        Equal(0, tracker.Observe(Battle(cursorX: 180, cursorY: 200, placementLegal: false)).Count, "cursor still on blocked ground");
        Equal(
            "Clear.",
            Single(Settle(tracker, cursorX: 180, cursorY: 280, placementLegal: true)),
            "cursor resting on open ground again");
    }

    private static void SaysNothingWhileThePlacementFlagContradictsItself()
    {
        // The flag is not stable to sample. In a real battle on 2026-08-21 six of
        // the twenty positions the cursor rested on reported both answers, one of
        // them five times without the cursor moving. Announcing each read turned
        // that into "Clear, Blocked, Clear" - which sounds exactly like fine
        // terrain detail to somebody who cannot see the hill, and is nothing of
        // the kind. Until what drives it is known, an unsettled reading is worth
        // less than silence.
        var logged = new List<string>();
        var tracker = new CondorBattleSpeechTracker(logged.Add);
        tracker.Observe(Battle(cursorX: 260, cursorY: 440, placementLegal: true));

        var spoken = 0;
        for (var sample = 0; sample < 12; sample++)
        {
            spoken += tracker
                .Observe(Battle(cursorX: 260, cursorY: 440, placementLegal: sample % 2 == 0))
                .Count;
        }

        Equal(0, spoken, "lines spoken while the flag alternates");
        Equal(true, tracker.PlacementDisagreements > 0, "disagreements counted");
        Equal(true, logged.Any(line => line.Contains("disagreed at a stationary cursor")), "disagreement logged");
        AssertContains(logged[0], "(260,440)");

        // Once it holds still it is trustworthy again, and is said.
        Equal("Clear.", Single(Settle(tracker, cursorX: 260, cursorY: 440, placementLegal: true)), "settled reading after the flapping stops");
    }

    private static void StatusAnswersWhatASightedPlayerSeesAtAGlance()
    {
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 2, currentHp: 180, maximumHp: 180, attack: 25, x: 240, y: 500);
        memory.WriteUnit(slot: 20, typeId: 10, currentHp: 120, maximumHp: 200, attack: 30, x: 240, y: 620);
        memory.WriteInt32(CondorMemory.AlliedCount, 1);
        memory.WriteInt32(CondorMemory.EnemyCount, 4);
        memory.WriteInt32(CondorMemory.Gil, 9436);
        memory.WriteInt16(CondorMemory.CursorX, 240);
        memory.WriteInt16(CondorMemory.CursorY, 500);
        memory.WriteUInt16(CondorMemory.CursorPlacementLegal, 1);
        memory.WriteInt16(CondorMemory.UnitUnderCursor, -1);

        var reader = new CondorBattleStateReader(memory);
        var snapshot = reader.TryRead();
        AssertNotNull(snapshot, "snapshot for the status line");

        // Before the placement reading has held still, the status line leaves it
        // out rather than reporting whichever value the flag happened to be on.
        var tracker = new CondorBattleSpeechTracker();
        var unsettled = tracker.DescribeStatus(snapshot!);
        Equal(
            "9436 gil. 1 unit. 4 enemies. nearest enemy unit, 120 of 200, 120 down.",
            unsettled,
            "status line before the placement reading settles");

        for (var sample = 0; sample < 6; sample++) { tracker.Observe(reader.TryRead()!); }
        Equal(
            "9436 gil. 1 unit. 4 enemies. can place here. nearest enemy unit, 120 of 200, 120 down.",
            tracker.DescribeStatus(reader.TryRead()!),
            "status line once the placement reading settles");
    }

    private static void NamesOnlyUnitTypesThatHaveBeenProved()
    {
        // The ten hireable types are tied to their names through condor.lgp's
        // record table. The enemy roster is not, so it is described by side and
        // never given a guessed name.
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 20, typeId: 10, currentHp: 200, maximumHp: 200, attack: 30, x: 100, y: 100);
        var snapshot = new CondorBattleStateReader(memory).TryRead();
        AssertNotNull(snapshot, "snapshot with an unnamed type");
        Equal("enemy unit", snapshot!.Units[0].Name, "unnamed enemy type");

        var logged = new List<string>();
        new CondorBattleSpeechTracker(logged.Add).Observe(snapshot);
        Equal(1, logged.Count, "unnamed type reported once");
        AssertContains(logged[0], "unnamed unit type 10");

        // Named ones keep their side too, because the same type can stand on both.
        memory.WriteUnit(slot: 21, typeId: 2, currentHp: 180, maximumHp: 180, attack: 25, x: 120, y: 100);
        Equal(
            "enemy Attacker",
            new CondorBattleStateReader(memory).TryRead()!.Units[1].Name,
            "named enemy type");
    }

    private static CondorBattleSnapshot Battle(
        IReadOnlyList<int>? availableTypeIds = null,
        int row = 0,
        int rotation = 0,
        int modalState = 0,
        int gil = 1000,
        int messageId = -1,
        int outcome = 0,
        int cursorX = 0,
        int cursorY = 0,
        bool placementLegal = true) =>
        new(
            InteractionMode: CondorBattleSnapshot.CursorInteractionMode,
            ModalState: modalState,
            SettingMenuRow: row,
            SettingMenuRotation: rotation,
            AvailableTypeIds: availableTypeIds ?? [],
            Gil: gil,
            CursorX: cursorX,
            CursorY: cursorY,
            CursorPlacementLegal: placementLegal,
            UnitUnderCursorSlot: -1,
            Units: [],
            AlliedCount: 0,
            EnemyCount: 0,
            Outcome: outcome,
            MessageId: messageId);

    /// <summary>
    /// Moves the cursor and holds it there long enough for the placement reading
    /// to settle, returning whatever the settling sample said.
    /// </summary>
    private static IReadOnlyList<string> Settle(
        CondorBattleSpeechTracker tracker, int cursorX, int cursorY, bool placementLegal)
    {
        IReadOnlyList<string> lines = [];
        for (var sample = 0; sample < 8; sample++)
        {
            var spoken = tracker.Observe(Battle(cursorX: cursorX, cursorY: cursorY, placementLegal: placementLegal));
            if (spoken.Count > 0) { lines = spoken; }
        }

        return lines;
    }

    private static string Single(IReadOnlyList<string> lines)
    {
        Equal(1, lines.Count, "spoken line count");
        return lines[0];
    }

    /// <summary>
    /// A sparse stand-in for the module 9 globals. Anything never written reads
    /// as zero, and anything in <see cref="Unreadable"/> fails, so a test can put
    /// a hole exactly where it wants one.
    /// </summary>
    private sealed class CondorMemory : ILegacyAddressSpace
    {
        internal const uint InteractionMode = 0x00C74C50;
        internal const uint ModalState = 0x00C625E0;
        internal const uint SettingMenuRow = 0x00CBCCA0;
        internal const uint SettingMenuRotation = 0x00C75254;
        internal const uint SettingMenuCount = 0x00C75264;
        internal const uint AvailableTypeIds = 0x00C75278;
        internal const uint Gil = 0x00CBC7E0;
        internal const uint CursorX = 0x00CBCCC0;
        internal const uint CursorY = 0x00CBCCC2;
        internal const uint CursorPlacementLegal = 0x00CBCC9C;
        internal const uint UnitUnderCursor = 0x00C6097C;
        internal const uint LiveUnits = 0x00CBCCD8;
        internal const uint AlliedCount = 0x00C60AD0;
        internal const uint EnemyCount = 0x00CBC7A4;
        internal const int UnitStride = 0x78;

        private readonly Dictionary<uint, byte> bytes = [];

        internal HashSet<uint> Unreadable { get; } = [];

        internal CondorMemory()
        {
            // Nothing under the cursor unless a test says otherwise; the native
            // value for that is -1, not 0, and 0 is a real slot.
            WriteInt16(UnitUnderCursor, -1);

            // An ordinary battle with the player moving the battlefield cursor.
            // Zero is not a mode the game uses, so leaving it unset would make
            // every cursor test pass by never reaching the cursor at all.
            WriteInt32(InteractionMode, CondorBattleSnapshot.CursorInteractionMode);
        }

        internal void WriteUnit(
            int slot,
            int typeId,
            int currentHp,
            int maximumHp,
            int attack,
            int x,
            int y,
            sbyte removalState = 0)
        {
            var unit = LiveUnits + (uint)(slot * UnitStride);
            WriteUInt16(unit + 0x00, 1);
            bytes[unit + 0x05] = (byte)removalState;
            WriteUInt16(unit + 0x06, (ushort)typeId);
            bytes[unit + 0x10] = (byte)currentHp;
            bytes[unit + 0x11] = (byte)maximumHp;
            bytes[unit + 0x12] = (byte)attack;
            WriteInt16(unit + 0x48, (short)x);
            WriteInt16(unit + 0x4A, (short)y);
        }

        internal void WriteInt32(uint address, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            Store(address, buffer);
        }

        internal void WriteInt16(uint address, short value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
            Store(address, buffer);
        }

        internal void WriteUInt16(uint address, ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            Store(address, buffer);
        }

        internal void WriteTypeIds(IReadOnlyList<int> ids)
        {
            WriteInt16(SettingMenuCount, (short)ids.Count);
            for (var index = 0; index < ids.Count; index++)
            {
                bytes[AvailableTypeIds + (uint)index] = (byte)ids[index];
            }
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var offset = 0u; offset < destination.Length; offset++)
            {
                if (Unreadable.Contains(virtualAddress + offset))
                {
                    return false;
                }

                destination[(int)offset] = bytes.GetValueOrDefault(virtualAddress + offset);
            }

            return true;
        }

        private void Store(uint address, ReadOnlySpan<byte> value)
        {
            for (var offset = 0; offset < value.Length; offset++)
            {
                bytes[address + (uint)offset] = value[offset];
            }
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void AssertNull(object? actual, string label)
    {
        if (actual is not null)
        {
            throw new InvalidOperationException($"{label}: expected null, got {actual}.");
        }
    }

    private static void AssertNotNull(object? actual, string label)
    {
        if (actual is null)
        {
            throw new InvalidOperationException($"{label}: expected a value, got null.");
        }
    }

    private static void AssertContains(string actual, string expected)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"expected \"{expected}\" within \"{actual}\".");
        }
    }
}
