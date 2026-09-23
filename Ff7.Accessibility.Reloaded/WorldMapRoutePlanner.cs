namespace Ff7.Accessibility.Reloaded;

public readonly record struct WorldMapRouteWaypoint(int X, int Y, int Z);

public sealed record WorldMapRoutePlan(
    string TargetId,
    int StartTriangleId,
    int TargetTriangleId,
    IReadOnlyList<int> TrianglePath,
    IReadOnlyList<WorldMapRouteWaypoint> Waypoints,
    double TotalDistance);

public static class WorldMapTerrainPassability
{
    private static readonly HashSet<int> WalkingTerrain =
    [
        // Native ground type 7 is the Midgar Zolom swamp. The party can cross
        // it on foot; the Zolom is merely constrained to that surface.
        // Ground type 12 is a cliff face separating two occupiable levels and
        // must never be used as a walking bridge between them.
        0, 1, 7, 8, 9, 10, 11, 13, 14, 16, 17, 19, 20, 21,
        24, 25, 27, 28, 29, 30
    ];

    public static bool CanTraverse(int playerModelId, int worldMapType, int terrainId)
    {
        if (terrainId is < 0 or > 31)
        {
            return false;
        }

        return playerModelId switch
        {
            // Highwind travels above terrain. Landing eligibility is a target
            // concern and must not break an airborne route.
            3 => true,
            // Tiny Bronco can use ordinary coast/river/shallow-water terrain.
            5 => WalkingTerrain.Contains(terrainId) || terrainId is 4 or 5 or 6,
            // Buggy adds the native river-crossing surface to walking land.
            6 => WalkingTerrain.Contains(terrainId) || terrainId == 4,
            // Submarine and red submarine own the underwater world.
            13 or 28 => worldMapType == 2 && terrainId is 3 or 18 or 26,
            // Model 4 is the live caught Chocobo used immediately after a
            // Chocobo battle; model 19 is the alternate ridden form. A
            // Chocobo's color/capability is stored separately, so both retain
            // ordinary walking terrain until that native capability is read.
            4 or 19 => WalkingTerrain.Contains(terrainId),
            // Cloud, Tifa, and Cid are the controllable walking models.
            0 or 1 or 2 => WalkingTerrain.Contains(terrainId),
            _ => false
        };
    }
}

public sealed class WorldMapRoutePlanner
{
    private const double ElevationCostRatio = 0.15d;
    private const double PortalClearanceUnits = 64d;
    private const double PortalClearanceFraction = 1d / 6d;

    private readonly WorldMapData map;
    private readonly IReadOnlyDictionary<(int X, int Z), IReadOnlyList<int>> trianglesByMesh;
    private readonly Dictionary<(int PlayerModelId, int WorldMapType), int[]> componentIdsByProfile = new();
    private readonly object componentCacheLock = new();

