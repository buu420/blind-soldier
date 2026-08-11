using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ff7.Accessibility.Reloaded;

public enum WorldMapNavigationCategory
{
    Locations,
    Story,
    Transportation,
    Events,
    ChocoboTracks
}

public enum WorldMapTargetKind
{
    Location,
    Story,
    Transportation,
    Event,
    ChocoboTracks
}

public sealed record WorldMapNavigationTarget(
    WorldMapNavigationCategory Category,
    WorldMapTargetKind Kind,
    string Label,
    int X,
    int Y,
    int Z,
    int TriangleId,
    int RegionId,
    string StableId,
    IReadOnlySet<int> ArrivalTriangleIds)
{
    public bool HasArrived(int triangleId) =>
        triangleId >= 0 && ArrivalTriangleIds.Contains(triangleId);
}

public sealed class WorldMapTargetCatalog
{
    private static readonly Regex MenuNamePattern = new(
        @"^\s*0x(?<id>[0-9A-Fa-f]+)\s+wm\d+\s+(?<name>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] RegionNames =
    [
        "Midgar Area",
        "Grasslands Area",
        "Junon Area",
        "Corel Area",
        "Gold Saucer Area",
        "Gongaga Area",
        "Cosmo Area",
        "Nibel Area",
        "Rocket Launch Pad Area",
        "Wutai Area",
        "Woodlands Area",
        "Icicle Area",
        "Mideel Area",
        "North Corel Area",
        "Cactus Island",
        "Goblin Island",
        "Round Island",
        "Sea",
        "Bottom of Sea",
        "Glacier"
    ];

    // Game Moment is FFVII's native primary-story variable. Some consecutive
    // overworld stops intentionally share one value; keep those candidates in
    // story order and let the native terrain planner hide unreachable ones.
    private static readonly WorldStoryStage[] StoryStages =
    [
        new(341, 384, ["Kalm"]),
        new(385, 386, ["Chocobo Farm", "Mythril Mine (Midgar side)"]),
        new(387, 414, ["Junon"]),
        new(415, 426, ["Mt. Corel"]),
        new(427, 468, ["North Corel"]),
        new(469, 522, ["Cosmo Canyon"]),
        new(523, 534, ["Nibelheim (Town Side)", "Mt. Nibel (Nibelheim Side)", "Rocket Town (South Side)"]),
        new(535, 565, ["Rocket Town (South Side)"]),
        new(566, 582, ["North Corel"]),
        new(583, 637, ["Temple of the Ancients"]),
        new(638, 676, ["Bone Village"]),
        new(677, 769, ["Icicle Inn (South Side)"]),
        new(1033, 1099, ["Mideel"]),
        new(1110, 1115, ["North Corel", "Condor"]),
        new(1116, 1117, ["Condor", "Mideel"]),
        new(1118, 1198, ["Mideel"]),
        new(1199, 1298, ["Junon"]),
        new(1299, 1307, ["Rocket Town (North Side)"]),
        new(1389, 1391, ["Cosmo Canyon"]),
        new(1392, 1395, ["Bone Village"]),
        new(1397, 1399, ["Bone Village"]),
        new(1570, 1597, ["Midgar"]),
        new(1620, 1997, ["Northern Crater"])
    ];

    public static IReadOnlyList<WorldMapNavigationCategory> CategoryOrder { get; } =
    [
        WorldMapNavigationCategory.Locations,
        WorldMapNavigationCategory.Story,
        WorldMapNavigationCategory.Transportation,
        WorldMapNavigationCategory.Events,
        WorldMapNavigationCategory.ChocoboTracks
    ];

    private readonly IReadOnlyDictionary<string, WorldMapNavigationTarget> locationsByLabel;
    private readonly WorldMapData map;
    private readonly WorldMapRoutePlanner triangleResolver;

    private WorldMapTargetCatalog(
        WorldMapData map,
        IReadOnlyList<WorldMapNavigationTarget> locations,
        IReadOnlyList<WorldMapNavigationTarget> chocoboTracks)
    {
        this.map = map;
        triangleResolver = new WorldMapRoutePlanner(map);
        Locations = locations;
        ChocoboTracks = chocoboTracks;
        locationsByLabel = locations.ToDictionary(target => target.Label, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<WorldMapNavigationTarget> Locations { get; }

    public IReadOnlyList<WorldMapNavigationTarget> ChocoboTracks { get; }

    public static WorldMapTargetCatalog Load(
        WorldMapData map,
        string coordinatePath,
        string menuNamePath)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(menuNamePath);
        var coordinates = JsonSerializer.Deserialize<Dictionary<string, CoordinateRecord>>(
                File.ReadAllText(coordinatePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"World location coordinates are empty: {coordinatePath}");
        var names = ReadMenuNames(menuNamePath);
        var locations = new List<WorldMapNavigationTarget>();
        foreach (var entry in coordinates
                     .Select(pair => (Id: int.TryParse(pair.Key, out var id) ? id : -1, pair.Value))
                     .Where(entry => entry.Id >= 0)
                     .OrderBy(entry => entry.Id))
        {
            if (!names.TryGetValue(entry.Id, out var rawName) ||
                entry.Value.MeshX < 0 || entry.Value.MeshX >= map.MeshGridWidth ||
                entry.Value.MeshY < 0 || entry.Value.MeshY >= map.MeshGridHeight)
            {
                continue;
            }

            // The entrance table stores an unsigned coordinate inside its
            // 8192-unit mesh.  Mesh vertices are signed while decoded, but the
            // entrance coordinate must not be centered a second time.
            var x = checked(entry.Value.MeshX * WorldMapDataLoader.MeshSize + entry.Value.CoorX);
            var z = checked(entry.Value.MeshY * WorldMapDataLoader.MeshSize + entry.Value.CoorY);
            var triangle = ResolveTriangle(map, entry.Value.MeshX, entry.Value.MeshY, x, z);
            if (triangle is null)
            {
                continue;
            }

            var label = NormalizeLocationName(rawName);
            locations.Add(new WorldMapNavigationTarget(
                WorldMapNavigationCategory.Locations,
                WorldMapTargetKind.Location,
                label,
                x,
                triangle.Centroid.Y,
                z,
                triangle.Id,
                triangle.RegionId & 0x1F,
                $"world-location:{entry.Id}:{label}",
                new HashSet<int> { triangle.Id }));
        }

        var tracks = BuildChocoboTrackTargets(map);
        return new WorldMapTargetCatalog(map, locations, tracks);
    }

    public IReadOnlyList<WorldMapNavigationTarget> ReadTargets(
        WorldMapNavigationCategory category,
        int regionId,
        int gameMoment)
    {
        return category switch
        {
            WorldMapNavigationCategory.Locations => Locations,
            WorldMapNavigationCategory.Story => ReadStoryTargets(gameMoment),
            WorldMapNavigationCategory.Transportation => [],
            WorldMapNavigationCategory.Events => [],
            WorldMapNavigationCategory.ChocoboTracks => ChocoboTracks
                .Where(target => target.RegionId == regionId)
                .ToArray(),
            _ => []
        };
    }

    public IReadOnlyList<WorldMapNavigationTarget> ReadTargets(
        WorldMapNavigationCategory category,
        WorldMapStateSnapshot state,
        IReadOnlyList<WorldMapEntitySnapshot> entities)
    {
        if (category == WorldMapNavigationCategory.Story)
        {
            var staticTargets = ReadStoryTargets(state.GameMoment);
            if (state.WorldMapType != map.WorldMapType || entities.Count == 0)
            {
                return staticTargets;
            }

            var dynamicTargets = entities
                .Where(entity => !entity.IsPlayer)
                .Select(entity => CreateEntityTarget(category, state, entity))
                .Where(target => target is not null)
                .Select(target => target!)
                .OrderBy(target => target.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(target => target.StableId, StringComparer.Ordinal)
                .ToArray();
            return dynamicTargets.Length == 0
                ? staticTargets
                : staticTargets.Concat(dynamicTargets).ToArray();
        }

        if (category is not (WorldMapNavigationCategory.Transportation or WorldMapNavigationCategory.Events))
        {
            return ReadTargets(category, state.RegionId, state.GameMoment);
        }

        if (state.WorldMapType != map.WorldMapType || entities.Count == 0)
        {
            return Array.Empty<WorldMapNavigationTarget>();
        }

        return entities
            .Where(entity => !entity.IsPlayer)
            .Select(entity => CreateEntityTarget(category, state, entity))
            .Where(target => target is not null)
            .Select(target => target!)
            .OrderBy(target => target.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.StableId, StringComparer.Ordinal)
            .ToArray();
    }

    private WorldMapNavigationTarget? CreateEntityTarget(
        WorldMapNavigationCategory category,
        WorldMapStateSnapshot player,
        WorldMapEntitySnapshot entity)
    {
        var label = category switch
        {
            WorldMapNavigationCategory.Story => player.GameMoment switch
            {
                1396 when entity.ModelId == 26 => "Key of the Ancients",
                >= 1400 and <= 1569 when entity.ModelId == 10 => "Diamond Weapon",
                _ => null
            },
            WorldMapNavigationCategory.Transportation => entity.ModelId switch
            {
                3 => "Highwind",
                5 => "Tiny Bronco",
                6 => "Buggy",
                13 => "Submarine",
                28 => "Red submarine",
                _ => null
            },
            WorldMapNavigationCategory.Events => entity.ModelId switch
            {
                4 => "Wild chocobo",
                10 => "Diamond Weapon",
                11 => "Ultimate Weapon",
                26 => "Key of the Ancients",
                29 => "Ruby Weapon",
                30 => "Emerald Weapon",
                _ => null
            },
            _ => null
        };
        if (label is null || entity.TerrainId is < 0 or > 31 || entity.RegionId is < 0 or > 31)
        {
            return null;
        }

        var entityState = player with
        {
            X = entity.X,
            Y = entity.Y,
            Z = entity.Z,
            TerrainId = entity.TerrainId,
            RegionId = entity.RegionId
        };
        if (!triangleResolver.TryResolvePlayerTriangle(entityState, out var triangleId))
        {
            return null;
        }

        var arrivals = new HashSet<int>();
        var triangle = map.Triangles[triangleId];
        if (WorldMapTerrainPassability.CanTraverse(player.PlayerModelId, player.WorldMapType, triangle.TerrainId))
        {
            arrivals.Add(triangleId);
        }

        foreach (var neighbor in triangle.Neighbors)
        {
            if (WorldMapTerrainPassability.CanTraverse(
                    player.PlayerModelId,
                    player.WorldMapType,
                    map.Triangles[neighbor].TerrainId))
            {
                arrivals.Add(neighbor);
            }
        }

        if (arrivals.Count == 0)
        {
            arrivals.Add(triangleId);
        }

        return new WorldMapNavigationTarget(
            category,
            category switch
            {
                WorldMapNavigationCategory.Story => WorldMapTargetKind.Story,
                WorldMapNavigationCategory.Transportation => WorldMapTargetKind.Transportation,
                _ => WorldMapTargetKind.Event
            },
            label,
            entity.X,
            entity.Y,
            entity.Z,
            triangleId,
            entity.RegionId,
            category == WorldMapNavigationCategory.Story
                ? $"world-story-entity:{entity.GuestPointer:X8}:{entity.ModelId}"
                : $"world-entity:{entity.GuestPointer:X8}:{entity.ModelId}",
            arrivals);
    }

    private IReadOnlyList<WorldMapNavigationTarget> ReadStoryTargets(int gameMoment)
    {
        var stage = StoryStages.FirstOrDefault(candidate =>
            gameMoment >= candidate.MinimumGameMoment &&
            gameMoment <= candidate.MaximumGameMoment);
        if (stage is null)
        {
            return [];
        }

        return stage.LocationLabels
            .Select(label => locationsByLabel.TryGetValue(label, out var location) ? location : null)
            .Where(location => location is not null)
            .Select(location => location! with
            {
                Category = WorldMapNavigationCategory.Story,
                Kind = WorldMapTargetKind.Story,
                StableId = $"world-story:{CreateStableName(location.Label)}"
            })
            .ToArray();
    }

    private static string CreateStableName(string label) =>
        Regex.Replace(label.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private static IReadOnlyDictionary<int, string> ReadMenuNames(string path)
    {
        var names = new Dictionary<int, string>();
        foreach (var line in File.ReadLines(path))
        {
            var match = MenuNamePattern.Match(line);
            if (!match.Success ||
                !int.TryParse(
                    match.Groups["id"].Value,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var id))
            {
                continue;
            }

            names[id] = match.Groups["name"].Value.Trim();
        }

        return names;
    }

    private static string NormalizeLocationName(string value) =>
        value
            .Replace("Mithryl", "Mythril", StringComparison.OrdinalIgnoreCase)
            .Replace("Coral", "Corel", StringComparison.OrdinalIgnoreCase)
            .Replace("Ancient Forset", "Ancient Forest", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('*')
            .Trim();

    private static WorldMapTriangle? ResolveTriangle(
        WorldMapData map,
        int meshX,
        int meshZ,
        int x,
        int z)
    {
        var candidates = map.Triangles
            .Where(triangle => triangle.MeshX == meshX && triangle.MeshZ == meshZ)
            .ToArray();
        var containing = candidates.FirstOrDefault(triangle => ContainsPoint(triangle, x, z));
        if (containing is not null)
        {
            return containing;
        }

        return candidates
            .OrderBy(triangle => WrappedDistanceSquared(map, x, z, triangle.Centroid.X, triangle.Centroid.Z))
            .FirstOrDefault();
    }

    private static IReadOnlyList<WorldMapNavigationTarget> BuildChocoboTrackTargets(WorldMapData map)
    {
        return map.Triangles
            .Where(triangle => WorldMapDataLoader.IsChocoboTrackTexture(triangle.TextureId))
            .GroupBy(triangle => triangle.RegionId & 0x1F)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var triangles = group.ToArray();
                var centerX = (int)Math.Round(triangles.Average(triangle => triangle.Centroid.X));
                var centerZ = (int)Math.Round(triangles.Average(triangle => triangle.Centroid.Z));
                var representative = triangles
                    .OrderBy(triangle => WrappedDistanceSquared(
                        map,
                        centerX,
                        centerZ,
                        triangle.Centroid.X,
                        triangle.Centroid.Z))
                    .First();
                var regionName = group.Key >= 0 && group.Key < RegionNames.Length
                    ? RegionNames[group.Key]
                    : $"Region {group.Key}";
                return new WorldMapNavigationTarget(
                    WorldMapNavigationCategory.ChocoboTracks,
                    WorldMapTargetKind.ChocoboTracks,
                    $"{regionName} chocobo tracks",
                    representative.Centroid.X,
                    representative.Centroid.Y,
                    representative.Centroid.Z,
                    representative.Id,
                    group.Key,
                    $"world-chocobo-tracks:{group.Key}",
                    new HashSet<int>(triangles.Select(triangle => triangle.Id)));
            })
            .ToArray();
    }

    private static bool ContainsPoint(WorldMapTriangle triangle, int x, int z)
    {
        var first = SignedArea(x, z, triangle.Vertex0, triangle.Vertex1);
        var second = SignedArea(x, z, triangle.Vertex1, triangle.Vertex2);
        var third = SignedArea(x, z, triangle.Vertex2, triangle.Vertex0);
        var hasNegative = first < 0 || second < 0 || third < 0;
        var hasPositive = first > 0 || second > 0 || third > 0;
        return !(hasNegative && hasPositive);
    }

    private static long SignedArea(int x, int z, WorldMapVertex start, WorldMapVertex end) =>
        ((long)x - end.X) * (start.Z - end.Z) - ((long)start.X - end.X) * (z - end.Z);

    internal static double WrappedDistanceSquared(
        WorldMapData map,
        int firstX,
        int firstZ,
        int secondX,
        int secondZ)
    {
        var dx = WrappedDelta(firstX, secondX, map.WrapWidth);
        var dz = WrappedDelta(firstZ, secondZ, map.WrapHeight);
        return dx * (double)dx + dz * (double)dz;
    }

    internal static int WrappedDelta(int from, int to, int extent)
    {
        var delta = to - from;
        if (delta > extent / 2)
        {
            delta -= extent;
        }
        else if (delta < -extent / 2)
        {
            delta += extent;
        }

        return delta;
    }

    private sealed record CoordinateRecord(int MeshX, int MeshY, int CoorX, int CoorY);

    private sealed record WorldStoryStage(
        int MinimumGameMoment,
        int MaximumGameMoment,
        IReadOnlyList<string> LocationLabels);
}
