using System.Text;

namespace WholeGameStateAudit;

/// <summary>
/// A field's script section (section 1) as the engine sees it: a header, the entity
/// names, the AKAO offsets, a pointer table of <see cref="SlotCount"/> script entry points
/// per entity, the code, and the dialogue table. Offsets are relative to the section.
///
/// <para>The engine enters a script at its pointer and runs until a return. Nothing in the
/// file says where a script ends, so no range is "the" script: <see cref="MakouRange"/> is
/// the reference editor's reading and <see cref="ShippingRange"/> the shipping catalog's,
/// and both are kept only so their disagreements with the control flow can be counted.
/// </para>
/// </summary>
internal sealed class ScriptSection
{
    public const ushort DemoVersion = 0x0301;

    private ScriptSection(byte[] data)
    {
        Data = data;
    }

    public byte[] Data { get; }

    public ushort Version { get; private set; }

    public int EntityCount { get; private set; }

    public int ModelCount { get; private set; }

    public int StringOffset { get; private set; }

    public int AkaoCount { get; private set; }

    public int Scale { get; private set; }

    public string Creator { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int SlotCount { get; private set; }

    public int NamesOffset { get; private set; }

    public int PointerTableOffset { get; private set; }

    public int CodeStart { get; private set; }

    /// <summary>Makou's end of scripts: the first AKAO block or the text table, whichever is first.</summary>
    public int CodeEnd { get; private set; }

    /// <summary>The shipping catalog's end: the same rule, but only a positive AKAO offset counts.</summary>
    public int ShippingCodeEnd { get; private set; }

    public IReadOnlyList<int> AkaoOffsets { get; private set; } = [];

    public IReadOnlyList<ScriptEntity> Entities { get; private set; } = [];

    public int DialogCount { get; private set; }

    public List<string> Issues { get; } = [];

    public static ScriptSection? TryParse(byte[] data, out string diagnostic)
    {
        diagnostic = string.Empty;
        if (data.Length < 32)
        {
            diagnostic = $"script section is {data.Length} bytes";
            return null;
        }

        var section = new ScriptSection(data)
        {
            Version = BitConverter.ToUInt16(data, 0),
            EntityCount = data[2],
            ModelCount = data[3],
            StringOffset = BitConverter.ToUInt16(data, 4),
            AkaoCount = BitConverter.ToUInt16(data, 6)
        };
        var isDemo = section.Version == DemoVersion;
        section.SlotCount = isDemo ? 16 : 32;
        section.Scale = isDemo ? 0 : BitConverter.ToUInt16(data, 8);
        var creatorOffset = isDemo ? 8 : 16;
        section.Creator = ReadAscii(data, creatorOffset, 8);
        section.Name = ReadAscii(data, creatorOffset + 8, 8);
        section.NamesOffset = creatorOffset + 16;
        var akaoTable = section.NamesOffset + section.EntityCount * 8;
        section.PointerTableOffset = akaoTable + section.AkaoCount * 4;
        section.CodeStart = section.PointerTableOffset + section.EntityCount * section.SlotCount * 2;
        if (section.Version != 0x0502)
        {
            section.Issues.Add($"version 0x{section.Version:X4}");
        }

        if (section.StringOffset > data.Length || section.StringOffset < 32 ||
            section.CodeStart > section.StringOffset)
        {
            diagnostic = $"pointer table {section.PointerTableOffset}..{section.CodeStart} does not fit before text {section.StringOffset} in {data.Length} bytes";
            return null;
        }

        var akao = new List<int>(section.AkaoCount);
        for (var index = 0; index < section.AkaoCount; index++)
        {
            akao.Add(BitConverter.ToInt32(data, akaoTable + index * 4));
        }

        section.AkaoOffsets = akao;
        var makouAkao = section.AkaoCount > 0 ? (long)(uint)akao[0] : data.Length;
        section.CodeEnd = (int)Math.Min(makouAkao, section.StringOffset);
        section.ShippingCodeEnd = section.AkaoCount > 0 && akao[0] > 0
            ? Math.Min(section.StringOffset, akao[0])
            : section.StringOffset;
        if (section.CodeEnd != section.ShippingCodeEnd)
        {
            section.Issues.Add($"code end differs: Makou {section.CodeEnd}, shipping {section.ShippingCodeEnd}");
        }

        if (section.StringOffset + 2 <= data.Length)
        {
            section.DialogCount = BitConverter.ToUInt16(data, section.StringOffset);
        }

        var entities = new List<ScriptEntity>(section.EntityCount);
        for (var entity = 0; entity < section.EntityCount; entity++)
        {
            var pointers = new int[section.SlotCount];
            for (var slot = 0; slot < section.SlotCount; slot++)
            {
                pointers[slot] = BitConverter.ToUInt16(
                    data,
                    section.PointerTableOffset + (entity * section.SlotCount + slot) * 2);
            }

            entities.Add(new ScriptEntity(entity, ReadAscii(data, section.NamesOffset + entity * 8, 8), pointers));
            for (var slot = 1; slot < section.SlotCount; slot++)
            {
                if (pointers[slot] < pointers[slot - 1])
                {
                    section.Issues.Add($"entity {entity} slot {slot} pointer {pointers[slot]} is before slot {slot - 1}'s {pointers[slot - 1]}");
                    break;
                }
            }
        }

        section.Entities = entities;
        section.ComputeRanges();
        return section;
    }

    public bool IsInCode(int offset) => offset >= CodeStart && offset < CodeEnd;

    /// <summary>
    /// Every (entity, slot) whose pointer is <paramref name="offset"/>, lowest first.
    /// </summary>
    public IReadOnlyList<(int Entity, int Slot)> EntriesAt(int offset) =>
        entriesByOffset.TryGetValue(offset, out var entries) ? entries : [];

    public IReadOnlyCollection<int> EntryOffsets => entriesByOffset.Keys;

    private readonly Dictionary<int, List<(int Entity, int Slot)>> entriesByOffset = new();

    private void ComputeRanges()
    {
        foreach (var entity in Entities)
        {
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var pointer = entity.Pointers[slot];
                if (!entriesByOffset.TryGetValue(pointer, out var list))
                {
                    list = [];
                    entriesByOffset[pointer] = list;
                }

                list.Add((entity.Index, slot));
            }
        }

        // Shipping: every distinct in-range pointer is a boundary; each entity keeps only
        // the lowest slot of each distinct pointer, and that slot runs to the next boundary.
        var boundaries = new SortedSet<int>();
        foreach (var entity in Entities)
        {
            foreach (var pointer in entity.Pointers)
            {
                if (pointer >= CodeStart && pointer < ShippingCodeEnd)
                {
                    boundaries.Add(pointer);
                }
            }
        }

        boundaries.Add(ShippingCodeEnd);
        var ordered = boundaries.ToArray();
        foreach (var entity in Entities)
        {
            var firstSlotByPointer = new Dictionary<int, int>();
            for (var slot = 0; slot < SlotCount; slot++)
            {
                firstSlotByPointer.TryAdd(entity.Pointers[slot], slot);
            }

            for (var slot = 0; slot < SlotCount; slot++)
            {
                var pointer = entity.Pointers[slot];
                var owner = firstSlotByPointer[pointer];
                entity.Slots[slot].AliasOf = owner == slot ? null : owner;
                if (owner != slot)
                {
                    continue;
                }

                var index = Array.BinarySearch(ordered, pointer);
                if (index >= 0 && index + 1 < ordered.Length && ordered[index + 1] > pointer &&
                    ordered[index + 1] <= Data.Length)
                {
                    entity.Slots[slot].ShippingRange = (pointer, ordered[index + 1]);
                }
            }
        }

        // Makou: each entity's slot j runs to slot j+1's pointer when that is later, the last
        // slot to the next non-empty entity's first pointer, and an entity whose pointers do
        // not pass the previous one's last pointer is an empty group with no scripts at all.
        var emptyGroups = 0;
        for (var entityIndex = 0; entityIndex < Entities.Count; entityIndex++)
        {
            var entity = Entities[entityIndex];
            if (emptyGroups > 1)
            {
                emptyGroups--;
                entity.MakouEmptyGroup = true;
                continue;
            }

            var positions = new int[SlotCount + 1];
            Array.Copy(entity.Pointers, positions, SlotCount);
            if (entityIndex == Entities.Count - 1)
            {
                positions[SlotCount] = CodeEnd;
            }
            else
            {
                var next = Entities[entityIndex + 1].Pointers[0];
                if (next > positions[SlotCount - 1])
                {
                    positions[SlotCount] = next;
                }
                else
                {
                    emptyGroups = 1;
                    while (next <= positions[SlotCount - 1] && entityIndex + emptyGroups < Entities.Count - 1)
                    {
                        next = Entities[entityIndex + emptyGroups + 1].Pointers[0];
                        emptyGroups++;
                    }

                    positions[SlotCount] = entityIndex + emptyGroups == Entities.Count ? CodeEnd : next;
                }
            }

            var previousDefined = -1;
            for (var slot = 0; slot < SlotCount; slot++)
            {
                if (positions[slot + 1] <= positions[slot])
                {
                    continue;
                }

                // Makou files the code under the slot after the previous defined one, which
                // is the first slot of a run of equal pointers.
                var makouSlot = previousDefined + 1;
                entity.Slots[makouSlot].MakouRange = (positions[slot], positions[slot + 1]);
                previousDefined = slot;
            }
        }
    }

    public static string ReadAscii(byte[] bytes, int offset, int maxLength)
    {
        if (offset < 0 || offset >= bytes.Length)
        {
            return string.Empty;
        }

        var length = 0;
        while (length < maxLength && offset + length < bytes.Length && bytes[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(bytes, offset, length).Trim();
    }
}

internal sealed class ScriptEntity
{
    public ScriptEntity(int index, string name, int[] pointers)
    {
        Index = index;
        Name = name;
        Pointers = pointers;
        Slots = pointers.Select((pointer, slot) => new ScriptSlot(slot, pointer)).ToArray();
    }

    public int Index { get; }

    public string Name { get; }

    public int[] Pointers { get; }

    public ScriptSlot[] Slots { get; }

    public bool MakouEmptyGroup { get; set; }
}

internal sealed class ScriptSlot
{
    public ScriptSlot(int index, int pointer)
    {
        Index = index;
        Pointer = pointer;
    }

    public int Index { get; }

    public int Pointer { get; }

    /// <summary>The lowest slot of this entity that has the same pointer, when that is not this one.</summary>
    public int? AliasOf { get; set; }

    public (int Start, int End)? ShippingRange { get; set; }

    public (int Start, int End)? MakouRange { get; set; }
}
