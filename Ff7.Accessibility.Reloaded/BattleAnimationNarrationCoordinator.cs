namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Tells each limit break and summon animation once per save, in the recorded voice, as
/// a schedule of cues timed against the engine's own battle clock.
///
/// <para><b>When.</b> Only a row the engine has started counts (see
/// <see cref="BattleAnimationStateReader"/>): choosing a limit in the menu, a queue
/// written ahead of its turn and an attack cancelled before its turn never play, so they
/// never speak. Each started row opens one schedule; polling the same row again and
/// again changes nothing. Tifa's reels each queue their own row, so every move that
/// plays has its own schedule, and a reel that missed queued nothing to tell.</para>
///
/// <para><b>Clock.</b> A cue's <see cref="BattleAnimationCue.AtFrame"/> counts native
/// battle ticks since the row started: the wrapping byte <c>0x00BFD0E4</c> that
/// <c>FUN_0042D808</c> increments once per animation-queue update (15 a second in
/// vanilla). Ticks from a poll interval with the battle paused at either end are not
/// counted, because the queue tick runs on while effects and scripts are frozen. A gap
/// between readable polls longer than <see cref="MaximumTickGap"/> is not counted either,
/// since the byte could have wrapped unseen. The schedule's phase lasts while the engine
/// keeps that row current, which includes effects still running after the performer is
/// idle; it ends when the queue moves on, empties or the battle ends.</para>
///
/// <para><b>Order.</b> One cue plays at a time and never before its tick. A cue that is
/// due waits for the independent voice. Once its phase has ended it may still start for
/// <see cref="GraceAfterPhase"/> of unpaused time and is then dropped; cues whose tick the
/// animation never reached are not told at all. Nothing starts on an unreadable scan or
/// outside battle.</para>
///
/// <para><b>Once.</b> The save's history records the description's own key
/// (<see cref="BattleAnimationDescription.HistoryKey"/>) only when the phase has ended
/// and every cue it reached was delivered: a recording its device reports as played to
/// the end (<see cref="IFieldMovieNarrationCompletion"/>), or words a screen reader
/// accepted when there is no recording. A cue that failed, stuck, was stopped or dropped
/// leaves the animation unheard, and a load or new game discards everything in flight.
/// The whole schedule is then told again the next time the animation plays.</para>
///
/// <para><b>Anchor.</b> Cue ticks count from the row start, or for a summon described
/// with <see cref="BattleAnimationAnchor.SummonSequence"/> from the tick the summon
/// dispatcher <c>FUN_005C0E4B</c> left the effect table, which is when its stage 1 began
/// the summon's own sequence. A summon whose sequence never began reaches no cue.</para>
///
/// <para><b>Pause.</b> While the battle is paused no cue starts, and the playing
/// recording is paused through <see cref="IFieldMovieNarrationPause"/> and resumed where
/// it stood. A recording that cannot pause is stopped instead, so the description never
/// runs ahead of a frozen battle, and it is not counted as delivered. Paused time does not
/// count towards the stuck-device limit.</para>
///
/// <para><b>Lost progress.</b> A gap between readable scans longer than
/// <see cref="MaximumTickGap"/> leaves the engine's real progress unknown. Every schedule
/// that was live across it is abandoned - its later cues would be stale - and is not
/// heard, so a long stall can never pass for a short animation.</para>
///
/// <para><b>Summon opening.</b> Every summon cast also opens the shared casting
/// description (<see cref="BattleAnimationNarrationCatalog.SummonOpening"/>), under its
/// own history key, so it is told once per save whether or not that summon's own
/// description was heard. Its ticks count from the tick the attack-name banner came on
/// screen; a banner already showing when the cast is first seen leaves the anchor
/// unknown, and nothing of it is told. It waits while the screen reader is speaking (the
/// banner's name is spoken then), may start only while the summoner is still casting -
/// once the summon's own sequence begins its untold cues are dropped - and gives way to
/// the summon's own cues: if one falls due while the opening is still playing, the
/// opening is stopped, so their validated timing never shifts. Dropped or stopped, the
/// opening is not heard and is told at a later summon.</para>
///
/// <para><b>Frame rate.</b> Cue ticks are authored at the vanilla 15 a second. FFNx's
/// 30 and 60 fps battle modes multiply every script wait by its battle frame multiplier
/// (2 or 4), so the coordinator is told that multiplier and scales cue ticks by it.</para>
///
/// <para>Nothing here pauses the game or touches its input: the hosts call
/// <see cref="Update"/> from their own monitor loop, never from a game detour.</para>
/// </summary>
public sealed class BattleAnimationNarrationCoordinator : IDisposable
{
    /// <summary>The most animations being told or waiting at once. Tifa's chain is seven.</summary>
    public const int MaximumPending = 8;

