using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

internal readonly record struct LegacyNativeMoviePlaybackIdentity(
    string ExecutableSha256, uint FfnxModuleBase, string FfnxModuleSha256);

/// <summary>
/// Ending2/ending3 only. The host calls this on the normal game tick and supplies
/// the current game getter at 00418613, including FFNx's existing replacement.
/// No hook, playback command, decoder-pointer traversal or state write is made.
/// Stock IMediaSeeking failure may perform the game's own COM cleanup; the
/// stock getter is therefore not promised to be side-effect-free on failure.
/// </summary>
internal sealed class LegacyNativeMoviePlaybackReader
{
    internal const string StockSha256 = "4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225";
    internal const string FfnxSha256 = "7D7EC5997A4FE5C8F203D8ADF55E90C4663D0B30F9004426659AA7E38386397A";
    internal const uint ModuleAddress = 0x00CBF9DC;
    internal const uint GetterAddress = 0x00418613;
    internal const uint MovieObjectAddress = 0x009A0E14;
    internal const uint StartedAddress = MovieObjectAddress + 0x1FC;
    internal const uint CompletedAddress = MovieObjectAddress + 0x200;
    internal const uint PreparedAddress = MovieObjectAddress + 0x204;
    internal const uint StockSampleAddress = MovieObjectAddress + 0x94;
    internal const uint StockSeekingAddress = MovieObjectAddress + 0x1EC;
    internal const uint Ending2StageAddress = 0x009A06C8;
    internal const uint MovieDirectoryPointerAddress = 0x009A069C;
    internal const uint Ending2NameAddress = 0x007BAD10;
    internal const uint CreditsModeAddress = 0x00F4F408;
    internal const uint CreditsStageAddress = 0x00F4F414;
    internal const uint CreditsStartedAddress = 0x00F4F440;
    internal const uint CreditsCompletedAddress = 0x00F4F444;
    internal const uint CreditsPathAddress = 0x00F4F490;
    // Matching installed FFNx PDB and DLL, SHA above. movie_object is +0x20 in
    // ff7_externals. ff7_get_movie_frame tail-jumps to the raw frame getter.
    internal const uint FfnxGetterRva = 0x0042B700;
    internal const uint FfnxFrameGetterRva = 0x00438990;
    internal const uint FfnxMovieObjectPointerRva = 0x0210D9E0;
    internal const uint FfnxFpsRva = 0x0231AE70;
    internal const uint UpdateSampleAddress = 0x00417F39;
    internal const uint FfnxUpdateSampleRva = 0x0042BBE0;
    private static ReadOnlySpan<byte> StockGetterPrefix =>
        Convert.FromHexString("558BEC83EC0CC745F800000000C745FC00000000C745F400000000833D10109A");
    private readonly ILegacyAddressSpace memory;
    private readonly Func<uint> currentGetter;
    private readonly Func<uint, int, bool> executableRange;
    private readonly LegacyNativeMoviePlaybackIdentity identity;
    private NativeMoviePlaybackSample? previous;
    private bool observedStopped;
    private long generation;
    internal string LastDiagnostic { get; private set; } = "No native ending sample observed.";

