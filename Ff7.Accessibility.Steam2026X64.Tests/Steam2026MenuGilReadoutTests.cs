using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;

/// <summary>
/// x64 only: the dispatcher speaks the root selection, the balance follows it,
/// and the pump's fresh reads decide which native screen may answer G.
/// </summary>
internal static class Steam2026MenuGilReadoutTests
{
    internal static void Run()
    {
        OpeningBalanceWaitsForTheDispatchedRootSelection();
        ClosedOrForeignMenuFramesRearmTheSelectionGate();
        RepeatDoesNotWaitForTheRootSelection();
        PumpVisibilityUsesRootOwnershipAndTheShopWallet();
    }

    private static void OpeningBalanceWaitsForTheDispatchedRootSelection()
    {
        var readout = new Steam2026MenuGilReadout();
        var spoken = new List<string>();
        MenuGilReadoutResult Tick(bool allowed = true) =>
            readout.Tick(false, true, allowed, () => MenuGilScreen.MainMenu, () => 2500u, Speak(spoken));

        readout.ObserveDispatchedMenu(MainMenuFrame(), menuSpeechDispatched: false);
        Equal(MenuGilReadoutOutcome.None, Tick().Outcome, "no balance before the root selection is spoken");

        readout.ObserveDispatchedMenu(MainMenuFrame(), menuSpeechDispatched: true);
        Equal(MenuGilReadoutOutcome.None, Tick(allowed: false).Outcome, "background frame cannot announce");
        Equal(MenuGilReadoutOutcome.Announced, Tick().Outcome, "balance follows the dispatched selection");
        Equal("2500 gil.|queued", string.Join(" / ", spoken), "opening balance queues after the selection");

        readout.ObserveDispatchedMenu(RuntimeDomainUpdate<MenuFrameObservation>.Unchanged, false);
        Equal(MenuGilReadoutOutcome.None, Tick().Outcome, "an unchanged frame does not repeat the balance");
    }

    private static void ClosedOrForeignMenuFramesRearmTheSelectionGate()
    {
        var readout = new Steam2026MenuGilReadout();
        var spoken = new List<string>();
        MenuGilReadoutResult Tick(bool? session) =>
            readout.Tick(false, session, true, () => MenuGilScreen.MainMenu, () => 10u, Speak(spoken));

        readout.ObserveDispatchedMenu(MainMenuFrame(), true);
        Equal(MenuGilReadoutOutcome.Announced, Tick(true).Outcome, "first opening");

        readout.ObserveDispatchedMenu(RuntimeDomainUpdate<MenuFrameObservation>.Closed, false);
        Equal(MenuGilReadoutOutcome.None, Tick(false).Outcome, "menu closed");
        readout.ObserveDispatchedMenu(MainMenuFrame(), false);
        Equal(MenuGilReadoutOutcome.None, Tick(true).Outcome, "reopened menu waits for its selection again");
        readout.ObserveDispatchedMenu(MainMenuFrame(), true);
        Equal(MenuGilReadoutOutcome.Announced, Tick(true).Outcome, "reopened menu announces after its selection");

        readout.ObserveDispatchedMenu(QuitFrame(), true);
        Equal(MenuGilReadoutOutcome.None, Tick(false).Outcome, "Quit dialog frame is not the root");
        Equal(MenuGilReadoutOutcome.None, Tick(true).Outcome, "Quit frame cleared the root selection gate");

        readout.ObserveDispatchedMenu(MainMenuFrame(), true);
        readout.Reset();
        Equal(MenuGilReadoutOutcome.None, Tick(true).Outcome, "a runtime reset clears the selection gate");
    }

    private static void RepeatDoesNotWaitForTheRootSelection()
    {
        var readout = new Steam2026MenuGilReadout();
        var spoken = new List<string>();
        var result = readout.Tick(true, null, false, () => MenuGilScreen.Shop, () => 0u, Speak(spoken));
        Equal(MenuGilReadoutOutcome.Repeated, result.Outcome, "G on a shop wallet screen");
        Equal("0 gil.|interrupt", string.Join(" / ", spoken), "G speaks a real zero balance at once");
    }

    private static void PumpVisibilityUsesRootOwnershipAndTheShopWallet()
    {
        var root = new MainMenuSnapshot(1, 0, 0, 0, 0, 1, 0x7ff, 0, 0);
        Equal(MenuGilScreen.MainMenu,
            Steam2026ResearchObservationPump.ResolveMenuGilScreen(3, false, false, false, true, root, false),
            "rendered world-map root menu");
        Equal(MenuGilScreen.MainMenu,
            Steam2026ResearchObservationPump.ResolveMenuGilScreen(5, false, true, false, true, root, false),
            "rendered root in the menu module");
        Equal(MenuGilScreen.None,
            Steam2026ResearchObservationPump.ResolveMenuGilScreen(1, false, false, false, false, root, false),
            "root globals without live rendering");
        Equal(MenuGilScreen.None,
            Steam2026ResearchObservationPump.ResolveMenuGilScreen(1, false, false, false, true, root with { Target = 4 }, false),
            "Status submenu under an old render lease");
        Equal(MenuGilScreen.None,
            Steam2026ResearchObservationPump.ResolveMenuGilScreen(1, false, false, false, true, root, true),
            "Quit dialog");
        Equal(MenuGilScreen.None,
            Steam2026ResearchObservationPump.ResolveMenuGilScreen(1, false, false, false, true, null, false),
            "unreadable main menu");
        Equal(MenuGilScreen.Shop,
            Steam2026ResearchObservationPump.ResolveMenuGilScreen(5, true, true, true, false, null, false),
            "shop screen drawing the wallet");
        Equal(MenuGilScreen.None,
            Steam2026ResearchObservationPump.ResolveMenuGilScreen(5, false, true, true, true, root, false),
            "an owned shop without a wallet on screen is not the root");
        Equal(MenuGilScreen.None,
            Steam2026ResearchObservationPump.ResolveMenuGilScreen(1, true, false, false, false, null, false),
            "stale shop visibility outside the shop module");
    }

    private static RuntimeDomainUpdate<MenuFrameObservation> MainMenuFrame() =>
        RuntimeDomainUpdate<MenuFrameObservation>.Present(
            new MenuFrameObservation(
                Steam2026MenuGilReadout.MainMenuScreen,
                isOpen: true,
                revision: 1,
                [new MenuRowObservation(0, "Item", true, true)]));

    private static RuntimeDomainUpdate<MenuFrameObservation> QuitFrame() =>
        RuntimeDomainUpdate<MenuFrameObservation>.Present(
            Steam2026ResearchObservationPump.CreateQuitConfirmationMenuFrame(
                new QuitConfirmationSnapshot(1, 0, 1),
                revision: 2));

    private static Func<string, bool, bool> Speak(List<string> spoken) =>
        (text, interrupt) =>
        {
            spoken.Add($"{text}|{(interrupt ? "interrupt" : "queued")}");
            return true;
        };

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}
