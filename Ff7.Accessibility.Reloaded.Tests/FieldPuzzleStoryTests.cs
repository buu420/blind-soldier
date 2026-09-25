using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The three field puzzles the Steam 2026 player was left in on 2026-09-24: Godo's Pagoda
/// (586), the Cait Sith chase through the Gold Saucer (598..600) and the Temple of the
/// Ancients clock room (607). Every expectation is derived from the installed scripts and
/// walkmeshes where the data root is available; the state cases run everywhere.
///
/// <para>The cases are also compiled, unchanged, into the failing-first harness, so they
/// use only the public catalog, reader and planner surface that 0.6.9 already had.</para>
/// </summary>
internal static class FieldPuzzleStoryTests
{
    internal static void Run(Func<int, FieldWalkmeshReader>? createWalkmeshReader = null)
    {
        foreach (var (_, check) in Cases(createWalkmeshReader))
        {
            check();
        }
    }

    internal static IEnumerable<(string Name, Action Check)> Cases(Func<int, FieldWalkmeshReader>? createWalkmeshReader)
    {
        yield return ("clock readout names the short hand's bridge too", ClockReadoutNamesBothHandsBridges);
        yield return ("clock readout says where the party stands", ClockReadoutSaysWhereThePartyStands);
        yield return ("clock Story asks for the way the party actually has", ClockStoryAsksForTheWayThePartyHas);
        yield return ("pagoda Story: each floor's opponent, then the stairs", PagodaStoryOffersEachFloorsOpponentThenTheStairs);
        yield return ("a crossing target removed at its line was reached", CrossingTargetRemovedAtItsLineWasReached);
        yield return ("an ordinary crossing removed nearby was not reached", OrdinaryCrossingRemovedNearbyWasNotReached);
        yield return ("chase Story follows the trail Cait Sith is seen to take", ChaseStoryFollowsTheTrailCaitSithIsSeenToTake);
        if (createWalkmeshReader is null || DataRoot is null)
        {
            yield break;
        }

        yield return ("clock native evidence", ClockNativeEvidence);
        yield return ("clock places are the walkmesh's own parts", () => ClockPlacesAreTheWalkmeshsOwnParts(createWalkmeshReader));
        yield return ("clock Story is a route exactly when the bridges make one", () => ClockStoryIsARouteExactlyWhenTheBridgesMakeOne(createWalkmeshReader));
        yield return ("clock second hand is not a static exit", () => ClockSecondHandIsNotAStaticExit(createWalkmeshReader));
        yield return ("pagoda native evidence", PagodaNativeEvidence);
        yield return ("pagoda routes follow the floor's own locks", () => PagodaRoutesFollowTheFloorsOwnLocks(createWalkmeshReader));
        yield return ("pagoda stairs are Exits on their own floors", () => PagodaStairsAreExitsOnTheirOwnFloors(createWalkmeshReader));
        yield return ("other same-field resets stay out of Exits", OrdinarySameFieldResetsStayOutOfExits);
        yield return ("chase native evidence", ChaseNativeEvidence);
        yield return ("chase routes from each native arrival", () => ChaseRoutesFromEachNativeArrival(createWalkmeshReader));
    }

    private static string? DataRoot => Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") is { Length: > 0 } root
        ? root
        : null;

    // --- 607 kuro_4, the clock -----------------------------------------------------------------

    private static readonly FieldNavigationTriggerLine DoorSix = new(-87, -716, 0, 85, -716, 0);
    private static readonly FieldNavigationTriggerLine DoorTwelve = new(-82, 700, 0, 81, 708, 0);
    private const string CrossToSix = "Cross to doorway six";
    private const string SetTheHands = "Set the clock hands to make a way to doorway six";

    /// <summary>
    /// Where each way into the clock room puts the party, from the installed map jumps and
    /// gateways (the native evidence case re-reads them), and the doorway whose side that is.
    /// </summary>
    private static readonly (string From, int X, int Y, ushort Triangle, int Door)[] ClockArrivals =
    [
        ("606 gateway1", -555, 308, 26, 10),
        ("604 mapjump", 320, -535, 52, 5),
        ("614, doorway two", 527, 299, 70, 2),
        ("614, doorway nine", -598, 13, 30, 9),
        ("614, doorway eleven", -290, 540, 22, 11),
        ("615, doorway one", 315, 544, 8, 1),
        ("615, doorway three", 631, -8, 64, 3),
        ("615, doorway four", 540, -326, 58, 4),
        ("615, doorway seven", -298, -551, 40, 7),
        ("615, doorway eight", -533, -332, 34, 8),
        ("610 gateway0", 7, -555, 47, 6),
    ];

    private static readonly string[] ClockNumeralNames =
        ["twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven"];

    /// <summary>What the clock says with the long hand on six and the short hand on ten, both bridges open.</summary>
    private const string SixAndTen =
        "Long hand at six, short hand at ten. The bridge to doorway six is open. The bridge to doorway ten is open.";

    private static FieldActivityModelReading ClockHand(int entityId, int bearing) =>
        new(entityId, FieldActivityReadStatus.Visible, new FieldActivityModelState(true, true, 0, 0, 0, 0, bearing));

    /// <summary>The clock at six and ten with the party on the given triangle.</summary>
    private static FieldActivityObservation ClockAt(int triangle) =>
        new(607, 0, 0, 0, triangle, true, 618, new FieldNavigationControlTransform(0), true,
            [ClockHand(21, 0), ClockHand(22, 170)], new Dictionary<int, FieldActivityWaitState>(), _ => true,
            locked => locked is not (85 or 48 or 76 or 10), 0);

    private static void ClockReadoutNamesBothHandsBridges()
    {
        var epoch = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        static FieldActivityModelReading Hand(int entityId, int bearing) => ClockHand(entityId, bearing);
        // Triangle 110 is on a part of the walkmesh no way in reaches, so these say only what
        // the hands do (ClockReadoutSaysWhereThePartyStands covers the place).
        static FieldActivityObservation Clock(Func<int, bool>? locked, params FieldActivityModelReading[] hands) =>
            new(607, 0, 0, 0, 110, true, 618, new FieldNavigationControlTransform(0), true, hands,
                new Dictionary<int, FieldActivityWaitState>(), _ => true, locked, 0);

        // The first visit as the Init leaves it: long hand on two (bearing 86), short hand on
        // ten (170), and only those two pairs unlocked by the hands' own scripts.
        var firstVisit = new FieldActivityReadout().Observe(
            Clock(triangle => triangle is not (79 or 0 or 76 or 10), Hand(21, 86), Hand(22, 170)), epoch).Speech;
        Equal(
            "Long hand at two, short hand at ten. The bridge to doorway two is open. The bridge to doorway ten is open.",
            firstVisit,
            "the short hand's bridge is said as well as the long hand's");
        Equal(
            "Long hand at six, short hand at ten. The bridge to doorway six is open. The bridge to doorway ten is not open.",
            new FieldActivityReadout().Observe(
                Clock(triangle => triangle is not (85 or 48), Hand(21, 0), Hand(22, 170)), epoch).Speech,
            "a short hand whose pair is still locked is not a bridge");
        Equal(
            "Long hand at six, short hand at six. The bridge to doorway six is open.",
            new FieldActivityReadout().Observe(Clock(_ => false, Hand(21, 0), Hand(22, 0)), epoch).Speech,
            "two hands on one hour are one bridge, said once");
        Equal(
            "Long hand at six, short hand between ten and eleven. The bridge to doorway six is open.",
            new FieldActivityReadout().Observe(Clock(_ => false, Hand(21, 0), Hand(22, 160)), epoch).Speech,
            "a turning short hand claims no bridge");

        // A new short-hand bridge is news even when the long hand has not moved.
        var readout = new FieldActivityReadout();
        _ = readout.Observe(Clock(_ => false, Hand(21, 0), Hand(22, 170)), epoch);
        Equal(
            "Long hand at six, short hand at nine. The bridge to doorway six is open. The bridge to doorway nine is open.",
            readout.Observe(Clock(_ => false, Hand(21, 0), Hand(22, 192)), epoch.AddSeconds(3)).Speech,
            "the short hand reaching another hour is spoken");
    }

