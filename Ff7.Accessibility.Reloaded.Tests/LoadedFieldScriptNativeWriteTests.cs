using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The loaded script identity across the field engine's own writes into the loaded script.
///
/// <para>KAWAI (00620136) keeps a pointer to its packet in the model's state. The per-frame
/// dispatcher 0064A070 stores the executing model's slot (00CBFB70[entity], which CHAR
/// 00614353 assigns in load order) in packet[1] of a LIGHT, 7, 8 or 9 setup packet
/// (packet[0] 0) before reading it: 0064A259, 0064A487 and 0064A5F9. A processed packet
/// changes the hashed prefix while the reviewed script is loaded. The identity must undo
/// exactly those writes and nothing else.</para>
/// </summary>
internal static class LoadedFieldScriptNativeWriteTests
{
    // Field 345 frcyo and 738 md8brdg2 as installed, and as the tester's Steam 2026 x64 game
    // reported them (2026-10-28 13.57.57 and 2026-10-22 12.03.16 logs).
    private const string FrcyoStock = "D19FF1F3F75BAE8518E52E0E70B0BC9F9C9FA595F31677C5533CEAD9FD75A872";
    private const string FrcyoTester = "B749296FACC7D8BB86A4381EF657E32CC84EAF026F1B0B3823EB96DFC3A7FB33";
    private const string BridgeStock = "A63B0DCC18AB576F9370AC0766E13554A5365005201CB6857CA0E6382E0ADCB0";
    private const string BridgeTester = "2922D6FD20E09F5BBEB313E98A58FF28DBAC45AD743942E333AA6D6005AEFE37";

