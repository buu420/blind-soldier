namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The Ancient Forest's Mutant Flytraps as ground auto walk keeps away from (620 anfrst_1,
/// 622 anfrst_3, 623 anfrst_4).
///
/// <para>Each jaw is a LINE entity whose touch slot runs the party leader's bite: HPD 1000 then
/// HPU 1 on all three members, the party placed in the mouth, and a JUMP back out on the jaw's
/// own side. The engine counts a touch as 00637879 and 00637ABB do
/// (<see cref="FieldNativeLineContact"/>): the foot of the leader's projection inside the
/// segment's X and Y extent and the squared 3D distance under the leader's +0x72 radius, which
/// field set-up (0060BCFA) makes 30 here. 622's only walkable link between its halves is its
/// flytrap's mouth, and on 2026-11-04 0.10.8's auto walk to the Spring Gun Clip went from the
/// bite landing (179,-962) back into bigm0 37 times, until the party sat at 1 HP and the next
/// battle ended the game.</para>
///
/// <para>The five reviewed Echo-S layouts keep the stock walkmesh, LINE coordinates and bank
/// gates, so the same geometry serves them. Their entity numbers are not the stock ones - both
/// 620's and 623's jaws are renumbered there - and nothing here is looked up by number: the
/// entity is only a jaw's name in a diagnostic.</para>
/// </summary>
internal static class AncientForestRouteHazards
{
    /// <summary>The leader's +0x72 after field set-up (0060BCFA) in all three fields.</summary>
    internal const int NativeLeaderRadius = 30;

    /// <summary>
    /// How far outside the engine's own touch region a walk is kept. Auto walk steers with eight
    /// keyboard directions, up to 22.5 degrees off its leg, and checks its next step only between
    /// samples: sixteen units is that error over some forty units of running. The private raster
    /// of 2026-11-04 shows it closes no region the native rule leaves open, only the knife-edge
    /// ways into 622's throat and 620's Slash-All pocket, which those objects hand to the player.
    /// </summary>
    internal const double RouteMargin = 16d;

    private const int NoGate = -1;
    private const double SampleStep = 1d;

    /// <summary>How much nearer than its start a walk out of the margin may sample, for rounding.</summary>
    private const double LeavingTolerance = 0.5d;

    /// <summary>One jaw: its LINE entity, and the temporary-bank byte its touch script tests (-1: none).</summary>
    internal readonly record struct Jaw(int FieldId, int EntityId, string Name, FieldNavigationTriggerLine Line, int GateAddress)
    {
        public override string ToString() => $"{FieldId} e{EntityId} {Name}";
    }

    /// <summary>A point of a walk nearer a biting jaw than it may come.</summary>
    internal readonly record struct Contact(Jaw Jaw, double X, double Y, double Z, double Clearance)
    {
        public override string ToString() =>
            $"{Jaw} at {X:0},{Y:0},{Z:0}, {Clearance:0.0} from its bite";
    }

    // Each jaw's touch (slot 4) requests the leader's bite; the gated ones test their byte first
    // (IFUB [5][n] == 0). The beehive's throw sets that byte for the rest of the visit and locks
    // the shut mouth: 620 t123-124 for flytrap 1, t207-209 and t217-219 for flytrap 2; 623 t114-115.
    private static readonly Jaw[] Field620 =
    [
        new(620, 25, "big0lt", new(-270, -207, 0, -270, -546, 0), 51),
        new(620, 26, "big0rt", new(21, -263, 5, -1, -611, 0), 51),
        new(620, 27, "big1lt", new(1441, -26, -12, 1315, -384, -11), 52),
        new(620, 28, "big1rt", new(1700, -65, -11, 1433, -560, -12), 52),
    ];

    // No beehive and no gate: bigm0 throws the party back out to (179,-962) t185, bigm1 to
    // (726,-846) t241.
    private static readonly Jaw[] Field622 =
    [
        new(622, 22, "bigm0", new(406, -868, 22, 406, -968, 25), NoGate),
        new(622, 23, "bigm1", new(493, -863, 22, 485, -963, 18), NoGate),
    ];

    private static readonly Jaw[] Field623 =
    [
        new(623, 31, "big0up", new(443, -178, 37, 663, -178, 37), 61),
        new(623, 32, "big0dn", new(390, -399, 37, 646, -458, 37), 61),
        new(623, 33, "big0dn1", new(646, -458, 37, 767, -335, 37), 61),
    ];

