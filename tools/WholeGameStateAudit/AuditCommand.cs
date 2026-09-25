using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit;

internal static class AuditCommand
{
    private const string Usage =
        "WholeGameStateAudit --archive <label>=<game working dir> [--archive ...] --out <private output dir>\n" +
        "                    [--field <id>]... [--fields <from>-<to>] [--no-instructions] [--no-text]\n" +
        "Writes <out>\\fields\\<label>\\NNN-name.json and <out>\\reports\\claude-field-audit-<label>-*.json.\n" +
        "The output is decoded game data and dialogue: keep it outside the repository.";

    /// <summary>
    /// Maplist names the retail archives ship no field for: the world map's slots wm0-wm63,
    /// the reserved first entry, and twenty names the game never loads. The same 85 in both
    /// archives. Their absence is the archive as released, not missing field data; any other
    /// maplist name without a field is a failure.
    /// </summary>
    private static readonly HashSet<string> MaplistEntriesWithoutFieldData = new(
        Enumerable.Range(0, 64).Select(index => $"wm{index}").Concat(
        [
            "dummy", "qe", "blackbga", "blackbgf", "blackbgg", "whitebg1", "whitebg2", "onna_1", "onna_3", "onna_6",
            "blin69_2", "trap", "convil_3", "junmon", "subin_4", "pass", "hyou14", "xmvtes", "fallp", "m_endo", "fship_26"
        ]),
        StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly JsonSerializerOptions Indented = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = true
    };