    public static void Run()
    {
        var failures = new List<string>();
        void Require(bool condition, string message)
        {
            if (!condition) failures.Add(message);
        }

        var script = BuildMixedScript();
        var stock = Sha(script.Prefix);
        int At(string mark) => script.Marks[mark];

        // Untouched: exactly the raw prefix, as before.
        var untouched = Observe(Mixed(script));
        Require(untouched.Read, "untouched synthetic prefix reads");
        Require(untouched.Identity.ScriptPrefixSha256 == stock, "untouched identity is the raw prefix");
        Require(untouched.Normalization.RawScriptPrefixSha256 == stock &&
                untouched.Normalization.RestoredOffsets.Count == 0 &&
                untouched.Normalization.UnexplainedNativeWrites == 0, "untouched prefix has nothing to restore");
        Require(untouched.Prefix.AsSpan().SequenceEqual(script.Prefix), "untouched prefix bytes are returned unchanged");

        // The engine's write: the sole owner's live model slot (1), not its CHAR operand (5).
        var lit = Mixed(script);
        lit.Poke(At("choko.light") + 4, 1);
        var written = Observe(lit);
        Require(written.Read, "processed LIGHT prefix reads");
        Require(written.Identity.ScriptPrefixSha256 == stock, "a processed LIGHT setup packet keeps the reviewed identity");
        Require(written.Identity == untouched.Identity, "identity equality is stable across the native write");
        Require(written.Prefix.AsSpan().SequenceEqual(script.Prefix), "consumers receive the canonical prefix bytes");
        Require(written.Normalization.RawScriptPrefixSha256 == Sha(lit.Current()) &&
                written.Normalization.RawScriptPrefixSha256 != stock, "the actual raw fingerprint remains diagnostic");
        Require(written.Normalization.RestoredOffsets.SequenceEqual(new[] { At("choko.light") + 4 }) &&
                written.Normalization.UnexplainedNativeWrites == 0, "exactly the written byte is restored");
        var twoArgument = new LoadedFieldScriptIdentityReader(lit);
        Require(twoArgument.TryReadPrefix(out var twoIdentity, out var twoPrefix) &&
                twoIdentity == untouched.Identity && twoPrefix.AsSpan().SequenceEqual(script.Prefix) &&
                twoArgument.TryRead(out var oneIdentity) && oneIdentity == untouched.Identity,
            "every reader entry point returns the canonical identity");

        RequireRaw(failures, script, At("choko.light") + 4, 5, "the CHAR operand is not the model slot the engine writes");
        RequireRaw(failures, script, At("choko.light") + 4, 2, "another entity's model slot is not this packet's write");

        // Sub-ops 7, 8 and 9 take the same path (0064A487, 0064A5F9).
        var three = Mixed(script);
        foreach (var mark in new[] { "mixed.k7", "mixed.k8", "mixed.k9" }) three.Poke(At(mark) + 4, 2);
        var restoredThree = Observe(three);
        Require(restoredThree.Identity.ScriptPrefixSha256 == stock &&
                restoredThree.Normalization.RestoredOffsets.SequenceEqual(
                    new[] { "mixed.k7", "mixed.k8", "mixed.k9" }.Select(mark => At(mark) + 4)),
            "KAWAI 7, 8 and 9 setup packets are restored");

        var all = Mixed(script);
        all.Poke(At("choko.light") + 4, 1);
        foreach (var mark in new[] { "mixed.k7", "mixed.k8", "mixed.k9" }) all.Poke(At(mark) + 4, 2);
        var restoredAll = Observe(all);
        Require(restoredAll.Identity.ScriptPrefixSha256 == stock &&
                restoredAll.Normalization.RestoredOffsets.Count == 4, "several entities' writes are restored together");

        // Bytes the engine never writes stay strict.
        RequireRaw(failures, script, At("mixed.shine") + 4, 2, "SHINE packet bytes are not native-writable");
        RequireRaw(failures, script, At("mixed.trnsp") + 4, 2, "TRNSP packet bytes are not native-writable");
        RequireRaw(failures, script, At("mixed.apply") + 4, 2, "a packet whose packet[0] is 1 is not rewritten");
        RequireRaw(failures, script, At("mixed.short") + 4, 2, "a four-byte packet never reaches the next opcode");
        RequireRaw(failures, script, At("mixed.xyzi") + 7, 2, "KAWAI-shaped operand bytes are not a packet");
        RequireRaw(failures, script, At("mixed.k7") + 5, 2, "only packet[1] is written");
        RequireRaw(failures, script, At("twochar.light") + 4, 3, "an owner with more than one CHAR is ambiguous");
        RequireRaw(failures, script, At("nochar.light") + 4, 1, "an entity without a model never has KAWAI processed");
        RequireRaw(failures, script, At("host.light") + 4, 4, "code two entities run has no sole owner (host slot)");
        RequireRaw(failures, script, At("host.light") + 4, 5, "code two entities run has no sole owner (guest slot)");
        RequireRaw(failures, script, At("overlap.light") + 4, 6, "a packet[1] another path runs as code is not restored");

        var outside = Mixed(script, modelCount: 1);
        outside.Poke(At("choko.light") + 4, 1);
        RequireUnrestored(failures, outside, "a model slot outside the loaded model count is not a write");
        var unreadable = Mixed(script);
        unreadable.ForgetModel(2);
        unreadable.Poke(At("choko.light") + 4, 1);
        RequireUnrestored(failures, unreadable, "an unreadable owner model slot is not assumed");

        // Two matching raw reads and a coherent header come before any normalization.
        var racing = Mixed(script);
        racing.BetweenPrefixReads = memory => memory.Poke(At("choko.light") + 4, 1);
        Require(!Observe(racing).Read, "a native write between the two raw reads fails closed");
        var moving = Mixed(script);
        moving.BetweenPrefixReads = memory => memory.SetField(901);
        Require(!Observe(moving).Read, "a field change during the read fails closed");

        // A non-zero original is never mistaken for an unwritten packet.
        var original = Build(
            Entity("cloud", new Code().Add(0xA1, 0x00, 0x00, 0x00)),
            Entity("orig", new Code().Add(0xA1, 0x01, 0x00).Mark("light").Add(Light(9)).Add(0x00)));
        var originalMemory = new NativeMemory(902, original.Prefix);
        originalMemory.SetModels(2, (0, 0), (1, 1));
        var kept = Observe(originalMemory);
        Require(kept.Read && kept.Identity.ScriptPrefixSha256 == Sha(original.Prefix) &&
                kept.Normalization.RestoredOffsets.Count == 0 && kept.Normalization.UnexplainedNativeWrites == 1,
            "an untouched non-zero original keeps the raw identity and is reported");
        originalMemory.SetModels(10, (1, 9));
        var zeroed = (byte[])original.Prefix.Clone();
        zeroed[original.Marks["orig.light"] + 4] = 0;
        var ambiguousOriginal = Observe(originalMemory);
        Require(ambiguousOriginal.Identity.ScriptPrefixSha256 == Sha(zeroed) &&
                ambiguousOriginal.Identity.ScriptPrefixSha256 != Sha(original.Prefix),
            "a non-zero original equal to the owner's slot canonicalizes to zero, so no raw identity with such a byte can be reviewed");

        // A header that cannot be a script is never normalized, and never rejected for it.
        var malformed = new byte[0x40];
        BinaryPrimitives.WriteUInt16LittleEndian(malformed, 0x0502);
        malformed[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(4), 0x40);
        new byte[] { 0x28, 0x18, 0x06, 0x00, 0x01 }.CopyTo(malformed, 0x30);
        var malformedMemory = new NativeMemory(903, malformed);
        malformedMemory.SetModels(2, (0, 1), (1, 1));
        var malformedRead = Observe(malformedMemory);
        Require(malformedRead.Read && malformedRead.Identity.ScriptPrefixSha256 == Sha(malformed) &&
                malformedRead.Normalization.RestoredOffsets.Count == 0,
            "a prefix whose code cannot be parsed keeps its raw identity");

        // A packet cut off by the text offset is not an instruction the engine can run.
        var whole = Build(Entity("cloud", new Code().Add(0xA1, 0x00, 0x00).Mark("light").Add(Light(1))));
        var cut = whole.Prefix[..(whole.Marks["cloud.light"] + 8)];
        BinaryPrimitives.WriteUInt16LittleEndian(cut.AsSpan(4), (ushort)cut.Length);
        var cutMemory = new NativeMemory(904, cut);
        cutMemory.SetModels(2, (0, 1));
        var cutRead = Observe(cutMemory);
        Require(cutRead.Read && cutRead.Identity.ScriptPrefixSha256 == Sha(cut) &&
                cutRead.Normalization.RestoredOffsets.Count == 0, "a truncated packet is never restored");

        // Diagnostics name both fingerprints, once per raw state, and stay quiet otherwise.
        var messages = new List<string>();
        var quiet = new LoadedFieldScriptIdentityReader(Mixed(script), messages.Add);
        Require(quiet.TryRead(out _) && messages.Count == 0, "an untouched prefix logs nothing");
        var logged = Mixed(script);
        logged.Poke(At("choko.light") + 4, 1);
        var loud = new LoadedFieldScriptIdentityReader(logged, messages.Add);
        Require(loud.TryRead(out _) && loud.TryRead(out _) && messages.Count == 1, "a restored state is logged once");
        Require(messages.Count == 1 && messages[0].Contains(Sha(logged.Current()), StringComparison.Ordinal) &&
                messages[0].Contains(stock, StringComparison.Ordinal) && messages[0].Contains("restored=1", StringComparison.Ordinal),
            "the log names the raw and canonical fingerprints");
        var odd = Mixed(script);
        odd.Poke(At("choko.light") + 4, 5);
        var oddReader = new LoadedFieldScriptIdentityReader(odd, messages.Add);
        Require(oddReader.TryRead(out _) && messages.Count == 2 &&
                messages[1].Contains("unexplained=1", StringComparison.Ordinal), "an unexplained value is logged");

        Report(failures, "native KAWAI writes", "Loaded script identity restores exactly the engine's KAWAI packet writes.");
    }

