namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The narration output. Kept behind an interface so the lifecycle is testable
/// without a real audio device, and so both runtimes share one tracker.
/// </summary>
public interface IFieldMovieNarrationOutput : IDisposable
{
    bool IsPlaying { get; }
    bool Start(string reason);
    bool Stop(string reason);
}

/// <summary>
/// What a delivery attempt should do next. The waiting state is the reason this is
/// not a bool: a caller that treats "not started" as "speak the paragraph" commits
/// and dequeues the cue a frame before the engine raises its own active flag, and
/// the independent track can then never start because no cue is left.
/// </summary>
public enum FieldMovieNarrationStartResult
{
    /// <summary>This cue carries no independent track; speak it normally.</summary>
    NotDescribed,

    /// <summary>Independent audio is playing; do not speak the paragraph.</summary>
    Started,

    /// <summary>
    /// The native film has not raised its active flag yet. Keep the cue queued and
    /// speak nothing this tick; the bound is enforced inside the tracker.
    /// </summary>
    WaitingForNativeStart,

    /// <summary>
    /// Stale, disabled, missing or failed. Speak the paragraph now.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The film that is running is not the one this anchor's paragraph describes,
    /// which happens whenever the disc has moved on: a script address names a film
    /// by number, and the number means a different film on discs 2 and 3. The
    /// paragraph must not be spoken. Ask
    /// <see cref="FieldMovieNarrationTracker.TryDescribeRunningFilm"/> for the film
    /// that is actually playing; if nothing has been written for it, say nothing.
    /// </summary>
    DescribesADifferentFilm,

    /// <summary>
    /// This film is already being described - by its recording, or by the cue
    /// schedule that took over when the recording gave way to the game's own words.
    /// The anchor's paragraph is about the same footage, so reading it out as well
    /// would say the scene twice, and the schedule is the better of the two because
    /// it follows the film. Spend the cue without speaking it and without reserving
    /// the dialogue window for words nobody said.
    /// </summary>
    AlreadyDescribed
}

/// <summary>
/// Starts an independent narration track when a described native film begins, and
/// stops it at every native boundary. Independent output matters because
/// screen-reader speech is interrupted by ordinary button presses, which would cut
/// a 45-second description into a fragment.
///
/// The unit of correctness is the film *episode*, not the field and not the opcode
/// arrival. Two films can start from the same field in the same module seconds
/// apart, and the native F9 handler re-delivers the same opcode on every frame of a
/// running film, so an episode that has already been started, consumed or expired
/// is remembered and cannot be reopened by those repeats.
/// </summary>
public sealed class FieldMovieNarrationTracker : IDisposable
{
    private readonly Func<FieldMovieNarrationTrack, IFieldMovieNarrationOutput?> createOutput;
    private readonly Action<string> log;
    private readonly int fieldModule;
    private readonly object sync = new();

    private IFieldMovieNarrationOutput? output;
    private FieldMovieNarrationTrack activeTrack;
    private bool hasActiveTrack;
    private long activeEpisode;

    // The film this episode is, decided once when the episode begins and then left
    // alone. The number lives in a word the field module's current command owns, so
    // re-reading it later can pick up another command's argument; and the same number
    // on another disc is a different film, so the disc belongs to the identity too.
    private string? episodeFilm;
    private long episodeFilmDecidedFor = -1;

    // Where the running film has got to, taken from the film's own frame counter.
    // The wall clock cannot do this job: it runs on through a pause and it keeps
    // running after the film ends, so cues timed off it drift and then fire into
    // ordinary gameplay.
    private double episodePositionSeconds = -1d;

    // What is left to say once the recording has had to give way to the game's own
    // words. The recording cannot duck - it can only start and stop - so the rest of
    // the film is described through ordinary speech, on the film's own clock.
    private readonly Func<FieldMovieNarrationTrack, IReadOnlyList<MovieNarrationCue>>? readCues;
    private readonly Action<CutsceneVoiceOwner, string>? yieldDescriptionAudio;
    private List<MovieNarrationCue>? deferredCues;
    private int deferredIndex;
    private long deferredEpisode = -1;

    // The episode a spoken cue's recording belongs to, or -1 when none is out.
    //
    // A clip started from the deferred schedule has its own life on the audio device:
    // it goes on talking after the schedule has been dropped, after the schedule has
    // run out, and after the film that owns it has ended. Tracking only the schedule
    // therefore misses the case this exists for - the last cue of a film still
    // speaking over whatever the game cut to. The episode is what the clip actually
    // belongs to, so that is what is remembered.
    private long filmCueAudioEpisode = -1;

    private PendingStart? pending;

    // Episode bookkeeping, driven purely by observed native samples. Episodes only
    // ever increase, so one high-water mark records every settled episode.
    private long episode;
    private bool lastMovieActive;
    private int lastMovieNumber;
    private int lastDisc = MovieFilmNameResolver.DiscUnknown;
    private long settledThroughEpisode = -1;

    // The anchor whose one start opportunity has been spent. It is held until a
    // native boundary says a genuinely new film can begin there. The episode counter
    // alone cannot do this job in both directions: it does not advance at all while
    // the player is between rides and no film is running, and it *does* advance when
    // a film that was too slow to raise its active flag finally starts, which is the
    // moment the handler's own repeat of the same opcode would otherwise be mistaken
    // for a new ride and describe the film from the middle.
    private bool anchorHeld;
    private int heldFieldId;
    private int heldByteIndex;

