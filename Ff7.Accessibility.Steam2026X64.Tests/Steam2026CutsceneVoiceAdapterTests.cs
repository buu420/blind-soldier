using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// The recorded voice for scene descriptions, driven through the actual x64 runtime
/// adapter rather than through <c>CutsceneVoicePlayer</c> on its own.
///
/// <para>Everything here is about which sentence owns the one audio device and for
/// how long. Three ways of getting that wrong lose content or describe the wrong
/// scene, and none of them is visible from the player in isolation:</para>
///
/// <list type="number">
/// <item>A second description arriving while the first is still playing must stay in
/// the queue. Reading it out in the other voice instead would double up <em>and</em>
/// spend it, so those words would never be heard as recorded at all.</item>
/// <item>A film starting must take the device from a field description before its own
/// recording opens, not merely refuse to start over one.</item>
/// <item>A clip started from a film's deferred cue schedule has its own life. The
/// film ending, being skipped, becoming unreadable or being replaced has to stop it,
/// or a sentence about that film is still being read over whatever came next.</item>
/// </list>
/// </summary>
internal static class Steam2026CutsceneVoiceAdapterTests
{
    private const int Gldst = 496;
    private const int Nivl = 497;

    /// <summary>boogdemo, a reviewed catalog film the global path describes.</summary>
    private const int CatalogFilm = 42;

    /// <summary>A different reviewed catalog film, for "another film took over".</summary>
    private const int OtherCatalogFilm = 40;

    private const string First = "Barret slams a fist into the wall.";
    private const string Second = "Tifa looks away from the window.";
    private const string CueText = "The plate falls through the smoke.";

