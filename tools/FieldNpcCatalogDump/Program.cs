using Ff7.Accessibility.Reloaded;

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: FieldNpcCatalogDump <game-root> <field-id> [field-id ...]");
    return 2;
}

var gameRoot = args[0];
var dataSource = new FlevelDataSource(gameRoot);
var catalog = new FieldScriptNavigationCatalog(gameRoot);
var textResolver = new FlevelFieldTextResolver(gameRoot);

foreach (var value in args.Skip(1))
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

    foreach (var exit in field.Exits.OrderBy(exit => exit.StableId, StringComparer.Ordinal))
    {
        Console.WriteLine(
            $"EXIT id={exit.StableId} point={exit.X},{exit.Y},{exit.Z} " +
            $"destinations={string.Join(',', exit.DestinationFieldIds ?? Array.Empty<int>())} " +
            $"line={exit.TriggerLine}");
    }

    foreach (var npc in field.Npcs.OrderBy(npc => npc.EntityId))
    {
        Console.WriteLine(
            $"NPC entity={npc.EntityId} internal={npc.EntityName} dialogs={string.Join(',', npc.DialogIds)}");
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
