using System.Buffers.Binary;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapSubmarineNavigationTests
{
    internal static void Run()
    {
        for (var terrain = 0; terrain < 32; terrain++)
        {
            var permitted = ((0x04048008u >> terrain) & 1) != 0;
            Equal(permitted, WorldMapTerrainPassability.CanTraverse(13, 0, terrain), "surface submarine native mask");
            Equal(permitted, WorldMapTerrainPassability.CanTraverse(13, 2, terrain), "underwater submarine native mask");
            Equal(false, WorldMapTerrainPassability.CanTraverse(28, 2, terrain), "red wreck is not a player vehicle");
        }
        ThrustAndDepthUseWorldControlsAndReleaseOnEveryBoundary();
        MappingChangesAndConflictingActionsFailClosed();
        SteeringTurnsBeforeThrustAndRecoversSafeDepth();
        SharedNavigatorDrivesTheSubWithThrustAndKeepsRecoveryOwned();
        DeepApproachGuardCoversTheWholeWrappedSegment();
        InstalledUnderwaterDestinationsUseNativeArrivalAndHideUnseenEmerald();
        Console.WriteLine("world-map submarine navigation tests passed.");
    }

    private static void ThrustAndDepthUseWorldControlsAndReleaseOnEveryBoundary()
    {
        var memory = new Mapping();
        var sink = new Sink();
        using var input = new NavigationAutoWalkController(sink, new HighwayDirectionInputMappingResolver(memory));
        Equal(true, input.TryStart(NavigationAutoWalkDomain.WorldMap, true), "start native world steering");
        Equal(true, input.Drive(FieldNavigationInput.None, true, true,
            worldSubmarine: true, holdSubmarineThrust: true).Success, "pure thrust is valid without directions");
        Equal(true, sink.Held.SetEquals([0x2D]), "Confirm slot5 alone supplies world thrust");
        Equal(true, input.Drive(FieldNavigationInput.Right, true, true,
            worldSubmarine: true).Success, "yaw without thrust");
        Equal(true, sink.Held.SetEquals([0x4D]), "turning releases thrust");
        input.Drive(FieldNavigationInput.Down, true, true, worldSubmarine: true);
        Equal(true, sink.Held.SetEquals([0x50]), "depth recovery uses native Down without thrust");
        input.Drive(FieldNavigationInput.None, false, true, worldSubmarine: true, holdSubmarineThrust: true);
        Equal(0, sink.Held.Count, "ownership loss releases pure thrust");
        input.Drive(FieldNavigationInput.None, true, true, worldSubmarine: true, holdSubmarineThrust: true);
        input.Suspend();
        Equal(0, sink.Held.Count, "pause/focus/read suspension releases thrust");
        input.Drive(FieldNavigationInput.None, true, false, worldSubmarine: true, holdSubmarineThrust: true);
        Equal(false, input.Enabled, "route completion stops input owner");
        Equal(0, sink.Held.Count, "route completion releases all world inputs");
    }

    private static void MappingChangesAndConflictingActionsFailClosed()
    {
        var memory = new Mapping();
        var sink = new Sink();
        using var controller = new HighwayAutoSteeringController(sink, new HighwayDirectionInputMappingResolver(memory));
        Equal(true, controller.ApplyWorldSubmarine(HighwaySteeringDirection.None, true).Success, "bound thrust");
        memory.Write(0, 5, 0x1E);
        Equal(true, controller.ApplyWorldSubmarine(HighwaySteeringDirection.None, true).Success, "changed binding");
        Equal(true, sink.Held.SetEquals([0x1E]), "remap releases the old Confirm key");
        memory.Write(1, 6, 0x1E);
        Equal(false, controller.ApplyWorldSubmarine(HighwaySteeringDirection.None, true).Success, "Cancel alias would surface and is refused");
        Equal(0, sink.Held.Count, "alias failure releases all owned keys");
        memory.Write(1, 6, 0);
        memory.Readable = false;
        Equal(false, controller.ApplyWorldSubmarine(HighwaySteeringDirection.None, true).Success, "read failure has no fallback control");
        Equal(true, controller.ReleaseAll().Success, "release needs no mapping read");
    }

    private static void SteeringTurnsBeforeThrustAndRecoversSafeDepth()
    {
        var state = Submarine(-3000, 0);
        var ahead = new WorldMapRouteWaypoint(10000, -4500, 9000);
        Equal(true, WorldMapSubmarineSteering.TryResolve(state, ahead, out var direction, out var thrust), "ahead command");
        Equal(FieldNavigationInput.None, direction, "camera0 forward is negative Z");
        Equal(true, thrust, "ahead thrust");
        Equal(true, WorldMapSubmarineSteering.TryResolve(state, ahead with { X = 11000, Z = 10000 }, out direction, out thrust), "east command");
        Equal(FieldNavigationInput.Right, direction, "Right increases native heading toward positive X");
        Equal(false, thrust, "large turn stays in place");
        Equal(true, WorldMapSubmarineSteering.TryResolve(state with { Y = -4200 }, ahead, out direction, out thrust), "recover entry depth");
        Equal(FieldNavigationInput.Down, direction, "native Down raises Y toward -3000");
        Equal(false, thrust, "no forward movement while recovering depth");
        Equal(false, WorldMapSubmarineSteering.TryResolve(state with { HasNativeControlMode = false }, ahead, out _, out _), "unknown native camera mode fails closed");
        var progress = WorldMapNavigationController.MeasurePolylineProgress(
            new(10000, -3000, 10000), [new(10000, -4500, 9000)], state with { Z = 9500 }, 0x48000, 0x38000);
        Equal(true, Math.Abs(progress.Fraction - 0.5) < 0.001, "seabed waypoint height does not block horizontal progress");
    }

    internal static WorldMapStateSnapshot Submarine(int y, int camera) =>
        new(3, 2, 0, 1396, 10000, y, 10000, 0, 0, 3, 0, 13, 0, camera,
            new FieldNavigationControlTransform(-camera / 16))
        { HasNativeControlMode = true, NativeCameraMode = 2, NativeFrameMultiplier = 2 };

    private static void SharedNavigatorDrivesTheSubWithThrustAndKeepsRecoveryOwned()
    {
        var a = new WorldMapVertex(9000, -4500, 8500);
        var b = new WorldMapVertex(12000, -4500, 8500);
        var c = new WorldMapVertex(9000, -4500, 11000);
        var d = new WorldMapVertex(12000, -4500, 11000);
        var map = new WorldMapData(2, 0, 9, 7, 12,
            [new(0, 0, 1, 1, 0, 0, a, b, c, 3, 0, 0, 0, false, [1]),
             new(1, 0, 1, 1, 0, 1, b, d, c, 3, 0, 0, 0, false, [0])], [], "sub input fixture");
        var target = new WorldMapNavigationTarget(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location,
            "Underwater destination", 10000, -4500, 9000, 0, 0, "sub-destination", new HashSet<int> { 0 });
        var state = Submarine(-3000, 0) with { X = 11000, Z = 10500 };
        var navigation = new WorldMapNavigationController(map, new WorldMapRoutePlanner(map), (_, _) => [target]);
        var spoken = navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state)?.Speech;
        Equal(true, navigation.BeaconEnabled, $"shared submarine route starts: {spoken}");
        var aim = navigation.Probe.Route!.Waypoints[^1];
        var heading = (int)Math.Round(Math.Atan2(aim.X - state.X, state.Z - aim.Z) * 4096 / (2 * Math.PI));
        state = state with { CameraFront = (heading + 4096) % 4096 };
        Equal(true, navigation.TryResolveAutomaticInput(state, out var input), "shared route resolves thrust");
        Equal(FieldNavigationInput.None, input, "aligned submarine has no directional key");
        Equal(true, navigation.AutomaticInputIsWorldSubmarine && navigation.AutomaticInputHoldsSubmarineThrust,
            "hosts receive the world-submarine action profile");
        Equal(false, navigation.AutomaticInputHoldsFlightAction, "no Highwind strafe action");
        var deep = state with { Y = -4200 };
        Equal(true, navigation.TryResolveAutomaticInput(deep, out input), "safe depth is recovered in place");
        Equal(FieldNavigationInput.Down, input, "recovery native action");
        Equal(false, navigation.AutomaticInputHoldsSubmarineThrust, "recovery cannot drift toward hidden Emerald");
        navigation.PauseForCombat("test combat");
        Equal(false, navigation.TryResolveAutomaticInput(state, out _), "combat releases world-submarine command");
        Equal(false, navigation.AutomaticInputHoldsSubmarineThrust, "stale thrust property cleared on combat");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
    }

    private static void DeepApproachGuardCoversTheWholeWrappedSegment()
    {
        var ship = WorldMapUnderwaterEntryGuard.Gelnika;
        var start = Submarine(-4250, 2048) with { X = ship.X + 500, Z = ship.Z - 1500 };
        Equal(true, WorldMapUnderwaterEntryGuard.TryFindBlockedSite(start,
            new(start.X, start.Y, ship.Z + 1500), false, out var site),
            "both endpoints can be outside while a deep lease crosses the ship entrance");
        Equal("Sunken Gelnika", site, "fixed script entrance explains the blocked lease");
        Equal(false, WorldMapUnderwaterEntryGuard.TryFindBlockedSite(start with { X = ship.X + 976 },
            new(ship.X + 976, start.Y, ship.Z + 1500), false, out _),
            "a segment outside the native horizontal Manhattan bound remains clear");
        Equal(true, WorldMapUnderwaterEntryGuard.TryFindBlockedSite(start with { X = start.X + 0x48000 },
            new(start.X + 0x48000, start.Y, ship.Z + 1500), false, out _),
            "native wrapped coordinates do not hide a fixed entrance");
        var wreck = Submarine(-4250, 0) with
            { X = WorldMapUnderwaterEntryGuard.RedWreck.X, Z = WorldMapUnderwaterEntryGuard.RedWreck.Z };
        Equal(false, WorldMapUnderwaterEntryGuard.TryFindBlockedSite(wreck, new(wreck.X, wreck.Y, wreck.Z), false, out _),
            "a removed red wreck does not block an otherwise clear deliberate approach");
        Equal(true, WorldMapUnderwaterEntryGuard.TryFindBlockedSite(wreck, new(wreck.X, wreck.Y, wreck.Z), true, out site),
            "the present red wreck also guards descent in place");
        Equal("Red submarine wreck", site, "red wreck guard uses its fixed place label");
    }

    private static void InstalledUnderwaterDestinationsUseNativeArrivalAndHideUnseenEmerald()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (dataRoot is null || sourceRoot is null)
        {
            Console.WriteLine("installed underwater destination/replay checks skipped: data/source roots were not provided.");
            return;
        }
        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm2.map"), 2, 0);
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        SightingsAreAnnouncedOnceAndUnreadableVisibilityCannotLeakTargets(map, catalog);
        EmeraldDescentCannotEnterAnUnselectedWreck(map, catalog);
        Equal(true, catalog.Locations.Any(t => t.Label == "Sunken Gelnika"), "underwater Locations includes native Gelnika");
        var gelnika = catalog.Locations.Single(t => t.Label == "Sunken Gelnika");
        var top = Submarine(-3000, 0) with { X = gelnika.X, Z = gelnika.Z };
        Equal(false, gelnika.HasArrived(top, gelnika.TriangleId), "above Gelnika is not native arrival");
        Equal(true, gelnika.HasArrived(top with { Y = gelnika.Y + 800 }, gelnika.TriangleId), "native Gelnika proximity is honored");
        var key = catalog.Locations.Single(t => t.Label == "Key of the Ancients");
        Equal(true, key.NativeTriggerTriangleIds.Count == 2, "Key native mesh/script triangles");
        var keyState = top with { X = key.X, Z = key.Z, Y = key.Y + 501, TerrainId = 15, TerrainScriptId = 3 };
        Equal(false, key.HasArrived(keyState, key.TriangleId), "Key needs native seabed clearance");
        Equal(true, key.HasArrived(keyState with { Y = key.Y + 499 }, key.TriangleId), "Key native clearance handoff");
        var hidden = new WorldMapEntitySnapshot(0x123000, 0, false, 151732, -4250, 159688, 3, 0, 30, 2);
        Equal(false, catalog.ReadTargets(WorldMapNavigationCategory.Events, top, [hidden]).Any(t => t.Label == "Emerald Weapon"),
            "loaded/drawable Emerald alone cannot be selected");
        var red = new WorldMapEntitySnapshot(0x124000, 0, false, 143655, -4970, 184718, 3, 0, 28, 2);
        Equal(false, catalog.ReadTargets(WorldMapNavigationCategory.Transportation, top, [red]).Any(),
            "underwater red wreck is a destination rather than a boardable vehicle");
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        Equal(true, planner.TryBuildRoute(top, key, out _), $"installed Gelnika-to-Key route: {planner.LastDiagnostic}");
        Equal(true, planner.TryBuildRoute(top, catalog.Locations.Single(t => t.Label == "Red submarine wreck"), out _),
            $"installed Gelnika-to-red-wreck route: {planner.LastDiagnostic}");
        var navigation = new WorldMapNavigationController(map, planner, (_, _) => [gelnika]);
        var away = top;
        var started = navigation.HandleAction(FieldNavigationAction.ToggleBeacon, away);
        Equal(true, navigation.BeaconEnabled, $"underwater Gelnika navigation starts: {started?.Speech} ({navigation.LastDiagnostic})");
        var approach = navigation.Observe(top, automaticWalkActive: true);
        Equal(true, approach?.StopAutoWalk == true && approach?.Speech?.Contains("descend", StringComparison.OrdinalIgnoreCase) == true,
            $"above Gelnika gives a manual descent handoff: {approach?.Speech}");
        Equal(true, navigation.BeaconEnabled, "beacon retained during manual descent");
        Equal(false, navigation.TryResolveAutomaticInput(top, out _), "no hidden-risk automatic descent");
        Equal(false, navigation.AutomaticInputHoldsSubmarineThrust, "handoff stops native thrust");
        Equal(true, navigation.Observe(top with { Y = gelnika.Y + 800 })?.Speech?.Contains("Arrived", StringComparison.Ordinal) == true,
            "descent only completes on native proximity");

        IReadOnlyList<WorldMapNavigationTarget> choices = [gelnika];
        var switchRoute = new WorldMapNavigationController(map, planner, (_, _) => choices);
        switchRoute.HandleAction(FieldNavigationAction.ToggleBeacon, top);
        switchRoute.Observe(top, automaticWalkActive: true);
        choices = [key];
        switchRoute.HandleAction(FieldNavigationAction.NextTarget, top);
        Equal(true, switchRoute.TryResolveAutomaticInput(top, out _), "new destination releases the prior descent hold");

        var visible = hidden with { IsVisibleUnderwater = true };
        var emerald = catalog.ReadTargets(WorldMapNavigationCategory.Events, top, [visible]).Single(t => t.Label == "Emerald Weapon");
        IReadOnlyList<WorldMapEntitySnapshot> observed = [visible];
        Equal(true, WorldMapBroncoLanding.TryFindSurface(map, visible.X - 3000, visible.Z, out var explicitFloor),
            "a clear deliberate Emerald approach has native terrain");
        var explicitStart = top with
        {
            X = visible.X - 3000, Z = visible.Z, TerrainId = explicitFloor.TerrainId,
            RegionId = explicitFloor.RegionId & 31, TerrainScriptId = explicitFloor.TerrainScriptId
        };
        var pursue = new WorldMapNavigationController(map, planner,
            (current, _) => catalog.ReadTargets(WorldMapNavigationCategory.Events, current, observed), entityProvider: () => observed);
        pursue.HandleAction(FieldNavigationAction.ToggleBeacon, explicitStart);
        Equal(true, pursue.BeaconEnabled, "explicit visible Emerald target can start navigation");
        Equal(true, pursue.TryResolveAutomaticInput(explicitStart, out var depth), "explicit selection authorizes approach depth");
        Equal(FieldNavigationInput.Up, depth, "only explicitly selected Emerald may use automatic descent");
        observed = [visible with { X = visible.X + 400 }];
        pursue.Observe(explicitStart);
        Equal(visible.X + 400, pursue.Probe.Route!.Waypoints[^1].X, "visible movement refreshes Emerald's route endpoint within the same triangle");
        observed = [hidden];
        Equal(false, pursue.TryResolveAutomaticInput(explicitStart, out _), "loss of sight releases approach input immediately");
        var visibilityClock = new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc);
        Equal(false, pursue.Observe(explicitStart, visibilityClock, automaticWalkActive: true)?.StopAutoWalk == true,
            "one missing render frame pauses input but keeps the selected approach");
        observed = [visible];
        pursue.Observe(explicitStart, visibilityClock.AddMilliseconds(100), automaticWalkActive: true);
        Equal(true, pursue.TryResolveAutomaticInput(explicitStart, out _), "fresh positive visibility resumes the same approach");
        observed = [hidden];
        pursue.Observe(explicitStart, visibilityClock.AddMilliseconds(200), automaticWalkActive: true);
        var lost = pursue.Observe(explicitStart, visibilityClock.AddMilliseconds(800), automaticWalkActive: true);
        Equal(true, lost?.StopAutoWalk == true && lost?.Speech?.Contains("confirm", StringComparison.OrdinalIgnoreCase) == true,
            "sustained loss of positive visibility stops with an explanation");
        foreach (var origin in catalog.Locations)
        foreach (var destination in catalog.Locations.Where(t => t.StableId != origin.StableId))
            ReplayInstalledRoute(map, catalog, origin, destination);
    }

    private static void SightingsAreAnnouncedOnceAndUnreadableVisibilityCannotLeakTargets(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var runtime = new WorldMapRuntimeContext(map, catalog, null, 256, TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(300), 1, 10, TimeSpan.FromSeconds(2));
        var state = Submarine(-3000, 0);
        var visible = new WorldMapEntitySnapshot(0x123000, 0, false, 10000, -4250, 9000, 3, 0, 30, 2)
            { IsVisibleUnderwater = true };
        runtime.UpdateEntities([visible]);
        Equal(null, runtime.ObserveUnderwaterSightings(state, speechReserved: true), "speech reservation retains pending sighting");
        Equal(true, runtime.ObserveUnderwaterSightings(state)?.Contains("visible", StringComparison.Ordinal) == true, "positive sighting announced");
        Equal(null, runtime.ObserveUnderwaterSightings(state), "stationary visible Emerald does not repeat");
        runtime.UpdateEntities(state, [visible], memory: null);
        Equal(false, runtime.Entities[0].IsVisibleUnderwater, "unavailable visibility clears caller's old flag");
        Equal(null, runtime.ObserveUnderwaterSightings(state), "hidden Emerald is silent");
        runtime.UpdateEntities([visible]);
        Equal(null, runtime.ObserveUnderwaterSightings(state), "a transient unreadable render frame does not repeat the sighting");
        var clock = new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Utc);
        runtime.UpdateEntities([]);
        runtime.ObserveUnderwaterSightings(state, nowUtc: clock);
        runtime.ObserveUnderwaterSightings(state, nowUtc: clock.AddMilliseconds(600));
        runtime.UpdateEntities([visible]);
        Equal(true, runtime.ObserveUnderwaterSightings(state, nowUtc: clock.AddMilliseconds(700)) is not null,
            "sustained absence re-arms the next positive sighting");
        runtime.Reset();
        runtime.UpdateEntities([visible]);
        Equal(true, runtime.ObserveUnderwaterSightings(state) is not null, "reset re-arms a genuine fresh sighting");
    }

    private static void EmeraldDescentCannotEnterAnUnselectedWreck(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        foreach (var site in catalog.Locations.Where(t => t.NativeUnderwaterArrival?.ModelId is 17 or 26 or 28))
        {
            var visible = new WorldMapEntitySnapshot(0x123000, 0, false, 151732, -4250,
                159688, 3, 0, 30, 2) { IsVisibleUnderwater = true };
            var entities = new[] { visible, new WorldMapEntitySnapshot(0x124000, 0, false,
                143655, -4970, 184718, 3, 0, 28, 2) };
            var nativeFace = map.Triangles[site.TriangleId];
            var state = Submarine(-3000, 0) with
            {
                X = site.X, Z = site.Z, TerrainId = nativeFace.TerrainId,
                RegionId = nativeFace.RegionId & 31, TerrainScriptId = nativeFace.TerrainScriptId
            };
            var target = catalog.ReadTargets(WorldMapNavigationCategory.Events, state, entities)
                .Single(t => t.Label == "Emerald Weapon");
            var navigation = new WorldMapNavigationController(map,
                new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds },
                (_, _) => [target], entityProvider: () => entities);
            var started = navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state);
            Equal(true, navigation.BeaconEnabled,
                $"a visible Emerald route can be selected above {site.Label}: {started?.Speech}; {navigation.LastDiagnostic}");
            Equal(false, navigation.TryResolveAutomaticInput(state, out _),
                $"Emerald descent must not enter unselected {site.Label}");
            Equal(false, navigation.AutomaticInputHoldsSubmarineThrust, "unsafe approach releases thrust immediately");
            target = target with { X = target.X + 400 };
            var stopped = navigation.Observe(state, automaticWalkActive: true);
            Equal(true, stopped?.StopAutoWalk == true && stopped?.Speech?.Contains(site.Label, StringComparison.Ordinal) == true,
                $"unsafe Emerald approach must explain the unselected {site.Label}");
        }
    }

    private static void ReplayInstalledRoute(WorldMapData map, WorldMapTargetCatalog catalog,
        WorldMapNavigationTarget origin, WorldMapNavigationTarget destination)
    {
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var navigator = new WorldMapNavigationController(map, planner, (_, _) => [destination]);
        var multiplier = origin.NativeUnderwaterArrival!.ModelId == 26 ? 1 : 2;
        var state = Submarine(-3000, origin.NativeUnderwaterArrival.ModelId * 277 % 4096) with
            { X = origin.X, Z = origin.Z, NativeFrameMultiplier = multiplier };
        var clock = new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);
        UpdateSurface();
        var started = navigator.HandleAction(FieldNavigationAction.ToggleBeacon, state, clock);
        Equal(true, navigator.BeaconEnabled, $"{origin.Label} to {destination.Label} starts: {started?.Speech}");
        var handoff = false;
        var moved = 0;
        for (var tick = 0; tick < 12000 && navigator.BeaconEnabled; tick++)
        {
            clock = clock.AddMilliseconds(100);
            var output = navigator.Observe(state, clock, automaticWalkActive: true);
            if (output?.StopAutoWalk == true)
            {
                Equal(true, output?.Speech?.Contains("descend", StringComparison.OrdinalIgnoreCase) == true,
                    $"native route must reach descent handoff: {output?.Speech} ({navigator.LastDiagnostic})");
                handoff = true;
                break;
            }
            if (!navigator.TryResolveAutomaticInput(state, out var input)) continue;
            Equal(false, input == FieldNavigationInput.Up, "ordinary destination never automatically descends toward hidden Emerald");
            // FUN_0074EA48: underwater turn = multiplier * 8 per native frame;
            // Confirm gives multiplier * 30 forward, with no eased velocity after release.
            for (var frame = 0; frame < 3; frame++)
            {
                if (input is FieldNavigationInput.Left or FieldNavigationInput.Right)
                    state = state with { CameraFront = (state.CameraFront +
                        (input == FieldNavigationInput.Right ? 1 : -1) * multiplier * 8 + 4096) % 4096 };
                if (navigator.AutomaticInputHoldsSubmarineThrust)
                {
                    var radians = state.CameraFront * Math.PI * 2 / 4096;
                    var sine = (int)Math.Round(Math.Sin(radians) * 4096);
                    var cosine = (int)Math.Round(Math.Cos(radians) * 4096);
                    var x = (state.X + ((sine * multiplier * 30) >> 12) + map.WrapWidth) % map.WrapWidth;
                    var z = (state.Z + ((-cosine * multiplier * 30) >> 12) + map.WrapHeight) % map.WrapHeight;
                    var step = new WorldMapRouteWaypoint(x, state.Y, z);
                    Equal(true, planner.CanTraverseSegment(state, step, null, destination.NativeEntranceExemptions),
                        $"replayed native movement must fit actual terrain: {state.X},{state.Z},terrain={state.TerrainId},script={state.TerrainScriptId} -> {x},{z}");
                    state = state with { X = x, Z = z };
                    UpdateSurface();
                    moved++;
                }
                Equal(true, state.Y >= -3000, "ordinary replay stays above Emerald encounter range");
            }
        }
        Equal(true, handoff && moved > 0,
            $"{origin.Label} -> {destination.Label} must reach handoff: {state.X},{state.Z}; {navigator.LastDiagnostic}");
        Equal(false, navigator.TryResolveAutomaticInput(state, out _), "handoff releases all automatic input");
        Equal(true, WorldMapBroncoLanding.TryFindSurface(map, state.X, state.Z, out var arrivalFloor),
            "manual descent has the native seabed at the actual handoff position");
        var descended = state with
        {
            Y = Math.Min(-3000, (int)WorldMapUnderwaterArrival.FloorHeight(arrivalFloor, state.X, state.Z) + 200),
            TerrainId = arrivalFloor.TerrainId,
            TerrainScriptId = arrivalFloor.TerrainScriptId
        };
        Equal(true, destination.HasArrived(descended, arrivalFloor.Id),
            $"{destination.Label}: manual descent from the actual handoff must satisfy native arrival");
        Console.WriteLine($"underwater replay: {origin.Label} -> {destination.Label}, {moved} native forward frames, depth {state.Y}");

        void UpdateSurface()
        {
            // FUN_0074CC07 selects the lowest surface on map2, independent of player Y.
            Equal(true, WorldMapBroncoLanding.TryFindSurface(map, state.X, state.Z, out var floor), "replay has native seabed");
            state = state with { TerrainId = floor.TerrainId, RegionId = floor.RegionId & 31, TerrainScriptId = floor.TerrainScriptId };
        }
    }

    private sealed class Mapping : ILegacyAddressSpace
    {
        private readonly byte[] table = new byte[HighwayDirectionInputMappingResolver.MappingTableSize];
        internal bool Readable = true;
        internal Mapping()
        {
            Write(0, 5, 0x2D); Write(0, 12, 0x48); Write(0, 13, 0x4D); Write(0, 14, 0x50); Write(0, 15, 0x4B);
        }
        internal void Write(int bank, int slot, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(
            table.AsSpan(bank * 0x64 + slot * 4, 4), value);
        public bool TryRead(uint address, Span<byte> destination)
        {
            if (!Readable || address != HighwayDirectionInputMappingResolver.MappingTableAddress || destination.Length != table.Length) return false;
            table.CopyTo(destination); return true;
        }
    }

    private sealed class Sink : IHighwayKeyboardInputSink
    {
        internal readonly HashSet<int> Held = [];
        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            foreach (var t in transitions)
            {
                var token = t.ScanCode | (t.IsExtended ? 0x80 : 0);
                if (t.IsKeyDown) Held.Add(token); else Held.Remove(token);
            }
            return new(transitions.Count, 0);
        }
    }
}
