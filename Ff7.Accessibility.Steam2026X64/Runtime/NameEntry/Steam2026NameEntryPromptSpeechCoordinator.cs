using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;

namespace Ff7.Accessibility.Steam2026X64.Runtime.NameEntry;

/// <summary>
/// Restores the visible name-entry prompt from the checked translated draw
/// stream while the native editor tracker owns character and command speech.
/// </summary>
internal sealed class Steam2026NameEntryPromptSpeechCoordinator
{
    private readonly bool enabled;
    private readonly FieldDialogueDrawSpeechTracker tracker;
    private readonly Action<string, bool> speak;
    private readonly Action<string> log;
    private bool ownsNameEntry;
    private long lastSequence;
    private string? pendingSpeech;

    internal Steam2026NameEntryPromptSpeechCoordinator(
        bool enabled,
        TimeSpan stableTime,
        Action<string, bool> speak,
        Action<string> log)
    {
        this.enabled = enabled;
        tracker = new FieldDialogueDrawSpeechTracker(stableTime);
        this.speak = speak ?? throw new ArgumentNullException(nameof(speak));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal void SetOwnership(bool owns)
    {
        if (!enabled || !owns)
        {
            Reset();
            return;
        }

        ownsNameEntry = true;
    }

    internal void Observe(TranslatedMenuIngressSnapshot snapshot)
    {
        if (!ownsNameEntry
            || snapshot.Sequence <= lastSequence
            || snapshot.TimestampUtc.Kind != DateTimeKind.Utc
            || snapshot.CallbackKind is not (
                Steam2026MenuCallbackKind.EncodedTextA or
                Steam2026MenuCallbackKind.EncodedTextB)
            || snapshot.Text is not { } text
            || text.Source != snapshot.CallbackKind)
        {
            return;
        }

        lastSequence = snapshot.Sequence;
        tracker.Observe(
            new MenuTextRenderEntry(
                text.Text,
                unchecked((uint)text.X),
                unchecked((uint)text.Y),
                text.Color,
                text.Context),
            NameEntryStateReader.NameEntryModule,
            snapshot.TimestampUtc);
    }

    internal void Poll(DateTime nowUtc)
    {
        if (!ownsNameEntry || nowUtc.Kind != DateTimeKind.Utc)
        {
            return;
        }

        var speech = pendingSpeech ?? tracker.Poll(nowUtc);
        if (string.IsNullOrWhiteSpace(speech))
        {
            return;
        }

        pendingSpeech = speech;
        speak(speech, true);
        pendingSpeech = null;
        log($"Native Steam 2026 name-entry prompt: {speech}");
    }

    internal void Reset()
    {
        ownsNameEntry = false;
        lastSequence = 0;
        pendingSpeech = null;
        tracker.Reset();
    }
}
