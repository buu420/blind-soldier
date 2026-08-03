using Ff7.Accessibility.Core;

internal static class HighwayAccessibilityTrackerTests
{
    internal static void Run()
    {
        MarksNativeSwordRangeAndTruckThreatsImportant();
        SelectsOneLowerPriorityBikerAndInterleavesTheTruckBeacon();
        AssignsStableSquareAndCircleAttackSides();
        ArmsTheDistanceWarningOnlyAfterCloudReachesTheTruck();
        ReportsVisibleStatusOnDemandAndResetsCleanly();
        ExposesNoHitOrDefeatConfirmationRequests();
    }

    private static void AssignsStableSquareAndCircleAttackSides()
    {
        var tracker = CreateTracker();
        var now = UtcNow();
        var left = State(
            cloud: new HighwayPoint(0, 0),
            truck: new HighwayPoint(0, 200),
            enemies: [Enemy(2, new HighwayPoint(-40, 100))]);

        Equal(
            HighwayAttackSide.LeftSquare,
            tracker.Update(left, now, statusRequested: false).Cue?.AttackSide,
            "biker left of Cloud requests Square");

        var justRightOfCenter = State(
            cloud: new HighwayPoint(0, 0),
            truck: new HighwayPoint(0, 200),
            enemies: [Enemy(2, new HighwayPoint(5, 100))]);
        Equal(
            HighwayAttackSide.LeftSquare,
            tracker.Update(justRightOfCenter, now.AddMilliseconds(300), statusRequested: false).Cue?.AttackSide,
            "center-line deadband retains the prior attack side");

        var clearlyRight = State(
            cloud: new HighwayPoint(0, 0),
            truck: new HighwayPoint(0, 200),
            enemies: [Enemy(2, new HighwayPoint(40, 100))]);
        Equal(
            HighwayAttackSide.RightCircle,
            tracker.Update(clearlyRight, now.AddMilliseconds(600), statusRequested: false).Cue?.AttackSide,
            "biker clearly right of Cloud requests Circle");

        tracker.Reset();
        Equal(
            HighwayAttackSide.RightCircle,
            tracker.Update(clearlyRight, now.AddMilliseconds(610), statusRequested: false).Cue?.AttackSide,
            "reset does not retain a stale side for the next chase");
    }

    private static void MarksNativeSwordRangeAndTruckThreatsImportant()
    {
        var tracker = CreateTracker();
        var now = UtcNow();
        var swordRangeState = State(
            cloud: new HighwayPoint(0, 0),
            truck: new HighwayPoint(0, 800),
            enemies:
            [
                Enemy(2, new HighwayPoint(80, 100)),
                Enemy(3, new HighwayPoint(500, 300))
            ]);

        var swordUpdate = tracker.Update(swordRangeState, now, statusRequested: false);
        Equal(HighwayCueKind.ImportantEnemy, swordUpdate.Cue?.Kind, "native sword-range cue kind");
        Equal(2, swordUpdate.Cue?.TargetSlot, "native sword-range selected slot");
        Near(80d, swordUpdate.Cue?.DeltaLateral ?? double.NaN, "sword-range lateral delta");
        Near(100d, swordUpdate.Cue?.DeltaLongitudinal ?? double.NaN, "sword-range longitudinal delta");

        tracker.Reset();
        var truckThreatState = State(
            cloud: new HighwayPoint(0, 0),
            truck: new HighwayPoint(0, 800),
            enemies:
            [
                Enemy(2, new HighwayPoint(500, 300)),
                Enemy(3, new HighwayPoint(40, 760))
            ]);
        var threatUpdate = tracker.Update(truckThreatState, now, statusRequested: false);
        Equal(HighwayCueKind.ImportantEnemy, threatUpdate.Cue?.Kind, "truck-threat cue kind");
        Equal(3, threatUpdate.Cue?.TargetSlot, "nearest truck threat selected first");
    }

    private static void SelectsOneLowerPriorityBikerAndInterleavesTheTruckBeacon()
    {
        var tracker = CreateTracker();
        var now = UtcNow();
        var state = State(
            cloud: new HighwayPoint(0, 0),
            truck: new HighwayPoint(0, 800),
            enemies:
            [
                Enemy(2, new HighwayPoint(260, 220)),
                Enemy(3, new HighwayPoint(-450, 100))
            ]);

        var first = tracker.Update(state, now, statusRequested: false);
        Equal(HighwayCueKind.LowerPriorityEnemy, first.Cue?.Kind, "first lower-priority biker cue");
        Equal(2, first.Cue?.TargetSlot, "nearest lower-priority biker selected");

        var tooSoon = tracker.Update(state, now.AddMilliseconds(100), statusRequested: false);
        Equal(null, tooSoon.Cue, "global timing gate starts no overlapping cue");

        var second = tracker.Update(state, now.AddMilliseconds(300), statusRequested: false);
        Equal(HighwayCueKind.TruckBeacon, second.Cue?.Kind, "truck beacon interleaves after an enemy cue");
        Equal(1, second.Cue?.TargetSlot, "native truck slot");

        var third = tracker.Update(state, now.AddMilliseconds(600), statusRequested: false);
        Equal(HighwayCueKind.LowerPriorityEnemy, third.Cue?.Kind, "enemy cue resumes after truck beacon");
        Equal(1, Enum.GetValues<HighwayCueKind>().Count(kind => kind == HighwayCueKind.TruckBeacon), "one truck cue enum value");
    }

