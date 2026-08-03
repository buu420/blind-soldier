using Ff7.Accessibility.Steam2026X64.Runtime.Menus;

internal static class Steam2026RenderedMenuSpeechTrackerTests
{
    internal static void Run()
    {
        UnknownLifecycleUsesNativeTitleEvidence();
        UnknownLifecycleRejectsUnidentifiedMenus();
        KnownNonTitleModuleRemainsRejected();
        KnownTitleModuleRetainsTranslatedBehavior();
    }

    private static void UnknownLifecycleUsesNativeTitleEvidence()
    {
        var tracker = new Steam2026RenderedMenuSpeechTracker();
        var now = new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);
        var sequence = 0L;

        ObserveText(tracker, ref sequence, now, "NEW GAME", 100, 100, moduleId: null);
        ObserveText(tracker, ref sequence, now, "Continue?", 100, 140, moduleId: null);
        ObserveText(tracker, ref sequence, now, "ADDITIONAL CREDITS", 100, 180, moduleId: null);
        ObserveText(tracker, ref sequence, now, "QUIT", 100, 220, moduleId: null);
        ObserveCursor(tracker, ref sequence, now, 70, 180, moduleId: null);

        Equal(true, tracker.TryGetPending(out var selection),
            "native title evidence produces a selection without guest lifecycle");
        Equal("ADDITIONAL CREDITS", selection.Text,
            "native selected title text is preserved exactly");
    }

    private static void UnknownLifecycleRejectsUnidentifiedMenus()
    {
        var tracker = new Steam2026RenderedMenuSpeechTracker();
        var now = new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);
        var sequence = 0L;

        ObserveText(tracker, ref sequence, now, "Item", 100, 100, moduleId: null);
        ObserveCursor(tracker, ref sequence, now, 70, 100, moduleId: null);

        Equal(false, tracker.TryGetPending(out _),
            "unknown menu is silent without native title anchors");
    }

    private static void KnownNonTitleModuleRemainsRejected()
    {
        var tracker = new Steam2026RenderedMenuSpeechTracker();
        var now = new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);
        var sequence = 0L;

        ObserveText(tracker, ref sequence, now, "NEW GAME", 100, 100, moduleId: 5);
        ObserveText(tracker, ref sequence, now, "Continue?", 100, 140, moduleId: 5);
        ObserveText(tracker, ref sequence, now, "QUIT", 100, 260, moduleId: 5);
        ObserveCursor(tracker, ref sequence, now, 70, 140, moduleId: 5);

        Equal(false, tracker.TryGetPending(out _),
            "known non-title module overrides fallback title evidence");
    }

    private static void KnownTitleModuleRetainsTranslatedBehavior()
    {
        var tracker = new Steam2026RenderedMenuSpeechTracker();
        var now = new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);
        var sequence = 0L;

        ObserveText(
            tracker,
            ref sequence,
            now,
            "Continue?",
            100,
            140,
            Steam2026RenderedMenuSpeechTracker.TitleModule);
        ObserveCursor(
            tracker,
            ref sequence,
            now,
            70,
            140,
            Steam2026RenderedMenuSpeechTracker.TitleModule);

        Equal(true, tracker.TryGetPending(out var selection),
            "known title module does not require fallback anchors");
        Equal("Continue?", selection.Text,
            "known title module selection remains native text");
    }

    private static void ObserveText(
        Steam2026RenderedMenuSpeechTracker tracker,
        ref long sequence,
        DateTime now,
        string text,
        int x,
        int y,
        int? moduleId)
    {
        sequence++;
        tracker.Observe(
            new TranslatedMenuIngressSnapshot(
                Steam2026MenuCallbackKind.EncodedTextA,
                sequence,
                now,
                null,
                null,
                new TranslatedMenuTextObservation(
                    Steam2026MenuCallbackKind.EncodedTextA,
                    text,
                    x,
                    y,
                    Color: 0,
                    Context: 7)),
            moduleId,
            isHostForeground: true);
    }

    private static void ObserveCursor(
        Steam2026RenderedMenuSpeechTracker tracker,
        ref long sequence,
        DateTime now,
        int x,
        int y,
        int? moduleId)
    {
        sequence++;
        tracker.Observe(
            new TranslatedMenuIngressSnapshot(
                Steam2026MenuCallbackKind.CursorA,
                sequence,
                now,
                new TranslatedMenuCursorObservation(
                    Steam2026MenuCallbackKind.CursorA,
                    x,
                    y,
                    Context: 7),
                null,
                null),
            moduleId,
            isHostForeground: true);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }
}
