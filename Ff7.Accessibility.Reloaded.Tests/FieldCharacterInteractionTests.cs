using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FieldCharacterInteractionTests
{
    public static void Run()
    {
        var hiddenTalkRows = FieldStoryEventCatalog.CreateAllFields().Where(d => d.UsesHiddenTalkTarget).ToArray();
        Equal(2, hiddenTalkRows.Length, "only two reviewed visible-pose/scenery interactions use hidden actors");
        Equal(true, hiddenTalkRows.All(d => d.Kind == FieldStoryTargetKind.Model &&
            (d.FieldId == 579 && d.EntityId == 16 || d.FieldId == 323 && d.EntityId == 7 &&
                d.MinimumGameMoment == 382 && d.MaximumGameMoment == 382)),
            "hidden Talk rows are limited to the shaking pot and wounded Tifa");
        // Echo-S dialogue indices cannot name this character from the stock text table.
        var priscilla = new FieldScriptNpcDefinition(429, 12, "prisl", [22],
            ModelResourceName: "sub_pricilla.char")
        {
            ReviewedStockEntityId = 12,
            SuppressInstalledDialogueLabels = true
        };
        var beach = new NativeMemory();
        beach.Moment(394);
        beach.Actor(12, 1, -592, 474, -17, visible: true);
        var position = new FieldPositionSnapshot(1, 429, 0, -400, 693, -32, 31, 0);
        var npcs = beach.Npcs([priscilla]);
        Equal("Priscilla", npcs.ReadTargets(position).SingleOrDefault().Label,
            "Echo-S Priscilla must have a verified label without a stock-dialogue guess");
        beach.Visible(1, false);
        Equal(0, npcs.ReadTargets(position).Count, "an absent Priscilla stays absent");
        beach.Visible(1, true);
        beach.TalkDisabled(1, true);
        Equal(0, npcs.ReadTargets(position).Count, "Priscilla is unavailable when native Talk is disabled");

        // The injured pose is entity 6; the game deliberately leaves entity 7 invisible
        // with TLKON 0, TALKR 40 at (39,-256,186) to receive the player's Confirm.
        var reactor = new NativeMemory();
        reactor.Moment(382);
        reactor.Actor(7, 1, 39, -256, 186, visible: false);
        reactor.Actor(6, 2, 15, -272, 186, visible: true);
        reactor.Word(NativeMemory.Event(1) + FieldNavigationNpcReader.TalkRadiusOffset, 40);
        var tifa = new FieldScriptNpcDefinition(323, 7, "ti2", [127]);
        var chamber = new FieldPositionSnapshot(1, 323, 0, -309, -242, 186, 6, 0);
        var tifaNpcs = reactor.Npcs([tifa]);
        Equal("Tifa", tifaNpcs.ReadTargets(chamber).SingleOrDefault().Label,
            "the wounded pose's native Talk proxy remains in NPCs");
        var story = reactor.Story();
        var target = story.ReadTargets(chamber).SingleOrDefault();
        Equal("Talk to Tifa beside the reactor pods", target.Label,
            "Story must reach the actual injured-pose interaction target");
        Equal((39, -256, 186), (target.X, target.Y, target.Z), "use the native proxy position");
        Equal(79, target.InteractionRadius, "native player width plus TALKR determines reach");
        Equal(FieldNavigationActivation.Talk, target.Activation, "interaction stays on the player's Confirm button");
        reactor.TalkDisabled(1, true);
        Equal(0, tifaNpcs.ReadTargets(chamber).Count, "disabled native proxy is unavailable");
        Equal(0, story.ReadTargets(chamber).Count, "Story also rejects a disabled hidden proxy");
        reactor.TalkDisabled(1, false);
        reactor.Moment(383);
        Equal(0, tifaNpcs.ReadTargets(chamber).Count, "the hidden proxy cannot duplicate Tifa after she is moved");
        Equal("Follow Sephiroth into Jenova's chamber", story.ReadTargets(chamber).Single().Label,
            "the next native Story step still follows Jenova's chamber");
        reactor.Moment(382);
        reactor.Byte(FieldNavigationObjectReader.AddressFieldModelIdArray + 7, 0xFF);
        Equal(0, tifaNpcs.ReadTargets(chamber).Count, "missing model mappings do not create a target");
        Equal(0, story.ReadTargets(chamber).Count, "Story rejects a missing model mapping");

        var unrelated = new NativeMemory();
        unrelated.Moment(382);
        unrelated.Actor(8, 1, 39, -256, 186, visible: false);
        Equal(0, unrelated.Npcs([new(323, 8, "cef", [1])]).ReadTargets(chamber).Count,
            "ordinary hidden models remain excluded");
        Console.WriteLine("Priscilla and wounded Tifa interaction regressions passed.");
    }

    public static void RunWithInstalledGameData(Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner>? wrap = null)
    {
        var stock = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(stock))
        {
            Console.WriteLine("Character interaction native archives not tested: FF7_ACCESSIBILITY_DATA_ROOT unset.");
            return;
        }
        var roots = new[] { stock }.Concat((Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var root in roots)
        {
            foreach (var field in new[] { 323, 429 })
            {
                var state = new NativeMemory();
                state.Load(root, field);
                var native = new FieldScriptNavigationCatalog(root);
                if (field == 323)
                {
                    var init = native.ReadScriptOpcodes(323, 7, 0);
                    Equal(true, init.Any(o => Convert.ToHexString(o.Bytes.ToArray()) == "A400"), "native ti2 is hidden");
                    Equal(true, init.Any(o => Convert.ToHexString(o.Bytes.ToArray()) == "7E00"), "native ti2 enables Talk");
                    var enable = native.ReadScriptOpcodes(323, 7, 5);
                    Equal(true, enable.Any(o => Convert.ToHexString(o.Bytes.ToArray()) == "7E00"), "post-cutscene proxy enables Talk");
                    Equal(false, enable.Any(o => Convert.ToHexString(o.Bytes.ToArray()) == "A401"), "post-cutscene proxy stays hidden");
                }
                var loaded = new FieldScriptNavigationCatalog(stock, Ff7GameLanguageDetector.Detect(stock),
                    new LoadedFieldScriptIdentityReader(state));
                var current = loaded.ReadField(field);
                Equal(true, current.IsUsable, $"reviewed loaded native prefix: {root}/{field}: {current.Diagnostic}");
                var canonical = field == 323 ? 7 : 12;
                var definition = current.Npcs.Single(n => (n.ReviewedStockEntityId ?? n.EntityId) == canonical);
                var x = field == 323 ? 39 : -592;
                var y = field == 323 ? -256 : 474;
                var z = field == 323 ? 186 : -17;
                state.Moment(field == 323 ? 382 : 394);
                state.Actor(definition.EntityId, 1, x, y, z, visible: field != 323);
                state.Word(NativeMemory.Event(1) + FieldNavigationNpcReader.TalkRadiusOffset, field == 323 ? 40 : 30);
                var position = field == 323 ? new FieldPositionSnapshot(1, 323, 0, -309, -242, 186, 6, 0)
                    : new FieldPositionSnapshot(1, 429, 0, -400, 693, -32, 31, 0);
                var reader = state.Npcs(current.Npcs);
                var npc = reader.ReadTargets(position).Single();
                Equal(field == 323 ? "Tifa" : "Priscilla", npc.Label, "loaded native target identity");
                var story = new FieldStoryTargetReader(state.ReadInt, state.ReadShort, state.ReadByte,
                    FieldStoryEventCatalog.CreateAllFields(), remapDefinition: loaded.RemapStoryDefinition,
                    nativeGuardReader: loaded.ReadNativeGuard);
                var target = story.ReadTargets(position).Single();
                Equal(field == 323 ? "Talk to Tifa beside the reactor pods" : "Talk to Priscilla at the beach",
                    target.Label, "loaded native Story target");
                Equal((x, y, z), (target.X, target.Y, target.Z), "Story uses current native model coordinates");
                IFieldNavigationRoutePlanner planner = new FieldWalkmeshRoutePlanner(state.Walkmesh());
                if (wrap is not null) planner = wrap(planner);
                Equal(true, planner.TryBuildRoute(position, npc, out _), $"native NPC route: {planner.LastDiagnostic}");
                Equal(true, planner.TryBuildRoute(position, target, out _), $"native Story route: {planner.LastDiagnostic}");
                state.TalkDisabled(1, true);
                Equal(0, reader.ReadTargets(position).Count, "native unavailable conversation disappears");
                if (field == 323) Equal(0, story.ReadTargets(position).Count, "native hidden proxy disappears when disabled");
                else
                {
                    state.TalkDisabled(1, false);
                    state.Visible(1, false);
                    Equal(0, reader.ReadTargets(position).Count, "native absent Priscilla is not revealed");
                    Equal(0, story.ReadTargets(position).Count, "native absent Priscilla is not a Story target");
                }
            }
            Console.WriteLine($"Character interaction loaded-prefix and native routes passed: {Path.GetFileName(root)}.");
        }
    }

    private sealed class NativeMemory : ILegacyAddressSpace
    {
        private const int Events = 0x03000000;
        internal readonly Dictionary<int, byte> Bytes = [];
        private byte[] fieldBytes = [];

        internal FieldWalkmeshReader Walkmesh()
        {
            const int data = 0x04000000;
            return new FieldWalkmeshReader(
                address => address == FieldWalkmeshReader.AddressFieldDataPtr ? data :
                    address >= data && address - data + 4 <= fieldBytes.Length ?
                        BinaryPrimitives.ReadInt32LittleEndian(fieldBytes.AsSpan(address - data)) : 0,
                address => address >= data && address - data + 2 <= fieldBytes.Length ?
                    BinaryPrimitives.ReadInt16LittleEndian(fieldBytes.AsSpan(address - data)) : (short)0);
        }

        internal void Load(string root, int field)
        {
            var source = new FlevelDataSource(root);
            Equal(true, source.TryReadField(field, out var encoded), "native input exists");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            fieldBytes = decoded;
            var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
            var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
            const int script = 0x02000000;
            Write(script, decoded.AsSpan(at + 4, length));
            Byte(FieldScriptContextReader.AddressCurrentModule, 1);
            Word(FieldScriptContextReader.AddressCurrentFieldId, field);
            Int(FieldScriptContextReader.AddressFieldScriptPtr, script);
        }

        internal NativeMemory()
        {
            Int(FieldNavigationObjectReader.AddressFieldEventDataPtr, Events);
            Byte(FieldPositionReader.AddressFieldNumModels, 3);
            for (var id = 0; id < 256; id++) Byte(FieldNavigationObjectReader.AddressFieldModelIdArray + id, 0xFF);
            Word(Event(0) + FieldNavigationNpcReader.CollisionRadiusOffset, 39);
        }

        internal static int Event(int model) => Events + model * FieldNavigationObjectReader.FieldEventDataStride;
        internal void Moment(int value) => Word(FieldNavigationObjectReader.AddressFieldBankBase, value);
        internal void Byte(int at, byte value) => Bytes[at] = value;
        internal byte ReadByte(int at) => Bytes.GetValueOrDefault(at);
        internal int ReadInt(int at) => BinaryPrimitives.ReadInt32LittleEndian(Read(at, 4));
        internal short ReadShort(int at) => BinaryPrimitives.ReadInt16LittleEndian(Read(at, 2));
        private byte[] Read(int at, int count) => Enumerable.Range(at, count).Select(ReadByte).ToArray();
        internal void Int(int at, int value)
        {
            Span<byte> data = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(data, value);
            Write(at, data);
        }
        internal void Word(int at, int value)
        {
            Span<byte> data = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)value);
            Write(at, data);
        }
        private void Write(int at, ReadOnlySpan<byte> data)
        {
            for (var i = 0; i < data.Length; i++) Byte(at + i, data[i]);
        }
        internal void Actor(int entity, int model, int x, int y, int z, bool visible)
        {
            Byte(FieldNavigationObjectReader.AddressFieldModelIdArray + entity, (byte)model);
            Int(Event(model) + FieldNavigationObjectReader.PositionXOffset, x * 4096);
            Int(Event(model) + FieldNavigationObjectReader.PositionYOffset, y * 4096);
            Int(Event(model) + FieldNavigationObjectReader.PositionZOffset, z * 4096);
            Visible(model, visible);
        }
        internal void Visible(int model, bool value) => Byte(Event(model) + FieldNavigationObjectReader.VisibilityOffset, value ? (byte)1 : (byte)0);
        internal void TalkDisabled(int model, bool value) => Byte(Event(model) + FieldNavigationNpcReader.TalkDisabledOffset, value ? (byte)1 : (byte)0);
        internal FieldNavigationNpcReader Npcs(IReadOnlyList<FieldScriptNpcDefinition> definitions) =>
            new(ReadInt, ReadShort, ReadByte, (_, _) => throw new InvalidOperationException("Stock dialogue must not name an Echo-S NPC"),
                _ => definitions, useVerifiedFallback: _ => false);
        internal FieldStoryTargetReader Story() => new(ReadInt, ReadShort, ReadByte, FieldStoryEventCatalog.CreateAllFields());

        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
                if (!Bytes.TryGetValue(unchecked((int)address + i), out destination[i])) return false;
            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string reason)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Character interaction: {reason}: expected {expected}, got {actual}");
    }
}
