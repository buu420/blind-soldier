using System.Text.Json;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Scratch export of every description cue the mod already delivers, so a new batch cannot
/// duplicate one. Not a test: it writes a file and says nothing about correctness.
/// </summary>
internal static class DescriptionInventoryProbe
{
    public static void Run(string outputPath)
    {
        var cues = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        var areas = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions()
            .Select(cue => cue.Key)
            .ToHashSet();

        var payload = new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            totalCues = cues.Count,
            areaCues = areas.Count,
            distinctFields = cues.Select(cue => cue.FieldId).Distinct().Count(),
            cues = cues
                .OrderBy(cue => cue.FieldId)
                .ThenBy(cue => cue.EntityId)
                .ThenBy(cue => cue.ScriptId)
                .ThenBy(cue => cue.ByteIndex)
                .Select(cue => new
                {
                    key = $"{cue.FieldId}:{cue.EntityId}:{cue.ScriptId}:{cue.ByteIndex}",
                    fieldId = cue.FieldId,
                    entityId = cue.EntityId,
                    scriptId = cue.ScriptId,
                    byteIndex = cue.ByteIndex,
                    opcode = $"0x{cue.Opcode:X2}",
                    kind = areas.Contains(cue.Key) ? "area" : "action",
                    recurringGroup = cue.RecurringGroup,
                    startsRecurringGroup = cue.StartsRecurringGroup,
                    text = cue.Text
                })
                .ToArray(),
            fieldsCovered = cues
                .GroupBy(cue => cue.FieldId)
                .OrderBy(group => group.Key)
                .Select(group => new
                {
                    fieldId = group.Key,
                    cues = group.Count(),
                    hasAreaCue = group.Any(cue => areas.Contains(cue.Key))
                })
                .ToArray()
        };

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(
            $"wrote {payload.totalCues} cues ({payload.areaCues} area) over " +
            $"{payload.distinctFields} fields to {outputPath}");
    }
}
