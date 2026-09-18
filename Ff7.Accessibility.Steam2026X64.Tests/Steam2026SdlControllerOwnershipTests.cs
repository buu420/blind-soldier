using Ff7.Accessibility.Core;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// The reported fault: the navigation menu worked on an Xbox pad and did nothing at all
/// on a DualSense plugged in beside it.
///
/// <para>The capture latched the first controller pointer the game read and refused every
/// other one for the life of the process. On the reporting machine SDL enumerates the
/// Xbox pad at index 0 and the DualSense at index 1, so the Xbox pad took the latch
/// during startup - before the player had touched anything - and the pad actually in the
/// player's hands could never produce an edge. Its ordinary controls kept working,
/// because a device we do not own is handed straight back to the game, so the failure was
/// silent in the log and in speech alike.</para>
///
/// <para>These run on an injected getter and a held clock rather than real SDL, because
/// the fault is about which device is asked and when.
/// <c>Steam2026SdlControllerCaptureHookTests</c> proves the same code against the
/// installed SDL2 with two virtual pads.</para>
/// </summary>
internal static class Steam2026SdlControllerOwnershipTests
{
    private const int ButtonA = 0;
    private const int ButtonRightStick = 8;
    private const int ButtonDPadDown = 12;

    private static readonly DateTime Start = new(2026, 9, 17, 22, 38, 0, DateTimeKind.Utc);

    /// <summary>Index 0 on the reporting machine: the Xbox pad, latched during startup.</summary>
    private static readonly nint First = 0x1000;

    /// <summary>Index 1: the DualSense the player is actually holding.</summary>
    private static readonly nint Second = 0x2000;

    public static void Run()
    {
        ASecondControllerOpensTheMenuWithItsOwnStickClick();
        AForeignControllerCannotTakeAnOpenMenu();
        AStickClickHeldBeforeWeEverSawTheDeviceCannotOpenAnything();
        ATailHeldOnTheOldOwnerIsNotCutShortByAnotherPad();
        ADeviceThatGoesAwayIsForgottenAndCannotReturnHolding();
        ASinglePadStillLatchesOnItsFirstReadAndIsPolledEveryTime();
    }

    /// <summary>
    /// The player's report, reduced to its bones. Two pads are open and polled; the one
    /// the player is holding is not the one that was latched first.
    /// </summary>
    private static void ASecondControllerOpensTheMenuWithItsOwnStickClick()
    {
        var host = new FakeHost();

        // Startup. Both pads are open and read every frame, so the first one SDL
        // enumerates is latched before the player has pressed anything.
        host.Frame(First, Second);
        Equal(First, host.LatchedController,
            "the first pad the game reads is latched during startup");

        // The player clicks the right stick on the other pad.
        host.Down(Second, ButtonRightStick);
        host.Frame(First, Second);

        Equal(true, host.Capture.IsOpen,
            "R3 on the pad the player is actually holding opens the menu");
        Equal(Second, host.LatchedController,
            "and that pad becomes the one the menu listens to");
    }

    /// <summary>
    /// The other half of the same rule. Taking a menu somebody else has open would hand
    /// one player's list to another player's thumbs, so a second pad has to wait.
    /// </summary>
    private static void AForeignControllerCannotTakeAnOpenMenu()
    {
        var host = new FakeHost();
        host.Frame(First, Second);
        host.OpenOn(First);

        host.Down(Second, ButtonRightStick);
        host.Frame(First, Second);

        Equal(true, host.Capture.IsOpen, "the menu the first pad opened stays open");
        Equal(First, host.LatchedController, "and the second pad does not take it away");
        Equal((byte)1, host.Read(Second, ButtonRightStick),
            "a pad that does not own the menu keeps every button it presses");
    }

    /// <summary>
    /// A resting state, a stuck stick, or a thumb that happened to be down. The first
    /// thing we ever learn about a device cannot be a press: it has to be released and
    /// pressed again before it means anything.
    /// </summary>
    private static void AStickClickHeldBeforeWeEverSawTheDeviceCannotOpenAnything()
    {
        var host = new FakeHost();
        host.Frame(First);

        host.Down(Second, ButtonRightStick);
        host.Frame(First, Second);
        Equal(false, host.Capture.IsOpen,
            "a stick click we never saw go down cannot open the menu");
        Equal(First, host.LatchedController,
            "and it does not move the menu to that pad");

        host.Up(Second, ButtonRightStick);
        host.Frame(First, Second);
        host.Down(Second, ButtonRightStick);
        host.Frame(First, Second);
        Equal(true, host.Capture.IsOpen,
            "released and clicked afresh, the same button opens the menu");
        Equal(Second, host.LatchedController, "on the pad that clicked it");
    }

