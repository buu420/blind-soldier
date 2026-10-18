using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ff7.Accessibility.Reloaded;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The snowboard font at 72F9F4 receives its owner in ECX and ends with RET 14h.
/// A real x86 caller checks the production callback's stack balance, then the
/// production Original call forwards all six arguments to a native recording body.
/// This catches the Cdecl declaration which ordinary managed reader tests miss.
/// </summary>
internal static class SnowboardTextHookAbiTests
{
    private const uint Owner = 0x11223344, X = 0x12345678, Y = 0x23456789, Color = 0xFFABCDEF;
    private const uint FontEntry = 0x0072F9F4, FontEnd = 0x0072FDFF;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeProbe();

    public static void Run()
    {
        Check(!Environment.Is64BitProcess, "the native font ABI probe must run in x86");
        using var text = new NativeBlock([0]);
        var (mod, detour) = CreateProductionCallback();
        using (var caller = new NativeBlock(Caller(Marshal.GetFunctionPointerForDelegate(detour), text.Address)))
        {
            var delta = Marshal.GetDelegateForFunctionPointer<NativeProbe>(caller.Address)();
            Check(delta == 0, $"snowboard callback must clean five native stack arguments; ESP delta was {delta}, expected 0");
        }
        var convention = typeof(MenuTextRendererDelegate).GetCustomAttribute<global::Reloaded.Hooks.Definitions.X86.FunctionAttribute>()!;
        Check(convention.Cleanup.ToString() == "Callee" &&
            convention.SourceRegisters.Select(register => register.ToString()).SequenceEqual(new[] { "ecx" }),
            "the Reloaded controller must use the native ECX owner and callee stack cleanup too");

        using var capture = new NativeBlock(new byte[28]);
        using var body = new NativeBlock(RecordingBody(capture.Address));
        var original = Marshal.GetDelegateForFunctionPointer<MenuTextRendererDelegate>(body.Address);
        BindOriginal(mod, original);
        try
        {
            using var caller = new NativeBlock(Caller(Marshal.GetFunctionPointerForDelegate(detour), text.Address));
            var invoke = Marshal.GetDelegateForFunctionPointer<NativeProbe>(caller.Address);
            for (var i = 1; i <= 32; i++)
            {
                Check(invoke() == 0, "callback and original both preserve the native caller's stack");
                Check(Marshal.ReadInt32(capture.Address) == i, "the original native renderer runs exactly once per call");
                uint[] expected = [Owner, Address(text.Address), X, Y, Color, 0];
                for (var argument = 0; argument < expected.Length; argument++)
                    Check(unchecked((uint)Marshal.ReadInt32(capture.Address, 4 + argument * 4)) == expected[argument],
                        $"native renderer argument {argument} reaches Original unchanged");
            }
        }
        finally
        {
            GC.KeepAlive(original);
            GC.KeepAlive(detour);
            GC.KeepAlive(mod);
        }
        Console.WriteLine("Snowboard x86 callback: balanced native stack, ECX owner and five arguments, Original exactly once across 32 native callback/renderer calls.");
        RunFontSpeechBoundary();
    }

