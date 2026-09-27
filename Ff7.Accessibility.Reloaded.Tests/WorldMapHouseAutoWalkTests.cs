using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The 2026-09-26 legacy log: auto walk on foot to Old Man's House (Mythril) stalled short of
/// the door and oscillated. The house's script-7 trigger meets the grass along one edge with
/// mountain faces on both sides, so the party's native footprint - FUN_00751EFC's centre and
/// the four points 200 units along each axis (007530B3; 350 only for the boat) - fits on the
/// way in only in one row about 30 units wide.
///
/// <para>These replays walk the production controller on the installed wm0.map with an
/// oracle that does not use the production planner: faces are resolved here from raw
/// vertices, ground is judged by FUN_0074CECA's walking mask 0x721B6F83, and the footprint is
/// tested here. A native frame is FUN_0074EA48's integer vector - 30 units on an axis,
/// <c>(30 * 3) &gt;&gt; 2</c> signed on a diagonal (+22 or -23) - turned by the negative camera
/// angle in 4096ths with the fixed-point shift, and a refused frame does not move.</para>
///
/// <para>This is a partial model of the engine and is labelled as one: the native collision
/// also retries rotated steps when one is refused, which is left out, so a refusal here is at
/// least as hard as in the game. The camera is a simplified stress model, not the game's:
/// held still, following the heading only while the party moves, or following it on every
/// frame including refused ones - the log's stall shows the model turning with the camera
/// while the party did not move. A host observation covers one, three or five native frames
/// with the same key held (the log moves the party 120 to 186 units per sample). Every
/// logged start is walked at eight cameras; each must stand on the house's own trigger
/// without a fail-stop.</para>
/// </summary>
internal static class WorldMapHouseAutoWalkTests
{
    private const string House = "Old Man's House (Mythril)";
    /// <summary>
    /// A model the oracle moves: its native frame (FUN_0074EA48: 0x1E on foot, 0x2D for the
    /// Buggy) and the terrain FUN_0074CECA lets it onto (0x721B6F83 on foot, 0x331B6F13 for
    /// the Buggy in ordinary travel). Both are held by the same 200-unit footprint.
    /// </summary>
    private readonly record struct Mover(int Model, int Step, uint Mask);

    private static readonly Mover OnFoot = new(0, 30, 0x721B6F83u);
    private static readonly Mover Buggy = new(6, 45, 0x331B6F13u);
    private const int FootprintReach = 200;

    /// <summary>
    /// Simplified camera models: how far the camera turns towards the heading per native
    /// frame, and whether it also turns on a refused frame.
    /// </summary>
    private static readonly (int Rate, bool TurnsWhenRefused)[] CameraModels = [(0, false), (32, true), (64, false)];

    // Accepted positions from the log: the attempt starts, the restart, the stall point, the
    // spin point and the final position.
    private static readonly (int X, int Y, int Z)[] LoggedStarts =
    [
        (204063, 1821, 134761),
        (203473, 1493, 136136),
        (199717, 1838, 134000),
        (202069, 2191, 131317),
        (199141, 1750, 133518),
        (199507, 1772, 133712)
    ];

    private static readonly int[] Cameras = [0, 512, 1024, 1632, 2048, 2848, 3152, 3584];

    internal static void Run()
    {
        var (map, catalog) = Load();
        var target = catalog.Locations.Single(candidate => candidate.Label == House);
        TheBuggyOnTheHouseTriggerIsNotAnArrival(map, target);
        TheBuggyDrivesOutOfTheLoggedStalls(map, catalog);
        InputTimes.Clear();
        var failures = new List<string>();
        var runs = 0;
        // The log moves the party 120 to 186 units between host samples: several native
        // frames with the same key held.
        foreach (var follow in CameraModels)
        foreach (var framesPerObservation in new[] { 1, 3, 5 })
        foreach (var start in LoggedStarts)
        foreach (var camera in Cameras)
        {
            runs++;
            var result = Walk(map, target, OnFoot, start, camera, framesPerObservation, follow,
                state => IsOnHouseTrigger(map, state), 2400);
            if (result is not null)
            {
                failures.Add(result);
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{failures.Count} of {runs} native house approaches did not enter:{Environment.NewLine}" +
                string.Join(Environment.NewLine, failures));
        }

        Console.WriteLine($"PASS Old Man's House native auto walk: {runs} approaches entered the trigger. {DescribeTimes()}.");
    }

