using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// How the Temple clock's readings reach the speaker, on both runtimes: the newest reading
/// replaces an older one instead of queueing behind it, the Time Guardian's newly spoken
/// words are not cut off, and a reading the speaker refused is kept only while it is still
/// what the clock shows.
/// </summary>
internal static class TempleClockDeliveryTests
{
    private static readonly DateTime Epoch = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        OnlyTheNewestReadingIsSaid();
        TheGuardiansWordsAreNotCutOff();
        WithoutTheScreenReadersWordTheWordsAreTimed();
        AnOpenWindowDoesNotSilenceTheClock();
        TheClocksOwnReadingIsNotProtected();
        ARefusedReadingIsRetriedOnlyWhileCurrent();
        AThrowingSpeakerIsARefusal();
        LeavingTheRoomOwesNothing();
        TheRequestedDetailsAreHeardToTheirEnd();
        OverlappingSpeechIsWaitedForInFull();
        TheHostSpeaksTheClockOnlyWithFocus();
        TheHostKeepsOnlyTheCurrentTimeWhileMuted();
        TheHostLeavesOtherRoomsAlone();
    }

    /// <summary>
    /// The repeat key's own clock line (the one root's review probe uses), 34 words: at a slow
    /// screen reader rate it takes well over six seconds. While the screen reader says it is
    /// still speaking, the clock waits for it - at seven seconds and at twenty - and only a
    /// device that never stops is outlasted, after the line's own ceiling of 34 x 0.6 + 5 = 25.4
    /// seconds. Meanwhile only the newest time is owed.
    /// </summary>
    private static void TheRequestedDetailsAreHeardToTheirEnd()
    {
        const string details =
            "Stopped, ten thirty. You are by doorway ten. Long hand at six, short hand at ten, second hand at three. " +
            "The bridge to doorway six is open. The bridge to doorway ten is open.";
        Equal(34, details.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, "the real repeat line");
        var delivery = new FieldActivityLatestLineDelivery();
        var speaker = new Speaker();
        delivery.NoteOtherSpeech(details, T(0));
        Equal(null, delivery.Pump("Moving, ten thirty.", "Moving, ten thirty.", T(7), speaker.At(7), () => true),
            "still being spoken at seven seconds: not interrupted");
        Equal(null, delivery.Pump("Moving, about ten forty.", "Moving, about ten forty.", T(20), speaker.At(20), () => true),
            "nor at twenty");
        Equal(true, delivery.HasPending, "the clock's reading is kept, as the newest one only");
        Equal("Stopped, ten forty-five.", delivery.Pump(null, "Stopped, ten forty-five.", T(21), speaker.At(21), () => false),
            "the moment the screen reader has finished, the clock as it is now");
        Equal(1, speaker.Said.Count, "one reading");

        var stuck = new FieldActivityLatestLineDelivery();
        stuck.NoteOtherSpeech(details, T(0));
        Equal(null, stuck.Pump("Stopped, ten ten.", "Stopped, ten ten.", T(25.3), new Speaker().At(25.3), () => true),
            "a device that says it is speaking is believed for the line's ceiling");
        Equal("Stopped, ten ten.", stuck.Pump(null, "Stopped, ten ten.", T(25.5), new Speaker().At(25.5), () => true),
            "and no longer: a stuck device still gives the clock back");

        // Without the screen reader's word, the words are timed at 180 a minute: 11.2 seconds.
        var timed = new FieldActivityLatestLineDelivery();
        timed.NoteOtherSpeech(details, T(0));
        Equal(null, timed.Pump("Stopped, ten ten.", "Stopped, ten ten.", T(11.1), new Speaker().At(11.1), () => null), "timed: not yet");
        Equal("Stopped, ten ten.", timed.Pump(null, "Stopped, ten ten.", T(11.3), new Speaker().At(11.3), () => null), "timed: then");
    }

    /// <summary>A short line spoken while a long one is still protected does not cut the long one's wait short.</summary>
    private static void OverlappingSpeechIsWaitedForInFull()
    {
        var delivery = new FieldActivityLatestLineDelivery();
        delivery.NoteOtherSpeech("What is it you wish, seeker of knowledge? Speed up.", T(0));
        delivery.NoteOtherSpeech("[OK] Stop!", T(0.5));
        Equal(null, delivery.Pump("Stopped, ten ten.", "Stopped, ten ten.", T(3.0), new Speaker().At(3.0), () => null),
            "the earlier, longer line is still being said");
        Equal("Stopped, ten ten.", delivery.Pump(null, "Stopped, ten ten.", T(3.4), new Speaker().At(3.4), () => null),
            "and is waited for to its end");
    }

    /// <summary>The same rules the Reloaded and Steam 2026 hosts apply, through the shared host helper.</summary>
    private sealed class Host
    {
        public FieldActivityReadout Readout { get; } = new();
        public FieldActivityLatestLineDelivery Delivery { get; } = new();
        public List<string> Said { get; } = [];
        public bool Foreground { get; set; } = true;
        public bool SpeechEnabled { get; set; } = true;
        public Action? DuringSpeak { get; set; }

        /// <summary>One host tick over the clock with its long hand at <paramref name="longBearing"/>, short hand at ten.</summary>
        public void Tick(double seconds, int longBearing, int fieldId = 607)
        {
            if (!FieldActivityClockHost.MayObserve(fieldId, Foreground, Readout, Delivery))
            {
                return;
            }

            var observation = new FieldActivityObservation(fieldId, 0, 0, 0, 26, false, 618,
                new FieldNavigationControlTransform(0), true,
                [Hand(21, longBearing), Hand(22, 170), Hand(23, 64)],
                new Dictionary<int, FieldActivityWaitState>(), _ => true, _ => false, 0);
            var cue = Readout.Observe(observation, T(seconds));
            _ = FieldActivityClockHost.Deliver(cue, Readout, Delivery, T(seconds),
                () => Foreground, () => SpeechEnabled,
                text =>
                {
                    DuringSpeak?.Invoke();
                    Said.Add(text);
                    return true;
                },
                () => null);
        }

        private static FieldActivityModelReading Hand(int entityId, int bearing) =>
            new(entityId, FieldActivityReadStatus.Visible, new FieldActivityModelState(true, true, 0, 0, 0, 0, bearing));
    }

    /// <summary>
    /// Losing focus: nothing is spoken in the background, what was owed is dropped and the hands
    /// are watched afresh on return - so an aligned look then is not a stop. Focus lost between
    /// deciding to speak and speaking is not delivery either.
    /// </summary>
    private static void TheHostSpeaksTheClockOnlyWithFocus()
    {
        var host = new Host();
        host.Tick(0, 86);
        host.Tick(0.4, 86);
        Equal("Stopped, ten ten.", string.Join("|", host.Said), "with focus");
        host.Tick(2.0, 76);
        host.Said.Clear();
        host.Foreground = false;
        host.Tick(2.1, 64);
        host.Tick(3.0, 64);
        Equal(0, host.Said.Count, "nothing in the background");
        Equal(false, host.Delivery.HasPending, "and nothing owed from before");
        host.Foreground = true;
        host.Tick(3.1, 42);
        Equal(0, host.Said.Count, "back in focus, one aligned look during a spin claims nothing");
        host.Tick(3.2, 22);
        Equal("Moving, ten twenty-five.", string.Join("|", host.Said), "and the spin going on is movement");

        // Focus lost at the very moment of speaking - after the tick's own focus check - is
        // not delivery; the next tick, in the background, drops the reading.
        var readout = new FieldActivityReadout();
        var delivery = new FieldActivityLatestLineDelivery();
        var clock = new FieldActivityObservation(607, 0, 0, 0, 26, false, 618,
            new FieldNavigationControlTransform(0), true,
            [Visible(21, 86), Visible(22, 170), Visible(23, 64)],
            new Dictionary<int, FieldActivityWaitState>(), _ => true, _ => false, 0);
        _ = readout.Observe(clock, T(0));
        var cue = readout.Observe(clock, T(0.4));
        Equal("Stopped, ten ten.", cue.Speech, "a reading is due");
        var spoken = new List<string>();
        _ = FieldActivityClockHost.Deliver(cue, readout, delivery, T(0.4), () => false, () => true,
            text => { spoken.Add(text); return true; }, () => null);
        Equal(0, spoken.Count, "focus gone at the moment of speaking: not spoken");
        Equal(true, delivery.HasPending, "and not taken for said");
        Equal(false, FieldActivityClockHost.MayObserve(607, false, readout, delivery), "the next tick in the background");
        Equal(false, delivery.HasPending, "drops it");
    }

    private static FieldActivityModelReading Visible(int entityId, int bearing) =>
        new(entityId, FieldActivityReadStatus.Visible, new FieldActivityModelState(true, true, 0, 0, 0, 0, bearing));

    /// <summary>
    /// Speech turned off: the clock is silent, keeps only its current time owed, and on speech
    /// coming back says the time as it is then - not what it would have said while muted.
    /// </summary>
    private static void TheHostKeepsOnlyTheCurrentTimeWhileMuted()
    {
        var host = new Host { SpeechEnabled = false };
        host.Tick(0, 86);
        host.Tick(0.4, 86);
        host.Tick(2.0, 76);
        host.Tick(2.1, 64);
        host.Tick(2.6, 64);
        Equal(0, host.Said.Count, "silent while muted");
        Equal(true, host.Delivery.HasPending, "the current time is owed");
        host.SpeechEnabled = true;
        host.Tick(3.2, 64);
        Equal("Stopped, ten fifteen.", string.Join("|", host.Said), "on unmute, the clock as it is now, once");
        host.Tick(4.0, 64);
        Equal(1, host.Said.Count, "and nothing more");
    }

    /// <summary>The host helper does not stop any other room from being watched, with or without focus.</summary>
    private static void TheHostLeavesOtherRoomsAlone()
    {
        foreach (var field in new[] { 606, 610, 709, 402 })
        {
            Equal(true, FieldActivityClockHost.MayObserve(field, false, new FieldActivityReadout(), new FieldActivityLatestLineDelivery()),
                $"field {field} is watched as before, in the background too");
        }
    }

    private sealed class Speaker
    {
        public List<(double At, string Text)> Said { get; } = [];
        public bool Accepts { get; set; } = true;
        public bool Throws { get; set; }
        public int Attempts { get; private set; }
        public Action<string>? OnDelivered { get; set; }

        public Func<string, bool> At(double seconds) => text =>
        {
            Attempts++;
            if (Throws) throw new InvalidOperationException("Prism did not accept the speech request.");
            if (!Accepts) return false;
            Said.Add((seconds, text));
            OnDelivered?.Invoke(text);
            return true;
        };
    }

    private static DateTime T(double seconds) => Epoch.AddSeconds(seconds);

    private static void OnlyTheNewestReadingIsSaid()
    {
        var delivery = new FieldActivityLatestLineDelivery();
        var speaker = new Speaker();
        delivery.NoteOtherSpeech("The Guardian speaks.", T(0));
        // Two readings while the Guardian is being spoken: neither goes yet.
        _ = delivery.Pump("Moving, ten ten.", "Moving, ten ten.", T(0.1), speaker.At(0.1), () => true);
        _ = delivery.Pump("Moving, about ten twenty.", "Moving, about ten twenty.", T(0.5), speaker.At(0.5), () => true);
        // Nothing new is offered, but the clock has moved on; the Guardian has finished.
        Equal("Moving, about ten thirty.",
            delivery.Pump(null, "Moving, about ten thirty.", T(1.0), speaker.At(1.0), () => false),
            "what is said is the clock now, not the first reading that was owed");
        Equal(1, speaker.Said.Count, "one reading, not a queue of three");
        Equal(false, delivery.HasPending, "and nothing is owed after it");
    }

    private static void TheGuardiansWordsAreNotCutOff()
    {
        var delivery = new FieldActivityLatestLineDelivery();
        var speaker = new Speaker();
        var speaking = true;
        delivery.NoteOtherSpeech("[OK] Stop!", T(0));
        Equal(null, delivery.Pump("Moving, about four twenty.", "Moving, about four twenty.", T(0.05), speaker.At(0.05), () => speaking),
            "not while the window's words are still being said");
        Equal(null, delivery.Pump(null, "Moving, about five twenty.", T(0.6), speaker.At(0.6), () => speaking), "nor a moment later");
        speaking = false;
        Equal("Moving, about five twenty.", delivery.Pump(null, "Moving, about five twenty.", T(0.7), speaker.At(0.7), () => speaking),
            "as soon as the screen reader has finished them");
        // A screen reader that says it has stopped before it could have started is not believed.
        var early = new FieldActivityLatestLineDelivery();
        early.NoteOtherSpeech("Leave it up to the Guard.", T(0));
        Equal(null, early.Pump("Stopped, ten ten.", "Stopped, ten ten.", T(0.1), new Speaker().At(0.1), () => false),
            "the words may not have started yet");
        // And a screen reader that never stops still gives the clock back in the end: five
        // words are believed for the minimum ceiling of 6 seconds or 5 words at 100 a minute
        // plus 5 seconds, whichever is longer - 8 seconds.
        var stuck = new FieldActivityLatestLineDelivery();
        stuck.NoteOtherSpeech("I am the Time Guardian.", T(0));
        Equal(null, stuck.Pump("Stopped, ten ten.", "Stopped, ten ten.", T(7.9), new Speaker().At(7.9), () => true), "still speaking");
        Equal("Stopped, ten ten.", stuck.Pump(null, "Stopped, ten ten.", T(8.0), new Speaker().At(8.0), () => true),
            "but never held longer than that line's own ceiling");
    }

    private static void WithoutTheScreenReadersWordTheWordsAreTimed()
    {
        var delivery = new FieldActivityLatestLineDelivery();
        var speaker = new Speaker();
        // Ten words at a slow 180 words a minute: about 3.3 seconds.
        delivery.NoteOtherSpeech("What is it you wish, seeker of knowledge? Speed up.", T(0));
        Equal(null, delivery.Pump("Stopped, ten ten.", "Stopped, ten ten.", T(3.2), speaker.At(3.2), () => null), "not yet");
        Equal("Stopped, ten ten.", delivery.Pump(null, "Stopped, ten ten.", T(3.4), speaker.At(3.4), () => null), "then");
        // Two words are still given a moment.
        var shortLine = new FieldActivityLatestLineDelivery();
        shortLine.NoteOtherSpeech("[OK] Stop!", T(0));
        Equal(null, shortLine.Pump("Moving, ten ten.", "Moving, ten ten.", T(0.7), new Speaker().At(0.7), null), "a short line");
        Equal("Moving, ten ten.", shortLine.Pump(null, "Moving, ten ten.", T(0.8), new Speaker().At(0.8), null), "is over soon");
    }

    /// <summary>
    /// The "[OK] Stop!" window stays open for as long as the spin runs. It was spoken once, and
    /// once its words are done the moving readings go on being said while it stays open.
    /// </summary>
    private static void AnOpenWindowDoesNotSilenceTheClock()
    {
        var delivery = new FieldActivityLatestLineDelivery();
        var speaker = new Speaker();
        var speaking = true;
        delivery.NoteOtherSpeech("[OK] Stop!", T(0));
        _ = delivery.Pump("Moving, about four twenty.", "Moving, about four twenty.", T(0.3), speaker.At(0.3), () => speaking);
        speaking = false;
        _ = delivery.Pump(null, "Moving, about four twenty.", T(1.0), speaker.At(1.0), () => speaking);
        // The clock's own reading now plays; the screen reader is busy with it, not the window.
        speaking = true;
        Equal("Moving, about eight forty.",
            delivery.Pump("Moving, about eight forty.", "Moving, about eight forty.", T(2.5), speaker.At(2.5), () => speaking),
            "the next reading replaces the clock's own last one");
        Equal("Moving, about one oh five.",
            delivery.Pump("Moving, about one oh five.", "Moving, about one oh five.", T(4.0), speaker.At(4.0), () => speaking),
            "and so on, for as long as the spin runs");
        Equal(3, speaker.Said.Count, "every reading after the window's words was said");
    }

    /// <summary>
    /// On the Steam 2026 runtime the clock's own line comes back as a delivered line. That is
    /// the clock itself, and must not hold back the clock's next reading.
    /// </summary>
    private static void TheClocksOwnReadingIsNotProtected()
    {
        var delivery = new FieldActivityLatestLineDelivery();
        var speaker = new Speaker();
        speaker.OnDelivered = text => delivery.NoteOtherSpeech(text, T(0));
        _ = delivery.Pump("Moving, ten ten.", "Moving, ten ten.", T(0), speaker.At(0), () => true);
        Equal("Moving, about ten thirty.",
            delivery.Pump("Moving, about ten thirty.", "Moving, about ten thirty.", T(0.1), speaker.At(0.1), () => true),
            "the clock's own line is replaced, not waited for");
    }

    private static void ARefusedReadingIsRetriedOnlyWhileCurrent()
    {
        var delivery = new FieldActivityLatestLineDelivery();
        var speaker = new Speaker { Accepts = false };
        Equal(null, delivery.Pump("Moving, ten ten.", "Moving, ten ten.", T(0), speaker.At(0)), "muted: not said");
        Equal(true, delivery.HasPending, "and still owed");
        _ = delivery.Pump(null, "Moving, ten ten.", T(0.2), speaker.At(0.2));
        _ = delivery.Pump(null, "Moving, ten ten.", T(0.4), speaker.At(0.4));
        Equal(1, speaker.Attempts, "a refusing speaker is not asked on every tick");
        _ = delivery.Pump(null, "Moving, ten ten.", T(0.5), speaker.At(0.5));
        Equal(2, speaker.Attempts, "but is asked again after the retry interval");
        speaker.Accepts = true;
        Equal("Stopped, ten fifteen.", delivery.Pump(null, "Stopped, ten fifteen.", T(1.0), speaker.At(1.0)),
            "unmuted, it is the clock as it is now that is said");
        // Refused, then the clock is no longer being read: nothing stale is kept.
        speaker.Accepts = false;
        _ = delivery.Pump("Moving, ten fifteen.", "Moving, ten fifteen.", T(2.0), speaker.At(2.0));
        _ = delivery.Pump(null, null, T(2.1), speaker.At(2.1));
        speaker.Accepts = true;
        Equal(null, delivery.Pump(null, "Stopped, ten twenty.", T(3.0), speaker.At(3.0)),
            "a reading dropped because nothing was current is not revived");
        Equal(false, delivery.HasPending, "nothing is owed");
    }

    private static void AThrowingSpeakerIsARefusal()
    {
        var delivery = new FieldActivityLatestLineDelivery();
        var speaker = new Speaker { Throws = true };
        Equal(null, delivery.Pump("Stopped, ten ten.", "Stopped, ten ten.", T(0), speaker.At(0)), "Prism refused");
        Equal(true, delivery.HasPending, "the reading is still owed");
        speaker.Throws = false;
        Equal("Stopped, ten ten.", delivery.Pump(null, "Stopped, ten ten.", T(0.6), speaker.At(0.6)), "and said once it can be");
    }

    private static void LeavingTheRoomOwesNothing()
    {
        var delivery = new FieldActivityLatestLineDelivery();
        delivery.NoteOtherSpeech("Temple of the Ancients.", T(0));
        _ = delivery.Pump("Stopped, ten ten.", "Stopped, ten ten.", T(0.1), new Speaker().At(0.1), () => true);
        delivery.Reset();
        Equal(false, delivery.HasPending, "reset owes nothing");
        Equal(null, delivery.Pump(null, "Stopped, ten ten.", T(9), new Speaker().At(9), () => false),
            "and nothing is said later on its account");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
