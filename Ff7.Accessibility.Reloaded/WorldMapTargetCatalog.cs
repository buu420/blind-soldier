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
    ChocoboTracks,
    Regions
}

public enum WorldMapTargetKind
{
    Location,
    Story,
    Transportation,
    Event,
    ChocoboTracks,
    TerrainArea
}

public readonly record struct WorldMapNativeLocationArrival(
    int TriangleId,
    int MeshX,
    int MeshZ,
    int TerrainScriptId,
    int X,
    int Y,
    int Z);

public sealed record WorldMapUnresolvedLocation(
    int LocationId,
    string Label,
    string Reason);

/// <summary>
/// The native condition behind a Story stop that has no resolved trigger entry: the
/// world script's own proximity test on one of its points, or the terrain the landing
/// handler requires under the player.
/// </summary>
/// <param name="RequiredTerrainId">
/// FUN_0076667C reads the terrain under the player and tests it for 27 before it
/// invokes the world system's landing function. Negative means no terrain condition.
/// </param>
/// <param name="ManhattanBound">
/// The world script measures a wrapped three-axis Manhattan distance and shifts it
/// right five before comparing against 256, so what it accepts is a raw distance of
/// up to 8223. It is not a 256-unit radius, and it is not Euclidean.
/// </param>
/// <param name="AllowedPlayerModelIds">
/// Which world models the native handler will run for. The crater flyover and landing
/// are the Highwind's own, model 3; the Great Glacier's northern boundary handlers each
/// test the current player model for 0, 1 or 2 - Cloud, Tifa or Cid on foot - so a
/// single required model cannot describe both.
/// </param>
/// <param name="MinimumGameMoment">
/// The story counter the native handler is written for. A saved target keeps its
/// coordinates after the story has moved on, and standing on them again is not the same
/// as the game running the handler: point 14's proximity advances the story only at
/// 1580, and at 1583 the same position does nothing at all.
/// </param>
public sealed record WorldMapNativeStoryArrival(
    IReadOnlySet<int> AllowedPlayerModelIds,
    int RequiredWorldMapType,
    int RequiredTerrainId,
    int PointX,
    int PointY,
    int PointZ,
    int ManhattanBound,
    int WrapWidth,
    int WrapHeight,
    int MinimumGameMoment,
    int MaximumGameMoment)
{
    public bool IsSatisfiedBy(WorldMapStateSnapshot state)
    {
        if (!AllowedPlayerModelIds.Contains(state.PlayerModelId) ||
            state.WorldMapType != RequiredWorldMapType ||
            state.GameMoment < MinimumGameMoment ||
            state.GameMoment > MaximumGameMoment)
        {
            return false;
        }

        if (RequiredTerrainId >= 0)
        {
            return state.TerrainId == RequiredTerrainId;
        }

        if (ManhattanBound < 0)
        {
            // A boundary has no point to be near: being on one of its triangles, in the
            // right module, as a character the handler runs for, is the whole test.
            return true;
        }

        // Absolute per axis, and all three axes. Signed deltas would let a point far to
        // the east and far to the south cancel each other out into a distance the script
        // would never accept, and leaving height out would accept an airship directly
        // above the point at any altitude - which the native three-axis measure does
        // not: point 14 is initialised with Y 0.
        var distance =
            (long)Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.X, PointX, WrapWidth)) +
            Math.Abs(state.Y - (long)PointY) +
            Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.Z, PointZ, WrapHeight));
        return distance <= ManhattanBound;
    }
}

