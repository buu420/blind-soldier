using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

public static class FieldFootstepDistanceRegressionTests
{
    private const double DistanceUnitsPerFootstep = 60d;
    private static readonly DateTime StartTime =
        new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        WalkingAndRunningUseTheSameWorldDistanceThreshold();
        PartialMovementAccumulatesToTheThreshold();
        StationaryWallContactStaysSilent();
        LargeMovementEmitsOneStepAndKeepsOnlyModuloRemainder();
        DistanceTriggerStillRespectsRunCadenceInterval();
    }

    private static void WalkingAndRunningUseTheSameWorldDistanceThreshold()
    {
        var start = Position(x: 0);

        var walking = CreateTracker();
        AssertFalse(
            walking.Observe(start, StartTime, false, DistanceUnitsPerFootstep),
            "Walking should prime without a footstep.");
        AssertFalse(
            walking.Observe(start with { X = 59 }, StartTime.AddSeconds(1), false, DistanceUnitsPerFootstep),
            "Walking should remain silent before 60 world units.");
        AssertTrue(
            walking.Observe(start with { X = 60 }, StartTime.AddSeconds(2), false, DistanceUnitsPerFootstep),
            "Walking should step after 60 world units.");

        var running = CreateTracker();
        AssertFalse(
            running.Observe(start, StartTime, true, DistanceUnitsPerFootstep),
            "Running should prime without a footstep.");
        AssertFalse(
            running.Observe(start with { X = 59 }, StartTime.AddMilliseconds(50), true, DistanceUnitsPerFootstep),
            "Running should remain silent before 60 world units despite its higher speed.");
        AssertTrue(
            running.Observe(start with { X = 60 }, StartTime.AddMilliseconds(100), true, DistanceUnitsPerFootstep),
            "Running should step after the same 60 world units.");
    }

    private static void PartialMovementAccumulatesToTheThreshold()
    {
        var tracker = CreateTracker();
        var start = Position(x: 0);

        AssertFalse(tracker.Observe(start, StartTime, false, DistanceUnitsPerFootstep), "The first sample should prime.");
        AssertFalse(tracker.Observe(start with { X = 20 }, StartTime.AddMilliseconds(100), false, DistanceUnitsPerFootstep), "Twenty units should not step.");
        AssertFalse(tracker.Observe(start with { X = 40 }, StartTime.AddMilliseconds(200), false, DistanceUnitsPerFootstep), "Forty accumulated units should not step.");
        AssertFalse(tracker.Observe(start with { X = 59 }, StartTime.AddMilliseconds(300), false, DistanceUnitsPerFootstep), "Fifty-nine accumulated units should not step.");
        AssertTrue(tracker.Observe(start with { X = 60 }, StartTime.AddMilliseconds(400), false, DistanceUnitsPerFootstep), "Partial movement should step when it accumulates to 60 units.");
    }

    private static void StationaryWallContactStaysSilent()
    {
        var tracker = CreateTracker();
        var againstWall = Position(x: 100, y: 200, z: 300);

        AssertFalse(tracker.Observe(againstWall, StartTime, false, DistanceUnitsPerFootstep), "The wall sample should prime.");
        AssertFalse(tracker.Observe(againstWall, StartTime.AddSeconds(1), false, DistanceUnitsPerFootstep), "Held walking input against a wall should stay silent.");
        AssertFalse(tracker.Observe(againstWall, StartTime.AddSeconds(2), true, DistanceUnitsPerFootstep), "Held running input against a wall should stay silent.");
    }

    private static void LargeMovementEmitsOneStepAndKeepsOnlyModuloRemainder()
    {
        var tracker = CreateTracker();
        var start = Position(x: 0);

        AssertFalse(tracker.Observe(start, StartTime, false, DistanceUnitsPerFootstep), "The large-movement sample should prime.");
        AssertTrue(
            tracker.Observe(start with { X = 145 }, StartTime.AddMilliseconds(100), true, DistanceUnitsPerFootstep),
            "A 145-unit update should emit one footstep.");
        AssertFalse(
            tracker.Observe(start with { X = 146 }, StartTime.AddMilliseconds(200), true, DistanceUnitsPerFootstep),
            "A large update must retain only its 25-unit modulo remainder, not an 85-unit backlog.");
        AssertTrue(
            tracker.Observe(start with { X = 180 }, StartTime.AddMilliseconds(300), true, DistanceUnitsPerFootstep),
            "The 26-unit remainder plus 34 new units should emit the next footstep.");
    }

    private static void DistanceTriggerStillRespectsRunCadenceInterval()
    {
        var tracker = new FieldFootstepTracker(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(300),
            measuredRunSpeedUnitsPerSecond: 300);
        var start = Position(x: 0);

        AssertFalse(
            tracker.Observe(start, StartTime, true, DistanceUnitsPerFootstep),
            "The running cadence test should prime without a footstep.");
        AssertFalse(
            tracker.Observe(start with { X = 60 }, StartTime.AddMilliseconds(100), true, DistanceUnitsPerFootstep),
            "Reaching the distance threshold must not bypass the 300 ms running interval.");
        AssertFalse(
            tracker.Observe(start with { X = 120 }, StartTime.AddMilliseconds(200), true, DistanceUnitsPerFootstep),
            "Repeated distance thresholds must remain silent before the running interval.");
        AssertTrue(
            tracker.Observe(start with { X = 180 }, StartTime.AddMilliseconds(300), true, DistanceUnitsPerFootstep),
            "Running should emit one footstep when both distance and cadence thresholds are satisfied.");
        AssertFalse(
            tracker.Observe(start with { X = 240 }, StartTime.AddMilliseconds(400), true, DistanceUnitsPerFootstep),
            "Running must not emit another footstep only 100 ms later.");
        AssertTrue(
            tracker.Observe(start with { X = 360 }, StartTime.AddMilliseconds(600), true, DistanceUnitsPerFootstep),
            "Running should emit the next footstep after another full cadence interval.");
    }

    private static FieldFootstepTracker CreateTracker() => new(TimeSpan.Zero);

    private static FieldPositionSnapshot Position(int x, int y = 0, int z = 0) =>
        new(1, 116, 0, x, y, z, 0, 0);

    private static void AssertTrue(bool actual, string message)
    {
        if (!actual)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFalse(bool actual, string message) => AssertTrue(!actual, message);
}
