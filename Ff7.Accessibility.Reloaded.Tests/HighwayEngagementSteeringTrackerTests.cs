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
        ArcadeKeepsUpWithAFarAheadTruck();
        ArcadePrioritizesABikerThreateningTheTruck();
        TheLoggedArcadeFrameDrivesOnInsteadOfBrakingForABikerBehind();
        ArcadeBrakesBackTowardATruckFarBehind();
        ArcadeReturnToTheTruckNeverDisplacesAnInRangeBiker();
        ArcadeReturnToTheTruckStillAttacksABikerOnTheTruck();
        ArcadeReturnToTheTruckReleasesForTheSwordAndBadState();
        TheStoryChaseDoesNotBrakeForATruckBehind();
    }

    /// <summary>
    /// The G Bike is the arcade mode of the same native module. Its bikers still steer for
    /// the truck (FUN_00656880 heads types 10..12 at actor slot 1 with no story-mode test),
    /// and every hit on the truck takes fifty points off (FUN_00656361 -> FUN_006567B0), so
    /// the arcade gets the same truck protection as the story chase.
    /// </summary>
    private static void ArcadeKeepsUpWithAFarAheadTruck()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.Up,
            tracker.Update(ArcadeState(
                truck: new HighwayPoint(0, 900),
                Enemy(2, 0, -600))),
            "in the arcade too, a far-ahead truck outranks chasing a biker backward");
        Equal(
            HighwaySteeringDirection.Up,
            CreateTracker().Update(ArcadeState(truck: new HighwayPoint(0, 800))),
            "and with no biker the arcade bike closes the gap to the truck");
    }

    private static void ArcadePrioritizesABikerThreateningTheTruck()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.UpRight,
            tracker.Update(ArcadeState(
                truck: new HighwayPoint(0, 800),
                Enemy(2, -180, -200),
                Enemy(3, 180, 750))),
            "in the arcade the biker on the truck is chosen over the one nearest Cloud");
    }

    /// <summary>
    /// The user's first ride, 12:25:35: biker 3 behind-left at (-78.8,-226.2) and the truck
    /// 1876 ahead. Automatic steering braked toward the biker ("Down") and the truck pulled
    /// away; the ride ended "Too far from the truck" with Score 0.
    /// </summary>
    private static void TheLoggedArcadeFrameDrivesOnInsteadOfBrakingForABikerBehind()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.Up,
            tracker.Update(ArcadeState(
                truck: new HighwayPoint(58.7, 1876.2),
                Enemy(3, -78.8, -226.2))),
            "the logged frame follows the truck instead of braking for a biker out of sword range");
    }

    /// <summary>
    /// The second ride left the truck up to 1300 units behind. With nothing on the truck
    /// and nothing in sword range the arcade bike brakes back toward it, and holds that
    /// until it is back within half the comfortable distance.
    /// </summary>
    private static void ArcadeBrakesBackTowardATruckFarBehind()
    {
        var tracker = CreateTracker();

        Equal(
            HighwaySteeringDirection.Down,
            tracker.Update(ArcadeState(truck: new HighwayPoint(0, -1300))),
            "the logged truck 1300 behind: brake back toward it");
        Equal(
            HighwaySteeringDirection.Down,
            tracker.Update(ArcadeState(
                truck: new HighwayPoint(0, -1300),
                Enemy(2, 0, 600))),
            "a biker far ahead and away from the truck does not lead the bike further from it");
        Equal(
            HighwaySteeringDirection.Down,
            tracker.Update(ArcadeState(truck: new HighwayPoint(0, -400))),
            "the return holds until the truck is close again");
        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(ArcadeState(truck: new HighwayPoint(0, -200))),
            "and releases once it is");
        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(ArcadeState(truck: new HighwayPoint(0, -400))),
            "without re-entering until the truck is far behind again");
        Equal(
            HighwaySteeringDirection.Down,
            tracker.Update(ArcadeState(
                truck: new HighwayPoint(0, -1300),
                Enemy(2, 0, -600, isActive: false),
                Enemy(3, 0, -600, hitPoints: 0))),
            "inactive and defeated bikers are not engagements");
    }

    private static void ArcadeReturnToTheTruckNeverDisplacesAnInRangeBiker()
    {
        Equal(
            HighwaySteeringDirection.None,
            CreateTracker().Update(ArcadeState(
                truck: new HighwayPoint(0, -1300),
                Enemy(2, 100, 70))),
            "an attack-ready biker keeps the bike in the sword pocket even with the truck far behind");
        Equal(
            HighwaySteeringDirection.Up,
            CreateTracker().Update(ArcadeState(
                truck: new HighwayPoint(0, -1300),
                Enemy(2, 120, 100))),
            "a biker already inside the native 160-unit range is still lined up, not abandoned");
    }

    private static void ArcadeReturnToTheTruckStillAttacksABikerOnTheTruck()
    {
        Equal(
            HighwaySteeringDirection.DownLeft,
            CreateTracker().Update(ArcadeState(
                truck: new HighwayPoint(0, -1300),
                Enemy(2, -250, -1150))),
            "a biker on the far-behind truck is approached on its sword side, not just braked toward");
    }

    private static void ArcadeReturnToTheTruckReleasesForTheSwordAndBadState()
    {
        var tracker = CreateTracker();
        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(StateWithAttack(
                truck: new HighwayPoint(0, -1300),
                cloudAttackTimer: 19,
                isStoryChase: false)),
            "the native sword animation still releases every key");
        Equal(
            HighwaySteeringDirection.None,
            tracker.Update(ArcadeState(truck: new HighwayPoint(double.NaN, -1300))),
            "an unreadable truck position steers nothing");
        Equal(
            HighwaySteeringDirection.Down,
            tracker.Update(ArcadeState(truck: new HighwayPoint(0, -1300))),
            "and the return resumes on the next valid frame");
    }

    private static void TheStoryChaseDoesNotBrakeForATruckBehind()
    {
        Equal(
            HighwaySteeringDirection.None,
            CreateTracker().Update(State(truck: new HighwayPoint(0, -1300))),
            "the story chase is unchanged: a truck behind is not a reason to brake");
        Equal(
            HighwaySteeringDirection.Up,
            CreateTracker().Update(State(
                truck: new HighwayPoint(0, -1300),
                Enemy(2, 100, 600))),
            "and it still chases the biker ahead");
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
        StateWithAttack(truck, cloudAttackTimer: 0, isStoryChase: true, enemies);

    private static HighwayAccessibilityState ArcadeState(
        HighwayPoint truck,
        params HighwayEnemyState[] enemies) =>
        StateWithAttack(truck, cloudAttackTimer: 0, isStoryChase: false, enemies);

    private static HighwayAccessibilityState StateWithAttack(
        HighwayPoint truck,
        int cloudAttackTimer,
        params HighwayEnemyState[] enemies) =>
        StateWithAttack(truck, cloudAttackTimer, isStoryChase: true, enemies);

    private static HighwayAccessibilityState StateWithAttack(
        HighwayPoint truck,
        int cloudAttackTimer,
        bool isStoryChase,
        params HighwayEnemyState[] enemies) =>
        new(
            Cloud: new HighwayPoint(0, 0),
            truck,
            Array.AsReadOnly(enemies),
            Array.Empty<HighwayPartyHealth>(),
            Score: 0,
            IsStoryChase: isStoryChase,
            cloudAttackTimer);

    private static HighwayEnemyState Enemy(
        int slot,
        double lateral,
        double longitudinal,
        bool isActive = true,
        int hitPoints = 5) =>
        new(
            slot,
            NativeType: 10,
            IsActive: isActive,
            HitPoints: hitPoints,
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