/// <summary>
/// The native collision that makes a parked vehicle boardable, from Ghidra
/// <c>FUN_00762A21</c>: two 8x8 masks over 256-unit cells, centred on a 1024-unit offset,
/// and contact when either participant's mask covers the other. <c>FUN_00762993</c> stores
/// the entity that reports contact in <c>current + 4</c>, and <c>FUN_0076420A</c> reads
/// that slot when Confirm is pressed. Nothing here presses anything: it is the condition
/// under which the player's own Confirm would work.
///
/// <para>Internal, and it has to stay that way: it carries a guest entity pointer,
/// and the trust surface the x64 runtime publishes does not expose native addresses.
/// Both test assemblies see it through InternalsVisibleTo.</para>
/// </summary>
internal sealed record WorldMapNativeVehicleContact(
    uint VehicleEntityPointer,
    int VehicleModelId,
    int VehicleX,
    int VehicleZ,
    int WrapWidth,
    int WrapHeight)
{
    /// <summary>
    /// Either the party is standing inside the mask, or the game has recorded that they
    /// are touching this vehicle.
    ///
    /// <para>Both are needed. A dismount can leave the party inside a vehicle's footprint,
    /// which the geometry test covers. Walking up to one never ends there: FUN_00762A21
    /// refuses a step that collides and closes, so the approach is rolled back to just
    /// outside the mask every frame. The witness is what the game recorded during those
    /// refused steps.</para>
    /// </summary>
    public bool IsSatisfiedBy(WorldMapStateSnapshot state) =>
        IsSomebodyElse(state) && (OccupiesNativeMask(state) || IsWitnessedBy(state));

    /// <summary>
    /// The party is not this vehicle. A remount puts them at its coordinates in its model,
    /// which occupies the mask perfectly, so without this the moment the player boards a
    /// boat the mod would announce that they had arrived at it.
    /// </summary>
    public bool IsSomebodyElse(WorldMapStateSnapshot state) =>
        state.NativePlayerEntityPointer != VehicleEntityPointer &&
        state.PlayerModelId != VehicleModelId;

    public bool OccupiesNativeMask(WorldMapStateSnapshot state) =>
        WorldMapVehicleObstacles.Blocks(
            state.PlayerModelId,
            state.X,
            state.Z,
            VehicleModelId,
            VehicleX,
            VehicleZ,
            WrapWidth,
            WrapHeight);

    /// <summary>
    /// Whether <c>player + 4</c> names this vehicle, credibly. The slot is dynamic and
    /// nothing clears it for us, so a pointer alone only says the party touched something
    /// once; it must name this entity, the party must not be that entity, and the vehicle
    /// must be inside the 1024-unit window FUN_00762A21 checks before either mask. A stale
    /// pointer from a collision elsewhere fails the last of those.
    /// </summary>
    public bool IsWitnessedBy(WorldMapStateSnapshot state) =>
        VehicleEntityPointer != 0 &&
        state.NativeContactEntityPointer == VehicleEntityPointer &&
        IsSomebodyElse(state) &&
        IsWithinNativeReach(state);

    /// <summary>
    /// The guard FUN_00762A21 applies before it indexes either mask: both axes inside
    /// 1024 wrapped units.
    /// </summary>
    public bool IsWithinNativeReach(WorldMapStateSnapshot state) =>
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.X, VehicleX, WrapWidth))
            < WorldMapVehicleObstacles.NativeReach &&
        Math.Abs(WorldMapTargetCatalog.WrappedDelta(state.Z, VehicleZ, WrapHeight))
            < WorldMapVehicleObstacles.NativeReach;
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
        NativeLocationArrivals.Count == 0 &&
        triangleId >= 0 &&
        ArrivalTriangleIds.Contains(triangleId);

    public IReadOnlyList<WorldMapNativeLocationArrival> NativeLocationArrivals { get; init; } = [];

    private static readonly IReadOnlySet<int> NoEntranceExemptions = new HashSet<int>();

    /// <summary>
    /// The native field entrances this destination licenses walking onto.
    ///
    /// <para>Only a place the player actually chose to enter. A location, or the story copy
    /// of that same location, is the whole reason its trigger exists, so its own entrance
    /// triangles are exempt. Everything else is empty: a Buggy parked in Gongaga's trigger
    /// cell, a chocobo track laid across one, or a terrain area that overlaps one does not
    /// make entering that town what the player asked for.</para>
    /// </summary>
    public IReadOnlySet<int> NativeEntranceExemptions =>
        Kind is WorldMapTargetKind.Location or WorldMapTargetKind.Story
            ? ArrivalTriangleIds
            : NoEntranceExemptions;

    /// <summary>
    /// For a Story stop the trigger metadata cannot name, the condition the game
    /// itself applies. A candidate triangle is only where the target might be; it is
    /// not proof the player has satisfied the native test.
    /// </summary>
    public WorldMapNativeStoryArrival? NativeStoryArrival { get; init; }

    /// <summary>
    /// For a parked vehicle, the native collision the game itself requires before it will
    /// dispatch boarding. Standing in the arrival triangle is not enough: the triangles
    /// here are large, and the far corner of one can be a thousand units from any point
    /// the mask covers. Announcing arrival there would be announcing something the player
    /// cannot then do.
    /// </summary>
    internal WorldMapNativeVehicleContact? NativeVehicleContact { get; init; }

    /// <summary>
    /// Per arrival triangle, the point in it nearest the vehicle that is in native
    /// contact. The route ends on one of these rather than on a centroid.
    /// </summary>
    public IReadOnlyDictionary<int, WorldMapVertex> VehicleContactPoints { get; init; } =
        new Dictionary<int, WorldMapVertex>();

    public bool HasArrived(WorldMapStateSnapshot state, int triangleId)
    {
        if (NativeVehicleContact is { } vehicleContact)
        {
            return ArrivalTriangleIds.Contains(triangleId) && vehicleContact.IsSatisfiedBy(state);
        }

        if (NativeStoryArrival is { } nativeArrival)
        {
            return nativeArrival.IsSatisfiedBy(state) && ArrivalTriangleIds.Contains(triangleId);
        }

        if (NativeLocationArrivals.Count == 0)
        {
            return HasArrived(triangleId);
        }

        var meshX = (int)Math.Floor(state.X / (double)WorldMapDataLoader.MeshSize);
        var meshZ = (int)Math.Floor(state.Z / (double)WorldMapDataLoader.MeshSize);
        return NativeLocationArrivals.Any(arrival =>
            arrival.TriangleId == triangleId &&
            arrival.MeshX == meshX &&
            arrival.MeshZ == meshZ &&
            arrival.TerrainScriptId == state.TerrainScriptId);
    }
}

