namespace WholeGameStateAudit;

internal enum EntityKind
{
    Director,
    Model,
    PartyCharacter,
    Line
}

internal enum EntryKind
{
    Init,
    Main,
    Talk,
    Contact,
    LineOk,
    LineMove,
    LineMoveAlt,
    LineGo,
    LineGoOnce,
    LineGoAway,
    Script
}

/// <summary>One side of a conditional on the way to an instruction: the test at <see cref="At"/> held or failed.</summary>
internal readonly record struct Literal(int At, bool Holds)
{
    public long Key => ((long)At << 1) | (Holds ? 1L : 0L);

    public static Literal FromKey(long key) => new((int)(key >> 1), (key & 1) != 0);
}

internal sealed class EntityModel
{
    public required int Index { get; init; }

    public required string Name { get; init; }

    public EntityKind Kind { get; set; } = EntityKind.Director;

    public List<int> ModelIds { get; } = [];

    public List<int> PartyCharacters { get; } = [];

    public int[]? Line { get; set; }

    public bool DynamicLine { get; set; }

    public int InitOffset { get; set; }

    /// <summary>The returns reachable from the start of script 0 before any other return.</summary>
    public List<int> InitReturns { get; } = [];

    /// <summary>Makou's static split (first return outside a pending forward jump), when it finds one.</summary>
    public int? MakouMainOffset { get; set; }

    /// <summary>Every place the main script can begin: Makou's split and each reachable first return.</summary>
    public SortedSet<int> MainOffsets { get; } = [];

    public List<string> Issues { get; } = [];
}

internal sealed class EntryModel
{
    public required string Id { get; init; }

    public required int Entity { get; init; }

    /// <summary>The pointer slot, or -1 for a main entry.</summary>
    public required int Slot { get; init; }

    public required EntryKind Kind { get; init; }

    public required int Offset { get; init; }

    /// <summary>Whether the engine or the player starts this entry without any script asking for it.</summary>
    public required bool EngineTriggered { get; init; }

    public int? AliasOf { get; init; }

    public IReadOnlyList<int> Reach { get; set; } = [];

    public Dictionary<int, HashSet<long>> Must { get; } = new();

    public Dictionary<int, HashSet<long>> May { get; } = new();

    /// <summary>Whether a live entry (engine-triggered, or called from one) reaches this entry.</summary>
    public bool Live { get; set; }

    public List<string> CalledFrom { get; } = [];
}

internal sealed record CallSite(int At, string FromEntry, Call Call, IReadOnlyList<string> Targets, string? Problem);

internal sealed record Effect(int At, string Kind, string Detail);

internal sealed record ClosureEffect(
    Effect Effect,
    string EntryId,
    IReadOnlyList<string> Chain,
    IReadOnlyList<Literal> Must,
    int PathDependent,
    bool Dynamic);

/// <summary>
/// The engine's view of one field: what every entry point can execute, which calls it
/// makes, what each reachable instruction does, and which tests guard it.
/// </summary>
internal sealed class FieldAnalysis
{
    public const int MaximumCallDepth = 12;

    private FieldAnalysis(int fieldId, string fieldName, FieldFile file, ScriptSection section)
    {
        FieldId = fieldId;
        FieldName = fieldName;
        File = file;
        Section = section;
        Flow = new ControlFlow(section.Data, section.CodeStart, section.CodeEnd);
    }

    public int FieldId { get; }

    public string FieldName { get; }

    public FieldFile File { get; }

    public ScriptSection Section { get; }

    public ControlFlow Flow { get; }

    public List<EntityModel> Entities { get; } = [];

    public List<EntryModel> Entries { get; } = [];

    public Dictionary<string, EntryModel> EntriesById { get; } = new(StringComparer.Ordinal);

    public List<CallSite> CallSites { get; } = [];

    public Dictionary<int, List<Effect>> EffectsAt { get; } = new();

    public Dictionary<int, Condition> Conditions { get; } = new();

    /// <summary>Variables written by PXYZI, AXYZI, GETAI and GETAXY: tests on them are position tests.</summary>
    public HashSet<string> PositionVariables { get; } = new(StringComparer.Ordinal);

    public List<string> Issues { get; } = [];

