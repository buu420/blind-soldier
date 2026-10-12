using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Reloaded;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
internal delegate void LegacyNativeCreatureDrawOriginal(uint polygonSet, uint gameObject);

/// <summary>Owned current creature geometry at the exact native polygon draw boundary.</summary>
internal sealed class LegacyNativeCreatureDrawObservationHook : IDisposable
{
    internal const uint RendererAddress = 0x00660D6E;
    internal const string ExpectedPrefixHex = "558BEC518B450C50E8435E000083C4048945FC8B4D0C518B5508528B45FCFF50";
    internal static readonly IReadOnlyList<(uint Address, string Prefix)> Cohort = Array.AsReadOnly(new[]
    {
        (RendererAddress, ExpectedPrefixHex),
        (0x006392BBu, "558BEC81EC1C010000C745F800000000C78504FFFFFF00000000833D38F7CF00"),
        // FFNx1.24.3 uncrop replaces 639252. This observer consumes the final
        // owned pose and clips its triangles; it neither calls nor trusts that culler.
        (0x0064314Fu, "558BEC83EC0C68D8F3CF00E81405020083C40468D8F3CF00E89B05020083C404"),
        (0x0063A60Bu, "558BEC83EC2CC745E400000000C745E800000000C745DC00000000833D38F7CF"),
        (0x006840DAu, "558BEC81EC340200005657837D0C000F841B090000837D14000F841109000083"),
        (0x0066067Au, "558BEC518B45188B4D088988480800008B55188B450C89824C0800008B4D188B"),
        (0x00660BE3u, "558BEC518B451450E8CE5F000083C4048945FC8B4D14518B5510528B450C508B")
    });
    private readonly LegacyNativeCreatureDrawOriginal detour;
    private readonly NativeCreatureDrawObservationIngress ingress;
    private readonly Action<uint, uint> forwardOriginal;
    private IHook<LegacyNativeCreatureDrawOriginal>? hook;
    private LegacyNativeCreatureDrawOriginal? original;
    private int disposed;
    private GCHandle nativeRoot;

    private LegacyNativeCreatureDrawObservationHook(ILegacyAddressSpace memory,
        Action<FieldCreatureApproachSample, DateTime> capture)
    {
        ingress = new NativeCreatureDrawObservationIngress(new FieldCreatureApproachReader(memory), capture);
        detour = OnCallback;
        forwardOriginal = (polygon, owner) => original!(polygon, owner);
        // A failed/retired owner must preserve the callable path while a native callback can still enter.
        nativeRoot = GCHandle.Alloc(this);
    }
    private void Install(IReloadedHooks hooks)
    {
        hook = hooks.CreateHook(detour, RendererAddress, -1);
        original = hook.OriginalFunction;
        hook.Activate();
    }
    internal static bool TryCreate(string executableSha256, ILegacyAddressSpace memory, IReloadedHooks hooks,
        Action<FieldCreatureApproachSample, DateTime> capture, Action<string> log,
        out LegacyNativeCreatureDrawObservationHook? result, out string diagnostic)
    {
        result = null; LegacyNativeCreatureDrawObservationHook? candidate = null;
        try
        {
            if (IntPtr.Size != 4 || !TryValidateIdentity(executableSha256, memory)
                || !Cohort.All(entry => IsMainExecutableCode(entry.Address, entry.Prefix.Length / 2)))
                throw new InvalidOperationException("The exact x86 creature-renderer cohort is unavailable.");
            ArgumentNullException.ThrowIfNull(hooks); ArgumentNullException.ThrowIfNull(capture);
            candidate = new LegacyNativeCreatureDrawObservationHook(memory, capture);
            candidate.Install(hooks); result = candidate;
            diagnostic = "Installed exact native creature draw observation callback.";
            SafeLog(log, diagnostic); return true;
        }
        catch (Exception ex)
        {
            candidate?.Dispose(); result = candidate;
            diagnostic = "Native creature callback is not ready: " + ex.Message;
            SafeLog(log, diagnostic); return false;
        }
    }
    internal static bool TryValidateIdentity(string executableSha256, ILegacyAddressSpace memory)
    {
        if (!string.Equals(executableSha256, LegacyNativeMoviePlaybackReader.StockSha256, StringComparison.Ordinal)
            || memory is null) return false;
        try
        {
            Span<byte> first = stackalloc byte[32]; Span<byte> second = stackalloc byte[32];
            foreach (var entry in Cohort)
            {
                var expected = Convert.FromHexString(entry.Prefix);
                if (expected.Length != first.Length) return false;
                if (!memory.TryRead(entry.Address, first) || !memory.TryRead(entry.Address, second)
                    || !first.SequenceEqual(second) || !first.SequenceEqual(expected)) return false;
            }
            return true;
        }
        catch { return false; }
    }
    private static bool IsMainExecutableCode(uint address, int length)
    {
        if (VirtualQuery((nint)address, out var region, (nuint)Marshal.SizeOf<MemoryRegion>()) == 0) return false;
        var start = (ulong)region.BaseAddress; var size = (ulong)region.RegionSize;
        var protection = region.Protection & 0xFF;
        return region.State == 0x1000 && region.Type == 0x1000000 && (ulong)region.AllocationBase == 0x00400000
            && protection is 0x10 or 0x20 or 0x40 or 0x80 && (region.Protection & 0x100) == 0
            && start <= address && size >= (ulong)length && address - start <= size - (ulong)length;
    }
    private void OnCallback(uint polygonSet, uint gameObject) => ingress.OnCallback(polygonSet, gameObject, forwardOriginal);
    internal bool HasOriginalFault => ingress.HasOriginalFault;
    internal bool HasCaptureFault => ingress.HasCaptureFault;
    private static void SafeLog(Action<string>? log, string message) { try { log?.Invoke(message); } catch { } }
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
        ingress.Stop(); try { hook?.Disable(); } catch { }
        // Do not free the root, detach the delegate, or discard Original: retired callbacks only forward it.
    }
}
