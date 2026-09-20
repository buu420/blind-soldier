using Ff7.Accessibility.Reloaded;

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: FieldNpcCatalogDump <game-root> <field-id> [field-id ...]");
    return 2;
}

var includeDialogs = !args.Contains("--no-dialogs", StringComparer.OrdinalIgnoreCase);
var includeLabels = args.Contains("--labels", StringComparer.OrdinalIgnoreCase);
var includeActors = args.Contains("--actors", StringComparer.OrdinalIgnoreCase);
var gameRoot = args[0];
var dataSource = new FlevelDataSource(gameRoot);
var catalog = new FieldScriptNavigationCatalog(gameRoot);
var textResolver = new FlevelFieldTextResolver(gameRoot);

foreach (var value in args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
{
    if (!int.TryParse(value, out var fieldId))
    {
        Console.Error.WriteLine($"Invalid field id: {value}");
        return 2;
    }

    var fieldName = dataSource.FieldNames.TryGetValue(fieldId, out var name)
        ? name
        : "<unknown>";
    var field = catalog.ReadField(fieldId);
    Console.WriteLine(
        $"FIELD {fieldId} {fieldName} usable={field.IsUsable} npcs={field.Npcs.Count} exits={field.Exits.Count}");
    Console.WriteLine($"DIAGNOSTIC {field.Diagnostic}");

    // Section 3, the model loader, in load order - so MODEL n is the resource the
    // field's own CHAR opcode argument n selects. Printed beside the script's entity
    // names so a role can be corroborated against the mesh the game actually loads
    // rather than inferred from an entity name alone.
    var models = dataSource.TryReadField(fieldId, out var encodedBytes)
        ? ReadModelResourceNames(Ff7LzsDecoder.DecodeFieldFile(encodedBytes))
        : Array.Empty<string>();
    for (var index = 0; index < models.Count; index++)
    {
        Console.WriteLine($"MODEL {index} {models[index]}");
    }

    if (includeActors)
    {
        DumpActors(catalog, fieldId, models);
    }

    foreach (var exit in field.Exits.OrderBy(exit => exit.StableId, StringComparer.Ordinal))
    {
        Console.WriteLine(
            $"EXIT id={exit.StableId} point={exit.X},{exit.Y},{exit.Z} " +
            $"destinations={string.Join(',', exit.DestinationFieldIds ?? Array.Empty<int>())} " +
            $"line={exit.TriggerLine}");
    }

    foreach (var npc in field.Npcs.OrderBy(npc => npc.EntityId))
    {
        // The entity's own CHAR argument, so the mesh is bound to the entity rather
        // than guessed from load order - not every entity loads one.
        var modelName = "<none>";
        foreach (var opcode in catalog.ReadScriptOpcodes(fieldId, npc.EntityId, 0))
        {
            if (opcode.Opcode != 0xA1 || opcode.Bytes.Count < 2)
            {
                continue;
            }

            var modelIndex = opcode.Bytes[1];
            modelName = modelIndex < models.Count
                ? models[modelIndex]
                : $"<index {modelIndex} beyond {models.Count} loaded>";
            break;
        }

        Console.WriteLine(
            $"NPC entity={npc.EntityId} internal={npc.EntityName} model={modelName} " +
            $"catalogmodel={(npc.ModelResourceName.Length > 0 ? npc.ModelResourceName : "<none>")} " +
            $"line={(npc.InteractionLineEntityId?.ToString() ?? "none")} " +
            $"dialogs={string.Join(',', npc.DialogIds)}");

        if (includeLabels)
        {
            // The label the shipping reader would actually speak, produced by the
            // shipping reader - every gate it applies except the ones that depend on
            // where the player is standing is satisfied here, so what is left is the
            // classification decision alone.
            var label = ResolveShippingLabel(fieldId, npc, textResolver);
            Console.WriteLine(
                $"  LABEL entity={npc.EntityId} " +
                $"label={(label.Length > 0 ? label : "<dropped>")}");
        }

        if (!includeDialogs) continue;
        foreach (var dialogId in npc.DialogIds)
        {
            var lines = textResolver
                .ReadMessageLinesById(fieldId, dialogId)
                .Select(line => line.Replace("\r", " ").Replace("\n", " ").Trim())
                .Where(line => line.Length > 0);
            Console.WriteLine($"  DIALOG {dialogId}: {string.Join(" | ", lines)}");
        }
    }
}

return 0;

// Field model loader, section 3. Header is a blank word, the model count and the scale;
// each model record is a length-prefixed name then a fixed 46-byte tail carrying its
// animation count at offset 14, then that many animation records of a length-prefixed
// name plus two bytes. Walked by its records rather than scanned for printable text so
// that slot n is exactly what a CHAR n selects; if the records do not add up, nothing is
// returned rather than a list whose indexes have quietly shifted.
static IReadOnlyList<string> ReadModelResourceNames(byte[] fieldBytes)
{
    const int sectionOffsetsOffset = 6;
    const int sectionIndex = 2;
    const int headerSize = 6;
    const int countOffset = 2;
    const int recordTailSize = 46;
    const int animationCountOffset = 14;
    const int animationTailSize = 2;

    var offsetPosition = sectionOffsetsOffset + (sectionIndex * sizeof(int));
    if (fieldBytes.Length < offsetPosition + sizeof(int))
    {
        return Array.Empty<string>();
    }

    var sectionOffset = BitConverter.ToInt32(fieldBytes, offsetPosition);
    if (sectionOffset <= 0 || fieldBytes.Length < sectionOffset + 4)
    {
        return Array.Empty<string>();
    }

    var length = BitConverter.ToInt32(fieldBytes, sectionOffset);
    var start = sectionOffset + 4;
    if (length < headerSize || fieldBytes.Length < start + length)
    {
        return Array.Empty<string>();
    }

    var end = start + length;
    var modelCount = BitConverter.ToUInt16(fieldBytes, start + countOffset);
    var position = start + headerSize;
    var names = new List<string>(modelCount);

    for (var model = 0; model < modelCount; model++)
    {
        if (position + sizeof(ushort) > end)
        {
            return Array.Empty<string>();
        }

        var nameLength = BitConverter.ToUInt16(fieldBytes, position);
        position += sizeof(ushort);
        if (nameLength == 0 || position + nameLength + recordTailSize > end)
        {
            return Array.Empty<string>();
        }

        names.Add(System.Text.Encoding.ASCII
            .GetString(fieldBytes, position, nameLength)
            .TrimEnd('\0'));
        position += nameLength;

        var animations = BitConverter.ToUInt16(fieldBytes, position + animationCountOffset);
        position += recordTailSize;

        for (var animation = 0; animation < animations; animation++)
        {
            if (position + sizeof(ushort) > end)
            {
                return Array.Empty<string>();
            }

            position += sizeof(ushort) +
                        BitConverter.ToUInt16(fieldBytes, position) +
                        animationTailSize;
            if (position > end)
            {
                return Array.Empty<string>();
            }
        }
    }

    return names;
}

// Runs the shipping FieldNavigationNpcReader over one catalog definition, with native
// memory answered so that every position-independent gate passes: the entity has a
// loaded model that is not the player's, it is visible, its Talk is enabled and any
// interaction line it depends on is live. What survives is therefore exactly what
// ResolveLabel decided, which is the thing being audited. Returns the spoken label, or
// empty where the reader would drop the entity for want of one.
static string ResolveShippingLabel(
    int fieldId,
    FieldScriptNpcDefinition npc,
    FlevelFieldTextResolver textResolver)
{
    const int eventTable = 0x02404000;
    // Slot 0 is the player in the snapshot below, so entities start at 1.
    var modelId = (byte)Math.Min(npc.EntityId + 1, 254);

    var reader = new FieldNavigationNpcReader(
        address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr
            ? eventTable
            : 0,
        _ => 48,
        address =>
        {
            if (address == FieldPositionReader.AddressFieldNumModels)
            {
                return 255;
            }

            if (address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                address < FieldNavigationObjectReader.AddressFieldModelIdArray + 256)
            {
                return address == FieldNavigationObjectReader.AddressFieldModelIdArray + npc.EntityId
                    ? modelId
                    : (byte)0xff;
            }

            var eventAddress = eventTable + (modelId * FieldNavigationObjectReader.FieldEventDataStride);
            if (address == eventAddress + FieldNavigationObjectReader.VisibilityOffset)
            {
                return 1;
            }

            return 0;
        },
        (field, dialogId) => textResolver.ReadMessageLinesById(field, dialogId),
        _ => [npc],
        null,
        _ => true);

    var targets = reader.ReadTargets(
        new FieldPositionSnapshot(1, fieldId, 0, 0, 0, 0, 0, 0));
    return targets.Count > 0 ? targets[0].Label : string.Empty;
}

// --actors: every CHAR actor in the field, what its Talk script actually does, and every
// LINE entity that asks it to run one of its scripts. Written to design the counter rule
// against the corpus rather than against the handful of fields anyone looked at.
static void DumpActors(FieldScriptNavigationCatalog catalog, int fieldId, IReadOnlyList<string> models)
{
    var scripts = catalog.ReadAllScriptOpcodes(fieldId);
    if (scripts.Count == 0)
    {
        return;
    }

    var byEntity = scripts
        .GroupBy(s => s.EntityId)
        .ToDictionary(g => g.Key, g => g.ToArray());

    foreach (var entityId in byEntity.Keys.OrderBy(k => k))
    {
        var entityScripts = byEntity[entityId];
        var init = entityScripts.Where(s => s.ScriptId == 0).ToArray();
        if (init.Length == 0)
        {
            continue;
        }

        var charOps = init[0].Opcodes.Where(o => o.Opcode == 0xA1 && o.Bytes.Count >= 2).ToArray();
        if (charOps.Length == 0)
        {
            continue;
        }

        var slot = charOps[0].Bytes[1];
        var model = slot < models.Count ? models[slot] : $"<slot {slot}>";
        var talkScripts = entityScripts.Where(s => s.ScriptId == 1).ToArray();
        var kinds = new List<string>();
        if (talkScripts.Length == 0)
        {
            kinds.Add("NOTALK");
        }
        else
        {
            var talk = talkScripts[0];
            if (talk.Opcodes.Any(o => o.Opcode == 0x40)) kinds.Add("MESSAGE");
            if (talk.Opcodes.Any(o => o.Opcode == 0x49)) kinds.Add("MENU");
            if (talk.Opcodes.Any(o => o.Opcode is 0xF1 or 0xF2)) kinds.Add("SOUND");
            if (talk.Opcodes.Any(o => o.Opcode is 0xA3 or 0xA4 or 0xAB or 0xA8)) kinds.Add("ANIM");
            if (talk.Opcodes.Any(o => o.Opcode == 0x35)) kinds.Add("TURN");
            if (kinds.Count == 0) kinds.Add(talk.Opcodes.Count <= 1 ? "EMPTY" : "OTHER");
        }

        var tlkon = init[0].Opcodes
            .Where(o => o.Opcode == 0x7E && o.Bytes.Count >= 2)
            .Select(o => o.Bytes[1].ToString())
            .FirstOrDefault() ?? "none";

        Console.WriteLine(
            $"ACTOR field={fieldId} entity={entityId} name={entityScripts[0].EntityName} " +
            $"model={model} talk={string.Join('+', kinds)} tlkon={tlkon} " +
            $"ops={(talkScripts.Length == 0 ? 0 : talkScripts[0].Opcodes.Count)}");

        foreach (var lineEntity in byEntity.Keys.OrderBy(k => k))
        {
            if (lineEntity == entityId) continue;
            var lineScripts = byEntity[lineEntity];
            var lineInit = lineScripts.Where(s => s.ScriptId == 0).ToArray();
            if (lineInit.Length == 0 || !lineInit[0].Opcodes.Any(o => o.Opcode == 0xD0)) continue;

            foreach (var script in lineScripts)
            {
                var requests = script.Opcodes
                    .Where(o => o.Opcode is >= 0x01 and <= 0x06 && o.Bytes.Count >= 3)
                    .ToArray();
                var mine = requests.Where(o => o.Bytes[1] == entityId).ToArray();
                if (mine.Length == 0) continue;

                var action = script.Opcodes.Any(o =>
                    o.Opcode is 0x30 or 0x31 && o.Bytes.Count >= 3 &&
                    ((o.Bytes[1] | (o.Bytes[2] << 8)) & 0x20) != 0);

                Console.WriteLine(
                    $"  REQBY line={lineEntity} lineName={lineScripts[0].EntityName} " +
                    $"lineScript={script.ScriptId} wants={mine[0].Bytes[2] & 0x1F} " +
                    $"action={action} " +
                    $"menu={script.Opcodes.Any(o => o.Opcode == 0x49)} " +
                    $"message={script.Opcodes.Any(o => o.Opcode == 0x40)} " +
                    $"targets={requests.Select(o => o.Bytes[1]).Distinct().Count()}");
            }
        }
    }
}
