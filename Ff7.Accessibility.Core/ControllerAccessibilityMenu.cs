namespace Ff7.Accessibility.Core;

/// <summary>
/// Cyber Sleuth-style held-trigger commands. A persistent settings page is the
/// only latched page. R3 by itself is never an accessibility command.
/// </summary>
public sealed class ControllerAccessibilityMenu : IControllerNavigationMenu
{
    public const GamepadButton Modifiers = GamepadButton.LeftTrigger | GamepadButton.RightTrigger;
    public const GamepadButton OwnedButtons = Modifiers |
        GamepadButton.DPadUp | GamepadButton.DPadDown | GamepadButton.DPadLeft | GamepadButton.DPadRight |
        GamepadButton.A | GamepadButton.B | GamepadButton.X | GamepadButton.Y |
        GamepadButton.LeftThumb | GamepadButton.RightThumb |
        GamepadButton.LeftShoulder | GamepadButton.RightShoulder |
        GamepadButton.Start | GamepadButton.Back;

    private readonly IGameInputSuppressor suppressor;
    private readonly GamepadButtonEdgeTracker edges = new();
    private volatile Mode mode;
    private GamepadButton draining;
    private GamepadButton reservedStick;
    private GamepadButton lastDown;
    private GamepadButton withheld;
    private int closeRequested;
    private bool waitForModifierRelease;
    private bool nativeInputExclusive;
    private volatile bool suppressing;

    public ControllerAccessibilityMenu(IGameInputSuppressor suppressor) =>
        this.suppressor = suppressor ?? throw new ArgumentNullException(nameof(suppressor));

    public bool IsOpen => mode != Mode.None;
    public bool IsSuppressing => suppressing;
    public bool UsesModifierControls => true;
    public bool SettingsIsOpen => mode == Mode.Settings;
    public string LastRefusal { get; private set; } = string.Empty;

    public void RequestClose() => Interlocked.Exchange(ref closeRequested, 1);

    public void SetSettingsOpen(bool open)
    {
        if (open) mode = Mode.Settings;
        else Finish(lastDown);
        edges.BlockHeld();
    }

    public void Close()
    {
        mode = Mode.None;
        draining = reservedStick = GamepadButton.None;
        lastDown = withheld = GamepadButton.None;
        waitForModifierRelease = false;
        nativeInputExclusive = false;
        suppressing = false;
        edges.Reset();
        suppressor.ReleaseAll();
    }

