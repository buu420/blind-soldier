using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapDataLoaderTests
{
    internal static void Run()
    {
        LoadsInstalledOverworldGeometryAndMetadata();
        AppliesNativeOverworldProgressReplacements();
        LoadsInstalledAlternateWorldMaps();
        RejectsTruncatedAndInvalidMapData();
    }

    private static void LoadsInstalledOverworldGeometryAndMetadata()
    {
        var map = WorldMapDataLoader.Load(InstalledMap("WM0.MAP"), worldMapType: 0, worldProgress: 0);

        Equal(0, map.WorldMapType, "overworld type");
        Equal(9, map.BlockGridWidth, "overworld block columns");
        Equal(7, map.BlockGridHeight, "overworld block rows");
        Equal(36, map.MeshGridWidth, "overworld mesh columns");
        Equal(28, map.MeshGridHeight, "overworld mesh rows");
        Equal(0x48000, map.WrapWidth, "overworld x wrap");
        Equal(0x38000, map.WrapHeight, "overworld z wrap");
        Equal(69, map.RawBlockCount, "overworld physical blocks");
        Equal(63, map.ActiveBlockCount, "overworld logical blocks");
        Equal(true, map.Triangles.Count > 10_000, "overworld triangle population");
        Equal(124, map.Triangles.Count(triangle => WorldMapDataLoader.IsChocoboTrackTexture(triangle.TextureId)), "native chocobo-track triangles");
        Equal(true, map.Triangles.Any(triangle => triangle.Neighbors.Count > 0), "triangle adjacency");
    }

    private static void AppliesNativeOverworldProgressReplacements()
    {
        var path = InstalledMap("WM0.MAP");
        var expectedSourcesByProgress = new[]
        {
            Array.Empty<int>(),
            new[] { 63 },
            new[] { 63, 64, 65 },
            new[] { 63, 64, 65, 66 },
            new[] { 63, 64, 65, 66, 67, 68 }
        };

        for (var progress = 0; progress < expectedSourcesByProgress.Length; progress++)
        {
            var map = WorldMapDataLoader.Load(path, worldMapType: 0, worldProgress: progress);
            var activeReplacementSources = map.Triangles
                .Select(triangle => triangle.SourceBlockIndex)
                .Where(source => source >= 63)
                .Distinct()
                .OrderBy(source => source)
                .ToArray();
            SequenceEqual(
                expectedSourcesByProgress[progress],
                activeReplacementSources,
                $"native replacement sources at progress {progress}");
        }

        var stageOne = WorldMapDataLoader.Load(path, worldMapType: 0, worldProgress: 1);
        Equal(
            false,
            stageOne.Triangles.Any(triangle => triangle.SourceBlockIndex == 50),
            "stage one removes original logical block 50 geometry");
        Equal(
            true,
            stageOne.Triangles
                .Where(triangle => triangle.SourceBlockIndex == 63)
                .All(triangle => triangle.MeshX is >= 20 and <= 23 && triangle.MeshZ is >= 20 and <= 23),
            "replacement block 63 occupies logical block 50 coordinates");
    }

    private static void LoadsInstalledAlternateWorldMaps()
    {
        var underwater = WorldMapDataLoader.Load(InstalledMap("WM2.MAP"), 2, 0);
        Equal(3, underwater.BlockGridWidth, "underwater block columns");
        Equal(4, underwater.BlockGridHeight, "underwater block rows");
        Equal(12, underwater.MeshGridWidth, "underwater mesh columns");
        Equal(16, underwater.MeshGridHeight, "underwater mesh rows");

        var glacier = WorldMapDataLoader.Load(InstalledMap("WM3.MAP"), 3, 0);
        Equal(2, glacier.BlockGridWidth, "glacier block columns");
        Equal(2, glacier.BlockGridHeight, "glacier block rows");
        Equal(8, glacier.MeshGridWidth, "glacier mesh columns");
        Equal(8, glacier.MeshGridHeight, "glacier mesh rows");
    }

    private static void RejectsTruncatedAndInvalidMapData()
    {
        Throws<InvalidDataException>(
            () => WorldMapDataLoader.Parse([1, 2, 3], worldMapType: 0, worldProgress: 0),
            "truncated map");
        Throws<ArgumentOutOfRangeException>(
            () => WorldMapDataLoader.Parse(new byte[0xB800], worldMapType: 1, worldProgress: 0),
            "unknown world type");
    }

    private static string InstalledMap(string fileName) =>
        Path.Combine(
            Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
                @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir",
            "data",
            "wm",
            fileName);

    private static void Throws<TException>(Action action, string label)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }

    private static void SequenceEqual<T>(
        IReadOnlyList<T> expected,
        IReadOnlyList<T> actual,
        string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(", ", expected)}], " +
                $"actual [{string.Join(", ", actual)}]");
        }
    }
}
