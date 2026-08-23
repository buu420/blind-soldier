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
        SaysNothingOnArrivalSoTheReadoutIsNotDuplicated();
        LetsGoWhenTheGameNeverReportsHoldingTheKey();
    }

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
        AssertSpoken(step, "refused keystrokes tell the player the jump failed");
    }

    private static void SaysNothingOnArrivalSoTheReadoutIsNotDuplicated()
    {
        // The cursor readout announces where the cursor came to rest and what is
        // standing there the moment the keys are released. Announcing it here as
        // well is the same duplicate the battle opening had to be fixed for.
        var sink = new RecordingSink();
        var steering = new CondorCursorSteering(new HighwayAutoSteeringController(sink));
        steering.Begin(targetX: 300, targetY: 700, cursorX: 300, cursorY: 700);

        var step = steering.Step(cursorReadable: true, underCursorControl: true, 300, 700, AllDirections);
        Equal(CondorSteeringOutcome.Arrived, step.Outcome, "already there is arrival");
        AssertNotSpoken(step, "arrival is left to the cursor readout");
    }

    private sealed class RecordingSink : IHighwayKeyboardInputSink
    {
        private readonly HashSet<ushort> held = [];

        internal bool RefuseEverything { get; init; }

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            if (RefuseEverything)
            {
                return new HighwayKeyboardSendResult(0, 5);
            }

            foreach (var transition in transitions)
            {
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
