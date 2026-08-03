using Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

internal static class Steam2026SystemMenuSpeechTests
{
    internal static void Run()
    {
        FormatsEveryNativeControlShape();
        DelaysHelpUntilFocusIsStable();
        CancelsStaleHelpWhenFocusMoves();
        DeduplicatesFramesAndSpeaksVerifiedValueChanges();
        RepeatsOnlyTheSingleAutosaveSettingAfterNativeVerticalInput();
        ReadsModalTextOnceAndThenOnlyTheFocusedChoice();
        ResetClearsPendingAndAcknowledgedState();
    }

    private static void FormatsEveryNativeControlShape()
    {
        var catalog = Steam2026SystemMenuCatalog.CreateEnglish();

        Equal(
            "Game Options",
            catalog.FormatImmediate(Observe("escape-root", "game-options")),
            "native system-menu button");
        Equal(
            "Speed Boost (x3), On",
            catalog.FormatImmediate(Observe("boosts", "speed-boost", value: "On")),
            "native system-menu toggle");
        Equal(
            "Display Mode, Borderless Windowed, 2 of 3",
            catalog.FormatImmediate(Observe(
                "system",
                "display-mode",
                value: "Borderless Windowed",
                position: 2,
                count: 3)),
            "native system-menu list");
        Equal(
            "Brightness, 52 percent",
            catalog.FormatImmediate(Observe("system", "brightness", value: "52")),
            "native system-menu slider");
        Equal(
            "Move Up. Primary, W. Secondary, Up Arrow",
            catalog.FormatImmediate(Observe(
                "keyboard",
                "move-up",
                primaryBinding: "W",
                secondaryBinding: "Up Arrow")),
            "native system-menu binding row");
        Equal(
            "You cannot undo these changes once applied. You also cannot unlock achievements with this save. Proceed? Yes",
            catalog.FormatImmediate(Observe(
                "boosts-modal",
                "confirm-choice",
                value: "Yes",
                modalText:
                    "You cannot undo these changes once applied. " +
                    "You also cannot unlock achievements with this save. Proceed?")),
            "native system-menu modal");
    }

    private static void DelaysHelpUntilFocusIsStable()
    {
        var coordinator = CreateCoordinator();
        var now = Utc(12, 0, 0);

        var immediate = coordinator.Observe(
            Observe("escape-root", "game-options", generation: 1),
            now);
        Equal(1, immediate.Count, "new focus speaks immediately");
        Equal("Game Options", immediate[0].Text, "new focus label");
        Equal(true, immediate[0].Interrupt, "new focus interrupts stale speech");

        Equal(0, coordinator.Poll(now.AddMilliseconds(499)).Count, "help waits 499 milliseconds");
        var help = coordinator.Poll(now.AddMilliseconds(500));
        Equal(1, help.Count, "help speaks at 500 milliseconds");
        Equal("Change game options.", help[0].Text, "visible help text");
        Equal(false, help[0].Interrupt, "help does not interrupt current speech");
        Equal(0, coordinator.Poll(now.AddMilliseconds(700)).Count, "help speaks once");
    }

    private static void CancelsStaleHelpWhenFocusMoves()
    {
        var coordinator = CreateCoordinator();
        var now = Utc(12, 1, 0);
        coordinator.Observe(Observe("escape-root", "game-options", generation: 1), now);

        var moved = coordinator.Observe(
            Observe("escape-root", "boosts", generation: 2),
            now.AddMilliseconds(300));
        Equal(1, moved.Count, "moved focus speaks");
        Equal("Boosts", moved[0].Text, "moved focus label");
        Equal(
            0,
            coordinator.Poll(now.AddMilliseconds(500)).Count,
            "old focus help is cancelled");
        var newHelp = coordinator.Poll(now.AddMilliseconds(800));
        Equal(1, newHelp.Count, "new focus help uses its own deadline");
        Equal("Select in-game boosts.", newHelp[0].Text, "new focus help text");
    }

