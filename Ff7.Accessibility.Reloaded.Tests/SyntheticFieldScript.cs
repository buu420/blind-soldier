using System.Text;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// A field-script assembler for tests: opcodes by name, labels for slot entries and jump
/// targets. Jumps are encoded as the PC handlers read them: forward from the operand byte
/// (JMPF/JMPFL +1, IFUB +5, IFSW +7, IFKEY +3, IFPRTYQ +2), backward from the opcode.
/// </summary>
internal sealed class SyntheticScriptCode
{
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, int> labels = new(StringComparer.Ordinal);
    private readonly List<(int OpStart, int OperandAt, bool Long, bool Back, string Label)> fixups = [];

    public int this[string label] => labels[label];

    public SyntheticScriptCode Label(string name)
    {
        labels.Add(name, bytes.Count);
        return this;
    }

    public SyntheticScriptCode Raw(params int[] data)
    {
        bytes.AddRange(data.Select(value => (byte)value));
        return this;
    }

    public SyntheticScriptCode Ret() => Raw(0x00);

    public SyntheticScriptCode Req(int entity, int script, int priority = 6) => Raw(0x01, entity, (priority << 5) | script);

    public SyntheticScriptCode ReqEw(int entity, int script, int priority = 6) => Raw(0x03, entity, (priority << 5) | script);

    public SyntheticScriptCode Preq(int partySlot, int script, int priority = 6) => Raw(0x04, partySlot, (priority << 5) | script);

    public SyntheticScriptCode PrqEw(int partySlot, int script, int priority = 6) => Raw(0x06, partySlot, (priority << 5) | script);

    public SyntheticScriptCode Retto(int script, int priority = 6) => Raw(0x07, (priority << 5) | script);

    public SyntheticScriptCode JmpF(string label) => Jump(0x10, false, false, label);

    public SyntheticScriptCode JmpFL(string label) => Jump(0x11, true, false, label);

    public SyntheticScriptCode JmpB(string label) => Jump(0x12, false, true, label);

    /// <summary>IFUB: falls through when the test holds, jumps to <paramref name="elseLabel"/> when it fails.</summary>
    public SyntheticScriptCode IfUb(int bank1, int left, int bank2, int right, int op, string elseLabel)
    {
        var start = bytes.Count;
        Raw(0x14, (bank1 << 4) | bank2, left, right, op, 0);
        fixups.Add((start, start + 5, false, false, elseLabel));
        return this;
    }

    /// <summary>IFSW (0x16) or IFUW (0x18) of a variable against a word constant.</summary>
    public SyntheticScriptCode IfWord(bool signed, int bank1, int left, int right, int op, string elseLabel)
    {
        var start = bytes.Count;
        Raw(signed ? 0x16 : 0x18, bank1 << 4, left & 0xFF, left >> 8, right & 0xFF, (right >> 8) & 0xFF, op, 0);
        fixups.Add((start, start + 7, false, false, elseLabel));
        return this;
    }

    public SyntheticScriptCode IfKey(int mask, string elseLabel)
    {
        var start = bytes.Count;
        Raw(0x30, mask & 0xFF, mask >> 8, 0);
        fixups.Add((start, start + 3, false, false, elseLabel));
        return this;
    }

    public SyntheticScriptCode IfPartyMember(int character, string elseLabel)
    {
        var start = bytes.Count;
        Raw(0xCB, character, 0);
        fixups.Add((start, start + 2, false, false, elseLabel));
        return this;
    }

    public SyntheticScriptCode Message(int window, int dialog) => Raw(0x40, window, dialog);

    public SyntheticScriptCode SetByte(int bank, int address, int value) => Raw(0x80, bank << 4, address, value);

    public SyntheticScriptCode Inc(int bank, int address) => Raw(0x95, bank, address);

    public SyntheticScriptCode MapJump(int field, int x = 1, int y = 2, int triangle = 3, int direction = 0) =>
        Raw(0x60, field & 0xFF, field >> 8, x & 0xFF, (x >> 8) & 0xFF, y & 0xFF, (y >> 8) & 0xFF,
            triangle & 0xFF, triangle >> 8, direction);

    public SyntheticScriptCode Ladder(int x, int y, int z, int triangle, int key) =>
        Raw(0xC2, 0, 0, x & 0xFF, (x >> 8) & 0xFF, y & 0xFF, (y >> 8) & 0xFF, z & 0xFF, (z >> 8) & 0xFF,
            triangle & 0xFF, triangle >> 8, key, 0, 0, 0);

    /// <summary>JUMP (0xC0) to a constant position and triangle.</summary>
    public SyntheticScriptCode FieldJump(int x, int y, int triangle, int height = 0) =>
        Raw(0xC0, 0, 0, x & 0xFF, (x >> 8) & 0xFF, y & 0xFF, (y >> 8) & 0xFF, triangle & 0xFF, triangle >> 8,
            height & 0xFF, (height >> 8) & 0xFF);

    public SyntheticScriptCode Char(int model) => Raw(0xA1, model);

    public SyntheticScriptCode Pc(int character) => Raw(0xA0, character);

    /// <summary>CC (0xBF): control goes to the named entity's model.</summary>
    public SyntheticScriptCode Cc(int entity) => Raw(0xBF, entity);

    /// <summary>PRTYE (0xCA): the whole party, FE for an empty slot and FF to keep one from the old party.</summary>
    public SyntheticScriptCode PartyIs(int first, int second, int third) => Raw(0xCA, first, second, third);

