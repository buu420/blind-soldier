using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Materia and Limit notices a sighted player sees on the battle-results EXP
/// page. Layout, text and timing are the shipped engine's (Ghidra: legacy
/// ff7_en.exe and the Steam 2026 translation, which keeps the same constants).
/// FUN_006c7b54 places party row s at y = 120 * (s + 1), draws the character's
/// name at (0x8D, row + 0x18) and opens a notice box at (0xFB, row + 9).
/// FUN_006c8643 draws a levelled Materia's name at box + (0x28, 0x0B) and
/// "LEVEL UP" (0x0091AA50) at box + (0x5D, 0x27); FUN_006c87bb draws kernel2
/// battle text 0x24, " gained.", at box + (0x4E, 0x27) and then the learned
/// Limit's name at box + (0x0D, 0x0B). FUN_006c86f6 draws battle text 0x25,
/// " was born.", at (0xD8 + 0x35, 0x1A + 0x27) and the new Materia's name at
/// (0xD8 + 0x28, 0x1A + 0x0B). A row notice is drawn on exactly 0x50 updates and
/// the row's next notice starts on the following update; a born notice is drawn
/// on 0x96 updates. The character "LEVEL UP" box (FUN_006c85f1) sits at
/// (0x3A + 0x0D, row + 0x40 + 0x0D).
/// </summary>
internal static class BattleResultsNotificationTests
{
    private const int RowNoticeUpdates = 0x50;
    private const int BornNoticeUpdates = 0x96;
    private const uint CloudRow = 120;
    private const uint BarretRow = 240;
    private const uint CidRow = 360;

    private static readonly BattlePartyProgressSnapshot[] Party =
    [
        new(0, 0, "Cloud", 58),
        new(1, 1, "Barret", 53),
        new(2, 8, "Cid", 52)
    ];

    internal static void Run()
    {
        SpeaksAMateriaLevelUpOnceForItsWholePresentation();
        SpeaksALearnedLimitWithItsCharacter();
        SpeaksANewlyBornMateria();
        SpeaksIdenticalNoticesInDifferentRowsSeparately();
        DistinguishesRowsWithTheSameCharacterName();
        SpeaksBackToBackIdenticalNoticesInOneRowSeparately();
        KeepsANoticeOneEventWhileThePageSlidesAway();
        DoesNotRepeatANoticeAfterAMissedUpdate();
        WaitsForTheRowOwnerBeforeSpeakingARowNotice();
        RejectsNamesAndLabelsFromDifferentDrawContexts();
        SpeaksTheSameNoticeAgainInTheNextBattle();
        Console.WriteLine("Battle results Materia and Limit notice tests passed.");
    }

    private static void SpeaksAMateriaLevelUpOnceForItsWholePresentation()
    {
        var tracker = CreateTrackerOnExperiencePage();
        for (var update = 0; update < RowNoticeUpdates; update++)
        {
            Update(
                tracker,
                [
                    .. CharacterLevelUp(CloudRow),
                    .. MateriaNotice("Fire", CloudRow),
                    .. Row("Cloud", CloudRow),
                    .. Row("Barret", BarretRow),
                    .. Row("Cid", CidRow)
                ]);
        }

        Equal("Cloud. Fire. LEVEL UP", tracker.Poll(), "visible Materia level-up with its row");
        Null(tracker.Poll(), "one native Materia notice is one announcement across its 0x50 updates");
    }

    private static void SpeaksALearnedLimitWithItsCharacter()
    {
        var tracker = CreateTrackerOnExperiencePage();
        for (var update = 0; update < 3; update++)
        {
            Update(
                tracker,
                [
                    .. LimitNotice("Cross-Slash", CloudRow),
                    .. Row("Cloud", CloudRow),
                    .. Row("Barret", BarretRow)
                ]);
        }

        Equal("Cloud. Cross-Slash gained.", tracker.Poll(), "visible learned Limit reads as one sentence");
        Null(tracker.Poll(), "learned Limit notice does not repeat");
    }

    private static void SpeaksANewlyBornMateria()
    {
        var tracker = CreateTrackerOnExperiencePage();
        for (var update = 0; update < BornNoticeUpdates; update++)
        {
            Update(
                tracker,
                [
                    .. BornNotice("Fire"),
                    .. Header(),
                    .. Row("Cloud", CloudRow)
                ]);
        }

        Equal("Fire was born.", tracker.Poll(), "visible new Materia notice");
        Null(tracker.Poll(), "one born notice is one announcement across its 0x96 updates");
    }