    public static int Run(string[] args)
    {
        var archives = new List<(string Label, string Root)>();
        string? output = null;
        var fields = new SortedSet<int>();
        var includeInstructions = true;
        var includeText = true;
        string? snapshot = null;
        int? walkEntity = null;
        var walkTimings = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--archive" when index + 1 < args.Length:
                    var value = args[++index];
                    var split = value.IndexOf('=');
                    if (split <= 0)
                    {
                        Console.Error.WriteLine(Usage);
                        return 2;
                    }

                    var label = value[..split];
                    // The label names an output subdirectory and a report file: nothing that
                    // could climb out of the output folder or collide with another name.
                    if (!System.Text.RegularExpressions.Regex.IsMatch(label, "^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$"))
                    {
                        Console.Error.WriteLine($"archive label '{label}' must be letters, digits, '-' or '_' (a plain folder name)");
                        return 2;
                    }

                    if (archives.Any(archive => string.Equals(archive.Label, label, StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.Error.WriteLine($"archive label '{label}' is given twice");
                        return 2;
                    }

                    archives.Add((label, value[(split + 1)..].Trim('"')));
                    break;
                case "--out" when index + 1 < args.Length:
                    output = Path.GetFullPath(args[++index]);
                    break;
                case "--field" when index + 1 < args.Length:
                    fields.Add(int.Parse(args[++index]));
                    break;
                case "--fields" when index + 1 < args.Length:
                    var range = args[++index].Split('-');
                    for (var field = int.Parse(range[0]); field <= int.Parse(range[1]); field++)
                    {
                        fields.Add(field);
                    }

                    break;
                case "--no-instructions":
                    includeInstructions = false;
                    break;
                case "--no-text":
                    includeText = false;
                    break;
                case "--walk-entity" when index + 1 < args.Length:
                    walkEntity = int.Parse(args[++index]);
                    break;
                case "--walk-timings":
                    walkTimings = true;
                    break;
                case "--catalog-snapshot" when index + 1 < args.Length:
                    snapshot = Path.GetFullPath(args[++index]);
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        if (walkTimings && archives.Count == 1 && fields.Count == 1)
        {
            return WalkDump.WriteTimings(archives[0].Root, fields.Min);
        }

        if (walkEntity is { } walked && archives.Count == 1 && fields.Count == 1)
        {
            return WalkDump.Write(archives[0].Root, fields.Min, walked);
        }

        if (snapshot is not null && archives.Count == 1)
        {
            // What the production catalog publishes, for before/after regression comparisons.
            return CatalogSnapshot.Write(archives[0].Root, snapshot, fields);
        }

        if (archives.Count == 0 || output is null)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        var repository = FindRepository(AppContext.BaseDirectory);
        if (repository is not null && output.StartsWith(repository, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Refusing to write decoded game data inside the repository ({repository}).");
            return 2;
        }

        var results = new List<(string Label, GlobalAudit Global)>();
        var failed = false;
        foreach (var (label, root) in archives)
        {
            var global = RunArchive(label, root, output, fields, includeInstructions, includeText);
            if (global is null)
            {
                return 1;
            }

            results.Add((label, global));
            // A run that could not read, decode or report a real field is not a complete
            // audit, however much of it was written.
            var failures = new[] { "field.missing", "field.undecodable", "field.noScript", "field.reportFailed" }
                .Sum(key => global.Counts.GetValueOrDefault(key));
            if (failures > 0)
            {
                Console.Error.WriteLine($"{label}: {failures} fields could not be audited; see the summary's unreadable list");
                failed = true;
            }
        }

        if (results.Count > 1)
        {
            WriteArchiveComparison(output, results);
        }

        return failed ? 1 : 0;
    }

    private static GlobalAudit? RunArchive(
        string label,
        string root,
        string output,
        IReadOnlySet<int> selectedFields,
        bool includeInstructions,
        bool includeText)
    {
        var stopwatch = Stopwatch.StartNew();
        var context = new AuditContext(label, root, includeInstructions, includeText);
        var source = context.Source!;
        if (!source.IsUsable)
        {
            Console.Error.WriteLine($"{label}: {source.Diagnostic}");
            return null;
        }

        var global = context.Global;
        foreach (var (id, name) in source.FieldNames)
        {
            global.FieldNames[id] = name;
        }

        var fieldDirectory = Path.Combine(output, "fields", label);
        Directory.CreateDirectory(fieldDirectory);
        Console.WriteLine($"{label}: {source.Diagnostic}; {source.FieldNames.Count} maplist entries");
        foreach (var (id, name) in source.FieldNames.OrderBy(pair => pair.Key))
        {
            if (selectedFields.Count > 0 && !selectedFields.Contains(id))
            {
                continue;
            }

            global.Count("field.maplist");
            if (!source.TryReadField(id, out var encoded))
            {
                if (MaplistEntriesWithoutFieldData.Contains(name))
                {
                    global.Count("field.maplistOnly");
                    global.MaplistOnly.Add(new JsonObject { ["id"] = id, ["name"] = name });
                    continue;
                }

                global.Count("field.missing");
                global.Unreadable.Add(new JsonObject { ["id"] = id, ["name"] = name, ["reason"] = "not in archive" });
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
            }
            catch (Exception exception)
            {
                global.Count("field.undecodable");
                global.Unreadable.Add(new JsonObject { ["id"] = id, ["name"] = name, ["reason"] = $"LZS: {exception.Message}" });
                continue;
            }

            var sha = Convert.ToHexString(SHA256.HashData(bytes));
            global.FieldHashes[id] = sha;
            var analysis = FieldAnalysis.Analyze(id, name, bytes, out var diagnostic);
            if (analysis is null)
            {
                global.Count("field.noScript");
                global.Unreadable.Add(new JsonObject { ["id"] = id, ["name"] = name, ["reason"] = diagnostic, ["sha256"] = sha });
                continue;
            }

            JsonObject json;
            try
            {
                json = new FieldReport(context, analysis, sha).Build();
            }
            catch (Exception exception)
            {
                global.Count("field.reportFailed");
                global.Unreadable.Add(new JsonObject { ["id"] = id, ["name"] = name, ["reason"] = $"report failed: {exception}" });
                Console.Error.WriteLine($"{label} {id} {name}: {exception}");
                continue;
            }

            File.WriteAllText(Path.Combine(fieldDirectory, $"{id:D3}-{name}.json"), json.ToJsonString(Compact));
            if (id % 50 == 0)
            {
                Console.WriteLine($"{label}: field {id} {name} ({stopwatch.Elapsed:mm\\:ss})");
            }
        }

        GlobalFindings.Derive(global);
        if (selectedFields.Count == 0)
        {
            // Needs every field's writers; a partial run would report every row outside it.
            GlobalFindings.CheckRowsAgainstWriters(global, context);
        }

        GlobalFindings.AnnotateReachability(global);
        WriteReports(output, label, root, global, stopwatch.Elapsed);
        Console.WriteLine($"{label}: {global.Counts.GetValueOrDefault("field.analyzed")} fields analysed, {global.Candidates.Count} candidates, {stopwatch.Elapsed:mm\\:ss}");
        return global;
    }

    private static void WriteReports(string output, string label, string root, GlobalAudit global, TimeSpan elapsed)
    {
        var reports = Path.Combine(output, "reports");
        Directory.CreateDirectory(reports);
        var byClass = global.Candidates
            .GroupBy(candidate => candidate.Class)
            .OrderBy(group => group.Min(candidate => candidate.Priority))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (JsonNode)new JsonObject
            {
                ["class"] = group.Key,
                ["priority"] = group.Min(candidate => candidate.Priority),
                ["count"] = group.Count(),
                ["fields"] = group.Select(candidate => candidate.FieldId).Distinct().Count()
            })
            .ToArray();
        var summary = new JsonObject
        {
            ["schema"] = "claude-field-audit-summary/1",
            ["archive"] = label,
            ["root"] = root,
            ["generatedUtc"] = DateTime.UtcNow.ToString("O"),
            ["elapsedSeconds"] = Math.Round(elapsed.TotalSeconds, 1),
            ["opcodeTableCorrections"] = new JsonArray(Opcodes.VariableTableCorrections.Select(text => (JsonNode)JsonValue.Create(text)!).ToArray()),
            ["counts"] = new JsonObject(global.Counts.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)JsonValue.Create(pair.Value)))),
            ["candidateClasses"] = new JsonArray(byClass),
            ["unreadable"] = new JsonArray(global.Unreadable.Select(item => (JsonNode)item.DeepClone()).ToArray()),
            ["maplistWithoutFieldData"] = new JsonArray(global.MaplistOnly.Select(item => (JsonNode)item.DeepClone()).ToArray()),
            ["walkerModel"] = ShippingReflection.IsRepairedCatalog ? "published-catalog" : "baseline-0.6.8",
            ["fields"] = new JsonArray(global.FieldRows.Select(row => (JsonNode)row.DeepClone()).ToArray()),
            ["fieldHashes"] = new JsonObject(global.FieldHashes.OrderBy(pair => pair.Key)
                .Select(pair => KeyValuePair.Create(pair.Key.ToString(), (JsonNode?)JsonValue.Create(pair.Value))))
        };
        File.WriteAllText(Path.Combine(reports, $"claude-field-audit-{label}-summary.json"), summary.ToJsonString(Indented));

