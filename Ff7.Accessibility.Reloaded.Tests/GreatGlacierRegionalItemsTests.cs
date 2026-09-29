using System.Diagnostics;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Great Glacier's seven treasures as regional Objects: listed on every connected screen,
/// the cabin and the snowfield until each is picked up, and walked to one native leg at a
/// time along the least estimated walking, across screens, passage reloads and the snowfield.
///
/// <para>Everything is checked against the installed game data: the route table is built from
/// the installed scripts, walkmeshes, WM3.MAP, field.tbl and wm3.ev, and the controllers are
/// driven over the installed walkmeshes with the game's own arrivals.</para>
/// </summary>
internal static class GreatGlacierRegionalItemsTests
{
    private static Fixture? shared;

    public static void Run()
    {
        SameScreenReloadIsAnArrivalOnlyOnceTheGameHasMovedTheParty();
        CollapseIsToldApartFromWalkingIntoTheCabin();
        AlexanderGoesThroughTheSpringAndSnow();
        Console.WriteLine("Great Glacier regional items: arrival, collapse and Alexander stage rules passed.");
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var fixture = shared ??= new Fixture(root);
        TheRouteTableIsBuiltFromNativeDataOffThePollingPath(fixture);
        SnowfieldPlacementsAreWm3Own(fixture);
        MoveFLocksFollowTheCorridorByte(fixture);
        ALockedStartingTriangleCanBeLeftButNotEntered(fixture);
        EveryTreasureIsReachableFromEveryNativeArrival(fixture);
        TheRouteMinimisesWalkingNotScreens(fixture);
        OneWayPathsStayOneWay(fixture);
        AddedCutIsOnlyThereFromTheRightPassage(fixture);
        TheSafetyBitIsAcrossTheIceFloes(fixture);
        RowsListEveryUncollectedTreasureOnceWithoutClaimingItIsHere(fixture);
        CabinToAllAcrossTheSnowfield(fixture);
        PassageReloadsAdvanceTheRouteAndDoNotLoop(fixture);
        BattleMapScreenAndCancelKeepOrEndTheRoute(fixture);
        CollapseLoadAndLeavingClearOrPause(fixture);
        TheIceFloesAreHandedToThePlayer(fixture);
        AlexanderIsWalkedThroughItsNativeSteps(fixture);
        AFailureStopsOnceWithAReason(fixture);
        FieldAndWorldLegsAreRetiredAtTheDomainSeam(fixture);
        TheSnowfieldEntranceHoldsUntilTheGameEntersTheField(fixture);
        AMapOpenedMidLegResumesTheRoute(fixture);
        MissingInputsEndTheRouteWithAReasonInsteadOfWaiting(fixture);
        AGoalNotYetOfferedInItsOwnRoomIsWaitedForNotCircled(fixture);
        Console.WriteLine(
            $"Great Glacier regional items: {fixture.Graph.Nodes.Count} native arrivals, {fixture.Graph.Edges.Count} legs, " +
            $"cold build {fixture.Graph.BuildTime.TotalMilliseconds:0} ms, warm first leg {fixture.WarmLegMilliseconds:0.0} ms.");
    }

    // ------------------------------------------------------------------ rules without data

    private static void SameScreenReloadIsAnArrivalOnlyOnceTheGameHasMovedTheParty()
    {
        var region = new GreatGlacierRegionalNavigator(_ => 0);
        var t = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        Equal(GreatGlacierArrival.FirstSample, region.ObserveField(674, 57, 100, 100, t), "the first sample");
        Equal(GreatGlacierArrival.None, region.ObserveField(674, 57, 110, 100, t.AddMilliseconds(100)), "walking is not an arrival");
        // The LINE writes the new byte, then MAPJUMPs into the same screen.
        Equal(GreatGlacierArrival.None, region.ObserveField(674, 58, 112, 100, t.AddMilliseconds(200)),
            "a byte written on the line, before the jump, is not yet an arrival");
        Equal(GreatGlacierArrival.SameFieldReload, region.ObserveField(674, 58, -900, 40, t.AddMilliseconds(600)),
            "placed at the reload's arrival: a new leg");
        Equal(GreatGlacierArrival.None, region.ObserveField(674, 58, -890, 40, t.AddMilliseconds(700)),
            "the next sample on the new leg is not another arrival");
        region.NotePositionGap();
        Equal(GreatGlacierArrival.None, region.ObserveField(674, 58, -890, 40, t.AddMilliseconds(800)), "a gap without a new byte is nothing");
        _ = region.ObserveField(674, 57, -890, 40, t.AddMilliseconds(900));
        region.NotePositionGap();
        Equal(GreatGlacierArrival.SameFieldReload, region.ObserveField(674, 57, -880, 40, t.AddMilliseconds(1000)),
            "a new byte across a load gap is a reload even when the arrival is near the line");
        _ = region.ObserveField(674, 58, -880, 40, t.AddSeconds(2));
        Equal(GreatGlacierArrival.SameFieldReload, region.ObserveField(674, 58, -880, 41, t.AddSeconds(4.1)),
            "a new byte that holds is a reload");
        Equal(GreatGlacierArrival.NewField, region.ObserveField(679, null, 0, 0, t.AddSeconds(5)), "another screen");
    }

    private static void CollapseIsToldApartFromWalkingIntoTheCabin()
    {
        var region = new GreatGlacierRegionalNavigator(_ => 0);
        var t = DateTime.UtcNow;
        _ = region.ObserveField(670, 52, 0, 0, t);
        Equal(GreatGlacierArrival.CollapsedToCabin, region.ObserveField(688, null, 0, 0, t.AddSeconds(1)),
            "a glacier screen straight into the cabin is Cloud's collapse script, not an exit");
        _ = region.ObserveField(686, null, 0, 0, t.AddSeconds(2));
        Equal(GreatGlacierArrival.NewField, region.ObserveField(687, null, 0, 0, t.AddSeconds(3)),
            "walking in from outside the cabin is an ordinary arrival");
    }

    private static void AlexanderGoesThroughTheSpringAndSnow()
    {
        var bank = new byte[256];
        var region = new GreatGlacierRegionalNavigator(address => bank[address]);
        Equal(GreatGlacierGoalStage.HotSpring, region.CurrentGoal(GreatGlacierTreasure.Alexander, out _)!.Value.Stage,
            "before the spring: the spring (Snow only fights once 199 bit 0 is set)");
        bank[199] = 0x01;
        Equal(GreatGlacierGoalStage.Snow, region.CurrentGoal(GreatGlacierTreasure.Alexander, out _)!.Value.Stage,
            "spring touched: Snow");
        bank[199] = 0x05;
        var goal = region.CurrentGoal(GreatGlacierTreasure.Alexander, out _)!.Value;
        Equal((GreatGlacierGoalStage.Treasure, 684, 14), (goal.Stage, goal.FieldId, goal.EntityId),
            "Snow beaten (bit 2): the materia model is shown");
        bank[199] = 0x15;
        Equal(true, region.CurrentGoal(GreatGlacierTreasure.Alexander, out var readable) is null && readable,
            "picked up (bit 4): no goal at all - victory alone is not collection");
        var unreadable = new GreatGlacierRegionalNavigator(_ => null);
        Equal(true, unreadable.CurrentGoal(GreatGlacierTreasure.Elixir, out var ok) is null && !ok,
            "an unreadable flag is not a collected treasure");
    }

    // ------------------------------------------------------------------ the route table

    private static void TheRouteTableIsBuiltFromNativeDataOffThePollingPath(Fixture fixture)
    {
        var graph = fixture.Graph;
        Equal(true, graph.Nodes.Count > 100 && graph.Edges.Count > 400,
            $"the region graph covers the glacier ({graph.Nodes.Count} nodes, {graph.Edges.Count} edges)");
        Equal(true, graph.BuildTime < TimeSpan.FromSeconds(30), $"a cold build is bounded ({graph.BuildTime.TotalMilliseconds:0} ms)");

        // The navigator builds it on a task: attaching returns at once.
        var region = new GreatGlacierRegionalNavigator(_ => 0);
        var clock = Stopwatch.StartNew();
        region.AttachFieldData(fixture.Root, null);
        region.AttachSnowfield(fixture.Snowfield, fixture.SnowfieldCatalog.Locations, fixture.SnowfieldCatalog.EntranceTriangleIds, fixture.WorldArchive);
        var attach = clock.Elapsed;
        Equal(true, attach < TimeSpan.FromMilliseconds(250), $"attaching does not build on the caller's thread ({attach.TotalMilliseconds:0} ms)");
        Equal(true, region.WaitForGraph(TimeSpan.FromSeconds(60)) && region.Graph!.Nodes.Count == graph.Nodes.Count,
            "the background build produces the same table");
    }

