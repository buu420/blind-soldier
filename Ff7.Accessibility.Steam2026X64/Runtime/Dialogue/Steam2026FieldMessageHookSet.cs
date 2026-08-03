using Ff7.Accessibility.Steam2026X64.Runtime;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

/// <summary>
/// Owns the exact-fingerprint translated field MESSAGE opcode detour.
/// </summary>
internal sealed class Steam2026FieldMessageHookSet : IDisposable
{
    private const int CaptureQueueCapacity = 512;

    private readonly BoundedNativeIngressQueue<Steam2026FieldMessageIngressSnapshot>
        captureQueue = new(CaptureQueueCapacity);
    private readonly TranslatedFieldMessageCallbackOriginal messageDetour;
    private IHook<TranslatedFieldMessageCallbackOriginal>? messageHook;
    private Steam2026FieldMessageCallbackContract? contract;
    private Steam2026FieldMessageDetourIngressCoordinator? coordinator;
    private int disposed;

    private Steam2026FieldMessageHookSet(
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
        messageDetour = OnMessage;

        try
        {
            contract = new Steam2026FieldMessageCallbackContract(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory);
            if (!contract.TryValidateCaptureIdentity(out var hostAddress)
                || hostAddress == 0)
            {
                throw new InvalidOperationException(
                    "The exact translated MESSAGE callback identity is unavailable.");
            }

            messageHook = hooks.CreateHook(
                messageDetour,
                checked((long)hostAddress),
                -1);
            coordinator = new Steam2026FieldMessageDetourIngressCoordinator(
                contract,
                messageHook.OriginalFunction,
                dialogueIngressSequencer,
                () => DateTime.UtcNow,
                captureQueue);

            messageHook.Activate();
            contract.ActivateHookLease(IsMessageHookEnabled);
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
        out Steam2026FieldMessageHookSet hookSet,
        out string diagnostic)
    {
        hookSet = null!;
        diagnostic = string.Empty;
        try
        {
            hookSet = new Steam2026FieldMessageHookSet(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory,
                hooks,
                dialogueIngressSequencer);
            diagnostic = "Installed the exact translated MESSAGE lifecycle callback.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"Translated MESSAGE lifecycle callback is not ready: {ex.Message}";
            return false;
        }
    }

    internal bool TryDequeue(out Steam2026FieldMessageIngressSnapshot snapshot) =>
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

    private void OnMessage() => coordinator?.OnMessage();

    private bool IsMessageHookEnabled() =>
        messageHook is { IsHookActivated: true, IsHookEnabled: true };

    private void DisableHook()
    {
        try
        {
            if (messageHook is { IsHookActivated: true, IsHookEnabled: true })
            {
                messageHook.Disable();
            }
        }
        catch
        {
            // Reloaded-II teardown is best effort and must not escape.
        }
    }
}
