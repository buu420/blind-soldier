using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>Real stock/Echo-S scripts, live entity indices, and the native counter.</summary>
internal static class Sector7NavigationTests
{
    public static void Run(Func<int, FieldWalkmeshReader>? createMesh = null)
    {
        var failures = new List<string>();
        void Case(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS Sector 7 " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.GetBaseException().Message); }
        }
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.WriteLine("Sector 7 installed-data checks require FF7_ACCESSIBILITY_DATA_ROOT.");
            return;
        }
        Case("stock shop counter", () => Counter(root, createMesh));
        Case("stock bar and hideout", () => Doors(root));
        Case("native lift approach", () => LiftApproach(root, createMesh));
        Case("loaded layout and re-entry", () => Loaded(root, root));
        Case("future field metadata", () => Metadata(root));
        foreach (var echo in (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Case(Path.GetFileName(echo) + " counter", () => Counter(echo, createMesh));
            Case(Path.GetFileName(echo) + " doors", () => Doors(echo));
            Case(Path.GetFileName(echo) + " loaded layout", () => Loaded(root, echo));
            Case(Path.GetFileName(echo) + " NPC identity", () => EchoNpc(echo));
            Case(Path.GetFileName(echo) + " Object identity", () => EchoObjects(root, echo));
            Case(Path.GetFileName(echo) + " Story identity", () => EchoStory(root, echo));
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("Sector 7 navigation failures:\n" + string.Join('\n', failures));
        Console.WriteLine("Sector 7 stock, loaded-script, counter and Echo-S checks passed.");
    }

    private static void Counter(string root, Func<int, FieldWalkmeshReader>? createMesh)
    {
        var catalog = new FieldScriptNavigationCatalog(root);
        var state = new State();
        state.Show(4, -91, 175);
        state.TalkDisabled = true; // The counter remains the actual manual interaction.
        var reader = state.Npcs(catalog);
        var position = new FieldPositionSnapshot(1, 152, 0, 169, 6, 0, 21, 0);
        var targets = reader.ReadTargets(position);
        Equal(1, targets.Count, "visible shopkeeper remains selectable across the counter");
        var target = targets.Single();
        Equal("Item and Materia shopkeeper", target.Label, "the native SHOP 3 sells both types");
        Equal(2, target.TriggerEntityId, "the target uses border3, not the blocked model");
        Equal(new FieldNavigationTriggerLine(70, 19, 0, -65, -11, 0), target.TriggerLine!.Value, "native counter segment");
        Equal(30, target.LineActivationRadius, "strict touch distance uses the leader's radius");
        state.LineEnabled = false;
        Equal(0, reader.ReadTargets(position).Count, "a disabled counter is absent");
        state.LineEnabled = true;
        state.Visible = false;
        Equal(0, reader.ReadTargets(position).Count, "a hidden shopkeeper is absent");
        state.Visible = true;
        if (createMesh is not null)
        {
            var planner = new FieldWalkmeshRoutePlanner(createMesh(152));
            Equal(true, planner.TryBuildRoute(position, target, out var route), "native arrival reaches the counter");
            Equal(true, route.TrianglePath.Count > 0, "counter route has an approach");
        }
    }

    private static void Doors(string root)
    {
        var catalog = new FieldScriptNavigationCatalog(root);
        var labels = new FieldExitLabelResolver(_ => new FieldMapNameResolution(true, ["Sector 7"]), () => "Sector 7");
        foreach (var (field, destination, expected, line) in new[]
                 {
                     (151, 154, "Enter Seventh Heaven", new FieldNavigationTriggerLine(290,379,76,326,364,76)),
                     (154, 155, "Use pinball lift to AVALANCHE hideout", new FieldNavigationTriggerLine(-52,187,0,-8,173,0)),
                     (155, 154, "Use pinball lift to Seventh Heaven", new FieldNavigationTriggerLine(155,-14,0,129,-65,0))
                 })
        {
            var result = catalog.ReadField(field);
            var target = result.Exits.Single(t => t.TriggerLine == line && t.DestinationFieldIds!.Contains(destination));
            Equal(expected, labels.Resolve([target]).Single().Label, "the doorway names its actual room");
            Equal(destination.ToString(), string.Join(',', target.DestinationFieldIds!), "no unrelated scene destinations");
        }
        var state = new State();
        var bar = catalog.ReadField(154);
        state.WriteWord((uint)FieldNavigationObjectReader.AddressFieldBankBase, 71);
        Equal(0, FieldScriptExitGuards.Apply(bar.Exits, bar.ExitGuards, state).Count, "hideout is unavailable before the native opening");
        state.WriteWord((uint)FieldNavigationObjectReader.AddressFieldBankBase, 72);
        Equal(1, FieldScriptExitGuards.Apply(bar.Exits, bar.ExitGuards, state).Count, "opened hideout remains an ordinary exit");
        foreach (var moment in new ushort[] { 95, 96, 97, 108, 116 })
        {
            state.WriteWord((uint)FieldNavigationObjectReader.AddressFieldBankBase, moment);
            var expected = !bar.UsesEchoSector7Layout && moment == 96 ? 0 : 1;
            Equal(expected, FieldScriptExitGuards.Apply(bar.Exits, bar.ExitGuards, state).Count,
                $"the native descent branch at moment {moment}");
        }
        state.Bytes[(uint)FieldScriptExitGuards.AddressTemporaryFieldBank + 26] = 1;
        Equal(0, FieldScriptExitGuards.Apply(bar.Exits, bar.ExitGuards, state).Count, "lift in motion is not offered");
        var hideout = catalog.ReadField(155);
        state.Bytes[(uint)FieldScriptExitGuards.AddressTemporaryFieldBank + 21] = 1;
        Equal(0, FieldScriptExitGuards.Apply(hideout.Exits, hideout.ExitGuards, state).Count, "return lift respects its native busy flag");
    }

    private static void LiftApproach(string root, Func<int, FieldWalkmeshReader>? createMesh)
    {
        var state = new State();
        state.Load(root, 154);
        state.WriteWord((uint)FieldNavigationObjectReader.AddressFieldBankBase, 72);
        state.WriteWord(State.Events + FieldNavigationNpcReader.CollisionRadiusOffset, 34);
        state.WriteDword((uint)FieldNavigationObjectReader.AddressFieldEventDataPtr, State.Events);
        state.Bytes[(uint)FieldPositionReader.AddressFieldNumModels] = 2;
        var position = new FieldPositionSnapshot(1,154,0,12,13,0,26,0);
        var exit = new FieldScriptNavigationCatalog(root).ReadField(154).Exits.Single();
        var target = Sector7NavigationLayout.ApplyLiftApproaches(position, [exit], state).Single();
        Equal(34, target.LineActivationRadius, "the lift uses the leader's current radius, not a guessed arrival tolerance");
        if (createMesh is not null)
        {
            var planner = new FieldWalkmeshRoutePlanner(createMesh(154));
            Equal(true, planner.TryBuildRoute(position, target, out var route), "bar arrival routes to the native lift line");
            Equal(true, FieldNativeLineContact.Touches(target.TriggerLine!.Value,
                position with { X = route.FinalApproach.X, Y = route.FinalApproach.Y, Z = route.FinalApproach.Z }, target.LineActivationRadius),
                "the final approach satisfies the game's strict touch test");
        }
        foreach (var moment in new ushort[] { 72, 95, 96, 97, 108, 116 })
        {
            state.WriteWord((uint)FieldNavigationObjectReader.AddressFieldBankBase, moment);
            Equal(moment == 96 ? 0 : 1, Sector7NavigationLayout.ApplyLiftApproaches(position, [exit], state).Count,
                $"native stock lift approach at moment {moment}");
        }
        state.Bytes.Remove((uint)FieldScriptExitGuards.AddressTemporaryFieldBank + 26);
        Equal(0, Sector7NavigationLayout.ApplyLiftApproaches(position, [exit], state).Count, "an unreadable lift flag cannot publish a usable lift");
    }

    private static void Loaded(string installedRoot, string loadedRoot)
    {
        var state = new State();
        var identity = new LoadedFieldScriptIdentityReader(state);
        var catalog = new FieldScriptNavigationCatalog(installedRoot, Ff7GameLanguageDetector.Detect(installedRoot), identity);
        foreach (var field in new[] { 151, 154, 155, 152, 151 })
        {
            state.Load(loadedRoot, field);
            var actual = catalog.ReadField(field);
            var expected = new FieldScriptNavigationCatalog(loadedRoot).ReadField(field);
            Equal(true, actual.IsUsable, "loaded field remains usable");
            Equal(string.Join('|', expected.Exits.Select(e => e.StableId)), string.Join('|', actual.Exits.Select(e => e.StableId)),
                "the loaded triggers override disk and survive returning to a field");
        }
        state.Bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 0;
        Equal(false, catalog.ReadField(151).IsUsable, "a field unload releases the cached layout");
        state.Load(loadedRoot, 154);
        Equal(false, catalog.ReadField(151).IsUsable, "another field's script cannot authorize the bar door");
        state.Load(loadedRoot, 151);
        state.TearAddress = State.Script + 40;
        Equal(false, catalog.ReadField(151).IsUsable, "a torn prefix cannot reuse the cached layout");
        state.TearAddress = null;
        state.Load(loadedRoot, 151);
        state.Bytes[State.Script + 40] ^= 1;
        Equal(false, catalog.ReadField(151).IsUsable, "an unreviewed mutation cannot reuse the old identity");
        state.Load(loadedRoot, 151);
        Equal(true, catalog.ReadField(151).IsUsable, "coherent re-entry restores navigation");
    }

    private static void EchoNpc(string root)
    {
        var state = new State();
        var catalog = new FieldScriptNavigationCatalog(root);
        foreach (var (field, entity, label) in new[] { (151,12,"Barret"), (151,18,"Man"), (151,19,"Woman"), (155,10,"Jessie") })
        {
            state.Show(entity, 330, 300);
            var target = state.Npcs(catalog).ReadTargets(new FieldPositionSnapshot(1,field,0,0,0,0,0,0)).Single();
            Equal(label, target.Label, "a current Echo-S NPC is not excluded by a stock object id");
            Equal($"npc:{field}:{entity}", target.StableId, "the current model identity is retained");
        }
    }

    private static void Metadata(string root)
    {
        var state = new State();
        state.Load(root, 154);
        var catalog = new FieldScriptNavigationCatalog(root, Ff7GameLanguageDetector.Detect(root), new LoadedFieldScriptIdentityReader(state));
        var names = new FieldMapNameCatalog(catalog, new FlevelFieldTextResolver(root));
        Equal(true, names.Read(151).IsKnownField, "map metadata is available before entering the destination");
        Equal(true, catalog.ReadDiskField(155).IsUsable, "future cross-field routing retains installed metadata");
        Equal(false, catalog.ReadField(151).IsUsable, "metadata cannot authorize a target in an unloaded field");
    }

    private static void EchoObjects(string installedRoot, string echoRoot)
    {
        var state = new State();
        var catalog = new FieldScriptNavigationCatalog(installedRoot, Ff7GameLanguageDetector.Detect(installedRoot), new LoadedFieldScriptIdentityReader(state));
        foreach (var (field, originalId, actualId) in new[] { (151,18,9), (151,19,10), (155,10,4) })
        {
            state.Load(echoRoot, field);
            var definition = FieldNavigationObjectCatalog.CreateAllFields().Single(d => d.FieldId == field && d.EntityId == originalId);
            Equal(actualId, catalog.RemapObjectDefinition(definition)!.Value.EntityId, "object identity follows the loaded layout");
            var enabled = actualId;
            var reader = new FieldNavigationObjectReader(state.Int32, state.Byte, _ => null, _ => null, [definition],
                isLineEnabled: entity => entity == enabled, remapDefinition: catalog.RemapObjectDefinition);
            var position = new FieldPositionSnapshot(1,field,0,0,0,0,0,0);
            var target = reader.ReadTargets(position).Single();
            Equal(true, target.StableId.StartsWith($"object:{field}:{actualId}:"), "the object's current id is used for native line state");
            enabled = originalId;
            Equal(0, reader.ReadTargets(position).Count, "an unrelated stock-id line cannot expose the object");
            state.Bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 0;
            Equal(0, reader.ReadTargets(position).Count, "an unloaded identity cannot expose the object");
        }
    }

    private static void EchoStory(string installedRoot, string echoRoot)
    {
        var state = new State();
        state.Load(echoRoot, 154);
        state.Show(8, 100, 90);
        var catalog = new FieldScriptNavigationCatalog(installedRoot, Ff7GameLanguageDetector.Detect(installedRoot), new LoadedFieldScriptIdentityReader(state));
        var story = new FieldStoryTargetReader(state.Int32, state.Int16, state.Byte,
            [new FieldStoryEventDefinition(154, FieldStoryTargetKind.Model, "Talk to Barret", 19)],
            _ => true, remapDefinition: catalog.RemapStoryDefinition);
        var position = new FieldPositionSnapshot(1,154,0,0,0,0,0,0);
        var target = story.ReadTargets(position).Single();
        Equal(100, target.X, "Story resolves Echo-S Barret's current model");
        state.Visible = false;
        Equal(0, story.ReadTargets(position).Count, "Story cannot offer a hidden actor");
        state.Visible = true;
        state.Bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 0;
        Equal(0, story.ReadTargets(position).Count, "Story cannot use a stale loaded identity");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private sealed class State : ILegacyAddressSpace
    {
        internal const uint Script = 0x02000000;
        internal const uint Events = 0x03000000;
        internal readonly Dictionary<uint, byte> Bytes = new();
        internal bool Visible = true, TalkDisabled, LineEnabled = true;
        internal uint? TearAddress;
        private bool torn;
        private int entity, x, y;

        internal State()
        {
            for (uint i = 0; i < 0x500; i++) Bytes[(uint)FieldScriptExitGuards.AddressSavemapFieldBanks + i] = 0;
            for (uint i = 0; i < 0x100; i++) Bytes[(uint)FieldScriptExitGuards.AddressTemporaryFieldBank + i] = 0;
        }
        internal void Show(int id, int px, int py) { entity = id; x = px; y = py; }
        internal int Int32(int address) => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? (int)Events :
                address == Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.PositionXOffset ? x * 4096 :
                address == Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.PositionYOffset ? y * 4096 : 0;
        internal short Int16(int address) => address == Events + FieldNavigationNpcReader.CollisionRadiusOffset ? (short)30 : (short)0;
        internal byte Byte(int address) => address == FieldPositionReader.AddressFieldNumModels ? (byte)2 :
                address == Events + FieldNavigationNpcReader.CollisionRadiusOffset ? (byte)30 :
                address == Events + FieldNavigationNpcReader.CollisionRadiusOffset + 1 ? (byte)0 :
                address == FieldNavigationObjectReader.AddressFieldModelIdArray + entity ? (byte)1 :
                address == Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset ? (byte)(Visible ? 1 : 0) :
                address == Events + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationNpcReader.TalkDisabledOffset ? (byte)(TalkDisabled ? 1 : 0) :
                Bytes.GetValueOrDefault((uint)address, (byte)255);
        internal FieldNavigationNpcReader Npcs(FieldScriptNavigationCatalog catalog) => new(
            Int32, Int16, Byte,
            (_, _) => [], field => catalog.ReadField(field).Npcs,
            FieldNavigationObjectCatalog.CreateAllFields().Select(d => (d.FieldId, d.EntityId)), isLineEnabled: _ => LineEnabled);
        internal void Load(string root, int field)
        {
            var source = new FlevelDataSource(root);
            Equal(true, source.TryReadField(field, out var encoded), "installed script exists");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
            var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
            Write(Script, decoded.AsSpan(at + 4, length));
            Bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 1;
            WriteWord((uint)FieldScriptContextReader.AddressCurrentFieldId, (ushort)field);
            Span<byte> pointer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(pointer, Script);
            Write((uint)FieldScriptContextReader.AddressFieldScriptPtr, pointer);
            torn = false;
        }
        internal void WriteWord(uint address, ushort value)
        {
            Span<byte> data = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(data, value); Write(address, data);
        }
        internal void WriteDword(uint address, uint value)
        {
            Span<byte> data = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(data, value); Write(address, data);
        }
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
