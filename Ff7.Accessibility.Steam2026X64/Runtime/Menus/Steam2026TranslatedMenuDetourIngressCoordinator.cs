using System.Runtime.InteropServices;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void TranslatedMenuCallbackOriginal();

internal sealed record TranslatedMenuWidgetIngressObservation(
    string VerifiedName,
    MenuWidgetKind Kind,
    int First,
    int Cursor,
    int Columns,
    int Rows,
    int ScrollOffset,
    int ScrollDelta,
    int ScrollState)
{
    public uint WidgetIdentity { get; init; }
}

/// <summary>
/// Immutable, pointer-free research copy completed after one translated menu
/// original returns. It has no runtime publication or speech surface.
/// </summary>
internal sealed record TranslatedMenuIngressSnapshot(
    Steam2026MenuCallbackKind CallbackKind,
    long Sequence,
    DateTime TimestampUtc,
    TranslatedMenuCursorObservation? Cursor,
    TranslatedMenuWidgetIngressObservation? ActiveWidget,
    TranslatedMenuTextObservation? Text);

/// <summary>
/// Research-only ingress around the six exact capture-eligible translated
/// menu callbacks. All native originals and observation dependencies are
/// supplied by the caller; observation dependencies must be bounded,
/// nonwaiting callback-safe probes. This type creates no interception objects.
/// </summary>
internal sealed class Steam2026TranslatedMenuDetourIngressCoordinator : IDisposable
{
    private readonly NativeIngressObservationGate observationGate = new();
    private readonly Steam2026MenuCallbackContract contract;
    private readonly TranslatedMenuCallbackOriginal cursorBOriginal;
    private readonly TranslatedMenuCallbackOriginal cursorAOriginal;
    private readonly TranslatedMenuCallbackOriginal activeWidgetOriginal;
    private readonly TranslatedMenuCallbackOriginal encodedTextBOriginal;
    private readonly TranslatedMenuCallbackOriginal encodedTextAOriginal;
    private readonly TranslatedMenuCallbackOriginal asciiRendererOriginal;
    private readonly Func<uint, (bool Success, ActiveMenuWidgetSnapshot Snapshot)> widgetNormalizer;
    private readonly Func<DateTime> clock;
    private readonly INativeIngressQueue<TranslatedMenuIngressSnapshot> captureQueue;
    private readonly Steam2026MenuCallbackIdentity cursorBIdentity;
    private readonly Steam2026MenuCallbackIdentity cursorAIdentity;
    private readonly Steam2026MenuCallbackIdentity activeWidgetIdentity;
    private readonly Steam2026MenuCallbackIdentity encodedTextBIdentity;
    private readonly Steam2026MenuCallbackIdentity encodedTextAIdentity;
    private readonly Steam2026MenuCallbackIdentity asciiRendererIdentity;
    private long nextSequence;
    private long observationEpoch;
    private int fatalIngressFailure;
    private int stopped;

    internal Steam2026TranslatedMenuDetourIngressCoordinator(
        Steam2026MenuCallbackContract contract,
        TranslatedMenuCallbackOriginal cursorBOriginal,
        TranslatedMenuCallbackOriginal cursorAOriginal,
        TranslatedMenuCallbackOriginal activeWidgetOriginal,
        TranslatedMenuCallbackOriginal encodedTextBOriginal,
        TranslatedMenuCallbackOriginal encodedTextAOriginal,
        TranslatedMenuCallbackOriginal asciiRendererOriginal,
        Func<uint, (bool Success, ActiveMenuWidgetSnapshot Snapshot)> widgetNormalizer,
        Func<DateTime> clock,
        INativeIngressQueue<TranslatedMenuIngressSnapshot> captureQueue)
    {
        this.contract = contract ?? throw new ArgumentNullException(nameof(contract));
        if (!contract.HasExactSupportedFingerprint)
        {
            throw new InvalidOperationException(
                "Translated menu ingress requires an exact supported Steam 2026 fingerprint contract.");
        }

        this.cursorBOriginal = cursorBOriginal ?? throw new ArgumentNullException(nameof(cursorBOriginal));
        this.cursorAOriginal = cursorAOriginal ?? throw new ArgumentNullException(nameof(cursorAOriginal));
        this.activeWidgetOriginal = activeWidgetOriginal ?? throw new ArgumentNullException(nameof(activeWidgetOriginal));
        this.encodedTextBOriginal = encodedTextBOriginal ?? throw new ArgumentNullException(nameof(encodedTextBOriginal));
        this.encodedTextAOriginal = encodedTextAOriginal ?? throw new ArgumentNullException(nameof(encodedTextAOriginal));
        this.asciiRendererOriginal = asciiRendererOriginal ?? throw new ArgumentNullException(nameof(asciiRendererOriginal));
        this.widgetNormalizer = widgetNormalizer ?? throw new ArgumentNullException(nameof(widgetNormalizer));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.captureQueue = captureQueue ?? throw new ArgumentNullException(nameof(captureQueue));

        cursorBIdentity = ValidateInitialIdentity(Steam2026MenuCallbackKind.CursorB);
        cursorAIdentity = ValidateInitialIdentity(Steam2026MenuCallbackKind.CursorA);
        activeWidgetIdentity = ValidateInitialIdentity(Steam2026MenuCallbackKind.ActiveWidgetUpdate);
        encodedTextBIdentity = ValidateInitialIdentity(Steam2026MenuCallbackKind.EncodedTextB);
        encodedTextAIdentity = ValidateInitialIdentity(Steam2026MenuCallbackKind.EncodedTextA);
        asciiRendererIdentity = ValidateInitialIdentity(Steam2026MenuCallbackKind.AsciiRenderer);

        if (Steam2026MenuCallbackCatalog.GetMetadata(
                Steam2026MenuCallbackKind.WidgetConstructor).IsCaptureEligible)
        {
            throw new InvalidOperationException(
                "The translated widget constructor must remain identity-only.");
        }
    }

