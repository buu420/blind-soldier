namespace WholeGameStateAudit;

/// <summary>
/// The shipping catalog's decode of one field, tied back to absolute section offsets.
/// The catalog hands out byte slices with no start offset, so each slice is matched to the
/// audit's replica of <c>ParseScriptGroups</c>; any disagreement is reported rather than
/// papered over, and the replica is only trusted where every slice matched.
/// </summary>
internal sealed class ShippingView
{
    public ShippingView(FieldAnalysis analysis)
    {
        Analysis = analysis;
        (Groups, Native) = ShippingReflection.ParseScriptGroups(analysis.Section.Data);
        var section = analysis.Section;
        if (Groups.Count != section.EntityCount)
        {
            Mismatches.Add($"shipping parsed {Groups.Count} groups, section has {section.EntityCount}");
        }

        foreach (var group in Groups)
        {
            if (group.Index >= section.EntityCount)
            {
                continue;
            }

            var entity = section.Entities[group.Index];
            var replicaSlots = entity.Slots.Where(slot => slot.ShippingRange is not null).Select(slot => slot.Index).ToHashSet();
            if (!replicaSlots.SetEquals(group.Scripts.Keys))
            {
                Mismatches.Add($"entity {group.Index}: shipping slots {string.Join(",", group.Scripts.Keys.Order())} vs replica {string.Join(",", replicaSlots.Order())}");
            }

            foreach (var (slot, bytes) in group.Scripts)
            {
                var range = entity.Slots[slot].ShippingRange;
                if (range is null)
                {
                    continue;
                }

                var (start, end) = range.Value;
                if (end - start != bytes.Length || !section.Data.AsSpan(start, end - start).SequenceEqual(bytes))
                {
                    Mismatches.Add($"entity {group.Index} slot {slot}: shipping slice of {bytes.Length} bytes does not match replica {start}..{end}");
                    continue;
                }

                Starts[(group.Index, slot)] = start;
            }
        }
    }

    public FieldAnalysis Analysis { get; }

    public IReadOnlyList<ShippingGroup> Groups { get; }

    public object Native { get; }

    public List<string> Mismatches { get; } = [];

    /// <summary>The absolute start of every shipping script slice, keyed by (entity, slot).</summary>
    public Dictionary<(int Entity, int Slot), int> Starts { get; } = new();

    public bool HasScript(int entity, int slot) =>
        entity >= 0 && entity < Groups.Count && Groups[entity].Scripts.ContainsKey(slot);

    public IReadOnlyList<ShippingOpcode> Opcodes(int entity, int slot) =>
        HasScript(entity, slot) ? ShippingReflection.ReadOpcodes(Groups[entity].Scripts[slot]) : [];

    public bool IsPlayableCharacterGroup(int entity) =>
        HasAnyScript(entity) && Groups[entity].Scripts.Values.Any(script =>
            ShippingReflection.ReadOpcodes(script).Any(opcode => opcode.Id == 0xA0));

    public bool IsLineGroup(int entity) =>
        HasScript(entity, 0) && Opcodes(entity, 0).Any(opcode => opcode.Id == 0xD0);

    private bool HasAnyScript(int entity) => entity >= 0 && entity < Groups.Count && Groups[entity].Scripts.Count > 0;
}

[Flags]
internal enum WalkFix
{
    None = 0,
    Jmpfl = 1,
    AllConditionals = 2,
    Retto = 4,
    AliasedRequests = 8,
    CrossSlice = 16,
    All = Jmpfl | AllConditionals | Retto | AliasedRequests | CrossSlice
}

/// <summary>
/// Which absolute instructions the 0.6.8 navigation walker (CollectNavigationActionPaths as
/// released) can visit from one script, with its exact control-flow rules and none of its
/// constant folding or party agreement (which can only prune further): it starts at the
/// slice's first byte, stops at RET, follows REQ/REQSW/REQEW into present slots, follows
/// PREQ* into every playable character's same-numbered slot, forks only on IFUB/IFUBL/IFSW/
/// IFSWL/IFUW/IFUWL, measures JMPFL from byte 2, treats everything else (RETTO, IFKEY*,
/// IFPRTYQ, IFMEMBQ, 0x1A-0x1C, GAMEOVER) as falling through, and ends a path whose target
/// is not an opcode start in its own slice.
///
/// <para>Each <see cref="WalkFix"/> replaces one of those rules with the engine's, so the
/// walk can say which rule an omission of the baseline comes from. It describes that
/// released walker only: the repaired catalog is compared on what it publishes
/// (FieldReport's catalog comparison), and this replica is run only when the catalog compiled
/// in is the baseline (<see cref="ShippingReflection.IsRepairedCatalog"/>).</para>
/// </summary>
internal sealed class BaselineShippingWalk
{
    private readonly ShippingView view;
    private readonly WalkFix fixes;
    private readonly Dictionary<(int, int), HashSet<int>> memo = new();

