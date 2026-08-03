namespace Ff7.Accessibility.Reloaded;

public readonly record struct WorldMapVertex(int X, int Y, int Z);

public sealed record WorldMapTriangle(
    int Id,
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
    int RegionId,
    IReadOnlyList<int> Neighbors)
{
    public WorldMapVertex Centroid => new(
        (Vertex0.X + Vertex1.X + Vertex2.X) / 3,
        (Vertex0.Y + Vertex1.Y + Vertex2.Y) / 3,
        (Vertex0.Z + Vertex1.Z + Vertex2.Z) / 3);

    public IEnumerable<(WorldMapVertex Start, WorldMapVertex End)> Edges
    {
        get
        {
            yield return (Vertex0, Vertex1);
            yield return (Vertex1, Vertex2);
            yield return (Vertex2, Vertex0);
        }
    }
}

public sealed record WorldMapReplacementBlock(
    int SourceBlockIndex,
    int ReplacesBlockIndex,
    int MinimumWorldProgress,
    string Description);

public sealed class WorldMapData
{
    internal WorldMapData(
        int worldMapType,
        int worldProgress,
        int blockGridWidth,
        int blockGridHeight,
        int rawBlockCount,
        IReadOnlyList<WorldMapTriangle> triangles,
        IReadOnlyList<WorldMapReplacementBlock> replacementBlocks,
        string sourcePath)
    {
        WorldMapType = worldMapType;
        WorldProgress = worldProgress;
        BlockGridWidth = blockGridWidth;
        BlockGridHeight = blockGridHeight;
        RawBlockCount = rawBlockCount;
        Triangles = triangles;
        ReplacementBlocks = replacementBlocks;
        SourcePath = sourcePath;
    }

    public int WorldMapType { get; }

    public int WorldProgress { get; }

    public int BlockGridWidth { get; }

    public int BlockGridHeight { get; }

    public int MeshGridWidth => BlockGridWidth * 4;

    public int MeshGridHeight => BlockGridHeight * 4;

    public int WrapWidth => MeshGridWidth * WorldMapDataLoader.MeshSize;

    public int WrapHeight => MeshGridHeight * WorldMapDataLoader.MeshSize;

    public int RawBlockCount { get; }

    public int ActiveBlockCount => BlockGridWidth * BlockGridHeight;

    public IReadOnlyList<WorldMapTriangle> Triangles { get; }

    public IReadOnlyList<WorldMapReplacementBlock> ReplacementBlocks { get; }

    public string SourcePath { get; }
}
