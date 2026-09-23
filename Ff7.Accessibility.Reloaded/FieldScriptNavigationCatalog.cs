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
    bool RequiresAction = false,
    // A traversal a LINE owns is anchored by that line's position, and the walkmesh
    // triangle under it has to be worked out from the elevation. One the field polls
    // for instead already names its triangle outright, and saying so is both exact and
    // cheaper than guessing a storey from a height.
    int SourceTriangle = -1);

/// <param name="ModelResourceName">
/// The mesh this entity's own <c>CHAR</c> argument selects out of the field's model
/// loader, or empty where it loads none.
///
/// <para>It is the only description of an NPC the game itself supplies that survives to
/// the runtime. The entity name is a script label and the dialogue is words on screen;
/// the model is what the player is looking at, which is why the generic role a target is
/// announced with is derived from this rather than from either of the others.</para>
/// </param>
public readonly record struct FieldScriptNpcDefinition(
    int FieldId,
    int EntityId,
    string EntityName,
    IReadOnlyList<int> DialogIds,
    int? InteractionLineEntityId = null,
    FieldNavigationTriggerLine? InteractionLine = null,
    string ModelResourceName = "")
{
    /// <summary>
    /// Whether <see cref="InteractionLine"/> runs this entity's own Talk script (REQ of
    /// script 1): the game's counter for talking to them, as opposed to a LINE that asks
    /// them for something else. zz2's reward boxes request the seller's scripts 5 and 7;
    /// the counters of fields 153, 376 (both receptionists), 443 and 503 request script 1.
    /// </summary>
    public bool InteractionLineRunsTalk { get; init; }
}

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
    private const int OpcodeLoadModel = 0xA1;

    // FLEVEL slot 0 contains Init/Main; slot 1 is Talk, or a LINE's OK handler.
    private const int TalkScript = 1;
    private const int LineConfirmScript = 1;
    private const byte MenuOpcode = 0x49;
    private const byte SoundOpcode = 0xF1;
    private const byte SoundParameterOpcode = 0xF2;
    private const byte EntityRequestOpcode = 0x01;
    private const byte EntityRequestSyncOpcode = 0x06;

    // Field model loader, section 3 of the field file. Header is a blank word, the model
    // count and the scale; each model record is a length-prefixed name followed by a fixed
    // tail carrying its animation count, then that many variable-length animation records.
    private const int ModelLoaderSectionIndex = 2;
    private const int ModelLoaderCountOffset = 2;
    private const int ModelLoaderHeaderSize = 6;
    private const int ModelRecordTailSize = 46;
    private const int ModelRecordAnimationCountOffset = 14;
    private const int ModelAnimationTailSize = 2;
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
            var npcs = ReadNpcs(
                fieldId,
                groups,
                StripFieldPrefix(ReadModelResourceNames(fieldBytes), fieldName));
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
                // A LINE entity's script 1 is its native [OK] handler. Treating those as
                // exits would invent doorways that need a button press, which is why they
                // sit behind IsVerifiedActionActivatedExitScript. A climb is not a doorway
                // though, and the Mythril Mine vines live in exactly this slot: excluding
                // it wholesale kept the upper ledge off the walkmesh graph, so the
                // Junon-side mine mouth and the Long Range Materia above it were reported
                // unreachable. Collect these separately so they can reach the transition
                // list without reaching the exit list.
                var okHandlerActions = new List<NavigationAction>();
                foreach (var script in group.Scripts)
                {
                    var isEntryScript =
                        script.Key > 1 ||
                        IsVerifiedActionActivatedExitScript(fieldId, group.Index, script.Key);
                    if (!isEntryScript && script.Key != 1)
                    {
                        continue;
                    }

                    var scriptPaths = CollectNavigationActionPaths(
                        groups,
                        group.Index,
                        script.Key,
                        new Dictionary<BankByteAddress, byte>(),
                        new HashSet<(int Group, int Script)>());
                    // Script 1 is action-activated even when the handler goes straight to
                    // MAPJUMP without an explicit field-button opcode.
                    var requiresAction =
                        script.Key == 1 ||
                        RequiresActionActivation(script.Value);
                    var sink = isEntryScript ? actions : okHandlerActions;
                    foreach (var path in scriptPaths)
                    {
                        sink.AddRange(CollapseNavigationRoutine(path.Actions).Select(action =>
                            action with { RequiresActionActivation = requiresAction }));
                    }
                }

                foreach (var action in actions
                             .Concat(okHandlerActions)
                             .Where(action => action.Kind is ActionKind.Ladder or ActionKind.Jump))
                {
                    if (IsMountCorelNpcCheeringJump(fieldId, group.Index, action, groups))
                    {
                        continue;
                    }

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
            transitions.AddRange(ReadTrianglePolledLadders(fieldId, groups));
            transitions = transitions
                .GroupBy(transition => transition.StableId, StringComparer.Ordinal)
                .Select(group => group.First() with
                {
                    RequiresAction = group.Any(transition => transition.RequiresAction)
                })
                .ToList();
            exits.AddRange(GoldSaucerPlatformExitCatalog.ForField(fieldId));
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
        IReadOnlyList<ScriptGroup> groups,
        IReadOnlyList<string> modelResources)
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

            // An entity that says nothing can still be somebody the player talks to. The
            // dogs and cats of Junon, Wutai, Icicle Inn and Mideel answer with a bark and
            // an animation, Nibelheim's n_woman opens a menu, and the Ghost Hotel's
            // receptionist hands off to the script that greets you. All of those are real
            // interactions and none of them carries a MESSAGE.
            // The model requirement applies only to this new path. An entity that says
            // something has always been catalogued whether or not it is drawn, and the
            // runtime decides; but an entity that says nothing has only its mesh to make
            // it somebody, so a scene director with a hand-off Talk is not one.
            var speaks = uniqueDialogIds.Length > 0 ||
                         (LoadsModel(group) && HasPerceivableTalk(group));

            var interactionLine = ReadNpcInteractionLine(
                groups,
                group,
                allowEnabledTalkProxy: !speaks);

            // Only an entity with nothing of its own is reached through a counter, so an
            // entity that already answers keeps being gated on its own native Talk flag.
            interactionLine ??= speaks ? null : ReadCounterLine(groups, group);

            if (!speaks && interactionLine is null)
            {
                continue;
            }

            definitions.Add(new FieldScriptNpcDefinition(
                fieldId,
                group.Index,
                group.Name,
                uniqueDialogIds,
                interactionLine?.EntityId,
                interactionLine?.Line,
                ReadModelResourceName(group, modelResources))
            {
                InteractionLineRunsTalk = interactionLine?.RunsTalk == true
            });
        }

        return definitions;
    }

    /// <summary>
    /// The mesh this entity loads, from its own init script's <c>CHAR</c> argument
    /// indexed into the field's model loader. Empty when it loads none, which is how a
    /// scene director or a pure trigger is told from something the player can see.
    /// </summary>
    private static string ReadModelResourceName(
        ScriptGroup group,
        IReadOnlyList<string> modelResources)
    {
        if (modelResources.Count == 0 || !group.Scripts.TryGetValue(0, out var initScript))
        {
            return string.Empty;
        }

        foreach (var opcode in ReadOpcodes(initScript))
        {
            if (opcode.Id != OpcodeLoadModel || opcode.Bytes.Length < 2)
            {
                continue;
            }

            var index = opcode.Bytes[1];
            return index < modelResources.Count ? modelResources[index] : string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// The loader stores each resource as the field's own name followed by the mesh, so
    /// Costa's harbour holds "del1" + "shinra_crew.char". Only the second half describes
    /// who is standing there, and leaving the first half attached would let a field name
    /// answer for the model: the Don Corneo screens are called "onna", which is Japanese
    /// for woman, and everything loaded on them would read as one. A resource that does
    /// not carry the prefix is left as it is.
    /// </summary>
    private static IReadOnlyList<string> StripFieldPrefix(
        IReadOnlyList<string> resources,
        string fieldName)
    {
        if (fieldName.Length == 0 || resources.Count == 0)
        {
            return resources;
        }

        var stripped = new List<string>(resources.Count);
        foreach (var resource in resources)
        {
            stripped.Add(resource.StartsWith(fieldName, StringComparison.OrdinalIgnoreCase)
                ? resource[fieldName.Length..]
                : resource);
        }

        return stripped;
    }

    /// <summary>
    /// The model loader's resource names, one per loader slot in the order the field
    /// declares them, so slot <c>n</c> here is exactly the model a <c>CHAR n</c> selects.
    ///
    /// <para>The section is walked by its documented records rather than scanned for
    /// printable text. A scan silently drops any record whose name is not the shape it
    /// expects, and every slot after that one then answers for the wrong model - which is
    /// the one failure this must not have, because the result is used to describe a person
    /// to somebody who cannot see them.</para>
    ///
    /// <para>If the records do not add up, no names are returned at all. An entity with no
    /// resource name falls back to its dialogue exactly as it did before; a shifted one
    /// would confidently describe somebody else.</para>
    /// </summary>
    private static IReadOnlyList<string> ReadModelResourceNames(byte[] fieldBytes)
    {
        // The header says how many sections the file has; a field with fewer than three
        // has no model loader and its third offset is somebody else's data.
        if (!IsReadable(fieldBytes, FieldHeaderSectionCountOffset, sizeof(int)) ||
            BitConverter.ToInt32(fieldBytes, FieldHeaderSectionCountOffset) <=
                ModelLoaderSectionIndex)
        {
            return Array.Empty<string>();
        }

        var offsetPosition = FieldHeaderSectionOffsetsOffset +
                             (ModelLoaderSectionIndex * sizeof(int));
        if (!IsReadable(fieldBytes, offsetPosition, sizeof(int)))
        {
            return Array.Empty<string>();
        }

        var sectionOffset = BitConverter.ToInt32(fieldBytes, offsetPosition);
        if (sectionOffset <= 0 || !IsReadable(fieldBytes, sectionOffset, SectionLengthSize))
        {
            return Array.Empty<string>();
        }

        var length = BitConverter.ToInt32(fieldBytes, sectionOffset);
        var start = sectionOffset + SectionLengthSize;
        if (length < ModelLoaderHeaderSize || !IsReadable(fieldBytes, start, length))
        {
            return Array.Empty<string>();
        }

        var end = start + length;
        var modelCount = BitConverter.ToUInt16(fieldBytes, start + ModelLoaderCountOffset);
        var position = start + ModelLoaderHeaderSize;
        var names = new List<string>(modelCount);

        for (var model = 0; model < modelCount; model++)
        {
            if (position + sizeof(ushort) > end)
            {
                return Array.Empty<string>();
            }

            var nameLength = BitConverter.ToUInt16(fieldBytes, position);
            position += sizeof(ushort);
            if (nameLength == 0 || position + nameLength + ModelRecordTailSize > end)
            {
                return Array.Empty<string>();
            }

            names.Add(System.Text.Encoding.ASCII
                .GetString(fieldBytes, position, nameLength)
                .TrimEnd('\0'));
            position += nameLength;

            // The animation table follows the record's fixed tail, and its own records are
            // variable length too, so they have to be stepped over to reach the next model.
            var animations = BitConverter.ToUInt16(
                fieldBytes,
                position + ModelRecordAnimationCountOffset);
            position += ModelRecordTailSize;

            for (var animation = 0; animation < animations; animation++)
            {
                if (position + sizeof(ushort) > end)
                {
                    return Array.Empty<string>();
                }

                position += sizeof(ushort) +
                            BitConverter.ToUInt16(fieldBytes, position) +
                            ModelAnimationTailSize;
                if (position > end)
                {
                    return Array.Empty<string>();
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Whether the entity's Talk script does anything the player would notice. A MESSAGE
    /// is the usual answer and is counted by the caller; this covers the rest - a shop or
    /// save menu, a sound, an animation, or handing off to another entity's script, which
    /// is how the Ghost Hotel's receptionist produces its greeting.
    ///
    /// <para>Turning to face the player is deliberately not enough. Several North Corel
    /// residents have a Talk that only turns them round, and they are scenery with legs.
    /// </para>
    /// </summary>
    /// <summary>
    /// Whether the field draws anything for this entity. Read from the entity's own CHAR
    /// opcode rather than from the model loader, so a field whose optional model metadata
    /// will not parse still discovers the same actors.
    /// </summary>
    private static bool LoadsModel(ScriptGroup group) =>
        group.Scripts.TryGetValue(0, out var initScript) &&
        ReadOpcodes(initScript).Any(opcode => opcode.Id == OpcodeLoadModel);

    private static bool HasPerceivableTalk(ScriptGroup group) =>
        group.Scripts.TryGetValue(TalkScript, out var talk) &&
        ReadOpcodes(talk).Any(opcode =>
            opcode.Id is MenuOpcode or SoundOpcode or SoundParameterOpcode ||
            // ANIME1/2, ANIM!1/2, CANIM1/2, CANM!1/2 and ANIMB.
            // A4 is VISI and AB is TURA; neither is an animation command.
            opcode.Id is 0xA3 or 0xAE or 0xAF or 0xBA or 0xB0 or 0xBB or 0xB1 or 0xBC or 0xDD ||
            opcode.Id is >= EntityRequestOpcode and <= EntityRequestSyncOpcode);

    /// <summary>
    /// The counter an entity is served across, for an entity that has no Talk of its own.
    ///
    /// <para>Shop and inn staff are commonly placed behind a counter with no interaction
    /// at all, and a LINE laid along the customer's side of it runs the exchange. The
    /// line's confirm script - the one the engine runs when the player presses OK inside
    /// it - is the game's own statement that this is something you walk up to and use,
    /// and so is an explicit confirm-key test in any of its other scripts. Both are
    /// required, because a LINE on its own is just as often a cutscene trigger: North
    /// Corel's thanks1 and Mideel's exit line both drive bystanders the same way and must
    /// not turn them into people to walk to.</para>
    ///
    /// <para>Counters are frequently approachable from more than one side - Icicle Inn's
    /// shop has four lines around it and its tables have one on each side - so the lowest
    /// numbered line is taken rather than the entity being dropped for being ambiguous.
    /// Any one of them is a valid way to reach the same person.</para>
    /// </summary>
    private static NpcInteractionLineDefinition? ReadCounterLine(
        IReadOnlyList<ScriptGroup> groups,
        ScriptGroup npcGroup)
    {
        if (!LoadsModel(npcGroup))
        {
            return null;
        }

        NpcInteractionLineDefinition? chosen = null;
        foreach (var lineGroup in groups)
        {
            if (lineGroup.Index == npcGroup.Index ||
                !lineGroup.Scripts.TryGetValue(0, out var lineInit) ||
                !TryReadLine(lineInit, out var line))
            {
                continue;
            }

            foreach (var (scriptIndex, script) in lineGroup.Scripts)
            {
                if (scriptIndex != LineConfirmScript && !RequiresActionActivation(script))
                {
                    continue;
                }

                if (!RequestsEntity(script, npcGroup.Index))
                {
                    continue;
                }

                if (chosen is null || lineGroup.Index < chosen.Value.EntityId)
                {
                    chosen = new NpcInteractionLineDefinition(
                        lineGroup.Index,
                        line.TriggerLine,
                        lineGroup.Scripts.Values.Any(lineScript =>
                            RequestsEntityScript(lineScript, npcGroup.Index, TalkScript)));
                }

                break;
            }
        }

        return chosen;
    }

    private static bool RequestsEntity(byte[] script, int entityId) =>
        ReadOpcodes(script).Any(opcode =>
            opcode.Id is >= EntityRequestOpcode and <= EntityRequestSyncOpcode &&
            opcode.Bytes.Length >= 3 &&
            opcode.Bytes[1] == entityId);

    /// <summary>
    /// REQ, REQSW and REQEW name the entity in their first argument and pack the priority
    /// over the script number in the second: <c>(priority &lt;&lt; 5) | script</c>.
    /// </summary>
    private static bool RequestsEntityScript(byte[] script, int entityId, int scriptId) =>
        ReadOpcodes(script).Any(opcode =>
            opcode.Id is >= EntityRequestOpcode and <= EntityRequestSyncOpcode &&
            opcode.Bytes.Length >= 3 &&
            opcode.Bytes[1] == entityId &&
            (opcode.Bytes[2] & 0x1F) == scriptId);

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

        // A scene can animate a non-talkable actor through a LINE as well. Require
        // the same manual-interaction evidence as other counters before exposing it.
        return ReadCounterLine(groups, npcGroup);
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


    private static bool IsMountCorelNpcCheeringJump(
        int fieldId,
        int lineEntityId,
        NavigationAction action,
        IReadOnlyList<ScriptGroup> groups)
    {
        // mtcrl_6/border7 calls party slot zero's script 5. Cloud wraps onto
        // the opposite upper track with XYZI; earith's coincidentally numbered
        // script instead jumps in place while cheering on the lower track.
        // The generic party routine comparison cannot see XYZI, so its lone
        // remaining JUMP is not evidence that the player crosses between levels.
        // Match the native Cloud landing bytes as well as this one false edge.
        return fieldId == 464 && lineEntityId == 12 &&
            action is { Kind: ActionKind.Jump, SourceGroup: 17, SourceScript: 5,
                X: -1579, Y: 241, Triangle: 207 } &&
            groups.Count > 16 && groups[16].Scripts.TryGetValue(5, out var cloudScript) &&
            ReadOpcodes(cloudScript).Any(opcode => opcode.Offset == 5 &&
                opcode.Bytes.AsSpan().SequenceEqual(
                    new byte[] { 0xA5, 0, 0, 0x11, 0x0A, 0xFB, 0xFA, 0x07, 0x04, 0xF7, 0 }));
    }

    /// <summary>
    /// Resolves a party-slot call by looking at every entity that carries a routine at this
    /// script index and doing something navigable with it. The copies must agree: agreement
    /// is what makes the slot a shared character routine rather than a coincidence, and it
    /// is what lets the call be resolved without knowing the live party. The comparison
    /// ignores which entity a copy came from, since that is the only thing that legitimately
    /// differs between them. Disagreement is skipped rather than guessed - an unresolved
    /// call is exactly what happens today, so skipping costs nothing.
    /// </summary>
    private static IReadOnlyList<NavigationExecutionPath> CollectPartyMemberActionPaths(
        IReadOnlyList<ScriptGroup> groups,
        int scriptIndex,
        IReadOnlyDictionary<BankByteAddress, byte> initialConstants,
        ISet<(int Group, int Script)> callStack)
    {
        IReadOnlyList<NavigationExecutionPath>? agreed = null;
        string? agreedKey = null;
        var agreedIsPlacementOnly = false;
        var agreedIsStrong = false;
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            // A party-member request names a slot, and only a playable character can be
            // in one. Script numbers are per entity, so an ordinary entity that happens
            // to have a script with the same number is answering a different question:
            // in the Forgotten Capital's lake every one of the four platform lines keeps
            // its own Move at index 3, so asking the family what its script 3 does used
            // to collect three other lines' crossings alongside the character's, decide
            // the family disagreed, and drop the first platform entirely.
            if (!groups[groupIndex].Scripts.ContainsKey(scriptIndex) ||
                !IsPlayableCharacterGroup(groups[groupIndex]))
            {
                continue;
            }

            var navigable = CollectNavigationActionPaths(
                    groups,
                    groupIndex,
                    scriptIndex,
                    initialConstants,
                    callStack)
                .Where(path => path.Actions.Count > 0)
                .ToArray();
            if (navigable.Length == 0)
            {
                continue;
            }

            // A member whose whole answer is a placement is not disagreeing with one
            // that climbs or jumps; it is doing something weaker. At Mount Corel's bird
            // nest Cloud, Tifa and Cid all climb the same ladder while Yuffie's version
            // of that script only sets her down at the top, and counting her landing as
            // a rival answer threw the climb away entirely.
            var placementOnly = navigable.All(path => path.Actions.All(action => action.IsPlacement));
            if (placementOnly && agreedIsStrong)
            {
                continue;
            }

            var key = DescribePartyMemberActions(navigable);
            if (agreed is null || (agreedIsPlacementOnly && !placementOnly))
            {
                agreed = navigable;
                agreedKey = key;
                agreedIsPlacementOnly = placementOnly;
                agreedIsStrong = !placementOnly;
                continue;
            }

            if (!string.Equals(agreedKey, key, StringComparison.Ordinal))
            {
                return Array.Empty<NavigationExecutionPath>();
            }
        }

        return agreed ?? (IReadOnlyList<NavigationExecutionPath>)Array.Empty<NavigationExecutionPath>();
    }

    private static string DescribePartyMemberActions(
        IReadOnlyList<NavigationExecutionPath> paths) =>
        string.Join(
            "|",
            paths.Select(path => string.Join(
                ";",
                path.Actions.Select(action =>
                    $"{action.Kind}:{action.SourceScript}:{action.X}:{action.Y}:{action.Z}:" +
                    $"{action.Triangle}:{action.DestinationField}:{action.RequiredInput}:" +
                    $"{action.RequiresActionActivation}"))));
    // How many ways through one script the walk will follow before it stops looking.
    //
    // Sixty-four was enough while only byte comparisons forked the walk. A room built
    // out of choices does not fit in that: Coral Valley's own climb routine asks twice
    // and reads the party's triangle four times, which is twelve forks and up to four
    // thousand ways through - so the walk ran out of room while still deep in one arm
    // and reported a single landing for a ladder that reaches five places. The collapse
    // afterwards keeps one edge per way through and then discards duplicates, so the
    // number that matters is how many distinct landings survive, not how many ways were
    // walked; this is set to cover the largest routine in the installed data with room
    // over, and the whole-game sweep is what says it stays affordable.
    private const int MaximumExecutionPaths = 4096;

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
        while (pending.Count != 0 && results.Count < MaximumExecutionPaths)
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
                foreach (var calledPath in calledPaths.Take(MaximumExecutionPaths - results.Count))
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

            if (opcode.Id is >= 0x04 and <= 0x06 && opcode.Bytes.Length >= 3)
            {
                // PREQ/PRQSW/PRQEW address a party slot, not an entity, so the group byte
                // cannot be resolved statically - which is why only the entity-addressed
                // calls above are followed. But a climbable vine is encoded exactly this
                // way: the LINE trigger freezes the player and asks whoever is leading to
                // run its ladder routine, and the field gives every playable character an
                // identical copy of it. Both Mythril Mine climbs are built like that, and
                // with the call unresolved the upper ledge stayed off the walkmesh graph -
                // the Junon-side mine mouth and the Long Range Materia above it were
                // reported unreachable and hidden from the exit list.
                var partyScript = opcode.Bytes[2] & 0x1F;
                var partyPaths = CollectPartyMemberActionPaths(
                    groups,
                    partyScript,
                    cursor.Constants,
                    nestedCallStack);
                foreach (var calledPath in partyPaths.Take(MaximumExecutionPaths - results.Count))
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
                // XYZI. A field can carry the party across a gap by putting them down
                // on the other side of it and then walking them a few steps, which is
                // what the Forgotten Capital's lake platforms do: each of loslake1's
                // four lines runs a party script whose XYZI places the character on the
                // far platform's own triangle and whose MOVE then walks them clear of
                // the edge. Without this the four platforms are four islands and the
                // crystal chamber cannot be reached at all.
                //
                // The landing is the XYZI, not the MOVE: the MOVE is ordinary walking on
                // the triangle the character has already been put on, and taking it as
                // the destination would describe a place the field never sends them to.
                case 0xA5 when HasConstantMovementArguments(opcode.Bytes) && opcode.Bytes.Length >= 11:
                    nextActions = [.. cursor.Actions, NavigationAction.PlacedMove(
                        groupIndex,
                        scriptIndex,
                        BitConverter.ToInt16(opcode.Bytes, 3),
                        BitConverter.ToInt16(opcode.Bytes, 5),
                        BitConverter.ToInt16(opcode.Bytes, 7),
                        BitConverter.ToUInt16(opcode.Bytes, 9))];
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
        (fieldId == 238 &&
         entityId is >= 14 and <= 17 &&
         scriptId == 1) ||

        // gongaga: line4 is the only way back out of the village - its six native
        // gateways are all building doors. The handler MAPJUMPs to wm16 or to gonjun1
        // depending on bank 3 address 132 bit 6, so both destinations are recorded and
        // the label promises neither.
        (fieldId == 518 &&
         entityId == 11 &&
         scriptId == 1);

    private static bool TryResolveConditionalBranch(
        ParsedOpcode opcode,
        IReadOnlyDictionary<BankByteAddress, byte> constants,
        out bool? condition,
        out int falseTarget)
    {
        condition = null;
        falseTarget = -1;
        // The engine measures a conditional's jump from the operand's own position, not
        // from the end of the opcode: 6116A6 and 61171F both add the operand to the
        // address of the byte it was read from, which is index 5 in a byte comparison
        // whether the operand is one byte wide or two. The long form used to add six and
        // so landed one past every branch it resolved - the same off-by-one Kujata's
        // formatted goto carries for the long forms.
        if (opcode.Id == 0x14 && opcode.Bytes.Length >= 6)
        {
            falseTarget = opcode.Offset + ByteComparisonOperandIndex + opcode.Bytes[5];
        }
        else if (opcode.Id == 0x15 && opcode.Bytes.Length >= 7)
        {
            falseTarget = opcode.Offset + ByteComparisonOperandIndex +
                BitConverter.ToUInt16(opcode.Bytes, 5);
        }
        else if (TryResolveComparisonBranch(opcode, out falseTarget))
        {
            // A word comparison forks the script exactly as a byte one does, and until
            // now this said "not a conditional" and let the walk carry straight on into
            // the false side. Every alternative a field guards with a word test was
            // therefore folded into whichever branch happened to come last in the file.
            // Coral Valley is where that showed: the cave asks which way to climb and
            // then reads the party's own triangle - a word test - to pick the landing,
            // and seven of its landings, including the one the way north is on, never
            // reached the walkmesh graph at all. The comparands live in banks this walk
            // does not track, so the answer is genuinely unknown and both sides are
            // walked, which is what the byte forms already do when their bank is not
            // constant.
            return true;
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

    // Ladders the field polls for rather than triggering from a LINE.
    //
    // Every traversal above is found by starting at a LINE entity and following what its
    // scripts do. Cosmo Canyon's stairwell has no LINE anywhere in it: four unnamed
    // entities sit in a loop reading the party leader's own position and, when the
    // triangle underneath is one of the twenty-nine the shaft is built from and the
    // confirm key is down, ask the leader to run one of seven pairs of climb scripts.
    // Read only from LINEs, that field has no traversals at all and its seven storeys
    // are seven islands, which is exactly what the installed catalog reported for it.
    //
    // Everything below is a check rather than an assumption, because a wrong edge here
    // is a route the player cannot walk. The first draft of this assumed that a test of
    // Bank[6][6] meant the leader's triangle, and the Cave of the Gi shows why that is
    // not safe: gidun_1 fills the same bank with AXYZI from an ordinary entity, and its
    // word 6 holds a Y coordinate. Comparing that against a triangle number would have
    // invented a ladder out of a coincidence.
    //
    //   * The producer is verified. Only PXYZI - party member, not entity - counts, only
    //     for party slot 0, and the address the later test reads has to be the address
    //     that opcode wrote the triangle to.
    //   * Control flow ends the press. The triangle test is the enclosing condition and
    //     stands until the next one or a return, because the branches inside it belong
    //     to it - the bottom rung offers one key on one branch and the confirm key on
    //     the next, and both run the same climb. The press itself has to be the thing
    //     immediately before the request, so any other conditional, any jump, or a
    //     request that does not qualify drops it.
    //   * The key mask is checked. Only the confirm bits count; a field that watches a
    //     different key is watching for something else.
    //   * The climb script has to belong to the leader family. A party-member request
    //     names a slot, not an entity, so the script it runs is whichever character is
    //     leading. That is only safe to read when the playable characters of the field
    //     agree on the climb, so the same script index has to carry the same LADER in at
    //     least two groups - the walk up to it differs by a few units per model and is
    //     not part of the agreement.
    private static IReadOnlyList<FieldScriptNavigationTransition> ReadTrianglePolledLadders(
        int fieldId,
        IReadOnlyList<ScriptGroup> groups)
    {
        var climbsByScript = ReadLeaderFamilyClimbs(groups);
        if (climbsByScript.Count == 0)
        {
            return Array.Empty<FieldScriptNavigationTransition>();
        }

        var emptyConstants = new Dictionary<BankByteAddress, byte>();
        var transitions = new List<FieldScriptNavigationTransition>();
        foreach (var group in groups)
        {
            foreach (var script in group.Scripts)
            {
                var leaderTriangleAddress = -1;
                var pendingTriangle = -1;
                var pendingTriangleEnd = int.MaxValue;
                var sawConfirm = false;
                foreach (var opcode in ReadOpcodes(script.Value))
                {
                    if (opcode.Id == PartyMemberPositionOpcode)
                    {
                        leaderTriangleAddress = ReadLeaderTriangleAddress(opcode);
                        continue;
                    }

                    if (opcode.Id == ReturnOpcode)
                    {
                        pendingTriangle = -1;
                        sawConfirm = false;
                        continue;
                    }

                    if (opcode.Id == CompareWordOpcode &&
                        leaderTriangleAddress >= 0 &&
                        IsLeaderTriangleTest(opcode, leaderTriangleAddress) &&
                        TryResolveComparisonBranch(opcode, out var triangleTestEnd))
                    {
                        pendingTriangle = BitConverter.ToUInt16(opcode.Bytes, 4);
                        // Where the test itself gives up. Everything the triangle governs
                        // is inside its own body, so the match ends where the body does
                        // rather than running on into whatever the field does next.
                        pendingTriangleEnd = triangleTestEnd;
                        sawConfirm = false;
                        continue;
                    }

                    if (pendingTriangle >= 0 && opcode.Offset >= pendingTriangleEnd)
                    {
                        pendingTriangle = -1;
                        pendingTriangleEnd = int.MaxValue;
                        sawConfirm = false;
                    }

                    if (opcode.Id is KeyHeldOpcode or KeyPressedOpcode)
                    {
                        // Only the confirm key counts. A test of some other key is not
                        // this shape, but it does not end the triangle test either: the
                        // stairwell's bottom rung offers one key on one branch and the
                        // confirm key on the next, and both run the same climb.
                        sawConfirm = pendingTriangle >= 0 && IsConfirmKeyTest(opcode);
                        continue;
                    }

                    if (opcode.Id is PartyMemberRequestOpcode or PartyMemberRequestSyncOpcode or
                        PartyMemberRequestGuaranteedOpcode)
                    {
                        if (pendingTriangle >= 0 &&
                            sawConfirm &&
                            opcode.Bytes.Length >= 3 &&
                            opcode.Bytes[1] == LeaderPartySlot &&
                            climbsByScript.TryGetValue(opcode.Bytes[2] & NativeScriptNumberMask, out var climb))
                        {
                            transitions.Add(new FieldScriptNavigationTransition(
                                fieldId,
                                FieldNavigationTransitionKind.Ladder,
                                group.Index,
                                climb.FootX,
                                climb.FootY,
                                // The foot's own elevation is whatever the polled triangle
                                // sits at, and the triangle travels with the transition so
                                // it never has to be recovered from a height at all.
                                0,
                                climb.Ladder.X,
                                climb.Ladder.Y,
                                climb.Ladder.Z,
                                climb.Ladder.Triangle,
                                $"polled-ladder:{fieldId}:{group.Index}:{pendingTriangle}:{opcode.Bytes[2] & NativeScriptNumberMask}",
                                climb.Ladder.RequiredInput,
                                RequiresAction: true,
                                SourceTriangle: pendingTriangle));
                        }

                        sawConfirm = false;
                        continue;
                    }

                    // Anything else that can move control elsewhere ends the press. The
                    // triangle test is the enclosing condition and stands until the next
                    // one or a return; the confirm press has to be the thing immediately
                    // before the request, so a jump or another test in between drops it.
                    if (TryResolveUnconditionalBranch(opcode, out _) ||
                        TryResolveConditionalBranch(opcode, emptyConstants, out _, out _) ||
                        TryResolveComparisonBranch(opcode, out _))
                    {
                        sawConfirm = false;
                    }
                }
            }
        }

        return transitions;
    }

    // The climb scripts a party-member request can reach. The request names a slot, so
    // the script that runs belongs to whoever is leading; taking it from the first group
    // that happens to have that number would read an unrelated actor's script. Only the
    // groups the field itself binds to a playable character are read, and only a script
    // they agree on, which in practice means the two or three leaders a field allows.
    private static Dictionary<int, (NavigationAction Ladder, int FootX, int FootY)> ReadLeaderFamilyClimbs(
        IReadOnlyList<ScriptGroup> groups)
    {
        var candidates = new Dictionary<int, List<(NavigationAction Ladder, int FootX, int FootY)>>();
        foreach (var group in groups)
        {
            if (!IsPlayableCharacterGroup(group))
            {
                continue;
            }

            foreach (var script in group.Scripts)
            {
                if (!TryReadPolledLadderScript(group.Index, script.Key, script.Value, out var climb))
                {
                    continue;
                }

                if (!candidates.TryGetValue(script.Key, out var found))
                {
                    found = [];
                    candidates[script.Key] = found;
                }

                found.Add(climb);
            }
        }

        var agreed = new Dictionary<int, (NavigationAction Ladder, int FootX, int FootY)>();
        foreach (var candidate in candidates)
        {
            if (candidate.Value.Count < MinimumLeaderFamilySize)
            {
                continue;
            }

            // Agreement is about the ladder, not about the walk up to it. Cosmo's
            // stairwell has Cloud stepping to (-151,745) and Tifa and Cid to (-148,757)
            // for the same climb, because they are different models standing in
            // slightly different places; the ladder they then take is identical.
            var first = candidate.Value[0];
            if (candidate.Value.All(entry =>
                    entry.Ladder.X == first.Ladder.X &&
                    entry.Ladder.Y == first.Ladder.Y &&
                    entry.Ladder.Z == first.Ladder.Z &&
                    entry.Ladder.Triangle == first.Ladder.Triangle &&
                    entry.Ladder.RequiredInput == first.Ladder.RequiredInput))
            {
                agreed[candidate.Key] = first;
            }
        }

        return agreed;
    }

    // Where a comparison gives up, for the word forms the byte-form resolver above does
    // not cover. A word comparison carries two sixteen-bit addresses instead of two
    // bytes, so its operand sits at index 7 rather than 5, and 611A89 and 611B02 add it
    // to that position exactly as the byte handlers do - the long form reads two bytes
    // from the same place the short form reads one, and both branch from there. Reading
    // it as "the end of the opcode" put the long form one byte past its real target.
    private static bool TryResolveComparisonBranch(ParsedOpcode opcode, out int falseTarget)
    {
        falseTarget = -1;
        switch (opcode.Id)
        {
            case CompareWordOpcode when opcode.Bytes.Length >= 8:
            case CompareUnsignedWordOpcode when opcode.Bytes.Length >= 8:
                falseTarget = opcode.Offset + WordComparisonOperandIndex +
                    opcode.Bytes[WordComparisonOperandIndex];
                return true;
            case CompareWordLongOpcode when opcode.Bytes.Length >= 9:
            case CompareUnsignedWordLongOpcode when opcode.Bytes.Length >= 9:
                falseTarget = opcode.Offset + WordComparisonOperandIndex +
                    BitConverter.ToUInt16(opcode.Bytes, WordComparisonOperandIndex);
                return true;
            default:
                return false;
        }
    }

    // Where each comparison keeps the jump it takes when the test fails.
    private const int ByteComparisonOperandIndex = 5;
    private const int WordComparisonOperandIndex = 7;

    // A group the field binds to a playable character with A0, rather than to an
    // ordinary field model with A1. This is what makes the family real: a party-member
    // request names a slot, and the script it runs belongs to whichever of these groups
    // is currently leading.
    private static bool IsPlayableCharacterGroup(ScriptGroup group) =>
        group.Scripts.Values.Any(script =>
            ReadOpcodes(script).Any(opcode => opcode.Id == PlayableCharacterBindingOpcode));

    // PXYZI reads a party member's own position into four bank words. Only slot 0 - the
    // character the player is actually moving - is a leader position, and the fourth
    // word is the triangle. AXYZI looks similar and is not this: it reads an arbitrary
    // entity, and in the Cave of the Gi its Y lands in the very word this used to
    // assume was a triangle.
    private static int ReadLeaderTriangleAddress(ParsedOpcode opcode)
    {
        if (opcode.Bytes.Length < 8 ||
            opcode.Bytes[3] != LeaderPartySlot ||
            (opcode.Bytes[2] & 0x0F) != LeaderPositionBank)
        {
            return -1;
        }

        return opcode.Bytes[7];
    }

    private static bool IsLeaderTriangleTest(ParsedOpcode opcode, int leaderTriangleAddress) =>
        opcode.Bytes.Length >= 8 &&
        (opcode.Bytes[1] & 0xF0) == LeaderPositionBank << 4 &&
        (opcode.Bytes[1] & 0x0F) == 0x00 &&
        BitConverter.ToUInt16(opcode.Bytes, 2) == leaderTriangleAddress &&
        opcode.Bytes[6] == 0;

    private static bool IsConfirmKeyTest(ParsedOpcode opcode) =>
        opcode.Bytes.Length >= 3 &&
        BitConverter.ToUInt16(opcode.Bytes, 1) == NativeConfirmKeyMask;

    private static bool TryReadPolledLadderScript(
        int groupIndex,
        int scriptIndex,
        byte[] script,
        out (NavigationAction Ladder, int FootX, int FootY) climb)
    {
        climb = default;
        var footX = 0;
        var footY = 0;
        var haveFoot = false;
        foreach (var opcode in ReadOpcodes(script))
        {
            if (opcode.Id == WalkToOpcode && opcode.Bytes.Length >= 6 && opcode.Bytes[1] == 0)
            {
                footX = BitConverter.ToInt16(opcode.Bytes, 2);
                footY = BitConverter.ToInt16(opcode.Bytes, 4);
                haveFoot = true;
                continue;
            }

            if (opcode.Id != 0xC2 || !HasConstantMovementArguments(opcode.Bytes))
            {
                continue;
            }

            var ladder = NavigationAction.Ladder(
                groupIndex,
                scriptIndex,
                BitConverter.ToInt16(opcode.Bytes, 3),
                BitConverter.ToInt16(opcode.Bytes, 5),
                BitConverter.ToInt16(opcode.Bytes, 7),
                BitConverter.ToUInt16(opcode.Bytes, 9),
                ResolveLadderInput(opcode.Bytes[11]));
            climb = (ladder, haveFoot ? footX : ladder.X, haveFoot ? footY : ladder.Y);
            return true;
        }

        return false;
    }

    private const byte ReturnOpcode = 0x00;
    private const byte CompareWordOpcode = 0x16;
    private const byte CompareWordLongOpcode = 0x17;
    private const byte CompareUnsignedWordOpcode = 0x18;
    private const byte CompareUnsignedWordLongOpcode = 0x19;
    private const byte KeyHeldOpcode = 0x30;
    private const byte KeyPressedOpcode = 0x31;
    // The three ways a script asks a party member to run one of its own scripts, taken
    // from the pinned data: PREQ is 0x04, PRQSW 0x05 and PRQEW 0x06. An earlier reading
    // of these was one out across the board and only worked because the stairwell
    // happens to use 0x06 either way; 0x07 is not a party request at all.
    private const byte PartyMemberRequestOpcode = 0x04;
    private const byte PartyMemberRequestGuaranteedOpcode = 0x05;
    private const byte PartyMemberRequestSyncOpcode = 0x06;

    // A0 binds a group to a playable character; A1 binds one to an ordinary field model.
    private const byte PlayableCharacterBindingOpcode = 0xA0;
    private const byte PartyMemberPositionOpcode = 0x75;
    private const byte WalkToOpcode = 0xA8;
    private const byte LeaderPartySlot = 0x00;
    private const int LeaderPositionBank = 6;
    private const int NativeConfirmKeyMask = 544;
    private const int NativeScriptNumberMask = 0x1F;
    private const int MinimumLeaderFamilySize = 2;


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
        FieldNavigationTriggerLine Line,
        bool RunsTalk = false);

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
        bool RequiresActionActivation,
        // An XYZI landing rather than a traversal the script performs. It is real
        // movement, but it is the weaker kind of evidence: scripts also use XYZI to
        // tidy a character into place at the end of an animation.
        bool IsPlacement = false)
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

        /// <summary>
        /// A crossing the field performs by placing the character somewhere and letting
        /// them walk on from there. It has no key of its own - stepping onto the line is
        /// the whole of it - so it is an ordinary jump as far as routing is concerned,
        /// but it carries a real height because the two sides are on different floors.
        /// </summary>
        public static NavigationAction PlacedMove(int group, int script, int x, int y, int z, int triangle) =>
            new(
                ActionKind.Jump,
                group,
                script,
                x,
                y,
                z,
                triangle,
                -1,
                FieldNavigationInput.None,
                false,
                IsPlacement: true);

        public static NavigationAction MapJump(int group, int script, int destinationField) =>
            new(ActionKind.MapJump, group, script, 0, 0, null, -1, destinationField, FieldNavigationInput.None, false);
    }
}