    public static FieldAnalysis? Analyze(int fieldId, string fieldName, byte[] fieldBytes, out string diagnostic)
    {
        var file = new FieldFile(fieldBytes);
        var sectionBytes = file.Section(0);
        if (sectionBytes is null)
        {
            diagnostic = "no script section";
            return null;
        }

        var section = ScriptSection.TryParse(sectionBytes, out diagnostic);
        if (section is null)
        {
            return null;
        }

        var analysis = new FieldAnalysis(fieldId, fieldName, file, section);
        analysis.Build();
        return analysis;
    }

    private void Build()
    {
        foreach (var entity in Section.Entities)
        {
            foreach (var slot in entity.Slots)
            {
                if (!Section.IsInCode(slot.Pointer))
                {
                    Issues.Add($"entity {entity.Index} slot {slot.Index} pointer {slot.Pointer} is outside code {Section.CodeStart}..{Section.CodeEnd}");
                }
            }
        }

        Flow.Explore(Section.Entities.SelectMany(entity => entity.Pointers).Where(Section.IsInCode));
        foreach (var entity in Section.Entities)
        {
            Entities.Add(BuildEntity(entity));
        }

        Flow.Explore(Entities.SelectMany(entity => entity.MainOffsets));
        foreach (var instruction in Flow.Instructions.Values)
        {
            if (Semantics.ReadCondition(instruction) is { } condition)
            {
                Conditions[instruction.Offset] = condition;
            }

            if (instruction.Op is Opcodes.PXYZI or 0xC1 or 0xB9 or 0xB8)
            {
                foreach (var write in Semantics.Writes(instruction))
                {
                    if (write.Variable is not null)
                    {
                        PositionVariables.Add(write.Variable.Key);
                        if (write.Variable.Width == 2)
                        {
                            PositionVariables.Add($"{write.Variable.Block}:{write.Variable.Address + 1}");
                        }
                    }
                }
            }
        }

        foreach (var entity in Entities)
        {
            var scriptEntity = Section.Entities[entity.Index];
            for (var slot = 0; slot < Section.SlotCount; slot++)
            {
                var pointer = scriptEntity.Pointers[slot];
                var kind = SlotKind(entity, slot);
                // A party character's model is on screen whenever it is not the one being
                // led (Barret in the Sector 7 slums, Red XIII in the Jenova chamber, Tifa at
                // the Nibelheim well), and the story needs its Talk: it is an engine event
                // like any model's, available while that character is not leading.
                var triggered = slot == 0 ||
                                (entity.Kind is EntityKind.Model or EntityKind.PartyCharacter && kind is EntryKind.Talk or EntryKind.Contact) ||
                                (entity.Kind == EntityKind.Line && kind is >= EntryKind.LineOk and <= EntryKind.LineGoAway);
                AddEntry(new EntryModel
                {
                    Id = $"e{entity.Index}.s{slot}",
                    Entity = entity.Index,
                    Slot = slot,
                    Kind = kind,
                    Offset = pointer,
                    EngineTriggered = triggered && Section.IsInCode(pointer),
                    AliasOf = scriptEntity.Slots[slot].AliasOf
                });
            }

            foreach (var main in entity.MainOffsets)
            {
                AddEntry(new EntryModel
                {
                    Id = $"e{entity.Index}.main@{main}",
                    Entity = entity.Index,
                    Slot = -1,
                    Kind = EntryKind.Main,
                    Offset = main,
                    EngineTriggered = Section.IsInCode(main)
                });
            }
        }

        foreach (var entry in Entries)
        {
            entry.Reach = Section.IsInCode(entry.Offset) ? Flow.Reach(entry.Offset) : [];
            ComputeGuards(entry);
        }

        foreach (var entry in Entries)
        {
            foreach (var at in entry.Reach)
            {
                var instruction = Flow.Instructions[at];
                if (Semantics.ReadCall(instruction) is { } call)
                {
                    CallSites.Add(ResolveCall(entry, instruction, call));
                }
            }
        }

        PropagateLiveness();
        foreach (var instruction in Flow.Instructions.Values)
        {
            var effects = Classify(instruction);
            if (effects.Count != 0)
            {
                EffectsAt[instruction.Offset] = effects;
            }
        }
    }

