namespace Ff7.Accessibility.Reloaded;

public sealed class StaticMenuCursorSpeechTracker
{
    private const int MenuModule = 5;
    private const int RootMainMenuContext = 0x3A83126F;
    private const int ConfigContext = 0x3DCCCCCD;
    private const int QuitPromptContext = 0x3C23D70A;
    private const int ConfigRowCount = 10;
    private static readonly TimeSpan ScreenEvidenceWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan NewScreenGap = TimeSpan.FromMilliseconds(250);

    private readonly TimeSpan settleTime;
    private readonly object sync = new();
    private readonly Dictionary<TextPosition, ObservedText> recentText = new();
    private DateTime lastConfigTitleAt = DateTime.MinValue;
    private DateTime lastQuitPromptAt = DateTime.MinValue;
    private int configGeneration;
    private int quitGeneration;
    private PendingConfigRow? pendingConfigRow;
    private PendingCursor? pendingCursor;
    private PendingQuitChoice? pendingQuitChoice;
    private DateTime lastQuitCursorAt = DateTime.MinValue;
    private string lastSpokenKey = string.Empty;

    public StaticMenuCursorSpeechTracker(TimeSpan settleTime)
    {
        this.settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
    }

    public void ObserveDraw(MenuTextRenderEntry entry, DateTime now)
    {
        if (!LooksLikeSpeechCandidate(entry.Text))
        {
            return;
        }

        lock (sync)
        {
            Prune(now);
            if (IsConfigTitle(entry))
            {
                if (ObserveScreenTitle(ref lastConfigTitleAt, ref configGeneration, now))
                {
                    pendingConfigRow = null;
                    pendingCursor = null;
                    pendingQuitChoice = null;
                }
            }
            else if (IsQuitPrompt(entry))
            {
                if (ObserveScreenTitle(ref lastQuitPromptAt, ref quitGeneration, now))
                {
                    // A prior x86 cursor can still be inside the evidence
                    // window when a newly opened x64 dialog supplies only
                    // selected-text color. Never let that stale cursor block
                    // or resolve the new dialog.
                    pendingCursor = null;
                    pendingQuitChoice = null;
                    lastQuitCursorAt = DateTime.MinValue;
                }
            }

            if (entry.Context is ConfigContext or QuitPromptContext)
            {
                recentText[new TextPosition(entry.Context, entry.X, entry.Y)] = new ObservedText(entry, now);
            }

            if (IsQuitChoice(entry) &&
                entry.Color == 0 &&
                IsRecent(lastQuitPromptAt, now) &&
                !IsRecent(lastQuitCursorAt, now))
            {
                QueueQuitChoice(quitGeneration, entry.Text.Trim(), now);
            }
        }
    }

    public void ObserveCursor(MenuCursorDrawObservation cursor, DateTime now)
    {
        if (cursor.CurrentModule != MenuModule)
        {
            return;
        }

        lock (sync)
        {
            Prune(now);
            if (IsConfigCursor(cursor) && IsRecent(lastConfigTitleAt, now))
            {
                QueueCursor(StaticMenuKind.Config, configGeneration, cursor, now);
            }
            else if (IsQuitCursor(cursor) && IsRecent(lastQuitPromptAt, now))
            {
                lastQuitCursorAt = now;
                pendingQuitChoice = null;
                QueueCursor(StaticMenuKind.Quit, quitGeneration, cursor, now);
            }
        }
    }

    public void ObserveConfigRow(int rowIndex, DateTime now)
    {
        if (rowIndex is < 0 or >= ConfigRowCount)
        {
            return;
        }

        lock (sync)
        {
            Prune(now);
            if (!IsRecent(lastConfigTitleAt, now) || !HasConfigLayoutEvidence(now))
            {
                return;
            }

            // This x86 build updates a native Config row index but does not
            // consistently call either cursor renderer. The module byte can
            // transition independently while the in-game menu animates, so the
            // rendered Config title establishes ownership and native row state
            // supplies the authoritative selection.
            if (pendingCursor is { Kind: StaticMenuKind.Config })
            {
                pendingCursor = null;
            }

            if (pendingConfigRow is { } current &&
                current.Generation == configGeneration &&
                current.RowIndex == rowIndex)
            {
                return;
            }

            pendingConfigRow = new PendingConfigRow(configGeneration, rowIndex, now);
        }
    }

