using Ff7.Accessibility.Core;

/// <summary>
/// Diagnostic only: the log lines that record the native highway score over a ride. The score
/// moves by +500 when a biker's HP reaches zero (FUN_00656620) and by -50 when the truck is hit
/// while the score is above zero (FUN_006567B0), but polling sees only the net change between
/// samples: +500 could be one defeat, or two defeats and ten hits. So the log records the
/// observed score and net change, and a run's summary totals observed changes, without naming
/// causes it cannot see. Nothing is spoken.
/// </summary>
internal static class HighwayScoreChangeTrackerTests
{
    internal static void Run()
    {
        TheFirstFrameOfARunIsTheBaseline();
        ChangesAreRecordedAsObservedNetChanges();
        UnreadableFramesNeitherChangeNorRestartTheRun();
        ReadyStartsANewRunWithASummaryOfTheLast();
        ResetEndsTheRunOnceAndTheNextRideIsCountedAfresh();
    }

    private static void TheFirstFrameOfARunIsTheBaseline()
    {
        var tracker = new HighwayScoreChangeTracker();
        Equal(
            "Highway score run 1 start: score 0, mode gold-saucer.",
            string.Join("|", tracker.Observe(Arcade(0, HighwayBannerState.Ready))),
            "the first frame opens run 1");
        Equal("", string.Join("|", tracker.Observe(Arcade(0, HighwayBannerState.Go))), "an unchanged score logs nothing");
    }

    private static void ChangesAreRecordedAsObservedNetChanges()
    {
        var tracker = new HighwayScoreChangeTracker();
        _ = tracker.Observe(Arcade(0, HighwayBannerState.Go));
        foreach (var (score, line) in new[]
                 {
                     (500, "Highway score run 1: 0 -> 500, net +500."),
                     (450, "Highway score run 1: 500 -> 450, net -50."),
                     (1450, "Highway score run 1: 450 -> 1450, net +1000."),
                     (1350, "Highway score run 1: 1450 -> 1350, net -100."),
                     (1800, "Highway score run 1: 1350 -> 1800, net +450.")
                 })
        {
            var logged = string.Join("|", tracker.Observe(Arcade(score)));
            Equal(line, logged, $"score {score} is recorded as observed");
            foreach (var claim in new[] { "defeated", "truck hit", "biker", "kill" })
            {
                Equal(false, logged.Contains(claim, StringComparison.OrdinalIgnoreCase),
                    $"a net change is not named as '{claim}': {logged}");
            }
        }
    }
    private static void UnreadableFramesNeitherChangeNorRestartTheRun()
    {
        var tracker = new HighwayScoreChangeTracker();
        _ = tracker.Observe(Arcade(0, HighwayBannerState.Go));
        Equal("", string.Join("|", tracker.Observe(null)), "a missing snapshot logs nothing");
        Equal(
            "Highway score run 1: 0 -> 500, net +500.",
            string.Join("|", tracker.Observe(Arcade(500))),
            "and the run continues after it");
    }

    private static void ReadyStartsANewRunWithASummaryOfTheLast()
    {
        var tracker = new HighwayScoreChangeTracker();
        _ = tracker.Observe(Arcade(0, HighwayBannerState.Ready));
        _ = tracker.Observe(Arcade(0, HighwayBannerState.Go));
        _ = tracker.Observe(Arcade(500));
        _ = tracker.Observe(Arcade(450));
        _ = tracker.Observe(Arcade(450, HighwayBannerState.Goal));
        Equal(
            "Highway score run 1 end: final 450; 2 observed changes, rises +500, falls -50.|Highway score run 2 start: score 0, mode gold-saucer.",
            string.Join("|", tracker.Observe(Arcade(0, HighwayBannerState.Ready))),
            "READY appearing again ends the ride and starts the next");
        Equal("", string.Join("|", tracker.Observe(Arcade(0, HighwayBannerState.Ready))), "READY held on screen is one start");
    }

    private static void ResetEndsTheRunOnceAndTheNextRideIsCountedAfresh()
    {
        var tracker = new HighwayScoreChangeTracker();
        _ = tracker.Observe(Story(0));
        _ = tracker.Observe(Story(500));
        Equal("Highway score run 1 end: final 500; 1 observed change, rises +500, falls 0.", tracker.Reset() ?? "", "leaving the module ends the run");
        Equal(null, tracker.Reset(), "a second reset has no run to end");
        Equal(
            "Highway score run 2 start: score 0, mode story.",
            string.Join("|", tracker.Observe(Story(0))),
            "the next ride is counted from its own first frame");
    }

    private static HighwayAccessibilityState Arcade(int score, HighwayBannerState banner = HighwayBannerState.None) =>
        new(
            new HighwayPoint(0, 0),
            new HighwayPoint(0, 300),
            Array.Empty<HighwayEnemyState>(),
            Array.Empty<HighwayPartyHealth>(),
            score,
            IsStoryChase: false,
            Banner: banner);

    private static HighwayAccessibilityState Story(int score) =>
        Arcade(score) with { IsStoryChase = true };

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Highway score log: {label}: expected {expected}, got {actual}.");
        }
    }
}