    private static void DeduplicatesFramesAndSpeaksVerifiedValueChanges()
    {
        var coordinator = CreateCoordinator();
        var now = Utc(12, 2, 0);
        var off = Observe("boosts", "speed-boost", value: "Off", generation: 8);

        Equal(1, coordinator.Observe(off, now).Count, "first verified toggle frame");
        Equal(
            0,
            coordinator.Observe(off, now.AddMilliseconds(1)).Count,
            "unchanged native frame is silent");

        var changed = coordinator.Observe(
            Observe("boosts", "speed-boost", value: "On", generation: 9),
            now.AddMilliseconds(2));
        Equal(1, changed.Count, "verified toggle change speaks");
        Equal("Speed Boost (x3), On", changed[0].Text, "verified toggle value");
        Equal(true, changed[0].Interrupt, "verified value change interrupts");
    }

    private static void RepeatsOnlyTheSingleAutosaveSettingAfterNativeVerticalInput()
    {
        var coordinator = CreateCoordinator();
        var now = Utc(12, 2, 30);
        var setting = Observe(
            "autosave",
            "autosave",
            value: "On",
            generation: 10);

        Equal(1, coordinator.Observe(setting, now).Count, "initial autosave setting");
        Equal(
            0,
            coordinator.Observe(setting, now.AddMilliseconds(1)).Count,
            "unchanged autosave polling frame");

        var repeated = coordinator.Observe(
            setting,
            now.AddMilliseconds(2),
            repeatUnchangedAutosave: true);
        Equal(1, repeated.Count, "native Up or Down repeats the single setting");
        Equal("Autosave, On", repeated[0].Text, "repeated autosave value");
        Equal(true, repeated[0].Interrupt, "repeated autosave value interrupts");

        var apply = Observe("autosave", "apply", generation: 10);
        Equal(1, coordinator.Observe(apply, now.AddMilliseconds(3)).Count, "apply focus");
        Equal(
            0,
            coordinator.Observe(
                apply,
                now.AddMilliseconds(4),
                repeatUnchangedAutosave: true).Count,
            "vertical input does not repeat another autosave control");
    }

    private static void ReadsModalTextOnceAndThenOnlyTheFocusedChoice()
    {
        var coordinator = CreateCoordinator();
        var now = Utc(12, 3, 0);
        const string warning =
            "You cannot undo these changes once applied. " +
            "You also cannot unlock achievements with this save. Proceed?";

        var opened = coordinator.Observe(
            Observe(
                "boosts-modal",
                "confirm-choice",
                value: "Yes",
                modalText: warning,
                generation: 20),
            now);
        Equal(1, opened.Count, "modal opening speaks");
        Equal($"{warning} Yes", opened[0].Text, "modal warning and focused choice");

        var moved = coordinator.Observe(
            Observe(
                "boosts-modal",
                "confirm-choice",
                value: "No",
                modalText: warning,
                generation: 21),
            now.AddMilliseconds(1));
        Equal(1, moved.Count, "modal choice movement speaks");
        Equal("No", moved[0].Text, "modal warning is not repeated while moving");
    }

    private static void ResetClearsPendingAndAcknowledgedState()
    {
        var coordinator = CreateCoordinator();
        var now = Utc(12, 4, 0);
        var focus = Observe("escape-root", "game-options", generation: 1);
        coordinator.Observe(focus, now);
        coordinator.Reset();

        Equal(
            0,
            coordinator.Poll(now.AddSeconds(1)).Count,
            "reset cancels pending help");
        Equal(
            1,
            coordinator.Observe(focus, now.AddSeconds(2)).Count,
            "reset permits current focus to be announced again");

        coordinator.Observe(null, now.AddSeconds(3));
        Equal(
            1,
            coordinator.Observe(focus, now.AddSeconds(4)).Count,
            "scene close clears acknowledged focus");
    }

    private static Steam2026SystemMenuSpeechCoordinator CreateCoordinator() =>
        new(
            Steam2026SystemMenuCatalog.CreateEnglish(),
            TimeSpan.FromMilliseconds(500));

    private static Steam2026SystemMenuObservation Observe(
        string sceneId,
        string controlId,
        string? value = null,
        int position = 0,
        int count = 0,
        string? primaryBinding = null,
        string? secondaryBinding = null,
        string? modalText = null,
        long generation = 1) =>
        new(
            sceneId,
            controlId,
            value,
            position,
            count,
            primaryBinding,
            secondaryBinding,
            modalText,
            IsFocused: true,
            generation);

    private static DateTime Utc(int hour, int minute, int second) =>
        new(2026, 7, 27, hour, minute, second, DateTimeKind.Utc);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }
}