    public string? Poll(
        DateTime now,
        Func<string, NativeMenuSelection?>? resolveNativeValue = null)
    {
        lock (sync)
        {
            Prune(now);
            if (pendingConfigRow is { } configRow)
            {
                if (now - configRow.SeenAt < settleTime)
                {
                    return null;
                }

                if (!IsRecent(lastConfigTitleAt, now))
                {
                    pendingConfigRow = null;
                    return null;
                }

                var configSpeech = BuildConfigSpeech(configRow.RowIndex, resolveNativeValue);
                if (string.IsNullOrWhiteSpace(configSpeech))
                {
                    return null;
                }

                var configKey = $"{StaticMenuKind.Config}\u001f{configRow.Generation}\u001fnative-row\u001f{configRow.RowIndex}\u001f{configSpeech}";
                if (string.Equals(configKey, lastSpokenKey, StringComparison.Ordinal))
                {
                    return null;
                }

                lastSpokenKey = configKey;
                return configSpeech;
            }

            if (pendingCursor is { } pending)
            {
                if (now - pending.SeenAt < settleTime)
                {
                    return null;
                }

                var screenSeenAt = pending.Kind == StaticMenuKind.Config
                    ? lastConfigTitleAt
                    : lastQuitPromptAt;
                if (!IsRecent(screenSeenAt, now))
                {
                    pendingCursor = null;
                    return null;
                }

                if (pending.Kind == StaticMenuKind.Config && !HasConfigLayoutEvidence(now))
                {
                    return null;
                }

                var speech = pending.Kind == StaticMenuKind.Config
                    ? BuildConfigSpeech(pending.Cursor, resolveNativeValue)
                    : BuildQuitSpeech(pending.Cursor);
                if (string.IsNullOrWhiteSpace(speech))
                {
                    return null;
                }

                var key = $"{pending.Kind}\u001f{pending.Generation}\u001f{pending.Cursor.X}\u001f{pending.Cursor.Y}\u001f{speech}";
                if (string.Equals(key, lastSpokenKey, StringComparison.Ordinal))
                {
                    return null;
                }

                lastSpokenKey = key;
                return speech;
            }

            if (pendingQuitChoice is not { } choice ||
                now - choice.SeenAt < settleTime ||
                !IsRecent(lastQuitPromptAt, now))
            {
                return null;
            }

            var choiceKey = $"{StaticMenuKind.Quit}\u001f{choice.Generation}\u001fdraw\u001f{choice.Text}";
            if (string.Equals(choiceKey, lastSpokenKey, StringComparison.Ordinal))
            {
                return null;
            }

            var choiceSpeech = choice.Text;
            lastSpokenKey = choiceKey;
            return choiceSpeech;
        }
    }

    public void DiscardPending()
    {
        lock (sync)
        {
            pendingConfigRow = null;
            pendingCursor = null;
            pendingQuitChoice = null;
            recentText.Clear();
            lastConfigTitleAt = DateTime.MinValue;
            lastQuitPromptAt = DateTime.MinValue;
            lastQuitCursorAt = DateTime.MinValue;
        }
    }

    private string? BuildConfigSpeech(
        MenuCursorDrawObservation cursor,
        Func<string, NativeMenuSelection?>? resolveNativeValue)
    {
        var rows = recentText.Values
            .Where(item => item.Entry.Context == ConfigContext)
            .Where(item => item.Entry.X is >= 40 and <= 180)
            .Where(item => item.Entry.Y is >= 60 and <= 460)
            .OrderBy(item => Math.Abs((int)item.Entry.Y - cursor.Y))
            .ThenByDescending(item => item.SeenAt)
            .ToList();
        if (rows.Count == 0 || Math.Abs((int)rows[0].Entry.Y - cursor.Y) > 10)
        {
            return null;
        }

        return BuildConfigSpeech(rows[0].Entry, resolveNativeValue);
    }

