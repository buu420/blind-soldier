using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

internal readonly record struct FieldCreatureWarning(LoadedFieldScriptIdentity Identity,
    int DialogId, uint WindowPointer, string Text);

/// <summary>The reviewed MESSAGE44 page and its native window ownership, including typing.</summary>
internal sealed class FieldCreatureWarningReader
{
    private readonly ILegacyAddressSpace memory;
    private readonly Func<LoadedFieldScriptIdentity?> readIdentity;
    private readonly FieldActivityStateReader activity;
    private readonly FieldScriptControllerReader controller;
    private readonly FieldMessageReader messages;

    internal FieldCreatureWarningReader(ILegacyAddressSpace memory, Func<LoadedFieldScriptIdentity?> readIdentity)
    {
        this.memory = memory;
        this.readIdentity = readIdentity;
        activity = new(memory);
        controller = new(memory);
        messages = new(memory);
    }

    internal bool TryRead(out FieldCreatureWarning warning) => TryRead(requireFullText: true, out warning);

    // Native speech may be accepted at MESSAGE entry, before the window types any letters.
    // Return the checked full table text, never the visible fragment as a speech token.
    internal bool TryReadSpeechOwner(out FieldCreatureWarning warning) => TryRead(requireFullText: false, out warning);

    private bool TryRead(bool requireFullText, out FieldCreatureWarning warning)
    {
        warning = default;
        try
        {
            if (!Capture(requireFullText, out var before) || !Capture(requireFullText, out var after) || before != after)
                return false;
            warning = before.Warning;
            return true;
        }
        catch { return false; }
    }

    private bool Capture(bool requireFullText, out WarningCapture capture)
    {
        capture = default;
        if (readIdentity() is not { FieldId: 767 } identity) return false;
        var variant = EchoSCompatibilityManifest.ResolveVariant(identity);
        var dialog = variant switch
        {
            SupportedFieldScriptVariant.Vanilla => 10,
            SupportedFieldScriptVariant.EchoS124 => 9,
            _ => -1
        };
        if (dialog < 0 || !controller.TryRead(767, 3, 6, out var control) || !control.IsControllerActive) return false;
        var wait = activity.ReadWaitState(767, 3, [new(44, 0x40, 1 | (dialog << 8), 44, 44)]);
        if (!wait.IsWaiting || wait.RelativeOffset != 44 ||
            !TryReadPageBounds(identity, dialog, out var start, out var terminator) ||
            !memory.TryReadByte(FieldMessageReader.AddressFieldWindowStates + 1, out var owner) || owner != 3 ||
            !memory.TryReadUInt32(FieldMessageReader.AddressFieldWindowMessagePointers + 4, out var cursor) ||
            cursor < start || cursor > terminator ||
            !messages.TryReadVisibleWindows(out var windows) || windows.Count > 1 ||
            windows.Count == 1 && (windows[0].WindowId != 1 || windows[0].GuestPointer != cursor) ||
            !messages.TryReadMessageById(dialog, out var exact) || string.IsNullOrWhiteSpace(exact.Text)) return false;
        var visibleText = windows.Count == 0 ? string.Empty : windows[0].Text;
        if (requireFullText ? visibleText != exact.Text : !exact.Text.StartsWith(visibleText, StringComparison.Ordinal)) return false;
        if (!memory.TryReadByte(FieldMessageReader.AddressFieldWindowStates + 1, out var ownerAfter) || owner != ownerAfter ||
            !memory.TryReadUInt32(FieldMessageReader.AddressFieldWindowMessagePointers + 4, out var cursorAfter) || cursor != cursorAfter)
            return false;
        // CBF578 is an advancing text cursor. The immutable table start is the page token.
        capture = new(new(identity, dialog, start, exact.Text), cursor, visibleText);
        return true;
    }

    private bool TryReadPageBounds(LoadedFieldScriptIdentity identity, int dialog, out uint start, out uint terminator)
    {
        start = terminator = 0;
        if (identity.ScriptPointer == 0 ||
            !memory.TryReadUInt16(identity.ScriptPointer + 4, out var textOffset) ||
            !memory.TryReadUInt32(FieldMessageReader.AddressFieldMessageDataPointer, out var table) ||
            (ulong)identity.ScriptPointer + textOffset != table ||
            !memory.TryReadUInt16(table, out var count) || count > 256 || dialog >= count ||
            !memory.TryReadUInt16(table + 2 + (uint)dialog * 2, out var offset) || offset < 2 + count * 2 ||
            !LegacyFf7TextReader.TryAdd(table, offset, out start) ||
            !LegacyFf7TextReader.TryReadTerminated(memory, start, FieldMessageReader.FieldTextBufferLength, out var raw, out _) ||
            raw.Length < 2 || !LegacyFf7TextReader.TryAdd(start, (uint)raw.Length - 1, out terminator)) return false;
        return true;
    }

    private readonly record struct WarningCapture(FieldCreatureWarning Warning, uint Cursor, string VisibleText);
}

/// <summary>Speech acceptance alone is not completion. Require observed native/backend progress.</summary>
internal sealed class FieldCreatureWarningSpeechGate
{
    private readonly Func<FieldCreatureWarning?> readWarning;
    private readonly Func<FieldCreatureWarning?> readSpeechOwner;
    private FieldCreatureWarning? prismWarning;
    private LoadedFieldScriptIdentity? originalWarning;
    private bool heardPrismActive;

    internal FieldCreatureWarningSpeechGate(FieldCreatureWarningReader reader)
        : this(() => reader.TryRead(out var warning) ? warning : null,
            () => reader.TryReadSpeechOwner(out var warning) ? warning : null) { }
    internal FieldCreatureWarningSpeechGate(Func<FieldCreatureWarning?> readWarning)
        : this(readWarning, readWarning) { }
    private FieldCreatureWarningSpeechGate(Func<FieldCreatureWarning?> readWarning, Func<FieldCreatureWarning?> readSpeechOwner)
    {
        this.readWarning = readWarning;
        this.readSpeechOwner = readSpeechOwner;
    }

    internal void NotePrismAccepted(int windowId, string text)
    {
        if (windowId != 1 || readSpeechOwner() is not { } warning || warning.Text != text) return;
        if (prismWarning == warning) return;
        prismWarning = warning;
        heardPrismActive = false;
    }
    internal void NoteOriginalVoice(LoadedFieldScriptIdentity identity)
    {
        if (identity.FieldId == 767 && EchoSCompatibilityManifest.ResolveVariant(identity) == SupportedFieldScriptVariant.EchoS124)
            originalWarning = identity;
    }
    internal bool IsCompleted(bool? prismIsSpeaking, bool? originalVoiceFinished = null)
    {
        if (readSpeechOwner() is not { } owner) { Reset(); return false; }
        if (prismWarning == owner && prismIsSpeaking == true) heardPrismActive = true;
        // Keep the accepted owner and backend progress while its window is typing.
        // Narration still waits for the retained, fully visible checked page.
        if (readWarning() is not { } current || current != owner) return false;
        if (prismWarning == current && heardPrismActive && prismIsSpeaking == false) return true;
        return originalVoiceFinished == true && originalWarning == current.Identity && prismIsSpeaking == false;
    }
    internal void Reset() { prismWarning = null; originalWarning = null; heardPrismActive = false; }
}
