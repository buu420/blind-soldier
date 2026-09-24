using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

/// <summary>
/// The x64 side of the shared gil readout. The runtime dispatcher speaks the
/// root selection, so the opening balance waits until a main-menu frame has
/// been dispatched with menu speech able to run; it then follows that
/// selection instead of being interrupted by it.
/// </summary>
internal sealed class Steam2026MenuGilReadout
{
    internal const string MainMenuScreen = "Main Menu";

    private readonly MenuGilReadoutController controller = new();
    private bool mainMenuSelectionDelivered;

    /// <param name="menu">The menu update this iteration handed the dispatcher.</param>
    /// <param name="menuSpeechDispatched">
    /// The dispatcher completed with speech and runtime menu speech enabled and
    /// the frame in the foreground, so a changed selection was spoken.
    /// </param>
    internal void ObserveDispatchedMenu(
        RuntimeDomainUpdate<MenuFrameObservation> menu,
        bool menuSpeechDispatched)
    {
        switch (menu.Kind)
        {
            case RuntimeDomainUpdateKind.Closed:
                mainMenuSelectionDelivered = false;
                break;
            case RuntimeDomainUpdateKind.Present:
                if (menu.Value is not { IsOpen: true, Screen: MainMenuScreen })
                {
                    mainMenuSelectionDelivered = false;
                }
                else if (menuSpeechDispatched)
                {
                    mainMenuSelectionDelivered = true;
                }

                break;
        }
    }

    internal MenuGilReadoutResult Tick(
        bool repeatPressed,
        bool? mainMenuSessionOpen,
        bool automaticAnnouncementAllowed,
        Func<MenuGilScreen> readVisibleScreen,
        Func<uint?> readGil,
        Func<string, bool, bool> speak) =>
        controller.Tick(
            repeatPressed,
            mainMenuSessionOpen,
            automaticAnnouncementAllowed && mainMenuSelectionDelivered,
            readVisibleScreen,
            readGil,
            speak);

    internal void Reset()
    {
        controller.Reset();
        mainMenuSelectionDelivered = false;
    }
}
