using System.Text.RegularExpressions;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The reviewed film descriptions: 47 films across 86 installed anchors, now live
/// alongside the eleven Gold Saucer anchors that shipped before them.
///
/// <para>What these hold is the part a player notices. A film starts its own
/// recording and no other film's; a recording starts only once the native film is
/// really running, and never a second time while it runs; and when the audio is
/// missing, switched off or refused by the device, the spoken paragraph still
/// describes the scene rather than leaving the player with silence.</para>
///
/// <para>The Gold Saucer suite keeps the detailed episode lifecycle - delayed cues,
/// the docking film, pre-activation ordering, every native stop boundary - and
/// <c>EchoSCompatibilityTests</c> keeps the identity and mutated-script negatives.
/// This file does not repeat them.</para>
/// </summary>
internal static class ReviewedFilmNarrationTests
{
    private const int FieldModule = 1;
    private const int MovieOpcode = FieldOpcodeAddressResolver.OpcodeMovieIndex;
    private static readonly DateTime Start = new(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The Junon panorama's first-visit anchor ends with a sentence of guidance -
    /// that the story carries on by itself when the film ends - which belongs to that
    /// arrival and not to the later replay in zmind1. It is the one film whose two
    /// anchors say different things on purpose.
    /// </summary>
    private const int JunonPanoramaFilm = 37;

    public static void Run()
    {
        EveryReviewedAnchorIsRegisteredExactlyOnce();
        AnAnchorResolvesToItsOwnFilmAndRefusesTheNeighbours();
        ARecordingStartsOnlyOnceTheNativeFilmIsRunning();
        ARunningFilmIsNeverDescribedTwice();
        MissingDisabledOrRefusedAudioFallsBackToSpeech();
        EveryActiveAnchorHasASpokenParagraph();
        OneFilmSaysOneThingExceptTheJunonPanorama();
        AFilmNumberOnlyNamesAFilmOnceTheDiscIsKnown();
        AnAnchorFollowsTheFilmThatIsActuallyRunning();
        AFilmWithNoAnchorIsStillDescribed();
        AnUnverifiedFilmIdentityNarratesNothing();
        DialogueInsideAFilmKeepsTheRestOfTheDescription();
        DeferredCuesDieWithTheFilmTheyDescribe();
        AFilmAlreadyUnderWayIsNeverDescribedFromItsOpeningShot();
        TheGamesOwnWordsStopTheRecording();
        AParagraphForTheWrongFilmIsNeverSpoken();
        EveryDescribedFilmIsInTheFilmCatalogUnderItsOwnName();
    }

    /// <summary>
    /// Adds the check that needs the source tree. That the recordings themselves ship
    /// and that their sidecars stay inside the film is asserted once, by the Gold
    /// Saucer suite, over the same <see cref="FieldMovieNarrationPolicy.All"/>.
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        Run();
        EveryRecordingIsDeclaredInBothRuntimePackages();
        EveryNameMatchesTheInstalledExecutable();
        EveryInstalledRecordingCanBeScheduledAroundDialogue();
    }

    private static void EveryReviewedAnchorIsRegisteredExactlyOnce()
    {
        var all = FieldMovieNarrationPolicy.All;
        Equal(97, all.Count,
            "eleven Gold Saucer anchors, eighty-one reviewed ones, and five found by call flow");
        Equal(all.Count,
            all.Select(track => (track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex))
                .Distinct().Count(),
            "no two anchors may share a field, entity, script and byte");

        foreach (var group in all.GroupBy(track => track.MovieNumber))
        {
            Equal(1, group.Select(track => track.FileName).Distinct().Count(),
                $"film {group.Key}: every anchor of one film must name one recording");
            Equal(1, group.Select(track => track.DurationSeconds).Distinct().Count(),
                $"film {group.Key}: every anchor of one film must declare one length");
        }

        foreach (var track in all)
        {
            Equal(true, track.FileName.EndsWith("_audio_description.ogg", StringComparison.Ordinal),
                $"{track.Label}: the recording must follow the shipped naming convention");
            Equal(true, track.DurationSeconds > 0d,
                $"{track.Label}: a length measured from the installed film is required");
        }

        // The opening movie is described through its own path, not through a field
        // script anchor, so it must not appear here.
        Equal(false, all.Any(track => track.FileName.StartsWith("opening", StringComparison.Ordinal)),
            "the opening movie keeps its separate path");
    }

    private static void AnAnchorResolvesToItsOwnFilmAndRefusesTheNeighbours()
    {
        var all = FieldMovieNarrationPolicy.All;
        foreach (var track in all)
        {
            Equal(true,
                FieldMovieNarrationPolicy.TryResolve(
                    track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex, out var resolved),
                $"{track.Label}: its own anchor must resolve");
            Equal(track.MovieNumber, resolved.MovieNumber,
                $"{track.Label}: the anchor must resolve to the film it starts");
            Equal(track.FileName, resolved.FileName,
                $"{track.Label}: the anchor must resolve to that film's recording");

            // A byte either side of the anchor is inside an opcode, not at a boundary.
            foreach (var offset in new[] { -1, 1 })
            {
                var neighbour = track.ByteIndex + offset;
                if (all.Any(other =>
                    other.FieldId == track.FieldId && other.EntityId == track.EntityId &&
                    other.ScriptId == track.ScriptId && other.ByteIndex == neighbour))
                {
                    continue;
                }

                Equal(false,
                    FieldMovieNarrationPolicy.TryResolve(
                        track.FieldId, track.EntityId, track.ScriptId, neighbour, out _),
                    $"{track.Label}: byte {neighbour} must not resolve");
            }

            // Another entity or script in the same field is a different piece of the
            // game running, not this film.
            Equal(false,
                FieldMovieNarrationPolicy.TryResolve(
                    track.FieldId, track.EntityId + 100, track.ScriptId, track.ByteIndex, out _),
                $"{track.Label}: another entity must not resolve to this film");
        }
    }