    public ControllerNavigationMenuResult Observe(GamepadSnapshot snapshot,
        ControllerNavigationContext context, DateTime nowUtc)
    {
        var down = snapshot.IsConnected ? snapshot.AccessibilityButtons : GamepadButton.None;
        var heldBefore = lastDown;
        lastDown = down;
        snapshot = snapshot with { Buttons = down };
        nativeInputExclusive = context.NativeInputIsExclusive;
        var modifierDown = (down & Modifiers) != 0;
        if (!modifierDown) waitForModifierRelease = false;
        draining &= down;
        // While the game reads the pad as an instrument, only a press it has never seen
        // stays private: one withheld on the last read and held ever since. Handing it over
        // mid-hold would play it as a fresh note or chord.
        if (nativeInputExclusive) draining |= down & heldBefore & withheld;
        reservedStick &= down;
        // A modified R3 sample must stay private even if the context is stale,
        // unavailable or changing. Battle Assist toggles on RELEASE.
        if (modifierDown && (down & GamepadButton.RightThumb) != 0)
            reservedStick |= GamepadButton.RightThumb;

        var allowed = context.IsHostForeground && snapshot.IsConnected;
        // Whatever is held while the game owns the pad has to be released and pressed
        // again before it means anything here.
        var pressed = edges.Observe(snapshot, allowed && !nativeInputExclusive, nowUtc,
            GamepadButton.DPadUp | GamepadButton.DPadDown | GamepadButton.DPadLeft | GamepadButton.DPadRight,
            out var repeated);
        var deliberate = pressed & ~repeated;
        if (deliberate != GamepadButton.None) pressed = deliberate;

        var requestedClose = Interlocked.Exchange(ref closeRequested, 0) != 0;
        if (!allowed || requestedClose)
        {
            var wasSettings = mode == Mode.Settings;
            var wasOpen = mode != Mode.None;
            Finish(down);
            return Result(wasSettings ? ControllerNavigationCommand.SettingsClose :
                wasOpen ? ControllerNavigationCommand.Closed : ControllerNavigationCommand.None, down);
        }

        if (nativeInputExclusive)
        {
            // A piano is reading these buttons as notes, chords and its own end. Browsing
            // and the party readout give way; the settings page stays open for the
            // keyboard but takes nothing from the pad.
            if (mode is Mode.Navigation or Mode.Party)
            {
                Finish(down);
                return Result(ControllerNavigationCommand.Closed, down);
            }

            return Result(ControllerNavigationCommand.None, down);
        }

        if (context.SupportsSettings && (down & Modifiers) == Modifiers && Has(pressed, GamepadButton.Y))
        {
            if (!suppressor.IsAvailable)
                return Refuse(down);
            var command = mode == Mode.Settings ? ControllerNavigationCommand.SettingsClose :
                ControllerNavigationCommand.SettingsOpen;
            if (mode == Mode.Settings) Finish(down);
            else { mode = Mode.Settings; edges.BlockHeld(); }
            return Result(command, down);
        }

        if (mode == Mode.Settings)
        {
            if (!context.SupportsSettings)
            {
                Finish(down);
                return Result(ControllerNavigationCommand.SettingsClose, down);
            }
            if (Has(pressed, GamepadButton.B))
            {
                Finish(down);
                return Result(ControllerNavigationCommand.SettingsClose, down);
            }
            return Result(MapSettings(pressed), down);
        }

        var wantedMode = (down & Modifiers) == Modifiers ? Mode.None :
            context.SupportsPartyReadout && Has(down, GamepadButton.LeftTrigger)
            ? Mode.Party
            : context.ModuleSupportsNavigation && !context.GameIsBusy && modifierDown
                ? Mode.Navigation : Mode.None;
        if (mode != Mode.None && mode != wantedMode)
        {
            Finish(down);
            return Result(ControllerNavigationCommand.Closed, down);
        }
        if (mode == Mode.None)
        {
            // A held trigger from a focus/reconnect gap is not a request. Also do
            // not reopen after a start/stop until the modifier has been released.
            if (wantedMode == Mode.None || waitForModifierRelease || draining != GamepadButton.None ||
                (pressed & Modifiers) == 0)
                return Result(ControllerNavigationCommand.None, down);
            if (!suppressor.IsAvailable) return Refuse(down);
            mode = wantedMode;
            edges.BlockHeld();
            return Result(mode == Mode.Navigation ? ControllerNavigationCommand.Opened :
                ControllerNavigationCommand.PartySummary, down);
        }

        if (mode == Mode.Party)
            return Result(MapParty(pressed), down);

        var action = MapNavigation(pressed);
        if (action is ControllerNavigationCommand.StartNavigation or
            ControllerNavigationCommand.StartAutoWalk or ControllerNavigationCommand.StopNavigation)
            Finish(down);
        return Result(action, down);
    }

    /// <summary>
    /// What one device's own read withholds before the shared policy adds its part: with
    /// a trigger held, every owned button, and afterwards whatever that chord still has
    /// down until it is let go. While the game reads the pad as an instrument a resting
    /// trigger takes nothing, and only a press that was already being kept back drains.
    /// </summary>
    public static GamepadButton ReserveModifiedChord(GamepadButton down, ref GamepadButton tail,
        bool nativeInputExclusive)
    {
        var modified = !nativeInputExclusive && (down & Modifiers) != 0;
        tail &= down;
        if (nativeInputExclusive) tail &= ~Modifiers;
        if (modified) tail |= down & OwnedButtons;
        return modified ? OwnedButtons : tail;
    }

