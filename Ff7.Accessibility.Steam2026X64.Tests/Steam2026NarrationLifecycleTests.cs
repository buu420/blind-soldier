using System.Reflection;
using System.Runtime.CompilerServices;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;

internal static class Steam2026NarrationLifecycleTests
{
    internal static void Run()
    {
        // The lifecycle methods need no game process. Deliberately omit the worker
        // and every native input/hook dependency; exercise the actual publication
        // method and loader transitions concurrently.
        var session = (Steam2026ResearchSession)RuntimeHelpers.GetUninitializedObject(typeof(Steam2026ResearchSession));
        Set("narrationLifecycleSync", new object());
        Set("resumeGate", new ManualResetEventSlim(true));
        Set("cancellation", new CancellationTokenSource());
        Set("config", new AccessibilityConfig { EnableSpeech = false });
        Set("log", (Action<string>)(_ => { }));
        var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            session.Suspend();
            var endgameSpeech = 0; var creditsSpeech = 0; var titleSpeech = 0;
            using var endgame = new EndgameMovieNarrationTracker(_ => [new(0, 5, "film")], _ => null,
                _ => { endgameSpeech++; return true; }, _ => { });
            using var credits = new NativeCreditsNarrator(() => null, _ => { creditsSpeech++; return true; }, _ => { });
            using var title = new NativeObservedCaptionNarrator(_ => null, _ => { titleSpeech++; return true; }, _ => { });
            using var ready = new Barrier(2);
            var publisher = Task.Run(() => { ready.SignalAndWait(); session.PublishNarrationOwners(endgame, credits, title); });
            var resumer = Task.Run(() => { ready.SignalAndWait(); session.Resume(); });
            Task.WaitAll(publisher, resumer);
            // Do not issue another Resume: that would hide the stale Suspend race.
            endgame.Observe(new("ending2.avi", 25, 0, true, false), now, true, true);
            credits.Observe(new(0, 0, [new(0, 0, "DIRECTOR", 30)]), now, true, true);
            title.Observe(new("title", now), now, true, true);
            if (endgameSpeech != 1 || creditsSpeech != 1 || titleSpeech != 1)
                throw new InvalidOperationException($"Publication raced with Resume at iteration {iteration}: {endgameSpeech}/{creditsSpeech}/{titleSpeech}.");
        }
        session.Dispose();
        Console.WriteLine("Concurrent ending, credits and title publication/Resume tests passed.");
        void Set(string field, object value) => typeof(Steam2026ResearchSession)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, value);
    }
}