    private static void SpeaksIdenticalNoticesInDifferentRowsSeparately()
    {
        var tracker = CreateTrackerOnExperiencePage();
        for (var update = 0; update < 4; update++)
        {
            Update(
                tracker,
                [
                    .. MateriaNotice("All", CloudRow),
                    .. Row("Cloud", CloudRow),
                    .. MateriaNotice("All", BarretRow),
                    .. Row("Barret", BarretRow),
                    .. Row("Cid", CidRow)
                ]);
        }

        Equal("Cloud. All. LEVEL UP", tracker.Poll(), "first row's notice");
        Equal("Barret. All. LEVEL UP", tracker.Poll(), "identical notice in another row is another event");
        Null(tracker.Poll(), "two simultaneous notices are two announcements");
    }

    private static void DistinguishesRowsWithTheSameCharacterName()
    {
        var tracker = CreateTrackerOnExperiencePage();
        Update(tracker,
        [
            .. MateriaNotice("Fire", CloudRow),
            .. MateriaNotice("Fire", BarretRow),
            .. Row("Cloud", CloudRow),
            .. Row("Cloud", BarretRow)
        ]);

        Equal("Cloud. Fire. LEVEL UP", tracker.Poll(), "first native row with the shared name");
        Equal("Cloud. Fire. LEVEL UP", tracker.Poll(), "second native row with the shared name");
        Null(tracker.Poll(), "two awards in two rows");
    }

    private static void SpeaksBackToBackIdenticalNoticesInOneRowSeparately()
    {
        var tracker = CreateTrackerOnExperiencePage();
        foreach (var notice in new[] { "Restore", "Restore", "Ice" })
        {
            for (var update = 0; update < RowNoticeUpdates; update++)
            {
                Update(tracker, [.. MateriaNotice(notice, CloudRow), .. Row("Cloud", CloudRow)]);
            }
        }

        for (var update = 0; update < RowNoticeUpdates; update++)
        {
            Update(tracker, [.. LimitNotice("Cross-Slash", CloudRow), .. Row("Cloud", CloudRow)]);
        }

        Equal("Cloud. Restore. LEVEL UP", tracker.Poll(), "first of two equipped Restore Materia");
        Equal("Cloud. Restore. LEVEL UP", tracker.Poll(), "second Restore starts on the update after the first ends");
        Equal("Cloud. Ice. LEVEL UP", tracker.Poll(), "next Materia in the row");
        Equal("Cloud. Cross-Slash gained.", tracker.Poll(), "the row's Limit notice follows its Materia notices");
        Null(tracker.Poll(), "four native row notices are four announcements");
    }

    private static void KeepsANoticeOneEventWhileThePageSlidesAway()
    {
        var tracker = CreateTrackerOnExperiencePage();
        for (var update = 0; update < 30; update++)
        {
            Update(tracker, [.. MateriaNotice("Fire", CloudRow), .. Row("Cloud", CloudRow)]);
        }

        // Confirm turns the slide factor from 20 toward 100 by 3 per update, and
        // FUN_006c7b54 places the row at (s + 1) * 120 * factor / 20.
        for (var factor = 23u; factor <= 50; factor += 3)
        {
            var row = CloudRow * factor / 20;
            Update(tracker, [.. MateriaNotice("Fire", row), .. Row("Cloud", row)]);
        }

        Equal("Cloud. Fire. LEVEL UP", tracker.Poll(), "notice before the page slides");
        Null(tracker.Poll(), "a notice moving with its row is still one event");
    }

    private static void DoesNotRepeatANoticeAfterAMissedUpdate()
    {
        var tracker = CreateTrackerOnExperiencePage();
        for (var update = 0; update < 30; update++)
        {
            Update(tracker, [.. MateriaNotice("Restore", CloudRow), .. Row("Cloud", CloudRow)]);
        }

        Update(tracker, [.. Row("Cloud", CloudRow)]);
        for (var update = 31; update < RowNoticeUpdates; update++)
        {
            Update(tracker, [.. MateriaNotice("Restore", CloudRow), .. Row("Cloud", CloudRow)]);
        }

        Equal("Cloud. Restore. LEVEL UP", tracker.Poll(), "notice with one unseen update");
        Null(tracker.Poll(), "an update without the capture does not start a new event");

        for (var update = 0; update < RowNoticeUpdates; update++)
        {
            Update(tracker, [.. MateriaNotice("Restore", CloudRow), .. Row("Cloud", CloudRow)]);
        }

        Equal("Cloud. Restore. LEVEL UP", tracker.Poll(), "the row's next identical notice is still heard");
        Null(tracker.Poll(), "and only once");
    }

