using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldScriptLineStateReader
{
    public const int AddressFieldLineIndexByEntity = 0x00CBF600;
    public const int AddressFieldLineStates = 0x00CC1F7C;
    public const int LineStateStride = 0x18;

    private const int MaximumEntityId = byte.MaxValue;

    private readonly Func<int, byte>? readByte;
    private readonly Func<int, int, bool>? isReadableMemory;
    private readonly ILegacyAddressSpace? addressSpace;

    public FieldScriptLineStateReader(Func<int, byte> readByte, Func<int, int, bool> isReadableMemory)
    {
        this.readByte = readByte;
        this.isReadableMemory = isReadableMemory;
    }

    public FieldScriptLineStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public string LastDiagnostic { get; private set; } = "not read";

    public bool IsEnabled(int entityId)
    {
        if (addressSpace is not null)
        {
            return TryRead(entityId, out var checkedEnabled) && checkedEnabled;
        }

        if (entityId < 0 || entityId > MaximumEntityId)
        {
            LastDiagnostic = $"invalid entity={entityId}";
            return false;
        }

        var mappingAddress = AddressFieldLineIndexByEntity + entityId;
        if (!isReadableMemory!(mappingAddress, sizeof(byte)))
        {
            LastDiagnostic = $"entity={entityId}, line mapping unreadable";
            return false;
        }

        var lineIndex = readByte!(mappingAddress);
        var stateAddress = AddressFieldLineStates + lineIndex * LineStateStride;
        if (!isReadableMemory(stateAddress, sizeof(byte)))
        {
            LastDiagnostic = $"entity={entityId}, line={lineIndex}, state unreadable";
            return false;
        }

        var legacyEnabled = readByte(stateAddress) != 0;
        LastDiagnostic = $"entity={entityId}, line={lineIndex}, enabled={legacyEnabled}";
        return legacyEnabled;
    }

    public bool TryRead(int entityId, out bool enabled)
    {
        enabled = false;
        if (addressSpace is null || entityId < 0 || entityId > MaximumEntityId)
        {
            LastDiagnostic = $"invalid entity={entityId}";
            return false;
        }

        var mappingAddress = (uint)(AddressFieldLineIndexByEntity + entityId);
        if (!TryCapture(mappingAddress, out var before) || before.Module != FieldPositionReader.FieldModule)
        {
            LastDiagnostic = $"entity={entityId}, checked state unreadable";
            return false;
        }

        var stateAddress = (uint)(AddressFieldLineStates + before.LineIndex * LineStateStride);
        if (!addressSpace.TryReadByte(stateAddress, out var state) ||
            !TryCapture(mappingAddress, out var after) || !before.Equals(after) ||
            !addressSpace.TryReadByte(stateAddress, out var stateAfter) || stateAfter != state)
        {
            LastDiagnostic = $"entity={entityId}, line={before.LineIndex}, checked state torn";
            return false;
        }

        enabled = state != 0;
        LastDiagnostic = $"entity={entityId}, line={before.LineIndex}, enabled={enabled}";
        return true;
    }

    private bool TryCapture(uint mappingAddress, out CheckedLine value)
    {
        value = default;
        var memory = addressSpace!;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !memory.TryReadByte(mappingAddress, out var lineIndex))
        {
            return false;
        }

        value = new CheckedLine(module, fieldId, lineIndex);
        return true;
    }

    private readonly record struct CheckedLine(byte Module, ushort FieldId, byte LineIndex);
}