    /// <summary>How long after its animation a due cue may still start.</summary>
    public static readonly TimeSpan GraceAfterPhase = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The longest gap between readable polls whose tick difference is trusted: well under
    /// the 17 seconds the byte takes to wrap at 15 ticks a second. Divide this by the
    /// battle frame multiplier: at 60 fps the same byte wraps in only 4.27 seconds.
    /// </summary>
    public static readonly TimeSpan MaximumTickGap = TimeSpan.FromSeconds(12);

    private readonly BattleAnimationNarrationCatalog catalog;
    private readonly FieldAreaDescriptionHistory history;
    private readonly CutsceneVoiceManifest voice;
    private readonly Func<CutsceneVoiceClip, IFieldMovieNarrationOutput?> createVoiceOutput;
    private readonly Action<string> log;
    private readonly Func<string, bool>? speakWithoutRecording;
    private readonly Func<bool> anotherDescriptionIsPlaying;
    private readonly Func<bool?>? speechIsPlaying;
    private readonly object sync = new();
    private readonly List<Schedule> schedules = [];

    private Episode? lastEpisode;
    private Playback? playing;
    private long playthroughRevision;
    private byte? lastTick;
    private bool lastPaused;
    private DateTime lastReadableUtc;
    private DateTime? lastUpdateUtc;
    private readonly int ticksPerCueFrame;
    private TimeSpan TrustedTickGap => MaximumTickGap / ticksPerCueFrame;

    // Read without the lock. The cutscene voice asks this while holding its own lock,
    // and a scan here asks the cutscene voice the same question; if the answer waited on
    // a scan, the two players could wait on each other forever.
    private volatile bool isPlaying;