    private static readonly DateTime Timestamp = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        ABusyDeviceKeepsTheCueInsteadOfSpendingItInTheWrongVoice();
        AFilmStartingTakesTheDeviceFromAFieldDescription();
        ADeferredCueClipDiesWithTheFilmItDescribes();
        ADeferredCueClipSurvivesAFieldChangeUnderTheSameFilm();
        TheHostBoundariesStopAFieldDescriptionAsWellAsAFilm();
        TheHostSuspendsOffForegroundWhenNoFilmIsPlaying();
        TheHostStartsNoDeferredCueOffForegroundAndKeepsIt();
    }

    /// <summary>
    /// The host's own foreground branch, not the coordinator's.
    ///
    /// <para>The coordinator has done the right thing for a while: ask it to suspend
    /// and it stops every description owner. The host asked it only when
    /// <c>IsNativeFilmNarrationPlaying</c> was true, and that property reports the
    /// film's own track alone. An action description - the common case, and now the
    /// case for all 217 recorded ones - is on a device that property cannot see, so
    /// the host never asked and the description carried on over another window.</para>
    /// </summary>
    private static void TheHostSuspendsOffForegroundWhenNoFilmIsPlaying()
    {
        var fixture = SpeakingAFieldDescription("off-foreground with no film");

        // Precisely the blind spot: something is talking and the old predicate says
        // nothing is.
        Equal(false, fixture.Coordinator.IsNativeFilmNarrationPlaying,
            "no film track is playing, which is what the old host branch asked about");
        Equal(true, fixture.Clip.IsPlaying, "and yet a description is talking");

        Steam2026FieldCutsceneHostTick.ObserveOrSuspend(
            fixture.Coordinator, isHostForeground: false, Timestamp.AddSeconds(1), null);

        Equal(false, fixture.Clip.IsPlaying, "the host takes the device back anyway");
        Equal(1, fixture.Clip.Stops, "exactly once");

        // Asking again on the next unfocused frame is harmless, which is what lets
        // the branch be unconditional.
        Steam2026FieldCutsceneHostTick.ObserveOrSuspend(
            fixture.Coordinator, isHostForeground: false, Timestamp.AddSeconds(2), null);
        Equal(1, fixture.Clip.Stops, "and repeating it changes nothing");

        // The control: with the foreground, the same call is the ordinary per-frame
        // film lifecycle and still starts a film.
        var focused = SpeakingAFieldDescription("the control");
        focused.Memory.SetFilm(CatalogFilm, active: true);
        focused.Memory.MovieFrame = 0;
        Steam2026FieldCutsceneHostTick.ObserveOrSuspend(
            focused.Coordinator, isHostForeground: true, Timestamp.AddSeconds(1), null);
        Equal(true, focused.Coordinator.IsNativeFilmNarrationPlaying,
            "the control - the focused branch still runs the film lifecycle");
    }

    /// <summary>
    /// The other half of the same tick. The deferred cue dispatch was reached
    /// whatever the foreground said, so the frame that had just suspended everything
    /// could open a new clip on the same device immediately afterwards.
    ///
    /// <para>Refusing must not cost the cue: the film's schedule only advances when
    /// the words are actually taken.</para>
    /// </summary>
    private static void TheHostStartsNoDeferredCueOffForegroundAndKeepsIt()
    {
        var fixture = WithADeferredCueDue("off-foreground deferred cue");
        var spoken = new List<string>();

        Equal(false,
            Steam2026FieldCutsceneHostTick.TryDeliverDeferredFilmCue(
                fixture.Coordinator,
                isHostForeground: false,
                Timestamp.AddSeconds(12),
                null,
                text => { spoken.Add(text); return true; },
                out var offForeground,
                out var offForegroundField),
            "no cue is delivered while the window is not in front of the player");
        Equal(string.Empty, offForeground, "and nothing is reported as delivered");
        Equal(-1, offForegroundField, "with no field");
        Equal(0, fixture.Clip.Starts, "no clip is started on the device just suspended");
        Equal(0, spoken.Count, "and nothing is spoken either");

        // The window comes back while the same film is still running, and the cue is
        // still owed. It was held, not lost.
        Equal(true,
            Steam2026FieldCutsceneHostTick.TryDeliverDeferredFilmCue(
                fixture.Coordinator,
                isHostForeground: true,
                Timestamp.AddSeconds(12),
                null,
                text => { spoken.Add(text); return true; },
                out var delivered,
                out _),
            "and the same cue is delivered once the window is back");
        Equal(CueText, delivered, "it is the cue that was held");
        Equal(1, fixture.Clip.Starts, "said once, in the recorded voice");
    }

    /// <summary>
    /// The host's own boundaries - the window losing the foreground, the session
    /// being unloaded, a reset, and the film state becoming unreadable - are not
    /// about which lifetime owns the clip. They stop whatever is talking.
    ///
    /// <para>These went through the film tracker only, so a field description playing
    /// on the same device carried on into whatever the player had alt-tabbed to.</para>
    /// </summary>
    private static void TheHostBoundariesStopAFieldDescriptionAsWellAsAFilm()
    {
        var cases = new (string What, Action<FakeAddressSpace, Fixture> Apply)[]
        {
            ("the window lost the foreground", (_, f) =>
                f.Coordinator.SuspendNativeFilmNarration(FieldMovieNarrationStopReason.Suspended)),
            ("the session was unloaded", (_, f) =>
                f.Coordinator.SuspendNativeFilmNarration(FieldMovieNarrationStopReason.Unloaded)),
            ("the coordinator was reset", (_, f) => f.Coordinator.Reset()),
            ("the film state stopped reading", (memory, f) =>
            {
                memory.FilmStateReadable = false;
                f.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(1));
            }),
            ("the game put its own words on screen", (memory, f) =>
            {
                memory.ActiveMessageCount = 1;
                f.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(1), () => true);
            }),
        };

        foreach (var (what, apply) in cases)
        {
            var fixture = SpeakingAFieldDescription(what);
            apply(fixture.Memory, fixture);
            Equal(false, fixture.Clip.IsPlaying, $"{what}: the field description stops");
            Equal(1, fixture.Clip.Stops, $"{what}: exactly once");
        }
    }

    /// <summary>A field description playing in the recorded voice, and nothing else.</summary>
    private static Fixture SpeakingAFieldDescription(string what)
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var filmOutput = new FakeOutput();
        var clip = new FakeOutput();
        CutsceneVoicePlayer? voice = null;
        var narration = new FieldMovieNarrationTracker(
            _ => filmOutput,
            _ => { },
            FieldPositionReader.FieldModule,
            null,
            (owner, reason) => voice?.StopIfOwnedBy(owner, reason));
        voice = new CutsceneVoicePlayer(
            Manifest((First, 30d)), _ => clip, _ => { }, () => narration.IsPlaying);
        var coordinator = Create(memory, narration, voice);

        Equal(true, coordinator.Observe(Snapshot(Gldst, 10, 1, Timestamp)), $"{what}: the cue is queued");
        Equal(true, coordinator.TrySpeakPending(true, () => false, _ => true, Timestamp, out _),
            $"{what}: the description is delivered");
        Equal(true, clip.IsPlaying, $"{what}: in the recorded voice, and still going");
        return new Fixture(memory, coordinator, clip, filmOutput);
    }

    /// <summary>
    /// Root's first seam. The delivery step asks the recorded voice, and a "not yet"
    /// used to be indistinguishable from a "never": both were false, and false meant
    /// speak it. Two cues a frame apart - which the story scripts do constantly -
    /// therefore overlapped, and the second was spent in the screen reader's voice.
    /// </summary>
    private static void ABusyDeviceKeepsTheCueInsteadOfSpendingItInTheWrongVoice()
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var filmOutput = new FakeOutput();
        var clip = new FakeOutput();
        CutsceneVoicePlayer? voice = null;
        var narration = new FieldMovieNarrationTracker(
            _ => filmOutput,
            _ => { },
            FieldPositionReader.FieldModule,
            null,
            (owner, reason) => voice?.StopIfOwnedBy(owner, reason));
        voice = new CutsceneVoicePlayer(
            Manifest((First, 2.5d), (Second, 1.5d)),
            _ => clip,
            _ => { },
            () => narration.IsPlaying);
        var coordinator = Create(memory, narration, voice);

        var spokenAloud = new List<string>();
        bool Prism(string text) { spokenAloud.Add(text); return true; }

        // Two descriptions in the same field, a frame apart.
        Equal(true, coordinator.Observe(Snapshot(Gldst, 10, 1, Timestamp)), "the first cue is queued");
        Equal(true, coordinator.Observe(Snapshot(Gldst, 20, 2, Timestamp.AddMilliseconds(33))),
            "the second cue is queued");

        Equal(true, coordinator.TrySpeakPending(true, () => false, Prism, Timestamp, out var firstCue),
            "the first description is delivered");
        Equal(First, firstCue.Text, "the oldest first");
        Equal(1, clip.Starts, "in the recorded voice");
        Equal(0, spokenAloud.Count, "and not by the screen reader");

        // The seam. The first clip is still playing.
        Equal(false,
            coordinator.TrySpeakPending(true, () => false, Prism, Timestamp.AddMilliseconds(66), out _),
            "the second is not delivered while the first is still playing");
        Equal(0, spokenAloud.Count, "and nothing reaches the screen reader");
        Equal(1, clip.Starts, "nothing is started over the first clip");
        Equal(true, coordinator.HasPendingNarration(Gldst), "the cue is still queued");

        // The main movie holding the device is the same answer: wait, do not fall
        // back. The film has its own voice and the description is not read over it.
        clip.Finish();
        memory.SetFilm(CatalogFilm, active: true);
        memory.MovieFrame = 0;
        coordinator.ObserveNativeFilm(Timestamp.AddMilliseconds(99));
        Equal(true, coordinator.IsNativeFilmNarrationPlaying, "the film's own recording is playing");
        Equal(false,
            coordinator.TrySpeakPending(true, () => false, Prism, Timestamp.AddMilliseconds(132), out _),
            "the queued description waits for the film rather than talking over it");
        Equal(0, spokenAloud.Count, "still nothing reaches the screen reader");
        Equal(true, coordinator.HasPendingNarration(Gldst), "and it is still queued");

        // The film ends and the device is free. The cue is delivered late, but in the
        // voice it was always meant to have, and exactly once.
        memory.SetFilm(0, active: false);
        coordinator.ObserveNativeFilm(Timestamp.AddSeconds(30));
        Equal(false, coordinator.IsNativeFilmNarrationPlaying, "the film is over");
        Equal(true,
            coordinator.TrySpeakPending(true, () => false, Prism, Timestamp.AddSeconds(30), out var secondCue),
            "a later tick delivers it");
        Equal(Second, secondCue.Text, "it is the cue that was held, unreordered");
        Equal(2, clip.Starts, "in the recorded voice");
        Equal(0, spokenAloud.Count, "the screen reader was never used for a recorded description");
        Equal(false, coordinator.HasPendingNarration(Gldst), "and now it is spent");

        // The control. Busy is not a queue that has stopped delivering: a description
        // nobody recorded still reaches the screen reader, because a scene left
        // undescribed is the worst outcome there is.
        clip.Finish();
        Equal(true, coordinator.Observe(Snapshot(Gldst, 30, 3, Timestamp.AddSeconds(31))),
            "an unrecorded cue is queued");
        Equal(true,
            coordinator.TrySpeakPending(true, () => false, Prism, Timestamp.AddSeconds(31), out _),
            "the control - it is delivered");
        Equal(1, spokenAloud.Count, "by the screen reader");
    }

    /// <summary>
    /// Root's second seam. Ownership only worked one way: a description would not
    /// start over a playing film, but a film would start over a playing description,
    /// because the native tick that starts a film never consulted the recorded voice.
    /// The description has to give way <em>before</em> the film's own output opens -
    /// not after, and without the film being delayed or restarted from zero.
    /// </summary>
    private static void AFilmStartingTakesTheDeviceFromAFieldDescription()
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var filmOutput = new FakeOutput();
        var clip = new FakeOutput();

        // Wired exactly as the research session wires it: the tracker can take the
        // description device back, and the player can see the film holding it.
        CutsceneVoicePlayer? voice = null;
        var narration = new FieldMovieNarrationTracker(
            _ => filmOutput,
            _ => { },
            FieldPositionReader.FieldModule,
            null,
            (owner, reason) => voice?.StopIfOwnedBy(owner, reason));
        voice = new CutsceneVoicePlayer(
            Manifest((First, 6d)), _ => clip, _ => { }, () => narration.IsPlaying);
        var coordinator = Create(memory, narration, voice);

        Equal(true, coordinator.Observe(Snapshot(Gldst, 10, 1, Timestamp)), "the cue is queued");
        Equal(true, coordinator.TrySpeakPending(true, () => false, _ => true, Timestamp, out _),
            "the description plays in the recorded voice");
        Equal(true, clip.IsPlaying, "and is still going");

        // Six seconds of description, and the film starts one second in.
        memory.SetFilm(CatalogFilm, active: true);
        memory.MovieFrame = 0;
        coordinator.ObserveNativeFilm(Timestamp.AddSeconds(1));

        Equal(false, clip.IsPlaying, "the description gives way to the film");
        Equal(1, clip.Stops, "exactly once");
        Equal(true, coordinator.IsNativeFilmNarrationPlaying, "and the film is being described");
        Equal(1, filmOutput.Starts, "the film started once");

        // The film must not have been held back or restarted while the device was
        // handed over: the frames that follow are the same film, from where it is.
        for (var frame = 1; frame <= 10; frame++)
        {
            memory.MovieFrame = (ushort)(frame * 15);
            coordinator.ObserveNativeFilm(Timestamp.AddSeconds(1 + frame));
        }

        Equal(1, filmOutput.Starts, "and is never restarted from zero");
        Equal(true, coordinator.IsNativeFilmNarrationPlaying, "and is still playing");
    }

    /// <summary>
    /// Root's third seam. A cue spoken from a film's deferred schedule is a clip with
    /// its own life on the device: it is still talking after the schedule that
    /// produced it has been dropped, after the schedule has run out, and after the
    /// film itself has gone. Every way of losing the film has to take it back.
    /// </summary>
    private static void ADeferredCueClipDiesWithTheFilmItDescribes()
    {
        // Each case is a different way the film stops being what is on screen. The
        // first four are what a native tick can see; the last two are the host.
        var cases = new (string What, Action<FakeAddressSpace, Fixture> Apply)[]
        {
            ("the film ended", (memory, f) =>
            {
                memory.SetFilm(0, active: false);
                f.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(30));
            }),
            ("the player skipped it", (memory, f) =>
            {
                memory.MoviesSkipped = 1;
                f.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(30));
            }),
            ("its frame stopped reading", (memory, f) =>
            {
                memory.MovieFrameReadable = false;
                f.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(30));
            }),
            ("the command byte changed hands", (memory, f) =>
            {
                memory.MovieCommand = FieldMovieNarrationSample.CommandStopMovie;
                f.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(30));
            }),
            ("another film took over", (memory, f) =>
            {
                memory.SetFilm(OtherCatalogFilm, active: true);
                memory.MovieFrame = 0;
                f.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(30));
            }),
            ("the whole film state stopped reading", (memory, f) =>
            {
                memory.FilmStateReadable = false;
                f.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(30));
            }),
            ("the window lost the foreground", (_, f) =>
                f.Coordinator.SuspendNativeFilmNarration(
                    FieldMovieNarrationStopReason.Suspended)),
            ("the session was unloaded", (_, f) =>
                f.Coordinator.SuspendNativeFilmNarration(FieldMovieNarrationStopReason.Unloaded)),
            ("the coordinator was reset", (_, f) => f.Coordinator.Reset()),
        };

        foreach (var (what, apply) in cases)
        {
            var fixture = SpeakingADeferredCue(what);
            apply(fixture.Memory, fixture);
            Equal(false, fixture.Clip.IsPlaying, $"{what}: the cue's clip stops");
            Equal(1, fixture.Clip.Stops, $"{what}: exactly once");
        }
    }

    /// <summary>
    /// The control for the case above, and the reason ownership is tracked at all.
    /// One film runs across several fields - the Highwind sequence does - and its cue
    /// is about the film, not about the room. A field change must leave it alone.
    /// </summary>
    private static void ADeferredCueClipSurvivesAFieldChangeUnderTheSameFilm()
    {
        var fixture = SpeakingADeferredCue("the control");

        // The story moves to the next field while the same verified film runs on.
        fixture.Memory.FieldId = Nivl;
        fixture.Memory.MovieFrame = 15 * 22;
        fixture.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(22));
        Equal(true, fixture.Clip.IsPlaying,
            "a field change under the same film must not stop the film's own cue");

        // A field-bound description would have gone, which is what makes the two
        // lifetimes different rather than the same rule written twice.
        Equal(true,
            fixture.Coordinator.Observe(Snapshot(Nivl, 10, 9, Timestamp.AddSeconds(22))),
            "the new field queues its own cue");
        Equal(true, fixture.Clip.IsPlaying, "and that alone still does not stop the film's cue");

        // And the film ending does stop it, so the survival above is ownership and
        // not a clip nothing can reach.
        fixture.Memory.SetFilm(0, active: false);
        fixture.Coordinator.ObserveNativeFilm(Timestamp.AddSeconds(40));
        Equal(false, fixture.Clip.IsPlaying, "the film ending stops it");
    }

    /// <summary>
    /// Drives the real adapter to the state every case above starts from: a film
    /// playing its own recording, the game talking over it so the recording gives way
    /// to its cue schedule, and one of those cues now being spoken by a clip.
    /// </summary>
    private static Fixture SpeakingADeferredCue(string what)
    {
        var fixture = WithADeferredCueDue(what);
        Equal(true,
            fixture.Coordinator.TryDeliverDeferredFilmCue(
                Timestamp.AddSeconds(12), null, _ => true, out var delivered, out _),
            $"{what}: the film's cue is delivered");
        Equal(CueText, delivered, $"{what}: it is the cue whose moment it is");
        Equal(1, fixture.Clip.Starts, $"{what}: in the recorded voice");
        Equal(true, fixture.Clip.IsPlaying, $"{what}: and it is still playing");
        return fixture;
    }

    /// <summary>
    /// The same film, stopped one step earlier: its recording has given way, its cue
    /// schedule has taken over, and the cue whose moment it is has not been offered
    /// to anybody yet.
    /// </summary>
    private static Fixture WithADeferredCueDue(string what)
    {
        var memory = new FakeAddressSpace { Module = 1, FieldId = Gldst };
        var filmOutput = new FakeOutput();
        var clip = new FakeOutput();
        CutsceneVoicePlayer? voice = null;
        var narration = new FieldMovieNarrationTracker(
            _ => filmOutput,
            _ => { },
            FieldPositionReader.FieldModule,
            _ => [new MovieNarrationCue(10d, 16d, CueText)],
            (owner, reason) => voice?.StopIfOwnedBy(owner, reason));
        voice = new CutsceneVoicePlayer(
            Manifest((CueText, 30d)), _ => clip, _ => { }, () => narration.IsPlaying);
        var coordinator = Create(memory, narration, voice);

        memory.SetFilm(CatalogFilm, active: true);
        memory.MovieFrame = 0;
        coordinator.ObserveNativeFilm(Timestamp);
        Equal(true, coordinator.IsNativeFilmNarrationPlaying, $"{what}: the film's recording starts");

        // The game puts its own words on screen eleven seconds in. The recording has
        // no way to duck, so it stops and the rest of the film is described from its
        // cue windows instead.
        memory.MovieFrame = 15 * 11;
        memory.ActiveMessageCount = 1;
        coordinator.ObserveNativeFilm(Timestamp.AddSeconds(11), () => true);
        Equal(false, coordinator.IsNativeFilmNarrationPlaying,
            $"{what}: the recording gives way to the game's own words");

        // The dialogue closes and the cue whose moment this is gets said - in the
        // recorded voice, which is a clip on the same device with a long way to run.
        memory.ActiveMessageCount = 0;
        memory.MovieFrame = 15 * 12;
        coordinator.ObserveNativeFilm(Timestamp.AddSeconds(12));
        Equal(0, clip.Starts, $"{what}: nothing has said the cue yet");

        return new Fixture(memory, coordinator, clip, filmOutput);
    }

    private sealed record Fixture(
        FakeAddressSpace Memory,
        Steam2026FieldCutsceneDescriptionCoordinator Coordinator,
        FakeOutput Clip,
        FakeOutput FilmOutput);

    private static Steam2026FieldCutsceneDescriptionCoordinator Create(
        FakeAddressSpace memory,
        FieldMovieNarrationTracker narration,
        CutsceneVoicePlayer voice) =>
        new(memory, Cues(), narration, voice);

    /// <summary>
    /// Three plain script descriptions in each field, at addresses no film anchor
    /// claims, so the film narration path reports <c>NotDescribed</c> and the
    /// ordinary spoken-description path is what runs.
    /// </summary>
    private static IEnumerable<FieldCutsceneDescriptionCue> Cues()
    {
        foreach (var fieldId in new[] { Gldst, Nivl })
        {
            yield return new FieldCutsceneDescriptionCue(
                fieldId, 7, 3, 10, First, FieldOpcodeAddressResolver.OpcodeRequestIndex);
            yield return new FieldCutsceneDescriptionCue(
                fieldId, 7, 3, 20, Second, FieldOpcodeAddressResolver.OpcodeRequestIndex);
            yield return new FieldCutsceneDescriptionCue(
                fieldId, 7, 3, 30, "Nobody recorded this one.",
                FieldOpcodeAddressResolver.OpcodeRequestIndex);
        }
    }

    private static Steam2026FieldCutsceneIngressSnapshot Snapshot(
        int fieldId, int byteIndex, int sequence, DateTime capturedUtc) =>
        new(
            sequence,
            capturedUtc,
            new FieldScriptContext(
                fieldId, 7, 3, byteIndex, FieldOpcodeAddressResolver.OpcodeRequestIndex));

    private static CutsceneVoiceManifest Manifest(params (string Text, double Seconds)[] entries) =>
        CutsceneVoiceManifest.Parse(
            "{ \"entries\": [" + string.Join(",", entries.Select(entry =>
                $"{{ \"text\": {System.Text.Json.JsonSerializer.Serialize(entry.Text)}, " +
                $"\"file\": \"{CutsceneVoiceManifest.FileNameFor(entry.Text)}\", " +
                $"\"duration_seconds\": " +
                $"{entry.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}")) +
            "] }");

    private sealed class FakeAddressSpace : ILegacyAddressSpace
    {
        private const uint ScriptContext = 0x00700000;

        public byte Module { get; set; } = 1;
        public ushort FieldId { get; set; }
        public byte ActiveMessageCount { get; set; }
        public byte Disc { get; set; } = 1;
        public byte MovieCommand { get; set; } = FieldMovieNarrationSample.CommandStartMovie;
        public byte MoviesSkipped { get; set; }
        public ushort MovieFrame { get; set; }

        /// <summary>Whether the film's own frame counter still reads.</summary>
        public bool MovieFrameReadable { get; set; } = true;

        /// <summary>Whether the film's active flag and number still read at all.</summary>
        public bool FilmStateReadable { get; set; } = true;

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
                case (uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount
                    when destination.Length == 1:
                    destination[0] = ActiveMessageCount;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldScriptContextPointer
                    when destination.Length == 4:
                    return Write(destination, ScriptContext);
                case ScriptContext + FieldAudibleCueStateReader.FieldScriptContextStateOffset
                    when destination.Length == 1:
                    destination[0] = FieldMovieNarrationPolicy.MovieHandlerStateInProgress;
                    return true;
                case ScriptContext + FieldAudibleCueStateReader.FieldScriptContextPhaseOffset
                    when destination.Length == 2:
                    return Write(destination, (ushort)FieldMovieNarrationPolicy.MovieHandlerPhaseYielding);
                case (uint)FieldPositionReader.AddressFieldId when destination.Length == 2:
                    return Write(destination, FieldId);
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieActive when destination.Length == 2:
                    return FilmStateReadable && Write(destination, movieActive);
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieNumber when destination.Length == 2:
                    return FilmStateReadable && Write(destination, movieNumber);
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
                    return MovieFrameReadable && Write(destination, MovieFrame);
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

        /// <summary>The clip reaching its own end, which is not a stop.</summary>
        public void Finish() => playing = false;

        public bool Start(string reason)
        {
            Starts++;
            playing = true;
            return true;
        }

        public bool Stop(string reason)
        {
            if (playing)
            {
                Stops++;
            }

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
                $"x64 cutscene voice adapter - {label}: expected {expected}, got {actual}.");
        }
    }
}