    private void Finish(GamepadButton down)
    {
        if (mode != Mode.None)
        {
            // Under a lease the page has not been taking the pad, and what it last took
            // is already draining.
            if (!nativeInputExclusive) draining |= down & OwnedButtons;
            edges.BlockHeld();
            waitForModifierRelease = (down & Modifiers) != 0;
        }
        mode = Mode.None;
    }

    private ControllerNavigationMenuResult Refuse(GamepadButton down)
    {
        LastRefusal = "controller capture is unavailable";
        edges.BlockHeld();
        return Result(ControllerNavigationCommand.None, down,
            "Controller accessibility is unavailable: this copy of the game cannot hold the controller.");
    }

    private ControllerNavigationMenuResult Result(ControllerNavigationCommand command,
        GamepadButton down, string? announcement = null)
    {
        GamepadButton mask;
        if (nativeInputExclusive)
        {
            // Only a press already being kept back stays private until it is let go.
            // A resting trigger is the game's again, and so are the sticks: the input
            // hooks clear every stick axis whenever anything at all is withheld.
            draining &= ~Modifiers;
            mask = draining;
        }
        else
        {
            mask = (mode != Mode.None ? OwnedButtons : draining) | reservedStick;
            // Capture attempted modifier chords in invalid contexts too. Their face
            // buttons must not become a game command when navigation refuses to open.
            if ((down & Modifiers) != 0)
            {
                mask |= OwnedButtons;
                draining |= down & OwnedButtons;
            }
        }
        withheld = mask;
        suppressing = mask != GamepadButton.None;
        if (mask == GamepadButton.None) suppressor.ReleaseAll();
        else if (!suppressor.TryHold(mask, out var diagnostic)) LastRefusal = diagnostic;
        return new(command, IsOpen, mask, announcement);
    }

    private static ControllerNavigationCommand MapNavigation(GamepadButton pressed) =>
        Has(pressed, GamepadButton.B) ? ControllerNavigationCommand.StopNavigation :
        Has(pressed, GamepadButton.LeftThumb) ? ControllerNavigationCommand.StartNavigation :
        Has(pressed, GamepadButton.RightThumb) ? ControllerNavigationCommand.StartAutoWalk :
        Has(pressed, GamepadButton.DPadLeft) ? ControllerNavigationCommand.PreviousCategory :
        Has(pressed, GamepadButton.DPadRight) ? ControllerNavigationCommand.NextCategory :
        Has(pressed, GamepadButton.DPadUp) ? ControllerNavigationCommand.PreviousTarget :
        Has(pressed, GamepadButton.DPadDown) ? ControllerNavigationCommand.NextTarget :
        ControllerNavigationCommand.None;

    private static ControllerNavigationCommand MapParty(GamepadButton pressed) =>
        Has(pressed, GamepadButton.DPadLeft) ? ControllerNavigationCommand.PartyPrevious :
        Has(pressed, GamepadButton.DPadRight) ? ControllerNavigationCommand.PartyNext :
        Has(pressed, GamepadButton.DPadUp) ? ControllerNavigationCommand.PartySummary :
        Has(pressed, GamepadButton.DPadDown) ? ControllerNavigationCommand.PartyStatuses :
        Has(pressed, GamepadButton.Y) ? ControllerNavigationCommand.PartyLimit :
        ControllerNavigationCommand.None;

    private static ControllerNavigationCommand MapSettings(GamepadButton pressed) =>
        Has(pressed, GamepadButton.DPadUp) ? ControllerNavigationCommand.SettingsPrevious :
        Has(pressed, GamepadButton.DPadDown) ? ControllerNavigationCommand.SettingsNext :
        Has(pressed, GamepadButton.DPadLeft) ? ControllerNavigationCommand.SettingsDecrease :
        Has(pressed, GamepadButton.DPadRight) ? ControllerNavigationCommand.SettingsIncrease :
        Has(pressed, GamepadButton.A) ? ControllerNavigationCommand.SettingsActivate :
        ControllerNavigationCommand.None;

    private static bool Has(GamepadButton value, GamepadButton flag) => (value & flag) == flag;
    private enum Mode { None, Navigation, Party, Settings }
}
