using System.Buffers.Binary;

namespace Ff7.Accessibility.Steam2026X64;

/// <summary>
/// Immutable evidence needed to identify one translated x86 function in the
/// Steam 2026 x64 module.
/// </summary>
internal readonly record struct TranslatedFunctionMapDefinition(
    uint LegacyVirtualAddress,
    ulong MappingRecordRva,
    ulong HostRva,
    string ExpectedPrefixHex);

/// <summary>
/// Revalidates a translated-function map record and relocated host prefix.
/// It identifies code only; it does not construct or invoke hooks.
/// </summary>
internal sealed class TranslatedFunctionMapValidator
{
    public const int MappingRecordSize = 0x10;

    private readonly ulong moduleBase;
    private readonly ulong moduleImageEndExclusive;
    private readonly INativeMemoryReader memory;

    public TranslatedFunctionMapValidator(
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory)
    {
        if (moduleBase == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
        }

        if (moduleImageSize == 0
            || moduleBase > ulong.MaxValue - moduleImageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleImageSize));
        }

        this.moduleBase = moduleBase;
        moduleImageEndExclusive = moduleBase + moduleImageSize;
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public bool TryValidate(
        TranslatedFunctionMapDefinition definition,
        out ulong relocatedHostAddress)
    {
        relocatedHostAddress = 0;
        if (!TryDecodePrefix(definition.ExpectedPrefixHex, out var expectedPrefix)
            || !TryValidateMappedTarget(
                definition,
                (ulong)expectedPrefix.Length,
                out var expectedHostAddress))
        {
            return false;
        }

        Span<byte> firstActualPrefix = expectedPrefix.Length <= 256
            ? stackalloc byte[expectedPrefix.Length]
            : new byte[expectedPrefix.Length];
        Span<byte> secondActualPrefix = expectedPrefix.Length <= 256
            ? stackalloc byte[expectedPrefix.Length]
            : new byte[expectedPrefix.Length];
        if (!memory.TryRead(expectedHostAddress, firstActualPrefix)
            || !memory.TryRead(expectedHostAddress, secondActualPrefix)
            || !firstActualPrefix.SequenceEqual(secondActualPrefix)
            || !firstActualPrefix.SequenceEqual(expectedPrefix))
        {
            return false;
        }

        relocatedHostAddress = expectedHostAddress;
        return true;
    }

    /// <summary>
    /// Revalidates the immutable translated mapping and executable image
    /// ownership without requiring the pristine entry prefix. This is only
    /// suitable while an owning inline-hook lease is active, because Reloaded
    /// replaces that prefix with its detour jump.
    /// </summary>
    internal bool TryValidateMappedTarget(
        TranslatedFunctionMapDefinition definition,
        out ulong relocatedHostAddress) =>
        TryValidateMappedTarget(definition, 1, out relocatedHostAddress);

    private bool TryValidateMappedTarget(
        TranslatedFunctionMapDefinition definition,
        ulong requiredHostLength,
        out ulong relocatedHostAddress)
    {
        relocatedHostAddress = 0;
        if (definition.LegacyVirtualAddress == 0
            || definition.MappingRecordRva == 0
            || definition.HostRva == 0
            || requiredHostLength == 0
            || !TryAdd(moduleBase, definition.MappingRecordRva, out var mappingRecordAddress)
            || !IsInsideMainImage(mappingRecordAddress, MappingRecordSize)
            || !TryAdd(moduleBase, definition.HostRva, out var expectedHostAddress)
            || !IsInsideMainImage(expectedHostAddress, requiredHostLength))
        {
            return false;
        }

        Span<byte> firstMappingRecord = stackalloc byte[MappingRecordSize];
        Span<byte> secondMappingRecord = stackalloc byte[MappingRecordSize];
        if (!memory.TryRead(mappingRecordAddress, firstMappingRecord))
        {
            return false;
        }

        var legacyAddressQword = BinaryPrimitives.ReadUInt64LittleEndian(firstMappingRecord);
        var relocatedAddressQword = BinaryPrimitives.ReadUInt64LittleEndian(
            firstMappingRecord[sizeof(ulong)..]);
        if (legacyAddressQword != definition.LegacyVirtualAddress
            || relocatedAddressQword != expectedHostAddress
            || !memory.TryQueryRegion(expectedHostAddress, out var firstRegion)
            || !IsCommittedExecutableImageRange(
                firstRegion,
                expectedHostAddress,
                requiredHostLength)
            || !memory.TryRead(mappingRecordAddress, secondMappingRecord)
            || !memory.TryQueryRegion(expectedHostAddress, out var secondRegion)
            || firstRegion != secondRegion
            || !IsCommittedExecutableImageRange(
                secondRegion,
                expectedHostAddress,
                requiredHostLength)
            || !firstMappingRecord.SequenceEqual(secondMappingRecord))
        {
            return false;
        }

        relocatedHostAddress = expectedHostAddress;
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

    private bool IsCommittedExecutableImageRange(
        NativeMemoryRegion region,
        ulong address,
        ulong length)
    {
        if (!region.IsCommitted
            || !region.IsExecutable
            || region.AllocationBase != moduleBase
            || region.Size == 0
            || address < region.BaseAddress
            || region.BaseAddress > ulong.MaxValue - (region.Size - 1)
            || address > ulong.MaxValue - (length - 1))
        {
            return false;
        }

        return address + length - 1 <= region.BaseAddress + region.Size - 1;
    }

    private static bool TryDecodePrefix(string? prefixHex, out byte[] prefix)
    {
        prefix = [];
        if (string.IsNullOrWhiteSpace(prefixHex)
            || (prefixHex.Length & 1) != 0)
        {
            return false;
        }

        try
        {
            prefix = Convert.FromHexString(prefixHex);
            return prefix.Length > 0;
        }
        catch (FormatException)
        {
            prefix = [];
            return false;
        }
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
