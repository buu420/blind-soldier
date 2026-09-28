using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Great Glacier's optional content, each checked against the installed scripts and
/// walked to from every way the game brings the party into its screen.
///
/// <para>The hot spring is hyou10 (field 680), not hyou8_1: line80..83 (entities 20..23) run
/// event script 3 from their Move slot, once per visit, and that asks dialog 54. Choosing
/// "Touch it" runs Cloud's script 4, which shows dialog 55 and sets bank 1 byte 199 bit 0 -
/// the flag hyou13_2's Snow tests before she will fight. The ice-floe crossing starts where
/// line50a (south shore, entity 21) and line50b (north shore, entity 22) run Cloud's script
/// 3 from their GoOnce slot. The seven treasures are models their own Init hides once taken,
/// and the object reader already goes by that live visibility.</para>
/// </summary>
internal static class GreatGlacierOptionalContentTests
{
    private const int HotSpringField = 680;
    private const int HotSpringEntity = 20;
    private const int LakeField = GreatGlacierIceFloePuzzle.FieldId;
    private const int SouthShoreStart = 21;
    private const int NorthShoreStart = 22;

    // Field, entity and native gate: bank 1 byte and bit of the flag the Init hides the model on.
    private static readonly (int Field, int Entity, string Name, int FlagAddress, int FlagMask)[] Treasures =
    [
        (659, 17, "Mind Source", 37, 0x08),
        (664, 23, "Potion", 37, 0x02),
        (666, 13, "Safety Bit", 37, 0x04),
        (678, 12, "Elixir", 37, 0x01),
        (675, 20, "Added Cut", 199, 0x20),
        (682, 11, "All", 199, 0x40),
        (684, 14, "Alexander", 199, 0x10)
    ];

    private static (int Field, int X, int Y, int Triangle)[] worldArrivals = [];
    private static readonly List<string> breakdown = [];

    public static void Run()
    {
        TheHotSpringIsOfferedUntilItHasBeenTouched();
        TheCrossingStartsAreOfferedOnTheirOwnShores();
        TheTreasuresGoByTheirLiveModels();
        TheWaysBackToTheSnowfieldAreNamed();
        TheLakeStoryStepPointsAtTheCrossing();
        Console.WriteLine("Great Glacier optional content: hot spring, crossing starts and treasures are live objects.");
    }

    /// <summary>
    /// hyou5_2's Story step is the south exit, and the lake splits the field: from the north
    /// shore the only way to it is across the floes. Its guidance names the object that starts
    /// the crossing and the game's own instruction, rather than "puzzle controls" nobody has
    /// been told about.
    /// </summary>
    private static void TheLakeStoryStepPointsAtTheCrossing()
    {
        var row = FieldStoryEventCatalog.CreateAllFields().Single(r => r.FieldId == LakeField);
        var guidance = row.ManualNavigationGuidance ?? string.Empty;
        foreach (var start in new[] { SouthShoreStart, NorthShoreStart })
        {
            Equal(true, Definition(LakeField, start).Label.StartsWith("Ice floes, start the crossing", StringComparison.Ordinal),
                "the crossing starts keep the name the guidance uses");
        }

        Contains(guidance, "Ice floes, start the crossing from the north shore", "the guidance names the crossing start");
        Contains(guidance, "face a direction and press OK", "and the game's own instruction for jumping");
        Contains(guidance, "south shore", "and where the exit it leads to is");
    }

