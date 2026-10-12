#if BLIND_SOLDIER_LEGACY_TESTS
using System.Reflection;
using System.Runtime.InteropServices;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.LegacyLayout;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class LegacyNativeCreditsTitleHookGuardTests
{
    public static void Run()
    {
        var stock = LegacyNativeMoviePlaybackReader.StockSha256;
        Check(LegacyNativeCreditsTitleObservationHook.TryValidateIdentity(stock, new Memory(0)), "exact renderer cohort identity");
        Check(!LegacyNativeCreditsTitleObservationHook.TryValidateIdentity(new string('0', 64), new Memory(0)), "wrong stock executable rejected");
        foreach (var address in new[] { LegacyNativeCreditsTitleObservationHook.LowResolutionRendererAddress,
                     LegacyNativeCreditsTitleObservationHook.HighResolutionRendererAddress })
            Check(!LegacyNativeCreditsTitleObservationHook.TryValidateIdentity(stock, new Memory(address)), "either altered cohort entry rejected");
        var abi = typeof(LegacyNativeCreditsTitleRendererOriginal);
        Check(abi.GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention == CallingConvention.Cdecl
            && abi.GetCustomAttribute<global::Reloaded.Hooks.Definitions.X86.FunctionAttribute>() is not null
            && abi.GetMethod("Invoke")!.ReturnType == typeof(void)
            && abi.GetMethod("Invoke")!.GetParameters().Length == 0, "exact Reloaded x86 Cdecl void ABI");
        var hooks = DispatchProxy.Create<IReloadedHooks, NoCalls>();
        Check(!LegacyNativeCreditsTitleObservationHook.TryCreate(stock, new Memory(0), hooks, out _, out _),
            "test process cannot authorize native game callables");
        Check(((NoCalls)(object)hooks).Calls == 0, "non-game native allocation rejects before any hook construction");
        Console.WriteLine("Legacy title renderer cohort identity/ABI and native callable rejection passed.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public class NoCalls : DispatchProxy
    {
        public int Calls;
        protected override object? Invoke(MethodInfo? method, object?[]? args) { Calls++; throw new NotSupportedException(); }
    }
    private sealed class Memory(uint altered) : ILegacyAddressSpace
    {
        public bool TryRead(uint address, Span<byte> target)
        {
            var prefix = address switch
            {
                LegacyNativeCreditsTitleObservationHook.LowResolutionRendererAddress => LegacyNativeCreditsTitleObservationHook.LowResolutionPrefixHex,
                LegacyNativeCreditsTitleObservationHook.HighResolutionRendererAddress => LegacyNativeCreditsTitleObservationHook.HighResolutionPrefixHex,
                _ => null
            };
            if (prefix is null) return false;
            var bytes = Convert.FromHexString(prefix); if (address == altered) bytes[0] = 0x90;
            bytes.CopyTo(target); return true;
        }
    }
}
#endif