    public static void RunWithInstalledGameData()
    {
        var failures = new List<string>();
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.WriteLine("Native KAWAI identity installed-data checks skipped: FF7_ACCESSIBILITY_DATA_ROOT unavailable.");
        }
        else
        {
            if (Context(root, Ff7GameLanguage.English) is not { } english)
            {
                failures.Add("the English official field archive is required for the tester reproductions");
            }
            else
            {
                Reproduce(failures, root, english, 345, FrcyoStock, FrcyoTester,
                    [(7, 5), (8, 6), (9, 7), (10, 8), (11, 9), (12, 10), (13, 11)],
                    [(0x3600, 5), (0x3934, 6), (0x3C70, 7), (0x3FAC, 8), (0x42E8, 9), (0x4624, 10), (0x4942, 11)]);
                Reproduce(failures, root, english, 738, BridgeStock, BridgeTester,
                    [(16, 10)], [(0x0E6E, 10), (0x0EA0, 10)]);

                // jtemplc's cefi slot 3 returns at 0x173C; the LIGHT packet at 0x17A1 after it is
                // reached by no slot and no jump, so the engine never runs or writes it.
                if (new FlevelDataSource(root, english).TryReadField(775, out var temple))
                {
                    var templePrefix = Prefix(temple);
                    if (templePrefix.Length <= 0x17A5 || templePrefix[0x17A1] != 0x28 || templePrefix[0x17A3] != 0x06 ||
                        NativeFieldScriptPrefixCanonicalizer.FindWriteSites(templePrefix).Any(site => site.PacketOffset == 0x17A1))
                        failures.Add("field 775: the LIGHT packet in code no slot reaches is not a native write site");
                }
            }

            foreach (var descriptor in Ff7GameLanguages.All)
            {
                if (Context(root, descriptor.Language) is not { } language)
                {
                    Console.WriteLine($"Native KAWAI identity {descriptor.Code} matrix skipped: official archive absent.");
                    continue;
                }

                // Every official language has 224 LIGHT/7/8/9 setup packets in 46 fields; one is dead code.
                var (fields, sites) = Matrix(failures, $"{descriptor.Code} official", new FlevelDataSource(root, language));
                if (fields != 46 || sites != 223)
                    failures.Add($"{descriptor.Code} official: expected 46 fields with 223 native-writable bytes, found {fields} with {sites}");
            }
        }