    /// <summary>
    /// The All cave's way out and the cabin's front gateway both lead onto the snowfield,
    /// world entries 64 and 60. A world entry has no map name, so without these they were
    /// a bare "Exit". The cabin's rooms share one map name, Base of Gaea's Cliff, so each
    /// room is told apart by where it sits: 686 is outside, 687 the room the outside door
    /// opens into, 688 the room behind it.
    /// </summary>
    private static void TheWaysBackToTheSnowfieldAreNamed()
    {
        var cave = new FieldExitLabelResolver(_ => FieldMapNameResolution.Unknown, () => "Cave");
        Equal("Leave the cave for the snowfield", cave.Resolve([Exit(682, "script-exit:682:1:64", 64)]).Single().Label,
            "the All cave's way out");
        var names = new FieldExitLabelResolver(
            field => field is >= 686 and <= 688 ? FieldMapNameResolution.Known(["Base of Gaea's Cliff"]) : FieldMapNameResolution.Unknown,
            () => "Base of Gaea's Cliff");
        Equal("Leave for the snowfield", names.Resolve([Exit(686, "gateway:686:1:60", 60)]).Single().Label,
            "the cabin's front gateway back onto the snowfield");
        Equal("Exit to Base of Gaea's Cliff, cabin front room", names.Resolve([Exit(686, "gateway:686:0:687", 687)]).Single().Label,
            "the outside door opens into the front room");
        Equal("Exit to Base of Gaea's Cliff, outside the cabin", names.Resolve([Exit(687, "gateway:687:0:686", 686)]).Single().Label,
            "the front room's way outside");
        Equal("Exit to Base of Gaea's Cliff, cabin back room", names.Resolve([Exit(687, "gateway:687:1:688", 688)]).Single().Label,
            "the front room's way to the back room");
        Equal("Exit to Base of Gaea's Cliff, cabin front room", names.Resolve([Exit(688, "gateway:688:0:687", 687)]).Single().Label,
            "the back room's way out");
    }

    private static FieldNavigationTarget Exit(int field, string stableId, int destination) =>
        new(field, FieldNavigationCategory.Exits, "Exit", 0, 0, 0, stableId, DestinationFieldIds: [destination]);

    private static void TheHotSpringIsOfferedUntilItHasBeenTouched()
    {
        var spring = Definition(HotSpringField, HotSpringEntity);
        Equal(FieldNavigationObjectTargetKind.Line, spring.TargetKind, "the hot spring is its LINE, not a model");
        Equal("Hot spring", spring.Label, "named as Cloud names it in dialog 55, 'Hmm, a hot springs.'");
        Equal((1, 199, (byte)0x01), (spring.CollectedBank, spring.CollectedAddress, spring.CollectedMask),
            "done once Cloud's script 4 has set bank 1 byte 199 bit 0");
        Equal(true, spring.UsesPlayerCollisionRadius, "reached on the engine's own LINE touch");

        var memory = new ObjectMemory();
        var enabled = true;
        var reader = memory.Reader(entity => enabled);
        var position = new FieldPositionSnapshot(1, HotSpringField, 0, -330, 100, 99, 0, 0);
        Equal(true, reader.ReadTargets(position).Any(t => t.Label == "Hot spring"), "an untouched spring is offered");
        memory.Bank1[199] = 0x01;
        Equal(false, reader.ReadTargets(position).Any(t => t.Label == "Hot spring"), "a touched spring is done");
        memory.Bank1[199] = 0x00;
        enabled = false;
        Equal(false, reader.ReadTargets(position).Any(t => t.Label == "Hot spring"),
            "after the question the lines are off for the rest of the visit, and so is the target");
    }

    private static void TheCrossingStartsAreOfferedOnTheirOwnShores()
    {
        var south = Definition(LakeField, SouthShoreStart);
        var north = Definition(LakeField, NorthShoreStart);
        Equal("Ice floes, start the crossing from the south shore", south.Label, "south shore start");
        Equal("Ice floes, start the crossing from the north shore", north.Label, "north shore start");
        foreach (var start in new[] { south, north })
        {
            Equal(FieldNavigationObjectTargetKind.Line, start.TargetKind, "the crossing starts at a LINE");
            Equal(true, start.UsesPlayerCollisionRadius, "GoOnce runs on entering the LINE's range");
            Equal(-1, start.CollectedBank, "the crossing can be started again");
        }
    }

