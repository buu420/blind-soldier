using System.Buffers.Binary;
using System.Security.Cryptography;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The FFRTT Russian 1.02 field scripts as 7th Heaven presents them: the Russian flevel is loaded
/// while the English flevel.lgp stays on disk. Every reviewed Russian prefix must navigate with the
/// audited English disk metadata, a physically installed Russian archive must navigate normally,
/// and anything else (unknown, stale, excluded, or a disk that is not the audited one) is refused.
/// </summary>
internal static class RussianNavigationCompatibilityTests
{
    private static readonly int[] OfficialIdenticalFields = [228, 362, 372, 540];

    public static void Run()
    {
        var failures = new List<string>();
        void Case(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS Russian navigation " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.GetBaseException().Message); Console.WriteLine("FAIL Russian navigation " + name + ": " + ex.GetBaseException().Message); }
        }

        Case("generated manifest is exact, complete and separate from descriptions", ManifestIsExactAndSeparate);
        Case("disk metadata gate takes only the same script or the audited English disk", DiskMetadataGate);
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var russian = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_RUSSIAN_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(russian))
            Console.WriteLine("Russian navigation installed-data checks require FF7_ACCESSIBILITY_DATA_ROOT and FF7_ACCESSIBILITY_RUSSIAN_DATA_ROOT.");
        else
        {
            Case("installed archives are the audited ones", () => InstalledArchivesAreTheAuditedOnes(root, russian));
            Case("active Russian overlay on the English disk navigates every reviewed field", () => ActiveOverlayOnEnglishDisk(root, russian));
            Case("a physically installed Russian archive navigates normally", () => PhysicalRussianArchive(russian));
            Case("unknown, stale and excluded Russian scripts are refused", () => UnreviewedScriptsAreRefused(root, russian));
            Case("a disk that is not the audited English one is refused", () => WrongDiskMetadataIsRefused(root, russian));
            Case("reviewed entity shifts keep canonical identities", () => ReviewedEntityShifts(root, russian));
            Case("official localized scripts gain no English-disk bypass", () => OfficialLocalizedScriptsGainNoBypass(root));
            Case("navigation follows the loaded script, not the text profile", () => NavigationFollowsTheLoadedScript(root, russian));
            Case("cross-field routes are not planned on English data the Russian script contradicts", () => CrossFieldRouteMetadata(root, russian));
            Case("route-trusted reviewed fields are proved route-equivalent", () => TrustedRoutesAreProvedEquivalent(root, russian));
            Case("moved Crater ladder approaches follow native Russian LINEs", () => CraterLadderApproaches(root, russian));
            Case("chair waits follow each verified native layout", () => ChairWaitLayouts(root, russian));
            Case("Ancient Forest native state, controls and timer anchors", () => ForestState(russian));
        }

