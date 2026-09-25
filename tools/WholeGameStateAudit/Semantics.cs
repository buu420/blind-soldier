namespace WholeGameStateAudit;

/// <summary>
/// A bank variable: <see cref="Block"/> is the savemap block "1".."5" or "T" for the
/// field's temporary bank, <see cref="Address"/> the byte offset in it, and
/// <see cref="Width"/> how many bytes the opcode reads or writes. Banks 1/2, 3/4, 5/6,
/// 11/12, 13/14 and 15/7 address the same 256 bytes; the opcode decides the width.
/// </summary>
internal sealed record VariableRef(string Block, int Bank, int Address, int Width)
{
    public bool IsSavemap => Block != "T";

    public string Key => $"{Block}:{Address}";

    public override string ToString() => $"{Bank}[{Address}]{(Width == 2 ? "w" : string.Empty)}";
}

/// <summary>An operand: a variable when its bank is non-zero, an immediate otherwise.</summary>
internal sealed record Operand(string Name, int Bank, int Raw, VariableRef? Variable, OperandAccess Access)
{
    public bool IsImmediate => Variable is null;

    public override string ToString() => Variable?.ToString() ?? Raw.ToString();
}

internal enum ConditionKind
{
    Compare,
    Key,
    PartyMember,
    Member,
    NanakiName
}

/// <summary>
/// What a conditional tests. The engine falls through when the test holds and takes the
/// jump when it does not.
/// </summary>
internal sealed record Condition(
    ConditionKind Kind,
    Operand? Left,
    Operand? Right,
    int Operator,
    bool Signed,
    int Width,
    int KeyMask,
    int CharacterId)
{
    public static readonly string[] OperatorNames = ["==", "!=", ">", "<", ">=", "<=", "&", "^", "|", "bitON", "bitOFF"];

    private static readonly string[] KeyNames =
    [
        "L2", "R2", "L1", "R1", "Menu", "OK", "Cancel", "Switch", "Assist", "Key9", "Key10", "Start",
        "Up", "Right", "Down", "Left"
    ];

    public bool IsDynamic => Kind is ConditionKind.Key or ConditionKind.PartyMember or ConditionKind.Member or ConditionKind.NanakiName ||
                             Left?.Variable is not null || Right?.Variable is not null;

    public string Text => Kind switch
    {
        ConditionKind.Compare => $"{Left} {(Operator < OperatorNames.Length ? OperatorNames[Operator] : $"op{Operator}?")} {Right}",
        ConditionKind.Key => $"key {DescribeKeys(KeyMask)}",
        ConditionKind.PartyMember => $"character {CharacterId} in party",
        ConditionKind.Member => $"character {CharacterId} available",
        ConditionKind.NanakiName => "Red XIII is named Nanaki",
        _ => "?"
    };

    public static string DescribeKeys(int mask)
    {
        var names = new List<string>();
        for (var bit = 0; bit < 16; bit++)
        {
            if ((mask >> bit & 1) != 0)
            {
                names.Add(KeyNames[bit]);
            }
        }

        return names.Count == 0 ? "(none)" : string.Join("|", names);
    }

    /// <summary>Whether the test is on the confirm button, as native field handlers use it.</summary>
    public bool IsConfirmKey => Kind == ConditionKind.Key && (KeyMask & 0x20) != 0;
}

internal enum CallKind
{
    Request,
    RequestStart,
    RequestWait,
    PartyRequest,
    PartyRequestStart,
    PartyRequestWait,
    ReturnTo
}

/// <summary>A request for a script: an entity (REQ*), a party slot (PREQ*) or this entity (RETTO).</summary>
internal sealed record Call(CallKind Kind, int Target, int Script, int Priority)
{
    public bool IsParty => Kind is CallKind.PartyRequest or CallKind.PartyRequestStart or CallKind.PartyRequestWait;
}

internal static class Semantics
{
    /// <summary>
    /// Opcodes whose two-byte immediates are signed: coordinates (Makou's qint16 fields)
    /// and the signed word comparisons. Only immediates are affected; a variable's address
    /// is read from the same bytes either way.
    /// </summary>
    private static readonly HashSet<byte> SignedWordOpcodes =
    [
        0x09, 0x2C, 0x2D, 0x64, 0x66, 0x68, Opcodes.IFSW, Opcodes.IFSWL, Opcodes.XYZI, Opcodes.XYI, Opcodes.XYZ,
        0xA8, 0xA9, 0xAD, 0xC0, 0xC2, 0xC3, Opcodes.SLINE
    ];

