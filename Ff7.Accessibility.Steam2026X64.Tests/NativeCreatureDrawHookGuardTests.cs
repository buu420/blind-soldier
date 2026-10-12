using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Reloaded.Hooks.Definitions;

internal static class NativeCreatureDrawHookGuardTests
{
    public static void Run(Steam2026FingerprintResult? verifiedFingerprint = null)
    {
        var path = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_STEAM_EXECUTABLE")
            ?? @"D:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\FFVII.exe";
        var fingerprint = verifiedFingerprint ?? Steam2026Fingerprint.Inspect(path);
        foreach (var entry in Steam2026NativeCreatureDrawObservationHook.Cohort)
        foreach (var reason in new[] { "prefix", "mapped-host", "non-executable", "foreign-image", "uncommitted" })
        {
            var hooks = DispatchProxy.Create<IReloadedHooks, HookSpy>(); var spy = (HookSpy)(object)hooks;
            Check(!Steam2026NativeCreatureDrawObservationHook.TryCreate(fingerprint, Memory.ModuleBase, Memory.ImageSize,
                new Memory(entry.HostRva, reason), hooks, (_, _) => { }, _ => { }, out var owner, out _), "reject invalid native cohort");
            Check(owner is null && spy.Calls == 0, "whole native cohort is validated before first callable");
        }
        var type = typeof(Steam2026NativeCreatureDrawOriginal); var invoke = type.GetMethod("Invoke")!;
        Check(invoke.ReturnType == typeof(void) && invoke.GetParameters().Length == 0
            && type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention == CallingConvention.Winapi,
            "translated polygon renderer has physical void(void) ABI");
        var shortHooks = DispatchProxy.Create<IReloadedHooks, HookSpy>();
        Check(!Steam2026NativeCreatureDrawObservationHook.TryCreate(fingerprint, Memory.ModuleBase,
            Steam2026NativeCreatureDrawObservationHook.GuestEspRva, new Memory(0, ""), shortHooks,
            (_, _) => { }, _ => { }, out var shortOwner, out _)
            && shortOwner is null && ((HookSpy)(object)shortHooks).Calls == 0,
            "module lacking the guest argument root cannot bind a callable");
        foreach (var activationFails in new[] { false, true })
        {
            var hooks = DispatchProxy.Create<IReloadedHooks, HookSpy>(); var spy = (HookSpy)(object)hooks;
            spy.FailActivation = activationFails; var memory = new Memory(0, ""); var publishes = 0;
            var installed = Steam2026NativeCreatureDrawObservationHook.TryCreate(fingerprint, Memory.ModuleBase, Memory.ImageSize,
                memory, hooks, (_, _) => publishes++, _ => { }, out var owner, out var diagnostic);
            Check(installed != activationFails && owner is not null, "failed activation retains stopped callable owner: " + diagnostic);
            Check(spy.Calls == 1 && spy.Detour is not null && spy.Target == Memory.ModuleBase + Steam2026NativeCreatureDrawObservationHook.Renderer.HostRva,
                "typed hook binds only the exact polygon wrapper");
            spy.Detour!(); Check(spy.OriginalCalls == 1 && publishes == 0, "unreadable geometry forwards Original once");
            owner!.Dispose(); owner.Dispose(); var reads = memory.Reads;
            spy.Detour(); Check(spy.OriginalCalls == 2 && memory.Reads == reads && publishes == 0, "retired callback retains Original without guest reads");
            Check(spy.Hook!.Disables == 1, "retirement/failed activation disables exactly once");
            var root = (GCHandle)owner.GetType().GetField("nativeRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
            Check(root.IsAllocated, "process-lifetime owner survives native callable retirement");
        }
        var faultHooks = DispatchProxy.Create<IReloadedHooks, HookSpy>(); var faultSpy = (HookSpy)(object)faultHooks;
        faultSpy.ThrowOriginal = true;
        Check(Steam2026NativeCreatureDrawObservationHook.TryCreate(fingerprint, Memory.ModuleBase, Memory.ImageSize,
            new Memory(0, ""), faultHooks, (_, _) => { }, _ => { }, out var faultOwner, out _), "offline fault fixture installs");
        faultSpy.Detour!(); Check(faultSpy.OriginalCalls == 1 && faultOwner!.HasOriginalFault, "Original exception is recorded without retry");
        faultOwner!.Dispose();

        var native = new Memory(0, ""); var guest = new GuestMemory(); native.SetEsp(0x6000); guest.Set(0x6004, 0x30000); guest.Set(0x6008, 0x40000);
        Check(Steam2026NativeCreatureDrawObservationHook.TryCaptureGuestArguments(Memory.ModuleBase, Memory.ImageSize, native, guest, out var polygon, out var gameObject)
            && polygon == 0x30000 && gameObject == 0x40000, "pre-Original guest ESP+4/+8 supplies the two native arguments");
        native.SetEsp(uint.MaxValue - 7);
        Check(!Steam2026NativeCreatureDrawObservationHook.TryCaptureGuestArguments(Memory.ModuleBase, Memory.ImageSize, native, guest, out _, out _), "guest stack overflow rejected");
        native.SetEsp(0x6000); guest.AfterRead = () => native.SetEsp(0x7000);
        Check(!Steam2026NativeCreatureDrawObservationHook.TryCaptureGuestArguments(Memory.ModuleBase, Memory.ImageSize, native, guest, out _, out _), "changed guest ESP defeats argument capture");
        guest.AfterRead = null; native.SetEsp(0x6000); native.GuestStackForeignRegion = true;
        Check(!Steam2026NativeCreatureDrawObservationHook.TryCaptureGuestArguments(Memory.ModuleBase, Memory.ImageSize, native, guest, out _, out _), "foreign host register region rejected");
        native.GuestStackForeignRegion = false;
        Check(!Steam2026NativeCreatureDrawObservationHook.TryCaptureGuestArguments(Memory.ModuleBase, Steam2026NativeCreatureDrawObservationHook.GuestEspRva, native, guest, out _, out _), "small module excludes guest register root");
        Check(!Steam2026NativeCreatureDrawObservationHook.TryCaptureGuestArguments(ulong.MaxValue - 1, Memory.ImageSize, native, guest, out _, out _), "module overflow rejected");
        Console.WriteLine("Steam creature hook: full cohort, void host ABI, exact Original, retirement and guest argument guards passed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public class HookSpy : DispatchProxy
    {
        public int Calls, OriginalCalls; public bool FailActivation, ThrowOriginal;
        public ulong Target; public Steam2026NativeCreatureDrawOriginal? Detour; public FakeHook? Hook;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Check(method!.Name == "CreateHook" && method.GetGenericArguments().Single() == typeof(Steam2026NativeCreatureDrawOriginal), "only exact typed hook construction");
            Calls++; Detour = (Steam2026NativeCreatureDrawOriginal)args![0]!; Target = (ulong)(long)args[1]!;
            Hook = new FakeHook { FailActivation = FailActivation, Original = () => { OriginalCalls++; if (ThrowOriginal) throw new InvalidOperationException(); } };
            return Hook;
        }
    }
    public sealed class FakeHook : IHook<Steam2026NativeCreatureDrawOriginal>
    {
        public Steam2026NativeCreatureDrawOriginal Original = null!; public bool FailActivation; public int Disables;
        public Steam2026NativeCreatureDrawOriginal OriginalFunction => Original;
        public IReverseWrapper<Steam2026NativeCreatureDrawOriginal> ReverseWrapper => throw new NotSupportedException();
        public bool IsHookEnabled { get; private set; } public bool IsHookActivated { get; private set; }
        public nint OriginalFunctionAddress => 0; public nint OriginalFunctionWrapperAddress => 0;
        public IHook<Steam2026NativeCreatureDrawOriginal> Activate()
        {
            if (FailActivation) throw new InvalidOperationException("offline activation error"); IsHookEnabled = IsHookActivated = true; return this;
        }
        IHook IHook.Activate() => Activate();
        public void Disable() { Disables++; IsHookEnabled = false; } public void Enable() => IsHookEnabled = true;
    }
    private sealed class GuestMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = []; public Action? AfterRead;
        public void Set(uint address, uint value)
        {
            var raw = BitConverter.GetBytes(value); for (uint i = 0; i < 4; i++) bytes[address + i] = raw[(int)i];
        }
        public bool TryRead(uint address, Span<byte> target)
        {
            for (uint i = 0; i < target.Length; i++) { if (!bytes.TryGetValue(address + i, out var b)) return false; target[(int)i] = b; }
            AfterRead?.Invoke(); return true;
        }
    }
    private sealed class Memory : INativeMemoryReader
    {
        public const ulong ModuleBase = 0x140000000, ImageSize = 0x0212A000;
        private readonly Dictionary<ulong, byte> bytes = []; private readonly ulong failedRva; private readonly string failure;
        public int Reads; public bool GuestStackForeignRegion;
        public Memory(ulong failedRva, string failure)
        {
            this.failedRva = failedRva; this.failure = failure;
            Write(ModuleBase + TranslatedX86AddressSpace.ResolverRva,
                Convert.FromHexString("85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3"));
            foreach (var entry in Steam2026NativeCreatureDrawObservationHook.Cohort)
            {
                var host = ModuleBase + entry.HostRva;
                Write(ModuleBase + entry.MappingRecordRva, BitConverter.GetBytes((ulong)entry.LegacyVirtualAddress));
                Write(ModuleBase + entry.MappingRecordRva + 8, BitConverter.GetBytes(host + (entry.HostRva == failedRva && failure == "mapped-host" ? 1UL : 0UL)));
                Write(host, Convert.FromHexString(entry.ExpectedPrefixHex)); if (entry.HostRva == failedRva && failure == "prefix") Write(host, [0x90]);
            }
            SetEsp(0);
        }
        public void SetEsp(uint value) => Write(ModuleBase + Steam2026NativeCreatureDrawObservationHook.GuestEspRva, BitConverter.GetBytes(value));
        private void Write(ulong address, byte[] raw) { for (ulong i = 0; i < (ulong)raw.Length; i++) bytes[address + i] = raw[(int)i]; }
        public bool TryReadUInt64(ulong address, out ulong value) { Span<byte> raw = stackalloc byte[8]; value = 0; if (!TryRead(address, raw)) return false; value = BinaryPrimitives.ReadUInt64LittleEndian(raw); return true; }
        public bool TryRead(ulong address, Span<byte> target)
        {
            Reads++; for (ulong i = 0; i < (ulong)target.Length; i++) { if (!bytes.TryGetValue(address + i, out var b)) return false; target[(int)i] = b; } return true;
        }
        public bool TryQueryRegion(ulong address, out NativeMemoryRegion region)
        {
            var selected = address == ModuleBase + failedRva;
            var foreign = selected && failure == "foreign-image" || GuestStackForeignRegion && address == ModuleBase + Steam2026NativeCreatureDrawObservationHook.GuestEspRva;
            region = new(ModuleBase, ImageSize, foreign ? ModuleBase + 0x1000 : ModuleBase,
                !(selected && failure == "uncommitted"), !(selected && failure == "non-executable"), true, true);
            return true;
        }
    }
}
