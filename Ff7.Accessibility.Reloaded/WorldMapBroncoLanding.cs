using System.Runtime.CompilerServices;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// One place the Tiny Bronco can be left from: the boat at <see cref="X"/>,<see cref="Z"/>
/// facing <see cref="Rotation"/>, and the ground the game then puts the party on.
/// </summary>
internal readonly record struct WorldMapBroncoLandingOption(
    int WaterTriangleId,
    int X,
    int Y,
    int Z,
    int Rotation,
    int LandingX,
    int LandingZ,
    int LandingTriangleId);

/// <summary>
/// A shore landing chosen for one destination: where to take the boat, the straight run
/// in that lines it up, and the ground connected on foot to the destination.
/// </summary>
internal sealed record WorldMapBroncoLandingPlan(
    WorldMapBroncoLandingOption Option,
    WorldMapRouteWaypoint ApproachStart,
    IReadOnlySet<int> DestinationFootComponents,
    double BoatDistance,
    double FootDistance);

/// <summary>
/// The Tiny Bronco's native get-off, reproduced from the installed executable, so the mod
/// only ever tells the player to get off where pressing Cancel really puts them on the
/// way to where they are going.
///
/// <para>FUN_007667B2 starts the get-off when Cancel (0x40) is released after being held
/// for one to fourteen frames, and FUN_0076667C only if FUN_007666FF accepts the terrain
/// under the boat: mask 0x70, terrain 4, 5 or 6. Two frames later FUN_0075079D calls
/// FUN_00766417(0), which puts the boat back on its committed position (+0x1C) and moves
/// it 800 units along FUN_00761EEC - the model rotation at +0x3C plus the slide turn at
/// +0x3E. FUN_00751EFC then accepts the first of the move's fanned samples - the move
/// itself, then turned by 160, 320 ... 1120 angle units to one side and, after eight
/// frames, the other - whose five contact points, the centre and 350 units along each
/// axis, all stand on ground FUN_0074CECA accepts for the Bronco in get-off state 2: mask
/// 0x20800, terrain 11 riverside and 17 beach. FUN_007667B2 puts the party there and the
/// boat back. If no sample is accepted nothing happens at all.</para>
///
/// <para>The 2026-09-22 and 2026-09-23 logs hold three native get-offs. All three landings
/// have all five contact points on 11 or 17 and all three boats a water footprint, and the
/// two with the boat at rest landed 799 and 800 units from it.</para>
///
/// <para>Only the unturned sample is ever promised. Its landing does not depend on which
/// side the fan would try first, which the mod cannot see.</para>
/// </summary>
internal static class WorldMapBroncoLanding
{
    public const int BroncoModelId = 5;

    /// <summary>FUN_00766417: 800 units for the boat; 100 when entity flag 0x80 is set.</summary>
    public const int ProbeDistance = 800;

    /// <summary>FUN_00751EFC: the contact radius is 0x15E for model 5 (0xC8 otherwise).</summary>
    public const int ContactRadius = 350;

    /// <summary>
    /// Entity flag 0x80 gives the Bronco a 100-unit probe and a water-only landing mask.
    /// That is not the boat this plans for, so a flagged Bronco gets no landing at all.
    /// </summary>
    public const int ProbeVariantFlag = 0x80;

    /// <summary>
    /// How far either side of a landing's rotation it must still land on the same ground.
    /// The automatic approach presses one of eight directions: in both camera modes the
    /// model then faces within 256 units of the way it is going (FUN_0074EA48), so a landing
    /// that only works dead ahead is not one it can deliver.
    /// </summary>
    public const int RotationTolerance = 256;

    /// <summary>The straight run in, longest first, that lines the boat up with a landing.</summary>
    private static readonly int[] ApproachLengths = [1600, 1200, 800];

    private const int RotationStep = 64;
    private const int RotationSteps = 4096 / RotationStep;
    private const int SurfaceBucket = 256;
    private const int ShoreBucket = 512;

    private static readonly (int X, int Z)[] ContactOffsets =
        [(0, 0), (-ContactRadius, 0), (ContactRadius, 0), (0, -ContactRadius), (0, ContactRadius)];

    private static readonly ConditionalWeakTable<WorldMapData, SurfaceIndex> SurfaceIndexes = new();
    private static readonly ConditionalWeakTable<WorldMapRoutePlanner, LandingCatalog> Catalogs = new();