    private static void ARecordingStartsOnlyOnceTheNativeFilmIsRunning()
    {
        // One film per field, so a field that starts several films is exercised on
        // each of them without the earlier one's opportunity interfering.
        foreach (var track in FieldMovieNarrationPolicy.All.DistinctBy(candidate =>
            (candidate.FieldId, candidate.MovieNumber)))
        {
            var output = new CountingOutput();
            var tracker = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldModule);
            var idle = Idle(track.FieldId);

            Equal(true,
                tracker.NoteIngress(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
                    MovieOpcode, idle, Start),
                $"{track.Label}: the film-start opcode opens an opportunity");

            // The engine has not raised its own active flag yet, so nothing plays and
            // nothing is spoken over.
            Equal(FieldMovieNarrationStartResult.WaitingForNativeStart,
                tracker.Begin(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
                    idle, Start.AddMilliseconds(50)),
                $"{track.Label}: a film that has not started is waited for");
            Equal(0, output.Starts, $"{track.Label}: nothing may play before the film does");

            var playing = Playing(track, 1);
            Equal(FieldMovieNarrationStartResult.Started,
                tracker.Begin(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
                    playing, Start.AddMilliseconds(120)),
                $"{track.Label}: the recording starts once the film is running");
            Equal(1, output.Starts, $"{track.Label}: exactly one start");
        }
    }

    private static void ARunningFilmIsNeverDescribedTwice()
    {
        // The native handler re-delivers the film-start opcode on every frame of the
        // film it started. Restarting on those would replay the description from zero
        // half way through the scene.
        var track = FieldMovieNarrationPolicy.All.First(candidate => candidate.MovieNumber == 30);
        var output = new CountingOutput();
        var tracker = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldModule);
        var playing = Playing(track, 1);

        _ = tracker.NoteIngress(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
            MovieOpcode, playing, Start);
        Equal(FieldMovieNarrationStartResult.Started,
            tracker.Begin(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
                playing, Start.AddMilliseconds(20)),
            "the first delivery starts the recording");

        var repeat = playing with
        {
            MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateInProgress,
            MovieHandlerPhase = FieldMovieNarrationPolicy.MovieHandlerPhaseYielding
        };
        for (var frame = 1; frame <= 40; frame++)
        {
            var now = Start.AddMilliseconds(20 + (frame * 33));
            _ = tracker.NoteIngress(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
                MovieOpcode, repeat, now);
            Equal(FieldMovieNarrationStartResult.Started,
                tracker.Begin(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex, repeat, now),
                "a native repeat reports the recording as already playing");
        }

        Equal(1, output.Starts, "one film, one start, however many times its opcode arrives");
    }

    private static void MissingDisabledOrRefusedAudioFallsBackToSpeech()
    {
        // Only Started suppresses the spoken paragraph. Everything that can go wrong
        // with the audio has to end in Unavailable so the scene is still described.
        var track = FieldMovieNarrationPolicy.All.First(candidate => candidate.MovieNumber == 28);
        foreach (var (factory, what) in new (Func<FieldMovieNarrationTrack, IFieldMovieNarrationOutput?>, string)[]
                 {
                     (_ => null, "a missing or disabled recording"),
                     (_ => throw new FileNotFoundException("no such file"), "a factory that throws"),
                     (_ => new CountingOutput { FailStart = true }, "a device that refuses"),
                     (_ => new CountingOutput { ThrowOnStart = true }, "a device that throws"),
                 })
        {
            var tracker = new FieldMovieNarrationTracker(factory, _ => { }, FieldModule);
            var playing = Playing(track, 1);
            Equal(true,
                tracker.NoteIngress(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
                    MovieOpcode, playing, Start),
                $"{what}: the opcode still opens an opportunity");
            Equal(FieldMovieNarrationStartResult.Unavailable,
                tracker.Begin(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
                    playing, Start.AddMilliseconds(40)),
                $"{what} must leave the spoken paragraph in charge");
        }
    }

    private static void EveryActiveAnchorHasASpokenParagraph()
    {
        // Every described film needs its paragraph: it is what a player hears when the
        // recording is missing, switched off or refused, and those are exactly the
        // runs where silence would be worst. Held here as well as in the Gold Saucer
        // suite because this one needs no installed game data, so it is the only
        // place the x64 module-only run checks it.
        var cues = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        Equal(cues.Count, cues.Select(cue => cue.Key).Distinct().Count(),
            "two description cues sharing an anchor would throw when the tracker is built");

        foreach (var track in FieldMovieNarrationPolicy.All)
        {
            var cue = cues.SingleOrDefault(candidate =>
                candidate.FieldId == track.FieldId &&
                candidate.EntityId == track.EntityId &&
                candidate.ScriptId == track.ScriptId &&
                candidate.ByteIndex == track.ByteIndex);
            Equal(true, !string.IsNullOrWhiteSpace(cue.Text),
                $"{track.Label} {track.FieldId}/{track.EntityId}/{track.ScriptId}/{track.ByteIndex}: " +
                "a described film needs a spoken fallback");
            Equal(MovieOpcode, cue.Opcode,
                $"{track.Label}: the paragraph must be anchored to the film start opcode");
        }
    }

    private static void OneFilmSaysOneThingExceptTheJunonPanorama()
    {
        var cues = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        foreach (var group in FieldMovieNarrationPolicy.All.GroupBy(track => track.MovieNumber))
        {
            var texts = group
                .Select(track => cues.Single(cue =>
                    cue.FieldId == track.FieldId && cue.EntityId == track.EntityId &&
                    cue.ScriptId == track.ScriptId && cue.ByteIndex == track.ByteIndex).Text)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var allowed = group.Key == JunonPanoramaFilm ? 2 : 1;
            Equal(true, texts.Length <= allowed,
                $"film {group.Key}: {texts.Length} paragraphs across its anchors, expected at most {allowed}");
        }

        // And that one exception is the guidance, not a stale draft.
        var panorama = FieldMovieNarrationPolicy.All
            .Where(track => track.MovieNumber == JunonPanoramaFilm)
            .Select(track => cues.Single(cue =>
                cue.FieldId == track.FieldId && cue.EntityId == track.EntityId &&
                cue.ScriptId == track.ScriptId && cue.ByteIndex == track.ByteIndex).Text)
            .ToArray();
        Equal(1, panorama.Count(text =>
                text.Contains("story continues automatically", StringComparison.Ordinal)),
            "exactly one Junon panorama anchor carries the story-continuation guidance");
    }

    private static void AFilmNumberOnlyNamesAFilmOnceTheDiscIsKnown()
    {
        // sm_movie.cpp reads films 0..19 from one block and everything above from a
        // block chosen by the disc. Film 20 is the clearest case: the Midgar North
        // Gate on disc 1 and the last battle on disc 3.
        Equal("mkup.avi", MovieFilmNameResolver.Resolve(20, 1), "film 20 on disc 1");
        Equal("greatpit.avi", MovieFilmNameResolver.Resolve(20, 2), "film 20 on disc 2");
        Equal("last4_2.avi", MovieFilmNameResolver.Resolve(20, 3), "film 20 on disc 3");

        // Below the block boundary the disc makes no difference at all.
        foreach (var disc in new[] { 1, 2, 3 })
        {
            Equal("d_ropego.avi", MovieFilmNameResolver.Resolve(2, disc),
                $"film 2 is the same file on disc {disc}");
        }

        // Nothing is guessed when the pair does not name a film: an unknown disc, a
        // number past the end of a disc's block, and a negative number all decline.
        Equal(null, MovieFilmNameResolver.Resolve(20, 4), "an unknown disc names nothing");
        Equal(null, MovieFilmNameResolver.Resolve(30, 3),
            "disc 3 has ten numbered films and no thirtieth");
        Equal(null, MovieFilmNameResolver.Resolve(-1, 1), "a negative number names nothing");
        Equal(null, MovieFilmNameResolver.Resolve(95, 1),
            "the hole in the table names nothing");

        // Every shipped anchor must name the film its own recording describes, on the
        // disc the game actually runs at. This is what would have caught the mapping
        // being taken for granted.
        foreach (var track in FieldMovieNarrationPolicy.All)
        {
            Equal(track.FilmFileName, MovieFilmNameResolver.Resolve(track.MovieNumber, 1),
                $"{track.Label}: film {track.MovieNumber} on disc 1");
        }
    }

    private static void AnAnchorFollowsTheFilmThatIsActuallyRunning()
    {
        // The story sets the disc to 2 at the Forgotten Capital and 3 at the final
        // dungeon, and from film 20 up the game reads a different block of names per
        // disc. So this anchor's address plays mkup on disc 1 and greatpit on disc 2,
        // and what it describes has to follow the film rather than the address.
        var track = FieldMovieNarrationPolicy.All.First(candidate => candidate.MovieNumber == 20);
        Equal("mkup.avi", track.FilmFileName, "the anchor under test describes mkup");

        var requested = new List<string>();
        FieldMovieNarrationTracker Build(bool provideOutput) => new(
            candidate =>
            {
                requested.Add(candidate.FileName);
                return provideOutput ? new CountingOutput() : null;
            },
            _ => { },
            FieldModule);

        // Disc 1: unchanged. The anchor asks for its own recording and plays it.
        requested.Clear();
        var onDiscOne = Build(true);
        Equal(FieldMovieNarrationStartResult.Started,
            Deliver(onDiscOne, track, disc: 1),
            "disc 1 must start the anchor's own recording");
        Equal("mkup_audio_description.ogg", requested.Single(), "disc 1 asks for mkup");

        // Disc 2: the same address is greatpit, so that is the recording asked for -
        // not the one the anchor was written with.
        requested.Clear();
        var onDiscTwo = Build(true);
        Equal(FieldMovieNarrationStartResult.Started,
            Deliver(onDiscTwo, track, disc: 2),
            "disc 2 must start the running film's recording");
        Equal("greatpit_audio_description.ogg", requested.Single(),
            "disc 2 asks for the film that is running");

        // Disc 2 with no recording installed: the paragraph must not be the anchor's,
        // because the anchor's paragraph is about the wrong film.
        requested.Clear();
        var noRecording = Build(false);
        Equal(FieldMovieNarrationStartResult.DescribesADifferentFilm,
            Deliver(noRecording, track, disc: 2),
            "a missing recording for another film must not release the anchor's paragraph");
        Equal(true,
            FieldMovieNarrationTracker.TryDescribeRunningFilm(Playing(track, 2), out var greatpit),
            "the running film must have a paragraph of its own");
        Equal(MovieFilmDescriptionCatalog.Paragraph("greatpit.avi"), greatpit,
            "and it must be the paragraph written for that film");

        // A film nothing has been written for: say nothing at all.
        var unwritten = new FieldMovieNarrationSample(
            true, 30, FieldModule, track.FieldId, Disc: 3,
            MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0);
        Equal(null, MovieFilmNameResolver.Resolve(30, 3), "film 30 does not exist on disc 3");
        Equal(false,
            FieldMovieNarrationTracker.TryDescribeRunningFilm(unwritten, out _),
            "an unidentifiable film has nothing to say");

        // An unreadable disc is not knowing which film this is, and this number names
        // three different ones. Nothing plays and nothing is said.
        requested.Clear();
        var unread = Build(true);
        Equal(FieldMovieNarrationStartResult.DescribesADifferentFilm,
            Deliver(unread, track, MovieFilmNameResolver.DiscUnknown),
            "an unreadable disc must not let a disc-dependent film be described");
        Equal(0, requested.Count, "and no recording may be asked for");
        Equal(false,
            FieldMovieNarrationTracker.TryDescribeRunningFilm(
                Playing(track, MovieFilmNameResolver.DiscUnknown), out _),
            "nor may a paragraph be offered for a film that cannot be identified");

        // A film below the block boundary is the same file on every disc, so the game
        // never consults the disc for it and neither does this.
        var shared = FieldMovieNarrationPolicy.All.First(candidate =>
            candidate.MovieNumber < MovieFilmNameResolver.SharedFilmCount);
        requested.Clear();
        var sharedTracker = Build(true);
        Equal(FieldMovieNarrationStartResult.Started,
            Deliver(sharedTracker, shared, MovieFilmNameResolver.DiscUnknown),
            $"{shared.Label} is the same film on every disc and needs no disc to play");
        Equal(shared.FileName, requested.Single(),
            "and it is that film's own recording");
    }

    // Production refuses to name a film without a readable disc and a command byte
    // that says the argument word is a film number, so every synthetic sample states
    // both rather than leaning on a default the engine never produces.
    private static FieldMovieNarrationSample Playing(
        FieldMovieNarrationTrack track, int disc, int frame = 0) =>
        Fresh(new FieldMovieNarrationSample(
            true, track.MovieNumber, FieldModule, track.FieldId, Disc: disc,
            MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
            MovieFrame: frame));

    private static FieldMovieNarrationSample Idle(int fieldId, int disc = 1) =>
        Fresh(new FieldMovieNarrationSample(
            false, 0, FieldModule, fieldId, Disc: disc,
            MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
            MovieFrame: 0));

    private static FieldMovieNarrationStartResult Deliver(
        FieldMovieNarrationTracker tracker,
        FieldMovieNarrationTrack track,
        int disc)
    {
        var idle = Idle(track.FieldId, disc);
        Equal(true,
            tracker.NoteIngress(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
                MovieOpcode, idle, Start),
            $"disc {disc}: the film-start opcode must always open an opportunity");
        return tracker.Begin(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
            Playing(track, disc), Start.AddMilliseconds(120));
    }

    private static void AFilmWithNoAnchorIsStillDescribed()
    {
        // The 46 films root prepared play at addresses nobody enumerated - the same
        // addresses the earlier batch uses, read under a different disc - so a table
        // of anchors cannot reach them. The engine raising its own flag is the film
        // starting, and that is the same evidence for every film in the game.
        var requested = new List<string>();
        var output = new CountingOutput();
        var tracker = new FieldMovieNarrationTracker(
            track => { requested.Add(track.FileName); return output; }, _ => { }, FieldModule);

        // Field 725 zmind1, film 37, on disc 2: zmind01.avi. There is no anchor at
        // this address for that film and there never will be one.
        const int Zmind1 = 725;
        Equal("zmind01.avi", MovieFilmNameResolver.Resolve(37, 2), "the film under test");
        Equal(false, FieldMovieNarrationPolicy.All.Any(track =>
                track.FieldId == Zmind1 && track.FilmFileName == "zmind01.avi"),
            "no anchor names this film, which is the point");

        tracker.Observe(
            new FieldMovieNarrationSample(false, 0, FieldModule, Zmind1, Disc: 2,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0),
            Start);
        Equal(0, output.Starts, "nothing plays while no film is running");

        tracker.Observe(
            new FieldMovieNarrationSample(true, 37, FieldModule, Zmind1, Disc: 2,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0),
            Start.AddMilliseconds(30));
        Equal(1, output.Starts, "the film starting is enough to start its recording");
        Equal("zmind01_audio_description.ogg", requested.Single(),
            "and it is that film's own recording");

        // The same address on disc 1 is a different film and gets that film's own.
        requested.Clear();
        var onDiscOne = new CountingOutput();
        var otherTracker = new FieldMovieNarrationTracker(
            track => { requested.Add(track.FileName); return onDiscOne; }, _ => { }, FieldModule);
        otherTracker.Observe(
            new FieldMovieNarrationSample(true, 37, FieldModule, Zmind1, Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0),
            Start);
        Equal("junon_audio_description.ogg", requested.Single(),
            "the same number on disc 1 is a different film");

        // The opening keeps its own player; starting it here as well would have the
        // player hear it twice.
        requested.Clear();
        var openingTracker = new FieldMovieNarrationTracker(
            track => { requested.Add(track.FileName); return new CountingOutput(); }, _ => { }, FieldModule);
        Equal("opening.avi", MovieFilmNameResolver.Resolve(53, 1), "film 53 is the opening");
        openingTracker.Observe(
            new FieldMovieNarrationSample(true, 53, FieldModule, 116, Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0),
            Start);
        Equal(0, requested.Count, "the opening movie is not started a second time here");
    }

    private static void AnUnverifiedFilmIdentityNarratesNothing()
    {
        // Three ways the engine can be showing something that is not a film we can
        // name, each of which used to be indistinguishable from a film starting.
        var cases = new (string What, FieldMovieNarrationSample Sample)[]
        {
            ("a command that is not a movie command owns the argument word",
                new FieldMovieNarrationSample(true, 37, FieldModule, 725, Disc: 2, MovieCommand: 2)),
            ("the command byte could not be read",
                new FieldMovieNarrationSample(true, 37, FieldModule, 725, Disc: 2,
                    MovieCommand: FieldMovieNarrationSample.CommandUnknown)),
            ("the engine is skipping films",
                new FieldMovieNarrationSample(true, 37, FieldModule, 725, Disc: 2,
                    MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MoviesSkipped: 1)),
        };

        foreach (var (what, sample) in cases)
        {
            var requested = new List<string>();
            var tracker = new FieldMovieNarrationTracker(
                track => { requested.Add(track.FileName); return new CountingOutput(); },
                _ => { }, FieldModule);
            tracker.Observe(sample, Start);
            Equal(0, requested.Count, $"{what}: no recording may be asked for");
            Equal(null, FieldMovieNarrationTracker.ResolveFilmAtSample(sample),
                $"{what}: no film may be named");
            Equal(false, FieldMovieNarrationTracker.TryDescribeRunningFilm(sample, out _),
                $"{what}: nothing may be said");
        }

        // The positive control: the same state with a movie command, a readable disc
        // and nothing being skipped does name its film.
        var good = new FieldMovieNarrationSample(true, 37, FieldModule, 725, Disc: 2,
            MovieCommand: FieldMovieNarrationSample.CommandStartMovie);
        Equal("zmind01.avi", FieldMovieNarrationTracker.ResolveFilmAtSample(good),
            "a verified sample still names its film");
    }

    private static void DialogueInsideAFilmKeepsTheRestOfTheDescription()
    {
        // The observatory's Study of Planet Life narrates itself in the game's own
        // text while the film runs. The recording has only start and stop, so it
        // cannot duck; giving way must not mean giving up on the rest of the film.
        //
        // Everything here is positioned by the film's own frame counter, at the
        // fifteen frames a second every one of these films runs at. The wall clock
        // cannot do it: it runs on through a pause and keeps running after the film
        // ends, which is how a queued sentence ends up spoken into gameplay.
        var cues = new List<MovieNarrationCue>
        {
            new(0d, 3d, "first"),
            new(10d, 13d, "second"),
            new(20d, 23d, "third"),
            new(30d, 33d, "fourth"),
        };
        var output = new CountingOutput();
        var tracker = new FieldMovieNarrationTracker(
            _ => output, _ => { }, FieldModule, _ => cues);

        const int Bugin1c = 543;
        static FieldMovieNarrationSample AtSecond(double seconds) =>
            new(true, 42, FieldModule, Bugin1c, Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
                MovieFrame: (int)Math.Round(seconds * FieldMovieNarrationSample.FramesPerSecond));

        var spoken = new List<string>();
        bool Accept(string cue) { spoken.Add(cue); return true; }
        bool Refuse(string cue) { _ = cue; return false; }

        tracker.Observe(AtSecond(0), Start);
        Equal(1, output.Starts, "a film at frame zero starts its recording");

        // Eleven seconds in the game starts talking, in the middle of the cue that
        // runs from ten to thirteen.
        tracker.Observe(AtSecond(11), Start.AddSeconds(11), dialogueIsOnScreen: true);
        Equal(false, tracker.IsPlaying, "the recording gives way to the game's own words");
        Equal(false, tracker.TryDeliverDeferredCue(true, Accept, out _),
            "and nothing is said while the game is still talking");

        // The cue that was cut off mid-sentence has not been heard, so it is kept and
        // said in full. The one that had already finished is not repeated.
        tracker.Observe(AtSecond(12), Start.AddSeconds(12));
        Equal(true, tracker.TryDeliverDeferredCue(false, Accept, out var interrupted),
            "the cue the dialogue cut off is said again in full");
        Equal("second", interrupted, "and it is the interrupted one, not the one before it");

        // A speaker that refuses keeps the cue. This is the case that used to lose it.
        tracker.Observe(AtSecond(21), Start.AddSeconds(21));
        Equal(false, tracker.TryDeliverDeferredCue(false, Refuse, out _),
            "a refused cue reports no delivery");
        Equal(true, tracker.TryDeliverDeferredCue(false, Accept, out var retried),
            "and is offered again on the next tick");
        Equal("third", retried, "the refused cue is not skipped");

        // A second dialogue window, and the description survives that too.
        tracker.Observe(AtSecond(29), Start.AddSeconds(29), dialogueIsOnScreen: true);
        Equal(false, tracker.TryDeliverDeferredCue(true, Accept, out _),
            "the second window silences it as well");
        tracker.Observe(AtSecond(31), Start.AddSeconds(31));
        Equal(true, tracker.TryDeliverDeferredCue(false, Accept, out var afterSecond),
            "and the film keeps being described after it");
        Equal("fourth", afterSecond, "with the cue whose moment has come");
        Equal(3, spoken.Count, "three cues in total, each once");
        Equal(1, output.Starts, "the recording is never restarted from the beginning");

        // The film's clock, not the room's. A frame that does not advance is a paused
        // film, and a paused film has not reached its next cue.
        var paused = new FieldMovieNarrationTracker(_ => new CountingOutput(), _ => { }, FieldModule, _ => cues);
        paused.Observe(AtSecond(0), Start);
        // Interrupted at five seconds: the first cue has finished, the second has not
        // begun, so nothing is owed until the film reaches ten seconds.
        paused.Observe(AtSecond(5), Start.AddSeconds(5), dialogueIsOnScreen: true);
        paused.Observe(AtSecond(5), Start.AddMinutes(5));
        Equal(false, paused.TryDeliverDeferredCue(false, Accept, out _),
            "five wall-clock minutes at the same frame reach no new cue");
        paused.Observe(AtSecond(10), Start.AddMinutes(5).AddSeconds(1));
        Equal(true, paused.TryDeliverDeferredCue(false, Accept, out var resumed),
            "and the film moving on again does");
        Equal("second", resumed, "with the cue that moment belongs to");

        // A cue whose shot has long left the screen is dropped rather than spoken
        // late over something else.
        var late = new FieldMovieNarrationTracker(_ => new CountingOutput(), _ => { }, FieldModule, _ => cues);
        late.Observe(AtSecond(0), Start);
        late.Observe(AtSecond(1), Start.AddSeconds(1), dialogueIsOnScreen: true);
        late.Observe(AtSecond(29), Start.AddSeconds(29));
        Equal(true, late.TryDeliverDeferredCue(false, Accept, out var survivor),
            "the cue whose moment is now is still offered");
        Equal("third", survivor, "the ones whose shots are long gone were dropped, not queued up");
    }

    private static void DeferredCuesDieWithTheFilmTheyDescribe()
    {
        // A deferred schedule is spoken by the host, not by the audio device, so
        // stopping the device does not stop it. Every way of losing the film has to
        // clear it or a sentence about a film arrives minutes into ordinary play.
        var cues = new List<MovieNarrationCue> { new(0d, 3d, "first"), new(20d, 23d, "later") };
        const int Bugin1c = 543;
        static FieldMovieNarrationSample AtSecond(double seconds) =>
            new(true, 42, FieldModule, Bugin1c, Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
                MovieFrame: (int)Math.Round(seconds * FieldMovieNarrationSample.FramesPerSecond));

        var losses = new (string What, FieldMovieNarrationSample After)[]
        {
            ("the film ended",
                new FieldMovieNarrationSample(false, 0, FieldModule, Bugin1c, Disc: 1,
                    MovieCommand: FieldMovieNarrationSample.CommandStartMovie, MovieFrame: 0)),
            ("the engine started skipping films",
                AtSecond(21) with { MoviesSkipped = 1 }),
            ("the frame stopped reading",
                AtSecond(21) with { MovieFrame = FieldMovieNarrationSample.FrameUnknown }),
            ("the command byte stopped reading",
                AtSecond(21) with { MovieCommand = FieldMovieNarrationSample.CommandUnknown }),
            ("another command took the argument word",
                AtSecond(21) with { MovieCommand = 2 }),
            ("the module changed",
                AtSecond(21) with { CurrentModule = FieldModule + 2 }),
            ("a different film started",
                AtSecond(21) with { MovieNumber = 44 }),
        };

        // Only the film under test has a schedule, so a different film starting
        // cannot supply one of its own and mask a stale schedule surviving.
        IReadOnlyList<MovieNarrationCue> CuesForBoogdemoOnly(FieldMovieNarrationTrack track) =>
            track.Label == "boogdemo.avi" || track.FilmFileName == "boogdemo.avi" ? cues : [];

        foreach (var (what, after) in losses)
        {
            var tracker = new FieldMovieNarrationTracker(
                _ => new CountingOutput(), _ => { }, FieldModule, CuesForBoogdemoOnly);
            tracker.Observe(AtSecond(0), Start);
            tracker.Observe(AtSecond(1), Start.AddSeconds(1), dialogueIsOnScreen: true);

            // The schedule is live at this point: prove it before taking it away.
            tracker.Observe(AtSecond(21), Start.AddSeconds(21));
            Equal(true, tracker.TryDeliverDeferredCue(false, _ => true, out _),
                $"{what}: the control - a live schedule does deliver");

            var control = new FieldMovieNarrationTracker(
                _ => new CountingOutput(), _ => { }, FieldModule, CuesForBoogdemoOnly);
            control.Observe(AtSecond(0), Start);
            control.Observe(AtSecond(1), Start.AddSeconds(1), dialogueIsOnScreen: true);
            control.Observe(after, Start.AddSeconds(21));
            Equal(false, control.TryDeliverDeferredCue(false, _ => true, out _),
                $"{what}: nothing may be said afterwards");
        }

        // Losing the foreground, or being unloaded, clears it too.
        foreach (var reason in new[]
        {
            FieldMovieNarrationStopReason.Suspended,
            FieldMovieNarrationStopReason.Unloaded,
        })
        {
            var tracker = new FieldMovieNarrationTracker(
                _ => new CountingOutput(), _ => { }, FieldModule, _ => cues);
            tracker.Observe(AtSecond(0), Start);
            tracker.Observe(AtSecond(1), Start.AddSeconds(1), dialogueIsOnScreen: true);
            tracker.Stop(reason);
            tracker.Observe(AtSecond(21), Start.AddSeconds(21));
            Equal(false, tracker.TryDeliverDeferredCue(false, _ => true, out _),
                $"{reason}: the schedule must not survive it");
        }
    }

    private static void AFilmAlreadyUnderWayIsNeverDescribedFromItsOpeningShot()
    {
        // Attaching to a film in progress - the mod loaded late, or the identity only
        // became readable part-way through - must not play a recording of the opening
        // shot over the middle of the film. The film is still described, from where
        // it has actually got to.
        var cues = new List<MovieNarrationCue>
        {
            new(0d, 3d, "the opening shot"),
            new(30d, 33d, "the middle"),
            new(60d, 63d, "the end"),
        };
        const int Bugin1c = 543;
        static FieldMovieNarrationSample AtSecond(double seconds) =>
            new(true, 42, FieldModule, Bugin1c, Disc: 1,
                MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
                MovieFrame: (int)Math.Round(seconds * FieldMovieNarrationSample.FramesPerSecond));

        var cold = new CountingOutput();
        var attached = new FieldMovieNarrationTracker(_ => cold, _ => { }, FieldModule, _ => cues);
        attached.Observe(AtSecond(20), Start);
        Equal(0, cold.Starts, "a film twenty seconds in must not start a recording from zero");
        Equal(false, attached.TryDeliverDeferredCue(false, _ => true, out _),
            "and the shot it has passed is not described");

        attached.Observe(AtSecond(31), Start.AddSeconds(11));
        Equal(true, attached.TryDeliverDeferredCue(false, _ => true, out var midway),
            "but the film is still described from where it is");
        Equal("the middle", midway, "with the cue whose moment has come");

        // The bound is generous enough for the ordinary case: a film observed within
        // its first moments still gets its recording.
        var prompt = new CountingOutput();
        var fresh = new FieldMovieNarrationTracker(_ => prompt, _ => { }, FieldModule, _ => cues);
        fresh.Observe(AtSecond(1), Start);
        Equal(1, prompt.Starts, "a film caught in its first second still plays its recording");

        // An unreadable frame is not knowing where the film is, so nothing starts.
        var blind = new CountingOutput();
        var unknown = new FieldMovieNarrationTracker(_ => blind, _ => { }, FieldModule, _ => cues);
        unknown.Observe(
            AtSecond(0) with { MovieFrame = FieldMovieNarrationSample.FrameUnknown }, Start);
        Equal(0, blind.Starts, "an unreadable frame must not start a recording");
    }

    private static void TheGamesOwnWordsStopTheRecording()
    {
        // Bugenhagen's Study of Planet Life runs its explanation as ordinary field
        // text - bugin1c texts 25 to 27, MESSAGE opcodes in entity 12 script 7 -
        // while the film plays. The recording plays on its own device, so without
        // this the player gets the screen reader and the recording at once.
        var track = FieldMovieNarrationPolicy.All.First();
        var output = new CountingOutput();
        var tracker = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldModule);
        var idle = Idle(track.FieldId);
        var playing = Playing(track, 1);

        _ = tracker.NoteIngress(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
            MovieOpcode, idle, Start);
        Equal(FieldMovieNarrationStartResult.Started,
            tracker.Begin(track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex,
                playing, Start.AddMilliseconds(120)),
            "the recording starts with the film");

        // A quiet frame changes nothing: the film is still running.
        tracker.Observe(playing, Start.AddSeconds(1), dialogueIsOnScreen: false);
        Equal(true, tracker.IsPlaying, "the recording keeps playing while nothing is said");

        // The game opens a window and the recording gives way.
        tracker.Observe(playing, Start.AddSeconds(2), dialogueIsOnScreen: true);
        Equal(false, tracker.IsPlaying, "a readable dialogue window stops the recording");

        // And it does not creep back in when the window closes: the film is part-way
        // through and starting from zero would describe the wrong moment.
        tracker.Observe(playing, Start.AddSeconds(3), dialogueIsOnScreen: false);
        Equal(false, tracker.IsPlaying, "a stopped recording is not resumed mid-film");
        Equal(1, output.Starts, "the recording is started exactly once");
    }

    private static void AParagraphForTheWrongFilmIsNeverSpoken()
    {
        // The failure this exists to stop: on disc 2 the anchor in Junon still fires,
        // the recording is missing, and the old delivery step would have spoken the
        // Midgar paragraph over a Weapon attack. Confident and wrong is worse than
        // silent, so the cue is spent either way.
        var queue = new FieldCutsceneDescriptionDeliveryQueue();
        var delivery = new FieldCutsceneDescriptionDelivery(queue);
        var cue = new FieldCutsceneDescriptionCue(117, 0, 0, 143, "the North Gate stands open", MovieOpcode);

        var spoken = new List<string>();
        var reserved = new List<FieldCutsceneDescriptionCue>();
        string? substitute = null;
        FieldCutsceneDeliveryOutcome Run() => delivery.Deliver(
            117,
            _ => FieldMovieNarrationStartResult.DescribesADifferentFilm,
            text => { spoken.Add(text); return true; },
            reserved.Add,
            out _,
            () => substitute);

        queue.Enqueue(cue);
        Equal(FieldCutsceneDeliveryOutcome.Withheld, Run(),
            "with nothing written for the running film the cue is withheld");
        Equal(0, spoken.Count, "the wrong paragraph must never reach the speaker");
        Equal(1, reserved.Count, "the cue is still spent, so it cannot be retried forever");
        Equal(0, queue.Count, "and it leaves the queue");

        // When the running film does have a paragraph, that is what is spoken.
        queue.Enqueue(cue);
        substitute = "A cannon fires on the ocean.";
        Equal(FieldCutsceneDeliveryOutcome.Spoken, Run(), "the running film's own paragraph is spoken");
        Equal(substitute, spoken.Single(), "and it is the substitute, not the anchor's text");
        Equal(substitute, reserved[^1].Text,
            "the dialogue window is reserved for the words actually spoken");

        // Unchanged for every ordinary cue: a paragraph that does describe the film
        // is still spoken.
        queue.Enqueue(cue);
        spoken.Clear();
        Equal(FieldCutsceneDeliveryOutcome.Spoken,
            delivery.Deliver(117, _ => FieldMovieNarrationStartResult.Unavailable,
                text => { spoken.Add(text); return true; }, reserved.Add, out _, () => substitute),
            "an unavailable recording still releases the anchor's own paragraph");
        Equal(cue.Text, spoken.Single(), "and it is the anchor's text, not the substitute");
    }

    private static void EveryDescribedFilmIsInTheFilmCatalogUnderItsOwnName()
    {
        // The address-keyed paragraph and the film-keyed one describe the same film,
        // so they have to say the same thing. Two copies that drift apart would mean
        // a player hears one description on disc 1 and a different one on disc 2 for
        // the same footage.
        var cues = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        var checkedFilms = 0;
        foreach (var track in FieldMovieNarrationPolicy.All)
        {
            var paragraph = MovieFilmDescriptionCatalog.Paragraph(track.FilmFileName);
            if (paragraph is null) { continue; }

            var cue = cues.SingleOrDefault(candidate =>
                candidate.FieldId == track.FieldId && candidate.EntityId == track.EntityId &&
                candidate.ScriptId == track.ScriptId && candidate.ByteIndex == track.ByteIndex);
            if (string.IsNullOrWhiteSpace(cue.Text)) { continue; }

            // The Junon panorama's first-visit anchor adds a sentence of guidance to
            // the film's own paragraph; everything else says exactly the paragraph.
            Equal(true, cue.Text.StartsWith(paragraph, StringComparison.Ordinal),
                $"{track.Label}: the anchor's paragraph must be the film's paragraph");
            checkedFilms++;
        }

        Equal(true, checkedFilms >= 80,
            $"only {checkedFilms} anchors were compared; the catalog lookup is not matching");
    }

    private static void EveryNameMatchesTheInstalledExecutable()
    {
        // The baked table is the game's own, so it has to still be the game's own.
        // Read straight out of ff7_en.exe: the pointer array at 0x007BAE80, each
        // pointer converted from a virtual address to a file offset through the PE
        // section headers.
        var exePath = Path.Combine(RequireDataRoot(), "ff7_en.exe");
        var bytes = File.ReadAllBytes(exePath);
        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        Equal(0x00004550, BitConverter.ToInt32(bytes, peOffset), "the executable must be a PE");
        var optionalSize = BitConverter.ToUInt16(bytes, peOffset + 20);
        var sectionCount = BitConverter.ToUInt16(bytes, peOffset + 6);
        var imageBase = BitConverter.ToUInt32(bytes, peOffset + 24 + 28);

        var sections = new List<(uint Virtual, uint Size, uint Raw, uint RawSize)>();
        for (var index = 0; index < sectionCount; index++)
        {
            var at = peOffset + 24 + optionalSize + index * 40;
            sections.Add((
                BitConverter.ToUInt32(bytes, at + 12),
                BitConverter.ToUInt32(bytes, at + 8),
                BitConverter.ToUInt32(bytes, at + 20),
                BitConverter.ToUInt32(bytes, at + 16)));
        }

        int? ToFileOffset(uint virtualAddress)
        {
            var relative = virtualAddress - imageBase;
            foreach (var section in sections)
            {
                if (relative >= section.Virtual &&
                    relative < section.Virtual + Math.Max(section.Size, section.RawSize))
                {
                    return (int)(relative - section.Virtual + section.Raw);
                }
            }

            return null;
        }

        var table = ToFileOffset(0x007BAE80u)
            ?? throw new InvalidOperationException("the movie name table is not in any section.");
        var names = MovieFilmNameResolver.InstalledNames;
        for (var index = 0; index < names.Count; index++)
        {
            var pointer = BitConverter.ToUInt32(bytes, table + index * 4);
            var at = pointer == 0 ? null : ToFileOffset(pointer);
            string? installed = null;
            if (at is { } start)
            {
                var end = start;
                while (end < bytes.Length && bytes[end] != 0 && end - start < 64) { end++; }
                installed = System.Text.Encoding.ASCII.GetString(bytes, start, end - start);
            }

            Equal(names[index], installed, $"movie name table entry {index}");
        }
    }

    private static void EveryRecordingIsDeclaredInBothRuntimePackages()
    {
        // A film declared on one runtime only ships without its audio on the other and
        // silently falls back to speech there - which looks like working software
        // until someone plays the other executable.
        var root = RequireSourceRoot();
        var legacy = File.ReadAllText(Path.Combine(
            root, "Ff7.Accessibility.Reloaded", "Ff7.Accessibility.Reloaded.csproj"));
        var native = File.ReadAllText(Path.Combine(
            root, "Ff7.Accessibility.Steam2026X64", "Ff7.Accessibility.Steam2026X64.csproj"));

        // Every shipped file, not only the ones a script anchor names: a film now
        // starts from the engine's own state, so any of them can play and all of them
        // have to be in the package.
        var assets = Path.Combine(root, "Ff7.Accessibility.Reloaded", "Assets", "movies");
        var onDisk = Directory.GetFiles(assets).Select(Path.GetFileName).ToArray();
        Equal(true, onDisk.Length >= 298,
            $"only {onDisk.Length} movie assets were found; the second batch is missing");

        foreach (var name in onDisk)
        {
            Equal(true, legacy.Contains($@"Assets\movies\{name}", StringComparison.Ordinal),
                $"{name} must be declared in the legacy project");
            Equal(true, native.Contains($@"Assets\movies\{name}", StringComparison.Ordinal),
                $"{name} must be declared in the x64 project");
        }

        // And nothing may be declared that is not there, which is what would ship a
        // build that fails to copy an asset it promised.
        foreach (Match declaration in Regex.Matches(legacy, @"Assets\\movies\\([\w.]+)"))
        {
            var name = declaration.Groups[1].Value;
            Equal(true, File.Exists(Path.Combine(assets, name)),
                $"the legacy project declares {name}, which is not installed");
        }
    }

    private static void EveryInstalledRecordingCanBeScheduledAroundDialogue()
    {
        // A film the game talks over is described from its sidecar rather than from
        // its continuous track, so a recording whose sidecar will not parse loses
        // everything after the first line of dialogue.
        var assets = Path.Combine(
            RequireSourceRoot(), "Ff7.Accessibility.Reloaded", "Assets", "movies");
        var checkedFilms = 0;
        foreach (var film in MovieFilmDescriptionCatalog.Paragraphs.Keys)
        {
            var recording = MovieFilmDescriptionCatalog.RecordingFileName(film);
            if (!File.Exists(Path.Combine(assets, recording)))
            {
                continue;
            }

            var sidecar = Path.Combine(
                assets, Path.GetFileNameWithoutExtension(recording) + ".json");
            Equal(true, File.Exists(sidecar), $"{film}: the recording ships without its sidecar");

            var cues = MovieNarrationCueSchedule.Parse(File.ReadAllText(sidecar));
            Equal(true, cues.Count > 0, $"{film}: the sidecar yielded no cue windows");
            for (var index = 1; index < cues.Count; index++)
            {
                Equal(true, cues[index].Start >= cues[index - 1].Start,
                    $"{film}: cue {index} starts before the one before it");
            }

            foreach (var cue in cues)
            {
                Equal(true, cue.End >= cue.Start, $"{film}: a cue ends before it starts");
                Equal(true, cue.Text.Length > 0, $"{film}: a cue has no words");
            }

            checkedFilms++;
        }

        Equal(true, checkedFilms >= 93,
            $"only {checkedFilms} films were checked; the catalog is not reaching the installed assets");
    }

    private static FieldMovieNarrationSample Fresh(FieldMovieNarrationSample sample) =>
        sample with
        {
            MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry,
            MovieHandlerPhase = 0
        };

    private static string RequireSourceRoot() =>
        Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT")
        ?? throw new InvalidOperationException(
            "The reviewed film asset checks need FF7_ACCESSIBILITY_SOURCE_ROOT.");

    private static string RequireDataRoot() =>
        Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
        ?? throw new InvalidOperationException(
            "The movie name table check needs FF7_ACCESSIBILITY_DATA_ROOT.");

    private sealed class CountingOutput : IFieldMovieNarrationOutput
    {
        private bool playing;
        public int Starts { get; private set; }
        public bool FailStart { get; init; }
        public bool ThrowOnStart { get; init; }
        public bool IsPlaying => playing;

        public bool Start(string reason)
        {
            if (ThrowOnStart) throw new InvalidOperationException("device refused to open");
            if (FailStart) return false;
            Starts++;
            playing = true;
            return true;
        }

        public bool Stop(string reason)
        {
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
                $"Reviewed film narration: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
