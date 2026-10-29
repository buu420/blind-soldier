using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Reviewed current-layout Echo-S NPC names (<see cref="EchoNpcIdentityCatalog"/>) and the native
/// gates around them. Every row is pinned to the loaded Echo-S entity it was reviewed against.
/// A proper name must be the heading of one of that entity's own windows in the variant's own
/// loaded text. A role is the stock reviewed role of the same mesh and the same conversation.
/// </summary>
internal static class EchoNpcIdentityCoverageTests
{
    private static readonly (int Field, int Entity, string EntityName, string Model, string Label, bool ProperName)[] Expected =
    [
        (66, 11, "boo", "cosmo_boo.char", "Bugenhagen", true),
        (80, 4, "m1", "another_guid.char", "Chocobo Sage", true),
        (119, 5, "av_w", "midgal_avafat.char", "Wedge", true),
        (149, 9, "Tsuna", "main_n_cloud.char", "Tsuna", true),
        (153, 8, "J_father", "7min1midgal_fatman.char", "Johnnys Dad", true),
        (153, 9, "J_mother", "7min1midgal_obasan2.char", "Johnnys Mom", true),
        (168, 6, "wedge", "4sbwy_5midgal_avafat.char", "Wedge", true),
        (214, 12, "Manager", "market_buka.char", "Honeybee Manor doorman", false),
        (214, 13, "Fat Man", "midgal_fatman.char", "Honeybee Manor attendant", false),
        (214, 15, "Punk", "market_usan.char", "Man", false),
        (214, 17, "Soldier1", "shinra_reifuku.char", "Shinra soldier", false),
        (214, 18, "Soldier2", "shinra_hei.char", "Shinra soldier", false),
        (220, 10, "muki1", "market_muki1.char", "Mukki", true),
        (229, 11, "UpRisen", "main_ballet.char", "UpRisen", true),
        (377, 9, "leno", "shinra_reno.char", "Reno", true),
        (377, 10, "tuon", "shinra_tuon.char", "Tseng", true),
        (377, 11, "irena", "shinra_irena.char", "Elena", true),
        (379, 14, "lude", "shinra_lude.char", "Rude", true),
        (401, 6, "sca", "sub_scarlet.char", "Scarlet", true),
        (445, 10, "mukki1", "market_muki1.char", "Mukki", true),
        (477, 8, "corts", "korel_korts.char", "Coates", true),
        (495, 4, "kubi", "gold_hang.char", "Mr. Hangman", true),
        (496, 16, "dio", "sub_dio.char", "Dio", true),
        (503, 5, "dio", "sub_dio.char", "Dio", true),
        (511, 11, "esto", "sub_esto.char", "Ester", true),
        (525, 28, "BUGEN", "cosmo_boo.char", "Bugenhagen", true),
        (530, 5, "CHORO", "cosmo_old.char", "Elder Hago", true),
        (531, 15, "BUGEN", "cosmo_boo.char", "Bugenhagen", true),
        (534, 5, "BUGEN", "cosmo_boo.char", "Bugenhagen", true),
        (541, 11, "BUGEN", "cosmo_boo.char", "Bugenhagen", true),
        (544, 11, "BUGEN", "cosmo_boo.char", "Bugenhagen", true),
        (544, 20, "CHORO", "cosmo_old.char", "Elder Hago", true),
        (558, 23, "siera", "sub_siera.char", "Shera", true),
        (558, 24, "palmer", "sub_palmer.char", "Palmer", true),
        (562, 10, "lude", "shinra_lude.char", "Rude", true),
        (580, 13, "IRENA", "shinra_irena.char", "Elena", true),
        (580, 14, "LUDE", "shinra_lude.char", "Rude", true),
        (580, 15, "RENO", "shinra_reno.char", "Reno", true),
        (586, 21, "godoh", "5towerutai_godoh.char", "Godo", true),
        (587, 12, "RENO", "shinra_reno.char", "Reno", true),
        (587, 13, "LUDE", "shinra_lude.char", "Rude", true),
        (589, 9, "GODO", "utai_godoh.char", "Godo", true),
        (592, 6, "reno", "shinra_reno.char", "Reno", true),
        (592, 7, "lude", "shinra_lude.char", "Rude", true),
        (594, 4, "lude", "shinra_lude.char", "Rude", true),
        (595, 4, "reno", "shinra_reno.char", "Reno", true),
        (596, 4, "lude", "shinra_lude.char", "Rude", true),
        (598, 4, "reno", "shinra_reno.char", "Reno", true),
        (599, 17, "lude", "shinra_lude.char", "Rude", true),
        (602, 16, "tuon", "shinra_tuon.char", "Tseng", true),
        (617, 4, "", "bone_man.char", "Excavation foreman", false),
        (642, 10, "", "cosmo_boo.char", "Bugenhagen", true)
    ];

