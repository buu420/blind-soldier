using System.Buffers.Binary;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The surfaced submarine (model 13, map 0) driven by ordinary camera-relative commands,
/// checked against FUN_0074EA48's own surface movement. Camera mode 2 Left/Right turn the
/// camera and also move the boat sideways.
/// </summary>
internal static class WorldMapSubmarineSurfaceNavigationTests
{
    private const int Sea = 3;
    private const int Grass = 0;
    private const int Cell = 500;

    internal static void Run()
    {
        NativeFramesAreLiteral();
        SurfaceCommandsNeverHoldConfirm();
        IncoherentStateFailsClosed();
        TurningDriftIntoLandIsRefused();
        NativeSelectedSurfaceDecides();
        OtherEntrancesAreRefusedAndOwnEntranceIsExempt();
        CoastalDestinationIsApproachable();
        InstalledSurfaceReplays();
        Console.WriteLine("world-map surface submarine navigation tests passed.");
    }

    private static void NativeFramesAreLiteral()
    {
        // Independent arithmetic: submarine-navigation-20261006\surface-literals.py.
        Frame((-60, 2, 4064), 2, 2, 0, FieldNavigationInput.Left, "mode 2 Left turns 32 and moves 60 sideways");
        Frame((59, 2, 32), 2, 2, 0, FieldNavigationInput.Right, "mode 2 Right turns 32 and moves 60 sideways");
        Frame((60, 0, 1024), 2, 2, 1024, FieldNavigationInput.Up, "mode 2 Up moves along the camera without turning");
        Frame((-47, -44, 4080), 2, 2, 0, FieldNavigationInput.UpLeft, "Up halves the turn and makes a 3/4 diagonal");
        Frame((-47, 43, 16), 2, 2, 0, FieldNavigationInput.DownLeft, "Down swaps the turn keys: Left turns right");
        Frame((-30, -4, 84), 2, 1, 100, FieldNavigationInput.Left, "multiplier 1 halves turn and step");
        Frame((60, 0, 1000), 0, 2, 1000, FieldNavigationInput.Right, "mode 0 moves in world axes whatever the camera front");
        Frame((22, -23, 0), 1, 1, 0, FieldNavigationInput.UpRight, "mode 1 diagonal uses arithmetic shifts");
        Frame((0, 60, 2048), 1, 2, 2048, FieldNavigationInput.Down, "mode 1 ignores a restored camera front");
        Equal(15, WorldMapSubmarineSurfaceSteering.LeaseUpdates(2), "500 ms lease at the PC's multiplier 2");
        Equal(30, WorldMapSubmarineSurfaceSteering.LeaseUpdates(1), "multiplier 1 lease covers the same time");
        Equal(8, WorldMapSubmarineSurfaceSteering.LeaseUpdates(4), "multiplier 4 lease rounds up");

        static void Frame((int X, int Z, int Front) expected, int mode, int multiplier, int front,
            FieldNavigationInput input, string label)
        {
            Equal(true, WorldMapSubmarineSurfaceSteering.TryGetMask(input, out var mask), $"{label}: mask");
            var frame = WorldMapSubmarineSurfaceSteering.Step(mode, multiplier, front, mask);
            Equal(expected, (frame.DeltaX, frame.DeltaZ, frame.CameraFront), label);
        }
    }

