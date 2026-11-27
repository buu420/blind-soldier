using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ff7.Accessibility.Reloaded;
using LocalizedDescriptionCueAligner;
using RussianFieldAudit;

// Audits the FFRTT Russian translation (ayumosa/ff7-7h-russian 1.02 for 7th Heaven) against the
// official English field archive for every reviewed navigation field, and emits the exact Russian
// navigation identities. 7th Heaven overlays the Russian flevel while the installed English
// flevel stays on disk, so the runtime parses the loaded Russian script but reads model metadata
// from the English archive; a field is admitted only when that combination is proven to describe
// the field the player is in. No game asset or script is written to the output, only hashes,
// entity names, model resource names and instruction-difference evidence.
if (args.Length < 3)
{
    Console.Error.WriteLine(
        "Usage: RussianFieldAudit <english-workingdir> <russian-root> <report-directory> [--reviewed <decisions.json>] [--emit <RussianFieldCompatibility.Generated.cs>] [--verify-runtime]");
    return 2;
}

var englishRoot = Path.GetFullPath(args[0]);
var russianRoot = Path.GetFullPath(args[1]);
var reportDirectory = Path.GetFullPath(args[2]);
string? reviewedPath = null;
string? emitPath = null;
var verifyRuntime = false;
for (var index = 3; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--reviewed" when index + 1 < args.Length: reviewedPath = Path.GetFullPath(args[++index]); break;
        case "--emit" when index + 1 < args.Length: emitPath = Path.GetFullPath(args[++index]); break;
        case "--verify-runtime": verifyRuntime = true; break;
        default: Console.Error.WriteLine($"Unknown argument {args[index]}"); return 2;
    }
}

Directory.CreateDirectory(reportDirectory);
static Ff7GameLanguageContext ContextAt(Ff7GameLanguageDescriptor descriptor, string gameRoot) =>
    new(descriptor, Path.Combine(gameRoot, "data"), Ff7GameLanguageDetectionSource.Configuration, "Russian field audit");
static string FileSha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

var englishContext = ContextAt(Ff7GameLanguages.Get(Ff7GameLanguage.English), englishRoot);
var russianContext = ContextAt(Ff7GameLanguages.RussianFanTranslation, russianRoot);
foreach (var context in new[] { englishContext, russianContext })
    if (!File.Exists(context.FieldArchivePath))
    {
        Console.Error.WriteLine($"Missing {context.Code} field archive {context.FieldArchivePath}");
        return 1;
    }

var englishArchiveSha = FileSha256(englishContext.FieldArchivePath);
var russianArchiveSha = FileSha256(russianContext.FieldArchivePath);
var englishData = new FlevelDataSource(englishRoot, englishContext);
var russianData = new FlevelDataSource(russianRoot, russianContext);
var englishScripts = new FieldScriptNavigationCatalog(englishRoot, englishContext);
var russianScripts = new FieldScriptNavigationCatalog(russianRoot, russianContext);
if (!englishData.FieldNames.OrderBy(p => p.Key).SequenceEqual(russianData.FieldNames.OrderBy(p => p.Key)))
{
    Console.Error.WriteLine("The Russian maplist does not name the same fields as the English maplist.");
    return 1;
}