    /// <summary>
    /// Which bridges are any use depends on where the party stands, so the readout and its
    /// repeat say that first: the doorway whose side the party is on, or the middle. Every way
    /// in lands on a doorway's side. A triangle that is neither is given no place rather than
    /// a guessed one, and walking from one place to another is said at the clock's own cadence.
    /// </summary>
    private static void ClockReadoutSaysWhereThePartyStands()
    {
        var epoch = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        foreach (var arrival in ClockArrivals)
        {
            var expected = $"You are by doorway {ClockNumeralNames[arrival.Door % 12]}. {SixAndTen}";
            Equal(expected, new FieldActivityReadout().Observe(ClockAt(arrival.Triangle), epoch).Speech, $"{arrival.From}: the readout");
            Equal(expected, new FieldActivityReadout().Describe(ClockAt(arrival.Triangle)), $"{arrival.From}: the repeat");
        }

        Equal($"You are in the middle of the clock. {SixAndTen}", new FieldActivityReadout().Describe(ClockAt(131)), "the middle");
        Equal($"You are in the middle of the clock. {SixAndTen}", new FieldActivityReadout().Describe(ClockAt(76)),
            "the inner end of the tenth bridge belongs to the middle");
        Equal($"You are by doorway ten. {SixAndTen}", new FieldActivityReadout().Describe(ClockAt(11)),
            "the rest of the tenth bridge belongs to its doorway");
        foreach (var unknown in new[] { 110, 125, 132, 500, -1 })
        {
            Equal(SixAndTen, new FieldActivityReadout().Describe(ClockAt(unknown)), $"triangle {unknown} is given no place");
        }

        var readout = new FieldActivityReadout();
        Equal($"You are by doorway ten. {SixAndTen}", readout.Observe(ClockAt(26), epoch).Speech, "in from the corridor");
        Equal(null, readout.Observe(ClockAt(131), epoch.AddSeconds(1)).Speech, "a new place waits for the cadence");
        Equal($"You are in the middle of the clock. {SixAndTen}", readout.Observe(ClockAt(131), epoch.AddSeconds(3)).Speech,
            "and is then said");
        Equal(null, readout.Observe(ClockAt(130), epoch.AddSeconds(6)).Speech, "walking about the middle is not news");
        Equal($"You are in the middle of the clock. {SixAndTen}", readout.Describe(ClockAt(130)), "though the repeat still says it");
    }

    private static void ClockStoryAsksForTheWayThePartyHas()
    {
        var memory = new PuzzleMemory(607);
        memory.SetGameMoment(618);
        var reader = memory.StoryReader();

        // The first visit, standing at the tenth doorway where the corridor comes in.
        memory.SetHands(2, 10);
        var first = reader.ReadTargets(Position(607, -555, 308, 26));
        Equal(1, first.Count, "the first visit offers one Story step");
        Equal(SetTheHands, first[0].Label, "before the clock is set there is no bridge to doorway six");
        var guidance = first[0].ManualNavigationGuidance ?? string.Empty;
        Equal(true, guidance.Contains("Move it myself", StringComparison.Ordinal) &&
                    guidance.Contains("Proceed now!", StringComparison.Ordinal),
            "the step names the Time Guardian's own options");

        memory.SetHands(6, 10);
        var turned = Single(reader, Position(607, -555, 308, 26), CrossToSix);
        Equal(DoorSix, turned.TriggerLine!.Value, "doorway six is gateway1's own line");
        memory.SetHands(10, 6);
        Equal(CrossToSix, Single(reader, Position(607, -555, 308, 26), null).Label, "either hand may be the one on six");

        // Turning the long hand back past twelve carries the short hand from ten to nine,
        // and the tenth doorway loses its bridge.
        memory.SetHands(6, 9);
        Equal(SetTheHands, Single(reader, Position(607, -555, 308, 26), null).Label,
            "a hand on six is not enough from a doorway whose own bridge is gone");

        // In the middle a hand on six is enough; on doorway six's own side nothing is needed.
        foreach (var (longHour, shortHour) in new[] { (6, 3), (3, 6), (12, 6), (0, 6), (6, 12) })
        {
            memory.SetHands(longHour, shortHour);
            Equal(CrossToSix, Single(reader, Position(607, 5, 10, 131), null).Label, $"middle, hands {longHour}/{shortHour}");
        }

        memory.SetHands(2, 10);
        Equal(SetTheHands, Single(reader, Position(607, 5, 10, 131), null).Label, "middle without a hand on six");
        Equal(CrossToSix, Single(reader, Position(607, 7, -555, 47), null).Label, "doorway six's own side needs no bridge");

        foreach (var arrival in ClockArrivals.Where(arrival => arrival.Door != 6))
        {
            var at = Position(607, arrival.X, arrival.Y, arrival.Triangle);
            foreach (var spelling in arrival.Door == 0 ? new[] { 0, 12 } : new[] { arrival.Door })
            {
                memory.SetHands(spelling, 6);
                Equal(CrossToSix, Single(reader, at, null).Label, $"{arrival.From}: long on the doorway, short on six");
                memory.SetHands(6, spelling);
                Equal(CrossToSix, Single(reader, at, null).Label, $"{arrival.From}: long on six, short on the doorway");
            }

            memory.SetHands(6, (arrival.Door + 1) % 12);
            Equal(SetTheHands, Single(reader, at, null).Label, $"{arrival.From}: six alone does not reach it");
        }

        // After the mural the Init itself sets twelve and six and runs no session.
        memory.SetGameMoment(627);
        memory.SetHands(0, 6);
        Equal(DoorTwelve, Single(reader, Position(607, 7, -555, 47), "Cross to doorway twelve").TriggerLine!.Value,
            "doorway twelve is unchanged at 627");
    }

    private static void ClockNativeEvidence()
    {
        var scripts = Scripts();
        // IDdr's script 1 locks all twenty-four triangles; the long hand's script 3 and the short
        // hand's script 6 each unlock the pair for their own hour word.
        var pairs = ClockBridgePairs(scripts);
        Equal("93,2|78,12|79,0|89,66|72,60|73,54|85,48|74,42|75,36|81,14|76,10|77,6",
            string.Join("|", Enumerable.Range(0, 12).Select(hour => string.Join(",", pairs[hour]))),
            "each hour's bridge pair, from entity 8's own unlock scripts");
        Equal(24, scripts.ReadScriptOpcodes(607, 8, 1).Count(op => op.Opcode == 0x6D && op.Bytes[3] == 1),
            "IDdr script 1 locks all twenty-four bridge triangles");
        AssertOpcode(scripts, 607, 22, 6, 66, [0x16, 0x40, 0xE4, 0x00, 0x06, 0x00, 0x00, 0x04], "the short hand tests 4[228]w == 6");
        AssertOpcode(scripts, 607, 22, 6, 74, [0x02, 0x08, 0xC8], "and requests IDdr script 8");
        // The first visit's Init: long hand two, short hand ten.
        Equal(true, scripts.ReadScriptOpcodes(607, 0, 0).Any(op => Hex(op) == "8140E20200"), "e0 sets 4[226]w = 2");
        Equal(true, scripts.ReadScriptOpcodes(607, 0, 0).Any(op => Hex(op) == "8140E40A00"), "and 4[228]w = 10");
        // Move it myself: OK adds an hour, and passing twelve carries the short hand.
        AssertOpcode(scripts, 607, 5, 1, 0, [0x30, 0x20, 0x00, 0x1C], "longdr script 1 waits on OK");
        AssertOpcode(scripts, 607, 5, 1, 4, [0x86, 0x40, 0xE2, 0x01, 0x00], "OK adds one to 4[226]w");
        AssertOpcode(scripts, 607, 5, 1, 17, [0x02, 0x16, 0xC4], "and past twelve moves the short hand");
        // The Time Guardian's session runs once, from the director's Main, on entering.
        Equal(true, scripts.ReadScriptOpcodes(607, 1, 0).Any(op => Hex(op) == "0319C3"), "e1's Main requests face script 3");
        Equal(true, scripts.ReadAllScriptOpcodes(607).Where(script => !(script.EntityId == 1 && script.ScriptId == 0))
                .All(script => script.Opcodes.All(op => op.Opcode is not (0x01 or 0x02 or 0x03) || op.Bytes[1] != 0x19 || (op.Bytes[2] & 0x1F) != 3)),
            "nothing else requests the session");
        // Every way in lands on the doorway side the state case uses.
        var source = new FlevelDataSource(DataRoot!);
        foreach (var arrival in ClockArrivals)
        {
            var from = int.Parse(arrival.From[..3]);
            Equal(true, NativeArrivals(scripts, source, from, 607).Contains((arrival.X, arrival.Y, arrival.Triangle)),
                $"{arrival.From} arrives at ({arrival.X},{arrival.Y}) t{arrival.Triangle}");
        }
    }

