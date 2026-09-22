using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class GreatGlacierStoryTests
{
    private static readonly HashSet<int> Fields = Enumerable.Range(658, 27).Where(f => f != 669).ToHashSet();
    private static FieldStoryEventDefinition[] Rows => FieldStoryEventCatalog.CreateAllFields()
        .Where(r => Fields.Contains(r.FieldId)).ToArray();

    public static void Run()
    {
        var rows = Rows;
        Check(Fields.All(f => rows.Any(r => r.FieldId == f)), "every walkable glacier field needs a Story step");
        foreach (var row in rows)
        {
            var state = row.RequiredCondition.Mask == 0 ? 0 : row.RequiredCondition.Value;
            var memory = new Memory(state);
            var offered = memory.Reader().ReadTargets(new(1, row.FieldId, 0, 0, 0, 0, 0, 0));
            Check(offered.Count == 1 && offered[0].TriggerLine == row.TriggerLine,
                $"field {row.FieldId}, route state {state} must select one native boundary");
            Check(offered[0].CompletesOnArrival && offered[0].TriggerLine is not null,
                "walking through a corridor cannot become a paused Confirm interaction");
            Check(string.IsNullOrWhiteSpace(offered[0].ManualNavigationGuidance) == (row.FieldId != 665),
                "only the optional ice-floe puzzle requires manual traversal");
            memory.Enabled = false;
            Check(memory.Reader().ReadTargets(new(1, row.FieldId, 0, 0, 0, 0, 0, 0)).Count == 0,
                "a disabled native boundary must disappear");
            memory.Enabled = true;
            memory.Write(1, 0, 770, true);
            Check(memory.Reader().ReadTargets(new(1, row.FieldId, 0, 0, 0, 0, 0, 0)).Count == 0,
                "first-visit routing must retire at the Whirlwind Maze");
        }
        foreach (var f in Enumerable.Range(670, 6))
            Check(new Memory(255).Reader().ReadTargets(new(1, f, 0, 0, 0, 0, 0, 0)).Count == 0,
                "an unknown corridor state is not guessed");
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var catalog = new FieldScriptNavigationCatalog(root);
        var source = new FlevelDataSource(root);
        var scripts = Fields.ToDictionary(f => f, f => catalog.ReadAllScriptOpcodes(f));
        var rows = Rows;
        var routes = 0;
        var puzzleArrivals = 0;
        var checkedArrivals = new HashSet<string>();
        var meshCache = new Dictionary<int, (FieldWalkmeshReader Reader, FieldWalkmeshReadResult Data)>();
        foreach (var first in rows)
        {
            var memory = new Memory(first.RequiredCondition.Mask == 0 ? 0 : first.RequiredCondition.Value);
            var field = first.FieldId;
            var seen = new HashSet<(int, int)>();
            while (Fields.Contains(field))
            {
                var node = memory.Read(1, 184, false);
                Check(seen.Add((field, node)), $"native route cycles at {field}/{node}");
                var matchingRows = rows.Where(r => r.FieldId == field &&
                    (r.RequiredCondition.Mask == 0 || r.RequiredCondition.Value == node)).ToArray();
                Check(matchingRows.Length == 1, $"native state {field}/{node} has {matchingRows.Length} rows");
                var row = matchingRows[0];
                var entity = scripts[field].Where(s => s.EntityId == row.RequiredEnabledLineEntityId).ToArray();
                Check(entity.All(e => e.EntityName == row.SourceEntityName), "native LINE identity changed");
                var activations = entity.Where(s => s.ScriptId is >= 2 and <= 6 &&
                    s.Opcodes.Any(o => o.Opcode is 0x02 or 0x03 or 0x04 or 0x60)).OrderBy(s => s.ScriptId).ToArray();
                Check(activations.Length > 0, $"native state {field}/{node} entity{row.EntityId} has no boundary handler");
                var activation = activations[0];
                var steps = 0;
                var jump = Execute(scripts[field], activation.EntityId, activation.ScriptId, memory, ref steps, 0)
                    ?? throw new InvalidOperationException($"Glacier {field}/{node} LINE {row.EntityId} does not enter another field.");
                field = jump.Field;
                if (!Fields.Contains(field)) break;
                var positionKey = $"{field}:{memory.Read(1,184,false)}:{jump.X}:{jump.Y}:{jump.Triangle}";
                if (!checkedArrivals.Add(positionKey)) continue;
                if (!meshCache.TryGetValue(field, out var cached))
                {
                    Check(source.TryReadField(field, out var encoded), "glacier field archive missing");
                    var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
                    const int pointer = 0x03000000;
                    int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
                        a >= pointer && a + 4 <= pointer + bytes.Length ? BitConverter.ToInt32(bytes, a - pointer) : 0;
                    short Short(int a) => a >= pointer && a + 2 <= pointer + bytes.Length ? BitConverter.ToInt16(bytes, a - pointer) : (short)0;
                    var reader = new FieldWalkmeshReader(Int, Short);
                    cached = (reader, reader.Read(new(1, field, 0, 0, 0, 0, 0, 0)));
                    meshCache[field] = cached;
                }
                var mesh = cached.Data.Walkmesh!;
                Check(jump.Triangle < mesh.Triangles.Count, "native MAPJUMP triangle outside walkmesh");
                var position = new FieldPositionSnapshot(1, field, 0, jump.X, jump.Y,
                    (int)Math.Round(mesh.Triangles[jump.Triangle].GetCentroid().Z), (ushort)jump.Triangle, 0);
                var offered = memory.Reader().ReadTargets(position);
                Check(offered.Count == 1, $"native arrival {positionKey} offers {offered.Count} targets, moment={memory.Read(1,0,true)}");
                var target = offered[0];
                if (field == 665)
                {
                    Check(!string.IsNullOrWhiteSpace(target.ManualNavigationGuidance),
                        "the disconnected ice-floe puzzle must explain manual traversal");
                    puzzleArrivals++;
                    continue;
                }
                var planner = new FieldWalkmeshRoutePlanner(cached.Reader,
                    transitionProvider: _ => catalog.ReadField(field).Transitions);
                Check(planner.TryBuildRoute(position, target, out var route),
                    $"native arrival {positionKey} cannot reach its next boundary: {planner.LastDiagnostic}");
                Check(route.TargetTriggerLine == target.TriggerLine, "route must reach the same native boundary");
                routes++;
            }
            Check(field is >= 61 and <= 64, $"glacier route ends at unrelated field {field}");
        }
        Console.WriteLine($"Great Glacier: {rows.Length} native exit chains enter the snowfield; {routes} walkable arrivals checked, {puzzleArrivals} optional ice-floe arrivals require the game puzzle controls.");
    }

    private sealed record Jump(int Field, int X, int Y, int Triangle);

    // A deliberately bounded interpreter for exit dispatch only. Animation, sound,
    // camera and waits do not alter the bank1 route graph. Native branch offsets,
    // requests and MAPJUMPs are read from the production decoder, not an expected graph.
    private static Jump? Execute(IReadOnlyList<FieldScriptDefinition> scripts, int entity, int id,
        Memory memory, ref int steps, int depth)
    {
        Check(depth < 20, "recursive exit dispatch");
        var ops = scripts.Single(s => s.EntityId == entity && s.ScriptId == id).Opcodes;
        var offset = 0;
        while (true)
        {
            Check(++steps < 1000, "exit dispatch did not terminate");
            var matches = ops.Where(o => o.ByteIndex == offset).ToArray();
            Check(matches.Length == 1, $"invalid native script offset {entity}/{id}@{offset}");
            var op = matches[0]; var b = op.Bytes.ToArray();
            var next = offset + b.Length;
            switch (op.Opcode)
            {
                case 0: return null;
                case 0x02: case 0x03: case 0x04:
                    var called = Execute(scripts, b[1], b[2] & 31, memory, ref steps, depth + 1);
                    if (called is not null) return called;
                    break;
                case 0x10: next = offset + 1 + b[1]; break;
                case 0x11: next = offset + 1 + BitConverter.ToUInt16(b, 1); break;
                case 0x12: next = offset + 1 - b[1]; break;
                case 0x13: next = offset + 1 - BitConverter.ToUInt16(b, 1); break;
                case 0x14: case 0x15: case 0x16: case 0x17:
                    var word = op.Opcode is 0x16 or 0x17;
                    var left = memory.Read(b[1] >> 4, b[2], word);
                    var rightIndex = word ? BitConverter.ToUInt16(b, 4) : b[3];
                    var right = memory.Read(b[1] & 15, rightIndex, word);
                    var comparison = b[word ? 6 : 4];
                    var passes = comparison switch { 0 => left == right, 1 => left != right,
                        2 => left > right, 3 => left < right, 4 => left >= right, 5 => left <= right,
                        6 => (left & right) != 0, 9 => (left & (1 << right)) != 0,
                        10 => (left & (1 << right)) == 0, _ => throw new InvalidOperationException("Unknown native comparison") };
                    if (!passes)
                    {
                        var at = word ? 7 : 5;
                        next = offset + at + (op.Opcode is 0x15 or 0x17 ? BitConverter.ToUInt16(b, at) : b[at]);
                    }
                    break;
                case 0x80: case 0x81:
                    var isWord = op.Opcode == 0x81;
                    memory.Write(b[1] >> 4, b[2], memory.Read(b[1] & 15,
                        isWord ? BitConverter.ToUInt16(b, 3) : b[3], isWord), isWord);
                    break;
                case 0x60:
                    return new(BitConverter.ToUInt16(b,1), BitConverter.ToInt16(b,3), BitConverter.ToInt16(b,5), BitConverter.ToUInt16(b,7));
            }
            offset = next;
        }
    }

    private sealed class Memory
    {
        private const int Events = 0x02500000;
        private readonly Dictionary<int, byte> bytes = [];
        public bool Enabled = true;
        public Memory(int route) { Write(1, 0, 677, true); Write(1,184,route,false); }
        private static int Address(int bank, int index) => bank switch
        {
            1 or 2 => FieldNavigationObjectReader.AddressFieldBankBase + index,
            3 or 4 => FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + index,
            5 or 6 => FieldNavigationObjectReader.AddressTemporaryFieldBankBase + index,
            _ => throw new InvalidOperationException($"Unexpected glacier bank {bank}")
        };
        public int Read(int bank, int index, bool word) => bank == 0 ? index :
            Byte(Address(bank,index)) | (word ? Byte(Address(bank,index)+1) << 8 : 0);
        public void Write(int bank, int index, int value, bool word)
        { var a=Address(bank,index);bytes[a]=(byte)value;if(word)bytes[a+1]=(byte)(value>>8); }
        private byte Byte(int a) => a == FieldPositionReader.AddressFieldNumModels ? (byte)1 :
            a == Events + FieldNavigationNpcReader.CollisionRadiusOffset ? (byte)20 : bytes.GetValueOrDefault(a);
        public FieldStoryTargetReader Reader() => new(a => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? Events : 0,
            a => (short)(Byte(a) | Byte(a+1)<<8), Byte, FieldStoryEventCatalog.CreateAllFields(), _ => Enabled);
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Great Glacier: " + message); }
}
