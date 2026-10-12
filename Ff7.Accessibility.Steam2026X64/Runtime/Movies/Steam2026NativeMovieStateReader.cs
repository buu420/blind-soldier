using System.Buffers.Binary;
using System.Text;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Movies;

/// <summary>
/// Copies the two native movie values required by the exact callback ingress.
/// Every value is sampled twice and rejected if its backing region or bytes
/// change during the copy.
/// </summary>
internal sealed class Steam2026NativeMovieStateReader
{
    // The exact FrameGetter signature is MOV EAX,[RIP+00B0A61E]; RET.
    // This is the decoder counter, not FIELD's last copy of that counter.
    internal const ulong NativeMovieFrameRva = 0x0207D014;
    internal const ulong MovieObjectPointerRva = 0x0207CF08;
    internal const ulong CanonicalMoviePathRva = 0x0207CF10;
    internal const ulong StartedStateOffset = 0x01FC;
    internal const int CanonicalMoviePathCapacity = 0x104;

    private readonly ulong moduleBase;
    private readonly ulong moduleImageEndExclusive;
    private readonly INativeMemoryReader memory;
    private readonly NativeMovieCallbackContract callbackContract;

    internal Steam2026NativeMovieStateReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (!fingerprint.IsSupported
            || !fingerprint.Identity.Is64Bit
            || !string.Equals(
                fingerprint.Identity.RuntimeId,
                Steam2026Fingerprint.SupportedRuntimeId,
                StringComparison.Ordinal)
            || !string.Equals(
                fingerprint.Identity.Sha256,
                Steam2026Fingerprint.SupportedSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The native movie state reader requires the exact supported Steam 2026 x64 fingerprint.",
                nameof(fingerprint));
        }

        if (moduleBase == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
        }