    private static readonly string[] PlayableNames =
        ["Cloud", "Barret", "Tifa", "Aerith", "Aeris", "Red XIII", "Red", "Yuffie", "Cait Sith", "Vincent", "Cid"];

    public static void Run()
    {
        var failures = new List<string>();

        // The catalog carries exactly the reviewed rows.
        foreach (var row in Expected)
        {
            var label = EchoNpcIdentityCatalog.Label(row.Field, row.Entity);
            if (label != row.Label)
                failures.Add($"catalog row {row.Field}:{row.Entity} ({row.EntityName}): expected {row.Label}, got {label ?? "<none>"}");
        }
        foreach (var row in EchoNpcIdentityCatalog.Rows)
        {
            if (!Expected.Any(e => e.Field == row.FieldId && e.Entity == row.LoadedEntityId))
                failures.Add($"catalog row {row.FieldId}:{row.LoadedEntityId} {row.Label} has no reviewed evidence pin");
            if (PlayableNames.Contains(row.Label, StringComparer.OrdinalIgnoreCase))
                failures.Add($"catalog row {row.FieldId}:{row.LoadedEntityId} names a playable character; that is model identity's job");
            if (row.Label.Length == 0 || row.Label != row.Label.Trim() ||
                !row.Label.All(c => char.IsAsciiLetter(c) || c is ' ' or '.' or '\'' or '-'))
                failures.Add($"catalog row {row.FieldId}:{row.LoadedEntityId} label '{row.Label}' carries a stray glyph or spacing");
            // The Sector 7 reviewed Echo layout labels its own loaded cast.
            if (Sector7NavigationLayout.IsField(row.FieldId))
                failures.Add($"catalog row {row.FieldId}:{row.LoadedEntityId} overlaps the Sector 7 reviewed layout");
            if (WholeGameEchoNavigationManifest.EchoFingerprint(row.FieldId) is null)
                failures.Add($"catalog row {row.FieldId}:{row.LoadedEntityId} has no reviewed Echo-S layout");
        }

        // Nothing is named without a review: the temple's Demons Gate wall, Yuffie's lever
        // proxy, the Lifestream's unheaded young Cloud and an arbitrary entity stay unnamed.
        foreach (var (field, entity) in new[] { (616, 16), (582, 12), (727, 7), (500, 99) })
            if (EchoNpcIdentityCatalog.Label(field, entity) is { } named)
                failures.Add($"unreviewed {field}:{entity} acquired the name {named}");

        // A reviewed name still only reaches the player through the native gates.
        var bugenhagen = new FieldScriptNpcDefinition(66, 11, "boo", [3], ModelResourceName: "cosmo_boo.char")
        {
            ReviewedStockEntityId = 11,
            ReviewedModelLabel = EchoNpcIdentityCatalog.Label(66, 11),
            SuppressInstalledDialogueLabels = true,
            HasPerceivableTalk = true,
            OwnDialogIds = [3]
        };
        if (Read(bugenhagen)?.Label != "Bugenhagen")
            failures.Add("a drawn, talkable reviewed Echo-S actor keeps its reviewed name");
        if (Read(bugenhagen, visible: false) is not null)
            failures.Add("a hidden reviewed actor was exposed");
        if (Read(bugenhagen, talk: false) is not null)
            failures.Add("a reviewed actor with native Talk disabled was exposed");
        if (Read(bugenhagen, controlled: true) is not null)
            failures.Add("the controlled player model was listed under a reviewed name");
        if (Read(bugenhagen with { DialogIds = [], OwnDialogIds = [], HasPerceivableTalk = false }) is not null)
            failures.Add("a reviewed name cannot make an actor without a perceivable Talk a conversation target");
        if (Read(bugenhagen with { ModelResourceName = "fieldbg_cosmo_boo.char" }) is not null)
            failures.Add("a reviewed name cannot turn scenery into a person");

        // The Echo-S author cameo reuses Cloud's mesh. Unmapped and uncorroborated as Cloud, it
        // keeps the name its own windows give it; the mesh does not make it Cloud.
        var tsuna = new FieldScriptNpcDefinition(149, 9, "Tsuna", [0], ModelResourceName: "main_n_cloud.char")
        {
            ReviewedStockEntityId = -1,
            ReviewedModelLabel = EchoNpcIdentityCatalog.Label(149, 9),
            SuppressInstalledDialogueLabels = true,
            HasPerceivableTalk = true,
            OwnDialogIds = [0]
        };
        if (Read(tsuna)?.Label != "Tsuna")
            failures.Add("the unmapped Tsuna cameo must keep its own reviewed name, not Cloud's");

        foreach (var failure in failures) Console.WriteLine("FAIL Echo NPC identity: " + failure);
        if (failures.Count != 0) throw new InvalidOperationException($"Echo NPC identity regressions: {failures.Count} failures.");
        Console.WriteLine($"PASS {Expected.Length} reviewed Echo-S NPC identities keep their native gates.");
    }

