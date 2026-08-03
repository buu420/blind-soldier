using Ff7.Accessibility.Core;

internal static class HighwaySteeringTrackerTests
{
    internal static void Run()
    {
        EntersAndLeavesCorrectionWithHysteresis();
        MapsCloudOffsetToTheRequiredSteeringDirection();
        AvoidsTheTruckWithForwardBackAndDiagonalDirections();
        KeepsTruckAvoidanceInsideTheRoadAndAboveRoadCentering();
        ReleasesTruckAvoidanceAfterLongitudinalClearance();
        UsesNormalAndCriticalCadence();
        KeepsAutomaticDirectionBetweenAudibleCues();
        AnnouncesAnImmediateDirectionChange();
        ResetsAndRejectsInvalidRoadState();
    }

    private static void EntersAndLeavesCorrectionWithHysteresis()
    {
        var tracker = CreateTracker();
        var now = UtcNow();

        Equal(null, tracker.Update(Road(20), now).Cue, "silence inside the entry corridor");
        Equal(
            HighwaySteeringDirection.Left,
            tracker.Update(Road(25), now.AddMilliseconds(10)).Cue?.Direction,
            "correction begins at twenty-five percent");
        Equal(
            HighwaySteeringDirection.Left,
            tracker.Update(Road(20), now.AddMilliseconds(800)).Cue?.Direction,
            "active correction continues until the release corridor");

        Equal(
            null,
            tracker.Update(Road(14), now.AddMilliseconds(810)).Cue,
            "returning inside fifteen percent releases correction");
        Equal(
            null,
            tracker.Update(Road(20), now.AddMilliseconds(1600)).Cue,
            "released correction does not re-enter inside twenty-five percent");
    }

    private static void MapsCloudOffsetToTheRequiredSteeringDirection()
    {
        var now = UtcNow();
        var left = CreateTracker().Update(Road(40), now).Cue;
        Equal(HighwaySteeringDirection.Left, left?.Direction, "positive lateral offset means steer left");
        Near(0.4, left?.EdgeRatio ?? double.NaN, "positive edge ratio");
        Equal(false, left?.IsCritical, "forty percent is a normal correction");

        var right = CreateTracker().Update(Road(-40), now).Cue;
        Equal(HighwaySteeringDirection.Right, right?.Direction, "negative lateral offset means steer right");
        Near(0.4, right?.EdgeRatio ?? double.NaN, "negative offset uses absolute edge ratio");
    }

    private static void AvoidsTheTruckWithForwardBackAndDiagonalDirections()
    {
        var now = UtcNow();

        var ahead = CreateTracker().Update(Road(0), Truck(0, 250), now).Cue;
        Equal(HighwaySteeringDirection.Down, ahead?.Direction, "truck ahead means move down");
        Equal(HighwaySteeringCueReason.TruckAvoidance, ahead?.Reason, "truck cue reason");
        Equal(true, ahead?.IsCritical, "truck avoidance is critical");

        var behind = CreateTracker().Update(Road(0), Truck(0, -250), now).Cue;
        Equal(HighwaySteeringDirection.Up, behind?.Direction, "truck behind means move up");

        var aheadRight = CreateTracker().Update(Road(0), Truck(80, 250), now).Cue;
        Equal(
            HighwaySteeringDirection.DownLeft,
            aheadRight?.Direction,
            "truck ahead-right means move down-left");

        var behindLeft = CreateTracker().Update(Road(0), Truck(-80, -250), now).Cue;
        Equal(
            HighwaySteeringDirection.UpRight,
            behindLeft?.Direction,
            "truck behind-left means move up-right");
    }

    private static void KeepsTruckAvoidanceInsideTheRoadAndAboveRoadCentering()
    {
        var now = UtcNow();
        var tracker = CreateTracker();

        var cue = tracker.Update(Road(80), Truck(-80, -200), now).Cue;
        Equal(
            HighwaySteeringDirection.Up,
            cue?.Direction,
            "an outward right component is removed near the right road edge");
        Equal(
            HighwaySteeringCueReason.TruckAvoidance,
            cue?.Reason,
            "truck avoidance overrides the road's competing left correction");
    }

