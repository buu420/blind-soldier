using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Eleven fields in the 2026-09-21 continuation batch run a MOVIE opcode, and seven of
/// those films have a reviewed recording of their own. Adding an arrival description to
/// those screens put a second description on a screen that already had one, so this
/// holds the three things that have to be true for the two never to compete.
///
/// <para>First, that they are different cues: no arrival anchor is a film anchor, so the
/// tracker cannot deliver one where the other was meant. Second, that the arrival always
/// comes first in the game's own execution order - the same script at a lower byte, or an
/// init script against an event script - so the ordering is a property of the flevel and
/// not of our queue. Third, and the only one that needs a runtime, that while a film's
/// recording actually has the device the arrival description is held rather than spoken,
/// on <em>both</em> generic delivery routes: the x86 <see cref="FieldCutsceneDescriptionDelivery"/>
/// step that the monitor loop drives, and the x64 coordinator, which is covered by
/// <c>Steam2026FieldMovieNarrationAdapterTests</c> in its own project.</para>
///
/// <para>The third is the one that matters for a cold start. An ordinary entry runs MPNAM
/// long before the MOVIE opcode, but the mod attaching to a game that is already standing
/// in one of these rooms offers the arrival line from a stable field observation instead,
/// and that can land at any moment - including the middle of the film.</para>
/// </summary>
internal static class ContinuationMovieOverlapTests
{
    /// <summary>
    /// The batch 6 fields whose scripts contain a MOVIE opcode, read from both installed
    /// archives during the research pass. 767 las4_4 is here and deliberately has no
    /// arrival description at all: it never runs MPNAM.
    /// </summary>
    private static readonly int[] MovieBearingFields =
        [311, 312, 327, 567, 569, 637, 647, 763, 765, 766, 767];

    /// <summary>las4_4: an FMV screen with no MPNAM, so no arrival anchor exists for it.</summary>
    private const int Las4_4 = 767;

    /// <summary>mtnvl2, whose film is movie 31 mtnvl - a reviewed recording.</summary>
    private const int Mtnvl2 = 311;