var decisions = ReadDecisions(reviewedPath);
var audits = new List<FieldAudit>();
var failures = new List<string>();
var reviewedFields = 0;
foreach (var (fieldId, name) in englishData.FieldNames.OrderBy(p => p.Key))
{
    if (!englishData.TryReadField(fieldId, out var englishEncoded) || !russianData.TryReadField(fieldId, out var russianEncoded))
        continue;
    var englishFile = FieldSections.TryRead(Ff7LzsDecoder.DecodeFieldFile(englishEncoded));
    var russianFile = FieldSections.TryRead(Ff7LzsDecoder.DecodeFieldFile(russianEncoded));
    if (englishFile is null) continue;
    var englishPrefix = englishFile.PrefixSha256;
    // The reviewed navigation fields are exactly those whose English prefix is the manifest's stock row.
    if (WholeGameEchoNavigationManifest.ResolveDescriptionVariant(new LoadedFieldScriptIdentity(fieldId, englishPrefix)) != SupportedFieldScriptVariant.Vanilla ||
        WholeGameEchoNavigationManifest.ResolveVariant(new LoadedFieldScriptIdentity(fieldId, englishPrefix)) != SupportedFieldScriptVariant.Vanilla)
        continue;
    reviewedFields++;
    if (russianFile is null || russianFile.Sections.Length != englishFile.Sections.Length)
    {
        failures.Add($"field {fieldId} {name}: Russian field file unreadable or has a different section count");
        continue;
    }

    var audit = new FieldAudit
    {
        FieldId = fieldId,
        Name = name,
        EnglishPrefix = englishPrefix,
        RussianPrefix = russianFile.PrefixSha256,
        EnglishModelSection = englishFile.SectionSha256(FieldSections.ModelLoader),
        RussianModelSection = russianFile.SectionSha256(FieldSections.ModelLoader),
        HeaderSame = englishFile.HeaderWithoutTextOffset == russianFile.HeaderWithoutTextOffset,
        EnglishTextOffset = englishFile.TextOffset,
        RussianTextOffset = russianFile.TextOffset,
        EnglishAkaoCount = englishFile.AkaoCount,
        RussianAkaoCount = russianFile.AkaoCount,
    };
    for (var section = 0; section < englishFile.Sections.Length; section++)
        if (!englishFile.Sections[section].AsSpan().SequenceEqual(russianFile.Sections[section]))
            audit.DifferentSections.Add(section < FieldSections.Names.Length ? FieldSections.Names[section] : $"section{section}");
    audit.CompareModels(englishFile, russianFile);
    var englishNative = NativeField.TryRead(englishData, englishScripts, fieldId);
    var russianNative = NativeField.TryRead(russianData, russianScripts, fieldId);
    if (englishNative is null || russianNative is null)
    {
        audit.StructuralEdits.Add("native script listing unreadable");
        audit.StructuralPresenceChange = true;
    }
    else
        audit.CompareScripts(englishNative, russianNative);

    // The production parser on each archive's own disk file: the field as the catalog publishes it.
    var englishResult = englishScripts.ReadDiskField(fieldId);
    var russianResult = russianScripts.ReadDiskField(fieldId);
    audit.EnglishNavigationUsable = englishResult.IsUsable;
    audit.RussianNavigationUsable = russianResult.IsUsable;
    audit.EnglishNavigationDiagnostic = englishResult.Diagnostic;
    audit.RussianNavigationDiagnostic = russianResult.Diagnostic;
    var toCanonical = audit.EntityMap.ToDictionary(p => p.Value, p => p.Key);
    var englishSignature = NavigationSignature.Create(englishResult);
    var russianSignature = NavigationSignature.Create(russianResult, audit.EntityNamesSame ? null : toCanonical);
    audit.EnglishNavigation = Sha(englishSignature.Navigation);
    audit.RussianNavigation = Sha(russianSignature.Navigation);
    audit.NavigationDifferences.AddRange(NavigationSignature.Differences(englishSignature.Navigation, russianSignature.Navigation));
    audit.TextIdDifferences.AddRange(NavigationSignature.Differences(englishSignature.Text, russianSignature.Text));
    audit.WaitPositionsMoved = englishSignature.Waits != russianSignature.Waits;
    audits.Add(audit);
}

static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

