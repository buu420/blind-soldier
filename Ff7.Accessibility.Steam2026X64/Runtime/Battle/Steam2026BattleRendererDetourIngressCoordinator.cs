using System.Runtime.InteropServices;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void TranslatedBattleRendererCallbackOriginal();

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void TranslatedBattleUpdateCallbackOriginal();

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void TranslatedBattleTextActivationCallbackOriginal();

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void TranslatedBattleResultsUpdateCallbackOriginal();

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void TranslatedBattleDamageDisplayCallbackOriginal();

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void TranslatedBattleActionTextCommitCallbackOriginal();

internal readonly record struct Steam2026BattleRendererIngressSnapshot(
    long Sequence,
    DateTime TimestampUtc,
    Steam2026BattleRendererCallbackKind Kind,
    short GuestValue,
    BattleDamagePopupSnapshot CapturedDamage = default,
    Steam2026BattleActionTextCommitSnapshot CapturedAction = default,
    Steam2026BattleEnemyActionIngressSnapshot EnemyActionBefore = default,
    Steam2026BattleEnemyActionIngressSnapshot EnemyActionAfter = default,
    Steam2026BattleVictoryIngressSnapshot VictoryAfter = default,
    Steam2026BattleResultsIngressSnapshot ResultsBefore = default,
    Steam2026BattleResultsIngressSnapshot ResultsAfter = default,
    TifaSlotResultSnapshot TifaSlotsBefore = default,
    TifaSlotResultSnapshot TifaSlotsAfter = default,
    TifaSlotCommittedResultSnapshot TifaSlotsCommittedAfter = default)
{
    internal Steam2026BattleRendererIngressSnapshot(
        long sequence,
        DateTime timestampUtc,
        short rendererState)
        : this(
            sequence,
            timestampUtc,
            Steam2026BattleRendererCallbackKind.MenuRenderer,
            rendererState)
    {
    }

    internal short RendererState =>
        Kind == Steam2026BattleRendererCallbackKind.MenuRenderer ? GuestValue : (short)0;

    internal short TextBufferIndex =>
        Kind == Steam2026BattleRendererCallbackKind.TextActivation ? GuestValue : (short)-1;
}

/// <summary>
/// Captures callback identity, stable signed guest arguments, ordering, time,
/// and the bounded damage-popup record that the native damage callback retires.
/// Actor correlation, all other battle decoding, and speech remain worker-side.
/// </summary>
internal sealed class Steam2026BattleRendererDetourIngressCoordinator : IDisposable
{
    private static readonly NativeIngressSequenceAssigner<
        Steam2026BattleRendererIngressSnapshot> AssignQueueSequence =
        static (snapshot, sequence) => snapshot with { Sequence = sequence };

    private readonly Steam2026BattleRendererCallbackContract contract;
    private readonly Action rendererOriginal;
    private readonly Action updateOriginal;
    private readonly Action textOriginal;
    private readonly Action resultsOriginal;
    private readonly Action damageOriginal;
    private readonly Action actionTextCommitOriginal;
    private readonly Func<DateTime> clock;
    private readonly ISequencedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>
        captureQueue;
    private readonly Dictionary<
        Steam2026BattleRendererCallbackKind,
        Steam2026BattleRendererCallbackIdentity> identities;
    private readonly NativeIngressObservationGate rendererGate = new();
    private readonly NativeIngressObservationGate updateGate = new();
    private readonly NativeIngressObservationGate textGate = new();
    private readonly NativeIngressObservationGate resultsGate = new();
    private readonly NativeIngressObservationGate damageGate = new();
    private readonly NativeIngressObservationGate actionTextCommitGate = new();
    private long observationEpoch;
    private int stopped;
    private int fatalIngressFailure;
    private int tifaSlotCaptureArmed;

    internal Steam2026BattleRendererDetourIngressCoordinator(
        Steam2026BattleRendererCallbackContract contract,
        TranslatedBattleRendererCallbackOriginal rendererOriginal,
        Func<DateTime> clock,
        ISequencedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot> captureQueue)
        : this(
            contract,
            rendererOriginal,
            static () => { },
            static () => { },
            static () => { },
            static () => { },
            static () => { },
            clock,
            captureQueue)
    {
    }

