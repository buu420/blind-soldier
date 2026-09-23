using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Steering the Tiny Bronco.
///
/// <para>From <c>ff7_accessibility_steam2026_x64 (9).log</c>, 2026-09-23: boarding works -
/// 07:52:11 <c>arrived on native triangle 89722</c>, 07:52:12 the player entity becomes
/// <c>0x00E3A1C8</c> model 5. Every auto walk taken afterwards fails the same way. Eight
/// attempts between 08:36:13 and 08:42:33, all <c>Auto walk stopped. Could not get closer
/// to Gongaga.</c>, with the waypoint index stuck at 0 and the off-route offset climbing
/// 77, 154, 231, 476, 707, 822, 1012, 1246, 1344 while the boat swung back and forth
/// along X in a thousand-unit band.</para>
///
/// <para>The mod was steering a boat at a town. Replayed against the installed map, the
/// route it built crosses grass, riverside, hillside and jungle - four terrains the boat
/// was never once observed on across 1677 native samples, because it cannot occupy
/// them.</para>
/// </summary>
internal static class WorldMapVehicleTerrainTests
{
    // The boat where the log left it, at the start of the 08:36:08 auto walk.
    private const int BoatX = 112773;
    private const int BoatY = 31;
    private const int BoatZ = 167101;
    private const int BoatTerrain = 5;
    private const int Region = 4;

    /// <summary>
    /// Everything the mod lets the party walk on. Model 5 was given all of this, plus
    /// water, which is how a boat came to be routed through a jungle.
    /// </summary>
    private static readonly int[] WalkingTerrain =
        [0, 1, 7, 8, 9, 10, 11, 13, 14, 16, 17, 19, 20, 21, 24, 25, 27, 28, 29, 30];

    public static void Run()
    {
        TheTinyBroncoIsABoat();
        Console.WriteLine("world map vehicle terrain tests passed.");
    }