    private static void TheTreasuresGoByTheirLiveModels()
    {
        foreach (var treasure in Treasures)
        {
            var definition = Definition(treasure.Field, treasure.Entity);
            Equal(FieldNavigationObjectTargetKind.Model, definition.TargetKind, $"{treasure.Name} is its model");
            Equal(true, definition.UsesTalkInteraction, $"{treasure.Name} is picked up by talking to it");
            Equal(-1, definition.CollectedBank, $"{treasure.Name} goes by live visibility, which its Init sets");
        }
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var catalog = new FieldScriptNavigationCatalog(root);
        CheckNativeNpcTargets(catalog, new FlevelFieldTextResolver(root));
        var fields = Enumerable.Range(658, 31).Where(f => f is not (669 or 685)).ToArray();
        var scripts = fields.ToDictionary(f => f, f => catalog.ReadAllScriptOpcodes(f));
        string Hex(int field, int entity, int script) => string.Concat(scripts[field]
            .Single(s => s.EntityId == entity && s.ScriptId == script).Opcodes
            .Select(o => Convert.ToHexString(o.Bytes.ToArray())));

        // The hot spring, in hyou10's own bytes.
        var cloud = scripts[HotSpringField].Single(s => s.EntityName == "cloud" && s.ScriptId == 0).EntityId;
        var eventEntity = scripts[HotSpringField].Single(s => s.EntityName == "event" && s.ScriptId == 0).EntityId;
        foreach (var line in Enumerable.Range(20, 4))
        {
            Contains(Hex(HotSpringField, line, 2), $"03{eventEntity:X2}A3", $"line8{line - 20}'s Move asks about the spring");
            Contains(Hex(HotSpringField, line, 7), "D100", $"line8{line - 20} is switched off after the question");
        }

        var question = Hex(HotSpringField, eventEntity, 3);
        Contains(question, "48050136020300", "the spring asks dialog 54: Touch it, or Let's go on");
        Contains(question, $"03{cloud:X2}A4", "Touch it runs Cloud's script 4");
        var touch = Hex(HotSpringField, cloud, 4);
        Contains(touch, "400137", "Cloud says dialog 55, 'Hmm, a hot springs.'");
        Contains(touch, "9110C701", "and sets bank 1 byte 199 bit 0");
        var snow = scripts[684].Single(s => s.EntityName == "snoww" && s.ScriptId == 0).EntityId;
        Contains(Hex(684, snow, 1), "1510C70106", "Snow fights only once the hot spring has been touched");
        Contains(Hex(684, snow, 1), "7000A702", "and the fight is battle 679");

        var source = new FlevelDataSource(root);
        // The All cave is entered from the snowfield, not from another field: field.tbl's
        // entries are the world map's own MAPJUMPs.
        var world = new LgpArchiveReader(Path.Combine(root, "data", "wm", "world_us.lgp"));
        Equal(true, world.TryReadFile("field.tbl", out var fieldTable), "field.tbl is installed");
        worldArrivals = Enumerable.Range(0, fieldTable.Length / 12)
            .Select(record => (Field: (int)BitConverter.ToUInt16(fieldTable, record * 12 + 6),
                X: (int)BitConverter.ToInt16(fieldTable, record * 12),
                Y: (int)BitConverter.ToInt16(fieldTable, record * 12 + 2),
                Triangle: (int)BitConverter.ToUInt16(fieldTable, record * 12 + 4)))
            .Where(entry => entry.Field != 0)
            .ToArray();
        var routes = 0;
        var spring = Definition(HotSpringField, HotSpringEntity);
        var springLine = NativeLine(scripts[HotSpringField], HotSpringEntity);
        Equal((springLine.Mid.X, springLine.Mid.Y, springLine.Mid.Z), (spring.StaticX, spring.StaticY, spring.StaticZ),
            "the hot spring target sits on its own LINE");
        routes += RouteFromEveryArrival(catalog, source, scripts, HotSpringField, "Hot spring",
            springLine.Line, 0, _ => true);

        // The crossing starts: each is reached from its own shore and not from the other.
        foreach (var (entity, shoreSouth) in new[] { (SouthShoreStart, true), (NorthShoreStart, false) })
        {
            var definition = Definition(LakeField, entity);
            var line = NativeLine(scripts[LakeField], entity);
            Equal((line.Mid.X, line.Mid.Y, line.Mid.Z), (definition.StaticX, definition.StaticY, definition.StaticZ),
                $"{definition.Label} sits on its own LINE");
            Contains(Hex(LakeField, entity, 5), $"805005{(shoreSouth ? 0 : 1):X2}0317A3", $"{definition.Label} starts Cloud's crossing");
            routes += RouteFromEveryArrival(catalog, source, scripts, LakeField, definition.Label, line.Line, 0,
                arrival => shoreSouth ? arrival.Y < 0 : arrival.Y > 0);
        }

        // The seven treasures: hidden by their own Init once taken, and walked to.
        foreach (var treasure in Treasures)
        {
            var init = Hex(treasure.Field, treasure.Entity, 0);
            var talk = Hex(treasure.Field, treasure.Entity, 1);
            Contains(init, $"1410{treasure.FlagAddress:X2}{treasure.FlagMask:X2}06", $"{treasure.Name}'s Init tests its flag");
            Contains(init, "A400", $"{treasure.Name}'s Init hides it once taken");
            Contains(talk, $"9110{treasure.FlagAddress:X2}{treasure.FlagMask:X2}", $"picking {treasure.Name} up sets its flag");
            var placed = scripts[treasure.Field].Single(s => s.EntityId == treasure.Entity && s.ScriptId == 0).Opcodes
                .Single(o => o.Opcode == 0xA5).Bytes.ToArray();
            var x = BitConverter.ToInt16(placed, 3);
            var y = BitConverter.ToInt16(placed, 5);
            var z = BitConverter.ToInt16(placed, 7);
            Func<(int X, int Y, int Triangle, int Node), bool> arrives = treasure.Field == 675
                // Added Cut stands in move_d only at corridor state 75.
                ? arrival => arrival.Node == 75
                : _ => true;
            routes += RouteFromEveryArrival(catalog, source, scripts, treasure.Field, treasure.Name,
                null, 48, arrives, (x, y, z));
        }

        var snowPlacement = scripts[684].Single(s => s.EntityId == snow && s.ScriptId == 0).Opcodes
            .Single(o => o.Opcode == 0xA5).Bytes.ToArray();
        var snowRoutes = RouteFromEveryArrival(catalog, source, scripts, 684, "Snow",
            null, 40, _ => true, (BitConverter.ToInt16(snowPlacement, 3),
                BitConverter.ToInt16(snowPlacement, 5), BitConverter.ToInt16(snowPlacement, 7)));
        Equal(true, snowRoutes > 0, "the cave arrival reaches the optional NPC before the Alexander reward");
        routes += snowRoutes;

        // The named ways back: the All cave's one script exit leads to world entry 64, and the
        // cabin's gateways are where the labels say.
        Equal("script-exit:682:1:64", string.Join(";", catalog.ReadField(682).Exits.Select(e => e.StableId)),
            "hyou12's only exit is its LINE back to the snowfield");
        foreach (var (field, index, destination) in new[] { (686, 0, 687), (686, 1, 60), (687, 0, 686), (687, 1, 688), (688, 0, 687) })
        {
            Equal(true, source.TryReadField(field, out var encodedCabin), $"field {field} is installed");
            var cabin = Ff7LzsDecoder.DecodeFieldFile(encodedCabin);
            var triggerSection = BitConverter.ToInt32(cabin, 6 + 7 * 4) + 4;
            Equal(destination, (int)BitConverter.ToUInt16(cabin, triggerSection + 0x38 + index * 24 + 18),
                $"field {field} gateway {index} leads to {destination}");
        }

        Console.WriteLine($"Great Glacier optional content: {routes} native arrivals reach the hot spring, the crossing starts, Snow and the seven treasures ({string.Join(", ", breakdown)}).");
        breakdown.Clear();
    }

