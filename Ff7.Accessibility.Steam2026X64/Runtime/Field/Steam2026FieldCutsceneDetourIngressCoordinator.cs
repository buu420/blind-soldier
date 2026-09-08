using System.Runtime.InteropServices;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void TranslatedFieldCutsceneCallbackOriginal();

/// <param name="TimestampUtc">
/// The instant observed at the native boundary, before the original ran. The
/// description's start window is measured from here, so a queue that backs up
/// cannot buy it a fresh two seconds.
/// </param>
/// <param name="HasMovieSample">
/// True when <paramref name="MovieSample"/> was taken before the original. Only
/// such a sample carries a usable MOVIE handler state: FUN_0061A321 turns a fresh
/// 0 into a 4 on the call itself, so a sample read when this snapshot is drained
/// reports the very first described film as a repeat.
/// </param>
internal readonly record struct Steam2026FieldCutsceneIngressSnapshot(
    long Sequence,
    DateTime TimestampUtc,
    FieldScriptContext Context,
    bool HasMovieSample = false,
    FieldMovieNarrationSample MovieSample = default);

/// <summary>
/// Copies a checked, pointer-free WAIT or SOUND script context before invoking
/// the translated native original. Publication and all speech happen afterward.
/// </summary>
internal sealed class Steam2026FieldCutsceneDetourIngressCoordinator : IDisposable
{
    private readonly Steam2026FieldCutsceneCallbackContract contract;
    private readonly TranslatedFieldCutsceneCallbackOriginal original;
    private readonly Func<DateTime> clock;
    private readonly INativeIngressQueue<Steam2026FieldCutsceneIngressSnapshot> captureQueue;
    private readonly NativeIngressObservationGate observationGate = new();
    private readonly Steam2026FieldCutsceneCallbackIdentity callbackIdentity;
    private long nextSequence;
    private long observationEpoch;
    private int stopped;
    private int fatalIngressFailure;

    internal Steam2026FieldCutsceneDetourIngressCoordinator(
        Steam2026FieldCutsceneCallbackContract contract,
        Steam2026FieldCutsceneCallbackKind callbackKind,
        TranslatedFieldCutsceneCallbackOriginal original,
        Func<DateTime> clock,
        INativeIngressQueue<Steam2026FieldCutsceneIngressSnapshot> captureQueue)
    {
        this.contract = contract ?? throw new ArgumentNullException(nameof(contract));
        this.original = original ?? throw new ArgumentNullException(nameof(original));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.captureQueue = captureQueue ?? throw new ArgumentNullException(nameof(captureQueue));
        callbackIdentity = ValidateInitialIdentity(contract, callbackKind);
    }

    /// <summary>
    /// Signals the owner to remove the detour outside this unmanaged callback.
    /// Managed failures are never allowed to unwind through translated code.
    /// </summary>
    internal bool IsFatallyDegraded => Volatile.Read(ref fatalIngressFailure) != 0;

    internal void OnCallback()
    {
        var entryEpoch = Volatile.Read(ref observationEpoch);
        var ownsObservation = observationGate.TryEnter();
        try
        {
            var context = default(FieldScriptContext);
            var movieSample = default(FieldMovieNarrationSample);
            var canPublish = ownsObservation
                             && Volatile.Read(ref stopped) == 0
                             && !IsFatallyDegraded
                             && IsCurrentIdentity()
                             && TryCaptureContext(out context);

            // Both of these are destroyed by the original and cannot be recovered
            // afterwards: the MOVIE handler mutates its own state on this call, and
            // the elapsed time becomes whatever the queue happened to take. The
            // sample is only taken for the film-start opcode, so every other hooked
            // opcode keeps the cheap path.
            var timestampUtc = default(DateTime);
            var capturedTimestamp = canPublish && TryReadTimestamp(out timestampUtc);
            var hasMovieSample = canPublish
                                 && context.Opcode == FieldOpcodeAddressResolver.OpcodeMovieIndex
                                 && TryCaptureMovieSample(context.FieldId, out movieSample);

            InvokeOriginal();

            if (!canPublish
                || !capturedTimestamp
                || !IsObservationCurrent(entryEpoch)
                || !TryAllocateSequence(out var sequence))
            {
                ResetObservationState();
                return;
            }

            var snapshot = new Steam2026FieldCutsceneIngressSnapshot(
                sequence,
                timestampUtc,
                context,
                hasMovieSample,
                movieSample);
            if (!IsObservationCurrent(entryEpoch)
                || !observationGate.TryCommit())
            {
                ResetObservationState();
                return;
            }

            TryPublish(snapshot);
        }
        catch
        {
            MarkFatalIngressFailure();
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

    private bool IsObservationCurrent(long entryEpoch) =>
        Volatile.Read(ref stopped) == 0
        && !IsFatallyDegraded
        && entryEpoch == Volatile.Read(ref observationEpoch)
        && IsCurrentIdentity();

    private static Steam2026FieldCutsceneCallbackIdentity ValidateInitialIdentity(
        Steam2026FieldCutsceneCallbackContract contract,
        Steam2026FieldCutsceneCallbackKind callbackKind)
    {
        if (!contract.HasExactSupportedFingerprint)
        {
            throw new InvalidOperationException(
                "Translated field-cutscene ingress requires the exact supported fingerprint.");
        }

        try
        {
            if (contract.TryValidateCaptureIdentity(callbackKind, out var identity)
                && identity.Metadata.Kind == callbackKind
                && identity.Metadata.HostAbi
                    == TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments)
            {
                return identity;
            }
        }
        catch
        {
            // Construction fails closed below.
        }

        throw new InvalidOperationException(
            $"The exact translated {callbackKind.ToString().ToUpperInvariant()} callback identity is unavailable.");
    }

    private bool IsCurrentIdentity()
    {
        try
        {
            return contract.IsCurrentCaptureIdentity(callbackIdentity);
        }
        catch
        {
            return false;
        }
    }

    private bool TryCaptureContext(out FieldScriptContext context)
    {
        context = default;
        try
        {
            return contract.TryCaptureContext(callbackIdentity, out context);
        }
        catch
        {
            context = default;
            return false;
        }
    }

    private bool TryCaptureMovieSample(int fieldId, out FieldMovieNarrationSample sample)
    {
        sample = default;
        try
        {
            return contract.TryCaptureMovieSample(fieldId, out sample);
        }
        catch
        {
            sample = default;
            return false;
        }
    }

    private void InvokeOriginal()
    {
        try
        {
            original();
        }
        catch
        {
            MarkFatalIngressFailure();
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

    private bool TryAllocateSequence(out long sequence)
    {
        sequence = Interlocked.Increment(ref nextSequence);
        return sequence > 0;
    }

    private bool TryPublish(Steam2026FieldCutsceneIngressSnapshot snapshot)
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
            // Queue failures become permanent degradation below.
        }

        MarkFatalIngressFailure();
        return false;
    }

    private void ResetObservationState() =>
        Interlocked.Increment(ref observationEpoch);

    private void MarkFatalIngressFailure()
    {
        Interlocked.Exchange(ref fatalIngressFailure, 1);
        ResetObservationState();
    }
}