        foreach (var echo in (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (fields, sites) = Matrix(failures, "Echo-S " + echo, new FlevelDataSource(echo));
            Console.WriteLine($"Native KAWAI identity Echo-S {echo}: {fields} fields, {sites} native-writable bytes, all reviewed originals zero.");
        }

        var opening = Environment.GetEnvironmentVariable("BLIND_SOLDIER_POSTSCRIPT_OPENING_PREFIX");
        if (!string.IsNullOrWhiteSpace(opening) && File.Exists(opening))
        {
            var prefix = File.ReadAllBytes(opening);
            var observed = Observe(new NativeMemory(PostscriptFieldCompatibility.OpeningFieldId, prefix));
            if (!observed.Read || !PostscriptFieldCompatibility.IsSupportedOpeningIdentity(observed.Identity))
                failures.Add("the reviewed Postscript opening keeps its exact identity");
            if (NativeFieldScriptPrefixCanonicalizer.FindWriteSites(prefix).Any(site => prefix[site.ValueOffset] != 0))
                failures.Add("the reviewed Postscript opening has a non-zero native-writable byte");
        }
        else
        {
            Console.WriteLine("Native KAWAI identity Postscript opening check skipped: BLIND_SOLDIER_POSTSCRIPT_OPENING_PREFIX unavailable.");
        }

        Report(failures, "native KAWAI installed data",
            "Native KAWAI writes reproduce the tester prefixes and restore every reviewed native prefix.");
    }

