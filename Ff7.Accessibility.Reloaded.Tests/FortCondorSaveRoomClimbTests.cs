using Ff7.Accessibility.Reloaded;

/// <summary>
/// Fort Condor's save room sits at the foot of a full-height ladder, and the
/// only way out of it is up. The runtime log at 18:58:03Z and 18:58:06Z caught
/// the route being rebuilt twice inside four seconds while the player was
/// halfway up that ladder, each time with its next waypoint back at the mount
/// point - so auto-walk turned around and started steering them back down the
/// ladder they were climbing.
///
/// A climb travels in Z while the planner reasons in X and Y. Two frames apart
/// the player has made no horizontal progress and the walkmesh re-derives their
/// triangle from those unchanged X,Y as one at the foot of the ladder, which is
/// no longer on the committed route. Every deviation detector reads that as a
/// backtrack. The climb is not a deviation, so the route is held instead.
/// </summary>
internal static class FortCondorSaveRoomClimbTests
{
    private const int FieldId = 355;

    // Triangle 157 carries the ladder, 176 is the landing at the top; 96 is the
    // floor tile the walkmesh re-derives from the climber's unchanged X,Y.
    private const int LadderTriangle = 157;
    private const int LandingTriangle = 176;
    private const int FloorBelowLadder = 96;

    internal static void Run()
    {
        HoldsTheRouteThroughTheClimbInsteadOfSteeringBackDown();
        TheHeldClimbWouldOtherwiseHaveReplanned();
        ResumesNormallyOnceTheClimbEnds();
        HoldsExitsForAsLongAsTheScriptedMovementLasts();
        NeverHoldsAnExitTheFieldHasTakenAway();
    }

    /// <summary>
    /// The climb itself: mount at Z=575 and ride to Z=1142, exactly the run the
    /// log recorded. The route must survive it untouched.
    /// </summary>
    private static void HoldsTheRouteThroughTheClimbInsteadOfSteeringBackDown()
    {
        var planner = new ClimbAwarePlanner();
        var tracker = new FieldNavigationRouteTracker(planner);
        var target = WatchRoomExit();

        Equal(
            true,
            tracker.TryStart(Mounting(), target, out var start),
            "the route to the watch room is planned from the foot of the ladder");
        var mountWaypoint = start.Waypoint;
        Equal(1, planner.BuildCount, "starting navigation plans the route once");

        foreach (var z in ClimbHeights())
        {
            Equal(
                true,
                tracker.TryHold(Climbing(z), target, out var held),
                "the committed route stays available for every rung");
            Equal(false, held.Replanned, $"the route must not be replanned at z={z}");
            Equal(
                mountWaypoint,
                held.Waypoint,
                $"guidance at z={z} must not point back at the mount point");
        }

        Equal(1, planner.BuildCount, "no rung of the climb may rebuild the route");
    }

    /// <summary>
    /// Guards the test above: if the ordinary update path stopped replanning on
    /// these frames for some unrelated reason, holding them would prove nothing.
    /// </summary>
    private static void TheHeldClimbWouldOtherwiseHaveReplanned()
    {
        var planner = new ClimbAwarePlanner();
        var tracker = new FieldNavigationRouteTracker(planner);
        var target = WatchRoomExit();
        tracker.TryStart(Mounting(), target, out _);

        var replanned = false;
        foreach (var z in ClimbHeights())
        {
            if (tracker.TryUpdate(Climbing(z), target, out var guidance) && guidance.Replanned)
            {
                replanned = true;
            }
        }

        Equal(
            true,
            replanned,
            "the ordinary update path is what rebuilt the route mid-climb in the log");
    }

    /// <summary>
    /// The ladder is a portal on the route already, so nothing has to be handed
    /// back when the hold ends - the first ordinary update after the dismount
    /// resolves the landing triangle and carries on.
    /// </summary>
    private static void ResumesNormallyOnceTheClimbEnds()
    {
        var planner = new ClimbAwarePlanner();
        var tracker = new FieldNavigationRouteTracker(planner);
        var target = WatchRoomExit();
        tracker.TryStart(Mounting(), target, out _);
        foreach (var z in ClimbHeights())
        {
            tracker.TryHold(Climbing(z), target, out _);
        }

        var landed = new FieldPositionSnapshot(1, FieldId, 0, 197, 580, 1153, LandingTriangle, 144);
        Equal(
            true,
            tracker.TryUpdate(landed, target, out var resumed),
            "the route is still live at the top of the ladder");
        Equal(false, resumed.Replanned, "landing on the route's own portal is not a deviation");
        Equal(1, planner.BuildCount, "the whole climb costs a single route build");
    }