    private void AddEntry(EntryModel entry)
    {
        Entries.Add(entry);
        EntriesById[entry.Id] = entry;
    }

    private static EntryKind SlotKind(EntityModel entity, int slot) => slot switch
    {
        0 => EntryKind.Init,
        1 when entity.Kind is EntityKind.Model or EntityKind.PartyCharacter => EntryKind.Talk,
        2 when entity.Kind is EntityKind.Model or EntityKind.PartyCharacter => EntryKind.Contact,
        1 when entity.Kind == EntityKind.Line => EntryKind.LineOk,
        2 when entity.Kind == EntityKind.Line => EntryKind.LineMove,
        3 when entity.Kind == EntityKind.Line => EntryKind.LineMoveAlt,
        4 when entity.Kind == EntityKind.Line => EntryKind.LineGo,
        5 when entity.Kind == EntityKind.Line => EntryKind.LineGoOnce,
        6 when entity.Kind == EntityKind.Line => EntryKind.LineGoAway,
        _ => EntryKind.Script
    };

    private EntityModel BuildEntity(ScriptEntity scriptEntity)
    {
        var entity = new EntityModel { Index = scriptEntity.Index, Name = scriptEntity.Name, InitOffset = scriptEntity.Pointers[0] };
        if (!Section.IsInCode(entity.InitOffset))
        {
            entity.Issues.Add("script 0 pointer outside code");
            return entity;
        }

        var init = Flow.Reach(entity.InitOffset);
        foreach (var at in init)
        {
            var instruction = Flow.Instructions[at];
            switch (instruction.Op)
            {
                case Opcodes.RET:
                case Opcodes.RETTO:
                    entity.InitReturns.Add(at);
                    break;
                case Opcodes.CHAR:
                    entity.ModelIds.Add(instruction.Bytes[1]);
                    break;
                case Opcodes.PC:
                    entity.PartyCharacters.Add(instruction.Bytes[1]);
                    break;
                case Opcodes.LINE when entity.Line is null:
                    entity.Line =
                    [
                        BitConverter.ToInt16(instruction.Bytes, 1), BitConverter.ToInt16(instruction.Bytes, 3),
                        BitConverter.ToInt16(instruction.Bytes, 5), BitConverter.ToInt16(instruction.Bytes, 7),
                        BitConverter.ToInt16(instruction.Bytes, 9), BitConverter.ToInt16(instruction.Bytes, 11)
                    ];
                    break;
            }
        }

        entity.Kind = entity.PartyCharacters.Count > 0
            ? EntityKind.PartyCharacter
            : entity.ModelIds.Count > 0
                ? EntityKind.Model
                : entity.Line is not null
                    ? EntityKind.Line
                    : EntityKind.Director;
        if (entity.ModelIds.Count > 0 && entity.Line is not null)
        {
            entity.Issues.Add("init loads a model and defines a line");
        }

        entity.MakouMainOffset = MakouSplit(scriptEntity);
        if (entity.MakouMainOffset is { } makouMain)
        {
            entity.MainOffsets.Add(makouMain);
        }

        foreach (var ret in entity.InitReturns)
        {
            var end = Flow.Instructions[ret].End;
            if (end < Section.CodeEnd)
            {
                entity.MainOffsets.Add(end);
            }
        }

        if (entity.InitReturns.Count == 0)
        {
            entity.Issues.Add("init never returns");
        }
        else if (entity.MainOffsets.Count > 1)
        {
            entity.Issues.Add($"init/main split ambiguous: {string.Join(",", entity.MainOffsets)} (Makou {entity.MakouMainOffset?.ToString() ?? "none"})");
        }

        return entity;
    }

