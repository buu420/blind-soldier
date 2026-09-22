using Ff7.Accessibility.Reloaded;

internal static class NorthCorelAlternateCorridorTests
{
    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        NativeWitnessPhaseUsesTheInteriorOfTheSameGateway(createWalkmeshReader);
        DisappearingResidentsRebuildTheOrdinaryRoute(createWalkmeshReader);
        OtherFlatFieldsDoNotAssumeTheVerifiedHouseMovementSpeed(createWalkmeshReader);
        EntryHandoffConsidersAValidFallbackHeading(createWalkmeshReader);
        MovementReadStatusDistinguishesFailureFromCollision(createWalkmeshReader);
        LadderPlanningDropsOnlyTheSyntheticPositionPrecision(createWalkmeshReader);
        foreach (var grandfatherY in new[] { -3, 107 })
        {
            ClearResidentPhaseFindsNativeAlternateCorridor(createWalkmeshReader, grandfatherY);
            UnverifiedWallSqueezingRouteRemainsUnavailable(createWalkmeshReader, grandfatherY);
            TrackerRetainsSameTriangleAvoidanceBend(createWalkmeshReader, grandfatherY);
            foreach (var ticksPerObservation in new[] { 1, 2, 4, 8 })
                NativeControllerCrossesTheDoor(createWalkmeshReader, grandfatherY, ticksPerObservation);
        }
        NativeBoundaryClosesTheAlternatePassage(createWalkmeshReader);
        AuthoredCheckpointIsNotBypassed(createWalkmeshReader);
    }

    // Twelve native ticks are a400ms held-key stress, beyond the installed50ms
    // polling cadence. Keep the observed long-hold limitation separate.
    internal static void RunLongHoldStress(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var grandfatherY in new[] { -3, 107 })
            NativeControllerCrossesTheDoor(createWalkmeshReader, grandfatherY, 12);
    }

    // Exploratory stress model only: the native engine can rotate movement
    // heading on a wall hit. These translate/reject bursts intentionally omit
    // that behavior and are not a native-physics regression expectation.
    internal static void RunTranslationOnlyStress(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var grandfatherY in new[] { -3, 107 })
        foreach (var stepLength in new[] { 4, 8, 16, 32 })
            AutomaticMovementReachesTheNativeDoor(createWalkmeshReader, grandfatherY, stepLength);
    }

    private static readonly FieldPositionSnapshot CapturedStart = new(1, 453, 0, 137, 167, -10, 7, 192);
    private static readonly FieldNavigationTarget Exit = new(453, FieldNavigationCategory.Exits,
        "Exit to North Corel", -26, -217, 0, "regression:north-corel-house-return",
        CompletesOnArrival: true, TriggerLine: new(-75, -217, 0, 23, -217, 0));

    private static FieldNavigationDynamicObstacle[] Residents(int womanX, int womanY, int grandfatherY) =>
    [
        new(3, womanX, womanY, 0, 27.5, 30),
        new(4, 119, grandfatherY, 0, 30, 30)
    ];

    private static void NativeWitnessPhaseUsesTheInteriorOfTheSameGateway(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var grandfatherY in new[] { -3, 107 })
        {
            var reader = createWalkmeshReader(453);
            var ordinary = new FieldWalkmeshRoutePlanner(reader);
            Check(ordinary.TryBuildRoute(CapturedStart, Exit, out var original), ordinary.LastDiagnostic);
            Check(original.FinalApproach.X == 23,
                "The regression must retain the original closest approach beside the native x24 wall.");
            var residents = Residents(113, 211, grandfatherY);
            var planner = new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => residents);
            var endpointRepresentative = Exit with { X = 23 };
            Check(planner.TryBuildRoute(CapturedStart, endpointRepresentative, out var route),
                "Exactly replayed native input witnesses cross the doorway in this resident phase; " + planner.LastDiagnostic);
            Console.WriteLine($"North Corel native witness, residentY={grandfatherY}: {planner.LastDiagnostic}");
            Check(route.TargetTriangle == original.TargetTriangle && route.TargetTriggerLine == Exit.TriggerLine &&
                  route.FinalApproach.Y == -217 && route.FinalApproach.Z == 0 &&
                  route.FinalApproach.X > -75 && route.FinalApproach.X < 23,
                "The alternate approach must use the interior of the same native gateway and floor.");
            Check(route.FinalApproachToTargetDistance == 0d,
                "A validated native-line point has no phantom distance to the line's representative endpoint.");
        }
    }

    private static void DisappearingResidentsRebuildTheOrdinaryRoute(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        IReadOnlyList<FieldNavigationDynamicObstacle> residents = Residents(113, 211, -3);
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(453), dynamicObstacleProvider: (_, _) => residents);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([Exit]), planner);
        var transform = new FieldNavigationControlTransform(-128);
        while (controller.CurrentCategory != Exit.Category)
            controller.HandleAction(FieldNavigationAction.NextCategory, CapturedStart, transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, CapturedStart, transform);
        controller.UpdateLiveTracking(CapturedStart, default, transform, false, 80);
        Check(controller.CurrentRouteGuidance?.UsesNativeProbeClearance == true,
            "The resident fixture must first activate the native-probe corridor.");
        residents = Array.Empty<FieldNavigationDynamicObstacle>();
        controller.UpdateLiveTracking(CapturedStart, default, transform, false, 80);
        Check(controller.CurrentRouteGuidance is { UsesNativeProbeClearance: false, Replanned: true } &&
              controller.TryResolveAutomaticInput(CapturedStart, transform, 80, out _),
            "Removing the residents must rebuild an ordinary route and retain directional input.");
    }

    private static void OtherFlatFieldsDoNotAssumeTheVerifiedHouseMovementSpeed(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // Reuse the exact proven geometry and residents while changing only
        // field identity. Flat triangles alone do not establish native speed.
        var residents = Residents(113, 211, -3);
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(453),
            dynamicObstacleProvider: (_, _) => residents);
        Check(!planner.TryBuildRoute(CapturedStart with { FieldId = 454 },
                Exit with { FieldId = 454 }, out _),
            "The new native-probe fallback must not assume the verified house's walking speed in another flat field.");
    }

    private static void ClearResidentPhaseFindsNativeAlternateCorridor(
        Func<int, FieldWalkmeshReader> createWalkmeshReader, int grandfatherY)
    {
        var reader = createWalkmeshReader(453);
        var mesh = reader.Read(CapturedStart).Walkmesh!;
        var residents = Residents(113, 211, grandfatherY);
        var nativePlanner = new FieldWalkmeshRoutePlanner(reader);
        Check(nativePlanner.TryBuildRoute(CapturedStart, Exit, out var ordinary), nativePlanner.LastDiagnostic);
        var planner = new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => residents);
        Check(planner.TryBuildRoute(CapturedStart, Exit, out var plan),
            "The native input witness proves the left passage in the woman-near phase; " + planner.LastDiagnostic);
        Check(plan.UsesNativeProbeClearance && plan.TargetTriggerLine == Exit.TriggerLine &&
              plan.TargetTriangle == ordinary.TargetTriangle,
            "Alternate corridor must preserve the native gateway line/floor and carry its full-probe clearance mode.");
        Check(plan.TrianglePath.Contains(11) && plan.TrianglePath.Contains(13) && !plan.TrianglePath.Contains(5),
            "The returned corridor must describe the actual clear left passage, not the blocked right passage.");
        var steps = plan.StableWaypointsOverride;
        Check(steps is { Count: > 1 }, "The alternate passage needs explicit model-avoidance turns.");
        var current = new FieldNavigationRouteWaypoint(CapturedStart.X, CapturedStart.Y, CapturedStart.Z);
        var triangle = (int)CapturedStart.TriangleId;
        var traversed = new List<int> { triangle };
        var nativeEntry = new FieldNavigationNativeMovementState(CapturedStart.X << 12,
            CapturedStart.Y << 12, CapturedStart.Z << 12, CapturedStart.TriangleId, CapturedStart.Direction);
        var entryIndex = 0;
        foreach (var step in steps!)
        {
            var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, triangle, current, step.Waypoint, applyPortalInset: false);
            Check(trace.IsClear, "Every alternate leg must pass native geometry: " + trace.Diagnostic);
            if (step.IsNativeEntryStep)
            {
                // Independent native entry-prefix search found DownRight,
                // then Down. These short retry arcs are not straight sweeps
                // through the rounded destination coordinates.
                Check(grandfatherY == 107 && entryIndex < 2, "The verified entry prefix is bounded to two native ticks.");
                var movement = FieldNavigationNativeProbeMovement.Step(mesh, nativeEntry,
                    entryIndex++ == 0 ? (byte)32 : (byte)0, 1024, 30, residents);
                Check(movement.IsSupported && movement.Moved, "Each entry arc must pass every exact native wall/model probe.");
                nativeEntry = movement.State;
                Check(step.Waypoint == new FieldNavigationRouteWaypoint((int)Math.Round(nativeEntry.FixedX / 4096d),
                    (int)Math.Round(nativeEntry.FixedY / 4096d), nativeEntry.FixedZ >> 12),
                    "Derived entry points must retain the independently verified native arc endpoints.");
            }
            else
            {
                Check(!FieldNavigationDynamicObstacleGeometry.IntersectsAny(current, step.Waypoint, residents),
                    "Every straight alternate leg must clear all unchanged native player probes.");
                Check(planner.IsNativeProbeMovementClear(CapturedStart with
                    { X = current.X, Y = current.Y, Z = current.Z, TriangleId = (ushort)triangle }, Exit, step.Waypoint),
                    "Every straight generated leg must clear native center, all three wall/model probes, and live boundaries.");
            }
            Check(step.Waypoint.Z == 0, "Generated house bends use the native floor height.");
            traversed.AddRange(trace.TraversedTriangles.Skip(1));
            current = step.Waypoint;
            triangle = trace.EndTriangle;
        }
        Check(traversed.SequenceEqual(plan.TrianglePath), "Route metadata must match the freshly traced alternate corridor.");
        Check(current == plan.FinalApproach, "The route must finish at its validated interior native gateway approach.");
    }

    private static void TrackerRetainsSameTriangleAvoidanceBend(
        Func<int, FieldWalkmeshReader> createWalkmeshReader, int grandfatherY)
    {
        var reader = createWalkmeshReader(453);
        var mesh = reader.Read(CapturedStart).Walkmesh!;
        var residents = Residents(113, 211, grandfatherY);
        var planner = new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => residents);
        var tracker = new FieldNavigationRouteTracker(planner);
        Check(tracker.TryStart(CapturedStart, Exit, out _), planner.LastDiagnostic);
        var bends = tracker.CurrentProbeSnapshot!.StableWaypoints;
        var sameTriangleIndex = -1;
        for (var index = 1; index < bends.Count - 1; index++)
            if (bends[index].RequiredPortalIndex == bends[index - 1].RequiredPortalIndex)
            {
                sameTriangleIndex = index;
                break;
            }
        Check(sameTriangleIndex > 0, "The native model fixture must require two turns inside the same triangle.");
        for (var index = 0; index < sameTriangleIndex; index++)
        {
            var bend = bends[index].Waypoint;
            var triangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, bend.X, bend.Y, 0, -1);
            var position = CapturedStart with { X = bend.X, Y = bend.Y, Z = -10, TriangleId = (ushort)triangle };
            Check(tracker.TryUpdate(position, Exit, out var guidance), planner.LastDiagnostic);
            if (index == sameTriangleIndex - 1)
            {
                Check(tracker.CurrentProbeSnapshot!.WaypointIndex == sameTriangleIndex,
                    "Entering a triangle cannot satisfy a later avoidance turn within that triangle.");
                Check(guidance.Waypoint == bends[sameTriangleIndex].Waypoint,
                    "The next guidance must retain the unvisited same-triangle avoidance bend.");
                Check(planner.IsNativeProbeMovementClear(position, Exit, guidance.Waypoint),
                    "The retained bend must provide a clear movement direction, not point through the woman.");
            }
        }
    }

    private static void AutomaticMovementReachesTheNativeDoor(
        Func<int, FieldWalkmeshReader> createWalkmeshReader, int grandfatherY, int stepLength)
    {
        var reader = createWalkmeshReader(453);
        var mesh = reader.Read(CapturedStart).Walkmesh!;
        var residents = Residents(113, 211, grandfatherY);
        var planner = new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => residents);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([Exit]), planner);
        var transform = new FieldNavigationControlTransform(-128);
        while (controller.CurrentCategory != Exit.Category)
            controller.HandleAction(FieldNavigationAction.NextCategory, CapturedStart, transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, CapturedStart, transform);
        var position = CapturedStart;
        var lastInput = FieldNavigationInput.None;
        for (var sample = 0; sample < 600; sample++)
        {
            controller.UpdateLiveTracking(position, new(0, lastInput), transform, false, 80,
                observedAt: DateTime.UnixEpoch.AddMilliseconds(sample * 34));
            var context = $"native house return, grandfather={grandfatherY}, step={stepLength}, sample={sample}, " +
                $"position={position.X},{position.Y},{position.Z}, triangle={position.TriangleId}";
            Check(controller.TryResolveAutomaticInput(position, transform, 80, out var input),
                context + " must keep a clear automatic direction: " + controller.LastNavigationDiagnostic);
            var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
            // Larger logged deltas span several native movement frames under
            // held input. The game rejects the first blocked frame; it cannot
            // teleport the entire requested sampling distance through a wall.
            var nativeStep = stepLength <= 8 ? stepLength : 4;
            for (var frame = 0; frame < stepLength / nativeStep; frame++)
            {
                var current = new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z);
                var next = new FieldNavigationRouteWaypoint(position.X + (int)Math.Round(dx * nativeStep),
                    position.Y + (int)Math.Round(dy * nativeStep), position.Z);
                var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, position.TriangleId, current, next);
                var modelBlocked = FieldNavigationDynamicObstacleGeometry.IntersectsAny(current, next, residents);
                if (stepLength > 8 && (!trace.IsClear || modelBlocked)) break;
                Check(trace.IsClear, context + $" emitted {input} must stay on native geometry: {trace.Diagnostic}");
                Check(!modelBlocked, context + $" emitted {input} must clear every native model probe.");
                if (next.Y <= -217 && next.X >= -75 && next.X <= 23) return;
                position = position with { X = next.X, Y = next.Y, TriangleId = (ushort)trace.EndTriangle };
            }
            lastInput = input;
        }
        throw new InvalidOperationException("Automatic return must cross the native doorway without a bend loop.");
    }

    private static void NativeControllerCrossesTheDoor(
        Func<int, FieldWalkmeshReader> createWalkmeshReader, int grandfatherY, int ticksPerObservation)
    {
        var reader = createWalkmeshReader(453);
        var mesh = reader.Read(CapturedStart).Walkmesh!;
        var residents = Residents(113, 211, grandfatherY);
        var planner = new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => residents);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([Exit]), planner);
        var transform = new FieldNavigationControlTransform(-128);
        var native = new FieldNavigationNativeMovementState(CapturedStart.X << 12,
            CapturedStart.Y << 12, CapturedStart.Z << 12, CapturedStart.TriangleId, CapturedStart.Direction);
        while (controller.CurrentCategory != Exit.Category)
            controller.HandleAction(FieldNavigationAction.NextCategory, CapturedStart, transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, CapturedStart, transform);
        var lastInput = FieldNavigationInput.None;
        var stationary = 0;
        for (var sample = 0; sample < 600; sample++)
        {
            // ReadNavigation uses arithmetic shifts. The verified helper keeps
            // fixed XY between native ticks and assumes the flat mesh's Z0 floor
            // after a successful move; no rendered-origin offset is invented.
            var position = CapturedStart with { X = native.FixedX >> 12, Y = native.FixedY >> 12,
                Z = native.FixedZ >> 12, TriangleId = (ushort)native.TriangleId, Direction = native.Heading,
                NativeFixedPosition = new(native.FixedX, native.FixedY, native.FixedZ) };
            controller.UpdateLiveTracking(position, new(0, lastInput), transform, false, 80,
                observedAt: DateTime.UnixEpoch.AddMilliseconds(sample * ticksPerObservation * 1000d / 30d));
            Check(controller.TryResolveAutomaticInput(position, transform, 80, out var input),
                $"Native house return must retain an input, grandfather={grandfatherY}, ticks={ticksPerObservation}, " +
                $"sample={sample}, position={position.X},{position.Y}: {controller.LastNavigationDiagnostic}");
            var heading = input switch
            {
                FieldNavigationInput.Up => 128, FieldNavigationInput.UpRight => 96,
                FieldNavigationInput.Right => 64, FieldNavigationInput.DownRight => 32,
                FieldNavigationInput.Down => 0, FieldNavigationInput.DownLeft => 224,
                FieldNavigationInput.Left => 192, FieldNavigationInput.UpLeft => 160,
                _ => throw new InvalidOperationException("The replay must never emit a gameplay action.")
            };
            for (var tick = 0; tick < ticksPerObservation; tick++)
            {
                var before = native;
                var step = FieldNavigationNativeProbeMovement.Step(mesh, native, (byte)heading,
                    1024, 30, residents);
                Check(step.IsSupported, "The installed flat native room must be supported by the verified movement helper.");
                native = step.State;
                stationary = step.Moved ? 0 : stationary + 1;
                Check(stationary < 120, $"Native model/wall probes cannot remain stationary: grandpa={grandfatherY}, ticks={ticksPerObservation}, " +
                    $"sample={sample}, position={native.FixedX >> 12},{native.FixedY >> 12}, heading={heading}; {controller.LastNavigationDiagnostic}");
                var lineY = -217L * 4096;
                if (before.FixedY > lineY && native.FixedY <= lineY)
                {
                    var amount = (lineY - before.FixedY) / (double)(native.FixedY - before.FixedY);
                    var crossingX = before.FixedX + (native.FixedX - before.FixedX) * amount;
                    if (crossingX >= -75L * 4096 && crossingX <= 23L * 4096) return;
                }
            }
            lastInput = input;
        }
        throw new InvalidOperationException("The native controller replay must cross the original doorway without a route loop.");
    }

    private static void UnverifiedWallSqueezingRouteRemainsUnavailable(
        Func<int, FieldWalkmeshReader> createWalkmeshReader, int grandfatherY)
    {
        var residents = Residents(-69, 195, grandfatherY);
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(453),
            dynamicObstacleProvider: (_, _) => residents);
        Check(!planner.TryBuildRoute(CapturedStart, Exit, out _),
            "The old center-only wall-squeezing route failed the native replay; do not return it as usable.");
    }

    private static void NativeBoundaryClosesTheAlternatePassage(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        const int globalObject = 0x02400000;
        var blocked = true;
        byte ReadByte(int address) => address == FieldPositionReader.AddressCurrentModule ? (byte)1 :
            address == FieldPositionReader.AddressFieldId ? (byte)(453 & 255) :
            address == FieldPositionReader.AddressFieldId + 1 ? (byte)(453 >> 8) :
            address == globalObject + FieldBoundaryStateReader.BoundaryBitsOffset + 1 && blocked ? (byte)4 : (byte)0;
        var boundaries = new FieldBoundaryStateReader(
            address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? globalObject : 0,
            ReadByte, (_, _) => true);
        var reader = createWalkmeshReader(453);
        var residents = Residents(113, 211, 107);
        var planner = new FieldWalkmeshRoutePlanner(reader, boundaries, dynamicObstacleProvider: (_, _) => residents);
        Check(!planner.TryBuildRoute(CapturedStart, Exit, out _) && planner.LastReadWasCoherent,
            "A native IDLCK boundary on triangle10 must block the alternate passage with a coherent no-route result.");
        blocked = false;
        Check(planner.TryBuildRoute(CapturedStart, Exit, out var reopened) && reopened.TrianglePath.Contains(10),
            "Clearing the same live native boundary must reopen the alternate passage.");
    }

    private static void AuthoredCheckpointIsNotBypassed(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var target = Exit with { RouteDetour = new(new(100, 100, 0, 145, 100, 0), 153, 110, 0) };
        var reader = createWalkmeshReader(453);
        var ordinary = new FieldWalkmeshRoutePlanner(reader);
        Check(ordinary.TryBuildRoute(CapturedStart, target, out var required) &&
              required.StableWaypointsOverride?.Any(step => step.MustReach) == true,
            "The fixture must contain a real required authored checkpoint on the otherwise available right passage.");
        var residents = Residents(-69, 195, 107);
        var planner = new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => residents);
        Check(!planner.TryBuildRoute(CapturedStart, target, out _),
            "The alternate route must not discard an authored checkpoint that the resident blocks.");
    }

    private static void LadderPlanningDropsOnlyTheSyntheticPositionPrecision(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var position = CapturedStart with { NativeFixedPosition = new((137 << 12) + 100, (167 << 12) + 200, -10 << 12) };
        Check(FieldNavigationController.ResolveRoutePlanningPosition(position, FieldLadderStateSnapshot.NotMounted) == position,
            "Ordinary planning must retain exact native coordinates.");
        var projected = FieldNavigationController.ResolveRoutePlanningPosition(position,
            new(true, true, FieldLadderPhase.Mounted, FieldNavigationInput.Up, new(12, 30, 120), 2, 1, 0));
        Check(projected.X == 12 && projected.Y == 30 && projected.Z == 120 && projected.TriangleId == 2 &&
              projected.NativeFixedPosition is null,
            "A synthetic ladder landing must drop raw coordinates from the original player location.");
    }

    private static void MovementReadStatusDistinguishesFailureFromCollision(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var destination = new FieldNavigationRouteWaypoint(137, 163, -10);
        (string Name, Func<FieldWalkmeshRoutePlanner, bool> Read)[] readers =
        [
            ("ordinary", planner => planner.IsAutomaticMovementClear(CapturedStart, Exit, destination)),
            ("straight native", planner => planner.IsNativeProbeMovementClear(CapturedStart, Exit, destination)),
            ("immediate native", planner => planner.IsNativeProbeAutomaticMovementClear(CapturedStart, Exit, destination, 0))
        ];
        var failures = new List<string>();
        foreach (var check in readers)
        {
            FieldNavigationDynamicObstacle[] far = [new(3, 5000, 5000, 0, 30, 30)];
            var unreadable = new FieldWalkmeshRoutePlanner(new FieldWalkmeshReader(_ => 0, _ => 0),
                dynamicObstacleProvider: (_, _) => far);
            if (check.Read(unreadable) || unreadable.LastReadWasCoherent ||
                !unreadable.LastDiagnostic.Contains("field data pointer is null")) failures.Add(check.Name + " unreadable mesh");
            var missingBoundary = new FieldWalkmeshRoutePlanner(createWalkmeshReader(453),
                new FieldBoundaryStateReader(_ => 0, _ => 0, (_, _) => true), dynamicObstacleProvider: (_, _) => far);
            if (check.Read(missingBoundary) || missingBoundary.LastReadWasCoherent ||
                !missingBoundary.LastDiagnostic.Contains("dynamic boundaries unavailable")) failures.Add(check.Name + " unreadable boundary");
            FieldNavigationDynamicObstacle[] blocked = [new(3, 137, 167, 0, 50, 30)];
            var collision = new FieldWalkmeshRoutePlanner(createWalkmeshReader(453), dynamicObstacleProvider: (_, _) => blocked);
            collision.TryResolvePlayerTriangle(CapturedStart with { CurrentModule = 0 }, out _);
            if (check.Read(collision) || !collision.LastReadWasCoherent) failures.Add(check.Name + " stale failure after coherent collision");
        }
        Check(failures.Count == 0, "Movement read status must separate unreadable native state from a real collision: " + string.Join(", ", failures));
    }

    private static void EntryHandoffConsidersAValidFallbackHeading(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var planner = new EntryFallbackPlanner();
        var tracker = new FieldNavigationRouteTracker(planner);
        var position = CapturedStart with { X = 0, Y = 0, Z = 0, TriangleId = 0 };
        Check(tracker.TryStart(position, Exit, out _) && tracker.TryUpdate(position, Exit, out var guidance) &&
              guidance.Waypoint == new FieldNavigationRouteWaypoint(2, -4, 0),
            "An arrived native entry must accept a legal forward fallback heading to the next short entry step.");
        Check(planner.Headings.Contains(32) && planner.Headings.Contains(0),
            "Entry handoff must try the blocked nearest heading and then the verified forward alternative.");
    }

    private sealed class EntryFallbackPlanner : IFieldNavigationRoutePlanner, IFieldNavigationAutomaticMovementPlanner
    {
        public List<byte?> Headings { get; } = [];
        public string LastDiagnostic => "entry handoff fixture";
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle) { triangle = 0; return true; }
        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint) { waypoint = new(2, 0, 0); return true; }
        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan)
        {
            plan = new(453, $"453:{target.StableId}", [0], [], new(100, -4, 0), 0,
                StableWaypointsOverride: [new(new(2, 0, 0), 0, true, true, true),
                    new(new(2, -4, 0), 0, true, true, true), new(new(100, -4, 0), 0)],
                UsesNativeProbeClearance: true);
            return true;
        }
        public bool IsAutomaticMovementClear(FieldPositionSnapshot position, FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination) => true;
        public bool IsNativeProbeAutomaticMovementClear(FieldPositionSnapshot position, FieldNavigationTarget target,
            FieldNavigationRouteWaypoint destination, byte? requestedHeading = null)
        { Headings.Add(requestedHeading); return requestedHeading == 0; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