    private static void SurfaceCommandsNeverHoldConfirm()
    {
        // On the surface, Confirm in camera mode 2 adds Up and drops Down (FUN_0074EA48), so a
        // surface command must be the directions alone.
        var resolver = new HighwayDirectionInputMappingResolver(new Mapping());
        foreach (var direction in new[]
                 {
                     HighwaySteeringDirection.Up, HighwaySteeringDirection.UpRight, HighwaySteeringDirection.Right,
                     HighwaySteeringDirection.DownRight, HighwaySteeringDirection.Down, HighwaySteeringDirection.DownLeft,
                     HighwaySteeringDirection.Left, HighwaySteeringDirection.UpLeft
                 })
        {
            Equal(true, resolver.TryResolveWorldSubmarine(direction, thrust: false, out var keys, out var diagnostic),
                $"{direction} resolves: {diagnostic}");
            Equal(false, keys.Any(key => key.ScanCode == Mapping.ConfirmScan), $"{direction} holds no Confirm");
        }

        Equal(false, WorldMapSubmarineSurfaceSteering.TryGetMask(FieldNavigationInput.Conflicting, out _),
            "conflicting input has no native mask");
    }

    private static void IncoherentStateFailsClosed()
    {
        var map = Grid((_, _) => Sea, out _);
        var planner = new WorldMapRoutePlanner(map);
        var state = Boat(11000, 12000, 2, 0);
        Equal(true, Safe(state, FieldNavigationInput.Up, map, planner), "fixture is open water");
        Equal(false, Safe(state with { PlayerModelId = 5 }, FieldNavigationInput.Up, map, planner), "not the submarine");
        Equal(false, Safe(state with { WorldMapType = 2 }, FieldNavigationInput.Up, map, planner), "underwater");
        Equal(false, Safe(state with { CurrentModule = 1 }, FieldNavigationInput.Up, map, planner), "not the world module");
        Equal(false, Safe(state with { NativeCameraMode = 3 }, FieldNavigationInput.Up, map, planner), "flight camera");
        Equal(false, Safe(state with { NativeCameraMode = -1, HasNativeControlMode = false }, FieldNavigationInput.Up, map, planner),
            "unreadable camera mode");
        Equal(false, Safe(state with { NativeFrameMultiplier = 0 }, FieldNavigationInput.Up, map, planner), "multiplier 0");
        Equal(false, Safe(state with { NativeFrameMultiplier = 5 }, FieldNavigationInput.Up, map, planner), "multiplier 5");
        Equal(false, Safe(state with { CameraFront = 4096 }, FieldNavigationInput.Up, map, planner), "camera front out of range");
        Equal(false, Safe(state, FieldNavigationInput.Conflicting, map, planner), "conflicting command");
        Equal(false, Safe(state with { X = 5000 }, FieldNavigationInput.Up, map, planner), "position off the native mesh");
    }

    private static void TurningDriftIntoLandIsRefused()
    {
        // Land from X 13000 east. The boat is 600 units west of it, facing north.
        var map = Grid((x, _) => x >= 13000 ? Grass : Sea, out _);
        var planner = new WorldMapRoutePlanner(map);
        var state = Boat(12400, 12000, 2, 0);
        Equal(true, Safe(state, FieldNavigationInput.Up, map, planner), "straight ahead is open water");
        Equal(true, planner.CanTraverseSegment(state, new(12400, 0, 11100)), "a turn-in-place model sees open water ahead");
        Equal(false, Safe(state, FieldNavigationInput.Right, map, planner, out var right),
            "camera mode 2 Right drifts east into the land while turning");
        Equal(true, right.Contains("leaves submarine water", StringComparison.Ordinal), $"drift diagnostic: {right}");
        Equal(true, Safe(state, FieldNavigationInput.Left, map, planner), "Left drifts west into open water");
        Equal(false, Safe(state with { NativeCameraMode = 0 }, FieldNavigationInput.Right, map, planner),
            "camera mode 0 Right moves 900 units east over the lease");
        Equal(true, Safe(state with { NativeCameraMode = 1 }, FieldNavigationInput.Up, map, planner), "camera mode 1 Up");

        Equal(false, Safe(state with { X = 12600 }, FieldNavigationInput.DownRight, map, planner),
            "Down+Right still moves east: Down swaps only the turn, not the sideways step");
    }