    /// <summary>
    /// Makou Reactor's Script::splitScriptAtReturn over a linear decode of script 0: the
    /// first RET or RETTO that is not inside a pending forward jump. Only one pending jump
    /// is tracked, exactly as Makou does.
    /// </summary>
    private int? MakouSplit(ScriptEntity scriptEntity)
    {
        var range = scriptEntity.Slots[0].MakouRange ?? (scriptEntity.Pointers[0], Section.CodeEnd);
        var (sweep, _) = ControlFlow.Sweep(Section.Data, range.Start, Math.Min(range.End, Section.CodeEnd));
        var pending = -1;
        foreach (var instruction in sweep)
        {
            if (pending != -1)
            {
                if (instruction.Offset != pending)
                {
                    continue;
                }

                pending = -1;
            }

            var isForwardJump =
                Opcodes.Flow(instruction.Op) is FlowKind.Jump or FlowKind.ConditionalJump &&
                instruction.Op is not (Opcodes.JMPB or Opcodes.JMPBL);
            if (isForwardJump && Opcodes.TryGetJumpTarget(instruction.Op, instruction.Bytes, instruction.Offset, out var target))
            {
                pending = target;
                continue;
            }

            if (instruction.Op is Opcodes.RET or Opcodes.RETTO)
            {
                return instruction.End;
            }
        }

        return null;
    }

    private void ComputeGuards(EntryModel entry)
    {
        if (entry.Reach.Count == 0)
        {
            return;
        }

        var inReach = entry.Reach.ToHashSet();
        var must = new Dictionary<int, HashSet<long>?>();
        var may = new Dictionary<int, HashSet<long>>();
        foreach (var at in entry.Reach)
        {
            must[at] = null;
            may[at] = [];
        }

        must[entry.Offset] = [];
        var pending = new Queue<int>();
        pending.Enqueue(entry.Offset);
        var queued = new HashSet<int> { entry.Offset };
        while (pending.Count != 0)
        {
            var at = pending.Dequeue();
            queued.Remove(at);
            var current = must[at]!;
            var currentMay = may[at];
            foreach (var successor in Flow.Successors(at))
            {
                if (!inReach.Contains(successor))
                {
                    continue;
                }

                var literal = EdgeLiteral(at, successor);
                var candidate = new HashSet<long>(current);
                if (literal is { } edge)
                {
                    candidate.Add(edge.Key);
                }

                var changed = false;
                var existing = must[successor];
                if (existing is null)
                {
                    must[successor] = candidate;
                    changed = true;
                }
                else
                {
                    var before = existing.Count;
                    existing.IntersectWith(candidate);
                    changed = existing.Count != before;
                }

                var targetMay = may[successor];
                var mayBefore = targetMay.Count;
                targetMay.UnionWith(currentMay);
                if (literal is { } mayEdge)
                {
                    targetMay.Add(mayEdge.Key);
                }

                changed |= targetMay.Count != mayBefore;
                if (changed && queued.Add(successor))
                {
                    pending.Enqueue(successor);
                }
            }
        }

        foreach (var at in entry.Reach)
        {
            entry.Must[at] = must[at] ?? [];
            entry.May[at] = may[at];
        }
    }

    private Literal? EdgeLiteral(int from, int to)
    {
        var instruction = Flow.Instructions[from];
        if (Opcodes.Flow(instruction.Op) != FlowKind.ConditionalJump)
        {
            return null;
        }

        var fallthrough = instruction.End;
        var target = Flow.JumpTarget(from);
        if (to == fallthrough && to == target)
        {
            return null;
        }

        return to == fallthrough ? new Literal(from, true) : new Literal(from, false);
    }

    private CallSite ResolveCall(EntryModel entry, Instruction instruction, Call call)
    {
        switch (call.Kind)
        {
            case CallKind.ReturnTo:
                return Resolve(entry.Entity, call.Script);
            case CallKind.Request:
            case CallKind.RequestStart:
            case CallKind.RequestWait:
                return Resolve(call.Target, call.Script);
            default:
            {
                // A party slot names whoever is in it, so every entity the field binds to a
                // playable character is a candidate and none is certain.
                var targets = Entities
                    .Where(entity => entity.Kind == EntityKind.PartyCharacter)
                    .Select(entity => $"e{entity.Index}.s{call.Script}")
                    .Where(EntriesById.ContainsKey)
                    .ToArray();
                return new CallSite(instruction.Offset, entry.Id, call, targets,
                    targets.Length == 0 ? "party slot request with no party-character entity" : "dynamic: party slot");
            }
        }

        CallSite Resolve(int entity, int script)
        {
            if (entity < 0 || entity >= Section.EntityCount)
            {
                return new CallSite(instruction.Offset, entry.Id, call, [], $"entity {entity} does not exist");
            }

            if (script >= Section.SlotCount)
            {
                return new CallSite(instruction.Offset, entry.Id, call, [], $"script {script} does not exist");
            }

            var target = $"e{entity}.s{script}";
            var targetEntry = EntriesById[target];
            if (!Section.IsInCode(targetEntry.Offset))
            {
                return new CallSite(instruction.Offset, entry.Id, call, [target], "target pointer outside code");
            }

            return new CallSite(instruction.Offset, entry.Id, call, [target],
                targetEntry.AliasOf is { } alias ? $"target slot aliases slot {alias}" : null);
        }
    }