    /// <summary>FUN_0074CECA case 5, get-off state 2: mask 0x20800.</summary>
    public static bool IsLandingGround(int terrainId) => terrainId is 11 or 17;

    /// <summary>FUN_007666FF case 5 and FUN_0074CECA case 5 while sailing: mask 0x70.</summary>
    public static bool IsBoatWater(int terrainId) => terrainId is 4 or 5 or 6;

    /// <summary>
    /// Whether this snapshot is the Tiny Bronco in the form the landing rule describes, with
    /// the rotation the rule needs.
    /// </summary>
    public static bool CanPredict(WorldMapStateSnapshot state) =>
        state.PlayerModelId == BroncoModelId &&
        state.HasModelRotation &&
        (state.EntityFlags & ProbeVariantFlag) == 0;

    /// <summary>
    /// FUN_00766417's move: (0, 0, 800) turned by FUN_00753D00, whose Y rotation puts it at
    /// (800 sin, 800 cos) before the result is truncated.
    /// </summary>
    public static (int X, int Z) ProbeOffset(int rotation)
    {
        var radians = rotation * Math.PI * 2d / 4096d;
        return ((int)(ProbeDistance * Math.Sin(radians)), (int)(ProbeDistance * Math.Cos(radians)));
    }

    /// <summary>
    /// Where the unturned sample puts the party, if the game accepts it: all five contact
    /// points on landing ground.
    /// </summary>
    public static bool TryPredictDirectLanding(
        WorldMapData map,
        int boatX,
        int boatZ,
        int rotation,
        out WorldMapTriangle landingTriangle,
        out int landingX,
        out int landingZ)
    {
        ArgumentNullException.ThrowIfNull(map);
        var (offsetX, offsetZ) = ProbeOffset(rotation);
        landingX = Normalize(boatX + offsetX, map.WrapWidth);
        landingZ = Normalize(boatZ + offsetZ, map.WrapHeight);
        return IsLandingFootprint(map, landingX, landingZ, out landingTriangle);
    }

    /// <summary>
    /// Whether FUN_00751EFC would accept a Bronco get-off sample centred here: all five
    /// contact points on landing ground. The centre's triangle is where the party stands.
    /// </summary>
    public static bool IsLandingFootprint(WorldMapData map, int x, int z, out WorldMapTriangle centre)
    {
        ArgumentNullException.ThrowIfNull(map);
        centre = null!;
        foreach (var (contactX, contactZ) in ContactOffsets)
        {
            if (!TryFindSurface(map, x + contactX, z + contactZ, out var surface) ||
                !IsLandingGround(surface.TerrainId))
            {
                centre = null!;
                return false;
            }

            if (contactX == 0 && contactZ == 0)
            {
                centre = surface;
            }
        }

        return true;
    }

    /// <summary>The native surface height at a point on a triangle, as FUN_0076085F takes it.</summary>
    public static int SurfaceHeight(WorldMapTriangle triangle, int x, int z) =>
        (int)Math.Round(HeightAt(triangle, x, z));