// Decide every field. A prefix shared with English is already the stock identity and needs no row.
var rows = new List<ManifestRow>();
var excluded = new List<(FieldAudit Audit, string Reason)>();
var queue = new List<FieldAudit>();
var officialStockPrefixes = new SortedSet<int>();
foreach (var audit in audits)
{
    if (audit.SamePrefix)
    {
        if (audit.DifferentSections.Contains("model-loader"))
            failures.Add($"field {audit.FieldId}: shares the English prefix but not its model loader; the stock path would read English models");
        continue;
    }

    // A Russian prefix may be byte-identical to an official localized stock prefix (the translation
    // kept that archive's window geometry). It is still a stock identity, but its disk is English
    // under 7th Heaven, so it needs the same row; an Echo-S or Postscript identity never does.
    var known = WholeGameEchoNavigationManifest.ResolveVariant(new LoadedFieldScriptIdentity(audit.FieldId, audit.RussianPrefix));
    if (known is not (SupportedFieldScriptVariant.Unknown or SupportedFieldScriptVariant.Vanilla))
    {
        failures.Add($"field {audit.FieldId}: Russian prefix {audit.RussianPrefix} collides with a reviewed {known} identity");
        continue;
    }

    if (known == SupportedFieldScriptVariant.Vanilla) officialStockPrefixes.Add(audit.FieldId);

    decisions.TryGetValue((audit.FieldId, audit.RussianPrefix), out var decision);
    if (decision is { Include: false })
    {
        excluded.Add((audit, "reviewed exclusion: " + decision.Evidence));
        continue;
    }

    if (audit.ExactSemanticEquivalent && decision is null)
    {
        rows.Add(new ManifestRow(audit.FieldId, audit.RussianPrefix, audit.EnglishPrefix, audit.EnglishModelSection, "semantic", null));
        continue;
    }

    queue.Add(audit);
    if (decision is { Include: true })
    {
        if (!audit.EnglishNavigationUsable || !audit.RussianNavigationUsable)
        {
            failures.Add($"field {audit.FieldId}: reviewed inclusion of an unreadable field");
            continue;
        }

        if (!audit.ModelNamesSame)
        {
            failures.Add($"field {audit.FieldId}: reviewed inclusion cannot waive different model resource names");
            continue;
        }

        if (audit.EntityNamesSame != (decision.Map is null))
        {
            failures.Add($"field {audit.FieldId}: a reviewed entity map is required exactly when entity names differ");
            continue;
        }

        if (decision.Map is { } map && !map.OrderBy(p => p.Key).SequenceEqual(audit.EntityMap.OrderBy(p => p.Key)))
        {
            failures.Add($"field {audit.FieldId}: reviewed entity map disagrees with the forced name alignment");
            continue;
        }

        rows.Add(new ManifestRow(audit.FieldId, audit.RussianPrefix, audit.EnglishPrefix, audit.EnglishModelSection, "reviewed", decision.Map));
    }
    else
        excluded.Add((audit, "pending manual review: " + string.Join(',', audit.ReviewReasons())));
}

foreach (var key in decisions.Keys.Where(k => !audits.Any(a => a.FieldId == k.Field && a.RussianPrefix == k.Prefix)))
    failures.Add($"decision for field {key.Field} {key.Prefix} names no audited Russian identity");

