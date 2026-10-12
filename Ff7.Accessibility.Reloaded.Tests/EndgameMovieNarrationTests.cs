using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class EndgameMovieNarrationTests
{
    internal static void Run()
    {
        StartsOutsideFieldAndDoesNotRepeat();
        RefusalRetainsCurrentCueAndDropsExpiredCue();
        NativeClockPausesAndReplacementStops();
        LateAttachmentDescribesCurrentShot();
        UnknownAndForegroundLossStopOwnedAudio();
        NewEpisodeRestartsAfterNativeBoundary();
        FailedAfterStartFallsBackInTheCurrentShot();
        SuspensionStopsImmediatelyAndResumesCurrentShot();
    }

    private static void StartsOutsideFieldAndDoesNotRepeat()
    {
        using var f = new Fixture();
        f.Tick("ending2.avi", 25, 0);
        f.Tick("ending2.avi", 25, 1);
        Equal(1, f.Outputs.Count, "ending2 starts in module25 exactly once");
        f.Tick("ending3.avi", 28, 30);
        Equal(2, f.Outputs.Count, "postcredits starts in credits module28");
        Equal(true, f.Outputs[0].Stopped, "replacement stops the previous movie cue");
        f.Tick("ending3.avi", 1, 31);
        Equal(true, f.Outputs[1].Stopped, "a field cannot claim the postcredits movie");
    }

    private static void RefusalRetainsCurrentCueAndDropsExpiredCue()
    {
        using var f = new Fixture { AllowOutput = false, AllowSpeech = false };
        f.Tick("ending2.avi", 25, 0);
        Equal(0, f.Accepted.Count, "refused output is not consumed");
        f.AllowSpeech = true;
        f.Tick("ending2.avi", 25, 1);
        Equal("first", f.Accepted.Single(), "current cue retried after backend refusal");
        f.Tick("ending2.avi", 25, 150);
        Equal(1, f.Accepted.Count, "expired cue is never spoken late");
    }

    private static void NativeClockPausesAndReplacementStops()
    {
        using var f = new Fixture();
        f.Tick("ending2.avi", 25, 0);
        f.Tick("ending2.avi", 25, 1, paused: true);
        Equal(true, f.Outputs[0].Paused, "native pause holds owned audio");
        f.Tick("ending2.avi", 25, 2);
        Equal(false, f.Outputs[0].Paused, "native resume releases owned audio");
        f.Tick("ending2.avi", 25, 2, advanceMilliseconds: 500);
        Equal(true, f.Outputs[0].Paused, "decoder frame stall holds owned audio");
        f.Tick("ending2.avi", 25, 3);
        Equal(false, f.Outputs[0].Paused, "frame progression resumes owned audio");
        f.Tick("other.avi", 25, 4);
        Equal(true, f.Outputs[0].Stopped, "unknown replacement stops audio");
    }

    private static void LateAttachmentDescribesCurrentShot()
    {
        using var f = new Fixture();
        f.Tick("ending2.avi", 25, 75);
        Equal("second", f.Started.Single(), "late attachment uses the current shot");
        f.Tick("ending2.avi", 25, 150);
        Equal(true, f.Outputs.Single().Stopped, "frame jump expires a still playing old shot");
    }

    private static void UnknownAndForegroundLossStopOwnedAudio()
    {
        using var f = new Fixture();
        f.Tick("ending2.avi", 25, 0);
        f.Tracker.Observe(null, f.Now, enabled: true, foreground: true);
        Equal(true, f.Outputs[0].Stopped, "unreadable state stops audio");
        f.Tick("ending2.avi", 25, 1);
        Equal(2, f.Outputs.Count, "an interrupted current cue can be recovered");
        f.Tick("ending2.avi", 25, 2, foreground: false);
        Equal(true, f.Outputs[1].Stopped, "background cannot keep independent narration playing");
        f.Tick("ending2.avi", 25, 3, enabled: false);
        Equal(2, f.Outputs.Count, "disabled descriptions cannot open another output");
    }

    private static void NewEpisodeRestartsAfterNativeBoundary()
    {
        using var f = new Fixture();
        f.Tick("ending2.avi", 25, 0);
        f.Tick("ending2.avi", 25, 1, playing: false);
        f.Tick("ending2.avi", 25, 0);
        Equal(2, f.Outputs.Count, "a native stop followed by start is a new movie episode");
    }

    private static void FailedAfterStartFallsBackInTheCurrentShot()
    {
        using var f = new Fixture();
        f.Tick("ending2.avi", 25, 0);
        f.Outputs.Single().Fail();
        f.Tick("ending2.avi", 25, 1);
        Equal("first", f.Accepted.Single(), "a device failure must retain the current cue for text fallback");
        Equal(1, f.Outputs.Count, "a failed device is not reopened every worker frame");
        f.Tick("ending2.avi", 25, 2);
        Equal(1, f.Accepted.Count, "an accepted fallback is not repeated");
    }

    private static void SuspensionStopsImmediatelyAndResumesCurrentShot()
    {
        using var f = new Fixture();
        f.Tick("ending2.avi", 25, 0);
        f.Tracker.Suspend();
        Equal(true, f.Outputs.Single().Stopped, "suspension stops independent audio before a worker can block");
        f.Tick("ending2.avi", 25, 1);
        Equal(1, f.Outputs.Count, "a racing worker cannot restart suspended audio");
        f.Tracker.Resume();
        f.Tick("ending2.avi", 25, 75);
        Equal("second", f.Started.Last(), "resume describes the current shot rather than the expired first shot");
    }

    private sealed class Fixture : IDisposable
    {
        internal bool AllowOutput = true;
        internal bool AllowSpeech = true;
        internal DateTime Now = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        internal readonly List<Output> Outputs = [];
        internal readonly List<string> Accepted = [];
        internal readonly List<string> Started = [];
        internal readonly EndgameMovieNarrationTracker Tracker;

        internal Fixture()
        {
            Tracker = new EndgameMovieNarrationTracker(
                film => film == "ending3.avi"
                    ? [new(2, 4.34, "postcredits")]
                    : [new(0, 3.65, "first"), new(5, 7, "second")],
                text =>
                {
                    if (!AllowOutput) return null;
                    var output = new Output();
                    Outputs.Add(output);
                    Started.Add(text);
                    return output;
                },
                text => { if (!AllowSpeech) return false; Accepted.Add(text); return true; },
                _ => { });
        }

        internal void Tick(string film, int module, uint frame, bool paused = false,
            bool playing = true, bool foreground = true, bool enabled = true,
            int advanceMilliseconds = 35)
        {
            Now = Now.AddMilliseconds(advanceMilliseconds);
            Tracker.Observe(new(film, module, frame, playing, paused), Now, enabled, foreground);
        }

        public void Dispose() => Tracker.Dispose();
    }

    private sealed class Output : IFieldMovieNarrationOutput, IFieldMovieNarrationPause, IFieldMovieNarrationCompletion
    {
        public bool IsPlaying { get; private set; }
        internal bool Paused;
        internal bool Stopped;
        public bool CompletedNormally { get; private set; }
        internal void Fail() { IsPlaying = false; CompletedNormally = false; }
        public bool Start(string reason) { IsPlaying = true; return true; }
        public bool Stop(string reason) { Stopped = true; IsPlaying = false; return true; }
        public bool SetPaused(bool paused) { Paused = paused; return true; }
        public void Dispose() => IsPlaying = false;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}
