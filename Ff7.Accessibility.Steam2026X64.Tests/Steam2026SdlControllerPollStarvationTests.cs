using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// The live fault: the menu opened and closed itself about a second later, every time.
///
/// <para><c>Decide</c> returned a cached strip mask whenever the button word matched the
/// last decision, and that path skipped <c>ObserveRawPoll</c> altogether. Everything the
/// policy measures in time therefore froze as soon as the player stopped moving a
/// button: the watchdog saw no poll and retired the menu, D-pad repeats never came due,
/// and a worker close request was never picked up. The log shows it exactly - the menu
/// survives only while target changes keep arriving, and dies once they stop.</para>
///
/// <para>A fake clock and an injected getter are needed because this is a timing fault.
/// <c>Steam2026SdlControllerCaptureHookTests</c> proves the same code against the real
/// installed SDL2, but it cannot hold time still.</para>
/// </summary>
internal static class Steam2026SdlControllerPollStarvationTests
{
    private const int ButtonA = 0;
    private const int ButtonRightStick = 8;
    private const int ButtonRightShoulder = 10;
    private const int ButtonDPadDown = 12;

    private static readonly DateTime Start = new(2026, 9, 11, 14, 50, 30, DateTimeKind.Utc);
    private static readonly nint Controller = 0x1234;

    public static void Run()
    {
        AnUnchangedPadKeepsTheMenuOpen();
        AControllerThatStopsAnsweringStillRetires();
        AHeldDirectionRepeatsWhileNothingElseChanges();
        AHeldStickClickAndBumperDoNotRepeatTheirActions();
        AContextChangeIsHonouredWithIdenticalPhysicalState();
    }

    /// <summary>
    /// The player's report. Open the menu, let go, and do nothing: the pad reports the
    /// same empty word every frame for two seconds, which is four times the watchdog's
    /// patience. The menu must stay open.
    /// </summary>
    private static void AnUnchangedPadKeepsTheMenuOpen()
    {
        var host = new FakeHost();
        host.Open();

        for (var frame = 0; frame < 120; frame++)
        {
            host.Frame();
            host.WorkerTick();
        }

        Equal(true, host.Capture.IsOpen,
            "two seconds of an unchanged pad leaves the menu open");
        Equal(0, host.Retirements,
            "and the watchdog never claims the controller stopped responding");
    }

    /// <summary>
    /// The watchdog still has to work. Here the game genuinely stops reading the pad -
    /// no <c>SDL_GameControllerGetButton</c> at all - which is what an unplugged device
    /// looks like from inside SDL.
    /// </summary>
    private static void AControllerThatStopsAnsweringStillRetires()
    {
        var host = new FakeHost();
        host.Open();

        // Worker frames only. Nothing reads the pad.
        for (var tick = 0; tick < 10; tick++)
        {
            host.Advance(TimeSpan.FromMilliseconds(100));
            host.WorkerTick();
        }

        Equal(false, host.Capture.IsOpen, "a pad nobody is reading retires the menu");
        Equal(1, host.Retirements, "and says so exactly once");
    }

    private static void AHeldDirectionRepeatsWhileNothingElseChanges()
    {
        var host = new FakeHost();
        host.Open();

        host.Down(ButtonDPadDown);
        for (var frame = 0; frame < 120; frame++)
        {
            host.Frame();
            host.WorkerTick();
        }

        var moves = host.CountApplied("NextTarget", "PreviousTarget");
        // One press plus repeats at the bounded rate: deliberate, not a flood, and not
        // the single press the frozen cache used to allow.
        Equal(true, moves is >= 4 and <= 9,
            $"two seconds of a held direction is a handful of moves (was {moves})");
        Equal(true, host.Capture.IsOpen, "and the menu is still open");
    }

    private static void AHeldStickClickAndBumperDoNotRepeatTheirActions()
    {
        foreach (var (button, name) in new[]
                 {
                     (ButtonRightShoulder, "a held bumper"),
                     (ButtonA, "a held A"),
                 })
        {
            var host = new FakeHost();
            host.Open();
            host.Down(button);

            for (var frame = 0; frame < 120; frame++)
            {
                host.Frame();
                host.WorkerTick();
            }

            var actions = host.CountApplied(
                "NextCategory", "PreviousCategory", "Start", "AutoWalk", "Stop");
            Equal(1, actions, $"{name} is one action, however long it is held");
        }

        // And a held R3 does not reopen what it just closed.
        var reopen = new FakeHost();
        reopen.Open();
        reopen.Down(ButtonRightStick);
        reopen.Frame();
        Equal(false, reopen.Capture.IsOpen, "a second R3 closes the menu");
        for (var frame = 0; frame < 60; frame++)
        {
            reopen.Frame();
            reopen.WorkerTick();
        }

        Equal(false, reopen.Capture.IsOpen, "and holding it does not reopen it");
    }

