using System.Text.Json;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Scratch export of the native MPNAM anchor and displayed area name for a list of fields,
/// read straight from the installed flevel through the production catalogs. Research only.
///
/// <para>One <see cref="FieldScriptNavigationCatalog.ReadAllScriptOpcodes"/> call per field.
/// The first version of this probe asked for every entity and script id separately, and each
/// of those 1360 calls decompressed and re-parsed the whole field file; a 205-field run had
/// not finished after eight minutes.</para>
/// </summary>
internal static class AreaCandidateProbe
{
    public static void Run(string fieldList, string outputPath)
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? @"C:\Games\Final Fantasy VII\workingdir";
        var scripts = new FieldScriptNavigationCatalog(gameRoot);
        var names = new FieldMapNameCatalog(scripts, new FlevelFieldTextResolver(gameRoot));
        var covered = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .Select(cue => cue.FieldId)
            .ToHashSet();
        var areaCovered = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions()
            .Select(cue => cue.FieldId)
            .ToHashSet();

        var rows = new List<object>();
        foreach (var fieldId in fieldList.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(value => int.Parse(value.Trim()))
                     .Distinct()
                     .OrderBy(value => value))
        {
            var definitions = scripts.ReadAllScriptOpcodes(fieldId);
            var resolution = names.Read(fieldId);
            var anchors = definitions
                .SelectMany(definition => definition.Opcodes
                    .Where(op => op.Opcode == FieldOpcodeAddressResolver.OpcodeMapNameIndex)
                    .Select(op => new
                    {
                        entityId = definition.EntityId,
                        entityName = op.EntityName,
                        scriptId = definition.ScriptId,
                        byteIndex = op.ByteIndex,
                        opcode = $"0x{op.Opcode:X2}",
                        bytes = Convert.ToHexString(op.Bytes.ToArray())
                    }))
                .OrderBy(anchor => anchor.entityId)
                .ThenBy(anchor => anchor.scriptId)
                .ThenBy(anchor => anchor.byteIndex)
                .ToArray();

            rows.Add(new
            {
                fieldId,
                displayedNames = resolution.Names,
                isKnownField = resolution.IsKnownField,
                mapNameAnchors = anchors,
                entityCount = definitions.Select(definition => definition.EntityId).Distinct().Count(),
                scriptCount = definitions.Count,
                alreadyHasAnyCue = covered.Contains(fieldId),
                alreadyHasAreaCue = areaCovered.Contains(fieldId)
            });
        }

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"probed {rows.Count} fields into {outputPath}");
    }
}