    internal Steam2026BattleRendererDetourIngressCoordinator(
        Steam2026BattleRendererCallbackContract contract,
        TranslatedBattleRendererCallbackOriginal rendererOriginal,
        TranslatedBattleUpdateCallbackOriginal updateOriginal,
        TranslatedBattleTextActivationCallbackOriginal textOriginal,
        TranslatedBattleResultsUpdateCallbackOriginal resultsOriginal,
        TranslatedBattleDamageDisplayCallbackOriginal damageOriginal,
        TranslatedBattleActionTextCommitCallbackOriginal actionTextCommitOriginal,
        Func<DateTime> clock,
        ISequencedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot> captureQueue)
    {
        this.contract = contract ?? throw new ArgumentNullException(nameof(contract));
        ArgumentNullException.ThrowIfNull(rendererOriginal);
        ArgumentNullException.ThrowIfNull(updateOriginal);
        ArgumentNullException.ThrowIfNull(textOriginal);
        ArgumentNullException.ThrowIfNull(resultsOriginal);
        ArgumentNullException.ThrowIfNull(damageOriginal);
        ArgumentNullException.ThrowIfNull(actionTextCommitOriginal);
        this.rendererOriginal = rendererOriginal.Invoke;
        this.updateOriginal = updateOriginal.Invoke;
        this.textOriginal = textOriginal.Invoke;
        this.resultsOriginal = resultsOriginal.Invoke;
        this.damageOriginal = damageOriginal.Invoke;
        this.actionTextCommitOriginal = actionTextCommitOriginal.Invoke;
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.captureQueue = captureQueue ?? throw new ArgumentNullException(nameof(captureQueue));
        identities = ValidateInitialIdentities(contract);
    }

    internal bool IsFatallyDegraded => Volatile.Read(ref fatalIngressFailure) != 0;

    internal void OnMenuRenderer() =>
        OnCallback(Steam2026BattleRendererCallbackKind.MenuRenderer, rendererOriginal, rendererGate);

    internal void OnBattleUpdate() =>
        OnCallback(Steam2026BattleRendererCallbackKind.BattleUpdate, updateOriginal, updateGate);

    internal void OnTextActivation() =>
        OnCallback(Steam2026BattleRendererCallbackKind.TextActivation, textOriginal, textGate);

    internal void OnResultsUpdate() =>
        OnCallback(Steam2026BattleRendererCallbackKind.ResultsUpdate, resultsOriginal, resultsGate);

    internal void OnDamageDisplay() =>
        OnCallback(Steam2026BattleRendererCallbackKind.DamageDisplay, damageOriginal, damageGate);

    internal void OnActionTextCommit() =>
        OnCallback(
            Steam2026BattleRendererCallbackKind.ActionTextCommit,
            actionTextCommitOriginal,
            actionTextCommitGate);

    internal void Stop()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
        {
            return;
        }

