namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Retains only speech that Blind Soldier successfully delivered and repeats
/// it without treating the repeat itself as a new utterance.
/// </summary>
public sealed class RepeatLastSpeechController
{
    public const int VirtualKeyR = 0x52;

    private readonly object sync = new();
    private string lastDelivered = string.Empty;

    public void RememberDelivered(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (sync)
        {
            lastDelivered = text;
        }
    }

    public bool Poll(
        Func<int, bool> observeRisingEdge,
        Func<string, bool> deliver)
    {
        ArgumentNullException.ThrowIfNull(observeRisingEdge);
        ArgumentNullException.ThrowIfNull(deliver);

        // Always sample R so presses made while another application owns the
        // foreground cannot become delayed presses when FFVII regains focus.
        return observeRisingEdge(VirtualKeyR) && Repeat(deliver);
    }

    public bool Repeat(Func<string, bool> deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        string text;
        lock (sync)
        {
            text = lastDelivered;
        }

        return text.Length > 0 && deliver(text);
    }
}
