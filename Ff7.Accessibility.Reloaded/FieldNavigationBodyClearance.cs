namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// What the party's body can actually get through, measured with the game's own movement.
///
/// <para>Route search, the funnel and the corridor lookahead all treat the party as a point.
/// The native step (<c>00636C41</c>/<c>006367B7</c>, replayed by
/// <see cref="FieldNavigationNativeProbeMovement"/>) does not: it tests the heading and both
/// forty-five-degree flanks a collision radius ahead, turns eight angle units away from one
/// obstructed flank, and stops when both are obstructed. So a gap narrower than the body is a
/// wall to the game and an open doorway to the planner. nvmin1_1 (271) is the reported case:
/// its triangle 23 leaves 13 units of floor under a table corner, the shortest route runs
/// through it, and auto walk pushed into it until the player wiggled round.</para>
///
/// <para>Two questions, for one walkmesh, radius and set of native locks:</para>
/// <list type="bullet">
/// <item>Can the body cross this portal? A portal with room for the whole body plus a step is
/// open without a search. A narrower one is searched with the native step, best first over
/// sixteen headings, for a step from one of its triangles into the other, starting wherever
/// near it the whole body fits. With no such step either way it is closed in
/// <see cref="Walkmesh"/>, a copy of the mesh with the same triangle numbers, so every existing
/// search, trace and funnel sees a wall there. Portals are measured when a route wants them,
/// each once, within a time budget per call; the rest wait for a later call.</item>
/// <item>Does a straight leg make native progress at walking (1024) and running (2048) speed?
/// This is for the corridor lookahead's held headings and the immediate input choice.</item>
/// </list>
///
/// <para>The oracle is exact only on the z = 0 faces it was verified on. Anything within reach
/// of an edge into a sloped face is unknown: never closed, never reported clear, and the caller
/// keeps the answer it had before. Native locks are walls to the replay, as they are to the
/// game. Nothing here opens anything: a closed portal only ever removes a way.</para>
/// </summary>
public sealed class FieldNavigationBodyClearance
{
    private const int FixedScale = 4096;
    private const ushort WalkingSpeed = 1024;
    private const ushort RunningSpeed = 2048;
    private const int SearchHeadings = 16;
    private const int MaximumSearchStates = 6000;
    private const double SampleSpacing = 4d;

    private readonly FieldWalkmesh flat;
    private readonly int[] fullToFlat;
    private readonly bool[] supported;
    private readonly (double X1, double Y1, double X2, double Y2)[] walls;
    private readonly (double X1, double Y1, double X2, double Y2)[] unsupportedEdges;
    private readonly HashSet<int> blocked;
    private readonly HashSet<int> blockedFlat;
    private readonly int radius;
    private readonly Dictionary<(int A, int B), bool?> verdicts = new();
    private readonly List<(int A, int B)> closed = new();
    private FieldWalkmesh? cut;

