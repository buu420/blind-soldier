using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The x86 half of root's P1 finding, which lives at the seam between the native
/// callback and the worker rather than inside the tracker.
///
/// The detour reads the script context, captures the film sample and the instant,
/// calls the original, and enqueues all of it. The worker drains that later and
/// reads the live film state separately. The defect being guarded against is that
/// the native MOVIE handler turns its own fresh 0 into a 4 during the original, so
/// a worker that re-reads the handler state sees a repeat and rejects the very
/// first described film - and a worker that re-reads the clock gives a backed-up
/// queue a fresh start window.
///
/// These tests drive the real queue and the real tracker in the same order the
/// production path does. Handing the tracker a hand-made fresh sample, as the
/// earlier tests did, cannot fail on any of this.
/// </summary>
internal static class FieldMovieNarrationQueueSeamTests
{
    private const int Gldst = 496;
    private const int ArrivalByte = 190;
    private const int ArrivalFilm = 40;

    private static readonly DateTime Start = new(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        TheCapturedStateSurvivesTheOriginalAndTheQueue();
        TheCaptureInstantSurvivesADelayedDrain();
        AnUncapturedSampleFallsBackRatherThanClaimingAFreshEntry();
        OtherQueuedEventsKeepTheirOwnPayload();
    }

    /// <summary>
    /// Replays the production ordering: capture at the boundary, the original
    /// mutates the handler state and raises the film, then the queue drains.
    /// </summary>
    private static void TheCapturedStateSurvivesTheOriginalAndTheQueue()
    {
        var queue = new NativeFieldHookEventQueue(capacity: 16);
        var output = new FakeOutput();
        var tracker = new FieldMovieNarrationTracker(
            _ => output, _ => { }, FieldPositionReader.FieldModule);

        // At the boundary: the handler has not run yet, so its state is the fresh 0
        // and the film is not active.
        var captured = new FieldMovieNarrationSample(
            MovieActive: false,
            MovieNumber: ArrivalFilm,
            CurrentModule: FieldPositionReader.FieldModule,
            CurrentFieldId: Gldst,
            MovieHandlerState: FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry,
            MovieHandlerPhase: 0);
        Equal(true,
            queue.TryCaptureCutsceneContext(
                new FieldScriptContext(Gldst, 0, 0, ArrivalByte, FieldOpcodeAddressResolver.OpcodeMovieIndex),
                hasIngressMovieSample: true,
                captured,
                Start.Ticks),
            "the boundary capture is queued");

        // The original has since run: the handler state is 4 and the film is up.
        var live = captured with
        {
            MovieActive = true,
            MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateInProgress,
            MovieHandlerPhase = FieldMovieNarrationPolicy.MovieHandlerPhaseYielding
        };

        Equal(true, queue.TryDequeue(out var drained), "the worker drains it");
        Equal(true, drained.HasIngressMovieSample, "the captured sample survives the queue");
        Equal(FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry,
            drained.IngressMovieSample.MovieHandlerState,
            "and still carries the pre-original handler state");

        Equal(true, Drain(tracker, drained, live), "the very first described film opens its opportunity");
        Equal(FieldMovieNarrationStartResult.Started,
            tracker.Begin(Gldst, 0, 0, ArrivalByte, live, Start.AddMilliseconds(100)),
            "and the track starts");
        Equal(1, output.Starts, "exactly one start");

        // Every yielded repeat now carries state 4 at the boundary too.
        for (var frame = 1; frame <= 30; frame++)
        {
            queue.TryCaptureCutsceneContext(
                new FieldScriptContext(Gldst, 0, 0, ArrivalByte, FieldOpcodeAddressResolver.OpcodeMovieIndex),
                hasIngressMovieSample: true,
                live,
                Start.AddSeconds(frame).Ticks);
            Equal(true, queue.TryDequeue(out var repeat), "the repeat drains");
            Equal(false, Drain(tracker, repeat, live), "a handler repeat must never reopen the opportunity");
        }

        Equal(1, output.Starts, "the repeats must not start a second track");
        Equal(FieldMovieNarrationStartResult.Started,
            tracker.Begin(Gldst, 0, 0, ArrivalByte, live, Start.AddSeconds(20)),
            "a delivery during playback reports the running track");
        Equal(1, output.Starts, "and does not restart it");
    }

    /// <summary>
    /// The two-second start window has to run from the instant the opcode ran, not
    /// from whenever the worker got round to it.
    /// </summary>
    private static void TheCaptureInstantSurvivesADelayedDrain()
    {
        var queue = new NativeFieldHookEventQueue(capacity: 16);
        var output = new FakeOutput();
        var tracker = new FieldMovieNarrationTracker(
            _ => output, _ => { }, FieldPositionReader.FieldModule);
        var live = new FieldMovieNarrationSample(
            MovieActive: true,
            MovieNumber: ArrivalFilm,
            CurrentModule: FieldPositionReader.FieldModule,
            CurrentFieldId: Gldst,
            MovieHandlerState: FieldMovieNarrationPolicy.MovieHandlerStateInProgress,
            MovieHandlerPhase: FieldMovieNarrationPolicy.MovieHandlerPhaseYielding);

        queue.TryCaptureCutsceneContext(
            new FieldScriptContext(Gldst, 0, 0, ArrivalByte, FieldOpcodeAddressResolver.OpcodeMovieIndex),
            hasIngressMovieSample: true,
            live with
            {
                MovieActive = false,
                MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry,
                MovieHandlerPhase = 0
            },
            Start.Ticks);

        Equal(true, queue.TryDequeue(out var drained), "the worker drains it, three seconds late");
        Equal(Start, new DateTime(drained.IngressTimestampTicks, DateTimeKind.Utc),
            "the captured instant is what survives, not the drain time");

        Drain(tracker, drained, live);
        Equal(FieldMovieNarrationStartResult.Unavailable,
            tracker.Begin(Gldst, 0, 0, ArrivalByte, live, Start.AddSeconds(3)),
            "a three-second-old capture is already past its start window");
        Equal(0, output.Starts, "so no track starts part-way through the film");
    }

