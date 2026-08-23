using Ff7.Accessibility.Reloaded;

internal static class CondorUnitMovementSpeechTests
{
    internal static void Run()
    {
        OrderedMoveSpeaksOnceAndSilencesTheCarriedCursor();
        OrderedMoveWaitsToAcknowledgeUntilTheCursorFollowsTheUnit();
        AnUncommandedMovingUnitUsesTheFullSurveyReadout();
        ManualSurveyKeepsCoordinatesAndSaysWhenAUnitStops();
        ManualPlacementStillSaysCannotPlaceDuringAnOrder();
        ArrivalIsTheOnlySuccessfulOrderEnding();
        AnInterruptedOrderEndsInSpeech();
    }

    private static void OrderedMoveWaitsToAcknowledgeUntilTheCursorFollowsTheUnit()
    {
        var tracker = StartedWith(
            Unit(x: 256, y: 870),
            cursorX: 256,
            cursorY: 895,
            unitUnderCursorSlot: -1);

        var ordered = Unit(x: 256, y: 870, primaryActionState: 1, commandId: 3);
        Equal(
            0,
            tracker.Observe(Battle(
                ordered,
                cursorX: 256,
                cursorY: 895,
                unitUnderCursorSlot: -1)).Count,
            "the Action byte can become visible before the cursor-follow store");

        Equal(
            "Moving.",
            Single(tracker.Observe(Battle(
                ordered,
                cursorX: 256,
                cursorY: 870,
                unitUnderCursorSlot: 0))),
            "the pending order is acknowledged when the game starts following its unit");

        Equal(
            0,
            tracker.Observe(Battle(
                Unit(x: 256, y: 872, primaryActionState: 1, commandId: 3),
                cursorX: 256,
                cursorY: 872,
                unitUnderCursorSlot: 0)).Count,
            "the delayed acknowledgement is still spoken only once");
    }

    private static void OrderedMoveSpeaksOnceAndSilencesTheCarriedCursor()
    {
        var tracker = StartedWith(
            Unit(x: 256, y: 870),
            cursorX: 256,
            cursorY: 870,
            unitUnderCursorSlot: 0);

        Equal(
            "Moving.",
            Single(tracker.Observe(Battle(
                Unit(x: 256, y: 870, primaryActionState: 1, commandId: 3),
                cursorX: 256,
                cursorY: 870,
                unitUnderCursorSlot: 0))),
            "the Action order starts once");
        Equal(
            true,
            tracker.LastObservationSupersedesSpeech,
            "the current movement state replaces a stale destination coordinate");

        foreach (var y in new[] { 872, 874, 876, 878 })
        {
            var carried = Battle(
                Unit(x: 256, y: y, primaryActionState: 1, commandId: 3),
                cursorX: 256,
                cursorY: y,
                unitUnderCursorSlot: 0);

            Equal(0, tracker.Observe(carried).Count, $"carried cursor moving through {y}");
            Equal(0, tracker.Observe(carried).Count, $"carried cursor pausing at {y}");
        }
    }

    private static void AnUncommandedMovingUnitUsesTheFullSurveyReadout()
    {
        var tracker = StartedWith(
            Unit(x: 256, y: 870),
            cursorX: 256,
            cursorY: 870,
            unitUnderCursorSlot: 0);

        Equal(
            "256, 870. Attacker, 180 of 180. Moving.",
            Single(tracker.Observe(Battle(
                Unit(x: 256, y: 870, primaryActionState: 1, commandId: 0),
                cursorX: 256,
                cursorY: 870,
                unitUnderCursorSlot: 0))),
            "motion not caused by a new Action order keeps the full readout");
    }