    public static void RunWithInstalledGameData() => RunWithInstalledGameData(null);

    /// <param name="emulateCatalogWiring">
    /// Null for the shipping test hosts. The private implementation harness passes the proposed
    /// one-line catalog wiring here to check these expectations before that line lands.
    /// </param>
    internal static void RunWithInstalledGameData(Func<int, FieldScriptNpcDefinition, FieldScriptNpcDefinition>? emulateCatalogWiring)
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var echoes = (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (string.IsNullOrWhiteSpace(root)) return;
        var language = Ff7GameLanguageDetector.Detect(root);
        var failures = new List<string>();
        var fields = Expected.Select(row => row.Field).Distinct().Order().ToArray();

        // Stock layouts never consult the Echo-S table.
        {
            var memory = new ScriptMemory();
            var loaded = new FieldScriptNavigationCatalog(root, language, new LoadedFieldScriptIdentityReader(memory));
            foreach (var field in fields)
            {
                memory.Load(root, field);
                var current = loaded.ReadField(field);
                if (!current.IsUsable) { failures.Add($"stock {field}: {current.Diagnostic}"); continue; }
                if (current.UsesReviewedEchoLayout || current.Npcs.Any(n => n.ReviewedModelLabel is not null))
                    failures.Add($"stock {field}: an Echo-S reviewed label reached a stock layout");
            }
        }

        foreach (var echo in echoes)
        {
            var variant = Path.GetFileName(echo.TrimEnd('\\', '/'));
            var memory = new ScriptMemory();
            var loaded = new FieldScriptNavigationCatalog(root, language, new LoadedFieldScriptIdentityReader(memory));
            var ownText = new FlevelFieldTextResolver(echo);
            FieldScriptNavigationReadResult Current(int field)
            {
                memory.Load(echo, field);
                var result = loaded.ReadField(field);
                if (!result.IsUsable || !result.UsesReviewedEchoLayout)
                    throw new InvalidOperationException($"{variant} {field}: not an accepted Echo-S layout ({result.Diagnostic})");
                return emulateCatalogWiring is null ? result :
                    result with { Npcs = result.Npcs.Select(n => emulateCatalogWiring(field, n)).ToArray() };
            }

            foreach (var row in Expected)
            {
                var actor = Current(row.Field).Npcs.SingleOrDefault(n => n.EntityId == row.Entity);
                if (actor == default)
                {
                    failures.Add($"{variant} {row.Field}:{row.Entity}: reviewed actor is not a native NPC definition");
                    continue;
                }

                if (actor.EntityName != row.EntityName || actor.ModelResourceName != row.Model)
                {
                    failures.Add($"{variant} {row.Field}:{row.Entity}: reviewed against {row.EntityName}/{row.Model}, loaded {actor.EntityName}/{actor.ModelResourceName}");
                    continue;
                }

                // The name is the heading of one of this actor's own windows in this variant's
                // own loaded text, never a window another entity opens.
                if (row.ProperName && !(actor.OwnDialogIds ?? []).Any(id =>
                        ownText.ReadMessageLinesById(row.Field, id) is { Count: >= 2 } lines &&
                        lines[0].Trim() == row.Label))
                    failures.Add($"{variant} {row.Field}:{row.Entity}: {row.Label} is not the heading of an own window");

                // A counter that runs the actor's own Talk (Battle Square's Dio) is reached only
                // while that LINE is live; everyone else is reached at their own model.
                var counter = actor.InteractionLineRunsTalk;
                var target = Read(actor, lineEnabled: counter);
                if (target?.Label != row.Label)
                    failures.Add($"{variant} {row.Field}:{row.Entity} ({row.EntityName}): expected {row.Label}, got {target?.Label ?? "<missing>"}");
                if (Read(actor, visible: false, lineEnabled: counter) is not null)
                    failures.Add($"{variant} {row.Field}:{row.Entity}: hidden actor was exposed");
                if (Read(actor, controlled: true, lineEnabled: counter) is not null)
                    failures.Add($"{variant} {row.Field}:{row.Entity}: the controlled model was listed as an NPC");
                if (!actor.ContactOnly && Read(actor, talk: false, lineEnabled: false) is not null)
                    failures.Add($"{variant} {row.Field}:{row.Entity}: native Talk disabled (and no live counter) but still offered");
                if (counter && Read(actor, lineEnabled: false) is not null)
                    failures.Add($"{variant} {row.Field}:{row.Entity}: offered while its counter LINE is switched off");
            }

            // Ladders and choreography LINEs do not make party actors conversation targets.
            foreach (var (field, entity) in new[] { (431, 8), (431, 9), (431, 10), (432, 3), (432, 4), (432, 5) })
            {
                var actor = Current(field).Npcs.Single(n => n.EntityId == entity);
                if (Read(actor, lineEnabled: true) is { } resurrected)
                    failures.Add($"{variant} {field}:{entity}: a choreography LINE resurrected {resurrected.Label}");
            }

            // Unmapped actors keep their native identity: Red XIII by his own PC binding, Tifa by
            // her flashback mesh and entity name.
            foreach (var (field, entity, label) in new[] { (262, 5, "Red XIII"), (323, 6, "Tifa") })
            {
                var actor = Current(field).Npcs.Single(n => n.EntityId == entity);
                if (Read(actor)?.Label != label)
                    failures.Add($"{variant} {field}:{entity}: expected {label}, got {Read(actor)?.Label ?? "<missing>"}");
            }

            // Gongaga's reviewed counters still offer Tifa and Aerith at their native LINEs while
            // their conversations are pending, and not afterwards.
            {
                var current = Current(518);
                var map = WholeGameEchoNavigationManifest.EntityMap(518);
                foreach (var (stockEntity, stockLine, label) in new[] { (15, 9, "Tifa"), (16, 8, "Aerith") })
                {
                    if (!map.TryGetValue(stockEntity, out var entity) || !map.TryGetValue(stockLine, out var line) ||
                        !current.NativeLines.TryGetValue(line, out var segment))
                    {
                        failures.Add($"{variant} 518: reviewed counter {label} is not mapped");
                        continue;
                    }

                    var pending = ReadMerged(current, loaded, entity, pending: true);
                    if (pending is not { } offered || offered.Label != label || offered.TriggerLine != segment)
                        failures.Add($"{variant} 518: pending {label} counter expected at {segment}, got {pending?.Label ?? "<missing>"} {pending?.TriggerLine}");
                    if (ReadMerged(current, loaded, entity, pending: false) is not null)
                        failures.Add($"{variant} 518: {label}'s finished conversation is still offered");
                }
            }
        }

        foreach (var failure in failures) Console.WriteLine("FAIL Echo NPC native identity: " + failure);
        if (failures.Count != 0) throw new InvalidOperationException($"Echo NPC native identity regressions: {failures.Count} failures.");
        if (echoes.Length > 0)
            Console.WriteLine($"PASS {Expected.Length} reviewed Echo-S NPC identities in {echoes.Length} Echo-S variants; stock layouts unaffected.");
    }