    /// <summary>
    /// Whether the boat can sit here at all: its own five contact points on the water
    /// FUN_0074CECA lets it move on.
    /// </summary>
    public static bool HasBoatFootprint(WorldMapData map, int x, int z)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var (contactX, contactZ) in ContactOffsets)
        {
            if (!TryFindSurface(map, x + contactX, z + contactZ, out var surface) ||
                !IsBoatWater(surface.TerrainId))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The native ground under a point: FUN_0074CC07 over the point's own mesh cell, with
    /// FUN_0076085F's edge-inclusive test, keeping the lowest surface as it does for model 5.
    /// </summary>
    public static bool TryFindSurface(WorldMapData map, int x, int z, out WorldMapTriangle triangle)
    {
        ArgumentNullException.ThrowIfNull(map);
        triangle = null!;
        x = Normalize(x, map.WrapWidth);
        z = Normalize(z, map.WrapHeight);
        var index = SurfaceIndexes.GetValue(map, static built => new SurfaceIndex(built));
        if (!index.Buckets.TryGetValue((x / SurfaceBucket, z / SurfaceBucket), out var candidates))
        {
            return false;
        }

        var meshX = x / WorldMapDataLoader.MeshSize;
        var meshZ = z / WorldMapDataLoader.MeshSize;
        var lowest = double.PositiveInfinity;
        foreach (var id in candidates)
        {
            var candidate = map.Triangles[id];
            if (candidate.MeshX != meshX || candidate.MeshZ != meshZ || !IsInsideNative(candidate, x, z))
            {
                continue;
            }

            var height = HeightAt(candidate, x, z);
            if (height < lowest)
            {
                lowest = height;
                triangle = candidate;
            }
        }

        return triangle is not null;
    }

    /// <summary>
    /// The native ground under a point for a model that walks: FUN_0074CC07 over the point's
    /// own mesh cell, with FUN_0076085F's edge-inclusive test, keeping the surface nearest
    /// the party's own height as it does for every model but 3 and 5 - on a bridge the deck,
    /// not the gorge beneath it.
    /// </summary>
    public static bool TryFindSurfaceNear(
        WorldMapData map,
        int x,
        int z,
        int referenceHeight,
        out WorldMapTriangle triangle)
    {
        ArgumentNullException.ThrowIfNull(map);
        triangle = null!;
        x = Normalize(x, map.WrapWidth);
        z = Normalize(z, map.WrapHeight);
        var index = SurfaceIndexes.GetValue(map, static built => new SurfaceIndex(built));
        if (!index.Buckets.TryGetValue((x / SurfaceBucket, z / SurfaceBucket), out var candidates))
        {
            return false;
        }

        var meshX = x / WorldMapDataLoader.MeshSize;
        var meshZ = z / WorldMapDataLoader.MeshSize;
        var nearest = double.PositiveInfinity;
        foreach (var id in candidates)
        {
            var candidate = map.Triangles[id];
            if (candidate.MeshX != meshX || candidate.MeshZ != meshZ || !IsInsideNative(candidate, x, z))
            {
                continue;
            }

            var gap = Math.Abs(HeightAt(candidate, x, z) - referenceHeight);
            if (gap < nearest)
            {
                nearest = gap;
                triangle = candidate;
            }
        }

        return triangle is not null;
    }

    /// <summary>
    /// Whether the boat as it is now would land the party on ground joined on foot to the
    /// destination, and keep doing so while its rotation settles.
    ///
    /// <para>The rotation the probe uses is +0x3C plus +0x3E, and at rest the game eases
    /// +0x3C to the facing at +0x40 (FUN_0076420A through FUN_00761C07) and +0x3E to nothing
    /// (FUN_00761DF5). Every rotation on the way from one to the other, with a margin, has to
    /// land on the same side, or the answer would depend on how soon the player pressed.</para>
    /// </summary>
    public static bool IsLinedUp(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        WorldMapStateSnapshot state,
        IReadOnlySet<int> destinationFootComponents,
        out WorldMapTriangle landingTriangle)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(destinationFootComponents);
        landingTriangle = null!;
        if (!CanPredict(state) || !IsBoatWater(state.TerrainId))
        {
            return false;
        }

        var current = state.ModelRotation + state.SlideRotation;
        var settled = (int)state.Facing;
        var arc = ShortestArc(current, settled);
        const int margin = 32;
        var steps = Math.Max(1, (Math.Abs(arc) + margin * 2 + 15) / 16);
        var from = current - Math.Sign(arc == 0 ? 1 : arc) * margin;
        var span = arc + Math.Sign(arc == 0 ? 1 : arc) * margin * 2;
        for (var step = 0; step <= steps; step++)
        {
            var rotation = from + (int)Math.Round(span * (step / (double)steps));
            if (!TryPredictDirectLanding(map, state.X, state.Z, rotation, out var landing, out _, out _) ||
                landing.TerrainScriptId >= 3 ||
                !destinationFootComponents.Contains(
                    planner.GetComponentId(0, map.WorldMapType, landing.Id)))
            {
                return false;
            }
        }

        return TryPredictDirectLanding(map, state.X, state.Z, settled, out landingTriangle, out _, out _);
    }

    /// <summary>
    /// Whether any landing the boat can reach from here leads on foot to this destination.
    /// Cheap after the first call on a map, so the target list can ask it for every town.
    /// </summary>
    public static bool HasLanding(
        WorldMapRoutePlanner planner,
        WorldMapData map,
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target)
    {
        if (!TryResolveBoat(planner, map, state, out _, out var boatComponent))
        {
            return false;
        }

        var destination = DestinationFootComponents(planner, map, target);
        var catalog = Catalogs.GetValue(planner, built => new LandingCatalog(built, map));
        return catalog.FootComponentsByBoatComponent.TryGetValue(boatComponent, out var reachable) &&
               destination.Overlaps(reachable);
    }

    /// <summary>
    /// The landing for this destination with the shortest boat trip plus walk, and the
    /// straight run in that lines the boat up with it.
    /// </summary>
    public static bool TryPlan(
        WorldMapRoutePlanner planner,
        WorldMapData map,
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target,
        out WorldMapBroncoLandingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);
        plan = null!;
        if (!TryResolveBoat(planner, map, state, out var boatTriangle, out var boatComponent))
        {
            return false;
        }

        var destination = DestinationFootComponents(planner, map, target);
        if (destination.Count == 0)
        {
            return false;
        }

        var catalog = Catalogs.GetValue(planner, built => new LandingCatalog(built, map));
        var candidates = catalog.Options
            .Where(option =>
                planner.GetComponentId(BroncoModelId, map.WorldMapType, option.WaterTriangleId) == boatComponent &&
                destination.Contains(planner.GetComponentId(0, map.WorldMapType, option.LandingTriangleId)))
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        var boatDistances = MeasureDistances(
            map,
            [boatTriangle],
            id => WorldMapTerrainPassability.CanTraverse(BroncoModelId, map.WorldMapType, map.Triangles[id].TerrainId));
        var exemptions = target.NativeEntranceExemptions;
        var footDistances = MeasureDistances(
            map,
            target.ArrivalTriangleIds.Where(id =>
                id >= 0 && id < map.Triangles.Count &&
                WorldMapTerrainPassability.CanTraverse(0, map.WorldMapType, map.Triangles[id].TerrainId)),
            id => WorldMapTerrainPassability.CanTraverse(0, map.WorldMapType, map.Triangles[id].TerrainId) &&
                  !planner.IsUnwantedEntrance(id, exemptions));

        WorldMapBroncoLandingPlan? best = null;
        var bestCost = double.PositiveInfinity;
        // The trip is judged by time, not distance: FUN_0074EA48 moves the Bronco 0x3C a
        // frame and the party on foot 0x1E, so a unit sailed costs half a unit walked. From
        // the 08:42:12 boat the Weapon Seller's own beach is 23,075 sailed and 1,772 walked;
        // adding distances as equals preferred 2,601 sailed and 12,149 walked instead.
        double Cost(WorldMapBroncoLandingOption option) =>
            boatDistances[option.WaterTriangleId] / 2d + footDistances[option.LandingTriangleId];

        foreach (var option in candidates.OrderBy(Cost))
        {
            var boatDistance = boatDistances[option.WaterTriangleId];
            var footDistance = footDistances[option.LandingTriangleId];
            var cost = Cost(option);
            if (double.IsPositiveInfinity(cost) ||
                cost >= bestCost ||
                !TryFindApproachStart(planner, map, option, out var approachStart))
            {
                continue;
            }

            bestCost = cost;
            best = new WorldMapBroncoLandingPlan(option, approachStart, destination, boatDistance, footDistance);
        }

        if (best is null)
        {
            return false;
        }

        plan = best;
        return true;
    }

    /// <summary>
    /// The on-foot components a destination can be walked to in: those of its own arrival
    /// triangles the party can stand on.
    /// </summary>
    public static IReadOnlySet<int> DestinationFootComponents(
        WorldMapRoutePlanner planner,
        WorldMapData map,
        WorldMapNavigationTarget target) =>
        target.ArrivalTriangleIds
            .Where(id => id >= 0 && id < map.Triangles.Count &&
                         WorldMapTerrainPassability.CanTraverse(0, map.WorldMapType, map.Triangles[id].TerrainId))
            .Select(id => planner.GetComponentId(0, map.WorldMapType, id))
            .Where(component => component >= 0)
            .ToHashSet();

    /// <summary>
    /// A point far enough back along the landing's own heading that the boat covers a
    /// straight run on it before it stops, on water it can sit on, with that run clear.
    /// </summary>
    private static bool TryFindApproachStart(
        WorldMapRoutePlanner planner,
        WorldMapData map,
        WorldMapBroncoLandingOption option,
        out WorldMapRouteWaypoint start)
    {
        var radians = option.Rotation * Math.PI * 2d / 4096d;
        foreach (var length in ApproachLengths)
        {
            var x = Normalize(option.X - (int)Math.Round(length * Math.Sin(radians)), map.WrapWidth);
            var z = Normalize(option.Z - (int)Math.Round(length * Math.Cos(radians)), map.WrapHeight);
            if (!HasBoatFootprint(map, x, z) || !TryFindSurface(map, x, z, out var surface))
            {
                continue;
            }

            var from = new WorldMapStateSnapshot(
                WorldMapStateReader.WorldModule, map.WorldMapType, 0, 0,
                x, (int)Math.Round(HeightAt(surface, x, z)), z, 0, 0, surface.TerrainId, 0,
                BroncoModelId, 0, 0, new FieldNavigationControlTransform(0))
            {
                TerrainScriptId = surface.TerrainScriptId
            };
            if (planner.CanTraverseSegment(from, new WorldMapRouteWaypoint(option.X, option.Y, option.Z)) &&
                IsSailable(map, x, z, option.X, option.Z))
            {
                start = new WorldMapRouteWaypoint(x, from.Y, z);
                return true;
            }
        }

        start = default;
        return false;
    }

    /// <summary>
    /// Whether the boat keeps its whole footprint on water along a straight line, one native
    /// frame of movement (0x3C) at a time.
    /// </summary>
    private static bool IsSailable(WorldMapData map, int fromX, int fromZ, int toX, int toZ)
    {
        var dx = (double)WorldMapTargetCatalog.WrappedDelta(fromX, toX, map.WrapWidth);
        var dz = (double)WorldMapTargetCatalog.WrappedDelta(fromZ, toZ, map.WrapHeight);
        var samples = (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / 60d);
        for (var sample = 1; sample < samples; sample++)
        {
            var fraction = sample / (double)samples;
            if (!HasBoatFootprint(map, fromX + (int)Math.Round(dx * fraction), fromZ + (int)Math.Round(dz * fraction)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveBoat(
        WorldMapRoutePlanner planner,
        WorldMapData map,
        WorldMapStateSnapshot state,
        out int boatTriangle,
        out int boatComponent)
    {
        boatComponent = -1;
        // No rotation, no promise: the landing is wherever the boat is facing, and a plan
        // that could never be confirmed at the shore would end in a guess.
        if (!CanPredict(state) ||
            !planner.TryResolvePlayerTriangle(state, out boatTriangle))
        {
            boatTriangle = -1;
            return false;
        }

        boatComponent = planner.GetComponentId(BroncoModelId, map.WorldMapType, boatTriangle);
        return boatComponent >= 0;
    }

    private static double[] MeasureDistances(
        WorldMapData map,
        IEnumerable<int> sources,
        Func<int, bool> canEnter)
    {
        var distances = new double[map.Triangles.Count];
        Array.Fill(distances, double.PositiveInfinity);
        var frontier = new PriorityQueue<int, double>();
        foreach (var source in sources)
        {
            if (distances[source] > 0d)
            {
                distances[source] = 0d;
                frontier.Enqueue(source, 0d);
            }
        }

        while (frontier.TryDequeue(out var current, out var distance))
        {
            if (distance > distances[current])
            {
                continue;
            }

            var from = map.Triangles[current].Centroid;
            foreach (var neighbor in map.Triangles[current].Neighbors)
            {
                if (!canEnter(neighbor))
                {
                    continue;
                }

                var to = map.Triangles[neighbor].Centroid;
                var dx = (double)WorldMapTargetCatalog.WrappedDelta(from.X, to.X, map.WrapWidth);
                var dz = (double)WorldMapTargetCatalog.WrappedDelta(from.Z, to.Z, map.WrapHeight);
                var next = distance + Math.Sqrt(dx * dx + dz * dz);
                if (distances[neighbor] <= next)
                {
                    continue;
                }

                distances[neighbor] = next;
                frontier.Enqueue(neighbor, next);
            }
        }

        return distances;
    }

    /// <summary>
    /// FUN_0076085F: the point is on the triangle when no edge's cross product is positive,
    /// edges included. The installed map is wound for it - all but 235 of 142,340
    /// triangles with area hold their own centroid under this test.
    /// </summary>
    private static bool IsInsideNative(WorldMapTriangle triangle, int x, int z) =>
        EdgeCross(triangle.Vertex0, triangle.Vertex1, x, z) <= 0 &&
        EdgeCross(triangle.Vertex1, triangle.Vertex2, x, z) <= 0 &&
        EdgeCross(triangle.Vertex2, triangle.Vertex0, x, z) <= 0;

    private static long EdgeCross(WorldMapVertex from, WorldMapVertex to, int x, int z) =>
        (long)(to.Z - from.Z) * (x - from.X) - (long)(to.X - from.X) * (z - from.Z);

    private static double HeightAt(WorldMapTriangle triangle, int x, int z)
    {
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
        if (Math.Abs(denominator) < 1e-9)
        {
            return (a.Y + b.Y + c.Y) / 3d;
        }

        var weightA = ((b.Z - c.Z) * (double)(x - c.X) + (c.X - b.X) * (double)(z - c.Z)) / denominator;
        var weightB = ((c.Z - a.Z) * (double)(x - c.X) + (a.X - c.X) * (double)(z - c.Z)) / denominator;
        return weightA * a.Y + weightB * b.Y + (1d - weightA - weightB) * c.Y;
    }

    private static int ShortestArc(int from, int to)
    {
        var arc = (to - from) % 4096;
        if (arc > 2048)
        {
            arc -= 4096;
        }
        else if (arc < -2048)
        {
            arc += 4096;
        }

        return arc;
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

    private sealed class SurfaceIndex
    {
        public SurfaceIndex(WorldMapData map)
        {
            foreach (var triangle in map.Triangles)
            {
                var minX = Math.Min(triangle.Vertex0.X, Math.Min(triangle.Vertex1.X, triangle.Vertex2.X));
                var maxX = Math.Max(triangle.Vertex0.X, Math.Max(triangle.Vertex1.X, triangle.Vertex2.X));
                var minZ = Math.Min(triangle.Vertex0.Z, Math.Min(triangle.Vertex1.Z, triangle.Vertex2.Z));
                var maxZ = Math.Max(triangle.Vertex0.Z, Math.Max(triangle.Vertex1.Z, triangle.Vertex2.Z));
                for (var bucketX = minX / SurfaceBucket; bucketX <= maxX / SurfaceBucket; bucketX++)
                {
                    for (var bucketZ = minZ / SurfaceBucket; bucketZ <= maxZ / SurfaceBucket; bucketZ++)
                    {
                        if (!Buckets.TryGetValue((bucketX, bucketZ), out var bucket))
                        {
                            bucket = [];
                            Buckets[(bucketX, bucketZ)] = bucket;
                        }

                        bucket.Add(triangle.Id);
                    }
                }
            }
        }

        public Dictionary<(int X, int Z), List<int>> Buckets { get; } = [];
    }

    /// <summary>
    /// Every landing the map offers, found once: from the middle of each patch of water the
    /// boat fits on, each heading whose unturned probe lands, and keeps landing on the same
    /// walkable ground for <see cref="RotationTolerance"/> either side.
    /// </summary>
    private sealed class LandingCatalog
    {
        public LandingCatalog(WorldMapRoutePlanner planner, WorldMapData map)
        {
            var options = new List<WorldMapBroncoLandingOption>();
            var reach = new Dictionary<int, HashSet<int>>();
            var tolerance = RotationTolerance / RotationStep;
            var landings = new WorldMapTriangle?[RotationSteps];

            // Where riverside and beach are at all, coarsely. A landing's contact points lie
            // within ProbeDistance + ContactRadius of the boat, so water with none of this
            // ground in reach cannot land anybody and is not probed sixty-four times over.
            var shore = new HashSet<(int X, int Z)>();
            foreach (var ground in map.Triangles)
            {
                if (!IsLandingGround(ground.TerrainId))
                {
                    continue;
                }

                var minX = Math.Min(ground.Vertex0.X, Math.Min(ground.Vertex1.X, ground.Vertex2.X)) / ShoreBucket;
                var maxX = Math.Max(ground.Vertex0.X, Math.Max(ground.Vertex1.X, ground.Vertex2.X)) / ShoreBucket;
                var minZ = Math.Min(ground.Vertex0.Z, Math.Min(ground.Vertex1.Z, ground.Vertex2.Z)) / ShoreBucket;
                var maxZ = Math.Max(ground.Vertex0.Z, Math.Max(ground.Vertex1.Z, ground.Vertex2.Z)) / ShoreBucket;
                for (var bucketX = minX; bucketX <= maxX; bucketX++)
                {
                    for (var bucketZ = minZ; bucketZ <= maxZ; bucketZ++)
                    {
                        shore.Add((bucketX, bucketZ));
                    }
                }
            }

            bool IsShoreInReach(int x, int z)
            {
                const int reachUnits = ProbeDistance + ContactRadius;
                for (var bucketX = Math.Max(0, x - reachUnits) / ShoreBucket;
                     bucketX <= (x + reachUnits) / ShoreBucket;
                     bucketX++)
                {
                    for (var bucketZ = Math.Max(0, z - reachUnits) / ShoreBucket;
                         bucketZ <= (z + reachUnits) / ShoreBucket;
                         bucketZ++)
                    {
                        if (shore.Contains((bucketX, bucketZ)))
                        {
                            return true;
                        }
                    }
                }

                // Across the wrap the coarse test is skipped rather than reproduced.
                return x - reachUnits < 0 || z - reachUnits < 0 ||
                       x + reachUnits >= map.WrapWidth || z + reachUnits >= map.WrapHeight;
            }

            foreach (var water in map.Triangles)
            {
                if (!IsBoatWater(water.TerrainId))
                {
                    continue;
                }

                var x = (water.Vertex0.X + water.Vertex1.X + water.Vertex2.X) / 3;
                var z = (water.Vertex0.Z + water.Vertex1.Z + water.Vertex2.Z) / 3;
                if (!IsShoreInReach(x, z) ||
                    !TryFindSurface(map, x, z, out var surface) || surface.Id != water.Id ||
                    !HasBoatFootprint(map, x, z))
                {
                    continue;
                }

                var any = false;
                for (var step = 0; step < RotationSteps; step++)
                {
                    landings[step] =
                        TryPredictDirectLanding(map, x, z, step * RotationStep, out var landing, out _, out _) &&
                        landing.TerrainScriptId < 3
                            ? landing
                            : null;
                    any |= landings[step] is not null;
                }

                if (!any)
                {
                    continue;
                }

                var y = (int)Math.Round(HeightAt(water, x, z));
                var boatComponent = planner.GetComponentId(BroncoModelId, map.WorldMapType, water.Id);
                for (var step = 0; step < RotationSteps; step++)
                {
                    if (landings[step] is not { } landing)
                    {
                        continue;
                    }

                    var footComponent = planner.GetComponentId(0, map.WorldMapType, landing.Id);
                    var robust = footComponent >= 0;
                    for (var offset = -tolerance; robust && offset <= tolerance; offset++)
                    {
                        robust = landings[(step + offset + RotationSteps) % RotationSteps] is { } neighbor &&
                                 planner.GetComponentId(0, map.WorldMapType, neighbor.Id) == footComponent;
                    }

                    if (!robust)
                    {
                        continue;
                    }

                    var (offsetX, offsetZ) = ProbeOffset(step * RotationStep);
                    options.Add(new WorldMapBroncoLandingOption(
                        water.Id, x, y, z, step * RotationStep,
                        Normalize(x + offsetX, map.WrapWidth), Normalize(z + offsetZ, map.WrapHeight),
                        landing.Id));
                    if (boatComponent >= 0)
                    {
                        if (!reach.TryGetValue(boatComponent, out var feet))
                        {
                            feet = [];
                            reach[boatComponent] = feet;
                        }

                        feet.Add(footComponent);
                    }
                }
            }

            Options = options;
            FootComponentsByBoatComponent = reach.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<int>)pair.Value);
        }

        public IReadOnlyList<WorldMapBroncoLandingOption> Options { get; }

        public IReadOnlyDictionary<int, IReadOnlySet<int>> FootComponentsByBoatComponent { get; }
    }
}
