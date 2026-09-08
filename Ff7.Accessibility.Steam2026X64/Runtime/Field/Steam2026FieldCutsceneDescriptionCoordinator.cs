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
    private readonly FieldMovieNarrationTracker? narration;
    private readonly FieldAreaDescriptionColdStartTracker areaColdStart =
        new(FieldCutsceneDescriptionCatalog.CreateGoldSaucerAreaDescriptions());
    private int currentFieldId = -1;

    internal Steam2026FieldCutsceneDescriptionCoordinator(
        ILegacyAddressSpace addressSpace)
        : this(addressSpace, FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions())
    {
    }

    internal Steam2026FieldCutsceneDescriptionCoordinator(
        ILegacyAddressSpace addressSpace,
        IEnumerable<FieldCutsceneDescriptionCue> cues,
        FieldMovieNarrationTracker? narration = null)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        ArgumentNullException.ThrowIfNull(cues);
        tracker = new FieldCutsceneDescriptionTracker(cues);
        this.narration = narration;
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

            // Record the film episode where the native opcode ran, before any
            // catalog filtering, so a film start that carries no narration still
            // expires an older opportunity.
            //
            // The handler state must come from the snapshot, which was taken before
            // the translated original ran. Re-reading it here would always see the 4
            // the handler itself wrote, and the very first described film would be
            // rejected as a repeat. The live sample is still read, for the episode
            // counter and the "another film is running" refusal, which are facts
            // about now rather than about the capture.
            if (narration is not null && TryReadMovieSample(snapshot.Context.FieldId, out var liveSample))
            {
                var ingressSample = snapshot.HasMovieSample
                    ? snapshot.MovieSample
                    : liveSample with
                    {
                        MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateUnknown,
                        MovieHandlerPhase = FieldMovieNarrationPolicy.MovieHandlerStateUnknown
                    };
                narration.NoteIngress(
                    snapshot.Context.FieldId,
                    snapshot.Context.EntityId,
                    snapshot.Context.ScriptId,
                    snapshot.Context.ByteIndex,
                    snapshot.Context.Opcode,
                    ingressSample,
                    snapshot.TimestampUtc,
                    liveSample);
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

            // The field's own area-name anchor really did run, so the cold-start
            // fallback must not offer the same description a second time.
            if (cue.Value.Opcode == FieldOpcodeAddressResolver.OpcodeMapNameIndex)
            {
                areaColdStart.NoteNativeAnchor(cue.Value.FieldId);
            }

            pending.Enqueue(cue.Value);
            return true;
        }
    }

    /// <summary>
    /// Offers the area description for a field whose own entry anchor was never
    /// observed - the mod attaching to a game already in a room, or a save loaded
    /// straight into one. It goes through the same queue, so it still waits behind
    /// native dialogue and still happens once per visit.
    /// </summary>
    internal void ObserveStableField(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            return;
        }

        lock (sync)
        {
            if (!addressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
                !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId))
            {
                return;
            }

            if (areaColdStart.Observe(module, fieldId, nowUtc) is { } cue)
            {
                pending.Enqueue(cue);
            }
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

            // Independent narration replaces the paragraph only once it has actually
            // started. Anything else - no live opportunity, a different native film,
            // a missing asset or a device that will not start - falls through to the
            // ordinary spoken description below.
            if (narration is not null &&
                TryReadMovieSample(cue.FieldId, out var deliverySample))
            {
                FieldMovieNarrationStartResult result;
                try
                {
                    result = narration.Begin(
                        cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, deliverySample, nowUtc);
                }
                catch
                {
                    result = FieldMovieNarrationStartResult.Unavailable;
                }

                if (result == FieldMovieNarrationStartResult.Started)
                {
                    pending.Dequeue();
                    speechPriority.BeginNarration(cue.FieldId, cue.Text, nowUtc);
                    spokenCue = cue;
                    return true;
                }

                // The opcode hook can run a frame or two ahead of the engine's own
                // active flag. Speaking now would dequeue the cue and leave nothing
                // to start the track with, so the cue is held. The tracker bounds
                // this wait itself and then reports Unavailable.
                if (result == FieldMovieNarrationStartResult.WaitingForNativeStart)
                {
                    return false;
                }
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

    /// <summary>
    /// Per-frame native film lifecycle. Expires a pending start opportunity and
    /// stops an active track when the film ends, another film starts, or the field
    /// or module changes.
    /// </summary>
    internal void ObserveNativeFilm(DateTime nowUtc)
    {
        if (narration is null || nowUtc.Kind != DateTimeKind.Utc)
        {
            return;
        }

        lock (sync)
        {
            if (!addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
                !TryReadMovieSample(fieldId, out var sample))
            {
                return;
            }

            narration.Observe(sample, nowUtc);
        }
    }

    /// <summary>
    /// Losing the foreground, a host pause and unload all stop the independent
    /// track: it plays on its own device and would otherwise keep talking over a
    /// game the player is no longer looking at.
    /// </summary>
    internal void SuspendNativeFilmNarration(FieldMovieNarrationStopReason reason)
    {
        lock (sync)
        {
            narration?.Stop(reason);
        }
    }

    internal bool IsNativeFilmNarrationPlaying => narration?.IsPlaying == true;

    private bool TryReadMovieSample(int fieldId, out FieldMovieNarrationSample sample)
    {
        sample = default;
        try
        {
            return FieldMovieNarrationSampleReader.TryRead(addressSpace, fieldId, out sample);
        }
        catch
        {
            return false;
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
        narration?.Stop(FieldMovieNarrationStopReason.FieldChanged);
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
            or FieldOpcodeAddressResolver.OpcodeDfanmIndex
            or FieldOpcodeAddressResolver.OpcodeVisibilityIndex
            or FieldOpcodeAddressResolver.OpcodeAnimOnceIndex
            or FieldOpcodeAddressResolver.OpcodeCanm1Index
            or FieldOpcodeAddressResolver.OpcodeAnimHoldIndex
            or FieldOpcodeAddressResolver.OpcodeCanm2Index
            or FieldOpcodeAddressResolver.OpcodeBackgroundOnIndex
            or FieldOpcodeAddressResolver.OpcodeSoundIndex
            or FieldOpcodeAddressResolver.OpcodeAkaoIndex
            or FieldOpcodeAddressResolver.OpcodeMovieIndex
            or FieldOpcodeAddressResolver.OpcodeMapNameIndex;
}
