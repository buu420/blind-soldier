namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// When a host loop may turn "the player asked to be walked there" into an actual
/// ToggleBeacon. Shared because both runtimes run the same loop shape and got this wrong in
/// the same way.
/// </summary>
public static class FieldNavigationAutoWalkIntent
{
    /// <summary>
    /// Whether this scan should queue a ToggleBeacon to start the route the player asked to
    /// walk.
    ///
    /// <para>Never while a destination is held for a shut native door. The hold already
    /// <em>is</em> the request, and a second ToggleBeacon is exactly how the player cancels
    /// a hold - so queueing one each scan cancelled the wait the moment it began. The beacon
    /// is off for the whole wait, which is why testing that flag alone was not enough.</para>
    /// </summary>
    public static bool ShouldQueueAutoWalkToggle(
        bool pendingAutoWalkStart,
        bool beaconEnabled,
        bool holdingForNativeBoundary,
        bool alreadyQueued) =>
        pendingAutoWalkStart && !beaconEnabled && !holdingForNativeBoundary && !alreadyQueued;
}
