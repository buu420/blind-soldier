using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class AerisWallClimbRegressionTests
{
    internal static void Run()
    {
        var stock = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(stock))
        {
            Console.WriteLine("Aeris/wall-climb native checks require FF7_ACCESSIBILITY_DATA_ROOT.");
            return;
        }
        var echoes = (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var root in new[] { stock }.Concat(echoes))
        {
            OptionalBatteryHasAnHonestNativeRoute(root);
            DisabledBedRemainsVisibleScenery(root);
        }
    }

    private static void OptionalBatteryHasAnHonestNativeRoute(string root)
    {
        const int field = 224;
        var scripts = new FieldScriptNavigationCatalog(root);
        var native = scripts.ReadField(field);
        var line = new FieldNavigationTriggerLine(-297, 949, 2595, -223, 995, 2582);
        var target = new FieldNavigationTarget(field, FieldNavigationCategory.Objects, "Optional battery socket",
            -260, 972, 2588, "optional-battery", TriggerEntityId: 11, InteractionRadius: 0,
            TriggerLine: line, LineActivationRadius: 30);
        var source = new FlevelDataSource(root);
        Check(source.TryReadField(field, out var encoded), "native wall-climb data exists");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        const int address = 0x02000000;
        var reader = new FieldWalkmeshReader(
            at => at == FieldWalkmeshReader.AddressFieldDataPtr ? address :
                at >= address && at + 4 <= address + bytes.Length ? BitConverter.ToInt32(bytes, at - address) : 0,
            at => at >= address && at + 2 <= address + bytes.Length ? BitConverter.ToInt16(bytes, at - address) : (short)0);
        var planner = new FieldWalkmeshRoutePlanner(reader, transitionProvider: _ => native.Transitions);
        var positions = new[]
        {
            new FieldPositionSnapshot(1, field, 0, 104, 1257, 2758, 69, 0),
            new FieldPositionSnapshot(1, field, 0, -154, 1274, 2872, 99, 0),
            new FieldPositionSnapshot(1, field, 0, -322, 1131, 3333, 102, 0)
        };
        foreach (var position in positions)
        {
            var built = planner.TryBuildRoute(position, target, out var plan);
            Check(built, $"{Path.GetFileName(root)}: optional battery from triangle {position.TriangleId}: {planner.LastDiagnostic}");
            Check(plan.TargetTriangle is 80 or 81 or 82 or 87, "socket approach stays on its native ledge");
            Check(plan.Portals.Any(p => p.TransitionId == "ladder:224:12:13:13:81"), "the native descent and cleanup jumps reach the socket ledge");
            Console.WriteLine($"PASS optional battery {Path.GetFileName(root)} from triangle {position.TriangleId}");
        }
    }

    private static void DisabledBedRemainsVisibleScenery(string root)
    {
        var definition = FieldNavigationObjectCatalog.CreateAllFields().Single(d => d.FieldId == 190 && d.EntityId == 9);
        var enabled = false;
        const int events = 0x02404000;
        int Int(int at) => at == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0;
        byte Byte(int at) => at == FieldPositionReader.AddressFieldNumModels ? (byte)1 :
            at == events + FieldNavigationNpcReader.CollisionRadiusOffset ? (byte)30 : (byte)0;
        var line = new FieldNavigationTriggerLine(-257, 158, 288, -126, 131, 307);
        var objects = new FieldNavigationObjectReader(Int, Byte, _ => null, _ => null, [definition],
            isLineEnabled: _ => enabled, readLiveLine: _ => enabled ? line : null);
        var at = new FieldPositionSnapshot(1, 190, 0, -89, 299, 288, 33, 0);
        var inactive = objects.ReadTargets(at);
        Check(inactive.Count == 1, "the visible bed is findable when Echo-S disables its interaction");
        Check(inactive[0].Label == "Bed (resting unavailable)" && inactive[0].TriggerLine is null,
            "scenery cannot promise a disabled rest interaction");
        enabled = true;
        var active = objects.ReadTargets(at);
        Check(active.Count == 1 && active[0].Label == "Bed" && active[0].TriggerLine == line,
            "the enabled bed keeps its actual native touch interaction");
        var source = new FlevelDataSource(root);
        Check(source.TryReadField(190, out var encoded), "native bedroom data exists");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        const int address = 0x02000000;
        var mesh = new FieldWalkmeshReader(
            a => a == FieldWalkmeshReader.AddressFieldDataPtr ? address :
                a >= address && a + 4 <= address + bytes.Length ? BitConverter.ToInt32(bytes, a - address) : 0,
            a => a >= address && a + 2 <= address + bytes.Length ? BitConverter.ToInt16(bytes, a - address) : (short)0);
        var planner = new FieldWalkmeshRoutePlanner(mesh);
        var fromStairs = new FieldPositionSnapshot(1, 190, 0, 96, 462, 65, 8, 0);
        foreach (var target in new[] { inactive[0], active[0] })
            Check(planner.TryBuildRoute(fromStairs, target, out _), $"bed target routes on the native floor: {planner.LastDiagnostic}");
        enabled = false;
        var ordinaryLine = new FieldNavigationObjectReader(Int, Byte, _ => null, _ => null,
            [definition with { InactiveLineLabel = null }], isLineEnabled: _ => false);
        Check(ordinaryLine.ReadTargets(at).Count == 0, "other disabled interactions stay unavailable");
        Console.WriteLine($"PASS bed availability {Path.GetFileName(root)}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Aeris/wall climb: " + message);
    }
}
