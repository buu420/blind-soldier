using Ff7.Accessibility.Reloaded;

/// <summary>
/// Routes out of Fort Condor's save room against the real convil_1 walkmesh and
/// the real field script, because the defect this guards was invisible to any
/// test built on synthetic geometry.
///
/// convil_1 is a tower. Its ladder trigger <c>ladder:355:12</c> is authored at
/// (1080, 270, 671); nothing at that height covers those coordinates and the
/// save room floor does, 653 units below. Anchoring the trigger there gave the
/// planner a ladder that appeared to lead up out of the save room, so the route
/// to the Watch Room opened with a climb the player was standing underneath
/// rather than the one they could actually reach. Auto-walk delivered them to
/// the point beneath it and oscillated: 663 units of route left and no
/// horizontal move able to close any of it.
/// </summary>
internal static class FortCondorSaveRoomRouteTests
{
    private const int FieldId = 355;

    /// <summary>Entity 15's trigger - the climb the player can actually reach.</summary>
    private const int ReachableLadderX = 1081;
    private const int ReachableLadderY = 172;
    private const int ReachableLadderZ = 20;

    /// <summary>The storey the phantom ladder was authored on.</summary>
    private const int PhantomLadderZ = 671;

    internal static void Run(
        Func<int, FieldWalkmeshReader> createReader,
        FieldScriptNavigationCatalog catalog)
    {
        var transitions = catalog.ReadField(FieldId).Transitions;
        var planner = new FieldWalkmeshRoutePlanner(
            createReader(FieldId),
            transitionProvider: _ => transitions);

        // The exact position the runtime log recorded at 09:22:01Z.
        var saveRoom = new FieldPositionSnapshot(1, FieldId, 0, 1088, 345, 8, 12, 0);
        var watchRoom = new FieldNavigationTarget(
            FieldId,
            FieldNavigationCategory.Exits,
            "Way up to the Watch Room",
            -127,
            268,
            1376,
            "gateway:355:0:356",
            DestinationFieldIds: [356]);

        Equal(
            true,
            planner.TryBuildRoute(saveRoom, watchRoom, out var plan),
            $"the Watch Room must stay reachable from the save room: {planner.LastDiagnostic}");

        var climbs = plan.Portals
            .Where(portal => portal.TransitionKind == FieldNavigationTransitionKind.Ladder)
            .ToArray();
        Equal(true, climbs.Length > 0, "leaving the save room means climbing at least one ladder");

        var first = climbs[0];
        var entry = first.Midpoint;
        Equal(
            true,
            Math.Abs(entry.Z - ReachableLadderZ) <= 64,
            $"the first climb must start on the save room's own storey, not at z={entry.Z}");
        Equal(
            true,
            Distance2D(entry, ReachableLadderX, ReachableLadderY) <= 96,
            $"the first climb must be entity 15's ladder near " +
            $"({ReachableLadderX},{ReachableLadderY}); got ({entry.X},{entry.Y},{entry.Z})");
        Equal(
            FieldNavigationInput.Up,
            first.RequiredInput,
            "the first climb out of the save room goes up");

        // Nothing on the phantom ladder's storey may appear before the player has
        // actually climbed to it.
        var portalsBeforeFirstClimb = plan.Portals
            .TakeWhile(portal => portal.TransitionKind != FieldNavigationTransitionKind.Ladder)
            .ToArray();
        foreach (var portal in portalsBeforeFirstClimb)
        {
            Equal(
                true,
                Math.Abs(portal.Midpoint.Z - PhantomLadderZ) > 192,
                $"no step at the upper storey may precede the first climb; " +
                $"found one at z={portal.Midpoint.Z}");
        }
    }

    private static double Distance2D(FieldNavigationRouteWaypoint waypoint, int x, int y)
    {
        double dx = waypoint.X - x;
        double dy = waypoint.Y - y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
