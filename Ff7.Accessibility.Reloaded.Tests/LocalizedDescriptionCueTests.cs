using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Field audio-description triggers beyond English: the generated exact-identity manifest, the
/// runtime tracker and canonical keys, the creature gates and, with installed archives, every
/// official French, German, Spanish and Japanese field and the Bunio Polish V1.05 flevel.
/// </summary>
internal static class LocalizedDescriptionCueTests
{
    private static readonly string[] Official = ["fr", "de", "es", "ja"];

    public static void Run()
    {
        var tests = new Action[]
        {
            GeneratedManifestMatchesTheCurrentEnglishCatalog,
            EveryOfficialLocalizedPrefixOfEveryCueFieldIsReviewedWithEveryCue,
            EntityShiftsAgreeWithTheReviewedNavigationEntityMaps,
            EveryAuthorizedRowDeliversItsEnglishCueOnceAtItsLoadedCoordinates,
            ExcludedAndUnknownIdentitiesNeverAuthorizeEnglishOffsets,
            RecurringGroupsReleaseOnTheLocalizedNativeReentry,
            AnotherCatalogsFieldInBetweenIsANewVisit,
            ReviewedCreatureWarningsKeepTheStockMessage
        };
        var failures = new List<string>();
        foreach (var test in tests)
        {
            try { test(); Console.WriteLine($"PASS {test.Method.Name}"); }
            catch (Exception error) { failures.Add($"{test.Method.Name}: {error.Message}"); Console.WriteLine($"FAIL {test.Method.Name}: {error.Message}"); }
        }

        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var polish = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_POLISH_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            Console.WriteLine("Localized cue installed-archive checks require FF7_ACCESSIBILITY_DATA_ROOT (and FF7_ACCESSIBILITY_POLISH_DATA_ROOT for Polish).");
        else
        {
            try { InstalledArchivesRunEveryReviewedCue(root, string.IsNullOrWhiteSpace(polish) ? null : polish); }
            catch (Exception error) { failures.Add($"{nameof(InstalledArchivesRunEveryReviewedCue)}: {error.Message}"); Console.WriteLine($"FAIL installed archives: {error.Message}"); }
        }

        if (failures.Count > 0) throw new InvalidOperationException("Localized description cue failures:\n" + string.Join('\n', failures));
        Console.WriteLine("Localized description cue tests passed.");
    }

    private static IReadOnlyList<FieldCutsceneDescriptionCue> Catalog => FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();

