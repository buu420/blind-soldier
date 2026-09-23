using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Shera and Palmer standing in Cid's house, and the way out past them.
///
/// <para>At 2026-09-22 15:54:53Z the capture has field 558 at GameMoment 550 with
/// <c>story=1</c> - the row was there and the read was coherent - and the controller
/// saying "Story, Go out into the town. direction unavailable" and then "Route
/// unavailable to Go out into the town. Navigation off." The footsteps immediately
/// before it put the party on triangle 27 at (-311,195).</para>
///
/// <para>rktsid's own Main scripts at that moment: <c>siera</c> (entity 23, model 9) is
/// placed at (-363,205,0) on triangle 28 and sets no SLIDR, so it keeps the engine's
/// default collision width; <c>palmer</c> (entity 24, model 10) is placed at
/// (-288,146,0) on triangle 27 and <c>SLIDR 48</c> runs past the branch join, so it
/// applies whichever way the branch went; <c>cloud</c> (entity 14, model 0) is
/// <c>SLIDR 34</c>. Palmer is therefore standing on the party's own triangle, between
/// them and the door.</para>
///
/// <para>Nothing here relaxes a collision. The models are real and the route still has
/// to clear them by the native half-sum width at the forward and +/-45-degree probes,
/// and every leg also has to pass the native wall probes. What the planner was missing
/// is that the way round is a hundred units in the other direction before the room opens
/// again, which no side-step bend can express.</para>
///
/// <para>An earlier note here said a 98-segment witness proved the way round. That
/// witness only checked the centre line against the walkmesh and the two bodies; it said
/// nothing about walls, and the first corridor built from it emitted five legs that all
/// failed <c>AreNativeWallProbesClear</c> - <c>(-249,392)</c> to <c>(-38,375)</c> puts
/// its start flank at <c>(-226,366)</c>, off the mesh entirely. The claim was wrong and
/// is withdrawn. Root's wall-aware witness is the one that stands: 113 short steps, every
/// one of them wall and model clear, at every Shera width in {20, 30, 34, 48}, with the
/// native LINE reached within 32 units rather than by walking to an approach point past
/// the floor edge.</para>
///
/// <para>These cases assert the shipping check itself rather than a copy of it:
/// <c>FieldWalkmeshRoutePlanner.AreNativeWallProbesClear</c> is invoked by reflection, so
/// renaming it breaks this file rather than quietly weakening the assertion.</para>
/// </summary>
internal static class CidHouseLiveModelRouteTests
{
    private const int Field = 558;
    private const int PlayerCollisionWidth = 34;
    private const int PalmerCollisionWidth = 48;

    /// <summary>Shera sets no SLIDR, so her width is bracketed rather than assumed.</summary>
    private static readonly int[] SheraCollisionWidths = [20, 30, 34, 48];

    /// <summary>line3, entity 8: the way out to the town.</summary>
    private static FieldNavigationTarget OutIntoTheTown() =>
        new(
            Field,
            FieldNavigationCategory.Story,
            "Go out into the town",
            -199, -12, 0,
            InteractionRadius: PlayerCollisionWidth - 1,
            TriggerLine: new FieldNavigationTriggerLine(-239, -9, 0, -160, -15, 0));

