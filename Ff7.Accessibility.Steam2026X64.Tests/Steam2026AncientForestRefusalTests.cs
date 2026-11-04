using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Reloaded.Tests;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026AncientForestRefusalTests
{
    internal static void Run()
    {
        var inner = new AncientForestInteractionTests.RefusedForestRoute { Flytrap = true };
        var wrapper = new Steam2026FailClosedFieldRoutePlanner(inner);
        var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 622, 0, 179, -962, 0, 185, 0);
        var target = new FieldNavigationTarget(622, FieldNavigationCategory.Objects, "Spring Gun Clip", 900, -900, 13, "object:622:27");
        wrapper.BeginObservation();
        var preflight = wrapper.PrepareActionRoute(position, target);
        if (!preflight.IsCoherent || preflight.RouteAvailable)
            throw new InvalidOperationException("The visible jaw is a coherent route refusal.");
        inner.Flytrap = false;
        if (wrapper.TryBuildRoute(position, target, out _) ||
            !((IFieldNavigationNativeBoundaryStatus)wrapper).LastFailureWasFlytrap)
            throw new InvalidOperationException("A prepared x64 action must preserve its own flytrap reason after another query overwrites the inner status.");
        wrapper.TryBuildRoute(position with { X = 180 }, target, out _);
        if (!wrapper.HadReadFailure || ((IFieldNavigationNativeBoundaryStatus)wrapper).LastFailureWasFlytrap)
            throw new InvalidOperationException("An incoherent replay must retire the old jaw classification.");
        wrapper.CompletePreparedActionRoute();
        AncientForestInteractionTests.AssertFlytrapRefusal(planner => new Steam2026FailClosedFieldRoutePlanner(planner));
        Console.WriteLine("Steam x64 flytrap refusal speech and prepared-state coherence passed.");
    }
}