    public WorldMapRoutePlanner(WorldMapData map)
    {
        this.map = map ?? throw new ArgumentNullException(nameof(map));
        trianglesByMesh = map.Triangles
            .GroupBy(triangle => (triangle.MeshX, triangle.MeshZ))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<int>)group.Select(triangle => triangle.Id).ToArray());
    }

    public string LastDiagnostic { get; private set; } = string.Empty;

    /// <summary>
    /// Triangles the native world script turns into a field entry, from
    /// <see cref="WorldMapTargetCatalog.EntranceTriangleIds"/>. Routing, segment clearance
    /// and the controller's own step probe all consult the same set, so a shortcut, a
    /// detour and an automatic key press cannot disagree about where a zone starts.
    /// Empty by default, which is exactly the behaviour that shipped.
    /// </summary>
    public IReadOnlySet<int> EntranceTriangleIds { get; set; } = new HashSet<int>();

    /// <summary>
    /// Whether walking onto this triangle would enter a field the player did not ask for.
    ///
    /// <para>Only a selected place's own entrance is exempt - see
    /// <see cref="WorldMapNavigationTarget.NativeEntranceExemptions"/>, which is empty for
    /// anything that is not a location or its story copy. A vehicle or a chocobo track that
    /// happens to sit in a trigger cell does not license entering that zone.</para>
    ///
    /// <para>The triangle the party is already standing on is never closed to them, so a
    /// party that starts on a trigger can still be walked off it.</para>
    /// </summary>
    public bool IsUnwantedEntrance(int triangleId, IReadOnlySet<int>? exemptTriangles, int startTriangle = -1) =>
        EntranceTriangleIds.Count > 0 &&
        triangleId != startTriangle &&
        EntranceTriangleIds.Contains(triangleId) &&
        exemptTriangles?.Contains(triangleId) != true;

    /// <summary>
    /// The exemptions that apply to a route starting here: the destination's own entrance,
    /// plus the whole trigger the party is already standing in.
    ///
    /// <para>A native trigger is a mesh cell, not one triangle - Gongaga's is sixty-four of
    /// them. Exempting only the single triangle under the party walls them inside their own
    /// doorway with no way out at all. Crossing the rest of that same trigger enters nothing
    /// new either: FUN_00765F61 fires a terrain handler only when the script id under the
    /// party <em>changes</em>, and clears its latch only on a script below 3, so moving
    /// within one script-7 cell cannot re-trigger it.</para>
    /// </summary>
    private IReadOnlySet<int> ResolveEscapeExemptions(int startTriangle, IReadOnlySet<int>? exempt)
    {
        if (EntranceTriangleIds.Count == 0 ||
            startTriangle < 0 ||
            !EntranceTriangleIds.Contains(startTriangle))
        {
            return exempt ?? EmptyExemptions;
        }

        var escape = new HashSet<int>(exempt ?? EmptyExemptions) { startTriangle };
        var pending = new Queue<int>();
        pending.Enqueue(startTriangle);
        while (pending.TryDequeue(out var current))
        {
            foreach (var neighbor in map.Triangles[current].Neighbors)
            {
                if (EntranceTriangleIds.Contains(neighbor) && escape.Add(neighbor))
                {
                    pending.Enqueue(neighbor);
                }
            }
        }

        return escape;
    }

    private static readonly IReadOnlySet<int> EmptyExemptions = new HashSet<int>();

    public bool TryResolvePlayerTriangle(WorldMapStateSnapshot state, out int triangleId)
    {
        triangleId = -1;
        if (state.CurrentModule != WorldMapStateReader.WorldModule ||
            state.WorldMapType != map.WorldMapType)
        {
            LastDiagnostic = $"state module/map {state.CurrentModule}/{state.WorldMapType} does not own map {map.WorldMapType}";
            return false;
        }

        var normalizedX = Normalize(state.X, map.WrapWidth);
        var normalizedZ = Normalize(state.Z, map.WrapHeight);
        var meshX = Math.Clamp(normalizedX / WorldMapDataLoader.MeshSize, 0, map.MeshGridWidth - 1);
        var meshZ = Math.Clamp(normalizedZ / WorldMapDataLoader.MeshSize, 0, map.MeshGridHeight - 1);
        if (!trianglesByMesh.TryGetValue((meshX, meshZ), out var candidates) || candidates.Count == 0)
        {
            LastDiagnostic = $"mesh {meshX},{meshZ} has no native triangles";
            return false;
        }

        var containing = candidates
            .Select(id => map.Triangles[id])
            .Where(triangle => ContainsPoint(triangle, normalizedX, normalizedZ))
            .OrderBy(triangle => triangle.TerrainId == state.TerrainId ? 0 : 1)
            .ThenBy(triangle => triangle.TerrainScriptId == state.TerrainScriptId ? 0 : 1)
            .ThenBy(triangle => (triangle.RegionId & 0x1F) == state.RegionId ? 0 : 1)
            .ThenBy(triangle => Math.Abs(SurfaceHeightAt(triangle, normalizedX, normalizedZ) - state.Y))
            .FirstOrDefault();
        if (containing is not null)
        {
            triangleId = containing.Id;
            LastDiagnostic = $"resolved containing triangle {triangleId}";
            return true;
        }

        var nearest = candidates
            .Select(id => map.Triangles[id])
            .Where(triangle => triangle.TerrainId == state.TerrainId)
            .OrderBy(triangle => triangle.TerrainScriptId == state.TerrainScriptId ? 0 : 1)
            .ThenBy(triangle => DistanceSquared(
                state.X,
                state.Y,
                state.Z,
                triangle.Centroid.X,
                triangle.Centroid.Y,
                triangle.Centroid.Z))
            .FirstOrDefault();
        if (nearest is null)
        {
            LastDiagnostic = $"mesh {meshX},{meshZ} has no triangle matching terrain {state.TerrainId}";
            return false;
        }

        triangleId = nearest.Id;
        LastDiagnostic = $"resolved nearest terrain-matched triangle {triangleId}";
        return true;
    }

    public bool TryBuildRoute(
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target,
        out WorldMapRoutePlan plan)
    {
        plan = default!;
        if (!TryResolvePlayerTriangle(state, out var startTriangle))
        {
            return false;
        }

        var goals = target.ArrivalTriangleIds
            .Where(id => id >= 0 && id < map.Triangles.Count)
            .Where(id => WorldMapTerrainPassability.CanTraverse(
                state.PlayerModelId,
                state.WorldMapType,
                map.Triangles[id].TerrainId))
            .ToHashSet();
        if (goals.Count == 0)
        {
            LastDiagnostic = $"target {target.Label} has no traversable native arrival triangle";
            return false;
        }

        if (!WorldMapTerrainPassability.CanTraverse(
                state.PlayerModelId,
                state.WorldMapType,
                map.Triangles[startTriangle].TerrainId))
        {
            LastDiagnostic = $"player triangle {startTriangle} terrain {map.Triangles[startTriangle].TerrainId} is not traversable by model {state.PlayerModelId}";
            return false;
        }

        // Route around every other place's entrance. There is no fallback: a route that
        // walks the party into a town they did not ask for is not a success, and the step
        // probe would refuse it anyway, so the two would disagree. A truthful "no safe
        // route" is the only other answer.
        var exemptEntrances = ResolveEscapeExemptions(startTriangle, target.NativeEntranceExemptions);
        if (!TryFindTrianglePath(
                state, startTriangle, goals, exemptEntrances, out var path, out var targetTriangle))
        {
            LastDiagnostic = EntranceTriangleIds.Count > 0 &&
                             TryFindTrianglePath(state, startTriangle, goals, null, out _, out _)
                ? $"no route to {target.Label} for model {state.PlayerModelId} that avoids " +
                  "another native field entrance"
                : $"no native route from triangle {startTriangle} to {target.Label} " +
                  $"for model {state.PlayerModelId}";
            return false;
        }

        var waypoints = BuildStableWaypoints(state, target, path, targetTriangle);
        var distance = MeasureRouteDistance(state, waypoints);
        plan = new WorldMapRoutePlan(
            target.StableId,
            startTriangle,
            targetTriangle,
            path,
            waypoints,
            distance);
        LastDiagnostic =
            $"route {target.Label}: triangles={path.Count}, waypoints={waypoints.Count}, distance={distance:0}";
        return true;
    }

    public bool CanReach(
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!TryResolvePlayerTriangle(state, out var startTriangle))
        {
            return false;
        }

        if (!WorldMapTerrainPassability.CanTraverse(
                state.PlayerModelId,
                state.WorldMapType,
                map.Triangles[startTriangle].TerrainId))
        {
            LastDiagnostic =
                $"player triangle {startTriangle} terrain {map.Triangles[startTriangle].TerrainId} " +
                $"is not traversable by model {state.PlayerModelId}";
            return false;
        }

        var componentIds = GetComponentIds(state.PlayerModelId, state.WorldMapType);
        var startComponent = componentIds[startTriangle];
        var reachable = startComponent >= 0 && target.ArrivalTriangleIds.Any(
            id => id >= 0 &&
                  id < componentIds.Length &&
                  componentIds[id] == startComponent);
        LastDiagnostic = reachable
            ? $"target {target.Label} shares walkable component {startComponent}"
            : $"target {target.Label} is outside walkable component {startComponent}";
        return reachable;
    }

    /// <param name="exemptEntrances">
    /// The selected destination's own arrival triangles, when there is one. Supplying it
    /// makes the segment refuse to cross any other native field entrance, which is what
    /// keeps a walk to a parked vehicle out of the neighbouring town.
    /// </param>
    internal bool CanTraverseSegment(
        WorldMapStateSnapshot state,
        WorldMapRouteWaypoint destination,
        int? destinationTriangle = null,
        IReadOnlySet<int>? exemptEntrances = null)
    {
        if (!TryResolvePlayerTriangle(state, out var startTriangle))
        {
            return false;
        }

        // Highwind flies above ground faces, including native vertical faces
        // with no X/Z area. Retain the native owner/position check above.
        if (state.PlayerModelId == 3)
        {
            return true;
        }

        // A null exempt set still means "this caller is not avoiding entrances at all".
        if (exemptEntrances is not null)
        {
            exemptEntrances = ResolveEscapeExemptions(startTriangle, exemptEntrances);
        }

        var end = Unwrap(destination, state.X, state.Z);
        var pending = new Queue<(int TriangleId, double EnteredAt)>();
        var entered = new Dictionary<int, double> { [startTriangle] = 0d };
        pending.Enqueue((startTriangle, 0d));
        while (pending.TryDequeue(out var current))
        {
            var triangle = map.Triangles[current.TriangleId];
            if (!WorldMapTerrainPassability.CanTraverse(state.PlayerModelId, state.WorldMapType, triangle.TerrainId) ||
                (exemptEntrances is not null &&
                 IsUnwantedEntrance(current.TriangleId, exemptEntrances, startTriangle)) ||
                !TryClipSegment(triangle, state, end, out var first, out var last) ||
                current.EnteredAt < first - 1e-7 || current.EnteredAt > last + 1e-7)
            {
                continue;
            }

            if (last >= 1d - 1e-7 && (destinationTriangle is null || current.TriangleId == destinationTriangle))
            {
                return true;
            }

            foreach (var neighborId in triangle.Neighbors)
            {
                if (!TryFindSharedEdge(triangle, map.Triangles[neighborId], out var a, out var b) ||
                    !TryIntersectEdge(state, end, Unwrap(a, state.X, state.Z), Unwrap(b, state.X, state.Z),
                        out var edgeFirst, out var edgeLast))
                {
                    continue;
                }

                var nextAt = Math.Max(current.EnteredAt, edgeFirst);
                if (nextAt > Math.Min(last, edgeLast) + 1e-7 ||
                    entered.TryGetValue(neighborId, out var prior) && prior <= nextAt + 1e-7)
                {
                    continue;
                }

                entered[neighborId] = nextAt;
                pending.Enqueue((neighborId, nextAt));
            }
        }

        return false;
    }

    private bool TryClipSegment(WorldMapTriangle triangle, WorldMapStateSnapshot start,
        WorldMapRouteWaypoint end, out double first, out double last)
    {
        first = 0d;
        last = 1d;
        var vertices = new[] { Unwrap(triangle.Vertex0, start.X, start.Z),
            Unwrap(triangle.Vertex1, start.X, start.Z), Unwrap(triangle.Vertex2, start.X, start.Z) };
        var winding = Math.Sign(Cross(vertices[1].X - vertices[0].X, vertices[1].Z - vertices[0].Z,
            vertices[2].X - vertices[0].X, vertices[2].Z - vertices[0].Z));
        if (winding == 0) return false;
        for (var index = 0; index < 3; index++)
        {
            var a = vertices[index];
            var b = vertices[(index + 1) % 3];
            var from = winding * Cross(b.X - a.X, b.Z - a.Z, start.X - a.X, start.Z - a.Z);
            var to = winding * Cross(b.X - a.X, b.Z - a.Z, end.X - a.X, end.Z - a.Z);
            if (from < 0d && to < 0d) return false;
            if (from < 0d) first = Math.Max(first, from / (from - to));
            if (to < 0d) last = Math.Min(last, from / (from - to));
            if (first > last + 1e-7) return false;
        }
        return true;
    }

    private static bool TryIntersectEdge(WorldMapStateSnapshot start, WorldMapRouteWaypoint end,
        WorldMapRouteWaypoint a, WorldMapRouteWaypoint b, out double first, out double last)
    {
        first = last = 0d;
        var dx = end.X - (double)start.X;
        var dz = end.Z - (double)start.Z;
        var ex = b.X - (double)a.X;
        var ez = b.Z - (double)a.Z;
        var denominator = Cross(dx, dz, ex, ez);
        var ax = a.X - (double)start.X;
        var az = a.Z - (double)start.Z;
        if (Math.Abs(denominator) > 1e-7)
        {
            first = last = Cross(ax, az, ex, ez) / denominator;
            var edgeAt = Cross(ax, az, dx, dz) / denominator;
            return first >= -1e-7 && first <= 1d + 1e-7 && edgeAt >= -1e-7 && edgeAt <= 1d + 1e-7;
        }

        var lengthSquared = dx * dx + dz * dz;
        if (lengthSquared <= 0d || Math.Abs(Cross(ax, az, dx, dz)) > 1e-7) return false;
        var atA = (ax * dx + az * dz) / lengthSquared;
        var atB = ((b.X - (double)start.X) * dx + (b.Z - (double)start.Z) * dz) / lengthSquared;
        first = Math.Max(0d, Math.Min(atA, atB));
        last = Math.Min(1d, Math.Max(atA, atB));
        return first <= last + 1e-7;
    }

    private static double Cross(double ax, double az, double bx, double bz) => ax * bz - az * bx;

    internal bool TryResolveReachableComponent(
        WorldMapStateSnapshot state,
        out int componentId)
    {
        componentId = -1;
        if (!TryResolvePlayerTriangle(state, out var triangleId) ||
            !WorldMapTerrainPassability.CanTraverse(
                state.PlayerModelId,
                state.WorldMapType,
                map.Triangles[triangleId].TerrainId))
        {
            return false;
        }

        componentId = GetComponentId(
            state.PlayerModelId,
            state.WorldMapType,
            triangleId);
        return componentId >= 0;
    }

    internal int GetComponentId(
        int playerModelId,
        int worldMapType,
        int triangleId)
    {
        if (triangleId < 0 || triangleId >= map.Triangles.Count)
        {
            return -1;
        }

        return GetComponentIds(playerModelId, worldMapType)[triangleId];
    }

    public double MeasureRemainingDistance(
        WorldMapStateSnapshot state,
        WorldMapRoutePlan route,
        int waypointIndex)
    {
        if (route.Waypoints.Count == 0)
        {
            return 0d;
        }

        var startIndex = Math.Clamp(waypointIndex, 0, route.Waypoints.Count - 1);
        var first = route.Waypoints[startIndex];
        var distance = Distance(state.X, state.Y, state.Z, first.X, first.Y, first.Z);
        for (var index = startIndex + 1; index < route.Waypoints.Count; index++)
        {
            var previous = route.Waypoints[index - 1];
            var current = route.Waypoints[index];
            distance += Distance(previous.X, previous.Y, previous.Z, current.X, current.Y, current.Z);
        }

        return distance;
    }

    private bool TryFindTrianglePath(
        WorldMapStateSnapshot state,
        int start,
        IReadOnlySet<int> goals,
        IReadOnlySet<int>? exemptEntrances,
        out IReadOnlyList<int> path,
        out int target)
    {
        path = [];
        target = -1;
        if (goals.Contains(start))
        {
            path = [start];
            target = start;
            return true;
        }

        var cameFrom = Enumerable.Repeat(-1, map.Triangles.Count).ToArray();
        var scores = Enumerable.Repeat(double.PositiveInfinity, map.Triangles.Count).ToArray();
        var closed = new bool[map.Triangles.Count];
        var frontier = new PriorityQueue<int, double>();
        scores[start] = 0;
        frontier.Enqueue(start, 0);

        while (frontier.TryDequeue(out var current, out _))
        {
            if (closed[current])
            {
                continue;
            }

            if (goals.Contains(current))
            {
                target = current;
                break;
            }

            closed[current] = true;
            var currentTriangle = map.Triangles[current];
            foreach (var neighbor in currentTriangle.Neighbors)
            {
                if (closed[neighbor])
                {
                    continue;
                }

                var next = map.Triangles[neighbor];
                if (!WorldMapTerrainPassability.CanTraverse(
                        state.PlayerModelId,
                        state.WorldMapType,
                        next.TerrainId) ||
                    (exemptEntrances is not null &&
                     IsUnwantedEntrance(neighbor, exemptEntrances, start)))
                {
                    continue;
                }

                var tentative = scores[current] + EdgeCost(currentTriangle, next);
                if (tentative >= scores[neighbor])
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                scores[neighbor] = tentative;
                frontier.Enqueue(neighbor, tentative);
            }
        }

        if (target < 0)
        {
            return false;
        }

        var reversed = new List<int> { target };
        for (var current = target; current != start;)
        {
            current = cameFrom[current];
            if (current < 0)
            {
                return false;
            }

            reversed.Add(current);
        }

        reversed.Reverse();
        path = reversed;
        return true;
    }

    private int[] GetComponentIds(int playerModelId, int worldMapType)
    {
        var key = (playerModelId, worldMapType);
        lock (componentCacheLock)
        {
            if (componentIdsByProfile.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var componentIds = Enumerable.Repeat(-1, map.Triangles.Count).ToArray();
            var pending = new Queue<int>();
            var componentId = 0;
            for (var triangleId = 0; triangleId < map.Triangles.Count; triangleId++)
            {
                if (componentIds[triangleId] >= 0 ||
                    !WorldMapTerrainPassability.CanTraverse(
                        playerModelId,
                        worldMapType,
                        map.Triangles[triangleId].TerrainId))
                {
                    continue;
                }

                componentIds[triangleId] = componentId;
                pending.Enqueue(triangleId);
                while (pending.TryDequeue(out var current))
                {
                    foreach (var neighbor in map.Triangles[current].Neighbors)
                    {
                        if (componentIds[neighbor] >= 0 ||
                            !WorldMapTerrainPassability.CanTraverse(
                                playerModelId,
                                worldMapType,
                                map.Triangles[neighbor].TerrainId))
                        {
                            continue;
                        }

                        componentIds[neighbor] = componentId;
                        pending.Enqueue(neighbor);
                    }
                }

                componentId++;
            }

            componentIdsByProfile.Add(key, componentIds);
            return componentIds;
        }
    }

    private IReadOnlyList<WorldMapRouteWaypoint> BuildStableWaypoints(
        WorldMapStateSnapshot state,
        WorldMapNavigationTarget target,
        IReadOnlyList<int> path,
        int targetTriangle)
    {
        var start = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        var unwrappedCentroids = BuildUnwrappedCentroids(path, start);
        var portals = new List<WorldMapRoutePortal>(Math.Max(0, path.Count - 1));
        for (var index = 0; index < path.Count - 1; index++)
        {
            if (!TryFindSharedEdge(
                    map.Triangles[path[index]],
                    map.Triangles[path[index + 1]],
                    out var first,
                    out var second))
            {
                // Adjacency is itself built from shared native edges. Reaching
                // this branch means the route data is inconsistent, so retain
                // a safe point inside the next triangle instead of inventing a
                // cross-terrain shortcut.
                var center = unwrappedCentroids[index + 1];
                portals.Add(new WorldMapRoutePortal(center, center));
                continue;
            }

            var referenceX = (unwrappedCentroids[index].X + unwrappedCentroids[index + 1].X) / 2;
            var referenceZ = (unwrappedCentroids[index].Z + unwrappedCentroids[index + 1].Z) / 2;
            var firstPoint = Unwrap(first, referenceX, referenceZ);
            var secondPoint = Unwrap(second, referenceX, referenceZ);
            InsetPortal(ref firstPoint, ref secondPoint);
            portals.Add(OrientPortal(
                firstPoint,
                secondPoint,
                unwrappedCentroids[index],
                unwrappedCentroids[index + 1]));
        }

        var nativeArrival = target.NativeLocationArrivals
            .Where(arrival => arrival.TriangleId == targetTriangle)
            .Select(arrival => (WorldMapNativeLocationArrival?)arrival)
            .FirstOrDefault();
        // A vehicle's arrival triangle can be a thousand units across, and the only part of
        // it that boards the vehicle is the part inside the native mask. Ending on the
        // centroid would stop the party somewhere Confirm does nothing.
        var normalizedFinalPoint = target.VehicleContactPoints.TryGetValue(targetTriangle, out var contactPoint)
            ? new WorldMapRouteWaypoint(contactPoint.X, contactPoint.Y, contactPoint.Z)
            : nativeArrival is { } resolvedNativeArrival
                ? new WorldMapRouteWaypoint(
                    resolvedNativeArrival.X,
                    resolvedNativeArrival.Y,
                    resolvedNativeArrival.Z)
                : targetTriangle == target.TriangleId
                    ? new WorldMapRouteWaypoint(target.X, target.Y, target.Z)
                    : ToWaypoint(map.Triangles[targetTriangle].Centroid);
        var finalReference = unwrappedCentroids.Count > 0 ? unwrappedCentroids[^1] : start;
        var unwrappedFinalPoint = Unwrap(
            normalizedFinalPoint,
            finalReference.X,
            finalReference.Z);
        var pulled = WorldMapFunnel.BuildStableWaypoints(start, portals, unwrappedFinalPoint);
        var normalized = pulled
            .Select(point => new WorldMapRouteWaypoint(
                Normalize(point.X, map.WrapWidth),
                point.Y,
                Normalize(point.Z, map.WrapHeight)))
            .ToList();
        var exactFinalPoint = new WorldMapRouteWaypoint(
            Normalize(normalizedFinalPoint.X, map.WrapWidth),
            normalizedFinalPoint.Y,
            Normalize(normalizedFinalPoint.Z, map.WrapHeight));
        if (normalized.Count == 0)
        {
            normalized.Add(exactFinalPoint);
        }
        else
        {
            normalized[^1] = exactFinalPoint;
        }

        return normalized;
    }

    private IReadOnlyList<WorldMapRouteWaypoint> BuildUnwrappedCentroids(
        IReadOnlyList<int> path,
        WorldMapRouteWaypoint start)
    {
        var result = new List<WorldMapRouteWaypoint>(path.Count);
        var reference = start;
        foreach (var triangleId in path)
        {
            var unwrapped = Unwrap(map.Triangles[triangleId].Centroid, reference.X, reference.Z);
            result.Add(unwrapped);
            reference = unwrapped;
        }

        return result;
    }

    private WorldMapRouteWaypoint Unwrap(WorldMapVertex vertex, int referenceX, int referenceZ) =>
        new(
            UnwrapCoordinate(vertex.X, referenceX, map.WrapWidth),
            vertex.Y,
            UnwrapCoordinate(vertex.Z, referenceZ, map.WrapHeight));

    private WorldMapRouteWaypoint Unwrap(WorldMapRouteWaypoint waypoint, int referenceX, int referenceZ) =>
        new(
            UnwrapCoordinate(waypoint.X, referenceX, map.WrapWidth),
            waypoint.Y,
            UnwrapCoordinate(waypoint.Z, referenceZ, map.WrapHeight));

    private static int UnwrapCoordinate(int value, int reference, int extent) =>
        reference + WorldMapTargetCatalog.WrappedDelta(Normalize(reference, extent), Normalize(value, extent), extent);

    private static WorldMapRoutePortal OrientPortal(
        WorldMapRouteWaypoint first,
        WorldMapRouteWaypoint second,
        WorldMapRouteWaypoint fromCentroid,
        WorldMapRouteWaypoint toCentroid)
    {
        var midpointX = (first.X + second.X) / 2d;
        var midpointZ = (first.Z + second.Z) / 2d;
        var travelX = toCentroid.X - fromCentroid.X;
        var travelZ = toCentroid.Z - fromCentroid.Z;
        var firstSide = travelX * (first.Z - midpointZ) - travelZ * (first.X - midpointX);
        return firstSide >= 0d
            ? new WorldMapRoutePortal(first, second)
            : new WorldMapRoutePortal(second, first);
    }

    private static void InsetPortal(
        ref WorldMapRouteWaypoint first,
        ref WorldMapRouteWaypoint second)
    {
        var dx = second.X - (double)first.X;
        var dy = second.Y - (double)first.Y;
        var dz = second.Z - (double)first.Z;
        var length = Math.Sqrt(dx * dx + dz * dz);
        if (length <= 0.001d)
        {
            return;
        }

        var inset = Math.Min(PortalClearanceUnits, length * PortalClearanceFraction);
        var amount = inset / length;
        var originalFirst = first;
        first = new WorldMapRouteWaypoint(
            (int)Math.Round(first.X + dx * amount),
            (int)Math.Round(first.Y + dy * amount),
            (int)Math.Round(first.Z + dz * amount));
        second = new WorldMapRouteWaypoint(
            (int)Math.Round(second.X + (originalFirst.X - second.X) * amount),
            (int)Math.Round(second.Y + (originalFirst.Y - second.Y) * amount),
            (int)Math.Round(second.Z + (originalFirst.Z - second.Z) * amount));
    }

    private double MeasureRouteDistance(
        WorldMapStateSnapshot state,
        IReadOnlyList<WorldMapRouteWaypoint> waypoints)
    {
        var distance = 0d;
        var x = state.X;
        var y = state.Y;
        var z = state.Z;
        foreach (var waypoint in waypoints)
        {
            distance += Distance(x, y, z, waypoint.X, waypoint.Y, waypoint.Z);
            x = waypoint.X;
            y = waypoint.Y;
            z = waypoint.Z;
        }

        return distance;
    }

    private double EdgeCost(WorldMapTriangle first, WorldMapTriangle second)
    {
        var a = first.Centroid;
        var b = second.Centroid;
        var planar = Math.Sqrt(WorldMapTargetCatalog.WrappedDistanceSquared(map, a.X, a.Z, b.X, b.Z));
        return planar + Math.Abs(b.Y - a.Y) * ElevationCostRatio;
    }

    private double Distance(int firstX, int firstY, int firstZ, int secondX, int secondY, int secondZ)
    {
        var dx = WorldMapTargetCatalog.WrappedDelta(firstX, secondX, map.WrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(firstZ, secondZ, map.WrapHeight);
        var dy = secondY - firstY;
        return Math.Sqrt(dx * (double)dx + dy * (double)dy + dz * (double)dz);
    }

    private double DistanceSquared(int firstX, int firstY, int firstZ, int secondX, int secondY, int secondZ)
    {
        var distance = Distance(firstX, firstY, firstZ, secondX, secondY, secondZ);
        return distance * distance;
    }

    private bool TryFindSharedEdge(
        WorldMapTriangle first,
        WorldMapTriangle second,
        out WorldMapVertex edgeStart,
        out WorldMapVertex edgeEnd)
    {
        foreach (var firstEdge in first.Edges)
        {
            foreach (var secondEdge in second.Edges)
            {
                if ((SameWrappedVertex(firstEdge.Start, secondEdge.Start) && SameWrappedVertex(firstEdge.End, secondEdge.End)) ||
                    (SameWrappedVertex(firstEdge.Start, secondEdge.End) && SameWrappedVertex(firstEdge.End, secondEdge.Start)))
                {
                    edgeStart = firstEdge.Start;
                    edgeEnd = firstEdge.End;
                    return true;
                }
            }
        }

        edgeStart = edgeEnd = default;
        return false;
    }

    private bool SameWrappedVertex(WorldMapVertex first, WorldMapVertex second) =>
        Normalize(first.X, map.WrapWidth) == Normalize(second.X, map.WrapWidth) &&
        first.Y == second.Y &&
        Normalize(first.Z, map.WrapHeight) == Normalize(second.Z, map.WrapHeight);

    private static WorldMapRouteWaypoint ToWaypoint(WorldMapVertex vertex) =>
        new(vertex.X, vertex.Y, vertex.Z);

    private static double SurfaceHeightAt(WorldMapTriangle triangle, int x, int z)
    {
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
        if (denominator == 0d) return triangle.Centroid.Y;
        var weightA = ((b.Z - c.Z) * (double)(x - c.X) + (c.X - b.X) * (double)(z - c.Z)) / denominator;
        var weightB = ((c.Z - a.Z) * (double)(x - c.X) + (a.X - c.X) * (double)(z - c.Z)) / denominator;
        return weightA * a.Y + weightB * b.Y + (1d - weightA - weightB) * c.Y;
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

    private static int Normalize(int value, int extent)
    {
        var normalized = value % extent;
        return normalized < 0 ? normalized + extent : normalized;
    }
}
