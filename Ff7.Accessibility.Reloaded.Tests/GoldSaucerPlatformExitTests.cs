using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class GoldSaucerPlatformExitTests
{
    public static void Run()
    {
        var platforms = GoldSaucerPlatformExitCatalog.ForField(497);
        Check(platforms.Count == 7, "all seven visible platforms belong in Exits");
        Check(GoldSaucerPlatformExitCatalog.ForField(496).Count == 0, "platforms must not leak into the station");
        Check(platforms.All(p => p.Category == FieldNavigationCategory.Exits && p.TriggerEntityId == -1 &&
            p.CompletionTriangles?.Count == 2 && p.TriggerLine is null),
            "platforms use triangle activation, not an unrelated LINE enable flag");
        var labels = new FieldExitLabelResolver(_ => FieldMapNameResolution.Known(["Gold Saucer"]),
            () => "Gold Saucer").Resolve(platforms);
        Check(labels.Select(p => p.Label).Distinct().Count() == 7,
            "a shared map name must not collapse every platform into the same spoken label");
        Check(labels.Any(p => p.Label == "Platform to Speed Square" && p.DestinationFieldIds!.Single() == 486),
            "Speed Square must not be confused with Event Square");
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var source = new FlevelDataSource(root);
        var catalog = new FieldScriptNavigationCatalog(root);
        var exits = catalog.ReadField(497).Exits;
        Check(exits.Count == 7, "the shipping script catalog must expose the platforms");
        var dispatch = catalog.ReadAllScriptOpcodes(497).Single(s => s.EntityId == 1 && s.ScriptId == 4);
        // Native cloud/Script4 compares the triangle before each MAPJUMP. The lowest
        // pair is the final else; chekun/Main already rejects triangles below24.
        var branches = new[] { (36,488,10,38), (34,486,50,78), (32,484,90,118),
            (30,509,130,158), (28,505,170,198), (26,499,210,238), (24,491,-1,270) };
        foreach (var (triangle, destination, conditionAt, jumpAt) in branches)
        {
            var jump = dispatch.Opcodes.Single(o => o.ByteIndex == jumpAt);
            Check(jump.Opcode == 0x60 && BitConverter.ToUInt16(jump.Bytes.ToArray(), 1) == destination,
                $"native platform {triangle} must enter field {destination}");
            if (conditionAt >= 0)
            {
                var condition = dispatch.Opcodes.Single(o => o.ByteIndex == conditionAt).Bytes;
                Check(condition[0] == 0x16 && condition[1] == 0x60 && condition[2] == 7 &&
                    condition[4] == triangle && condition[6] == 4, "native triangle threshold changed");
            }
            var target = exits.Single(e => e.DestinationFieldIds!.Single() == destination);
            Check(target.CompletionTriangles!.SequenceEqual(new[] { triangle, triangle + 1 }),
                "completion must require standing on the native platform");
        }

        var bytes = ReadField(source, 497);
        const int pointer = 0x02000000;
        int Int(int address) => address == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
            address >= pointer && address + 4 <= pointer + bytes.Length ? BitConverter.ToInt32(bytes, address - pointer) : 0;
        short Short(int address) => address >= pointer && address + 2 <= pointer + bytes.Length ?
            BitConverter.ToInt16(bytes, address - pointer) : (short)0;
        var reader = new FieldWalkmeshReader(Int, Short);
        var mesh = reader.Read(new(1, 497, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var station = ReadField(source, 496);
        var gateway = BitConverter.ToInt32(station, 6 + 7 * 4) + 4 + 0x38;
        Check(BitConverter.ToUInt16(station, gateway + 18) == 497, "station gateway must enter the Terminal Floor");
        var startTriangle = BitConverter.ToUInt16(station, gateway + 16);
        var start = new FieldPositionSnapshot(1, 497, 0,
            BitConverter.ToInt16(station, gateway + 12), BitConverter.ToInt16(station, gateway + 14),
            (int)Math.Round(mesh.Triangles[startTriangle].GetCentroid().Z), startTriangle, 0);
        var planner = new FieldWalkmeshRoutePlanner(reader);
        foreach (var target in exits)
        {
            Check(planner.TryBuildRoute(start, target, out var route),
                $"{target.Label} must be reachable from the native station arrival: {planner.LastDiagnostic}");
            Check(target.CompletionTriangles!.Contains(route.TargetTriangle),
                $"{target.Label} route must reach an activation triangle, not stop on the plaza");
        }
        Console.WriteLine("Gold Saucer: seven platform exits verified against native dispatch and arrival routes.");
    }

    private static byte[] ReadField(FlevelDataSource source, int field)
    {
        Check(source.TryReadField(field, out var encoded), $"native field {field} missing");
        return Ff7LzsDecoder.DecodeFieldFile(encoded);
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
