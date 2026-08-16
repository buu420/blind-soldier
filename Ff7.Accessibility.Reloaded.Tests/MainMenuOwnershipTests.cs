using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class MainMenuOwnershipTests
{
    internal static void Run()
    {
        StaleMainMenuCannotOwnBattleResults();
        ExactOrIncoherentShopOwnershipBlocksMainMenu();
        MainMenuReacquiresAfterOwnershipLoss();
    }

    private static void StaleMainMenuCannotOwnBattleResults()
    {
        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 17,
                shopOwnershipRead: true,
                ownsShop: false,
                saveMenuOwnsSpeech: false),
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
                saveMenuOwnsSpeech: false),
            "exact shop ownership must exclude stale main-menu state");
        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 5,
                shopOwnershipRead: false,
                ownsShop: false,
                saveMenuOwnsSpeech: false),
            "incoherent shop ownership must fail closed");
        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(
                currentModule: 5,
                shopOwnershipRead: true,
                ownsShop: false,
                saveMenuOwnsSpeech: true),
            "save-menu ownership must remain exclusive");
    }

    private static void MainMenuReacquiresAfterOwnershipLoss()
    {
        var scheduler = new MainMenuSpeechScheduler(TimeSpan.Zero);
        var now = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

        Equal(
            true,
            MainMenuSpeechOwnership.CanRead(5, true, false, false),
            "root menu ownership is accepted");
        Equal("Magic", scheduler.Observe("Magic", now), "root menu speaks its selection");

        Equal(
            false,
            MainMenuSpeechOwnership.CanRead(17, true, false, false),
            "ownership loss blocks stale selection");
        scheduler.Observe(string.Empty, now.AddMilliseconds(50));

        Equal(
            true,
            MainMenuSpeechOwnership.CanRead(5, true, false, false),
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
