using Ff7.Accessibility.Steam2026X64.Runtime;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Owns the exact-fingerprint translated opcode detours used by the shared
/// Steam 2026 cutscene-description catalog.
/// </summary>
internal sealed class Steam2026FieldCutsceneHookSet : IDisposable
{
    private const int CaptureQueueCapacity = 512;

    private readonly BoundedNativeIngressQueue<Steam2026FieldCutsceneIngressSnapshot>
        captureQueue = new(CaptureQueueCapacity);
    private readonly Dictionary<
        Steam2026FieldCutsceneCallbackKind,
        TranslatedFieldCutsceneCallbackOriginal> detours = [];
    private readonly Dictionary<
        Steam2026FieldCutsceneCallbackKind,
        IHook<TranslatedFieldCutsceneCallbackOriginal>> hooks = [];
    private readonly Dictionary<
        Steam2026FieldCutsceneCallbackKind,
        Steam2026FieldCutsceneDetourIngressCoordinator> coordinators = [];
    private Steam2026FieldCutsceneCallbackContract? contract;
    private int disposed;

    private Steam2026FieldCutsceneHookSet(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(hooks);

        try
        {
            contract = new Steam2026FieldCutsceneCallbackContract(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory);

            var identities = new Dictionary<
                Steam2026FieldCutsceneCallbackKind,
                Steam2026FieldCutsceneCallbackIdentity>();
            foreach (var kind in Enum.GetValues<Steam2026FieldCutsceneCallbackKind>())
            {
                if (!TryValidateIdentity(contract, kind, out var identity))
                {
                    throw new InvalidOperationException(
                        $"The exact translated {kind.ToString().ToUpperInvariant()} callback identity is unavailable.");
                }

                identities.Add(kind, identity);
            }

            foreach (var (kind, identity) in identities)
            {
                var callbackKind = kind;
                TranslatedFieldCutsceneCallbackOriginal detour =
                    () => OnCallback(callbackKind);
                var hook = hooks.CreateHook(
                    detour,
                    checked((long)identity.HostAddress),
                    -1);
                detours.Add(kind, detour);
                this.hooks.Add(kind, hook);
                coordinators.Add(
                    kind,
                    new Steam2026FieldCutsceneDetourIngressCoordinator(
                        contract,
                        kind,
                        hook.OriginalFunction,
                        () => DateTime.UtcNow,
                        captureQueue));
            }

            foreach (var hook in this.hooks.Values)
            {
                hook.Activate();
            }

            contract.ActivateHookLease(IsHookEnabled);
        }
        catch
        {
            StopCoordinators();
            contract?.RevokeHookLease();
            DisableHooks();
            throw;
        }
    }

    internal bool IsFatallyDegraded =>
        coordinators.Values.Any(coordinator => coordinator.IsFatallyDegraded);

    internal static bool TryCreate(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks,
        out Steam2026FieldCutsceneHookSet hookSet,
        out string diagnostic)
    {
        hookSet = null!;
        diagnostic = string.Empty;
        try
        {
            hookSet = new Steam2026FieldCutsceneHookSet(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory,
                hooks);
            diagnostic =
                $"Installed {hookSet.hooks.Count} exact translated cutscene opcode callbacks.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"Translated cutscene opcode callbacks are not ready: {ex.Message}";
            return false;
        }
    }

    internal bool TryDequeue(out Steam2026FieldCutsceneIngressSnapshot snapshot) =>
        captureQueue.TryDequeue(out snapshot);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        StopCoordinators();
        contract?.RevokeHookLease();
        DisableHooks();
        // Retain stopped coordinators as fallback original-function paths
        // if third-party hook teardown unexpectedly leaves a detour live.
    }

    private static bool TryValidateIdentity(
        Steam2026FieldCutsceneCallbackContract contract,
        Steam2026FieldCutsceneCallbackKind kind,
        out Steam2026FieldCutsceneCallbackIdentity identity)
    {
        identity = default;
        return contract.TryValidateCaptureIdentity(kind, out identity)
               && identity.Metadata.Kind == kind
               && identity.Metadata.HostAbi
                    == TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments
               && identity.HostAddress != 0;
    }

    private void OnCallback(Steam2026FieldCutsceneCallbackKind kind)
    {
        if (coordinators.TryGetValue(kind, out var coordinator))
        {
            coordinator.OnCallback();
        }
    }

    private bool IsHookEnabled(Steam2026FieldCutsceneCallbackKind kind) =>
        hooks.TryGetValue(kind, out var hook)
        && hook is { IsHookActivated: true, IsHookEnabled: true };

    private void StopCoordinators()
    {
        foreach (var coordinator in coordinators.Values)
        {
            coordinator.Stop();
        }
    }

    private void DisableHooks()
    {
        foreach (var hook in hooks.Values)
        {
            TryDisableHook(hook);
        }
    }

    private static void TryDisableHook(
        IHook<TranslatedFieldCutsceneCallbackOriginal>? hook)
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
            // Teardown is best effort and must never escape into Reloaded-II.
        }
    }
}
