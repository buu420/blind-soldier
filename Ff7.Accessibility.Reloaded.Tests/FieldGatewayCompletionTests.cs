using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FieldGatewayCompletionTests
{
    internal static void Run()
    {
        // These are the two native mtcrl_4 gateways. Both enter field464, on separate tracks.
        var upper = new FieldNavigationTarget(462, FieldNavigationCategory.Exits,
            "Upper tracks", 2449, -8, 1039, DestinationFieldIds: [464],
            TriggerLine: new(2442, -39, 1057, 2457, 23, 1022));
        var lower = new FieldNavigationTarget(462, FieldNavigationCategory.Exits,
            "Lower tracks", 2146, -52, -355, DestinationFieldIds: [464],
            TriggerLine: new(2115, -79, -295, 2177, -25, -415));
        var lowerDeparture = Position(462, 2157, -44, -385, 95);
        var upperDeparture = Position(462, 2400, -8, 1040, 9);
        var lowerArrival = Position(464, -1911, 143, 358, 151);
        var upperArrival = Position(464, -3053, 976, 756, 138);

        AssertCompletion(upper, [upper, lower], lowerDeparture, lowerArrival, false,
            "entering the lower track cannot claim the upper Story gateway was reached");
        AssertCompletion(upper, [upper, lower], upperDeparture, upperArrival, true,
            "the upper gateway still completes from its actual departure");
        AssertCompletion(lower, [upper, lower], upperDeparture, upperArrival, false,
            "the reverse wrong-track transition cannot complete either");
        AssertCompletion(lower, [upper, lower], lowerDeparture, lowerArrival, true,
            "the lower gateway completes from its actual departure");
        AssertCompletion(upper, [upper], upperDeparture, upperArrival, true,
            "unambiguous gateways retain field-transition completion");
        AssertCompletion(upper, [upper, lower], upperDeparture, upperArrival with { FieldId = 465 }, false,
            "another destination remains a cancellation");

        // Costa's beach exit is a three-segment native gateway into the same town entry.
        var beach0 = new FieldNavigationTarget(449, FieldNavigationCategory.Exits,
            "Town", -791, 871, 106, DestinationFieldIds: [443],
            TriggerLine: new(-834, 880, 104, -749, 863, 109));
        var beach1 = beach0 with { TriggerLine = new(-749, 863, 109, -685, 867, 105) };
        var beach2 = beach0 with { TriggerLine = new(-685, 867, 105, -620, 902, 97) };
        AssertCompletion(beach0, [beach0, beach1, beach2], Position(449, -640, 890, 99, 0),
            Position(443, 1351, 1006, 0, 49), true,
            "connected segments of one native gateway remain equivalent, including transitive connections");
    }

    private static void AssertCompletion(FieldNavigationTarget exit,
        FieldNavigationTarget[] exits, FieldPositionSnapshot departure,
        FieldPositionSnapshot arrival, bool expected, string label)
    {
        var target = exit with { Category = FieldNavigationCategory.Story, CompletesOnArrival = true };
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target, .. exits]), new DirectPlanner());
        var transform = new FieldNavigationControlTransform(0);
        controller.HandleAction(FieldNavigationAction.NextCategory, departure, transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, departure, transform);
        if (!controller.BeaconEnabled) throw new InvalidOperationException("Test route did not start.");
        var speech = controller.UpdateLiveTracking(arrival, default, transform, false)?.Speech ?? "";
        if (speech.Contains(" reached.", StringComparison.Ordinal) != expected || controller.BeaconEnabled)
            throw new InvalidOperationException($"{label}: {speech}; active={controller.BeaconEnabled}");
    }

    private static FieldPositionSnapshot Position(int field, int x, int y, int z, ushort triangle) =>
        new(FieldPositionReader.FieldModule, field, 0, x, y, z, triangle, 0);

    private sealed class DirectPlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "single-triangle test route";
        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = new(position.FieldId,
                $"{target.FieldId}:{target.Category}:{target.Label}:{target.X}:{target.Y}:{target.Z}",
                [position.TriangleId], [], new(target.X, target.Y, target.Z), position.TriangleId);
            return true;
        }
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        { triangle = position.TriangleId; return true; }
        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        { waypoint = new(target.X, target.Y, target.Z); return true; }
    }
}