// The production runtime path, after integration: the Russian script loaded over the English disk
// archive, the Russian script over its own archive, and the English script, each through
// LoadedFieldScriptIdentityReader and FieldScriptNavigationCatalog.ReadField.
var runtimeRows = new List<string> { "field\tname\tenglish_loaded\trussian_same_disk\trussian_on_english_disk\tequal_to_same_disk\tcanonical_equal_to_english\tdiagnostic" };
var runtimeFailures = 0;
if (verifyRuntime)
{
    var englishMemory = new LoadedScriptMemory();
    var sameMemory = new LoadedScriptMemory();
    var hybridMemory = new LoadedScriptMemory();
    var englishLoaded = new FieldScriptNavigationCatalog(englishRoot, englishContext, new LoadedFieldScriptIdentityReader(englishMemory));
    var sameLoaded = new FieldScriptNavigationCatalog(russianRoot, russianContext, new LoadedFieldScriptIdentityReader(sameMemory));
    var hybridLoaded = new FieldScriptNavigationCatalog(englishRoot, englishContext, new LoadedFieldScriptIdentityReader(hybridMemory));
    var admitted = rows.Select(r => r.Field).ToHashSet();
    foreach (var audit in audits)
    {
        englishData.TryReadField(audit.FieldId, out var englishEncoded);
        russianData.TryReadField(audit.FieldId, out var russianEncoded);
        var englishSection = FieldSections.TryRead(Ff7LzsDecoder.DecodeFieldFile(englishEncoded))!.Sections[0];
        var russianSection = FieldSections.TryRead(Ff7LzsDecoder.DecodeFieldFile(russianEncoded))!.Sections[0];
        englishMemory.Load(audit.FieldId, englishSection);
        sameMemory.Load(audit.FieldId, russianSection);
        hybridMemory.Load(audit.FieldId, russianSection);
        var english = englishLoaded.ReadField(audit.FieldId);
        var same = sameLoaded.ReadField(audit.FieldId);
        var hybrid = hybridLoaded.ReadField(audit.FieldId);
        var expected = audit.SamePrefix || admitted.Contains(audit.FieldId);
        var toCanonical = audit.EntityMap.ToDictionary(p => p.Value, p => p.Key);
        var sameValue = NavigationSignature.Create(same);
        var hybridValue = NavigationSignature.Create(hybrid);
        var equalToSame = same.IsUsable && hybrid.IsUsable && sameValue == hybridValue;
        var canonical = NavigationSignature.Create(hybrid, audit.EntityNamesSame ? null : toCanonical);
        var englishValue = NavigationSignature.Create(english);
        var canonicalEqual = hybrid.IsUsable && english.IsUsable && canonical.Navigation == englishValue.Navigation;
        var ok = english.IsUsable && (expected
            ? same.IsUsable && hybrid.IsUsable && equalToSame && (canonicalEqual || !audit.ExactSemanticEquivalent)
            : !same.IsUsable && !hybrid.IsUsable);
        if (!ok) runtimeFailures++;
        runtimeRows.Add(string.Join('\t', audit.FieldId, audit.Name, english.IsUsable, same.IsUsable, hybrid.IsUsable, equalToSame, canonicalEqual,
            ok ? "" : $"expected-admitted={expected}; {hybrid.Diagnostic}; {same.Diagnostic}"));
    }

    File.WriteAllLines(Path.Combine(reportDirectory, "runtime-verification.tsv"), runtimeRows);
}

var routeUntrusted = audits.Where(a =>
        (!a.EnglishRouteMetadataEquivalent &&
         !(decisions.TryGetValue((a.FieldId, a.RussianPrefix), out var decision) && decision.Include && decision.EnglishRoutesEquivalent)) ||
        excluded.Any(e => e.Audit == a))
    .Select(a => a.FieldId).Order().ToArray();
WriteReports();
if (emitPath is not null)
{
    File.WriteAllText(emitPath, ManifestEmitter.Write(rows, englishArchiveSha, russianArchiveSha, audits.Count(a => a.SamePrefix), excluded.Count, routeUntrusted));
    Console.WriteLine($"Wrote {emitPath}");
}

Console.WriteLine($"reviewed-fields={reviewedFields} audited={audits.Count} same-prefix={audits.Count(a => a.SamePrefix)} " +
                  $"semantic={rows.Count(r => r.Review == "semantic")} reviewed={rows.Count(r => r.Review == "reviewed")} " +
                  $"excluded={excluded.Count} review-queue={queue.Count} failures={failures.Count}" +
                  (verifyRuntime ? $" runtime-failures={runtimeFailures}" : ""));
foreach (var failure in failures) Console.Error.WriteLine("FAIL " + failure);
return failures.Count == 0 && runtimeFailures == 0 && reviewedFields == 702 ? 0 : 1;