    private static void ArmsTheDistanceWarningOnlyAfterCloudReachesTheTruck()
    {
        var tracker = CreateTracker();
        var now = UtcNow();
        var far = State(
            cloud: new HighwayPoint(0, 0),
            truck: new HighwayPoint(0, 1500),
            enemies: [Enemy(2, new HighwayPoint(300, 300))]);
        Equal(
            null,
            tracker.Update(far, now, statusRequested: false).Speech,
            "staged starting distance does not produce a false warning");

        var recovered = State(
            cloud: new HighwayPoint(0, 700),
            truck: new HighwayPoint(0, 1500),
            enemies: [Enemy(2, new HighwayPoint(300, 900))]);
        Equal(
            null,
            tracker.Update(recovered, now.AddMilliseconds(300), statusRequested: false).Speech,
            "entering the recovery radius only arms the warning");

        var leftBehind = State(
            cloud: new HighwayPoint(0, 200),
            truck: new HighwayPoint(0, 1500),
            enemies: [Enemy(2, new HighwayPoint(300, 900))]);
        var warning = tracker.Update(leftBehind, now.AddMilliseconds(600), statusRequested: false);
        Equal(HighwaySpeechKind.Warning, warning.Speech?.Kind, "far-distance warning kind");
        Equal("Too far from the truck.", warning.Speech?.Text, "far-distance warning text");
        Equal(null, warning.Cue, "warning speech owns its update without a competing cue");

        Equal(
            null,
            tracker.Update(leftBehind, now.AddMilliseconds(900), statusRequested: false).Speech,
            "remaining far does not repeat the warning");

        tracker.Update(recovered, now.AddMilliseconds(1200), statusRequested: false);
        var warningAgain = tracker.Update(leftBehind, now.AddMilliseconds(1500), statusRequested: false);
        Equal(
            "Too far from the truck.",
            warningAgain.Speech?.Text,
            "returning to the truck rearms one later warning");
    }

    private static void ReportsVisibleStatusOnDemandAndResetsCleanly()
    {
        var tracker = CreateTracker();
        var now = UtcNow();
        var state = State(
            cloud: new HighwayPoint(0, 0),
            truck: new HighwayPoint(200, 800),
            enemies:
            [
                Enemy(2, new HighwayPoint(260, 220)),
                Enemy(3, new HighwayPoint(-450, 100), active: false)
            ],
            health:
            [
                new HighwayPartyHealth("Cloud", 700, 900),
                new HighwayPartyHealth("Barret", 610, 650)
            ],
            score: 3210);

        var status = tracker.Update(state, now, statusRequested: true);
        Equal(HighwaySpeechKind.Status, status.Speech?.Kind, "on-demand status kind");
        Contains(status.Speech?.Text, "1 biker active", "on-demand active biker count");
        Contains(status.Speech?.Text, "Truck ahead right", "on-demand visible truck direction");
        Contains(status.Speech?.Text, "Score 3210", "on-demand visible score");
        Contains(status.Speech?.Text, "Cloud 700 of 900", "on-demand party health");
        Equal(null, status.Cue, "status speech owns its update without a competing cue");

        tracker.Reset();
        var afterReset = tracker.Update(state, now.AddMilliseconds(10), statusRequested: false);
        Equal(
            HighwayCueKind.LowerPriorityEnemy,
            afterReset.Cue?.Kind,
            "reset clears prior timing and selection state");
    }

    private static void ExposesNoHitOrDefeatConfirmationRequests()
    {
        var names = Enum.GetNames<HighwayCueKind>();
        Equal(3, names.Length, "only approved spatial cue kinds exist");
        Equal(false, names.Any(name => name.Contains("Hit", StringComparison.OrdinalIgnoreCase)), "no hit cue kind");
        Equal(false, names.Any(name => name.Contains("Defeat", StringComparison.OrdinalIgnoreCase)), "no defeat cue kind");
    }

    private static HighwayAccessibilityTracker CreateTracker() =>
        new(
            enemyCueInterval: TimeSpan.FromMilliseconds(300),
            truckCueInterval: TimeSpan.FromMilliseconds(300),
            comfortableTruckDistance: 500,
            truckThreatDistance: 300,
            warningDistance: 1200,
            warningRecoveryDistance: 900);

    private static HighwayAccessibilityState State(
        HighwayPoint cloud,
        HighwayPoint truck,
        IReadOnlyList<HighwayEnemyState> enemies,
        IReadOnlyList<HighwayPartyHealth>? health = null,
        int score = 0) =>
        new(
            cloud,
            truck,
            enemies,
            health ?? Array.Empty<HighwayPartyHealth>(),
            score,
            IsStoryChase: true);

    private static HighwayEnemyState Enemy(int slot, HighwayPoint position, bool active = true) =>
        new(slot, NativeType: 10, active, HitPoints: active ? 5 : 0, position);

    private static DateTime UtcNow() =>
        new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

    private static void Contains(string? actual, string expected, string label)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}: expected '{actual ?? "<null>"}' to contain '{expected}'.");
        }
    }

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
