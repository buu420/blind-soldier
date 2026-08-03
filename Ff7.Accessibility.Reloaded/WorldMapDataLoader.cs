using System.Buffers.Binary;

namespace Ff7.Accessibility.Reloaded;

public static class WorldMapDataLoader
{
    public const int BlockSize = 0xB800;
    public const int MeshSize = 0x2000;
    private const int MeshesPerBlock = 16;
    private const int TriangleRecordSize = 12;
    private const int VertexRecordSize = 8;
    private const int MaximumTrianglesPerMesh = 4096;
    private const int MaximumVerticesPerMesh = 256;

    private static readonly IReadOnlyList<WorldMapReplacementBlock> OverworldReplacementBlocks =
    [
        // Exact FF7 2013 native rules from FUN_00750f3c. The x64 runtime
        // executes this same x86 guest code through its translated address space.
        new(63, 50, 1, "Huge Materia - North Corel"),
        new(64, 41, 2, "Huge Materia - Fort Condor"),
        new(65, 42, 2, "Huge Materia - Underwater Reactor"),
        new(66, 60, 3, "Meteor impact - Midgar area"),
        new(67, 47, 4, "Diamond Weapon - Midgar approach"),
        new(68, 48, 4, "Ultimate Weapon - crater formation")
    ];

    public static WorldMapData Load(string path, int worldMapType, int worldProgress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        return Parse(bytes, worldMapType, worldProgress, path);
    }

    public static WorldMapData Parse(
        ReadOnlySpan<byte> bytes,
        int worldMapType,
        int worldProgress,
        string sourcePath = "memory")
    {
        var (blockWidth, blockHeight) = ResolveGrid(worldMapType);
        var activeBlockCount = checked(blockWidth * blockHeight);
        if (bytes.Length < checked(activeBlockCount * BlockSize) || bytes.Length % BlockSize != 0)
        {
            throw new InvalidDataException(
                $"World map {worldMapType} has invalid length {bytes.Length}; " +
                $"expected at least {activeBlockCount * BlockSize} bytes in {BlockSize}-byte blocks.");
        }

        var rawBlockCount = bytes.Length / BlockSize;
        var builders = new List<TriangleBuilder>(20_000);
        for (var logicalBlockIndex = 0; logicalBlockIndex < activeBlockCount; logicalBlockIndex++)
        {
            var sourceBlockIndex = ResolveSourceBlockIndex(
                worldMapType,
                worldProgress,
                logicalBlockIndex);
            if (sourceBlockIndex >= rawBlockCount)
            {
                throw new InvalidDataException(
                    $"World map {worldMapType} progress {worldProgress} selects missing " +
                    $"physical block {sourceBlockIndex}; file contains {rawBlockCount} blocks.");
            }

            ParseBlock(
                bytes,
                sourceBlockIndex,
                logicalBlockIndex,
                blockWidth,
                builders);
        }

        var neighbors = BuildAdjacency(
            builders,
            checked(blockWidth * 4 * MeshSize),
            checked(blockHeight * 4 * MeshSize));
        var triangles = new WorldMapTriangle[builders.Count];
        for (var index = 0; index < builders.Count; index++)
        {
            var triangle = builders[index];
            triangles[index] = new WorldMapTriangle(
                index,
                triangle.SourceBlockIndex,
                triangle.MeshX,
                triangle.MeshZ,
                triangle.MeshIndex,
                triangle.TriangleIndex,
                triangle.Vertex0,
                triangle.Vertex1,
                triangle.Vertex2,
                triangle.TerrainId,
                triangle.TextureId,
                triangle.RegionId,
                neighbors[index]);
        }

        return new WorldMapData(
            worldMapType,
            worldProgress,
            blockWidth,
            blockHeight,
            rawBlockCount,
            triangles,
            worldMapType == 0 ? OverworldReplacementBlocks : [],
            sourcePath);
    }

    public static bool IsChocoboTrackTexture(int textureId) =>
        textureId is 233 or 254 or 281;

    public static int ResolveProgressStage(int worldMapType, int worldProgress) =>
        worldMapType == 0
            ? Math.Clamp(worldProgress, 0, 4)
            : 0;

    private static (int Width, int Height) ResolveGrid(int worldMapType) =>
        worldMapType switch
        {
            0 => (9, 7),
            2 => (3, 4),
            3 => (2, 2),
            _ => throw new ArgumentOutOfRangeException(
                nameof(worldMapType),
                worldMapType,
                "FFVII world-map type must be overworld 0, underwater 2, or glacier 3.")
        };