    public static IReadOnlyList<Operand> Operands(Instruction instruction)
    {
        var definitions = Opcodes.Variables(instruction.Op);
        if (definitions.Count == 0)
        {
            return [];
        }

        var bankOffset = Opcodes.BankOffset(instruction.Op);
        var operands = new List<Operand>(definitions.Count);
        foreach (var definition in definitions)
        {
            if (definition.Offset + definition.FieldSize > instruction.Length)
            {
                continue;
            }

            var bank = Opcodes.BankNibble(instruction.Bytes, bankOffset, definition.Nibble);
            var raw = definition.FieldSize switch
            {
                1 => instruction.Bytes[definition.Offset],
                2 when SignedWordOpcodes.Contains(instruction.Op) => BitConverter.ToInt16(instruction.Bytes, definition.Offset),
                2 => BitConverter.ToUInt16(instruction.Bytes, definition.Offset),
                4 => BitConverter.ToInt32(instruction.Bytes, definition.Offset),
                _ => 0
            };
            VariableRef? variable = null;
            if (bank > 0)
            {
                // The address is the operand's first byte; a word helper touches two bytes
                // only on a word bank.
                var block = Opcodes.BankBlock(bank);
                var width = definition.VariableSize == 2 && Opcodes.IsWordBank(bank) ? 2 : 1;
                variable = new VariableRef(block ?? $"?{bank}", bank, instruction.Bytes[definition.Offset], width);
            }

            operands.Add(new Operand(definition.Name, bank, raw, variable, definition.Access));
        }

        return operands;
    }

    public static Condition? ReadCondition(Instruction instruction)
    {
        var bytes = instruction.Bytes;
        switch (instruction.Op)
        {
            case Opcodes.IFUB:
            case Opcodes.IFUBL:
            {
                var operands = Operands(instruction);
                return new Condition(ConditionKind.Compare, operands[0], operands[1], bytes[4], false, 1, 0, -1);
            }
            case Opcodes.IFSW:
            case Opcodes.IFSWL:
            case Opcodes.IFUW:
            case Opcodes.IFUWL:
            {
                var operands = Operands(instruction);
                var signed = instruction.Op is Opcodes.IFSW or Opcodes.IFSWL;
                if (bytes[6] >= 6)
                {
                    // Native comparator 00611BAE narrows both operands to a byte for &, ^, |,
                    // bitON and bitOFF, so only the low byte of a word variable is tested.
                    operands = operands
                        .Select(operand => operand.Variable is null ? operand : operand with { Variable = operand.Variable with { Width = 1 } })
                        .ToArray();
                    return new Condition(ConditionKind.Compare, operands[0], operands[1], bytes[6], signed, 1, 0, -1);
                }

                return new Condition(ConditionKind.Compare, operands[0], operands[1], bytes[6], signed, 2, 0, -1);
            }
            case Opcodes.IFKEY:
            case Opcodes.IFKEYON:
            case Opcodes.IFKEYOFF:
                return new Condition(ConditionKind.Key, null, null, instruction.Op - Opcodes.IFKEY, false, 0,
                    BitConverter.ToUInt16(bytes, 1), -1);
            case Opcodes.IFPRTYQ:
                return new Condition(ConditionKind.PartyMember, null, null, 0, false, 0, 0, bytes[1]);
            case Opcodes.IFMEMBQ:
                return new Condition(ConditionKind.Member, null, null, 0, false, 0, 0, bytes[1]);
            case Opcodes.IFNANAKI:
                return new Condition(ConditionKind.NanakiName, null, null, 0, false, 0, 0, -1);
            default:
                return null;
        }
    }

    public static Call? ReadCall(Instruction instruction)
    {
        var bytes = instruction.Bytes;
        return instruction.Op switch
        {
            Opcodes.REQ => new Call(CallKind.Request, bytes[1], bytes[2] & 0x1F, bytes[2] >> 5),
            Opcodes.REQSW => new Call(CallKind.RequestStart, bytes[1], bytes[2] & 0x1F, bytes[2] >> 5),
            Opcodes.REQEW => new Call(CallKind.RequestWait, bytes[1], bytes[2] & 0x1F, bytes[2] >> 5),
            Opcodes.PREQ => new Call(CallKind.PartyRequest, bytes[1], bytes[2] & 0x1F, bytes[2] >> 5),
            Opcodes.PRQSW => new Call(CallKind.PartyRequestStart, bytes[1], bytes[2] & 0x1F, bytes[2] >> 5),
            Opcodes.PRQEW => new Call(CallKind.PartyRequestWait, bytes[1], bytes[2] & 0x1F, bytes[2] >> 5),
            Opcodes.RETTO => new Call(CallKind.ReturnTo, -1, bytes[1] & 0x1F, bytes[1] >> 5),
            _ => null
        };
    }

