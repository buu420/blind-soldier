using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

/// <summary>
/// The I-key jump: taking the Fort Condor cursor somewhere by holding the game's
/// own direction keys.
/// </summary>
/// <remarks>
/// The failure this must never produce is a key left held down in the player's
/// game, so every exit is checked for a release - arrival, stall, divergence,
/// losing cursor control, an unreadable cursor, a refused keystroke, and the
/// caller cancelling.
/// </remarks>
internal static class CondorCursorSteeringTests
{
    /// <summary>The battle reporting every direction held, so tests that are not
    /// about acknowledgement are not silently about it.</summary>
    private const uint AllDirections =
        CondorCursorSteering.MaskUp | CondorCursorSteering.MaskDown |
        CondorCursorSteering.MaskLeft | CondorCursorSteering.MaskRight;

    internal static void Run()
    {
        DrivesTowardsTheTargetAndStopsOnArrival();
        LetsGoWhenTheCursorStopsMoving();
        LetsGoWhenTheCursorRunsAwayFromTheTarget();
        LetsGoWhenCursorControlIsTakenAway();
        LetsGoWhenTheCursorCannotBeRead();
        LetsGoWhenTheGameRefusesTheKeystrokes();
        SaysMovingStoppedOnArrival();
        LetsGoWhenTheGameNeverReportsHoldingTheKey();
        LetsGoOfTheKeysWhenTheNextStrideWouldOvershootTheTarget();
        StopsInsteadOfCrossingATargetItCannotLandOn();
        SlowingDownIsNotMistakenForAKeyTheGameIgnored();
        SittingOnTheTargetIsNotCrossingIt();
        SteersTheDestinationCursorWithoutConfirmingTheOrder();
        DestinationSteeringTracksTheSelectedEnemyRatherThanAStaleCoordinate();
        DestinationSteeringRequiresACleanModeAndNoHeldDirection();
        DestinationSteeringStopsAudiblyWhenItsModeOrTargetDisappears();
        DestinationSteeringRefusesANewKeyDownWhenAnotherDirectionAppears();
        SharedProductionPathStillSteersTheBattlefieldCursor();
    }

    /// <summary>
    /// Mode 3 owns a separate camera-relative coordinate pair. The jump must
    /// steer that pair with directions and stop inside the native unit hit box;
    /// it must never inject OK on the player's behalf.
    /// </summary>
    private static void SteersTheDestinationCursorWithoutConfirmingTheOrder()
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        var target = new CondorNavigationTarget("enemy Beast, 230 of 230", 300, 700, 20);
        var snapshot = DestinationSnapshot(200, 500, Enemy(slot: 20, x: 300, y: 700));

        Equal(true, steering.TryBegin(target, snapshot), "mode-3 destination jump accepted");
        Equal(CondorCursorDomain.Destination, steering.Domain, "destination domain retained");

