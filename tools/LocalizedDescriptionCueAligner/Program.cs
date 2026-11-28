using System.Security.Cryptography;
using System.Text;
using Ff7.Accessibility.Reloaded;
using LocalizedDescriptionCueAligner;

// Aligns every English field audio-description anchor with the native field scripts of the official
// French, German, Spanish and Japanese archives (and, when supplied, each Polish fan translation
// release), and emits the exact per-identity map the runtime uses. --polish may be repeated: every
// release is reviewed on its own and shares the "pl" archive code. See
// docs/localized-description-triggers-20261125.md.
if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: LocalizedDescriptionCueAligner <ff7-workingdir> <report-directory> [--polish <workingdir>]... [--russian <workingdir>] [--emit <generated.cs>] [--navigation-emit <generated.cs>]");
    return 2;
}

var root = Path.GetFullPath(args[0]);
var reportDirectory = Path.GetFullPath(args[1]);
var polishRoots = new List<string>();
string? russianRoot = null;
string? emitPath = null;
string? navigationEmitPath = null;
for (var index = 2; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--polish" when index + 1 < args.Length: polishRoots.Add(Path.GetFullPath(args[++index])); break;
        case "--russian" when index + 1 < args.Length: russianRoot = Path.GetFullPath(args[++index]); break;
        case "--emit" when index + 1 < args.Length: emitPath = Path.GetFullPath(args[++index]); break;
        case "--navigation-emit" when index + 1 < args.Length: navigationEmitPath = Path.GetFullPath(args[++index]); break;
        default: Console.Error.WriteLine($"Unknown argument {args[index]}"); return 2;
    }
}

Directory.CreateDirectory(reportDirectory);
static Ff7GameLanguageContext ContextAt(Ff7GameLanguageDescriptor descriptor, string gameRoot) =>
    new(descriptor, Path.Combine(gameRoot, "data"), Ff7GameLanguageDetectionSource.Configuration, "localized cue alignment");
static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

var englishDescriptor = Ff7GameLanguages.Get(Ff7GameLanguage.English);
var targets = Ff7GameLanguages.All.Where(d => d.Language != Ff7GameLanguage.English)
    .Select(d => (Descriptor: d, Root: root))
    .Concat(polishRoots.Select(polishRoot => (Descriptor: Ff7GameLanguages.PolishFanTranslation, Root: polishRoot)))
    .Concat(russianRoot is null ? [] : [(Descriptor: Ff7GameLanguages.RussianFanTranslation, Root: russianRoot)])
    .ToArray();
var sources = new List<string>();
foreach (var (descriptor, targetRoot) in new[] { (Descriptor: englishDescriptor, Root: root) }.Concat(targets))
{
    var archive = ContextAt(descriptor, targetRoot).FieldArchivePath;
    if (!File.Exists(archive))
    {
        Console.Error.WriteLine($"Missing {descriptor.Code} field archive {archive}");
        return 1;
    }

    sources.Add($"{descriptor.Code}: {Path.GetFileName(archive)} sha256={Sha256(archive)}");
}

var catalog = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
var films = FieldMovieNarrationPolicy.All;
var fields = catalog.Select(c => c.FieldId).Concat(films.Select(f => f.FieldId)).Append(767).Distinct().Order().ToArray();
var englishData = new FlevelDataSource(root, ContextAt(englishDescriptor, root));
var englishScripts = new FieldScriptNavigationCatalog(root, ContextAt(englishDescriptor, root));
var englishFields = fields.ToDictionary(f => f, f => NativeField.TryRead(englishData, englishScripts, f));
var englishText = new FlevelFieldTextResolver(root, ContextAt(englishDescriptor, root));