    /// <summary>
    /// The variables an instruction writes, with the bit it sets or clears when that is a
    /// constant. BITON/BITOFF/BITXOR address bit <c>n</c> of the byte at the variable's
    /// address plus <c>n / 8</c>; a bit index from a variable is reported as unknown.
    /// </summary>
    public static IReadOnlyList<VariableWrite> Writes(Instruction instruction)
    {
        var writes = new List<VariableWrite>();
        if (instruction.Op == Opcodes.SPECIAL && instruction.Length >= 4 && instruction.Bytes[1] == 0xF7)
        {
            // GMSPD: the game speed into the byte at operand 3, bank from nibble 4.
            var bank = instruction.Bytes[2] & 0x0F;
            writes.Add(bank == 0
                ? new VariableWrite(null, instruction.Op, -1, null, "GMSPD destination has bank 0")
                : new VariableWrite(new VariableRef(Opcodes.BankBlock(bank) ?? $"?{bank}", bank, instruction.Bytes[3], 1), instruction.Op, -1, null, null));
            return writes;
        }

        if (instruction.Op == Opcodes.SPECIAL && instruction.Length >= 2 && instruction.Bytes[1] == 0xF6)
        {
            writes.Add(new VariableWrite(null, instruction.Op, -1, null, "PNAME copies a string into the savemap; not modelled"));
            return writes;
        }

        var operands = Operands(instruction);
        foreach (var operand in operands)
        {
            if (operand.Access == OperandAccess.Read)
            {
                continue;
            }

            if (operand.Variable is null)
            {
                writes.Add(new VariableWrite(null, instruction.Op, -1, null, $"{operand.Name} has bank 0"));
                continue;
            }

            if (operand.Access == OperandAccess.BitWrite)
            {
                // Native 00611098: byte = byte op (1 << (position & 31)), truncated to the
                // byte, so positions 8-31 change nothing and 32-39 wrap to 0-7.
                var position = operands.FirstOrDefault(other => other.Name == "position");
                var variable = operand.Variable with { Width = 1 };
                if (position is { IsImmediate: true })
                {
                    var bit = position.Raw & 31;
                    if (bit >= 8)
                    {
                        writes.Add(new VariableWrite(null, instruction.Op, -1, null, $"bit position {position.Raw} changes nothing in the byte"));
                    }
                    else
                    {
                        writes.Add(new VariableWrite(variable, instruction.Op, bit, null, null));
                    }
                }
                else
                {
                    writes.Add(new VariableWrite(variable, instruction.Op, -1, null, "bit index from a variable"));
                }

                continue;
            }

            int? value = null;
            if (instruction.Op is Opcodes.SETBYTE or Opcodes.SETWORD)
            {
                var source = operands.FirstOrDefault(other => other.Name == "value");
                if (source is { IsImmediate: true })
                {
                    value = source.Raw;
                }
            }

            writes.Add(new VariableWrite(operand.Variable, instruction.Op, -1, value, null));
        }

        return writes;
    }

    public static IReadOnlyList<VariableRef> Reads(Instruction instruction) =>
        Operands(instruction)
            .Where(operand => operand.Access == OperandAccess.Read && operand.Variable is not null)
            .Select(operand => operand.Variable!)
            .ToArray();

    /// <summary>
    /// Opcodes whose memory addresses are computed at run time (SETX/GETX/SEARCHX index an
    /// array by a variable). Their reads and writes cannot be named statically.
    /// </summary>
    public static bool HasDynamicAddress(Instruction instruction) =>
        instruction.Op is Opcodes.SETX or Opcodes.GETX or Opcodes.SEARCHX;
}

internal sealed record VariableWrite(VariableRef? Variable, byte Opcode, int Bit, int? Value, string? Note)
{
    public string OperationName => Opcodes.Name(Opcode);
}
