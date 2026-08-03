using Ff7.Accessibility.Core;

internal static class HighwayEngagementSteeringTrackerTests
{
    internal static void Run()
    {
        DrivesTowardAnAheadRightBikerWithoutRoadGeometry();
        ApproachesAnOffAxisBikerOutsideSwordRange();
        MovesABehindRightBikerIntoTheNativeCircleAttackArc();
        StopsInsideTheNativeSwordAttackCorridor();
        ReleasesSteeringDuringTheNativeSwordAnimation();
        FollowsTheTruckForwardWhenNoBikerIsActive();
        DoesNotChaseBehindWhenTheTruckIsAlreadyFarAhead();
        PrioritizesABikerThreateningTheTruck();
        UsesAxisHysteresisUntilTheAttackCorridorIsReached();
        ResetClearsHeldAxisCorrections();
    }

    private static void DrivesTowardAnAheadRightBikerWithoutRoadGeometry()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.UpRight,
            tracker.Update(State(
                truck: new HighwayPoint(0, 450),
                Enemy(2, 160, 200))),
            "ahead-right biker produces a diagonal approach from combat coordinates");
    }

    private static void StopsInsideTheNativeSwordAttackCorridor()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(State(
                truck: new HighwayPoint(0, 300),
                Enemy(2, 100, 70))),
            "attack-ready biker releases movement so the player can swing");
    }

    private static void ApproachesAnOffAxisBikerOutsideSwordRange()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.Up,
            tracker.Update(State(
                truck: new HighwayPoint(0, 300),
                Enemy(2, 120, 120))),
            "an off-axis biker outside the circular sword range cannot occupy a steering dead zone");
    }

    private static void MovesABehindRightBikerIntoTheNativeCircleAttackArc()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.DownLeft,
            tracker.Update(State(
                truck: new HighwayPoint(0, 300),
                Enemy(2, 60, -70))),
            "a close biker behind Cloud is moved into the native right-sword angle instead of treated as attack-ready");
    }

    private static void FollowsTheTruckForwardWhenNoBikerIsActive()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.Up,
            tracker.Update(State(truck: new HighwayPoint(0, 800))),
            "auto steering closes a large forward gap to the truck");
    }

    private static void ReleasesSteeringDuringTheNativeSwordAnimation()
    {
        var tracker = CreateTracker();
        var approaching = State(
            truck: new HighwayPoint(0, 300),
            Enemy(2, 160, 200));

        Equal(
            HighwaySteeringDirection.UpRight,
            tracker.Update(approaching),
            "auto steering first approaches the attack target");
        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(StateWithAttack(
                truck: new HighwayPoint(0, 300),
                cloudAttackTimer: 19,
                Enemy(2, 160, 200))),
            "positive native sword timer releases all automatic movement");
        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(StateWithAttack(
                truck: new HighwayPoint(0, 300),
                cloudAttackTimer: -19,
                Enemy(2, 160, 200))),
            "negative native sword timer releases all automatic movement");
        Equal(
            HighwaySteeringDirection.UpRight,
            tracker.Update(approaching),
            "automatic approach resumes after the sword animation ends");
    }

    private static void DoesNotChaseBehindWhenTheTruckIsAlreadyFarAhead()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.Up,
            tracker.Update(State(
                truck: new HighwayPoint(0, 900),
                Enemy(2, 0, -600))),
            "truck protection overrides chasing a lower-priority biker backward");
    }

    private static void PrioritizesABikerThreateningTheTruck()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.UpRight,
            tracker.Update(State(
                truck: new HighwayPoint(0, 800),
                Enemy(2, -180, -200),
                Enemy(3, 180, 750))),
            "biker nearest the truck determines the horizontal approach");
    }

    private static void UsesAxisHysteresisUntilTheAttackCorridorIsReached()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.UpRight,
            tracker.Update(State(
                truck: new HighwayPoint(0, 300),
                Enemy(2, 150, 140))),
            "correction enters outside the attack corridor");
        Equal(
            HighwaySteeringDirection.UpRight,
            tracker.Update(State(
                truck: new HighwayPoint(0, 300),
                Enemy(2, 120, 100))),
            "correction remains active through the hysteresis band");
        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(State(
                truck: new HighwayPoint(0, 300),
                Enemy(2, 100, 70))),
            "both axes release inside the hand-checked native sword attack pocket");
    }

    private static void ResetClearsHeldAxisCorrections()
    {
        var tracker = CreateTracker();
        _ = tracker.Update(State(
            truck: new HighwayPoint(0, 300),
            Enemy(2, 160, 200)));

        tracker.Reset();

        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(State(
                truck: new HighwayPoint(0, 300),
                Enemy(2, 120, 70))),
            "reset removes stale lateral correction ownership");
    }

    private static HighwayEngagementSteeringTracker CreateTracker() =>
        new(
            comfortableTruckDistance: 500,
            truckThreatDistance: 300);

    private static HighwayAccessibilityState State(
        HighwayPoint truck,
        params HighwayEnemyState[] enemies) =>
        StateWithAttack(truck, cloudAttackTimer: 0, enemies);

    private static HighwayAccessibilityState StateWithAttack(
        HighwayPoint truck,
        int cloudAttackTimer,
        params HighwayEnemyState[] enemies) =>
        new(
            Cloud: new HighwayPoint(0, 0),
            truck,
            Array.AsReadOnly(enemies),
            Array.Empty<HighwayPartyHealth>(),
            Score: 0,
            IsStoryChase: true,
            cloudAttackTimer);

    private static HighwayEnemyState Enemy(
        int slot,
        double lateral,
        double longitudinal) =>
        new(
            slot,
            NativeType: 10,
            IsActive: true,
            HitPoints: 5,
            new HighwayPoint(lateral, longitudinal));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }
}