    /// <summary>Where the capture's own footsteps put the party.</summary>
    private static FieldPositionSnapshot Conversation() =>
        new(FieldPositionReader.FieldModule, Field, 0, -311, 195, 0, 27, 0);

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        TheWayOutIsFoundFromTheConversationPosition(createWalkmeshReader);
        TheRouteStillClearsBothModelsAtEveryStep(createWalkmeshReader);
        EveryEmittedLegPassesTheNativeWallProbes(createWalkmeshReader);
        TheLastLegEndsInsideTheNativeLineActivation(createWalkmeshReader);
        ALineOnAnotherFloorIsNotAnArrival();
        TheControllerTakesTheRouteRatherThanRefusingIt(createWalkmeshReader);
        TheRouteTrackerWalksEveryCornerAndAcceptsTheEnding(createWalkmeshReader);
        AnEmptyRoomKeepsItsOrdinaryStraightRoute(createWalkmeshReader);
        TheAnswerNeverWalksOverSomebody(createWalkmeshReader);
        SomewhereWithNoPathToItIsStillRefused(createWalkmeshReader);
        Console.WriteLine("Cid house live model route tests passed.");
    }

    /// <summary>The defect: no route at all from the spot the player was standing on.</summary>
    private static void TheWayOutIsFoundFromTheConversationPosition(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var sheraWidth in SheraCollisionWidths)
        {
            var reader = createWalkmeshReader(Field);
            var planner = new FieldWalkmeshRoutePlanner(
                reader,
                dynamicObstacleProvider: (_, _) => Models(sheraWidth));
            Equal(true, planner.TryBuildRoute(Conversation(), OutIntoTheTown(), out var plan),
                $"the way out must be found with Shera at width {sheraWidth}: {planner.LastDiagnostic}");
            Equal(true, plan.TrianglePath.Count > 0, "and it must be a real corridor");
        }
    }

    /// <summary>
    /// The route the planner answers with is only worth having if every leg of it clears
    /// both bodies, measured the same way the refusal measured them.
    /// </summary>
    private static void TheRouteStillClearsBothModelsAtEveryStep(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var sheraWidth in SheraCollisionWidths)
        {
            var reader = createWalkmeshReader(Field);
            var models = Models(sheraWidth);
            var planner = new FieldWalkmeshRoutePlanner(
                reader,
                dynamicObstacleProvider: (_, _) => models);
            var start = Conversation();
            Equal(true, planner.TryBuildRoute(start, OutIntoTheTown(), out var plan),
                "the route must exist before its clearance can be checked");

            var steps = plan.StableWaypointsOverride ?? FieldWalkmeshPathfinder.BuildStableWaypoints(
                start.X, start.Y, start.Z, plan.Portals, plan.FinalApproach);
            Equal(true, steps.Count > 0, "a route has at least one step");
            var current = new FieldNavigationRouteWaypoint(start.X, start.Y, start.Z);
            for (var index = 0; index < steps.Count; index++)
            {
                Equal(false,
                    FieldNavigationDynamicObstacleGeometry.IntersectsAny(current, steps[index].Waypoint, models),
                    $"leg {index} of the route must clear both models at Shera width {sheraWidth}");
                current = steps[index].Waypoint;
            }

            // The detour is a checkpoint the player has to actually reach, not a hint
            // that auto walk may round off on its way to the door.
            Equal(true, steps[0].MustReach || steps.Count == 1,
                "the first checkpoint is required rather than advisory");
        }
    }

    /// <summary>
    /// With nobody in the room the ordinary funnel answer must come back unchanged, so
    /// the new search cannot quietly lengthen every route in the game.
    /// </summary>
    private static void AnEmptyRoomKeepsItsOrdinaryStraightRoute(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var withoutModels = new FieldWalkmeshRoutePlanner(createWalkmeshReader(Field));
        Equal(true, withoutModels.TryBuildRoute(Conversation(), OutIntoTheTown(), out var plain),
            "an empty room routes as it always did");

        var withModels = new FieldWalkmeshRoutePlanner(
            createWalkmeshReader(Field),
            dynamicObstacleProvider: (_, _) => Array.Empty<FieldNavigationDynamicObstacle>());
        Equal(true, withModels.TryBuildRoute(Conversation(), OutIntoTheTown(), out var empty),
            "and an empty model list is the same thing");
        Equal(plain.TrianglePath.Count, empty.TrianglePath.Count,
            "an empty model list must not change the corridor");
        Equal(true, plain.TrianglePath.Count <= 6,
            $"which is the short way across the room, not a detour: [{string.Join(",", plain.TrianglePath)}]");
    }

    /// <summary>
    /// Fail closed. mtnvl4 is three pieces of walkmesh with no path between them, and a
    /// target on another piece stays unreachable whether or not anybody is standing in
    /// the way. The occupied-ground rule closes triangles; it must never open one.
    /// </summary>
    private static void SomewhereWithNoPathToItIsStillRefused(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        const int mountainLedges = 313;
        var reader = createWalkmeshReader(mountainLedges);
        var worldExit = new FieldNavigationTarget(
            mountainLedges,
            FieldNavigationCategory.Story,
            "Leave the mountain by the northern path",
            -657, 1001, 37,
            TriggerLine: new FieldNavigationTriggerLine(-570, 1014, 36, -744, 988, 39));

        // The party as nvdun2's gateway 0 puts it down, on the 221-triangle component
        // the world exit is not part of, with somebody standing beside them.
        var strandedLedge = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, mountainLedges, 0, 984, 804, -210, 240, 0);
        IReadOnlyList<FieldNavigationDynamicObstacle> bystander =
        [
            new(1, 940, 760, -210, PlayerCollisionWidth, PlayerCollisionWidth),
        ];
        var planner = new FieldWalkmeshRoutePlanner(
            reader,
            dynamicObstacleProvider: (_, _) => bystander);
        Equal(false, planner.TryBuildRoute(strandedLedge, worldExit, out _),
            "a piece of mesh with no path to the target is still unreachable");
    }

    /// <summary>
    /// The route goes round the bodies rather than over them: no triangle the occupied-
    /// ground rule closed may appear in the corridor it answers with.
    /// </summary>
    private static void TheAnswerNeverWalksOverSomebody(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(Field);
        var models = Models(34);
        var planner = new FieldWalkmeshRoutePlanner(
            reader,
            dynamicObstacleProvider: (_, _) => models);
        var start = Conversation();
        Equal(true, planner.TryBuildRoute(start, OutIntoTheTown(), out var plan),
            "the route must exist before its corridor can be checked");

        var mesh = reader.Read(new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, Field, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        foreach (var triangle in plan.TrianglePath)
        {
            if (triangle == start.TriangleId || triangle == plan.TargetTriangle)
            {
                continue;
            }

            var centre = mesh.Triangles[triangle].GetCentroid();
            foreach (var model in models)
            {
                var offsetX = model.X - centre.X;
                var offsetY = model.Y - centre.Y;
                Equal(true,
                    offsetX * offsetX + offsetY * offsetY >=
                        model.ClearanceRadius * model.ClearanceRadius,
                    $"triangle {triangle} is inside model {model.ModelIndex} and must not be in the corridor");
            }
        }
    }

    /// <summary>
    /// The body has to fit, not just the centre line. Every leg the planner emits is put
    /// through the shipping wall check at the player's own collision width - the same
    /// forward and +/-45-degree probes <c>FUN_00636C41</c> walks - because a leg whose
    /// flanks leave the floor is a leg the game will not walk.
    /// </summary>
    private static void EveryEmittedLegPassesTheNativeWallProbes(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var sheraWidth in SheraCollisionWidths)
        {
            var reader = createWalkmeshReader(Field);
            var mesh = Mesh(reader);
            var planner = new FieldWalkmeshRoutePlanner(
                reader,
                dynamicObstacleProvider: (_, _) => Models(sheraWidth));
            var start = Conversation();
            Equal(true, planner.TryBuildRoute(start, OutIntoTheTown(), out var plan),
                $"the way out must be found at Shera width {sheraWidth}: {planner.LastDiagnostic}");

            var steps = Steps(plan, start);
            var apex = new FieldNavigationRouteWaypoint(start.X, start.Y, start.Z);
            var apexTriangle = (int)start.TriangleId;
            for (var index = 0; index < steps.Count; index++)
            {
                var waypoint = steps[index].Waypoint;
                Equal(true, AreNativeWallProbesClear(mesh, apexTriangle, apex, waypoint),
                    $"leg {index} of {steps.Count} at Shera width {sheraWidth} must pass the native " +
                    $"wall probes: {apex.X},{apex.Y} to {waypoint.X},{waypoint.Y} from triangle {apexTriangle}");
                var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    mesh, apexTriangle, apex, waypoint, isTriangleBlocked: null, applyPortalInset: false);
                Equal(true, trace.IsClear, $"leg {index} must also be walkable end to end");
                apexTriangle = trace.EndTriangle;
                apex = waypoint;
            }
        }
    }

    /// <summary>
    /// And the corridor has to end somewhere the line actually fires. The approach point
    /// a trigger-line route names sits on the line itself, which here is at the floor
    /// edge, so the last leg ends on the floor instead - inside the reach
    /// <c>FUN_00637ABB</c> uses, which is strictly less than the player's own collision
    /// radius.
    /// </summary>
    private static void TheLastLegEndsInsideTheNativeLineActivation(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var line = OutIntoTheTown().TriggerLine!.Value;
        foreach (var sheraWidth in SheraCollisionWidths)
        {
            var reader = createWalkmeshReader(Field);
            var mesh = Mesh(reader);
            var planner = new FieldWalkmeshRoutePlanner(
                reader,
                dynamicObstacleProvider: (_, _) => Models(sheraWidth));
            var start = Conversation();
            Equal(true, planner.TryBuildRoute(start, OutIntoTheTown(), out var plan),
                "the route must exist before its ending can be checked");

            var last = Steps(plan, start)[^1].Waypoint;
            var distance = DistanceToLine(last, line);
            Equal(true, distance <= PlayerCollisionWidth - 1,
                $"the last waypoint at Shera width {sheraWidth} must be inside the reach the " +
                $"controller resolves: {last.X},{last.Y} is {distance:0.0} from it");

            // On the floor, not past it: a waypoint the walkmesh does not own is not
            // somewhere the party can be standing when the line fires.
            Equal(true,
                FieldWalkmeshPathfinder.ResolveTriangle(mesh, last.X, last.Y, last.Z, -1) >= 0,
                $"and it must be on the walkmesh: {last.X},{last.Y},{last.Z}");
        }
    }

    /// <summary>
    /// The controller's own answer, which is what the player hears. Before the fix this
    /// said "Route unavailable to Go out into the town. Navigation off."
    /// </summary>
    private static void TheControllerTakesTheRouteRatherThanRefusingIt(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(Field);
        var target = OutIntoTheTown();
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource(
                Array.Empty<FieldNavigationTarget>(),
                storyTargetProvider: _ => [target]),
            new FieldWalkmeshRoutePlanner(
                reader,
                dynamicObstacleProvider: (_, _) => Models(34)));
        var transform = new FieldNavigationControlTransform(0);
        var start = Conversation();

        var guard = 0;
        while (controller.CurrentCategory != FieldNavigationCategory.Story && guard++ < 8)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        }

        controller.HandleAction(FieldNavigationAction.NextTarget, start, transform);
        var walking = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Equal(true, controller.BeaconEnabled,
            $"auto walk takes the way out rather than refusing it: {walking?.Speech}");
        Equal(false, walking?.Speech?.Contains("Route unavailable", StringComparison.Ordinal) ?? false,
            $"and says so: {walking?.Speech}");
    }

    private static IReadOnlyList<FieldNavigationRouteStep> Steps(
        FieldNavigationRoutePlan plan,
        FieldPositionSnapshot start) =>
        plan.StableWaypointsOverride ?? FieldWalkmeshPathfinder.BuildStableWaypoints(
            start.X, start.Y, start.Z, plan.Portals, plan.FinalApproach);

    private static FieldWalkmesh Mesh(FieldWalkmeshReader reader) =>
        reader
            .Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, 0, 0, 0, 0, 0))
            .Walkmesh
        ?? throw new InvalidOperationException("the installed field 558 walkmesh must be readable.");

    private static double DistanceToLine(
        FieldNavigationRouteWaypoint point,
        FieldNavigationTriggerLine line)
    {
        var segmentX = line.EndX - (double)line.StartX;
        var segmentY = line.EndY - (double)line.StartY;
        var lengthSquared = segmentX * segmentX + segmentY * segmentY;
        var amount = lengthSquared <= 0d
            ? 0d
            : Math.Clamp(
                ((point.X - line.StartX) * segmentX + (point.Y - line.StartY) * segmentY) / lengthSquared,
                0d,
                1d);
        var closestX = line.StartX + segmentX * amount;
        var closestY = line.StartY + segmentY * amount;
        return Math.Sqrt(
            (point.X - closestX) * (point.X - closestX) + (point.Y - closestY) * (point.Y - closestY));
    }

    /// <summary>
    /// The shipping check, invoked rather than reimplemented. A copy of this logic in a
    /// test would assert that the copy agrees with itself.
    /// </summary>
    private static bool AreNativeWallProbesClear(
        FieldWalkmesh walkmesh,
        int startTriangle,
        FieldNavigationRouteWaypoint start,
        FieldNavigationRouteWaypoint end)
    {
        var method = typeof(FieldWalkmeshRoutePlanner).GetMethod(
            "AreNativeWallProbesClear",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "FieldWalkmeshRoutePlanner.AreNativeWallProbesClear is the native wall check this " +
                "regression exists to assert; it has been renamed or removed.");
        return (bool)method.Invoke(
            null,
            [walkmesh, startTriangle, start, end, (double)PlayerCollisionWidth, null])!;
    }

    /// <summary>
    /// The corridor may finish short of the approach point when it is already inside the
    /// line's reach - but "inside" is the controller's own reach and the native vertical
    /// range, not a planar radius. A point directly above or below a line is on another
    /// floor of an overlapping mesh, and standing there activates nothing.
    /// </summary>
    private static void ALineOnAnotherFloorIsNotAnArrival()
    {
        var line = new FieldNavigationTriggerLine(-239, -9, 0, -160, -15, 0);
        const double playerRadius = PlayerCollisionWidth;

        Equal(true, ActivatesNativeTriggerLine(new FieldNavigationRouteWaypoint(-200, 20, 0), line, playerRadius),
            "a point on the floor inside the reach is an arrival");
        Equal(false, ActivatesNativeTriggerLine(new FieldNavigationRouteWaypoint(-200, 20, 512), line, playerRadius),
            "the same point five hundred units up is a different floor");
        Equal(false, ActivatesNativeTriggerLine(new FieldNavigationRouteWaypoint(-200, 20, -512), line, playerRadius),
            "and so is the same point below it");

        // The reach itself is the controller's, which for a row using the player's own
        // collision width is one less than that width.
        Equal(true, ActivatesNativeTriggerLine(new FieldNavigationRouteWaypoint(-200, 21, 0), line, playerRadius),
            "thirty-three units out is inside the reach the controller resolves");
        Equal(false, ActivatesNativeTriggerLine(new FieldNavigationRouteWaypoint(-200, 22, 0), line, playerRadius),
            "thirty-four is not, even though it is inside the raw collision width");

        // And a target with no line has no such ending at all.
        Equal(false, ActivatesNativeTriggerLine(new FieldNavigationRouteWaypoint(-200, 20, 0), null, playerRadius),
            "a target with no line must be walked to");
    }

    /// <summary>
    /// Accepting the route is not the same as walking it. The corners the corridor emits
    /// are required, so the tracker has to advance through each of them in turn and then
    /// accept the ending rather than steering on past it to an approach point the party
    /// cannot stand on.
    /// </summary>
    private static void TheRouteTrackerWalksEveryCornerAndAcceptsTheEnding(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(Field);
        var mesh = Mesh(reader);
        var models = Models(34);
        var planner = new FieldWalkmeshRoutePlanner(
            reader,
            dynamicObstacleProvider: (_, _) => models);
        var target = OutIntoTheTown();
        var start = Conversation();
        Equal(true, planner.TryBuildRoute(start, target, out var plan),
            "the route must exist before it can be walked");
        var corners = Steps(plan, start);

        var tracker = new FieldNavigationRouteTracker(planner);
        Equal(true, tracker.TryStart(start, target, out var guidance),
            "the tracker takes the corridor");
        Equal(corners[0].Waypoint, guidance.Waypoint,
            $"and aims at its first corner: {guidance.Diagnostic}");

        // Walk the party corner to corner. Each update is the party standing on the
        // corner it was just sent to, which is what auto walk produces.
        var reached = 0;
        for (var index = 0; index < corners.Count; index++)
        {
            var corner = corners[index];
            var triangle = FieldWalkmeshPathfinder.ResolveTriangle(
                mesh, corner.Waypoint.X, corner.Waypoint.Y, corner.Waypoint.Z, -1);
            Equal(true, triangle >= 0,
                $"corner {index} at {corner.Waypoint.X},{corner.Waypoint.Y} must be on the walkmesh");
            var at = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, Field, 0,
                corner.Waypoint.X, corner.Waypoint.Y, corner.Waypoint.Z, (ushort)triangle, 0);
            if (!tracker.TryUpdate(at, target, out guidance))
            {
                // The tracker stops answering once the route has nothing left to steer,
                // which is the ending being accepted rather than overshot.
                break;
            }

            reached++;
            Equal(false, guidance.Replanned,
                $"corner {index} must not force a replan: {guidance.Diagnostic}");
            if (index + 1 < corners.Count)
            {
                Equal(corners[index + 1].Waypoint, guidance.Waypoint,
                    $"reaching corner {index} must aim at the next corner, not retain or skip one");
                Equal(index + 1, tracker.CurrentProbeSnapshot!.WaypointIndex,
                    $"corner {index} must advance the route's tracked waypoint");
            }
        }

        Equal(true, reached >= corners.Count - 1,
            $"every corner but the ending must be consumed in order: {reached} of {corners.Count}");

        // Standing on the last corner, the party is inside the line's reach - so the
        // route is finished here, not somewhere further on.
        var last = corners[^1].Waypoint;
        Equal(true, DistanceToLine(last, target.TriggerLine!.Value) <= PlayerCollisionWidth - 1,
            $"and the ending is inside the reach the controller resolves: {last.X},{last.Y}");
        var atTheEnding = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, Field, 0, last.X, last.Y, last.Z,
            (ushort)FieldWalkmeshPathfinder.ResolveTriangle(mesh, last.X, last.Y, last.Z, -1), 0);
        var measured = tracker.TryMeasureRemainingDistance(atTheEnding, out var remaining);
        Equal(true, !measured || remaining <= PlayerCollisionWidth,
            $"with nothing meaningful left to walk: measured={measured}, remaining={remaining:0.0}");
    }

    /// <summary>
    /// The shipping activation test, invoked rather than reimplemented.
    /// </summary>
    private static bool ActivatesNativeTriggerLine(
        FieldNavigationRouteWaypoint point,
        FieldNavigationTriggerLine? line,
        double playerRadius)
    {
        var method = typeof(FieldWalkmeshRoutePlanner).GetMethod(
            "ActivatesNativeTriggerLine",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "FieldWalkmeshRoutePlanner.ActivatesNativeTriggerLine is the ending test this " +
                "regression exists to assert; it has been renamed or removed.");
        return (bool)method.Invoke(null, [point, line, playerRadius])!;
    }

    /// <summary>
    /// The two bodies as the obstacle reader builds them: the clearance is half the sum
    /// of the two collision widths, and the player's own width drives the forward and
    /// flank probes.
    /// </summary>
    private static IReadOnlyList<FieldNavigationDynamicObstacle> Models(int sheraWidth) =>
    [
        new(9, -363, 205, 0, (PlayerCollisionWidth + sheraWidth) / 2d, PlayerCollisionWidth),
        new(10, -288, 146, 0, (PlayerCollisionWidth + PalmerCollisionWidth) / 2d, PlayerCollisionWidth),
    ];

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Cid house live model route - {label}: expected {expected}, got {actual}.");
        }
    }
}