    /// <summary>
    /// The readout's places are the installed walkmesh's own parts. Each bridge is three
    /// triangles, and IDdr's pair locks its inner and outer ones. With the twelve inner ones
    /// shut, a doorway's side is what can be walked from its bridge's outer end: its platform
    /// and the outer two of the three. The middle is what can be walked from the centre with
    /// all twenty-four shut, and the inner ends with it. Every triangle is given exactly its
    /// part's place, and a triangle that belongs to no part cannot be walked onto from any way
    /// in, whatever the hands do.
    /// </summary>
    private static void ClockPlacesAreTheWalkmeshsOwnParts(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var pairs = ClockBridgePairs(Scripts());
        var walkmesh = createWalkmeshReader(607).Read(Position(607)).Walkmesh
            ?? throw new InvalidOperationException("field607 walkmesh must be readable");
        var inner = pairs.Select(pair => pair[0]).ToHashSet();
        var middle = Walkable(walkmesh, [131], pairs.SelectMany(pair => pair).ToHashSet());
        Equal(30, middle.Count, "the middle, every bridge shut");
        middle.UnionWith(inner);
        var places = middle.ToDictionary(triangle => triangle, _ => "You are in the middle of the clock. ");
        for (var hour = 0; hour < 12; hour++)
        {
            var side = Walkable(walkmesh, [pairs[hour][1]], inner);
            Equal(6, side.Count, $"doorway {ClockNumeralNames[hour]}'s side: [{string.Join(",", side.Order())}]");
            foreach (var triangle in side)
            {
                Equal(false, places.ContainsKey(triangle), $"triangle {triangle} is in one part only");
                places[triangle] = $"You are by doorway {ClockNumeralNames[hour]}. ";
            }
        }

        foreach (var arrival in ClockArrivals)
        {
            Equal($"You are by doorway {ClockNumeralNames[arrival.Door % 12]}. ", places.GetValueOrDefault(arrival.Triangle),
                $"{arrival.From} lands on its own doorway's side");
        }

        var walkableAtAll = Walkable(walkmesh, ClockArrivals.Select(arrival => (int)arrival.Triangle), new HashSet<int>());
        for (var triangle = 0; triangle < walkmesh.Triangles.Count; triangle++)
        {
            var place = places.GetValueOrDefault(triangle, string.Empty);
            Equal(place + SixAndTen, new FieldActivityReadout().Describe(ClockAt(triangle)), $"triangle {triangle}");
            if (place.Length == 0)
            {
                Equal(false, walkableAtAll.Contains(triangle), $"triangle {triangle}, given no place, is walkable from no way in");
            }
        }
    }

