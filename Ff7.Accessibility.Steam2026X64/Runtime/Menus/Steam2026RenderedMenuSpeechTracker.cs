namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

internal readonly record struct Steam2026RenderedMenuSpeechCandidate(
    string Key,
    string Text,
    long Sequence);

/// <summary>
/// Correlates a native cursor draw with a native text draw on the title menu.
/// It intentionally has no coordinate-to-label table: a selection exists only
/// when one unique rendered string shares the callback family, context, and Y
/// coordinate with the most recent cursor observation.
/// </summary>
internal sealed class Steam2026RenderedMenuSpeechTracker
{
    internal const int TitleModule = 20;

    private const int MaximumObservationsPerKind = 96;
    private const int MaximumHorizontalDistance = 640;
    private const long MaximumSequenceDistance = 64;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMilliseconds(300);
    private static readonly string[] NativeTitleAnchorKeys =
    [
        "newgame",
        "continue",
        "quit"
    ];

    private readonly List<TranslatedMenuCursorObservationFrame> cursors = [];
    private readonly List<TranslatedMenuTextObservationFrame> texts = [];
    private Steam2026RenderedMenuSpeechCandidate? pending;
    private string? acknowledgedKey;
    private int? lastModule;

    internal void Observe(
        TranslatedMenuIngressSnapshot snapshot,
        int? moduleId,
        bool isHostForeground)
    {
        if (!isHostForeground
            || moduleId.HasValue && moduleId.Value != TitleModule)
        {
            Reset();
            lastModule = moduleId;
            return;
        }

        if (lastModule != moduleId)
        {
            Reset();
            lastModule = moduleId;
        }

        if (snapshot.Cursor is { } cursor && IsSupportedCursor(cursor.Source))
        {
            cursors.Add(new TranslatedMenuCursorObservationFrame(
                cursor,
                snapshot.Sequence,
                snapshot.TimestampUtc));
        }

        if (snapshot.Text is { } text
            && IsSupportedText(text.Source)
            && !string.IsNullOrWhiteSpace(text.Text))
        {
            texts.Add(new TranslatedMenuTextObservationFrame(
                text,
                snapshot.Sequence,
                snapshot.TimestampUtc));
        }

        Prune(snapshot.Sequence, snapshot.TimestampUtc);
        RefreshPending(requireNativeTitleEvidence: !moduleId.HasValue);
    }

    internal bool TryGetPending(out Steam2026RenderedMenuSpeechCandidate candidate)
    {
        candidate = pending.GetValueOrDefault();
        return pending.HasValue;
    }

    internal void Acknowledge(Steam2026RenderedMenuSpeechCandidate candidate)
    {
        if (pending is not { } current || current != candidate)
        {
            return;
        }

        acknowledgedKey = candidate.Key;
        pending = null;
    }

    internal void Reset()
    {
        cursors.Clear();
        texts.Clear();
        pending = null;
        acknowledgedKey = null;
    }

    private void RefreshPending(bool requireNativeTitleEvidence)
    {
        if (cursors.Count == 0 || texts.Count == 0)
        {
            pending = null;
            return;
        }

        var cursorFrame = cursors.MaxBy(item => item.Sequence);
        if (requireNativeTitleEvidence && !HasNativeTitleEvidence(cursorFrame))
        {
            pending = null;
            return;
        }

        var matches = texts
            .Where(item => IsMatch(cursorFrame, item))
            .GroupBy(
                item => new
                {
                    item.Observation.Source,
                    item.Observation.Text,
                    item.Observation.X,
                    item.Observation.Y,
                    item.Observation.Context
                })
            .Select(group => group.MaxBy(item => item.Sequence))
            .ToArray();
        if (matches.Length != 1)
        {
            pending = null;
            return;
        }

        var match = matches[0];
        var text = match.Observation.Text.Trim();
        var key = string.Join(
            '\u001f',
            cursorFrame.Observation.Source,
            cursorFrame.Observation.Context,
            cursorFrame.Observation.Y,
            text);
        if (string.Equals(key, acknowledgedKey, StringComparison.Ordinal))
        {
            pending = null;
            return;
        }

        pending = new Steam2026RenderedMenuSpeechCandidate(
            key,
            text,
            Math.Max(cursorFrame.Sequence, match.Sequence));
    }

