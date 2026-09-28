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
        CheckSnowfieldExits(root, map, catalog, planner);
    }

    /// <summary>
    /// Every way off the snowfield is a Location, taken from the installed wm3.ev rather
    /// than restated: the central cave (All materia), the edge on to Gaea's Cliff, and the
    /// three edges that lead back into the glacier - the east one being the only way to
    /// the cave where Snow waits. Before this the snowfield had no Locations at all
    /// ("Locations: none available" in the 2026-09-27 log) and nothing kept a route from
    /// walking over one of these triggers by accident.
    /// </summary>
    private static void CheckSnowfieldExits(
        string root,
        WorldMapData map,
        WorldMapTargetCatalog catalog,
        WorldMapRoutePlanner planner)
    {
        var archive = new LgpArchiveReader(Path.Combine(root, "data", "wm", "world_us.lgp"));
        Check(archive.TryReadFile("wm3.ev", out var events), "wm3.ev is installed");
        Check(archive.TryReadFile("field.tbl", out var fieldTable), "field.tbl is installed");
        var handlers = ReadEnterFieldHandlers(events);
        (string Label, int Location, int Field, int? Corridor)[] expected =
        [
            ("North edge, to the Base of Gaea's Cliff", 60, 686, null),
            ("West edge, into the Great Glacier", 61, 670, 52),
            ("South edge, into the Great Glacier", 62, 670, 53),
            ("East edge, into the Great Glacier", 63, 670, 55),
            ("Cave in the middle of the snowfield", 64, 682, null)
        ];
        Check(handlers.Select(h => h.Location).Distinct().Order().SequenceEqual(expected.Select(e => e.Location)),
            "wm3.ev enters exactly these five locations");
        Check(catalog.Locations.Select(l => l.Label).SequenceEqual(expected.Select(e => e.Label)),
            $"the snowfield offers every native way off it, got {string.Join("; ", catalog.Locations.Select(l => l.Label))}");
        foreach (var exit in expected)
        {
            var location = catalog.Locations.Single(l => l.Label == exit.Label);
            var cells = handlers.Where(h => h.Location == exit.Location)
                .Select(h => (h.MeshX, h.MeshZ, h.Script)).ToHashSet();
            var trigger = map.Triangles.Where(t => cells.Contains((t.MeshX, t.MeshZ, t.TerrainScriptId)))
                .Select(t => t.Id).ToHashSet();
            Check(trigger.Count > 0 && location.NativeTriggerTriangleIds.SetEquals(trigger),
                $"{exit.Label} is every triangle of its own native handlers");
            Check(location.ArrivalTriangleIds.Count > 0 && location.ArrivalTriangleIds.IsSubsetOf(trigger),
                $"{exit.Label} ends inside its own handlers");
            Check(location.NativeLocationArrivals.Count > 0 &&
                  location.NativeLocationArrivals.All(a => cells.Contains((a.MeshX, a.MeshZ, a.TerrainScriptId))),
                $"{exit.Label} arrives by the game's own cell and terrain-script test");
            var record = (exit.Location * 2 - 2) * 12;
            Check(BitConverter.ToUInt16(fieldTable, record + 6) == exit.Field,
                $"field.tbl sends location {exit.Location} to field {exit.Field}");
            Check(handlers.Where(h => h.Location == exit.Location).All(h => h.Corridor == exit.Corridor),
                $"{exit.Label} sets the corridor state its field expects");
        }

        var allTriggers = map.Triangles
            .Where(t => handlers.Any(h => h.MeshX == t.MeshX && h.MeshZ == t.MeshZ && h.Script == t.TerrainScriptId))
            .Select(t => t.Id).ToHashSet();
        Check(catalog.EntranceTriangleIds.SetEquals(allTriggers),
            "every snowfield handler is an entrance a route must not cross by accident");

        var routes = 0;
        foreach (var (meshX, meshZ) in new[] { (3, 6), (5, 6), (4, 3), (2, 2), (5, 4) })
        {
            var start = map.Triangles.First(t => t.MeshX == meshX && t.MeshZ == meshZ &&
                t.TerrainScriptId == 0 && WorldMapTerrainPassability.CanTraverse(0, 3, t.TerrainId));
            var point = start.Centroid;
            var state = new WorldMapStateSnapshot(WorldMapStateReader.WorldModule, 3, 0, 677,
                point.X, point.Y, point.Z, 0, 0, start.TerrainId, start.RegionId,
                0, 30, 0, new FieldNavigationControlTransform(0)) { TerrainScriptId = start.TerrainScriptId };
            foreach (var location in catalog.ReadTargets(WorldMapNavigationCategory.Locations, state, []))
            {
                Check(!location.HasArrived(state, start.Id), $"{location.Label} is not already reached");
                Check(planner.TryBuildRoute(state, location, out var route),
                    $"{location.Label} from mesh ({meshX},{meshZ}): {planner.LastDiagnostic}");
                Check(location.ArrivalTriangleIds.Contains(route.TargetTriangleId), $"{location.Label} route ends on its trigger");
                Check(route.TrianglePath.All(id => !planner.IsUnwantedEntrance(id, location.NativeEntranceExemptions, start.Id)),
                    $"the route to {location.Label} does not leave the snowfield some other way");
                var last = map.Triangles[route.TargetTriangleId];
                var arrival = state with { X = last.Centroid.X, Y = last.Centroid.Y, Z = last.Centroid.Z,
                    TerrainId = last.TerrainId, TerrainScriptId = last.TerrainScriptId };
                Check(location.HasArrived(arrival, last.Id), $"stepping onto {location.Label} is arriving there");
                Check(!location.HasArrived(arrival with { TerrainScriptId = 0 }, last.Id),
                    $"{location.Label} needs the native terrain script under the party, not just the triangle");
                routes++;
            }
        }

        Console.WriteLine($"Glacier snowfield: {expected.Length} native exits offered; {routes} routes reach them without crossing another.");
    }

    /// <summary>
    /// The terrain handlers of a world event file that end in EnterFieldScene, as
    /// tools/Generate-WorldMapLocationTriggers.ps1 reads them, with the corridor state
    /// (savemap bank 1 byte 184) the handler writes first, if any.
    /// </summary>
    private static List<(int MeshX, int MeshZ, int Script, int Location, int? Corridor)> ReadEnterFieldHandlers(byte[] events)
    {
        var entries = new List<(int Id, int Offset)>();
        for (var index = 0; index < 256; index++)
        {
            var id = BitConverter.ToUInt16(events, index * 4);
            var address = BitConverter.ToUInt16(events, index * 4 + 2);
            if (id != 0xFFFF && address != 0xFFFF) entries.Add((id, 0x400 + address * 2));
        }

        var starts = entries.Select(e => e.Offset).Distinct().Order().ToArray();
        var handlers = new List<(int, int, int, int, int?)>();
        foreach (var (id, offset) in entries.Where(e => (e.Id & 0xC000) == 0x8000))
        {
            var end = starts.FirstOrDefault(s => s > offset, events.Length);
            var history = new List<(int Opcode, int Argument)>();
            for (var at = offset; at <= end - 2;)
            {
                var opcode = BitConverter.ToUInt16(events, at);
                var words = opcode is > 0x100 and < 0x200 or 0x200 or 0x201 ? 2 : 1;
                var argument = words == 2 ? BitConverter.ToUInt16(events, at + 2) : -1;
                if (opcode == 0x318)
                {
                    Check(history.Count >= 2 && history[^1].Opcode == 0x110 && history[^2].Opcode == 0x110,
                        "EnterFieldScene takes two literal operands");
                    int? corridor = null;
                    for (var index = 2; index < history.Count; index++)
                    {
                        if (history[index].Opcode == 0xE0 && history[index - 1].Opcode == 0x110 &&
                            history[index - 2] == (0x118, 0xB8))
                        {
                            corridor = history[index - 1].Argument;
                        }
                    }

                    var mesh = (id & 0x3FF0) >> 4;
                    handlers.Add((mesh % 36, mesh / 36, (id & 0xF) + 3, history[^2].Argument, corridor));
                }

                history.Add((opcode, argument));
                at += words * 2;
            }
        }

        return handlers;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Glacier snowfield: " + message);
    }
}
