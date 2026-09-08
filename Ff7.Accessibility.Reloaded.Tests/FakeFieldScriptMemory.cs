using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// A guest address space that models the parts of the field script the arcade
/// readers actually consult: the module and field, the script table with its entity
/// count, each entity's running priority and script, each entity's model, and the
/// model table's animation id, current frame and end frame.
///
/// The point is that a test cannot activate a minigame just by writing plausible
/// numbers into the shared temporary bank. It has to make the native controller
/// actually run, which is what the production readers now require.
/// </summary>
internal sealed class FakeFieldScriptMemory : ILegacyAddressSpace
{
    private const uint ScriptTableBase = 0x00500000;
    private const uint ModelTableBase = 0x00600000;
    private const int MaxEntities = 32;
    private const int MaxModels = 32;

    private readonly byte[] bank = new byte[256];
    private readonly byte[] priorities = new byte[MaxEntities];
    private readonly byte[] scriptIds = new byte[MaxEntities * FieldScriptControllerReader.ScriptSlotsPerEntity];
    private readonly byte[] modelIds = new byte[MaxEntities];
    private readonly byte[] modelRunStates = new byte[MaxModels];
    private readonly byte[] models = new byte[MaxModels * FieldScriptControllerReader.ModelStride];

    public FakeFieldScriptMemory()
    {
        Array.Fill(priorities, (byte)0xFF);
        Array.Fill(modelIds, (byte)FieldScriptControllerReader.NoModel);
    }

    public byte Module { get; set; } = FieldPositionReader.FieldModule;
    public ushort FieldId { get; set; }
    public uint ScriptPointer { get; set; } = ScriptTableBase;
    public byte EntityCount { get; set; } = MaxEntities;

    /// <summary>Addresses that must fail to read, for torn-read coverage.</summary>
    public HashSet<uint> UnreadableAddresses { get; } = [];

    public void RunScript(int entityId, int scriptId, int priority = 2)
    {
        priorities[entityId] = (byte)priority;
        scriptIds[(entityId * FieldScriptControllerReader.ScriptSlotsPerEntity) + priority] = (byte)scriptId;
    }

    public void StopScripts(int entityId) => priorities[entityId] = 0xFF;

    /// <summary>Gives the entity a model and makes it visible, as VISI on does.</summary>
    public void GiveModel(int entityId, int modelId)
    {
        modelIds[entityId] = (byte)modelId;
        SetModelVisible(modelId, true);
    }

    public void SetModelVisible(int modelId, bool visible) =>
        models[(modelId * FieldScriptControllerReader.ModelStride) +
               FieldScriptControllerReader.ModelVisibilityOffset] = visible ? (byte)1 : (byte)0;

    /// <summary>
    /// Seeds a running animation in the native units: the current frame shifted left
    /// by four, and the end frame as a plain index, exactly as CANM!2 writes them.
    /// </summary>
    public void PlayAnimation(int modelId, int animationId, int currentFrame, int endFrame, int runState = 2)
    {
        var offset = modelId * FieldScriptControllerReader.ModelStride;
        models[offset + FieldScriptControllerReader.ModelAnimationIdOffset] = unchecked((byte)animationId);
        BinaryPrimitives.WriteInt16LittleEndian(
            models.AsSpan(offset + FieldScriptControllerReader.ModelCurrentFrameOffset),
            (short)FieldScriptControllerReader.ToFixedFrame(currentFrame));
        BinaryPrimitives.WriteInt16LittleEndian(
            models.AsSpan(offset + FieldScriptControllerReader.ModelEndFrameOffset),
            (short)endFrame);
        modelRunStates[modelId] = (byte)runState;
    }

    public void SetBankByte(int offset, byte value) => bank[offset] = value;

    public void SetBankWord(int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bank.AsSpan(offset), value);

    public void ClearBank() => Array.Clear(bank);

    public bool TryRead(uint virtualAddress, Span<byte> destination)
    {
        destination.Clear();
        if (destination.Length == 0 || UnreadableAddresses.Contains(virtualAddress))
        {
            return false;
        }

        switch (virtualAddress)
        {
            case (uint)FieldPositionReader.AddressCurrentModule when destination.Length == 1:
                destination[0] = Module;
                return true;
            case (uint)FieldPositionReader.AddressFieldId when destination.Length == 2:
                BinaryPrimitives.WriteUInt16LittleEndian(destination, FieldId);
                return true;
            case (uint)FieldScriptControllerReader.AddressFieldScriptPointer when destination.Length == 4:
                BinaryPrimitives.WriteUInt32LittleEndian(destination, ScriptPointer);
                return true;
            case (uint)FieldScriptControllerReader.AddressModelTablePointer when destination.Length == 4:
                BinaryPrimitives.WriteUInt32LittleEndian(destination, ModelTableBase);
                return true;
        }

        if (ScriptPointer != 0 &&
            virtualAddress == ScriptPointer + FieldScriptControllerReader.FieldScriptEntityCountOffset &&
            destination.Length == 1)
        {
            destination[0] = EntityCount;
            return true;
        }

        return TryReadFrom(
                   virtualAddress,
                   destination,
                   (uint)FieldScriptControllerReader.AddressEntityScriptPriorities,
                   priorities) ||
               TryReadFrom(
                   virtualAddress,
                   destination,
                   (uint)FieldScriptControllerReader.AddressEntityScriptIds,
                   scriptIds) ||
               TryReadFrom(
                   virtualAddress,
                   destination,
                   (uint)FieldScriptControllerReader.AddressEntityModelIds,
                   modelIds) ||
               TryReadFrom(
                   virtualAddress,
                   destination,
                   (uint)FieldScriptControllerReader.AddressModelAnimationRunState,
                   modelRunStates) ||
               TryReadFrom(virtualAddress, destination, ModelTableBase, models) ||
               TryReadFrom(
                   virtualAddress,
                   destination,
                   (uint)JunonMinigameStateReader.AddressTemporaryFieldBank,
                   bank);
    }

    private static bool TryReadFrom(uint address, Span<byte> destination, uint regionBase, byte[] region)
    {
        if (address < regionBase || address + (uint)destination.Length > regionBase + (uint)region.Length)
        {
            return false;
        }

        region.AsSpan((int)(address - regionBase), destination.Length).CopyTo(destination);
        return true;
    }
}
