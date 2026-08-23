using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using System.Buffers.Binary;

internal static class CondorBattleReaderTests
{
    internal static void Run()
    {
        // First, because it is about entering a battle at all: everything below
        // assumes the reader hands out snapshots, and this is the test that says
        // when it may and when it must not.
        KeepsSpeakingAfterTheGeometryCountStopsReading();
        ReadsBothSidesOfTheLiveUnitArray();
        ReadsTheNativeMovementAndCommandBytes();
        FailsClosedWhenAnyPartOfTheStateIsUnreadable();
        TreatsAUnitOutOfHpAsDyingRatherThanGone();
        ResolvesTheHighlightedHireRowThroughTheListRotation();
        ReadsEveryBlockingChoiceFromNativeState();
        ReadsStartAndDirectionChoicesAtTheirNativeWidths();
        RefusesUnsupportedChoiceIdentifiers();
        RefusesAChoiceThatChangesWhileItIsBeingRead();
        RefusesADestinationCursorThatChangesWhileItIsBeingRead();
        RefusesAReportWhoseTextureCellDoesNotMatchItsState();
        SpeaksTheAllyUnitCommandAndDestinationChoices();
        SpeaksTheStartDirectionAndCrowdedUnitChoices();
        SpeaksPlacementBeforeTheDirectionItNowRequires();
        SpeaksReportsPauseAndHelpOverlays();
        SpeaksTheVisibleGameSpeedWhenItChanges();
        SupersedesSpeechThatDescribesASelectionThePlayerAlreadyLeft();
        AnnouncesTheBannerMessagesTheGameDrawsAsPictures();
        SaysWhatTheEndingBannersMean();
        SpeaksTheResultFromTheGamesOwnLatch();
        SpeaksUnitsGoingDownDuringTheFight();
        DoesNotReportAPhaseChangeAsCasualties();
        AnchorsThePlacementScanToTheCursorRow();
        NamesTheEnemyTypesTheGameDraws();
        ReportsTheAdvanceGaugeTheGameDraws();
        SkipsARemovingUnitWhenDecidingWhatTheCursorIsOn();
        SpeaksTheHireListWithAffordability();
        SpeaksTheUnitUnderTheCursorAndWhenItClears();
        SpeaksWhereTheCursorStopsNotEveryRowItCrosses();
        EventsAreNeverCutShortByACursorMove();
        ReproducesTheNativePlacementRegionFromTheShippedTerrain();
        AppliesTheSetupBoundaryAndTheCombatFrontier();
        ExistingUnitsCutHolesInAPlacementBand();
        TreatsTheNativePlacementFlagAsUndefinedOutsideItsValidationWindow();
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

    private static void ReadsTheNativeMovementAndCommandBytes()
    {
        var memory = new CondorMemory();
        memory.WriteUnit(
            slot: 0,
            typeId: 2,
            currentHp: 180,
            maximumHp: 180,
            attack: 25,
            x: 256,
            y: 872,
            primaryActionState: 1,
            commandId: 3);

        var snapshot = new CondorBattleStateReader(memory).TryRead();
        AssertNotNull(snapshot, "snapshot with a directed unit");
        Equal(1, snapshot!.Units[0].PrimaryActionState, "unit +0x02 primary action state");
        Equal(3, snapshot.Units[0].CommandId, "unit +0x03 command id");
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

    private static void ReadsEveryBlockingChoiceFromNativeState()
    {
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 2, currentHp: 180, maximumHp: 180, attack: 25, x: 240, y: 500);
        memory.WriteUnit(slot: 3, typeId: 3, currentHp: 220, maximumHp: 220, attack: 35, x: 240, y: 500);

        memory.WriteInt32(CondorMemory.InteractionMode, CondorBattleSnapshot.AllyUnitInteractionMode);
        memory.WriteByte(CondorMemory.AllyUnitCommandCount, 2);
        memory.WriteInt16(CondorMemory.AllyUnitCommandRow, 1);
        memory.WriteByte(CondorMemory.AllyUnitCommand0, 3);
        memory.WriteByte(CondorMemory.AllyUnitCommand1, 0);

        var reader = new CondorBattleStateReader(memory);
        var commands = reader.TryRead();
        AssertNotNull(commands, "snapshot with the Ally Unit menu open");
        AssertNotNull(commands!.AllyUnitMenu, "Ally Unit menu state");
        Equal(2, commands.AllyUnitMenu!.CommandIds.Count, "Ally Unit row count");
        Equal(0, commands.AllyUnitMenu.HighlightedCommandId, "highlighted Ally Unit command");

        memory.WriteInt32(CondorMemory.InteractionMode, CondorBattleSnapshot.CursorInteractionMode);
        memory.WriteInt32(CondorMemory.ModalState, CondorBattleSnapshot.CrowdedUnitModalState);
        memory.WriteInt16(CondorMemory.CrowdedUnitCount, 2);
        memory.WriteInt16(CondorMemory.CrowdedUnitRow, 1);
        memory.WriteUInt32(CondorMemory.CrowdedUnitPointers, CondorMemory.LiveUnits);
        memory.WriteUInt32(
            CondorMemory.CrowdedUnitPointers + 8,
            CondorMemory.LiveUnits + (3u * CondorMemory.UnitStride));

        var crowded = reader.TryRead();
        AssertNotNull(crowded, "snapshot with the crowded-unit selector open");
        AssertNotNull(crowded!.CrowdedUnitMenu, "crowded-unit menu state");
        Equal(3, crowded.CrowdedUnitMenu!.HighlightedUnitSlot, "highlighted crowded-unit slot");

        // An active native menu with an impossible selection is not converted
        // into a plausible but wrong command. The next 100 ms sample may work;
        // the player cannot undo a command sent to the wrong unit.
        memory.WriteInt16(CondorMemory.CrowdedUnitRow, 2);
        AssertNull(reader.TryRead(), "crowded-unit row outside its candidate list");

        memory.WriteInt32(CondorMemory.ModalState, 0);
        memory.WriteInt32(CondorMemory.InteractionMode, CondorBattleSnapshot.DestinationInteractionMode);
        memory.WriteInt16(CondorMemory.DestinationX, 444);
        memory.WriteInt16(CondorMemory.DestinationY, 666);

        var destination = reader.TryRead();
        AssertNotNull(destination, "snapshot with the destination cursor active");
        Equal(444, destination!.DestinationX, "destination cursor X");
        Equal(666, destination.DestinationY, "destination cursor Y");
        Equal(2, destination.GameSpeed, "initial game speed drawn by module 9");

        memory.WriteInt16(CondorMemory.GameSpeed, 5);
        AssertNull(reader.TryRead(), "game speed outside the four levels the native input permits");
    }

    private static void ReadsStartAndDirectionChoicesAtTheirNativeWidths()
    {
        var memory = new CondorMemory();
        var reader = new CondorBattleStateReader(memory);

        memory.WriteInt32(CondorMemory.ModalState, CondorBattleSnapshot.StartGameModalState);
        memory.WriteInt16(CondorMemory.StartGameSelection, 0x10);
        Equal(0x10, reader.TryRead()!.StartGameSelection, "native Start Game No selection");

        memory.WriteInt16(CondorMemory.StartGameSelection, 1);
        AssertNull(reader.TryRead(), "Start Game selection outside the two native texture cells");

        memory.WriteInt32(CondorMemory.ModalState, CondorBattleSnapshot.NewUnitDirectionModalState);
        memory.WriteInt16(CondorMemory.DirectionSelection, 0x200);

        // 0x00C625D0 is a signed 16-bit selector. The two bytes before phase at
        // 0x00C625D4 are not part of it and may not be consumed as padding.
        memory.WriteByte(CondorMemory.DirectionSelection + 2, 0x7F);
        memory.WriteByte(CondorMemory.DirectionSelection + 3, 0x55);
        var newUnitDirection = reader.TryRead();
        AssertNotNull(newUnitDirection, "snapshot with native 16-bit direction selector");
        Equal(0x200, newUnitDirection!.DirectionSelection, "native 16-bit direction selector");

        memory.WriteInt32(CondorMemory.ModalState, CondorBattleSnapshot.CommandDirectionModalState);
        memory.WriteInt16(CondorMemory.DirectionSelection, 0x400);
        var existingUnitDirection = reader.TryRead();
        AssertNotNull(existingUnitDirection, "snapshot with existing-unit direction selector");
        Equal(0x400, existingUnitDirection!.DirectionSelection, "existing-unit direction endpoint");

        memory.WriteInt16(CondorMemory.DirectionSelection, 0x201);
        AssertNull(reader.TryRead(), "direction selector between its 0x20 steps");

        memory.WriteInt16(CondorMemory.DirectionSelection, 0x200);
        var changed = false;
        memory.AfterRead = address =>
        {
            if (!changed && address == CondorMemory.DirectionSelection)
            {
                changed = true;
                memory.WriteInt16(CondorMemory.DirectionSelection, 0x220);
            }
        };
        AssertNull(reader.TryRead(), "direction changing between its first and confirmation reads");

        var unreadableDirection = new CondorMemory();
        unreadableDirection.WriteInt32(
            CondorMemory.ModalState,
            CondorBattleSnapshot.CommandDirectionModalState);
        unreadableDirection.Unreadable.Add(CondorMemory.DirectionSelection);
        AssertNull(
            new CondorBattleStateReader(unreadableDirection).TryRead(),
            "unreadable active direction selector");

        var tornStart = new CondorMemory();
        tornStart.WriteInt32(CondorMemory.ModalState, CondorBattleSnapshot.StartGameModalState);
        tornStart.WriteInt16(CondorMemory.StartGameSelection, 0);
        var startChanged = false;
        tornStart.AfterRead = address =>
        {
            if (!startChanged && address == CondorMemory.StartGameSelection)
            {
                startChanged = true;
                tornStart.WriteInt16(CondorMemory.StartGameSelection, 0x10);
            }
        };
        AssertNull(
            new CondorBattleStateReader(tornStart).TryRead(),
            "Start Game row changing between its first and confirmation reads");

        var unreadableStart = new CondorMemory();
        unreadableStart.WriteInt32(CondorMemory.ModalState, CondorBattleSnapshot.StartGameModalState);
        unreadableStart.Unreadable.Add(CondorMemory.StartGameSelection);
        AssertNull(
            new CondorBattleStateReader(unreadableStart).TryRead(),
            "unreadable active Start Game selection");
    }

    private static void RefusesUnsupportedChoiceIdentifiers()
    {
        var commandMemory = new CondorMemory();
        commandMemory.WriteInt32(
            CondorMemory.InteractionMode,
            CondorBattleSnapshot.AllyUnitInteractionMode);
        commandMemory.WriteByte(CondorMemory.AllyUnitCommandCount, 1);
        commandMemory.WriteByte(CondorMemory.AllyUnitCommand0, 4);
        AssertNull(
            new CondorBattleStateReader(commandMemory).TryRead(),
            "command id that the native row constructor never emits");

        var reportMemory = new CondorMemory();
        reportMemory.WriteUnit(
            slot: 0,
            typeId: 2,
            currentHp: 180,
            maximumHp: 180,
            attack: 25,
            x: 240,
            y: 500);
        reportMemory.WriteInt16(CondorMemory.ReportState, 6);
        reportMemory.WriteInt16(CondorMemory.ReportMessageCell, 5);
        reportMemory.WriteInt16(CondorMemory.ReportUnitSlot, 0);
        AssertNull(
            new CondorBattleStateReader(reportMemory).TryRead(),
            "report texture cell with no native call site");
    }

    private static void SpeaksTheAllyUnitCommandAndDestinationChoices()
    {
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Battle());

        Equal(
            "Ally unit. Action. 1 of 2.",
            Single(tracker.Observe(Battle(
                interactionMode: CondorBattleSnapshot.AllyUnitInteractionMode,
                allyUnitMenu: new CondorAllyUnitMenu(0, [3, 0])))),
            "Ally Unit menu opening");
        Equal(
            "Bomb. 2 of 2.",
            Single(tracker.Observe(Battle(
                interactionMode: CondorBattleSnapshot.AllyUnitInteractionMode,
                allyUnitMenu: new CondorAllyUnitMenu(1, [3, 0])))),
            "Ally Unit menu row change");

        var directional = new CondorBattleSpeechTracker();
        directional.Observe(Battle());
        Equal(
            "Ally unit. Direction. 1 of 2.",
            Single(directional.Observe(Battle(
                interactionMode: CondorBattleSnapshot.AllyUnitInteractionMode,
                allyUnitMenu: new CondorAllyUnitMenu(0, [5, 2])))),
            "stationary-unit Direction command");

        Equal(
            "Choose destination. Cursor at 240, 500.",
            Single(tracker.Observe(Battle(
                interactionMode: CondorBattleSnapshot.DestinationInteractionMode,
                cursorX: 12,
                cursorY: 34,
                destinationX: 240,
                destinationY: 500))),
            "MOVE destination choice");

        // If module 9 is first observed with this menu already open, it belongs
        // in the accepted opening rather than becoming a silent baseline.
        var openedLate = new CondorBattleSpeechTracker().Observe(Battle(
            interactionMode: CondorBattleSnapshot.AllyUnitInteractionMode,
            allyUnitMenu: new CondorAllyUnitMenu(0, [2])));
        Equal(true, openedLate.Contains("Ally unit. Remove. 1 of 1."), "menu on the opening snapshot");
    }

