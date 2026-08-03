using Ff7.Accessibility.Reloaded;

internal static class PartyFormationSpeechTrackerTests
{
    private const int MenuModule = 5;
    private const int TextContext = 0x3DCCCCCD;
    private const int ReserveNameContext = 0x3DCED917;
    private const int PartyCursorContext = 0x3DCF0D84;
    private const int ReserveCursorContext = 0x3DCD0679;

    internal static void Run()
    {
        ReadsHighResolutionReformSelections();
        ReadsLowResolutionReformSelections();
        AnnouncesNativePartyValidationTransitionsOnce();
        RequiresExactReformOwnershipAndResetsOnExit();
    }

    private static void ReadsHighResolutionReformSelections()
    {
        var tracker = new PartyFormationSpeechTracker(TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        ObserveHighResolutionFrame(tracker, now);

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 0, 120, PartyCursorContext),
            now.AddMilliseconds(1));
        Equal(
            "Reform. Party slot 1, Cloud. Press Start when finished.",
            tracker.Poll(now.AddMilliseconds(80)),
            "first active party selection includes Reform ownership and the native Start instruction");

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 0, 257, PartyCursorContext),
            now.AddMilliseconds(100));
        Equal(
            "Party slot 2, Barret.",
            tracker.Poll(now.AddMilliseconds(180)),
            "second active party slot uses its native rendered name");

        tracker.ObserveDraw(
            new MenuTextRenderEntry("Reform", 508, 14, 7, 0),
            MenuModule,
            now.AddMilliseconds(190));
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 326, 223, ReserveCursorContext),
            now.AddMilliseconds(200));
        tracker.ObserveDraw(
            new MenuTextRenderEntry("Tifa", 438, 68, 7, ReserveNameContext),
            MenuModule,
            now.AddMilliseconds(205));
        Equal(
            "Available member, Tifa.",
            tracker.Poll(now.AddMilliseconds(280)),
            "occupied reserve cell uses the native highlighted member name");

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 326, 322, ReserveCursorContext),
            now.AddMilliseconds(300));
        Equal(
            "Empty.",
            tracker.Poll(now.AddMilliseconds(380)),
            "reserve cell with no native highlighted name is announced as empty");
        Equal(
            null,
            tracker.Poll(now.AddMilliseconds(400)),
            "stable empty reserve cell does not repeat");
    }

    private static void ReadsLowResolutionReformSelections()
    {
        var tracker = new PartyFormationSpeechTracker(TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        tracker.ObserveDraw(new MenuTextRenderEntry("Reform", 262, 7, 7, 0), MenuModule, now);
        tracker.ObserveDraw(
            new MenuTextRenderEntry("Select with START button.", 13, 7, 7, TextContext),
            MenuModule,
            now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Cloud", 67, 39, 7, TextContext), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Barret", 67, 108, 7, TextContext), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Red XIII", 67, 177, 7, TextContext), MenuModule, now);

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("A", MenuModule, 0, 61, PartyCursorContext),
            now.AddMilliseconds(1));
        Equal(
            "Reform. Party slot 1, Cloud. Press Start when finished.",
            tracker.Poll(now.AddMilliseconds(80)),
            "low-resolution active party selection");

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("A", MenuModule, 163, 113, ReserveCursorContext),
            now.AddMilliseconds(100));
        tracker.ObserveDraw(
            new MenuTextRenderEntry("Tifa", 219, 35, 7, ReserveNameContext),
            MenuModule,
            now.AddMilliseconds(105));
        Equal(
            "Available member, Tifa.",
            tracker.Poll(now.AddMilliseconds(180)),
            "low-resolution reserve selection");
    }

    private static void AnnouncesNativePartyValidationTransitionsOnce()
    {
        var tracker = new PartyFormationSpeechTracker(TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        ObserveHighResolutionFrame(tracker, now);
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 0, 120, PartyCursorContext),
            now.AddMilliseconds(1));
        tracker.Poll(now.AddMilliseconds(80));

        tracker.ObserveDraw(
            new MenuTextRenderEntry("Please make a party of three.", 26, 13, 7, TextContext),
            MenuModule,
            now.AddMilliseconds(100));
        Equal(
            "Please make a party of three.",
            tracker.Poll(now.AddMilliseconds(140)),
            "native incomplete-party validation");
        tracker.ObserveDraw(
            new MenuTextRenderEntry("Please make a party of three.", 26, 13, 7, TextContext),
            MenuModule,
            now.AddMilliseconds(150));
        Equal(null, tracker.Poll(now.AddMilliseconds(190)), "repeated native validation remains silent");

        tracker.ObserveDraw(
            new MenuTextRenderEntry("Select with START button.", 26, 13, 7, TextContext),
            MenuModule,
            now.AddMilliseconds(200));
        Equal(
            "Party complete. Press Start when finished.",
            tracker.Poll(now.AddMilliseconds(240)),
            "native valid-party transition");
    }

    private static void RequiresExactReformOwnershipAndResetsOnExit()
    {
        var tracker = new PartyFormationSpeechTracker(TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        tracker.ObserveDraw(new MenuTextRenderEntry("Cloud", 134, 77, 7, TextContext), MenuModule, now);
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 0, 120, PartyCursorContext),
            now.AddMilliseconds(1));
        Equal(null, tracker.Poll(now.AddMilliseconds(80)), "party-like draws outside Reform remain silent");

        ObserveHighResolutionFrame(tracker, now.AddMilliseconds(100));
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 0, 120, PartyCursorContext),
            now.AddMilliseconds(101));
        Equal(true, tracker.IsActive(now.AddMilliseconds(150)), "exact Reform title acquires ownership");
        Equal(
            "Reform. Party slot 1, Cloud. Press Start when finished.",
            tracker.Poll(now.AddMilliseconds(180)),
            "same selection speaks after exact ownership acquisition");

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", 19, 0, 120, PartyCursorContext),
            now.AddMilliseconds(200));
        Equal(false, tracker.IsActive(now.AddMilliseconds(200)), "leaving module 5 releases Reform ownership");
        Equal(null, tracker.Poll(now.AddMilliseconds(280)), "exited Reform screen has no pending speech");
    }

    private static void ObserveHighResolutionFrame(PartyFormationSpeechTracker tracker, DateTime now)
    {
        tracker.ObserveDraw(new MenuTextRenderEntry("Reform", 508, 14, 7, 0), MenuModule, now);
        tracker.ObserveDraw(
            new MenuTextRenderEntry("Select with START button.", 26, 13, 7, TextContext),
            MenuModule,
            now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Cloud", 134, 77, 7, TextContext), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Barret", 134, 214, 7, TextContext), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Red XIII", 134, 351, 7, TextContext), MenuModule, now);
    }

    private static DateTime UtcNow() =>
        new(2026, 7, 31, 21, 14, 0, DateTimeKind.Utc);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
