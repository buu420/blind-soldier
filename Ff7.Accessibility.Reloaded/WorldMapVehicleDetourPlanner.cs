namespace Ff7.Accessibility.Reloaded;

/// <summary>A bounded local path around a parked Buggy, using native masks and ground faces.</summary>
public sealed class WorldMapVehicleDetourPlanner
{
    private readonly WorldMapData map;
    private readonly WorldMapRoutePlanner planner;
    private string key = string.Empty;
    private readonly List<WorldMapRouteWaypoint> path = new();
    private int corner;
    private IReadOnlySet<int> exemptEntrances = new HashSet<int>();
    private (int X, int Y, int Z, WorldMapRouteWaypoint Goal)? blockedAt;
    public int Corner => corner;
    public IReadOnlyList<WorldMapRouteWaypoint> Path => path;
    public enum DetourOutcome { Clear, Detour, Blocked }

    public WorldMapVehicleDetourPlanner(WorldMapData map, WorldMapRoutePlanner planner)
    {
        this.map = map;
        this.planner = planner;
    }

    public void Invalidate() { key = string.Empty; path.Clear(); corner = 0; blockedAt = null; }

    public double RemainingDistance(WorldMapStateSnapshot state)
    {
        var previous = new WorldMapRouteWaypoint(state.X, state.Y, state.Z);
        var distance = 0d;
        for (var index = corner; index < path.Count; index++)
        {
            distance += Distance(previous, path[index]);
            previous = path[index];
        }
        return distance;
    }

    public DetourOutcome Resolve(WorldMapStateSnapshot state,
        IReadOnlyList<WorldMapEntitySnapshot> entities, WorldMapNavigationTarget target,
        WorldMapRouteWaypoint goal, out WorldMapRouteWaypoint aim)
    {
        aim = goal;
        // Every leg this planner offers is walked automatically, so it obeys the same
        // native-entrance rule the route does: only the selected destination's own
        // entrance may be stepped on.
        exemptEntrances = target.NativeEntranceExemptions;
        if (state.PlayerModelId is not (0 or 1 or 2)) { Invalidate(); return DetourOutcome.Clear; }
        var obstacles = entities.Where(WorldMapVehicleObstacles.IsParkedBuggy)
            .OrderBy(e => e.X).ThenBy(e => e.Z).ToArray();
        var nextKey = $"{target.StableId}:{state.WorldMapType}:{state.PlayerModelId}:{state.WorldProgress}:" +
            string.Join(";", obstacles.Select(e => $"{e.X},{e.Z},{e.Flags}"));
        if (nextKey != key) { Invalidate(); key = nextKey; }
        if (obstacles.Length == 0) return DetourOutcome.Clear;

        if (path.Count > 0)
        {
            // Commit the route around the car. Advance only when the next leg is clear;
            // recalculating a single cheapest sidestep kept aiming at the same corner.
            while (corner + 1 < path.Count && Clear(state, path[corner + 1], obstacles)) corner++;
            if (Clear(state, path[corner], obstacles))
            {
                aim = path[corner];
                if (corner == path.Count - 1 && Distance(new(state.X, state.Y, state.Z), aim) < 120 &&
                    !target.ArrivalTriangleIds.Contains(ResolveTriangle(state)))
                {
                    Invalidate(); key = nextKey;
                }
                else return DetourOutcome.Detour;
            }
            else { path.Clear(); corner = 0; }
        }

        if (!BlockedSegment(state, goal, obstacles)) { blockedAt = null; return DetourOutcome.Clear; }
        var query = (state.X, state.Y, state.Z, goal);
        if (blockedAt == query) return DetourOutcome.Blocked;
        // A stationary blocked approach has the same answer until input, target or live
        // entities change. Do not rebuild its graph every frame while waiting for input.
        blockedAt = query;

        var nodes = new List<WorldMapStateSnapshot> { state };
        var goals = new HashSet<int>();
        void AddGoal(WorldMapRouteWaypoint point, bool requireArrival = false)
        {
            // Quantized directions keep moving for a native step. A goal six units
            // outside a car corner is mathematically clear but cannot be aimed at
            // reliably from every camera angle. Choose an interior approach with room.
            if (requireArrival && new[] { (-96, -96), (-96, 96), (96, -96), (96, 96) }
                .Any(offset => WorldMapVehicleObstacles.IsBlocked(obstacles, state.PlayerModelId,
                    point.X + offset.Item1, point.Z + offset.Item2, map.WrapWidth, map.WrapHeight))) return;
            if (requireArrival && new[] { (-64, 0), (64, 0), (0, -64), (0, 64) }.Any(offset =>
                !TryGround(state, new(point.X + offset.Item1, point.Y, point.Z + offset.Item2), out var nearby) ||
                !target.HasArrived(nearby, ResolveTriangle(nearby)))) return;
            if (!WorldMapVehicleObstacles.IsBlocked(obstacles, state.PlayerModelId,
                point.X, point.Z, map.WrapWidth, map.WrapHeight) && TryGround(state, point, out var ground) &&
                (!requireArrival || target.HasArrived(ground, ResolveTriangle(ground))))
            { goals.Add(nodes.Count); nodes.Add(ground); }
        }
        AddGoal(goal);
        // An alternative entrance is valid only when it belongs to this actual target,
        // and is local to the obstruction. Never use a town as a shortcut on a long route.
        foreach (var id in target.ArrivalTriangleIds)
        {
            if (id < 0 || id >= map.Triangles.Count) continue;
            var triangle = map.Triangles[id];
            var c = triangle.Centroid;
            if (Distance(new(state.X, state.Y, state.Z), new(c.X, c.Y, c.Z)) > 4096) continue;
            AddGoal(new(c.X, c.Y, c.Z), requireArrival: true);
            foreach (var v in new[] { triangle.Vertex0, triangle.Vertex1, triangle.Vertex2 })
                AddGoal(new(c.X + (int)((v.X - c.X) * .6), c.Y + (int)((v.Y - c.Y) * .6),
                    c.Z + (int)((v.Z - c.Z) * .6)), requireArrival: true);
        }
        if (goals.Count == 0) return DetourOutcome.Blocked;

        foreach (var obstacle in obstacles.Where(e => Distance(new(state.X, state.Y, state.Z), new(e.X, e.Y, e.Z)) < 4096))
        foreach (var radius in new[] { 768, 1024, 1280 })
        for (var n = 0; n < 16; n++)
        {
            var angle = n * Math.PI / 8;
            var point = new WorldMapRouteWaypoint(Wrap(obstacle.X + (int)Math.Round(Math.Cos(angle) * radius), map.WrapWidth),
                state.Y, Wrap(obstacle.Z + (int)Math.Round(Math.Sin(angle) * radius), map.WrapHeight));
            // A sidestep must not be somewhere the game turns into a field. The party
            // parked beside Gongaga, so most of the ring around the Buggy is its trigger.
            if (!WorldMapVehicleObstacles.IsBlocked(obstacles, state.PlayerModelId, point.X, point.Z, map.WrapWidth, map.WrapHeight)
                && TryGround(state, point, out var ground)
                && !planner.IsUnwantedEntrance(ResolveTriangle(ground), exemptEntrances)) nodes.Add(ground);
        }

        var costs = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var parent = Enumerable.Repeat(-1, nodes.Count).ToArray();
        var queue = new PriorityQueue<int, double>();
        costs[0] = 0; queue.Enqueue(0, 0);
        while (queue.TryDequeue(out var from, out var cost))
        {
            if (cost > costs[from]) continue;
            if (goals.Contains(from))
            {
                for (var node = from; node != 0; node = parent[node])
                    path.Add(new(nodes[node].X, nodes[node].Y, nodes[node].Z));
                path.Reverse(); corner = 0; aim = path[0];
                blockedAt = null;
                return DetourOutcome.Detour;
            }
            for (var to = 1; to < nodes.Count; to++)
            {
                if (to == from) continue;
                var end = new WorldMapRouteWaypoint(nodes[to].X, nodes[to].Y, nodes[to].Z);
                var next = cost + Distance(new(nodes[from].X, nodes[from].Y, nodes[from].Z), end);
                if (next >= costs[to] || !Clear(nodes[from], end, obstacles)) continue;
                costs[to] = next; parent[to] = from; queue.Enqueue(to, next);
            }
        }
        return DetourOutcome.Blocked;
    }

