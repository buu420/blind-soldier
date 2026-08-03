using System.Buffers.Binary;

namespace Ff7.Accessibility.Steam2026X64;

/// <summary>
/// Reads the emulated x86 call state used by translated <c>void(void)</c> wrappers.
/// Every operation reads current memory; translated host page pointers are never cached.
/// </summary>
internal sealed class TranslatedX86CallFrameReader
{
    public const ulong EaxRva = 0x00000000020395B8;
    public const ulong EspRva = 0x00000000020395C8;
    public const ulong EbpRva = 0x00000000020395CC;

    private readonly INativeMemoryReader memory;
    private readonly TranslatedX86AddressSpace addressSpace;
    private readonly ulong eaxAddress;
    private readonly ulong espAddress;
    private readonly ulong ebpAddress;

    public TranslatedX86CallFrameReader(
        ulong moduleBase,
        INativeMemoryReader memory,
        TranslatedX86AddressSpace addressSpace)
    {
        if (moduleBase == 0 || moduleBase > ulong.MaxValue - EbpRva)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
        }

        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        eaxAddress = moduleBase + EaxRva;
        espAddress = moduleBase + EspRva;
        ebpAddress = moduleBase + EbpRva;
    }

    public bool TryReadEax(out uint value)
    {
        return TryReadRegister(eaxAddress, out value);
    }

    public bool TryReadEsp(out uint value)
    {
        return TryReadRegister(espAddress, out value);
    }

    public bool TryReadEbp(out uint value)
    {
        return TryReadRegister(ebpAddress, out value);
    }

    public bool TryReadArgument(int argumentIndex, out uint value)
    {
        value = 0;
        if (argumentIndex < 0 || !TryReadEsp(out var guestEsp) || guestEsp == 0)
        {
            return false;
        }

        return TryReadArgumentAtEsp(guestEsp, argumentIndex, out value);
    }

    internal bool TryReadArgumentAtEsp(
        uint guestEsp,
        int argumentIndex,
        out uint value)
    {
        value = 0;
        if (guestEsp == 0 || argumentIndex < 0)
        {
            return false;
        }

        var guestAddress = (ulong)guestEsp
                           + sizeof(uint)
                           + ((ulong)(uint)argumentIndex * sizeof(uint));
        if (guestAddress > uint.MaxValue)
        {
            return false;
        }

        return addressSpace.TryReadUInt32((uint)guestAddress, out value);
    }

    public bool TryReadArgumentLowByte(int argumentIndex, out byte value)
    {
        var success = TryReadArgument(argumentIndex, out var raw);
        value = success ? unchecked((byte)raw) : (byte)0;
        return success;
    }

    public bool TryReadArgumentSignedLow16(int argumentIndex, out short value)
    {
        var success = TryReadArgument(argumentIndex, out var raw);
        value = success ? unchecked((short)raw) : (short)0;
        return success;
    }

    public bool TryReadPostCallEax(out uint value)
    {
        return TryReadEax(out value);
    }

    private bool TryReadRegister(ulong address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        var success = memory.TryRead(address, bytes);
        value = success ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : 0;
        return success;
    }
}
