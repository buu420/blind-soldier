using System.Security.Cryptography;
using System.Text.Json;
using Ff7.Accessibility.Reloaded;

if (args.Length != 2) { Console.Error.WriteLine("FieldInteractionAudit <licensed-game-root> <local-output-directory>"); return 2; }
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var source = new FlevelDataSource(args[0]);
var native = new FieldScriptNavigationCatalog(args[0]);
var text = new FlevelFieldTextResolver(args[0]);
var objects = FieldNavigationObjectCatalog.CreateAllFields();
var story = FieldStoryEventCatalog.CreateAllFields();
var inventory = new List<object>();
var unreadable = new List<object>();
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
foreach (var (id, name) in source.FieldNames.OrderBy(e => e.Key))
{
    if (!source.TryReadField(id, out var encoded)) { unreadable.Add(new { id, name, source.Diagnostic }); continue; }
    var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
    var scripts = native.ReadAllScriptOpcodes(id);
    var nav = native.ReadField(id);
    var models = ReadModelResourceNames(bytes);
    var rows = objects.Where(r => r.FieldId == id).ToArray();
    var npcLabels = nav.Npcs.ToDictionary(n => n.EntityId, n => ResolveShippingLabel(id, n, text));
    var entities = scripts.GroupBy(s => s.EntityId).Select(g =>
    {
        var init = g.Where(s => s.ScriptId == 0).SelectMany(s => s.Opcodes).ToArray();
        var slots = init.Where(o => o.Opcode == 0xA1 && o.Bytes.Count > 1).Select(o => (int)o.Bytes[1]).Distinct().ToArray();
        var lines = init.Where(o => o.Opcode == 0xD0 && o.Bytes.Count == 13).Select(o =>
        {
            var b = o.Bytes.ToArray(); return new { o.ByteIndex, start = new[] { (int)BitConverter.ToInt16(b, 1), BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5) }, end = new[] { (int)BitConverter.ToInt16(b, 7), BitConverter.ToInt16(b, 9), BitConverter.ToInt16(b, 11) } };
        }).ToArray();
        return new
        {
            entityId = g.Key,
            name = g.First().EntityName,
            modelSlots = slots,
            models = slots.Select(s => s < models.Count ? models[s] : "<invalid>"),
            hasNpcDefinition = npcLabels.ContainsKey(g.Key),
            npcLabel = npcLabels.GetValueOrDefault(g.Key),
            hasObject = rows.Any(r => r.EntityId == g.Key),
            lines,
            scripts = g.Select(s => new { scriptId = s.ScriptId, opcodes = s.Opcodes.Select(o => new { offset = o.ByteIndex, op = (int)o.Opcode, hex = Convert.ToHexString(o.Bytes.ToArray()) }) })
        };
    }).ToArray();
    var scriptOffset = BitConverter.ToInt32(bytes, 6) + 4;
    var strings = scriptOffset + BitConverter.ToUInt16(bytes, scriptOffset + 4);
    var dialogCount = BitConverter.ToUInt16(bytes, strings);
    var dialogs = Enumerable.Range(0, dialogCount).Select(i => new { id = i, lines = text.ReadMessageLinesById(id, i) }).Where(d => d.lines.Count > 0).ToArray();
    var gateways = new List<object>();
    var trigger = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
    for (var i = 0; i < 12; i++)
    {
        var at = trigger + 0x38 + i * 0x18;
        if (at + 24 > bytes.Length) throw new InvalidDataException($"Truncated gateway table in {id}");
        var dest = BitConverter.ToInt16(bytes, at + 18);
        if (dest < 0 || dest == 32767) continue;
        gateways.Add(new
        {
            index = i,
            destination = dest,
            start = new[] { (int)BitConverter.ToInt16(bytes, at), BitConverter.ToInt16(bytes, at + 2), BitConverter.ToInt16(bytes, at + 4) },
            end = new[] { (int)BitConverter.ToInt16(bytes, at + 6), BitConverter.ToInt16(bytes, at + 8), BitConverter.ToInt16(bytes, at + 10) },
            arrivalX = BitConverter.ToInt16(bytes, at + 12),
            arrivalY = BitConverter.ToInt16(bytes, at + 14),
            arrivalTriangle = BitConverter.ToUInt16(bytes, at + 16)
        });
    }
    var record = new
    {
        id,
        name,
        nativeSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
        models,
        entities,
        dialogs,
        gateways,
        npcs = nav.Npcs,
        scriptExits = nav.Exits,
        transitions = nav.Transitions,
        objects = rows,
        story = story.Where(r => r.FieldId == id),
        evidenceLevel = "Native static extraction. Labels simulate visible enabled models; not proof of current availability or reachability."
    };
    File.WriteAllText(Path.Combine(output, $"{id:D3}-{name}.json"), JsonSerializer.Serialize(record, options));
    inventory.Add(new
    {
        id,
        name,
        modelActors = entities.Count(e => e.modelSlots.Length > 0),
        npcs = nav.Npcs.Count,
        objects = rows.Length,
        unlabeled = entities.Where(e => e.modelSlots.Length > 0 && e.hasNpcDefinition && string.IsNullOrEmpty(e.npcLabel) && !e.hasObject).Select(e => new { e.entityId, e.name, e.models }),
        undiscovered = entities.Where(e => e.modelSlots.Length > 0 && !e.hasNpcDefinition && !e.hasObject).Select(e => new { e.entityId, e.name, e.models }),
        unlistedLines = entities.Where(e => e.lines.Length > 0 && !e.hasObject).Select(e => e.entityId)
    });
}
File.WriteAllText(Path.Combine(output, "inventory.json"), JsonSerializer.Serialize(new { fields = inventory, unreadable }, new JsonSerializerOptions(options) { WriteIndented = true }));
Console.WriteLine($"Extracted {inventory.Count} native fields; {unreadable.Count} unavailable maplist entries. Local output: {output}");
return 0;

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

