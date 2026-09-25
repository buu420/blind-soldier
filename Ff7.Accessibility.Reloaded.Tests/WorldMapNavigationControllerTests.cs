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
        NativeDialogueDoesNotConsumeTheAutomaticMovementTimeout();
        AllowsAutomaticWalkingThatKeepsMakingProgress();
        KeepsTheRouteAcrossNearbyWalkableTriangleDrift();
        DoesNotReannounceAParkedVehicleThatHasNotMoved();
        StillReplansWhenAVehicleActuallyMoves();
        StandingOnAnIntermediateWaypointIsNotArrival();
        CosmoCanyonHandsOffToFootInsteadOfClaimingArrival();
        TheCosmoEntryRuleCoversStorySelectionAndRepeat();
        TheNativeVehicleMaskRefusesTheApproachTheUserStalledOn();
        TheParkedBuggyIsWalkedAroundInsteadOfPressedInto();
        AnEntranceWithNoClearApproachIsNotClaimedAsProgress();
        WorldMapVehicleApproachReplayTests.Run();
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

    private static void NativeDialogueDoesNotConsumeTheAutomaticMovementTimeout()
    {
        var (map, catalog, planner) = Load();
        var state = StateAt(map, catalog.Locations.Single(target => target.Label == "Kalm"));
        var destination = catalog.Locations.Single(target => target.Label == "Chocobo Farm");
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [destination]);
        var now = new DateTime(2026, 9, 25, 14, 0, 0, DateTimeKind.Utc);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
        for (var sample = 0; sample <= 8; sample++)
            controller.Observe(state, now.AddMilliseconds(sample * 500), automaticWalkActive: true);
        controller.PauseForNativeControl();
        var resumed = controller.Observe(state, now.AddMilliseconds(5500), automaticWalkActive: true);
        Equal(false, resumed?.StopAutoWalk == true,
            "time spent in a short native dialogue is not failed automatic movement");
        Equal(true, controller.BeaconEnabled, "the requested destination survives a native dialogue");
        Equal(true, controller.TryResolveAutomaticInput(state, out _),
            "automatic movement is available again when the host restores control");
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

    /// <summary>
    /// The reported defect: navigating to the parked Buggy repeats "Buggy. down-right 4."
    /// several times a second while the player and the vehicle both stand still.
    ///
    /// <para>Entity targets are rebuilt on every read, and the arrival set the catalog
    /// gives them is the vehicle's own triangle plus its walkable neighbours. The planner
    /// then routes to whichever of those the search reaches first, which is usually a
    /// neighbour rather than the centre. Comparing the vehicle's centre triangle against
    /// the triangle the route ends on therefore reported a move on every tick, and each
    /// replan announces itself outside the guidance interval.</para>
    ///
    /// <para>The other tests hand the controller the same target instance every read, so
    /// the refresh never ran and the repeat never showed. This one rebuilds the target the
    /// way the catalog does.</para>
    /// </summary>
    private static void DoesNotReannounceAParkedVehicleThatHasNotMoved()
    {
        var (map, catalog, planner) = Load();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var start = StateAt(map, midgar);
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [ParkedVehicle(map, kalm)],
            guidanceInterval: TimeSpan.FromMinutes(1));
        var now = DateTime.UtcNow;

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, now);
        var firstRoute = controller.Probe.Route
            ?? throw new InvalidOperationException("the vehicle route was not created");

        var spoken = 0;
        for (var tick = 1; tick <= 12; tick++)
        {
            var observed = controller.Observe(start, now.AddMilliseconds(tick * 100));
            if (!string.IsNullOrWhiteSpace(observed?.Speech))
            {
                spoken++;
            }
        }

        Equal(0, spoken, "a parked vehicle is not re-announced on every observation");
        Equal(
            firstRoute.TargetTriangleId,
            controller.Probe.Route?.TargetTriangleId,
            "and the route it is already following is not rebuilt underneath it");
    }

    /// <summary>
    /// wm0.ev handler ADA4 admits the party to Cosmo Canyon only in player model 0, 1 or
    /// 2. Driving the Buggy onto the entrance triangle satisfies the mod's arrival test
    /// while the game will not open the field, so announcing "Arrived" leaves a blind
    /// player waiting at a door that never opens.
    ///
    /// <para>The handoff says what the game wants, stops the automatic walk, keeps the
    /// destination, and says it once. Nothing dismounts anybody.</para>
    /// </summary>
    private static void CosmoCanyonHandsOffToFootInsteadOfClaimingArrival()
    {
        var (map, catalog, planner) = Load();
        var cosmo = catalog.Locations.Single(target => target.Label == "Cosmo Canyon");
        var entrance = StateAt(map, cosmo);
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

        var inBuggy = entrance with { PlayerModelId = 6 };
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [cosmo],
            guidanceInterval: TimeSpan.FromMinutes(1));

        // Start the route from somewhere else so the walk is genuinely running.
        var approach = StateAt(map, catalog.Locations.Single(target => target.Label == "Gongaga"))
            with { PlayerModelId = 6 };
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, approach, now);

        var handoff = controller.Observe(inBuggy, now.AddSeconds(1), automaticWalkActive: true);
        DoesNotContain("Arrived", handoff?.Speech,
            "the Buggy cannot enter Cosmo Canyon, so nothing has arrived");
        Contains("on foot", handoff?.Speech,
            "the party is told the way in is on foot");
        Equal(true, handoff?.StopAutoWalk, "the automatic walk releases at the entrance");
        Equal(true, controller.BeaconEnabled, "the destination is kept");

        var repeated = controller.Observe(inBuggy, now.AddSeconds(2), automaticWalkActive: true);
        Equal(null, repeated?.Speech, "the handoff is not repeated on every sample");

        // Native script 7 restores the position immediately before the entrance. This
        // rollback must not release the hold or trigger another route announcement.
        var rollback = inBuggy with { X = 87079, Y = 1501, Z = 170461, TerrainId = 19, TerrainScriptId = 0 };
        for (var sample = 0; sample < 3; sample++)
        {
            Equal(null, controller.Observe(rollback, now.AddSeconds(2).AddMilliseconds(sample * 80), true)?.Speech,
                "native rollback does not reannounce the route");
            Equal(false, controller.TryResolveAutomaticInput(rollback, out _), "rollback preserves the input hold");
        }
        Contains("on foot", controller.HandleAction(FieldNavigationAction.RepeatTarget, rollback)?.Speech,
            "repeating after rollback still explains the handoff");
        var preview = new WorldMapNavigationController(map, planner, (_, _) => [cosmo]);
        DoesNotContain("Navigation stays on", preview.HandleAction(FieldNavigationAction.RepeatTarget, inBuggy)?.Speech,
            "browsing never claims to have enabled navigation");

        // An actual on-foot model change replans and then arrival is allowed to happen.
        Equal(false, controller.TryResolveAutomaticInput(inBuggy, out var held),
            "automatic walking yields no input while the party is waiting to dismount");
        Equal(FieldNavigationInput.None, held, "and the held input is explicitly none");

        var onFoot = entrance with { PlayerModelId = 0 };
        var arrived = controller.Observe(onFoot, now.AddSeconds(3));
        Contains("Arrived", arrived?.Speech,
            "once the party is on foot the native entrance is a real arrival");

        var retreat = new WorldMapNavigationController(map, planner, (_, _) => [cosmo]);
        retreat.HandleAction(FieldNavigationAction.ToggleBeacon, inBuggy, now);
        DoesNotContain("entrance", retreat.HandleAction(FieldNavigationAction.RepeatTarget, approach)?.Speech,
            "driving far away cannot repeat a stale entrance claim");
        var resumed = retreat.Observe(approach, now.AddSeconds(4), true);
        Contains("Cosmo Canyon", resumed?.Speech, "an intentional mounted retreat resumes the route");
        Equal(true, retreat.TryResolveAutomaticInput(approach, out _), "retreat releases the entry input hold");
    }

    // The Buggy the user parked at the Cosmo Canyon entrance, and the two on-foot positions
    // their log records afterwards.
    private const int ParkedBuggyX = 87271;
    private const int ParkedBuggyZ = 170546;

    private static WorldMapEntitySnapshot ParkedBuggy(int x = ParkedBuggyX, int z = ParkedBuggyZ) =>
        new(0x1000, 0, IsPlayer: false, x, 1499, z, TerrainId: 16, RegionId: 4, ModelId: 6, Flags: 0);

    /// <summary>
    /// The native mask, checked against the position the user actually stalled on.
    ///
    /// <para>Their log stops at (87533, 1448, 170886) roughly 678 units short of the
    /// entrance. The mask test refuses every step from there that closes the distance to
    /// the car, which is why automatic walking pressed into it until the convergence
    /// watchdog gave up. Moving away is always allowed, exactly as FUN00762A21 does.</para>
    /// </summary>
    private static void TheNativeVehicleMaskRefusesTheApproachTheUserStalledOn()
    {
        const int wrapWidth = 0x48000;
        const int wrapHeight = 0x38000;
        var buggy = ParkedBuggy();

        // Standing where the log stopped is itself legal - the refusal is of the next step.
        Equal(false,
            WorldMapVehicleObstacles.IsBlocked([buggy], 0, 87533, 170886, wrapWidth, wrapHeight),
            "the position the party stalled on is not itself inside the footprint");

        // A step of one native movement toward the car is refused.
        Equal(true,
            WorldMapVehicleObstacles.BlocksSegment(
                [buggy], 0, 87533, 170886, ParkedBuggyX, ParkedBuggyZ, wrapWidth, wrapHeight),
            "walking on into the parked Buggy is refused by the native mask");

        // The entrance the route aims at is underneath the car.
        Equal(true,
            WorldMapVehicleObstacles.IsBlocked([buggy], 0, 87144, 170406, wrapWidth, wrapHeight),
            "the Cosmo Canyon entrance centre is inside the parked Buggy's footprint");

        // Only the verified participants take part.
        Equal(false,
            WorldMapVehicleObstacles.IsBlocked(
                [buggy with { IsPlayer = true }], 0, 87144, 170406, wrapWidth, wrapHeight),
            "the party is never an obstacle to itself");
        Equal(false,
            WorldMapVehicleObstacles.IsBlocked(
                [buggy with { Flags = WorldMapVehicleObstacles.SkippedFlag }],
                0, 87144, 170406, wrapWidth, wrapHeight),
            "an entity the native routine skips is skipped here too");
        Equal(false,
            WorldMapVehicleObstacles.IsBlocked(
                [buggy with { ModelId = 3 }], 0, 87144, 170406, wrapWidth, wrapHeight),
            "a model with no verified mask is not guessed at");
        Equal(false,
            WorldMapVehicleObstacles.IsBlocked([buggy], 3, 87144, 170406, wrapWidth, wrapHeight),
            "and neither is a party model with no verified mask");

        Equal(true, WorldMapVehicleObstacles.BlocksSegment([buggy], 0,
            87335, 170625, 86343, 171217, wrapWidth, wrapHeight),
            "a coarse chord cannot hide the initial inward move");
        Equal(true, WorldMapVehicleObstacles.BlocksSegment([buggy], 0,
            87138, 170038, 87331, 169895, wrapWidth, wrapHeight),
            "a short corner clip between old 128-unit samples remains blocked");
        Equal(false, WorldMapVehicleObstacles.BlocksSegment([buggy], 0,
            86999, 170670, 86631, 170546, wrapWidth, wrapHeight),
            "native outward escape from the dismount overlap is allowed");
        foreach (var (x, z, toX, toZ) in new[] { (86580, 170075, 87084, 171444),
            (86447, 169220, 88355, 171062), (88082, 169271, 86111, 171111),
            (88660, 169149, 86229, 170691), (86478, 169393, 88551, 171055) })
            Equal(true, WorldMapVehicleObstacles.BlocksSegment([buggy], 0, x, z, toX, toZ, wrapWidth, wrapHeight),
                "native steps straddling the closest point cannot slip through a mask");
        // Wrapped coordinates are measured the short way round.
        var wrapped = ParkedBuggy(x: 16);
        Equal(true,
            WorldMapVehicleObstacles.IsBlocked(
                [wrapped], 0, wrapWidth - 16, ParkedBuggyZ, wrapWidth, wrapHeight),
            "the footprint still applies across the world-map seam");
    }

    /// <summary>
    /// The repair: with the car where the user left it, the walk is given a clear point
    /// beside it and a standable part of the entrance, instead of a direction that the
    /// native test will refuse.
    /// </summary>
    private static void TheParkedBuggyIsWalkedAroundInsteadOfPressedInto()
    {
        var (map, catalog, planner) = Load();
        var cosmo = catalog.Locations.Single(target => target.Label == "Cosmo Canyon");
        var detour = new WorldMapVehicleDetourPlanner(map, planner);
        var buggy = ParkedBuggy();
        var goal = new WorldMapRouteWaypoint(cosmo.X, cosmo.Y, cosmo.Z);

        // Both on-foot positions the log records after the dismount.
        foreach (var (x, y, z) in new[] { (87577, 1443, 170926), (86999, 1509, 170670) })
        {
            var onFoot = StateAt(map, cosmo) with { PlayerModelId = 0, X = x, Y = y, Z = z };
            var outcome = detour.Resolve(onFoot, [buggy], cosmo, goal, out var aim);

            NotEqual(WorldMapVehicleDetourPlanner.DetourOutcome.Blocked, outcome,
                $"from {x},{z} there is a way around the parked Buggy");
            Equal(false,
                WorldMapVehicleObstacles.IsBlocked(
                    [buggy], 0, aim.X, aim.Z, map.WrapWidth, map.WrapHeight),
                $"the point chosen from {x},{z} is outside the footprint");
            Equal(false,
                WorldMapVehicleObstacles.BlocksSegment(
                    [buggy], 0, x, z, aim.X, aim.Z, map.WrapWidth, map.WrapHeight),
                $"and the walk to it from {x},{z} does not cross the car");
        }

        // Through the controller, at the camera angles the user's session used: automatic
        // walking still produces a direction, and the step it asks for is one the native
        // mask allows.
        foreach (var camera in new[] { 0, 344, 1256, 3550 })
        {
            var onFoot = StateAt(map, cosmo) with
            {
                PlayerModelId = 0,
                X = 87577,
                Y = 1443,
                Z = 170926,
                // The walkmap under this position: canyon, triangle 106277, native script 0.
                // StateAt copies the entrance's script 7, which the game only reports while
                // the party is standing on the trigger, and arrival is now read from that
                // native word rather than from where the mod resolves the party to be.
                TerrainScriptId = 0,
                CameraFront = camera,
                ControlTransform = new FieldNavigationControlTransform(ScreenAxis(camera))
            };
            var controller = new WorldMapNavigationController(
                map,
                planner,
                (_, _) => [cosmo],
                guidanceInterval: TimeSpan.FromMinutes(1),
                entityProvider: () => [buggy]);
            controller.HandleAction(
                FieldNavigationAction.ToggleBeacon,
                onFoot,
                new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));

            Equal(true, controller.TryResolveAutomaticInput(onFoot, out var input),
                $"camera {camera} still has somewhere to walk with the Buggy parked");
            var (stepX, stepZ) = PredictStep(input, camera);
            Equal(false,
                WorldMapVehicleObstacles.BlocksSegment(
                    [buggy],
                    0,
                    onFoot.X,
                    onFoot.Z,
                    onFoot.X + stepX,
                    onFoot.Z + stepZ,
                    map.WrapWidth,
                    map.WrapHeight),
                $"and the step camera {camera} asks for is not refused by the native mask");
        }

        // With no car in the way the goal is left exactly as the route planned it.
        var clear = StateAt(map, cosmo) with { PlayerModelId = 0, X = 87577, Y = 1443, Z = 170926 };
        Equal(WorldMapVehicleDetourPlanner.DetourOutcome.Clear,
            detour.Resolve(clear, [], cosmo, goal, out var untouched),
            "an empty world leaves the route alone");
        Equal(goal, untouched, "and aims exactly where the route did");
    }

    // The eight-bit screen axis the controller transform uses for a native camera front.
    private static int ScreenAxis(int cameraFront)
    {
        var axis = -(cameraFront / 16);
        return axis < -128 ? axis + 256 : axis;
    }

    // One native movement step for a spoken direction, as FUN0074EA48 applies it: three
    // quarter speed on both diagonal axes, rotated by the camera front.
    private static (int X, int Z) PredictStep(FieldNavigationInput input, int cameraFront)
    {
        var x = input is FieldNavigationInput.Left or FieldNavigationInput.UpLeft or FieldNavigationInput.DownLeft ? -1
            : input is FieldNavigationInput.Right or FieldNavigationInput.UpRight or FieldNavigationInput.DownRight ? 1
            : 0;
        var z = input is FieldNavigationInput.Up or FieldNavigationInput.UpLeft or FieldNavigationInput.UpRight ? -1
            : input is FieldNavigationInput.Down or FieldNavigationInput.DownLeft or FieldNavigationInput.DownRight ? 1
            : 0;
        var scale = x != 0 && z != 0 ? 60 * 0.75d : 60d;
        var angle = cameraFront * Math.PI * 2d / 4096d;
        return (
            (int)Math.Round((x * Math.Cos(angle) - z * Math.Sin(angle)) * scale),
            (int)Math.Round((x * Math.Sin(angle) + z * Math.Cos(angle)) * scale));
    }

    /// <summary>
    /// If there is genuinely nowhere clear to stand, the walk stops rather than reporting
    /// progress it cannot make.
    /// </summary>
    private static void AnEntranceWithNoClearApproachIsNotClaimedAsProgress()
    {
        var (map, catalog, planner) = Load();
        var cosmo = catalog.Locations.Single(target => target.Label == "Cosmo Canyon");
        var detour = new WorldMapVehicleDetourPlanner(map, planner);

        // A car parked on every part of every native entrance triangle.
        var covered = cosmo.ArrivalTriangleIds
            .Select(id => map.Triangles[id].Centroid)
            .SelectMany(centre => new[]
            {
                ParkedBuggy(centre.X, centre.Z),
                ParkedBuggy(centre.X + 200, centre.Z),
                ParkedBuggy(centre.X - 200, centre.Z),
                ParkedBuggy(centre.X, centre.Z + 200),
                ParkedBuggy(centre.X, centre.Z - 200)
            })
            .ToArray();
        var onFoot = StateAt(map, cosmo) with { PlayerModelId = 0, X = 87577, Y = 1443, Z = 170926 };

        Equal(WorldMapVehicleDetourPlanner.DetourOutcome.Blocked,
            detour.Resolve(
                onFoot,
                covered,
                cosmo,
                new WorldMapRouteWaypoint(cosmo.X, cosmo.Y, cosmo.Z),
                out _),
            "an entrance with nowhere standable is reported blocked, not quietly walked at");
    }

    /// <summary>
    /// The user's failing route was the Story target, not the Locations one. The catalog
    /// hands the same place out under both, so the native entry rule has to answer for
    /// both - and selecting or repeating it while parked on the entrance must not claim an
    /// arrival or quietly drop the destination.
    /// </summary>
    private static void TheCosmoEntryRuleCoversStorySelectionAndRepeat()
    {
        var (map, catalog, planner) = Load();
        var location = catalog.Locations.Single(target => target.Label == "Cosmo Canyon");
        var story = location with
        {
            Category = WorldMapNavigationCategory.Story,
            Kind = WorldMapTargetKind.Story,
            StableId = "world-story:cosmo-canyon"
        };
        var entrance = StateAt(map, location) with { PlayerModelId = 6 };
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [story],
            guidanceInterval: TimeSpan.FromMinutes(1));

        // Selecting it while already parked on the entrance.
        var selected = controller.HandleAction(FieldNavigationAction.ToggleBeacon, entrance, now);
        DoesNotContain("Arrived", selected?.Speech,
            "a Story destination the Buggy cannot enter has not been arrived at");
        Contains("on foot", selected?.Speech, "the Story route gives the same handoff");
        Equal(true, controller.BeaconEnabled, "and the destination is kept, not dropped");

        // Repeating it must not claim arrival either.
        var repeated = controller.HandleAction(
            FieldNavigationAction.RepeatTarget,
            entrance,
            now.AddSeconds(1));
        DoesNotContain("at destination", repeated?.Speech,
            "repeating a refused entrance does not report arrival");
        DoesNotContain("Arrived", repeated?.Speech,
            "repeating a refused entrance does not report arrival");

        // Leaving the vehicle resumes and then genuinely arrives.
        var arrived = controller.Observe(
            entrance with { PlayerModelId = 0 },
            now.AddSeconds(2));
        Contains("Arrived", arrived?.Speech, "on foot the same entrance is a real arrival");
    }

    /// <summary>
    /// Reported from the user's own log: at 52% of the Cosmo Canyon route, standing at
    /// (80909, 346, 178480) on waypoint 17, the mod said "Cosmo Canyon. at destination."
    ///
    /// <para>Standing on an intermediate corner is not arriving. The segment resolver
    /// reports nothing when the party is within a unit of the corner it is measuring from,
    /// and the world formatter was reading that silence as arrival. With route left to
    /// walk it has to keep saying something the player can act on; whether they have
    /// actually arrived is the controller's native test to make, not the formatter's.</para>
    /// </summary>
    private static void StandingOnAnIntermediateWaypointIsNotArrival()
    {
        var onCorner = State(x: 1000, z: 1000) with
        {
            ControlTransform = new FieldNavigationControlTransform(0)
        };
        var run = WorldMapConnectedRunFormatter.Resolve(
            new WorldMapRouteWaypoint(1000, 0, 1000),
            [
                // The corner the party is standing on, and then a run of legs each
                // shorter than half a spoken count, as the approach to a town entrance is.
                new WorldMapRouteWaypoint(1000, 0, 1000),
                new WorldMapRouteWaypoint(1060, 0, 1040),
                new WorldMapRouteWaypoint(1120, 0, 1080)
            ],
            0,
            onCorner,
            0x48000,
            0x38000,
            512);

        DoesNotContain("at destination", run.Speech,
            "standing on an intermediate corner is not arriving at the destination");
        NotEqual(string.Empty, run.Direction,
            "the player is still given a direction they can act on");

        // The same shape, but the short legs turn a corner. Those corners exist because
        // the ground between them is not walkable, so the answer has to be the next one,
        // not a straight line to the far end of the chain.
        var turning = WorldMapConnectedRunFormatter.Resolve(
            new WorldMapRouteWaypoint(1000, 0, 1000),
            [
                new WorldMapRouteWaypoint(1000, 0, 1000),
                new WorldMapRouteWaypoint(1000, 0, 1100),
                new WorldMapRouteWaypoint(900, 0, 1100),
                new WorldMapRouteWaypoint(900, 0, 1000)
            ],
            0,
            onCorner,
            0x48000,
            0x38000,
            512);

        Equal(1, turning.EndWaypointIndex,
            "a turning chain is answered one corner at a time, in order");
        DoesNotContain("at destination", turning.Speech,
            "a turning chain of short legs is not an arrival either");
    }

    /// <summary>
    /// The other half: a vehicle that genuinely drives away must still be replanned to.
    /// </summary>
    private static void StillReplansWhenAVehicleActuallyMoves()
    {
        var (map, catalog, planner) = Load();
        var midgar = catalog.Locations.Single(target => target.Label == "Midgar");
        var kalm = catalog.Locations.Single(target => target.Label == "Kalm");
        var current = ParkedVehicle(map, kalm);
        var start = StateAt(map, midgar);
        var controller = new WorldMapNavigationController(
            map,
            planner,
            (_, _) => [current],
            guidanceInterval: TimeSpan.FromMinutes(1));
        var now = DateTime.UtcNow;

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, now);
        var before = controller.Probe.Route
            ?? throw new InvalidOperationException("the vehicle route was not created");

        // Drive it back along its own route, to a triangle that is provably reachable and
        // is neither where the route ends nor next to it, so the old arrival really has
        // stopped being one.
        var start1 = map.Triangles[before.StartTriangleId];
        var drivenTo = before.TrianglePath
            .Select(id => map.Triangles[id])
            .First(triangle =>
                triangle.Id != before.TargetTriangleId &&
                !triangle.Neighbors.Contains(before.TargetTriangleId) &&
                triangle.Id != before.StartTriangleId &&
                !triangle.Neighbors.Contains(before.StartTriangleId) &&
                !start1.Neighbors.Contains(triangle.Id));
        current = ParkedVehicle(map, drivenTo, kalm.RegionId);
        controller.Observe(start, now.AddSeconds(1));
        var after = controller.Probe.Route
            ?? throw new InvalidOperationException("the moved vehicle lost its route");

        NotEqual(
            before.TargetTriangleId,
            after.TargetTriangleId,
            "a vehicle that has driven somewhere else is replanned to");
    }

    /// <summary>
    /// A stationary entity target shaped exactly as WorldMapTargetCatalog.CreateEntityTarget
    /// builds one: the same identity and position every time, in a new instance whose
    /// arrival set is its own triangle plus the walkable neighbours, in a new set.
    /// </summary>
    private static WorldMapNavigationTarget ParkedVehicle(
        WorldMapData map,
        WorldMapNavigationTarget at) =>
        ParkedVehicle(map, map.Triangles[at.TriangleId], at.RegionId, at.X, at.Y, at.Z);

    private static WorldMapNavigationTarget ParkedVehicle(
        WorldMapData map,
        WorldMapTriangle triangle,
        int regionId) =>
        ParkedVehicle(
            map,
            triangle,
            regionId,
            triangle.Centroid.X,
            triangle.Centroid.Y,
            triangle.Centroid.Z);

    private static WorldMapNavigationTarget ParkedVehicle(
        WorldMapData map,
        WorldMapTriangle triangle,
        int regionId,
        int x,
        int y,
        int z)
    {
        var arrivals = new HashSet<int> { triangle.Id };
        foreach (var neighbor in triangle.Neighbors)
        {
            if (WorldMapTerrainPassability.CanTraverse(0, 0, map.Triangles[neighbor].TerrainId))
            {
                arrivals.Add(neighbor);
            }
        }

        return new WorldMapNavigationTarget(
            WorldMapNavigationCategory.Transportation,
            WorldMapTargetKind.Transportation,
            "Buggy",
            x,
            y,
            z,
            triangle.Id,
            regionId,
            "world-entity:transportation:6",
            arrivals);
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

    private static void NotEqual<T>(T unexpected, T actual, string label)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        {
            throw new InvalidOperationException($"{label}: expected something other than {actual}");
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
