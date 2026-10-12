using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Reloaded.Hooks.Definitions;

internal static class NativeCreditsTitleHookGuardTests
{
    public static void Run(Steam2026FingerprintResult? verifiedFingerprint = null)
    {
        var path = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_STEAM_EXECUTABLE")
            ?? @"D:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\FFVII.exe";
        var fingerprint = verifiedFingerprint ?? Steam2026Fingerprint.Inspect(path);
        foreach (var side in new[] { "low", "high" })
        foreach (var reason in new[] { "prefix", "mapped-host", "non-executable", "foreign-image", "uncommitted" })
        {
            var hooks = DispatchProxy.Create<IReloadedHooks, HookSpy>();
            var spy = (HookSpy)(object)hooks;
            Check(!Steam2026NativeCreditsTitleObservationHook.TryCreate(fingerprint, Memory.ModuleBase,
                Memory.ImageSize, new Memory(side, reason), hooks, out var rejected, out _), "invalid target rejected");
            Check(rejected is null && spy.Calls == 0, "whole cohort validated before first callable");
        }
        var invoke = typeof(Steam2026NativeCreditsTitleRendererOriginal).GetMethod("Invoke")!;
        Check(invoke.ReturnType == typeof(void) && invoke.GetParameters().Length == 0
            && typeof(Steam2026NativeCreditsTitleRendererOriginal)
                .GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention == CallingConvention.Winapi,
            "translated title renderers use exact void zero-argument host ABI");

        foreach (var failSecondActivation in new[] { false, true })
        {
            var hooks = DispatchProxy.Create<IReloadedHooks, HookSpy>();
            var spy = (HookSpy)(object)hooks; spy.FailSecondActivation = failSecondActivation;
            var memory = new Memory("", "");
            var installed = Steam2026NativeCreditsTitleObservationHook.TryCreate(fingerprint, Memory.ModuleBase,
                Memory.ImageSize, memory, hooks, out var owner, out var diagnostic);
            Check(installed != failSecondActivation && owner is not null,
                "retain stopped owner after partial activation failure: " + diagnostic);
            Check(spy.Calls == 2 && spy.Detours.All(d => d.GetType() == typeof(Steam2026NativeCreditsTitleRendererOriginal)),
                "both exact validated targets request typed title renderers");
            foreach (var detour in spy.Detours) detour.DynamicInvoke();
            Check(spy.OriginalCalls == 2, "each detour forwards its own native original exactly once");
            owner!.Dispose(); owner.Dispose();
            Check(spy.FakeHooks.All(h => h.Disables == 1), "dispose/failed activation disables each created hook once");
            owner.TryTakeLatest(out _);
            var reads = memory.Reads;
            foreach (var detour in spy.Detours) detour.DynamicInvoke();
            Check(spy.OriginalCalls == 4 && memory.Reads == reads, "retired callbacks retain originals without guest reads");
            Check(!owner.TryTakeLatest(out _), "retired callbacks never republish title evidence");
        }
        Console.WriteLine("Title hook cohort guards, exact typed original forwarding and late cleanup passed.");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    public class HookSpy : DispatchProxy
    {
        public int Calls;
        public int OriginalCalls;
        public bool FailSecondActivation;
        public readonly List<Delegate> Detours = [];
        public readonly List<FakeHook> FakeHooks = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Check(method!.Name == "CreateHook", "only hook creation expected"); Calls++;
            Check(method.GetGenericArguments().Single() == typeof(Steam2026NativeCreditsTitleRendererOriginal), "typed native callable");
            Detours.Add((Delegate)args![0]!);
            var fake = new FakeHook { Original = () => OriginalCalls++ };
            fake.FailActivation = FailSecondActivation && Calls == 2;
            FakeHooks.Add(fake); return fake;
        }
    }
    public sealed class FakeHook : IHook<Steam2026NativeCreditsTitleRendererOriginal>
    {
        public Steam2026NativeCreditsTitleRendererOriginal Original = null!;
        public bool FailActivation;
        public int Disables;
        public Steam2026NativeCreditsTitleRendererOriginal OriginalFunction => Original;
        public IReverseWrapper<Steam2026NativeCreditsTitleRendererOriginal> ReverseWrapper => throw new NotSupportedException();
        public bool IsHookEnabled { get; private set; }
        public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 0;
        public nint OriginalFunctionWrapperAddress => 0;
        public IHook<Steam2026NativeCreditsTitleRendererOriginal> Activate()
        {
            if (FailActivation) throw new InvalidOperationException("offline activation failure");
            IsHookActivated = true; IsHookEnabled = true; return this;
        }
        IHook IHook.Activate() => Activate();
        public void Disable() { Disables++; IsHookEnabled = false; }
        public void Enable() => IsHookEnabled = true;
    }
    private sealed class Memory : INativeMemoryReader
    {
        public const ulong ModuleBase = 0x140000000;
        public const ulong ImageSize = 0x0212A000;
        private readonly Dictionary<ulong, byte> bytes = [];
        private readonly ulong failedHost;
        private readonly string failure;
        public int Reads;
        public Memory(string side, string failure)
        {
            this.failure = failure;
            failedHost = ModuleBase + (side == "low" ? Steam2026NativeCreditsTitleObservationHook.LowResolutionRenderer.HostRva
                : Steam2026NativeCreditsTitleObservationHook.HighResolutionRenderer.HostRva);
            Write(ModuleBase + TranslatedX86AddressSpace.ResolverRva,
                Convert.FromHexString("85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3"));
            foreach (var map in new[] { Steam2026NativeCreditsTitleObservationHook.LowResolutionRenderer,
                         Steam2026NativeCreditsTitleObservationHook.HighResolutionRenderer })
            {
                var host = ModuleBase + map.HostRva;
                Write(ModuleBase + map.MappingRecordRva, BitConverter.GetBytes((ulong)map.LegacyVirtualAddress));
                Write(ModuleBase + map.MappingRecordRva + 8,
                    BitConverter.GetBytes(host + (host == failedHost && failure == "mapped-host" ? 1UL : 0UL)));
                Write(host, Convert.FromHexString(map.ExpectedPrefixHex));
                if (host == failedHost && failure == "prefix") Write(host, [0x90]);
            }
        }
        private void Write(ulong address, byte[] data)
        {
            for (ulong i = 0; i < (ulong)data.Length; i++) bytes[address + i] = data[i];
        }
        public bool TryReadUInt64(ulong address, out ulong value)
        {
            Span<byte> raw = stackalloc byte[8]; value = 0;
            if (!TryRead(address, raw)) return false;
            value = BinaryPrimitives.ReadUInt64LittleEndian(raw); return true;
        }
        public bool TryRead(ulong address, Span<byte> target)
        {
            Reads++;
            for (ulong i = 0; i < (ulong)target.Length; i++)
            {
                if (!bytes.TryGetValue(address + i, out var value)) return false;
                target[(int)i] = value;
            }
            return true;
        }
        public bool TryQueryRegion(ulong address, out NativeMemoryRegion region)
        {
            var selected = address == failedHost;
            region = new(ModuleBase, ImageSize,
                selected && failure == "foreign-image" ? ModuleBase + 0x1000 : ModuleBase,
                !(selected && failure == "uncommitted"), !(selected && failure == "non-executable"), true, true);
            return true;
        }
    }
}
