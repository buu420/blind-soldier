using System.Security.Cryptography;
using Ff7.Accessibility.Reloaded;

namespace LocalizedDescriptionCueAligner;

/// <summary>One native instruction of one entity script slot, at its runtime byte index.</summary>
internal sealed record NativeInstruction(int ByteIndex, byte Opcode, byte[] Bytes)
{
    public string Hex => Convert.ToHexString(Bytes);
}

/// <summary>
/// One installed field read the way the runtime identifies and observes it: the script
/// prefix hash over section 1 through the text offset (LoadedFieldScriptIdentityReader),
/// the 8-byte entity names, the 32-slot pointer table, and each slot's instructions with
/// the byte index FieldScriptContextReader reports (absolute position minus the slot's base).
/// </summary>
internal sealed class NativeField
{
    private const int SlotCount = 32;
    private readonly Dictionary<(int Entity, int Slot), IReadOnlyList<NativeInstruction>> slots = new();

    private NativeField(int fieldId, string name, string prefixSha256, string[] entityNames, int[][] pointers)
    {
        FieldId = fieldId;
        Name = name;
        PrefixSha256 = prefixSha256;
        EntityNames = entityNames;
        Pointers = pointers;
    }

    public int FieldId { get; }
    public string Name { get; }
    public string PrefixSha256 { get; }
    public string[] EntityNames { get; }
    public int[][] Pointers { get; }
    public int EntityCount => EntityNames.Length;

    public IReadOnlyList<NativeInstruction>? Slot(int entity, int slot) =>
        slots.TryGetValue((entity, slot), out var list) ? list : null;

    /// <summary>The slots of <paramref name="entity"/> whose pointer equals that of <paramref name="slot"/>.</summary>
    public string SlotAliases(int entity, int slot) =>
        string.Join('|', Enumerable.Range(0, SlotCount).Where(s => Pointers[entity][s] == Pointers[entity][slot]));

    public static NativeField? TryRead(FlevelDataSource data, FieldScriptNavigationCatalog scripts, int fieldId)
    {
        if (!data.FieldNames.TryGetValue(fieldId, out var name) || !data.TryReadField(fieldId, out var encoded))
            return null;
        var file = Ff7LzsDecoder.DecodeFieldFile(encoded);
        if (file.Length < 14) return null;
        var sectionOffset = BitConverter.ToInt32(file, 6);
        if (sectionOffset < 0 || sectionOffset > file.Length - 4) return null;
        var sectionLength = BitConverter.ToInt32(file, sectionOffset);
        var dataOffset = sectionOffset + 4;
        if (sectionLength < 32 || dataOffset > file.Length - sectionLength) return null;
        var section = file.AsSpan(dataOffset, sectionLength).ToArray();
        var textOffset = BitConverter.ToUInt16(section, 4);
        if (textOffset < 32 || textOffset > section.Length) return null;
        var prefix = Convert.ToHexString(SHA256.HashData(section.AsSpan(0, textOffset)));

        var version = BitConverter.ToUInt16(section, 0);
        var entityCount = section[2];
        var akaoCount = BitConverter.ToUInt16(section, 6);
        var tableSlots = version == 0x0301 ? 16 : SlotCount;
        var namesOffset = (version == 0x0301 ? 8 : 16) + 16;
        var pointerTable = namesOffset + entityCount * 8 + akaoCount * 4;
        if (entityCount == 0 || pointerTable + entityCount * tableSlots * 2 > textOffset) return null;
        var names = new string[entityCount];
        var pointers = new int[entityCount][];
        for (var entity = 0; entity < entityCount; entity++)
        {
            var raw = section.AsSpan(namesOffset + entity * 8, 8);
            var length = raw.IndexOf((byte)0);
            names[entity] = System.Text.Encoding.ASCII.GetString(raw[..(length < 0 ? 8 : length)]).Trim();
            pointers[entity] = new int[SlotCount];
            for (var slot = 0; slot < SlotCount; slot++)
                pointers[entity][slot] = slot < tableSlots
                    ? BitConverter.ToUInt16(section, pointerTable + (entity * tableSlots + slot) * 2)
                    : -1;
        }

        var field = new NativeField(fieldId, name, prefix, names, pointers);
        // The production listing (FieldScriptNavigationCatalog) decodes each distinct slot start
        // once with the engine's opcode lengths and names it by the first slot holding that start.
        // A later slot with the same pointer runs the same bytes from the same base, so the
        // runtime byte index is identical; alias it explicitly.
        var listed = scripts.ReadAllScriptOpcodes(fieldId);
        var byEntity = listed.GroupBy(s => s.EntityId).ToDictionary(g => g.Key, g => g.ToDictionary(s => s.ScriptId));
        for (var entity = 0; entity < entityCount; entity++)
        {
            if (!byEntity.TryGetValue(entity, out var entityScripts)) continue;
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var pointer = pointers[entity][slot];
                if (pointer < 0) continue;
                var first = Enumerable.Range(0, slot + 1).First(s => pointers[entity][s] == pointer);
                if (!entityScripts.TryGetValue(first, out var definition)) continue;
                field.slots[(entity, slot)] = definition.Opcodes
                    .Select(o => new NativeInstruction(o.ByteIndex, o.Opcode, o.Bytes.ToArray()))
                    .ToArray();
            }
        }

        return field;
    }
}
