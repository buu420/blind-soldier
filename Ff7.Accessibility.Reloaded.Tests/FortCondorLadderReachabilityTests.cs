using Ff7.Accessibility.Reloaded;

/// <summary>
/// While the player climbs a ladder FF7 keeps the native triangle id on the
/// triangle they left while their coordinates travel up the rungs, so the
/// walkmesh planner cannot resolve them and every exit in the field fails to
/// route at once. The provider used to answer that with an empty list, which
/// made auto-walk abandon its target on every climb - crippling in Fort Condor,
/// which is a tower of ladders.
/// </summary>
internal static class FortCondorLadderReachabilityTests
{
    private const int FieldId = 355;

    internal static void Run()
    {
        HoldsExitsThroughAnUnresolvablePositionMidClimb();
        StillReportsAFieldWhoseExitsAreGenuinelyAllBlocked();
        StopsHoldingOnceTheWindowPasses();
        DropsTheHeldSetWhenTheFieldChanges();
    }

    private static void HoldsExitsThroughAnUnresolvablePositionMidClimb()
    {
        // Positions replayed from the runtime log at 13:00:24Z-13:00:29Z.
        var clock = new TestClock(new DateTime(2026, 8, 19, 13, 0, 24, DateTimeKind.Utc));
        var routable = true;
        var provider = new ReachableFieldExitTargetProvider(
            _ => Exits(),
            new StubPlanner(() => routable),
            () => clock.UtcNow);

        var onMesh = Position(850, 231, 1115, triangle: 29);
        Equal(3, provider.ReadTargets(onMesh).Count, "standing on the walkmesh reports every exit");

        // Mid-climb: coordinates are up the ladder, the triangle id is stale.
        routable = false;
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Equal(
            3,
            provider.ReadTargets(Position(869, 690, 1200, triangle: 29)).Count,
            "an unresolvable mid-climb position must not empty the exit list");
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Equal(
            3,
            provider.ReadTargets(Position(888, 693, 1200, triangle: 29)).Count,
            "the exit list survives the whole climb");

        // Top of the ladder: the native triangle catches up.
        routable = true;
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Equal(
            3,
            provider.ReadTargets(Position(879, 744, 1104, triangle: 168)).Count,
            "the exit list is intact once the triangle catches up");
    }

    private static void StillReportsAFieldWhoseExitsAreGenuinelyAllBlocked()
    {
        var clock = new TestClock(new DateTime(2026, 8, 19, 13, 0, 0, DateTimeKind.Utc));
        var provider = new ReachableFieldExitTargetProvider(
            _ => Exits(),
            new StubPlanner(() => false),
            () => clock.UtcNow);

        Equal(
            0,
            provider.ReadTargets(Position(850, 231, 1115, triangle: 29)).Count,
            "a field that never had a reachable exit still reports none");
    }

    private static void StopsHoldingOnceTheWindowPasses()
    {
        var clock = new TestClock(new DateTime(2026, 8, 19, 13, 0, 0, DateTimeKind.Utc));
        var routable = true;
        var provider = new ReachableFieldExitTargetProvider(
            _ => Exits(),
            new StubPlanner(() => routable),
            () => clock.UtcNow);

        var start = Position(850, 231, 1115, triangle: 29);
        Equal(3, provider.ReadTargets(start).Count, "seed a reachable set");

        routable = false;
        clock.Advance(TimeSpan.FromSeconds(5));
        Equal(
            0,
            provider.ReadTargets(start).Count,
            "a lasting routing failure is not a climb and must not be held open forever");
    }

    private static void DropsTheHeldSetWhenTheFieldChanges()
    {
        var clock = new TestClock(new DateTime(2026, 8, 19, 13, 0, 0, DateTimeKind.Utc));
        var routable = true;
        var provider = new ReachableFieldExitTargetProvider(
            _ => Exits(),
            new StubPlanner(() => routable),
            () => clock.UtcNow);

        Equal(3, provider.ReadTargets(Position(850, 231, 1115, triangle: 29)).Count, "seed a reachable set");

        routable = false;
        clock.Advance(TimeSpan.FromMilliseconds(200));
        var elsewhere = new FieldPositionSnapshot(1, 356, 0, 0, 0, 0, 1, 0);
        Equal(
            0,
            provider.ReadTargets(elsewhere).Count,
            "one field's exits must never be held open for a different field");
    }

    private static FieldPositionSnapshot Position(int x, int y, int z, ushort triangle) =>
        new(1, FieldId, 0, x, y, z, triangle, 128);

    private static IReadOnlyList<FieldNavigationTarget> Exits() =>
    [
        Exit("gateway:355:0:356", "Way up to the Watch Room", -127, 268, 1376, 356),
        Exit("script-exit:355:3:354", "Ladder down to the fort entrance", -396, 148, -174, 354),
        Exit("script-exit:355:4:354", "Ladder down to the fort entrance", -362, 80, -174, 354)
    ];

    private static FieldNavigationTarget Exit(
        string stableId, string label, int x, int y, int z, int destination) =>
        new(
            FieldId,
            FieldNavigationCategory.Exits,
            label,
            x,
            y,
            z,
            stableId,
            DestinationFieldIds: [destination]);

    private sealed class TestClock(DateTime start)
    {
        public DateTime UtcNow { get; private set; } = start;

        public void Advance(TimeSpan amount) => UtcNow += amount;
    }

    private sealed class StubPlanner(Func<bool> routable) : IFieldNavigationRoutePlanner
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
            plan = default;
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

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