    private static FieldNavigationTarget? Read(FieldScriptNpcDefinition definition, bool visible = true,
        bool talk = true, bool lineEnabled = false, bool controlled = false)
    {
        const int events = 0x03000000;
        const int model = 1;
        var npc = events + model * FieldNavigationObjectReader.FieldEventDataStride;
        int Int(int at) => at == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events :
            at == npc + FieldNavigationObjectReader.PositionXOffset ? 100 * 4096 : 0;
        short Short(int at) => at == events + FieldNavigationNpcReader.CollisionRadiusOffset ||
            at == npc + FieldNavigationNpcReader.TalkRadiusOffset ? (short)40 : (short)0;
        byte Byte(int at) => at == FieldPositionReader.AddressFieldNumModels ? (byte)2 :
            at == FieldNavigationObjectReader.AddressFieldModelIdArray + definition.EntityId ? (controlled ? (byte)0 : (byte)model) :
            at == npc + FieldNavigationObjectReader.VisibilityOffset ? (visible ? (byte)1 : (byte)0) :
            at == npc + FieldNavigationNpcReader.TalkDisabledOffset ? (talk ? (byte)0 : (byte)1) : (byte)0;
        var reader = new FieldNavigationNpcReader(Int, Short, Byte, (_, _) => [],
            _ => [definition], isLineEnabled: _ => lineEnabled, useVerifiedFallback: _ => false);
        return reader.ReadTargets(new(1, definition.FieldId, 0, 0, 0, 0, 0, 0))
            .Select(t => (FieldNavigationTarget?)t).SingleOrDefault();
    }

