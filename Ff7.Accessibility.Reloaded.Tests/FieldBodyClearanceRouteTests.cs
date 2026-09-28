using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Auto walk that stalls against a wall until the player wiggles round it, replayed on the
/// installed walkmeshes with the game's own movement step.
///
/// <para>The 2026-09-28 user log. Field 271 (nvmin1_1, scale 512, leader radius 30):
/// at 01:14:45..50 auto walk to the upstairs Exit commanded Right and UpRight at
/// (-80,309) with no movement; at 01:18:09..14 auto walk to Exit to Nibelheim commanded
/// Left and DownLeft at (58,299) and got no further than (54,298). Both times the route
/// ran under the vertex (-9,288), where triangle 23 leaves the body about 13 units of
/// floor. The player got round by hand along 19, 20, 26, 32, 28, 29 - over the top of
/// the table.</para>
///
/// <para>Field 308 (sininb42, scale 2048, leader radius 120, the Director's locks on 18,
/// 55 and 56): at 01:42:50..55 auto walk to Return through the basement library held
/// DownRight at (437,1720) with waypoints sliding (399,1604), (429,1613), (459,1625),
/// (474,1631) - a held heading whose centre line is clear and whose body is not.</para>
///
/// <para>The movement is <see cref="FieldNavigationNativeProbeMovement.Step"/>, the
/// verified flat-floor subset of 00636C41/006367B7, at both native speeds (1024 walking,
/// 2048 running) and several frames per navigation sample. It runs only on the exact
/// z = 0 faces; their edges to anything else are closed, so a replay can only stop
/// short there, never walk through. Success is the real activation - crossing the
/// line - or, where the exit is up a staircase the oracle cannot model, standing at the
/// foot of that staircase with nothing but the unsupported slope in the way.</para>
/// </summary>
internal static class FieldBodyClearanceRouteTests
{
    private readonly record struct Case(
        string Name, int FieldId, int Radius, int[] Locks, FieldNavigationTarget Target,
        int StartX, int StartY, Goal Goal);

    private abstract record Goal;

    private sealed record CrossLine(FieldNavigationTriggerLine Line) : Goal;

    /// <summary>
    /// A LINE whose Move slot is the exit: it runs while the leader is strictly inside the
    /// line's own radius (+0x72, 30 x scale / 512 from field init) and moving.
    /// </summary>
    private sealed record TouchLine(FieldNavigationTriggerLine Line, int Radius) : Goal;

    /// <summary>Stand within the body's own reach of the closed edge to a sloped face.</summary>
    private sealed record StairFoot(int X1, int Y1, int X2, int Y2, int Reach) : Goal;

    private static readonly FieldNavigationTriggerLine NibelheimDoorLine = new(-420, 294, 0, -431, 189, 0);
    private static readonly FieldNavigationTriggerLine UpstairsLine = new(421, 110, 149, 322, 103, 157);
    private static readonly FieldNavigationTriggerLine LibraryGatewayLine = new(116, 38, 0, -425, 420, 0);

    private static FieldNavigationTarget ToNibelheim() =>
        new(271, FieldNavigationCategory.Exits, "Exit to Nibelheim", -426, 242, 0,
            "script-exit:271:4:284", TriggerEntityId: 4, CompletesOnArrival: true,
            DestinationFieldIds: [284], TriggerLine: NibelheimDoorLine);

    private static FieldNavigationTarget Upstairs() =>
        new(271, FieldNavigationCategory.Exits, "Exit", 372, 107, 153,
            "script-exit:271:5:272", TriggerEntityId: 5, CompletesOnArrival: true,
            DestinationFieldIds: [272], TriggerLine: UpstairsLine);

    private static FieldNavigationTarget ToLibrary() =>
        new(308, FieldNavigationCategory.Exits, "Return through the basement library", -155, 229, 0,
            "gateway:308:1:305", CompletesOnArrival: true, DestinationFieldIds: [305],
            TriggerLine: LibraryGatewayLine);

    // Triangle 42's edge to the staircase face 45 in nvmin1_1.
    private static readonly StairFoot UpstairsFoot = new(315, 263, 428, 262, 30 + 8);

