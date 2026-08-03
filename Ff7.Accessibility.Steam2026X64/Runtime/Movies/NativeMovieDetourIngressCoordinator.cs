using System.Runtime.InteropServices;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Movies;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate int NativeMoviePrepareOriginal(int argument0, int argument1);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void NativeMovieReleaseOriginal();

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate int NativeMovieStartOriginal();

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void NativeMovieStopOriginal();

internal readonly record struct NativeMoviePrepareArguments(
    int Argument0,
    int Argument1);

/// <summary>
/// Immutable, pointer-free copy of one validated native callback invocation.
/// This research record has no publication or activation surface.
/// </summary>
internal sealed record NativeMovieIngressSnapshot(
    NativeMovieCallbackKind CallbackKind,
    long Sequence,
    DateTime TimestampUtc,
    NativeMoviePrepareArguments? PrepareArguments,
    int? OriginalReturnValue,
    bool OriginalSucceeded,
    string? CanonicalMoviePath,
    int? StateBefore,
    int? StateAfter,
    MovieLifecycleEvent? LifecycleEvent);

/// <summary>
/// Research-only ingress for four exact native movie callback shapes. The
/// caller supplies every original function and observation dependency; this
/// type requires bounded, nonwaiting callback-safe observation probes, creates
/// no detours, and publishes no runtime capability.
/// </summary>
internal sealed class NativeMovieDetourIngressCoordinator : IDisposable
{
    internal const int MaximumCanonicalMoviePathLength = 32_767;

    private readonly NativeIngressObservationGate observationGate = new();
    private readonly NativeMovieCallbackContract contract;
    private readonly OpeningMovieLifecycleObserver observer;
    private readonly NativeMoviePrepareOriginal prepareOriginal;
    private readonly NativeMovieReleaseOriginal releaseOriginal;
    private readonly NativeMovieStartOriginal startOriginal;
    private readonly NativeMovieStopOriginal stopOriginal;
    private readonly Func<string?> pathReader;
    private readonly Func<int> stateReader;
    private readonly Func<DateTime> clock;
    private readonly INativeIngressQueue<NativeMovieIngressSnapshot> captureQueue;
    private readonly NativeMovieCallbackIdentity prepareIdentity;
    private readonly NativeMovieCallbackIdentity releaseIdentity;
    private readonly NativeMovieCallbackIdentity startIdentity;
    private readonly NativeMovieCallbackIdentity stopIdentity;
    private long observationEpoch;
    private int fatalIngressFailure;
    private int stopped;

    internal NativeMovieDetourIngressCoordinator(
        NativeMovieCallbackContract contract,
        OpeningMovieLifecycleObserver observer,
        NativeMoviePrepareOriginal prepareOriginal,
        NativeMovieReleaseOriginal releaseOriginal,
        NativeMovieStartOriginal startOriginal,
        NativeMovieStopOriginal stopOriginal,
        Func<string?> pathReader,
        Func<int> stateReader,
        Func<DateTime> clock,
        INativeIngressQueue<NativeMovieIngressSnapshot> captureQueue)
    {
        this.contract = contract ?? throw new ArgumentNullException(nameof(contract));
        this.observer = observer ?? throw new ArgumentNullException(nameof(observer));
        this.prepareOriginal = prepareOriginal ?? throw new ArgumentNullException(nameof(prepareOriginal));
        this.releaseOriginal = releaseOriginal ?? throw new ArgumentNullException(nameof(releaseOriginal));
        this.startOriginal = startOriginal ?? throw new ArgumentNullException(nameof(startOriginal));
        this.stopOriginal = stopOriginal ?? throw new ArgumentNullException(nameof(stopOriginal));
        this.pathReader = pathReader ?? throw new ArgumentNullException(nameof(pathReader));
        this.stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.captureQueue = captureQueue ?? throw new ArgumentNullException(nameof(captureQueue));

        prepareIdentity = ValidateInitialIdentity(
            NativeMovieCallbackKind.Prepare,
            NativeMovieCallbackParameterShape.Two32BitIntegers,
            NativeMovieCallbackReturnShape.BooleanCompatibleInteger);
        releaseIdentity = ValidateInitialIdentity(
            NativeMovieCallbackKind.Release,
            NativeMovieCallbackParameterShape.None,
            NativeMovieCallbackReturnShape.Void);
        startIdentity = ValidateInitialIdentity(
            NativeMovieCallbackKind.Start,
            NativeMovieCallbackParameterShape.None,
            NativeMovieCallbackReturnShape.BooleanCompatibleInteger);
        stopIdentity = ValidateInitialIdentity(
            NativeMovieCallbackKind.Stop,
            NativeMovieCallbackParameterShape.None,
            NativeMovieCallbackReturnShape.Void);
    }

