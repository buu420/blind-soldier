using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Under Junon, field 428, 2026-09-26 (log 426013-426345): auto walk to the gateway to field 7
/// routed to the native line's own endpoint, 633,888. The installed line runs 1097,191 to
/// 633,888, and that endpoint lies on the closed edge 569,902-721,876 of triangle 127, so no
/// body can stand there: Cloud slid along the wall around 619..635,862..866 and never crossed
/// the line. The native init sets the leader's collision radius to 30 * scale / 512
/// (0060BCFA; field 428's scale is 512, so 30), and the approach has to be a point on the real
/// line that a body of that radius can reach.
///
/// <para>The replay keeps the installed mesh for planning. The native movement oracle
/// (FieldNavigationNativeProbeMovement, verified separately) supports flat meshes only, so it
/// runs on the exact z = 0 portion of the mesh, geometry and adjacency preserved and edges to
/// sloped faces closed - nothing is flattened. Arrival is the real crossing of the gateway
/// line by the party's own movement, not a proximity test.</para>
///
/// <para>Junon street, field 370, the same session: auto walk to the Barracks gateway
/// (line -2769,117 to -2694,80) from the logged start -1830,10 looped at the line's end in
/// the same replay. Its scale is 700, so the native radius is 41. The field is flat, so the
/// oracle runs on the whole mesh. (The log's mid-route stalls at -2616,-38 are not what this
/// replay reproduces; they are not claimed here.)</para>
/// </summary>
internal static class FieldGatewayBodyClearanceTests
{
    private readonly record struct Case(
        int FieldId, string Label, string StableId, int DestinationField, int TargetX, int TargetY,
        FieldNavigationTriggerLine Gateway, int StartX, int StartY, int StartZ, int StartTriangle,
        int Radius, ushort[] Speeds);

    private static readonly Case[] Cases =
    [
        new(428, "Exit", "gateway:428:4:7", 7, 865, 540, new(1097, 191, 0, 633, 888, 0), 0, 924, 0, 118, 30, [1024, 2048]),
        new(370, "Exit to Barracks", "gateway:370:4:380", 380, -2732, 98, new(-2769, 117, 0, -2694, 80, 0), -1830, 10, 0, 59, 41, [1400, 2048])
    ];

    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        AWallOnAnotherFloorDoesNotMoveTheApproach();
        TheLeadersOwnRadiusIsReadInAnEmptyRoom();
        var failures = new List<string>();
        foreach (var testCase in Cases)
        {
            failures.AddRange(RunCase(createWalkmeshReader, testCase));
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException("field gateways: " + string.Join(Environment.NewLine, failures));
        }

