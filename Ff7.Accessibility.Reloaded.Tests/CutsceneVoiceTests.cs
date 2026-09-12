using System.Security.Cryptography;
using System.Text;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Scene descriptions read in the recorded voice rather than by the screen reader.
///
/// <para>What this holds is that the voice changes and nothing else does. A cue is
/// still only consumed when somebody actually said it; a missing, disabled or broken
/// recording still falls back to speech rather than leaving the scene undescribed;
/// two descriptions never play at once; the game's own words still take the device
/// away; and the dialogue window is now held for exactly as long as a recording
/// really lasts instead of for a guess made from its word count.</para>
/// </summary>
internal static class CutsceneVoiceTests
{
    private const int Field = 496;
    private const string Spoken = "Barret slams a fist into the wall.";
    private const string Second = "Tifa looks away from the window.";
    private const string Unrecorded = "Nobody recorded this one.";
    private static readonly DateTime Start = new(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        TheFileNameIsTheHashOfTheExactText();
        AManifestEntryWithoutADurationIsSkipped();
        ARecordedDescriptionIsPlayedInsteadOfSpoken();
        AMissingOrRefusedRecordingFallsBackToSpeech();
        ADescriptionIsNeverPlayedOverAnother();
        AClipIsOnlyStoppedByItsOwnLifetime();
        ASecondDescriptionWaitsInTheQueueRatherThanBeingSpokenOver();
        TheDialogueWindowIsHeldForTheClipsRealLength();
        TheDeliveryStepConsumesACueOnlyWhenSomebodySaidIt();
    }