public sealed class WorldMapTargetCatalog
{
    private static readonly Regex MenuNamePattern = new(
        @"^\s*0x(?<id>[0-9A-Fa-f]+)\s+wm\d+\s+(?<name>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Declared before the stage table that uses it: a static field initialised later in
    /// the file is still null while an earlier one is being built.
    /// </summary>
    private static readonly IReadOnlySet<int> HighwindOnlyModels = new HashSet<int> { HighwindModelId };

    /// <summary>
    /// Native point 14, initialised at wm0.ev 0B8C..0B9C as mesh (16,4) with local
    /// X 1 and Z 3999 - world X 131073, Z 36767.
    /// </summary>
    private static readonly WorldStoryNativeTarget NorthernCraterFlyover =
        WorldStoryNativeTarget.AtWorldPoint("Northern Crater (fly over)", 131073, 36767, 1580, 1580);

    /// <summary>
    /// The landing ground itself, taken from the installed map rather than named: every
    /// triangle whose terrain is 27, which is what the native landing handler tests.
    /// </summary>
    private static readonly WorldStoryNativeTarget NorthernCraterLanding =
        WorldStoryNativeTarget.OnTerrain("Northern Crater (land the Highwind)", 27, 1620, 1997);

    /// <summary>
    /// The Great Glacier's way north, out of the snowfield towards the cliff.
    ///
    /// The installed wm3.ev has six terrain handlers along the northern edge, all
    /// entering location 60 entry point 0: 0x8253 at mesh (1,1) script 6, 0x8264 (2,1)
    /// script 7, 0x8274 (3,1) script 7, 0x8284 (4,1) script 7, 0x8294 (5,1) script 7 and
    /// 0x82A3 (6,1) script 6. Each of them first tests the current player model for 0, 1
    /// or 2 - Cloud, Tifa or Cid walking - so this is a way out on foot and not a
    /// flight. They are equivalent exits, which is why all six cells are one target and
    /// the nearest of them is the one offered.
    ///
    /// It is not a catalog location: location 60 has no world menu name and no field
    /// coordinate entry, because there is no field behind it - it is one world map
    /// leading to another. The central cave, location 64, is optional and is not this.
    /// </summary>
    private static readonly WorldStoryNativeTarget GreatGlacierNorthernExit =
        WorldStoryNativeTarget.AtBoundary(
            "The way north out of the snowfield",
            worldMapType: 3,
            allowedPlayerModelIds: new HashSet<int> { 0, 1, 2 },
            minimumGameMoment: 677,
            maximumGameMoment: 769,
            cells:
            [
                new WorldStoryBoundaryCell(1, 1, 6),
                new WorldStoryBoundaryCell(2, 1, 7),
                new WorldStoryBoundaryCell(3, 1, 7),
                new WorldStoryBoundaryCell(4, 1, 7),
                new WorldStoryBoundaryCell(5, 1, 7),
                new WorldStoryBoundaryCell(6, 1, 6)
            ]);
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
        // The forest is what separates these. Below 652 the excavation is still the
        // step; the write of 652 is the Sleeping Forest letting the party through, and
        // from there Bone Village is behind them.
        new(638, 651, ["Bone Village"]),
        new(652, 676, ["Valley, City of Ancients entrance"]),
        new(677, 769, ["Icicle Inn (South Side)"], GreatGlacierNorthernExit),
        new(1033, 1099, ["Mideel"]),
        new(1110, 1115, ["North Corel", "Condor"]),
        // 1116 is written after both Huge Materia missions have finished, whichever
        // order they were done in and whether or not the train was caught. Offering the
        // Fort again here would be sending the party back to something already over.
        new(1116, 1117, ["Mideel"]),
        new(1118, 1198, ["Mideel"]),
        new(1199, 1298, ["Junon"]),
        new(1299, 1307, ["Rocket Town (North Side)"]),
        // Bugenhagen's room writes1391 before returning the party to the Highwind
        // (bugin1a AD Script7). The next destination is the Capital entrance58;
        // routing back to Cosmo Canyon repeats the completed handoff.
        new(1389, 1390, ["Cosmo Canyon"]),
        new(1391, 1395, ["Valley, City of Ancients entrance"]),
        new(1397, 1399, ["Valley, City of Ancients entrance"]),
        // Two different native triggers, not one Midgar stage. wm0.ev's Highwind Tick
        // (function 4302 at 20EF) flies the party over the crater first: below 1580 a
        // proximity test on native point 14 calls Highwind function 20, and at 1580 the
        // same proximity enters field location 52. Only at 1596 does the test switch to
        // point 9, write 1598 and enter 52 again - that one is the Midgar approach.
        // Offering Midgar during the flyover would skip the scene that leads to it.
        new(1580, 1595, [], NorthernCraterFlyover),

        // Point 9, not point 14, and still the Highwind's own Tick: reaching Midgar
        // here is flying there, and the ordinary on-foot approach is a different
        // trigger entirely.
        new(1596, 1597, ["Midgar"], RequiredPlayerModelIds: HighwindOnlyModels),

        // The ending. Location 59 has no resolved entry in the native trigger metadata,
        // so a label lookup produces nothing at all and this stage was silently empty.
        // The landing is not an ordinary terrain-script entrance either: FUN_0076667C
        // tests the terrain under the player for 27 before invoking the world system's
        // landing handler, and wm0.ev system function 9 gates on GameMoment >= 1620.
        new(1620, 1997, [], NorthernCraterLanding)
    ];


    public static IReadOnlyList<WorldMapNavigationCategory> CategoryOrder { get; } =
    [
        WorldMapNavigationCategory.Locations,
        WorldMapNavigationCategory.Story,
        WorldMapNavigationCategory.Transportation,
        WorldMapNavigationCategory.Events,
        WorldMapNavigationCategory.ChocoboTracks,
        WorldMapNavigationCategory.Regions
    ];

    private readonly IReadOnlyDictionary<string, WorldMapNavigationTarget> locationsByLabel;
    private readonly WorldMapData map;
    private readonly WorldMapRoutePlanner triangleResolver;
    private readonly IReadOnlyList<TerrainAreaPatch> terrainAreaPatches;

    private WorldMapTargetCatalog(
        WorldMapData map,
        IReadOnlyList<WorldMapNavigationTarget> locations,
        IReadOnlyList<WorldMapNavigationTarget> chocoboTracks,
        IReadOnlyList<WorldMapUnresolvedLocation> unresolvedLocations,
        IReadOnlySet<int> entranceTriangleIds)
    {
        this.map = map;
        triangleResolver = new WorldMapRoutePlanner(map);
        terrainAreaPatches = BuildTerrainAreaPatches(map);
        Locations = locations;
        ChocoboTracks = chocoboTracks;
        UnresolvedLocations = unresolvedLocations;
        locationsByLabel = locations.ToDictionary(target => target.Label, StringComparer.OrdinalIgnoreCase);
        EntranceTriangleIds = entranceTriangleIds;
    }

    public IReadOnlyList<WorldMapNavigationTarget> Locations { get; }