    /// <summary>
    /// The watch room's lookout is reached by walking onto a trigger line that
    /// hands the player to a script: it jumps them onto a walkmesh triangle
    /// nothing else connects to, holds them there while the fort's commander
    /// talks, then jumps them back down. The log has that lasting from 18:58:54Z
    /// to 18:58:57Z with every exit reported blocked throughout. No timer can
    /// separate that from a genuine dead end, but the game says outright that
    /// the player is not the one moving.
    /// </summary>
    private static void HoldsExitsForAsLongAsTheScriptedMovementLasts()
    {
        var clock = new TestClock(new DateTime(2026, 8, 19, 18, 58, 53, DateTimeKind.Utc));
        var routable = true;
        var provider = new ReachableFieldExitTargetProvider(
            _ => WatchRoomExits(),
            new AlwaysStubPlanner(() => routable),
            () => clock.UtcNow);

        var onTheFloor = new FieldPositionSnapshot(1, 356, 0, -73, 11, -10, 10, 0);
        Equal(1, provider.ReadTargets(onTheFloor).Count, "the way out is reachable from the floor");

        // Parked on the scripted ledge, far longer than the transient window.
        routable = false;
        var ledge = new FieldPositionSnapshot(1, 356, 0, -53, 90, 25, 24, 128);
        clock.Advance(TimeSpan.FromSeconds(3));
        Equal(
            1,
            provider.ReadTargets(ledge, positionIsScripted: true).Count,
            "an exit must not disappear while a script is moving the player");

        clock.Advance(TimeSpan.FromSeconds(30));
        Equal(
            1,
            provider.ReadTargets(ledge, positionIsScripted: true).Count,
            "the hold lasts as long as the scripted movement does, not as long as a timer");

        Equal(
            0,
            provider.ReadTargets(ledge).Count,
            "once the player has their controls back an unroutable position is real");
    }

    /// <summary>
    /// Holding must never invent a doorway. A gateway the script switches off
    /// leaves the native list, and the held set may not outlive it.
    /// </summary>
    private static void NeverHoldsAnExitTheFieldHasTakenAway()
    {
        var clock = new TestClock(new DateTime(2026, 8, 19, 18, 58, 53, DateTimeKind.Utc));
        var routable = true;
        var native = WatchRoomExits();
        var provider = new ReachableFieldExitTargetProvider(
            _ => native,
            new AlwaysStubPlanner(() => routable),
            () => clock.UtcNow);

        var onTheFloor = new FieldPositionSnapshot(1, 356, 0, -73, 11, -10, 10, 0);
        Equal(1, provider.ReadTargets(onTheFloor).Count, "seed a reachable set");

        routable = false;
        native = [];
        Equal(
            0,
            provider.ReadTargets(onTheFloor, positionIsScripted: true).Count,
            "an exit the game has removed must not survive in the held set");
    }

    // The Z track the log recorded between mounting at 18:58:01Z and the
    // dismount at 18:58:07Z. X and Y barely move across the whole climb.
    private static IEnumerable<int> ClimbHeights()
    {
        for (var z = 585; z <= 1142; z += 31)
        {
            yield return z;
        }
    }

    private static FieldPositionSnapshot Mounting() =>
        new(1, FieldId, 0, 201, 947, 572, LadderTriangle, 128);

    private static FieldPositionSnapshot Climbing(int z) =>
        new(1, FieldId, 0, 200, 900 - ((z - 585) / 2), z, LadderTriangle, 144);

    private static FieldNavigationTarget WatchRoomExit() =>
        new(
            FieldId,
            FieldNavigationCategory.Exits,
            "Way up to the Watch Room",
            -127,
            268,
            1376,
            "gateway:355:0:356",
            DestinationFieldIds: [356]);

    private static IReadOnlyList<FieldNavigationTarget> WatchRoomExits() =>
    [
        new FieldNavigationTarget(
            356,
            FieldNavigationCategory.Exits,
            "Way back down into Fort Condor",
            175,
            -66,
            -116,
            "gateway:356:0:355",
            DestinationFieldIds: [355])
    ];

    /// <summary>
    /// Stands in for the walkmesh planner's behaviour on a ladder: the climber's
    /// X and Y sit over the floor tile at the foot of the ladder, so that is the
    /// triangle it resolves once they leave the ground.
    /// </summary>
    private sealed class ClimbAwarePlanner : IFieldNavigationRoutePlanner
    {
        public int BuildCount { get; private set; }

        public string LastDiagnostic { get; private set; } = string.Empty;

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.Z > 580 && position.Z < 1150 ? FloorBelowLadder : position.TriangleId;
            return true;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            BuildCount++;
            LastDiagnostic = $"climb-aware build {BuildCount}";
            plan = new FieldNavigationRoutePlan(
                position.FieldId,
                $"{target.FieldId}:{target.StableId}",
                [LadderTriangle, LandingTriangle],
                [
                    new FieldNavigationRoutePortal(
                        LadderTriangle,
                        LandingTriangle,
                        new FieldNavigationRouteWaypoint(186, 979, 587),
                        new FieldNavigationRouteWaypoint(186, 979, 587),
                        FieldNavigationTransitionKind.Ladder,
                        "ladder:355:7:18:8:176",
                        FieldNavigationInput.Up,
                        new FieldNavigationRouteWaypoint(197, 580, 1153),
                        RequiresAction: true)
                ],
                new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z),
                LandingTriangle);
            return position.FieldId == target.FieldId;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
            return position.FieldId == target.FieldId;
        }
    }

    private sealed class AlwaysStubPlanner(Func<bool> routable) : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => routable() ? "routed" : "no route from resolved triangle";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return routable();
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = default!;
            return routable();
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
            return routable();
        }
    }

    private sealed class TestClock(DateTime start)
    {
        public DateTime UtcNow { get; private set; } = start;

        public void Advance(TimeSpan amount) => UtcNow += amount;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