    private static void RefusesAChoiceThatChangesWhileItIsBeingRead()
    {
        var memory = new CondorMemory();
        memory.WriteInt32(CondorMemory.InteractionMode, CondorBattleSnapshot.AllyUnitInteractionMode);
        memory.WriteByte(CondorMemory.AllyUnitCommandCount, 2);
        memory.WriteInt16(CondorMemory.AllyUnitCommandRow, 0);
        memory.WriteByte(CondorMemory.AllyUnitCommand0, 3);
        memory.WriteByte(CondorMemory.AllyUnitCommand1, 0);

        var changed = false;
        memory.AfterRead = address =>
        {
            if (!changed && address == CondorMemory.AllyUnitCommand1)
            {
                changed = true;
                memory.WriteInt16(CondorMemory.AllyUnitCommandRow, 1);
            }
        };

        AssertNull(
            new CondorBattleStateReader(memory).TryRead(),
            "Ally Unit row changing during the translated-memory read");
    }

    private static void RefusesADestinationCursorThatChangesWhileItIsBeingRead()
    {
        var memory = new CondorMemory();
        memory.WriteInt32(CondorMemory.InteractionMode, CondorBattleSnapshot.DestinationInteractionMode);
        memory.WriteInt16(CondorMemory.DestinationX, 240);
        memory.WriteInt16(CondorMemory.DestinationY, 500);

        var changed = false;
        memory.AfterRead = address =>
        {
            if (!changed && address == CondorMemory.DestinationY)
            {
                changed = true;
                memory.WriteInt16(CondorMemory.DestinationY, 504);
            }
        };

        AssertNull(
            new CondorBattleStateReader(memory).TryRead(),
            "destination cursor changing during the translated-memory read");
    }

