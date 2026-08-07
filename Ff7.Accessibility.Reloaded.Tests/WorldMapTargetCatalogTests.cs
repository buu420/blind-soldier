using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapTargetCatalogTests
{
    internal static void Run()
    {
        ExposesTheApprovedCategoriesWithoutARegionsCategory();
        JoinsWorldLocationNamesByNativeFieldId();
        ResolvesInstalledLocationsToNativeTerrain();
        PlacesEveryWorldEntranceOnItsResolvedNativeTriangle();
        GroupsNativeChocoboTracksByRegion();
        BuildsTransportationAndEventsOnlyFromLiveNativeEntities();
        SelectsKalmAsTheFirstWorldStoryObjective();
    }

    private static void JoinsWorldLocationNamesByNativeFieldId()
    {
        var catalog = LoadCatalog();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        Equal(185_636, midgar.X, "Midgar native X coordinate");
        Equal(123_325, midgar.Z, "Midgar native Z coordinate");

        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        Equal(201_686, kalm.X, "Kalm native X coordinate");
        Equal(112_928, kalm.Z, "Kalm native Z coordinate");
    }

    private static void BuildsTransportationAndEventsOnlyFromLiveNativeEntities()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var state = new WorldMapStateSnapshot(
            WorldMapStateReader.WorldModule, 0, 0, 341,
            kalm.X, kalm.Y, kalm.Z, 0, 0,
            map.Triangles[kalm.TriangleId].TerrainId, kalm.RegionId,
            0, 30, 0, new FieldNavigationControlTransform(0));
        WorldMapEntitySnapshot[] entities =
        [
            new(0x1000, 0x2000, true, kalm.X, kalm.Y, kalm.Z, state.TerrainId, kalm.RegionId, 0, 1),
            new(0x2000, 0x3000, false, kalm.X + 20, kalm.Y, kalm.Z, state.TerrainId, kalm.RegionId, 3, 1),
            new(0x3000, 0, false, kalm.X + 40, kalm.Y, kalm.Z, state.TerrainId, kalm.RegionId, 11, 1)
        ];

        var transport = catalog.ReadTargets(WorldMapNavigationCategory.Transportation, state, entities);
        var events = catalog.ReadTargets(WorldMapNavigationCategory.Events, state, entities);
        Equal(1, transport.Count, "one live transport");
        Equal("Highwind", transport[0].Label, "native transport label");
        Equal(1, events.Count, "one live event");
        Equal("Ultimate Weapon", events[0].Label, "native event label");
    }

    private static void ExposesTheApprovedCategoriesWithoutARegionsCategory()
    {
        SequenceEqual(
            [
                WorldMapNavigationCategory.Locations,
                WorldMapNavigationCategory.Story,
                WorldMapNavigationCategory.Transportation,
                WorldMapNavigationCategory.Events,
                WorldMapNavigationCategory.ChocoboTracks
            ],
            WorldMapTargetCatalog.CategoryOrder,
            "world navigation category order");
    }

    private static void ResolvesInstalledLocationsToNativeTerrain()
    {
        var catalog = LoadCatalog();
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        Equal(0, kalm.RegionId, "Kalm native region");
        Equal(true, kalm.TriangleId >= 0, "Kalm native triangle");

        var farm = catalog.Locations.Single(target => target.Label == "Chocobo Farm");
        Equal(1, farm.RegionId, "Chocobo Farm native region");

        var midgarMine = catalog.Locations.Single(target => target.Label.Contains("Midgar side", StringComparison.OrdinalIgnoreCase));
        var junonMine = catalog.Locations.Single(target => target.Label.Contains("Junon side", StringComparison.OrdinalIgnoreCase));
        Equal(1, midgarMine.RegionId, "Mythril Mine Midgar-side region");
        Equal(2, junonMine.RegionId, "Mythril Mine Junon-side region");
    }

    private static void GroupsNativeChocoboTracksByRegion()
    {
        var tracks = LoadCatalog().ChocoboTracks;
        SequenceEqual([1, 2, 4, 8, 9, 11, 12], tracks.Select(target => target.RegionId), "track regions");
        Equal(true, tracks.All(target => target.ArrivalTriangleIds.Count > 0), "track targets retain patch membership");
        Equal(124, tracks.Sum(target => target.ArrivalTriangleIds.Count), "all native track triangles grouped");
    }

    private static void PlacesEveryWorldEntranceOnItsResolvedNativeTriangle()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var planner = new WorldMapRoutePlanner(map);

        foreach (var target in catalog.Locations)
        {
            var triangle = map.Triangles[target.TriangleId];
            var state = new WorldMapStateSnapshot(
                WorldMapStateReader.WorldModule,
                map.WorldMapType,
                0,
                341,
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

            Equal(true, planner.TryResolvePlayerTriangle(state, out var resolved),
                $"resolve {target.Label}: {planner.LastDiagnostic}");
            Equal(target.TriangleId, resolved, $"native triangle for {target.Label}");
        }
    }

    private static void SelectsKalmAsTheFirstWorldStoryObjective()
    {
        var catalog = LoadCatalog();
        var story = catalog.ReadTargets(WorldMapNavigationCategory.Story, regionId: 0, gameMoment: 341);
        Equal(1, story.Count, "one first-world story target");
        Equal("Kalm", story[0].Label, "first world story objective");
    }

    private static WorldMapTargetCatalog LoadCatalog()
    {
        return LoadCatalog(LoadMap());
    }

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
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
            @"C:\FF7A11Y\accessibility_prototype";
        return WorldMapTargetCatalog.Load(
            map,
            Path.Combine(sourceRoot, "tools", "kujata", "metadata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "tools", "kujata", "metadata-src", "world-map", "wm-field-menu-names.txt"));
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(',', expected)}], actual [{string.Join(',', actual)}]");
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
