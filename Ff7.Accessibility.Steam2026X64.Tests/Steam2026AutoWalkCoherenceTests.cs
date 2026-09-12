using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// An independent source of intermittent pauses in x64 auto walk.
///
/// <para><c>routeCoherent</c> was <c>nativeExitsCoherent &amp;&amp; !HadReadFailure</c>, and
/// that one flag gates <b>all</b> automatic walking through
/// <c>CanUpdateLiveTracking</c>. The exit scan fails routinely while the field's model
/// ownership changes - the live log alternates <c>nativeExits=-1</c> and healthy reads -
/// so walking to a <em>story</em> target was blocked by a domain it has nothing to do
/// with. <c>Drive</c> is told it may not move, which it reports as success, so nothing
/// was logged either.</para>
/// </summary>
internal static class Steam2026AutoWalkCoherenceTests
{
    public static void Run()
    {
        AFailedExitScanDoesNotBlockAStoryRoute();
        TheExitsCategoryStillNeedsItsOwnDomain();
        ARoutePlannerReadFailureStillBlocksWalking();
    }

    /// <summary>
    /// The live case. Field 156, story targets coherent, exit scan failing, target
    /// "Take the right way to the station" - a story target.
    /// </summary>
    private static void AFailedExitScanDoesNotBlockAStoryRoute()
    {
        var coherence = Steam2026FieldNavigationActionGate.ResolveCoherence(
            nativeExitsCoherent: false,
            storyCoherent: true,
            npcsCoherent: true,
            objectsCoherent: true,
            routePlannerHadReadFailure: false);

        Equal(true, coherence.Route,
            "a failing exit scan does not make the route incoherent");
        Equal(true, coherence.Story, "the story domain is still coherent");
        Equal(false, coherence.Exits, "and the exits domain is correctly not");

        Equal(true,
            Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                FieldNavigationCategory.Story, beaconEnabled: true, coherence),
            "so a story route may be walked while the exit scan is failing");

        // The same for the other two categories that do not involve exits.
        foreach (var category in new[]
                 { FieldNavigationCategory.Npcs, FieldNavigationCategory.Objects })
        {
            Equal(true,
                Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                    category, beaconEnabled: true, coherence),
                $"and so may a {category} route");
        }
    }

    private static void TheExitsCategoryStillNeedsItsOwnDomain()
    {
        var failing = Steam2026FieldNavigationActionGate.ResolveCoherence(
            nativeExitsCoherent: false,
            storyCoherent: true,
            npcsCoherent: true,
            objectsCoherent: true,
            routePlannerHadReadFailure: false);
        Equal(false,
            Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                FieldNavigationCategory.Exits, beaconEnabled: true, failing),
            "an exit route is not walked while the exit scan is failing");

        var healthy = Steam2026FieldNavigationActionGate.ResolveCoherence(
            nativeExitsCoherent: true,
            storyCoherent: true,
            npcsCoherent: true,
            objectsCoherent: true,
            routePlannerHadReadFailure: false);
        Equal(true, healthy.Exits, "a healthy exit scan makes the exits domain coherent");
        Equal(true,
            Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                FieldNavigationCategory.Exits, beaconEnabled: true, healthy),
            "and an exit route is walked then");
    }

    /// <summary>
    /// The safety half. The route planner's own reads are what the walking is steered
    /// by, so losing those must still stop every category.
    /// </summary>
    private static void ARoutePlannerReadFailureStillBlocksWalking()
    {
        var coherence = Steam2026FieldNavigationActionGate.ResolveCoherence(
            nativeExitsCoherent: true,
            storyCoherent: true,
            npcsCoherent: true,
            objectsCoherent: true,
            routePlannerHadReadFailure: true);

        Equal(false, coherence.Route, "an unreadable route planner is an incoherent route");
        Equal(false, coherence.Exits, "and takes the exits domain with it");
        foreach (var category in new[]
                 {
                     FieldNavigationCategory.Exits,
                     FieldNavigationCategory.Story,
                     FieldNavigationCategory.Npcs,
                     FieldNavigationCategory.Objects,
                 })
        {
            Equal(false,
                Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                    category, beaconEnabled: true, coherence),
                $"so no {category} route is walked on an unreadable planner");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Auto walk coherence: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