    public BaselineShippingWalk(ShippingView view, WalkFix fixes = WalkFix.None)
    {
        this.view = view;
        this.fixes = fixes;
    }

    public HashSet<int> Visit(int entity, int slot) => Visit(entity, slot, new HashSet<(int, int)>());

    private HashSet<int> Visit(int entity, int slot, HashSet<(int, int)> callStack)
    {
        var section = view.Analysis.Section;
        if (!view.HasScript(entity, slot) && fixes.HasFlag(WalkFix.AliasedRequests) &&
            entity >= 0 && entity < section.EntityCount && slot < section.SlotCount &&
            section.Entities[entity].Slots[slot].AliasOf is { } owner)
        {
            slot = owner;
        }

        var key = (entity, slot);
        if (!view.HasScript(entity, slot) || !view.Starts.TryGetValue(key, out var start) || callStack.Contains(key))
        {
            return [];
        }

        // A walk cut short by a recursive call depends on who asked, so only top-level
        // walks are remembered.
        if (callStack.Count == 0 && memo.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var nested = new HashSet<(int, int)>(callStack) { key };
        var opcodes = view.Opcodes(entity, slot).ToDictionary(opcode => start + opcode.Offset);
        var visited = new HashSet<int>();
        if (opcodes.Count == 0)
        {
            return visited;
        }

        var pending = new Stack<int>();
        pending.Push(start);
        var seen = new HashSet<int>();
        while (pending.Count != 0)
        {
            var offset = pending.Pop();
            if (!seen.Add(offset))
            {
                continue;
            }

            byte id;
            byte[] bytes;
            if (opcodes.TryGetValue(offset, out var opcode))
            {
                id = opcode.Id;
                bytes = opcode.Bytes;
            }
            else if (fixes.HasFlag(WalkFix.CrossSlice) &&
                     view.Analysis.Flow.Instructions.TryGetValue(offset, out var instruction))
            {
                id = instruction.Op;
                bytes = instruction.Bytes;
            }
            else
            {
                continue;
            }

            visited.Add(offset);
            var next = offset + bytes.Length;
            switch (id)
            {
                case 0x00:
                    continue;
                case >= 0x01 and <= 0x03 when bytes.Length >= 3:
                    visited.UnionWith(Visit(bytes[1], bytes[2] & 0x1F, nested));
                    pending.Push(next);
                    continue;
                case >= 0x04 and <= 0x06 when bytes.Length >= 3:
                    for (var group = 0; group < view.Groups.Count; group++)
                    {
                        if (view.HasScript(group, bytes[2] & 0x1F) && view.IsPlayableCharacterGroup(group))
                        {
                            visited.UnionWith(Visit(group, bytes[2] & 0x1F, nested));
                        }
                    }

                    pending.Push(next);
                    continue;
                case Opcodes.RETTO when fixes.HasFlag(WalkFix.Retto) && bytes.Length >= 2:
                    visited.UnionWith(Visit(entity, bytes[1] & 0x1F, nested));
                    continue;
                case 0x14 when bytes.Length >= 6:
                    pending.Push(offset + 5 + bytes[5]);
                    pending.Push(next);
                    continue;
                case 0x15 when bytes.Length >= 7:
                    pending.Push(offset + 5 + BitConverter.ToUInt16(bytes, 5));
                    pending.Push(next);
                    continue;
                case 0x16 or 0x18 when bytes.Length >= 8:
                    pending.Push(offset + 7 + bytes[7]);
                    pending.Push(next);
                    continue;
                case 0x17 or 0x19 when bytes.Length >= 9:
                    pending.Push(offset + 7 + BitConverter.ToUInt16(bytes, 7));
                    pending.Push(next);
                    continue;
                case >= 0x30 and <= 0x32 when fixes.HasFlag(WalkFix.AllConditionals) && bytes.Length >= 4:
                    pending.Push(offset + 3 + bytes[3]);
                    pending.Push(next);
                    continue;
                case 0xCB or 0xCC when fixes.HasFlag(WalkFix.AllConditionals) && bytes.Length >= 3:
                    pending.Push(offset + 2 + bytes[2]);
                    pending.Push(next);
                    continue;
                case 0x10 when bytes.Length >= 2:
                    pending.Push(offset + bytes[1] + 1);
                    continue;
                case 0x11 when bytes.Length >= 3:
                    pending.Push(offset + BitConverter.ToUInt16(bytes, 1) + (fixes.HasFlag(WalkFix.Jmpfl) ? 1 : 2));
                    continue;
                case 0x12 when bytes.Length >= 2:
                    pending.Push(offset - bytes[1]);
                    continue;
                case 0x13 when bytes.Length >= 3:
                    pending.Push(offset - BitConverter.ToUInt16(bytes, 1));
                    continue;
                default:
                    pending.Push(next);
                    continue;
            }
        }

        if (callStack.Count == 0)
        {
            memo[key] = visited;
        }

        return visited;
    }
}
