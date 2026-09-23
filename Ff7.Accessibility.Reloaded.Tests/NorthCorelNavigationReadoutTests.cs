using Ff7.Accessibility.Reloaded;

internal static class NorthCorelNavigationReadoutTests
{
    private static readonly FieldNavigationControlTransform Transform = new(-128);
    private static readonly FieldNavigationInputSnapshot NoInput = new(0, FieldNavigationInput.None);
    private static readonly FieldPositionSnapshot BeforePot = new(1, 453, 0, 134, 205, -10, 7, 128);
    private static readonly FieldPositionSnapshot TownArrival = new(1, 450, 0, 475, -277, -10, 32, 192);

    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var failures = new List<string>();
        Check("uncollected Ether field departure", () =>
            LeavingTheFieldDoesNotDeclareTheEtherUnavailable(createWalkmeshReader));
        Check("unexpected exit field departure", () =>
            UnexpectedExitDepartureReportsTheAreaChange(createWalkmeshReader));
        Check("matching exit completion", () =>
            MatchingExitCompletionKeepsItsSpeech(createWalkmeshReader));
        Check("genuine item removal", () =>
            GenuineItemRemovalKeepsItsSpeech(createWalkmeshReader));
        Check("short opening corner with a long return route", () =>
            ShortOpeningCornerDoesNotClaimArrival(createWalkmeshReader));
        Check("zero-vector formatter contract", () =>
            Equal("at destination", FieldNavigationSpokenCueFormatter.Format(0, 0, Transform, 60),
                "the formatter's genuine zero-vector contract is unchanged"));
        if (failures.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));

        void Check(string name, Action action)
        {
            try { action(); }
            catch (Exception exception) { failures.Add($"North Corel readout [{name}]: {exception.Message}"); }
        }
    }

    private static void LeavingTheFieldDoesNotDeclareTheEtherUnavailable(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = CreateEtherReader(() => 0);
        var controller = LockEther(reader, createWalkmeshReader);
        var result = controller.UpdateLiveTracking(TownArrival, NoInput, Transform, false, 80);
        AssertFieldDeparture(result, controller, "Ether");
        Equal(1, reader.ReadTargets(BeforePot).Count,
            "the uncollected Ether remains available when its original field is revisited");
        Equal("navigation completion, target field changed, reason=native target field changed to 450",
            controller.LastNavigationDiagnostic, "the existing field-departure diagnostic is preserved");
    }

    private static void UnexpectedExitDepartureReportsTheAreaChange(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var controller = LockExit(createWalkmeshReader);
        var unexpected = TownArrival with { FieldId = 451 };
        AssertFieldDeparture(
            controller.UpdateLiveTracking(unexpected, NoInput, Transform, false, 80),
            controller,
            "Exit to North Corel");
    }

    private static void MatchingExitCompletionKeepsItsSpeech(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var controller = LockExit(createWalkmeshReader);
        var result = controller.UpdateLiveTracking(TownArrival, NoInput, Transform, false, 80);
        Equal("Exit to North Corel reached. Navigation off.", result?.Speech,
            "the matching native exit still reports successful completion");
        Equal(false, controller.BeaconEnabled, "a completed exit stops navigation");
    }

    private static void GenuineItemRemovalKeepsItsSpeech(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        byte collected = 0;
        var reader = CreateEtherReader(() => collected);
        var controller = LockEther(reader, createWalkmeshReader);
        collected = 1;
        var result = controller.UpdateLiveTracking(BeforePot, NoInput, Transform, false, 80);
        Equal("Ether no longer available. Navigation off.", result?.Speech,
            "same-field native removal keeps its distinct availability announcement");
        Equal(false, controller.BeaconEnabled, "native removal stops the beacon");
    }

    private static void ShortOpeningCornerDoesNotClaimArrival(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // runtime-before.log, 15:36:44-46: player(-10,211,-10), first corner(7,230,0),
        // then(-154,243,0), with671 units remaining. Freeze the recorded opening
        // at the planner boundary so subsequent obstacle-planner repairs cannot
        // erase this speech regression. The rest uses the native left-hand corridor.
        var position = new FieldPositionSnapshot(1, 453, 0, -10, 211, -10, 9, 192);
        var planner = new RecordedOpeningPlanner(createWalkmeshReader(453), position);
        var target = Exit();
        var tracker = new FieldNavigationRouteTracker(planner);
        Equal(true, tracker.TryStart(position, target, out _), "the recorded return route starts");
        Equal(true, tracker.TryUpdate(position, target, out var guidance), "the opening remains usable");
        Equal(new FieldNavigationRouteWaypoint(7, 230, 0), guidance.Waypoint,
            "the replay retains the logged short first corner");
        Require(guidance.RemainingDistance > 600d,
            $"the return route must remain long, got {guidance.RemainingDistance}");

        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), planner, 60);
        var speech = controller.HandleAction(FieldNavigationAction.RepeatTarget, position, Transform)?.Speech
            ?? throw new InvalidOperationException("the exit selection must be spoken");
        Require(!speech.Contains("at destination", StringComparison.OrdinalIgnoreCase),
            $"a short opening corner falsely announced arrival: {speech}");
        Require(!speech.Contains("direction unavailable", StringComparison.OrdinalIgnoreCase),
            $"the computed direction was discarded: {speech}");
        Equal("Exits, Exit to North Corel. up first, 14 to go by route.", speech,
            "a short opening must distinguish its first direction from the remaining route distance");
    }

    private static void AssertFieldDeparture(
        FieldNavigationActionResult? result, FieldNavigationController controller, string label)
    {
        var speech = result?.Speech ?? throw new InvalidOperationException("field departure must be announced");
        Require(!speech.Contains("no longer available", StringComparison.OrdinalIgnoreCase),
            $"leaving a field is not evidence that its target disappeared: {speech}");
        Require(!speech.Contains("reached", StringComparison.OrdinalIgnoreCase),
            $"the unexpected departure cannot claim successful arrival: {speech}");
        Require(speech.Contains("area", StringComparison.OrdinalIgnoreCase) &&
            speech.Contains(label, StringComparison.Ordinal), $"identify the area change and stopped target: {speech}");
        Equal(false, controller.BeaconEnabled, "field departure stops navigation");
    }

    private static FieldNavigationController LockEther(FieldNavigationObjectReader reader,
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], objectTargetProvider: reader.ReadTargets),
            new FieldWalkmeshRoutePlanner(createWalkmeshReader(453)), 60);
        while (controller.CurrentCategory != FieldNavigationCategory.Objects)
            controller.HandleAction(FieldNavigationAction.NextCategory, BeforePot, Transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, BeforePot, Transform);
        Equal(true, controller.BeaconEnabled, "the uncollected Ether beacon must actually lock");
        return controller;
    }

    private static FieldNavigationController LockExit(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([Exit()]),
            new FieldWalkmeshRoutePlanner(createWalkmeshReader(453)), 60);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, BeforePot, Transform);
        Equal(true, controller.BeaconEnabled, "the native exit beacon must actually lock");
        return controller;
    }

    private static FieldNavigationTarget Exit() => new(453, FieldNavigationCategory.Exits,
        "Exit to North Corel", -26, -217, 0, "readout:ncoin1:exit", DestinationFieldIds: [450],
        TriggerLine: new(-75, -217, 0, 23, -217, 0));

    private static FieldNavigationObjectReader CreateEtherReader(Func<byte> collectionByte)
    {
        const int eventTable = 0x03000000;
        return new(address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? eventTable : 0,
            address => address switch
            {
                FieldPositionReader.AddressFieldNumModels => 1,
                eventTable + 0x72 => 30,
                FieldNavigationObjectReader.AddressFieldBankBase + 0x401 => collectionByte(),
                _ => 0
            },
            _ => "Ether", _ => null,
            [FieldNavigationObjectCatalog.CreateAllFields().Single(item => item.FieldId == 453 && item.EntityId == 3)],
            _ => true);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private sealed class RecordedOpeningPlanner : IFieldNavigationRoutePlanner
    {
        private readonly FieldNavigationRoutePlan recorded;

        internal RecordedOpeningPlanner(FieldWalkmeshReader reader, FieldPositionSnapshot position)
        {
            var mesh = reader.Read(position).Walkmesh
                ?? throw new InvalidOperationException("the installed ncoin1 walkmesh is required");
            int[] triangles = [9, 10, 11, 12, 13, 14, 2, 1, 0];
            Require(FieldWalkmeshPathfinder.TryBuildNativePortals(mesh, triangles, out var portals),
                "the recorded left-side return corridor must retain native adjacent portals");
            var final = new FieldNavigationRouteWaypoint(-26, -217, 0);
            var tail = FieldWalkmeshPathfinder.BuildStableWaypoints(-154, 243, 0, portals.Skip(1).ToArray(), final);
            recorded = new(453, "readout:ncoin1:exit", triangles, portals, final, 0,
                TargetTriggerLine: Exit().TriggerLine,
                StableWaypointsOverride:
                [
                    new(new(7, 230, 0), 1, MustReach: true),
                    new(new(-154, 243, 0), 1, MustReach: true),
                    .. tail.Select(step => step with { RequiredPortalIndex = step.RequiredPortalIndex + 1 })
                ]);
        }

        public string LastDiagnostic => "recorded North Corel short opening, native return corridor";
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return position.FieldId == 453;
        }
        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = recorded;
            return position.FieldId == 453 && target.FieldId == 453;
        }
        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new(7, 230, 0);
            return position.FieldId == 453 && target.FieldId == 453;
        }
    }
}
