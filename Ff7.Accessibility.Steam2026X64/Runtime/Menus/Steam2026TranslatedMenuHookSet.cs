using Ff7.Accessibility.Reloaded;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

/// <summary>
/// Owns the six exact-fingerprint translated menu detours used by the native
/// Steam 2026 research path. No detour is activated until every translated
/// identity has validated and every original-function wrapper exists.
/// </summary>
internal sealed class Steam2026TranslatedMenuHookSet : IDisposable
{
    private const int CaptureQueueCapacity = 2048;

    private readonly BoundedNativeIngressQueue<TranslatedMenuIngressSnapshot> captureQueue =
        new(CaptureQueueCapacity);
    private readonly TranslatedMenuCallbackOriginal cursorBDetour;
    private readonly TranslatedMenuCallbackOriginal cursorADetour;
    private readonly TranslatedMenuCallbackOriginal activeWidgetDetour;
    private readonly TranslatedMenuCallbackOriginal encodedTextBDetour;
    private readonly TranslatedMenuCallbackOriginal encodedTextADetour;
    private readonly TranslatedMenuCallbackOriginal asciiRendererDetour;

    private IHook<TranslatedMenuCallbackOriginal>? cursorBHook;
    private IHook<TranslatedMenuCallbackOriginal>? cursorAHook;
    private IHook<TranslatedMenuCallbackOriginal>? activeWidgetHook;
    private IHook<TranslatedMenuCallbackOriginal>? encodedTextBHook;
    private IHook<TranslatedMenuCallbackOriginal>? encodedTextAHook;
    private IHook<TranslatedMenuCallbackOriginal>? asciiRendererHook;
    private Steam2026MenuCallbackContract? contract;
    private Steam2026TranslatedMenuDetourIngressCoordinator? coordinator;
    private int disposed;

    private Steam2026TranslatedMenuHookSet(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(hooks);

        cursorBDetour = OnCursorB;
        cursorADetour = OnCursorA;
        activeWidgetDetour = OnActiveWidget;
        encodedTextBDetour = OnEncodedTextB;
        encodedTextADetour = OnEncodedTextA;
        asciiRendererDetour = OnAsciiRenderer;

        try
        {
            contract = new Steam2026MenuCallbackContract(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory);
            var targets = ValidateAllTargets(contract);

            cursorBHook = CreateHook(hooks, cursorBDetour, targets[Steam2026MenuCallbackKind.CursorB]);
            cursorAHook = CreateHook(hooks, cursorADetour, targets[Steam2026MenuCallbackKind.CursorA]);
            activeWidgetHook = CreateHook(
                hooks,
                activeWidgetDetour,
                targets[Steam2026MenuCallbackKind.ActiveWidgetUpdate]);
            encodedTextBHook = CreateHook(
                hooks,
                encodedTextBDetour,
                targets[Steam2026MenuCallbackKind.EncodedTextB]);
            encodedTextAHook = CreateHook(
                hooks,
                encodedTextADetour,
                targets[Steam2026MenuCallbackKind.EncodedTextA]);
            asciiRendererHook = CreateHook(
                hooks,
                asciiRendererDetour,
                targets[Steam2026MenuCallbackKind.AsciiRenderer]);

            var addressSpace = ValidatedTranslatedX86AddressSpaceFactory.Create(
                fingerprint,
                moduleBase,
                memory);
            var widgetReader = new ActiveMenuWidgetReader(addressSpace);
            coordinator = new Steam2026TranslatedMenuDetourIngressCoordinator(
                contract,
                cursorBHook.OriginalFunction,
                cursorAHook.OriginalFunction,
                activeWidgetHook.OriginalFunction,
                encodedTextBHook.OriginalFunction,
                encodedTextAHook.OriginalFunction,
                asciiRendererHook.OriginalFunction,
                address => widgetReader.TryRead(address, out var snapshot)
                    ? (true, snapshot)
                    : (false, default),
                () => DateTime.UtcNow,
                captureQueue);

            cursorBHook.Activate();
            cursorAHook.Activate();
            activeWidgetHook.Activate();
            encodedTextBHook.Activate();
            encodedTextAHook.Activate();
            asciiRendererHook.Activate();
            contract.ActivateHookLease(IsHookCohortEnabled);
        }
        catch
        {
            coordinator?.Stop();
            contract?.RevokeHookLease();
            DisableHooks();
            throw;
        }
    }