    /// <summary>
    /// The press that closed the menu is still down, and it is being kept from the game
    /// until it is let go - that is how choosing a destination does not also confirm the
    /// dialogue underneath. Handing the pad over mid-tail would release it into the game.
    /// </summary>
    private static void ATailHeldOnTheOldOwnerIsNotCutShortByAnotherPad()
    {
        var host = new FakeHost();
        host.Frame(First, Second);
        host.OpenOn(First);

        host.Down(First, ButtonA);
        host.Frame(First, Second);
        Equal(false, host.Capture.IsOpen, "choosing a destination closes the menu");
        Equal((byte)0, host.Read(First, ButtonA),
            "and the press that chose it is still kept from the game");

        // The other pad asks for the menu while that tail is still running.
        host.Down(Second, ButtonRightStick);
        host.Frame(First, Second);
        Equal(First, host.LatchedController,
            "the pad does not change hands while a press is still being held back");
        Equal((byte)0, host.Read(First, ButtonA), "and the tail is still held back");

        // Released. The tail ends, and only now may the other pad take over.
        host.Up(First, ButtonA);
        host.Frame(First, Second);
        host.Up(Second, ButtonRightStick);
        host.Frame(First, Second);
        host.Down(Second, ButtonRightStick);
        host.Frame(First, Second);
        Equal(true, host.Capture.IsOpen,
            "once nothing is held back, the other pad may open the menu");
        Equal(Second, host.LatchedController, "and becomes the pad the menu listens to");
    }

    /// <summary>
    /// A device that stops answering is gone, and everything we knew about it goes with
    /// it - otherwise the click it was holding when it left looks like a fresh press the
    /// moment it comes back.
    /// </summary>
    private static void ADeviceThatGoesAwayIsForgottenAndCannotReturnHolding()
    {
        var host = new FakeHost();
        host.Frame(First, Second);
        host.Down(Second, ButtonRightStick);
        host.Frame(First, Second);
        Equal(true, host.Capture.IsOpen, "the second pad opened the menu");
        Equal(Second, host.LatchedController, "and owns it");

        // Unplugged with the stick still clicked.
        host.Detach(Second);
        host.Frame(First, Second);
        Equal(false, host.Capture.IsOpen, "a pad that goes away takes the menu with it");
        Equal((nint)0, host.LatchedController, "and stops being the one we listen to");

        // Back, still clicked. That click was never seen going down on this device.
        host.Attach(Second);
        host.Frame(First, Second);
        Equal(false, host.Capture.IsOpen,
            "and a click that survived the unplug does not reopen the menu");
    }

    /// <summary>
    /// The single-pad case has to be exactly what it was. One device is still latched on
    /// its first read, and every read it makes still reaches the policy - which is the
    /// only thing that keeps the watchdog, the D-pad repeat and the worker's close
    /// request moving.
    /// </summary>
    private static void ASinglePadStillLatchesOnItsFirstReadAndIsPolledEveryTime()
    {
        var host = new FakeHost();
        host.Frame(First);
        Equal(First, host.LatchedController,
            "one pad is latched on its very first read, as before");

        var polls = host.Capture.ObservedPolls;
        host.Frame(First);
        Equal(true, host.Capture.ObservedPolls > polls,
            "and every read still reaches the policy, so its clock keeps moving");
    }

    private sealed class FakeHost
    {
        private readonly HashSet<(nint Controller, int Button)> down = [];
        private readonly HashSet<nint> detached = [];
        private readonly Steam2026SdlControllerCaptureHook hook;

        public FakeHost()
        {
            ControllerNavigationCapture? created = null;
            hook = Steam2026SdlControllerCaptureHook.CreateForDecideTest(
                isInstalled =>
                {
                    created = new ControllerNavigationCapture(
                        suppressor => new ControllerNavigationMenu(
                            EmptyGamepadReader.Instance, suppressor),
                        isInstalled);
                    return created;
                },
                (controller, button) => (byte)(down.Contains((controller, button)) ? 1 : 0),
                controller => detached.Contains(controller) ? 0 : 1,
                () => Now);
            Capture = created!;
            Publish();
        }

        public ControllerNavigationCapture Capture { get; }

        public DateTime Now { get; private set; } = Start;

        public nint LatchedController => hook.LatchedController;

        public void Down(nint controller, int sdlButton) => down.Add((controller, sdlButton));

        public void Up(nint controller, int sdlButton) => down.Remove((controller, sdlButton));

        public void Detach(nint controller) => detached.Add(controller);

        public void Attach(nint controller) => detached.Remove(controller);

        /// <summary>What the game receives for one button on one pad.</summary>
        public byte Read(nint controller, int sdlButton) =>
            hook.InvokeGetButtonForTest(controller, sdlButton);

        /// <summary>
        /// One 60 Hz frame. The game reads the buttons it is mapped to on every pad it
        /// has open - deliberately never R3, because the game does not ask about the
        /// stick click and the capture must not need it to.
        /// </summary>
        public void Frame(params nint[] controllers)
        {
            Now += TimeSpan.FromMilliseconds(16);
            Publish();
            foreach (var controller in controllers)
            {
                _ = Read(controller, ButtonA);
                _ = Read(controller, ButtonDPadDown);
            }
        }

        /// <summary>Opens the menu from a pad that already owns it.</summary>
        public void OpenOn(nint controller)
        {
            Down(controller, ButtonRightStick);
            Frame(First, Second);
            Up(controller, ButtonRightStick);
            Frame(First, Second);
            Equal(true, Capture.IsOpen, "the fixture opened the menu");
            while (Capture.Commands.TryDequeue(out _))
            {
            }
        }

        private void Publish() =>
            Capture.PublishContext(
                ControllerNavigationDomain.Field,
                isHostForeground: true,
                moduleSupportsNavigation: true,
                gameIsBusy: false,
                Now,
                identity: 907);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"SDL controller ownership: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
