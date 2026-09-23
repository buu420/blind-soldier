using System.Runtime.CompilerServices;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Where the party can stand and be touching a parked vehicle.
///
/// <para>The native interaction is a collision, not a proximity radius: FUN_00762A21
/// reports contact, FUN_00762993 records it at <c>current + 4</c>, and FUN_0076420A
/// dispatches the model's function 3 or 4 from that slot on Confirm. So an arrival for a
/// vehicle cannot be a triangle, a ring of neighbours or a centroid - it is the ground the
/// mask reaches. The Tiny Bronco moored in the river proves it: the one-ring rule offered
/// the far bank, which holds no position inside the boat's mask at all.</para>
/// </summary>
public static class WorldMapVehicleShoreApproach
{
    /// <summary>
    /// How finely a candidate triangle is sampled inside a contact cell. A cell is 256
    /// units square, so eight samples across it; the search is for somewhere to stand,
    /// not for the exact boundary of one.
    /// </summary>
    public const int SampleStep = 32;

    private static readonly ConditionalWeakTable<WorldMapData, Dictionary<(int MeshX, int MeshZ), List<int>>>
        MeshIndexes = new();

    /// <summary>
    /// Every triangle the party can stand on that holds at least one point in native
    /// contact with this vehicle, mapped to the contact point nearest the vehicle.
    ///
    /// <para>Empty when either model has no mask read out of the executable. Guessing a
    /// footprint for the Highwind or the submarine would be inventing an arrival, so those
    /// keep the behaviour they already had.</para>
    /// </summary>
    public static IReadOnlyDictionary<int, WorldMapVertex> FindContactPoints(
        WorldMapData map,
        int playerModelId,
        int vehicleModelId,
        int vehicleX,
        int vehicleZ)
    {
        ArgumentNullException.ThrowIfNull(map);
        var cells = WorldMapVehicleObstacles.ContactCells(playerModelId, vehicleModelId);
        if (cells.Count == 0)
        {
            return new Dictionary<int, WorldMapVertex>();
        }

        var contacts = new Dictionary<int, WorldMapVertex>();
        var best = new Dictionary<int, long>();
        foreach (var triangle in CandidateTriangles(map, vehicleX, vehicleZ))
        {
            if (!WorldMapTerrainPassability.CanTraverse(playerModelId, map.WorldMapType, triangle.TerrainId) ||
                !IsWithinReach(map, triangle, vehicleX, vehicleZ))
            {
                continue;
            }

            foreach (var (minX, minZ) in cells)
            {
                for (var offsetZ = minZ; offsetZ < minZ + WorldMapVehicleObstacles.NativeCellSize; offsetZ += SampleStep)
                {
                    for (var offsetX = minX; offsetX < minX + WorldMapVehicleObstacles.NativeCellSize; offsetX += SampleStep)
                    {
                        // The sample is expressed from the vehicle outward, so the wrap is
                        // applied once, here, and the native test is then asked about the
                        // absolute position the party would occupy.
                        var x = Normalize(vehicleX + offsetX, map.WrapWidth);
                        var z = Normalize(vehicleZ + offsetZ, map.WrapHeight);
                        if (!WorldMapVehicleObstacles.Blocks(
                                playerModelId, x, z, vehicleModelId, vehicleX, vehicleZ,
                                map.WrapWidth, map.WrapHeight) ||
                            !TryResolveHeight(triangle, x, z, out var y))
                        {
                            continue;
                        }

                        var reach = (long)Math.Abs(offsetX) + Math.Abs(offsetZ);
                        if (best.TryGetValue(triangle.Id, out var existing) && existing <= reach)
                        {
                            continue;
                        }

                        best[triangle.Id] = reach;
                        contacts[triangle.Id] = new WorldMapVertex(x, y, z);
                    }
                }
            }
        }

        return contacts;
    }

    /// <summary>
    /// Whether the party is standing in native contact with this vehicle right now. This
    /// is the same test the game applies before it will dispatch the boarding function, so
    /// it is what an arrival at a vehicle has to mean.
    /// </summary>
    public static bool IsInNativeContact(
        WorldMapData map,
        WorldMapStateSnapshot state,
        int vehicleModelId,
        int vehicleX,
        int vehicleZ)
    {
        ArgumentNullException.ThrowIfNull(map);
        return WorldMapVehicleObstacles.Blocks(
            state.PlayerModelId,
            state.X,
            state.Z,
            vehicleModelId,
            vehicleX,
            vehicleZ,
            map.WrapWidth,
            map.WrapHeight);
    }

