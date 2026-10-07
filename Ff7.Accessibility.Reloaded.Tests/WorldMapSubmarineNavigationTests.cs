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
        var surfaceMap = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 3);
        var surfaceCatalog = WorldMapTargetCatalog.Load(surfaceMap,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        var loggedSurface = Submarine(-240, 3672) with
        { WorldMapType = 0, WorldProgress = 3, X = 121103, Z = 113875, TerrainId = 26, TerrainScriptId = 1 };
        Equal(true, surfaceCatalog.ReadTargets(WorldMapNavigationCategory.Events, loggedSurface, [])
            .Any(t => t.Label == "Dive underwater"), "surface submarine offers a route to native diveable Sea");
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
        TerminalPlanningIsNeutralBoundedAndFresh(map, catalog);
        // Every directed route at the default world-map scan (AccessibilityConfig
        // WorldMapScanIntervalMs = 50: 1 or 2 native frames a scan at multiplier 2), at
        // 100 ms, and at a jittered 50 ms like Windows sleep granularity.
        foreach (var (scan, jitter) in new[] { (50, 0), (100, 0), (50, 7) })
        foreach (var origin in catalog.Locations)
        foreach (var destination in catalog.Locations.Where(t => t.StableId != origin.StableId))
            ReplayInstalledRoute(map, catalog, origin, destination, scan, jitter);
        // The configurable floor (30 ms: 0 or 1 frame a scan) and a slow 200 ms host.
        foreach (var scan in new[] { 30, 200 })
        foreach (var origin in catalog.Locations)
        foreach (var destination in catalog.Locations.Where(t => t.StableId != origin.StableId))
            ReplayInstalledRoute(map, catalog, origin, destination, scan);
        // Native delivery that stops moving the sub at any point of the final approach -
        // before, during or after a positioning maneuver - must end in a truthful stop
        // within the host's no-motion bound, with every automatic input released.
        var frozenOrigin = catalog.Locations.Single(t => t.Label == "Sunken Gelnika");
        var frozenLake = catalog.Locations.Single(t => t.SubmarineSurfacingPoint is not null);
        foreach (var freezeAfter in new[] { 0, 1000, 2500, 4000, 6000 })
            ReplayInstalledRoute(map, catalog, frozenOrigin, frozenLake, 50, 0, freezeAfter);
        // The reviewer's changed-cadence case: two-frame scans accept a heading whose capture
        // is only native frames 9-11, then the host slows to 200 ms (six frames) mid-approach.
        // Arrival or a clearly explained stop are both acceptable; a false arrival is not.
        // The switch comes after one, two or three polls of thrust, so it lands before, at and
        // after the capture the two-frame plan relied on.
        foreach (var thrustPollsBeforeSlowing in new[] { 1, 2, 3 })
            ReplayInstalledRoute(map, catalog, frozenLake, frozenLake, 50, 0,
                start: Submarine(-3000, 3584) with { X = 102338, Z = 145010, NativeFrameMultiplier = 2 },
                slowToMillisecondsAfterThrust: 200, allowExplainedStop: true, slowAfterThrustPolls: thrustPollsBeforeSlowing);
        Console.WriteLine($"underwater replays: worst controller poll {worstPollWork} work units, {worstPollMilliseconds:0.0} ms (Debug, this machine)");
    }

    private static int worstPollWork;
    private static double worstPollMilliseconds;

    /// <summary>
    /// The terminal planning contract, poll by poll, at the reviewer's lake state: the first
    /// poll arms planning with no search work and neutral input; search and result polls stay
    /// neutral and within one work slice; a changed position or camera at delivery is not
    /// flown but planned again; a search that runs out of budget stays neutral, keeps the
    /// destination, and the host's no-motion deadline still runs out on time.
    /// </summary>
    private static void TerminalPlanningIsNeutralBoundedAndFresh(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var lake = catalog.Locations.Single(t => t.SubmarineSurfacingPoint is not null);
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var exempt = lake.NativeEntranceExemptions;
        WorldMapStateSnapshot At(int x, int z, int camera)
        {
            Equal(true, WorldMapBroncoLanding.TryFindSurface(map, x, z, out var floor), "planning fixture has native seabed");
            return Submarine(-3000, camera) with
            {
                X = x, Z = z, NativeFrameMultiplier = 2, TerrainId = floor.TerrainId,
                RegionId = floor.RegionId & 31, TerrainScriptId = floor.TerrainScriptId
            };
        }
        var bound = WorldMapNavigationController.SubmarineSearchSlice + 2000;

        var navigator = new WorldMapNavigationController(map, planner, (_, _) => [lake]);
        var state = At(102338, 145010, 3584);
        Equal(true, navigator.HandleAction(FieldNavigationAction.ToggleBeacon, state) is not null && navigator.BeaconEnabled,
            "lake destination starts from the reviewer's terminal state");
        Equal(true, navigator.TryResolveAutomaticInput(state, out var input), "terminal poll keeps automatic intent");
        Equal("Prepare", navigator.SubmarineTerminalState, "the first terminal poll only arms planning");
        Equal(0, navigator.LastSubmarinePollWork, "the arming poll does no search or forecast work");
        Equal(true, navigator.LastSubmarineCommandWasPlanning && input == FieldNavigationInput.None &&
            !navigator.AutomaticInputHoldsSubmarineThrust, "the arming poll is neutral, so the host releases held keys");

        void RunToReady(WorldMapStateSnapshot pose, string label)
        {
            for (var poll = 0; ; poll++)
            {
                Equal(true, poll < WorldMapNavigationController.SubmarineSearchBudget / WorldMapNavigationController.SubmarineSearchSlice + 4,
                    $"{label}: planning finishes within its work budget");
                navigator.TryResolveAutomaticInput(pose, out var planned);
                Equal(true, navigator.LastSubmarinePollWork <= bound,
                    $"{label}: one planning poll stays within a slice ({navigator.LastSubmarinePollWork} units)");
                if (navigator.SubmarineTerminalState is "Prepare" or "Search" or "Ready")
                    Equal(true, navigator.LastSubmarineCommandWasPlanning && planned == FieldNavigationInput.None &&
                        !navigator.AutomaticInputHoldsSubmarineThrust, $"{label}: search and result polls are neutral");
                if (navigator.SubmarineTerminalState == "Ready") return;
            }
        }

        RunToReady(state, "reviewer lake state");
        var moved = At(102368, 145010, 3584);
        navigator.TryResolveAutomaticInput(moved, out input);
        Equal(true, navigator.LastSubmarineCommandWasPlanning && input == FieldNavigationInput.None &&
            !navigator.AutomaticInputHoldsSubmarineThrust && navigator.SubmarineTerminalState == "Prepare" &&
            navigator.LastDiagnostic.Contains("changed since planning", StringComparison.Ordinal),
            $"a position changed since planning is not flown but planned again: {navigator.LastDiagnostic}");
        RunToReady(moved, "moved state");
        var turned = moved with { CameraFront = (moved.CameraFront + 16) % 4096 };
        navigator.TryResolveAutomaticInput(turned, out input);
        Equal(true, navigator.LastSubmarineCommandWasPlanning && input == FieldNavigationInput.None &&
            navigator.SubmarineTerminalState == "Prepare",
            $"a camera changed since planning is not flown but planned again: {navigator.LastDiagnostic}");
        RunToReady(turned, "turned state");
        navigator.TryResolveAutomaticInput(turned, out input);
        Equal(false, navigator.LastSubmarineCommandWasPlanning, "the following fresh poll with the planned pose flies the plan");
        Equal(true, navigator.SubmarineTerminalState is "Final" or "Maneuver", $"a checked plan is flown: {navigator.SubmarineTerminalState}");
        if (navigator.AutomaticInputHoldsSubmarineThrust)
            Equal(true, ExactFullLeaseFits(map, planner, turned, exempt), "the first planned thrust has an exact full lease from the actual pose");

        // A search that runs out of budget: neutral, destination kept, no clock reset.
        var starved = new WorldMapNavigationController(map, planner, (_, _) => [lake]) { SubmarineSearchBudgetOverride = 1 };
        starved.HandleAction(FieldNavigationAction.ToggleBeacon, state);
        starved.TryResolveAutomaticInput(state, out _);
        Equal("Prepare", starved.SubmarineTerminalState, "starved planning arms first");
        starved.TryResolveAutomaticInput(state, out input);
        Equal(true, starved.LastSubmarineCommandWasPlanning && input == FieldNavigationInput.None &&
            !starved.AutomaticInputHoldsSubmarineThrust && starved.SubmarineTerminalState == "Ordinary" &&
            starved.LastDiagnostic.Contains("work budget", StringComparison.Ordinal) && starved.BeaconEnabled,
            $"an exhausted search stays neutral on its result poll and keeps the destination: {starved.LastDiagnostic}");
        var deadline = new DateTime(2026, 10, 6, 18, 0, 0, DateTimeKind.Utc);
        var stopAfter = -1d;
        for (var poll = 0; poll < 400 && stopAfter < 0; poll++)
        {
            var output = starved.Observe(state, deadline.AddMilliseconds(poll * 50), automaticWalkActive: true);
            if (output?.StopAutoWalk == true)
            {
                Equal(true, output.Value.Speech?.Contains("Auto walk stopped", StringComparison.Ordinal) == true,
                    $"exhausted planning ends in an explained stop: {output.Value.Speech}");
                stopAfter = poll * 0.05;
                break;
            }
            starved.TryResolveAutomaticInput(state, out _);
        }
        Equal(true, stopAfter >= 0 && stopAfter <= 5.5,
            $"repeated starved searches with the native side frozen do not extend the 5-second no-motion deadline ({stopAfter:0.00} s)");
        Console.WriteLine($"terminal planning contract: arming poll 0 units; planning polls <= {bound} units; exhausted search stopped after {stopAfter:0.00} s frozen");
    }

    /// <summary>
    /// The test's own check of the native full input lease from an actual pose: every native
    /// frame of LeaseUpdates(multiplier) along the camera, then 96 units of margin, each a
    /// segment the planner's exact submarine body test accepts.
    /// </summary>
    private static bool ExactFullLeaseFits(WorldMapData map, WorldMapRoutePlanner planner, WorldMapStateSnapshot state,
        IReadOnlySet<int>? exempt)
    {
        var multiplier = Math.Clamp(state.NativeFrameMultiplier, 1, 4);
        var (fx, fz) = WorldMapNavigationController.SubmarineForwardFrame(state.CameraFront, multiplier);
        var current = state;
        for (var frame = 0; frame < WorldMapSubmarineSurfaceSteering.LeaseUpdates(multiplier); frame++)
        {
            var next = new WorldMapRouteWaypoint((current.X + fx + map.WrapWidth) % map.WrapWidth, current.Y,
                (current.Z + fz + map.WrapHeight) % map.WrapHeight);
            if (!planner.CanTraverseSegment(current, next, null, exempt) ||
                !WorldMapBroncoLanding.TryFindSurface(map, next.X, next.Z, out var floor)) return false;
            current = current with
            {
                X = next.X, Z = next.Z, TerrainId = floor.TerrainId,
                TerrainScriptId = floor.TerrainScriptId, RegionId = floor.RegionId & 31
            };
        }
        var radians = state.CameraFront * Math.PI * 2 / 4096;
        return planner.CanTraverseSegment(current, new WorldMapRouteWaypoint(
            current.X + (int)Math.Round(Math.Sin(radians) * 96), current.Y,
            current.Z - (int)Math.Round(Math.Cos(radians) * 96)), null, exempt);
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

    /// <param name="scanMilliseconds">The host's world-map scan interval; the controller is
    /// asked for input once per scan, the auto-walk owner holds it until the next one.</param>
    /// <param name="jitterSeed">Nonzero: each scan lasts the interval +-15 ms.</param>
    /// <param name="freezeAfterApproachMs">Zero or more: this long into the final approach the
    /// native side stops delivering any frames, and the replay expects a bounded stop.</param>
    /// <param name="start">A starting pose other than the origin's own.</param>
    /// <param name="slowToMillisecondsAfterThrust">Nonzero: after
    /// <paramref name="slowAfterThrustPolls"/> polls of thrust the host's scans become this long.</param>
    /// <param name="allowExplainedStop">An explained guard stop also ends the replay.</param>
    internal static void ReplayInstalledRoute(WorldMapData map, WorldMapTargetCatalog catalog,
        WorldMapNavigationTarget origin, WorldMapNavigationTarget destination,
        int scanMilliseconds = 100, int jitterSeed = 0, int freezeAfterApproachMs = -1,
        WorldMapStateSnapshot? start = null, int slowToMillisecondsAfterThrust = 0, bool allowExplainedStop = false,
        int slowAfterThrustPolls = 2)
    {
        var jitter = jitterSeed == 0 ? null : new Random(jitterSeed);
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var navigator = new WorldMapNavigationController(map, planner, (_, _) => [destination]);
        var originModel = origin.NativeUnderwaterArrival?.ModelId ?? 13;
        var multiplier = start?.NativeFrameMultiplier ?? (originModel == 26 ? 1 : 2);
        var state = start ?? Submarine(-3000, originModel * 277 % 4096) with
            { X = origin.X, Z = origin.Z, NativeFrameMultiplier = multiplier };
        var clock = new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);
        UpdateSurface();
        var started = navigator.HandleAction(FieldNavigationAction.ToggleBeacon, state, clock);
        Equal(true, navigator.BeaconEnabled, $"{origin.Label} to {destination.Label} starts: {started?.Speech}");
        // The hosts' own input owner: they resolve, then Drive; StopAutoWalk stops it.
        var memory = new Mapping();
        var sink = new Sink();
        using var autoWalk = new NavigationAutoWalkController(sink, new HighwayDirectionInputMappingResolver(memory));
        Equal(true, autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true), "replay input owner starts");
        const int Thrust = 0x2D, Right = 0x4D, Left = 0x4B, Up = 0x48;
        var handoff = false;
        var moved = 0;
        var thrustPolls = 0;
        var label = start is null ? origin.Label : $"{origin.Label} (from {state.X},{state.Z})";
        var cadence = $"{scanMilliseconds} ms scan{(jitter is null ? "" : " +-15 ms")}" +
                      (slowToMillisecondsAfterThrust > 0 ? $" then {slowToMillisecondsAfterThrust} ms after {slowAfterThrustPolls} thrust polls" : "");
        // The native world update runs 60 / multiplier times a second (FUN_0074EA48 scales
        // each update by the multiplier); a scan spans however many updates its time covers.
        var elapsed = 0L;
        // The final approach - turning, positioning maneuvers and the last run in - is timed
        // from first coming within a lease and a waypoint's reach of the point (where the
        // controller starts positioning) to the handoff, and must stay well inside the host's
        // 30-second positioning bound.
        var (pointX, pointZ) = destination.SubmarineSurfacingPoint is { } surfacing
            ? (surfacing.X, surfacing.Z) : (destination.X, destination.Z);
        long? approachFrom = null;
        for (var tick = 0; elapsed < 1_200_000 && navigator.BeaconEnabled; tick++)
        {
            if (approachFrom is null &&
                Math.Sqrt(Math.Pow(WorldMapTargetCatalog.WrappedDelta(state.X, pointX, map.WrapWidth), 2) +
                          Math.Pow(WorldMapTargetCatalog.WrappedDelta(state.Z, pointZ, map.WrapHeight), 2)) <= 900 + 96 + 240)
                approachFrom = elapsed;
            var interval = (slowToMillisecondsAfterThrust > 0 && thrustPolls >= slowAfterThrustPolls ? slowToMillisecondsAfterThrust : scanMilliseconds) +
                           (jitter?.Next(-15, 16) ?? 0);
            var frames = (int)((elapsed + interval) * 60 / (1000L * multiplier) - elapsed * 60 / (1000L * multiplier));
            var frozen = freezeAfterApproachMs >= 0 && approachFrom is { } from && elapsed - from >= freezeAfterApproachMs;
            if (frozen) frames = 0;
            elapsed += interval;
            clock = clock.AddMilliseconds(interval);
            var output = navigator.Observe(state, clock, automaticWalkActive: true);
            if (output?.StopAutoWalk == true)
            {
                // What both hosts do with it: stop the input owner, releasing every key.
                autoWalk.Stop();
                Equal(true, sink.Held.Count == 0 && !autoWalk.Enabled,
                    $"StopAutoWalk releases every held key: {string.Join(",", sink.Held)}");
            }
            if (freezeAfterApproachMs >= 0 && output?.StopAutoWalk == true)
            {
                var frozenFor = (elapsed - (approachFrom ?? elapsed) - freezeAfterApproachMs) / 1000d;
                Equal(true, frozenFor >= 0 && frozenFor <= 6.5 &&
                    output?.Speech?.Contains("Auto walk stopped", StringComparison.Ordinal) == true,
                    $"frozen native delivery {freezeAfterApproachMs} ms into the final approach stops truthfully within the bound: {frozenFor:0.0} s, {output?.Speech}");
                Console.WriteLine($"underwater freeze ({cadence}): {label} -> {destination.Label}, frozen {freezeAfterApproachMs} ms into the final approach, stopped {frozenFor:0.0} s later with no key held: {navigator.LastDiagnostic}");
                return;
            }
            if (destination.SubmarineSurfacingPoint is not null && !navigator.BeaconEnabled)
            {
                Equal(true, output?.Speech?.Contains("Press Cancel to surface", StringComparison.Ordinal) == true,
                    "lake arrival explains manual surface and dock controls");
                autoWalk.Drive(FieldNavigationInput.None, canMove: false, routeActive: false, worldSubmarine: true);
                Equal(0, sink.Held.Count, "lake arrival releases every held key");
                handoff = true;
                break;
            }
            if (output?.StopAutoWalk == true)
            {
                if (allowExplainedStop && output?.Speech?.Contains("Auto walk stopped. Could not", StringComparison.Ordinal) == true)
                {
                    Console.WriteLine($"underwater replay ({cadence}): {label} -> {destination.Label} ended in an explained stop with no key held: {output?.Speech} ({navigator.LastDiagnostic})");
                    return;
                }
                Equal(true, output?.Speech?.Contains("descend", StringComparison.OrdinalIgnoreCase) == true,
                    $"native route must reach descent handoff at {cadence}: {output?.Speech} ({navigator.LastDiagnostic}; state {state.X},{state.Z}, heading {state.CameraFront}; aim {navigator.Probe.Route?.Waypoints[navigator.Probe.WaypointIndex]})");
                handoff = true;
                break;
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var hasDirection = navigator.TryResolveAutomaticInput(state, out var input);
            worstPollMilliseconds = Math.Max(worstPollMilliseconds, timer.Elapsed.TotalMilliseconds);
            worstPollWork = Math.Max(worstPollWork, navigator.LastSubmarinePollWork);
            Equal(false, input == FieldNavigationInput.Up, "ordinary destination never automatically descends toward hidden Emerald");
            autoWalk.Drive(hasDirection ? input : FieldNavigationInput.None, canMove: hasDirection,
                routeActive: navigator.BeaconEnabled, worldSubmarine: navigator.AutomaticInputIsWorldSubmarine,
                holdSubmarineThrust: navigator.AutomaticInputHoldsSubmarineThrust);
            if (navigator.LastSubmarineCommandWasPlanning)
                Equal(false, sink.Held.Contains(Thrust) || sink.Held.Contains(Right) || sink.Held.Contains(Left),
                    $"a planning poll holds no turn or thrust key ({navigator.SubmarineTerminalState})");
            if (sink.Held.Contains(Thrust))
            {
                thrustPolls++;
                Equal(true, ExactFullLeaseFits(map, planner, state, destination.NativeEntranceExemptions),
                    $"every delivered thrust has an exact full native lease from the actual pose {state.X},{state.Z} camera {state.CameraFront}");
            }
            Equal(false, sink.Held.Contains(Up), "no automatic descent key");
            // FUN_0074EA48: underwater turn = multiplier * 8 per native frame; Confirm gives
            // multiplier * 30 forward, with no eased velocity after release. Only the keys the
            // owner actually holds move the sub.
            for (var frame = 0; frame < frames; frame++)
            {
                if (sink.Held.Contains(Right) != sink.Held.Contains(Left))
                    state = state with { CameraFront = (state.CameraFront +
                        (sink.Held.Contains(Right) ? 1 : -1) * multiplier * 8 + 4096) % 4096 };
                if (sink.Held.Contains(Thrust))
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
                    if (destination.SubmarineSurfacingPoint is not null)
                    {
                        foreach (var (ox, oz) in new[] { (0, 0), (-200, 0), (200, 0), (0, -200), (0, 200) })
                            Equal(true, WorldMapBroncoLanding.TryFindSurface(map, state.X + ox, state.Z + oz, out var contact) &&
                                WorldMapTerrainPassability.CanTraverse(13, 2, contact.TerrainId),
                                $"Lucrecia tunnel route must fit native submarine contact {state.X + ox},{state.Z + oz}");
                    }
                    moved++;
                }
                Equal(true, state.Y >= -3000, "ordinary replay stays above Emerald encounter range");
            }
        }
        Equal(true, handoff && moved > 0,
            $"{label} -> {destination.Label} must reach handoff at {cadence}: {state.X},{state.Z}; {navigator.LastDiagnostic}");
        var approachSeconds = (elapsed - (approachFrom ?? elapsed)) / 1000d;
        Equal(true, freezeAfterApproachMs < 0, $"{label} -> {destination.Label}: frozen native delivery must not reach the handoff");
        Equal(true, approachSeconds <= 30,
            $"{label} -> {destination.Label} at {cadence}: final approach took {approachSeconds:0.0} s");
        Equal(false, navigator.TryResolveAutomaticInput(state, out _), "handoff releases all automatic input");
        Equal(0, sink.Held.Count, "handoff leaves no key held");
        if (destination.SubmarineSurfacingPoint is not null)
        {
            Equal(true, destination.HasArrived(state, -1), "lake handoff is at the safe point on native Sea");
            Equal(-3000, state.Y, "lake trip stays at safe travel depth");
            Console.WriteLine($"underwater replay ({cadence}): {label} -> {destination.Label}, {moved} native forward frames, final approach {approachSeconds:0.0} s, depth {state.Y}");
            return;
        }
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
        Console.WriteLine($"underwater replay ({cadence}): {label} -> {destination.Label}, {moved} native forward frames, final approach {approachSeconds:0.0} s, depth {state.Y}");

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
