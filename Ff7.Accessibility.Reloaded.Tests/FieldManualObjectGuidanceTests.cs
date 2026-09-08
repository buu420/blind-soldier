using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

internal static class FieldManualObjectGuidanceTests
{
    private static readonly FieldPositionSnapshot Position = new(1, 463, 0, 0, 0, 0, 0, 0);
    private static readonly FieldNavigationControlTransform Transform = new(0);

    internal static void Run()
    {
        FallRewardsKeepTheirNativeVisibilityAndCollectedState();
        SelectingAndRepeatingFallRewardsExplainsManualControlsWithoutPlanning();
        FallRewardsCannotStartAutomaticWalking();
        SelectingAFallRewardStopsThePreviousAutomaticRoute();
    }

    internal static FieldNavigationTarget CreateVisibleFallReward(int entity = 6) =>
        new NativeMemory().Reader().ReadTargets(Position).Single(target => target.TriggerEntityId == entity);

    private static void FallRewardsKeepTheirNativeVisibilityAndCollectedState()
    {
        var memory = new NativeMemory();
        var reader = memory.Reader();
        var targets = reader.ReadTargets(Position);
        Equal(2, targets.Count, "both visible uncollected rewards remain discoverable as Objects");
        Equal("Wizard Staff", targets.Single(target => target.TriggerEntityId == 6).Label,
            "the staff keeps its native kernel item name");
        Equal("Star Pendant", targets.Single(target => target.TriggerEntityId == 5).Label,
            "the pendant keeps its native kernel item name");
        Equal(true, targets.All(target => target.ObjectCueKind == FieldObjectCueKind.Item),
            "manual rewards retain ordinary visible-item cue information");
        memory.SetVisible(6, false);
        Equal(5, reader.ReadTargets(Position).Single().TriggerEntityId, "a hidden staff is not announced");
        memory.SetVisible(6, true);
        memory.Collected = 8;
        Equal(5, reader.ReadTargets(Position).Single().TriggerEntityId, "native15[115] bit3 retires only the staff");
        memory.Collected = 4;
        Equal(6, reader.ReadTargets(Position).Single().TriggerEntityId, "native15[115] bit2 retires only the pendant");
        memory.Collected = 12;
        Equal(0, reader.ReadTargets(Position).Count, "collected rewards remain absent");
    }

