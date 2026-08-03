namespace Ff7.Accessibility.Reloaded;

public sealed class FieldMessageSpeechTracker
{
    private readonly TimeSpan stableWindow;
    private string pendingText = string.Empty;
    private DateTime pendingSince = DateTime.MinValue;
    private string lastSpokenText = string.Empty;

    public FieldMessageSpeechTracker(TimeSpan stableWindow)
    {
        this.stableWindow = stableWindow < TimeSpan.Zero ? TimeSpan.Zero : stableWindow;
    }

    public string? Observe(string? text, DateTime now)
    {
        var normalized = Ff7EncodedTextDecoder.NormalizeWhitespace(text ?? string.Empty);
        if (normalized.Length == 0)
        {
            pendingText = string.Empty;
            pendingSince = DateTime.MinValue;
            lastSpokenText = string.Empty;
            return null;
        }

        if (!string.Equals(normalized, pendingText, StringComparison.Ordinal))
        {
            pendingText = normalized;
            pendingSince = now;
            return null;
        }

        if (string.Equals(normalized, lastSpokenText, StringComparison.Ordinal))
        {
            return null;
        }

        if (now - pendingSince < stableWindow)
        {
            return null;
        }

        lastSpokenText = normalized;
        return normalized;
    }
}
