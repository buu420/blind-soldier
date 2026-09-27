using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Corel Valley Cave (628 sandun_1): the vines are LINEs whose Move asks the leader to run one
/// of its climbing routines. Each routine climbs (LADER) or jumps (JUMP) to a triangle, reads
/// the leader's position (PXYZI into Bank[6][0]), and compares it with junction triangles
/// dir's Init stores once (Bank[6][9] = 179, [11] = 180, ...). Only on a junction does it ask
/// "right / left" or "up / down" (ASK into Bank[5][2]) and jump to the side chosen.
///
/// <para>The catalog used to take both sides of every one of those comparisons, so it listed
/// landings the game never leaves the party on (the junction itself) and landings it never
/// reaches that way (e5 and e6 "climbing down to" the top platforms), and a route through them
/// sent the tester back and forth between triangles 132 and 179. The comparison is decided
/// only where it is proven: the position was just set by the leader's own copy of the routine
/// from its own landing, and the junction is a word dir's Init sets once, unconditionally, that
/// nothing else in the field writes. Anything short of that keeps both sides.</para>
/// </summary>
internal static class CorelValleyCaveTests
{
    private const int CaveFieldId = 628;

    internal static void Run(Func<int, FieldWalkmeshReader>? createWalkmeshReader, string? dataRoot)
    {
        AProvenJunctionIsDecidedAndItsAnswerIsRequired();
        AClimbThatEndsOffTheJunctionNeverAsks();
        TheWrongActorsPositionDecidesNothing();
        AJunctionWrittenElsewhereDecidesNothing();
        AConditionalInitDecidesNothing();
        MovementBetweenTheLandingAndTheReadDecidesNothing();
        EveryLeadersCopyKeepsItsLadder();
        if (dataRoot is null || createWalkmeshReader is null)
        {
            return;
        }

        CosTopLadderKeepsAllThreeLeaders(dataRoot);

        TheCaveVinesEndWhereTheirScriptsDo(dataRoot);
        TheCaveRoutesUseOnlyRealClimbs(createWalkmeshReader, dataRoot);
        TheLadderPromptNamesTheAnswer(createWalkmeshReader, dataRoot);
    }

    // --- synthetic vines ----------------------------------------------------------------

    private const int Junction = 50;
    private const int OtherJunction = 51;
    private const int Bottom = 60;
    private const int RightLanding = 131;
    private const int LeftLanding = 134;
    private const int ChoiceDialog = 25;

