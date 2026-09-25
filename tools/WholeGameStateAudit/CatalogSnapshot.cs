using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit;

/// <summary>
/// Everything the production catalog publishes for each field, through its public
/// <c>ReadField</c> only: transitions, script exits, NPC definitions and map names, with
/// how long each field took. Two snapshots (before and after a change) are what a
/// regression comparison runs on.
/// </summary>
internal static class CatalogSnapshot
{
    public static int Write(string root, string path, IReadOnlySet<int> selectedFields)
    {
        var source = new FlevelDataSource(root);
        if (!source.IsUsable)
        {
            Console.Error.WriteLine(source.Diagnostic);
            return 1;
        }

        var catalog = new FieldScriptNavigationCatalog(root);
        var fields = new JsonObject();
        var failures = 0;
        var total = Stopwatch.StartNew();
        foreach (var (id, name) in source.FieldNames.OrderBy(pair => pair.Key))
        {
            if (selectedFields.Count > 0 && !selectedFields.Contains(id) || !source.HasField(id))
            {
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var result = catalog.ReadField(id);
            var milliseconds = stopwatch.Elapsed.TotalMilliseconds;
            if (milliseconds > 1000)
            {
                Console.WriteLine($"slow field {id} {name}: {milliseconds:0} ms");
            }

            if (!result.IsUsable)
            {
                failures++;
            }

            fields[id.ToString()] = new JsonObject
            {
                ["name"] = name,
                ["usable"] = result.IsUsable,
                ["ms"] = Math.Round(milliseconds, 2),
                ["transitions"] = new JsonArray(result.Transitions
                    .OrderBy(transition => transition.StableId, StringComparer.Ordinal)
                    .Select(transition => (JsonNode)new JsonObject
                    {
                        ["id"] = transition.StableId,
                        ["kind"] = transition.Kind.ToString(),
                        ["source"] = transition.SourceEntityId,
                        ["from"] = new JsonArray(transition.SourceX, transition.SourceY, transition.SourceZ, transition.SourceTriangle),
                        ["to"] = new JsonArray(transition.TargetX, transition.TargetY, transition.TargetZ, transition.TargetTriangle),
                        ["input"] = transition.RequiredInput.ToString(),
                        ["action"] = transition.RequiresAction,
                        ["movers"] = transition.MoverEntityIds is { } movers
                            ? new JsonArray(movers.Select(value => (JsonNode)JsonValue.Create(value)).ToArray())
                            : null,
                        ["conditions"] = transition.Conditions is { } conditions
                            ? new JsonArray(conditions.Select(condition => (JsonNode)JsonValue.Create(condition.Key)!).ToArray())
                            : null
                    }).ToArray()),
                ["exits"] = new JsonArray(result.Exits
                    .OrderBy(exit => exit.StableId, StringComparer.Ordinal)
                    .Select(exit => (JsonNode)new JsonObject
                    {
                        ["id"] = exit.StableId,
                        ["entity"] = exit.TriggerEntityId,
                        ["destinations"] = new JsonArray((exit.DestinationFieldIds ?? []).Select(value => (JsonNode)JsonValue.Create(value)).ToArray()),
                        ["completes"] = exit.CompletesOnArrival,
                        ["at"] = new JsonArray(exit.X, exit.Y, exit.Z),
                        ["guards"] = result.ExitGuards.TryGetValue(exit.StableId, out var guards)
                            ? new JsonArray(guards.Select(guard => (JsonNode)new JsonObject
                            {
                                ["destination"] = guard.DestinationFieldId,
                                ["alternatives"] = new JsonArray(guard.Alternatives
                                    .Select(alternative => (JsonNode)new JsonArray(alternative
                                        .Select(test => (JsonNode)JsonValue.Create(test.Key)!).ToArray()))
                                    .ToArray())
                            }).ToArray())
                            : null
                    }).ToArray()),
                ["npcs"] = new JsonArray(result.Npcs
                    .OrderBy(npc => npc.EntityId)
                    .Select(npc => (JsonNode)new JsonObject
                    {
                        ["entity"] = npc.EntityId,
                        ["name"] = npc.EntityName,
                        ["dialogs"] = new JsonArray(npc.DialogIds.Select(value => (JsonNode)JsonValue.Create(value)).ToArray()),
                        ["line"] = npc.InteractionLineEntityId,
                        ["runsTalk"] = npc.InteractionLineRunsTalk,
                        ["model"] = npc.ModelResourceName,
                        ["contact"] = npc.ContactOnly
                    }).ToArray()),
                ["mapNames"] = new JsonArray(result.MapNameDialogIds.Select(value => (JsonNode)JsonValue.Create(value)).ToArray()),
                ["diagnostic"] = result.Diagnostic
            };
        }

        var document = new JsonObject
        {
            ["schema"] = "claude-catalog-snapshot/1",
            ["root"] = root,
            ["generatedUtc"] = DateTime.UtcNow.ToString("O"),
            ["totalSeconds"] = Math.Round(total.Elapsed.TotalSeconds, 2),
            ["fields"] = fields
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }));
        Console.WriteLine($"catalog snapshot: {fields.Count} fields, {failures} unusable, {total.Elapsed.TotalSeconds:0.0}s -> {path}");
        return failures == 0 ? 0 : 1;
    }
}
