using System.Reflection.PortableExecutable;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapAncientForestTests
{
    internal static void Run()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (root is null || source is null) return;
        NativeEntrance(root);
        foreach (var stage in new[] { 3, 4 })
        {
            var map = WorldMapDataLoader.Load(Path.Combine(root, "data", "wm", "wm0.map"), 0, stage);
            var catalog = WorldMapTargetCatalog.Load(map,
                Path.Combine(source, "external", "kujata", "field-id-to-world-map-coords.json"),
                Path.Combine(source, "external", "kujata", "wm-field-menu-names.txt"),
                Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
            var forest = catalog.Locations.SingleOrDefault(t => t.Label == "Ancient Forest") ??
                throw new InvalidOperationException("Ancient Forest must have a native model-contact Location, not be omitted");
            Check(!catalog.UnresolvedLocations.Any(t => t.LocationId == 55), "the runtime accounts for the model entrance");
            Check(forest.X == 92960 && forest.Z == 179731, "the entrance uses model25's native placement rather than the old marker");
            Check(forest.NativeTriggerTriangleIds.Count == 0 && !forest.ArrivalTriangleIds.Overlaps(catalog.EntranceTriangleIds),
                "Confirm entry is not an automatic field transition");
            var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
            var cosmo = catalog.Locations.Single(t => t.Label == "Cosmo Canyon");
            WorldMapStateSnapshot Pose(int x, int z, int model = 0)
            {
                Check(WorldMapBroncoLanding.TryFindSurface(map, x, z, out var floor), "a test pose has installed native ground");
                return new(3, 0, stage, 1620, x, floor.Centroid.Y, z, 0, 0,
                    floor.TerrainId, floor.RegionId & 31, model, 30, 0, new(0))
                { TerrainScriptId = floor.TerrainScriptId, NativePlayerEntityPointer = 0x00E3A108 };
            }
            var start = stage == 4 ? Pose(81630, 166401) : Pose(cosmo.X, cosmo.Z);
            var native = new WorldMapEntitySnapshot(0x00123400, 0, false, forest.X, forest.Y, forest.Z,
                map.Triangles[forest.TriangleId].TerrainId, forest.RegionId, 25, 2);
            WorldMapNavigationTarget Live(WorldMapStateSnapshot s) => catalog.ReadTargets(WorldMapNavigationCategory.Locations, s, [native])
                .Single(t => t.Label == "Ancient Forest");
            var live = Live(start);
            Check(live.NativeVehicleContact?.VehicleEntityPointer == native.GuestPointer, "live model25 provides the native contact witness");
            Check(!catalog.ReadTargets(WorldMapNavigationCategory.Locations, start, [native with { Flags = 8 }])
                .Any(t => t.Label == "Ancient Forest"), "a natively hidden model cannot become an entrance");
            Check(!catalog.ReadTargets(WorldMapNavigationCategory.Locations, start, [native with { ModelId = 0, IsPlayer = true }])
                .Any(t => t.Label == "Ancient Forest"), "a complete native pool without the entrance does not invent it");
            Check(planner.TryBuildRoute(start, live, out var route) == (stage == 4),
                $"on foot, stage{stage} follows the actual crater terrain: {planner.LastDiagnostic}");
            if (stage == 4)
            {
                foreach (var model in new[] { 0, 1, 2 })
                {
                    var foot = start with { PlayerModelId = model };
                    Check(planner.TryBuildRoute(foot, live, out route), "every native party leader can reach the entrance");
                    var navigator = Navigator();
                    navigator.HandleAction(FieldNavigationAction.ToggleBeacon, foot);
                    Check(navigator.BeaconEnabled, "the supplied log's missing Locations route can start");
                    var end = route.Waypoints[^1];
                    var contact = Pose(end.X, end.Z, model);
                    Check(live.HasArrived(contact, route.TargetTriangleId), "arrival is inside native contact");
                    Check(!live.HasArrived(contact with { X = forest.X + 1000, Z = forest.Z + 1000 }, route.TargetTriangleId),
                        "an arrival triangle alone cannot falsely claim contact");
                    var reached = navigator.Observe(contact, automaticWalkActive: true);
                    Check(reached?.Speech?.Contains("Press Confirm to enter", StringComparison.Ordinal) == true &&
                        !navigator.BeaconEnabled && !navigator.TryResolveAutomaticInput(contact, out _),
                        "Forest arrival stops and leaves entry to the player's normal Confirm: " + reached?.Speech);
                    Check(!live.HasArrived(contact with { PlayerModelId = 3 }, route.TargetTriangleId), "flying over contact is not entry");
                }
            }
            var flight = Navigator();
            var airborne = start with { PlayerModelId = 3, Y = 2000 };
            var flying = flight.HandleAction(FieldNavigationAction.ToggleBeacon, airborne);
            Check(flight.BeaconEnabled == (stage == 4), $"Highwind only promises a real landing route at stage{stage}: {flying?.Speech}");
            if (stage == 4) Check(flying?.Speech?.Contains("landing", StringComparison.OrdinalIgnoreCase) == true, "flight uses the existing grass landing flow");
            foreach (var (color, mask) in new[] { (3, 0x325B7FF7u), (4, 0x375B7FFFu) })
            {
                var bird = start with { PlayerModelId = 19, HasNativeChocoboTerrainMask = true,
                    NativeChocoboColor = color, NativeChocoboTerrainMask = mask };
                Check(planner.TryBuildRoute(bird, live, out var birdRoute), $"native black/gold terrain can reach stage{stage}: {planner.LastDiagnostic}");
                var navigator = Navigator();
                navigator.HandleAction(FieldNavigationAction.ToggleBeacon, bird);
                var endpoint = birdRoute.Waypoints[^1];
                var mounted = Pose(endpoint.X, endpoint.Z, 19) with { HasNativeChocoboTerrainMask = true,
                    NativeChocoboColor = color, NativeChocoboTerrainMask = mask };
                Check(!live.HasArrived(mounted, birdRoute.TargetTriangleId), "a mounted bird is not an admitted party model");
                var handoff = navigator.Observe(mounted, automaticWalkActive: true);
                Check(handoff?.StopAutoWalk == true && handoff.Value.Speech?.Contains("on foot", StringComparison.Ordinal) == true,
                    "the mounted approach asks for normal dismount instead of claiming arrival: " + handoff?.Speech);
                Check(!navigator.TryResolveAutomaticInput(mounted, out _), "dismount handoff releases automatic movement");
            }
            WorldMapNavigationController Navigator() => new(map, new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds },
                (s, category) => category == WorldMapNavigationCategory.Locations ? [Live(s)] : [], entityProvider: () => [native]);
        }
        Console.WriteLine("Ancient Forest native model entry, stage3/4 terrain, party/chocobo and Highwind route checks passed.");
    }

    private static void NativeEntrance(string root)
    {
        var archives = Directory.GetFiles(Path.Combine(root, "data", "wm"), "world_*.lgp");
        Check(archives.Length > 0, "a licensed native world event archive is present");
        foreach (var path in archives)
        {
            var archive = new LgpArchiveReader(path);
            var hasEvents = archive.TryReadFile("wm0.ev", out var events);
            var hasFields = archive.TryReadFile("field.tbl", out var fields);
            Check(hasEvents && hasFields, "native world scripts exist");
            var entry = Enumerable.Range(0, 256).Single(i => BitConverter.ToUInt16(events, i * 4) == 0x5903);
            var at = 0x400 + 2 * BitConverter.ToUInt16(events, entry * 4 + 2);
            var words = Enumerable.Range(0, 28).Select(i => BitConverter.ToUInt16(events, at + i * 2)).ToArray();
            Check(Enumerable.Range(0, words.Length - 4).Any(i => words.Skip(i).Take(5).SequenceEqual(new ushort[] { 0x110, 55, 0x110, 0, 0x318 })),
                "model25 function3 owns native location55 entry on Confirm");
            Check(BitConverter.ToUInt16(fields, (55 * 2 - 2) * 12 + 6) == 622, "field.tbl enters anfrst_3");
        }
        using var stream = File.OpenRead(Path.Combine(root, "ff7_en.exe"));
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var rva = checked((int)(0x0096DDB0 + 25 * 8 - pe.PEHeaders.PEHeader!.ImageBase));
        var section = pe.PEHeaders.SectionHeaders.Single(s => rva >= s.VirtualAddress && rva < s.VirtualAddress + s.VirtualSize);
        stream.Position = rva - section.VirtualAddress + section.PointerToRawData;
        var mask = new byte[8];
        stream.ReadExactly(mask);
        Check(mask.SequenceEqual(new byte[] { 0, 0, 0x18, 0x3C, 0x3C, 0x18, 0, 0 }), "licensed executable model25 collision mask");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Ancient Forest: " + message); }
}
