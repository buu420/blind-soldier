using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The 2026-09-27 legacy session, on foot beside Old Man's House (Mythril) with the Buggy
/// parked where the party got out of it. At the selected logged starts, the released
/// controller replay predicts no detour around the saved car and returns no key. The live
/// log also shows no commanded input there while the player moved by hand. Yet the same
/// log has the game accepting the party's own steps straight through that
/// modelled footprint (16:37:37-16:37:38) and loading the house at 16:37:39, with the car
/// where the save puts it. So "no way round" is the mask model's prediction, not the game's
/// answer: automatic walking must walk the route and let the game's own refusals decide.
/// </summary>
internal static class WorldMapHouseParkingTests
{
    private const string House = "Old Man's House (Mythril)";

    /// <summary>
    /// save01 slot 12 (+F74), the save logged at 16:42:50: model 6 at 199323,1761,133609. The
    /// party never got back in between the dismount at 16:36:25 and that save, so this is
    /// where the car stood for these logged attempts. Flags 0 is an explicit test fixture
    /// that enables the collision prediction; the log did not capture the car's live flags.
    /// </summary>
    private static readonly WorldMapEntitySnapshot SavedBuggy =
        new(0x00E3A108, 0, false, 199_323, 1_761, 133_609, 0, 2, 6, 0);

    /// <summary>Accepted positions and cameras from the log where automatic walking was on and pressed nothing.</summary>
    private static readonly (int X, int Y, int Z, int Camera, string At)[] LoggedStarts =
    [
        (199_498, 1_768, 133_447, 4_059, "16:36:33, just out of the Buggy"),
        (199_841, 1_850, 133_376, 3_163, "16:36:36"),
        (200_274, 1_929, 134_635, 2_265, "16:37:31"),
        (200_042, 2_117, 134_350, 2_436, "16:41:47"),
        (201_218, 1_817, 133_963, 2_676, "16:42:13")
    ];

    internal static void Run()
    {
        var (map, catalog) = WorldMapHouseAutoWalkTests.Load();
        var house = catalog.Locations.Single(target => target.Label == House);
        TheWitnessedWalkIsOneTheMaskModelRefuses(map);
        TheSavedBuggyDoesNotSilenceAutomaticWalking(map, catalog, house);
        AGameThatDoesRefuseEndsInASpokenStopAfterEveryRestart(map, catalog, house);
        Console.WriteLine(
            "PASS Old Man's House with the saved Buggy: automatic walking steers at once and completes as the game allowed, " +
            "and a game that refuses ends in a spoken stop after a keyboard restart too.");
    }

    /// <summary>
    /// 16:37:37-16:37:38, the player's own Right held (commanded=None): 199779,133377 ->
    /// 199665,133400 -> 199491,133435 -> 199377,133458 -> 199263,133483, each accepted, and
    /// field 78 loaded a second later. The mask model refuses every one of those steps: they
    /// overlap the car and close on it. That is why the replays below move the party as the
    /// game did, rather than as the model predicts.
    /// </summary>
    private static void TheWitnessedWalkIsOneTheMaskModelRefuses(WorldMapData map)
    {
        (int X, int Z)[] witnessed = [(199_779, 133_377), (199_665, 133_400), (199_491, 133_435), (199_377, 133_458), (199_263, 133_483)];
        var refused = 0;
        for (var index = 1; index < witnessed.Length; index++)
        {
            if (WorldMapVehicleObstacles.BlocksSegment(
                    [SavedBuggy], 0, witnessed[index - 1].X, witnessed[index - 1].Z, witnessed[index].X, witnessed[index].Z,
                    map.WrapWidth, map.WrapHeight))
            {
                refused++;
            }
        }

        Require(refused == witnessed.Length - 1,
            $"the mask model refuses the steps the game accepted at 16:37:38 ({refused} of {witnessed.Length - 1})");
    }