var identities = new Dictionary<(int Field, string Prefix), ReviewedIdentity>();
var cueRows = new List<string> { "target\tfield\tname\tentity\tslot\tbyte\topcode\tstatus\tauthorized\tt_entity\tt_slot\tt_byte\tequivalence\tevidence\ttext" };
var filmRows = new List<string> { "target\tfield\tentity\tslot\tbyte\tmovie\tfile\tstatus\tauthorized\tt_entity\tt_slot\tt_byte\tevidence" };
var textRows = new List<string> { "target\tfield\tname\tentity\tslot\tbyte\tenglish_id\tenglish_text\ttarget_id\ttarget_text" };
var editRows = new List<string> { "target\tfield\tname\tentity\tslot\tt_entity\tsimilarity\tstructural_edits" };
var creatureRows = new List<string>();
var entityRows = new List<string> { "target\tfield\tname\tenglish_entities\ttarget_entities\tshifted_pairs" };
var summary = new StringBuilder();
var failed = false;
foreach (var (descriptor, targetRoot) in targets)
{
    var context = ContextAt(descriptor, targetRoot);
    var data = new FlevelDataSource(targetRoot, context);
    var scripts = new FieldScriptNavigationCatalog(targetRoot, context);
    var text = new FlevelFieldTextResolver(targetRoot, context);
    var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var changedFields = 0;
    foreach (var fieldId in fields)
    {
        var english = englishFields[fieldId];
        var target = NativeField.TryRead(data, scripts, fieldId);
        var fieldCues = catalog.Where(c => c.FieldId == fieldId).ToArray();
        if (english is null || target is null)
        {
            foreach (var cue in fieldCues)
                counts[english is null ? "english-unreadable" : "target-unreadable"] = counts.GetValueOrDefault(english is null ? "english-unreadable" : "target-unreadable") + 1;
            if (fieldCues.Length > 0) failed = true;
            continue;
        }

        var aligner = new CueAligner(english, target);
        if (descriptor.Code == "ru" && fieldId == 700)
        {
            File.WriteAllLines(Path.Combine(reportDirectory, "russian-crater-area-entry.tsv"),
                new[] { "language\tfield\tentity\tslot\tbyte\topcode\thex" }.Concat(
                    new[] { (Code: "en", Field: english), (Code: "ru", Field: target) }
                        .SelectMany(item => item.Field.Slot(1, 0)!.Select(op =>
                            $"{item.Code}\t700\t1\t0\t{op.ByteIndex}\t{op.Opcode:X2}\t{op.Hex}"))));
        }
        if (fieldCues.Length > 0 && !english.EntityNames.SequenceEqual(target.EntityNames, StringComparer.Ordinal))
            entityRows.Add(string.Join('\t', descriptor.Code, fieldId, english.Name, string.Join(',', english.EntityNames),
                string.Join(',', target.EntityNames),
                string.Join(',', aligner.EntityMap.Where(p => p.Key != p.Value).Select(p => $"{p.Key}->{p.Value}"))));
        ReviewedIdentity? identity = null;
        if (!aligner.SamePrefix && fieldCues.Length > 0)
        {
            changedFields++;
            identity = new ReviewedIdentity(fieldId, target.PrefixSha256);
            identity.Languages.Add(descriptor.Code);
        }

        foreach (var cue in fieldCues)
        {
            var result = aligner.Align(cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode);
            // Reviewed separately: FFRTT's crater_1 removes the English party
            // mask initialization after the room-entry instruction. The loaded
            // prefix, entry sequence, camera/models/palette/walkmesh/triggers and
            // background image were checked; do not relax whole-slot alignment.
            if (descriptor.Code == "ru" && fieldId == 700 && cue.EntityId == 1 &&
                cue.ScriptId == 0 && cue.ByteIndex == 0 && cue.Opcode == 0x43 &&
                target.PrefixSha256 == "2AEFA4D8984F70EC75FD5D5131E3E896AC30A08DDED69E2752CD324B4BC3336B" &&
                target.Slot(1, 0)!.Take(3).Select(op => op.Hex).SequenceEqual(new[] { "4300", "F000", "F601" }))
                result = new AnchorResult("reviewed-russian-area-entry", true, 1, 0, 0, Equivalence.Exact,
                    "exact reviewed crater_1 prefix; unconditional MPNAM 4300 before unchanged fade; see docs/russian-support-20261127.md");
            counts[result.Status] = counts.GetValueOrDefault(result.Status) + 1;
            var evidence = result.Evidence;
            if (result.Authorized && aligner.RequestTargetEvidence(cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode) is { Length: > 0 } requested)
                evidence = (evidence + " " + requested).Trim();
            if (identity is not null)
            {
                if (result.Authorized)
                    identity.Cues.Add(new IdentityCue(cue, result.Entity!.Value, result.Script!.Value, result.ByteIndex!.Value));
                else
                    identity.Exclusions.Add((cue, result.Status));
            }

            cueRows.Add(string.Join('\t', descriptor.Code, fieldId, english.Name, cue.EntityId, cue.ScriptId, cue.ByteIndex,
                $"0x{cue.Opcode:X2}", result.Status, result.Authorized || result.Status == "identical-prefix",
                result.Entity?.ToString() ?? "", result.Script?.ToString() ?? "", result.ByteIndex?.ToString() ?? "",
                result.Equivalence, evidence, cue.Text.Replace('\t', ' ')));
            if (result.Equivalence == Equivalence.Text && cue.Opcode == 0x43 && result.Authorized)
            {
                var englishId = english.Slot(cue.EntityId, cue.ScriptId)!.First(i => i.ByteIndex == cue.ByteIndex).Bytes[1];
                var targetId = target.Slot(result.Entity!.Value, result.Script!.Value)!.First(i => i.ByteIndex == result.ByteIndex).Bytes[1];
                textRows.Add(string.Join('\t', descriptor.Code, fieldId, english.Name, cue.EntityId, cue.ScriptId, cue.ByteIndex,
                    englishId, Clean(englishText.ReadMessageById(fieldId, englishId).Text), targetId,
                    Clean(text.ReadMessageById(fieldId, targetId).Text)));
            }
        }

        foreach (var film in films.Where(f => f.FieldId == fieldId))
        {
            var result = aligner.Align(film.EntityId, film.ScriptId, film.ByteIndex, 0xF9);
            filmRows.Add(string.Join('\t', descriptor.Code, fieldId, film.EntityId, film.ScriptId, film.ByteIndex, film.MovieNumber,
                film.FileName, result.Status, result.Authorized || result.Status == "identical-prefix", result.Entity?.ToString() ?? "",
                result.Script?.ToString() ?? "", result.ByteIndex?.ToString() ?? "", result.Evidence));
        }

        if (fieldId == 767 && !aligner.SamePrefix)
        {
            var warning = CreatureWarningEvidence(aligner, out var creatureEvidence);
            creatureRows.Add($"{descriptor.Code}\t767\t{target.PrefixSha256}\t{warning}\t{creatureEvidence}");
            if (identity is not null) identity.CreatureWarning = warning;
        }

        foreach (var (entity, slot) in fieldCues.Select(c => (c.EntityId, c.ScriptId)).Distinct())
        {
            if (aligner.SamePrefix || aligner.Compare(entity, slot) is not { Identical: false } comparison) continue;
            var structural = comparison.StructuralEdits.Select(e => e.ToString()).ToArray();
            editRows.Add(string.Join('\t', descriptor.Code, fieldId, english.Name, entity, slot,
                aligner.EntityMap.GetValueOrDefault(entity, -1), comparison.Similarity.ToString("0.000"),
                structural.Length == 0 ? "presentation-only" : string.Join(';', structural)));
        }

        if (identity is null) continue;
        if (identities.TryGetValue((fieldId, identity.PrefixSha256), out var existing))
        {
            // Two archives with the same loaded prefix must have produced the same decisions.
            if (existing.Signature != identity.Signature)
            {
                Console.Error.WriteLine($"Field {fieldId} prefix {identity.PrefixSha256} disagrees between {string.Join('|', existing.Languages)} and {descriptor.Code}");
                failed = true;
            }

            existing.Languages.Add(descriptor.Code);
        }
        else
        {
            identities.Add((fieldId, identity.PrefixSha256), identity);
        }
    }

    var line = $"{descriptor.Code}: anchors={catalog.Count} changed-prefix-fields={changedFields} " +
               string.Join(' ', counts.Select(p => $"{p.Key}={p.Value}"));
    Console.WriteLine(line);
    summary.AppendLine(line);
}

