namespace Ff7.Accessibility.Reloaded;

public sealed class FieldNavigationNpcReader
{
    // Ghidra: ff7_en.exe opcode 0x7E (TLKON) writes directly to field_event_data + 0x61.
    public const int TalkDisabledOffset = 0x61;
    public const int CollisionRadiusOffset = 0x72;
    public const int TalkRadiusOffset = 0x74;

    private static readonly IReadOnlyList<FieldNavigationTarget> EmptyTargets =
        Array.Empty<FieldNavigationTarget>();

    private static readonly IReadOnlyDictionary<(int FieldId, int EntityId), string>
        VerifiedLabels = new Dictionary<(int FieldId, int EntityId), string>
        {
            // tin_1: first passenger car. The beggar can give Cloud a
            // Phoenix Down during the Reactor 5 security countdown.
            [(139, 31)] = "Barret",
            [(139, 33)] = "Biggs",
            [(139, 34)] = "Jessie",
            [(139, 35)] = "Wedge",
            [(139, 37)] = "Shinra Manager",
            [(139, 38)] = "Man",
            [(139, 39)] = "Beggar",

            // tin_2: second passenger car. The adult man can give Cloud a
            // Hi-Potion during the Reactor 5 security countdown.
            [(140, 22)] = "Tifa",
            [(140, 27)] = "Boy",
            [(140, 28)] = "Boy",
            [(140, 29)] = "Girl",
            [(140, 30)] = "Man",
            [(140, 31)] = "Shinra employee",

            // tin_3: final passenger car before the party jumps.
            [(141, 14)] = "Barret",
            [(141, 15)] = "Tifa",
            [(141, 17)] = "Wedge",
            [(141, 18)] = "Jessie",

            // tin_4: crowded passenger car. Two passengers can bump into
            // Cloud and take gil; their visible roles do not reveal that.
            [(142, 18)] = "Barret",
            [(142, 19)] = "Tifa",
            [(142, 22)] = "Man",
            [(142, 23)] = "Woman",
            [(142, 25)] = "Johnny",
            [(142, 26)] = "Old man",
            [(142, 27)] = "Man",
            [(142, 28)] = "Man",
            [(142, 29)] = "Man",

            // mds7st3: Sector 7 station.
            [(146, 17)] = "Station attendant",
            [(146, 18)] = "Man",
            [(146, 20)] = "Woman",

            // mds7_w1: weapon shop.
            [(148, 9)] = "Weapon shopkeeper",
            [(148, 10)] = "Boy",
            [(148, 11)] = "Man",

            // mds7_w2: Beginner's Hall above the weapon shop.
            [(149, 9)] = "Beginner's Hall instructor",
            [(149, 10)] = "Man",
            [(149, 11)] = "Dog",
            [(149, 16)] = "Save Point instructor",
            [(149, 17)] = "Boy",
            [(149, 18)] = "Girl",
            [(149, 19)] = "Battle instructor",
            [(149, 20)] = "Battle instructor",
            [(149, 21)] = "Battle instructor",

            // mds7: Sector 7 slums.
            [(151, 22)] = "Barret",
            [(151, 27)] = "Johnny",
            [(151, 28)] = "Man",
            [(151, 29)] = "Woman",
            [(151, 30)] = "Boy",
            [(151, 31)] = "Woman",

            // mds7_im: item and Materia shop.
            [(152, 4)] = "Item shopkeeper",

            // min71: Johnny's home.
            [(153, 12)] = "Johnny",
            [(153, 13)] = "Johnny's father",
            [(153, 14)] = "Man",
            [(153, 15)] = "Johnny's mother",
            [(153, 17)] = "Woman",
            [(153, 18)] = "Shinra employee",

            // mds7pb_1: 7th Heaven bar. Some seated conversations use
            // interactive proxy models, so the same speaker can have two IDs.
            [(154, 19)] = "Barret",
            [(154, 20)] = "Tifa",
            [(154, 22)] = "Jessie",
            [(154, 23)] = "Biggs",
            [(154, 24)] = "Wedge",
            [(154, 25)] = "Marlene",
            [(154, 26)] = "Biggs",
            [(154, 28)] = "Wedge",
            [(154, 29)] = "Jessie",

            // mds7pb_2: AVALANCHE hideout.
            [(155, 12)] = "Barret",
            [(155, 13)] = "Tifa",
            [(155, 15)] = "Wedge",
            [(155, 16)] = "Jessie",
            [(155, 17)] = "Marlene",

            // mds7plr1: road to the Sector 7 pillar.
            [(156, 11)] = "Aerith",
            [(156, 17)] = "Guard",
            [(156, 18)] = "Guard",
            [(156, 19)] = "Man",
            [(156, 20)] = "Man",

            // Sector 5 slums and the market around Aeris's house. Labels were
            // checked against the installed FLEVEL model-loader resources, not
            // inferred from ordinary dialogue fragments.
            [(172, 6)] = "Man",
            [(172, 7)] = "Man",
            [(173, 7)] = "Man",
            [(174, 5)] = "Man",
            [(174, 6)] = "Boy",
            [(175, 4)] = "Man",
            [(175, 5)] = "Boy",
            [(176, 6)] = "Sick man",
            [(177, 10)] = "Man",
            [(177, 11)] = "Woman",
            [(177, 12)] = "Child",
            [(177, 13)] = "Man",
            [(177, 14)] = "Man",
            [(177, 15)] = "Dog",
            [(178, 4)] = "Weapon shopkeeper",
            [(178, 5)] = "Man",
            [(178, 6)] = "Child",
            [(179, 4)] = "Item shopkeeper",
            [(180, 6)] = "Materia shopkeeper",

            // Aeris's house variants reuse the same visible household models
            // while changing script/text sections with story progress.
            [(188, 6)] = "Tifa",
            [(188, 8)] = "Elmyra",
            [(189, 6)] = "Tifa",
            [(189, 8)] = "Elmyra",
            [(190, 6)] = "Marlene",

            // Wall Market's native Talk scripts frequently begin with ordinary
            // dialogue rather than a speaker heading. Every label below was
            // checked against the field's model resource, native interaction
            // script, and the visible role described by the Wall Market guide.
            // Generic pedestrians deliberately stay generic so navigation does
            // not reveal a reward or story role before a sighted player would.
            [(195, 16)] = "Diner promoter",
            [(195, 17)] = "Man",
            [(195, 18)] = "Man",
            [(195, 19)] = "Old man",
            [(195, 20)] = "Man",
            [(195, 21)] = "Man",
            [(195, 22)] = "Boy",

            [(196, 8)] = "Weapon shopkeeper",
            [(196, 9)] = "Weapon shop owner",

            [(197, 8)] = "Bodybuilder",
            [(197, 9)] = "Bodybuilder",
            [(197, 10)] = "Bodybuilder",
            [(197, 11)] = "Bodybuilder",
            [(197, 12)] = "Big Bro",

            [(199, 17)] = "Innkeeper",
            [(200, 5)] = "Materia shopkeeper",

            [(201, 9)] = "Dress shop attendant",
            [(201, 10)] = "Dress shop owner",
            [(201, 11)] = "Customer",

            [(202, 8)] = "Diner cook",
            [(202, 9)] = "Diner server",
            [(202, 10)] = "Diner patron",
            [(202, 11)] = "Diner patron",
            [(202, 12)] = "Diner patron",
            [(202, 13)] = "Diner patron",
            [(202, 14)] = "Diner patron",
            [(202, 15)] = "Diner patron",
            [(202, 16)] = "Diner patron",

            [(203, 7)] = "Pharmacy shopkeeper",

            [(204, 11)] = "Woman in bathroom",
            [(204, 12)] = "Man waiting for bathroom",
            [(204, 13)] = "Bartender",
            [(204, 14)] = "Dress shop owner",
            [(204, 15)] = "Man",
            [(204, 16)] = "Woman",

            [(205, 15)] = "Woman",
            [(205, 16)] = "Old woman",
            [(205, 17)] = "Boy",
            [(205, 18)] = "Boy",
            [(205, 19)] = "Girl",

            [(206, 14)] = "Corneo Hall doorman",

            [(207, 8)] = "Corneo Hall guard",
            [(207, 10)] = "Aerith",
            [(207, 11)] = "Tifa",

            [(208, 11)] = "Scotch",
            [(208, 13)] = "Corneo lackey",
            [(208, 14)] = "Corneo lackey",
            [(208, 15)] = "Corneo lackey",
            [(208, 16)] = "Corneo lackey",
            [(208, 17)] = "Corneo lackey",

            [(209, 12)] = "Tifa",
            [(209, 13)] = "Aerith",
            [(209, 15)] = "Kotch",

            [(214, 15)] = "Honey Bee Inn doorman",
            [(214, 16)] = "Honey Bee Inn attendant",
            [(214, 17)] = "Johnny",
            [(214, 18)] = "Man",
            [(214, 20)] = "Shinra soldier",
            [(214, 21)] = "Shinra soldier",

            [(216, 8)] = "Honey Bee Inn makeup artist",
            [(216, 9)] = "Woman changing",
            [(216, 10)] = "Woman exercising",

            [(218, 24)] = "Honey Bee Inn hostess",

            // Cloud's Nibelheim flashback. These identities come from the
            // native model-loader resources. Event directors, line proxies,
            // furniture, items, and other non-people remain intentionally
            // unlisted even when their delegated dialog begins with a name.
            [(273, 17)] = "Old man",
            [(273, 18)] = "Zangan",
            [(273, 19)] = "Innkeeper",
            [(273, 20)] = "Man in black cape",
            [(274, 8)] = "Sephiroth",
            [(274, 9)] = "Shinra infantryman",
            [(276, 11)] = "Cloud's mother",
            [(279, 7)] = "Tifa",
            [(279, 8)] = "Barret",
            [(279, 9)] = "Red XIII",
            [(279, 10)] = "Yuffie",
            [(279, 11)] = "Cait Sith",
            [(279, 12)] = "Vincent",
            [(279, 13)] = "Cid",
            [(279, 14)] = "Sephiroth",
            [(279, 15)] = "Shinra infantryman",
            [(279, 16)] = "Shinra infantryman",
            [(282, 8)] = "Sephiroth",
            [(282, 9)] = "Shinra infantryman",
            [(282, 10)] = "Shinra infantryman",
            [(282, 11)] = "Photographer",
            [(282, 12)] = "Tifa's father",
            [(282, 13)] = "Zangan",
            [(284, 16)] = "Man in black cape",
            [(284, 17)] = "Man in black cape",
            [(286, 17)] = "Man in black cape",
            [(286, 18)] = "Man in black cape",
            [(287, 21)] = "Man in black cape",
            [(290, 7)] = "Sephiroth",
            [(290, 8)] = "Shinra infantryman",
            [(290, 9)] = "Zangan",
            [(290, 10)] = "Photographer",
            [(291, 7)] = "Sephiroth",
            [(291, 8)] = "Shinra infantryman",
            [(291, 9)] = "Zangan",
            [(291, 10)] = "Photographer",
            [(293, 3)] = "Tifa",
            [(293, 4)] = "Barret",
            [(293, 5)] = "Red XIII",
            [(293, 6)] = "Yuffie",
            [(293, 7)] = "Cait Sith",
            [(293, 8)] = "Vincent",
            [(293, 9)] = "Cid",
            [(293, 10)] = "Sephiroth",
            [(300, 5)] = "Shinra infantryman",
            [(307, 3)] = "Sephiroth",
            [(312, 6)] = "Tifa",
            [(312, 8)] = "Shinra infantryman",
            [(312, 9)] = "Shinra infantryman",
            [(323, 6)] = "Tifa",
            [(323, 7)] = "Tifa",
            [(323, 8)] = "Sephiroth",
            [(324, 8)] = "Tifa",

            // Kalm and Chocobo Ranch. These labels were checked against each
            // native Talk entity and its visible field model. Models with an
            // empty/noninteractive Talk script are deliberately omitted.
            [(328, 12)] = "Weapon shopkeeper",
            [(328, 13)] = "Materia shopkeeper",
            [(329, 8)] = "Item shopkeeper",
            [(330, 9)] = "Bartender",
            [(330, 11)] = "Man",
            [(330, 12)] = "Man",
            [(331, 11)] = "Innkeeper",
            [(333, 7)] = "Woman",
            [(334, 6)] = "Girl",
            [(335, 16)] = "Man",
            [(335, 17)] = "Old man",
            [(335, 18)] = "Man",
            [(335, 19)] = "Woman",
            [(335, 20)] = "Man",
            [(335, 21)] = "Man",
            [(335, 22)] = "Boy",
            [(336, 8)] = "Old man",
            [(336, 9)] = "Dog",
            [(338, 6)] = "Man",
            [(339, 7)] = "Boy",
            [(339, 8)] = "Girl",
            [(341, 8)] = "Woman",
            [(342, 5)] = "Old man",
            [(342, 6)] = "Chocobo",
            [(343, 4)] = "Chocobo",
            [(343, 5)] = "Chocobo",
            [(344, 4)] = "Choco Bill",
            [(345, 4)] = "Chole",
            [(345, 5)] = "Choco Billy",
            [(345, 7)] = "Chocobo",
            [(345, 8)] = "Chocobo",
            [(345, 9)] = "Chocobo",
            [(345, 10)] = "Chocobo",
            [(345, 11)] = "Chocobo",
            [(345, 12)] = "Chocobo"
        };

