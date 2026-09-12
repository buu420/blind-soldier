using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The runaway-category bug, reproduced at the detour.
///
/// <para>The live log shows the capture installed over <c>xinput1_4.dll</c> and
/// <c>xinput9_1_0.dll</c>, and 9_1_0 forwards into 1_4. So one game poll entered the
/// detour twice. The inner run decided on the raw state and stripped the owned buttons
/// out of the shared buffer; the outer run then observed that stripped buffer and told
/// the edge tracker the button had been released. Every frame therefore looked like a
/// fresh press, and a held bumper cycled categories about twenty times a second.</para>
/// </summary>
internal static class ControllerCaptureNestedExportTests
{
    private static readonly DateTime Start = new(2026, 9, 11, 11, 25, 40, DateTimeKind.Utc);

    public static void Run()
    {
        OneNestedPollIsOnePress();
        AHeldBumperDoesNotRepeatThroughNestedExports();
    }

    private static void OneNestedPollIsOnePress()
    {
        var pad = new FakePad();
        using var hook = CreateNestedHook(pad, out var capture);

        // Nothing held: the outer call must see the raw word and the game gets it all.
        Equal(GamepadButton.None, pad.PollOuter(hook), "an empty pad passes through");

        // One game poll, two entries into the detour: exactly one policy observation.
        pad.Buttons = GamepadButton.RightThumb;
        var before = capture.ObservedPolls;
        Equal(GamepadButton.None, pad.PollOuter(hook), "the stick click opens the menu and is taken");
        Equal(true, capture.IsOpen, "the menu is open");
        Equal(1L, capture.ObservedPolls - before,
            "one game poll is one policy observation, not one per hooked export");
    }

    /// <summary>
    /// The player's own report: hold a bumper and categories run away. One poll per
    /// frame, the button never released, so exactly one command may ever come out.
    /// </summary>
    private static void AHeldBumperDoesNotRepeatThroughNestedExports()
    {
        var pad = new FakePad();
        using var hook = CreateNestedHook(pad, out var capture);

        Open(pad, hook, capture);

        var categoryCommands = 0;
        pad.Buttons = GamepadButton.RightShoulder;

        // Two seconds of a 60 Hz game, bumper held the whole time.
        for (var frame = 0; frame < 120; frame++)
        {
            pad.Advance(TimeSpan.FromMilliseconds(16));
            Equal(GamepadButton.None, pad.PollOuter(hook),
                $"frame {frame}: the held bumper never reaches the game");
            while (capture.Commands.TryDequeue(out var command))
            {
                if (command is ControllerNavigationCommand.NextCategory
                    or ControllerNavigationCommand.PreviousCategory)
                {
                    categoryCommands++;
                }
            }
        }

        Equal(1, categoryCommands,
            "two seconds of a held bumper is one category change, not one per frame");

        // Released and pressed again is a second one, so the press still works.
        pad.Buttons = GamepadButton.None;
        pad.Advance(TimeSpan.FromMilliseconds(16));
        _ = pad.PollOuter(hook);
        pad.Buttons = GamepadButton.RightShoulder;
        pad.Advance(TimeSpan.FromMilliseconds(16));
        _ = pad.PollOuter(hook);

        var second = 0;
        while (capture.Commands.TryDequeue(out var command))
        {
            if (command == ControllerNavigationCommand.NextCategory)
            {
                second++;
            }
        }

        Equal(1, second, "and a genuine second press still moves one category");
    }

    private static void Open(FakePad pad, XInputCaptureHook hook, ControllerNavigationCapture capture)
    {
        pad.Buttons = GamepadButton.RightThumb;
        pad.Advance(TimeSpan.FromMilliseconds(16));
        _ = pad.PollOuter(hook);
        pad.Buttons = GamepadButton.None;
        pad.Advance(TimeSpan.FromMilliseconds(16));
        _ = pad.PollOuter(hook);
        Equal(true, capture.IsOpen, "the fixture opened the menu");
        while (capture.Commands.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// The production detour over two exports, wired as the live process had them:
    /// export 0 is xinput9_1_0 and forwards into export 1, xinput1_4.
    /// </summary>
    private static XInputCaptureHook CreateNestedHook(FakePad pad, out ControllerNavigationCapture capture)
    {
        ControllerNavigationCapture? created = null;
        var hook = XInputCaptureHook.CreateForDetourTest(
            isInstalled =>
            {
                created = new ControllerNavigationCapture(
                    suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
                    isInstalled,
                    isForegroundNow: () => true);
                return created;
            },
            ["xinput9_1_0.dll", "xinput1_4.dll"],
            () => pad.Now);

        // xinput9_1_0's "original" is a call into xinput1_4's export, which is hooked
        // too - so it re-enters the detour. That is the forwarding the live log shows.
        hook.SetOriginalForTest(0, (int userIndex, out XInputCaptureHook.XInputState state) =>
            hook.InvokeDetourForTest(1, userIndex, out state));

        // xinput1_4's original is the device itself.
        hook.SetOriginalForTest(1, pad.Read);

        capture = created!;
        pad.Capture = capture;
        capture.PublishContext(
            ControllerNavigationDomain.Field, true, true, false, pad.Now, identity: 500);
        return hook;
    }

    private sealed class FakePad
    {
        public DateTime Now { get; private set; } = Start;

        public GamepadButton Buttons { get; set; }

        public ControllerNavigationCapture? Capture { get; set; }

        /// <summary>A frame of game time, with the worker tick that goes with it.</summary>
        public void Advance(TimeSpan span)
        {
            Now += span;
            Capture?.PublishContext(
                ControllerNavigationDomain.Field, true, true, false, Now, identity: 500);
        }

        public int Read(int userIndex, out XInputCaptureHook.XInputState state)
        {
            state = default;
            if (userIndex != 0)
            {
                return 1167;
            }

            state.PacketNumber = 1;
            state.Gamepad.Buttons = (ushort)Buttons;
            return 0;
        }

        /// <summary>One game poll, entering at the forwarding export.</summary>
        public GamepadButton PollOuter(XInputCaptureHook hook)
        {
            var result = hook.InvokeDetourForTest(0, 0, out var state);
            return result == 0 ? (GamepadButton)state.Gamepad.Buttons : GamepadButton.None;
        }

    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Controller nested export: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
