using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldNavigationControlTransform(int SignedControlDirection)
{
    private const double FullTurn = Math.PI * 2d;
    private const double DirectionUnitsPerTurn = 256d;

    public FieldNavigationStickDirection TransformWorldVector(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return new FieldNavigationStickDirection(0f, 0f);
        }

        // FFVII rotates field input with the signed control byte in the trigger header.
        var worldAngle = Math.Atan2(dx, -dy);
        var controlAngle = SignedControlDirection * FullTurn / DirectionUnitsPerTurn;
        var inputAngle = worldAngle - controlAngle;
        return new FieldNavigationStickDirection(
            (float)-Math.Sin(inputAngle),
            (float)-Math.Cos(inputAngle));
    }
}

public readonly record struct FieldNavigationStickDirection(float X, float Y);

public readonly record struct FieldNavigationControlReadResult(
    bool IsUsable,
    FieldNavigationControlTransform Transform,
    string Diagnostic);

public sealed class FieldNavigationControlReader
{
    public const int AddressFieldTriggersPtr = 0x00CFF454;
    public const int ControlDirectionOffset = 0x09;

    private readonly Func<int, int>? readInt32;
    private readonly Func<int, byte>? readByte;
    private readonly ILegacyAddressSpace? addressSpace;

    public FieldNavigationControlReader(Func<int, int> readInt32, Func<int, byte> readByte)
    {
        this.readInt32 = readInt32 ?? throw new ArgumentNullException(nameof(readInt32));
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
    }

    public FieldNavigationControlReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public FieldNavigationControlReadResult Read(FieldPositionSnapshot position) =>
        addressSpace is null ? ReadLegacy(position) : ReadChecked(position);

    private FieldNavigationControlReadResult ReadLegacy(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return Invalid("not in field module");
        }

        if (position.FieldId is < 0 or > ushort.MaxValue)
        {
            return Invalid("invalid field id");
        }

        if (!TryReadLegacyFrame(position, out var candidate, out var diagnostic))
        {
            return Invalid(diagnostic);
        }

        if (!TryReadLegacyFrame(position, out var confirmation, out _) || confirmation != candidate)
        {
            return Invalid("field navigation control changed during read");
        }

        return CreateValidResult(position, candidate);
    }

    private FieldNavigationControlReadResult ReadChecked(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return Invalid("not in field module");
        }

        if (position.FieldId is < 0 or > ushort.MaxValue)
        {
            return Invalid("invalid field id");
        }

        if (!TryReadCheckedFrame(position, out var candidate, out var diagnostic))
        {
            return Invalid(diagnostic);
        }

        if (!TryReadCheckedFrame(position, out var confirmation, out var confirmationDiagnostic))
        {
            return Invalid(confirmationDiagnostic);
        }

        if (confirmation != candidate)
        {
            return Invalid("field navigation control changed during read");
        }

        return CreateValidResult(position, candidate);
    }

    private bool TryReadLegacyFrame(
        FieldPositionSnapshot position,
        out FieldNavigationControlFrame frame,
        out string diagnostic)
    {
        frame = default;
        var module = readByte!(FieldPositionReader.AddressCurrentModule);
        if (!TryReadLegacyUInt16(FieldPositionReader.AddressFieldId, out var fieldId))
        {
            diagnostic = "field id address overflowed";
            return false;
        }

        var triggerHeader = unchecked((uint)readInt32!(AddressFieldTriggersPtr));
        if (!TryValidateHeader(position, module, fieldId, triggerHeader, out diagnostic) ||
            !TryAdd(triggerHeader, ControlDirectionOffset, out var controlAddress))
        {
            if (string.IsNullOrEmpty(diagnostic))
            {
                diagnostic = "trigger control address overflowed";
            }

            return false;
        }

        frame = new FieldNavigationControlFrame(
            module,
            fieldId,
            triggerHeader,
            readByte!(unchecked((int)controlAddress)));
        diagnostic = string.Empty;
        return true;
    }

    private bool TryReadCheckedFrame(
        FieldPositionSnapshot position,
        out FieldNavigationControlFrame frame,
        out string diagnostic)
    {
        frame = default;
        diagnostic = "field position is unavailable";
        var checkedAddressSpace = addressSpace!;
        if (!checkedAddressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !checkedAddressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId))
        {
            return false;
        }

        if (!checkedAddressSpace.TryReadUInt32((uint)AddressFieldTriggersPtr, out var triggerHeader))
        {
            diagnostic = "trigger pointer read failed";
            return false;
        }

        if (!TryValidateHeader(position, module, fieldId, triggerHeader, out diagnostic))
        {
            return false;
        }

        if (!TryAdd(triggerHeader, ControlDirectionOffset, out var controlAddress))
        {
            diagnostic = "trigger control address overflowed";
            return false;
        }

        if (!checkedAddressSpace.TryReadByte(controlAddress, out var controlDirection))
        {
            diagnostic = "trigger control read failed";
            return false;
        }

        frame = new FieldNavigationControlFrame(module, fieldId, triggerHeader, controlDirection);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryValidateHeader(
        FieldPositionSnapshot position,
        byte module,
        ushort fieldId,
        uint triggerHeader,
        out string diagnostic)
    {
        diagnostic = string.Empty;
        if (module != position.CurrentModule || fieldId != position.FieldId)
        {
            diagnostic = "field position is unavailable";
            return false;
        }

        if (triggerHeader == 0)
        {
            diagnostic = "trigger=0x00000000";
            return false;
        }

        return true;
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

    private static FieldNavigationControlReadResult CreateValidResult(
        FieldPositionSnapshot position,
        FieldNavigationControlFrame frame)
    {
        var signedControl = unchecked((sbyte)frame.ControlDirection);
        var transform = new FieldNavigationControlTransform(signedControl);
        return new FieldNavigationControlReadResult(
            true,
            transform,
            $"field={position.FieldId}, trigger=0x{frame.TriggerHeader:X8}, control={signedControl}");
    }

    private static FieldNavigationControlReadResult Invalid(string diagnostic) =>
        new(false, default, diagnostic);

    private readonly record struct FieldNavigationControlFrame(
        byte Module,
        ushort FieldId,
        uint TriggerHeader,
        byte ControlDirection);
}