    private static void CheckNativeNpcTargets(FieldScriptNavigationCatalog catalog, FlevelFieldTextResolver text)
    {
        // Holzoff in the front room is a scripted scene actor: its Talk script is RET.
        // His interactive rest dialogue is in the back room (688).
        Equal(false, catalog.ReadField(687).Npcs.Any(d => d.EntityId == 14),
            "the front-room scene actor is not a fabricated conversation target");
        Equal(true, catalog.ReadAllScriptOpcodes(687).Single(s => s.EntityId == 14 && s.ScriptId == 1)
            .Opcodes.All(o => o.Opcode == 0), "front-room Holzoff has no native Talk action");
        foreach (var (field, entity) in new[] { (684, 13), (688, 13) })
        {
            // Read actual Talk metadata and dialogue, without injecting names into the test.
            var definitions = catalog.ReadField(field).Npcs;
            Equal(true, definitions.Any(d => d.EntityId == entity),
                $"native NPC {field}/{entity} exists among {string.Join(",", definitions.Select(d => d.EntityId))}");
            var definition = definitions.Single(d => d.EntityId == entity);
            const int events = 0x02500000;
            var npc = events + FieldNavigationObjectReader.FieldEventDataStride;
            var hidden = false;
            var talkDisabled = false;
            byte Byte(int a) => a == FieldPositionReader.AddressFieldNumModels ? (byte)2 :
                a == FieldNavigationObjectReader.AddressFieldModelIdArray + entity ? (byte)1 :
                a == npc + FieldNavigationObjectReader.VisibilityOffset ? (hidden ? (byte)0 : (byte)1) :
                a == npc + FieldNavigationNpcReader.TalkDisabledOffset ? (talkDisabled ? (byte)1 : (byte)0) :
                a >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                a < FieldNavigationObjectReader.AddressFieldModelIdArray + 256 ? (byte)255 : (byte)0;
            var reader = new FieldNavigationNpcReader(
                a => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0,
                _ => 20, Byte, text.ReadMessageLinesById, _ => [definition]);
            var position = new FieldPositionSnapshot(1, field, 0, 0, 0, 0, 0, 0);
            var targets = reader.ReadTargets(position);
            Equal(1, targets.Count, $"native NPC {field}/{entity} has an accessible name and target");
            Equal(FieldNavigationCategory.Npcs, targets[0].Category, "the person belongs in NPCs");
            hidden = true;
            Equal(0, reader.ReadTargets(position).Count, "hidden actors are absent");
            hidden = false;
            talkDisabled = true;
            Equal(0, reader.ReadTargets(position).Count, "scene actors with Talk disabled are absent");
        }
        Console.WriteLine("Great Glacier: Snow and Holzoff's rest dialogue retain native NPC labels and visibility/Talk gates; the front-room scene actor is excluded.");
    }

