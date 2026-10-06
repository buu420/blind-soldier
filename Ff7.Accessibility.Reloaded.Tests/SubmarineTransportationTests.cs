using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class SubmarineTransportationTests
{
    private const uint BoatPointer = 0x00E3A1C8;

    public static void Run()
    {
        Equal(true, WorldMapVehicleObstacles.TryGetNativeMask(13, out var mask),
            "the usable submarine must have its measured native boarding footprint");
        Equal("00183C7E7E3C1800", Convert.ToHexString(mask.ToArray()),
            "submarine footprint from licensed ff7_en.exe 0096DDB0 + 13 * 8");
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(sourceRoot))
        {
            Console.WriteLine("submarine transportation: installed map cases not run (data roots unset)");
            return;
        }
        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 2);
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        var docks = map.Triangles.Where(t => t.TerrainId == 18)
            .GroupBy(t => (t.MeshX, t.MeshZ)).ToArray();
        Equal(true, docks.Length >= 2, "installed map must supply both submarine dock areas");
        var reachableDocks = 0;
        var unavailableDocks = 0;
        foreach (var dock in docks)
        {
            var surface = dock.First();
            var boat = new WorldMapEntitySnapshot(BoatPointer, 0, false,
                surface.Centroid.X, surface.Centroid.Y, surface.Centroid.Z,
                surface.TerrainId, surface.RegionId & 31, 13, 0);
            var flying = State(map, 207590, 179729, 3) with { Y = 3977, HasModelRotation = true, ModelRotation = 1184 };
            var target = catalog.ReadTargets(WorldMapNavigationCategory.Transportation, flying, [boat]).Single();
            Equal("Submarine", target.Label, "parked usable boat is named in Transportation");
            Equal(true, target.NativeVehicleContact is not null, "flying selection retains native on-foot boarding contact");
            Equal(true, target.ArrivalTriangleIds.Count > 0, $"dock {dock.Key} has a boarding shore");
            Equal(true, target.ArrivalTriangleIds.All(id =>
                WorldMapTerrainPassability.CanTraverse(0, 0, map.Triangles[id].TerrainId)),
                "arrival is walking ground, not water traversable only by the Highwind");
            var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
            var footComponents = WorldMapBroncoLanding.DestinationFootComponents(planner, map, target);
            WorldMapEntitySnapshot[] entities = [boat];
            var controller = new WorldMapNavigationController(map, planner,
                (state, category) => catalog.ReadTargets(category, state, entities));
            while (controller.CurrentCategory != WorldMapNavigationCategory.Transportation)
                controller.HandleAction(FieldNavigationAction.NextCategory, flying);
            var announcement = controller.HandleAction(FieldNavigationAction.ToggleBeacon, flying)?.Speech ?? "";
            if (!map.Triangles.Any(t => t.TerrainId == 0 && footComponents.Contains(planner.GetComponentId(0, 0, t.Id))))
            {
                Equal(false, controller.BeaconEnabled, "a dock isolated from grass cannot promise a Highwind landing");
                Equal(true, announcement.Contains("Route unavailable", StringComparison.Ordinal),
                    "the unreachable dock is still listed and its landing limitation is spoken");
                unavailableDocks++;
                Console.WriteLine($"submarine transportation: dock {dock.Key} truthfully unavailable to Highwind: {controller.LastDiagnostic}");
                continue;
            }
            Equal(true, controller.BeaconEnabled, $"dock {dock.Key} starts navigation: {announcement} ({controller.LastDiagnostic})");
            Equal(true, announcement.Contains("landing", StringComparison.OrdinalIgnoreCase),
                "the Highwind trip is announced as landing then walking");
            var flight = controller.Probe.Route ?? throw new InvalidOperationException("no flight route");
            var end = flight.Waypoints[^1];
            Equal(true, WorldMapHighwindLanding.HasShipFootprint(map, end.X, end.Z),
                "Highwind route ends with all native contact points over grass");
            Equal(false, WorldMapVehicleObstacles.Blocks(0, end.X, end.Z, 13, boat.X, boat.Z, map.WrapWidth, map.WrapHeight),
                "Highwind lands clear of the submarine's boarding footprint");
            var overBoat = flying with { X = boat.X, Z = boat.Z };
            Equal(false, controller.Observe(overBoat)?.Speech?.Contains("Arrived", StringComparison.Ordinal) ?? false,
                "flying over the submarine is not boarding arrival");
            var atSpot = State(map, end.X, end.Z, 3) with { Y = 3977, HasModelRotation = true, ModelRotation = 1184 };
            var now = new DateTime(2026, 10, 6, 19, 0, 0, DateTimeKind.Utc);
            var speech = string.Join(" | ", Enumerable.Range(0, 21)
                .Select(i => controller.Observe(atSpot, now.AddMilliseconds(i * 100), automaticWalkActive: true)?.Speech));
            Equal(true, speech.Contains("press Cancel to land", StringComparison.Ordinal),
                "existing manual landing prompt is given at rest");
            Equal(true, WorldMapHighwindLanding.TryPredictDisembark(map, end.X, end.Z, 1184,
                out var ground, out var footX, out var footZ), "native Highwind disembark reaches ground");
            var onFoot = State(map, footX, footZ, 0) with { Y = WorldMapBroncoLanding.SurfaceHeight(ground, footX, footZ) };
            controller.Observe(onFoot, now.AddSeconds(3), automaticWalkActive: true);
            Equal(true, controller.BeaconEnabled, "same submarine target survives manual landing");
            Equal("Submarine", controller.Probe.TargetLabel, "walking continues to the submarine");
            var walk = controller.Probe.Route ?? throw new InvalidOperationException("no walk route");
            Equal(true, walk.TrianglePath.All(id => WorldMapTerrainPassability.CanTraverse(0, 0, map.Triangles[id].TerrainId)),
                "the complete walk stays on native walking terrain");
            Equal(false, walk.TrianglePath.Any(catalog.EntranceTriangleIds.Contains), "the walk enters no unrelated field");
            var point = walk.Waypoints[^1];
            Equal(true, WorldMapVehicleObstacles.Blocks(0, point.X, point.Z, 13, boat.X, boat.Z, map.WrapWidth, map.WrapHeight),
                "walk route ends inside native submarine boarding contact");
            Equal(true, controller.TryResolveAutomaticInput(onFoot, out _), "walking input is available after landing");
            Equal(false, controller.AutomaticInputHoldsFlightAction, "walking releases Highwind's flight action");
            entities = [boat with { IsPlayer = true }];
            var boarded = State(map, boat.X, boat.Z, 13) with { NativePlayerEntityPointer = BoatPointer };
            var stopped = controller.Observe(boarded, now.AddSeconds(4), automaticWalkActive: true);
            Equal(true, stopped?.StopAutoWalk ?? false, "boarding stops the route to the now-ridden boat");
            Equal(false, controller.BeaconEnabled, "boarded submarine is no longer a parked target");
            Equal(0, catalog.ReadTargets(WorldMapNavigationCategory.Transportation, boarded, entities).Count,
                "the player submarine is excluded from Transportation");
            Console.WriteLine($"submarine transportation: dock {dock.Key}, landing {end.X},{end.Z}, walk to {point.X},{point.Z}");
            reachableDocks++;
        }
        Equal(true, reachableDocks >= 2, "at least two installed docks must prove a real Highwind landing and walk");
        Equal(true, unavailableDocks > 0, "an isolated dock must prove the truthful refusal");
        Console.WriteLine("submarine transportation tests passed with installed game data.");
    }

    private static WorldMapStateSnapshot State(WorldMapData map, int x, int z, int model)
    {
        if (!WorldMapBroncoLanding.TryFindSurface(map, x, z, out var ground))
            throw new InvalidOperationException($"no installed surface at {x},{z}");
        return new WorldMapStateSnapshot(3, 0, 2, 1620, x,
            WorldMapBroncoLanding.SurfaceHeight(ground, x, z), z,
            1232, 1232, ground.TerrainId, ground.RegionId & 31, model,
            model == 3 ? 120 : 30, 112, new FieldNavigationControlTransform(-7))
        { TerrainScriptId = ground.TerrainScriptId };
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Submarine transportation - {message}: expected {expected}, got {actual}");
    }
}