    private static void ParseBlock(
        ReadOnlySpan<byte> file,
        int sourceBlockIndex,
        int logicalBlockIndex,
        int blockGridWidth,
        ICollection<TriangleBuilder> triangles)
    {
        var blockBase = checked(sourceBlockIndex * BlockSize);
        for (var meshIndex = 0; meshIndex < MeshesPerBlock; meshIndex++)
        {
            var offsetAddress = checked(blockBase + meshIndex * sizeof(int));
            var meshOffset = ReadInt32(file, offsetAddress, "mesh offset");
            if (meshOffset == 0)
            {
                continue;
            }

            if (meshOffset < MeshesPerBlock * sizeof(int) || meshOffset > BlockSize - sizeof(int))
            {
                throw new InvalidDataException(
                    $"World block {sourceBlockIndex}, mesh {meshIndex} has invalid offset {meshOffset}.");
            }

            var lengthAddress = checked(blockBase + meshOffset);
            var compressedLength = ReadInt32(file, lengthAddress, "compressed mesh length");
            if (compressedLength <= 0 ||
                compressedLength > BlockSize - meshOffset - sizeof(int) ||
                lengthAddress + sizeof(int) + compressedLength > file.Length)
            {
                throw new InvalidDataException(
                    $"World block {sourceBlockIndex}, mesh {meshIndex} has invalid compressed length {compressedLength}.");
            }

            var compressed = file.Slice(lengthAddress, sizeof(int) + compressedLength);
            var decompressed = Ff7LzsDecoder.DecodeFieldFile(compressed);
            ParseMesh(
                decompressed,
                sourceBlockIndex,
                logicalBlockIndex,
                meshIndex,
                blockGridWidth,
                triangles);
        }
    }

    private static void ParseMesh(
        ReadOnlySpan<byte> mesh,
        int sourceBlockIndex,
        int logicalBlockIndex,
        int meshIndex,
        int blockGridWidth,
        ICollection<TriangleBuilder> output)
    {
        if (mesh.Length < sizeof(short) * 2)
        {
            throw new InvalidDataException($"World block {sourceBlockIndex}, mesh {meshIndex} header is truncated.");
        }

        var triangleCount = BinaryPrimitives.ReadInt16LittleEndian(mesh);
        var vertexCount = BinaryPrimitives.ReadInt16LittleEndian(mesh[sizeof(short)..]);
        if (triangleCount is < 0 or > MaximumTrianglesPerMesh ||
            vertexCount is <= 0 or > MaximumVerticesPerMesh)
        {
            throw new InvalidDataException(
                $"World block {sourceBlockIndex}, mesh {meshIndex} has invalid counts " +
                $"triangles={triangleCount}, vertices={vertexCount}.");
        }

        var trianglesLength = checked(triangleCount * TriangleRecordSize);
        var verticesLength = checked(vertexCount * VertexRecordSize);
        var requiredLength = checked(sizeof(short) * 2 + trianglesLength + verticesLength);
        if (requiredLength > mesh.Length)
        {
            throw new InvalidDataException(
                $"World block {sourceBlockIndex}, mesh {meshIndex} payload is truncated: " +
                $"requires {requiredLength}, has {mesh.Length}.");
        }

        var blockX = logicalBlockIndex % blockGridWidth;
        var blockZ = logicalBlockIndex / blockGridWidth;
        var meshX = blockX * 4 + meshIndex % 4;
        var meshZ = blockZ * 4 + meshIndex / 4;
        var triangleBase = sizeof(short) * 2;
        var vertexBase = triangleBase + trianglesLength;
        var vertices = new WorldMapVertex[vertexCount];
        for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            var offset = vertexBase + vertexIndex * VertexRecordSize;
            var localX = BinaryPrimitives.ReadInt16LittleEndian(mesh[offset..]);
            var localY = BinaryPrimitives.ReadInt16LittleEndian(mesh[(offset + 2)..]);
            var localZ = BinaryPrimitives.ReadInt16LittleEndian(mesh[(offset + 4)..]);
            vertices[vertexIndex] = new WorldMapVertex(
                checked(meshX * MeshSize + localX),
                localY,
                checked(meshZ * MeshSize + localZ));
        }

        for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            var offset = triangleBase + triangleIndex * TriangleRecordSize;
            var first = mesh[offset];
            var second = mesh[offset + 1];
            var third = mesh[offset + 2];
            if (first >= vertexCount || second >= vertexCount || third >= vertexCount)
            {
                throw new InvalidDataException(
                    $"World block {sourceBlockIndex}, mesh {meshIndex}, triangle {triangleIndex} " +
                    "references a vertex outside the mesh.");
            }