    private bool Clear(WorldMapStateSnapshot state, WorldMapRouteWaypoint end, IReadOnlyList<WorldMapEntitySnapshot> obstacles) =>
        !BlockedSegment(state, end, obstacles) && TryGround(state, end, out var ground) &&
        planner.CanTraverseSegment(state, end, ResolveTriangle(ground), exemptEntrances);
    private bool BlockedSegment(WorldMapStateSnapshot state, WorldMapRouteWaypoint end, IReadOnlyList<WorldMapEntitySnapshot> obstacles) =>
        WorldMapVehicleObstacles.BlocksSegment(obstacles, state.PlayerModelId, state.X, state.Z, end.X, end.Z, map.WrapWidth, map.WrapHeight);
    private int ResolveTriangle(WorldMapStateSnapshot state) => planner.TryResolvePlayerTriangle(state, out var id) ? id : -1;
    private bool TryGround(WorldMapStateSnapshot template, WorldMapRouteWaypoint point, out WorldMapStateSnapshot result)
    {
        result = template with { X = point.X, Y = point.Y, Z = point.Z, TerrainId = -1, TerrainScriptId = -1, RegionId = -1 };
        if (!planner.TryResolvePlayerTriangle(result, out var id)) return false;
        var triangle = map.Triangles[id];
        if (!WorldMapTerrainPassability.CanTraverse(template.PlayerModelId, template.WorldMapType, triangle.TerrainId)) return false;
        var a = triangle.Vertex0; var b = triangle.Vertex1; var c = triangle.Vertex2;
        var denominator = (b.Z - c.Z) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Z - c.Z);
        if (Math.Abs(denominator) < 1e-7) return false;
        var wa = ((b.Z - c.Z) * (double)(point.X - c.X) + (c.X - b.X) * (double)(point.Z - c.Z)) / denominator;
        var wb = ((c.Z - a.Z) * (double)(point.X - c.X) + (a.X - c.X) * (double)(point.Z - c.Z)) / denominator;
        if (wa < -1e-7 || wb < -1e-7 || wa + wb > 1 + 1e-7) return false;
        result = result with { Y = (int)Math.Round(wa * a.Y + wb * b.Y + (1 - wa - wb) * c.Y),
            TerrainId = triangle.TerrainId, TerrainScriptId = triangle.TerrainScriptId, RegionId = triangle.RegionId & 31 };
        return true;
    }
    private double Distance(WorldMapRouteWaypoint a, WorldMapRouteWaypoint b)
    {
        var x = (double)WorldMapTargetCatalog.WrappedDelta(a.X, b.X, map.WrapWidth);
        var z = (double)WorldMapTargetCatalog.WrappedDelta(a.Z, b.Z, map.WrapHeight);
        return Math.Sqrt(x * x + z * z);
    }
    private static int Wrap(int value, int width) => width > 0 ? (value % width + width) % width : value;
}
