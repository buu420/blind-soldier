using Ff7.Accessibility.Core;

internal static class FieldCountdownSpeechTrackerTests
{
    public static void Run()
    {
        AnnouncesTheRequestedNativeThresholdSchedule();
        AnnouncesOnlyTheMostUrgentThresholdCrossedByASkippedObservation();
        DoesNotRepeatWhileTheNativeTimerIsPaused();
        ResetsForANewOrReappearingNativeCountdown();
        JoinsAnAlreadyRunningCountdownAtItsNextThreshold();
    }

    private static void AnnouncesTheRequestedNativeThresholdSchedule()
    {
        var expected = new (int Seconds, string Speech, bool IsFinalTen)[]
        {
            (660, "11 minutes remaining", false),
            (600, "10 minutes remaining", false),
            (540, "9 minutes remaining", false),
            (480, "8 minutes remaining", false),
            (420, "7 minutes remaining", false),
            (360, "6 minutes remaining", false),
            (300, "5 minutes remaining", false),
            (240, "4 minutes remaining", false),
            (180, "3 minutes remaining", false),
            (120, "2 minutes remaining", false),
            (90, "1 minute 30 seconds remaining", false),
            (60, "1 minute remaining", false),
            (30, "30 seconds remaining", false),
            (15, "15 seconds remaining", false),
            (10, "10", true),
            (9, "9", true),
            (8, "8", true),
            (7, "7", true),
            (6, "6", true),
            (5, "5", true),
            (4, "4", true),
            (3, "3", true),
            (2, "2", true),
            (1, "1", true),
            (0, "0", true)
        };

        var tracker = new FieldCountdownSpeechTracker();
        Equal(null, tracker.Observe(true, 661), "unscheduled starting second");
        foreach (var item in expected)
        {
            var announcement = tracker.Observe(true, item.Seconds);
            Equal(true, announcement.HasValue, $"announcement at {item.Seconds} seconds");
            Equal(item.Seconds, announcement?.RemainingSeconds, $"native seconds at {item.Seconds}");
            Equal(item.Speech, announcement?.Speech, $"speech at {item.Seconds}");
            Equal(item.IsFinalTen, announcement?.IsFinalTen, $"final-ten priority at {item.Seconds}");
        }

        Equal(null, tracker.Observe(true, 0), "zero is not repeated");
    }

    private static void AnnouncesOnlyTheMostUrgentThresholdCrossedByASkippedObservation()
    {
        var tracker = new FieldCountdownSpeechTracker();
        Equal(null, tracker.Observe(true, 31), "skip fixture start");

        var announcement = tracker.Observe(true, 9);
        Equal("9", announcement?.Speech, "skipped observations produce current urgent countdown number");
        Equal(null, tracker.Observe(true, 9), "skipped thresholds are not queued as a backlog");

        tracker.Reset();
        Equal(null, tracker.Observe(true, 91), "single-threshold fixture start");
        Equal(
            "1 minute 30 seconds remaining",
            tracker.Observe(true, 89)?.Speech,
            "crossing a threshold between polls still announces it");
    }

    private static void DoesNotRepeatWhileTheNativeTimerIsPaused()
    {
        var tracker = new FieldCountdownSpeechTracker();
        Equal("2 minutes remaining", tracker.Observe(true, 120)?.Speech, "paused threshold first speech");
        Equal(null, tracker.Observe(true, 120), "paused threshold first duplicate");
        Equal(null, tracker.Observe(true, 120), "paused threshold second duplicate");
    }

    private static void ResetsForANewOrReappearingNativeCountdown()
    {
        var tracker = new FieldCountdownSpeechTracker();
        Equal("1 minute remaining", tracker.Observe(true, 60)?.Speech, "first countdown minute");
        Equal(null, tracker.Observe(false, 60), "hidden native clock produces no speech");
        Equal("1 minute remaining", tracker.Observe(true, 60)?.Speech, "reappearing native clock is new countdown");

        Equal(null, tracker.Observe(true, 59), "new countdown advances");
        Equal("10 minutes remaining", tracker.Observe(true, 600)?.Speech, "native timer increase begins a new countdown");
        Equal(
            "1 minute 30 seconds remaining",
            tracker.Observe(true, 90)?.Speech,
            "new countdown can announce thresholds used by the prior session");
    }

    private static void JoinsAnAlreadyRunningCountdownAtItsNextThreshold()
    {
        var tracker = new FieldCountdownSpeechTracker();
        Equal(null, tracker.Observe(true, 83), "mid-countdown observation is not rounded up");
        Equal(null, tracker.Observe(true, 75), "ordinary second is silent");
        Equal("1 minute remaining", tracker.Observe(true, 60)?.Speech, "next real threshold speaks");
        Equal(null, tracker.Observe(true, -1), "invalid countdown ends the session");
        Equal("30 seconds remaining", tracker.Observe(true, 30)?.Speech, "valid countdown after invalid state is new");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