    private FieldNavigationBodyClearance(FieldWalkmesh source, int radius, IReadOnlySet<int> blockedTriangles)
    {
        Source = source;
        this.radius = radius;
        blocked = blockedTriangles.ToHashSet();
        var triangles = source.Triangles;
        supported = new bool[triangles.Count];
        for (var index = 0; index < triangles.Count; index++)
        {
            supported[index] = IsSupportedTriangle(triangles[index], index, triangles.Count);
        }

        fullToFlat = Enumerable.Repeat(-1, triangles.Count).ToArray();
        var flatToFull = new List<int>();
        for (var index = 0; index < triangles.Count; index++)
        {
            if (supported[index])
            {
                fullToFlat[index] = flatToFull.Count;
                flatToFull.Add(index);
            }
        }

        // Native movement walks triangle to triangle across shared edges, so a sloped face
        // is only reachable through the flat edges that border it: those are the unknown
        // edges. A face joined to nothing on this floor - nvmin1_1's z 40 table tops, 2 and
        // 3, sit right over its pinch - cannot be entered and decides nothing.
        var wallList = new List<(double, double, double, double)>();
        var unsupportedList = new List<(double, double, double, double)>();
        var flatTriangles = new FieldWalkmeshTriangle[flatToFull.Count];
        for (var flatIndex = 0; flatIndex < flatToFull.Count; flatIndex++)
        {
            var triangle = triangles[flatToFull[flatIndex]];
            var adjacent = new short[3];
            for (var edge = 0; edge < 3; edge++)
            {
                var neighbour = triangle.GetAdjacentTriangle(edge);
                var (a, b) = triangle.GetEdge(edge);
                if (neighbour < 0 || neighbour >= triangles.Count)
                {
                    adjacent[edge] = -1;
                    wallList.Add((a.X, a.Y, b.X, b.Y));
                }
                else if (!supported[neighbour])
                {
                    adjacent[edge] = -1;
                    unsupportedList.Add((a.X, a.Y, b.X, b.Y));
                }
                else
                {
                    adjacent[edge] = checked((short)fullToFlat[neighbour]);
                    if (blocked.Contains(neighbour) && !blocked.Contains(triangle.Index))
                    {
                        wallList.Add((a.X, a.Y, b.X, b.Y));
                    }
                }
            }

            flatTriangles[flatIndex] = new FieldWalkmeshTriangle(flatIndex, triangle.Vertex0, triangle.Vertex1,
                triangle.Vertex2, adjacent[0], adjacent[1], adjacent[2]);
        }

        flat = new FieldWalkmesh(flatTriangles);
        walls = wallList.ToArray();
        unsupportedEdges = unsupportedList.ToArray();
        blockedFlat = blocked.Where(index => index >= 0 && index < fullToFlat.Length && fullToFlat[index] >= 0)
            .Select(index => fullToFlat[index]).ToHashSet();
    }

    /// <summary>The mesh this was measured on.</summary>
    public FieldWalkmesh Source { get; }

    /// <summary>
    /// <see cref="Source"/> with every portal found closed so far closed on both sides; the same
    /// object as <see cref="Source"/> while nothing is.
    /// </summary>
    public FieldWalkmesh Walkmesh => cut ??= BuildCut();

    public int Radius => radius;

    /// <summary>Closed portals, lower triangle first.</summary>
    public IReadOnlyList<(int A, int B)> ClosedPortals => closed;

    /// <summary>Narrow portals the native step has been asked about.</summary>
    public int SearchedPortals { get; private set; }

    /// <summary>Portals within reach of a face the oracle cannot model.</summary>
    public int UnknownPortals { get; private set; }

    /// <summary>Portals a call's budget ran out before; a later call measures them.</summary>
    public int DeferredPortals { get; private set; }

    public double AnalysisMilliseconds { get; private set; }

    public string Diagnostic =>
        $"body clearance r={radius}: closed portals={(closed.Count == 0 ? "none" : string.Join(";", closed.Select(p => $"{p.A}|{p.B}")))}, " +
        $"searched={SearchedPortals}, unknown={UnknownPortals}" +
        (DeferredPortals > 0 ? $", deferred={DeferredPortals}" : string.Empty) +
        $", {AnalysisMilliseconds:0.0} ms";

