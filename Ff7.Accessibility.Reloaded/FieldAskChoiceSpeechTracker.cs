namespace Ff7.Accessibility.Reloaded;

public static class FieldAskTextFormatter
{
    public static string FormatPrompt(
        IReadOnlyList<string> lines,
        int firstQuestionLine,
        int lastQuestionLine)
    {
        if (!HasValidQuestionRange(lines, firstQuestionLine, lastQuestionLine))
        {
            return string.Empty;
        }

        return Ff7EncodedTextDecoder.NormalizeWhitespace(string.Join(
            ' ',
            lines.Where((line, index) =>
                index < firstQuestionLine)));
    }

    public static string GetChoice(
        IReadOnlyList<string> lines,
        int firstQuestionLine,
        int lastQuestionLine,
        int currentQuestionLine)
    {
        if (!HasValidQuestionRange(lines, firstQuestionLine, lastQuestionLine) ||
            currentQuestionLine < firstQuestionLine ||
            currentQuestionLine > lastQuestionLine)
        {
            return string.Empty;
        }

        var text = Ff7EncodedTextDecoder.NormalizeWhitespace(lines[currentQuestionLine]);
        return text.Length == 0 ? "Blank" : text;
    }

    public static bool TryResolveChoicePage(
        IReadOnlyList<Ff7DecodedTextPage> pages,
        int firstQuestionLine,
        int lastQuestionLine,
        out IReadOnlyList<string> lines)
    {
        lines = Array.Empty<string>();
        if (pages.Count == 0 ||
            firstQuestionLine < 0 ||
            lastQuestionLine < firstQuestionLine)
        {
            return false;
        }

        // FUN_00631945 enters phase 14 at E8/E9 page breaks, and phase 6 only at
        // the FF string terminator. FUN_006310A1 handles the ASK selector only in
        // phase 6. Its first/last row numbers therefore belong to the final page;
        // E0 indentation is optional formatting, not evidence of a choice.
        // Callers still require that this complete page is actually visible before
        // publishing it. Blank rows are real selectable rows (the Mansion safe).
        var resolved = pages[^1];
        if (lastQuestionLine >= resolved.Lines.Count)
        {
            return false;
        }

        lines = resolved.Lines.Select(line => line.Text).ToArray();
        return true;
    }

    public static bool IsChoicePageVisible(
        IReadOnlyList<string> pageLines,
        string visibleText)
    {
        if (pageLines.Count == 0 || string.IsNullOrWhiteSpace(visibleText))
        {
            return false;
        }

        var expected = Ff7EncodedTextDecoder.NormalizeWhitespace(
            string.Join(' ', pageLines));
        var visible = Ff7EncodedTextDecoder.NormalizeWhitespace(visibleText);
        return expected.Length != 0 &&
            string.Equals(expected, visible, StringComparison.Ordinal);
    }

    private static bool HasValidQuestionRange(
        IReadOnlyList<string> lines,
        int firstQuestionLine,
        int lastQuestionLine) =>
        firstQuestionLine >= 0 &&
        lastQuestionLine >= firstQuestionLine &&
        lastQuestionLine < lines.Count;
}

public sealed class FieldAskChoiceSpeechTracker
{
    private readonly object sync = new();
    private FieldAskIdentity? identity;
    private int lastQuestionLine = -1;
    private string? pending;
    private int pendingPosition;
    private int choiceCount;
    private bool openingChoiceTaken;

    public void Observe(FieldAskChoiceObservation observation)
    {
        if (!observation.IsValid)
        {
            return;
        }

        var choice = FieldAskTextFormatter.GetChoice(
            observation.Lines,
            observation.FirstQuestionLine,
            observation.LastQuestionLine,
            observation.CurrentQuestionLine);
        if (choice.Length == 0)
        {
            return;
        }

        lock (sync)
        {
            var nextIdentity = new FieldAskIdentity(
                observation.FieldId,
                observation.WindowId,
                observation.DialogId,
                observation.FirstQuestionLine,
                observation.LastQuestionLine,
                observation.LifecycleToken);
            if (identity != nextIdentity)
            {
                identity = nextIdentity;
                lastQuestionLine = -1;
                pending = null;
                openingChoiceTaken = false;
            }

            if (lastQuestionLine == observation.CurrentQuestionLine)
            {
                return;
            }

            lastQuestionLine = observation.CurrentQuestionLine;
            pending = choice;
            pendingPosition = observation.CurrentQuestionLine - observation.FirstQuestionLine + 1;
            choiceCount = observation.LastQuestionLine - observation.FirstQuestionLine + 1;
        }
    }

    public string? Poll(long? lifecycleToken = null)
    {
        lock (sync)
        {
            if (lifecycleToken is not null && identity?.LifecycleToken != lifecycleToken)
            {
                return null;
            }

            var result = pending;
            pending = null;
            if (result is not null && !openingChoiceTaken)
            {
                // The list's first spoken option also says it is one of several.
                openingChoiceTaken = true;
                result = Ff7.Accessibility.Core.DialogueChoiceSpeech.FormatOpening(result, pendingPosition, choiceCount);
            }

            return result;
        }
    }

    public void Reset(long? lifecycleToken = null)
    {
        lock (sync)
        {
            if (lifecycleToken is not null && identity?.LifecycleToken != lifecycleToken)
            {
                return;
            }

            identity = null;
            lastQuestionLine = -1;
            pending = null;
            openingChoiceTaken = false;
        }
    }

    private readonly record struct FieldAskIdentity(
        int FieldId,
        int WindowId,
        int DialogId,
        int FirstQuestionLine,
        int LastQuestionLine,
        long LifecycleToken);
}

public readonly record struct FieldAskChoiceObservation(
    bool IsValid,
    int FieldId,
    int WindowId,
    int DialogId,
    int FirstQuestionLine,
    int LastQuestionLine,
    int CurrentQuestionLine,
    IReadOnlyList<string> Lines,
    long LifecycleToken = 0);
