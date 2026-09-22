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
    private FieldAreaDescriptionHistoryGate historyGate = new(null);
    private readonly FieldCutsceneDescriptionTracker tracker;
    private readonly FieldCutsceneSpeechPriority speechPriority = new();
    private readonly Queue<FieldCutsceneDescriptionCue> pending = new();
    private readonly FieldMovieNarrationTracker? narration;
    private readonly CutsceneVoicePlayer? cutsceneVoice;
    private readonly FieldAreaDescriptionColdStartTracker areaColdStart =
        new(FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions());
    private int currentFieldId = -1;

    internal Steam2026FieldCutsceneDescriptionCoordinator(
        ILegacyAddressSpace addressSpace)
        : this(addressSpace, FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions())
    {
    }

    internal Steam2026FieldCutsceneDescriptionCoordinator(
        ILegacyAddressSpace addressSpace,
        IEnumerable<FieldCutsceneDescriptionCue> cues,
        FieldMovieNarrationTracker? narration = null,
        CutsceneVoicePlayer? cutsceneVoice = null)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        ArgumentNullException.ThrowIfNull(cues);
        tracker = new FieldCutsceneDescriptionTracker(cues);
        this.narration = narration;
        this.cutsceneVoice = cutsceneVoice;

        // The priority policy asks the device, not a word-count estimate, whether a
        // recording is still being heard. Without this the only way the game's words
        // could take precedence was to stop the clip.
        if (cutsceneVoice is not null)
        {
            speechPriority.AttachRecordingProbe(() => cutsceneVoice.IsPlaying);
        }
    }

    /// <summary>
    /// Says a description in the recorded voice when there is a recording of those
    /// exact words, and hands it to the caller's speaker when there is not. Returning
    /// false means nobody said it, which keeps the cue at the head of the queue.
    /// </summary>
    private bool SpeakDescription(
        Func<string, bool> trySpeak,
        string text,
        CutsceneVoiceOwner owner,
        out TimeSpan? clipDuration)
    {
        var delivery = CutsceneVoiceSpeaker.Deliver(cutsceneVoice, text, owner, trySpeak);
        clipDuration = delivery.ClipDuration;
        return delivery.Spoken;
    }

    /// <summary>
    /// Holds the dialogue window for the description just said: the clip's own length
    /// when it was a recording, the word-count estimate when it was speech.
    /// </summary>
    private void ReserveDescriptionWindow(int fieldId, string text, TimeSpan? clipDuration, DateTime nowUtc)
    {
        if (clipDuration is { } duration)
        {
            speechPriority.BeginNarration(fieldId, duration, nowUtc);
            return;
        }

        speechPriority.BeginNarration(fieldId, text, nowUtc);
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
    /// <summary>
    /// The playthrough's room history. Set once by the host; until then nothing is gated,
    /// which is what every field did before this existed.
    /// </summary>
    internal FieldAreaDescriptionHistoryGate HistoryGate
    {
        get { lock (sync) { return historyGate; } }
        set { lock (sync) { historyGate = value ?? new FieldAreaDescriptionHistoryGate(null); } }
    }

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

            // A room this playthrough has already heard leaves the queue here, before
            // anything is started or reserved, so it costs no narration track, no speech
            // attempt and no dialogue window.
            if (!historyGate.ShouldOffer(cue))
            {
                pending.Dequeue();
                return false;
            }

            if (module != FieldPositionReader.FieldModule
                || fieldId != cue.FieldId)
            {
                ResetFieldState();
                currentFieldId = module == FieldPositionReader.FieldModule
                    ? fieldId
                    : -1;
                return false;
            }

            if (speechPriority.ShouldDeferDialogueDelivery(nowUtc) ||
                FieldCutsceneSpeechPriority.ShouldWaitForDialogue(
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
                    historyGate.NoteSpoken(cue);
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

                // The film is already being described, by its recording or by the cue
                // schedule that took over from one. Reading the paragraph as well
                // would describe the same footage twice, so the cue is spent without
                // being spoken and without reserving a dialogue window.
                if (result == FieldMovieNarrationStartResult.AlreadyDescribed)
                {
                    pending.Dequeue();
                    return false;
                }

                // The paragraph describes a film the game is not playing, which is
                // what a disc change does to an anchor: same address, different
                // film. Say what is actually running, or say nothing.
                if (result == FieldMovieNarrationStartResult.DescribesADifferentFilm)
                {
                    if (!FieldMovieNarrationTracker.TryDescribeRunningFilm(
                            deliverySample, out var running))
                    {
                        pending.Dequeue();
                        return false;
                    }

                    cue = cue with { Text = running };
                }
            }

            bool accepted;
            TimeSpan? clipDuration;
            try
            {
                accepted = SpeakDescription(
                    trySpeak, cue.Text, CutsceneVoiceOwner.FieldAction, out clipDuration);
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
            ReserveDescriptionWindow(cue.FieldId, cue.Text, clipDuration, nowUtc);
            historyGate.NoteSpoken(cue);
            spokenCue = cue;
            return true;
        }
    }

    /// <summary>
    /// Per-frame native film lifecycle. Expires a pending start opportunity and
    /// stops an active track when the film ends, another film starts, the field or
    /// module changes, or the game puts its own words on screen.
    /// </summary>
    /// <param name="hasReadableActiveMessage">
    /// Whether a dialogue window is open and readable right now. A film can run with
    /// native text over it, and the text wins.
    /// </param>
    internal void ObserveNativeFilm(DateTime nowUtc, Func<bool>? hasReadableActiveMessage = null)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            return;
        }

        if (narration is null)
        {
            // An already-started action clip finishes; dialogue delivery waits for
            // its device. No new description starts while a text box is open.
            return;
        }

        lock (sync)
        {
            // A failed read is not "carry on". The independent track plays on its own
            // device and the deferred schedule is spoken by the host, so leaving both
            // running against state nobody can see is how a description ends up over
            // the wrong scene. Stop, and let the next readable frame start again.
            if (!addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
                !TryReadMovieSample(fieldId, out var sample))
            {
                narration.Stop(FieldMovieNarrationStopReason.Unloaded);

                // The same unknown applies to a recorded description: nobody can see
                // what is on screen, so nothing should be describing it.
                cutsceneVoice?.Stop("the native film state could not be read");
                return;
            }

            // The flag still reaches the film tracker, so a long recording hands over to
            // the game's words and its remaining cues go onto the deferred schedule
            // exactly as before. What no longer happens is stopping a clip that has
            // already started: it finishes, and the dialogue waits.
            var dialogueIsOnScreen = DialogueIsOnScreen(hasReadableActiveMessage);
            narration.Observe(sample, nowUtc, dialogueIsOnScreen);
        }
    }

    /// <summary>
    /// The next cue of a film whose recording gave way to the game's own words, or
    /// null. Spoken through the ordinary path, which is what lets the description and
    /// the dialogue alternate instead of overlapping.
    /// </summary>
    /// <param name="trySpeak">
    /// Only acceptance advances the film's schedule, so a speaker that refuses leaves
    /// the cue to be offered again instead of losing it.
    /// </param>
    internal bool TryDeliverDeferredFilmCue(
        DateTime nowUtc,
        Func<bool>? hasReadableActiveMessage,
        Func<string, bool> trySpeak,
        out string delivered,
        out int deliveredFieldId)
    {
        delivered = string.Empty;
        deliveredFieldId = -1;
        if (narration is null || nowUtc.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        lock (sync)
        {
            // While a description is still being read out, the next one waits - the
            // same reservation every other cue in this coordinator obeys.
            if (speechPriority.ShouldQueueDialogue(currentFieldId, nowUtc))
            {
                return false;
            }

            TimeSpan? clipDuration = null;
            if (!narration.TryDeliverDeferredCue(
                    DialogueIsOnScreen(hasReadableActiveMessage),
                    text => SpeakDescription(
                        trySpeak, text, CutsceneVoiceOwner.FilmCue, out clipDuration),
                    out delivered))
            {
                return false;
            }

            ReserveDescriptionWindow(currentFieldId, delivered, clipDuration, nowUtc);
            deliveredFieldId = currentFieldId;
            return true;
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

            // A recorded description plays on the same device and would keep talking
            // over whatever window the player moved to.
            cutsceneVoice?.Stop(reason.ToString());
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


    internal bool ShouldDeferDialogueDelivery(DateTime nowUtc) =>
        speechPriority.ShouldDeferDialogueDelivery(nowUtc);

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

            // An explicit reset is the host tearing the session down, not the story
            // moving to the next room, so the film narration and every recorded
            // description go with it.
            narration?.Stop(FieldMovieNarrationStopReason.Unloaded);
            cutsceneVoice?.Stop("the coordinator was reset");
            currentFieldId = -1;
        }
    }

    /// <summary>
    /// Whether the game has its own words on screen right now.
    ///
    /// <para>Two different unknowns, deliberately treated differently. No probe at
    /// all means dialogue reading is not configured on this host, so there is no
    /// second voice to collide with and narration proceeds. A probe that is there but
    /// whose read fails is a real unknown: assume the game is talking rather than
    /// talk over it.</para>
    /// </summary>
    private bool DialogueIsOnScreen(Func<bool>? hasReadableActiveMessage)
    {
        if (hasReadableActiveMessage is null)
        {
            return false;
        }

        return !TryReadStableFieldState(hasReadableActiveMessage, out _, out _,
                   out var activeMessageCount, out var hasReadableMessage) ||
               FieldCutsceneSpeechPriority.ShouldWaitForDialogue(
                   activeMessageCount, hasReadableMessage);
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

    /// <summary>
    /// Clears the cues that belong to the field the player has left.
    ///
    /// <para>This deliberately does <b>not</b> stop the film narration. Story cues are
    /// bound to a field; a film is not. One film runs across four Highwind fields,
    /// and the first snapshot the coordinator ever drains arrives while
    /// <c>currentFieldId</c> is still -1, so stopping here killed a film that the
    /// native tick had legitimately started moments earlier. The film's own lifetime
    /// is decided by <see cref="ObserveNativeFilm"/> from the engine's state - it
    /// ends when the film ends, when another film starts, when the module changes or
    /// when the evidence stops reading - and by the explicit stops below.</para>
    /// </summary>
    private void ResetFieldState()
    {
        pending.Clear();
        tracker.Reset();
        speechPriority.Reset();
        cutsceneVoice?.StopIfOwnedBy(CutsceneVoiceOwner.FieldAction, "the field changed");
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