    private static unsafe void RunFontSpeechBoundary()
    {
        var (mod, detour) = CreateProductionCallback();
        var started = DateTime.UtcNow;
        var now = started;
        var speech = new RenderedMenuTextSpeechTracker(TimeSpan.Zero);
        SetField(mod, "config", new Ff7.Accessibility.Core.AccessibilityConfig());
        SetField(mod, "menuTextRenderDiagnostics", new MenuTextRenderDiagnostics(TimeSpan.FromMilliseconds(750), () => now));
        SetField(mod, "saveMenuSpeechTracker", new SaveMenuSpeechTracker(TimeSpan.Zero));
        SetField(mod, "renderedMenuTextSpeechTracker", speech);
        var log = Path.Combine(Path.GetTempPath(), "snowboard-font-" + Guid.NewGuid().ToString("N") + ".log");
        SetField(mod, "logPath", log);
        using var capture = new NativeBlock(new byte[28]);
        using var body = new NativeBlock(RecordingBody(capture.Address));
        var original = Marshal.GetDelegateForFunctionPointer<MenuTextRendererDelegate>(body.Address);
        BindOriginal(mod, original);
        var spoken = new List<string>();
        var calls = 0;
        try
        {
            // Native 72D6C8 draws three rows then the visible > marker. 72E5EC
            // pulses the selected colour across these 16 frames. Colour is a
            // presentation effect, not a new selection or a new spoken message.
            for (var selected = 0; selected < 3; selected++)
            for (var frame = 0; frame < 16; frame++)
            {
                var phase = frame < 8 ? frame : 15 - frame;
                var grey = 0x8F + 16 * phase;
                var pulse = unchecked((int)(0xFF000000U | (uint)grey * 0x010101U));
                string[] rows = ["CONTINUE", "RETRY", "EXIT"];
                for (var row = 0; row < rows.Length; row++)
                    Draw(rows[row], 248, (uint)(192 + 32 * row), row == selected ? pulse : -1);
                Draw(">", 230, (uint)(192 + 32 * selected), pulse);
                var candidate = speech.Poll(DateTime.UtcNow.AddSeconds(1));
                if (candidate is not null) spoken.Add(candidate);
                now = now.AddMilliseconds(1000.0 / 60);
            }
            // Results use ASCII scores. A label or suffix alone cannot identify
            // a complete result or establish which menu item is selected.
            Draw("TOTAL", 0xAA, 0xD6, unchecked((int)0xFF8F8F8F));
            Draw(" 71", 0x13C, 0xD6, unchecked((int)0xFF8F8F8F));
            Draw("/# PTS.", 0x168, 0xD6, unchecked((int)0xFF8F8F8F));
            var resultCandidate = speech.Poll(DateTime.UtcNow.AddSeconds(1));
            if (resultCandidate is not null) spoken.Add(resultCandidate);
            Check(File.Exists(log) && File.ReadAllText(log).Contains("CONTINUE", StringComparison.Ordinal),
                "snowboard font telemetry remains available");
            Check(Marshal.ReadInt32(capture.Address) == calls,
                "every observed font draw still reaches the original native renderer once");
            Check((int)typeof(Mod).GetField("menuTextRendererErrorCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mod)! == 0,
                "the font callback must not conceal managed errors while retaining telemetry and Original");
            Check(spoken.Count == 0,
                $"snowboard font must not produce generic menu speech; observed {spoken.Count} messages, first: {spoken.FirstOrDefault()}");
        }
        finally
        {
            GC.KeepAlive(original);
            GC.KeepAlive(detour);
            GC.KeepAlive(mod);
            if (File.Exists(log)) File.Delete(log);
        }
        Console.WriteLine("Snowboard font speech boundary: pulse menus and results produce no incorrect generic menu speech; telemetry and Original calls preserved, no callback errors.");

        void Draw(string text, uint x, uint y, int colour)
        {
            using var bytes = new NativeBlock(System.Text.Encoding.ASCII.GetBytes(text + '\0'));
            detour((nint)Owner, (byte*)bytes.Address, x, y, colour, 0x1000);
            calls++;
        }
    }

    private static void SetField(Mod mod, string name, object value) =>
        typeof(Mod).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mod, value);

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var path = Path.Combine(root, "ff7_en.exe");
        if (!File.Exists(path)) return;
        var image = File.ReadAllBytes(path);
        Check(Convert.ToHexString(SHA256.HashData(image)) == LegacyNativeMoviePlaybackReader.StockSha256,
            "native function witness uses the exact supported executable");
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var imageBase = checked((uint)pe.PEHeaders.PEHeader!.ImageBase);
        var section = pe.PEHeaders.SectionHeaders.Single(s => FontEntry >= imageBase + s.VirtualAddress &&
            FontEnd <= imageBase + s.VirtualAddress + s.SizeOfRawData);
        var offset = checked(section.PointerToRawData + (int)(FontEntry - imageBase) - section.VirtualAddress);
        var code = image.AsSpan(offset, checked((int)(FontEnd - FontEntry))).ToArray();
        Check(code.AsSpan(0, 12).SequenceEqual(Convert.FromHexString("558BEC81EC94000000894DCC")) &&
            code.AsSpan(code.Length - 3).SequenceEqual(new byte[] { 0xC2, 0x14, 0 }),
            "installed font saves ECX and performs the native RET 14h cleanup");

