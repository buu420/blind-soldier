using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class ControllerModifierNavigationTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
    private const GamepadButton LeftTrigger = (GamepadButton)0x10000;
    private const GamepadButton RightTrigger = (GamepadButton)0x20000;

    public static void Run()
    {
        StandaloneStickClickDoesNotOpenNavigation();
        EitherHeldTriggerBrowsesAndCapturesItsCommands();
        ReleasingModifierBeforeStickClickDoesNotLeakTheTail();
#if BLIND_SOLDIER_LEGACY_TESTS
        TheXInputDetourReadsAndSuppressesActualTriggerAndStickFields();
#endif
        AnUnavailableChordKeepsItsFaceButtonUntilRelease();
        PartyCommandsAndLatchedSettingsUseTheirOwnQueue();
        FocusAndContextChangesDoNotTurnHeldInputIntoCommands();
#if BLIND_SOLDIER_LEGACY_TESTS
        AnotherXInputPadCanTakeOverOnlyWithAFreshTrigger();
#endif
        Console.WriteLine("Controller modifier navigation tests passed.");
    }

    private static ControllerNavigationCapture Create() => new(
        suppressor => new ControllerAccessibilityMenu(suppressor),
        () => true);

    private static ControllerNavigationCapture Walking()
    {
        var capture = Create();
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 500);
        _ = capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now);
        return capture;
    }

    private static void StandaloneStickClickDoesNotOpenNavigation()
    {
        var capture = Walking();
        _ = capture.ObserveRawPoll(new(true, 0, 1, GamepadButton.RightThumb), Now);
        Equal(false, capture.IsOpen, "R3 alone must not open the mod or start navigation");
        Equal(0, capture.Commands.Count, "R3 alone produces no accessibility command");
    }

    private static void EitherHeldTriggerBrowsesAndCapturesItsCommands()
    {
        foreach (var modifier in new[] { LeftTrigger, RightTrigger })
        {
            var capture = Walking();
            var strip = capture.ObserveRawPoll(new(true, 0, 1, modifier), Now);
            Equal(true, capture.IsOpen, "holding either trigger opens browsing");
            Equal(true, (strip & modifier) != 0, "the modifier is captured");
            Expect(capture, ControllerNavigationCommand.Opened);
            foreach (var (button, command) in new[]
            {
                (GamepadButton.DPadLeft, ControllerNavigationCommand.PreviousCategory),
                (GamepadButton.DPadRight, ControllerNavigationCommand.NextCategory),
                (GamepadButton.DPadUp, ControllerNavigationCommand.PreviousTarget),
                (GamepadButton.DPadDown, ControllerNavigationCommand.NextTarget)
            })
            {
                _ = capture.ObserveRawPoll(new(true, 0, 2, modifier), Now);
                strip = capture.ObserveRawPoll(new(true, 0, 3, modifier | button), Now);
                Equal(GamepadButton.None, (modifier | button) & ~strip,
                    "the game cannot see a command used to browse");
                Expect(capture, command);
            }
            _ = capture.ObserveRawPoll(new(true, 0, 4, modifier), Now);
            strip = capture.ObserveRawPoll(new(true, 0, 5, modifier | GamepadButton.LeftThumb), Now);
            Equal(GamepadButton.None, (modifier | GamepadButton.LeftThumb) & ~strip,
                "L3 guidance is consumed");
            Expect(capture, ControllerNavigationCommand.StartNavigation);
        }
    }

    private static void ReleasingModifierBeforeStickClickDoesNotLeakTheTail()
    {
        var capture = Walking();
        _ = capture.ObserveRawPoll(new(true, 0, 1, LeftTrigger), Now);
        capture.Commands.ClearExceptStops();
        var strip = capture.ObserveRawPoll(new(true, 0, 2, LeftTrigger | GamepadButton.RightThumb), Now);
        Equal(true, (strip & GamepadButton.RightThumb) != 0, "modified R3 is captured");
        Expect(capture, ControllerNavigationCommand.StartAutoWalk);
        strip = capture.ObserveRawPoll(new(true, 0, 3, GamepadButton.RightThumb), Now);
        Equal(true, (strip & GamepadButton.RightThumb) != 0,
            "R3 stays captured after the trigger is released");
        strip = capture.ObserveRawPoll(new(true, 0, 4, GamepadButton.None), Now);
        Equal(GamepadButton.None, strip, "controls return after the complete chord releases");
    }

    private static void Expect(ControllerNavigationCapture capture, ControllerNavigationCommand wanted)
    {
        Equal(true, capture.Commands.TryDequeue(out var got), "command was queued");
        Equal(wanted, got, "queued command matches the deliberate press");
    }

    private static void ExpectAccessibility(ControllerNavigationCapture capture, ControllerNavigationCommand wanted)
    {
        Equal(true, capture.AccessibilityCommands.TryDequeue(out var got), "accessibility command was queued");
        Equal(wanted, got, "party/settings command matches the deliberate press");
    }

    private static void PartyCommandsAndLatchedSettingsUseTheirOwnQueue()
    {
        var capture = Create();
        capture.PublishContext(ControllerNavigationDomain.Battle, true, false, false, Now, 1, true);
        _ = capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now);
        _ = capture.ObserveRawPoll(new(true, 0, 1, LeftTrigger), Now);
        ExpectAccessibility(capture, ControllerNavigationCommand.PartySummary);
        foreach (var (button, command) in new[]
        {
            (GamepadButton.DPadLeft, ControllerNavigationCommand.PartyPrevious),
            (GamepadButton.DPadRight, ControllerNavigationCommand.PartyNext),
            (GamepadButton.DPadUp, ControllerNavigationCommand.PartySummary),
            (GamepadButton.DPadDown, ControllerNavigationCommand.PartyStatuses),
            (GamepadButton.Y, ControllerNavigationCommand.PartyLimit)
        })
        {
            _ = capture.ObserveRawPoll(new(true, 0, 2, LeftTrigger), Now);
            var strip = capture.ObserveRawPoll(new(true, 0, 3, LeftTrigger | button), Now);
            Equal(GamepadButton.None, (LeftTrigger | button) & ~strip, "party commands never reach battle input");
            ExpectAccessibility(capture, command);
        }
        _ = capture.ObserveRawPoll(new(true, 0, 4, GamepadButton.None), Now);
        _ = capture.ObserveRawPoll(new(true, 0, 5, LeftTrigger | RightTrigger), Now);
        _ = capture.ObserveRawPoll(new(true, 0, 6, LeftTrigger | RightTrigger | GamepadButton.Y), Now);
        ExpectAccessibility(capture, ControllerNavigationCommand.SettingsOpen);
        Equal(true, capture.SettingsIsOpen, "settings opens in battle with both triggers and Y");
        _ = capture.ObserveRawPoll(new(true, 0, 7, GamepadButton.None), Now);
        Equal(true, capture.SettingsIsOpen, "settings stays open when the opening chord is released");
        _ = capture.ObserveRawPoll(new(true, 0, 8, GamepadButton.A), Now);
        ExpectAccessibility(capture, ControllerNavigationCommand.SettingsActivate);
        capture.SetSettingsOpen(false);
        var tail = capture.ObserveRawPoll(new(true, 0, 9, GamepadButton.A), Now);
        Equal(true, (tail & GamepadButton.A) != 0, "closing settings by keyboard keeps held controller Confirm private");
    }

    private static void FocusAndContextChangesDoNotTurnHeldInputIntoCommands()
    {
        var capture = Walking();
        _ = capture.ObserveRawPoll(new(true, 0, 1, LeftTrigger), Now);
        capture.Commands.ClearExceptStops();
        capture.PublishContext(ControllerNavigationDomain.Field, false, true, false, Now, 500);
        _ = capture.ObserveRawPoll(new(true, 0, 2, LeftTrigger | GamepadButton.RightThumb), Now);
        capture.Commands.ClearExceptStops();
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 500);
        var strip = capture.ObserveRawPoll(new(true, 0, 3, LeftTrigger | GamepadButton.RightThumb), Now);
        Equal(0, capture.Commands.Count, "a held chord on focus return cannot start a route");
        Equal(true, (strip & GamepadButton.RightThumb) != 0, "modified R3 is private across focus gaps");
        _ = capture.ObserveRawPoll(new(true, 0, 4, GamepadButton.None), Now);
        capture.PublishUnavailable(ControllerNavigationDomain.None, Now);
        _ = capture.ObserveRawPoll(new(true, 0, 4, GamepadButton.None), Now);
        capture.PublishContext(ControllerNavigationDomain.Battle, true, false, false, Now, 2, true);
        _ = capture.ObserveRawPoll(new(true, 0, 5, LeftTrigger), Now);
        ExpectAccessibility(capture, ControllerNavigationCommand.PartySummary);
        capture.PublishUnavailable(ControllerNavigationDomain.None, Now);
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 501);
        _ = capture.ObserveRawPoll(new(true, 0, 6, LeftTrigger | GamepadButton.DPadRight), Now);
        Equal(0, capture.AccessibilityCommands.Count, "a domain change discards old party commands");
        Equal(false, capture.IsOpen, "a trigger held through a domain transition must be released first");
    }