    /// <summary>
    /// A failed capture must be unknown, never a plausible-looking fresh entry.
    /// </summary>
    private static void AnUncapturedSampleFallsBackRatherThanClaimingAFreshEntry()
    {
        var queue = new NativeFieldHookEventQueue(capacity: 16);
        var output = new FakeOutput();
        var tracker = new FieldMovieNarrationTracker(
            _ => output, _ => { }, FieldPositionReader.FieldModule);

        queue.TryCaptureCutsceneContext(
            new FieldScriptContext(Gldst, 0, 0, ArrivalByte, FieldOpcodeAddressResolver.OpcodeMovieIndex));
        Equal(true, queue.TryDequeue(out var drained), "the uncaptured event drains");
        Equal(false, drained.HasIngressMovieSample, "and reports that it carries no sample");
        Equal(0L, drained.IngressTimestampTicks, "nor an instant");

        var live = new FieldMovieNarrationSample(
            MovieActive: false,
            MovieNumber: ArrivalFilm,
            CurrentModule: FieldPositionReader.FieldModule,
            CurrentFieldId: Gldst,
            MovieHandlerState: FieldMovieNarrationPolicy.MovieHandlerStateInProgress,
            MovieHandlerPhase: FieldMovieNarrationPolicy.MovieHandlerPhaseYielding);

        // The worker substitutes an explicitly unknown handler state, so the tracker
        // uses its own bookkeeping instead of trusting the mutated live value.
        Equal(true, Drain(tracker, drained, live, fallbackUtc: Start),
            "an unknown handler state falls back to the tracker's own bookkeeping");
        Equal(FieldMovieNarrationStartResult.WaitingForNativeStart,
            tracker.Begin(Gldst, 0, 0, ArrivalByte, live, Start.AddMilliseconds(50)),
            "and the cue is held for the film rather than dropped");
    }

    /// <summary>
    /// Adding fields to the shared queue must not disturb the other event kinds.
    /// </summary>
    private static void OtherQueuedEventsKeepTheirOwnPayload()
    {
        var queue = new NativeFieldHookEventQueue(capacity: 16);
        queue.TryCaptureMessageOpen(3, 7, 1);
        queue.TryCaptureAskCursor(Gldst, 1, 2, 0, 3, 2, lifecycleToken: 99);
        queue.TryCaptureCutsceneContext(
            new FieldScriptContext(Gldst, 0, 0, ArrivalByte, FieldOpcodeAddressResolver.OpcodeMovieIndex),
            hasIngressMovieSample: true,
            new FieldMovieNarrationSample(true, ArrivalFilm, 1, Gldst, 0, 0),
            Start.Ticks);

        Equal(true, queue.TryDequeue(out var message), "the message event drains first");
        Equal(NativeFieldHookEventKind.MessageOpen, message.Kind, "in order");
        Equal(3, message.WindowId, "with its window");
        Equal(7, message.DialogId, "and its dialogue");
        Equal(false, message.HasIngressMovieSample, "and no film sample of its own");

        Equal(true, queue.TryDequeue(out var ask), "the ask event drains next");
        Equal(NativeFieldHookEventKind.AskCursor, ask.Kind, "in order");
        Equal(2, ask.CurrentQuestionLine, "with its cursor line");
        Equal(99L, ask.LifecycleToken, "and its lifecycle token");

        Equal(true, queue.TryDequeue(out var cutscene), "the cutscene event drains last");
        Equal(NativeFieldHookEventKind.CutsceneContext, cutscene.Kind, "in order");
        Equal(ArrivalByte, cutscene.ScriptContext.ByteIndex, "with its context");
        Equal(true, cutscene.HasIngressMovieSample, "and its film sample");
        Equal(false, queue.TryDequeue(out _), "and the queue is then empty");
    }

    /// <summary>
    /// The worker's own step, written exactly as Mod.HandleFieldCutsceneDescription
    /// does it: the captured sample where one exists, an explicitly unknown handler
    /// state where it does not, the captured instant, and the live state alongside.
    /// </summary>
    private static bool Drain(
        FieldMovieNarrationTracker tracker,
        NativeFieldHookEvent hookEvent,
        FieldMovieNarrationSample liveSample,
        DateTime? fallbackUtc = null)
    {
        var ingressSample = hookEvent.HasIngressMovieSample
            ? hookEvent.IngressMovieSample
            : liveSample with
            {
                MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateUnknown,
                MovieHandlerPhase = FieldMovieNarrationPolicy.MovieHandlerStateUnknown
            };
        var ingressUtc = hookEvent.IngressTimestampTicks > 0
            ? new DateTime(hookEvent.IngressTimestampTicks, DateTimeKind.Utc)
            : fallbackUtc ?? Start;
        return tracker.NoteIngress(
            hookEvent.ScriptContext.FieldId,
            hookEvent.ScriptContext.EntityId,
            hookEvent.ScriptContext.ScriptId,
            hookEvent.ScriptContext.ByteIndex,
            hookEvent.ScriptContext.Opcode,
            ingressSample,
            ingressUtc,
            liveSample);
    }

    private sealed class FakeOutput : IFieldMovieNarrationOutput
    {
        private bool playing;
        public int Starts { get; private set; }
        public bool IsPlaying => playing;

        public bool Start(string reason)
        {
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
            throw new InvalidOperationException(
                $"Field movie narration queue seam - {message}: expected {expected}, actual {actual}.");
    }
}
