using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NativeCreditsNarratorTests
{
    internal static void Run()
    {
        ReadsOnlyCurrentlyVisibleRowsWithoutRepeating();
        RefusalDoesNotConsumeOrQueueOffscreenRows();
        OwnedIntroductionStopsOnSuspensionAndFailureFallsBack();
        ThrownStartCannotOrphanAudioEvenWhenLoggingFails();
        EmptyViewportDoesNotDescribeInvisibleCredits();
    }

    private static void ReadsOnlyCurrentlyVisibleRowsWithoutRepeating()
    {
        using var f = new Fixture();
        f.Tick(0, "DIRECTOR");
        Equal(NativeCreditsNarrator.Introduction, f.Accepted.Single(), "credits introduction is grounded in the observed renderer");
        f.Tick(0, "DIRECTOR", seconds: 5);
        Equal("DIRECTOR", f.Accepted.Last(), "visible native credit row is readable");
        f.Tick(0, "DIRECTOR", seconds: 3);
        Equal(2, f.Accepted.Count, "stationary credits do not repeat");
        f.Tick(1, "VISIBLE NAME", seconds: 3);
        Equal("VISIBLE NAME", f.Accepted.Last(), "a new visible row is read as it scrolls into view");
        f.Tracker.Observe(null, f.Now, true, true);
        Equal(3, f.Accepted.Count, "unreadable or absent renderer does not invent credits");
    }

    private static void RefusalDoesNotConsumeOrQueueOffscreenRows()
    {
        using var f = new Fixture();
        f.Tick(0, "OLD NAME");
        f.AllowSpeech = false;
        f.Tick(0, "OLD NAME", seconds: 5);
        f.AllowSpeech = true;
        f.Tick(1, "NEW NAME", seconds: 1);
        Equal("NEW NAME", f.Accepted.Last(), "a declined row that left the viewport is never read late");
        Equal(false, f.Accepted.Contains("OLD NAME"), "only accepted currently visible text is committed");
    }

    private static void OwnedIntroductionStopsOnSuspensionAndFailureFallsBack()
    {
        using var f = new Fixture { UseOutput = true };
        f.Tick(0, "DIRECTOR");
        Equal(true, f.Output.IsPlaying, "approved introduction recording starts");
        f.Tracker.Suspend();
        Equal(false, f.Output.IsPlaying, "suspension stops independent credits output immediately");
        f.Tick(1, "VISIBLE NAME", seconds: 2);
        Equal(1, f.Output.Starts, "a racing worker cannot restart suspended credits");
        f.Tracker.Resume();
        f.Tick(1, "VISIBLE NAME", seconds: 1);
        f.Output.Fail();
        f.Tick(1, "VISIBLE NAME", seconds: 1);
        Equal(NativeCreditsNarrator.Introduction, f.Accepted.Single(), "failed recorded introduction retains its spoken fallback");
    }

    private static void EmptyViewportDoesNotDescribeInvisibleCredits()
    {
        using var f = new Fixture { UseOutput = true };
        f.Tracker.Observe(new(0, 240, []), f.Now, true, true);
        Equal(0, f.Output.Starts, "an empty renderer viewport cannot authorize an introduction");
        Equal(0, f.Accepted.Count, "no names are invented before the first visible row");
        f.Tick(0, "DIRECTOR");
        Equal(1, f.Output.Starts, "the introduction remains available when text becomes visible");
    }

    private sealed class Fixture : IDisposable
    {
        internal bool AllowSpeech = true;
        internal bool UseOutput;
        internal bool ThrowLog;
        internal DateTime Now = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        internal List<string> Accepted = [];
        internal Output Output = new();
        internal NativeCreditsNarrator Tracker;
        internal Fixture() => Tracker = new(() => UseOutput ? Output : null,
            text => { if (!AllowSpeech) return false; Accepted.Add(text); return true; }, _ => { if (ThrowLog) throw new InvalidOperationException(); });
        internal void Tick(int index, string text, double seconds = 0.1)
        {
            Now = Now.AddSeconds(seconds);
            Tracker.Observe(new(index, 0, [new(index, 0, text, 30)]), Now, true, true);
        }
        public void Dispose() => Tracker.Dispose();
    }

    private sealed class Output : IFieldMovieNarrationOutput, IFieldMovieNarrationCompletion
    {
        public bool IsPlaying { get; private set; }
        public bool CompletedNormally => false;
        internal int Starts;
        internal bool ThrowStart;
        internal int Disposes;
        public bool Start(string reason) { Starts++; IsPlaying = true; if (ThrowStart) throw new InvalidOperationException(); return true; }
        public bool Stop(string reason) { IsPlaying = false; return true; }
        internal void Fail() => IsPlaying = false;
        public void Dispose() { Disposes++; IsPlaying = false; }
    }

    private static void ThrownStartCannotOrphanAudioEvenWhenLoggingFails()
    {
        using var f = new Fixture { UseOutput = true, ThrowLog = true };
        f.Output.ThrowStart = true;
        f.Tick(0, "DIRECTOR");
        f.Tracker.Suspend();
        Equal(false, f.Output.IsPlaying, "a thrown start cannot leave unowned audio running");
        Equal(1, f.Output.Disposes, "every unadopted device is disposed in finally");
        Equal(NativeCreditsNarrator.Introduction, f.Accepted.Single(), "failure reporting cannot defeat the text fallback");
    }
    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Credits narration {label}: expected {expected}, got {actual}.");
    }
}
