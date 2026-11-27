using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace RussianFieldAudit;

/// <summary>
/// The legacy address space as the field module leaves it after loading a field: the module and
/// field id words and a script pointer to section 1. Used to run the production loaded-field path
/// (LoadedFieldScriptIdentityReader, FieldScriptNavigationCatalog.ReadField) on another archive's
/// section 1 while the catalog reads its model metadata from the installed disk archive, which is
/// how 7th Heaven presents an overlaid flevel to the mod.
/// </summary>
internal sealed class LoadedScriptMemory : ILegacyAddressSpace
{
    private const uint Script = 0x02000000;
    private readonly Dictionary<uint, byte> bytes = new();

    public void Load(int fieldId, byte[] scriptSection)
    {
        bytes.Clear();
        for (var index = 0; index < scriptSection.Length; index++) bytes[Script + (uint)index] = scriptSection[index];
        bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 1;
        Span<byte> word = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)fieldId);
        Write((uint)FieldScriptContextReader.AddressCurrentFieldId, word);
        Span<byte> pointer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(pointer, Script);
        Write((uint)FieldScriptContextReader.AddressFieldScriptPtr, pointer);
    }

    private void Write(uint address, ReadOnlySpan<byte> data)
    {
        for (var index = 0; index < data.Length; index++) bytes[address + (uint)index] = data[index];
    }

    public bool TryRead(uint virtualAddress, Span<byte> destination)
    {
        for (var index = 0; index < destination.Length; index++)
            if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index])) return false;
        return true;
    }
}
