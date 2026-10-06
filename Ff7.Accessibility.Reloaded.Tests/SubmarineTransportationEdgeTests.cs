using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Edges of the parked submarine in Transportation, on the installed world maps.
/// <list type="bullet">
/// <item>wm0 model 13 fn 4 enters the vehicle only for a party on foot (player model 0, 1 or 2).</item>
/// <item>FUN_00762A21 decides contact from the measured masks, and it skips an entity flagged 0x80.</item>
/// <item>The submarine is left only on terrain 18, its docks.</item>
/// </list>
/// The Highwind trip to each dock is covered by <see cref="SubmarineTransportationTests"/>.
/// </summary>
internal static class SubmarineTransportationEdgeTests
{
    private const uint BoatPointer = 0x00E3A1C8;
    private const uint OtherPointer = 0x00E3A288;
    private const byte HiddenFlag = 0x08;

    internal static void Run()
    {
        if (!TryLoad(0, out var map, out var catalog))
        {
            Console.WriteLine("submarine transportation edges: installed map cases not run (data roots unset)");
            return;
        }

        var docks = Docks(map);
        Equal(true, docks.Length >= 2, "installed wm0 supplies the submarine docks");
        foreach (var dock in docks)
        {
            OnFootTheRouteEndsInNativeContact(map, catalog, dock);
        }

        WitnessedContactIsArrivalAndAStaleOneIsNot(map, catalog, docks);
        FlagsAndRidingDecideWhatIsOffered(map, catalog, docks[0]);
        foreach (var dock in docks)
        {
            AnotherVehicleCannotArriveAtTheSubmarine(map, catalog, dock);
        }

        ADisappearedSubmarineStopsTheRoute(map, catalog, docks[0]);
        OpenWaterIsTruthfullyUnavailable(map, catalog);
        if (TryLoad(2, out var underwater, out var underwaterCatalog))
        {
            UnderwaterTransportationOffersNoSubmarine(underwater, underwaterCatalog);
        }

        Console.WriteLine("submarine transportation edge tests passed with installed game data.");
    }

    /// <summary>A party on foot at each real dock: arrival is the native contact, on walking ground.</summary>
    private static void OnFootTheRouteEndsInNativeContact(WorldMapData map, WorldMapTargetCatalog catalog, WorldMapTriangle dock)
    {
        var boat = Boat(dock);
        var planner = Planner(map, catalog);
        var probe = catalog.ReadTargets(WorldMapNavigationCategory.Transportation, Walker(map, dock.Centroid.X, dock.Centroid.Z, planner), [boat])
            .Single(t => t.Label == "Submarine");
        Equal(true, probe.NativeVehicleContact is not null && probe.VehicleContactPoints.Count > 0,
            $"dock {dock.Id}: the party on foot has a native boarding contact");
        foreach (var (triangle, point) in probe.VehicleContactPoints)
        {
            Equal(true, WorldMapTerrainPassability.CanTraverse(0, 0, map.Triangles[triangle].TerrainId),
                $"dock {dock.Id}: contact triangle {triangle} is walking ground");
            Equal(false, catalog.EntranceTriangleIds.Contains(triangle), $"dock {dock.Id}: contact {triangle} is no field entrance");
            Equal(true, WorldMapVehicleObstacles.Blocks(0, point.X, point.Z, 13, boat.X, boat.Z, map.WrapWidth, map.WrapHeight),
                $"dock {dock.Id}: contact point {point.X},{point.Z} is inside the measured submarine mask");
        }

        // A start on foot a few thousand units away on the same walking component.
        var component = planner.GetComponentId(0, 0, probe.ArrivalTriangleIds.First());
        var start = map.Triangles
            .Where(t => WorldMapTerrainPassability.CanTraverse(0, 0, t.TerrainId) &&
                        !catalog.EntranceTriangleIds.Contains(t.Id) &&
                        planner.GetComponentId(0, 0, t.Id) == component)
            .OrderBy(t => Math.Abs(Manhattan(map, t.Centroid.X, t.Centroid.Z, boat.X, boat.Z) - 4000))
            .First();
        var walker = Walker(map, start.Centroid.X, start.Centroid.Z, planner);
        var target = catalog.ReadTargets(WorldMapNavigationCategory.Transportation, walker, [boat]).Single(t => t.Label == "Submarine");
        Equal(false, target.HasArrived(walker, start.Id), $"dock {dock.Id}: a distant party has not arrived");
        Equal(true, planner.TryBuildRoute(walker, target, out var route), $"dock {dock.Id}: walking route ({planner.LastDiagnostic})");
        Equal(true, route.TrianglePath.All(id => WorldMapTerrainPassability.CanTraverse(0, 0, map.Triangles[id].TerrainId)),
            $"dock {dock.Id}: the walk stays on walking terrain");
        Equal(false, route.TrianglePath.Any(catalog.EntranceTriangleIds.Contains), $"dock {dock.Id}: the walk enters no field");
        var end = route.Waypoints[^1];
        Equal(true, WorldMapVehicleObstacles.Blocks(0, end.X, end.Z, 13, boat.X, boat.Z, map.WrapWidth, map.WrapHeight),
            $"dock {dock.Id}: the walk ends inside native boarding contact");
        var there = Walker(map, end.X, end.Z, planner);
        Equal(true, planner.TryResolvePlayerTriangle(there, out var endTriangle) && target.HasArrived(there, endTriangle),
            $"dock {dock.Id}: standing in contact is arrival");
    }

