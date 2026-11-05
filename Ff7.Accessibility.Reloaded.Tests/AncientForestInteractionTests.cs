using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class AncientForestInteractionTests
{
    internal static void Run()
    {
        AssertCreatureSelectionLocation();
        AssertObjectsPlantSummary();
        AssertFlytrapRefusal();
        AssertVisibleExitSurvivesFlytrap();
        Console.WriteLine("Ancient Forest flytrap refusals name the hazard and retain native exits.");
        var target = new FieldNavigationTarget(622, FieldNavigationCategory.Objects, "Frog 2", 0, 0, 0,
            "object:622:10", TriggerEntityId: 10, CompletesOnArrival: false, InteractionRadius: 40);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), new StraightRoute());
        controller.InteractionArrivalInstruction = (_, _) => "Your normal Confirm button releases what you carry first.";
        var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 622, 0, 120, 0, 0, 3, 0);
        var here = start with { X = 0 };
        for (var i = 0; controller.CurrentCategory != FieldNavigationCategory.Objects && i < 5; i++)
            controller.HandleAction(FieldNavigationAction.NextCategory, start, new(0));
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, new(0));
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        controller.UpdateLiveTracking(start, default, new(0), false, 40, FieldLadderStateSnapshot.NotMounted, now);
        var arrival = controller.UpdateLiveTracking(here, default, new(0), false, 40, FieldLadderStateSnapshot.NotMounted, now.AddMilliseconds(200));
        if (arrival?.Speech.Contains("releases what you carry", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("A forest creature arrival must explain that Confirm releases the held creature first: " + arrival?.Speech);
        if (arrival?.Speech.Contains("Interact here", StringComparison.Ordinal) == true)
            throw new InvalidOperationException("The generic arrival obscures the carried-creature interaction.");
        var bank = new byte[128];
        bank[9] = 1;
        if (AncientForestInteractionGuidance.Describe(target, bank) is null ||
            AncientForestInteractionGuidance.Describe(target with { FieldId = 116 }, bank) is not null ||
            AncientForestInteractionGuidance.Describe(target, null) is not null)
            throw new InvalidOperationException("Carrying advice must be tied to coherent forest state.");
        bank[9] = 255;
        if (AncientForestInteractionGuidance.Describe(target, bank) is not null)
            throw new InvalidOperationException("An invalid carried ID must stay unknown.");
        bank[9] = 1;
        foreach (var field in new[] { 620, 623 })
            if (AncientForestInteractionGuidance.Describe(target with { FieldId = field, Label = "Beehive" }, bank) is not null)
                throw new InvalidOperationException("Beehive Talk can replace what is carried; it cannot promise a release first.");
        var bite = new FieldActivityObservation(622, 441, -914, 13, 23, false, 1620, new(0), true,
            [], new Dictionary<int, FieldActivityWaitState>(), null, null, -1) { ReadTemporaryByte = _ => 0 };
        if (!AncientForestInteractionGuidance.IsBitePose(bite) ||
            AncientForestInteractionGuidance.IsBitePose(bite with { IsPlayerControlled = true }) ||
            AncientForestInteractionGuidance.IsBitePose(bite with { ReadTemporaryByte = _ => -1 }) ||
            AncientForestInteractionGuidance.IsBitePose(bite with { PlayerZ = 300 }))
            throw new InvalidOperationException("Only the observed native bite pose can retire automatic travel.");
        var bitePosition = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 622, 0, 441, -914, 13, 23, 0);
        if (!AncientForestInteractionGuidance.IsBitePose(bitePosition, false, new byte[128]) ||
            AncientForestInteractionGuidance.IsBitePose(bitePosition, true, new byte[128]) ||
            AncientForestInteractionGuidance.IsBitePose(bitePosition, false, null) ||
            AncientForestInteractionGuidance.IsBitePose(bitePosition with { Z = 300 }, false, new byte[128]))
            throw new InvalidOperationException("Navigation safety must recognize the bite without constructing or enabling an activity readout.");
        Console.WriteLine("Ancient Forest carrying-aware arrival test passed.");
    }

    private static void AssertCreatureSelectionLocation()
    {
        var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 622, 0, -2016, -748, 28, 85, 0);
        var target = new FieldNavigationTarget(622, FieldNavigationCategory.Objects, "Insect 1",
            -1810, -665, 28, "object:622:6", TriggerEntityId: 6);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([], objectTargetProvider: _ => [target]));
        var preview = controller.HandleAction(FieldNavigationAction.PreviousCategory, position, new(0));
        if (preview?.Speech.Contains("near the forest entrance", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException("The Objects preview must identify the insect's place even without a route: " + preview?.Speech);

        target = target with { X = -848, Y = -735, Z = 17 };
        preview = controller.HandleAction(FieldNavigationAction.RepeatTarget, position, new(0));
        if (preview?.Speech.Contains("Insect 1", StringComparison.Ordinal) != true ||
            !preview.Value.Speech.Contains("lower side of the path", StringComparison.OrdinalIgnoreCase) ||
            preview.Value.Speech.Contains("near the forest entrance", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Moving the insect must update its place and retain its identity: " + preview?.Speech);
    }

    private static void AssertObjectsPlantSummary()
    {
        var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 622, 0, -2016, -748, 28, 85, 0);
        var throwingSpot = new FieldNavigationTarget(622, FieldNavigationCategory.Objects,
            "Pitcher plant 1, throwing spot", -56, -155, 228, "throwing-spot");
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([throwingSpot]));
        var bank = new byte[128];
        bank[17] = 1;
        bank[18] = 1;
        controller.ForestPlantSummary = field =>
            AncientForestPuzzleReadout.DescribePitcherPlants(field, new(bank, new HashSet<int> { 2 }, []));

        var preview = controller.HandleAction(FieldNavigationAction.PreviousCategory, position, new(0));
        if (preview?.Speech.Contains(throwingSpot.Label, StringComparison.Ordinal) != true ||
            !preview.Value.Speech.Contains("Pitcher plants from left to right. Plant 1 open. Plant 2 shaking. Plant 3 closed.",
                StringComparison.Ordinal))
            throw new InvalidOperationException("One reachable throwing spot must still expose all three current plants: " + preview?.Speech);

        bank[18] = 0;
        preview = controller.HandleAction(FieldNavigationAction.RepeatTarget, position, new(0));
        if (preview?.Speech.Contains("Plant 3 open", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Repeating the Objects selection must read the current plant state.");

        preview = controller.HandleAction(FieldNavigationAction.NextTarget, position, new(0));
        if (preview?.Speech.Contains("Pitcher plants from left to right", StringComparison.Ordinal) == true)
            throw new InvalidOperationException("Browsing each target must avoid replaying the whole plant list.");

        bank[17] = 255;
        preview = controller.HandleAction(FieldNavigationAction.RepeatTarget, position, new(0));
        if (preview?.Speech.Contains("Pitcher plants from left to right", StringComparison.Ordinal) == true)
            throw new InvalidOperationException("Unreadable plant state must be omitted from the Objects preview.");

        Console.WriteLine("Ancient Forest Objects previews distinguish creatures and describe all current plants.");
    }

    internal static void AssertFlytrapRefusal(Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner>? wrap = null)
    {
        var target = new FieldNavigationTarget(622, FieldNavigationCategory.Objects, "Spring Gun Clip", 900, -900, 13,
            "object:622:27", TriggerEntityId: 27, InteractionRadius: 110);
        var obstruction = new RefusedForestRoute { Flytrap = true };
        var planner = wrap?.Invoke(obstruction) ?? obstruction;
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), planner);
        var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 622, 0, 179, -962, 0, 185, 0);
        for (var i = 0; controller.CurrentCategory != FieldNavigationCategory.Objects && i < 5; i++)
            controller.HandleAction(FieldNavigationAction.NextCategory, position, new(0));
        var refusal = controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, new(0));
        if (refusal?.Speech.Contains("Mutant Flytrap", StringComparison.Ordinal) != true || controller.BeaconEnabled || controller.IsHoldingForNativeBoundary)
            throw new InvalidOperationException("A jaw refusal must name the visible hazard and leave navigation off, rather than silently waiting or reporting only unavailable: " + refusal?.Speech);
    }

    private static void AssertVisibleExitSurvivesFlytrap()
    {
        var exit = new FieldNavigationTarget(622, FieldNavigationCategory.Exits, "Exit to Ancient Forest", 900, -900, 13,
            "gateway:622:0:620");
        var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 622, 0, 179, -962, 0, 185, 0);
        IReadOnlyList<FieldNavigationTarget> native = [exit];
        var obstruction = new RefusedForestRoute { Flytrap = true };
        var provider = new ReachableFieldExitTargetProvider(_ => native, obstruction);
        if (provider.ReadTargets(position).SingleOrDefault().StableId != exit.StableId)
            throw new InvalidOperationException("A native exit must stay selectable when an active flytrap blocks automatic walking to it.");
        native = [];
        if (provider.ReadTargets(position).Count != 0)
            throw new InvalidOperationException("The jaw status cannot retain an exit the native game removed.");
        var ordinaryFailure = new ReachableFieldExitTargetProvider(_ => [exit], new RefusedForestRoute());
        if (ordinaryFailure.ReadTargets(position).Count != 0)
            throw new InvalidOperationException("An ordinary unavailable route must not be described as a visible flytrap obstruction.");
    }

    internal sealed class RefusedForestRoute : IFieldNavigationRoutePlanner, IFieldNavigationNativeBoundaryStatus, IFieldNavigationRouteReadStatus
    {
        internal bool Flytrap { get; set; }
        public bool LastFailureWasFlytrap => Flytrap;
        public bool LastFailureWasNativeBoundary => false;
        public bool LastReadWasCoherent => true;
        public IReadOnlyList<int> LastBlockingBoundaryTriangles => [];
        public string LastDiagnostic => "coherent native obstruction";
        public bool WouldRouteIfBoundaryReleased(FieldPositionSnapshot position, FieldNavigationTarget target, int releasedTriangle) => false;
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle) { triangle = position.TriangleId; return true; }
        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan) { plan = null!; return false; }
        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRouteWaypoint waypoint) { waypoint = default; return false; }
    }

    private sealed class StraightRoute : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight test floor";
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        { triangle = position.TriangleId; return true; }
        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan)
        {
            plan = new(position.FieldId, target.StableId, [position.TriangleId], [], new(target.X, target.Y, target.Z), position.TriangleId);
            return true;
        }
        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRouteWaypoint waypoint)
        { waypoint = new(target.X, target.Y, target.Z); return true; }
    }
}