    private static void NativeSelectedSurfaceDecides()
    {
        // The boat rides 8 above the water. A deck 12 above the water over X 10000-11000,
        // Z 10000-11000 is nearer that height than the water is; an impassable basin 3000
        // below over X 9000-10000, Z 11000-12000 is the lowest surface there.
        var map = Grid((_, _) => Sea, out var triangles, extra: id =>
        [
            Overlap(id, 10000, 10000, 11000, 11000, 12, Grass),
            Overlap(id + 1, 10000, 10000, 11000, 11000, 12, Grass, second: true),
            Overlap(id + 2, 9000, 11000, 10000, 12000, -3000, Grass),
            Overlap(id + 3, 9000, 11000, 10000, 12000, -3000, Grass, second: true)
        ]);
        var planner = new WorldMapRoutePlanner(map);
        var underDeck = Boat(10500, 11800, 0, 0) with { Y = 8 };
        Equal(true, planner.CanTraverseSegment(underDeck, new(10500, 0, 10900)),
            "the water under the deck is connected in plan");
        Equal(false, Safe(underDeck, FieldNavigationInput.Up, map, planner, out var deck),
            "FUN_0074CC07 selects the deck nearest the boat's height");
        Equal(true, deck.Contains("non-submarine terrain", StringComparison.Ordinal), $"deck diagnostic: {deck}");

        var overBasin = Boat(9500, 12700, 0, 0) with { Y = 8 };
        Equal(true, WorldMapBroncoLanding.TryFindSurface(map, 9500, 11700, out var lowest) && lowest.TerrainId == Grass,
            "the lowest surface there is the impassable basin");
        Equal(true, Safe(overBasin, FieldNavigationInput.Up, map, planner),
            "map 0 selects the water nearest the boat, not the lowest surface");
        Equal(triangles + 4, map.Triangles.Count, "fixture holds the four overlapping faces");
    }