    public static void RunWithInstalledGameData()
    {
        if (!TryLoadInstalledWorld(out var map, out var catalog))
        {
            Console.WriteLine("world vehicle terrain: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        NoRouteAboardEverCrossesGroundTheVehicleCannotOccupy(map, catalog);
        ATownIsStillNamedAboardAndReachedByALanding(map, catalog);
        WalkingToTheParkedBoatIsUnchanged(map, catalog);
        Console.WriteLine("world map vehicle terrain tests passed with installed game data.");
    }

    /// <summary>
    /// The terrain rule itself.
    ///
    /// <para>Across the whole recorded session the party on foot occupied terrain 0, 11,
    /// 16 and 17 and never 4, 5 or 6; aboard the Bronco it occupied 4, 5 and 6 across 1677
    /// samples and never anything else, including the minutes the mod spent steering it at
    /// inland Gongaga. Terrain 3 is deliberately not granted: it is 87,411 of the map's
    /// 142,586 triangles, and a boat that can cross the open sea is the progression the
    /// Highwind exists to give.</para>
    /// </summary>
    private static void TheTinyBroncoIsABoat()
    {
        foreach (var water in new[] { 4, 5, 6 })
        {
            Equal(true, WorldMapTerrainPassability.CanTraverse(5, 0, water),
                $"the Tiny Bronco must keep terrain {water} ({WorldMapTerrainNames.GetName(water)}), " +
                "which is where the log records it");
        }

        foreach (var land in WalkingTerrain)
        {
            Equal(false, WorldMapTerrainPassability.CanTraverse(5, 0, land),
                $"the Tiny Bronco must not be given terrain {land} " +
                $"({WorldMapTerrainNames.GetName(land)}); it is a boat");
        }

        Equal(false, WorldMapTerrainPassability.CanTraverse(5, 0, 3),
            "and it must not be given the open sea, which is the Highwind's job");
        Equal(false, WorldMapTerrainPassability.CanTraverse(5, 0, 26),
            "nor the deep sea");

        // The models this is not about must be left exactly as they were.
        Equal(true, WorldMapTerrainPassability.CanTraverse(0, 0, 0), "Cloud still walks on grass");
        Equal(false, WorldMapTerrainPassability.CanTraverse(0, 0, 6), "and still cannot walk on water");
        Equal(true, WorldMapTerrainPassability.CanTraverse(6, 0, 0), "the Buggy still drives on grass");
        Equal(true, WorldMapTerrainPassability.CanTraverse(6, 0, 4), "and still fords a river crossing");
        Equal(false, WorldMapTerrainPassability.CanTraverse(6, 0, 5), "but does not sail a river");
        Equal(true, WorldMapTerrainPassability.CanTraverse(3, 0, 2), "the Highwind still flies over mountains");
    }

    /// <summary>
    /// The invariant the failure breaks, stated generally: whatever the mod is prepared to
    /// walk the party along, the party has to be able to occupy. Checked for every target
    /// the catalog offers from the boat, not only for Gongaga, because the shipped rule
    /// made 26 of 37 towns look sailable.
    /// </summary>
    private static void NoRouteAboardEverCrossesGroundTheVehicleCannotOccupy(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var aboard = State(BoatX, BoatY, BoatZ, BoatTerrain, 1, Region, 5);
        Equal(true, planner.TryResolvePlayerTriangle(aboard, out var boatTriangle),
            "the logged boat position must resolve on the installed map");
        Equal(true, WorldMapTerrainPassability.CanTraverse(5, 0, map.Triangles[boatTriangle].TerrainId),
            "and the boat must be somewhere a boat can be");

        var routed = 0;
        foreach (var category in WorldMapTargetCatalog.CategoryOrder)
        {
            foreach (var target in catalog.ReadTargets(category, aboard, []))
            {
                if (!planner.TryBuildRoute(aboard, target, out var plan))
                {
                    continue;
                }

                routed++;
                var impassable = plan.TrianglePath
                    .Where(id => !WorldMapTerrainPassability.CanTraverse(
                        5, map.WorldMapType, map.Triangles[id].TerrainId))
                    .Select(id => $"t{id} {WorldMapTerrainNames.GetName(map.Triangles[id].TerrainId)}")
                    .Distinct()
                    .ToArray();
                Equal(0, impassable.Length,
                    $"the route to \"{target.Label}\" aboard the Tiny Bronco crosses ground the boat " +
                    $"cannot occupy: [{string.Join(", ", impassable.Take(8))}]");
            }
        }

        // Gongaga by name, because that is the one the user watched fail eight times.
        var gongaga = catalog.Locations.Single(target => target.Label == "Gongaga");
        Equal(false, planner.CanReach(aboard, gongaga),
            "Gongaga must not be claimed as somewhere a boat can get to: " + planner.LastDiagnostic);
        Equal(false, planner.TryBuildRoute(aboard, gongaga, out _),
            "and no route to it may be built from the water");
    }

    /// <summary>
    /// Correcting the rule must not make the towns disappear instead.
    ///
    /// <para>A category that goes quiet is the worst outcome: it hides that Gongaga exists
    /// at all. No town can be sailed into - FUN_0074CECA holds model 5 to terrain 4, 5 and 6,
    /// and every entrance is on land - but the game's own get-off lands the party on the
    /// shore, so the town is reached by landing and walking. Selecting it says so, and
    /// starting it steers the boat along water, never at the bank the log recorded.</para>
    /// </summary>
    private static void ATownIsStillNamedAboardAndReachedByALanding(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        // As the live reader delivers it: the boat's rotation is part of the state.
        var aboard = State(BoatX, BoatY, BoatZ, BoatTerrain, 1, Region, 5) with
        {
            Facing = 0x800 - 2273,
            HasModelRotation = true,
            ModelRotation = 0x1000 + 0x800 - 2273
        };
        var now = new DateTime(2026, 9, 23, 8, 36, 8, DateTimeKind.Utc);
        while (runtime.Navigation.CurrentCategory != WorldMapNavigationCategory.Locations)
        {
            runtime.Navigation.HandleAction(FieldNavigationAction.NextCategory, aboard, now);
        }

        var spoken = runtime.Navigation
            .HandleAction(FieldNavigationAction.RepeatTarget, aboard, now)!.Value.Speech;
        Equal(false, spoken.Contains("none available", StringComparison.OrdinalIgnoreCase),
            $"the towns must still be listed from the boat; the category said \"{spoken}\"");

        // Walk the category round to Gongaga and read what it says.
        var described = string.Empty;
        for (var step = 0; step < catalog.Locations.Count; step++)
        {
            var output = runtime.Navigation
                .HandleAction(FieldNavigationAction.NextTarget, aboard, now)!.Value.Speech;
            if (output.Contains(", Gongaga.", StringComparison.Ordinal))
            {
                described = output;
                break;
            }
        }

        Equal(true, described.Length > 0, "Gongaga must still be reachable in the category list");
        Equal(true, described.Contains("By Tiny Bronco to a shore landing, then on foot", StringComparison.Ordinal),
            $"and selecting it from the boat must say how it is reached; said \"{described}\"");

        var started = runtime.Navigation
            .HandleAction(FieldNavigationAction.ToggleBeacon, aboard, now)!.Value.Speech;
        Equal(true, runtime.Navigation.BeaconEnabled,
            $"asking to go there starts the trip to the landing; said \"{started}\"");
        Equal(true, runtime.Navigation.Probe.Route!.TrianglePath.All(id =>
                WorldMapTerrainPassability.CanTraverse(5, map.WorldMapType, map.Triangles[id].TerrainId)),
            "and the boat's route is water it may occupy, all of it");
        Equal(true, runtime.Navigation.TryResolveAutomaticInput(aboard, out var input),
            "auto walk steers the boat");
        var stick = FieldNavigationMovementObserver.ToStickDirection(input);
        var angle = aboard.CameraFront * Math.PI * 2d / 4096d;
        var stepX = (int)(60 * (stick.X * Math.Cos(angle) - stick.Y * Math.Sin(angle)));
        var stepZ = (int)(60 * (stick.X * Math.Sin(angle) + stick.Y * Math.Cos(angle)));
        Equal(true, WorldMapBroncoLanding.HasBoatFootprint(map, aboard.X + stepX, aboard.Z + stepZ),
            $"and its first step, {input}, keeps the boat on water rather than at the bank");
    }

    /// <summary>
    /// The 0.6.6 approach must be untouched: on foot, the parked Bronco is still walkable
    /// to and still routes to the shore the native mask reaches.
    /// </summary>
    private static void WalkingToTheParkedBoatIsUnchanged(
        WorldMapData map,
        WorldMapTargetCatalog catalog)
    {
        var runtime = CreateShippedRuntime(map, catalog);
        var onFoot = State(109923, 258, 162872, 11, 0, Region, 0);
        var bronco = new WorldMapEntitySnapshot(0x00E3A1C8, 0, false, 109072, 102, 162804, 5, Region, 5, 0);
        runtime.UpdateEntities([bronco]);

        var target = runtime.Catalog
            .ReadTargets(WorldMapNavigationCategory.Transportation, onFoot, runtime.Entities)
            .Single(candidate => candidate.Label == "Tiny Bronco");
        Equal(true, runtime.Planner.CanReach(onFoot, target),
            $"the parked boat must still be walkable to on foot: {runtime.Planner.LastDiagnostic}");
        Equal(true, runtime.Planner.TryBuildRoute(onFoot, target, out var plan),
            $"and the approach route must still build: {runtime.Planner.LastDiagnostic}");
        Equal(0,
            plan.TrianglePath.Count(id => !WorldMapTerrainPassability.CanTraverse(
                0, map.WorldMapType, map.Triangles[id].TerrainId)),
            "and must still stay on ground the party can walk on");
    }

    private static WorldMapRuntimeContext CreateShippedRuntime(
        WorldMapData map, WorldMapTargetCatalog catalog) =>
        new(map,
            catalog,
            progressSink: null,
            distanceUnitsPerCount: 512,
            guidanceInterval: TimeSpan.FromSeconds(2),
            walkingFootstepInterval: TimeSpan.FromMilliseconds(500),
            chocoboFootstepInterval: TimeSpan.FromMilliseconds(300),
            entranceCueInnerRange: 80,
            entranceCueOuterRange: 400,
            entranceCueInterval: TimeSpan.FromSeconds(3));

    private static bool TryLoadInstalledWorld(out WorldMapData map, out WorldMapTargetCatalog catalog)
    {
        map = null!;
        catalog = null!;
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 0);
        catalog = WorldMapTargetCatalog.Load(
            map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return true;
    }

    private static WorldMapStateSnapshot State(
        int x, int y, int z, int terrain, int terrainScript, int region, int model) =>
        new(3, 0, 0, 566, x, y, z, 0, 0, terrain, region, model, 30, 2273,
            new FieldNavigationControlTransform(0))
        {
            TerrainScriptId = terrainScript
        };

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"World map vehicle terrain - {label}: expected {expected}, got {actual}.");
        }
    }
}
