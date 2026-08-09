namespace Ff7.Accessibility.Reloaded;

public sealed class MateriaTutorialSpeechTracker
{
    private const int MenuModule = 5;
    private const int TutorialContext = 0;
    private static readonly TimeSpan EvidenceWindow = TimeSpan.FromMilliseconds(2500);

    private readonly object sync = new();
    private readonly Queue<string> speechQueue = new();
    private readonly List<ObservedInstruction> unconfirmedInstructions = new();
    private readonly HashSet<string> unconfirmedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> spokenKeys = new(StringComparer.Ordinal);
    private DateTime lastEvidenceAt = DateTime.MinValue;
    private DateTime lastSentinelAt = DateTime.MinValue;

    public void Observe(MenuTextRenderEntry entry, byte currentModule, DateTime now)
    {
        if (currentModule != MenuModule || entry.Context != TutorialContext)
        {
            return;
        }

        lock (sync)
        {
            ResetExpired(now);
            if (IsTutorialSentinel(entry))
            {
                lastEvidenceAt = now;
                lastSentinelAt = now;
                ConfirmPendingInstructions(now);
                return;
            }

            if (!IsInstruction(entry))
            {
                return;
            }

            lastEvidenceAt = now;
            var text = Ff7EncodedTextDecoder.NormalizeWhitespace(entry.Text);
            if (spokenKeys.Contains(text) || !unconfirmedKeys.Add(text))
            {
                return;
            }

            unconfirmedInstructions.Add(new ObservedInstruction(text, entry.Y, now));
            if (IsRecent(lastSentinelAt, now))
            {
                ConfirmPendingInstructions(now);
            }
        }
    }

    public bool IsActive(DateTime now)
    {
        lock (sync)
        {
            ResetExpired(now);
            return IsRecent(lastEvidenceAt, now);
        }
    }

    public string? Poll(DateTime now)
    {
        lock (sync)
        {
            ResetExpired(now);
            return speechQueue.Count == 0 ? null : speechQueue.Dequeue();
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            ResetSession();
        }
    }

    private void ConfirmPendingInstructions(DateTime now)
    {
        foreach (var instruction in unconfirmedInstructions
            .Where(instruction => now - instruction.SeenAt <= EvidenceWindow)
            .OrderBy(instruction => instruction.SeenAt)
            .ThenBy(instruction => instruction.Y))
        {
            if (spokenKeys.Add(instruction.Text))
            {
                speechQueue.Enqueue(instruction.Text);
            }
        }

        unconfirmedInstructions.Clear();
        unconfirmedKeys.Clear();
    }

    private void ResetExpired(DateTime now)
    {
        if (lastEvidenceAt != DateTime.MinValue && now - lastEvidenceAt > EvidenceWindow)
        {
            ResetSession();
        }
    }

    private void ResetSession()
    {
        speechQueue.Clear();
        unconfirmedInstructions.Clear();
        unconfirmedKeys.Clear();
        spokenKeys.Clear();
        lastEvidenceAt = DateTime.MinValue;
        lastSentinelAt = DateTime.MinValue;
    }

    private static bool IsTutorialSentinel(MenuTextRenderEntry entry) =>
        entry.X is >= 40 and <= 100 &&
        entry.Y >= 400 &&
        entry.Text.Any(char.IsLetterOrDigit);

    private static bool IsInstruction(MenuTextRenderEntry entry) =>
        entry.Color == 7 &&
        entry.X <= 120 &&
        entry.Y <= 220 &&
        entry.Text.Length >= 4 &&
        entry.Text.Any(char.IsLetter);

    private static bool IsRecent(DateTime seenAt, DateTime now) =>
        seenAt != DateTime.MinValue && now - seenAt <= EvidenceWindow;

    private readonly record struct ObservedInstruction(string Text, uint Y, DateTime SeenAt);
}
