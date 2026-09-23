using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// A native screen that is drawing the party's current gil at this moment.
/// </summary>
public enum MenuGilScreen
{
    None,
    MainMenu,
    Shop
}

public enum MenuGilReadoutOutcome
{
    None,
    Announced,
    Repeated,
    RepeatNotVisible,
    ReadFailed,
    SpeechRejected
}

public readonly record struct MenuGilReadoutResult(
    MenuGilReadoutOutcome Outcome,
    MenuGilScreen Screen,
    string? Text)
{
    public static MenuGilReadoutResult None { get; } =
        new(MenuGilReadoutOutcome.None, MenuGilScreen.None, null);
}

/// <summary>
/// Reads the balance FFVII draws on its menus, and the main menu's own
/// open/close phase so an opening can be told apart from a submenu return.
/// </summary>
public sealed class MenuGilStateReader
{
    /// <summary>
    /// Savemap offset 0x0B7C. FUN_006CA346 (main menu) and FUN_0071AAA3
    /// (shops) both draw this unsigned value directly.
    /// </summary>
    public const int AddressGil = ShopMenuStateReader.AddressGil;

    /// <summary>
    /// FUN_006CB56A sets this when it starts a main-menu session and clears it
    /// once the menu has finished closing. Submenus run inside the session.
    /// </summary>
    public const int AddressMainMenuSession = 0x00DC12F0;

    /// <summary>
    /// FUN_006CA346 slide phase: 0 opening, 1 open, 2 closing, -1 closed.
    /// </summary>
    public const int AddressMainMenuPhase = 0x00DC1298;

    private readonly ILegacyAddressSpace memory;

    public MenuGilStateReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>
    /// Two equal reads, so a balance changing mid-read is never spoken. Zero
    /// is a real balance; a failed or torn read returns false instead.
    /// </summary>
    public bool TryReadGil(out uint gil)
    {
        gil = 0;
        if (!memory.TryReadUInt32((uint)AddressGil, out var candidate) ||
            !memory.TryReadUInt32((uint)AddressGil, out var bookend) ||
            candidate != bookend)
        {
            return false;
        }

        gil = candidate;
        return true;
    }

    /// <summary>
    /// Whether a main-menu session is opening or open. Closing, closed and a
    /// driver that has not started all end the session; a torn or unreadable
    /// pair answers nothing.
    /// </summary>
    public bool TryReadMainMenuSessionOpen(out bool open)
    {
        open = false;
        if (!TryReadSession(out var candidate) ||
            !TryReadSession(out var bookend) ||
            candidate != bookend)
        {
            return false;
        }

        open = candidate.Session == 1 && candidate.Phase is 0 or 1;
        return true;
    }

    /// <summary>
    /// The root screen draws its Time and Gil box on every frame that it draws
    /// the full command column: state 1 with target 0, which is root renderer
    /// FUN_006CA346 at index 0 of table 0091AB98. Submenus, the column's
    /// transitions and the separate Quit dialog do not count, even while an
    /// older root render lease is still active.
    /// </summary>
    public static bool IsMainMenuBalanceVisible(
        bool rootMenuOwned,
        MainMenuSnapshot? snapshot,
        bool quitConfirmationVisible) =>
        rootMenuOwned &&
        !quitConfirmationVisible &&
        snapshot is { State: 1, Target: 0 } root &&
        MainMenuStateReader.TryCreateSelection(root, out _);

    public static MenuGilScreen ResolveScreen(
        bool shopBalanceVisible,
        bool mainMenuBalanceVisible) =>
        shopBalanceVisible
            ? MenuGilScreen.Shop
            : mainMenuBalanceVisible
                ? MenuGilScreen.MainMenu
                : MenuGilScreen.None;

    private bool TryReadSession(out (int Session, int Phase) state)
    {
        state = default;
        if (!memory.TryReadInt32((uint)AddressMainMenuSession, out var session) ||
            !memory.TryReadInt32((uint)AddressMainMenuPhase, out var phase))
        {
            return false;
        }

        state = (session, phase);
        return true;
    }
}

