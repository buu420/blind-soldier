namespace Ff7.Accessibility.Reloaded;

public readonly record struct WorldMapEntranceProximityCue(
    WorldMapNavigationTarget Target,
    WorldMapNativeLocationArrival Arrival,
    float Gain,
    double DistanceUnits);

public static class WorldMapEntranceProximitySpatializer
{
    public static NavigationBeaconCue? CreateCue(
        WorldMapData map,
        WorldMapStateSnapshot state,
        WorldMapEntranceProximityCue proximity)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (state.CurrentModule != WorldMapStateReader.WorldModule ||
            state.WorldMapType != map.WorldMapType)
        {
            return null;
        }

        var dx = WorldMapTargetCatalog.WrappedDelta(state.X, proximity.Arrival.X, map.WrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(state.Z, proximity.Arrival.Z, map.WrapHeight);
        var distance = Math.Sqrt(dx * (double)dx + dz * (double)dz);

        // World-map X uses the opposite handedness from field X. Mirror it
        // once, then use the same camera-relative transform as spoken world
        // navigation and the field proximity cues.
        var stick = state.ControlTransform.TransformWorldVector(-dx, dz);
        return new NavigationBeaconCue(
            proximity.Target.Label,
            distance <= 0d ? "here" : "proximity",
            stick.X,
            stick.Y,
            distance <= 0d ? 0f : stick.X,
            0f,
            distance <= 0d ? -1f : stick.Y,
            NavigationBeaconMovementState.OnCourse,
            DurationMs: 220,
            DistanceUnits: distance);
    }
}

public sealed class WorldMapEntranceProximityCueTracker
{
    private readonly WorldMapData map;
    private readonly WorldMapRoutePlanner planner;
    private readonly IReadOnlyList<WorldMapNavigationTarget> locations;
    private readonly int innerRange;
    private readonly int outerRange;
    private readonly TimeSpan pulseInterval;
    private readonly Dictionary<int, IReadOnlyList<EntrancePoint>> entrancesByModel = new();

    private string activeTargetKey = string.Empty;
    private DateTime nextPulseAt = DateTime.MinValue;

    public WorldMapEntranceProximityCueTracker(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        IReadOnlyList<WorldMapNavigationTarget> locations,
        int innerRange,
        int outerRange,
        TimeSpan pulseInterval)
    {
        this.map = map ?? throw new ArgumentNullException(nameof(map));
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.locations = locations ?? throw new ArgumentNullException(nameof(locations));
        this.innerRange = Math.Max(0, innerRange);
        this.outerRange = Math.Max(this.innerRange + 1, outerRange);
        this.pulseInterval = pulseInterval < TimeSpan.Zero ? TimeSpan.Zero : pulseInterval;
    }

    public bool HasAudibleTarget { get; private set; }

    public WorldMapEntranceProximityCue? Update(
        WorldMapStateSnapshot state,
        DateTime observedAt)
    {
        if (state.CurrentModule != WorldMapStateReader.WorldModule ||
            state.WorldMapType != map.WorldMapType ||
            !planner.TryResolveReachableComponent(state, out var playerComponent))
        {
            Reset();
            return null;
        }

        var candidate = GetEntrancePoints(state.PlayerModelId)
            .Where(point => point.ComponentId == playerComponent)
            .Select(point =>
            {
                var distanceSquared = WorldMapTargetCatalog.WrappedDistanceSquared(
                    map,
                    state.X,
                    state.Z,
                    point.Arrival.X,
                    point.Arrival.Z);
                return new EntranceCandidate(point.Target, point.Arrival, distanceSquared);
            })
            .Where(value => value.DistanceSquared < outerRange * (double)outerRange)
            .OrderBy(value => value.DistanceSquared)
            .ThenBy(value => value.Target.StableId, StringComparer.Ordinal)
            .ThenBy(value => value.Arrival.TriangleId)
            .FirstOrDefault();

        if (candidate is null)
        {
            Reset();
            return null;
        }

        HasAudibleTarget = true;
        // A building can contain many script faces. Moving between their nearest
        // boundary edges must not restart the sound on every navigation poll.
        var key = candidate.Target.StableId;
        if (!string.Equals(activeTargetKey, key, StringComparison.Ordinal))
        {
            activeTargetKey = key;
            nextPulseAt = DateTime.MinValue;
        }

        if (observedAt < nextPulseAt)
        {
            return null;
        }

        nextPulseAt = observedAt + pulseInterval;
        var distance = Math.Sqrt(candidate.DistanceSquared);
        return new WorldMapEntranceProximityCue(
            candidate.Target,
            candidate.Arrival,
            CalculateGain(distance),
            distance);
    }

