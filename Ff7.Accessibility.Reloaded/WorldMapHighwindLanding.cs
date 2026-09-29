namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Where to bring the Highwind down for one destination: the ship's spot on grass, and the
/// on-foot components the party is set down in from there.
/// </summary>
internal sealed record WorldMapHighwindLandingPlan(
    int ShipX,
    int ShipY,
    int ShipZ,
    int ShipTriangleId,
    IReadOnlySet<int> DestinationFootComponents,
    double FlightDistance,
    double FootDistance);

/// <summary>
/// The Highwind's native landing, reproduced from the installed executable, so the mod only
/// ever tells the player to land where the game's own get-off puts the party on the way to
/// where they are going. Nothing here presses anything: landing is the player's Cancel.
///
/// <para>FUN_007667B2 starts the get-off when Cancel (0x40) is released after being held for
/// one to fourteen frames, and FUN_0076667C only if FUN_007666FF accepts the terrain under
/// the ship: case 3, terrain 0, grass. While it descends FUN_0074CECA case 3 lets the ship
/// move only over terrain 0 (DAT_00de6a08 below zero), so the whole of its own five-point
/// footprint - the centre and 0xC8 along each axis (FUN_00751EFC) - is asked to be grass.
/// Then FUN_00766417 moves the party 300 units along FUN_00761EEC's rotation turned by
/// 0x400, and FUN_00751EFC accepts the first fanned sample whose five contact points stand
/// on ground FUN_0074CECA case 3 accepts in get-off state 2: mask 0x21B6F83, and not a
/// type byte whose top three bits are all set. If none fits the ship takes off again.</para>
///
/// <para>The flight rotation does not follow the camera (the 2026-09-29 x64 flight logs the
/// facing anywhere from half a turn to an eighth of a turn from the heading), so the side
/// the party is set down on cannot be foretold when the spot is chosen. A spot is chosen
/// only where the unturned sample fits, on the destination's own ground, at 64 rotations a
/// sixty-fourth of a turn apart - a sample, not every angle. Before the landing is asked
/// for, the ship's actual live rotation (+0x3C plus +0x3E) and every angle it can still
/// ease through to its facing are checked one by one, so a sliver between the samples is
/// never promised.</para>
///
/// <para>The parked ship keeps its native mask, 00 00 18 3C 3C 18 00 00 at 0096DDB0 + 3 * 8
/// - the Buggy's - and FUN_00762A21 refuses a step that overlaps it and closes on it. A spot
/// whose reach covers the destination's own arrival ground would park the ship across the
/// way in, so none is offered.</para>
/// </summary>
internal static class WorldMapHighwindLanding
{
    public const int HighwindModelId = 3;

    /// <summary>FUN_007666FF case 3: the ship lands only over terrain 0.</summary>
    public const int LandingTerrainId = 0;

    /// <summary>FUN_00766417, every model but 5 and 13: 300 units.</summary>
    public const int DisembarkDistance = 300;

    /// <summary>FUN_00766417 turns FUN_00761EEC's rotation by 0x400 before moving the party.</summary>
    public const int DisembarkTurn = 0x400;

    /// <summary>FUN_00751EFC: 0x15E for model 5, 0xC8 for every other model.</summary>
    public const int ContactRadius = 200;

    /// <summary>FUN_0074CECA case 3, get-off state 2.</summary>
    public const uint DisembarkGroundMask = 0x21B6F83;

    /// <summary>FUN_00762A21 compares masks only inside 1024 wrapped units on both axes.</summary>
    public const int ParkedReach = 1024;

    /// <summary>The rotations a spot is sampled at when it is chosen, one every 64 of 4096.</summary>
    private const int RotationStep = 64;

    /// <summary>How many spots are examined, nearest by time first, before giving up.</summary>
    private const int MaximumCandidates = 6000;

    /// <summary>How many of those may be proved with a real route on foot.</summary>
    private const int MaximumRouteProofs = 24;

    /// <summary>
    /// How far the ship may come to rest from its spot and still land the same way. Released,
    /// the flying speed (0x78 a frame on each axis) eases by a quarter a frame, and a host
    /// sample spans several frames, so it seldom stops exactly on the spot.
    /// </summary>
    private static readonly int[] DriftRadii = [256, 512];

    /// <summary>After the first spot that fits, how many more are looked at for one that also fits around it.</summary>
    private const int DriftSearchCandidates = 400;

    /// <summary>
    /// The trip is judged by time: FUN_0074EA48 moves the flying ship 0x78 a frame and the
    /// party 0x1E, so a unit flown costs a quarter of a unit walked.
    /// </summary>
    private const double FlightCostPerUnit = 30d / 120d;

