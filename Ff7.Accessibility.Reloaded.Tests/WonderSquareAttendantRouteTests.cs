using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Wonder Square's first floor (games_1, 506), 2026-09-29 12:21:37-44: auto walk to the Man
/// (entity 5, 323,1176) set off from (86,1654) through the prize attendant's triangle 112, held
/// against her at (141,1603) for five seconds and stopped with "no route round 3 occupied
/// triangle(s)".
///
/// <para>The installed script places the attendant (s1, entity 8) at (172,1543) in triangle
/// 112, Woman m2 (6) at (201,1030) and Man m3 (7) at (143,994); none of them moves, and all are
/// solid before moment 1008. 0060BCFA gives each the default width 30 at scale 512; the logged
/// leader width is 34, so the model half-sum is 32. Triangle 112 is the only link from the
/// entrance side to the south half on the -255 floor, and its only way round is the west stair
/// and platform, which comes down past Man m3 through triangle 124.</para>
///
/// <para>Native evidence is the flat-plane replay of 00636C41/006367B7 on the horizontal -255
/// floor, translated to z = 0: 006367B7 tests walls in X and Y only and 00636C41 takes its slope
/// from edge differences, so a horizontal face moves the same at any height. Faces that are not
/// on that plane are left out as walls - never walked, never assumed clear.</para>
/// </summary>
internal static class WonderSquareAttendantRouteTests
{
    private const int Field = 506;
    private const int Floor = -255;
    private const int PlayerWidth = 34;
    private const int NpcWidth = 30;
    private const double HalfSum = (PlayerWidth + NpcWidth) / 2d;

    private static readonly FieldPositionSnapshot LoggedStart = new(FieldPositionReader.FieldModule, Field, 0, 86, 1654, Floor, 74, 0);
    private static readonly FieldPositionSnapshot LoggedStall = new(FieldPositionReader.FieldModule, Field, 0, 141, 1603, Floor, 105, 0);

    private static FieldNavigationTarget Man() =>
        new(Field, FieldNavigationCategory.Npcs, "Man", 323, 1176, Floor, StableId: "npc:506:5", InteractionRadius: 114);

