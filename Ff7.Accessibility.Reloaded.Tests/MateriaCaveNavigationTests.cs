using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class MateriaCaveNavigationTests
{
    internal static void Run()
    {
        var definitions = FieldNavigationObjectCatalog.CreateAllFields();
        foreach (var (field, materia, mask) in new[] { (82, 43, 1), (84, 35, 4), (85, 89, 8) })
        {
            var rows = definitions.Where(d => d.FieldId == field && d.Kind == FieldNavigationObjectKind.Named && d.NativeId == materia).ToArray();
            Check(rows.Length == 1, $"cave {field} must offer one crystal in Objects, found {rows.Length}");
            var row = rows[0];
            Check(row.TargetKind == FieldNavigationObjectTargetKind.Line && row.EntityId == 4 && row.UsesPlayerCollisionRadius,
                $"cave {field} uses its actual OK line and collision range");
            Check(row.CollectedBank == 1 && row.CollectedAddress == 49 && row.CollectedMask == mask,
                $"cave {field} uses the native collection bit");
            Check(row.Label == "Materia crystal", "describe the visible crystal before its contents are revealed");

            const int events = 0x02404000;
            var bytes = new Dictionary<int, byte>
            {
                [FieldPositionReader.AddressFieldNumModels] = 1,
                [events + FieldNavigationNpcReader.CollisionRadiusOffset] = 30
            };
            var enabled = true;
            byte ReadByte(int address) => bytes.GetValueOrDefault(address);
            int ReadInt(int address) => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0;
            var reader = new FieldNavigationObjectReader(ReadInt, ReadByte, _ => null, _ => null, [row], _ => enabled);
            var position = new FieldPositionSnapshot(1, field, 0, 0, 0, -853, 0, 0);
            var target = reader.ReadTargets(position).Single();
            Check(target.Category == FieldNavigationCategory.Objects && target.InteractionRadius == 29 &&
                  target.Label == "Materia crystal" && target.ObjectCueKind == FieldObjectCueKind.Materia,
                "the approach remains inside the native strict activation range");
            enabled = false;
            Check(reader.ReadTargets(position).Count == 0, "a disabled crystal line is not offered");
            enabled = true;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 49] = (byte)mask;
            Check(reader.ReadTargets(position).Count == 0, "a collected crystal is no longer offered");
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 49] = 0;
            bytes[events + FieldNavigationNpcReader.CollisionRadiusOffset] = 0;
            Check(reader.ReadTargets(position).Count == 0, "missing player geometry is not replaced by a guessed range");

            if (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") is { Length: > 0 } root)
                CheckNative(root, row, materia, mask);
        }
        Check(definitions.Count(d => d.FieldId == 83 && d.Kind == FieldNavigationObjectKind.Materia && d.NativeId == 18) == 1,
            "the existing HP-MP crystal is retained without duplication");
        Console.WriteLine("Materia cave Objects, native flags, and interaction routes passed.");
    }

    private static void CheckNative(string root, FieldNavigationObjectDefinition row, int materia, int mask)
    {
        var source = new FlevelDataSource(root);
        var catalog = new FieldScriptNavigationCatalog(root);
        var init = catalog.ReadScriptOpcodes(row.FieldId, 4, 0);
        var line = init.Single(o => o.Opcode == 0xD0).Bytes.ToArray();
        Check(row.StaticX == (BitConverter.ToInt16(line, 1) + BitConverter.ToInt16(line, 7)) / 2 &&
              row.StaticY == (BitConverter.ToInt16(line, 3) + BitConverter.ToInt16(line, 9)) / 2 &&
              row.StaticZ == (BitConverter.ToInt16(line, 5) + BitConverter.ToInt16(line, 11)) / 2,
            "the crystal approach is on its native line");
        var ok = catalog.ReadScriptOpcodes(row.FieldId, 4, 1);
        Check(ok.Any(o => o.Opcode == 0x5B && o.Bytes.Count == 7 && o.Bytes[3] == materia), "native OK handler gives this materia");
        var bit = (int)Math.Log2(mask);
        Check(ok.Any(o => Convert.ToHexString(o.Bytes.ToArray()) == $"821031{bit:X2}"), "native OK handler marks this crystal collected");
        Check(source.TryReadField(row.FieldId, out var encoded), "cave is readable");
        var data = Ff7LzsDecoder.DecodeFieldFile(encoded);
        // 0060BCFA initializes collision to 30 * the script header scale / 512.
        // Prove these caves do not replace it before using that radius as evidence.
        var scripts = catalog.ReadAllScriptOpcodes(row.FieldId);
        var partyActors = scripts.Where(s => s.Opcodes.Any(o => o.Opcode is 0xA0 or 0xBF))
            .Select(s => s.EntityId).ToHashSet();
        Check(partyActors.Count > 0 && !scripts.Where(s => partyActors.Contains(s.EntityId))
            .SelectMany(s => s.Opcodes).Any(o => o.Opcode is 0xC6 or 0xD7),
            "the cave's player collision radius is the native default");
        var scale = BitConverter.ToInt16(data, BitConverter.ToInt32(data, 6) + 4 + 8);
        var nativeRadius = 30 * scale / 512;
        Check(nativeRadius > 1, "native player collision range is usable");
        const int ptr = 0x02000000;
        int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? ptr :
            a >= ptr && a + 4 <= ptr + data.Length ? BitConverter.ToInt32(data, a - ptr) : 0;
        short Short(int a) => a >= ptr && a + 2 <= ptr + data.Length ? BitConverter.ToInt16(data, a - ptr) : (short)0;
        var walkmesh = new FieldWalkmeshReader(Int, Short);
        var planner = new FieldWalkmeshRoutePlanner(walkmesh);
        var mesh = walkmesh.Read(new(1, row.FieldId, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var floor = EntryFloor(mesh, data);
        const int events = 0x02404000;
        var objects = new FieldNavigationObjectReader(
            a => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0,
            a => a == FieldPositionReader.AddressFieldNumModels ? (byte)1 :
                a == events + FieldNavigationNpcReader.CollisionRadiusOffset ? (byte)nativeRadius :
                a == events + FieldNavigationNpcReader.CollisionRadiusOffset + 1 ? (byte)(nativeRadius >> 8) : (byte)0,
            _ => null, _ => null, [row], _ => true);
        // Every floor triangle is a possible controlled start in these single-room caves.
        // This also covers returns from either side of the crystal, not one chosen entrance.
        var routes = 0;
        foreach (var triangle in mesh.Triangles)
        {
            var center = triangle.GetCentroid();
            var p = new FieldPositionSnapshot(1, row.FieldId, 0, (int)center.X, (int)center.Y, (int)center.Z, (ushort)triangle.Index, 0);
            // Some triangles belong to the crystal scenery, rather than the playable floor.
            // Keep only the floor component containing the native entry placement below.
            if (!floor.Contains(triangle.Index)) continue;
            var t = objects.ReadTargets(p).Single();
            Check(planner.TryBuildRoute(p, t, out var plan), $"cave {row.FieldId}, floor {triangle.Index}: {planner.LastDiagnostic}");
            var distance = Math.Sqrt(Math.Pow(plan.FinalApproach.X - t.X, 2) + Math.Pow(plan.FinalApproach.Y - t.Y, 2) +
                Math.Pow(plan.FinalApproach.Z - t.Z, 2));
            Check(distance < nativeRadius, "final approach is inside the native three-dimensional OK range");
            // These crystals' segments are short enough for exact integer projection.
            // Native 00637879 uses an 8-bit fraction and bounds projected X/Y.
            var a = new[] { (int)BitConverter.ToInt16(line, 1), BitConverter.ToInt16(line, 3), BitConverter.ToInt16(line, 5) };
            var d = new[] { BitConverter.ToInt16(line, 7) - a[0], BitConverter.ToInt16(line, 9) - a[1], BitConverter.ToInt16(line, 11) - a[2] };
            var end = new[] { plan.FinalApproach.X, plan.FinalApproach.Y, plan.FinalApproach.Z };
            var denominator = d.Sum(v => v * v);
            var fraction = (end.Zip(a, (v, start) => v - start).Zip(d, (v, delta) => v * delta).Sum() * 256) / denominator;
            var projection = a.Zip(d, (start, delta) => start + (fraction * delta >> 8)).ToArray();
            Check(Enumerable.Range(0, 2).All(i => projection[i] >= Math.Min(a[i], a[i] + d[i]) && projection[i] <= Math.Max(a[i], a[i] + d[i])) &&
                end.Zip(projection, (v, projected) => (v - projected) * (v - projected)).Sum() < nativeRadius * nativeRadius,
                "final approach satisfies the native segment projection and strict collision test");
            routes++;
        }
        Check(routes == floor.Count && routes > 0, "every triangle of the entry floor has a native route witness");
        Console.WriteLine($"  Cave {row.FieldId}: {routes} floor approaches reach the native crystal interaction.");
    }

    private static HashSet<int> EntryFloor(FieldWalkmesh mesh, byte[] data)
    {
        // World-map entrances have no field MAPJUMP witness. Their native return
        // gateway identifies the floor independently of the item being tested.
        var gateway = BitConverter.ToInt32(data, 6 + 7 * 4) + 4 + 0x38;
        int Midpoint(int offset) => (BitConverter.ToInt16(data, gateway + offset) +
                                    BitConverter.ToInt16(data, gateway + offset + 6)) / 2;
        var start = FieldWalkmeshPathfinder.ResolveTriangle(mesh, Midpoint(0), Midpoint(2), Midpoint(4), -1);
        Check(start >= 0 && start < mesh.Triangles.Count, "native exit resolves to the cave floor");
        var visited = new HashSet<int> { start };
        var pending = new Queue<int>();
        pending.Enqueue(start);
        while (pending.TryDequeue(out var current))
        {
            for (var edge = 0; edge < 3; edge++)
            {
                var adjacent = mesh.Triangles[current].GetAdjacentTriangle(edge);
                if (adjacent >= 0 && adjacent < mesh.Triangles.Count && visited.Add(adjacent))
                    pending.Enqueue(adjacent);
            }
        }
        return visited;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Materia cave navigation: " + message);
    }
}