        if (failures.Count > 0) throw new InvalidOperationException("Russian navigation compatibility failures:\n" + string.Join('\n', failures));
        Console.WriteLine("Russian navigation compatibility tests passed.");
    }

    private static void ManifestIsExactAndSeparate()
    {
        var rows = RussianFieldCompatibility.ReviewedRows;
        Equal(RussianFieldCompatibility.GeneratedRowCount, rows.Count, "every generated row is read");
        Equal(RussianFieldCompatibility.GeneratedRowCount, RussianFieldCompatibility.ReviewedIdentityCount, "public count");
        Equal(702, RussianFieldCompatibility.GeneratedRowCount + RussianFieldCompatibility.GeneratedUnchangedEnglishPrefixCount +
            RussianFieldCompatibility.GeneratedExcludedFieldCount, "every reviewed navigation field is admitted, unchanged or excluded");
        True(RussianFieldCompatibility.GeneratedExcludedFieldCount == 0 || RussianFieldCompatibility.GeneratedRowCount < 702,
            "exclusions are counted, never hidden");
        var officialIdentical = new List<int>();
        foreach (var row in rows)
        {
            var identity = new LoadedFieldScriptIdentity(row.Field, row.Russian);
            True(RussianFieldCompatibility.IsReviewedNavigationIdentity(identity), $"field {row.Field} exact prefix");
            True(RussianFieldCompatibility.IsReviewedNavigationIdentity(identity with { ScriptPrefixSha256 = row.Russian.ToLowerInvariant() }), "hex case");
            True(!RussianFieldCompatibility.IsReviewedNavigationIdentity(NearMiss(identity)), $"field {row.Field} one changed digit is unknown");
            True(!RussianFieldCompatibility.IsReviewedNavigationIdentity(identity with { FieldId = row.Field + 1 }), $"field {row.Field} prefix is not another field's");
            True(!RussianFieldCompatibility.IsReviewedNavigationIdentity(new LoadedFieldScriptIdentity(row.Field, row.English)),
                "the English stock prefix stays the stock identity");
            Equal(EchoSCompatibilityManifest.GetVanillaFingerprint(row.Field), row.English, $"field {row.Field} audited against the reviewed English stock prefix");
            Equal(SupportedFieldScriptVariant.Unknown, WholeGameEchoNavigationManifest.ResolveDescriptionVariant(identity),
                $"field {row.Field}: English description offsets are not authorized by a navigation row");
            Equal(SupportedFieldScriptVariant.Unknown, EchoSCompatibilityManifest.ResolveVariant(identity), $"field {row.Field}: no English action anchors");
            var navigation = WholeGameEchoNavigationManifest.ResolveVariant(identity);
            True(navigation is SupportedFieldScriptVariant.Unknown or SupportedFieldScriptVariant.Vanilla, $"field {row.Field}: never an Echo-S or Postscript identity");
            if (navigation == SupportedFieldScriptVariant.Vanilla) officialIdentical.Add(row.Field);
            True(row.Review is "semantic" or "reviewed", "admission kind");
            True(row.Review == "reviewed" || row.Map is null, "an automatically admitted row has the English entity layout");
            if (row.Map is not null) True(!RussianFieldCompatibility.EnglishDiskDescribesRussianRoutes(row.Field), $"field {row.Field}: shifted entities never trust English routes");
            if (row.Review == "semantic") True(RussianFieldCompatibility.EnglishDiskDescribesRussianRoutes(row.Field), $"field {row.Field}: a window-only row trusts English routes");
        }

        Equal(string.Join(',', OfficialIdenticalFields), string.Join(',', officialIdentical),
            "Russian prefixes byte-identical to an official localized prefix (window geometry only)");
    }

    private static void CraterLadderApproaches(string root, string russian)
    {
        var memory = new ScriptMemory();
        memory.Load(russian, 751, RussianContext(russian));
        var catalog = new FieldScriptNavigationCatalog(root, RussianContext(root), new LoadedFieldScriptIdentityReader(memory));
        var field = catalog.ReadField(751);
        True(field.IsUsable && field.UsesReviewedLocalizedStockLayout, "las0_8 rebases its moved LINEs");
        var checkedRows = 0;
        foreach (var original in FieldStoryEventCatalog.CreateAllFields().Where(d => d.FieldId == 751 && d.TriggerLine.HasValue))
        {
            var lineEntity = original.RequiredEnabledLineEntityId ?? original.EntityId;
            if (!field.NativeLines.TryGetValue(lineEntity, out var native)) continue;
            var mapped = catalog.RemapStoryDefinition(original);
            True(mapped.HasValue, "Crater story ladder remains available");
            Equal(native, mapped!.Value.TriggerLine!.Value, "story follows actual Russian ladder LINE");
            Equal((native.StartX + native.EndX) / 2, mapped.Value.X, "Russian ladder approach X");
            Equal((native.StartY + native.EndY) / 2, mapped.Value.Y, "Russian ladder approach Y");
            checkedRows++;
        }
        True(checkedRows > 0, "at least the gather-party ladder is covered");
    }

    private static void ChairWaitLayouts(string root, string russian)
    {
        foreach (var (source, language, loopStart, loopEnd) in new[]
        {
            (root, EnglishContext(root), 42, 400), (russian, RussianContext(russian), 39, 397)
        })
        {
            var memory = new ScriptMemory(); memory.Load(source, 402, language);
            const int entity = 3, script = 12;
            memory.Bytes[(uint)FieldScriptControllerReader.AddressEntityScriptPriorities + entity] = 0;
            memory.Bytes[(uint)FieldScriptControllerReader.AddressEntityScriptIds + entity * FieldScriptControllerReader.ScriptSlotsPerEntity] = script;
            True(memory.TryReadUInt16(ScriptMemory.Script + 6, out var akao), "chair AKAO table");
            var entryAddress = ScriptMemory.Script + 32 + (uint)(memory.Bytes[ScriptMemory.Script + 2] * 8 + akao * 4 + (entity * 32 + script) * 2);
            True(memory.TryReadUInt16(entryAddress, out var entry), "chair script entry");
            var reader = new FieldActivityStateReader(memory);
            var anchors = FieldActivityReadout.WaitAnchors(402, entity);
            foreach (var relative in new[] { loopStart - 1, loopStart, loopEnd, loopEnd + 1 })
            {
                memory.Word((uint)FieldActivityStateReader.AddressEntityProgramCounters + entity * 2, (ushort)(entry + relative));
                var state = reader.ReadWaitState(402, entity, anchors, verifyEachLoop: true);
                True(state.IsReadable, "verified native chair script");
                Equal(relative >= loopStart && relative <= loopEnd, state.IsWaiting, "only this layout's loop is a player wait");
            }
        }
    }

    private static void ForestState(string russian)
    {
        var catalog = new FieldScriptNavigationCatalog(russian, RussianContext(russian));
        for (var field = 620; field <= 624; field++)
        {
            var memory = new ScriptMemory(); memory.Load(russian, field, RussianContext(russian));
            memory.Bytes[FieldAudibleCueStateReader.AddressUserControl] = 0;
            for (uint i = 0; i < 128; i++) memory.Bytes[(uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase + i] = 0;
            memory.Word(FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + 37, 0);
            var reader = new AncientForestPuzzleStateReader(memory);
            var state = reader.ReadStoryState(field);
            True(state is not null, $"field {field} native Russian story state is available");
            Equal(field is 620 or 622 or 623, state!.ResetUsesPress, "Russian reset uses its actual press edge");
            if (field is 620 or 622 or 623)
            {
                True(reader.Read(field) is { Length: 128 }, "native puzzle bank remains available");
                var directors = field switch { 620 => new[] { 11, 12, 13 }, 622 => new[] { 17, 18, 19 }, _ => new[] { 11, 12, 13, 14 } };
                foreach (var entity in directors)
                {
                    var wait = catalog.ReadScriptOpcodes(field, entity, 3).Single(o => o.ByteIndex == 12);
                    Equal("242C01", Convert.ToHexString(wait.Bytes.ToArray()), "the authorized plant timer anchor is unchanged");
                }
            }
            // No broadly similar or edited future translation is admitted by this native-state rule.
            memory.Bytes[ScriptMemory.Script + 20]++;
            Equal(null, new AncientForestPuzzleStateReader(memory).ReadStoryState(field), "unknown Forest scripts stay unavailable");
        }
    }

    private static void DiskMetadataGate()
    {
        var russian = new string('A', 64);
        var english = new string('B', 64);
        var file = SyntheticFieldFile([0x10, 0x20, 0x30, 0x40]);
        var models = RussianFieldCompatibility.ModelLoaderSectionSha256(file)!;
        Equal(Convert.ToHexString(SHA256.HashData(new byte[] { 0x10, 0x20, 0x30, 0x40 })), models, "section 3 is hashed exactly");
        var row = new RussianFieldCompatibility.Row(1, russian, english, models, "semantic", null);
        True(RussianFieldCompatibility.AcceptsDiskMetadata(row, russian, file, out _), "the same Russian script on disk");
        True(RussianFieldCompatibility.AcceptsDiskMetadata(row, english.ToLowerInvariant(), file, out _), "the audited English disk");
        var tampered = SyntheticFieldFile([0x10, 0x20, 0x30, 0x41]);
        True(!RussianFieldCompatibility.AcceptsDiskMetadata(row, english, tampered, out var why) && why.Contains("model loader"),
            "a changed English model loader");
        True(!RussianFieldCompatibility.AcceptsDiskMetadata(row, new string('C', 64), file, out why) && why.Contains("not the audited English prefix"),
            "another disk script, even with the audited loader");
        var truncated = file[..(BitConverter.ToInt32(file, 6 + 2 * 4) + 4 + 2)];
        Equal(null, RussianFieldCompatibility.ModelLoaderSectionSha256(truncated), "a truncated loader is unreadable");
        True(!RussianFieldCompatibility.AcceptsDiskMetadata(row, english, truncated, out _), "an unreadable loader is refused");
        Equal(null, RussianFieldCompatibility.ModelLoaderSectionSha256([]), "an empty file has no loader");
    }

    private static void InstalledArchivesAreTheAuditedOnes(string root, string russian)
    {
        Equal(RussianFieldCompatibility.AuditedEnglishArchiveSha256, FileSha256(EnglishContext(root).FieldArchivePath), "English flevel.lgp");
        Equal(RussianFieldCompatibility.AuditedRussianArchiveSha256, FileSha256(RussianContext(russian).FieldArchivePath), "Russian 1.02 flevel.lgp");
    }

    private static void ActiveOverlayOnEnglishDisk(string root, string russian)
    {
        // The runtime selects the ru profile over the English game root; its archive is the English flevel.lgp.
        var overlayLanguage = RussianContext(root);
        Equal(EnglishContext(root).FieldArchivePath, overlayLanguage.FieldArchivePath, "the Russian profile reads the installed English archive");
        var loaded = new ScriptMemory();
        var identities = new LoadedFieldScriptIdentityReader(loaded);
        var overlay = new FieldScriptNavigationCatalog(root, overlayLanguage, identities);
        var englishMemory = new ScriptMemory();
        var english = new FieldScriptNavigationCatalog(root, EnglishContext(root), new LoadedFieldScriptIdentityReader(englishMemory));
        var checkedRows = 0;
        foreach (var row in RussianFieldCompatibility.ReviewedRows)
        {
            loaded.Load(russian, row.Field, RussianContext(russian));
            True(identities.TryRead(out var identity) && identity.ScriptPrefixSha256 == row.Russian, $"field {row.Field} loaded Russian identity");
            var current = overlay.ReadField(row.Field);
            True(current.IsUsable, $"field {row.Field} Russian script on the English disk: {current.Diagnostic}");
            if (row.Review == "reviewed")
            {
                var own = new FieldScriptNavigationCatalog(russian, RussianContext(russian), identities).ReadField(row.Field);
                True(own.IsUsable, $"field {row.Field} Russian archive control");
                Equal(Exits(own), Exits(current), $"field {row.Field} preserves actual Russian exits");
                Equal(Transitions(own), Transitions(current), $"field {row.Field} preserves actual Russian traversals");
                Equal(Npcs(own), Npcs(current), $"field {row.Field} preserves actual Russian actors");
                Equal(Lines(own), Lines(current), $"field {row.Field} preserves actual Russian LINEs");
                Equal(Guards(own), Guards(current), $"field {row.Field} preserves actual Russian guards");
                checkedRows++;
                continue;
            }
            englishMemory.Load(root, row.Field, EnglishContext(root));
            var stock = english.ReadField(row.Field);
            True(stock.IsUsable, $"field {row.Field} English control");
            Equal(Exits(stock), Exits(current), $"field {row.Field} exits, destinations and lines");
            Equal(Transitions(stock), Transitions(current), $"field {row.Field} ladders and jumps");
            Equal(Npcs(stock), Npcs(current), $"field {row.Field} entities, English disk model names and bound characters");
            Equal(Lines(stock), Lines(current), $"field {row.Field} native lines");
            Equal(Guards(stock), Guards(current), $"field {row.Field} exit guards");
            Equal(false, current.UsesReviewedLocalizedStockLayout || current.UsesReviewedEchoLayout, $"field {row.Field} keeps the stock layout flags");
            checkedRows++;
        }

        Equal(RussianFieldCompatibility.ReviewedRows.Count, checkedRows, "every reviewed row follows the appropriate actual native navigation");
        // Inactive overlay: the English script under the Russian profile is plain stock navigation.
        loaded.Load(root, 116, EnglishContext(root));
        True(overlay.ReadField(116).IsUsable, "the English script with the Russian profile");
    }

    private static void PhysicalRussianArchive(string russian)
    {
        var memory = new ScriptMemory();
        var catalog = new FieldScriptNavigationCatalog(russian, RussianContext(russian), new LoadedFieldScriptIdentityReader(memory));
        var source = new FlevelDataSource(russian, RussianContext(russian));
        var reviewed = RussianFieldCompatibility.ReviewedRows.Select(r => r.Field).ToHashSet();
        var count = 0;
        foreach (var field in source.FieldNames.Keys)
        {
            if (!source.TryReadField(field, out _)) continue;
            memory.Load(russian, field, RussianContext(russian));
            Equal(true, new LoadedFieldScriptIdentityReader(memory).TryRead(out var identity), "coherent Russian identity");
            var stock = WholeGameEchoNavigationManifest.ResolveVariant(identity) == SupportedFieldScriptVariant.Vanilla;
            if (!reviewed.Contains(field) && !stock) continue;
            True(catalog.ReadField(field).IsUsable, $"field {field} Russian script on the Russian disk");
            count++;
        }

        Equal(RussianFieldCompatibility.GeneratedRowCount + RussianFieldCompatibility.GeneratedUnchangedEnglishPrefixCount, count,
            "every admitted and unchanged field navigates from a Russian disk");
    }

    private static void UnreviewedScriptsAreRefused(string root, string russian)
    {
        var memory = new ScriptMemory();
        var overlay = new FieldScriptNavigationCatalog(root, RussianContext(root), new LoadedFieldScriptIdentityReader(memory));
        var physical = new FieldScriptNavigationCatalog(russian, RussianContext(russian), new LoadedFieldScriptIdentityReader(memory));
        var rows = RussianFieldCompatibility.ReviewedRows;
        var sample = rows.Where((_, index) => index % 25 == 0).ToArray();
        foreach (var row in sample)
        {
            memory.Load(russian, row.Field, RussianContext(russian));
            memory.Bytes[ScriptMemory.Script + 12]++;
            True(Refused(overlay.ReadField(row.Field), "unreviewed loaded script"), $"field {row.Field} an edited Russian prefix on the English disk");
            True(Refused(physical.ReadField(row.Field), "unreviewed loaded script"), $"field {row.Field} an edited Russian prefix on the Russian disk");
        }

        // A stale prefix: field B reported while field A's Russian script is still loaded.
        var a = sample[0];
        var b = rows.First(r => r.Field != a.Field);
        memory.Load(russian, a.Field, RussianContext(russian));
        memory.Word((uint)FieldScriptContextReader.AddressCurrentFieldId, (ushort)b.Field);
        True(Refused(overlay.ReadField(b.Field), "unreviewed loaded script"), "another field's reviewed prefix");

        // Fields the audit did not admit stay refused on both disks.
        var source = new FlevelDataSource(russian, RussianContext(russian));
        var reviewed = rows.Select(r => r.Field).ToHashSet();
        var excluded = 0;
        foreach (var field in source.FieldNames.Keys.Where(f => !reviewed.Contains(f)))
        {
            if (EchoSCompatibilityManifest.GetVanillaFingerprint(field) is not { } stock ||
                WholeGameEchoNavigationManifest.StockFingerprint(field) is null) continue;
            memory.Load(russian, field, RussianContext(russian));
            if (!new LoadedFieldScriptIdentityReader(memory).TryRead(out var identity) || identity.ScriptPrefixSha256 == stock) continue;
            True(!overlay.ReadField(field).IsUsable, $"excluded field {field} on the English disk");
            True(!physical.ReadField(field).IsUsable, $"excluded field {field} on the Russian disk");
            excluded++;
        }

        Equal(RussianFieldCompatibility.GeneratedExcludedFieldCount, excluded, "every excluded Russian field is refused");
    }

    private static void WrongDiskMetadataIsRefused(string root, string russian)
    {
        var english = new LgpArchiveReader(EnglishContext(root).FieldArchivePath);
        var french = new LgpArchiveReader(Path.Combine(root, "data", "field", "fflevel.lgp"));
        True(english.TryReadFile("maplist", out var maplist), "English maplist");
        var names = FieldMapListResolver.ReadFieldNames(maplist);
        var stock = new FieldScriptNavigationCatalog(root, EnglishContext(root));
        byte[] Decoded(LgpArchiveReader archive, int field) =>
            archive.TryReadFile(names[field], out var encoded) ? Ff7LzsDecoder.DecodeFieldFile(encoded) : [];
        static string Prefix(byte[] file)
        {
            var at = BitConverter.ToInt32(file, 6) + 4;
            return Convert.ToHexString(SHA256.HashData(file.AsSpan(at, BitConverter.ToUInt16(file, at + 4))));
        }

        // A field whose entities show English disk models and whose official French script is neither
        // the English nor the Russian one, so each disk below differs in exactly one way.
        var row = RussianFieldCompatibility.ReviewedRows.First(r => r.Map is null &&
            stock.ReadDiskField(r.Field).Npcs.Any(n => n.ModelResourceName.Length > 0) &&
            Prefix(Decoded(french, r.Field)) is var p && p != r.English && p != r.Russian);
        var name = names[row.Field];
        var englishFile = Decoded(english, row.Field);
        var frenchFile = Decoded(french, row.Field);
        // The first model record's name (section 3: blank, count, scale, then length-prefixed names).
        var modelName = BitConverter.ToInt32(englishFile, 6 + 2 * 4) + 4 + 6 + 2;
        var tampered = (byte[])englishFile.Clone();
        tampered[modelName] ^= 0x01;

        foreach (var (label, disk, usable, reason) in new[]
                 {
                     ("the audited English field", englishFile, true, ""),
                     ("a changed English model loader", tampered, false, "not the audited English model loader"),
                     ("another archive's script", frenchFile, false, "not the audited English prefix"),
                 })
        {
            var directory = Path.Combine(Path.GetTempPath(), "blind-soldier-russian-navigation-" + Guid.NewGuid().ToString("N"));
            try
            {
                var extracted = Directory.CreateDirectory(Path.Combine(directory, "data", "field", "flevel")).FullName;
                File.WriteAllBytes(Path.Combine(extracted, "maplist"), maplist);
                File.WriteAllBytes(Path.Combine(extracted, name), LiteralLzs(disk));
                var memory = new ScriptMemory();
                var catalog = new FieldScriptNavigationCatalog(directory, RussianContext(directory), new LoadedFieldScriptIdentityReader(memory));
                memory.Load(russian, row.Field, RussianContext(russian));
                var result = catalog.ReadField(row.Field);
                if (usable) True(result.IsUsable, $"{label}: {result.Diagnostic}");
                else True(Refused(result, reason), $"{label}: {result.Diagnostic}");
                if (label == "a changed English model loader")
                {
                    // The English script itself still trusts its own disk, exactly as before.
                    memory.Load(root, row.Field, EnglishContext(root));
                    True(catalog.ReadField(row.Field).IsUsable, "the English script is not subject to the Russian model gate");
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static void ReviewedEntityShifts(string root, string russian)
    {
        var memory = new ScriptMemory();
        var overlay = new FieldScriptNavigationCatalog(root, RussianContext(root), new LoadedFieldScriptIdentityReader(memory));
        var englishMemory = new ScriptMemory();
        var english = new FieldScriptNavigationCatalog(root, EnglishContext(root), new LoadedFieldScriptIdentityReader(englishMemory));
        foreach (var row in RussianFieldCompatibility.ReviewedRows.Where(r => r.Map is not null))
        {
            memory.Load(russian, row.Field, RussianContext(russian));
            var current = overlay.ReadField(row.Field);
            True(current.IsUsable && current.UsesReviewedLocalizedStockLayout && !current.UsesReviewedEchoLayout, $"field {row.Field} reviewed localized layout");
            Equal(string.Join(',', row.Map!.OrderBy(p => p.Key)), string.Join(',', current.StockToLoadedEntities.OrderBy(p => p.Key)), "the reviewed map is applied");
            englishMemory.Load(root, row.Field, EnglishContext(root));
            var stock = english.ReadField(row.Field);
            var canonical = row.Map!.ToDictionary(p => p.Value, p => p.Key);
            foreach (var npc in current.Npcs)
            {
                var expected = canonical.TryGetValue(npc.EntityId, out var stockEntity) ? stockEntity : -1;
                Equal(expected, npc.ReviewedStockEntityId, $"field {row.Field} entity {npc.EntityId} canonical identity");
                if (expected >= 0 && stock.Npcs.FirstOrDefault(n => n.EntityId == expected) is { EntityName.Length: > 0 } original)
                    Equal(original.EntityName, npc.EntityName, $"field {row.Field} canonical entity {expected} is the same named entity");
            }

            foreach (var definition in FieldNavigationObjectCatalog.CreateAllFields().Where(d => d.FieldId == row.Field))
            {
                var remapped = overlay.RemapObjectDefinition(definition);
                if (definition.EntityId < 0) Equal(definition.EntityId, remapped?.EntityId, "an entity-free object is unchanged");
                else if (row.Map.TryGetValue(definition.EntityId, out var loadedEntity)) Equal(loadedEntity, remapped?.EntityId, "object follows its entity");
                else Equal(null, remapped, "an object of a removed entity is withheld");
            }
        }
    }

    private static void OfficialLocalizedScriptsGainNoBypass(string root)
    {
        // An official localized prefix is navigable from its own archive and, as before, refused over
        // the English disk, except where it is byte for byte a reviewed Russian prefix.
        var memory = new ScriptMemory();
        var overEnglish = new FieldScriptNavigationCatalog(root, RussianContext(root), new LoadedFieldScriptIdentityReader(memory));
        var refused = 0;
        var admittedAsRussian = 0;
        foreach (var descriptor in Ff7GameLanguages.All.Where(d => d.Language != Ff7GameLanguage.English))
        {
            var language = new Ff7GameLanguageContext(descriptor, Path.Combine(root, "data"), Ff7GameLanguageDetectionSource.Configuration, "official localized control");
            if (!File.Exists(language.FieldArchivePath)) continue;
            var own = new FieldScriptNavigationCatalog(root, language, new LoadedFieldScriptIdentityReader(memory));
            var source = new FlevelDataSource(root, language);
            foreach (var field in source.FieldNames.Keys.Where(f => f % 7 == 0 || OfficialIdenticalFields.Contains(f)))
            {
                if (WholeGameEchoNavigationManifest.StockFingerprint(field) is not { } stock || !source.TryReadField(field, out _)) continue;
                memory.Load(root, field, language);
                if (!new LoadedFieldScriptIdentityReader(memory).TryRead(out var identity) || identity.ScriptPrefixSha256 == stock) continue;
                True(own.ReadField(field).IsUsable, $"{descriptor.Code} field {field} on its own archive");
                var english = overEnglish.ReadField(field);
                if (RussianFieldCompatibility.IsReviewedNavigationIdentity(identity))
                {
                    True(english.IsUsable, $"{descriptor.Code} field {field} is byte for byte the reviewed Russian prefix");
                    admittedAsRussian++;
                }
                else
                {
                    True(Refused(english, "unreviewed loaded script"), $"{descriptor.Code} field {field} over the English disk");
                    refused++;
                }
            }
        }

        True(refused > 300, $"official localized prefixes over the English disk were exercised ({refused})");
        Equal(OfficialIdenticalFields.Length, admittedAsRussian, "only the four byte-identical prefixes");
    }

    private static void NavigationFollowsTheLoadedScript(string root, string russian)
    {
        // If language detection falls back to English while 7th Heaven loads the Russian scripts, the
        // reviewed loaded identity still decides navigation; the text profile does not.
        var memory = new ScriptMemory();
        var catalog = new FieldScriptNavigationCatalog(root, EnglishContext(root), new LoadedFieldScriptIdentityReader(memory));
        foreach (var row in RussianFieldCompatibility.ReviewedRows.Where((_, index) => index % 50 == 0))
        {
            memory.Load(russian, row.Field, RussianContext(russian));
            True(catalog.ReadField(row.Field).IsUsable, $"field {row.Field} Russian script under the English profile");
        }
    }

    private static void CrossFieldRouteMetadata(string root, string russian)
    {
        var fields = new FlevelDataSource(root, EnglishContext(root)).FieldNames.Keys
            .Where(f => WholeGameEchoNavigationManifest.StockFingerprint(f) is not null).ToArray();
        var untrusted = fields.Where(f => !RussianFieldCompatibility.EnglishDiskDescribesRussianRoutes(f)).ToArray();
        True(untrusted.Length > 0, "some Russian fields contradict their English routes");
        var reviewed = RussianFieldCompatibility.ReviewedRows.Select(r => r.Field).ToHashSet();
        var trusted = fields.Where(f => RussianFieldCompatibility.EnglishDiskDescribesRussianRoutes(f)).Where((_, i) => i % 20 == 0).ToArray();
        // The Russian profile over the English disk: untrusted fields are withheld from route planning.
        var overlay = new FieldScriptNavigationCatalog(root, RussianContext(root));
        foreach (var field in untrusted)
            True(Refused(overlay.ReadFutureFieldMetadata(field, false), "does not describe its Russian routes"), $"field {field} under the Russian profile");
        foreach (var field in trusted)
            True(overlay.ReadFutureFieldMetadata(field, false).IsUsable, $"field {field} route-equivalent English metadata");
        // An English session is unchanged.
        var english = new FieldScriptNavigationCatalog(root, EnglishContext(root));
        foreach (var field in untrusted)
            True(english.ReadFutureFieldMetadata(field, false).IsUsable, $"field {field} English session");
        // A Russian script observed under the English profile switches the same protection on.
        var memory = new ScriptMemory();
        var observed = new FieldScriptNavigationCatalog(root, EnglishContext(root), new LoadedFieldScriptIdentityReader(memory));
        True(observed.ReadFutureFieldMetadata(untrusted[0], false).IsUsable, "before any Russian script is loaded");
        var semantic = RussianFieldCompatibility.ReviewedRows.First(r => r.Review == "semantic");
        memory.Load(russian, semantic.Field, RussianContext(russian));
        True(observed.ReadField(semantic.Field).IsUsable, "a reviewed Russian script loads");
        True(Refused(observed.ReadFutureFieldMetadata(untrusted[0], false), "does not describe its Russian routes"), "after a Russian script loaded");
        // A physically installed Russian archive describes its own routes.
        var physical = new FieldScriptNavigationCatalog(russian, RussianContext(russian));
        foreach (var field in untrusted.Where(reviewed.Contains))
            True(physical.ReadFutureFieldMetadata(field, false).IsUsable, $"field {field} from a Russian disk");
    }

    /// <summary>
    /// For each reviewed (non window-only) field the route list trusts, the cross-field resolver must
    /// reach the same answers from the English disk as from the Russian script: the same route
    /// metadata (exits, transitions, guards, native lines), the same source transfers for every
    /// MAPJUMP, and the same entry placement for every arrival other fields can deliver.
    /// </summary>
    private static void TrustedRoutesAreProvedEquivalent(string root, string russian)
    {
        var proof = new RouteProof(root, russian);
        var trusted = RussianFieldCompatibility.ReviewedRows
            .Where(r => r.Review == "reviewed" && RussianFieldCompatibility.EnglishDiskDescribesRussianRoutes(r.Field)).ToArray();
        foreach (var row in trusted)
            True(proof.Differences(row.Field) is [], $"field {row.Field}: {string.Join("; ", proof.Differences(row.Field))}");
        Console.WriteLine($"Russian navigation: {trusted.Length} reviewed fields proved route-equivalent to English.");
    }

    /// <summary>The resolver's own analyses of one field, from the English disk and from the Russian archive.</summary>
    internal sealed class RouteProof
    {
        private readonly FieldScriptNavigationCatalog english;
        private readonly FieldScriptNavigationCatalog russian;
        private readonly Dictionary<int, List<(int X, int Y, int Triangle, FieldEntryWrites Writes)>> arrivals = new();

        public RouteProof(string root, string russianRoot)
        {
            english = new FieldScriptNavigationCatalog(root, EnglishContext(root));
            russian = new FieldScriptNavigationCatalog(russianRoot, RussianContext(russianRoot));
            var source = new FlevelDataSource(root, EnglishContext(root));
            // Every arrival the English data of any field delivers: its MAPJUMP transfers with the
            // writes proven on their paths, the MAPJUMP points themselves, and its gateways.
            foreach (var field in source.FieldNames.Keys.Where(f => WholeGameEchoNavigationManifest.StockFingerprint(f) is not null))
            {
                var scripts = english.ReadAllScriptOpcodes(field);
                foreach (var (entity, destination, jump) in MapJumps(scripts))
                {
                    Arrivals(destination).Add((jump.X, jump.Y, jump.Triangle, FieldEntryWrites.None));
                    foreach (var transfer in FieldSourceTransfers.Find(scripts, entity, destination))
                        Arrivals(destination).Add((transfer.X, transfer.Y, transfer.Triangle, transfer.Writes));
                }

                if (!source.TryReadField(field, out var encoded)) continue;
                var file = Ff7LzsDecoder.DecodeFieldFile(encoded);
                var triggers = BitConverter.ToInt32(file, 6 + 7 * 4) + 4;
                for (var index = 0; index < 12; index++)
                {
                    var at = triggers + 0x38 + index * 24;
                    if (at + 24 > file.Length) break;
                    var destination = BitConverter.ToInt16(file, at + 18);
                    if (destination < 0 || destination == short.MaxValue || destination == field) continue;
                    Arrivals(destination).Add((BitConverter.ToInt16(file, at + 12), BitConverter.ToInt16(file, at + 14),
                        BitConverter.ToUInt16(file, at + 16), FieldEntryWrites.None));
                }
            }
        }

        private List<(int X, int Y, int Triangle, FieldEntryWrites Writes)> Arrivals(int field) =>
            arrivals.TryGetValue(field, out var list) ? list : arrivals[field] = [];

        public IReadOnlyList<string> Differences(int field)
        {
            var differences = new List<string>();
            var a = english.ReadFutureFieldMetadata(field, false);
            var b = russian.ReadFutureFieldMetadata(field, false);
            if (!a.IsUsable || !b.IsUsable) return ["route metadata unreadable"];
            if (Exits(a) != Exits(b)) differences.Add("exits");
            if (Transitions(a) != Transitions(b)) differences.Add("transitions");
            if (Guards(a) != Guards(b)) differences.Add("exit guards");
            if (Lines(a) != Lines(b)) differences.Add("native lines");
            var englishScripts = english.ReadAllScriptOpcodes(field);
            var russianScripts = russian.ReadAllScriptOpcodes(field);
            var jumps = MapJumps(englishScripts).Select(j => (j.Entity, j.Destination))
                .Union(MapJumps(russianScripts).Select(j => (j.Entity, j.Destination))).Distinct();
            foreach (var (entity, destination) in jumps)
            {
                var left = string.Join(';', FieldSourceTransfers.Find(englishScripts, entity, destination).Select(Describe));
                var right = string.Join(';', FieldSourceTransfers.Find(russianScripts, entity, destination).Select(Describe));
                if (left != right) differences.Add($"transfers of entity {entity} to {destination}: {left} vs {right}");
            }

            foreach (var arrival in Arrivals(field).Distinct())
            {
                var left = FieldEntryPlacement.Place(englishScripts, arrival.X, arrival.Y, arrival.Triangle, arrival.Writes);
                var right = FieldEntryPlacement.Place(russianScripts, arrival.X, arrival.Y, arrival.Triangle, arrival.Writes);
                if (left != right) differences.Add($"entry at {arrival.X},{arrival.Y} t{arrival.Triangle}: {left} vs {right}");
            }

            return differences;
        }

        private static IEnumerable<(int Entity, int Destination, (int X, int Y, int Triangle) Point)> MapJumps(IReadOnlyList<FieldScriptDefinition> scripts) =>
            scripts.SelectMany(s => s.Opcodes.Where(o => o.Opcode == 0x60 && o.Bytes.Count >= 10).Select(o =>
                (s.EntityId, o.Bytes[1] | o.Bytes[2] << 8,
                    ((int)BitConverter.ToInt16([o.Bytes[3], o.Bytes[4]]), (int)BitConverter.ToInt16([o.Bytes[5], o.Bytes[6]]), o.Bytes[7] | o.Bytes[8] << 8))));

        private static string Describe(FieldSourceTransfers.Transfer transfer) =>
            $"{transfer.X},{transfer.Y},t{transfer.Triangle},{Describe(transfer.Writes)}";

        private static string Describe(FieldEntryWrites writes) =>
            string.Join(',', writes.Known.OrderBy(p => p.Key).Select(p => $"{p.Key.Block}:{p.Key.Address}={p.Value}")) + "|" +
            string.Join(',', writes.Unknown.Order()) + "|" + writes.AnyUnknown;
    }

    private static bool Refused(FieldScriptNavigationReadResult result, string reason) =>
        !result.IsUsable && result.Diagnostic.Contains(reason, StringComparison.Ordinal);

    private static string Exits(FieldScriptNavigationReadResult r) => string.Join(';', r.Exits.Select(e =>
        $"{e.StableId}|{e.TriggerEntityId}|{string.Join(',', e.DestinationFieldIds ?? [])}|{e.TriggerLine}|{e.CompletesOnArrival}|{e.X},{e.Y},{e.Z}"));

    private static string Transitions(FieldScriptNavigationReadResult r) => string.Join(';', r.Transitions.Select(t =>
        $"{t.StableId}|{t.Kind}|{t.SourceEntityId}|{t.TargetX},{t.TargetY},{t.TargetZ}|{t.TargetTriangle}|{t.RequiredInput}|{t.RequiresAction}|" +
        $"{string.Join(',', t.MoverEntityIds ?? [])}|{string.Join(',', (t.Conditions ?? []).Select(c => c.Key))}|{t.RequiredChoice?.Line}"));

    private static string Npcs(FieldScriptNavigationReadResult r) => string.Join(';', r.Npcs.Select(n =>
        $"{n.EntityId}|{n.EntityName}|{n.ModelResourceName}|{n.BoundCharacterId}|{n.InteractionLineEntityId}|{n.InteractionLine}|" +
        $"{n.HasPerceivableTalk}|{n.HasConversationCounter}|{n.InteractionLineRunsTalk}|{n.ContactOnly}"));

    private static string Lines(FieldScriptNavigationReadResult r) => string.Join(';', r.NativeLines.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

    private static string Guards(FieldScriptNavigationReadResult r) => string.Join(';', r.ExitGuards.OrderBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => p.Key + "=" + string.Join('/', p.Value.Select(g => $"{g.DestinationFieldId}:" +
            string.Join('|', g.Alternatives.Select(a => string.Join('&', a)))))));

    private static LoadedFieldScriptIdentity NearMiss(LoadedFieldScriptIdentity identity)
    {
        var hash = identity.ScriptPrefixSha256.ToCharArray();
        hash[^1] = hash[^1] == '0' ? '1' : '0';
        return identity with { ScriptPrefixSha256 = new string(hash) };
    }

    /// <summary>A decoded field file of nine sections whose model loader holds exactly these bytes.</summary>
    private static byte[] SyntheticFieldFile(byte[] modelLoader)
    {
        var sections = Enumerable.Range(0, 9).Select(i => i == 2 ? modelLoader : new[] { (byte)i }).ToArray();
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0);
        writer.Write(sections.Length);
        var offsets = 6 + sections.Length * 4;
        foreach (var section in sections)
        {
            writer.Write(offsets);
            offsets += 4 + section.Length;
        }

        foreach (var section in sections)
        {
            writer.Write(section.Length);
            writer.Write(section);
        }

        writer.Flush();
        return file.ToArray();
    }

    /// <summary>The field file as an LGP entry: its length, then LZS made only of literal runs.</summary>
    private static byte[] LiteralLzs(byte[] decoded)
    {
        var encoded = new List<byte>(decoded.Length + decoded.Length / 8 + 8);
        for (var index = 0; index < decoded.Length; index += 8)
        {
            encoded.Add(0xFF);
            encoded.AddRange(decoded.AsSpan(index, Math.Min(8, decoded.Length - index)).ToArray());
        }

        return [.. BitConverter.GetBytes(encoded.Count), .. encoded];
    }

    private static Ff7GameLanguageContext EnglishContext(string root) =>
        new(Ff7GameLanguages.Get(Ff7GameLanguage.English), Path.Combine(root, "data"), Ff7GameLanguageDetectionSource.Configuration, "Russian navigation English control");

    private static Ff7GameLanguageContext RussianContext(string root) =>
        new(Ff7GameLanguages.RussianFanTranslation, Path.Combine(root, "data"), Ff7GameLanguageDetectionSource.Configuration, "Russian navigation compatibility");

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void True(bool condition, string why)
    {
        if (!condition) throw new InvalidOperationException(why);
    }

    private static void Equal<T>(T expected, T actual, string why)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{why}: expected {expected}; actual {actual}");
    }

    private sealed class ScriptMemory : ILegacyAddressSpace
    {
        internal const uint Script = 0x02000000;
        internal readonly Dictionary<uint, byte> Bytes = new();

        internal void Load(string root, int field, Ff7GameLanguageContext language)
        {
            Bytes.Clear();
            var source = new FlevelDataSource(root, language);
            Equal(true, source.TryReadField(field, out var encoded), "native input exists");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
            var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
            Write(Script, decoded.AsSpan(at + 4, length));
            Bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 1;
            Word((uint)FieldScriptContextReader.AddressCurrentFieldId, (ushort)field);
            Span<byte> pointer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(pointer, Script);
            Write((uint)FieldScriptContextReader.AddressFieldScriptPtr, pointer);
        }

        internal void Word(uint address, ushort value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            Write(address, bytes);
        }

        private void Write(uint address, ReadOnlySpan<byte> data)
        {
            for (var i = 0; i < data.Length; i++) Bytes[address + (uint)i] = data[i];
        }

        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
                if (!Bytes.TryGetValue(address + (uint)i, out destination[i])) return false;
            return true;
        }
    }
}
