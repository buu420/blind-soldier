using Ff7.Accessibility.Reloaded;
namespace Ff7.Accessibility.Reloaded.Tests;

// Independent movement oracle over installed native triangles. This does not simulate
// the engine's footprint/slide logic; it catches rejected vehicle steps, loops and false arrivals.
internal static class WorldMapVehicleApproachReplayTests
{
    internal static void Run()
    {
        var (map, catalog) = Load(); var target = catalog.Locations.Single(t => t.Label == "Cosmo Canyon");
        foreach (var start in new[] { (87577, 1443, 170926, 0, 19), (86999, 1509, 170670, 0, 19) })
            foreach (var sampleDistance in new[] { 60, 120 })
                foreach (var camera in new[] { 0, 344, 1256, 3550 })
                {
                    var direction = -(camera / 16); if (direction < -128) direction += 256;
                    var state = new WorldMapStateSnapshot(3, 0, 0, 469, start.Item1, start.Item2, start.Item3, 0, 0, start.Item5, 6, start.Item4, 30, camera, new FieldNavigationControlTransform(direction));
                    var planner = new WorldMapRoutePlanner(map); var controller = new WorldMapNavigationController(map, planner, (_, _) => [target], entityProvider: () => [new WorldMapEntitySnapshot(0x1000, 0, false, 87271, 1499, 170546, 16, 6, 6, 0)]);
                    planner.TryResolvePlayerTriangle(state, out var triangle);
                    var now = new DateTime(2026, 9, 20, 23, 0, 0, DateTimeKind.Utc);
                    controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
                    var arrived = false;
                    var recent = new Queue<string>();
                    for (var frame = 0; frame < 200; frame++)
                    {
                        var output = controller.Observe(state, now.AddMilliseconds(frame * 80), automaticWalkActive: true);
                        if (output?.StopAutoWalk == true || !controller.BeaconEnabled) { arrived = !controller.BeaconEnabled && target.HasArrived(state, triangle); break; }
                        if (!controller.TryResolveAutomaticInput(state, out var input)) continue;
                        var (dx, dz) = NativeStep(input, camera, sampleDistance);
                        recent.Enqueue($"{state.X},{state.Y},{state.Z} tri {triangle} terrain {state.TerrainId} input {input} next {controller.Probe.Route!.Waypoints[controller.Probe.WaypointIndex]}"); if (recent.Count > 6) recent.Dequeue();
                        var before = state; var beforeTri = triangle;
                        state = MoveOnRawNativeTriangles(map, state, ref triangle, dx, dz);
                        if (state.PlayerModelId == 0)
                        {
                            int diffX = 87271 - state.X, diffZ = 170546 - state.Z;
                            int col = (diffX + 1024) >> 8, row = (diffZ + 1024) >> 8;
                            byte[] cloud = [0, 0, 0, 24, 24, 0, 0, 0], buggy = [0, 0, 24, 60, 60, 24, 0, 0];
                            bool collision = col >= 0 && col < 8 && row >= 0 && row < 8 && (((cloud[row] >> col) & 1) != 0 || ((buggy[7 - row] >> (7 - col)) & 1) != 0);
                            if (collision && Math.Abs(diffX) + Math.Abs(diffZ) < Math.Abs(87271 - before.X) + Math.Abs(170546 - before.Z)) { state = before; triangle = beforeTri; }
                        }

                    }
                    Equal(true, arrived, $"parked Buggy approach {start}, camera {camera}, step {sampleDistance}: {string.Join(";", recent)}");
                }
    }
    private static (double X, double Z) NativeStep(FieldNavigationInput input, int camera, int distance)
    {
        var x = input is FieldNavigationInput.Left or FieldNavigationInput.UpLeft or FieldNavigationInput.DownLeft ? -1 :
            input is FieldNavigationInput.Right or FieldNavigationInput.UpRight or FieldNavigationInput.DownRight ? 1 : 0;
        var z = input is FieldNavigationInput.Up or FieldNavigationInput.UpLeft or FieldNavigationInput.UpRight ? -1 :
            input is FieldNavigationInput.Down or FieldNavigationInput.DownLeft or FieldNavigationInput.DownRight ? 1 : 0;
        // FUN0074EA48 assigns the four native direction axes, scales diagonals
        // by3/4 and then applies the negative camera-front Y rotation. This
        // oracle does not use the production controller transform or formatter.
        var scale = x != 0 && z != 0 ? distance * 0.75d : distance;
        var angle = camera * Math.PI * 2d / 4096d;
        return ((x * Math.Cos(angle) - z * Math.Sin(angle)) * scale,
            (x * Math.Sin(angle) + z * Math.Cos(angle)) * scale);
    }

