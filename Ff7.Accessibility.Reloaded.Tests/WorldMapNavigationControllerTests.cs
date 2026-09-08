using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapNavigationControllerTests
{
    internal static void Run()
    {
        UsesTheApprovedFieldNavigationActionsAndCategoryOrder();
        NavigatesTerrainRegionsThroughTheSharedCategoryUx();
        ListsOnlyDestinationsReachableOnTheCurrentWorldSurface();
        StartsTheKalmRouteFromMidgarInsteadOfClaimingArrival();
        ExposesTheCurrentWorldRouteAsAutomaticDirectionalInput();
        DoesNotRepeatAnUnchangedWorldMapLegOnTheTimer();
        DoesNotRepeatOneDirectionAcrossConnectedWorldWaypoints();
        CorrectsBackTowardACrossedCommittedWaypoint();
        StopsNearTargetHuntingInsteadOfOscillatingForever();
        StopsAutomaticWalkingThatCannotConvergeWithoutDroppingNavigation();
        AllowsAutomaticWalkingThatKeepsMakingProgress();
        KeepsTheRouteAcrossNearbyWalkableTriangleDrift();
        WaitsForSustainedWorldMapDeviationBeforeReplanning();
        ResumesTheSameRouteAfterWorldMapCombat();
        PreservesRoutesOnlyAcrossTheNativeWorldBattleLifecycle();
        ConnectsCollinearWorldWaypointsIntoOneSpokenRun();
        UsesNativeWorldMapAxesAtCameraZero();
        UsesNativeWorldMapAxesAfterQuarterTurn();
        StartsRoutesReportsProgressAndCompletesOnNativeArrival();
        DoesNotArriveAtTheOldJunonMarkerBeforeTheNativeTrigger();
        ProgressFallsWhenThePlayerBacktracksAlongTheSameRoute();
        UsesTheSharedScreenRelativeDirectionFormatter();
    }

    private static void NavigatesTerrainRegionsThroughTheSharedCategoryUx()
    {
        var (map, catalog, planner) = Load();
        var junon = catalog.Locations.Single(target => target.Label == "Junon");
        var state = StateAt(map, junon);
        var progress = new RecordingProgress();
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (current, category) => catalog.ReadTargets(
                category,
                current,
                Array.Empty<WorldMapEntitySnapshot>()),
            progress);

        WorldMapNavigationOutput? selected = null;
        for (var index = 1; index < WorldMapTargetCatalog.CategoryOrder.Count; index++)
        {
            selected = controller.HandleAction(FieldNavigationAction.NextCategory, state);
        }

        Equal(WorldMapNavigationCategory.Regions, controller.CurrentCategory,
            "O reaches the terrain-region category through the normal category order");
        Contains("Regions", selected!.Value.Speech,
            "the shared category speech names Regions");

        var regionTargets = catalog.ReadTargets(
            WorldMapNavigationCategory.Regions,
            state,
            Array.Empty<WorldMapEntitySnapshot>());
        var forestIndex = regionTargets
            .Select((target, index) => (target, index))
            .Single(pair => pair.target.Label == "Forest, Junon Area")
            .index;
        for (var index = 0; index < forestIndex; index++)
        {
            selected = controller.HandleAction(FieldNavigationAction.NextTarget, state);
        }

        Contains("Forest, Junon Area", selected!.Value.Speech,
            "J and L use the normal target speech for a terrain region");
        var started = controller.HandleAction(FieldNavigationAction.ToggleBeacon, state);
        Equal(true, controller.BeaconEnabled,
            "I starts spoken navigation to a terrain region");
        Contains("Navigation on", started!.Value.Speech,
            "terrain-region navigation uses the normal route-start speech");
        Contains("Forest, Junon Area", started.Value.Speech,
            "terrain-region route speech retains the selected label");
        Equal(0, progress.ActivatedAt,
            "terrain-region navigation activates the shared progress indicator");

        var retainedTriangle = map.Triangles[regionTargets[forestIndex].ArrivalTriangleIds.First()];
        var inside = state with
        {
            X = retainedTriangle.Centroid.X,
            Y = retainedTriangle.Centroid.Y,
            Z = retainedTriangle.Centroid.Z,
            TerrainId = retainedTriangle.TerrainId,
            RegionId = retainedTriangle.RegionId
        };
        var arrivalController = new WorldMapNavigationController(
            map,
            planner,
            (current, category) => catalog.ReadTargets(
                category,
                current,
                Array.Empty<WorldMapEntitySnapshot>()));
        WorldMapNavigationOutput? currentRegion = null;
        for (var index = 1; index < WorldMapTargetCatalog.CategoryOrder.Count; index++)
        {
            currentRegion = arrivalController.HandleAction(FieldNavigationAction.NextCategory, inside);
        }

        Contains("Forest, Junon Area", currentRegion!.Value.Speech,
            "the current terrain region sorts first when the player is inside it");
        Contains("at destination", currentRegion.Value.Speech,
            "being inside a retained terrain region uses the normal arrival wording");
    }

    private static void ListsOnlyDestinationsReachableOnTheCurrentWorldSurface()
    {
        var (map, catalog, planner) = Load();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (state, category) => catalog.ReadTargets(category, state.RegionId, state.GameMoment));
        var state = StateAt(map, midgar);

        Contains("Midgar", controller.HandleAction(FieldNavigationAction.RepeatTarget, state)!.Value.Speech,
            "current location is listed");
        Contains("Kalm", controller.HandleAction(FieldNavigationAction.NextTarget, state)!.Value.Speech,
            "Kalm is reachable");
        Contains("Chocobo Farm", controller.HandleAction(FieldNavigationAction.NextTarget, state)!.Value.Speech,
            "Chocobo Farm is reachable");
        Contains("Mythril Mine (Midgar side)",
            controller.HandleAction(FieldNavigationAction.NextTarget, state)!.Value.Speech,
            "the accessible side of Mythril Mine is reachable");
        Contains("Midgar", controller.HandleAction(FieldNavigationAction.NextTarget, state)!.Value.Speech,
            "the list wraps before inaccessible destinations");
    }

    private static void StartsTheKalmRouteFromMidgarInsteadOfClaimingArrival()
    {
        var (map, catalog, planner) = Load();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (state, category) => catalog.ReadTargets(category, state.RegionId, state.GameMoment));
        var state = StateAt(map, midgar);

        var selected = controller.HandleAction(FieldNavigationAction.NextCategory, state);
        DoesNotContain("at destination", selected!.Value.Speech,
            "category selection describes the first meaningful route segment");
        var started = controller.HandleAction(FieldNavigationAction.ToggleBeacon, state);

        Equal(true, controller.BeaconEnabled, "Kalm navigation starts");
        Contains("Navigation on", started!.Value.Speech, "route announces navigation on");
        Contains("Kalm", started.Value.Speech, "route names Kalm");
        DoesNotContain("at destination", started.Value.Speech,
            "route start describes the first meaningful route segment");
    }

    private static void ExposesTheCurrentWorldRouteAsAutomaticDirectionalInput()
    {
        var (map, catalog, planner) = Load();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (state, category) => catalog.ReadTargets(category, state.RegionId, state.GameMoment));
        var state = StateAt(map, midgar);

        _ = controller.HandleAction(FieldNavigationAction.NextCategory, state);
        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, state);

        Equal(
            true,
            controller.TryResolveAutomaticInput(state, out var direction),
            "active world route exposes an automatic direction");
        Equal(
            true,
            direction is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft,
            "world route direction is one of the same eight navigation inputs");
        controller.Suspend("test complete");
        Equal(
            false,
            controller.TryResolveAutomaticInput(state, out _),
            "suspended world navigation never emits movement");
    }

    private static void DoesNotRepeatAnUnchangedWorldMapLegOnTheTimer()
    {
        var (map, catalog, planner) = Load();
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var farm = catalog.Locations.Single(target => target.Label == "Chocobo Farm");
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [farm],
            guidanceInterval: TimeSpan.FromSeconds(1));
        var state = StateAt(map, kalm);
        var now = DateTime.UtcNow;

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
        var repeated = controller.Observe(state, now.AddSeconds(2));

        Equal<string?>(null, repeated?.Speech,
            "the timer does not repeat a connected leg whose direction has not changed");
        Contains("Route progress", controller.HandleAction(
                FieldNavigationAction.RepeatTarget,
                state,
                now.AddSeconds(3))!.Value.Speech,
            "the explicit repeat key still reports the active route");
    }

    private static void DoesNotRepeatOneDirectionAcrossConnectedWorldWaypoints()
    {
        var (map, catalog, planner) = Load();
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [kalm],
            guidanceInterval: TimeSpan.FromSeconds(1));
        var start = State(x: 192_106, z: 119_955) with
        {
            Y = 445,
            TerrainId = 9,
            RegionId = 0
        };
        var now = DateTime.UtcNow;

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, now);
        var firstRightCorrection = controller.Observe(
            start with { X = 194_636, Y = 442, Z = 117_310 },
            now.AddSeconds(3));
        Contains("right", firstRightCorrection?.Speech,
            "the captured route announces its changed direction");

        var sameDirectionPastWaypoint = controller.Observe(
            start with { X = 196_000, Y = 514, Z = 115_884 },
            now.AddSeconds(4));
        Equal<string?>(null, sameDirectionPastWaypoint?.Speech,
            "crossing a route waypoint does not repeat the same controller direction");
    }

    private static void CorrectsBackTowardACrossedCommittedWaypoint()
    {
        var crossed = State(x: 140, z: 0) with
        {
            ControlTransform = new FieldNavigationControlTransform(0)
        };
        var run = WorldMapConnectedRunFormatter.Resolve(
            new WorldMapRouteWaypoint(0, 0, 0),
            [new WorldMapRouteWaypoint(100, 0, 0)],
            0,
            crossed,
            0x48000,
            0x38000,
            1);

        Equal("left", run.Direction,
            "a crossed committed leg corrects back toward its waypoint instead of continuing away");
        DoesNotContain("at destination", run.Speech,
            "crossing a waypoint outside its native arrival trigger is not a false arrival");
    }

    private static void StopsAutomaticWalkingThatCannotConvergeWithoutDroppingNavigation()
    {
        var (map, catalog, planner) = Load();
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var farm = catalog.Locations.Single(target => target.Label == "Chocobo Farm");
        var state = StateAt(map, kalm);
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [farm],
            guidanceInterval: TimeSpan.Zero);

        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
        _ = controller.Observe(state, now.AddMilliseconds(100), automaticWalkActive: true);
        WorldMapNavigationOutput? stopped = null;
        for (var sample = 1; sample <= 12; sample++)
        {
            stopped = controller.Observe(
                state,
                now.AddMilliseconds(100 + sample * 500),
                automaticWalkActive: true) ?? stopped;
        }

        Equal(true, stopped?.StopAutoWalk,
            "a world auto walk with no net progress issues an explicit stop directive");
        Contains("Auto walk stopped", stopped?.Speech,
            "a stranded player is told that automatic movement stopped");
        Contains("Could not get closer to Chocobo Farm", stopped?.Speech,
            "the convergence failure names the destination");
        Equal(true, controller.BeaconEnabled,
            "spoken navigation stays on so the player can continue manually");
        Equal(true, controller.TryResolveAutomaticInput(state, out _),
            "the route remains available after only automatic movement stops");
    }

    private static void StopsNearTargetHuntingInsteadOfOscillatingForever()
    {
        var tracker = new WorldMapAutoWalkConvergenceTracker();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var bouncingNearTarget = new[] { 80d, 8d, 40d, 7d, 60d, 6d, 48d, 9d, 55d, 8d, 42d, 7d };

        for (var index = 0; index < bouncingNearTarget.Length - 1; index++)
        {
            Equal(
                false,
                tracker.Observe(waypointIndex: 2, bouncingNearTarget[index], now.AddMilliseconds(index * 500)),
                $"near-target correction sample {index} gets time to converge");
        }

        Equal(
            true,
            tracker.Observe(
                waypointIndex: 2,
                bouncingNearTarget[^1],
                now.AddMilliseconds((bouncingNearTarget.Length - 1) * 500)),
            "repeated near-target crossings fail closed instead of hunting forever");
    }

    private static void AllowsAutomaticWalkingThatKeepsMakingProgress()
    {
        var tracker = new WorldMapAutoWalkConvergenceTracker();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var distances = new[] { 1_000d, 900d, 800d, 700d, 600d, 500d, 400d };

        for (var index = 0; index < distances.Length; index++)
        {
            Equal(
                false,
                tracker.Observe(waypointIndex: 2, distances[index], now.AddSeconds(index)),
                $"steady automatic progress sample {index} remains active");
        }
    }

    private static void KeepsTheRouteAcrossNearbyWalkableTriangleDrift()
    {
        var (map, catalog, planner) = Load();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [kalm],
            guidanceInterval: TimeSpan.FromMinutes(1));
        var start = StateAt(map, midgar);
        var now = DateTime.UtcNow;

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, now);
        var route = controller.Probe.Route
            ?? throw new InvalidOperationException("Kalm route was not created");
        var routeTriangles = route.TrianglePath.ToHashSet();
        var nearbyTriangle = map.Triangles[route.StartTriangleId].Neighbors
            .Where(id => !routeTriangles.Contains(id))
            .Select(id => map.Triangles[id])
            .First(triangle => WorldMapTerrainPassability.CanTraverse(
                start.PlayerModelId,
                start.WorldMapType,
                triangle.TerrainId));
        var drifted = StateOnTriangle(start, nearbyTriangle);

        var observed = controller.Observe(drifted, now.AddSeconds(1));

        Equal(true, controller.BeaconEnabled, "nearby walkable drift keeps navigation active");
        DoesNotContain("Route updated", observed?.Speech,
            "ordinary movement beside the exact A-star chain does not replan");
    }

    private static void ResumesTheSameRouteAfterWorldMapCombat()
    {
        var (map, catalog, planner) = Load();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var progress = new RecordingProgress();
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [kalm],
            progress,
            guidanceInterval: TimeSpan.FromMinutes(1));
        var state = StateAt(map, midgar);
        var now = DateTime.UtcNow;

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
        controller.PauseForCombat("native world battle transition");

        Equal(true, controller.BeaconEnabled, "combat pause preserves the selected destination");
        Equal(true, progress.Deactivated, "combat pause hides the route progress indicator");

        var resumed = controller.Observe(state, now.AddSeconds(30));

        Equal(true, controller.BeaconEnabled, "world return resumes navigation");
        Contains("Navigation resumed", resumed!.Value.Speech, "world return announces resumed route");
        Contains("Kalm", resumed.Value.Speech, "resumed route retains its destination");
    }

    private static void WaitsForSustainedWorldMapDeviationBeforeReplanning()
    {
        var (map, catalog, planner) = Load();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var farm = catalog.Locations.Single(target => target.Label == "Chocobo Farm");
        var progress = new RecordingProgress();
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [kalm],
            progress,
            guidanceInterval: TimeSpan.FromMinutes(1));
        var now = DateTime.UtcNow;

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, StateAt(map, midgar), now);
        var initialRoute = controller.Probe.Route
            ?? throw new InvalidOperationException("Kalm route was not created");
        var early = controller.Observe(StateAt(map, farm), now.AddSeconds(1));

        Equal(true, ReferenceEquals(initialRoute, controller.Probe.Route),
            "one open-terrain deviation sample retains the committed route");
        DoesNotContain("Route updated", early?.Speech,
            "an internal route check is never announced");

        var sustained = controller.Observe(StateAt(map, farm), now.AddSeconds(7));

        Equal(true, controller.BeaconEnabled, "a sustained deviation keeps navigation active");
        Equal(false, ReferenceEquals(initialRoute, controller.Probe.Route),
            "a sustained large deviation can intelligently rebuild the route");
        DoesNotContain("Route updated", sustained?.Speech,
            "an intelligent replan speaks only the usable direction");
        Contains("Kalm", sustained?.Speech,
            "an intelligent replan retains the selected destination");
        Equal(1, progress.ActivationCount,
            "an internal replan keeps one continuous route progress control");
    }

    private static void PreservesRoutesOnlyAcrossTheNativeWorldBattleLifecycle()
    {
        Equal(true,
            WorldMapNavigationLifecycle.IsCombatInterruptionModule(0x17),
            "native world battle transition preserves the route");
        Equal(true,
            WorldMapNavigationLifecycle.IsCombatInterruptionModule(BattleStateReader.BattleModule),
            "battle module preserves the route");
        Equal(true,
            WorldMapNavigationLifecycle.IsCombatInterruptionModule(0x11),
            "native post-battle results module preserves the route");
        Equal(false,
            WorldMapNavigationLifecycle.IsCombatInterruptionModule(FieldPositionReader.FieldModule),
            "entering a field remains a permanent world-map exit");
        Equal(false,
            WorldMapNavigationLifecycle.IsCombatInterruptionModule(0x13),
            "quit module remains a permanent world-map exit");
    }

    private static void ConnectsCollinearWorldWaypointsIntoOneSpokenRun()
    {
        var state = State(x: 0, z: 0) with
        {
            ControlTransform = new FieldNavigationControlTransform(0)
        };
        WorldMapRouteWaypoint[] waypoints =
        [
            new(1_024, 0, 0),
            new(4_096, 0, 0),
            new(4_096, 0, -1_024)
        ];

        var run = WorldMapConnectedRunFormatter.Resolve(
            new WorldMapRouteWaypoint(0, 0, 0),
            waypoints,
            0,
            state,
            0x48000,
            0x38000,
            512);

        Equal("right 8", run.Speech, "collinear waypoints become one instruction");
        Equal(1, run.EndWaypointIndex, "the run stops before the next turn");
    }

    private static void UsesNativeWorldMapAxesAtCameraZero()
    {
        var state = State(x: 0, z: 0) with
        {
            ControlTransform = new FieldNavigationControlTransform(0)
        };

        Equal("right 2", ResolveSingleRun(state, 1_024, 0),
            "camera zero maps positive world X to native right");
        Equal("left 2", ResolveSingleRun(state, -1_024, 0),
            "camera zero maps negative world X to native left");
        Equal("up 2", ResolveSingleRun(state, 0, -1_024),
            "camera zero maps negative world Z to native up");
        Equal("down 2", ResolveSingleRun(state, 0, 1_024),
            "camera zero maps positive world Z to native down");
    }

    private static void UsesNativeWorldMapAxesAfterQuarterTurn()
    {
        var state = State(x: 0, z: 0) with
        {
            ControlTransform = new FieldNavigationControlTransform(-64)
        };

        Equal("up 2", ResolveSingleRun(state, 1_024, 0),
            "a quarter-turn camera maps positive world X to native up");
        Equal("right 2", ResolveSingleRun(state, 0, 1_024),
            "a quarter-turn camera maps positive world Z to native right");
    }

    private static void UsesTheApprovedFieldNavigationActionsAndCategoryOrder()
    {
        var (map, catalog, planner) = Load();
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (state, category) => catalog.ReadTargets(category, state.RegionId, state.GameMoment));
        var state = StateAt(map, kalm);

        Contains("Story", controller.HandleAction(FieldNavigationAction.NextCategory, state)!.Value.Speech, "O category");
        Contains("Transportation", controller.HandleAction(FieldNavigationAction.NextCategory, state)!.Value.Speech, "O category again");
        Contains("Story", controller.HandleAction(FieldNavigationAction.PreviousCategory, state)!.Value.Speech, "U category");
        controller.HandleAction(FieldNavigationAction.PreviousCategory, state);
        Contains("Locations", controller.HandleAction(FieldNavigationAction.NextTarget, state)!.Value.Speech, "L target");
        Contains("Locations", controller.HandleAction(FieldNavigationAction.PreviousTarget, state)!.Value.Speech, "J target");
        Contains("Locations", controller.HandleAction(FieldNavigationAction.RepeatTarget, state)!.Value.Speech, "K repeat");
    }

    private static void StartsRoutesReportsProgressAndCompletesOnNativeArrival()
    {
        var (map, catalog, planner) = Load();
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var farm = catalog.Locations.Single(target => target.Label == "Chocobo Farm");
        var progress = new RecordingProgress();
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [farm],
            progress,
            guidanceInterval: TimeSpan.Zero);
        var now = DateTime.UtcNow;

        var enabled = controller.HandleAction(FieldNavigationAction.ToggleBeacon, StateAt(map, kalm), now);
        Equal(true, controller.BeaconEnabled, "I enables world navigation");
        Contains("Navigation on", enabled!.Value.Speech, "navigation enable speech");
        Equal(0, progress.ActivatedAt, "native progress starts at zero");

        var repeated = controller.HandleAction(FieldNavigationAction.RepeatTarget, StateAt(map, kalm), now);
        Contains("Route progress 0 percent", repeated!.Value.Speech, "K reports progress");

        var arrived = controller.Observe(StateAt(map, farm), now.AddSeconds(1));
        Equal(false, controller.BeaconEnabled, "arrival disables beacon");
        Contains("Arrived at Chocobo Farm", arrived!.Value.Speech, "native triangle arrival");
        Equal(true, progress.Completed, "native progress completes");
    }

    private static void DoesNotArriveAtTheOldJunonMarkerBeforeTheNativeTrigger()
    {
        var (map, catalog, planner) = Load();
        var junon = catalog.Locations.Single(target => target.Label == "Junon");
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [junon],
            guidanceInterval: TimeSpan.Zero);
        var oldMarker = State(x: 169_821, z: 146_958) with
        {
            Y = 0,
            TerrainId = 9,
            RegionId = 2,
            TerrainScriptId = 0
        };
        var now = DateTime.UtcNow;

        var started = controller.HandleAction(FieldNavigationAction.ToggleBeacon, oldMarker, now);

        Equal(true, controller.BeaconEnabled,
            "Junon navigation remains active at the former coordinate marker");
        DoesNotContain("Arrived", started?.Speech,
            "the former coordinate marker does not announce a false arrival");

        var arrived = controller.Observe(StateAt(map, junon), now.AddSeconds(1));
        Equal(false, controller.BeaconEnabled,
            "Junon navigation ends on the native terrain-script trigger");
        Contains("Arrived at Junon", arrived?.Speech,
            "the native trigger announces the real arrival");
    }

    private static void ProgressFallsWhenThePlayerBacktracksAlongTheSameRoute()
    {
        var start = new WorldMapRouteWaypoint(0, 0, 0);
        WorldMapRouteWaypoint[] route = [new(1_000, 0, 0), new(2_000, 0, 0)];
        var forward = WorldMapNavigationController.MeasurePolylineProgress(
            start,
            route,
            State(x: 1_500, z: 0),
            0x48000,
            0x38000);
        var backtracked = WorldMapNavigationController.MeasurePolylineProgress(
            start,
            route,
            State(x: 500, z: 0),
            0x48000,
            0x38000);

        Equal(true, forward.Fraction > backtracked.Fraction, "progress reverses on backtracking");
        Equal(0.75d, Math.Round(forward.Fraction, 2), "forward progress");
        Equal(0.25d, Math.Round(backtracked.Fraction, 2), "backtracked progress");
    }

    private static void UsesTheSharedScreenRelativeDirectionFormatter()
    {
        var state = State(x: 0, z: 0) with
        {
            ControlTransform = new FieldNavigationControlTransform(0)
        };
        var world = FieldNavigationSpokenCueFormatter.Format(0, -1_024, state.ControlTransform, 512);
        var field = FieldNavigationSpokenCueFormatter.Format(0, -1_024, state.ControlTransform, 512);
        Equal(field, world, "world and field direction speech share the formatter");
    }

    private static WorldMapStateSnapshot StateAt(WorldMapData map, WorldMapNavigationTarget target)
    {
        var triangle = map.Triangles[target.TriangleId];
        return State(target.X, target.Z) with
        {
            Y = target.Y,
            TerrainId = triangle.TerrainId,
            RegionId = target.RegionId,
            TerrainScriptId = triangle.TerrainScriptId
        };
    }

    private static WorldMapStateSnapshot StateOnTriangle(
        WorldMapStateSnapshot template,
        WorldMapTriangle triangle) => template with
    {
        X = triangle.Centroid.X,
        Y = triangle.Centroid.Y,
        Z = triangle.Centroid.Z,
        TerrainId = triangle.TerrainId,
        RegionId = triangle.RegionId & 0x1F,
        TerrainScriptId = triangle.TerrainScriptId
    };

    private static WorldMapStateSnapshot State(int x, int z) => new(
        WorldMapStateReader.WorldModule,
        0,
        0,
        341,
        x,
        0,
        z,
        0,
        0,
        0,
        0,
        0,
        30,
        0,
        new FieldNavigationControlTransform(0));

    private static string ResolveSingleRun(WorldMapStateSnapshot state, int destinationX, int destinationZ) =>
        WorldMapConnectedRunFormatter.Resolve(
            new WorldMapRouteWaypoint(0, 0, 0),
            [new WorldMapRouteWaypoint(destinationX, 0, destinationZ)],
            0,
            state,
            0x48000,
            0x38000,
            512).Speech;

    private static (WorldMapData Map, WorldMapTargetCatalog Catalog, WorldMapRoutePlanner Planner) Load()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir";
        var map = WorldMapDataLoader.Load(
            Path.Combine(dataRoot, "data", "wm", "WM0.MAP"),
            0,
            0);
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
            @"C:\FF7A11Y\accessibility_prototype";
        var catalog = WorldMapTargetCatalog.Load(
            map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return (map, catalog, new WorldMapRoutePlanner(map));
    }

    private static void Contains(string expected, string? actual, string label)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}' in '{actual}'");
        }
    }

    private static void DoesNotContain(string unexpected, string? actual, string label)
    {
        if (actual?.Contains(unexpected, StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException($"{label}: did not expect '{unexpected}' in '{actual}'");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }

    private sealed class RecordingProgress : IFieldNavigationProgressSink
    {
        internal int ActivatedAt { get; private set; } = -1;
        internal int ActivationCount { get; private set; }
        internal bool Completed { get; private set; }
        internal bool Deactivated { get; private set; }

        public void Activate(int percent)
        {
            ActivatedAt = percent;
            ActivationCount++;
            Deactivated = false;
        }
        public void SetValue(int percent) { }
        public void Complete() => Completed = true;
        public void Deactivate() => Deactivated = true;
    }
}