    private static void SelectingAndRepeatingFallRewardsExplainsManualControlsWithoutPlanning()
    {
        foreach (var (entity, direction) in new[] { (6, "Left"), (5, "Right") })
        {
            var planner = new CountingPlanner();
            var target = CreateVisibleFallReward(entity);
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), planner);
            var selection = SelectObjects(controller);
            AssertManualGuidance(selection?.Speech, target.Label, direction);
            AssertManualGuidance(controller.HandleAction(FieldNavigationAction.RepeatTarget, Position, Transform)?.Speech,
                target.Label, direction);
            Equal(0, planner.BuildCalls, "describing a fall reward must never plan a walking route");
            Equal(false, controller.PreviewActionRoute(FieldNavigationAction.ToggleBeacon, Position,
                includeSelectionRoute: true).UsesRoute, "manual reward selection must not depend on route preflight");
        }
    }

    private static void FallRewardsCannotStartAutomaticWalking()
    {
        var target = CreateVisibleFallReward();
        var planner = new CountingPlanner();
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), planner);
        _ = SelectObjects(controller);
        var result = controller.HandleAction(FieldNavigationAction.ToggleBeacon, Position, Transform);
        AssertManualGuidance(result?.Speech, target.Label, "Left");
        Equal(false, controller.BeaconEnabled, "the fall reward cannot become an active walking route");
        Equal(false, controller.TryResolveAutomaticInput(Position, Transform, 80, out var input),
            "manual reward guidance must not create any directional input");
        Equal(FieldNavigationInput.None, input, "manual reward steering remains unowned");
        var sink = new RecordingSink();
        using var autoWalk = new NavigationAutoWalkController(sink);
        Equal(false, autoWalk.TryStart(NavigationAutoWalkDomain.Field, controller.BeaconEnabled),
            "both hosts' existing route-active guard must reject starting auto walk");
        Equal(0, planner.BuildCalls, "P must not attempt an unreachable reward route");
        Equal(0, sink.Transitions.Count, "manual reward selection must not press OK or any movement key");
    }

    private static void SelectingAFallRewardStopsThePreviousAutomaticRoute()
    {
        var ordinary = new FieldNavigationTarget(463, FieldNavigationCategory.Objects, "Walkable object",
            100, 0, 0, "ordinary-control");
        var manual = CreateVisibleFallReward();
        var planner = new CountingPlanner();
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([ordinary, manual]), planner);
        _ = SelectObjects(controller);
        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, Position, Transform);
        Equal(true, controller.BeaconEnabled, "ordinary objects still start normal navigation");
        var sink = new RecordingSink();
        using var autoWalk = new NavigationAutoWalkController(sink);
        Equal(true, autoWalk.TryStart(NavigationAutoWalkDomain.Field, controller.BeaconEnabled),
            "the ordinary control route owns automatic movement");
        _ = autoWalk.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true);
        var priorBuilds = planner.BuildCalls;
        var selection = controller.HandleAction(FieldNavigationAction.NextTarget, Position, Transform);
        AssertManualGuidance(selection?.Speech, manual.Label, "Left");
        Equal(false, controller.BeaconEnabled, "cycling onto a manual reward must cancel the previous route");
        Equal(priorBuilds, planner.BuildCalls, "relocking onto a fall reward must not invoke the planner");
        _ = autoWalk.Drive(FieldNavigationInput.None, canMove: false, routeActive: controller.BeaconEnabled);
        Equal(false, autoWalk.Enabled, "native host drive must finish the old automatic movement ownership");
        Equal(new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeRight, false),
            sink.Transitions.Last(), "the previously held mapped direction is released");
        Equal(2, sink.Transitions.Count, "only the control route's direction press and release are sent");
    }

    private static FieldNavigationActionResult? SelectObjects(FieldNavigationController controller)
    {
        FieldNavigationActionResult? result = null;
        while (controller.CurrentCategory != FieldNavigationCategory.Objects)
            result = controller.HandleAction(FieldNavigationAction.NextCategory, Position, Transform);
        return result;
    }

    private static void AssertManualGuidance(string? speech, string item, string direction)
    {
        Equal(true, speech?.Contains(item, StringComparison.Ordinal) == true, "manual guidance identifies the visible item");
        Equal(true, speech?.Contains($"hold {direction}", StringComparison.OrdinalIgnoreCase) == true,
            $"{item} must identify its native direction during the fall");
        Equal(true, speech?.Contains("repeatedly press OK", StringComparison.OrdinalIgnoreCase) == true,
            "the fall's repeated-button requirement is explained");
        Equal(true, speech?.Contains("during the fall", StringComparison.OrdinalIgnoreCase) == true,
            "fall input must be distinguished from the later OK then Up climb");
        Equal(true, speech?.Contains("From here, press OK, then hold Up", StringComparison.Ordinal) == true,
            "current hanging-field guidance must explain how to resume the native climb");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private sealed class CountingPlanner : IFieldNavigationRoutePlanner
    {
        public int BuildCalls { get; private set; }
        public string LastDiagnostic => "ordinary route control fixture";
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        { triangle = position.TriangleId; return true; }
        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRoutePlan route)
        {
            BuildCalls++;
            route = new(position.FieldId, $"{target.FieldId}:{target.StableId}", [0], [],
                new(target.X, target.Y, target.Z), 0);
            return true;
        }
        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        { waypoint = new(target.X, target.Y, target.Z); return true; }
    }

    private sealed class RecordingSink : IHighwayKeyboardInputSink
    {
        public List<HighwayKeyboardTransition> Transitions { get; } = [];
        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        { Transitions.AddRange(transitions); return new(transitions.Count, 0); }
    }

    private sealed class NativeMemory
    {
        private const int Table = 0x02700000;
        private readonly Dictionary<int, byte> bytes = [];
        public byte Collected { get; set; }
        public NativeMemory()
        {
            bytes[FieldPositionReader.AddressFieldNumModels] = 3;
            foreach (var (entity, model) in new[] { (6, 1), (5, 2) })
            {
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = (byte)model;
                SetVisible(entity, true);
            }
        }
        public void SetVisible(int entity, bool visible) => bytes[Table +
            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] *
            FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] =
                visible ? (byte)1 : (byte)0;
        public FieldNavigationObjectReader Reader() => new(ReadInt32, ReadByte,
            id => id == 196 ? "Wizard Staff" : id == 298 ? "Star Pendant" : null, _ => null,
            FieldNavigationObjectCatalog.CreateAllFields().Where(definition => definition.FieldId == 463));
        private byte ReadByte(int address) => address ==
            FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + 115 ? Collected : bytes.GetValueOrDefault(address);
        private int ReadInt32(int address)
        {
            if (address == FieldNavigationObjectReader.AddressFieldEventDataPtr) return Table;
            if (address == Table + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.PositionXOffset)
                return 500 * 4096;
            if (address == Table + 2 * FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.PositionXOffset)
                return 700 * 4096;
            return 0;
        }
    }
}
