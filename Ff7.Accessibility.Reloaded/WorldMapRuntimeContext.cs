namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Owns the architecture-neutral world-map services for one native map type.
/// The live entity list is replaced atomically once per checked observation,
/// so Transportation and Events always use the same frame as navigation.
/// </summary>
public sealed class WorldMapRuntimeContext
{
    private IReadOnlyList<WorldMapEntitySnapshot> entities = Array.Empty<WorldMapEntitySnapshot>();
    private WorldMapUnderwaterVisibilityReader? underwaterVisibility;
    private uint announcedEmerald;
    private DateTime emeraldAbsentSince = DateTime.MinValue;
    private DateTime unavailableStateSince = DateTime.MinValue;

    public WorldMapRuntimeContext(
        WorldMapData map,
        WorldMapTargetCatalog catalog,
        IFieldNavigationProgressSink? progressSink,
        int distanceUnitsPerCount,
        TimeSpan guidanceInterval,
        TimeSpan walkingFootstepInterval,
        TimeSpan chocoboFootstepInterval,
        int entranceCueInnerRange,
        int entranceCueOuterRange,
        TimeSpan entranceCueInterval)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        // The entrance metadata has to be on the planner the game actually uses, not only
        // on one a test configured. Without this assignment every guarded routing test
        // passes while the live guard is empty and automatic walking still zones into the
        // neighbouring town.
        Planner = new WorldMapRoutePlanner(map)
        {
            EntranceTriangleIds = catalog.EntranceTriangleIds
        };
        Footsteps = new WorldMapFootstepTracker(
            map.WrapWidth,
            map.WrapHeight,
            walkingFootstepInterval,
            chocoboFootstepInterval);
        TerrainAnnouncements = new WorldMapTerrainAnnouncementTracker();
        EntranceProximityCues = new WorldMapEntranceProximityCueTracker(
            map,
            Planner,
            catalog.Locations,
            entranceCueInnerRange,
            entranceCueOuterRange,
            entranceCueInterval);
        Navigation = new WorldMapNavigationController(
            map,
            Planner,
            (state, category) => Catalog.ReadTargets(category, state, Entities),
            progressSink,
            distanceUnitsPerCount,
            guidanceInterval,
            () => Entities);
    }

    public WorldMapData Map { get; }

    public WorldMapTargetCatalog Catalog { get; }

    public WorldMapRoutePlanner Planner { get; }

    public WorldMapFootstepTracker Footsteps { get; }

    public WorldMapTerrainAnnouncementTracker TerrainAnnouncements { get; }

    public WorldMapEntranceProximityCueTracker EntranceProximityCues { get; }

    public WorldMapNavigationController Navigation { get; }

    public bool IsAtTerrainBoundary(WorldMapStateSnapshot state, int terrainId) =>
        WorldMapTerrainProximity.IsAtBoundary(Map, Planner, state, terrainId);

    public bool IsOnTerrain(WorldMapStateSnapshot state, int terrainId) =>
        WorldMapTerrainProximity.IsOnTerrain(Map, Planner, state, terrainId);

    public bool TryResolveSurface(
        WorldMapStateSnapshot state,
        out WorldMapSurfaceSample surface,
        out string diagnostic)
    {
        surface = default;
        if (state.CurrentModule != WorldMapStateReader.WorldModule)
        {
            diagnostic = $"module={state.CurrentModule}, not world map";
            return false;
        }

        if (state.WorldMapType != Map.WorldMapType)
        {
            diagnostic = $"state map={state.WorldMapType}, loaded map={Map.WorldMapType}";
            return false;
        }

        if (state.TerrainId is < 0 or > 31)
        {
            diagnostic = $"native terrain id={state.TerrainId} is outside 0 through 31";
            return false;
        }

        if (state.RegionId is < 0 or > 31)
        {
            diagnostic = $"native region id={state.RegionId} is outside 0 through 31";
            return false;
        }

        surface = new WorldMapSurfaceSample(
            state.TerrainId,
            state.HasChocoboTracks,
            state.RegionId);
        diagnostic = string.Empty;
        return true;
    }

    public IReadOnlyList<WorldMapEntitySnapshot> Entities => Volatile.Read(ref entities);
    public string LastUnderwaterVisibilityDiagnostic { get; private set; } = string.Empty;

    public void UpdateEntities(IReadOnlyList<WorldMapEntitySnapshot>? value)
    {
        unavailableStateSince = DateTime.MinValue;
        PublishEntities(value ?? Array.Empty<WorldMapEntitySnapshot>());
    }

    private void PublishEntities(IReadOnlyList<WorldMapEntitySnapshot> value)
    {
        if (value.Count == 0)
        {
            announcedEmerald = 0;
            emeraldAbsentSince = DateTime.MinValue;
        }
        Volatile.Write(ref entities, value);
        LastUnderwaterVisibilityDiagnostic = string.Empty;
    }

    /// <summary>
    /// A moving player's double-sampled state can tear without unloading the world.
    /// Release entity-dependent input immediately, but keep the sighting announcement
    /// through brief tears. A continuous 500 ms outage retires that too.
    /// </summary>
    public void ObserveUnavailableState(DateTime nowUtc)
    {
        if (unavailableStateSince == DateTime.MinValue) unavailableStateSince = nowUtc;
        if (nowUtc - unavailableStateSince >= TimeSpan.FromMilliseconds(500))
            PublishEntities(Array.Empty<WorldMapEntitySnapshot>());
        else
            Volatile.Write(ref entities, Array.Empty<WorldMapEntitySnapshot>());
    }

    public void UpdateEntities(WorldMapStateSnapshot state,
        IReadOnlyList<WorldMapEntitySnapshot>? value, Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace? memory)
    {
        value ??= Array.Empty<WorldMapEntitySnapshot>();
        if (Map.WorldMapType != 2) { UpdateEntities(value); return; }
        WorldMapUnderwaterVisibilityReadResult sighting = default;
        if (memory is not null && value.Any(IsUnderwaterTarget))
        {
            underwaterVisibility ??= new(memory, Map);
            sighting = underwaterVisibility.Read(state, value);
        }

        // Tracking and visibility share one checked capture: each node it still lists takes its
        // live position there, and only a positive draw makes one visible. A node a coherent
        // capture no longer lists was retired after the caller's scan. With no coherent capture
        // of the underwater map the caller's own checked scan is the current position, and
        // nothing is visible.
        var live = sighting.LiveEntities;
        UpdateEntities(value
            .Where(e => live is null || !IsUnderwaterTarget(e) || live.ContainsKey(e.GuestPointer))
            .Select(e => live is not null && live.TryGetValue(e.GuestPointer, out var current)
                ? current : e with { IsVisibleUnderwater = false })
            .ToArray());
        LastUnderwaterVisibilityDiagnostic = sighting.Diagnostic ??
            (memory is null ? "underwater visibility memory unavailable" : "no live underwater target entities");
    }

    private static bool IsUnderwaterTarget(WorldMapEntitySnapshot entity) =>
        !entity.IsPlayer && entity.ModelId is 17 or 26 or 28 or 30;

    public string? ObserveUnderwaterSightings(WorldMapStateSnapshot state, bool speechReserved = false, DateTime? nowUtc = null)
    {
        var pointer = state.WorldMapType == 2 && state.PlayerModelId == 13
            ? Entities.FirstOrDefault(e => !e.IsPlayer && e.ModelId == 30 && e.IsVisibleUnderwater).GuestPointer
            : 0;
        if (pointer == 0)
        {
            var now = nowUtc ?? DateTime.UtcNow;
            if (emeraldAbsentSince == DateTime.MinValue) emeraldAbsentSince = now;
            if (now - emeraldAbsentSince >= TimeSpan.FromMilliseconds(500)) announcedEmerald = 0;
            return null;
        }
        emeraldAbsentSince = DateTime.MinValue;
        if (speechReserved || announcedEmerald == pointer) return null;
        announcedEmerald = pointer;
        if (Navigation.BeaconEnabled && string.Equals(Navigation.Probe.TargetId,
            $"world-entity:{pointer:X8}:30", StringComparison.Ordinal))
            return "Emerald Weapon is visible nearby. It remains your navigation target.";
        Navigation.FocusSightedEmerald(pointer);
        return "Emerald Weapon is visible nearby. Select it in Events.";
    }

    public void Reset()
    {
        announcedEmerald = 0;
        emeraldAbsentSince = DateTime.MinValue;
        UpdateEntities(Array.Empty<WorldMapEntitySnapshot>());
        Footsteps.Reset();
        TerrainAnnouncements.Reset();
        EntranceProximityCues.Reset();
        Navigation.Reset();
    }
}
