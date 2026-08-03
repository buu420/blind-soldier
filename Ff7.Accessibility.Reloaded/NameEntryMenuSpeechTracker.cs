namespace Ff7.Accessibility.Reloaded;

public sealed class NameEntryMenuSpeechTracker
{
    public const int NameEntryModule = 5;

    private static readonly HashSet<string> CommandTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Space",
        "Delete",
        "Select",
        "Default"
    };

    private const int GridLeftCursorX = 33;
    private const int GridTopCursorY = 214;
    private const int GridCellWidth = 33;
    private const int GridCellHeight = 34;
    private static readonly TimeSpan TextRetention = TimeSpan.FromSeconds(3);

    private readonly TimeSpan settleTime;
    private readonly object sync = new();
    private readonly List<ObservedText> recentTexts = [];
    private PendingSelection? pendingSelection;
    private string lastObservedKey = string.Empty;
    private string lastSpokenKey = string.Empty;

    public NameEntryMenuSpeechTracker(TimeSpan settleTime)
    {
        this.settleTime = settleTime;
    }

    public void ObserveText(MenuTextRenderEntry entry, int currentModule, DateTime now)
    {
        lock (sync)
        {
            if (currentModule != NameEntryModule)
            {
                return;
            }

            if (!IsSelectableText(entry))
            {
                return;
            }

            PruneOldText(now);
            var key = CreateTextKey(entry);
            for (var i = recentTexts.Count - 1; i >= 0; i--)
            {
                if (string.Equals(recentTexts[i].Key, key, StringComparison.Ordinal))
                {
                    recentTexts.RemoveAt(i);
                }
            }

            recentTexts.Add(new ObservedText(entry, now, key));
        }
    }

    public NameEntryCursorObservation? ObserveCursor(NameEntryCursorSnapshot snapshot, DateTime now)
    {
        lock (sync)
        {
            if (snapshot.CurrentModule != NameEntryModule)
            {
                Clear();
                return null;
            }

            PruneOldText(now);
            if (!TryResolveText(snapshot, now, out var text, out var source))
            {
                return new NameEntryCursorObservation(snapshot, null, CreateDiagnosticKey(snapshot, "unmapped"), "unmapped");
            }

            var key = $"name\u001f{text}\u001f{source}";
            var observation = new NameEntryCursorObservation(snapshot, text, CreateDiagnosticKey(snapshot, key), source);
            if (string.Equals(key, lastObservedKey, StringComparison.Ordinal))
            {
                return observation;
            }

            lastObservedKey = key;
            pendingSelection = new PendingSelection(text, key, now);
            return observation;
        }
    }

    public string? Poll(DateTime now)
    {
        lock (sync)
        {
            if (pendingSelection is null)
            {
                return null;
            }

            if (now - pendingSelection.Value.SeenAt < settleTime)
            {
                return null;
            }

            var selection = pendingSelection.Value;
            pendingSelection = null;
            if (string.Equals(selection.Key, lastSpokenKey, StringComparison.Ordinal))
            {
                return null;
            }

            lastSpokenKey = selection.Key;
            return selection.Text;
        }
    }

    private bool TryResolveText(NameEntryCursorSnapshot snapshot, DateTime now, out string text, out string source)
    {
        if (TryResolveRecentDrawnText(snapshot, now, out text))
        {
            source = "drawn";
            return true;
        }

        if (!HasRecentNameEntryScreenText(now))
        {
            source = string.Empty;
            return false;
        }

        if (TryResolveGridText(snapshot, out text))
        {
            source = "grid";
            return true;
        }

        source = string.Empty;
        return false;
    }

    private bool HasRecentNameEntryScreenText(DateTime now)
    {
        var commandLabelCount = recentTexts
            .Where(item => now - item.SeenAt <= TextRetention)
            .Select(item => item.Entry.Text.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(CommandTexts.Contains);
        return commandLabelCount >= 2;
    }

    private bool TryResolveRecentDrawnText(NameEntryCursorSnapshot snapshot, DateTime now, out string text)
    {
        var bestDistance = int.MaxValue;
        ObservedText? best = null;
        foreach (var item in recentTexts)
        {
            if (now - item.SeenAt > TextRetention)
            {
                continue;
            }

            var dx = Math.Abs((int)item.Entry.X - snapshot.X);
            var dy = Math.Abs((int)item.Entry.Y - snapshot.Y);
            if (dy > 18 || dx > 100)
            {
                continue;
            }

            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = item;
            }
        }

        if (best is not null)
        {
            text = NormalizeSpokenText(best.Value.Entry.Text);
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static bool TryResolveGridText(NameEntryCursorSnapshot snapshot, out string text)
    {
        var row = NearestIndex(snapshot.Y, GridTopCursorY, GridCellHeight);
        var column = NearestIndex(snapshot.X, GridLeftCursorX, GridCellWidth);
        return NameEntryCharacterTable.TryGet(column, row, out text);
    }

    private static int NearestIndex(int value, int origin, int stride)
    {
        var raw = (double)(value - origin) / stride;
        var index = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        var expected = origin + (index * stride);
        return Math.Abs(value - expected) <= stride / 2 ? index : -1;
    }

    private static bool IsSelectableText(MenuTextRenderEntry entry)
    {
        var text = entry.Text.Trim();
        return CommandTexts.Contains(text) || text.Length == 1;
    }

    private static string NormalizeSpokenText(string text)
    {
        return text.Trim() switch
        {
            "," => "comma",
            "." => "period",
            "+" => "plus",
            "-" => "minus",
            ":" => "colon",
            ";" => "semicolon",
            var value => value
        };
    }

    private void PruneOldText(DateTime now)
    {
        for (var i = recentTexts.Count - 1; i >= 0; i--)
        {
            if (now - recentTexts[i].SeenAt > TextRetention)
            {
                recentTexts.RemoveAt(i);
            }
        }
    }

    private void Clear()
    {
        pendingSelection = null;
        lastObservedKey = string.Empty;
        lastSpokenKey = string.Empty;
        recentTexts.Clear();
    }

    private static string CreateTextKey(MenuTextRenderEntry entry) =>
        $"{entry.Text}\u001f{entry.X}\u001f{entry.Y}";

    private static string CreateDiagnosticKey(NameEntryCursorSnapshot snapshot, string resolvedKey) =>
        $"{snapshot.Source}\u001f{snapshot.CurrentModule}\u001f{snapshot.X}\u001f{snapshot.Y}\u001f{snapshot.Context}\u001f{resolvedKey}";

    private readonly record struct ObservedText(MenuTextRenderEntry Entry, DateTime SeenAt, string Key);

    private readonly record struct PendingSelection(string Text, string Key, DateTime SeenAt);
}

public readonly record struct NameEntryCursorSnapshot(
    string Source,
    int CurrentModule,
    int X,
    int Y,
    int Context);

public readonly record struct NameEntryCursorObservation(
    NameEntryCursorSnapshot Snapshot,
    string? SpokenText,
    string Key,
    string Source)
{
    public string ToLogLine()
    {
        var resolved = SpokenText is null ? "<unmapped>" : SpokenText;
        return $"Name entry cursor: {resolved} source={Snapshot.Source} module={Snapshot.CurrentModule} x={Snapshot.X} y={Snapshot.Y} context=0x{Snapshot.Context:X8} resolver={Source}";
    }
}