File.WriteAllLines(Path.Combine(reportDirectory, "cue-alignment.tsv"), cueRows);
File.WriteAllLines(Path.Combine(reportDirectory, "film-anchors.tsv"), filmRows);
File.WriteAllLines(Path.Combine(reportDirectory, "mpnam-text-evidence.tsv"), textRows);
File.WriteAllLines(Path.Combine(reportDirectory, "edited-cue-slots.tsv"), editRows);
File.WriteAllLines(Path.Combine(reportDirectory, "creature-warning.tsv"), creatureRows);
File.WriteAllLines(Path.Combine(reportDirectory, "entity-maps.tsv"), entityRows);
summary.AppendLine($"identities={identities.Count} authorized-rows={identities.Values.Sum(i => i.Cues.Count)} " +
                   $"exclusions={identities.Values.Sum(i => i.Exclusions.Count)} catalog-fingerprint={ManifestWriter.CatalogFingerprint(catalog)}");
foreach (var source in sources) summary.AppendLine(source);
File.WriteAllText(Path.Combine(reportDirectory, "summary.txt"), summary.ToString());
Console.WriteLine(summary.ToString().TrimEnd().Split('\n')[^(sources.Count + 1)]);
if (emitPath is not null)
{
    File.WriteAllText(emitPath, ManifestWriter.Write(identities.Values.ToArray(), catalog,
        sources.Prepend($"Command: LocalizedDescriptionCueAligner <workingdir> <reports>{string.Concat(polishRoots.Select(_ => " --polish <Polish fan translation workingdir>"))}{(russianRoot is null ? "" : " --russian <FFRTT 1.02 workingdir>")} --emit <this file>").ToArray()),
        new UTF8Encoding(false));
    Console.WriteLine($"Wrote {emitPath}");
}

