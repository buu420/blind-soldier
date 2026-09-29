namespace Ff7.Accessibility.Core;

/// <summary>
/// Diagnostic log lines for the native highway score, one run at a time. The score moves by
/// +500 when a biker's HP reaches zero (FUN_00656620) and by -50 when the truck is hit while the
/// score is above zero (FUN_006567B0), but a poll sees only the net change since the last one:
/// +500 could be one defeat, or two defeats and ten hits. So each line records the observed
/// score and net change, and a run's summary totals the observed rises and falls; no cause is
/// named that the poll cannot see. A hit at score 0 changes nothing and cannot appear at all.
/// Nothing is spoken.
/// </summary>
public sealed class HighwayScoreChangeTracker
{
    private int run;
    private bool started;
    private int score;
    private int changes;
    private int rises;
    private int falls;
    private HighwayBannerState lastBanner = HighwayBannerState.None;

    public IReadOnlyList<string> Observe(HighwayAccessibilityState? state)
    {
        if (state is null)
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>(2);
        var readyAppeared = state.Banner == HighwayBannerState.Ready && lastBanner != HighwayBannerState.Ready;
        if (state.Banner != HighwayBannerState.Unknown)
        {
            lastBanner = state.Banner;
        }

        if (started && readyAppeared && End() is { } summary)
        {
            lines.Add(summary);
        }

        if (!started)
        {
            started = true;
            run++;
            score = state.Score;
            changes = rises = falls = 0;
            lines.Add($"Highway score run {run} start: score {score}, mode {(state.IsStoryChase ? "story" : "gold-saucer")}.");
            return lines;
        }

        if (state.Score != score)
        {
            var change = state.Score - score;
            changes++;
            if (change > 0)
            {
                rises += change;
            }
            else
            {
                falls += change;
            }

            lines.Add($"Highway score run {run}: {score} -> {state.Score}, net {change:+0;-0}.");
            score = state.Score;
        }

        return lines;
    }

    /// <summary>Ends the current run, if any, and returns its summary line.</summary>
    public string? Reset()
    {
        lastBanner = HighwayBannerState.None;
        return End();
    }

    private string? End()
    {
        if (!started)
        {
            return null;
        }

        started = false;
        return $"Highway score run {run} end: final {score}; {changes} observed change{(changes == 1 ? string.Empty : "s")}, " +
            $"rises {(rises == 0 ? "0" : $"+{rises}")}, falls {(falls == 0 ? "0" : falls.ToString())}.";
    }
}