    /// <summary>
    /// Prepares a walkmesh for measurement. Null when the oracle has nothing to say: no usable
    /// body radius, or no supported face at all.
    /// </summary>
    public static FieldNavigationBodyClearance? Create(
        FieldWalkmesh walkmesh, double bodyRadius, IReadOnlySet<int> blockedTriangles)
    {
        ArgumentNullException.ThrowIfNull(walkmesh);
        ArgumentNullException.ThrowIfNull(blockedTriangles);
        if (!double.IsFinite(bodyRadius) || bodyRadius < 1d || bodyRadius > 1024d ||
            bodyRadius != Math.Truncate(bodyRadius))
        {
            return null;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var clearance = new FieldNavigationBodyClearance(walkmesh, (int)bodyRadius, blockedTriangles);
        clearance.AnalysisMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return clearance.flat.Triangles.Count == 0 ||
               !FieldNavigationNativeProbeMovement.IsSupportedWalkmesh(clearance.flat)
            ? null
            : clearance;
    }

    /// <summary>Measures every portal of the mesh, without a budget. For audits and tests.</summary>
    public static FieldNavigationBodyClearance? Analyze(
        FieldWalkmesh walkmesh, double bodyRadius, IReadOnlySet<int> blockedTriangles)
    {
        var clearance = Create(walkmesh, bodyRadius, blockedTriangles);
        if (clearance is null)
        {
            return null;
        }

        var portals = new List<(int, int)>();
        for (var index = 0; index < walkmesh.Triangles.Count; index++)
        {
            for (var edge = 0; edge < 3; edge++)
            {
                var neighbour = walkmesh.Triangles[index].GetAdjacentTriangle(edge);
                if (neighbour > index && neighbour < walkmesh.Triangles.Count)
                {
                    portals.Add((index, neighbour));
                }
            }
        }

        clearance.MeasurePortals(portals, TimeSpan.MaxValue);
        return clearance;
    }

    /// <summary>
    /// Measures the portals not yet measured until <paramref name="budget"/> is spent, counted
    /// from <paramref name="clock"/> when one is given so a caller can bound a whole build.
    /// Returns how many were newly found closed.
    ///
    /// <para>The budget is checked inside the native search as well as between portals, and a
    /// search the budget cuts short is only deferred: nothing is decided about that portal and
    /// a later call measures it again. Closing needs a search that ran out of places to go.</para>
    /// </summary>
    public int MeasurePortals(
        IEnumerable<(int From, int To)> portals, TimeSpan budget, System.Diagnostics.Stopwatch? clock = null)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        clock ??= System.Diagnostics.Stopwatch.StartNew();
        var newlyClosed = 0;
        var deferred = 0;
        foreach (var (from, to) in portals)
        {
            var key = from < to ? (from, to) : (to, from);
            if (verdicts.ContainsKey(key))
            {
                continue;
            }

            if (clock.Elapsed >= budget)
            {
                deferred++;
                continue;
            }

            var verdict = Measure(key.Item1, key.Item2, clock, budget);
            if (verdict == Verdict.Deferred)
            {
                deferred++;
                continue;
            }

            verdicts[key] = verdict switch
            {
                Verdict.Open => true,
                Verdict.Closed => false,
                _ => null
            };
            if (verdict == Verdict.Closed)
            {
                closed.Add(key);
                cut = null;
                newlyClosed++;
            }
        }

        DeferredPortals = deferred;
        AnalysisMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return newlyClosed;
    }

    /// <summary>
    /// Whether a portal lies wholly where the oracle is exact: both faces flat and supported and
    /// nothing unknown within the body's reach of it. Only such a portal may be reshaped for the
    /// body; anything else keeps the geometry the planner gave it.
    /// </summary>
    public bool IsPortalExactlyModelled(int from, int to)
    {
        if ((uint)from >= (uint)supported.Length || (uint)to >= (uint)supported.Length ||
            !supported[from] || !supported[to])
        {
            return false;
        }

        var edge = SharedEdge(Source.Triangles[from], to);
        if (edge < 0)
        {
            return false;
        }

        var (p, q) = Source.Triangles[from].GetEdge(edge);
        return DistanceToEdges(unsupportedEdges, p.X, p.Y, q.X, q.Y) >= 2d * radius + 56d;
    }

    /// <summary>The longest straight leg <see cref="IsLegWalkable"/> replays in full.</summary>
    private const int MaximumLegFrames = 160;

    /// <summary>
    /// Whether the body makes native progress along the straight leg at both walking and
    /// running speed. Null when the leg is anywhere the oracle cannot judge, or longer than it
    /// replays: a prefix that ran out of frames without being refused proves nothing about the
    /// rest of the leg either way.
    /// </summary>
    public bool? IsLegWalkable(
        FieldNavigationRouteWaypoint from,
        FieldNavigationFixedPosition? nativeFrom,
        int fromTriangle,
        FieldNavigationRouteWaypoint to)
    {
        // Support is the triangle's, not the reported height: the live position is the
        // rendered one (z -10 on this floor), while the oracle's floor is the mesh's z 0.
        if ((uint)fromTriangle >= (uint)supported.Length || !supported[fromTriangle] ||
            blocked.Contains(fromTriangle))
        {
            return null;
        }

        var dx = (double)to.X - from.X;
        var dy = (double)to.Y - from.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 1d)
        {
            return true;
        }

        if (DistanceToEdges(unsupportedEdges, from.X, from.Y, to.X, to.Y) < radius + 16d)
        {
            return null;
        }