    /// <summary>
    /// The released controller, configured as the live runtime is (the catalog's entrance
    /// triangles, the live entity list with the saved car), pressed nothing from any of these
    /// starts. Now it must press a key at once, and walk into the house by native frames the
    /// game accepts as it did - terrain and the 200-unit footprint, not the car's mask.
    /// </summary>
    private static void TheSavedBuggyDoesNotSilenceAutomaticWalking(
        WorldMapData map, WorldMapTargetCatalog catalog, WorldMapNavigationTarget house)
    {
        var failures = new List<string>();
        var runs = 0;
        foreach (var start in LoggedStarts)
        {
            var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
            var controller = new WorldMapNavigationController(map, planner, (_, _) => [house], entityProvider: () => [SavedBuggy]);
            var state = WorldMapHouseAutoWalkTests.State(start.X, start.Y, start.Z, start.Camera);
            var now = new DateTime(2026, 9, 27, 16, 36, 33, DateTimeKind.Utc);
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
            Require(controller.BeaconEnabled, $"{start.At}: navigation to the house starts ({controller.LastDiagnostic})");
            var observed = controller.Observe(state, now.AddMilliseconds(100), automaticWalkActive: true);
            Require(observed?.StopAutoWalk != true, $"{start.At}: automatic walking is not stopped at once ({observed?.Speech})");
            if (!controller.TryResolveAutomaticInput(state, out _))
            {
                failures.Add($"{start.At}: no key pressed at {start.X},{start.Z} ({controller.LastDiagnostic})");
            }

            foreach (var frames in new[] { 1, 3, 5 })
            foreach (var camera in new[] { start.Camera, (start.Camera + 1024) % 4096, (start.Camera + 2048) % 4096 })
            {
                runs++;
                var result = WorldMapHouseAutoWalkTests.Walk(
                    map, house, WorldMapHouseAutoWalkTests.OnFoot, (start.X, start.Y, start.Z), camera, frames, (0, false),
                    walked => WorldMapHouseAutoWalkTests.IsOnHouseTrigger(map, walked), 2400,
                    [SavedBuggy], catalog.EntranceTriangleIds, vehiclesRefuse: false);
                if (result is not null)
                {
                    var withoutCar = WorldMapHouseAutoWalkTests.Walk(
                        map, house, WorldMapHouseAutoWalkTests.OnFoot, (start.X, start.Y, start.Z), camera, frames, (0, false),
                        walked => WorldMapHouseAutoWalkTests.IsOnHouseTrigger(map, walked), 2400,
                        [], catalog.EntranceTriangleIds);
                    failures.Add($"{start.At}: {result} [with no car: {withoutCar ?? "entered"}]");
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{failures.Count} checks failed with the saved Buggy parked beside the house:{Environment.NewLine}" +
                string.Join(Environment.NewLine, failures));
        }

        Console.WriteLine($"PASS saved Buggy beside Old Man's House: {LoggedStarts.Length} logged starts press a key at once; {runs} walks entered.");
    }

    /// <summary>
    /// Were the game to refuse as the mask model says, automatic walking must still not go
    /// quiet: it keeps trying keys, learns what the game refuses, and ends in the ordinary
    /// spoken fail-stop with the destination kept. The host's keyboard toggle restarts the
    /// walk without selecting the destination again; that walk must try again and end aloud
    /// again, not press nothing forever. Nothing may claim the car blocks the way.
    /// </summary>
    private static void AGameThatDoesRefuseEndsInASpokenStopAfterEveryRestart(
        WorldMapData map, WorldMapTargetCatalog catalog, WorldMapNavigationTarget house)
    {
        foreach (var start in LoggedStarts)
        foreach (var frames in new[] { 1, 3, 5 })
        {
            var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
            var controller = new WorldMapNavigationController(map, planner, (_, _) => [house], entityProvider: () => [SavedBuggy]);
            var state = WorldMapHouseAutoWalkTests.State(start.X, start.Y, start.Z, start.Camera);
            var now = new DateTime(2026, 9, 27, 16, 36, 33, DateTimeKind.Utc);
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
            var label = $"{start.At}, {frames} frames per observation";
            for (var walk = 1; walk <= 2; walk++)
            {
                var attempt = Attempt(map, controller, ref state, ref now, frames, frameLimit: 1800);
                Require(attempt.Keys > 0, $"{label}, walk {walk}: automatic walking presses keys ({controller.LastDiagnostic})");
                Require(attempt.Speech?.Contains("block", StringComparison.OrdinalIgnoreCase) != true,
                    $"{label}, walk {walk}: no obstruction is claimed that the game has not shown: \"{attempt.Speech}\"");
                if (attempt.Entered)
                {
                    break;
                }

                Require(attempt.Stopped && attempt.Speech?.Contains("Could not get closer", StringComparison.Ordinal) == true,
                    $"{label}, walk {walk}: a refused walk ends in a spoken stop, not silence (last {state.X},{state.Z}: {controller.LastDiagnostic})");
                Require(controller.BeaconEnabled, $"{label}, walk {walk}: the destination is kept");

                // The host stops automatic walking; a second later the keyboard toggle starts
                // it again with navigation still on, so nothing selects the destination anew.
                for (var tick = 0; tick < 10; tick++)
                {
                    now = now.AddMilliseconds(100);
                    controller.Observe(state, now, automaticWalkActive: false);
                }
            }
        }

        Console.WriteLine("PASS a refusing game: every walk from the logged starts steers and ends in the spoken stop, restart included.");
    }

    /// <summary>
    /// One automatic walk, moved by native frames the mask model's endpoint test refuses
    /// (00762A21/00762993: overlapping and closing): until the house, a fail-stop, or the
    /// frame limit.
    /// </summary>
    private static (bool Entered, bool Stopped, string? Speech, int Keys) Attempt(
        WorldMapData map,
        WorldMapNavigationController controller,
        ref WorldMapStateSnapshot state,
        ref DateTime now,
        int frames,
        int frameLimit)
    {
        var keys = 0;
        for (var frame = 0; frame < frameLimit; frame += frames)
        {
            now = now.AddMilliseconds(1000d / 30d * frames);
            var output = controller.Observe(state, now, automaticWalkActive: true);
            if (output?.StopAutoWalk == true)
            {
                return (false, true, output.Value.Speech, keys);
            }

            if (WorldMapHouseAutoWalkTests.IsOnHouseTrigger(map, state))
            {
                return (true, false, output?.Speech, keys);
            }

            if (!controller.TryResolveAutomaticInput(state, out var input))
            {
                continue;
            }

            keys++;
            for (var step = 0; step < frames; step++)
            {
                var (moved, next) = WorldMapHouseAutoWalkTests.NativeFrame(
                    map, WorldMapHouseAutoWalkTests.OnFoot, state, input, (0, false), [SavedBuggy]);
                if (moved)
                {
                    state = next;
                }

                if (WorldMapHouseAutoWalkTests.IsOnHouseTrigger(map, state))
                {
                    return (true, false, null, keys);
                }
            }
        }

        return (false, false, null, keys);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