    private static readonly Case[] Cases =
    [
        new("271 upstairs from the logged start", 271, 30, [], Upstairs(), -330, 239, UpstairsFoot),
        new("271 upstairs from the logged stall", 271, 30, [], Upstairs(), -80, 309, UpstairsFoot),
        new("271 Nibelheim from the logged start", 271, 30, [], ToNibelheim(), 386, 293, new TouchLine(NibelheimDoorLine, 30)),
        new("271 Nibelheim from the logged stall", 271, 30, [], ToNibelheim(), 54, 298, new TouchLine(NibelheimDoorLine, 30)),
        new("308 library from the logged start", 308, 120, [18, 55, 56], ToLibrary(), 258, 2802, new CrossLine(LibraryGatewayLine)),
        new("308 library from the logged stall", 308, 120, [18, 55, 56], ToLibrary(), 437, 1720, new CrossLine(LibraryGatewayLine)),
    ];

    private static readonly ushort[] Speeds = [1024, 2048];
    private static readonly int[] FramesPerSample = [2, 4];

    internal static void Run(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner>? wrap = null,
        string runtime = "legacy")
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")))
        {
            Console.WriteLine("body clearance routes: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        var failures = new List<string>();
        failures.AddRange(PortalsThePlayerWalkedStayOpen(createWalkmeshReader));
        failures.AddRange(AnOpenLongLegIsNeverCalledBlocked());
        failures.AddRange(ADeadlineIsKeptInsideTheSearch(createWalkmeshReader));
        failures.AddRange(AnUnjudgeableMeshIsPreparedOnce());
        failures.AddRange(AFallbackRouteIsSteeredOnItsOwnGeometry(createWalkmeshReader));
        failures.AddRange(StairsAndStackedFloorsKeepTheirPortals(createWalkmeshReader));
        foreach (var testCase in Cases)
        {
            foreach (var speed in Speeds)
            {
                foreach (var frames in FramesPerSample)
                {
                    if (!Replay(createWalkmeshReader, wrap, testCase, speed, frames, out var trail))
                    {
                        failures.Add($"{runtime} {testCase.Name}, speed {speed}, {frames} frames/sample: {trail}");
                    }
                }
            }
        }

        failures.AddRange(RouteLatency(createWalkmeshReader, wrap, runtime));
        if (failures.Count > 0)
        {
            throw new InvalidOperationException("body clearance routes:" + Environment.NewLine +
                string.Join(Environment.NewLine, failures));
        }

        Console.WriteLine($"PASS {runtime} body clearance routes: 271 to Nibelheim and 308 to the library reach native activation, " +
            "and 271 upstairs reaches the foot of its staircase (the oracle does not model the slope beyond), " +
            "at both native speeds; logged crossings stay open, deadlines hold, fallbacks keep their geometry.");
    }

    /// <summary>
    /// A closed portal is a claim that the game will not let the body through. Every portal
    /// the player's own logged movement crossed on 2026-09-28 is proof the other way, so none
    /// of them may be closed. The pairs are consecutive logged triangles that are adjacent and
    /// each contain their logged position (the log's triangle lags near edges: 14 of 174
    /// samples in 271, among them (-70,311) reported as 22, are discarded).
    /// </summary>
    private static IEnumerable<string> PortalsThePlayerWalkedStayOpen(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var evidence = new (int Field, int Radius, int[] Locks, (int, int)[] Crossed)[]
        {
            (271, 30, [], [(6, 13), (13, 16), (14, 15), (15, 16), (16, 17), (17, 18), (18, 19), (19, 20), (20, 26),
                (24, 34), (26, 27), (27, 32), (28, 29), (29, 30), (29, 33), (30, 34), (34, 35), (35, 36), (36, 40),
                (40, 41), (41, 42), (43, 44), (44, 58), (48, 49), (48, 62), (51, 52), (52, 53), (53, 54), (53, 56),
                (57, 58), (57, 62)]),
            (308, 120, [18, 55, 56], [(8, 24), (8, 39), (10, 75), (11, 30), (11, 32), (13, 30), (13, 36), (16, 79),
                (17, 71), (17, 74), (24, 38), (36, 71), (37, 65), (37, 74), (38, 79), (71, 73), (73, 74), (75, 79)]),
            (303, 30, [], [(10, 11), (10, 16), (11, 12), (12, 13), (16, 17), (17, 18), (18, 19), (19, 21), (21, 23),
                (23, 28)]),
            (302, 30, [], [(0, 1), (0, 4), (4, 5), (5, 6), (6, 7), (7, 12), (8, 10), (8, 14), (10, 11), (12, 13),
                (13, 14), (13, 15), (15, 16), (15, 18), (16, 23), (18, 19), (19, 20), (20, 22), (22, 30), (23, 24),
                (24, 29), (29, 30), (30, 31)]),
        };
        foreach (var (field, radius, locks, crossed) in evidence)
        {
            var mesh = createWalkmeshReader(field).Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule,
                field, 0, 0, 0, 0, 0, 0)).Walkmesh ?? throw new InvalidOperationException($"field {field} walkmesh");
            var clearance = FieldNavigationBodyClearance.Analyze(mesh, radius, locks.ToHashSet());
            if (clearance is null)
            {
                yield return $"field {field}: no body clearance measured";
                continue;
            }

            Console.WriteLine($"field {field}: {clearance.Diagnostic}");
            var closed = clearance.ClosedPortals.ToHashSet();
            foreach (var pair in crossed.Where(closed.Contains))
            {
                yield return $"field {field}: portal {pair.Item1}|{pair.Item2} is closed, but the logged player walked through it";
            }

            if (field == 271 && !(closed.Contains((22, 23)) || closed.Contains((23, 24))))
            {
                yield return "field 271: the 13-unit pinch under (-9,288) is still open to the body";
            }
        }
    }

    /// <summary>
    /// Root's probe: one large clockwise flat triangle, a 30-unit body well inside it, and
    /// straight legs of every length. None of them is obstructed, so none may be reported
    /// blocked; the replay only proves the legs it walks in full, and says so for the rest.
    /// </summary>
    private static IEnumerable<string> AnOpenLongLegIsNeverCalledBlocked()
    {
        var mesh = BuildMesh([((-2000, -2000, 0), (-2000, 2000, 0), (2000, -2000, 0))]);
        var clearance = FieldNavigationBodyClearance.Create(mesh, 30, new HashSet<int>())
                        ?? throw new InvalidOperationException("the open triangle must be supported");
        var from = new FieldNavigationRouteWaypoint(-500, -1000, 0);
        foreach (var length in new[] { 100, 300, 400, 600, 1000 })
        {
            var answer = clearance.IsLegWalkable(from, null, 0, new(-500, -1000 + length, 0));
            if (answer == false)
            {
                yield return $"an open {length}-unit leg was reported blocked";
            }

            if (length <= 300 && answer != true)
            {
                yield return $"an open {length}-unit leg, short enough to walk in full, was not proved walkable ({answer})";
            }
        }
    }

    /// <summary>
    /// The deadline is kept inside the native search, not only between portals, and a search
    /// the deadline stops closes nothing. Every portal of both reported fields, each measured
    /// by a fresh analysis with a 1 ms budget, must return promptly, and anything it did close
    /// must also be closed by the unbounded measurement.
    /// </summary>
    private static IEnumerable<string> ADeadlineIsKeptInsideTheSearch(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var (field, radius, locks) in new[] { (271, 30, Array.Empty<int>()), (308, 120, new[] { 18, 55, 56 }) })
        {
            var mesh = createWalkmeshReader(field).Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule,
                field, 0, 0, 0, 0, 0, 0)).Walkmesh!;
            var complete = FieldNavigationBodyClearance.Analyze(mesh, radius, locks.ToHashSet())!;
            var fullyClosed = complete.ClosedPortals.ToHashSet();
            var worst = 0d;
            for (var index = 0; index < mesh.Triangles.Count; index++)
            {
                for (var edge = 0; edge < 3; edge++)
                {
                    var neighbour = mesh.Triangles[index].GetAdjacentTriangle(edge);
                    if (neighbour <= index)
                    {
                        continue;
                    }

                    var bounded = FieldNavigationBodyClearance.Create(mesh, radius, locks.ToHashSet())!;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    bounded.MeasurePortals([(index, neighbour)], TimeSpan.FromMilliseconds(1));
                    worst = Math.Max(worst, clock.Elapsed.TotalMilliseconds);
                    foreach (var portal in bounded.ClosedPortals.Where(portal => !fullyClosed.Contains(portal)))
                    {
                        yield return $"field {field}: {portal.A}|{portal.B} was closed under a 1 ms budget but not by the full search";
                    }
                }
            }

            Console.WriteLine($"field {field}: slowest single portal under a 1 ms budget took {worst:0.00} ms");
            if (worst > 10d)
            {
                yield return $"field {field}: a 1 ms portal measurement took {worst:0.0} ms";
            }
        }
    }

    /// <summary>A mesh the oracle cannot judge is remembered as such, not prepared on every poll.</summary>
    private static IEnumerable<string> AnUnjudgeableMeshIsPreparedOnce()
    {
        var mesh = BuildMesh([((-400, -400, 0), (-400, 400, 100), (400, -400, 0)), ((400, -400, 0), (-400, 400, 100), (400, 400, 100))]);
        var planner = new FieldWalkmeshRoutePlanner(ReaderFor(mesh), playerCollisionRadiusProvider: _ => 30);
        var target = new FieldNavigationTarget(900, FieldNavigationCategory.Objects, "far corner", 300, 300, 75,
            StableId: "synthetic:far");
        var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 900, 0, -300, -300, 0, 0, 0);
        for (var poll = 0; poll < 5; poll++)
        {
            planner.TryBuildRoute(start, target, out _);
        }

        if (planner.LastBodyClearance is not null || planner.BodyClearancePreparations != 1)
        {
            yield return $"a sloped mesh was prepared {planner.BodyClearancePreparations} times (clearance {planner.LastBodyClearance is not null})";
        }
    }

    /// <summary>
    /// When closing what the body cannot cross leaves no way at all, the centre-line route is
    /// kept - and it is then steered on its own geometry. nvmin1_1's triangle 23 is the pinch
    /// itself: a destination inside it has no body-passable way in, so the route falls back,
    /// and the immediate move into 23 must still be clear to it and the body preference silent.
    /// </summary>
    private static IEnumerable<string> AFallbackRouteIsSteeredOnItsOwnGeometry(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(271), LockedTriangles(271, []),
            playerCollisionRadiusProvider: _ => 30);
        var target = new FieldNavigationTarget(271, FieldNavigationCategory.Objects, "under the table corner", -4, 279, 0,
            StableId: "synthetic:271:23");
        var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 271, 0, -46, 293, 0, 22, 0);
        for (var poll = 0; poll < 4 && !planner.LastDiagnostic.Contains("centre-line route kept", StringComparison.Ordinal); poll++)
        {
            if (!planner.TryBuildRoute(start, target, out _))
            {
                yield return $"the fallback route was lost: {planner.LastDiagnostic}";
                yield break;
            }
        }

        if (!planner.LastDiagnostic.Contains("centre-line route kept", StringComparison.Ordinal))
        {
            yield return $"the pinch destination did not fall back to the centre-line route: {planner.LastDiagnostic}";
            yield break;
        }

        var intoThePinch = new FieldNavigationRouteWaypoint(-20, 280, 0);
        if (!planner.IsAutomaticMovementClear(start, target, intoThePinch))
        {
            yield return "the fallback route's own next move was refused on the pruned geometry it was not planned on";
        }

        if (planner.IsBodyMovementClear(start, target, intoThePinch) is not null)
        {
            yield return "the body preference spoke for a route planned without it";
        }
    }

    /// <summary>
    /// Only portals the oracle models exactly are reshaped for the body. The staircase portal of
    /// nvmin1_1 (42 to the sloped 45) keeps the planner's own geometry; and on a synthetic
    /// two-storey room a wall of the upper floor directly above a lower portal's inner end does
    /// not move that end, while the lower floor's own wall corner does move by the radius.
    /// </summary>
    private static IEnumerable<string> StairsAndStackedFloorsKeepTheirPortals(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(271);
        var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 271, 0, 386, 293, 0, 42, 0);
        var bodyPlanner = new FieldWalkmeshRoutePlanner(reader, LockedTriangles(271, []), playerCollisionRadiusProvider: _ => 30);
        var plainPlanner = new FieldWalkmeshRoutePlanner(reader, LockedTriangles(271, []));
        if (!bodyPlanner.TryBuildRoute(start, Upstairs(), out var bodyPlan) ||
            !plainPlanner.TryBuildRoute(start, Upstairs(), out var plainPlan))
        {
            yield return "no route up the nvmin1_1 stairs";
            yield break;
        }

        var sloped = new HashSet<int> { 0, 1, 2, 3, 45, 46 };
        foreach (var portal in bodyPlan.Portals.Where(p => sloped.Contains(p.FromTriangle) || sloped.Contains(p.ToTriangle)))
        {
            var plain = plainPlan.Portals.FirstOrDefault(p => p.FromTriangle == portal.FromTriangle && p.ToTriangle == portal.ToTriangle);
            if (plain != default && (plain.Left != portal.Left || plain.Right != portal.Right))
            {
                yield return $"the staircase portal {portal.FromTriangle}>{portal.ToTriangle} was reshaped: " +
                             $"{portal.Left}|{portal.Right} against {plain.Left}|{plain.Right}";
            }
        }

        // Lower floor: a square fanned round its centre O, so O is an inner vertex of every portal.
        // Upper floor: a lone triangle 500 above, with a corner exactly over O.
        var mesh = BuildMesh([
            ((0, 0, 0), (-400, -400, 0), (400, -400, 0)),
            ((0, 0, 0), (400, -400, 0), (400, 400, 0)),
            ((0, 0, 0), (400, 400, 0), (-400, 400, 0)),
            ((0, 0, 0), (-400, 400, 0), (-400, -400, 0)),
            ((0, 0, 500), (200, 0, 500), (0, 200, 500))
        ]);
        var stacked = new FieldWalkmeshRoutePlanner(ReaderFor(mesh), playerCollisionRadiusProvider: _ => 100);
        var plainStacked = new FieldWalkmeshRoutePlanner(ReaderFor(mesh));
        var from = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 900, 0, 0, -300, 0,
            (ushort)FieldWalkmeshPathfinder.ResolveTriangle(mesh, 0, -300, 0, -1), 0);
        var to = new FieldNavigationTarget(900, FieldNavigationCategory.Objects, "the far side", 0, 300, 0, StableId: "synthetic:stacked");
        if (!stacked.TryBuildRoute(from, to, out var stackedPlan) || !plainStacked.TryBuildRoute(from, to, out var plainStackedPlan))
        {
            yield return "no route across the stacked room";
            yield break;
        }

        foreach (var portal in stackedPlan.Portals)
        {
            var plain = plainStackedPlan.Portals.First(p => p.FromTriangle == portal.FromTriangle && p.ToTriangle == portal.ToTriangle);
            var (innerBody, outerBody) = Nearer(portal, 0, 0);
            var (innerPlain, _) = Nearer(plain, 0, 0);
            if (innerBody != innerPlain)
            {
                yield return $"the upper floor's corner moved the lower portal's inner end: {innerBody} against {innerPlain}";
            }

            var corner = Math.Abs(outerBody.X) > Math.Abs(outerBody.Y)
                ? (X: Math.Sign(outerBody.X) * 400, Y: Math.Sign(outerBody.Y) * 400)
                : (X: Math.Sign(outerBody.X) * 400, Y: Math.Sign(outerBody.Y) * 400);
            var fromCorner = Math.Sqrt(Math.Pow(outerBody.X - corner.X, 2) + Math.Pow(outerBody.Y - corner.Y, 2));
            if (fromCorner < 99d)
            {
                yield return $"the lower floor's own wall corner is still {fromCorner:0} from the portal end, not the body's 100";
            }
        }
    }

    private static (FieldNavigationRouteWaypoint Inner, FieldNavigationRouteWaypoint Outer) Nearer(
        FieldNavigationRoutePortal portal, int x, int y)
    {
        double D(FieldNavigationRouteWaypoint p) => Math.Pow(p.X - x, 2) + Math.Pow(p.Y - y, 2);
        return D(portal.Left) <= D(portal.Right) ? (portal.Left, portal.Right) : (portal.Right, portal.Left);
    }

    /// <summary>A walkmesh from plain triangles: wound as the native mesh is, adjacency by shared vertices.</summary>
    internal static FieldWalkmesh BuildMesh(IReadOnlyList<((int X, int Y, int Z) A, (int X, int Y, int Z) B, (int X, int Y, int Z) C)> faces)
    {
        var wound = faces.Select(face =>
        {
            var cross = (long)(face.B.X - face.A.X) * (face.C.Y - face.A.Y) - (long)(face.B.Y - face.A.Y) * (face.C.X - face.A.X);
            return cross < 0 ? face : (A: face.A, B: face.C, C: face.B);
        }).ToArray();
        FieldWalkmeshVertex V((int X, int Y, int Z) p) => new((short)p.X, (short)p.Y, (short)p.Z);
        short Neighbour(int self, (int, int, int) p, (int, int, int) q)
        {
            for (var other = 0; other < wound.Length; other++)
            {
                var o = wound[other];
                var corners = new[] { o.A, o.B, o.C };
                if (other != self && corners.Contains(p) && corners.Contains(q))
                {
                    return (short)other;
                }
            }

            return -1;
        }

        return new FieldWalkmesh(wound.Select((face, index) => new FieldWalkmeshTriangle(index, V(face.A), V(face.B), V(face.C),
            Neighbour(index, face.A, face.B), Neighbour(index, face.B, face.C), Neighbour(index, face.C, face.A))).ToArray());
    }

    internal static FieldWalkmeshReader ReaderFor(FieldWalkmesh mesh)
    {
        const int fieldDataBase = 0x02000000;
        const int walkmeshSectionOffset = 0x100;
        var memory = new Dictionary<int, int> { [FieldWalkmeshReader.AddressFieldDataPtr] = fieldDataBase };
        var sectionTableEntry = fieldDataBase + FieldWalkmeshReader.SectionOffsetsHeaderOffset +
            FieldWalkmeshReader.WalkmeshSectionIndex * sizeof(int);
        var triangles = mesh.Triangles;
        memory[sectionTableEntry] = walkmeshSectionOffset;
        memory[sectionTableEntry + sizeof(int)] = walkmeshSectionOffset + sizeof(int) + sizeof(int) +
            triangles.Count * (FieldWalkmeshReader.TriangleSize + FieldWalkmeshReader.AccessSize);
        var payload = fieldDataBase + walkmeshSectionOffset + sizeof(int);
        memory[payload] = triangles.Count;
        var trianglesBase = payload + sizeof(int);
        void Vertex(int address, FieldWalkmeshVertex vertex)
        {
            memory[address] = vertex.X;
            memory[address + sizeof(short)] = vertex.Y;
            memory[address + sizeof(short) * 2] = vertex.Z;
            memory[address + sizeof(short) * 3] = 0;
        }

        for (var index = 0; index < triangles.Count; index++)
        {
            var triangleBase = trianglesBase + index * FieldWalkmeshReader.TriangleSize;
            Vertex(triangleBase, triangles[index].Vertex0);
            Vertex(triangleBase + FieldWalkmeshReader.VertexSize, triangles[index].Vertex1);
            Vertex(triangleBase + FieldWalkmeshReader.VertexSize * 2, triangles[index].Vertex2);
        }

        var accessBase = trianglesBase + triangles.Count * FieldWalkmeshReader.TriangleSize;
        for (var index = 0; index < triangles.Count; index++)
        {
            var entry = accessBase + index * FieldWalkmeshReader.AccessSize;
            memory[entry] = triangles[index].Adjacent0;
            memory[entry + sizeof(short)] = triangles[index].Adjacent1;
            memory[entry + sizeof(short) * 2] = triangles[index].Adjacent2;
        }

        return new FieldWalkmeshReader(
            address => memory.TryGetValue(address, out var value) ? value : 0,
            address => (short)(memory.TryGetValue(address, out var value) ? value : 0));
    }

    private static bool Replay(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner>? wrap,
        Case testCase,
        ushort speed,
        int framesPerSample,
        out string trail)
    {
        var reader = createWalkmeshReader(testCase.FieldId);
        var full = reader.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, testCase.FieldId, 0,
                       testCase.StartX, testCase.StartY, 0, 0, 0)).Walkmesh
                   ?? throw new InvalidOperationException($"field {testCase.FieldId} walkmesh is unavailable");
        var (flat, originalIds, flatIds) = FlatPortion(full);
        var locked = testCase.Locks.Where(flatIds.ContainsKey).Select(id => flatIds[id]).ToHashSet();
        IFieldNavigationRoutePlanner planner = new FieldWalkmeshRoutePlanner(reader,
            LockedTriangles(testCase.FieldId, testCase.Locks), playerCollisionRadiusProvider: _ => testCase.Radius);
        if (wrap is not null)
        {
            planner = wrap(planner);
        }

        var controller = new FieldNavigationController(new FieldNavigationTargetSource([testCase.Target]), planner);
        var transform = new FieldNavigationControlTransform(0);
        var startTriangle = ResolveFlat(flat, testCase.StartX, testCase.StartY);
        if (startTriangle < 0)
        {
            trail = $"start {testCase.StartX},{testCase.StartY} is not on the flat floor";
            return false;
        }

        var state = new FieldNavigationNativeMovementState(testCase.StartX * 4096, testCase.StartY * 4096, 0, startTriangle, 0);
        FieldPositionSnapshot Position() => new(FieldPositionReader.FieldModule, testCase.FieldId, 0,
            state.FixedX >> 12, state.FixedY >> 12, 0, (ushort)originalIds[state.TriangleId], state.Heading)
        {
            NativeFixedPosition = new(state.FixedX, state.FixedY, 0)
        };

        while (controller.CurrentCategory != testCase.Target.Category)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, Position(), transform);
        }

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, Position(), transform);
        var input = FieldNavigationInput.None;
        var recent = new Queue<string>();
        for (var tick = 0; tick < 600; tick++)
        {
            var position = Position();
            controller.UpdateLiveTracking(position, new(0, input), transform, false, 80,
                observedAt: DateTime.UnixEpoch.AddMilliseconds(tick * 80));
            if (!controller.TryResolveAutomaticInput(position, transform, 80, out input))
            {
                trail = $"auto walk gave up at {position.X},{position.Y} tri {position.TriangleId}: " +
                        $"{controller.LastAutomaticInputHold}; last {string.Join(" / ", recent)}";
                return false;
            }

            var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
            var heading = unchecked((byte)(int)Math.Round(Math.Atan2(dx, -dy) * 128 / Math.PI));
            for (var frame = 0; frame < framesPerSample; frame++)
            {
                var fromX = state.FixedX / 4096d;
                var fromY = state.FixedY / 4096d;
                state = FieldNavigationNativeProbeMovement.Step(flat, state, heading, speed, testCase.Radius,
                    Array.Empty<FieldNavigationDynamicObstacle>(), locked.Contains).State;
                if (Reached(testCase.Goal, fromX, fromY, state.FixedX / 4096d, state.FixedY / 4096d))
                {
                    trail = $"reached at tick {tick}";
                    return true;
                }
            }

            recent.Enqueue($"{Position().X},{Position().Y} {input}");
            if (recent.Count > 6)
            {
                recent.Dequeue();
            }
        }

        trail = $"no activation in 600 samples; last {string.Join(" / ", recent)}";
        return false;
    }

    /// <summary>
    /// The exit and object lists ask for routes on every poll, so a body-aware route must
    /// not cost a movement search each time. The first build of a field may pay for its
    /// analysis once; later builds of the same state are held to a small budget.
    /// </summary>
    private static IEnumerable<string> RouteLatency(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner>? wrap,
        string runtime)
    {
        foreach (var testCase in Cases.DistinctBy(item => item.FieldId))
        {
            var reader = createWalkmeshReader(testCase.FieldId);
            IFieldNavigationRoutePlanner planner = new FieldWalkmeshRoutePlanner(reader,
                LockedTriangles(testCase.FieldId, testCase.Locks), playerCollisionRadiusProvider: _ => testCase.Radius);
            if (wrap is not null)
            {
                planner = wrap(planner);
            }

            var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, testCase.FieldId, 0,
                testCase.StartX, testCase.StartY, 0, 0, 0);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            planner.TryBuildRoute(start, testCase.Target, out _);
            var cold = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            const int repeats = 50;
            for (var index = 0; index < repeats; index++)
            {
                planner.TryBuildRoute(start, testCase.Target, out _);
            }

            var warm = clock.Elapsed.TotalMilliseconds / repeats;
            Console.WriteLine($"{runtime} field {testCase.FieldId} route build: first {cold:0.0} ms, then {warm:0.00} ms each ({planner.LastDiagnostic})");
            if (cold > 400d)
            {
                yield return $"{runtime} field {testCase.FieldId}: the first body-aware route took {cold:0} ms";
            }

            if (warm > 10d)
            {
                yield return $"{runtime} field {testCase.FieldId}: repeated route builds took {warm:0.0} ms each";
            }
        }
    }

    private static bool Reached(Goal goal, double ax, double ay, double bx, double by) => goal switch
    {
        CrossLine cross => SegmentsCross(cross.Line, ax, ay, bx, by),
        TouchLine touch => (ax != bx || ay != by) &&
            DistanceToSegment(bx, by, touch.Line.StartX, touch.Line.StartY, touch.Line.EndX, touch.Line.EndY) < touch.Radius,
        StairFoot foot => DistanceToSegment(bx, by, foot.X1, foot.Y1, foot.X2, foot.Y2) <= foot.Reach,
        _ => false
    };

    internal static FieldBoundaryStateReader LockedTriangles(int field, IReadOnlyList<int> triangles)
    {
        const int fieldState = 0x00200000;
        var bits = new byte[FieldBoundaryStateReader.BoundaryByteCount];
        foreach (var triangle in triangles)
        {
            bits[triangle >> 3] |= (byte)(1 << (triangle & 7));
        }

        return new FieldBoundaryStateReader(
            readInt32: address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? fieldState : 0,
            readByte: address =>
            {
                if (address == FieldPositionReader.AddressCurrentModule)
                {
                    return (byte)FieldPositionReader.FieldModule;
                }

                if (address == FieldPositionReader.AddressFieldId)
                {
                    return (byte)(field & 0xFF);
                }

                if (address == FieldPositionReader.AddressFieldId + 1)
                {
                    return (byte)((field >> 8) & 0xFF);
                }

                var offset = address - (fieldState + FieldBoundaryStateReader.BoundaryBitsOffset);
                return offset >= 0 && offset < bits.Length ? bits[offset] : (byte)0;
            },
            isReadableMemory: (_, _) => true);
    }

    /// <summary>The exact z = 0 faces, their own edges kept and edges to anything else closed.</summary>
    internal static (FieldWalkmesh Flat, int[] OriginalIds, Dictionary<int, int> FlatIds) FlatPortion(FieldWalkmesh full)
    {
        var flat = full.Triangles.Where(t => t.Vertex0.Z == 0 && t.Vertex1.Z == 0 && t.Vertex2.Z == 0).ToArray();
        var ids = flat.Select((t, i) => (t.Index, i)).ToDictionary(pair => pair.Index, pair => pair.i);
        short Adjacent(int old) => checked((short)ids.GetValueOrDefault(old, -1));
        var mesh = new FieldWalkmesh(flat.Select((t, i) => new FieldWalkmeshTriangle(i, t.Vertex0, t.Vertex1, t.Vertex2,
            Adjacent(t.Adjacent0), Adjacent(t.Adjacent1), Adjacent(t.Adjacent2))).ToArray());
        if (!FieldNavigationNativeProbeMovement.IsSupportedWalkmesh(mesh))
        {
            throw new InvalidOperationException("the flat portion must be supported by the native oracle");
        }

        return (mesh, flat.Select(t => t.Index).ToArray(), ids);
    }

    private static int ResolveFlat(FieldWalkmesh mesh, int x, int y)
    {
        foreach (var triangle in mesh.Triangles)
        {
            var (a, b, c) = (triangle.Vertex0, triangle.Vertex1, triangle.Vertex2);
            double Cross(FieldWalkmeshVertex p, FieldWalkmeshVertex q) => (q.X - p.X) * (double)(y - p.Y) - (q.Y - p.Y) * (double)(x - p.X);
            var (d1, d2, d3) = (Cross(a, b), Cross(b, c), Cross(c, a));
            if ((d1 <= 0 && d2 <= 0 && d3 <= 0) || (d1 >= 0 && d2 >= 0 && d3 >= 0))
            {
                return triangle.Index;
            }
        }

        return -1;
    }

    private static bool SegmentsCross(FieldNavigationTriggerLine line, double ax, double ay, double bx, double by)
    {
        double Side(double px, double py, double qx, double qy, double rx, double ry) =>
            (qx - px) * (ry - py) - (qy - py) * (rx - px);
        var d1 = Side(line.StartX, line.StartY, line.EndX, line.EndY, ax, ay);
        var d2 = Side(line.StartX, line.StartY, line.EndX, line.EndY, bx, by);
        var d3 = Side(ax, ay, bx, by, line.StartX, line.StartY);
        var d4 = Side(ax, ay, bx, by, line.EndX, line.EndY);
        return d1 * d2 <= 0 && d3 * d4 <= 0 && (d1 != 0 || d2 != 0);
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
}
