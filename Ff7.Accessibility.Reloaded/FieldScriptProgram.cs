namespace Ff7.Accessibility.Reloaded;

/// <summary>One decoded field opcode at an absolute offset in the script section.</summary>
internal readonly record struct FieldScriptInstruction(byte Id, int Offset, byte[] Bytes)
{
    public int Next => Offset + Bytes.Length;
}

/// <summary>
/// A byte of a field bank the scripts address: a savemap or temporary block and the byte
/// offset in it. Bank nibbles 1/2, 3/4, 5/6, 11/12, 13/14 and 15/7 are one block each, so
/// both nibbles of a pair give the same key.
/// </summary>
internal readonly record struct FieldBankByte(int Block, int Address)
{
    public static int BlockOf(int bank) => bank switch
    {
        1 or 2 => 1,
        3 or 4 => 3,
        5 or 6 => 5,
        11 or 12 => 11,
        13 or 14 => 13,
        15 or 7 => 15,
        _ => 0
    };

    /// <summary>The nibbles whose word helper writes two bytes.</summary>
    public static bool IsWordBank(int bank) => bank is 2 or 4 or 6 or 12 or 14 or 7;
}

/// <summary>
/// A test on a savemap byte (or word) that decides whether a MAPJUMP runs, as the native
/// comparators make it (IFUB 006117CB on bytes, IFSW 00611BAE on signed words, IFUW 00611F40
/// on unsigned ones): <paramref name="Operator"/> 0-5 compare, 6-8 are &amp;, ^ and | on bytes,
/// 9 and 10 test bit <c>Value &amp; 31</c> of the byte. <paramref name="Holds"/> says which side
/// the MAPJUMP is on. The byte is <paramref name="Address"/> in savemap block
/// <paramref name="Block"/> (1, 3, 11, 13 or 15), and a word runs on into the next byte. An
/// IFSW constant is kept as the signed word it is.
/// </summary>
public readonly record struct FieldScriptGuardTest(
    int Block,
    int Address,
    bool Word,
    bool Signed,
    int Operator,
    int Value,
    bool Holds)
{
    /// <summary>Whether <paramref name="left"/> (the live byte or word) meets the test.</summary>
    public bool IsMetBy(int left)
    {
        // A byte bank reads a byte even for a word test (0060FD6C), zero-extended; the constant
        // is the instruction's word, signed for IFSW.
        if (Word && Signed)
        {
            left = (short)left;
        }

        var right = Signed ? (short)Value : Value;
        var result = Operator switch
        {
            0 => left == right,
            1 => left != right,
            2 => left > right,
            3 => left < right,
            4 => left >= right,
            5 => left <= right,
            6 => ((left & right) & 0xFF) != 0,
            7 => ((left ^ right) & 0xFF) != 0,
            8 => ((left | right) & 0xFF) != 0,
            9 => ((left & 0xFF) & (1 << (right & 31))) != 0,
            10 => ((left & 0xFF) & (1 << (right & 31))) == 0,
            _ => true
        };
        return result == Holds;
    }

    public string Key => $"{Block}:{Address}:{(Word ? Signed ? "sw" : "uw" : "b")}:{Operator}:{Value}:{Holds}";
}

/// <summary>
/// A field's script section read the way the PC engine executes it.
///
/// <para>Entry points are the pointer table itself: the event dispatcher (0060D29B) and
/// RETTO (00612E47) both load <c>entity * 0x40 + slot * 2</c> and never compare one slot's
/// pointer with another's, so a slot that repeats a neighbour's pointer runs that code.
/// Code is not divided into per-slot ranges; control flow is followed wherever it goes in
/// the code area.</para>
///
/// <para>Opcode lengths are the engine's (SPECIAL's sub-opcodes from 0061E78C), branch
/// targets are the handlers' (JMPFL 00613141: IP + word + 1; the byte, word, key and party
/// tests fork), RET and RETTO end a path, and a handler PC does not implement stops it: the
/// dispatcher's unimplemented slot 006107E1 returns without advancing.</para>
///
/// <para>Reach is cached per entry offset, so the many slots that share one pointer cost a
/// single walk.</para>
/// </summary>
internal sealed class FieldScriptProgram
{
    public const int SlotCount = 32;
    private const ushort DemoVersion = 0x0301;

    // PC field opcode lengths by opcode; SPECIAL, KAWAI and 0x1C are adjusted below.
    private static readonly byte[] BaseLengths =
    [
        1,3,3,3,3,3,3,2,2,15,6,6,1,1,2,2,
        2,3,2,3,6,7,8,9,8,9,10,3,6,1,1,1,
        11,2,5,3,3,9,2,2,3,1,2,2,5,7,2,10,
        4,4,4,2,2,4,5,8,6,6,6,4,1,1,1,1,
        3,5,6,2,1,5,1,5,7,4,2,2,1,5,1,5,
        10,6,4,2,2,3,7,7,5,5,5,7,8,10,8,1,
        10,2,5,6,6,1,9,1,9,2,7,9,1,4,3,6,
        4,2,3,4,4,8,4,5,4,5,3,3,3,3,2,3,
        4,5,4,4,4,4,5,4,5,4,5,4,5,4,5,4,
        5,4,5,4,5,3,3,3,3,3,4,5,6,7,7,11,
        2,2,3,3,2,11,9,9,6,6,2,4,1,6,3,3,
        5,5,4,3,6,6,2,4,5,4,3,5,5,4,1,2,
        11,8,15,12,1,3,3,2,2,2,4,3,3,3,2,2,
        13,2,2,16,10,10,4,4,3,1,15,2,4,1,1,11,
        4,4,3,3,3,5,5,5,7,10,10,5,5,8,8,11,
        2,5,14,2,2,2,2,4,2,1,3,2,2,8,3,1
    ];

    /// <summary>
    /// Dispatcher slots that point at the unimplemented handler 006107E1 on PC (0x1A-0x1C
    /// included, whatever community metadata calls them). A script that reaches one stops.
    /// </summary>
    private static readonly HashSet<byte> UnimplementedOpcodes =
        [0x0C, 0x0D, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x44, 0x46, 0x4C, 0x4E, 0xBE];