void WriteReports()
{
    var options = new JsonSerializerOptions { WriteIndented = false };
    File.WriteAllLines(Path.Combine(reportDirectory, "fields.jsonl"), audits.Select(a => JsonSerializer.Serialize(new
    {
        a.FieldId, a.Name, a.EnglishPrefix, a.RussianPrefix, a.SamePrefix, a.DifferentSections, a.HeaderSame,
        a.EnglishTextOffset, a.RussianTextOffset, a.EnglishAkaoCount, a.RussianAkaoCount, a.EntityNamesSame,
        EnglishEntities = a.EntityNamesSame ? null : a.EnglishEntities, RussianEntities = a.EntityNamesSame ? null : a.RussianEntities,
        EntityMap = a.EntityNamesSame ? null : a.EntityMap, a.UnmappedEnglishEntities, a.UnmappedRussianEntities,
        a.EnglishModelSection, a.RussianModelSection, a.ModelSectionSame, a.ModelNamesSame, a.ModelDifferences,
        a.IdenticalSlots, a.EquivalentEdits, a.StructuralEdits, a.BranchInconsistencies, a.AliasDifferences,
        a.NavigationOpcodeDifferences, a.EnglishNavigationUsable, a.RussianNavigationUsable, a.EnglishNavigation,
        a.RussianNavigation, a.NavigationDifferences, a.TextIdDifferences, a.WaitPositionsMoved, a.ExactSemanticEquivalent,
        StructuralOpcodes = a.StructuralOpcodes.Select(o => $"{o:X2}").ToArray(), a.EnglishRouteMetadataEquivalent,
        ReviewReasons = a.ReviewReasons().ToArray(),
        Decision = a.SamePrefix ? "english-stock-identity" : rows.FirstOrDefault(r => r.Field == a.FieldId)?.Review ?? "excluded",
    }, options)));

    var tsv = new List<string> { "field\tname\tdecision\tdifferent_sections\tidentical_slots\tequivalent_edits\tstructural_edits\tbranch_inconsistencies\tnavigation_output\ttext_id_differences\treasons" };
    foreach (var a in audits)
        tsv.Add(string.Join('\t', a.FieldId, a.Name,
            a.SamePrefix ? "english-stock-identity" : rows.FirstOrDefault(r => r.Field == a.FieldId)?.Review ?? "excluded",
            string.Join(',', a.DifferentSections), a.IdenticalSlots,
            string.Join(',', a.EquivalentEdits.Select(p => $"{p.Key}={p.Value}")), a.StructuralEdits.Count, a.BranchInconsistencies.Count,
            a.NavigationDifferences.Count == 0 ? "same" : "differs", a.TextIdDifferences.Count, string.Join(',', a.ReviewReasons())));
    File.WriteAllLines(Path.Combine(reportDirectory, "fields.tsv"), tsv);

    var review = new StringBuilder();
    review.AppendLine("# Russian navigation fields needing manual review");
    review.AppendLine();
    foreach (var a in queue.Concat(excluded.Select(e => e.Audit)).Distinct().OrderBy(a => a.FieldId))
    {
        review.AppendLine($"## {a.FieldId} {a.Name}");
        review.AppendLine($"- Russian prefix `{a.RussianPrefix}`; English `{a.EnglishPrefix}`");
        review.AppendLine($"- Reasons: {string.Join(", ", a.ReviewReasons())}");
        review.AppendLine($"- Decision: {rows.FirstOrDefault(r => r.Field == a.FieldId)?.Review ?? excluded.FirstOrDefault(e => e.Audit == a).Reason}");
        if (!a.EntityNamesSame)
        {
            review.AppendLine($"- English entities: {string.Join(',', a.EnglishEntities)}");
            review.AppendLine($"- Russian entities: {string.Join(',', a.RussianEntities)}");
            review.AppendLine($"- Forced map: {string.Join(',', a.EntityMap.Select(p => $"{p.Key}->{p.Value}"))}");
        }

        foreach (var line in a.ModelDifferences) review.AppendLine($"- Model: {line}");
        foreach (var line in a.StructuralEdits.Take(60)) review.AppendLine($"- Structural: `{line}`");
        if (a.StructuralEdits.Count > 60) review.AppendLine($"- ... {a.StructuralEdits.Count - 60} more structural edits in fields.jsonl");
        foreach (var line in a.BranchInconsistencies) review.AppendLine($"- Branch: `{line}`");
        foreach (var line in a.AliasDifferences) review.AppendLine($"- Aliases: `{line}`");
        foreach (var line in a.NavigationOpcodeDifferences) review.AppendLine($"- Navigation opcodes: `{line}`");
        foreach (var line in a.NavigationDifferences) review.AppendLine($"- Navigation output: `{line}`");
        if (!a.EnglishNavigationUsable || !a.RussianNavigationUsable)
            review.AppendLine($"- Unreadable: english={a.EnglishNavigationDiagnostic} russian={a.RussianNavigationDiagnostic}");
        review.AppendLine();
    }

    File.WriteAllText(Path.Combine(reportDirectory, "review-queue.md"), review.ToString());

    var summary = new StringBuilder();
    summary.AppendLine($"english {englishContext.FieldArchivePath} sha256={englishArchiveSha}");
    summary.AppendLine($"russian {russianContext.FieldArchivePath} sha256={russianArchiveSha}");
    summary.AppendLine($"reviewed navigation fields={reviewedFields} audited={audits.Count}");
    summary.AppendLine($"english-stock-identity (prefix unchanged)={audits.Count(a => a.SamePrefix)}");
    summary.AppendLine($"russian rows: semantic={rows.Count(r => r.Review == "semantic")} reviewed={rows.Count(r => r.Review == "reviewed")}");
    summary.AppendLine($"excluded={excluded.Count} review-queue={queue.Count}");
    summary.AppendLine($"russian prefixes identical to an official localized stock prefix: {string.Join(',', officialStockPrefixes)}");
    summary.AppendLine($"english disk untrusted for Russian cross-field routes ({routeUntrusted.Length}): {string.Join(',', routeUntrusted)}");
    foreach (var group in audits.Where(a => !a.SamePrefix).SelectMany(a => a.ReviewReasons()).GroupBy(r => r).OrderBy(g => g.Key))
        summary.AppendLine($"reason {group.Key}={group.Count()}");
    foreach (var group in audits.SelectMany(a => a.DifferentSections).GroupBy(s => s).OrderBy(g => g.Key))
        summary.AppendLine($"different section {group.Key}={group.Count()}");
    foreach (var group in audits.SelectMany(a => a.EquivalentEdits).GroupBy(p => p.Key).OrderBy(g => g.Key))
        summary.AppendLine($"equivalent edit {group.Key}={group.Sum(p => p.Value)} in {group.Count()} fields");
    summary.AppendLine($"fields whose text ids differ={audits.Count(a => a.TextIdDifferences.Count > 0)}; wait byte positions moved={audits.Count(a => a.WaitPositionsMoved)}");
    if (verifyRuntime) summary.AppendLine($"runtime verification failures={runtimeFailures}");
    foreach (var failure in failures) summary.AppendLine("FAIL " + failure);
    File.WriteAllText(Path.Combine(reportDirectory, "summary.txt"), summary.ToString());
}