    private static void ManualSurveyKeepsCoordinatesAndSaysWhenAUnitStops()
    {
        var moving = Unit(x: 256, y: 872, primaryActionState: 1, commandId: 0);
        var tracker = StartedWith(
            moving,
            cursorX: 200,
            cursorY: 800,
            unitUnderCursorSlot: -1);

        Equal(
            0,
            tracker.Observe(Battle(
                moving,
                cursorX: 256,
                cursorY: 872,
                unitUnderCursorSlot: 0,
                heldDirectionMask: 0x1000)).Count,
            "manual cursor still travelling onto the unit");

        Equal(
            "256, 872. Attacker, 180 of 180. Moving.",
            Single(tracker.Observe(Battle(
                moving,
                cursorX: 256,
                cursorY: 872,
                unitUnderCursorSlot: 0))),
            "manual survey names the unit before saying it is moving");

        var stopped = Unit(x: 256, y: 872, primaryActionState: 0, commandId: 0);
        Equal(
            0,
            tracker.Observe(Battle(
                stopped,
                cursorX: 256,
                cursorY: 872,
                unitUnderCursorSlot: 0)).Count,
            "one nonmoving sample cannot declare a stop");
        Equal(
            "256, 872. Attacker, 180 of 180. Stopped.",
            Single(tracker.Observe(Battle(
                stopped,
                cursorX: 256,
                cursorY: 872,
                unitUnderCursorSlot: 0))),
            "a surveyed unit's confirmed stop");
        Equal(
            0,
            tracker.Observe(Battle(
                stopped,
                cursorX: 256,
                cursorY: 872,
                unitUnderCursorSlot: 0)).Count,
            "the stop is not repeated");
    }

    private static void ManualPlacementStillSaysCannotPlaceDuringAnOrder()
    {
        var tracker = StartedWith(
            Unit(x: 256, y: 870),
            cursorX: 256,
            cursorY: 870,
            unitUnderCursorSlot: 0);

        var ordered = Unit(x: 256, y: 870, primaryActionState: 1, commandId: 3);
        Equal(
            "Moving.",
            Single(tracker.Observe(Battle(
                ordered,
                cursorX: 256,
                cursorY: 870,
                unitUnderCursorSlot: 0))),
            "ordered movement starts");

        Equal(
            0,
            tracker.Observe(Battle(
                ordered,
                cursorX: 220,
                cursorY: 820,
                unitUnderCursorSlot: -1,
                heldDirectionMask: 0x4000)).Count,
            "manual cursor leaving the moving unit");

        Equal(
            "220, 820. Cannot place.",
            Single(tracker.Observe(Battle(
                ordered,
                cursorX: 220,
                cursorY: 820,
                unitUnderCursorSlot: -1))),
            "manual placement readout remains available during the order");
    }

    private static void ArrivalIsTheOnlySuccessfulOrderEnding()
    {
        var tracker = StartedWith(
            Unit(x: 256, y: 870),
            cursorX: 256,
            cursorY: 870,
            unitUnderCursorSlot: 0);

        tracker.Observe(Battle(
            Unit(x: 256, y: 870, primaryActionState: 1, commandId: 3),
            cursorX: 256,
            cursorY: 870,
            unitUnderCursorSlot: 0));
        tracker.Observe(Battle(
            Unit(x: 256, y: 895, primaryActionState: 1, commandId: 3),
            cursorX: 256,
            cursorY: 895,
            unitUnderCursorSlot: 0));

        var arrivedUnit = Unit(x: 256, y: 895, primaryActionState: 0, commandId: 3);
        var arrival = Single(tracker.Observe(Battle(
            arrivedUnit,
            cursorX: 256,
            cursorY: 895,
            unitUnderCursorSlot: 0,
            messageId: 3,
            reportState: 4,
            reportMessageCell: 3,
            reportUnitSlot: 0)));
        Contains(arrival, "Arrived at the directed position.", "native arrival report");
        DoesNotContain(arrival, "Stopped", "arrival does not get a second stop line");

        Equal(
            0,
            tracker.Observe(Battle(
                arrivedUnit,
                cursorX: 256,
                cursorY: 895,
                unitUnderCursorSlot: 0)).Count,
            "closing the arrival report does not repeat the stopped unit");
    }

