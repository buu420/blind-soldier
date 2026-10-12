using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Reloaded;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
internal delegate void LegacyNativeCreditsRendererOriginal();

/// <summary>The exact stock credits renderer; observation and speech are consumed by the worker.</summary>
internal sealed class LegacyNativeCreditsObservationHook : IDisposable
{
    internal const uint RendererAddress = 0x007A753A;
    internal const string ExpectedPrefixHex = "558BEC83EC14E83BD8C5FF83E802F7D81BC083E0038945FCC745F400000000C7";
    private readonly LegacyNativeCreditsRendererOriginal detour;
    private IHook<LegacyNativeCreditsRendererOriginal>? hook;
    private NativeCreditsObservationIngress? ingress;
    private int disposed;
    private GCHandle nativeRoot;

    private LegacyNativeCreditsObservationHook(ILegacyAddressSpace memory, IReloadedHooks hooks)
    {
        detour = OnCallback;
        hook = hooks.CreateHook(detour, RendererAddress, -1);
        var original = hook.OriginalFunction;
        ingress = new NativeCreditsObservationIngress(memory, () => original());
        nativeRoot = GCHandle.Alloc(this);
    }

    internal static bool TryCreate(string executableSha256, ILegacyAddressSpace memory,
        IReloadedHooks hooks, out LegacyNativeCreditsObservationHook? result, out string diagnostic)
    {
        result = null;
        diagnostic = string.Empty;
        LegacyNativeCreditsObservationHook? candidate = null;
        try
        {
            if (IntPtr.Size != 4 || !IsMainExecutableRenderer()
                || !TryValidateIdentity(executableSha256, memory))
                throw new InvalidOperationException("The exact x86 native credits renderer is unavailable.");
            candidate = new LegacyNativeCreditsObservationHook(memory, hooks);
            candidate.hook!.Activate();
            result = candidate;
            diagnostic = "Installed exact native credits observation callback.";
            return true;
        }
        catch (Exception ex)
        {
            candidate?.Dispose();
            // A created hook can have an already-entered callback even when activation failed.
            // Return that stopped owner so the runtime can retain its original-function root.
            result = candidate;
            diagnostic = "Native credits callback is not ready: " + ex.Message;
            return false;
        }
    }

    internal static bool TryValidateIdentity(string executableSha256, ILegacyAddressSpace memory)
    {
        if (!string.Equals(executableSha256, LegacyNativeMoviePlaybackReader.StockSha256, StringComparison.Ordinal)
            || memory is null) return false;
        var expected = Convert.FromHexString(ExpectedPrefixHex);
        Span<byte> first = stackalloc byte[expected.Length];
        Span<byte> second = stackalloc byte[expected.Length];
        try { return memory.TryRead(RendererAddress, first) && memory.TryRead(RendererAddress, second)
            && first.SequenceEqual(second) && first.SequenceEqual(expected); }
        catch { return false; }
    }

    internal bool TryTakeLatest(out NativeCreditsSnapshot? snapshot)
    {
        snapshot = null;
        return ingress is not null && ingress.TryTakeLatest(out snapshot);
    }
    internal bool HasOriginalFault => ingress?.HasOriginalFault == true;
    private void OnCallback() => ingress?.OnCallback();
    private static bool IsMainExecutableRenderer()
    {
        if (VirtualQuery((nint)RendererAddress, out var region, (nuint)Marshal.SizeOf<MemoryRegion>()) == 0)
            return false;
        var start = (ulong)region.BaseAddress;
        var size = (ulong)region.RegionSize;
        var protection = region.Protection & 0xFF;
        var executable = protection is 0x10 or 0x20 or 0x40 or 0x80;
        var prefixLength = (ulong)(ExpectedPrefixHex.Length / 2);
        return region.State == 0x1000 && region.Type == 0x1000000
            && (ulong)region.AllocationBase == 0x00400000 && executable
            && (region.Protection & 0x100) == 0 && start <= RendererAddress
            && size >= prefixLength && RendererAddress - start <= size - prefixLength;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion
    {
        public nuint BaseAddress;
        public nuint AllocationBase;
        public uint AllocationProtection;
        public nuint RegionSize;
        public uint State;
        public uint Protection;
        public uint Type;
    }
    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(nint address, out MemoryRegion region, nuint length);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        ingress?.Stop();
        try { hook?.Disable(); } catch { }
        // Keep the typed original/ingress available for an already-entered callback.
    }
}
