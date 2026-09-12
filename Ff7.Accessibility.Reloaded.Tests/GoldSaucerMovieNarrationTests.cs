using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Independent narration for the described Gold Saucer arrival film. The unit of
/// correctness is the native film episode: the arrival panorama is film 40 and the
/// docking film that follows it in the same field and module is film 4, so a cue
/// delayed across that boundary must expire rather than talk over the wrong film.
/// </summary>
internal static class GoldSaucerMovieNarrationTests
{
    private const int FieldModule = 1;
    private const int Gldst = 496;
    private const int ArrivalByte = 190;
    private const int DockingByte = 201;
    private const int ArrivalFilm = 40;
    private const int DockingFilm = 4;
    private const int MovieOpcode = FieldOpcodeAddressResolver.OpcodeMovieIndex;

    private static readonly DateTime Start = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        OnlyTheReviewedFilmStartAnchorResolves();
        StartsOnItsOwnFilmAndIgnoresDuplicateDelivery();
        ADelayedArrivalCueExpiresOnceTheDockingFilmStarts();
        ALongDelayInsideTheFilmNeverStartsFromZero();
        ADuplicateAfterNaturalCompletionDoesNotRestart();
        TheNativeRepeatOfTheSameOpcodeCannotReopenAnEpisode();
        ALaterRideReachesItsOpcodeBeforeItsFilmActivates();
        AFilmThatStartsLateIsNotDescribedFromTheMiddle();
        TheNativeHandlerStateSeparatesAFreshStartFromARepeat();
        ThePreActivationOrderIsToleratedButNotStartedEarly();
        EveryNativeBoundaryStopsTheTrack();
        UnavailableOrFailedAudioFallsBackToSpeech();
        ARefusedDescriptionKeepsItsPlaceAtTheHeadOfTheQueue();
        TheInstalledAssetMatchesTheReviewedRecording();
        EveryGondolaFilmIsAnchoredToItsOwnInstalledFilmStart();
        EveryDescribedFilmShipsItsReviewedRecording();
        EveryDescribedFilmHasASpokenFallback();
    }

    private static void ARefusedDescriptionKeepsItsPlaceAtTheHeadOfTheQueue()
    {
        // The x86 delivery path used to dequeue on attempt and re-enqueue on
        // failure, which moved a refused cue behind later ones and played the scene
        // out of order. It also began the fifteen-second dialogue reservation before
        // the speaker had accepted anything, so an unavailable speaker renewed that
        // window on every tick.
        var queue = new FieldCutsceneDescriptionDeliveryQueue();
        var first = new FieldCutsceneDescriptionCue(Gldst, 0, 0, ArrivalByte, "first", MovieOpcode);
        var second = new FieldCutsceneDescriptionCue(Gldst, 0, 0, DockingByte, "second", MovieOpcode);
        queue.Enqueue(first);
        queue.Enqueue(second);

        Equal(true, queue.TryPeek(Gldst, out var head), "the queue offers its oldest cue");
        Equal("first", head.Text, "the oldest cue is offered first");
        // A refused delivery commits nothing.
        Equal(true, queue.TryPeek(Gldst, out head), "a refused cue is offered again");
        Equal("first", head.Text, "a refused cue keeps its place at the head");
        Equal(2, queue.Count, "a refused cue is neither lost nor duplicated");

        queue.CommitDelivered(head);
        Equal(true, queue.TryPeek(Gldst, out head), "the next cue follows");
        Equal("second", head.Text, "delivery order is preserved");
        queue.CommitDelivered(head);
        Equal(false, queue.TryPeek(Gldst, out _), "an emptied queue offers nothing");

        // Committing a cue that is no longer the head must not drop someone else's.
        queue.Enqueue(first);
        queue.CommitDelivered(second);
        Equal(1, queue.Count, "committing a stale cue must not remove the current head");

        // Cues for a field the player has left can never be delivered.
        Equal(false, queue.TryPeek(497, out _), "another field's cue is not offered");
        Equal(0, queue.Count, "undeliverable cues are dropped rather than blocking the queue");
        Equal(false, queue.HasPendingFor(Gldst), "the queue reports itself empty");

        TheX86DeliveryStepHoldsACueForAFilmThatHasNotStartedYet();
    }

    private static void TheX86DeliveryStepHoldsACueForAFilmThatHasNotStartedYet()
    {
        // This is the x86 half of root's finding: a delivery that treats "not
        // started" as "speak the paragraph" commits the cue, and a frame later the
        // native film is running with nothing left to start the track with.
        var queue = new FieldCutsceneDescriptionDeliveryQueue();
        var delivery = new FieldCutsceneDescriptionDelivery(queue);
        var cue = new FieldCutsceneDescriptionCue(Gldst, 0, 0, ArrivalByte, "arrival", MovieOpcode);
        queue.Enqueue(cue);

        var spoken = new List<string>();
        var narrated = new List<FieldCutsceneDescriptionCue>();
        var result = FieldMovieNarrationStartResult.WaitingForNativeStart;
        FieldCutsceneDeliveryOutcome Run() => delivery.Deliver(
            Gldst,
            _ => result,
            text => { spoken.Add(text); return true; },
            narrated.Add,
            out _);

        // A speaker that would happily accept must not be offered the text.
        Equal(FieldCutsceneDeliveryOutcome.Waiting, Run(), "an early film holds the cue");
        Equal(FieldCutsceneDeliveryOutcome.Waiting, Run(), "it keeps holding while still early");
        Equal(0, spoken.Count, "no paragraph may be spoken while waiting for the film");
        Equal(0, narrated.Count, "no dialogue window may be reserved while waiting");
        Equal(1, queue.Count, "the cue must still be queued");

        // The film starts and the independent track takes it.
        result = FieldMovieNarrationStartResult.Started;
        Equal(FieldCutsceneDeliveryOutcome.Narrated, Run(), "the track takes the cue once the film starts");
        Equal(0, spoken.Count, "the paragraph is still not spoken");
        Equal(1, narrated.Count, "the dialogue window is reserved exactly once");
        Equal(0, queue.Count, "the cue is committed exactly once");

        // The bounded deadline: the tracker eventually reports Unavailable and the
        // paragraph is spoken rather than the scene being left undescribed.
        queue.Enqueue(cue);
        result = FieldMovieNarrationStartResult.WaitingForNativeStart;
        Equal(FieldCutsceneDeliveryOutcome.Waiting, Run(), "still waiting");
        result = FieldMovieNarrationStartResult.Unavailable;
        Equal(FieldCutsceneDeliveryOutcome.Spoken, Run(), "past the deadline the paragraph is spoken");
        Equal(1, spoken.Count, "the paragraph is spoken exactly once");
        Equal(0, queue.Count, "the cue is not lost");

        // The Started and Spoken outcomes both reserve the dialogue window, and only
        // those two: two deliveries so far, so two reservations.
        Equal(2, narrated.Count, "each real delivery reserves the dialogue window once");

        // A refused speaker keeps the cue and reserves nothing.
        queue.Enqueue(cue);
        var refusedReservations = 0;
        var refusing = new FieldCutsceneDescriptionDelivery(queue);
        Equal(FieldCutsceneDeliveryOutcome.Refused,
            refusing.Deliver(Gldst, _ => FieldMovieNarrationStartResult.NotDescribed,
                _ => false, _ => refusedReservations++, out _),
            "a refused speaker reports refusal");
        Equal(1, queue.Count, "a refused cue stays queued");
        Equal(0, refusedReservations,
            "a refused cue must not renew the fifteen-second dialogue reservation");
        Equal(FieldCutsceneDeliveryOutcome.Nothing,
            refusing.Deliver(497, _ => FieldMovieNarrationStartResult.NotDescribed,
                _ => true, _ => refusedReservations++, out _),
            "another field has nothing to deliver");
        Equal(0, refusedReservations, "an empty delivery reserves nothing either");
    }

    private static void OnlyTheReviewedFilmStartAnchorResolves()
    {
        Equal(true, FieldMovieNarrationPolicy.TryResolve(Gldst, 0, 0, ArrivalByte, out var track),
            "the reviewed arrival film start must resolve to a narration track");
        Equal("gold1_audio_description.ogg", track.FileName, "the arrival track file must be the reviewed asset");
        Equal(ArrivalFilm, track.MovieNumber, "the arrival track must name the native film number");
        Equal(45.0d, track.DurationSeconds, "the arrival track duration must match the 45-second film");
        // The docking film now has a recording of its own, so this anchor resolves -
        // but it must resolve to that film and never to the 45-second panorama. The
        // point of the byte-level anchor is exactly this separation: both films start
        // from the same field, module and entity.
        Equal(true, FieldMovieNarrationPolicy.TryResolve(Gldst, 0, 0, DockingByte, out var docking),
            "the docking film start resolves to its own narration track");
        Equal(DockingFilm, docking.MovieNumber,
            "the docking anchor must name the docking film, not the panorama");
        Equal("u_ropein_audio_description.ogg", docking.FileName,
            "the docking anchor must play the docking recording");
        Equal(false, FieldMovieNarrationPolicy.TryResolve(Gldst, 0, 0, 185, out _),
            "the prepare opcode must not start narration");
        Equal(true, FieldMovieNarrationPolicy.TryResolve(457, 2, 3, 109, out var ropeway),
            "the ropeway departure film now carries its own recording");
        Equal("d_ropego_audio_description.ogg", ropeway.FileName,
            "the ropeway departure anchor must play the ropeway recording");
    }

    private static void StartsOnItsOwnFilmAndIgnoresDuplicateDelivery()
    {
        var output = new FakeOutput();
        using var tracker = Create(output, out _);
        Ingress(tracker, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(true, Deliver(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)),
            "the native film start must begin independent narration");
        Equal(1, output.Starts, "the track must start once");
        Equal(true, tracker.IsPlaying, "the tracker must report the track as playing");

        Equal(true, Deliver(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(200)),
            "a duplicate delivery must still suppress the spoken paragraph");
        Equal(1, output.Starts, "a duplicate delivery must not restart the description from zero");
        Equal(0, output.Stops, "a duplicate delivery must not stop the running track");
    }

    private static void ADelayedArrivalCueExpiresOnceTheDockingFilmStarts()
    {
        // Root's exact scenario: byte 190 is queued, delivery is held behind
        // dialogue, and by the time it runs the byte 201 docking film of the *same
        // field and module* is playing. Checking only movieActive, field and module
        // would accept it.
        var output = new FakeOutput();
        using var tracker = Create(output, out var log);
        Ingress(tracker, ArrivalByte, Playing(ArrivalFilm), Start);
        Ingress(tracker, DockingByte, Playing(DockingFilm), Start.AddMilliseconds(300));
        Equal(false, tracker.HasPendingStart, "the docking film start must expire the arrival opportunity");
        Equal(false, Deliver(tracker, ArrivalByte, Playing(DockingFilm), Start.AddMilliseconds(400)),
            "a delayed arrival cue must not start over the docking film");
        Equal(0, output.Starts, "no track may start for the wrong film");

        // The same must hold if the docking film is running but its own ingress was
        // never seen, which is what the film number is for.
        var second = new FakeOutput();
        using var later = Create(second, out _);
        Ingress(later, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(false, Deliver(later, ArrivalByte, Playing(DockingFilm), Start.AddMilliseconds(400)),
            "a different native film number must be refused even without its own ingress");
        Equal(0, second.Starts, "no track may start while another film is running");

        // And an already-running track must stop when the other film takes over.
        var third = new FakeOutput();
        using var running = Create(third, out _);
        Ingress(running, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(true, Deliver(running, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)), "starts");
        running.Observe(Playing(DockingFilm), Start.AddSeconds(50));
        Equal(false, running.IsPlaying, "the docking film must stop the arrival narration");
        Equal(1, third.Stops, "the arrival track must be stopped exactly once");
    }

    private static void ALongDelayInsideTheFilmNeverStartsFromZero()
    {
        var output = new FakeOutput();
        using var tracker = Create(output, out _);
        Ingress(tracker, ArrivalByte, Playing(ArrivalFilm), Start);
        // Still the same film, still the same episode - but 20 seconds into a
        // 45-second film. Starting the track now would describe the wrong seconds.
        Equal(false, Deliver(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddSeconds(20)),
            "a long delay inside the film must not start the description from zero");
        Equal(0, output.Starts, "no track may start part-way through the film");

        // A delivery just inside the window is still fine.
        var prompt = new FakeOutput();
        using var promptly = Create(prompt, out _);
        Ingress(promptly, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(true, Deliver(promptly, ArrivalByte, Playing(ArrivalFilm),
                Start.Add(FieldMovieNarrationPolicy.StartWindow).AddMilliseconds(-1)),
            "a delivery inside the start window is still accepted");
    }

    private static void ADuplicateAfterNaturalCompletionDoesNotRestart()
    {
        var output = new FakeOutput();
        using var tracker = Create(output, out _);
        Ingress(tracker, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(true, Deliver(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)), "starts");

        // The 45-second track finishes while the native film is still running.
        output.Finish();
        tracker.Observe(Playing(ArrivalFilm), Start.AddSeconds(45));
        Equal(false, tracker.IsPlaying, "a finished device must clear the active track");

        // A second delivery of the same cue in the same episode must not replay it.
        Ingress(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddSeconds(46));
        Equal(false, Deliver(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddSeconds(46.1)),
            "a duplicate in the same episode must not restart a completed description");
        Equal(1, output.Starts, "the description must have started exactly once");

        // A genuinely new episode of the same film is allowed again.
        tracker.Observe(Idle(), Start.AddSeconds(47));
        Ingress(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddSeconds(48));
        Equal(true, Deliver(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddSeconds(48.1)),
            "a new episode of the same film may be described again");
    }

    private static void ThePreActivationOrderIsToleratedButNotStartedEarly()
    {
        // The opcode hook can run a frame or two before the engine raises its own
        // active flag. The opportunity must survive and the caller must be told to
        // wait, not that the track is unavailable: a caller that speaks here commits
        // the cue and there is nothing left to start the track with.
        var output = new FakeOutput();
        using var tracker = Create(output, out _);
        Ingress(tracker, ArrivalByte, Idle(), Start);
        Equal(true, tracker.HasPendingStart, "the opportunity must survive before the film is active");
        Equal(FieldMovieNarrationStartResult.WaitingForNativeStart,
            DeliverResult(tracker, ArrivalByte, Idle(), Start.AddMilliseconds(50)),
            "an early delivery must report waiting, not unavailable");
        Equal(0, output.Starts, "no track may start before the film");

        tracker.Observe(Idle(), Start.AddMilliseconds(100));
        Equal(true, tracker.HasPendingStart, "an idle frame must not expire a fresh opportunity");
        Equal(true, Deliver(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(200)),
            "the track starts once the native film actually begins");
        Equal(1, output.Starts, "the track must start exactly once");

        // The wait is bounded: a film that never starts must not hold the cue for
        // ever, and the ordinary paragraph has to be spoken instead.
        var stalled = new FakeOutput();
        using var stalledTracker = Create(stalled, out _);
        Ingress(stalledTracker, ArrivalByte, Idle(), Start);
        Equal(FieldMovieNarrationStartResult.WaitingForNativeStart,
            DeliverResult(stalledTracker, ArrivalByte, Idle(),
                Start.Add(FieldMovieNarrationPolicy.PreActivationWindow).AddMilliseconds(-1)),
            "the wait holds until its deadline");
        Equal(FieldMovieNarrationStartResult.Unavailable,
            DeliverResult(stalledTracker, ArrivalByte, Idle(),
                Start.Add(FieldMovieNarrationPolicy.PreActivationWindow).AddMilliseconds(1)),
            "past the deadline the paragraph must be spoken instead");
        Equal(0, stalled.Starts, "a film that never started must not start a track");
    }

    private static void TheNativeRepeatOfTheSameOpcodeCannotReopenAnEpisode()
    {
        // The native F9 handler yields without advancing the script pointer while a
        // film is running (0061A321), so the same byte 190 arrives on every movie
        // frame. Those repeats must not refresh the start window nor resurrect an
        // opportunity that has already expired, or a delivery twenty seconds into a
        // forty-five second film would still start the description from zero.
        var output = new FakeOutput();
        using var tracker = Create(output, out _);
        var film = Playing(ArrivalFilm);
        Ingress(tracker, ArrivalByte, film, Start);

        // Repeats while the opportunity is still live must not move its clock.
        for (var frame = 1; frame <= 4; frame++)
        {
            Ingress(tracker, ArrivalByte, film, Start.AddMilliseconds(100 * frame));
        }

        Equal(FieldMovieNarrationStartResult.Unavailable,
            DeliverResult(tracker, ArrivalByte, film, Start.AddSeconds(20)),
            "a repeat must not extend the start window");
        Equal(0, output.Starts, "no track may start twenty seconds into the film");

        // And after expiry, further repeats must not reopen the same episode.
        Ingress(tracker, ArrivalByte, film, Start.AddSeconds(21));
        Equal(false, tracker.HasPendingStart, "an expired episode must not be reopened by a repeat");
        Equal(FieldMovieNarrationStartResult.Unavailable,
            DeliverResult(tracker, ArrivalByte, film, Start.AddSeconds(21.1)),
            "a repeat after expiry must not start the description");
        Equal(0, output.Starts, "no track may start from a reopened episode");

        // A genuinely new episode of the same film is still allowed.
        tracker.Observe(Idle(), Start.AddSeconds(46));
        Ingress(tracker, ArrivalByte, film, Start.AddSeconds(47));
        Equal(true, Deliver(tracker, ArrivalByte, film, Start.AddSeconds(47.1)),
            "the next genuine episode may still be described");
        Equal(1, output.Starts, "the next episode starts exactly one track");

        // Repeats during the running track must not restart it either.
        Ingress(tracker, ArrivalByte, film, Start.AddSeconds(48));
        Equal(FieldMovieNarrationStartResult.Started,
            DeliverResult(tracker, ArrivalByte, film, Start.AddSeconds(48.1)),
            "a repeat during playback reports the running track");
        Equal(1, output.Starts, "a repeat during playback must not restart the description");
    }

    private static void ALaterRideReachesItsOpcodeBeforeItsFilmActivates()
    {
        // Root's repro: the arrival film is described, ends, and the player rides the
        // ropeway again. The second ride reaches byte 190 before the engine raises the
        // active flag, so the episode counter has not moved since the first ride was
        // settled. Refusing on the settled episode alone silently drops the second
        // description, which is the failure the mod exists to prevent.
        var output = new FakeOutput();
        using var tracker = Create(output, out _);
        var film = Playing(ArrivalFilm);

        Ingress(tracker, ArrivalByte, film, Start);
        Equal(true, Deliver(tracker, ArrivalByte, film, Start), "the first ride is described");
        output.Finish();
        tracker.Observe(Idle(), Start.AddSeconds(46));

        Equal(true, Ingress(tracker, ArrivalByte, Idle(), Start.AddSeconds(60)),
            "a later ride must be able to open its own start opportunity");
        Equal(FieldMovieNarrationStartResult.WaitingForNativeStart,
            DeliverResult(tracker, ArrivalByte, Idle(), Start.AddSeconds(60)),
            "the second ride waits for its film rather than being refused");
        Equal(true, Deliver(tracker, ArrivalByte, film, Start.AddSeconds(60.2)),
            "the second ride is described once its film starts");
        Equal(2, output.Starts, "each genuine ride starts exactly one track");
    }

    private static void AFilmThatStartsLateIsNotDescribedFromTheMiddle()
    {
        // Root's second repro: the opportunity is given up because the film had not
        // activated in time, and the film then starts anyway. That start advances the
        // episode counter, so the episode high-water mark no longer refuses anything -
        // and the handler's own repeat of byte 190 would open a fresh opportunity part
        // way through the film. The anchor stays held until a native boundary.
        var output = new FakeOutput();
        using var tracker = Create(output, out _);

        Ingress(tracker, ArrivalByte, Idle(), Start);
        tracker.Observe(Idle(), Start.AddSeconds(3));
        Equal(false, tracker.HasPendingStart, "the stalled opportunity is given up");

        var film = Playing(ArrivalFilm);
        Equal(false, Ingress(tracker, ArrivalByte, film, Start.AddSeconds(20)),
            "a repeat inside the late film must not reopen the anchor");
        Equal(FieldMovieNarrationStartResult.Unavailable,
            DeliverResult(tracker, ArrivalByte, film, Start.AddSeconds(20)),
            "nothing may start twenty seconds into the film");
        Equal(0, output.Starts, "no track may start part way through the film");

        // The next genuine ride, after the film ends, is still described.
        tracker.Observe(Idle(), Start.AddSeconds(50));
        Ingress(tracker, ArrivalByte, film, Start.AddSeconds(51));
        Equal(true, Deliver(tracker, ArrivalByte, film, Start.AddSeconds(51)),
            "the hold is released by the film ending, not by time");
        Equal(1, output.Starts, "the next genuine ride starts one track");
    }

    private static void TheNativeHandlerStateSeparatesAFreshStartFromARepeat()
    {
        // FUN_0061A321 reads the field script context at 0x00CBF9D8 and takes the
        // state byte at +0x01: state 0 is the fresh-entry branch, state 4 is the
        // handler meeting the same opcode again, where the phase word at +0x26 is 1
        // while it yields and 2 on the completion that advances the script pointer.
        // When that state is readable it decides directly, in both directions.
        Equal(true, FieldMovieNarrationPolicy.IsFreshNativeStart(Fresh(Playing(ArrivalFilm))),
            "state 0 is a fresh film start");
        Equal(false, FieldMovieNarrationPolicy.IsFreshNativeStart(Repeat(Playing(ArrivalFilm))),
            "state 4 with phase 1 is the handler yielding on a running film");
        Equal(null, FieldMovieNarrationPolicy.IsFreshNativeStart(Playing(ArrivalFilm)),
            "an unreadable state must stay unknown rather than guess");

        var output = new FakeOutput();
        using var tracker = Create(output, out _);
        var film = Playing(ArrivalFilm);

        Ingress(tracker, ArrivalByte, Fresh(film), Start);
        Equal(true, Deliver(tracker, ArrivalByte, Fresh(film), Start), "the fresh entry is described");

        // Every frame of the running film re-delivers byte 190 with state 4.
        for (var second = 1; second <= 30; second++)
        {
            Equal(false, Ingress(tracker, ArrivalByte, Repeat(film), Start.AddSeconds(second)),
                "a handler repeat must never open an opportunity");
        }

        Equal(1, output.Starts, "the repeats must not start a second track");

        // The completion pass releases the film, and the next fresh entry is
        // described even though no idle sample was ever observed between them.
        Ingress(tracker, ArrivalByte, Completion(film), Start.AddSeconds(45));
        output.Finish();
        tracker.Observe(Completion(film), Start.AddSeconds(45));
        Equal(true, Ingress(tracker, ArrivalByte, Fresh(Idle()), Start.AddSeconds(90)),
            "a fresh handler entry opens the next ride's opportunity");
        Equal(true, Deliver(tracker, ArrivalByte, Fresh(film), Start.AddSeconds(90)),
            "the next ride is described");
        Equal(2, output.Starts, "each fresh entry starts exactly one track");
    }

    private static void EveryNativeBoundaryStopsTheTrack()
    {
        var boundaries = new (FieldMovieNarrationSample Sample, string Expected)[]
        {
            (Idle(), "MovieEnded"),
            (new FieldMovieNarrationSample(true, ArrivalFilm, 3, Gldst, Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0), "ModuleChanged"),
            // A new field with no film running, or a different one: the scene the
            // description belongs to is over either way.
            (new FieldMovieNarrationSample(false, 0, FieldModule, 497, Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0), "FieldChanged"),
            (new FieldMovieNarrationSample(true, DockingFilm, FieldModule, 497, Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0), "FieldChanged"),
            (Playing(DockingFilm), "OtherMovieStarted")
        };
        foreach (var (sample, expected) in boundaries)
        {
            var output = new FakeOutput();
            using var tracker = Create(output, out var log);
            Ingress(tracker, ArrivalByte, Playing(ArrivalFilm), Start);
            Equal(true, Deliver(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)),
                "the track must be running before the boundary");
            tracker.Observe(sample, Start.AddSeconds(5));
            Equal(false, tracker.IsPlaying, $"{expected} must stop the narration");
            Equal(1, output.Stops, $"{expected} must stop the output exactly once");
            Equal(true, log.Any(line => line.Contains($"reason={expected}", StringComparison.Ordinal)),
                $"the {expected} stop must be recorded; log was {string.Join(" | ", log)}");
        }

        var steady = new FakeOutput();
        using var steadyTracker = Create(steady, out _);
        Ingress(steadyTracker, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(true, Deliver(steadyTracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)), "starts");
        steadyTracker.Observe(Playing(ArrivalFilm), Start.AddSeconds(10));
        Equal(true, steadyTracker.IsPlaying, "an unchanged film must keep playing");
        Equal(0, steady.Stops, "an unchanged film must not stop the track");

        // A film can outlive the field that started it - the Highwind sequence runs
        // one film across four fields whose scripts have a MOVIE and no PMVIE of
        // their own. The story moving on under a film that is still the same film is
        // not the film ending, and cutting the description there would lose the rest
        // of it for no reason.
        var continued = new FakeOutput();
        using var continuedTracker = Create(continued, out _);
        Ingress(continuedTracker, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(true, Deliver(continuedTracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)), "starts");
        continuedTracker.Observe(
            new FieldMovieNarrationSample(true, ArrivalFilm, FieldModule, 497, Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 90),
            Start.AddSeconds(6));
        Equal(true, continuedTracker.IsPlaying, "the same film in a new field keeps playing");
        Equal(0, continued.Stops, "and is not restarted");

        var unloaded = new FakeOutput();
        var unloadedTracker = Create(unloaded, out _);
        Ingress(unloadedTracker, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(true, Deliver(unloadedTracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)), "starts");
        unloadedTracker.Dispose();
        Equal(1, unloaded.Stops, "disposal must stop the track");
        Equal(true, unloaded.Disposed, "disposal must release the audio device");

        // Suspending on foreground loss stops it and clears any pending start.
        var suspended = new FakeOutput();
        using var suspendedTracker = Create(suspended, out _);
        Ingress(suspendedTracker, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(true, Deliver(suspendedTracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)), "starts");
        suspendedTracker.Stop(FieldMovieNarrationStopReason.Suspended);
        Equal(false, suspendedTracker.IsPlaying, "a suspended host must stop the independent track");
        Equal(false, suspendedTracker.HasPendingStart, "a suspended host must clear any pending start");
    }

    private static void UnavailableOrFailedAudioFallsBackToSpeech()
    {
        using var missing = new FieldMovieNarrationTracker(_ => null, _ => { }, FieldModule);
        Ingress(missing, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(false, Deliver(missing, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)),
            "a missing narration asset must fall back to the spoken paragraph");

        var failing = new FakeOutput { FailStart = true };
        using var tracker = Create(failing, out _);
        Ingress(tracker, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(false, Deliver(tracker, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)),
            "a failed audio device must fall back to the spoken paragraph");
        Equal(true, failing.Disposed, "a failed start must release the device it created");
        Equal(false, tracker.IsPlaying, "a failed start must leave no active track");

        // A factory that throws must be caught inside the tracker and behave exactly
        // like a missing asset, so neither runtime has to guard the call itself.
        using var throwing = new FieldMovieNarrationTracker(
            _ => throw new InvalidOperationException("no audio device"), _ => { }, FieldModule);
        Ingress(throwing, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(FieldMovieNarrationStartResult.Unavailable,
            DeliverResult(throwing, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)),
            "a throwing output factory must fall back to the spoken paragraph");
        Equal(false, throwing.IsPlaying, "a throwing factory must leave no active track");

        // A device that throws from Start must be caught inside the tracker.
        var throwsOnStart = new FakeOutput { ThrowOnStart = true };
        using var startFault = Create(throwsOnStart, out _);
        Ingress(startFault, ArrivalByte, Playing(ArrivalFilm), Start);
        Equal(FieldMovieNarrationStartResult.Unavailable,
            DeliverResult(startFault, ArrivalByte, Playing(ArrivalFilm), Start.AddMilliseconds(100)),
            "a device that throws while starting must fall back to speech");
        Equal(true, throwsOnStart.Disposed, "a throwing start must release the device");
    }

    private static void TheInstalledAssetMatchesTheReviewedRecording()
    {
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new InvalidOperationException("Gold Saucer narration tests require FF7_ACCESSIBILITY_SOURCE_ROOT.");
        var assets = Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "movies");
        var track = Path.Combine(assets, FieldMovieNarrationPolicy.GoldSaucerArrival.FileName);
        Equal(true, File.Exists(track), $"the reviewed narration asset must be present at {track}");
        // Brice's approved voice export (2026-09-12), with the same script and cue starts.
        // qa-release.json records the audio, transcript and duration checks for these bytes.
        Equal("6E4D52C3F5297F024FB4F2E4B8C1D258EA524063ED0C5E7FE96B8F38830EC255",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(track))),
            "the packaged narration must be the reviewed recording");
        foreach (var companion in new[] { "gold1_audio_description.json", "gold1_audio_description.credits.txt" })
        {
            Equal(true, File.Exists(Path.Combine(assets, companion)),
                $"{companion} must ship alongside the narration for provenance and attribution");
        }
    }

    private static void EveryGondolaFilmIsAnchoredToItsOwnInstalledFilmStart()
    {
        // The five first-visit gondola views, each with two anchors because the
        // ride's script forks on which companion came along. Selected by film number
        // rather than by field: gold7 and gold7_2, the evening date scene, start from
        // the same field and now carry reviewed recordings of their own, so a
        // field-wide filter no longer means "the gondola films".
        int[] gondolaFilms = [6, 7, 8, 9, 10];
        var gondola = FieldMovieNarrationPolicy.All
            .Where(track => gondolaFilms.Contains(track.MovieNumber))
            .ToArray();
        Equal(10, gondola.Length, "the five gondola films must each carry both companion branches");
        Equal(5, gondola.Select(track => track.MovieNumber).Distinct().Count(),
            "the ten anchors must cover exactly five films");
        Equal(true, gondola.All(track => track.FieldId is 489 or 490),
            "every gondola anchor belongs to bwhlin or bwhlin2");
        Equal(10, gondola.Select(track => (track.FieldId, track.ByteIndex)).Distinct().Count(),
            "no two anchors may share a field and byte");

        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new InvalidOperationException("Gold Saucer narration tests require FF7_ACCESSIBILITY_DATA_ROOT.");
        var scripts = new FieldScriptNavigationCatalog(dataRoot);

        foreach (var track in gondola)
        {
            var opcodes = scripts
                .ReadScriptOpcodes(track.FieldId, track.EntityId, track.ScriptId)
                .OrderBy(item => item.ByteIndex)
                .ToArray();
            var index = Array.FindIndex(opcodes, item => item.ByteIndex == track.ByteIndex);
            Equal(true, index >= 0,
                $"{track.Label}: byte {track.ByteIndex} must be an installed opcode boundary");
            Equal("F9", Convert.ToHexString(opcodes[index].Bytes.ToArray()),
                $"{track.Label}: the anchor must be the film-start opcode");

            // The film identity comes from the F8 that prepares it, which is the last
            // one before this start. This is what proves the anchor belongs to the
            // recording named on the track: the native numbers are not in file-name
            // order, so 9 is gold6 and 10 is gold5.
            var prepared = -1;
            for (var scan = index - 1; scan >= 0; scan--)
            {
                var bytes = opcodes[scan].Bytes.ToArray();
                if (bytes.Length == 2 && bytes[0] == 0xF8)
                {
                    prepared = bytes[1];
                    break;
                }
            }

            Equal(track.MovieNumber, prepared,
                $"{track.Label}: the preceding prepare opcode must name film {track.MovieNumber}");
        }
    }

    private static void EveryDescribedFilmShipsItsReviewedRecording()
    {
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new InvalidOperationException("Gold Saucer narration tests require FF7_ACCESSIBILITY_SOURCE_ROOT.");
        var assets = Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "movies");

        foreach (var track in FieldMovieNarrationPolicy.All)
        {
            var stem = track.FileName[..^Path.GetExtension(track.FileName).Length];
            foreach (var companion in new[] { track.FileName, stem + ".json", stem + ".credits.txt" })
            {
                Equal(true, File.Exists(Path.Combine(assets, companion)),
                    $"{track.Label}: {companion} must ship with the mod");
            }

            // A track that ends before its own last described moment would cut the
            // description off, so the declared length has to cover the recording.
            var sidecar = File.ReadAllText(Path.Combine(assets, stem + ".json"));
            var lastEnd = System.Text.RegularExpressions.Regex
                .Matches(sidecar, "\"end_time\"\\s*:\\s*\"(\\d+):(\\d+):(\\d+(?:\\.\\d+)?)\"")
                .Select(match => (int.Parse(match.Groups[1].Value) * 3600)
                                 + (int.Parse(match.Groups[2].Value) * 60)
                                 + double.Parse(match.Groups[3].Value,
                                     System.Globalization.CultureInfo.InvariantCulture))
                .DefaultIfEmpty(-1d)
                .Max();
            Equal(true, lastEnd > 0d, $"{track.Label}: the sidecar must describe at least one moment");
            Equal(true, lastEnd <= track.DurationSeconds + 0.001d,
                $"{track.Label}: the declared {track.DurationSeconds:0.###}s must cover the recording's " +
                $"last described moment at {lastEnd:0.###}s");
        }
    }

    private static void EveryDescribedFilmHasASpokenFallback()
    {
        // The independent track is stopped by every native boundary and can be
        // missing, disabled or refused by the device. When that happens the ordinary
        // paragraph is spoken instead, so every anchor needs one - a film with a
        // track but no cue would be silent on exactly the runs that need it most.
        var cues = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        foreach (var track in FieldMovieNarrationPolicy.All)
        {
            var cue = cues.SingleOrDefault(candidate =>
                candidate.FieldId == track.FieldId &&
                candidate.EntityId == track.EntityId &&
                candidate.ScriptId == track.ScriptId &&
                candidate.ByteIndex == track.ByteIndex);
            Equal(true, !string.IsNullOrWhiteSpace(cue.Text),
                $"{track.Label}: a described film must have exactly one spoken fallback paragraph");
            Equal(MovieOpcode, cue.Opcode, $"{track.Label}: the fallback must be anchored to the film start");
        }

        // The identity-manifest entries the gondola fields need are asserted for the
        // whole catalog by EchoSCompatibilityTests, which is x86-only because the
        // manifest is.
    }

    // A synthetic sample has to say which disc it is on and which command owns the
    // film number, because production refuses to identify a film without both. The
    // Gold Saucer is disc 1 and these samples are the engine playing a film, so that
    // is what they say rather than leaving a default to stand in for evidence.
    // The film's own frame counter is part of a sample now: a film at frame zero is
    // a film that has just started, which is what these fixtures mean.
    private static FieldMovieNarrationSample Playing(int movieNumber, int frame = 0) =>
        new(true, movieNumber, FieldModule, Gldst,
            Disc: 1, MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
            MovieFrame: frame);

    private static FieldMovieNarrationSample Idle() =>
        new(false, 0, FieldModule, Gldst,
            Disc: 1, MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
            MovieFrame: 0);

    private static FieldMovieNarrationSample Fresh(FieldMovieNarrationSample sample) =>
        sample with
        {
            MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry,
            MovieHandlerPhase = 0
        };

    private static FieldMovieNarrationSample Repeat(FieldMovieNarrationSample sample) =>
        sample with
        {
            MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateInProgress,
            MovieHandlerPhase = FieldMovieNarrationPolicy.MovieHandlerPhaseYielding
        };

    private static FieldMovieNarrationSample Completion(FieldMovieNarrationSample sample) =>
        sample with
        {
            MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateInProgress,
            MovieHandlerPhase = FieldMovieNarrationPolicy.MovieHandlerPhaseCompleting
        };

    private static bool Ingress(
        FieldMovieNarrationTracker tracker, int byteIndex, FieldMovieNarrationSample sample, DateTime nowUtc) =>
        tracker.NoteIngress(Gldst, 0, 0, byteIndex, MovieOpcode, sample, nowUtc);

    private static bool Deliver(
        FieldMovieNarrationTracker tracker, int byteIndex, FieldMovieNarrationSample sample, DateTime nowUtc) =>
        DeliverResult(tracker, byteIndex, sample, nowUtc) == FieldMovieNarrationStartResult.Started;

    private static FieldMovieNarrationStartResult DeliverResult(
        FieldMovieNarrationTracker tracker, int byteIndex, FieldMovieNarrationSample sample, DateTime nowUtc) =>
        tracker.Begin(Gldst, 0, 0, byteIndex, sample, nowUtc);

    private static FieldMovieNarrationTracker Create(FakeOutput output, out List<string> log)
    {
        var lines = new List<string>();
        log = lines;
        return new FieldMovieNarrationTracker(_ => output, lines.Add, FieldModule);
    }

    private sealed class FakeOutput : IFieldMovieNarrationOutput
    {
        private bool playing;
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailStart { get; init; }
        public bool ThrowOnStart { get; init; }
        public bool IsPlaying => playing;

        public bool Start(string reason)
        {
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("device refused to open");
            }

            if (FailStart)
            {
                return false;
            }

            Starts++;
            playing = true;
            return true;
        }

        public bool Stop(string reason)
        {
            Stops++;
            playing = false;
            return true;
        }

        public void Finish() => playing = false;

        public void Dispose() => Disposed = true;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Gold Saucer narration: {message}; expected {expected}, actual {actual}.");
    }
}
