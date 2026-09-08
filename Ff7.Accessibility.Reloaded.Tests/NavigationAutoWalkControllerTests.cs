using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

internal static class NavigationAutoWalkControllerTests
{
    internal static void Run()
    {
        SamplesPAsAForegroundRisingEdgeWithoutDelayedActivation();
        StartsOnlyForAnActiveRouteAndTogglesOffCleanly();
        DrivesTheCurrentRouteDirectionAndReleasesDuringSuspension();
        ReassertsAnOwnedDirectionWhenTheGameStopsReportingIt();
        FailsClosedAfterPartialDirectionalInputFailure();
        ResolvesFieldRouteAndMountedLadderDirections();
        NativeExitsKeepAutomaticDirectionInsideTheProximityRadius();
        StoryCrossingsDoNotCompleteAtTheOrdinaryArrivalRadius();
        StoryGatewayCrossingsWaitForTheNativeFieldTransition();
        InteractionTargetsKeepTheirArrivalRadius();
    }

    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        Run();
        NativeLockerRoomCornerRemainsUntilTheNextLegIsWalkable(createWalkmeshReader);
        NativeCornerPortalHandoffHonorsTraversalBlocks(createWalkmeshReader);
    }

    private static void NativeLockerRoomCornerRemainsUntilTheNextLegIsWalkable(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var target = new FieldNavigationTarget(
            387, FieldNavigationCategory.Story, "Leave the locker room for the send-off",
            -1271, -652, 605, "gateway:387:0:386", CompletesOnArrival: true,
            DestinationFieldIds: [386],
            TriggerLine: new FieldNavigationTriggerLine(-1269, -688, 605, -1272, -615, 605));
        // Replay the 2026-09-05 19:27:16-19:27:17Z native observations, with both
        // the logged model elevation and the walkmesh elevation. The next corner
        // is only 16 units away at (-1546,-764), but its continuation crosses a wall.
        foreach (var z in new[] { 595, 605 })
        {
            var reader = createWalkmeshReader(387);
            var tracker = new FieldNavigationRouteTracker(new FieldWalkmeshRoutePlanner(reader));
            var samples = new (int X, int Y, ushort Triangle)[]
            {
                (-1561, -796, 22), (-1554, -786, 22), (-1550, -771, 21),
                (-1546, -764, 21), (-1544, -760, 21), (-1541, -757, 21)
            };
            foreach (var (sample, index) in samples.Select((sample, index) => (sample, index)))
            {
                var position = new FieldPositionSnapshot(
                    FieldPositionReader.FieldModule, 387, 0,
                    sample.X, sample.Y, z, sample.Triangle, 0);
                FieldNavigationRouteGuidance guidance;
                Equal(true, index == 0
                        ? tracker.TryStart(position, target, out guidance)
                        : tracker.TryUpdate(position, target, out guidance),
                    "the native locker-room replay retains its route");
                AssertClearGuidance(position, guidance);
            }

            // This native triangle-20 position is beyond the portal. The rounded
            // funnel endpoint (-1535,-753) itself still resolves to triangle 21.
            var corner = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, 387, 0, -1534, -748, z, 20, 0);
            Equal(true, tracker.TryUpdate(corner, target, out var beyondCorner),
                "reaching the native corner retains forward guidance");
            Equal(true, beyondCorner.Waypoint.X > corner.X,
                "the route advances after the next native segment becomes walkable");
            AssertClearGuidance(corner, beyondCorner);

            // Starting again at the actual stopped position must also survive a
            // stationary update instead of immediately dropping its nearby corner.
            var stopped = corner with { X = -1541, Y = -757, TriangleId = 21 };
            Equal(true, tracker.TryStart(stopped, target, out _),
                "a fresh route can start at the logged stopping point");
            Equal(true, tracker.TryUpdate(stopped, target, out var stationary),
                "the unchanged stopping point retains guidance");
            AssertClearGuidance(stopped, stationary);

            foreach (var freshAtCorner in new[] { false, true })
            {
                var exactCorner = corner with { X = -1535, Y = -753, TriangleId = 21 };
                var origin = freshAtCorner
                    ? exactCorner
                    : exactCorner with { X = -1561, Y = -796, TriangleId = 22 };
                var controller = new FieldNavigationController(
                    new FieldNavigationTargetSource([target]), new FieldWalkmeshRoutePlanner(reader));
                var transform = new FieldNavigationControlTransform(-128);
                var noInput = new FieldNavigationInputSnapshot(0, FieldNavigationInput.None);
                controller.HandleAction(FieldNavigationAction.NextCategory, origin, transform);
                controller.HandleAction(FieldNavigationAction.ToggleBeacon, origin, transform);
                if (!freshAtCorner)
                {
                    foreach (var sample in samples.Skip(1))
                    {
                        controller.UpdateLiveTracking(origin with
                        {
                            X = sample.X, Y = sample.Y, TriangleId = sample.Triangle
                        }, noInput, transform, false, 80);
                    }
                }

                controller.UpdateLiveTracking(exactCorner, noInput, transform, false, 80);
                Equal(true, controller.TryResolveAutomaticInput(exactCorner, transform, 80, out _),
                    $"fresh={freshAtCorner}: exact native corner must keep a nonzero automatic direction");
                var handoff = controller.CurrentRouteGuidance!.Value.Waypoint;
                var firstLeg = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    reader.Read(exactCorner).Walkmesh!, 21,
                    new FieldNavigationRouteWaypoint(exactCorner.X, exactCorner.Y, exactCorner.Z), handoff);
                Equal(true, firstLeg.IsClear,
                    $"fresh={freshAtCorner}: the exact corner handoff is native-walkable: {firstLeg.Diagnostic}");
                Equal(20, firstLeg.EndTriangle,
                    "the short handoff crosses the native portal into its destination triangle");
                for (var sample = 1; sample < 4; sample++)
                {
                    var between = exactCorner with
                    {
                        X = (int)Math.Round(exactCorner.X + (handoff.X - exactCorner.X) * sample / 4d),
                        Y = (int)Math.Round(exactCorner.Y + (handoff.Y - exactCorner.Y) * sample / 4d)
                    };
                    controller.UpdateLiveTracking(between, noInput, transform, false, 80);
                    Equal(true, controller.TryResolveAutomaticInput(between, transform, 80, out _),
                        "quantized movement across the corner handoff keeps driving");
                    var intermediateTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                        reader.Read(between).Walkmesh!, 21,
                        new FieldNavigationRouteWaypoint(between.X, between.Y, between.Z),
                        controller.CurrentRouteGuidance!.Value.Waypoint);
                    Equal(true, intermediateTrace.IsClear,
                        $"each corner handoff sample keeps a clear next leg: {intermediateTrace.Diagnostic}");
                }

                var afterHandoff = exactCorner with
                {
                    X = handoff.X, Y = handoff.Y, TriangleId = (ushort)firstLeg.EndTriangle
                };
                controller.UpdateLiveTracking(afterHandoff, noInput, transform, false, 80);
                Equal(true, controller.TryResolveAutomaticInput(afterHandoff, transform, 80, out _),
                    "reaching the portal handoff must continue automatically toward the exit");
                var secondLeg = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    reader.Read(afterHandoff).Walkmesh!, firstLeg.EndTriangle,
                    new FieldNavigationRouteWaypoint(afterHandoff.X, afterHandoff.Y, afterHandoff.Z),
                    controller.CurrentRouteGuidance!.Value.Waypoint);
                Equal(true, secondLeg.IsClear,
                    $"the handoff reconnects to a clear forward route: {secondLeg.Diagnostic}");
            }

            void AssertClearGuidance(
                FieldPositionSnapshot position,
                FieldNavigationRouteGuidance guidance)
            {
                var probe = tracker.CurrentProbeSnapshot!;
                var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    reader.Read(position).Walkmesh!, probe.ResolvedTriangle,
                    new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z),
                    guidance.Waypoint);
                Equal(true, trace.IsClear,
                    $"locker-room ({position.X},{position.Y},{position.Z}) next step must be native-walkable: " +
                    $"{trace.Diagnostic}; {guidance.Diagnostic}");
            }
        }
    }

    private static void NativeCornerPortalHandoffHonorsTraversalBlocks(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, 387, 0, -1535, -753, 605, 21, 0);
        var target = new FieldNavigationTarget(
            387, FieldNavigationCategory.Story, "Locker-room gateway", -1271, -652, 605,
            CompletesOnArrival: true, DestinationFieldIds: [386],
            TriggerLine: new FieldNavigationTriggerLine(-1269, -688, 605, -1272, -615, 605));
        var reader = createWalkmeshReader(387);
        var planner = new FieldWalkmeshRoutePlanner(reader);
        Equal(true, planner.TryBuildRoute(position, target, out var plan),
            "the corner handoff safety fixture has a native route");
        var tracker = new FieldNavigationRouteTracker(planner);
        Equal(true, tracker.TryStart(position, target, out _),
            "the corner handoff safety fixture has stable steps");
        var steps = tracker.CurrentProbeSnapshot!.StableWaypoints;
        var mesh = reader.Read(position).Walkmesh!;
        var clear = Observe();
        Equal(true, IsHandoff(clear), "the unblocked exact corner uses a traced portal handoff");
        Equal(false, IsHandoff(Observe(isTriangleBlocked: triangle => triangle == 20)),
            "a locked destination triangle forbids the corner handoff");
        FieldNavigationDynamicObstacle[] obstacles =
        [
            new(4, clear.Waypoint.X, clear.Waypoint.Y, clear.Waypoint.Z, 4d)
        ];
        Equal(false, IsHandoff(Observe(obstacles: obstacles)),
            "a native actor cylinder on the handoff forbids that movement");
        var action = new FieldNavigationRouteAction(
            FieldNavigationTransitionKind.Ladder, "pending-native-action", plan.Portals[0].Midpoint,
            FieldNavigationInput.Up, RequiresAction: true, PortalIndex: 0);
        var actionBlocked = Observe(action: action);
        Equal(false, IsHandoff(actionBlocked), "a pending native action forbids walking through its portal");
        Equal(action.Waypoint, actionBlocked.Waypoint,
            "the blocked route retains the native action approach");

        FieldNavigationCorridorObservation Observe(
            Func<int, bool>? isTriangleBlocked = null,
            IReadOnlyList<FieldNavigationDynamicObstacle>? obstacles = null,
            FieldNavigationRouteAction? action = null)
        {
            Equal(true, FieldNavigationCorridorLookahead.TryResolve(
                    mesh, 21, position, plan, steps, 0, action, default,
                    isTriangleBlocked, obstacles, out var observation),
                "the native corner still provides an observation when blocked");
            return observation;
        }

        static bool IsHandoff(FieldNavigationCorridorObservation observation) =>
            observation.Diagnostic.Contains("corner-portal-handoff", StringComparison.Ordinal);
    }

    private static void SamplesPAsAForegroundRisingEdgeWithoutDelayedActivation()
    {
        var tracker = new NavigationKeyPressTracker();
        var isDown = false;
        var isForeground = true;
        bool Observe(int virtualKey) => tracker.Observe(virtualKey, isDown, isForeground);

        Equal(false, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "released P has no action");
        isDown = true;
        Equal(true, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "foreground P press starts one toggle");
        Equal(false, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "held P cannot toggle repeatedly");

        isDown = false;
        _ = NavigationAutoWalkKeyRouter.ObserveToggle(Observe);
        isForeground = false;
        isDown = true;
        Equal(false, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "background P is observed but suppressed");
        isForeground = true;
        Equal(false, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "a P key held across refocus cannot start walking late");
        isDown = false;
        _ = NavigationAutoWalkKeyRouter.ObserveToggle(Observe);
        isDown = true;
        Equal(true, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "a fresh foreground press works after release");
    }

    private static void StartsOnlyForAnActiveRouteAndTogglesOffCleanly()
    {
        var sink = new RecordingSink();
        using var controller = new NavigationAutoWalkController(sink);

        Equal(false, controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: false),
            "auto walk cannot start without the selected route");
        Equal(false, controller.Enabled, "failed start leaves auto walk off");
        Equal(true, controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true),
            "P starts auto walk after navigation locks the selected target");
        Equal(true, controller.IsEnabledFor(NavigationAutoWalkDomain.Field),
            "field route owns auto walk");

        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true);
        Equal(true, controller.Stop(), "second P stops active auto walk");
        SequenceEqual(
            [new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeRight, false)],
            sink.Batches[^1],
            "stopping releases the owned direction");
        Equal(false, controller.Stop(), "stopping an inactive route is idempotent");
    }

    private static void DrivesTheCurrentRouteDirectionAndReleasesDuringSuspension()
    {
        var sink = new RecordingSink();
        using var controller = new NavigationAutoWalkController(sink);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);

        var driven = controller.Drive(FieldNavigationInput.UpLeft, canMove: true, routeActive: true);
        Equal(true, driven.Success, "route direction is injected");
        SequenceEqual(
            [
                new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeUp, true),
                new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeLeft, true)
            ],
            sink.Batches.Single(),
            "diagonal route owns both matching arrow keys");

        controller.Suspend();
        SequenceEqual(
            [
                new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeUp, false),
                new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeLeft, false)
            ],
            sink.Batches[^1],
            "battle, focus, or frame suspension releases every direction");
        Equal(true, controller.Enabled, "temporary suspension retains user intent");

        _ = controller.Drive(FieldNavigationInput.Down, canMove: true, routeActive: true);
        Equal(
            new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeDown, true),
            sink.Batches[^1].Single(),
            "the same route resumes after coherent navigation returns");

        _ = controller.Drive(FieldNavigationInput.Down, canMove: true, routeActive: false);
        Equal(false, controller.Enabled, "completed or failed navigation disables auto walk");
        Equal(
            new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeDown, false),
            sink.Batches[^1].Single(),
            "route completion releases the final key");
    }

    private static void FailsClosedAfterPartialDirectionalInputFailure()
    {
        var sink = new RecordingSink();
        sink.Results.Enqueue(new HighwayKeyboardSendResult(1, 5));
        sink.Results.Enqueue(new HighwayKeyboardSendResult(1, 0));
        using var controller = new NavigationAutoWalkController(sink);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);

        var result = controller.Drive(FieldNavigationInput.UpRight, canMove: true, routeActive: true);

        Equal(false, result.Success, "partial SendInput fails closed");
        Equal(false, controller.Enabled, "input failure disables automatic movement");
        Equal(
            true,
            controller.LastDiagnostic.Contains("inserted 1 of 2", StringComparison.OrdinalIgnoreCase),
            "failure preserves actionable input diagnostics");
    }

    private static void ReassertsAnOwnedDirectionWhenTheGameStopsReportingIt()
    {
        var sink = new RecordingSink();
        using var controller = new NavigationAutoWalkController(sink);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);

        _ = controller.Drive(
            FieldNavigationInput.Left,
            canMove: true,
            routeActive: true,
            observedInput: FieldNavigationInput.None);
        Equal(1, sink.Batches.Count, "the initial route direction is pressed once");

        _ = controller.Drive(
            FieldNavigationInput.Left,
            canMove: true,
            routeActive: true,
            observedInput: FieldNavigationInput.None);
        _ = controller.Drive(
            FieldNavigationInput.Left,
            canMove: true,
            routeActive: true,
            observedInput: FieldNavigationInput.None);
        _ = controller.Drive(
            FieldNavigationInput.Left,
            canMove: true,
            routeActive: true,
            observedInput: FieldNavigationInput.None);

        Equal(3, sink.Batches.Count,
            "three missing native samples reassert the swallowed route direction");
        SequenceEqual(
            [new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeLeft, false)],
            sink.Batches[1],
            "reassertion first releases the stale owned key");
        SequenceEqual(
            [new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeLeft, true)],
            sink.Batches[2],
            "reassertion presses the required route direction again");
    }

    private static void ResolvesFieldRouteAndMountedLadderDirections()
    {
        var target = new FieldNavigationTarget(
            500,
            FieldNavigationCategory.Story,
            "Test destination",
            0,
            -1000,
            0,
            "test-destination",
            CompletesOnArrival: true);
        var planner = new StraightRoutePlanner();
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target]),
            planner);
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            500,
            0,
            0,
            0,
            0,
            0,
            0);
        var transform = new FieldNavigationControlTransform(0);

        _ = controller.HandleAction(FieldNavigationAction.NextCategory, position, transform);
        var activation = controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, transform);
        Equal(true, controller.BeaconEnabled,
            $"test route activates; speech={activation?.Speech ?? "none"}; diagnostic={controller.LastNavigationDiagnostic}");
        Equal(true, controller.CurrentRouteGuidance is not null,
            $"test route has guidance; diagnostic={controller.LastNavigationDiagnostic}");
        Equal(
            true,
            controller.TryResolveAutomaticInput(position, transform, 80, out var routeInput),
            "active route exposes a safe automatic direction");
        Equal(FieldNavigationInput.Up, routeInput, "camera-relative route direction is reused");

        controller.Reset();
        var mounted = FieldLadderStateSnapshot.NotMounted with
        {
            IsMounted = true,
            Phase = FieldLadderPhase.Climbing,
            RequiredInput = FieldNavigationInput.Left,
            Target = new FieldNavigationRouteWaypoint(0, -1000, 500),
            TargetTriangle = 0
        };
        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, transform, mounted);
        Equal(
            true,
            controller.TryResolveAutomaticInput(position, transform, 80, out var ladderInput),
            "mounted ladder exposes its native route-owned climb input");
        Equal(FieldNavigationInput.Left, ladderInput, "auto walk follows the proven ladder direction");
    }

    private static void NativeExitsKeepAutomaticDirectionInsideTheProximityRadius()
    {
        var gateway = new FieldNavigationTarget(
            500, FieldNavigationCategory.Exits, "Next area", 100, 0, 0,
            "gateway:500:0:501", CompletesOnArrival: true, DestinationFieldIds: [501]);
        var line = gateway with
        {
            StableId = "script-exit:500:1:501",
            TriggerLine = new FieldNavigationTriggerLine(100, -50, 0, 100, 50, 0),
            InteractionRadius = 128
        };
        foreach (var (target, shortX) in new[] { (gateway, 50), (line, 90) })
        {
            var start = new FieldPositionSnapshot(1, 500, 0, 0, 0, 0, 0, 0);
            var controller = StartFieldRoute(target, start);
            var shortOfExit = start with { X = shortX };
            var update = controller.UpdateLiveTracking(
                shortOfExit, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);

            Equal(true, controller.BeaconEnabled, "an exit is not complete before its crossing");
            Equal(true, controller.TryResolveAutomaticInput(
                shortOfExit, new FieldNavigationControlTransform(0), 80, out var input),
                $"{target.StableId} must keep driving inside its proximity radius");
            Equal(FieldNavigationInput.Left, input, "control byte zero maps positive world X to Left");
            Equal(false, (update?.Speech ?? "").Contains("reached", StringComparison.Ordinal),
                "proximity must not announce native exit completion");
            if (target.TriggerLine is null)
            {
                Equal(true, controller.CreateSpokenGuidance(
                    shortOfExit, new FieldNavigationControlTransform(0), 80) is not null,
                    "spoken guidance must keep describing the unfinished gateway approach");
            }
        }
    }

    private static void StoryCrossingsDoNotCompleteAtTheOrdinaryArrivalRadius()
    {
        var target = new FieldNavigationTarget(
            500, FieldNavigationCategory.Story, "Cross the story line", 100, 0, 0,
            "story:crossing", CompletesOnArrival: true,
            TriggerLine: new FieldNavigationTriggerLine(100, -50, 0, 100, 50, 0));
        var start = new FieldPositionSnapshot(1, 500, 0, 0, 0, 0, 0, 0);
        var controller = StartFieldRoute(target, start);
        var shortOfLine = start with { X = 50 };
        var update = controller.UpdateLiveTracking(
            shortOfLine, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
            new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);

        Equal(true, controller.BeaconEnabled, "a Story LINE is not reached 50 units before crossing");
        Equal(false, (update?.Speech ?? "").Contains("reached", StringComparison.Ordinal),
            "Story category must not turn crossing proximity into completion");
        Equal(true, controller.TryResolveAutomaticInput(
            shortOfLine, new FieldNavigationControlTransform(0), 80, out var input),
            "automatic movement continues to the actual Story crossing");
        Equal(FieldNavigationInput.Left, input, "the Story crossing remains ahead in native control space");

        _ = controller.UpdateLiveTracking(
            start with { X = 100 }, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
            new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);
        Equal(false, controller.BeaconEnabled, "the same-field Story LINE completes at its crossing");
    }

    private static void StoryGatewayCrossingsWaitForTheNativeFieldTransition()
    {
        var line = new FieldNavigationTriggerLine(100, -50, 0, 100, 50, 0);
        var nativeExit = new FieldNavigationTarget(
            500, FieldNavigationCategory.Exits, "Next area", 100, 0, 0,
            "gateway:500:0:501", CompletesOnArrival: true,
            DestinationFieldIds: [501], TriggerLine: line);
        var storyLine = nativeExit with
        {
            Category = FieldNavigationCategory.Story,
            StableId = "story:gateway-line",
            DestinationFieldIds = null
        };
        var directStoryGateway = storyLine with
        {
            StableId = "story:direct-gateway",
            TriggerLine = null,
            DestinationFieldIds = [501]
        };
        foreach (var target in new[] { storyLine, directStoryGateway })
        {
            var start = new FieldPositionSnapshot(1, 500, 0, 0, 0, 0, 0, 0);
            var controller = StartFieldRoute(target, start, [nativeExit]);
            var shortOfGateway = start with { X = 50 };
            var update = controller.UpdateLiveTracking(
                shortOfGateway, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);

            Equal(true, controller.BeaconEnabled, "a Story gateway keeps its route before transition");
            Equal(false, (update?.Speech ?? "").Contains("Interact", StringComparison.Ordinal),
                "a native crossing must never become an interaction prompt");
            Equal(true, controller.TryResolveAutomaticInput(
                shortOfGateway, new FieldNavigationControlTransform(0), 80, out var input),
                "a Story gateway keeps automatic movement inside the ordinary arrival radius");
            Equal(FieldNavigationInput.Left, input, "a Story gateway remains ahead in native control space");

            var transition = controller.UpdateLiveTracking(
                shortOfGateway with { FieldId = 501 },
                new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);
            Equal(false, controller.BeaconEnabled, "the matching native field transition ends the route");
            Equal(true, (transition?.Speech ?? "").Contains("reached", StringComparison.Ordinal),
                "the actual Story destination transition confirms completion");
        }
    }

    private static void InteractionTargetsKeepTheirArrivalRadius()
    {
        foreach (var category in new[] { FieldNavigationCategory.Story, FieldNavigationCategory.Objects })
        {
            var target = new FieldNavigationTarget(
                500, category, "Interactable", 1000, 0, 0, "interaction:test",
                CompletesOnArrival: false, InteractionRadius: 128,
                TriggerLine: new FieldNavigationTriggerLine(1000, -50, 0, 1000, 50, 0));
            var start = new FieldPositionSnapshot(1, 500, 0, 0, 0, 0, 0, 0);
            var controller = StartFieldRoute(target, start);
            var inRange = start with { X = 900 };
            var update = controller.UpdateLiveTracking(
                inRange, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);

            Equal(true, (update?.Speech ?? "").Contains("Interact here", StringComparison.Ordinal),
                "an interaction target still pauses at its native radius");
            Equal(false, controller.TryResolveAutomaticInput(
                inRange, new FieldNavigationControlTransform(0), 80, out _),
                "automatic walking must not push through an interaction prompt");
        }
    }

    private static FieldNavigationController StartFieldRoute(
        FieldNavigationTarget target,
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationTarget>? exits = null)
    {
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target], exitTargetProvider: exits is null ? null : _ => exits),
            new StraightRoutePlanner());
        var categorySteps = target.Category switch
        {
            FieldNavigationCategory.Story => 1,
            FieldNavigationCategory.Npcs => 2,
            FieldNavigationCategory.Objects => 3,
            _ => 0
        };
        for (var index = 0; index < categorySteps; index++)
        {
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, position);
        }
        _ = controller.HandleAction(
            FieldNavigationAction.ToggleBeacon, position, new FieldNavigationControlTransform(0));
        Equal(true, controller.BeaconEnabled, $"test route to {target.StableId} is active");
        return controller;
    }

    private sealed class StraightRoutePlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight test route";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return true;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(
                position.FieldId,
                $"{target.FieldId}:{target.StableId}",
                [position.TriangleId],
                [],
                new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z),
                position.TriangleId);
            return position.FieldId == target.FieldId;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
            return position.FieldId == target.FieldId;
        }
    }

    private sealed class RecordingSink : IHighwayKeyboardInputSink
    {
        internal List<IReadOnlyList<HighwayKeyboardTransition>> Batches { get; } = [];
        internal Queue<HighwayKeyboardSendResult> Results { get; } = new();

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            Batches.Add(transitions.ToArray());
            return Results.Count > 0
                ? Results.Dequeue()
                : new HighwayKeyboardSendResult(transitions.Count, 0);
        }
    }

    private static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
