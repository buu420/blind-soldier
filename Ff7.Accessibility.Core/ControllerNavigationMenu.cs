namespace Ff7.Accessibility.Core;

/// <summary>What the player asked the navigation menu for on this tick.</summary>
public enum ControllerNavigationCommand
{
    None,

    /// <summary>The menu just opened. The adapter describes where the selection is.</summary>
    Opened,

    /// <summary>The menu just closed without starting or stopping anything.</summary>
    Closed,

    PreviousCategory,
    NextCategory,
    PreviousTarget,
    NextTarget,

    /// <summary>
    /// Start walking to the selected target. Explicit: asking for a route that is
    /// already running restates it, it does not turn it off.
    /// </summary>
    StartNavigation,

    /// <summary>Start the route and have the mod walk it.</summary>
    StartAutoWalk,

    /// <summary>Stop the route and any auto walk with it.</summary>
    StopNavigation,
}

/// <summary>
/// What the world looks like this tick, from the adapter that knows.
/// </summary>
/// <param name="IsHostForeground">Whether the game is the window in front of the player.</param>
/// <param name="ModuleSupportsNavigation">
/// Whether the player is somewhere the spoken navigation applies - a field or the
/// world map. A battle or a minigame is not, and the menu must not take the pad
/// there: those controls are the game's.
/// </param>
/// <param name="GameIsBusy">
/// Whether the game has its own dialogue or menu on screen. The menu closes: those
/// windows are driven by the same buttons and the player is talking to them.
/// </param>
public readonly record struct ControllerNavigationContext(
    bool IsHostForeground,
    bool ModuleSupportsNavigation,
    bool GameIsBusy);

/// <param name="Command">What to do, or <see cref="ControllerNavigationCommand.None"/>.</param>
/// <param name="MenuIsOpen">Whether the menu owns the pad after this tick.</param>
/// <param name="SuppressedButtons">
/// What the game must not see right now. Non-empty after the menu has closed too,
/// until the button that closed it has been let go.
/// </param>
/// <param name="Announcement">
/// Something to say that is not about the selection - the menu refusing to open, or
/// closing because the game took over. Selection wording belongs to the navigation
/// controllers, which already own it.
/// </param>
public readonly record struct ControllerNavigationMenuResult(
    ControllerNavigationCommand Command,
    bool MenuIsOpen,
    GamepadButton SuppressedButtons,
    string? Announcement)
{
    public static ControllerNavigationMenuResult Idle => default;
}

/// <summary>
/// The spoken navigation menu on a controller: which button means what, when the
/// menu may own the pad, and when it has to give it back.
///
/// <para>The mapping is the player's, as asked for: R3 opens and closes, D-pad up
/// and down cycle targets, the bumpers cycle categories, A starts walking to the
/// selection, X starts auto walk, and B stops and closes. Starting closes the menu
/// so the ordinary controls come straight back - the player asked to go somewhere,
/// not to stay in a menu.</para>
///
/// <para>R3 with the menu closed <em>always</em> opens it, whether or not a route is
/// running. The player asked for that directly: a route in progress is exactly when
/// you want to look at where you are going, or pick somewhere else, and a stick
/// click that sometimes browses and sometimes cancels is a click you cannot make
/// confidently. Stopping is B or a second R3, both from inside the open menu, and
/// both unambiguous.</para>
///
/// <para>Every start is explicit, so a second A on the same target restates the
/// route rather than cancelling it by accident, and A on a different target replaces
/// the route. Only B and R3-from-open stop anything.</para>
///
/// <para>The menu will not open unless the host can keep those buttons away from the
/// game. See <see cref="IGameInputSuppressor"/> for why that is a refusal and not a
/// warning.</para>
/// </summary>
public sealed class ControllerNavigationMenu
{
    /// <summary>
    /// Everything the menu takes over while it is open: the D-pad, the face buttons
    /// and the bumpers, plus the stick click that opens it.
    /// </summary>
    public const GamepadButton OwnedButtons =
        GamepadButton.DPadUp | GamepadButton.DPadDown |
        GamepadButton.DPadLeft | GamepadButton.DPadRight |
        GamepadButton.A | GamepadButton.B | GamepadButton.X | GamepadButton.Y |
        GamepadButton.LeftShoulder | GamepadButton.RightShoulder |
        GamepadButton.RightThumb;

