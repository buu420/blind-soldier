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
        ReadsNormalPhsSelectionsInNativeModule();
        ReadsLowResolutionReformSelections();
        AnnouncesNativePartyValidationTransitionsOnce();
        PassiveInstructionDoesNotAlternateWithValidationOrStarveSelections();
        RequiresExactReformOwnershipAndResetsOnExit();
        OpensFromTheFramesTheGameActuallyDraws();
        NamesReserveCellsFromTheNativeRosterBecauseTheyAreDrawnAsPortraits();
        FollowsTheReserveCursorAcrossTheGrid();
        SpeaksWhenPhsIsOpenedFromTheWorldMap();
        SurvivesTheMainMenuLabelThatKeepsDrawingBehindPhs();
    }

    /// <summary>
    /// Verbatim replay of the 11:19:37Z runtime-log frame, which produced total
    /// silence. PHS has no module of its own, and opening it from the world map
    /// keeps module 3 — the old 5-or-19 gate discarded every draw and cursor.
    /// </summary>
    private static void SpeaksWhenPhsIsOpenedFromTheWorldMap()
    {
        const int worldMapModule = 3;
        var roster = new[] { "Tifa", "Red XIII", null, null, null, null, null, null, null };
        var tracker = new PartyFormationSpeechTracker(
            TimeSpan.FromMilliseconds(30),
            index => index >= 0 && index < roster.Length ? roster[index] : null);
        var now = UtcNow();
        ObserveLoggedWorldMapPhsFrame(tracker, worldMapModule, now);
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", worldMapModule, 326, 223, ReserveCursorContext),
            now.AddMilliseconds(1));

        Equal(
            true,
            tracker.IsActive(now.AddMilliseconds(10)),
            "PHS opened from the world map must be owned, not discarded by module id");
        Equal(
            "PHS. Available member, Tifa. Please make a party of three.",
            tracker.Poll(now.AddMilliseconds(80)),
            "PHS opened from the world map speaks the highlighted reserve member");
    }

    /// <summary>
    /// The selected main-menu label keeps rendering at 508,13 on every PHS frame.
    /// Treating that as an exit reset the screen once per frame, which is silence.
    /// </summary>
    private static void SurvivesTheMainMenuLabelThatKeepsDrawingBehindPhs()
    {
        const int worldMapModule = 3;
        var tracker = new PartyFormationSpeechTracker(TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        ObserveLoggedWorldMapPhsFrame(tracker, worldMapModule, now);
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", worldMapModule, 0, 120, PartyCursorContext),
            now.AddMilliseconds(1));
        tracker.Poll(now.AddMilliseconds(80));

        // Three more frames, each carrying the root-menu label again.
        var offset = 100;
        for (var frame = 0; frame < 3; frame++)
        {
            ObserveLoggedWorldMapPhsFrame(tracker, worldMapModule, now.AddMilliseconds(offset));
            Equal(
                true,
                tracker.IsActive(now.AddMilliseconds(offset + 5)),
                "the persistent main-menu label must not close the PHS screen");
            offset += 100;
        }

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", worldMapModule, 0, 257, PartyCursorContext),
            now.AddMilliseconds(offset));
        Equal(
            "Party slot 2, Barret.",
            tracker.Poll(now.AddMilliseconds(offset + 80)),
            "selections still speak after several frames of root-menu label draws");
    }

    /// <summary>One PHS frame exactly as the 11:19:37Z runtime log records it.</summary>
    private static void ObserveLoggedWorldMapPhsFrame(
        PartyFormationSpeechTracker tracker, int module, DateTime now)
    {
        tracker.ObserveDraw(
            new MenuTextRenderEntry("PHS", 508, 13, 7, 0x3A83126F), module, now);
        tracker.ObserveDraw(
            new MenuTextRenderEntry("Please make a party of three.", 27, 13, 7, TextContext),
            module,
            now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Tifa", 438, 68, 7, ReserveNameContext), module, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Cloud", 134, 77, 7, TextContext), module, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Barret", 134, 214, 7, TextContext), module, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Aeris", 134, 351, 7, TextContext), module, now);
    }

    /// <summary>
    /// The real PHS screen never draws a title with context 0. Replaying the
    /// frames from the runtime log verbatim must still open the screen; the old
    /// synthetic-title tests hid the fact that it never did.
    /// </summary>
    private static void OpensFromTheFramesTheGameActuallyDraws()
    {
        var tracker = new PartyFormationSpeechTracker(TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        ObserveNativePhsFrame(tracker, now);
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 0, 120, PartyCursorContext),
            now.AddMilliseconds(1));

        Equal(
            true,
            tracker.IsActive(now.AddMilliseconds(10)),
            "the frames the game actually draws must open the PHS screen");
        Equal(
            "PHS. Party slot 1, Cloud. Please make a party of three.",
            tracker.Poll(now.AddMilliseconds(80)),
            "PHS introduces itself from its native prompt without any context-0 title");
    }

    private static void NamesReserveCellsFromTheNativeRosterBecauseTheyAreDrawnAsPortraits()
    {
        var roster = new[] { "Tifa", "Red XIII", null, null, null, null, null, null, null };
        var tracker = new PartyFormationSpeechTracker(
            TimeSpan.FromMilliseconds(30),
            index => index >= 0 && index < roster.Length ? roster[index] : null);
        var now = UtcNow();
        ObserveNativePhsFrame(tracker, now);

        // No name text ever arrives for the reserve grid: the game blits a
        // portrait there, so the roster read is the only source.
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 326, 223, ReserveCursorContext),
            now.AddMilliseconds(100));
        Equal(
            "PHS. Available member, Tifa. Please make a party of three.",
            tracker.Poll(now.AddMilliseconds(180)),
            "the first reserve cell is named from the native roster, not from a text draw");

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 403, 223, ReserveCursorContext),
            now.AddMilliseconds(200));
        Equal(
            "Available member, Red XIII.",
            tracker.Poll(now.AddMilliseconds(280)),
            "the second reserve column is named from the native roster");

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 480, 223, ReserveCursorContext),
            now.AddMilliseconds(300));
        Equal(
            "Empty.",
            tracker.Poll(now.AddMilliseconds(380)),
            "an unfilled roster cell is announced as empty");
    }

    /// <summary>
    /// FUN_00700c90 indexes the roster as row * 3 + column, so grid geometry and
    /// roster order have to agree or the wrong member gets announced.
    /// </summary>
    private static void FollowsTheReserveCursorAcrossTheGrid()
    {
        var roster = new[]
        {
            "Tifa", "Red XIII", "Yuffie",
            "Cait Sith", "Vincent", "Cid",
            null, null, null
        };
        var seen = new List<string?>();
        var tracker = new PartyFormationSpeechTracker(
            TimeSpan.FromMilliseconds(30),
            index => index >= 0 && index < roster.Length ? roster[index] : null);
        var now = UtcNow();
        ObserveNativePhsFrame(tracker, now);

        var offset = 100;
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                tracker.ObserveCursor(
                    new MenuCursorDrawObservation(
                        "B", MenuModule, 326 + (column * 77), 223 + (row * 99), ReserveCursorContext),
                    now.AddMilliseconds(offset));
                seen.Add(tracker.Poll(now.AddMilliseconds(offset + 80)));
                offset += 100;
            }
        }

        Equal(9, seen.Count, "every reserve cell reports once");
        Equal(
            true,
            seen[0]!.Contains("Available member, Tifa.", StringComparison.Ordinal),
            "row 0 column 0 is Tifa");
        Equal("Available member, Yuffie.", seen[2], "row 0 column 2 is Yuffie");
        Equal("Available member, Cait Sith.", seen[3], "row 1 column 0 is Cait Sith");
        Equal("Available member, Cid.", seen[5], "row 1 column 2 is Cid");
        Equal("Empty.", seen[6], "row 2 column 0 has no member");
    }

    /// <summary>
    /// One PHS frame exactly as the runtime log records it: the prompt carries
    /// the party text context, and there is no context-0 title anywhere.
    /// </summary>
    private static void ObserveNativePhsFrame(PartyFormationSpeechTracker tracker, DateTime now)
    {
        tracker.ObserveDraw(
            new MenuTextRenderEntry("Please make a party of three.", 27, 13, 7, TextContext),
            MenuModule,
            now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Cloud", 134, 77, 7, TextContext), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Barret", 134, 214, 7, TextContext), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Aeris", 134, 351, 7, TextContext), MenuModule, now);
    }

    private static void ReadsNormalPhsSelectionsInNativeModule()
    {
        // Module 19 is quit/game-over, never PHS. Real sessions run under the
        // module that raised the menu; the world map keeps its own.
        const int phsModule = 3;
        var tracker = new PartyFormationSpeechTracker(TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        tracker.ObserveDraw(new MenuTextRenderEntry("PHS", 508, 14, 7, 0), phsModule, now);
        tracker.ObserveDraw(
            new MenuTextRenderEntry("Select with START button.", 26, 13, 7, TextContext),
            phsModule,
            now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Cloud", 134, 77, 7, TextContext), phsModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Barret", 134, 214, 7, TextContext), phsModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Tifa", 134, 351, 7, TextContext), phsModule, now);
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", phsModule, 0, 257, PartyCursorContext),
            now.AddMilliseconds(1));

        Equal(
            "PHS. Party slot 2, Barret. Press Start when finished.",
            tracker.Poll(now.AddMilliseconds(80)),
            "normal PHS module reads the checked active party slot");
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
            null,
            tracker.Poll(now.AddMilliseconds(240)),
            "returning to the continuously rendered instruction is not a status transition");
    }

    private static void PassiveInstructionDoesNotAlternateWithValidationOrStarveSelections()
    {
        var tracker = new PartyFormationSpeechTracker(TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        tracker.ObserveDraw(new MenuTextRenderEntry("Reform", 508, 14, 7, 0), MenuModule, now);
        tracker.ObserveDraw(
            new MenuTextRenderEntry("Select with Menu button.", 26, 13, 7, TextContext),
            MenuModule,
            now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Cloud", 134, 77, 7, TextContext), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Barret", 134, 214, 7, TextContext), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Red XIII", 134, 351, 7, TextContext), MenuModule, now);
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 0, 120, PartyCursorContext),
            now.AddMilliseconds(1));
        Equal(
            "Reform. Party slot 1, Cloud. Select with Menu button.",
            tracker.Poll(now.AddMilliseconds(80)),
            "translated x64 instruction is included in the Reform introduction");

        tracker.ObserveDraw(
            new MenuTextRenderEntry("Please make a party of three.", 26, 13, 7, TextContext),
            MenuModule,
            now.AddMilliseconds(100));
        Equal(
            "Please make a party of three.",
            tracker.Poll(now.AddMilliseconds(140)),
            "native validation is announced once");

        tracker.ObserveDraw(
            new MenuTextRenderEntry("Select with Menu button.", 26, 13, 7, TextContext),
            MenuModule,
            now.AddMilliseconds(150));
        Equal(
            null,
            tracker.Poll(now.AddMilliseconds(190)),
            "the continuously rendered selection instruction does not interrupt speech");

        tracker.ObserveDraw(
            new MenuTextRenderEntry("Please make a party of three.", 26, 13, 7, TextContext),
            MenuModule,
            now.AddMilliseconds(200));
        Equal(
            null,
            tracker.Poll(now.AddMilliseconds(240)),
            "the same validation does not re-arm through the passive instruction");

        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 0, 257, PartyCursorContext),
            now.AddMilliseconds(250));
        Equal(
            "Party slot 2, Barret.",
            tracker.Poll(now.AddMilliseconds(330)),
            "member and slot speech remains available after the validation prompt");
    }

    private static void RequiresExactReformOwnershipAndResetsOnExit()
    {
        var tracker = new PartyFormationSpeechTracker(TimeSpan.FromMilliseconds(30));
        var now = UtcNow();

        // A party-slot name draw on its own is layout evidence, not the screen.
        tracker.ObserveDraw(new MenuTextRenderEntry("Cloud", 134, 77, 7, TextContext), MenuModule, now);
        Equal(false, tracker.IsActive(now.AddMilliseconds(10)), "a name draw alone does not open PHS");
        Equal(null, tracker.Poll(now.AddMilliseconds(80)), "a name draw alone remains silent");

        // A cursor outside the party modules is not this screen either.
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", 20, 0, 120, PartyCursorContext),
            now.AddMilliseconds(85));
        Equal(
            false,
            tracker.IsActive(now.AddMilliseconds(90)),
            "a party cursor outside the party modules does not open PHS");

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
            new MenuCursorDrawObservation("B", 20, 0, 120, PartyCursorContext),
            now.AddMilliseconds(200));
        Equal(false, tracker.IsActive(now.AddMilliseconds(200)), "leaving both party modules releases Reform ownership");
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
