namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The one place that decides what a controller start or stop does to the navigation
/// services, for the field and the world map on both runtimes.
///
/// <para>It is built from primitives only - toggle the beacon, is the beacon on,
/// start the walk, stop every walk, suspend the walk - so the rules below exist once
/// rather than being written out again in each adapter and a third time in a test.
/// The rules are the part that was wrong: A is spoken guidance, and spoken guidance
/// means the mod stops driving.</para>
/// </summary>
public sealed class ControllerNavigationServices : IControllerNavigationTarget
{
    private readonly Func<bool> beaconEnabled;
    private readonly Func<bool> navigationIsHeld;
    private readonly Action requestAutoWalkWhenHeld;
    private readonly Func<FieldNavigationAction, string?> handleAction;
    private readonly Func<bool> autoWalkIsActive;
    private readonly Func<bool> tryStartAutoWalk;
    private readonly Action stopEveryAutoWalk;
    private readonly Action suspendAutoWalk;

    /// <param name="stopEveryAutoWalk">
    /// Stops the walk whatever domain it belongs to - not just this adapter's. The
    /// player can press B after the module has already changed, and a stop that only
    /// knew about the domain it was asked from would leave them being driven.
    /// </param>
    /// <param name="navigationIsHeld">
    /// A destination is selected and waiting for the game to open its way. The beacon is
    /// off while that is true, so every rule below that asked only about the beacon would
    /// treat a held selection as no selection: B would not cancel it, and A or X would
    /// cancel it by accident instead of restating it.
    /// </param>
    /// <param name="requestAutoWalkWhenHeld">
    /// Carries "walk me there" across the wait. Without it, X at a shut door degrades into
    /// spoken guidance and the player is never walked when the door opens.
    /// </param>
    public ControllerNavigationServices(
        Func<bool> beaconEnabled,
        Func<FieldNavigationAction, string?> handleAction,
        Func<bool> autoWalkIsActive,
        Func<bool> tryStartAutoWalk,
        Action stopEveryAutoWalk,
        Action suspendAutoWalk,
        Func<bool>? navigationIsHeld = null,
        Action? requestAutoWalkWhenHeld = null)
    {
        this.navigationIsHeld = navigationIsHeld ?? (static () => false);
        this.requestAutoWalkWhenHeld = requestAutoWalkWhenHeld ?? (static () => { });
        this.beaconEnabled = beaconEnabled ?? throw new ArgumentNullException(nameof(beaconEnabled));
        this.handleAction = handleAction ?? throw new ArgumentNullException(nameof(handleAction));
        this.autoWalkIsActive = autoWalkIsActive ?? throw new ArgumentNullException(nameof(autoWalkIsActive));
        this.tryStartAutoWalk = tryStartAutoWalk ?? throw new ArgumentNullException(nameof(tryStartAutoWalk));
        this.stopEveryAutoWalk = stopEveryAutoWalk ?? throw new ArgumentNullException(nameof(stopEveryAutoWalk));
        this.suspendAutoWalk = suspendAutoWalk ?? throw new ArgumentNullException(nameof(suspendAutoWalk));
    }

    /// <summary>
    /// Navigation is engaged: either walking a route, or holding a destination until the
    /// game opens its way. Both are a selection the player made and can cancel.
    /// </summary>
    public bool RouteIsActive => beaconEnabled() || navigationIsHeld();

    public bool AutoWalkIsActive => autoWalkIsActive();

    public string? Apply(FieldNavigationAction action) => handleAction(action);

    /// <summary>
    /// A: spoken guidance to the selection, and the mod stops driving.
    ///
    /// <para>Leaving the walk running here was the defect: the player opens the menu
    /// mid-walk, picks somewhere, presses A for directions, and is still being
    /// carried along by a route they have just replaced.</para>
    /// </summary>
    public string? StartNavigation()
    {
        stopEveryAutoWalk();
        return RestartBeacon();
    }

    /// <summary>X: guidance to the selection, and the mod walks it.</summary>
    public string? StartAutoWalk()
    {
        stopEveryAutoWalk();
        var speech = RestartBeacon();
        if (navigationIsHeld())
        {
            // The way is shut for now. Remember that this was a walk, not a reading.
            requestAutoWalkWhenHeld();
            if (!beaconEnabled())
            {
                // Nothing is routed, so nothing moves: the hold produces no movement at all.
                return speech;
            }

            // A route is running even though the destination is held - the walk to the
            // line that opens the door. Walking that leg is the request, not a substitute
            // for it, so it falls through and starts.
        }

        if (!beaconEnabled() || !tryStartAutoWalk())
        {
            return speech;
        }

        return string.IsNullOrWhiteSpace(speech) ? "Auto walk on." : $"{speech} Auto walk on.";
    }

    /// <summary>B, or R3 from the open menu. Always both, and always answers.</summary>
    public string? Stop()
    {
        stopEveryAutoWalk();
        var speech = RouteIsActive
            ? handleAction(FieldNavigationAction.ToggleBeacon)
            : null;
        return string.IsNullOrWhiteSpace(speech) ? "Navigation off." : speech;
    }

    public void SuspendAutoWalkWhileBrowsing() => suspendAutoWalk();

    /// <summary>
    /// Makes the selection the route, whatever the route was.
    ///
    /// <para>The keyboard's own binding toggles, so pressing it on a running route
    /// turns it off. Turning off first and on again makes every press mean "go
    /// here": the same target is restated, a different one replaces it, and nothing
    /// the player does with A or X can cancel by accident.</para>
    /// </summary>
    private string? RestartBeacon()
    {
        if (RouteIsActive)
        {
            _ = handleAction(FieldNavigationAction.ToggleBeacon);
        }

        return handleAction(FieldNavigationAction.ToggleBeacon);
    }
}
