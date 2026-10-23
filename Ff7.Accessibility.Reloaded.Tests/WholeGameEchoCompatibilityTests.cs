using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>Reproduces stale stock identities against the actual current Echo-S native scripts.</summary>
internal static class WholeGameEchoCompatibilityTests
{
    public static void Run()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var echoes = (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (string.IsNullOrWhiteSpace(root) || echoes.Length == 0)
        {
            Console.WriteLine("Whole-game Echo-S installed-data checks require stock and Echo-S roots.");
            return;
        }
        var failures = new List<string>();
        var filter = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_TEST_FILTER");
        void Case(string name, Action action)
        {
            if (!string.IsNullOrWhiteSpace(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { action(); Console.WriteLine("PASS Echo-S " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.GetBaseException().Message); }
        }
        foreach (var descriptor in Ff7GameLanguages.All)
        {
            var language = new Ff7GameLanguageContext(descriptor, Path.Combine(root, "data"), Ff7GameLanguageDetectionSource.Configuration, "exact installed official archive regression");
            if (!File.Exists(language.FieldArchivePath)) continue;
            Case(descriptor.Code + " reviewed localized wind-valley objects", () =>
            {
                var state = new ScriptMemory();
                var loaded = new FieldScriptNavigationCatalog(root, language, new LoadedFieldScriptIdentityReader(state));
                state.Load(root,709,language);
                var european = descriptor.Language is Ff7GameLanguage.French or Ff7GameLanguage.German or Ff7GameLanguage.Spanish;
                foreach (var id in new[] {20,21})
                {
                    var original = FieldNavigationObjectCatalog.CreateAllFields().First(d => d.FieldId == 709 && d.EntityId == id);
                    var current = loaded.RemapObjectDefinition(original);
                    Equal(european ? id+1 : id, current?.EntityId, "native zcheck insertion preserves the actual kusuri object identity");
                }
                if (european)
                {
                    var field = loaded.ReadField(709);
                    Equal(true, field.UsesReviewedLocalizedStockLayout, "reviewed official identity mapping is explicit");
                    Equal(false, field.UsesReviewedEchoLayout, "localized stock keeps its stock behavior");
                    Equal(true, field.Npcs.All(n => !n.SuppressInstalledDialogueLabels), "selected-language native dialogue titles remain eligible");
                }
            });
            Case(descriptor.Code + " official localized native matrix", () =>
            {
                var native = new FieldScriptNavigationCatalog(root, language);
                var state = new ScriptMemory();
                var identities = new LoadedFieldScriptIdentityReader(state);
                var loaded = new FieldScriptNavigationCatalog(root, language, identities);
                var source = new FlevelDataSource(root, language);
                var count = 0;
                foreach (var field in source.FieldNames.Keys)
                {
                    if (!native.ReadDiskField(field).IsUsable) continue;
                    state.Load(root, field, language);
                    Equal(true, identities.TryRead(out var observed), "coherent localized identity");
                    Equal(SupportedFieldScriptVariant.Vanilla, WholeGameEchoNavigationManifest.ResolveVariant(observed), $"{descriptor.Code} field {field} exact official prefix");
                    Equal(true, loaded.ReadField(field).IsUsable, $"{descriptor.Code} field {field} current navigation");
                    if (observed.ScriptPrefixSha256 != EchoSCompatibilityManifest.GetVanillaFingerprint(field))
                        Equal(SupportedFieldScriptVariant.Unknown, EchoSCompatibilityManifest.ResolveVariant(observed), "English action offsets cannot authorize localized bytes");
                    count++;
                }
                Equal(702, count, "all readable official fields checked");
                state.Load(echoes[0],449);
                Equal("main_n_earith.char", loaded.ReadField(449).Npcs.Single(n => n.EntityId == 5).ModelResourceName, "reviewed Echo uses its own curated native models over selected-language stock metadata");
                state.Load(root, 256, language);
                state.Bytes[ScriptMemory.Script + 12]++;
                Equal(false, loaded.ReadField(256).IsUsable, "an unreviewed localized override is rejected");
            });
        }
        foreach (var echo in echoes)
        {
            var state = new ScriptMemory();
            var catalog = new FieldScriptNavigationCatalog(root, Ff7GameLanguageDetector.Detect(root), new LoadedFieldScriptIdentityReader(state));
            var variant = Path.GetFileName(echo);
            Case(variant + " Sector 8 ladder exits retain their mount action", () =>
            {
                foreach (var (field, entities) in new[] { (733, new[] { 3, 4, 5 }), (734, new[] { 8, 9 }) })
                {
                    state.Load(echo, field);
                    var current = catalog.ReadField(field);
                    Equal(true, current.IsUsable && current.UsesReviewedEchoLayout, "checked current Echo-S field");
                    foreach (var entity in entities)
                    {
                        var exit = current.Exits.Single(e => e.TriggerEntityId == entity);
                        Equal(true, current.Transitions.Any(t => t.StableId == exit.ExitTraversalId &&
                            t.SourceEntityId == entity && t.Kind == FieldNavigationTransitionKind.Ladder && t.RequiresAction),
                            $"{field} line {entity} routes its native mount before exiting");
                        Equal(false, exit.CompletesOnArrival, "the mount line does not complete the exit");
                    }
                    var future = catalog.ReadFutureFieldMetadata(field, true);
                    Equal(true, future.Exits.Where(e => entities.Contains(e.TriggerEntityId)).All(e =>
                        e.ExitTraversalId is { Length: > 0 } && !e.CompletesOnArrival),
                        "future detours preserve the same ladder-exit semantics");
                }
            });
            Case(variant + " Shinra floor 67", () =>
            {
                state.Load(echo, 256);
                var exits = catalog.ReadField(256).Exits;
                Equal(true, exits.Any(e => e.TriggerEntityId == 8 && e.DestinationFieldIds!.Contains(259)), "sjp is loaded entity 8");
                Equal(true, exits.Any(e => e.TriggerEntityId == 15 && e.DestinationFieldIds!.Contains(262)), "floor 68 elevator is loaded entity 15");
            });
            Case(variant + " Shinra floor 69 keycard", () =>
            {
                state.Load(echo, 264);
                var guards = catalog.ReadField(264).ExitGuards.Values.SelectMany(g => g).SelectMany(g => g.Alternatives).SelectMany(g => g);
                Equal(true, guards.Any(g => g.Block == 1 && g.Address == 224 && g.Value == 69), "loaded keycard 69 guard is retained");
            });
            Case(variant + " Shinra manual story keeps the current native guard", () =>
            {
                state.Load(echo, 264);
                var row = FieldStoryEventCatalog.CreateAllFields().First(d => d.FieldId == 264 && d.Label == "Enter the left elevator for the escape battles");
                var mapped = catalog.RemapStoryDefinition(row);
                var availability = mapped.HasValue ? typeof(FieldStoryEventDefinition).GetProperty("NativeAvailabilityGuards")?.GetValue(mapped.Value) : null;
                Equal(true, availability is not null, "manual Story must retain Echo-S's actual keycard guard");
                state.Word((uint)FieldNavigationObjectReader.AddressFieldBankBase, 311);
                var card = (uint)FieldNavigationObjectReader.AddressFieldBankBase + 224;
                var reader = new FieldStoryTargetReader(_ => 0, _ => 30, address => state.Bytes.GetValueOrDefault((uint)address),
                    [row], _ => true, remapDefinition: catalog.RemapStoryDefinition, nativeGuardReader: catalog.ReadNativeGuard);
                var position = new FieldPositionSnapshot(1,264,0,0,0,0,0,0);
                state.Bytes[card] = 68;
                Equal(0, reader.ReadTargets(position).Count, "keycard 68 cannot open the current elevator");
                state.Bytes[card] = 69;
                Equal(1, reader.ReadTargets(position).Count, "keycard 69 permits the current elevator");
                state.Bytes.Remove(card);
                Equal(0, reader.ReadTargets(position).Count, "unreadable current native condition does not authorize a target");
                var current = catalog.ReadField(264);
                var left = current.Exits.Where(e => e.TriggerEntityId == 16).ToArray();
                Equal(1, FieldScriptExitGuards.Apply(left, current.ExitGuards, state).Count, "stock unreadable guard behavior stays unchanged");
                Equal(0, FieldScriptExitGuards.Apply(left, current.ExitGuards, state, catalog.ReadNativeGuard).Count, "reviewed Echo exit cannot use unreadable native keycard");
                state.Bytes[card] = 68;
                Equal(0, FieldScriptExitGuards.Apply(left, current.ExitGuards, state, catalog.ReadNativeGuard).Count, "reviewed Echo exit rejects keycard 68");
                state.Bytes[card] = 69;
                Equal(1, FieldScriptExitGuards.Apply(left, current.ExitGuards, state, catalog.ReadNativeGuard).Count, "reviewed Echo exit accepts coherent keycard 69");
            });
            Case(variant + " retained train and Nibelheim native conditions", () =>
            {
                state.Load(echo, 139);
                var train = catalog.ReadField(139);
                var forward = train.Exits.Where(e => e.TriggerEntityId == 26).ToArray();
                state.Word((uint)FieldNavigationObjectReader.AddressFieldBankBase, 108);
                state.Bytes[(uint)FieldScriptExitGuards.AddressTemporaryFieldBank + 32] = 0;
                state.Bytes[(uint)FieldScriptExitGuards.AddressTemporaryFieldBank + 255] = 0;
                Equal(1, FieldScriptExitGuards.Apply(forward, train.ExitGuards, state, catalog.ReadNativeGuard).Count, "current forward train MAPJUMP is open");
                state.Word((uint)FieldNavigationObjectReader.AddressFieldBankBase, 107);
                Equal(0, FieldScriptExitGuards.Apply(forward, train.ExitGuards, state, catalog.ReadNativeGuard).Count, "current native train progression IF remains effective");
                state.Word((uint)FieldNavigationObjectReader.AddressFieldBankBase, 108);
                state.Bytes[(uint)FieldScriptExitGuards.AddressTemporaryFieldBank + 32] = 1;
                Equal(0, FieldScriptExitGuards.Apply(forward, train.ExitGuards, state, catalog.ReadNativeGuard).Count, "training scene branch does not claim the MAPJUMP");
                state.Bytes[(uint)FieldScriptExitGuards.AddressTemporaryFieldBank + 32] = 0;
                state.Bytes.Remove((uint)FieldScriptExitGuards.AddressTemporaryFieldBank + 255);
                Equal(0, FieldScriptExitGuards.Apply(forward, train.ExitGuards, state, catalog.ReadNativeGuard).Count, "unreadable Echo visit latch cannot authorize train exit");
                state.Load(echo, 279);
                var gate = catalog.ReadField(279);
                var route = gate.Exits.Where(e => e.TriggerEntityId == 5).ToArray();
                state.Word((uint)FieldNavigationObjectReader.AddressFieldBankBase, 383);
                Equal(1, FieldScriptExitGuards.Apply(route, gate.ExitGuards, state, catalog.ReadNativeGuard).Count, "current Nibelheim native MAPJUMP is open below384");
                state.Word((uint)FieldNavigationObjectReader.AddressFieldBankBase, 384);
                Equal(0, FieldScriptExitGuards.Apply(route, gate.ExitGuards, state, catalog.ReadNativeGuard).Count, "current Nibelheim native guard blocks384");
                state.Bytes.Remove((uint)FieldNavigationObjectReader.AddressFieldBankBase);
                Equal(0, FieldScriptExitGuards.Apply(route, gate.ExitGuards, state, catalog.ReadNativeGuard).Count, "unreadable Nibelheim progression cannot authorize exit");
            });
            Case(variant + " reactor elevator rename", () =>
            {
                state.Load(echo, 121);
                var row = FieldStoryEventCatalog.CreateAllFields().First(d => d.FieldId == 121 && d.EntityId == 10);
                var mapped = catalog.RemapStoryDefinition(row);
                Equal(7, mapped?.EntityId, "jp0 is the reviewed renamed Door entity 7");
                Equal(-174, mapped?.TriggerLine?.StartX, "current doorway geometry replaces the stock line");
            });
            Case(variant + " removed Reactor 5 Materia", () =>
            {
                state.Load(echo, 132);
                var row = FieldNavigationObjectCatalog.CreateAllFields().First(d => d.FieldId == 132 && d.EntityId == 5);
                Equal(true, catalog.RemapObjectDefinition(row) is null, "removed mtr cannot select smoke1 at entity 5");
            });
            Case(variant + " Northern Crater line", () =>
            {
                state.Load(echo, 749);
                var row = FieldStoryEventCatalog.CreateAllFields().First(d => d.FieldId == 749 && d.EntityId == 33);
                var mapped = catalog.RemapStoryDefinition(row);
                Equal(31, mapped?.EntityId, "down_9 is loaded entity 31");
                Equal(34, mapped?.TriggerLine?.StartX, "the reviewed native down_9 line starts at x34");
                Equal(31, mapped?.RequiredEnabledLineEntityId, "availability reads the loaded LINE");
            });
            Case(variant + " unknown prefix cannot reuse cache", () =>
            {
                state.Load(echo, 256); _ = catalog.ReadField(256);
                state.Bytes[ScriptMemory.Script + 12]++;
                Equal(false, catalog.ReadField(256).IsUsable, "unreviewed script is unavailable");
            });
            Case(variant + " torn prefix cannot reuse cache", () =>
            {
                state.Load(echo, 256); _ = catalog.ReadField(256); state.TearAddress = ScriptMemory.Script + 32;
                Equal(false, catalog.ReadField(256).IsUsable, "changing loaded bytes are unavailable");
            });
            Case(variant + " unloaded field cannot reuse cache", () =>
            {
                state.Load(echo, 256); _ = catalog.ReadField(256); state.Word((uint)FieldScriptContextReader.AddressCurrentFieldId, 257);
                Equal(false, catalog.ReadField(256).IsUsable, "field identity disagreement is unavailable");
            });
            Case(variant + " installed future metadata remains separate", () =>
            {
                state.Load(echo, 256);
                Equal(true, catalog.ReadDiskField(256).Exits.Any(e => e.TriggerEntityId == 18), "future routing retains installed metadata");
            });
            Case(variant + " complete native field matrix", () => Matrix(root, echo));
            Case(variant + " every retained narration anchor", () => Narration(echo));
            Case(variant + " future Gongaga topology and arrival", () =>
            {
                state.Load(echo, 514);
                var resolver = new FieldCrossFieldApproachResolver(catalog, new FlevelDataSource(root),
                    new FieldWalkmeshRoutePlanner(new FieldWalkmeshReader(_ => 0, _ => 0)), _ => null);
                _ = resolver.Resolve(new FieldPositionSnapshot(1,514,0,0,0,0,0,0),
                    new FieldNavigationTarget(514,FieldNavigationCategory.Objects,"audit target",0,0,0), []);
                var cache = (System.Collections.IDictionary)typeof(FieldCrossFieldApproachResolver)
                    .GetField("fields", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(resolver)!;
                var field = cache[514]!;
                var gateways = ((System.Collections.IEnumerable)field.GetType().GetProperty("Gateways")!.GetValue(field)!).Cast<object>().ToArray();
                var gateway = gateways.Single(g => (int)g.GetType().GetProperty("Index")!.GetValue(g)! == 0);
                Equal(516, (int)gateway.GetType().GetProperty("Destination")!.GetValue(gateway)!, "future atlas follows the actual Echo-S reactor connection");
                Equal(110, (int)gateway.GetType().GetProperty("ArrivalTriangle")!.GetValue(gateway)!, "actual Echo-S arrival triangle");
            });
            Case(variant + " Highwind and regroup visible named party survive", () =>
            {
                foreach (var expected in new[] { (70,9,"Barret"),(70,10,"Red XIII"),(70,12,"Cait Sith"),(70,13,"Vincent"),(70,14,"Cid"),
                    (72,7,"Tifa"),(72,8,"Barret"),(72,9,"Red XIII"),(72,11,"Cait Sith"),(72,12,"Vincent"),(72,13,"Cid"),
                    (170,2,"Barret"),(170,3,"Aerith"),(170,4,"Tifa"),(170,6,"Red XIII"),
                    (293,3,"Tifa"),(293,4,"Barret"),(293,5,"Red XIII"),(293,6,"Yuffie"),(293,7,"Cait Sith"),(293,8,"Vincent"),(293,9,"Cid") })
                {
                    state.Load(echo, expected.Item1);
                    var definition = catalog.ReadField(expected.Item1).Npcs.Single(n => n.EntityId == expected.Item2);
                    AssertVisibleNamedNpc(definition, expected.Item3);
                }
            });
            Case(variant + " every reviewed visible party identity and known service survives", () =>
            {
                var count = 0;
                var native = new FlevelDataSource(echo);
                foreach (var field in native.FieldNames.Keys)
                {
                    if (!native.TryReadField(field, out _)) continue;
                    var labels = WholeGameEchoNavigationManifest.NpcLabels(field);
                    if (labels.Count == 0) continue;
                    state.Load(echo, field);
                    var current = catalog.ReadField(field);
                    foreach (var pair in labels)
                    {
                        var definition = current.Npcs.Single(n => n.EntityId == pair.Key);
                        Equal(pair.Value, definition.ReviewedModelLabel, "reviewed current native label survives catalog adaptation");
                        AssertVisibleNamedNpc(definition, pair.Value);
                        count++;
                    }
                }
                Equal(376, count, "complete reviewed native party inventory");
                foreach (var expected in new[] { (70,17,"Pilot"),(72,16,"Pilot"),(73,12,"Operations crew member"),(76,6,"Chocobo handler"),(79,4,"Weapon seller") })
                {
                    state.Load(echo, expected.Item1);
                    var definition = catalog.ReadField(expected.Item1).Npcs.Single(n => n.ReviewedStockEntityId == expected.Item2);
                    AssertVisibleNamedNpc(definition, expected.Item3);
                }
                state.Load(echo,149);
                Equal(true, catalog.ReadField(149).Npcs.Single(n => n.EntityName == "Tsuna").ReviewedModelLabel is null, "sharing Cloud's mesh cannot name a new unknown character Cloud");
                state.Load(echo,582);
                Equal(true, catalog.ReadField(582).Npcs.Single(n => n.EntityName == "SWITCH").ReviewedModelLabel is null, "Cloud-shaped scenery does not acquire a party identity");
            });
            Case(variant + " shuffled dialogue cannot name an Echo-S NPC from stock text", () =>
            {
                state.Load(echo, 449);
                var definition = catalog.ReadField(449).Npcs.Single(n => n.EntityId == 5);
                var stockHeadingReads = 0;
                const int events = 0x03000000;
                var reader = new FieldNavigationNpcReader(
                    address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0,
                    _ => 30,
                    address => address == FieldPositionReader.AddressFieldNumModels ? (byte)2 :
                        address == FieldNavigationObjectReader.AddressFieldModelIdArray + 5 ? (byte)1 :
                        address == events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset ? (byte)1 : (byte)0,
                    (_, _) => { stockHeadingReads++; return ["Cloud", "A stock line belonging to another entity."]; },
                    _ => [definition], useVerifiedFallback: _ => false);
                var targets = reader.ReadTargets(new FieldPositionSnapshot(1,449,0,0,0,0,0,0));
                Equal(0, stockHeadingReads, "changed native dialog IDs cannot consult the installed stock table");
                Equal(false, targets.Any(t => t.Label == "Cloud"), "an unrelated stock heading cannot name the current Aerith entity");
                Equal(true, targets.Any(t => t.Label == "Aerith"), "the visible talk-enabled Aerith remains selectable");
            });
        }
        if (failures.Count > 0) throw new InvalidOperationException("Whole-game Echo-S failures:\n" + string.Join('\n', failures));
    }

    private static void Matrix(string stockRoot, string echoRoot)
    {
        var source = new FlevelDataSource(echoRoot);
        var native = new FieldScriptNavigationCatalog(echoRoot);
        var state = new ScriptMemory();
        var loaded = new FieldScriptNavigationCatalog(stockRoot, Ff7GameLanguageDetector.Detect(stockRoot), new LoadedFieldScriptIdentityReader(state));
        var identity = new LoadedFieldScriptIdentityReader(state);
        var count = 0;
        foreach (var field in source.FieldNames.Keys.Order())
        {
            if (!source.TryReadField(field, out _)) continue;
            state.Load(echoRoot, field);
            Equal(true, identity.TryRead(out var fingerprint), "coherent native prefix");
            Equal(SupportedFieldScriptVariant.EchoS124, EchoSCompatibilityManifest.ResolveVariant(fingerprint), "every current Echo-S field has an exact reviewed identity");
            var current = loaded.ReadField(field);
            var expected = native.ReadField(field);
            Equal(expected.IsUsable, current.IsUsable, "loaded native parse status");
            Equal(string.Join('|', expected.Exits.Select(e => e.StableId)), string.Join('|', current.Exits.Select(e => e.StableId)), "all native LINE exits keep current identities");
            Equal(string.Join('|', expected.Npcs.Select(n => $"{n.EntityId}:{n.EntityName}:{n.ModelResourceName}:{n.InteractionLineEntityId}")),
                string.Join('|', current.Npcs.Select(n => $"{n.EntityId}:{n.EntityName}:{n.ModelResourceName}:{n.InteractionLineEntityId}")), "native NPC models and counter bindings");
            Equal(expected.Transitions.Count, current.Transitions.Count, "native traversals remain represented");
            string GuardSignature(FieldScriptNavigationReadResult result) => System.Text.Json.JsonSerializer.Serialize(result.ExitGuards.OrderBy(p => p.Key));
            if (field is 139 or 279)
            {
                var id = field == 139 ? "script-exit:139:26:140" : "script-exit:279:5:332";
                Equal(true, current.ExitGuards.ContainsKey(id), "reviewed still-existing native IF retained beyond generic scan");
                Equal(field == 139 ? "1:0:sw:4:108:True,5:32:b:0:1:False,5:255:b:0:0:True" : "1:0:sw:3:384:True",
                    string.Join(',', current.ExitGuards[id].Single().Alternatives.Single().Select(t => t.Key)), "reviewed exact current guard operands");
                current = current with { ExitGuards = current.ExitGuards.Where(p => p.Key != id).ToDictionary(p => p.Key, p => p.Value) };
            }
            Equal(GuardSignature(expected), GuardSignature(current), "every other native destination guard and bank test comes from current scripts");
            count++;
        }
        Equal(702, count, "complete playable native fields, excluding four non-field archive entries");
    }

    private static void Narration(string echoRoot)
    {
        var descriptions = EchoSCompatibilityManifest.CreateEchoS124Descriptions();
        var original = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        var invalidDuplicate = original.Any(c => c.Key == new FieldCutsceneDescriptionKey(322,0,0,444)) ? 1 : 0;
        Equal(original.Count - invalidDuplicate - 2, descriptions.Count,
            "invalid322 and deleted708/291 anchors excluded; both actual121 MPNAM branches retained");
        var native = new FieldScriptNavigationCatalog(echoRoot);
        var state = new ScriptMemory();
        var identityReader = new LoadedFieldScriptIdentityReader(state);
        var canonical = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        foreach (var byField in descriptions.GroupBy(c => c.FieldId))
        {
            state.Load(echoRoot, byField.Key);
            Equal(true, identityReader.TryRead(out var identity), "loaded narration identity");
            var scripts = native.ReadAllScriptOpcodes(byField.Key);
            var tracker = new EchoSFieldCutsceneDescriptionTracker();
            foreach (var cue in byField)
            {
                Equal(true, scripts.Any(s => s.EntityId == cue.EntityId && s.ScriptId == cue.ScriptId &&
                    s.Opcodes.Any(o => o.ByteIndex == cue.ByteIndex && o.Opcode == cue.Opcode)), "retained cue is on the current native instruction");
                Equal(true, canonical.Any(c => c.FieldId == cue.FieldId && c.Text == cue.Text &&
                    c.RecurringGroup == cue.RecurringGroup && c.StartsRecurringGroup == cue.StartsRecurringGroup), "recording words and recurring episode ownership survive remapping");
                var context = new FieldScriptContext(cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode);
                Equal(cue, tracker.Observe(context, identity)!.Value, "actual current anchor delivers the expected cue");
                Equal(true, tracker.Observe(context, identity) is null, "repeated native delivery stays silent");
                Equal(true, tracker.Observe(context with { Opcode = cue.Opcode ^ 255 }, identity) is null, "wrong native opcode stays silent");
                Equal(true, tracker.Observe(context, identity with { ScriptPrefixSha256 = new string('0', 64) }) is null, "unknown loaded prefix stays silent");
            }
        }
    }

    private static void AssertVisibleNamedNpc(FieldScriptNpcDefinition definition, string label)
    {
        const int events = 0x03000000;
        byte visible = 1, talkDisabled = 0, model = 1;
        var stockReads = 0;
        var reader = new FieldNavigationNpcReader(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0,
            _ => 30,
            address => address == FieldPositionReader.AddressFieldNumModels ? (byte)2 :
                address == FieldNavigationObjectReader.AddressFieldModelIdArray + definition.EntityId ? model :
                address == events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset ? visible :
                address == events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationNpcReader.TalkDisabledOffset ? talkDisabled :
                address == FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 129 ? (byte)0xFF : (byte)0,
            (_, _) => { stockReads++; return ["Wrong stock title", "A mismatched old message."]; },
            _ => [definition], isLineEnabled: _ => true, useVerifiedFallback: _ => false);
        var position = new FieldPositionSnapshot(1,definition.FieldId,0,0,0,0,0,0);
        Equal(true, reader.ReadTargets(position).Any(t => t.Label == label), $"visible native NPC {definition.FieldId}/{definition.EntityId} remains {label}");
        Equal(0, stockReads, "current reviewed party identity requires no stale stock dialogue");
        visible = 0;
        Equal(0, reader.ReadTargets(position).Count, "hidden party actor is not announced");
        visible = 1; model = 0;
        Equal(0, reader.ReadTargets(position).Count, "actual controlled actor is not an NPC target");
        model = 1;
        if (!definition.ContactOnly && definition.InteractionLine is null)
        {
            talkDisabled = 1;
            Equal(0, reader.ReadTargets(position).Count, "native Talk-disabled party actor is not a conversation target");
        }
    }

    private static void Equal<T>(T expected, T actual, string why)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{why}: expected {expected}; actual {actual}");
    }

    private sealed class ScriptMemory : ILegacyAddressSpace
    {
        internal const uint Script = 0x02000000;
        internal readonly Dictionary<uint, byte> Bytes = new();
        internal uint? TearAddress;
        private bool torn;
        internal void Load(string root, int field, Ff7GameLanguageContext? language = null)
        {
            var source = language is null ? new FlevelDataSource(root) : new FlevelDataSource(root, language);
            Equal(true, source.TryReadField(field, out var encoded), "native input exists");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
            var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
            Write(Script, decoded.AsSpan(at + 4, length));
            Bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 1;
            Word((uint)FieldScriptContextReader.AddressCurrentFieldId, (ushort)field);
            Span<byte> pointer = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(pointer, Script);
            Write((uint)FieldScriptContextReader.AddressFieldScriptPtr, pointer); torn = false; TearAddress = null;
        }
        internal void Word(uint address, ushort value)
        { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, value); Write(address, bytes); }
        private void Write(uint address, ReadOnlySpan<byte> data)
        { for (var i = 0; i < data.Length; i++) Bytes[address + (uint)i] = data[i]; }
        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                var at = address + (uint)i;
                if (!Bytes.TryGetValue(at, out destination[i])) return false;
                if (!torn && TearAddress == at) { Bytes[at]++; torn = true; }
            }
            return true;
        }
    }
}