        rendererGate.InvalidateUncommitted();
        updateGate.InvalidateUncommitted();
        textGate.InvalidateUncommitted();
        resultsGate.InvalidateUncommitted();
        damageGate.InvalidateUncommitted();
        actionTextCommitGate.InvalidateUncommitted();
        ResetObservationState();
    }

    public void Dispose() => Stop();

    private void OnCallback(
        Steam2026BattleRendererCallbackKind kind,
        Action original,
        NativeIngressObservationGate observationGate)
    {
        var entryEpoch = Volatile.Read(ref observationEpoch);
        var ownsObservation = observationGate.TryEnter();
        var canPublish = false;
        var guestValue = default(short);
        var capturedDamage = BattleDamagePopupSnapshot.Invalid;
        var capturedAction = Steam2026BattleActionTextCommitSnapshot.Invalid;
        var enemyActionBefore =
            Steam2026BattleEnemyActionIngressSnapshot.NotCaptured;
        var enemyActionAfter =
            Steam2026BattleEnemyActionIngressSnapshot.NotCaptured;
        var victoryAfter = Steam2026BattleVictoryIngressSnapshot.NotCaptured;
        var resultsBefore = Steam2026BattleResultsIngressSnapshot.NotCaptured;
        var resultsAfter = Steam2026BattleResultsIngressSnapshot.NotCaptured;
        var tifaSlotsBefore = TifaSlotResultSnapshot.Invalid;
        var tifaSlotsAfter = TifaSlotResultSnapshot.Invalid;
        var tifaSlotsCommittedAfter = TifaSlotCommittedResultSnapshot.Invalid;
        var tifaWindowBefore = byte.MaxValue;
        var tifaWindowAfter = byte.MaxValue;
        var captureTifaWindow = false;
        var timestampUtc = default(DateTime);
        try
        {
            canPublish = ownsObservation
                         && Volatile.Read(ref stopped) == 0
                         && !IsFatallyDegraded
                          && TryPrepareObservation(
                              kind,
                              out guestValue,
                              out capturedDamage,
                              out capturedAction)
                          && TryReadTimestamp(out timestampUtc);
            if (canPublish
                && kind == Steam2026BattleRendererCallbackKind.BattleUpdate)
            {
                _ = contract.TryCaptureRawEnemyAction(
                    identities[kind],
                    out enemyActionBefore);
                captureTifaWindow = Volatile.Read(ref tifaSlotCaptureArmed) != 0;
                if (captureTifaWindow)
                {
                    _ = contract.TryCaptureTifaSlotWindowState(
                        identities[kind],
                        out tifaWindowBefore);
                }
            }

            if (canPublish
                && kind == Steam2026BattleRendererCallbackKind.ResultsUpdate)
            {
                _ = contract.TryCaptureResults(
                    identities[kind],
                    out resultsBefore);
            }

            if (canPublish
                && kind == Steam2026BattleRendererCallbackKind.MenuRenderer
                && guestValue == 0x1B)
            {
                if (contract.TryCaptureTifaSlots(
                        identities[kind],
                        out tifaSlotsBefore)
                    && tifaSlotsBefore.IsValid)
                {
                    Volatile.Write(ref tifaSlotCaptureArmed, 1);
                }
            }
        }
        catch
        {
            canPublish = false;
        }

        var originalSucceeded = InvokeOriginal(original);
        try
        {
            if (canPublish
                && originalSucceeded
                && kind == Steam2026BattleRendererCallbackKind.BattleUpdate)
            {
                _ = contract.TryCaptureRawEnemyAction(
                    identities[kind],
                    out enemyActionAfter);
            }

            if (canPublish
                && originalSucceeded
                && kind == Steam2026BattleRendererCallbackKind.BattleUpdate)
            {
                _ = contract.TryCaptureVictorySignal(
                    identities[kind],
                    out victoryAfter);
                if (captureTifaWindow
                    && contract.TryCaptureTifaSlotWindowState(
                        identities[kind],
                        out tifaWindowAfter)
                    && tifaWindowBefore == BattleStateReader.ActiveWindowState
                    && tifaWindowAfter == 3)
                {
                    _ = contract.TryCaptureCommittedTifaSlots(
                        identities[kind],
                        out tifaSlotsCommittedAfter);
                }

                if (captureTifaWindow
                    && (tifaWindowAfter == 0 || tifaWindowAfter == 3))
                {
                    Volatile.Write(ref tifaSlotCaptureArmed, 0);
                }
            }

            if (canPublish
                && originalSucceeded
                && kind == Steam2026BattleRendererCallbackKind.ResultsUpdate)
            {
                _ = contract.TryCaptureResults(
                    identities[kind],
                    out resultsAfter);
            }

            if (canPublish
                && originalSucceeded
                && kind == Steam2026BattleRendererCallbackKind.MenuRenderer
                && guestValue == 0x1B)
            {
                if (contract.TryCaptureTifaSlots(
                        identities[kind],
                        out tifaSlotsAfter)
                    && tifaSlotsAfter.IsValid)
                {
                    Volatile.Write(ref tifaSlotCaptureArmed, 1);
                }
            }

            if (!canPublish
                || !originalSucceeded
                || !IsObservationCurrent(kind, entryEpoch))
            {
                return;
            }

            var snapshot = new Steam2026BattleRendererIngressSnapshot(
                Sequence: 0,
                timestampUtc,
                kind,
                guestValue,
                capturedDamage,
                capturedAction,
                enemyActionBefore,
                enemyActionAfter,
                victoryAfter,
                resultsBefore,
                resultsAfter,
                tifaSlotsBefore,
                tifaSlotsAfter,
                tifaSlotsCommittedAfter);
            if (!IsObservationCurrent(kind, entryEpoch)
                || !observationGate.TryCommit())
            {
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

    private bool TryPrepareObservation(
        Steam2026BattleRendererCallbackKind kind,
        out short guestValue,
        out BattleDamagePopupSnapshot capturedDamage,
        out Steam2026BattleActionTextCommitSnapshot capturedAction)
    {
        guestValue = default;
        capturedDamage = BattleDamagePopupSnapshot.Invalid;
        capturedAction = Steam2026BattleActionTextCommitSnapshot.Invalid;
        if (!IsCurrentIdentity(kind))
        {
            return false;
        }

        return kind switch
        {
            Steam2026BattleRendererCallbackKind.MenuRenderer =>
                contract.TryCaptureRendererState(identities[kind], out guestValue),
            Steam2026BattleRendererCallbackKind.TextActivation =>
                contract.TryCaptureTextBufferIndex(identities[kind], out guestValue),
            Steam2026BattleRendererCallbackKind.BattleUpdate
                or Steam2026BattleRendererCallbackKind.ResultsUpdate => true,
            Steam2026BattleRendererCallbackKind.DamageDisplay =>
                contract.TryCaptureDamagePopup(
                    identities[kind],
                    out capturedDamage),
            Steam2026BattleRendererCallbackKind.ActionTextCommit =>
                contract.TryCaptureActionTextCommit(
                    identities[kind],
                    out capturedAction),
            _ => false
        };
    }

    private bool IsObservationCurrent(
        Steam2026BattleRendererCallbackKind kind,
        long entryEpoch) =>
        Volatile.Read(ref stopped) == 0
        && !IsFatallyDegraded
        && entryEpoch == Volatile.Read(ref observationEpoch)
        && IsCurrentIdentity(kind);

    private static Dictionary<
        Steam2026BattleRendererCallbackKind,
        Steam2026BattleRendererCallbackIdentity> ValidateInitialIdentities(
        Steam2026BattleRendererCallbackContract contract)
    {
        if (!contract.HasExactSupportedFingerprint)
        {
            throw new InvalidOperationException(
                "Battle callback ingress requires the exact supported fingerprint.");
        }

        var identities = new Dictionary<
            Steam2026BattleRendererCallbackKind,
            Steam2026BattleRendererCallbackIdentity>();
        try
        {
            foreach (var kind in Enum.GetValues<Steam2026BattleRendererCallbackKind>())
            {
                if (!contract.TryValidateCaptureIdentity(kind, out var identity)
                    || identity.Metadata.Kind != kind
                    || identity.Metadata.HostAbi
                        != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
                    || identity.HostAddress == 0)
                {
                    throw new InvalidOperationException(
                        $"The exact translated battle identity for {kind} is unavailable.");
                }

                identities.Add(kind, identity);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The exact translated battle callback cohort is unavailable.",
                ex);
        }

        if (identities.Values.Select(identity => identity.HostAddress).Distinct().Count()
            != identities.Count)
        {
            throw new InvalidOperationException("Translated battle callback targets are not unique.");
        }

        return identities;
    }

    private bool IsCurrentIdentity(Steam2026BattleRendererCallbackKind kind)
    {
        try
        {
            return identities.TryGetValue(kind, out var identity)
                   && contract.IsCurrentCaptureIdentity(identity);
        }
        catch
        {
            return false;
        }
    }

    private bool InvokeOriginal(Action original)
    {
        try
        {
            original();
            return true;
        }
        catch
        {
            MarkFatalIngressFailure();
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

    private bool TryPublish(Steam2026BattleRendererIngressSnapshot snapshot)
    {
        try
        {
            if (captureQueue.TryEnqueueSequenced(snapshot, AssignQueueSequence))
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

    private void ResetObservationState()
    {
        Volatile.Write(ref tifaSlotCaptureArmed, 0);
        Interlocked.Increment(ref observationEpoch);
    }

    private void MarkFatalIngressFailure()
    {
        Interlocked.Exchange(ref fatalIngressFailure, 1);
        ResetObservationState();
    }
}
