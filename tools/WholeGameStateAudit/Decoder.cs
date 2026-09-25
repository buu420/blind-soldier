namespace WholeGameStateAudit;

internal sealed record Instruction(int Offset, byte Op, byte[] Bytes, string? LengthIssue)
{
    public int Length => Bytes.Length;

    public int End => Offset + Bytes.Length;

    public string Name => Opcodes.DisplayName(Op, Bytes);

    public string Hex => Convert.ToHexString(Bytes);
}

internal enum AnomalyKind
{
    EntryOutsideCode,
    TargetOutsideCode,
    Truncated,
    UndefinedOpcode,
    RawMemoryOpcode,
    UnusualLength,
    Overlap,
    FallsOffCode
}

internal sealed record Anomaly(AnomalyKind Kind, int Offset, string Detail);

/// <summary>
/// Recursive-descent decoding of every byte the engine can execute from a set of entry
/// points. Each offset decodes to exactly one instruction; a path that lands inside an
/// instruction another path decoded is kept and reported as an overlap, because that is
/// what the engine would run. Nothing that cannot be decoded is guessed at.
/// </summary>
internal sealed class ControlFlow
{
    private readonly byte[] code;
    private readonly int codeStart;
    private readonly int codeEnd;
    private readonly Dictionary<int, Instruction> instructions = new();
    private readonly Dictionary<int, int[]> successors = new();
    private readonly Dictionary<int, int?> jumpTargets = new();
    private readonly List<Anomaly> anomalies = [];
    private readonly HashSet<(AnomalyKind, int)> anomalyKeys = [];
    private readonly SortedDictionary<int, int> covered = new();

    public ControlFlow(byte[] code, int codeStart, int codeEnd)
    {
        this.code = code;
        this.codeStart = codeStart;
        this.codeEnd = codeEnd;
    }

    public IReadOnlyDictionary<int, Instruction> Instructions => instructions;

    public IReadOnlyList<Anomaly> Anomalies => anomalies;

    public int CodeStart => codeStart;

    public int CodeEnd => codeEnd;

    public IReadOnlyList<int> Successors(int offset) =>
        successors.TryGetValue(offset, out var next) ? next : [];

    /// <summary>The decoded jump target of a jump or conditional, including an out-of-code one.</summary>
    public int? JumpTarget(int offset) => jumpTargets.TryGetValue(offset, out var target) ? target : null;

    /// <summary>Decodes everything reachable from <paramref name="entries"/> that has not been decoded yet.</summary>
    public void Explore(IEnumerable<int> entries)
    {
        var pending = new Stack<int>(entries);
        while (pending.Count != 0)
        {
            var offset = pending.Pop();
            if (instructions.ContainsKey(offset) || successors.ContainsKey(offset))
            {
                continue;
            }

            if (offset < codeStart || offset >= codeEnd)
            {
                successors[offset] = [];
                continue;
            }

            var length = Opcodes.GetLength(code.AsSpan(0, codeEnd), offset, out var issue);
            if (length is null || offset + length.Value > codeEnd)
            {
                Report(AnomalyKind.Truncated, offset,
                    $"{Opcodes.Name(code[offset])} at {offset} needs {length?.ToString() ?? "?"} bytes, code ends at {codeEnd}" +
                    (issue is null ? string.Empty : $" ({issue})"));
                successors[offset] = [];
                continue;
            }

            var bytes = code.AsSpan(offset, length.Value).ToArray();
            var instruction = new Instruction(offset, bytes[0], bytes, issue);
            instructions[offset] = instruction;
            MarkCovered(instruction);
            if (issue is not null)
            {
                Report(AnomalyKind.UnusualLength, offset, $"{instruction.Name}: {issue}");
            }

            if (Opcodes.IsUndefined(instruction.Op))
            {
                Report(AnomalyKind.UndefinedOpcode, offset, $"undefined opcode {instruction.Op:X2} is reachable");
                successors[offset] = [];
                continue;
            }

            if (Opcodes.IsRawMemory(instruction.Op))
            {
                Report(AnomalyKind.RawMemoryOpcode, offset,
                    $"{instruction.Name} {instruction.Hex} is unimplemented on PC: the script stops here");
            }

            var next = new List<int>(2);
            var flow = Opcodes.Flow(instruction.Op);
            switch (flow)
            {
                case FlowKind.Return:
                case FlowKind.ReturnTo:
                case FlowKind.GameOver:
                case FlowKind.Stall:
                case FlowKind.LeavesField:
                    break;
                case FlowKind.Jump:
                case FlowKind.ConditionalJump:
                    if (flow == FlowKind.ConditionalJump)
                    {
                        AddFallthrough(instruction, next);
                    }

                    if (Opcodes.TryGetJumpTarget(instruction.Op, bytes, offset, out var target))
                    {
                        jumpTargets[offset] = target;
                        if (target < codeStart || target >= codeEnd)
                        {
                            Report(AnomalyKind.TargetOutsideCode, offset,
                                $"{instruction.Name} at {offset} targets {target}, outside code {codeStart}..{codeEnd}");
                        }
                        else if (!next.Contains(target))
                        {
                            next.Add(target);
                        }
                    }

                    break;
                default:
                    AddFallthrough(instruction, next);
                    break;
            }

            successors[offset] = next.ToArray();
            foreach (var successor in next)
            {
                pending.Push(successor);
            }
        }

        foreach (var instruction in instructions.Values)
        {
            CheckOverlap(instruction);
        }
    }

