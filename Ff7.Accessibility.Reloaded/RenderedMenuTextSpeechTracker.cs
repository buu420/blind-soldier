namespace Ff7.Accessibility.Reloaded;

public sealed class RenderedMenuTextSpeechTracker
{
    private readonly TimeSpan settleTime;
    private readonly object sync = new();
    private Candidate? pending;
    private string lastSpokenKey = string.Empty;

    public RenderedMenuTextSpeechTracker(TimeSpan settleTime)
    {
        this.settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
    }

    public void Observe(MenuTextRenderEntry entry, DateTime now)
    {
        if (!LooksLikeSpeechCandidate(entry.Text))
        {
            return;
        }

        var candidate = new Candidate(entry, now, GetPriority(entry), CreateKey(entry));
        lock (sync)
        {
            if (pending is null || candidate.Priority >= pending.Value.Priority)
            {
                pending = candidate;
            }
        }
    }

    public string? Poll(DateTime now)
    {
        lock (sync)
        {
            if (pending is null)
            {
                return null;
            }

            var candidate = pending.Value;
            if (now - candidate.SeenAt < settleTime)
            {
                return null;
            }

            pending = null;
            if (string.Equals(candidate.Key, lastSpokenKey, StringComparison.Ordinal))
            {
                return null;
            }

            lastSpokenKey = candidate.Key;
            return candidate.Entry.Text;
        }
    }

    public void DiscardPending()
    {
        lock (sync)
        {
            pending = null;
        }
    }

    private static bool LooksLikeSpeechCandidate(string text)
    {
        if (text.Length < 2)
        {
            return false;
        }

        return text.Any(char.IsLetterOrDigit);
    }

    private static int GetPriority(MenuTextRenderEntry entry) =>
        entry.Color == unchecked((int)0xffffffff) ? 0 : 1;

    private static string CreateKey(MenuTextRenderEntry entry) =>
        $"{entry.Text}\u001f{entry.X}\u001f{entry.Y}\u001f{entry.Color}\u001f{entry.Context}";

    private readonly record struct Candidate(MenuTextRenderEntry Entry, DateTime SeenAt, int Priority, string Key);
}