    internal int OnPrepare(int argument0, int argument1)
    {
        var entryEpoch = Volatile.Read(ref observationEpoch);
        var ownsObservation = observationGate.TryEnter();
        try
        {
            var mayObserve = ownsObservation
                             && Volatile.Read(ref stopped) == 0
                             && !IsFatallyDegraded
                             && IsCurrentIdentity(
                                 NativeMovieCallbackKind.Prepare,
                                 prepareIdentity);

            var result = InvokePrepareOriginal(argument0, argument1);
            if (!mayObserve
                || !IsObservationCurrent(
                    entryEpoch,
                    NativeMovieCallbackKind.Prepare,
                    prepareIdentity))
            {
                ResetObservationState();
                return result;
            }

            var path = ReadBoundedPath();
            if (!TryReadTimestamp(out var timestampUtc)
                || !TryCapturePrepare(
                    timestampUtc,
                    path,
                    result != 0,
                    out var capture))
            {
                ResetObservationState();
                return result;
            }

            var lifecycleEvent = ObserveSafely(capture);
            var snapshot = new NativeMovieIngressSnapshot(
                NativeMovieCallbackKind.Prepare,
                capture.Sequence,
                capture.TimestampUtc,
                new NativeMoviePrepareArguments(argument0, argument1),
                result,
                result != 0,
                path,
                null,
                null,
                lifecycleEvent);
            if (!IsObservationCurrent(
                    entryEpoch,
                    NativeMovieCallbackKind.Prepare,
                    prepareIdentity)
                || !observationGate.TryCommit())
            {
                ResetObservationState();
                return result;
            }

            TryPublish(snapshot);
            return result;
        }
        finally
        {
            observationGate.Exit();
        }
    }

    internal void OnRelease()
    {
        var entryEpoch = Volatile.Read(ref observationEpoch);
        var ownsObservation = observationGate.TryEnter();
        try
        {
            var mayObserve = ownsObservation
                             && Volatile.Read(ref stopped) == 0
                             && !IsFatallyDegraded
                             && IsCurrentIdentity(
                                 NativeMovieCallbackKind.Release,
                                 releaseIdentity);

            InvokeReleaseOriginal();
            if (!mayObserve
                || !IsObservationCurrent(
                    entryEpoch,
                    NativeMovieCallbackKind.Release,
                    releaseIdentity))
            {
                ResetObservationState();
                return;
            }

            if (!TryCreateTerminalSnapshot(
                    releaseIdentity,
                    NativeMovieCallbackKind.Release,
                    out var snapshot)
                || !IsObservationCurrent(
                    entryEpoch,
                    NativeMovieCallbackKind.Release,
                    releaseIdentity)
                || !observationGate.TryCommit())
            {
                ResetObservationState();
                return;
            }

            TryPublish(snapshot);
        }
        finally
        {
            observationGate.Exit();
        }
    }

    internal int OnStart()
    {
        var entryEpoch = Volatile.Read(ref observationEpoch);
        var ownsObservation = observationGate.TryEnter();
        try
        {
            var mayObserve = ownsObservation
                             && Volatile.Read(ref stopped) == 0
                             && !IsFatallyDegraded
                             && IsCurrentIdentity(
                                 NativeMovieCallbackKind.Start,
                                 startIdentity);
            var stateBefore = 0;
            var stateBeforeRead = mayObserve && TryReadState(out stateBefore);

            var result = InvokeStartOriginal();
            if (!mayObserve
                || !stateBeforeRead
                || !IsObservationCurrent(
                    entryEpoch,
                    NativeMovieCallbackKind.Start,
                    startIdentity)
                || !TryReadState(out var stateAfter)
                || !TryReadTimestamp(out var timestampUtc)
                || !TryCaptureStart(
                    timestampUtc,
                    stateBefore,
                    stateAfter,
                    out var capture))
            {
                ResetObservationState();
                return result;
            }

            MovieLifecycleEvent? lifecycleEvent;
            if (result == 0)
            {
                ResetLifecycleState();
                lifecycleEvent = null;
            }
            else
            {
                lifecycleEvent = ObserveSafely(capture);
            }
            var snapshot = new NativeMovieIngressSnapshot(
                NativeMovieCallbackKind.Start,
                capture.Sequence,
                capture.TimestampUtc,
                null,
                result,
                result != 0,
                null,
                stateBefore,
                stateAfter,
                lifecycleEvent);
            if (!IsObservationCurrent(
                    entryEpoch,
                    NativeMovieCallbackKind.Start,
                    startIdentity)
                || !observationGate.TryCommit())
            {
                ResetObservationState();
                return result;
            }

            TryPublish(snapshot);
            return result;
        }
        finally
        {
            observationGate.Exit();
        }
    }