        if (moduleImageSize == 0 || moduleBase > ulong.MaxValue - moduleImageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleImageSize));
        }

        this.moduleBase = moduleBase;
        moduleImageEndExclusive = moduleBase + moduleImageSize;
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        callbackContract = new NativeMovieCallbackContract(fingerprint, moduleBase, moduleImageSize, memory);

        if (!TryAdd(moduleBase, MovieObjectPointerRva, out var pointerAddress)
            || !IsInsideMainImage(pointerAddress, sizeof(ulong))
            || !TryAdd(moduleBase, CanonicalMoviePathRva, out var pathAddress)
            || !IsInsideMainImage(pathAddress, CanonicalMoviePathCapacity))
        {
            throw new InvalidDataException(
                "The native movie state globals are outside the supported main image.");
        }
    }

    internal bool TryReadCanonicalPath(out string path)
    {
        path = string.Empty;
        if (!TryAdd(moduleBase, CanonicalMoviePathRva, out var address)
            || !memory.TryQueryRegion(address, out var firstRegion)
            || !IsCommittedReadableImageRange(
                firstRegion,
                address,
                CanonicalMoviePathCapacity))
        {
            return false;
        }

        Span<byte> first = stackalloc byte[CanonicalMoviePathCapacity];
        Span<byte> second = stackalloc byte[CanonicalMoviePathCapacity];
        if (!memory.TryRead(address, first)
            || !memory.TryRead(address, second)
            || !memory.TryQueryRegion(address, out var secondRegion)
            || firstRegion != secondRegion
            || !IsCommittedReadableImageRange(
                secondRegion,
                address,
                CanonicalMoviePathCapacity)
            || !first.SequenceEqual(second))
        {
            return false;
        }

        var terminator = first.IndexOf((byte)0);
        if (terminator <= 0)
        {
            return false;
        }

        path = Encoding.Latin1.GetString(first[..terminator]);
        return path.Length > 0;
    }

    internal bool TryReadStartState(out int state)
    {
        state = 0;
        if (!TryAdd(moduleBase, MovieObjectPointerRva, out var pointerAddress)
            || !memory.TryQueryRegion(pointerAddress, out var pointerRegion)
            || !IsCommittedReadableImageRange(pointerRegion, pointerAddress, sizeof(ulong))
            || !memory.TryReadUInt64(pointerAddress, out var firstObject)
            || firstObject == 0
            || !TryAdd(firstObject, StartedStateOffset, out var stateAddress)
            || !memory.TryQueryRegion(stateAddress, out var firstStateRegion)
            || !IsCommittedReadableRange(firstStateRegion, stateAddress, sizeof(int)))
        {
            return false;
        }

        Span<byte> firstState = stackalloc byte[sizeof(int)];
        Span<byte> secondState = stackalloc byte[sizeof(int)];
        if (!memory.TryRead(stateAddress, firstState)
            || !memory.TryReadUInt64(pointerAddress, out var secondObject)
            || firstObject != secondObject
            || !memory.TryRead(stateAddress, secondState)
            || !memory.TryQueryRegion(pointerAddress, out var secondPointerRegion)
            || pointerRegion != secondPointerRegion
            || !IsCommittedReadableImageRange(
                secondPointerRegion,
                pointerAddress,
                sizeof(ulong))
            || !memory.TryQueryRegion(stateAddress, out var secondStateRegion)
            || firstStateRegion != secondStateRegion
            || !IsCommittedReadableRange(secondStateRegion, stateAddress, sizeof(int))
            || !firstState.SequenceEqual(secondState))
        {
            return false;
        }

        state = BinaryPrimitives.ReadInt32LittleEndian(firstState);
        return true;
    }

    internal bool TryReadEndgamePlayback(Func<int?> readModule, string workingDirectory,
        string languageDirectoryName, out Ff7.Accessibility.Reloaded.NativeMoviePlaybackSample sample)
    {
        sample = default;
        ArgumentNullException.ThrowIfNull(readModule);
        var module = readModule();
        if (module is null) return false;
        if (module is not 25 and not 28)
        {
            if (readModule() != module) return false;
            sample = new(string.Empty, module.Value, 0, false, false);
            return true;
        }

        if (!callbackContract.TryValidateIdentity(NativeMovieCallbackKind.FrameGetter, out _)
            || !memory.TryReadUInt64(moduleBase + MovieObjectPointerRva, out var firstObject)
            || !TryReadStartState(out var started) || started is < 0 or > 1)
            return false;
        if (started == 0)
        {
            if (readModule() != module || !TryReadStartState(out var after) || after != 0) return false;
            sample = new(string.Empty, module.Value, 0, false, false);
            return true;
        }

        if (!TryReadCanonicalPath(out var path)
            || !TryResolveEndgamePath(path, module.Value, workingDirectory, languageDirectoryName, out var film)
            || !TryReadDecoderFrame(out var frame)
            || !TryReadDecoderFrame(out var secondFrame)
            || secondFrame < frame || secondFrame - frame > 2
            || !TryReadCanonicalPath(out var secondPath) || secondPath != path
            || !TryReadStartState(out var secondStarted) || secondStarted != started
            || !memory.TryReadUInt64(moduleBase + MovieObjectPointerRva, out var secondObject)
            || firstObject != secondObject || readModule() != module
            || !callbackContract.TryValidateIdentity(NativeMovieCallbackKind.FrameGetter, out _))
            return false;

        // Both shipped ending films were measured at 15/1 fps. FIELD's movie
        // command and counter are deliberately not consulted in modules25/28.
        sample = new(film, module.Value, secondFrame, true, false);
        return true;
    }

    private bool TryReadDecoderFrame(out uint frame)
    {
        frame = 0;
        var address = moduleBase + NativeMovieFrameRva;
        if (!IsInsideMainImage(address, sizeof(uint))
            || !memory.TryQueryRegion(address, out var region)
            || !IsCommittedReadableImageRange(region, address, sizeof(uint))) return false;
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        if (!memory.TryRead(address, bytes)) return false;
        frame = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryResolveEndgamePath(string path, int module, string workingDirectory,
        string languageDirectoryName, out string film)
    {
        film = string.Empty;
        if (!Path.IsPathFullyQualified(workingDirectory)
            || !Ff7.Accessibility.Reloaded.Ff7GameLanguages.All.Any(
                language => language.LanguageDirectoryName == languageDirectoryName)) return false;
        var name = module == 25 ? "ending2.avi" : "ending3.avi";
        var relative = module == 25 ? $"data/{languageDirectoryName}/movies/{name}" : $"data/movies/{name}";
        var physical = Path.Combine(Path.GetFullPath(workingDirectory), relative.Replace('/', Path.DirectorySeparatorChar));
        if (!string.Equals(path, "0://" + relative, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, physical, StringComparison.OrdinalIgnoreCase)) return false;
        film = name;
        return true;
    }

    private bool IsInsideMainImage(ulong address, ulong length)
    {
        if (length == 0
            || address < moduleBase
            || address > ulong.MaxValue - (length - 1))
        {
            return false;
        }

        return address + length - 1 < moduleImageEndExclusive;
    }

    private bool IsCommittedReadableImageRange(
        NativeMemoryRegion region,
        ulong address,
        ulong length) =>
        region.IsImage
        && region.AllocationBase == moduleBase
        && IsCommittedReadableRange(region, address, length);

    private static bool IsCommittedReadableRange(
        NativeMemoryRegion region,
        ulong address,
        ulong length)
    {
        if (!region.IsCommitted
            || !region.IsReadable
            || region.Size == 0
            || length == 0
            || address < region.BaseAddress
            || region.BaseAddress > ulong.MaxValue - (region.Size - 1)
            || address > ulong.MaxValue - (length - 1))
        {
            return false;
        }

        return address + length - 1 <= region.BaseAddress + region.Size - 1;
    }

    private static bool TryAdd(ulong left, ulong right, out ulong sum)
    {
        if (left > ulong.MaxValue - right)
        {
            sum = 0;
            return false;
        }

        sum = left + right;
        return true;
    }
}
