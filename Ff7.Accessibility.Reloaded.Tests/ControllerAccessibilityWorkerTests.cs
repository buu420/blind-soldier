using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class ControllerAccessibilityWorkerTests
{
    public static void Run()
    {
        KeyboardSettingsSurvivesAbsentControllerAndSpeechOff();
        ControllerSettingsAndPartyRespectNativeOwnership();
        DeclinedAnnouncementsCannotStopTheWorker();
        NavigationRetirementPreservesSettings();
        Console.WriteLine("Controller accessibility worker tests passed.");
    }

    private static void KeyboardSettingsSurvivesAbsentControllerAndSpeechOff()
    {
        var open = false;
        var commands = new List<ModSettingsInputCommand>();
        var spoken = new List<string>();
        var down = new HashSet<int>();
        var pauses = 0;
        var dispatcher = new ControllerAccessibilityDispatcher(() => null, () => open,
            cmd => { commands.Add(cmd); if (cmd == ModSettingsInputCommand.Open) open = true;
                if (cmd == ModSettingsInputCommand.Close) open = false; return cmd.ToString(); },
            _ => throw new Exception("keyboard settings must not query party"), spoken.Add,
            _ => throw new Exception("keyboard settings must not speak party"), () => pauses++);
        var now = DateTime.UtcNow;
        dispatcher.Tick(true, down.Contains, now);
        down.Add('I');
        dispatcher.Tick(true, down.Contains, now);
        down.Add(0x7A);
        dispatcher.Tick(true, down.Contains, now);
        Assert(open && spoken.SequenceEqual(new[] { "Open" }), "F11 opens self-audible settings independently of normal speech");
        down.Remove(0x7A);
        dispatcher.Tick(true, down.Contains, now.AddSeconds(2));
        Assert(commands.Count == 1, "I held before menu opening cannot activate a setting");
        Assert(open && pauses == 2, "keyboard menu survives absent controller polling and pauses walking on each tick");
        down.Remove('I'); dispatcher.Tick(true, down.Contains, now);
        down.Add('I'); dispatcher.Tick(true, down.Contains, now);
        Assert(commands[^1] == ModSettingsInputCommand.Activate, "fresh I activates the menu");
        dispatcher.Tick(false, down.Contains, now);
        Assert(!open && spoken[^1] == "Activate", "focus loss closes settings without background speech");
        dispatcher.Tick(true, down.Contains, now);
        Assert(!open, "held keys on focus return do not reopen settings");
    }

    private static void ControllerSettingsAndPartyRespectNativeOwnership()
    {
        var now = DateTime.UtcNow;
        var capture = new ControllerNavigationCapture(s => new ControllerAccessibilityMenu(s), () => true);
        var open = false;
        var party = new List<ControllerNavigationCommand>();
        var dispatcher = new ControllerAccessibilityDispatcher(() => capture, () => open,
            cmd => { if (cmd == ModSettingsInputCommand.Open) open = true;
                if (cmd == ModSettingsInputCommand.Close) open = false; return cmd.ToString(); },
            cmd => { party.Add(cmd); return "native party"; }, _ => { }, _ => { }, () => { });
        capture.PublishContext(ControllerNavigationDomain.Battle, true, false, false, now, 0, true);
        _ = capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), now);
        _ = capture.ObserveRawPoll(new(true, 0, 1, GamepadButton.LeftTrigger), now);
        dispatcher.Tick(true, _ => false, now);
        Assert(party.SequenceEqual(new[] { ControllerNavigationCommand.PartySummary }), "battle context delivers its native party command");
        _ = capture.ObserveRawPoll(new(true, 0, 2, GamepadButton.None), now);
        _ = capture.ObserveRawPoll(new(true, 0, 3, ControllerAccessibilityMenu.Modifiers | GamepadButton.Y), now);
        dispatcher.Tick(true, _ => false, now);
        Assert(open && capture.SettingsIsOpen, "the controller menu captures the native pad");
        capture.AccessibilityCommands.Enqueue(ControllerNavigationCommand.SettingsClose, -1);
        dispatcher.Tick(true, _ => false, now);
        Assert(!open, "a close is honored even when the context changed");
        capture.PublishUnavailable(ControllerNavigationDomain.None, now);
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, now, 500);
        capture.AccessibilityCommands.Enqueue(ControllerNavigationCommand.PartyNext, capture.Generation.Id);
        dispatcher.Tick(true, _ => false, now);
        Assert(party.Count == 1, "field input cannot execute a battle party query");
    }

    private static void DeclinedAnnouncementsCannotStopTheWorker()
    {
        var now = DateTime.UtcNow;
        var capture = new ControllerNavigationCapture(s => new ControllerAccessibilityMenu(s), () => true);
        var open = false;
        var settingsAttempts = 0;
        var partyAttempts = 0;
        var pauses = 0;
        var down = new HashSet<int>();
        var dispatcher = new ControllerAccessibilityDispatcher(() => capture, () => open,
            cmd => { if (cmd == ModSettingsInputCommand.Open) open = true;
                if (cmd == ModSettingsInputCommand.Close) open = false; return "current setting"; },
            _ => "current native party",
            _ => { if (++settingsAttempts == 1) throw new InvalidOperationException("Prism declined settings"); },
            _ => { if (++partyAttempts == 1) throw new InvalidOperationException("Prism declined party"); },
            () => pauses++);
        capture.PublishSettingsContext(ControllerNavigationDomain.Battle, 2, true, true, now);
        capture.AccessibilityCommands.Enqueue(ControllerNavigationCommand.PartySummary, capture.Generation.Id);
        dispatcher.Tick(true, down.Contains, now);
        capture.AccessibilityCommands.Enqueue(ControllerNavigationCommand.PartyStatuses, capture.Generation.Id);
        dispatcher.Tick(true, down.Contains, now.AddMilliseconds(20));
        Assert(partyAttempts == 2, "a declined party announcement does not escape or stop later readouts");
        down.Add(0x7A);
        dispatcher.Tick(true, down.Contains, now.AddMilliseconds(40));
        Assert(open && pauses == 1, "declined menu speech must still pause movement");
        down.Clear();
        dispatcher.Tick(true, down.Contains, now.AddMilliseconds(600));
        Assert(settingsAttempts == 2 && open, "a declined current settings line is retried while its menu stays open");
    }

    private static void NavigationRetirementPreservesSettings()
    {
        var now = DateTime.UtcNow;
        var capture = new ControllerNavigationCapture(s => new ControllerAccessibilityMenu(s), () => true);
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, now, 500);
        capture.SetSettingsOpen(true);
        capture.RequestClose(ControllerNavigationDomain.Field);
        capture.PublishSettingsContext(ControllerNavigationDomain.SystemMenu, -1, true, false, now);
        _ = capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), now);
        Assert(capture.SettingsIsOpen, "retiring navigation cannot close the independent settings menu");
        capture.PublishNavigationUnavailable(ControllerNavigationDomain.WorldMap, true, now);
        capture.PublishSettingsContext(ControllerNavigationDomain.WorldMap, 0, true, false, now);
        capture.PublishNavigationUnavailable(ControllerNavigationDomain.WorldMap, true, now);
        _ = capture.ObserveRawPoll(new(true, 0, 1, GamepadButton.None), now);
        Assert(capture.SettingsIsOpen && !capture.Generation.ModuleSupportsNavigation,
            "a native world menu pauses navigation while settings keeps owning input");
        capture.RequestClose();
        _ = capture.ObserveRawPoll(new(true, 0, 1, GamepadButton.None), now);
        Assert(!capture.SettingsIsOpen, "global shutdown still closes settings");
        capture.PublishNavigationUnavailable(ControllerNavigationDomain.WorldMap, true, now);
        _ = capture.ObserveRawPoll(new(true, 0, 2, GamepadButton.None), now);
        _ = capture.ObserveRawPoll(new(true, 0, 3, ControllerAccessibilityMenu.Modifiers | GamepadButton.Y), now);
        Assert(capture.SettingsIsOpen, "controller settings can open while a native menu blocks movement");
        capture.PublishNavigationUnavailable(ControllerNavigationDomain.WorldMap, false, now);
        _ = capture.ObserveRawPoll(new(true, 0, 4, GamepadButton.None), now);
        Assert(!capture.SettingsIsOpen, "actual focus loss closes settings as well as navigation");
    }

    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
