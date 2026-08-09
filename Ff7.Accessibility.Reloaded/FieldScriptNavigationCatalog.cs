using System.Text;

namespace Ff7.Accessibility.Reloaded;

public enum FieldNavigationTransitionKind
{
    Ladder,
    Jump
}

public readonly record struct FieldScriptNavigationTransition(
    int FieldId,
    FieldNavigationTransitionKind Kind,
    int SourceEntityId,
    int SourceX,
    int SourceY,
    int SourceZ,
    int TargetX,
    int TargetY,
    int? TargetZ,
    int TargetTriangle,
    string StableId,
    FieldNavigationInput RequiredInput = FieldNavigationInput.None,
    bool RequiresAction = false);

public readonly record struct FieldScriptNpcDefinition(
    int FieldId,
    int EntityId,
    string EntityName,
    IReadOnlyList<int> DialogIds,
    int? InteractionLineEntityId = null,
    FieldNavigationTriggerLine? InteractionLine = null);

public readonly record struct FieldScriptWaitDefinition(
    int FieldId,
    int EntityId,
    int ScriptId,
    int ByteIndex,
    int Frames);

public readonly record struct FieldScriptOpcodeDefinition(
    int FieldId,
    int EntityId,
    string EntityName,
    int ScriptId,
    int ByteIndex,
    byte Opcode,
    IReadOnlyList<byte> Bytes);

public readonly record struct FieldScriptDefinition(
    int FieldId,
    int EntityId,
    string EntityName,
    int ScriptId,
    IReadOnlyList<FieldScriptOpcodeDefinition> Opcodes);

public sealed record FieldScriptNavigationReadResult(
    bool IsUsable,
    IReadOnlyList<FieldScriptNavigationTransition> Transitions,
    IReadOnlyList<FieldNavigationTarget> Exits,
    IReadOnlyList<FieldScriptNpcDefinition> Npcs,
    IReadOnlyList<FieldScriptWaitDefinition> Waits,
    string Diagnostic)
{
    public IReadOnlyList<int> MapNameDialogIds { get; init; } = Array.Empty<int>();

    public static FieldScriptNavigationReadResult Invalid(string diagnostic) =>
        new(
            false,
            Array.Empty<FieldScriptNavigationTransition>(),
            Array.Empty<FieldNavigationTarget>(),
            Array.Empty<FieldScriptNpcDefinition>(),
            Array.Empty<FieldScriptWaitDefinition>(),
            diagnostic);
}

public sealed class FieldScriptNavigationCatalog
{
    private const int FieldHeaderSectionCountOffset = 2;
    private const int FieldHeaderSectionOffsetsOffset = 6;
    private const int SectionLengthSize = 4;
    private const int ScriptCount = 32;
    private const int ScriptPointerTableEntrySize = sizeof(ushort);
    private const int ScriptPointerTableGroupSize = ScriptCount * ScriptPointerTableEntrySize;

    // PC field opcode lengths, indexed by opcode. Dynamic SPECIAL, KAWAI, and 0x1C sizes are handled below.
    private static readonly byte[] OpcodeLengths =
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

    private readonly FlevelDataSource flevelDataSource;
    private readonly IReadOnlyDictionary<int, string> fieldNames;
    private readonly Dictionary<int, FieldScriptNavigationReadResult> cache = new();
    private readonly object cacheLock = new();

    public FieldScriptNavigationCatalog(string gameRootDirectory)
        : this(gameRootDirectory, Ff7GameLanguageDetector.Detect(gameRootDirectory))
    {
    }

    public FieldScriptNavigationCatalog(
        string gameRootDirectory,
        Ff7GameLanguageContext language)
    {
        flevelDataSource = new FlevelDataSource(gameRootDirectory, language);
        fieldNames = flevelDataSource.FieldNames;
    }

    public FieldScriptNavigationReadResult ReadField(int fieldId)
    {
        lock (cacheLock)
        {
            if (cache.TryGetValue(fieldId, out var cached))
            {
                return cached;
            }

            var result = ReadFieldCore(fieldId);
            cache[fieldId] = result;
            return result;
        }
    }

    public IReadOnlyList<FieldScriptOpcodeDefinition> ReadScriptOpcodes(
        int fieldId,
        int entityId,
        int scriptId)
    {
        if (!fieldNames.TryGetValue(fieldId, out var fieldName))
        {
            return Array.Empty<FieldScriptOpcodeDefinition>();
        }

        if (!flevelDataSource.TryReadField(fieldName, out var encodedFieldBytes))
        {
            return Array.Empty<FieldScriptOpcodeDefinition>();
        }

        var fieldBytes = Ff7LzsDecoder.DecodeFieldFile(encodedFieldBytes);
        if (!TryReadSectionOne(fieldBytes, out var section, out _))
        {
            return Array.Empty<FieldScriptOpcodeDefinition>();
        }

        var groups = ParseScriptGroups(section, out _);
        if (entityId < 0 || entityId >= groups.Count ||
            !groups[entityId].Scripts.TryGetValue(scriptId, out var script))
        {
            return Array.Empty<FieldScriptOpcodeDefinition>();
        }

        var group = groups[entityId];
        return ReadOpcodes(script)
            .Select(opcode => new FieldScriptOpcodeDefinition(
                fieldId,
                entityId,
                group.Name,
                scriptId,
                opcode.Offset,
                opcode.Id,
                opcode.Bytes))
            .ToArray();
    }

