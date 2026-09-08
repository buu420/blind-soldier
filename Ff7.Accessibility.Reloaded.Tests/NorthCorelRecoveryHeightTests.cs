using Ff7.Accessibility.Reloaded;

internal static class NorthCorelRecoveryHeightTests
{
    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // Native man3 pauses at (397,-327), then walks toward (239,-367).
        // These positions exercise the one-bend and two-bend avoidance paths.
        foreach (var movingModel in new[] { (397, -327), (389, -329), (350, -339) })
        {
            GeneratedGroundBendsRemainReachable(createWalkmeshReader, movingModel.Item1, movingModel.Item2);
            LiveRecoveryBendsUseTheNativeSurface(createWalkmeshReader, movingModel.Item1, movingModel.Item2);
        }
        RuntimeGeneratedTurnsCannotBeSatisfiedByTheirSharedTriangle();
    }

    private static void GeneratedGroundBendsRemainReachable(
        Func<int, FieldWalkmeshReader> createWalkmeshReader, int modelX, int modelY)
    {
        var start = new FieldPositionSnapshot(1, 450, 0, 523, -413, -10, 54, 0);
        var target = new FieldNavigationTarget(450, FieldNavigationCategory.Story,
            "Visit the left house (optional)", -417, -86, 213, "regression:north-corel-left-house",
            CompletesOnArrival: true, TriggerLine: new(-426, -109, 213, -409, -63, 213));
        var reader = createWalkmeshReader(450);
        var mesh = reader.Read(start).Walkmesh!;
        // Installed scale512, native default widths30, models9/10/11/13/14.
        // The resident on the higher floor is excluded by native vertical range.
        FieldNavigationDynamicObstacle[] models =
        [
            new(9, 421, 108, 0, 30, 30), new(10, 79, -455, 14, 30, 30),
            new(11, modelX, modelY, 0, 30, 30), new(13, -273, -367, 14, 30, 30),
            new(14, 95, -83, 14, 30, 30)
        ];
        var nativePlanner = new FieldWalkmeshRoutePlanner(reader);
        Check(nativePlanner.TryBuildRoute(start, target, out var nativePlan), nativePlanner.LastDiagnostic);
        var nativeSteps = FieldWalkmeshPathfinder.BuildStableWaypoints(start.X, start.Y, start.Z,
            nativePlan.Portals, nativePlan.FinalApproach);
        var planner = new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => models);
        Check(planner.TryBuildRoute(start, target, out var plan), planner.LastDiagnostic);
        var steps = plan.StableWaypointsOverride ?? throw new InvalidOperationException("Expected a native model bypass.");
        var generatedCount = steps.TakeWhile(step => !nativeSteps.Contains(step)).Count();
        Check(generatedCount is 1 or 2, "the fixture must exercise a generated one-bend or two-bend bypass");
        Check(plan.Portals.SequenceEqual(nativePlan.Portals), "avoidance projection preserves the native portal corridor");
        Check(plan.FinalApproach == nativePlan.FinalApproach && plan.FinalApproach.Z == 213,
            "the higher doorway is never projected onto the ground floor");

        var current = new FieldNavigationRouteWaypoint(start.X, start.Y, start.Z);
        var triangle = (int)start.TriangleId;
        for (var index = 0; index <= generatedCount; index++)
        {
            var step = steps[index];
            var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, triangle, current, step.Waypoint);
            Check(trace.IsClear, $"every projected recovery/rejoin leg must remain native-walkable: {trace.Diagnostic}");
            Check(!FieldNavigationDynamicObstacleGeometry.IntersectsAny(current, step.Waypoint, models),
                "every projected recovery/rejoin leg must still clear the native model probes");
            if (index < generatedCount)
            {
                var surface = mesh.Triangles[trace.EndTriangle];
                Check(surface.Vertex0.Z == 0 && surface.Vertex1.Z == 0 && surface.Vertex2.Z == 0,
                    "the installed bend fixture must lie on the ground floor");
                Check(step.Waypoint.Z == 0,
                    $"generated bend {step.Waypoint} must use native ground Z0, not an interpolated height " +
                    "toward the elevated house; Cloud's rendered Z-10 can never reach a bend over Z10");
                Check(step.MustReach, "projecting a recovery bend does not remove its mandatory arrival gate");
            }
            else
            {
                Check(nativeSteps.Contains(step) && step.Waypoint == new FieldNavigationRouteWaypoint(-202, -147, 90),
                    "the native elevated approach and its required-corner metadata remain unchanged");
            }

            current = step.Waypoint;
            triangle = trace.EndTriangle;
        }

        var tracker = new FieldNavigationRouteTracker(planner);
        Check(tracker.TryStart(start, target, out _), planner.LastDiagnostic);
        for (var index = 0; index < generatedCount; index++)
        {
            var bend = steps[index].Waypoint;
            var nativeTriangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, bend.X, bend.Y, 0, -1);
            var reached = start with { X = bend.X, Y = bend.Y, Z = -10, TriangleId = (ushort)nativeTriangle };
            Check(tracker.TryUpdate(reached, target, default, out _), planner.LastDiagnostic);
            Check(tracker.CurrentProbeSnapshot!.WaypointIndex > index,
                "reaching a projected bend at Cloud's live ground height advances the existing 20-unit gate");
        }
    }

    private static void LiveRecoveryBendsUseTheNativeSurface(
        Func<int, FieldWalkmeshReader> createWalkmeshReader, int modelX, int modelY)
    {
        var start = new FieldPositionSnapshot(1, 450, 0, 523, -413, -10, 54, 0);
        var target = new FieldNavigationTarget(450, FieldNavigationCategory.Story,
            "Visit the left house (optional)", -417, -86, 213, "regression:live-house-recovery",
            CompletesOnArrival: true, TriggerLine: new(-426, -109, 213, -409, -63, 213));
        IReadOnlyList<FieldNavigationDynamicObstacle> residents = Array.Empty<FieldNavigationDynamicObstacle>();
        var reader = createWalkmeshReader(450);
        var planner = new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => residents);
        var tracker = new FieldNavigationRouteTracker(planner);
        Check(tracker.TryStart(start, target, out _), planner.LastDiagnostic);
        residents = [new(9, 421, 108, 0, 30, 30), new(10, 79, -455, 14, 30, 30),
            new(11, modelX, modelY, 0, 30, 30), new(13, -273, -367, 14, 30, 30), new(14, 95, -83, 14, 30, 30)];
        Check(tracker.TryUpdate(start, target, out var guidance), planner.LastDiagnostic);
        var probe = tracker.CurrentProbeSnapshot!;
        Check(guidance.Waypoint.Z == 0,
            $"Live corridor recovery must project generated ground guidance, found {guidance.Waypoint}: {guidance.Diagnostic}");
        var generated = probe.StableWaypoints.TakeWhile(step => step.Waypoint.Z < 90).ToArray();
        Check(generated.All(step => step.Waypoint.Z == 0),
            "The tracker must not splice a floating continuation into the live ground route.");
    }

    private static void RuntimeGeneratedTurnsCannotBeSatisfiedByTheirSharedTriangle()
    {
        var planner = new RuntimeRecoveryPlanner();
        var start = new FieldPositionSnapshot(1, 999, 0, 100, 100, 0, 0, 0);
        var target = new FieldNavigationTarget(999, FieldNavigationCategory.Objects,
            "Recovery test destination", 800, 100, 0, "runtime-recovery");
        var tracker = new FieldNavigationRouteTracker(planner);
        Check(tracker.TryStart(start, target, out _), "the unobstructed test route starts");
        planner.ResidentVisible = true;
        Check(tracker.TryUpdate(start, target, out _), "the live resident creates a recovery");
        var generated = tracker.CurrentProbeSnapshot!.StableWaypoints;
        Check(generated.Count == 3 && generated[0].MustReach && generated[1].MustReach,
            "the actual recovery generator must splice two bends before the same-triangle rejoin");
        var first = generated[0].Waypoint;
        Check(tracker.TryUpdate(start with { X = first.X, Y = first.Y }, target, out var guidance),
            "the first generated bend is reached");
        Check(tracker.CurrentProbeSnapshot!.WaypointIndex == 1 && guidance.Waypoint == generated[1].Waypoint,
            "reaching the first generated bend cannot satisfy the unvisited second bend in the same triangle");
    }

    private sealed class RuntimeRecoveryPlanner : IFieldNavigationRoutePlanner,
        IFieldNavigationCorridorLookaheadPlanner, IFieldNavigationAutomaticMovementPlanner
    {
        private readonly FieldWalkmesh mesh = new([new FieldWalkmeshTriangle(0,
            new(0, 0, 0), new(0, 1500, 0), new(1500, 0, 0), -1, -1, -1)]);
        private readonly FieldNavigationDynamicObstacle[] resident = [new(1, 400, 100, 0, 200, 30)];
        internal bool ResidentVisible { get; set; }
        private IReadOnlyList<FieldNavigationDynamicObstacle> Obstacles => ResidentVisible ? resident : [];
        public string LastDiagnostic => "single-triangle live recovery regression";
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, position.X, position.Y, position.Z, 0);
            return triangle == 0;
        }
        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan)
        {
            plan = new(999, "999:runtime-recovery", [0], [], new(800, 100, 0), 0);
            return true;
        }
        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new(800, 100, 0);
            return true;
        }
        public bool TryObserveCorridor(FieldPositionSnapshot position, FieldNavigationRoutePlan plan,
            IReadOnlyList<FieldNavigationRouteStep> steps, int index, FieldNavigationRouteAction? action,
            FieldNavigationRouteHeading heading, out FieldNavigationCorridorObservation observation) =>
            FieldNavigationCorridorLookahead.TryResolve(mesh, 0, position, plan, steps, index, action,
                heading, null, Obstacles, out observation);
        public bool IsAutomaticMovementClear(FieldPositionSnapshot position, FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination)
        {
            var start = new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z);
            return FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, 0, start, destination).IsClear &&
                   !FieldNavigationDynamicObstacleGeometry.IntersectsAny(start, destination, Obstacles);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
