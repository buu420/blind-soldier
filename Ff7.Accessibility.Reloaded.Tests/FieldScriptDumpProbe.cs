using System.Text.Json;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Scratch dump of complete native opcode streams for chosen fields, straight from the
/// installed flevel through the production reader. Conditions and jumps are included, which
/// the earlier event-only extraction dropped, so control flow can actually be read.
///
/// <para>One <see cref="FieldScriptNavigationCatalog.ReadAllScriptOpcodes"/> call per field,
/// for the same reason <see cref="AreaCandidateProbe"/> has one. This probe used to ask for
/// 40 entities x 34 script ids separately and every one of those 1360 calls decompressed and
/// re-parsed the whole field file: 3m25s for 45 fields, against about a second for the same
/// fields through the single-parse call. Asking the field what it contains is also more
/// complete than guessing a rectangle, so an entity past 39 or a script past 33 is no longer
/// silently dropped.</para>
/// </summary>
internal static class FieldScriptDumpProbe
{
    public static void Run(string fieldList, string outputPath)
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? @"C:\Games\Final Fantasy VII\workingdir";
        var catalog = new FieldScriptNavigationCatalog(gameRoot);
        var fields = fieldList.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.Parse(value.Trim()))
            .ToArray();

        var payload = fields.Select(fieldId => new
        {
            fieldId,
            entities = catalog.ReadAllScriptOpcodes(fieldId)
                .GroupBy(definition => definition.EntityId)
                .OrderBy(group => group.Key)
                .Select(group => new
                {
                    entityId = group.Key,
                    scripts = group
                        .OrderBy(definition => definition.ScriptId)
                        .Select(definition => new
                        {
                            scriptId = definition.ScriptId,
                            opcodes = definition.Opcodes
                                .Select(op => new
                                {
                                    at = op.ByteIndex,
                                    op = $"0x{op.Opcode:X2}",
                                    entity = op.EntityName,
                                    bytes = Convert.ToHexString(op.Bytes.ToArray())
                                })
                                .ToArray()
                        })
                        .Where(script => script.opcodes.Length > 0)
                        .ToArray()
                })
                .Where(entity => entity.scripts.Length > 0)
                .ToArray()
        }).ToArray();

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false }));
        Console.WriteLine(
            $"dumped {payload.Length} fields, " +
            $"{payload.Sum(f => f.entities.Sum(e => e.scripts.Length))} scripts to {outputPath}");
    }
}