    private static void Reproduce(List<string> failures, string root, Ff7GameLanguageContext english, int field,
        string stock, string tester, (int Entity, byte Slot)[] models, (int Offset, byte Value)[] writes)
    {
        var source = new FlevelDataSource(root, english);
        if (!source.TryReadField(field, out var encoded))
        {
            failures.Add($"field {field} is installed");
            return;
        }

        var prefix = Prefix(encoded);
        if (Sha(prefix) != stock)
        {
            failures.Add($"field {field} installed prefix is the reviewed stock {stock}, found {Sha(prefix)}");
            return;
        }

        var memory = new NativeMemory(field, prefix);
        memory.SetModels((byte)(models.Max(model => model.Slot) + 1), models);
        foreach (var (offset, value) in writes)
        {
            if (prefix[offset - 4] != 0x28 || prefix[offset - 2] != 0x06 || prefix[offset - 1] != 0 || prefix[offset] != 0)
                failures.Add($"field {field} 0x{offset:X5} is packet[1] of a LIGHT setup packet");
            memory.Poke(offset, value);
        }

        if (Sha(memory.Current()) != tester)
            failures.Add($"field {field}: the native writes reproduce the tester's {tester}, found {Sha(memory.Current())}");

        var reader = new LoadedFieldScriptIdentityReader(memory);
        var observed = Observe(reader);
        if (!observed.Read || observed.Identity.ScriptPrefixSha256 != stock)
            failures.Add($"field {field}: identity is the reviewed {stock}, found {observed.Identity.ScriptPrefixSha256}");
        if (observed.Normalization.RawScriptPrefixSha256 != tester)
            failures.Add($"field {field}: raw fingerprint stays diagnostic as {tester}");
        if (!observed.Normalization.RestoredOffsets.SequenceEqual(writes.Select(write => write.Offset)) ||
            observed.Normalization.UnexplainedNativeWrites != 0)
            failures.Add($"field {field}: exactly the tester's native writes are restored");
        if (WholeGameEchoNavigationManifest.ResolveVariant(observed.Identity) != SupportedFieldScriptVariant.Vanilla)
            failures.Add($"field {field}: canonical identity resolves as reviewed stock");

        var current = new FieldScriptNavigationCatalog(root, english, reader).ReadField(field);
        if (!current.IsUsable)
            failures.Add($"field {field}: loaded navigation is usable instead of '{current.Diagnostic}'");
        Console.WriteLine($"Native KAWAI identity field {field}: raw {tester} -> {observed.Identity.ScriptPrefixSha256}, " +
                          $"{observed.Normalization.RestoredOffsets.Count} writes restored, {current.Npcs.Count} NPC definitions.");
    }

