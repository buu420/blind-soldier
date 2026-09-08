using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapLocationEntranceTests
{
    internal static void Run()
    {
        RoutesJunonToItsNativeTerrainScriptTrigger();
        RoutesCostaDelSolToItsNativeTerrainScriptTrigger();
        ResolvesSleepingForestAndValleyByTheirDistinctNativeScripts();
        RequiresTheNativeMeshAndScriptBeforeAnnouncingArrival();
        EveryNativeArrivalPointResolvesInsideItsTrigger();
        AccountsForEveryInstalledLocationWithoutProximityFallbacks();
        GeneratedAssetRetainsNativeEventProvenance();
    }

    private static void RoutesJunonToItsNativeTerrainScriptTrigger()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var junon = catalog.Locations.Single(target => target.Label == "Junon");

        Equal(false, junon.ArrivalTriangleIds.Contains(174),
            "the former coordinate marker triangle is not an arrival");
        Equal(true, junon.ArrivalTriangleIds.Count > 0,
            "Junon has native trigger triangles");
        Equal(true, junon.ArrivalTriangleIds.All(id =>
            map.Triangles[id].MeshX == 20 &&
            map.Triangles[id].MeshZ == 17 &&
            map.Triangles[id].TerrainScriptId == 7),
            "Junon routes only to mesh 20,17 script 7");
    }

    private static void RoutesCostaDelSolToItsNativeTerrainScriptTrigger()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var costa = catalog.Locations.Single(target => target.Label == "Costa Del Sol");

        Equal(true, costa.ArrivalTriangleIds.Count > 0,
            "Costa del Sol has native trigger triangles");
        Equal(true, costa.ArrivalTriangleIds.All(id =>
            map.Triangles[id].MeshX == 17 &&
            map.Triangles[id].MeshZ == 15 &&
            map.Triangles[id].TerrainScriptId == 7),
            "Costa del Sol routes only to mesh 17,15 script 7");
    }

    private static void ResolvesSleepingForestAndValleyByTheirDistinctNativeScripts()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var sleepingForest = catalog.Locations.Single(target => target.Label == "Sleeping Forest");
        var valley = catalog.Locations.Single(target => target.Label == "Valley, City of Ancients entrance");

        Equal(true, sleepingForest.ArrivalTriangleIds.All(id =>
            map.Triangles[id].MeshX == 19 &&
            map.Triangles[id].MeshZ == 10 &&
            map.Triangles[id].TerrainScriptId == 6),
            "Sleeping Forest uses native script 6");
        Equal(true, valley.ArrivalTriangleIds.All(id =>
            map.Triangles[id].MeshX == 19 &&
            map.Triangles[id].MeshZ == 9 &&
            map.Triangles[id].TerrainScriptId == 7),
            "Valley uses its different native mesh and script");
    }

    private static void RequiresTheNativeMeshAndScriptBeforeAnnouncingArrival()
    {
        var map = LoadMap();
        var junon = LoadCatalog(map).Locations.Single(target => target.Label == "Junon");
        var triangle = map.Triangles[junon.TriangleId];
        var state = StateAt(junon, triangle) with { TerrainScriptId = 7 };

        Equal(true, junon.HasArrived(state, junon.TriangleId),
            "matching native mesh and script is an arrival");
        Equal(false, junon.HasArrived(state with { TerrainScriptId = 0 }, junon.TriangleId),
            "the old nearby script is not an arrival");
        Equal(false, junon.HasArrived(
                state with { X = state.X + WorldMapDataLoader.MeshSize },
                junon.TriangleId),
            "a matching triangle id cannot hide a different native mesh");
    }

    private static void AccountsForEveryInstalledLocationWithoutProximityFallbacks()
    {
        var catalog = LoadCatalog(LoadMap());
        var sourceRoot = SourceRoot();
        var installedIds = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                File.ReadAllText(Path.Combine(
                    sourceRoot,
                    "external",
                    "kujata",
                    "field-id-to-world-map-coords.json")))
            ?? throw new InvalidDataException("installed world-map location coordinates are empty");

        Equal(installedIds.Count, catalog.Locations.Count + catalog.UnresolvedLocations.Count,
            "every installed location is either native-trigger-backed or named unresolved");
        SequenceEqual(
            [40, 41, 45, 54, 55, 59],
            catalog.UnresolvedLocations.Select(location => location.LocationId),
            "only locations with no terrain-script entrance remain unresolved");
        Equal(true, catalog.Locations.All(target => target.NativeLocationArrivals.Count > 0),
            "no location falls back to coordinate proximity");

        var rocketNorth = catalog.Locations.Single(target => target.Label == "Rocket Town (North Side)");
        Equal(true, rocketNorth.NativeLocationArrivals.All(arrival =>
            arrival.MeshX == 10 && arrival.MeshZ == 14 && arrival.TerrainScriptId == 7),
            "Rocket Town north uses the native shared north/south handler");
    }

    private static void EveryNativeArrivalPointResolvesInsideItsTrigger()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var planner = new WorldMapRoutePlanner(map);
        foreach (var target in catalog.Locations)
        {
            foreach (var arrival in target.NativeLocationArrivals)
            {
                var triangle = map.Triangles[arrival.TriangleId];
                var state = new WorldMapStateSnapshot(
                    WorldMapStateReader.WorldModule,
                    map.WorldMapType,
                    map.WorldProgress,
                    341,
                    arrival.X,
                    arrival.Y,
                    arrival.Z,
                    0,
                    0,
                    triangle.TerrainId,
                    triangle.RegionId,
                    0,
                    30,
                    0,
                    new FieldNavigationControlTransform(0))
                {
                    TerrainScriptId = arrival.TerrainScriptId
                };

                Equal(true, planner.TryResolvePlayerTriangle(state, out var resolved),
                    $"resolve native arrival point for {target.Label}: {planner.LastDiagnostic}");
                Equal(true, target.HasArrived(state, resolved),
                    $"safe native arrival point for {target.Label}, triangle {arrival.TriangleId}");
            }
        }
    }

    private static void GeneratedAssetRetainsNativeEventProvenance()
    {
        var path = Path.Combine(
            SourceRoot(),
            "Ff7.Accessibility.Reloaded",
            "Assets",
            "world",
            "world-map-location-triggers.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Equal(1, root.GetProperty("SchemaVersion").GetInt32(), "native trigger schema");
        var hashes = root.GetProperty("SourceFilesSha256");
        Equal(
            "a020dace94f21b73094c204c54b01e13b3688002f65ffd9b1edf8ada5e372a64",
            hashes.GetProperty("wm0.ev").GetString(),
            "wm0 event source hash");
        Equal(
            "d1dea5c78225873d07b8c532d74e3bc35aebf4fb4a744906dd9771fbc3c18e64",
            hashes.GetProperty("wm2.ev").GetString(),
            "wm2 event source hash");
        Equal(
            "df90900e379b547eff8371e234dbf7d5e8fcf290b28ab484994a73ad4e6aef8d",
            hashes.GetProperty("wm3.ev").GetString(),
            "wm3 event source hash");
        var ancientForest = root.GetProperty("UnresolvedLocations")
            .EnumerateArray()
            .Single(location => location.GetProperty("LocationId").GetInt32() == 55);
        Equal(
            "Ancient Forest",
            ancientForest.GetProperty("Label").GetString(),
            "generated native-trigger data corrects the source table's Ancient Forset typo");
    }

    private static WorldMapStateSnapshot StateAt(
        WorldMapNavigationTarget target,
        WorldMapTriangle triangle) => new(
        WorldMapStateReader.WorldModule,
        0,
        0,
        387,
        target.X,
        target.Y,
        target.Z,
        0,
        0,
        triangle.TerrainId,
        target.RegionId,
        0,
        30,
        0,
        new FieldNavigationControlTransform(0));

    private static WorldMapData LoadMap() =>
        WorldMapDataLoader.Load(
            Path.Combine(
                Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
                    @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir",
                "data",
                "wm",
                "WM0.MAP"),
            0,
            0);

    private static WorldMapTargetCatalog LoadCatalog(WorldMapData map)
    {
        var root = SourceRoot();
        return WorldMapTargetCatalog.Load(
            map,
            Path.Combine(root, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(root, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(root, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
    }

    private static string SourceRoot() =>
        Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
        @"C:\FF7A11Y\accessibility_prototype";

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }
}