            var textureAndRegion = BinaryPrimitives.ReadUInt16LittleEndian(mesh[(offset + 10)..]);
            output.Add(new TriangleBuilder(
                sourceBlockIndex,
                meshX,
                meshZ,
                meshIndex,
                triangleIndex,
                vertices[first],
                vertices[second],
                vertices[third],
                mesh[offset + 3] & 0x1F,
                textureAndRegion & 0x1FF,
                (textureAndRegion >> 9) & 0x7F));
        }
    }

    private static int ResolveSourceBlockIndex(
        int worldMapType,
        int worldProgress,
        int logicalBlockIndex)
    {
        if (worldMapType != 0)
        {
            return logicalBlockIndex;
        }

        // Keep the checks data-driven while preserving the native function's
        // cumulative thresholds. Later stages retain all earlier replacements.
        foreach (var replacement in OverworldReplacementBlocks)
        {
            if (logicalBlockIndex == replacement.ReplacesBlockIndex &&
                worldProgress >= replacement.MinimumWorldProgress)
            {
                return replacement.SourceBlockIndex;
            }
        }

        return logicalBlockIndex;
    }

    private static IReadOnlyList<int>[] BuildAdjacency(
        IReadOnlyList<TriangleBuilder> triangles,
        int wrapWidth,
        int wrapHeight)
    {
        var edgeOwners = new Dictionary<EdgeKey, List<int>>(triangles.Count * 2);
        for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            var triangle = triangles[triangleIndex];
            AddEdge(triangleIndex, triangle.Vertex0, triangle.Vertex1);
            AddEdge(triangleIndex, triangle.Vertex1, triangle.Vertex2);
            AddEdge(triangleIndex, triangle.Vertex2, triangle.Vertex0);
        }

        var neighbors = Enumerable.Range(0, triangles.Count)
            .Select(_ => new HashSet<int>())
            .ToArray();
        foreach (var owners in edgeOwners.Values)
        {
            for (var first = 0; first < owners.Count; first++)
            {
                for (var second = first + 1; second < owners.Count; second++)
                {
                    if (owners[first] == owners[second])
                    {
                        continue;
                    }

                    neighbors[owners[first]].Add(owners[second]);
                    neighbors[owners[second]].Add(owners[first]);
                }
            }
        }

        return neighbors
            .Select(set => (IReadOnlyList<int>)set.OrderBy(value => value).ToArray())
            .ToArray();

        void AddEdge(int owner, WorldMapVertex first, WorldMapVertex second)
        {
            var key = EdgeKey.Create(first, second, wrapWidth, wrapHeight);
            if (!edgeOwners.TryGetValue(key, out var owners))
            {
                owners = [];
                edgeOwners.Add(key, owners);
            }

            owners.Add(owner);
        }
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(int))
        {
            throw new InvalidDataException($"World map {label} at {offset} is outside the file.");
        }

        return BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    }

    private readonly record struct TriangleBuilder(
        int SourceBlockIndex,
        int MeshX,
        int MeshZ,
        int MeshIndex,
        int TriangleIndex,
        WorldMapVertex Vertex0,
        WorldMapVertex Vertex1,
        WorldMapVertex Vertex2,
        int TerrainId,
        int TextureId,
        int RegionId);

    private readonly record struct VertexKey(int X, int Y, int Z) : IComparable<VertexKey>
    {
        public int CompareTo(VertexKey other)
        {
            var x = X.CompareTo(other.X);
            if (x != 0)
            {
                return x;
            }

            var y = Y.CompareTo(other.Y);
            return y != 0 ? y : Z.CompareTo(other.Z);
        }

        public static VertexKey Create(WorldMapVertex vertex, int wrapWidth, int wrapHeight) =>
            new(Normalize(vertex.X, wrapWidth), vertex.Y, Normalize(vertex.Z, wrapHeight));

        private static int Normalize(int value, int extent)
        {
            var normalized = value % extent;
            return normalized < 0 ? normalized + extent : normalized;
        }
    }

    private readonly record struct EdgeKey(VertexKey First, VertexKey Second)
    {
        public static EdgeKey Create(
            WorldMapVertex first,
            WorldMapVertex second,
            int wrapWidth,
            int wrapHeight)
        {
            var firstKey = VertexKey.Create(first, wrapWidth, wrapHeight);
            var secondKey = VertexKey.Create(second, wrapWidth, wrapHeight);
            return firstKey.CompareTo(secondKey) <= 0
                ? new EdgeKey(firstKey, secondKey)
                : new EdgeKey(secondKey, firstKey);
        }
    }
}