    private static (int Fields, int Sites) Matrix(List<string> failures, string name, FlevelDataSource source)
    {
        var fields = 0;
        var total = 0;
        var checkedFields = 0;
        foreach (var field in source.FieldNames.Keys.Order())
        {
            if (!source.TryReadField(field, out var encoded))
                continue;
            checkedFields++;
            var prefix = Prefix(encoded);
            var raw = Sha(prefix);
            var untouched = Observe(new NativeMemory(field, prefix));
            var variant = WholeGameEchoNavigationManifest.ResolveVariant(untouched.Identity);
            if (!untouched.Read || untouched.Identity.ScriptPrefixSha256 != raw || variant == SupportedFieldScriptVariant.Unknown)
            {
                failures.Add($"{name} field {field}: untouched reviewed prefix keeps its exact identity");
                continue;
            }

            var sites = NativeFieldScriptPrefixCanonicalizer.FindWriteSites(prefix);
            if (sites.Count == 0)
                continue;
            fields++;
            total += sites.Count;
            foreach (var site in sites)
            {
                if (prefix[site.ValueOffset] != 0)
                    failures.Add($"{name} field {field} 0x{site.ValueOffset:X5}: reviewed native-writable original is zero");
                if (site.Owner < 0)
                    failures.Add($"{name} field {field} 0x{site.PacketOffset:X5}: packet has one owner with one CHAR");
            }

            // Every packet processed by its owner, as the engine would write it.
            var memory = new NativeMemory(field, prefix);
            var owners = sites.Where(site => site.Owner >= 0).Select(site => site.Owner).Distinct()
                .Select(owner => (owner, Slot(owner))).ToArray();
            memory.SetModels(0xFE, owners);
            foreach (var site in sites.Where(site => site.Owner >= 0))
                memory.Poke(site.ValueOffset, Slot(site.Owner));
            var processed = Observe(new LoadedFieldScriptIdentityReader(memory));
            if (!processed.Read || processed.Identity.ScriptPrefixSha256 != raw ||
                processed.Identity != untouched.Identity ||
                WholeGameEchoNavigationManifest.ResolveVariant(processed.Identity) != variant ||
                processed.Normalization.RawScriptPrefixSha256 != Sha(memory.Current()) ||
                processed.Normalization.RestoredOffsets.Count != sites.Count(site => site.Owner >= 0) ||
                processed.Normalization.UnexplainedNativeWrites != 0)
                failures.Add($"{name} field {field}: every native write is restored to the reviewed identity");
        }

        if (checkedFields != 702)
            failures.Add($"{name}: all 702 playable fields checked, found {checkedFields}");
        return (fields, total);

        static byte Slot(int owner) => (byte)(1 + owner % 200);
    }

    private static Ff7GameLanguageContext? Context(string root, Ff7GameLanguage language)
    {
        var context = new Ff7GameLanguageContext(Ff7GameLanguages.Get(language), Path.Combine(root, "data"),
            Ff7GameLanguageDetectionSource.Configuration, "native KAWAI identity regression");
        return File.Exists(context.FieldArchivePath) ? context : null;
    }

    private static byte[] Prefix(byte[] encoded)
    {
        var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
        var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
        var section = decoded.AsSpan(at + 4, length);
        return section[..BinaryPrimitives.ReadUInt16LittleEndian(section[4..])].ToArray();
    }

    private static void RequireRaw(List<string> failures, Synthetic script, int offset, byte value, string message)
    {
        var memory = Mixed(script);
        memory.Poke(offset, value);
        RequireUnrestored(failures, memory, message);
    }

    private static void RequireUnrestored(List<string> failures, NativeMemory memory, string message)
    {
        var observed = Observe(memory);
        if (!observed.Read || observed.Identity.ScriptPrefixSha256 != Sha(memory.Current()) ||
            observed.Normalization.RestoredOffsets.Count != 0)
            failures.Add(message);
    }

    private static Observation Observe(NativeMemory memory) => Observe(new LoadedFieldScriptIdentityReader(memory));

    private static Observation Observe(LoadedFieldScriptIdentityReader reader)
    {
        var read = reader.TryReadPrefix(out var identity, out var prefix, out var normalization);
        return new Observation(read, identity, prefix, normalization);
    }

