using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ff7.Accessibility.Reloaded;

namespace RussianFieldAudit;

/// <summary>
/// Everything the production catalog publishes for one field, as a value: exits, transitions, NPCs,
/// waits, native lines, map-name dialogs and exit guards. Text-table ids and message text, which a
/// translation legitimately renumbers or rewrites, are split out; so are the reviewed-mapping flags,
/// which describe how a result was authorized rather than what the field contains. Entity indices
/// can be rewritten into the English (canonical) numbering for comparison.
/// </summary>
internal static partial class NavigationSignature
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.Strict,
    };

    private static readonly HashSet<string> EntityProperties = new(StringComparer.Ordinal)
    {
        "TriggerEntityId", "SourceEntityId", "EntityId", "InteractionLineEntityId", "ControlledEntityId", "ReviewedStockEntityId",
    };

    private static readonly HashSet<string> EntityListProperties = new(StringComparer.Ordinal) { "MoverEntityIds" };

    private static readonly HashSet<string> TextProperties = new(StringComparer.Ordinal)
    {
        "DialogIds", "OwnDialogIds", "MapNameDialogIds", "RequiredChoice",
    };

    private static readonly HashSet<string> AuthorizationProperties = new(StringComparer.Ordinal)
    {
        "Diagnostic", "UsesReviewedLocalizedStockLayout", "StockToLoadedEntities", "UsesReviewedEchoLayout",
        "UsesEchoSector7Layout", "ReviewedModelLabel", "ReviewedLabel", "SuppressInstalledDialogueLabels", "ReviewedStockEntityId",
    };

    // script-exit:{field}:{entity}:..., ladder|jump:{field}:{entity}:{sourceEntity}:{script}:{triangle}
    [GeneratedRegex(@"^(?<kind>script-exit|ladder|jump):(?<field>\d+):(?<entity>\d+)(?<rest>.*)$")]
    private static partial Regex StableIdPattern();

    public sealed record Value(string Navigation, string Text, string Waits);

    public static Value Create(FieldScriptNavigationReadResult result, IReadOnlyDictionary<int, int>? toCanonical = null)
    {
        var node = JsonSerializer.SerializeToNode(result, Options)!.AsObject();
        var text = new JsonObject();
        var waits = new JsonArray();
        foreach (var property in AuthorizationProperties) node.Remove(property);
        if (node["Npcs"] is JsonArray npcs)
        {
            var npcText = new JsonArray();
            foreach (var npc in npcs.OfType<JsonObject>())
            {
                foreach (var property in AuthorizationProperties) npc.Remove(property);
                var entry = new JsonObject { ["EntityId"] = npc["EntityId"]?.DeepClone() };
                foreach (var property in TextProperties)
                    if (Take(npc, property, out var removed)) entry[property] = removed;
                npcText.Add(entry);
            }

            text["Npcs"] = npcText;
        }

        if (Take(node, "MapNameDialogIds", out var mapNames)) text["MapNameDialogIds"] = mapNames;
        if (node["Transitions"] is JsonArray transitions)
        {
            var choices = new JsonArray();
            foreach (var transition in transitions.OfType<JsonObject>())
                if (Take(transition, "RequiredChoice", out var choice))
                {
                    // The answer line and count are control flow; the dialog id and its text are not.
                    if (choice is JsonObject value)
                    {
                        transition["RequiredChoiceLine"] = value["Line"]?.DeepClone();
                        transition["RequiredChoiceAnswers"] = value["Answers"]?.DeepClone();
                    }

                    choices.Add(new JsonObject { ["StableId"] = transition["StableId"]?.DeepClone(), ["Choice"] = choice });
                }

            text["Choices"] = choices;
        }

        // Waits carry the byte index of their WAIT; record where it moved separately.
        if (node["Waits"] is JsonArray waitArray)
        {
            foreach (var wait in waitArray.OfType<JsonObject>())
                if (Take(wait, "ByteIndex", out var byteIndex))
                    waits.Add(new JsonObject { ["EntityId"] = wait["EntityId"]?.DeepClone(), ["ScriptId"] = wait["ScriptId"]?.DeepClone(), ["ByteIndex"] = byteIndex });
        }

        StripDerivedKeys(node);
        if (toCanonical is not null)
        {
            Canonicalize(node, toCanonical);
            Canonicalize(text, toCanonical);
            Canonicalize(waits, toCanonical);
        }

        return new Value(node.ToJsonString(), text.ToJsonString(), waits.ToJsonString());
    }

    // A transition condition's Key is a string built from its controlled entity and leaders, which
    // are compared (and canonicalized) themselves.
    private static void StripDerivedKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject value:
                if (value.ContainsKey("ControlledEntityId") && value.ContainsKey("LeaderCharacterIds")) value.Remove("Key");
                foreach (var (_, child) in value.ToArray()) StripDerivedKeys(child);
                break;
            case JsonArray array:
                foreach (var child in array) StripDerivedKeys(child);
                break;
        }
    }

    private static bool Take(JsonObject value, string key, out JsonNode? taken)
    {
        if (!value.TryGetPropertyValue(key, out taken)) return false;
        value.Remove(key);
        return true;
    }

    private static int Map(int entity, IReadOnlyDictionary<int, int> toCanonical) =>
        entity < 0 ? entity : toCanonical.TryGetValue(entity, out var canonical) ? canonical : 1000 + entity;

    private static string MapStableId(string id, IReadOnlyDictionary<int, int> toCanonical)
    {
        var match = StableIdPattern().Match(id);
        if (!match.Success) return id;
        var rest = match.Groups["rest"].Value;
        if (match.Groups["kind"].Value is "ladder" or "jump")
        {
            // :{sourceEntity}:{script}:{triangle}
            var parts = rest.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var source)) parts[1] = Map(source, toCanonical).ToString();
            rest = string.Join(':', parts);
        }

        return $"{match.Groups["kind"].Value}:{match.Groups["field"].Value}:{Map(int.Parse(match.Groups["entity"].Value), toCanonical)}{rest}";
    }

    private static void Canonicalize(JsonNode? node, IReadOnlyDictionary<int, int> toCanonical)
    {
        switch (node)
        {
            case JsonObject value:
                foreach (var (key, child) in value.ToArray())
                {
                    if (EntityProperties.Contains(key) && child is JsonValue number && number.TryGetValue<int>(out var entity))
                        value[key] = Map(entity, toCanonical);
                    else if (EntityListProperties.Contains(key) && child is JsonArray list)
                        value[key] = new JsonArray(list.Select(e => (JsonNode)Map(e!.GetValue<int>(), toCanonical)).ToArray());
                    else if (key is "StableId" or "ExitTraversalId" && child is JsonValue id && id.TryGetValue<string>(out var text))
                        value[key] = MapStableId(text, toCanonical);
                    else if (key is "NativeLines" && child is JsonObject lines)
                    {
                        var mapped = new JsonObject();
                        foreach (var (line, segment) in lines.ToArray().OrderBy(p => Map(int.Parse(p.Key), toCanonical)))
                            mapped[Map(int.Parse(line), toCanonical).ToString()] = segment?.DeepClone();
                        value[key] = mapped;
                    }
                    else if (key is "ExitGuards" && child is JsonObject guards)
                    {
                        var mapped = new JsonObject();
                        foreach (var (exit, guard) in guards.ToArray().OrderBy(p => MapStableId(p.Key, toCanonical), StringComparer.Ordinal))
                            mapped[MapStableId(exit, toCanonical)] = guard?.DeepClone();
                        value[key] = mapped;
                        Canonicalize(mapped, toCanonical);
                    }
                    else Canonicalize(child, toCanonical);
                }

                break;
            case JsonArray array:
                foreach (var child in array) Canonicalize(child, toCanonical);
                break;
        }
    }

    /// <summary>The top-level members whose canonical values differ, for the evidence report.</summary>
    public static IReadOnlyList<string> Differences(string left, string right)
    {
        if (left == right) return [];
        var a = JsonNode.Parse(left)!.AsObject();
        var b = JsonNode.Parse(right)!.AsObject();
        var differences = new List<string>();
        foreach (var key in a.Select(p => p.Key).Union(b.Select(p => p.Key)).Distinct())
        {
            var x = a[key]?.ToJsonString() ?? "null";
            var y = b[key]?.ToJsonString() ?? "null";
            if (x == y) continue;
            if (a[key] is JsonArray xs && b[key] is JsonArray ys)
            {
                var only = xs.Select(n => n!.ToJsonString()).ToList();
                var other = ys.Select(n => n!.ToJsonString()).ToList();
                var removed = only.Except(other).ToArray();
                var added = other.Except(only).ToArray();
                differences.Add($"{key}: english-only={removed.Length} target-only={added.Length}" +
                                (removed.Length > 0 ? " e.g. -" + Truncate(removed[0]) : "") +
                                (added.Length > 0 ? " +" + Truncate(added[0]) : ""));
            }
            else differences.Add($"{key}: {Truncate(x)} vs {Truncate(y)}");
        }

        return differences;
    }

    private static string Truncate(string value) => value.Length <= 400 ? value : value[..400] + "...";
}
