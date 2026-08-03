using System.Runtime.InteropServices;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void TranslatedFieldMessageCallbackOriginal();

/// <summary>
/// Captures exact MESSAGE ownership before invoking the translated original,
/// then copies the guest EAX lifecycle result and publishes pointer-free state.
/// </summary>
internal sealed class Steam2026FieldMessageDetourIngressCoordinator : IDisposable
{
    private readonly Steam2026FieldMessageCallbackContract contract;
    private readonly TranslatedFieldMessageCallbackOriginal original;
    private readonly Steam2026DialogueIngressSequencer dialogueIngressSequencer;
    private readonly Func<DateTime> clock;
    private readonly INativeIngressQueue<Steam2026FieldMessageIngressSnapshot> captureQueue;
    private readonly NativeIngressObservationGate observationGate = new();
    private long observationEpoch;
    private int stopped;
    private int fatalIngressFailure;

    internal Steam2026FieldMessageDetourIngressCoordinator(
        Steam2026FieldMessageCallbackContract contract,
        TranslatedFieldMessageCallbackOriginal original,
        Steam2026DialogueIngressSequencer dialogueIngressSequencer,
        Func<DateTime> clock,
        INativeIngressQueue<Steam2026FieldMessageIngressSnapshot> captureQueue)
    {
        this.contract = contract ?? throw new ArgumentNullException(nameof(contract));
        this.original = original ?? throw new ArgumentNullException(nameof(original));
        this.dialogueIngressSequencer = dialogueIngressSequencer
            ?? throw new ArgumentNullException(nameof(dialogueIngressSequencer));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.captureQueue = captureQueue ?? throw new ArgumentNullException(nameof(captureQueue));
        if (!contract.HasExactSupportedFingerprint
            || !contract.TryValidateCaptureIdentity(out var hostAddress)
            || hostAddress == 0)
        {
            throw new InvalidOperationException(
                "The exact translated MESSAGE callback identity is unavailable.");
        }
    }

    internal bool IsFatallyDegraded => Volatile.Read(ref fatalIngressFailure) != 0;

    internal void OnMessage()
    {
        var entryEpoch = Volatile.Read(ref observationEpoch);
        var ownsObservation = observationGate.TryEnter();
        try
        {
            var observation = default(FieldOpcodeMessageObservation);
            var timestampUtc = default(DateTime);
            var sequence = 0L;
            var canPublish = ownsObservation
                             && Volatile.Read(ref stopped) == 0
                             && !IsFatallyDegraded
                             && contract.TryCaptureMessage(out observation)
                             && TryReadTimestamp(out timestampUtc)
                             && dialogueIngressSequencer.TryReserve(out sequence);

            InvokeOriginal();

            if (!canPublish
                || !IsObservationCurrent(entryEpoch)
                || !contract.TryReadPostCallResult(out var result))
            {
                ResetObservationState();
                return;
            }

            var snapshot = new Steam2026FieldMessageIngressSnapshot(
                sequence,
                timestampUtc,
                observation,
                result);
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
        && entryEpoch == Volatile.Read(ref observationEpoch);

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

    private bool TryPublish(Steam2026FieldMessageIngressSnapshot snapshot)
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