    public void Reset()
    {
        HasAudibleTarget = false;
        activeTargetKey = string.Empty;
        nextPulseAt = DateTime.MinValue;
    }

    private IReadOnlyList<EntrancePoint> GetEntrancePoints(int playerModelId)
    {
        if (entrancesByModel.TryGetValue(playerModelId, out var cached)) return cached;

        var points = new List<EntrancePoint>();
        foreach (var target in locations.Where(target => target.Kind == WorldMapTargetKind.Location))
        {
            // Older/synthetic catalogs can supply only a point. Keep that contract;
            // the installed catalog supplies the complete native trigger membership.
            if (target.NativeTriggerTriangleIds.Count == 0)
            {
                foreach (var arrival in target.NativeLocationArrivals)
                {
                    var component = planner.GetComponentId(playerModelId, map.WorldMapType, arrival.TriangleId);
                    if (component >= 0) points.Add(new(target, arrival, component));
                }
                continue;
            }

            // Native location scripts also cover walls and roofs. A sound belongs at
            // the boundary where traversable ground enters the trigger, not at the
            // nearest face centroid merely connected to that ground in the graph.
            // Include thin trigger faces: Junon's door is one of these and has no
            // strict interior arrival point, but its ground boundary is a real entry.
            foreach (var triangleId in target.NativeTriggerTriangleIds.Order())
            {
                var component = planner.GetComponentId(playerModelId, map.WorldMapType, triangleId);
                if (component < 0) continue;
                var triangle = map.Triangles[triangleId];
                foreach (var neighborId in triangle.Neighbors)
                {
                    if (target.NativeTriggerTriangleIds.Contains(neighborId) ||
                        planner.GetComponentId(playerModelId, map.WorldMapType, neighborId) != component)
                        continue;

                    var neighbor = map.Triangles[neighborId];
                    foreach (var edge in triangle.Edges)
                    {
                        if (!neighbor.Edges.Any(other =>
                            SameVertex(edge.Start, other.Start) && SameVertex(edge.End, other.End) ||
                            SameVertex(edge.Start, other.End) && SameVertex(edge.End, other.Start)))
                            continue;

                        var x = edge.Start.X + WorldMapTargetCatalog.WrappedDelta(edge.Start.X, edge.End.X, map.WrapWidth) / 2;
                        var z = edge.Start.Z + WorldMapTargetCatalog.WrappedDelta(edge.Start.Z, edge.End.Z, map.WrapHeight) / 2;
                        var arrival = new WorldMapNativeLocationArrival(triangleId, triangle.MeshX, triangle.MeshZ,
                            triangle.TerrainScriptId, Wrap(x, map.WrapWidth), (edge.Start.Y + edge.End.Y) / 2,
                            Wrap(z, map.WrapHeight));
                        points.Add(new(target, arrival, component));
                    }
                }
            }
        }

        entrancesByModel[playerModelId] = points;
        return points;
    }

    private bool SameVertex(WorldMapVertex first, WorldMapVertex second) =>
        WorldMapTargetCatalog.WrappedDelta(first.X, second.X, map.WrapWidth) == 0 &&
        first.Y == second.Y &&
        WorldMapTargetCatalog.WrappedDelta(first.Z, second.Z, map.WrapHeight) == 0;

    private static int Wrap(int value, int extent) => extent > 0 ? (value % extent + extent) % extent : value;

    private float CalculateGain(double distance)
    {
        if (distance <= innerRange)
        {
            return 1f;
        }

        if (distance >= outerRange)
        {
            return 0f;
        }

        return (float)((outerRange - distance) / (outerRange - innerRange));
    }

    private sealed record EntrancePoint(
        WorldMapNavigationTarget Target,
        WorldMapNativeLocationArrival Arrival,
        int ComponentId);

    private sealed record EntranceCandidate(
        WorldMapNavigationTarget Target,
        WorldMapNativeLocationArrival Arrival,
        double DistanceSquared);
}