    internal static IReadOnlyList<Jaw> Jaws(int fieldId) => fieldId switch
    {
        620 => Field620,
        622 => Field622,
        623 => Field623,
        _ => Array.Empty<Jaw>()
    };

    /// <summary>
    /// The jaws that bite now. A gated jaw is left out only when a read of the field's temporary
    /// bank holds its byte non-zero: no bank, or one too short to hold the byte, keeps it biting.
    /// </summary>
    internal static IReadOnlyList<Jaw> ActiveJaws(int fieldId, byte[]? temporaryBank)
    {
        var jaws = Jaws(fieldId);
        if (jaws.Count == 0 || temporaryBank is null)
        {
            return jaws;
        }

        return jaws.Where(jaw => jaw.GateAddress == NoGate ||
                                 jaw.GateAddress >= temporaryBank.Length ||
                                 temporaryBank[jaw.GateAddress] == 0)
            .ToArray();
    }

    /// <summary>
    /// The 3D distance from a point to the region where the engine can count a touch with this
    /// radius; zero inside it. The region ends flat at both ends of the segment, as 00637879's
    /// foot test does: past an end there is no touch at any distance. It is a little larger than
    /// the exact geometry, never smaller: 00637879 truncates the foot's position along the line
    /// to 1/256 steps toward zero, so a point up to one step past an end still projects onto it
    /// (2.2 units on 620's big1rt), and it floors the foot's coordinates, so the distance it
    /// squares can be up to about 1.7 shorter than the true one.
    /// </summary>
    internal static double Clearance(FieldNavigationTriggerLine line, double x, double y, double z, double radius)
    {
        double startX = line.StartX, startY = line.StartY, startZ = line.StartZ;
        double alongX = line.EndX - startX, alongY = line.EndY - startY, alongZ = line.EndZ - startZ;
        var lengthSquared = alongX * alongX + alongY * alongY + alongZ * alongZ;
        if (lengthSquared <= 0d)
        {
            // 00637879 answers -1 for a line of no length: it never touches.
            return double.PositiveInfinity;
        }

        var length = Math.Sqrt(lengthSquared);
        var amount = ((x - startX) * alongX + (y - startY) * alongY + (z - startZ) * alongZ) / lengthSquared;
        var offX = startX + amount * alongX - x;
        var offY = startY + amount * alongY - y;
        var offZ = startZ + amount * alongZ - z;
        var beyond = (amount < 0d ? -amount : amount > 1d ? amount - 1d : 0d) * length;
        beyond = Math.Max(0d, beyond - (length / 256d + NativeRoundingSlack));
        var outside = Math.Max(0d, Math.Sqrt(offX * offX + offY * offY + offZ * offZ) - radius - NativeRoundingSlack);
        return Math.Sqrt(beyond * beyond + outside * outside);
    }

    /// <summary>Whether a point, at its own floor height, keeps the margin from every jaw's touch.</summary>
    internal static bool IsClear(FieldNavigationRouteWaypoint point, IReadOnlyList<Jaw> jaws, double radius) =>
        jaws.All(jaw => Clearance(jaw.Line, point.X, point.Y, point.Z, radius) >= RouteMargin);

    /// <summary>
    /// Whole frames the leader can move from (x, y, z) at <paramref name="unitsPerFrame"/> in
    /// plan, by any path - a wall slide included - before the engine's touch could fire on one
    /// of <paramref name="jaws"/>. The clearance is a distance, so no shorter path reaches the
    /// touch; a frame covers at most its plan step lifted by the steepest floor that close.
    /// </summary>
    internal static int SafeFrames(FieldWalkmesh walkmesh, IReadOnlyList<Jaw> jaws, double radius,
        double x, double y, double z, double unitsPerFrame)
    {
        var clearance = jaws.Select(jaw => Clearance(jaw.Line, x, y, z, radius)).DefaultIfEmpty(double.PositiveInfinity).Min();
        if (double.IsPositiveInfinity(clearance))
        {
            return int.MaxValue;
        }

        var steepest = walkmesh.Triangles
            .Where(triangle => TrianglePlanDistance(triangle, x, y) <= clearance)
            .Select(Steepness)
            .Where(double.IsFinite)
            .DefaultIfEmpty(0d)
            .Max();
        return (int)Math.Min(int.MaxValue, Math.Floor(clearance / (unitsPerFrame * Math.Sqrt(1d + steepest * steepest))));
    }