        var start = nativeFrom is { } native && native.X >> 12 == from.X && native.Y >> 12 == from.Y
            ? new FieldNavigationNativeMovementState(native.X, native.Y, 0, fullToFlat[fromTriangle], 0)
            : new FieldNavigationNativeMovementState(from.X * FixedScale, from.Y * FixedScale, 0,
                fullToFlat[fromTriangle], 0);
        var heading = unchecked((byte)(int)Math.Round(Math.Atan2(dx, -dy) * 128d / Math.PI,
            MidpointRounding.AwayFromZero));
        foreach (var speed in new[] { WalkingSpeed, RunningSpeed })
        {
            var stepLength = speed / 256d;
            var needed = (int)Math.Ceiling(distance / stepLength) + 1;
            var frames = Math.Min(MaximumLegFrames, needed);
            var state = start;
            var refused = false;
            for (var frame = 0; frame < frames; frame++)
            {
                var result = FieldNavigationNativeProbeMovement.StepOnSupportedWalkmesh(flat, state, heading,
                    speed, radius, Array.Empty<FieldNavigationDynamicObstacle>(), blockedFlat.Contains);
                if (!result.IsSupported)
                {
                    return null;
                }

                if (!result.Moved)
                {
                    refused = true;
                    break;
                }

                state = result.State with { Heading = heading };
            }

            // The native step turns away from an obstructed flank, so a body sliding along a
            // wall makes less than the whole distance. A leg no longer than two steps must still
            // move the body at least half a step on; a longer one must come within two steps.
            var progress = ((state.FixedX - (double)start.FixedX) * dx + (state.FixedY - (double)start.FixedY) * dy) /
                           (FixedScale * distance);
            var required = distance <= 2d * stepLength
                ? Math.Min(distance, stepLength) / 2d
                : distance - 2d * stepLength;
            if (progress >= required)
            {
                continue;
            }

            // Out of frames rather than refused: the rest of the leg was never walked.
            return refused || frames >= needed ? false : null;
        }

