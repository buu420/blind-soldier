namespace Ff7.Accessibility.Reloaded;

/// <summary>Observed quest information kept separately for the native save being played.</summary>
public sealed class KeyToSector5StoryKnowledge
{
    public KeyToSector5StoryKnowledge(string? path, Action<string>? log = null) =>
        History = new FieldAreaDescriptionHistory(path, log);

    public FieldAreaDescriptionHistory History { get; }

    public bool HasClue => History.HasHeard(170);

    public void ObserveVisibleClue(int currentModule, int fieldId,
        IReadOnlyList<FieldVisibleWindowSnapshot>? windows, bool isReviewedLayout,
        long expectedPlaythroughRevision, IReadOnlyList<string> expectedCluePages)
    {
        if (HasClue || currentModule != FieldPositionReader.FieldModule || fieldId != 170 ||
            !isReviewedLayout || windows is null || expectedCluePages.Count == 0)
            return;

        // The native active-window buffer is the evidence, independent of speech settings.
        // Echo-S rewords and renumbers this line, so a stock dialogue number is insufficient.
        if (windows.Any(window => window.WindowId == 0 &&
                window.NativeState != FieldMessageReader.FreeWindowState && window.GuestPointer != 0 &&
                MatchesLoadedClue(window.Text, expectedCluePages)))
            History.TryMarkHeard(170, expectedPlaythroughRevision);
    }

    private static bool MatchesLoadedClue(string? text, IReadOnlyList<string> pages)
    {
        var visible = NormalizeVisibleText(text);
        return visible.Length != 0 && pages.Any(page =>
            string.Equals(visible, NormalizeVisibleText(page), StringComparison.Ordinal));
    }

    internal static string NormalizeVisibleText(string? text) =>
        string.Concat((text ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
}
