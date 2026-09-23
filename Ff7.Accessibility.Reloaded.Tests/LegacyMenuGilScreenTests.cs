using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// x86 only: the legacy runtime's one fresh set of reads that decides which
/// native screen may answer G, using the same ownership as root-menu speech.
/// </summary>
internal static class LegacyMenuGilScreenTests
{
    internal static void Run()
    {
        RenderedRootMenuShowsTheBalanceOverFieldWorldAndMenuModules();
        ShopWalletNeedsTheShopModule();
        StaleOrForeignOwnershipCannotExposeTheRootBalance();
    }

    private static void RenderedRootMenuShowsTheBalanceOverFieldWorldAndMenuModules()
    {
        Equal(MenuGilScreen.MainMenu, Resolve(module: 1), "root menu over a field");
        Equal(MenuGilScreen.MainMenu, Resolve(module: 3), "root menu over the world map");
        Equal(MenuGilScreen.MainMenu, Resolve(module: 5, shopOwnershipRead: true), "root menu in the menu module");
    }

    private static void ShopWalletNeedsTheShopModule()
    {
        Equal(MenuGilScreen.Shop,
            Resolve(module: 5, shopBalanceVisible: true, shopOwnershipRead: true, ownsShop: true, rootRendered: false),
            "shop screen drawing the wallet");
        Equal(MenuGilScreen.None,
            Resolve(module: 5, shopOwnershipRead: true, ownsShop: true, rootRendered: false),
            "shop Buy/Sell/Exit and sale list draw no wallet");
        Equal(MenuGilScreen.None,
            Resolve(module: 1, shopBalanceVisible: true, rootRendered: false),
            "stale shop visibility outside the shop module");
    }

    private static void StaleOrForeignOwnershipCannotExposeTheRootBalance()
    {
        Equal(MenuGilScreen.None, Resolve(module: 1, rootRendered: false), "root globals without live rendering");
        Equal(MenuGilScreen.None, Resolve(module: 1, saveMenuOwnsSpeech: true), "Save menu owns the screen");
        Equal(MenuGilScreen.None, Resolve(module: 2), "battle cannot reuse root-menu state");
        Equal(MenuGilScreen.None, Resolve(module: 5, shopOwnershipRead: false), "incoherent shop ownership");
        Equal(MenuGilScreen.None, Resolve(module: 5, shopOwnershipRead: true, ownsShop: true), "an owned shop is not the root");
        Equal(MenuGilScreen.None, Resolve(module: 1, rootSnapshot: Root() with { Target = 2 }),
            "Materia submenu under an old render lease");
        Equal(MenuGilScreen.None, Resolve(module: 1, rootSnapshot: Root() with { State = 0 }), "inside a submenu");
        Equal(MenuGilScreen.None, Resolve(module: 1, rootReadable: false), "unreadable main menu");
        Equal(MenuGilScreen.None, Resolve(module: 1, quitVisible: true), "Quit dialog");
    }

    private static MenuGilScreen Resolve(
        byte module,
        bool shopBalanceVisible = false,
        bool shopOwnershipRead = false,
        bool ownsShop = false,
        bool saveMenuOwnsSpeech = false,
        bool rootRendered = true,
        bool rootReadable = true,
        MainMenuSnapshot? rootSnapshot = null,
        bool quitVisible = false) =>
        MainMenuSpeechOwnership.ResolveGilScreen(
            module,
            shopBalanceVisible,
            shopOwnershipRead,
            ownsShop,
            saveMenuOwnsSpeech,
            rootRendered,
            rootReadable ? rootSnapshot ?? Root() : null,
            quitVisible);

    private static MainMenuSnapshot Root() =>
        new(1, 0, 0, 0, 0, 1, 0x7ff, 0, 0);

    private static void Equal<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{context}: expected {expected}, got {actual}");
        }
    }
}
