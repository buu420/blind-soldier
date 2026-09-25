using System.Text;

namespace WholeGameStateAudit.Tests;

/// <summary>
/// A field-script assembler: opcodes by name, labels for entry points and jump targets.
/// Jumps are encoded the way the engine measures them (Makou's jumpShift): a forward jump
/// from its operand's position, a backward one from the opcode's own first byte.
/// </summary>
internal sealed class Asm
{
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, int> labels = new(StringComparer.Ordinal);
    private readonly List<(int OpStart, int OperandAt, bool Long, bool Back, string Label)> fixups = [];

    public int Length => bytes.Count;

    public int this[string label] => labels[label];

    public Asm Label(string name)
    {
        labels.Add(name, bytes.Count);
        return this;
    }

    public Asm Raw(params int[] data)
    {
        bytes.AddRange(data.Select(value => (byte)value));
        return this;
    }

    public Asm Ret() => Raw(0x00);

    public Asm Req(int entity, int script, int priority = 6) => Raw(0x01, entity, (priority << 5) | script);

    public Asm ReqEw(int entity, int script, int priority = 6) => Raw(0x03, entity, (priority << 5) | script);

    public Asm Preq(int partySlot, int script, int priority = 6) => Raw(0x04, partySlot, (priority << 5) | script);

    public Asm PrqEw(int partySlot, int script, int priority = 6) => Raw(0x06, partySlot, (priority << 5) | script);

    public Asm Retto(int script, int priority = 6) => Raw(0x07, (priority << 5) | script);

    public Asm JmpF(string label) => Jump(0x10, 1, false, false, label);

    public Asm JmpFL(string label) => Jump(0x11, 1, true, false, label);

    public Asm JmpB(string label) => Jump(0x12, 1, false, true, label);

    public Asm JmpBL(string label) => Jump(0x13, 1, true, true, label);

    /// <summary>IFUB: falls through when <c>left op right</c> holds, jumps to <paramref name="elseLabel"/> otherwise.</summary>
    public Asm IfUb(int bank1, int left, int bank2, int right, int op, string elseLabel)
    {
        var start = bytes.Count;
        Raw(0x14, (bank1 << 4) | bank2, left, right, op, 0);
        fixups.Add((start, start + 5, false, false, elseLabel));
        return this;
    }

    public Asm IfUbL(int bank1, int left, int bank2, int right, int op, string elseLabel)
    {
        var start = bytes.Count;
        Raw(0x15, (bank1 << 4) | bank2, left, right, op, 0, 0);
        fixups.Add((start, start + 5, true, false, elseLabel));
        return this;
    }

    public Asm IfSw(int bank1, int left, int bank2, int right, int op, string elseLabel)
    {
        var start = bytes.Count;
        Raw(0x16, (bank1 << 4) | bank2, left & 0xFF, (left >> 8) & 0xFF, right & 0xFF, (right >> 8) & 0xFF, op, 0);
        fixups.Add((start, start + 7, false, false, elseLabel));
        return this;
    }

    public Asm IfKey(int mask, string elseLabel)
    {
        var start = bytes.Count;
        Raw(0x30, mask & 0xFF, mask >> 8, 0);
        fixups.Add((start, start + 3, false, false, elseLabel));
        return this;
    }

    public Asm IfPartyMember(int character, string elseLabel)
    {
        var start = bytes.Count;
        Raw(0xCB, character, 0);
        fixups.Add((start, start + 2, false, false, elseLabel));
        return this;
    }

    public Asm Message(int window, int dialog) => Raw(0x40, window, dialog);

    public Asm SetByte(int bank, int address, int value) => Raw(0x80, bank << 4, address, value);

    public Asm SetWord(int bank, int address, int value) => Raw(0x81, bank << 4, address, value & 0xFF, value >> 8);

    public Asm BitOn(int bank, int address, int bit) => Raw(0x82, bank << 4, address, bit);

    public Asm MapJump(int field, int x, int y, int triangle, int direction = 0) =>
        Raw(0x60, field & 0xFF, field >> 8, x & 0xFF, (x >> 8) & 0xFF, y & 0xFF, (y >> 8) & 0xFF, triangle & 0xFF, triangle >> 8, direction);

    public Asm Ladder(int x, int y, int z, int triangle, int key) =>
        Raw(0xC2, 0, 0, x & 0xFF, (x >> 8) & 0xFF, y & 0xFF, (y >> 8) & 0xFF, z & 0xFF, (z >> 8) & 0xFF,
            triangle & 0xFF, triangle >> 8, key, 0, 0, 0);

    public Asm Char(int model) => Raw(0xA1, model);

    public Asm Pc(int character) => Raw(0xA0, character);

    public Asm Line(int x1, int y1, int z1, int x2, int y2, int z2) =>
        Raw(0xD0, x1 & 0xFF, (x1 >> 8) & 0xFF, y1 & 0xFF, (y1 >> 8) & 0xFF, z1 & 0xFF, (z1 >> 8) & 0xFF,
            x2 & 0xFF, (x2 >> 8) & 0xFF, y2 & 0xFF, (y2 >> 8) & 0xFF, z2 & 0xFF, (z2 >> 8) & 0xFF);