    /// <param name="readCues">
    /// The recording's own cue windows, from the sidecar that ships beside it. Only
    /// needed for films the game talks over; without it such a film keeps its
    /// paragraph and loses the rest of its narration, which is the older behaviour.
    /// </param>
    /// <param name="yieldDescriptionAudio">
    /// Takes the shared audio device back from whatever recorded description is using
    /// it. Called with <see cref="CutsceneVoiceOwner.FieldAction"/> immediately before
    /// a film's own recording starts - a field description must not still be talking
    /// underneath it - and with <see cref="CutsceneVoiceOwner.FilmCue"/> whenever this
    /// film's timed cues stop being valid, because one of them may already be playing
    /// and would otherwise carry on over the next scene.
    /// </param>
    public FieldMovieNarrationTracker(
        Func<FieldMovieNarrationTrack, IFieldMovieNarrationOutput?> createOutput,
        Action<string> log,
        int fieldModule,
        Func<FieldMovieNarrationTrack, IReadOnlyList<MovieNarrationCue>>? readCues = null,
        Action<CutsceneVoiceOwner, string>? yieldDescriptionAudio = null)
    {
        this.createOutput = createOutput;
        this.log = log;
        this.fieldModule = fieldModule;
        this.readCues = readCues;
        this.yieldDescriptionAudio = yieldDescriptionAudio;
    }

    /// <summary>
    /// Ends any recorded description owned by <paramref name="owner"/>. Safe to call
    /// when nothing is playing and when no coordinator was supplied.
    /// </summary>
    private void YieldDescriptionAudio(CutsceneVoiceOwner owner, string reason)
    {
        try
        {
            yieldDescriptionAudio?.Invoke(owner, reason);
        }
        catch (Exception ex)
        {
            log($"Field movie narration could not take the description device back: {ex.Message}");
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (sync)
            {
                return hasActiveTrack && output?.IsPlaying == true;
            }
        }
    }

    /// <summary>
    /// The film the game is showing, as the game itself names it, or null when that
    /// cannot be established. Every one of these is a way of not knowing, and not
    /// knowing means saying nothing:
    ///
    /// <list type="bullet">
    /// <item>no film on screen, or one the engine is skipping;</item>
    /// <item>a command byte that is not a movie command, or could not be read, so
    /// the argument word is another command's and the number is stale;</item>
    /// <item>a film of 20 or more with no readable disc, because that number names a
    /// different film on each of the three;</item>
    /// <item>a number and disc that do not name a film at all.</item>
    /// </list>
    /// </summary>
    public static string? ResolveFilmAtSample(FieldMovieNarrationSample sample)
    {
        if (!sample.FilmIsOnScreen || !sample.MovieNumberIsCurrent)
        {
            return null;
        }

        if (MovieFilmNameResolver.RequiresDisc(sample.MovieNumber) &&
            sample.Disc == MovieFilmNameResolver.DiscUnknown)
        {
            return null;
        }

        return MovieFilmNameResolver.Resolve(sample.MovieNumber, sample.Disc);
    }

    /// <summary>
    /// What to say about the film that is running, when the anchor's own paragraph
    /// describes a different one. Null means nothing has been written for it, and
    /// nothing is the right thing to say.
    /// </summary>
    public static bool TryDescribeRunningFilm(
        FieldMovieNarrationSample sample,
        out string paragraph)
    {
        paragraph = MovieFilmDescriptionCatalog.Paragraph(ResolveFilmAtSample(sample)) ?? string.Empty;
        return paragraph.Length > 0;
    }

    public bool HasPendingStart
    {
        get
        {
            lock (sync)
            {
                return pending is not null;
            }
        }
    }