// Navigation review for every field of every Polish release: a Polish prefix may use the
// English navigation review only when its script is the English script up to presentation
// (NavigationEquivalence). The official archives are compared the same way as a calibration.
if (navigationEmitPath is not null)
{
    var navigationRows = new List<string> { "target\tfield\tname\tstatus\tequivalent\tevidence" };
    var polishNavigation = new SortedSet<(int Field, string Prefix)>();
    var officialStock = 0;
    var officialRejected = new List<string>();
    var allFields = englishData.FieldNames.Keys.Order().ToArray();
    var englishAll = allFields.ToDictionary(f => f, f => NativeField.TryRead(englishData, englishScripts, f));
    var navigationSummary = new StringBuilder();
    foreach (var (descriptor, targetRoot) in targets)
    {
        var context = ContextAt(descriptor, targetRoot);
        var data = new FlevelDataSource(targetRoot, context);
        var scripts = new FieldScriptNavigationCatalog(targetRoot, context);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var fieldId in allFields)
        {
            var english = englishAll[fieldId];
            var target = NativeField.TryRead(data, scripts, fieldId);
            if (english is null || target is null)
            {
                counts["unreadable"] = counts.GetValueOrDefault("unreadable") + 1;
                continue;
            }

            var result = NavigationEquivalence.Compare(english, target);
            counts[result.Status] = counts.GetValueOrDefault(result.Status) + 1;
            navigationRows.Add(string.Join('\t', descriptor.Code, fieldId, english.Name, result.Status, result.Equivalent, Clean(result.Evidence)));
            var reviewed = WholeGameEchoNavigationManifest.SourceKnownExactPrefixHash(fieldId, target.PrefixSha256);
            if (descriptor.Code != "pl")
            {
                if (!reviewed) continue;
                officialStock++;
                if (!result.Equivalent) officialRejected.Add($"{descriptor.Code} {fieldId} {english.Name} {result.Status}");
                continue;
            }

            // A Polish prefix equal to an official one is already a navigation identity.
            if (result.Equivalent && result.Status != "identical-prefix" &&
                !WholeGameEchoNavigationManifest.IsOfficialOrModIdentity(new LoadedFieldScriptIdentity(fieldId, target.PrefixSha256)))
                polishNavigation.Add((fieldId, target.PrefixSha256));
        }

        navigationSummary.AppendLine($"{descriptor.Code} navigation: " + string.Join(' ', counts.Select(p => $"{p.Key}={p.Value}")));
    }

    navigationSummary.AppendLine($"official reviewed prefixes compared={officialStock} rejected-by-this-rule={officialRejected.Count}");
    foreach (var rejected in officialRejected) navigationSummary.AppendLine("  " + rejected);
    navigationSummary.AppendLine($"polish navigation identities={polishNavigation.Count}");
    File.WriteAllLines(Path.Combine(reportDirectory, "navigation-equivalence.tsv"), navigationRows);
    File.WriteAllText(Path.Combine(reportDirectory, "navigation-summary.txt"), navigationSummary.ToString());
    Console.Write(navigationSummary.ToString());

    var navigation = new StringBuilder();
    navigation.AppendLine("// <auto-generated>");
    navigation.AppendLine("// Generated by tools/LocalizedDescriptionCueAligner --navigation-emit. Do not edit; regenerate instead.");
    navigation.AppendLine("// Polish fan translation field prefixes with the English stock entity layout whose slots stay the English");
    navigation.AppendLine("// code (NavigationEquivalence), so the English navigation review applies as it does to the official archives.");
    foreach (var source in sources.Where(s => s.StartsWith("en:", StringComparison.Ordinal) || s.StartsWith("pl:", StringComparison.Ordinal)))
        navigation.AppendLine("// " + source);
    navigation.AppendLine($"// Identities: {polishNavigation.Count}.");
    navigation.AppendLine("// </auto-generated>");
    navigation.AppendLine("namespace Ff7.Accessibility.Reloaded;");
    navigation.AppendLine();
    navigation.AppendLine("public static partial class PolishNavigationManifest");
    navigation.AppendLine("{");
    navigation.AppendLine("    // field,exact loaded prefix SHA-256 (same entity layout as the English stock script)");
    navigation.AppendLine("    private const string RawIdentities = \"\"\"");
    foreach (var (fieldId, prefix) in polishNavigation)
        navigation.AppendLine($"{fieldId},{prefix}");
    navigation.AppendLine("\"\"\";");
    navigation.AppendLine("}");
    File.WriteAllText(navigationEmitPath, navigation.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
    Console.WriteLine($"Wrote {navigationEmitPath}");
}