    private static void Report(List<string> failures, string name, string pass)
    {
        foreach (var failure in failures) Console.WriteLine($"FAIL {name}: {failure}");
        if (failures.Count != 0) throw new InvalidOperationException($"{name} regressions: {failures.Count} failures.");
        Console.WriteLine("PASS " + pass);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private readonly record struct Observation(bool Read, LoadedFieldScriptIdentity Identity, byte[] Prefix,
        LoadedFieldScriptPrefixNormalization Normalization);

    /// <summary>
    /// Synthetic entities, live model slots in load order: dir none, cloud 0, choko 1, mixed 2,
    /// twochar 3, nochar none, host 4, guest 5, overlap 6. CHAR operands deliberately differ
    /// from slots.
    /// </summary>
    private static Synthetic BuildMixedScript() => Build(
        [
            Entity("dir", new Code().Add(0x00, 0x00)),
            Entity("cloud", new Code().Add(0xA1, 0x00, 0x00, 0x00)),
            Entity("choko", new Code().Add(0xA1, 0x05, 0x00).Mark("light").Add(Light(0))
                .Add(0x24, 0x01, 0x00, 0x28, 0x04, 0x06, 0x01, 0x29, 0x00)),
            Entity("mixed", new Code().Add(0xA1, 0x07, 0x00)
                .Mark("k7").Add(Kawai(6, 7, 0)).Mark("k8").Add(Kawai(6, 8, 0)).Mark("k9").Add(Kawai(6, 9, 0))
                .Mark("shine").Add(Kawai(5, 0x0D, 0)).Mark("trnsp").Add(Kawai(5, 0x01, 0))
                .Mark("apply").Add(Kawai(6, 6, 1))
                .Mark("short").Add(0x28, 0x04, 0x06, 0x00).Add(0x5F)
                .Mark("xyzi").Add(0xA5, 0x00, 0x00, 0x28, 0x18, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00)
                .Add(0x00)),
            // IFUB 5[0]==1 ? CHAR : CHAR; either way one model, from one of two CHARs.
            Entity("twochar", new Code().Add(0x14, 0x50, 0x00, 0x01, 0x00, 0x05, 0xA1, 0x01, 0x10, 0x03, 0xA1, 0x02, 0x00)
                .Mark("light").Add(Light(0)).Add(0x00)),
            Entity("nochar", new Code().Add(0x00).Mark("light").Add(Light(0)).Add(0x00)),
            Entity("host", new Code().Add(0xA1, 0x0B, 0x00).Mark("light").Add(Light(0)).Add(0x00)),
            Entity("guest", new Code().Add(0xA1, 0x0C, 0x00, 0x00), new Code().Add(0x00)),
            // Its slot 1 starts at the LIGHT packet's packet[1], so that byte is also code.
            Entity("overlap", new Code().Add(0xA1, 0x0D, 0x00).Mark("light").Add(Light(0)).Add(0x00), new Code().Add(0x00))
        ],
        (entries, marks) =>
        {
            entries[7][1] = marks["host.light"];
            entries[8][1] = marks["overlap.light"] + 4;
        });

    private static NativeMemory Mixed(Synthetic script, byte modelCount = 7)
    {
        var memory = new NativeMemory(900, script.Prefix);
        memory.SetModels(modelCount, (0, 0xFF), (1, 0), (2, 1), (3, 2), (4, 3), (5, 0xFF), (6, 4), (7, 5), (8, 6));
        return memory;
    }

    private static byte[] Light(byte value) =>
    [
        0x28, 0x18, 0x06, 0x00, value, 0x00, 0x10, 0x00, 0x20, 0x00, 0x30, 0x00,
        0x10, 0x00, 0x20, 0x00, 0x30, 0x00, 0x01, 0x00, 0x01, 0x00, 0x01, 0x01
    ];

    private static byte[] Kawai(byte length, byte subOpcode, byte first)
    {
        var packet = new byte[length];
        packet[0] = 0x28;
        packet[1] = length;
        packet[2] = subOpcode;
        packet[3] = first;
        return packet;
    }

    private sealed class Code
    {
        private readonly List<byte> bytes = [];
        private readonly Dictionary<string, int> marks = [];

        public byte[] Bytes => bytes.ToArray();

        public IReadOnlyDictionary<string, int> Marks => marks;

        public Code Add(params byte[] data)
        {
            bytes.AddRange(data);
            return this;
        }

        public Code Mark(string name)
        {
            marks[name] = bytes.Count;
            return this;
        }
    }

    private sealed record SyntheticEntity(string Name, Code[] Scripts);

    private sealed record Synthetic(byte[] Prefix, IReadOnlyDictionary<string, int> Marks);

    private static SyntheticEntity Entity(string name, params Code[] scripts) => new(name, scripts);

    private static Synthetic Build(params SyntheticEntity[] entities) => Build(entities, null);

    private static Synthetic Build(SyntheticEntity[] entities, Action<int[][], IReadOnlyDictionary<string, int>>? relink)
    {
        var table = 0x20 + entities.Length * 8;
        var code = table + entities.Length * 64;
        var body = new List<byte>();
        var entries = new int[entities.Length][];
        var marks = new Dictionary<string, int>();
        for (var entity = 0; entity < entities.Length; entity++)
        {
            entries[entity] = new int[32];
            var scripts = entities[entity].Scripts;
            for (var slot = 0; slot < scripts.Length; slot++)
            {
                entries[entity][slot] = code + body.Count;
                foreach (var (name, at) in scripts[slot].Marks)
                    marks[$"{entities[entity].Name}.{name}"] = entries[entity][slot] + at;
                body.AddRange(scripts[slot].Bytes);
            }

            var idle = code + body.Count;
            body.Add(0x00);
            for (var slot = scripts.Length; slot < 32; slot++)
                entries[entity][slot] = idle;
        }

        relink?.Invoke(entries, marks);
        var prefix = new byte[code + body.Count];
        BinaryPrimitives.WriteUInt16LittleEndian(prefix, 0x0502);
        prefix[2] = (byte)entities.Length;
        prefix[3] = (byte)entities.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(4), (ushort)prefix.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(8), 512);
        Encoding.ASCII.GetBytes("synth").CopyTo(prefix, 0x18);
        for (var entity = 0; entity < entities.Length; entity++)
        {
            Encoding.ASCII.GetBytes(entities[entity].Name).CopyTo(prefix, 0x20 + entity * 8);
            for (var slot = 0; slot < 32; slot++)
                BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(table + entity * 64 + slot * 2), (ushort)entries[entity][slot]);
        }