    /// <summary>
    /// Called where the native opcode is observed, before the description is queued.
    /// This is the only place a start opportunity is created.
    /// </summary>
    /// <param name="opcode">
    /// Only a film-start opcode may create or expire an opportunity, so the hook can
    /// be called for every opcode without a pending start being lost to unrelated
    /// work.
    /// </param>
    /// <param name="sample">
    /// The sample taken at the native boundary, *before* the original ran. Only its
    /// handler state is used, and only that sample can carry a usable one: the
    /// native handler turns a fresh 0 into a 4 on the call itself.
    /// </param>
    /// <param name="nowUtc">
    /// The instant the native opcode actually ran, not the instant a queued capture
    /// was drained. The start window is measured from here, so a delayed drain must
    /// not buy the description a fresh two seconds.
    /// </param>
    /// <param name="currentSample">
    /// The live film state at the moment this is being processed. Episode
    /// bookkeeping and the "another film is already running" refusal use this,
    /// because a captured sample can be several frames old by now and feeding it to
    /// the episode counter would make the next live observation look like a second
    /// film start. Null means the caller has only one sample.
    /// </param>
    public bool NoteIngress(
        int fieldId,
        int entityId,
        int scriptId,
        int byteIndex,
        int opcode,
        FieldMovieNarrationSample sample,
        DateTime nowUtc,
        FieldMovieNarrationSample? currentSample = null)
    {
        lock (sync)
        {
            var liveState = currentSample ?? sample;
            AdvanceEpisode(liveState);
            if (opcode != FieldOpcodeAddressResolver.OpcodeMovieIndex)
            {
                return false;
            }

            // Any other film start invalidates an older opportunity, whether or not
            // it carries narration of its own. The docking F9 in field 496 is
            // exactly this case: it has no track, but it does end the arrival film's
            // opportunity.
            if (pending is { } existing &&
                (existing.Track.ByteIndex != byteIndex || existing.Track.FieldId != fieldId))
            {
                SettleAnchor(existing.Track, existing.EpisodeAtIngress, "another film start replaced it");
                pending = null;
            }

            if (!FieldMovieNarrationPolicy.TryResolve(fieldId, entityId, scriptId, byteIndex, out var track))
            {
                return false;
            }

            // The native handler re-delivers this same opcode on every frame of the
            // running film. A repeat must neither create a second opportunity nor
            // refresh the first one's clock, or the start window never closes.
            if (pending is { } live &&
                live.Track.ByteIndex == byteIndex &&
                live.Track.FieldId == fieldId)
            {
                return true;
            }

            var fresh = FieldMovieNarrationPolicy.IsFreshNativeStart(sample);
            if (fresh == true)
            {
                // The handler is on its fresh-entry branch, so this opcode really is
                // starting a film now. That is the strongest evidence available and
                // it releases any hold left by the previous attempt.
                ReleaseAnchorHold();
            }
            else if (fresh == false)
            {
                log(
                    $"Field movie narration ingress ignored ({track.Label}): the native " +
                    $"handler is re-entering a film it already started (state " +
                    $"{sample.MovieHandlerState}, phase {sample.MovieHandlerPhase}).");
                return false;
            }
            else if (IsHeldFor(track))
            {
                // No readable handler state. The previous opportunity for this exact
                // anchor was spent and nothing native has since said a new film can
                // begin there, so this is a repeat.
                log(
                    $"Field movie narration ingress ignored ({track.Label}): the previous " +
                    "start for this film has not been released by a native boundary.");
                return false;
            }
            else if (liveState.MovieActive && episode <= settledThroughEpisode)
            {
                log(
                    $"Field movie narration ingress ignored ({track.Label}): " +
                    $"episode {episode} was already started, described or expired.");
                return false;
            }

            // A different film is already running, so this cannot be starting our
            // track now. Judged on the live state, because whether some other film
            // owns the screen is a fact about now rather than about the capture.
            if (liveState.MovieActive && liveState.MovieNumber != track.MovieNumber)
            {
                log(
                    $"Field movie narration ingress ignored ({track.Label}): " +
                    $"film {liveState.MovieNumber} is running.");
                return false;
            }

            // Which film this address plays is not decided here. The number only
            // names a file once the disc is known, and the disc can move between one
            // visit and the next, so the opportunity is opened either way and the
            // film is identified in Begin from the live state.

            pending = new PendingStart(track, episode, nowUtc);
            return true;
        }
    }

