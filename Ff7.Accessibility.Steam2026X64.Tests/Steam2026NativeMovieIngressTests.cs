using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Movies;

internal static class Steam2026NativeMovieIngressTests
{
    private const string OpeningPath = @"C:\Games\FF7\data\movies\opening.avi";
    private static readonly DateTime Timestamp =
        new(2026, 7, 19, 23, 0, 0, DateTimeKind.Utc);

    public static void Run(Steam2026FingerprintResult supportedRuntime)
    {
        DelegatesMatchOnlyProvenMicrosoftX64Shapes();
        ConstructionRequiresAllFourExactIdentities(supportedRuntime);
        PreparePreservesOriginalReturnAndCopiesAfterOriginal(supportedRuntime);
        StartReadsNativeStateAroundOriginalAndPreservesReturn(supportedRuntime);
        FailedStartCannotEmitOrLeaveArmedLifecycle(supportedRuntime);
        ReleaseAndStopPreserveOriginalFirstTerminalOrdering(supportedRuntime);
        StaleIdentitiesStillCallOriginalButSuppressCapture(supportedRuntime);
        ConcurrentCallbacksDoNotSerializeOriginalInvocations(supportedRuntime);
        ObserverContentionNeverBlocksNativeCallback(supportedRuntime);
        StopDoesNotWaitForInFlightObservation(supportedRuntime);
        ObservationAndSinkFailuresNeverEscapeIngress(supportedRuntime);
        QueueOverflowPermanentlyDegradesObservation(supportedRuntime);
        OriginalFailuresAreContainedAndPermanentlyDegradeObservation(supportedRuntime);
        ConcurrentCallbacksCallEveryOriginalOnceAndDeduplicateLifecycle(supportedRuntime);
        StopAndDisposeAreIdempotentAndLeaveOriginalsCallable(supportedRuntime);
        IngressHasNoHookBackendCapabilityOrInstallSurface(supportedRuntime);
    }

    private static void DelegatesMatchOnlyProvenMicrosoftX64Shapes()
    {
        Equal(8, IntPtr.Size, "movie ingress tests execute in x64 process");
        AssertDelegateShape(
            typeof(NativeMoviePrepareOriginal),
            typeof(int),
            [typeof(int), typeof(int)],
            "prepare");
        AssertDelegateShape(
            typeof(NativeMovieReleaseOriginal),
            typeof(void),
            [],
            "release");
        AssertDelegateShape(
            typeof(NativeMovieStartOriginal),
            typeof(int),
            [],
            "start");
        AssertDelegateShape(
            typeof(NativeMovieStopOriginal),
            typeof(void),
            [],
            "stop");

        var ingressDelegateNames = typeof(NativeMovieDetourIngressCoordinator).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(NativeMovieDetourIngressCoordinator).Namespace)
            .Where(type => typeof(MulticastDelegate).IsAssignableFrom(type.BaseType))
            .Select(type => type.Name)
            .ToArray();
        Equal(false, ingressDelegateNames.Any(name => name.Contains("Frame", StringComparison.Ordinal)), "no frame-getter detour delegate");
        Equal(false, ingressDelegateNames.Any(name => name.Contains("Update", StringComparison.Ordinal)), "no unneeded update detour delegate");