    private static readonly (int X, int Z)[] ContactOffsets =
        [(0, 0), (-ContactRadius, 0), (ContactRadius, 0), (0, -ContactRadius), (0, ContactRadius)];

    public static bool IsLandingTerrain(int terrainId) => terrainId == LandingTerrainId;

    /// <summary>FUN_0074CECA case 3, get-off state 2: the walking mask, not script type 7.</summary>
    public static bool IsDisembarkGround(WorldMapTriangle triangle) =>
        triangle.TerrainId is >= 0 and < 32 &&
        ((DisembarkGroundMask >> triangle.TerrainId) & 1) != 0 &&
        triangle.TerrainScriptId != 7;

    /// <summary>
    /// FUN_00766417's move: (0, 0, 300) turned by FUN_00753D00 through rotation + 0x400,
    /// which puts it at (300 sin, 300 cos) before the result is truncated.
    /// </summary>
    public static (int X, int Z) DisembarkOffset(int rotation)
    {
        var radians = (rotation + DisembarkTurn) * Math.PI * 2d / 4096d;
        return ((int)(DisembarkDistance * Math.Sin(radians)), (int)(DisembarkDistance * Math.Cos(radians)));
    }

    /// <summary>Whether the ship's own five contact points are all over grass.</summary>
    public static bool HasShipFootprint(WorldMapData map, int x, int z)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var (contactX, contactZ) in ContactOffsets)
        {
            if (!WorldMapBroncoLanding.TryFindSurface(map, x + contactX, z + contactZ, out var surface) ||
                !IsLandingTerrain(surface.TerrainId))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Where the unturned sample puts the party for this rotation, if FUN_00751EFC accepts
    /// it: all five contact points on get-off ground. The centre is where the party stands.
    /// </summary>
    public static bool TryPredictDisembark(
        WorldMapData map,
        int shipX,
        int shipZ,
        int rotation,
        out WorldMapTriangle ground,
        out int groundX,
        out int groundZ)
    {
        ArgumentNullException.ThrowIfNull(map);
        var (offsetX, offsetZ) = DisembarkOffset(rotation);
        groundX = Normalize(shipX + offsetX, map.WrapWidth);
        groundZ = Normalize(shipZ + offsetZ, map.WrapHeight);
        ground = null!;
        foreach (var (contactX, contactZ) in ContactOffsets)
        {
            if (!WorldMapBroncoLanding.TryFindSurface(map, groundX + contactX, groundZ + contactZ, out var surface) ||
                !IsDisembarkGround(surface))
            {
                ground = null!;
                return false;
            }

            if (contactX == 0 && contactZ == 0)
            {
                ground = surface;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether landing here sets the party down on the destination's own ground at every
    /// sampled rotation (<see cref="RotationStep"/> apart): the ship's footprint over grass,
    /// and the unturned get-off sample on get-off ground, off every entrance trigger, in one
    /// of <paramref name="destinationFootComponents"/>. Angles between the samples are not
    /// proved here; <see cref="IsReadyToLand"/> checks the live ones exactly.
    /// </summary>
    public static bool FitsEveryRotation(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        int shipX,
        int shipZ,
        IReadOnlySet<int> destinationFootComponents,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(destinationFootComponents);
        if (!HasShipFootprint(map, shipX, shipZ))
        {
            failure = "the ship's footprint is not all grass";
            return false;
        }

        for (var rotation = 0; rotation < 4096; rotation += RotationStep)
        {
            if (!TryPredictDisembark(map, shipX, shipZ, rotation, out var ground, out var groundX, out var groundZ))
            {
                failure = $"rotation {rotation} sets the party down at {groundX},{groundZ}, off get-off ground";
                return false;
            }

            // The Tiny Bronco's rule too: a script handler from 3 up is an entrance, and a
            // party set down on one is not where the player asked to go.
            if (ground.TerrainScriptId >= 3 ||
                !destinationFootComponents.Contains(planner.GetComponentId(0, map.WorldMapType, ground.Id)))
            {
                failure = $"rotation {rotation} sets the party down on triangle {ground.Id}, not joined on foot to the destination";
                return false;
            }
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether this Highwind state is where a manual landing now does what the plan promises:
    /// over grass by the ship's own live terrain, every sampled rotation fitting from here, and
    /// - exactly, angle by angle - the live rotation and every rotation it can still ease
    /// through to its facing. No readable rotation, no promise.
    /// </summary>
    public static bool IsReadyToLand(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        WorldMapStateSnapshot state,
        IReadOnlySet<int> destinationFootComponents) =>
        state.PlayerModelId == HighwindModelId &&
        IsLandingTerrain(state.TerrainId) &&
        FitsEveryRotation(map, planner, state.X, state.Z, destinationFootComponents, out _) &&
        FitsLiveRotation(map, planner, state, destinationFootComponents, out _);

    /// <summary>
    /// The live rotation FUN_00766417 will use - FUN_00761EEC, +0x3C plus +0x3E - and every
    /// whole angle between it and the facing at +0x40 it eases to, with 32 either side: each
    /// one's unturned get-off on the destination's ground.
    /// </summary>
    public static bool FitsLiveRotation(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        WorldMapStateSnapshot state,
        IReadOnlySet<int> destinationFootComponents,
        out string failure)
    {
        if (!state.HasModelRotation)
        {
            failure = "the ship's rotation is not readable";
            return false;
        }

        var current = state.ModelRotation + state.SlideRotation;
        var arc = ((state.Facing - current) % 4096 + 4096 + 2048) % 4096 - 2048;
        const int margin = 32;
        var from = arc >= 0 ? current - margin : current + margin;
        var span = arc >= 0 ? arc + 2 * margin : arc - 2 * margin;
        for (var step = 0; step <= Math.Abs(span); step++)
        {
            var rotation = from + Math.Sign(span == 0 ? 1 : span) * step;
            if (!TryPredictDisembark(map, state.X, state.Z, rotation, out var ground, out _, out _) ||
                ground.TerrainScriptId >= 3 ||
                !destinationFootComponents.Contains(planner.GetComponentId(0, map.WorldMapType, ground.Id)))
            {
                failure = $"live rotation {rotation & 4095} does not set the party down on the destination's ground";
                return false;
            }
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// The landing for this destination with the least time flown plus walked, proved with
    /// a real route on foot from the ground it sets the party down on.
    /// </summary>
    public static bool TryPlan(
        WorldMapRoutePlanner planner,
        WorldMapData map,
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target,
        out WorldMapHighwindLandingPlan plan,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);
        plan = null!;
        var destination = WorldMapBroncoLanding.DestinationFootComponents(planner, map, target);
        if (destination.Count == 0)
        {
            diagnostic = $"{target.Label} has no arrival ground the party can stand on";
            return false;
        }

        var exemptions = target.NativeEntranceExemptions;
        var arrivals = target.ArrivalTriangleIds
            .Where(id => id >= 0 && id < map.Triangles.Count &&
                         WorldMapTerrainPassability.CanTraverse(0, map.WorldMapType, map.Triangles[id].TerrainId))
            .ToArray();
        var footDistances = WorldMapBroncoLanding.MeasureDistances(
            map,
            arrivals,
            id => WorldMapTerrainPassability.CanTraverse(0, map.WorldMapType, map.Triangles[id].TerrainId) &&
                  !planner.IsUnwantedEntrance(id, exemptions));

        double Flight(WorldMapVertex point)
        {
            var dx = (double)WorldMapTargetCatalog.WrappedDelta(state.X, point.X, map.WrapWidth);
            var dz = (double)WorldMapTargetCatalog.WrappedDelta(state.Z, point.Z, map.WrapHeight);
            return Math.Sqrt(dx * dx + dz * dz);
        }

        var candidates = map.Triangles
            .Where(triangle => IsLandingTerrain(triangle.TerrainId) &&
                               !double.IsPositiveInfinity(footDistances[triangle.Id]) &&
                               destination.Contains(planner.GetComponentId(0, map.WorldMapType, triangle.Id)))
            .Select(triangle => (Triangle: triangle, Flight: Flight(triangle.Centroid), Foot: footDistances[triangle.Id]))
            .OrderBy(candidate => candidate.Flight * FlightCostPerUnit + candidate.Foot)
            .Take(MaximumCandidates);

        var examined = 0;
        var proofs = 0;
        var lastFailure = "no grass joined on foot to it";
        WorldMapHighwindLandingPlan? fallback = null;
        var fallbackExamined = 0;
        foreach (var (triangle, flight, foot) in candidates)
        {
            examined++;
            if (fallback is not null && examined - fallbackExamined > DriftSearchCandidates)
            {
                break;
            }

            var point = triangle.Centroid;
            if (CoversArrival(map, point.X, point.Z, arrivals))
            {
                lastFailure = "the parked ship would stand across its way in";
                continue;
            }

            if (!FitsEveryRotation(map, planner, point.X, point.Z, destination, out var failure))
            {
                lastFailure = failure;
                continue;
            }

            // Checked before the route on foot, which costs far more: once a spot that fits
            // only at its centre is in hand, only one that also fits around it is proved.
            var fitsAround = FitsAround(map, planner, point.X, point.Z, destination);
            if (!fitsAround && fallback is not null)
            {
                lastFailure = "it fits only at its centre";
                continue;
            }

            if (proofs++ >= MaximumRouteProofs)
            {
                break;
            }

            // A component says the ground is joined; the route the party will be given on
            // foot is the proof, asked from every quarter turn of the ring.
            if (!TryProveOnFoot(planner, map, state, target, point.X, point.Z, out failure))
            {
                lastFailure = failure;
                continue;
            }

            var found = new WorldMapHighwindLandingPlan(
                point.X,
                point.Y,
                point.Z,
                triangle.Id,
                destination,
                flight,
                foot);
            if (fitsAround)
            {
                plan = found;
                diagnostic =
                    $"Highwind landing for {target.Label}: spot {point.X},{point.Z} triangle {triangle.Id}, " +
                    $"flight {flight:0}, walk {foot:0}, fits within {DriftRadii[^1]} of it, {examined} spot(s) examined";
                return true;
            }

            fallback = found;
            fallbackExamined = examined;
        }
        if (fallback is not null)
        {
            plan = fallback;
            diagnostic =
                $"Highwind landing for {target.Label}: spot {fallback.ShipX},{fallback.ShipZ} triangle {fallback.ShipTriangleId}, " +
                $"flight {fallback.FlightDistance:0}, walk {fallback.FootDistance:0}, fits at the spot only, {examined} spot(s) examined";
            return true;
        }

        diagnostic = $"no Highwind landing for {target.Label} after {examined} spot(s): {lastFailure}";
        return false;
    }

    /// <summary>
    /// Whether the landing still works wherever round the spot the ship comes to rest, out to
    /// the farthest of <see cref="DriftRadii"/>, in eight directions.
    /// </summary>
    private static bool FitsAround(
        WorldMapData map,
        WorldMapRoutePlanner planner,
        int shipX,
        int shipZ,
        IReadOnlySet<int> destinationFootComponents)
    {
        foreach (var radius in DriftRadii)
        {
            for (var direction = 0; direction < 8; direction++)
            {
                var radians = direction * Math.PI / 4d;
                var x = Normalize(shipX + (int)Math.Round(radius * Math.Sin(radians)), map.WrapWidth);
                var z = Normalize(shipZ + (int)Math.Round(radius * Math.Cos(radians)), map.WrapHeight);
                if (!FitsEveryRotation(map, planner, x, z, destinationFootComponents, out _))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether the parked ship's native reach would cover any of the destination's arrivals.</summary>
    private static bool CoversArrival(WorldMapData map, int shipX, int shipZ, IReadOnlyList<int> arrivals)
    {
        foreach (var id in arrivals)
        {
            var centre = map.Triangles[id].Centroid;
            if (Math.Abs(WorldMapTargetCatalog.WrappedDelta(shipX, centre.X, map.WrapWidth)) < ParkedReach &&
                Math.Abs(WorldMapTargetCatalog.WrappedDelta(shipZ, centre.Z, map.WrapHeight)) < ParkedReach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A route on foot from the ground every quarter turn sets the party down on, built by the
    /// same planner that will walk it, with the native entrance rules it always applies.
    /// </summary>
    private static bool TryProveOnFoot(
        WorldMapRoutePlanner planner,
        WorldMapData map,
        WorldMapStateSnapshot flying,
        WorldMapNavigationTarget target,
        int shipX,
        int shipZ,
        out string failure)
    {
        for (var rotation = 0; rotation < 4096; rotation += 1024)
        {
            if (!TryPredictDisembark(map, shipX, shipZ, rotation, out var ground, out var groundX, out var groundZ))
            {
                failure = $"rotation {rotation} does not set the party down";
                return false;
            }

            var onFoot = flying with
            {
                X = groundX,
                Y = WorldMapBroncoLanding.SurfaceHeight(ground, groundX, groundZ),
                Z = groundZ,
                TerrainId = ground.TerrainId,
                TerrainScriptId = ground.TerrainScriptId,
                RegionId = ground.RegionId & 31,
                PlayerModelId = 0,
                MovementSpeed = 30
            };
            if (!planner.TryBuildRoute(onFoot, target, out _))
            {
                failure = $"no route on foot from {groundX},{groundZ}: {planner.LastDiagnostic}";
                return false;
            }
        }

        failure = string.Empty;
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