    private static WorldMapStateSnapshot MoveOnRawNativeTriangles(WorldMapData map,
        WorldMapStateSnapshot state, ref int triangle, double dx, double dz)
    {
        var count = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dz))));
        var startX = state.X;
        var startZ = state.Z;
        var currentX = (double)startX;
        var currentZ = (double)startZ;
        for (var step = 1; step <= count; step++)
        {
            var nextX = Math.Round(startX + dx * step / count);
            var nextZ = Math.Round(startZ + dz * step / count);
            var nextTriangle = ResolveRawNeighbor(map, triangle, currentX, currentZ, nextX, nextZ);
            if (nextTriangle < 0) break;
            triangle = nextTriangle;
            currentX = nextX;
            currentZ = nextZ;
        }
        var native = map.Triangles[triangle];
        // Truncate the accepted movement like native integer world positions.
        var x = (int)currentX;
        var z = (int)currentZ;
        var a = native.Vertex0;
        var b = native.Vertex1;
        var c = native.Vertex2;
        var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
        var wa = ((b.Z - c.Z) * (double)(x - c.X) + (c.X - b.X) * (double)(z - c.Z)) / denominator;
        var wb = ((c.Z - a.Z) * (double)(x - c.X) + (a.X - c.X) * (double)(z - c.Z)) / denominator;
        return state with
        {
            X = x,
            Z = z,
            Y = (int)Math.Round(wa * a.Y + wb * b.Y + (1d - wa - wb) * c.Y),
            TerrainId = native.TerrainId,
            RegionId = native.RegionId & 31,
            TerrainScriptId = native.TerrainScriptId
        };
    }

    private static int ResolveRawNeighbor(WorldMapData map, int initial,
        double fromX, double fromZ, double toX, double toZ)
    {
        var queue = new Queue<int>();
        var visited = new HashSet<int> { initial };
        queue.Enqueue(initial);
        while (queue.TryDequeue(out var id))
        {
            var triangle = map.Triangles[id];
            // Walking grass, dirt, tracks and ordinary ground only. Mountain,
            // ocean and vertical cliff faces remain impassable in this oracle.
            if (triangle.TerrainId is 2 or 3 or 5 or 6 or 12 or 15 or 18 or 22 or 23 or 26 or 31) continue;
            if (Math.Abs(Side(triangle.Vertex0.X, triangle.Vertex0.Z, triangle.Vertex1.X, triangle.Vertex1.Z,
                triangle.Vertex2.X, triangle.Vertex2.Z)) <= 1e-7) continue;
            var sides = triangle.Edges.Select(edge => Side(edge.Start.X, edge.Start.Z,
                edge.End.X, edge.End.Z, toX, toZ)).ToArray();
            if (!sides.Any(side => side < -1e-7) || !sides.Any(side => side > 1e-7)) return id;
            foreach (var neighborId in triangle.Neighbors)
            {
                if (visited.Contains(neighborId)) continue;
                var neighbor = map.Triangles[neighborId];
                var shared = triangle.Edges.FirstOrDefault(edge => neighbor.Edges.Any(other =>
                    edge.Start == other.Start && edge.End == other.End || edge.Start == other.End && edge.End == other.Start));
                if (shared.Start == shared.End || !SegmentsTouch(fromX, fromZ, toX, toZ,
                    shared.Start.X, shared.Start.Z, shared.End.X, shared.End.Z)) continue;
                visited.Add(neighborId);
                queue.Enqueue(neighborId);
            }
        }
        return -1;
    }

    private static bool SegmentsTouch(double ax, double az, double bx, double bz,
        double cx, double cz, double dx, double dz) =>
        Math.Max(ax, bx) >= Math.Min(cx, dx) - 1e-7 && Math.Max(cx, dx) >= Math.Min(ax, bx) - 1e-7 &&
        Math.Max(az, bz) >= Math.Min(cz, dz) - 1e-7 && Math.Max(cz, dz) >= Math.Min(az, bz) - 1e-7 &&
        Side(ax, az, bx, bz, cx, cz) * Side(ax, az, bx, bz, dx, dz) <= 1e-7 &&
        Side(cx, cz, dx, dz, ax, az) * Side(cx, cz, dx, dz, bx, bz) <= 1e-7;

    private static double Side(double ax, double az, double bx, double bz, double px, double pz) =>
        (bx - ax) * (pz - az) - (bz - az) * (px - ax);

    private static (WorldMapData Map, WorldMapTargetCatalog Catalog) Load()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            @"C:\Games\Final Fantasy VII\workingdir";
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
            @"C:\FF7A11Y\accessibility_prototype";
        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 0);
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return (map, catalog);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
    }
}