    private static bool IsMatch(
        TranslatedMenuCursorObservationFrame cursor,
        TranslatedMenuTextObservationFrame text)
    {
        if (!TryGetExpectedTextSource(cursor.Observation.Source, out var expectedTextSource)
            || text.Observation.Source != expectedTextSource
            || text.Observation.Context != cursor.Observation.Context
            || text.Observation.Y != cursor.Observation.Y
            || text.Observation.X <= cursor.Observation.X
            || text.Observation.X - cursor.Observation.X > MaximumHorizontalDistance
            || Math.Abs(text.Sequence - cursor.Sequence) > MaximumSequenceDistance)
        {
            return false;
        }

        var age = text.TimestampUtc - cursor.TimestampUtc;
        return age.Duration() <= MaximumAge;
    }

    private bool HasNativeTitleEvidence(
        TranslatedMenuCursorObservationFrame cursor)
    {
        if (!TryGetExpectedTextSource(
                cursor.Observation.Source,
                out var expectedTextSource))
        {
            return false;
        }

        var observedKeys = texts
            .Where(item =>
                item.Observation.Source == expectedTextSource
                && item.Observation.Context == cursor.Observation.Context
                && item.Observation.X > cursor.Observation.X
                && item.Observation.X - cursor.Observation.X <= MaximumHorizontalDistance
                && Math.Abs(item.Sequence - cursor.Sequence) <= MaximumSequenceDistance
                && (item.TimestampUtc - cursor.TimestampUtc).Duration() <= MaximumAge)
            .Select(item => NormalizeTitleAnchor(item.Observation.Text))
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        return NativeTitleAnchorKeys.All(observedKeys.Contains);
    }

    private static bool TryGetExpectedTextSource(
        Steam2026MenuCallbackKind cursorSource,
        out Steam2026MenuCallbackKind textSource)
    {
        switch (cursorSource)
        {
            case Steam2026MenuCallbackKind.CursorA:
                textSource = Steam2026MenuCallbackKind.EncodedTextA;
                return true;
            case Steam2026MenuCallbackKind.CursorB:
                textSource = Steam2026MenuCallbackKind.EncodedTextB;
                return true;
            default:
                textSource = default;
                return false;
        }
    }

    private static string NormalizeTitleAnchor(string text) =>
        new(text
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private void Prune(long newestSequence, DateTime newestTimestampUtc)
    {
        cursors.RemoveAll(item =>
            newestSequence - item.Sequence > MaximumSequenceDistance
            || newestTimestampUtc - item.TimestampUtc > MaximumAge);
        texts.RemoveAll(item =>
            newestSequence - item.Sequence > MaximumSequenceDistance
            || newestTimestampUtc - item.TimestampUtc > MaximumAge);

        if (cursors.Count > MaximumObservationsPerKind)
        {
            cursors.RemoveRange(0, cursors.Count - MaximumObservationsPerKind);
        }

        if (texts.Count > MaximumObservationsPerKind)
        {
            texts.RemoveRange(0, texts.Count - MaximumObservationsPerKind);
        }
    }

    private static bool IsSupportedCursor(Steam2026MenuCallbackKind kind) =>
        kind is Steam2026MenuCallbackKind.CursorA or Steam2026MenuCallbackKind.CursorB;

    private static bool IsSupportedText(Steam2026MenuCallbackKind kind) =>
        kind is Steam2026MenuCallbackKind.EncodedTextA or Steam2026MenuCallbackKind.EncodedTextB;

    private readonly record struct TranslatedMenuCursorObservationFrame(
        TranslatedMenuCursorObservation Observation,
        long Sequence,
        DateTime TimestampUtc);

    private readonly record struct TranslatedMenuTextObservationFrame(
        TranslatedMenuTextObservation Observation,
        long Sequence,
        DateTime TimestampUtc);
}