    /// <summary>
    /// Attempts delivery. Only <see cref="FieldMovieNarrationStartResult.Started"/>
    /// means the ordinary paragraph must be suppressed, and only
    /// <see cref="FieldMovieNarrationStartResult.WaitingForNativeStart"/> means the
    /// cue must be kept queued.
    /// </summary>
    public FieldMovieNarrationStartResult Begin(
        int fieldId,
        int entityId,
        int scriptId,
        int byteIndex,
        FieldMovieNarrationSample sample,
        DateTime nowUtc)
    {
        lock (sync)
        {
            AdvanceEpisode(sample);

            if (!FieldMovieNarrationPolicy.TryResolve(fieldId, entityId, scriptId, byteIndex, out var track))
            {
                return FieldMovieNarrationStartResult.NotDescribed;
            }

            // The same opcode can be delivered twice while the film is still
            // running. Restarting would replay the description from zero.
            if (hasActiveTrack && output?.IsPlaying == true && activeEpisode == episode)
            {
                log($"Field movie narration already playing: {activeTrack.Label}.");
                return FieldMovieNarrationStartResult.Started;
            }

            // This film is already being described, by a schedule that took over from
            // the recording or by one that stood in for a recording that never
            // opened. The anchor's paragraph covers the same footage, so reading it
            // as well would describe the scene twice - and the schedule is the better
            // of the two, because it follows the film rather than summarising it.
            if (EpisodeIsAlreadyDescribed(sample))
            {
                log($"Field movie narration paragraph dropped ({track.Label}): " +
                    $"{episodeFilm} is already being described.");
                SettleAnchor(track, episode, "the film is already being described");
                pending = null;
                return FieldMovieNarrationStartResult.AlreadyDescribed;
            }

            if (pending is not { } opportunity ||
                opportunity.Track.ByteIndex != byteIndex ||
                opportunity.Track.FieldId != fieldId)
            {
                log($"Field movie narration skipped ({track.Label}): no live start opportunity.");
                return FieldMovieNarrationStartResult.Unavailable;
            }

            var expiry = ValidateForRetention(opportunity, sample, nowUtc);
            if (expiry is not null)
            {
                SettleAnchor(track, opportunity.EpisodeAtIngress, expiry);
                pending = null;
                return FieldMovieNarrationStartResult.Unavailable;
            }

            if (!sample.MovieActive)
            {
                // Measured from the first ingress, never from a repeat, so the
                // deferral cannot be extended indefinitely by the native repeats.
                if (nowUtc - opportunity.FirstIngressUtc <= FieldMovieNarrationPolicy.PreActivationWindow)
                {
                    return FieldMovieNarrationStartResult.WaitingForNativeStart;
                }

                SettleAnchor(track, opportunity.EpisodeAtIngress, "the native film did not start in time");
                pending = null;
                return FieldMovieNarrationStartResult.Unavailable;
            }

            pending = null;

            // What is actually on screen. A script address names a film by number
            // and the number means a different film once the disc has moved, so the
            // recording and the paragraph both follow the running film rather than
            // the address. When the two agree - every anchor on disc 1 - this is the
            // track that was already going to play.
            var runningFilm = LatchEpisodeFilm(sample, nowUtc);
            var describesThisAnchor = string.Equals(
                runningFilm, track.FilmFileName, StringComparison.OrdinalIgnoreCase);
            if (!describesThisAnchor)
            {
                if (runningFilm is null ||
                    MovieFilmDescriptionCatalog.Paragraph(runningFilm) is null)
                {
                    log(
                        $"Field movie narration refused ({track.Label}): film " +
                        $"{sample.MovieNumber} on disc {sample.Disc} is " +
                        $"{runningFilm ?? "unidentifiable"}, not {track.FilmFileName}.");
                    SettleAnchor(track, episode, "the running film is not this one");
                    return FieldMovieNarrationStartResult.DescribesADifferentFilm;
                }

                log(
                    $"Field movie narration follows the running film ({track.Label}): " +
                    $"film {sample.MovieNumber} on disc {sample.Disc} is {runningFilm}.");
                track = track with
                {
                    MovieNumber = sample.MovieNumber,
                    FileName = MovieFilmDescriptionCatalog.RecordingFileName(runningFilm),
                    Label = runningFilm,
                };
            }

            // When the running film is not this anchor's, its paragraph is wrong,
            // so every way of failing below has to say so rather than fall back to it.
            var unavailable = describesThisAnchor
                ? FieldMovieNarrationStartResult.Unavailable
                : FieldMovieNarrationStartResult.DescribesADifferentFilm;

            // The film owns the device from here. A field description still playing
            // is about the room, not the film now on screen, so it gives way before
            // the film's own recording opens rather than overlapping it.
            YieldDescriptionAudio(CutsceneVoiceOwner.FieldAction, $"the film {track.Label} is starting");

            IFieldMovieNarrationOutput? candidate;
            try
            {
                candidate = createOutput(track);
            }
            catch (Exception ex)
            {
                // A factory that throws must behave exactly like a missing asset:
                // the ordinary paragraph is still spoken.
                log($"Field movie narration output could not be created ({track.Label}): {ex.Message}");
                SettleAnchor(track, episode, "output could not be created");
                return unavailable;
            }

            if (candidate is null)
            {
                log($"Field movie narration unavailable: {track.Label}.");
                SettleAnchor(track, episode, "no output available");
                return unavailable;
            }

            output?.Dispose();
            output = candidate;

            bool started;
            try
            {
                started = output.Start($"native film {track.MovieNumber} at {track.FieldId}:{track.ByteIndex}");
            }
            catch (Exception ex)
            {
                log($"Field movie narration failed to start ({track.Label}): {ex.Message}");
                SafeDisposeOutput();
                SettleAnchor(track, episode, "the device threw while starting");
                return unavailable;
            }

            if (!started)
            {
                SafeDisposeOutput();
                SettleAnchor(track, episode, "the device refused to start");
                return unavailable;
            }

            activeTrack = track;
            activeEpisode = episode;
            hasActiveTrack = true;
            // Settling here is what stops a native F9 repeat from restarting the
            // description from zero half-way through the film it is describing.
            SettleAnchor(track, episode, "started");
            log($"Field movie narration started: {track.Label} ({track.DurationSeconds:0.#}s), episode {episode}.");
            return FieldMovieNarrationStartResult.Started;
        }
    }