    private static void OtherEntrancesAreRefusedAndOwnEntranceIsExempt()
    {
        var map = Grid((_, _) => Sea, out _);
        var entrance = map.Triangles
            .Where(t => t.Centroid.X is >= 10000 and < 11000 && t.Centroid.Z is >= 9500 and < 10000)
            .Select(t => t.Id)
            .ToHashSet();
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = entrance };
        var state = Boat(10500, 10600, 0, 0);
        Equal(false, Safe(state, FieldNavigationInput.Up, map, planner, out var refused),
            "900 units north reaches another place's entrance");
        Equal(true, refused.Contains("entrance", StringComparison.Ordinal), $"entrance diagnostic: {refused}");
        Equal(true, WorldMapSubmarineSurfaceSteering.IsCommandSafe(state, FieldNavigationInput.Up, map, planner, entrance),
            "the selected destination's own entrance is exempt");
        Equal(true, Safe(state, FieldNavigationInput.Down, map, planner), "away from the entrance");
        Equal(true, Safe(Boat(10500, 9750, 0, 0), FieldNavigationInput.Down, map, planner),
            "a boat already in a trigger may leave it");
    }

    private static void CoastalDestinationIsApproachable()
    {
        // Land from X 13000. The destination is the water at X 12500-13000, beside it.
        var map = Grid((x, _) => x >= 13000 ? Grass : Sea, out _);
        var arrival = map.Triangles
            .Where(t => t.TerrainId == Sea && t.Centroid.X >= 12500 && t.Centroid.Z is >= 11500 and < 12500)
            .Select(t => t.Id)
            .ToHashSet();
        var planner = new WorldMapRoutePlanner(map);
        var state = Boat(12300, 12000, 0, 0);
        Equal(false, Safe(state, FieldNavigationInput.Right, map, planner),
            "without the destination, 900 units east meets the shore");
        Equal(true, WorldMapSubmarineSurfaceSteering.IsCommandSafe(state, FieldNavigationInput.Right, map, planner,
                new HashSet<int>(), out var held, arrival),
            "after reaching the destination native movement can deflect on submarine water");
        Equal(true, held.Contains("deflect along the shore", StringComparison.Ordinal), $"shore diagnostic: {held}");
        Equal(false, WorldMapSubmarineSurfaceSteering.IsCommandSafe(state with { Z = 10600 }, FieldNavigationInput.Right,
                map, planner, new HashSet<int>(), arrival),
            "a shore met before the destination is still refused");

        var entrance = map.Triangles
            .Where(t => t.TerrainId == Sea && t.Centroid.X >= 12800 && t.Centroid.Z is >= 11500 and < 12500)
            .Select(t => t.Id)
            .ToHashSet();
        var guarded = new WorldMapRoutePlanner(map) { EntranceTriangleIds = entrance };
        var nearer = arrival.Except(entrance).ToHashSet();
        Equal(false, WorldMapSubmarineSurfaceSteering.IsCommandSafe(state, FieldNavigationInput.Right, map, guarded,
                new HashSet<int>(), nearer),
            "another place's entrance past the destination is still refused");
    }

    private static void InstalledSurfaceReplays()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(sourceRoot))
        {
            Console.WriteLine("installed surface submarine replays skipped: data/source roots were not provided.");
            return;
        }

        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 2);
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        // Junon and Costa del Sol share an inner sea that terrain 6 and cliffs close off: mask
        // 0x4048008 has no terrain 6, so the surfaced submarine cannot sail out of it.
        // Wutai and Mideel are on the open ocean.
        var junonPort = Port(map, catalog, "Junon");
        var costaPort = Port(map, catalog, "Costa");
        var wutaiPort = Port(map, catalog, "Wutai");
        var mideelPort = Port(map, catalog, "Mideel");
        var cases = new[]
        {
            ("inner sea to Junon's coast", FarWater(map, catalog, junonPort, 15000), junonPort),
            ("Junon's coast to Costa del Sol's coast", junonPort, costaPort),
            ("open ocean to Wutai's coast", FarWater(map, catalog, wutaiPort, 20000), wutaiPort),
            ("open ocean to Mideel's coast", FarWater(map, catalog, mideelPort, 20000), mideelPort)
        };
        var outside = WaterTarget(map, wutaiPort);
        var inner = Place(Boat(junonPort.Centroid.X, junonPort.Centroid.Z, 0, 0), map);
        Equal(false, new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds }.CanReach(inner, outside),
            "the surfaced submarine cannot leave the Junon inner sea");
        foreach (var (label, start, destination) in cases)
        {
            foreach (var mode in new[] { 0, 1, 2 })
            {
                ForecastGatedReplay(map, catalog, label, start, destination, mode);
                NavigatorReplay(map, catalog, label, start, destination, mode);
            }
        }
    }

    /// <summary>
    /// Native motion along the planned route, each command chosen from the eight directions
    /// by the native one-update step and gated by the full-lease forecast. This checks the
    /// forecast against real coastline without the shared navigator's own choices.
    /// </summary>
    private static void ForecastGatedReplay(WorldMapData map, WorldMapTargetCatalog catalog, string label,
        WorldMapTriangle start, WorldMapTriangle destination, int mode)
    {
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var target = WaterTarget(map, destination);
        var state = Place(Boat(start.Centroid.X, start.Centroid.Z, mode, mode == 2 ? 1536 : 0) with { Y = start.Centroid.Y }, map);
        Equal(true, planner.TryBuildRoute(state, target, out var route), $"{label}: route ({planner.LastDiagnostic})");
        var waypoint = 0;
        var budget = (int)(route.TotalDistance / 150) + 400;
        var moved = 0;
        for (var tick = 0; tick < budget; tick++)
        {
            if (Distance(state, destination.Centroid) < 600)
            {
                Console.WriteLine($"surface forecast replay {label}, camera {mode}: arrived after {tick} ticks, {moved} native updates");
                return;
            }

            while (waypoint < route.Waypoints.Count - 1 &&
                   (Distance(state, route.Waypoints[waypoint]) < 480 ||
                    planner.CanTraverseSegment(state, route.Waypoints[waypoint + 1], null, target.NativeEntranceExemptions)))
            {
                waypoint++;
            }

            var aim = route.Waypoints[waypoint];
            var aimX = WorldMapTargetCatalog.WrappedDelta(state.X, aim.X, map.WrapWidth);
            var aimZ = WorldMapTargetCatalog.WrappedDelta(state.Z, aim.Z, map.WrapHeight);
            var input = Enum.GetValues<FieldNavigationInput>()
                .Where(candidate => candidate is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft)
                .Select(candidate =>
                {
                    WorldMapSubmarineSurfaceSteering.TryGetMask(candidate, out var mask);
                    var frame = WorldMapSubmarineSurfaceSteering.Step(mode, state.NativeFrameMultiplier, state.CameraFront, mask);
                    return (Input: candidate, Progress: frame.DeltaX * (double)aimX + frame.DeltaZ * (double)aimZ);
                })
                .Where(candidate => candidate.Progress > 0d)
                .OrderByDescending(candidate => candidate.Progress)
                .Select(candidate => candidate.Input)
                .FirstOrDefault(candidate => WorldMapSubmarineSurfaceSteering.IsCommandSafe(
                    state, candidate, map, planner, target.NativeEntranceExemptions, target.ArrivalTriangleIds));
            if (input == FieldNavigationInput.None)
            {
                throw new InvalidOperationException(
                    $"surface forecast replay {label}, camera {mode}: no safe forward command at {state.X},{state.Z} front {state.CameraFront}, waypoint {waypoint}/{route.Waypoints.Count}");
            }

            state = Advance(state, input, map, planner, target, $"{label}, camera {mode}", ref moved);
        }

        throw new InvalidOperationException(
            $"surface forecast replay {label}, camera {mode}: not arrived at {state.X},{state.Z} after {budget} ticks");
    }

    /// <summary>
    /// The shared navigator choosing the camera-relative command, and the host refusing any
    /// command whose lease forecast leaves submarine water, as the parent hook does.
    /// </summary>
    private static void NavigatorReplay(WorldMapData map, WorldMapTargetCatalog catalog, string label,
        WorldMapTriangle start, WorldMapTriangle destination, int mode)
    {
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var target = WaterTarget(map, destination);
        var navigator = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        var state = Place(Boat(start.Centroid.X, start.Centroid.Z, mode, mode == 2 ? 1536 : 0) with { Y = start.Centroid.Y }, map);
        var clock = new DateTime(2026, 10, 6, 18, 0, 0, DateTimeKind.Utc);
        var started = navigator.HandleAction(FieldNavigationAction.ToggleBeacon, state, clock);
        Equal(true, navigator.BeaconEnabled, $"{label}, camera {mode}: navigator starts ({started?.Speech}; {navigator.LastDiagnostic})");
        var budget = (int)(navigator.Probe.Route!.TotalDistance / 150) + 400;
        var moved = 0;
        var refused = 0;
        var idle = 0;
        var trace = new Queue<string>();
        for (var tick = 0; tick < budget; tick++)
        {
            if (trace.Count > 12) trace.Dequeue();
            clock = clock.AddMilliseconds(100);
            var output = navigator.Observe(state, clock, automaticWalkActive: true);
            if (!navigator.BeaconEnabled || output?.Speech?.Contains("Arrived", StringComparison.Ordinal) == true)
            {
                Equal(true, Distance(state, destination.Centroid) < 2000,
                    $"{label}, camera {mode}: navigation ended near the destination ({output?.Speech})");
                Console.WriteLine($"surface navigator replay {label}, camera {mode}: arrived after {tick} ticks, {moved} native updates, {refused} refused commands");
                return;
            }

            if (!navigator.TryResolveAutomaticInput(state, out var input))
            {
                trace.Enqueue($"{tick} {state.X},{state.Z} idle {navigator.LastDiagnostic}");
                Equal(true, ++idle < 50, $"{label}, camera {mode}: navigator idle at {state.X},{state.Z} ({navigator.LastDiagnostic})");
                continue;
            }

            Equal(false, navigator.AutomaticInputHoldsSubmarineThrust, $"{label}, camera {mode}: surface commands hold no Confirm");
            var safe = WorldMapSubmarineSurfaceSteering.IsCommandSafe(state, input, map, planner,
                target.NativeEntranceExemptions, out var why, target.ArrivalTriangleIds);
            planner.TryResolvePlayerTriangle(state, out var here);
            trace.Enqueue($"{tick} {state.X},{state.Z} triangle {here} arrival={target.ArrivalTriangleIds.Contains(here)} {input} {why}");
            if (!safe)
            {
                Equal(true, ++refused < 50, $"{label}, camera {mode}: navigator keeps choosing refused commands ({why})");
                continue;
            }

            idle = 0;
            state = Advance(state, input, map, planner, target, $"{label}, camera {mode}", ref moved);
        }

        throw new InvalidOperationException(
            $"surface navigator replay {label}, camera {mode}: not arrived at {state.X},{state.Z} after {budget} ticks ({navigator.LastDiagnostic}); {refused} refused; route end {navigator.Probe.Route?.Waypoints[^1]}; forecasts here: {DescribeForecasts(state, map, planner, target)}; last ticks: {string.Join(" | ", trace)}");
    }

    private static string DescribeForecasts(WorldMapStateSnapshot state, WorldMapData map, WorldMapRoutePlanner planner,
        WorldMapNavigationTarget target) =>
        string.Join("; ", Enum.GetValues<FieldNavigationInput>()
            .Where(input => input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft)
            .Select(input =>
            {
                WorldMapSubmarineSurfaceSteering.IsCommandSafe(state, input, map, planner,
                    target.NativeEntranceExemptions, out var why, target.ArrivalTriangleIds);
                return why;
            }));

    /// <summary>One 100 ms host tick: three native updates at multiplier 2, each one the game accepts.</summary>
    private static WorldMapStateSnapshot Advance(WorldMapStateSnapshot state, FieldNavigationInput input,
        WorldMapData map, WorldMapRoutePlanner planner, WorldMapNavigationTarget target, string label, ref int moved)
    {
        WorldMapSubmarineSurfaceSteering.TryGetMask(input, out var mask);
        for (var update = 0; update < 3; update++)
        {
            var frame = WorldMapSubmarineSurfaceSteering.Step(state.NativeCameraMode, state.NativeFrameMultiplier, state.CameraFront, mask);
            var x = Wrap(state.X + frame.DeltaX, map.WrapWidth);
            var z = Wrap(state.Z + frame.DeltaZ, map.WrapHeight);
            Equal(true, planner.CanTraverseSegment(state, new(x, state.Y, z), null, target.NativeEntranceExemptions),
                $"{label}: native update {input} from {state.X},{state.Z} stays on submarine water");
            state = Place(state with { X = x, Z = z, CameraFront = frame.CameraFront }, map);
            moved++;
        }

        return state;
    }

    private static WorldMapStateSnapshot Place(WorldMapStateSnapshot state, WorldMapData map)
    {
        Equal(true, WorldMapBroncoLanding.TryFindSurfaceNear(map, state.X, state.Z, state.Y, out var surface),
            $"native surface at {state.X},{state.Z}");
        Equal(true, WorldMapTerrainPassability.CanTraverse(13, 0, surface.TerrainId),
            $"selected surface at {state.X},{state.Z} is submarine terrain");
        return state with
        {
            TerrainId = surface.TerrainId,
            TerrainScriptId = surface.TerrainScriptId,
            RegionId = surface.RegionId & 0x1F,
            ControlTransform = new FieldNavigationControlTransform(SignedControl(state.CameraFront))
        };
    }

    private static WorldMapNavigationTarget WaterTarget(WorldMapData map, WorldMapTriangle destination)
    {
        var arrivals = map.Triangles
            .Where(t => WorldMapTerrainPassability.CanTraverse(13, 0, t.TerrainId) &&
                        Math.Abs(t.Centroid.X - destination.Centroid.X) + Math.Abs(t.Centroid.Z - destination.Centroid.Z) < 600)
            .Select(t => t.Id)
            .ToHashSet();
        arrivals.Add(destination.Id);
        return new WorldMapNavigationTarget(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location,
            "Surface test water", destination.Centroid.X, destination.Centroid.Y, destination.Centroid.Z,
            destination.Id, destination.RegionId & 0x1F, $"surface-water:{destination.Id}", arrivals);
    }

    /// <summary>Sea-level water nearest a place: Junon's own streets hold a one-face pool at 784.</summary>
    private static WorldMapTriangle Port(WorldMapData map, WorldMapTargetCatalog catalog, string label)
    {
        var place = catalog.Locations.First(t => t.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase));
        return map.Triangles
            .Where(t => t.TerrainId == Sea && Math.Abs(t.Centroid.Y) < 100)
            .OrderBy(t => Math.Abs(WorldMapTargetCatalog.WrappedDelta(place.X, t.Centroid.X, map.WrapWidth)) +
                          Math.Abs(WorldMapTargetCatalog.WrappedDelta(place.Z, t.Centroid.Z, map.WrapHeight)))
            .First();
    }

    /// <summary>The submarine water nearest the given straight-line distance from a port that routes to it.</summary>
    private static WorldMapTriangle FarWater(WorldMapData map, WorldMapTargetCatalog catalog, WorldMapTriangle port, int distance)
    {
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var target = WaterTarget(map, port);
        foreach (var candidate in map.Triangles
                     .Where(t => t.TerrainId == Sea && Math.Abs(t.Centroid.Y) < 100)
                     .OrderBy(t => Math.Abs(Distance(Boat(port.Centroid.X, port.Centroid.Z, 0, 0), t.Centroid) - distance))
                     .Take(2000))
        {
            var state = Place(Boat(candidate.Centroid.X, candidate.Centroid.Z, 0, 0) with { Y = candidate.Centroid.Y }, map);
            if (planner.CanReach(state, target) && planner.TryBuildRoute(state, target, out _))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"no routed submarine water {distance} units from {port.Centroid}");
    }

    private static WorldMapStateSnapshot Boat(int x, int z, int mode, int front) =>
        new(3, 0, 2, 1396, x, 0, z, 0, 0, Sea, 0, 13, 0, front, new FieldNavigationControlTransform(SignedControl(front)))
        {
            HasNativeControlMode = true,
            NativeCameraMode = mode,
            NativeFrameMultiplier = 2
        };

    private static int SignedControl(int front)
    {
        var direction = -((front % 4096 + 4096) % 4096 / 16);
        return direction < -128 ? direction + 256 : direction;
    }

    private static bool Safe(WorldMapStateSnapshot state, FieldNavigationInput input, WorldMapData map, WorldMapRoutePlanner planner) =>
        Safe(state, input, map, planner, out _);

    private static bool Safe(WorldMapStateSnapshot state, FieldNavigationInput input, WorldMapData map,
        WorldMapRoutePlanner planner, out string diagnostic) =>
        WorldMapSubmarineSurfaceSteering.IsCommandSafe(state, input, map, planner, new HashSet<int>(), out diagnostic);

    /// <summary>
    /// A flat map-0 fixture of 500-unit squares over X and Z 8500-16000 (all in mesh 1,1),
    /// two triangles each, with shared-edge neighbours.
    /// </summary>
    private static WorldMapData Grid(Func<int, int, int> terrainAt, out int count,
        Func<int, IReadOnlyList<WorldMapTriangle>>? extra = null)
    {
        var faces = new List<(WorldMapVertex A, WorldMapVertex B, WorldMapVertex C, int Terrain)>();
        for (var x = 8500; x < 16000; x += Cell)
        for (var z = 8500; z < 16000; z += Cell)
        {
            var terrain = terrainAt(x, z);
            faces.Add((new(x, 0, z), new(x + Cell, 0, z), new(x, 0, z + Cell), terrain));
            faces.Add((new(x + Cell, 0, z), new(x + Cell, 0, z + Cell), new(x, 0, z + Cell), terrain));
        }

        var edges = new Dictionary<(int, int, int, int), List<int>>();
        for (var i = 0; i < faces.Count; i++)
        {
            var (a, b, c, _) = faces[i];
            foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
            {
                var key = p.X < q.X || (p.X == q.X && p.Z < q.Z) ? (p.X, p.Z, q.X, q.Z) : (q.X, q.Z, p.X, p.Z);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = [];
                list.Add(i);
            }
        }

        var neighbors = Enumerable.Range(0, faces.Count).Select(_ => new List<int>()).ToArray();
        foreach (var list in edges.Values.Where(list => list.Count == 2))
        {
            neighbors[list[0]].Add(list[1]);
            neighbors[list[1]].Add(list[0]);
        }

        var triangles = faces
            .Select((f, i) => new WorldMapTriangle(i, 0, 1, 1, 0, i, f.A, f.B, f.C, f.Terrain, 0, 0, 0, false, neighbors[i]))
            .ToList();
        count = triangles.Count;
        if (extra is not null) triangles.AddRange(extra(triangles.Count));
        return new WorldMapData(0, 2, 9, 7, 63, triangles, [], "surface submarine fixture");
    }

    /// <summary>One half of a flat square with no neighbours, overlapping the grid at another height.</summary>
    private static WorldMapTriangle Overlap(int id, int x0, int z0, int x1, int z1, int y, int terrain, bool second = false)
    {
        var a = new WorldMapVertex(x0, y, z0);
        var b = new WorldMapVertex(x1, y, z0);
        var c = new WorldMapVertex(x0, y, z1);
        var d = new WorldMapVertex(x1, y, z1);
        return second
            ? new WorldMapTriangle(id, 0, 1, 1, 0, id, a, b, d, terrain, 0, 0, 0, false, [])
            : new WorldMapTriangle(id, 0, 1, 1, 0, id, a, d, c, terrain, 0, 0, 0, false, []);
    }

    private static double Distance(WorldMapStateSnapshot state, WorldMapVertex point) =>
        Math.Sqrt(Math.Pow(state.X - (double)point.X, 2) + Math.Pow(state.Z - (double)point.Z, 2));

    private static double Distance(WorldMapStateSnapshot state, WorldMapRouteWaypoint point) =>
        Math.Sqrt(Math.Pow(state.X - (double)point.X, 2) + Math.Pow(state.Z - (double)point.Z, 2));

    private static int Wrap(int value, int extent) => ((value % extent) + extent) % extent;

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"World map surface submarine - {label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class Mapping : ILegacyAddressSpace
    {
        internal const int ConfirmScan = 0x2D;
        private readonly byte[] table = new byte[HighwayDirectionInputMappingResolver.MappingTableSize];

        internal Mapping()
        {
            Write(5, ConfirmScan);
            Write(12, 0x48);
            Write(13, 0x4D);
            Write(14, 0x50);
            Write(15, 0x4B);
        }

        private void Write(int slot, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(slot * 4, 4), value);

        public bool TryRead(uint address, Span<byte> destination)
        {
            if (address != HighwayDirectionInputMappingResolver.MappingTableAddress || destination.Length != table.Length)
            {
                return false;
            }

            table.CopyTo(destination);
            return true;
        }
    }
}
