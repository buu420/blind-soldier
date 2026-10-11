using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapChocoboNavigationTests
{
    // Literal native guest addresses and masks, independently recovered from FUN_0074CECA.
    private const int ColorAddress = 0xDE6B54;
    private const int MaskTableAddress = 0x969A40;
    private const int Player = 0x123000;
    private static readonly uint[] NativeMasks = [0x321B6F83, 0x321B6F87, 0x321B6FF3, 0x325B7FF7, 0x375B7FFF];

    internal static void Run()
    {
        List<Exception> failures = [];
        foreach (var test in new Action[] { RoutesFollowNativeMountedAbilities, ColorChangesDoNotReuseOtherBirdsComponents,
            MissingOrInvalidCapabilitiesDoNotGrantWater, TornNativeCapabilitiesAreRejected, QuicksandFollowsTheNativeScriptFlag,
            ChangingBirdStopsAnOceanRoute, EntranceCuesUseTheCurrentBirdsComponent })
        {
            try { test(); } catch (Exception e) { failures.Add(e); }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
        Console.WriteLine("chocobo native capability and routing tests passed.");
    }

    private static void RoutesFollowNativeMountedAbilities()
    {
        // A wrong native mask, or leaving any routing caller on the model-only overload,
        // makes a legitimate green/blue/black/gold crossing unavailable.
        (int Color, int Terrain, bool Reachable)[] cases =
        [
            (0, 2, false), (0, 6, false), (0, 3, false),
            (1, 2, true), (1, 6, false), (1, 3, false),
            (2, 2, false), (2, 4, true), (2, 5, true), (2, 6, true), (2, 3, false),
            (3, 2, true), (3, 6, true), (3, 12, true), (3, 22, true), (3, 3, false),
            (4, 2, true), (4, 3, true), (4, 6, true), (4, 12, true), (4, 24, true), (4, 26, true),
            (4, 27, false), (4, 30, false), (4, 31, false)
        ];
        foreach (var (color, terrain, reachable) in cases)
        {
            var (map, target) = Crossing(terrain);
            var planner = new WorldMapRoutePlanner(map);
            var state = ReadBird(color);
            Check(planner.CanReach(state, target) == reachable,
                $"native color {color} terrain {terrain} reachability expected {reachable}: {planner.LastDiagnostic}");
            Check(planner.TryBuildRoute(state, target, out _) == reachable,
                $"native color {color} terrain {terrain} route expected {reachable}: {planner.LastDiagnostic}");
        }
    }

    private static void ColorChangesDoNotReuseOtherBirdsComponents()
    {
        var (map, target) = Crossing(3);
        var planner = new WorldMapRoutePlanner(map);
        foreach (var color in new[] { 0, 4, 2, 4, 1 })
            Check(planner.CanReach(ReadBird(color), target) == (color == 4),
                $"model 19 color {color} must use its own cached connectivity: {planner.LastDiagnostic}");
    }

    private static void MissingOrInvalidCapabilitiesDoNotGrantWater()
    {
        var (map, target) = Crossing(3);
        foreach (var color in new[] { -1, 5, int.MaxValue })
        {
            var memory = BirdMemory(color);
            var read = new WorldMapStateReader(memory).Read();
            Check(read.IsUsable, "an unavailable optional bird profile preserves basic world state");
            Check(!new WorldMapRoutePlanner(map).CanReach(read.State, target),
                $"unreadable or invalid native color {color} cannot grant ocean access");
        }
        var missing = BirdMemory(4);
        missing.Bytes.Remove(MaskTableAddress + 16);
        var unavailable = new WorldMapStateReader(missing).Read();
        Check(unavailable.IsUsable && !new WorldMapRoutePlanner(map).CanReach(unavailable.State, target),
            "a missing native mask cannot grant gold-bird ocean access");
        var caught = new WorldMapStateReader(BirdMemory(4, model: 4)).Read();
        Check(caught.IsUsable && !new WorldMapRoutePlanner(map).CanReach(caught.State, target),
            "a caught model 4 never inherits a previous bred bird's color");
    }

    private static void TornNativeCapabilitiesAreRejected()
    {
        foreach (var address in new[] { ColorAddress, MaskTableAddress + 16 })
        {
            var memory = BirdMemory(4);
            memory.TearingAddress = address;
            var read = new WorldMapStateReader(memory).Read();
            Check(!read.IsUsable, $"torn native chocobo observation at {address:X8} must be rejected");
        }
    }

    private static void QuicksandFollowsTheNativeScriptFlag()
    {
        var (map, target) = Crossing(28);
        var planner = new WorldMapRoutePlanner(map);
        var memory = BirdMemory(4);
        memory.Put(0xE3A7E0, 0x140000); // native world-event bank 0 pointer
        foreach (var flag in new byte[] { 0, 0x40, 0 })
        {
            memory.PutByte(0x140000 + (0x03F6 >> 3), flag);
            var read = new WorldMapStateReader(memory).Read();
            Check(read.IsUsable && planner.CanReach(read.State, target) == (flag != 0),
                "model function 530F's native bank-0 bit 0x03F6 controls the quicksand crossing");
        }
    }

    private static void ChangingBirdStopsAnOceanRoute()
    {
        var (map, target) = Crossing(3);
        var controller = new WorldMapNavigationController(map, new(map), (_, _) => [target]);
        var gold = ReadBird(4);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, gold);
        Check(controller.BeaconEnabled, "the gold-bird ocean route starts");
        controller.Observe(ReadBird(0), automaticWalkActive: true);
        Check(!controller.BeaconEnabled && !controller.TryResolveAutomaticInput(ReadBird(0), out _),
            "changing capabilities with model 19 unchanged releases ocean-route steering");
    }

    private static void EntranceCuesUseTheCurrentBirdsComponent()
    {
        var (map, target) = Crossing(2);
        target = target with { NativeLocationArrivals = [new(1, 0, 0, 7, 800, 0, 800)] };
        var cues = new WorldMapEntranceProximityCueTracker(map, new(map), [target], 1, 1500, TimeSpan.Zero);
        foreach (var color in new[] { 0, 1, 0, 4 })
            Check((cues.Update(ReadBird(color), DateTime.UtcNow) is not null) == (color is 1 or 4),
                $"native mountain entrance cues update when the same model switches to color {color}");
    }

    internal static void RunWithInstalledGameData()
    {
        var data = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("Chocobo navigation requires installed game and source roots.");
        var map = WorldMapDataLoader.Load(Path.Combine(data, "data", "wm", "WM0.MAP"), 0, 4);
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(source, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(source, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
        // Last mounted native position in the newly supplied log: the bird is on shallow water.
        var state = ReadBird(4, 236632, -50, 114494, 6, 1) with { GameMoment = 1580, WorldProgress = 4 };
        foreach (var label in new[] { "Materia Cave (Knights of the Round Materia)", "Materia Cave (Mime Materia)",
            "Materia Cave (HP<->MP Materia)", "Materia Cave (Quadra Magic)", "Chocobo Farm" })
        {
            var target = catalog.ReadTargets(WorldMapNavigationCategory.Locations, state, []).SingleOrDefault(t => t.Label == label);
            Check(target is not null, $"gold chocobo offers {label} from the logged water position");
            Check(planner.TryBuildRoute(state, target!, out var route), $"gold chocobo routes to {label}: {planner.LastDiagnostic}");
            Check(target!.ArrivalTriangleIds.Contains(route.TargetTriangleId), $"{label} ends on its native entrance");
            Check(route.TrianglePath.All(id => (NativeMasks[4] & (1u << map.Triangles[id].TerrainId)) != 0),
                $"{label} never crosses ground rejected by the native gold mask");
            var controller = new WorldMapNavigationController(map, planner, (_, _) => [target!]);
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, state);
            Check(controller.BeaconEnabled && controller.TryResolveAutomaticInput(state, out var input) && input != FieldNavigationInput.None,
                $"{label} produces actual automatic steering while mounted: {controller.LastDiagnostic}");
            if (label.Contains("Materia Cave", StringComparison.Ordinal))
            {
                var arrival = target.NativeLocationArrivals.Single(a => a.TriangleId == route.TargetTriangleId);
                var parked = state with { X = arrival.X, Y = arrival.Y, Z = arrival.Z,
                    TerrainId = map.Triangles[arrival.TriangleId].TerrainId, TerrainScriptId = arrival.TerrainScriptId };
                var output = controller.Observe(parked, automaticWalkActive: true);
                Check(controller.BeaconEnabled && output?.Speech?.Contains("leave the vehicle", StringComparison.OrdinalIgnoreCase) == true,
                    $"{label} asks for normal dismount instead of announcing a false arrival: {output?.Speech}");
                Check(!controller.TryResolveAutomaticInput(parked, out _), $"{label} holds still for the player to dismount");
                var onFoot = controller.Observe(parked with { PlayerModelId = 0 }, automaticWalkActive: true);
                Check(onFoot?.Speech?.Contains("Arrived", StringComparison.OrdinalIgnoreCase) == true,
                    $"{label} can arrive after the native player model is on foot: {onFoot?.Speech}");
            }
        }
        Check(catalog.ReadTargets(WorldMapNavigationCategory.Regions, state, []).Count > 0,
            "gold-bird regions remain available while standing on water");
        ReplaysGoldBirdCaveJourneys(map, catalog);
        ReplaysMountedWutaiJourneys(map, catalog);
        Console.WriteLine("gold chocobo installed-map cave and destination navigation tests passed.");
    }

    private static void ReplaysMountedWutaiJourneys(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        var target = catalog.Locations.Single(t => t.Label == "Wutai");
        foreach (var model in new[] { 4, 19 })
        foreach (var color in model == 4 ? new[] { 0 } : Enumerable.Range(0, NativeMasks.Length))
        foreach (var camera in new[] { 0, 512 })
        {
            var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
            var controller = new WorldMapNavigationController(map, planner,
                (_, category) => category == WorldMapNavigationCategory.Locations ? [target] : []);
            // A native logged approach through Wutai's pass and bridges. The replay's
            // 60-unit mounted movement and 200-unit contact are independent of production.
            var mask = NativeMasks[color] & ~(1u << 28);
            var walker = new WorldMapWutaiNavigationTests.NativeWalker(map, 52227, 1900, 156981, camera,
                model, mask, color);
            var state = walker.Snapshot() with { WorldProgress = 4, GameMoment = 1580 };
            var label = $"Wutai model {model} color {color} camera {camera}";
            Check(planner.TryBuildRoute(state, target, out var route), $"{label}: route available: {planner.LastDiagnostic}");
            Check(route.TrianglePath.All(id => (mask & (1u << map.Triangles[id].TerrainId)) != 0),
                $"{label}: route stays on the bird's native terrain");
            var now = new DateTime(2026, 10, 11, 2, 29, 26, DateTimeKind.Utc);
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
            var parked = false;
            for (var frame = 0; frame < 16000; frame++)
            {
                var output = controller.Observe(state, now.AddMilliseconds(frame * 33), automaticWalkActive: true);
                if (output?.Speech?.Contains("leave the vehicle", StringComparison.OrdinalIgnoreCase) == true)
                { parked = true; break; }
                Check(output?.StopAutoWalk != true,
                    $"{label}: steering stopped at {state.X},{state.Y},{state.Z}: {output?.Speech}");
                walker.Frame(controller.TryResolveAutomaticInput(state, out var input) ? input : FieldNavigationInput.None);
                state = walker.Snapshot() with { WorldProgress = 4, GameMoment = 1580 };
            }
            Check(parked, $"{label}: reaches its native entrance for normal dismount: {controller.LastDiagnostic}");
            Check(controller.BeaconEnabled && !controller.TryResolveAutomaticInput(state, out _),
                $"{label}: retains navigation while holding for the player's dismount button");
        }
        Console.WriteLine("mounted native-footprint Wutai journeys passed (12 caught/bred color/camera replays).");
    }

    private static void ReplaysGoldBirdCaveJourneys(WorldMapData map, WorldMapTargetCatalog catalog)
    {
        foreach (var label in new[] { "Materia Cave (Knights of the Round Materia)", "Materia Cave (Mime Materia)" })
        foreach (var camera in new[] { 0, 512, 1776 })
        {
            var target = catalog.Locations.Single(t => t.Label == label);
            var planner = new WorldMapRoutePlanner(map) { EntranceTriangleIds = catalog.EntranceTriangleIds };
            var controller = new WorldMapNavigationController(map, planner, (_, category) => category == WorldMapNavigationCategory.Locations ? [target] : []);
            var walker = new WorldMapWutaiNavigationTests.NativeWalker(map, 236632, -50, 114494, camera,
                19, NativeMasks[4] & ~(1u << 28), 4);
            var state = walker.Snapshot() with { WorldProgress = 4, GameMoment = 1580 };
            var now = new DateTime(2026, 10, 11, 2, 29, 26, DateTimeKind.Utc);
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
            var parked = false;
            for (var frame = 0; frame < 16000; frame++)
            {
                var output = controller.Observe(state, now.AddMilliseconds(frame * 33), automaticWalkActive: true);
                if (output?.Speech?.Contains("leave the vehicle", StringComparison.OrdinalIgnoreCase) == true)
                { parked = true; break; }
                Check(output?.StopAutoWalk != true, $"{label} camera {camera}: steering stopped at {state.X},{state.Y},{state.Z}: {output?.Speech}");
                walker.Frame(controller.TryResolveAutomaticInput(state, out var input) ? input : FieldNavigationInput.None);
                state = walker.Snapshot() with { WorldProgress = 4, GameMoment = 1580 };
            }
            Check(parked, $"{label} camera {camera}: gold bird reaches the native cave and holds for dismount at {state.X},{state.Y},{state.Z}: {controller.LastDiagnostic}");
        }
        Console.WriteLine("gold chocobo complete native-footprint journeys passed (6 camera/cave replays).");
    }

    private static (WorldMapData, WorldMapNavigationTarget) Crossing(int terrain)
    {
        WorldMapTriangle[] triangles =
        [
            new(0, 0, 0, 0, 0, 0, new(0, 0, 0), new(1000, 0, 0), new(0, 0, 1000), 0, 0, 0, 0, false, [1]),
            new(1, 0, 0, 0, 0, 1, new(1000, 0, 0), new(1000, 0, 1000), new(0, 0, 1000), terrain, 0, 0, 0, false, [0])
        ];
        return (new(0, 0, 9, 7, 63, triangles, [], "native chocobo fixture"),
            new(WorldMapNavigationCategory.Locations, WorldMapTargetKind.Location, "Across terrain", 800, 0, 800, 1, 0, "across", new HashSet<int> { 1 }));
    }

    private static WorldMapStateSnapshot ReadBird(int color, int x = 100, int y = 0, int z = 100, int terrain = 0, int script = 0)
    {
        var memory = BirdMemory(color);
        memory.Put(Player + 0x0C, x); memory.Put(Player + 0x10, y); memory.Put(Player + 0x14, z);
        memory.PutShort(Player + 0x4A, (short)(terrain | (script << 5)));
        var read = new WorldMapStateReader(memory).Read();
        Check(read.IsUsable, "native bird frame readable: " + read.Diagnostic);
        return read.State;
    }

    private static BirdAddressSpace BirdMemory(int color, byte model = 19)
    {
        var m = new BirdAddressSpace();
        m.PutByte(WorldMapStateReader.AddressCurrentModule, 3);
        m.Put(WorldMapStateReader.AddressWorldMapType, 0); m.Put(WorldMapStateReader.AddressWorldProgress, 4);
        m.PutShort(WorldMapStateReader.AddressGameMoment, 1580); m.Put(WorldMapStateReader.AddressWorldPlayerEntityPointer, Player);
        m.Put(WorldMapStateReader.AddressWorldCameraFront, 0);
        m.Put(Player + 0x04, 0); m.Put(Player + 0x0C, 100); m.Put(Player + 0x10, 0); m.Put(Player + 0x14, 100);
        m.PutShort(Player + 0x40, 0); m.PutShort(Player + 0x4A, 0); m.PutShort(Player + 0x4C, 0);
        m.PutByte(Player + 0x50, model); m.PutByte(Player + 0x55, 60);
        m.Put(ColorAddress, color);
        for (var i = 0; i < NativeMasks.Length; i++) m.Put(MaskTableAddress + i * 4, unchecked((int)NativeMasks[i]));
        return m;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }

    private sealed class BirdAddressSpace : ILegacyAddressSpace
    {
        internal readonly Dictionary<int, byte> Bytes = [];
        internal int TearingAddress;
        private int reads;
        internal void Put(int address, int value) => Write(address, BitConverter.GetBytes(value));
        internal void PutShort(int address, short value) => Write(address, BitConverter.GetBytes(value));
        internal void PutByte(int address, byte value) => Bytes[address] = value;
        private void Write(int address, byte[] value) { for (var i = 0; i < value.Length; i++) Bytes[address + i] = value[i]; }
        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
                if (!Bytes.TryGetValue(checked((int)address + i), out destination[i])) { destination.Clear(); return false; }
            if (address == TearingAddress && ++reads >= 2) destination[0] ^= 1;
            return true;
        }
    }
}