    private void PropagateLiveness()
    {
        var sitesByEntry = CallSites.GroupBy(site => site.FromEntry).ToDictionary(group => group.Key, group => group.ToArray());
        var pending = new Queue<EntryModel>(Entries.Where(entry => entry.EngineTriggered));
        foreach (var entry in pending)
        {
            entry.Live = true;
        }

        while (pending.Count != 0)
        {
            var entry = pending.Dequeue();
            if (!sitesByEntry.TryGetValue(entry.Id, out var sites))
            {
                continue;
            }

            foreach (var site in sites)
            {
                foreach (var target in site.Targets)
                {
                    var targetEntry = EntriesById[target];
                    if (!targetEntry.CalledFrom.Contains(entry.Id))
                    {
                        targetEntry.CalledFrom.Add(entry.Id);
                    }

                    if (!targetEntry.Live && targetEntry.Reach.Count > 0)
                    {
                        targetEntry.Live = true;
                        pending.Enqueue(targetEntry);
                    }
                }
            }
        }
    }

    public string Describe(Literal literal) =>
        Conditions.TryGetValue(literal.At, out var condition)
            ? $"{(literal.Holds ? string.Empty : "not ")}({condition.Text})"
            : $"?{literal.At}";

    private Dictionary<string, List<CallSite>>? sitesByEntry;

    public IReadOnlyList<CallSite> SitesIn(string entryId)
    {
        sitesByEntry ??= CallSites.GroupBy(site => site.FromEntry).ToDictionary(group => group.Key, group => group.ToList());
        return sitesByEntry.TryGetValue(entryId, out var sites) ? sites : [];
    }

    /// <summary>
    /// Everything <paramref name="root"/> can make happen: its own effects and those of every
    /// script it requests, breadth first so each callee is reached by its shortest chain.
    /// Each effect carries the tests that must hold on that chain: the call sites' own
    /// guards followed by the callee's. Party-slot requests are included and marked
    /// dynamic, because who answers them depends on the live party.
    /// </summary>
    public (IReadOnlyList<ClosureEffect> Effects, bool DepthLimited) Closure(EntryModel root)
    {
        var results = new List<ClosureEffect>();
        var expanded = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        var pending = new Queue<(EntryModel Entry, List<string> Chain, List<Literal> Prefix, bool Dynamic)>();
        pending.Enqueue((root, [root.Id], [], false));
        var depthLimited = false;
        while (pending.Count != 0)
        {
            var (entry, chain, prefix, dynamic) = pending.Dequeue();
            foreach (var at in entry.Reach)
            {
                if (!EffectsAt.TryGetValue(at, out var effects))
                {
                    continue;
                }

                var must = prefix.Concat(entry.Must[at].Select(Literal.FromKey).OrderBy(literal => literal.At)).ToArray();
                var pathDependent = entry.May[at].Count - entry.Must[at].Count;
                foreach (var effect in effects)
                {
                    results.Add(new ClosureEffect(effect, entry.Id, chain, must, pathDependent, dynamic));
                }
            }

            if (chain.Count > MaximumCallDepth)
            {
                depthLimited = true;
                continue;
            }

            foreach (var site in SitesIn(entry.Id))
            {
                var sitePrefix = prefix.Concat(entry.Must[site.At].Select(Literal.FromKey).OrderBy(literal => literal.At)).ToList();
                foreach (var target in site.Targets)
                {
                    if (!expanded.Add(target))
                    {
                        continue;
                    }

                    pending.Enqueue((EntriesById[target], [.. chain, target], sitePrefix, dynamic || site.Call.IsParty));
                }
            }
        }

        return (results, depthLimited);
    }