    internal void OnStop()
    {
        var entryEpoch = Volatile.Read(ref observationEpoch);
        var ownsObservation = observationGate.TryEnter();
        try
        {
            var mayObserve = ownsObservation
                             && Volatile.Read(ref stopped) == 0
                             && !IsFatallyDegraded
                             && IsCurrentIdentity(
                                 NativeMovieCallbackKind.Stop,
                                 stopIdentity);

            InvokeStopOriginal();
            if (!mayObserve
                || !IsObservationCurrent(
                    entryEpoch,
                    NativeMovieCallbackKind.Stop,
                    stopIdentity))
            {
                ResetObservationState();
                return;
            }

            if (!TryCreateTerminalSnapshot(
                    stopIdentity,
                    NativeMovieCallbackKind.Stop,
                    out var snapshot)
                || !IsObservationCurrent(
                    entryEpoch,
                    NativeMovieCallbackKind.Stop,
                    stopIdentity)
                || !observationGate.TryCommit())
            {
                ResetObservationState();
                return;
            }

            TryPublish(snapshot);
        }
        finally
        {
            observationGate.Exit();
        }
    }

    internal void Stop()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
        {
            return;
        }

        observationGate.InvalidateUncommitted();
        ResetObservationState();
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Signals the owner to disable and remove the detours outside the native
    /// callback. Managed exceptions are never allowed to unwind through the
    /// unmanaged callback boundary.
    /// </summary>
    internal bool IsFatallyDegraded => Volatile.Read(ref fatalIngressFailure) != 0;

    private NativeMovieCallbackIdentity ValidateInitialIdentity(
        NativeMovieCallbackKind kind,
        NativeMovieCallbackParameterShape parameterShape,
        NativeMovieCallbackReturnShape returnShape)
    {
        try
        {
            if (contract.TryValidateIdentity(kind, out var identity)
                && identity.Metadata.Kind == kind
                && identity.Metadata.IsInlineDetourEligible
                && identity.Metadata.Shape == new NativeMovieCallbackShape(
                    NativeMovieCallbackAbi.MicrosoftX64,
                    parameterShape,
                    returnShape))
            {
                return identity;
            }
        }
        catch
        {
            // The constructor fails closed below.
        }

        throw new InvalidOperationException(
            $"The exact {kind} native movie callback identity is unavailable.");
    }

    private int InvokePrepareOriginal(int argument0, int argument1)
    {
        try
        {
            return prepareOriginal(argument0, argument1);
        }
        catch
        {
            MarkFatalOriginalFailure();
            return 0;
        }
    }

    private void InvokeReleaseOriginal()
    {
        try
        {
            releaseOriginal();
        }
        catch
        {
            MarkFatalOriginalFailure();
        }
    }

    private int InvokeStartOriginal()
    {
        try
        {
            return startOriginal();
        }
        catch
        {
            MarkFatalOriginalFailure();
            return 0;
        }
    }

    private void InvokeStopOriginal()
    {
        try
        {
            stopOriginal();
        }
        catch
        {
            MarkFatalOriginalFailure();
        }
    }

    private void MarkFatalOriginalFailure()
    {
        MarkFatalIngressFailure();
    }

