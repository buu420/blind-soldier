using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The three seams root found by reading the runtime rather than the tracker: a
/// playing recording surviving the loss of the evidence that justified it, a field
/// change killing a film that is still on screen, and an old anchor paragraph being
/// read out on top of a schedule that is already describing the same film.
///
/// <para>Each of these is only visible where two pieces meet, which is why the
/// isolated tracker tests missed them.</para>
/// </summary>
internal static class FilmNarrationSeamTests
{
    private const int FieldModule = 1;
    private const int Bugin1c = 543;
    private const int BoogdemoFilm = 42;
    private static readonly DateTime Start = new(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);

    private static readonly List<MovieNarrationCue> Cues =
    [
        new(0d, 3d, "the first thing"),
        new(20d, 23d, "the later thing"),
    ];

    public static void Run()
    {
        LosingTheEvidenceStopsAPlayingRecording();
        APreparedFilmIsNotPlayback();
        AScheduledFilmSilencesItsOwnAnchorParagraph();
        AnUnanchoredFilmWithNoAudioIsStillDescribed();
    }

    /// <summary>
    /// Seam one. The deferred schedule was invalidated on every loss of evidence, but
    /// an <em>actively playing</em> recording was not: the stop policy only looked at
    /// module, field, the active flag and the number, so an unreadable command byte,
    /// an unreadable frame or the skip gate coming on all left the audio running over
    /// a film nobody could still identify.
    /// </summary>
    private static void LosingTheEvidenceStopsAPlayingRecording()
    {
        var losses = new (string What, Func<FieldMovieNarrationSample, FieldMovieNarrationSample> Break)[]
        {
            ("the command byte stopped reading",
                sample => sample with { MovieCommand = FieldMovieNarrationSample.CommandUnknown }),
            ("another command took the argument word",
                sample => sample with { MovieCommand = 2 }),
            ("the engine started skipping films",
                sample => sample with { MoviesSkipped = 1 }),
            ("the frame stopped reading",
                sample => sample with { MovieFrame = FieldMovieNarrationSample.FrameUnknown }),
        };

        foreach (var (what, breakIt) in losses)
        {
            var output = new CountingOutput();
            var tracker = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldModule, _ => Cues);
            tracker.Observe(Playing(0d), Start);
            Equal(true, tracker.IsPlaying, $"{what}: the control - the recording is playing first");

            tracker.Observe(breakIt(Playing(2d)), Start.AddSeconds(2));
            Equal(false, tracker.IsPlaying, $"{what}: the recording must stop");
            Equal(1, output.Stops, $"{what}: and stop exactly once");

            // And a later good sample must not buy a fresh start from zero: the film
            // is twenty seconds in by then.
            tracker.Observe(Playing(20d), Start.AddSeconds(20));
            Equal(1, output.Starts, $"{what}: a recovered read must not restart the recording");
        }

