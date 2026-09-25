using System.Collections.Immutable;
using System.Numerics;
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
    int SourceTriangle = -1,
    // The entities whose model this traversal moves. It is the player's only while one of
    // those models is the controlled one (script context +0x2A, which field setup, PC,
    // 0061BC67 and CC set), so the runtime offers it only then. Null when the walk could not
    // tell whose model moves. With Conditions this is only their movers, listed.
    IReadOnlyList<int>? MoverEntityIds = null,
    // Every way the traversal can be the player's, any one of which is enough: a model that has
    // to be the controlled one, paired with the characters that have to be in party slot 0 for
    // the routine to go this way. A party-slot request runs slot 0's own copy, so a member's
    // copy moves the player only while that member both leads and is controlled; a routine that
    // tests who leads goes one way per leader. Null when nothing is required.
    IReadOnlyList<FieldScriptNavigationCondition>? Conditions = null);

/// <summary>
/// One way a traversal is the player's: <paramref name="ControlledEntityId"/>'s model is the
/// controlled one (null: whichever is), and a character in
/// <paramref name="LeaderCharacterIds"/> is in party slot 0 (null: whoever is).
/// </summary>
public readonly record struct FieldScriptNavigationCondition(
    int? ControlledEntityId,
    IReadOnlyList<int>? LeaderCharacterIds)
{
    /// <summary>A value key: the record's own equality compares the list by reference.</summary>
    public string Key => $"{ControlledEntityId?.ToString() ?? "*"}:{(LeaderCharacterIds is null ? "*" : string.Join(",", LeaderCharacterIds))}";
}

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

    /// <summary>
    /// Whether the player reaches this entity by walking into it rather than talking to it:
    /// its Talk does nothing, and touching it runs its Contact script (slot 2). The collision
    /// routine (00637724) sets a model's contact byte when the controlled model touches it,
    /// whatever its Talk flag, and 0060C94D then starts script 2. <see cref="DialogIds"/> are
    /// then what Contact can show.
    /// </summary>
    public bool ContactOnly { get; init; }
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

/// <summary>
/// When a script exit's MAPJUMP to <paramref name="DestinationFieldId"/> runs: any one of the
/// <paramref name="Alternatives"/>, each a set of live savemap tests
/// (<see cref="FieldScriptGuardTest"/>) that must all be met. A destination with no entry, or
/// reached some way no test decides, is not guarded.
/// </summary>
public sealed record FieldScriptExitGuard(
    int DestinationFieldId,
    IReadOnlyList<IReadOnlyList<FieldScriptGuardTest>> Alternatives);