    /// <summary>
    /// Outside the mask, only a contact the game recorded against this boat, within the
    /// native reach, counts. Contact is a party-on-foot condition: the Highwind standing
    /// in the mask cannot board.
    /// </summary>
    private static void WitnessedContactIsArrivalAndAStaleOneIsNot(WorldMapData map, WorldMapTargetCatalog catalog, IReadOnlyList<WorldMapTriangle> docks)
    {
        var planner = Planner(map, catalog);
        var witnessed = false;
        foreach (var dock in docks)
        {
            var boat = Boat(dock);
            var target = catalog.ReadTargets(WorldMapNavigationCategory.Transportation,
                Walker(map, dock.Centroid.X, dock.Centroid.Z, planner), [boat]).Single(t => t.Label == "Submarine");
            var contact = target.VehicleContactPoints.First();
            FlyingThroughTheContactIsNotArrival(map, catalog, boat, contact.Value, dock.Id);

            foreach (var triangleId in target.ArrivalTriangleIds)
            {
                if (!TryFindOutsideMask(map, map.Triangles[triangleId], boat, out var x, out var z)) continue;
                var outside = Walker(map, x, z, planner);
                if (!planner.TryResolvePlayerTriangle(outside, out var outsideTriangle) || outsideTriangle != triangleId) continue;
                Equal(false, target.HasArrived(outside, outsideTriangle), $"dock {dock.Id}: outside the mask, unwitnessed");
                Equal(true, target.HasArrived(outside with { NativeContactEntityPointer = BoatPointer }, outsideTriangle),
                    $"dock {dock.Id}: the game's recorded contact with this boat is arrival");
                Equal(false, target.HasArrived(outside with { NativeContactEntityPointer = OtherPointer }, outsideTriangle),
                    $"dock {dock.Id}: a recorded contact with another entity is not");
                witnessed = true;
                break;
            }

            var far = map.Triangles
                .Where(t => target.ArrivalTriangleIds.Contains(t.Id))
                .SelectMany(t => new[] { t.Vertex0, t.Vertex1, t.Vertex2 })
                .Where(v => Math.Abs(WorldMapTargetCatalog.WrappedDelta(v.X, boat.X, map.WrapWidth)) >= WorldMapVehicleObstacles.NativeReach ||
                            Math.Abs(WorldMapTargetCatalog.WrappedDelta(v.Z, boat.Z, map.WrapHeight)) >= WorldMapVehicleObstacles.NativeReach)
                .Take(1).ToArray();
            foreach (var vertex in far)
            {
                var stale = Walker(map, vertex.X, vertex.Z, planner) with { NativeContactEntityPointer = BoatPointer };
                if (planner.TryResolvePlayerTriangle(stale, out var staleTriangle))
                {
                    Equal(false, target.HasArrived(stale, staleTriangle),
                        $"dock {dock.Id}: a recorded contact beyond the native 1024 reach is stale");
                }
            }
        }

        Equal(true, witnessed, "at least one installed dock has contact ground outside the mask to test the witness");
    }

