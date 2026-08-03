using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Lifecycle;

internal static class Steam2026LifecycleObservationTests
{
    private static readonly byte[] ResolverSignature = Convert.FromHexString(
        "85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3");

    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        PublishesOnlyCoherentLifecycleSnapshots();
        RejectsTornOrUnreadableLifecycleState();
        PreventsOlderCaptureFromCommittingAfterNewerState();
        SerializesConcurrentRevisionCommits();
        BeginShutdownCannotReturnBeforeInFlightPublicationCompletes();
        PublicConstructionRequiresExactIdentityAndResolver(supported, unsupported);
    }

    private static void PublishesOnlyCoherentLifecycleSnapshots()
    {
        byte module = 1;
        var foreground = true;
        var reader = new Steam2026LifecycleObservationReader(
            () => (true, module),
            () => foreground);

        Equal(true, reader.TryRead(out var first), "coherent lifecycle observation");
        Equal(1, first.ModuleId, "coherent lifecycle module");
        Equal(true, first.IsForeground, "coherent lifecycle foreground state");
        Equal(false, first.IsShuttingDown, "initial lifecycle shutdown state");
        Equal(1, first.Revision, "initial lifecycle revision");

        Equal(true, reader.TryRead(out var unchanged), "stable lifecycle observation");
        Equal(1, unchanged.Revision, "stable lifecycle revision deduplicates");
        foreground = false;
        Equal(true, reader.TryRead(out var focusChanged), "focus transition lifecycle observation");
        Equal(2, focusChanged.Revision, "focus transition increments lifecycle revision");
        reader.BeginShutdown();
        Equal(true, reader.TryRead(out var shuttingDown), "explicit shutdown lifecycle observation");
        Equal(true, shuttingDown.IsShuttingDown, "explicit shutdown state");
        Equal(3, shuttingDown.Revision, "shutdown transition increments lifecycle revision");
    }

    private static void RejectsTornOrUnreadableLifecycleState()
    {
        var reads = 0;
        var tornModule = new Steam2026LifecycleObservationReader(
            () => (true, (byte)(++reads == 1 ? 1 : 2)),
            () => true);
        Equal(false, tornModule.TryRead(out _), "module transition tearing is rejected");

        var focusReads = 0;
        var tornFocus = new Steam2026LifecycleObservationReader(
            () => (true, (byte)1),
            () => ++focusReads == 1);
        Equal(false, tornFocus.TryRead(out _), "foreground transition tearing is rejected");

        var unreadable = new Steam2026LifecycleObservationReader(
            () => (false, (byte)0),
            () => true);
        Equal(false, unreadable.TryRead(out _), "unreadable module state is rejected");
    }

    private static void SerializesConcurrentRevisionCommits()
    {
        const int workerCount = 24;
        using var startBarrier = new Barrier(workerCount + 1);
        var activeCaptures = 0;
        var maximumConcurrentCaptures = 0;
        var reader = new Steam2026LifecycleObservationReader(
            () =>
            {
                var active = Interlocked.Increment(ref activeCaptures);
                var observedMaximum = Volatile.Read(ref maximumConcurrentCaptures);
                while (active > observedMaximum)
                {
                    var prior = Interlocked.CompareExchange(
                        ref maximumConcurrentCaptures,
                        active,
                        observedMaximum);
                    if (prior == observedMaximum)
                    {
                        break;
                    }

                    observedMaximum = prior;
                }

                Thread.Sleep(20);
                Interlocked.Decrement(ref activeCaptures);
                return (true, (byte)1);
            },
            () => true);

        var tasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    if (!startBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Concurrent lifecycle start barrier timed out.");
                    }

                    var success = reader.TryRead(out var observation);
                    return (Success: success, Observation: observation);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        if (!startBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
        {
            throw new InvalidOperationException("Concurrent lifecycle workers did not reach the start barrier.");
        }

        Task.WaitAll(tasks, TimeSpan.FromSeconds(10));
        Equal(true, tasks.All(task => task.IsCompletedSuccessfully), "concurrent lifecycle observations complete");
        Equal(true, tasks.All(task => task.Result.Success), "concurrent lifecycle observations succeed");
        Equal(1, maximumConcurrentCaptures, "lifecycle coherent capture and revision commit are serialized");
        SequenceEqual(
            Enumerable.Repeat(1, workerCount),
            tasks.Select(task => task.Result.Observation.Revision).Order(),
            "concurrent identical lifecycle snapshots share one serialized revision");
    }

    private static void PreventsOlderCaptureFromCommittingAfterNewerState()
    {
        var module = 1;
        using var oldCaptureBarrier = new Barrier(2);
        using var releaseOldCapture = new ManualResetEventSlim(false);
        using var newerTaskStarted = new ManualResetEventSlim(false);
        using var newerCaptureEntered = new ManualResetEventSlim(false);
        using var isOldCapture = new ThreadLocal<bool>();
        using var foregroundReads = new ThreadLocal<int>();
        var reader = new Steam2026LifecycleObservationReader(
            () =>
            {
                if (!isOldCapture.Value)
                {
                    newerCaptureEntered.Set();
                }

                return (true, checked((byte)Volatile.Read(ref module)));
            },
            () =>
            {
                foregroundReads.Value++;
                if (isOldCapture.Value && foregroundReads.Value == 2)
                {
                    if (!oldCaptureBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Old lifecycle capture barrier timed out.");
                    }

                    if (!releaseOldCapture.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Old lifecycle capture release timed out.");
                    }
                }

                return true;
            });

        var oldTask = Task.Factory.StartNew(
            () =>
            {
                isOldCapture.Value = true;
                var success = reader.TryRead(out var observation);
                return (Success: success, Observation: observation);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        Equal(
            true,
            oldCaptureBarrier.SignalAndWait(TimeSpan.FromSeconds(10)),
            "older lifecycle capture reaches its second coherent bookend barrier");
        Volatile.Write(ref module, 2);
        var newTask = Task.Factory.StartNew(
            () =>
            {
                isOldCapture.Value = false;
                newerTaskStarted.Set();
                var success = reader.TryRead(out var observation);
                return (Success: success, Observation: observation);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        Equal(
            true,
            newerTaskStarted.Wait(TimeSpan.FromSeconds(10)),
            "newer lifecycle task starts while the old capture is held");
        if (newerCaptureEntered.Wait(TimeSpan.FromSeconds(1)))
        {
            Equal(
                true,
                newTask.Wait(TimeSpan.FromSeconds(10)),
                "newer lifecycle capture completes before releasing the old commit");
        }

        releaseOldCapture.Set();
        Equal(true, oldTask.Wait(TimeSpan.FromSeconds(10)), "older lifecycle task completes");
        Equal(true, newTask.Wait(TimeSpan.FromSeconds(10)), "newer lifecycle task completes");
        Equal(true, oldTask.Result.Success, "older coherent lifecycle snapshot succeeds");
        Equal(true, newTask.Result.Success, "newer coherent lifecycle snapshot succeeds");
        Equal(1, oldTask.Result.Observation.ModuleId, "older lifecycle module remains coherent");
        Equal(1, oldTask.Result.Observation.Revision, "older lifecycle state commits first");
        Equal(2, newTask.Result.Observation.ModuleId, "newer lifecycle module remains coherent");
        Equal(2, newTask.Result.Observation.Revision, "newer lifecycle state commits second");
    }

    private static void BeginShutdownCannotReturnBeforeInFlightPublicationCompletes()
    {
        using var secondCaptureReached = new ManualResetEventSlim(false);
        using var releaseSecondCapture = new ManualResetEventSlim(false);
        using var shutdownStarted = new ManualResetEventSlim(false);
        using var shutdownCompleted = new ManualResetEventSlim(false);
        var foregroundReads = 0;
        var reader = new Steam2026LifecycleObservationReader(
            () => (true, (byte)1),
            () =>
            {
                if (Interlocked.Increment(ref foregroundReads) == 2)
                {
                    secondCaptureReached.Set();
                    if (!releaseSecondCapture.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Lifecycle second-capture release timed out.");
                    }
                }

                return true;
            });

        var readTask = Task.Factory.StartNew(
            () =>
            {
                var success = reader.TryRead(out var observation);
                return (Success: success, Observation: observation);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Equal(
            true,
            secondCaptureReached.Wait(TimeSpan.FromSeconds(10)),
            "lifecycle read reaches its final capture before publication");

        var shutdownTask = Task.Factory.StartNew(
            () =>
            {
                shutdownStarted.Set();
                reader.BeginShutdown();
                shutdownCompleted.Set();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Equal(true, shutdownStarted.Wait(TimeSpan.FromSeconds(10)), "shutdown task begins during in-flight publication");
        Equal(
            false,
            shutdownCompleted.Wait(TimeSpan.FromMilliseconds(250)),
            "BeginShutdown cannot return while a pre-shutdown observation can still publish");

        releaseSecondCapture.Set();
        Equal(true, readTask.Wait(TimeSpan.FromSeconds(10)), "in-flight lifecycle read completes");
        Equal(true, readTask.Result.Success, "in-flight pre-shutdown lifecycle snapshot remains coherent");
        Equal(false, readTask.Result.Observation.IsShuttingDown, "in-flight lifecycle snapshot precedes shutdown signal");
        Equal(true, shutdownTask.Wait(TimeSpan.FromSeconds(10)), "shutdown completes after publication transaction");
        Equal(true, shutdownCompleted.IsSet, "shutdown completion is observable after transaction");
        Equal(true, reader.TryRead(out var afterShutdown), "post-shutdown lifecycle snapshot is coherent");
        Equal(true, afterShutdown.IsShuttingDown, "no post-return lifecycle observation can publish stale shutdown state");
    }

    private static void PublicConstructionRequiresExactIdentityAndResolver(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        const ulong moduleBase = 0x0000000140000000;
        var memory = new FakeNativeMemoryReader();
        memory.Write(moduleBase + TranslatedX86AddressSpace.ResolverRva, ResolverSignature);
        _ = new Steam2026LifecycleObservationReader(supported, moduleBase, memory);

        memory.Write(moduleBase + TranslatedX86AddressSpace.ResolverRva, [0x90]);
        Throws<InvalidOperationException>(
            () => _ = new Steam2026LifecycleObservationReader(supported, moduleBase, memory),
            "lifecycle reader rejects a bad translated resolver signature");
        Throws<ArgumentException>(
            () => _ = new Steam2026LifecycleObservationReader(unsupported, moduleBase, memory),
            "lifecycle reader rejects an unsupported executable identity");
    }

    private static void Throws<TException>(Action action, string label)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}].");
        }
    }
}
