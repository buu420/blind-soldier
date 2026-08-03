using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime;

internal static class Steam2026FieldCountdownSpeechTests
{
    public static void Run()
    {
        ClockPagesAreAcknowledgedWithoutEnteringOrdinaryDialogueSpeech();
        OrdinaryDialogueBesideTheClockRemainsDispatchable();
    }

    private static void ClockPagesAreAcknowledgedWithoutEnteringOrdinaryDialogueSpeech()
    {
        var countdown = new FieldCountdownSpeechCoordinator();
        countdown.Observe(new FieldCountdownSnapshot(true, 599, 0b0001));
        var page = new DialoguePageObservation(
            true,
            0,
            7,
            string.Empty,
            "Time 09:59",
            Array.Empty<DialogueChoiceObservation>());
        var acknowledgements = new List<DialoguePageObservation>();

        var filtered = Steam2026ResearchObservationPump.SuppressClockWindowDialogue(
            RuntimeDomainUpdate<DialoguePageObservation>.Present(page),
            countdown,
            delivered =>
            {
                acknowledgements.Add(delivered);
                return true;
            });

        Equal(RuntimeDomainUpdateKind.Unchanged, filtered.Kind, "clock page is silent in ordinary dialogue");
        Equal(1, acknowledgements.Count, "clock page is acknowledged exactly once");
        Equal(page, acknowledgements[0], "clock acknowledgement preserves the exact stable page");
    }

    private static void OrdinaryDialogueBesideTheClockRemainsDispatchable()
    {
        var countdown = new FieldCountdownSpeechCoordinator();
        countdown.Observe(new FieldCountdownSnapshot(true, 599, 0b0001));
        var page = new DialoguePageObservation(
            true,
            2,
            8,
            "Barret",
            "Move out!",
            Array.Empty<DialogueChoiceObservation>());
        var acknowledged = false;

        var filtered = Steam2026ResearchObservationPump.SuppressClockWindowDialogue(
            RuntimeDomainUpdate<DialoguePageObservation>.Present(page),
            countdown,
            _ =>
            {
                acknowledged = true;
                return true;
            });

        Equal(RuntimeDomainUpdateKind.Present, filtered.Kind, "ordinary dialogue remains present");
        Equal(page, filtered.Value, "ordinary dialogue remains exact");
        Equal(false, acknowledged, "ordinary dialogue is acknowledged only after actual speech");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
