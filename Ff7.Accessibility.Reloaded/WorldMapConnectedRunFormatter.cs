namespace Ff7.Accessibility.Reloaded;

public readonly record struct WorldMapSpokenRun(
    string Speech,
    string Direction,
    int EndWaypointIndex);

/// <summary>
/// Converts the world route's stable corners into the same connected-run
/// instructions used by field navigation. Small lateral drift does not break
/// a committed cardinal leg; consecutive legs with the same spoken direction
/// are presented as one usable distance.
/// </summary>
public static class WorldMapConnectedRunFormatter
{
    private const double StraightRunSecondaryAxisRatio = 0.35d;

    public static WorldMapSpokenRun Resolve(
        WorldMapRouteWaypoint routeStart,
        IReadOnlyList<WorldMapRouteWaypoint> waypoints,
        int waypointIndex,
        WorldMapStateSnapshot state,
        int wrapWidth,
        int wrapHeight,
        int distanceUnitsPerCount)
    {
        if (waypoints.Count == 0)
        {
            return new WorldMapSpokenRun("at destination", string.Empty, 0);
        }

        var scale = Math.Max(1, distanceUnitsPerCount);
        var firstIndex = Math.Clamp(waypointIndex, 0, waypoints.Count - 1);
        FieldNavigationSpokenSegment? firstSegment =
            TryResolveCommittedStraightRun(
                routeStart,
                waypoints,
                firstIndex,
                state,
                wrapWidth,
                wrapHeight,
                out var committed)
                ? committed
                : ResolveSegment(
                    state.X,
                    state.Z,
                    waypoints[firstIndex],
                    state.ControlTransform,
                    wrapWidth,
                    wrapHeight,
                    scale);
        var endIndex = firstIndex;
        var previous = waypoints[firstIndex];

        for (var index = firstIndex + 1; index < waypoints.Count; index++)
        {
            var segment = ResolveSegment(
                previous.X,
                previous.Z,
                waypoints[index],
                state.ControlTransform,
                wrapWidth,
                wrapHeight,
                scale);
            previous = waypoints[index];
            if (segment is not { } resolved || resolved.CountedDistance < scale * 0.5d)
            {
                continue;
            }

            if (firstSegment is null)
            {
                firstSegment = resolved;
                endIndex = index;
                continue;
            }

            if (!string.Equals(
                    firstSegment.Value.Direction,
                    resolved.Direction,
                    StringComparison.Ordinal))
            {
                break;
            }

            firstSegment = firstSegment.Value with
            {
                CountedDistance = firstSegment.Value.CountedDistance + resolved.CountedDistance
            };
            endIndex = index;
        }

        if (firstSegment is not { } run)
        {
            return new WorldMapSpokenRun("at destination", string.Empty, firstIndex);
        }

        return new WorldMapSpokenRun(
            FieldNavigationSpokenCueFormatter.Format(run, scale),
            run.Direction,
            endIndex);
    }

    private static FieldNavigationSpokenSegment? ResolveSegment(
        int fromX,
        int fromZ,
        WorldMapRouteWaypoint destination,
        FieldNavigationControlTransform controlTransform,
        int wrapWidth,
        int wrapHeight,
        int scale)
    {
        var dx = WorldMapTargetCatalog.WrappedDelta(fromX, destination.X, wrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(fromZ, destination.Z, wrapHeight);
        return FieldNavigationSpokenCueFormatter.TryResolveSegment(
            -dx,
            dz,
            controlTransform,
            scale,
            out var segment)
            ? segment
            : null;
    }

    private static bool TryResolveCommittedStraightRun(
        WorldMapRouteWaypoint routeStart,
        IReadOnlyList<WorldMapRouteWaypoint> waypoints,
        int waypointIndex,
        WorldMapStateSnapshot state,
        int wrapWidth,
        int wrapHeight,
        out FieldNavigationSpokenSegment segment)
    {
        segment = default;
        var legStart = waypointIndex == 0 ? routeStart : waypoints[waypointIndex - 1];
        var legEnd = waypoints[waypointIndex];
        var legDx = WorldMapTargetCatalog.WrappedDelta(legStart.X, legEnd.X, wrapWidth);
        var legDz = WorldMapTargetCatalog.WrappedDelta(legStart.Z, legEnd.Z, wrapHeight);
        var legDistance = Math.Sqrt(legDx * (double)legDx + legDz * (double)legDz);
        if (legDistance <= 0d)
        {
            return false;
        }

        var legStick = TransformWorldVector(state.ControlTransform, legDx, legDz);
        var legHorizontal = legStick.X * legDistance;
        var legVertical = legStick.Y * legDistance;
        var dominant = Math.Max(Math.Abs(legHorizontal), Math.Abs(legVertical));
        var secondary = Math.Min(Math.Abs(legHorizontal), Math.Abs(legVertical));
        if (dominant <= 1d || secondary > dominant * StraightRunSecondaryAxisRatio)
        {
            return false;
        }

        var direction = Math.Abs(legVertical) > Math.Abs(legHorizontal)
            ? legVertical < 0d ? "up" : "down"
            : legHorizontal < 0d ? "left" : "right";
        var remainingDx = WorldMapTargetCatalog.WrappedDelta(state.X, legEnd.X, wrapWidth);
        var remainingDz = WorldMapTargetCatalog.WrappedDelta(state.Z, legEnd.Z, wrapHeight);
        var remainingDistance = Math.Sqrt(
            remainingDx * (double)remainingDx + remainingDz * (double)remainingDz);
        var remainingStick = TransformWorldVector(state.ControlTransform, remainingDx, remainingDz);
        var remainingHorizontal = remainingStick.X * remainingDistance;
        var remainingVertical = remainingStick.Y * remainingDistance;
        var countedDistance = direction switch
        {
            "up" => -remainingVertical,
            "right" => remainingHorizontal,
            "down" => remainingVertical,
            "left" => -remainingHorizontal,
            _ => 0d
        };
        segment = new FieldNavigationSpokenSegment(direction, Math.Max(0d, countedDistance));
        return true;
    }

    private static FieldNavigationStickDirection TransformWorldVector(
        FieldNavigationControlTransform controlTransform,
        int dx,
        int dz)
    {
        // FFVII's field coordinates and world-map coordinates use opposite X
        // handedness. Mirror world X once, then reuse the shared field
        // controller transform and speech formatter.
        return controlTransform.TransformWorldVector(-dx, dz);
    }
}
