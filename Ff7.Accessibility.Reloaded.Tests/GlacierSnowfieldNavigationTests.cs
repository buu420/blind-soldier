using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class GlacierSnowfieldNavigationTests
{
    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT")
            ?? throw new InvalidOperationException("Snowfield verification needs the source root.");
        var map = WorldMapDataLoader.Load(Path.Combine(root, "data", "wm", "WM3.MAP"), 3, 0);
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(source, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(source, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        var cases = 0;
        // Native terrain samples in the southern snowfield and beside its central
        // cave. These are static route checks, not recorded player arrivals.
        foreach (var (meshX, meshZ) in new[] { (3, 6), (5, 6), (4, 3) })
        {
            var start = map.Triangles.First(t => t.MeshX == meshX && t.MeshZ == meshZ &&
                t.TerrainScriptId == 0 && WorldMapTerrainPassability.CanTraverse(0, 3, t.TerrainId));
            var point = start.Centroid;
            var state = new WorldMapStateSnapshot(WorldMapStateReader.WorldModule, 3, 0, 677,
                point.X, point.Y, point.Z, 0, 0, start.TerrainId, start.RegionId,
                0, 30, 0, new FieldNavigationControlTransform(0)) { TerrainScriptId = start.TerrainScriptId };
            var targets = catalog.ReadTargets(WorldMapNavigationCategory.Story, state, []);
            Check(targets.Count == 1 && targets[0].Label == "The way north out of the snowfield",
                "the glacier has one northbound Story objective");
            var target = targets[0];
            Check(target.ArrivalTriangleIds.Count > 0 && target.ArrivalTriangleIds.All(id =>
            {
                var t = map.Triangles[id];
                return t.MeshZ == 1 && t.MeshX is >= 1 and <= 6 &&
                    t.TerrainScriptId == (t.MeshX is 1 or 6 ? 6 : 7);
            }), "arrival uses only the six native northern boundary handlers");
            Check(!target.HasArrived(state, start.Id), "a snowfield position is not already at the exit");
            Check(planner.TryBuildRoute(state, target, out var route), planner.LastDiagnostic);
            Check(target.ArrivalTriangleIds.Contains(route.TargetTriangleId), "the route reaches a native northern trigger");
            Check(route.TrianglePath.All(id => !planner.IsUnwantedEntrance(id, target.NativeEntranceExemptions, start.Id)),
                "the route does not enter an unrelated cave or southern glacier exit");
            var last = map.Triangles[route.TargetTriangleId];
            var arrival = state with { X = last.Centroid.X, Y = last.Centroid.Y, Z = last.Centroid.Z,
                TerrainId = last.TerrainId, TerrainScriptId = last.TerrainScriptId };
            Check(target.HasArrived(arrival, last.Id), "walking onto the northern boundary completes the step");
            Check(!target.HasArrived(arrival with { PlayerModelId = 3 }, last.Id), "the walking exit rejects a vehicle");
            Check(!target.HasArrived(arrival with { GameMoment = 770 }, last.Id), "a retained target expires after this chapter");
            cases++;
        }
        Console.WriteLine($"Glacier snowfield: {cases} native terrain routes reach the northern boundary.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Glacier snowfield: " + message);
    }
}
