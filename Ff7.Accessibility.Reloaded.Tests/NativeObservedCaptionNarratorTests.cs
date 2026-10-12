using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NativeObservedCaptionNarratorTests
{
    internal static void Run()
    {
        var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        var outputs = new List<Output>();
        var accepted = new List<string>();
        using var narrator = new NativeObservedCaptionNarrator(_ => { var output = new Output(); outputs.Add(output); return output; },
            text => { accepted.Add(text); return true; }, _ => { });
        var caption = new NativeCaptionObservation(NativeCreditsTitleSnapshotReader.CaptionText, now);
        narrator.Observe(null, now, true, true);
        narrator.Observe(caption with { ObservedUtc = now.AddSeconds(1) }, now, true, true);
        Check(outputs.Count == 0, "no output from initialized title state or future-clock evidence");
        narrator.Observe(caption, now, true, true);
        narrator.Suspend();
        Check(!outputs[0].IsPlaying, "suspension stops the title immediately");
        narrator.Observe(caption, now.AddMilliseconds(100), true, true);
        Check(outputs.Count == 1, "a racing worker cannot restart a suspended caption");
        narrator.Resume();
        now = now.AddMilliseconds(200);
        narrator.Observe(caption with { ObservedUtc = now }, now, true, true);
        outputs[1].Fail();
        narrator.Observe(caption with { ObservedUtc = now }, now.AddMilliseconds(20), true, true);
        Check(accepted.Single() == caption.Text, "failed playback retains the current native title's spoken fallback");
        narrator.Observe(caption with { ObservedUtc = now }, now.AddMilliseconds(40), true, true);
        Check(accepted.Count == 1, "a continuously rendered title is described once");
        narrator.EndEpisode();
        narrator.Observe(caption with { ObservedUtc = now }, now, true, true);
        narrator.Observe(caption with { ObservedUtc = now }, now.AddSeconds(1), true, true);
        Check(!outputs.Last().IsPlaying, "expired renderer evidence stops independent title audio");
    }
    private sealed class Output : IFieldMovieNarrationOutput, IFieldMovieNarrationCompletion
    {
        public bool IsPlaying { get; private set; }
        public bool CompletedNormally => false;
        public bool Start(string reason) { IsPlaying = true; return true; }
        public bool Stop(string reason) { IsPlaying = false; return true; }
        public void Dispose() => IsPlaying = false;
        internal void Fail() => IsPlaying = false;
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Observed native caption: " + label);
    }
}