    /// <summary>
    /// Routes to a target from every MAPJUMP into its field that <paramref name="include"/>
    /// accepts, and returns how many were checked. A field entered through a corridor state
    /// records the bank 1 byte 184 its caller wrote as the arrival's node.
    /// </summary>
    private static int RouteFromEveryArrival(
        FieldScriptNavigationCatalog catalog,
        FlevelDataSource source,
        IReadOnlyDictionary<int, IReadOnlyList<FieldScriptDefinition>> scripts,
        int field,
        string label,
        FieldNavigationTriggerLine? line,
        int radius,
        Func<(int X, int Y, int Triangle, int Node), bool> include,
        (int X, int Y, int Z)? point = null)
    {
        var arrivals = Arrivals(scripts, field).Where(include).ToArray();
        Equal(true, arrivals.Length > 0, $"{label} has a native arrival to start from");
        breakdown.Add($"{label} {arrivals.Length}");
        Equal(true, source.TryReadField(field, out var encoded), $"field {field} is installed");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        const int pointer = 0x03000000;
        int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
            a >= pointer && a + 4 <= pointer + bytes.Length ? BitConverter.ToInt32(bytes, a - pointer) : 0;
        short Short(int a) => a >= pointer && a + 2 <= pointer + bytes.Length ? BitConverter.ToInt16(bytes, a - pointer) : (short)0;
        var reader = new FieldWalkmeshReader(Int, Short);
        var mesh = reader.Read(new FieldPositionSnapshot(1, field, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var planner = new FieldWalkmeshRoutePlanner(reader, transitionProvider: _ => catalog.ReadField(field).Transitions);
        var target = line is { } segment
            ? new FieldNavigationTarget(field, FieldNavigationCategory.Objects, label,
                (segment.StartX + segment.EndX) / 2, (segment.StartY + segment.EndY) / 2, (segment.StartZ + segment.EndZ) / 2,
                $"glacier-optional:{field}:{label}", TriggerLine: segment, LineActivationRadius: 20)
            : new FieldNavigationTarget(field, FieldNavigationCategory.Objects, label,
                point!.Value.X, point.Value.Y, point.Value.Z, $"glacier-optional:{field}:{label}", InteractionRadius: radius);
        foreach (var arrival in arrivals)
        {
            Equal(true, arrival.Triangle < mesh.Triangles.Count, $"{label}: native arrival triangle is on the walkmesh");
            var position = new FieldPositionSnapshot(1, field, 0, arrival.X, arrival.Y,
                (int)Math.Round(mesh.Triangles[arrival.Triangle].GetCentroid().Z), (ushort)arrival.Triangle, 0);
            Equal(true, planner.TryBuildRoute(position, target, out _),
                $"{label} in field {field} from the arrival at ({arrival.X},{arrival.Y}) triangle {arrival.Triangle}: {planner.LastDiagnostic}");
        }

        return arrivals.Length;
    }

    /// <summary>
    /// Every MAPJUMP into <paramref name="field"/> from the glacier's scripts, with the bank 1
    /// byte 184 its script wrote just before, which is how the corridor screens choose.
    /// </summary>
    private static IEnumerable<(int X, int Y, int Triangle, int Node)> Arrivals(
        IReadOnlyDictionary<int, IReadOnlyList<FieldScriptDefinition>> scripts,
        int field)
    {
        var found = new HashSet<(int, int, int, int)>();
        foreach (var script in scripts.Values.SelectMany(s => s))
        {
            var node = -1;
            foreach (var op in script.Opcodes)
            {
                var b = op.Bytes.ToArray();
                if (op.Opcode == 0x80 && b[1] == 0x10 && b[2] == 184) node = b[3];
                if (op.Opcode != 0x60 || BitConverter.ToUInt16(b, 1) != field) continue;
                found.Add((BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5), BitConverter.ToUInt16(b, 7), node));
            }
        }

        foreach (var entry in worldArrivals.Where(entry => entry.Field == field))
        {
            found.Add((entry.X, entry.Y, entry.Triangle, -1));
        }

        // A corridor screen's jump scripts are shared; the node its event script set before
        // requesting the jump is recorded by the corridor graph, so pair them from there.
        foreach (var (x, y, triangle, node) in found)
        {
            yield return (x, y, triangle, node);
        }

        if (field is >= 670 and <= 675)
        {
            foreach (var arrival in CorridorArrivals(scripts, field))
            {
                if (!found.Contains(arrival)) yield return arrival;
            }
        }
    }

    /// <summary>
    /// A corridor screen's event scripts write bank 1 byte 184 and then request one of the
    /// jump entity's scripts, whose MAPJUMP carries no state of its own. This pairs them.
    /// </summary>
    private static IEnumerable<(int X, int Y, int Triangle, int Node)> CorridorArrivals(
        IReadOnlyDictionary<int, IReadOnlyList<FieldScriptDefinition>> scripts,
        int field)
    {
        foreach (var source in scripts.Keys.Where(f => f is >= 670 and <= 675))
        {
            var jumpEntity = scripts[source].Single(s => s.EntityName == "jump" && s.ScriptId == 0).EntityId;
            foreach (var script in scripts[source].Where(s => s.EntityName == "event"))
            {
                var node = -1;
                foreach (var op in script.Opcodes)
                {
                    var b = op.Bytes.ToArray();
                    // Each branch opens with IFUB 1[184] == n; a branch that requests a jump
                    // without writing leaves the state at n.
                    if (op.Opcode == 0x14 && b[1] == 0x10 && b[2] == 184 && b[4] == 0) node = b[3];
                    if (op.Opcode == 0x80 && b[1] == 0x10 && b[2] == 184) node = b[3];
                    if (op.Opcode is not (0x01 or 0x02 or 0x03) || b[1] != jumpEntity) continue;
                    var jump = scripts[source].Single(s => s.EntityId == jumpEntity && s.ScriptId == (b[2] & 31)).Opcodes
                        .Single(o => o.Opcode == 0x60).Bytes.ToArray();
                    if (BitConverter.ToUInt16(jump, 1) == field)
                    {
                        yield return (BitConverter.ToInt16(jump, 3), BitConverter.ToInt16(jump, 5),
                            BitConverter.ToUInt16(jump, 7), node);
                    }
                }
            }
        }
    }

    private static (FieldNavigationTriggerLine Line, (int X, int Y, int Z) Mid) NativeLine(
        IReadOnlyList<FieldScriptDefinition> scripts, int entity)
    {
        var b = scripts.Single(s => s.EntityId == entity && s.ScriptId == 0).Opcodes
            .Single(o => o.Opcode == 0xD0).Bytes.ToArray();
        int C(int i) => BitConverter.ToInt16(b, 1 + i * 2);
        var line = new FieldNavigationTriggerLine(C(0), C(1), C(2), C(3), C(4), C(5));
        return (line, ((C(0) + C(3)) / 2, (C(1) + C(4)) / 2, (C(2) + C(5)) / 2));
    }

    private static FieldNavigationObjectDefinition Definition(int field, int entity)
    {
        var matches = FieldNavigationObjectCatalog.CreateAllFields()
            .Where(d => d.FieldId == field && d.EntityId == entity).ToArray();
        Equal(1, matches.Length, $"field {field} entity {entity} has one object definition");
        return matches[0];
    }

    private sealed class ObjectMemory
    {
        private const int Events = 0x02500000;
        public byte[] Bank1 { get; } = new byte[256];

        public FieldNavigationObjectReader Reader(Func<int, bool> isLineEnabled) => new(
            ReadInt32,
            ReadByte,
            _ => "Item",
            _ => "Materia",
            FieldNavigationObjectCatalog.CreateAllFields(),
            isLineEnabled);

        private int ReadInt32(int address) =>
            address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? Events : ReadByte(address) | ReadByte(address + 1) << 8;

        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressFieldNumModels) return 1;
            if (address == Events + FieldNavigationNpcReader.CollisionRadiusOffset) return 20;
            var bank = address - FieldNavigationObjectReader.AddressFieldBankBase;
            return bank is >= 0 and < 256 ? Bank1[bank] : (byte)0;
        }
    }

    private static void Contains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Great Glacier optional content: {message}. Expected {expected} in {text}.");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Great Glacier optional content: {message}. Expected {expected}, got {actual}.");
        }
    }
}
