using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldOpcodeParameterReader
{
    public const byte MessageOpcode = 0x40;
    public const byte AskOpcode = 0x48;
    public const int MaximumAskVisibleLineCount = 13;
    public const int AddressFieldScriptPtr = 0x00CBF5E8;
    public const int AddressCurrentEntityId = 0x00CC0964;
    public const int AddressFieldCurrScriptPosition = 0x00CC0CF8;
    public const int AddressCurrentFieldId = FieldPositionReader.AddressFieldId;

    private readonly Func<int, int>? readInt32;
    private readonly Func<int, ushort>? readUInt16;
    private readonly Func<int, byte>? readByte;
    private readonly ILegacyAddressSpace? addressSpace;

    public FieldOpcodeParameterReader(Func<int, int> readInt32, Func<int, ushort> readUInt16, Func<int, byte> readByte)
    {
        this.readInt32 = readInt32;
        this.readUInt16 = readUInt16;
        this.readByte = readByte;
    }

    public FieldOpcodeParameterReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public bool TryReadMessage(out FieldOpcodeMessageObservation observation)
    {
        if (addressSpace is not null)
        {
            return TryReadChecked(FieldOpcodeKind.Message, out observation);
        }

        if (!TryReadByteParameter(0, out var windowId) || !TryReadByteParameter(1, out var dialogId))
        {
            observation = default;
            return false;
        }

        observation = new FieldOpcodeMessageObservation(
            FieldOpcodeKind.Message,
            readUInt16!(AddressCurrentFieldId),
            windowId,
            dialogId);
        return true;
    }

    public bool TryReadAsk(out FieldOpcodeMessageObservation observation)
    {
        if (addressSpace is not null)
        {
            return TryReadChecked(FieldOpcodeKind.Ask, out observation);
        }

        if (!TryReadByteParameter(1, out var windowId) ||
            !TryReadByteParameter(2, out var dialogId) ||
            !TryReadByteParameter(3, out var firstQuestionLine) ||
            !TryReadByteParameter(4, out var lastQuestionLine))
        {
            observation = default;
            return false;
        }

        observation = new FieldOpcodeMessageObservation(
            FieldOpcodeKind.Ask,
            readUInt16!(AddressCurrentFieldId),
            windowId,
            dialogId,
            firstQuestionLine,
            lastQuestionLine);
        return true;
    }

    private bool TryReadChecked(FieldOpcodeKind kind, out FieldOpcodeMessageObservation observation)
    {
        observation = default;
        if (!TryCapture(out var before) || before.Module != FieldPositionReader.FieldModule ||
            !TryAdd(before.ScriptPointer, before.ScriptPosition, out var instructionAddress) ||
            !TryReadInstruction(kind, instructionAddress, before.EntityId, out var instruction, out var windowId))
        {
            return false;
        }

        if (!TryCapture(out var middle) || !before.Equals(middle) ||
            !TryReadInstruction(kind, instructionAddress, before.EntityId, out var instructionAfter, out var windowIdAfter) ||
            windowIdAfter != windowId ||
            !instruction.AsSpan().SequenceEqual(instructionAfter) ||
            !TryCapture(out var after) || !before.Equals(after))
        {
            return false;
        }

        if (kind == FieldOpcodeKind.Message)
        {
            observation = new FieldOpcodeMessageObservation(
                kind,
                before.FieldId,
                windowId,
                instruction[2]);
            return true;
        }

        observation = new FieldOpcodeMessageObservation(
            kind,
            before.FieldId,
            windowId,
            instruction[3],
            instruction[4],
            instruction[5]);
        return true;
    }

    private bool TryReadInstruction(
        FieldOpcodeKind kind,
        uint instructionAddress,
        byte entityId,
        out byte[] instruction,
        out byte windowId)
    {
        var expectedOpcode = kind == FieldOpcodeKind.Message ? MessageOpcode : AskOpcode;
        var instructionLength = kind == FieldOpcodeKind.Message ? 3 : 7;
        instruction = new byte[instructionLength];
        windowId = 0;
        if (!addressSpace!.TryRead(instructionAddress, instruction) || instruction[0] != expectedOpcode)
        {
            return false;
        }

        windowId = instruction[kind == FieldOpcodeKind.Message ? 1 : 2];
        if (windowId >= FieldMessageReader.WindowCount ||
            !TryAdd((uint)FieldMessageReader.AddressFieldWindowStates, windowId, out var ownerAddress) ||
            !addressSpace.TryReadByte(ownerAddress, out var owner) ||
            (owner != entityId && owner != FieldMessageReader.FreeWindowState))
        {
            return false;
        }

        if (kind == FieldOpcodeKind.Ask)
        {
            var firstQuestionLine = instruction[4];
            var lastQuestionLine = instruction[5];
            var choiceCount = lastQuestionLine - firstQuestionLine + 1;
            if (firstQuestionLine > lastQuestionLine ||
                lastQuestionLine >= MaximumAskVisibleLineCount ||
                choiceCount is < 1 or > MaximumAskVisibleLineCount)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryCapture(out CheckedOpcode value)
    {
        value = default;
        var memory = addressSpace!;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)AddressCurrentFieldId, out var fieldId) ||
            !memory.TryReadUInt32((uint)AddressFieldScriptPtr, out var scriptPointer) || scriptPointer == 0 ||
            !TryAdd(scriptPointer, 2, out var entityCountAddress) ||
            !memory.TryReadByte(entityCountAddress, out var entityCount) || entityCount == 0 ||
            !memory.TryReadByte((uint)AddressCurrentEntityId, out var entityId) ||
            entityId >= entityCount ||
            !TryAdd((uint)AddressFieldCurrScriptPosition, (ulong)entityId * sizeof(ushort), out var scriptPositionAddress) ||
            !memory.TryReadUInt16(scriptPositionAddress, out var scriptPosition))
        {
            return false;
        }

        value = new CheckedOpcode(module, fieldId, scriptPointer, entityCount, entityId, scriptPosition);
        return true;
    }

    private bool TryReadByteParameter(int parameterIndex, out byte value)
    {
        var scriptPtr = readInt32!(AddressFieldScriptPtr);
        if (scriptPtr <= 0)
        {
            value = 0;
            return false;
        }

        var entityId = readByte!(AddressCurrentEntityId);
        var scriptPosition = readUInt16!(AddressFieldCurrScriptPosition + entityId * sizeof(ushort));
        value = readByte(scriptPtr + scriptPosition + parameterIndex + 1);
        return true;
    }

    private static bool TryAdd(uint address, ulong offset, out uint result)
    {
        var sum = (ulong)address + offset;
        result = sum <= uint.MaxValue ? (uint)sum : 0;
        return sum <= uint.MaxValue;
    }

    private readonly record struct CheckedOpcode(
        byte Module,
        ushort FieldId,
        uint ScriptPointer,
        byte EntityCount,
        byte EntityId,
        ushort ScriptPosition);
}

public readonly record struct FieldOpcodeMessageObservation(
    FieldOpcodeKind Kind,
    int FieldId,
    int WindowId,
    int DialogId,
    int FirstQuestionLine = -1,
    int LastQuestionLine = -1,
    long LifecycleToken = 0);

public enum FieldOpcodeKind
{
    Message,
    Ask
}
