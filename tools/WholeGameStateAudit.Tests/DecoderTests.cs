using System.Reflection;
using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit.Tests;

/// <summary>Opcode lengths, jump arithmetic and decoding anomalies.</summary>
internal static class DecoderTests
{
    public static void Run()
    {
        FixedLengthsMatchTheShippingTable();
        VariableLengthOpcodes();
        JumpTargetsAreMeasuredFromTheOperand();
        UndecodableAndDynamicCodeIsReportedNotCovered();
    }

    /// <summary>
    /// The generated table must be the shipping catalog's table, byte for byte. The repaired
    /// catalog keeps it in FieldScriptProgram; the 0.6.8 one kept it on the catalog itself.
    /// </summary>
    private static void FixedLengthsMatchTheShippingTable()
    {
        Check.Current = nameof(FixedLengthsMatchTheShippingTable);
        var program = typeof(FieldScriptNavigationCatalog).Assembly.GetType("Ff7.Accessibility.Reloaded.FieldScriptProgram");
        var field = program?.GetField("BaseLengths", BindingFlags.NonPublic | BindingFlags.Static) ??
                    typeof(FieldScriptNavigationCatalog).GetField("OpcodeLengths", BindingFlags.NonPublic | BindingFlags.Static)!;
        var shipping = (byte[])field.GetValue(null)!;
        Check.Equal(256, shipping.Length, "shipping table size");
        var differences = Enumerable.Range(0, 256).Where(op => shipping[op] != Opcodes.BaseLength((byte)op)).ToArray();
        Check.Sequence([], differences, "opcodes whose fixed length differs");
        Check.Equal("REQ", Opcodes.Name(0x01), "0x01 name");
        Check.Equal("CHAR_", Opcodes.Name(0xA1), "0xA1 name");
        Check.Equal("GAMEOVER", Opcodes.Name(0xFF), "0xFF name");
    }

    private static void VariableLengthOpcodes()
    {
        Check.Current = nameof(VariableLengthOpcodes);
        int? Length(params byte[] code) => Opcodes.GetLength(code, 0, out _);
        Check.Equal<int?>(3, Length(0x0F, 0xF5, 0x01), "SPECIAL ARROW has one argument");
        Check.Equal<int?>(4, Length(0x0F, 0xF8, 0x01, 0x02), "SPECIAL SMSPD has two");
        Check.Equal<int?>(4, Length(0x0F, 0xFD, 0x01, 0x02), "SPECIAL SPCNM has two");
        Check.Equal<int?>(2, Length(0x0F, 0xF9), "SPECIAL FLMAT has none");
        Check.Equal<int?>(6, Length(0x0F, 0xF6, 0, 0, 0, 0), "SPECIAL F6 is six bytes (native 0061E78C; both decoders had three)");
        Check.Equal<int?>(4, Length(0x0F, 0xF7, 0, 0), "SPECIAL F7 is four bytes (native 0061E78C)");
        Opcodes.GetLength(new byte[] { 0x0F, 0xE0 }, 0, out var undefinedSpecial);
        Check.True(undefinedSpecial?.Contains("undefined", StringComparison.Ordinal) == true, "an undefined SPECIAL sub-opcode is reported");
        Check.Equal<int?>(7, Length(0x28, 7, 0, 0, 0, 0, 0), "KAWAI carries its own size");
        Opcodes.GetLength(new byte[] { 0x28, 0 }, 0, out var tinyKawai);
        Check.True(tinyKawai is not null, "a KAWAI that declares less than its header is reported");
        var memoryWrite = new byte[140];
        memoryWrite[0] = 0x1C;
        memoryWrite[5] = 200;
        Check.Equal<int?>(6 + 128, Opcodes.GetLength(memoryWrite, 0, out _), "0x1C is capped at 128 data bytes, as Makou does");
        Check.Equal<int?>(null, Opcodes.GetLength(new byte[] { 0x0F }, 0, out _), "a SPECIAL cut off before its sub-opcode is not decoded");
    }