    private string? BuildConfigSpeech(
        int rowIndex,
        Func<string, NativeMenuSelection?>? resolveNativeValue)
    {
        var rows = recentText.Values
            .Where(item => item.Entry.Context == ConfigContext)
            .Where(item => item.Entry.X is >= 40 and <= 180)
            .Where(item => item.Entry.Y is >= 60 and <= 460)
            .OrderBy(item => item.Entry.Y)
            .ThenBy(item => item.Entry.X)
            .Select(item => item.Entry)
            .ToList();
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            return null;
        }

        return BuildConfigSpeech(rows[rowIndex], resolveNativeValue);
    }

    private string BuildConfigSpeech(
        MenuTextRenderEntry selectedRow,
        Func<string, NativeMenuSelection?>? resolveNativeValue)
    {
        var values = recentText.Values
            .Where(item => item.Entry.Context == ConfigContext)
            .Where(item => item.Entry.X >= 220)
            .Where(item => Math.Abs((int)item.Entry.Y - (int)selectedRow.Y) <= 10)
            .OrderBy(item => item.Entry.X)
            .Select(item => item.Entry)
            .ToList();
        var nativeValue = resolveNativeValue?.Invoke(selectedRow.Text.Trim());
        var highlightedValues = values.Where(item => item.Color == 7).ToList();
        if (nativeValue is null && highlightedValues.Count > 0)
        {
            values = highlightedValues;
        }

        var help = recentText.Values
            .Where(item => item.Entry.Context == ConfigContext)
            .Where(item => item.Entry.X <= 32 && item.Entry.Y <= 32)
            .OrderByDescending(item => item.SeenAt)
            .Select(item => item.Entry.Text.Trim())
            .FirstOrDefault();

        var parts = new List<string> { selectedRow.Text.Trim() };
        if (nativeValue is { Text.Length: > 0 } currentValue)
        {
            parts.Add(currentValue.Text.Trim());
        }
        else if (values.Count > 0)
        {
            parts.Add(string.Join(", ", values.Select(item => item.Text.Trim()).Distinct(StringComparer.Ordinal)));
        }

        if (!string.IsNullOrWhiteSpace(help) && !parts.Contains(help, StringComparer.Ordinal))
        {
            parts.Add(help);
        }

        return string.Join(". ", parts);
    }

    private string? BuildQuitSpeech(MenuCursorDrawObservation cursor)
    {
        var lowResolution = IsLowResolutionQuitCursor(cursor);
        var minimumY = lowResolution ? 130u : 260u;
        var maximumY = lowResolution ? 160u : 320u;
        var minimumGap = lowResolution ? 10 : 20;
        var maximumGap = lowResolution ? 50 : 90;
        var preferredGap = lowResolution ? 25 : 50;
        var selected = recentText.Values
            .Where(item => item.Entry.Context == QuitPromptContext)
            .Where(item => item.Entry.Y >= minimumY && item.Entry.Y <= maximumY)
            .Select(item => new
            {
                item.Entry,
                VerticalDistance = Math.Abs((int)item.Entry.Y - cursor.Y),
                HorizontalGap = (int)item.Entry.X - cursor.X
            })
            .Where(item => item.VerticalDistance <= 12)
            .Where(item => item.HorizontalGap >= minimumGap && item.HorizontalGap <= maximumGap)
            .OrderBy(item => item.VerticalDistance)
            .ThenBy(item => Math.Abs(item.HorizontalGap - preferredGap))
            .FirstOrDefault();
        return selected?.Entry.Text.Trim();
    }

    private void QueueCursor(StaticMenuKind kind, int generation, MenuCursorDrawObservation cursor, DateTime now)
    {
        if (pendingCursor is { } current &&
            current.Kind == kind &&
            current.Generation == generation &&
            current.Cursor.X == cursor.X &&
            current.Cursor.Y == cursor.Y)
        {
            return;
        }

        pendingCursor = new PendingCursor(kind, generation, cursor, now);
    }

    private void QueueQuitChoice(int generation, string text, DateTime now)
    {
        if (pendingQuitChoice is { } current &&
            current.Generation == generation &&
            string.Equals(current.Text, text, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        pendingQuitChoice = new PendingQuitChoice(generation, text, now);
    }

    private void Prune(DateTime now)
    {
        foreach (var key in recentText
            .Where(item => now - item.Value.SeenAt > ScreenEvidenceWindow)
            .Select(item => item.Key)
            .ToList())
        {
            recentText.Remove(key);
        }
    }

    private static bool ObserveScreenTitle(ref DateTime lastSeenAt, ref int generation, DateTime now)
    {
        var isNewScreen = lastSeenAt == DateTime.MinValue || now - lastSeenAt > NewScreenGap;
        if (isNewScreen)
        {
            generation++;
        }

        lastSeenAt = now;
        return isNewScreen;
    }

    private static bool IsRecent(DateTime seenAt, DateTime now) =>
        seenAt != DateTime.MinValue && now - seenAt <= ScreenEvidenceWindow;

    private static bool IsConfigTitle(MenuTextRenderEntry entry) =>
        entry.Context == RootMainMenuContext &&
        entry.X == 508 &&
        entry.Y <= 20 &&
        LooksLikeSpeechCandidate(entry.Text);

    private static bool IsQuitPrompt(MenuTextRenderEntry entry) =>
        entry.Context == QuitPromptContext &&
        ((entry.X is >= 200 and <= 240 && entry.Y is >= 140 and <= 175) ||
         (entry.X is >= 100 and <= 120 && entry.Y is >= 70 and <= 90)) &&
        entry.Text.Trim().Length >= 4 &&
        LooksLikeSpeechCandidate(entry.Text);

    private static bool IsQuitChoice(MenuTextRenderEntry entry) =>
        entry.Context == QuitPromptContext &&
        (((entry.X is >= 180 and <= 260) || (entry.X is >= 380 and <= 460)) &&
            entry.Y is >= 260 and <= 320 ||
         ((entry.X is >= 90 and <= 130) || (entry.X is >= 190 and <= 230)) &&
            entry.Y is >= 130 and <= 160) &&
        LooksLikeSpeechCandidate(entry.Text);

    private bool HasConfigLayoutEvidence(DateTime now)
    {
        var hasHelp = recentText.Values.Any(item =>
            item.Entry.Context == ConfigContext &&
            item.Entry.X <= 32 &&
            item.Entry.Y <= 32 &&
            IsRecent(item.SeenAt, now));
        if (!hasHelp)
        {
            return false;
        }

        return recentText.Values
            .Where(item => item.Entry.Context == ConfigContext &&
                item.Entry.X is >= 40 and <= 180 &&
                item.Entry.Y is >= 60 and <= 460 &&
                IsRecent(item.SeenAt, now))
            .Select(item => item.Entry.Y)
            .Distinct()
            .Any();
    }

    private static bool IsConfigCursor(MenuCursorDrawObservation cursor) =>
        cursor.Context == ConfigContext &&
        cursor.X is >= 0 and <= 20 &&
        cursor.Y is >= 70 and <= 455;

    private static bool IsQuitCursor(MenuCursorDrawObservation cursor) =>
        cursor.Context == RootMainMenuContext &&
        ((cursor.X is >= 140 and <= 390 && cursor.Y is >= 290 and <= 315) ||
         IsLowResolutionQuitCursor(cursor));

    private static bool IsLowResolutionQuitCursor(MenuCursorDrawObservation cursor) =>
        cursor.Context == RootMainMenuContext &&
        cursor.X is >= 70 and <= 200 &&
        cursor.Y is >= 145 and <= 160;

    private static bool LooksLikeSpeechCandidate(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Any(char.IsLetterOrDigit);

    private enum StaticMenuKind
    {
        Config,
        Quit
    }

    private readonly record struct TextPosition(int Context, uint X, uint Y);

    private readonly record struct ObservedText(MenuTextRenderEntry Entry, DateTime SeenAt);

    private readonly record struct PendingCursor(
        StaticMenuKind Kind,
        int Generation,
        MenuCursorDrawObservation Cursor,
        DateTime SeenAt);

    private readonly record struct PendingConfigRow(
        int Generation,
        int RowIndex,
        DateTime SeenAt);

    private readonly record struct PendingQuitChoice(
        int Generation,
        string Text,
        DateTime SeenAt);
}
