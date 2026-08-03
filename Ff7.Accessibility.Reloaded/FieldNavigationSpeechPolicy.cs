namespace Ff7.Accessibility.Reloaded;

public static class FieldNavigationSpeechPolicy
{
    public const int MinimumIntervalMs = 1000;
    public const int MinimumRunningIntervalMs = 250;

    public static int ResolveIntervalMs(
        int configuredIntervalMs,
        int configuredRunningIntervalMs,
        bool isRunning) =>
        isRunning
            ? Math.Max(MinimumRunningIntervalMs, configuredRunningIntervalMs)
            : Math.Max(MinimumIntervalMs, configuredIntervalMs);

    public static bool IsDue(
        DateTime now,
        DateTime lastSpokenAt,
        int configuredIntervalMs,
        int configuredRunningIntervalMs,
        bool isRunning,
        bool isSuppressed,
        bool isForeground,
        bool hasUsableControl,
        bool navigationEnabled)
    {
        if (isSuppressed || !isForeground || !hasUsableControl || !navigationEnabled)
        {
            return false;
        }

        var intervalMs = ResolveIntervalMs(
            configuredIntervalMs,
            configuredRunningIntervalMs,
            isRunning);
        var interval = TimeSpan.FromMilliseconds(intervalMs);
        return lastSpokenAt == DateTime.MinValue || now - lastSpokenAt >= interval;
    }
}

public sealed class FieldNavigationGuidanceRepeatGate
{
    public const int UnchangedGuidanceReminderIntervalMs = 2000;

    private string lastSpeech = string.Empty;
    private DateTime lastSpokenAt = DateTime.MinValue;

    public bool ShouldSpeak(string speech, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(speech))
        {
            return false;
        }

        var changed = !string.Equals(speech, lastSpeech, StringComparison.Ordinal);
        var clockReset = lastSpokenAt != DateTime.MinValue && now < lastSpokenAt;
        var reminderDue =
            lastSpokenAt == DateTime.MinValue ||
            now - lastSpokenAt >= TimeSpan.FromMilliseconds(UnchangedGuidanceReminderIntervalMs);
        if (!changed && !clockReset && !reminderDue)
        {
            return false;
        }

        lastSpeech = speech;
        lastSpokenAt = now;
        return true;
    }

    public void Reset()
    {
        lastSpeech = string.Empty;
        lastSpokenAt = DateTime.MinValue;
    }
}
