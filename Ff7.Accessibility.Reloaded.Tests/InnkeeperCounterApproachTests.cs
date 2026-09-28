using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Nibelheim inn (nivinn_1, 273), 2026-09-28 user log: at 01:10:53 the player selected the
/// Innkeeper at (-121,247,-10), triangle 53, and was told the way was shut - by the native lock
/// on triangle 56 at the far end of the room, which has nothing to do with the counter.
///
/// <para>Installed script (identical in both archives): the Director locks triangle 56
/// (6D380001); e19 imayado, the innkeeper, stands at (-5,-232) (XYZI A50000FBFF18FF00003000);
/// e20 mant, the black-caped man, at (-26,293) (XYZI A50000E6FF250100004D00). The field's scale
/// is 512, so field init's defaults (Ghidra 0060BCFA) make the leader's collision radius 30 and
/// a Talk radius 80: native Talk reach 110 (tools/GuideRouteAudit/NativeRadii.cs,
/// baseline-route-audit/x86-legacy.json). The counter's front is triangle 11's closed edge at
/// y about -308; a 30-unit body facing her fits from y -338 down, and Talk reach ends near
/// y -342. The black-caped man is the live model that gives the planner a body width at all.</para>
///
/// <para>The replay is the production controller, its own emitted input, and the native step
/// with the black-caped man's cylinder and lock 56 enforced on every frame. Success is standing
/// strictly inside Talk reach and being told the innkeeper is reached - with 56 still locked.</para>
/// </summary>
internal static class InnkeeperCounterApproachTests
{
    private const int Field = 273;
    private const int Lock = 56;
    private const int PlayerRadius = 30;
    private const int TalkReach = 30 + 80;
    private static readonly FieldNavigationDynamicObstacle BlackCape = new(13, -26, 293, 0, 30, 30);
    private static readonly FieldPositionSnapshot LoggedStart = new(FieldPositionReader.FieldModule, Field, 0, -121, 247, -10, 53, 56);
    private static readonly FieldPositionSnapshot GroundFloorArrival = new(FieldPositionReader.FieldModule, Field, 0, -22, -553, 0, 33, 0);

    private static FieldNavigationTarget Innkeeper(int reach = TalkReach) =>
        new(Field, FieldNavigationCategory.Npcs, "Innkeeper", -5, -232, 0, "npc:273:19",
            TriggerEntityId: 19, InteractionRadius: reach);