        body.CopyTo(prefix, code);
        return new Synthetic(prefix, marks);
    }

    /// <summary>The loaded script, the field context and the live model slots, as guest memory.</summary>
    private sealed class NativeMemory : ILegacyAddressSpace
    {
        private const uint Script = 0x02000000;
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly int prefixLength;
        private int prefixReads;

        public NativeMemory(int fieldId, byte[] prefix)
        {
            prefixLength = prefix.Length;
            for (var index = 0; index < prefix.Length; index++)
                bytes[Script + (uint)index] = prefix[index];
            bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = FieldPositionReader.FieldModule;
            SetField(fieldId);
            Span<byte> pointer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(pointer, Script);
            Write((uint)FieldScriptContextReader.AddressFieldScriptPtr, pointer);
        }

        public Action<NativeMemory>? BetweenPrefixReads { get; set; }

        public void SetField(int fieldId)
        {
            Span<byte> id = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(id, (ushort)fieldId);
            Write((uint)FieldScriptContextReader.AddressCurrentFieldId, id);
        }

        public void Poke(int offset, byte value) => bytes[Script + (uint)offset] = value;

        public void SetModels(byte count, params (int Entity, byte Slot)[] slots)
        {
            bytes[(uint)FieldPositionReader.AddressFieldNumModels] = count;
            foreach (var (entity, slot) in slots)
                bytes[(uint)(FieldScriptControllerReader.AddressEntityModelIds + entity)] = slot;
        }

        public void ForgetModel(int entity) =>
            bytes.Remove((uint)(FieldScriptControllerReader.AddressEntityModelIds + entity));

        public byte[] Current()
        {
            var current = new byte[prefixLength];
            for (var index = 0; index < prefixLength; index++)
                current[index] = bytes[Script + (uint)index];
            return current;
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index]))
                    return false;
            }

            if (virtualAddress == Script && destination.Length == prefixLength && ++prefixReads == 1)
                BetweenPrefixReads?.Invoke(this);
            return true;
        }

        private void Write(uint address, ReadOnlySpan<byte> data)
        {
            for (var index = 0; index < data.Length; index++)
                bytes[address + (uint)index] = data[index];
        }
    }
}