    /// <summary>Per-tick native lifecycle. Expires opportunities and stops tracks.</summary>
    /// <param name="dialogueIsOnScreen">
    /// Whether the game has a readable message window open. A film with native text
    /// over it is not unusual - the observatory's Study of Planet Life narrates
    /// itself in ordinary field text while the film runs - and the game's own words
    /// win, because the description is there to add what cannot be read, not to talk
    /// over what can.
    /// </param>
    public void Observe(
        FieldMovieNarrationSample sample,
        DateTime nowUtc,
        bool dialogueIsOnScreen = false)
    {
        lock (sync)
        {
            AdvanceEpisode(sample);

            if (pending is { } opportunity)
            {
                var reason = ValidateForRetention(opportunity, sample, nowUtc);
                if (reason is null &&
                    !sample.MovieActive &&
                    nowUtc - opportunity.FirstIngressUtc > FieldMovieNarrationPolicy.PreActivationWindow)
                {
                    reason = "the native film did not start in time";
                }

                if (reason is not null)
                {
                    SettleAnchor(opportunity.Track, opportunity.EpisodeAtIngress, reason);
                    pending = null;
                }
            }

            // The film's position moves on every frame, and everything timed here
            // reads it from this one place. Without this the schedule would be
            // positioned at wherever the film was when it started.
            _ = LatchEpisodeFilm(sample, nowUtc);

            // Deferred cues live on the film, not on the audio device, so nothing
            // below stops them being spoken. They have to be invalidated here, on
            // every way the film can stop being the thing on screen: it ended, it is
            // being skipped, its identity stopped resolving, the frame stopped
            // reading, the module changed, or it became a different film. Without
            // this, a queued sentence is spoken into ordinary gameplay minutes later.
            if (deferredCues is not null && !EpisodeStateIsStillValid(deferredEpisode, sample))
            {
                log("Field movie narration deferred cues dropped: the film they describe is gone.");
                deferredCues = null;
            }

            // A cue already handed to the recorded voice is checked separately, and
            // against the episode it was spoken for rather than against the schedule.
            // The schedule can be gone while the clip is still talking - it is
            // dropped here, it is nulled when a new film starts, and it is nulled
            // when the last cue has been taken - and in every one of those cases the
            // clip is still on the device with nothing watching it.
            if (filmCueAudioEpisode >= 0 && !EpisodeStateIsStillValid(filmCueAudioEpisode, sample))
            {
                filmCueAudioEpisode = -1;
                YieldDescriptionAudio(
                    CutsceneVoiceOwner.FilmCue, "the film the cue describes is gone");
            }

            if (!hasActiveTrack)
            {
                // Nothing is playing. If a film has just come up that has a recording
                // of its own, this is where it starts - no anchor needed, because the
                // engine raising its own flag is the film starting and is the same
                // evidence for every film in the game.
                StartRunningFilmLocked(sample, nowUtc, dialogueIsOnScreen);
                return;
            }

            var stop = FieldMovieNarrationPolicy.ResolveStopReason(sample, activeTrack, fieldModule);

            // A playing recording rests on the same evidence a starting one does. The
            // stop policy above only knows about the module, the field, the active
            // flag and the number; it cannot see the command byte changing hands, the
            // skip gate coming on, or the frame becoming unreadable. Any of those and
            // we no longer know what is on screen, so the audio stops rather than
            // playing on over a film nobody can identify.
            if (stop == FieldMovieNarrationStopReason.None && !sample.PlaybackIsVerified)
            {
                log($"Field movie narration stopping ({activeTrack.Label}): the film can no longer " +
                    $"be verified - command {sample.MovieCommand}, skipped {sample.MoviesSkipped}, " +
                    $"frame {sample.MovieFrame}.");
                stop = FieldMovieNarrationStopReason.MovieEnded;
            }

            // The game has put its own words on screen. The recording has no way to
            // hold - start and stop is all it has - so it gives way, and the rest of
            // the film is described from its cue windows through ordinary speech,
            // which yields to dialogue by itself. The audio is never restarted from
            // the beginning and never runs on behind the picture.
            if (stop == FieldMovieNarrationStopReason.None && dialogueIsOnScreen)
            {
                DeferRemainingCuesLocked();
                StopLocked(FieldMovieNarrationStopReason.DialogueOpened);
                return;
            }

            if (stop == FieldMovieNarrationStopReason.None && activeEpisode != episode)
            {
                stop = FieldMovieNarrationStopReason.OtherMovieStarted;
            }

            if (stop == FieldMovieNarrationStopReason.None)
            {
                // The device can finish before the native film does.
                if (output?.IsPlaying != false)
                {
                    return;
                }

                stop = FieldMovieNarrationStopReason.MovieEnded;
            }

            StopLocked(stop);
        }
    }

    /// <summary>
    /// Offers the next thing to say about a film whose recording had to give way, and
    /// only advances past it once the speaker has actually taken it.
    ///
    /// <para>A cue is offered once the film has reached its moment - the film's own
    /// frame counter, not the wall clock - and only while the game is not talking, so
    /// the description and the dialogue alternate instead of overlapping. A cue whose
    /// moment has passed by more than
    /// <see cref="FieldMovieNarrationPolicy.CueLatenessAllowance"/> is dropped rather
    /// than spoken late: describing a shot that has left the screen is the failure
    /// this path exists to avoid.</para>
    ///
    /// <para><paramref name="trySpeak"/> is what commits. A speaker that refuses
    /// leaves the cue exactly where it was, to be offered again on the next tick,
    /// which is the same contract the ordinary description queue keeps.</para>
    /// </summary>
    public bool TryDeliverDeferredCue(
        bool dialogueIsOnScreen,
        Func<string, bool> trySpeak,
        out string delivered)
    {
        ArgumentNullException.ThrowIfNull(trySpeak);
        delivered = string.Empty;
        lock (sync)
        {
            if (deferredCues is null || dialogueIsOnScreen || deferredEpisode != episode ||
                episodePositionSeconds < 0d)
            {
                return false;
            }

            var position = episodePositionSeconds;
            while (deferredIndex < deferredCues.Count)
            {
                var cue = deferredCues[deferredIndex];
                if (position < cue.Start)
                {
                    return false;
                }

                if (position > cue.End + FieldMovieNarrationPolicy.CueLatenessAllowance.TotalSeconds)
                {
                    deferredIndex++;
                    log($"Field movie narration cue at {cue.Start:0.#}s dropped: the film is at {position:0.#}s.");
                    continue;
                }

                // Offered, not yet spent. Only acceptance advances the schedule.
                bool accepted;
                try
                {
                    accepted = trySpeak(cue.Text);
                }
                catch (Exception ex)
                {
                    log($"Field movie narration cue could not be spoken: {ex.Message}");
                    return false;
                }

                if (!accepted)
                {
                    return false;
                }

                deferredIndex++;
                delivered = cue.Text;

                // Whoever said it may still be saying it. Remember which film it was
                // said for, so the next native tick that shows a different film can
                // take the device back.
                filmCueAudioEpisode = episode;
                log($"Field movie narration cue at {cue.Start:0.#}s spoken at {position:0.#}s.");
                return true;
            }

            deferredCues = null;
            return false;
        }
    }