    /// <summary>
    /// Every triangle the native world script turns into a field entry.
    ///
    /// <para>These are the mapped location triggers only - the cells
    /// world-map-location-triggers.json accounts for, matched on mesh X/Y and terrain
    /// script id. Terrain script ids are not zones in themselves, so nothing is inferred
    /// from a script id alone.</para>
    ///
    /// <para>Walking onto one of these enters its field. That is correct when the player
    /// asked for that place and a defect otherwise: the 2026-09-21 session shows the party
    /// walking to their parked Buggy through Gongaga's trigger at mesh (13,22) and being
    /// dropped into the jungle over and over.</para>
    /// </summary>
    public IReadOnlySet<int> EntranceTriangleIds { get; }

    public IReadOnlyList<WorldMapNavigationTarget> ChocoboTracks { get; }

    public IReadOnlyList<WorldMapUnresolvedLocation> UnresolvedLocations { get; }

    public static WorldMapTargetCatalog Load(
        WorldMapData map,
        string coordinatePath,
        string menuNamePath,
        string nativeTriggerPath)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(menuNamePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeTriggerPath);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var coordinates = JsonSerializer.Deserialize<Dictionary<string, CoordinateRecord>>(
                File.ReadAllText(coordinatePath),
                jsonOptions)
            ?? throw new InvalidDataException($"World location coordinates are empty: {coordinatePath}");
        var names = ReadMenuNames(menuNamePath);
        var triggerDocument = JsonSerializer.Deserialize<NativeLocationTriggerDocument>(
                File.ReadAllText(nativeTriggerPath),
                jsonOptions)
            ?? throw new InvalidDataException($"World location triggers are empty: {nativeTriggerPath}");
        if (triggerDocument.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"World location trigger schema {triggerDocument.SchemaVersion} is unsupported: {nativeTriggerPath}");
        }

        var coordinateIds = coordinates.Keys
            .Select(key => int.TryParse(key, out var id) ? id : -1)
            .Where(id => id >= 0)
            .ToHashSet();
        var mappedIds = triggerDocument.Locations.Select(location => location.LocationId).ToArray();
        var unresolvedIds = triggerDocument.UnresolvedLocations.Select(location => location.LocationId).ToArray();
        if (mappedIds.Length != mappedIds.Distinct().Count() ||
            unresolvedIds.Length != unresolvedIds.Distinct().Count() ||
            mappedIds.Intersect(unresolvedIds).Any() ||
            !coordinateIds.SetEquals(mappedIds.Concat(unresolvedIds)))
        {
            throw new InvalidDataException(
                "World location trigger metadata must account for every coordinate exactly once.");
        }

        var locations = new List<WorldMapNavigationTarget>();
        foreach (var entry in coordinates
                     .Select(pair => (Id: int.TryParse(pair.Key, out var id) ? id : -1, pair.Value))
                     .Where(entry => entry.Id >= 0)
                     .OrderBy(entry => entry.Id))
        {
            if (!names.TryGetValue(entry.Id, out var rawName))
            {
                continue;
            }

            var mapping = triggerDocument.Locations.SingleOrDefault(location =>
                location.LocationId == entry.Id && location.WorldMapType == map.WorldMapType);
            if (mapping is null)
            {
                continue;
            }

            var triggerCandidates = map.Triangles
                .Where(triangle =>
                    triangle.MeshX == mapping.MeshX &&
                    triangle.MeshZ == mapping.MeshY &&
                    triangle.TerrainScriptId == mapping.TerrainScriptId)
                .Select(triangle => TryFindStrictInteriorPoint(triangle, out var point)
                    ? new NativeLocationCandidate(triangle, point)
                    : (NativeLocationCandidate?)null)
                .Where(candidate => candidate.HasValue)
                .Select(candidate => candidate!.Value)
                .ToArray();
            if (triggerCandidates.Length == 0)
            {
                continue;
            }

            // Keep the old display coordinate only as a deterministic tie-breaker
            // between triangles in the same native trigger. Navigation itself ends
            // at the selected trigger triangle's centroid, never at that old marker.
            var markerX = checked(entry.Value.MeshX * WorldMapDataLoader.MeshSize + entry.Value.CoorX);
            var markerZ = checked(entry.Value.MeshY * WorldMapDataLoader.MeshSize + entry.Value.CoorY);
            var representative = triggerCandidates
                .OrderBy(candidate => WrappedDistanceSquared(
                    map,
                    markerX,
                    markerZ,
                    candidate.Point.X,
                    candidate.Point.Z))
                .ThenBy(candidate => candidate.Triangle.Id)
                .First();
            var triangle = representative.Triangle;
            var label = NormalizeLocationName(rawName);
            locations.Add(new WorldMapNavigationTarget(
                WorldMapNavigationCategory.Locations,
                WorldMapTargetKind.Location,
                label,
                representative.Point.X,
                representative.Point.Y,
                representative.Point.Z,
                triangle.Id,
                triangle.RegionId & 0x1F,
                $"world-location:{entry.Id}:{label}",
                new HashSet<int>(triggerCandidates.Select(candidate => candidate.Triangle.Id)))
            {
                NativeLocationArrivals = triggerCandidates
                    .Select(candidate => new WorldMapNativeLocationArrival(
                        candidate.Triangle.Id,
                        mapping.MeshX,
                        mapping.MeshY,
                        mapping.TerrainScriptId,
                        candidate.Point.X,
                        candidate.Point.Y,
                        candidate.Point.Z))
                    .ToArray()
            });
        }

        var tracks = BuildChocoboTrackTargets(map);
        var unresolved = triggerDocument.UnresolvedLocations
            .Select(location => new WorldMapUnresolvedLocation(
                location.LocationId,
                NormalizeLocationName(
                    names.TryGetValue(location.LocationId, out var name) ? name : location.Label),
                location.Reason))
            .OrderBy(location => location.LocationId)
            .ToArray();
        // Every triangle the native handler would fire on, not only the ones that also
        // offered a usable arrival point. A trigger triangle too thin to stand in the
        // middle of still enters its field when the party walks across it, and (13,22)
        // - Gongaga - has exactly that shape.
        var entrances = triggerDocument.Locations
            .Where(location => location.WorldMapType == map.WorldMapType)
            .SelectMany(location => map.Triangles
                .Where(triangle =>
                    triangle.MeshX == location.MeshX &&
                    triangle.MeshZ == location.MeshY &&
                    triangle.TerrainScriptId == location.TerrainScriptId)
                .Select(triangle => triangle.Id))
            .ToHashSet();
        return new WorldMapTargetCatalog(map, locations, tracks, unresolved, entrances);
    }

    public IReadOnlyList<WorldMapNavigationTarget> ReadTargets(
        WorldMapNavigationCategory category,
        int regionId,
        int gameMoment)
    {
        return category switch
        {
            WorldMapNavigationCategory.Locations => Locations,
            WorldMapNavigationCategory.Story => ReadStoryTargets(gameMoment, null),
            WorldMapNavigationCategory.Transportation => [],
            WorldMapNavigationCategory.Events => [],
            WorldMapNavigationCategory.ChocoboTracks => ChocoboTracks
                .Where(target => target.RegionId == regionId)
                .ToArray(),
            WorldMapNavigationCategory.Regions => [],
            _ => []
        };
    }

    public IReadOnlyList<WorldMapNavigationTarget> ReadTargets(
        WorldMapNavigationCategory category,
        WorldMapStateSnapshot state,
        IReadOnlyList<WorldMapEntitySnapshot> entities)
    {
        if (category == WorldMapNavigationCategory.Regions)
        {
            return ReadTerrainAreaTargets(state);
        }

        if (category == WorldMapNavigationCategory.Story)
        {
            var staticTargets = ReadStoryTargets(state.GameMoment, state);
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

    private IReadOnlyList<WorldMapNavigationTarget> ReadTerrainAreaTargets(
        WorldMapStateSnapshot state)
    {
        if (state.WorldMapType != map.WorldMapType ||
            !triangleResolver.TryResolveReachableComponent(state, out var componentId))
        {
            return [];
        }

        return terrainAreaPatches
            .Where(patch => triangleResolver.GetComponentId(
                state.PlayerModelId,
                state.WorldMapType,
                patch.RepresentativeTriangleId) == componentId)
            .GroupBy(patch => (patch.TerrainId, patch.DominantRegionId))
            .Select(group => CreateTerrainAreaTarget(group.Key, group.ToArray()))
            .OrderBy(target => MinimumDistanceSquared(state, target))
            .ThenBy(target => target.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.StableId, StringComparer.Ordinal)
            .ToArray();
    }

    private WorldMapNavigationTarget CreateTerrainAreaTarget(
        (int TerrainId, int DominantRegionId) key,
        IReadOnlyList<TerrainAreaPatch> patches)
    {
        var representativePatch = patches
            .OrderByDescending(patch => patch.PlanarArea)
            .ThenBy(patch => patch.RepresentativeTriangleId)
            .First();
        var representative = map.Triangles[representativePatch.RepresentativeTriangleId];
        var terrainName = CapitalizeFirst(WorldMapTerrainNames.GetName(key.TerrainId));
        var regionName = WorldMapRegionNames.TryGetName(key.DominantRegionId, out var namedRegion)
            ? namedRegion
            : $"Region {key.DominantRegionId}";
        return new WorldMapNavigationTarget(
            WorldMapNavigationCategory.Regions,
            WorldMapTargetKind.TerrainArea,
            $"{terrainName}, {regionName}",
            representative.Centroid.X,
            representative.Centroid.Y,
            representative.Centroid.Z,
            representative.Id,
            key.DominantRegionId,
            $"world-terrain-area:{map.WorldMapType}:{key.TerrainId}:{key.DominantRegionId}",
            new HashSet<int>(patches.SelectMany(patch => patch.TriangleIds)));
    }

    private double MinimumDistanceSquared(
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target) =>
        target.ArrivalTriangleIds.Min(id =>
        {
            var center = map.Triangles[id].Centroid;
            return WrappedDistanceSquared(
                map,
                state.X,
                state.Z,
                center.X,
                center.Z);
        });

    private static IReadOnlyList<TerrainAreaPatch> BuildTerrainAreaPatches(WorldMapData map)
    {
        var minimumArea = WorldMapDataLoader.MeshSize * (double)WorldMapDataLoader.MeshSize / 16d;
        var visited = new bool[map.Triangles.Count];
        var patches = new List<TerrainAreaPatch>();
        for (var start = 0; start < map.Triangles.Count; start++)
        {
            if (visited[start])
            {
                continue;
            }

            var terrainId = map.Triangles[start].TerrainId;
            var pending = new Queue<int>();
            var triangleIds = new List<int>();
            var regionAreas = new Dictionary<int, double>();
            var planarArea = 0d;
            pending.Enqueue(start);
            visited[start] = true;
            while (pending.Count > 0)
            {
                var currentId = pending.Dequeue();
                var triangle = map.Triangles[currentId];
                var triangleArea = PlanarArea(triangle);
                triangleIds.Add(currentId);
                planarArea += triangleArea;
                regionAreas.TryGetValue(triangle.RegionId, out var regionArea);
                regionAreas[triangle.RegionId] = regionArea + triangleArea;

                foreach (var neighborId in triangle.Neighbors)
                {
                    if (!visited[neighborId] && map.Triangles[neighborId].TerrainId == terrainId)
                    {
                        visited[neighborId] = true;
                        pending.Enqueue(neighborId);
                    }
                }
            }

            if (planarArea < minimumArea)
            {
                continue;
            }

            var dominantRegionId = regionAreas
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .First().Key;
            var representativeTriangleId = triangleIds
                .OrderByDescending(id => PlanarArea(map.Triangles[id]))
                .ThenBy(id => id)
                .First();
            patches.Add(new TerrainAreaPatch(
                terrainId,
                dominantRegionId,
                representativeTriangleId,
                planarArea,
                triangleIds.ToArray()));
        }

        return patches;
    }

    private static double PlanarArea(WorldMapTriangle triangle)
    {
        var firstX = (long)triangle.Vertex1.X - triangle.Vertex0.X;
        var firstZ = (long)triangle.Vertex1.Z - triangle.Vertex0.Z;
        var secondX = (long)triangle.Vertex2.X - triangle.Vertex0.X;
        var secondZ = (long)triangle.Vertex2.Z - triangle.Vertex0.Z;
        return Math.Abs(firstX * (double)secondZ - firstZ * (double)secondX) / 2d;
    }

    private static string CapitalizeFirst(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];

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

        // A vehicle whose native footprint was read out of the executable resolves its own
        // approach: the ground the party can stand on and be in collision with it, which
        // is the condition the game requires before Confirm will board. The neighbour ring
        // below is not that. It offered the Tiny Bronco's far bank, where no position at
        // all is inside the boat's mask, while leaving out the bank the party was standing
        // on, because that one is two triangles away across the water the boat floats in.
        var nativeFootprint =
            category == WorldMapNavigationCategory.Transportation &&
            WorldMapVehicleObstacles.TryGetNativeMask(entity.ModelId, out _) &&
            WorldMapVehicleObstacles.TryGetNativeMask(player.PlayerModelId, out _);
        var vehicleContacts = nativeFootprint && (entity.Flags & WorldMapVehicleObstacles.SkippedFlag) == 0
            ? WorldMapVehicleShoreApproach.FindContactPoints(
                map, player.PlayerModelId, entity.ModelId, entity.X, entity.Z)
            : new Dictionary<int, WorldMapVertex>();
        if (nativeFootprint)
        {
            // Including when there is nothing: a boat out in open water, or one the native
            // routine skips, has no ground that boards it, and an approach it cannot be
            // boarded from is worse than none.
            foreach (var contact in vehicleContacts.Keys)
            {
                arrivals.Add(contact);
            }
        }
        else
        {
            // No mask for one of the two models - the Highwind, the submarines, or a party
            // leader nobody has measured. Inventing a footprint for those would be
            // inventing an arrival, so they keep the ring they always had.
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
        }

        // A vehicle parked beside a town walks the party onto that town's entrance if the
        // entrance triangle is offered as a way to reach it. The user's Buggy sat in
        // Gongaga's trigger cell, so every approach zoned into the jungle.
        //
        // There is no last resort here. Entering a field the player did not ask for is not
        // a way of reaching anything, so if the only ground beside this entity is a trigger
        // the target keeps no arrival at all and routing to it fails truthfully.
        arrivals.RemoveWhere(EntranceTriangleIds.Contains);
        var contactPoints = vehicleContacts
            .Where(contact => arrivals.Contains(contact.Key))
            .ToDictionary(contact => contact.Key, contact => contact.Value);

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
            arrivals)
        {
            VehicleContactPoints = contactPoints,
            NativeVehicleContact = contactPoints.Count > 0
                ? new WorldMapNativeVehicleContact(
                    entity.GuestPointer, entity.ModelId, entity.X, entity.Z,
                    map.WrapWidth, map.WrapHeight)
                : null
        };
    }

    private IReadOnlyList<WorldMapNavigationTarget> ReadStoryTargets(int gameMoment, WorldMapStateSnapshot? state)
    {
        var stage = StoryStages.FirstOrDefault(candidate =>
            gameMoment >= candidate.MinimumGameMoment &&
            gameMoment <= candidate.MaximumGameMoment);
        if (stage is null)
        {
            return [];
        }

        // A stage that only the airship reaches says nothing to a party on foot. A
        // caller that supplies no state cannot be told apart from one on foot, and the
        // stop is a real one, so it is still named: withholding a required destination
        // from a player who might already be flying is the worse of the two mistakes.
        // A native target is different - it cannot even be described without state.
        var named = stage.RequiredPlayerModelIds is { } requiredModels &&
                state is { } modelState && !requiredModels.Contains(modelState.PlayerModelId)
            ? Array.Empty<WorldMapNavigationTarget>().AsEnumerable()
            : stage.LocationLabels
                .Select(label => locationsByLabel.TryGetValue(label, out var location) ? location : null)
                .Where(location => location is not null)
                .Select(location => location! with
                {
                    Category = WorldMapNavigationCategory.Story,
                    Kind = WorldMapTargetKind.Story,
                    StableId = $"world-story:{CreateStableName(location.Label)}"
                });

        if (stage.NativeTarget is not { } native)
        {
            return named.ToArray();
        }

        // Each of these belongs to a native handler with its own conditions: the crater
        // flyover and landing are the Highwind's own script, and the Great Glacier's
        // northern edge runs only for a character walking. None of them exists on
        // another world module, so a caller with no state to check gets nothing rather
        // than a target it cannot qualify.
        if (state is not { } current ||
            !native.AllowedPlayerModelIds.Contains(current.PlayerModelId) ||
            current.WorldMapType != map.WorldMapType ||
            native.WorldMapType != map.WorldMapType ||
            current.GameMoment < native.MinimumGameMoment ||
            current.GameMoment > native.MaximumGameMoment)
        {
            return named.ToArray();
        }

        var resolved = ResolveNativeStoryTarget(native, current);
        return resolved is null ? named.ToArray() : named.Append(resolved).ToArray();
    }

    /// <summary>
    /// The Highwind's own world model. wm0.ev system function 9 pushes model 3 before
    /// calling the landing function, and the flyover proximity is measured from the
    /// active entity while that model is the one being flown.
    /// </summary>
    private const int HighwindModelId = 3;

    /// <summary>
    /// One mesh cell and terrain script of a native world-to-world boundary handler.
    /// </summary>
    private sealed record WorldStoryBoundaryCell(int MeshX, int MeshZ, int TerrainScriptId);

    /// <summary>
    /// Builds a Story target out of the installed map for a stop the trigger metadata
    /// leaves unresolved.
    /// </summary>
    private WorldMapNavigationTarget? ResolveNativeStoryTarget(
        WorldStoryNativeTarget native,
        WorldMapStateSnapshot state)
    {
        if (native.IsBoundarySet)
        {
            return ResolveNativeBoundaryTarget(native, state);
        }

        var arrivals = new HashSet<int>();
        var bestTriangle = -1;
        long bestDistance = long.MaxValue;
        long sumX = 0;
        long sumY = 0;
        long sumZ = 0;

        for (var index = 0; index < map.Triangles.Count; index++)
        {
            var triangle = map.Triangles[index];
            if (native.IsTerrainSet)
            {
                if (triangle.TerrainId != native.TerrainId)
                {
                    continue;
                }


                arrivals.Add(index);
                var centre = triangle.Centroid;
                sumX += centre.X;
                sumY += centre.Y;
                sumZ += centre.Z;
                continue;
            }

            // The native test is a wrapped Manhattan distance over all three axes.
            // Flight height is not knowable from the map alone, so what is computed
            // here is the horizontal part: a triangle outside this cannot satisfy the
            // native test, and one inside it still depends on the Highwind's own
            // altitude at the time.
            var centroid = triangle.Centroid;
            var distance =
                (long)WrappedDelta(centroid.X, native.X, map.WrapWidth) +
                WrappedDelta(centroid.Z, native.Z, map.WrapHeight);
            if (distance > WorldStoryNativeTarget.ManhattanBound)
            {
                continue;
            }

            arrivals.Add(index);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTriangle = index;
            }
        }

        if (arrivals.Count == 0)
        {
            return null;
        }

        int x, y, z, triangleId, regionId;
        if (native.IsTerrainSet)
        {
            x = (int)(sumX / arrivals.Count);
            y = (int)(sumY / arrivals.Count);
            z = (int)(sumZ / arrivals.Count);
            triangleId = ClosestTriangle(arrivals, x, z);
            regionId = map.Triangles[triangleId].RegionId;
        }
        else
        {
            x = native.X;
            z = native.Z;
            triangleId = bestTriangle;
            y = map.Triangles[triangleId].Centroid.Y;
            regionId = map.Triangles[triangleId].RegionId;
        }

        return new WorldMapNavigationTarget(
            WorldMapNavigationCategory.Story,
            WorldMapTargetKind.Story,
            native.Label,
            x,
            y,
            z,
            triangleId,
            regionId,
            $"world-story:{CreateStableName(native.Label)}",
            arrivals)
        {
            // The triangles above are where the target can be; this is what the game
            // actually tests before it does anything. Standing on a candidate triangle
            // is not arrival on its own.
            NativeStoryArrival = new WorldMapNativeStoryArrival(
                native.AllowedPlayerModelIds,
                map.WorldMapType,
                native.TerrainId,
                native.X,
                // Point 14 is initialised with height zero, and the native measure is
                // over all three axes, so an airship far above it is not near it.
                PointY: 0,
                native.Z,
                native.IsTerrainSet ? -1 : WorldStoryNativeTarget.ManhattanBound,
                map.WrapWidth,
                map.WrapHeight,
                native.MinimumGameMoment,
                native.MaximumGameMoment)
        };
    }

    /// <summary>
    /// A world-to-world exit resolved out of the installed map: every triangle in one of
    /// the boundary's mesh cells with its terrain script. All six of the Great Glacier's
    /// northern handlers do the same thing, so the one offered is whichever is nearest
    /// to where the party is actually standing.
    /// </summary>
    private WorldMapNavigationTarget? ResolveNativeBoundaryTarget(
        WorldStoryNativeTarget native,
        WorldMapStateSnapshot state)
    {
        var arrivals = new HashSet<int>();
        for (var index = 0; index < map.Triangles.Count; index++)
        {
            var triangle = map.Triangles[index];
            foreach (var cell in native.BoundaryCells)
            {
                if (triangle.MeshX == cell.MeshX &&
                    triangle.MeshZ == cell.MeshZ &&
                    triangle.TerrainScriptId == cell.TerrainScriptId)
                {
                    arrivals.Add(index);
                    break;
                }
            }
        }

        if (arrivals.Count == 0)
        {
            return null;
        }

        var triangleId = ClosestTriangle(arrivals, state.X, state.Z);
        var centre = map.Triangles[triangleId].Centroid;
        return new WorldMapNavigationTarget(
            WorldMapNavigationCategory.Story,
            WorldMapTargetKind.Story,
            native.Label,
            centre.X,
            centre.Y,
            centre.Z,
            triangleId,
            map.Triangles[triangleId].RegionId,
            $"world-story-native:{CreateStableName(native.Label)}",
            arrivals)
        {
            NativeStoryArrival = new WorldMapNativeStoryArrival(
                native.AllowedPlayerModelIds,
                map.WorldMapType,
                RequiredTerrainId: -1,
                PointX: 0,
                PointY: 0,
                PointZ: 0,
                ManhattanBound: -1,
                map.WrapWidth,
                map.WrapHeight,
                native.MinimumGameMoment,
                native.MaximumGameMoment)
        };
    }

    private int ClosestTriangle(IEnumerable<int> candidates, int x, int z)
    {
        var best = -1;
        long bestDistance = long.MaxValue;
        foreach (var index in candidates)
        {
            var centroid = map.Triangles[index].Centroid;
            var distance =
                (long)WrappedDelta(centroid.X, x, map.WrapWidth) +
                WrappedDelta(centroid.Z, z, map.WrapHeight);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
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
            .Where(triangle => triangle.HasChocoboTracks)
            .GroupBy(triangle => triangle.RegionId)
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
                var regionName = WorldMapRegionNames.TryGetName(group.Key, out var namedRegion)
                    ? namedRegion
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

    private static bool TryFindStrictInteriorPoint(
        WorldMapTriangle triangle,
        out WorldMapVertex point)
    {
        var centerX = (int)Math.Round(
            (triangle.Vertex0.X + (double)triangle.Vertex1.X + triangle.Vertex2.X) / 3d);
        var centerZ = (int)Math.Round(
            (triangle.Vertex0.Z + (double)triangle.Vertex1.Z + triangle.Vertex2.Z) / 3d);
        for (var radius = 0; radius <= 4; radius++)
        {
            for (var dz = -radius; dz <= radius; dz++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius ||
                        !ContainsPointStrict(triangle, centerX + dx, centerZ + dz))
                    {
                        continue;
                    }

                    point = new WorldMapVertex(
                        centerX + dx,
                        triangle.Centroid.Y,
                        centerZ + dz);
                    return true;
                }
            }
        }

        point = default;
        return false;
    }

    private static bool ContainsPointStrict(WorldMapTriangle triangle, int x, int z)
    {
        var first = SignedArea(x, z, triangle.Vertex0, triangle.Vertex1);
        var second = SignedArea(x, z, triangle.Vertex1, triangle.Vertex2);
        var third = SignedArea(x, z, triangle.Vertex2, triangle.Vertex0);
        if (first == 0 || second == 0 || third == 0)
        {
            return false;
        }

        var allNegative = first < 0 && second < 0 && third < 0;
        var allPositive = first > 0 && second > 0 && third > 0;
        return allNegative || allPositive;
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

    private sealed record NativeLocationTriggerDocument(
        int SchemaVersion,
        IReadOnlyList<NativeLocationTriggerRecord> Locations,
        IReadOnlyList<NativeUnresolvedLocationRecord> UnresolvedLocations);

    private sealed record NativeLocationTriggerRecord(
        int LocationId,
        string Label,
        int WorldMapType,
        int MeshX,
        int MeshY,
        int TerrainScriptId);

    private sealed record NativeUnresolvedLocationRecord(
        int LocationId,
        string Label,
        string Reason);

    private readonly record struct NativeLocationCandidate(
        WorldMapTriangle Triangle,
        WorldMapVertex Point);

    /// <param name="RequiredPlayerModelIds">
    /// When set, the named stops of this stage are only offered while one of these
    /// world models is being controlled. Most stages are places the party can walk to;
    /// a few are places only the airship reaches.
    /// </param>
    private sealed record WorldStoryStage(
        int MinimumGameMoment,
        int MaximumGameMoment,
        IReadOnlyList<string> LocationLabels,
        WorldStoryNativeTarget? NativeTarget = null,
        IReadOnlySet<int>? RequiredPlayerModelIds = null);

    /// <summary>
    /// A Story stop the native trigger metadata cannot name, described by what the
    /// game itself tests instead: a world point the Highwind's own script measures
    /// against, or the terrain the landing handler requires under the player.
    /// </summary>
    private sealed record WorldStoryNativeTarget(
        string Label,
        int TerrainId,
        int X,
        int Z,
        IReadOnlySet<int> AllowedPlayerModelIds,
        int WorldMapType,
        int MinimumGameMoment,
        int MaximumGameMoment,
        IReadOnlyList<WorldStoryBoundaryCell> BoundaryCells)
    {
        private static readonly IReadOnlySet<int> HighwindOnly = new HashSet<int> { HighwindModelId };
        private static readonly IReadOnlyList<WorldStoryBoundaryCell> NoCells = [];

        public static WorldStoryNativeTarget AtWorldPoint(
            string label, int x, int z, int minimumGameMoment, int maximumGameMoment) =>
            new(label, TerrainId: -1, x, z, HighwindOnly, 0, minimumGameMoment, maximumGameMoment, NoCells);

        public static WorldStoryNativeTarget OnTerrain(
            string label, int terrainId, int minimumGameMoment, int maximumGameMoment) =>
            new(label, terrainId, X: 0, Z: 0, HighwindOnly, 0, minimumGameMoment, maximumGameMoment, NoCells);

        /// <summary>
        /// A world-to-world exit: several equivalent native terrain handlers along one
        /// edge of a map, each entering the same place. There is no field behind it and
        /// no menu name for it, so it cannot be a catalog location; what identifies it
        /// is the mesh cells and terrain scripts its handlers belong to.
        /// </summary>
        public static WorldStoryNativeTarget AtBoundary(
            string label,
            int worldMapType,
            IReadOnlySet<int> allowedPlayerModelIds,
            int minimumGameMoment,
            int maximumGameMoment,
            IReadOnlyList<WorldStoryBoundaryCell> cells) =>
            new(label, TerrainId: -1, X: 0, Z: 0, allowedPlayerModelIds, worldMapType,
                minimumGameMoment, maximumGameMoment, cells);

        public bool IsTerrainSet => TerrainId >= 0;

        public bool IsBoundarySet => BoundaryCells.Count > 0;

        /// <summary>
        /// The native proximity bound for a world point. FUN_00764336 case 0x18 takes
        /// the wrapped three-axis Manhattan distance from FUN_00753C23 and shifts it
        /// right five before comparing it against 256, so the raw distance the script
        /// accepts is up to 8223 - not a 256-unit radius.
        /// </summary>
        public const int ManhattanBound = (256 << 5) + 31;
    }

    private sealed record TerrainAreaPatch(
        int TerrainId,
        int DominantRegionId,
        int RepresentativeTriangleId,
        double PlanarArea,
        IReadOnlyList<int> TriangleIds);
}