    /// <summary>
    /// How much a triangle's floor rises per unit of plan distance at most. One with no area in
    /// plan has no floor to stand on, and answers infinity for the caller to pass over.
    /// </summary>
    private static double Steepness(FieldWalkmeshTriangle triangle)
    {
        double ax = triangle.Vertex1.X - triangle.Vertex0.X, ay = triangle.Vertex1.Y - triangle.Vertex0.Y, az = triangle.Vertex1.Z - triangle.Vertex0.Z;
        double bx = triangle.Vertex2.X - triangle.Vertex0.X, by = triangle.Vertex2.Y - triangle.Vertex0.Y, bz = triangle.Vertex2.Z - triangle.Vertex0.Z;
        var normalX = ay * bz - az * by;
        var normalY = az * bx - ax * bz;
        var normalZ = ax * by - ay * bx;
        return normalZ == 0d
            ? double.PositiveInfinity
            : Math.Sqrt(normalX * normalX + normalY * normalY) / Math.Abs(normalZ);
    }

    /// <summary>The foot's floored coordinates: up to one unit on each axis.</summary>
    private const double NativeRoundingSlack = 2d;

    /// <summary>
    /// The plan distance from a jaw's segment beyond which no point is within the margin of its
    /// touch: everything <see cref="Clearance"/> leaves nearer than the margin lies within the
    /// margin of the radius and rounding about the segment stretched by its end step, and a plan
    /// distance is never more than the 3D one. One unit more for the sampling.
    /// </summary>
    private static double Reach(Jaw jaw, double radius)
    {
        var line = jaw.Line;
        double alongX = line.EndX - line.StartX, alongY = line.EndY - line.StartY, alongZ = line.EndZ - line.StartZ;
        return radius + RouteMargin + 2d * NativeRoundingSlack +
               Math.Sqrt(alongX * alongX + alongY * alongY + alongZ * alongZ) / 256d + 1d;
    }

