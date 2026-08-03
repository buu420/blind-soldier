using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Matches pointer-free native opcode snapshots against the shared x86
/// description catalog and performs retryable speech on the managed worker
/// thread.
/// </summary>
internal sealed class Steam2026FieldCutsceneDescriptionCoordinator
{
    private readonly object sync = new();
    private readonly ILegacyAddressSpace addressSpace;
    private readonly FieldCutsceneDescriptionTracker tracker;
    private readonly FieldCutsceneSpeechPriority speechPriority = new();
    private readonly Queue<FieldCutsceneDescriptionCue> pending = new();
    private int currentFieldId = -1;

    internal Steam2026FieldCutsceneDescriptionCoordinator(
        ILegacyAddressSpace addressSpace)
        : this(addressSpace, FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions())
    {
    }

    internal Steam2026FieldCutsceneDescriptionCoordinator(
        ILegacyAddressSpace addressSpace,
        IEnumerable<FieldCutsceneDescriptionCue> cues)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        ArgumentNullException.ThrowIfNull(cues);
        tracker = new FieldCutsceneDescriptionTracker(cues);
    }

    internal bool Observe(Steam2026FieldCutsceneIngressSnapshot snapshot)
    {
        if (snapshot.Sequence <= 0
            || snapshot.TimestampUtc.Kind != DateTimeKind.Utc
            || snapshot.Context.FieldId < 0
            || snapshot.Context.EntityId < 0
            || snapshot.Context.ScriptId < 0
            || snapshot.Context.ByteIndex < 0
            || !IsSupportedIngressOpcode(snapshot.Context.Opcode))
        {
            return false;
        }

        lock (sync)
        {
            if (currentFieldId != snapshot.Context.FieldId)
            {
                ResetFieldState();
                currentFieldId = snapshot.Context.FieldId;
            }

            var cue = tracker.Observe(snapshot.Context);
            if (cue is null
                || cue.Value.FieldId != snapshot.Context.FieldId
                || cue.Value.EntityId != snapshot.Context.EntityId
                || cue.Value.ScriptId != snapshot.Context.ScriptId
                || cue.Value.ByteIndex != snapshot.Context.ByteIndex
                || cue.Value.Opcode != snapshot.Context.Opcode
                || !IsSupportedIngressOpcode(cue.Value.Opcode)
                || string.IsNullOrWhiteSpace(cue.Value.Text))
            {
                return false;
            }

            pending.Enqueue(cue.Value);
            return true;
        }
    }

    /// <summary>
    /// Attempts the oldest narration once. A thrown or rejected output leaves
    /// it at the head of the queue for the next worker iteration.
    /// </summary>
    internal bool TrySpeakPending(
        bool isHostForeground,
        Func<bool> hasReadableActiveMessage,
        Func<string, bool> trySpeak,
        DateTime nowUtc,
        out FieldCutsceneDescriptionCue spokenCue)
    {
        spokenCue = default;
        ArgumentNullException.ThrowIfNull(hasReadableActiveMessage);
        ArgumentNullException.ThrowIfNull(trySpeak);
        if (!isHostForeground || nowUtc.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        lock (sync)
        {
            if (pending.Count == 0
                || !TryReadStableFieldState(
                    hasReadableActiveMessage,
                    out var module,
                    out var fieldId,
                    out var activeMessageCount,
                    out var hasReadableMessage))
            {
                return false;
            }

            var cue = pending.Peek();
            if (module != FieldPositionReader.FieldModule
                || fieldId != cue.FieldId)
            {
                ResetFieldState();
                currentFieldId = module == FieldPositionReader.FieldModule
                    ? fieldId
                    : -1;
                return false;
            }

            if (FieldCutsceneSpeechPriority.ShouldWaitForDialogue(
                    activeMessageCount,
                    hasReadableMessage))
            {
                return false;
            }

            bool accepted;
            try
            {
                accepted = trySpeak(cue.Text);
            }
            catch
            {
                return false;
            }

            if (!accepted)
            {
                return false;
            }

            pending.Dequeue();
            speechPriority.BeginNarration(cue.FieldId, cue.Text, nowUtc);
            spokenCue = cue;
            return true;
        }
    }

    internal bool ShouldQueueDialogue(int fieldId, DateTime nowUtc) =>
        nowUtc.Kind == DateTimeKind.Utc
        && speechPriority.ShouldQueueDialogue(fieldId, nowUtc);

    internal bool HasPendingNarration(int fieldId)
    {
        lock (sync)
        {
            return pending.Any(cue => cue.FieldId == fieldId);
        }
    }

    internal void Reset()
    {
        lock (sync)
        {
            ResetFieldState();
            currentFieldId = -1;
        }
    }

    private bool TryReadStableFieldState(
        Func<bool> hasReadableActiveMessage,
        out byte module,
        out ushort fieldId,
        out byte activeMessageCount,
        out bool hasReadableMessage)
    {
        module = 0;
        fieldId = 0;
        activeMessageCount = 0;
        hasReadableMessage = false;
        try
        {
            if (!TryReadFieldState(
                    hasReadableActiveMessage,
                    out var beforeModule,
                    out var beforeFieldId,
                    out var beforeActiveMessageCount,
                    out var beforeReadable)
                || !TryReadFieldState(
                    hasReadableActiveMessage,
                    out var afterModule,
                    out var afterFieldId,
                    out var afterActiveMessageCount,
                    out var afterReadable)
                || beforeModule != afterModule
                || beforeFieldId != afterFieldId
                || beforeActiveMessageCount != afterActiveMessageCount
                || beforeReadable != afterReadable)
            {
                return false;
            }

            module = beforeModule;
            fieldId = beforeFieldId;
            activeMessageCount = beforeActiveMessageCount;
            hasReadableMessage = beforeReadable;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadFieldState(
        Func<bool> hasReadableActiveMessage,
        out byte module,
        out ushort fieldId,
        out byte activeMessageCount,
        out bool hasReadableMessage)
    {
        module = 0;
        fieldId = 0;
        activeMessageCount = 0;
        hasReadableMessage = false;
        if (!addressSpace.TryReadByte(
                (uint)FieldPositionReader.AddressCurrentModule,
                out module)
            || !addressSpace.TryReadUInt16(
                (uint)FieldPositionReader.AddressFieldId,
                out fieldId)
            || !addressSpace.TryReadByte(
                (uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount,
                out activeMessageCount))
        {
            return false;
        }

        hasReadableMessage = hasReadableActiveMessage();
        return true;
    }

    private void ResetFieldState()
    {
        pending.Clear();
        tracker.Reset();
        speechPriority.Reset();
    }

    private static bool IsSupportedIngressOpcode(int opcode) =>
        opcode is FieldOpcodeAddressResolver.OpcodeRequestIndex
            or FieldOpcodeAddressResolver.OpcodeRequestSwIndex
            or FieldOpcodeAddressResolver.OpcodeRequestEwIndex
            or FieldOpcodeAddressResolver.OpcodeSplitIndex
            or FieldOpcodeAddressResolver.OpcodeWaitIndex
            or FieldOpcodeAddressResolver.OpcodeScroll2DIndex
            or FieldOpcodeAddressResolver.OpcodeFadeIndex
            or FieldOpcodeAddressResolver.OpcodeAnime1Index
            or FieldOpcodeAddressResolver.OpcodeVisibilityIndex
            or FieldOpcodeAddressResolver.OpcodeAnimOnceIndex
            or FieldOpcodeAddressResolver.OpcodeCanm1Index
            or FieldOpcodeAddressResolver.OpcodeAnimHoldIndex
            or FieldOpcodeAddressResolver.OpcodeCanm2Index
            or FieldOpcodeAddressResolver.OpcodeBackgroundOnIndex
            or FieldOpcodeAddressResolver.OpcodeSoundIndex
            or FieldOpcodeAddressResolver.OpcodeAkaoIndex
            or FieldOpcodeAddressResolver.OpcodeMovieIndex;
}