    private static readonly DateTime Start = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        NoArrivalAnchorIsAlsoAFilmAnchor();
        TheArrivalAlwaysRunsBeforeTheFilmInTheGamesOwnOrder();
        AFilmsRecordingHoldsTheArrivalDescriptionOnTheX86Route();
    }

    /// <summary>
    /// The two catalogs are indexed by the same four numbers. An arrival description
    /// sharing an anchor with a film would mean one of them silently replaced the other.
    /// </summary>
    private static void NoArrivalAnchorIsAlsoAFilmAnchor()
    {
        var films = FieldMovieNarrationPolicy.All
            .Select(track => (track.FieldId, track.EntityId, track.ScriptId, track.ByteIndex))
            .ToHashSet();

        var collisions = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions()
            .Concat(FieldCutsceneContinuationDescriptions.CreateAll())
            .Where(cue => films.Contains((cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex)))
            .Select(cue => $"{cue.FieldId}:{cue.EntityId}:{cue.ScriptId}:{cue.ByteIndex}")
            .ToList();

        if (collisions.Count > 0)
        {
            throw new InvalidOperationException(
                "Continuation movie overlap - a description is anchored on a film anchor: " +
                string.Join(", ", collisions));
        }

        // The control: the film catalog really does cover these fields, so the check
        // above is not vacuously true because nothing was looked at.
        var covered = MovieBearingFields
            .Where(field => FieldMovieNarrationPolicy.All.Any(track => track.FieldId == field))
            .ToArray();
        if (covered.Length != 7)
        {
            throw new InvalidOperationException(
                "Continuation movie overlap - expected 7 of the 11 movie-bearing fields to have a " +
                $"reviewed film, found {covered.Length}: {string.Join(", ", covered)}.");
        }
    }

    /// <summary>
    /// MPNAM runs from a director's init script, which the engine runs at field load;
    /// a film runs from an event script later, or from the same init script at a higher
    /// byte. Either way the arrival description is queued first and the film cannot be
    /// stuck behind it.
    /// </summary>
    private static void TheArrivalAlwaysRunsBeforeTheFilmInTheGamesOwnOrder()
    {
        var areas = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions()
            .Where(cue => cue.Opcode == FieldOpcodeAddressResolver.OpcodeMapNameIndex)
            .ToDictionary(cue => cue.FieldId, cue => cue);

        foreach (var field in MovieBearingFields)
        {
            if (field == Las4_4)
            {
                if (areas.ContainsKey(field))
                {
                    throw new InvalidOperationException(
                        "Continuation movie overlap - las4_4 has no MPNAM in either installed " +
                        "archive, so it must not carry an arrival description.");
                }

                continue;
            }

            if (!areas.TryGetValue(field, out var arrival))
            {
                throw new InvalidOperationException(
                    $"Continuation movie overlap - field {field} lost its arrival description.");
            }

            foreach (var film in FieldMovieNarrationPolicy.All.Where(track => track.FieldId == field))
            {
                var sameScript = film.EntityId == arrival.EntityId && film.ScriptId == arrival.ScriptId;
                var ordered = sameScript
                    ? arrival.ByteIndex < film.ByteIndex
                    : arrival.ScriptId == 0 && film.ScriptId != 0;

                if (!ordered)
                {
                    throw new InvalidOperationException(
                        $"Continuation movie overlap - field {field}: the arrival anchor " +
                        $"{arrival.EntityId}:{arrival.ScriptId}:{arrival.ByteIndex} is not proven to run " +
                        $"before the film anchor {film.EntityId}:{film.ScriptId}:{film.ByteIndex} " +
                        $"({film.Label}).");
                }
            }
        }
    }

    /// <summary>
    /// The x86 route as the monitor loop assembles it: the shared delivery step, the
    /// recorded voice, and the film narration tracker wired to each other exactly as
    /// <c>Mod.Initialize</c> wires them.
    ///
    /// <para>The failure this rules out is the cold-start arrival line being read out on
    /// top of a film's own 20-second description. The recorded voice answers Busy rather
    /// than Unavailable while the film has the device, and Busy must leave the cue at the
    /// head of the queue instead of falling through to the screen reader - which would
    /// spend the cue, in the wrong voice, over the film.</para>
    /// </summary>
    private static void AFilmsRecordingHoldsTheArrivalDescriptionOnTheX86Route()
    {
        var arrival = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions()
            .Single(cue => cue.FieldId == Mtnvl2 &&
                           cue.Opcode == FieldOpcodeAddressResolver.OpcodeMapNameIndex);

        var filmOutput = new CountingOutput();
        var narration = new FieldMovieNarrationTracker(
            _ => filmOutput, _ => { }, FieldPositionReader.FieldModule);

        var descriptionOutput = new CountingOutput();
        var voice = new CutsceneVoicePlayer(
            CutsceneVoiceManifest.Parse(Manifest((arrival.Text, 8.5d))),
            _ => descriptionOutput,
            _ => { },
            () => narration.IsPlaying);

        // Film 31 is mtnvl, the film this very field plays, and it is in the reviewed
        // catalog - so the global path starts a real description of it.
        narration.Observe(Film(number: 31, active: true), Start);
        if (!narration.IsPlaying)
        {
            throw new InvalidOperationException(
                "Continuation movie overlap - the mtnvl film narration did not start, so the " +
                "rest of this test would prove nothing.");
        }

        var queue = new FieldCutsceneDescriptionDeliveryQueue();
        var delivery = new FieldCutsceneDescriptionDelivery(queue);
        queue.Enqueue(arrival);

        var spokenByScreenReader = new List<string>();
        var reserved = new List<int>();

        var outcome = delivery.Deliver(
            Mtnvl2,
            candidate => narration.Begin(
                candidate.FieldId, candidate.EntityId, candidate.ScriptId, candidate.ByteIndex,
                Film(number: 31, active: true), Start),
            text => Speak(voice, text, spokenByScreenReader),
            candidate => reserved.Add(candidate.FieldId),
            out _);

        Equal(FieldCutsceneDeliveryOutcome.Refused, outcome,
            "the arrival description is refused while the film's recording is playing");
        Equal(0, spokenByScreenReader.Count, "and it is not read out by the screen reader either");
        Equal(0, descriptionOutput.Starts, "and no second clip was started on the device");
        Equal(0, reserved.Count, "and nothing that was never said reserved a dialogue window");
        Equal(1, queue.Count, "the cue is still at the head, to be offered again");

        // The film ends. Now it is the arrival description's turn, in its own voice.
        filmOutput.Finish();
        narration.Observe(Film(number: 0, active: false), Start.AddSeconds(21));
        Equal(false, narration.IsPlaying, "the film is over");

        outcome = delivery.Deliver(
            Mtnvl2,
            candidate => narration.Begin(
                candidate.FieldId, candidate.EntityId, candidate.ScriptId, candidate.ByteIndex,
                Film(number: 0, active: false), Start.AddSeconds(21)),
            text => Speak(voice, text, spokenByScreenReader),
            candidate => reserved.Add(candidate.FieldId),
            out var delivered);

        Equal(FieldCutsceneDeliveryOutcome.Spoken, outcome, "the held cue is delivered afterwards");
        Equal(arrival.Text, delivered.Text, "and it is the arrival description, unaltered");
        Equal(1, descriptionOutput.Starts, "said once, by its own recording");
        Equal(0, spokenByScreenReader.Count, "never by the screen reader");
        Equal(0, queue.Count, "and the queue is empty");
    }

    private static bool Speak(CutsceneVoicePlayer voice, string text, List<string> screenReader) =>
        CutsceneVoiceSpeaker.Deliver(
            voice,
            text,
            CutsceneVoiceOwner.FieldAction,
            spoken =>
            {
                screenReader.Add(spoken);
                return true;
            }).Spoken;

    private static FieldMovieNarrationSample Film(int number, bool active) =>
        new(
            MovieActive: active,
            MovieNumber: number,
            CurrentModule: FieldPositionReader.FieldModule,
            CurrentFieldId: Mtnvl2,
            MovieHandlerState: FieldMovieNarrationPolicy.MovieHandlerStateInProgress,
            MovieHandlerPhase: FieldMovieNarrationPolicy.MovieHandlerPhaseYielding,
            // Mt. Nibel is disc 1, and film 31 only names mtnvl.avi on that disc.
            Disc: 1,
            MovieCommand: FieldMovieNarrationSample.CommandStartMovie,
            MoviesSkipped: 0,
            MovieFrame: active ? 0 : FieldMovieNarrationSample.FrameUnknown);

    private static string Manifest(params (string Text, double Seconds)[] entries) =>
        "{ \"entries\": [" + string.Join(",", entries.Select(entry =>
            $"{{ \"text\": {System.Text.Json.JsonSerializer.Serialize(entry.Text)}, " +
            $"\"file\": \"{CutsceneVoiceManifest.FileNameFor(entry.Text)}\", " +
            $"\"duration_seconds\": " +
            $"{entry.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}")) +
        "] }";

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

        /// <summary>The device finishing on its own, before anything stopped it.</summary>
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
                $"Continuation movie overlap - {label}: expected {expected}, got {actual}.");
        }
    }
}
