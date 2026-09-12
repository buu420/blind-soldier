using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The controller navigation menu: the mapping the player asked for, and the rules
/// about who owns the pad.
///
/// <para>Two failures matter more than the mapping itself. One is the menu acting on
/// a button the player did not press on purpose - held through a reconnect, through
/// a focus change, or through the press that opened the menu. The other is the game
/// seeing a button the menu was using, which is how choosing a destination also
/// confirms the dialogue underneath it.</para>
/// </summary>
internal static class ControllerNavigationMenuTests
{
    private static readonly DateTime Start = new(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ControllerNavigationContext Walking =
        new(IsHostForeground: true, ModuleSupportsNavigation: true, GameIsBusy: false);

    public static void Run()
    {
        TheStickClickOpensAndClosesTheMenu();
        TheMappingIsTheOneTheUserAsked();
        StartingIsExplicitAndNeverTurnsARouteOff();
        TheStickClickAlwaysOpensTheMenuEvenMidRoute();
        AButtonHeldThroughOpeningCannotAlsoActInside();
        TheGameNeverSeesTheButtonThatClosedTheMenu();
        TheMenuWillNotOpenWithoutInputSuppression();
        LosingTheGameOrThePadClosesTheMenuSafely();
        HoldingADirectionRepeatsAtABoundedRate();
        ABumperPressIsNotEatenByAHeldDirectionRepeat();
    }

    private static void TheStickClickOpensAndClosesTheMenu()
    {
        var pad = new FakePad();
        var suppressor = new FakeSuppressor();
        var menu = new ControllerNavigationMenu(pad, suppressor);

        Equal(ControllerNavigationCommand.None, Tick(menu, pad, GamepadButton.None).Command,
            "nothing happens while nothing is pressed");
        Equal(false, suppressor.IsHolding, "and the game keeps its controls");

        var opened = Tick(menu, pad, GamepadButton.RightThumb);
        Equal(ControllerNavigationCommand.Opened, opened.Command, "R3 opens the menu");
        Equal(true, opened.MenuIsOpen, "and it is open");
        Equal(ControllerNavigationMenu.OwnedButtons, opened.SuppressedButtons,
            "the menu takes the D-pad, the face buttons and the bumpers");
        Equal(true, suppressor.IsHolding, "and the game stops seeing them");

        // Released, then clicked again.
        _ = Tick(menu, pad, GamepadButton.None);
        var closed = Tick(menu, pad, GamepadButton.RightThumb);
        Equal(ControllerNavigationCommand.StopNavigation, closed.Command,
            "clicking again closes it and stops navigation");
        Equal(false, closed.MenuIsOpen, "the menu is closed");
    }

    private static void TheMappingIsTheOneTheUserAsked()
    {
        var cases = new (GamepadButton Button, ControllerNavigationCommand Expected, string What)[]
        {
            (GamepadButton.DPadUp, ControllerNavigationCommand.PreviousTarget, "D-pad up cycles back through targets"),
            (GamepadButton.DPadDown, ControllerNavigationCommand.NextTarget, "D-pad down cycles forward"),
            (GamepadButton.LeftShoulder, ControllerNavigationCommand.PreviousCategory, "LB is the previous category"),
            (GamepadButton.RightShoulder, ControllerNavigationCommand.NextCategory, "RB is the next"),
            (GamepadButton.A, ControllerNavigationCommand.StartNavigation, "A starts spoken navigation"),
            (GamepadButton.X, ControllerNavigationCommand.StartAutoWalk, "X starts auto walk"),
            (GamepadButton.B, ControllerNavigationCommand.StopNavigation, "B stops and closes"),
        };

        foreach (var (button, expected, what) in cases)
        {
            var pad = new FakePad();
            var menu = new ControllerNavigationMenu(pad, new FakeSuppressor());
            Open(menu, pad);

            var result = Tick(menu, pad, button);
            Equal(expected, result.Command, what);

            // Only starting and stopping close the menu. Cycling keeps it open so
            // the player can keep looking.
            var closes = expected is ControllerNavigationCommand.StartNavigation
                or ControllerNavigationCommand.StartAutoWalk
                or ControllerNavigationCommand.StopNavigation;
            Equal(!closes, result.MenuIsOpen, $"{what}: the menu stays open only while browsing");
        }
    }

    private static void StartingIsExplicitAndNeverTurnsARouteOff()
    {
        // The keyboard's I is a toggle, and a toggle on a controller is how somebody
        // ends up cancelling the route they just asked for by pressing A twice.
        var pad = new FakePad();
        var menu = new ControllerNavigationMenu(pad, new FakeSuppressor());
        Open(menu, pad);
        Equal(ControllerNavigationCommand.StartNavigation, Tick(menu, pad, GamepadButton.A).Command,
            "A starts the route");

        // Open again and press A again on the same selection.
        _ = Tick(menu, pad, GamepadButton.None);
        Open(menu, pad);
        Equal(ControllerNavigationCommand.StartNavigation, Tick(menu, pad, GamepadButton.A).Command,
            "a second A on the same target is another start, never a cancel");

        // Only these two ever stop anything.
        _ = Tick(menu, pad, GamepadButton.None);
        Open(menu, pad);
        Equal(ControllerNavigationCommand.StopNavigation, Tick(menu, pad, GamepadButton.B).Command,
            "B stops");
        _ = Tick(menu, pad, GamepadButton.None);
        Open(menu, pad);
        Equal(ControllerNavigationCommand.StopNavigation, Tick(menu, pad, GamepadButton.RightThumb).Command,
            "and so does a second R3 from inside the menu");
    }

    /// <summary>
    /// The user's own correction, and the reason it matters: the middle of a walk is
    /// exactly when you want to see where you are going or pick somewhere else. A
    /// stick click that browses when idle and cancels when walking is one nobody can
    /// press with confidence.
    /// </summary>
    private static void TheStickClickAlwaysOpensTheMenuEvenMidRoute()
    {
        var pad = new FakePad();
        var suppressor = new FakeSuppressor();
        var menu = new ControllerNavigationMenu(pad, suppressor);

        // Start a route, so something really is running.
        Open(menu, pad);
        Equal(ControllerNavigationCommand.StartNavigation, Tick(menu, pad, GamepadButton.A).Command,
            "a route is started");
        _ = Tick(menu, pad, GamepadButton.None);

        var reopened = Tick(menu, pad, GamepadButton.RightThumb);
        Equal(ControllerNavigationCommand.Opened, reopened.Command,
            "R3 mid-route opens the menu rather than cancelling");
        Equal(true, reopened.MenuIsOpen, "and it is open to browse in");

        // Browsing does not disturb the route: no start and no stop comes out of it.
        _ = Tick(menu, pad, GamepadButton.None);
        Equal(ControllerNavigationCommand.NextTarget, Tick(menu, pad, GamepadButton.DPadDown).Command,
            "and the player can look through the targets");
        _ = Tick(menu, pad, GamepadButton.None);
        Equal(ControllerNavigationCommand.PreviousCategory,
            Tick(menu, pad, GamepadButton.LeftShoulder).Command,
            "and the categories");

        // Choosing a different one replaces the route. Stopping is still B or R3.
        _ = Tick(menu, pad, GamepadButton.None);
        Equal(ControllerNavigationCommand.StartNavigation, Tick(menu, pad, GamepadButton.A).Command,
            "A on a different target replaces the route");
    }

    private static void AButtonHeldThroughOpeningCannotAlsoActInside()
    {
        // One long stick click is one command. Without this the click that opens the
        // menu is still down on the next tick and closes it again.
        var pad = new FakePad();
        var menu = new ControllerNavigationMenu(pad, new FakeSuppressor());
        Equal(ControllerNavigationCommand.Opened, Tick(menu, pad, GamepadButton.RightThumb).Command,
            "R3 opens it");
        Equal(ControllerNavigationCommand.None, Tick(menu, pad, GamepadButton.RightThumb).Command,
            "and holding it does nothing more");
        Equal(true, menu.IsOpen, "the menu is still open");

        // Released and pressed again is a new command.
        _ = Tick(menu, pad, GamepadButton.None);
        Equal(ControllerNavigationCommand.StopNavigation, Tick(menu, pad, GamepadButton.RightThumb).Command,
            "releasing and clicking again does close it");
    }

    private static void TheGameNeverSeesTheButtonThatClosedTheMenu()
    {
        // The case this exists for: A chooses a destination and the menu closes, but
        // A is still physically down. Handing the pad back now is how the same press
        // also confirms the dialogue on screen or opens the game's own menu.
        var pad = new FakePad();
        var suppressor = new FakeSuppressor();
        var menu = new ControllerNavigationMenu(pad, suppressor);
        Open(menu, pad);

        var started = Tick(menu, pad, GamepadButton.A);
        Equal(ControllerNavigationCommand.StartNavigation, started.Command, "A starts the route");
        Equal(false, started.MenuIsOpen, "and closes the menu so the game's controls come back");
        Equal(GamepadButton.A, started.SuppressedButtons, "but A itself is still held away from the game");
        Equal(true, suppressor.IsHolding, "so the suppressor is still holding");

        // Still down a frame later: still held away.
        Equal(GamepadButton.A, Tick(menu, pad, GamepadButton.A).SuppressedButtons,
            "and stays held while the player is still pressing it");

        // Released, and only now does the game get everything back.
        Equal(GamepadButton.None, Tick(menu, pad, GamepadButton.None).SuppressedButtons,
            "letting go hands the pad back");
        Equal(false, suppressor.IsHolding, "and nothing is held any more");
        Equal(GamepadButton.None, suppressor.LastHeld, "the suppressor was released, not left holding");
    }

    private static void TheMenuWillNotOpenWithoutInputSuppression()
    {
        // Reading the pad while the game reads it too would give the player a menu
        // whose every press also does something in the game. That is worse than no
        // menu, so it refuses and says so.
        var pad = new FakePad();
        var menu = new ControllerNavigationMenu(pad, UnavailableGameInputSuppressor.Instance);

        var refused = Tick(menu, pad, GamepadButton.RightThumb);
        Equal(ControllerNavigationCommand.None, refused.Command, "the menu does not open");
        Equal(false, refused.MenuIsOpen, "and is not open");
        Equal(GamepadButton.None, refused.SuppressedButtons, "nothing is taken from the game");
        Equal(true, refused.Announcement is not null, "and the player is told why");

        // Suppression that lapses while the menu is open closes it rather than
        // leaving it reading a pad the game can see as well.
        var lapsing = new FakeSuppressor();
        var open = new ControllerNavigationMenu(pad, lapsing);
        Open(open, pad);
        lapsing.Fail = true;
        var lost = Tick(open, pad, GamepadButton.None);
        Equal(ControllerNavigationCommand.Closed, lost.Command, "losing the hold closes the menu");
        Equal(true, lost.Announcement is not null, "and says so");
    }

    private static void LosingTheGameOrThePadClosesTheMenuSafely()
    {
        var cases = new (string What, ControllerNavigationContext Context, bool PadConnected)[]
        {
            ("the window lost the foreground", Walking with { IsHostForeground = false }, true),
            ("the player left the field", Walking with { ModuleSupportsNavigation = false }, true),
            ("the game opened its own window", Walking with { GameIsBusy = true }, true),
            ("the controller disconnected", Walking, false),
        };

        foreach (var (what, context, connected) in cases)
        {
            var pad = new FakePad();
            var suppressor = new FakeSuppressor();
            var menu = new ControllerNavigationMenu(pad, suppressor);
            Open(menu, pad);

            pad.IsConnected = connected;
            var result = Tick(menu, pad, GamepadButton.None, context);
            Equal(ControllerNavigationCommand.Closed, result.Command, $"{what}: the menu closes");
            Equal(false, menu.IsOpen, $"{what}: and stays closed");
            Equal(false, suppressor.IsHolding, $"{what}: the game gets its controls back");

            // The hazard: the player is holding the stick click through the gap - on
            // a loading screen, in a battle, while the window is behind something
            // else - and the mod comes back to find it down. That is not a press.
            // Acting on it would open the menu, or worse start a route, at the
            // moment the player got control back.
            _ = Tick(menu, pad, GamepadButton.RightThumb, context);
            pad.IsConnected = true;
            Equal(ControllerNavigationCommand.None,
                Tick(menu, pad, GamepadButton.RightThumb, Walking).Command,
                $"{what}: a button held across the gap is not a press");
            Equal(ControllerNavigationCommand.None, Tick(menu, pad, GamepadButton.None).Command,
                $"{what}: releasing it is not one either");
            Equal(ControllerNavigationCommand.Opened,
                Tick(menu, pad, GamepadButton.RightThumb).Command,
                $"{what}: and a genuine press afterwards still works");
        }
    }

    private static void HoldingADirectionRepeatsAtABoundedRate()
    {
        // A list is no use if the player has to click once per entry, and useless
        // if holding down runs through it at the polling rate.
        var pad = new FakePad();
        var menu = new ControllerNavigationMenu(
            pad,
            new FakeSuppressor(),
            new GamepadButtonEdgeTracker(
                TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(180)));
        Open(menu, pad);

        var now = Start.AddSeconds(1);
        Equal(ControllerNavigationCommand.NextTarget,
            Tick(menu, pad, GamepadButton.DPadDown, Walking, now).Command,
            "the first press moves one target");

        // Still held, but not for long enough yet.
        Equal(ControllerNavigationCommand.None,
            Tick(menu, pad, GamepadButton.DPadDown, Walking, now.AddMilliseconds(300)).Command,
            "holding it does not immediately run away");

        Equal(ControllerNavigationCommand.NextTarget,
            Tick(menu, pad, GamepadButton.DPadDown, Walking, now.AddMilliseconds(420)).Command,
            "after the delay it repeats");
        Equal(ControllerNavigationCommand.None,
            Tick(menu, pad, GamepadButton.DPadDown, Walking, now.AddMilliseconds(500)).Command,
            "but not on every poll");
        Equal(ControllerNavigationCommand.NextTarget,
            Tick(menu, pad, GamepadButton.DPadDown, Walking, now.AddMilliseconds(620)).Command,
            "one repeat per interval");

        // Two seconds of holding must not have produced dozens of moves.
        var moves = 0;
        for (var ms = 700; ms <= 2000; ms += 16)
        {
            if (Tick(menu, pad, GamepadButton.DPadDown, Walking, now.AddMilliseconds(ms)).Command
                == ControllerNavigationCommand.NextTarget)
            {
                moves++;
            }
        }

        Equal(true, moves is >= 5 and <= 9,
            $"1.3 seconds of holding is a handful of moves, not one per poll (was {moves})");

        // The bumpers do not repeat: a category list is short and stepping past it
        // is worse than pressing again.
        _ = Tick(menu, pad, GamepadButton.None, Walking, now.AddSeconds(3));
        Equal(ControllerNavigationCommand.NextCategory,
            Tick(menu, pad, GamepadButton.RightShoulder, Walking, now.AddSeconds(3)).Command,
            "RB moves one category");
        Equal(ControllerNavigationCommand.None,
            Tick(menu, pad, GamepadButton.RightShoulder, Walking, now.AddSeconds(5)).Command,
            "and holding it does nothing more");
    }

    /// <summary>
    /// The other half of the player's report: sometimes a bumper press does nothing.
    ///
    /// <para>One poll yields one command. A direction held down produces a repeat on
    /// the same poll as a deliberate bumper press, and the repeat used to win - but the
    /// bumper's edge had already been consumed, so the category change was gone for
    /// good. A deliberate press now outranks a repeat of something already held.</para>
    /// </summary>
    private static void ABumperPressIsNotEatenByAHeldDirectionRepeat()
    {
        var pad = new FakePad();
        var menu = new ControllerNavigationMenu(pad, new FakeSuppressor());
        Open(menu, pad);

        var now = Start.AddSeconds(1);
        Equal(ControllerNavigationCommand.NextTarget,
            Tick(menu, pad, GamepadButton.DPadDown, Walking, now).Command,
            "the direction moves one target");

        // Held long enough to be repeating, and the player reaches for a bumper on the
        // very poll a repeat is due.
        now = now + GamepadButtonEdgeTracker.RepeatDelay + TimeSpan.FromMilliseconds(20);
        Equal(ControllerNavigationCommand.NextCategory,
            Tick(menu, pad, GamepadButton.DPadDown | GamepadButton.RightShoulder, Walking, now).Command,
            "the deliberate bumper press wins over the direction's repeat");

        // And it is not merely deferred onto the next poll either - the direction
        // repeat resumes, which is what the player still has held.
        now += TimeSpan.FromMilliseconds(GamepadButtonEdgeTracker.RepeatInterval.TotalMilliseconds + 20);
        Equal(ControllerNavigationCommand.NextTarget,
            Tick(menu, pad, GamepadButton.DPadDown | GamepadButton.RightShoulder, Walking, now).Command,
            "and the held direction carries on repeating afterwards");

        // The bumper does not repeat: still held, nothing more from it.
        var categories = 0;
        for (var step = 0; step < 40; step++)
        {
            now += TimeSpan.FromMilliseconds(16);
            if (Tick(menu, pad, GamepadButton.RightShoulder, Walking, now).Command
                == ControllerNavigationCommand.NextCategory)
            {
                categories++;
            }
        }

        Equal(0, categories, "a bumper held on its own never repeats");
    }

    private static void Open(ControllerNavigationMenu menu, FakePad pad)
    {
        Equal(ControllerNavigationCommand.Opened, Tick(menu, pad, GamepadButton.RightThumb).Command,
            "the fixture opens the menu");
        Equal(ControllerNavigationCommand.None, Tick(menu, pad, GamepadButton.None).Command,
            "and lets the stick go");
    }

    private static ControllerNavigationMenuResult Tick(
        ControllerNavigationMenu menu,
        FakePad pad,
        GamepadButton buttons,
        ControllerNavigationContext? context = null,
        DateTime? nowUtc = null)
    {
        pad.Buttons = buttons;
        return menu.Observe(context ?? Walking, nowUtc ?? Start);
    }

    private sealed class FakePad : IGamepadReader
    {
        public bool IsConnected { get; set; } = true;

        public GamepadButton Buttons { get; set; }

        public int ActiveUserIndex => IsConnected ? 0 : -1;

        public GamepadSnapshot Poll() => IsConnected
            ? new GamepadSnapshot(true, 0, 0, Buttons)
            : GamepadSnapshot.Disconnected;
    }

    private sealed class FakeSuppressor : IGameInputSuppressor
    {
        public bool Fail { get; set; }

        public bool IsAvailable => true;

        public bool IsHolding { get; private set; }

        public GamepadButton LastHeld { get; private set; }

        public bool TryHold(GamepadButton buttons, out string diagnostic)
        {
            diagnostic = string.Empty;
            if (Fail)
            {
                diagnostic = "the fake suppressor was told to fail";
                return false;
            }

            IsHolding = true;
            LastHeld = buttons;
            return true;
        }

        public void ReleaseAll()
        {
            IsHolding = false;
            LastHeld = GamepadButton.None;
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Controller navigation menu: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