        // The native renderer explicitly skips its drawing body when context is
        // zero. Its intact instructions can therefore run here without graphics,
        // globals, a game process, or a copy of the licensed code in this repo.
        using var body = new NativeBlock(code);
        using var text = new NativeBlock([0]);
        using var caller = new NativeBlock(Caller(body.Address, text.Address));
        var invoke = Marshal.GetDelegateForFunctionPointer<NativeProbe>(caller.Address);
        Check(invoke() == 0, "unhooked installed native renderer preserves its caller's stack");
        var (mod, detour) = CreateProductionCallback();
        var original = Marshal.GetDelegateForFunctionPointer<MenuTextRendererDelegate>(body.Address);
        BindOriginal(mod, original);
        try
        {
            using var callbackCaller = new NativeBlock(Caller(Marshal.GetFunctionPointerForDelegate(detour), text.Address));
            var invokeCallback = Marshal.GetDelegateForFunctionPointer<NativeProbe>(callbackCaller.Address);
            for (var i = 0; i < 128; i++)
                Check(invokeCallback() == 0, "actual installed native font remains callable through the production callback");
        }
        finally
        {
            GC.KeepAlive(original);
            GC.KeepAlive(detour);
            GC.KeepAlive(mod);
        }
        Console.WriteLine("Snowboard installed x86 font: exact game bytes, unhooked baseline and 128 production-callback/original calls passed without graphics.");
    }

    private static void BindOriginal(Mod mod, MenuTextRendererDelegate original)
    {
        // Reloaded-II supplies IHook from its own host. Substitute only that
        // external controller's Original property; the production callback and
        // the native renderer body both execute, with no simulated argument data.
        var hook = DispatchProxy.Create<IHook<MenuTextRendererDelegate>, OriginalBinding>();
        ((OriginalBinding)(object)hook).Original = original;
        typeof(Mod).GetField("menuTextRendererHook", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mod, hook);
    }

    public class OriginalBinding : DispatchProxy
    {
        internal MenuTextRendererDelegate? Original;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) =>
            method?.Name == "get_OriginalFunction" ? Original : throw new NotSupportedException(method?.Name);
    }

    private static (Mod Mod, MenuTextRendererDelegate Detour) CreateProductionCallback()
    {
        // Avoid launching speech, input hooks or the game. The uninitialised host
        // has no diagnostics consumer; its real finally block still calls Original.
        var mod = (Mod)RuntimeHelpers.GetUninitializedObject(typeof(Mod));
        var method = typeof(Mod).GetMethod("MenuTextRendererDetour", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (mod, (MenuTextRendererDelegate)method.CreateDelegate(typeof(MenuTextRendererDelegate), mod));
    }

    private static byte[] Caller(nint target, nint text)
    {
        List<byte> code = [0x55, 0x8B, 0xEC, 0x53, 0x56, 0x8B, 0xDC]; // frame; save EBX/ESI; EBX=ESP
        code.Add(0xB9); Dword(code, Owner); // ECX is the native owner
        foreach (var value in new uint[] { 0, Color, Y, X, Address(text) }) { code.Add(0x68); Dword(code, value); }
        code.Add(0xB8); Dword(code, Address(target));
        code.AddRange([0xFF, 0xD0, 0x8B, 0xC4, 0x2B, 0xC3, 0x8B, 0xE3, 0x5E, 0x5B, 0x8B, 0xE5, 0x5D, 0xC3]);
        // CALL EAX; return ESP delta; restore even a mismatched callee safely.
        return code.ToArray();
    }

    private static byte[] RecordingBody(nint capture)
    {
        List<byte> code = [0x55, 0x8B, 0xEC, 0x81, 0xEC, 0x94, 0, 0, 0, 0x89, 0x4D, 0xCC, 0xB8];
        Dword(code, Address(capture));
        code.AddRange([0xFF, 0x00, 0x89, 0x48, 0x04]); // increment count, record ECX
        for (byte argument = 0; argument < 5; argument++)
            code.AddRange([0x8B, 0x55, (byte)(8 + argument * 4), 0x89, 0x50, (byte)(8 + argument * 4)]);
        code.AddRange([0x8B, 0xE5, 0x5D, 0xC2, 0x14, 0]);
        return code.ToArray();
    }

    private static void Dword(List<byte> code, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        code.AddRange(bytes.ToArray());
    }
    private static uint Address(nint pointer) => unchecked((uint)pointer.ToInt32());
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Snowboard native font ABI: " + message);
    }

    private sealed class NativeBlock : IDisposable
    {
        internal nint Address { get; }
        internal NativeBlock(byte[] code)
        {
            Address = VirtualAlloc(0, (nuint)code.Length, 0x3000, 0x40);
            if (Address == 0) throw new InvalidOperationException("Native test allocation failed.");
            Marshal.Copy(code, 0, Address, code.Length);
            Check(FlushInstructionCache((nint)(-1), Address, (nuint)code.Length), "test code cache flush");
        }
        public void Dispose() => VirtualFree(Address, 0, 0x8000);
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nuint size, uint type, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(nint address, nuint size, uint type);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushInstructionCache(nint process, nint address, nuint size);
}
