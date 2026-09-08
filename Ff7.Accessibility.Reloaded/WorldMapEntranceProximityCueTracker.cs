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

    private string activeArrivalKey = string.Empty;
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

        var candidate = locations
            .Where(target =>
                target.Kind == WorldMapTargetKind.Location &&
                target.NativeLocationArrivals.Count > 0)
            .SelectMany(target => target.NativeLocationArrivals.Select(arrival =>
            {
                var component = planner.GetComponentId(
                    state.PlayerModelId,
                    state.WorldMapType,
                    arrival.TriangleId);
                var distanceSquared = WorldMapTargetCatalog.WrappedDistanceSquared(
                    map,
                    state.X,
                    state.Z,
                    arrival.X,
                    arrival.Z);
                return new EntranceCandidate(target, arrival, component, distanceSquared);
            }))
            .Where(value => value.ComponentId == playerComponent)
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
        var key = $"{candidate.Target.StableId}:{candidate.Arrival.TriangleId}";
        if (!string.Equals(activeArrivalKey, key, StringComparison.Ordinal))
        {
            activeArrivalKey = key;
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
        activeArrivalKey = string.Empty;
        nextPulseAt = DateTime.MinValue;
    }

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

    private sealed record EntranceCandidate(
        WorldMapNavigationTarget Target,
        WorldMapNativeLocationArrival Arrival,
        int ComponentId,
        double DistanceSquared);
}
