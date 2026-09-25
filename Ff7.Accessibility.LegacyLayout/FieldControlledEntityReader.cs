using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Whether a field entity's model is the one the player is moving.
///
/// <para>The field keeps the controlled model's index at script context +0x2A. PC (0061BCD7)
/// sets it to the model of the entity it binds when that character is in party slot 0, and
/// CC (006142D5) sets it to any entity's model without touching the party list, so party slot
/// 0 is not always the character being moved. The model each entity uses is in the table at
/// 0x00CBFB70 (0xFF for an entity without one). An entity is the player exactly when its
/// model is the controlled one.</para>
///
/// <para>Every answer is read between two captures of the module, field and controlled model,
/// and a torn or unreadable read is no answer rather than "no".</para>
///
/// <para>It also reads who is in party slot 0: the character at 0x00DC09E5 (field bank 3[9]),
/// which a party-slot request (PREQ, PRQSW, PRQEW with slot 0) runs the copy of, whoever CC has
/// made controlled.</para>
/// </summary>
public sealed class FieldControlledEntityReader
{
    public const int AddressScriptContextPointer = 0x00CBF9D8;
    public const int ControlledModelOffset = 0x2A;
    public const int AddressEntityModelIndices = 0x00CBFB70;
    public const int AddressPartySlots = 0x00DC09E5;

    private const byte NoModel = 0xFF;
    private const int MaximumEntityId = byte.MaxValue;

    private readonly ILegacyAddressSpace addressSpace;

    public FieldControlledEntityReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public string LastDiagnostic { get; private set; } = "not read";

    /// <summary>Whether the entity's model is the controlled one, or null when that cannot be read.</summary>
    public bool? IsControlled(int entityId)
    {
        if (entityId < 0 || entityId > MaximumEntityId)
        {
            LastDiagnostic = $"invalid entity={entityId}";
            return null;
        }

        var modelAddress = (uint)(AddressEntityModelIndices + entityId);
        if (!TryCapture(out var before) ||
            !addressSpace.TryReadByte(modelAddress, out var model) ||
            !TryCapture(out var after) ||
            !before.Equals(after) ||
            !addressSpace.TryReadByte(modelAddress, out var modelAfter) ||
            modelAfter != model)
        {
            LastDiagnostic = $"entity={entityId}, controlled model unreadable or torn";
            return null;
        }

        var controlled = model != NoModel && model == before.ControlledModel;
        LastDiagnostic = $"entity={entityId}, model={model}, controlled={before.ControlledModel}, is={controlled}";
        return controlled;
    }

    /// <summary>
    /// The character in party slot 0 (0xFF when it is empty), or null when that cannot be read.
    /// </summary>
    public int? ReadPartyLeaderCharacter()
    {
        if (!TryCapture(out var before) ||
            !addressSpace.TryReadByte((uint)AddressPartySlots, out var leader) ||
            !TryCapture(out var after) ||
            !before.Equals(after) ||
            !addressSpace.TryReadByte((uint)AddressPartySlots, out var leaderAfter) ||
            leaderAfter != leader)
        {
            LastDiagnostic = "party slot 0 unreadable or torn";
            return null;
        }

        LastDiagnostic = $"party slot 0={leader}";
        return leader;
    }

    private bool TryCapture(out ControlSnapshot snapshot)
    {
        snapshot = default;
        if (!addressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            module != FieldPositionReader.FieldModule ||
            !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !addressSpace.TryReadUInt32(AddressScriptContextPointer, out var context) ||
            context == 0 ||
            !addressSpace.TryReadUInt16(context + ControlledModelOffset, out var controlledModel))
        {
            return false;
        }

        snapshot = new ControlSnapshot(module, fieldId, context, controlledModel);
        return true;
    }

    private readonly record struct ControlSnapshot(byte Module, ushort FieldId, uint Context, ushort ControlledModel);
}
