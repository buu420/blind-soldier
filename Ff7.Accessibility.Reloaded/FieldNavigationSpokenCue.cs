namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldNavigationSpokenSegment(
    string Direction,
    double CountedDistance);

public static class FieldNavigationSpokenCueFormatter
{
    public const int DefaultDistanceUnitsPerCount = 80;

    public static string Format(
        int worldDeltaX,
        int worldDeltaY,
        FieldNavigationControlTransform controlTransform,
        int distanceUnitsPerCount = DefaultDistanceUnitsPerCount)
    {
        if (!TryResolveSegment(
                worldDeltaX,
                worldDeltaY,
                controlTransform,
                distanceUnitsPerCount,
                out var segment))
        {
            return "at destination";
        }

        return Format(segment, distanceUnitsPerCount);
    }

    internal static bool TryResolveSegment(
        int worldDeltaX,
        int worldDeltaY,
        FieldNavigationControlTransform controlTransform,
        int distanceUnitsPerCount,
        out FieldNavigationSpokenSegment segment)
    {
        segment = default;
        var distance = Math.Sqrt(
            worldDeltaX * (double)worldDeltaX +
            worldDeltaY * (double)worldDeltaY);
        if (distance <= 0d)
        {
            return false;
        }

        var stick = controlTransform.TransformWorldVector(worldDeltaX, worldDeltaY);
        var horizontalOffset = stick.X * distance;
        var verticalOffset = stick.Y * distance;
        var horizontalMagnitude = Math.Abs(horizontalOffset);
        var verticalMagnitude = Math.Abs(verticalOffset);
        var dominantMagnitude = Math.Max(horizontalMagnitude, verticalMagnitude);
        if (dominantMagnitude <= 1d)
        {
            return false;
        }

        var secondaryMagnitude = Math.Min(horizontalMagnitude, verticalMagnitude);
        var minimumMeaningfulSecondary = Math.Max(1, distanceUnitsPerCount);
        var usesDiagonal =
            secondaryMagnitude >= dominantMagnitude * 0.5d &&
            secondaryMagnitude >= minimumMeaningfulSecondary;
        if (usesDiagonal)
        {
            var verticalDirection = verticalOffset < 0d ? "up" : "down";
            var horizontalDirection = horizontalOffset < 0d ? "left" : "right";
            segment = new FieldNavigationSpokenSegment(
                $"{verticalDirection}-{horizontalDirection}",
                distance);
            return true;
        }

        // Small secondary-axis offsets are funnel geometry rather than a
        // separate player action. Keep those instructions cardinal and count
        // only the controller axis the player is being asked to hold.
        var cardinalDirection = verticalMagnitude > horizontalMagnitude
            ? verticalOffset < 0d ? "up" : "down"
            : horizontalOffset < 0d ? "left" : "right";
        segment = new FieldNavigationSpokenSegment(cardinalDirection, dominantMagnitude);
        return true;
    }

    internal static string Format(
        FieldNavigationSpokenSegment segment,
        int distanceUnitsPerCount)
    {
        var countScale = Math.Max(1, distanceUnitsPerCount);
        var amount = (int)Math.Round(
            segment.CountedDistance / countScale,
            MidpointRounding.AwayFromZero);
        if (amount == 0)
        {
            return "at destination";
        }

        return $"{segment.Direction} {amount}";
    }
}

public static class FieldNavigationConnectedRunFormatter
{
    private const double StraightRunSecondaryAxisRatio = 0.35d;

    public static string Format(
        FieldPositionSnapshot position,
        FieldNavigationRouteWaypoint immediateWaypoint,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int waypointIndex,
        FieldNavigationRouteAction? nextAction,
        FieldNavigationControlTransform controlTransform,
        int distanceUnitsPerCount,
        out string direction)
    {
        direction = string.Empty;
        var scale = Math.Max(1, distanceUnitsPerCount);
        FieldNavigationSpokenSegment? immediateSegment =
            FieldNavigationSpokenCueFormatter.TryResolveSegment(
                immediateWaypoint.X - position.X,
                immediateWaypoint.Y - position.Y,
                controlTransform,
                scale,
                out var resolvedImmediate)
                ? resolvedImmediate
                : null;

        var routeSegments = new List<FieldNavigationSpokenSegment>();
        var firstRouteIndex = Math.Clamp(
            waypointIndex,
            0,
            Math.Max(0, stableWaypoints.Count - 1));
        var committedRouteIndex = FindCommittedRouteIndex(
            immediateWaypoint,
            stableWaypoints,
            firstRouteIndex);
        var usesCommittedStraightRun =
            TryResolveCommittedStraightRun(
                position,
                stableWaypoints,
                committedRouteIndex,
                controlTransform,
                out var committedSegment);
        var previous = usesCommittedStraightRun
            ? stableWaypoints[committedRouteIndex].Waypoint
            : new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z);
        var routeStartIndex = usesCommittedStraightRun
            ? committedRouteIndex + 1
            : firstRouteIndex;
        if (usesCommittedStraightRun &&
            committedSegment.CountedDistance > 1d)
        {
            routeSegments.Add(committedSegment);
        }

