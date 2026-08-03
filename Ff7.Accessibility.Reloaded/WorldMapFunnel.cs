namespace Ff7.Accessibility.Reloaded;

public readonly record struct WorldMapRoutePortal(
    WorldMapRouteWaypoint Left,
    WorldMapRouteWaypoint Right);

/// <summary>
/// Pulls the shortest stable line through a connected sequence of world-map
/// navmesh portals. Coordinates passed to this class must already be unwrapped
/// into one continuous copy of the looping world map.
/// </summary>
public static class WorldMapFunnel
{
    public static IReadOnlyList<WorldMapRouteWaypoint> BuildStableWaypoints(
        WorldMapRouteWaypoint start,
        IReadOnlyList<WorldMapRoutePortal> portals,
        WorldMapRouteWaypoint finalApproach)
    {
        ArgumentNullException.ThrowIfNull(portals);
        if (portals.Count == 0)
        {
            return [finalApproach];
        }

        var corridor = new List<WorldMapRoutePortal>(portals.Count + 1);
        corridor.AddRange(portals);
        corridor.Add(new WorldMapRoutePortal(finalApproach, finalApproach));

        var waypoints = new List<WorldMapRouteWaypoint>();
        var apex = start;
        var firstPortalIndex = 0;
        while (firstPortalIndex < corridor.Count)
        {
            var corner = FindNextCorner(apex, corridor, firstPortalIndex);
            if (corner.PortalIndex >= corridor.Count - 1)
            {
                AddWaypoint(waypoints, finalApproach);
                break;
            }

            // A malformed or completely degenerate portal must not leave the
            // funnel spinning at its existing apex.
            if (corner.PortalIndex < firstPortalIndex)
            {
                AddWaypoint(waypoints, finalApproach);
                break;
            }

            AddWaypoint(waypoints, corner.Point);
            apex = corner.Point;
            firstPortalIndex = corner.PortalIndex + 1;
        }

        if (waypoints.Count == 0 || waypoints[^1] != finalApproach)
        {
            AddWaypoint(waypoints, finalApproach);
        }

        return waypoints;
    }

    private static FunnelCorner FindNextCorner(
        WorldMapRouteWaypoint start,
        IReadOnlyList<WorldMapRoutePortal> portals,
        int firstPortalIndex)
    {
        var apex = start;
        var left = start;
        var right = start;
        var leftIndex = firstPortalIndex - 1;
        var rightIndex = firstPortalIndex - 1;

        for (var index = firstPortalIndex; index < portals.Count; index++)
        {
            var nextLeft = portals[index].Left;
            var nextRight = portals[index].Right;

            if (TwiceSignedArea(apex, right, nextRight) <= 0d)
            {
                if (SamePlanarPoint(apex, right) || TwiceSignedArea(apex, left, nextRight) > 0d)
                {
                    right = nextRight;
                    rightIndex = index;
                }
                else
                {
                    return new FunnelCorner(left, leftIndex);
                }
            }

            if (TwiceSignedArea(apex, left, nextLeft) >= 0d)
            {
                if (SamePlanarPoint(apex, left) || TwiceSignedArea(apex, right, nextLeft) < 0d)
                {
                    left = nextLeft;
                    leftIndex = index;
                }
                else
                {
                    return new FunnelCorner(right, rightIndex);
                }
            }
        }

        return new FunnelCorner(portals[^1].Left, portals.Count - 1);
    }

    private static double TwiceSignedArea(
        WorldMapRouteWaypoint first,
        WorldMapRouteWaypoint second,
        WorldMapRouteWaypoint third) =>
        (third.X - (double)first.X) * (second.Z - (double)first.Z) -
        (second.X - (double)first.X) * (third.Z - (double)first.Z);

    private static bool SamePlanarPoint(
        WorldMapRouteWaypoint first,
        WorldMapRouteWaypoint second) =>
        first.X == second.X && first.Z == second.Z;

    private static void AddWaypoint(
        ICollection<WorldMapRouteWaypoint> waypoints,
        WorldMapRouteWaypoint waypoint)
    {
        if (waypoints.LastOrDefault() != waypoint || waypoints.Count == 0)
        {
            waypoints.Add(waypoint);
        }
    }

    private readonly record struct FunnelCorner(
        WorldMapRouteWaypoint Point,
        int PortalIndex);
}