    /// <summary>
    /// The Highwind can hover over the shore where the party would touch the boat, even
    /// with a stale contact slot naming it. Only the party on foot boards (wm0 4D04), so
    /// the selected submarine is not arrived at in flight; the landing is still asked for.
    /// </summary>
    private static void FlyingThroughTheContactIsNotArrival(WorldMapData map, WorldMapTargetCatalog catalog,
        WorldMapEntitySnapshot boat, WorldMapVertex contact, int dockId)
    {
        var planner = Planner(map, catalog);
        var flying = Walker(map, contact.X, contact.Z, planner) with
        {
            PlayerModelId = 3, Y = 3977, MovementSpeed = 120, HasModelRotation = true, ModelRotation = 1184
        };
        WorldMapEntitySnapshot[] entities = [boat];
        var controller = Controller(map, catalog, () => entities);
        var away = flying with { X = flying.X + 6000 };
        SelectTransportation(controller, away);
        var started = controller.HandleAction(FieldNavigationAction.ToggleBeacon, away)?.Speech ?? "";
        var target = Submarines(catalog, away, boat).Single();
        var components = WorldMapBroncoLanding.DestinationFootComponents(planner, map, target);
        if (!map.Triangles.Any(t => t.TerrainId == 0 && components.Contains(planner.GetComponentId(0, 0, t.Id))))
        {
            Equal(false, controller.BeaconEnabled, "isolated dock has no Highwind landing");
            Equal(true, started.Contains("Route unavailable", StringComparison.Ordinal) &&
                !started.Contains("Arrived", StringComparison.Ordinal), "isolated dock is refused truthfully");
            return;
        }
        Equal(true, controller.BeaconEnabled, $"dock {dockId}: the Highwind trip to the submarine starts");
        var now = new DateTime(2026, 10, 6, 21, 0, 0, DateTimeKind.Utc);
        foreach (var hovering in new[] { flying, flying with { NativeContactEntityPointer = BoatPointer } })
        {
            var spoken = controller.Observe(hovering, now, automaticWalkActive: true)?.Speech ?? string.Empty;
            Equal(false, spoken.Contains("Arrived", StringComparison.Ordinal),
                $"dock {dockId}: hovering over the boarding shore is not arriving ({spoken})");
            Equal(true, controller.BeaconEnabled, $"dock {dockId}: the route to the submarine stays on in flight");
            now = now.AddMilliseconds(100);
        }
    }

    /// <summary>
    /// Only the party on foot boards. Aboard the Tiny Bronco or the Buggy the submarine
    /// must not be arrived at: reaching its side in another vehicle does not board it.
    /// </summary>
    private static void AnotherVehicleCannotArriveAtTheSubmarine(WorldMapData map, WorldMapTargetCatalog catalog, WorldMapTriangle dock)
    {
        var planner = Planner(map, catalog);
        var boat = Boat(dock);
        foreach (var vehicle in new[] { 5, 6 })
        {
            var riding = Walker(map, dock.Centroid.X, dock.Centroid.Z, planner) with { PlayerModelId = vehicle };
            foreach (var target in Submarines(catalog, riding, boat))
            {
                foreach (var (triangle, point) in target.VehicleContactPoints)
                {
                    var there = riding with { X = point.X, Z = point.Z };
                    Equal(false, target.HasArrived(there, triangle),
                        $"model {vehicle} at {point.X},{point.Z} has not boarded the submarine");
                    Equal(false, target.HasArrived(there with { NativeContactEntityPointer = BoatPointer }, triangle),
                        $"model {vehicle} touching the submarine has not boarded it");
                }
            }
        }
    }

