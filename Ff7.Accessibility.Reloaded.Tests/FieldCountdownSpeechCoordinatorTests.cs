using Ff7.Accessibility.Reloaded;

internal static class FieldCountdownSpeechCoordinatorTests
{
    public static void Run()
    {
        HoldsEachNativeThresholdUntilSpeechIsAcknowledged();
        SuppressesOnlyTheClockWindowFromOrdinaryDialogueSpeech();
        ClearsSpeechAndOwnershipWhenTheNativeObservationIsUnavailable();
    }

    private static void HoldsEachNativeThresholdUntilSpeechIsAcknowledged()
    {
        var coordinator = new FieldCountdownSpeechCoordinator();
        coordinator.Observe(new FieldCountdownSnapshot(true, 120, 0b0010));

        Equal(true, coordinator.TryGetPending(out var first), "native threshold becomes pending");
        Equal("2 minutes remaining", first.Speech, "native threshold speech");
        Equal(true, coordinator.TryGetPending(out var retry), "unacknowledged native threshold remains pending");
        Equal(first, retry, "retry preserves the exact pending announcement");

        coordinator.Acknowledge(first);
        Equal(false, coordinator.TryGetPending(out _), "acknowledged native threshold clears");
        coordinator.Observe(new FieldCountdownSnapshot(true, 120, 0b0010));
        Equal(false, coordinator.TryGetPending(out _), "paused native second does not requeue");
    }

    private static void SuppressesOnlyTheClockWindowFromOrdinaryDialogueSpeech()
    {
        var countdown = new FieldCountdownSpeechCoordinator();
        countdown.Observe(new FieldCountdownSnapshot(true, 599, 0b0001));
        var dialogue = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot[] windows =
        [
            new(0, 1, "Time 09:59", 0x710000),
            new(2, 1, "Barret: Move out!", 0x720000)
        ];

        var ordinaryWindows = windows.Where(window => !countdown.ShouldSuppressWindow(window)).ToArray();
        Equal(0, dialogue.Observe(
            ordinaryWindows,
            activeMessageCount: 2,
            DateTime.UnixEpoch,
            shouldSuppress: null).Count,
            "new native dialogue lifecycle is observed before dispatch");
        var speech = dialogue.Observe(
            ordinaryWindows,
            activeMessageCount: 2,
            DateTime.UnixEpoch.AddTicks(1),
            shouldSuppress: null);

        Equal(1, speech.Count, "clock text is excluded while ordinary dialogue remains");
        Equal(2, speech[0].WindowId, "ordinary dialogue window remains owned");
        Equal("Barret: Move out!", speech[0].Text, "ordinary dialogue remains exact");
    }

    private static void ClearsSpeechAndOwnershipWhenTheNativeObservationIsUnavailable()
    {
        var coordinator = new FieldCountdownSpeechCoordinator();
        coordinator.Observe(new FieldCountdownSnapshot(true, 60, 0b1000));
        Equal(true, coordinator.TryGetPending(out _), "countdown pending before loss");
        Equal(true, coordinator.ShouldSuppressWindow(new FieldVisibleWindowSnapshot(3, 1, "Time 01:00", 1)), "clock owned before loss");

        coordinator.Observe(null);

        Equal(false, coordinator.TryGetPending(out _), "unavailable observation drops stale speech");
        Equal(false, coordinator.ShouldSuppressWindow(new FieldVisibleWindowSnapshot(3, 1, "Time 01:00", 1)), "unavailable observation releases stale ownership");
        coordinator.Observe(new FieldCountdownSnapshot(true, 60, 0b1000));
        Equal(true, coordinator.TryGetPending(out _), "reappearing countdown starts a new speech session");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
