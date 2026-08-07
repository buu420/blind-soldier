using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapRoutePlannerTests
{
    internal static void Run()
    {
        BuildsNativeWalkingRouteBetweenEarlyWorldLocations();
        WalksThroughTheZolomSwampButNeverAcrossCliffFaces();
        SelectsTheNearestMemberOfAChocoboTrackArea();
        PullsAWorldRouteStraightThroughOverlappingPortals();
        UsesTheShortWrappedDeltaAcrossTheWorldSeam();
        AppliesVehicleAwareTerrainRules();
    }

    private static void WalksThroughTheZolomSwampButNeverAcrossCliffFaces()
    {
        var (map, catalog) = Load();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var mineMidgarSide = catalog.Locations.Single(
            target => target.Label.Contains("Mythril Mine (Midgar side)", StringComparison.OrdinalIgnoreCase));
        var mineJunonSide = catalog.Locations.Single(
            target => target.Label.Contains("Mythril Mine (Junon side)", StringComparison.OrdinalIgnoreCase));
        var planner = new WorldMapRoutePlanner(map);
        var state = StateAt(map, midgar, playerModelId: 0);

        Equal(true, planner.TryBuildRoute(state, mineMidgarSide, out var route),
            $"Midgar-side mine remains reachable through the swamp: {planner.LastDiagnostic}");
        Equal(true, route.TrianglePath.Any(id => map.Triangles[id].TerrainId == 7),
            "walking route crosses native swamp terrain");
        Equal(false, route.TrianglePath.Any(id => map.Triangles[id].TerrainId == 12),
            "walking route never crosses a cliff face");
        Equal(false, planner.TryBuildRoute(state, mineJunonSide, out _),
            "Junon-side mine remains unavailable until the party traverses the mine field");
    }

    private static void BuildsNativeWalkingRouteBetweenEarlyWorldLocations()
    {
        var (map, catalog) = Load();
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var farm = catalog.Locations.Single(target => target.Label == "Chocobo Farm");
        var planner = new WorldMapRoutePlanner(map);

        var success = planner.TryBuildRoute(StateAt(map, kalm, playerModelId: 0), farm, out var route);

        Equal(true, success, $"Kalm to Chocobo Farm walking route: {planner.LastDiagnostic}");
        Equal(
            kalm.TriangleId,
            route.StartTriangleId,
            $"route start triangle target={Describe(map.Triangles[kalm.TriangleId])} resolved={Describe(map.Triangles[route.StartTriangleId])} point={kalm.X},{kalm.Z}");
        Equal(farm.TriangleId, route.TargetTriangleId, "route target triangle");
        Equal(true, route.TrianglePath.Count > 1, "route traverses native terrain");
        Equal(true, route.Waypoints.Count >= 1, "route has stable guidance waypoints");
        Equal(true, route.TotalDistance > 0, "route has measurable progress");
    }

    private static void SelectsTheNearestMemberOfAChocoboTrackArea()
    {
        var (map, catalog) = Load();
        var farm = catalog.Locations.Single(target => target.Label == "Chocobo Farm");
        var tracks = catalog.ChocoboTracks.Single(target => target.RegionId == 1);
        var planner = new WorldMapRoutePlanner(map);

        var success = planner.TryBuildRoute(StateAt(map, farm, 0), tracks, out var route);

        Equal(true, success, $"farm to Grasslands tracks: {planner.LastDiagnostic}");
        Equal(true, tracks.ArrivalTriangleIds.Contains(route.TargetTriangleId), "route ends on a native track triangle");
    }

    private static void PullsAWorldRouteStraightThroughOverlappingPortals()
    {
        var start = new WorldMapRouteWaypoint(0, 0, 0);
        var target = new WorldMapRouteWaypoint(3_000, 0, 0);
        WorldMapRoutePortal[] portals =
        [
            new(
                new WorldMapRouteWaypoint(1_000, 0, 500),
                new WorldMapRouteWaypoint(1_000, 0, -1_000)),
            new(
                new WorldMapRouteWaypoint(2_000, 0, 1_000),
                new WorldMapRouteWaypoint(2_000, 0, -500))
        ];

        var waypoints = WorldMapFunnel.BuildStableWaypoints(start, portals, target);

        Equal(1, waypoints.Count, "overlapping portals need no artificial midpoint turns");
        Equal(target, waypoints[0], "the open corridor remains one straight run");
    }

    private static void UsesTheShortWrappedDeltaAcrossTheWorldSeam()
    {
        Equal(20, WorldMapTargetCatalog.WrappedDelta(0x47FF6, 10, 0x48000), "positive seam delta");
        Equal(-20, WorldMapTargetCatalog.WrappedDelta(10, 0x47FF6, 0x48000), "negative seam delta");
    }

    private static void AppliesVehicleAwareTerrainRules()
    {
        Equal(true, WorldMapTerrainPassability.CanTraverse(0, worldMapType: 0, terrainId: 0), "Cloud grass");
        Equal(false, WorldMapTerrainPassability.CanTraverse(0, 0, 2), "Cloud mountain");
        Equal(false, WorldMapTerrainPassability.CanTraverse(0, 0, 3), "Cloud deep sea");
        Equal(true, WorldMapTerrainPassability.CanTraverse(0, 0, 7), "Cloud swamp");
        Equal(false, WorldMapTerrainPassability.CanTraverse(0, 0, 12), "Cloud cliff face");
        Equal(true, WorldMapTerrainPassability.CanTraverse(6, 0, 4), "buggy river crossing");
        Equal(true, WorldMapTerrainPassability.CanTraverse(5, 0, 6), "Tiny Bronco shallow water");
        Equal(true, WorldMapTerrainPassability.CanTraverse(3, 0, 2), "Highwind flies over mountains");
        Equal(true, WorldMapTerrainPassability.CanTraverse(13, 2, 3), "submarine sea");
    }

    private static WorldMapStateSnapshot StateAt(
        WorldMapData map,
        WorldMapNavigationTarget target,
        int playerModelId) =>
        new(
            WorldMapStateReader.WorldModule,
            0,
            0,
            341,
            target.X,
            target.Y,
            target.Z,
            0,
            0,
            map.Triangles[target.TriangleId].TerrainId,
            target.RegionId,
            playerModelId,
            30,
            0,
            new FieldNavigationControlTransform(0));

    private static (WorldMapData Map, WorldMapTargetCatalog Catalog) Load()
    {
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
            @"C:\FF7A11Y\accessibility_prototype";
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir";
        var map = WorldMapDataLoader.Load(
            Path.Combine(dataRoot, "data", "wm", "WM0.MAP"),
            0,
            0);
        var catalog = WorldMapTargetCatalog.Load(
            map,
            Path.Combine(sourceRoot, "tools", "kujata", "metadata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "tools", "kujata", "metadata-src", "world-map", "wm-field-menu-names.txt"));
        return (map, catalog);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }

    private static string Describe(WorldMapTriangle triangle) =>
        $"id{triangle.Id}/mesh{triangle.MeshX},{triangle.MeshZ}/terrain{triangle.TerrainId}/center{triangle.Centroid.X},{triangle.Centroid.Y},{triangle.Centroid.Z}/" +
        $"v[{triangle.Vertex0.X},{triangle.Vertex0.Z};{triangle.Vertex1.X},{triangle.Vertex1.Z};{triangle.Vertex2.X},{triangle.Vertex2.Z}]";
}
