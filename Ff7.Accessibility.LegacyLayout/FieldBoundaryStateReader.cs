using System.Collections.Immutable;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly struct FieldBoundaryState : IEquatable<FieldBoundaryState>
{
    private readonly ImmutableArray<byte> bits;

    public FieldBoundaryState(IEnumerable<byte> bits, int triangleCount)
    {
        ArgumentNullException.ThrowIfNull(bits);
        this.bits = ImmutableArray.CreateRange(bits);
        TriangleCount = triangleCount;
    }

    public ImmutableArray<byte> Bits => bits.IsDefault ? ImmutableArray<byte>.Empty : bits;

    public int TriangleCount { get; }

    public bool IsBoundaryEnabled(int triangleIndex)
    {
        if (triangleIndex < 0 || triangleIndex >= TriangleCount)
        {
            return false;
        }

        var byteIndex = triangleIndex >> 3;
        var bitMask = 1 << (triangleIndex & 7);
        var snapshot = Bits;
        return byteIndex < snapshot.Length && (snapshot[byteIndex] & bitMask) != 0;
    }

    public IReadOnlyList<int> ActiveBoundaryTriangles
    {
        get
        {
            var active = new List<int>();
            for (var triangleIndex = 0; triangleIndex < TriangleCount; triangleIndex++)
            {
                if (IsBoundaryEnabled(triangleIndex))
                {
                    active.Add(triangleIndex);
                }
            }

            return active;
        }
    }

    public bool Equals(FieldBoundaryState other) =>
        TriangleCount == other.TriangleCount &&
        Bits.AsSpan().SequenceEqual(other.Bits.AsSpan());

    public override bool Equals(object? obj) =>
        obj is FieldBoundaryState other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TriangleCount);
        foreach (var value in Bits)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(FieldBoundaryState left, FieldBoundaryState right) => left.Equals(right);

    public static bool operator !=(FieldBoundaryState left, FieldBoundaryState right) => !left.Equals(right);
}

public readonly record struct FieldBoundaryStateReadResult(
    bool IsUsable,
    FieldBoundaryState State,
    string Diagnostic)
{
    public static FieldBoundaryStateReadResult Valid(FieldBoundaryState state, string diagnostic) =>
        new(true, state, diagnostic);

    public static FieldBoundaryStateReadResult Invalid(string diagnostic) =>
        new(false, default, diagnostic);
}

public sealed class FieldBoundaryStateReader
{
    public const int AddressFieldGlobalObjectPtr = 0x00CBF9D8;
    public const int BoundaryBitsOffset = 0xB2;
    public const int BoundaryByteCount = 64;
    public const int MaximumTriangleCount = BoundaryByteCount * 8;

    private readonly Func<int, int>? readInt32;
    private readonly Func<int, byte>? readByte;
    private readonly Func<int, int, bool>? isReadableMemory;
    private readonly ILegacyAddressSpace? addressSpace;

    public FieldBoundaryStateReader(
        Func<int, int> readInt32,
        Func<int, byte> readByte,
        Func<int, int, bool> isReadableMemory)
    {
        this.readInt32 = readInt32 ?? throw new ArgumentNullException(nameof(readInt32));
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
        this.isReadableMemory = isReadableMemory ?? throw new ArgumentNullException(nameof(isReadableMemory));
    }

    public FieldBoundaryStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public FieldBoundaryStateReadResult Read(FieldPositionSnapshot position, int triangleCount) =>
        addressSpace is null ? ReadLegacy(position, triangleCount) : ReadChecked(position, triangleCount);