    private static HashSet<int> Walkable(FieldWalkmesh walkmesh, IEnumerable<int> from, IReadOnlySet<int> shut)
    {
        var seen = new HashSet<int>(from);
        var queue = new Queue<int>(seen);
        while (queue.Count > 0)
        {
            var triangle = walkmesh.Triangles[queue.Dequeue()];
            for (var edge = 0; edge < 3; edge++)
            {
                var next = triangle.GetAdjacentTriangle(edge);
                if (next >= 0 && next < walkmesh.Triangles.Count && !shut.Contains(next) && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// For every way in and every pair of hands, the Story step is "Cross to doorway six"
    /// exactly when the installed walkmesh, locked the way the hands' own scripts lock it, has
    /// a route from where the party stands to doorway six - and otherwise the step that says
    /// how to make one.
    /// </summary>
    private static void ClockStoryIsARouteExactlyWhenTheBridgesMakeOne(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var scripts = Scripts();
        var pairs = ClockBridgePairs(scripts);
        var mesh = createWalkmeshReader(607);
        var memory = new PuzzleMemory(607);
        memory.SetGameMoment(618);
        var reader = memory.StoryReader();
        // With the field's own transitions, as the live planner has them: nothing the scripts
        // publish may make a way the bridges do not.
        var planner = memory.Planner(mesh, scripts.ReadField(607).Transitions);
        var starts = ClockArrivals.Select(arrival => (arrival.From, Arrival(mesh, 607, arrival.X, arrival.Y, arrival.Triangle)))
            .Append(("the middle", Arrival(mesh, 607, 5, 10, 131)))
            .ToArray();
        var routed = 0;
        for (var longHour = 0; longHour <= 12; longHour++)
        {
            for (var shortHour = 0; shortHour <= 12; shortHour++)
            {
                memory.SetHands(longHour, shortHour);
                memory.SetLocked(pairs.SelectMany((pair, hour) => hour == longHour % 12 || hour == shortHour % 12 ? [] : pair));
                foreach (var (from, start) in starts)
                {
                    var target = Single(reader, start, null);
                    var doorSix = new FieldNavigationTarget(607, FieldNavigationCategory.Story, CrossToSix, -1, -716, 0,
                        TriggerLine: DoorSix, CompletesOnArrival: true);
                    var reachable = planner.TryBuildRoute(start, doorSix, out var route);
                    Equal(reachable ? CrossToSix : SetTheHands, target.Label,
                        $"{from}, hands {longHour}/{shortHour}: {planner.LastDiagnostic}");
                    if (reachable)
                    {
                        routed++;
                        Equal(true, planner.TryBuildRoute(start, target, out var storyRoute) && storyRoute.TargetTriggerLine == DoorSix,
                            $"{from}, hands {longHour}/{shortHour}: the Story target itself is routed to doorway six");
                        Equal(true, route.TrianglePath.All(triangle => !memory.IsLocked(triangle)),
                            $"{from}, hands {longHour}/{shortHour}: the route never crosses a locked triangle");
                    }
                    else
                    {
                        Equal(false, string.IsNullOrWhiteSpace(target.ManualNavigationGuidance),
                            $"{from}, hands {longHour}/{shortHour}: the unreachable step is guidance, not a route");
                    }
                }
            }
        }

        Equal(true, routed > 100, $"a meaningful share of the {13 * 13 * starts.Length} states are routes ({routed})");

        // The user's own evening: in through the corridor, long hand turned four hours to six.
        memory.SetHands(6, 10);
        memory.SetLocked(pairs.SelectMany((pair, hour) => hour is 6 or 10 ? [] : pair));
        var entrance = Arrival(mesh, 607, -555, 308, 26);
        Equal(true, planner.TryBuildRoute(entrance, Single(reader, entrance, CrossToSix), out var evening),
            $"the corridor's doorway reaches doorway six over two bridges: {planner.LastDiagnostic}");
        Equal(true, evening.TrianglePath.Contains(pairs[10][0]) && evening.TrianglePath.Contains(pairs[6][0]),
            "the route crosses the tenth doorway's bridge and then the sixth's");
    }

    /// <summary>
    /// The second hand is a moving hazard, not a door. Its LINE (line10, entity 18) starts on
    /// the twelfth bridge, its Main sets it again from the hand's own position every frame, and
    /// walking into it throws the party off the clock and out to kuro_5 (608). So where it
    /// starts is neither an exit nor a jump onto doorway six's side. The doors themselves stay
    /// exits, and the second hand is still read out live from its model.
    /// </summary>
    private static void ClockSecondHandIsNotAStaticExit(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var scripts = Scripts();
        AssertOpcode(scripts, 607, 18, 0, 0, [0xD0, 0x04, 0x00, 0xB2, 0x00, 0x00, 0x00, 0x01, 0x00, 0xFF, 0x01, 0x00, 0x00],
            "line10 starts at (4,178)-(1,511)");
        Equal(true, scripts.ReadScriptOpcodes(607, 18, 0).Any(op => op.Opcode == 0xD3),
            "and its Main moves it with SLINE");
        AssertOpcode(scripts, 607, 18, 5, 0, [0x02, 0x14, 0xC5], "walking into it runs cloud's script 5");
        var knockdown = scripts.ReadScriptOpcodes(607, 20, 5);
        Equal(true, knockdown.Any(op => Hex(op) == "A50000FDFF81FD00002E00"), "which puts the party on triangle 46");
        Equal(true, knockdown.Any(op => op.Opcode == 0x60 && BitConverter.ToUInt16(op.Bytes.ToArray(), 1) == 608),
            "and map jumps down to 608");

        var read = scripts.ReadField(607);
        Equal("script-exit:607:10:614,script-exit:607:11:615,script-exit:607:12:615,script-exit:607:13:604,script-exit:607:14:615," +
              "script-exit:607:15:615,script-exit:607:16:614,script-exit:607:17:614,script-exit:607:9:615",
            string.Join(",", read.Exits.Select(exit => exit.StableId).Order(StringComparer.Ordinal)),
            "the nine doors are exits and the second hand is not");
        Equal(false, read.Transitions.Any(transition => transition.SourceEntityId == 18), "nor is it a way onto doorway six's side");

        // Long hand on twelve, short hand on ten: the tenth bridge and the twelfth, and no hand
        // on six. The hand's first place is on the twelfth bridge, so a jump from there would
        // have been a way to doorway six that the clock does not make.
        var mesh = createWalkmeshReader(607);
        var memory = new PuzzleMemory(607);
        memory.SetGameMoment(618);
        memory.SetHands(12, 10);
        var pairs = ClockBridgePairs(scripts);
        memory.SetLocked(pairs.SelectMany((pair, hour) => hour is 0 or 10 ? [] : pair));
        var planner = memory.Planner(mesh, read.Transitions);
        var doorSix = new FieldNavigationTarget(607, FieldNavigationCategory.Story, CrossToSix, -1, -716, 0,
            TriggerLine: DoorSix, CompletesOnArrival: true);
        foreach (var start in new[] { Arrival(mesh, 607, -555, 308, 26), Arrival(mesh, 607, 5, 10, 131) })
        {
            Equal(false, planner.TryBuildRoute(start, doorSix, out _),
                $"no way to doorway six from t{start.TriangleId} without a hand on six: {planner.LastDiagnostic}");
        }

        Equal(SetTheHands, Single(memory.StoryReader(), Position(607, 5, 10, 131), null).Label, "and the Story says so");
        var withSecondHand = new FieldActivityObservation(607, 0, 0, 0, 131, true, 618, new FieldNavigationControlTransform(0), true,
            [ClockHand(21, 0), ClockHand(22, 170), ClockHand(23, 64)], new Dictionary<int, FieldActivityWaitState>(), _ => true,
            locked => locked is not (85 or 48 or 76 or 10), 0);
        Equal(true, (new FieldActivityReadout().Describe(withSecondHand) ?? string.Empty).Contains("second hand at three", StringComparison.Ordinal),
            "the second hand is still read out where it is");
    }

    private static int[][] ClockBridgePairs(FieldScriptNavigationCatalog scripts)
    {
        // Long hand script 3: IFSW 4[226]w == hour, then REQSW IDdr (entity 8) script N.
        var ops = scripts.ReadScriptOpcodes(607, 21, 3);
        var pairs = new int[13][];
        for (var index = 0; index < ops.Count; index++)
        {
            var op = ops[index];
            if (op.Opcode != 0x16 || op.Bytes[2] != 0xE2)
            {
                continue;
            }

            var hour = op.Bytes[4] | (op.Bytes[5] << 8);
            var request = ops.Skip(index + 1).First(candidate => candidate.Opcode == 0x02 && candidate.Bytes[1] == 8);
            var unlockScript = request.Bytes[2] & 0x1F;
            pairs[hour] = scripts.ReadScriptOpcodes(607, 8, unlockScript)
                .Where(candidate => candidate.Opcode == 0x6D && candidate.Bytes[3] == 0)
                .Select(candidate => candidate.Bytes[1] | (candidate.Bytes[2] << 8))
                .ToArray();
        }

        Equal(string.Join(",", pairs[0]), string.Join(",", pairs[12]), "twelve is one bridge whichever word says it");
        return pairs[..12];
    }

    // --- 586 tower5, Godo's Pagoda ---------------------------------------------------------------

    private static readonly FieldNavigationTriggerLine PagodaUp = new(82, 547, 116, 125, 426, 83);
    private static readonly FieldNavigationTriggerLine PagodaDown = new(-137, 430, -18, -118, 544, -18);
    private static readonly FieldNavigationTriggerLine PagodaDoor = new(-94, -401, 20, 101, -399, 20);
    private static readonly string[] PagodaOpponents = ["Gorky", "Shake", "Chekhov", "Staniv", "Godo"];
    private static readonly string[] PagodaFloorNames = ["first", "second", "third", "fourth", "fifth"];
    private static string Climb(int floor) => $"Climb the stairs to the {PagodaFloorNames[floor + 1]} floor (optional)";
    private static string GoDown(int floor) => $"Go back down to the {PagodaFloorNames[floor - 1]} floor (optional)";
    private const string LeavePagoda = "Leave the Pagoda (optional)";
    private const int Yuffie = 5;

    private static void PagodaStoryOffersEachFloorsOpponentThenTheStairs()
    {
        var memory = new PuzzleMemory(586);
        memory.SetGameMoment(700);
        for (var floor = 0; floor < 5; floor++)
        {
            memory.AddActor(17 + floor, 30, 315, 20);
        }

        var reader = memory.StoryReader();
        var courtyard = Position(586, 8, -356, 16);
        var upstairs = Position(586, -215, 312, 15);

        memory.SetParty(0, Yuffie, 2);
        for (var floor = 0; floor < 5; floor++)
        {
            memory.SetBankByte(15, 138, (byte)floor);
            var at = floor == 0 ? courtyard : upstairs;
            var beatenBelow = (byte)((1 << floor) - 1);
            memory.SetBankByte(15, 139, beatenBelow);
            var labels = Labels(reader, at);
            var expected = new List<string> { $"Talk to {PagodaOpponents[floor]} (optional)" };
            if (floor > 0)
            {
                expected.Add(GoDown(floor));
            }

            Equal(string.Join(" | ", expected), labels, $"floor {floor}, its opponent still to fight");
            var talk = reader.ReadTargets(at).Single(target => target.Label.StartsWith("Talk to", StringComparison.Ordinal));
            Equal(17 + floor, talk.TriggerEntityId, $"floor {floor}: the Talk is the floor's own opponent");
            Equal(FieldNavigationActivation.Talk, talk.Activation, $"floor {floor}: a Talk, not a walk-over");

            memory.SetBankByte(15, 139, (byte)(beatenBelow | (1 << floor)));
            labels = Labels(reader, at);
            expected = floor < 4 ? [Climb(floor)] : [];
            if (floor > 0)
            {
                expected.Add(GoDown(floor));
            }

            Equal(string.Join(" | ", expected), labels, $"floor {floor}, its opponent beaten");
            if (floor < 4)
            {
                Equal(PagodaUp, reader.ReadTargets(at).Single(target => target.Label == Climb(floor)).TriggerLine!.Value,
                    $"floor {floor}: the stairs up are jump_u's own line");
            }
        }

        // Each floor's stairs are their own target, so the one being walked to is gone once
        // its Move script has changed the floor byte, even though the field id stays 586.
        memory.SetBankByte(15, 139, 0x03);
        memory.SetBankByte(15, 138, 1);
        var climbFromSecond = reader.ReadTargets(upstairs).Single(target => target.Label == Climb(1)).StableId;
        memory.SetBankByte(15, 138, 2);
        Equal(false, reader.ReadTargets(upstairs).Any(target => target.StableId == climbFromSecond),
            "the second floor's stairs up are not offered on the third floor");

        // Godo's win puts the party on the floor below with the quest done: nothing to climb.
        memory.SetBankByte(15, 138, 3);
        memory.SetBankByte(15, 139, 0x1F);
        Equal(GoDown(3), Labels(reader, Position(586, 275, 277, 22)), "after Godo only the way down is offered");
        Equal(PagodaDown, reader.ReadTargets(Position(586, 275, 277, 22)).Single().TriggerLine!.Value,
            "the stairs down are jump_d's own line");
        memory.SetBankByte(15, 138, 0);
        Equal(LeavePagoda, Labels(reader, courtyard), "the ground floor's way out once the Pagoda is done");
        Equal(PagodaDoor, reader.ReadTargets(courtyard).Single().TriggerLine!.Value, "the courtyard door is gateway0");

        // Without Yuffie nobody fights, and the ground floor offers only the way out.
        memory.SetParty(0, 2, 3);
        memory.SetBankByte(15, 139, 0);
        Equal(LeavePagoda, Labels(reader, courtyard), "no opponent is offered without Yuffie in the party");
        memory.SetBankByte(15, 138, 2);
        memory.SetBankByte(15, 139, 0x03);
        Equal(GoDown(2), Labels(reader, upstairs), "and upstairs only the stairs down");
    }

    /// <summary>
    /// The stairs' Move script changes the floor byte as the party crosses the line and then
    /// reloads the same field, so the exit or Story step being walked to goes away at its line.
    /// That is the crossing: navigation ends there as reached rather than as a lost route, and
    /// does not carry on to the next floor's stairs, which are a different target. A crossing
    /// target that goes away while the party is still far from it is still said to be gone.
    /// </summary>
    private static void CrossingTargetRemovedAtItsLineWasReached()
    {
        var floor = 1;
        FieldNavigationTarget StairsExit() => new(586, FieldNavigationCategory.Exits,
            $"Stairs up to the {PagodaFloorNames[floor + 1]} floor", 103, 486, 99, $"script-exit:586:14:586:floor{floor}",
            TriggerEntityId: 14, CompletesOnArrival: true, DestinationFieldIds: [586], TriggerLine: PagodaUp);
        FieldNavigationTarget StairsStep() => new(586, FieldNavigationCategory.Story,
            Climb(floor), 103, 486, 99, $"story:586:jump_u:{Climb(floor)}",
            TriggerEntityId: 14, CompletesOnArrival: true, TriggerLine: PagodaUp);
        var source = new FieldNavigationTargetSource([],
            storyTargetProvider: _ => [StairsStep()],
            exitTargetProvider: _ => [StairsExit()]);
        var noInput = new FieldNavigationInputSnapshot(0, FieldNavigationInput.None);
        var noRotation = new FieldNavigationControlTransform(-128);
        var landing = Position(586, -215, 312, 15);
        // Forty units short of the stairs, square on to the line.
        var atTheStairs = Position(586, 65, 473, 37, 99);
        var epoch = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        foreach (var (category, label, gone) in new[]
                 {
                     (FieldNavigationCategory.Exits, "Stairs up to the third floor", "is no longer reachable. Navigation off."),
                     (FieldNavigationCategory.Story, Climb(1), "no longer available. Navigation off.")
                 })
        {
            foreach (var nearTheLine in new[] { true, false })
            {
                floor = 1;
                var controller = new FieldNavigationController(source, new StraightRoutePlanner());
                if (category == FieldNavigationCategory.Story)
                {
                    controller.HandleAction(FieldNavigationAction.NextCategory, landing, noRotation);
                }

                controller.HandleAction(FieldNavigationAction.ToggleBeacon, landing, noRotation);
                Equal(true, controller.BeaconEnabled, $"{category}: navigation on to {label}");
                _ = controller.UpdateLiveTracking(landing, noInput, noRotation, isSuppressed: false, 80, observedAt: epoch);
                var last = nearTheLine ? atTheStairs : landing;
                var approach = controller.UpdateLiveTracking(last, noInput, noRotation, isSuppressed: false, 80,
                    observedAt: epoch.AddMilliseconds(200));
                Equal(false, (approach?.Speech ?? string.Empty).Contains("reached", StringComparison.Ordinal),
                    $"{category}: forty units short of the line is not the crossing");
                Equal(true, controller.BeaconEnabled, $"{category}: still on short of the line");

                // jump_u's Move: 15[138] goes up by one, and the field reloads into itself.
                floor = 2;
                var removed = controller.UpdateLiveTracking(landing, noInput, noRotation, isSuppressed: false, 80,
                    observedAt: epoch.AddMilliseconds(400));
                Equal(nearTheLine ? $"{label} reached. Navigation off." : $"{label} {gone}", removed?.Speech,
                    $"{category}, {(nearTheLine ? "at the line" : "far from it")}");
                Equal(false, controller.BeaconEnabled, $"{category}: navigation is off");
                var after = controller.UpdateLiveTracking(landing, noInput, noRotation, isSuppressed: false, 80,
                    observedAt: epoch.AddMilliseconds(600));
                Equal(null, after?.Speech, $"{category}: nothing carries on to the third floor's stairs");
                Equal(false, controller.BeaconEnabled, $"{category}: and navigation stays off");
            }
        }
    }

    private static void OrdinaryCrossingRemovedNearbyWasNotReached()
    {
        var epoch = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        var noInput = new FieldNavigationInputSnapshot(0, FieldNavigationInput.None);
        var noRotation = new FieldNavigationControlTransform(-128);
        var landing = Position(586, -215, 312, 15);
        var shortOfDoorway = Position(586, 65, 473, 37, 99);
        foreach (var category in new[] { FieldNavigationCategory.Exits, FieldNavigationCategory.Story })
        {
            var offered = true;
            // A different scripted doorway can be disabled while the party stands nearby.
            // No crossing, teleport or Pagoda floor change occurs in this observation.
            var target = new FieldNavigationTarget(586, category, "Doorway", 103, 486, 99,
                "script-exit:586:99:587", TriggerEntityId: 99, CompletesOnArrival: true,
                DestinationFieldIds: [587], TriggerLine: PagodaUp);
            var source = new FieldNavigationTargetSource([],
                storyTargetProvider: _ => offered && category == FieldNavigationCategory.Story ? [target] : [],
                exitTargetProvider: _ => offered && category == FieldNavigationCategory.Exits ? [target] : []);
            var controller = new FieldNavigationController(source, new StraightRoutePlanner());
            if (category == FieldNavigationCategory.Story)
            {
                controller.HandleAction(FieldNavigationAction.NextCategory, landing, noRotation);
            }

            controller.HandleAction(FieldNavigationAction.ToggleBeacon, landing, noRotation);
            _ = controller.UpdateLiveTracking(landing, noInput, noRotation, false, 80, observedAt: epoch);
            _ = controller.UpdateLiveTracking(shortOfDoorway, noInput, noRotation, false, 80,
                observedAt: epoch.AddMilliseconds(200));
            Equal(true, controller.BeaconEnabled, $"{category}: not yet at the doorway");
            offered = false;
            var removed = controller.UpdateLiveTracking(shortOfDoorway, noInput, noRotation, false, 80,
                observedAt: epoch.AddMilliseconds(400));
            var reason = category == FieldNavigationCategory.Exits ? "is no longer reachable" : "no longer available";
            Equal($"Doorway {reason}. Navigation off.", removed?.Speech,
                $"{category}: a disabled nearby doorway must not be called reached");
            Equal(false, controller.BeaconEnabled, $"{category}: stop on an unavailable target");
        }
    }

    /// <summary>A straight line to the target, like the one-triangle planners of the controller tests.</summary>
    private sealed class StraightRoutePlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return true;
        }

        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(position.FieldId, $"{target.FieldId}:{target.StableId}", [position.TriangleId], [],
                new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z), position.TriangleId);
            return position.FieldId == target.FieldId;
        }

        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
            return position.FieldId == target.FieldId;
        }
    }

    /// <summary>
    /// The stairs as Exits. The generic rule still never publishes a line that only map jumps
    /// back into its own field; tower5's jump_u and jump_d are its one verified exception, with
    /// one exit per floor each offered only while 15[138] names that floor. So the ground floor
    /// has no stairs down and the top floor none up, and the exit a route is following is gone
    /// once the stairs' own Move script has changed the floor byte.
    /// </summary>
    private static void PagodaStairsAreExitsOnTheirOwnFloors(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var scripts = Scripts();
        var read = scripts.ReadField(586);
        var stairs = read.Exits.Where(exit => exit.TriggerEntityId is 14 or 15).ToArray();
        Equal("script-exit:586:14:586:floor0,script-exit:586:14:586:floor1,script-exit:586:14:586:floor2,script-exit:586:14:586:floor3," +
              "script-exit:586:15:586:floor1,script-exit:586:15:586:floor2,script-exit:586:15:586:floor3,script-exit:586:15:586:floor4",
            string.Join(",", stairs.Select(exit => exit.StableId).Order(StringComparer.Ordinal)),
            "one exit per floor each staircase serves");
        foreach (var exit in stairs)
        {
            Equal(exit.TriggerEntityId == 14 ? PagodaUp : PagodaDown, exit.TriggerLine!.Value, $"{exit.StableId} is its own LINE");
            Equal(true, exit.CompletesOnArrival, $"{exit.StableId} is finished on its crossing");
            Equal("586", string.Join(",", exit.DestinationFieldIds ?? []), $"{exit.StableId} reloads this field");
        }

        var labels = new FieldExitLabelResolver(_ => default, () => "Wutai, Godo's Pagoda");
        var memory = new PuzzleMemory(586);
        memory.SetGameMoment(700);
        var mesh = createWalkmeshReader(586);
        var planner = memory.Planner(mesh, read.Transitions);
        var fromBelow = Arrival(mesh, 586, -215, 312, 15);
        var fromAbove = Arrival(mesh, 586, 275, 277, 22);
        var courtyard = Arrival(mesh, 586, 8, -356, 16);
        IReadOnlyList<FieldNavigationTarget> Offered() =>
            labels.Resolve(FieldScriptExitGuards.Apply(read.Exits, read.ExitGuards, memory.AddressSpace));
        for (var floor = 0; floor < 5; floor++)
        {
            memory.SetBankByte(15, 138, (byte)floor);
            var offered = Offered();
            var expected = new List<string>();
            if (floor < 4)
            {
                expected.Add($"Stairs up to the {PagodaFloorNames[floor + 1]} floor");
            }

            if (floor > 0)
            {
                expected.Add($"Stairs down to the {PagodaFloorNames[floor - 1]} floor");
            }

            var stairsHere = offered.Where(exit => exit.TriggerEntityId is 14 or 15).ToArray();
            Equal(string.Join(" | ", expected), string.Join(" | ", stairsHere.Select(exit => exit.Label)),
                $"floor {floor}: its own stairs, and no other floor's");
            // The line entity 16's own guard, unchanged: its map jump to 587 runs only on the
            // ground floor (and while 15[139] bits 0 and 5 are clear, as they are here).
            Equal(floor == 0, offered.Any(exit => exit.StableId == "script-exit:586:16:587"),
                $"floor {floor}: entity 16's way out to 587 is offered exactly on the ground floor");
            foreach (var beaten in new[] { false, true })
            {
                // init's own locks for this floor and this bit.
                memory.SetLocked(floor == 0 ? beaten ? [27] : [27, 29] : beaten && floor < 4 ? [16] : [16, 29]);
                var start = floor == 0 ? courtyard : fromBelow;
                foreach (var exit in stairsHere)
                {
                    var reachable = planner.TryBuildRoute(start, exit, out var route);
                    Equal(exit.TriggerEntityId == 15 || beaten, reachable,
                        $"floor {floor}, beaten={beaten}: {exit.Label} is walkable exactly when init opens it: {planner.LastDiagnostic}");
                    if (reachable)
                    {
                        Equal(exit.TriggerLine, route.TargetTriggerLine, $"floor {floor}: {exit.Label} ends on its own line");
                        Equal(true, route.TrianglePath.All(triangle => !memory.IsLocked(triangle)),
                            $"floor {floor}: {exit.Label} crosses no lock");
                    }
                    else
                    {
                        // Held shut by the floor's own lock, as any locked door is: listed,
                        // and selecting it says the way is shut rather than routing through.
                        Equal(true, planner.LastFailureWasNativeBoundary, $"floor {floor}: {exit.Label} is shut, not missing");
                    }
                }

                var listed = new ReachableFieldExitTargetProvider(_ => stairsHere, planner).ReadTargets(start);
                Equal(string.Join(" | ", expected), string.Join(" | ", listed.Select(exit => exit.Label)),
                    $"floor {floor}, beaten={beaten}: the Exits list itself");
            }
        }

        // Going down from the floor above lands beside that floor's stairs up.
        memory.SetBankByte(15, 138, 1);
        memory.SetLocked([16]);
        var upFromSecond = Offered().Single(exit => exit.TriggerEntityId == 14);
        Equal(true, planner.TryBuildRoute(fromAbove, upFromSecond, out _), $"from the stairs down's landing: {planner.LastDiagnostic}");

        // Crossing the stairs up is jump_u's Move adding one to the floor byte: the exit being
        // walked to is gone and the next floor's is a different one.
        memory.SetBankByte(15, 138, 2);
        Equal(false, Offered().Any(exit => exit.StableId == upFromSecond.StableId), "the second floor's stairs up are gone on the third");
    }

    /// <summary>
    /// Every other line that only map jumps into its own field is still not an exit: the Shinra
    /// stairwell, the floor 60 resets, the Honeycomb escape, the airport glin, Mount Corel's
    /// border5 and the desert's wander.
    /// </summary>
    private static void OrdinarySameFieldResetsStayOutOfExits()
    {
        var scripts = Scripts();
        foreach (var (field, entities) in new[]
                 {
                     (190, new[] { 11, 14 }), (230, new[] { 12, 13 }), (239, new[] { 19, 20, 22, 23, 24, 25, 26, 27 }),
                     (384, new[] { 15 }), (464, new[] { 10 }), (481, new[] { 6 })
                 })
        {
            var read = scripts.ReadField(field);
            var all = scripts.ReadAllScriptOpcodes(field);
            foreach (var entity in entities)
            {
                Equal(true, all.Any(script => script.EntityId == entity && script.ScriptId == 0 &&
                                              script.Opcodes.Any(op => op.Opcode == 0xD0)),
                    $"field {field} entity {entity} is a LINE");
                Equal($"{field}", string.Join(",", MapJumpDestinations(all, entity).Order()),
                    $"field {field} line {entity} map jumps only into its own field");
                Equal(false, read.Exits.Any(exit => exit.TriggerEntityId == entity),
                    $"field {field} line {entity} is a same-field reset, not an exit");
            }
        }
    }

    /// <summary>The fields a line's scripts map jump to, following the scripts they request.</summary>
    private static HashSet<int> MapJumpDestinations(IReadOnlyList<FieldScriptDefinition> all, int entity)
    {
        var seen = new HashSet<(int Entity, int Script)>();
        var queue = new Queue<(int Entity, int Script)>(all.Where(script => script.EntityId == entity)
            .Select(script => (script.EntityId, script.ScriptId)));
        var destinations = new HashSet<int>();
        while (queue.TryDequeue(out var next))
        {
            if (!seen.Add(next))
            {
                continue;
            }

            foreach (var op in all.Where(script => script.EntityId == next.Entity && script.ScriptId == next.Script)
                         .SelectMany(script => script.Opcodes))
            {
                if (op.Opcode == 0x60 && op.Bytes.Count == 10)
                {
                    destinations.Add(BitConverter.ToUInt16(op.Bytes.ToArray(), 1));
                }
                else if (op.Opcode is 0x01 or 0x02 or 0x03 && op.Bytes.Count == 3)
                {
                    // REQ, REQSW, REQEW: entity, then priority and script.
                    queue.Enqueue((op.Bytes[1], op.Bytes[2] & 0x1F));
                }
            }
        }

        return destinations;
    }

    private static void PagodaNativeEvidence()
    {
        var scripts = Scripts();
        AssertOpcode(scripts, 586, 14, 2, 0, [0x95, 0x0F, 0x8A], "jump_u's Move adds one to 15[138]");
        Equal(true, scripts.ReadScriptOpcodes(586, 14, 2).Any(op => Hex(op) == "604A0229FF38010F0000"),
            "and map jumps into 586 at (-215,312), triangle 15");
        AssertOpcode(scripts, 586, 15, 2, 0, [0x97, 0x0F, 0x8A], "jump_d's Move takes one off 15[138]");
        Equal(true, scripts.ReadScriptOpcodes(586, 15, 2).Any(op => Hex(op) == "604A0213011501160000"),
            "and map jumps to (275,277), triangle 22");
        var init = scripts.ReadScriptOpcodes(586, 4, 0).Select(Hex).ToArray();
        for (var floor = 0; floor < 4; floor++)
        {
            Equal(true, init.Contains($"14F08A0{floor}0011") || init.Any(op => op.StartsWith($"14F08A0{floor}00", StringComparison.Ordinal)),
                $"init tests 15[138] == {floor}");
            Equal(true, init.Any(op => op.StartsWith($"14F08B0{floor}09", StringComparison.Ordinal)),
                $"and 15[139] bit {floor} before triangle 29");
        }

        Equal(true, init.Contains("6D1D0000") && init.Contains("6D1D0001"), "triangle 29 is unlocked and locked by init");
        Equal(true, init.Contains("6D100000") && init.Contains("6D1B0001"), "ground floor: 16 open, 27 locked");
        Equal(true, init.Contains("6D100001") && init.Contains("6D1B0000"), "upper floors: 16 locked, 27 open");
        foreach (var (entity, bit, battle) in new[] { (17, 0, 623), (18, 1, 624), (19, 2, 625), (20, 3, 626) })
        {
            var talk = scripts.ReadScriptOpcodes(586, entity, 1).Select(Hex).ToArray();
            Equal(true, talk.Any(op => op.StartsWith("CB05", StringComparison.Ordinal)), $"e{entity}'s Talk asks IFPRTYQ 5 (Yuffie)");
            Equal(true, talk.Any(op => op.Length == 8 && op.StartsWith("70", StringComparison.Ordinal) &&
                                       op.EndsWith($"{battle & 0xFF:X2}{battle >> 8:X2}", StringComparison.Ordinal)),
                $"e{entity} fights battle {battle}");
            Equal(true, talk.Any(op => op == $"82F08B0{bit}"), $"and a win sets 15[139] bit {bit}");
        }

        var godo = scripts.ReadScriptOpcodes(586, 21, 1).Select(Hex).ToArray();
        Equal(true, godo.Any(op => op.StartsWith("CB05", StringComparison.Ordinal)), "Godo's Talk needs Yuffie too");
        Equal(true, scripts.ReadScriptOpcodes(586, 21, 0).Any(op => Hex(op) == "82F08B04"), "Godo's win sets 15[139] bit 4");
        // No save point in the Pagoda or its courtyard; Wutai's is on the main street.
        foreach (var field in new[] { 586, 587 })
        {
            Equal(false, scripts.ReadAllScriptOpcodes(field).Any(script => script.EntityName.Equals("save", StringComparison.OrdinalIgnoreCase)),
                $"field {field} has no save point entity");
        }

        Equal(true, scripts.ReadAllScriptOpcodes(579).Any(script => script.EntityName.Equals("save", StringComparison.OrdinalIgnoreCase)),
            "uutai1 has the town's save point");
    }

    private static void PagodaRoutesFollowTheFloorsOwnLocks(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var mesh = createWalkmeshReader(586);
        var memory = new PuzzleMemory(586);
        memory.SetGameMoment(700);
        for (var floor = 0; floor < 5; floor++)
        {
            memory.AddActor(17 + floor, 30, 315, 20);
        }

        memory.SetParty(Yuffie, 0, 2);
        var reader = memory.StoryReader();
        var planner = memory.Planner(mesh);
        var courtyard = Arrival(mesh, 586, 8, -356, 16);
        var fromBelow = Arrival(mesh, 586, -215, 312, 15);
        var fromAbove = Arrival(mesh, 586, 275, 277, 22);
        for (var floor = 0; floor < 5; floor++)
        {
            foreach (var beaten in new[] { false, true })
            {
                memory.SetBankByte(15, 138, (byte)floor);
                memory.SetBankByte(15, 139, (byte)((1 << floor) - 1 | (beaten ? 1 << floor : 0)));
                // init's own locks for this floor and this bit.
                var locked = new List<int> { floor == 0 ? 27 : 16 };
                if (!beaten || floor == 4)
                {
                    locked.Add(29);
                }

                memory.SetLocked(locked);
                foreach (var (name, start) in floor == 0
                             ? new[] { ("courtyard", courtyard), ("stairs down", fromAbove) }
                             : new[] { ("stairs up", fromBelow), ("stairs down", fromAbove) })
                {
                    foreach (var target in reader.ReadTargets(start))
                    {
                        Equal(true, planner.TryBuildRoute(start, target, out var route),
                            $"floor {floor}, beaten={beaten}, from {name}: {target.Label}: {planner.LastDiagnostic}");
                        Equal(true, route.TrianglePath.All(triangle => !memory.IsLocked(triangle)),
                            $"floor {floor}, beaten={beaten}, from {name}: {target.Label} crosses no lock");
                    }
                }

                // The stairs up are shut exactly while this floor is unbeaten.
                var up = new FieldNavigationTarget(586, FieldNavigationCategory.Story, "up", 103, 486, 99,
                    TriggerLine: PagodaUp, CompletesOnArrival: true);
                Equal(beaten && floor < 4, planner.TryBuildRoute(floor == 0 ? courtyard : fromBelow, up, out _),
                    $"floor {floor}, beaten={beaten}: the stairs up are walkable exactly when offered");
            }
        }
    }

    // --- 484..511, the Cait Sith chase -------------------------------------------------------

    private static readonly (int Field, string Label, FieldNavigationTriggerLine? Line, int[]? Pads, int Destination)[] ChaseSteps =
    [
        (497, "Follow Cait Sith to the Battle Square platform", null, [26, 27], 499),
        (499, "Follow Cait Sith to the Speed Square", new(-477, -2539, -1139, -503, -2469, -1139), null, 486),
        (486, "Follow Cait Sith to the Wonder Square", new(467, -1320, -54, 370, -1378, -54), null, 505),
        (505, "Follow Cait Sith to the Chocobo Square", new(-195, -369, 0, -201, -317, 0), null, 509),
        (509, "Follow Cait Sith into the Ticket Office", new(-175, -875, -85, 53, -886, -85), null, 511),
        (484, "Go back to the Terminal Floor", new(2322, -3158, 256, 2057, -3189, 256), null, 497),
        (488, "Go back to the Terminal Floor", new(84, -445, 0, 379, -179, 0), null, 497),
        (491, "Go back to the Terminal Floor tube and press Confirm", new(208, 449, -93, 228, 1, -96), null, 497),
    ];

    private static void ChaseStoryFollowsTheTrailCaitSithIsSeenToTake()
    {
        foreach (var step in ChaseSteps)
        {
            var memory = new PuzzleMemory(step.Field);
            var reader = memory.StoryReader();
            var at = Position(step.Field, 0, 0, 0);
            foreach (var moment in new[] { 598, 599, 600 })
            {
                memory.SetGameMoment(moment);
                var target = Single(reader, at, step.Label);
                Equal(step.Line, target.TriggerLine, $"{step.Field} at {moment}: the line Cait Sith is seen to take");
                if (step.Pads is { } pads)
                {
                    Equal(string.Join(",", pads), string.Join(",", target.CompletionTriangles ?? []),
                        $"{step.Field}: the Battle Square tube is chekun's own pad pair");
                }
            }

            // Cornered in the corridor, or already the next morning: the trail says nothing.
            memory.SetGameMoment(598);
            memory.SetBankByte(3, 69, 0x01);
            Equal(false, reader.ReadTargets(at).Any(target => target.Label == step.Label), $"{step.Field}: nothing once he is cornered");
            memory.SetBankByte(3, 69, 0x1E);
            Equal(true, reader.ReadTargets(at).Any(target => target.Label == step.Label),
                $"{step.Field}: sightings seen or missed change nothing");
            memory.SetBankByte(3, 69, 0x00);
            memory.SetGameMoment(601);
            Equal(false, reader.ReadTargets(at).Any(target => target.Label == step.Label), $"{step.Field}: nothing the next morning");
            memory.SetGameMoment(595);
            Equal(false, reader.ReadTargets(at).Any(target => target.Label == step.Label), $"{step.Field}: nothing before the chase");
        }
    }

    private static void ChaseNativeEvidence()
    {
        var scripts = Scripts();
        // Where Cait Sith is seen to go, square by square.
        Equal(true, scripts.ReadScriptOpcodes(497, 8, 7).Any(op => Hex(op).StartsWith("C000D901550201", StringComparison.Ordinal) ||
                Hex(op).Contains("D90155022B00", StringComparison.Ordinal)),
            "gldgate's cait jumps to (473,597), triangle 43");
        Equal(true, scripts.ReadScriptOpcodes(497, 1, 0).Any(op => Hex(op).Contains("D901550", StringComparison.Ordinal)),
            "which is where cloud's Init lands the party out of the Battle Square tube");
        Equal(true, scripts.ReadScriptOpcodes(499, 7, 0).Any(op => Hex(op) == "A80019FE35F6"),
            "coloss's cait runs to (-487,-2507), the tube to the Speed Square");
        Equal(true, scripts.ReadScriptOpcodes(486, 4, 0).Any(op => Hex(op) == "A8009801B5FA"),
            "jet's cait runs to (408,-1355), the tube to the Wonder Square");
        Equal(true, scripts.ReadScriptOpcodes(505, 7, 1).Any(op => Hex(op).StartsWith("C00000EFFEA1FE5600", StringComparison.Ordinal)),
            "games's cait jumps to (-273,-351), triangle 86");
        AssertOpcode(scripts, 505, 22, 2, 4, [0x80, 0x50, 0x08, 0x07], "jp7 selects tube 7");
        Equal(true, scripts.ReadScriptOpcodes(509, 8, 15).Any(op => Hex(op) == "A800BCFFC1FB"),
            "chorace's cait runs to (-68,-1087), in front of the Ticket Office");
        var source = new FlevelDataSource(DataRoot!);
        foreach (var step in ChaseSteps.Where(step => step.Line is not null && step.Field != 505 && step.Field != 491))
        {
            Equal(true, NativeGateways(source, step.Field).Contains((step.Line!.Value, step.Destination)),
                $"{step.Field}'s trigger table has that gateway to {step.Destination}");
        }
    }

    private static void ChaseRoutesFromEachNativeArrival(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var scripts = Scripts();
        var source = new FlevelDataSource(DataRoot!);
        // A tube back to the Terminal Floor map jumps to (0,0); gldgate's own cloud Init then
        // places the party by the field it came from - (473,597) out of the Battle Square, and
        // so on - so those placements are its arrivals.
        var terminalPlacements = scripts.ReadScriptOpcodes(497, 1, 0)
            .Where(op => op.Opcode == 0xA5 && op.Bytes.Count == 11)
            .Select(op => op.Bytes.ToArray())
            .Select(b => (X: (int)BitConverter.ToInt16(b, 3), Y: (int)BitConverter.ToInt16(b, 5), Triangle: BitConverter.ToUInt16(b, 9)))
            .ToHashSet();
        foreach (var (field, from) in new[] { (497, 0), (499, 497), (486, 499), (505, 486), (509, 505), (484, 497), (488, 497), (491, 497) })
        {
            var mesh = createWalkmeshReader(field);
            var memory = new PuzzleMemory(field);
            memory.SetGameMoment(598);
            var reader = memory.StoryReader();
            var planner = new FieldWalkmeshRoutePlanner(mesh, transitionProvider: _ => scripts.ReadField(field).Transitions);
            var arrivals = (field == 497 ? terminalPlacements : NativeArrivals(scripts, source, from, field))
                .Where(arrival => arrival.Triangle != 0 || arrival.X != 0).ToArray();
            Equal(true, arrivals.Length > (field == 497 ? 6 : 0), $"{(field == 497 ? "every tube" : from)} lands the party in {field}");
            foreach (var (x, y, triangle) in arrivals)
            {
                var start = Arrival(mesh, field, x, y, triangle);
                var target = Single(reader, start, ChaseSteps.First(step => step.Field == field).Label);
                Equal(true, planner.TryBuildRoute(start, target, out _),
                    $"{field} from {from} at ({x},{y}) t{triangle}: {target.Label}: {planner.LastDiagnostic}");
            }
        }
    }

    // --- shared plumbing ------------------------------------------------------------------------

    private static FieldScriptNavigationCatalog Scripts() => new(DataRoot!);

    private static string Hex(FieldScriptOpcodeDefinition op) => Convert.ToHexString(op.Bytes.ToArray());

    private static void AssertOpcode(FieldScriptNavigationCatalog scripts, int field, int entity, int script, int offset,
        byte[] expected, string label)
    {
        var op = scripts.ReadScriptOpcodes(field, entity, script).FirstOrDefault(candidate => candidate.ByteIndex == offset);
        Equal(Convert.ToHexString(expected), op.Bytes is null ? "(none)" : Hex(op), label);
    }

    /// <summary>Gateway arrivals from a field's trigger table and MAPJUMP arrivals from its scripts.</summary>
    private static HashSet<(int X, int Y, ushort Triangle)> NativeArrivals(FieldScriptNavigationCatalog scripts,
        FlevelDataSource source, int from, int to)
    {
        var arrivals = new HashSet<(int, int, ushort)>();
        if (source.TryReadField(from, out var encoded))
        {
            var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var section = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
            for (var index = 0; index < 12; index++)
            {
                var at = section + 0x38 + index * 24;
                if (BitConverter.ToInt16(bytes, at + 18) == to)
                {
                    arrivals.Add((BitConverter.ToInt16(bytes, at + 12), BitConverter.ToInt16(bytes, at + 14),
                        BitConverter.ToUInt16(bytes, at + 16)));
                }
            }
        }

        foreach (var op in scripts.ReadAllScriptOpcodes(from).SelectMany(script => script.Opcodes)
                     .Where(op => op.Opcode == 0x60 && op.Bytes.Count == 10))
        {
            var b = op.Bytes.ToArray();
            if (BitConverter.ToUInt16(b, 1) == to)
            {
                arrivals.Add((BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5), BitConverter.ToUInt16(b, 7)));
            }
        }

        return arrivals;
    }

    private static HashSet<(FieldNavigationTriggerLine Line, int Destination)> NativeGateways(FlevelDataSource source, int field)
    {
        Equal(true, source.TryReadField(field, out var encoded), $"field {field} is in the archive");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var section = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
        var gateways = new HashSet<(FieldNavigationTriggerLine, int)>();
        for (var index = 0; index < 12; index++)
        {
            var at = section + 0x38 + index * 24;
            var destination = BitConverter.ToInt16(bytes, at + 18);
            if (destination >= 0)
            {
                gateways.Add((new FieldNavigationTriggerLine(
                    BitConverter.ToInt16(bytes, at), BitConverter.ToInt16(bytes, at + 2), BitConverter.ToInt16(bytes, at + 4),
                    BitConverter.ToInt16(bytes, at + 6), BitConverter.ToInt16(bytes, at + 8), BitConverter.ToInt16(bytes, at + 10)),
                    destination));
            }
        }

        return gateways;
    }

    private static FieldPositionSnapshot Arrival(FieldWalkmeshReader mesh, int field, int x, int y, ushort triangle)
    {
        var walkmesh = mesh.Read(Position(field)).Walkmesh
            ?? throw new InvalidOperationException($"field{field} walkmesh must be readable");
        return Position(field, x, y, triangle, (int)Math.Round(walkmesh.Triangles[triangle].GetCentroid().Z));
    }

    private static FieldPositionSnapshot Position(int field, int x = 0, int y = 0, ushort triangle = 0, int z = 0) =>
        new(FieldPositionReader.FieldModule, field, 0, x, y, z, triangle, 0);

    private static string Labels(FieldStoryTargetReader reader, FieldPositionSnapshot position) =>
        string.Join(" | ", reader.ReadTargets(position).Select(target => target.Label));

    /// <summary>The one Story step offered here, optionally checking its label.</summary>
    private static FieldNavigationTarget Single(FieldStoryTargetReader reader, FieldPositionSnapshot position, string? label)
    {
        var targets = reader.ReadTargets(position);
        Equal(1, targets.Count, $"field {position.FieldId}, triangle {position.TriangleId}: one Story step, got [{string.Join(" | ", targets.Select(t => t.Label))}]");
        if (label is not null)
        {
            Equal(label, targets[0].Label, $"field {position.FieldId}, triangle {position.TriangleId}");
        }

        return targets[0];
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }

    /// <summary>
    /// Just enough of the field's memory for the Story reader and the route planner: the saved
    /// banks, the three party slots, the models a Talk row resolves and the IDLCK bits.
    /// </summary>
    private sealed class PuzzleMemory
    {
        private const int FieldState = 0x03200000;
        private const int EventTable = 0x03300000;
        private readonly Dictionary<int, byte> bytes = [];
        private readonly Dictionary<int, (int X, int Y, int Z)> actors = [];
        private readonly HashSet<int> locked = [];

        public PuzzleMemory(int field)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = (byte)FieldPositionReader.FieldModule;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
            SetParty(0xFF, 0xFF, 0xFF);
            SetGameMoment(0);
            bytes[FieldPositionReader.AddressFieldNumModels] = 1;
        }

        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        public void SetBankByte(int bank, int address, byte value) => bytes[BankAddress(bank, address)] = value;

        /// <summary>4[226]w and 4[228]w, the two hands' hour words.</summary>
        public void SetHands(int longHour, int shortHour)
        {
            SetBankByte(3, 226, (byte)longHour);
            SetBankByte(3, 227, 0);
            SetBankByte(3, 228, (byte)shortHour);
            SetBankByte(3, 229, 0);
        }

        public void SetParty(int first, int second, int third)
        {
            SetBankByte(3, 9, (byte)first);
            SetBankByte(3, 10, (byte)second);
            SetBankByte(3, 11, (byte)third);
        }

        public void AddActor(int entityId, int x, int y, int z)
        {
            actors[entityId] = (x, y, z);
            bytes[FieldPositionReader.AddressFieldNumModels] = (byte)(actors.Count + 1);
        }

        public void SetLocked(IEnumerable<int> triangles)
        {
            locked.Clear();
            locked.UnionWith(triangles);
        }

        public bool IsLocked(int triangle) => locked.Contains(triangle);

        public FieldStoryTargetReader StoryReader() =>
            new(ReadInt32, ReadInt16, ReadByte, FieldStoryEventCatalog.CreateAllFields(), _ => true);

        public FieldWalkmeshRoutePlanner Planner(
            FieldWalkmeshReader mesh,
            IReadOnlyList<FieldScriptNavigationTransition>? transitions = null) =>
            new(mesh, new FieldBoundaryStateReader(ReadInt32, ReadByte, (_, _) => true),
                transitions is null ? null : _ => transitions);

        /// <summary>The same bytes as the guest address space the live exit guards read.</summary>
        public ILegacyAddressSpace AddressSpace => new GuestMemory(this);

        private sealed class GuestMemory : ILegacyAddressSpace
        {
            private readonly PuzzleMemory memory;

            public GuestMemory(PuzzleMemory memory) => this.memory = memory;

            public bool TryRead(uint virtualAddress, Span<byte> destination)
            {
                for (var index = 0; index < destination.Length; index++)
                {
                    destination[index] = memory.ReadByte((int)virtualAddress + index);
                }

                return true;
            }
        }

        private int ModelIndex(int entityId) =>
            actors.Keys.Order().Select((entity, index) => (entity, index)).FirstOrDefault(pair => pair.entity == entityId).index + 1;

        public byte ReadByte(int address)
        {
            var modelIds = FieldNavigationObjectReader.AddressFieldModelIdArray;
            if (address >= modelIds && address < modelIds + 256)
            {
                return actors.ContainsKey(address - modelIds) ? (byte)ModelIndex(address - modelIds) : (byte)0xFF;
            }

            var eventOffset = address - EventTable;
            if (eventOffset >= 0 && eventOffset < (actors.Count + 1) * FieldNavigationObjectReader.FieldEventDataStride)
            {
                return eventOffset % FieldNavigationObjectReader.FieldEventDataStride == FieldNavigationObjectReader.VisibilityOffset
                    ? (byte)1
                    : (byte)0;
            }

            var lockOffset = address - FieldState - FieldBoundaryStateReader.BoundaryBitsOffset;
            if (lockOffset is >= 0 and < FieldBoundaryStateReader.BoundaryByteCount)
            {
                byte value = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    if (locked.Contains(lockOffset * 8 + bit))
                    {
                        value |= (byte)(1 << bit);
                    }
                }

                return value;
            }

            return bytes.GetValueOrDefault(address);
        }

        public int ReadInt32(int address)
        {
            if (address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr)
            {
                return FieldState;
            }

            if (address == FieldNavigationObjectReader.AddressFieldEventDataPtr)
            {
                return EventTable;
            }

            var offset = address - EventTable;
            var model = offset / FieldNavigationObjectReader.FieldEventDataStride;
            if (offset < 0 || model < 1 || model > actors.Count)
            {
                return 0;
            }

            var actor = actors[actors.Keys.Order().ElementAt(model - 1)];
            return (offset % FieldNavigationObjectReader.FieldEventDataStride) switch
            {
                FieldNavigationObjectReader.PositionXOffset => actor.X * FieldNavigationObjectReader.ModelPositionFixedPointScale,
                FieldNavigationObjectReader.PositionYOffset => actor.Y * FieldNavigationObjectReader.ModelPositionFixedPointScale,
                FieldNavigationObjectReader.PositionZOffset => actor.Z * FieldNavigationObjectReader.ModelPositionFixedPointScale,
                _ => 0
            };
        }

        public short ReadInt16(int address) =>
            address >= EventTable && (address - EventTable) % FieldNavigationObjectReader.FieldEventDataStride is 0x72 or 0x74
                ? (short)40
                : (short)0;

        private static int BankAddress(int bank, int address) => bank switch
        {
            1 => FieldNavigationObjectReader.AddressFieldBankBase + address,
            3 => FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + address,
            5 => FieldNavigationObjectReader.AddressTemporaryFieldBankBase + address,
            11 => FieldNavigationObjectReader.AddressFieldBankBase + 0x200 + address,
            13 => FieldNavigationObjectReader.AddressFieldBankBase + 0x300 + address,
            15 => FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + address,
            _ => throw new ArgumentOutOfRangeException(nameof(bank))
        };
    }
}