        // The control that keeps this honest: a film that keeps all its evidence
        // keeps playing.
        var steadyOutput = new CountingOutput();
        var steady = new FieldMovieNarrationTracker(_ => steadyOutput, _ => { }, FieldModule, _ => Cues);
        steady.Observe(Playing(0d), Start);
        steady.Observe(Playing(2d), Start.AddSeconds(2));
        Equal(true, steady.IsPlaying, "an unchanged film keeps playing");
        Equal(0, steadyOutput.Stops, "and is not stopped");
    }

    /// <summary>
    /// Seam one, second half. Command 3 identifies a film the engine is preparing;
    /// only command 4 is the film actually on screen. Starting a recording off a
    /// stale active flag while the engine is opening the next film would describe a
    /// film the player is not being shown yet.
    /// </summary>
    private static void APreparedFilmIsNotPlayback()
    {
        var output = new CountingOutput();
        var tracker = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldModule, _ => Cues);

        var preparing = Playing(0d) with { MovieCommand = FieldMovieNarrationSample.CommandOpenMovie };
        tracker.Observe(preparing, Start);
        Equal(0, output.Starts, "a film being prepared must not start a recording");

        // The identity is still knowable while it is being prepared - that is what
        // command 3 is for - so the film can be named without being described.
        Equal("boogdemo.avi", FieldMovieNarrationTracker.ResolveFilmAtSample(preparing),
            "a prepared film still names itself");

        tracker.Observe(Playing(0d), Start.AddMilliseconds(60));
        Equal(1, output.Starts, "and playing it does start the recording");

        // Preparation also ends an existing schedule. A stale active flag and a
        // readable frame do not permit deferred speech while the engine opens a file.
        tracker.Observe(Playing(1d), Start.AddSeconds(1), dialogueIsOnScreen: true);
        tracker.Observe(preparing, Start.AddSeconds(2));
        Equal(false, tracker.TryDeliverDeferredCue(false, _ => true, out _),
            "preparing a film must not deliver a cue from the previous playback");
    }

    /// <summary>
    /// Seam three. Once a film is being described - by its recording or by its cue
    /// schedule - the old address-keyed paragraph for the same film must not be read
    /// out on top of it. Before this, a paragraph queued behind dialogue reached
    /// <c>Begin</c> after the audio had given way, found no live opportunity, and was
    /// spoken in full alongside the scheduled cues.
    /// </summary>
    private static void AScheduledFilmSilencesItsOwnAnchorParagraph()
    {
        var anchor = FieldMovieNarrationPolicy.All.First(track => track.MovieNumber == BoogdemoFilm);
        var output = new CountingOutput();
        var tracker = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldModule, _ => Cues);

        // The film starts and is described by its recording.
        tracker.Observe(AtField(anchor.FieldId, 0d), Start);
        Equal(1, output.Starts, "the film is described by its recording");

        // The game starts talking; the recording gives way to the schedule.
        tracker.Observe(AtField(anchor.FieldId, 2d), Start.AddSeconds(2), dialogueIsOnScreen: true);
        Equal(false, tracker.IsPlaying, "the recording gives way");

        // The dialogue ends and the anchor's own paragraph, queued all this time,
        // finally reaches the tracker. It describes a film that is already being
        // described.
        var result = tracker.Begin(
            anchor.FieldId, anchor.EntityId, anchor.ScriptId, anchor.ByteIndex,
            AtField(anchor.FieldId, 4d), Start.AddSeconds(4));
        Equal(FieldMovieNarrationStartResult.AlreadyDescribed, result,
            "the anchor paragraph must be dropped, not spoken over the schedule");
        Equal(1, output.Starts, "and nothing may be restarted");

        // The delivery step spends such a cue without speaking it and without
        // reserving the dialogue window for words nobody said.
        var queue = new FieldCutsceneDescriptionDeliveryQueue();
        var delivery = new FieldCutsceneDescriptionDelivery(queue);
        var cue = new FieldCutsceneDescriptionCue(
            anchor.FieldId, anchor.EntityId, anchor.ScriptId, anchor.ByteIndex,
            "the whole paragraph", FieldOpcodeAddressResolver.OpcodeMovieIndex);
        queue.Enqueue(cue);

        var spoken = new List<string>();
        var reserved = new List<FieldCutsceneDescriptionCue>();
        Equal(FieldCutsceneDeliveryOutcome.AlreadyDescribed,
            delivery.Deliver(anchor.FieldId,
                _ => FieldMovieNarrationStartResult.AlreadyDescribed,
                text => { spoken.Add(text); return true; },
                reserved.Add,
                out _),
            "the delivery step reports it as already described");
        Equal(0, spoken.Count, "the paragraph is not spoken");
        Equal(0, reserved.Count, "and no dialogue window is reserved for it");
        Equal(0, queue.Count, "but the cue is spent, so it cannot be retried forever");

        // Ownership survives the schedule running out: a paragraph arriving after the
        // last cue must still not be read.
        var exhausted = new FieldMovieNarrationTracker(_ => new CountingOutput(), _ => { }, FieldModule, _ => Cues);
        exhausted.Observe(AtField(anchor.FieldId, 0d), Start);
        exhausted.Observe(AtField(anchor.FieldId, 1d), Start.AddSeconds(1), dialogueIsOnScreen: true);
        while (exhausted.TryDeliverDeferredCue(false, _ => true, out _)) { }
        exhausted.Observe(AtField(anchor.FieldId, 25d), Start.AddSeconds(25));
        while (exhausted.TryDeliverDeferredCue(false, _ => true, out _)) { }
        Equal(FieldMovieNarrationStartResult.AlreadyDescribed,
            exhausted.Begin(anchor.FieldId, anchor.EntityId, anchor.ScriptId, anchor.ByteIndex,
                AtField(anchor.FieldId, 26d), Start.AddSeconds(26)),
            "a finished schedule still owns its film's paragraph");
    }

    /// <summary>
    /// The other half of seam three: a film with no anchor at all whose recording
    /// cannot be opened must still be described from its reviewed cues, rather than
    /// settling quietly and saying nothing for the whole film.
    /// </summary>
    private static void AnUnanchoredFilmWithNoAudioIsStillDescribed()
    {
        var tracker = new FieldMovieNarrationTracker(
            _ => null, _ => { }, FieldModule, _ => Cues);
        tracker.Observe(Playing(0d), Start);
        Equal(true, tracker.TryDeliverDeferredCue(false, _ => true, out var first),
            "a film with no installed recording is still described from its cues");
        Equal("the first thing", first, "starting with the cue its position has reached");

        tracker.Observe(Playing(21d), Start.AddSeconds(21));
        Equal(true, tracker.TryDeliverDeferredCue(false, _ => true, out var later),
            "and keeps being described as it goes on");
        Equal("the later thing", later, "with each cue at its own moment");

        // Genuine fallback is preserved: a film with no cue schedule and no recording
        // leaves the ordinary paragraph in charge rather than owning it silently.
        var anchor = FieldMovieNarrationPolicy.All.First(track => track.MovieNumber == BoogdemoFilm);
        var bare = new FieldMovieNarrationTracker(_ => null, _ => { }, FieldModule, _ => []);
        bare.Observe(AtField(anchor.FieldId, 0d), Start);
        Equal(false, bare.TryDeliverDeferredCue(false, _ => true, out _),
            "a film with neither a recording nor cues has nothing scheduled");
        Equal(FieldMovieNarrationStartResult.Unavailable,
            bare.Begin(anchor.FieldId, anchor.EntityId, anchor.ScriptId, anchor.ByteIndex,
                AtField(anchor.FieldId, 0.2d), Start.AddMilliseconds(200)),
            "so the ordinary paragraph is still spoken - the genuine fallback survives");
    }

    private static FieldMovieNarrationSample Playing(double seconds) => AtField(Bugin1c, seconds);

    private static FieldMovieNarrationSample AtField(int fieldId, double seconds) =>
        new(true, BoogdemoFilm, FieldModule, fieldId,
            Disc: 1,
            MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
            MovieFrame: (int)Math.Round(seconds * FieldMovieNarrationSample.FramesPerSecond));

    private sealed class CountingOutput : IFieldMovieNarrationOutput
    {
        private bool playing;

        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public bool IsPlaying => playing;

        public bool Start(string reason)
        {
            Starts++;
            playing = true;
            return true;
        }

        public bool Stop(string reason)
        {
            if (playing) { Stops++; }
            playing = false;
            return true;
        }

        public void Dispose() => playing = false;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Film narration seam: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
