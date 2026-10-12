using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void Steam2026NativeCreditsTitleRendererOriginal();

/// <summary>Both exact translated title renderers, validated before either is activated.</summary>
internal sealed class Steam2026NativeCreditsTitleObservationHook : IDisposable
{
    internal static readonly TranslatedFunctionMapDefinition LowResolutionRenderer = new(
        0x007AD064, 0x016FD5F0, 0x01528520,
        "40574883EC308B0D9C10B10083C1FC48895C24408B1D9210B100890D8810B100");
    internal static readonly TranslatedFunctionMapDefinition HighResolutionRenderer = new(
        0x007ACCAD, 0x016FD5E0, 0x01526AD0,
        "40574883EC308B0DEC2AB10083C1FC48895C24408B1DE22AB100890DD82AB100");
    private readonly Steam2026NativeCreditsTitleRendererOriginal lowDetour;
    private readonly Steam2026NativeCreditsTitleRendererOriginal highDetour;
    private IHook<Steam2026NativeCreditsTitleRendererOriginal>? lowHook;
    private IHook<Steam2026NativeCreditsTitleRendererOriginal>? highHook;
    private Steam2026NativeCreditsTitleRendererOriginal? lowOriginal;
    private Steam2026NativeCreditsTitleRendererOriginal? highOriginal;
    private readonly NativeCreditsTitleObservationIngress ingress;
    private int disposed;
    private GCHandle nativeRoot;

    private Steam2026NativeCreditsTitleObservationHook(ILegacyAddressSpace memory)
    {
        ingress = new NativeCreditsTitleObservationIngress(memory);
        lowDetour = OnLowCallback; highDetour = OnHighCallback;
        nativeRoot = GCHandle.Alloc(this);
    }
    private void Install(IReloadedHooks hooks, ulong lowTarget, ulong highTarget)
    {
        lowHook = hooks.CreateHook(lowDetour, checked((long)lowTarget), -1);
        lowOriginal = lowHook.OriginalFunction;
        highHook = hooks.CreateHook(highDetour, checked((long)highTarget), -1);
        highOriginal = highHook.OriginalFunction;
        lowHook.Activate(); highHook.Activate();
    }
    internal static bool TryCreate(Steam2026FingerprintResult fingerprint, ulong moduleBase,
        ulong imageSize, INativeMemoryReader memory, IReloadedHooks hooks,
        out Steam2026NativeCreditsTitleObservationHook? result, out string diagnostic)
    {
        result = null;
        Steam2026NativeCreditsTitleObservationHook? candidate = null;
        try
        {
            var addressSpace = ValidatedTranslatedX86AddressSpaceFactory.Create(fingerprint, moduleBase, memory);
            var validator = new TranslatedFunctionMapValidator(moduleBase, imageSize, memory);
            if (!validator.TryValidate(LowResolutionRenderer, out var lowTarget)
                || !validator.TryValidate(HighResolutionRenderer, out var highTarget))
                throw new InvalidOperationException("The exact translated title-renderer cohort is unavailable.");
            candidate = new Steam2026NativeCreditsTitleObservationHook(addressSpace);
            candidate.Install(hooks, lowTarget, highTarget);
            result = candidate;
            diagnostic = "Installed exact translated low/high title observation callbacks.";
            return true;
        }
        catch (Exception ex)
        {
            candidate?.Dispose(); result = candidate;
            diagnostic = "Translated title callbacks are not ready: " + ex.Message;
            return false;
        }
    }
    private void OnLowCallback() => ingress.OnCallback(NativeCreditsTitleRenderer.LowResolution, () => lowOriginal!());
    private void OnHighCallback() => ingress.OnCallback(NativeCreditsTitleRenderer.HighResolution, () => highOriginal!());
    internal bool TryTakeLatest(out NativeCaptionObservation? observation) => ingress.TryTakeLatest(out observation);
    internal bool HasOriginalFault => ingress.HasOriginalFault;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        ingress.Stop();
        try { lowHook?.Disable(); } catch { }
        try { highHook?.Disable(); } catch { }
        // A late callback retains its original path and performs no native-state reads.
    }
}