    public Asm TalkOn(bool enabled) => Raw(0x7E, enabled ? 0 : 1);

    public Asm Visible(bool shown) => Raw(0xA4, shown ? 1 : 0);

    public Asm Wait(int frames) => Raw(0x24, frames & 0xFF, frames >> 8);

    public Asm GetItem(int item, int quantity) => Raw(0x58, 0, item & 0xFF, item >> 8, quantity);

    private Asm Jump(int opcode, int operandOffset, bool isLong, bool back, string label)
    {
        var start = bytes.Count;
        Raw(opcode);
        Raw(isLong ? new[] { 0, 0 } : new[] { 0 });
        fixups.Add((start, start + operandOffset, isLong, back, label));
        return this;
    }

    public byte[] Assemble()
    {
        var output = bytes.ToArray();
        foreach (var (opStart, operandAt, isLong, back, label) in fixups)
        {
            var target = labels[label];
            var value = back ? opStart - target : target - operandAt;
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
/// Builds a whole field file around one <see cref="Asm"/> code block. Each entity names
/// the labels its slots start at; a slot left out repeats the previous slot's pointer,
/// which is how the shipped files leave unused scripts.
/// </summary>
internal sealed class SyntheticField
{
    private readonly List<(string Name, SortedDictionary<int, string> Slots)> entities = [];

    public Asm Code { get; } = new();

    public List<(int Destination, int[] Line)> Gateways { get; } = [];

    public SyntheticField Entity(string name, params (int Slot, string Label)[] slots)
    {
        var map = new SortedDictionary<int, string>();
        foreach (var (slot, label) in slots)
        {
            map[slot] = label;
        }

        if (!map.ContainsKey(0))
        {
            throw new ArgumentException("slot 0 is required", nameof(slots));
        }

        entities.Add((name, map));
        return this;
    }

    /// <summary>Points one more slot of an already declared entity at <paramref name="label"/>.</summary>
    public SyntheticField Slot(int entity, int slot, string label)
    {
        entities[entity].Slots[slot] = label;
        return this;
    }

    public int CodeStart => 32 + entities.Count * 8 + entities.Count * 32 * 2;

    /// <summary>The section offset of a label.</summary>
    public int At(string label) => CodeStart + Code[label];

    public byte[] BuildScriptSection()
    {
        var code = Code.Assemble();
        var codeStart = CodeStart;
        var stringOffset = codeStart + code.Length;
        var section = new List<byte>();
        void U16(int value)
        {
            section.Add((byte)value);
            section.Add((byte)(value >> 8));
        }

        U16(0x0502);
        section.Add((byte)entities.Count);
        section.Add(0);
        U16(stringOffset);
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

        if (section.Count != codeStart)
        {
            throw new InvalidOperationException("script header layout");
        }

        section.AddRange(code);
        U16(0);
        return section.ToArray();
    }

    public byte[] BuildFieldFile()
    {
        var sections = new byte[9][];
        sections[0] = BuildScriptSection();
        sections[1] = [];
        sections[2] = [0, 0, 0, 0, 0, 2];
        sections[3] = [];
        sections[4] = [0, 0, 0, 0];
        sections[5] = [];
        sections[6] = [];
        var triggers = new byte[0x224 + 12 * 16];
        for (var index = 0; index < 12; index++)
        {
            var at = 0x38 + index * 24;
            triggers[at + 18] = 0xFF;
            triggers[at + 19] = 0x7F;
        }

        for (var index = 0; index < Gateways.Count; index++)
        {
            var at = 0x38 + index * 24;
            for (var coordinate = 0; coordinate < 6; coordinate++)
            {
                BitConverter.GetBytes((short)Gateways[index].Line[coordinate]).CopyTo(triggers, at + coordinate * 2);
            }

            BitConverter.GetBytes((ushort)Gateways[index].Destination).CopyTo(triggers, at + 18);
        }

        sections[7] = triggers;
        sections[8] = [];
        var file = new List<byte> { 0, 0 };
        file.AddRange(BitConverter.GetBytes(9));
        var offset = 2 + 4 + 9 * 4;
        var offsets = new List<int>();
        foreach (var section in sections)
        {
            offsets.Add(offset);
            offset += 4 + section.Length;
        }

        foreach (var value in offsets)
        {
            file.AddRange(BitConverter.GetBytes(value));
        }

        foreach (var section in sections)
        {
            file.AddRange(BitConverter.GetBytes(section.Length));
            file.AddRange(section);
        }

        return file.ToArray();
    }

    public FieldAnalysis Analyze(int fieldId = 900)
    {
        var analysis = FieldAnalysis.Analyze(fieldId, "synthfld", BuildFieldFile(), out var diagnostic);
        return analysis ?? throw new InvalidOperationException(diagnostic);
    }

    private static byte[] Padded(string text, int length)
    {
        var result = new byte[length];
        Encoding.ASCII.GetBytes(text, 0, Math.Min(text.Length, length), result, 0);
        return result;
    }
}
