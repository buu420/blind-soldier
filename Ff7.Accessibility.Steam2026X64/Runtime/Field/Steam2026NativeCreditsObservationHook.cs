using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void Steam2026NativeCreditsRendererOriginal();

/// <summary>Bounded visible-row observations from the exact translated staff-credit renderer.</summary>
internal sealed class Steam2026NativeCreditsObservationHook : IDisposable
{
    internal static readonly TranslatedFunctionMapDefinition Renderer = new(
        0x007A753A, 0x016FD2B0, 0x01503BC0,
        "40534883EC208B0DFC59B3008B1DFA59B30083C1FC890DED59B300E8C0B4B3FE");
    private readonly Steam2026NativeCreditsRendererOriginal detour;
    private IHook<Steam2026NativeCreditsRendererOriginal>? hook;
    private NativeCreditsObservationIngress? ingress;
    private int disposed;
    private GCHandle nativeRoot;

    private Steam2026NativeCreditsObservationHook(Steam2026FingerprintResult fingerprint,
        ulong moduleBase, ulong imageSize, INativeMemoryReader memory, IReloadedHooks hooks)
    {
        var addressSpace = ValidatedTranslatedX86AddressSpaceFactory.Create(fingerprint, moduleBase, memory);
        var validator = new TranslatedFunctionMapValidator(moduleBase, imageSize, memory);
        if (!validator.TryValidate(Renderer, out var target))
            throw new InvalidOperationException("The exact translated native credits renderer is unavailable.");
        detour = OnCallback;
        hook = hooks.CreateHook(detour, checked((long)target), -1);
        var original = hook.OriginalFunction;
        ingress = new NativeCreditsObservationIngress(addressSpace, () => original());
        nativeRoot = GCHandle.Alloc(this);
    }

    internal static bool TryCreate(Steam2026FingerprintResult fingerprint, ulong moduleBase,
        ulong imageSize, INativeMemoryReader memory, IReloadedHooks hooks,
        out Steam2026NativeCreditsObservationHook? result, out string diagnostic)
    {
        result = null;
        diagnostic = string.Empty;
        Steam2026NativeCreditsObservationHook? candidate = null;
        try
        {
            candidate = new Steam2026NativeCreditsObservationHook(fingerprint, moduleBase, imageSize, memory, hooks);
            candidate.hook!.Activate();
            result = candidate;
            diagnostic = "Installed exact translated native credits observation callback.";
            return true;
        }
        catch (Exception ex)
        {
            candidate?.Dispose();
            // Preserve an owner for any callback that entered during failed activation.
            result = candidate;
            diagnostic = "Translated native credits callback is not ready: " + ex.Message;
            return false;
        }
    }

    internal bool TryTakeLatest(out NativeCreditsSnapshot? snapshot)
    {
        snapshot = null;
        return ingress is not null && ingress.TryTakeLatest(out snapshot);
    }
    internal bool HasOriginalFault => ingress?.HasOriginalFault == true;
    private void OnCallback() => ingress?.OnCallback();
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        ingress?.Stop();
        try { hook?.Disable(); } catch { }
        // Preserve the original-function path for callbacks already inside the detour.
    }
}
