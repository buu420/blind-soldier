using Ff7.Accessibility.Reloaded;

internal static class FieldAutomaticMovementClearanceTests
{
    internal static void Run(Func<int, FieldWalkmeshReader> createReader)
    {
        // The user's 2026-09-22 Cosmo Canyon stall, on the upper path.
        // A running sample can cross the narrow portal even though its first
        // eight units are clear. NPC presence must not determine wall clearance.
        var position = new FieldPositionSnapshot(1, 525, 0, -677, -551, -1468, 314, 0);
        var target = new FieldNavigationTarget(525, FieldNavigationCategory.Story,
            "Upper path", -646, -595, -1468);
        var aroundCorner = new FieldNavigationRouteWaypoint(-659, -596, -1468);
        var acrossWall = new FieldNavigationRouteWaypoint(-643, -585, -1468);
        foreach (var withEmptyProvider in new[] { false, true })
        {
            var planner = new FieldWalkmeshRoutePlanner(createReader(525),
                dynamicObstacleProvider: withEmptyProvider ? (_, _) => [] : null);
            Check(!planner.IsAutomaticMovementClear(position, target, acrossWall),
                "an empty NPC list must still reject running across the Cosmo wall");
            Check(planner.IsAutomaticMovementClear(position, target, aroundCorner),
                "the walkable opening must remain available without nearby NPCs");
        }

        var unavailable = new FieldWalkmeshRoutePlanner(new FieldWalkmeshReader(_ => 0, _ => 0));
        Check(!unavailable.IsAutomaticMovementClear(position, target, aroundCorner),
            "an unreadable field must not authorize movement");
        Check(!unavailable.LastReadWasCoherent, "unreadable geometry must be reported to the caller");

        var locked = new FieldWalkmeshRoutePlanner(createReader(525), Boundaries(0));
        Check(!locked.IsAutomaticMovementClear(position, target, aroundCorner),
            "native locks still block movement when no NPCs are nearby");

        CosmoUpperPathUsesTheWiderApproach(createReader);
    }

    private static void CosmoUpperPathUsesTheWiderApproach(Func<int, FieldWalkmeshReader> createReader)
    {
        var planner = new FieldWalkmeshRoutePlanner(createReader(525));
        var start = new FieldPositionSnapshot(1, 525, 0, -677, -551, -1468, 314, 32);
        var mesh = createReader(525).Read(start).Walkmesh!;
        Check(FieldWalkmeshPathfinder.ResolveTriangle(mesh, -772, -698, -1468, -1) == 9,
            "the captured wider-approach checkpoint lies on installed triangle 9");
        var target = new FieldNavigationTarget(525, FieldNavigationCategory.Story,
            "Take the upper path to the sealed door", -494, -504, -1468);
        Check(planner.TryBuildRoute(start, target, out var plan), "upper path still has a native route");
        Check(!plan.TrianglePath.Contains(0), "upper path must avoid the repeatedly stalled thin triangle");
        Check(plan.StableWaypointsOverride?.Any(step => step.MustReach &&
                step.Waypoint is { X: -772, Y: -698, Z: -1468 }) == true,
            "the wider approach must remain a required checkpoint during lookahead");

        var reverse = new FieldPositionSnapshot(1, 525, 0, -600, -630, -1468, 65, 0);
        Check(planner.TryBuildRoute(reverse, target with { X = start.X, Y = start.Y }, out var returnPlan),
            "the upper path remains reachable in reverse");
        Check(!returnPlan.TrianglePath.Contains(0), "the return route avoids the same pinch point");

        var alreadyPast = start with { X = -772, Y = -698, TriangleId = 9 };
        Check(planner.TryBuildRoute(alreadyPast, target, out var pastPlan), "the route continues past the checkpoint");
        Check(pastPlan.StableWaypointsOverride is null,
            "the approach must not pull the player back after reaching it");

        // The actual story row ends on the exit line and the user's capture had
        // IDLCK 299/300 set. Test that combination, not just a coordinate target.
        var exit = target with
        {
            TriggerLine = new(-536, -468, -1468, -452, -541, -1468),
            CompletesOnArrival = true
        };
        var lockedPlanner = new FieldWalkmeshRoutePlanner(createReader(525), Boundaries(299, 300));
        Check(lockedPlanner.TryBuildRoute(start, exit, out var exitPlan),
            "the captured story exit remains reachable with its native locks");
        Check(!exitPlan.TrianglePath.Any(triangle => triangle is 0 or 299 or 300),
            "the wider story route crosses neither the pinch point nor locked triangles");

        var shutCheckpoint = new FieldWalkmeshRoutePlanner(createReader(525), Boundaries(9));
        Check(!shutCheckpoint.TryBuildRoute(start, target, out _),
            "an authored approach must never override a native lock on its checkpoint");
    }

    private static FieldBoundaryStateReader Boundaries(params int[] locked)
    {
        const int state = 0x2800000;
        var bits = new byte[64];
        foreach (var triangle in locked) bits[triangle >> 3] |= (byte)(1 << (triangle & 7));
        return new FieldBoundaryStateReader(
            address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? state : 0,
            address => address switch
            {
                FieldPositionReader.AddressCurrentModule => 1,
                FieldPositionReader.AddressFieldId => 13,
                FieldPositionReader.AddressFieldId + 1 => 2,
                >= state + FieldBoundaryStateReader.BoundaryBitsOffset and
                < state + FieldBoundaryStateReader.BoundaryBitsOffset + 64 =>
                    bits[address - state - FieldBoundaryStateReader.BoundaryBitsOffset],
                _ => 0
            },
            (_, _) => true);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Automatic movement clearance: " + message);
    }
}
