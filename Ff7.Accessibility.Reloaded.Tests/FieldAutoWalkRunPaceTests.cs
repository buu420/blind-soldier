using Ff7.Accessibility.Reloaded;

/// <summary>
/// Auto walk when the party is running, and what happens when it stops.
///
/// <para>The reported symptom was that auto walk "freezes or pauses, mostly while holding
/// the Run button". The clearance probe is why. When auto walk resolves a direction it
/// asks the native movement planner whether that direction is clear, and it asked for a
/// fixed eight units ahead whatever the party's speed. The recorded session measures the
/// speed: between two navigation samples the party covers about sixteen units walking and
/// forty-five to forty-eight running - 637 to 589 in one sample, then 589 to 573 to 529.
/// </para>
///
/// <para>So a direction can be clear for the eight units that were checked and blocked at
/// twenty. The party runs into the wall and stops; the next sample measures the same clear
/// eight units, asks for the same direction, and the party stays there. It is worse with
/// Run held for the plain reason that the gap between what is checked and what is walked
/// is six times wider.</para>
///
/// <para>The fix only engages once a sample has produced no movement, so a party that is
/// travelling keeps exactly the probe it always had - which matters, because threading a
/// narrow doorway needs a direction that is clear through the gap and no further.</para>
/// </summary>
internal static class FieldAutoWalkRunPaceTests
{
    private const int FieldId = 450;

    // A wall across the route with a gap to one side. Anything probed past it on the
    // centre line is blocked; going round is clear.
    private const int WallX = 24;
    private const int WallHalfHeight = 40;

    internal static void Run()
    {
        ARunningPartyStoppedByAWallIsGivenAWayRound();
    }

    private static void ARunningPartyStoppedByAWallIsGivenAWayRound()
    {
        var target = new FieldNavigationTarget(
            FieldId,
            FieldNavigationCategory.Exits,
            "Exit past the wall",
            400,
            0,
            0,
            StableId: "past-the-wall",
            CompletesOnArrival: true);
        var start = Position(-160, 0);
        var planner = new WalledPlanner(new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z));
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target]), planner);
        var transform = new FieldNavigationControlTransform(0);
        while (controller.CurrentCategory != target.Category)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        }

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);

        // One running sample is forty-eight units, which is what the session measured.
        const int runningPace = 48;
        var position = start;
        var stalledSamples = 0;
        var trail = new List<string>();

        for (var sample = 0; sample < 40; sample++)
        {
            controller.UpdateLiveTracking(
                position,
                new(0, FieldNavigationInput.None),
                transform,
                false,
                80,
                observedAt: DateTime.UnixEpoch.AddMilliseconds(sample * 100));
            if (!controller.TryResolveAutomaticInput(position, transform, 80, out var input))
            {
                break;
            }

            var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
            var wanted = (
                X: position.X + (int)Math.Round(dx * runningPace),
                Y: position.Y + (int)Math.Round(dy * runningPace));

            // The game stops the party at the wall rather than teleporting them through it.
            var moved = IsClearOfTheWall(position.X, position.Y, wanted.X, wanted.Y)
                ? wanted
                : (X: position.X, Y: position.Y);
            stalledSamples = moved == (position.X, position.Y) ? stalledSamples + 1 : 0;
            trail.Add($"#{sample} ({position.X},{position.Y}) {input}");
            position = position with { X = moved.X, Y = moved.Y };

            // One stalled sample is how the mod learns it is stuck. More than a handful and
            // it is asking for the same blocked direction over and over, which is the
            // freeze the player reported.
            if (stalledSamples > 3)
            {
                throw new InvalidOperationException(
                    $"auto walk stalled against the wall for {stalledSamples} samples at " +
                    $"({position.X},{position.Y}) while running: it keeps asking for a direction " +
                    "that is clear for eight units and blocked beyond. Recent: " +
                    string.Join("; ", trail.TakeLast(10)));
            }

            if (position.X > WallX + runningPace)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"a running party never got past the wall (finished at {position.X},{position.Y}). " +
            "Recent: " + string.Join("; ", trail.TakeLast(10)));
    }

    // Clear unless the segment ends past the wall on the centre line. The gap is above and
    // below, so a direction with enough Y in it goes round.
    private static bool IsClearOfTheWall(int fromX, int fromY, int toX, int toY) =>
        toX <= WallX || Math.Abs(toY) >= WallHalfHeight;

    private static FieldPositionSnapshot Position(int x, int y) =>
        new(FieldPositionReader.FieldModule, FieldId, 0, x, y, 0, 0, 0);

    /// <summary>
    /// One waypoint straight ahead and a wall in the way, answering the movement probe for
    /// whatever distance it is asked about. That distance is the thing under test.
    /// </summary>
    private sealed class WalledPlanner(FieldNavigationRouteWaypoint waypoint)
        : IFieldNavigationRoutePlanner, IFieldNavigationAutomaticMovementPlanner
    {
        public string LastDiagnostic => "walled test route";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return true;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(
                position.FieldId,
                $"{target.FieldId}:{target.StableId}",
                [position.TriangleId],
                [],
                waypoint,
                position.TriangleId,
                StableWaypointsOverride: [new FieldNavigationRouteStep(waypoint, 0)]);
            return position.FieldId == target.FieldId;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint next)
        {
            next = waypoint;
            return position.FieldId == target.FieldId;
        }

        public bool IsAutomaticMovementClear(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination) =>
            IsClearOfTheWall(position.X, position.Y, destination.X, destination.Y);

        public bool IsNativeProbeAutomaticMovementClear(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination,
            byte? requestedHeading = null) =>
            IsAutomaticMovementClear(position, target, destination);

        public bool IsNativeProbeMovementClear(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination) =>
            IsAutomaticMovementClear(position, target, destination);
    }
}