    private bool IsCurrentIdentity(
        NativeMovieCallbackKind kind,
        NativeMovieCallbackIdentity expected)
    {
        try
        {
            return contract.TryValidateIdentity(kind, out var current)
                   && current.Metadata == expected.Metadata
                   && current.Address == expected.Address
                   && string.Equals(
                       current.RuntimeSha256,
                       expected.RuntimeSha256,
                       StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private bool IsObservationCurrent(
        long entryEpoch,
        NativeMovieCallbackKind kind,
        NativeMovieCallbackIdentity expected) =>
        Volatile.Read(ref stopped) == 0
        && !IsFatallyDegraded
        && entryEpoch == Volatile.Read(ref observationEpoch)
        && IsCurrentIdentity(kind, expected);

    private string? ReadBoundedPath()
    {
        try
        {
            var path = pathReader();
            if (path is null
                || path.Length == 0
                || path.Length > MaximumCanonicalMoviePathLength)
            {
                return null;
            }

            return new string(path.AsSpan());
        }
        catch
        {
            return null;
        }
    }

    private bool TryReadState(out int state)
    {
        try
        {
            state = stateReader();
            return true;
        }
        catch
        {
            state = 0;
            return false;
        }
    }

    private bool TryReadTimestamp(out DateTime timestampUtc)
    {
        try
        {
            timestampUtc = clock();
            return timestampUtc.Kind == DateTimeKind.Utc;
        }
        catch
        {
            timestampUtc = default;
            return false;
        }
    }

    private bool TryCapturePrepare(
        DateTime timestampUtc,
        string? canonicalMoviePath,
        bool succeeded,
        out NativeMovieCallbackCapture capture)
    {
        try
        {
            return contract.TryCapturePrepare(
                prepareIdentity,
                timestampUtc,
                canonicalMoviePath,
                succeeded,
                out capture);
        }
        catch
        {
            capture = default;
            return false;
        }
    }

    private bool TryCaptureStart(
        DateTime timestampUtc,
        int stateBefore,
        int stateAfter,
        out NativeMovieCallbackCapture capture)
    {
        try
        {
            return contract.TryCaptureStart(
                startIdentity,
                timestampUtc,
                stateBefore,
                stateAfter,
                out capture);
        }
        catch
        {
            capture = default;
            return false;
        }
    }

    private bool TryCreateTerminalSnapshot(
        NativeMovieCallbackIdentity identity,
        NativeMovieCallbackKind kind,
        out NativeMovieIngressSnapshot snapshot)
    {
        if (!TryReadTimestamp(out var timestampUtc)
            || !TryCaptureTerminal(identity, timestampUtc, out var capture))
        {
            snapshot = null!;
            return false;
        }

        var lifecycleEvent = ObserveSafely(capture);
        snapshot = new NativeMovieIngressSnapshot(
            kind,
            capture.Sequence,
            capture.TimestampUtc,
            null,
            null,
            false,
            null,
            null,
            null,
            lifecycleEvent);
        return true;
    }

    private bool TryCaptureTerminal(
        NativeMovieCallbackIdentity identity,
        DateTime timestampUtc,
        out NativeMovieCallbackCapture capture)
    {
        try
        {
            return contract.TryCaptureTerminal(identity, timestampUtc, out capture);
        }
        catch
        {
            capture = default;
            return false;
        }
    }

    private MovieLifecycleEvent? ObserveSafely(NativeMovieCallbackCapture capture)
    {
        try
        {
            if (observer.TryObserve(capture, out var lifecycleEvent))
            {
                return lifecycleEvent;
            }
        }
        catch
        {
            // Observer failures are converted to permanent degradation below.
        }

        MarkFatalWithoutLifecycleReset();
        return null;
    }

    private void ResetObservationState()
    {
        Interlocked.Increment(ref observationEpoch);
        ResetLifecycleState();
    }

    private bool ResetLifecycleState()
    {
        try
        {
            if (observer.TryReset())
            {
                return true;
            }
        }
        catch
        {
            // Observer failures are converted to permanent degradation below.
        }

        MarkFatalWithoutLifecycleReset();
        return false;
    }

    private bool TryPublish(NativeMovieIngressSnapshot snapshot)
    {
        try
        {
            if (captureQueue.TryEnqueue(snapshot))
            {
                return true;
            }
        }
        catch
        {
            // Queue failures are converted to permanent degradation below.
        }

        MarkFatalIngressFailure();
        return false;
    }

    private void MarkFatalIngressFailure()
    {
        Interlocked.Exchange(ref fatalIngressFailure, 1);
        ResetObservationState();
    }

    private void MarkFatalWithoutLifecycleReset()
    {
        Interlocked.Exchange(ref fatalIngressFailure, 1);
        observationGate.InvalidateUncommitted();
        Interlocked.Increment(ref observationEpoch);
    }
}
