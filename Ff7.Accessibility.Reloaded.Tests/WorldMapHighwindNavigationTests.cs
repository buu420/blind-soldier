using System.Buffers.Binary;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Navigating the Highwind to a destination: fly to a grass spot where the game's own landing
/// sets the party down on ground that leads there, ask the player to land, and walk on.
///
/// <para>2026-09-29 x64 log, 12:51-12:57: auto walk to Mideel pressed directions in the
/// Highwind (model 3) and the ship turned and changed height but did not move, eleven times
/// ending "Auto walk stopped. Could not get closer to Mideel." FUN_0074EA48 in flight camera 3
/// turns on Left/Right, climbs and descends on Up/Down, and only translates on the directions
/// while the 0x80 action (control slot 7) is held. The first route also ended on Mideel's own
/// triangles, over the town, which the game never enters from the air.</para>
/// </summary>
internal static class WorldMapHighwindNavigationTests
{
    private const int FlightAltitude = 3977;

    /// <summary>12:55:44, the Highwind west of Mideel after "route Mideel", camera 112.</summary>
    private static readonly (int X, int Z, int Camera) LoggedFlight = (207590, 179729, 112);

    public static void Run()
    {
        FlightHoldsTheSlotSevenActionWithItsDirections();
        TheSlotSevenActionIsReleasedWithTheDirections();
        FlightFailsClosedWithoutAKeyForTheAction();
        TheBrakeIsTheActionAloneAndEveryReleaseLetsItGo();
        Console.WriteLine("world map Highwind input tests passed.");
    }

    public static void RunWithInstalledGameData()
    {
        if (!TryLoadInstalledWorld(out var map, out var catalog))
        {
            Console.WriteLine("world Highwind navigation: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        var mideel = catalog.Locations.Single(target => target.Label == "Mideel");
        TheRouteEndsOverGrassJoinedOnFootNotOverTheTown(map, mideel);
        FlyingOverTheTownIsNotArriving(map, mideel);
        FlightSteersWithTheSlotSevenAction(map, mideel);
        AtTheSpotItHoldsAndAsksForAManualLanding(map, mideel);
        AfterLandingItWalksOnToTheDestination(map, mideel);
        ThePartyWalksOffTheParkedShip(map, mideel);
        TheHighwindsOwnStoryStopStillFlies(map, catalog);
        AWalkOrABoatIsUnchanged(map, mideel);
        EveryTownFromTheLoggedFlightLandsOrSaysItCannot(map, catalog);
        if (TryLoadInstalledWorld(out var progressTwo, out var progressTwoCatalog, worldProgress: 2))
        {
            NativeFlightReachesTheSpotAndComesToRest(progressTwo, progressTwoCatalog);
            ASpokenOnlyPilotIsWaitedFor(progressTwo, progressTwoCatalog);
        }

        Console.WriteLine("world map Highwind navigation tests passed with installed game data.");
    }

    /// <summary>
    /// The route the ship is flown along ends on a spot the native landing accepts, not on the
    /// town: its footprint over grass, and every rotation's get-off on ground joined on foot
    /// to Mideel, proved by the planner's own route on foot.
    /// </summary>
    private static void TheRouteEndsOverGrassJoinedOnFootNotOverTheTown(WorldMapData map, WorldMapNavigationTarget mideel)
    {
        var planner = new WorldMapRoutePlanner(map);
        var start = Flying(map, LoggedFlight.X, LoggedFlight.Z, LoggedFlight.Camera);
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [mideel]);
        var spoken = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start)?.Speech ?? string.Empty;
        Equal(true, controller.BeaconEnabled, $"navigation to Mideel starts in the Highwind: {spoken} ({controller.LastDiagnostic})");
        var route = controller.Probe.Route ?? throw new InvalidOperationException("no Highwind route");
        var end = route.Waypoints[^1];
        Equal(false, mideel.ArrivalTriangleIds.Contains(route.TargetTriangleId),
            $"the flight does not end over the town ({controller.LastDiagnostic})");
        Equal(true, WorldMapHighwindLanding.HasShipFootprint(map, end.X, end.Z), "the ship's footprint is over grass");
        var components = WorldMapBroncoLanding.DestinationFootComponents(planner, map, mideel);
        Equal(true, WorldMapHighwindLanding.FitsEveryRotation(map, planner, end.X, end.Z, components, out var failure),
            $"every rotation sets the party down on Mideel's ground: {failure}");
        foreach (var rotation in new[] { 0, 512, 1024, 1536, 2048, 2560, 3072, 3584 })
        {
            Equal(true, WorldMapHighwindLanding.TryPredictDisembark(map, end.X, end.Z, rotation, out var ground, out var x, out var z),
                $"rotation {rotation} gets off");
            Equal(true, planner.TryBuildRoute(OnFoot(map, x, z, ground), mideel, out _),
                $"and from {x},{z} Mideel is walked to: {planner.LastDiagnostic}");
        }

        Equal(true, spoken.Contains("land", StringComparison.OrdinalIgnoreCase),
            $"the route is announced as a landing: {spoken}");
        Equal(true, controller.LastDiagnostic.Contains("fits within 512", StringComparison.Ordinal),
            $"and the landing still works wherever near the spot the easing ship comes to rest ({controller.LastDiagnostic})");
        Console.WriteLine($"world Highwind: Mideel {controller.LastDiagnostic}");
    }

