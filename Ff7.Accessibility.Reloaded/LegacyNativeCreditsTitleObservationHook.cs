using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Reloaded;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
internal delegate void LegacyNativeCreditsTitleRendererOriginal();

/// <summary>Exact low/high stock title-renderer cohort; no output occurs in either detour.</summary>
internal sealed class LegacyNativeCreditsTitleObservationHook : IDisposable
{
    internal const uint LowResolutionRendererAddress = 0x007AD064;
    internal const uint HighResolutionRendererAddress = 0x007ACCAD;
    internal const string LowResolutionPrefixHex = "558BEC83EC48C745E0CDCCCC3DA118F4F4008945E88B0D1CF4F400894DE4C745";
    internal const string HighResolutionPrefixHex = "558BEC83EC40C745E8CDCCCC3DC745F483000000C745EC44010000C745FCC800";
    private readonly LegacyNativeCreditsTitleRendererOriginal lowDetour;
    private readonly LegacyNativeCreditsTitleRendererOriginal highDetour;
    private IHook<LegacyNativeCreditsTitleRendererOriginal>? lowHook;
    private IHook<LegacyNativeCreditsTitleRendererOriginal>? highHook;
    private LegacyNativeCreditsTitleRendererOriginal? lowOriginal;
    private LegacyNativeCreditsTitleRendererOriginal? highOriginal;
    private readonly NativeCreditsTitleObservationIngress ingress;
    private int disposed;
    private GCHandle nativeRoot;

    private LegacyNativeCreditsTitleObservationHook(ILegacyAddressSpace memory)
    {
        ingress = new NativeCreditsTitleObservationIngress(memory);
        lowDetour = OnLowCallback;
        highDetour = OnHighCallback;
        // Process-lifetime root includes both typed originals, including failed/retired owners.
        nativeRoot = GCHandle.Alloc(this);
    }
    private void Install(IReloadedHooks hooks)
    {
        lowHook = hooks.CreateHook(lowDetour, LowResolutionRendererAddress, -1);
        lowOriginal = lowHook.OriginalFunction;
        highHook = hooks.CreateHook(highDetour, HighResolutionRendererAddress, -1);
        highOriginal = highHook.OriginalFunction;
        lowHook.Activate();
        highHook.Activate();
    }
    internal static bool TryCreate(string executableSha256, ILegacyAddressSpace memory,
        IReloadedHooks hooks, out LegacyNativeCreditsTitleObservationHook? result, out string diagnostic)
    {
        result = null;
        LegacyNativeCreditsTitleObservationHook? candidate = null;
        try
        {
            // Validate the whole cohort before creating or activating either native callable.
            if (IntPtr.Size != 4 || !TryValidateIdentity(executableSha256, memory)
                || !IsMainExecutableRenderer(LowResolutionRendererAddress, LowResolutionPrefixHex)
                || !IsMainExecutableRenderer(HighResolutionRendererAddress, HighResolutionPrefixHex))
                throw new InvalidOperationException("The exact x86 native title-renderer cohort is unavailable.");
            candidate = new LegacyNativeCreditsTitleObservationHook(memory);
            candidate.Install(hooks);
            result = candidate;
            diagnostic = "Installed exact low/high native title observation callbacks.";
            return true;
        }
        catch (Exception ex)
        {
            candidate?.Dispose(); result = candidate;
            diagnostic = "Native title callbacks are not ready: " + ex.Message;
            return false;
        }
    }
    internal static bool TryValidateIdentity(string executableSha256, ILegacyAddressSpace memory)
    {
        if (!string.Equals(executableSha256, LegacyNativeMoviePlaybackReader.StockSha256, StringComparison.Ordinal)
            || memory is null) return false;
        try
        {
            return Matches(memory, LowResolutionRendererAddress, LowResolutionPrefixHex)
                && Matches(memory, HighResolutionRendererAddress, HighResolutionPrefixHex);
        }
        catch { return false; }
    }
    private static bool Matches(ILegacyAddressSpace memory, uint address, string prefix)
    {
        var expected = Convert.FromHexString(prefix);
        Span<byte> first = stackalloc byte[expected.Length];
        Span<byte> second = stackalloc byte[expected.Length];
        return memory.TryRead(address, first) && memory.TryRead(address, second)
            && first.SequenceEqual(second) && first.SequenceEqual(expected);
    }
    private void OnLowCallback() => ingress.OnCallback(NativeCreditsTitleRenderer.LowResolution, () => lowOriginal!());
    private void OnHighCallback() => ingress.OnCallback(NativeCreditsTitleRenderer.HighResolution, () => highOriginal!());
    internal bool TryTakeLatest(out NativeCaptionObservation? observation) => ingress.TryTakeLatest(out observation);
    internal bool HasOriginalFault => ingress.HasOriginalFault;
    private static bool IsMainExecutableRenderer(uint address, string prefix)
    {
        if (VirtualQuery((nint)address, out var region, (nuint)Marshal.SizeOf<MemoryRegion>()) == 0) return false;
        var start = (ulong)region.BaseAddress; var size = (ulong)region.RegionSize;
        var protection = region.Protection & 0xFF; var length = (ulong)(prefix.Length / 2);
        return region.State == 0x1000 && region.Type == 0x1000000
            && (ulong)region.AllocationBase == 0x00400000 && protection is 0x10 or 0x20 or 0x40 or 0x80
            && (region.Protection & 0x100) == 0 && start <= address && size >= length && address - start <= size - length;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion
    {
        public nuint BaseAddress; public nuint AllocationBase; public uint AllocationProtection;
        public nuint RegionSize; public uint State; public uint Protection; public uint Type;
    }
    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(nint address, out MemoryRegion region, nuint length);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        ingress.Stop();
        try { lowHook?.Disable(); } catch { }
        try { highHook?.Disable(); } catch { }
        // Keep ingress and typed originals available to already-entered callbacks.
    }
}
