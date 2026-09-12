using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The navigation services a controller command acts on, as the runtime that owns
/// them sees it. Both runtimes implement this over the same
/// <see cref="FieldNavigationController"/> / <see cref="WorldMapNavigationController"/>
/// and the one auto walk controller, so the controller cannot end up meaning
/// different things on the two executables.
/// </summary>
public interface IControllerNavigationTarget
{
    /// <summary>Whether spoken guidance is running to some target right now.</summary>
    bool RouteIsActive { get; }

    /// <summary>Whether the mod is currently walking the party.</summary>
    bool AutoWalkIsActive { get; }

    /// <summary>Moves the selection. Returns what to say, or null.</summary>
    string? Apply(FieldNavigationAction action);

    /// <summary>
    /// Starts guidance to whatever is selected, explicitly. Asking again for the
    /// same target restates it; asking for a different one replaces the route. This
    /// never turns guidance off - only <see cref="Stop"/> does.
    /// </summary>
    string? StartNavigation();

    /// <summary>Starts guidance if needed and then walks it.</summary>
    string? StartAutoWalk();

    /// <summary>
    /// Stops guidance and any auto walk with it. Always does both, and always
    /// answers, because a stop that quietly did nothing leaves the player being
    /// walked somewhere by a mod that has stopped listening.
    /// </summary>
    string? Stop();

    /// <summary>
    /// Holds the party still while the menu is open, without giving up the route.
    /// Browsing mid-walk is the case the player asked for; being carried away from
    /// the spot while reading a list is not.
    /// </summary>
    void SuspendAutoWalkWhileBrowsing();
}

/// <summary>
/// Drains what the input hook queued and does the parts that are allowed to take
/// time - speaking, planning routes, starting and stopping the walk.
///
/// <para>This is the other half of the split the hook forces. The hook decides, in
/// the game's own input read, which buttons the game may see; it cannot speak there,
/// so it leaves commands behind. Everything here runs on the mod's worker, where
/// blocking is fine.</para>
/// </summary>
public sealed class ControllerNavigationDispatcher
{
    private readonly IControllerNavigationTarget target;
    private readonly Func<string, bool> speak;
    private readonly Action<string>? log;