    internal bool IsFatallyDegraded =>
        coordinator?.IsFatallyDegraded == true
        || (Volatile.Read(ref disposed) == 0
            && contract is { } activeContract
            && !activeContract.IsActiveHookLeaseHealthy(Environment.TickCount64));

    internal static bool TryCreate(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks,
        out Steam2026TranslatedMenuHookSet hookSet,
        out string diagnostic)
    {
        hookSet = null!;
        diagnostic = string.Empty;
        try
        {
            hookSet = new Steam2026TranslatedMenuHookSet(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory,
                hooks);
            diagnostic = "Installed six exact-identity translated menu callbacks.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"Translated menu callbacks are not ready: {ex.Message}";
            return false;
        }
    }

    internal bool TryDequeue(out TranslatedMenuIngressSnapshot snapshot) =>
        captureQueue.TryDequeue(out snapshot);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // Stop capture publication before removing any detour so a callback
        // already in flight cannot publish after teardown begins.
        coordinator?.Stop();
        contract?.RevokeHookLease();
        DisableHooks();
        coordinator = null;
        contract = null;
    }

    private static Dictionary<Steam2026MenuCallbackKind, ulong> ValidateAllTargets(
        Steam2026MenuCallbackContract contract)
    {
        Steam2026MenuCallbackKind[] required =
        [
            Steam2026MenuCallbackKind.CursorB,
            Steam2026MenuCallbackKind.CursorA,
            Steam2026MenuCallbackKind.ActiveWidgetUpdate,
            Steam2026MenuCallbackKind.EncodedTextB,
            Steam2026MenuCallbackKind.EncodedTextA,
            Steam2026MenuCallbackKind.AsciiRenderer
        ];

        var targets = new Dictionary<Steam2026MenuCallbackKind, ulong>();
        foreach (var kind in required)
        {
            if (!contract.TryValidateCaptureIdentity(kind, out var identity)
                || identity.Metadata.Kind != kind
                || identity.HostAddress == 0)
            {
                throw new InvalidOperationException(
                    $"The exact translated callback identity for {kind} is unavailable.");
            }

            targets.Add(kind, identity.HostAddress);
        }

        if (targets.Values.Distinct().Count() != required.Length)
        {
            throw new InvalidOperationException("Translated callback targets are not unique.");
        }

        return targets;
    }

    private static IHook<TranslatedMenuCallbackOriginal> CreateHook(
        IReloadedHooks hooks,
        TranslatedMenuCallbackOriginal detour,
        ulong address) =>
        hooks.CreateHook(detour, checked((long)address), -1);

    private void OnCursorB() => coordinator?.OnCursorB();

    private void OnCursorA() => coordinator?.OnCursorA();

    private void OnActiveWidget() => coordinator?.OnActiveWidgetUpdate();

    private void OnEncodedTextB() => coordinator?.OnEncodedTextB();

    private void OnEncodedTextA() => coordinator?.OnEncodedTextA();

    private void OnAsciiRenderer() => coordinator?.OnAsciiRenderer();

    private bool IsHookCohortEnabled(Steam2026MenuCallbackKind kind) =>
        (kind is Steam2026MenuCallbackKind.CursorB
            or Steam2026MenuCallbackKind.CursorA
            or Steam2026MenuCallbackKind.ActiveWidgetUpdate
            or Steam2026MenuCallbackKind.EncodedTextB
            or Steam2026MenuCallbackKind.EncodedTextA
            or Steam2026MenuCallbackKind.AsciiRenderer)
        && IsEnabled(cursorBHook)
        && IsEnabled(cursorAHook)
        && IsEnabled(activeWidgetHook)
        && IsEnabled(encodedTextBHook)
        && IsEnabled(encodedTextAHook)
        && IsEnabled(asciiRendererHook);

    private void DisableHooks()
    {
        Disable(asciiRendererHook);
        Disable(encodedTextAHook);
        Disable(encodedTextBHook);
        Disable(activeWidgetHook);
        Disable(cursorAHook);
        Disable(cursorBHook);
    }

    private static void Disable(IHook<TranslatedMenuCallbackOriginal>? hook)
    {
        try
        {
            if (hook is { IsHookActivated: true, IsHookEnabled: true })
            {
                hook.Disable();
            }
        }
        catch
        {
            // Teardown is best-effort and must never escape into Reloaded-II.
        }
    }

    private static bool IsEnabled(IHook<TranslatedMenuCallbackOriginal>? hook) =>
        hook is { IsHookActivated: true, IsHookEnabled: true };
}