public sealed record FieldScriptNavigationReadResult(
    bool IsUsable,
    IReadOnlyList<FieldScriptNavigationTransition> Transitions,
    IReadOnlyList<FieldNavigationTarget> Exits,
    IReadOnlyList<FieldScriptNpcDefinition> Npcs,
    IReadOnlyList<FieldScriptWaitDefinition> Waits,
    string Diagnostic)
{
    public IReadOnlyList<int> MapNameDialogIds { get; init; } = Array.Empty<int>();

    /// <summary>
    /// The live guards of <see cref="Exits"/>, by exit <see cref="FieldNavigationTarget.StableId"/>:
    /// for each guarded destination, the savemap tests that decide whether its MAPJUMP runs when
    /// the line is crossed (FieldScriptExitGuards applies them).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<FieldScriptExitGuard>> ExitGuards { get; init; } =
        new Dictionary<string, IReadOnlyList<FieldScriptExitGuard>>(StringComparer.Ordinal);

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
    // The last LINE script the event dispatcher starts (0060C94D: scripts 1 to 6).
    private const int LastLineEventScript = 6;
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
            return ReadFieldFromBytes(fieldId, fieldName, Ff7LzsDecoder.DecodeFieldFile(encodedFieldBytes));
        }
        catch (Exception ex)
        {
            return FieldScriptNavigationReadResult.Invalid(
                $"field={fieldId} {fieldName}, read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Everything <see cref="ReadField"/> publishes, from a decoded field file. Tests build
    /// synthetic fields and read them through here, so they exercise the same readers the
    /// runtime uses.
    /// </summary>
    internal static FieldScriptNavigationReadResult ReadFieldFromBytes(int fieldId, string fieldName, byte[] fieldBytes)
    {
        {
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
            var exitGuards = new Dictionary<string, IReadOnlyList<FieldScriptExitGuard>>(StringComparer.Ordinal);
            var npcs = ReadNpcs(
                fieldId,
                groups,
                StripFieldPrefix(ReadModelResourceNames(fieldBytes), fieldName));
            var waits = ReadWaits(fieldId, groups);
            var mapNameDialogIds = ReadMapNameDialogIds(groups);
            var walkLimits = new NavigationLimits();
            // What a walk may assume about the field's values does not depend on where it
            // starts, so every line shares one, and a routine several lines ask for is walked
            // once.
            var walk = new NavigationWalk(groups, walkLimits, fieldId);
            foreach (var group in groups)
            {
                if (!TryReadLine(group.Script(0), out var line))
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
                foreach (var (_, entrySlots) in group.ScriptEntries())
                {
                    // The dispatcher (0060C94D) starts a LINE's scripts 1 to 6 from its
                    // events and nothing else; a higher script runs only when something asks
                    // for it, and that request is where it is followed from. Every slot that
                    // shares one pointer runs the same code, and 0060D29B runs whatever a
                    // slot points at without comparing slots: code that [OK] shares with a
                    // walk slot also runs when the line is walked on, so it is an entry
                    // script too. It is walked once, under the lowest slot that uses it.
                    var slots = entrySlots.Where(slot => slot <= LastLineEventScript).ToArray();
                    if (slots.Length == 0)
                    {
                        continue;
                    }

                    var script = slots[0];
                    var isEntryScript = slots.Any(slot =>
                        slot > LineConfirmScript ||
                        IsVerifiedActionActivatedExitScript(fieldId, group.Index, slot));
                    var scriptPaths = CollectNavigationActionPaths(
                        groups,
                        group.Index,
                        script,
                        new Dictionary<BankByteAddress, byte>(),
                        new HashSet<(int Group, int Script)>(),
                        walk);
                    // [OK] is action-activated even when the handler goes straight to
                    // MAPJUMP without an explicit field-button opcode; a walk slot's code
                    // needs the button only when it tests for it.
                    var requiresAction =
                        slots.All(slot => slot == LineConfirmScript) ||
                        RequiresActionActivation(group.Script(script));
                    var sink = isEntryScript ? actions : okHandlerActions;
                    foreach (var path in scriptPaths)
                    {
                        // Who has to be in slot 0 for the routine to go this way; a party the
                        // routine changes says nothing about who leads when it is set off.
                        var slotZero = path.PartyChanged ? AnyLeader : path.PartyMask;
                        sink.AddRange(CollapseNavigationRoutineByLeader(OwnedByTheEndOfTheRoutine(walk.Program, path)).Select(action =>
                            action with { RequiresActionActivation = requiresAction, SlotZero = slotZero }));
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

                    var conditions = TraversalConditions(walk.Program, action);
                    if (conditions is { Count: 0 })
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
                        action.RequiresActionActivation,
                        MoverEntityIds: MoverEntities(action.LeaderMask),
                        Conditions: conditions));
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
                    var exitId = $"script-exit:{fieldId}:{group.Index}:{string.Join(',', destinations)}";
                    if (ExitGuards(walk.Program, actions, fieldId, group.Index) is { Count: > 0 } guards)
                    {
                        exitGuards[exitId] = guards;
                    }

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
                    RequiresAction = group.Any(transition => transition.RequiresAction),
                    // One traversal several characters' copies make moves any of them; one
                    // whose mover is not known is offered to anybody. The same holds for who
                    // has to lead.
                    MoverEntityIds = group.Any(transition => transition.MoverEntityIds is null)
                        ? null
                        : group.SelectMany(transition => transition.MoverEntityIds!).Distinct().Order().ToArray(),
                    Conditions = group.Any(transition => transition.Conditions is null)
                        ? null
                        : group.SelectMany(transition => transition.Conditions!)
                            .DistinctBy(condition => condition.Key, StringComparer.Ordinal)
                            .ToArray()
                })
                .ToList();
            exits.AddRange(GoldSaucerPlatformExitCatalog.ForField(fieldId));
            exits.AddRange(FieldTriangleExitCatalog.ForField(fieldId));
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
                $"mapNames={(mapNameDialogIds.Count == 0 ? "none" : string.Join(',', mapNameDialogIds))}, " +
                $"walkLimits={walkLimits.Count}" +
                (walkLimits.Count == 0
                    ? string.Empty
                    : $" (recursive requests {walkLimits.Recursions}, walk budget {walkLimits.Budgets}, long ways {walkLimits.LongPaths})") +
                (walkLimits.UnsupportedEntities == 0 ? string.Empty : $", unsupportedMovers={walkLimits.UnsupportedEntities}"))
            {
                MapNameDialogIds = mapNameDialogIds,
                ExitGuards = exitGuards
            };
        }
    }

    private static IReadOnlyList<int> ReadMapNameDialogIds(IReadOnlyList<ScriptGroup> groups) =>
        groups
            .OrderBy(group => group.Index)
            .SelectMany(group => group.Script(0))
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
            // The dispatcher (0060D29B) runs a Talk slot that repeats script 0's pointer like
            // any other, starting Init over, so for a model that is a real Talk. But only a
            // model can be talked to (0060C94D starts Talk from a model's own event flags), and
            // the entities laid out that way without one are scene directors whose script 0 runs
            // the field's cutscenes: their lines are the scene's, not somebody's.
            if (!group.HasSlot(TalkScript) ||
                (group.Program.CanonicalSlot(group.Index, TalkScript) != TalkScript && !LoadsModel(group)))
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
                // Some people answer only when walked into: Bugenhagen at the foot of Cosmo
                // Canyon's observatory has an empty Talk and his whole conversation in Contact.
                if (ReadContactNpc(fieldId, groups, group, modelResources) is { } contact)
                {
                    definitions.Add(contact);
                }

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
    /// An entity the player talks to by walking into it: it loads a model, is not a party
    /// character (followers are hidden by PC and are the party, not somebody to meet), and its
    /// Contact script (slot 2) shows dialogue or opens a menu. 0060C94D starts script 2 at
    /// priority 1 once the collision routine (00637724) has marked the model as touched by the
    /// controlled one, and 0060D29B runs whatever the slot points to unless its first byte is
    /// RET. Its live visibility and collision state decide at runtime whether it can be touched
    /// (FieldNavigationNpcReader).
    /// </summary>
    private static FieldScriptNpcDefinition? ReadContactNpc(
        int fieldId,
        IReadOnlyList<ScriptGroup> groups,
        ScriptGroup group,
        IReadOnlyList<string> modelResources)
    {
        const int ContactScript = 2;
        if (!LoadsModel(group) || IsPlayableCharacterGroup(group) ||
            !group.Program.Runs(group.Index, ContactScript))
        {
            return null;
        }

        var dialogIds = new List<int>();
        CollectTalkDialogIds(groups, group.Index, ContactScript, dialogIds, new HashSet<(int Group, int Script)>());
        var menu = group.Program.SlotReach(group.Index, ContactScript).Any(opcode => opcode.Id == MenuOpcode);
        if (dialogIds.Count == 0 && !menu)
        {
            return null;
        }

        return new FieldScriptNpcDefinition(
            fieldId,
            group.Index,
            group.Name,
            dialogIds.Distinct().ToArray(),
            ModelResourceName: ReadModelResourceName(group, modelResources))
        {
            ContactOnly = true
        };
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
        if (modelResources.Count == 0)
        {
            return string.Empty;
        }

        foreach (var opcode in group.Script(0))
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
        group.Script(0).Any(opcode => opcode.Id == OpcodeLoadModel);

    private static bool HasPerceivableTalk(ScriptGroup group) =>
        group.HasSlot(TalkScript) &&
        group.Script(TalkScript).Any(opcode =>
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
                !TryReadLine(lineGroup.Script(0), out var line))
            {
                continue;
            }

            foreach (var (_, slots) in lineGroup.Program.DistinctEntries(lineGroup.Index))
            {
                var script = lineGroup.Script(slots[0]);
                if (!slots.Contains(LineConfirmScript) && !RequiresActionActivation(script))
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
                        lineGroup.Program.DistinctEntries(lineGroup.Index).Any(entry =>
                            RequestsEntityScript(npcGroup, lineGroup.Script(entry.Slots[0]), TalkScript)));
                }

                break;
            }
        }

        return chosen;
    }

    /// <summary>
    /// Whether <paramref name="script"/> requests one of <paramref name="entityId"/>'s own
    /// scripts. Only REQ, REQSW and REQEW name an entity: PREQ, PRQSW and PRQEW name a party
    /// slot, whoever is in it, so a party-slot number that happens to equal an entity index
    /// is not a request of that entity.
    /// </summary>
    private static bool RequestsEntity(IReadOnlyList<FieldScriptInstruction> script, int entityId) =>
        script.Any(opcode =>
            opcode.Id is >= EntityRequestOpcode and <= EntityRequestWaitOpcode &&
            opcode.Bytes.Length >= 3 &&
            opcode.Bytes[1] == entityId);

    /// <summary>
    /// REQ, REQSW and REQEW name the entity in their first argument and pack the priority
    /// over the script number in the second: <c>(priority &lt;&lt; 5) | script</c>. A request
    /// of a slot that shares the named script's pointer runs that script, so pointers are
    /// compared, not slot numbers.
    /// </summary>
    private static bool RequestsEntityScript(ScriptGroup target, IReadOnlyList<FieldScriptInstruction> script, int scriptId) =>
        target.Program.Pointer(target.Index, scriptId) is { } pointer &&
        script.Any(opcode =>
            opcode.Id is >= EntityRequestOpcode and <= EntityRequestWaitOpcode &&
            opcode.Bytes.Length >= 3 &&
            opcode.Bytes[1] == target.Index &&
            target.Program.Pointer(target.Index, opcode.Bytes[2] & 0x1F) == pointer);

    private static NpcInteractionLineDefinition? ReadNpcInteractionLine(
        IReadOnlyList<ScriptGroup> groups,
        ScriptGroup npcGroup,
        bool allowEnabledTalkProxy = false)
    {
        if (!npcGroup.Script(0).Any(opcode =>
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

    /// <summary>
    /// The dialogue an entity's script shows, following what it can actually run: control
    /// flow wherever it goes, the scripts it requests (REQ, REQSW, REQEW), and the script
    /// RETTO hands over to in place of the next byte. A message the script jumps over is not
    /// shown and is not collected. PREQ, PRQSW and PRQEW ask whoever is in a party slot to
    /// run their own script: those are a party member's lines, not this entity's.
    /// </summary>
    private static void CollectTalkDialogIds(
        IReadOnlyList<ScriptGroup> groups,
        int groupIndex,
        int scriptIndex,
        ICollection<int> dialogIds,
        ISet<(int Group, int Script)> visited)
    {
        if (groupIndex < 0 || groupIndex >= groups.Count)
        {
            return;
        }

        var group = groups[groupIndex];
        var slot = group.Program.CanonicalSlot(groupIndex, scriptIndex);
        if (!group.HasSlot(slot) || !visited.Add((groupIndex, slot)))
        {
            return;
        }

        // What an event or a request starts runs from the slot's pointer to the RET it reaches:
        // a Talk that shares Init's pointer runs Init's code, never the Main after it.
        foreach (var opcode in group.Program.SlotReach(groupIndex, slot))
        {
            switch (opcode.Id)
            {
                case >= EntityRequestOpcode and <= EntityRequestWaitOpcode when opcode.Bytes.Length >= 3:
                    CollectTalkDialogIds(groups, opcode.Bytes[1], opcode.Bytes[2] & 0x1F, dialogIds, visited);
                    break;
                case ReturnToOpcode when opcode.Bytes.Length >= 2:
                    CollectTalkDialogIds(groups, groupIndex, opcode.Bytes[1] & 0x1F, dialogIds, visited);
                    break;
                case 0x40 when opcode.Bytes.Length >= 3:
                    dialogIds.Add(opcode.Bytes[2]);
                    break;
                case 0x48 when opcode.Bytes.Length >= 4:
                    dialogIds.Add(opcode.Bytes[3]);
                    break;
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
        var program = FieldScriptProgram.TryParse(section, out var programDiagnostic);
        if (program is null)
        {
            diagnostic = programDiagnostic;
            return Array.Empty<ScriptGroup>();
        }

        var groups = new List<ScriptGroup>(groupCount);
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var name = ReadAscii(section, namesOffset + groupIndex * 8, 8);
            var scripts = new Dictionary<int, byte[]>();
            var starts = new Dictionary<int, int>();
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
                starts[pair.Value] = start;
            }

            groups.Add(new ScriptGroup(groupIndex, name, scripts, program, starts));
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
    /// What a party-slot request can make happen: one variant for each distinct thing the
    /// members' copies of the script do, each marked with the members whose copy does it.
    ///
    /// <para>PREQ names a party slot, and what runs is the copy belonging to the character in
    /// it - PC (0061BCD7) binds a character to its entity - so each playable character's copy
    /// is a real answer for that character. This used to take an answer only when every copy
    /// agreed and dropped the routine when they did not: Mount Corel's border9 places most of
    /// the party on triangle 9 and Aeris on 16, and neither landing survived. Members whose
    /// copies do the same thing share a variant, and members whose copy moves nobody share
    /// one of their own, so the walk goes on for them without a movement.</para>
    ///
    /// <para>A request to slot 0 also settles who is in slot 0 for the rest of the way through
    /// (<see cref="NavigationExecutionPath.PartyMask"/>); a request to slot 1 or 2 does not.</para>
    ///
    /// <para>Whose movement a member's copy makes is the player's follows who is controlled when
    /// the request is made (<see cref="ControlAtTrigger"/>). While control is still the
    /// trigger's, slot 0's movement is tagged with the member and offered while that member's
    /// model is the controlled one: PC and 0061BC67 give control to slot 0, and the walk takes
    /// the player to be controlling slot 0's model unless CC has named another. A member in slot 1
    /// or 2 is then not the one being moved. After CC, the member CC named is the player whichever
    /// slot it answers for; while control follows slot 0 after a party change, slot 0's copy is
    /// the player's.</para>
    /// </summary>
    private static IReadOnlyList<NavigationExecutionPath> CollectPartyMemberActionPaths(
        IReadOnlyList<ScriptGroup> groups,
        int scriptIndex,
        bool leaderSlot,
        IReadOnlyDictionary<BankByteAddress, byte> initialConstants,
        ISet<(int Group, int Script)> callStack,
        NavigationWalk walk,
        int control = ControlAtTrigger,
        ulong leaders = AnyLeader)
    {
        var memoKey = $"p{scriptIndex}:{leaderSlot}#{DescribeConstants(initialConstants)}#{DescribeCallStack(callStack)}#{control}:{leaders}";
        if (walk.Memo.TryGetValue(memoKey, out var walked))
        {
            return walked;
        }

        var computed = CollectPartyMemberVariants(groups, scriptIndex, leaderSlot, initialConstants, callStack, walk, control, leaders);
        walk.Memo[memoKey] = computed;
        return computed;
    }

    private static IReadOnlyList<NavigationExecutionPath> CollectPartyMemberVariants(
        IReadOnlyList<ScriptGroup> groups,
        int scriptIndex,
        bool leaderSlot,
        IReadOnlyDictionary<BankByteAddress, byte> initialConstants,
        ISet<(int Group, int Script)> callStack,
        NavigationWalk walk,
        int control,
        ulong leaders)
    {
        var variants = new Dictionary<string, (NavigationExecutionPath Path, ulong Members, ulong Representative)>(
            StringComparer.Ordinal);
        foreach (var group in groups)
        {
            // A party-member request names a slot, and only a playable character can be
            // in one. Script numbers are per entity, so an ordinary entity that happens
            // to have a script with the same number is answering a different question:
            // in the Forgotten Capital's lake every one of the four platform lines keeps
            // its own Move at index 3, so asking the family what its script 3 does used
            // to collect three other lines' crossings alongside the character's, decide
            // the family disagreed, and drop the first platform entirely.
            if (!group.HasSlot(scriptIndex) || !IsPlayableCharacterGroup(group))
            {
                continue;
            }

            var member = PartyBit(group.Index);
            if (leaderSlot && (leaders & member) == 0)
            {
                // The caller already knows this member is not in slot 0.
                continue;
            }

            foreach (var path in CollectNavigationActionPaths(
                         groups,
                         group.Index,
                         scriptIndex,
                         initialConstants,
                         callStack,
                         walk,
                         control,
                         leaderSlot && control == ControlFollowsLeader ? leaders & member : AnyLeader))
            {
                var partyMask = leaderSlot && !path.PartyChanged ? path.PartyMask & member : path.PartyMask;
                if (partyMask == 0)
                {
                    continue;
                }

                // A companion's own movement is not the player's while control is the
                // trigger's or follows slot 0: PC (0061BCD7) hides the models of the characters
                // in slots 1 and 2, and control follows slot 0 or CC. What it asks others to
                // do, and a field change it makes, still happen. Without this every
                // character's own way of stepping aside was a variant of its own, and Rocket
                // Town's launch scene, which asks both companions to move again and again,
                // multiplied its ways through by nine at every request. After CC names the
                // companion, its walk has already made its movements the player's (0), and
                // they stay.
                var own = MoverBit(group.Index);
                var variant = leaderSlot || own == 0
                    ? path
                    : path with
                    {
                        Actions = path.Actions
                            .Where(action => action.Kind == ActionKind.MapJump || action.LeaderMask != own)
                            .ToArray()
                    };
                if (leaderSlot && own != 0)
                {
                    // Slot 0's own copy moves slot 0's actor: the pairing survives the members
                    // sharing one variant below.
                    variant = variant with
                    {
                        Actions = variant.Actions
                            .Select(action => action.Kind != ActionKind.MapJump && action.LeaderMask == own
                                ? action with { MovesSlotZero = true }
                                : action)
                            .ToArray()
                    };
                }
                variant = variant with { Actions = LandingActions(variant.Actions) };
                var key = $"{variant.End}#{(leaderSlot ? 0 : partyMask)}#{variant.Control}:{variant.PartyChanged}#{DescribeVariant(variant, own)}";
                variants[key] = variants.TryGetValue(key, out var existing)
                    ? (existing.Path with
                        {
                            PartyMask = existing.Path.PartyMask | partyMask,
                            Constants = Agreed(existing.Path.Constants, variant.Constants)
                        },
                        existing.Members | own,
                        existing.Representative)
                    : (variant with { PartyMask = partyMask }, own, own);
            }
        }

        return variants.Values
            .Select(variant => variant.Members == variant.Representative || variant.Representative == 0
                ? variant.Path
                : variant.Path with
                {
                    // The walked member's own movements are every sharing member's movements.
                    Actions = variant.Path.Actions
                        .Select(action => action.LeaderMask == variant.Representative
                            ? action with { LeaderMask = variant.Members }
                            : action)
                        .ToArray()
                })
            .ToArray();
    }

    // A member's way through, in terms that do not depend on which member it is: its own
    // movements are "self", anything it asks another entity to do keeps that entity.
    private static string DescribeVariant(NavigationExecutionPath path, ulong self) =>
        string.Join(
            ";",
            path.Actions.Select(action =>
                $"{action.Kind}:{action.SourceScript}:{action.X}:{action.Y}:{action.Z}:" +
                $"{action.Triangle}:{action.DestinationField}:{action.RequiredInput}:" +
                $"{action.RequiresActionActivation}:{action.IsPlacement}:{action.MovesSlotZero}:" +
                (self != 0 && action.LeaderMask == self ? "self" : $"{action.SourceGroup}:{action.LeaderMask}")));

    /// <summary>
    /// The ways through a script, merged as they are found: two with the same landing actions
    /// (<see cref="LandingActions"/>), possible leaders and end are one, knowing only the
    /// values they both know. Knowing less can only fork a later test one of them would have
    /// decided, so nothing either leads to is lost; what it saves is a caller forking once per
    /// way through that differs only in values nothing looks at again, and a routine whose
    /// key tests fork thousands of times over running out of room before it is walked.
    /// </summary>
    private sealed class NavigationResults
    {
        private readonly Dictionary<string, int> index = new(StringComparer.Ordinal);
        private readonly List<NavigationExecutionPath> paths = [];

        public int Count => paths.Count;

        public IReadOnlyList<NavigationExecutionPath> Paths => paths;

        public void Add(NavigationExecutionPath path)
        {
            path = path with { Actions = LandingActions(path.Actions) };
            var key = path.ActionKey;
            if (index.TryGetValue(key, out var at))
            {
                var agreed = Agreed(paths[at].Constants, path.Constants);
                if (!ReferenceEquals(agreed, paths[at].Constants))
                {
                    paths[at] = paths[at] with { Constants = agreed };
                }

                return;
            }

            index[key] = paths.Count;
            paths.Add(path);
        }
    }

    /// <summary>
    /// The actions of a way through that decide where it lands, in their order: for the
    /// ordinary models and for each playable character it moves, the first ladder and the
    /// last movement of that route, and the first of each distinct map jump. The collapse
    /// (<see cref="CollapseNavigationRoutineByLeader"/>) reads nothing else, and whatever
    /// comes before or after a way through only adds a ladder before these or a movement
    /// after them, so two ways through that keep the same actions land the same everywhere
    /// they are used. Keeping only these is what lets them be recognised as the same.
    /// </summary>
    private static IReadOnlyList<NavigationAction> LandingActions(IReadOnlyList<NavigationAction> actions)
    {
        if (actions.Count <= 2)
        {
            return actions;
        }

        var named = 0UL;
        foreach (var action in actions)
        {
            if (action.Kind is ActionKind.Ladder or ActionKind.Jump)
            {
                named |= action.LeaderMask;
            }
        }

        var keep = new bool[actions.Count];
        var mapJumps = new HashSet<NavigationAction>();
        for (var index = 0; index < actions.Count; index++)
        {
            if (actions[index].Kind == ActionKind.MapJump && mapJumps.Add(actions[index]))
            {
                keep[index] = true;
            }
        }

        KeepRoute(0);
        for (var remaining = named; remaining != 0; remaining &= remaining - 1)
        {
            KeepRoute(remaining & (~remaining + 1));
        }

        // And each entity's own: which model the player ends up controlling is only known at the
        // end of the whole routine (OwnedByTheEndOfTheRoutine), and its landing is then that
        // model's own first ladder and last movement.
        foreach (var entity in actions.Where(action => action.Kind is ActionKind.Ladder or ActionKind.Jump)
                     .Select(action => action.SourceGroup).Distinct())
        {
            KeepEntity(entity);
        }

        if (Array.TrueForAll(keep, kept => kept))
        {
            return actions;
        }

        var landing = new List<NavigationAction>();
        for (var index = 0; index < actions.Count; index++)
        {
            if (keep[index])
            {
                landing.Add(actions[index]);
            }
        }

        return landing;

        void KeepRoute(ulong mover)
        {
            var firstLadder = -1;
            var last = -1;
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                if (action.Kind is not (ActionKind.Ladder or ActionKind.Jump) ||
                    (action.LeaderMask != 0 && (action.LeaderMask & mover) == 0))
                {
                    continue;
                }

                if (firstLadder < 0 && action.Kind == ActionKind.Ladder)
                {
                    firstLadder = index;
                }

                last = index;
            }

            if (firstLadder >= 0)
            {
                keep[firstLadder] = true;
            }

            if (last >= 0)
            {
                keep[last] = true;
            }
        }

        void KeepEntity(int entity)
        {
            var firstLadder = -1;
            var last = -1;
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                if (action.Kind is not (ActionKind.Ladder or ActionKind.Jump) || action.SourceGroup != entity)
                {
                    continue;
                }

                if (firstLadder < 0 && action.Kind == ActionKind.Ladder)
                {
                    firstLadder = index;
                }

                last = index;
            }

            if (firstLadder >= 0)
            {
                keep[firstLadder] = true;
            }

            if (last >= 0)
            {
                keep[last] = true;
            }
        }
    }

    /// <summary>
    /// The movements of a whole routine that are the player's, now that it is known who the
    /// player controls when it ends. A model the player stops controlling before the routine is
    /// over is not where the player ends up: Cosmo Canyon's observatory sign (cos_btm line 30)
    /// hands control to the telescope view with CC, moves it, and hands control back to
    /// whoever leads, so the view's jump is nobody's landing. After CC, only the named entity's
    /// own movements are where the player is, wherever in the routine they came; after a party
    /// change, slot 0's; otherwise each movement stays offered while its own model is the
    /// controlled one.
    /// </summary>
    private static IReadOnlyList<NavigationAction> OwnedByTheEndOfTheRoutine(
        FieldScriptProgram program,
        NavigationExecutionPath path)
    {
        int? owner = path.Control switch
        {
            >= 0 and var controlled => controlled,
            ControlFollowsLeader when path.PartyMask != AnyLeader && BitOperations.PopCount(path.PartyMask) == 1 =>
                BitOperations.TrailingZeroCount(path.PartyMask),
            _ => null
        };
        if (owner is null && path.Control != ControlFollowsLeader)
        {
            return path.Actions;
        }

        var owned = new List<NavigationAction>(path.Actions.Count);
        foreach (var action in path.Actions)
        {
            if (action.Kind == ActionKind.MapJump)
            {
                owned.Add(action);
            }
            else if (owner is { } controlled)
            {
                if (action.SourceGroup == controlled)
                {
                    owned.Add(action with { LeaderMask = 0 });
                }
            }
            else if (program.IsPartyCharacter(action.SourceGroup) &&
                     MoverBit(action.SourceGroup) is not 0 and var own &&
                     (path.PartyMask & own) != 0)
            {
                // Slot 0 is one of several members: whichever it is moves, while its model is controlled.
                owned.Add(action.LeaderMask == 0 ? action with { LeaderMask = own } : action);
            }
        }

        return owned;
    }

    // The values two ways through both know, with the same value; the first itself when
    // the second knows all of it.
    private static IReadOnlyDictionary<BankByteAddress, byte> Agreed(
        IReadOnlyDictionary<BankByteAddress, byte> first,
        IReadOnlyDictionary<BankByteAddress, byte> second)
    {
        if (first.Count == 0 || ReferenceEquals(first, second))
        {
            return first;
        }

        var agreed = new Dictionary<BankByteAddress, byte>();
        foreach (var (key, value) in first)
        {
            if (second.TryGetValue(key, out var other) && other == value)
            {
                agreed[key] = value;
            }
        }

        return agreed.Count == first.Count ? first : agreed;
    }

    private static string DescribeConstants(IReadOnlyDictionary<BankByteAddress, byte> constants) =>
        string.Join(
            ";",
            constants
                .OrderBy(pair => pair.Key.Bank)
                .ThenBy(pair => pair.Key.Index)
                .Select(pair => $"{pair.Key.Bank}:{pair.Key.Index}:{pair.Value}"));

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

    // How many instructions one way through may run. Loops end sooner, once going round
    // again teaches the walk nothing new; this only bounds a way through that is simply long.
    private const int MaximumExecutionSteps = 2048;

    // How many instructions the walk of one script may run over all its ways through. The
    // path limit above counts distinct results, and ways through that land the same are
    // merged as they are found, so this is what bounds the work of a routine with many forks
    // that all come to the same few landings.
    private const int MaximumWalkSteps = 200_000;

    private static IReadOnlyList<NavigationExecutionPath> CollectNavigationActionPaths(
        IReadOnlyList<ScriptGroup> groups,
        int groupIndex,
        int scriptIndex,
        IReadOnlyDictionary<BankByteAddress, byte> initialConstants,
        ISet<(int Group, int Script)> callStack) =>
        groups.Count == 0 || groupIndex < 0 || groupIndex >= groups.Count
            ? [NavigationExecutionPath.Empty(initialConstants)]
            : CollectNavigationActionPaths(
                groups,
                groupIndex,
                scriptIndex,
                initialConstants,
                callStack,
                new NavigationWalk(groups, new NavigationLimits(), -1));

    /// <summary>
    /// Every way through one script, as the engine runs it: from the slot's pointer,
    /// following control flow anywhere in the code, forking on every test whose answer is
    /// not known, and following the scripts it requests.
    ///
    /// <para>REQEW waits for the requested script (006127A2 mode 3), so each way through the
    /// callee that finishes goes on with the values the callee left, and one that never
    /// finishes is where the caller stops too. REQ and REQSW do not wait (modes 1 and 2), so
    /// the caller goes on with its own values and the callee starts without any the caller
    /// goes on to change. RETTO goes on in the named slot of the same entity instead of the
    /// next byte. RET finishes a way through; GAMEOVER, a handler PC does not implement and a
    /// return to the same place with nothing changed are ways through that never finish.
    /// Only the walk's own limits leave the answer open, and they are counted.</para>
    /// </summary>
    /// <param name="control">Who the player controls when the script starts; see <see cref="ControlAtTrigger"/>.</param>
    /// <param name="leaders">
    /// Who can be in party slot 0 when the script starts. Only passed on while control follows
    /// the leader, where it decides whose movement is the player's; otherwise the callee starts
    /// knowing nothing about slot 0 and its caller combines the answers.
    /// </param>
    private static IReadOnlyList<NavigationExecutionPath> CollectNavigationActionPaths(
        IReadOnlyList<ScriptGroup> groups,
        int groupIndex,
        int scriptIndex,
        IReadOnlyDictionary<BankByteAddress, byte> initialConstants,
        ISet<(int Group, int Script)> callStack,
        NavigationWalk walk,
        int control = ControlAtTrigger,
        ulong leaders = AnyLeader)
    {
        var program = walk.Program;
        if (groupIndex < 0 || groupIndex >= groups.Count)
        {
            return [NavigationExecutionPath.Empty(initialConstants) with { Control = control }];
        }

        var script = program.CanonicalSlot(groupIndex, scriptIndex);
        if (program.Pointer(groupIndex, script) is not { } entry)
        {
            return [NavigationExecutionPath.Empty(initialConstants) with { Control = control }];
        }

        if (callStack.Contains((groupIndex, script)))
        {
            // A script that asks for itself again, directly or through others. The walk does
            // not go round it a second time, so whether it finishes is not known.
            walk.Limits.Count++;
            walk.Limits.Recursions++;
            return [NavigationExecutionPath.Empty(initialConstants) with { End = PathEnd.Limited, Control = control }];
        }

        // The same script from the same known values with the same callers is the same
        // walk. A cutscene that falls through from one script into the next and asks for
        // others as it goes reaches each of them once for every way through that leads
        // there, and walking each again every time is what made Rocket Town's launch scene
        // take minutes.
        var memoKey = $"s{groupIndex}:{script}#{DescribeConstants(initialConstants)}#{DescribeCallStack(callStack)}#{control}:{leaders}";
        if (walk.Memo.TryGetValue(memoKey, out var walked))
        {
            return walked;
        }

        var computed = WalkScript(groups, groupIndex, script, entry, initialConstants, callStack, walk, control, leaders);
        walk.Memo[memoKey] = computed;
        return computed;
    }

    private static string DescribeCallStack(ISet<(int Group, int Script)> callStack) =>
        callStack.Count == 0
            ? string.Empty
            : string.Join(",", callStack.OrderBy(call => call.Group).ThenBy(call => call.Script).Select(call => $"{call.Group}.{call.Script}"));

    private static IReadOnlyList<NavigationExecutionPath> WalkScript(
        IReadOnlyList<ScriptGroup> groups,
        int groupIndex,
        int script,
        int entry,
        IReadOnlyDictionary<BankByteAddress, byte> initialConstants,
        ISet<(int Group, int Script)> callStack,
        NavigationWalk walk,
        int initialControl,
        ulong initialLeaders)
    {
        var program = walk.Program;

        var nestedCallStack = new HashSet<(int Group, int Script)>(callStack)
        {
            (groupIndex, script)
        };
        var pending = new Stack<NavigationExecutionCursor>();
        pending.Push(new NavigationExecutionCursor(
            entry,
            new Dictionary<BankByteAddress, byte>(initialConstants),
            [],
            ImmutableDictionary<int, JoinState>.Empty,
            0,
            initialLeaders,
            initialControl));
        var results = new NavigationResults();
        var explored = new HashSet<(int Offset, ulong State)>();
        var successors = new List<int>(2);
        var steps = 0;
        while (pending.Count != 0)
        {
            if (results.Count >= MaximumExecutionPaths || ++steps > MaximumWalkSteps)
            {
                walk.Limits.Count++;
                walk.Limits.Budgets++;
                break;
            }

            var cursor = pending.Pop();
            if (cursor.Steps >= MaximumExecutionSteps)
            {
                walk.Limits.Count++;
                walk.Limits.LongPaths++;
                results.Add(cursor.End(PathEnd.Limited));
                continue;
            }

            if (!program.TryGetInstruction(cursor.Offset, out var opcode))
            {
                results.Add(cursor.End(PathEnd.Invalid));
                continue;
            }

            // Every loop comes back through a place something jumps to. Back at one knowing
            // nothing it did not know the last time, the way through has already been
            // walked from here, and it goes round for ever; knowing less, it goes on once
            // more with only what both times agree on (widening), which is where a value
            // the loop changes stops being assumed.
            if (program.IsJoinPoint(cursor.Offset))
            {
                if (!cursor.Arrive())
                {
                    results.Add(cursor.End(PathEnd.Stuck));
                    continue;
                }

                // Another way through already came here in exactly this state - the same
                // known values, leaders, actions and loop history - so everything after it
                // has been walked and would only be found again. A fork whose two sides meet
                // again unchanged doubles the ways through at every test otherwise, and a
                // routine of a few dozen such tests never finishes.
                if (!explored.Add((cursor.Offset, cursor.StateHash())))
                {
                    continue;
                }
            }

            var nextOffset = opcode.Next;
            if (opcode.Id == ReturnOpcode)
            {
                results.Add(cursor.End(PathEnd.Returned));
                continue;
            }

            if (opcode.Id == ReturnToOpcode && opcode.Bytes.Length >= 2)
            {
                if (program.Pointer(groupIndex, opcode.Bytes[1] & 0x1F) is { } transfer)
                {
                    pending.Push(cursor.Advance(transfer));
                }
                else
                {
                    results.Add(cursor.End(PathEnd.Invalid));
                }

                continue;
            }

            if (FieldScriptProgram.Stops(opcode.Id))
            {
                // GAMEOVER, or a handler PC does not implement: 006107E1 returns without
                // advancing, so the script never gets past it.
                results.Add(cursor.End(PathEnd.Stuck));
                continue;
            }

            if (opcode.Id is >= EntityRequestOpcode and <= EntityRequestWaitOpcode && opcode.Bytes.Length >= 3)
            {
                var waits = opcode.Id == EntityRequestWaitOpcode;
                var calleeEntity = opcode.Bytes[1];
                var calleeSlot = opcode.Bytes[2] & 0x1F;
                var calledPaths = CollectNavigationActionPaths(
                    groups,
                    calleeEntity,
                    calleeSlot,
                    waits ? cursor.Constants : walk.WithoutLaterWrites(cursor.Constants, groupIndex, nextOffset),
                    nestedCallStack,
                    walk,
                    cursor.Control,
                    LeadersPassedOn(cursor));
                // No way through the callee agrees with who is leading here when the leader
                // has no copy of a routine it asks the party for; the callee then does
                // nothing of its own, and the caller goes on without what it may write.
                GoOnAfter(
                    cursor,
                    calledPaths,
                    waits,
                    nextOffset,
                    () => waits && program.Pointer(calleeEntity, calleeSlot) is { } calleeEntry
                        ? walk.WithoutLaterWrites(cursor.Constants, calleeEntity, calleeEntry)
                        : cursor.Constants);
                continue;
            }

            if (opcode.Id is >= PartyMemberRequestOpcode and <= PartyMemberRequestSyncOpcode && opcode.Bytes.Length >= 3)
            {
                // PREQ/PRQSW/PRQEW address a party slot, not an entity. A climbable vine is
                // encoded exactly this way: the LINE trigger freezes the player and asks
                // whoever is leading to run its ladder routine, and the field gives every
                // playable character a copy of it. Both Mythril Mine climbs are built like
                // that, and with the call unresolved the upper ledge stayed off the walkmesh
                // graph - the Junon-side mine mouth and the Long Range Materia above it were
                // reported unreachable and hidden from the exit list.
                var waits = opcode.Id == PartyMemberRequestSyncOpcode;
                var memberScript = opcode.Bytes[2] & 0x1F;
                var partyPaths = CollectPartyMemberActionPaths(
                    groups,
                    memberScript,
                    opcode.Bytes[1] == LeaderPartySlot,
                    waits ? cursor.Constants : walk.WithoutLaterWrites(cursor.Constants, groupIndex, nextOffset),
                    nestedCallStack,
                    walk,
                    cursor.Control,
                    LeadersPassedOn(cursor));
                // When no member has a copy the caller goes on all the same: most party
                // requests only turn or animate the character, and cutting the walk here
                // lost everything after them (rktsid's way to rcktin6 comes after two).
                // After PRQEW the members have run, so what they write is no longer known.
                GoOnAfter(
                    cursor,
                    partyPaths,
                    waits,
                    nextOffset,
                    () => waits ? walk.WithoutPartyWrites(cursor.Constants, memberScript) : cursor.Constants);
                continue;
            }

            if (opcode.Id == MapJumpOpcode && opcode.Bytes.Length >= 3)
            {
                // MAPJUMP (006131C4) records the destination as request 1 and returns without
                // advancing; it would go on only once script context +0x26 read 2. The field
                // loop (0063C17F) answers request 1 by making the destination the current field
                // and loading it - its own field too - with the previous module set to 1, which
                // 0063BDA8 answers with a complete reset, and field setup (0060BCFA) clears the
                // request and +0x26 again. Only returns from other modules (a battle, a menu, a
                // minigame) set +0x26 to 2. So nothing after a MAPJUMP ever runs, wherever it
                // goes: a jump into the same field reloads it and starts every Init over.
                var destination = BitConverter.ToUInt16(opcode.Bytes, 1);
                cursor.AddAction(NavigationAction.MapJump(groupIndex, script, destination) with { At = opcode.Offset });
                results.Add(cursor.End(PathEnd.LeftField));
                continue;
            }

            if (opcode.Id == ChangeControlOpcode && opcode.Bytes.Length >= 2)
            {
                // CC hands control to the named entity's model (006142D5); from here on it is
                // that entity's movement that is the player's, whoever was moving before.
                cursor.Control = opcode.Bytes[1];
            }

            if (FieldScriptProgram.ChangesParty(opcode))
            {
                // Slot 0 is whoever the change leaves there. PRTYE names the new party, so its
                // first member settles it when an entity here is bound to that character; any
                // other change can leave anybody there.
                cursor.ChangeParty(NewLeaders(program, opcode));
                if (FieldScriptProgram.HandsControlToTheLeader(opcode))
                {
                    cursor.Control = ControlFollowsLeader;
                }
            }

            switch (opcode.Id)
            {
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
                case 0xA5 when HasConstantMovementArguments(opcode.Bytes) && opcode.Bytes.Length >= 11 &&
                               MovementOwner(cursor) is { } placedOwner:
                    cursor.AddAction(NavigationAction.PlacedMove(
                        groupIndex,
                        script,
                        BitConverter.ToInt16(opcode.Bytes, 3),
                        BitConverter.ToInt16(opcode.Bytes, 5),
                        BitConverter.ToInt16(opcode.Bytes, 7),
                        BitConverter.ToUInt16(opcode.Bytes, 9)) with { LeaderMask = placedOwner });
                    break;
                case 0xC0 when HasConstantMovementArguments(opcode.Bytes) && MovementOwner(cursor) is { } jumpOwner:
                    cursor.AddAction(NavigationAction.Jump(
                        groupIndex,
                        script,
                        BitConverter.ToInt16(opcode.Bytes, 3),
                        BitConverter.ToInt16(opcode.Bytes, 5),
                        BitConverter.ToUInt16(opcode.Bytes, 7)) with { LeaderMask = jumpOwner });
                    break;
                case 0xC2 when HasConstantMovementArguments(opcode.Bytes) && MovementOwner(cursor) is { } ladderOwner:
                    cursor.AddAction(NavigationAction.Ladder(
                        groupIndex,
                        script,
                        BitConverter.ToInt16(opcode.Bytes, 3),
                        BitConverter.ToInt16(opcode.Bytes, 5),
                        BitConverter.ToInt16(opcode.Bytes, 7),
                        BitConverter.ToUInt16(opcode.Bytes, 9),
                        ResolveLadderInput(opcode.Bytes[11])) with { LeaderMask = ladderOwner });
                    break;
            }

            if (LeaderTest(program, opcode, cursor) is { } leaderTest &&
                FieldScriptProgram.FalseTarget(opcode) is { } leaderFalseTarget)
            {
                // A test of who leads: each side goes on with the members it leaves in slot 0,
                // and a side no member of the field can be on is not a way through.
                if (leaderTest.Fails is { } failing)
                {
                    pending.Push(cursor.Fork(leaderFalseTarget, partyMask: failing));
                }

                if (leaderTest.Holds is { } holding)
                {
                    cursor.Narrow(holding);
                    pending.Push(cursor.Advance(nextOffset));
                }

                continue;
            }

            // A test is decided only by a value the walk can trust; anything else forks.
            var holds = ResolveCondition(opcode, cursor.Constants);
            walk.ApplyWrites(opcode, cursor.Constants);
            program.Successors(opcode, successors);
            if (holds is { } known && FieldScriptProgram.FalseTarget(opcode) is { } falseTarget)
            {
                var target = known ? nextOffset : falseTarget;
                if (successors.Contains(target))
                {
                    pending.Push(cursor.Advance(target));
                }
                else
                {
                    results.Add(cursor.End(PathEnd.Invalid));
                }

                continue;
            }

            if (successors.Count == 0)
            {
                // Every way on leaves the code.
                results.Add(cursor.End(PathEnd.Invalid));
                continue;
            }

            // The fall-through side is walked first, as it always was.
            for (var index = successors.Count - 1; index >= 1; index--)
            {
                pending.Push(cursor.Fork(successors[index]));
            }

            pending.Push(cursor.Advance(successors[0]));
        }

        return results.Count == 0
            ? [NavigationExecutionPath.Empty(initialConstants) with { End = PathEnd.Limited, Control = initialControl }]
            : results.Paths;

        // Whose movement a movement opcode run here is (JUMP 00615CA3, LADER and XYZI move only
        // the running entity's own model, and nothing when it has none): 0 when it is the
        // player's whoever was controlled before, the entity's bit when it is the player's only
        // while that entity's model is the controlled one, and null when it can never be the
        // player's. After CC named this entity, or while control follows slot 0 and this entity
        // is certainly there, it is the player's; otherwise it is the player's only if this
        // entity's model can ever be controlled (FieldScriptProgram.IsControllable), and then
        // only while it is. Which movements the player ends up having made is settled once the
        // whole routine is known (OwnedByTheEndOfTheRoutine): control can still change after it.
        ulong? MovementOwner(NavigationExecutionCursor cursor)
        {
            switch (cursor.Control)
            {
                case >= 0 and var controlled when controlled == groupIndex:
                    return 0;
                case ControlFollowsLeader when program.IsPartyCharacter(groupIndex) &&
                                               MoverBit(groupIndex) is not 0 and var own &&
                                               cursor.PartyMask == own:
                    return 0;
                default:
                    return program.IsControllable(groupIndex) ? Tag() : null;
            }

            ulong? Tag()
            {
                if (MoverBit(groupIndex) is not 0 and var bit)
                {
                    return bit;
                }

                // An entity past the 64 a mask can name cannot be told apart from anybody else,
                // so its movement is not offered as anybody's; the field says how often.
                walk.Limits.UnsupportedEntities++;
                return null;
            }
        }

        // The caller after a request: once for each way through the requested script that
        // agrees with who this way through says is leading. A caller that waits goes on only
        // after a way through that finishes, with the values it left and whoever it left in
        // control; one that does not wait goes on at once with its own. With no way through
        // that agrees, it goes on with the values unmatched gives.
        void GoOnAfter(
            NavigationExecutionCursor caller,
            IReadOnlyList<NavigationExecutionPath> called,
            bool waits,
            int next,
            Func<IReadOnlyDictionary<BankByteAddress, byte>> unmatched)
        {
            var went = false;
            foreach (var calledPath in called)
            {
                // A party the callee changed is a new party, and what the callee then knows of
                // slot 0 is about that one: a caller that waits goes on in it, and one that does
                // not keeps what it knew, since the change can come before or after its next
                // step. Only an unchanged party's slot 0 is the same person on both sides.
                var mask = calledPath.PartyChanged
                    ? waits ? calledPath.PartyMask : caller.PartyMask
                    : caller.PartyMask & calledPath.PartyMask;
                if (mask == 0)
                {
                    continue;
                }

                went = true;
                var control = waits ? calledPath.Control : caller.Control;
                var partyChanged = caller.PartyChanged || (waits && calledPath.PartyChanged);
                if (waits && !Finishes(calledPath.End))
                {
                    var actions = new List<NavigationAction>(caller.Actions.Count + calledPath.Actions.Count);
                    actions.AddRange(caller.Actions);
                    actions.AddRange(calledPath.Actions);
                    results.Add(new NavigationExecutionPath(actions, calledPath.Constants, mask, calledPath.End, control, partyChanged));
                    continue;
                }

                pending.Push(caller.Fork(
                    next,
                    new Dictionary<BankByteAddress, byte>(waits ? calledPath.Constants : caller.Constants),
                    calledPath.Actions,
                    mask,
                    control,
                    partyChanged));
            }

            if (!went)
            {
                pending.Push(caller.Fork(next, new Dictionary<BankByteAddress, byte>(unmatched())));
            }
        }
    }

    /// <summary>
    /// A byte test of who is in party slot 0 (IFUB on 3[9], 00DC09E5, against a character) that
    /// no earlier write in the walk decides: the members each side leaves in slot 0, or null for a
    /// side none can be on.
    ///
    /// <para>Slot 0 is a character the field binds with PC, since that is who the player is
    /// moving (0061BCD7, 0061BC67). The "is" side keeps every member bound to that character;
    /// when none is, it is not taken as impossible, and goes on knowing nothing new. The "is not"
    /// side keeps the members bound to some other character, and is impossible only when every
    /// member slot 0 can be on is that character. That is what a chain of tests handing control
    /// back to whoever leads needs: cos_btm's sign CCs back to Cloud, Tifa or Cid, the three
    /// characters it binds, and without this the fourth way on, where none of them leads, kept
    /// the telescope view as the player.</para>
    /// </summary>
    private static (ulong? Holds, ulong? Fails)? LeaderTest(
        FieldScriptProgram program,
        FieldScriptInstruction opcode,
        NavigationExecutionCursor cursor)
    {
        var raw = opcode.Bytes;
        if (opcode.Id is not (0x14 or 0x15) || raw.Length < 6 || (raw[1] & 0x0F) != 0 ||
            FieldBankByte.BlockOf(raw[1] >> 4) != 3 || raw[2] != 9 || raw[4] is not (0 or 1) ||
            cursor.Constants.ContainsKey(new BankByteAddress(3, 9)))
        {
            return null;
        }

        var character = raw[3];
        var equal = 0UL;
        var other = 0UL;
        var domain = 0UL;
        for (var entity = 0; entity < Math.Min(program.EntityCount, 64); entity++)
        {
            var bit = 1UL << entity;
            var characters = program.PartyCharactersOf(entity);
            if ((cursor.PartyMask & bit) == 0 || characters.Count == 0)
            {
                continue;
            }

            domain |= bit;
            if (characters.Contains(character))
            {
                equal |= bit;
            }

            if (characters.Any(bound => bound != character))
            {
                other |= bit;
            }
        }

        if (domain == 0)
        {
            return null;
        }

        ulong? isCharacter = equal != 0 ? equal : cursor.PartyMask;
        ulong? isNot = other != 0 ? other : null;
        return raw[4] == 0 ? (isCharacter, isNot) : (isNot, isCharacter);
    }

    // What a caller tells a script it requests about slot 0: only while control follows the
    // leader does it decide whose movement is the player's, and only then is it passed on.
    private static ulong LeadersPassedOn(NavigationExecutionCursor cursor) =>
        cursor.Control == ControlFollowsLeader ? cursor.PartyMask : AnyLeader;

    // Who can be in party slot 0 after a party change. PRTYE (0061C26A) puts its first operand
    // in slot 0 unless it is FF (filled from the old party) or FE (left empty), so slot 0 is then
    // an entity the field binds to that character; any other change can leave anybody there.
    private static ulong NewLeaders(FieldScriptProgram program, FieldScriptInstruction opcode)
    {
        if (opcode.Id != 0xCA || opcode.Bytes.Length < 2 || opcode.Bytes[1] is 0xFE or 0xFF)
        {
            return AnyLeader;
        }

        var leaders = 0UL;
        for (var entity = 0; entity < program.EntityCount; entity++)
        {
            if (program.PartyCharactersOf(entity).Contains(opcode.Bytes[1]))
            {
                leaders |= PartyBit(entity);
            }
        }

        return leaders == 0 ? AnyLeader : leaders;
    }

    // Whether a caller waiting on a way through goes on after it. The walk's own limits do
    // not say the script never finishes, so they are not treated as if it did not; they are
    // counted instead (NavigationLimits).
    private static bool Finishes(PathEnd end) => end is PathEnd.Returned or PathEnd.Limited;

    // The bit of an entity in a mover mask, or 0 past the 64 a mask holds (not tagged).
    private static ulong MoverBit(int entity) => entity is >= 0 and < 64 ? 1UL << entity : 0;

    // The bit of an entity in a party mask, or every bit past the 64 a mask holds.
    private static ulong PartyBit(int entity) => entity is >= 0 and < 64 ? 1UL << entity : AnyLeader;

    /// <summary>How often the walks of one field stopped at their own limits rather than the engine's.</summary>
    private sealed class NavigationLimits
    {
        public int Count;

        // Of those: a script asked for again while it is still being walked; a script whose
        // ways through outran the walk's step or result budget; one way through that ran longer
        // than MaximumExecutionSteps.
        public int Recursions;
        public int Budgets;
        public int LongPaths;

        // Movements left unoffered because their entity is past the 64 a mover mask can name.
        public int UnsupportedEntities;
    }

    /// <summary>
    /// What the navigation walks of one field may assume about bank values.
    ///
    /// <para>A value is folded only when no script that writes it can run while the walk is
    /// under way. <see cref="FieldScriptProgram.IsConcurrent"/> says which code can: every
    /// Main, every script requested without waiting, and everything those request or hand
    /// over to - because the engine gives each entity up to eight opcodes a pass and switches
    /// on any that yields (0060C94D), so such code can run between any two of the walked
    /// script's opcodes. Init runs to its RET before any of that, so Init-only code cannot.
    /// tunnel_2 is the case that needs this: e8.s4 sets 5[7] to 0 and waits for the scripts
    /// it requested to set it to 1, and folding the 0 hid its exit.</para>
    ///
    /// <para>Every native bank writer (the direct calls to 0060FA7D and 0061031E) ends a
    /// folded value it overwrites, and one whose byte is computed (SETX) ends every folded
    /// value in the blocks it can reach; only SETBYTE from an immediate sets one.</para>
    /// </summary>
    private sealed class NavigationWalk
    {
        private readonly IReadOnlyList<ScriptGroup> groups;
        private readonly Dictionary<BankByteAddress, bool> volatility = new();
        private readonly Dictionary<(int Entity, int Offset), (HashSet<BankByteAddress> Bytes, HashSet<int> Blocks)> laterWrites = new();
        private readonly Dictionary<int, (HashSet<BankByteAddress> Bytes, HashSet<int> Blocks)> partyWrites = new();
        private readonly List<FieldBankByte> scratch = [];

        public NavigationWalk(IReadOnlyList<ScriptGroup> groups, NavigationLimits limits, int fieldId)
        {
            this.groups = groups;
            Program = groups[0].Program;
            Limits = limits;
            FieldId = fieldId;
        }

        public FieldScriptProgram Program { get; }

        /// <summary>The field being walked, or -1 when the caller did not say.</summary>
        public int FieldId { get; }

        public NavigationLimits Limits { get; }

        /// <summary>Walks of requested scripts and party requests already made on this walk.</summary>
        public Dictionary<string, IReadOnlyList<NavigationExecutionPath>> Memo { get; } = new(StringComparer.Ordinal);

        /// <summary>Whether a value can change without this walk seeing the write.</summary>
        public bool IsVolatile(BankByteAddress address)
        {
            if (!volatility.TryGetValue(address, out var result))
            {
                result = (Program.WritersByByte().TryGetValue(new FieldBankByte(address.Bank, address.Index), out var writers) &&
                          writers.Any(IsConcurrent)) ||
                         Program.UnnamedWriters(address.Bank).Any(IsConcurrent);
                volatility[address] = result;
            }

            return result;
        }

        private bool IsConcurrent(int offset) => Program.IsConcurrent(offset);

        /// <summary>Applies an instruction's writes to the values the walk has folded.</summary>
        public void ApplyWrites(FieldScriptInstruction instruction, Dictionary<BankByteAddress, byte> constants)
        {
            if (constants.Count != 0)
            {
                FieldScriptProgram.Writes(instruction, scratch, out _);
                foreach (var written in scratch)
                {
                    constants.Remove(new BankByteAddress(written.Block, written.Address));
                }

                foreach (var block in FieldScriptProgram.UnnamedWriteBlocks(instruction))
                {
                    foreach (var key in constants.Keys.Where(key => key.Bank == block).ToArray())
                    {
                        constants.Remove(key);
                    }
                }
            }

            var bytes = instruction.Bytes;
            if (instruction.Id == SetByteOpcode && bytes.Length >= 4 && (bytes[1] & 0x0F) == 0)
            {
                var destination = new BankByteAddress(FieldBankByte.BlockOf(bytes[1] >> 4), bytes[2]);
                if (destination.Bank != 0 && !IsVolatile(destination))
                {
                    constants[destination] = bytes[3];
                }
            }
        }

        /// <summary>
        /// The caller's values a script it requests without waiting can rely on: those the
        /// caller does not go on to change from <paramref name="next"/>.
        /// </summary>
        public Dictionary<BankByteAddress, byte> WithoutLaterWrites(
            IReadOnlyDictionary<BankByteAddress, byte> constants,
            int entity,
            int next)
        {
            if (constants.Count == 0)
            {
                return new Dictionary<BankByteAddress, byte>();
            }

            if (!laterWrites.TryGetValue((entity, next), out var written))
            {
                written = WritesInOrderFrom([(entity, next)]);
                laterWrites[(entity, next)] = written;
            }

            return Without(constants, written);
        }

        /// <summary>The folded values no playable character's script <paramref name="slot"/> writes.</summary>
        public Dictionary<BankByteAddress, byte> WithoutPartyWrites(
            IReadOnlyDictionary<BankByteAddress, byte> constants,
            int slot)
        {
            if (constants.Count == 0)
            {
                return new Dictionary<BankByteAddress, byte>();
            }

            if (!partyWrites.TryGetValue(slot, out var written))
            {
                var members = new List<(int Entity, int Entry)>();
                for (var group = 0; group < groups.Count; group++)
                {
                    if (IsPlayableCharacterGroup(groups[group]) && Program.Pointer(group, slot) is { } member)
                    {
                        members.Add((group, member));
                    }
                }

                written = WritesInOrderFrom(members);
                partyWrites[slot] = written;
            }

            return Without(constants, written);
        }

        private static Dictionary<BankByteAddress, byte> Without(
            IReadOnlyDictionary<BankByteAddress, byte> constants,
            (HashSet<BankByteAddress> Bytes, HashSet<int> Blocks) written)
        {
            var result = new Dictionary<BankByteAddress, byte>(constants);
            foreach (var key in constants.Keys)
            {
                if (written.Bytes.Contains(key) || written.Blocks.Contains(key.Bank))
                {
                    result.Remove(key);
                }
            }

            return result;
        }

        private (HashSet<BankByteAddress> Bytes, HashSet<int> Blocks) WritesInOrderFrom(
            IEnumerable<(int Entity, int Entry)> starts)
        {
            var bytes = new HashSet<BankByteAddress>();
            var blocks = new HashSet<int>();
            foreach (var offset in InOrderFrom(starts))
            {
                if (!Program.TryGetInstruction(offset, out var instruction))
                {
                    continue;
                }

                FieldScriptProgram.Writes(instruction, scratch, out _);
                foreach (var key in scratch)
                {
                    bytes.Add(new BankByteAddress(key.Block, key.Address));
                }

                blocks.UnionWith(FieldScriptProgram.UnnamedWriteBlocks(instruction));
            }

            return (bytes, blocks);
        }

        /// <summary>
        /// Offsets that run in order from the starts: control flow, RETTO's slot and the
        /// scripts requested with REQEW and PRQEW.
        /// </summary>
        private HashSet<int> InOrderFrom(IEnumerable<(int Entity, int Entry)> starts)
        {
            var offsets = new HashSet<int>();
            var seen = new HashSet<(int Entity, int Offset)>();
            var pending = new Stack<(int Entity, int Offset)>(starts);
            var successors = new List<int>(2);
            while (pending.Count != 0)
            {
                var (current, offset) = pending.Pop();
                if (!seen.Add((current, offset)) || !Program.TryGetInstruction(offset, out var instruction))
                {
                    continue;
                }

                offsets.Add(offset);
                var bytes = instruction.Bytes;
                switch (instruction.Id)
                {
                    case EntityRequestWaitOpcode when bytes.Length >= 3 &&
                                                     Program.Pointer(bytes[1], bytes[2] & 0x1F) is { } waitedFor:
                        pending.Push((bytes[1], waitedFor));
                        break;
                    case PartyMemberRequestSyncOpcode when bytes.Length >= 3:
                        for (var group = 0; group < groups.Count; group++)
                        {
                            if (IsPlayableCharacterGroup(groups[group]) &&
                                Program.Pointer(group, bytes[2] & 0x1F) is { } member)
                            {
                                pending.Push((group, member));
                            }
                        }

                        break;
                    case ReturnToOpcode when bytes.Length >= 2 &&
                                            Program.Pointer(current, bytes[1] & 0x1F) is { } transfer:
                        pending.Push((current, transfer));
                        break;
                }

                Program.Successors(instruction, successors);
                foreach (var successor in successors)
                {
                    pending.Push((current, successor));
                }
            }

            return offsets;
        }
    }

    /// <summary>
    /// A way through collapsed once for each playable character whose model it moves, and
    /// once for anyone else. A named character's movement is the player's only while that
    /// character's model is the one being controlled, so each such character's route is its
    /// own movements with those of ordinary models, and anyone else's is the ordinary models'
    /// alone. A landing anyone else reaches is not tagged; one only named characters reach is
    /// tagged with them. A way through that moves no named character collapses exactly as
    /// before.
    /// </summary>
    private static IEnumerable<NavigationAction> CollapseNavigationRoutineByLeader(
        IReadOnlyList<NavigationAction> actions)
    {
        var named = 0UL;
        foreach (var action in actions)
        {
            if (action.Kind is ActionKind.Ladder or ActionKind.Jump)
            {
                named |= action.LeaderMask;
            }
        }

        if (named == 0)
        {
            foreach (var action in CollapseNavigationRoutine(actions))
            {
                yield return action;
            }

            yield break;
        }

        var landings = new Dictionary<NavigationAction, ulong>();
        var landingOrder = new List<NavigationAction>();
        var mapJumps = new List<NavigationAction>();
        void Collect(IReadOnlyList<NavigationAction> route, ulong movers)
        {
            foreach (var action in CollapseNavigationRoutine(route))
            {
                if (action.Kind == ActionKind.MapJump)
                {
                    if (!mapJumps.Contains(action))
                    {
                        mapJumps.Add(action);
                    }

                    continue;
                }

                var landing = action with { LeaderMask = 0 };
                if (landings.TryGetValue(landing, out var reached))
                {
                    landings[landing] = reached | movers;
                }
                else
                {
                    landings[landing] = movers;
                    landingOrder.Add(landing);
                }
            }
        }

        for (var remaining = named; remaining != 0; remaining &= remaining - 1)
        {
            var mover = remaining & (~remaining + 1);
            Collect(
                actions
                    .Where(action => action.Kind == ActionKind.MapJump ||
                                     action.LeaderMask == 0 ||
                                     (action.LeaderMask & mover) != 0)
                    .ToArray(),
                mover);
        }

        var ordinary = actions.Where(action => action.LeaderMask == 0).ToArray();
        if (ordinary.Any(action => action.Kind is ActionKind.Ladder or ActionKind.Jump))
        {
            Collect(ordinary, AnyLeader);
        }

        foreach (var landing in landingOrder)
        {
            var movers = landings[landing];
            yield return movers == AnyLeader ? landing : landing with { LeaderMask = movers & named };
        }

        foreach (var mapJump in mapJumps)
        {
            yield return mapJump;
        }
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

    /// <summary>
    /// Whether a byte test is decided by a value the walk has folded: IFUB or IFUBL whose
    /// left operand is a folded bank byte and whose right operand is an immediate. Anything
    /// else is unknown, and the walk takes both sides.
    /// </summary>
    private static bool? ResolveCondition(
        FieldScriptInstruction opcode,
        IReadOnlyDictionary<BankByteAddress, byte> constants)
    {
        if (opcode.Id is not (0x14 or 0x15) || opcode.Bytes.Length < 6)
        {
            return null;
        }

        var bank = opcode.Bytes[1] >> 4;
        var sourceBank = opcode.Bytes[1] & 0x0F;
        if (sourceBank != 0 ||
            !constants.TryGetValue(new BankByteAddress(FieldBankByte.BlockOf(bank), opcode.Bytes[2]), out var actual))
        {
            return null;
        }

        var expected = opcode.Bytes[3];
        return opcode.Bytes[4] switch
        {
            0 => actual == expected,
            1 => actual != expected,
            2 => actual > expected,
            3 => actual < expected,
            4 => actual >= expected,
            5 => actual <= expected,
            _ => null
        };
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
         scriptId == 1) ||

        // blin60_2: ELINEL and ELINER are the 60th floor's elevator doors. Their [OK]
        // plays the elevator sound, waits for the doors (REQEW) and MAPJUMPs to eleout, as
        // floor 59's do; the walk lines only lead back to the guards' hall.
        (fieldId == 240 &&
         entityId is 12 or 13 &&
         scriptId == 1) ||

        // blin63_1: DUCTLB and DUCTLC are the air-duct openings. [OK] asks "An air-duct..."
        // and on "Climb in and look around" MAPJUMPs into the duct crawl (blin63_t);
        // "Leave it alone" returns. Nothing else in the field leads there.
        (fieldId == 245 &&
         entityId is 46 or 47 &&
         scriptId == 1);

    /// <summary>
    /// Whether <paramref name="opcode"/> is a byte comparison, and where it goes when the
    /// test fails. The engine measures a conditional's jump from the operand's own position,
    /// not from the end of the opcode: 6116A6 and 61171F both add the operand to the address
    /// of the byte it was read from, which is index 5 in a byte comparison whether the
    /// operand is one byte wide or two.
    /// </summary>
    private static bool TryResolveConditionalBranch(
        FieldScriptInstruction opcode,
        out int falseTarget)
    {
        falseTarget = -1;
        if (opcode.Id == 0x14 && opcode.Bytes.Length >= 6)
        {
            falseTarget = opcode.Offset + ByteComparisonOperandIndex + opcode.Bytes[5];
            return true;
        }

        if (opcode.Id == 0x15 && opcode.Bytes.Length >= 7)
        {
            falseTarget = opcode.Offset + ByteComparisonOperandIndex +
                BitConverter.ToUInt16(opcode.Bytes, 5);
            return true;
        }

        return TryResolveComparisonBranch(opcode, out falseTarget);
    }

    /// <summary>
    /// An unconditional jump's target. JMPF and JMPFL both measure from their operand
    /// (JMPFL 00613141 is IP + word + 1); JMPB and JMPBL subtract from the opcode.
    /// </summary>
    private static bool TryResolveUnconditionalBranch(FieldScriptInstruction opcode, out int target)
    {
        target = -1;
        switch (opcode.Id)
        {
            case 0x10 when opcode.Bytes.Length >= 2:
                target = opcode.Offset + opcode.Bytes[1] + 1;
                return true;
            case 0x11 when opcode.Bytes.Length >= 3:
                target = opcode.Offset + BitConverter.ToUInt16(opcode.Bytes, 1) + 1;
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
        var nativeLadders = LiveLadders(groups);
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
                reverseAction.RequiredInput,
                MoverEntityIds: MoverEntities(reverseAction.LeaderMask),
                Conditions: TraversalConditions(groups[0].Program, reverseAction)));
        }
    }

    /// <summary>
    /// Every LADER the engine can run, once each, named by the script whose range holds it
    /// (the name stable transition ids have always used). A LADER nothing starts
    /// (<see cref="FieldScriptProgram.IsEventReachable"/>) is not a way down anybody takes, and
    /// neither is one in the script of an entity whose model can never be the controlled one
    /// (<see cref="FieldScriptProgram.IsControllable"/>) or one past the 64 entities a mover
    /// mask can name.
    /// </summary>
    private static IReadOnlyList<NavigationAction> LiveLadders(IReadOnlyList<ScriptGroup> groups)
    {
        if (groups.Count == 0)
        {
            return [];
        }

        var program = groups[0].Program;
        var owners = new List<(int Start, int End, int Group, int Script)>();
        foreach (var group in groups)
        {
            foreach (var (slot, start) in group.ScriptStarts)
            {
                owners.Add((start, start + group.Scripts[slot].Length, group.Index, slot));
            }
        }

        var ladders = new List<NavigationAction>();
        var seen = new HashSet<int>();
        foreach (var opcode in program.LiveInstructions())
        {
            if (opcode.Id != 0xC2 || !HasConstantMovementArguments(opcode.Bytes) || !seen.Add(opcode.Offset) ||
                !program.IsEventReachable(opcode.Offset))
            {
                continue;
            }

            var owner = owners.FirstOrDefault(range => opcode.Offset >= range.Start && opcode.Offset < range.End);
            if (owner.End == 0 || !program.IsControllable(owner.Group) || MoverBit(owner.Group) == 0)
            {
                continue;
            }

            ladders.Add(NavigationAction.Ladder(
                owner.Group,
                owner.Script,
                BitConverter.ToInt16(opcode.Bytes, 3),
                BitConverter.ToInt16(opcode.Bytes, 5),
                BitConverter.ToInt16(opcode.Bytes, 7),
                BitConverter.ToUInt16(opcode.Bytes, 9),
                ResolveLadderInput(opcode.Bytes[11])) with { LeaderMask = MoverBit(owner.Group) });
        }

        return ladders
            .OrderBy(ladder => ladder.SourceGroup)
            .ThenBy(ladder => ladder.SourceScript)
            .ToArray();
    }

    /// <summary>
    /// The live guard of each destination a line's routines MAPJUMP to: one alternative per
    /// MAPJUMP that goes there, each the savemap tests that decide whether it runs from the
    /// script it is in (<see cref="FieldScriptProgram.MapJumpGuard"/>). A destination reached by
    /// any MAPJUMP no such test decides is always offered, and is left out.
    /// </summary>
    private static IReadOnlyList<FieldScriptExitGuard> ExitGuards(
        FieldScriptProgram program,
        IEnumerable<NavigationAction> actions,
        int fieldId,
        int lineEntity)
    {
        // Whatever else one crossing of the line can start: its event scripts 1-6 (0060C94D).
        var events = Enumerable.Range(1, LastLineEventScript)
            .Select(slot => program.Pointer(lineEntity, slot))
            .OfType<int>()
            .Distinct()
            .Select(pointer => (lineEntity, pointer))
            .ToArray();
        var eventSlots = Enumerable.Range(1, LastLineEventScript)
            .Select(slot => (Slot: slot, Pointer: program.Pointer(lineEntity, slot)))
            .Where(entry => entry.Pointer is not null)
            .Select(entry => (entry.Slot, Pointer: entry.Pointer!.Value))
            .DistinctBy(entry => entry.Pointer)
            .ToArray();
        var guards = new List<FieldScriptExitGuard>();
        foreach (var byDestination in actions
                     .Where(action => action.Kind == ActionKind.MapJump && action.DestinationField != fieldId)
                     .GroupBy(action => action.DestinationField)
                     .OrderBy(group => group.Key))
        {
            var alternatives = new List<IReadOnlyList<FieldScriptGuardTest>>();
            var always = false;
            foreach (var site in byDestination.DistinctBy(action => (action.SourceGroup, action.SourceScript, action.At)))
            {
                // The script the MAPJUMP is in is not its own sibling: what it writes after the test
                // cannot change the test.
                var own = program.Pointer(site.SourceGroup, site.SourceScript);
                var fromTheLine = site.SourceGroup == lineEntity && site.SourceScript <= LastLineEventScript;
                IReadOnlyList<IReadOnlyList<FieldScriptGuardTest>>? ways = site.At < 0
                    ? null
                    : fromTheLine
                        ? program.MapJumpGuard(site.SourceGroup, site.SourceScript, site.At,
                            events.Where(start => start.pointer != own).ToArray(), startedByTheDispatcher: true) is { } direct
                            ? [direct]
                            : null
                        // A MAPJUMP in a script the event asks for is also held by the event's own
                        // tests on the way to the request: tunnel_4's ladder line tests 15[131] bit 6
                        // and only then REQs the script that climbs to md0.
                        : program.RequestedMapJumpGuards(lineEntity, eventSlots, site.SourceGroup, site.SourceScript, site.At) ??
                          (program.MapJumpGuard(site.SourceGroup, site.SourceScript, site.At,
                              events.Where(start => start.pointer != own).ToArray()) is { } callee
                              ? [callee]
                              : null);
                if (ways is null || ways.Any(way => way.Count == 0))
                {
                    always = true;
                    break;
                }

                foreach (var guard in ways)
                {
                    if (!alternatives.Any(existing => existing.Select(test => test.Key).SequenceEqual(guard.Select(test => test.Key))))
                    {
                        alternatives.Add(guard);
                    }
                }
            }

            // Two ways that each need one test, on either side of it, leave the field whatever the
            // byte holds (gaiin_5's bat1 runs its battle only while 1[131] bit 2 is clear, and
            // REQEWs the same jump both ways): no guard.
            always |= alternatives.Any(first => first.Count == 1 && alternatives.Any(second =>
                second.Count == 1 && second[0] with { Holds = !second[0].Holds } == first[0]));
            if (!always && alternatives.Count > 0)
            {
                guards.Add(new FieldScriptExitGuard(byDestination.Key, alternatives));
            }
        }

        return guards;
    }

    /// <summary>
    /// The ways a landing is the player's. Each entity whose model it moves is one way, needing
    /// that model controlled; all of them need whoever the routine's path leaves in slot 0. A
    /// movement slot 0's own copy made (<see cref="NavigationAction.MovesSlotZero"/>) is that
    /// member's only while it leads, so its way needs one of its own characters in slot 0 too.
    /// </summary>
    private static IReadOnlyList<FieldScriptNavigationCondition>? TraversalConditions(
        FieldScriptProgram program,
        NavigationAction action)
    {
        var leaders = LeaderCharacters(program, action.SlotZero);
        if (MoverEntities(action.LeaderMask) is not { } movers)
        {
            return leaders is null ? null : [new FieldScriptNavigationCondition(null, leaders)];
        }

        var ways = new List<FieldScriptNavigationCondition>(movers.Count);
        foreach (var mover in movers)
        {
            IReadOnlyList<int>? own = leaders;
            if (action.MovesSlotZero)
            {
                var bound = program.PartyCharactersOf(mover);
                own = (leaders is null ? bound : bound.Intersect(leaders)).Order().ToArray();
                if (own.Count == 0)
                {
                    // This member cannot be slot 0 on the way the routine went.
                    continue;
                }
            }

            ways.Add(new FieldScriptNavigationCondition(mover, own));
        }

        return ways;
    }

    // The characters the entities in a slot-0 mask are bound to; null when any may lead.
    private static IReadOnlyList<int>? LeaderCharacters(FieldScriptProgram program, ulong slotZero)
    {
        if (slotZero == AnyLeader)
        {
            return null;
        }

        var characters = new SortedSet<int>();
        for (var entity = 0; entity < Math.Min(program.EntityCount, 64); entity++)
        {
            if ((slotZero & (1UL << entity)) != 0)
            {
                characters.UnionWith(program.PartyCharactersOf(entity));
            }
        }

        return characters.Count == 0 ? null : characters.ToArray();
    }

    private static IReadOnlyList<int>? MoverEntities(ulong mask)
    {
        if (mask is 0 or AnyLeader)
        {
            return null;
        }

        var entities = new List<int>();
        for (var entity = 0; entity < 64; entity++)
        {
            if ((mask & (1UL << entity)) != 0)
            {
                entities.Add(entity);
            }
        }

        return entities;
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

        var transitions = new List<FieldScriptNavigationTransition>();
        foreach (var group in groups)
        {
            foreach (var (pointer, slots) in group.Program.DistinctEntries(group.Index))
            {
                // Only a poll the engine actually runs: a Main, or a script something starts.
                // A private slot nothing requests can hold the very same loop and never run it.
                if (!group.Program.IsEventReachable(pointer))
                {
                    continue;
                }

                var leaderTriangleAddress = -1;
                var pendingTriangle = -1;
                var pendingTriangleEnd = int.MaxValue;
                var sawConfirm = false;
                foreach (var opcode in group.Script(slots[0]))
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
                                SourceTriangle: pendingTriangle,
                                // PXYZI and PRQEW both name party slot 0, so the climb moves
                                // whichever family member leads, and is the player's only while
                                // that member's model is the controlled one.
                                MoverEntityIds: climb.Members,
                                // Each member climbs only as slot 0's actor: its own model has to be
                                // the controlled one while its own character leads.
                                Conditions: climb.Members
                                    .Select(member => new FieldScriptNavigationCondition(
                                        member, group.Program.PartyCharactersOf(member)))
                                    .ToArray()));
                        }

                        sawConfirm = false;
                        continue;
                    }

                    // Anything else that can move control elsewhere ends the press. The
                    // triangle test is the enclosing condition and stands until the next
                    // one or a return; the confirm press has to be the thing immediately
                    // before the request, so a jump or another test in between drops it.
                    if (TryResolveUnconditionalBranch(opcode, out _) ||
                        TryResolveConditionalBranch(opcode, out _) ||
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
    private static Dictionary<int, (NavigationAction Ladder, int FootX, int FootY, IReadOnlyList<int> Members)> ReadLeaderFamilyClimbs(
        IReadOnlyList<ScriptGroup> groups)
    {
        var candidates = new Dictionary<int, List<(NavigationAction Ladder, int FootX, int FootY)>>();
        foreach (var group in groups)
        {
            if (!IsPlayableCharacterGroup(group))
            {
                continue;
            }

            // Every slot, aliases included: a party request names a slot number, and a slot
            // that shares a climb script's pointer runs that climb.
            for (var slot = 1; slot < FieldScriptProgram.SlotCount; slot++)
            {
                if (!group.HasSlot(slot) ||
                    !TryReadPolledLadderScript(group.Index, slot, group.Script(slot), out var climb))
                {
                    continue;
                }

                if (!candidates.TryGetValue(slot, out var found))
                {
                    found = [];
                    candidates[slot] = found;
                }

                found.Add(climb);
            }
        }

        var agreed = new Dictionary<int, (NavigationAction Ladder, int FootX, int FootY, IReadOnlyList<int> Members)>();
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
                agreed[candidate.Key] = (first.Ladder, first.FootX, first.FootY,
                    candidate.Value.Select(entry => entry.Ladder.SourceGroup).Distinct().Order().ToArray());
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
    private static bool TryResolveComparisonBranch(FieldScriptInstruction opcode, out int falseTarget)
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
        group.Program.IsPartyCharacter(group.Index);

    // PXYZI reads a party member's own position into four bank words. Only slot 0 - the
    // character the player is actually moving - is a leader position, and the fourth
    // word is the triangle. AXYZI looks similar and is not this: it reads an arbitrary
    // entity, and in the Cave of the Gi its Y lands in the very word this used to
    // assume was a triangle.
    private static int ReadLeaderTriangleAddress(FieldScriptInstruction opcode)
    {
        if (opcode.Bytes.Length < 8 ||
            opcode.Bytes[3] != LeaderPartySlot ||
            (opcode.Bytes[2] & 0x0F) != LeaderPositionBank)
        {
            return -1;
        }

        return opcode.Bytes[7];
    }

    private static bool IsLeaderTriangleTest(FieldScriptInstruction opcode, int leaderTriangleAddress) =>
        opcode.Bytes.Length >= 8 &&
        (opcode.Bytes[1] & 0xF0) == LeaderPositionBank << 4 &&
        (opcode.Bytes[1] & 0x0F) == 0x00 &&
        BitConverter.ToUInt16(opcode.Bytes, 2) == leaderTriangleAddress &&
        opcode.Bytes[6] == 0;

    private static bool IsConfirmKeyTest(FieldScriptInstruction opcode) =>
        opcode.Bytes.Length >= 3 &&
        BitConverter.ToUInt16(opcode.Bytes, 1) == NativeConfirmKeyMask;

    private static bool TryReadPolledLadderScript(
        int groupIndex,
        int scriptIndex,
        IReadOnlyList<FieldScriptInstruction> script,
        out (NavigationAction Ladder, int FootX, int FootY) climb)
    {
        climb = default;
        var footX = 0;
        var footY = 0;
        var haveFoot = false;
        foreach (var opcode in script)
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

    // A mask that holds every entity: whoever is in slot 0, or a movement anybody makes.
    private const ulong AnyLeader = ulong.MaxValue;

    // Who the player controls on a way through. The walk starts with whoever the player was
    // controlling when they set it off, which only the running game knows: a movement is then
    // tagged with its entity and offered while that entity's model is the controlled one. CC
    // (006142D5) names an entity instead; PRTYP, PRTYM and PRTYE hand control to whoever is
    // in party slot 0 (0061BC67); two ways through that meet with different answers leave it
    // unknown, which is treated as the trigger's.
    private const int ControlAtTrigger = -1;
    private const int ControlFollowsLeader = -2;
    private const int ControlUnknown = -3;
    private const byte ChangeControlOpcode = 0xBF;
    private const byte EntityRequestWaitOpcode = 0x03;
    private const byte ReturnToOpcode = 0x07;
    private const byte SetByteOpcode = 0x80;
    private const byte MapJumpOpcode = 0x60;
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

    private static bool RequiresActionActivation(IReadOnlyList<FieldScriptInstruction> script) =>
        script.Any(opcode =>
            (opcode.Id is 0x30 or 0x31) &&
            opcode.Bytes.Length >= 3 &&
            (BitConverter.ToUInt16(opcode.Bytes, 1) & 0x20) != 0);

    private static bool TryReadLine(IReadOnlyList<FieldScriptInstruction> script, out LineDefinition line)
    {
        foreach (var opcode in script)
        {
            if (opcode.Id != 0xD0 || opcode.Bytes.Length < 13)
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

    // The engine's lengths, SPECIAL's sub-opcodes included (0061E78C); the listing reads
    // each script's range with them.
    private static int GetOpcodeLength(byte[] script, int offset) =>
        FieldScriptProgram.Length(script, offset, script.Length);

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

    /// <summary>How a way through a script ends.</summary>
    private enum PathEnd
    {
        // RET: the script finished.
        Returned,
        // MAPJUMP to another field: the script waits there until the field changes.
        LeftField,
        // It never finishes: GAMEOVER, a handler PC does not implement, or back at the same
        // place with the same known values.
        Stuck,
        // It leaves the code: RETTO to a slot with no code, or a jump or fall-through past
        // the end of the script section's code.
        Invalid,
        // The walk's own step, path or call limits rather than anything the engine does.
        Limited
    }

    private sealed record NavigationExecutionPath(
        IReadOnlyList<NavigationAction> Actions,
        IReadOnlyDictionary<BankByteAddress, byte> Constants,
        // Which entities can be in party slot 0 on this way through; see PartyBit.
        ulong PartyMask = AnyLeader,
        PathEnd End = PathEnd.Returned,
        // Who the player controls where the way through ends; see ControlAtTrigger.
        int Control = ControlAtTrigger,
        // Whether the party changed on the way, so that PartyMask is about the new party.
        bool PartyChanged = false)
    {
        /// <summary>What the way through does, who can be leading on it and how it ends.</summary>
        public string ActionKey =>
            string.Join(
                ";",
                Actions.Select(action =>
                    $"{action.Kind}:{action.SourceGroup}:{action.SourceScript}:{action.X}:{action.Y}:" +
                    $"{action.Z}:{action.Triangle}:{action.DestinationField}:{action.RequiredInput}:" +
                    $"{action.RequiresActionActivation}:{action.IsPlacement}:{action.LeaderMask}:{action.MovesSlotZero}:{action.At}")) +
            $"#{PartyMask}#{End}#{Control}#{PartyChanged}";

        public static NavigationExecutionPath Empty(
            IReadOnlyDictionary<BankByteAddress, byte> constants) =>
            new(
                Array.Empty<NavigationAction>(),
                new Dictionary<BankByteAddress, byte>(constants));
    }

    /// <summary>
    /// One way through being walked. A cursor belongs to one pending entry at a time: going
    /// on to the next instruction reuses it, and only a fork copies it.
    /// </summary>
    /// <summary>What a way through went on with at a join point it passed.</summary>
    private readonly record struct JoinState(
        IReadOnlyDictionary<BankByteAddress, byte> Constants,
        ulong PartyMask,
        int Control,
        ulong Hash);

    private sealed class NavigationExecutionCursor
    {
        private ulong actionsHash;
        private ulong seenHash;

        public NavigationExecutionCursor(
            int offset,
            Dictionary<BankByteAddress, byte> constants,
            List<NavigationAction> actions,
            ImmutableDictionary<int, JoinState> seen,
            int steps,
            ulong partyMask,
            int control = ControlAtTrigger,
            bool partyChanged = false)
        {
            Offset = offset;
            Constants = constants;
            Actions = actions;
            Seen = seen;
            Steps = steps;
            PartyMask = partyMask;
            Control = control;
            PartyChanged = partyChanged;
            foreach (var action in actions)
            {
                actionsHash = RollAction(actionsHash, action);
            }
        }

        public int Offset { get; private set; }

        public Dictionary<BankByteAddress, byte> Constants { get; }

        public List<NavigationAction> Actions { get; }

        // What the walk went on with at each join point this way through has passed. Forks
        // share it until one of them changes it.
        public ImmutableDictionary<int, JoinState> Seen { get; private set; }

        public int Steps { get; private set; }

        public ulong PartyMask { get; private set; }

        /// <summary>Who the player controls here; see <see cref="ControlAtTrigger"/>.</summary>
        public int Control { get; set; }

        public bool PartyChanged { get; private set; }

        public NavigationExecutionCursor Advance(int offset)
        {
            Offset = offset;
            Steps++;
            return this;
        }

        public void AddAction(NavigationAction action)
        {
            Actions.Add(action);
            actionsHash = RollAction(actionsHash, action);
        }

        /// <summary>The party changed: from here on slot 0 can be whoever <paramref name="leaders"/> allows.</summary>
        public void ChangeParty(ulong leaders)
        {
            PartyMask = leaders;
            PartyChanged = true;
        }

        /// <summary>A test settled that slot 0 is one of <paramref name="leaders"/>.</summary>
        public void Narrow(ulong leaders) => PartyMask = leaders;

        /// <summary>A copy going on at <paramref name="offset"/>, with <paramref name="appended"/> after its actions.</summary>
        public NavigationExecutionCursor Fork(
            int offset,
            Dictionary<BankByteAddress, byte>? constants = null,
            IReadOnlyList<NavigationAction>? appended = null,
            ulong? partyMask = null,
            int? control = null,
            bool? partyChanged = null)
        {
            var actions = new List<NavigationAction>(Actions.Count + (appended?.Count ?? 0));
            actions.AddRange(Actions);
            var fork = new NavigationExecutionCursor(
                offset,
                constants ?? new Dictionary<BankByteAddress, byte>(Constants),
                actions,
                Seen,
                Steps + 1,
                partyMask ?? PartyMask,
                control ?? Control,
                partyChanged ?? PartyChanged)
            {
                actionsHash = actionsHash,
                seenHash = seenHash
            };
            if (appended is not null)
            {
                foreach (var action in appended)
                {
                    fork.AddAction(action);
                }
            }

            return fork;
        }

        public NavigationExecutionPath End(PathEnd end) => new(Actions, Constants, PartyMask, end, Control, PartyChanged);

        /// <summary>
        /// Arrives at a join point: false when an earlier arrival on this way through already
        /// knew no more than this one, so what follows has been walked; otherwise true, going
        /// on with what every arrival here agrees on, whoever any of them allowed, and control
        /// unknown where they disagree about it.
        /// </summary>
        public bool Arrive()
        {
            if (!Seen.TryGetValue(Offset, out var earlier))
            {
                Remember(new Dictionary<BankByteAddress, byte>(Constants), PartyMask, Control, 0);
                return true;
            }

            var agreed = new Dictionary<BankByteAddress, byte>();
            foreach (var (key, value) in earlier.Constants)
            {
                if (Constants.TryGetValue(key, out var now) && now == value)
                {
                    agreed[key] = value;
                }
            }

            var mask = earlier.PartyMask | PartyMask;
            var control = earlier.Control == Control ? Control : ControlUnknown;
            if (agreed.Count == earlier.Constants.Count && mask == earlier.PartyMask && control == earlier.Control)
            {
                return false;
            }

            Constants.Clear();
            foreach (var (key, value) in agreed)
            {
                Constants[key] = value;
            }

            PartyMask = mask;
            Control = control;
            Remember(agreed, mask, control, earlier.Hash);
            return true;
        }

        /// <summary>Everything that decides what this way through does from here on.</summary>
        public ulong StateHash() =>
            Mix((ulong)Offset) ^ (Mix(PartyMask) * 3) ^ (HashOf(Constants) * 5) ^ (actionsHash * 7) ^ (seenHash * 11) ^
            (Mix((ulong)(uint)Control ^ (PartyChanged ? 1UL << 40 : 0)) * 13);

        private void Remember(IReadOnlyDictionary<BankByteAddress, byte> constants, ulong mask, int control, ulong replaced)
        {
            var hash = Mix((ulong)Offset) ^ HashOf(constants) ^ (Mix(mask) * 3) ^ (Mix((ulong)(uint)control) * 5);
            Seen = Seen.SetItem(Offset, new JoinState(constants, mask, control, hash));
            seenHash = seenHash - replaced + hash;
        }

        private static ulong HashOf(IReadOnlyDictionary<BankByteAddress, byte> constants)
        {
            var hash = 0UL;
            foreach (var (key, value) in constants)
            {
                hash += Mix(((ulong)(uint)key.Bank << 40) ^ ((ulong)(uint)key.Index << 8) ^ value);
            }

            return hash;
        }

        private static ulong RollAction(ulong hash, NavigationAction action)
        {
            var value = Mix((ulong)action.Kind);
            value = Mix(value ^ (uint)action.SourceGroup) ^ Mix(value + (uint)action.SourceScript);
            value = Mix(value ^ (uint)action.X) ^ Mix(value + (uint)action.Y);
            value = Mix(value ^ (ulong)(uint)(action.Z ?? int.MinValue)) ^ Mix(value + (uint)action.Triangle);
            value = Mix(value ^ (uint)action.DestinationField) ^ Mix(value + (uint)action.RequiredInput);
            value = Mix(value ^ (action.RequiresActionActivation ? 1UL : 0UL) ^ (action.IsPlacement ? 2UL : 0UL));
            value = Mix(value ^ action.LeaderMask ^ (action.MovesSlotZero ? 1UL << 63 : 0) ^ ((ulong)(uint)action.At << 20));
            return Mix((hash * 0x100000001B3UL) ^ value);
        }

        private static ulong Mix(ulong value)
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    /// <summary>
    /// One entity. <see cref="Scripts"/> and <see cref="ScriptStarts"/> are the listing's view:
    /// each distinct pointer's range, filed under the lowest slot that uses it.
    /// Everything that asks what the entity does uses <see cref="Program"/> instead, which
    /// runs every slot from its own pointer and follows control flow wherever it goes.
    /// </summary>
    private sealed record ScriptGroup(
        int Index,
        string Name,
        IReadOnlyDictionary<int, byte[]> Scripts,
        FieldScriptProgram Program,
        IReadOnlyDictionary<int, int> ScriptStarts)
    {
        /// <summary>Whether script <paramref name="slot"/> has an entry point in the code.</summary>
        public bool HasSlot(int slot) => Program.Pointer(Index, slot) is not null;

        /// <summary>What script <paramref name="slot"/> runs; for script 0, Init and every Main.</summary>
        public IReadOnlyList<FieldScriptInstruction> Script(int slot) => Program.Script(Index, slot);

        /// <summary>Each distinct entry point other than script 0's, with every slot that uses it.</summary>
        public IEnumerable<(int Pointer, IReadOnlyList<int> Slots)> ScriptEntries() =>
            Program.DistinctEntries(Index)
                .Select(entry => (entry.Pointer, (IReadOnlyList<int>)entry.Slots.Where(slot => slot > 0).ToArray()))
                .Where(entry => entry.Item2.Count > 0);
    }

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
        bool IsPlacement = false,
        // Bit e set for each entity e whose model this movement moves, and so is the player's
        // only while that model is the controlled one; 0 when it is the player's whoever was
        // controlled when the routine was set off.
        ulong LeaderMask = 0,
        // Who has to be in party slot 0 (see PartyBit) for the routine to go the way this
        // came from; set once the routine is collapsed.
        ulong SlotZero = AnyLeader,
        // A movement a party-slot-0 request's own copy made: the mover is slot 0's actor, so it
        // is the player's only while that same member leads.
        bool MovesSlotZero = false,
        // Where a MAPJUMP sits, so its live guard can be read from its script; -1 otherwise.
        int At = -1)
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









