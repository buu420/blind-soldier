using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Exercises the actual x64 runtime path for independent film narration: the
/// coordinator that the research session drives, with a real address space stub,
/// rather than the shared policy in isolation.
/// </summary>
internal static class Steam2026FieldMovieNarrationAdapterTests
{
    private const int Gldst = 496;
    private const int ArrivalByte = 190;
    private const int DockingByte = 201;
    private const int ArrivalFilm = 40;
    private const int DockingFilm = 4;

    /// <summary>boogdemo, a reviewed catalog film the global path can describe.</summary>
    private const int CatalogFilm = 42;

    private static readonly DateTime Timestamp = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly FieldCutsceneDescriptionCue ArrivalCue =
        FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .Single(cue => cue.FieldId == Gldst && cue.ByteIndex == ArrivalByte);

    private static readonly FieldCutsceneDescriptionCue DockingCue =
        FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .Single(cue => cue.FieldId == Gldst && cue.ByteIndex == DockingByte);

    public static void Run()
    {
        NarrationSuppressesTheParagraphOnlyWhenItActuallyStarts();
        ADockingFilmIngressExpiresTheArrivalOpportunity();
        UnavailableNarrationFallsBackToTheSpokenParagraph();
        NativeFilmLifecycleStopsTheTrack();
        ForegroundLossAndResetStopTheTrack();
        RejectedSpeechKeepsTheOldestCueAtTheHead();
        AnInactiveIngressHoldsTheCueUntilTheFilmActuallyStarts();
        TheNativeF9RepeatCannotRestartTheDescription();
        TheStateCapturedBeforeTheOriginalStartsTheVeryFirstFilm();
        ADelayedDrainUsesTheRealCaptureTimeRatherThanTheDrainTime();
        AFieldChangeDoesNotKillAFilmThatIsStillOnScreen();
    }

    /// <summary>
    /// Root's item 6, in the wrapper rather than in the policy. The coordinator
    /// resets its field state whenever a snapshot arrives for a new field - including
    /// the very first one, because it starts at -1 - and the reset stopped the film
    /// narration outright. In the production order the native tick runs before the
    /// first snapshot drains, so a film started globally was killed by its own first
    /// snapshot; and a film that legitimately runs across a field change was killed
    /// by the change.
    /// </summary>
    private static void AFieldChangeDoesNotKillAFilmThatIsStillOnScreen()
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var output = new FakeOutput();
        var narration = new FieldMovieNarrationTracker(
            _ => output, _ => { }, FieldPositionReader.FieldModule);
        var coordinator = Create(memory, narration);

        // The native tick sees the film first, with no snapshot yet: this is the
        // global path, and there is no anchor involved at all.
        // Film 42 is boogdemo, one of the reviewed catalog films the global path
        // describes. gold1 deliberately is not in that catalog - it keeps its own
        // anchor - so it cannot exercise this path.
        memory.SetFilm(CatalogFilm, active: true);
        memory.MovieFrame = 0;
        coordinator.ObserveNativeFilm(Timestamp);
        Equal(true, coordinator.IsNativeFilmNarrationPlaying,
            "the film starts from the native tick alone");
        Equal(1, output.Starts, "exactly one start");

        // Now its first snapshot drains, which is the first time the coordinator has
        // ever seen a field. That must not stop the film it just started.
        coordinator.Observe(Snapshot(ArrivalByte));
        Equal(true, coordinator.IsNativeFilmNarrationPlaying,
            "the first drained snapshot must not stop the film");
        Equal(0, output.Stops, "nothing may have been stopped");

        // The story moves to another field while the same film keeps running - the
        // Highwind case. The pending field-bound cues are cleared, the film is not.
        memory.FieldId = 497;
        memory.MovieFrame = 90;
        coordinator.Observe(new Steam2026FieldCutsceneIngressSnapshot(
            2,
            Timestamp.AddSeconds(6),
            new FieldScriptContext(497, 0, 0, 638, FieldOpcodeAddressResolver.OpcodeRequestSwIndex)));
        Equal(true, coordinator.IsNativeFilmNarrationPlaying,
            "a field change under a film that is still on screen must not stop it");
        Equal(1, output.Starts, "and must not restart it either");