        Console.WriteLine("PASS field 428 and 370 gateways: the approach is body-reachable and auto walk crosses the real line.");
    }

    /// <summary>
    /// Clearance is measured on the floor the approach stands on. A lower room at z 0 holds
    /// the gateway line; directly above it, at z 500, an unconnected upper floor has a closed
    /// edge 10 units from the line's nearest point in plan. That upper wall is not beside the
    /// gateway: the approach stays at the nearest point of the line, 150,100.
    /// </summary>
    private static void AWallOnAnotherFloorDoesNotMoveTheApproach()
    {
        var triangles = new[]
        {
            new FieldWalkmeshTriangle(0, new(0, 0, 0), new(400, 0, 0), new(0, 400, 0), -1, 1, -1),
            new FieldWalkmeshTriangle(1, new(400, 0, 0), new(400, 400, 0), new(0, 400, 0), -1, -1, 0),
            new FieldWalkmeshTriangle(2, new(100, 110, 500), new(200, 110, 500), new(150, 300, 500), -1, -1, -1)
        };
        var reader = SyntheticReader(triangles);
        var planner = new FieldWalkmeshRoutePlanner(reader, playerCollisionRadiusProvider: _ => 30d);
        var target = new FieldNavigationTarget(900, FieldNavigationCategory.Exits, "Exit", 150, 100, 0,
            StableId: "gateway:900:0:1", CompletesOnArrival: true, DestinationFieldIds: [1],
            TriggerLine: new FieldNavigationTriggerLine(50, 100, 0, 250, 100, 0));
        var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 900, 0, 150, 20, 0, 0, 0);
        Equal(true, planner.TryBuildRoute(start, target, out var plan), $"a route to the lower gateway: {planner.LastDiagnostic}");
        Equal((150, 100), (plan.FinalApproach.X, plan.FinalApproach.Y),
            "the approach stays at the line's nearest point; the upper floor's wall is not on this floor");
    }

    /// <summary>
    /// The leader's own +0x72 is read directly from its event entry, so a room with nobody else
    /// in it - where the obstacle reader has no other model to report - still has a body width.
    /// </summary>
    private static void TheLeadersOwnRadiusIsReadInAnEmptyRoom()
    {
        const int eventTable = 0x02600000;
        var ints = new Dictionary<int, int> { [FieldNavigationObjectReader.AddressFieldEventDataPtr] = eventTable };
        var bytes = new Dictionary<int, byte> { [FieldPositionReader.AddressFieldNumModels] = 1 };
        var shorts = new Dictionary<int, short> { [eventTable + FieldNavigationNpcReader.CollisionRadiusOffset] = 30 };
        var reader = new FieldNavigationDynamicObstacleReader(
            address => ints.GetValueOrDefault(address),
            address => shorts.GetValueOrDefault(address),
            address => bytes.GetValueOrDefault(address));
        var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 428, 0, 0, 924, 0, 118, 0);
        Equal(0, reader.Read(position, null).Count, "nobody else is in the room");
        Equal(30d, reader.ReadPlayerCollisionRadius(position), "the leader's +0x72 is still read");
    }

    private static FieldWalkmeshReader SyntheticReader(FieldWalkmeshTriangle[] triangles)
    {
        const int fieldDataBase = 0x02000000;
        const int walkmeshSectionOffset = 0x100;
        var memory = new Dictionary<int, int> { [FieldWalkmeshReader.AddressFieldDataPtr] = fieldDataBase };
        var sectionTableEntry = fieldDataBase + FieldWalkmeshReader.SectionOffsetsHeaderOffset +
            FieldWalkmeshReader.WalkmeshSectionIndex * sizeof(int);
        memory[sectionTableEntry] = walkmeshSectionOffset;
        memory[sectionTableEntry + sizeof(int)] = walkmeshSectionOffset + sizeof(int) + sizeof(int) +
            triangles.Length * (FieldWalkmeshReader.TriangleSize + FieldWalkmeshReader.AccessSize);
        var payload = fieldDataBase + walkmeshSectionOffset + sizeof(int);
        memory[payload] = triangles.Length;
        var trianglesBase = payload + sizeof(int);
        void Vertex(int address, FieldWalkmeshVertex vertex)
        {
            memory[address] = vertex.X;
            memory[address + sizeof(short)] = vertex.Y;
            memory[address + sizeof(short) * 2] = vertex.Z;
            memory[address + sizeof(short) * 3] = 0;
        }

        for (var index = 0; index < triangles.Length; index++)
        {
            var triangleBase = trianglesBase + index * FieldWalkmeshReader.TriangleSize;
            Vertex(triangleBase, triangles[index].Vertex0);
            Vertex(triangleBase + FieldWalkmeshReader.VertexSize, triangles[index].Vertex1);
            Vertex(triangleBase + FieldWalkmeshReader.VertexSize * 2, triangles[index].Vertex2);
        }

        var accessBase = trianglesBase + triangles.Length * FieldWalkmeshReader.TriangleSize;
        for (var index = 0; index < triangles.Length; index++)
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

    private static IEnumerable<string> RunCase(Func<int, FieldWalkmeshReader> createWalkmeshReader, Case testCase)
    {
        Gateway = testCase.Gateway;
        var reader = createWalkmeshReader(testCase.FieldId);
        var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, testCase.FieldId, 0,
            testCase.StartX, testCase.StartY, testCase.StartZ, (ushort)testCase.StartTriangle, 0);
        var full = reader.Read(start).Walkmesh
            ?? throw new InvalidOperationException($"field {testCase.FieldId} walkmesh is unavailable");
        var (flat, originalIds) = FlatPortion(full);
        var target = new FieldNavigationTarget(testCase.FieldId, FieldNavigationCategory.Exits, testCase.Label,
            testCase.TargetX, testCase.TargetY, 0, StableId: testCase.StableId, CompletesOnArrival: true,
            DestinationFieldIds: [testCase.DestinationField], TriggerLine: testCase.Gateway);

        var planner = new FieldWalkmeshRoutePlanner(reader, playerCollisionRadiusProvider: _ => testCase.Radius);
        Equal(true, planner.TryBuildRoute(start, target, out var plan), $"field {testCase.FieldId}: a route to the gateway: {planner.LastDiagnostic}");
        var end = plan.FinalApproach;
        var failures = new List<string>();
        if (DistanceToLine(end.X, end.Y) > 1d)
        {
            failures.Add($"field {testCase.FieldId}: the route does not end on the real line: {end.X},{end.Y}");
        }

        // The room the approach must leave: the whole body where the line has that much
        // anywhere, otherwise as much as the real line offers (370's Barracks doorway is
        // narrower than the leader's radius; its middle has 18).
        var roomOnLine = Enumerable.Range(0, 201)
            .Select(step => (X: testCase.Gateway.StartX + (testCase.Gateway.EndX - testCase.Gateway.StartX) * step / 200d,
                Y: testCase.Gateway.StartY + (testCase.Gateway.EndY - testCase.Gateway.StartY) * step / 200d))
            .Where(point => full.Triangles.Any(triangle => InPlan(triangle, point.X, point.Y)))
            .Select(point => DistanceToClosedEdges(full, point.X, point.Y))
            .DefaultIfEmpty(0d)
            .Max();
        // The planner samples the line every 8 units, and room changes at most a unit per
        // unit along it, so its choice can be up to half a sample short of the best.
        var required = Math.Min(testCase.Radius, roomOnLine) - 4d;
        var room = DistanceToClosedEdges(full, end.X, end.Y);
        if (room < required)
        {
            failures.Add($"field {testCase.FieldId}: the approach {end.X},{end.Y} leaves {room:0.0} units of room where " +
                         $"the line offers {roomOnLine:0.0} and the body needs {testCase.Radius}");
        }

        foreach (var speed in testCase.Speeds)
        {
            if (!Replay(reader, flat, originalIds, target, testCase, speed, out var trail))
            {
                failures.Add($"field {testCase.FieldId} speed {speed}: auto walk did not cross the gateway line; last {trail}");
            }
        }

        return failures;
    }

    private static bool Replay(
        FieldWalkmeshReader reader,
        FieldWalkmesh flat,
        int[] originalIds,
        FieldNavigationTarget target,
        Case testCase,
        ushort speed,
        out string trail)
    {
        var planner = new FieldWalkmeshRoutePlanner(reader, playerCollisionRadiusProvider: _ => testCase.Radius);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), planner);
        var transform = new FieldNavigationControlTransform(0);
        var state = new FieldNavigationNativeMovementState(testCase.StartX * 4096, testCase.StartY * 4096, testCase.StartZ * 4096,
            Array.IndexOf(originalIds, testCase.StartTriangle), 0);
        FieldPositionSnapshot Position() => new(FieldPositionReader.FieldModule, testCase.FieldId, 0, state.FixedX >> 12, state.FixedY >> 12,
            state.FixedZ >> 12, (ushort)originalIds[state.TriangleId], state.Heading)
        {
            NativeFixedPosition = new(state.FixedX, state.FixedY, state.FixedZ)
        };

        while (controller.CurrentCategory != target.Category)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, Position(), transform);
        }

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, Position(), transform);
        var input = FieldNavigationInput.None;
        var recent = new Queue<string>();
        for (var tick = 0; tick < 400; tick++)
        {
            var position = Position();
            controller.UpdateLiveTracking(position, new(0, input), transform, false, 80,
                observedAt: DateTime.UnixEpoch.AddMilliseconds(tick * 80));
            if (!controller.TryResolveAutomaticInput(position, transform, 80, out input))
            {
                trail = $"stopped at {position.X},{position.Y}: {controller.LastAutomaticInputHold}";
                return false;
            }

            var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
            var heading = unchecked((byte)(int)Math.Round(Math.Atan2(dx, -dy) * 128 / Math.PI));
            for (var frame = 0; frame < 3; frame++)
            {
                var fromX = state.FixedX / 4096d;
                var fromY = state.FixedY / 4096d;
                state = FieldNavigationNativeProbeMovement.Step(flat, state, heading, speed, testCase.Radius,
                    Array.Empty<FieldNavigationDynamicObstacle>()).State;
                if (SegmentsCross(fromX, fromY, state.FixedX / 4096d, state.FixedY / 4096d))
                {
                    trail = $"crossed at tick {tick}";
                    return true;
                }
            }

            recent.Enqueue($"{Position().X},{Position().Y} {input}");
            if (recent.Count > 6)
            {
                recent.Dequeue();
            }
        }

        trail = string.Join(" / ", recent);
        return false;
    }

    private static FieldNavigationTriggerLine Gateway;

    /// <summary>The exact z = 0 faces, their own edges kept and edges to anything else closed.</summary>
    private static (FieldWalkmesh Flat, int[] OriginalIds) FlatPortion(FieldWalkmesh full)
    {
        var flat = full.Triangles.Where(t => t.Vertex0.Z == 0 && t.Vertex1.Z == 0 && t.Vertex2.Z == 0).ToArray();
        var ids = flat.Select((t, i) => (t.Index, i)).ToDictionary(pair => pair.Index, pair => pair.i);
        short Adjacent(int old) => checked((short)ids.GetValueOrDefault(old, -1));
        var mesh = new FieldWalkmesh(flat.Select((t, i) => new FieldWalkmeshTriangle(i, t.Vertex0, t.Vertex1, t.Vertex2,
            Adjacent(t.Adjacent0), Adjacent(t.Adjacent1), Adjacent(t.Adjacent2))).ToArray());
        Equal(true, FieldNavigationNativeProbeMovement.IsSupportedWalkmesh(mesh), "the flat portion is supported by the native oracle");
        return (mesh, flat.Select(t => t.Index).ToArray());
    }

    private static bool InPlan(FieldWalkmeshTriangle triangle, double x, double y)
    {
        var (a, b, c) = (triangle.Vertex0, triangle.Vertex1, triangle.Vertex2);
        var denominator = (b.Y - c.Y) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Y - c.Y);
        if (Math.Abs(denominator) < 1e-9)
        {
            return false;
        }

        var wa = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / denominator;
        var wb = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / denominator;
        return wa >= 0 && wb >= 0 && 1 - wa - wb >= 0;
    }

    private static bool SegmentsCross(double ax, double ay, double bx, double by)
    {
        double Side(double px, double py, double qx, double qy, double rx, double ry) =>
            (qx - px) * (ry - py) - (qy - py) * (rx - px);
        var d1 = Side(Gateway.StartX, Gateway.StartY, Gateway.EndX, Gateway.EndY, ax, ay);
        var d2 = Side(Gateway.StartX, Gateway.StartY, Gateway.EndX, Gateway.EndY, bx, by);
        var d3 = Side(ax, ay, bx, by, Gateway.StartX, Gateway.StartY);
        var d4 = Side(ax, ay, bx, by, Gateway.EndX, Gateway.EndY);
        return d1 * d2 <= 0 && d3 * d4 <= 0 && (d1 != 0 || d2 != 0);
    }

    private static double DistanceToLine(double x, double y) =>
        DistanceToSegment(x, y, Gateway.StartX, Gateway.StartY, Gateway.EndX, Gateway.EndY);

    private static double DistanceToClosedEdges(FieldWalkmesh mesh, double x, double y)
    {
        var best = double.PositiveInfinity;
        foreach (var triangle in mesh.Triangles)
        {
            for (var edge = 0; edge < 3; edge++)
            {
                if (triangle.GetAdjacentTriangle(edge) >= 0)
                {
                    continue;
                }

                var (a, b) = triangle.GetEdge(edge);
                best = Math.Min(best, DistanceToSegment(x, y, a.X, a.Y, b.X, b.Y));
            }
        }

        return best;
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

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