return failed ? 1 : 0;

static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

// FieldCreatureWarningReader reads the warning MESSAGE at 767 entity 3 slot 6 byte 44 (window 1,
// dialog 10 in stock) and FieldCreatureApproachReader requires creature model slot 8 for entity 12.
// A target identity keeps both only when that MESSAGE is the identical instruction at the same
// coordinates, the entity list is unchanged, and every model-assigning CHAR or PC is forced exact
// (model slots are handed out by CHAR in load order).
static bool CreatureWarningEvidence(CueAligner aligner, out string evidence)
{
    var message = aligner.Align(3, 6, 44, 0x40);
    var english = aligner.English.Slot(3, 6)?.FirstOrDefault(i => i.ByteIndex == 44);
    var target = message.Entity is { } e && message.Script is { } s ? aligner.Target.Slot(e, s)?.FirstOrDefault(i => i.ByteIndex == message.ByteIndex) : null;
    var sameMessage = message.Authorized && message.Entity == 3 && message.Script == 6 && message.ByteIndex == 44 &&
                      english is not null && target is not null && english.Bytes.AsSpan().SequenceEqual(target.Bytes) &&
                      english.Hex == "40010A";
    var sameEntities = aligner.English.EntityNames.SequenceEqual(aligner.Target.EntityNames, StringComparer.Ordinal);
    var models = true;
    for (var entity = 0; entity < aligner.English.EntityCount && sameEntities; entity++)
    {
        var comparison = aligner.Compare(entity, 0);
        var englishSlot = aligner.English.Slot(entity, 0) ?? [];
        var targetSlot = aligner.Target.Slot(entity, 0) ?? [];
        for (var i = 0; i < englishSlot.Count; i++)
        {
            if (englishSlot[i].Opcode is not (0xA0 or 0xA1)) continue;
            models &= comparison?.Forced[i] is { } j && comparison.Alignment.At(i, j) == Equivalence.Exact;
        }

        var images = comparison?.Forced.OfType<int>().ToHashSet() ?? [];
        for (var j = 0; j < targetSlot.Count; j++)
            if (targetSlot[j].Opcode is 0xA0 or 0xA1) models &= images.Contains(j);
    }

    evidence = $"message={message.Status}:{english?.Hex}->{target?.Hex}@{message.Entity}/{message.Script}/{message.ByteIndex} " +
               $"entities={(sameEntities ? "same" : "differ")} models={(models ? "forced-exact" : "differ")}";
    return sameMessage && sameEntities && models;
}