    /// <summary>
    /// Starts the recording for whatever film is now on screen. This is the path that
    /// covers every film rather than only the ones with a script anchor: identity
    /// comes from the engine's own state, so a film started from a site nobody
    /// enumerated is described exactly like one that was.
    /// </summary>
    private void StartRunningFilmLocked(
        FieldMovieNarrationSample sample,
        DateTime nowUtc,
        bool dialogueIsOnScreen)
    {
        // Command 3 is the engine opening the next file while the previous film's
        // flag may still be up. That is the right moment to know which film is
        // coming and the wrong moment to start describing it.
        if (!sample.FilmIsPlaying)
        {
            return;
        }

        var film = LatchEpisodeFilm(sample, nowUtc);
        if (film is null || settledThroughEpisode >= episode)
        {
            return;
        }

        // The opening movie has its own player and its own lifetime. Starting it here
        // as well would have the player hear the whole thing twice.
        if (string.Equals(film, OpeningFilmFileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (MovieFilmDescriptionCatalog.Paragraph(film) is null)
        {
            return;
        }

        var track = new FieldMovieNarrationTrack(
            sample.CurrentFieldId, 0, 0, 0, sample.MovieNumber,
            MovieFilmDescriptionCatalog.RecordingFileName(film), film, 0d);

        // A continuous recording is fixed from its first second, so it may only start
        // where the film is. Attaching to a film already in progress - the mod loaded
        // late, the identity only became readable part-way through - must not play a
        // description of the opening shot over the middle of the film. Past that
        // bound the film is still described, from the cues its position has reached.
        var startFromTheTop = episodePositionSeconds >= 0d &&
            episodePositionSeconds <= FieldMovieNarrationPolicy.ContinuousStartAllowance.TotalSeconds;

        // Talking over the game's own words is the other thing this must not do, so a
        // film that comes up mid-conversation goes straight to deferred cues too.
        if (dialogueIsOnScreen || !startFromTheTop)
        {
            var cues = readCues?.Invoke(track);
            if (cues is { Count: > 0 })
            {
                deferredCues = [.. cues];
                deferredEpisode = episode;
                deferredIndex = 0;
                while (deferredIndex < deferredCues.Count &&
                       deferredCues[deferredIndex].End <= episodePositionSeconds)
                {
                    deferredIndex++;
                }

                log($"Field movie narration described from cues ({film}) at " +
                    $"{episodePositionSeconds:0.#}s: " +
                    (dialogueIsOnScreen ? "dialogue is on screen" : "the film is already under way") +
                    $"; {deferredCues.Count - deferredIndex} cue(s) to come.");
            }
            else
            {
                log($"Field movie narration has no cue schedule for {film}; " +
                    "it cannot be described from part-way through.");
            }

            Settle(episode, $"{film}: not started from the beginning");
            return;
        }

        // Same rule on the path that needs no anchor at all.
        YieldDescriptionAudio(CutsceneVoiceOwner.FieldAction, $"the film {film} is starting");

        IFieldMovieNarrationOutput? candidate;
        try
        {
            candidate = createOutput(track);
        }
        catch (Exception ex)
        {
            log($"Field movie narration output could not be created ({film}): {ex.Message}");
            DescribeFromCuesLocked(track, film, "the output could not be created");
            return;
        }

        if (candidate is null)
        {
            // No recording, but the film is still happening. Its reviewed cues go
            // through ordinary speech instead: describing it late is far better than
            // settling quietly and saying nothing for the whole film.
            DescribeFromCuesLocked(track, film, "no recording is installed");
            return;
        }

        bool started;
        try
        {
            started = candidate.Start($"native film {sample.MovieNumber} ({film})");
        }
        catch (Exception ex)
        {
            log($"Field movie narration failed to start ({film}): {ex.Message}");
            try { candidate.Dispose(); } catch (Exception) { }
            DescribeFromCuesLocked(track, film, "the device threw while starting");
            return;
        }

        if (!started)
        {
            try { candidate.Dispose(); } catch (Exception) { }
            DescribeFromCuesLocked(track, film, "the device refused to start");
            return;
        }

        output?.Dispose();
        output = candidate;
        activeTrack = track;
        activeEpisode = episode;
        hasActiveTrack = true;
        Settle(episode, $"{film}: started");
        log($"Field movie narration started from the native film: {film}, episode {episode}.");
    }

    /// <summary>
    /// Decides once per episode which film this is, and remembers when it started.
    /// Deciding again later would risk reading the argument word after another
    /// command has taken it over.
    /// </summary>
    private string? LatchEpisodeFilm(FieldMovieNarrationSample sample, DateTime nowUtc)
    {
        _ = nowUtc;
        if (episodeFilmDecidedFor == episode)
        {
            // The film was decided when the episode began, but that is not a licence
            // to keep asserting it: this sample has to still show the same film, or
            // the caller is told we no longer know.
            if (ResolveFilmAtSample(sample) != episodeFilm || sample.PositionSeconds is not { } current)
            {
                return null;
            }

            episodePositionSeconds = current;
            return episodeFilm;
        }

        var film = ResolveFilmAtSample(sample);
        if (film is null || sample.PositionSeconds is not { } position)
        {
            // Without the film's own clock there is no way to know where in the film
            // this is, so there is no honest way to describe it.
            return null;
        }

        episodeFilm = film;
        episodeFilmDecidedFor = episode;
        episodePositionSeconds = position;
        return film;
    }

    /// <summary>
    /// Whether something belonging to <paramref name="ownerEpisode"/> - a cue
    /// schedule, or a clip already speaking one of its cues - still belongs to what
    /// is on screen. Every clause is a way of having lost the evidence it rests on:
    /// a different film, a film that ended or is being skipped, an identity or a
    /// frame that stopped reading, or a module change.
    ///
    /// <para>The field is deliberately not a clause. One film runs across several
    /// fields, and its cues describe the film.</para>
    /// </summary>
    private bool EpisodeStateIsStillValid(long ownerEpisode, FieldMovieNarrationSample sample) =>
        ownerEpisode == episode &&
        sample.CurrentModule == fieldModule &&
        sample.PlaybackIsVerified &&
        episodeFilm is not null &&
        ResolveFilmAtSample(sample) == episodeFilm;

    /// <summary>
    /// Describes a film through its cue schedule rather than its recording, from
    /// wherever the film has got to. Used when there is no recording to play, when
    /// the device will not take it, and when the film was already under way before
    /// anything could start.
    ///
    /// <para>The episode is settled either way, so the anchor cannot try again; what
    /// differs is whether the film is left undescribed. A film with cues is
    /// described; one without keeps its ordinary paragraph.</para>
    /// </summary>
    private void DescribeFromCuesLocked(
        FieldMovieNarrationTrack track,
        string film,
        string why)
    {
        var cues = readCues?.Invoke(track);
        if (cues is not { Count: > 0 })
        {
            log($"Field movie narration unavailable ({film}): {why}, and it has no cue schedule.");
            Settle(episode, $"{film}: {why}");
            return;
        }

        deferredCues = [.. cues];
        deferredEpisode = episode;
        deferredIndex = 0;
        while (deferredIndex < deferredCues.Count &&
               deferredCues[deferredIndex].End <= episodePositionSeconds)
        {
            deferredIndex++;
        }

        log($"Field movie narration describing {film} from its cues at " +
            $"{episodePositionSeconds:0.#}s: {why}; " +
            $"{deferredCues.Count - deferredIndex} cue(s) to come.");
        Settle(episode, $"{film}: described from cues because {why}");
    }

    /// <summary>
    /// Whether this episode's film is already being described, by a playing recording
    /// or by a cue schedule that has taken over from one. An anchor's paragraph about
    /// the same film must not be read out on top of either.
    ///
    /// <para>Ownership outlives the schedule running out: the last cue being spoken
    /// does not make the paragraph welcome again.</para>
    /// </summary>
    private bool EpisodeIsAlreadyDescribed(FieldMovieNarrationSample sample)
    {
        if (episodeFilmDecidedFor != episode || episodeFilm is null)
        {
            return false;
        }

        if (!sample.PlaybackIsVerified || ResolveFilmAtSample(sample) != episodeFilm)
        {
            return false;
        }

        return (hasActiveTrack && activeEpisode == episode) || deferredEpisode == episode;
    }

    /// <summary>
    /// Hands the rest of the film to the cue schedule, positioned by how far into the
    /// film the recording actually got.
    /// </summary>
    private void DeferRemainingCuesLocked()
    {
        if (episodePositionSeconds < 0d || readCues is null)
        {
            return;
        }

        var cues = readCues(activeTrack);
        if (cues is not { Count: > 0 })
        {
            log($"Field movie narration has no cue schedule for {activeTrack.Label}; " +
                "the rest of the film cannot be described around the dialogue.");
            return;
        }

        var position = episodePositionSeconds;
        deferredCues = [.. cues];
        deferredEpisode = episode;
        deferredIndex = 0;

        // Only cues that had finished are behind us. A cue the dialogue cut off part
        // way through has not been heard, and it is usually the one describing what
        // the player is looking at right now, so it is kept and said again in full.
        while (deferredIndex < deferredCues.Count && deferredCues[deferredIndex].End <= position)
        {
            deferredIndex++;
        }

        log($"Field movie narration gave way to dialogue at {position:0.#}s ({activeTrack.Label}); " +
            $"{deferredCues.Count - deferredIndex} cue(s) still to come.");
    }

    /// <summary>The opening movie, which is narrated by its own player.</summary>
    private const string OpeningFilmFileName = "opening.avi";

    public void Stop(FieldMovieNarrationStopReason reason)
    {
        lock (sync)
        {
            if (pending is { } opportunity)
            {
                // Held rather than released: a suspend is not a native boundary, and
                // after the foreground returns the handler's repeat of the same
                // opcode must not restart the description part-way through. The next
                // observed boundary releases it.
                SettleAnchor(opportunity.Track, opportunity.EpisodeAtIngress, reason.ToString());
                pending = null;
            }

            // Losing the foreground or being unloaded is not the game pausing for a
            // line of dialogue: the deferred cues would be spoken against a clock
            // that no longer matches anything on screen.
            deferredCues = null;
            filmCueAudioEpisode = -1;
            YieldDescriptionAudio(CutsceneVoiceOwner.FilmCue, reason.ToString());
            StopLocked(reason);
        }
    }

    private string? ValidateForRetention(
        PendingStart opportunity,
        FieldMovieNarrationSample sample,
        DateTime nowUtc)
    {
        if (sample.CurrentModule != fieldModule)
        {
            return "the module changed";
        }

        if (sample.CurrentFieldId != opportunity.Track.FieldId)
        {
            return "the field changed";
        }

        if (nowUtc - opportunity.FirstIngressUtc > FieldMovieNarrationPolicy.StartWindow)
        {
            return "the start window elapsed; the film is already part-way through";
        }

        // A different film took over while the opportunity was waiting.
        if (sample.MovieActive && sample.MovieNumber != opportunity.Track.MovieNumber)
        {
            return $"film {sample.MovieNumber} replaced it";
        }

        // The film that was running when the opcode was seen has since ended.
        if (opportunity.EpisodeAtIngress != episode && !sample.MovieActive)
        {
            return "the film ended before the description could start";
        }

        return null;
    }

    private void AdvanceEpisode(FieldMovieNarrationSample sample)
    {
        // A new episode is a new film: the flag rising, or the number changing under
        // it. The disc counts too - the same number on another disc is another film -
        // and so does the command byte changing hands, because the number the episode
        // was decided from is only that command's argument.
        var started = sample.MovieActive &&
                      (!lastMovieActive || sample.MovieNumber != lastMovieNumber ||
                       sample.Disc != lastDisc);
        if (started)
        {
            episode++;
            deferredCues = null;
        }

        // Only a native boundary releases a spent anchor: the film that was running
        // has ended, the handler has reported its own completion pass, or the script
        // has left the module or the field the anchor belongs to. Time alone must not
        // release it, because the case being guarded against is precisely a film that
        // takes longer than expected to raise its active flag.
        if (anchorHeld &&
            ((lastMovieActive && !sample.MovieActive) ||
             FieldMovieNarrationPolicy.IsNativeCompletion(sample) ||
             sample.CurrentModule != fieldModule ||
             sample.CurrentFieldId != heldFieldId))
        {
            ReleaseAnchorHold();
        }

        lastMovieActive = sample.MovieActive;
        lastMovieNumber = sample.MovieNumber;
        lastDisc = sample.Disc;
    }

    private bool IsHeldFor(FieldMovieNarrationTrack track) =>
        anchorHeld && heldFieldId == track.FieldId && heldByteIndex == track.ByteIndex;

    /// <summary>
    /// Settles the episode and holds the anchor, so the native handler's repeats of
    /// the same opcode cannot buy a second start for the same film.
    /// </summary>
    private void SettleAnchor(FieldMovieNarrationTrack track, long settledEpisode, string reason)
    {
        anchorHeld = true;
        heldFieldId = track.FieldId;
        heldByteIndex = track.ByteIndex;
        Settle(settledEpisode, $"{track.Label}: {reason}");
    }

    private void ReleaseAnchorHold()
    {
        anchorHeld = false;
        heldFieldId = 0;
        heldByteIndex = 0;
    }

    /// <summary>
    /// Records that an episode's one start opportunity is spent. Episodes only ever
    /// increase, so a single high-water mark is enough and cannot grow without
    /// bound. A pre-activation opportunity settles both the episode it was seen in
    /// and the one the film actually started, because the film start incremented the
    /// counter between ingress and delivery.
    /// </summary>
    private void Settle(long settledEpisode, string reason)
    {
        var highest = Math.Max(settledEpisode, episode);
        if (highest > settledThroughEpisode)
        {
            settledThroughEpisode = highest;
        }

        log($"Field movie narration episode {highest} settled: {reason}.");
    }

    private void SafeDisposeOutput()
    {
        try
        {
            output?.Dispose();
        }
        catch (Exception ex)
        {
            log($"Field movie narration device could not be released: {ex.Message}");
        }

        output = null;
    }

    private void StopLocked(FieldMovieNarrationStopReason reason)
    {
        if (!hasActiveTrack)
        {
            return;
        }

        var label = activeTrack.Label;
        try
        {
            output?.Stop(reason.ToString());
        }
        catch (Exception ex)
        {
            log($"Field movie narration could not be stopped cleanly: {ex.Message}");
        }

        SafeDisposeOutput();
        hasActiveTrack = false;
        activeTrack = default;
        log($"Field movie narration stopped: {label}, reason={reason}.");
    }

    public void Dispose()
    {
        lock (sync)
        {
            pending = null;
            ReleaseAnchorHold();
            StopLocked(FieldMovieNarrationStopReason.Unloaded);
        }
    }

    private sealed record PendingStart(
        FieldMovieNarrationTrack Track,
        long EpisodeAtIngress,
        DateTime FirstIngressUtc);
}
