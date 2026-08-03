namespace Ff7.Accessibility.Reloaded;

public sealed class MainMenuSpeechScheduler
{
    private readonly TimeSpan settleTime;
    private string pendingText = string.Empty;
    private string lastSpokenText = string.Empty;
    private DateTime pendingSince = DateTime.MinValue;

    public MainMenuSpeechScheduler(TimeSpan settleTime)
    {
        this.settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
    }

    public string? Observe(string text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            pendingText = string.Empty;
            lastSpokenText = string.Empty;
            pendingSince = DateTime.MinValue;
            return null;
        }

        if (!string.Equals(text, pendingText, StringComparison.Ordinal))
        {
            pendingText = text;
            pendingSince = now;
            if (settleTime == TimeSpan.Zero && !string.Equals(text, lastSpokenText, StringComparison.Ordinal))
            {
                lastSpokenText = text;
                return text;
            }

            return null;
        }

        if (string.Equals(text, lastSpokenText, StringComparison.Ordinal))
        {
            return null;
        }

        if (now - pendingSince < settleTime)
        {
            return null;
        }

        lastSpokenText = text;
        return text;
    }
}
