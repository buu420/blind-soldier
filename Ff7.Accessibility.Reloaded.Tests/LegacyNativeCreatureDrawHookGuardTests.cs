#if BLIND_SOLDIER_LEGACY_TESTS
using System.Reflection;
using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class LegacyNativeCreatureDrawHookGuardTests
{
    public static void Run()
    {
        var stock = LegacyNativeMoviePlaybackReader.StockSha256;
        Check(LegacyNativeCreatureDrawObservationHook.TryValidateIdentity(stock, new Memory(0)), "exact full native renderer cohort");
        // FFNx1.24.3 replaces this culler whenever uncrop is enabled. This is
        // the exact five-byte E9 patch format from its pinned patch.cpp:93-105,
        // with an illustrative relocated FFNx destination of 10001000.
        var uncrop = new Memory(0) { FfnxUncrop = true };
        Span<byte> culler = stackalloc byte[32];
        Check(uncrop.TryRead(0x00639252, culler) && culler[0] == 0xE9 &&
              unchecked(0x00639257u + (uint)System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(culler[1..])) == 0x10001000,
              "literal FFNx relative-jump culler fixture");
        Check(LegacyNativeCreatureDrawObservationHook.TryValidateIdentity(stock, uncrop), "FFNx uncrop culler is not required by the final polygon observer");
        Check(!LegacyNativeCreatureDrawObservationHook.TryValidateIdentity(new string('0', 64), new Memory(0)), "wrong executable rejected");
        foreach (var entry in LegacyNativeCreatureDrawObservationHook.Cohort)
        {
            Check(!LegacyNativeCreatureDrawObservationHook.TryValidateIdentity(stock, new Memory(entry.Address)), "each altered cohort entry rejected");
            Check(!LegacyNativeCreatureDrawObservationHook.TryValidateIdentity(stock, new Memory(entry.Address) { FfnxUncrop = true }), "FFNx culler does not excuse a changed required draw or pose entry");
        }
        Check(!LegacyNativeCreatureDrawObservationHook.TryValidateIdentity(stock, new Memory(0) { ThrowReads = true }), "read fault rejects identity");
        Check(!LegacyNativeCreatureDrawObservationHook.TryValidateIdentity(stock, new Memory(0) { TearReads = true }), "torn prefix rejects identity");
        var type = typeof(LegacyNativeCreatureDrawOriginal); var invoke = type.GetMethod("Invoke")!;
        Check(invoke.ReturnType == typeof(void) && invoke.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(uint), typeof(uint) })
            && type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention == CallingConvention.Cdecl
            && type.GetCustomAttribute<global::Reloaded.Hooks.Definitions.X86.FunctionAttribute>() is not null,
            "legacy draw uses exact typed Cdecl two-uint ABI");
        var hooks = DispatchProxy.Create<IReloadedHooks, NoCalls>(); var captures = 0;
        Check(!LegacyNativeCreatureDrawObservationHook.TryCreate(stock, new Memory(0), hooks,
            (sample, utc) => captures++, _ => { }, out var owner, out _), "non-game executable allocation rejects native callables");
        Check(owner is null && ((NoCalls)(object)hooks).Calls == 0 && captures == 0, "validation completes before native hook creation");
        Console.WriteLine("Legacy creature hook: exact required draw/pose cohort, FFNx uncrop, ABI, unreadable/torn identity and native callable rejection passed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public class NoCalls : DispatchProxy
    {
        public int Calls;
        protected override object? Invoke(MethodInfo? method, object?[]? args) { Calls++; throw new NotSupportedException(); }
    }
    private sealed class Memory(uint altered) : ILegacyAddressSpace
    {
        public bool ThrowReads; public bool TearReads; public bool FfnxUncrop; private int reads;
        public bool TryRead(uint address, Span<byte> target)
        {
            if (ThrowReads) throw new InvalidOperationException("offline read error");
            if (address == 0x00639252 && FfnxUncrop)
            {
                Convert.FromHexString("E9A97D9C0F05E415CC0085C07407B801000000EB520FBF4D0C8B1508F2CF0083").CopyTo(target);
                return true;
            }
            var entry = LegacyNativeCreatureDrawObservationHook.Cohort.FirstOrDefault(e => e.Address == address);
            if (entry.Prefix is null) return false;
            var raw = Convert.FromHexString(entry.Prefix); if (address == altered || TearReads && ++reads % 2 == 0) raw[0] ^= 0xFF;
            raw.CopyTo(target); return true;
        }
    }
}
#endif