    /// <summary>
    /// The same physical state, but the worker says the player has left the field. That
    /// has to reach the menu even though no button moved - which the cached path could
    /// never do, because it never asked.
    /// </summary>
    private static void AContextChangeIsHonouredWithIdenticalPhysicalState()
    {
        var host = new FakeHost();
        host.Open();
        host.Frame();
        Equal(true, host.Capture.IsOpen, "the menu is open with nothing held");

        host.ModuleSupportsNavigation = false;
        host.Frame();
        Equal(false, host.Capture.IsOpen,
            "leaving the field closes the menu with no button having moved");

        // And suppression goes with it: the game gets its buttons back.
        host.Down(ButtonDPadDown);
        Equal((byte)1, host.Read(ButtonDPadDown),
            "the game receives the D-pad once the menu is not entitled to it");
    }

    private sealed class FakeHost
    {
        private readonly HashSet<int> down = [];
        private readonly Steam2026SdlControllerCaptureHook hook;
        private readonly ControllerNavigationDispatcher dispatcher;

        public FakeHost()
        {
            ControllerNavigationCapture? created = null;
            hook = Steam2026SdlControllerCaptureHook.CreateForDecideTest(
                isInstalled =>
                {
                    created = new ControllerNavigationCapture(
                        suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
                        isInstalled,
                        isForegroundNow: () => IsForeground);
                    return created;
                },
                (nint controller, int button) => (byte)(down.Contains(button) ? 1 : 0),
                _ => 1,
                () => Now);
            Capture = created!;

            dispatcher = new ControllerNavigationDispatcher(
                new DelegatedControllerNavigationTarget(
                    () => false,
                    () => false,
                    action => { Applied.Add(action.ToString()); return null; },
                    () => { Applied.Add("Start"); return null; },
                    () => { Applied.Add("AutoWalk"); return null; },
                    () => { Applied.Add("Stop"); return null; },
                    () => { }),
                speech =>
                {
                    if (speech.Contains("stopped responding", StringComparison.Ordinal))
                    {
                        Retirements++;
                    }

                    return true;
                });

            Publish();
        }

        public ControllerNavigationCapture Capture { get; }

        public DateTime Now { get; private set; } = Start;

        public bool IsForeground { get; set; } = true;

        public bool ModuleSupportsNavigation { get; set; } = true;

        public int Retirements { get; private set; }

        public void Advance(TimeSpan span)
        {
            Now += span;
            Publish();
        }

        public void Down(int sdlButton) => down.Add(sdlButton);

        public void Up(int sdlButton) => down.Remove(sdlButton);

        /// <summary>What the game receives for one button.</summary>
        public byte Read(int sdlButton) => hook.InvokeGetButtonForTest(Controller, sdlButton);

        /// <summary>
        /// One 60 Hz frame: the host advances, then reads the buttons it cares about.
        /// Deliberately not every button - the game reads what it is mapped to, and the
        /// capture must not depend on being asked about R3 or the bumpers.
        /// </summary>
        public void Frame()
        {
            Advance(TimeSpan.FromMilliseconds(16));
            _ = Read(ButtonA);
            _ = Read(ButtonDPadDown);
        }

        public void WorkerTick() =>
            dispatcher.Drain(Capture, ControllerNavigationDomain.Field, Now);

        /// <summary>What the dispatcher has applied to the navigation services.</summary>
        public List<string> Applied { get; } = [];

        public int CountApplied(params string[] wanted) =>
            Applied.Count(entry => wanted.Contains(entry));

        public void Open()
        {
            // The device is latched on its first read and deliberately rearmed neutral,
            // so whatever is held at that moment must be released first. In the game the
            // latch happens long before the player reaches for R3; here it needs a
            // quiet frame to match.
            Frame();
            Down(ButtonRightStick);
            Frame();
            Up(ButtonRightStick);
            Frame();
            Equal(true, Capture.IsOpen, "the fixture opened the menu");
            while (Capture.Commands.TryDequeue(out _))
            {
            }
        }

        private void Publish() =>
            Capture.PublishContext(
                ControllerNavigationDomain.Field,
                IsForeground,
                ModuleSupportsNavigation,
                gameIsBusy: false,
                Now,
                identity: 124);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"SDL poll starvation: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
