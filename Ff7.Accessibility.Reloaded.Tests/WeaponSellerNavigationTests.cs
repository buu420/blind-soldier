using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WeaponSellerNavigationTests
{
    public static void Run()
    {
        var memory = new Memory();
        var story = new FieldStoryTargetReader(memory.Int32, memory.Int16, memory.Byte,
            FieldStoryEventCatalog.CreateAllFields(), _ => true);
        memory.Moment(566);
        memory.Bank(11, 132, 1); // Native automatic introduction has finished.
        var targets = story.ReadTargets(memory.Position);
        Check(targets.Any(t => t.Label == "Ask the weapon seller about the Keystone" && t.TriggerEntityId == 4),
            "first visit offers the seller's real conversation");
        Check(targets.Any(t => t.Label == "Leave the weapon seller's house" && t.TriggerLine.HasValue),
            "optional conversation cannot hide the way out");
        memory.Moment(565);
        Check(story.ReadTargets(memory.Position).Count == 0, "closed house has no actionable Story");
        memory.Moment(609);
        Check(!story.ReadTargets(memory.Position).Any(t => t.Label.Contains("Keystone")),
            "obsolete Keystone conversation is not offered after its native branch ends");

        var objects = new FieldNavigationObjectReader(memory.Int32, memory.Byte, _ => null, _ => null,
            FieldNavigationObjectCatalog.CreateAllFields(), _ => memory.LineEnabled);
        memory.Moment(566);
        Check(!objects.ReadTargets(memory.Position).Any(t => t.Label.Contains("box")),
            "reward boxes are not offered before permission");
        memory.Bank(11, 132, 0x11);
        targets = objects.ReadTargets(memory.Position);
        Check(targets.Any(t => t.Label == "Small box upstairs" && t.X == -466 && t.Y == -87 && t.Z == 240 && t.InteractionRadius == 39),
            "small reward uses upstairs native interaction");
        Check(targets.Any(t => t.Label == "Large box downstairs" && t.X == 225 && t.Y == -24 && t.Z == 36 && t.InteractionRadius == 39),
            "large reward uses downstairs native interaction");
        memory.Bank(1, 51, 2); // Native small-box collection.
        memory.Bank(11, 132, 1); // Either choice consumes the shared permission.
        Check(!objects.ReadTargets(memory.Position).Any(t => t.Label.Contains("box")),
            "taking one reward withdraws both choices");
        memory.Bank(11, 132, 0x11);
        memory.LineEnabled = false;
        Check(objects.ReadTargets(memory.Position).Count == 0, "disabled native lines stay unavailable");
        Console.WriteLine("Weapon seller story and reward-state tests passed.");
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrEmpty(root)) return;
        var scripts = new FieldScriptNavigationCatalog(root);
        var field = scripts.ReadField(79);
        var ops = scripts.ReadAllScriptOpcodes(79);
        foreach (var (entity, script, raw) in new[]
        {
            (5, 0, "D02DFE81FFF0002FFED0FFF000"),
            (6, 0, "D03601EDFF24008D00E3FF2400"),
            (5, 1, "14B084040A06"),
            (6, 1, "14B084040A06")
        })
            Check(ops.Any(s => s.EntityId == entity && s.ScriptId == script &&
                  s.Opcodes.Any(o => Convert.ToHexString(o.Bytes.ToArray()) == raw)),
                $"installed field confirms reward geometry/permission for {entity}:{script}");
        var seller = field.Npcs.Single(d => d.EntityId == 4);
        var memory = new Memory();
        var reader = new FieldNavigationNpcReader(memory.Int32, memory.Int16, memory.Byte,
            (_, _) => [], _ => [seller], isLineEnabled: _ => memory.LineEnabled);
        var target = reader.ReadTargets(memory.Position).Single();
        Check(target.Label == "Weapon seller" && target.X == -176 && target.Y == -12 && target.Z == 0 &&
              target.TriggerEntityId == 4 && target.TriggerLine is null,
            "talkable seller must follow his model rather than upstairs reward box l1");
        memory.SellerX = 150;
        memory.LineEnabled = false;
        target = reader.ReadTargets(memory.Position).Single();
        Check(target.X == 150 && target.TriggerLine is null,
            "seller's walking animation and irrelevant box LINE state cannot misroute the NPC");
        memory.Hidden = true;
        Check(reader.ReadTargets(memory.Position).Count == 0, "hidden seller is not offered");
        RoutesUseThePlayableFloor(root);
        Console.WriteLine("Weapon seller native NPC tests passed.");
    }

    private static void RoutesUseThePlayableFloor(string root)
    {
        var source = new FlevelDataSource(root);
        Check(source.TryReadField(79, out var encoded), "installed weapon seller field is readable");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        const int pointer = 0x02000000;
        int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
            a >= pointer && a + 4 <= pointer + bytes.Length ? BitConverter.ToInt32(bytes, a - pointer) : 0;
        short Short(int a) => a >= pointer && a + 2 <= pointer + bytes.Length ?
            BitConverter.ToInt16(bytes, a - pointer) : (short)0;
        var meshReader = new FieldWalkmeshReader(Int, Short);
        var mesh = meshReader.Read(new(1,79,0,-70,-124,0,28,0)).Walkmesh!;
        // Native Cloud placement is triangle 28. Eight other triangles are three
        // disconnected furniture tops; they cannot be reached by walking from the door.
        var floor = new HashSet<int> { 28 };
        var pending = new Queue<int>();
        pending.Enqueue(28);
        while (pending.TryDequeue(out var index))
            for (var edge = 0; edge < 3; edge++)
            {
                var next = mesh.Triangles[index].GetAdjacentTriangle(edge);
                if (next >= 0 && floor.Add(next)) pending.Enqueue(next);
            }
        Check(floor.Count == 65, "native entrance component contains the 65 playable floor triangles");
        var memory = new Memory();
        memory.Moment(566);
        memory.Bank(11,132,0x11);
        var objects = new FieldNavigationObjectReader(memory.Int32,memory.Byte,_=>null,_=>null,
            FieldNavigationObjectCatalog.CreateAllFields(),_=>true);
        var story = new FieldStoryTargetReader(memory.Int32,memory.Int16,memory.Byte,
            FieldStoryEventCatalog.CreateAllFields(),_=>true);
        var targets = objects.ReadTargets(memory.Position).Concat(story.ReadTargets(memory.Position)).ToArray();
        Check(targets.Length == 5, "route checks cover both boxes, the bed, seller and exit");
        foreach (var target in targets)
            foreach (var index in floor)
            {
                var p = mesh.Triangles[index].GetCentroid();
                var planner = new FieldWalkmeshRoutePlanner(meshReader);
                Check(planner.TryBuildRoute(new(1,79,0,(int)p.X,(int)p.Y,(int)p.Z,(ushort)index,0),target,out _),
                    $"{target.Label} from native floor triangle {index}: {planner.LastDiagnostic}");
            }
        Console.WriteLine($"Weapon seller: {targets.Length * floor.Count} native floor routes passed.");
    }

    private sealed class Memory
    {
        private const int Events = 0x03000000;
        private const int Seller = Events + 3 * FieldNavigationObjectReader.FieldEventDataStride;
        private readonly Dictionary<int, byte> bytes = [];
        public int SellerX = -176;
        public bool LineEnabled = true;
        public bool Hidden;
        public FieldPositionSnapshot Position => new(1, 79, 0, 0, 0, 0, 0, 0);
        public int Int32(int a) => a switch
        {
            FieldNavigationObjectReader.AddressFieldEventDataPtr => Events,
            Seller + FieldNavigationObjectReader.PositionXOffset => SellerX * 4096,
            Seller + FieldNavigationObjectReader.PositionYOffset => -12 * 4096,
            _ => 0
        };
        public short Int16(int a) => a == Events + FieldNavigationNpcReader.CollisionRadiusOffset ||
            a == Seller + FieldNavigationNpcReader.TalkRadiusOffset ? (short)40 : (short)0;
        public byte Byte(int a) => a switch
        {
            FieldPositionReader.AddressFieldNumModels => 4,
            FieldNavigationObjectReader.AddressFieldModelIdArray + 4 => 3,
            Seller + FieldNavigationObjectReader.VisibilityOffset => Hidden ? (byte)0 : (byte)1,
            Events + FieldNavigationNpcReader.CollisionRadiusOffset => 40,
            _ => bytes.GetValueOrDefault(a)
        };
        public void Moment(int moment)
        {
            Bank(1, 0, (byte)moment);
            Bank(1, 1, (byte)(moment >> 8));
        }
        public void Bank(int bank, int index, byte value) =>
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + (bank == 11 ? 0x200 : 0) + index] = value;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Weapon seller: " + message);
    }
}
