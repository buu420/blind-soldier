using System.Text.Json.Nodes;
using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit;

/// <summary>Findings that need every field at once: the cross-field flag graph.</summary>
internal static class GlobalFindings
{
    private static readonly HashSet<string> AccessGates = new(StringComparer.Ordinal)
    {
        "MapJump", "TriangleLock", "LineEnable", "GatewaysEnable"
    };

    private static readonly HashSet<string> PresenceGates = new(StringComparer.Ordinal)
    {
        "TalkEnable", "Visibility", "Solid", "ModelLoad", "ItemAdd", "MateriaAdd", "GilAdd", "Battle", "Menu",
        "Minigame", "PartyAdd", "PartyRemove", "PartySet", "MemberAvailability"
    };

    /// <summary>
    /// A savemap byte that a script writes and that guards something a player can see or
    /// use, where no Story row and no Object row in any field reads it. These are the
    /// progress steps that never touch GameMoment and that nothing in the mod follows.
    /// Matching is per byte, so a byte counts as followed when any row reads any of its
    /// bits; that can hide an unfollowed bit next to a followed one, never the reverse.
    /// </summary>
    public static void Derive(GlobalAudit global)
    {
        foreach (var (key, record) in global.Flags)
        {
            if (key.Block == "T" || key.IsGameMoment)
            {
                continue;
            }

            if (record.WriterCount > 0)
            {
                global.Count("flag.savemapBytesWritten");
            }

            if (record.GateCount > 0)
            {
                global.Count("flag.savemapBytesGating");
            }

            if (record.StoryFields.Count > 0)
            {
                global.Count("flag.savemapBytesReadByStory");
            }

            var untrackedBits = record.WrittenMask & record.GatedMask & ~record.TrackedMask & 0xFF;
            if (record.WriterCount == 0 || record.GateCount == 0 || untrackedBits == 0)
            {
                continue;
            }

            var access = record.GatedKinds.Where(AccessGates.Contains).ToArray();
            var presence = record.GatedKinds.Where(PresenceGates.Contains).ToArray();
            var playerWritten = record.WriterTriggers.Any(FieldReport.IsPlayerTrigger);
            // 1: something the player does opens or closes a way, and no row reads any bit
            // of the byte. 2: the same with some bits read, or an automatic write that
            // gates access, or a player write that gates presence. 3: the rest.
            var priority = playerWritten && access.Length > 0 && record.TrackedMask == 0 ? 1
                : access.Length > 0 || (playerWritten && presence.Length > 0) ? 2
                : 3;
            global.Count($"flag.untracked.p{priority}");
            var firstField = record.WriterFields.Concat(record.GateFields).Min();
            global.Add(new Candidate(
                "UntrackedProgressionFlag",
                priority,
                firstField,
                global.FieldNames.GetValueOrDefault(firstField) ?? string.Empty,
                $"{key} bits 0x{untrackedBits:X2} (savemap 0x{(Opcodes.SavemapOffset(key.Block) ?? 0) + key.Address:X4}): written in fields {string.Join(",", record.WriterFields)} " +
                $"by {string.Join("/", record.WriterTriggers)}; guards {string.Join(",", record.GatedKinds)} in fields {string.Join(",", record.GateFields)}; " +
                (record.TrackedMask == 0 ? "no Story or Object row reads it" : $"rows read only bits 0x{record.TrackedMask:X2}"),
                new JsonObject
                {
                    ["key"] = key.ToString(),
                    ["untrackedBits"] = $"0x{untrackedBits:X2}",
                    ["writtenMask"] = $"0x{record.WrittenMask:X2}",
                    ["gatedMask"] = $"0x{record.GatedMask:X2}",
                    ["trackedMask"] = $"0x{record.TrackedMask:X2}",
                    ["playerWritten"] = playerWritten,
                    ["writerFields"] = new JsonArray(record.WriterFields.Select(value => (JsonNode)JsonValue.Create(value)).ToArray()),
                    ["gateFields"] = new JsonArray(record.GateFields.Select(value => (JsonNode)JsonValue.Create(value)).ToArray()),
                    ["gatedKinds"] = new JsonArray(record.GatedKinds.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
                    ["writers"] = new JsonArray(record.Writers.Take(8).Select(item => (JsonNode)item.DeepClone()).ToArray()),
                    ["gates"] = new JsonArray(record.Gates
                        .OrderBy(item => AccessGates.Contains(Kind(item)) ? 0 : PresenceGates.Contains(Kind(item)) ? 1 : 2)
                        .Take(10)
                        .Select(item => (JsonNode)item.DeepClone()).ToArray()),
                    ["repro"] = "see claude-field-audit-<archive>-flags.json key " + key
                }));
        }
    }

    /// <summary>
    /// Fields no real field leads into: nothing but a debug room (or nothing at all) has a
    /// gateway or live map jump to them. The world map's own entrances are not in field
    /// data, so this is "no field-side way in", not "unreachable".
    /// </summary>
    public static void AnnotateReachability(GlobalAudit global)
    {
        var inbound = new Dictionary<int, SortedSet<int>>();
        foreach (var (from, transitions) in global.Transitions)
        {
            if (GlobalAudit.IsDebugRoom(global.FieldNames.GetValueOrDefault(from) ?? string.Empty))
            {
                continue;
            }

            foreach (var (destination, _) in transitions)
            {
                if (destination == from)
                {
                    continue;
                }

                if (!inbound.TryGetValue(destination, out var set))
                {
                    set = [];
                    inbound[destination] = set;
                }

                set.Add(from);
            }
        }

        foreach (var row in global.FieldRows)
        {
            var id = row["id"]!.GetValue<int>();
            var sources = inbound.GetValueOrDefault(id);
            row["inboundFromFields"] = sources?.Count ?? 0;
            if (sources is null || sources.Count == 0)
            {
                global.Count("field.noFieldSideInbound");
            }
        }

        foreach (var candidate in global.Candidates)
        {
            var sources = inbound.GetValueOrDefault(candidate.FieldId);
            candidate.Witness["inboundFromFields"] = sources is null
                ? new JsonArray()
                : new JsonArray(sources.Take(8).Select(value => (JsonNode)JsonValue.Create(value)).ToArray());
        }
    }

    /// <summary>
    /// Story and Object rows that wait for a savemap bit to be set when no live, non-debug
    /// field script sets it. Other modules (menus, battles, minigames, the world map) also
    /// write the savemap, so this is a candidate for a stale or mistyped row, not proof.
    /// </summary>
    public static void CheckRowsAgainstWriters(GlobalAudit global, AuditContext context)
    {
        var analyzed = global.FieldRows.Select(row => row["id"]!.GetValue<int>()).ToHashSet();
        foreach (var row in context.Story.SelectMany(group => group).Where(row => analyzed.Contains(row.FieldId)))
        {
            var conditions = new List<(string Role, FieldStoryStateCondition Condition)>
            {
                ("required", row.RequiredCondition),
                ("completed", row.CompletedCondition)
            };
            conditions.AddRange((row.RequiredConditions ?? []).Select(condition => ("required", condition)));
            foreach (var (role, condition) in conditions)
            {
                var needsSetBits = condition.AnyBitSet || condition.Value != 0 ||
                                   (condition.MinimumSetBits ?? 0) > 0 || (condition.MinimumValue ?? 0) > 0;
                if (condition.PartyMemberId is not null || !needsSetBits ||
                    ByteKey.FromModBank(condition.Bank, condition.Address) is not { } key || key.Block == "T")
                {
                    continue;
                }

                var mask = condition.Mask != 0 ? condition.Mask : 0xFF;
                global.Count("rows.storyConditionsChecked");
                var written = global.Flags.TryGetValue(key, out var flag) ? flag.WrittenMask & mask : 0;
                if (written != 0)
                {
                    continue;
                }

                global.Add(new Candidate("StoryConditionNeverWritten", 2, row.FieldId,
                    global.FieldNames.GetValueOrDefault(row.FieldId) ?? string.Empty,
                    $"story row \"{row.Label}\" ({role}) waits for {key} mask 0x{mask:X2}, which no live field script sets",
                    new JsonObject
                    {
                        ["label"] = row.Label,
                        ["role"] = role,
                        ["key"] = key.ToString(),
                        ["mask"] = $"0x{mask:X2}",
                        ["value"] = condition.Value,
                        ["writtenBitsOfByte"] = $"0x{(flag?.WrittenMask ?? 0):X2}"
                    }));
            }
        }

        foreach (var row in context.Objects.SelectMany(group => group).Where(row => analyzed.Contains(row.FieldId)))
        {
            if (row.CollectedBank < 0 || ByteKey.FromModBank(row.CollectedBank, row.CollectedAddress) is not { } key || key.Block == "T")
            {
                continue;
            }

            var mask = row.CollectedMask != 0 ? row.CollectedMask : 0xFF;
            global.Count("rows.objectCollectedChecked");
            var written = global.Flags.TryGetValue(key, out var flag) ? flag.WrittenMask & mask : 0;
            if (written != 0)
            {
                continue;
            }

            global.Add(new Candidate("ObjectCollectedFlagNeverWritten", 2, row.FieldId,
                global.FieldNames.GetValueOrDefault(row.FieldId) ?? string.Empty,
                $"object row \"{row.Label ?? row.Kind.ToString()}\" (entity {row.EntityId}) is collected by {key} mask 0x{mask:X2}, which no live field script sets",
                new JsonObject
                {
                    ["entity"] = row.EntityId,
                    ["label"] = row.Label,
                    ["key"] = key.ToString(),
                    ["mask"] = $"0x{mask:X2}",
                    ["writtenBitsOfByte"] = $"0x{(flag?.WrittenMask ?? 0):X2}"
                }));
        }
    }

    private static string Kind(JsonObject gate)
    {
        var effect = gate["effect"]?.GetValue<string>() ?? string.Empty;
        var colon = effect.IndexOf(':');
        var space = effect.IndexOf(' ', Math.Max(0, colon));
        return colon < 0 ? string.Empty : space < 0 ? effect[(colon + 1)..] : effect[(colon + 1)..space];
    }
}
