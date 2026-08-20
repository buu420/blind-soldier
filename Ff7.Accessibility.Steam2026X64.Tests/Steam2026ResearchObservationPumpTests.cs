using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;

internal static class Steam2026ResearchObservationPumpTests
{
    internal static void Run()
    {
        PublishesOnlyCoherentFieldUpdates();
        PublishesNativeQuitConfirmationSelection();
        ExactRenderedRootMenuOwnsWorldMapOverlay();
        MainMenuOwnershipFailsClosedAndReacquires();
    }

    private static void ExactRenderedRootMenuOwnsWorldMapOverlay()
    {
        var raw = RuntimeDomainUpdate<MenuFrameObservation>.Present(
            new MenuFrameObservation(
                "Main Menu",
                isOpen: true,
                revision: 4,
                [new MenuRowObservation(1, "Magic", true, true)]));
        string? stateKey = "world-map-root";

        var visible = Steam2026ResearchObservationPump.NormalizeMainMenuOwnershipUpdate(
            moduleId: 3,
            shopOwnershipRead: false,
            ownsShop: false,
            rootMenuRecentlyRendered: true,
            raw,
            ref stateKey);
        Equal(RuntimeDomainUpdateKind.Present, visible.Kind, "rendered world-map root menu owns speech");

        var stale = Steam2026ResearchObservationPump.NormalizeMainMenuOwnershipUpdate(
            moduleId: 3,
            shopOwnershipRead: false,
            ownsShop: false,
            rootMenuRecentlyRendered: false,
            raw,
            ref stateKey);
        Equal(RuntimeDomainUpdateKind.Closed, stale.Kind, "stale world-map root menu is closed");
        Equal<string?>(null, stateKey, "stale world-map root menu clears dedupe state");
    }

    private static void PublishesNativeQuitConfirmationSelection()
    {
        var noFrame = Steam2026ResearchObservationPump.CreateQuitConfirmationMenuFrame(
            new QuitConfirmationSnapshot(1, 0, 1),
            revision: 7);
        Equal("Quit Confirmation", noFrame.Screen, "Quit-confirmation screen name");
        Equal(2, noFrame.Rows.Length, "Quit-confirmation row count");
        Equal("No", noFrame.Rows.Single(row => row.Selected).Text, "native No selection");

        var yesFrame = Steam2026ResearchObservationPump.CreateQuitConfirmationMenuFrame(
            new QuitConfirmationSnapshot(0, 0, 1),
            revision: 8);
        Equal("Yes", yesFrame.Rows.Single(row => row.Selected).Text, "native Yes selection");
    }

    private static void PublishesOnlyCoherentFieldUpdates()
    {
        var observation = new FieldFrameObservation(
            116,
            1,
            100,
            200,
            300,
            9,
            true,
            0,
            0,
            160);

        var present = Steam2026ResearchObservationPump.NormalizeFieldUpdate(
            moduleId: 1,
            readSucceeded: true,
            observation);
        Equal(RuntimeDomainUpdateKind.Present, present.Kind, "coherent field update kind");
        Equal(observation, present.Value, "coherent field update value");

        var unavailable = Steam2026ResearchObservationPump.NormalizeFieldUpdate(
            moduleId: 1,
            readSucceeded: false,
            observation: null);
        Equal(RuntimeDomainUpdateKind.Unchanged, unavailable.Kind, "unavailable field update kind");

        var closed = Steam2026ResearchObservationPump.NormalizeFieldUpdate(
            moduleId: 5,
            readSucceeded: true,
            observation);
        Equal(RuntimeDomainUpdateKind.Closed, closed.Kind, "non-field module update kind");
    }

    private static void MainMenuOwnershipFailsClosedAndReacquires()
    {
        var raw = RuntimeDomainUpdate<MenuFrameObservation>.Present(
            new MenuFrameObservation(
                "Main Menu",
                isOpen: true,
                revision: 4,
                [new MenuRowObservation(1, "Magic", true, true)]));

        AssertClosedAndCleared(moduleId: 17, shopOwnershipRead: true, ownsShop: false, raw,
            "battle-results module must close stale main-menu state");
        AssertClosedAndCleared(moduleId: 5, shopOwnershipRead: true, ownsShop: true, raw,
            "exact shop ownership must close stale main-menu state");
        AssertClosedAndCleared(moduleId: 5, shopOwnershipRead: false, ownsShop: false, raw,
            "incoherent shop ownership must close stale main-menu state");

        string? transitionStateKey = "stale-shop-transition";
        var transition = Steam2026ResearchObservationPump.NormalizeMainMenuOwnershipUpdate(
            moduleId: 5,
            shopOwnershipRead: true,
            ownsShop: false,
            rootMenuRecentlyRendered: false,
            raw,
            ref transitionStateKey);
        Equal(RuntimeDomainUpdateKind.Closed, transition.Kind, "shop transition without root-menu rendering closes stale state");
        Equal<string?>(null, transitionStateKey, "shop transition clears stale main-menu state");

        string? stateKey = null;
        var reacquired = Steam2026ResearchObservationPump.NormalizeMainMenuOwnershipUpdate(
            moduleId: 5,
            shopOwnershipRead: true,
            ownsShop: false,
            rootMenuRecentlyRendered: true,
            raw,
            ref stateKey);
        Equal(RuntimeDomainUpdateKind.Present, reacquired.Kind, "root menu reacquires a present update");
        Equal("Main Menu", reacquired.Value?.Screen, "root menu reacquisition preserves native frame");
    }

    private static void AssertClosedAndCleared(
        int moduleId,
        bool shopOwnershipRead,
        bool ownsShop,
        RuntimeDomainUpdate<MenuFrameObservation> raw,
        string label)
    {
        string? stateKey = "stale-main-menu";
        var update = Steam2026ResearchObservationPump.NormalizeMainMenuOwnershipUpdate(
            moduleId,
            shopOwnershipRead,
            ownsShop,
            rootMenuRecentlyRendered: true,
            raw,
            ref stateKey);
        Equal(RuntimeDomainUpdateKind.Closed, update.Kind, label);
        Equal<string?>(null, stateKey, $"{label} clears main-menu dedupe state");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