        var travelling = steering.Step(snapshot);
        Equal(CondorSteeringOutcome.Steering, travelling.Outcome, "destination cursor is travelling");
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown, HighwayAutoSteeringController.ScanCodeRight },
            sink.HeldScanCodes(),
            "mode 3 steers its own cursor down and right");

        var arrived = steering.Step(snapshot with
        {
            DestinationX = 299,
            DestinationY = 699,
            HeldDirectionMask = CondorCursorSteering.MaskDown | CondorCursorSteering.MaskRight
        });
        Equal(CondorSteeringOutcome.Arrived, arrived.Outcome, "destination cursor arrives in the hit box");
        Equal("Moving stopped.", arrived.Speech, "destination arrival is announced");
        Equal(0, sink.HeldScanCodes().Length, "direction keys are released before the player presses OK");

        var directionScanCodes = new HashSet<ushort>
        {
            HighwayAutoSteeringController.ScanCodeUp,
            HighwayAutoSteeringController.ScanCodeDown,
            HighwayAutoSteeringController.ScanCodeLeft,
            HighwayAutoSteeringController.ScanCodeRight
        };
        if (sink.Transitions.Any(transition => !directionScanCodes.Contains(transition.ScanCode)))
        {
            throw new InvalidOperationException(
                "destination steering emitted something other than a direction key; " +
                "the player alone must confirm an order.");
        }
    }

    /// <summary>
    /// Enemies move from the first frame. A jump aimed at the coordinate spoken
    /// when L was pressed would chase empty ground; the selected slot is the
    /// identity and its current coordinate is the destination on every sample.
    /// </summary>
    private static void DestinationSteeringTracksTheSelectedEnemyRatherThanAStaleCoordinate()
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        var selected = new CondorNavigationTarget("enemy Beast, 230 of 230", 300, 700, 20);
        var first = DestinationSnapshot(
            200,
            500,
            Enemy(slot: 21, x: 100, y: 500),
            Enemy(slot: 20, x: 300, y: 700));
        Equal(true, steering.TryBegin(selected, first), "moving enemy selected");

        steering.Step(first);
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown, HighwayAutoSteeringController.ScanCodeRight },
            sink.HeldScanCodes(),
            "first sample follows the enemy's first position");

        // The same slot crossed to the other side of the cursor. The controller
        // must turn with it rather than continue towards the old X=300 point.
        var moved = DestinationSnapshot(
            250,
            550,
            Enemy(slot: 21, x: 100, y: 500),
            Enemy(slot: 20, x: 200, y: 700)) with
        {
            HeldDirectionMask = CondorCursorSteering.MaskDown | CondorCursorSteering.MaskRight
        };
        steering.Step(moved);
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown, HighwayAutoSteeringController.ScanCodeLeft },
            sink.HeldScanCodes(),
            "the selected slot's live position replaces its stale coordinate");
    }

    private static void DestinationSteeringRequiresACleanModeAndNoHeldDirection()
    {
        var target = new CondorNavigationTarget("enemy Beast, 230 of 230", 300, 700, 20);
        var clean = DestinationSnapshot(200, 500, Enemy(slot: 20, x: 300, y: 700));

        Refuses(clean with { ModalState = 2 }, "a modal overlay owns the keys");
        Refuses(clean with { ReportState = 1 }, "a report owns the keys");
        Refuses(clean with { HeldDirectionMask = CondorCursorSteering.MaskRight },
            "a physical direction is already held");
        Refuses(clean with { Units = Array.Empty<CondorBattleUnit>() },
            "the selected enemy no longer exists");

        void Refuses(CondorBattleSnapshot snapshot, string label)
        {
            var steering = new CondorCursorSteering(
                new HighwayAutoSteeringController(new RecordingSink()));
            Equal(false, steering.TryBegin(target, snapshot), label);
            Equal(false, steering.IsSteering, $"{label}: no jump was armed");
        }
    }

    private static void DestinationSteeringStopsAudiblyWhenItsModeOrTargetDisappears()
    {
        Abandons(
            snapshot => snapshot with { InteractionMode = CondorBattleSnapshot.AllyUnitInteractionMode },
            "destination mode changed");
        Abandons(
            snapshot => snapshot with { Units = Array.Empty<CondorBattleUnit>() },
            "selected enemy disappeared");

        static void Abandons(
            Func<CondorBattleSnapshot, CondorBattleSnapshot> change,
            string label)
        {
            var sink = new RecordingSink();
            var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
            var target = new CondorNavigationTarget("enemy Beast, 230 of 230", 300, 700, 20);
            var snapshot = DestinationSnapshot(200, 500, Enemy(slot: 20, x: 300, y: 700));
            Equal(true, steering.TryBegin(target, snapshot), $"{label}: jump accepted");
            steering.Step(snapshot);

            var stopped = steering.Step(change(snapshot));
            Equal(CondorSteeringOutcome.Abandoned, stopped.Outcome, $"{label}: jump abandoned");
            AssertSpoken(stopped, $"{label}: failure is never silent");
            if (stopped.Speech?.Contains("Movement stopped", StringComparison.OrdinalIgnoreCase) != true)
            {
                throw new InvalidOperationException(
                    $"{label}: expected an explicit movement-stop line, got '{stopped.Speech ?? "<null>"}'.");
            }

            Equal(0, sink.HeldScanCodes().Length, $"{label}: every direction key released");
        }
    }

    private static void DestinationSteeringRefusesANewKeyDownWhenAnotherDirectionAppears()
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        var selected = new CondorNavigationTarget("enemy Beast, 230 of 230", 200, 700, 20);
        var first = DestinationSnapshot(200, 500, Enemy(slot: 20, x: 200, y: 700));
        Equal(true, steering.TryBegin(selected, first), "vertical destination jump accepted");
        steering.Step(first);
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown },
            sink.HeldScanCodes(),
            "only the controller's down key is held");

        // The enemy moved right, so the next steering decision would add Right.
        // Left appearing in the native mask is not ours. Release before adding
        // another key instead of fighting a physical direction from the player.
        var contested = DestinationSnapshot(200, 520, Enemy(slot: 20, x: 300, y: 700)) with
        {
            HeldDirectionMask = CondorCursorSteering.MaskDown | CondorCursorSteering.MaskLeft
        };
        var stopped = steering.Step(contested);
        Equal(CondorSteeringOutcome.Abandoned, stopped.Outcome,
            "unexpected physical direction aborts before the new key-down");
        AssertSpoken(stopped, "contested steering is never silent");
        Equal(0, sink.HeldScanCodes().Length, "controller releases its own key on contested input");
        if (sink.Transitions.Any(transition =>
                transition.IsKeyDown &&
                transition.ScanCode == HighwayAutoSteeringController.ScanCodeRight))
        {
            throw new InvalidOperationException(
                "steering pressed Right after the native mask showed an unowned Left direction.");
        }
    }

    private static void SharedProductionPathStillSteersTheBattlefieldCursor()
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        var unit = Enemy(slot: 20, x: 300, y: 700);
        var target = new CondorNavigationTarget(unit.Describe(), unit.X, unit.Y, unit.Slot);
        var snapshot = DestinationSnapshot(10, 20, unit) with
        {
            InteractionMode = CondorBattleSnapshot.CursorInteractionMode,
            CursorX = 200,
            CursorY = 500
        };

        Equal(true, steering.TryBegin(target, snapshot), "ordinary battlefield jump accepted");
        Equal(CondorCursorDomain.Battlefield, steering.Domain, "battlefield domain retained");
        steering.Step(snapshot);
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown, HighwayAutoSteeringController.ScanCodeRight },
            sink.HeldScanCodes(),
            "shared production overload reads the battlefield pair in mode 1");
    }

    private static CondorBattleSnapshot DestinationSnapshot(
        int destinationX,
        int destinationY,
        params CondorBattleUnit[] units) =>
        new(
            InteractionMode: CondorBattleSnapshot.DestinationInteractionMode,
            ModalState: 0,
            SettingMenuRow: 0,
            SettingMenuRotation: 0,
            AvailableTypeIds: Array.Empty<int>(),
            Gil: 1000,
            // Deliberately opposite the destination cursor relative to the
            // target. Reading the wrong pair would steer up-left, not down-right.
            CursorX: 400,
            CursorY: 800,
            // Native terrain legality gates OK, not cursor movement. Steering
            // must still reach the point and leave confirmation to the player.
            CursorPlacementLegal: false,
            UnitUnderCursorSlot: -1,
            Units: units,
            AlliedCount: units.Count(unit => !unit.IsEnemy),
            EnemyCount: units.Count(unit => unit.IsEnemy),
            Outcome: 0,
            MessageId: -1,
            Phase: 2,
            ReportState: 0,
            DeploymentFrontierY: 480,
            EnemyAdvance: 0,
            CollisionTriangles: Array.Empty<CondorCollisionTriangle>())
        {
            DestinationX = destinationX,
            DestinationY = destinationY,
            HeldDirectionMask = 0
        };

    private static CondorBattleUnit Enemy(int slot, int x, int y) =>
        new(
            Slot: slot,
            IsEnemy: true,
            TypeId: 18,
            CurrentHp: 230,
            MaximumHp: 230,
            Attack: 35,
            X: x,
            Y: y,
            IsDying: false,
            Width: 16,
            HeightAbove: 16);

    /// <summary>
    /// The check that matters most. Module 9 polls DirectInput, applies the
    /// player's own ff7input.cfg mapping, and only then sets the bits in its held
    /// mask. A keystroke Windows accepts can still mean nothing to the battle -
    /// the injection may be filtered, or the player may have that direction bound
    /// to a key we did not press. FFVII's untouched default is the numeric keypad,
    /// not the arrows, so this is the ordinary case and not an exotic one.
    /// </summary>
    private static void LetsGoWhenTheGameNeverReportsHoldingTheKey()
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 700, cursorX: 300, cursorY: 500);

        // The battle reports holding nothing at all, however hard we press.
        CondorSteeringStep step = default;
        for (var attempt = 0; attempt <= CondorCursorSteering.AcknowledgementLimit + 1; attempt++)
        {
            step = steering.Step(
                cursorReadable: true,
                underCursorControl: true,
                300,
                500,
                heldDirectionMask: 0);
        }

        Equal(CondorSteeringOutcome.Abandoned, step.Outcome, "gave up when the battle never saw the key");
        Equal(
            "The game is not taking the direction keys.",
            step.Speech,
            "a missing native acknowledgement keeps its specific failure");
        AssertSpoken(step, "a key the game never sees tells the player so");
        Equal(0, sink.HeldScanCodes().Length, "every key released when the press is not acknowledged");

        // And it must fail faster than the stall check would have caught it,
        // otherwise the acknowledgement is decoration.
        if (CondorCursorSteering.AcknowledgementLimit >= CondorCursorSteering.StallLimit)
        {
            throw new InvalidOperationException(
                "the acknowledgement must fail sooner than the stall check, or it adds nothing.");
        }

        // Some other direction being held is not acknowledgement of ours. The
        // player may well be leaning on a direction key themselves while the jump
        // runs, and taking their keypress as proof that ours landed would leave
        // the loop open in exactly the case it exists to catch.
        var otherSink = new RecordingSink();
        var other = new CondorCursorSteering(new HighwayAutoSteeringController(otherSink));
        other.Begin(targetX: 300, targetY: 700, cursorX: 300, cursorY: 500);

        CondorSteeringStep otherStep = default;
        for (var attempt = 0; attempt <= CondorCursorSteering.AcknowledgementLimit + 1; attempt++)
        {
            otherStep = other.Step(
                cursorReadable: true,
                underCursorControl: true,
                300,
                500,
                heldDirectionMask: CondorCursorSteering.MaskLeft);
        }

        Equal(
            CondorSteeringOutcome.Abandoned,
            otherStep.Outcome,
            "a different direction being held does not acknowledge ours");
        Equal(0, otherSink.HeldScanCodes().Length, "every key released when only another direction is held");
    }

    /// <summary>
    /// Full native repeat is four coordinate units per module update, and the
    /// battle runs far faster than the mod reads it, so a single reading can
    /// carry the cursor further than the whole arrival tolerance is wide.
    /// Pressing on regardless would sail past the target every time.
    /// </summary>
    private static void LetsGoOfTheKeysWhenTheNextStrideWouldOvershootTheTarget()
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 700, cursorX: 300, cursorY: 500);

        steering.Step(cursorReadable: true, underCursorControl: true, 300, 500, AllDirections);
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown },
            sink.HeldScanCodes(),
            "holds down while there is room to travel");

        // The battle carried the cursor 188 units between two readings and only
        // twelve are left. Pressing on would overshoot.
        var slowing = steering.Step(cursorReadable: true, underCursorControl: true, 300, 688, AllDirections);
        Equal(CondorSteeringOutcome.Steering, slowing.Outcome, "still travelling while it slows down");
        Equal(
            0,
            sink.HeldScanCodes().Length,
            "lets go, which clears the battle's repeat counter back to one unit an update");

        // Ramp reset: four units this reading rather than 188, and eight left to
        // travel, so there is room to press again.
        var resumed = steering.Step(cursorReadable: true, underCursorControl: true, 300, 692, AllDirections);
        Equal(CondorSteeringOutcome.Steering, resumed.Outcome, "still travelling");
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown },
            sink.HeldScanCodes(),
            "presses on once the stride fits the distance left");

        var arrived = steering.Step(cursorReadable: true, underCursorControl: true, 300, 699, AllDirections);
        Equal(CondorSteeringOutcome.Arrived, arrived.Outcome, "arrives on the fine approach");
        Equal(0, sink.HeldScanCodes().Length, "every key released on arrival");
    }

    /// <summary>
    /// Letting go resets the battle's repeat counter only while nothing else is
    /// holding a direction. A player leaning on their own direction key keeps
    /// the counter at full repeat, so the stride never shrinks and the cursor
    /// crosses the target on every reading without ever landing on it.
    /// </summary>
    /// <remarks>
    /// Nothing else in this class would end that jump. The cursor is moving, so
    /// it is not stalled; it stays beside the target, so it has not diverged;
    /// and it is never inside the tolerance, so it never arrives. It would
    /// twitch back and forth across the target for the whole sample ceiling.
    /// </remarks>
    private static void StopsInsteadOfCrossingATargetItCannotLandOn()
    {
        // Both axes, because the latch is per axis and a jump that gives up
        // vertically but oscillates horizontally forever is the same failure
        // wearing the other hat.
        CrossesUntilItGivesUp(
            targetX: 300,
            targetY: 700,
            startX: 300,
            startY: 690,
            horizontal: false,
            label: "vertically");

        CrossesUntilItGivesUp(
            targetX: 700,
            targetY: 300,
            startX: 690,
            startY: 300,
            horizontal: true,
            label: "horizontally");
    }

    private static void CrossesUntilItGivesUp(
        int targetX,
        int targetY,
        int startX,
        int startY,
        bool horizontal,
        string label)
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX, targetY, startX, startY);

        CondorSteeringStep step = default;
        var readings = 0;
        foreach (var moving in new[] { 690, 710, 690, 710, 690, 710, 690, 710, 690, 710, 690, 710 })
        {
            step = steering.Step(
                cursorReadable: true,
                underCursorControl: true,
                horizontal ? moving : startX,
                horizontal ? startY : moving,
                AllDirections);

            readings++;
            if (step.Outcome != CondorSteeringOutcome.Steering)
            {
                break;
            }
        }

        Equal(
            CondorSteeringOutcome.Abandoned,
            step.Outcome,
            $"stops crossing {label} over a target it cannot land on");
        Equal("Could not get closer.", step.Speech, $"crossing {label} keeps its specific failure");
        Equal(0, sink.HeldScanCodes().Length, $"every key released when it gives up crossing {label}");

        // Said out loud, because the cursor readout that follows will name a
        // position the player did not ask for and they are owed the reason.
        AssertSpoken(step, $"a jump that could not land {label} says so rather than leaving the player guessing");

        // And it must give up promptly. Bounded by the crossing limit rather
        // than by the sample ceiling, which at this reading rate would be about
        // a quarter of a minute of the cursor twitching.
        if (readings > CondorCursorSteering.CrossingLimit + 2)
        {
            throw new InvalidOperationException(
                $"expected to give up {label} within {CondorCursorSteering.CrossingLimit + 2} " +
                $"readings, took {readings}.");
        }
    }

    /// <summary>
    /// Letting go is not a press, so there is nothing for the battle to
    /// confirm. Leaving the last request armed while the loop is deliberately
    /// holding nothing counts readings against a key that is already up, and
    /// abandons a jump that is behaving exactly as designed.
    /// </summary>
    private static void SlowingDownIsNotMistakenForAKeyTheGameIgnored()
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 700, cursorX: 300, cursorY: 690);

        // The battle reports holding nothing at all. The only press this jump
        // makes is the first one; every reading after it slows down instead,
        // because the cursor is being carried further than the distance left.
        CondorSteeringStep step = default;
        foreach (var y in new[] { 690, 710, 690, 710, 690, 710, 690 })
        {
            step = steering.Step(
                cursorReadable: true,
                underCursorControl: true,
                300,
                y,
                heldDirectionMask: 0);

            if (step.Outcome != CondorSteeringOutcome.Steering)
            {
                break;
            }
        }

        Equal(CondorSteeringOutcome.Abandoned, step.Outcome, "the jump still ends");

        // It ends because it cannot land on the target, which is true. Blaming
        // the game for ignoring keys the loop chose not to press would send the
        // player off checking their controls over a fault that is not there.
        if (step.Speech?.Contains("not taking", StringComparison.Ordinal) == true)
        {
            throw new InvalidOperationException(
                "a jump deliberately holding nothing must not be blamed on the game " +
                $"ignoring keys: \"{step.Speech}\".");
        }

        Equal(0, sink.HeldScanCodes().Length, "every key released either way");
    }

    /// <summary>
    /// An axis that lands exactly on the target has not crossed it. Treating
    /// zero as a side of the target counts a crossing every time the cursor
    /// touches it and drifts off again, and an axis that has "crossed" too
    /// often stops being steered at all.
    /// </summary>
    private static void SittingOnTheTargetIsNotCrossingIt()
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 900, cursorX: 290, cursorY: 500);

        // X touches the target exactly and slips off it again, over and over,
        // while Y is still a long way out.
        var y = 500;
        CondorSteeringStep step = default;
        foreach (var x in new[] { 290, 300, 290, 300, 290, 300, 290, 300, 290 })
        {
            y += 20;
            step = steering.Step(cursorReadable: true, underCursorControl: true, x, y, AllDirections);
        }

        Equal(CondorSteeringOutcome.Steering, step.Outcome, "still travelling");
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown, HighwayAutoSteeringController.ScanCodeRight },
            sink.HeldScanCodes(),
            "an axis that only ever touched the target is still steered towards it");
    }

    private static void DrivesTowardsTheTargetAndStopsOnArrival()
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));

        // Target below and to the right: higher Y is further down the mountain.
        steering.Begin(targetX: 300, targetY: 700, cursorX: 200, cursorY: 500);
        Equal(true, steering.IsSteering, "a jump is running");

        var first = steering.Step(cursorReadable: true, underCursorControl: true, 200, 500, AllDirections);
        Equal(CondorSteeringOutcome.Steering, first.Outcome, "still travelling");
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown, HighwayAutoSteeringController.ScanCodeRight },
            sink.HeldScanCodes(),
            "holds down and right towards a target below and to the right");

        // One axis finishing must not disturb the other: X is inside tolerance
        // here, so only Down is still held.
        steering.Step(cursorReadable: true, underCursorControl: true, 299, 600, AllDirections);
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeDown },
            sink.HeldScanCodes(),
            "only the unfinished axis is still held");

        var arrived = steering.Step(cursorReadable: true, underCursorControl: true, 301, 699, AllDirections);
        Equal(CondorSteeringOutcome.Arrived, arrived.Outcome, "arrival inside the tolerance");
        Equal(false, steering.IsSteering, "the jump is over");
        Equal(0, sink.HeldScanCodes().Length, "every key released on arrival");

        // Each single-axis direction pinned by itself. Without these, inverting
        // the vertical mapping - so that Down drove the cursor up the mountain -
        // passed the whole suite untouched.
        AssertHolds(300, 200, 300, 700, HighwayAutoSteeringController.ScanCodeUp, "a target above holds up");
        AssertHolds(300, 900, 300, 700, HighwayAutoSteeringController.ScanCodeDown, "a target below holds down");
        AssertHolds(100, 700, 300, 700, HighwayAutoSteeringController.ScanCodeLeft, "a target to the left holds left");
        AssertHolds(500, 700, 300, 700, HighwayAutoSteeringController.ScanCodeRight, "a target to the right holds right");
    }

    private static void AssertHolds(
        int targetX,
        int targetY,
        int cursorX,
        int cursorY,
        ushort expectedScanCode,
        string label)
    {
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX, targetY, cursorX, cursorY);
        steering.Step(cursorReadable: true, underCursorControl: true, cursorX, cursorY, AllDirections);
        Equal(new[] { expectedScanCode }, sink.HeldScanCodes(), label);
    }

    private static void LetsGoWhenTheCursorStopsMoving()
    {
        // The battlefield has edges and the cursor stops dead at them. Holding a
        // direction into an edge forever is the worst outcome available.
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 900, targetY: 900, cursorX: 200, cursorY: 200);

        CondorSteeringStep step = default;
        for (var attempt = 0; attempt <= CondorCursorSteering.StallLimit + 1; attempt++)
        {
            step = steering.Step(cursorReadable: true, underCursorControl: true, 200, 200, AllDirections);
        }

        Equal(CondorSteeringOutcome.Abandoned, step.Outcome, "gave up on a stuck cursor");
        AssertSpoken(step, "a stuck jump tells the player it failed");
        Equal(0, sink.HeldScanCodes().Length, "every key released on a stall");
    }

    private static void LetsGoWhenTheCursorRunsAwayFromTheTarget()
    {
        // The guard against the direction mapping being wrong. If Down decreased
        // Y, the cursor would run for the edge of the map; stopping and saying so
        // is the only honest response.
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 700, cursorX: 300, cursorY: 600);

        var step = steering.Step(
            cursorReadable: true,
            underCursorControl: true,
            300,
            600 - CondorCursorSteering.DivergenceSlack - 10,
            AllDirections);

        Equal(CondorSteeringOutcome.Abandoned, step.Outcome, "gave up on a diverging cursor");
        AssertSpoken(step, "a diverging jump tells the player it failed");
        Equal(0, sink.HeldScanCodes().Length, "every key released on divergence");
    }

    private static void LetsGoWhenCursorControlIsTakenAway()
    {
        // A menu opened, or the battle ended. The same keys now move something
        // else and holding them would be operating a menu the player did not ask
        // for. Silent on purpose: the player opened the menu and knows they did.
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 700, cursorX: 200, cursorY: 500);
        steering.Step(cursorReadable: true, underCursorControl: true, 200, 500, AllDirections);

        var step = steering.Step(cursorReadable: true, underCursorControl: false, 210, 520, AllDirections);
        Equal(CondorSteeringOutcome.Abandoned, step.Outcome, "gave up when a menu took the keys");
        AssertNotSpoken(step, "losing cursor control to the player's own menu is not announced");
        Equal(0, sink.HeldScanCodes().Length, "every key released when control is lost");
    }

    private static void LetsGoWhenTheCursorCannotBeRead()
    {
        // Steering on a stale position is steering blind.
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 700, cursorX: 200, cursorY: 500);
        steering.Step(cursorReadable: true, underCursorControl: true, 200, 500, AllDirections);

        // Deliberately the last known-good position rather than an obviously
        // wrong one. Passing 0,0 here let the divergence guard abandon the jump
        // for a different reason, so removing the unreadable check entirely still
        // passed - the mutation survived until this was tightened.
        var step = steering.Step(cursorReadable: false, underCursorControl: true, 200, 500, AllDirections);
        Equal(CondorSteeringOutcome.Abandoned, step.Outcome, "gave up on an unreadable cursor");
        Equal("Lost track of the cursor.", step.Speech, "an unreadable cursor keeps its specific failure");
        AssertSpoken(step, "an unreadable cursor tells the player the jump failed");
        Equal(0, sink.HeldScanCodes().Length, "every key released on an unreadable cursor");
    }

    private static void LetsGoWhenTheGameRefusesTheKeystrokes()
    {
        var sink = new RecordingSink { RefuseEverything = true };
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 700, cursorX: 200, cursorY: 500);

        var step = steering.Step(cursorReadable: true, underCursorControl: true, 200, 500, AllDirections);
        Equal(CondorSteeringOutcome.Abandoned, step.Outcome, "gave up when SendInput was refused");
        Equal("Could not get there.", step.Speech, "refused input keeps its specific failure");
        AssertSpoken(step, "refused keystrokes tell the player the jump failed");
    }

    private static void SaysMovingStoppedOnArrival()
    {
        // The readout still owns the final coordinate and what is there. The
        // steering owns only the state change, so arrival is one short line and
        // never another coordinate that could disagree with the next snapshot.
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 700, cursorX: 300, cursorY: 700);

        var step = steering.Step(cursorReadable: true, underCursorControl: true, 300, 700, AllDirections);
        Equal(CondorSteeringOutcome.Arrived, step.Outcome, "already there is arrival");
        Equal("Moving stopped.", step.Speech, "arrival announces that movement ended once");
    }

    private sealed class RecordingSink : IHighwayKeyboardInputSink
    {
        private readonly HashSet<ushort> held = [];

        internal List<HighwayKeyboardTransition> Transitions { get; } = [];

        internal bool RefuseEverything { get; init; }

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            if (RefuseEverything)
            {
                return new HighwayKeyboardSendResult(0, 5);
            }

            foreach (var transition in transitions)
            {
                Transitions.Add(transition);
                if (transition.IsKeyDown)
                {
                    held.Add(transition.ScanCode);
                }
                else
                {
                    held.Remove(transition.ScanCode);
                }
            }

            return new HighwayKeyboardSendResult(transitions.Count, 0);
        }

        internal ushort[] HeldScanCodes() => held.Order().ToArray();
    }

    private static void AssertSpoken(CondorSteeringStep step, string label)
    {
        if (string.IsNullOrWhiteSpace(step.Speech))
        {
            throw new InvalidOperationException($"{label}: expected something to be said, got silence.");
        }
    }

    private static void AssertNotSpoken(CondorSteeringStep step, string label)
    {
        if (!string.IsNullOrWhiteSpace(step.Speech))
        {
            throw new InvalidOperationException($"{label}: expected silence, got \"{step.Speech}\".");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void Equal(ushort[] expected, ushort[] actual, string label)
    {
        // Which keys are held is a set. Comparing it as an ordered sequence would
        // fail on nothing more than the order they happen to come back in.
        expected = expected.Order().ToArray();
        actual = actual.Order().ToArray();
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }
}
