namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Resolves whether the player's native world triangle lies on either side of
/// a terrain boundary. This keeps feature gates tied to the actual walkmesh.
/// </summary>
public static class WorldMapTerrainProximity
{
    /// <summary>
    /// Whether the player's own native world triangle is the given terrain, as
    /// opposed to merely bordering it.
    /// </summary>
    public static bool IsOnTerrain(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        WorldMapStateSnapshot state,
        int terrainId)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(planner);
        if (terrainId is < 0 or > 31 ||
            !planner.TryResolvePlayerTriangle(state, out var triangleId) ||
            triangleId < 0 ||
            triangleId >= map.Triangles.Count)
        {
            return false;
        }

        return map.Triangles[triangleId].TerrainId == terrainId;
    }

    public static bool IsAtBoundary(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        WorldMapStateSnapshot state,
        int terrainId)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(planner);
        if (terrainId is < 0 or > 31 ||
            !planner.TryResolvePlayerTriangle(state, out var triangleId) ||
            triangleId < 0 ||
            triangleId >= map.Triangles.Count)
        {
            return false;
        }

        var triangle = map.Triangles[triangleId];
        var playerIsOnTerrain = triangle.TerrainId == terrainId;
        return triangle.Neighbors.Any(neighborId =>
            neighborId >= 0 &&
            neighborId < map.Triangles.Count &&
            (map.Triangles[neighborId].TerrainId == terrainId) != playerIsOnTerrain);
    }
}