    private static void ReleasesTruckAvoidanceAfterLongitudinalClearance()
    {
        var now = UtcNow();
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.Down,
            tracker.Update(Road(0), Truck(0, 300), now).Cue?.Direction,
            "truck avoidance enters inside the reaction envelope");
        Equal(
            HighwaySteeringDirection.Down,
            tracker.Update(Road(0), Truck(0, 400), now.AddMilliseconds(260)).Cue?.Direction,
            "active avoidance uses a release margin");
        Equal(
            null,
            tracker.Update(Road(0), Truck(0, 421), now.AddMilliseconds(520)).Cue,
            "longitudinal clearance releases truck avoidance before the truck beacon resumes");
    }

    private static void UsesNormalAndCriticalCadence()
    {
        var now = UtcNow();
        var normal = CreateTracker();
        Equal(
            HighwaySteeringDirection.Left,
            normal.Update(Road(30), now).Cue?.Direction,
            "normal correction begins immediately");
        Equal(null, normal.Update(Road(30), now.AddMilliseconds(699)).Cue, "normal cue waits 700 ms");
        Equal(
            HighwaySteeringDirection.Left,
            normal.Update(Road(30), now.AddMilliseconds(700)).Cue?.Direction,
            "normal cue repeats at 700 ms");

        var critical = CreateTracker();
        Equal(true, critical.Update(Road(75), now).Cue?.IsCritical, "seventy-five percent is critical");
        Equal(null, critical.Update(Road(75), now.AddMilliseconds(259)).Cue, "critical cue waits 260 ms");
        Equal(true, critical.Update(Road(75), now.AddMilliseconds(260)).Cue?.IsCritical, "critical cue repeats at 260 ms");
    }

    private static void KeepsAutomaticDirectionBetweenAudibleCues()
    {
        var now = UtcNow();
        var road = CreateTracker();

        Equal(
            HighwaySteeringDirection.Left,
            road.Update(Road(30), now).Direction,
            "road correction exposes its automatic direction immediately");
        var roadGap = road.Update(Road(30), now.AddMilliseconds(699));
        Equal(null, roadGap.Cue, "road tone remains cadence-limited");
        Equal(
            HighwaySteeringDirection.Left,
            roadGap.Direction,
            "road automatic direction remains continuous between tones");
        Equal(
            HighwaySteeringDirection.None,
            road.Update(Road(14), now.AddMilliseconds(700)).Direction,
            "road automatic direction releases inside the hysteresis corridor");

        var truck = CreateTracker();
        Equal(
            HighwaySteeringDirection.DownLeft,
            truck.Update(Road(0), Truck(80, 250), now).Direction,
            "truck avoidance exposes its automatic diagonal immediately");
        var truckGap = truck.Update(Road(0), Truck(80, 250), now.AddMilliseconds(259));
        Equal(null, truckGap.Cue, "truck tone remains cadence-limited");
        Equal(
            HighwaySteeringDirection.DownLeft,
            truckGap.Direction,
            "truck automatic direction remains continuous between tones");
    }

    private static void AnnouncesAnImmediateDirectionChange()
    {
        var tracker = CreateTracker();
        var now = UtcNow();
        Equal(
            HighwaySteeringDirection.Left,
            tracker.Update(Road(30), now).Cue?.Direction,
            "initial left correction");
        Equal(
            HighwaySteeringDirection.Right,
            tracker.Update(Road(-30), now.AddMilliseconds(100)).Cue?.Direction,
            "crossing the center publishes the new direction immediately");
    }

    private static void ResetsAndRejectsInvalidRoadState()
    {
        var tracker = CreateTracker();
        var now = UtcNow();
        Equal(true, tracker.Update(Road(30), now).Cue is not null, "pre-reset cue");
        tracker.Reset();
        Equal(null, tracker.Update(Road(0), now.AddMilliseconds(10)).Cue, "reset clears correction ownership");

        Equal(
            null,
            tracker.Update(new HighwayRoadState(double.NaN, 100), now.AddMilliseconds(20)).Cue,
            "non-finite lateral input is silent");
        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(new HighwayRoadState(double.NaN, 100), now.AddMilliseconds(21)).Direction,
            "non-finite lateral input clears automatic direction");
        Equal(
            null,
            tracker.Update(new HighwayRoadState(30, 0), now.AddMilliseconds(30)).Cue,
            "non-positive road width is silent");
        Equal(
            HighwaySteeringDirection.Left,
            tracker.Update(Road(30), now.AddMilliseconds(40)).Cue?.Direction,
            "valid state after invalid input starts fresh");
    }

    private static HighwaySteeringTracker CreateTracker() =>
        new(
            normalCueInterval: TimeSpan.FromMilliseconds(700),
            criticalCueInterval: TimeSpan.FromMilliseconds(260));

    private static HighwayRoadState Road(double lateral, double halfWidth = 100) =>
        new(lateral, halfWidth);

    private static HighwayPoint Truck(double lateral, double longitudinal) =>
        new(lateral, longitudinal);

    private static DateTime UtcNow() =>
        new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

    private static void Near(double expected, double actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.001)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
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
