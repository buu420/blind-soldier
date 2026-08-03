using System.Buffers.Binary;

namespace Ff7.Accessibility.LegacyLayout;

public static class LegacyAddressSpaceExtensions
{
    public static bool TryReadByte(
        this ILegacyAddressSpace addressSpace,
        uint virtualAddress,
        out byte value)
    {
        ArgumentNullException.ThrowIfNull(addressSpace);
        Span<byte> buffer = stackalloc byte[1];
        var success = addressSpace.TryRead(virtualAddress, buffer);
        value = success ? buffer[0] : (byte)0;
        return success;
    }

    public static bool TryReadInt16(
        this ILegacyAddressSpace addressSpace,
        uint virtualAddress,
        out short value)
    {
        ArgumentNullException.ThrowIfNull(addressSpace);
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        var success = addressSpace.TryRead(virtualAddress, buffer);
        value = success ? BinaryPrimitives.ReadInt16LittleEndian(buffer) : (short)0;
        return success;
    }

    public static bool TryReadUInt16(
        this ILegacyAddressSpace addressSpace,
        uint virtualAddress,
        out ushort value)
    {
        ArgumentNullException.ThrowIfNull(addressSpace);
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        var success = addressSpace.TryRead(virtualAddress, buffer);
        value = success ? BinaryPrimitives.ReadUInt16LittleEndian(buffer) : (ushort)0;
        return success;
    }

    public static bool TryReadInt32(
        this ILegacyAddressSpace addressSpace,
        uint virtualAddress,
        out int value)
    {
        ArgumentNullException.ThrowIfNull(addressSpace);
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        var success = addressSpace.TryRead(virtualAddress, buffer);
        value = success ? BinaryPrimitives.ReadInt32LittleEndian(buffer) : 0;
        return success;
    }

    public static bool TryReadUInt32(
        this ILegacyAddressSpace addressSpace,
        uint virtualAddress,
        out uint value)
    {
        ArgumentNullException.ThrowIfNull(addressSpace);
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        var success = addressSpace.TryRead(virtualAddress, buffer);
        value = success ? BinaryPrimitives.ReadUInt32LittleEndian(buffer) : 0;
        return success;
    }

    public static bool TryReadSingle(
        this ILegacyAddressSpace addressSpace,
        uint virtualAddress,
        out float value)
    {
        var success = addressSpace.TryReadInt32(virtualAddress, out var bits);
        value = success ? BitConverter.Int32BitsToSingle(bits) : 0.0f;
        return success;
    }
}