    private static void AnInterruptedOrderEndsInSpeech()
    {
        var tracker = StartedWith(
            Unit(x: 256, y: 870),
            cursorX: 256,
            cursorY: 870,
            unitUnderCursorSlot: 0);

        tracker.Observe(Battle(
            Unit(x: 256, y: 870, primaryActionState: 1, commandId: 3),
            cursorX: 256,
            cursorY: 870,
            unitUnderCursorSlot: 0));
        tracker.Observe(Battle(
            Unit(x: 256, y: 880, primaryActionState: 1, commandId: 3),
            cursorX: 256,
            cursorY: 880,
            unitUnderCursorSlot: 0));

        var interrupted = Unit(x: 256, y: 880, primaryActionState: 0, commandId: 3);
        Equal(
            0,
            tracker.Observe(Battle(
                interrupted,
                cursorX: 256,
                cursorY: 880,
                unitUnderCursorSlot: 0)).Count,
            "one inactive sample cannot end an order");
        Equal(
            "Movement stopped at 256, 880.",
            Single(tracker.Observe(Battle(
                interrupted,
                cursorX: 256,
                cursorY: 880,
                unitUnderCursorSlot: 0))),
            "an interrupted order cannot end silently");
        Equal(
            0,
            tracker.Observe(Battle(
                interrupted,
                cursorX: 256,
                cursorY: 880,
                unitUnderCursorSlot: 0)).Count,
            "the interruption is not repeated");
    }

    private static CondorBattleSpeechTracker StartedWith(
        CondorBattleUnit unit,
        int cursorX,
        int cursorY,
        int unitUnderCursorSlot)
    {
        var tracker = new CondorBattleSpeechTracker();
        var initial = Battle(
            unit,
            cursorX,
            cursorY,
            unitUnderCursorSlot);
        tracker.Observe(initial);
        tracker.Observe(initial);
        return tracker;
    }

    private static CondorBattleUnit Unit(
        int x,
        int y,
        int primaryActionState = 0,
        int commandId = 0) =>
        new(
            Slot: 0,
            IsEnemy: false,
            TypeId: 2,
            CurrentHp: 180,
            MaximumHp: 180,
            Attack: 25,
            X: x,
            Y: y,
            IsDying: false,
            Width: 22,
            HeightAbove: 26,
            IsRemoving: false,
            PrimaryActionState: primaryActionState,
            CommandId: commandId);

    private static CondorBattleSnapshot Battle(
        CondorBattleUnit unit,
        int cursorX,
        int cursorY,
        int unitUnderCursorSlot,
        uint heldDirectionMask = 0,
        int messageId = -1,
        int reportState = 0,
        int reportMessageCell = -1,
        int reportUnitSlot = -1) =>
        new(
            InteractionMode: CondorBattleSnapshot.CursorInteractionMode,
            ModalState: 0,
            SettingMenuRow: 0,
            SettingMenuRotation: 0,
            AvailableTypeIds: [],
            Gil: 9436,
            CursorX: cursorX,
            CursorY: cursorY,
            CursorPlacementLegal: false,
            UnitUnderCursorSlot: unitUnderCursorSlot,
            Units: [unit],
            AlliedCount: 1,
            EnemyCount: 0,
            Outcome: 0,
            MessageId: messageId,
            Phase: 0,
            ReportState: reportState,
            DeploymentFrontierY: 0,
            EnemyAdvance: 0,
            CollisionTriangles: [],
            ReportMessageCell: reportMessageCell,
            ReportUnitSlot: reportUnitSlot)
        {
            HeldDirectionMask = heldDirectionMask,
            GameSpeed = 2
        };

    private static string Single(IReadOnlyList<string> lines)
    {
        Equal(1, lines.Count, "spoken line count");
        return lines[0];
    }

    private static void Contains(string actual, string expected, string label)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label}: expected '{expected}' within '{actual}'.");
        }
    }

    private static void DoesNotContain(string actual, string unexpected, string label)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label}: did not expect '{unexpected}' within '{actual}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }
}