static Dictionary<(int Field, string Prefix), Decision> ReadDecisions(string? path)
{
    var result = new Dictionary<(int Field, string Prefix), Decision>();
    if (path is null) return result;
    var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    foreach (var item in root["decisions"]!.AsArray())
    {
        var field = item!["field"]!.GetValue<int>();
        var prefix = item["russianPrefix"]!.GetValue<string>().ToUpperInvariant();
        var include = item["decision"]!.GetValue<string>() switch
        {
            "include" => true,
            "exclude" => false,
            var other => throw new InvalidDataException($"field {field}: unknown decision {other}")
        };
        SortedDictionary<int, int>? map = null;
        if (item["entityMap"] is JsonObject mapNode)
        {
            map = new SortedDictionary<int, int>();
            foreach (var (key, value) in mapNode) map[int.Parse(key)] = value!.GetValue<int>();
        }

        var evidence = item["evidence"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(evidence)) throw new InvalidDataException($"field {field}: a decision needs its evidence");
        var routes = item["englishRoutesEquivalent"]?.GetValue<bool>() ?? false;
        if (routes && (!include || map is not null)) throw new InvalidDataException($"field {field}: route equivalence cannot waive an exclusion or entity shift");
        if (!result.TryAdd((field, prefix), new Decision(include, map, evidence, routes)))
            throw new InvalidDataException($"field {field}: duplicate decision");
    }

    return result;
}

internal sealed record Decision(bool Include, SortedDictionary<int, int>? Map, string Evidence, bool EnglishRoutesEquivalent);

internal sealed record ManifestRow(int Field, string Russian, string English, string EnglishModels, string Review, SortedDictionary<int, int>? Map);
