using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldScriptContextReader
{
    public const int AddressCurrentModule = FieldPositionReader.AddressCurrentModule;
    public const int AddressCurrentFieldId = FieldPositionReader.AddressFieldId;
    public const int AddressFieldScriptPtr = FieldOpcodeParameterReader.AddressFieldScriptPtr;
    public const int AddressCurrentEntityId = FieldOpcodeParameterReader.AddressCurrentEntityId;
    public const int AddressCurrentEntityScriptId = 0x00CBF9E8;
    public const int AddressCurrentEntityScriptPriority = 0x00CC0B30;
    public const int AddressFieldCurrScriptPosition = FieldOpcodeParameterReader.AddressFieldCurrScriptPosition;
    public const int ScriptSlotsPerEntity = 8;
    public const int ScriptOffsetsPerEntity = 32;
    public const int ScriptOffsetTableHeaderSize = 0x20;
    public const int ScriptOffsetEntityStride = ScriptOffsetsPerEntity * sizeof(ushort);

    private readonly Func<int, byte>? readByte;
    private readonly Func<int, ushort>? readUInt16;
    private readonly Func<int, int>? readInt32;
    private readonly ILegacyAddressSpace? addressSpace;

    public FieldScriptContextReader(Func<int, byte> readByte, Func<int, ushort> readUInt16, Func<int, int> readInt32)
    {
        this.readByte = readByte;
        this.readUInt16 = readUInt16;
        this.readInt32 = readInt32;
    }

    public FieldScriptContextReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public bool TryRead(out FieldScriptContext context)
    {
        if (addressSpace is not null)
        {
            return TryReadChecked(out context);
        }

        if (readByte!(AddressCurrentModule) != FieldPositionReader.FieldModule)
        {
            context = default;
            return false;
        }

        var scriptPtr = readInt32!(AddressFieldScriptPtr);
        if (scriptPtr <= 0)
        {
            context = default;
            return false;
        }

        var entityCount = readByte(scriptPtr + 2);
        var entityId = readByte(AddressCurrentEntityId);
        if (entityCount == 0 || entityId >= entityCount)
        {
            context = default;
            return false;
        }

        var priority = readByte(AddressCurrentEntityScriptPriority + entityId);
        if (priority >= ScriptSlotsPerEntity)
        {
            context = default;
            return false;
        }

        var scriptId = readByte(AddressCurrentEntityScriptId + entityId * ScriptSlotsPerEntity + priority);
        if (scriptId >= ScriptOffsetsPerEntity)
        {
            context = default;
            return false;
        }

        var absolutePosition = readUInt16!(AddressFieldCurrScriptPosition + entityId * sizeof(ushort));
        var scriptOffsetTable = scriptPtr +
            (readUInt16(scriptPtr + 6) << 2) +
            ScriptOffsetTableHeaderSize +
            entityCount * ScriptSlotsPerEntity +
            entityId * ScriptOffsetEntityStride;
        var scriptBaseOffset = readUInt16(scriptOffsetTable + scriptId * sizeof(ushort));
        if (absolutePosition < scriptBaseOffset)
        {
            context = default;
            return false;
        }

        context = new FieldScriptContext(
            readUInt16(AddressCurrentFieldId),
            entityId,
            scriptId,
            absolutePosition - scriptBaseOffset,
            readByte(scriptPtr + absolutePosition));
        return true;
    }

    private bool TryReadChecked(out FieldScriptContext context)
    {
        context = default;
        var memory = addressSpace!;
        if (!TryCapture(out var before) || before.Module != FieldPositionReader.FieldModule ||
            before.EntityCount == 0 || before.EntityId >= before.EntityCount ||
            before.Priority >= ScriptSlotsPerEntity || before.ScriptId >= ScriptOffsetsPerEntity ||
            before.AbsolutePosition < before.ScriptBaseOffset ||
            !LegacyFf7TextReader.TryAdd(before.ScriptPointer, before.AbsolutePosition, out var opcodeAddress) ||
            !memory.TryReadByte(opcodeAddress, out var opcode) ||
            !TryCapture(out var middle) || !before.Equals(middle) ||
            !memory.TryReadByte(opcodeAddress, out var opcodeAfter) || opcodeAfter != opcode ||
            !TryCapture(out var after) || !before.Equals(after))
        {
            return false;
        }

        context = new FieldScriptContext(
            before.FieldId,
            before.EntityId,
            before.ScriptId,
            before.AbsolutePosition - before.ScriptBaseOffset,
            opcode);
        return true;
    }

    private bool TryCapture(out CheckedContext value)
    {
        value = default;
        var memory = addressSpace!;
        if (!memory.TryReadByte((uint)AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)AddressCurrentFieldId, out var fieldId) ||
            !memory.TryReadUInt32((uint)AddressFieldScriptPtr, out var scriptPointer) || scriptPointer == 0 ||
            !LegacyFf7TextReader.TryAdd(scriptPointer, 2, out var countAddress) ||
            !LegacyFf7TextReader.TryAdd(scriptPointer, 6, out var headerAddress) ||
            !memory.TryReadByte(countAddress, out var entityCount) ||
            !memory.TryReadUInt16(headerAddress, out var headerWords) ||
            !memory.TryReadByte((uint)AddressCurrentEntityId, out var entityId) ||
            !TryCaptureEntity(scriptPointer, entityCount, entityId, headerWords, out value))
        {
            return false;
        }

        value = value with { Module = module, FieldId = fieldId };
        return true;
    }

    private bool TryCaptureEntity(uint scriptPointer, byte entityCount, byte entityId, ushort headerWords, out CheckedContext value)
    {
        value = default;
        var memory = addressSpace!;
        if (entityCount == 0 || entityId >= entityCount ||
            !memory.TryReadByte((uint)(AddressCurrentEntityScriptPriority + entityId), out var priority) ||
            priority >= ScriptSlotsPerEntity ||
            !memory.TryReadByte((uint)(AddressCurrentEntityScriptId + entityId * ScriptSlotsPerEntity + priority), out var scriptId) ||
            scriptId >= ScriptOffsetsPerEntity ||
            !memory.TryReadUInt16((uint)(AddressFieldCurrScriptPosition + entityId * sizeof(ushort)), out var absolutePosition))
        {
            return false;
        }

        var tableOffset = ((ulong)headerWords << 2) + ScriptOffsetTableHeaderSize +
            (ulong)entityCount * ScriptSlotsPerEntity + (ulong)entityId * ScriptOffsetEntityStride +
            (ulong)scriptId * sizeof(ushort);
        if (!TryAdd(scriptPointer, tableOffset, out var baseOffsetAddress) ||
            !memory.TryReadUInt16(baseOffsetAddress, out var scriptBaseOffset))
        {
            return false;
        }

        value = new CheckedContext(0, 0, scriptPointer, entityCount, headerWords, entityId, priority, scriptId, absolutePosition, scriptBaseOffset);
        return true;
    }

    private static bool TryAdd(uint address, ulong offset, out uint result)
    {
        var sum = address + offset;
        result = sum <= uint.MaxValue ? (uint)sum : 0;
        return sum <= uint.MaxValue;
    }

    private readonly record struct CheckedContext(
        byte Module,
        ushort FieldId,
        uint ScriptPointer,
        byte EntityCount,
        ushort HeaderWords,
        byte EntityId,
        byte Priority,
        byte ScriptId,
        ushort AbsolutePosition,
        ushort ScriptBaseOffset);
}

public readonly record struct FieldScriptContext(int FieldId, int EntityId, int ScriptId, int ByteIndex, int Opcode);
