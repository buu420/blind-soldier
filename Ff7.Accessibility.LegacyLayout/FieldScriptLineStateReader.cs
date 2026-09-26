using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldScriptLineStateReader
{
    public const int AddressFieldLineIndexByEntity = 0x00CBF600;
    public const int AddressFieldLineStates = 0x00CC1F7C;
    public const int LineStateStride = 0x18;

    /// <summary>
    /// Each line's segment: six words (start X, Y, Z, end X, Y, Z) twelve bytes before its enabled
    /// byte. LINE (006111D8) writes them there from its operands and SLINE (006114D0) moves them.
    /// </summary>
    public const int AddressFieldLineSegments = AddressFieldLineStates - 12;

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

    /// <summary>
    /// The line's live segment, read twice with its mapping and field around it; false when any
    /// read fails, the mapping or segment changes between them, or the line is off.
    /// </summary>
    public bool TryReadSegment(int entityId, out FieldNavigationTriggerLine segment)
    {
        segment = default;
        if (entityId < 0 || entityId > MaximumEntityId)
        {
            LastDiagnostic = $"invalid entity={entityId}";
            return false;
        }

        if (addressSpace is null)
        {
            // The unchecked host: the same bytes through its readable-memory test.
            var mappingAddress = AddressFieldLineIndexByEntity + entityId;
            if (!isReadableMemory!(mappingAddress, sizeof(byte)))
            {
                return false;
            }

            var lineIndex = readByte!(mappingAddress);
            var at = AddressFieldLineSegments + lineIndex * LineStateStride;
            if (!isReadableMemory(at, 13) || readByte(at + 12) == 0)
            {
                return false;
            }

            short Word(int offset) => (short)(readByte(at + offset) | (readByte(at + offset + 1) << 8));
            segment = new FieldNavigationTriggerLine(Word(0), Word(2), Word(4), Word(6), Word(8), Word(10));
            return true;
        }

        var mapping = (uint)(AddressFieldLineIndexByEntity + entityId);
        if (!TryCapture(mapping, out var before) || before.Module != FieldPositionReader.FieldModule ||
            !TryReadSegmentWords((uint)(AddressFieldLineSegments + before.LineIndex * LineStateStride), out var first) ||
            !TryReadSegmentWords((uint)(AddressFieldLineSegments + before.LineIndex * LineStateStride), out var second) ||
            !TryCapture(mapping, out var after) || !before.Equals(after) || first != second)
        {
            LastDiagnostic = $"entity={entityId}, checked segment torn or unreadable";
            return false;
        }

        if (!first.Enabled)
        {
            LastDiagnostic = $"entity={entityId}, line={before.LineIndex}, off";
            return false;
        }

        segment = first.Line;
        LastDiagnostic = $"entity={entityId}, line={before.LineIndex}, segment={segment}";
        return true;
    }

    private bool TryReadSegmentWords(uint at, out (FieldNavigationTriggerLine Line, bool Enabled) value)
    {
        value = default;
        var memory = addressSpace!;
        var words = new short[6];
        for (var index = 0; index < 6; index++)
        {
            if (!memory.TryReadUInt16(at + (uint)(index * 2), out var word))
            {
                return false;
            }

            words[index] = unchecked((short)word);
        }

        if (!memory.TryReadByte(at + 12, out var enabled))
        {
            return false;
        }

        value = (new FieldNavigationTriggerLine(words[0], words[1], words[2], words[3], words[4], words[5]), enabled != 0);
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