    private void AddFallthrough(Instruction instruction, List<int> next)
    {
        if (instruction.End >= codeEnd)
        {
            Report(AnomalyKind.FallsOffCode, instruction.Offset,
                $"{instruction.Name} at {instruction.Offset} falls through the end of code at {codeEnd}");
            return;
        }

        next.Add(instruction.End);
    }

    private void MarkCovered(Instruction instruction)
    {
        covered[instruction.Offset] = instruction.End;
    }

    private void CheckOverlap(Instruction instruction)
    {
        // Any other decoded instruction that starts strictly inside this one.
        for (var inner = instruction.Offset + 1; inner < instruction.End; inner++)
        {
            if (instructions.TryGetValue(inner, out var other))
            {
                Report(AnomalyKind.Overlap, inner,
                    $"{other.Name} at {inner} starts inside {instruction.Name} {instruction.Offset}..{instruction.End}");
            }
        }
    }

    private void Report(AnomalyKind kind, int offset, string detail)
    {
        if (anomalyKeys.Add((kind, offset)))
        {
            anomalies.Add(new Anomaly(kind, offset, detail));
        }
    }

    /// <summary>
    /// The instructions reachable from <paramref name="entry"/> without leaving the script:
    /// jumps and fall-through are followed, calls are not.
    /// </summary>
    public IReadOnlyList<int> Reach(int entry, Func<Instruction, bool>? stopAfter = null)
    {
        var seen = new HashSet<int>();
        var order = new List<int>();
        var pending = new Stack<int>();
        pending.Push(entry);
        while (pending.Count != 0)
        {
            var offset = pending.Pop();
            if (!instructions.TryGetValue(offset, out var instruction) || !seen.Add(offset))
            {
                continue;
            }

            order.Add(offset);
            if (stopAfter?.Invoke(instruction) == true)
            {
                continue;
            }

            foreach (var successor in Successors(offset))
            {
                pending.Push(successor);
            }
        }

        order.Sort();
        return order;
    }

    /// <summary>
    /// A linear decode of [start, end) exactly as the shipping catalog reads a script: from
    /// the first byte to the first opcode that does not fit, which ends the list silently.
    /// </summary>
    public static (List<Instruction> Instructions, int? StoppedAt) Sweep(byte[] code, int start, int end)
    {
        var list = new List<Instruction>();
        var offset = start;
        while (offset < end)
        {
            var length = Opcodes.GetLength(code.AsSpan(0, end), offset, out var issue);
            if (length is null || length.Value <= 0 || length.Value > end - offset)
            {
                return (list, offset);
            }

            list.Add(new Instruction(offset, code[offset], code.AsSpan(offset, length.Value).ToArray(), issue));
            offset += length.Value;
        }

        return (list, null);
    }
}