        // A real ending still stops it: the film goes away.
        memory.SetFilm(0, active: false);
        coordinator.ObserveNativeFilm(Timestamp.AddSeconds(10));
        Equal(false, coordinator.IsNativeFilmNarrationPlaying, "the film ending stops the track");

        // And so does leaving the field module altogether.
        var exiting = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var exitingOutput = new FakeOutput();
        var exitingCoordinator = Create(
            exiting,
            new FieldMovieNarrationTracker(_ => exitingOutput, _ => { }, FieldPositionReader.FieldModule));
        exiting.SetFilm(CatalogFilm, active: true);
        exitingCoordinator.ObserveNativeFilm(Timestamp);
        Equal(true, exitingCoordinator.IsNativeFilmNarrationPlaying, "the control - it is playing");
        exiting.Module = 3;
        exitingCoordinator.ObserveNativeFilm(Timestamp.AddSeconds(2));
        Equal(false, exitingCoordinator.IsNativeFilmNarrationPlaying,
            "leaving the field module stops the track");

        // An explicit reset stops it too.
        var resetting = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var resetOutput = new FakeOutput();
        var resetCoordinator = Create(
            resetting,
            new FieldMovieNarrationTracker(_ => resetOutput, _ => { }, FieldPositionReader.FieldModule));
        resetting.SetFilm(CatalogFilm, active: true);
        resetCoordinator.ObserveNativeFilm(Timestamp);
        Equal(true, resetCoordinator.IsNativeFilmNarrationPlaying, "the control - it is playing");
        resetCoordinator.Reset();
        Equal(false, resetCoordinator.IsNativeFilmNarrationPlaying, "an explicit reset stops the track");
    }

    /// <summary>
    /// The defect this covers is at the callback seam, not in the tracker: the
    /// translated original turns the handler's fresh 0 into a 4 on the call itself,
    /// so a coordinator that re-reads the state when it drains sees a repeat and
    /// rejects the very first described film. Passing a hand-made fresh sample
    /// straight to the tracker cannot catch that.
    /// </summary>
    private static void TheStateCapturedBeforeTheOriginalStartsTheVeryFirstFilm()
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var output = new FakeOutput();
        var narration = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldPositionReader.FieldModule);
        var coordinator = Create(memory, narration);

        // Exactly the production ordering: the callback captured state 0 with the
        // film not yet active, then the original ran and left state 4 behind, and
        // only then does the worker drain the snapshot.
        memory.SetFilm(ArrivalFilm, active: false);
        memory.HandlerState = FieldMovieNarrationPolicy.MovieHandlerStateInProgress;
        memory.HandlerPhase = FieldMovieNarrationPolicy.MovieHandlerPhaseYielding;
        Equal(true,
            coordinator.Observe(Snapshot(
                ArrivalByte, 1, Timestamp,
                handlerState: FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry,
                handlerPhase: 0,
                movieNumber: ArrivalFilm,
                movieActive: false)),
            "the arrival cue is queued from the pre-original capture");

        memory.SetFilm(ArrivalFilm, active: true);
        var spoken = new List<string>();
        Equal(true, coordinator.TrySpeakPending(true, () => false,
                text => { spoken.Add(text); return true; }, Timestamp.AddMilliseconds(120), out _),
            "the very first described film starts its track");
        Equal(1, output.Starts, "exactly one track start");
        Equal(0, spoken.Count, "and the paragraph is not spoken over it");

        // Every yielded repeat carries state 4 and must not reopen anything.
        for (var frame = 1; frame <= 20; frame++)
        {
            coordinator.Observe(Snapshot(
                ArrivalByte, 1 + frame, Timestamp.AddSeconds(frame),
                handlerState: FieldMovieNarrationPolicy.MovieHandlerStateInProgress,
                handlerPhase: FieldMovieNarrationPolicy.MovieHandlerPhaseYielding,
                movieNumber: ArrivalFilm,
                movieActive: true));
        }

        Equal(1, output.Starts, "the handler's own repeats must not start a second track");

        // The completion pass, then a genuinely fresh entry for the next film.
        coordinator.Observe(Snapshot(
            ArrivalByte, 40, Timestamp.AddSeconds(45),
            handlerState: FieldMovieNarrationPolicy.MovieHandlerStateInProgress,
            handlerPhase: FieldMovieNarrationPolicy.MovieHandlerPhaseCompleting,
            movieNumber: ArrivalFilm,
            movieActive: true));
        output.Finish();
        memory.SetFilm(ArrivalFilm, active: false);

        // The player leaves the field and comes back for another ride. The catalog
        // deliberately speaks a story cue once per visit, so the round trip is what
        // makes the second ride's cue available again.
        memory.FieldId = 497;
        coordinator.Observe(new Steam2026FieldCutsceneIngressSnapshot(
            50,
            Timestamp.AddSeconds(60),
            new FieldScriptContext(497, 0, 0, 638, FieldOpcodeAddressResolver.OpcodeRequestSwIndex)));
        memory.FieldId = Gldst;

        Equal(true,
            coordinator.Observe(Snapshot(
                ArrivalByte, 41, Timestamp.AddSeconds(90),
                handlerState: FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry,
                handlerPhase: 0,
                movieNumber: ArrivalFilm,
                movieActive: false)),
            "a later genuine ride is queued again");
        memory.SetFilm(ArrivalFilm, active: true);
        Equal(true, coordinator.TrySpeakPending(true, () => false,
                text => { spoken.Add(text); return true; }, Timestamp.AddSeconds(90.1), out _),
            "and is described");
        Equal(2, output.Starts, "each genuine film starts exactly one track");

        // An unreadable context is unknown, not a fresh entry: the snapshot simply
        // carries no sample and the tracker falls back to its own bookkeeping.
        var unknownMemory = new FakeAddressSpace
        {
            Module = 1,
            FieldId = Gldst,
            ScriptContextReadable = false
        };
        var unknownOutput = new FakeOutput();
        var unknownCoordinator = Create(
            unknownMemory,
            new FieldMovieNarrationTracker(_ => unknownOutput, _ => { }, FieldPositionReader.FieldModule));
        unknownMemory.SetFilm(ArrivalFilm, active: false);
        Equal(true, unknownCoordinator.Observe(Snapshot(ArrivalByte)),
            "a snapshot with no captured sample still queues the cue");
        unknownMemory.SetFilm(ArrivalFilm, active: true);
        Equal(true, unknownCoordinator.TrySpeakPending(true, () => false, _ => true,
                Timestamp.AddMilliseconds(120), out _),
            "and the fallback bookkeeping still describes it");
        Equal(1, unknownOutput.Starts, "with exactly one start");
    }

    /// <summary>
    /// The start window is measured from the instant the opcode ran, which the
    /// snapshot carries. A queue that backs up must not buy the description a fresh
    /// two seconds and start it part-way through the film.
    /// </summary>
    private static void ADelayedDrainUsesTheRealCaptureTimeRatherThanTheDrainTime()
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var output = new FakeOutput();
        var narration = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldPositionReader.FieldModule);
        var coordinator = Create(memory, narration);

        memory.SetFilm(ArrivalFilm, active: true);
        coordinator.Observe(Snapshot(
            ArrivalByte, 1, Timestamp,
            handlerState: FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry,
            handlerPhase: 0,
            movieNumber: ArrivalFilm,
            movieActive: true));

        var spoken = new List<string>();
        Equal(true, coordinator.TrySpeakPending(true, () => false,
                text => { spoken.Add(text); return true; }, Timestamp.AddSeconds(20),
                out _),
            "the cue is still delivered rather than lost");
        Equal(0, output.Starts,
            "but no track starts twenty seconds into the film: the window runs from the capture");
        Equal(1, spoken.Count, "the ordinary paragraph is spoken instead");

        // The same capture drained promptly does start the track, which is what
        // shows the refusal above came from the elapsed time and not from something
        // else about the snapshot.
        var promptMemory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var promptOutput = new FakeOutput();
        var promptCoordinator = Create(
            promptMemory,
            new FieldMovieNarrationTracker(_ => promptOutput, _ => { }, FieldPositionReader.FieldModule));
        promptMemory.SetFilm(ArrivalFilm, active: true);
        promptCoordinator.Observe(Snapshot(
            ArrivalByte, 1, Timestamp,
            handlerState: FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry,
            handlerPhase: 0,
            movieNumber: ArrivalFilm,
            movieActive: true));
        Equal(true, promptCoordinator.TrySpeakPending(true, () => false, _ => true,
                Timestamp.AddMilliseconds(100), out _),
            "a prompt drain is delivered");
        Equal(1, promptOutput.Starts, "and starts the track");
    }

    private static void AnInactiveIngressHoldsTheCueUntilTheFilmActuallyStarts()
    {
        // The opcode hook can run a frame ahead of the engine's own active flag. A
        // coordinator that speaks the paragraph then commits the cue, and the
        // independent track can never start because nothing is left to start it.
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var output = new FakeOutput();
        var narration = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldPositionReader.FieldModule);
        var coordinator = Create(memory, narration);

        memory.SetFilm(ArrivalFilm, active: false);
        Equal(true, coordinator.Observe(Snapshot(ArrivalByte)), "the arrival cue is queued before the film starts");

        var spoken = new List<string>();
        // A speaker that would gladly accept must not be offered the text yet.
        Equal(false, coordinator.TrySpeakPending(true, () => false, text => { spoken.Add(text); return true; },
                Timestamp.AddMilliseconds(50), out _),
            "delivery is held while the native film has not started");
        Equal(0, spoken.Count, "no early paragraph may be spoken");
        Equal(0, output.Starts, "no track may start before the film");
        Equal(true, coordinator.HasPendingNarration(Gldst), "the cue must still be queued");

        memory.SetFilm(ArrivalFilm, active: true);
        Equal(true, coordinator.TrySpeakPending(true, () => false, text => { spoken.Add(text); return true; },
                Timestamp.AddMilliseconds(120), out var delivered),
            "the held cue is taken by the track once the film starts");
        Equal(0, spoken.Count, "the paragraph is still never spoken");
        Equal(1, output.Starts, "exactly one track start");
        Equal(ArrivalByte, delivered.ByteIndex, "the held cue is the one delivered");

        // The deadline: a film that never starts must fall back to the paragraph.
        var stalledMemory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var stalledOutput = new FakeOutput();
        var stalled = new FieldMovieNarrationTracker(_ => stalledOutput, _ => { }, FieldPositionReader.FieldModule);
        var stalledCoordinator = Create(stalledMemory, stalled);
        stalledMemory.SetFilm(ArrivalFilm, active: false);
        stalledCoordinator.Observe(Snapshot(ArrivalByte));
        var stalledSpoken = new List<string>();
        Equal(false, stalledCoordinator.TrySpeakPending(true, () => false,
                text => { stalledSpoken.Add(text); return true; }, Timestamp.AddMilliseconds(50), out _),
            "the wait holds inside its deadline");
        Equal(true, stalledCoordinator.TrySpeakPending(true, () => false,
                text => { stalledSpoken.Add(text); return true; },
                Timestamp.Add(FieldMovieNarrationPolicy.PreActivationWindow).AddSeconds(1), out _),
            "past the deadline the description is spoken instead");
        Equal(1, stalledSpoken.Count, "the paragraph is spoken exactly once");
        Equal(0, stalledOutput.Starts, "a film that never started must not start a track");
    }

    private static void TheNativeF9RepeatCannotRestartTheDescription()
    {
        // The native F9 handler re-delivers the same opcode on every movie frame, so
        // the coordinator sees repeated ingress throughout the film.
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var output = new FakeOutput();
        var narration = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldPositionReader.FieldModule);
        var coordinator = Create(memory, narration);
        memory.SetFilm(ArrivalFilm, active: true);

        for (var frame = 1; frame <= 5; frame++)
        {
            coordinator.Observe(Snapshot(ArrivalByte, sequence: frame));
        }

        var spoken = new List<string>();
        Equal(true, coordinator.TrySpeakPending(true, () => false, text => { spoken.Add(text); return true; },
                Timestamp.AddMilliseconds(100), out _), "the first delivery starts the track");
        Equal(1, output.Starts, "exactly one track start");

        // More repeats during playback, then a delivery twenty seconds in.
        for (var frame = 6; frame <= 10; frame++)
        {
            coordinator.Observe(Snapshot(ArrivalByte, sequence: frame));
        }

        coordinator.ObserveNativeFilm(Timestamp.AddSeconds(20));
        Equal(1, output.Starts, "native repeats must not restart the description mid-film");
        Equal(0, spoken.Count, "native repeats must not produce duplicate paragraphs");
    }

    private static void NarrationSuppressesTheParagraphOnlyWhenItActuallyStarts()
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var output = new FakeOutput();
        var narration = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldPositionReader.FieldModule);
        var coordinator = Create(memory, narration);

        memory.SetFilm(ArrivalFilm, active: true);
        Equal(true, coordinator.Observe(Snapshot(ArrivalByte)), "the arrival cue is queued");

        var spoke = 0;
        Equal(true, coordinator.TrySpeakPending(true, () => false, _ => { spoke++; return true; },
                Timestamp.AddMilliseconds(100), out var delivered),
            "the arrival cue is delivered");
        Equal(0, spoke, "the ordinary paragraph must not be spoken once independent audio starts");
        Equal(1, output.Starts, "the independent track must start exactly once");
        Equal(ArrivalByte, delivered.ByteIndex, "the delivered cue is the arrival cue");
        Equal(true, coordinator.IsNativeFilmNarrationPlaying, "the coordinator reports the track as playing");

        // The delivered cue is gone from the queue.
        Equal(false, coordinator.TrySpeakPending(true, () => false, _ => { spoke++; return true; },
                Timestamp.AddMilliseconds(200), out _),
            "a delivered cue is not offered twice");
    }

    private static void ADockingFilmIngressExpiresTheArrivalOpportunity()
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var output = new FakeOutput();
        var narration = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldPositionReader.FieldModule);
        var coordinator = Create(memory, narration);

        memory.SetFilm(ArrivalFilm, active: true);
        Equal(true, coordinator.Observe(Snapshot(ArrivalByte)), "the arrival cue is queued");

        // Delivery is held behind dialogue while the docking film takes over.
        memory.SetFilm(DockingFilm, active: true);
        Equal(true, coordinator.Observe(Snapshot(DockingByte, sequence: 2)), "the docking cue is queued");

        var spoken = new List<string>();
        Equal(true, coordinator.TrySpeakPending(true, () => false, text => { spoken.Add(text); return true; },
                Timestamp.AddMilliseconds(300), out var delivered),
            "the arrival cue is still delivered");
        Equal(0, output.Starts, "no independent track may start over the docking film");
        Equal(ArrivalByte, delivered.ByteIndex, "the oldest cue is still delivered first");
        Equal(1, spoken.Count, "the arrival description falls back to the spoken paragraph");
        Equal(ArrivalCue.Text, spoken[0], "the spoken paragraph is the arrival description");
    }

    private static void UnavailableNarrationFallsBackToTheSpokenParagraph()
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        // A factory that returns null models a missing asset or a disabled track; a
        // factory that throws models a device that cannot be opened at all.
        foreach (var factory in new Func<FieldMovieNarrationTrack, IFieldMovieNarrationOutput?>[]
                 {
                     _ => null,
                     _ => throw new InvalidOperationException("no audio device")
                 })
        {
            var narration = new FieldMovieNarrationTracker(factory, _ => { }, FieldPositionReader.FieldModule);
            var coordinator = Create(memory, narration);
            memory.SetFilm(ArrivalFilm, active: true);
            Equal(true, coordinator.Observe(Snapshot(ArrivalByte)), "the arrival cue is queued");

            var spoken = new List<string>();
            Equal(true, coordinator.TrySpeakPending(true, () => false, text => { spoken.Add(text); return true; },
                    Timestamp.AddMilliseconds(100), out _),
                "an unavailable narration device still delivers the description");
            Equal(1, spoken.Count, "the ordinary paragraph is the fallback");
            Equal(ArrivalCue.Text, spoken[0], "the fallback text is the reviewed description");
        }
    }

    private static void NativeFilmLifecycleStopsTheTrack()
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var output = new FakeOutput();
        var narration = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldPositionReader.FieldModule);
        var coordinator = Create(memory, narration);

        memory.SetFilm(ArrivalFilm, active: true);
        coordinator.Observe(Snapshot(ArrivalByte));
        Equal(true, coordinator.TrySpeakPending(true, () => false, _ => true,
                Timestamp.AddMilliseconds(100), out _), "the track starts");

        coordinator.ObserveNativeFilm(Timestamp.AddSeconds(5));
        Equal(true, coordinator.IsNativeFilmNarrationPlaying, "an unchanged film keeps the track running");

        memory.SetFilm(ArrivalFilm, active: false);
        coordinator.ObserveNativeFilm(Timestamp.AddSeconds(46));
        Equal(false, coordinator.IsNativeFilmNarrationPlaying, "the native film ending stops the track");
        Equal(1, output.Stops, "the track is stopped exactly once");
    }

    private static void ForegroundLossAndResetStopTheTrack()
    {
        foreach (var useReset in new[] { false, true })
        {
            var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
            var output = new FakeOutput();
            var narration = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldPositionReader.FieldModule);
            var coordinator = Create(memory, narration);
            memory.SetFilm(ArrivalFilm, active: true);
            coordinator.Observe(Snapshot(ArrivalByte));
            Equal(true, coordinator.TrySpeakPending(true, () => false, _ => true,
                    Timestamp.AddMilliseconds(100), out _), "the track starts");

            if (useReset)
            {
                coordinator.Reset();
            }
            else
            {
                coordinator.SuspendNativeFilmNarration(FieldMovieNarrationStopReason.Suspended);
            }

            Equal(false, coordinator.IsNativeFilmNarrationPlaying,
                useReset ? "reset stops the track" : "losing the foreground stops the track");
            Equal(1, output.Stops, "the track is stopped exactly once");
        }
    }

    private static void RejectedSpeechKeepsTheOldestCueAtTheHead()
    {
        // Two cues queue in the same field and the speaker refuses the first. The
        // older one must stay at the head and be delivered first once accepted.
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var coordinator = Create(memory, narration: null);
        memory.SetFilm(ArrivalFilm, active: true);
        Equal(true, coordinator.Observe(Snapshot(ArrivalByte)), "the arrival cue is queued");
        memory.SetFilm(DockingFilm, active: true);
        Equal(true, coordinator.Observe(Snapshot(DockingByte, sequence: 2)), "the docking cue is queued");

        var accept = false;
        var spoken = new List<string>();
        bool TrySpeak(string text)
        {
            if (!accept)
            {
                return false;
            }

            spoken.Add(text);
            return true;
        }

        Equal(false, coordinator.TrySpeakPending(true, () => false, TrySpeak, Timestamp, out _),
            "a refused cue is not delivered");
        Equal(0, spoken.Count, "nothing is spoken while the speaker refuses");

        accept = true;
        Equal(true, coordinator.TrySpeakPending(true, () => false, TrySpeak, Timestamp.AddSeconds(1), out var first),
            "the refused cue is retried");
        Equal(ArrivalByte, first.ByteIndex, "the oldest cue is delivered first, not reordered");
        Equal(true, coordinator.TrySpeakPending(true, () => false, TrySpeak, Timestamp.AddSeconds(2), out var second),
            "the second cue follows");
        Equal(DockingByte, second.ByteIndex, "the later cue is delivered second");
        Equal(2, spoken.Count, "both cues are delivered exactly once");
        Equal(ArrivalCue.Text, spoken[0], "the arrival description came first");
        Equal(DockingCue.Text, spoken[1], "the docking description came second");
    }

    private static Steam2026FieldCutsceneDescriptionCoordinator Create(
        FakeAddressSpace memory, FieldMovieNarrationTracker? narration) =>
        new(memory, FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions(), narration);

    private static Steam2026FieldCutsceneIngressSnapshot Snapshot(int byteIndex, int sequence = 1) =>
        new(
            sequence,
            Timestamp,
            new FieldScriptContext(Gldst, 0, 0, byteIndex, FieldOpcodeAddressResolver.OpcodeMovieIndex));

    /// <summary>
    /// A snapshot carrying the film state as it stood at the native boundary, which
    /// is the only place the MOVIE handler's own state can still be read.
    /// </summary>
    private static Steam2026FieldCutsceneIngressSnapshot Snapshot(
        int byteIndex,
        int sequence,
        DateTime capturedUtc,
        int handlerState,
        int handlerPhase,
        int movieNumber,
        bool movieActive) =>
        new(
            sequence,
            capturedUtc,
            new FieldScriptContext(Gldst, 0, 0, byteIndex, FieldOpcodeAddressResolver.OpcodeMovieIndex),
            HasMovieSample: true,
            MovieSample: new FieldMovieNarrationSample(
                movieActive,
                movieNumber,
                FieldPositionReader.FieldModule,
                Gldst,
                handlerState,
                handlerPhase,
                // The Gold Saucer is disc 1, and these samples are the engine playing
                // a film, so the fixture says both rather than relying on a default
                // production would refuse.
                Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
                // A film at frame zero is a film that has just started.
                MovieFrame: 0));

    private sealed class FakeAddressSpace : ILegacyAddressSpace
    {
        /// <summary>Any plausible guest pointer; the reader only range-checks it.</summary>
        private const uint ScriptContext = 0x00700000;

        public byte Module { get; set; } = 1;
        public ushort FieldId { get; set; }
        public byte ActiveMessageCount { get; set; }

        /// <summary>
        /// What the handler state reads as *now*. After the original has run this is
        /// 4 even for a film that has only just started, which is exactly why the
        /// coordinator must use the snapshot's captured state instead.
        /// </summary>
        public byte HandlerState { get; set; } = FieldMovieNarrationPolicy.MovieHandlerStateInProgress;

        public short HandlerPhase { get; set; } = FieldMovieNarrationPolicy.MovieHandlerPhaseYielding;
        public bool ScriptContextReadable { get; set; } = true;

        public byte Disc { get; set; } = 1;

        public byte MovieCommand { get; set; } =
            FieldMovieNarrationSample.CommandStartMovie;

        public byte MoviesSkipped { get; set; }

        /// <summary>The film's own frame counter; zero is a film that just started.</summary>
        public ushort MovieFrame { get; set; }

        private ushort movieActive;
        private ushort movieNumber;

        public void SetFilm(int number, bool active)
        {
            movieNumber = (ushort)number;
            movieActive = active ? (ushort)1 : (ushort)0;
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            switch (virtualAddress)
            {
                case (uint)FieldPositionReader.AddressCurrentModule when destination.Length == 1:
                    destination[0] = Module;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount when destination.Length == 1:
                    destination[0] = ActiveMessageCount;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldScriptContextPointer
                    when destination.Length == 4:
                    return ScriptContextReadable && Write(destination, ScriptContext);
                case ScriptContext + FieldAudibleCueStateReader.FieldScriptContextStateOffset
                    when destination.Length == 1:
                    if (!ScriptContextReadable)
                    {
                        return false;
                    }

                    destination[0] = HandlerState;
                    return true;
                case ScriptContext + FieldAudibleCueStateReader.FieldScriptContextPhaseOffset
                    when destination.Length == 2:
                    return ScriptContextReadable && Write(destination, (ushort)HandlerPhase);
                case (uint)FieldPositionReader.AddressFieldId when destination.Length == 2:
                    return Write(destination, FieldId);
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieActive when destination.Length == 2:
                    return Write(destination, movieActive);
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieNumber when destination.Length == 2:
                    return Write(destination, movieNumber);

                // The three bytes the runtime now needs before it will name a film:
                // which disc the name blocks are chosen by, which command owns the
                // argument word, and whether the engine is skipping films entirely.
                // The Gold Saucer is disc 1 and these fixtures are the engine playing
                // a film normally.
                case (uint)MovieFilmNameResolver.AddressMovieDisc when destination.Length == 1:
                    destination[0] = Disc;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieCommand
                    when destination.Length == 1:
                    destination[0] = MovieCommand;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldMoviesSkipped
                    when destination.Length == 1:
                    destination[0] = MoviesSkipped;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieFrame
                    when destination.Length == 2:
                    return Write(destination, MovieFrame);
                default:
                    return false;
            }
        }

        private static bool Write(Span<byte> destination, ushort value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
            return true;
        }

        private static bool Write(Span<byte> destination, uint value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
            return true;
        }
    }

    private sealed class FakeOutput : IFieldMovieNarrationOutput
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

        /// <summary>The device finishing before the native film does.</summary>
        public void Finish() => playing = false;

        public bool Stop(string reason)
        {
            Stops++;
            playing = false;
            return true;
        }

        public void Dispose() => playing = false;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"x64 film narration adapter - {label}: expected {expected}, got {actual}.");
        }
    }
}