    private static void GeneratedManifestMatchesTheCurrentEnglishCatalog()
    {
        Equal(0, LocalizedFieldDescriptionCueManifest.InvalidRows.Count,
            "every generated row parses and names an English cue: " + string.Join("; ", LocalizedFieldDescriptionCueManifest.InvalidRows.Take(5)));
        var catalog = Catalog;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join('\n', catalog
            .Select(c => $"{c.FieldId},{c.EntityId},{c.ScriptId},{c.ByteIndex},{c.Opcode}").Order(StringComparer.Ordinal)))));
        Equal(LocalizedFieldDescriptionCueManifest.GeneratedCatalogAnchorCount, catalog.Count,
            "English anchors changed: regenerate with tools/LocalizedDescriptionCueAligner");
        Equal(LocalizedFieldDescriptionCueManifest.GeneratedCatalogFingerprint, fingerprint,
            "English anchors changed: regenerate with tools/LocalizedDescriptionCueAligner");
        var identities = LocalizedFieldDescriptionCueManifest.ReviewedIdentities;
        Equal(2964, identities.Count, "reviewed exact identities");
        Equal(4539, identities.Sum(i => i.Cues.Count), "authorized localized cue rows");
        Equal(1, identities.Sum(i => i.ExcludedEnglishKeys.Count), "explicit exclusions");
        foreach (var identity in identities)
        {
            Equal(identity.Cues.Count, identity.CanonicalKeys.Count, "each authorized row has one canonical key");
            Equal(identity.Cues.Count, identity.Cues.Select(c => c.Key).Distinct().Count(), "no two rows share loaded coordinates");
            True(identity.Languages.Count > 0 && identity.Languages.All(l => Official.Contains(l) || l == "pl"), "reviewed archive codes");
            True(!string.Equals(identity.PrefixSha256, EchoSCompatibilityManifest.GetVanillaFingerprint(identity.FieldId), StringComparison.OrdinalIgnoreCase),
                "the English stock prefix needs no localized row");
        }
    }

    private static void EveryOfficialLocalizedPrefixOfEveryCueFieldIsReviewedWithEveryCue()
    {
        var catalog = Catalog.GroupBy(c => c.FieldId).ToDictionary(g => g.Key, g => g.ToArray());
        var official = OfficialPrefixes();
        var checkedPrefixes = 0;
        foreach (var (field, cues) in catalog)
        {
            True(official.TryGetValue(field, out var row), $"field {field} has reviewed official navigation identities");
            foreach (var prefix in row.Official.Where(p => !string.Equals(p, row.Stock, StringComparison.OrdinalIgnoreCase)))
            {
                var identity = new LoadedFieldScriptIdentity(field, prefix);
                True(LocalizedFieldDescriptionCueManifest.IsReviewedIdentity(identity), $"field {field} official localized prefix {prefix[..12]} is reviewed");
                var languages = LocalizedFieldDescriptionCueManifest.ResolveLanguages(identity);
                True(languages.Any(Official.Contains), $"field {field} {prefix[..12]} names its official archive");
                var mapped = LocalizedFieldDescriptionCueManifest.CreateDescriptions(identity);
                Equal(cues.Length, mapped.Count, $"field {field} {string.Join('|', languages)}: every English cue runs in the official localized script");
                Equal(SupportedFieldScriptVariant.Vanilla, WholeGameEchoNavigationManifest.ResolveVariant(identity), "navigation still knows it as official stock");
                Equal(SupportedFieldScriptVariant.Unknown, EchoSCompatibilityManifest.ResolveVariant(identity), "English offsets stay unauthorized for it");
                checkedPrefixes++;
            }
        }

        foreach (var identity in LocalizedFieldDescriptionCueManifest.ReviewedIdentities)
        {
            var loaded = new LoadedFieldScriptIdentity(identity.FieldId, identity.PrefixSha256);
            var isOfficial = official.TryGetValue(identity.FieldId, out var row) && row.Official.Contains(identity.PrefixSha256, StringComparer.OrdinalIgnoreCase);
            Equal(identity.Languages.Any(Official.Contains), isOfficial, $"field {identity.FieldId} {identity.PrefixSha256[..12]} official membership");
            // Polish V1.05 prefixes are exact description identities, not navigation identities:
            // both hosts deliver them through LocalizedFieldScriptIdentityPolicy.AllowsDescriptions.
            if (!isOfficial) Equal(SupportedFieldScriptVariant.Unknown, WholeGameEchoNavigationManifest.ResolveVariant(loaded), "Polish prefixes are not navigation identities");
        }

        Equal(2378, checkedPrefixes, "official localized cue-field prefixes");
    }

    private static void EntityShiftsAgreeWithTheReviewedNavigationEntityMaps()
    {
        var shifted = 0;
        foreach (var identity in LocalizedFieldDescriptionCueManifest.ReviewedIdentities)
        {
            foreach (var cue in identity.Cues)
            {
                var english = identity.CanonicalKeys[cue.Key];
                if (english.EntityId == cue.EntityId) continue;
                var map = WholeGameEchoNavigationManifest.ReviewedOfficialStockEntityMap(new LoadedFieldScriptIdentity(identity.FieldId, identity.PrefixSha256));
                True(map is not null && map.TryGetValue(english.EntityId, out var loaded) && loaded == cue.EntityId,
                    $"field {identity.FieldId} {english.EntityId}->{cue.EntityId} agrees with the reviewed navigation entity map");
                shifted++;
            }
        }

        Equal(6, shifted, "woa_1 zcheck insertion: two cues in each European archive");
    }

    private static void EveryAuthorizedRowDeliversItsEnglishCueOnceAtItsLoadedCoordinates()
    {
        var english = Catalog.ToDictionary(c => c.Key);
        var delivered = 0;
        foreach (var identity in LocalizedFieldDescriptionCueManifest.ReviewedIdentities)
        {
            var loaded = new LoadedFieldScriptIdentity(identity.FieldId, identity.PrefixSha256);
            var tracker = new EchoSFieldCutsceneDescriptionTracker();
            var targets = identity.Cues.Select(c => c.Key).ToHashSet();
            foreach (var cue in identity.Cues)
            {
                var canonical = english[identity.CanonicalKeys[cue.Key]];
                Equal(canonical.Text, cue.Text, "English words are kept for the translation step");
                Equal(canonical.Opcode, cue.Opcode, "same native opcode");
                Equal((canonical.RecurringGroup, canonical.StartsRecurringGroup, canonical.ObservationOnly),
                    (cue.RecurringGroup, cue.StartsRecurringGroup, cue.ObservationOnly), "recurring ownership survives");
                var context = new FieldScriptContext(cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode);
                Equal(canonical.Key, EchoSCompatibilityManifest.ResolveCanonicalCueKey(context, loaded), "canonical English key and history");
                Equal(true, tracker.Observe(context with { Opcode = cue.Opcode ^ 0xFF }, loaded) is null, "another opcode at the reviewed byte is silent");
                Equal(cue, tracker.Observe(context, loaded), $"field {cue.FieldId} {cue.Key} delivers its cue");
                Equal(true, tracker.Observe(context, loaded) is null, "repeated native delivery stays silent");
                if (canonical.Key != cue.Key && !targets.Contains(canonical.Key))
                    Equal(true, tracker.Observe(context with { EntityId = canonical.EntityId, ScriptId = canonical.ScriptId, ByteIndex = canonical.ByteIndex }, loaded) is null,
                        "English coordinates never stand in for moved localized ones");
                delivered++;
            }
        }

        Equal(4539, delivered, "every authorized row was delivered");
    }

    private static void ExcludedAndUnknownIdentitiesNeverAuthorizeEnglishOffsets()
    {
        var excluded = LocalizedFieldDescriptionCueManifest.ReviewedIdentities.Single(i => i.ExcludedEnglishKeys.Count > 0);
        Equal((700, "pl"), (excluded.FieldId, string.Join('|', excluded.Languages)), "the one exclusion is Polish crater_1");
        var key = excluded.ExcludedEnglishKeys.Single();
        Equal(new FieldCutsceneDescriptionKey(700, 1, 0, 0), key, "its MPNAM slot is mostly different code");
        var loaded = new LoadedFieldScriptIdentity(excluded.FieldId, excluded.PrefixSha256);
        var tracker = new EchoSFieldCutsceneDescriptionTracker();
        var context = new FieldScriptContext(700, 1, 0, 0, FieldOpcodeAddressResolver.OpcodeMapNameIndex);
        Equal(true, tracker.Observe(context, loaded) is null, "an excluded cue is silent");
        Equal(true, EchoSCompatibilityManifest.ResolveCanonicalCueKey(context, loaded) is null, "an excluded cue has no canonical key");

        var sample = LocalizedFieldDescriptionCueManifest.ReviewedIdentities.First(i => i.Cues.Count > 0);
        var cue = sample.Cues[0];
        var near = new LoadedFieldScriptIdentity(sample.FieldId, sample.PrefixSha256[..^1] + (sample.PrefixSha256[^1] == '0' ? '1' : '0'));
        var nativeContext = new FieldScriptContext(cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode);
        Equal(false, LocalizedFieldDescriptionCueManifest.IsReviewedIdentity(near), "one changed prefix digit is unreviewed");
        Equal(true, new EchoSFieldCutsceneDescriptionTracker().Observe(nativeContext, near) is null, "an unreviewed prefix is silent");
        Equal(true, EchoSCompatibilityManifest.ResolveCanonicalCueKey(nativeContext, near) is null, "an unreviewed prefix has no canonical key");
        Equal(true, new EchoSFieldCutsceneDescriptionTracker().Observe(nativeContext with { FieldId = cue.FieldId + 1 },
            new LoadedFieldScriptIdentity(cue.FieldId, sample.PrefixSha256)) is null, "the observed field must be the identity's field");
    }

    private static void RecurringGroupsReleaseOnTheLocalizedNativeReentry()
    {
        var (identity, start) = LocalizedFieldDescriptionCueManifest.ReviewedIdentities
            .SelectMany(i => i.Cues.Where(c => c.StartsRecurringGroup).Select(c => (Identity: i, Cue: c)))
            .First();
        var loaded = new LoadedFieldScriptIdentity(identity.FieldId, identity.PrefixSha256);
        var tracker = new EchoSFieldCutsceneDescriptionTracker();
        var context = new FieldScriptContext(start.FieldId, start.EntityId, start.ScriptId, start.ByteIndex, start.Opcode);
        Equal(start, tracker.Observe(context, loaded), "the repeatable action is described");
        Equal(true, tracker.Observe(context, loaded) is null, "a yielding opcode re-delivered in place is not a new action");
        Equal(true, tracker.Observe(context with { ByteIndex = start.ByteIndex + 1, Opcode = 0 }, loaded) is null, "the script moves on");
        Equal(start, tracker.Observe(context, loaded), "the native re-entry releases the localized group");
    }

    private static void AnotherCatalogsFieldInBetweenIsANewVisit()
    {
        var stock = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions().First(c => c.FieldId == 116 && c.Opcode == FieldOpcodeAddressResolver.OpcodeMapNameIndex);
        var stockIdentity = new LoadedFieldScriptIdentity(116, EchoSCompatibilityManifest.GetVanillaFingerprint(116)!);
        var localized = LocalizedFieldDescriptionCueManifest.ReviewedIdentities.First(i => i.FieldId != 116 && i.Cues.Count > 0);
        var localizedIdentity = new LoadedFieldScriptIdentity(localized.FieldId, localized.PrefixSha256);
        var localizedCue = localized.Cues[0];
        FieldScriptContext Context(FieldCutsceneDescriptionCue cue) => new(cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode);
        var tracker = new EchoSFieldCutsceneDescriptionTracker();
        Equal(stock, tracker.Observe(Context(stock), stockIdentity), "a stock-identical room");
        Equal(localizedCue, tracker.Observe(Context(localizedCue), localizedIdentity), "then a reviewed localized room");
        Equal(stock, tracker.Observe(Context(stock), stockIdentity), "returning to the stock room is a new visit");
        Equal(localizedCue, tracker.Observe(Context(localizedCue), localizedIdentity), "returning to the localized room is a new visit");
        Equal(true, tracker.Observe(Context(localizedCue), localizedIdentity) is null, "within one visit it stays once-only");
    }

    private static void ReviewedCreatureWarningsKeepTheStockMessage()
    {
        var creature = LocalizedFieldDescriptionCueManifest.ReviewedIdentities.Where(i => i.FieldId == 767).ToArray();
        Equal("de|es|fr|ja|pl", string.Join('|', creature.SelectMany(i => i.Languages).Order(StringComparer.Ordinal)), "every reviewed 767 archive");
        foreach (var identity in creature)
        {
            var loaded = new LoadedFieldScriptIdentity(767, identity.PrefixSha256);
            True(identity.CreatureWarning && LocalizedFieldDescriptionCueManifest.HasReviewedCreatureWarning(loaded), $"{identity.Languages[0]} stock warning MESSAGE");
            foreach (var key in new[] { new FieldCutsceneDescriptionKey(767, 12, 3, 12), new FieldCutsceneDescriptionKey(767, 12, 4, 22) })
                Equal(key, EchoSCompatibilityManifest.ResolveCanonicalCueKey(new FieldScriptContext(767, key.EntityId, key.ScriptId, key.ByteIndex, 0xC4), loaded),
                    "the creature episodes keep their stock coordinates and keys");
        }

        Equal(false, LocalizedFieldDescriptionCueManifest.HasReviewedCreatureWarning(new LoadedFieldScriptIdentity(767, new string('0', 64))), "unknown 767 prefix");
        Equal(false, LocalizedFieldDescriptionCueManifest.HasReviewedCreatureWarning(new LoadedFieldScriptIdentity(766, creature[0].PrefixSha256)), "another field");
    }

    private static void InstalledArchivesRunEveryReviewedCue(string root, string? polishRoot)
    {
        var catalog = Catalog;
        var byField = catalog.GroupBy(c => c.FieldId).ToDictionary(g => g.Key, g => g.ToArray());
        var targets = Ff7GameLanguages.All.Where(d => d.Language != Ff7GameLanguage.English).Select(d => (Descriptor: d, Root: root)).ToList();
        if (polishRoot is not null) targets.Add((Ff7GameLanguages.PolishFanTranslation, polishRoot));
        foreach (var (descriptor, targetRoot) in targets)
        {
            var language = new Ff7GameLanguageContext(descriptor, Path.Combine(targetRoot, "data"), Ff7GameLanguageDetectionSource.Configuration, "localized cue regression");
            if (!File.Exists(language.FieldArchivePath)) throw new InvalidOperationException($"missing {language.FieldArchivePath}");
            var source = new FlevelDataSource(targetRoot, language);
            var scripts = new FieldScriptNavigationCatalog(targetRoot, language);
            var reviewed = LocalizedFieldDescriptionCueManifest.ReviewedIdentities.Where(i => i.Languages.Contains(descriptor.Code))
                .Select(i => $"{i.FieldId}:{i.PrefixSha256}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int stock = 0, localized = 0, excluded = 0;
            foreach (var (field, cues) in byField)
            {
                var section = ReadSection(source, field);
                Equal(true, new LoadedFieldScriptIdentityReader(new SectionMemory(field, section)).TryRead(out var identity), $"{descriptor.Code} {field} loaded identity");
                if (string.Equals(identity.ScriptPrefixSha256, EchoSCompatibilityManifest.GetVanillaFingerprint(field), StringComparison.OrdinalIgnoreCase))
                {
                    Equal(SupportedFieldScriptVariant.Vanilla, EchoSCompatibilityManifest.ResolveVariant(identity), "stock-identical prefix keeps English offsets");
                    stock += cues.Length;
                    continue;
                }

                True(LocalizedFieldDescriptionCueManifest.ResolveLanguages(identity).Contains(descriptor.Code), $"{descriptor.Code} field {field} installed prefix is reviewed");
                seen.Add($"{field}:{identity.ScriptPrefixSha256}");
                var native = scripts.ReadAllScriptOpcodes(field).ToDictionary(s => (s.EntityId, s.ScriptId), s => s.Opcodes);
                var tracker = new EchoSFieldCutsceneDescriptionTracker();
                var mapped = LocalizedFieldDescriptionCueManifest.CreateDescriptions(identity);
                foreach (var cue in mapped)
                {
                    var slot = FirstSlotWithPointer(section, cue.EntityId, cue.ScriptId);
                    True(native.TryGetValue((cue.EntityId, slot), out var opcodes) &&
                         opcodes.Any(o => o.ByteIndex == cue.ByteIndex && o.Opcode == cue.Opcode),
                        $"{descriptor.Code} field {field} {cue.Key} is the installed native instruction");
                    var context = new FieldScriptContext(field, cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode);
                    Equal(cue, tracker.Observe(context, identity), $"{descriptor.Code} field {field} {cue.Key} delivers");
                }

                var exclusions = LocalizedFieldDescriptionCueManifest.ReviewedIdentities
                    .Single(i => i.FieldId == field && string.Equals(i.PrefixSha256, identity.ScriptPrefixSha256, StringComparison.OrdinalIgnoreCase))
                    .ExcludedEnglishKeys.Count;
                Equal(cues.Length, mapped.Count + exclusions, $"{descriptor.Code} field {field} every English cue is mapped or explicitly excluded");
                localized += mapped.Count;
                excluded += exclusions;
            }

            Equal(catalog.Count, stock + localized + excluded, $"{descriptor.Code} complete coverage");
            Equal(descriptor.Code == "pl" ? 1 : 0, excluded, $"{descriptor.Code} exclusions");
            True(reviewed.SetEquals(seen), $"{descriptor.Code}: the manifest's identities are exactly the installed changed prefixes");
            var warning = FieldCreatureWarningReaderTests.ReadInstalledWarning(ReadSection(source, 767), descriptor.Code);
            True(warning is { DialogId: 10 } w && !string.IsNullOrWhiteSpace(w.Text), $"{descriptor.Code} creature warning page is read from the loaded table");
            Console.WriteLine($"PASS localized cues {descriptor.Code}: stock-identical {stock}, localized {localized}, excluded {excluded}, identities {seen.Count}");
        }
    }

    private static byte[] ReadSection(FlevelDataSource source, int field)
    {
        True(source.TryReadField(field, out var encoded), $"field {field} exists");
        var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
        var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
        return decoded.AsSpan(at + 4, length).ToArray();
    }

    // FieldScriptNavigationCatalog lists a shared slot start under its first slot; the runtime
    // reports the slot that ran, from the same base.
    private static int FirstSlotWithPointer(byte[] section, int entity, int slot)
    {
        var table = 32 + section[2] * 8 + BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(6)) * 4;
        int Pointer(int s) => BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(table + (entity * 32 + s) * 2));
        var pointer = Pointer(slot);
        return Enumerable.Range(0, slot + 1).First(s => Pointer(s) == pointer);
    }

    private static Dictionary<int, (string Stock, string[] Official)> OfficialPrefixes()
    {
        var layouts = (IEnumerable)typeof(WholeGameEchoNavigationManifest)
            .GetField("Layouts", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var result = new Dictionary<int, (string, string[])>();
        foreach (var pair in layouts)
        {
            var row = pair.GetType().GetProperty("Value")!.GetValue(pair)!;
            var type = row.GetType();
            result[(int)type.GetProperty("Field")!.GetValue(row)!] =
                ((string)type.GetProperty("Stock")!.GetValue(row)!, (string[])type.GetProperty("OfficialStock")!.GetValue(row)!);
        }

        return result;
    }

    private sealed class SectionMemory : ILegacyAddressSpace
    {
        private const uint Script = 0x02000000;
        private readonly byte[] section;
        private readonly Dictionary<uint, byte> globals = new();

        public SectionMemory(int field, byte[] section)
        {
            this.section = section;
            globals[(uint)FieldScriptContextReader.AddressCurrentModule] = 1;
            Put((uint)FieldScriptContextReader.AddressCurrentFieldId, BitConverter.GetBytes((ushort)field));
            Put((uint)FieldScriptContextReader.AddressFieldScriptPtr, BitConverter.GetBytes(Script));
        }

        public bool TryRead(uint address, Span<byte> destination)
        {
            if (address >= Script && address - Script + (ulong)destination.Length <= (ulong)section.Length)
            {
                section.AsSpan((int)(address - Script), destination.Length).CopyTo(destination);
                return true;
            }

            for (var i = 0; i < destination.Length; i++)
                if (!globals.TryGetValue(address + (uint)i, out destination[i])) return false;
            return true;
        }

        private void Put(uint address, byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++) globals[address + (uint)i] = bytes[i];
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}; actual {actual}");
    }
}