    public ControllerNavigationDispatcher(
        IControllerNavigationTarget target,
        Func<string, bool> speak,
        Action<string>? log = null)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.speak = speak ?? throw new ArgumentNullException(nameof(speak));
        this.log = log;
    }

    /// <summary>How many commands one drain will take, so a backlog cannot stall a frame.</summary>
    public const int MaximumCommandsPerDrain = 8;

    /// <summary>
    /// Applies what is queued. <paramref name="menuIsOpen"/> is the hook's own view,
    /// not a guess: while it is open the party is held still.
    /// </summary>
    /// <returns>Whether anything was said.</returns>
    /// <summary>
    /// How long the game may stop polling before the menu is treated as abandoned. An
    /// SDL device that goes away simply stops being read, and a menu left open would
    /// hold the walk suspended for ever.
    /// </summary>
    public static readonly TimeSpan PollFreshness = TimeSpan.FromMilliseconds(500);

    public bool Drain(
        ControllerNavigationCapture capture,
        ControllerNavigationDomain domain,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(capture);

        // One capture serves both domains, and exactly one module is live at a time.
        // Without this the field would consume the world map's commands and answer
        // them against a field selection that is not on screen.
        if (capture.Owner != domain)
        {
            return false;
        }

        // The game has stopped asking. RequestClose cannot serve this: it only raises
        // a flag for the next poll, and there is no next poll - which is how an
        // unplugged pad left the menu open for ever, repeating its complaint on every
        // worker frame. The capture retires it here instead, once.
        if (capture.TryRetireForLostPolling(nowUtc, PollFreshness))
        {
            var spokeRetirement = Say("Navigation menu closed: the controller stopped responding.");
            if (target.AutoWalkIsActive)
            {
                target.SuspendAutoWalkWhileBrowsing();
            }

            return spokeRetirement;
        }

        var menuIsOpen = capture.IsOpen;
        var liveGeneration = capture.Generation.Id;
        var spoke = false;

        while (capture.Announcements.TryDequeue(out var announcement))
        {
            spoke |= Say(announcement);
        }

        // Held still, every tick the menu is open, rather than once when it opened -
        // the walk is re-asserted per frame by its own controller, so a single
        // suspend would be undone by the next one.
        if (menuIsOpen && target.AutoWalkIsActive)
        {
            target.SuspendAutoWalkWhileBrowsing();
        }

        for (var taken = 0; taken < MaximumCommandsPerDrain; taken++)
        {
            if (!capture.Commands.TryDequeue(out var command, out var commandGeneration))
            {
                break;
            }

            // A selection decided in another room, or under another domain, is not
            // applied here: the target it named is not the one on screen. A stop is
            // never rejected - it is about the walking, which is still happening.
            if (command != ControllerNavigationCommand.StopNavigation &&
                commandGeneration != liveGeneration)
            {
                continue;
            }

            spoke |= Apply(command);
        }

        return spoke;
    }

    private bool Apply(ControllerNavigationCommand command)
    {
        switch (command)
        {
            case ControllerNavigationCommand.Opened:
                // Opening says where the selection already is and how to move it. It
                // starts nothing: the player asked for a menu, not a journey.
                var opening = target.Apply(FieldNavigationAction.RepeatTarget);
                return Say(string.IsNullOrWhiteSpace(opening)
                    ? "Navigation menu. Up and down for targets, bumpers for categories, " +
                      "A to guide, X to walk, B to stop."
                    : $"Navigation menu. {opening}");

            case ControllerNavigationCommand.PreviousCategory:
                return Say(target.Apply(FieldNavigationAction.PreviousCategory));
            case ControllerNavigationCommand.NextCategory:
                return Say(target.Apply(FieldNavigationAction.NextCategory));
            case ControllerNavigationCommand.PreviousTarget:
                return Say(target.Apply(FieldNavigationAction.PreviousTarget));
            case ControllerNavigationCommand.NextTarget:
                return Say(target.Apply(FieldNavigationAction.NextTarget));

            case ControllerNavigationCommand.StartNavigation:
                return Say(target.StartNavigation());
            case ControllerNavigationCommand.StartAutoWalk:
                return Say(target.StartAutoWalk());

            case ControllerNavigationCommand.StopNavigation:
                // Never conditional on anything. A stop reaching a runtime whose
                // module has changed underneath it still has to stop the walking.
                return Say(target.Stop());

            default:
                return false;
        }
    }

    private bool Say(string? speech)
    {
        if (string.IsNullOrWhiteSpace(speech))
        {
            return false;
        }

        log?.Invoke($"Controller navigation: {speech}");
        try
        {
            return speak(speech);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Controller navigation speech failed: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// An <see cref="IControllerNavigationTarget"/> assembled from a runtime's own
/// delegates, so the field and the world map - and x86 and x64 - all reach the same
/// dispatcher without either runtime growing a second copy of these rules.
/// </summary>
public sealed class DelegatedControllerNavigationTarget : IControllerNavigationTarget
{
    private readonly Func<bool> routeIsActive;
    private readonly Func<bool> autoWalkIsActive;
    private readonly Func<FieldNavigationAction, string?> apply;
    private readonly Func<string?> startNavigation;
    private readonly Func<string?> startAutoWalk;
    private readonly Func<string?> stop;
    private readonly Action suspendAutoWalk;

    public DelegatedControllerNavigationTarget(
        Func<bool> routeIsActive,
        Func<bool> autoWalkIsActive,
        Func<FieldNavigationAction, string?> apply,
        Func<string?> startNavigation,
        Func<string?> startAutoWalk,
        Func<string?> stop,
        Action suspendAutoWalk)
    {
        this.routeIsActive = routeIsActive ?? throw new ArgumentNullException(nameof(routeIsActive));
        this.autoWalkIsActive = autoWalkIsActive ?? throw new ArgumentNullException(nameof(autoWalkIsActive));
        this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
        this.startNavigation = startNavigation ?? throw new ArgumentNullException(nameof(startNavigation));
        this.startAutoWalk = startAutoWalk ?? throw new ArgumentNullException(nameof(startAutoWalk));
        this.stop = stop ?? throw new ArgumentNullException(nameof(stop));
        this.suspendAutoWalk = suspendAutoWalk ?? throw new ArgumentNullException(nameof(suspendAutoWalk));
    }

    public bool RouteIsActive => routeIsActive();

    public bool AutoWalkIsActive => autoWalkIsActive();

    public string? Apply(FieldNavigationAction action) => apply(action);

    public string? StartNavigation() => startNavigation();

    public string? StartAutoWalk() => startAutoWalk();

    public string? Stop() => stop();

    public void SuspendAutoWalkWhileBrowsing() => suspendAutoWalk();
}
