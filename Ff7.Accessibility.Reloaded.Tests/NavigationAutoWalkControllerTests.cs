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