    internal static void Run(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner>? wrap = null,
        string runtime = "legacy")
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            Console.WriteLine("innkeeper counter: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        wrap ??= planner => planner;
        TheInstalledScriptPlacesTheCounterAndTheLock(dataRoot);
        var failures = new List<string>();
        failures.AddRange(ARouteEndsInsideTalkReachWithTheLockOn(createWalkmeshReader, wrap));
        failures.AddRange(NothingIsInventedBeyondTalkReach(createWalkmeshReader, wrap));
        failures.AddRange(AnExhaustedSearchIsNotALock(wrap));
        failures.AddRange(AutomaticArrivalRequiresFacingEvenAtTheFinalWaypoint(createWalkmeshReader, wrap));
        foreach (var withModel in new[] { true, false })
        {
            foreach (var start in new[] { LoggedStart, GroundFloorArrival })
            {
                foreach (var (speed, frames) in new (ushort, int)[] { (1024, 2), (1024, 4), (2048, 2), (2048, 4) })
                {
                    failures.AddRange(WalkToTheCounter(createWalkmeshReader, wrap, start, speed, frames, withModel));
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"{runtime} innkeeper counter:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        Console.WriteLine($"PASS {runtime} innkeeper counter: from the logged start and the ground-floor arrival, walking " +
                          "and running, with and without another live model, auto walk reaches native Talk reach 110 " +
                          "facing her strictly inside +/-64 with lock 56 still on; an exhausted search is not reported as a lock.");
    }

    private static void TheInstalledScriptPlacesTheCounterAndTheLock(string dataRoot)
    {
        var source = new FlevelDataSource(dataRoot);
        if (!source.TryReadField(Field, out var encoded))
        {
            throw new InvalidOperationException($"installed field {Field} is unavailable: {source.Diagnostic}");
        }

        var field = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var start = BitConverter.ToInt32(field, 6) + 4;
        var script = field[start..BitConverter.ToInt32(field, 10)];
        Equal(512, (int)BitConverter.ToInt16(script, 8), "nivinn_1's scale, so the native defaults are 30 and 80");
        Equal(true, script.AsSpan().IndexOf((byte[])[0x6D, 0x38, 0x00, 0x01]) >= 0, "the Director locks triangle 56");
        Equal(true, script.AsSpan().IndexOf((byte[])[0xA5, 0x00, 0x00, 0xFB, 0xFF, 0x18, 0xFF, 0x00, 0x00, 0x30, 0x00]) >= 0,
            "the innkeeper stands at (-5,-232), triangle 48");
        Equal(true, script.AsSpan().IndexOf((byte[])[0xA5, 0x00, 0x00, 0xE6, 0xFF, 0x25, 0x01, 0x00, 0x00, 0x4D, 0x00]) >= 0,
            "the black-caped man at (-26,293), triangle 77");
    }

    /// <param name="withModel">
    /// Whether the black-caped man is in the room. Without him no other model carries a body
    /// width, and the leader's own +0x72, read directly, is what keeps the body check on.
    /// </param>
    private static FieldWalkmeshRoutePlanner Planner(Func<int, FieldWalkmeshReader> createWalkmeshReader, bool withModel = true) =>
        new(createWalkmeshReader(Field), FieldBodyClearanceRouteTests.LockedTriangles(Field, [Lock]),
            dynamicObstacleProvider: (_, _) => withModel ? [BlackCape] : [],
            playerCollisionRadiusProvider: _ => PlayerRadius);

    private static IEnumerable<string> ARouteEndsInsideTalkReachWithTheLockOn(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        foreach (var (start, withModel) in new[] { (LoggedStart, true), (GroundFloorArrival, true), (LoggedStart, false), (GroundFloorArrival, false) })
        {
            var planner = wrap(Planner(createWalkmeshReader, withModel));
            Begin(planner);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            planner.TryBuildRoute(start, Innkeeper(), out _);
            var cold = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            for (var repeat = 0; repeat < 20; repeat++)
            {
                Begin(planner);
                planner.TryBuildRoute(start, Innkeeper(), out _);
            }

            var warm = clock.Elapsed.TotalMilliseconds / 20;
            Console.WriteLine($"innkeeper route from {start.X},{start.Y}{(withModel ? string.Empty : " (no other model)")}: " +
                              $"first {cold:0.0} ms, then {warm:0.00} ms each");
            if (warm > 25d)
            {
                yield return $"from {start.X},{start.Y}: an innkeeper route build takes {warm:0.0} ms";
            }

            Begin(planner);
            if (!planner.TryBuildRoute(start, Innkeeper(), out var plan))
            {
                yield return $"from {start.X},{start.Y}: no route to the innkeeper: {planner.LastDiagnostic}";
                continue;
            }

            var end = plan.FinalApproach;
            var distance = Math.Sqrt(Math.Pow(end.X + 5, 2) + Math.Pow(end.Y + 232, 2));
            if (distance >= TalkReach || plan.TrianglePath.Contains(Lock))
            {
                yield return $"from {start.X},{start.Y}: the approach {end.X},{end.Y} is {distance:0.0} away (reach {TalkReach}), " +
                             $"path {string.Join(',', plan.TrianglePath)}";
            }

            // A centre-only approach would stop at the counter's front edge (about y -316), where a
            // 30-unit body facing her cannot stand. The body-checked answer is at y -338 or lower.
            if (end.Y > -338)
            {
                yield return $"from {start.X},{start.Y}{(withModel ? string.Empty : " with no other model")}: the approach " +
                             $"{end.X},{end.Y} is not one a 30-unit body can stand at facing her";
            }
        }
    }

    /// <summary>
    /// The search finds legal ground; it does not make any. With a reach nothing at the counter
    /// honours - 90 is closer than a body can stand - the answer is still no route, and the
    /// lock is still what is named as in the way (it is: behind 56 the counter's back is open).
    /// </summary>
    private static IEnumerable<string> NothingIsInventedBeyondTalkReach(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var planner = wrap(Planner(createWalkmeshReader));
        Begin(planner);
        if (planner.TryBuildRoute(LoggedStart, Innkeeper(90), out var plan))
        {
            var end = plan.FinalApproach;
            var distance = Math.Sqrt(Math.Pow(end.X + 5, 2) + Math.Pow(end.Y + 232, 2));
            if (distance >= 90 || plan.TrianglePath.Contains(Lock))
            {
                yield return $"reach 90 produced an approach outside it or through the lock: {end.X},{end.Y} ({distance:0.0})";
            }
        }
    }

    /// <summary>
    /// The native Talk test (00636284) also needs the leader facing the target within 64 of 256
    /// direction units. Auto walk stops on arrival and the player presses OK themselves, so the
    /// direction the walk left the leader in has to already qualify.
    /// </summary>
    private static int FacingError(FieldPositionSnapshot position, byte heading)
    {
        var toTarget = (int)Math.Round(Math.Atan2(-5d - position.X, -(-232d - position.Y)) * 128d / Math.PI) & 255;
        var difference = Math.Abs(toTarget - heading) & 255;
        return Math.Min(difference, 256 - difference);
    }

    private static IEnumerable<string> AutomaticArrivalRequiresFacingEvenAtTheFinalWaypoint(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var planner = wrap(Planner(createWalkmeshReader, withModel: false));
        var target = Innkeeper();
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([], npcTargetProvider: _ => [target]), planner);
        var mesh = createWalkmeshReader(Field).Read(GroundFloorArrival).Walkmesh!;
        FieldPositionSnapshot At(int y, byte direction) => new(FieldPositionReader.FieldModule, Field, 0, -5, y, 0,
            (ushort)FieldWalkmeshPathfinder.ResolveTriangle(mesh, -5, y, 0, -1), direction);
        var start = At(-370, 64);
        var transform = new FieldNavigationControlTransform(0);
        for (var guard = 0; guard < 8 && controller.CurrentCategory != FieldNavigationCategory.Npcs; guard++)
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        Begin(planner);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Begin(planner);
        if (!controller.TryResolveAutomaticInput(start, transform, 80, out _))
        {
            yield return "facing boundary regression: could not start the automatic approach";
            yield break;
        }

        // 00636284 initializes the best angular error to 0x40 and replaces it only
        // for a STRICTLY smaller value. At x=-5, the direction to the innkeeper is
        // exactly 128 even in 00636515's quantized lookup; heading 64 cannot Talk.
        // Remaining route length is already below 110 here, so that older arrival
        // branch must not bypass the new automatic-facing requirement.
        var sideways = At(-338, 64);
        Begin(planner);
        var speech = controller.UpdateLiveTracking(sideways, new(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: DateTime.UnixEpoch.AddMilliseconds(80))?.Speech;
        if (speech?.Contains("Innkeeper reached", StringComparison.Ordinal) == true)
            yield return "automatic arrival announced reached at exactly 64 direction units off the NPC";
        Begin(planner);
        if (!controller.TryResolveAutomaticInput(sideways, transform, 80, out var turn) ||
            FieldNavigationMovementObserver.PredictWorldDirection(turn, transform).Y <= 0)
            yield return "automatic arrival at the final waypoint did not turn toward the NPC";
        Begin(planner);
        speech = controller.UpdateLiveTracking(At(-338, 128), new(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: DateTime.UnixEpoch.AddMilliseconds(160))?.Speech;
        if (speech?.Contains("Innkeeper reached", StringComparison.Ordinal) != true)
            yield return "automatic arrival did not complete after facing the NPC";
    }

    /// <summary>
    /// A search the budget stops is not evidence of a lock. A strip too narrow for the body, a
    /// target whose reach (1140, the largest in the installed baseline) covers all of it, and an
    /// unrelated native lock: the search has to stop at its budget, promptly, and must not name
    /// the lock as what is in the way.
    /// </summary>
    private static IEnumerable<string> AnExhaustedSearchIsNotALock(
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var mesh = FieldBodyClearanceRouteTests.BuildMesh([
            ((0, 0, 0), (4000, 0, 0), (4000, 20, 0)),
            ((0, 0, 0), (4000, 20, 0), (0, 20, 0)),
            ((0, 20, 0), (4000, 20, 0), (2000, 400, 0))
        ]);
        var planner = wrap(new FieldWalkmeshRoutePlanner(FieldBodyClearanceRouteTests.ReaderFor(mesh),
            FieldBodyClearanceRouteTests.LockedTriangles(900, [2]), playerCollisionRadiusProvider: _ => PlayerRadius));
        var target = new FieldNavigationTarget(900, FieldNavigationCategory.Npcs, "far away", 2000, 600, 0, "synthetic:900:npc",
            InteractionRadius: 1140);
        var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 900, 0, 100, 10, 0,
            (ushort)FieldWalkmeshPathfinder.ResolveTriangle(mesh, 100, 10, 0, -1), 0);
        Begin(planner);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var routed = planner.TryBuildRoute(start, target, out _);
        var elapsed = clock.Elapsed.TotalMilliseconds;
        Console.WriteLine($"exhausted interaction search: {elapsed:0.0} ms, routed {routed}: {planner.LastDiagnostic}");
        if (routed)
        {
            yield return "a strip narrower than the body produced an interaction approach";
        }

        if (!planner.LastDiagnostic.Contains("budget exhausted (not a native lock)", StringComparison.Ordinal) ||
            planner is IFieldNavigationNativeBoundaryStatus { LastFailureWasNativeBoundary: true })
        {
            yield return $"an exhausted search was reported as a native lock: {planner.LastDiagnostic}";
        }

        if (elapsed > 60d)
        {
            yield return $"the bounded search still took {elapsed:0.0} ms";
        }
    }

    private static IEnumerable<string> WalkToTheCounter(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap,
        FieldPositionSnapshot start,
        ushort speed,
        int framesPerSample,
        bool withModel)
    {
        var name = $"from {start.X},{start.Y} speed {speed}, {framesPerSample} frames/sample{(withModel ? string.Empty : ", no other model")}";
        var planner = wrap(Planner(createWalkmeshReader, withModel));
        var target = Innkeeper();
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([], npcTargetProvider: _ => [target]), planner);
        var transform = new FieldNavigationControlTransform(0);
        var mesh = createWalkmeshReader(Field).Read(start).Walkmesh!;
        var (flat, originalIds, flatIds) = FieldBodyClearanceRouteTests.FlatPortion(mesh);
        var state = new FieldNavigationNativeMovementState(start.X * 4096, start.Y * 4096, 0, flatIds[start.TriangleId], 0);
        FieldPositionSnapshot Here() => new(FieldPositionReader.FieldModule, Field, 0, state.FixedX >> 12, state.FixedY >> 12, 0,
            (ushort)originalIds[state.TriangleId], state.Heading) { NativeFixedPosition = new(state.FixedX, state.FixedY, 0) };
        for (var guard = 0; guard < 8 && controller.CurrentCategory != FieldNavigationCategory.Npcs; guard++)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, Here(), transform);
        }