    internal LegacyNativeMoviePlaybackReader(ILegacyAddressSpace memory, Func<uint> currentGetter,
        LegacyNativeMoviePlaybackIdentity identity, Func<uint, int, bool> executableRange)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.currentGetter = currentGetter ?? throw new ArgumentNullException(nameof(currentGetter));
        this.executableRange = executableRange ?? throw new ArgumentNullException(nameof(executableRange));
        if (!IsSupportedExecutable(identity.ExecutableSha256) ||
            (identity.FfnxModuleBase != 0 && (!string.Equals(identity.FfnxModuleSha256, FfnxSha256, StringComparison.Ordinal) ||
                (ulong)identity.FfnxModuleBase + FfnxFpsRva + sizeof(double) > uint.MaxValue)))
            throw new ArgumentException("Native movie reader requires the exact verified legacy/FFNx fingerprint.", nameof(identity));
        this.identity = identity;
    }

    internal static bool TryCreate(Process process, ILegacyAddressSpace memory, Func<uint> currentGetter,
        out LegacyNativeMoviePlaybackReader reader, out string diagnostic)
    {
        reader = null!;
        diagnostic = "Legacy native movie identity is unavailable.";
        if (Environment.Is64BitProcess) return false;
        try
        {
            var main = process.MainModule;
            if (main is null || unchecked((ulong)(nuint)main.BaseAddress) != 0x00400000)
                return false;
            var hash = Hash(main.FileName);
            if (!IsSupportedExecutable(hash)) { diagnostic = $"Unsupported native movie executable SHA-256 {hash}."; return false; }
            uint ffnxBase = 0;
            string ffnxHash = string.Empty;
            foreach (ProcessModule module in process.Modules)
            {
                if (!FfnxRuntimeDetector.IsFfnxModule(module.ModuleName, FileVersionInfo.GetVersionInfo(module.FileName).ProductName))
                    continue;
                ffnxHash = Hash(module.FileName);
                if (!string.Equals(ffnxHash, FfnxSha256, StringComparison.Ordinal))
                { diagnostic = $"Unsupported native movie FFNx SHA-256 {ffnxHash}."; return false; }
                var address = unchecked((ulong)(nuint)module.BaseAddress);
                if (address == 0 || address + FfnxFpsRva + sizeof(double) > uint.MaxValue) return false;
                ffnxBase = (uint)address;
                break;
            }
            var candidate = new LegacyNativeMoviePlaybackReader(memory, currentGetter,
                new(hash, ffnxBase, ffnxHash), IsCurrentExecutableRange);
            if (!candidate.TryValidateCallable(out diagnostic)) return false;
            reader = candidate;
            diagnostic = ffnxBase == 0 ? "Verified native stock ending frame getter." : "Verified exact FFNx ending frame getter and decoder fps.";
            return true;
        }
        catch (Exception ex) { diagnostic = $"Native movie identity check failed: {ex.Message}"; return false; }
    }

    internal bool TryRead(out NativeMoviePlaybackSample sample)
    {
        sample = default;
        if (!TryCapture(out var first) || !TryCapture(out var before) || first != before)
            return Fail("Native ending module, path or flags are unreadable/torn.");
        if (!before.Playing)
        {
            observedStopped = true;
            return Fail("No verified ending film is playing in the current native stage.");
        }
        if (!TryValidateCallable(out var failure) || !TryValidateCallable(out failure)) return Fail(failure);
        // The stock getter dereferences IMediaSeeking when sample is non-null.
        // Verify those prerequisites before delegating to the game's own call.
        if (identity.FfnxModuleBase == 0 && !TryValidateStockSeeking())
            return Fail("Stock ending getter's sample/IMediaSeeking/vtable target is unavailable.");
        uint frame;
        try { frame = currentGetter(); }
        catch (Exception ex) { return Fail($"Native ending frame getter refused: {ex.Message}"); }
        if (!TryCapture(out var after) || before != after || !TryValidateCallable(out _))
            return Fail("Native ending state changed during its frame getter.");
        if (previous is not { } last || observedStopped || last.FilmFileName != after.Film || last.Module != after.Module || frame < last.Frame)
            generation++;
        observedStopped = false;
        sample = new(after.Film, after.Module, frame, true, false, generation, after.Fps);
        previous = sample;
        LastDiagnostic = $"Verified native ending: module={after.Module}, film={after.Film}, frame={frame}, fps={after.Fps}, generation={generation}.";
        return true;
    }

    internal bool TryResolveObservationTarget(out long address, out string diagnostic)
    {
        address = 0;
        diagnostic = "Native movie update callable identity is unavailable.";
        if (!TryValidateCallable(out diagnostic) || !executableRange(UpdateSampleAddress, 32)) return false;
        Span<byte> code = stackalloc byte[32];
        if (!memory.TryRead(UpdateSampleAddress, code)) return false;
        if (identity.FfnxModuleBase == 0)
        {
            if (!code.SequenceEqual(Convert.FromHexString("558BEC81EC040100005657C745FC01000000C745F000000000C70514109A0000"))) return false;
            address = UpdateSampleAddress;
        }
        else
        {
            var target = identity.FfnxModuleBase + FfnxUpdateSampleRva;
            if (code[0] != 0xE9 || unchecked(UpdateSampleAddress + 5 + (uint)BinaryPrimitives.ReadInt32LittleEndian(code[1..5])) != target ||
                !executableRange(target, 32) || !memory.TryRead(target, code)) return false;
            var expected = Convert.FromHexString("A1E0D91012C7800002000000000000A1E0D9101283B8FC01000000750333C0C3");
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(1, 4), identity.FfnxModuleBase + FfnxMovieObjectPointerRva);
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(16, 4), identity.FfnxModuleBase + FfnxMovieObjectPointerRva);
            if (!code.SequenceEqual(expected)) return false;
            address = target;
        }
        diagnostic = $"Verified native cdecl movie update observer target 0x{address:X8}.";
        return true;
    }

    /// <summary>
    /// Worker-safe validity check for a cached game-thread frame. Copies native
    /// flags/identity only; it never enters IMediaSeeking or the native getter.
    /// </summary>
    internal bool TryIsCurrentPlayback(NativeMoviePlaybackSample expected, out bool playing)
    {
        playing = false;
        if (!TryCapture(out var first) || !TryCapture(out var second) || first != second ||
            !TryValidateCallable(out _) || !TryValidateCallable(out _)) return false;
        playing = second.Playing && expected.IsPlaying && expected.Module == second.Module &&
            string.Equals(expected.FilmFileName, second.Film, StringComparison.Ordinal) &&
            expected.FramesPerSecond == second.Fps;
        return true;
    }

    private bool TryCapture(out Capture state)
    {
        state = default;
        if (!memory.TryReadUInt16(ModuleAddress, out var module)) return false;
        if (module is not 25 and not 28) { state = new(module, false, string.Empty, 0, 0, 0, 0, 0, 0, string.Empty, 15); return true; }
        if (!memory.TryReadUInt32(StartedAddress, out var started) || !memory.TryReadUInt32(CompletedAddress, out var completed) ||
            !memory.TryReadUInt32(PreparedAddress, out var prepared) || started > 1 || completed > 1 || prepared > 1)
            return false;
        var playing = started == 1 && prepared == 1 && completed == 0;
        uint mode = 0, stage = 0, stageStarted = 0, stageCompleted = 0, objectPointer = MovieObjectAddress;
        var path = string.Empty;
        var film = string.Empty;
        var fps = 15d;
        if (module == 25)
        {
            if (!memory.TryReadUInt32(Ending2StageAddress, out stage) || stage > 1) return false;
            playing &= stage == 1;
            if (playing)
            {
                if (!TryMovieDirectory(out var directory) || !TryText(Ending2NameAddress, 12, out var name) || name != "ending2.avi") return false;
                path = directory + name;
                film = name;
            }
        }
        else
        {
            if (!memory.TryReadUInt32(CreditsModeAddress, out mode) || !memory.TryReadUInt32(CreditsStageAddress, out stage) ||
                !memory.TryReadUInt32(CreditsStartedAddress, out stageStarted) || !memory.TryReadUInt32(CreditsCompletedAddress, out stageCompleted) ||
                mode > 1 || stageStarted > 1 || stageCompleted > 1) return false;
            playing &= mode == 1 && stage == 3 && stageStarted == 1 && stageCompleted == 0;
            if (playing)
            {
                if (!TryMovieDirectory(out var directory) || !TryText(CreditsPathAddress, 260, out path) ||
                    !string.Equals(path, directory + "ending3.avi", StringComparison.OrdinalIgnoreCase)) return false;
                film = "ending3.avi";
            }
        }
        if (playing && identity.FfnxModuleBase != 0)
        {
            if (!memory.TryReadUInt32(identity.FfnxModuleBase + FfnxMovieObjectPointerRva, out objectPointer) || objectPointer != MovieObjectAddress)
                return false;
            Span<byte> fpsBytes = stackalloc byte[sizeof(double)];
            if (!memory.TryRead(identity.FfnxModuleBase + FfnxFpsRva, fpsBytes)) return false;
            fps = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(fpsBytes));
            if (!double.IsFinite(fps) || fps <= 0) return false;
        }
        state = new(module, playing, film, started, completed, prepared, mode, stage,
            ((ulong)stageStarted << 32) | stageCompleted, path, fps, objectPointer);
        return true;
    }

    private bool TryMovieDirectory(out string directory)
    {
        directory = string.Empty;
        if (!memory.TryReadUInt32(MovieDirectoryPointerAddress, out var pointer) || pointer < 0x10000 ||
            !TryText(pointer, 260, out directory)) return false;
        // The native call appends its literal directly. A missing separator cannot
        // be repaired into a guessed location by the accessibility reader.
        return directory.EndsWith('\\') || directory.EndsWith('/');
    }

    private bool TryText(uint address, int capacity, out string text)
    {
        text = string.Empty;
        Span<byte> bytes = stackalloc byte[capacity];
        for (var i = 0; i < capacity; i++)
        {
            if ((ulong)address + (uint)i > uint.MaxValue || !memory.TryReadByte(address + (uint)i, out var value)) return false;
            if (value == 0) { text = Encoding.Latin1.GetString(bytes[..i]); return i > 0; }
            if (value < 32) return false;
            bytes[i] = value;
        }
        return false;
    }

    private bool TryValidateCallable(out string diagnostic)
    {
        diagnostic = "Native ending getter identity/readability changed.";
        if (!executableRange(GetterAddress, 32)) return false;
        Span<byte> code = stackalloc byte[32];
        if (!memory.TryRead(GetterAddress, code)) return false;
        if (identity.FfnxModuleBase == 0) return code.SequenceEqual(StockGetterPrefix);
        var target = identity.FfnxModuleBase + FfnxGetterRva;
        if (code[0] != 0xE9 || unchecked(GetterAddress + 5 + (uint)BinaryPrimitives.ReadInt32LittleEndian(code[1..5])) != target ||
            !executableRange(target, 22)) return false;
        Span<byte> replacement = stackalloc byte[22];
        if (!memory.TryRead(target, replacement) || replacement[0] != 0xA1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(replacement[1..5]) != identity.FfnxModuleBase + FfnxMovieObjectPointerRva ||
            !replacement[5..18].SequenceEqual(Convert.FromHexString("83B8FC01000000750333C0C3E9"))) return false;
        var tail = unchecked(target + 22 + (uint)BinaryPrimitives.ReadInt32LittleEndian(replacement[18..22]));
        if (tail != identity.FfnxModuleBase + FfnxFrameGetterRva || !executableRange(tail, 6)) return false;
        Span<byte> frameGetter = stackalloc byte[6];
        return memory.TryRead(tail, frameGetter) && frameGetter[0] == 0xA1 && frameGetter[5] == 0xC3 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frameGetter[1..5]) == identity.FfnxModuleBase + 0x0231AE60;
    }

    private bool TryValidateStockSeeking() =>
        memory.TryReadUInt32(StockSampleAddress, out var sample) && sample >= 0x10000 &&
        memory.TryReadUInt32(StockSeekingAddress, out var seeking) && seeking >= 0x10000 &&
        memory.TryReadUInt32(seeking, out var vtable) && vtable >= 0x10000 && vtable <= uint.MaxValue - 0x30 &&
        memory.TryReadUInt32(vtable + 0x30, out var getter) && getter >= 0x10000 && executableRange(getter, 1);

    private bool Fail(string diagnostic) { LastDiagnostic = diagnostic; return false; }
    private static bool IsSupportedExecutable(string hash) => hash is StockSha256 or
        "C1437392C5E4178765FBD238DCC9B33D86D2B97337310131C874F302236E4B6F" or
        "68CF1B8C1D732CC00A1DDB02CED161F7C94B06680D9E8641A11C7361417375C2";
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private readonly record struct Capture(int Module, bool Playing, string Film, uint Started, uint Completed,
        uint Prepared, uint Mode, uint Stage, ulong StageFlags, string Path, double Fps, uint Object = MovieObjectAddress);

    private static bool IsCurrentExecutableRange(uint address, int length)
    {
        if (address == 0 || length <= 0 || (ulong)address + (uint)length > uint.MaxValue + 1UL) return false;
        if (VirtualQuery((nint)(nuint)address, out var region, (nuint)Marshal.SizeOf<MemoryRegion>()) == 0 ||
            region.State != 0x1000 || (region.Protect & 0x101) != 0 || (region.Protect & 0xF0) == 0) return false;
        var start = (ulong)(nuint)region.Base;
        return address >= start && (ulong)address + (uint)length <= start + (ulong)region.Size;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion
    {
        internal nint Base, AllocationBase;
        internal uint AllocationProtect;
        internal nuint Size;
        internal uint State, Protect, Type;
    }
    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(nint address, out MemoryRegion information, nuint length);
}