    private static void JumpTargetsAreMeasuredFromTheOperand()
    {
        Check.Current = nameof(JumpTargetsAreMeasuredFromTheOperand);
        int Target(params byte[] bytes)
        {
            Opcodes.TryGetJumpTarget(bytes[0], bytes, 100, out var target);
            return target;
        }

        Check.Equal(106, Target(0x10, 5), "JMPF: operand at +1");
        Check.Equal(401, Target(0x11, 0x2C, 0x01), "JMPFL: operand at +1 (the 0.6.8 walker used +2)");
        Check.Equal(95, Target(0x12, 5), "JMPB: from the opcode");
        Check.Equal(70, Target(0x13, 30, 0), "JMPBL: from the opcode");
        Check.Equal(107, Target(0x14, 0, 0, 0, 0, 2), "IFUB: operand at +5");
        Check.Equal(405, Target(0x15, 0, 0, 0, 0, 0x2C, 0x01), "IFUBL: operand at +5");
        Check.Equal(108, Target(0x16, 0, 0, 0, 0, 0, 0, 1), "IFSW: operand at +7");
        Check.Equal(407, Target(0x17, 0, 0, 0, 0, 0, 0, 0x2C, 0x01), "IFSWL: operand at +7");
        Check.Equal(107, Target(0x30, 0x20, 0, 4), "IFKEY: operand at +3");
        Check.Equal(105, Target(0xCB, 1, 3), "IFPRTYQ: operand at +2");
        Check.Equal(FlowKind.Stall, Opcodes.Flow(0x1B), "0x1B is unimplemented on PC and stops the script");
        Check.Equal(FlowKind.Stall, Opcodes.Flow(0x1A), "so is 0x1A");
        Check.Equal(FlowKind.Stall, Opcodes.Flow(0x1C), "and 0x1C");
        Check.Equal(FlowKind.ReturnTo, Opcodes.Flow(0x07), "RETTO ends the script");
        Check.Equal(FlowKind.LeavesField, Opcodes.Flow(0x60), "MAPJUMP never goes on: the field is loaded again, its own included");
    }

    /// <summary>
    /// Nothing that cannot be decoded is treated as understood: an undefined opcode, a jump
    /// out of the code, a jump into the middle of an instruction, a truncated tail and a
    /// raw memory write are each reported, and the path stops where the engine's behaviour
    /// is unknown.
    /// </summary>
    private static void UndecodableAndDynamicCodeIsReportedNotCovered()
    {
        Check.Current = nameof(UndecodableAndDynamicCodeIsReportedNotCovered);
        var field = new SyntheticField();
        var code = field.Code;
        code.Label("init").Ret().Label("main").Ret();
        code.Label("undefined").Raw(0x0C).Message(0, 1).Ret();
        code.Label("overlap").JmpF("wide").Label("wide").MapJump(1, 0, 0, 0).Ret();
        code.Label("outside").JmpB("init");
        code.Label("memory").Raw(0x1C, 0, 0, 0, 0, 1, 0xAA).Ret();
        code.Label("dynamic").Raw(0x9E, 0x11, 0, 0, 0, 0, 0).Ret();
        code.Label("truncated").Raw(0x40, 0);
        field.Entity("dir", (0, "init"));
        field.Entity("u", (0, "init"), (3, "undefined"), (4, "overlap"), (5, "wide"), (6, "outside"),
            (7, "memory"), (8, "dynamic"), (9, "truncated"));
        var bytes = field.BuildFieldFile();
        var scriptStart = BitConverter.ToInt32(bytes, 6) + 4;
        // One byte into the MAPJUMP: the operand is at +1, so 2 lands on wide + 1.
        bytes[scriptStart + field.At("overlap") + 1] = 2;
        // Far enough back to leave the code entirely.
        bytes[scriptStart + field.At("outside") + 1] = 0xFF;
        var analysis = FieldAnalysis.Analyze(901, "synthfld", bytes, out var diagnostic)
                       ?? throw new InvalidOperationException(diagnostic);
        var kinds = analysis.Flow.Anomalies.Select(anomaly => anomaly.Kind).ToHashSet();
        Check.True(kinds.Contains(AnomalyKind.UndefinedOpcode), "undefined opcode reported");
        Check.True(kinds.Contains(AnomalyKind.RawMemoryOpcode), "0x1C reported as unimplemented on PC");
        Check.Equal(1, analysis.EntriesById["e1.s7"].Reach.Count, "nothing after 0x1C is claimed reachable");
        Check.True(kinds.Contains(AnomalyKind.Truncated), "truncated tail reported");
        Check.True(kinds.Contains(AnomalyKind.Overlap), "jump into an instruction reported as overlap");
        Check.True(kinds.Contains(AnomalyKind.TargetOutsideCode), "jump out of the code reported");
        Check.Equal(1, analysis.EntriesById["e1.s3"].Reach.Count, "nothing after an undefined opcode is claimed reachable");
        Check.Equal(1, analysis.EntriesById["e1.s6"].Reach.Count, "a jump out of the code has no successor");
        Check.True(analysis.EffectsAt.Values.SelectMany(list => list).Any(effect => effect.Kind == "DynamicAddress"),
            "GETX's computed address is reported as dynamic");
    }
}
