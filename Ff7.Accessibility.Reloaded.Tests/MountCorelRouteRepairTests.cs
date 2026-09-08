using Ff7.Accessibility.Reloaded;

internal static class MountCorelRouteRepairTests
{
    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        EnteringTheApproachTriangleDoesNotSkipTheUpperTrackCorner(createWalkmeshReader);
        AutomaticDirectionsCrossTheNativeUpperTrackJunction(createWalkmeshReader);
        CapturedWideStairAscentKeepsItsVisibleContinuation(createWalkmeshReader);
        VisibleContinuationPreservesNativeAndInteractionGates(createWalkmeshReader);
    }

    private static void VisibleContinuationPreservesNativeAndInteractionGates(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(230);
        var planner = new FieldWalkmeshRoutePlanner(reader);
        var start = new FieldPositionSnapshot(1, 230, 0, -102, 187, 2722, 79, 0);
        var target = new FieldNavigationTarget(230, FieldNavigationCategory.Exits,
            "Exit to Shinra Bldg. 59f", 175, 403, 3069, "regression:shinra-required-corner-gates");
        Check(planner.TryBuildRoute(start, target, out var plan), planner.LastDiagnostic);
        var steps = FieldWalkmeshPathfinder.BuildStableWaypoints(start.X, start.Y, start.Z,
            plan.Portals, plan.FinalApproach).ToArray();
        var cornerIndex = Array.FindIndex(steps, step => step.MustReach);
        var corner = steps[cornerIndex];
        var current = start with { X = 94, Y = 60, Z = 2787, TriangleId = 47 };
        var mesh = reader.Read(current).Walkmesh!;
        var nextTriangle = plan.Portals[corner.RequiredPortalIndex - 1].ToTriangle;

        FieldNavigationCorridorObservation Observe(
            IReadOnlyList<FieldNavigationRouteStep> routeSteps,
            Func<int, bool>? blocked = null,
            IReadOnlyList<FieldNavigationDynamicObstacle>? obstacles = null,
            FieldNavigationRouteAction? action = null)
        {
            Check(FieldNavigationCorridorLookahead.TryResolve(mesh, 47, current, plan, routeSteps,
                cornerIndex, action, default, blocked, obstacles, out var observation),
                "the captured wide ramp must yield a corridor observation");
            return observation;
        }

        Check(Observe(steps).StableWaypointIndex > cornerIndex,
            "the clear native continuation is available before applying each safety gate");
        Check(Observe(steps, blocked: triangle => triangle == nextTriangle).StableWaypointIndex == cornerIndex,
            "a blocked native continuation triangle must retain the required corner");
        Check(Observe(steps, obstacles: [new(1, 215, 109, 2870, 24, 0)]).StableWaypointIndex == cornerIndex,
            "a model blocking the continuation must retain the required corner");
        Check(Observe(steps, action: new(FieldNavigationTransitionKind.Ladder, "regression:required-action",
                corner.Waypoint, FieldNavigationInput.Up, RequiresAction: true,
                PortalIndex: corner.RequiredPortalIndex)).StableWaypointIndex == cornerIndex,
            "the continuation must not bypass an intervening action");

        var authoredCheckpoint = steps.ToArray();
        authoredCheckpoint[cornerIndex] = corner with
        {
            Waypoint = corner.Waypoint with { X = corner.Waypoint.X + 1 }
        };
        Check(Observe(authoredCheckpoint).StableWaypointIndex == cornerIndex,
            "an authored checkpoint near a portal endpoint must not inherit native-corner visibility skipping");
    }

    private static void CapturedWideStairAscentKeepsItsVisibleContinuation(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(230);
        var planner = new FieldWalkmeshRoutePlanner(reader);
        var tracker = new FieldNavigationRouteTracker(planner);
        var target = new FieldNavigationTarget(230, FieldNavigationCategory.Exits,
            "Exit to Shinra Bldg. 59f", 175, 403, 3069, "regression:shinra-emergency-stairs-top");
        var start = new FieldPositionSnapshot(1, 230, 0, -102, 187, 2722, 79, 0);
        FieldPositionSnapshot[] capturedAscent =
        [
            start with { Y = 139, TriangleId = 80 },
            start with { Y = 99, TriangleId = 80 },
            start with { Y = 67, TriangleId = 80 },
            start with { X = -72, Y = 60, Z = 2721, TriangleId = 81 },
            start with { X = -24, Y = 60, Z = 2721, TriangleId = 82 },
            start with { X = 19, Y = 60, Z = 2741, TriangleId = 47 },
            start with { X = 53, Y = 60, Z = 2762, TriangleId = 47 },
            start with { X = 94, Y = 60, Z = 2787, TriangleId = 47 }
        ];
        Check(tracker.TryStart(start, target, out _), planner.LastDiagnostic);
        var previous = start;
        FieldNavigationRouteGuidance guidance = default;
        foreach (var current in capturedAscent)
        {
            var dx = current.X - previous.X;
            var dy = current.Y - previous.Y;
            Check(tracker.TryUpdate(current, target, new(true, dx != 0 || dy != 0,
                    FieldNavigationInput.Right, dx, dy, Math.Sqrt(dx * dx + dy * dy), "captured Shinra ascent"),
                out guidance), planner.LastDiagnostic);
            Check(!guidance.Replanned, "the captured native stair ascent must retain its route");
            previous = current;
        }

        Check(guidance.Waypoint.X > previous.X,
            "the wide native ramp has a clear continuation and must not point back to its missed endpoint: " +
            guidance.Diagnostic);
        Check(FieldWalkmeshPathfinder.TraceWalkableSegment(reader.Read(start).Walkmesh!, 47,
                new(previous.X, previous.Y, previous.Z), guidance.Waypoint).IsClear,
            "guidance past the missed stair endpoint must still be directly walkable");
    }

    private static void EnteringTheApproachTriangleDoesNotSkipTheUpperTrackCorner(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(462);
        var planner = Planner(reader);
        var start = Position(1276, -27, 689, 235);
        var target = Target();
        var mesh = reader.Read(start).Walkmesh!;
        var entry = Position(1264, -16, 694, 234);
        var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, 235,
            new(start.X, start.Y, start.Z), new(entry.X, entry.Y, entry.Z));
        Check(trace.IsClear && trace.EndTriangle == 234,
            "the approach sample must cross the installed 235-to-234 portal");
        // Check both a fresh start at the observed corner and the 48-portal
        // retained route that began farther down the slope in the live log.
        foreach (var origin in new[] { start, Position(1945, -52, -76, 59) })
        {
            var tracker = new FieldNavigationRouteTracker(planner);
            Check(tracker.TryStart(origin, target, out _), planner.LastDiagnostic);
            if (origin != start)
            {
                Check(tracker.CurrentProbeSnapshot?.Portals.Count == 48,
                    "the retained test must reproduce the live 48-portal route");
                Check(tracker.TryUpdate(start, target, default, out _), planner.LastDiagnostic);
            }

            Check(tracker.TryUpdate(entry, target, new(true, true, FieldNavigationInput.UpLeft,
                -12, 11, Math.Sqrt(265), "native approach portal"), out var guidance), planner.LastDiagnostic);
            Check(guidance.Waypoint != new FieldNavigationRouteWaypoint(2003, -8, 1054),
                "entering approach triangle 234 must retain the upper corner until triangle 36 is reachable; " +
                guidance.Diagnostic);
            Check(FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, 234,
                    new(entry.X, entry.Y, entry.Z), guidance.Waypoint).IsClear,
                "the retained upper-track guidance must be walkable from the approach triangle");
            Check(guidance.NextAction?.StableId == "jump:462:23:26:11:15",
                "the distant native upper-track jump remains pending");
        }
    }

    private static void AutomaticDirectionsCrossTheNativeUpperTrackJunction(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var stepLength in new[] { 4, 8 })
        {
            var reader = createWalkmeshReader(462);
            var planner = Planner(reader);
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([Target()]), planner);
            var position = Position(1276, -27, 689, 235);
            var mesh = reader.Read(position).Walkmesh!;
            var transform = new FieldNavigationControlTransform(-128);
            while (controller.CurrentCategory != FieldNavigationCategory.Story)
                controller.HandleAction(FieldNavigationAction.NextCategory, position, transform);
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, transform);
            var lastInput = FieldNavigationInput.None;
            var reachedUpperTrack = false;
            for (var sample = 0; sample < 100; sample++)
            {
                controller.UpdateLiveTracking(position, new(0, lastInput), transform, false, 80,
                    observedAt: DateTime.UnixEpoch.AddMilliseconds(sample * 34));
                var scenario = $"upper-track step={stepLength}, sample={sample}, " +
                    $"position={position.X},{position.Y},{position.Z}, triangle={position.TriangleId}";
                Check(controller.TryResolveAutomaticInput(position, transform, 80, out var input),
                    scenario + " must continue automatic movement");
                var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
                var next = new FieldNavigationRouteWaypoint(position.X + (int)Math.Round(dx * stepLength),
                    position.Y + (int)Math.Round(dy * stepLength), position.Z);
                var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, position.TriangleId,
                    new(position.X, position.Y, position.Z), next);
                Check(trace.IsClear, scenario + $" emitted {input} must clear the native portal: {trace.Diagnostic}");
                position = position with { X = next.X, Y = next.Y,
                    Z = Height(mesh.Triangles[trace.EndTriangle], next.X, next.Y),
                    TriangleId = (ushort)trace.EndTriangle };
                lastInput = input;
                Check(controller.CurrentRouteGuidance?.NextAction?.StableId == "jump:462:23:26:11:15",
                    scenario + " must preserve the native jump action");
                if (position.TriangleId == 35)
                {
                    reachedUpperTrack = true;
                    break;
                }
            }

            Check(reachedUpperTrack, $"step={stepLength} must cross both junction portals onto the upper track");
        }
    }

    private static FieldWalkmeshRoutePlanner Planner(FieldWalkmeshReader reader)
    {
        var scripts = new FieldScriptNavigationCatalog(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("Mount Corel repair tests need installed field data."));
        return new(reader, transitionProvider: field => scripts.ReadField(field).Transitions);
    }

    private static FieldPositionSnapshot Position(int x, int y, int z, ushort triangle) =>
        new(1, 462, 0, x, y, z, triangle, 160);

    private static FieldNavigationTarget Target() => new(462, FieldNavigationCategory.Story,
        "Take the upper tracks to the bridge switch", 2450, -8, 1040,
        "regression:mount-corel-upper-track", CompletesOnArrival: true,
        TriggerLine: new(2442, -39, 1057, 2457, 23, 1022));

    private static int Height(FieldWalkmeshTriangle triangle, int x, int y)
    {
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (b.Y - c.Y) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Y - c.Y);
        var wa = ((b.Y - c.Y) * (double)(x - c.X) + (c.X - b.X) * (double)(y - c.Y)) / denominator;
        var wb = ((c.Y - a.Y) * (double)(x - c.X) + (a.X - c.X) * (double)(y - c.Y)) / denominator;
        return (int)Math.Round(wa * a.Z + wb * b.Z + (1 - wa - wb) * c.Z);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