    internal void OnCursorB() => ProcessCallback(
        Steam2026MenuCallbackKind.CursorB,
        cursorBIdentity,
        cursorBOriginal);

    internal void OnCursorA() => ProcessCallback(
        Steam2026MenuCallbackKind.CursorA,
        cursorAIdentity,
        cursorAOriginal);

    internal void OnActiveWidgetUpdate() => ProcessCallback(
        Steam2026MenuCallbackKind.ActiveWidgetUpdate,
        activeWidgetIdentity,
        activeWidgetOriginal);

    internal void OnEncodedTextB() => ProcessCallback(
        Steam2026MenuCallbackKind.EncodedTextB,
        encodedTextBIdentity,
        encodedTextBOriginal);

    internal void OnEncodedTextA() => ProcessCallback(
        Steam2026MenuCallbackKind.EncodedTextA,
        encodedTextAIdentity,
        encodedTextAOriginal);

    internal void OnAsciiRenderer() => ProcessCallback(
        Steam2026MenuCallbackKind.AsciiRenderer,
        asciiRendererIdentity,
        asciiRendererOriginal);

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
    /// Signals the owner to remove the detours outside the unmanaged callback.
    /// Managed exceptions are never allowed to unwind through native code.
    /// </summary>
    internal bool IsFatallyDegraded => Volatile.Read(ref fatalIngressFailure) != 0;

