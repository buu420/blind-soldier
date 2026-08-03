namespace Ff7.Accessibility.Reloaded;

public enum FieldLadderPhase
{
    None,
    Mounted,
    Climbing,
    Completing
}

public readonly record struct FieldLadderStateSnapshot(
    bool IsUsable,
    bool IsMounted,
    FieldLadderPhase Phase,
    FieldNavigationInput RequiredInput,
    FieldNavigationRouteWaypoint Target,
    int TargetTriangle,
    byte MovementMode,
    ushort Progress)
{
    public static FieldLadderStateSnapshot NotMounted { get; } =
        new(true, false, FieldLadderPhase.None, FieldNavigationInput.None, default, -1, 0, 0);
}

public readonly record struct FieldLadderStateReadResult(
    bool IsUsable,
    FieldLadderStateSnapshot State,
    string Diagnostic)
{
    public static FieldLadderStateReadResult Invalid(string diagnostic) =>
        new(false, default, diagnostic);
}

public sealed class FieldLadderStateReader
{
    public const int FixedPointScale = 4096;
    public const int FieldEventDataStride = 0x88;
    public const int MovementModeOffset = 0x63;
    public const int LadderReverseOffset = 0x6E;
    public const int LadderProgressOffset = 0x70;
    public const int TargetTriangleOffset = 0x7A;
    public const int TargetXOffset = 0x7C;
    public const int TargetYOffset = 0x80;
    public const int TargetZOffset = 0x84;

    private readonly Func<int, int> readInt32;
    private readonly Func<int, ushort> readUInt16;
    private readonly Func<int, byte> readByte;

    public FieldLadderStateReader(
        Func<int, int> readInt32,
        Func<int, ushort> readUInt16,
        Func<int, byte> readByte)
    {
        this.readInt32 = readInt32;
        this.readUInt16 = readUInt16;
        this.readByte = readByte;
    }

    public FieldLadderStateReadResult Read(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return FieldLadderStateReadResult.Invalid(
                $"module={position.CurrentModule}, field={position.FieldId}, not field");
        }

        var eventTable = readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr);
        if (eventTable == 0)
        {
            return FieldLadderStateReadResult.Invalid(
                $"field={position.FieldId}, event data pointer is null");
        }

        var modelCount = readByte(FieldPositionReader.AddressFieldNumModels);
        if (modelCount == 0 || position.ModelIndex < 0 || position.ModelIndex >= modelCount)
        {
            return FieldLadderStateReadResult.Invalid(
                $"field={position.FieldId}, model={position.ModelIndex}/{modelCount}, model index out of range");
        }

        var eventAddress64 = (long)eventTable + position.ModelIndex * FieldEventDataStride;
        if (eventAddress64 is < int.MinValue or > int.MaxValue)
        {
            return FieldLadderStateReadResult.Invalid(
                $"field={position.FieldId}, model={position.ModelIndex}, event address overflow");
        }

        var eventAddress = (int)eventAddress64;
        var movementMode = readByte(eventAddress + MovementModeOffset);
        if (movementMode is not 4 and not 5)
        {
            return new FieldLadderStateReadResult(
                true,
                FieldLadderStateSnapshot.NotMounted,
                $"field={position.FieldId}, model={position.ModelIndex}, movementMode={movementMode}, not mounted");
        }

        var reverse = readUInt16(eventAddress + LadderReverseOffset);
        var progress = readUInt16(eventAddress + LadderProgressOffset);
        if (reverse > 1 || progress > 2)
        {
            return FieldLadderStateReadResult.Invalid(
                $"field={position.FieldId}, model={position.ModelIndex}, invalid ladder state mode={movementMode}, reverse={reverse}, progress={progress}");
        }

        var requiredInput = (movementMode, reverse) switch
        {
            (4, 0) => FieldNavigationInput.Down,
            (4, 1) => FieldNavigationInput.Up,
            (5, 0) => FieldNavigationInput.Right,
            (5, 1) => FieldNavigationInput.Left,
            _ => FieldNavigationInput.None
        };
        var phase = progress switch
        {
            1 => FieldLadderPhase.Climbing,
            2 => FieldLadderPhase.Completing,
            _ => FieldLadderPhase.Mounted
        };
        var target = new FieldNavigationRouteWaypoint(
            readInt32(eventAddress + TargetXOffset) >> 12,
            readInt32(eventAddress + TargetYOffset) >> 12,
            readInt32(eventAddress + TargetZOffset) >> 12);
        var targetTriangle = readUInt16(eventAddress + TargetTriangleOffset);
        requiredInput = ResolveVerifiedInput(position.FieldId, target, targetTriangle, requiredInput);
        var state = new FieldLadderStateSnapshot(
            true,
            true,
            phase,
            requiredInput,
            target,
            targetTriangle,
            movementMode,
            progress);
        return new FieldLadderStateReadResult(
            true,
            state,
            $"field={position.FieldId}, model={position.ModelIndex}, event=0x{eventAddress:X8}, " +
            $"mode={movementMode}, reverse={reverse}, progress={progress}, input={requiredInput}, " +
            $"target={target.X},{target.Y},{target.Z}, triangle={targetTriangle}");
    }

    private static FieldNavigationInput ResolveVerifiedInput(
        int fieldId,
        FieldNavigationRouteWaypoint target,
        int targetTriangle,
        FieldNavigationInput decodedInput)
    {
        // The penultimate wcrimb_1 ladder reports generic mode 4/reverse 1,
        // but Cloud actually moves across it with Left. Match its exact native
        // endpoint so the correction cannot leak into other ladders.
        if (fieldId == 223 &&
            target == new FieldNavigationRouteWaypoint(-40, 1039, 2273) &&
            targetTriangle == 158)
        {
            return FieldNavigationInput.Left;
        }

        return decodedInput;
    }
}
