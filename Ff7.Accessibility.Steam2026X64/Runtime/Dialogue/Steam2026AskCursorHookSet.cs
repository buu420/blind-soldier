using Ff7.Accessibility.Steam2026X64.Runtime;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

/// <summary>
/// Owns the exact-fingerprint translated ASK selection-update detour.
/// </summary>
internal sealed class Steam2026AskCursorHookSet : IDisposable
{
    private const int CaptureQueueCapacity = 256;

    private readonly BoundedNativeIngressQueue<Steam2026AskCursorIngressSnapshot>
        captureQueue = new(CaptureQueueCapacity);
    private readonly TranslatedAskCursorCallbackOriginal askCursorDetour;
    private IHook<TranslatedAskCursorCallbackOriginal>? askCursorHook;
    private Steam2026AskCursorCallbackContract? contract;
    private Steam2026AskCursorDetourIngressCoordinator? coordinator;
    private int disposed;

    private Steam2026AskCursorHookSet(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks,
        Steam2026DialogueIngressSequencer dialogueIngressSequencer)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(hooks);
        askCursorDetour = OnAskCursor;

        try
        {
            contract = new Steam2026AskCursorCallbackContract(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory);
            if (!contract.TryValidateCaptureIdentity(out var hostAddress)
                || hostAddress == 0)
            {
                throw new InvalidOperationException(
                    "The exact translated ASK cursor callback identity is unavailable.");
            }

            askCursorHook = hooks.CreateHook(
                askCursorDetour,
                checked((long)hostAddress),
                -1);
            coordinator = new Steam2026AskCursorDetourIngressCoordinator(
                contract,
                askCursorHook.OriginalFunction,
                dialogueIngressSequencer,
                () => DateTime.UtcNow,
                captureQueue);

            askCursorHook.Activate();
            contract.ActivateHookLease(IsAskCursorHookEnabled);
        }
        catch
        {
            coordinator?.Stop();
            contract?.RevokeHookLease();
            DisableHook();
            throw;
        }
    }

    internal bool IsFatallyDegraded => coordinator?.IsFatallyDegraded == true;

    internal static bool TryCreate(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks,
        Steam2026DialogueIngressSequencer dialogueIngressSequencer,
        out Steam2026AskCursorHookSet hookSet,
        out string diagnostic)
    {
        hookSet = null!;
        diagnostic = string.Empty;
        try
        {
            hookSet = new Steam2026AskCursorHookSet(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory,
                hooks,
                dialogueIngressSequencer);
            diagnostic = "Installed the exact translated ASK selection callback.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"Translated ASK selection callback is not ready: {ex.Message}";
            return false;
        }
    }

    internal bool TryDequeue(out Steam2026AskCursorIngressSnapshot snapshot) =>
        captureQueue.TryDequeue(out snapshot);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        coordinator?.Stop();
        contract?.RevokeHookLease();
        DisableHook();
        // Keep the stopped coordinator as a fallback original-function path
        // if another hook manager leaves this detour reachable during teardown.
    }

    private void OnAskCursor() => coordinator?.OnAskCursor();

    private bool IsAskCursorHookEnabled() =>
        askCursorHook is { IsHookActivated: true, IsHookEnabled: true };

    private void DisableHook()
    {
        try
        {
            if (askCursorHook is { IsHookActivated: true, IsHookEnabled: true })
            {
                askCursorHook.Disable();
            }
        }
        catch
        {
            // Reloaded-II teardown is best effort and must not escape.
        }
    }
}
