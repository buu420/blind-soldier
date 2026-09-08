using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapEntranceProximityCueTests
{
    private static readonly DateTime Start = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        UsesTheNearestReachableNativeEntranceInsteadOfTheDisplayMarker();
        RefusesACloserEntranceOnAnUnreachableTerrainComponent();
        RepeatsAtTheFieldExitCadenceAndGetsLouderTowardTheEntrance();
        UsesTheWrappedCameraRelativeDirectionForSpatialAudio();
        UsesJunonsNativeScriptSevenEntranceFromTheShippedCatalog();
    }

    private static void UsesTheNearestReachableNativeEntranceInsteadOfTheDisplayMarker()
    {
        var map = CreateMap(CreateTriangle(0, terrainId: 0, regionId: 1));
        var planner = new WorldMapRoutePlanner(map);
        var state = CreateState(x: 1_000, z: 1_000, terrainId: 0, regionId: 1);
        var junon = CreateLocation(
            "Junon",
            "world-location:junon",
            displayX: 20_000,
            displayZ: 20_000,
            CreateArrival(triangleId: 0, x: 3_500, z: 1_000, terrainScriptId: 7));
        var condor = CreateLocation(
            "Condor",
            "world-location:condor",
            displayX: 1_100,
            displayZ: 1_000,
            CreateArrival(triangleId: 0, x: 6_000, z: 1_000, terrainScriptId: 7));
        var tracker = new WorldMapEntranceProximityCueTracker(
            map,
            planner,
            [condor, junon],
            innerRange: 512,
            outerRange: 4_096,
            TimeSpan.FromMilliseconds(3_200));

        var cue = tracker.Update(state, Start)
            ?? throw new InvalidOperationException("a nearby native entrance should produce a cue");

        Equal("Junon", cue.Target.Label, "nearest native entrance label");
        Equal(3_500, cue.Arrival.X, "native trigger point, not the display marker");
        Equal(2_500d, cue.DistanceUnits, "native trigger distance");
    }

    private static void RefusesACloserEntranceOnAnUnreachableTerrainComponent()
    {
        var map = CreateMap(
            CreateTriangle(0, terrainId: 0, regionId: 1),
            CreateTriangle(1, terrainId: 1, regionId: 2));
        var planner = new WorldMapRoutePlanner(map);
        var state = CreateState(x: 1_000, z: 1_000, terrainId: 0, regionId: 1);
        var unreachable = CreateLocation(
            "Unreachable",
            "world-location:unreachable",
            displayX: 1_100,
            displayZ: 1_000,
            CreateArrival(triangleId: 1, x: 1_100, z: 1_000, terrainScriptId: 7));
        var reachable = CreateLocation(
            "Reachable",
            "world-location:reachable",
            displayX: 3_000,
            displayZ: 1_000,
            CreateArrival(triangleId: 0, x: 3_000, z: 1_000, terrainScriptId: 7));
        var tracker = new WorldMapEntranceProximityCueTracker(
            map,
            planner,
            [unreachable, reachable],
            innerRange: 512,
            outerRange: 4_096,
            TimeSpan.FromMilliseconds(3_200));

        var cue = tracker.Update(state, Start)
            ?? throw new InvalidOperationException("a reachable entrance should remain available");

        Equal("Reachable", cue.Target.Label, "unreachable entrance is never announced");
        Equal(0, cue.Arrival.TriangleId, "arrival shares the player's native component");
    }

    private static void RepeatsAtTheFieldExitCadenceAndGetsLouderTowardTheEntrance()
    {
        var map = CreateMap(CreateTriangle(0, terrainId: 0, regionId: 1));
        var target = CreateLocation(
            "Junon",
            "world-location:junon",
            displayX: 20_000,
            displayZ: 20_000,
            CreateArrival(triangleId: 0, x: 3_500, z: 1_000, terrainScriptId: 7));
        var tracker = new WorldMapEntranceProximityCueTracker(
            map,
            new WorldMapRoutePlanner(map),
            [target],
            innerRange: 512,
            outerRange: 4_096,
            TimeSpan.FromMilliseconds(3_200));
        var farState = CreateState(x: 1_000, z: 1_000, terrainId: 0, regionId: 1);

        var first = tracker.Update(farState, Start)
            ?? throw new InvalidOperationException("the first entrance pulse should be immediate");
        var early = tracker.Update(farState, Start.AddMilliseconds(3_199));
        var repeated = tracker.Update(farState, Start.AddMilliseconds(3_200))
            ?? throw new InvalidOperationException("the entrance pulse should repeat at 3200 ms");

        NearlyEqual(0.4453125f, first.Gain, 0.000001f, "linear world entrance gain");
        Equal(true, tracker.HasAudibleTarget, "range remains active between pulses");
        Equal(null, early, "no early pulse");
        Equal("Junon", repeated.Target.Label, "pulse repeats for the same entrance");

        var nearState = CreateState(x: 3_100, z: 1_000, terrainId: 0, regionId: 1);
        var louder = tracker.Update(nearState, Start.AddMilliseconds(6_400))
            ?? throw new InvalidOperationException("a closer entrance position should still pulse");
        Equal(1f, louder.Gain, "inner range reaches full gain");

        var outside = CreateState(x: 7_800, z: 100, terrainId: 0, regionId: 1);
        Equal(null, tracker.Update(outside, Start.AddMilliseconds(6_500)), "outside range is silent");
        Equal(false, tracker.HasAudibleTarget, "outside range clears audible state");
        Equal(
            true,
            tracker.Update(farState, Start.AddMilliseconds(6_600)).HasValue,
            "re-entering range pulses immediately instead of keeping a stale deadline");
    }

    private static void UsesTheWrappedCameraRelativeDirectionForSpatialAudio()
    {
        var map = CreateMap(CreateTriangle(0, terrainId: 0, regionId: 1));
        var target = CreateLocation(
            "Seam entrance",
            "world-location:seam",
            displayX: 8,
            displayZ: 1_000,
            CreateArrival(triangleId: 0, x: 8, z: 1_000, terrainScriptId: 7));
        var proximity = new WorldMapEntranceProximityCue(
            target,
            target.NativeLocationArrivals[0],
            Gain: 1f,
            DistanceUnits: 16d);
        var state = CreateState(x: map.WrapWidth - 8, z: 1_000, terrainId: 0, regionId: 1);

        var cue = WorldMapEntranceProximitySpatializer.CreateCue(map, state, proximity)
            ?? throw new InvalidOperationException("the wrapped entrance direction should be spatializable");

        NearlyEqual(1f, cue.SteamAudioX, 0.000001f, "short wrapped direction is to the right");
        NearlyEqual(0f, cue.SteamAudioZ, 0.000001f, "wrapped direction is not behind or ahead");
        Equal(16d, cue.DistanceUnits, "spatial cue keeps wrapped distance");
    }

    private static void UsesJunonsNativeScriptSevenEntranceFromTheShippedCatalog()
    {
        var (map, catalog) = LoadShippedWorldMap();
        var junon = catalog.Locations.Single(target => target.Label == "Junon");
        var arrival = junon.NativeLocationArrivals[0];
        var triangle = map.Triangles[arrival.TriangleId];
        var state = new WorldMapStateSnapshot(
            WorldMapStateReader.WorldModule,
            map.WorldMapType,
            map.WorldProgress,
            400,
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
            TerrainScriptId = triangle.TerrainScriptId
        };
        var tracker = new WorldMapEntranceProximityCueTracker(
            map,
            new WorldMapRoutePlanner(map),
            catalog.Locations,
            innerRange: 512,
            outerRange: 4_096,
            TimeSpan.FromMilliseconds(3_200));

        var cue = tracker.Update(state, Start)
            ?? throw new InvalidOperationException("Junon's shipped native entrance should produce a cue");

        Equal("Junon", cue.Target.Label, "Junon is selected at its entrance");
        Equal(7, cue.Arrival.TerrainScriptId, "Junon uses its native script-seven trigger");
        Equal(true, junon.ArrivalTriangleIds.Contains(cue.Arrival.TriangleId),
            "cue points at a native Junon trigger triangle");
    }

    private static WorldMapData CreateMap(params WorldMapTriangle[] triangles) =>
        new(
            worldMapType: 0,
            worldProgress: 0,
            blockGridWidth: 1,
            blockGridHeight: 1,
            rawBlockCount: 1,
            triangles,
            Array.Empty<WorldMapReplacementBlock>(),
            "synthetic");

    private static WorldMapTriangle CreateTriangle(int id, int terrainId, int regionId) =>
        new(
            id,
            SourceBlockIndex: 0,
            MeshX: 0,
            MeshZ: 0,
            MeshIndex: 0,
            TriangleIndex: id,
            new WorldMapVertex(0, 0, 0),
            new WorldMapVertex(8_191, 0, 0),
            new WorldMapVertex(0, 0, 8_191),
            terrainId,
            TerrainScriptId: 0,
            TextureId: 0,
            regionId,
            HasChocoboTracks: false,
            Array.Empty<int>());

    private static WorldMapStateSnapshot CreateState(int x, int z, int terrainId, int regionId) =>
        new(
            WorldMapStateReader.WorldModule,
            0,
            0,
            400,
            x,
            0,
            z,
            0,
            0,
            terrainId,
            regionId,
            0,
            30,
            0,
            new FieldNavigationControlTransform(0));

    private static WorldMapNativeLocationArrival CreateArrival(
        int triangleId,
        int x,
        int z,
        int terrainScriptId) =>
        new(
            triangleId,
            MeshX: 0,
            MeshZ: 0,
            terrainScriptId,
            x,
            Y: 0,
            z);

    private static WorldMapNavigationTarget CreateLocation(
        string label,
        string stableId,
        int displayX,
        int displayZ,
        params WorldMapNativeLocationArrival[] arrivals) =>
        new(
            WorldMapNavigationCategory.Locations,
            WorldMapTargetKind.Location,
            label,
            displayX,
            0,
            displayZ,
            arrivals[0].TriangleId,
            RegionId: 1,
            stableId,
            arrivals.Select(arrival => arrival.TriangleId).ToHashSet())
        {
            NativeLocationArrivals = arrivals
        };

    private static (WorldMapData Map, WorldMapTargetCatalog Catalog) LoadShippedWorldMap()
    {
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
            @"C:\FF7A11Y\accessibility_prototype";
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir";
        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "WM0.MAP"), 0, 0);
        var catalog = WorldMapTargetCatalog.Load(
            map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return (map, catalog);
    }

    private static void NearlyEqual(float expected, float actual, float tolerance, string label)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
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