    /// <summary>
    /// 20:45:10 in the log: the Buggy stood on the house's own trigger (terrain script 7 in
    /// mesh 24,16), nothing loaded, and navigation said "Arrived ... Navigation off". wm0.ev
    /// handler A584 enters field 9 only when special 8 is model 0, 1 or 2 (2C13..2C2B), so
    /// the party is told the way in is on foot and the route is kept; nothing dismounts them.
    /// </summary>
    private static void TheBuggyOnTheHouseTriggerIsNotAnArrival(WorldMapData map, WorldMapNavigationTarget target)
    {
        var planner = new WorldMapRoutePlanner(map);
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        var now = new DateTime(2026, 9, 26, 20, 45, 9, DateTimeKind.Utc);
        var approaching = State(199063, 1746, 133603, 3968) with { PlayerModelId = 6, TerrainId = 0 };
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, approaching, now);
        Equal(true, controller.BeaconEnabled, $"navigation starts in the Buggy: {controller.LastDiagnostic}");
        var onTrigger = approaching with { X = 198992, Y = 1742, Z = 133550, TerrainId = 16, TerrainScriptId = 7 };
        var output = controller.Observe(onTrigger, now.AddSeconds(1), automaticWalkActive: true);
        var speech = output?.Speech ?? string.Empty;
        Equal(false, speech.Contains("Arrived", StringComparison.Ordinal), $"the Buggy on the trigger is not an arrival: \"{speech}\"");
        Equal(true, speech.Contains("The way in is on foot", StringComparison.Ordinal), $"it says the way in is on foot: \"{speech}\"");
        Equal(true, output?.StopAutoWalk == true, "and automatic walking stops");
        Equal(true, controller.BeaconEnabled, "the destination is kept");
        Equal(false, controller.TryResolveAutomaticInput(onTrigger, out _), "nothing is pressed while the game waits for the party to get out");
        var onFoot = onTrigger with { X = 199261, Y = 1758, Z = 133679, PlayerModelId = 0, TerrainScriptId = 0 };
        controller.Observe(onFoot, now.AddSeconds(4), automaticWalkActive: true);
        Equal(true, controller.BeaconEnabled, "on foot the route to the house resumes");
        Equal(true, controller.TryResolveAutomaticInput(onFoot, out _), "and automatic walking has somewhere to go");
    }

    /// <summary>
    /// The same session's Buggy fail-stops, before the house: 19:52:07 at 87492,170633 and
    /// 19:52:18 at 87246,170533 (on the Cosmo Canyon trigger, whose handler turns the Buggy
    /// away), with Costa Del Sol selected; and 19:58:03 at 125993,148231. The Buggy is held by
    /// the same 200-unit footprint (00751EFC, 007530B3) on its own ground, 0x331B6F13, and
    /// moves 0x2D a frame. The old controller pressed one refused key until it gave up. Each
    /// start has to drive at least 1024 units clear without a fail-stop - out of the stall, not
    /// all the way to Costa, which is a long drive this oracle does not model.
    /// </summary>
    private static void TheBuggyDrivesOutOfTheLoggedStalls(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var costa = catalog.Locations.Single(candidate => candidate.Label == "Costa Del Sol");
        var failures = new List<string>();
        var runs = 0;
        foreach (var follow in CameraModels)
        foreach (var framesPerObservation in new[] { 1, 3, 5 })
        foreach (var start in new[] { (87492, 1462, 170633), (87246, 1500, 170533), (125993, 264, 148231) })
        foreach (var camera in Cameras)
        {
            runs++;
            var result = Walk(map, costa, Buggy, start, camera, framesPerObservation, follow,
                state => Math.Pow(state.X - start.Item1, 2) + Math.Pow(state.Z - start.Item3, 2) >= 1024d * 1024d, 900);
            if (result is not null)
            {
                failures.Add(result);
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{failures.Count} of {runs} logged Buggy stalls were not driven out of:{Environment.NewLine}" +
                string.Join(Environment.NewLine, failures));
        }

        Console.WriteLine($"PASS Buggy logged stalls: {runs} drives cleared 1024 units without a fail-stop. {DescribeTimes()}.");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    /// <summary>Null when the walk succeeded; otherwise why not.</summary>
    private static string? Walk(
        WorldMapData map,
        WorldMapNavigationTarget target,
        Mover mover,
        (int X, int Y, int Z) start,
        int camera,
        int framesPerObservation,
        (int Rate, bool TurnsWhenRefused) cameraFollow,
        Func<WorldMapStateSnapshot, bool> succeeded,
        int frameLimit)
    {
        var planner = new WorldMapRoutePlanner(map);
        var controller = new WorldMapNavigationController(map, planner, (_, _) => [target]);
        var state = State(start.X, start.Y, start.Z, camera) with { PlayerModelId = mover.Model };
        var now = new DateTime(2026, 9, 26, 20, 47, 0, DateTimeKind.Utc);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
        var label = $"start={start.X},{start.Z} camera={camera} frames/observation={framesPerObservation} " +
                    $"camera model={cameraFollow.Rate}{(cameraFollow.TurnsWhenRefused ? " turning when refused" : string.Empty)}";
        var recent = new Queue<string>();
        var refusedFrames = 0;
        for (var frame = 0; frame < frameLimit; frame += framesPerObservation)
        {
            now = now.AddMilliseconds(1000d / 30d * framesPerObservation);
            var output = controller.Observe(state, now, automaticWalkActive: true);
            if (output?.StopAutoWalk == true)
            {
                return $"{label}: fail-stop at {state.X},{state.Z} frame {frame} ({output?.Speech}) refused={refusedFrames}; " +
                       string.Join(" / ", recent);
            }

            if (succeeded(state))
            {
                return null;
            }

            if (!controller.BeaconEnabled)
            {
                return $"{label}: navigation ended off the trigger at {state.X},{state.Z}: {controller.LastDiagnostic}";
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var resolved = controller.TryResolveAutomaticInput(state, out var input);
            timer.Stop();
            InputTimes.Add(timer.Elapsed.TotalMilliseconds);
            if (!resolved)
            {
                recent.Enqueue($"{state.X},{state.Z} none");
                Trim(recent);
                continue;
            }

            recent.Enqueue($"{state.X},{state.Z} cam {state.CameraFront} {input} wp{controller.Probe.WaypointIndex}");
            Trim(recent);
            for (var step = 0; step < framesPerObservation; step++)
            {
                var (moved, next) = NativeFrame(map, mover, state, input, cameraFollow);
                if (!moved)
                {
                    // A refused frame does not move the party. Standing on the trigger is
                    // required; no entry from a refused step is assumed.
                    refusedFrames++;
                    continue;
                }

                state = next;
                if (succeeded(state))
                {
                    return null;
                }
            }
        }

        return $"{label}: did not succeed; last {state.X},{state.Z}; " + string.Join(" / ", recent);
    }

    /// <summary>Every automatic-input call's duration in these replays, for the timing line.</summary>
    private static readonly List<double> InputTimes = [];

    private static string DescribeTimes()
    {
        if (InputTimes.Count == 0)
        {
            return "no calls";
        }

        var sorted = InputTimes.OrderBy(value => value).ToArray();
        string At(double fraction) => sorted[Math.Min(sorted.Length - 1, (int)(fraction * sorted.Length))].ToString("0.00");
        var line = $"{sorted.Length} automatic-input calls: median {At(0.5)} ms, 99th percentile {At(0.99)} ms, " +
                   $"worst {sorted[^1]:0.00} ms, over 16 ms {sorted.Count(value => value > 16d)}";
        InputTimes.Clear();
        return line;
    }

    private static void Trim(Queue<string> recent)
    {
        while (recent.Count > 8)
        {
            recent.Dequeue();
        }
    }

    /// <summary>
    /// One native frame of walking: FUN_0074EA48's direction for the key under the current
    /// camera, 30 units, accepted only where the whole footprint fits. The camera then turns
    /// towards the heading, as the world camera follows the party.
    /// </summary>
    private static (bool Moved, WorldMapStateSnapshot Next) NativeFrame(
        WorldMapData map, Mover mover, WorldMapStateSnapshot state, FieldNavigationInput input, (int Rate, bool TurnsWhenRefused) cameraFollow)
    {
        var (dx, dz) = NativeVector(input, state.CameraFront, mover.Step);
        var turned = TurnCamera(state, dx, dz, cameraFollow.Rate);
        var x = state.X + dx;
        var z = state.Z + dz;
        // 007530B3 resolves all five contact points against the model's current height
        // (f_28 = the model position's y), not the height the step would reach.
        if (!TrySurface(map, x, z, state.Y, out var ground) || !FootprintFits(map, mover, x, state.Y, z))
        {
            return (false, cameraFollow.TurnsWhenRefused ? turned : state);
        }

        return (true, turned with
        {
            X = x,
            Y = ground.Y,
            Z = z,
            TerrainId = ground.TerrainId,
            TerrainScriptId = ground.TerrainScriptId
        });
    }

    private static WorldMapStateSnapshot TurnCamera(WorldMapStateSnapshot state, int dx, int dz, int rate)
    {
        var heading = (int)Math.Round(Math.Atan2(dx, -dz) * 4096d / (2d * Math.PI));
        var turn = ((heading - state.CameraFront) % 4096 + 6144) % 4096 - 2048;
        var camera = ((state.CameraFront + Math.Clamp(turn, -rate, rate)) % 4096 + 4096) % 4096;
        return state with { CameraFront = camera, ControlTransform = new FieldNavigationControlTransform(ControlDirection(camera)) };
    }

    /// <summary>
    /// FUN_0074EA48's movement for a key: Right sets x = +step, Left x = -step, Up z = -step,
    /// Down z = +step, and a diagonal takes each axis as <c>(axis * 3) &gt;&gt; 2</c>, an arithmetic
    /// shift. The vector is turned by a Y rotation of the negative camera angle (006628DE)
    /// and applied in 4096ths (00662ECC), shifted down by 12.
    /// </summary>
    private static (int X, int Z) NativeVector(FieldNavigationInput input, int camera, int step)
    {
        var x = input is FieldNavigationInput.Left or FieldNavigationInput.UpLeft or FieldNavigationInput.DownLeft ? -step :
            input is FieldNavigationInput.Right or FieldNavigationInput.UpRight or FieldNavigationInput.DownRight ? step : 0;
        var z = input is FieldNavigationInput.Up or FieldNavigationInput.UpLeft or FieldNavigationInput.UpRight ? -step :
            input is FieldNavigationInput.Down or FieldNavigationInput.DownLeft or FieldNavigationInput.DownRight ? step : 0;
        if (x != 0 && z != 0)
        {
            x = (x * 3) >> 2;
            z = (z * 3) >> 2;
        }

        var angle = camera * Math.PI * 2d / 4096d;
        var cos = (int)Math.Round(Math.Cos(angle) * 4096d);
        var sin = (int)Math.Round(Math.Sin(angle) * 4096d);
        return ((cos * x - sin * z) >> 12, (sin * x + cos * z) >> 12);
    }

    /// <summary>The centre and the four points 200 units along each axis, each on ground a walker may enter.</summary>
    private static bool FootprintFits(WorldMapData map, Mover mover, int x, int y, int z)
    {
        foreach (var (ox, oz) in new[] { (0, 0), (-FootprintReach, 0), (FootprintReach, 0), (0, -FootprintReach), (0, FootprintReach) })
        {
            if (!TrySurface(map, x + ox, z + oz, y, out var ground) || !Allows(mover, ground.TerrainId))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Ground the mover may enter: its FUN_0074CECA mask.</summary>
    private static bool Allows(Mover mover, int terrain) =>
        terrain is >= 0 and < 32 && ((mover.Mask >> terrain) & 1u) != 0;

    private static bool IsOnHouseTrigger(WorldMapData map, WorldMapStateSnapshot state) =>
        IsHouseTrigger(map, state.X, state.Y, state.Z);

    /// <summary>A script-7 face in mesh (24,16), where handler 0xA584 is registered.</summary>
    private static bool IsHouseTrigger(WorldMapData map, int x, int y, int z) =>
        TrySurface(map, x, z, y, out var ground) &&
        ground.TerrainScriptId == 7 && ground.MeshX == 24 && ground.MeshZ == 16;

    /// <summary>The raw face containing the point in plan whose height there is nearest the party's.</summary>
    private static bool TrySurface(WorldMapData map, int x, int z, int nearY, out (int Y, int TerrainId, int TerrainScriptId, int MeshX, int MeshZ) ground)
    {
        ground = default;
        var best = double.MaxValue;
        if (!Cells.TryGetValue(map, out var cells))
        {
            cells = map.Triangles.GroupBy(triangle => (triangle.MeshX, triangle.MeshZ))
                .ToDictionary(group => group.Key, group => group.ToArray());
            Cells[map] = cells;
        }

        var meshX = x / 8192;
        var meshZ = z / 8192;
        foreach (var triangle in Enumerable.Range(-1, 3).SelectMany(ox => Enumerable.Range(-1, 3)
                     .SelectMany(oz => cells.TryGetValue((meshX + ox, meshZ + oz), out var cell) ? cell : [])))
        {

            var a = triangle.Vertex0;
            var b = triangle.Vertex1;
            var c = triangle.Vertex2;
            var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
            if (Math.Abs(denominator) < 1e-6)
            {
                continue;
            }

            var wa = ((b.Z - c.Z) * (double)(x - c.X) + (c.X - b.X) * (double)(z - c.Z)) / denominator;
            var wb = ((c.Z - a.Z) * (double)(x - c.X) + (a.X - c.X) * (double)(z - c.Z)) / denominator;
            var wc = 1d - wa - wb;
            if (wa < -1e-9 || wb < -1e-9 || wc < -1e-9)
            {
                continue;
            }

            var y = wa * a.Y + wb * b.Y + wc * c.Y;
            var distance = Math.Abs(y - nearY);
            if (distance < best)
            {
                best = distance;
                ground = ((int)Math.Round(y), triangle.TerrainId, triangle.TerrainScriptId, triangle.MeshX, triangle.MeshZ);
            }
        }

        return best < double.MaxValue;
    }

    private static readonly Dictionary<WorldMapData, Dictionary<(int, int), WorldMapTriangle[]>> Cells = [];

    private static WorldMapStateSnapshot State(int x, int y, int z, int camera) =>
        new(3, 0, 0, 415, x, y, z, 0, 0, 0, 2, 0, 30, camera, new FieldNavigationControlTransform(ControlDirection(camera)));

    private static int ControlDirection(int camera)
    {
        var normalized = (camera % 4096 + 4096) % 4096;
        var direction = -(normalized / 16);
        return direction < -128 ? direction + 256 : direction;
    }

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
}