        var candidates = new JsonArray(global.Candidates
            .OrderBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Class, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.FieldId)
            .Select(candidate => (JsonNode)new JsonObject
            {
                ["class"] = candidate.Class,
                ["priority"] = candidate.Priority,
                ["field"] = candidate.FieldId,
                ["fieldName"] = candidate.FieldName,
                ["summary"] = candidate.Summary,
                ["witness"] = candidate.Witness.DeepClone()
            })
            .ToArray());
        File.WriteAllText(Path.Combine(reports, $"claude-field-audit-{label}-candidates.json"),
            new JsonObject { ["archive"] = label, ["candidates"] = candidates }.ToJsonString(Indented));

        var flags = new JsonArray(global.Flags
            .OrderBy(pair => pair.Key.Block, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Address)
            .Select(pair => (JsonNode)new JsonObject
            {
                ["key"] = pair.Key.ToString(),
                ["savemapOffset"] = Opcodes.SavemapOffset(pair.Key.Block) is { } offset ? $"0x{offset + pair.Key.Address:X4}" : null,
                ["gameMoment"] = pair.Key.IsGameMoment ? true : null,
                ["writerCount"] = pair.Value.WriterCount,
                ["readerCount"] = pair.Value.ReaderCount,
                ["gateCount"] = pair.Value.GateCount,
                ["writerFields"] = Numbers(pair.Value.WriterFields),
                ["readerFields"] = Numbers(pair.Value.ReaderFields),
                ["gateFields"] = Numbers(pair.Value.GateFields),
                ["storyFields"] = Numbers(pair.Value.StoryFields),
                ["objectFields"] = Numbers(pair.Value.ObjectFields),
                ["writerTriggers"] = new JsonArray(pair.Value.WriterTriggers.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
                ["gatedKinds"] = new JsonArray(pair.Value.GatedKinds.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
                ["writers"] = new JsonArray(pair.Value.Writers.Select(item => (JsonNode)item.DeepClone()).ToArray()),
                ["readers"] = new JsonArray(pair.Value.Readers.Select(item => (JsonNode)item.DeepClone()).ToArray()),
                ["gates"] = new JsonArray(pair.Value.Gates.Select(item => (JsonNode)item.DeepClone()).ToArray())
            })
            .ToArray());
        File.WriteAllText(Path.Combine(reports, $"claude-field-audit-{label}-flags.json"),
            new JsonObject { ["archive"] = label, ["flags"] = flags }.ToJsonString(Indented));
    }

    private static JsonArray Numbers(IEnumerable<int> values) =>
        new(values.Select(value => (JsonNode)JsonValue.Create(value)).ToArray());

    private static void WriteArchiveComparison(string output, IReadOnlyList<(string Label, GlobalAudit Global)> results)
    {
        var first = results[0];
        var comparison = new JsonArray();
        foreach (var (label, global) in results.Skip(1))
        {
            var ids = first.Global.FieldHashes.Keys.Union(global.FieldHashes.Keys).Order().ToArray();
            var differing = ids.Where(id =>
                    !first.Global.FieldHashes.TryGetValue(id, out var left) ||
                    !global.FieldHashes.TryGetValue(id, out var right) ||
                    left != right)
                .ToArray();
            var candidateCounts = first.Global.Candidates.GroupBy(candidate => candidate.Class)
                .ToDictionary(group => group.Key, group => group.Count());
            var otherCounts = global.Candidates.GroupBy(candidate => candidate.Class)
                .ToDictionary(group => group.Key, group => group.Count());
            comparison.Add(new JsonObject
            {
                ["left"] = first.Label,
                ["right"] = label,
                ["fieldsCompared"] = ids.Length,
                ["fieldsWithDifferentDecodedBytes"] = Numbers(differing),
                ["candidateClassCountsEqual"] = candidateCounts.Count == otherCounts.Count &&
                                                candidateCounts.All(pair => otherCounts.GetValueOrDefault(pair.Key) == pair.Value),
                ["leftCandidates"] = first.Global.Candidates.Count,
                ["rightCandidates"] = global.Candidates.Count
            });
        }

        var reports = Path.Combine(output, "reports");
        File.WriteAllText(Path.Combine(reports, "claude-field-audit-archives.json"),
            new JsonObject { ["comparisons"] = comparison }.ToJsonString(Indented));
    }

    private static string? FindRepository(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