    private static void RefusesAReportWhoseTextureCellDoesNotMatchItsState()
    {
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 2, currentHp: 180, maximumHp: 180, attack: 25, x: 240, y: 500);
        memory.WriteInt16(CondorMemory.ReportState, 1);
        memory.WriteInt16(CondorMemory.ReportMessageCell, 3);
        memory.WriteInt16(CondorMemory.ReportUnitSlot, 0);

        // FUN_006027C2 stores reportState = messageCell + 1. A disagreement is
        // an asynchronous read through the middle of that native update, not a
        // fourth report the game can show.
        AssertNull(
            new CondorBattleStateReader(memory).TryRead(),
            "report state paired with another texture cell");
    }

    private static void SpeaksTheStartDirectionAndCrowdedUnitChoices()
    {
        var start = new CondorBattleSpeechTracker();
        start.Observe(Battle());
        Equal(
            "Start the game? No. 2 of 2.",
            Single(start.Observe(Battle(
                modalState: CondorBattleSnapshot.StartGameModalState,
                messageId: 13,
                startGameSelection: 0x10))),
            "Start Game prompt opening");
        Equal(
            "Yes. 1 of 2.",
            Single(start.Observe(Battle(
                modalState: CondorBattleSnapshot.StartGameModalState,
                startGameSelection: 0))),
            "Start Game prompt row change");

        var direction = new CondorBattleSpeechTracker();
        direction.Observe(Battle());
        Equal(
            "Direction. Straight down. 17 of 33.",
            Single(direction.Observe(Battle(
                modalState: CondorBattleSnapshot.NewUnitDirectionModalState,
                directionSelection: 0x200))),
            "new-unit direction opening");
        Equal(
            "Direction. 3 degrees left of down. 18 of 33.",
            Single(direction.Observe(Battle(
                modalState: CondorBattleSnapshot.NewUnitDirectionModalState,
                directionSelection: 0x220))),
            "direction selection change");
        Equal(
            "Direction. 45 degrees right of down. 1 of 33.",
            Single(direction.Observe(Battle(
                modalState: CondorBattleSnapshot.NewUnitDirectionModalState,
                directionSelection: 0))),
            "right endpoint of the drawn direction arc");
        Equal(
            "Direction. 45 degrees left of down. 33 of 33.",
            Single(direction.Observe(Battle(
                modalState: CondorBattleSnapshot.NewUnitDirectionModalState,
                directionSelection: 0x400))),
            "left endpoint of the drawn direction arc");

        var units = new[]
        {
            Unit(slot: 0, x: 240, y: 500, typeId: 2),
            Unit(slot: 3, x: 240, y: 500, typeId: 3)
        };
        var crowded = new CondorBattleSpeechTracker();
        crowded.Observe(Battle(units: units));
        Equal(
            "Choose a unit. Attacker, 100 of 100, at 240, 500. 1 of 2.",
            Single(crowded.Observe(Battle(
                modalState: CondorBattleSnapshot.CrowdedUnitModalState,
                units: units,
                crowdedUnitMenu: new CondorCrowdedUnitMenu(0, [0, 3])))),
            "crowded-unit selector opening");
        Equal(
            "Defender, 100 of 100, at 240, 500. 2 of 2.",
            Single(crowded.Observe(Battle(
                modalState: CondorBattleSnapshot.CrowdedUnitModalState,
                units: units,
                crowdedUnitMenu: new CondorCrowdedUnitMenu(1, [0, 3])))),
            "crowded-unit selector row change");
    }

    private static void SpeaksPlacementBeforeTheDirectionItNowRequires()
    {
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Battle(
            modalState: CondorBattleSnapshot.SettingMenuModalState,
            alliedCount: 0));

        var lines = tracker.Observe(Battle(
            modalState: CondorBattleSnapshot.NewUnitDirectionModalState,
            alliedCount: 1,
            gil: 580,
            units: [Unit(slot: 0, x: 240, y: 500)],
            directionSelection: 0x200));

        Equal(1, lines.Count, "placement and its blocking direction prompt stay together");
        Equal(
            "Placed. 580 gil. Direction. Straight down. 17 of 33.",
            lines[0],
            "completed hire is spoken before the current blocking choice");
        Equal(
            true,
            tracker.LastObservationSupersedesSpeech,
            "non-lossy combined prompt replaces stale menu speech");
    }

    private static void SpeaksReportsPauseAndHelpOverlays()
    {
        var reportingUnit = Unit(slot: 0, x: 240, y: 500, typeId: 2);
        var report = new CondorBattleSpeechTracker();
        report.Observe(Battle(units: [reportingUnit]));
        Equal(
            "Report. Encountered enemy. Attacker, 100 of 100. " +
            "OK sends a command to this unit. Cancel lets it move freely.",
            Single(report.Observe(Battle(
                units: [reportingUnit],
                reportState: 1,
                messageId: 0,
                reportMessageCell: 0,
                reportUnitSlot: 0))),
            "actionable report");
        AssertContains(
            Single(report.Observe(Battle(
                units: [reportingUnit],
                reportState: 4,
                reportMessageCell: 3,
                reportUnitSlot: 0))),
            "Arrived at the directed position.");
        AssertContains(
            Single(report.Observe(Battle(
                units: [reportingUnit],
                reportState: 11,
                reportMessageCell: 10,
                reportUnitSlot: 0))),
            "Set units.");

        var pause = new CondorBattleSpeechTracker();
        pause.Observe(Battle());
        Equal("Paused.", Single(pause.Observe(Battle(modalState: 9))), "pause opening");
        Equal("Battle resumed.", Single(pause.Observe(Battle())), "pause closing");

        var help = new CondorBattleSpeechTracker();
        help.Observe(Battle());
        var helpLine = Single(help.Observe(Battle(modalState: 14)));
        AssertContains(helpLine, "Fort Condor help");
        AssertContains(helpLine, "OK opens Setting Menu");
        AssertContains(helpLine, "Page Up raises and Page Down lowers game speed");
    }

    private static void SpeaksTheVisibleGameSpeedWhenItChanges()
    {
        var tracker = new CondorBattleSpeechTracker();
        var opening = tracker.Observe(Battle(gameSpeed: 2));
        AssertContains(opening[0], "game speed 2 of 4");

        Equal(
            "Game speed 3 of 4.",
            Single(tracker.Observe(Battle(gameSpeed: 3))),
            "Page Up speed change");
        Equal(0, tracker.Observe(Battle(gameSpeed: 3)).Count, "unchanged game speed");
    }

    private static void SupersedesSpeechThatDescribesASelectionThePlayerAlreadyLeft()
    {
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Battle());

        tracker.Observe(Battle(
            interactionMode: CondorBattleSnapshot.AllyUnitInteractionMode,
            allyUnitMenu: new CondorAllyUnitMenu(0, [3, 0])));
        Equal(true, tracker.LastObservationSupersedesSpeech, "opening blocking menu replaces stale speech");

        var moved = tracker.Observe(Battle(
            interactionMode: CondorBattleSnapshot.AllyUnitInteractionMode,
            allyUnitMenu: new CondorAllyUnitMenu(1, [3, 0])));
        Equal("Bomb. 2 of 2.", Single(moved), "current Ally Unit row");
        Equal(true, tracker.LastObservationSupersedesSpeech, "new menu row replaces the old row");

        var eventAndChoice = tracker.Observe(Battle(
            modalState: CondorBattleSnapshot.StartGameModalState,
            messageId: 1,
            startGameSelection: 0));
        Equal(1, eventAndChoice.Count, "event and blocking choice form one non-lossy utterance");
        AssertContains(eventAndChoice[0], "Start combat.");
        AssertContains(eventAndChoice[0], "Start the game? Yes. 1 of 2.");
        Equal(true, tracker.LastObservationSupersedesSpeech, "combined event and choice is current speech");

        tracker.Observe(Battle(gameSpeed: 3));
        Equal(true, tracker.LastObservationSupersedesSpeech, "visible game-speed change replaces stale state");
    }

    private static void AnnouncesTheBannerMessagesTheGameDrawsAsPictures()
    {
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Battle(messageId: 12));

        // The entry reading returns the status line only, so the first reading
        // after it carries the cursor readout. Consumed here so the assertions
        // below are about banners and nothing else.
        tracker.Observe(Battle(messageId: 12));

        Equal(
            "Encountered enemy.",
            Single(tracker.Observe(Battle(messageId: 0))),
            "banner message on change");

        // The same identifier still standing is the same picture still on screen.
        Equal(0, tracker.Observe(Battle(messageId: 0)).Count, "banner message repeated");
        Equal("Enemy destroyed.", Single(tracker.Observe(Battle(messageId: 10))), "later banner message");
    }

    private static void SaysWhatTheEndingBannersMean()
    {
        // The banner is a caption, not a result. A player fought a whole battle
        // on 2026-08-21, heard "Enemy invasion.", and still had to ask whether
        // they had won - so the game's own words are kept and what they mean is
        // said with them.
        var won = new CondorBattleSpeechTracker();
        won.Observe(Battle());
        won.Observe(Battle());
        Equal(
            "Halted enemy attack! Battle won.",
            Single(won.Observe(Battle(messageId: 2))),
            "victory said with what it means");

        var lost = new CondorBattleSpeechTracker();
        lost.Observe(Battle());
        lost.Observe(Battle());
        Equal(
            "Enemy invasion. They reached the fort. Battle lost.",
            Single(lost.Observe(Battle(messageId: 7))),
            "defeat said with what it means");

        // A banner returning to the same identifier is the same picture back on
        // screen, not a second defeat.
        lost.Observe(Battle(messageId: 0));
        Equal(0, lost.Observe(Battle(messageId: 7)).Count, "the result is announced once");
    }

    private static void SpeaksTheResultFromTheGamesOwnLatch()
    {
        // 0x00CBEDC0 is the module's result latch and the game sets it before it
        // publishes the banner, so it is the earliest honest answer to "did I
        // win". One is the enemy stopped, two is the enemy reaching the fort.
        var logged = new List<string>();
        var lost = new CondorBattleSpeechTracker(logged.Add);
        lost.Observe(Battle());
        lost.Observe(Battle());
        Equal(
            "Enemy invasion. They reached the fort. Battle lost.",
            Single(lost.Observe(Battle(outcome: 2))),
            "defeat taken from the latch");

        // The banner is published from that same latch, so hearing it twice
        // would be the game saying it once and the mod saying it again.
        Equal(
            0,
            lost.Observe(Battle(outcome: 2, messageId: 7)).Count,
            "the banner does not repeat the result");
        Equal(
            1,
            logged.Count(line => line.Contains("result latch set to 2")),
            "the latch is written down as well as spoken");

        var won = new CondorBattleSpeechTracker();
        won.Observe(Battle());
        won.Observe(Battle());
        Equal(
            "Halted enemy attack! Battle won.",
            Single(won.Observe(Battle(outcome: 1))),
            "victory taken from the latch");
    }

    private static void SpeaksUnitsGoingDownDuringTheFight()
    {
        // Nothing in module 9 tells a blind player the fight is going badly. The
        // line thinning is what a sighted player is actually watching, and it is
        // the only warning there is before the enemy reaches the fort.
        var tracker = new CondorBattleSpeechTracker();
        var line = new[]
        {
            Unit(slot: 0, x: 200, y: 500),
            Unit(slot: 1, x: 240, y: 500),
            Unit(slot: 20, x: 200, y: 700, typeId: 17)
        };
        tracker.Observe(Battle(units: line, phase: 2));
        tracker.Observe(Battle(units: line, phase: 2));

        var afterLoss = tracker.Observe(Battle(units: [line[0], line[2]], phase: 2));
        Equal("Lost Attacker. 1 unit left.", Single(afterLoss), "an allied unit going down");

        // Named from the label the game draws for type 17, with the count the
        // banner never gives.
        var afterKill = tracker.Observe(Battle(units: [line[0]], phase: 2));
        Equal("Enemy Wyvern destroyed. 0 enemies left.", Single(afterKill), "an enemy going down");

        Equal(0, tracker.Observe(Battle(units: [line[0]], phase: 2)).Count, "a steady field");
    }

    private static void DoesNotReportAPhaseChangeAsCasualties()
    {
        var tracker = new CondorBattleSpeechTracker();
        var line = new[] { Unit(slot: 0, x: 200, y: 500), Unit(slot: 1, x: 240, y: 500) };
        tracker.Observe(Battle(units: line, phase: 1));
        tracker.Observe(Battle(units: line, phase: 1));

        // The live array is rebuilt when the battle changes phase. Reporting
        // that as two deaths would be a lie told loudly.
        Equal(
            0,
            tracker.Observe(Battle(units: [], phase: 2)).Count,
            "units cleared across a phase change");
    }

    private static void AnchorsThePlacementScanToTheCursorRow()
    {
        // In combat the cursor is not locked to the four-unit grid - it was
        // observed at 525, 761 and 937 in a real battle. A scan starting at zero
        // never lands on those rows, and every distance it reports is then off
        // by up to three, which is how "nearest placeable 7 down" reached a
        // player who can only move in fours.
        var terrain = LoadShippedCollisionTriangles();
        var odd = Battle(cursorX: 256, cursorY: 701, phase: 0, frontierY: 2000, terrain: terrain);
        var bands = odd.PlacementIntervals;

        Equal(true, bands.Count > 0, "the odd row is on terrain at all");
        Equal(true, bands.Any(band => band.Contains(701)), "the cursor's own row is scanned");
        foreach (var band in bands)
        {
            Equal(
                701 % CondorPlacementRegion.CursorStep,
                band.FromY % CondorPlacementRegion.CursorStep,
                "band start shares the cursor's row parity");
        }
    }

    private static void SpeaksTheHireListWithAffordability()
    {
        var tracker = new CondorBattleSpeechTracker();
        var ids = new[] { 1, 2, 3, 4, 12, 13, 5, 7 };
        tracker.Observe(Battle(ids, row: 0, modalState: 0, gil: 500));
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
        tracker.Observe(reader.TryRead()!);

        memory.WriteInt16(CondorMemory.UnitUnderCursor, 0);

        // The cursor's own position, not the unit's. Every line this readout
        // speaks opens with where the cursor is, so that the player never has to
        // work out whether the coordinates they just heard were their own or
        // something else's.
        var onUnit = reader.TryRead()!;
        Equal(
            $"{onUnit.CursorX}, {onUnit.CursorY}. Defender, 140 of 220.",
            Single(tracker.Observe(onUnit)),
            "unit under the cursor");

        // The native stat panel clears when the cursor leaves the unit, so what
        // the spot can take is said instead. Leaving the last unit standing as the
        // player's picture of where they are would be worse than saying nothing.
        memory.WriteInt16(CondorMemory.UnitUnderCursor, -1);
        AssertContains(
            Single(tracker.Observe(reader.TryRead()!)),
            CondorBattleSpeechTracker.CanPlaceText);
    }

    private static void KeepsSpeakingAfterTheGeometryCountStopsReading()
    {
        // The pre-load refusal must not become a gag once the battle is running.
        // The result banner - the single thing a player most needs out of Fort
        // Condor - is announced from a module 9 snapshot, so a collision count
        // that reads zero late in the battle has to leave the reader working. The
        // terrain is cached for the life of the battle and cannot go stale inside
        // one, and Reset drops it on the way out, so the cache is the safe answer.
        var memory = new CondorMemory();
        memory.WriteOpenGround();
        var reader = new CondorBattleStateReader(memory);

        AssertNotNull(reader.TryRead(), "snapshot once the battlefield has loaded");

        memory.WriteInt32(CondorMemory.CollisionCount, 0);
        var afterCountCleared = reader.TryRead();
        AssertNotNull(afterCountCleared, "snapshot after the geometry count stops reading");
        Equal(
            2,
            afterCountCleared!.CollisionTriangles.Count,
            "the battle's own terrain is kept rather than dropped");

        // A battle that never loaded geometry is still refused: that is the
        // pre-initialization state, and it is not a battle yet.
        var preLoadMemory = new CondorMemory();
        preLoadMemory.WriteInt32(CondorMemory.CollisionCount, 0);
        var neverLoaded = new CondorBattleStateReader(preLoadMemory);
        AssertNull(neverLoaded.TryRead(), "snapshot before any geometry has loaded");

        // And leaving the battle drops the cache, so the next module 9 cannot
        // inherit the last hill.
        reader.Reset();
        memory.WriteInt32(CondorMemory.CollisionCount, 0);
        AssertNull(reader.TryRead(), "snapshot after leaving the battle");
    }

    private static void EventsAreNeverCutShortByACursorMove()
    {
        // The cursor readout is allowed to replace what the reader is still
        // saying, because only the latest position is worth anything. An event is
        // not: a banner, a casualty or a result is said once, and interrupting it
        // loses it outright rather than merely delaying it. Without this, letting
        // every batch supersede passes the rest of the suite untouched.
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Battle(messageId: 12));
        tracker.Observe(Battle(messageId: 12));

        var banner = tracker.Observe(Battle(messageId: 0));
        Equal("Encountered enemy.", Single(banner), "banner line");
        Equal(
            false,
            tracker.LastObservationSupersedesSpeech,
            "a batch carrying an event never replaces speech");

        var result = tracker.Observe(Battle(messageId: 0, outcome: 2));
        AssertContains(Single(result), "Battle lost");
        Equal(
            false,
            tracker.LastObservationSupersedesSpeech,
            "a batch carrying the result never replaces speech");
    }

    private static void SpeaksWhereTheCursorStopsNotEveryRowItCrosses()
    {
        // Brice's 2026-08-22 placement phase: the readout used to speak only when
        // the ground under the cursor changed character, so sweeping inside one
        // placement band said nothing at all and he had no idea where the cursor
        // was for most of the phase. Position is what the readout exists to
        // deliver; whether a unit fits there is the qualifier on it.

        // The two phrases themselves, pinned. Every other assertion here refers to
        // the constants, so without this the words could be changed to anything at
        // all and the suite would still pass - and these are the words Brice asked
        // for by name.
        Equal("Can place", CondorBattleSpeechTracker.CanPlaceText, "the placeable wording");
        Equal("Cannot place", CondorBattleSpeechTracker.CannotPlaceText, "the denied wording");

        var memory = new CondorMemory();
        var reader = new CondorBattleStateReader(memory);
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(reader.TryRead()!);
        tracker.Observe(reader.TryRead()!);

        // Brice's 2026-08-22 evening session: the game's own held-key repeat
        // carries the cursor about twenty units per reading, so speaking every
        // reading queued sixteen announcements for two seconds of movement and
        // the speech ran on long after he let go. Rows crossed in transit are not
        // announced; where the cursor stops is.
        foreach (var y in new[] { 100, 160, 240, 320 })
        {
            memory.WriteInt16(CondorMemory.CursorY, (short)y);
            Equal(
                0,
                tracker.Observe(reader.TryRead()!).Count,
                $"cursor still travelling through row {y}");
        }

        // It comes to rest on the last of those rows, and that is what is said.
        var settled = reader.TryRead()!;
        Equal(
            $"{settled.CursorX}, 320. {CondorBattleSpeechTracker.CanPlaceText}.",
            Single(tracker.Observe(settled)),
            "cursor came to rest");
        Equal(
            true,
            tracker.LastObservationSupersedesSpeech,
            "a cursor-only batch replaces what is still being said");

        // Saying it again once it is already at rest tells the player nothing and
        // costs them the time it takes to say.
        Equal(0, tracker.Observe(reader.TryRead()!).Count, "cursor held still");

        // Ground the frontier will not allow reads as such, once the cursor has
        // stopped on it.
        memory.WriteInt32(CondorMemory.DeploymentFrontierY, 300);
        memory.WriteInt16(CondorMemory.CursorY, 400);
        tracker.Observe(reader.TryRead()!);
        AssertContains(
            Single(tracker.Observe(reader.TryRead()!)),
            CondorBattleSpeechTracker.CannotPlaceText);

        // And coming back inside it reads as such.
        memory.WriteInt16(CondorMemory.CursorY, 200);
        tracker.Observe(reader.TryRead()!);
        AssertContains(
            Single(tracker.Observe(reader.TryRead()!)),
            CondorBattleSpeechTracker.CanPlaceText);
    }

    private static void ReproducesTheNativePlacementRegionFromTheShippedTerrain()
    {
        // The real collision mesh, read out of the installed condor.lgp, checked
        // against the legal-row intervals the disassembly published for four
        // cursor columns.
        //
        // The game decides membership with fixed-point wedge angles and an
        // eight-unit tolerance out of a 4096-unit turn; this reproduces it with
        // an exact integer cross-product instead. These four audited columns
        // match at their edges and prove the region has holes, so a single
        // minimum and maximum would be false. They are not an exhaustive mesh
        // equivalence proof.
        var terrain = LoadShippedCollisionTriangles();
        Equal(333, terrain.Count, "collision triangle count");

        var expected = new Dictionary<int, (int From, int To)[]>
        {
            [128] = [(484, 544), (652, 732), (792, 904)],
            [256] = [(420, 1008)],
            [260] = [(420, 476), (552, 1008)],
            [320] = [(424, 460), (568, 716), (888, 1008)]
        };

        foreach (var (cursorX, bands) in expected)
        {
            // Combat phase with the frontier past the bottom of the map, so the
            // terrain is the only thing constraining the answer.
            var snapshot = Battle(cursorX: cursorX, phase: 0, frontierY: 2000, terrain: terrain);
            var actual = snapshot.PlacementIntervals;

            Equal(bands.Length, actual.Count, $"placement band count at X {cursorX}");
            for (var index = 0; index < bands.Length; index++)
            {
                Equal(bands[index].From, actual[index].FromY, $"band {index} start at X {cursorX}");
                Equal(bands[index].To, actual[index].ToY, $"band {index} end at X {cursorX}");
            }
        }
    }

    private static void AppliesTheSetupBoundaryAndTheCombatFrontier()
    {
        var terrain = LoadShippedCollisionTriangles();

        // During setup the executable refuses anything below a fixed line. The
        // cursor moves in four-unit steps, so the lowest row a player can
        // actually reach under it is 668.
        var setup = Battle(cursorX: 260, phase: CondorPlacementRegion.SetupPhase, terrain: terrain);
        var setupBands = setup.PlacementIntervals;
        Equal(2, setupBands.Count, "setup band count at X 260");
        Equal(420, setupBands[0].FromY, "setup first band start");
        Equal(476, setupBands[0].ToY, "setup first band end");
        Equal(552, setupBands[1].FromY, "setup second band start");
        Equal(668, setupBands[1].ToY, "setup second band end");

        // Once the battle is running the limit becomes a frontier that starts at
        // 480 and moves down as the allied units advance, so the ground a player
        // may build on genuinely grows during a battle.
        var earlyCombat = Battle(cursorX: 256, phase: 0, frontierY: 480, terrain: terrain);
        Equal(476, earlyCombat.PlacementIntervals[^1].ToY, "combat limit at the opening frontier");

        var advanced = Battle(cursorX: 256, phase: 0, frontierY: 928, terrain: terrain);
        Equal(924, advanced.PlacementIntervals[^1].ToY, "combat limit at the furthest frontier");
    }

    private static void ExistingUnitsCutHolesInAPlacementBand()
    {
        // A unit denies more ground than the square it stands on, and the game
        // keeps that ground denied until the slot is released. Reporting a band
        // without its holes would send a player to spend gil somewhere the
        // confirm does nothing at all.
        var terrain = LoadShippedCollisionTriangles();
        var clear = Battle(cursorX: 256, phase: 0, frontierY: 2000, terrain: terrain);
        Equal(1, clear.PlacementIntervals.Count, "band count with an empty field");

        var occupied = Battle(
            cursorX: 256, phase: 0, frontierY: 2000, terrain: terrain,
            units: [Unit(slot: 0, x: 256, y: 700)]);
        Equal(true, occupied.PlacementIntervals.Count > 1, "a unit splits the band");
        Equal(
            false,
            occupied.PlacementIntervals.Any(interval => interval.Contains(700)),
            "the row the unit stands on is not offered");
    }

    private static void TreatsTheNativePlacementFlagAsUndefinedOutsideItsValidationWindow()
    {
        var terrain = LoadShippedCollisionTriangles();
        var tracker = new CondorBattleSpeechTracker();
        var ordinary = Battle(
            cursorX: 256, cursorY: 500, phase: 0, frontierY: 2000,
            placementLegal: true, terrain: terrain);
        tracker.Observe(ordinary);
        tracker.Observe(ordinary);

        // FUN_005FD958 does not clear or recompute 0x00CBCC9C while the report
        // overlay owns input. It retains the last answer, so comparing it with
        // the report-gated managed predicate would manufacture a disagreement
        // and make an unchanged piece of ground sound blocked.
        var report = Battle(
            cursorX: 256, cursorY: 500, phase: 0, frontierY: 2000,
            placementLegal: true, reportState: 1, reportMessageCell: 0, terrain: terrain);
        var reportLines = tracker.Observe(report);
        Equal(1, reportLines.Count, "report overlay speaks its own actionable information");
        Equal(
            false,
            reportLines[0].Contains("place", StringComparison.OrdinalIgnoreCase),
            "report overlay does not narrate stale placement");
        Equal(0, tracker.PlacementDisagreements, "report overlay is not a geometry disagreement");

        // The async reader fetches the flag before the unit array. A hire can
        // therefore finish between those reads: old clear-ground flag, new unit
        // occupying that ground. This is a mixed event snapshot, not a failed
        // reproduction of the native predicate.
        tracker.Observe(Battle(
            modalState: CondorBattleSnapshot.SettingMenuModalState,
            cursorX: 256, cursorY: 500, phase: 0, frontierY: 2000,
            placementLegal: true, terrain: terrain));
        tracker.Observe(Battle(
            cursorX: 256, cursorY: 500, phase: 0, frontierY: 2000,
            placementLegal: true, alliedCount: 1, terrain: terrain,
            units: [Unit(slot: 0, x: 256, y: 500)]));
        Equal(0, tracker.PlacementDisagreements, "completed hire is not a geometry disagreement");
    }

    private static IReadOnlyList<CondorCollisionTriangle> LoadShippedCollisionTriangles()
    {
        var archive = new LgpArchiveReader(
            Path.Combine(FindRuntimeRoot(), "data", "minigame", "condor.lgp"));
        if (!archive.TryReadFile("vert.bin", out var vertices))
        {
            throw new InvalidOperationException("condor.lgp does not contain vert.bin.");
        }

        const int stride = 0x4C;
        var triangles = new List<CondorCollisionTriangle>();
        for (var offset = 0; offset + stride <= vertices.Length; offset += stride)
        {
            var span = vertices.AsSpan(offset, stride);
            triangles.Add(new CondorCollisionTriangle(
                BitConverter.ToInt16(span[0x28..]), BitConverter.ToInt16(span[0x2A..]),
                BitConverter.ToInt16(span[0x30..]), BitConverter.ToInt16(span[0x32..]),
                BitConverter.ToInt16(span[0x38..]), BitConverter.ToInt16(span[0x3A..]),
                BitConverter.ToInt16(span[0x40..]) - 0x4000,
                BitConverter.ToInt16(span[0x42..]) - 0x4000,
                BitConverter.ToInt16(span[0x44..]) - 0x4000,
                BitConverter.ToInt16(span[0x46..]) - 0x4000));
        }

        return triangles;
    }

    private static string FindRuntimeRoot()
    {
        var configured = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_RUNTIME");
        if (!string.IsNullOrWhiteSpace(configured) &&
            Directory.Exists(Path.Combine(configured, "data", "minigame")))
        {
            return configured;
        }

        throw new InvalidOperationException(
            "FF7_ACCESSIBILITY_RUNTIME must name an FFVII runtime containing data/minigame.");
    }

    private static CondorBattleUnit Unit(
        int slot, int x, int y, int width = 22, int heightAbove = 26, int typeId = 2,
        bool removing = false) =>
        new(slot, slot >= 20, typeId, 100, 100, 20, x, y, false, width, heightAbove, removing);

    private static void NamesTheEnemyTypesTheGameDraws()
    {
        // The executable picks region 0x5F + typeId from emes01.tex. The atlas
        // is four columns of six cells, so this pins every cell in native id
        // order, including the Japanese "dummy" placeholders the English game
        // itself draws for unused ids.
        var expected = new[]
        {
            "Dummy", "Fighter", "Attacker", "Defender", "Shooter", "Stoner",
            "Tristoner", "Catapult", "Fire Catapult", "Dummy", "Dummy", "Dummy",
            "Repairer", "Worker", "Dummy", "Dummy", "Commander", "Wyvern",
            "Beast", "Barbarian", "Dummy", "Dummy", "Dummy", "Dummy"
        };

        for (var typeId = 0; typeId < expected.Length; typeId++)
        {
            Equal(expected[typeId], CondorUnitCatalog.ResolveName(typeId), $"drawn label for type {typeId}");
        }

        Equal("enemy Dummy", Unit(slot: 20, x: 0, y: 0, typeId: 11).Name, "dummy enemy label keeps its side");
        Equal("enemy unit", Unit(slot: 20, x: 0, y: 0, typeId: 24).Name, "out-of-atlas type remains unnamed");
    }

    private static void ReportsTheAdvanceGaugeTheGameDraws()
    {
        // The game derives this from the leading enemy's position and draws it
        // as a row of segments that is on screen all battle. It is the one thing
        // a sighted player can glance at to know they are losing.
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Battle(enemyAdvance: 0));
        tracker.Observe(Battle(enemyAdvance: 0));

        Equal(0, tracker.Observe(Battle(enemyAdvance: 20)).Count, "still inside the first quarter");
        Equal(
            "Enemy advance a quarter.",
            Single(tracker.Observe(Battle(enemyAdvance: 24))),
            "the first quarter");
        Equal(
            "Enemy advance halfway.",
            Single(tracker.Observe(Battle(enemyAdvance: 50))),
            "halfway");
        Equal(
            "Enemies at the fort.",
            Single(tracker.Observe(Battle(enemyAdvance: 96))),
            "the gauge full");

        // Driving them back is worth hearing as much as losing ground is.
        Equal(
            "Enemy advance halfway.",
            Single(tracker.Observe(Battle(enemyAdvance: 48))),
            "pushed back down the gauge");
    }

    private static void SkipsARemovingUnitWhenDecidingWhatTheCursorIsOn()
    {
        // The game runs two scans over the live units and they disagree on
        // purpose. The footprint scan stops at slot 38 and counts units that are
        // playing their removal animation; the hit-box scan covers all forty and
        // skips them. Slot 39 is therefore the only place the difference shows,
        // and getting it wrong reports ground as blocked that the game accepts.
        var terrain = LoadShippedCollisionTriangles();

        var standing = Battle(
            cursorX: 256, cursorY: 700, phase: 0, frontierY: 2000, terrain: terrain,
            units: [Unit(slot: 39, x: 256, y: 700)]);
        Equal(
            false,
            CondorPlacementRegion.IsLegalAt(standing, 256, 700),
            "a live unit in slot 39 blocks the cursor");

        var removing = Battle(
            cursorX: 256, cursorY: 700, phase: 0, frontierY: 2000, terrain: terrain,
            units: [Unit(slot: 39, x: 256, y: 700, removing: true)]);
        Equal(
            true,
            CondorPlacementRegion.IsLegalAt(removing, 256, 700),
            "a unit in its removal animation in slot 39 does not");

        // Below slot 39 the footprint scan still counts it, exactly as the game
        // does, so the removal state changes nothing there.
        var lowSlot = Battle(
            cursorX: 256, cursorY: 700, phase: 0, frontierY: 2000, terrain: terrain,
            units: [Unit(slot: 5, x: 256, y: 700, removing: true)]);
        Equal(
            false,
            CondorPlacementRegion.IsLegalAt(lowSlot, 256, 700),
            "a removing unit below slot 39 still blocks");
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
        // The allied unit stands exactly where the cursor is, so the spot is
        // denied and the status says so in the same two words the cursor readout
        // uses. The nearest usable row, the extent of the band and the count of
        // remaining bands were dropped on 2026-08-22 at Brice's direction: they
        // buried the coordinates the line exists to deliver.
        // The advance gauge closes the line because the game draws it for the
        // whole battle; a glance takes it in whether or not it just moved.
        // Every position is a coordinate, not a bearing. A bearing is only true
        // until the cursor moves, and this line is most useful precisely when the
        // player is about to move it.
        Equal(
            "9436 gil. 1 unit. 4 enemies. cursor at 240, 500. " +
            $"{CondorBattleSpeechTracker.CannotPlaceText}. " +
            "nearest enemy Dummy, 120 of 200, at 240, 620. game speed 2 of 4. no enemy advance.",
            unsettled,
            "status line with the cursor on an occupied row");
    }

    private static void NamesOnlyUnitTypesThatHaveBeenProved()
    {
        // All 24 atlas cells are tied to their names. A value outside that
        // table is still described by side and logged once rather than guessed.
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 20, typeId: 24, currentHp: 200, maximumHp: 200, attack: 30, x: 100, y: 100);
        var snapshot = new CondorBattleStateReader(memory).TryRead();
        AssertNotNull(snapshot, "snapshot with an unnamed type");
        Equal("enemy unit", snapshot!.Units[0].Name, "unnamed enemy type");

        var logged = new List<string>();
        var tracker = new CondorBattleSpeechTracker(logged.Add);
        tracker.Observe(snapshot);
        tracker.Observe(snapshot);
        tracker.Observe(snapshot);
        var unnamed = logged.Where(line => line.Contains("unnamed unit type")).ToList();
        Equal(1, unnamed.Count, "unnamed type reported once, not once per snapshot");
        AssertContains(unnamed[0], "unnamed unit type 24");

        var namedMemory = new CondorMemory();
        namedMemory.WriteUnit(
            slot: 20, typeId: 16, currentHp: 200, maximumHp: 200,
            attack: 30, x: 100, y: 100);
        var namedLog = new List<string>();
        new CondorBattleSpeechTracker(namedLog.Add).Observe(
            new CondorBattleStateReader(namedMemory).TryRead()!);
        Equal(
            0,
            namedLog.Count(line => line.Contains("unnamed unit type")),
            "an atlas-named non-hireable type is not logged as unnamed");

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
        int interactionMode = CondorBattleSnapshot.CursorInteractionMode,
        int gil = 1000,
        int messageId = -1,
        int outcome = 0,
        int cursorX = 0,
        int cursorY = 0,
        bool placementLegal = true,
        int phase = 0,
        int frontierY = 2000,
        int reportState = 0,
        int alliedCount = 0,
        int enemyAdvance = 0,
        IReadOnlyList<CondorCollisionTriangle>? terrain = null,
        IReadOnlyList<CondorBattleUnit>? units = null,
        CondorAllyUnitMenu? allyUnitMenu = null,
        int startGameSelection = 0,
        CondorCrowdedUnitMenu? crowdedUnitMenu = null,
        int directionSelection = 0,
        int reportMessageCell = -1,
        int reportUnitSlot = -1,
        int? destinationX = null,
        int? destinationY = null,
        int gameSpeed = 2) =>
        new CondorBattleSnapshot(
            InteractionMode: interactionMode,
            ModalState: modalState,
            SettingMenuRow: row,
            SettingMenuRotation: rotation,
            AvailableTypeIds: availableTypeIds ?? [],
            Gil: gil,
            CursorX: cursorX,
            CursorY: cursorY,
            CursorPlacementLegal: placementLegal,
            UnitUnderCursorSlot: -1,
            Units: units ?? [],
            AlliedCount: alliedCount,
            EnemyCount: 0,
            Outcome: outcome,
            MessageId: messageId,
            Phase: phase,
            ReportState: reportState,
            DeploymentFrontierY: frontierY,
            EnemyAdvance: enemyAdvance,
            CollisionTriangles: terrain ?? [],
            AllyUnitMenu: allyUnitMenu,
            StartGameSelection: startGameSelection,
            CrowdedUnitMenu: crowdedUnitMenu,
            DirectionSelection: directionSelection,
            ReportMessageCell: reportMessageCell,
            ReportUnitSlot: reportUnitSlot)
        {
            DestinationX = destinationX ?? cursorX,
            DestinationY = destinationY ?? cursorY,
            GameSpeed = gameSpeed
        };

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
        internal const uint AllyUnitCommandRow = 0x00CBC930;
        internal const uint AllyUnitCommandCount = 0x00C752D4;
        internal const uint AllyUnitCommand0 = 0x00C74CA8;
        internal const uint AllyUnitCommand1 = 0x00C74CB0;
        internal const uint AllyUnitCommand2 = 0x00C74CB8;
        internal const uint CrowdedUnitPointers = 0x00C60980;
        internal const uint CrowdedUnitCount = 0x00C61BF4;
        internal const uint CrowdedUnitRow = 0x00C74C68;
        internal const uint StartGameSelection = 0x00CBC7D8;
        internal const uint DirectionSelection = 0x00C625D0;
        internal const uint ReportState = 0x00C72DEC;
        internal const uint ReportMessageCell = 0x00C60AC4;
        internal const uint ReportUnitSlot = 0x00C72E3C;
        internal const uint SettingMenuRow = 0x00CBCCA0;
        internal const uint SettingMenuRotation = 0x00C75254;
        internal const uint SettingMenuCount = 0x00C75264;
        internal const uint AvailableTypeIds = 0x00C75278;
        internal const uint Gil = 0x00CBC7E0;
        internal const uint CursorX = 0x00CBCCC0;
        internal const uint CursorY = 0x00CBCCC2;
        internal const uint DestinationX = 0x00C75268;
        internal const uint DestinationY = 0x00C7526A;
        internal const uint GameSpeed = 0x00C752B4;
        internal const uint CursorPlacementLegal = 0x00CBCC9C;
        internal const uint UnitUnderCursor = 0x00C6097C;
        internal const uint LiveUnits = 0x00CBCCD8;
        internal const uint Phase = 0x00C625D4;
        internal const uint DeploymentFrontierY = 0x00C60AE8;
        internal const uint CollisionCount = 0x00C60AA4;
        internal const uint CollisionRecords = 0x00C625E8;
        internal const int CollisionStride = 0x4C;
        internal const uint AlliedCount = 0x00C60AD0;
        internal const uint EnemyCount = 0x00CBC7A4;
        internal const int UnitStride = 0x78;

        private readonly Dictionary<uint, byte> bytes = [];

        internal HashSet<uint> Unreadable { get; } = [];
        internal Action<uint>? AfterRead { get; set; }

        internal CondorMemory()
        {
            // Nothing under the cursor unless a test says otherwise; the native
            // value for that is -1, not 0, and 0 is a real slot.
            WriteInt16(UnitUnderCursor, -1);

            // An ordinary battle with the player moving the battlefield cursor.
            // Zero is not a mode the game uses, so leaving it unset would make
            // every cursor test pass by never reaching the cursor at all.
            WriteInt32(InteractionMode, CondorBattleSnapshot.CursorInteractionMode);
            WriteInt16(GameSpeed, 2);

            // Open ground over the whole map, and a deployment frontier past the
            // bottom of it. Without terrain nothing is placeable anywhere, and a
            // test about what the cursor says would pass by never getting there.
            WriteOpenGround();
            WriteInt32(DeploymentFrontierY, 2000);
        }

        internal void WriteUnit(
            int slot,
            int typeId,
            int currentHp,
            int maximumHp,
            int attack,
            int x,
            int y,
            sbyte removalState = 0,
            byte primaryActionState = 0,
            byte commandId = 0)
        {
            var unit = LiveUnits + (uint)(slot * UnitStride);
            WriteUInt16(unit + 0x00, 1);
            bytes[unit + 0x02] = primaryActionState;
            bytes[unit + 0x03] = commandId;
            bytes[unit + 0x05] = (byte)removalState;
            WriteUInt16(unit + 0x06, (ushort)typeId);
            bytes[unit + 0x10] = (byte)currentHp;
            bytes[unit + 0x11] = (byte)maximumHp;
            bytes[unit + 0x12] = (byte)attack;
            bytes[unit + 0x22] = 22;
            bytes[unit + 0x23] = 26;
            WriteInt16(unit + 0x48, (short)x);
            WriteInt16(unit + 0x4A, (short)y);
        }

        /// <summary>
        /// Two triangles covering the whole battlefield, so every row of every
        /// column is on terrain unless a test puts something in the way.
        /// </summary>
        internal void WriteOpenGround()
        {
            var corners = new[] { (-600, -700), (600, -700), (600, 700), (-600, 700) };
            WriteCollisionTriangle(0, corners[0], corners[1], corners[2]);
            WriteCollisionTriangle(1, corners[0], corners[2], corners[3]);
            WriteInt32(CollisionCount, 2);
        }

        internal void WriteCollisionTriangle(
            int index, (int X, int Y) a, (int X, int Y) b, (int X, int Y) c)
        {
            var record = CollisionRecords + (uint)(index * CollisionStride);
            WriteInt16(record + 0x28, (short)a.X);
            WriteInt16(record + 0x2A, (short)a.Y);
            WriteInt16(record + 0x30, (short)b.X);
            WriteInt16(record + 0x32, (short)b.Y);
            WriteInt16(record + 0x38, (short)c.X);
            WriteInt16(record + 0x3A, (short)c.Y);

            // The record carries its own inclusive bounds, biased by 0x4000, and
            // the game applies them before the triangle test.
            WriteInt16(record + 0x40, (short)(0x4000 + Math.Min(a.X, Math.Min(b.X, c.X))));
            WriteInt16(record + 0x42, (short)(0x4000 + Math.Max(a.X, Math.Max(b.X, c.X))));
            WriteInt16(record + 0x44, (short)(0x4000 + Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            WriteInt16(record + 0x46, (short)(0x4000 + Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        }

        internal void WriteInt32(uint address, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            Store(address, buffer);
        }

        internal void WriteUInt32(uint address, uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            Store(address, buffer);
        }

        internal void WriteByte(uint address, byte value) => bytes[address] = value;

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

            AfterRead?.Invoke(virtualAddress);
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