    private static void FlagsAndRidingDecideWhatIsOffered(WorldMapData map, WorldMapTargetCatalog catalog, WorldMapTriangle dock)
    {
        var planner = Planner(map, catalog);
        var walker = Walker(map, dock.Centroid.X, dock.Centroid.Z, planner);
        var flying = walker with { PlayerModelId = 3, Y = 3977 };
        var boat = Boat(dock);
        foreach (var observer in new[] { walker, flying })
        {
            var who = observer.PlayerModelId == 3 ? "from the Highwind" : "on foot";
            Equal(0, Submarines(catalog, observer, boat with { Flags = HiddenFlag }).Length,
                $"{who}: a hidden (0x08) submarine is not drawn and is not announced");
            var skipped = Submarines(catalog, observer, boat with { Flags = WorldMapVehicleObstacles.SkippedFlag });
            Equal(true, skipped.Length == 1 && skipped[0].ArrivalTriangleIds.Count == 0 && skipped[0].NativeVehicleContact is null,
                $"{who}: a submarine the native collision skips (0x80) is listed without a boarding approach");
            Equal(0, Submarines(catalog, observer, boat with { IsPlayer = true }).Length,
                $"{who}: the submarine being ridden is not a parked one");
        }

        WorldMapEntitySnapshot[] entities = [boat with { Flags = WorldMapVehicleObstacles.SkippedFlag }];
        var controller = Controller(map, catalog, () => entities);
        SelectTransportation(controller, walker);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, walker);
        Equal(false, controller.BeaconEnabled, "a skipped submarine cannot start a route that ends nowhere");
    }

    private static void ADisappearedSubmarineStopsTheRoute(WorldMapData map, WorldMapTargetCatalog catalog, WorldMapTriangle dock)
    {
        var planner = Planner(map, catalog);
        var boat = Boat(dock);
        var probe = Submarines(catalog, Walker(map, dock.Centroid.X, dock.Centroid.Z, planner), boat).Single();
        var contact = probe.VehicleContactPoints.First().Value;
        var component = planner.GetComponentId(0, 0, probe.ArrivalTriangleIds.First());
        var start = map.Triangles
            .Where(t => WorldMapTerrainPassability.CanTraverse(0, 0, t.TerrainId) && !catalog.EntranceTriangleIds.Contains(t.Id) &&
                        planner.GetComponentId(0, 0, t.Id) == component)
            .OrderBy(t => Math.Abs(Manhattan(map, t.Centroid.X, t.Centroid.Z, contact.X, contact.Z) - 2500))
            .First();
        var walker = Walker(map, start.Centroid.X, start.Centroid.Z, planner);
        WorldMapEntitySnapshot[] entities = [boat];
        var controller = Controller(map, catalog, () => entities);
        SelectTransportation(controller, walker);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, walker);
        Equal(true, controller.BeaconEnabled, "the walk to the parked submarine starts");
        entities = [];
        var output = controller.Observe(walker, new DateTime(2026, 10, 6, 20, 0, 0, DateTimeKind.Utc), automaticWalkActive: true);
        Equal(false, controller.BeaconEnabled, "a submarine that left the native list is not walked to");
        Equal(true, output?.StopAutoWalk == true && (output?.Speech ?? string.Empty).Contains("Submarine", StringComparison.Ordinal),
            $"the stop names the submarine: {output?.Speech}");
        Equal(false, controller.TryResolveAutomaticInput(walker, out _), "and no input is produced");
    }

    /// <summary>
    /// A submarine out on open water has no ground in contact. It stays reported, but
    /// neither a walk nor a Highwind landing is invented for it.
    /// </summary>
    private static void OpenWaterIsTruthfullyUnavailable(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var planner = Planner(map, catalog);
        var sea = map.Triangles
            .Where(t => t.TerrainId == 3 && Math.Abs(t.Centroid.Y) < 100 &&
                        WorldMapVehicleShoreApproach.FindContactPoints(map, 0, 13, t.Centroid.X, t.Centroid.Z).Count == 0)
            .First();
        var boat = Boat(sea) with { TerrainId = sea.TerrainId };
        var land = map.Triangles.First(t => t.TerrainId == 0 && !catalog.EntranceTriangleIds.Contains(t.Id));
        var walker = Walker(map, land.Centroid.X, land.Centroid.Z, planner);
        var flying = walker with { PlayerModelId = 3, Y = 3977, HasModelRotation = true, ModelRotation = 1184 };
        foreach (var observer in new[] { walker, flying })
        {
            var who = observer.PlayerModelId == 3 ? "from the Highwind" : "on foot";
            var listed = Submarines(catalog, observer, boat);
            Equal(true, listed.Length == 1 && listed[0].ArrivalTriangleIds.Count == 0 && listed[0].NativeVehicleContact is null,
                $"{who}: the open-water submarine is reported without an invented approach");
            WorldMapEntitySnapshot[] entities = [boat];
            var controller = Controller(map, catalog, () => entities);
            SelectTransportation(controller, observer);
            var spoken = controller.HandleAction(FieldNavigationAction.ToggleBeacon, observer)?.Speech;
            Equal(false, controller.BeaconEnabled, $"{who}: no route or landing is promised ({spoken})");
        }
    }

    private static void UnderwaterTransportationOffersNoSubmarine(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var floor = map.Triangles.First(t => WorldMapTerrainPassability.CanTraverse(13, 2, t.TerrainId));
        var diving = new WorldMapStateSnapshot(3, 2, 2, 1396, floor.Centroid.X, -3000, floor.Centroid.Z, 0, 0,
            floor.TerrainId, floor.RegionId & 31, 13, 0, 0, new FieldNavigationControlTransform(0))
        { TerrainScriptId = floor.TerrainScriptId };
        var other = new WorldMapEntitySnapshot(BoatPointer, 0, false, floor.Centroid.X + 2000, -3000, floor.Centroid.Z,
            floor.TerrainId, floor.RegionId & 31, 13, 0);
        var own = other with { GuestPointer = OtherPointer, IsPlayer = true, X = floor.Centroid.X };
        Equal(0, catalog.ReadTargets(WorldMapNavigationCategory.Transportation, diving, [own, other])
                .Count(t => t.Label == "Submarine"),
            "underwater, Transportation offers no submarine: it can only be left at a surface dock");
    }

    private static WorldMapNavigationTarget[] Submarines(WorldMapTargetCatalog catalog, WorldMapStateSnapshot observer, WorldMapEntitySnapshot boat) =>
        catalog.ReadTargets(WorldMapNavigationCategory.Transportation, observer, [boat]).Where(t => t.Label == "Submarine").ToArray();

    private static WorldMapNavigationController Controller(WorldMapData map, WorldMapTargetCatalog catalog,
        Func<IReadOnlyList<WorldMapEntitySnapshot>> entities) =>
        new(map, Planner(map, catalog), (state, category) => catalog.ReadTargets(category, state, entities()),
            entityProvider: entities);

    private static void SelectTransportation(WorldMapNavigationController controller, WorldMapStateSnapshot state)
    {
        for (var step = 0; step < 16 && controller.CurrentCategory != WorldMapNavigationCategory.Transportation; step++)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, state);
        }

        Equal(WorldMapNavigationCategory.Transportation, controller.CurrentCategory, "Transportation is selectable");
    }

    private static WorldMapRoutePlanner Planner(WorldMapData map, WorldMapTargetCatalog catalog) =>
        new(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };

    private static WorldMapTriangle[] Docks(WorldMapData map) =>
        map.Triangles.Where(t => t.TerrainId == 18).GroupBy(t => (t.MeshX, t.MeshZ)).Select(g => g.First()).ToArray();

    private static WorldMapEntitySnapshot Boat(WorldMapTriangle surface) =>
        new(BoatPointer, 0, false, surface.Centroid.X, surface.Centroid.Y, surface.Centroid.Z,
            surface.TerrainId, surface.RegionId & 31, 13, 0);

    /// <summary>The party on foot on the walking ground at or nearest this point.</summary>
    private static WorldMapStateSnapshot Walker(WorldMapData map, int x, int z, WorldMapRoutePlanner planner)
    {
        if (!WorldMapBroncoLanding.TryFindSurfaceNear(map, x, z, 0, out var ground))
        {
            throw new InvalidOperationException($"no installed surface at {x},{z}");
        }

        return new WorldMapStateSnapshot(3, 0, 2, 1620, x, (int)WorldMapBroncoLanding.SurfaceHeight(ground, x, z), z,
            1232, 1232, ground.TerrainId, ground.RegionId & 31, 0, 30, 0, new FieldNavigationControlTransform(0))
        { TerrainScriptId = ground.TerrainScriptId };
    }

    /// <summary>A point of this triangle within the native reach of the boat but outside its mask.</summary>
    private static bool TryFindOutsideMask(WorldMapData map, WorldMapTriangle triangle, WorldMapEntitySnapshot boat, out int x, out int z)
    {
        for (var a = 1; a < 8; a++)
        for (var b = 1; a + b < 8; b++)
        {
            var c = 8 - a - b;
            x = (triangle.Vertex0.X * a + triangle.Vertex1.X * b + triangle.Vertex2.X * c) / 8;
            z = (triangle.Vertex0.Z * a + triangle.Vertex1.Z * b + triangle.Vertex2.Z * c) / 8;
            var dx = Math.Abs(WorldMapTargetCatalog.WrappedDelta(x, boat.X, map.WrapWidth));
            var dz = Math.Abs(WorldMapTargetCatalog.WrappedDelta(z, boat.Z, map.WrapHeight));
            if (dx < WorldMapVehicleObstacles.NativeReach && dz < WorldMapVehicleObstacles.NativeReach &&
                !WorldMapVehicleObstacles.Blocks(0, x, z, 13, boat.X, boat.Z, map.WrapWidth, map.WrapHeight))
            {
                return true;
            }
        }

        x = z = 0;
        return false;
    }

    private static int Manhattan(WorldMapData map, int x, int z, int toX, int toZ) =>
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(x, toX, map.WrapWidth)) +
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(z, toZ, map.WrapHeight));

    private static bool TryLoad(int mapType, out WorldMapData map, out WorldMapTargetCatalog catalog)
    {
        map = null!;
        catalog = null!;
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", $"wm{mapType}.map"), mapType, mapType == 0 ? 2 : 0);
        catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return true;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Submarine transportation edge - {label}: expected {expected}, got {actual}.");
        }
    }
}
