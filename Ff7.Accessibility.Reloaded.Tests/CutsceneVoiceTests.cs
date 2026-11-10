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
        RecordingCompletionAndTimeoutReleaseDialogue();
        TheFileNameIsTheHashOfTheExactText();
        AManifestEntryWithoutADurationIsSkipped();
        ARecordedDescriptionIsPlayedInsteadOfSpoken();
        AMissingOrRefusedRecordingFallsBackToSpeech();
        ADescriptionIsNeverPlayedOverAnother();
        AClipIsOnlyStoppedByItsOwnLifetime();
        ASecondDescriptionWaitsInTheQueueRatherThanBeingSpokenOver();
        TheDialogueWindowIsHeldForTheClipsRealLength();
        TheDeliveryStepConsumesACueOnlyWhenSomebodySaidIt();
        AStartedDescriptionFinishesOverTheScriptedNameEntryScreen();
        OnlyAPlayingFieldDescriptionInsideItsOwnLengthIsHandedOver();
        AHandedOverDescriptionStillEndsAtEveryDiscontinuity();
    }

    private const string JessieDoor =
        "Jessie works the controls. The two heavy doors unlock and slide apart.";

    /// <summary>
    /// Issue 57, at the decision both runtimes share and in the order the x86 monitor
    /// loop takes it: on every tick the field module is not current it calls
    /// <see cref="CutsceneVoiceTransition.LeaveFieldModule"/>, then its own field reset
    /// stops whatever field description is left and resets the dialogue priority.
    ///
    /// <para>md1_1 queues the 3.4-second door description behind the gate description
    /// and Barret's dialog 0x29; it starts as that dialog closes, and eight frames later
    /// the script's MENU 00 06 01 opens Barret's name entry - module 5, field request 6.
    /// The sentence used to be cut off there with three seconds still to say.</para>
    /// </summary>
    private static void AStartedDescriptionFinishesOverTheScriptedNameEntryScreen()
    {
        var clock = Start;
        var output = new CountingOutput();
        var log = new List<string>();
        var player = new CutsceneVoicePlayer(
            CutsceneVoiceManifest.Parse(Manifest((JessieDoor, 3.4d))), _ => output, log.Add, null, () => clock);
        var priority = new FieldCutsceneSpeechPriority();
        priority.AttachRecordingProbe(() => player.IsPlaying);

        Equal(CutsceneVoiceResult.Played,
            player.TrySpeak(JessieDoor, CutsceneVoiceOwner.FieldAction, out var length),
            "the door description plays");
        priority.BeginNarration(117, length, clock);

        for (var tick = 0; tick < 40; tick++)
        {
            clock = Start.AddMilliseconds(270 + tick * 50);
            CutsceneVoiceTransition.LeaveFieldModule(
                player, NameEntryStateReader.NameEntryModule, CutsceneVoiceTransition.ScriptedNameEntryRequest);
            player.StopIfOwnedBy(CutsceneVoiceOwner.FieldAction, "the field description state was reset");
            priority.Reset();
            Equal(true, player.IsPlaying, $"tick {tick}: the description is still being heard over the name-entry screen");
            Equal(true, priority.ShouldDeferDialogueDelivery(clock), $"tick {tick}: and the screen's own words wait for it");
        }

        Equal(0, output.Stops, "nothing cut it off");
        Equal(true, player.IsContinuingAcrossTransition, "it now belongs to the transition, not the field");
        Equal(1, log.Count(line => line.Contains("continues across the name-entry screen", StringComparison.Ordinal)),
            "the hand-over is logged once");

        // A second description arriving meanwhile waits for it, in its own voice.
        Equal(CutsceneVoiceResult.Busy,
            player.TrySpeak(JessieDoor, CutsceneVoiceOwner.FieldAction, out _),
            "nothing else starts over it");

        output.Finish();
        Equal(false, player.IsContinuingAcrossTransition, "the recording reached its own end");
        Equal(false, priority.ShouldDeferDialogueDelivery(clock), "and the screen's words are released at once");
        Equal(0, output.Stops, "it was never stopped");
    }

    /// <summary>
    /// The hand-over is narrow on purpose. Only a field description that is really still
    /// playing, inside its own declared length, is handed over, and only to the
    /// script's own name-entry screen when the field module hands the screen away.
    /// </summary>
    private static void OnlyAPlayingFieldDescriptionInsideItsOwnLengthIsHandedOver()
    {
        // The request byte is what tells the script's name entry from everything else
        // module 5 shows.
        Equal(true, CutsceneVoiceTransition.IsScriptedNameEntry(5, 6), "MENU type 6 is the name-entry screen");
        Equal(false, CutsceneVoiceTransition.IsScriptedNameEntry(5, 9), "the player's own menu is not");
        Equal(false, CutsceneVoiceTransition.IsScriptedNameEntry(5, 7), "nor another scripted menu");
        Equal(false, CutsceneVoiceTransition.IsScriptedNameEntry(5, -1), "nor an unreadable request");
        Equal(false, CutsceneVoiceTransition.IsScriptedNameEntry(2, 6), "a battle is not, whatever the byte says");
        Equal(false, CutsceneVoiceTransition.IsScriptedNameEntry(1, 6), "and neither is the field itself");

        var manifest = CutsceneVoiceManifest.Parse(Manifest((JessieDoor, 3.4d), (Spoken, 20d)));
        var clock = Start;

        // Nothing playing: nothing to hand over.
        var idle = new CutsceneVoicePlayer(manifest, _ => new CountingOutput(), _ => { }, null, () => clock);
        Equal(false, idle.ContinueAcrossTransition("a test"), "nothing playing is never handed over");

        // A film's cue and a creature's description have lifetimes of their own.
        foreach (var owner in new[] { CutsceneVoiceOwner.FilmCue, CutsceneVoiceOwner.FieldCreature })
        {
            var output = new CountingOutput();
            var player = new CutsceneVoicePlayer(manifest, _ => output, _ => { }, null, () => clock);
            player.TrySpeak(JessieDoor, owner, out _);
            Equal(false, player.ContinueAcrossTransition("a test"), $"{owner}: is not handed over");
            player.StopIfOwnedBy(CutsceneVoiceOwner.SceneTransition, "a test");
            Equal(true, player.IsPlaying, $"{owner}: and the transition's lifetime cannot stop it");
            player.StopIfOwnedBy(owner, "its own lifetime ended");
            Equal(false, player.IsPlaying, $"{owner}: its own lifetime still does");
        }

        // A device still reporting playing well past the clip's own length is stuck, not
        // finishing, and is left to the field reset.
        var stuck = new CountingOutput();
        var overrun = new CutsceneVoicePlayer(manifest, _ => stuck, _ => { }, null, () => clock);
        clock = Start;
        overrun.TrySpeak(JessieDoor, CutsceneVoiceOwner.FieldAction, out _);
        clock = Start.AddSeconds(3.4d) + CutsceneVoicePlayer.TransitionOverrunAllowance + TimeSpan.FromMilliseconds(1);
        Equal(false, overrun.ContinueAcrossTransition("a test"), "a stuck clip is not handed over");
        overrun.StopIfOwnedBy(CutsceneVoiceOwner.FieldAction, "the field description state was reset");
        Equal(false, overrun.IsPlaying, "and the field reset ends it");

        // A clip with more to say than any description is not a sentence finishing.
        var longOutput = new CountingOutput();
        var tooLong = new CutsceneVoicePlayer(manifest, _ => longOutput, _ => { }, null, () => clock);
        clock = Start;
        tooLong.TrySpeak(Spoken, CutsceneVoiceOwner.FieldAction, out _);
        clock = Start.AddSeconds(1);
        Equal(false, tooLong.ContinueAcrossTransition("a test"), "a clip with nineteen seconds to go is not handed over");

        // Device latency: a clip just past its declared length, but within the allowance,
        // is still finishing.
        var lateOutput = new CountingOutput();
        var late = new CutsceneVoicePlayer(manifest, _ => lateOutput, _ => { }, null, () => clock);
        clock = Start;
        late.TrySpeak(JessieDoor, CutsceneVoiceOwner.FieldAction, out _);
        clock = Start.AddSeconds(4d);
        Equal(true, late.ContinueAcrossTransition("a test"), "a clip a moment past its length is still finishing");
        Equal(true, late.ContinueAcrossTransition("a test again"), "and asking again changes nothing");
    }

    /// <summary>
    /// Handed over is not unconditional. The player's own menu, any module that follows
    /// the name-entry screen, the host taking the device back and a film's own recording
    /// starting all still end the sentence - and leave nothing waiting behind it.
    /// </summary>
    private static void AHandedOverDescriptionStillEndsAtEveryDiscontinuity()
    {
        var cases = new (string What, bool HandedOver, Action<CutsceneVoicePlayer> Apply)[]
        {
            // Straight from the field: never handed over, and the field reset ends it.
            ("the player opened the menu instead", false, player =>
            {
                CutsceneVoiceTransition.LeaveFieldModule(player, NameEntryStateReader.NameEntryModule, 9);
                player.StopIfOwnedBy(CutsceneVoiceOwner.FieldAction, "the field description state was reset");
            }),
            ("a battle followed", true, player =>
                CutsceneVoiceTransition.LeaveFieldModule(player, 2, 0)),
            ("the title screen followed", true, player =>
                CutsceneVoiceTransition.LeaveFieldModule(player, 20, 0)),
            ("the window lost the foreground", true, player =>
                player.Stop("the game is no longer the foreground window")),
            ("the mod was suspended", true, player => player.Stop("mod suspended")),
            ("the mod was unloaded", true, player => player.Dispose()),
            // What a film's start asks of the device through its yield callback.
            ("a film's own recording started", true, player =>
            {
                player.StopIfOwnedBy(CutsceneVoiceOwner.FieldAction, "the film boogdemo.avi is starting");
                player.StopIfOwnedBy(CutsceneVoiceOwner.SceneTransition, "the film boogdemo.avi is starting");
            }),
        };

        foreach (var (what, handedOver, apply) in cases)
        {
            var output = new CountingOutput();
            var player = new CutsceneVoicePlayer(
                CutsceneVoiceManifest.Parse(Manifest((JessieDoor, 3.4d))), _ => output, _ => { });
            var priority = new FieldCutsceneSpeechPriority();
            priority.AttachRecordingProbe(() => player.IsPlaying);
            player.TrySpeak(JessieDoor, CutsceneVoiceOwner.FieldAction, out var length);
            priority.BeginNarration(117, length, Start);

            if (handedOver)
            {
                CutsceneVoiceTransition.LeaveFieldModule(
                    player, NameEntryStateReader.NameEntryModule, CutsceneVoiceTransition.ScriptedNameEntryRequest);
                Equal(true, player.IsContinuingAcrossTransition, $"{what}: the control - it was handed over");
            }

            apply(player);

            Equal(false, player.IsPlaying, $"{what}: the description stops");
            Equal(1, output.Stops, $"{what}: exactly once");
            Equal(false, priority.ShouldDeferDialogueDelivery(Start.AddSeconds(1)),
                $"{what}: and nothing waits behind it");
        }

        // The film case through the shared film tracker itself, which both runtimes use:
        // a film starting after the name-entry screen takes the device back before its
        // own recording opens.
        var clip = new CountingOutput();
        var filmOutput = new CountingOutput();
        CutsceneVoicePlayer? voice = null;
        var tracker = new FieldMovieNarrationTracker(
            _ => filmOutput, _ => { }, FieldPositionReader.FieldModule, null,
            (owner, reason) => voice?.StopIfOwnedBy(owner, reason));
        voice = new CutsceneVoicePlayer(
            CutsceneVoiceManifest.Parse(Manifest((JessieDoor, 3.4d))), _ => clip, _ => { }, () => tracker.IsPlaying);
        voice.TrySpeak(JessieDoor, CutsceneVoiceOwner.FieldAction, out _);
        CutsceneVoiceTransition.LeaveFieldModule(
            voice, NameEntryStateReader.NameEntryModule, CutsceneVoiceTransition.ScriptedNameEntryRequest);
        Equal(true, voice.IsContinuingAcrossTransition, "the control - the description was handed over");
        tracker.Observe(
            new FieldMovieNarrationSample(true, 42, FieldPositionReader.FieldModule, 117,
                Disc: 1, MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0),
            Start.AddSeconds(1));
        Equal(true, tracker.IsPlaying, "the film's own recording starts");
        Equal(false, clip.IsPlaying, "and the finishing description gave the device up first");
        Equal(1, clip.Stops, "exactly once");
    }

    private static void RecordingCompletionAndTimeoutReleaseDialogue()
    {
        var playing = true;
        var priority = new FieldCutsceneSpeechPriority();
        priority.AttachRecordingProbe(() => playing);
        priority.BeginNarration(Field, TimeSpan.FromSeconds(2), Start);
        Equal(true, priority.ShouldQueueDialogue(Field, Start.AddSeconds(3)),
            "actual recording outlives its estimated reservation");
        Equal(true, priority.ShouldDeferDialogueDelivery(Start.AddSeconds(3)), "independent output owns delivery");
        playing = false;
        Equal(false, priority.ShouldQueueDialogue(Field, Start.AddSeconds(3)), "device completion releases dialogue");
        priority.BeginNarration(Field, TimeSpan.FromSeconds(15), Start);
        Equal(false, priority.ShouldQueueDialogue(Field, Start.AddMilliseconds(1)),
            "cancellation before the first observation does not leave a silent reservation");
        playing = true;
        priority.BeginNarration(Field, TimeSpan.FromSeconds(2), Start);
        Equal(false, priority.ShouldQueueDialogue(Field, Start.AddSeconds(31)), "stalled recording cannot mute x64 forever");
        Equal(false, priority.ShouldDeferDialogueDelivery(Start.AddSeconds(31)), "same timeout applies to legacy delivery");
        priority.Reset();
        Equal(false, priority.ShouldDeferDialogueDelivery(Start.AddSeconds(32)), "field reset cannot extend a stalled clip");
        priority.AttachRecordingProbe(() => throw new InvalidOperationException("device lost"));
        Equal(false, priority.ShouldQueueDialogue(Field, Start.AddSeconds(33)), "failed device probe releases dialogue");
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