    /// <summary>The runtime's merged read of one drawn actor, with reviewed counters and remapping.</summary>
    private static FieldNavigationTarget? ReadMerged(FieldScriptNavigationReadResult current,
        FieldScriptNavigationCatalog catalog, int entity, bool pending)
    {
        const int events = 0x03000000;
        const int model = 1;
        var npc = events + model * FieldNavigationObjectReader.FieldEventDataStride;
        var fieldId = current.Npcs.Count > 0 ? current.Npcs[0].FieldId : 0;
        int Int(int at) => at == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0;
        short Short(int at) => at == events + FieldNavigationNpcReader.CollisionRadiusOffset ||
            at == npc + FieldNavigationNpcReader.TalkRadiusOffset ? (short)40 : (short)0;
        byte Byte(int at) => at == FieldPositionReader.AddressFieldNumModels ? (byte)2 :
            at >= FieldNavigationObjectReader.AddressFieldModelIdArray && at < FieldNavigationObjectReader.AddressFieldModelIdArray + 256
                ? (at == FieldNavigationObjectReader.AddressFieldModelIdArray + entity ? (byte)model : (byte)0xFF) :
            at == npc + FieldNavigationObjectReader.VisibilityOffset ? (byte)1 :
            at == FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 129 ? (pending ? (byte)0x06 : (byte)0) : (byte)0;
        var reader = new FieldNavigationNpcReader(Int, Short, Byte, (_, _) => [],
            _ => current.Npcs, isLineEnabled: _ => true, useVerifiedFallback: _ => true,
            definitionRemapper: catalog.RemapNpcDefinition);
        return reader.ReadTargets(new(1, fieldId, 0, 0, 0, 0, 0, 0))
            .Where(t => t.StableId == $"npc:{fieldId}:{entity}")
            .Select(t => (FieldNavigationTarget?)t).SingleOrDefault();
    }

    private sealed class ScriptMemory : ILegacyAddressSpace
    {
        private const uint Script = 0x02000000;
        private byte[] section = [];
        private readonly byte[] module = [1];
        private readonly byte[] fieldWord = new byte[2];
        private readonly byte[] pointer = new byte[4];

        internal void Load(string root, int field)
        {
            var source = new FlevelDataSource(root);
            if (!source.TryReadField(field, out var encoded)) throw new InvalidOperationException($"Missing native field {field} in {root}");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
            var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
            section = decoded.AsSpan(at + 4, length).ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(fieldWord, (ushort)field);
            BinaryPrimitives.WriteUInt32LittleEndian(pointer, Script);
        }

        public bool TryRead(uint address, Span<byte> destination) =>
            Copy(address, (uint)FieldScriptContextReader.AddressCurrentModule, module, destination) ||
            Copy(address, (uint)FieldScriptContextReader.AddressCurrentFieldId, fieldWord, destination) ||
            Copy(address, (uint)FieldScriptContextReader.AddressFieldScriptPtr, pointer, destination) ||
            Copy(address, Script, section, destination);

        private static bool Copy(uint address, uint start, byte[] data, Span<byte> destination)
        {
            if (address < start || (ulong)address + (ulong)destination.Length > start + (ulong)data.Length) return false;
            data.AsSpan((int)(address - start), destination.Length).CopyTo(destination);
            return true;
        }
    }
}