    private static IReadOnlyList<FieldNavigationDynamicObstacle> Models() =>
    [
        new(6, 201, 1030, Floor, HalfSum, PlayerWidth),
        new(7, 143, 994, Floor, HalfSum, PlayerWidth),
        new(8, 172, 1543, Floor, HalfSum, PlayerWidth)
    ];

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        var reader = createWalkmeshReader(Field);
        var mesh = reader.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, 0, 0, 0, 0, 0)).Walkmesh
            ?? throw new InvalidOperationException("Wonder Square route: the installed games_1 walkmesh must be readable.");
        var floor = HorizontalFloor(mesh, Floor);

        TheAttendantClosesTheDirectAisleNatively(mesh, floor);
        ManThreeLeavesTheWestAisleOpenNatively(mesh, floor);
        TheRouteFromTheLoggedStartGoesRoundTheAttendant(reader, mesh, floor);
        TheRouteFromTheLoggedStallIsFound(reader, mesh, floor);
        TheTrackerWalksEveryCornerInOrder(reader, mesh);
        AnEmptyFloorKeepsTheShortAisle(reader);
        EveryFirstFloorTargetFromEveryLoggedStart(reader, mesh, floor);
        ACornerWithinAStepNeedsAnUnobstructedContinuation(reader, mesh);
        TheTrackerKeepsABlockedCornerWithinAStep();
        Console.WriteLine("Wonder Square attendant route tests passed.");
    }

    /// <summary>
    /// The one-step corner rule on the installed walkmesh. The ramp corner 16,1026 (Machine 14 from
    /// the 12:32:28 start), everybody standing where the script puts them: at 16,1024 and 15,1032, where running steps land, neither clearance test passes for the leg
    /// on to 88,1022 - Man m3 is grazed at its far end - and the corner's own body clearance carries it. That witness counts
    /// only while the leg from where the party stands crosses nobody: a person standing on it,
    /// within the one step, keeps the corner. The raised walkway corner -16,1540 (Machine 14 from
    /// the logged start, -225 to -215) passes on the unshrunk check from the actual spot, and the
    /// same person test holds there.
    /// </summary>
    private static void ACornerWithinAStepNeedsAnUnobstructedContinuation(FieldWalkmeshReader reader, FieldWalkmesh mesh)
    {
        var machine = new FieldNavigationTarget(Field, FieldNavigationCategory.Objects, "Machine 14", 286, 1345, Floor,
            StableId: "object:506:14", TriggerLine: new FieldNavigationTriggerLine(300, 1371, Floor, 272, 1319, Floor),
            LineActivationRadius: PlayerWidth, LineActivatesOnOk: true);
        FieldPositionSnapshot At(int x, int y)
        {
            var triangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, x, y, Floor + 20, -1);
            return new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, x, y, HeightNear(mesh, triangle, x, y), (ushort)triangle, 0);
        }

        // Everybody on the first floor, as in the matrix: Man m3 (7) at 143,994 is the one the
        // ramp corner's leg passes closest to.
        IReadOnlyList<FieldNavigationDynamicObstacle> People(params FieldNavigationDynamicObstacle[] more) =>
            new (int Id, int X, int Y)[] { (5, 323, 1176), (6, 201, 1030), (7, 143, 994), (8, 172, 1543) }
                .Select(p => new FieldNavigationDynamicObstacle(p.Id, p.X, p.Y, Floor, HalfSum, PlayerWidth))
                .Concat(more).ToArray();
        var everybody = Planner(reader, People());
        var rampCorner = new FieldNavigationRouteWaypoint(16, 1026, -250);
        var rampNext = new FieldNavigationRouteWaypoint(88, 1022, -255);
        foreach (var (x, y) in new[] { (16, 1024), (15, 1032) })
        {
            var offCorner = At(x, y);
            Equal((false, false), (everybody.IsAutomaticMovementClear(offCorner, machine, rampNext), everybody.IsModelClearLegClear(offCorner, machine, rampNext)),
                $"at {x},{y}, a step off the ramp corner, neither clearance test passes for the next leg");
            Equal(true, FieldNavigationRouteTracker.IsCornerReachedWithinAStep(everybody, offCorner, machine, rampCorner, rampNext),
                $"at {x},{y} the ramp corner is reached within a step: Man m3 is grazed only inside the next waypoint's arrival distance");
            var blocker = Planner(reader, People(new FieldNavigationDynamicObstacle(9, 52, 1024, -255, HalfSum, PlayerWidth)));
            Equal(false, blocker.IsModelAndBoundaryClear(offCorner, machine, rampNext), $"from {x},{y} a person at 52,1024 obstructs the leg");
            Equal(false, FieldNavigationRouteTracker.IsCornerReachedWithinAStep(blocker, offCorner, machine, rampCorner, rampNext),
                $"at {x},{y}, within a step, a person on the continuation keeps the corner");
        }

        Equal(false, FieldNavigationRouteTracker.IsCornerReachedWithinAStep(everybody, At(16, 1040), machine, rampCorner, rampNext),
            "fourteen units off the corner is more than a step");

        var walkwayCorner = new FieldNavigationRouteWaypoint(-16, 1540, -225);
        var walkwayNext = new FieldNavigationRouteWaypoint(-17, 1124, -215);
        var offWalkway = At(-15, 1541);
        Equal(true, everybody.IsModelClearLegClear(offWalkway, machine, walkwayNext),
            "the raised walkway leg passes the unshrunk native-probe check from the actual spot");
        Equal(true, FieldNavigationRouteTracker.IsCornerReachedWithinAStep(everybody, offWalkway, machine, walkwayCorner, walkwayNext),
            "and its corner is reached within a step");
        var onTheWalkway = Planner(reader, People(new FieldNavigationDynamicObstacle(9, -17, 1330, -220, HalfSum, PlayerWidth)));
        Equal((false, false), (onTheWalkway.IsModelClearLegClear(offWalkway, machine, walkwayNext),
                FieldNavigationRouteTracker.IsCornerReachedWithinAStep(onTheWalkway, offWalkway, machine, walkwayCorner, walkwayNext)),
            "a person on the walkway keeps that corner too");
    }

    /// <summary>
    /// The same rule through the tracker, with the obstruction reported as a closed native
    /// boundary or a model: two units from an explicit corner whose own leg is clear, the tracker
    /// keeps the corner while the continuation from the actual spot is obstructed and moves on
    /// once it is not.
    /// </summary>
    private static void TheTrackerKeepsABlockedCornerWithinAStep()
    {
        var corner = new FieldNavigationRouteWaypoint(100, 0, 0);
        var next = new FieldNavigationRouteWaypoint(100, 200, 0);
        var end = new FieldNavigationRouteWaypoint(100, 300, 0);
        var target = new FieldNavigationTarget(Field, FieldNavigationCategory.Objects, "Corner test", end.X, end.Y, 0, StableId: "object:506:99");
        foreach (var obstructed in new[] { true, false })
        {
            var planner = new CornerPlanner(corner, [
                new FieldNavigationRouteStep(corner, 0, MustReach: true, RequiresExplicitArrival: true),
                new FieldNavigationRouteStep(next, 0, MustReach: true, RequiresExplicitArrival: true),
                new FieldNavigationRouteStep(end, 0)])
            { Obstructed = obstructed };
            var tracker = new FieldNavigationRouteTracker(planner);
            var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, 0, 0, 0, 0, 0);
            Equal(true, tracker.TryStart(start, target, out var guidance), $"the corner route starts: {guidance.Diagnostic}");
            Equal(corner, guidance.Waypoint, "the first waypoint is the corner");
            var nearCorner = start with { X = 98, Y = 1 };
            Equal(true, tracker.TryUpdate(start with { X = 90, Y = 0 }, target, out _), "on the way");
            Equal(true, tracker.TryUpdate(nearCorner, target, out guidance), $"two units off the corner: {guidance.Diagnostic}");
            Equal(obstructed ? corner : next, guidance.Waypoint,
                obstructed ? "an obstructed continuation keeps the corner within a step" : "an open one moves on within a step");
        }
    }

    /// <summary>
    /// Three explicit steps. The body clearance of the leg on from the corner holds only from the
    /// corner itself; <see cref="Obstructed"/> is a model or closed boundary across that leg from
    /// anywhere else.
    /// </summary>
    private sealed class CornerPlanner(FieldNavigationRouteWaypoint corner, IReadOnlyList<FieldNavigationRouteStep> steps)
        : IFieldNavigationRoutePlanner, IFieldNavigationAutomaticMovementPlanner
    {
        public bool Obstructed { get; init; }

        public string LastDiagnostic => "corner test route";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = 0;
            return true;
        }

        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(position.FieldId, $"{target.FieldId}:{target.StableId}", [0], [],
                steps[^1].Waypoint, 0, StableWaypointsOverride: steps);
            return true;
        }

        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRouteWaypoint next)
        {
            next = steps[0].Waypoint;
            return true;
        }

        private bool AtCorner(FieldPositionSnapshot position) => position.X == corner.X && position.Y == corner.Y;

        public bool IsAutomaticMovementClear(FieldPositionSnapshot position, FieldNavigationTarget target, FieldNavigationRouteWaypoint destination) =>
            destination == corner || AtCorner(position);

        public bool IsModelClearLegClear(FieldPositionSnapshot position, FieldNavigationTarget target, FieldNavigationRouteWaypoint destination) =>
            IsAutomaticMovementClear(position, target, destination);

        public bool IsModelAndBoundaryClear(FieldPositionSnapshot position, FieldNavigationTarget target, FieldNavigationRouteWaypoint destination) =>
            !Obstructed;
    }

    /// <summary>
    /// Evidence, not the fix: with the three models where the script puts them no native walk
    /// at walking or running speed carries the leader from the stall into the south half, and
    /// with the attendant alone removed it does - so she is the obstruction, not the walls.
    /// </summary>
    private static void TheAttendantClosesTheDirectAisleNatively(FieldWalkmesh mesh, FlatFloor floor)
    {
        var models = Models();
        var withoutAttendant = models.Where(model => model.ModelIndex != 8).ToArray();
        bool SouthHalf(int x, int y, int fullTriangle) => fullTriangle is 110 or 108 or 89;
        foreach (var speed in new ushort[] { 1024, 2048 })
        {
            Equal(false, Witness(mesh, floor, 141, 1603, speed, models, SouthHalf) is not null,
                $"at speed {speed} the body cannot pass the attendant from the stall");
            Equal(true, Witness(mesh, floor, 141, 1603, speed, withoutAttendant, SouthHalf) is not null,
                $"at speed {speed} it can once she is not there");
        }
    }

    private static void ManThreeLeavesTheWestAisleOpenNatively(FieldWalkmesh mesh, FlatFloor floor)
    {
        var centre = mesh.Triangles[127].GetCentroid();
        foreach (var speed in new ushort[] { 1024, 2048 })
        {
            Equal(true, Witness(mesh, floor, (int)Math.Round(centre.X), (int)Math.Round(centre.Y), speed, Models(),
                    (_, _, triangle) => triangle == 125) is not null,
                $"at speed {speed} the body passes Man m3 through triangle 124, where he stands against its wall");
        }
    }

    /// <summary>
    /// The defect from the start: the first leg was clear, so the route through the attendant
    /// was taken and only found blocked on arrival. A route that passes through a solid model
    /// further on is not a route either.
    /// </summary>
    private static void TheRouteFromTheLoggedStartGoesRoundTheAttendant(FieldWalkmeshReader reader, FieldWalkmesh mesh, FlatFloor floor)
    {
        var plan = Plan(reader, LoggedStart, "from the logged start");
        Equal(false, plan.TrianglePath.Contains(112), $"the route does not use the attendant's triangle: {string.Join(",", plan.TrianglePath)}");
        EveryLegClearsTheModelsAndWalksNatively(mesh, floor, LoggedStart, plan, "from the logged start");
    }

    /// <summary>The defect at the stall: "no route round 3 occupied triangle(s)".</summary>
    private static void TheRouteFromTheLoggedStallIsFound(FieldWalkmeshReader reader, FieldWalkmesh mesh, FlatFloor floor)
    {
        var plan = Plan(reader, LoggedStall, "from the logged stall");
        Equal(false, plan.TrianglePath.Contains(112), "the route from the stall turns away from the attendant");
        Equal(true, plan.TrianglePath.Contains(124), "and comes down past Man m3");
        EveryLegClearsTheModelsAndWalksNatively(mesh, floor, LoggedStall, plan, "from the logged stall");
    }

    private static void EveryLegClearsTheModelsAndWalksNatively(
        FieldWalkmesh mesh, FlatFloor floor, FieldPositionSnapshot start, FieldNavigationRoutePlan plan, string label)
    {
        var steps = Steps(plan, start);
        var models = Models();
        var current = new FieldNavigationRouteWaypoint(start.X, start.Y, start.Z);
        var replayed = 0;
        var passedManThree = false;
        var verdicts = new List<string>();
        for (var index = 0; index < steps.Count; index++)
        {
            var next = steps[index].Waypoint;
            Equal(false, FieldNavigationDynamicObstacleGeometry.IntersectsAny(current, next, models),
                $"{label}: leg {index} to {next.X},{next.Y} clears every model");

            // Inside the Man's reach the controller has arrived; the last leg to his own
            // position is never walked.
            if (Distance(current, Man().X, Man().Y) <= Man().InteractionRadius)
            {
                break;
            }

            // Legs wholly on the horizontal -255 floor are walked with the native step at both
            // speeds, steering straight at the corner as auto walk does. Legs touching the stair
            // or the platform stay unknown here, as they are to the planner.
            var from = FieldWalkmeshPathfinder.ResolveTriangle(mesh, current.X, current.Y, current.Z, -1);
            var to = FieldWalkmeshPathfinder.ResolveTriangle(mesh, next.X, next.Y, next.Z, -1);
            if (from >= 0 && to >= 0 && floor.FullToFlat[from] >= 0 && floor.FullToFlat[to] >= 0)
            {
                var known = true;
                foreach (var speed in new ushort[] { 1024, 2048 })
                {
                    var stop = Steer(mesh, floor, current, next, speed, models);
                    if (stop == Unknown)
                    {
                        known = false;
                        verdicts.Add($"{current.X},{current.Y}->{next.X},{next.Y}@{speed} unknown");
                        continue;
                    }

                    verdicts.Add($"{current.X},{current.Y}->{next.X},{next.Y}@{speed} {(stop is null ? "walks" : stop)}");

                    Equal(null, stop, $"{label}: leg {index} {current.X},{current.Y} -> {next.X},{next.Y} walks natively at speed {speed}");
                }

                if (known)
                {
                    replayed++;
                    passedManThree |= Math.Min(Distance(current, 143, 994), Distance(next, 143, 994)) < 160;
                }
            }

            current = next;
        }

        Console.WriteLine($"Wonder Square attendant route {label}: native leg replay {string.Join("; ", verdicts)}");
        Equal(true, replayed >= 2, $"{label}: the legs on the -255 floor were replayed ({replayed})");
        Equal(true, passedManThree, $"{label}: including the ones beside Man m3 and Woman m2");
    }

    /// <summary>
    /// Accepting the route is not walking it. From each logged position the party is moved
    /// eight units a tick (running pace) at the tracker's current waypoint, the way auto walk
    /// holds its heading, with the triangle re-resolved on the walkmesh each tick. The tracker
    /// must never replan, never go back to an earlier corner, never steer to a point off the
    /// walkmesh, and must bring the party inside the Man's reach.
    /// </summary>
    private static void TheTrackerWalksEveryCornerInOrder(FieldWalkmeshReader reader, FieldWalkmesh mesh)
    {
        foreach (var (label, start) in new[] { ("logged start", LoggedStart), ("logged stall", LoggedStall) })
        {
            var planner = Planner(reader, Models());
            var target = Man();
            var tracker = new FieldNavigationRouteTracker(planner);
            Equal(true, tracker.TryStart(start, target, out var guidance), $"{label}: the tracker takes the route: {guidance.Diagnostic}");
            var corners = new List<FieldNavigationRouteWaypoint> { guidance.Waypoint };
            var trail = new List<string>();
            double x = start.X, y = start.Y;
            var triangle = (int)start.TriangleId;
            var arrived = false;
            for (var tick = 0; tick < 1000 && !arrived; tick++)
            {
                var aim = guidance.Waypoint;
                var dx = aim.X - x;
                var dy = aim.Y - y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 0.5d)
                {
                    var move = Math.Min(8d, length);
                    x += dx / length * move;
                    y += dy / length * move;
                }

                var z = HeightNear(mesh, triangle, x, y);
                var resolved = FieldWalkmeshPathfinder.ResolveTriangle(mesh, (int)Math.Round(x), (int)Math.Round(y), z, triangle);
                Equal(true, resolved >= 0, $"{label}: tick {tick} at {x:0},{y:0} stays on the walkmesh");
                triangle = resolved;
                z = HeightNear(mesh, triangle, x, y);
                var at = new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0,
                    (int)Math.Round(x), (int)Math.Round(y), z, (ushort)triangle, 0);
                if (Distance(new FieldNavigationRouteWaypoint(at.X, at.Y, at.Z), target.X, target.Y) <= target.InteractionRadius)
                {
                    arrived = true;
                    break;
                }

                Equal(true, tracker.TryUpdate(at, target, out guidance), $"{label}: tick {tick} at {at.X},{at.Y} keeps the route: {guidance.Diagnostic}");
                Equal(false, guidance.Replanned, $"{label}: tick {tick} at {at.X},{at.Y} does not replan: {guidance.Diagnostic}");
                if (guidance.Waypoint != corners[^1])
                {
                    corners.Add(guidance.Waypoint);
                    trail.Add($"t{tick}@{at.X},{at.Y}->{guidance.Waypoint.X},{guidance.Waypoint.Y}");
                }
            }

            Console.WriteLine($"TRAIL {label}: {string.Join(" ", trail)}");
            Equal(true, arrived, $"{label}: the party reaches the Man's reach; corners {string.Join(" ", corners.Select(c => $"{c.X},{c.Y}"))}");
            Equal(false, corners.Any(c => Distance(c, 172, 1543) < 80), $"{label}: no corner leads back to the attendant");
            Console.WriteLine($"Wonder Square attendant route from the {label}: tracker walk through {string.Join(" ", corners.Select(c => $"{c.X},{c.Y},{c.Z}"))}");
        }
    }

    private static int HeightNear(FieldWalkmesh mesh, int triangle, double x, double y)
    {
        var t = mesh.Triangles[triangle];
        var (a, b, c) = (t.Vertex0, t.Vertex1, t.Vertex2);
        var d = (b.Y - c.Y) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Y - c.Y);
        if (Math.Abs(d) < 0.000001d) return a.Z;
        var wa = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d;
        var wb = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d;
        return (int)Math.Round(wa * a.Z + wb * b.Z + (1 - wa - wb) * c.Z);
    }
    /// <summary>
    /// The rest of the session's games_1 targets, from every logged start and the two native
    /// arrivals: the Man (5), the Woman (6, refused at 12:22:14 and 12:32:27) and Man m3 (7,
    /// refused at 12:22:11 and 12:22:29) with the other people standing and the chosen one left
    /// out of the obstacles, and the four restored machines with all four people standing. For
    /// each: a route exists; no leg crosses a person (native probe clearance); every leg on the
    /// horizontal -255 floor that the flat-plane replay can judge walks at 1024 and 2048; and the
    /// route tracker, walked eight units a tick without a replan, brings the party to the
    /// target's own activation - Talk reach for a person, the engine's touch for a machine LINE.
    /// Legs over the stair, the platform or the ramp are planned and checked statically only.
    /// </summary>
    private static void EveryFirstFloorTargetFromEveryLoggedStart(FieldWalkmeshReader reader, FieldWalkmesh mesh, FlatFloor floor)
    {
        var people = new (int Id, int X, int Y)[] { (5, 323, 1176), (6, 201, 1030), (7, 143, 994), (8, 172, 1543) };
        var starts = new (string Label, FieldPositionSnapshot Position)[]
        {
            ("logged start 12:21:37", LoggedStart),
            ("logged stall 12:21:44", LoggedStall),
            ("12:32:28 start", new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, 150, 1646, Floor, 105, 0)),
            ("arrival from 505", new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, 14, 1782, Floor, 67, 0)),
            ("arrival from 507", new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, -4, 761, Floor, 4, 0))
        };
        var targets = new List<(FieldNavigationTarget Target, int Excluded)>();
        foreach (var (id, x, y) in people.Where(p => p.Id != 8))
        {
            targets.Add((new FieldNavigationTarget(Field, FieldNavigationCategory.Npcs, id switch { 5 => "Man", 6 => "Woman", _ => "Man m3" },
                x, y, Floor, StableId: $"npc:506:{id}", TriggerEntityId: id, InteractionRadius: 114), id));
        }

        foreach (var (id, line) in new[]
                 {
                     (11, new FieldNavigationTriggerLine(178, 1645, Floor, 189, 1575, Floor)),
                     (14, new FieldNavigationTriggerLine(300, 1371, Floor, 272, 1319, Floor)),
                     (15, new FieldNavigationTriggerLine(384, 1423, Floor, 333, 1414, Floor)),
                     (16, new FieldNavigationTriggerLine(-188, 1618, Floor, -271, 1711, Floor))
                 })
        {
            targets.Add((new FieldNavigationTarget(Field, FieldNavigationCategory.Objects, $"Machine {id}",
                (line.StartX + line.EndX) / 2, (line.StartY + line.EndY) / 2, Floor, StableId: $"object:506:{id}",
                TriggerLine: line, LineActivationRadius: PlayerWidth, LineActivatesOnOk: id != 16), -1));
        }

        var routes = 0;
        var replayedLegs = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (target, excluded) in targets)
        foreach (var (startLabel, start) in starts)
        {
            var label = $"{target.Label} from the {startLabel}";
            IReadOnlyList<FieldNavigationDynamicObstacle> models = people.Where(p => p.Id != excluded)
                .Select(p => new FieldNavigationDynamicObstacle(p.Id, p.X, p.Y, Floor, HalfSum, PlayerWidth)).ToArray();
            var planner = Planner(reader, models);
            Equal(true, planner.TryBuildRoute(start, target, out var plan), $"{label}: a route exists: {planner.LastDiagnostic}");
            var steps = Steps(plan, start);
            var current = new FieldNavigationRouteWaypoint(start.X, start.Y, start.Z);
            foreach (var step in steps)
            {
                Equal(false, FieldNavigationDynamicObstacleGeometry.IntersectsAny(current, step.Waypoint, models),
                    $"{label}: the leg to {step.Waypoint.X},{step.Waypoint.Y} clears every person");
                if (Arrived(target, current))
                {
                    break;
                }

                var from = FieldWalkmeshPathfinder.ResolveTriangle(mesh, current.X, current.Y, current.Z, -1);
                var to = FieldWalkmeshPathfinder.ResolveTriangle(mesh, step.Waypoint.X, step.Waypoint.Y, step.Waypoint.Z, -1);
                if (from >= 0 && to >= 0 && floor.FullToFlat[from] >= 0 && floor.FullToFlat[to] >= 0)
                {
                    foreach (var speed in new ushort[] { 1024, 2048 })
                    {
                        var stop = Steer(mesh, floor, current, step.Waypoint, speed, models);
                        if (stop == Unknown) continue;
                        Equal(null, stop, $"{label}: the leg {current.X},{current.Y} -> {step.Waypoint.X},{step.Waypoint.Y} walks natively at {speed}");
                        replayedLegs++;
                    }
                }

                current = step.Waypoint;
            }

            TrackerWalk(planner, mesh, start, target, label);
            routes++;
        }

        Console.WriteLine($"Wonder Square first floor: {routes} routes (3 people, 4 machines, 5 starts) clear every person, " +
            $"{replayedLegs} native leg replays walk, every tracker walk arrives; {clock.Elapsed.TotalSeconds:0.0} s.");
    }

    /// <summary>
    /// Whether a leg (or a point, from = to) passes within the native probes' reach (the body radius and a running step)
    /// of any face that is not on the -255 floor: a stair, the platform or the ramp beside the
    /// south-west narrows. The flat-plane replay treats those faces as walls and the game does
    /// not, so such a leg is not the replay's to judge.
    /// </summary>
    private static bool PassesNearOffFloorFace(FieldWalkmesh mesh, FlatFloor floor, FieldNavigationRouteWaypoint from, FieldNavigationRouteWaypoint to)
    {
        const double reach = PlayerWidth + 8d;
        var length = Math.Max(1d, Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2)));
        for (var sample = 0d; sample <= length; sample += 4d)
        {
            var x = from.X + (to.X - from.X) * sample / length;
            var y = from.Y + (to.Y - from.Y) * sample / length;
            for (var index = 0; index < mesh.Triangles.Count; index++)
            {
                if (floor.FullToFlat[index] >= 0) continue;
                var t = mesh.Triangles[index];
                var vertices = new[] { t.Vertex0, t.Vertex1, t.Vertex2 };
                var nearest = double.MaxValue;
                for (var edge = 0; edge < 3; edge++)
                {
                    var (a, b) = (vertices[edge], vertices[(edge + 1) % 3]);
                    double ex = b.X - a.X, ey = b.Y - a.Y;
                    var l = ex * ex + ey * ey;
                    var u = l == 0 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l, 0, 1);
                    nearest = Math.Min(nearest, Math.Sqrt(Math.Pow(x - a.X - u * ex, 2) + Math.Pow(y - a.Y - u * ey, 2)));
                }

                if (nearest <= reach) return true;
            }
        }

        return false;
    }
    private static bool Arrived(FieldNavigationTarget target, FieldNavigationRouteWaypoint at) =>
        target.TriggerLine is { } line
            ? FieldNativeLineContact.Touches(line, at.X, at.Y, at.Z, target.LineActivationRadius)
            : Distance(at, target.X, target.Y) <= target.InteractionRadius;

    private static void TrackerWalk(FieldWalkmeshRoutePlanner planner, FieldWalkmesh mesh, FieldPositionSnapshot start,
        FieldNavigationTarget target, string label)
    {
        var tracker = new FieldNavigationRouteTracker(planner);
        Equal(true, tracker.TryStart(start, target, out var guidance), $"{label}: the tracker takes the route: {guidance.Diagnostic}");
        double x = start.X, y = start.Y;
        var triangle = (int)start.TriangleId;
        var tail = new Queue<string>();
        double headingX = 0d, headingY = 0d;
        for (var tick = 0; tick < 1500; tick++)
        {
            tail.Enqueue($"{x:0},{y:0}->{guidance.Waypoint.X},{guidance.Waypoint.Y}");
            if (tail.Count > 12) tail.Dequeue();
            // Native stepping moves a whole step each tick in the held direction; it does not
            // stop exactly on a corner. On the corner itself the last direction is kept.
            var aim = guidance.Waypoint;
            var dx = aim.X - x;
            var dy = aim.Y - y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 0.5d)
            {
                (headingX, headingY) = (dx / length, dy / length);
            }

            x += headingX * 8d;
            y += headingY * 8d;

            var z = HeightNear(mesh, triangle, x, y);
            var resolved = FieldWalkmeshPathfinder.ResolveTriangle(mesh, (int)Math.Round(x), (int)Math.Round(y), z, triangle);
            Equal(true, resolved >= 0, $"{label}: tick {tick} at {x:0},{y:0} stays on the walkmesh");
            triangle = resolved;
            z = HeightNear(mesh, triangle, x, y);
            var at = new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, (int)Math.Round(x), (int)Math.Round(y), z, (ushort)triangle, 0);
            if (Arrived(target, new FieldNavigationRouteWaypoint(at.X, at.Y, at.Z)))
            {
                return;
            }

            Equal(true, tracker.TryUpdate(at, target, out guidance), $"{label}: tick {tick} at {at.X},{at.Y} keeps the route: {guidance.Diagnostic}");
            Equal(false, guidance.Replanned, $"{label}: tick {tick} at {at.X},{at.Y} does not replan: {guidance.Diagnostic}");
        }

        Equal(true, false, $"{label}: the tracker walk arrives within 1500 ticks; last {string.Join(" ", tail)}; plan {planner.LastDiagnostic}; {guidance.Diagnostic}");
    }
    /// <summary>With nobody standing there the ordinary short aisle comes back unchanged.</summary>
    private static void AnEmptyFloorKeepsTheShortAisle(FieldWalkmeshReader reader)
    {
        var planner = Planner(reader, Array.Empty<FieldNavigationDynamicObstacle>());
        Equal(true, planner.TryBuildRoute(LoggedStart, Man(), out var plan), $"the empty-floor route exists: {planner.LastDiagnostic}");
        Equal(true, plan.TrianglePath.Contains(112), "and it is the short aisle through triangle 112");
    }

    private static FieldNavigationRoutePlan Plan(FieldWalkmeshReader reader, FieldPositionSnapshot start, string label)
    {
        var planner = Planner(reader, Models());
        Equal(true, planner.TryBuildRoute(start, Man(), out var plan), $"{label}: a route to the Man exists: {planner.LastDiagnostic}");
        return plan;
    }

    private static FieldWalkmeshRoutePlanner Planner(FieldWalkmeshReader reader, IReadOnlyList<FieldNavigationDynamicObstacle> models) =>
        new(reader, dynamicObstacleProvider: (_, _) => models, playerCollisionRadiusProvider: _ => PlayerWidth);

    private static IReadOnlyList<FieldNavigationRouteStep> Steps(FieldNavigationRoutePlan plan, FieldPositionSnapshot start) =>
        plan.StableWaypointsOverride ?? FieldWalkmeshPathfinder.BuildStableWaypoints(
            start.X, start.Y, start.Z, plan.Portals, plan.FinalApproach);

    private sealed record FlatFloor(FieldWalkmesh Flat, int[] FullToFlat, int[] FlatToFull);

    /// <summary>The faces wholly at <paramref name="height"/>, moved to z = 0; every other face is a wall.</summary>
    private static FlatFloor HorizontalFloor(FieldWalkmesh mesh, int height)
    {
        var fullToFlat = new int[mesh.Triangles.Count];
        var flatToFull = new List<int>();
        for (var index = 0; index < mesh.Triangles.Count; index++)
        {
            var t = mesh.Triangles[index];
            var on = t.Vertex0.Z == height && t.Vertex1.Z == height && t.Vertex2.Z == height;
            fullToFlat[index] = on ? flatToFull.Count : -1;
            if (on) flatToFull.Add(index);
        }

        var flat = new FieldWalkmesh(flatToFull.Select((full, index) =>
        {
            var t = mesh.Triangles[full];
            short Adjacent(int edge)
            {
                var neighbour = t.GetAdjacentTriangle(edge);
                return neighbour >= 0 && fullToFlat[neighbour] >= 0 ? (short)fullToFlat[neighbour] : (short)-1;
            }

            return new FieldWalkmeshTriangle(index, t.Vertex0 with { Z = 0 }, t.Vertex1 with { Z = 0 }, t.Vertex2 with { Z = 0 },
                Adjacent(0), Adjacent(1), Adjacent(2));
        }).ToArray());
        Equal(true, FieldNavigationNativeProbeMovement.IsSupportedWalkmesh(flat), "the translated -255 floor is a supported flat plane");
        return new FlatFloor(flat, fullToFlat, flatToFull.ToArray());
    }

    private static IReadOnlyList<FieldNavigationDynamicObstacle> OnFloor(IReadOnlyList<FieldNavigationDynamicObstacle> models) =>
        models.Select(model => model with { Z = model.Z - Floor }).ToArray();

    /// <summary>Breadth-first native walk over 16 held headings, two-unit cells.</summary>
    private static List<(int X, int Y)>? Witness(FieldWalkmesh mesh, FlatFloor floor, int x, int y, ushort speed,
        IReadOnlyList<FieldNavigationDynamicObstacle> models, Func<int, int, int, bool> goal)
    {
        var obstacles = OnFloor(models);
        var startTriangle = floor.FullToFlat[FieldWalkmeshPathfinder.ResolveTriangle(mesh, x, y, Floor, -1)];
        var start = new FieldNavigationNativeMovementState(x << 12, y << 12, 0, startTriangle, 0);
        (int, int) Key(FieldNavigationNativeMovementState state) => (state.FixedX >> 13, state.FixedY >> 13);
        var seen = new Dictionary<(int, int), (int, int)?> { [Key(start)] = null };
        var queue = new Queue<FieldNavigationNativeMovementState>();
        queue.Enqueue(start);
        while (queue.Count > 0 && seen.Count < 200_000)
        {
            var state = queue.Dequeue();
            if (goal(state.FixedX >> 12, state.FixedY >> 12, floor.FlatToFull[state.TriangleId]))
            {
                var path = new List<(int X, int Y)>();
                for ((int, int)? key = Key(state); key is { } k; key = seen[k]) path.Add((k.Item1 * 2, k.Item2 * 2));
                path.Reverse();
                return path;
            }

            for (var heading = 0; heading < 256; heading += 16)
            {
                var step = FieldNavigationNativeProbeMovement.Step(floor.Flat, state, (byte)heading, speed, PlayerWidth, obstacles);
                if (!step.IsSupported || !step.Moved || !seen.TryAdd(Key(step.State), Key(state))) continue;
                queue.Enqueue(step.State);
            }
        }

        return null;
    }

    private const string Unknown = "unknown: a probe reaches a face off this floor";

    /// <summary>
    /// Holds the native heading at the corner each tick, as auto walk does; null when it arrives.
    /// A stop whose probes reach a stair or platform face is <see cref="Unknown"/>: the replay
    /// treats those faces as walls, the game does not.
    /// </summary>
    private static string? Steer(FieldWalkmesh mesh, FlatFloor floor, FieldNavigationRouteWaypoint from, FieldNavigationRouteWaypoint to, ushort speed,
        IReadOnlyList<FieldNavigationDynamicObstacle> models)
    {
        var obstacles = OnFloor(models);
        var full = FieldWalkmeshPathfinder.ResolveTriangle(floor.Flat, from.X, from.Y, 0, -1);
        if (full < 0) return "the start is not on the floor";
        var state = new FieldNavigationNativeMovementState(from.X << 12, from.Y << 12, 0, full, 0);
        var arrival = speed / 256d;
        for (var tick = 0; tick < 2000; tick++)
        {
            var dx = to.X - state.FixedX / 4096d;
            var dy = to.Y - state.FixedY / 4096d;
            if (dx * dx + dy * dy <= arrival * arrival) return null;
            var heading = unchecked((byte)(int)Math.Round(Math.Atan2(dx, -dy) * 128d / Math.PI, MidpointRounding.AwayFromZero));
            var step = FieldNavigationNativeProbeMovement.Step(floor.Flat, state, heading, speed, PlayerWidth, obstacles);
            if (!step.IsSupported) return "unsupported";
            if (!step.Moved)
            {
                var x = state.FixedX / 4096d;
                var y = state.FixedY / 4096d;
                // The native step slides the heading in eights either way (up to sixteen tries)
                // before it gives up; a stop any of whose probes lands on a face off this floor
                // is the replay's wall, not the game's.
                foreach (var offset in Enumerable.Range(-8, 17).SelectMany(slide => new[] { slide * 8, slide * 8 + 32, slide * 8 - 32 }))
                {
                    var angle = unchecked((byte)(heading + offset)) * Math.PI / 128d;
                    var probe = FieldWalkmeshPathfinder.ResolveTriangle(mesh, (int)Math.Round(x + Math.Sin(angle) * PlayerWidth),
                        (int)Math.Round(y - Math.Cos(angle) * PlayerWidth), Floor, -1);
                    if (probe >= 0 && floor.FullToFlat[probe] < 0) return Unknown;
                }

                // Walking past an artificial wall proves the leg; stopping within probe reach of
                // one does not prove the game would stop.
                var here = new FieldNavigationRouteWaypoint((int)Math.Round(x), (int)Math.Round(y), 0);
                if (PassesNearOffFloorFace(mesh, floor, here, here)) return Unknown;
                return $"stopped at {x:0.0},{y:0.0}";
            }
            state = step.State;
        }

        return "no arrival";
    }

    private static double Distance(FieldNavigationRouteWaypoint point, int x, int y) =>
        Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Wonder Square attendant route - {label}: expected {expected}, got {actual}.");
        }
    }
}