    private static void SnowfieldPlacementsAreWm3Own(Fixture fixture)
    {
        var placements = fixture.Graph.SnowfieldPlacements;
        Equal((28576, 9692), placements[60], "north edge entry: the 2026-09-28/29 logs land at (28576, 9692)");
        Equal((9692, 36768), placements[61], "west edge entry: the full log lands at (9692, 36768)");
        Equal((36768, 56152), placements[62], "south edge entry, mesh (4,6) + (4000,7000)");
        Equal((56152, 28576), placements[63], "east edge entry, mesh (6,3) + (7000,4000)");
        Equal((34500, 36113), placements[64], "cave entry, mesh (4,4) + (1732,3345)");
        Equal<int?>(52, fixture.Graph.SnowfieldCorridors[61], "the west edge writes byte 184 = 52");
        Equal<int?>(53, fixture.Graph.SnowfieldCorridors[62], "the south edge writes 53");
        Equal<int?>(55, fixture.Graph.SnowfieldCorridors[63], "the east edge writes 55");
    }

    private static void MoveFLocksFollowTheCorridorByte(Fixture fixture)
    {
        int[] a = [175, 176, 178, 179, 180, 182, 187];
        int[] b = [76, 85, 129, 196, 197, 198, 199, 200, 209, 215, 216, 217, 221];
        int[] c = [184, 185, 188, 189, 205, 206, 207, 208, 213, 214];
        var scripts = fixture.Catalog.ReadAllScriptOpcodes(672);
        foreach (var (state, expected) in new (int, int[])[] { (0, [.. a, .. c]), (1, [.. a, .. c]), (3, [.. a, .. c]), (4, [.. a, .. c]), (5, [.. b, .. c]), (6, [.. b, .. c]), (7, c), (8, c) })
        {
            Equal(string.Join(',', expected.Order()), string.Join(',', GreatGlacierScriptMachine.LockedTriangles(scripts, state).Order()),
                $"move_f (672) with byte 184 = {state}: e3 scripts per state, then script 5 unconditionally");
        }

        Equal(0, GreatGlacierScriptMachine.LockedTriangles(fixture.Catalog.ReadAllScriptOpcodes(670), 52).Count,
            "move_s has no locks");
    }