    private static void TheFileNameIsTheHashOfTheExactText()
    {
        // The name is the whole binding between a description and its recording, so a
        // stray space has to produce a different file rather than quietly matching.
        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Spoken))).ToLowerInvariant() + ".ogg";
        Equal(expected, CutsceneVoiceManifest.FileNameFor(Spoken), "the file name is the text's hash");
        Equal(false, CutsceneVoiceManifest.FileNameFor(Spoken) == CutsceneVoiceManifest.FileNameFor(Spoken + " "),
            "a trailing space is a different recording");

        var manifest = CutsceneVoiceManifest.Parse(Manifest((Spoken, 2.5d)));
        Equal(1, manifest.Count, "the manifest holds the entry");
        Equal(true, manifest.TryGet(Spoken, out var clip), "the exact text finds its clip");
        Equal(expected, clip.FileName, "and the clip names the hashed file");
        Equal(2.5d, clip.Duration.TotalSeconds, "with its own duration");
        Equal(false, manifest.TryGet(Spoken.ToUpperInvariant(), out _),
            "nothing is normalised: different words are a different recording");
        Equal(false, manifest.TryGet(null, out _), "and no text finds nothing");
    }

    private static void AManifestEntryWithoutADurationIsSkipped()
    {
        // The duration decides how long dialogue is held off. A missing or nonsense
        // one is not guessed at - that description is spoken instead.
        var manifest = CutsceneVoiceManifest.Parse(
            $$"""
            {
              "sourceHash": "abc123",
              "entries": [
                { "text": {{Quote(Spoken)}}, "file": "a.ogg", "duration_seconds": 3.0 },
                { "text": {{Quote("no duration")}}, "file": "b.ogg" },
                { "text": {{Quote("zero duration")}}, "file": "c.ogg", "duration_seconds": 0 },
                { "text": "", "file": "d.ogg", "duration_seconds": 1.0 }
              ]
            }
            """);
        Equal(1, manifest.Count, "only the usable entry is kept");
        Equal("abc123", manifest.SourceHash, "the source hash is carried through");
        Equal(false, manifest.TryGet("no duration", out _), "an entry with no duration is skipped");
        Equal(false, manifest.TryGet("zero duration", out _), "and so is a zero one");

        // Nothing here throws, because a broken manifest must degrade to speech
        // rather than take the mod down.
        Equal(0, CutsceneVoiceManifest.Parse("{ not json").Count, "malformed json yields nothing");
        Equal(0, CutsceneVoiceManifest.Parse("{}").Count, "an object with no entries yields nothing");
        Equal(0, CutsceneVoiceManifest.Parse(null).Count, "and neither does nothing at all");
    }

    private static void ARecordedDescriptionIsPlayedInsteadOfSpoken()
    {
        var output = new CountingOutput();
        var player = new CutsceneVoicePlayer(
            CutsceneVoiceManifest.Parse(Manifest((Spoken, 2.5d))), _ => output, _ => { });

        Equal(CutsceneVoiceResult.Played,
            player.TrySpeak(Spoken, CutsceneVoiceOwner.FieldAction, out var duration),
            "a recorded description plays");
        Equal(2.5d, duration.TotalSeconds, "and reports its own length");
        Equal(1, output.Starts, "exactly one clip started");
        Equal(true, player.IsPlaying, "and it is playing");

        // A description nobody recorded is not the player's business.
        Equal(CutsceneVoiceResult.Unavailable,
            player.TrySpeak(Unrecorded, CutsceneVoiceOwner.FieldAction, out _),
            "an unrecorded description is declined");
        Equal(1, output.Starts, "and starts nothing");

        player.Stop("the field changed");
        Equal(false, player.IsPlaying, "a boundary stops it");
        Equal(1, output.Stops, "exactly once");
    }

    private static void AMissingOrRefusedRecordingFallsBackToSpeech()
    {
        // Four ways the recording cannot be the answer. Each one has to decline so
        // the caller speaks, because a scene left undescribed is the worst outcome.
        var manifest = CutsceneVoiceManifest.Parse(Manifest((Spoken, 2.5d)));
        var cases = new (string What, Func<CutsceneVoiceClip, IFieldMovieNarrationOutput?> Factory)[]
        {
            ("the file is not installed", _ => null),
            ("the factory throws", _ => throw new FileNotFoundException("no such clip")),
            ("the device refuses to start", _ => new CountingOutput { RefuseStart = true }),
            ("the device throws while starting", _ => new CountingOutput { ThrowOnStart = true }),
        };

        foreach (var (what, factory) in cases)
        {
            var player = new CutsceneVoicePlayer(manifest, factory, _ => { });
            Equal(CutsceneVoiceResult.Unavailable,
                player.TrySpeak(Spoken, CutsceneVoiceOwner.FieldAction, out var duration),
                $"{what}: the player declines");
            Equal(TimeSpan.Zero, duration, $"{what}: and reports no length");
            Equal(false, player.IsPlaying, $"{what}: and nothing is playing");
        }

        // The control: the same manifest with a working device does play, so the four
        // refusals above are the factory's doing and not a broken lookup.
        var working = new CountingOutput();
        Equal(CutsceneVoiceResult.Played,
            new CutsceneVoicePlayer(manifest, _ => working, _ => { }).TrySpeak(Spoken, CutsceneVoiceOwner.FieldAction, out _),
            "the control - a working device plays it");
    }

    private static void ADescriptionIsNeverPlayedOverAnother()
    {
        var manifest = CutsceneVoiceManifest.Parse(Manifest((Spoken, 2.5d), (Unrecorded, 1.5d)));

        // Its own clip still playing: the second description waits for the speaker
        // rather than doubling up on the device.
        var output = new CountingOutput();
        var player = new CutsceneVoicePlayer(manifest, _ => output, _ => { });
        Equal(CutsceneVoiceResult.Played,
            player.TrySpeak(Spoken, CutsceneVoiceOwner.FieldAction, out _),
            "the first description plays");

        // Busy, not unavailable: there is a recording of these words, so the answer
        // is "not yet" rather than "never". The caller keeps the cue.
        Equal(CutsceneVoiceResult.Busy,
            player.TrySpeak(Unrecorded, CutsceneVoiceOwner.FieldAction, out _),
            "a second must not play over it");
        Equal(1, output.Starts, "one clip at a time");

        // Once it has finished, the next may play.
        output.Finish();
        Equal(CutsceneVoiceResult.Played,
            player.TrySpeak(Unrecorded, CutsceneVoiceOwner.FieldAction, out _),
            "and follows once the first has finished");
        Equal(2, output.Starts, "each in its turn");

        // A film's own recording owns the same device. The field description waits
        // for it rather than being read out over it in the other voice.
        var filmIsPlaying = true;
        var busy = new CountingOutput();
        var deferring = new CutsceneVoicePlayer(manifest, _ => busy, _ => { }, () => filmIsPlaying);
        Equal(CutsceneVoiceResult.Busy,
            deferring.TrySpeak(Spoken, CutsceneVoiceOwner.FieldAction, out _),
            "a film recording keeps the device");
        Equal(0, busy.Starts, "and nothing else is started");
        filmIsPlaying = false;
        Equal(CutsceneVoiceResult.Played,
            deferring.TrySpeak(Spoken, CutsceneVoiceOwner.FieldAction, out _),
            "once the film is done the description plays");
    }

    /// <summary>
    /// A clip is stopped by the lifetime it belongs to and by nothing else.
    ///
    /// <para>The field changing ends a field description, because that description
    /// was about the room the player has left. It must not end a film's cue: one film
    /// runs across several fields, and the sentence is about the film.</para>
    /// </summary>
    private static void AClipIsOnlyStoppedByItsOwnLifetime()
    {
        var manifest = CutsceneVoiceManifest.Parse(Manifest((Spoken, 2.5d)));

        var fieldOutput = new CountingOutput();
        var fieldClip = new CutsceneVoicePlayer(manifest, _ => fieldOutput, _ => { });
        Equal(CutsceneVoiceResult.Played,
            fieldClip.TrySpeak(Spoken, CutsceneVoiceOwner.FieldAction, out _),
            "a field description plays");
        fieldClip.StopIfOwnedBy(CutsceneVoiceOwner.FilmCue, "a film's cues were dropped");
        Equal(true, fieldClip.IsPlaying, "a film's lifetime does not end a field description");
        fieldClip.StopIfOwnedBy(CutsceneVoiceOwner.FieldAction, "the field changed");
        Equal(false, fieldClip.IsPlaying, "its own lifetime does");
        Equal(1, fieldOutput.Stops, "exactly once");

        var cueOutput = new CountingOutput();
        var filmClip = new CutsceneVoicePlayer(manifest, _ => cueOutput, _ => { });
        Equal(CutsceneVoiceResult.Played,
            filmClip.TrySpeak(Spoken, CutsceneVoiceOwner.FilmCue, out _),
            "a film cue plays");
        filmClip.StopIfOwnedBy(CutsceneVoiceOwner.FieldAction, "the field changed");
        Equal(true, filmClip.IsPlaying,
            "a field change must not end a cue of a film that is still running");
        filmClip.StopIfOwnedBy(CutsceneVoiceOwner.FilmCue, "the film ended");
        Equal(false, filmClip.IsPlaying, "the film ending does");

        // Whatever is playing, an unconditional stop takes it: a reset, an unload and
        // the game putting its own words on screen are not about ownership.
        var anyOutput = new CountingOutput();
        var any = new CutsceneVoicePlayer(manifest, _ => anyOutput, _ => { });
        Equal(CutsceneVoiceResult.Played,
            any.TrySpeak(Spoken, CutsceneVoiceOwner.FilmCue, out _),
            "the control - something is playing");
        any.Stop("unloaded");
        Equal(false, any.IsPlaying, "an unconditional stop takes anything");
    }

    private static void TheDialogueWindowIsHeldForTheClipsRealLength()
    {
        // A recording's length is known, so it is used as it stands. The word-count
        // estimate exists only because nobody can know how long a screen reader takes.
        var priority = new FieldCutsceneSpeechPriority();
        priority.BeginNarration(Field, TimeSpan.FromSeconds(2.5d), Start);
        Equal(true, priority.ShouldQueueDialogue(Field, Start.AddSeconds(2)),
            "dialogue waits while the clip is still playing");
        Equal(false, priority.ShouldQueueDialogue(Field, Start.AddSeconds(2.6d)),
            "and stops waiting when it has finished");
        Equal(false, priority.ShouldQueueDialogue(497, Start.AddSeconds(1)),
            "another field is not held at all");

        // A four-second clip of three words would have been cut off at the estimate's
        // 1.8-second floor; a half-second one would have held the window for 1.8.
        priority.BeginNarration(Field, TimeSpan.FromSeconds(4d), Start);
        Equal(true, priority.ShouldQueueDialogue(Field, Start.AddSeconds(3.5d)),
            "a long clip of few words is not cut short by the estimate's floor");
        priority.BeginNarration(Field, TimeSpan.FromSeconds(0.5d), Start);
        Equal(false, priority.ShouldQueueDialogue(Field, Start.AddSeconds(0.6d)),
            "and a short one does not hold the window open after it has finished");

        // The estimating overload is unchanged for speech.
        priority.BeginNarration(Field, "four words go here", Start);
        Equal(true, priority.ShouldQueueDialogue(Field, Start.AddSeconds(1)),
            "spoken descriptions still use the word-count estimate");
    }

    private static void TheDeliveryStepConsumesACueOnlyWhenSomebodySaidIt()
    {
        // The rule that matters most: a cue leaves the queue when it has been said,
        // by either voice, and stays when it has not. This is the shape both runtimes
        // wire the player into.
        var manifest = CutsceneVoiceManifest.Parse(Manifest((Spoken, 2.5d)));
        var clipOutput = new CountingOutput();
        var player = new CutsceneVoicePlayer(manifest, _ => clipOutput, _ => { });
        var spokenAloud = new List<string>();
        var speakerAccepts = true;

        // The production decision, not a re-statement of it: this is the same helper
        // both runtimes' SpeakDescription calls.
        bool SpeakDescription(string text) =>
            CutsceneVoiceSpeaker.Deliver(
                player,
                text,
                CutsceneVoiceOwner.FieldAction,
                aloud =>
                {
                    if (!speakerAccepts)
                    {
                        return false;
                    }

                    spokenAloud.Add(aloud);
                    return true;
                }).Spoken;

        var queue = new FieldCutsceneDescriptionDeliveryQueue();
        var delivery = new FieldCutsceneDescriptionDelivery(queue);
        var recorded = new FieldCutsceneDescriptionCue(
            Field, 0, 0, 10, Spoken, FieldOpcodeAddressResolver.OpcodeRequestIndex);
        var unrecorded = new FieldCutsceneDescriptionCue(
            Field, 0, 0, 20, Unrecorded, FieldOpcodeAddressResolver.OpcodeRequestIndex);

        // Recorded: the clip plays and the screen reader is not used.
        queue.Enqueue(recorded);
        Equal(FieldCutsceneDeliveryOutcome.Spoken,
            delivery.Deliver(Field, _ => FieldMovieNarrationStartResult.NotDescribed,
                SpeakDescription, _ => { }, out _),
            "a recorded description is delivered");
        Equal(1, clipOutput.Starts, "by the recording");
        Equal(0, spokenAloud.Count, "and not by the screen reader");
        Equal(0, queue.Count, "and the cue is consumed");

        // Unrecorded: the screen reader says it, and the cue is still consumed.
        clipOutput.Finish();
        queue.Enqueue(unrecorded);
        Equal(FieldCutsceneDeliveryOutcome.Spoken,
            delivery.Deliver(Field, _ => FieldMovieNarrationStartResult.NotDescribed,
                SpeakDescription, _ => { }, out _),
            "an unrecorded description is still delivered");
        Equal(1, spokenAloud.Count, "by the screen reader");
        Equal(0, queue.Count, "and consumed");

        // Neither: nobody said it, so it stays. This is the silent-dequeue guard.
        speakerAccepts = false;
        queue.Enqueue(unrecorded);
        Equal(FieldCutsceneDeliveryOutcome.Refused,
            delivery.Deliver(Field, _ => FieldMovieNarrationStartResult.NotDescribed,
                SpeakDescription, _ => { }, out _),
            "a description nobody said is refused");
        Equal(1, queue.Count, "and stays at the head of the queue");
    }

    /// <summary>
    /// The seam this whole distinction exists for, at the x86 delivery step rather
    /// than in the player alone.
    ///
    /// <para>Two descriptions fire in the same field a frame apart, which the story
    /// scripts do constantly. The first is playing; the second must stay in the queue
    /// and reach the player untouched on a later tick. Reading it out through the
    /// screen reader instead would put a second voice over the first <em>and</em>
    /// spend the cue, so those words would never be heard in the recorded voice at
    /// all - a permanent loss for a condition that clears in about two seconds.</para>
    /// </summary>
    private static void ASecondDescriptionWaitsInTheQueueRatherThanBeingSpokenOver()
    {
        var manifest = CutsceneVoiceManifest.Parse(Manifest((Spoken, 2.5d), (Second, 1.5d)));
        var clip = new CountingOutput();
        var filmIsPlaying = false;
        var player = new CutsceneVoicePlayer(
            manifest, _ => clip, _ => { }, () => filmIsPlaying);
        var spokenAloud = new List<string>();

        bool SpeakDescription(string text) =>
            CutsceneVoiceSpeaker.Deliver(
                player,
                text,
                CutsceneVoiceOwner.FieldAction,
                aloud => { spokenAloud.Add(aloud); return true; }).Spoken;

        var queue = new FieldCutsceneDescriptionDeliveryQueue();
        var delivery = new FieldCutsceneDescriptionDelivery(queue);
        queue.Enqueue(new FieldCutsceneDescriptionCue(
            Field, 0, 0, 10, Spoken, FieldOpcodeAddressResolver.OpcodeRequestIndex));
        queue.Enqueue(new FieldCutsceneDescriptionCue(
            Field, 0, 0, 20, Second, FieldOpcodeAddressResolver.OpcodeRequestIndex));

        Equal(FieldCutsceneDeliveryOutcome.Spoken, Deliver(delivery, SpeakDescription),
            "the first description is delivered");
        Equal(1, clip.Starts, "in the recorded voice");
        Equal(1, queue.Count, "the second is still queued");

        // The first clip is still going. This is the whole case.
        Equal(FieldCutsceneDeliveryOutcome.Refused, Deliver(delivery, SpeakDescription),
            "the second is refused while the first is still playing");
        Equal(0, spokenAloud.Count, "and is not read out by the screen reader");
        Equal(1, clip.Starts, "and nothing was started over the first clip");
        Equal(1, queue.Count, "it stays at the head of the queue");

        // A film's own recording holding the device is the same answer: waiting, not
        // falling back. The main movie has its own voice and the description is not
        // read over it.
        filmIsPlaying = true;
        clip.Finish();
        Equal(FieldCutsceneDeliveryOutcome.Refused, Deliver(delivery, SpeakDescription),
            "a film recording on the device refuses the same way");
        Equal(0, spokenAloud.Count, "still nothing is read out");
        Equal(1, queue.Count, "and the cue is still there");

        // The device frees up, and the cue is delivered in the voice it was meant to
        // have - late, but in the right voice and exactly once.
        filmIsPlaying = false;
        Equal(FieldCutsceneDeliveryOutcome.Spoken, Deliver(delivery, SpeakDescription),
            "a later tick delivers it");
        Equal(2, clip.Starts, "in the recorded voice");
        Equal(0, spokenAloud.Count, "never through the screen reader");
        Equal(0, queue.Count, "and now it is spent");

        // The control, so the refusals above are the busy device and not a queue that
        // has stopped delivering: an unrecorded description still reaches the screen
        // reader, because a scene left undescribed is the worst outcome.
        clip.Finish();
        queue.Enqueue(new FieldCutsceneDescriptionCue(
            Field, 0, 0, 30, Unrecorded, FieldOpcodeAddressResolver.OpcodeRequestIndex));
        Equal(FieldCutsceneDeliveryOutcome.Spoken, Deliver(delivery, SpeakDescription),
            "the control - an unrecorded description still falls back to speech");
        Equal(1, spokenAloud.Count, "and is read out");
    }

    private static FieldCutsceneDeliveryOutcome Deliver(
        FieldCutsceneDescriptionDelivery delivery, Func<string, bool> speak) =>
        delivery.Deliver(
            Field, _ => FieldMovieNarrationStartResult.NotDescribed, speak, _ => { }, out _);

    private static string Quote(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    private static string Manifest(params (string Text, double Seconds)[] entries) =>
        "{ \"entries\": [" + string.Join(",", entries.Select(entry =>
            $"{{ \"text\": {Quote(entry.Text)}, " +
            $"\"file\": \"{CutsceneVoiceManifest.FileNameFor(entry.Text)}\", " +
            $"\"duration_seconds\": {entry.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}")) +
        "] }";

    private sealed class CountingOutput : IFieldMovieNarrationOutput
    {
        private bool playing;

        public bool RefuseStart { get; init; }

        public bool ThrowOnStart { get; init; }

        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public bool IsPlaying => playing;

        /// <summary>The clip reaching its own end, which is not a stop.</summary>
        public void Finish() => playing = false;

        public bool Start(string reason)
        {
            if (ThrowOnStart) { throw new InvalidOperationException("device refused to open"); }
            if (RefuseStart) { return false; }
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
                $"Cutscene voice: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
