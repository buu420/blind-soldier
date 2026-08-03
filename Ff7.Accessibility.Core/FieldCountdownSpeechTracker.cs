using System.Globalization;

namespace Ff7.Accessibility.Core;

public readonly record struct FieldCountdownAnnouncement(
    int RemainingSeconds,
    string Speech,
    bool IsFinalTen);

public sealed class FieldCountdownSpeechTracker
{
    private readonly HashSet<int> announcedThresholds = [];
    private int? previousSeconds;

    public FieldCountdownAnnouncement? Observe(bool isActive, int remainingSeconds)
    {
        if (!isActive || remainingSeconds < 0)
        {
            Reset();
            return null;
        }

        if (previousSeconds is null || remainingSeconds > previousSeconds.Value + 1)
        {
            announcedThresholds.Clear();
            previousSeconds = remainingSeconds;
            return TryCreateExactAnnouncement(remainingSeconds);
        }

        var previous = previousSeconds.Value;
        previousSeconds = remainingSeconds;
        if (remainingSeconds >= previous)
        {
            return null;
        }

        var crossedThreshold = FindMostUrgentCrossedThreshold(previous, remainingSeconds);
        return crossedThreshold is int threshold
            ? TryCreateAnnouncement(threshold)
            : null;
    }

    public void Reset()
    {
        previousSeconds = null;
        announcedThresholds.Clear();
    }

    private FieldCountdownAnnouncement? TryCreateExactAnnouncement(int remainingSeconds) =>
        IsThreshold(remainingSeconds)
            ? TryCreateAnnouncement(remainingSeconds)
            : null;

    private FieldCountdownAnnouncement? TryCreateAnnouncement(int remainingSeconds)
    {
        if (!announcedThresholds.Add(remainingSeconds))
        {
            return null;
        }

        return new FieldCountdownAnnouncement(
            remainingSeconds,
            FormatSpeech(remainingSeconds),
            remainingSeconds <= 10);
    }

    private static int? FindMostUrgentCrossedThreshold(int previousSeconds, int remainingSeconds)
    {
        int? mostUrgent = null;

        if (remainingSeconds <= 10)
        {
            mostUrgent = remainingSeconds;
        }

        Consider(15);
        Consider(30);
        Consider(60);
        Consider(90);

        var nextMinute = ((remainingSeconds + 59) / 60) * 60;
        if (nextMinute >= 120)
        {
            Consider(nextMinute);
        }

        return mostUrgent;

        void Consider(int threshold)
        {
            if (threshold >= remainingSeconds &&
                threshold < previousSeconds &&
                (mostUrgent is null || threshold < mostUrgent.Value))
            {
                mostUrgent = threshold;
            }
        }
    }

    private static bool IsThreshold(int remainingSeconds) =>
        remainingSeconds is >= 0 and <= 10 or 15 or 30 or 60 or 90 ||
        remainingSeconds >= 120 && remainingSeconds % 60 == 0;

    private static string FormatSpeech(int remainingSeconds)
    {
        if (remainingSeconds <= 10)
        {
            return remainingSeconds.ToString(CultureInfo.InvariantCulture);
        }

        return remainingSeconds switch
        {
            15 => "15 seconds remaining",
            30 => "30 seconds remaining",
            60 => "1 minute remaining",
            90 => "1 minute 30 seconds remaining",
            _ => $"{remainingSeconds / 60} minutes remaining"
        };
    }
}
