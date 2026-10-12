using Ff7.Accessibility.Reloaded;

internal static class LegacyNativeMovieObservationHookTests
{
    internal static void Run()
    {
        PreservesNativeArgumentResultAndOrder();
        ReplacesTheBoundedSlotAndPublishesRejection();
        ObservationFailureNeverChangesNativeResult();
        DisposedWrapperPreservesOriginalButStopsObservation();
    }
    private static void PreservesNativeArgumentResultAndOrder()
    {
        var events = new List<string>(); var argument = (nint)0x123456;
        using var observer = new LegacyNativeMovieObservationHook(surface =>
        {
            if (surface != argument) throw new InvalidOperationException("Native surface argument changed.");
            events.Add("original"); return 0xDEADBEEF;
        }, () => { events.Add("observe"); return Sample(150); });
        Equal(0xDEADBEEFu, observer.Invoke(argument), "the native return bits are unchanged");
        Equal("original|observe", string.Join("|", events), "original runs exactly once before observation");
        Equal(true, observer.TryDrain(out var captured), "worker receives checked observation");
        Equal(150u, captured.Sample?.Frame, "immutable checked native frame");
    }
    private static void ReplacesTheBoundedSlotAndPublishesRejection()
    {
        NativeMoviePlaybackSample? next = Sample(1);
        using var observer = new LegacyNativeMovieObservationHook(_ => 1, () => next);
        for (uint frame = 1; frame <= 1000; frame++) { next = Sample(frame); observer.Invoke(0); }
        Equal(true, observer.TryDrain(out var latest), "latest slot is available");
        Equal(1000u, latest.Sample?.Frame, "slow worker gets newest frame rather than stale backlog");
        Equal(false, observer.TryDrain(out _), "drain consumes the single bounded slot");
        next = null; observer.Invoke(0);
        Equal(true, observer.TryDrain(out var rejected), "a failed native read is still an observation");
        Equal(null, rejected.Sample, "rejection clears stale playback instead of retaining old film");
        Equal(latest.Revision + 1, rejected.Revision, "rejection has its own observation revision");
    }
    private static void ObservationFailureNeverChangesNativeResult()
    {
        var calls = 0;
        using var observer = new LegacyNativeMovieObservationHook(_ => { calls++; return 0x80000000; },
            () => throw new InvalidOperationException("Reader unavailable"));
        Equal(0x80000000u, observer.Invoke(0), "observer exception cannot alter native return");
        Equal(1, calls, "observer exception cannot cause an original retry");
        Equal(true, observer.TryDrain(out var failed), "worker sees rejection after observation exception");
        Equal(null, failed.Sample, "observer exception cannot publish previous native sample");
    }
    private static void DisposedWrapperPreservesOriginalButStopsObservation()
    {
        var originals = 0; var observations = 0;
        var observer = new LegacyNativeMovieObservationHook(_ => { originals++; return 7; },
            () => { observations++; return Sample(1); });
        observer.Invoke(0); observer.Dispose(); observer.Dispose();
        Equal(7u, observer.Invoke(0), "an in-flight disposed detour still forwards the original");
        Equal(2, originals, "dispose never duplicates or suppresses native updates");
        Equal(1, observations, "disposed wrapper never reads stock COM again");
        Equal(false, observer.TryDrain(out _), "disposed wrapper does not leak queued playback");
    }
    private static NativeMoviePlaybackSample Sample(uint frame) => new("ending2.avi", 25, frame, true, false, 1);
    private static void Equal<T>(T expected, T actual, string label)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{label}: expected {expected}, got {actual}"); }
}