/// <summary>
/// Speaks the balance once when the main menu opens and repeats it on G while
/// a screen that draws it is up. Every spoken value is read at the moment it is
/// spoken; nothing is cached, so a balance that changed after a purchase is
/// never repeated from before it.
/// </summary>
public sealed class MenuGilReadoutController
{
    public const int VirtualKeyG = 0x47;

    private bool mainMenuAnnounced;

    public static string Format(uint gil) => $"{gil} gil.";

    /// <param name="repeatPressed">A foreground rising edge of G.</param>
    /// <param name="mainMenuSessionOpen">
    /// The native session phase, or null when it could not be read. Only an
    /// observed close ends a session.
    /// </param>
    /// <param name="announceOpening">
    /// True once the root's first selection has been delivered, so the balance
    /// follows it instead of being cut off by it.
    /// </param>
    /// <param name="readVisibleScreen">Read only when needed, fresh.</param>
    /// <param name="readGil">Read only when needed, fresh; null on failure.</param>
    /// <param name="speak">Text and interrupt; true when delivered.</param>
    public MenuGilReadoutResult Tick(
        bool repeatPressed,
        bool? mainMenuSessionOpen,
        bool announceOpening,
        Func<MenuGilScreen> readVisibleScreen,
        Func<uint?> readGil,
        Func<string, bool, bool> speak)
    {
        ArgumentNullException.ThrowIfNull(readVisibleScreen);
        ArgumentNullException.ThrowIfNull(readGil);
        ArgumentNullException.ThrowIfNull(speak);
        if (mainMenuSessionOpen == false)
        {
            mainMenuAnnounced = false;
        }

        if (repeatPressed)
        {
            return Repeat(mainMenuSessionOpen, readVisibleScreen, readGil, speak);
        }

        if (!announceOpening ||
            mainMenuSessionOpen != true ||
            mainMenuAnnounced ||
            readVisibleScreen() != MenuGilScreen.MainMenu ||
            readGil() is not { } gil ||
            readVisibleScreen() != MenuGilScreen.MainMenu)
        {
            return MenuGilReadoutResult.None;
        }

        var text = Format(gil);
        if (!speak(text, false))
        {
            return new MenuGilReadoutResult(
                MenuGilReadoutOutcome.SpeechRejected,
                MenuGilScreen.MainMenu,
                text);
        }

        mainMenuAnnounced = true;
        return new MenuGilReadoutResult(
            MenuGilReadoutOutcome.Announced,
            MenuGilScreen.MainMenu,
            text);
    }

    public void Reset() => mainMenuAnnounced = false;

    private MenuGilReadoutResult Repeat(
        bool? mainMenuSessionOpen,
        Func<MenuGilScreen> readVisibleScreen,
        Func<uint?> readGil,
        Func<string, bool, bool> speak)
    {
        var screen = readVisibleScreen();
        if (screen == MenuGilScreen.None ||
            (screen == MenuGilScreen.MainMenu && mainMenuSessionOpen != true))
        {
            return new MenuGilReadoutResult(
                MenuGilReadoutOutcome.RepeatNotVisible,
                screen,
                null);
        }

        if (readGil() is not { } gil)
        {
            return new MenuGilReadoutResult(MenuGilReadoutOutcome.ReadFailed, screen, null);
        }

        // The menu may have closed or changed while the balance was sampled.
        // The callback reads current ownership again instead of reusing a lease.
        if (readVisibleScreen() != screen)
        {
            return new MenuGilReadoutResult(MenuGilReadoutOutcome.RepeatNotVisible, screen, null);
        }

        var text = Format(gil);
        if (!speak(text, true))
        {
            return new MenuGilReadoutResult(MenuGilReadoutOutcome.SpeechRejected, screen, text);
        }

        // Asking for the balance on the root answers the opening announcement too.
        if (screen == MenuGilScreen.MainMenu)
        {
            mainMenuAnnounced = true;
        }

        return new MenuGilReadoutResult(MenuGilReadoutOutcome.Repeated, screen, text);
    }
}