    /// <summary>
    /// Over the town's own triangles in the air nothing is entered, so nothing is announced as
    /// arrived and the destination is kept.
    /// </summary>
    private static void FlyingOverTheTownIsNotArriving(WorldMapData map, WorldMapNavigationTarget mideel)
    {
        var planner = new WorldMapRoutePlanner(map);
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [mideel]);
        var start = Flying(map, LoggedFlight.X, LoggedFlight.Z, LoggedFlight.Camera);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start);
        var over = map.Triangles[mideel.TriangleId].Centroid;
        var overTown = Flying(map, over.X, over.Z, LoggedFlight.Camera);
        var spoken = controller.Observe(overTown, automaticWalkActive: true)?.Speech ?? string.Empty;
        Equal(false, spoken.Contains("Arrived", StringComparison.Ordinal), $"flying over Mideel is not arriving: {spoken}");
        Equal(true, controller.BeaconEnabled, "and navigation stays on");
        Equal("Mideel", controller.Probe.TargetLabel, "for Mideel");
        var selected = new WorldMapNavigationController(map, planner, (_, _) => [mideel])
            .HandleAction(FieldNavigationAction.ToggleBeacon, overTown)?.Speech ?? string.Empty;
        Equal(false, selected.Contains("Arrived", StringComparison.Ordinal),
            $"asking for Mideel while over it is not arriving either: {selected}");
    }

    /// <summary>In flight every automatic direction comes with the 0x80 action held; on foot never.</summary>
    private static void FlightSteersWithTheSlotSevenAction(WorldMapData map, WorldMapNavigationTarget mideel)
    {
        var planner = new WorldMapRoutePlanner(map);
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [mideel]);
        var start = Flying(map, LoggedFlight.X, LoggedFlight.Z, LoggedFlight.Camera);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start);
        Equal(true, controller.TryResolveAutomaticInput(start, out var input), $"the ship is flown ({controller.LastDiagnostic})");
        Equal(true, controller.AutomaticInputHoldsFlightAction, $"with the flight action held under {input}");

        // FUN_0074EA48 rotates the strafe vector by the camera exactly as the walking one, so
        // the key chosen must close on the spot the way it would on foot.
        var end = controller.Probe.Route!.Waypoints[controller.Probe.WaypointIndex];
        var (worldX, worldZ) = WorldMapNavigationController.PredictNativeMovement(input, start.CameraFront);
        var dx = (double)WorldMapTargetCatalog.WrappedDelta(start.X, end.X, map.WrapWidth);
        var dz = (double)WorldMapTargetCatalog.WrappedDelta(start.Z, end.Z, map.WrapHeight);
        Equal(true, (worldX * dx + worldZ * dz) / (Math.Sqrt(dx * dx + dz * dz) * Math.Sqrt(worldX * worldX + worldZ * worldZ)) > 0.9,
            $"{input} under camera {start.CameraFront} heads for the next waypoint {end.X},{end.Z}");
    }

    /// <summary>
    /// Near the spot auto walk stops the ship the way the game allows - the flight action held
    /// alone while the eased thrust dies away - lets go, and asks once for the landing when the
    /// ship has stayed put over ground that fits as it is really turned. Auto walk is not
    /// stopped, so it walks on afterwards only if the player left it on.
    /// </summary>
    private static void AtTheSpotItHoldsAndAsksForAManualLanding(WorldMapData map, WorldMapNavigationTarget mideel)
    {
        var planner = new WorldMapRoutePlanner(map);
        var start = Flying(map, LoggedFlight.X, LoggedFlight.Z, LoggedFlight.Camera);
        var t0 = new DateTime(2026, 9, 29, 12, 56, 0, DateTimeKind.Utc);
        (WorldMapNavigationController Controller, WorldMapStateSnapshot Near) Begin(Func<WorldMapStateSnapshot, WorldMapStateSnapshot>? change = null)
        {
            var controller = new WorldMapNavigationController(map, planner, (_, _) => [mideel]);
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, t0);
            var end = controller.Probe.Route!.Waypoints[^1];
            var near = Flying(map, end.X - 100, end.Z, LoggedFlight.Camera);
            return (controller, change?.Invoke(near) ?? near);
        }

        var (controller, nearSpot) = Begin();
        Equal(0, nearSpot.TerrainId, "a hundred units short of the spot is over grass by the live terrain the game tests");
        Equal(null, controller.Observe(nearSpot, t0.AddMilliseconds(100), automaticWalkActive: true)?.Speech,
            "within reach of the spot: stop, say nothing yet");
        Equal(false, controller.TryResolveAutomaticInput(nearSpot, out var braking), $"no direction while it stops ({braking})");
        Equal(true, controller.AutomaticInputHoldsFlightAction, "the flight action alone brakes it");
        Equal(null, controller.Observe(nearSpot, t0.AddMilliseconds(400), automaticWalkActive: true)?.Speech, "still braking");
        controller.TryResolveAutomaticInput(nearSpot, out _);
        Equal(true, controller.AutomaticInputHoldsFlightAction, "the brake is held until the thrust has died away");
        Equal(null, controller.Observe(nearSpot, t0.AddMilliseconds(800), automaticWalkActive: true)?.Speech, "brake let go");
        Equal(false, controller.TryResolveAutomaticInput(nearSpot, out _), "nothing pressed once it is let go");
        Equal(false, controller.AutomaticInputHoldsFlightAction, "not even the flight action, before any landing is asked for");
        WorldMapNavigationOutput? output = null;
        var at = t0.AddMilliseconds(800);
        var samples = 0;
        for (; samples < 10 && output?.Speech is null; samples++)
        {
            at = at.AddMilliseconds(100);
            output = controller.Observe(nearSpot, at, automaticWalkActive: true);
        }

        var spoken = output?.Speech ?? string.Empty;
        Equal(true, spoken.Contains("Cancel", StringComparison.Ordinal) && spoken.Contains("Mideel", StringComparison.Ordinal),
            $"at rest the landing is asked for: {spoken} ({controller.LastDiagnostic})");
        Equal(true, (at - t0.AddMilliseconds(800)).TotalMilliseconds >= 250, "only once it has stayed put");
        Equal(false, output?.StopAutoWalk ?? false, "auto walk is left as the player set it");
        Equal(false, output?.StartAutoWalk ?? false, "and not started either");
        Equal(false, controller.TryResolveAutomaticInput(nearSpot, out _), "nothing is pressed at the spot");
        Equal(false, controller.AutomaticInputHoldsFlightAction, "not even the flight action");
        for (var descent = 1; descent <= 5; descent++)
        {
            Equal(null, controller.Observe(nearSpot with { Y = FlightAltitude - 400 * descent }, at.AddMilliseconds(200 * descent),
                automaticWalkActive: true)?.Speech, "descending in place says nothing more");
        }

        Equal(true, controller.BeaconEnabled, "the destination is kept through the landing");

        // Not over grass by the live terrain: no prompt, it says so and goes on in.
        var (grassless, notGrass) = Begin(near => near with { TerrainId = 1 });
        var heard = string.Empty;
        for (var step = 1; step <= 20 && !heard.Contains("Not over the landing ground", StringComparison.Ordinal); step++)
        {
            heard += grassless.Observe(notGrass, t0.AddMilliseconds(100 * step), automaticWalkActive: true)?.Speech ?? string.Empty;
        }

        Equal(false, heard.Contains("Cancel", StringComparison.Ordinal), $"not over grass, no landing prompt: {heard}");
        Equal(true, heard.Contains("Not over the landing ground", StringComparison.Ordinal), $"it says so: {heard}");
        Equal(true, grassless.TryResolveAutomaticInput(notGrass, out _), "and the way in is taken up again");

        // No readable rotation: the side the party is set down on cannot be checked, so no promise.
        var (blind, unturned) = Begin(near => near with { HasModelRotation = false });
        heard = SettleAndListen(blind, unturned, t0);
        Equal(false, heard.Contains("Cancel", StringComparison.Ordinal), $"no readable rotation, no landing prompt: {heard}");

        // Auto walk stopped by the player during the brake: the host lets every key go
        // (NavigationAutoWalkController.Stop) and stops asking for input; the landing is still
        // offered once at rest, and nothing asks for auto walk back.
        var (stopped, stopSpot) = Begin();
        stopped.Observe(stopSpot, t0.AddMilliseconds(100), automaticWalkActive: true);
        heard = string.Join(" | ", Enumerable.Range(1, 14)
            .Select(step => stopped.Observe(stopSpot, t0.AddMilliseconds(100 + 100 * step), automaticWalkActive: false))
            .Where(result => result is not null)
            .Select(result => $"{result!.Value.Speech}{(result.Value.StartAutoWalk ? " [start]" : string.Empty)}"));
        Equal(true, heard.Contains("Cancel", StringComparison.Ordinal) && !heard.Contains("[start]", StringComparison.Ordinal),
            $"stopped mid-brake, the landing is still offered and auto walk is not restarted: {heard}");
    }

    /// <summary>Observes a ship standing still near its spot for two seconds and returns everything said.</summary>
    private static string SettleAndListen(WorldMapNavigationController controller, WorldMapStateSnapshot state, DateTime t0) =>
        string.Join(" | ", Enumerable.Range(1, 20)
            .Select(step => controller.Observe(state, t0.AddMilliseconds(100 * step), automaticWalkActive: true)?.Speech)
            .Where(speech => speech is not null));
    /// <summary>
    /// The native get-off leaves the party on foot beside the parked ship: the same
    /// destination is then walked to, and walking needs no flight action.
    /// </summary>
    private static void AfterLandingItWalksOnToTheDestination(WorldMapData map, WorldMapNavigationTarget mideel)
    {
        var planner = new WorldMapRoutePlanner(map);
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [mideel]);
        var start = Flying(map, LoggedFlight.X, LoggedFlight.Z, LoggedFlight.Camera);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start);
        var end = controller.Probe.Route!.Waypoints[^1];
        var atSpot = Flying(map, end.X, end.Z, LoggedFlight.Camera);
        controller.Observe(atSpot, automaticWalkActive: true);
        Equal(true, WorldMapHighwindLanding.TryPredictDisembark(map, end.X, end.Z, 1184, out var ground, out var x, out var z),
            "the logged rotation 1184 gets off");
        var landed = OnFoot(map, x, z, ground) with { CameraFront = LoggedFlight.Camera };
        var spoken = controller.Observe(landed, automaticWalkActive: true)?.Speech ?? string.Empty;
        Equal(true, controller.BeaconEnabled, $"navigation continues on foot: {spoken} ({controller.LastDiagnostic})");
        Equal("Mideel", controller.Probe.TargetLabel, "to the same destination");
        Equal(true, controller.TryResolveAutomaticInput(landed, out _), $"and is walked ({controller.LastDiagnostic})");
        Equal(false, controller.AutomaticInputHoldsFlightAction, "without the flight action");
    }

    /// <summary>
    /// The parked ship keeps the Buggy's native mask (0096DDB0 + 3 * 8) and FUN_00762A21
    /// refuses a step that overlaps it and closes on it. From every side the party can be set
    /// down on, auto walk on foot leaves the ship's reach without one refused step.
    /// </summary>
    private static void ThePartyWalksOffTheParkedShip(WorldMapData map, WorldMapNavigationTarget mideel)
    {
        var planner = new WorldMapRoutePlanner(map);
        var flying = new WorldMapNavigationController(map, planner, (_, _) => [mideel]);
        flying.HandleAction(FieldNavigationAction.ToggleBeacon, Flying(map, LoggedFlight.X, LoggedFlight.Z, LoggedFlight.Camera));
        var spot = flying.Probe.Route!.Waypoints[^1];
        Equal(true, WorldMapVehicleObstacles.HasNativeMask(WorldMapHighwindLanding.HighwindModelId), "the parked ship has its native mask");
        IReadOnlyList<WorldMapEntitySnapshot> parked =
            [new WorldMapEntitySnapshot(0x00E3A108, 0, false, spot.X, spot.Y, spot.Z, 0, 0, WorldMapHighwindLanding.HighwindModelId, 0)];
        foreach (var rotation in new[] { 0, 512, 1024, 1536, 2048, 2560, 3072, 3584 })
        {
            Equal(true, WorldMapHighwindLanding.TryPredictDisembark(map, spot.X, spot.Z, rotation, out var ground, out var x, out var z),
                $"rotation {rotation} gets off");
            var state = OnFoot(map, x, z, ground);
            var controller = new WorldMapNavigationController(map, planner, (_, _) => [mideel], entityProvider: () => parked);
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, state);
            var steps = 0;
            for (; steps < 200 && WithinReach(map, state, spot); steps++)
            {
                Equal(true, controller.TryResolveAutomaticInput(state, out var key),
                    $"rotation {rotation}, step {steps} at {state.X},{state.Z}: a key ({controller.LastDiagnostic})");
                var (worldX, worldZ) = WorldMapNavigationController.PredictNativeMovement(key, state.CameraFront);
                var nextX = state.X + (int)Math.Round(worldX * 30d);
                var nextZ = state.Z + (int)Math.Round(worldZ * 30d);
                Equal(false, WorldMapVehicleObstacles.BlocksSegment(parked, 0, state.X, state.Z, nextX, nextZ, map.WrapWidth, map.WrapHeight),
                    $"rotation {rotation}, step {steps}: {key} from {state.X},{state.Z} is not refused by the parked ship");
                Equal(true, WorldMapBroncoLanding.TryFindSurfaceNear(map, nextX, nextZ, state.Y, out var next), "still on the map");
                state = OnFoot(map, nextX, nextZ, next);
                controller.Observe(state, automaticWalkActive: true);
            }

            Equal(false, WithinReach(map, state, spot), $"rotation {rotation}: the party is clear of the ship after {steps} steps");
        }
    }

    /// <summary>
    /// The tester's own flight (x64, world progress 2, Story Mideel at 1050): the Highwind at
    /// 218693,1281,204287, where the pre-fix route stalled turning Left and Right forever 50
    /// units from a ground-height waypoint (root-flight-replay4). Flown natively from there -
    /// FUN_0074EA48's eased thrust, (3 * previous + requested) &gt;&gt; 2 a frame; full thrust on
    /// both strafe axes; and, without the flight action, normal flight carrying on forward on
    /// whatever thrust is left - at six headings and one, three and six frames a sample, on an
    /// advancing clock. Each must reach the spot, come to rest with the thrust spent, and hear
    /// the landing asked for exactly once, over ground that lands at Mideel.
    /// </summary>
    private static void NativeFlightReachesTheSpotAndComesToRest(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var failures = new List<string>();
        foreach (var camera in new[] { 0, 112, 1024, 2048, 3388, 4032 })
        foreach (var framesPerSample in new[] { 1, 3, 6 })
        {
            var (controller, flight, mideel, planner) = StartLoggedFlight(map, catalog, camera);
            var now = new DateTime(2026, 9, 29, 21, 0, 0, DateTimeKind.Utc);
            var prompts = 0;
            var promptSample = -1;
            var still = 0;
            string? failure = null;
            for (var sample = 0; sample < 3000 && failure is null; sample++)
            {
                var before = (flight.State.X, flight.State.Z);
                var output = controller.Observe(flight.State, now.AddMilliseconds(sample * framesPerSample * 1000d / 30d), automaticWalkActive: true);
                if (output?.StopAutoWalk == true || output?.StartAutoWalk == true)
                {
                    failure = $"auto walk was stopped or restarted: {output?.Speech}";
                    break;
                }

                if (output?.Speech?.Contains("press Cancel", StringComparison.Ordinal) == true)
                {
                    prompts++;
                    promptSample = sample;
                    if (flight.Thrust != 0 || flight.MovedLastSample)
                    {
                        failure = $"asked to land while still moving (thrust {flight.Thrust})";
                    }
                    else if (!WorldMapHighwindLanding.IsReadyToLand(map, planner, flight.State,
                                 WorldMapBroncoLanding.DestinationFootComponents(planner, map, mideel)))
                    {
                        failure = "asked to land where the landing does not fit";
                    }
                }

                var driven = controller.TryResolveAutomaticInput(flight.State, out var direction);
                var action = controller.AutomaticInputHoldsFlightAction;
                if (prompts > 0 && (driven || action))
                {
                    failure = $"something is still pressed after the landing was asked for ({direction}, action {action})";
                }

                flight.Fly(map, driven ? direction : FieldNavigationInput.None, action, framesPerSample);
                still = prompts > 0 && !flight.MovedLastSample ? still + 1 : 0;
                if (still >= 12)
                {
                    break;
                }
            }

            failure ??= prompts != 1 ? $"asked to land {prompts} times" : still < 12 ? "never came to rest after the prompt" : null;
            var line = $"camera {camera}, {framesPerSample} frame(s)/sample: prompt at sample {promptSample}, at {flight.State.X},{flight.State.Z}; {controller.LastDiagnostic}";
            Console.WriteLine($"world Highwind native flight: {line}");
            if (failure is not null)
            {
                failures.Add($"{line} - {failure}");
            }
        }

        Equal(0, failures.Count, $"native flights that did not end at rest over the landing: {string.Join(" || ", failures)}");
    }

    /// <summary>
    /// Spoken-only navigation drives nothing: the player flies (here, holding the flight action
    /// toward the spot until near it, then letting go of everything, so the ship coasts on in
    /// normal flight). Nothing is ever asked of the auto-walk input, the landing is never
    /// offered while the ship is moving, and once it has stopped the player hears either the
    /// landing or that this is not landing ground.
    /// </summary>
    private static void ASpokenOnlyPilotIsWaitedFor(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        foreach (var camera in new[] { 0, 2048 })
        {
            var (controller, flight, _, _) = StartLoggedFlight(map, catalog, camera);
            var spot = controller.Probe.Route!.Waypoints[^1];
            var now = new DateTime(2026, 9, 29, 21, 30, 0, DateTimeKind.Utc);
            var heard = new List<string>();
            var letGo = false;
            for (var sample = 0; sample < 2000; sample++)
            {
                var output = controller.Observe(flight.State, now.AddMilliseconds(sample * 100), automaticWalkActive: false);
                Equal(false, output?.StopAutoWalk == true || output?.StartAutoWalk == true, "spoken-only never touches auto walk");
                if (output?.Speech is { } speech)
                {
                    heard.Add(speech);
                    Equal(false, speech.Contains("press Cancel", StringComparison.Ordinal) && (flight.Thrust != 0 || flight.MovedLastSample),
                        $"camera {camera}: the landing is not offered while the ship still moves");
                }

                var dx = (double)WorldMapTargetCatalog.WrappedDelta(flight.State.X, spot.X, map.WrapWidth);
                var dz = (double)WorldMapTargetCatalog.WrappedDelta(flight.State.Z, spot.Z, map.WrapHeight);
                letGo |= dx * dx + dz * dz < 400d * 400d;
                var key = letGo ? FieldNavigationInput.None : Toward(dx, dz, camera);
                flight.Fly(map, key, flightAction: !letGo, frames: 3);
                if (letGo && flight.Thrust == 0 && heard.Any(said => said.Contains("press Cancel", StringComparison.Ordinal) ||
                                                                     said.Contains("Not over the landing ground", StringComparison.Ordinal)))
                {
                    break;
                }
            }

            Equal(true, letGo, $"camera {camera}: the pilot reached the spot");
            Equal(true, heard.Count(said => said.Contains("press Cancel", StringComparison.Ordinal)) <= 1, "asked at most once");
            Equal(true, heard.Any(said => said.Contains("press Cancel", StringComparison.Ordinal) ||
                                          said.Contains("Not over the landing ground", StringComparison.Ordinal)),
                $"camera {camera}: at rest the pilot is told whether to land: {string.Join(" | ", heard)}");
            Equal(false, controller.AutomaticInputHoldsFlightAction, "no flight action is asked for when nothing drives");
        }
    }

    private static (WorldMapNavigationController Controller, NativeFlight Flight, WorldMapNavigationTarget Mideel, WorldMapRoutePlanner Planner) StartLoggedFlight(
        WorldMapData map, WorldMapTargetCatalog catalog, int camera)
    {
        var direction = -(camera / 16);
        var logged = new WorldMapStateSnapshot(3, 0, 2, 1050, 218693, 1281, 204287,
            4032, 4032, 3, 12, WorldMapHighwindLanding.HighwindModelId, 120, camera,
            new FieldNavigationControlTransform(direction < -128 ? direction + 256 : direction))
        {
            HasModelRotation = true,
            ModelRotation = (short)camera,
            EntityFlags = 0x82,
            NativePlayerEntityPointer = 0xE3A108
        };
        var mideel = catalog.ReadTargets(WorldMapNavigationCategory.Story, logged, Array.Empty<WorldMapEntitySnapshot>())
            .Single(target => target.Label == "Mideel");
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [mideel]);
        while (controller.CurrentCategory != WorldMapNavigationCategory.Story)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, logged);
        }

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, logged);
        Equal(true, controller.BeaconEnabled, $"the logged flight gets a landing for Mideel ({controller.LastDiagnostic})");
        return (controller, new NativeFlight(logged), mideel, planner);
    }

    /// <summary>The eight-way key closest to a world offset under this camera, as a player would press.</summary>
    private static FieldNavigationInput Toward(double dx, double dz, int camera) =>
        Enum.GetValues<FieldNavigationInput>()
            .Where(input => input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft)
            .OrderByDescending(input =>
            {
                var (x, z) = NativeFlight.Vector(input, camera);
                return (x * dx + z * dz) / Math.Sqrt(x * x + z * z);
            })
            .First();

    /// <summary>
    /// FUN_0074EA48 in flight camera 3, one native frame at a time: the eased thrust
    /// DAT_00DE6A18 = (3 * previous + requested) &gt;&gt; 2, requested being the model's 0x78 when
    /// the flight action and a direction are held (0 otherwise - nothing here presses the
    /// forward-thrust button); with the flight action, the thrust on each held axis in the
    /// camera's frame; without it, normal flight straight ahead on what is left.
    /// </summary>
    private sealed class NativeFlight(WorldMapStateSnapshot state)
    {
        public WorldMapStateSnapshot State { get; private set; } = state;

        public int Thrust { get; private set; }

        public bool MovedLastSample { get; private set; }

        public void Fly(WorldMapData map, FieldNavigationInput direction, bool flightAction, int frames)
        {
            var (startX, startZ) = (State.X, State.Z);
            for (var frame = 0; frame < frames; frame++)
            {
                var steering = flightAction && direction != FieldNavigationInput.None;
                Thrust = (Thrust * 3 + (steering ? 120 : 0)) >> 2;
                var (vx, vz) = flightAction
                    ? steering ? Vector(direction, State.CameraFront) : (0d, 0d)
                    : Vector(FieldNavigationInput.Up, State.CameraFront);
                var x = ((State.X + (int)(vx * Thrust)) % map.WrapWidth + map.WrapWidth) % map.WrapWidth;
                var z = ((State.Z + (int)(vz * Thrust)) % map.WrapHeight + map.WrapHeight) % map.WrapHeight;
                if (!WorldMapBroncoLanding.TryFindSurface(map, x, z, out var ground))
                {
                    throw new InvalidOperationException($"no surface under {x},{z}");
                }

                State = State with { X = x, Z = z, TerrainId = ground.TerrainId, TerrainScriptId = ground.TerrainScriptId };
            }

            MovedLastSample = State.X != startX || State.Z != startZ;
        }

        public static (double X, double Z) Vector(FieldNavigationInput input, int camera)
        {
            var x = input is FieldNavigationInput.Left or FieldNavigationInput.UpLeft or FieldNavigationInput.DownLeft ? -1 :
                input is FieldNavigationInput.Right or FieldNavigationInput.UpRight or FieldNavigationInput.DownRight ? 1 : 0;
            var z = input is FieldNavigationInput.Up or FieldNavigationInput.UpLeft or FieldNavigationInput.UpRight ? -1 :
                input is FieldNavigationInput.Down or FieldNavigationInput.DownLeft or FieldNavigationInput.DownRight ? 1 : 0;
            var angle = camera * Math.PI * 2d / 4096d;
            return (x * Math.Cos(angle) - z * Math.Sin(angle), x * Math.Sin(angle) + z * Math.Cos(angle));
        }
    }

    private static bool WithinReach(WorldMapData map, WorldMapStateSnapshot state, WorldMapRouteWaypoint ship) =>
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.X, ship.X, map.WrapWidth)) < WorldMapVehicleObstacles.NativeReach &&
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.Z, ship.Z, map.WrapHeight)) < WorldMapVehicleObstacles.NativeReach;

    /// <summary>
    /// Midgar at 1596-1597 is reached by the Highwind's own Tick (point 9), in the air: it
    /// keeps its flight route and its arrival, and gets no landing.
    /// </summary>
    private static void TheHighwindsOwnStoryStopStillFlies(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var planner = new WorldMapRoutePlanner(map);
        var start = Flying(map, LoggedFlight.X, LoggedFlight.Z, LoggedFlight.Camera) with { GameMoment = 1596 };
        var midgar = catalog.ReadTargets(WorldMapNavigationCategory.Story, start, Array.Empty<WorldMapEntitySnapshot>())
            .Single(target => target.Label == "Midgar");
        Equal(true, midgar.ReachedInFlight, "the 1596 Midgar stop is the Highwind's own");
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [midgar]);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start);
        Equal(true, midgar.ArrivalTriangleIds.Contains(controller.Probe.Route!.TargetTriangleId),
            "its route is flown to its own triangles");
        var over = map.Triangles[controller.Probe.Route.TargetTriangleId].Centroid;
        var spoken = controller.Observe(Flying(map, over.X, over.Z, LoggedFlight.Camera) with { GameMoment = 1596 })?.Speech ?? string.Empty;
        Equal(true, spoken.Contains("Arrived at Midgar", StringComparison.Ordinal), $"and arrived at in the air: {spoken}");
        var mideel = catalog.Locations.Single(target => target.Label == "Mideel");
        Equal(false, mideel.ReachedInFlight, "an ordinary town is not");
    }

    /// <summary>
    /// Every Location from the logged flight: either a spot the native landing accepts on
    /// ground proved to walk to it, or the plain answer that there is none - never a route
    /// that ends over the town. Also measures the planning cost a selection pays.
    /// </summary>
    private static void EveryTownFromTheLoggedFlightLandsOrSaysItCannot(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var planner = new WorldMapRoutePlanner(map);
        var start = Flying(map, LoggedFlight.X, LoggedFlight.Z, LoggedFlight.Camera);
        var landed = new List<string>();
        var none = new List<string>();
        var slowest = (Label: string.Empty, Milliseconds: 0L);
        var total = System.Diagnostics.Stopwatch.StartNew();
        foreach (var town in catalog.Locations)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var controller = new WorldMapNavigationController(map, planner, (_, _) => [town]);
            var spoken = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start)?.Speech ?? string.Empty;
            clock.Stop();
            if (clock.ElapsedMilliseconds > slowest.Milliseconds) slowest = (town.Label, clock.ElapsedMilliseconds);
            Equal(false, spoken.Contains("Arrived", StringComparison.Ordinal), $"{town.Label}: nothing is arrived at from the air: {spoken}");
            if (!controller.BeaconEnabled)
            {
                Equal(true, spoken.Contains("No grass landing spot", StringComparison.Ordinal),
                    $"{town.Label}: without a landing it says so: {spoken} ({controller.LastDiagnostic})");
                none.Add(town.Label);
                continue;
            }

            var route = controller.Probe.Route!;
            var end = route.Waypoints[^1];
            Equal(false, town.ArrivalTriangleIds.Contains(route.TargetTriangleId), $"{town.Label}: the flight ends off the town");
            Equal(true, WorldMapHighwindLanding.FitsEveryRotation(map, planner, end.X, end.Z,
                    WorldMapBroncoLanding.DestinationFootComponents(planner, map, town), out var failure),
                $"{town.Label}: the spot lands on its ground: {failure}");
            landed.Add(town.Label);
        }

        Console.WriteLine(
            $"world Highwind: {landed.Count} of {catalog.Locations.Count} locations have a grass landing from the logged flight; " +
            $"none for: {(none.Count == 0 ? "(all have one)" : string.Join(", ", none))}; " +
            $"{total.ElapsedMilliseconds} ms in all, slowest {slowest.Label} {slowest.Milliseconds} ms.");
    }

    /// <summary>The same destination on foot is routed as before, with no landing and no flight action.</summary>
    private static void AWalkOrABoatIsUnchanged(WorldMapData map, WorldMapNavigationTarget mideel)
    {
        var planner = new WorldMapRoutePlanner(map);
        var components = WorldMapBroncoLanding.DestinationFootComponents(planner, map, mideel);
        var ground = map.Triangles.First(triangle =>
            triangle.TerrainId == 0 && components.Contains(planner.GetComponentId(0, 0, triangle.Id)) &&
            triangle.TerrainScriptId < 3);
        var walking = OnFoot(map, ground.Centroid.X, ground.Centroid.Z, ground);
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [mideel]);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, walking);
        Equal(true, mideel.ArrivalTriangleIds.Contains(controller.Probe.Route!.TargetTriangleId),
            "on foot the route still ends on Mideel's own triangles");
        controller.TryResolveAutomaticInput(walking, out _);
        Equal(false, controller.AutomaticInputHoldsFlightAction, "and walking holds no flight action");
    }

    private static void FlightHoldsTheSlotSevenActionWithItsDirections()
    {
        var sink = new RecordingSink();
        var autoWalk = new NavigationAutoWalkController(sink, new TableResolver(slotSevenToken: 0x1E));
        Equal(true, autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true), "auto walk starts");
        var result = autoWalk.Drive(FieldNavigationInput.UpLeft, canMove: true, routeActive: true, holdFlightAction: true);
        Equal(true, result.Success, $"flight input is sent: {result.Diagnostic}");
        Equal("down 0x48, down 0x4B, down 0x1E", sink.Describe(), "Up and Left with the slot 7 key held");
    }

    private static void TheSlotSevenActionIsReleasedWithTheDirections()
    {
        var sink = new RecordingSink();
        var autoWalk = new NavigationAutoWalkController(sink, new TableResolver(slotSevenToken: 0x1E));
        autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true);
        autoWalk.Drive(FieldNavigationInput.Up, canMove: true, routeActive: true, holdFlightAction: true);
        sink.Clear();
        autoWalk.Drive(FieldNavigationInput.Up, canMove: true, routeActive: true, holdFlightAction: false);
        Equal("up 0x1E", sink.Describe(), "walking again lets the action go and keeps the direction");
        autoWalk.Drive(FieldNavigationInput.Up, canMove: true, routeActive: true, holdFlightAction: true);
        sink.Clear();
        autoWalk.Drive(FieldNavigationInput.None, canMove: false, routeActive: true, holdFlightAction: false);
        Equal(true, sink.Describe() is "up 0x48, up 0x1E" or "up 0x1E, up 0x48",
            $"holding still releases both ({sink.Describe()})");
    }

    /// <summary>
    /// The brake is the flight action alone, and every way the host lets go - focus or menu
    /// (Suspend), the player's stop (Stop), a module change (Reset), navigation ending - lets
    /// it go too. No flight key outlives the auto walk that pressed it.
    /// </summary>
    private static void TheBrakeIsTheActionAloneAndEveryReleaseLetsItGo()
    {
        foreach (var (name, release) in new (string, Action<NavigationAutoWalkController>)[]
                 {
                     ("focus or menu (Suspend)", autoWalk => autoWalk.Suspend()),
                     ("the player's stop (Stop)", autoWalk => autoWalk.Stop()),
                     ("a module change (Reset)", autoWalk => autoWalk.Reset()),
                     ("navigation ending", autoWalk => autoWalk.Drive(FieldNavigationInput.None, canMove: false, routeActive: false, holdFlightAction: true)),
                     ("disposal", autoWalk => autoWalk.Dispose())
                 })
        {
            var sink = new RecordingSink();
            var autoWalk = new NavigationAutoWalkController(sink, new TableResolver(slotSevenToken: 0x1E));
            autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true);
            autoWalk.Drive(FieldNavigationInput.UpRight, canMove: true, routeActive: true, holdFlightAction: true);
            sink.Clear();
            Equal(true, autoWalk.Drive(FieldNavigationInput.None, canMove: false, routeActive: true, holdFlightAction: true).Success,
                "the brake is sent");
            Equal("0x1E", sink.DescribeHeld(), $"{name}: braking holds the flight action alone ({sink.Describe()})");
            release(autoWalk);
            Equal(string.Empty, sink.DescribeHeld(), $"{name}: nothing is left held");
        }

        var inactive = new RecordingSink();
        var notStarted = new NavigationAutoWalkController(inactive, new TableResolver(slotSevenToken: 0x1E));
        notStarted.Drive(FieldNavigationInput.None, canMove: false, routeActive: true, holdFlightAction: true);
        Equal(string.Empty, inactive.DescribeHeld(), "spoken-only navigation (auto walk never started) never brakes");

        var unmapped = new RecordingSink();
        var noKey = new NavigationAutoWalkController(unmapped, new TableResolver(slotSevenToken: 0));
        noKey.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true);
        Equal(false, noKey.Drive(FieldNavigationInput.None, canMove: false, routeActive: true, holdFlightAction: true).Success,
            "no key for the action: the brake fails closed");
        Equal(string.Empty, unmapped.DescribeHeld(), "with nothing held");
    }

    private static void FlightFailsClosedWithoutAKeyForTheAction()
    {
        var sink = new RecordingSink();
        var autoWalk = new NavigationAutoWalkController(sink, new TableResolver(slotSevenToken: 0));
        autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true);
        var result = autoWalk.Drive(FieldNavigationInput.Up, canMove: true, routeActive: true, holdFlightAction: true);
        Equal(false, result.Success, "no keyboard key for the action: flight input is refused");
        Equal(string.Empty, sink.DescribeHeld(), "and nothing is left held");
        Equal(true, autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true), "auto walk can be started again");
        Equal(true, autoWalk.Drive(FieldNavigationInput.Up, canMove: true, routeActive: true).Success,
            "walking still needs no such key");
    }

    private sealed class RecordingSink : IHighwayKeyboardInputSink
    {
        private readonly List<HighwayKeyboardTransition> sent = [];
        private readonly HashSet<ushort> held = [];

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            foreach (var transition in transitions)
            {
                sent.Add(transition);
                if (transition.IsKeyDown) held.Add(transition.ScanCode); else held.Remove(transition.ScanCode);
            }

            return new HighwayKeyboardSendResult(transitions.Count, 0);
        }

        public void Clear() => sent.Clear();

        public string Describe() => string.Join(", ", sent.Select(t => $"{(t.IsKeyDown ? "down" : "up")} 0x{t.ScanCode:X2}"));

        public string DescribeHeld() => string.Join(", ", held.Order().Select(code => $"0x{code:X2}"));
    }

    /// <summary>A live control table with the stock directions and <c>slotSevenToken</c> in slot 7.</summary>
    private sealed class TableResolver(uint slotSevenToken) : IHighwayDirectionInputMappingResolver
    {
        private readonly HighwayDirectionInputMappingResolver inner = new(new Table(slotSevenToken));

        public bool TryResolve(HighwaySteeringDirection direction, out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic) =>
            inner.TryResolve(direction, out keys, out diagnostic);

        public bool TryResolveAction(int slotIndex, out HighwayKeyboardKey key, out string diagnostic) =>
            inner.TryResolveAction(slotIndex, out key, out diagnostic);

        private sealed class Table(uint slotSevenToken) : ILegacyAddressSpace
        {
            public bool TryRead(uint virtualAddress, Span<byte> destination)
            {
                destination.Clear();
                if (virtualAddress != HighwayDirectionInputMappingResolver.MappingTableAddress ||
                    destination.Length != HighwayDirectionInputMappingResolver.MappingTableSize)
                {
                    return false;
                }

                foreach (var (slot, token) in new[]
                         {
                             (HighwayDirectionInputMappingResolver.UpSlotIndex, 0x48u),
                             (HighwayDirectionInputMappingResolver.RightSlotIndex, 0x4Du),
                             (HighwayDirectionInputMappingResolver.DownSlotIndex, 0x50u),
                             (HighwayDirectionInputMappingResolver.LeftSlotIndex, 0x4Bu),
                             (HighwayDirectionInputMappingResolver.FlightActionSlotIndex, slotSevenToken)
                         })
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(slot * sizeof(uint), sizeof(uint)), token);
                }

                return true;
            }
        }
    }

    private static WorldMapStateSnapshot Flying(WorldMapData map, int x, int z, int camera)
    {
        if (!WorldMapBroncoLanding.TryFindSurface(map, x, z, out var surface))
        {
            throw new InvalidOperationException($"no ground under the Highwind at {x},{z}");
        }

        var direction = -(camera / 16);
        return new WorldMapStateSnapshot(
            3, 0, 0, 1100, x, FlightAltitude, z,
            1232, 1232, surface.TerrainId, surface.RegionId & 31,
            WorldMapHighwindLanding.HighwindModelId, 120, camera,
            new FieldNavigationControlTransform(direction < -128 ? direction + 256 : direction))
        {
            TerrainScriptId = surface.TerrainScriptId,
            HasModelRotation = true,
            ModelRotation = 1184
        };
    }

    private static WorldMapStateSnapshot OnFoot(WorldMapData map, int x, int z, WorldMapTriangle ground) =>
        new(3, 0, 0, 1100, x, WorldMapBroncoLanding.SurfaceHeight(ground, x, z), z,
            0, 0, ground.TerrainId, ground.RegionId & 31, 0, 30, 0, new FieldNavigationControlTransform(0))
        {
            TerrainScriptId = ground.TerrainScriptId
        };

    private static bool TryLoadInstalledWorld(out WorldMapData map, out WorldMapTargetCatalog catalog, int worldProgress = 0)
    {
        map = null!;
        catalog = null!;
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, worldProgress);
        catalog = WorldMapTargetCatalog.Load(
            map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return true;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"World map Highwind - {label}: expected {expected}, got {actual}.");
        }
    }
}