        Equal(
            new NativeMovieCallbackShape(
                NativeMovieCallbackAbi.MicrosoftX64,
                NativeMovieCallbackParameterShape.Two32BitIntegers,
                NativeMovieCallbackReturnShape.BooleanCompatibleInteger),
            NativeMovieCallbackContract.GetMetadata(NativeMovieCallbackKind.Prepare).Shape,
            "prepare contract shape");
        Equal(false, NativeMovieCallbackContract.IsHookable(NativeMovieCallbackKind.FrameGetter), "seven-byte frame getter remains unhookable");
    }

    private static void ConstructionRequiresAllFourExactIdentities(
        Steam2026FingerprintResult supportedRuntime)
    {
        foreach (var kind in new[]
                 {
                     NativeMovieCallbackKind.Prepare,
                     NativeMovieCallbackKind.Release,
                     NativeMovieCallbackKind.Start,
                     NativeMovieCallbackKind.Stop
                 })
        {
            var fixture = MovieIngressFixture.Create(supportedRuntime);
            fixture.Corrupt(kind);
            var threw = false;
            try
            {
                using var _ = CreateCoordinator(fixture);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Equal(true, threw, $"construction rejects stale {kind} identity");
        }
    }

    private static void PreparePreservesOriginalReturnAndCopiesAfterOriginal(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var order = new List<string>();
        var snapshots = new List<NativeMovieIngressSnapshot>();
        var originalCalls = 0;
        var coordinator = CreateCoordinator(
            fixture,
            prepareOriginal: (argument0, argument1) =>
            {
                order.Add($"original:{argument0}:{argument1}");
                originalCalls++;
                return -7;
            },
            pathReader: () =>
            {
                order.Add("path");
                return OpeningPath;
            },
            clock: () =>
            {
                order.Add("clock");
                return Timestamp;
            },
            captureSink: snapshot =>
            {
                order.Add("sink");
                snapshots.Add(snapshot);
            });

        var result = coordinator.OnPrepare(unchecked((int)0x81234567u), -19);

        Equal(-7, result, "prepare preserves exact original integer return");
        Equal(1, originalCalls, "prepare original called exactly once");
        SequenceEqual(
            ["original:-2128394905:-19", "path", "clock", "sink"],
            order,
            "prepare copy and capture ordering");
        Equal(1, snapshots.Count, "prepare capture count");
        var snapshot = snapshots.Single();
        Equal(NativeMovieCallbackKind.Prepare, snapshot.CallbackKind, "prepare snapshot kind");
        Equal(
            new NativeMoviePrepareArguments(unchecked((int)0x81234567u), -19),
            snapshot.PrepareArguments,
            "prepare preserves raw 32-bit argument slots");
        Equal(-7, snapshot.OriginalReturnValue, "prepare snapshot original return");
        Equal(true, snapshot.OriginalSucceeded, "nonzero prepare result is boolean-compatible success");
        Equal(OpeningPath, snapshot.CanonicalMoviePath, "prepare bounded path copy");
        Equal(Timestamp, snapshot.TimestampUtc, "prepare capture timestamp");
        Equal<MovieLifecycleEvent?>(null, snapshot.LifecycleEvent, "prepare alone emits no lifecycle event");
        coordinator.Dispose();
    }

    private static void StartReadsNativeStateAroundOriginalAndPreservesReturn(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var order = new List<string>();
        var snapshots = new List<NativeMovieIngressSnapshot>();
        var state = 0;
        var startCalls = 0;
        var coordinator = CreateCoordinator(
            fixture,
            startOriginal: () =>
            {
                order.Add("original");
                startCalls++;
                state = 1;
                return unchecked((int)0x89ABCDEFu);
            },
            stateReader: () =>
            {
                order.Add($"state:{state}");
                return state;
            },
            clock: () =>
            {
                order.Add("clock");
                return Timestamp;
            },
            captureSink: snapshot =>
            {
                order.Add("sink");
                snapshots.Add(snapshot);
            });

        Equal(1, coordinator.OnPrepare(1, 2), "start setup prepare return");
        order.Clear();
        snapshots.Clear();
        var result = coordinator.OnStart();

        Equal(unchecked((int)0x89ABCDEFu), result, "start preserves exact original integer return");
        Equal(1, startCalls, "start original called exactly once");
        SequenceEqual(["state:0", "original", "state:1", "clock", "sink"], order, "start state/original ordering");
        var snapshot = snapshots.Single();
        Equal(NativeMovieCallbackKind.Start, snapshot.CallbackKind, "start snapshot kind");
        Equal(0, snapshot.StateBefore, "start native state before original");
        Equal(1, snapshot.StateAfter, "start native state after original");
        Equal(unchecked((int)0x89ABCDEFu), snapshot.OriginalReturnValue, "start snapshot return");
        Equal(MovieLifecycleKind.Started, snapshot.LifecycleEvent!.Kind, "start rising edge lifecycle");
        coordinator.Dispose();
    }

    private static void FailedStartCannotEmitOrLeaveArmedLifecycle(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var snapshots = new List<NativeMovieIngressSnapshot>();
        var state = 0;
        var startResult = 0;
        var coordinator = CreateCoordinator(
            fixture,
            startOriginal: () =>
            {
                state = 1;
                return startResult;
            },
            stateReader: () => state,
            captureSink: snapshots.Add);

        coordinator.OnPrepare(0, 0);
        snapshots.Clear();
        Equal(0, coordinator.OnStart(), "failed start preserves zero original return");
        var failed = snapshots.Single();
        Equal(false, failed.OriginalSucceeded, "failed start snapshot records native failure");
        Equal<MovieLifecycleEvent?>(null, failed.LifecycleEvent, "failed start emits no lifecycle start");

        state = 0;
        startResult = 1;
        snapshots.Clear();
        Equal(1, coordinator.OnStart(), "later successful native start return preserved");
        Equal<MovieLifecycleEvent?>(
            null,
            snapshots.Single().LifecycleEvent,
            "failed start disarms lifecycle until another successful prepare");
        coordinator.Dispose();
    }

    private static void ReleaseAndStopPreserveOriginalFirstTerminalOrdering(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var order = new List<string>();
        var snapshots = new List<NativeMovieIngressSnapshot>();
        var state = 0;
        var releaseCalls = 0;
        var stopCalls = 0;
        var coordinator = CreateCoordinator(
            fixture,
            releaseOriginal: () =>
            {
                order.Add("release-original");
                releaseCalls++;
            },
            startOriginal: () =>
            {
                state = 1;
                return 1;
            },
            stopOriginal: () =>
            {
                order.Add("stop-original");
                stopCalls++;
                state = 0;
            },
            stateReader: () => state,
            clock: () =>
            {
                order.Add("clock");
                return Timestamp.AddTicks(snapshots.Count);
            },
            captureSink: snapshot =>
            {
                order.Add($"sink:{snapshot.CallbackKind}");
                snapshots.Add(snapshot);
            });

        ArmAndStart(coordinator);
        order.Clear();
        snapshots.Clear();
        coordinator.OnStop();
        SequenceEqual(["stop-original", "clock", "sink:Stop"], order, "stop original precedes terminal capture");
        Equal(1, stopCalls, "stop original once");
        Equal(MovieLifecycleKind.Stopped, snapshots.Single().LifecycleEvent!.Kind, "stop lifecycle terminal");

        order.Clear();
        snapshots.Clear();
        coordinator.OnStop();
        Equal(2, stopCalls, "duplicate stop still calls original once per native invocation");
        Equal<MovieLifecycleEvent?>(null, snapshots.Single().LifecycleEvent, "duplicate stop lifecycle deduplicated");

        ArmAndStart(coordinator);
        order.Clear();
        snapshots.Clear();
        coordinator.OnRelease();
        SequenceEqual(["release-original", "clock", "sink:Release"], order, "release original precedes terminal capture");
        Equal(1, releaseCalls, "release original once");
        Equal(MovieLifecycleKind.Stopped, snapshots.Single().LifecycleEvent!.Kind, "release lifecycle terminal");
        coordinator.Dispose();
    }

    private static void StaleIdentitiesStillCallOriginalButSuppressCapture(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var originalCalls = 0;
        var pathReads = 0;
        var captures = 0;
        var coordinator = CreateCoordinator(
            fixture,
            prepareOriginal: (_, _) =>
            {
                originalCalls++;
                return 77;
            },
            pathReader: () =>
            {
                pathReads++;
                return OpeningPath;
            },
            captureSink: _ => captures++);
        fixture.Corrupt(NativeMovieCallbackKind.Prepare);

        Equal(77, coordinator.OnPrepare(3, 4), "stale entry identity preserves original return");
        Equal(1, originalCalls, "stale entry identity still calls original once");
        Equal(0, pathReads, "stale entry identity does not read semantic path");
        Equal(0, captures, "stale entry identity suppresses capture");
        coordinator.Dispose();

        var afterOriginalFixture = MovieIngressFixture.Create(supportedRuntime);
        originalCalls = 0;
        captures = 0;
        var afterOriginal = CreateCoordinator(
            afterOriginalFixture,
            prepareOriginal: (_, _) =>
            {
                originalCalls++;
                afterOriginalFixture.Corrupt(NativeMovieCallbackKind.Prepare);
                return 88;
            },
            captureSink: _ => captures++);
        Equal(88, afterOriginal.OnPrepare(5, 6), "identity stale after original preserves return");
        Equal(1, originalCalls, "identity stale after original calls original once");
        Equal(0, captures, "identity stale after original suppresses capture");
        afterOriginal.Dispose();

        var startFixture = MovieIngressFixture.Create(supportedRuntime);
        var startCalls = 0;
        var stateReads = 0;
        captures = 0;
        var staleStart = CreateCoordinator(
            startFixture,
            startOriginal: () =>
            {
                startCalls++;
                return 91;
            },
            stateReader: () =>
            {
                stateReads++;
                return 0;
            },
            captureSink: _ => captures++);
        startFixture.Corrupt(NativeMovieCallbackKind.Start);
        Equal(91, staleStart.OnStart(), "stale start identity preserves original return");
        Equal(1, startCalls, "stale start identity calls original once");
        Equal(0, stateReads, "stale start identity performs no semantic state read");
        Equal(0, captures, "stale start identity suppresses capture");
        staleStart.Dispose();

        foreach (var terminalKind in new[]
                 {
                     NativeMovieCallbackKind.Release,
                     NativeMovieCallbackKind.Stop
                 })
        {
            var terminalFixture = MovieIngressFixture.Create(supportedRuntime);
            var terminalCalls = 0;
            captures = 0;
            var staleTerminal = CreateCoordinator(
                terminalFixture,
                releaseOriginal: () => terminalCalls++,
                stopOriginal: () => terminalCalls++,
                captureSink: _ => captures++);
            terminalFixture.Corrupt(terminalKind);
            if (terminalKind == NativeMovieCallbackKind.Release)
            {
                staleTerminal.OnRelease();
            }
            else
            {
                staleTerminal.OnStop();
            }

            Equal(1, terminalCalls, $"stale {terminalKind} identity calls original once");
            Equal(0, captures, $"stale {terminalKind} identity suppresses capture");
            staleTerminal.Dispose();
        }
    }

    private static void ObservationAndSinkFailuresNeverEscapeIngress(
        Steam2026FingerprintResult supportedRuntime)
    {
        var pathFixture = MovieIngressFixture.Create(supportedRuntime);
        var pathSnapshots = new List<NativeMovieIngressSnapshot>();
        var pathOriginalCalls = 0;
        var pathCoordinator = CreateCoordinator(
            pathFixture,
            prepareOriginal: (_, _) =>
            {
                pathOriginalCalls++;
                return 13;
            },
            pathReader: () => throw new InvalidOperationException("path probe failed"),
            captureSink: pathSnapshots.Add);
        Equal(13, pathCoordinator.OnPrepare(0, 0), "throwing path probe preserves prepare return");
        Equal(1, pathOriginalCalls, "throwing path probe original once");
        Equal<string?>(null, pathSnapshots.Single().CanonicalMoviePath, "throwing path probe copies no guessed path");
        pathCoordinator.Dispose();

        var oversizedPathFixture = MovieIngressFixture.Create(supportedRuntime);
        var oversizedPathSnapshots = new List<NativeMovieIngressSnapshot>();
        var oversizedPathCoordinator = CreateCoordinator(
            oversizedPathFixture,
            pathReader: () => new string(
                'x',
                NativeMovieDetourIngressCoordinator.MaximumCanonicalMoviePathLength + 1),
            captureSink: oversizedPathSnapshots.Add);
        Equal(1, oversizedPathCoordinator.OnPrepare(0, 0), "oversized path preserves prepare return");
        Equal<string?>(null, oversizedPathSnapshots.Single().CanonicalMoviePath, "oversized path is rejected instead of truncated");
        oversizedPathCoordinator.Dispose();

        var clockFixture = MovieIngressFixture.Create(supportedRuntime);
        var clockOriginalCalls = 0;
        var clockCaptures = 0;
        var clockCoordinator = CreateCoordinator(
            clockFixture,
            prepareOriginal: (_, _) =>
            {
                clockOriginalCalls++;
                return 17;
            },
            clock: () => throw new InvalidOperationException("clock failed"),
            captureSink: _ => clockCaptures++);
        Equal(17, clockCoordinator.OnPrepare(0, 0), "throwing clock preserves prepare return");
        Equal(1, clockOriginalCalls, "throwing clock prepare original once");
        Equal(0, clockCaptures, "throwing clock suppresses incomplete capture");
        clockCoordinator.Dispose();

        var stateFixture = MovieIngressFixture.Create(supportedRuntime);
        var stateStartCalls = 0;
        var stateCaptures = 0;
        var stateCoordinator = CreateCoordinator(
            stateFixture,
            startOriginal: () =>
            {
                stateStartCalls++;
                return 23;
            },
            stateReader: () => throw new InvalidOperationException("state probe failed"),
            captureSink: _ => stateCaptures++);
        stateCoordinator.OnPrepare(0, 0);
        Equal(23, stateCoordinator.OnStart(), "throwing state probe preserves start return");
        Equal(1, stateStartCalls, "throwing state probe start original once");
        Equal(1, stateCaptures, "throwing state probe adds no start capture");
        stateCoordinator.Dispose();

        var sinkFixture = MovieIngressFixture.Create(supportedRuntime);
        var sinkPrepareCalls = 0;
        var sinkStartCalls = 0;
        var sinkAttempts = 0;
        var state = 0;
        var sinkCoordinator = CreateCoordinator(
            sinkFixture,
            prepareOriginal: (_, _) =>
            {
                sinkPrepareCalls++;
                return 31;
            },
            startOriginal: () =>
            {
                sinkStartCalls++;
                state = 1;
                return 32;
            },
            stateReader: () => state,
            captureSink: _ =>
            {
                sinkAttempts++;
                throw new InvalidOperationException("sink failed");
            });
        Equal(31, sinkCoordinator.OnPrepare(0, 0), "throwing sink preserves prepare return");
        Equal(true, sinkCoordinator.IsFatallyDegraded, "throwing sink permanently degrades observation");
        Equal(32, sinkCoordinator.OnStart(), "throwing sink preserves start return");
        sinkCoordinator.OnStop();
        Equal(1, sinkPrepareCalls, "throwing sink prepare original once");
        Equal(1, sinkStartCalls, "throwing sink start original once");
        Equal(1, sinkAttempts, "degraded movie ingress never retries publication");
        sinkCoordinator.Dispose();

        var intermittentSinkFixture = MovieIngressFixture.Create(supportedRuntime);
        var delivered = new List<NativeMovieIngressSnapshot>();
        var intermittentState = 0;
        var failNextSink = false;
        var intermittentSinkCoordinator = CreateCoordinator(
            intermittentSinkFixture,
            startOriginal: () =>
            {
                intermittentState = 1;
                return 1;
            },
            stateReader: () => intermittentState,
            captureSink: snapshot =>
            {
                if (failNextSink)
                {
                    failNextSink = false;
                    throw new InvalidOperationException("one sink delivery failed");
                }

                delivered.Add(snapshot);
            });
        intermittentSinkCoordinator.OnPrepare(0, 0);
        delivered.Clear();
        failNextSink = true;
        Equal(1, intermittentSinkCoordinator.OnStart(), "failed start delivery preserves original return");
        Equal(true, intermittentSinkCoordinator.IsFatallyDegraded, "failed start delivery permanently degrades observation");
        intermittentSinkCoordinator.OnStop();
        Equal(0, delivered.Count, "terminal after failed start delivery is not published");
        intermittentSinkCoordinator.Dispose();

        var throwingMemoryFixture = MovieIngressFixture.CreateWithSwitchableThrowingMemory(
            supportedRuntime,
            out var throwingMemory);
        var throwingMemoryOriginalCalls = 0;
        var throwingMemoryCaptures = 0;
        var throwingMemoryCoordinator = CreateCoordinator(
            throwingMemoryFixture,
            prepareOriginal: (_, _) =>
            {
                throwingMemoryOriginalCalls++;
                return 41;
            },
            captureSink: _ => throwingMemoryCaptures++);
        throwingMemory.ThrowReads = true;
        Equal(41, throwingMemoryCoordinator.OnPrepare(0, 0), "throwing identity probe preserves return");
        Equal(1, throwingMemoryOriginalCalls, "throwing identity probe original once");
        Equal(0, throwingMemoryCaptures, "throwing identity probe suppresses capture");
        throwingMemoryCoordinator.Dispose();
    }

    private static void OriginalFailuresAreContainedAndPermanentlyDegradeObservation(
        Steam2026FingerprintResult supportedRuntime)
    {
        var prepareFixture = MovieIngressFixture.Create(supportedRuntime);
        var prepareThrows = false;
        var prepareCalls = 0;
        var prepareState = 0;
        var prepareSnapshots = new List<NativeMovieIngressSnapshot>();
        var prepareCoordinator = CreateCoordinator(
            prepareFixture,
            prepareOriginal: (_, _) =>
            {
                prepareCalls++;
                if (prepareThrows)
                {
                    throw new InvalidOperationException("prepare original failed");
                }

                return 1;
            },
            startOriginal: () =>
            {
                prepareState = 1;
                return 1;
            },
            stateReader: () => prepareState,
            captureSink: prepareSnapshots.Add);
        prepareCoordinator.OnPrepare(0, 0);
        prepareThrows = true;
        prepareSnapshots.Clear();
        Equal(0, prepareCoordinator.OnPrepare(0, 0), "prepare original exception returns a safe native failure value");
        Equal(2, prepareCalls, "throwing prepare original called exactly once for failing invocation");
        Equal(true, prepareCoordinator.IsFatallyDegraded, "throwing prepare permanently degrades observation");
        Equal(0, prepareSnapshots.Count, "throwing prepare publishes no observation");
        prepareThrows = false;
        prepareCoordinator.OnStart();
        Equal(0, prepareSnapshots.Count, "degraded prepare coordinator publishes no later lifecycle state");
        prepareCoordinator.Dispose();

        var startFixture = MovieIngressFixture.Create(supportedRuntime);
        var startThrows = true;
        var startCalls = 0;
        var startState = 0;
        var startSnapshots = new List<NativeMovieIngressSnapshot>();
        var startCoordinator = CreateCoordinator(
            startFixture,
            startOriginal: () =>
            {
                startCalls++;
                startState = 1;
                if (startThrows)
                {
                    throw new InvalidOperationException("start original failed");
                }

                return 1;
            },
            stateReader: () => startState,
            captureSink: startSnapshots.Add);
        startCoordinator.OnPrepare(0, 0);
        startSnapshots.Clear();
        Equal(0, startCoordinator.OnStart(), "start original exception returns a safe native failure value");
        Equal(1, startCalls, "throwing start original called exactly once");
        Equal(true, startCoordinator.IsFatallyDegraded, "throwing start permanently degrades observation");
        Equal(0, startSnapshots.Count, "throwing start publishes no observation");
        startThrows = false;
        startState = 0;
        startCoordinator.OnStart();
        Equal(0, startSnapshots.Count, "degraded start coordinator publishes no later lifecycle state");
        startCoordinator.Dispose();

        foreach (var terminalKind in new[]
                 {
                     NativeMovieCallbackKind.Release,
                     NativeMovieCallbackKind.Stop
                 })
        {
            var terminalFixture = MovieIngressFixture.Create(supportedRuntime);
            var terminalThrows = true;
            var terminalCalls = 0;
            var terminalState = 0;
            var terminalSnapshots = new List<NativeMovieIngressSnapshot>();
            void TerminalOriginal()
            {
                terminalCalls++;
                terminalState = 0;
                if (terminalThrows)
                {
                    throw new InvalidOperationException($"{terminalKind} original failed");
                }
            }

            var terminalCoordinator = CreateCoordinator(
                terminalFixture,
                releaseOriginal: TerminalOriginal,
                startOriginal: () =>
                {
                    terminalState = 1;
                    return 1;
                },
                stopOriginal: TerminalOriginal,
                stateReader: () => terminalState,
                captureSink: terminalSnapshots.Add);
            ArmAndStart(terminalCoordinator);
            terminalSnapshots.Clear();
            if (terminalKind == NativeMovieCallbackKind.Release)
            {
                terminalCoordinator.OnRelease();
            }
            else
            {
                terminalCoordinator.OnStop();
            }

            Equal(1, terminalCalls, $"throwing {terminalKind} original called exactly once");
            Equal(true, terminalCoordinator.IsFatallyDegraded, $"throwing {terminalKind} permanently degrades observation");
            Equal(0, terminalSnapshots.Count, $"throwing {terminalKind} publishes no observation");
            terminalThrows = false;
            if (terminalKind == NativeMovieCallbackKind.Release)
            {
                terminalCoordinator.OnRelease();
            }
            else
            {
                terminalCoordinator.OnStop();
            }

            Equal(0, terminalSnapshots.Count, $"degraded {terminalKind} coordinator publishes no later lifecycle state");
            terminalCoordinator.Dispose();
        }
    }

    private static void QueueOverflowPermanentlyDegradesObservation(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var originals = 0;
        var queue = new BoundedNativeIngressQueue<NativeMovieIngressSnapshot>(1);
        using var coordinator = CreateCoordinator(
            fixture,
            prepareOriginal: (_, _) =>
            {
                originals++;
                return 1;
            },
            captureQueue: queue);

        Equal(1, coordinator.OnPrepare(0, 0), "first queue-backed movie prepare return");
        Equal(false, coordinator.IsFatallyDegraded, "first queued movie capture remains healthy");

        Equal(1, coordinator.OnPrepare(0, 0), "overflowing movie prepare preserves return");
        Equal(true, coordinator.IsFatallyDegraded, "movie queue overflow permanently degrades observation");

        Equal(1, coordinator.OnPrepare(0, 0), "degraded movie prepare preserves return");
        Equal(3, originals, "movie originals remain callable after queue overflow");
        Equal(true, queue.TryDequeue(out _), "first movie capture remains queued after overflow");
        Equal(false, queue.TryDequeue(out _), "overflowed and degraded movie captures are not queued");
    }

    private static void ConcurrentCallbacksCallEveryOriginalOnceAndDeduplicateLifecycle(
        Steam2026FingerprintResult supportedRuntime)
    {
        const int count = 32;
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var snapshots = new ConcurrentBag<NativeMovieIngressSnapshot>();
        var state = 0;
        var startCalls = 0;
        var stopCalls = 0;
        var coordinator = CreateCoordinator(
            fixture,
            startOriginal: () =>
            {
                Interlocked.Increment(ref startCalls);
                state = 1;
                return 53;
            },
            stopOriginal: () =>
            {
                Interlocked.Increment(ref stopCalls);
                state = 0;
            },
            stateReader: () => state,
            clock: () => Timestamp,
            captureSink: snapshot => snapshots.Add(snapshot));
        coordinator.OnPrepare(0, 0);
        snapshots = new ConcurrentBag<NativeMovieIngressSnapshot>();

        var startResults = new int[count];
        Parallel.For(0, count, index => startResults[index] = coordinator.OnStart());
        Equal(count, startCalls, "concurrent start originals exactly once each");
        Equal(true, startResults.All(result => result == 53), "concurrent start returns preserved");
        Equal(
            true,
            snapshots.Count(snapshot => snapshot.CallbackKind == NativeMovieCallbackKind.Start) <= count,
            "concurrent start observation count is bounded by original count");
        Equal(
            true,
            snapshots.Count(snapshot => snapshot.LifecycleEvent?.Kind == MovieLifecycleKind.Started) <= 1,
            "concurrent starts emit at most one lifecycle start");

        Parallel.For(0, count, _ => coordinator.OnStop());
        Equal(count, stopCalls, "concurrent stop originals exactly once each");
        Equal(
            true,
            snapshots.Count(snapshot => snapshot.CallbackKind == NativeMovieCallbackKind.Stop) <= count,
            "concurrent stop observation count is bounded by original count");
        Equal(
            true,
            snapshots.Count(snapshot => snapshot.LifecycleEvent?.Kind == MovieLifecycleKind.Stopped) <= 1,
            "concurrent stops emit at most one lifecycle terminal");
        Equal(
            snapshots.Count,
            snapshots.Select(snapshot => snapshot.Sequence).Distinct().Count(),
            "concurrent ingress capture sequences remain unique");
        coordinator.Dispose();
    }

    private static void ConcurrentCallbacksDoNotSerializeOriginalInvocations(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var calls = 0;
        using var firstEntered = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var snapshots = new ConcurrentBag<NativeMovieIngressSnapshot>();
        using var coordinator = CreateCoordinator(
            fixture,
            prepareOriginal: (argument0, _) =>
            {
                Interlocked.Increment(ref calls);
                if (argument0 == 1)
                {
                    firstEntered.Set();
                    if (!releaseFirst.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("First movie original was not released.");
                    }
                }
                else
                {
                    secondEntered.Set();
                }

                return argument0 + 70;
            },
            captureSink: snapshots.Add);

        var first = Task.Factory.StartNew(
            () => coordinator.OnPrepare(1, 0),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Equal(true, firstEntered.Wait(TimeSpan.FromSeconds(5)), "first movie original entered");
        var second = Task.Factory.StartNew(
            () => coordinator.OnPrepare(2, 0),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var originalsOverlapped = secondEntered.Wait(TimeSpan.FromSeconds(1));
        var rejectedOverlapReturned = second.Wait(TimeSpan.FromSeconds(1));
        releaseFirst.Set();
        Equal(true, Task.WaitAll([first, second], TimeSpan.FromSeconds(5)), "movie callbacks complete");

        Equal(true, originalsOverlapped, "movie native originals are never serialized by ingress");
        Equal(true, rejectedOverlapReturned, "overlapping movie callback returns without waiting for observation ownership");
        Equal(2, calls, "concurrent movie callbacks call every original exactly once");
        Equal(71, first.Result, "first concurrent movie return is preserved");
        Equal(72, second.Result, "second concurrent movie return is preserved");
        Equal(0, snapshots.Count, "overlap invalidates both movie observations");
    }

    private static void StopDoesNotWaitForInFlightObservation(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        using var pathReadEntered = new ManualResetEventSlim();
        using var releasePathRead = new ManualResetEventSlim();
        var captures = 0;
        var coordinator = CreateCoordinator(
            fixture,
            prepareOriginal: (_, _) => 81,
            pathReader: () =>
            {
                pathReadEntered.Set();
                if (!releasePathRead.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Movie ingress path read was not released.");
                }

                return OpeningPath;
            },
            captureSink: _ => captures++);

        var callback = Task.Factory.StartNew(
            () => coordinator.OnPrepare(0, 0),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Equal(true, pathReadEntered.Wait(TimeSpan.FromSeconds(5)), "movie observation reached post-original path read");
        var stop = Task.Factory.StartNew(
            coordinator.Stop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var stopReturnedWithoutWaiting = stop.Wait(TimeSpan.FromSeconds(1));
        releasePathRead.Set();
        Equal(true, Task.WaitAll([callback, stop], TimeSpan.FromSeconds(5)), "movie callback and stop complete");

        Equal(true, stopReturnedWithoutWaiting, "movie Stop never waits for in-flight observation");
        Equal(81, callback.Result, "movie Stop preserves in-flight original return");
        Equal(0, captures, "Stop invalidates the in-flight movie observation");
        coordinator.Dispose();
    }

    private static void ObserverContentionNeverBlocksNativeCallback(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var observer = new OpeningMovieLifecycleObserver(OpeningPath, fixture.Contract);
        var observerLock = typeof(OpeningMovieLifecycleObserver)
                               .GetField("stateLock", BindingFlags.Instance | BindingFlags.NonPublic)
                               ?.GetValue(observer)
                           ?? throw new InvalidOperationException("Opening movie observer lock was unavailable.");
        using var lockHeld = new ManualResetEventSlim();
        using var releaseLock = new ManualResetEventSlim();
        var locker = Task.Factory.StartNew(
            () =>
            {
                Monitor.Enter(observerLock);
                try
                {
                    lockHeld.Set();
                    if (!releaseLock.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("Observer lock was not released.");
                    }
                }
                finally
                {
                    Monitor.Exit(observerLock);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Equal(true, lockHeld.Wait(TimeSpan.FromSeconds(5)), "test holds opening movie observer lock");

        var originals = 0;
        var captures = 0;
        var coordinator = CreateCoordinator(
            fixture,
            observer: observer,
            prepareOriginal: (_, _) =>
            {
                originals++;
                return 82;
            },
            captureSink: _ => captures++);
        var callback = Task.Factory.StartNew(
            () => coordinator.OnPrepare(0, 0),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var callbackReturnedWithoutWaiting = callback.Wait(TimeSpan.FromSeconds(1));
        releaseLock.Set();
        Equal(true, Task.WaitAll([callback, locker], TimeSpan.FromSeconds(5)), "observer contention test tasks complete");

        Equal(true, callbackReturnedWithoutWaiting, "movie callback never waits for observer ownership");
        Equal(82, callback.Result, "observer contention preserves native original return");
        Equal(1, originals, "observer contention calls native original exactly once");
        Equal(0, captures, "observer contention suppresses lifecycle capture");
        Equal(true, coordinator.IsFatallyDegraded, "observer contention permanently degrades movie observation");

        Equal(82, coordinator.OnPrepare(0, 0), "degraded observer path preserves later original return");
        Equal(2, originals, "native original remains callable after observer degradation");
        Equal(0, captures, "observer degradation suppresses every later observation");
        coordinator.Dispose();
    }

    private static void StopAndDisposeAreIdempotentAndLeaveOriginalsCallable(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var prepareCalls = 0;
        var startCalls = 0;
        var releaseCalls = 0;
        var stopCalls = 0;
        var pathReads = 0;
        var captures = 0;
        var coordinator = CreateCoordinator(
            fixture,
            prepareOriginal: (_, _) =>
            {
                prepareCalls++;
                return 61;
            },
            releaseOriginal: () => releaseCalls++,
            startOriginal: () =>
            {
                startCalls++;
                return 62;
            },
            stopOriginal: () => stopCalls++,
            pathReader: () =>
            {
                pathReads++;
                return OpeningPath;
            },
            captureSink: _ => captures++);

        coordinator.Stop();
        coordinator.Stop();
        coordinator.Dispose();
        coordinator.Dispose();

        Equal(61, coordinator.OnPrepare(0, 0), "stopped ingress preserves prepare original return");
        Equal(62, coordinator.OnStart(), "stopped ingress preserves start original return");
        coordinator.OnRelease();
        coordinator.OnStop();
        Equal(1, prepareCalls, "stopped ingress prepare original once");
        Equal(1, startCalls, "stopped ingress start original once");
        Equal(1, releaseCalls, "stopped ingress release original once");
        Equal(1, stopCalls, "stopped ingress stop original once");
        Equal(0, pathReads, "stopped ingress performs no semantic path read");
        Equal(0, captures, "stopped ingress performs no capture");
    }

    private static void IngressHasNoHookBackendCapabilityOrInstallSurface(
        Steam2026FingerprintResult supportedRuntime)
    {
        var type = typeof(NativeMovieDetourIngressCoordinator);
        Equal(false, type.IsPublic, "movie ingress coordinator remains internal");
        Equal(false, typeof(IFf7RuntimeBackend).IsAssignableFrom(type), "movie ingress is not a backend");
        Equal(false, typeof(IRuntimeEventSink).IsAssignableFrom(type), "movie ingress is not a runtime event sink");

        var prototypeRoot = FindPrototypeRoot();
        var projectRoot = Path.Combine(
            prototypeRoot,
            "reloaded",
            "Ff7.Accessibility.Steam2026X64");
        var sourcePath = Path.Combine(
            projectRoot,
            "Runtime",
            "Movies",
            "NativeMovieDetourIngressCoordinator.cs");
        var source = File.ReadAllText(sourcePath);
        foreach (var forbidden in new[]
                 {
                     "Reloaded.Hooks",
                     "IHook<",
                     "CreateHook",
                     "FrameGetter",
                     "NativeMovieCallbackKind.Update",
                     "IRuntimeEventSink",
                     "RuntimeCapability",
                     "captureSink",
                     "Action<",
                     ".Observe(",
                     "lock (",
                     "Monitor."
                 })
        {
            Equal(false, source.Contains(forbidden, StringComparison.Ordinal), $"movie ingress source excludes {forbidden}");
        }

        var backendSource = File.ReadAllText(Path.Combine(projectRoot, "Steam2026X64RuntimeBackend.cs"));
        Equal(false, backendSource.Contains(nameof(NativeMovieDetourIngressCoordinator), StringComparison.Ordinal), "backend has no movie ingress integration");
        using var backend = new Steam2026X64RuntimeBackend(supportedRuntime);
        Equal(RuntimeCapability.None, backend.ValidateCapabilities().Available, "movie ingress enables no capability");
    }

    private static NativeMovieDetourIngressCoordinator CreateCoordinator(
        MovieIngressFixture fixture,
        OpeningMovieLifecycleObserver? observer = null,
        NativeMoviePrepareOriginal? prepareOriginal = null,
        NativeMovieReleaseOriginal? releaseOriginal = null,
        NativeMovieStartOriginal? startOriginal = null,
        NativeMovieStopOriginal? stopOriginal = null,
        Func<string?>? pathReader = null,
        Func<int>? stateReader = null,
        Func<DateTime>? clock = null,
        Action<NativeMovieIngressSnapshot>? captureSink = null,
        INativeIngressQueue<NativeMovieIngressSnapshot>? captureQueue = null) =>
        new(
            fixture.Contract,
            observer ?? new OpeningMovieLifecycleObserver(OpeningPath, fixture.Contract),
            prepareOriginal ?? ((_, _) => 1),
            releaseOriginal ?? (() => { }),
            startOriginal ?? (() => 1),
            stopOriginal ?? (() => { }),
            pathReader ?? (() => OpeningPath),
            stateReader ?? (() => 0),
            clock ?? (() => Timestamp),
            captureQueue ?? new DelegatingNativeIngressQueue<NativeMovieIngressSnapshot>(
                captureSink ?? (_ => { })));

    private static void ArmAndStart(NativeMovieDetourIngressCoordinator coordinator)
    {
        Equal(1, coordinator.OnPrepare(0, 0), "terminal setup prepare return");
        Equal(1, coordinator.OnStart(), "terminal setup start return");
    }

    private static void AssertDelegateShape(
        Type delegateType,
        Type returnType,
        Type[] parameterTypes,
        string label)
    {
        Equal(false, delegateType.IsPublic, $"{label} delegate remains internal");
        var unmanaged = delegateType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>()
                        ?? throw new InvalidOperationException($"{label} delegate lacks unmanaged ABI metadata.");
        Equal(CallingConvention.Winapi, unmanaged.CallingConvention, $"{label} delegate Windows ABI");
        var invoke = delegateType.GetMethod("Invoke")
                     ?? throw new InvalidOperationException($"{label} delegate lacks Invoke.");
        Equal(returnType, invoke.ReturnType, $"{label} delegate return type");
        SequenceEqual(parameterTypes, invoke.GetParameters().Select(parameter => parameter.ParameterType), $"{label} delegate parameters");
    }

    private static string FindPrototypeRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "analysis", "dual_runtime")) &&
                Directory.Exists(Path.Combine(current.FullName, "reloaded")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate accessibility_prototype root.");
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
            throw new InvalidOperationException($"{label}: sequence mismatch.");
        }
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }
}

internal sealed class MovieIngressFixture
{
    public const ulong ModuleBase = 0x00000001A8000000;
    public const ulong ModuleImageSize = 0x02100000;

    private static readonly (NativeMovieCallbackKind Kind, string Signature)[] Signatures =
    [
        (NativeMovieCallbackKind.Prepare, "48895C2418555657415641574883EC60"),
        (NativeMovieCallbackKind.Release, "48895C2408574883EC20488B3DF766AC"),
        (NativeMovieCallbackKind.Start, "488B0541A0B00083B8FC010000007406"),
        (NativeMovieCallbackKind.Stop, "488B0511A0B00033C98988F801000048")
    ];

    private MovieIngressFixture(
        FakeNativeMemoryReader memory,
        NativeMovieCallbackContract contract)
    {
        Memory = memory;
        Contract = contract;
    }

    public FakeNativeMemoryReader Memory { get; }

    public NativeMovieCallbackContract Contract { get; }

    public static MovieIngressFixture Create(Steam2026FingerprintResult supportedRuntime)
    {
        var memory = CreateMemory();
        return new MovieIngressFixture(
            memory,
            new NativeMovieCallbackContract(
                supportedRuntime,
                ModuleBase,
                ModuleImageSize,
                memory));
    }

    public static MovieIngressFixture CreateWithSwitchableThrowingMemory(
        Steam2026FingerprintResult supportedRuntime,
        out SwitchableThrowingNativeMemoryReader throwingMemory)
    {
        var memory = CreateMemory();
        throwingMemory = new SwitchableThrowingNativeMemoryReader(memory);
        return new MovieIngressFixture(
            memory,
            new NativeMovieCallbackContract(
                supportedRuntime,
                ModuleBase,
                ModuleImageSize,
                throwingMemory));
    }

    public void Corrupt(NativeMovieCallbackKind kind) =>
        Memory.Write(ModuleBase + NativeMovieCallbackContract.GetRva(kind), [0x90]);

    private static FakeNativeMemoryReader CreateMemory()
    {
        var memory = new FakeNativeMemoryReader();
        memory.MapRegion(
            ModuleBase,
            ModuleImageSize,
            ModuleBase,
            isCommitted: true,
            isExecutable: true);
        foreach (var signature in Signatures)
        {
            memory.Write(
                ModuleBase + NativeMovieCallbackContract.GetRva(signature.Kind),
                Convert.FromHexString(signature.Signature));
        }

        return memory;
    }
}

internal sealed class SwitchableThrowingNativeMemoryReader(INativeMemoryReader inner) : INativeMemoryReader
{
    public bool ThrowReads { get; set; }

    public bool TryReadUInt64(ulong address, out ulong value)
    {
        ThrowIfRequested();
        return inner.TryReadUInt64(address, out value);
    }

    public bool TryRead(ulong address, Span<byte> destination)
    {
        ThrowIfRequested();
        return inner.TryRead(address, destination);
    }

    public bool TryQueryRegion(ulong address, out NativeMemoryRegion region)
    {
        ThrowIfRequested();
        return inner.TryQueryRegion(address, out region);
    }

    private void ThrowIfRequested()
    {
        if (ThrowReads)
        {
            throw new InvalidOperationException("native memory probe failed");
        }
    }
}