    private const GamepadButton RepeatingButtons =
        GamepadButton.DPadUp | GamepadButton.DPadDown;

    private readonly IGamepadReader reader;
    private readonly IGameInputSuppressor suppressor;
    private readonly GamepadButtonEdgeTracker edges;

    // Written only by the thread that observes - the game's, inside its input
    // getter - and read by the worker to decide whether to hold the party still.
    // Volatile so that read is never a stale cached one.
    private volatile bool isOpen;

    // What is still being kept from the game after the menu closed, until it is
    // let go. The press that closes the menu is still down at that moment: handing
    // the pad back there is how choosing a destination also confirms the dialogue
    // underneath it.
    private GamepadButton draining;

    private string lastRefusal = string.Empty;
    private int closeRequested;

    public ControllerNavigationMenu(
        IGamepadReader reader,
        IGameInputSuppressor suppressor,
        GamepadButtonEdgeTracker? edges = null)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.suppressor = suppressor ?? throw new ArgumentNullException(nameof(suppressor));
        this.edges = edges ?? new GamepadButtonEdgeTracker();
    }

    public bool IsOpen => isOpen;

    /// <summary>Whether the pad is being kept from the game at all right now.</summary>
    public bool IsSuppressing => isOpen || draining != GamepadButton.None;

    /// <summary>The last reason the menu would not open, for diagnostics.</summary>
    public string LastRefusal => lastRefusal;

    /// <summary>
    /// Polls the reader and decides. Used where there is no capture hook to be
    /// driven from - tests, and hosts whose pad is read on our own schedule.
    /// </summary>
    public ControllerNavigationMenuResult Observe(ControllerNavigationContext context, DateTime nowUtc) =>
        Observe(reader.Poll(), context, nowUtc);

    /// <summary>
    /// Decides from a snapshot somebody else has already taken.
    ///
    /// <para>This is the one the capture hook calls, on the game's own thread, inside
    /// the getter the game is asking. It has to be cheap and it must not block: no
    /// speech, no waiting on the worker, no allocation beyond the result. What it
    /// returns is applied to the very same read the game is about to receive, which
    /// is the only way the button that opens the menu or chooses a destination does
    /// not also reach the game.</para>
    /// </summary>
    public ControllerNavigationMenuResult Observe(
        GamepadSnapshot snapshot,
        ControllerNavigationContext context,
        DateTime nowUtc)
    {
        // A close asked for by the worker - a reset, an unload, the module changing
        // underneath - is honoured here rather than by the worker reaching into this
        // state, so the hook thread stays the only thread that touches it and there
        // is no lock on the game's own path.
        if (Interlocked.Exchange(ref closeRequested, 0) != 0 && isOpen)
        {
            // Also not the player finishing anything, so the pad goes straight back.
            CloseLocked(snapshot, drainHeldButtons: false);
        }

        // Every way of losing the right to own the pad. A disconnect and a focus
        // change are the player leaving; a battle, a minigame, a dialogue and a
        // native menu are the game needing its own controls back.
        var mayOwn = snapshot.IsConnected
            && context.IsHostForeground
            && context.ModuleSupportsNavigation
            && !context.GameIsBusy;

        if (!mayOwn)
        {
            var wasOpen = isOpen;
            _ = edges.Observe(snapshot, mayEmit: false, nowUtc);
            CloseLocked(snapshot, drainHeldButtons: false);
            return new ControllerNavigationMenuResult(
                wasOpen ? ControllerNavigationCommand.Closed : ControllerNavigationCommand.None,
                MenuIsOpen: false,
                DrainTick(snapshot),
                wasOpen ? "Navigation menu closed." : null);
        }

        var pressed = edges.Observe(snapshot, mayEmit: true, nowUtc, RepeatingButtons, out var repeated);

        if (!isOpen)
        {
            return ClosedTick(pressed, context, snapshot);
        }

        // Holding the buttons is re-asked every tick, so suppression that lapses -
        // a hook that came out, a device that went away - closes the menu instead of
        // leaving it reading a pad the game can also see.
        if (!suppressor.TryHold(OwnedButtons, out var diagnostic))
        {
            lastRefusal = diagnostic;
            CloseLocked(snapshot, drainHeldButtons: false);
            return new ControllerNavigationMenuResult(
                ControllerNavigationCommand.Closed,
                MenuIsOpen: false,
                DrainTick(snapshot),
                "Navigation menu closed: the game's controls could not be held.");
        }

        return OpenTick(pressed, repeated, context, snapshot);
    }

    /// <summary>
    /// Asks for the menu to close - a reset, an unload, the module changing under the
    /// adapter. Safe to call from the worker: it only raises a flag, and the close
    /// itself happens on the next observation, on the thread that owns this state.
    /// The drain still applies, so the game does not receive whatever was held.
    /// </summary>
    public void RequestClose() => Interlocked.Exchange(ref closeRequested, 1);

    /// <summary>
    /// Closes immediately. Only safe where no capture hook is observing concurrently -
    /// teardown, and tests.
    /// </summary>
    public void Close()
    {
        Interlocked.Exchange(ref closeRequested, 0);
        CloseLocked(GamepadSnapshot.Disconnected);
        edges.Reset();
        suppressor.ReleaseAll();
        draining = GamepadButton.None;
    }

    private ControllerNavigationMenuResult ClosedTick(
        GamepadButton pressed,
        ControllerNavigationContext context,
        GamepadSnapshot snapshot)
    {
        var suppressed = DrainTick(snapshot);
        if ((pressed & GamepadButton.RightThumb) != GamepadButton.RightThumb)
        {
            return new ControllerNavigationMenuResult(
                ControllerNavigationCommand.None, MenuIsOpen: false, suppressed, null);
        }

        // A route already running is not a reason to do something different here.
        // That is the player's own decision: the middle of a walk is exactly when
        // you want to check where you are going or choose somewhere else, and a
        // stick click that sometimes browses and sometimes cancels is one you
        // cannot press with any confidence. Stopping is B, or R3 again from inside.
        var diagnostic = "input suppression is not available on this host";
        if (!suppressor.IsAvailable || !suppressor.TryHold(OwnedButtons, out diagnostic))
        {
            lastRefusal = string.IsNullOrWhiteSpace(diagnostic)
                ? "input suppression is not available on this host"
                : diagnostic;
            return new ControllerNavigationMenuResult(
                ControllerNavigationCommand.None,
                MenuIsOpen: false,
                suppressed,
                "The navigation menu is not available: this copy of the game cannot hold the controller.");
        }

        isOpen = true;

        // The click that opened the menu is still down. Nothing it does next counts
        // until it has been released, so one long press cannot open the menu and
        // then act inside it.
        edges.BlockHeld();
        draining = GamepadButton.None;
        return new ControllerNavigationMenuResult(
            ControllerNavigationCommand.Opened, MenuIsOpen: true, OwnedButtons, null);
    }

    private ControllerNavigationMenuResult OpenTick(
        GamepadButton pressed,
        GamepadButton repeated,
        ControllerNavigationContext context,
        GamepadSnapshot snapshot)
    {
        // A deliberate press outranks an auto-repeat of something still held. Without
        // this, holding a direction while pressing a bumper silently ate the bumper:
        // the repeat won the one command this poll allows, and the bumper edge was
        // already consumed, so the category never changed.
        var deliberate = pressed & ~repeated;
        if (deliberate != GamepadButton.None)
        {
            pressed = deliberate;
        }

        // One button, one command. The order is the order of consequence: stopping
        // beats starting, and starting beats moving the selection, so a fumbled
        // simultaneous press does the safer thing.
        if (Has(pressed, GamepadButton.RightThumb) || Has(pressed, GamepadButton.B))
        {
            CloseLocked(snapshot);
            return new ControllerNavigationMenuResult(
                ControllerNavigationCommand.StopNavigation,
                MenuIsOpen: false,
                DrainTick(snapshot),
                null);
        }

        if (Has(pressed, GamepadButton.A))
        {
            CloseLocked(snapshot);
            return new ControllerNavigationMenuResult(
                ControllerNavigationCommand.StartNavigation,
                MenuIsOpen: false,
                DrainTick(snapshot),
                null);
        }

        if (Has(pressed, GamepadButton.X))
        {
            CloseLocked(snapshot);
            return new ControllerNavigationMenuResult(
                ControllerNavigationCommand.StartAutoWalk,
                MenuIsOpen: false,
                DrainTick(snapshot),
                null);
        }

        var command = ControllerNavigationCommand.None;
        if (Has(pressed, GamepadButton.DPadUp))
        {
            command = ControllerNavigationCommand.PreviousTarget;
        }
        else if (Has(pressed, GamepadButton.DPadDown))
        {
            command = ControllerNavigationCommand.NextTarget;
        }
        else if (Has(pressed, GamepadButton.LeftShoulder))
        {
            command = ControllerNavigationCommand.PreviousCategory;
        }
        else if (Has(pressed, GamepadButton.RightShoulder))
        {
            command = ControllerNavigationCommand.NextCategory;
        }

        return new ControllerNavigationMenuResult(command, MenuIsOpen: true, OwnedButtons, null);
    }

    /// <summary>
    /// Keeps holding whatever was held when the menu closed, until it is released.
    /// Once nothing is left, the game gets its controls back.
    /// </summary>
    private GamepadButton DrainTick(GamepadSnapshot snapshot)
    {
        if (draining == GamepadButton.None)
        {
            return GamepadButton.None;
        }

        draining &= snapshot.IsConnected ? snapshot.Buttons : GamepadButton.None;
        if (draining == GamepadButton.None)
        {
            suppressor.ReleaseAll();
            return GamepadButton.None;
        }

        // A lapse here is not worth closing anything over - the menu is already
        // closed - but the game will see the tail, so it is worth recording.
        if (!suppressor.TryHold(draining, out var diagnostic))
        {
            lastRefusal = diagnostic;
            suppressor.ReleaseAll();
            draining = GamepadButton.None;
            return GamepadButton.None;
        }

        return draining;
    }

    /// <param name="drainHeldButtons">
    /// Whether the buttons still down must be kept from the game until released.
    ///
    /// <para>True when a press closed the menu: the A that chose the destination is
    /// still down, and letting it go now is how choosing also confirms the dialogue
    /// underneath.</para>
    ///
    /// <para>False when something else closed it - the window went behind, the
    /// player walked into a battle, the pad was unplugged. There the menu is not
    /// finishing anything, and the player needs those buttons back <em>now</em>;
    /// holding a direction away from a game they have just returned to would look
    /// exactly like the mod having broken their controller.</para>
    /// </summary>
    private void CloseLocked(GamepadSnapshot snapshot, bool drainHeldButtons = true)
    {
        if (isOpen)
        {
            // Only a close of an open menu decides a tail. A tail an intentional
            // close has already established therefore survives every involuntary
            // close that follows - the menu is shut by then, so nothing here runs.
            // That matters: the player pressed A to choose somewhere and the module
            // changed a frame later, and the A they are still holding must not reach
            // the game just because the context moved on.
            draining = drainHeldButtons
                ? (snapshot.IsConnected ? snapshot.Buttons : GamepadButton.None) & OwnedButtons
                : GamepadButton.None;
            edges.BlockHeld();
        }

        isOpen = false;
        if (draining == GamepadButton.None)
        {
            suppressor.ReleaseAll();
        }
    }

    private static bool Has(GamepadButton set, GamepadButton button) => (set & button) == button;
}