#if BLIND_SOLDIER_LEGACY_TESTS
    private static void TheXInputDetourReadsAndSuppressesActualTriggerAndStickFields()
    {
        ControllerNavigationCapture? capture = null;
        var physical = new XInputCaptureHook.XInputState();
        using var hook = XInputCaptureHook.CreateForDetourTest(installed =>
            capture = new(s => new ControllerAccessibilityMenu(s), installed), ["test-xinput"], () => Now);
        hook.SetOriginalForTest(0, (int slot, out XInputCaptureHook.XInputState state) =>
        {
            state = physical;
            return 0;
        });
        capture!.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 500);
        _ = hook.InvokeDetourForTest(0, 0, out _);
        physical.Gamepad.LeftTrigger = 220;
        physical.Gamepad.ThumbLX = 20000;
        physical.Gamepad.ThumbRY = -18000;
        Equal(0, hook.InvokeDetourForTest(0, 0, out var returned), "original success code is retained");
        Equal(true, capture.IsOpen, "the hook observes the actual trigger byte");
        Equal((byte)0, returned.Gamepad.LeftTrigger, "captured trigger is not returned to FFNx");
        Equal((short)0, returned.Gamepad.ThumbLX, "browsing cannot move the party");
        Equal((short)0, returned.Gamepad.ThumbRY, "browsing cannot turn the camera");
        physical.Gamepad.Buttons = (ushort)GamepadButton.RightThumb;
        _ = hook.InvokeDetourForTest(0, 0, out returned);
        Equal((ushort)0, returned.Gamepad.Buttons, "modified R3 does not reach FFNx");
        physical.Gamepad.LeftTrigger = 0;
        _ = hook.InvokeDetourForTest(0, 0, out returned);
        Equal((ushort)0, returned.Gamepad.Buttons, "the native R3 release tail stays captured");
        physical.Gamepad.Buttons = 0;
        _ = hook.InvokeDetourForTest(0, 0, out returned);
        Equal((short)20000, returned.Gamepad.ThumbLX, "ordinary movement returns after the chord releases");
    }

    private static void AnotherXInputPadCanTakeOverOnlyWithAFreshTrigger()
    {
        var pads = new XInputCaptureHook.XInputState[2];
        using var hook = XInputCaptureHook.CreateForDetourTest(installed =>
            new(s => new ControllerAccessibilityMenu(s), installed), ["test-xinput"], () => Now);
        hook.SetOriginalForTest(0, (int slot, out XInputCaptureHook.XInputState state) =>
        { state = pads[slot]; return 0; });
        hook.Capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 500);
        _ = hook.InvokeDetourForTest(0, 0, out _);
        _ = hook.InvokeDetourForTest(0, 1, out _);
        pads[1].Gamepad.RightTrigger = 200;
        _ = hook.InvokeDetourForTest(0, 1, out var returned);
        Equal(true, hook.Capture.IsOpen, "a fresh trigger on the second pad takes over navigation");
        Equal((byte)0, returned.Gamepad.RightTrigger, "second-pad modifier is consumed");
        Expect(hook.Capture, ControllerNavigationCommand.Opened);
        pads[0].Gamepad.LeftTrigger = 200;
        pads[0].Gamepad.Buttons = (ushort)(GamepadButton.RightThumb | GamepadButton.A);
        _ = hook.InvokeDetourForTest(0, 0, out returned);
        Equal((ushort)0, returned.Gamepad.Buttons, "foreign modified buttons cannot toggle Battle Assist or Confirm");
        pads[0].Gamepad.LeftTrigger = 0;
        _ = hook.InvokeDetourForTest(0, 0, out returned);
        Equal((ushort)0, returned.Gamepad.Buttons, "foreign-pad release tails stay private");
        Equal(0, hook.Capture.Commands.Count, "another pad cannot steal an open menu");
    }

#endif

    private static void AnUnavailableChordKeepsItsFaceButtonUntilRelease()
    {
        var capture = Create();
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, true, Now, 500);
        _ = capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now);
        _ = capture.ObserveRawPoll(new(true, 0, 1, LeftTrigger | GamepadButton.A), Now);
        var strip = capture.ObserveRawPoll(new(true, 0, 2, GamepadButton.A), Now);
        Equal(true, (strip & GamepadButton.A) != 0,
            "a refused modifier chord must not confirm a native dialogue on trigger release");
    }

    private static void Equal<T>(T wanted, T got, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(wanted, got))
            throw new InvalidOperationException($"{message}: expected {wanted}, got {got}");
    }
}