    /// <summary>
    /// The first point of a straight walk from <paramref name="from"/> to <paramref name="to"/>
    /// that comes within <see cref="RouteMargin"/> of a jaw's touch, each point at the height of
    /// the floor under it. A walk that starts where the leader stands inside the margin (only
    /// <paramref name="fromLeader"/>) is let out: it may never come nearer than it started and has
    /// to end further away. A leader already touching has no clear walk.
    /// </summary>
    internal static bool TryFindContact(
        FieldWalkmesh walkmesh,
        FieldNavigationRouteWaypoint from,
        FieldNavigationRouteWaypoint to,
        IReadOnlyList<Jaw> jaws,
        double radius,
        bool fromLeader,
        out Contact contact)
    {
        contact = default;
        var dx = to.X - (double)from.X;
        var dy = to.Y - (double)from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var samples = Math.Max(1, (int)Math.Ceiling(length / SampleStep));
        foreach (var jaw in jaws)
        {
            var reach = Reach(jaw, radius);
            if (PlanDistance(from.X, from.Y, to.X, to.Y, jaw.Line) >= reach)
            {
                continue;
            }

            var started = double.PositiveInfinity;
            if (fromLeader)
            {
                started = Clearance(jaw.Line, from.X, from.Y, FloorHeight(walkmesh, from.X, from.Y, from.Z), radius);
            }

            var leaving = started < RouteMargin;
            for (var sample = 0; sample <= samples; sample++)
            {
                var amount = sample / (double)samples;
                var x = from.X + dx * amount;
                var y = from.Y + dy * amount;
                if (PointPlanDistance(x, y, jaw.Line) >= reach)
                {
                    continue;
                }

                var z = FloorHeight(walkmesh, x, y, from.Z + (to.Z - from.Z) * amount);
                var clearance = Clearance(jaw.Line, x, y, z, radius);
                if (clearance >= RouteMargin ||
                    (leaving && started > 0d && clearance >= started - LeavingTolerance))
                {
                    continue;
                }

                contact = new Contact(jaw, x, y, z, clearance);
                return true;
            }

            if (leaving && length >= 0.5d)
            {
                var endZ = FloorHeight(walkmesh, to.X, to.Y, to.Z);
                var ended = Clearance(jaw.Line, to.X, to.Y, endZ, radius);
                if (ended < Math.Min(RouteMargin, started + 1d))
                {
                    contact = new Contact(jaw, to.X, to.Y, endZ, ended);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The legs auto walk walks for a route's steps: each straight from the last, except that a
    /// scripted traversal (a jump, a vine) is walked to its entry and the walk resumes at its exit.
    /// The funnel passes through every traversal's entry, as a step or along a leg. Each leg says
    /// which step it ends at, or -1 where it ends at an entry the steps do not name.
    /// </summary>
    internal static IReadOnlyList<(FieldNavigationRouteWaypoint From, FieldNavigationRouteWaypoint To, int Step)> WalkedLegs(
        FieldNavigationRouteWaypoint start,
        IReadOnlyList<FieldNavigationRouteStep> steps,
        IReadOnlyList<FieldNavigationRoutePortal> portals)
    {
        var traversals = portals.Where(portal => portal.TransitionKind is not null).ToArray();
        var legs = new List<(FieldNavigationRouteWaypoint, FieldNavigationRouteWaypoint, int)>(steps.Count + traversals.Length);
        var from = start;
        var next = 0;
        for (var index = 0; index < steps.Count; index++)
        {
            var waypoint = steps[index].Waypoint;
            var arrivedAtEntry = false;
            while (next < traversals.Length && LiesOnLeg(traversals[next].Left, from, waypoint))
            {
                arrivedAtEntry = traversals[next].Left == waypoint;
                legs.Add((from, traversals[next].Left, arrivedAtEntry ? index : -1));
                from = traversals[next].TransitionExit ?? traversals[next].Left;
                next++;
                if (arrivedAtEntry)
                {
                    break;
                }
            }

            if (!arrivedAtEntry)
            {
                legs.Add((from, waypoint, index));
                from = waypoint;
            }
        }

        return legs;
    }

    /// <summary>The first contact of a route's walked legs; the first leg is the leader's own.</summary>
    internal static bool TryFindRouteContact(
        FieldWalkmesh walkmesh,
        FieldNavigationRouteWaypoint start,
        IReadOnlyList<FieldNavigationRouteStep> steps,
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        IReadOnlyList<Jaw> jaws,
        double radius,
        out Contact contact) =>
        TryFindContactLeg(walkmesh, start, steps, portals, jaws, radius, out _, out _, out _, out contact);

    /// <summary>
    /// <see cref="TryFindRouteContact"/> with the leg it found: where it starts, whether that is
    /// the leader's own first leg, and the step it walks to (-1: a traversal's entry).
    /// </summary>
    internal static bool TryFindContactLeg(
        FieldWalkmesh walkmesh,
        FieldNavigationRouteWaypoint start,
        IReadOnlyList<FieldNavigationRouteStep> steps,
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        IReadOnlyList<Jaw> jaws,
        double radius,
        out int step,
        out FieldNavigationRouteWaypoint from,
        out bool fromLeader,
        out Contact contact)
    {
        var legs = WalkedLegs(start, steps, portals);
        for (var index = 0; index < legs.Count; index++)
        {
            fromLeader = index == 0 && legs[index].From == start;
            if (TryFindContact(walkmesh, legs[index].From, legs[index].To, jaws, radius, fromLeader, out contact))
            {
                step = legs[index].Step;
                from = legs[index].From;
                return true;
            }
        }

        step = -1;
        from = default;
        fromLeader = false;
        contact = default;
        return false;
    }

    /// <summary>
    /// The corners of the plan rectangle a jaw's margin keeps clear, two units further out, the
    /// shortest way round from <paramref name="from"/> to <paramref name="to"/> first: the bends a
    /// walk that only skirts the bite can take instead. Each is outside the margin by construction.
    /// </summary>
    internal static IReadOnlyList<(double X, double Y)> CornersAround(
        Jaw jaw, double radius, FieldNavigationRouteWaypoint from, FieldNavigationRouteWaypoint to)
    {
        var line = jaw.Line;
        double alongX = line.EndX - line.StartX, alongY = line.EndY - line.StartY, alongZ = line.EndZ - line.StartZ;
        var planLength = Math.Sqrt(alongX * alongX + alongY * alongY);
        if (planLength < 1d)
        {
            return Array.Empty<(double, double)>();
        }

        var unitX = alongX / planLength;
        var unitY = alongY / planLength;
        var half = radius + NativeRoundingSlack + RouteMargin + CornerSlack;
        var past = Math.Sqrt(alongX * alongX + alongY * alongY + alongZ * alongZ) / 256d +
                   NativeRoundingSlack + RouteMargin + CornerSlack;
        (double, double) At(double x, double y, double along, double side) =>
            (x + unitX * along - unitY * side, y + unitY * along + unitX * side);
        return new[]
            {
                At(line.StartX, line.StartY, -past, half), At(line.StartX, line.StartY, -past, -half),
                At(line.EndX, line.EndY, past, half), At(line.EndX, line.EndY, past, -half)
            }
            .OrderBy(corner => Math.Sqrt(Math.Pow(corner.Item1 - from.X, 2) + Math.Pow(corner.Item2 - from.Y, 2)) +
                               Math.Sqrt(Math.Pow(to.X - corner.Item1, 2) + Math.Pow(to.Y - corner.Item2, 2)))
            .ToArray();
    }

    /// <summary>How far outside the margin a bend round a jaw is put.</summary>
    private const double CornerSlack = 2d;

    /// <summary>
    /// The triangles under every point where a route's walked legs come within the margin: the
    /// ground a replan has to keep off.
    /// </summary>
    internal static IReadOnlySet<int> TrianglesUnderContacts(
        FieldWalkmesh walkmesh,
        FieldNavigationRouteWaypoint start,
        IReadOnlyList<FieldNavigationRouteStep> steps,
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        IReadOnlyList<Jaw> jaws,
        double radius)
    {
        var triangles = new HashSet<int>();
        foreach (var (from, to, _) in WalkedLegs(start, steps, portals))
        {
            var dx = to.X - (double)from.X;
            var dy = to.Y - (double)from.Y;
            var samples = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / SampleStep));
            foreach (var jaw in jaws)
            {
                var reach = Reach(jaw, radius);
                if (PlanDistance(from.X, from.Y, to.X, to.Y, jaw.Line) >= reach)
                {
                    continue;
                }

                for (var sample = 0; sample <= samples; sample++)
                {
                    var amount = sample / (double)samples;
                    var x = from.X + dx * amount;
                    var y = from.Y + dy * amount;
                    if (PointPlanDistance(x, y, jaw.Line) >= reach)
                    {
                        continue;
                    }

                    var triangle = FloorTriangle(walkmesh, x, y, from.Z + (to.Z - from.Z) * amount, out var z);
                    if (triangle >= 0 && Clearance(jaw.Line, x, y, z, radius) < RouteMargin)
                    {
                        triangles.Add(triangle);
                    }
                }
            }
        }

        return triangles;
    }

    /// <summary>
    /// Every triangle some part of which may lie within the margin of a jaw's touch: within
    /// <see cref="Reach"/> of the segment in plan, and not wholly above or below it.
    /// </summary>
    internal static IReadOnlySet<int> TrianglesNear(FieldWalkmesh walkmesh, IReadOnlyList<Jaw> jaws, double radius)
    {
        var triangles = new HashSet<int>();
        foreach (var triangle in walkmesh.Triangles)
        {
            var low = Math.Min(triangle.Vertex0.Z, Math.Min(triangle.Vertex1.Z, triangle.Vertex2.Z));
            var high = Math.Max(triangle.Vertex0.Z, Math.Max(triangle.Vertex1.Z, triangle.Vertex2.Z));
            foreach (var jaw in jaws)
            {
                var line = jaw.Line;
                var reach = Reach(jaw, radius);
                if (high < Math.Min(line.StartZ, line.EndZ) - reach || low > Math.Max(line.StartZ, line.EndZ) + reach ||
                    TrianglePlanDistance(triangle, line) >= reach)
                {
                    continue;
                }

                triangles.Add(triangle.Index);
                break;
            }
        }

        return triangles;
    }

    /// <summary>The height of the floor under a point: the triangle covering it nearest the hint, else the hint.</summary>
    internal static double FloorHeight(FieldWalkmesh walkmesh, double x, double y, double zHint)
    {
        _ = FloorTriangle(walkmesh, x, y, zHint, out var z);
        return z;
    }

    private static int FloorTriangle(FieldWalkmesh walkmesh, double x, double y, double zHint, out double z)
    {
        var best = -1;
        z = zHint;
        var bestError = double.PositiveInfinity;
        foreach (var triangle in walkmesh.Triangles)
        {
            if (TryPlaneHeight(triangle, x, y, out var height) && Math.Abs(height - zHint) < bestError)
            {
                bestError = Math.Abs(height - zHint);
                best = triangle.Index;
                z = height;
            }
        }

        return best;
    }

    private static bool TryPlaneHeight(FieldWalkmeshTriangle triangle, double x, double y, out double height)
    {
        height = 0d;
        var (a, b, c) = (triangle.Vertex0, triangle.Vertex1, triangle.Vertex2);
        if (x < Math.Min(a.X, Math.Min(b.X, c.X)) || x > Math.Max(a.X, Math.Max(b.X, c.X)) ||
            y < Math.Min(a.Y, Math.Min(b.Y, c.Y)) || y > Math.Max(a.Y, Math.Max(b.Y, c.Y)))
        {
            return false;
        }

        var denominator = (b.Y - c.Y) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Y - c.Y);
        if (Math.Abs(denominator) < 1e-9)
        {
            return false;
        }

        var weightA = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / denominator;
        var weightB = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / denominator;
        var weightC = 1d - weightA - weightB;
        if (weightA < -1e-9 || weightB < -1e-9 || weightC < -1e-9)
        {
            return false;
        }

        height = weightA * a.Z + weightB * b.Z + weightC * c.Z;
        return true;
    }

    private static bool LiesOnLeg(FieldNavigationRouteWaypoint point, FieldNavigationRouteWaypoint from, FieldNavigationRouteWaypoint to) =>
        PointSegmentDistance(point.X, point.Y, from.X, from.Y, to.X, to.Y) < 1d;

    private static double PointPlanDistance(double x, double y, FieldNavigationTriggerLine line) =>
        PointSegmentDistance(x, y, line.StartX, line.StartY, line.EndX, line.EndY);

    /// <summary>The least plan distance between a leg and a LINE's segment.</summary>
    private static double PlanDistance(double fromX, double fromY, double toX, double toY, FieldNavigationTriggerLine line)
    {
        if (SegmentsCross(fromX, fromY, toX, toY, line.StartX, line.StartY, line.EndX, line.EndY))
        {
            return 0d;
        }

        return Math.Min(
            Math.Min(PointSegmentDistance(fromX, fromY, line.StartX, line.StartY, line.EndX, line.EndY),
                PointSegmentDistance(toX, toY, line.StartX, line.StartY, line.EndX, line.EndY)),
            Math.Min(PointSegmentDistance(line.StartX, line.StartY, fromX, fromY, toX, toY),
                PointSegmentDistance(line.EndX, line.EndY, fromX, fromY, toX, toY)));
    }

    /// <summary>The least plan distance from a triangle to a point: zero inside it.</summary>
    internal static double TrianglePlanDistance(FieldWalkmeshTriangle triangle, double x, double y)
    {
        if (TryPlaneHeight(triangle, x, y, out _))
        {
            return 0d;
        }

        var best = double.PositiveInfinity;
        for (var edge = 0; edge < 3; edge++)
        {
            var (a, b) = triangle.GetEdge(edge);
            best = Math.Min(best, PointSegmentDistance(x, y, a.X, a.Y, b.X, b.Y));
        }

        return best;
    }

    /// <summary>The least plan distance from a triangle to a LINE's segment: zero where they meet.</summary>
    internal static double TrianglePlanDistance(FieldWalkmeshTriangle triangle, FieldNavigationTriggerLine line)
    {
        if (TryPlaneHeight(triangle, line.StartX, line.StartY, out _) || TryPlaneHeight(triangle, line.EndX, line.EndY, out _))
        {
            return 0d;
        }

        var best = double.PositiveInfinity;
        for (var edge = 0; edge < 3; edge++)
        {
            var (a, b) = triangle.GetEdge(edge);
            best = Math.Min(best, PlanDistance(a.X, a.Y, b.X, b.Y, line));
        }

        return best;
    }

    private static double PointSegmentDistance(double x, double y, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var amount = lengthSquared <= 0d ? 0d : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / lengthSquared, 0d, 1d);
        var offX = ax + dx * amount - x;
        var offY = ay + dy * amount - y;
        return Math.Sqrt(offX * offX + offY * offY);
    }

    private static bool SegmentsCross(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        static double Side(double px, double py, double qx, double qy, double rx, double ry) =>
            (qx - px) * (ry - py) - (qy - py) * (rx - px);

        var d1 = Side(cx, cy, dx, dy, ax, ay);
        var d2 = Side(cx, cy, dx, dy, bx, by);
        var d3 = Side(ax, ay, bx, by, cx, cy);
        var d4 = Side(ax, ay, bx, by, dx, dy);
        return ((d1 > 0d && d2 < 0d) || (d1 < 0d && d2 > 0d)) && ((d3 > 0d && d4 < 0d) || (d3 < 0d && d4 > 0d));
    }
}