    /// <summary>PRTYP (0xC8): adds a character to the first empty party slot.</summary>
    public SyntheticScriptCode PartyAdd(int character) => Raw(0xC8, character);

    /// <summary>PRTYM (0xC9): takes a character out of the party.</summary>
    public SyntheticScriptCode PartyRemove(int character) => Raw(0xC9, character);

    public SyntheticScriptCode Line(int x1, int y1, int z1, int x2, int y2, int z2) =>
        Raw(0xD0, x1 & 0xFF, (x1 >> 8) & 0xFF, y1 & 0xFF, (y1 >> 8) & 0xFF, z1 & 0xFF, (z1 >> 8) & 0xFF,
            x2 & 0xFF, (x2 >> 8) & 0xFF, y2 & 0xFF, (y2 >> 8) & 0xFF, z2 & 0xFF, (z2 >> 8) & 0xFF);

    public SyntheticScriptCode Wait(int frames) => Raw(0x24, frames & 0xFF, frames >> 8);

    public SyntheticScriptCode Nop() => Raw(0x5F);

    private SyntheticScriptCode Jump(int opcode, bool isLong, bool back, string label)
    {
        var start = bytes.Count;
        Raw(opcode);
        Raw(isLong ? new[] { 0, 0 } : new[] { 0 });
        fixups.Add((start, start + 1, isLong, back, label));
        return this;
    }

    public byte[] Assemble()
    {
        var output = bytes.ToArray();
        foreach (var (opStart, operandAt, isLong, back, label) in fixups)
        {
            var value = back ? opStart - labels[label] : labels[label] - operandAt;
            if (value < 0 || value > (isLong ? 0xFFFF : 0xFF))
            {
                throw new InvalidOperationException($"jump to {label} does not fit ({value})");
            }

            output[operandAt] = (byte)value;
            if (isLong)
            {
                output[operandAt + 1] = (byte)(value >> 8);
            }
        }

        return output;
    }
}

/// <summary>
/// A whole field file around one code block. Each entity names the labels its slots start
/// at; a slot left out repeats the previous slot's pointer, as unused slots do in the
/// shipped files.
/// </summary>
internal sealed class SyntheticFieldScript
{
    private readonly List<(string Name, SortedDictionary<int, string> Slots)> entities = [];

    public SyntheticScriptCode Code { get; } = new();

    public SyntheticFieldScript Entity(string name, params (int Slot, string Label)[] slots)
    {
        var map = new SortedDictionary<int, string>();
        foreach (var (slot, label) in slots)
        {
            map[slot] = label;
        }

        entities.Add((name, map));
        return this;
    }

    public int CodeStart => 32 + entities.Count * 8 + entities.Count * 64;

    public int At(string label) => CodeStart + Code[label];

    public byte[] BuildFieldFile()
    {
        var code = Code.Assemble();
        var section = new List<byte>();
        void U16(int value)
        {
            section.Add((byte)value);
            section.Add((byte)(value >> 8));
        }

        var codeStart = CodeStart;
        U16(0x0502);
        section.Add((byte)entities.Count);
        section.Add(0);
        U16(codeStart + code.Length);
        U16(0);
        U16(512);
        section.AddRange(new byte[6]);
        section.AddRange(Padded("synth", 8));
        section.AddRange(Padded("synthfld", 8));
        foreach (var (name, _) in entities)
        {
            section.AddRange(Padded(name, 8));
        }

        foreach (var (_, slots) in entities)
        {
            var pointer = 0;
            for (var slot = 0; slot < 32; slot++)
            {
                if (slots.TryGetValue(slot, out var label))
                {
                    pointer = codeStart + Code[label];
                }

                U16(pointer);
            }
        }

        section.AddRange(code);
        U16(0);
        var sections = new byte[9][];
        sections[0] = section.ToArray();
        sections[1] = [];
        sections[2] = [0, 0, 0, 0, 0, 2];
        sections[3] = [];
        sections[4] = [0, 0, 0, 0];
        sections[5] = [];
        sections[6] = [];
        var triggers = new byte[0x224 + 12 * 16];
        for (var index = 0; index < 12; index++)
        {
            triggers[0x38 + index * 24 + 18] = 0xFF;
            triggers[0x38 + index * 24 + 19] = 0x7F;
        }

        sections[7] = triggers;
        sections[8] = [];
        var file = new List<byte> { 0, 0 };
        file.AddRange(BitConverter.GetBytes(9));
        var offset = 2 + 4 + 9 * 4;
        foreach (var part in sections)
        {
            file.AddRange(BitConverter.GetBytes(offset));
            offset += 4 + part.Length;
        }

        foreach (var part in sections)
        {
            file.AddRange(BitConverter.GetBytes(part.Length));
            file.AddRange(part);
        }

        return file.ToArray();
    }

    /// <summary>What the production catalog reads from this field.</summary>
    public FieldScriptNavigationReadResult Read(int fieldId = 900) =>
        FieldScriptNavigationCatalog.ReadFieldFromBytes(fieldId, "synthfld", BuildFieldFile());

    private static byte[] Padded(string text, int length)
    {
        var result = new byte[length];
        Encoding.ASCII.GetBytes(text, 0, Math.Min(text.Length, length), result, 0);
        return result;
    }
}