    /// <summary>
    /// Every fixed bank destination a native handler writes, from the handlers' direct calls
    /// to the byte writer 0060FA7D and the word writer 0061031E: (nibble, operand offset,
    /// word helper). The word helper writes two bytes only on a word bank. GTPYE's loop,
    /// SPECIAL F7 and the string SPECIAL F6 are handled in <see cref="Writes"/>.
    /// </summary>
    private static readonly Dictionary<byte, (int Nibble, int Offset, bool Word)[]> NativeWrites = new()
    {
        [0x23] = [(2, 2, true)], [0x3B] = [(1, 2, true), (2, 3, true)], [0x48] = [(2, 6, false)],
        [0x56] = [(2, 4, false), (3, 5, false), (4, 6, false)], [0x5A] = [(2, 4, false)], [0x5D] = [(6, 9, false)],
        [0x6E] = [(2, 2, true)], [0x73] = [(2, 3, false)], [0x74] = [(2, 3, false)],
        [0x75] = [(1, 4, true), (2, 5, true), (3, 6, true), (4, 7, true)],
        [0x76] = [(1, 2, false)], [0x77] = [(1, 2, true)], [0x78] = [(1, 2, false)], [0x79] = [(1, 2, true)],
        [0x7A] = [(2, 2, false)], [0x7B] = [(2, 2, true)], [0x7C] = [(2, 2, false)], [0x7D] = [(2, 2, true)],
        [0x80] = [(1, 2, false)], [0x81] = [(1, 2, true)], [0x82] = [(1, 2, false)], [0x83] = [(1, 2, false)],
        [0x84] = [(1, 2, false)], [0x85] = [(1, 2, false)], [0x86] = [(1, 2, true)], [0x87] = [(1, 2, false)],
        [0x88] = [(1, 2, true)], [0x89] = [(1, 2, false)], [0x8A] = [(1, 2, true)], [0x8B] = [(1, 2, false)],
        [0x8C] = [(1, 2, true)], [0x8D] = [(1, 2, false)], [0x8E] = [(1, 2, true)], [0x8F] = [(1, 2, false)],
        [0x90] = [(1, 2, true)], [0x91] = [(1, 2, false)], [0x92] = [(1, 2, true)], [0x93] = [(1, 2, false)],
        [0x94] = [(1, 2, true)], [0x95] = [(2, 2, false)], [0x96] = [(2, 2, true)], [0x97] = [(2, 2, false)],
        [0x98] = [(2, 2, true)], [0x99] = [(2, 2, false)], [0x9A] = [(1, 2, false)], [0x9B] = [(1, 2, false)],
        [0x9C] = [(1, 3, true)], [0x9E] = [(4, 5, false)], [0x9F] = [(6, 10, true)], [0xB7] = [(2, 3, false)],
        [0xB8] = [(1, 3, true), (2, 4, true)], [0xB9] = [(2, 3, true)],
        [0xC1] = [(1, 4, true), (2, 5, true), (3, 6, true), (4, 7, true)],
        [0xD4] = [(4, 9, true)], [0xD5] = [(4, 9, true)], [0xF7] = [(1, 2, true), (2, 3, false)],
        [0xFA] = [(2, 2, true)], [0xFE] = [(2, 2, false)]
    };

    /// <summary>
    /// Bank bytes handlers write directly rather than through the byte and word writers, from
    /// root's scan of every handler and two call levels (native-direct-bank-reference-review):
    /// (block, first byte, last byte). The party in field bank 3[9..11] is 00DC09E5: PRTYP
    /// 0061BE95, PRTYM 0061C045 and PRTYE 0061C113 (through 0061C26A) set it, and MENU 0061F04A
    /// brings it back into line with the menu's party (0061C577) when the menu returns. DSKCG
    /// 006140BF sets 13[0]; SMTRA 0061EDF8 sets 13[31] and, through 006CC0EA, 1[75]; the HP and MP
    /// opcodes all go through 005CB127, which sets and clears bit 7 of 1[122]; MPNAM 00633691 copies
    /// the room name, with its two-byte control codes and closing FF, into 13[104..128].
    /// MMBud and SPECIAL F9 depend on their operands and are handled in <see cref="Writes"/>.
    /// </summary>
    private static readonly Dictionary<byte, (int Block, int First, int Last)[]> DirectWrites = new()
    {
        [0x0E] = [(13, 0, 0)],
        [0x3C] = [(1, 122, 122)], [0x3D] = [(1, 122, 122)], [0x3F] = [(1, 122, 122)],
        [0x45] = [(1, 122, 122)], [0x47] = [(1, 122, 122)], [0x4D] = [(1, 122, 122)], [0x4F] = [(1, 122, 122)],
        [0x43] = [(13, 104, 128)],
        [0x49] = [(3, 9, 11)],
        [0x5B] = [(13, 31, 31), (1, 75, 75)],
        [0xC8] = [(3, 9, 11)], [0xC9] = [(3, 9, 11)], [0xCA] = [(3, 9, 11)]
    };

    private readonly byte[] section;
    private readonly Dictionary<int, FieldScriptInstruction?> decoded = new();
    private readonly Dictionary<int, IReadOnlyList<FieldScriptInstruction>> reachByEntry = new();
    private readonly Dictionary<int, IReadOnlyList<int>> initReturnsByEntity = new();
    private readonly Dictionary<int, IReadOnlyList<FieldScriptInstruction>> initAndMainByEntity = new();
    private readonly int[][] pointers;
    private Dictionary<FieldBankByte, List<int>>? writersByByte;
    private Dictionary<int, List<int>>? unnamedWritersByBlock;
    private HashSet<int>? concurrency;
    private HashSet<int>? eventReach;
    private HashSet<int>? joinPoints;
    private HashSet<int>? controllable;
    private bool?[]? partyCharacters;
    private IReadOnlyList<int>?[]? partyCharacterLists;

    private FieldScriptProgram(byte[] section, int codeStart, int codeEnd, string[] names, int[][] pointers)
    {
        this.section = section;
        CodeStart = codeStart;
        CodeEnd = codeEnd;
        EntityNames = names;
        this.pointers = pointers;
    }

    public int CodeStart { get; }

    public int CodeEnd { get; }

    public IReadOnlyList<string> EntityNames { get; }

    public int EntityCount => pointers.Length;

    public byte[] Section => section;

    public static FieldScriptProgram? TryParse(byte[] section, out string diagnostic)
    {
        diagnostic = "invalid script header";
        if (section.Length < 32)
        {
            return null;
        }

        var version = BitConverter.ToUInt16(section, 0);
        var entityCount = section[2];
        var textOffset = BitConverter.ToUInt16(section, 4);
        var akaoCount = BitConverter.ToUInt16(section, 6);
        var slotCount = version == DemoVersion ? 16 : SlotCount;
        var namesOffset = (version == DemoVersion ? 8 : 16) + 16;
        var pointerTable = namesOffset + entityCount * 8 + akaoCount * 4;
        var codeStart = pointerTable + entityCount * slotCount * 2;
        if (entityCount == 0 || textOffset < 32 || textOffset > section.Length || codeStart > textOffset)
        {
            return null;
        }

        var codeEnd = (int)textOffset;
        if (akaoCount > 0)
        {
            var firstAkao = BitConverter.ToInt32(section, namesOffset + entityCount * 8);
            if (firstAkao > 0)
            {
                codeEnd = Math.Min(codeEnd, firstAkao);
            }
        }

        var names = new string[entityCount];
        var table = new int[entityCount][];
        for (var entity = 0; entity < entityCount; entity++)
        {
            names[entity] = ReadName(section, namesOffset + entity * 8);
            table[entity] = new int[SlotCount];
            for (var slot = 0; slot < SlotCount; slot++)
            {
                // A 16-slot table leaves the upper half of each entity with no entry.
                table[entity][slot] = slot < slotCount
                    ? BitConverter.ToUInt16(section, pointerTable + (entity * slotCount + slot) * 2)
                    : -1;
            }
        }

        diagnostic = $"entities={entityCount}, code={codeStart}..{codeEnd}";
        return new FieldScriptProgram(section, codeStart, codeEnd, names, table);
    }

    /// <summary>The pointer of a slot, or null when the slot does not exist or points outside the code.</summary>
    public int? Pointer(int entity, int slot)
    {
        if (entity < 0 || entity >= pointers.Length || slot < 0 || slot >= SlotCount)
        {
            return null;
        }

        var pointer = pointers[entity][slot];
        return pointer >= CodeStart && pointer < CodeEnd ? pointer : null;
    }

    /// <summary>
    /// Whether an engine event for this slot would run anything: the dispatcher skips an
    /// entry whose first byte is RET.
    /// </summary>
    public bool Runs(int entity, int slot) =>
        Pointer(entity, slot) is { } pointer && section[pointer] != 0x00;

    /// <summary>The lowest slot of the entity that shares this slot's pointer.</summary>
    public int CanonicalSlot(int entity, int slot)
    {
        if (Pointer(entity, slot) is not { } pointer)
        {
            return slot;
        }

        for (var candidate = 0; candidate < slot; candidate++)
        {
            if (pointers[entity][candidate] == pointer)
            {
                return candidate;
            }
        }

        return slot;
    }

