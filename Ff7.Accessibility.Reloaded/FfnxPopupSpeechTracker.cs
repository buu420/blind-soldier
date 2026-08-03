namespace Ff7.Accessibility.Reloaded;

internal readonly record struct FfnxPopupSnapshot(
    string Text,
    uint Ttl,
    uint Color);

internal sealed class FfnxPopupSpeechTracker
{
    private string? lastText;
    private uint lastTtl;
    private bool active;

    internal string? Observe(FfnxPopupSnapshot? snapshot)
    {
        if (snapshot is not { Ttl: > 0 } visible)
        {
            MarkInactive();
            return null;
        }

        var text = Normalize(visible.Text);
        if (text is null)
        {
            MarkInactive();
            return null;
        }

        var isNewGeneration =
            !active
            || !string.Equals(lastText, text, StringComparison.Ordinal)
            || visible.Ttl > lastTtl;
        active = true;
        lastText = text;
        lastTtl = visible.Ttl;
        return isNewGeneration ? text : null;
    }

    internal void Reset()
    {
        active = false;
        lastText = null;
        lastTtl = 0;
    }

    private void MarkInactive()
    {
        active = false;
        lastTtl = 0;
    }

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return string.Join(
            ' ',
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
