using Ff7.Accessibility.Reloaded;

/// <summary>
/// A place the body cannot stand is not an approach.
///
/// <para>jetin1's High Score Cloud is the case the 2026-09-08 recording caught and root
/// reproduced independently. The attendant stands at (369,637) on triangle 53, an isolated
/// counter island. The player was at (503,636) on triangle 16, whose western boundary runs
/// (476,589) to (479,718) - so the eight-unit inset produced (482,641), four units inside a
/// wall, 113 units from the attendant, and chosen every time because being on the player's
/// own triangle makes the route length zero. The log shows the party stopping at x=503 and
/// auto walk asking for Up forever.</para>
///
/// <para>These are root's four body/talk combinations, run against the installed flevel
/// through the same planner the mod uses, with the same native probe. They are here rather
/// than only in root's harness because the harness is root's and can be retired; this is
/// the regression.</para>
/// </summary>
internal static class FieldInteractionApproachBodyClearanceTests
{
    // The player's collision radius as the native obstacle reader reports it, and the two
    // talk ranges the mod uses. Every combination has to work: a fix that only holds for
    // one body size is a coincidence.
    private static readonly int[] BodyRadii = [24, 30];
    private static readonly int[] TalkRanges = [120, 128];

    internal static void Run(Func<int, FieldWalkmeshReader> createReader)
    {
        var reader = createReader(487);
        TheApproachIsSomewhereTheBodyCanActuallyStand(reader);
        TheUnreachableCounterTriangleIsNotChosen(reader);
        WithoutAKnownBodyRadiusNothingChanges(reader);
    }

    private static void TheApproachIsSomewhereTheBodyCanActuallyStand(FieldWalkmeshReader reader)
    {
        foreach (var radius in BodyRadii)
        {
            foreach (var talk in TalkRanges)
            {
                var planner = CreatePlanner(reader, radius);
                var target = Attendant(talk);
                var start = PlayerStart();
                if (!planner.TryBuildRoute(start, target, out var plan))
                {
                    throw new InvalidOperationException(
                        $"Interaction approach: no route to the High Score attendant at body " +
                        $"{radius}, talk {talk}. Refusing to move is not a fix - the attendant " +
                        $"is reachable. {planner.LastDiagnostic}");
                }

                if (plan.FinalApproachToTargetDistance >= talk)
                {
                    throw new InvalidOperationException(
                        $"Interaction approach: the route ends {plan.FinalApproachToTargetDistance:0} " +
                        $"units from the attendant, outside the {talk}-unit talk range, at body " +
                        $"{radius}. Arriving out of range is arriving nowhere.");
                }

                if (!CanTheBodyMakeTheLastStep(planner, start, target, plan))
                {
                    throw new InvalidOperationException(
                        $"Interaction approach: the endpoint {plan.FinalApproach.X},{plan.FinalApproach.Y} " +
                        $"fails the native body probe at radius {radius}, talk {talk}. This is the " +
                        "original defect: a point a centre-only test accepts and a body cannot occupy.");
                }
            }
        }
    }

    private static void TheUnreachableCounterTriangleIsNotChosen(FieldWalkmeshReader reader)
    {
        // The exact endpoint from the log. It has to stop being chosen - not because these
        // coordinates are special-cased anywhere, but because a body of the player's real
        // size cannot stand there and the search now asks.
        foreach (var radius in BodyRadii)
        {
            var planner = CreatePlanner(reader, radius);
            var target = Attendant(120);
            if (!planner.TryBuildRoute(PlayerStart(), target, out var plan))
            {
                throw new InvalidOperationException(
                    $"Interaction approach: no route at body {radius}. {planner.LastDiagnostic}");
            }

            if (plan.FinalApproach is { X: 482, Y: 641 })
            {
                throw new InvalidOperationException(
                    $"Interaction approach: the logged dead endpoint 482,641 was chosen again at " +
                    $"body {radius}. It is four units inside triangle 16's western boundary and " +
                    "native motion stops twenty-four units short of it.");
            }

            if (plan.TargetTriangle is 53 or 52)
            {
                throw new InvalidOperationException(
                    $"Interaction approach: the route ends on triangle {plan.TargetTriangle}, the " +
                    "counter island the attendant stands on. The player cannot get onto it; an " +
                    "approach there would be a route to a place with no way in.");
            }
        }
    }

    private static void WithoutAKnownBodyRadiusNothingChanges(FieldWalkmeshReader reader)
    {
        // No obstacle reader means no player collision radius, and with nothing to check a
        // body against the search must behave exactly as it did before - legacy eight-unit
        // inset, same answer, including the wrong one. This is what keeps the change to the
        // one path that has the evidence to do better, and is why the doorway and required
        // waypoint regressions elsewhere are untouched.
        var planner = new FieldWalkmeshRoutePlanner(reader);
        if (!planner.TryBuildRoute(PlayerStart(), Attendant(120), out var plan))
        {
            throw new InvalidOperationException(
                $"Interaction approach: the radius-free planner built no route at all. " +
                $"{planner.LastDiagnostic}");
        }

        if (plan.FinalApproach is not { X: 482, Y: 641 })
        {
            throw new InvalidOperationException(
                "Interaction approach: without a readable body radius the search must produce " +
                "exactly what it always produced, and it produced " +
                $"{plan.FinalApproach.X},{plan.FinalApproach.Y} instead of 482,641. The inset " +
                "ladder has leaked into routes that have no body to check against.");
        }
    }

    /// <summary>
    /// The last four units into the endpoint, along the line the player arrives on - the
    /// same probe native movement uses, and the same one root's harness applies.
    /// </summary>
    private static bool CanTheBodyMakeTheLastStep(
        FieldWalkmeshRoutePlanner planner,
        FieldPositionSnapshot start,
        FieldNavigationTarget target,
        FieldNavigationRoutePlan plan)
    {
        var end = plan.FinalApproach;
        var toTargetX = target.X - (double)end.X;
        var toTargetY = target.Y - (double)end.Y;
        var length = Math.Sqrt((toTargetX * toTargetX) + (toTargetY * toTargetY));
        if (length <= 0.001d)
        {
            return true;
        }

        var approachStart = start with
        {
            X = end.X - (int)Math.Round(toTargetX / length * 4d),
            Y = end.Y - (int)Math.Round(toTargetY / length * 4d),
            Z = end.Z,
            TriangleId = (ushort)plan.TargetTriangle
        };
        return planner.IsNativeProbeMovementClear(approachStart, target, end);
    }

    private static FieldWalkmeshRoutePlanner CreatePlanner(FieldWalkmeshReader reader, int bodyRadius)
    {
        // The distant clerk is how the player's own collision radius reaches the planner -
        // the obstacle reader carries it on every obstacle it reports. At (290,-281) it is
        // nowhere near the counter and blocks nothing.
        var obstacles = new[] { new FieldNavigationDynamicObstacle(9, 290, -281, 0, 30, bodyRadius) };
        return new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => obstacles);
    }

    private static FieldNavigationTarget Attendant(int talkRange) =>
        new(
            487,
            FieldNavigationCategory.Npcs,
            "High score attendant",
            369,
            637,
            0,
            "npc:487:13",
            TriggerEntityId: 13,
            InteractionRadius: talkRange);

    private static FieldPositionSnapshot PlayerStart() => new(1, 487, 0, 503, 636, 0, 16, 192);
}
