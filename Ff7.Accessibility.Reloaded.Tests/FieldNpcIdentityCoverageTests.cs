using Ff7.Accessibility.Reloaded;
using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FieldNpcIdentityCoverageTests
{
    public static void Run()
    {
        var cases = new[]
        {
            ("main_n_cloud.char", "Cloud"),
            ("main_n_earith.char", "Aerith"),
            ("main_n_tifa.char", "Tifa"),
            ("main_ballet.char", "Barret"),
            ("main_red.char", "Red XIII"),
            ("main_yufi.char", "Yuffie"),
            ("main_ketcy.char", "Cait Sith"),
            ("main_vincent.char", "Vincent"),
            ("main_cid.char", "Cid"),
            ("market_earith.char", "Aerith"), ("market_tifa.char", "Tifa"),
            ("modify_clouds.char", "Cloud"),
            ("nible_tifa15.char", "Tifa"), ("nible_tifas.char", "Tifa"),
            ("junon_c_red.char", "Red XIII"), ("junon_c_ballet.char", "Barret"),
            ("sub_hojyo.char", "Hojo")
        };
        var failures = new List<string>();
        foreach (var (model, expected) in cases)
        {
            // These are the actual native resource families. The entity's script can
            // open another person's reply; that does not change the model's identity.
            var definition = new FieldScriptNpcDefinition(600, 3, "actor", [1], ModelResourceName: model);
            var target = Read(definition, ["Cloud", "“Let's go.”"]);
            if (target?.Label != expected)
                failures.Add($"{model}: expected {expected}, got {target?.Label ?? "<missing>"}");
            if (Read(definition with { SuppressInstalledDialogueLabels = true }, [])?.Label != expected)
                failures.Add($"{model}: native identity was lost with replacement text indices");
            if (Read(definition, ["Cloud", "“Let's go.”"], visible: false) is not null)
                failures.Add($"{model}: hidden actor was exposed");
            if (Read(definition, ["Cloud", "“Let's go.”"], talk: false) is not null)
                failures.Add($"{model}: disabled native interaction was exposed");
            if (Read(definition, [], controlled: true) is not null)
                failures.Add($"{model}: the current player model was listed as its own NPC");
            if (Read(definition with { ModelResourceName = "fieldbg_" + model },
                    ["Cloud", "“Let's go.”"]) is not null)
                failures.Add($"{model}: scenery was mistaken for a character");
        }

        var mismatchedReview = new FieldScriptNpcDefinition(428, 10, "earith", [14],
            ModelResourceName: "main_n_earith.char")
        {
            ReviewedStockEntityId = 10,
            ReviewedModelLabel = "Tifa",
            SuppressInstalledDialogueLabels = true
        };
        if (Read(mismatchedReview, ["Tifa", "“Hello.”"])?.Label != "Aerith")
            failures.Add("the current Aerith model must corroborate a cached speaker label");

        var homePriscilla = new FieldScriptNpcDefinition(431, 11, "prisl", [12],
            ModelResourceName: "sub_pricilla.char")
        {
            ReviewedStockEntityId = 11,
            SuppressInstalledDialogueLabels = true
        };
        if (Read(homePriscilla, [])?.Label != "Priscilla")
            failures.Add("Priscilla's visible/talkable home model must survive replacement text indices");

        var unbound = new FieldScriptNpcDefinition(600, 4, "other", [1],
            ModelResourceName: "unclassified.char");
        if (Read(unbound, ["Cloud", "“A reply.”"]) is not null)
            failures.Add("an unrelated actor cannot acquire Cloud's name from a reply");
        if (Read(unbound with { ReviewedModelLabel = "Cloud" }, []) is not null)
            failures.Add("an uncorroborated cached party heading is not actor identity");
        var requestedWindows = unbound with { DialogIds = [1, 2], OwnDialogIds = [2] };
        if (Read(requestedWindows, [], resolver: (_, id) => id == 1 ?
            ["Hojo", "“Another entity's window.”"] : ["Bugenhagen", "“This entity's own window.”"])?.Label != "Bugenhagen")
            failures.Add("another entity's requested script cannot supply this actor's name");

        // Echo-S adds a Tsuna cameo using Cloud's mesh. A reused mesh must not
        // turn a new, unmapped actor into the original party member.
        var cameo = new FieldScriptNpcDefinition(149, 9, "Tsuna", [1],
            ModelResourceName: "main_n_cloud.char")
        { ReviewedStockEntityId = -1, SuppressInstalledDialogueLabels = true };
        if (Read(cameo, []) is not null)
            failures.Add("an unreviewed reused mesh cannot establish the cameo's identity");
        if (Read(cameo with { ReviewedModelLabel = "Tsuna" }, [])?.Label != "Tsuna")
            failures.Add("a reviewed cameo identity must survive reuse of a party mesh");
        var extraAerith = cameo with { FieldId = 449, EntityId = 5, EntityName = "Aerith",
            ModelResourceName = "main_n_earith.char" };
        if (Read(extraAerith, [])?.Label != "Aerith")
            failures.Add("an unmapped actor corroborated as Aerith keeps her identity");
        var boundRed = cameo with { FieldId = 262, EntityId = 5, EntityName = "",
            ModelResourceName = "main_red.char", BoundCharacterId = 4 };
        if (Read(boundRed, [])?.Label != "Red XIII")
            failures.Add("an unnamed Echo actor's own PC binding corroborates Red XIII");
        var extraManager = cameo with { FieldId = 214, EntityId = 12, EntityName = "Manager",
            ModelResourceName = "market_buka.char" };
        if (Read(extraManager with { ReviewedModelLabel = "Honeybee Manor doorman" }, [])?.Label != "Honeybee Manor doorman")
            failures.Add("an added visible native person keeps its reviewed current-layout identity");

        var choreography = new FieldScriptNpcDefinition(431, 8, "cloud", [],
            InteractionLineEntityId: 5, InteractionLine: new(0, 0, 0, 80, 0, 0),
            ModelResourceName: "main_n_cloud.char");
        if (Read(choreography, [], lineEnabled: true) is not null)
            failures.Add("a party member used by a ladder/choreography LINE is not a conversation target");
        if (Read(choreography with { InteractionLineRunsTalk = true }, [], lineEnabled: true)?.Label != "Cloud")
            failures.Add("a real counter invoking the actor's Talk remains a conversation target");

        foreach (var failure in failures) Console.WriteLine("FAIL NPC identity: " + failure);
        if (failures.Count != 0) throw new InvalidOperationException($"NPC identity regressions: {failures.Count} failures.");
        Console.WriteLine("PASS NPC names follow native character models instead of dialogue replies.");
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var catalog = new FieldScriptNavigationCatalog(root);
        var resolver = new FlevelFieldTextResolver(root);
        var cases = new[]
        {
            (125, 4, "Barret"), (132, 3, "Barret"),
            (212, 2, "Aerith"), (212, 3, "Tifa"),
            (258, 2, "Aerith"), (258, 5, "Red XIII"),
            (400, 5, "Barret"), (428, 10, "Aerith"),
            (725, 4, "Cloud"), (726, 3, "Cloud"), (727, 1, "Cloud"), (727, 7, "Cloud"),
            (741, 13, "Hojo"), (605, 16, "Temple caretaker"), (720, 10, "Cloud"),
            (429, 12, "Priscilla"), (431, 11, "Priscilla"),
            (704, 16, "Cloaked figure"), (704, 17, "Cloaked figure"),
            (704, 20, "Cloaked figure"), (704, 21, "Cloaked figure")
        };
        var failures = new List<string>();
        foreach (var (fieldId, entity, expected) in cases)
        {
            var actor = catalog.ReadField(fieldId).Npcs.Single(n => n.EntityId == entity);
            var target = Read(actor, [], resolver: (field, message) => resolver.ReadMessageLinesById(field, message));
            if (target?.Label != expected)
                failures.Add($"native field {fieldId} entity {entity} ({actor.ModelResourceName}): expected {expected}, got {target?.Label ?? "<missing>"}");
        }
        var door = catalog.ReadField(616).Npcs.Single(n => n.EntityId == 16);
        if (Read(door, [], resolver: (field, message) => resolver.ReadMessageLinesById(field, message)) is not null)
            failures.Add("the temple's end door must not be an NPC called Cloud; its native Story route remains separate");
        foreach (var (field, entity, expected) in new[] {
            (451, 19, "Boy"), (575, 5, "Man"), (654, 26, "Man"), (650, 11, "Child") })
        {
            var counter = catalog.ReadField(field).Npcs.Single(n => n.EntityId == entity);
            if (!counter.HasConversationCounter)
                failures.Add($"native counter {field}:{entity} has a Confirm-owned dialogue or menu response");
            if (Read(counter, [], resolver: (f, d) => resolver.ReadMessageLinesById(f, d), lineEnabled: true)?.Label != expected)
                failures.Add($"native conversation counter {field}:{entity} remains {expected}");
            if (Read(counter, [], lineEnabled: false) is not null ||
                Read(counter, [], visible: false, lineEnabled: true) is not null)
                failures.Add($"native counter {field}:{entity} still requires its visible actor and enabled LINE");
        }
        foreach (var failure in failures) Console.WriteLine("FAIL NPC native identity: " + failure);
        if (failures.Count != 0) throw new InvalidOperationException($"Native NPC identity regressions: {failures.Count} failures.");
        Console.WriteLine("PASS native NPC identity witnesses across Midgar, Junon, Lifestream and Hojo.");

        var echoes = (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var echo in echoes)
        {
            var memory = new ScriptMemory();
            var loaded = new FieldScriptNavigationCatalog(root, Ff7GameLanguageDetector.Detect(root),
                new LoadedFieldScriptIdentityReader(memory));
            var native = new FieldScriptNavigationCatalog(echo);
            foreach (var (field, entity, model) in new[] {
                (291, 2, "shinra_hei.char"), (500, 10, "gold_mstaff.char"), (705, 19, "main_ballet.char") })
            {
                memory.Load(echo, field);
                var diskActor = native.ReadField(field).Npcs.Single(n => n.EntityId == entity);
                var current = loaded.ReadField(field);
                if (!current.IsUsable) throw new InvalidOperationException(current.Diagnostic);
                var liveActor = current.Npcs.Single(n => n.EntityId == entity);
                if (diskActor.ModelResourceName != model || liveActor.ModelResourceName != model)
                    throw new InvalidOperationException($"Echo native model slots {Path.GetFileName(echo)} {field}:{entity}: " +
                        $"expected {model}, disk={diskActor.ModelResourceName}, loaded={liveActor.ModelResourceName}");
            }
            foreach (var (field, entity, expected) in new[] {
                (451, 19, "Boy"), (575, 5, "Man"), (654, 26, "Man"), (650, 11, "Child") })
            {
                memory.Load(echo, field);
                var counter = loaded.ReadField(field).Npcs.Single(n => (n.ReviewedStockEntityId ?? n.EntityId) == entity);
                if (!counter.HasConversationCounter || Read(counter, [], lineEnabled: true)?.Label != expected ||
                    Read(counter, [], lineEnabled: false) is not null)
                    throw new InvalidOperationException($"Echo conversation counter {Path.GetFileName(echo)} {field}:{entity} lost its native response or LINE gate");
            }
        }
        if (echoes.Length > 0) Console.WriteLine($"PASS native empty model slots retain CHAR alignment in {echoes.Length} Echo-S variants.");
    }

    private static FieldNavigationTarget? Read(FieldScriptNpcDefinition definition,
        IReadOnlyList<string> dialogue, bool visible = true, bool talk = true,
        Func<int, int, IReadOnlyList<string>>? resolver = null, bool lineEnabled = false, bool controlled = false)
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
        var reader = new FieldNavigationNpcReader(Int, Short, Byte, resolver ?? ((_, _) => dialogue),
            _ => [definition], isLineEnabled: _ => lineEnabled, useVerifiedFallback: _ => false);
        return reader.ReadTargets(new(1, definition.FieldId, 0, 0, 0, 0, 0, 0))
            .Select(t => (FieldNavigationTarget?)t).SingleOrDefault();
    }

    private sealed class ScriptMemory : ILegacyAddressSpace
    {
        private const uint Script = 0x02000000;
        private readonly Dictionary<uint, byte> bytes = [];
        internal void Load(string root, int field)
        {
            var source = new FlevelDataSource(root);
            if (!source.TryReadField(field, out var encoded)) throw new InvalidOperationException("Missing native field");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
            var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
            Write(Script, decoded.AsSpan(at + 4, length));
            bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 1;
            Write((uint)FieldScriptContextReader.AddressCurrentFieldId, BitConverter.GetBytes((ushort)field));
            Write((uint)FieldScriptContextReader.AddressFieldScriptPtr, BitConverter.GetBytes(Script));
        }
        private void Write(uint at, ReadOnlySpan<byte> data)
        { for (var i = 0; i < data.Length; i++) bytes[at + (uint)i] = data[i]; }
        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
                if (!bytes.TryGetValue(address + (uint)i, out destination[i])) return false;
            return true;
        }
    }
}
