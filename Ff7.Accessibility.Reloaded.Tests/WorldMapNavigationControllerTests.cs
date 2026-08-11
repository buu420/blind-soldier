using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapNavigationControllerTests
{
    internal static void Run()
    {
        UsesTheApprovedFieldNavigationActionsAndCategoryOrder();
        ListsOnlyDestinationsReachableOnTheCurrentWorldSurface();
        StartsTheKalmRouteFromMidgarInsteadOfClaimingArrival();
        ExposesTheCurrentWorldRouteAsAutomaticDirectionalInput();
        NeverEmitsWorldMapAudioBeaconCues();
        DoesNotRepeatAnUnchangedWorldMapLegOnTheTimer();
        DoesNotRepeatOneDirectionAcrossConnectedWorldWaypoints();
        KeepsTheRouteAcrossNearbyWalkableTriangleDrift();
        WaitsForSustainedWorldMapDeviationBeforeReplanning();
        ResumesTheSameRouteAfterWorldMapCombat();
        PreservesRoutesOnlyAcrossTheNativeWorldBattleLifecycle();
        ConnectsCollinearWorldWaypointsIntoOneSpokenRun();
        UsesNativeWorldMapAxesAtCameraZero();
        UsesNativeWorldMapAxesAfterQuarterTurn();
        StartsRoutesReportsProgressAndCompletesOnNativeArrival();
        ProgressFallsWhenThePlayerBacktracksAlongTheSameRoute();
        UsesTheSharedScreenRelativeDirectionFormatter();
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

    private static void NeverEmitsWorldMapAudioBeaconCues()
    {
        var (map, catalog, planner) = Load();
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var farm = catalog.Locations.Single(target => target.Label == "Chocobo Farm");
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [farm],
            guidanceInterval: TimeSpan.Zero,
            beaconInterval: TimeSpan.Zero);
        var now = DateTime.UtcNow;

        var started = controller.HandleAction(FieldNavigationAction.ToggleBeacon, StateAt(map, kalm), now);
        Equal<NavigationBeaconCue?>(null, started!.Value.Beacon, "route start has no audio beacon");

        var observed = controller.Observe(StateAt(map, kalm), now.AddSeconds(1));
        Equal<NavigationBeaconCue?>(null, observed?.Beacon, "active route has no audio beacon");
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
            guidanceInterval: TimeSpan.Zero,
            beaconInterval: TimeSpan.Zero);
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
            RegionId = target.RegionId
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
        RegionId = triangle.RegionId & 0x1F
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
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"));
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