        for (var index = routeStartIndex;
             index < stableWaypoints.Count;
             index++)
        {
            var step = stableWaypoints[index];
            if (nextAction is { } action &&
                step.RequiredPortalIndex > action.PortalIndex)
            {
                break;
            }

            if (FieldNavigationSpokenCueFormatter.TryResolveSegment(
                    step.Waypoint.X - previous.X,
                    step.Waypoint.Y - previous.Y,
                    controlTransform,
                    scale,
                    out var segment))
            {
                routeSegments.Add(segment);
            }

            previous = step.Waypoint;
        }

        var firstRouteSegment = routeSegments.FirstOrDefault(segment =>
            segment.CountedDistance >= scale * 0.5d);
        var baseSegment = usesCommittedStraightRun
            ? string.IsNullOrEmpty(firstRouteSegment.Direction)
                ? null
                : firstRouteSegment
            : immediateSegment ??
            (string.IsNullOrEmpty(firstRouteSegment.Direction)
                ? null
                : firstRouteSegment);
        if (baseSegment is not { } first)
        {
            return "at destination";
        }

        direction = first.Direction;
        var routeDistance = 0d;
        var hasMatchingRouteSegment = false;
        foreach (var segment in routeSegments)
        {
            // Sub-half-step funnel corners are geometric bookkeeping, not a
            // direction the player can use. Let the connected run pass them.
            if (segment.CountedDistance < scale * 0.5d)
            {
                continue;
            }

            if (!string.Equals(segment.Direction, first.Direction, StringComparison.Ordinal))
            {
                break;
            }

            routeDistance += segment.CountedDistance;
            hasMatchingRouteSegment = true;
        }

        var connectedDistance = hasMatchingRouteSegment
            ? Math.Max(first.CountedDistance, routeDistance)
            : first.CountedDistance;
        return FieldNavigationSpokenCueFormatter.Format(
            new FieldNavigationSpokenSegment(first.Direction, connectedDistance),
            scale);
    }

    private static int FindCommittedRouteIndex(
        FieldNavigationRouteWaypoint immediateWaypoint,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int firstRouteIndex)
    {
        for (var index = firstRouteIndex; index < stableWaypoints.Count; index++)
        {
            if (stableWaypoints[index].Waypoint == immediateWaypoint)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryResolveCommittedStraightRun(
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int committedRouteIndex,
        FieldNavigationControlTransform controlTransform,
        out FieldNavigationSpokenSegment segment)
    {
        segment = default;
        if (committedRouteIndex <= 0 ||
            committedRouteIndex >= stableWaypoints.Count - 1)
        {
            return false;
        }

        var legStart = stableWaypoints[committedRouteIndex - 1].Waypoint;
        var legEnd = stableWaypoints[committedRouteIndex].Waypoint;
        var legDistance = Math.Sqrt(
            Math.Pow(legEnd.X - legStart.X, 2) +
            Math.Pow(legEnd.Y - legStart.Y, 2));
        if (legDistance <= 0d)
        {
            return false;
        }

        var legStick = controlTransform.TransformWorldVector(
            legEnd.X - legStart.X,
            legEnd.Y - legStart.Y);
        var legHorizontal = legStick.X * legDistance;
        var legVertical = legStick.Y * legDistance;
        var dominant = Math.Max(Math.Abs(legHorizontal), Math.Abs(legVertical));
        var secondary = Math.Min(Math.Abs(legHorizontal), Math.Abs(legVertical));
        if (dominant <= 1d ||
            secondary > dominant * StraightRunSecondaryAxisRatio)
        {
            return false;
        }

        var direction = Math.Abs(legVertical) > Math.Abs(legHorizontal)
            ? legVertical < 0d ? "up" : "down"
            : legHorizontal < 0d ? "left" : "right";
        var remainingDistance = Math.Sqrt(
            Math.Pow(legEnd.X - position.X, 2) +
            Math.Pow(legEnd.Y - position.Y, 2));
        var remainingStick = controlTransform.TransformWorldVector(
            legEnd.X - position.X,
            legEnd.Y - position.Y);
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
        segment = new FieldNavigationSpokenSegment(
            direction,
            Math.Max(0d, countedDistance));
        return true;
    }
}
