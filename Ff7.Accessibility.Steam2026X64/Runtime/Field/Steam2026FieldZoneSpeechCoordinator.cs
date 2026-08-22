using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal readonly record struct Steam2026FieldZoneSpeech(
    int FieldId,
    string Text,
    bool Interrupt);

/// <summary>
/// Reads MPNAM's translated x86 line buffer and retains each field-entry name
/// until the managed speech output explicitly acknowledges delivery.
/// </summary>
internal sealed class Steam2026FieldZoneSpeechCoordinator
{
    private readonly ILegacyAddressSpace addressSpace;
    private readonly FieldMessageReader messageReader;
    private readonly FieldAudibleCueStateReader cueReader;
    private readonly DeferredZoneSpeechTracker tracker;

    internal Steam2026FieldZoneSpeechCoordinator(ILegacyAddressSpace addressSpace)
        : this(addressSpace, TimeSpan.FromMilliseconds(500))
    {
    }

    internal Steam2026FieldZoneSpeechCoordinator(
        ILegacyAddressSpace addressSpace,
        TimeSpan fieldSettleWindow)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        messageReader = new FieldMessageReader(addressSpace);
        cueReader = new FieldAudibleCueStateReader(addressSpace);
        tracker = new DeferredZoneSpeechTracker(fieldSettleWindow);
    }

    /// <param name="openingDescriptionRunning">
    /// Whether the spoken opening-movie description is still running. Its schedule
    /// reaches 115 seconds and can outlast the movie itself, so without this the
    /// first field's zone name talks over the tail of the narration.
    /// </param>
    internal bool TryObserve(
        bool isHostForeground,
        bool openingMovieDetected,
        bool openingMovieActive,
        bool openingDescriptionRunning,
        bool narrationPending,
        bool narrationProtected,
        DateTime nowUtc,
        out Steam2026FieldZoneSpeech speech)
    {
        speech = default;
        if (nowUtc.Kind != DateTimeKind.Utc ||
            !TryReadStableState(out var state, out var candidate))
        {
            return false;
        }

        if (state.Module != FieldPositionReader.FieldModule)
        {
            tracker.LeaveField();
            return false;
        }

        var openingMovieBlocked = DeferredZoneSpeechTracker.ShouldBlockForOpeningMovie(
            state.FieldId,
            openingMovieDetected,
            openingMovieActive || state.Cue.MovieActive != 0,
            openingDescriptionRunning);
        var blocked = !isHostForeground ||
            DeferredZoneSpeechTracker.ShouldBlockForFieldEntry(
                state.FieldId,
                openingMovieBlocked,
                state.Cue.ActiveMessageCount,
                state.Cue.UserControl,
                narrationPending,
                narrationProtected);
        var text = tracker.Observe(state.FieldId, candidate, nowUtc, blocked);
        if (text is null)
        {
            return false;
        }

        speech = new Steam2026FieldZoneSpeech(
            state.FieldId,
            text,
            Interrupt: tracker.ShouldInterruptPendingAnnouncement);
        return true;
    }

    internal bool Acknowledge(Steam2026FieldZoneSpeech speech) =>
        tracker.Acknowledge(speech.FieldId, speech.Text);

    internal void Reset() => tracker.LeaveField();

    private bool TryReadStableState(
        out ZoneOwnershipState state,
        out FieldMessageCandidate candidate)
    {
        state = default;
        candidate = new FieldMessageCandidate(string.Empty, string.Empty);
        if (!TryReadOwnership(out var before) ||
            !cueReader.TryRead(out var beforeCue))
        {
            return false;
        }

        // TryReadLineBuffer is itself a checked, double-read snapshot.  An
        // empty line is allowed here so a previously retained name can still
        // be delivered after the opening narration releases ownership.
        if (messageReader.TryReadLineBuffer(out var checkedCandidate))
        {
            candidate = checkedCandidate;
        }

        if (!cueReader.TryRead(out var afterCue) ||
            !TryReadOwnership(out var after) ||
            before != after ||
            beforeCue != afterCue ||
            beforeCue.Module != before.Module)
        {
            return false;
        }

        state = new ZoneOwnershipState(before.Module, before.FieldId, beforeCue);
        return true;
    }

    private bool TryReadOwnership(out ZoneOwnership ownership)
    {
        ownership = default;
        if (!addressSpace.TryReadByte(
                (uint)FieldPositionReader.AddressCurrentModule,
                out var module) ||
            !addressSpace.TryReadUInt16(
                (uint)FieldPositionReader.AddressFieldId,
                out var fieldId))
        {
            return false;
        }

        ownership = new ZoneOwnership(module, fieldId);
        return true;
    }

    private readonly record struct ZoneOwnership(byte Module, ushort FieldId);

    private readonly record struct ZoneOwnershipState(
        byte Module,
        ushort FieldId,
        FieldAudibleCueState Cue);
}