    private static void WaitsForTheRowOwnerBeforeSpeakingARowNotice()
    {
        var tracker = CreateTrackerOnExperiencePage();
        Update(tracker, [.. MateriaNotice("Fire", CloudRow)]);
        Null(tracker.Poll(), "a row notice without its row's character is not guessed");

        Update(tracker, [.. MateriaNotice("Fire", CloudRow), .. Row("Cloud", CloudRow)]);
        Equal("Cloud. Fire. LEVEL UP", tracker.Poll(), "notice once its row is visible");
        Update(tracker, [.. MateriaNotice("Fire", CloudRow), .. Row("Cloud", CloudRow)]);
        Null(tracker.Poll(), "row notice does not repeat");
    }

    private static void RejectsNamesAndLabelsFromDifferentDrawContexts()
    {
        var tracker = CreateTrackerOnExperiencePage();
        Update(
            tracker,
            [
                Text("Ice", 291, CloudRow + 20, context: 0x11111111),
                Text("LEVEL UP", 344, CloudRow + 48, color: 6, context: 0x22222222),
                .. Row("Cloud", CloudRow)
            ]);
        Null(tracker.Poll(), "name and label from different native draw contexts must not pair");
    }

    private static void SpeaksTheSameNoticeAgainInTheNextBattle()
    {
        var tracker = CreateTrackerOnExperiencePage();
        Update(tracker, [.. MateriaNotice("Fire", CloudRow), .. Row("Cloud", CloudRow)]);
        Equal("Cloud. Fire. LEVEL UP", tracker.Poll(), "first battle notice");

        tracker.ObserveBattleProgress(Party);
        tracker.ObserveResults(new BattleResultsSnapshot(true, 0, 120, 12, 80, []), Party);
        Equal("120 experience. 12 AP.", tracker.Poll(), "next battle's experience page");
        Update(tracker, [.. MateriaNotice("Fire", CloudRow), .. Row("Cloud", CloudRow)]);
        Equal("Cloud. Fire. LEVEL UP", tracker.Poll(), "a new results screen presents a new notice");
    }

    private static BattleResultsSpeechTracker CreateTrackerOnExperiencePage()
    {
        var tracker = new BattleResultsSpeechTracker();
        tracker.ObserveBattleProgress(Party);
        tracker.ObserveResults(new BattleResultsSnapshot(true, 0, 1400, 160, 2600, []), Party);
        Equal("1400 experience. 160 AP.", tracker.Poll(), "experience page summary precedes its notices");
        return tracker;
    }

    private static void Update(BattleResultsSpeechTracker tracker, MenuTextRenderEntry[] draws)
    {
        tracker.BeginFrame();
        foreach (var draw in draws)
        {
            tracker.ObserveDraw(draw);
        }

        tracker.CompleteFrame();
    }

    private static MenuTextRenderEntry[] Header() =>
    [
        Text("Gained EXP and AP.", 27, 26),
        Text("EXP", 57, 82),
        Text("AP", 402, 82)
    ];

    private static MenuTextRenderEntry[] Row(string character, uint row) =>
    [
        Text("EXP:", 377, row + 19),
        Text("Level:", 178, row + 73),
        Text("next level:", 307, row + 73),
        Text(character, 141, row + 24)
    ];

    private static MenuTextRenderEntry[] CharacterLevelUp(uint row) =>
        [Text("LEVEL UP", 71, row + 77, color: 6)];

    private static MenuTextRenderEntry[] MateriaNotice(string materia, uint row) =>
    [
        Text(materia, 291, row + 20),
        Text("LEVEL UP", 344, row + 48, color: 6)
    ];

    private static MenuTextRenderEntry[] LimitNotice(string limit, uint row) =>
    [
        Text(" gained.", 329, row + 48),
        Text(limit, 264, row + 20, color: 6)
    ];

    private static MenuTextRenderEntry[] BornNotice(string materia) =>
    [
        Text(" was born.", 269, 65),
        Text(materia, 256, 37)
    ];

    private static MenuTextRenderEntry Text(
        string text,
        uint x,
        uint y,
        int color = 7,
        int context = 0) =>
        new(text, x, y, color, context);

    private static void Equal(string expected, string? actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual ?? "<null>"}'.");
        }
    }

    private static void Null(string? actual, string label)
    {
        if (actual is not null)
        {
            throw new InvalidOperationException($"{label}: expected no speech, got '{actual}'.");
        }
    }
}