        return true;
    }

    private enum Verdict
    {
        Open,
        Closed,
        Unknown,
        Deferred
    }

    private Verdict Measure(int a, int b, System.Diagnostics.Stopwatch clock, TimeSpan budget)
    {
        var triangles = Source.Triangles;
        if (blocked.Contains(a) || blocked.Contains(b))
        {
            // A native lock is the game's own wall; the route search already refuses it.
            return Verdict.Unknown;
        }

        if (!supported[a] || !supported[b])
        {
            UnknownPortals++;
            return Verdict.Unknown;
        }

        var edge = SharedEdge(triangles[a], b);
        if (edge < 0)
        {
            return Verdict.Unknown;
        }

        var (p, q) = triangles[a].GetEdge(edge);
        var room = MaximumRoomAlong(p, q);
        if (room >= radius + 16d)
        {
            return Verdict.Open;
        }

        if (DistanceToEdges(unsupportedEdges, p.X, p.Y, q.X, q.Y) < 2d * radius + 56d)
        {
            // Room is measured against walls only; beside a slope the oracle cannot tell
            // whether the far side is floor, so nothing is decided.
            UnknownPortals++;
            return Verdict.Unknown;
        }

        SearchedPortals++;
        var forward = CanCross(a, b, p, q, clock, budget);
        if (forward == Search.Found)
        {
            return Verdict.Open;
        }

        var backward = CanCross(b, a, p, q, clock, budget);
        if (backward == Search.Found)
        {
            return Verdict.Open;
        }

        if (forward == Search.OutOfTime || backward == Search.OutOfTime)
        {
            SearchedPortals--;
            return Verdict.Deferred;
        }

        // Closed only when both searches ran out of places to go. A search stopped by its
        // state cap is not evidence of a wall, and stays unknown.
        if (forward == Search.Exhausted && backward == Search.Exhausted)
        {
            return Verdict.Closed;
        }

        UnknownPortals++;
        return Verdict.Unknown;
    }

    private enum Search
    {
        Found,
        Exhausted,
        CapReached,
        OutOfTime
    }

    /// <summary>
    /// A native walk that steps from the <paramref name="from"/> triangle straight into the
    /// <paramref name="to"/> one. It may start anywhere near the portal the whole body fits,
    /// on either side or beyond: a thin triangle such as nvmin1_1's 19 has no such spot of its
    /// own and is still crossed in passing, which the log shows the player do. Only a native
    /// step across this very portal counts, not arriving by another.
    ///
    /// <para>A search over a 4-unit lattice and sixteen headings near the portal, not a proof
    /// over every approach: <see cref="Search.Exhausted"/> means no crossing on that lattice.</para>
    /// </summary>
    private Search CanCross(int from, int to, FieldWalkmeshVertex a, FieldWalkmeshVertex b,
        System.Diagnostics.Stopwatch clock, TimeSpan budget)
    {
        var window = radius + 40d;
        var minX = Math.Min(a.X, b.X) - window;
        var maxX = Math.Max(a.X, b.X) + window;
        var minY = Math.Min(a.Y, b.Y) - window;
        var maxY = Math.Max(a.Y, b.Y) + window;
        var frontier = new PriorityQueue<FieldNavigationNativeMovementState, double>();
        var seen = new HashSet<(int, int)>();
        for (var x = minX; x <= maxX; x += SampleSpacing)
        {
            if (clock.Elapsed >= budget)
            {
                return Search.OutOfTime;
            }

            for (var y = minY; y <= maxY; y += SampleSpacing)
            {
                var distance = DistanceToSegment(x, y, a.X, a.Y, b.X, b.Y);
                if (distance > window || RoomAt(x, y) < radius + 9d)
                {
                    continue;
                }

                var triangle = ResolveFlat(x, y);
                if (triangle < 0 || blockedFlat.Contains(triangle))
                {
                    continue;
                }

                var ix = (int)Math.Round(x);
                var iy = (int)Math.Round(y);
                if (seen.Add((ix >> 2, iy >> 2)))
                {
                    frontier.Enqueue(new FieldNavigationNativeMovementState(ix * FixedScale, iy * FixedScale, 0,
                        triangle, 0), distance);
                }
            }
        }

        var source = fullToFlat[from];
        var target = fullToFlat[to];
        var expanded = 0;
        while (frontier.TryDequeue(out var state, out _))
        {
            if (expanded >= MaximumSearchStates)
            {
                return Search.CapReached;
            }

            if ((expanded & 7) == 0 && clock.Elapsed >= budget)
            {
                return Search.OutOfTime;
            }

            expanded++;
            for (var direction = 0; direction < SearchHeadings; direction++)
            {
                var heading = (byte)(direction * (256 / SearchHeadings));
                var result = FieldNavigationNativeProbeMovement.StepOnSupportedWalkmesh(flat, state, heading,
                    WalkingSpeed, radius, Array.Empty<FieldNavigationDynamicObstacle>(), blockedFlat.Contains);
                if (!result.IsSupported || !result.Moved)
                {
                    continue;
                }

                var next = result.State;
                if (state.TriangleId == source && next.TriangleId == target)
                {
                    return Search.Found;
                }

                var px = next.FixedX / (double)FixedScale;
                var py = next.FixedY / (double)FixedScale;
                var distance = DistanceToSegment(px, py, a.X, a.Y, b.X, b.Y);
                if (distance > window || !seen.Add((next.FixedX >> 14, next.FixedY >> 14)))
                {
                    continue;
                }

                // Best first: toward the portal, and on its near side before its far one.
                frontier.Enqueue(next with { Heading = 0 }, distance + (next.TriangleId == source ? 0d : 8d));
            }
        }

        return Search.Exhausted;
    }

    private FieldWalkmesh BuildCut()
    {
        if (closed.Count == 0)
        {
            return Source;
        }

        var closedSet = closed.SelectMany(pair => new[] { (pair.A, pair.B), (pair.B, pair.A) }).ToHashSet();
        short Adjacent(FieldWalkmeshTriangle triangle, int edge)
        {
            var neighbour = triangle.GetAdjacentTriangle(edge);
            return closedSet.Contains((triangle.Index, neighbour)) ? (short)-1 : neighbour;
        }

        return new FieldWalkmesh(Source.Triangles.Select(triangle => triangle with
        {
            Adjacent0 = Adjacent(triangle, 0),
            Adjacent1 = Adjacent(triangle, 1),
            Adjacent2 = Adjacent(triangle, 2)
        }).ToArray());
    }

    private static int SharedEdge(FieldWalkmeshTriangle triangle, int neighbour)
    {
        for (var edge = 0; edge < 3; edge++)
        {
            if (triangle.GetAdjacentTriangle(edge) == neighbour)
            {
                return edge;
            }
        }

        return -1;
    }

    private int ResolveFlat(double x, double y)
    {
        foreach (var triangle in flat.Triangles)
        {
            if (ContainsInPlan(triangle, x, y))
            {
                return triangle.Index;
            }
        }

        return -1;
    }

    private double MaximumRoomAlong(FieldWalkmeshVertex a, FieldWalkmeshVertex b)
    {
        var length = Math.Sqrt((b.X - a.X) * (double)(b.X - a.X) + (b.Y - a.Y) * (double)(b.Y - a.Y));
        var samples = Math.Max(2, (int)Math.Ceiling(length / SampleSpacing));
        var best = 0d;
        for (var sample = 0; sample <= samples; sample++)
        {
            var t = sample / (double)samples;
            best = Math.Max(best, RoomAt(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
        }

        return best;
    }

    private double RoomAt(double x, double y)
    {
        var best = double.PositiveInfinity;
        foreach (var (x1, y1, x2, y2) in walls)
        {
            best = Math.Min(best, DistanceToSegment(x, y, x1, y1, x2, y2));
        }

        return best;
    }

    private static double DistanceToEdges(
        (double X1, double Y1, double X2, double Y2)[] edges, double ax, double ay, double bx, double by)
    {
        var best = double.PositiveInfinity;
        foreach (var (x1, y1, x2, y2) in edges)
        {
            best = Math.Min(best, SegmentDistance(ax, ay, bx, by, x1, y1, x2, y2));
        }

        return best;
    }

    private static bool IsSupportedTriangle(FieldWalkmeshTriangle triangle, int index, int count)
    {
        if (triangle.Index != index || triangle.Vertex0.Z != 0 || triangle.Vertex1.Z != 0 || triangle.Vertex2.Z != 0)
        {
            return false;
        }

        var (a, b, c) = (triangle.Vertex0, triangle.Vertex1, triangle.Vertex2);
        if ((long)(b.X - a.X) * (c.Y - a.Y) - (long)(b.Y - a.Y) * (c.X - a.X) >= 0)
        {
            return false;
        }

        for (var edge = 0; edge < 3; edge++)
        {
            if (triangle.GetAdjacentTriangle(edge) >= count)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsInPlan(FieldWalkmeshTriangle triangle, double x, double y)
    {
        var (a, b, c) = (triangle.Vertex0, triangle.Vertex1, triangle.Vertex2);
        double Cross(FieldWalkmeshVertex p, FieldWalkmeshVertex q) => (q.X - p.X) * (y - p.Y) - (q.Y - p.Y) * (x - p.X);
        var (d1, d2, d3) = (Cross(a, b), Cross(b, c), Cross(c, a));
        return (d1 <= 0 && d2 <= 0 && d3 <= 0) || (d1 >= 0 && d2 >= 0 && d3 >= 0);
    }

    private static double DistanceToSegment(double x, double y, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared <= 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / lengthSquared, 0, 1);
        var px = ax + dx * t - x;
        var py = ay + dy * t - y;
        return Math.Sqrt(px * px + py * py);
    }

    private static double SegmentDistance(
        double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double Side(double px, double py, double qx, double qy, double rx, double ry) =>
            (qx - px) * (ry - py) - (qy - py) * (rx - px);
        var d1 = Side(ax, ay, bx, by, cx, cy);
        var d2 = Side(ax, ay, bx, by, dx, dy);
        var d3 = Side(cx, cy, dx, dy, ax, ay);
        var d4 = Side(cx, cy, dx, dy, bx, by);
        if (d1 * d2 < 0 && d3 * d4 < 0)
        {
            return 0d;
        }

        return Math.Min(
            Math.Min(DistanceToSegment(ax, ay, cx, cy, dx, dy), DistanceToSegment(bx, by, cx, cy, dx, dy)),
            Math.Min(DistanceToSegment(cx, cy, ax, ay, bx, by), DistanceToSegment(dx, dy, ax, ay, bx, by)));
    }
}