    private static void ALockedStartingTriangleCanBeLeftButNotEntered(Fixture fixture)
    {
        var mesh = fixture.Mesh(672);
        var locked = GreatGlacierScriptMachine.LockedTriangles(fixture.Catalog.ReadAllScriptOpcodes(672), 1);
        var from = new FieldNavigationRouteWaypoint(1180, 179, (int)Math.Round(mesh.Triangles[179].GetCentroid().Z));
        var to = new FieldNavigationRouteWaypoint(1207, 114, (int)Math.Round(mesh.Triangles[125].GetCentroid().Z));
        Equal(true, FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, 179, from, to, locked.Contains, applyPortalInset: false).IsClear,
            "egress: 179 (locked, where the east arrival puts the party) -> 125 (open), as 006367b7 allows");
        Equal(false, FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, 125, to, from, locked.Contains, applyPortalInset: false).IsClear,
            "ingress: 125 -> 179 is refused, the entered triangle is locked");

        // The live planner from the native arrival with those locks set.
        var live = fixture.LivePlanner(672, locked);
        var arrival = fixture.Position(672, 1180, 179, 179);
        var exits = fixture.Exits(672);
        Equal(true, exits.Any(exit => live.TryBuildRoute(arrival, exit, out _)),
            $"from the locked arrival the leader walks out to an exit: {live.LastDiagnostic}");
        // Directed entry, in whole plans: from the locked start every planned route leaves 179
        // and never comes back into it or into any other locked triangle, and from its open
        // neighbour 125 nothing is planned into 179.
        var inside = fixture.Position(672, 1207, 114, 125);
        var planned = 0;
        foreach (var triangle in mesh.Triangles.Where(triangle => !locked.Contains(triangle.Index)))
        {
            var centre = triangle.GetCentroid();
            var goal = new FieldNavigationTarget(672, FieldNavigationCategory.Objects, "probe", (int)centre.X, (int)centre.Y,
                (int)centre.Z, $"probe:{triangle.Index}", InteractionRadius: 8);
            if (!live.TryBuildRoute(arrival, goal, out var plan))
            {
                continue;
            }

            planned++;
            Equal(179, plan.TrianglePath[0], "a route from the arrival starts on its locked triangle");
            Equal(false, plan.TrianglePath.Skip(1).Any(locked.Contains),
                $"the route to triangle {triangle.Index} enters no locked triangle after leaving 179 ({string.Join(',', plan.TrianglePath)})");
            var steps = FieldWalkmeshPathfinder.BuildStableWaypoints(arrival.X, arrival.Y, arrival.Z, plan.Portals, plan.FinalApproach);
            for (var index = 1; index < steps.Count; index++)
            {
                var a = steps[index - 1].Waypoint;
                var b = steps[index].Waypoint;
                var fromTriangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, a.X, a.Y, a.Z, -1);
                if (fromTriangle == 179 || fromTriangle < 0)
                {
                    continue;
                }

                Equal(false, FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, fromTriangle, a, b, applyPortalInset: false).TraversedTriangles.Contains(179),
                    $"the route to triangle {triangle.Index} does not walk back through 179");
            }
        }

        Equal(true, planned > 100, $"the locked arrival reaches the open walkmesh ({planned} targets)");
        var into179 = new FieldNavigationTarget(672, FieldNavigationCategory.Objects, "locked", 1180, 179, from.Z, "probe:179", InteractionRadius: 4);
        var fromNeighbour = fixture.LivePlanner(672, locked);
        Equal(false, fromNeighbour.TryBuildRoute(inside, into179, out _),
            "from 125 the planner refuses to route back into the locked 179 (re-entry is entry)");
        var node = fixture.Graph.FindNode(672, 1180, 179, 179, 1)!;
        Equal(true, fixture.Graph.Edges.Any(edge => edge.From == node.Index),
            "the route table leaves move_f's locked east arrival");
    }

    private static void EveryTreasureIsReachableFromEveryNativeArrival(Fixture fixture)
    {
        // Every native arrival the table knows - every glacier and cabin screen, both lake
        // shores, every passage byte, every snowfield entry - against every goal stage.
        var graph = fixture.Graph;
        var unreachable = GreatGlacierRegion.AllGoalPoints
            .SelectMany(goal => graph.Nodes.Where(node => !double.IsFinite(graph.CostToGoal(goal, node)))
                .Select(node => $"{goal.Key} from {node.Key}"))
            .ToArray();
        Equal(0, unreachable.Length, $"every goal stage from every native arrival ({graph.Nodes.Count} x {GreatGlacierRegion.AllGoalPoints.Count}): {string.Join("; ", unreachable.Take(10))}");
        Equal(true, graph.Nodes.Any(node => node.FieldId == 665 && node.Y > 0) && graph.Nodes.Any(node => node.FieldId == 665 && node.Y < 0),
            "both lake shores are arrivals of the table");
        Equal(true, graph.Nodes.Count(node => GreatGlacierRegion.IsPassage(node.FieldId)) > 40, "every passage byte reached is an arrival");
    }

    private static void TheRouteMinimisesWalkingNotScreens(Fixture fixture)
    {
        var graph = fixture.Graph;
        var cabin = fixture.CabinArrival;
        var mind = Goal(GreatGlacierTreasure.MindSource);
        var weighted = graph.Route(mind, cabin);
        var fewest = FewestExits(graph, mind, cabin);
        Equal(true, fewest.Count < weighted.Count,
            $"cabin -> Mind Source: fewest exits is {fewest.Count} screens, least walking {weighted.Count}");
        var weightedTicks = graph.CostToGoal(mind, cabin);
        var fewestTicks = fewest.Sum(edge => edge.Ticks) + graph.CostToGoal(mind, graph.Nodes[fewest[^1].To]);
        Equal(true, weightedTicks < fewestTicks,
            $"and the least-walking route is shorter to walk ({weightedTicks:0} < {fewestTicks:0} ticks)");
        foreach (var goal in GreatGlacierRegion.AllGoalPoints)
        {
            foreach (var node in graph.Nodes)
            {
                var cost = graph.CostToGoal(goal, node);
                if (!double.IsFinite(cost) || graph.NextEdge(goal, node) is not { } edge)
                {
                    continue;
                }

                var viaNext = edge.Ticks + graph.CostToGoal(goal, graph.Nodes[edge.To]);
                Equal(true, Math.Abs(viaNext - cost) < 0.01, $"{goal.Key} from {node.Key}: the next leg is on the best route");
                foreach (var other in graph.Edges.Where(candidate => candidate.From == node.Index))
                {
                    Equal(true, other.Ticks + graph.CostToGoal(goal, graph.Nodes[other.To]) >= cost - 0.01,
                        $"{goal.Key} from {node.Key}: no other exit is shorter than the chosen one");
                }
            }
        }
    }

    private static void OneWayPathsStayOneWay(Fixture fixture)
    {
        var graph = fixture.Graph;
        var spring = Goal(GreatGlacierTreasure.Alexander, GreatGlacierGoalStage.HotSpring);
        var alexander = Goal(GreatGlacierTreasure.Alexander);
        var fromRavine = graph.Nodes.First(node => node.FieldId == 683 && !node.IsWorld);
        Equal(false, graph.Route(spring, fromRavine).Any(edge => edge.Kind == GreatGlacierRegionGraph.EdgeKind.Snowfield),
            "from the cliff ravine (683) the hot spring is reached through the glacier");
        var fromSpring = graph.Nodes.First(node => node.FieldId == 680 && !node.IsWorld);
        Equal(true, graph.Route(alexander, fromSpring).Any(edge => edge.Kind == GreatGlacierRegionGraph.EdgeKind.Snowfield),
            "from the hot spring back to Snow's cave the glacier's one-way trail does not lead: the route goes by the snowfield");
    }

    private static void AddedCutIsOnlyThereFromTheRightPassage(Fixture fixture)
    {
        var graph = fixture.Graph;
        var addedCut = Goal(GreatGlacierTreasure.AddedCut);
        foreach (var node in graph.Nodes.Where(node => node.FieldId == 675 && !node.IsWorld))
        {
            var route = graph.Route(addedCut, node);
            Equal(node.State == 75, route.Count == 0 && double.IsFinite(graph.CostToGoal(addedCut, node)),
                $"move_d at byte {node.State}: Added Cut is picked up here only at 75");
        }

        var path = graph.Route(addedCut, fixture.CabinArrival);
        Equal(675, graph.Nodes[path[^1].To].FieldId, "the route ends by entering move_d");
        Equal(75, graph.Nodes[path[^1].To].State, "with byte 184 = 75");
    }

    private static void TheSafetyBitIsAcrossTheIceFloes(Fixture fixture)
    {
        var route = fixture.Graph.Route(Goal(GreatGlacierTreasure.SafetyBit), fixture.CabinArrival);
        Equal(true, route.Any(edge => edge.Kind == GreatGlacierRegionGraph.EdgeKind.IceFloeCrossing),
            "the Safety Bit's route crosses the lake, which the player solves");
        var lake = fixture.Graph.Nodes.Where(node => node.FieldId == 665).ToArray();
        foreach (var node in lake)
        {
            foreach (var edge in fixture.Graph.Edges.Where(edge => edge.From == node.Index && edge.Kind == GreatGlacierRegionGraph.EdgeKind.ScriptExit))
            {
                var exit = fixture.Exits(665).First(candidate => candidate.StableId == edge.ExitId);
                Equal(GreatGlacierRegionGraph.IsNorthShore(node.Y), GreatGlacierRegionGraph.IsNorthShore((exit.TriggerLine!.Value.StartY + exit.TriggerLine.Value.EndY) / 2),
                    $"{edge.ExitId} from {node.Key}: nothing walks across the floes");
            }
        }
    }

    // ------------------------------------------------------------------ rows

    private static void RowsListEveryUncollectedTreasureOnceWithoutClaimingItIsHere(Fixture fixture)
    {
        var run = fixture.NewRun();
        var cabin = run.Arrive(fixture.CabinArrival);
        var rows = run.Objects(cabin).Where(row => row.StableId.StartsWith(GreatGlacierRegionalNavigator.RowIdPrefix, StringComparison.Ordinal)).ToArray();
        Equal(7, rows.Length, "the cabin lists all seven treasures");
        Equal(true, rows.All(row => row.Label.Contains("elsewhere in the Great Glacier", StringComparison.Ordinal)),
            "a remote row never says the treasure is in this room");
        Equal(true, rows.Single(row => row.StableId.EndsWith("Alexander", StringComparison.Ordinal)).Label.Contains("first the hot spring, then Snow", StringComparison.Ordinal),
            "Alexander says what comes first");

        run.Bank[37] = 0x01 | 0x02;
        run.Bank[199] = 0x40;
        var fewer = run.Objects(cabin).Count(row => row.StableId.StartsWith(GreatGlacierRegionalNavigator.RowIdPrefix, StringComparison.Ordinal));
        Equal(4, fewer, "Elixir, Potion and All picked up: their native flags remove only them");
        run.Bank[37] = 0;
        run.Bank[199] = 0;

        // In the forest the Mind Source stands here: its own live row, not a second one.
        var forest = run.Arrive(fixture.Graph.Nodes.First(node => node.FieldId == 659));
        var objects = run.Objects(forest);
        Equal(1, objects.Count(row => row.StableId.StartsWith("object:659:17:", StringComparison.Ordinal)), "the live Mind Source");
        Equal(false, objects.Any(row => row.StableId == GreatGlacierRegionalNavigator.RowId(GreatGlacierTreasure.MindSource)),
            "no duplicate regional Mind Source row");
        Equal(6, objects.Count(row => row.StableId.StartsWith(GreatGlacierRegionalNavigator.RowIdPrefix, StringComparison.Ordinal)),
            "the other six stay listed");
        Equal(false, run.Objects(run.Position(700, 0, 0, 0)).Any(row => row.StableId.StartsWith(GreatGlacierRegionalNavigator.RowIdPrefix, StringComparison.Ordinal)),
            "nothing outside the region");

        // The snowfield's Objects.
        var world = run.World;
        var state = fixture.WorldState(60);
        var worldRows = world.ReadRows(state);
        Equal(7, worldRows.Count, "the snowfield lists all seven");
        Equal(true, worldRows.All(row => row.Label.Contains(". First to ", StringComparison.Ordinal)), "each says which snowfield exit comes first");
    }

    // ------------------------------------------------------------------ lifecycle

    private static void CabinToAllAcrossTheSnowfield(Fixture fixture)
    {
        var run = fixture.NewRun();
        var at = run.Arrive(fixture.CabinArrival);
        var start = run.Select(at, GreatGlacierTreasure.All);
        Contains(start, "Navigation on. All Materia: next,", "choosing All from the cabin starts its first leg");
        Equal(true, run.Controller.BeaconEnabled, "the leg is being walked");
        run.Region.NoteAutoWalk(true);
        var legIds = new List<string> { run.Controller.CurrentRouteIdentity };

        // Walk out: the cabin's gateway to 686.
        var outside = run.Position(fixture.Graph.Nodes.First(node => node.FieldId == 686 && node.X == -28));
        var next = run.Tick(outside);
        Contains(next, "All Materia: next,", $"outside the cabin the next leg starts on its own ({run.Controller.LastNavigationDiagnostic})");
        Equal(true, run.Controller.TryConsumeHeldAutoWalkRequest(), "and is walked, as the player asked");

        // The snowfield: every host reports the module each frame; the world controller starts
        // the cave leg and the field leg left behind is retired, not reset by the test.
        run.Region.ObserveModule(WorldMapStateReader.WorldModule);
        var world = run.World.Observe(fixture.WorldState(60), autoWalk: false);
        Contains(world?.Speech, "All Materia: first to Cave in the middle of the snowfield", "on the snowfield the next leg is the central cave");
        Equal(true, world!.Value.StartAutoWalk, "and it is walked");
        var arrived = run.World.ArriveAtCurrentLeg();
        Contains(arrived?.Speech, "Continuing to All Materia", "reaching the cave entrance does not end the route");

        // The cave: the goal itself.
        run.Region.ObserveModule(FieldPositionReader.FieldModule);
        var cave = run.Position(fixture.Graph.Nodes.First(node => node.FieldId == 682));
        var goal = run.Tick(cave);
        Contains(goal, "All Materia. Navigation on.", "in the cave the materia is the leg");
        run.Bank[199] |= 0x40;
        Contains(run.Tick(cave), "All Materia collected. Navigation off.", "picking it up ends the route");
        Equal(true, run.Controller.TryConsumeAutoWalkStopRequest(), "and stops the walk");
        Equal(false, run.Region.HasObjective, "the objective is gone");
    }

    private static void PassageReloadsAdvanceTheRouteAndDoNotLoop(Fixture fixture)
    {
        var run = fixture.NewRun();
        var west = fixture.Graph.FindNode(670, 1188, -49, 63, 52) ?? fixture.Graph.Nodes.First(node => node.FieldId == 670 && node.State == 52);
        var at = run.Arrive(west);
        Contains(run.Select(at, GreatGlacierTreasure.Elixir), "Elixir: next,", "Elixir from the west-edge passage");
        var first = run.Controller.CurrentTargetLabel;
        var firstExit = run.LegExitId();
        var firstEdge = fixture.Graph.NextEdge(Goal(GreatGlacierTreasure.Elixir), west)!;
        Equal(firstEdge.ExitId, firstExit, "the leg is the table's best exit");
        var after = fixture.Graph.Nodes[firstEdge.To];
        Equal(670, after.FieldId, "the west edge's best way to the Elixir re-enters move_s itself (a same-screen jump)");
        // The same-screen MAPJUMP puts the party back where it stood, with a new byte; what
        // tells the reload apart is the engine loading the field afresh (no position meanwhile).
        run.Corridor = after.State;
        Equal<string?>(null, run.Tick(run.Position(after) with { CurrentModule = 0 }), "the load itself says nothing");
        var reloaded = run.Tick(run.Position(after));
        Contains(reloaded, "Elixir: next,", $"the reload is a new leg ({west.Key} -> {after.Key}; {run.Controller.LastNavigationDiagnostic})");
        Equal(fixture.Graph.NextEdge(Goal(GreatGlacierTreasure.Elixir), after)!.ExitId, run.LegExitId(),
            "planned from the new byte, not the previous leg looped");
        _ = first;
    }

    private static void BattleMapScreenAndCancelKeepOrEndTheRoute(Fixture fixture)
    {
        var run = fixture.NewRun();
        var cabin = run.Arrive(fixture.CabinArrival);
        _ = run.Select(cabin, GreatGlacierTreasure.Potion);
        var exit = run.LegExitId();

        // A battle: the game has the party; the route waits and carries on.
        Equal<string?>(null, run.Tick(cabin, suppressed: true), "suppressed samples say nothing");
        Equal(true, run.Region.HasObjective && run.Controller.BeaconEnabled, "the leg survives a battle or a scene");
        _ = run.Tick(cabin);
        Equal(exit, run.LegExitId(), "and is still the same leg afterwards");

        // The Glacier Map: a picture; back on the same screen the leg is planned again.
        Equal<string?>(null, run.Tick(run.Position(669, 0, 0, 0)), "the map screen says nothing of the route");
        Equal(true, run.Region.HasObjective, "the treasure is kept while the map is open");
        Contains(run.Tick(cabin), "Potion: next,", "back from the map the route goes on from where the party is");

        // Cancel while walking.
        Contains(run.Controller.HandleAction(FieldNavigationAction.ToggleBeacon, cabin, new FieldNavigationControlTransform(0))?.Speech,
            "Navigation off.", "cancelling says so");
        Equal(false, run.Region.HasObjective, "and ends the treasure route");

        // Cancel while a leg is pending (the controller menu's B on a held route): outside the
        // cabin, before the new screen's exits are offered.
        _ = run.Select(cabin, GreatGlacierTreasure.Potion);
        var outside = run.Position(fixture.Graph.Nodes.First(node => node.FieldId == 686 && node.X == -28));
        run.HideExits = true;
        Equal<string?>(null, run.Tick(outside), "the new screen's exits are not offered yet");
        run.HideExits = false;
        Equal(true, run.Controller.IsHoldingForNativeBoundary, "between legs the route is a held destination");
        var services = new ControllerNavigationServices(
            () => run.Controller.BeaconEnabled,
            action => run.Controller.HandleAction(action, outside, new FieldNavigationControlTransform(0))?.Speech,
            () => false, () => false, () => { }, () => { },
            () => run.Controller.IsHoldingForNativeBoundary,
            run.Controller.RequestAutoWalkForHeldRoute);
        Contains(services.Stop(), "Navigation off.", "B cancels a pending leg");
        Equal(false, run.Region.HasObjective, "and the treasure with it");

        // Choosing another target replaces the treasure route.
        cabin = run.Position(fixture.CabinArrival);
        _ = run.Select(cabin, GreatGlacierTreasure.Potion);
        run.Controller.HandleAction(FieldNavigationAction.PreviousCategory, cabin, new FieldNavigationControlTransform(0));
        Equal(false, run.Region.HasObjective, "browsing to another target while walking replaces it");

        // The stall guard stops auto walk: later legs are spoken, not walked, until asked again.
        _ = run.Select(cabin, GreatGlacierTreasure.Potion);
        run.Controller.NoteAutoWalkStarted();
        run.Controller.NoteAutoWalkStopped();
        Contains(run.Tick(outside), "Potion: next,", "the next screen's leg is still given");
        Equal(false, run.Controller.TryConsumeHeldAutoWalkRequest(), "no walk re-armed after a stop");
        run.Region.Cancel("test");
    }

    private static void CollapseLoadAndLeavingClearOrPause(Fixture fixture)
    {
        var run = fixture.NewRun();
        var passage = run.Arrive(fixture.Graph.Nodes.First(node => node.FieldId == 670 && node.State == 52));
        _ = run.Select(passage, GreatGlacierTreasure.Elixir);
        run.Region.NoteAutoWalk(true);
        var cabin = run.Position(fixture.Graph.Nodes.First(node => node.FieldId == 688));
        Contains(run.Tick(cabin), "Taken to Holzoff's cabin. Navigation to Elixir paused and auto walk off.", "a collapse pauses the route");
        Equal(true, run.Controller.TryConsumeAutoWalkStopRequest(), "and stops the walk");
        Equal(true, run.Region.Objective is { Paused: true, AutoWalk: false }, "the destination is kept, not walked");
        Equal<string?>(null, run.Tick(cabin), "nothing starts by itself after the cabin scene");
        run.Controller.RequestAutoWalkForHeldRoute();
        Contains(run.Tick(cabin), "Elixir: next,", "asking to walk resumes it from the cabin");
        Equal(true, run.Controller.TryConsumeHeldAutoWalkRequest(), "walked");

        run.Region.ObserveModule(TitleMenuCursorReader.TitleModule);
        Equal(false, run.Region.HasObjective, "the title screen (a load or new game) clears it");
        run.Region.ObserveModule(FieldPositionReader.FieldModule);
        Equal<string?>(null, run.Tick(cabin), "after a load nothing of the old route speaks");
        Equal(false, run.Controller.BeaconEnabled, "and its leg is retired, not revived");

        _ = run.Select(cabin, GreatGlacierTreasure.Elixir);
        Contains(run.Tick(run.Position(700, 0, 0, 0)), "Left the Great Glacier. Navigation to Elixir cancelled.", "leaving the region ends it");
        Equal(false, run.Region.HasObjective, "gone");

        cabin = run.Position(fixture.CabinArrival);
        _ = run.Select(cabin, GreatGlacierTreasure.All);
        run.Region.ObserveModule(WorldMapStateReader.WorldModule);
        var elsewhere = run.World.Observe(fixture.WorldState(60) with { WorldMapType = 0 }, autoWalk: false);
        Contains(elsewhere?.Speech, "Left the Great Glacier", "the main world map ends it too");
    }

    private static void TheIceFloesAreHandedToThePlayer(Fixture fixture)
    {
        var run = fixture.NewRun();
        var south = fixture.Graph.Nodes.First(node => node.FieldId == 665 && node.Y < 0 && node.Triangle != GreatGlacierRegion.SouthShoreLanding.Triangle);
        var at = run.Arrive(south);
        var start = run.Select(at, GreatGlacierTreasure.SafetyBit);
        Contains(start, "Safety Bit: across the ice floes", "the leg is the crossing start, for the player to solve");
        Equal(true, run.Controller.CurrentTargetLabel.Contains("south shore", StringComparison.Ordinal), "on this shore");
        run.Crossing = true;
        Equal<string?>(null, run.Tick(run.Position(665, 170, -387, 139)), "while crossing, navigation stays out of the way");
        Equal(true, run.Region.HasObjective && !run.Controller.BeaconEnabled, "the treasure is kept, no leg is driven over the floes");
        run.Crossing = false;
        var north = run.Position(665, GreatGlacierRegion.NorthShoreLanding.X, GreatGlacierRegion.NorthShoreLanding.Y, GreatGlacierRegion.NorthShoreLanding.Triangle);
        Contains(run.Tick(north), "Safety Bit: next,", "across: the route resumes on the north shore");
        var landing = fixture.Graph.FindNode(665, north.X, north.Y, north.TriangleId, 0)!;
        Equal(fixture.Graph.NextEdge(Goal(GreatGlacierTreasure.SafetyBit), landing)!.ExitId, run.LegExitId(),
            "the leg from the north shore is the table's best exit from where the crossing lands");
        var exitLine = fixture.Exits(665).First(exit => exit.StableId == run.LegExitId()).TriggerLine!.Value;
        Equal(true, GreatGlacierRegionGraph.IsNorthShore((exitLine.StartY + exitLine.EndY) / 2), "and it is on the north shore");
        run.Region.Cancel("test");
    }

    private static void AlexanderIsWalkedThroughItsNativeSteps(Fixture fixture)
    {
        var run = fixture.NewRun();
        var springScreen = run.Arrive(fixture.Graph.Nodes.First(node => node.FieldId == GreatGlacierRegion.HotSpringField));
        Contains(run.Select(springScreen, GreatGlacierTreasure.Alexander), "Alexander Materia: first the hot spring.",
            "Alexander before the spring: the spring first");
        Equal("object:680:20:Named", run.LegExitId(), "the leg is the spring's own LINE");

        // "Let's go on": the spring's lines switch off for this visit and nothing is set.
        run.SpringLineOff = true;
        Contains(run.Tick(springScreen), "The hot spring was left untouched",
            "the route pauses and says why, rather than walking back and forth");
        Equal(true, run.Region.Objective is { Paused: true }, "paused, not dropped");
        Equal(true, run.Controller.TryConsumeAutoWalkStopRequest(), "and the walk stops");
        run.SpringLineOff = false;
        run.Region.Cancel("test");

        // "Touch it": bank 1 byte 199 bit 0.
        _ = run.Select(springScreen, GreatGlacierTreasure.Alexander);
        run.Bank[199] = 0x01;
        Contains(run.Tick(springScreen), "Alexander Materia: next,", "spring touched: on toward Snow's cave");
        var cave = run.Position(fixture.Graph.Nodes.First(node => node.FieldId == GreatGlacierRegion.SnowField));
        Contains(run.Tick(cave), "Alexander Materia: first Snow.", "in the cave: Snow, who the native script lets fight now");
        Equal("npc:684:13", run.LegExitId(), "Snow herself");
        run.Bank[199] = 0x05;
        Contains(run.Tick(cave), "Alexander Materia.", "Snow beaten: the materia, now shown");
        Equal("object:684:14:Item", run.LegExitId(), "the materia model");
        run.Bank[199] = 0x15;
        Contains(run.Tick(cave), "Alexander Materia collected. Navigation off.", "picked up");
    }

    private static void AFailureStopsOnceWithAReason(Fixture fixture)
    {
        var run = fixture.NewRun();
        var at = run.Arrive(fixture.CabinArrival);
        run.HideExits = true;
        Contains(run.Select(at, GreatGlacierTreasure.Potion), "Route unavailable to Potion: no native way on from here reaches it",
            "chosen where no way on is offered: says why at once");
        Equal(false, run.Region.HasObjective, "and nothing is left running");

        // A leg that finds nothing on arrival waits for the screen's exits to be offered, then
        // gives up once, with the reason - never a silent retry loop.
        run.HideExits = false;
        _ = run.Select(at, GreatGlacierTreasure.Potion);
        run.HideExits = true;
        var outside = run.Position(fixture.Graph.Nodes.First(node => node.FieldId == 686 && node.X == -28));
        var t = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var said = new List<string?>();
        for (var index = 0; index < 20; index++)
        {
            said.Add(run.Tick(outside, observedAt: t.AddSeconds(index)));
        }

        Equal(1, said.Count(speech => speech is not null), $"one message: {string.Join(" | ", said.Where(s => s is not null))}");
        Equal(true, said.IndexOf(said.First(speech => speech is not null)) >= 5, "only after the exits have had time to be offered");
        Contains(said.First(speech => speech is not null), "Route unavailable to Potion", "with the reason");
        Equal(false, run.Region.HasObjective, "and the route ends");
        Equal(true, run.Controller.TryConsumeAutoWalkStopRequest(), "and any walk stops");
    }

    /// <summary>
    /// The seam every host reports: the module, each frame. Going from a field to the world map
    /// retires the field leg (the hosts only suspend the field controller, as a battle must keep
    /// its route), and coming back retires the snowfield leg; the treasure and the wish to be
    /// walked carry on. Cancelled on the snowfield, nothing of the old field leg is revived on
    /// the way back.
    /// </summary>
    private static void FieldAndWorldLegsAreRetiredAtTheDomainSeam(Fixture fixture)
    {
        // A battle is not a domain change: the leg is kept.
        var run = fixture.NewRun();
        var cabin = run.Arrive(fixture.CabinArrival);
        _ = run.Select(cabin, GreatGlacierTreasure.Potion);
        var leg = run.LegExitId();
        run.Region.ObserveModule(2);
        run.Region.ObserveModule(FieldPositionReader.FieldModule);
        _ = run.Tick(cabin);
        Equal(true, run.Controller.BeaconEnabled && run.LegExitId() == leg, "after a battle the same field leg is walked");

        // To the snowfield, walked; cancelled there with B.
        run.Region.NoteAutoWalk(true);
        run.Region.ObserveModule(WorldMapStateReader.WorldModule);
        Equal(false, run.Controller.TryResolveAutomaticInput(cabin, new FieldNavigationControlTransform(0), 0, out _),
            "the field leg is retired as soon as the world map is entered, and drives nothing");
        Equal(false, run.Controller.BeaconEnabled, "its beacon is off");
        var world = run.World.Observe(fixture.WorldState(60), autoWalk: false);
        Equal(true, world is { StartAutoWalk: true }, "the walk carries on into the snowfield leg");
        Contains(run.World.Toggle(fixture.WorldState(60))?.Speech, "Navigation off.", "B on the snowfield");
        Equal(false, run.Region.HasObjective, "cancels the treasure");
        run.Region.ObserveModule(FieldPositionReader.FieldModule);
        Equal<string?>(null, run.Tick(cabin), "back in the cabin, nothing of the old leg speaks");
        Equal(false, run.Controller.BeaconEnabled, "or is revived");
        Equal(false, run.Controller.TryResolveAutomaticInput(cabin, new FieldNavigationControlTransform(0), 0, out _), "or walked");

        // Kept: into a field, the snowfield leg is retired and the field goes on.
        _ = run.Select(cabin, GreatGlacierTreasure.All);
        run.Region.ObserveModule(WorldMapStateReader.WorldModule);
        _ = run.World.Observe(fixture.WorldState(60), autoWalk: false);
        Equal(true, run.World.BeaconEnabled, "the cave leg");
        run.Region.ObserveModule(FieldPositionReader.FieldModule);
        Equal(false, run.World.TryResolveAutomaticInput(fixture.WorldState(60)), "entering a field retires the snowfield leg: it drives nothing");
        Equal(false, run.World.BeaconEnabled, "and is off, without the host resetting it");
        Equal(true, run.Region.HasObjective, "the treasure is kept");
        run.Region.Cancel("test");
    }

    /// <summary>
    /// The snowfield's exits are terrain scripts: the party stands on the trigger for a few
    /// samples before the game's MAPJUMP. Those samples keep the treasure and start nothing.
    /// </summary>
    private static void TheSnowfieldEntranceHoldsUntilTheGameEntersTheField(Fixture fixture)
    {
        var run = fixture.NewRun();
        run.Region.Start(GreatGlacierTreasure.All, autoWalk: true);
        run.Region.ObserveModule(WorldMapStateReader.WorldModule);
        Contains(run.World.Observe(fixture.WorldState(60), autoWalk: false)?.Speech, "first to Cave in the middle of the snowfield", "the cave leg");
        Contains(run.World.ArriveAtCurrentLeg()?.Speech, "Continuing to All Materia", "on the cave's trigger");
        for (var sample = 0; sample < 10; sample++)
        {
            Equal<string?>(null, run.World.ObserveLast(autoWalk: false)?.Speech, $"entrance sample {sample} says nothing");
            Equal(true, run.Region.HasObjective, $"entrance sample {sample} keeps the treasure");
            Equal(false, run.World.BeaconEnabled, $"entrance sample {sample} starts nothing: movement is held for the game's entry");
        }

        run.Region.ObserveModule(FieldPositionReader.FieldModule);
        var cave = run.Position(fixture.Graph.Nodes.First(node => node.FieldId == 682));
        Contains(run.Tick(cave), "All Materia.", "the field the entrance leads into takes the route on");
        run.Region.Cancel("test");
    }

    /// <summary>
    /// The Glacier Map opened in the middle of an exit leg: the game has the party while it
    /// shows, maybe for longer than the exit wait. Back on the same screen the route goes on.
    /// </summary>
    private static void AMapOpenedMidLegResumesTheRoute(Fixture fixture)
    {
        var run = fixture.NewRun();
        var t = new DateTime(2026, 9, 29, 13, 0, 0, DateTimeKind.Utc);
        var cabin = run.Arrive(fixture.CabinArrival);
        _ = run.Select(cabin, GreatGlacierTreasure.Potion);
        var map = run.Position(GreatGlacierRegion.MapScreenField, 0, 0, 0);
        Equal<string?>(null, run.Tick(map, suppressed: true, observedAt: t), "the map opens under the game's control");
        // hyoumap turns control off (UC 1) for as long as it shows: every sample is suppressed.
        Equal<string?>(null, run.Tick(map, suppressed: true, observedAt: t.AddSeconds(6)), "held open past the exit wait");
        cabin = run.Position(fixture.CabinArrival);
        Contains(run.Tick(cabin, observedAt: t.AddSeconds(7)), "Potion: next,", "back from the map the route goes on");
        Equal(true, run.Region.HasObjective && run.Controller.BeaconEnabled, "walked again from where the party stands");

        // A new screen reached while the game still has the party hands over once it lets go.
        var outside = run.Position(fixture.Graph.Nodes.First(node => node.FieldId == 686 && node.X == -28));
        Equal<string?>(null, run.Tick(outside, suppressed: true, observedAt: t.AddSeconds(8)), "arrival while suppressed");
        Contains(run.Tick(outside, observedAt: t.AddSeconds(15)), "Potion: next,", "handed over once usable, however long it took");
        run.Region.Cancel("test");
    }

    /// <summary>
    /// The goal's own room, in the right passage state, but the game not offering the goal
    /// (Snow not yet standing in hyou13_2): every exit only leads back into this room, so the
    /// route waits within the bounded grace - and resumes when the goal appears, or ends once
    /// with the reason, never leaving and re-entering. A goal that is offered but cannot be
    /// walked to on this part of the walkmesh, and Added Cut in move_d at the wrong byte, still
    /// take their native detours.
    /// </summary>
    private static void AGoalNotYetOfferedInItsOwnRoomIsWaitedForNotCircled(Fixture fixture)
    {
        var snowCave = fixture.Graph.Nodes.First(node => node.FieldId == GreatGlacierRegion.SnowField);

        // Transient: missing, then offered.
        var run = fixture.NewRun();
        run.Bank[199] = 0x01;
        run.HideSnow = true;
        var t = DateTime.UtcNow;
        var cave = run.Arrive(snowCave);
        Contains(run.Select(cave, GreatGlacierTreasure.Alexander), "Snow is not here to talk to; waiting for it here",
            "chosen before Snow stands there: held, with the reason");
        Equal(false, run.Controller.BeaconEnabled, "nothing is walked while waiting");
        Equal(true, run.Controller.IsHoldingForNativeBoundary, "a held destination B can cancel");
        Equal<string?>(null, run.Tick(cave, observedAt: t.AddSeconds(1)), "still waiting, silently");
        Equal(string.Empty, run.LegExitId(), "no exit is taken out of the room");
        run.HideSnow = false;
        Contains(run.Tick(cave, observedAt: t.AddSeconds(2)), "Alexander Materia: first Snow.", "Snow appears: the route resumes");
        Equal("npc:684:13", run.LegExitId(), "to Snow herself");
        run.Region.Cancel("test");

        // Sustained: one failure after the grace, no movement, no circling.
        run = fixture.NewRun();
        run.Bank[199] = 0x01;
        run.HideSnow = true;
        cave = run.Arrive(snowCave);
        _ = run.Select(cave, GreatGlacierTreasure.Alexander);
        var said = new List<string?>();
        var legs = new HashSet<string>();
        t = DateTime.UtcNow;
        for (var second = 1; second <= 15; second++)
        {
            said.Add(run.Tick(cave, observedAt: t.AddSeconds(second)));
            legs.Add(run.LegExitId());
            Equal(false, run.Controller.BeaconEnabled, $"second {second}: nothing is walked");
        }

        Equal(1, said.Count(text => text is not null), $"one message: {string.Join(" | ", said.Where(text => text is not null))}");
        Contains(said.First(text => text is not null), "Route unavailable to Alexander Materia: Snow is not here to talk to", "with the reason");
        Equal(false, legs.Any(id => id.StartsWith("script-exit:", StringComparison.Ordinal)), "no exit out of the room was ever taken");
        Equal(false, run.Region.HasObjective, "and the route ends");

        // Offered but not walkable from here: the native detour is still planned.
        var region = new GreatGlacierRegionalNavigator(_ => 0x01);
        region.UseGraph(fixture.Graph);
        var goal = region.CurrentGoal(GreatGlacierTreasure.Alexander, out _)!.Value;
        var position = fixture.Position(snowCave.FieldId, snowCave.X, snowCave.Y, snowCave.Triangle);
        var snow = new FieldNavigationTarget(684, FieldNavigationCategory.Npcs, "Snow", 0, 0, 0, "npc:684:13");
        var detour = region.PlanFieldLeg(position, goal, null, fixture.Exits(684), [], [snow],
            target => target.StableId == snow.StableId ? null : 100d, out var failure);
        Equal(GreatGlacierLegKind.Exit, detour?.Kind, $"a visible goal the walkmesh does not join is reached round by an exit ({failure})");
        Equal(false, region.LastPlanAwaitsGoal, "that is a route, not a wait");

        // Added Cut, in move_d at a byte where it is not placed: another arrival is needed.
        var wrongByte = fixture.Graph.Nodes.First(node => node.FieldId == 675 && node.State != 75 &&
            double.IsFinite(fixture.Graph.CostToGoal(Goal(GreatGlacierTreasure.AddedCut), node)));
        run = fixture.NewRun();
        var passage = run.Arrive(wrongByte);
        Contains(run.Select(passage, GreatGlacierTreasure.AddedCut), "Added Cut Materia: next,",
            "move_d at the wrong byte: on through the passage, not waiting for a materia that is not placed here");
        run.Region.Cancel("test");
    }

    /// <summary>
    /// A host that could not load WM3.MAP or the world archive says so; a route asked for then
    /// ends at once with the reason. Without any word, a route waits at most the preparation limit.
    /// </summary>
    private static void MissingInputsEndTheRouteWithAReasonInsteadOfWaiting(Fixture fixture)
    {
        var bank = new byte[256];
        var unavailable = new GreatGlacierRegionalNavigator(address => bank[address]);
        unavailable.MarkUnavailable("WM3.MAP could not be loaded");
        var run = fixture.NewRun(unavailable);
        var cabin = run.Arrive(fixture.CabinArrival);
        Contains(run.Select(cabin, GreatGlacierTreasure.Potion), "Route unavailable to Potion: the Glacier route table could not be built (WM3.MAP could not be loaded)",
            "a missing input is said at once");
        Equal(false, unavailable.HasObjective, "and nothing waits");

        var waiting = new GreatGlacierRegionalNavigator(address => bank[address]);
        run = fixture.NewRun(waiting);
        cabin = run.Arrive(fixture.CabinArrival);
        // Selection is stamped with the real clock (HandleAction), as the hosts' samples are.
        var t = DateTime.UtcNow;
        Contains(run.Select(cabin, GreatGlacierTreasure.Potion), "Preparing the Great Glacier routes", "a table still building is waited for");
        var said = new List<string?>();
        for (var second = 1; second <= 40; second++)
        {
            said.Add(run.Tick(cabin, observedAt: t.AddSeconds(second)));
        }

        Equal(1, said.Count(text => text is not null), $"one message after waiting: {string.Join(" | ", said.Where(text => text is not null))}");
        Contains(said.First(text => text is not null), "could not be prepared", "the preparation limit ends the route with the reason");
        Equal(false, waiting.HasObjective, "and nothing is left waiting");
    }

    // ------------------------------------------------------------------ helpers

    private static GreatGlacierGoalPoint Goal(GreatGlacierTreasure treasure, GreatGlacierGoalStage stage = GreatGlacierGoalStage.Treasure) =>
        GreatGlacierRegion.AllGoalPoints.First(goal => goal.Treasure == treasure && goal.Stage == stage);

    /// <summary>The route a fewest-exits search (FieldCrossFieldApproachResolver's rule) would take.</summary>
    private static IReadOnlyList<GreatGlacierRegionGraph.Edge> FewestExits(GreatGlacierRegionGraph graph, GreatGlacierGoalPoint goal, GreatGlacierRegionGraph.Node start)
    {
        var previous = new Dictionary<int, GreatGlacierRegionGraph.Edge>();
        var seen = new HashSet<int> { start.Index };
        var queue = new Queue<int>([start.Index]);
        while (queue.TryDequeue(out var current))
        {
            if (graph.NextEdge(goal, graph.Nodes[current]) is null && double.IsFinite(graph.CostToGoal(goal, graph.Nodes[current])))
            {
                var route = new List<GreatGlacierRegionGraph.Edge>();
                for (var at = current; previous.TryGetValue(at, out var edge); at = edge.From) route.Add(edge);
                route.Reverse();
                return route;
            }

            foreach (var edge in graph.Edges.Where(edge => edge.From == current).OrderBy(edge => edge.Ticks))
            {
                if (seen.Add(edge.To))
                {
                    previous[edge.To] = edge;
                    queue.Enqueue(edge.To);
                }
            }
        }

        return [];
    }

    private static void Contains(string? text, string expected, string message)
    {
        if (text is null || !text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Great Glacier regional items: {message}. Expected '{expected}' in '{text}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Great Glacier regional items: {message}. Expected {expected}, got {actual}.");
        }
    }

    /// <summary>The installed data every test here reads, loaded once.</summary>
    private sealed class Fixture
    {
        private readonly Dictionary<int, byte[]> raw = new();
        private double warmLegMilliseconds = double.NaN;

        public Fixture(string root)
        {
            Root = root;
            Catalog = new FieldScriptNavigationCatalog(root);
            var flevel = new FlevelDataSource(root);
            foreach (var field in Enumerable.Range(658, 31).Where(GreatGlacierRegion.IsRegionField))
            {
                Equal(true, flevel.TryReadField(field, out var encoded), $"field {field} is installed");
                raw[field] = Ff7LzsDecoder.DecodeFieldFile(encoded);
            }

            var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT")
                ?? throw new InvalidOperationException("The regional items tests need FF7_ACCESSIBILITY_SOURCE_ROOT.");
            Snowfield = WorldMapDataLoader.Load(Path.Combine(root, "data", "wm", "WM3.MAP"), 3, 0);
            SnowfieldCatalog = WorldMapTargetCatalog.Load(Snowfield,
                Path.Combine(source, "external", "kujata", "field-id-to-world-map-coords.json"),
                Path.Combine(source, "external", "kujata", "wm-field-menu-names.txt"),
                Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
            WorldArchive = Path.Combine(root, "data", "wm", "world_us.lgp");
            Graph = GreatGlacierRegionalNavigator.BuildGraph(root, null, Snowfield, SnowfieldCatalog.Locations,
                SnowfieldCatalog.EntranceTriangleIds, WorldArchive);
            CabinArrival = Graph.Nodes.First(node => node.FieldId == GreatGlacierRegion.CabinFrontRoom);
        }

        public string Root { get; }

        public FieldScriptNavigationCatalog Catalog { get; }

        public WorldMapData Snowfield { get; }

        public WorldMapTargetCatalog SnowfieldCatalog { get; }

        public string WorldArchive { get; }

        public GreatGlacierRegionGraph Graph { get; }

        public GreatGlacierRegionGraph.Node CabinArrival { get; }

        public double WarmLegMilliseconds => warmLegMilliseconds;

        public void NoteLegTime(double milliseconds)
        {
            warmLegMilliseconds = double.IsNaN(warmLegMilliseconds) ? milliseconds : Math.Max(warmLegMilliseconds, milliseconds);
        }

        public FieldWalkmeshReader MeshReader(Func<int> field) => new(
            address =>
            {
                var bytes = raw[field()];
                const int basePointer = 0x03000000;
                return address == FieldWalkmeshReader.AddressFieldDataPtr
                    ? basePointer
                    : address - basePointer is var offset && offset >= 0 && offset + 4 <= bytes.Length ? BitConverter.ToInt32(bytes, offset) : 0;
            },
            address =>
            {
                var bytes = raw[field()];
                const int basePointer = 0x03000000;
                return address - basePointer is var offset && offset >= 0 && offset + 2 <= bytes.Length ? BitConverter.ToInt16(bytes, offset) : (short)0;
            });

        public FieldWalkmesh Mesh(int field) =>
            MeshReader(() => field).Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, field, 0, 0, 0, 0, 0, 0)).Walkmesh!;

        public FieldWalkmeshRoutePlanner LivePlanner(int field, IReadOnlySet<int> locked)
        {
            var bits = new byte[FieldBoundaryStateReader.BoundaryByteCount];
            foreach (var triangle in locked) bits[triangle >> 3] |= (byte)(1 << (triangle & 7));
            const int global = 0x04000000;
            var bitsAt = global + FieldBoundaryStateReader.BoundaryBitsOffset;
            var boundary = new FieldBoundaryStateReader(
                address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? global : 0,
                address => address == FieldPositionReader.AddressCurrentModule ? (byte)FieldPositionReader.FieldModule
                    : address == FieldPositionReader.AddressFieldId ? (byte)(field & 0xFF)
                    : address == FieldPositionReader.AddressFieldId + 1 ? (byte)(field >> 8)
                    : address >= bitsAt && address < bitsAt + bits.Length ? bits[address - bitsAt] : (byte)0,
                (_, _) => true);
            return new FieldWalkmeshRoutePlanner(MeshReader(() => field), boundary, transitionProvider: f => Catalog.ReadField(f).Transitions);
        }

        public FieldPositionSnapshot Position(int field, int x, int y, int triangle)
        {
            if (!raw.ContainsKey(field))
            {
                return new FieldPositionSnapshot(FieldPositionReader.FieldModule, field, 0, x, y, 0, (ushort)triangle, 0);
            }

            var mesh = Mesh(field);
            var z = triangle < mesh.Triangles.Count ? FieldCrossFieldApproachResolver.SurfaceZ(mesh.Triangles[triangle], x, y) : 0;
            return new FieldPositionSnapshot(FieldPositionReader.FieldModule, field, 0, x, y, z, (ushort)triangle, 0);
        }

        public IReadOnlyList<FieldNavigationTarget> Exits(int field)
        {
            if (!raw.ContainsKey(field))
            {
                return [];
            }

            return Catalog.ReadField(field).Exits
                .Concat(GreatGlacierRegionGraph.ReadGateways(field, raw[field]).Select(gateway => gateway.ToTarget(field)))
                .ToArray();
        }

        public WorldMapStateSnapshot WorldState(int location)
        {
            var entry = Graph.SnowfieldPlacements[location];
            var planner = new WorldMapRoutePlanner(Snowfield) { EntranceTriangleIds = SnowfieldCatalog.EntranceTriangleIds };
            return GreatGlacierRegionGraph.WorldState(Snowfield, planner, entry.X, entry.Z)!.Value with { GameMoment = 677 };
        }

        public Session NewRun() => new(this);

        public Session NewRun(GreatGlacierRegionalNavigator region) => new(this, region);
    }

    /// <summary>One player's session: a field controller and a snowfield controller sharing one region navigator.</summary>
    private sealed class Session
    {
        private readonly Fixture fixture;
        private int field;
        private DateTime clock = new(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc);

        public Session(Fixture fixture, GreatGlacierRegionalNavigator? region = null)
        {
            this.fixture = fixture;
            Region = region ?? new GreatGlacierRegionalNavigator(address => Bank[address]);
            Region.ReadCorridorState = () => GreatGlacierRegion.IsPassage(field) ? Corridor : null;
            Region.ReadIceFloes = () => field == GreatGlacierRegion.LakeField
                ? new GreatGlacierIceFloeState(Crossing ? 23 : 26, Crossing ? 23 : 26, false, new byte[25])
                : null;
            if (region is null)
            {
                Region.UseGraph(fixture.Graph);
            }

            // Hosts report the module every frame; a session starts in a field.
            Region.ObserveModule(FieldPositionReader.FieldModule);
            var planner = new FieldWalkmeshRoutePlanner(fixture.MeshReader(() => field),
                transitionProvider: f => fixture.Catalog.ReadField(f).Transitions);
            var source = new FieldNavigationTargetSource(
                [],
                objectTargetProvider: ReadObjects,
                exitTargetProvider: position => HideExits ? [] : fixture.Exits(position.FieldId),
                npcTargetProvider: ReadNpcs);
            Controller = new FieldNavigationController(source, planner) { GlacierRegion = Region };
            World = new WorldRun(fixture, Region);
        }

        public byte[] Bank { get; } = new byte[256];

        public int Corridor { get; set; }

        public bool Crossing { get; set; }

        public bool HideExits { get; set; }

        public bool SpringLineOff { get; set; }

        public bool HideSnow { get; set; }

        public GreatGlacierRegionalNavigator Region { get; }

        public FieldNavigationController Controller { get; }

        public WorldRun World { get; }

        public FieldPositionSnapshot Position(int fieldId, int x, int y, int triangle)
        {
            field = fieldId;
            return fixture.Position(fieldId, x, y, triangle);
        }

        public FieldPositionSnapshot Position(GreatGlacierRegionGraph.Node node)
        {
            Corridor = node.State;
            return Position(node.FieldId, node.X, node.Y, node.Triangle);
        }

        /// <summary>The party placed at a native arrival, seen once.</summary>
        public FieldPositionSnapshot Arrive(GreatGlacierRegionGraph.Node node)
        {
            var position = Position(node);
            _ = Tick(position);
            return position;
        }

        public string? Tick(FieldPositionSnapshot position, bool suppressed = false, DateTime observedAt = default)
        {
            field = position.FieldId;
            clock = observedAt == default ? clock.AddMilliseconds(300) : observedAt;
            var watch = Stopwatch.StartNew();
            var result = Controller.UpdateLiveTracking(position, default, new FieldNavigationControlTransform(0), suppressed,
                observedAt: clock)?.Speech;
            if (result is not null && result.Contains("next,", StringComparison.Ordinal))
            {
                fixture.NoteLegTime(watch.Elapsed.TotalMilliseconds);
            }

            return result;
        }

        public string? Select(FieldPositionSnapshot position, GreatGlacierTreasure treasure)
        {
            field = position.FieldId;
            var controlTransform = new FieldNavigationControlTransform(0);
            for (var guard = 0; guard < 8 && Controller.CurrentCategory != FieldNavigationCategory.Objects; guard++)
            {
                Controller.HandleAction(FieldNavigationAction.NextCategory, position, controlTransform);
            }

            var id = GreatGlacierRegionalNavigator.RowId(treasure);
            for (var guard = 0; guard < 40; guard++)
            {
                var described = Controller.HandleAction(FieldNavigationAction.RepeatTarget, position, controlTransform)?.Speech ?? string.Empty;
                if (described.Contains(Region.NameOf(treasure), StringComparison.Ordinal) && described.Contains("elsewhere", StringComparison.Ordinal))
                {
                    break;
                }

                Controller.HandleAction(FieldNavigationAction.NextTarget, position, controlTransform);
            }

            _ = id;
            return Controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, controlTransform)?.Speech;
        }

        public string LegExitId() => Controller.CurrentGlacierLegTargetId;

        public IReadOnlyList<FieldNavigationTarget> Objects(FieldPositionSnapshot position)
        {
            field = position.FieldId;
            var source = new FieldNavigationTargetSource([], objectTargetProvider: ReadObjects) { ObjectRows = Region.AppendFieldRows };
            return source.GetTargets(position, FieldNavigationCategory.Objects);
        }

        /// <summary>The live Objects of a screen: its treasures still on the ground, the spring and the crossing starts.</summary>
        private IReadOnlyList<FieldNavigationTarget> ReadObjects(FieldPositionSnapshot position)
        {
            var targets = new List<FieldNavigationTarget>();
            var definitions = FieldNavigationObjectCatalog.CreateAllFields().Where(definition => definition.FieldId == position.FieldId);
            foreach (var definition in definitions)
            {
                var treasure = GreatGlacierRegion.Treasures.FirstOrDefault(candidate =>
                    candidate.FieldId == definition.FieldId && candidate.EntityId == definition.EntityId);
                if (treasure.FieldId != 0)
                {
                    if ((Bank[treasure.FlagAddress] & treasure.FlagMask) != 0 ||
                        (treasure.Treasure == GreatGlacierTreasure.AddedCut && Corridor != 75) ||
                        (treasure.Treasure == GreatGlacierTreasure.Alexander && (Bank[199] & 0x04) == 0))
                    {
                        continue;
                    }

                    var goal = GreatGlacierRegionGraph.GoalTarget(
                        new GreatGlacierGoalPoint(treasure.Treasure, GreatGlacierGoalStage.Treasure, treasure.FieldId, treasure.EntityId),
                        fixture.Catalog.ReadAllScriptOpcodes(treasure.FieldId), FieldNavigationObjectCatalog.CreateAllFields())!.Value;
                    targets.Add(goal with { Label = Region.NameOf(treasure.Treasure), StableId = $"object:{definition.FieldId}:{definition.EntityId}:Item" });
                    continue;
                }

                if (definition.TargetKind == FieldNavigationObjectTargetKind.Line &&
                    !(definition.FieldId == GreatGlacierRegion.HotSpringField && ((Bank[199] & 0x01) != 0 || SpringLineOff)))
                {
                    targets.Add(new FieldNavigationTarget(definition.FieldId, FieldNavigationCategory.Objects, definition.Label ?? "line",
                        definition.StaticX, definition.StaticY, definition.StaticZ, $"object:{definition.FieldId}:{definition.EntityId}:Named",
                        InteractionRadius: 48));
                }
            }

            return targets;
        }

        private IReadOnlyList<FieldNavigationTarget> ReadNpcs(FieldPositionSnapshot position)
        {
            if (HideSnow || position.FieldId != GreatGlacierRegion.SnowField || (Bank[199] & 0x01) == 0 || (Bank[199] & 0x04) != 0)
            {
                return [];
            }

            var snow = GreatGlacierRegionGraph.GoalTarget(
                new GreatGlacierGoalPoint(GreatGlacierTreasure.Alexander, GreatGlacierGoalStage.Snow, 684, 13),
                fixture.Catalog.ReadAllScriptOpcodes(684), FieldNavigationObjectCatalog.CreateAllFields())!.Value;
            return [snow with { Category = FieldNavigationCategory.Npcs, Label = "Snow", StableId = "npc:684:13" }];
        }
    }

    /// <summary>The snowfield's world-map controller on the installed WM3.MAP.</summary>
    private sealed class WorldRun
    {
        private readonly WorldMapNavigationController controller;
        private readonly WorldMapRoutePlanner planner;
        private readonly WorldMapTargetCatalog catalog;
        private WorldMapStateSnapshot last;

        public WorldRun(Fixture fixture, GreatGlacierRegionalNavigator region)
        {
            catalog = fixture.SnowfieldCatalog;
            planner = new WorldMapRoutePlanner(fixture.Snowfield) { EntranceTriangleIds = catalog.EntranceTriangleIds };
            controller = new WorldMapNavigationController(fixture.Snowfield, planner, (state, category) => catalog.ReadTargets(category, state, []))
            {
                GlacierRegion = region
            };
        }

        public WorldMapNavigationOutput? Observe(WorldMapStateSnapshot state, bool autoWalk)
        {
            last = state;
            return controller.Observe(state, DateTime.UtcNow, autoWalk);
        }

        public WorldMapNavigationOutput? ObserveLast(bool autoWalk) => controller.Observe(last, DateTime.UtcNow, autoWalk);

        public bool BeaconEnabled => controller.BeaconEnabled;

        public WorldMapNavigationOutput? Toggle(WorldMapStateSnapshot state) =>
            controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, DateTime.UtcNow);

        public bool TryResolveAutomaticInput(WorldMapStateSnapshot state) => controller.TryResolveAutomaticInput(state, out _);

        public IReadOnlyList<WorldMapNavigationTarget> ReadRows(WorldMapStateSnapshot state)
        {
            var region = controller.GlacierRegion!;
            return region.ReadWorldRows(state, catalog.Locations,
                location => planner.TryBuildRoute(state, location, out var route) ? route.TotalDistance : null);
        }

        /// <summary>Steps the party onto the active leg's trigger.</summary>
        public WorldMapNavigationOutput? ArriveAtCurrentLeg()
        {
            var probe = controller.Probe;
            var route = probe.Route!;
            var triangle = catalog.Snowfield(route.TargetTriangleId);
            var arrival = last with
            {
                X = triangle.Centroid.X,
                Y = triangle.Centroid.Y,
                Z = triangle.Centroid.Z,
                TerrainId = triangle.TerrainId,
                TerrainScriptId = triangle.TerrainScriptId
            };
            last = arrival;
            return controller.Observe(arrival, DateTime.UtcNow, automaticWalkActive: true);
        }
    }

    private static WorldMapTriangle Snowfield(this WorldMapTargetCatalog catalog, int triangle) =>
        shared!.Snowfield.Triangles[triangle];
}
