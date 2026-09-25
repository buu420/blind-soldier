namespace WholeGameStateAudit.Tests;

/// <summary>
/// The audit's bank-write model against the PC executable's own opcode handlers: every
/// direct call a handler makes to the byte writer 0060FA7D or the word writer 0061031E
/// (legacy ff7_en.exe SHA-256 4274ab2d..., extracted by root from a full Ghidra export,
/// reports/native-field-bank-writers.json). Each entry is (nibble, operand offset, widest
/// write); GTPYE's loop is written out. Indirect writes are not in the native table, so an
/// opcode the audit models as writing but the table lacks is a failure too.
/// </summary>
internal static class NativeWriterTests
{
    private static readonly Dictionary<int, (int Nibble, int Offset, int Width)[]> Native = new()
    {
        [0x0B] = [(1, 3, 1), (2, 4, 1), (3, 5, 1)], // 0061c202
        [0x0F] = [(4, 3, 1)], // 0061e78c
        [0x23] = [(2, 2, 2)], // 0062002c
        [0x3B] = [(1, 2, 2), (2, 3, 2)], // 0061fbbf
        [0x48] = [(2, 6, 1)], // 00618e83
        [0x56] = [(2, 4, 1), (3, 5, 1), (4, 6, 1)], // 0061e3c6
        [0x5A] = [(2, 4, 1)], // 0061e70b
        [0x5D] = [(6, 9, 1)], // 0061ef74
        [0x6E] = [(2, 2, 2)], // 0061e4e8
        [0x73] = [(2, 3, 1)], // 0061852b
        [0x74] = [(2, 3, 1)], // 0061f238
        [0x75] = [(1, 4, 2), (2, 5, 2), (3, 6, 2), (4, 7, 2)], // 00618892
        [0x76] = [(1, 2, 1)], // 006197ad
        [0x77] = [(1, 2, 2)], // 00619882
        [0x78] = [(1, 2, 1)], // 00619956
        [0x79] = [(1, 2, 2)], // 00619a28
        [0x7A] = [(2, 2, 1)], // 00619d2a
        [0x7B] = [(2, 2, 2)], // 00619dda
        [0x7C] = [(2, 2, 1)], // 00619e89
        [0x7D] = [(2, 2, 2)], // 00619f36
        [0x80] = [(1, 2, 1)], // 00610973
        [0x81] = [(1, 2, 2)], // 006109b6
        [0x82] = [(1, 2, 1)], // 00611098
        [0x83] = [(1, 2, 1)], // 00611102
        [0x84] = [(1, 2, 1)], // 0061116e
        [0x85] = [(1, 2, 1)], // 0061974d
        [0x86] = [(1, 2, 2)], // 00619829
        [0x87] = [(1, 2, 1)], // 006198f6
        [0x88] = [(1, 2, 2)], // 006199cf
        [0x89] = [(1, 2, 1)], // 00619a9c
        [0x8A] = [(1, 2, 2)], // 00619afd
        [0x8B] = [(1, 2, 1)], // 00619b57
        [0x8C] = [(1, 2, 2)], // 00619bbe
        [0x8D] = [(1, 2, 1)], // 00619c1b
        [0x8E] = [(1, 2, 2)], // 00619c82
        [0x8F] = [(1, 2, 1)], // 00619522
        [0x90] = [(1, 2, 2)], // 00619582
        [0x91] = [(1, 2, 1)], // 006195db
        [0x92] = [(1, 2, 2)], // 0061963b
        [0x93] = [(1, 2, 1)], // 00619694
        [0x94] = [(1, 2, 2)], // 006196f4
        [0x95] = [(2, 2, 1)], // 00619cdf
        [0x96] = [(2, 2, 2)], // 00619d91
        [0x97] = [(2, 2, 1)], // 00619e3e
        [0x98] = [(2, 2, 2)], // 00619eed
        [0x99] = [(2, 2, 1)], // 00619f9a
        [0x9A] = [(1, 2, 1)], // 006109f9
        [0x9B] = [(1, 2, 1)], // 00610a3c
        [0x9C] = [(1, 3, 2)], // 00610a8d
        [0x9E] = [(4, 5, 1)], // 00610c63
        [0x9F] = [(6, 10, 2)], // 00610dc8
        [0xB7] = [(2, 3, 1)], // 0061849a
        [0xB8] = [(1, 3, 2), (2, 4, 2)], // 006186a6
        [0xB9] = [(2, 3, 2)], // 00618614
        [0xC1] = [(1, 4, 2), (2, 5, 2), (3, 6, 2), (4, 7, 2)], // 0061876b
        [0xD4] = [(4, 9, 2)], // 0061f48c
        [0xD5] = [(4, 9, 2)], // 0061f516
        [0xF7] = [(1, 2, 2), (2, 3, 1)], // 0061fc23
        [0xFA] = [(2, 2, 2)], // 0061a438
        [0xFE] = [(2, 2, 1)], // 0061fc7f
    };

    public static void Run()
    {
        Check.Current = nameof(NativeWriterTests);
        for (var opcode = 0; opcode < 256; opcode++)
        {
            var modelled = Modelled((byte)opcode);
            var native = Native.TryGetValue(opcode, out var descriptors) ? descriptors : [];
            Check.Sequence(
                native.OrderBy(item => item.Offset).Select(item => $"n{item.Nibble}@{item.Offset}w{item.Width}"),
                modelled.OrderBy(item => item.Offset).Select(item => $"n{item.Nibble}@{item.Offset}w{item.Width}"),
                $"{Opcodes.Name((byte)opcode)} (0x{opcode:X2}) write destinations");
        }

        // The word writer truncates on byte banks: a SETWORD to bank 1 writes one byte.
        var toByteBank = new Instruction(0, 0x81, [0x81, 0x10, 5, 1, 2], null);
        var toWordBank = new Instruction(0, 0x81, [0x81, 0x20, 5, 1, 2], null);
        Check.Equal(1, Semantics.Writes(toByteBank).Single().Variable!.Width, "SETWORD to a byte bank writes one byte");
        Check.Equal(2, Semantics.Writes(toWordBank).Single().Variable!.Width, "SETWORD to a word bank writes two");
        var bitNine = new Instruction(0, 0x82, [0x82, 0x10, 5, 9], null);
        Check.True(Semantics.Writes(bitNine).Single().Variable is null, "BITON position 9 changes nothing in the byte");
        var bitThirtyThree = new Instruction(0, 0x82, [0x82, 0x10, 5, 33], null);
        Check.Equal(1, Semantics.Writes(bitThirtyThree).Single().Bit, "BITON position 33 wraps to bit 1");
        Check.Equal(5, Semantics.Writes(bitThirtyThree).Single().Variable!.Address, "on the same byte");
    }

    /// <summary>The destinations the audit's model writes for one opcode.</summary>
    private static IEnumerable<(int Nibble, int Offset, int Width)> Modelled(byte opcode)
    {
        if (opcode == Opcodes.SPECIAL)
        {
            // Only sub-opcode F7 writes a bank variable.
            return [(4, 3, 1)];
        }

        return Opcodes.Variables(opcode)
            .Where(operand => operand.Access != OperandAccess.Read)
            .Select(operand => (operand.Nibble, operand.Offset, operand.VariableSize));
    }
}
