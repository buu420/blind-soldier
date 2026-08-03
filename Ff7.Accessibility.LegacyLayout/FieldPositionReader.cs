using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldPositionReader
{
    public const int AddressCurrentModule = 0x00CBF9DC;
    public const int AddressFieldId = 0x00CC15D0;
    public const int AddressFieldCurrentModelId = 0x00CC0DB2;
    public const int AddressFieldNumModels = 0x00CFF73E;
    public const int AddressFieldModelsPtr = 0x00CFF738;
    public const int AddressFieldModelsObjs = 0x00CC1670;
    public const int FieldModelStride = 400;
    public const int FieldObjectStride = 0x88;
    public const int ModelXOffset = 0x04;
    public const int ModelYOffset = 0x08;
    public const int ModelZOffset = 0x0C;
    public const int ModelDirectionOffset = 0x1C;
    public const int ObjectTriangleOffset = 0x78;

    public const int FieldModule = 1;

    private readonly Func<int, int>? readInt32;
    private readonly Func<int, short>? readInt16;
    private readonly Func<int, ushort>? readUInt16;
    private readonly Func<int, byte>? readByte;
    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace? addressSpace;

    public FieldPositionReader(
        Func<int, int> readInt32,
        Func<int, short> readInt16,
        Func<int, ushort> readUInt16,
        Func<int, byte> readByte)
    {
        this.readInt32 = readInt32 ?? throw new ArgumentNullException(nameof(readInt32));
        this.readInt16 = readInt16 ?? throw new ArgumentNullException(nameof(readInt16));
        this.readUInt16 = readUInt16 ?? throw new ArgumentNullException(nameof(readUInt16));
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
    }

    public FieldPositionReader(Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public FieldPositionReadResult Read() => addressSpace is null ? ReadLegacy() : ReadChecked();

    private FieldPositionReadResult ReadLegacy()
    {
        if (!TryReadLegacyFrame(out var candidate, out var diagnostic))
        {
            return FieldPositionReadResult.Invalid(0, candidate.Position, diagnostic);
        }

        if (!TryReadLegacyFrame(out var confirmation, out _) || confirmation != candidate)
        {
            return FieldPositionReadResult.Invalid(0, candidate.Position, "field position changed during read");
        }

        return CreateValidResult(candidate);
    }

    private FieldPositionReadResult ReadChecked()
    {
        if (!TryReadCheckedFrame(out var candidate, out var diagnostic))
        {
            return FieldPositionReadResult.Invalid(0, candidate.Position, diagnostic);
        }

        if (!TryReadCheckedFrame(out var confirmation, out var confirmationDiagnostic))
        {
            return FieldPositionReadResult.Invalid(0, candidate.Position, confirmationDiagnostic);
        }

        if (confirmation != candidate)
        {
            return FieldPositionReadResult.Invalid(0, candidate.Position, "field position changed during read");
        }

        return CreateValidResult(candidate);
    }

    private bool TryReadLegacyFrame(out FieldPositionFrame frame, out string diagnostic)
    {
        var module = readByte!(AddressCurrentModule);
        var fieldId = readUInt16!(AddressFieldId);
        var modelIndex = readUInt16!(AddressFieldCurrentModelId);
        var modelCount = readByte!(AddressFieldNumModels);
        var modelTable = unchecked((uint)readInt32!(AddressFieldModelsPtr));
        frame = new FieldPositionFrame(module, fieldId, modelIndex, modelCount, modelTable, 0, 0, 0, 0, 0, 0);

        if (!TryValidateHeader(frame, out diagnostic) ||
            !TryCalculateAddresses(
                modelTable,
                modelIndex,
                out var modelBase,
                out var xAddress,
                out var yAddress,
                out var zAddress,
                out var triangleAddress,
                out var directionAddress))
        {
            if (string.IsNullOrEmpty(diagnostic))
            {
                diagnostic = "field position address calculation overflowed";
            }

            return false;
        }

        frame = frame with
        {
            ModelBase = modelBase,
            X = readInt32!(unchecked((int)xAddress)),
            Y = readInt32!(unchecked((int)yAddress)),
            Z = readInt32!(unchecked((int)zAddress)),
            TriangleId = readUInt16!(unchecked((int)triangleAddress)),
            Direction = readByte!(unchecked((int)directionAddress))
        };
        diagnostic = string.Empty;
        return true;
    }

    private bool TryReadCheckedFrame(out FieldPositionFrame frame, out string diagnostic)
    {
        frame = default;
        diagnostic = "field position primitive read failed";
        if (!TryReadByte(AddressCurrentModule, out var module) ||
            !TryReadUInt16(AddressFieldId, out var fieldId) ||
            !TryReadUInt16(AddressFieldCurrentModelId, out var modelIndex) ||
            !TryReadByte(AddressFieldNumModels, out var modelCount) ||
            !TryReadUInt32(AddressFieldModelsPtr, out var modelTable))
        {
            return false;
        }

        frame = new FieldPositionFrame(module, fieldId, modelIndex, modelCount, modelTable, 0, 0, 0, 0, 0, 0);
        if (!TryValidateHeader(frame, out diagnostic))
        {
            return false;
        }

        if (!TryCalculateAddresses(
                modelTable,
                modelIndex,
                out var modelBase,
                out var xAddress,
                out var yAddress,
                out var zAddress,
                out var triangleAddress,
                out var directionAddress))
        {
            diagnostic = "field position address calculation overflowed";
            return false;
        }

        var checkedAddressSpace = addressSpace!;
        if (!checkedAddressSpace.TryReadInt32(xAddress, out var x) ||
            !checkedAddressSpace.TryReadInt32(yAddress, out var y) ||
            !checkedAddressSpace.TryReadInt32(zAddress, out var z) ||
            !checkedAddressSpace.TryReadUInt16(triangleAddress, out var triangleId) ||
            !checkedAddressSpace.TryReadByte(directionAddress, out var direction))
        {
            diagnostic = "field position nested model read failed";
            return false;
        }

        frame = frame with
        {
            ModelBase = modelBase,
            X = x,
            Y = y,
            Z = z,
            TriangleId = triangleId,
            Direction = direction
        };
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryValidateHeader(FieldPositionFrame frame, out string diagnostic)
    {
        diagnostic = string.Empty;
        if (frame.Module != FieldModule)
        {
            diagnostic =
                $"module={frame.Module}, field={frame.FieldId}, model={frame.ModelIndex}, models={frame.ModelCount}, not field";
            return false;
        }

        if (frame.ModelTable == 0)
        {
            diagnostic =
                $"module={frame.Module}, field={frame.FieldId}, model={frame.ModelIndex}, models={frame.ModelCount}, model pointer is null";
            return false;
        }

        if (frame.ModelIndex >= frame.ModelCount)
        {
            diagnostic =
                $"module={frame.Module}, field={frame.FieldId}, model={frame.ModelIndex}, models={frame.ModelCount}, model index out of range";
            return false;
        }

        return true;
    }

    private static bool TryCalculateAddresses(
        uint modelTable,
        ushort modelIndex,
        out uint modelBase,
        out uint xAddress,
        out uint yAddress,
        out uint zAddress,
        out uint triangleAddress,
        out uint directionAddress)
    {
        modelBase = xAddress = yAddress = zAddress = triangleAddress = directionAddress = 0;
        return TryAddScaled(modelTable, modelIndex, FieldModelStride, out modelBase) &&
            TryAddScaled((uint)AddressFieldModelsObjs, modelIndex, FieldObjectStride, out var objectBase) &&
            TryAdd(modelBase, ModelXOffset, out xAddress) &&
            TryAdd(modelBase, ModelYOffset, out yAddress) &&
            TryAdd(modelBase, ModelZOffset, out zAddress) &&
            TryAdd(modelBase, ModelDirectionOffset, out directionAddress) &&
            TryAdd(objectBase, ObjectTriangleOffset, out triangleAddress);
    }

    private static FieldPositionReadResult CreateValidResult(FieldPositionFrame frame)
    {
        var position = frame.Position;
        return FieldPositionReadResult.Valid(
            frame.ModelBase,
            position,
            $"module={position.CurrentModule}, field={position.FieldId}, model={position.ModelIndex}/{frame.ModelCount}, " +
                $"base=0x{frame.ModelBase:X8}, x={position.X}, y={position.Y}, z={position.Z}, triangle={position.TriangleId}, direction={position.Direction}");
    }

    private bool TryReadByte(int address, out byte value) =>
        addressSpace!.TryReadByte((uint)address, out value);

    private bool TryReadUInt16(int address, out ushort value) =>
        addressSpace!.TryReadUInt16((uint)address, out value);

    private bool TryReadUInt32(int address, out uint value) =>
        addressSpace!.TryReadUInt32((uint)address, out value);

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

    private static bool TryAddScaled(uint address, ushort index, int stride, out uint result)
    {
        result = 0;
        if (stride < 0)
        {
            return false;
        }

        try
        {
            result = checked(address + checked((uint)index * (uint)stride));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private readonly record struct FieldPositionFrame(
        byte Module,
        ushort FieldId,
        ushort ModelIndex,
        byte ModelCount,
        uint ModelTable,
        uint ModelBase,
        int X,
        int Y,
        int Z,
        ushort TriangleId,
        byte Direction)
    {
        public FieldPositionSnapshot Position =>
            new(Module, FieldId, ModelIndex, X, Y, Z, TriangleId, Direction);
    }

    public static bool IsUsable(FieldPositionSnapshot position) => position.CurrentModule == FieldModule;
}

public readonly record struct FieldPositionReadResult(
    bool IsUsable,
    uint ModelBase,
    FieldPositionSnapshot Position,
    string Diagnostic)
{
    public static FieldPositionReadResult Valid(uint modelBase, FieldPositionSnapshot position, string diagnostic) =>
        new(true, modelBase, position, diagnostic);

    public static FieldPositionReadResult Invalid(uint modelBase, FieldPositionSnapshot position, string diagnostic) =>
        new(false, modelBase, position, diagnostic);
}

public readonly record struct FieldPositionSnapshot(
    int CurrentModule,
    int FieldId,
    int ModelIndex,
    int X,
    int Y,
    int Z,
    ushort TriangleId,
    byte Direction);
