using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class MainMenuOwnershipTests
{
    internal static void Run()
    {
        ExactRenderedRootMenuOwnsFieldAndWorldMapOverlays();
        RootMenuRenderEvidenceRequiresTheNativeRowPattern();
        RepeatedSubmenuTextCannotProveRootMenu();
        StaleMainMenuCannotOwnBattleResults();
        ExactOrIncoherentShopOwnershipBlocksMainMenu();
        MainMenuReacquiresAfterOwnershipLoss();
    }

    private static void ExactRenderedRootMenuOwnsFieldAndWorldMapOverlays()
    {
        Equal(
            true,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 1,
                shopOwnershipRead: false,
                ownsShop: false,
                saveMenuOwnsSpeech: false,
                rootMenuRecentlyRendered: true),
            "a visibly rendered root menu must own speech over a field");
        Equal(
            true,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 3,
                shopOwnershipRead: false,
                ownsShop: false,
                saveMenuOwnsSpeech: false,
                rootMenuRecentlyRendered: true),
            "a visibly rendered root menu must own speech over the world map");
        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 3,
                shopOwnershipRead: false,
                ownsShop: false,
                saveMenuOwnsSpeech: false,
                rootMenuRecentlyRendered: false),
            "stale world-map main-menu globals cannot own speech without live rendering");
    }

    private static void RootMenuRenderEvidenceRequiresTheNativeRowPattern()
    {
        var now = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var tracker = new RootMainMenuRenderEvidenceTracker(TimeSpan.FromMilliseconds(300));

        for (var row = 0; row < 4; row++)
        {
            tracker.Observe(
                new MenuTextRenderEntry($"row {row}", 508, (uint)(193 + (row * 26)), 7, 0x3A83126F),
                now.AddMilliseconds(row));
        }

        Equal(false, tracker.IsActive(now.AddMilliseconds(4)), "four root-context rows are not enough");
        tracker.Observe(
            new MenuTextRenderEntry("row 4", 508, 297, 7, 0x3A83126F),
            now.AddMilliseconds(4));
        Equal(true, tracker.IsActive(now.AddMilliseconds(5)), "five native rows prove the root menu is rendered");
        Equal(false, tracker.IsActive(now.AddMilliseconds(305)), "render evidence expires after callbacks stop");

        var unrelated = new RootMainMenuRenderEvidenceTracker(TimeSpan.FromMilliseconds(300));
        for (var row = 0; row < 5; row++)
        {
            unrelated.Observe(
                new MenuTextRenderEntry($"row {row}", 508, (uint)(193 + (row * 26)), 7, 0x3DCCCCCD),
                now.AddMilliseconds(row));
        }

        Equal(false, unrelated.IsActive(now.AddMilliseconds(5)), "a submenu context cannot prove root-menu ownership");
    }

    private static void RepeatedSubmenuTextCannotProveRootMenu()
    {
        var now = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var tracker = new RootMainMenuRenderEvidenceTracker(TimeSpan.FromMilliseconds(300));
        for (var row = 0; row < 5; row++)
        {
            tracker.Observe(
                new MenuTextRenderEntry("Limit", 508, (uint)(193 + (row * 26)), 7, 0x3A83126F),
                now.AddMilliseconds(row));
        }

        Equal(
            false,
            tracker.IsActive(now.AddMilliseconds(5)),
            "one animated submenu label repeated down the screen cannot prove the root menu");
    }

    private static void StaleMainMenuCannotOwnBattleResults()
    {
        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 17,
                shopOwnershipRead: true,
                ownsShop: false,
                saveMenuOwnsSpeech: false,
                rootMenuRecentlyRendered: true),
            "battle-results module must reject plausible stale main-menu state");
    }

    private static void ExactOrIncoherentShopOwnershipBlocksMainMenu()
    {
        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 5,
                shopOwnershipRead: true,
                ownsShop: true,
                saveMenuOwnsSpeech: false,
                rootMenuRecentlyRendered: true),
            "exact shop ownership must exclude stale main-menu state");
        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 5,
                shopOwnershipRead: false,
                ownsShop: false,
                saveMenuOwnsSpeech: false,
                rootMenuRecentlyRendered: true),
            "incoherent shop ownership must fail closed");
        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 5,
                shopOwnershipRead: true,
                ownsShop: false,
                saveMenuOwnsSpeech: true,
                rootMenuRecentlyRendered: true),
            "save-menu ownership must remain exclusive");
        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 5,
                shopOwnershipRead: true,
                ownsShop: false,
                saveMenuOwnsSpeech: false,
                rootMenuRecentlyRendered: false),
            "a shop transition cannot expose stale main-menu state without live root-menu rendering");
    }

    private static void MainMenuReacquiresAfterOwnershipLoss()
    {
        var scheduler = new MainMenuSpeechScheduler(TimeSpan.Zero);
        var now = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

        Equal(
            true,
            MainMenuSpeechOwnership.CanRead(5, true, false, false, true),
            "root menu ownership is accepted");
        Equal("Magic", scheduler.Observe("Magic", now), "root menu speaks its selection");

        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(17, true, false, false, true),
            "ownership loss blocks stale selection");
        scheduler.Observe(string.Empty, now.AddMilliseconds(50));

        Equal(
            true,
            MainMenuSpeechOwnership.CanRead(5, true, false, false, true),
            "root menu ownership reacquires");
        Equal(
            "Magic",
            scheduler.Observe("Magic", now.AddMilliseconds(100)),
            "root menu repeats its current selection after reacquiring ownership");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
