using Ff7.Accessibility.Reloaded;

/// <summary>
/// Reports where native ground type 7 - the Midgar Zolom swamp - actually lies
/// on the world map, so an area warning can be gated on the terrain itself
/// rather than on hand-drawn coordinate bounds.
///
/// Run with <c>--zolom-marsh-extent</c>.
/// </summary>
internal static class ZolomMarshExtentProbe
{
    internal static void Run()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            @"C:\Games\Final Fantasy VII\workingdir";
        var map = WorldMapDataLoader.Load(
            Path.Combine(dataRoot, "data", "wm", "WM0.MAP"),
            0,
            0);

        var swamp = new List<int>();
        for (var index = 0; index < map.Triangles.Count; index++)
        {
            if (map.Triangles[index].TerrainId == 7)
            {
                swamp.Add(index);
            }
        }

        Console.WriteLine($"total triangles: {map.Triangles.Count}");
        Console.WriteLine($"ground type 7 triangles: {swamp.Count}");
        if (swamp.Count == 0)
        {
            return;
        }

        long minX = long.MaxValue, maxX = long.MinValue;
        long minZ = long.MaxValue, maxZ = long.MinValue;
        foreach (var index in swamp)
        {
            foreach (var vertex in Vertices(map, index))
            {
                minX = Math.Min(minX, vertex.X);
                maxX = Math.Max(maxX, vertex.X);
                minZ = Math.Min(minZ, vertex.Z);
                maxZ = Math.Max(maxZ, vertex.Z);
            }
        }

        Console.WriteLine(
            $"extent: X 0x{minX:X} .. 0x{maxX:X}   Z 0x{minZ:X} .. 0x{maxZ:X}");
        Console.WriteLine(
            $"        X {minX} .. {maxX}   Z {minZ} .. {maxZ}");

        // Cluster by simple flood fill over neighbours to see whether ground type
        // 7 forms one region or several scattered ones.
        var remaining = new HashSet<int>(swamp);
        var clusters = 0;
        while (remaining.Count > 0)
        {
            var seed = remaining.First();
            var pending = new Queue<int>();
            pending.Enqueue(seed);
            remaining.Remove(seed);
            var size = 0;
            long clusterMinX = long.MaxValue, clusterMaxX = long.MinValue;
            long clusterMinZ = long.MaxValue, clusterMaxZ = long.MinValue;
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                size++;
                foreach (var vertex in Vertices(map, current))
                {
                    clusterMinX = Math.Min(clusterMinX, vertex.X);
                    clusterMaxX = Math.Max(clusterMaxX, vertex.X);
                    clusterMinZ = Math.Min(clusterMinZ, vertex.Z);
                    clusterMaxZ = Math.Max(clusterMaxZ, vertex.Z);
                }

                foreach (var neighbour in map.Triangles[current].Neighbors)
                {
                    if (neighbour >= 0 && remaining.Remove(neighbour))
                    {
                        pending.Enqueue(neighbour);
                    }
                }
            }

            clusters++;
            Console.WriteLine(
                $"  cluster {clusters}: {size} triangles, " +
                $"X 0x{clusterMinX:X}..0x{clusterMaxX:X}  Z 0x{clusterMinZ:X}..0x{clusterMaxZ:X}");
        }

        Console.WriteLine($"clusters of ground type 7: {clusters}");
    }

    private static IEnumerable<(long X, long Z)> Vertices(WorldMapData map, int triangleIndex)
    {
        var triangle = map.Triangles[triangleIndex];
        yield return (triangle.Vertex0.X, triangle.Vertex0.Z);
        yield return (triangle.Vertex1.X, triangle.Vertex1.Z);
        yield return (triangle.Vertex2.X, triangle.Vertex2.Z);
    }
}