    /// <param name="voice">The recordings, looked up by the exact cue text.</param>
    /// <param name="createVoiceOutput">Opens one recording; null when it cannot.</param>
    /// <param name="speakWithoutRecording">
    /// Speaks a cue that has no usable recording and says whether the speaker accepted it,
    /// or null when cues without a recording stay silent.
    /// </param>
    /// <param name="anotherDescriptionIsPlaying">
    /// Whether another description owns the independent voice, so this one waits.
    /// </param>
    /// <param name="ticksPerCueFrame">
    /// Native battle ticks per authored cue tick: 1 at the vanilla 15 a second, FFNx's
    /// battle frame multiplier (2 or 4) in its 30 and 60 fps battle modes.
    /// </param>
    /// <param name="speechIsPlaying">
    /// The screen reader's own word on whether it is still speaking, or null when it cannot
    /// say. The summon opening waits while it is.
    /// </param>
    public BattleAnimationNarrationCoordinator(
        BattleAnimationNarrationCatalog catalog,
        FieldAreaDescriptionHistory history,
        CutsceneVoiceManifest voice,
        Func<CutsceneVoiceClip, IFieldMovieNarrationOutput?> createVoiceOutput,
        Action<string> log,
        Func<string, bool>? speakWithoutRecording = null,
        Func<bool>? anotherDescriptionIsPlaying = null,
        int ticksPerCueFrame = 1,
        Func<bool?>? speechIsPlaying = null)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.history = history ?? throw new ArgumentNullException(nameof(history));
        this.voice = voice ?? CutsceneVoiceManifest.Empty;
        this.createVoiceOutput = createVoiceOutput ?? throw new ArgumentNullException(nameof(createVoiceOutput));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.speakWithoutRecording = speakWithoutRecording;
        this.anotherDescriptionIsPlaying = anotherDescriptionIsPlaying ?? (static () => false);
        this.speechIsPlaying = speechIsPlaying;
        this.ticksPerCueFrame = ticksPerCueFrame is >= 1 and <= 8
            ? ticksPerCueFrame
            : throw new ArgumentOutOfRangeException(nameof(ticksPerCueFrame));
        playthroughRevision = history.PlaythroughRevision;
    }

    /// <summary>Whether a battle narration owns the independent voice. Never waits.</summary>
    public bool IsPlaying => isPlaying;

    /// <summary>
    /// One scan. <paramref name="observation"/> is null when the queue could not be read
    /// coherently: it moves no clock and starts nothing, but the recording that is
    /// playing is still looked after.
    /// </summary>
    public void Update(BattleAnimationObservation? observation, DateTime nowUtc)
    {
        lock (sync)
        {
            var revision = history.PlaythroughRevision;
            if (revision != playthroughRevision)
            {
                Stop("another playthrough was loaded");
                lastEpisode = null;
                playthroughRevision = revision;
            }

            var wall = lastUpdateUtc is { } previous && nowUtc > previous ? nowUtc - previous : TimeSpan.Zero;
            lastUpdateUtc = nowUtc;

            // Only phases that had already ended before this scan age by its interval.
            if (observation?.Paused != true)
            {
                foreach (var schedule in schedules.Where(schedule => !schedule.PhaseLive))
                {
                    schedule.SincePhaseEnd += wall;
                }
            }

            if (observation is { } seen)
            {
                var ticks = CountTicks(seen, nowUtc, out var progressLost);
                FollowPhases(seen, ticks, wall, progressLost);
                FollowPause(seen.Paused, nowUtc);
            }

            var voiceFree = playing is not { } current || FinishPlayback(current, nowUtc);
            if (!voiceFree && observation is { InBattle: true, Paused: false })
            {
                voiceFree = GiveWayToADueCue();
            }

            ExpireAndSettle();
            if (observation is { } started)
            {
                OpenSchedule(started);
            }

            // An unreadable scan cannot say the battle is still on, so nothing is started
            // from it; the clip that was playing has still been settled above.
            if (voiceFree && observation is { InBattle: true, Paused: false })
            {
                StartDueCue(nowUtc);
            }
        }
    }

    /// <summary>
    /// Stops the recording and forgets every schedule, none of it recorded as heard. For
    /// the mod unloading, the option being switched off, or the window losing focus.
    /// </summary>
    public void Stop(string reason)
    {
        lock (sync)
        {
            schedules.Clear();
            if (playing is not { } current)
            {
                return;
            }

            SetPlaying(null);
            SafeStop(current.Output, reason);
            SafeDispose(current.Output);
            log($"Battle animation narration stopped ({reason}): {current.Schedule.Description.Identity.Key} not marked heard.");
        }
    }

    public void Dispose() => Stop("unloaded");

    /// <summary>
    /// Unpaused native ticks since the previous readable scan. <paramref name="lost"/> is
    /// set when the scans were too far apart for the wrapping byte to be trusted.
    /// </summary>
    private int CountTicks(BattleAnimationObservation seen, DateTime nowUtc, out bool lost)
    {
        lost = false;
        if (!seen.InBattle)
        {
            lastTick = null;
            return 0;
        }

        var counted = 0;
        if (lastTick is { } before)
        {
            if (nowUtc - lastReadableUtc > TrustedTickGap)
            {
                lost = true;
            }
            else if (!seen.Paused && !lastPaused)
            {
                counted = (byte)(seen.Tick - before);
            }
        }

        lastTick = seen.Tick;
        lastPaused = seen.Paused;
        lastReadableUtc = nowUtc;
        return counted;
    }

    /// <summary>Advances the schedules whose row is still current and ends the others.</summary>
    private void FollowPhases(BattleAnimationObservation seen, int ticks, TimeSpan wall, bool progressLost)
    {
        foreach (var schedule in schedules.Where(schedule => schedule.PhaseLive))
        {
            if (progressLost && !schedule.Lost)
            {
                schedule.Abandon();
                log($"Battle animation narration lost the native clock for {schedule.Description.Identity.Key} " +
                    $"(no readable scan for over {TrustedTickGap.TotalSeconds:0.##}s); its remaining cues are dropped.");
            }

            if (seen.InBattle && seen.HasRow && schedule.Episode == EpisodeOf(seen))
            {
                schedule.Elapsed += ticks;
                if (!seen.Paused)
                {
                    schedule.LiveSeconds += wall.TotalSeconds;
                }

                schedule.FollowSummon(seen.SummonEffectWaiting);
                schedule.FollowBanner(seen.BannerVisible);
                if (schedule.IsOpening && schedule.SequenceBegun && !schedule.Lost &&
                    schedule.NextCue < schedule.Description.Cues.Count)
                {
                    schedule.Abandon("the summon began before it could be told");
                    log($"Battle animation narration: {schedule.Description.Identity.Key} could not start while the " +
                        "summoner was casting; kept for a later summon.");
                }
            }
            else
            {
                schedule.PhaseLive = false;
            }
        }
    }

    /// <summary>
    /// Pauses the playing recording with the battle and resumes it with the battle. A
    /// recording that cannot pause is stopped rather than left describing a frozen scene.
    /// </summary>
    private void FollowPause(bool battlePaused, DateTime nowUtc)
    {
        if (playing is not { } current || current.Paused == battlePaused)
        {
            return;
        }

        bool done;
        try
        {
            done = current.Output is IFieldMovieNarrationPause pause && pause.SetPaused(battlePaused);
        }
        catch (Exception ex)
        {
            log($"Battle animation narration could not {(battlePaused ? "pause" : "resume")}: {ex.Message}");
            done = false;
        }

        if (done)
        {
            current.SetPaused(battlePaused, nowUtc);
            return;
        }

        SetPlaying(null);
        SafeStop(current.Output, battlePaused ? "battle paused; recording cannot pause" : "recording could not resume");
        SafeDispose(current.Output);
        log($"Battle animation cue of {current.Schedule.Description.Identity.Key} " +
            $"{(battlePaused ? "could not pause with the battle" : "could not resume")}; stopped and not counted.");
    }

    /// <summary>Opens a schedule for a row the engine has just started.</summary>
    private void OpenSchedule(BattleAnimationObservation seen)
    {
        if (!seen.InBattle || !seen.IsPlaying)
        {
            lastEpisode = null;
            return;
        }

        var episode = EpisodeOf(seen);
        if (lastEpisode == episode)
        {
            return;
        }

        lastEpisode = episode;
        if (BattleAnimationIdentities.IsSummonCast(seen) && catalog.SummonOpening is { } opening)
        {
            TryOpen(opening, episode, seen);
        }

        if (catalog.TryGet(seen, out var description))
        {
            TryOpen(description, episode, seen);
        }
    }

    private void TryOpen(BattleAnimationDescription description, Episode episode, BattleAnimationObservation seen)
    {
        var key = description.HistoryKey;
        if (history.HasHeard(key) || schedules.Exists(schedule => schedule.Description.HistoryKey == key))
        {
            return;
        }

        if (schedules.Count >= MaximumPending)
        {
            log($"Battle animation narration: {description.Identity.Key} dropped, {MaximumPending} already in flight; kept for next time.");
            return;
        }

        var schedule = new Schedule(description, episode, playthroughRevision, ticksPerCueFrame);
        if (description.Anchor == BattleAnimationAnchor.Banner && seen.BannerVisible)
        {
            // How long the banner has been up is unknown, so its cues cannot be timed.
            schedule.Abandon("its banner was already showing when the cast was first seen");
            log($"Battle animation narration: {description.Identity.Key} not timed, the banner was already " +
                "showing when the cast was first seen; kept for a later summon.");
        }

        schedules.Add(schedule);
    }

    /// <summary>
    /// Stops the summon opening when another cue is due, so the summon's own cues start on
    /// their tick. True when the voice is now free.
    /// </summary>
    private bool GiveWayToADueCue()
    {
        if (playing is not { } current || !current.Schedule.IsOpening ||
            !schedules.Exists(schedule => !schedule.IsOpening && schedule.DueCue() is not null))
        {
            return false;
        }

        SetPlaying(null);
        SafeStop(current.Output, "a summon cue is due");
        SafeDispose(current.Output);
        log($"Battle animation narration: {current.Schedule.Description.Identity.Key} stopped so the summon's own " +
            "cue starts on time; kept for a later summon.");
        return true;
    }

    private void ExpireAndSettle()
    {
        foreach (var schedule in schedules)
        {
            if (!schedule.PhaseLive && schedule.DueCue() is not null && schedule.SincePhaseEnd > GraceAfterPhase)
            {
                var dropped = schedule.DropReachedCues();
                log($"Battle animation narration: {dropped} cue(s) of {schedule.Description.Identity.Key} " +
                    $"missed the {GraceAfterPhase.TotalSeconds:0}s after its animation; kept for next time.");
            }
        }

        foreach (var finished in schedules.Where(schedule => schedule.IsFinished && playing?.Schedule != schedule).ToList())
        {
            schedules.Remove(finished);
            Settle(finished);
        }
    }

    private void StartDueCue(DateTime nowUtc)
    {
        foreach (var schedule in schedules)
        {
            if (schedule.DueCue() is not { } cue)
            {
                continue;
            }

            // The opening never talks over the screen reader, which says the banner's name.
            if (schedule.IsOpening && ScreenReaderIsSpeaking())
            {
                continue;
            }

            if (AnotherDescriptionIsPlaying())
            {
                return;
            }

            var index = schedule.NextCue++;
            if (!TryStartRecording(schedule, index, cue, nowUtc))
            {
                SpeakInstead(schedule, cue);
            }

            return;
        }
    }

    private void Settle(Schedule schedule)
    {
        var identity = schedule.Description.Identity;
        var cues = schedule.Description.Cues;
        var reached = cues.Count(schedule.Reached);
        var rate = schedule.LiveSeconds > 0.5 ? $", {schedule.Elapsed / schedule.LiveSeconds:0.0} ticks/s" : string.Empty;
        var summary = $"{schedule.Delivered} of {reached} reached cue(s) delivered, {cues.Count - reached} not reached; " +
            $"phase {schedule.FinalTick} ticks over {schedule.LiveSeconds:0.0}s unpaused{rate}, " +
            $"anchor tick {schedule.AnchorText}, {ticksPerCueFrame} native tick(s) per cue tick" +
            (schedule.Lost ? $", {schedule.DropReason ?? "native clock lost"}" : string.Empty);
        if (schedule.Lost || schedule.Delivered == 0 || schedule.Delivered < reached)
        {
            log($"Battle animation narration not heard: {identity.Key} ({summary}); kept for next time.");
        }
        else if (history.TryMarkHeard(schedule.Description.HistoryKey, schedule.PlaythroughRevision))
        {
            log($"Battle animation narration heard: {identity.NativeName} ({identity.Key}, revision " +
                $"{schedule.Description.Revision}; {summary}).");
        }
        else
        {
            log($"Battle animation narration finished after another playthrough was loaded: {identity.Key} not marked heard.");
        }
    }

    /// <summary>Settles the recording that was playing. False while it still is.</summary>
    private bool FinishPlayback(Playback current, DateTime nowUtc)
    {
        var elapsed = current.PlayingTime(nowUtc);
        bool stillPlaying;
        try
        {
            stillPlaying = current.Output.IsPlaying;
        }
        catch (Exception ex)
        {
            log($"Battle animation narration output could not be queried: {ex.Message}");
            stillPlaying = false;
        }

        var key = current.Schedule.Description.Identity.Key;
        if (stillPlaying)
        {
            var stuckAfter = current.Clip.Duration + (current.Clip.Duration / 2) + TimeSpan.FromSeconds(2);
            if (elapsed <= stuckAfter)
            {
                return false;
            }

            log($"Battle animation narration still reports playing {elapsed.TotalSeconds:0.0}s into a " +
                $"{current.Clip.Duration.TotalSeconds:0.0}s recording; stopped, {key} not marked heard.");
            SafeStop(current.Output, "overran its recording");
            SetPlaying(null);
            SafeDispose(current.Output);
            return true;
        }

        // Asked before the output is released: releasing it forgets how it ended.
        var completed = CompletedNormally(current.Output);
        SetPlaying(null);
        SafeDispose(current.Output);
        if (completed)
        {
            current.Schedule.Delivered++;
        }
        else
        {
            log($"Battle animation cue ended after {elapsed.TotalSeconds:0.0}s of " +
                $"{current.Clip.Duration.TotalSeconds:0.0}s without completing: {key} will not be marked heard.");
        }

        return true;
    }

    private bool TryStartRecording(Schedule schedule, int index, BattleAnimationCue cue, DateTime nowUtc)
    {
        if (!voice.TryGet(cue.Text, out var clip))
        {
            return false;
        }

        IFieldMovieNarrationOutput? output;
        try
        {
            output = createVoiceOutput(clip);
        }
        catch (Exception ex)
        {
            log($"Battle animation narration output could not be created ({clip.FileName}): {ex.Message}");
            return false;
        }

        if (output is null)
        {
            return false;
        }

        bool started;
        try
        {
            started = output.Start($"battle animation description ({clip.Duration.TotalSeconds:0.##}s)");
        }
        catch (Exception ex)
        {
            log($"Battle animation narration failed to start ({clip.FileName}): {ex.Message}");
            started = false;
        }

        if (!started)
        {
            SafeDispose(output);
            return false;
        }

        SetPlaying(new Playback(schedule, output, clip, nowUtc));
        var identity = schedule.Description.Identity;
        log($"Battle animation cue {index + 1}/{schedule.Description.Cues.Count} started: {identity.NativeName} " +
            $"({identity.Key}) at tick {Math.Min(schedule.Elapsed, schedule.FinalTick)}, due {cue.AtFrame}" +
            (schedule.PhaseLive ? "." : $", {schedule.SincePhaseEnd.TotalSeconds:0.0}s after its animation."));
        return true;
    }

    private void SpeakInstead(Schedule schedule, BattleAnimationCue cue)
    {
        var key = schedule.Description.Identity.Key;
        if (speakWithoutRecording is null)
        {
            log($"Battle animation cue has no recording for {key}; kept for next time.");
            return;
        }

        bool accepted;
        try
        {
            accepted = speakWithoutRecording(cue.Text);
        }
        catch (Exception ex)
        {
            log($"Battle animation cue could not be spoken ({key}): {ex.Message}");
            accepted = false;
        }

        if (accepted)
        {
            schedule.Delivered++;
            log($"Battle animation cue spoken without a recording: {key}.");
        }
        else
        {
            log($"Battle animation cue was refused by speech: {key} kept for next time.");
        }
    }

    private bool CompletedNormally(IFieldMovieNarrationOutput output)
    {
        try
        {
            return output is IFieldMovieNarrationCompletion completion && completion.CompletedNormally;
        }
        catch (Exception ex)
        {
            log($"Battle animation narration output could not report how it ended: {ex.Message}");
            return false;
        }
    }

    private void SetPlaying(Playback? value)
    {
        playing = value;
        isPlaying = value is not null;
    }

    private bool ScreenReaderIsSpeaking()
    {
        try
        {
            return speechIsPlaying?.Invoke() == true;
        }
        catch (Exception ex)
        {
            log($"Battle animation narration could not ask whether the screen reader is speaking: {ex.Message}");
            return true;
        }
    }

    private bool AnotherDescriptionIsPlaying()
    {
        try
        {
            return anotherDescriptionIsPlaying();
        }
        catch (Exception ex)
        {
            log($"Battle animation narration could not check the voice device: {ex.Message}");
            return true;
        }
    }

    private void SafeStop(IFieldMovieNarrationOutput output, string reason)
    {
        try
        {
            output.Stop(reason);
        }
        catch (Exception ex)
        {
            log($"Battle animation narration could not be stopped cleanly: {ex.Message}");
        }
    }

    private void SafeDispose(IFieldMovieNarrationOutput output)
    {
        try
        {
            output.Dispose();
        }
        catch (Exception ex)
        {
            log($"Battle animation narration output could not be released: {ex.Message}");
        }
    }

    private static Episode EpisodeOf(BattleAnimationObservation seen) =>
        new(seen.EventIndex, seen.Attacker, seen.Command, seen.Effect, seen.Action);

    private readonly record struct Episode(byte EventIndex, byte Attacker, byte Command, byte Effect, ushort Action);

    /// <summary>One started animation and how far its cues have got.</summary>
    private sealed class Schedule(
        BattleAnimationDescription description,
        Episode episode,
        long playthroughRevision,
        int ticksPerCueFrame)
    {
        private int finalTick = -1;
        private bool phaseLive = true;
        private bool summonWaitingSeen;

        /// <summary>
        /// The tick the cues count from: the row start, or once seen, the summon sequence's
        /// start or the banner's appearance. Null until then.
        /// </summary>
        public int? AnchorTick { get; private set; } =
            description.Anchor == BattleAnimationAnchor.RowStart ? 0 : null;

        /// <summary>
        /// Nothing more of it is told and it is not heard: the native clock was lost, or
        /// <see cref="DropReason"/>.
        /// </summary>
        public bool Lost { get; private set; }

        /// <summary>Why it was abandoned, when not for a lost clock.</summary>
        public string? DropReason { get; private set; }

        /// <summary>The summon dispatcher has been seen to begin the summon's own sequence.</summary>
        public bool SequenceBegun { get; private set; }

        /// <summary>The casting shared by every summon, anchored on the banner.</summary>
        public bool IsOpening => Description.Anchor == BattleAnimationAnchor.Banner;

        public BattleAnimationDescription Description { get; } = description;

        public Episode Episode { get; } = episode;

        public long PlaythroughRevision { get; } = playthroughRevision;

        public int Elapsed { get; set; }

        public double LiveSeconds { get; set; }

        public int NextCue { get; set; }

        public int Delivered { get; set; }

        public TimeSpan SincePhaseEnd { get; set; }

        /// <summary>The row is still the engine's current row.</summary>
        public bool PhaseLive
        {
            get => phaseLive;
            set
            {
                if (phaseLive && !value)
                {
                    finalTick = Elapsed;
                    SincePhaseEnd = TimeSpan.Zero;
                }

                phaseLive = value;
            }
        }

        /// <summary>The tick the phase ended on; while it lasts, as far as it has got.</summary>
        public int FinalTick => phaseLive ? Elapsed : finalTick;

        /// <summary>Whether the animation got as far as this cue's tick.</summary>
        public bool Reached(BattleAnimationCue cue) =>
            AnchorTick is { } anchor && (long)cue.AtFrame * ticksPerCueFrame <= FinalTick - anchor;

        /// <summary>The next cue if its tick has been reached.</summary>
        public BattleAnimationCue? DueCue() =>
            !Lost && NextCue < Description.Cues.Count && Reached(Description.Cues[NextCue])
                ? Description.Cues[NextCue]
                : null;

        /// <summary>
        /// Watches the summon dispatcher: the tick it is first seen gone after having been
        /// registered is the summon sequence's start.
        /// </summary>
        public void FollowSummon(bool dispatcherWaiting)
        {
            if (SequenceBegun)
            {
                return;
            }

            if (dispatcherWaiting)
            {
                summonWaitingSeen = true;
            }
            else if (summonWaitingSeen)
            {
                SequenceBegun = true;
                if (Description.Anchor == BattleAnimationAnchor.SummonSequence)
                {
                    AnchorTick = Elapsed;
                }
            }
        }

        /// <summary>The tick the banner is first seen on screen anchors the opening.</summary>
        public void FollowBanner(bool bannerVisible)
        {
            if (bannerVisible && IsOpening && AnchorTick is null && !Lost)
            {
                AnchorTick = Elapsed;
            }
        }

        /// <summary>The timeline can no longer be trusted: nothing more is told.</summary>
        public void Abandon(string? reason = null)
        {
            Lost = true;
            DropReason = reason;
            NextCue = Description.Cues.Count;
        }

        /// <summary>Gives up on every reached cue not yet told; returns how many.</summary>
        public int DropReachedCues()
        {
            var dropped = 0;
            while (DueCue() is not null)
            {
                NextCue++;
                dropped++;
            }

            return dropped;
        }

        public bool IsFinished => !phaseLive && DueCue() is null;

        public string AnchorText => AnchorTick is { } anchor ? $"{anchor}" : "not reached";
    }

    private sealed class Playback(
        Schedule schedule,
        IFieldMovieNarrationOutput output,
        CutsceneVoiceClip clip,
        DateTime startedAtUtc)
    {
        private DateTime pausedSince;
        private TimeSpan pausedTotal;

        public Schedule Schedule { get; } = schedule;

        public IFieldMovieNarrationOutput Output { get; } = output;

        public CutsceneVoiceClip Clip { get; } = clip;

        public bool Paused { get; private set; }

        public void SetPaused(bool paused, DateTime nowUtc)
        {
            if (paused)
            {
                pausedSince = nowUtc;
            }
            else if (nowUtc > pausedSince)
            {
                pausedTotal += nowUtc - pausedSince;
            }

            Paused = paused;
        }

        /// <summary>How long the recording has actually been playing, pauses excluded.</summary>
        public TimeSpan PlayingTime(DateTime nowUtc) =>
            nowUtc - startedAtUtc - pausedTotal - (Paused && nowUtc > pausedSince ? nowUtc - pausedSince : TimeSpan.Zero);
    }
}