    private void ProcessCallback(
        Steam2026MenuCallbackKind kind,
        Steam2026MenuCallbackIdentity expectedIdentity,
        TranslatedMenuCallbackOriginal original)
    {
        var entryEpoch = Volatile.Read(ref observationEpoch);
        var ownsObservation = observationGate.TryEnter();
        try
        {
            var payload = default(CapturedMenuPayload);
            var canPublish = ownsObservation
                             && Volatile.Read(ref stopped) == 0
                             && !IsFatallyDegraded
                             && IsCurrentIdentity(expectedIdentity)
                             && TryCapturePayload(kind, out payload);
            if (!canPublish)
            {
                payload = default;
                ResetObservationState();
            }

            InvokeOriginal(original);

            if (!canPublish
                || !IsObservationCurrent(entryEpoch, expectedIdentity)
                || !TryReadTimestamp(out var timestampUtc)
                || !TryAllocateSequence(out var sequence))
            {
                ResetObservationState();
                return;
            }

            var snapshot = payload.ToSnapshot(kind, sequence, timestampUtc);
            if (!IsObservationCurrent(entryEpoch, expectedIdentity)
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

    private bool IsObservationCurrent(
        long entryEpoch,
        Steam2026MenuCallbackIdentity expectedIdentity) =>
        Volatile.Read(ref stopped) == 0
        && !IsFatallyDegraded
        && entryEpoch == Volatile.Read(ref observationEpoch)
        && IsCurrentIdentity(expectedIdentity);

    private Steam2026MenuCallbackIdentity ValidateInitialIdentity(
        Steam2026MenuCallbackKind kind)
    {
        try
        {
            if (contract.TryValidateCaptureIdentity(kind, out var identity)
                && identity.Metadata.Kind == kind
                && identity.Metadata.IsCaptureEligible
                && identity.Metadata.HostAbi == TranslatedMenuHostAbi.TranslatedX86VoidNoArguments)
            {
                return identity;
            }
        }
        catch
        {
            // Construction fails closed below.
        }

        throw new InvalidOperationException(
            $"The exact {kind} translated menu callback identity is unavailable.");
    }

    private bool IsCurrentIdentity(Steam2026MenuCallbackIdentity expectedIdentity)
    {
        try
        {
            return contract.IsCurrentCaptureIdentity(expectedIdentity);
        }
        catch
        {
            return false;
        }
    }

    private bool TryCapturePayload(
        Steam2026MenuCallbackKind kind,
        out CapturedMenuPayload payload)
    {
        payload = default;
        try
        {
            switch (kind)
            {
                case Steam2026MenuCallbackKind.CursorB:
                case Steam2026MenuCallbackKind.CursorA:
                    if (!contract.TryCaptureCursor(kind, out var cursor))
                    {
                        return false;
                    }

                    payload = CapturedMenuPayload.FromCursor(cursor);
                    return true;

                case Steam2026MenuCallbackKind.ActiveWidgetUpdate:
                    if (!contract.TryCaptureActiveWidget(out var rawWidget)
                        || !TryNormalizeWidget(rawWidget.GuestWidgetAddress, out var widget))
                    {
                        return false;
                    }

                    payload = CapturedMenuPayload.FromWidget(widget);
                    return true;

                case Steam2026MenuCallbackKind.EncodedTextB:
                case Steam2026MenuCallbackKind.EncodedTextA:
                    if (!contract.TryCaptureEncodedText(kind, out var encodedText))
                    {
                        return false;
                    }

                    payload = CapturedMenuPayload.FromText(CopyText(encodedText));
                    return true;

                case Steam2026MenuCallbackKind.AsciiRenderer:
                    if (!contract.TryCaptureAsciiRenderer(out var asciiText))
                    {
                        return false;
                    }

                    payload = CapturedMenuPayload.FromText(CopyText(asciiText));
                    return true;

                default:
                    return false;
            }
        }
        catch
        {
            payload = default;
            return false;
        }
    }

    private bool TryNormalizeWidget(
        uint expectedGuestAddress,
        out TranslatedMenuWidgetIngressObservation observation)
    {
        observation = null!;
        (bool Success, ActiveMenuWidgetSnapshot Snapshot) result;
        try
        {
            result = widgetNormalizer(expectedGuestAddress);
        }
        catch
        {
            return false;
        }

        var snapshot = result.Snapshot;
        if (!result.Success
            || snapshot.Address != expectedGuestAddress
            || snapshot.Columns is <= 0 or > 16
            || snapshot.Rows is <= 0 or > 400
            || snapshot.First < 0
            || snapshot.First >= snapshot.Columns
            || snapshot.Cursor < 0
            || snapshot.Cursor >= snapshot.Rows
            || !MenuWidgetCatalog.TryResolve(expectedGuestAddress, out var descriptor)
            || !string.Equals(snapshot.Name, descriptor.Name, StringComparison.Ordinal)
            || snapshot.Kind != descriptor.Kind)
        {
            return false;
        }

        observation = new TranslatedMenuWidgetIngressObservation(
            new string(descriptor.Name.AsSpan()),
            descriptor.Kind,
            snapshot.First,
            snapshot.Cursor,
            snapshot.Columns,
            snapshot.Rows,
            snapshot.ScrollOffset,
            snapshot.ScrollDelta,
            snapshot.ScrollState)
        {
            WidgetIdentity = expectedGuestAddress
        };
        return true;
    }

    private void InvokeOriginal(TranslatedMenuCallbackOriginal original)
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

    private bool TryPublish(TranslatedMenuIngressSnapshot snapshot)
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

    private void ResetObservationState()
    {
        Interlocked.Increment(ref observationEpoch);
    }

    private void MarkFatalIngressFailure()
    {
        Interlocked.Exchange(ref fatalIngressFailure, 1);
        ResetObservationState();
    }

    private static TranslatedMenuTextObservation CopyText(
        TranslatedMenuTextObservation observation) =>
        observation with { Text = new string(observation.Text.AsSpan()) };

    private readonly record struct CapturedMenuPayload(
        TranslatedMenuCursorObservation? Cursor,
        TranslatedMenuWidgetIngressObservation? ActiveWidget,
        TranslatedMenuTextObservation? Text)
    {
        public static CapturedMenuPayload FromCursor(
            TranslatedMenuCursorObservation cursor) =>
            new(cursor, null, null);

        public static CapturedMenuPayload FromWidget(
            TranslatedMenuWidgetIngressObservation widget) =>
            new(null, widget, null);

        public static CapturedMenuPayload FromText(
            TranslatedMenuTextObservation text) =>
            new(null, null, text);

        public TranslatedMenuIngressSnapshot ToSnapshot(
            Steam2026MenuCallbackKind callbackKind,
            long sequence,
            DateTime timestampUtc) =>
            new(
                callbackKind,
                sequence,
                timestampUtc,
                Cursor,
                ActiveWidget,
                Text);
    }
}
