using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapTargetCatalogTests
{
    internal static void Run()
    {
        ExposesTheApprovedCategoriesIncludingRegions();
        JoinsWorldLocationNamesByNativeFieldId();
        ResolvesInstalledLocationsToNativeTerrain();
        PlacesEveryWorldEntranceOnItsResolvedNativeTriangle();
        GroupsNativeChocoboTracksByRegion();
        BuildsOnlySignificantReachableTerrainRegions();
        OrdersTerrainRegionsNearestFirstAndRecognizesTheCurrentOne();
        BuildsTransportationAndEventsOnlyFromLiveNativeEntities();
        SelectsKalmAsTheFirstWorldStoryObjective();
        SelectsOnlyTheCurrentMainStoryDestinationsThroughTheEnding();
        BuildsDynamicStoryObjectivesOnlyFromMatchingLiveNativeEntities();
    }

    private static void JoinsWorldLocationNamesByNativeFieldId()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        Equal(true, midgar.StableId.StartsWith("world-location:1:", StringComparison.Ordinal),
            "Midgar joins to native menu location id 1");
        Equal(true, midgar.ArrivalTriangleIds.All(id =>
            map.Triangles[id].MeshX == 22 &&
            map.Triangles[id].MeshZ == 14 &&
            map.Triangles[id].TerrainScriptId == 7),
            "Midgar uses its native terrain-script entrance");

        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        Equal(true, kalm.StableId.StartsWith("world-location:2:", StringComparison.Ordinal),
            "Kalm joins to native menu location id 2");
        Equal(true, kalm.ArrivalTriangleIds.All(id =>
            map.Triangles[id].MeshX == 24 &&
            map.Triangles[id].MeshZ == 13 &&
            map.Triangles[id].TerrainScriptId == 7),
            "Kalm uses its native terrain-script entrance");
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

    private static void ExposesTheApprovedCategoriesIncludingRegions()
    {
        SequenceEqual(
            [
                WorldMapNavigationCategory.Locations,
                WorldMapNavigationCategory.Story,
                WorldMapNavigationCategory.Transportation,
                WorldMapNavigationCategory.Events,
                WorldMapNavigationCategory.ChocoboTracks,
                WorldMapNavigationCategory.Regions
            ],
            WorldMapTargetCatalog.CategoryOrder,
            "world navigation category order");
    }

    private static void BuildsOnlySignificantReachableTerrainRegions()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var planner = new WorldMapRoutePlanner(map);
        var junon = catalog.Locations.Single(target => target.Label == "Junon");
        var state = StateAt(map, junon);

        var targets = catalog.ReadTargets(
            WorldMapNavigationCategory.Regions,
            state,
            Array.Empty<WorldMapEntitySnapshot>());

        Equal(true, targets.Count > 0, "Junon has useful terrain-region targets");
        Equal(true, targets.All(target => target.Kind == WorldMapTargetKind.TerrainArea),
            "Regions contains only terrain-area targets");
        Equal(true, targets.All(target => planner.CanReach(state, target)),
            "Regions never exposes an unroutable target");
        Equal(false, targets.Any(target => target.Label.StartsWith("Mountain", StringComparison.Ordinal)),
            "untraversable mountain terrain is not listed");

        var forest = targets.Single(target => target.Label == "Forest, Junon Area");
        Equal(258, forest.ArrivalTriangleIds.Count,
            "the two significant Junon forest patches are merged and the 30-triangle sliver is dropped");
        Equal(true, forest.ArrivalTriangleIds.All(id => map.Triangles[id].TerrainId == 1),
            "the forest target contains only native forest terrain");
    }

    private static void OrdersTerrainRegionsNearestFirstAndRecognizesTheCurrentOne()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var junon = catalog.Locations.Single(target => target.Label == "Junon");
        var state = StateAt(map, junon);
        var targets = catalog.ReadTargets(
            WorldMapNavigationCategory.Regions,
            state,
            Array.Empty<WorldMapEntitySnapshot>());

        Equal("Wasteland, Junon Area", targets[0].Label,
            "the terrain under Junon is the nearest region");

        var distances = targets
            .Select(target => MinimumWrappedDistanceSquared(map, state, target))
            .ToArray();
        Equal(true, distances.Zip(distances.Skip(1), (first, second) => first <= second).All(value => value),
            "terrain regions are ordered nearest first");

        var retainedTriangle = map.Triangles[targets[0].ArrivalTriangleIds.First()];
        var inside = state with
        {
            X = retainedTriangle.Centroid.X,
            Y = retainedTriangle.Centroid.Y,
            Z = retainedTriangle.Centroid.Z,
            TerrainId = retainedTriangle.TerrainId,
            RegionId = retainedTriangle.RegionId
        };
        var current = catalog.ReadTargets(
                WorldMapNavigationCategory.Regions,
                inside,
                Array.Empty<WorldMapEntitySnapshot>())
            .Single(target => target.StableId == targets[0].StableId);
        Equal(true, current.HasArrived(retainedTriangle.Id),
            "being inside a significant region is an arrival");
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
        Equal(837, tracks.Sum(target => target.ArrivalTriangleIds.Count), "all native track triangles grouped");
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
            state = state with { TerrainScriptId = triangle.TerrainScriptId };

            Equal(true, planner.TryResolvePlayerTriangle(state, out var resolved),
                $"resolve {target.Label}: {planner.LastDiagnostic}");
            Equal(true, target.ArrivalTriangleIds.Contains(resolved),
                $"resolved triangle {resolved} belongs to native trigger " +
                $"[{string.Join(',', target.ArrivalTriangleIds)}] for {target.Label}; " +
                $"target triangle={target.TriangleId}, terrain={triangle.TerrainId}, script={triangle.TerrainScriptId}; " +
                $"vertices={triangle.Vertex0}/{triangle.Vertex1}/{triangle.Vertex2}; point={target.X},{target.Z}; " +
                $"resolved mesh={map.Triangles[resolved].MeshX},{map.Triangles[resolved].MeshZ}, " +
                $"terrain={map.Triangles[resolved].TerrainId}, script={map.Triangles[resolved].TerrainScriptId}");
            Equal(true, target.HasArrived(state, resolved),
                $"native mesh and script arrival for {target.Label}");
        }
    }

    private static void SelectsKalmAsTheFirstWorldStoryObjective()
    {
        var catalog = LoadCatalog();
        var story = catalog.ReadTargets(WorldMapNavigationCategory.Story, regionId: 0, gameMoment: 341);
        Equal(1, story.Count, "one first-world story target");
        Equal("Kalm", story[0].Label, "first world story objective");
    }

    private static void SelectsOnlyTheCurrentMainStoryDestinationsThroughTheEnding()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        StoryLabels(catalog, 340, []);
        StoryLabels(catalog, 341, ["Kalm"]);
        StoryLabels(catalog, 385, ["Chocobo Farm", "Mythril Mine (Midgar side)"]);
        StoryLabels(catalog, 387, ["Junon"]);
        StoryLabels(catalog, 415, ["Mt. Corel"]);
        StoryLabels(catalog, 427, ["North Corel"]);
        StoryLabels(catalog, 469, ["Cosmo Canyon"]);
        StoryLabels(catalog, 523,
            ["Nibelheim (Town Side)", "Mt. Nibel (Nibelheim Side)", "Rocket Town (South Side)"]);
        StoryLabels(catalog, 566, ["North Corel"]);
        StoryLabels(catalog, 583, ["Temple of the Ancients"]);
        StoryLabels(catalog, 638, ["Bone Village"]);
        StoryLabels(catalog, 677, ["Icicle Inn (South Side)"]);
        StoryLabels(catalog, 770, []);
        StoryLabels(catalog, 1033, ["Mideel"]);
        StoryLabels(catalog, 1110, ["North Corel", "Condor"]);
        // 1116 is written once both Huge Materia missions are finished, whichever order
        // they were done in and whether or not the train was caught. Offering the Fort
        // again at that point sends the party back to something already over, so this
        // stage is Mideel alone.
        StoryLabels(catalog, 1116, ["Mideel"]);
        StoryLabels(catalog, 1199, ["Junon"]);
        StoryLabels(catalog, 1299, ["Rocket Town (North Side)"]);
        StoryLabels(catalog, 1389, ["Cosmo Canyon"]);
        StoryLabels(catalog, 1392, ["Bone Village"]);
        StoryLabels(catalog, 1396, []);
        StoryLabels(catalog, 1397, ["Bone Village"]);
        StoryLabels(catalog, 1400, []);
        // wm0.ev's Highwind Tick tests native point 14 below 1596 and enters field
        // 52 from it at 1580; only at 1596 does it switch to point 9, write 1598 and
        // approach Midgar. Treating the whole 1570 band as Midgar skipped the crater
        // flyover the story goes through first.
        // The crater flyover and the landing belong to the Highwind's own script, so
        // the label-only overload has no state to qualify them with and correctly says
        // nothing. HighwindStoryLabels below drives the state-aware path.
        StoryLabels(catalog, 1570, []);
        StoryLabels(catalog, 1580, []);
        StoryLabels(catalog, 1596, ["Midgar"]);
        StoryLabels(catalog, 1598, []);
        // Location 59 has no resolved native trigger entry, so a label lookup finds
        // nothing. The landing is built from the terrain the native landing handler
        // itself tests instead - FUN_0076667C requires terrain 27 under the player
        // before it invokes the world system's landing function.
        StoryLabels(catalog, 1620, []);
        StoryLabels(catalog, 1997, []);
        StoryLabels(catalog, 1998, []);

        // wm0.ev's Highwind Tick tests native point 14 below 1596 and enters field 52
        // from it at 1580; only at 1596 does it switch to point 9, write 1598 and
        // approach Midgar. Below 1580 the same proximity calls the barrier bounce
        // instead, so nothing is offered there.
        HighwindStoryLabels(catalog, map, 1570, []);
        HighwindStoryLabels(catalog, map, 1580, ["Northern Crater (fly over)"]);
        // The proximity on point 14 advances the story at 1580 and only there. Past it
        // the same position over the same crater does nothing at all, because what comes
        // next is the airship scene rather than another approach, so the flyover is not
        // still offered at 1595.
        HighwindStoryLabels(catalog, map, 1595, []);
        // Reaching Midgar here is the Highwind's own Tick on point 9, not the ordinary
        // approach on foot, so the party walking is offered nothing at this stage.
        HighwindStoryLabels(catalog, map, 1596, ["Midgar"], onFootExpected: []);

        // Location 59 has no resolved native trigger entry, so a label lookup finds
        // nothing at all. The landing is built from the terrain the native landing
        // handler itself tests - FUN_0076667C requires terrain 27 under the player.
        HighwindStoryLabels(catalog, map, 1620, ["Northern Crater (land the Highwind)"]);
        HighwindStoryLabels(catalog, map, 1997, ["Northern Crater (land the Highwind)"]);
        HighwindStoryLabels(catalog, map, 1998, []);
    }

    private static void BuildsDynamicStoryObjectivesOnlyFromMatchingLiveNativeEntities()
    {
        var map = LoadMap();
        var catalog = LoadCatalog(map);
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var triangle = map.Triangles[kalm.TriangleId];
        var template = new WorldMapStateSnapshot(
            WorldMapStateReader.WorldModule, map.WorldMapType, 0, 1396,
            kalm.X, kalm.Y, kalm.Z, 0, 0,
            triangle.TerrainId, kalm.RegionId,
            0, 30, 0, new FieldNavigationControlTransform(0));
        WorldMapEntitySnapshot[] entities =
        [
            new(0x1000, 0x2000, true, kalm.X, kalm.Y, kalm.Z, triangle.TerrainId, kalm.RegionId, 0, 1),
            new(0x2000, 0x3000, false, kalm.X + 20, kalm.Y, kalm.Z, triangle.TerrainId, kalm.RegionId, 26, 1),
            new(0x3000, 0x4000, false, kalm.X + 40, kalm.Y, kalm.Z, triangle.TerrainId, kalm.RegionId, 10, 1)
        ];

        var key = catalog.ReadTargets(WorldMapNavigationCategory.Story, template, entities);
        SequenceEqual(["Key of the Ancients"], key.Select(target => target.Label),
            "the live key is the only story target during the underwater search");
        Equal(WorldMapTargetKind.Story, key[0].Kind, "dynamic key uses story target kind");

        var diamond = catalog.ReadTargets(
            WorldMapNavigationCategory.Story,
            template with { GameMoment = 1400 },
            entities);
        SequenceEqual(["Diamond Weapon"], diamond.Select(target => target.Label),
            "the live weapon is the only story target during its approach");

        var afterDiamond = catalog.ReadTargets(
            WorldMapNavigationCategory.Story,
            template with { GameMoment = 1570 },
            entities);
        // 1570 is between the weapon and the crater flyover: the Highwind Tick calls
        // the barrier bounce there rather than entering anything, and the scene that
        // advances to 1580 happens aboard the airship. Nothing on the world map is the
        // next step, and this template is the party on foot in any case.
        SequenceEqual(Array.Empty<string>(), afterDiamond.Select(target => target.Label),
            "the defeated weapon is no longer exposed after its progression window");
    }

    /// <summary>
    /// The two Northern Crater stops the trigger metadata cannot name. Both belong to
    /// the Highwind's own world script - the flyover is its Tick measuring native
    /// point 14, the landing is the world system's landing function invoked for model
    /// 3 - so both need the state that says the Highwind is what is being flown, on
    /// the overworld.
    /// </summary>
    private static void HighwindStoryLabels(
        WorldMapTargetCatalog catalog,
        WorldMapData map,
        int gameMoment,
        IReadOnlyList<string> expected,
        IReadOnlyList<string>? onFootExpected = null)
    {
        var state = new WorldMapStateSnapshot(
            WorldMapStateReader.WorldModule, map.WorldMapType, 4, gameMoment,
            131073, 0, 36767, 0, 0, 27, 0, 3, 0, 0, new FieldNavigationControlTransform(0));
        var actual = catalog.ReadTargets(WorldMapNavigationCategory.Story, state, []);
        SequenceEqual(expected, actual.Select(target => target.Label),
            $"Highwind story targets at game moment {gameMoment}");

        // On foot, none of the airship's own stops are offered. By default that is the
        // crater, which only the Highwind reaches; a caller can say so explicitly where
        // the stage is one the airship reaches under an ordinary name.
        var onFoot = catalog.ReadTargets(
            WorldMapNavigationCategory.Story, state with { PlayerModelId = 0 }, []);
        SequenceEqual(
            onFootExpected ??
                expected.Where(label => !label.Contains("Crater", StringComparison.OrdinalIgnoreCase)).ToArray(),
            onFoot.Select(target => target.Label),
            $"the party on foot cannot fly or land at game moment {gameMoment}");
    }
    private static void StoryLabels(
        WorldMapTargetCatalog catalog,
        int gameMoment,
        IReadOnlyList<string> expected)
    {
        var actual = catalog.ReadTargets(WorldMapNavigationCategory.Story, regionId: 0, gameMoment);
        SequenceEqual(expected, actual.Select(target => target.Label), $"story targets at game moment {gameMoment}");
        Equal(true, actual.All(target => target.Category == WorldMapNavigationCategory.Story),
            $"story category at game moment {gameMoment}");
        Equal(true, actual.All(target => target.Kind == WorldMapTargetKind.Story),
            $"story kind at game moment {gameMoment}");
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

    private static WorldMapStateSnapshot StateAt(
        WorldMapData map,
        WorldMapNavigationTarget target)
    {
        var triangle = map.Triangles[target.TriangleId];
        return new WorldMapStateSnapshot(
            WorldMapStateReader.WorldModule,
            map.WorldMapType,
            map.WorldProgress,
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
            new FieldNavigationControlTransform(0))
        {
            TerrainScriptId = triangle.TerrainScriptId
        };
    }

    private static double MinimumWrappedDistanceSquared(
        WorldMapData map,
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target) =>
        target.ArrivalTriangleIds.Min(id =>
        {
            var center = map.Triangles[id].Centroid;
            return WorldMapTargetCatalog.WrappedDistanceSquared(
                map,
                state.X,
                state.Z,
                center.X,
                center.Z);
        });

    private static WorldMapTargetCatalog LoadCatalog(WorldMapData map)
    {
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
            @"C:\FF7A11Y\accessibility_prototype";
        return WorldMapTargetCatalog.Load(
            map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
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





