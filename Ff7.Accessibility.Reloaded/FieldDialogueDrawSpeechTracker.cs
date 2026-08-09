using System.Text;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldDialogueDrawSpeechTracker
{
    private const int RootMainMenuContext = 0x3A83126F;
    private const int ConfigHelpContext = 0x3DCCCCCD;
    private const int ItemMenuContext = 0x3DCED917;
    private const int FieldStatusContext = 0x3E99999A;
    private const int ClockGilContext = 0x3E4CCCCD;
    private const int DialogPromptContext = 0x3C23D70A;
    private const int NameEntryModule = 5;
    private static readonly TimeSpan VisibleRefreshGrace = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan CandidateLifetime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RepeatSuppression = TimeSpan.FromSeconds(4);

    private readonly TimeSpan stableTime;
    private readonly object sync = new();
    private readonly Dictionary<string, Candidate> candidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> lastSpokenByText = new(StringComparer.Ordinal);

    public FieldDialogueDrawSpeechTracker(TimeSpan stableTime)
    {
        this.stableTime = stableTime < TimeSpan.Zero ? TimeSpan.Zero : stableTime;
    }

    public void Observe(MenuTextRenderEntry entry, int currentModule, DateTime now)
    {
        if (!TryNormalizeCandidate(entry, currentModule, out var normalized))
        {
            return;
        }

        lock (sync)
        {
            Prune(now);
            if (!candidates.TryGetValue(normalized.Text, out var candidate))
            {
                candidates[normalized.Text] = new Candidate(normalized, now, now, 1);
                return;
            }

            candidate.Entry = normalized;
            candidate.LastSeenAt = now;
            candidate.SeenCount++;
        }
    }

    public string? Poll(DateTime now)
    {
        lock (sync)
        {
            Prune(now);
            var candidate = candidates.Values
                .Where(item => !item.Spoken &&
                    item.SeenCount >= 2 &&
                    now - item.FirstSeenAt >= stableTime &&
                    now - item.LastSeenAt <= VisibleRefreshGrace &&
                    IsPastRepeatSuppression(item.Entry.Text, now))
                .OrderBy(item => item.Entry.Y)
                .ThenBy(item => item.Entry.X)
                .FirstOrDefault();

            if (candidate is null)
            {
                return null;
            }

            candidate.Spoken = true;
            lastSpokenByText[candidate.Entry.Text] = now;
            return candidate.Entry.Text;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            candidates.Clear();
            lastSpokenByText.Clear();
        }
    }

    private bool IsPastRepeatSuppression(string text, DateTime now)
    {
        return !lastSpokenByText.TryGetValue(text, out var spokenAt) ||
            now - spokenAt >= RepeatSuppression;
    }

    private void Prune(DateTime now)
    {
        foreach (var key in candidates
            .Where(item => now - item.Value.LastSeenAt > CandidateLifetime)
            .Select(item => item.Key)
            .ToList())
        {
            candidates.Remove(key);
        }

        foreach (var key in lastSpokenByText
            .Where(item => now - item.Value > RepeatSuppression)
            .Select(item => item.Key)
            .ToList())
        {
            lastSpokenByText.Remove(key);
        }
    }

    private static bool TryNormalizeCandidate(MenuTextRenderEntry entry, int currentModule, out MenuTextRenderEntry normalized)
    {
        normalized = default;
        var text = NormalizeText(entry.Text);
        if (!IsAllowedModule(entry, currentModule, text))
        {
            return false;
        }

        if (!LooksLikeSpeechCandidate(text))
        {
            return false;
        }

        normalized = entry with { Text = text };
        if (currentModule == NameEntryModule && IsNameEntryPrompt(normalized, text))
        {
            return true;
        }

        return !IsIgnoredText(normalized);
    }

    private static bool IsAllowedModule(MenuTextRenderEntry entry, int currentModule, string normalizedText)
    {
        if (currentModule == FieldPositionReader.FieldModule)
        {
            return true;
        }

        return currentModule == NameEntryModule && IsNameEntryPrompt(entry, normalizedText);
    }

    private static bool IsNameEntryPrompt(MenuTextRenderEntry entry, string normalizedText)
    {
        return entry.Context == ItemMenuContext &&
            entry.X <= 80 &&
            entry.Y <= 64 &&
            LooksLikeSpeechCandidate(normalizedText);
    }

    private static bool IsIgnoredText(MenuTextRenderEntry entry)
    {
        if (entry.X > 640 || entry.Y > 480)
        {
            return true;
        }

        if (entry.Context is RootMainMenuContext or FieldStatusContext or ClockGilContext)
        {
            return true;
        }

        if (IsTitleMenuChoiceText(entry))
        {
            return true;
        }

        if (entry.Context == ItemMenuContext &&
            ((entry.Y <= 48 && entry.X is >= 40 and <= 340) ||
             (entry.X >= 480 && entry.Y <= 80)))
        {
            return true;
        }

        if (entry.Context == DialogPromptContext && entry.Text.Length <= 12)
        {
            return true;
        }

        if (entry.Context == ItemMenuContext && entry.X >= 300 && entry.Color == 0x107)
        {
            return true;
        }

        if (entry.Context == ConfigHelpContext && (entry.X >= 300 || entry.Y >= 340 || entry.X <= 32 && entry.Y <= 32))
        {
            return true;
        }

        return false;
    }

    private static bool IsTitleMenuChoiceText(MenuTextRenderEntry entry)
    {
        if (entry.Context != ConfigHelpContext ||
            entry.X is < 220 or > 340 ||
            entry.Y is < 160 or > 260)
        {
            return false;
        }

        return LooksLikeSpeechCandidate(entry.Text);
    }

    private static bool LooksLikeSpeechCandidate(string text)
    {
        if (text.Length < 2)
        {
            return false;
        }

        return text.Any(char.IsLetterOrDigit);
    }

    private static string NormalizeText(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(rawText.Length);
        foreach (var ch in rawText)
        {
            if (ch == '\0')
            {
                break;
            }

            if (char.IsWhiteSpace(ch))
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return CollapseWhitespace(builder.ToString().Trim());
    }

    private static string CollapseWhitespace(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var ch in text)
        {
            if (ch == ' ')
            {
                if (!previousWasSpace)
                {
                    builder.Append(ch);
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(ch);
            previousWasSpace = false;
        }

        return builder.ToString();
    }

    private sealed class Candidate
    {
        public Candidate(MenuTextRenderEntry entry, DateTime firstSeenAt, DateTime lastSeenAt, int seenCount)
        {
            Entry = entry;
            FirstSeenAt = firstSeenAt;
            LastSeenAt = lastSeenAt;
            SeenCount = seenCount;
        }

        public MenuTextRenderEntry Entry { get; set; }

        public DateTime FirstSeenAt { get; }

        public DateTime LastSeenAt { get; set; }

        public int SeenCount { get; set; }

        public bool Spoken { get; set; }
    }
}
