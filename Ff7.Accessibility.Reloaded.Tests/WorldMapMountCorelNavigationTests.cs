using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapMountCorelNavigationTests
{
    internal static void Run()
    {
        DoesNotCutAcrossTheMountainBeforeReachingTheNativeCorner();
        ChoosesTheClosestClearForwardKeyWhenThePrimaryKeyHitsTheNativeBend();
        ResolvesOverlappingNativeTracksAtThePlayersActualHeight();
        ListsOneReachableMountainEntranceInEachApplicableCategory();
        KeepsTheExistingHighwindFlightPolicy();
        PreservesHighwindRoutesAcrossNativeVerticalGroundFaces();
        TracesAndSteersThroughConnectedNativeEdgesAcrossTheWorldSeam();
        AdvancesFromAnOccupiedCornerAfterTheNextObservation();
        ReleasesDirectionsAndAnnouncesAnUnreachableFirstWaypoint();
        ReplansAfterSustainedAutomaticDeviationWithoutDroppingTheDestination();
        WalksTheCompleteInstalledCostaRouteWithNativeDirectionalSteps();
    }

    private static void DoesNotCutAcrossTheMountainBeforeReachingTheNativeCorner()
    {
        var (map, catalog) = Load();
        var planner = new WorldMapRoutePlanner(map);
        var target = catalog.Locations.Single(target => target.Label == "Mt. Corel");
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        var now = new DateTime(2026, 9, 6, 14, 13, 42, DateTimeKind.Utc);
        // Captured native accepted positions immediately before and during the
        // 14:13:43 failure. The first required corner lies south of the cliff
        // vertex (131072,135168); the player is still north/east of that lip.
        var start = State(131517, 584, 134924, 3452);
        var approaching = State(131371, 618, 135070, 3388);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, now);
        controller.Observe(approaching, now.AddSeconds(1), automaticWalkActive: true);

        var corner = controller.Probe.Route!.Waypoints[controller.Probe.WaypointIndex];
        Equal(true, corner.Z >= 135168,
            "the captured approach retains a corner south of the native cliff instead of skipping both corners");
        Equal(true, controller.TryResolveAutomaticInput(approaching, out var input),
            "a native corner retains directional guidance");
        // The key is steered from the accepted position, not from the tiny previous corner
        // leg, and has to take the party round the lip rather than into it: its native step
        // is clear, the party's 200-unit footprint fits where it lands, and it closes on the
        // corner. Which key that is depends on where the route rounds the lip - since
        // walking routes keep the footprint's room it rounds the apex further out, and at
        // camera 3388 that is Left rather than UpLeft.
        AssertStepRoundsTheLip(planner, approaching, input, corner, "automatic movement");

        var manualController = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        manualController.HandleAction(FieldNavigationAction.ToggleBeacon, start, now);
        manualController.Observe(approaching, now.AddSeconds(1), automaticWalkActive: false);
        Equal(true, manualController.TryResolveAutomaticInput(approaching, out var newlyEnabledInput),
            "enabling automatic movement guards the native corner without a previous automatic observation");
        Equal(input, newlyEnabledInput,
            "the first automatic key cannot inherit the smoothed manual shortcut");
    }

    private static void AssertStepRoundsTheLip(
        WorldMapRoutePlanner planner,
        WorldMapStateSnapshot state,
        FieldNavigationInput input,
        WorldMapRouteWaypoint corner,
        string label)
    {
        var (stepX, stepZ) = NativeStep(input, state.CameraFront, 120);
        var landing = new WorldMapRouteWaypoint(
            state.X + (int)Math.Round(stepX), state.Y, state.Z + (int)Math.Round(stepZ));
        Equal(true, planner.CanTraverseSegment(state, landing), $"{label}: {input} steps onto native ground");
        Equal(true, planner.HasWalkingFootprint(state, landing.X, landing.Y, landing.Z),
            $"{label}: {input} lands where the party's footprint fits, clear of the cliff");
        Equal(true, stepX * (corner.X - state.X) + stepZ * (corner.Z - state.Z) > 0d,
            $"{label}: {input} closes on the corner");
    }

    private static void ListsOneReachableMountainEntranceInEachApplicableCategory()
    {
        var (map, catalog) = Load();
        var planner = new WorldMapRoutePlanner(map);
        var state = State(140807, 233, 126130, 0);
        foreach (var category in new[] { WorldMapNavigationCategory.Locations, WorldMapNavigationCategory.Story })
        {
            var mountain = catalog.ReadTargets(category, state, [])
                .Where(target => target.Label == "Mt. Corel").ToArray();
            Equal(1, mountain.Length, $"one Mount Corel entrance in {category}");
            Equal(true, planner.CanReach(state, mountain[0]),
                $"the logged Costa exit can reach Mount Corel in {category}");
            Equal(true, mountain[0].NativeLocationArrivals.All(arrival =>
                arrival.MeshX == 13 && arrival.MeshZ == 14 && arrival.TerrainScriptId == 7),
                "Mount Corel retains the unique native A054 entrance handler");
        }
    }

    private static void ChoosesTheClosestClearForwardKeyWhenThePrimaryKeyHitsTheNativeBend()
    {
        var (map, catalog) = Load();
        var planner = new WorldMapRoutePlanner(map);
        var target = catalog.Locations.Single(target => target.Label == "Mt. Corel");
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        var now = new DateTime(2026, 9, 6, 14, 11, 42, DateTimeKind.Utc);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, State(140807, 233, 126130, 0), now);
        // The pre-guard full replay stopped here pressing Left repeatedly.
        var state = State(129778, 1819, 132490, 0) with { TerrainId = 16 };
        controller.Observe(state, now.AddSeconds(1), automaticWalkActive: true);
        var waypoint = controller.Probe.Route!.Waypoints[controller.Probe.WaypointIndex];
        Equal(true, waypoint.X < state.X, "the retained corner is still the later bend to the west");
        Equal(true, planner.CanTraverseSegment(state, waypoint), "the oblique route itself remains native-clear");
        Equal(false, planner.CanTraverseSegment(state, new(state.X - 120, state.Y, state.Z)),
            "the Left step hits the native boundary");
        Equal(true, planner.CanTraverseSegment(state, new(state.X - 90, state.Y, state.Z - 90)),
            "the three-quarter diagonal UpLeft step is clear");
        Equal(true, planner.CanTraverseSegment(state, new(state.X, state.Y, state.Z - 120)),
            "Up is also a clear forward alternative");
        // Where the route rounds this bend moved when walking routes began keeping the
        // party's footprint clear, so the leg it follows from here no longer asks for Left
        // first; what has to hold is that the key it presses is never the one the boundary
        // refuses, and still closes on the bend.
        Equal(true, controller.TryResolveAutomaticInput(state, out var input), "the bend retains a clear key");
        Equal(false, input == FieldNavigationInput.Left, "the key pressed is never the refused Left");
        var (stepX, stepZ) = NativeStep(input, state.CameraFront, 120);
        Equal(true, planner.CanTraverseSegment(
                state, new(state.X + (int)Math.Round(stepX), state.Y, state.Z + (int)Math.Round(stepZ))),
            $"the {input} step is native-clear");
        Equal(true, stepX * (waypoint.X - state.X) + stepZ * (waypoint.Z - state.Z) > 0d,
            $"and {input} closes on the bend");
    }

    private static void ResolvesOverlappingNativeTracksAtThePlayersActualHeight()
    {
        var (map, _) = Load();
        var planner = new WorldMapRoutePlanner(map);
        // Both installed triangles cover this X/Z and carry terrain21/script1.
        // Their plane heights here are 2116.67 and2070.19; their centroids lie
        // elsewhere on the slopes and must not switch the player's surface.
        var upper = State(118622, 2117, 135325, 0) with { TerrainId = 21, TerrainScriptId = 1 };
        Equal(true, planner.TryResolvePlayerTriangle(upper, out var upperTriangle), "upper track resolves");
        Equal(87972, upperTriangle, "accepted player height retains the upper native track");
        // Terrain 21 is the side of the rail bridge, not ground anybody walks: FUN_0074CECA's
        // walking mask 0x721B6F83 has no bit 21, and not one on-foot sample in the eleven
        // session logs the user has sent stands on it - 427,493 lines, many repeated between
        // downloads. The party crosses here on the deck, terrain 13, at 2605.
        Equal(false, planner.CanTraverseSegment(upper, new(118147, 1885, 135467)),
            "terrain 21 under the rail bridge is not walked on");
        Equal(true, planner.TryResolvePlayerTriangle(upper with { Y = 2070 }, out var lowerTriangle),
            "lower track resolves");
        Equal(87980, lowerTriangle, "lower player height selects the overlapping lower native track");
    }

    private static WorldMapStateSnapshot State(int x, int y, int z, int camera)
    {
        var direction = -(camera / 16);
        if (direction < -128) direction += 256;
        return new WorldMapStateSnapshot(3, 0, 0, 415, x, y, z, 0, 0, 0, 3, 0, 30,
            camera, new FieldNavigationControlTransform(direction));
    }

    private static void KeepsTheExistingHighwindFlightPolicy()
    {
        var (map, _) = Load();
        var planner = new WorldMapRoutePlanner(map);
        var state = State(131371, 618, 135070, 3388);
        var beyondCliff = new WorldMapRouteWaypoint(130107, 1800, 132510);
        Equal(false, planner.CanTraverseSegment(state, beyondCliff), "the native cliff blocks walking shortcuts");
        Equal(true, planner.CanTraverseSegment(state with { PlayerModelId = 3 }, beyondCliff),
            "the existing Highwind flight policy crosses ground cliffs");
        Equal(false, planner.CanTraverseSegment(state with { CurrentModule = 1, PlayerModelId = 3 }, beyondCliff),
            "flight does not bypass world ownership");
    }

    private static void TracesAndSteersThroughConnectedNativeEdgesAcrossTheWorldSeam()
    {
        const int width = 32768;
        WorldMapTriangle[] triangles =
        [
            new(0, 0, 3, 0, 3, 0, new(width - 128, 0, 1600), new(width, 0, 1600), new(width, 0, 3200),
                0, 0, 0, 0, false, [1]),
            new(1, 0, 0, 0, 0, 0, new(0, 0, 1600), new(128, 0, 3200), new(0, 0, 3200),
                0, 0, 0, 0, false, [0])
        ];
        var map = new WorldMapData(0, 0, 1, 1, 1, triangles, [], "connected-native-edge seam fixture");
        var planner = new WorldMapRoutePlanner(map);
        var state = State(width - 64, 0, 2240, 0) with { RegionId = 0 };
        var target = new WorldMapNavigationTarget(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location,
            "Across seam", 32, 0, 2400, 1, 0, "seam:1", new HashSet<int> { 1 });
        Equal(true, planner.CanTraverseSegment(state, new(target.X, target.Y, target.Z)),
            "the trace follows the shared native edge across coordinate wrap");
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, state);
        Equal(true, controller.TryResolveAutomaticInput(state, out var input), "wrapped route retains input");
        Equal(FieldNavigationInput.DownRight, input, "wrapped steering uses the short eastward delta");
        var (probeX, probeZ) = NativeStep(input, state.CameraFront, 120);
        var probe = new WorldMapRouteWaypoint(state.X + (int)Math.Round(probeX), state.Y,
            state.Z + (int)Math.Round(probeZ));
        Equal(true, probe.X >= map.WrapWidth, "the selected native short-step endpoint lies beyond the stored world width");
        Equal(true, planner.CanTraverseSegment(state, probe), "the out-of-range short-step endpoint wraps through the native seam");
        Equal(true, planner.CanTraverseSegment(state, probe with { X = probe.X - map.WrapWidth }),
            "the equivalent normalized probe follows the same connected native surface");
        controller.Observe(state with { X = target.X, Y = target.Y, Z = target.Z });
        Equal(false, controller.BeaconEnabled, "wrapped route completes on its actual native arrival triangle");
    }

    private static void PreservesHighwindRoutesAcrossNativeVerticalGroundFaces()
    {
        var (map, _) = Load();
        var planner = new WorldMapRoutePlanner(map);
        var state = State(37314, 541, 88503, 0) with
        {
            PlayerModelId = 3, MovementSpeed = 120, TerrainId = 16, TerrainScriptId = 7, RegionId = 9
        };
        var destination = new WorldMapRouteWaypoint(37427, 695, 88439);
        var target = new WorldMapNavigationTarget(WorldMapNavigationCategory.Regions, WorldMapTargetKind.Location,
            "Adjacent flight surface", destination.X, destination.Y, destination.Z, 41681, 16,
            "native-flight:41681", new HashSet<int> { 41681 });
        Equal(true, planner.TryBuildRoute(state, target, out var route), "the existing native Highwind route remains buildable");
        Equal(true, route.TrianglePath.SequenceEqual(new[] { 41635, 41638, 41681 }),
            "the native route crosses vertical face 41638 between two nonvertical surfaces");
        Equal(false, planner.CanTraverseSegment(state with { PlayerModelId = 0 }, destination),
            "walking retains the hard vertical ground boundary");
        Equal(true, planner.CanTraverseSegment(state, destination),
            "Highwind flight is not stopped by a native vertical ground face");
        Equal(false, planner.CanTraverseSegment(state with { CurrentModule = 1 }, destination),
            "the flight exception retains native module ownership");
        Equal(false, planner.CanTraverseSegment(state with { WorldMapType = 2 }, destination),
            "the flight exception retains the loaded map identity");
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, state);
        Equal(true, controller.TryResolveAutomaticInput(state, out var input), "the native Highwind route retains input");
        Equal(FieldNavigationInput.UpRight, input, "flight retains its closest native direction across the vertical face");
        controller.Observe(state with { X = destination.X, Y = destination.Y, Z = destination.Z }, automaticWalkActive: true);
        Equal(false, controller.BeaconEnabled, "flight still completes through the actual native arrival triangle");
    }

    private static void AdvancesFromAnOccupiedCornerAfterTheNextObservation()
    {
        var (map, catalog) = Load();
        var planner = new WorldMapRoutePlanner(map);
        var target = catalog.Locations.Single(target => target.Label == "Mt. Corel");
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        var now = new DateTime(2026, 9, 6, 14, 11, 42, DateTimeKind.Utc);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, State(140807, 233, 126130, 0), now);
        var first = controller.Probe.Route!.Waypoints[0];
        var onCorner = State(first.X, first.Y, first.Z, 0);
        Equal(false, controller.TryResolveAutomaticInput(onCorner, out var beforeObservation),
            "a zero vector releases the previous direction before progress is observed");
        Equal(FieldNavigationInput.None, beforeObservation, "zero delta never invents an input");
        controller.Observe(onCorner, now.AddSeconds(1), automaticWalkActive: true);
        Equal(true, controller.Probe.WaypointIndex > 0, "the next accepted observation advances past the occupied first corner");
        Equal(true, controller.TryResolveAutomaticInput(onCorner, out _),
            "the occupied native corner resumes movement without waiting for the convergence timeout");
    }

    private static void ReleasesDirectionsAndAnnouncesAnUnreachableFirstWaypoint()
    {
        WorldMapTriangle[] triangles =
        [
            new(0, 0, 0, 0, 0, 0, new(0, 0, 0), new(2048, 0, 0), new(0, 0, 2048),
                0, 0, 0, 0, false, [1]),
            new(1, 0, 0, 0, 0, 1, new(2048, 0, 0), new(2048, 0, 2048), new(0, 0, 2048),
                0, 0, 0, 0, false, [0]),
            new(2, 0, 0, 0, 0, 2, new(3990, 0, 3990), new(4010, 0, 3990), new(4000, 0, 4010),
                0, 0, 0, 0, false, [])
        ];
        var map = new WorldMapData(0, 0, 1, 1, 1, triangles, [], "disconnected native surface fixture");
        var planner = new WorldMapRoutePlanner(map);
        var target = new WorldMapNavigationTarget(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location,
            "Connected ground", 1600, 0, 1600, 1, 0, "ground:1", new HashSet<int> { 1 });
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        var now = new DateTime(2026, 9, 6, 14, 11, 42, DateTimeKind.Utc);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, State(256, 0, 256, 0) with { RegionId = 0 }, now);
        var isolated = State(4000, 0, 4000, 0) with { RegionId = 0 };
        controller.Observe(isolated, now, automaticWalkActive: true);
        Equal(0, controller.Probe.WaypointIndex, "unreachable earlier corners bottom out at the first committed waypoint");
        Equal(false, planner.CanTraverseSegment(isolated, controller.Probe.Route!.Waypoints[0]),
            "the first waypoint cannot be reached across disconnected native surfaces");
        Equal(false, controller.TryResolveAutomaticInput(isolated, out var input), "no forward short step is clear on the isolated surface");
        Equal(FieldNavigationInput.None, input, "the controller releases all directions when no candidate is clear");
        WorldMapNavigationOutput? stopped = null;
        for (var second = 1; second <= 5; second++)
            stopped = controller.Observe(isolated, now.AddSeconds(second), automaticWalkActive: true) ?? stopped;
        Equal(true, stopped?.StopAutoWalk == true, "the convergence deadline audibly stops unreachable automatic movement");
        Equal("Auto walk stopped. Could not get closer to Connected ground. Regular navigation is still on.",
            stopped?.Speech, "an unreachable first waypoint receives the existing explicit failure explanation");
        Equal(true, controller.BeaconEnabled, "the failure explanation preserves manual navigation");
    }

    private static void WalksTheCompleteInstalledCostaRouteWithNativeDirectionalSteps()
    {
        var (map, catalog) = Load();
        var target = catalog.Locations.Single(target => target.Label == "Mt. Corel");
        foreach (var camera in new[] { 0, 1024, 3388 })
        foreach (var sampleDistance in new[] { 60, 120 })
        {
            var planner = new WorldMapRoutePlanner(map);
            var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
            var state = State(140807, 233, 126130, camera);
            var now = new DateTime(2026, 9, 6, 14, 11, 42, DateTimeKind.Utc);
            Equal(true, planner.TryResolvePlayerTriangle(state, out var triangle), "native replay starts on grass");
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
            var arrived = false;
            var arrivalFrame = -1;
            var recent = new Queue<string>();
            for (var frame = 0; frame < 2500; frame++)
            {
                var output = controller.Observe(state, now.AddMilliseconds(frame * 80), automaticWalkActive: true);
                Equal(false, output?.StopAutoWalk == true,
                    $"native Costa replay camera={camera}, step={sampleDistance}, frame={frame}, " +
                    $"player={state.X},{state.Y},{state.Z}, triangle={triangle}, waypoint={controller.Probe.WaypointIndex}: {output?.Speech}; " +
                    string.Join(" / ", recent));
                if (!controller.BeaconEnabled)
                {
                    arrived = target.HasArrived(state, triangle);
                    arrivalFrame = frame;
                    break;
                }
                Equal(true, controller.TryResolveAutomaticInput(state, out var input), "directional replay retains input");
                recent.Enqueue($"{state.X},{state.Z} {input} to {controller.Probe.Route!.Waypoints[controller.Probe.WaypointIndex]}");
                if (recent.Count > 8) recent.Dequeue();
                var (dx, dz) = NativeStep(input, camera, sampleDistance);
                state = MoveOnRawNativeTriangles(map, state, ref triangle, dx, dz);
            }
            Equal(true, arrived, $"complete Costa to Mount Corel arrival camera={camera}, step={sampleDistance}");
            Console.WriteLine($"PASS native Costa world replay: camera={camera}, step={sampleDistance}, " +
                $"frames={arrivalFrame}, native arrival triangle={triangle}, script={state.TerrainScriptId}.");
        }
    }

    private static void ReplansAfterSustainedAutomaticDeviationWithoutDroppingTheDestination()
    {
        var (map, catalog) = Load();
        var planner = new WorldMapRoutePlanner(map);
        var target = catalog.Locations.Single(target => target.Label == "Mt. Corel");
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        var now = new DateTime(2026, 9, 6, 14, 13, 42, DateTimeKind.Utc);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, State(131517, 584, 134924, 3452), now);
        var initialRoute = controller.Probe.Route;
        var backAtCosta = State(140807, 233, 126130, 0);
        controller.Observe(backAtCosta, now.AddSeconds(1), automaticWalkActive: true);
        Equal(true, ReferenceEquals(initialRoute, controller.Probe.Route),
            "automatic corner recovery retains the committed route on a single off-route sample");
        var replanned = controller.Observe(backAtCosta, now.AddSeconds(7), automaticWalkActive: true);
        Equal(false, ReferenceEquals(initialRoute, controller.Probe.Route),
            "sustained automatic deviation still rebuilds the route from the accepted position");
        Equal(false, replanned?.StopAutoWalk == true, "the sustained deviation does not falsely fail-stop");
        Equal(true, controller.BeaconEnabled, "the replan preserves the Mount Corel destination");
        Equal(true, controller.TryResolveAutomaticInput(backAtCosta, out _),
            "automatic movement resumes along the replanned native route");
    }

    private static (double X, double Z) NativeStep(FieldNavigationInput input, int camera, int distance)
    {
        var x = input is FieldNavigationInput.Left or FieldNavigationInput.UpLeft or FieldNavigationInput.DownLeft ? -1 :
            input is FieldNavigationInput.Right or FieldNavigationInput.UpRight or FieldNavigationInput.DownRight ? 1 : 0;
        var z = input is FieldNavigationInput.Up or FieldNavigationInput.UpLeft or FieldNavigationInput.UpRight ? -1 :
            input is FieldNavigationInput.Down or FieldNavigationInput.DownLeft or FieldNavigationInput.DownRight ? 1 : 0;
        // FUN0074EA48 assigns the four native direction axes, scales diagonals
        // by3/4 and then applies the negative camera-front Y rotation. This
        // oracle does not use the production controller transform or formatter.
        var scale = x != 0 && z != 0 ? distance * 0.75d : distance;
        var angle = camera * Math.PI * 2d / 4096d;
        return ((x * Math.Cos(angle) - z * Math.Sin(angle)) * scale,
            (x * Math.Sin(angle) + z * Math.Cos(angle)) * scale);
    }

    private static WorldMapStateSnapshot MoveOnRawNativeTriangles(WorldMapData map,
        WorldMapStateSnapshot state, ref int triangle, double dx, double dz)
    {
        var count = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dz))));
        var startX = state.X;
        var startZ = state.Z;
        var currentX = (double)startX;
        var currentZ = (double)startZ;
        for (var step = 1; step <= count; step++)
        {
            var nextX = Math.Round(startX + dx * step / count);
            var nextZ = Math.Round(startZ + dz * step / count);
            var nextTriangle = ResolveRawNeighbor(map, triangle, currentX, currentZ, nextX, nextZ);
            if (nextTriangle < 0) break;
            triangle = nextTriangle;
            currentX = nextX;
            currentZ = nextZ;
        }
        var native = map.Triangles[triangle];
        // Truncate the accepted movement like native integer world positions.
        var x = (int)currentX;
        var z = (int)currentZ;
        var a = native.Vertex0;
        var b = native.Vertex1;
        var c = native.Vertex2;
        var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
        var wa = ((b.Z - c.Z) * (double)(x - c.X) + (c.X - b.X) * (double)(z - c.Z)) / denominator;
        var wb = ((c.Z - a.Z) * (double)(x - c.X) + (a.X - c.X) * (double)(z - c.Z)) / denominator;
        return state with { X = x, Z = z, Y = (int)Math.Round(wa * a.Y + wb * b.Y + (1d - wa - wb) * c.Y),
            TerrainId = native.TerrainId, RegionId = native.RegionId & 31, TerrainScriptId = native.TerrainScriptId };
    }

    private static int ResolveRawNeighbor(WorldMapData map, int initial,
        double fromX, double fromZ, double toX, double toZ)
    {
        var queue = new Queue<int>();
        var visited = new HashSet<int> { initial };
        queue.Enqueue(initial);
        while (queue.TryDequeue(out var id))
        {
            var triangle = map.Triangles[id];
            // Walking grass, dirt, tracks and ordinary ground only. Mountain,
            // ocean and vertical cliff faces remain impassable in this oracle.
            if (triangle.TerrainId is 2 or 3 or 4 or 5 or 6 or 12 or 15 or 18 or 22 or 23 or 26 or 31) continue;
            if (Math.Abs(Side(triangle.Vertex0.X, triangle.Vertex0.Z, triangle.Vertex1.X, triangle.Vertex1.Z,
                triangle.Vertex2.X, triangle.Vertex2.Z)) <= 1e-7) continue;
            var sides = triangle.Edges.Select(edge => Side(edge.Start.X, edge.Start.Z,
                edge.End.X, edge.End.Z, toX, toZ)).ToArray();
            if (!sides.Any(side => side < -1e-7) || !sides.Any(side => side > 1e-7)) return id;
            foreach (var neighborId in triangle.Neighbors)
            {
                if (visited.Contains(neighborId)) continue;
                var neighbor = map.Triangles[neighborId];
                var shared = triangle.Edges.FirstOrDefault(edge => neighbor.Edges.Any(other =>
                    edge.Start == other.Start && edge.End == other.End || edge.Start == other.End && edge.End == other.Start));
                if (shared.Start == shared.End || !SegmentsTouch(fromX, fromZ, toX, toZ,
                    shared.Start.X, shared.Start.Z, shared.End.X, shared.End.Z)) continue;
                visited.Add(neighborId);
                queue.Enqueue(neighborId);
            }
        }
        return -1;
    }

    private static bool SegmentsTouch(double ax, double az, double bx, double bz,
        double cx, double cz, double dx, double dz) =>
        Math.Max(ax, bx) >= Math.Min(cx, dx) - 1e-7 && Math.Max(cx, dx) >= Math.Min(ax, bx) - 1e-7 &&
        Math.Max(az, bz) >= Math.Min(cz, dz) - 1e-7 && Math.Max(cz, dz) >= Math.Min(az, bz) - 1e-7 &&
        Side(ax, az, bx, bz, cx, cz) * Side(ax, az, bx, bz, dx, dz) <= 1e-7 &&
        Side(cx, cz, dx, dz, ax, az) * Side(cx, cz, dx, dz, bx, bz) <= 1e-7;

    private static double Side(double ax, double az, double bx, double bz, double px, double pz) =>
        (bx - ax) * (pz - az) - (bz - az) * (px - ax);

    private static (WorldMapData Map, WorldMapTargetCatalog Catalog) Load()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            @"C:\Games\Final Fantasy VII\workingdir";
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
            @"C:\FF7A11Y\accessibility_prototype";
        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 0);
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return (map, catalog);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
    }
}