    private static readonly IReadOnlyDictionary<
        (int FieldId, int EntityId),
        (int LineEntityId, FieldNavigationTriggerLine Line)> VerifiedInteractionLines =
        new Dictionary<
            (int FieldId, int EntityId),
            (int LineEntityId, FieldNavigationTriggerLine Line)>
        {
            // mktinn: the visible innkeeper delegates the action-key counter
            // interaction to line00/event rather than its empty Talk script.
            [(199, 17)] = (
                7,
                new FieldNavigationTriggerLine(-163, 103, 0, -26, 55, 0)),

            // mkt_s2: the cook and server are visible models, while the kitchen
            // warning and order interaction live on the two native LINE regions.
            [(202, 8)] = (
                4,
                new FieldNavigationTriggerLine(209, -32, 0, 147, -34, 0)),
            [(202, 9)] = (
                5,
                new FieldNavigationTriggerLine(-59, -173, 0, -57, -101, 0)),

            // mkt_s3: the Pharmacy model is intentionally non-talkable; its
            // counter LINE calls the shopkeeper's native scripts 3 and 4.
            [(203, 7)] = (
                2,
                new FieldNavigationTriggerLine(-2, 0, 0, 127, 0, 0))
        };

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<FieldScriptNpcDefinition>>
        VerifiedDefinitionsByField = VerifiedLabels.Keys
            .GroupBy(key => key.FieldId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FieldScriptNpcDefinition>)group
                    .Select(key =>
                    {
                        var hasLine = VerifiedInteractionLines.TryGetValue(
                            key,
                            out var interactionLine);
                        return new FieldScriptNpcDefinition(
                            key.FieldId,
                            key.EntityId,
                            string.Empty,
                            Array.Empty<int>(),
                            hasLine ? interactionLine.LineEntityId : null,
                            hasLine ? interactionLine.Line : null);
                    })
                    .ToArray());

    // Every Talk entity in these reviewed field groups was checked against the
    // installed FLEVEL scripts and model-loader metadata.
    // Do not guess a name from ordinary dialogue in these fields.
    private static readonly IReadOnlySet<int> ReviewedLabelFields =
        new HashSet<int>(
            Enumerable.Range(139, 19)
                .Concat(Enumerable.Range(172, 9))
                .Concat([188, 189, 190])
                .Concat(Enumerable.Range(195, 17))
                .Concat([214, 216, 218])
                .Concat(Enumerable.Range(273, 55))
                .Concat(Enumerable.Range(328, 18)));

    private readonly Func<int, int> readInt32;
    private readonly Func<int, short> readInt16;
    private readonly Func<int, byte> readByte;
    private readonly Func<int, int, IReadOnlyList<string>> resolveDialogLines;
    private readonly Func<int, IReadOnlyList<FieldScriptNpcDefinition>> definitionProvider;
    private readonly HashSet<(int FieldId, int EntityId)> excludedEntities;
    private readonly Func<int, bool>? isLineEnabled;

    public FieldNavigationNpcReader(
        Func<int, int> readInt32,
        Func<int, short> readInt16,
        Func<int, byte> readByte,
        Func<int, int, IReadOnlyList<string>> resolveDialogLines,
        Func<int, IReadOnlyList<FieldScriptNpcDefinition>> definitionProvider,
        IEnumerable<(int FieldId, int EntityId)>? excludedEntities = null,
        Func<int, bool>? isLineEnabled = null)
    {
        this.readInt32 = readInt32;
        this.readInt16 = readInt16;
        this.readByte = readByte;
        this.resolveDialogLines = resolveDialogLines;
        this.definitionProvider = definitionProvider;
        this.excludedEntities = excludedEntities?.ToHashSet() ?? [];
        this.isLineEnabled = isLineEnabled;
    }

    public IReadOnlyList<FieldNavigationTarget> ReadTargets(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return EmptyTargets;
        }

        var definitions = MergeVerifiedDefinitions(
            position.FieldId,
            definitionProvider(position.FieldId));
        if (definitions.Count == 0)
        {
            return EmptyTargets;
        }

        var eventTable = readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr);
        var modelCount = readByte(FieldPositionReader.AddressFieldNumModels);
        if (eventTable == 0 || modelCount == 0)
        {
            return EmptyTargets;
        }

        var targets = new List<FieldNavigationTarget>(definitions.Count);
        var playerEventAddress = eventTable + position.ModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
        var playerCollisionRadius = Math.Max(0, (int)readInt16(playerEventAddress + CollisionRadiusOffset));
        foreach (var definition in definitions)
        {
            if (excludedEntities.Contains((definition.FieldId, definition.EntityId)))
            {
                continue;
            }

            var modelId = readByte(FieldNavigationObjectReader.AddressFieldModelIdArray + definition.EntityId);
            if (modelId == 0xFF || modelId >= modelCount || modelId == position.ModelIndex)
            {
                continue;
            }

            var eventAddress = eventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
            if (readByte(eventAddress + FieldNavigationObjectReader.VisibilityOffset) == 0)
            {
                continue;
            }

            var lineEntityId = definition.InteractionLineEntityId;
            var interactionLine = definition.InteractionLine;
            var usesInteractionLine =
                lineEntityId.HasValue &&
                interactionLine.HasValue;
            if (usesInteractionLine)
            {
                if (isLineEnabled is null || !isLineEnabled(lineEntityId!.Value))
                {
                    continue;
                }
            }
            else if (readByte(eventAddress + TalkDisabledOffset) != 0)
            {
                continue;
            }

            var label = ResolveLabel(definition);
            if (label.Length == 0)
            {
                continue;
            }

            var targetX = usesInteractionLine
                ? Midpoint(interactionLine!.Value.StartX, interactionLine.Value.EndX)
                : FromModelFixedPoint(readInt32(eventAddress + FieldNavigationObjectReader.PositionXOffset));
            var targetY = usesInteractionLine
                ? Midpoint(interactionLine!.Value.StartY, interactionLine.Value.EndY)
                : FromModelFixedPoint(readInt32(eventAddress + FieldNavigationObjectReader.PositionYOffset));
            var targetZ = usesInteractionLine
                ? Midpoint(interactionLine!.Value.StartZ, interactionLine.Value.EndZ)
                : FromModelFixedPoint(readInt32(eventAddress + FieldNavigationObjectReader.PositionZOffset));
            targets.Add(new FieldNavigationTarget(
                definition.FieldId,
                FieldNavigationCategory.Npcs,
                label,
                targetX,
                targetY,
                targetZ,
                $"npc:{definition.FieldId}:{definition.EntityId}",
                TriggerEntityId: usesInteractionLine ? lineEntityId!.Value : definition.EntityId,
                InteractionRadius: usesInteractionLine
                    ? 0
                    : playerCollisionRadius + Math.Max(0, (int)readInt16(eventAddress + TalkRadiusOffset)),
                TriggerLine: usesInteractionLine ? interactionLine : null));
        }

        return targets.Count == 0 ? EmptyTargets : targets;
    }

    private static IReadOnlyList<FieldScriptNpcDefinition> MergeVerifiedDefinitions(
        int fieldId,
        IReadOnlyList<FieldScriptNpcDefinition> scriptedDefinitions)
    {
        if (!VerifiedDefinitionsByField.TryGetValue(fieldId, out var verifiedDefinitions))
        {
            return scriptedDefinitions;
        }

        var merged = new List<FieldScriptNpcDefinition>(
            scriptedDefinitions.Count + verifiedDefinitions.Count);
        var verifiedByEntity = verifiedDefinitions.ToDictionary(
            definition => definition.EntityId);
        foreach (var scripted in scriptedDefinitions)
        {
            if (!verifiedByEntity.TryGetValue(scripted.EntityId, out var verified))
            {
                merged.Add(scripted);
                continue;
            }

            // Preserve the parser's native dialog and counter-proxy evidence,
            // but allow a reviewed manual proxy for interactions delegated
            // through an event group rather than the visible model itself.
            merged.Add(scripted with
            {
                InteractionLineEntityId =
                    verified.InteractionLineEntityId ??
                    scripted.InteractionLineEntityId,
                InteractionLine =
                    verified.InteractionLine ??
                    scripted.InteractionLine
            });
        }

        foreach (var verified in verifiedDefinitions)
        {
            if (scriptedDefinitions.Any(definition => definition.EntityId == verified.EntityId))
            {
                continue;
            }

            // Some visible native models (for example Sector 5's dog and the
            // child in the weapon shop) have a Talk entry that only plays a
            // sound/animation or delegates to a LINE proxy, so there is no
            // MESSAGE opcode for the generic script catalog to discover.
            merged.Add(verified);
        }

        return merged;
    }

    private string ResolveLabel(FieldScriptNpcDefinition definition)
    {
        if (VerifiedLabels.TryGetValue(
                (definition.FieldId, definition.EntityId),
                out var verifiedLabel))
        {
            return verifiedLabel;
        }

        if (ReviewedLabelFields.Contains(definition.FieldId))
        {
            return string.Empty;
        }

        foreach (var dialogId in definition.DialogIds)
        {
            var lines = resolveDialogLines(definition.FieldId, dialogId);
            if (lines.Count >= 2 && TryNormalizeSpeakerName(lines[0], out var speaker))
            {
                return speaker;
            }
        }

        return string.Empty;
    }

    private static bool TryNormalizeSpeakerName(string line, out string speaker)
    {
        speaker = Ff7EncodedTextDecoder.NormalizeWhitespace(line).Trim();
        if (speaker.Length is 0 or > 32)
        {
            return false;
        }

        var words = speaker.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 4)
        {
            return false;
        }

        return speaker.All(character =>
            char.IsLetter(character) ||
            char.IsWhiteSpace(character) ||
            character is '\'' or '-');
    }

    private static int FromModelFixedPoint(int value) =>
        value / FieldNavigationObjectReader.ModelPositionFixedPointScale;

    private static int Midpoint(int first, int second) =>
        (int)Math.Round((first + second) / 2d, MidpointRounding.AwayFromZero);
}