    public IReadOnlyList<FieldScriptDefinition> ReadAllScriptOpcodes(int fieldId)
    {
        if (!fieldNames.TryGetValue(fieldId, out var fieldName) ||
            !flevelDataSource.TryReadField(fieldName, out var encodedFieldBytes))
        {
            return Array.Empty<FieldScriptDefinition>();
        }

        var fieldBytes = Ff7LzsDecoder.DecodeFieldFile(encodedFieldBytes);
        if (!TryReadSectionOne(fieldBytes, out var section, out _))
        {
            return Array.Empty<FieldScriptDefinition>();
        }

        var groups = ParseScriptGroups(section, out _);
        return groups
            .SelectMany(group => group.Scripts
                .OrderBy(script => script.Key)
                .Select(script => new FieldScriptDefinition(
                    fieldId,
                    group.Index,
                    group.Name,
                    script.Key,
                    ReadOpcodes(script.Value)
                        .Select(opcode => new FieldScriptOpcodeDefinition(
                            fieldId,
                            group.Index,
                            group.Name,
                            script.Key,
                            opcode.Offset,
                            opcode.Id,
                            opcode.Bytes))
                        .ToArray())))
            .ToArray();
    }

    private FieldScriptNavigationReadResult ReadFieldCore(int fieldId)
    {
        if (!fieldNames.TryGetValue(fieldId, out var fieldName))
        {
            return FieldScriptNavigationReadResult.Invalid($"field={fieldId}, map name unavailable");
        }

        if (!flevelDataSource.TryReadField(fieldName, out var encodedFieldBytes))
        {
            return FieldScriptNavigationReadResult.Invalid(
                $"field={fieldId} {fieldName}, file unavailable from {flevelDataSource.Diagnostic}");
        }

        try
        {
            var fieldBytes = Ff7LzsDecoder.DecodeFieldFile(encodedFieldBytes);
            if (!TryReadSectionOne(fieldBytes, out var section, out var diagnostic))
            {
                return FieldScriptNavigationReadResult.Invalid($"field={fieldId} {fieldName}, {diagnostic}");
            }

            var groups = ParseScriptGroups(section, out diagnostic);
            if (groups.Count == 0)
            {
                return FieldScriptNavigationReadResult.Invalid($"field={fieldId} {fieldName}, {diagnostic}");
            }

            var transitions = new List<FieldScriptNavigationTransition>();
            var exits = new List<FieldNavigationTarget>();
            var npcs = ReadNpcs(fieldId, groups);
            var waits = ReadWaits(fieldId, groups);
            var mapNameDialogIds = ReadMapNameDialogIds(groups);
            foreach (var group in groups)
            {
                if (!group.Scripts.TryGetValue(0, out var initScript) ||
                    !TryReadLine(initScript, out var line))
                {
                    continue;
                }

                var actions = new List<NavigationAction>();
                foreach (var script in group.Scripts.Where(pair =>
                             pair.Key > 1 ||
                             IsVerifiedActionActivatedExitScript(fieldId, group.Index, pair.Key)))
                {
                    var scriptPaths = CollectNavigationActionPaths(
                        groups,
                        group.Index,
                        script.Key,
                        new Dictionary<BankByteAddress, byte>(),
                        new HashSet<(int Group, int Script)>());
                    // Script 1 is a LINE entity's native [OK] handler. It is
                    // action-activated even when the handler goes straight to
                    // MAPJUMP without an explicit field-button opcode.
                    var requiresAction =
                        script.Key == 1 ||
                        RequiresActionActivation(script.Value);
                    foreach (var path in scriptPaths)
                    {
                        actions.AddRange(CollapseNavigationRoutine(path.Actions).Select(action =>
                            action with { RequiresActionActivation = requiresAction }));
                    }
                }

                foreach (var action in actions.Where(action => action.Kind is ActionKind.Ladder or ActionKind.Jump))
                {
                    var kind = action.Kind == ActionKind.Ladder
                        ? FieldNavigationTransitionKind.Ladder
                        : FieldNavigationTransitionKind.Jump;
                    transitions.Add(new FieldScriptNavigationTransition(
                        fieldId,
                        kind,
                        group.Index,
                        line.MidpointX,
                        line.MidpointY,
                        line.MidpointZ,
                        action.X,
                        action.Y,
                        action.Z,
                        action.Triangle,
                        $"{kind.ToString().ToLowerInvariant()}:{fieldId}:{group.Index}:{action.SourceGroup}:{action.SourceScript}:{action.Triangle}",
                        ResolveVerifiedLadderInput(
                            fieldId,
                            group.Index,
                            action.X,
                            action.Y,
                            action.Z,
                            action.Triangle,
                            action.RequiredInput),
                        action.RequiresActionActivation));
                }

                var destinations = actions
                    .Where(action => action.Kind == ActionKind.MapJump)
                    .Select(action => action.DestinationField)
                    .Distinct()
                    .OrderBy(destination => destination)
                    .ToArray();
                // A LINE that only MAPJUMPs back into its current field is a reset,
                // intra-map warp, or other scripted state transition—not an exit.
                // Keep mixed self/other branches (for example the winding tunnel),
                // but never steer the player toward a self-only reset line.
                if (destinations.Any(destination => destination != fieldId))
                {
                    exits.Add(new FieldNavigationTarget(
                        fieldId,
                        FieldNavigationCategory.Exits,
                        "Scripted exit",
                        line.MidpointX,
                        line.MidpointY,
                        line.MidpointZ,
                        $"script-exit:{fieldId}:{group.Index}:{string.Join(',', destinations)}",
                        TriggerEntityId: group.Index,
                        // A normal one-way gateway is complete when its line is
                        // reached. Mixed self/other branches are repeating
                        // same-field wraps (for example the winding tunnel), so
                        // they must stay active until the actual field change.
                        CompletesOnArrival: destinations.All(destination => destination != fieldId),
                        DestinationFieldIds: destinations,
                        TriggerLine: line.TriggerLine));
                }
            }

            AddNativeReverseLadderTransitions(fieldId, groups, transitions);
            transitions = transitions
                .GroupBy(transition => transition.StableId, StringComparer.Ordinal)
                .Select(group => group.First() with
                {
                    RequiresAction = group.Any(transition => transition.RequiresAction)
                })
                .ToList();
            exits = exits
                .DistinctBy(exit => exit.StableId)
                .ToList();
            return new FieldScriptNavigationReadResult(
                true,
                transitions,
                exits,
                npcs,
                waits,
                $"field={fieldId} {fieldName}, groups={groups.Count}, transitions={transitions.Count}, " +
                $"scriptExits={exits.Count}, talkNpcs={npcs.Count}, waits={waits.Count}, " +
                $"mapNames={(mapNameDialogIds.Count == 0 ? "none" : string.Join(',', mapNameDialogIds))}")
            {
                MapNameDialogIds = mapNameDialogIds
            };
        }
        catch (Exception ex)
        {
            return FieldScriptNavigationReadResult.Invalid(
                $"field={fieldId} {fieldName}, read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IReadOnlyList<int> ReadMapNameDialogIds(IReadOnlyList<ScriptGroup> groups) =>
        groups
            .OrderBy(group => group.Index)
            .SelectMany(group => group.Scripts.Where(script => script.Key == 0))
            .SelectMany(script => ReadOpcodes(script.Value))
            .Where(opcode => opcode.Id == 0x43 && opcode.Bytes.Length >= 2)
            .Select(opcode => (int)opcode.Bytes[1])
            .Distinct()
            .Order()
            .ToArray();

    private static IReadOnlyList<FieldScriptNpcDefinition> ReadNpcs(
        int fieldId,
        IReadOnlyList<ScriptGroup> groups)
    {
        var definitions = new List<FieldScriptNpcDefinition>();
        foreach (var group in groups)
        {
            if (!group.Scripts.ContainsKey(1))
            {
                continue;
            }

            var dialogIds = new List<int>();
            CollectTalkDialogIds(
                groups,
                group.Index,
                1,
                dialogIds,
                new HashSet<(int Group, int Script)>());
            var uniqueDialogIds = dialogIds.Distinct().ToArray();
            var interactionLine = ReadNpcInteractionLine(
                groups,
                group,
                allowEnabledTalkProxy: uniqueDialogIds.Length == 0);
            if (uniqueDialogIds.Length == 0 && interactionLine is null)
            {
                continue;
            }

            definitions.Add(new FieldScriptNpcDefinition(
                fieldId,
                group.Index,
                group.Name,
                uniqueDialogIds,
                interactionLine?.EntityId,
                interactionLine?.Line));
        }

        return definitions;
    }

    private static NpcInteractionLineDefinition? ReadNpcInteractionLine(
        IReadOnlyList<ScriptGroup> groups,
        ScriptGroup npcGroup,
        bool allowEnabledTalkProxy = false)
    {
        if (!npcGroup.Scripts.TryGetValue(0, out var initScript) ||
            !ReadOpcodes(initScript).Any(opcode =>
                opcode.Id == 0x7E &&
                opcode.Bytes.Length >= 2 &&
                (opcode.Bytes[1] != 0 || allowEnabledTalkProxy)))
        {
            return null;
        }

        var candidates = new List<NpcInteractionLineDefinition>();
        foreach (var lineGroup in groups)
        {
            if (!lineGroup.Scripts.TryGetValue(0, out var lineInit) ||
                !TryReadLine(lineInit, out var line) ||
                !lineGroup.Scripts.Values.Any(script =>
                    ReadOpcodes(script).Any(opcode =>
                        opcode.Id is >= 0x01 and <= 0x06 &&
                        opcode.Bytes.Length >= 3 &&
                        opcode.Bytes[1] == npcGroup.Index)))
            {
                continue;
            }

            candidates.Add(new NpcInteractionLineDefinition(
                lineGroup.Index,
                line.TriggerLine));
        }

        return candidates
            .Distinct()
            .Take(2)
            .ToArray() is [var single]
                ? single
                : null;
    }

    private static IReadOnlyList<FieldScriptWaitDefinition> ReadWaits(
        int fieldId,
        IReadOnlyList<ScriptGroup> groups)
    {
        var waits = new List<FieldScriptWaitDefinition>();
        foreach (var group in groups)
        {
            foreach (var script in group.Scripts)
            {
                foreach (var opcode in ReadOpcodes(script.Value))
                {
                    if (opcode.Id != FieldOpcodeAddressResolver.OpcodeWaitIndex || opcode.Bytes.Length < 3)
                    {
                        continue;
                    }

                    waits.Add(new FieldScriptWaitDefinition(
                        fieldId,
                        group.Index,
                        script.Key,
                        opcode.Offset,
                        BitConverter.ToUInt16(opcode.Bytes, 1)));
                }
            }
        }

        return waits;
    }

    private static void CollectTalkDialogIds(
        IReadOnlyList<ScriptGroup> groups,
        int groupIndex,
        int scriptIndex,
        ICollection<int> dialogIds,
        ISet<(int Group, int Script)> visited)
    {
        if (groupIndex < 0 || groupIndex >= groups.Count ||
            !visited.Add((groupIndex, scriptIndex)) ||
            !groups[groupIndex].Scripts.TryGetValue(scriptIndex, out var script))
        {
            return;
        }

        foreach (var opcode in ReadOpcodes(script))
        {
            if (opcode.Id is >= 0x01 and <= 0x06)
            {
                CollectTalkDialogIds(
                    groups,
                    opcode.Bytes[1],
                    opcode.Bytes[2] & 0x1F,
                    dialogIds,
                    visited);
                continue;
            }

            if (opcode.Id == 0x40 && opcode.Bytes.Length >= 3)
            {
                dialogIds.Add(opcode.Bytes[2]);
            }
            else if (opcode.Id == 0x48 && opcode.Bytes.Length >= 4)
            {
                dialogIds.Add(opcode.Bytes[3]);
            }
        }
    }

    private static bool TryReadSectionOne(byte[] fieldBytes, out byte[] section, out string diagnostic)
    {
        section = [];
        diagnostic = "invalid field header";
        if (!IsReadable(fieldBytes, FieldHeaderSectionCountOffset, sizeof(int)) ||
            !IsReadable(fieldBytes, FieldHeaderSectionOffsetsOffset, sizeof(int)))
        {
            return false;
        }

        var sectionCount = BitConverter.ToInt32(fieldBytes, FieldHeaderSectionCountOffset);
        if (sectionCount <= 0)
        {
            return false;
        }

        var sectionOffset = BitConverter.ToInt32(fieldBytes, FieldHeaderSectionOffsetsOffset);
        if (!IsReadable(fieldBytes, sectionOffset, SectionLengthSize))
        {
            return false;
        }

        var sectionLength = BitConverter.ToInt32(fieldBytes, sectionOffset);
        var dataOffset = sectionOffset + SectionLengthSize;
        if (sectionLength <= 0 || !IsReadable(fieldBytes, dataOffset, sectionLength))
        {
            diagnostic = $"invalid script section length {sectionLength}";
            return false;
        }

        section = fieldBytes.AsSpan(dataOffset, sectionLength).ToArray();
        diagnostic = "script section decoded";
        return true;
    }

    private static IReadOnlyList<ScriptGroup> ParseScriptGroups(byte[] section, out string diagnostic)
    {
        diagnostic = "invalid script header";
        if (section.Length < 32)
        {
            return Array.Empty<ScriptGroup>();
        }

        var version = BitConverter.ToUInt16(section, 0);
        var groupCount = section[2];
        var textOffset = BitConverter.ToUInt16(section, 4);
        var akaoCount = BitConverter.ToUInt16(section, 6);
        var namesOffset = (version == 0x0301 ? 8 : 16) + 16;
        var pointerTableOffset = namesOffset + groupCount * 8 + akaoCount * sizeof(int);
        if (groupCount == 0 ||
            textOffset < 32 ||
            !IsReadable(section, namesOffset, groupCount * 8) ||
            !IsReadable(section, pointerTableOffset, groupCount * ScriptPointerTableGroupSize))
        {
            return Array.Empty<ScriptGroup>();
        }

        var scriptsEnd = (int)textOffset;
        if (akaoCount > 0)
        {
            var firstAkaoPointerOffset = namesOffset + groupCount * 8;
            if (IsReadable(section, firstAkaoPointerOffset, sizeof(int)))
            {
                var firstAkao = BitConverter.ToInt32(section, firstAkaoPointerOffset);
                if (firstAkao > 0)
                {
                    scriptsEnd = Math.Min(scriptsEnd, firstAkao);
                }
            }
        }

        var allStarts = new SortedSet<int>();
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            for (var scriptIndex = 0; scriptIndex < ScriptCount; scriptIndex++)
            {
                var start = BitConverter.ToUInt16(
                    section,
                    pointerTableOffset + groupIndex * ScriptPointerTableGroupSize + scriptIndex * sizeof(ushort));
                if (start >= pointerTableOffset + groupCount * ScriptPointerTableGroupSize && start < scriptsEnd)
                {
                    allStarts.Add(start);
                }
            }
        }

        allStarts.Add(scriptsEnd);
        var orderedStarts = allStarts.ToArray();
        var groups = new List<ScriptGroup>(groupCount);
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var name = ReadAscii(section, namesOffset + groupIndex * 8, 8);
            var scripts = new Dictionary<int, byte[]>();
            var firstScriptByStart = new Dictionary<int, int>();
            for (var scriptIndex = 0; scriptIndex < ScriptCount; scriptIndex++)
            {
                var start = BitConverter.ToUInt16(
                    section,
                    pointerTableOffset + groupIndex * ScriptPointerTableGroupSize + scriptIndex * sizeof(ushort));
                firstScriptByStart.TryAdd(start, scriptIndex);
            }

            foreach (var pair in firstScriptByStart)
            {
                var start = pair.Key;
                var orderedIndex = Array.BinarySearch(orderedStarts, start);
                if (orderedIndex < 0 || orderedIndex + 1 >= orderedStarts.Length)
                {
                    continue;
                }

                var end = orderedStarts[orderedIndex + 1];
                if (end <= start || !IsReadable(section, start, end - start))
                {
                    continue;
                }

                scripts[pair.Value] = section.AsSpan(start, end - start).ToArray();
            }

            groups.Add(new ScriptGroup(groupIndex, name, scripts));
        }

        diagnostic = $"groups={groups.Count}, scriptStarts={orderedStarts.Length - 1}";
        return groups;
    }

    private static IReadOnlyList<NavigationExecutionPath> CollectNavigationActionPaths(
        IReadOnlyList<ScriptGroup> groups,
        int groupIndex,
        int scriptIndex,
        IReadOnlyDictionary<BankByteAddress, byte> initialConstants,
        ISet<(int Group, int Script)> callStack)
    {
        if (groupIndex < 0 || groupIndex >= groups.Count ||
            callStack.Contains((groupIndex, scriptIndex)) ||
            !groups[groupIndex].Scripts.TryGetValue(scriptIndex, out var script))
        {
            return [NavigationExecutionPath.Empty(initialConstants)];
        }

        var opcodes = ReadOpcodes(script).ToDictionary(opcode => opcode.Offset);
        if (opcodes.Count == 0)
        {
            return [NavigationExecutionPath.Empty(initialConstants)];
        }

        var nestedCallStack = new HashSet<(int Group, int Script)>(callStack)
        {
            (groupIndex, scriptIndex)
        };
        var pending = new Stack<NavigationExecutionCursor>();
        pending.Push(new NavigationExecutionCursor(
            opcodes.Keys.Min(),
            new Dictionary<BankByteAddress, byte>(initialConstants),
            [],
            [],
            0));
        var results = new List<NavigationExecutionPath>();
        while (pending.Count != 0 && results.Count < 64)
        {
            var cursor = pending.Pop();
            if (cursor.Steps >= 2048 ||
                !opcodes.TryGetValue(cursor.Offset, out var opcode) ||
                !cursor.VisitedOffsets.Add(cursor.Offset))
            {
                results.Add(new NavigationExecutionPath(cursor.Actions, cursor.Constants));
                continue;
            }

            var nextOffset = opcode.Offset + opcode.Bytes.Length;
            if (opcode.Id == 0x00)
            {
                results.Add(new NavigationExecutionPath(cursor.Actions, cursor.Constants));
                continue;
            }

            if (opcode.Id is >= 0x01 and <= 0x03 && opcode.Bytes.Length >= 3)
            {
                var targetGroup = opcode.Bytes[1];
                var targetScript = opcode.Bytes[2] & 0x1F;
                var calledPaths = CollectNavigationActionPaths(
                    groups,
                    targetGroup,
                    targetScript,
                    cursor.Constants,
                    nestedCallStack);
                foreach (var calledPath in calledPaths.Take(64 - results.Count))
                {
                    var combinedActions = new List<NavigationAction>(
                        cursor.Actions.Count + calledPath.Actions.Count);
                    combinedActions.AddRange(cursor.Actions);
                    combinedActions.AddRange(calledPath.Actions);
                    pending.Push(cursor.Continue(
                        nextOffset,
                        new Dictionary<BankByteAddress, byte>(calledPath.Constants),
                        combinedActions));
                }

                continue;
            }

            if (TryApplyConstantBankWrite(opcode, cursor.Constants))
            {
                pending.Push(cursor.Continue(nextOffset));
                continue;
            }

            if (TryInvalidateRuntimeBankWrite(opcode, cursor.Constants))
            {
                pending.Push(cursor.Continue(nextOffset));
                continue;
            }

            if (TryResolveConditionalBranch(opcode, cursor.Constants, out var condition, out var falseTarget))
            {
                if (condition.HasValue)
                {
                    pending.Push(cursor.Continue(condition.Value ? nextOffset : falseTarget));
                }
                else
                {
                    pending.Push(cursor.Branch(falseTarget));
                    pending.Push(cursor.Continue(nextOffset));
                }

                continue;
            }

            if (TryResolveUnconditionalBranch(opcode, out var branchTarget))
            {
                pending.Push(cursor.Continue(branchTarget));
                continue;
            }

            var nextActions = cursor.Actions;
            switch (opcode.Id)
            {
                case 0x60:
                    nextActions = [.. cursor.Actions, NavigationAction.MapJump(
                        groupIndex,
                        scriptIndex,
                        BitConverter.ToUInt16(opcode.Bytes, 1))];
                    break;
                case 0xC0 when HasConstantMovementArguments(opcode.Bytes):
                    nextActions = [.. cursor.Actions, NavigationAction.Jump(
                        groupIndex,
                        scriptIndex,
                        BitConverter.ToInt16(opcode.Bytes, 3),
                        BitConverter.ToInt16(opcode.Bytes, 5),
                        BitConverter.ToUInt16(opcode.Bytes, 7))];
                    break;
                case 0xC2 when HasConstantMovementArguments(opcode.Bytes):
                    nextActions = [.. cursor.Actions, NavigationAction.Ladder(
                        groupIndex,
                        scriptIndex,
                        BitConverter.ToInt16(opcode.Bytes, 3),
                        BitConverter.ToInt16(opcode.Bytes, 5),
                        BitConverter.ToInt16(opcode.Bytes, 7),
                        BitConverter.ToUInt16(opcode.Bytes, 9),
                        ResolveLadderInput(opcode.Bytes[11]))];
                    break;
            }

            pending.Push(cursor.Continue(nextOffset, actions: nextActions));
        }

        return results.Count == 0
            ? [NavigationExecutionPath.Empty(initialConstants)]
            : results
                .DistinctBy(path => path.StableKey)
                .ToArray();
    }

    private static IEnumerable<NavigationAction> CollapseNavigationRoutine(
        IReadOnlyList<NavigationAction> actions)
    {
        var movements = actions
            .Where(action => action.Kind is ActionKind.Ladder or ActionKind.Jump)
            .ToArray();
        if (movements.Length == 0)
        {
            return actions;
        }

        // A LINE activation owns the complete scripted traversal. Setup jumps,
        // the LADER itself, and cleanup jumps therefore form one route edge
        // whose landing is the final native movement endpoint.
        var finalMovement = movements[^1];
        var ladder = movements
            .Where(action => action.Kind == ActionKind.Ladder)
            .Cast<NavigationAction?>()
            .FirstOrDefault();
        var collapsedMovement = ladder is { } ladderAction
            ? ladderAction with
            {
                X = finalMovement.X,
                Y = finalMovement.Y,
                Z = finalMovement.Z,
                Triangle = finalMovement.Triangle
            }
            : finalMovement;
        return
        [
            collapsedMovement,
            .. actions.Where(action => action.Kind == ActionKind.MapJump)
        ];
    }

    private static bool TryApplyConstantBankWrite(
        ParsedOpcode opcode,
        IDictionary<BankByteAddress, byte> constants)
    {
        if (opcode.Id != 0x80 || opcode.Bytes.Length < 4)
        {
            return false;
        }

        var destinationBank = opcode.Bytes[1] >> 4;
        var sourceBank = opcode.Bytes[1] & 0x0F;
        var address = new BankByteAddress(destinationBank, opcode.Bytes[2]);
        if (sourceBank == 0)
        {
            constants[address] = opcode.Bytes[3];
        }
        else
        {
            constants.Remove(address);
        }

        return true;
    }

    private static bool TryInvalidateRuntimeBankWrite(
        ParsedOpcode opcode,
        IDictionary<BankByteAddress, byte> constants)
    {
        if (opcode.Id != FieldOpcodeParameterReader.AskOpcode || opcode.Bytes.Length < 7)
        {
            return false;
        }

        // ASK stores the player's selected choice in ba/a (bytes 1 and 6).
        // Any SETBYTE value tracked for that location is therefore no longer
        // constant after the question has been answered.
        constants.Remove(new BankByteAddress(opcode.Bytes[1], opcode.Bytes[6]));
        return true;
    }

    private static bool IsVerifiedActionActivatedExitScript(
        int fieldId,
        int entityId,
        int scriptId) =>
        fieldId == 238 &&
        entityId is >= 14 and <= 17 &&
        scriptId == 1;

    private static bool TryResolveConditionalBranch(
        ParsedOpcode opcode,
        IReadOnlyDictionary<BankByteAddress, byte> constants,
        out bool? condition,
        out int falseTarget)
    {
        condition = null;
        falseTarget = -1;
        if (opcode.Id == 0x14 && opcode.Bytes.Length >= 6)
        {
            falseTarget = opcode.Offset + opcode.Bytes[5] + 5;
        }
        else if (opcode.Id == 0x15 && opcode.Bytes.Length >= 7)
        {
            falseTarget = opcode.Offset + BitConverter.ToUInt16(opcode.Bytes, 5) + 6;
        }
        else
        {
            return false;
        }

        var bank = opcode.Bytes[1] >> 4;
        var sourceBank = opcode.Bytes[1] & 0x0F;
        if (sourceBank != 0 ||
            !constants.TryGetValue(new BankByteAddress(bank, opcode.Bytes[2]), out var actual))
        {
            return true;
        }

        var expected = opcode.Bytes[3];
        condition = opcode.Bytes[4] switch
        {
            0 => actual == expected,
            1 => actual != expected,
            2 => actual > expected,
            3 => actual < expected,
            4 => actual >= expected,
            5 => actual <= expected,
            _ => null
        };
        return true;
    }

    private static bool TryResolveUnconditionalBranch(ParsedOpcode opcode, out int target)
    {
        target = -1;
        switch (opcode.Id)
        {
            case 0x10 when opcode.Bytes.Length >= 2:
                target = opcode.Offset + opcode.Bytes[1] + 1;
                return true;
            case 0x11 when opcode.Bytes.Length >= 3:
                target = opcode.Offset + BitConverter.ToUInt16(opcode.Bytes, 1) + 2;
                return true;
            case 0x12 when opcode.Bytes.Length >= 2:
                target = opcode.Offset - opcode.Bytes[1];
                return true;
            case 0x13 when opcode.Bytes.Length >= 3:
                target = opcode.Offset - BitConverter.ToUInt16(opcode.Bytes, 1);
                return true;
            default:
                return false;
        }
    }

    private static void AddNativeReverseLadderTransitions(
        int fieldId,
        IReadOnlyList<ScriptGroup> groups,
        ICollection<FieldScriptNavigationTransition> transitions)
    {
        const int maximumEndpointDistance = 192;
        var maximumDistanceSquared = maximumEndpointDistance * (double)maximumEndpointDistance;
        var nativeLadders = groups
            .SelectMany(group => group.Scripts.SelectMany(script =>
                ReadOpcodes(script.Value)
                    .Where(opcode => opcode.Id == 0xC2 && HasConstantMovementArguments(opcode.Bytes))
                    .Select(opcode => NavigationAction.Ladder(
                        group.Index,
                        script.Key,
                        BitConverter.ToInt16(opcode.Bytes, 3),
                        BitConverter.ToInt16(opcode.Bytes, 5),
                        BitConverter.ToInt16(opcode.Bytes, 7),
                        BitConverter.ToUInt16(opcode.Bytes, 9),
                        ResolveLadderInput(opcode.Bytes[11])))))
            .ToArray();
        var explicitTransitions = transitions
            .Where(transition => transition.Kind == FieldNavigationTransitionKind.Ladder)
            .ToArray();
        foreach (var transition in explicitTransitions)
        {
            var reverse = nativeLadders
                .Where(candidate =>
                    AreOppositeInputs(transition.RequiredInput, candidate.RequiredInput) &&
                    NavigationDistanceSquared(
                        candidate.X,
                        candidate.Y,
                        candidate.Z ?? transition.SourceZ,
                        transition.SourceX,
                        transition.SourceY,
                        transition.SourceZ) <= maximumDistanceSquared)
                .OrderBy(candidate => NavigationDistanceSquared(
                    candidate.X,
                    candidate.Y,
                    candidate.Z ?? transition.SourceZ,
                    transition.SourceX,
                    transition.SourceY,
                    transition.SourceZ))
                .Cast<NavigationAction?>()
                .FirstOrDefault();
            if (reverse is not { } reverseAction)
            {
                continue;
            }

            var nativeRoutineId = $":{reverseAction.SourceGroup}:{reverseAction.SourceScript}:";
            if (explicitTransitions.Any(candidate =>
                    candidate.StableId.Contains(nativeRoutineId, StringComparison.Ordinal)))
            {
                continue;
            }

            var reverseSourceZ = transition.TargetZ ?? transition.SourceZ;
            var alreadyRepresented = explicitTransitions.Any(candidate =>
                candidate.RequiredInput == reverseAction.RequiredInput &&
                NavigationDistanceSquared(
                    candidate.SourceX,
                    candidate.SourceY,
                    candidate.SourceZ,
                    transition.TargetX,
                    transition.TargetY,
                    reverseSourceZ) <= maximumDistanceSquared &&
                NavigationDistanceSquared(
                    candidate.TargetX,
                    candidate.TargetY,
                    candidate.TargetZ ?? candidate.SourceZ,
                    reverseAction.X,
                    reverseAction.Y,
                    reverseAction.Z ?? transition.SourceZ) <= maximumDistanceSquared);
            if (alreadyRepresented)
            {
                continue;
            }

            transitions.Add(new FieldScriptNavigationTransition(
                fieldId,
                FieldNavigationTransitionKind.Ladder,
                transition.SourceEntityId,
                transition.TargetX,
                transition.TargetY,
                reverseSourceZ,
                reverseAction.X,
                reverseAction.Y,
                reverseAction.Z,
                reverseAction.Triangle,
                $"ladder-auto:{fieldId}:{transition.SourceEntityId}:{reverseAction.SourceGroup}:{reverseAction.SourceScript}:{reverseAction.Triangle}",
                reverseAction.RequiredInput));
        }
    }

    private static bool AreOppositeInputs(FieldNavigationInput first, FieldNavigationInput second) =>
        (first, second) is
            (FieldNavigationInput.Up, FieldNavigationInput.Down) or
            (FieldNavigationInput.Down, FieldNavigationInput.Up) or
            (FieldNavigationInput.Left, FieldNavigationInput.Right) or
            (FieldNavigationInput.Right, FieldNavigationInput.Left);

    private static double NavigationDistanceSquared(
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        var deltaX = secondX - firstX;
        var deltaY = secondY - firstY;
        var deltaZ = secondZ - firstZ;
        return
            deltaX * (double)deltaX +
            deltaY * (double)deltaY +
            deltaZ * (double)deltaZ;
    }

    private static FieldNavigationInput ResolveLadderInput(byte nativeKey) => nativeKey switch
    {
        0 => FieldNavigationInput.Down,
        1 => FieldNavigationInput.Up,
        2 => FieldNavigationInput.Right,
        3 => FieldNavigationInput.Left,
        _ => FieldNavigationInput.None
    };

    private static FieldNavigationInput ResolveVerifiedLadderInput(
        int fieldId,
        int sourceEntityId,
        int targetX,
        int targetY,
        int? targetZ,
        int targetTriangle,
        FieldNavigationInput decodedInput)
    {
        // wcrimb_1 entity 15 is encoded as LADDER key 1, which normally maps
        // to Up. Runtime input traces and live verification show that this
        // particular sideways ladder is traversed by holding Left instead.
        if (fieldId == 223 &&
            sourceEntityId == 15 &&
            targetX == -40 &&
            targetY == 1039 &&
            targetZ == 2273 &&
            targetTriangle == 158)
        {
            return FieldNavigationInput.Left;
        }

        return decodedInput;
    }

    private static bool RequiresActionActivation(byte[] script) =>
        ReadOpcodes(script).Any(opcode =>
            (opcode.Id is 0x30 or 0x31) &&
            opcode.Bytes.Length >= 3 &&
            (BitConverter.ToUInt16(opcode.Bytes, 1) & 0x20) != 0);

    private static bool TryReadLine(byte[] script, out LineDefinition line)
    {
        foreach (var opcode in ReadOpcodes(script))
        {
            if (opcode.Id != 0xD0)
            {
                continue;
            }

            var startX = BitConverter.ToInt16(opcode.Bytes, 1);
            var startY = BitConverter.ToInt16(opcode.Bytes, 3);
            var startZ = BitConverter.ToInt16(opcode.Bytes, 5);
            var endX = BitConverter.ToInt16(opcode.Bytes, 7);
            var endY = BitConverter.ToInt16(opcode.Bytes, 9);
            var endZ = BitConverter.ToInt16(opcode.Bytes, 11);
            line = new LineDefinition(
                Midpoint(startX, endX),
                Midpoint(startY, endY),
                Midpoint(startZ, endZ),
                new FieldNavigationTriggerLine(startX, startY, startZ, endX, endY, endZ));
            return true;
        }

        line = default;
        return false;
    }

    private static IEnumerable<ParsedOpcode> ReadOpcodes(byte[] script)
    {
        var offset = 0;
        while (offset < script.Length)
        {
            var opcode = script[offset];
            var length = GetOpcodeLength(script, offset);
            if (length <= 0 || length > script.Length - offset)
            {
                yield break;
            }

            yield return new ParsedOpcode(opcode, offset, script.AsSpan(offset, length).ToArray());
            offset += length;
        }
    }

    private static int GetOpcodeLength(byte[] script, int offset)
    {
        var opcode = script[offset];
        var length = OpcodeLengths[opcode];
        if (opcode == 0x1C && script.Length - offset >= 6)
        {
            return length + Math.Min(script[offset + 5], (byte)128);
        }

        if (opcode == 0x28 && script.Length - offset >= 2)
        {
            return Math.Max(1, (int)script[offset + 1]);
        }

        if (opcode != 0x0F || script.Length - offset < 2)
        {
            return length;
        }

        return script[offset + 1] switch
        {
            0xF5 or 0xF6 or 0xF7 or 0xFB or 0xFC => length + 1,
            0xF8 or 0xFD => length + 2,
            _ => length
        };
    }

    private static bool HasConstantMovementArguments(byte[] opcode) =>
        opcode.Length >= 3 && opcode[1] == 0 && opcode[2] == 0;

    private static string ReadAscii(byte[] bytes, int offset, int maxLength)
    {
        var length = 0;
        while (length < maxLength && bytes[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(bytes, offset, length).Trim();
    }

    private static int Midpoint(short first, short second) =>
        (int)Math.Round((first + second) / 2d, MidpointRounding.AwayFromZero);

    private static bool IsReadable(byte[] bytes, int offset, int length) =>
        offset >= 0 && length >= 0 && offset <= bytes.Length && length <= bytes.Length - offset;

    private readonly record struct BankByteAddress(int Bank, int Index);

    private sealed record NavigationExecutionPath(
        IReadOnlyList<NavigationAction> Actions,
        IReadOnlyDictionary<BankByteAddress, byte> Constants)
    {
        public string StableKey =>
            string.Join(
                ";",
                Actions.Select(action =>
                    $"{action.Kind}:{action.SourceGroup}:{action.SourceScript}:{action.X}:{action.Y}:" +
                    $"{action.Z}:{action.Triangle}:{action.DestinationField}:{action.RequiredInput}")) +
            "#" +
            string.Join(
                ";",
                Constants
                    .OrderBy(pair => pair.Key.Bank)
                    .ThenBy(pair => pair.Key.Index)
                    .Select(pair => $"{pair.Key.Bank}:{pair.Key.Index}:{pair.Value}"));

        public static NavigationExecutionPath Empty(
            IReadOnlyDictionary<BankByteAddress, byte> constants) =>
            new(
                Array.Empty<NavigationAction>(),
                new Dictionary<BankByteAddress, byte>(constants));
    }

    private sealed record NavigationExecutionCursor(
        int Offset,
        Dictionary<BankByteAddress, byte> Constants,
        List<NavigationAction> Actions,
        HashSet<int> VisitedOffsets,
        int Steps)
    {
        public NavigationExecutionCursor Continue(
            int offset,
            Dictionary<BankByteAddress, byte>? constants = null,
            List<NavigationAction>? actions = null) =>
            new(
                offset,
                constants ?? new Dictionary<BankByteAddress, byte>(Constants),
                actions ?? new List<NavigationAction>(Actions),
                new HashSet<int>(VisitedOffsets),
                Steps + 1);

        public NavigationExecutionCursor Branch(int offset) =>
            Continue(offset);
    }

    private sealed record ScriptGroup(int Index, string Name, IReadOnlyDictionary<int, byte[]> Scripts);

    private readonly record struct ParsedOpcode(byte Id, int Offset, byte[] Bytes);

    private readonly record struct LineDefinition(
        int MidpointX,
        int MidpointY,
        int MidpointZ,
        FieldNavigationTriggerLine TriggerLine);

    private readonly record struct NpcInteractionLineDefinition(
        int EntityId,
        FieldNavigationTriggerLine Line);

    private enum ActionKind
    {
        Ladder,
        Jump,
        MapJump
    }

    private readonly record struct NavigationAction(
        ActionKind Kind,
        int SourceGroup,
        int SourceScript,
        int X,
        int Y,
        int? Z,
        int Triangle,
        int DestinationField,
        FieldNavigationInput RequiredInput,
        bool RequiresActionActivation)
    {
        public static NavigationAction Ladder(
            int group,
            int script,
            int x,
            int y,
            int z,
            int triangle,
            FieldNavigationInput requiredInput) =>
            new(ActionKind.Ladder, group, script, x, y, z, triangle, -1, requiredInput, false);

        public static NavigationAction Jump(int group, int script, int x, int y, int triangle) =>
            new(ActionKind.Jump, group, script, x, y, null, triangle, -1, FieldNavigationInput.None, false);

        public static NavigationAction MapJump(int group, int script, int destinationField) =>
            new(ActionKind.MapJump, group, script, 0, 0, null, -1, destinationField, FieldNavigationInput.None, false);
    }
}