    /// <summary>
    /// Whether any part of this triangle is inside the native reach. A mesh cell holds
    /// well over a hundred triangles and the reach is an eighth of one, so without this
    /// nearly every candidate would be sampled for nothing.
    /// </summary>
    private static bool IsWithinReach(WorldMapData map, WorldMapTriangle triangle, int vehicleX, int vehicleZ)
    {
        var minX = int.MaxValue;
        var maxX = int.MinValue;
        var minZ = int.MaxValue;
        var maxZ = int.MinValue;
        foreach (var vertex in new[] { triangle.Vertex0, triangle.Vertex1, triangle.Vertex2 })
        {
            var offsetX = WorldMapTargetCatalog.WrappedDelta(vehicleX, vertex.X, map.WrapWidth);
            var offsetZ = WorldMapTargetCatalog.WrappedDelta(vehicleZ, vertex.Z, map.WrapHeight);
            minX = Math.Min(minX, offsetX);
            maxX = Math.Max(maxX, offsetX);
            minZ = Math.Min(minZ, offsetZ);
            maxZ = Math.Max(maxZ, offsetZ);
        }

        return minX <= WorldMapVehicleObstacles.NativeReach &&
               maxX >= -WorldMapVehicleObstacles.NativeReach &&
               minZ <= WorldMapVehicleObstacles.NativeReach &&
               maxZ >= -WorldMapVehicleObstacles.NativeReach;
    }

    /// <summary>
    /// The triangles in the mesh cells the native reach can cover. A cell is 0x2000 units
    /// and the reach is 1024, so this is at most a two by two neighbourhood.
    /// </summary>
    private static IEnumerable<WorldMapTriangle> CandidateTriangles(WorldMapData map, int x, int z)
    {
        var index = MeshIndexes.GetValue(map, BuildMeshIndex);
        var seen = new HashSet<int>();
        foreach (var cornerX in new[] { x - WorldMapVehicleObstacles.NativeReach, x + WorldMapVehicleObstacles.NativeReach })
        {
            foreach (var cornerZ in new[] { z - WorldMapVehicleObstacles.NativeReach, z + WorldMapVehicleObstacles.NativeReach })
            {
                var meshX = Math.Clamp(
                    Normalize(cornerX, map.WrapWidth) / WorldMapDataLoader.MeshSize, 0, map.MeshGridWidth - 1);
                var meshZ = Math.Clamp(
                    Normalize(cornerZ, map.WrapHeight) / WorldMapDataLoader.MeshSize, 0, map.MeshGridHeight - 1);
                if (!seen.Add(meshX * map.MeshGridHeight + meshZ) ||
                    !index.TryGetValue((meshX, meshZ), out var triangles))
                {
                    continue;
                }

                foreach (var id in triangles)
                {
                    yield return map.Triangles[id];
                }
            }
        }
    }

    private static Dictionary<(int MeshX, int MeshZ), List<int>> BuildMeshIndex(WorldMapData map)
    {
        var index = new Dictionary<(int, int), List<int>>();
        foreach (var triangle in map.Triangles)
        {
            if (!index.TryGetValue((triangle.MeshX, triangle.MeshZ), out var bucket))
            {
                bucket = [];
                index[(triangle.MeshX, triangle.MeshZ)] = bucket;
            }

            bucket.Add(triangle.Id);
        }

        return index;
    }

    /// <summary>
    /// Whether this point is inside the triangle, and the height of the native surface
    /// there. Strictly inside: a point on an edge belongs to the neighbour just as much,
    /// and the party has to be standing somewhere definite.
    /// </summary>
    private static bool TryResolveHeight(WorldMapTriangle triangle, int x, int z, out int y)
    {
        y = 0;
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
        if (Math.Abs(denominator) < 1e-9)
        {
            return false;
        }

        var weightA = ((b.Z - c.Z) * (double)(x - c.X) + (c.X - b.X) * (double)(z - c.Z)) / denominator;
        var weightB = ((c.Z - a.Z) * (double)(x - c.X) + (a.X - c.X) * (double)(z - c.Z)) / denominator;
        var weightC = 1 - weightA - weightB;
        if (weightA <= 0.001 || weightB <= 0.001 || weightC <= 0.001)
        {
            return false;
        }

        y = (int)Math.Round(weightA * a.Y + weightB * b.Y + weightC * c.Y);
        return true;
    }

    private static int Normalize(int value, int wrap)
    {
        if (wrap <= 0)
        {
            return value;
        }

        var normalized = value % wrap;
        return normalized < 0 ? normalized + wrap : normalized;
    }
}