    /// <summary>Each distinct in-code pointer of an entity, with every slot that uses it.</summary>
    public IEnumerable<(int Pointer, IReadOnlyList<int> Slots)> DistinctEntries(int entity)
    {
        var seen = new Dictionary<int, List<int>>();
        var order = new List<int>();
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (Pointer(entity, slot) is not { } pointer)
            {
                continue;
            }

            if (!seen.TryGetValue(pointer, out var slots))
            {
                slots = [];
                seen[pointer] = slots;
                order.Add(pointer);
            }

            slots.Add(slot);
        }

        foreach (var pointer in order)
        {
            yield return (pointer, seen[pointer]);
        }
    }

    public bool TryGetInstruction(int offset, out FieldScriptInstruction instruction)
    {
        if (!decoded.TryGetValue(offset, out var cached))
        {
            cached = Decode(offset);
            decoded[offset] = cached;
        }

        instruction = cached ?? default;
        return cached is not null;
    }

    private FieldScriptInstruction? Decode(int offset)
    {
        if (offset < CodeStart || offset >= CodeEnd)
        {
            return null;
        }

        var length = Length(section, offset, CodeEnd);
        if (length <= 0 || offset + length > CodeEnd)
        {
            return null;
        }

        return new FieldScriptInstruction(section[offset], offset, section.AsSpan(offset, length).ToArray());
    }

    /// <summary>The engine's length of the opcode at <paramref name="offset"/>, or 0 when it does not fit.</summary>
    public static int Length(byte[] code, int offset, int end)
    {
        if (offset < 0 || offset >= end)
        {
            return 0;
        }

        var opcode = code[offset];
        int length = BaseLengths[opcode];
        switch (opcode)
        {
            case 0x1C:
                if (end - offset < 6)
                {
                    return 0;
                }

                length += Math.Min(code[offset + 5], (byte)128);
                break;
            case 0x28:
                if (end - offset < 2)
                {
                    return 0;
                }

                length = Math.Max(1, (int)code[offset + 1]);
                break;
            case 0x0F:
                if (end - offset < 2)
                {
                    return 0;
                }

                // Native 0061E78C: F5=3, F6=6, F7=4, F8=4, F9=2, FA=2, FB=3, FC=3, FD=4,
                // FE=2, FF=2, and every other sub-opcode 2.
                length = code[offset + 1] switch
                {
                    0xF5 or 0xFB or 0xFC => 3,
                    0xF6 => 6,
                    0xF7 or 0xF8 or 0xFD => 4,
                    _ => 2
                };
                break;
        }

        return length;
    }

    /// <summary>Whether the handler PC dispatches to leaves the script stopped at this opcode.</summary>
    public static bool Stops(byte opcode) =>
        opcode is 0x00 or 0x07 or 0xFF || UnimplementedOpcodes.Contains(opcode);

    public static bool IsUnimplemented(byte opcode) => UnimplementedOpcodes.Contains(opcode);

    /// <summary>
    /// Where execution can go after <paramref name="instruction"/>: fall-through, the taken
    /// side of a test, or a jump. RET, RETTO, GAMEOVER and unimplemented handlers have none.
    /// </summary>
    public void Successors(FieldScriptInstruction instruction, List<int> successors)
    {
        successors.Clear();
        var bytes = instruction.Bytes;
        var at = instruction.Offset;
        switch (instruction.Id)
        {
            case var stop when Stops(stop):
                return;
            case 0x10:
                Add(at + 1 + bytes[1]);
                return;
            case 0x11:
                Add(at + 1 + BitConverter.ToUInt16(bytes, 1));
                return;
            case 0x12:
                Add(at - bytes[1]);
                return;
            case 0x13:
                Add(at - BitConverter.ToUInt16(bytes, 1));
                return;
            case 0x14:
                Add(instruction.Next);
                Add(at + 5 + bytes[5]);
                return;
            case 0x15:
                Add(instruction.Next);
                Add(at + 5 + BitConverter.ToUInt16(bytes, 5));
                return;
            case 0x16 or 0x18:
                Add(instruction.Next);
                Add(at + 7 + bytes[7]);
                return;
            case 0x17 or 0x19:
                Add(instruction.Next);
                Add(at + 7 + BitConverter.ToUInt16(bytes, 7));
                return;
            case >= 0x30 and <= 0x32:
                Add(instruction.Next);
                Add(at + 3 + bytes[3]);
                return;
            case 0xCB or 0xCC:
                Add(instruction.Next);
                Add(at + 2 + bytes[2]);
                return;
            default:
                Add(instruction.Next);
                return;
        }

        void Add(int target)
        {
            if (target >= CodeStart && target < CodeEnd && !successors.Contains(target))
            {
                successors.Add(target);
            }
        }
    }

    /// <summary>
    /// The false (taken) target of a conditional, measured as its handler measures it, or
    /// null for anything that is not a conditional.
    /// </summary>
    public static int? FalseTarget(FieldScriptInstruction instruction)
    {
        var bytes = instruction.Bytes;
        var at = instruction.Offset;
        return instruction.Id switch
        {
            0x14 => at + 5 + bytes[5],
            0x15 => at + 5 + BitConverter.ToUInt16(bytes, 5),
            0x16 or 0x18 => at + 7 + bytes[7],
            0x17 or 0x19 => at + 7 + BitConverter.ToUInt16(bytes, 7),
            >= 0x30 and <= 0x32 => at + 3 + bytes[3],
            0xCB or 0xCC => at + 2 + bytes[2],
            _ => null
        };
    }

    /// <summary>Every instruction reachable from <paramref name="entry"/> by control flow, in offset order.</summary>
    public IReadOnlyList<FieldScriptInstruction> Reach(int entry)
    {
        if (reachByEntry.TryGetValue(entry, out var cached))
        {
            return cached;
        }

        var seen = new HashSet<int>();
        var result = new List<FieldScriptInstruction>();
        var pending = new Stack<int>();
        var next = new List<int>(2);
        pending.Push(entry);
        while (pending.Count != 0)
        {
            var offset = pending.Pop();
            if (!seen.Add(offset) || !TryGetInstruction(offset, out var instruction))
            {
                continue;
            }

            result.Add(instruction);
            Successors(instruction, next);
            foreach (var successor in next)
            {
                pending.Push(successor);
            }
        }

        // Offset order from the entry on, then anything the script jumps back to: the same
        // order a read of the script's own range gives, with the code it reaches elsewhere
        // after it.
        result.Sort((left, right) =>
        {
            var leftBefore = left.Offset < entry;
            var rightBefore = right.Offset < entry;
            return leftBefore != rightBefore ? leftBefore.CompareTo(rightBefore) : left.Offset.CompareTo(right.Offset);
        });
        reachByEntry[entry] = result;
        return result;
    }

    /// <summary>
    /// Whether execution can arrive at <paramref name="offset"/> other than by falling
    /// through from the opcode before it: a jump or branch target, a slot's entry or a Main's
    /// start. Falling through only ever moves forward, so every loop passes through one.
    /// </summary>
    public bool IsJoinPoint(int offset)
    {
        if (joinPoints is null)
        {
            var points = new HashSet<int>();
            var successors = new List<int>(2);
            for (var entity = 0; entity < EntityCount; entity++)
            {
                for (var slot = 0; slot < SlotCount; slot++)
                {
                    if (Pointer(entity, slot) is { } pointer)
                    {
                        points.Add(pointer);
                    }
                }

                foreach (var ret in InitReturns(entity))
                {
                    points.Add(ret + 1);
                }
            }

            foreach (var instruction in LiveInstructions())
            {
                Successors(instruction, successors);
                foreach (var successor in successors)
                {
                    if (successor != instruction.Next)
                    {
                        points.Add(successor);
                    }
                }
            }

            joinPoints = points;
        }

        return joinPoints.Contains(offset);
    }

    public IReadOnlyList<FieldScriptInstruction> SlotReach(int entity, int slot) =>
        Pointer(entity, slot) is { } pointer ? Reach(pointer) : [];

    /// <summary>
    /// The returns Init can end at. The engine runs script 0 to the first RET it reaches and
    /// starts Main one byte later (0060C683), so each return reachable from Init without
    /// passing another one is a place Main can begin.
    /// </summary>
    public IReadOnlyList<int> InitReturns(int entity)
    {
        if (initReturnsByEntity.TryGetValue(entity, out var cached))
        {
            return cached;
        }

        var returns = SlotReach(entity, 0).Where(instruction => instruction.Id == 0x00).Select(instruction => instruction.Offset).ToArray();
        initReturnsByEntity[entity] = returns;
        return returns;
    }

    /// <summary>Everything script 0 can run: Init and every Main that can follow it, in offset order.</summary>
    public IReadOnlyList<FieldScriptInstruction> InitAndMainReach(int entity)
    {
        if (initAndMainByEntity.TryGetValue(entity, out var cached))
        {
            return cached;
        }

        var combined = new Dictionary<int, FieldScriptInstruction>();
        foreach (var instruction in SlotReach(entity, 0))
        {
            combined[instruction.Offset] = instruction;
        }

        foreach (var ret in InitReturns(entity))
        {
            foreach (var instruction in Reach(ret + 1))
            {
                combined[instruction.Offset] = instruction;
            }
        }

        var entry = Pointer(entity, 0) ?? 0;
        var result = combined.Values
            .OrderBy(instruction => instruction.Offset < entry)
            .ThenBy(instruction => instruction.Offset)
            .ToArray();
        initAndMainByEntity[entity] = result;
        return result;
    }

    /// <summary>
    /// Whether the entity's model can ever be the one the player moves.
    ///
    /// <para>The field starts with model 0 controlled (0060BCFA clears script context +0x2A).
    /// The Init executor 0060C683 runs every entity's Init in entity order, each to the first
    /// RET it reaches, and CHAR (00614353) numbers models in the order they are loaded
    /// (00CC0898, which the executor resets), so model 0 belongs to the first entity whose Init
    /// actually runs CHAR. Every entity whose Init can run CHAR while every Init before it can
    /// finish without one is therefore a candidate (<see cref="InitModelLoad"/>): an earlier
    /// CHAR that its own Init provably skips does not take model 0. PC (0061BCD7) and 0061BC67
    /// hand control to the model of the entity bound to the character in party slot 0, and CC
    /// (006142D5) to the model of any entity it names. Those are the only writers of +0x2A, so
    /// no other model is ever the player's: a Shinra soldier climbing after Tifa at Junon moves
    /// the soldier.</para>
    /// </summary>
    public bool IsControllable(int entity)
    {
        if (controllable is null)
        {
            var entities = new HashSet<int>();
            for (var candidate = 0; candidate < EntityCount; candidate++)
            {
                var (mayLoad, maySkip) = InitModelLoad(candidate);
                if (mayLoad)
                {
                    entities.Add(candidate);
                }

                if (!maySkip)
                {
                    break;
                }
            }

            for (var candidate = 0; candidate < EntityCount; candidate++)
            {
                if (IsPartyCharacter(candidate))
                {
                    entities.Add(candidate);
                }
            }

            foreach (var instruction in LiveInstructions())
            {
                if (instruction.Id == 0xBF && instruction.Bytes.Length >= 2)
                {
                    entities.Add(instruction.Bytes[1]);
                }
            }

            controllable = entities;
        }

        return controllable.Contains(entity);
    }

    // How far one Init is followed before the answer is taken from its syntax alone.
    private const int MaximumInitSteps = 4096;

    /// <summary>
    /// Whether the entity's Init can load a model (run CHAR before its first RET) and whether it
    /// can finish without loading one.
    ///
    /// <para>Init is followed as the executor runs it: forking on every test whose answer is not
    /// known and deciding the byte tests whose left side Init itself has set, since nothing else
    /// runs while Inits do. A CHAR behind <c>SETBYTE 5[0]=0; IFUB 5[0]==1</c> is never run. What
    /// is not decided by Init's own writes is taken both ways. An Init that is too large to follow
    /// is answered from what it can reach: it may load a model if CHAR is reachable, and it may
    /// always finish without one.</para>
    /// </summary>
    public (bool MayLoad, bool MaySkip) InitModelLoad(int entity)
    {
        if (Pointer(entity, 0) is not { } entry)
        {
            return (false, true);
        }

        var mayLoad = false;
        var maySkip = false;
        var visited = new HashSet<(int Offset, bool Loaded, string Constants)>();
        var pending = new Stack<(int Offset, bool Loaded, Dictionary<FieldBankByte, byte> Constants)>();
        var successors = new List<int>(2);
        var written = new List<FieldBankByte>();
        pending.Push((entry, false, new Dictionary<FieldBankByte, byte>()));
        var steps = 0;
        while (pending.Count != 0)
        {
            if (++steps > MaximumInitSteps)
            {
                return (SlotReach(entity, 0).Any(instruction => instruction.Id == 0xA1), true);
            }

            var (offset, loaded, constants) = pending.Pop();
            if (!visited.Add((offset, loaded, Describe(constants))) || !TryGetInstruction(offset, out var instruction))
            {
                continue;
            }

            if (instruction.Id == 0x00)
            {
                mayLoad |= loaded;
                maySkip |= !loaded;
                continue;
            }

            if (instruction.Id == 0x07 && instruction.Bytes.Length >= 2)
            {
                // RETTO hands Init to another slot, and the executor goes on until a RET.
                if (Pointer(entity, instruction.Bytes[1] & 0x1F) is { } transfer)
                {
                    pending.Push((transfer, loaded, constants));
                }

                continue;
            }

            if (Stops(instruction.Id))
            {
                // The executor would never get past it; nothing after it loads a model.
                maySkip |= !loaded;
                mayLoad |= loaded;
                continue;
            }

            var loadsHere = loaded || instruction.Id == 0xA1;
            var holds = ResolveByteTest(instruction, constants);
            Writes(instruction, written, out _);
            foreach (var key in written)
            {
                constants.Remove(key);
            }

            foreach (var block in UnnamedWriteBlocks(instruction))
            {
                foreach (var key in constants.Keys.Where(key => key.Block == block).ToArray())
                {
                    constants.Remove(key);
                }
            }

            var raw = instruction.Bytes;
            if (instruction.Id == 0x80 && raw.Length >= 4 && (raw[1] & 0x0F) == 0 &&
                FieldBankByte.BlockOf(raw[1] >> 4) is var block2 and not 0)
            {
                constants[new FieldBankByte(block2, raw[2])] = raw[3];
            }

            Successors(instruction, successors);
            if (holds is { } known && FalseTarget(instruction) is { } falseTarget)
            {
                var target = known ? instruction.Next : falseTarget;
                if (successors.Contains(target))
                {
                    pending.Push((target, loadsHere, constants));
                }

                continue;
            }

            for (var index = 0; index < successors.Count; index++)
            {
                pending.Push((successors[index], loadsHere,
                    index == successors.Count - 1 ? constants : new Dictionary<FieldBankByte, byte>(constants)));
            }
        }

        return (mayLoad, maySkip);

        static string Describe(Dictionary<FieldBankByte, byte> constants) =>
            string.Join(";", constants.OrderBy(pair => pair.Key.Block).ThenBy(pair => pair.Key.Address)
                .Select(pair => $"{pair.Key.Block}:{pair.Key.Address}:{pair.Value}"));
    }

    /// <summary>
    /// IFUB or IFUBL decided by a known left byte and an immediate right one, as 006116A6 and
    /// 0061171F compare them; null for anything else.
    /// </summary>
    public static bool? ResolveByteTest(FieldScriptInstruction instruction, IReadOnlyDictionary<FieldBankByte, byte> constants)
    {
        var raw = instruction.Bytes;
        if (instruction.Id is not (0x14 or 0x15) || raw.Length < 6 || (raw[1] & 0x0F) != 0 ||
            !constants.TryGetValue(new FieldBankByte(FieldBankByte.BlockOf(raw[1] >> 4), raw[2]), out var actual))
        {
            return null;
        }

        var expected = raw[3];
        return raw[4] switch
        {
            0 => actual == expected,
            1 => actual != expected,
            2 => actual > expected,
            3 => actual < expected,
            4 => actual >= expected,
            5 => actual <= expected,
            _ => null
        };
    }

    /// <summary>
    /// The live tests that decide whether the MAPJUMP at <paramref name="at"/> runs when the
    /// entity's script <paramref name="slot"/> is started: every conditional on the way from the
    /// script's entry that the MAPJUMP cannot be reached without passing (it dominates it), and
    /// that only one of whose sides leads there, when it compares a savemap byte or word with a
    /// constant. A test only counts when its value is the one the byte had when the script
    /// started: nothing on the way to it - this script or anything it asks for - writes the byte,
    /// and no code that runs on its own (<see cref="IsConcurrent"/>) does. Whatever else decides
    /// the way (key tests, party tests, temporary values, callers' tests) is left out, so the
    /// guard is never stricter than the engine. <paramref name="alsoRunning"/> is what else the
    /// same crossing can start - the LINE's other event scripts - and a byte any of it writes is no
    /// guard either.
    ///
    /// <para>One exception to the rule on code that runs on its own: a script the dispatcher has
    /// just started (<paramref name="startedByTheDispatcher"/>, a LINE's event) runs straight away
    /// in the same pass of 0060C94D, eight opcodes at most and until a handler asks to wait, and
    /// nothing else runs in between. So a test among its first eight opcodes, reached only
    /// through tests and forward jumps (whose handlers all return 0 and never wait), sees the
    /// byte as it was when the line was crossed, whatever a Main does later.</para>
    ///
    /// <para>Null when the MAPJUMP is not reached from there.</para>
    /// </summary>
    public IReadOnlyList<FieldScriptGuardTest>? MapJumpGuard(
        int entity,
        int slot,
        int at,
        IReadOnlyCollection<(int Entity, int Pointer)>? alsoRunning = null,
        bool startedByTheDispatcher = false)
    {
        if (Pointer(entity, slot) is not { } entry)
        {
            return null;
        }

        // The script's own control flow, from its entry: a MAPJUMP never goes on (0063C17F),
        // RETTO carries on in the same entity's named slot, and requests are not followed.
        var forward = new Dictionary<int, List<int>>();
        var pending = new Stack<int>();
        var next = new List<int>(2);
        pending.Push(entry);
        while (pending.Count != 0)
        {
            var offset = pending.Pop();
            if (forward.ContainsKey(offset) || !TryGetInstruction(offset, out var instruction))
            {
                continue;
            }

            var successors = new List<int>(2);
            if (instruction.Id == 0x07 && instruction.Bytes.Length >= 2)
            {
                if (Pointer(entity, instruction.Bytes[1] & 0x1F) is { } transfer)
                {
                    successors.Add(transfer);
                }
            }
            else if (instruction.Id != 0x60)
            {
                Successors(instruction, next);
                successors.AddRange(next);
            }

            forward[offset] = successors;
            foreach (var successor in successors)
            {
                pending.Push(successor);
            }
        }

        if (!forward.ContainsKey(at))
        {
            return null;
        }

        var predecessors = new Dictionary<int, List<int>>();
        foreach (var (offset, successors) in forward)
        {
            foreach (var successor in successors)
            {
                if (!predecessors.TryGetValue(successor, out var list))
                {
                    list = [];
                    predecessors[successor] = list;
                }

                list.Add(offset);
            }
        }

        HashSet<int> Leading(int target)
        {
            var set = new HashSet<int> { target };
            var stack = new Stack<int>();
            stack.Push(target);
            while (stack.Count != 0)
            {
                foreach (var previous in predecessors.GetValueOrDefault(stack.Pop()) ?? [])
                {
                    if (set.Add(previous))
                    {
                        stack.Push(previous);
                    }
                }
            }

            return set;
        }

        var toMapJump = Leading(at);
        // Dominators within the ways that lead to the MAPJUMP, from the entry.
        var order = toMapJump.OrderBy(offset => offset).ToArray();
        var dominators = new Dictionary<int, HashSet<int>>();
        foreach (var offset in order)
        {
            dominators[offset] = offset == entry ? [entry] : new HashSet<int>(order);
        }

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var offset in order)
            {
                if (offset == entry)
                {
                    continue;
                }

                HashSet<int>? meet = null;
                foreach (var previous in predecessors.GetValueOrDefault(offset) ?? [])
                {
                    if (!dominators.TryGetValue(previous, out var theirs))
                    {
                        continue;
                    }

                    if (meet is null)
                    {
                        meet = new HashSet<int>(theirs);
                    }
                    else
                    {
                        meet.IntersectWith(theirs);
                    }
                }

                meet ??= [];
                meet.Add(offset);
                if (!meet.SetEquals(dominators[offset]))
                {
                    dominators[offset] = meet;
                    changed = true;
                }
            }
        }

        var guard = new List<FieldScriptGuardTest>();
        var written = new List<FieldBankByte>();
        var siblings = alsoRunning is { Count: > 0 } ? Closure(alsoRunning) : [];
        foreach (var offset in dominators[at].Order())
        {
            if (offset == at || !TryGetInstruction(offset, out var test) || GuardTest(test) is not { } literal ||
                FalseTarget(test) is not { } falseTarget)
            {
                continue;
            }

            var holdsLeads = toMapJump.Contains(test.Next);
            var failsLeads = toMapJump.Contains(falseTarget);
            if (holdsLeads == failsLeads)
            {
                continue;
            }

            literal = literal with { Holds = holdsLeads };
            var bytes = literal.Word
                ? [new FieldBankByte(literal.Block, literal.Address), NextByte(literal.Block, literal.Address)]
                : new[] { new FieldBankByte(literal.Block, literal.Address) };
            if (bytes.Any(key => key.Block == 0))
            {
                continue;
            }

            // Everything that can run before the test on the way from the entry, and what it asks for.
            var before = Leading(offset);
            before.IntersectWith(forward.Keys);
            before.Remove(offset);
            if (bytes.Any(IsConcurrentlyWritten) &&
                !(startedByTheDispatcher && before.Count < NativeOpcodesPerPass && before.All(IsNeverWaiting)))
            {
                continue;
            }
            var calls = new List<(int Entity, int Pointer)>();
            var overwritten = false;
            foreach (var earlier in before)
            {
                if (!TryGetInstruction(earlier, out var instruction))
                {
                    continue;
                }

                if (WritesAny(instruction, bytes, written))
                {
                    overwritten = true;
                    break;
                }

                var raw = instruction.Bytes;
                if (instruction.Id is >= 0x01 and <= 0x03 && raw.Length >= 3 && Pointer(raw[1], raw[2] & 0x1F) is { } requested)
                {
                    calls.Add((raw[1], requested));
                }
                else if (instruction.Id is >= 0x04 and <= 0x06 && raw.Length >= 3)
                {
                    calls.AddRange(PartyEntries(raw[2] & 0x1F));
                }
            }

            if (!overwritten && calls.Count != 0)
            {
                foreach (var calleeOffset in Closure(calls))
                {
                    if (TryGetInstruction(calleeOffset, out var instruction) && WritesAny(instruction, bytes, written))
                    {
                        overwritten = true;
                        break;
                    }
                }
            }

            if (!overwritten)
            {
                foreach (var siblingOffset in siblings)
                {
                    if (TryGetInstruction(siblingOffset, out var instruction) && WritesAny(instruction, bytes, written))
                    {
                        overwritten = true;
                        break;
                    }
                }
            }

            if (!overwritten)
            {
                guard.Add(literal);
            }
        }

        return guard;

        static FieldBankByte NextByte(int block, int address) =>
            address < 0xFF
                ? new FieldBankByte(block, address + 1)
                : new FieldBankByte(block switch { 1 => 3, 3 => 11, 11 => 13, 13 => 15, _ => 0 }, 0);
    }

    /// <summary>
    /// The ways a line's crossing reaches a MAPJUMP that is not in the line's own code but in a
    /// script one of its events asks another entity to run (REQ, REQSW, REQEW): for each such
    /// request in an event's own code, the event's tests on the way to the request (the event is
    /// <c>startedByTheDispatcher</c>) joined with the requested script's own guard. Null when the
    /// crossing can reach that script some other way - a request made by a script an event asks
    /// for, a party-slot request or RETTO - so the event's tests cannot be claimed for it.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<FieldScriptGuardTest>>? RequestedMapJumpGuards(
        int lineEntity,
        IReadOnlyList<(int Slot, int Pointer)> events,
        int entity,
        int slot,
        int at)
    {
        if (Pointer(entity, slot) is not { } target || entity == lineEntity)
        {
            return null;
        }

        var ownCode = new Dictionary<int, (int Slot, int Pointer)>();
        foreach (var (eventSlot, pointer) in events)
        {
            foreach (var instruction in Reach(pointer))
            {
                ownCode.TryAdd(instruction.Offset, (eventSlot, pointer));
            }
        }

        // Everything the crossing can run, requested scripts included.
        var crossing = Closure(events.Select(start => (lineEntity, start.Pointer)));
        var requests = new List<(int Offset, int Slot, int Pointer)>();
        foreach (var offset in crossing)
        {
            if (!TryGetInstruction(offset, out var instruction))
            {
                continue;
            }

            var raw = instruction.Bytes;
            if (instruction.Id is >= 0x01 and <= 0x03 && raw.Length >= 3)
            {
                if (raw[1] == entity && Pointer(raw[1], raw[2] & 0x1F) == target)
                {
                    if (!ownCode.TryGetValue(offset, out var owner))
                    {
                        return null;
                    }

                    requests.Add((offset, owner.Slot, owner.Pointer));
                }
            }
            else if (instruction.Id is >= 0x04 and <= 0x06 && raw.Length >= 3 &&
                     PartyEntries(raw[2] & 0x1F).Any(entry => entry.Entity == entity && entry.Pointer == target))
            {
                return null;
            }
            else if (instruction.Id == 0x07 && raw.Length >= 2 && Pointer(entity, raw[1] & 0x1F) == target &&
                     DistinctEntries(entity).Any(entry => Reach(entry.Pointer).Any(own => own.Offset == offset)))
            {
                return null;
            }
        }

        // The events keep running after an unwaited request, so what any of them writes is no
        // guard in the requested script either.
        if (requests.Count == 0 ||
            MapJumpGuard(entity, slot, at, events.Select(start => (lineEntity, start.Pointer)).ToArray()) is not { } callee)
        {
            return null;
        }

        var ways = new List<IReadOnlyList<FieldScriptGuardTest>>();
        foreach (var (offset, eventSlot, pointer) in requests)
        {
            var others = events.Where(start => start.Pointer != pointer).Select(start => (lineEntity, start.Pointer)).ToArray();
            var caller = MapJumpGuard(lineEntity, eventSlot, offset, others, startedByTheDispatcher: true) ?? [];
            ways.Add(caller.Concat(callee).DistinctBy(test => test.Key).ToArray());
        }

        return ways;
    }

    // 0060C94D runs each entity at most this many opcodes a pass.
    private const int NativeOpcodesPerPass = 8;

    // The byte, word and party tests (006116A6, 0061171F, 00611A89, 00611B02, 00611E1B, 00611E94,
    // 0061C696, 0061C75E) and the forward jumps (006130F6, 00613141) all return 0: they never end
    // the entity's turn. The key tests are left out; their handlers' result is not a constant.
    private bool IsNeverWaiting(int offset) =>
        TryGetInstruction(offset, out var instruction) &&
        instruction.Id is >= 0x14 and <= 0x19 or 0x10 or 0x11 or 0xCB or 0xCC;

    // Whether the instruction can write one of the bytes, named or not.
    private static bool WritesAny(FieldScriptInstruction instruction, IReadOnlyList<FieldBankByte> bytes, List<FieldBankByte> scratch)
    {
        Writes(instruction, scratch, out _);
        if (scratch.Any(bytes.Contains))
        {
            return true;
        }

        var blocks = UnnamedWriteBlocks(instruction);
        return bytes.Any(key => blocks.Contains(key.Block));
    }

    private bool IsConcurrentlyWritten(FieldBankByte key) =>
        (WritersByByte().TryGetValue(key, out var writers) && writers.Any(IsConcurrent)) ||
        UnnamedWriters(key.Block).Any(IsConcurrent);

    /// <summary>
    /// A byte or word test of a savemap variable against a constant (IFUB, IFUBL, IFSW, IFSWL,
    /// IFUW, IFUWL), as a guard literal with its side still to be chosen; null for anything else.
    /// The variable is the byte at the operand (0060FD6C), and a word is read only from a word
    /// bank (2, 4, 12, 14, 7).
    /// </summary>
    private static FieldScriptGuardTest? GuardTest(FieldScriptInstruction instruction)
    {
        var raw = instruction.Bytes;
        var (word, signed, operatorAt, valueAt) = instruction.Id switch
        {
            0x14 or 0x15 => (false, false, 4, 3),
            0x16 or 0x17 => (true, true, 6, 4),
            0x18 or 0x19 => (true, false, 6, 4),
            _ => (false, false, -1, -1)
        };
        if (operatorAt < 0 || raw.Length <= operatorAt || (raw[1] & 0x0F) != 0)
        {
            return null;
        }

        var bank = raw[1] >> 4;
        var block = FieldBankByte.BlockOf(bank);
        if (block is 0 or 5 || raw[operatorAt] > 10)
        {
            return null;
        }

        var isWord = word && FieldBankByte.IsWordBank(bank);
        // IFSW's constant is a signed word even against a byte bank's zero-extended byte, so it
        // is kept signed: IFSW and IFUW with the same bits are different tests.
        int value = !word ? raw[valueAt] : signed ? BitConverter.ToInt16(raw, valueAt) : BitConverter.ToUInt16(raw, valueAt);
        return new FieldScriptGuardTest(block, raw[2], isWord, signed, raw[operatorAt], value, true);
    }

    /// <summary>
    /// The characters PC binds the entity to anywhere it can run (0061BCD7 writes the entity
    /// into 00CC0998 for the character).
    /// </summary>
    public IReadOnlyList<int> PartyCharactersOf(int entity)
    {
        if (entity < 0 || entity >= EntityCount)
        {
            return [];
        }

        partyCharacterLists ??= new IReadOnlyList<int>?[EntityCount];
        return partyCharacterLists[entity] ??= InitAndMainReach(entity)
            .Concat(DistinctEntries(entity).SelectMany(entry => Reach(entry.Pointer)))
            .Where(instruction => instruction.Id == 0xA0 && instruction.Bytes.Length >= 2)
            .Select(instruction => (int)instruction.Bytes[1])
            .Distinct()
            .Order()
            .ToArray();
    }

    /// <summary>
    /// Whether the entity is bound to a playable character: some script it runs holds PC
    /// (0xA0), which is what makes it answer a party-slot request.
    /// </summary>
    public bool IsPartyCharacter(int entity)
    {
        partyCharacters ??= new bool?[EntityCount];
        if (entity < 0 || entity >= EntityCount)
        {
            return false;
        }

        if (partyCharacters[entity] is { } known)
        {
            return known;
        }

        var result = InitAndMainReach(entity).Any(instruction => instruction.Id == 0xA0) ||
                     DistinctEntries(entity).Any(entry => Reach(entry.Pointer).Any(instruction => instruction.Id == 0xA0));
        partyCharacters[entity] = result;
        return result;
    }

    /// <summary>The instructions an entity's script <paramref name="slot"/> runs: for slot 0, Init and Main.</summary>
    public IReadOnlyList<FieldScriptInstruction> Script(int entity, int slot) =>
        slot == 0 ? InitAndMainReach(entity) : SlotReach(entity, slot);

    /// <summary>
    /// The bank bytes an instruction writes, from the native writer table. A variable
    /// address is the operand's byte; a word helper writes two bytes only on a word bank.
    /// <paramref name="unknownDestination"/> is set when the instruction also writes a byte
    /// that cannot be named statically; <see cref="UnnamedWriteBlocks"/> says where.
    /// </summary>
    public static void Writes(FieldScriptInstruction instruction, List<FieldBankByte> bytes, out bool unknownDestination)
    {
        bytes.Clear();
        unknownDestination = UnnamedWriteBlocks(instruction).Count != 0;
        var raw = instruction.Bytes;
        if (DirectWrites.TryGetValue(instruction.Id, out var direct))
        {
            foreach (var (block, first, last) in direct)
            {
                AddRange(block, first, last);
            }
        }

        switch (instruction.Id)
        {
            case 0x0B:
                // GTPYE: three bytes, nibbles 1-3 at operands 3-5.
                for (var index = 0; index < 3; index++)
                {
                    AddByte(Nibble(raw, 1, index + 1), 3 + index, word: false);
                }

                return;
            case 0x0F when raw.Length >= 4 && raw[1] == 0xF7:
                AddByte(Nibble(raw, 1, 4), 3, word: false);
                return;
            case 0x0F when raw.Length >= 2 && raw[1] == 0xF9:
                // SPECIAL F9 (0061E78C case 4) gives every materia through 006CC0EA, which
                // sets 1[75].
                AddRange(1, 75, 75);
                return;
            case 0xCD when raw.Length >= 3 && raw[1] == 0:
                // MMBud 0061C812 with enable 0 takes the character out of the party.
                AddRange(3, 9, 11);
                return;
            case 0x0F:
            case 0x9D:
                return;
        }

        if (!NativeWrites.TryGetValue(instruction.Id, out var destinations))
        {
            return;
        }

        foreach (var (nibble, offset, word) in destinations)
        {
            AddByte(Nibble(raw, 1, nibble), offset, word);
        }

        void AddRange(int block, int first, int last)
        {
            for (var address = first; address <= last; address++)
            {
                bytes.Add(new FieldBankByte(block, address));
            }
        }

        void AddByte(int bank, int offset, bool word)
        {
            if (bank == 0 || offset >= raw.Length || FieldBankByte.BlockOf(bank) == 0)
            {
                return;
            }

            var block = FieldBankByte.BlockOf(bank);
            bytes.Add(new FieldBankByte(block, raw[offset]));
            if (word && FieldBankByte.IsWordBank(bank))
            {
                // The savemap blocks are contiguous (1, 3, 11, 13, 15), so a word at the
                // last byte of one ends in the first byte of the next.
                if (raw[offset] < 0xFF)
                {
                    bytes.Add(new FieldBankByte(block, raw[offset] + 1));
                }
                else if (block switch { 1 => 3, 3 => 11, 11 => 13, 13 => 15, _ => 0 } is var next and not 0)
                {
                    bytes.Add(new FieldBankByte(next, 0));
                }
            }
        }
    }

    private static readonly int[] SavemapBlocks = [1, 3, 11, 13, 15];
    private static readonly int[] TemporaryBlock = [5];
    private static readonly int[] AllBlocks = [1, 3, 5, 11, 13, 15];

    /// <summary>
    /// The blocks an instruction can write a byte of without the byte being named in it.
    ///
    /// <para>SETX (00610AFE) computes its index at run time (a word read plus an operand byte,
    /// signed) and switches on its first nibble: 5 writes the temporary block, clamped to its
    /// 256 bytes; 1, 3, 11, 13 and 15 add the index to that block's start in the savemap
    /// banks, clamped only at their end, so with an arbitrary index the byte can be in any
    /// savemap block; every other nibble writes nothing. SPECIAL F6 copies a string into a
    /// savemap block. Neither appears in the installed archives today, and a writer that
    /// cannot be named must still end what the walk knew.</para>
    ///
    /// <para>SPECIAL FE (0061E78C case 9, 0060BB9B) starts a new game: it clears all 0x500 bytes of
    /// the savemap blocks and sets the party. MINIGAME (006134C0) hands the field over to a
    /// minigame module (field loop 0063C17F, request 0x0C) and goes on only when 004090E6 resumes
    /// it; what the module keeps of its result is written outside every field opcode, so after it
    /// nothing the walk knew of the savemap blocks is known any more.</para>
    /// </summary>
    public static IReadOnlyList<int> UnnamedWriteBlocks(FieldScriptInstruction instruction)
    {
        var raw = instruction.Bytes;
        return instruction.Id switch
        {
            0x9D when raw.Length >= 2 => (raw[1] >> 4) switch
            {
                5 => TemporaryBlock,
                1 or 3 or 11 or 13 or 15 => SavemapBlocks,
                _ => []
            },
            0x0F when raw.Length >= 2 && raw[1] == 0xF6 => AllBlocks,
            0x0F when raw.Length >= 2 && raw[1] == 0xFE => SavemapBlocks,
            0x20 => SavemapBlocks,
            _ => []
        };
    }

    /// <summary>
    /// Whether the instruction can change who is in the party: PRTYP, PRTYM, PRTYE, MMBud taking
    /// a member out, MENU (the party menu returns through 0061C577) and SPECIAL FE's new game.
    /// </summary>
    public static bool ChangesParty(FieldScriptInstruction instruction) =>
        instruction.Id is 0xC8 or 0xC9 or 0xCA or 0x49 ||
        (instruction.Id == 0xCD && instruction.Bytes.Length >= 3 && instruction.Bytes[1] == 0) ||
        (instruction.Id == 0x0F && instruction.Bytes.Length >= 2 && instruction.Bytes[1] == 0xFE);

    /// <summary>
    /// Whether the instruction hands control to the model of whoever is in party slot 0:
    /// PRTYP, PRTYM and PRTYE all end in 0061BC67, which does exactly that.
    /// </summary>
    public static bool HandsControlToTheLeader(FieldScriptInstruction instruction) =>
        instruction.Id is 0xC8 or 0xC9 or 0xCA;

    /// <summary>A bank nibble: 1 is the high nibble of the first bank byte, 2 its low nibble, and so on.</summary>
    public static int Nibble(byte[] raw, int bankOffset, int nibble)
    {
        var index = bankOffset + (nibble - 1) / 2;
        if (index >= raw.Length)
        {
            return 0;
        }

        return nibble % 2 == 1 ? raw[index] >> 4 : raw[index] & 0x0F;
    }

    /// <summary>
    /// Every live instruction that writes each bank byte: all code any slot, Init or Main can
    /// reach.
    /// </summary>
    public IReadOnlyDictionary<FieldBankByte, List<int>> WritersByByte()
    {
        if (writersByByte is not null)
        {
            return writersByByte;
        }

        var writers = new Dictionary<FieldBankByte, List<int>>();
        var seen = new HashSet<int>();
        var bytes = new List<FieldBankByte>();
        foreach (var instruction in LiveInstructions())
        {
            if (!seen.Add(instruction.Offset))
            {
                continue;
            }

            Writes(instruction, bytes, out _);
            foreach (var key in bytes)
            {
                if (!writers.TryGetValue(key, out var list))
                {
                    list = [];
                    writers[key] = list;
                }

                list.Add(instruction.Offset);
            }
        }

        writersByByte = writers;
        return writers;
    }

    /// <summary>Every live instruction that can write a byte of <paramref name="block"/> it does not name.</summary>
    public IReadOnlyList<int> UnnamedWriters(int block)
    {
        if (unnamedWritersByBlock is null)
        {
            var writers = new Dictionary<int, List<int>>();
            var seen = new HashSet<int>();
            foreach (var instruction in LiveInstructions())
            {
                if (!seen.Add(instruction.Offset))
                {
                    continue;
                }

                foreach (var written in UnnamedWriteBlocks(instruction))
                {
                    if (!writers.TryGetValue(written, out var list))
                    {
                        list = [];
                        writers[written] = list;
                    }

                    list.Add(instruction.Offset);
                }
            }

            unnamedWritersByBlock = writers;
        }

        return unnamedWritersByBlock.TryGetValue(block, out var result) ? result : [];
    }

    /// <summary>
    /// Whether code at <paramref name="offset"/> can run on its own while a walk is under way,
    /// rather than only in order with it.
    ///
    /// <para>Code runs on its own when the engine starts it without the walk: every Main (the
    /// engine runs each entity's Main whenever nothing more urgent is scheduled on it, up to
    /// eight opcodes a pass, 0060C94D), and every script some live code requests without
    /// waiting (REQ, REQSW, PREQ, PRQSW) - with everything those go on to request, wait for or
    /// hand over to. A Main stays independent even when the walk also asks for its code under
    /// another slot. Init runs to its RET before any of this, so code only Init reaches is
    /// never independent.</para>
    ///
    /// <para>Event scripts are not counted: the dispatcher starts a LINE's or a model's event
    /// only from the player's own contact with it or press of OK beside it (0060C94D's event
    /// flags), and the walk is what follows the one the player just set off. The Gold Saucer's
    /// seven tube lines each lock control, set 5[2] to their own tube and ask the leader to
    /// travel by it; counting the other six lines' Go scripts as writers sent every tube to all
    /// seven places. A second interaction the player makes is that interaction's own walk.</para>
    /// </summary>
    public bool IsConcurrent(int offset) => (concurrency ??= BuildConcurrencyIndex()).Contains(offset);

    /// <summary>
    /// Whether the engine can ever run code at <paramref name="offset"/>: from an Init or a Main,
    /// from an event the dispatcher starts (0060C94D: a model's Talk and Contact, slots 1 and 2, and
    /// a LINE's slots 1 to 6), or from anything those request, wait for or hand over to. A script
    /// only reachable through its own slot, with nothing asking for it, is private: the engine
    /// never starts it.
    /// </summary>
    public bool IsEventReachable(int offset) => (eventReach ??= BuildEventReach()).Contains(offset);

    private HashSet<int> BuildEventReach()
    {
        var starts = new List<(int Entity, int Pointer)>();
        for (var entity = 0; entity < EntityCount; entity++)
        {
            if (Pointer(entity, 0) is { } init)
            {
                starts.Add((entity, init));
            }

            foreach (var ret in InitReturns(entity))
            {
                starts.Add((entity, ret + 1));
            }

            var lastEvent = InitAndMainReach(entity).Any(instruction => instruction.Id == 0xD0) ? 6 : 2;
            for (var slot = 1; slot <= lastEvent; slot++)
            {
                if (Pointer(entity, slot) is { } pointer)
                {
                    starts.Add((entity, pointer));
                }
            }
        }

        return Closure(starts);
    }

    private HashSet<int> BuildConcurrencyIndex()
    {
        var independent = new List<(int Entity, int Pointer)>();
        for (var entity = 0; entity < EntityCount; entity++)
        {
            foreach (var ret in InitReturns(entity))
            {
                independent.Add((entity, ret + 1));
            }
        }

        foreach (var instruction in LiveInstructions())
        {
            var bytes = instruction.Bytes;
            if (instruction.Id is 0x01 or 0x02 && bytes.Length >= 3 &&
                Pointer(bytes[1], bytes[2] & 0x1F) is { } requested)
            {
                independent.Add((bytes[1], requested));
            }
            else if (instruction.Id is 0x04 or 0x05 && bytes.Length >= 3)
            {
                independent.AddRange(PartyEntries(bytes[2] & 0x1F));
            }
        }

        return Closure(independent);
    }

    // Every offset the starts run, with everything they request, wait for or hand over to.
    private HashSet<int> Closure(IEnumerable<(int Entity, int Pointer)> starts)
    {
        var offsets = new HashSet<int>();
        var seen = new HashSet<(int Entity, int Pointer)>();
        var pending = new Stack<(int Entity, int Pointer)>(starts);
        while (pending.Count != 0)
        {
            var (entity, pointer) = pending.Pop();
            if (!seen.Add((entity, pointer)))
            {
                continue;
            }

            foreach (var instruction in Reach(pointer))
            {
                offsets.Add(instruction.Offset);
                var bytes = instruction.Bytes;
                switch (instruction.Id)
                {
                    case >= 0x01 and <= 0x03 when bytes.Length >= 3 &&
                                                  Pointer(bytes[1], bytes[2] & 0x1F) is { } requested:
                        pending.Push((bytes[1], requested));
                        break;
                    case >= 0x04 and <= 0x06 when bytes.Length >= 3:
                        foreach (var member in PartyEntries(bytes[2] & 0x1F))
                        {
                            pending.Push(member);
                        }

                        break;
                    case 0x07 when bytes.Length >= 2 && Pointer(entity, bytes[1] & 0x1F) is { } transfer:
                        pending.Push((entity, transfer));
                        break;
                }
            }
        }

        return offsets;
    }

    // What a party request for script <paramref name="slot"/> can start: that script of
    // every entity bound to a playable character.
    private IEnumerable<(int Entity, int Pointer)> PartyEntries(int slot)
    {
        for (var entity = 0; entity < EntityCount; entity++)
        {
            if (IsPartyCharacter(entity) && Pointer(entity, slot) is { } pointer)
            {
                yield return (entity, pointer);
            }
        }
    }

    /// <summary>Every instruction some slot, Init or Main can reach.</summary>
    public IEnumerable<FieldScriptInstruction> LiveInstructions()
    {
        for (var entity = 0; entity < EntityCount; entity++)
        {
            foreach (var instruction in InitAndMainReach(entity))
            {
                yield return instruction;
            }

            foreach (var (pointer, slots) in DistinctEntries(entity))
            {
                if (slots.All(slot => slot == 0))
                {
                    continue;
                }

                foreach (var instruction in Reach(pointer))
                {
                    yield return instruction;
                }
            }
        }
    }

    private static string ReadName(byte[] bytes, int offset)
    {
        var length = 0;
        while (length < 8 && offset + length < bytes.Length && bytes[offset + length] != 0)
        {
            length++;
        }

        return System.Text.Encoding.ASCII.GetString(bytes, offset, length).Trim();
    }
}
