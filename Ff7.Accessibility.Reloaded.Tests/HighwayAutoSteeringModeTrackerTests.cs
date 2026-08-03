using Ff7.Accessibility.Core;

internal static class HighwayAutoSteeringModeTrackerTests
{
    internal static void Run()
    {
        AnnouncesTheConfiguredModeOncePerHighwaySession();
        TogglesOnlyForForegroundHighwayInput();
        RetainsTheRuntimeModeAcrossHighwaySessions();
        HonorsADisabledConfiguredDefault();
    }

    private static void AnnouncesTheConfiguredModeOncePerHighwaySession()
    {
        var tracker = new HighwayAutoSteeringModeTracker(enabledByDefault: true);

        var background = tracker.Observe(isHighway: true, isForeground: false, toggleRequested: false);
        Equal(true, background.Enabled, "configured mode remains enabled in the background");
        Equal(false, background.ShouldControl, "background session cannot own steering");
        Equal(null, background.Announcement, "background session does not speak");

        var firstForeground = tracker.Observe(true, true, false);
        Equal(true, firstForeground.ShouldControl, "foreground highway session owns enabled steering");
        Equal(
            "Motorcycle auto steering on.",
            firstForeground.Announcement,
            "configured mode is announced on first foreground ownership");

        Equal(
            null,
            tracker.Observe(true, true, false).Announcement,
            "unchanged foreground mode is not repeated");
    }

    private static void TogglesOnlyForForegroundHighwayInput()
    {
        var tracker = new HighwayAutoSteeringModeTracker(enabledByDefault: true);
        _ = tracker.Observe(true, true, false);

        Equal(
            true,
            tracker.Observe(isHighway: false, isForeground: true, toggleRequested: true).Enabled,
            "F8 outside module 6 cannot toggle");
        Equal(
            true,
            tracker.Observe(isHighway: true, isForeground: false, toggleRequested: true).Enabled,
            "background F8 cannot toggle");

        var manual = tracker.Observe(true, true, true);
        Equal(false, manual.Enabled, "foreground module-6 F8 enables manual mode");
        Equal(false, manual.ShouldControl, "manual mode releases automatic ownership");
        Equal(
            "Motorcycle auto steering off. Steering beeps on.",
            manual.Announcement,
            "manual mode uses the approved Prism text");

        var automatic = tracker.Observe(true, true, true);
        Equal(true, automatic.Enabled, "second accepted F8 restores automatic mode");
        Equal(
            "Motorcycle auto steering on.",
            automatic.Announcement,
            "automatic mode uses the approved Prism text");
    }

    private static void RetainsTheRuntimeModeAcrossHighwaySessions()
    {
        var tracker = new HighwayAutoSteeringModeTracker(enabledByDefault: true);
        _ = tracker.Observe(true, true, false);
        Equal(false, tracker.Observe(true, true, true).Enabled, "first session switches to manual");
        _ = tracker.Observe(false, true, false);

        var nextSession = tracker.Observe(true, true, false);
        Equal(false, nextSession.Enabled, "runtime mode persists until mod reload");
        Equal(
            "Motorcycle auto steering off. Steering beeps on.",
            nextSession.Announcement,
            "next session announces the retained manual mode once");
    }

    private static void HonorsADisabledConfiguredDefault()
    {
        var tracker = new HighwayAutoSteeringModeTracker(enabledByDefault: false);
        var update = tracker.Observe(true, true, false);

        Equal(false, update.Enabled, "disabled configured default is honored");
        Equal(false, update.ShouldControl, "disabled configured default cannot control");
        Equal(
            "Motorcycle auto steering off. Steering beeps on.",
            update.Announcement,
            "disabled configured default is announced");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
