using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;

internal static class Steam2026ResearchObservationPumpTests
{
    internal static void Run()
    {
        PublishesOnlyCoherentFieldUpdates();
        PublishesNativeQuitConfirmationSelection();
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

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