    /// <summary>
    /// A vine like sandun_1's lin0: its Move asks slot 0 for script 3, which climbs to the
    /// junction, reads the leader's position and, on a junction, asks and jumps.
    /// </summary>
    private static SyntheticFieldScript Vine(
        Action<SyntheticScriptCode>? dirInit = null,
        Action<SyntheticScriptCode>? climb = null,
        int positionOf = 0,
        Action<SyntheticFieldScript>? more = null)
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit");
        (dirInit ?? (init => init.SetWord(6, 9, Junction).SetWord(6, 11, OtherJunction)))(code);
        code.Ret().Label("dirMain").Ret();
        code.Label("heroInit").Char(0).Pc(0).Ret().Label("heroMain").Ret().Label("heroIdle").Ret();
        code.Label("heroClimb");
        (climb ?? (routine => routine.Ladder(124, -233, -574, Junction, 1)))(code);
        code.Uc(1)
            .PartyPosition(positionOf, 6, 3, 5, 7, 0)
            .IfWordVariable(true, 6, 0, 6, 9, 0, "notFirst")
            .JmpF("ask")
            .Label("notFirst")
            .IfWordVariable(true, 6, 0, 6, 11, 0, "done")
            .Label("ask")
            .Ask(ChoiceDialog, 0, 1, 2)
            .IfUb(5, 2, 0, 0, 0, "notRight")
            .FieldJump(278, -360, RightLanding, 15)
            .Label("notRight")
            .IfUb(5, 2, 0, 1, 0, "done")
            .FieldJump(21, -261, LeftLanding, 15)
            .Label("done")
            .Uc(0)
            .Ret();
        code.Label("lineInit").Line(100, -256, -866, 183, -256, -883).Ret().Label("lineMain").Ret();
        code.Label("lineOk").Ret().Label("lineMove").Preq(0, 3).Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("cl", (0, "heroInit"), (1, "heroIdle"), (3, "heroClimb"));
        field.Entity("lin0", (0, "lineInit"), (1, "lineOk"), (2, "lineMove"));
        more?.Invoke(field);
        return field;
    }

    private static FieldScriptNavigationTransition[] VineTransitions(SyntheticFieldScript field) =>
        field.Read(CaveFieldId).Transitions.Where(transition => transition.SourceEntityId == 2).ToArray();

    private static string Landings(IEnumerable<FieldScriptNavigationTransition> transitions) =>
        string.Join(",", transitions.Select(transition => transition.TargetTriangle).Distinct().Order());

    private static void AProvenJunctionIsDecidedAndItsAnswerIsRequired()
    {
        var transitions = VineTransitions(Vine());
        Equal($"{RightLanding},{LeftLanding}", Landings(transitions),
            "the junction is not a landing: the routine always asks there and jumps");
        var right = transitions.Single(transition => transition.TargetTriangle == RightLanding);
        var left = transitions.Single(transition => transition.TargetTriangle == LeftLanding);
        Equal((ChoiceDialog, 0), (right.RequiredChoice?.DialogId ?? -1, right.RequiredChoice?.Line ?? -1),
            "the right-hand landing needs the first answer of dialog 25");
        Equal((ChoiceDialog, 1), (left.RequiredChoice?.DialogId ?? -1, left.RequiredChoice?.Line ?? -1),
            "the left-hand landing needs the second");
        Equal(FieldNavigationInput.Up, right.RequiredInput, "and both are still the climb up the vine");
    }

    private static void AClimbThatEndsOffTheJunctionNeverAsks()
    {
        var transitions = VineTransitions(Vine(climb: routine => routine
            .FieldJump(124, -233, Junction, 15)
            .Ladder(162, -309, -878, Bottom, 0)));
        Equal($"{Bottom}", Landings(transitions), "climbing down ends at the bottom, which is no junction");
        Equal(true, transitions.All(transition => transition.RequiredChoice is null), "and nothing is asked there");
    }

    private static void TheWrongActorsPositionDecidesNothing()
    {
        var transitions = VineTransitions(Vine(positionOf: 1));
        Equal(true, transitions.Any(transition => transition.TargetTriangle == Junction),
            "slot 1's position says nothing about where slot 0's copy climbed: both sides are kept");
    }

    private static void AJunctionWrittenElsewhereDecidesNothing()
    {
        var transitions = VineTransitions(Vine(more: field =>
        {
            field.Code.Label("otherInit").Ret().Label("otherMain").SetWord(6, 9, 99).Ret();
            field.Entity("other", (0, "otherInit"));
        }));
        Equal(true, transitions.Any(transition => transition.TargetTriangle == Junction),
            "a junction some other code can rewrite is not a constant: both sides are kept");
    }

    private static void AConditionalInitDecidesNothing()
    {
        var transitions = VineTransitions(Vine(dirInit: init => init
            .IfUb(5, 40, 0, 1, 0, "skip")
            .SetWord(6, 9, Junction)
            .Label("skip")
            .SetWord(6, 11, OtherJunction)));
        Equal(true, transitions.Any(transition => transition.TargetTriangle == Junction),
            "a junction Init may not have stored is not a constant: both sides are kept");
    }

    private static void MovementBetweenTheLandingAndTheReadDecidesNothing()
    {
        var transitions = VineTransitions(Vine(climb: routine => routine
            .Ladder(124, -233, -574, Junction, 1)
            .Raw(0xA8, 0, 0x10, 0x00, 0x20, 0x00)));
        Equal(true, transitions.Any(transition => transition.TargetTriangle == Junction),
            "a walk after the landing leaves the position unknown: both sides are kept");
    }

    /// <summary>
    /// cos_top (540) in miniature: a LINE asks slot 0 for script 4, and Cloud, Tifa and Cid each
    /// have the same copy - LADER to 122, read the leader's triangle (PXYZI slot 0), and MAPJUMP
    /// when it is 122. Deciding that test must not decide who can lead: the ladder is still every
    /// copy's, each with its own leader.
    /// </summary>
    private static void EveryLeadersCopyKeepsItsLadder()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        foreach (var (name, character) in new[] { ("cl", 0), ("ti", 2), ("cid", 8) })
        {
            code.Label($"{name}Init").Char(character == 0 ? 0 : character == 2 ? 1 : 2).Pc(character).Ret()
                .Label($"{name}Main").Ret().Label($"{name}Idle").Ret()
                .Label($"{name}Climb")
                .Ladder(-77, -559, -640, 122, 0)
                .PartyPosition(0, 6, 2, 4, 6, 8)
                .IfWord(true, 6, 8, 122, 0, $"{name}Stay")
                .MapJump(531)
                .Label($"{name}Stay")
                .Ret();
        }

        code.Label("lineInit").Line(9, -469, -544, -164, -516, -540).Ret().Label("lineMain").Ret();
        code.Label("lineOk").Ret().Label("lineGo").PrqEw(0, 4).Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("cl", (0, "clInit"), (1, "clIdle"), (4, "clClimb"));
        field.Entity("ti", (0, "tiInit"), (1, "tiIdle"), (4, "tiClimb"));
        field.Entity("cid", (0, "cidInit"), (1, "cidIdle"), (4, "cidClimb"));
        field.Entity("line", (0, "lineInit"), (1, "lineOk"), (4, "lineGo"));
        var ladders = field.Read(540).Transitions.Where(transition => transition.SourceEntityId == 4 && transition.TargetTriangle == 122).ToArray();
        Equal(1, ladders.Length, "one ladder down, whoever leads: " + string.Join(" | ", ladders.Select(transition =>
            $"{transition.StableId} movers={string.Join(",", transition.MoverEntityIds ?? [])} conditions={string.Join(";", transition.Conditions?.Select(condition => condition.Key) ?? [])}")));
        var ladder = ladders[0];
        Equal("ladder:540:4:1:4:122", ladder.StableId, "published once, as the first copy's");
        Equal("1,2,3", string.Join(",", ladder.MoverEntityIds ?? []), "each of the three copies moves its own model down the ladder");
        Equal("1:0;2:2;3:8", string.Join(";", ladder.Conditions?.Select(condition => condition.Key) ?? []),
            "and each is the player's while its own character leads");
    }

    private static void CosTopLadderKeepsAllThreeLeaders(string dataRoot)
    {
        var ladder = new FieldScriptNavigationCatalog(dataRoot).ReadField(540).Transitions
            .Single(transition => transition.StableId == "ladder:540:6:2:4:122");
        Equal("2,3,4", string.Join(",", ladder.MoverEntityIds ?? []), "cos_top: Cloud, Tifa and Cid each climb down");
        Equal("2:0;3:2;4:8", string.Join(";", ladder.Conditions?.Select(condition => condition.Key) ?? []),
            "cos_top: each while leading");
    }

    // --- the installed cave -------------------------------------------------------------

    private static int[] InstalledJunctions(FieldScriptNavigationCatalog catalog) =>
        catalog.ReadScriptOpcodes(CaveFieldId, 0, 0)
            .Where(op => op.Opcode == 0x81 && op.Bytes.Count >= 5 && op.Bytes[1] == 0x60)
            .Select(op => op.Bytes[3] | (op.Bytes[4] << 8))
            .ToArray();

    private static void TheCaveVinesEndWhereTheirScriptsDo(string dataRoot)
    {
        var catalog = new FieldScriptNavigationCatalog(dataRoot);
        var junctions = InstalledJunctions(catalog);
        Equal(true, junctions.Contains(179) && junctions.Contains(180), $"dir's Init stores the junctions ({string.Join(",", junctions)})");
        var transitions = catalog.ReadField(CaveFieldId).Transitions;
        var onJunction = transitions.Where(transition => junctions.Contains(transition.TargetTriangle)).Select(transition => transition.StableId).ToArray();
        Equal(string.Empty, string.Join(" ", onJunction), "no traversal ends on a junction: the routine always goes on from one");

        var lin0 = transitions.Where(transition => transition.SourceEntityId == 4).ToArray();
        Equal("131,134", Landings(lin0), "lin0 climbs to the junction and jumps right or left");
        Equal("right", lin0.Single(transition => transition.TargetTriangle == 131).RequiredChoice?.Text, "the right-hand ledge is the answer 'right'");
        Equal("left", lin0.Single(transition => transition.TargetTriangle == 134).RequiredChoice?.Text, "the left-hand ledge is 'left'");
        foreach (var entity in new[] { 5, 6 })
        {
            var down = transitions.Where(transition => transition.SourceEntityId == entity).ToArray();
            Equal("171", Landings(down), $"lin{entity - 4} climbs down to the bottom, and only there");
            Equal(true, down.All(transition => transition.RequiredChoice is null), $"lin{entity - 4} asks nothing");
        }

        Equal(true, transitions.Where(transition => transition.RequiredChoice is not null)
                .All(transition => (transition.RequiredChoice?.Text ?? "?").Split(", then ")
                    .All(answer => answer is "right" or "left" or "up" or "down")),
            "every answer a vine needs is one of the game's own choices");
        var twoQuestions = transitions.Single(transition => transition.StableId == "ladder:628:11:1:8:44");
        Equal(("up, then right", 2), (twoQuestions.RequiredChoice?.Text, twoQuestions.RequiredChoice?.Answers ?? 0),
            "a vine that asks twice needs both answers, in order");
    }

    private static void TheCaveRoutesUseOnlyRealClimbs(Func<int, FieldWalkmeshReader> createWalkmeshReader, string dataRoot)
    {
        var catalog = new FieldScriptNavigationCatalog(dataRoot);
        var read = catalog.ReadField(CaveFieldId);
        var junctions = InstalledJunctions(catalog);
        var mesh = createWalkmeshReader(CaveFieldId);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, CaveFieldId, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var planner = new FieldWalkmeshRoutePlanner(mesh, null, _ => read.Transitions);
        FieldPositionSnapshot At(int x, int y, int triangle) => new(FieldPositionReader.FieldModule, CaveFieldId, 0, x, y,
            FieldCrossFieldApproachResolver.SurfaceZ(walkmesh.Triangles[triangle], x, y), (ushort)triangle, 0);
        FieldPositionSnapshot Centre(int triangle)
        {
            var centre = walkmesh.Triangles[triangle].GetCentroid();
            return At((int)Math.Round(centre.X), (int)Math.Round(centre.Y), triangle);
        }

        var fromValley = At(885, -599, 168);
        var fromNorth = At(483, 400, 6);
        var north = new FieldNavigationTarget(CaveFieldId, FieldNavigationCategory.Story, "north exit", 357, 764, 1205,
            TriggerLine: new FieldNavigationTriggerLine(303, 764, 1205, 411, 764, 1205), CompletesOnArrival: true);
        var back = new FieldNavigationTarget(CaveFieldId, FieldNavigationCategory.Exits, "exit to the valley", 996, -580, -939,
            TriggerLine: new FieldNavigationTriggerLine(998, -459, -946, 995, -702, -932), CompletesOnArrival: true);
        var chests = new List<(string Name, FieldNavigationTarget Target)>();
        foreach (var entity in new[] { 22, 23, 24, 25 })
        {
            var init = catalog.ReadScriptOpcodes(CaveFieldId, entity, 0);
            var place = init.First(op => op.Opcode == 0xA5).Bytes.ToArray();
            chests.Add(($"treasure e{entity}", new FieldNavigationTarget(CaveFieldId, FieldNavigationCategory.Objects, $"treasure e{entity}",
                BitConverter.ToInt16(place, 3), BitConverter.ToInt16(place, 5), BitConverter.ToInt16(place, 7),
                InteractionRadius: FieldNavigationObjectReader.DefaultInteractionRadius)));
        }

        var legs = new List<(string Name, FieldPositionSnapshot Start, FieldNavigationTarget Target)>
        {
            ("the whole way north from the valley", fromValley, north),
            ("back to the valley from the north", fromNorth, back),
            ("north from the right-hand ledge", Centre(132), north)
        };
        foreach (var (name, target) in chests)
        {
            legs.Add(($"{name} from the valley", fromValley, target));
            legs.Add(($"{name} from the north", fromNorth, target));
        }

        foreach (var (name, start, target) in legs)
        {
            Equal(true, planner.TryBuildRoute(start, target, out var plan), $"{name}: a route ({planner.LastDiagnostic})");
            foreach (var portal in plan.Portals.Where(portal => portal.TransitionKind is not null))
            {
                var transition = read.Transitions.Single(candidate => candidate.StableId == portal.TransitionId);
                Equal(false, junctions.Contains(transition.TargetTriangle), $"{name}: {portal.TransitionId} does not end on a junction");
                Equal(transition.TargetTriangle, portal.ToTriangle, $"{name}: {portal.TransitionId} lands where its script does");
                Equal(transition.RequiredChoice?.Text, portal.RequiredChoice?.Text, $"{name}: {portal.TransitionId} carries its answer");
            }
        }
    }

    private static void TheLadderPromptNamesTheAnswer(Func<int, FieldWalkmeshReader> createWalkmeshReader, string dataRoot)
    {
        var catalog = new FieldScriptNavigationCatalog(dataRoot);
        var read = catalog.ReadField(CaveFieldId);
        var mesh = createWalkmeshReader(CaveFieldId);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, CaveFieldId, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var planner = new FieldWalkmeshRoutePlanner(mesh, null, _ => read.Transitions);
        var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, CaveFieldId, 0, 885, -599,
            FieldCrossFieldApproachResolver.SurfaceZ(walkmesh.Triangles[168], 885, -599), 168, 0);
        var north = new FieldNavigationTarget(CaveFieldId, FieldNavigationCategory.Story, "Take the way north out of the cave", 357, 764, 1205,
            TriggerLine: new FieldNavigationTriggerLine(303, 764, 1205, 411, 764, 1205), CompletesOnArrival: true);
        Equal(true, planner.TryBuildRoute(start, north, out var plan), $"the way north ({planner.LastDiagnostic})");
        var choosing = plan.Portals.First(portal => portal.RequiredChoice is not null);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([], storyTargetProvider: _ => [north]), planner);
        var transform = new FieldNavigationControlTransform(0);
        for (var index = 0; index < 5 && controller.CurrentCategory != FieldNavigationCategory.Story; index++)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        }

        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Equal(true, controller.BeaconEnabled, $"navigation starts ({controller.LastNavigationDiagnostic})");
        var at = choosing.Midpoint;
        var foot = new FieldPositionSnapshot(FieldPositionReader.FieldModule, CaveFieldId, 0, at.X, at.Y, at.Z, (ushort)choosing.FromTriangle, 0);
        var spoken = new List<string>();
        var clock = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        foreach (var position in new[] { start, foot, foot })
        {
            if (controller.UpdateLiveTracking(position, new(0, FieldNavigationInput.None), transform, false, 80, observedAt: clock) is { } result)
            {
                spoken.Add(result.Speech);
            }

            clock = clock.AddSeconds(2);
        }

        Equal(true, spoken.Any(text => text.EndsWith($". Answer {choosing.RequiredChoice?.Text}.", StringComparison.Ordinal)),
            $"at the vine the prompt names the answer the route needs ({string.Join(" | ", spoken)})");

        // The game climbs, asks and jumps; the player answers its window. Where the answer lands
        // them, the same route goes on to the same destination, and auto walk with it.
        var landing = choosing.TransitionExit ?? choosing.Midpoint;
        var landed = new FieldPositionSnapshot(FieldPositionReader.FieldModule, CaveFieldId, 0, landing.X, landing.Y,
            FieldCrossFieldApproachResolver.SurfaceZ(walkmesh.Triangles[choosing.ToTriangle], landing.X, landing.Y),
            (ushort)choosing.ToTriangle, 0);
        var after = controller.UpdateLiveTracking(landed, new(0, FieldNavigationInput.None), transform, false, 80, observedAt: clock);
        Equal(true, controller.BeaconEnabled, $"the route to the north is kept after the climb ({after?.Speech})");
        Equal(false, after?.Speech.Contains("reached", StringComparison.Ordinal) == true, "and it is not taken for arriving");
        Equal(true, controller.TryResolveAutomaticInput(landed, transform, 80, out var onward) && onward != FieldNavigationInput.None,
            $"auto walk goes on from the ledge ({controller.LastAutomaticInputHold}, {controller.LastNavigationDiagnostic})");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Corel Valley Cave: {label}: expected {expected}, got {actual}.");
        }
    }
}
