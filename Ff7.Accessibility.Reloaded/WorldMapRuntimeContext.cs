namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Owns the architecture-neutral world-map services for one native map type.
/// The live entity list is replaced atomically once per checked observation,
/// so Transportation and Events always use the same frame as navigation.
/// </summary>
public sealed class WorldMapRuntimeContext
{
    private IReadOnlyList<WorldMapEntitySnapshot> entities = Array.Empty<WorldMapEntitySnapshot>();

    public WorldMapRuntimeContext(
        WorldMapData map,
        WorldMapTargetCatalog catalog,
        IFieldNavigationProgressSink? progressSink,
        int distanceUnitsPerCount,
        TimeSpan guidanceInterval,
        TimeSpan walkingFootstepInterval,
        TimeSpan chocoboFootstepInterval)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Planner = new WorldMapRoutePlanner(map);
        Footsteps = new WorldMapFootstepTracker(
            map.WrapWidth,
            map.WrapHeight,
            walkingFootstepInterval,
            chocoboFootstepInterval);
        Navigation = new WorldMapNavigationController(
            map,
            Planner,
            (state, category) => Catalog.ReadTargets(category, state, Entities),
            progressSink,
            distanceUnitsPerCount,
            guidanceInterval);
    }

    public WorldMapData Map { get; }

    public WorldMapTargetCatalog Catalog { get; }

    public WorldMapRoutePlanner Planner { get; }

    public WorldMapFootstepTracker Footsteps { get; }

    public WorldMapNavigationController Navigation { get; }

    public bool IsAtTerrainBoundary(WorldMapStateSnapshot state, int terrainId) =>
        WorldMapTerrainProximity.IsAtBoundary(Map, Planner, state, terrainId);

    public bool IsOnTerrain(WorldMapStateSnapshot state, int terrainId) =>
        WorldMapTerrainProximity.IsOnTerrain(Map, Planner, state, terrainId);

    public IReadOnlyList<WorldMapEntitySnapshot> Entities => Volatile.Read(ref entities);

    public void UpdateEntities(IReadOnlyList<WorldMapEntitySnapshot>? value) =>
        Volatile.Write(ref entities, value ?? Array.Empty<WorldMapEntitySnapshot>());

    public void Reset()
    {
        UpdateEntities(Array.Empty<WorldMapEntitySnapshot>());
        Footsteps.Reset();
        Navigation.Reset();
    }
}