        Begin(planner);
        var selected = controller.HandleAction(FieldNavigationAction.ToggleBeacon, Here(), transform)?.Speech ?? string.Empty;
        if (!controller.BeaconEnabled)
        {
            yield return $"{name}: selecting the innkeeper did not start navigation: \"{selected}\"";
            yield break;
        }

        var input = FieldNavigationInput.None;
        var lockedFlat = flatIds.TryGetValue(Lock, out var flatLock) ? flatLock : -1;
        for (var tick = 0; tick < 700; tick++)
        {
            var position = Here();
            Begin(planner);
            var spoken = controller.UpdateLiveTracking(position, new(0, input), transform, false, 80,
                observedAt: DateTime.UnixEpoch.AddMilliseconds(tick * 80))?.Speech;
            if (spoken is not null)
            {
                var distance = Math.Sqrt(Math.Pow(position.X + 5, 2) + Math.Pow(position.Y + 232, 2));
                var facing = FacingError(position, state.Heading);
                if (!spoken.Contains("Innkeeper reached", StringComparison.Ordinal) || distance >= TalkReach || facing >= 64)
                {
                    yield return $"{name}: said \"{spoken}\" at {position.X},{position.Y}, {distance:0.0} from her (reach {TalkReach}), " +
                                 $"facing {facing} direction units off her (native Talk requires less than 64)";
                }
                else
                {
                    Console.WriteLine($"innkeeper reached {name}: {distance:0.0} away, facing {facing} off, heading {state.Heading}");
                }

                yield break;
            }

            Begin(planner);
            if (!controller.TryResolveAutomaticInput(position, transform, 80, out input))
            {
                input = FieldNavigationInput.None;
                continue;
            }

            var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
            var heading = unchecked((byte)(int)Math.Round(Math.Atan2(dx, -dy) * 128 / Math.PI));
            for (var frame = 0; frame < framesPerSample; frame++)
            {
                state = FieldNavigationNativeProbeMovement.Step(flat, state, heading, speed, PlayerRadius,
                    withModel ? [BlackCape] : [], triangle => triangle == lockedFlat).State;
            }
        }

        yield return $"{name}: never reached the counter; at {Here().X},{Here().Y} ({controller.LastNavigationDiagnostic})";
    }

    private static void Begin(IFieldNavigationRoutePlanner planner) =>
        planner.GetType().GetMethod("BeginObservation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public)?.Invoke(planner, null);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"innkeeper counter: {label}: expected {expected}, got {actual}.");
        }
    }
}