    private FieldBoundaryStateReadResult ReadLegacy(FieldPositionSnapshot position, int triangleCount)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return FieldBoundaryStateReadResult.Invalid($"field={position.FieldId}, not in field module");
        }

        if (triangleCount <= 0 || triangleCount > MaximumTriangleCount)
        {
            return FieldBoundaryStateReadResult.Invalid(
                $"field={position.FieldId}, unsupported triangle count {triangleCount}");
        }

        if (position.FieldId is < 0 or > ushort.MaxValue)
        {
            return FieldBoundaryStateReadResult.Invalid($"field={position.FieldId}, invalid field id");
        }

        try
        {
            var byteCount = (triangleCount + 7) / 8;
            if (!TryReadLegacyFrame(position, byteCount, out var candidate, out var diagnostic))
            {
                return FieldBoundaryStateReadResult.Invalid(diagnostic);
            }

            if (!TryReadLegacyFrame(position, byteCount, out var confirmation, out _) ||
                !candidate.Matches(confirmation))
            {
                return FieldBoundaryStateReadResult.Invalid(
                    $"field={position.FieldId}, IDLCK state changed during read");
            }

            return CreateValidResult(position, triangleCount, candidate);
        }
        catch (Exception ex)
        {
            return FieldBoundaryStateReadResult.Invalid(
                $"field={position.FieldId}, IDLCK read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private FieldBoundaryStateReadResult ReadChecked(FieldPositionSnapshot position, int triangleCount)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return FieldBoundaryStateReadResult.Invalid($"field={position.FieldId}, not in field module");
        }

        if (position.FieldId is < 0 or > ushort.MaxValue)
        {
            return FieldBoundaryStateReadResult.Invalid($"field={position.FieldId}, invalid field id");
        }

        if (triangleCount <= 0 || triangleCount > MaximumTriangleCount)
        {
            return FieldBoundaryStateReadResult.Invalid(
                $"field={position.FieldId}, unsupported triangle count {triangleCount}");
        }

        var byteCount = (triangleCount + 7) / 8;
        if (!TryReadCheckedFrame(position, byteCount, out var candidate, out var diagnostic))
        {
            return FieldBoundaryStateReadResult.Invalid(diagnostic);
        }

        if (!TryReadCheckedFrame(position, byteCount, out var confirmation, out var confirmationDiagnostic))
        {
            return FieldBoundaryStateReadResult.Invalid(confirmationDiagnostic);
        }

        if (!candidate.Matches(confirmation))
        {
            return FieldBoundaryStateReadResult.Invalid($"field={position.FieldId}, IDLCK state changed during read");
        }

        return CreateValidResult(position, triangleCount, candidate);
    }

    private bool TryReadLegacyFrame(
        FieldPositionSnapshot position,
        int byteCount,
        out FieldBoundaryFrame frame,
        out string diagnostic)
    {
        frame = default;
        var module = readByte!(FieldPositionReader.AddressCurrentModule);
        if (!TryReadLegacyUInt16(FieldPositionReader.AddressFieldId, out var fieldId))
        {
            diagnostic = $"field={position.FieldId}, field position address overflowed";
            return false;
        }

        var fieldGlobalObject = unchecked((uint)readInt32!(AddressFieldGlobalObjectPtr));
        if (!TryValidateFrameHeader(position, module, fieldId, fieldGlobalObject, out diagnostic) ||
            !TryCalculateBitsRange(fieldGlobalObject, byteCount, out var bitsAddress))
        {
            if (string.IsNullOrEmpty(diagnostic))
            {
                diagnostic = $"field={position.FieldId}, IDLCK address overflowed";
            }

            return false;
        }

        if (!isReadableMemory!(unchecked((int)bitsAddress), byteCount))
        {
            diagnostic = $"field={position.FieldId}, IDLCK state at 0x{bitsAddress:X8} is unreadable";
            return false;
        }

        var bits = new byte[byteCount];
        for (var index = 0; index < byteCount; index++)
        {
            if (!TryAdd(bitsAddress, index, out var byteAddress))
            {
                diagnostic = $"field={position.FieldId}, IDLCK address overflowed";
                return false;
            }

            bits[index] = readByte!(unchecked((int)byteAddress));
        }

        frame = new FieldBoundaryFrame(module, fieldId, fieldGlobalObject, bitsAddress, bits);
        diagnostic = string.Empty;
        return true;
    }

    private bool TryReadCheckedFrame(
        FieldPositionSnapshot position,
        int byteCount,
        out FieldBoundaryFrame frame,
        out string diagnostic)
    {
        frame = default;
        diagnostic = $"field={position.FieldId}, field position is unavailable";
        var checkedAddressSpace = addressSpace!;
        if (!checkedAddressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !checkedAddressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId))
        {
            return false;
        }

        if (!checkedAddressSpace.TryReadUInt32((uint)AddressFieldGlobalObjectPtr, out var fieldGlobalObject))
        {
            diagnostic = $"field={position.FieldId}, field global object read failed";
            return false;
        }

        if (!TryValidateFrameHeader(position, module, fieldId, fieldGlobalObject, out diagnostic))
        {
            return false;
        }

        if (!TryCalculateBitsRange(fieldGlobalObject, byteCount, out var bitsAddress))
        {
            diagnostic = $"field={position.FieldId}, IDLCK address overflowed";
            return false;
        }

        var bits = new byte[byteCount];
        if (!checkedAddressSpace.TryRead(bitsAddress, bits))
        {
            diagnostic = $"field={position.FieldId}, IDLCK state at 0x{bitsAddress:X8} is unreadable";
            return false;
        }

        frame = new FieldBoundaryFrame(module, fieldId, fieldGlobalObject, bitsAddress, bits);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryValidateFrameHeader(
        FieldPositionSnapshot position,
        byte module,
        ushort fieldId,
        uint fieldGlobalObject,
        out string diagnostic)
    {
        diagnostic = string.Empty;
        if (module != position.CurrentModule || fieldId != position.FieldId)
        {
            diagnostic = $"field={position.FieldId}, field position is unavailable";
            return false;
        }

        if (fieldGlobalObject == 0)
        {
            diagnostic = $"field={position.FieldId}, field global object is null";
            return false;
        }

        return true;
    }

    private static bool TryCalculateBitsRange(uint fieldGlobalObject, int byteCount, out uint bitsAddress)
    {
        bitsAddress = 0;
        return byteCount > 0 &&
            TryAdd(fieldGlobalObject, BoundaryBitsOffset, out bitsAddress) &&
            TryAdd(bitsAddress, byteCount - 1, out _);
    }

    private bool TryReadLegacyUInt16(int address, out ushort value)
    {
        value = 0;
        if (!TryAdd((uint)address, 1, out var highAddress))
        {
            return false;
        }

        var low = readByte!(address);
        var high = readByte!(unchecked((int)highAddress));
        value = (ushort)(low | (high << 8));
        return true;
    }

    private static bool TryAdd(uint address, int offset, out uint result)
    {
        result = 0;
        if (offset < 0)
        {
            return false;
        }

        try
        {
            result = checked(address + (uint)offset);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static FieldBoundaryStateReadResult CreateValidResult(
        FieldPositionSnapshot position,
        int triangleCount,
        FieldBoundaryFrame frame)
    {
        var state = new FieldBoundaryState(frame.Bits, triangleCount);
        var activeBoundaries = state.ActiveBoundaryTriangles;
        return FieldBoundaryStateReadResult.Valid(
            state,
            $"field={position.FieldId}, IDLCK=0x{frame.BitsAddress:X8}, triangles={triangleCount}, " +
            $"activeBoundaries={(activeBoundaries.Count == 0 ? "none" : string.Join(',', activeBoundaries))}");
    }

    private readonly record struct FieldBoundaryFrame(
        byte Module,
        ushort FieldId,
        uint FieldGlobalObject,
        uint BitsAddress,
        byte[] Bits)
    {
        public bool Matches(FieldBoundaryFrame other) =>
            Module == other.Module &&
            FieldId == other.FieldId &&
            FieldGlobalObject == other.FieldGlobalObject &&
            BitsAddress == other.BitsAddress &&
            Bits.AsSpan().SequenceEqual(other.Bits);
    }
}