    /// <summary>What an instruction does that the audit tracks; empty for anything else.</summary>
    public List<Effect> Classify(Instruction instruction)
    {
        var effects = new List<Effect>();
        var b = instruction.Bytes;
        var at = instruction.Offset;
        void Add(string kind, string detail) => effects.Add(new Effect(at, kind, detail));
        string Value(int index) => index < 0 ? "?" : Semantics.Operands(instruction).ElementAtOrDefault(index)?.ToString() ?? "?";

        switch (instruction.Op)
        {
            case Opcodes.MESSAGE:
                Add("Dialog", $"window {b[1]} dialog {b[2]}");
                break;
            case Opcodes.ASK:
                Add("Ask", $"window {b[2]} dialog {b[3]} lines {b[4]}..{b[5]} answer -> {Value(0)}");
                break;
            case Opcodes.MPNAM:
                Add("MapName", $"dialog {b[1]}");
                break;
            case Opcodes.MENU:
                Add("Menu", $"menu {b[2]} param {Value(0)}");
                break;
            case Opcodes.STITM:
                Add("ItemAdd", $"item {Value(0)} x{Value(1)}");
                break;
            case Opcodes.DLITM:
                Add("ItemRemove", $"item {Value(0)} x{Value(1)}");
                break;
            case Opcodes.CKITM:
                Add("ItemCheck", $"item {Value(0)} count -> {Value(1)}");
                break;
            case Opcodes.SMTRA:
                Add("MateriaAdd", $"materia {Value(0)}");
                break;
            case Opcodes.DMTRA:
                Add("MateriaRemove", $"materia {Value(0)}");
                break;
            case Opcodes.CMTRA:
                Add("MateriaCheck", $"materia {Value(0)}");
                break;
            case Opcodes.GOLDu:
                Add("GilAdd", $"{Value(0)}");
                break;
            case Opcodes.GOLDd:
                Add("GilRemove", $"{Value(0)}");
                break;
            case Opcodes.MAPJUMP:
                Add("MapJump", $"field {BitConverter.ToUInt16(b, 1)} ({BitConverter.ToInt16(b, 3)},{BitConverter.ToInt16(b, 5)}) triangle {BitConverter.ToUInt16(b, 7)} dir {b[9]}");
                break;
            case Opcodes.PMJMP:
                Add("PreloadMap", $"field {BitConverter.ToUInt16(b, 1)}");
                break;
            case Opcodes.BATTLE:
                Add("Battle", $"battle {Value(0)}");
                break;
            case Opcodes.BTLON:
                Add("RandomBattles", b[1] == 0 ? "enabled" : "disabled");
                break;
            case Opcodes.MINIGAME:
                Add("Minigame", $"game {b[10]} param {b[9]} return field {BitConverter.ToUInt16(b, 1)}");
                break;
            case Opcodes.PMVIE:
                Add("MoviePrepare", $"movie {b[1]}");
                break;
            case Opcodes.MOVIE:
                Add("Movie", "play");
                break;
            case Opcodes.GAMEOVER:
                Add("GameOver", "game over");
                break;
            case 0x0E:
                Add("DiskChange", $"disk {b[1]}");
                break;
            case Opcodes.PRTYP:
                Add("PartyAdd", $"character {b[1]}");
                break;
            case Opcodes.PRTYM:
                Add("PartyRemove", $"character {b[1]}");
                break;
            case Opcodes.PRTYE:
                Add("PartySet", $"characters {b[1]},{b[2]},{b[3]}");
                break;
            case Opcodes.MMBud:
                Add("MemberAvailability", $"character {b[2]} {(b[1] == 0 ? "unavailable" : "available")}");
                break;
            case Opcodes.MMBLK:
                Add("MemberLock", $"character {b[1]} locked");
                break;
            case Opcodes.MMBUK:
                Add("MemberLock", $"character {b[1]} unlocked");
                break;
            case Opcodes.TLKON:
                Add("TalkEnable", b[1] == 0 ? "talk on" : "talk off");
                break;
            case Opcodes.VISI:
                Add("Visibility", b[1] == 0 ? "hide" : "show");
                break;
            case Opcodes.SOLID:
                Add("Solid", b[1] == 0 ? "solid on" : "solid off");
                break;
            case Opcodes.LINON:
                Add("LineEnable", b[1] != 0 ? "line on" : "line off");
                break;
            case Opcodes.LINE:
                Add("LineDefine", $"({BitConverter.ToInt16(b, 1)},{BitConverter.ToInt16(b, 3)},{BitConverter.ToInt16(b, 5)})-({BitConverter.ToInt16(b, 7)},{BitConverter.ToInt16(b, 9)},{BitConverter.ToInt16(b, 11)})");
                break;
            case Opcodes.SLINE:
                Add("LineMove", "line set from variables");
                break;
            case Opcodes.IDLCK:
                Add("TriangleLock", $"triangle {BitConverter.ToUInt16(b, 1)} {(b[3] != 0 ? "lock" : "unlock")}");
                break;
            case Opcodes.MPJPO:
                Add("GatewaysEnable", b[1] == 0 ? "gateways on" : "gateways off");
                break;
            case Opcodes.UC:
                Add("ControlEnable", b[1] == 0 ? "control on" : "control off");
                break;
            case Opcodes.MENU2:
                Add("MenuEnable", b[1] == 0 ? "menu on" : "menu off");
                break;
            case Opcodes.TALKR:
            case Opcodes.TLKR2:
                Add("TalkRange", Value(0));
                break;
            case Opcodes.SLIDR:
            case Opcodes.SLDR2:
                Add("SolidRange", Value(0));
                break;
            case Opcodes.XYZI:
                Add("Place", $"({Value(0)},{Value(1)},{Value(2)}) triangle {Value(3)}");
                break;
            case 0xC0:
                Add("Jump", $"({Value(0)},{Value(1)}) triangle {Value(2)} height {Value(3)}");
                break;
            case 0xC2:
                Add("Ladder", $"({Value(0)},{Value(1)},{Value(2)}) triangle {Value(3)} key {b[11]}");
                break;
            case Opcodes.XYI:
                Add("Place", $"({Value(0)},{Value(1)}) triangle {Value(2)}");
                break;
            case Opcodes.XYZ:
                Add("Place", $"({Value(0)},{Value(1)},{Value(2)})");
                break;
            case Opcodes.CHAR:
                Add("ModelLoad", $"model {b[1]}");
                break;
            case Opcodes.PC:
                Add("CharacterBind", $"character {b[1]}");
                break;
            case Opcodes.BGON:
                Add("BackgroundOn", $"param {Value(0)} state {Value(1)}");
                break;
            case Opcodes.BGOFF:
                Add("BackgroundOff", $"param {Value(0)} state {Value(1)}");
                break;
            case Opcodes.BGCLR:
                Add("BackgroundClear", $"param {Value(0)}");
                break;
            case Opcodes.SPECIAL:
                Add("Special", instruction.Name + " " + instruction.Hex);
                break;
            case Opcodes.SAVEMAPCOPY:
            case Opcodes.MEMWRITE:
                Add("RawMemory", instruction.Hex);
                break;
            case 0xF1:
            case 0xF2:
            case 0xDA:
                Add("Sound", instruction.Name);
                break;
            case 0xA3:
            case 0xAE:
            case 0xAF:
            case 0xBA:
            case 0xB0:
            case 0xBB:
            case 0xB1:
            case 0xBC:
            case 0xDD:
                Add("Animation", instruction.Name);
                break;
        }

        if (Semantics.HasDynamicAddress(instruction))
        {
            Add("DynamicAddress", $"{instruction.Name} {instruction.Hex}");
        }

        foreach (var write in Semantics.Writes(instruction))
        {
            if (write.Variable is null)
            {
                Add("WriteIgnored", $"{write.OperationName}: {write.Note}");
                continue;
            }

            var kind = write.Variable.IsSavemap ? "SavemapWrite" : "TempWrite";
            var detail = write.Bit >= 0
                ? $"{write.OperationName} {write.Variable} bit {write.Bit}"
                : write.Value is { } value
                    ? $"{write.OperationName} {write.Variable} = {value}"
                    : $"{write.OperationName} {write.Variable}";
            Add(kind, detail + (write.Note is null ? string.Empty : $" ({write.Note})"));
        }

        return effects;
    }
}
