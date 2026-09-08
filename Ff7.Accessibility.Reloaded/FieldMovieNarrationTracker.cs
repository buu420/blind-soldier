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
    /// Stale, disabled, missing, failed or the wrong film. Speak the paragraph now.
    /// </summary>
    Unavailable
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

    private PendingStart? pending;

    // Episode bookkeeping, driven purely by observed native samples. Episodes only
    // ever increase, so one high-water mark records every settled episode.
    private long episode;
    private bool lastMovieActive;
    private int lastMovieNumber;
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

    public FieldMovieNarrationTracker(
        Func<FieldMovieNarrationTrack, IFieldMovieNarrationOutput?> createOutput,
        Action<string> log,
        int fieldModule)
    {
        this.createOutput = createOutput;
        this.log = log;
        this.fieldModule = fieldModule;
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
                return FieldMovieNarrationStartResult.Unavailable;
            }

            if (candidate is null)
            {
                log($"Field movie narration unavailable: {track.Label}.");
                SettleAnchor(track, episode, "no output available");
                return FieldMovieNarrationStartResult.Unavailable;
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
                return FieldMovieNarrationStartResult.Unavailable;
            }

            if (!started)
            {
                SafeDisposeOutput();
                SettleAnchor(track, episode, "the device refused to start");
                return FieldMovieNarrationStartResult.Unavailable;
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
    public void Observe(FieldMovieNarrationSample sample, DateTime nowUtc)
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

            if (!hasActiveTrack)
            {
                return;
            }

            var stop = FieldMovieNarrationPolicy.ResolveStopReason(sample, activeTrack, fieldModule);
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
        var started = sample.MovieActive &&
                      (!lastMovieActive || sample.MovieNumber != lastMovieNumber);
        if (started)
        {
            episode++;
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
