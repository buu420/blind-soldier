using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Temple of the Ancients doorway chase (610 kuro_7) and the Bone Village dig (772
/// bonevil2), where a tester found nothing to navigate to. Every expectation comes from the
/// installed field scripts where the data root is available; the readout cases run
/// everywhere.
///
/// <para>kuro_7: nine door LINEs, border1..border9 (entities 6..14), each of whose Go 1x runs
/// one of cloud's scripts 6..14 and places the party at another door's mouth; two [OK] ledges,
/// jump1 and jump2 (16, 17); the locked doorway, mapjump (15). keyman (27) is the guard. The
/// door he will come out of next is Bank[5][18]; that is the puzzle and is never read.</para>
///
/// <para>bonevil2: the phase is Bank[5][12], 7 at Init, one step down per digger ordered, 2 for
/// the blast and 1 for choosing the dig point. The diggers are entities 14..18, each placed
/// where the party stands; an unplaced one stands at its Init spot (-427, -5). luna (6) and the
/// boxes (7..13) are what is buried. The readout does not observe them; the one the diggers are
/// sent to is the dig spot in Objects, at the user's request (BoneVillageDigSpotTests).</para>
/// </summary>
internal static class ChaseAndExcavationTests
{
    internal static void Run(Func<int, FieldWalkmeshReader>? createWalkmeshReader = null)
    {
        ChaseGuardIsToldAsHeIsSeen();
        ChaseTellsWhereThePartyComesOut();
        ChaseSaysNothingWhileNothingChanges();
        ChaseRepeatGivesFloorsAndGuard();
        ChaseLedgeStillAsksForConfirm();
        ExcavationPhaseComesFromTheGame();
        ExcavationSaysEachDiggerPlaced();
        ExcavationLinesOfSightOnlyAfterTheBlast();
        ExcavationUnreadableSightDoesNotInventMovement();
        ExcavationReadoutDoesNotObserveTheBuriedModels();
        ExcavationDiggerTargetsAreOnlyThePlacedOnes();
        ExcavationWaitsForADiggerToArrive();
        ExcavationDoesNotRepeatAfterATornLook();
        ExcavationSaysEachPhaseBriefly();
        ExcavationRepeatGivesLevelsAndSightlines();
        ExcavationWaitsForTheGameToFinishSendingHim();
        ExcavationSettleIncludesHeight();
        ExcavationLadderSaysConfirm();
        BoneVillageDigSpotTests.Run(createWalkmeshReader, DataRoot);
        BoneVillageForemanTests.Run(createWalkmeshReader, DataRoot);
        CorelValleyCaveTests.Run(createWalkmeshReader, DataRoot);
        if (DataRoot is null || createWalkmeshReader is null)
        {
            return;
        }

        ChaseLayoutIsTheNativeScripts();
        ChaseDoorsAreExitsInLeftToRightOrder();
        ChaseEveryDoorRoutesFromEveryFloor(createWalkmeshReader);
        ChaseLinesAnchorToTheirOwnFloor(createWalkmeshReader);
        ExcavationLadderAndDiggerDefinitionsAreNative();
        ChaseTargetsStartRealNavigation(createWalkmeshReader);
        ExcavationTargetsStartRealNavigation(createWalkmeshReader);
    }

    private static string? DataRoot => Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") is { Length: > 0 } root
        ? root
        : null;

    private static readonly DateTime Epoch = new(2026, 9, 26, 17, 0, 0, DateTimeKind.Utc);

    // --- 610 kuro_7 -----------------------------------------------------------------------

    private static FieldActivityModelReading Guard(int x, int y, int z) =>
        new(27, FieldActivityReadStatus.Visible, new FieldActivityModelState(true, true, x, y, z, 0, 0));

    private static FieldActivityModelReading GuardHidden() =>
        new(27, FieldActivityReadStatus.Hidden, default);

    private static FieldActivityObservation Chase(
        int playerX, int playerY, int playerZ, FieldActivityModelReading guard, bool controlled = true) =>
        new(610, playerX, playerY, playerZ, 0, controlled, 612, new FieldNavigationControlTransform(0), true,
            [guard], new Dictionary<int, FieldActivityWaitState>(), _ => true, null, 0);

    private static string Said(FieldActivityReadout readout, FieldActivityObservation observation, double seconds) =>
        readout.Observe(observation, Epoch.AddSeconds(seconds)).Speech ?? string.Empty;

    /// <summary>
    /// keyman's script 4 with Bank[5][18] = 8, as the player sees it: out of lower door 4's
    /// mouth, back in, then out of the doorway from the clock room, down the upper floor and
    /// into the upper floor door. Each appearance and disappearance is said once, by the door
    /// it happens at; nothing is said about where he will be next.
    /// </summary>
    private static void ChaseGuardIsToldAsHeIsSeen()
    {
        var readout = new FieldActivityReadout();
        // The party stands at the middle floor, having gone into middle floor door 1.
        Equal(string.Empty, Said(readout, Chase(-688, 440, 0, GuardHidden()), 0), "no guard, nothing to say");
        Equal("The guard came out of lower floor door 4.", Said(readout, Chase(-688, 440, 0, Guard(566, 3, -433)), 1),
            "he comes out of the hole he is placed at");
        Equal(string.Empty, Said(readout, Chase(-688, 440, 0, Guard(566, -125, -433)), 1.5), "walking out is not news");
        Equal("The guard went into lower floor door 4.", Said(readout, Chase(-688, 440, 0, GuardHidden()), 2.5),
            "and back in the way he came");
        Equal("The guard came out of the doorway from the clock room.", Said(readout, Chase(-688, 440, 0, Guard(483, 614, 322)), 3),
            "the upper floor's way in");
        Equal(string.Empty, Said(readout, Chase(-688, 440, 0, Guard(226, 314, 322)), 4), "running along the upper floor");
        Equal(string.Empty, Said(readout, Chase(-688, 440, 0, Guard(-590, 777, 370)), 5), "to the upper floor door");
        Equal("The guard went into the upper floor door.", Said(readout, Chase(-688, 440, 0, GuardHidden()), 6),
            "the last place he was seen");

        // keyman script 4 with Bank[5][18] = 5: out of middle floor door 1, along the middle
        // floor, a jump down (makeObjectJump to (-218, -159) on the lower floor) and in.
        var dropping = new FieldActivityReadout();
        _ = Said(dropping, Chase(0, 0, 0, GuardHidden()), 0);
        Equal("The guard came out of middle floor door 1.", Said(dropping, Chase(0, 0, 0, Guard(-673, 275, -6)), 1),
            "keyman at 5[18] = 5 comes out of middle floor door 1's mouth");
        _ = Said(dropping, Chase(0, 0, 0, Guard(-164, -51, -6)), 2);
        Equal("The guard dropped to the lower floor.", Said(dropping, Chase(0, 0, 0, Guard(-218, -159, -392)), 3),
            "his jump down is seen");
        Equal("The guard went into lower floor door 2.", Said(dropping, Chase(0, 0, 0, GuardHidden()), 4.5),
            "keyman walks to (-218, 197) on the lower floor and is gone");
    }

    /// <summary>
    /// Going through a door puts the party at another door's mouth (cloud scripts 6..14). A
    /// sighted player sees where they come out; that is said, once.
    /// </summary>
    private static void ChaseTellsWhereThePartyComesOut()
    {
        var readout = new FieldActivityReadout();
        _ = Said(readout, Chase(-605, 790, 369, GuardHidden()), 0);
        Equal("You came out of middle floor door 3.", Said(readout, Chase(139, 189, -8, GuardHidden()), 1),
            "the upper floor door leads to middle floor door 3 (cloud script 6)");
        Equal(string.Empty, Said(readout, Chase(139, 160, -8, GuardHidden()), 1.5), "walking out of it is not news");
        // jump1's makeObjectJump lands on the middle floor at (-549, 29), at no door's mouth but
        // right on the end of jump2's own line (-562, 45): the next ledge down is underfoot.
        var ledge = new FieldActivityReadout();
        _ = Said(ledge, Chase(-581, 200, 370, GuardHidden()), 0);
        Equal("You are on the middle floor. At the ledge. Press Confirm to jump down to the lower floor.",
            Said(ledge, Chase(-549, 29, 0, GuardHidden()), 1), "the jump down is a floor change, onto the next ledge");
    }

    private static void ChaseSaysNothingWhileNothingChanges()
    {
        var readout = new FieldActivityReadout();
        _ = Said(readout, Chase(0, 0, 0, Guard(-612, 529, 361)), 0);
        for (var second = 1; second <= 40; second++)
        {
            Equal(string.Empty, Said(readout, Chase(second * 2, 0, 0, Guard(-612, 529, 361)), second),
                $"a guard standing still at {second}s is not repeated");
        }
    }

    private static void ChaseRepeatGivesFloorsAndGuard()
    {
        var readout = new FieldActivityReadout();
        var seen = Chase(-230, 190, -2, Guard(179, 22, -396));
        _ = readout.Observe(seen, Epoch);
        Equal(
            "You are on the middle floor, at middle floor door 2. The guard is on the lower floor, at lower floor door 3. " +
            "Doors are numbered from the left on each floor; the Exits list has every door and ledge.",
            readout.Describe(seen),
            "the repeat says where the party is and where the guard is seen");
        Equal(
            "You are on the lower floor. The guard is not in sight. " +
            "Doors are numbered from the left on each floor; the Exits list has every door and ledge.",
            new FieldActivityReadout().Describe(Chase(0, -300, -390, GuardHidden())),
            "and that the guard is not in sight when he is not");
    }

    /// <summary>
    /// jump1 and jump2 jump only on Confirm on their own lines; the prompt names where they go.
    /// A ledge is somewhere the party may jump from, not a wait the field is stopped in: the
    /// party can walk on, pick another target and auto walk away from it. Both hosts treat a
    /// pending activity as owning all input, so the ledge must not be pending.
    /// </summary>
    private static void ChaseLedgeStillAsksForConfirm()
    {
        var readout = new FieldActivityReadout();
        var onLedge = Chase(-581, 200, 370, GuardHidden());
        var cue = readout.Observe(onLedge, Epoch);
        Equal("At the ledge. Press Confirm to jump down to the middle floor.", cue.Speech, "jump1");
        Equal(false, cue.IsPending, "the ledge does not take over the player's input");
        Equal(false, readout.IsWaitingForInput(onLedge), "and the field is not waiting on it");
        Equal(null, readout.Observe(onLedge, Epoch.AddSeconds(20)).Speech, "said once, not on a repeating beat");
        Equal("At the ledge. Press Confirm to jump down to the lower floor.",
            new FieldActivityReadout().Observe(Chase(-614, 62, -10, GuardHidden()), Epoch).Speech, "jump2");
        Equal(false, new FieldActivityReadout().Observe(Chase(-581, 200, 370, GuardHidden(), controlled: false), Epoch).IsPending,
            "no button is named while the party is being moved for them");
    }

    // --- 610, installed data --------------------------------------------------------------

    private static FieldScriptNavigationCatalog Catalog() => new(DataRoot!);

    private static FieldNavigationTriggerLine NativeLine(FieldScriptNavigationCatalog catalog, int entity)
    {
        var opcode = catalog.ReadScriptOpcodes(610, entity, 0).First(op => op.Opcode == 0xD0);
        short S(int offset) => BitConverter.ToInt16(opcode.Bytes.ToArray(), offset);
        return new FieldNavigationTriggerLine(S(1), S(3), S(5), S(7), S(9), S(11));
    }

    /// <summary>The layout the readout and the exits use is the installed field's own.</summary>
    private static void ChaseLayoutIsTheNativeScripts()
    {
        var catalog = Catalog();
        var transitions = catalog.ReadField(610).Transitions;
        foreach (var door in TempleChaseLayout.Doors)
        {
            Equal(NativeLine(catalog, door.EntityId), door.Line, $"door e{door.EntityId}'s LINE");
            // The door that leads into this one places the party at this one's mouth.
            var leadingIn = transitions.Where(t => t.Kind == FieldNavigationTransitionKind.Jump &&
                                                   t.TargetX == door.MouthX && t.TargetY == door.MouthY &&
                                                   t.TargetZ == door.MouthZ).ToArray();
            Equal(1, leadingIn.Length, $"exactly one door leads to e{door.EntityId}'s mouth");
            Equal(door.EntityId, TempleChaseLayout.Doors.Single(o => o.EntityId == leadingIn[0].SourceEntityId).LeadsToEntityId,
                $"the pairing recorded for e{leadingIn[0].SourceEntityId}");
        }

        foreach (var ledge in TempleChaseLayout.Ledges)
        {
            Equal(NativeLine(catalog, ledge.EntityId), ledge.Line, $"ledge e{ledge.EntityId}'s LINE");
            Equal(true, transitions.Any(t => t.SourceEntityId == ledge.EntityId && t.RequiresAction),
                $"ledge e{ledge.EntityId} is an [OK] jump");
        }

        Equal(true, catalog.ReadAllScriptOpcodes(610).Where(s => s.EntityId == 27).SelectMany(s => s.Opcodes)
                .Any(op => Convert.ToHexString(op.Bytes.ToArray()) == "82 30 E9 03".Replace(" ", "")),
            "keyman's catch sets 3[233] bit 3");
    }

    private static void ChaseDoorsAreExitsInLeftToRightOrder()
    {
        var exits = Catalog().ReadField(610).Exits;
        var labels = new FieldExitLabelResolver(_ => FieldMapNameResolution.Unknown, () => "Temple of the Ancients").Resolve(exits);
        foreach (var door in TempleChaseLayout.Doors)
        {
            var exit = labels.Single(target => target.TriggerLine == door.Line);
            Equal(door.Label, exit.Label, $"door e{door.EntityId}'s exit");
            Equal(FieldNavigationCategory.Exits, exit.Category, $"e{door.EntityId} is an exit");
            Equal(true, exit.CompletesOnArrival, $"e{door.EntityId} is done by crossing");
        }

        foreach (var ledge in TempleChaseLayout.Ledges)
        {
            Equal(ledge.Label, labels.Single(target => target.TriggerLine == ledge.Line).Label, $"ledge e{ledge.EntityId}");
        }

        foreach (var floor in TempleChaseLayout.Doors.GroupBy(door => door.Floor))
        {
            var ordered = floor.OrderBy(door => (door.Line.StartX + door.Line.EndX) / 2).Select(door => door.Number).ToArray();
            Equal(string.Join(",", Enumerable.Range(1, ordered.Length)), string.Join(",", ordered),
                $"{floor.Key} floor doors are numbered from the left (the camera's x axis is the field's)");
        }
    }

    /// <summary>
    /// Every door and ledge is a route from every floor with the field's own transitions, and
    /// every route ends on the door's own floor: no line is anchored to the floor above it.
    /// </summary>
    private static void ChaseEveryDoorRoutesFromEveryFloor(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var catalog = Catalog();
        var mesh = createWalkmeshReader(610);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, 610, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var transitions = catalog.ReadField(610).Transitions;
        var planner = new FieldWalkmeshRoutePlanner(mesh, null, _ => transitions);
        FieldPositionSnapshot At(int x, int y, ushort triangle) =>
            new(FieldPositionReader.FieldModule, 610, 0, x, y, (int)Math.Round(walkmesh.Triangles[triangle].GetCentroid().Z), triangle, 0);
        var starts = new List<(string, FieldPositionSnapshot)> { ("in from the clock room", At(508, 535, 110)) };
        foreach (var t in transitions.Where(t => t.Kind == FieldNavigationTransitionKind.Jump))
        {
            starts.Add(($"out of e{t.SourceEntityId}", At(t.TargetX, t.TargetY, (ushort)t.TargetTriangle)));
        }

        var exits = new FieldExitLabelResolver(_ => FieldMapNameResolution.Unknown, () => "Temple of the Ancients").Resolve(catalog.ReadField(610).Exits);
        var chaseExits = exits.Where(exit => exit.StableId.StartsWith("chase-", StringComparison.Ordinal)).ToArray();
        Equal(TempleChaseLayout.Doors.Count + TempleChaseLayout.Ledges.Count, chaseExits.Length, "every door and ledge is an exit");
        foreach (var exit in chaseExits)
        {
            var line = exit.TriggerLine!.Value;
            foreach (var (name, start) in starts)
            {
                Equal(true, planner.TryBuildRoute(start, exit, out var route), $"{exit.Label} from {name}: {planner.LastDiagnostic}");
                var targetZ = walkmesh.Triangles[route.TargetTriangle].GetCentroid().Z;
                Equal(true, Math.Abs(targetZ - line.StartZ) < 192,
                    $"{exit.Label} from {name} ends on its own floor (triangle {route.TargetTriangle} at {targetZ:0}, line at {line.StartZ})");
            }
        }
    }

    /// <summary>
    /// The planner defect under the tester's missing routes: from the upper floor, middle floor
    /// door 4's LINE (z -44) sits under upper triangle 111 (z 323). The approach point on the
    /// middle floor rounds onto an edge of its own triangle, and a planar containment test then
    /// took the upper triangle for it, so the route stopped on the floor above the door. Any
    /// trigger-line route has to end on the line's own floor.
    /// </summary>
    private static void ChaseLinesAnchorToTheirOwnFloor(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var mesh = createWalkmeshReader(610);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, 610, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var transitions = Catalog().ReadField(610).Transitions;
        var planner = new FieldWalkmeshRoutePlanner(mesh, null, _ => transitions);
        var upper = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 610, 0, 508, 535,
            (int)Math.Round(walkmesh.Triangles[110].GetCentroid().Z), 110, 0);
        foreach (var door in TempleChaseLayout.Doors)
        {
            var target = new FieldNavigationTarget(610, FieldNavigationCategory.Exits, door.Label,
                (door.Line.StartX + door.Line.EndX) / 2, (door.Line.StartY + door.Line.EndY) / 2, door.Line.StartZ,
                TriggerLine: door.Line, CompletesOnArrival: true);
            Equal(true, planner.TryBuildRoute(upper, target, out var route), $"{door.Label} from the upper floor: {planner.LastDiagnostic}");
            var targetZ = walkmesh.Triangles[route.TargetTriangle].GetCentroid().Z;
            Equal(true, Math.Abs(targetZ - door.Line.StartZ) < 192,
                $"{door.Label} from the upper floor ends on its own floor (triangle {route.TargetTriangle} at {targetZ:0}, line at {door.Line.StartZ})");
        }
    }

    /// <summary>
    /// The real controller, not only the planner: each kind of chase target starts navigation
    /// (a route preview, the beacon on, and a movement for auto walk) on the native mesh with the
    /// field's own transitions - a door, both ledges - and from standing on a ledge, the party can
    /// choose a door on the same floor and walk away.
    /// </summary>
    private static void ChaseTargetsStartRealNavigation(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var catalog = Catalog();
        var mesh = createWalkmeshReader(610);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, 610, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var transitions = catalog.ReadField(610).Transitions;
        var exits = new FieldExitLabelResolver(_ => FieldMapNameResolution.Unknown, () => "Temple of the Ancients").Resolve(catalog.ReadField(610).Exits);
        FieldNavigationTarget Exit(string label) => exits.Single(exit => exit.Label == label);
        var fromClock = At(walkmesh, 610, 508, 535);
        foreach (var label in new[] { "Middle floor door 2", "Ledge down to the middle floor; press OK", "Ledge down to the lower floor; press OK", "Lower floor door 4" })
        {
            StartsNavigation(Exit(label), fromClock, new FieldWalkmeshRoutePlanner(mesh, null, _ => transitions), $"{label} from the clock room's doorway");
        }

        var onLedge = At(walkmesh, 610, -581, 200);
        StartsNavigation(Exit("Upper floor door"), onLedge, new FieldWalkmeshRoutePlanner(mesh, null, _ => transitions),
            "the upper floor door, chosen while standing on the ledge");
    }

    /// <summary>
    /// The dig's ladders and a placed digger start real navigation too: a digger placed up on
    /// the ledge is reached from the lower level through the native ladder, whose own [OK] the
    /// route stops for.
    /// </summary>
    private static void ExcavationTargetsStartRealNavigation(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var catalog = new FieldScriptNavigationCatalog(DataRoot!);
        var mesh = createWalkmeshReader(772);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, 772, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var transitions = catalog.ReadField(772).Transitions;
        var memory = new DigMemory();
        // Up on the ledge where the diggers walk to after the ladder (their script 3's (104, 566)).
        var upper = At(walkmesh, 772, 104, 566);
        memory.Place(14, upper.X, upper.Y, upper.Z);
        var reader = new FieldNavigationObjectReader(memory.ReadInt32, memory.ReadByte, _ => null, _ => null,
            FieldNavigationObjectCatalog.CreateAllFields(), isLineEnabled: _ => true);
        var lower = At(walkmesh, 772, -427, -5);
        var objects = reader.ReadTargets(lower);
        foreach (var (label, start) in new[]
                 {
                     ("Ladder up to the upper level; press OK", lower),
                     ("Ladder down to the lower level; press OK", upper),
                     ("Digger 1", lower)
                 })
        {
            StartsNavigation(objects.Single(target => target.Label == label), start,
                new FieldWalkmeshRoutePlanner(mesh, null, _ => transitions), $"{label} at the dig");
        }
    }

    private static FieldPositionSnapshot At(FieldWalkmesh walkmesh, int field, int x, int y)
    {
        var triangle = Enumerable.Range(0, walkmesh.Triangles.Count)
            .Select(index => (Index: index, Centre: walkmesh.Triangles[index].GetCentroid()))
            .OrderBy(candidate => Math.Pow(candidate.Centre.X - x, 2) + Math.Pow(candidate.Centre.Y - y, 2))
            .First();
        return new FieldPositionSnapshot(FieldPositionReader.FieldModule, field, 0,
            (int)Math.Round(triangle.Centre.X), (int)Math.Round(triangle.Centre.Y), (int)Math.Round(triangle.Centre.Z),
            (ushort)triangle.Index, 0);
    }

    private static void StartsNavigation(FieldNavigationTarget target, FieldPositionSnapshot start, IFieldNavigationRoutePlanner planner, string label)
    {
        Equal(true, string.IsNullOrWhiteSpace(target.ManualNavigationGuidance), $"{label}: a physical target, not manual-only guidance");
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), planner);
        var transform = new FieldNavigationControlTransform(0);
        for (var index = 0; index < 4 && controller.CurrentCategory != target.Category; index++)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        }

        var preview = controller.PreviewActionRoute(FieldNavigationAction.ToggleBeacon, start, true);
        Equal(true, preview.UsesRoute && preview.RequiresCoherentRoute, $"{label}: the beacon asks for a route");
        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Equal(true, controller.BeaconEnabled, $"{label}: navigation starts ({controller.LastNavigationDiagnostic})");
        _ = controller.UpdateLiveTracking(start, new(0, FieldNavigationInput.None), transform, false, 80, observedAt: Epoch);
        Equal(true, controller.TryResolveAutomaticInput(start, transform, 80, out var input) && input != FieldNavigationInput.None,
            $"{label}: auto walk has somewhere to go ({controller.LastNavigationDiagnostic})");
    }

    // --- 772 bonevil2 ---------------------------------------------------------------------

    private const int WaitX = -427;
    private const int WaitY = -5;
    private const int WaitZ = -11;

    private static FieldActivityModelReading Digger(int entity, int x, int y, int z, int direction = 0) =>
        new(entity, FieldActivityReadStatus.Visible, new FieldActivityModelState(true, true, x, y, z, 0, direction));

    private static IReadOnlyList<FieldActivityModelReading> Diggers(params (int Entity, int X, int Y, int Z, int Direction)[] placed) =>
        Enumerable.Range(14, 5).Select(entity =>
            placed.FirstOrDefault(p => p.Entity == entity) is { Entity: > 0 } p
                ? Digger(entity, p.X, p.Y, p.Z, p.Direction)
                : Digger(entity, WaitX, WaitY, WaitZ)).ToArray();

    private static FieldActivityObservation Dig(int phase, IReadOnlyList<FieldActivityModelReading> diggers,
        int playerX = 0, int playerY = 0, int playerZ = -40, Func<int, int>? bank = null) =>
        new(772, playerX, playerY, playerZ, 0, true, 700, new FieldNavigationControlTransform(0), true,
            diggers, new Dictionary<int, FieldActivityWaitState>(), _ => true, null, 0)
        {
            ReadTemporaryByte = bank ?? (address => address == 12 ? phase : 0)
        };

    /// <summary>
    /// The tester's case: every digger visible at the Init spot. That is five diggers waiting,
    /// not five placed. The phase is the game's own Bank[5][12].
    /// </summary>
    private static void ExcavationPhaseComesFromTheGame()
    {
        var readout = new FieldActivityReadout();
        var waiting = Dig(7, Diggers());
        Equal(string.Empty, readout.Observe(waiting, Epoch).Speech ?? string.Empty,
            "on arrival the game's own window already names the control; nothing is repeated");
        Equal(
            "Placing diggers: none placed, up to 5 more. Stand where you want a digger and press Switch, " +
            "then choose Order a search, 100 gil, or Done to set off the blast. You are on the lower level. The dig spot is in Objects: the Lunar Harp's from the start, any other treasure's after the blast.",
            readout.Describe(waiting),
            "the repeat says the phase and the control");
        Equal(
            "Blast: press Switch to set off the bomb. 2 diggers placed: digger 1 ahead at 400 on the lower level, " +
            "digger 2 behind at 200 on the lower level. You are on the lower level. The dig spot is in Objects: the Lunar Harp's from the start, any other treasure's after the blast.",
            new FieldActivityReadout().Describe(Dig(2, Diggers((14, 0, -400, -40, 0), (15, 0, 200, -40, 0)))),
            "the blast phase");
        Equal(
            "Choose the dig point: stand where the diggers' lines of sight meet and press Switch. " +
            // Exactly beside him: his line starts where he stands, 400 ahead, and runs sideways.
            "Digger 1 ahead at 400 on the lower level, facing right; his line of sight passes 400 ahead of you. You are in line with no digger. You are on the lower level. The dig spot is in Objects.",
            new FieldActivityReadout().Describe(Dig(1, Diggers((14, 0, -400, -40, 192)))),
            "after the blast, each digger's facing");
        Equal("The dig is under way.", new FieldActivityReadout().Describe(Dig(0, Diggers())), "after the choice");
        Equal("Cannot read the dig.", new FieldActivityReadout().Describe(Dig(7, Diggers(), bank: _ => -1)),
            "an unreadable phase is not guessed from the models");
    }

    private static void ExcavationSaysEachDiggerPlaced()
    {
        var readout = new FieldActivityReadout();
        _ = readout.Observe(Dig(7, Diggers()), Epoch);
        Equal(null, readout.Observe(Dig(6, Diggers((14, 120, 300, -40, 0))), Epoch.AddSeconds(5)).Speech,
            "the first digger has just got there");
        Equal("Digger 1 placed.", readout.Observe(Dig(6, Diggers((14, 120, 300, -40, 0))), Epoch.AddSeconds(6)).Speech,
            "and is placed once he stays where the party ordered him");
        Equal(null, readout.Observe(Dig(6, Diggers((14, 120, 300, -40, 0))), Epoch.AddSeconds(9)).Speech, "said once");
        _ = readout.Observe(Dig(5, Diggers((14, 120, 300, -40, 0), (15, -200, 50, 328, 0))), Epoch.AddSeconds(12));
        Equal("Digger 2 placed.", readout.Observe(Dig(5, Diggers((14, 120, 300, -40, 0), (15, -200, 50, 328, 0))), Epoch.AddSeconds(13)).Speech,
            "the second, up on the ledge");
        // With too little gil the phase advances and nobody moves: nothing is placed, nothing said.
        Equal(null, readout.Observe(Dig(4, Diggers((14, 120, 300, -40, 0), (15, -200, 50, 328, 0))), Epoch.AddSeconds(15)).Speech,
            "a phase step with no digger moved is not a placement");
    }

    /// <summary>
    /// The diggers' facing is the clue only after the blast (keyc phase 2 turns them all).
    /// Standing on a digger's line of sight is what a sighted player sees; it is said as the
    /// party walks onto one, and never before the blast.
    /// </summary>
    private static void ExcavationLinesOfSightOnlyAfterTheBlast()
    {
        // Facing byte d points along (sin, -cos) of d/256 of a turn, as DescribeFacing reads it:
        // digger 1 at (-300, 0) facing 64 looks along +x, digger 2 at (0, -300) facing 128 along +y.
        var placed = Diggers((14, -300, 0, -40, 64), (15, 0, -300, -40, 128));
        var before = new FieldActivityReadout();
        _ = before.Observe(Dig(2, placed, playerX: -100, playerY: 0), Epoch);
        Equal(null, before.Observe(Dig(2, placed, playerX: 0, playerY: 0), Epoch.AddSeconds(3)).Speech,
            "before the blast a digger's facing is nothing");
        var after = new FieldActivityReadout();
        _ = after.Observe(Dig(1, placed, playerX: 200, playerY: 200), Epoch);
        Equal("In line with digger 1.", after.Observe(Dig(1, placed, playerX: -100, playerY: 5), Epoch.AddSeconds(3)).Speech,
            "on digger 1's line, ahead of him");
        Equal("In line with diggers 1 and 2.", after.Observe(Dig(1, placed, playerX: 3, playerY: 2), Epoch.AddSeconds(6)).Speech,
            "where both lines meet");
        Equal("Out of line with diggers 1 and 2.", after.Observe(Dig(1, placed, playerX: -400, playerY: 0), Epoch.AddSeconds(9)).Speech,
            "behind a digger is not in his line of sight, and walking off both lines is said");
    }

    private static void ExcavationUnreadableSightDoesNotInventMovement()
    {
        var placed = Diggers((14, -300, 0, -40, 64));
        var observation = Dig(1, placed);
        var readout = new FieldActivityReadout();
        _ = readout.Observe(observation, Epoch);
        var unreadable = placed.Select(digger => digger.EntityId == 14
            ? FieldActivityModelReading.Unreadable(14) : digger).ToArray();
        Equal(null, readout.Observe(observation with { Models = unreadable }, Epoch.AddSeconds(1)).Speech,
            "a failed worker read is not movement off his line");
        Equal(null, readout.Observe(observation, Epoch.AddSeconds(2)).Speech,
            "a recovered worker read is not movement onto his line");
        Equal(null, readout.Observe(observation with { ReadTemporaryByte = _ => -1 }, Epoch.AddSeconds(3)).Speech,
            "a failed phase read is not a puzzle change");
        Equal(null, readout.Observe(observation, Epoch.AddSeconds(4)).Speech,
            "phase recovery does not repeat the same sightline");
        Equal("Out of line with digger 1.",
            readout.Observe(Dig(1, placed, playerY: 100), Epoch.AddSeconds(5)).Speech,
            "real movement after read recovery is still announced");
    }

    /// <summary>
    /// A digger sent out walks from the waiting spot (his script 3), maybe up the ladder, and
    /// jumps to where the party stands. keyc runs that script with entityExecuteSync and only
    /// then takes the phase down, so while the phase is still the one that sent him he is on his
    /// way, however still he stands. He is placed once the game has finished sending him.
    /// </summary>
    private static void ExcavationWaitsForADiggerToArrive()
    {
        var readout = new FieldActivityReadout();
        _ = readout.Observe(Dig(7, Diggers()), Epoch);
        Equal(null, readout.Observe(Dig(7, Diggers((14, -300, -20, -30, 0))), Epoch.AddSeconds(1)).Speech, "leaving the waiting spot");
        Equal(null, readout.Observe(Dig(7, Diggers((14, -138, -33, -30, 0))), Epoch.AddSeconds(1.3)).Speech, "on his way");
        Equal(null, readout.Observe(Dig(7, Diggers((14, 60, 150, -40, 0))), Epoch.AddSeconds(1.6)).Speech, "still walking");
        Equal(null, readout.Observe(Dig(7, Diggers((14, 120, 300, -40, 0))), Epoch.AddSeconds(1.9)).Speech, "just landed");
        Equal("Digger 1 placed.", readout.Observe(Dig(6, Diggers((14, 120, 300, -40, 0))), Epoch.AddSeconds(2.6)).Speech,
            "placed once the game has finished sending him");
    }

    /// <summary>
    /// Root's probe: digger 1 climbs the ladder (his script 3's climbLadder: x and y stay put,
    /// z rises) and stands on the landing before he walks on and jumps, all while keyc is still
    /// in its phase-7 entityExecuteSync. Nothing about that is a placement. With too little gil
    /// the phase goes down and nobody moves, which is not a placement either.
    /// </summary>
    private static void ExcavationWaitsForTheGameToFinishSendingHim()
    {
        var readout = new FieldActivityReadout();
        _ = readout.Observe(Dig(7, Diggers()), Epoch);
        Equal(null, readout.Observe(Dig(7, Diggers((14, 170, 514, 50, 0))), Epoch.AddSeconds(1)).Speech, "at the foot of the ladder");
        Equal(null, readout.Observe(Dig(7, Diggers((14, 170, 514, 331, 0))), Epoch.AddSeconds(1.6)).Speech, "at the top");
        Equal(null, readout.Observe(Dig(7, Diggers((14, 170, 514, 331, 0))), Epoch.AddSeconds(2.2)).Speech, "standing on the landing");
        Equal(null, readout.Observe(Dig(7, Diggers((14, 170, 514, 331, 0))), Epoch.AddSeconds(4)).Speech, "however long he stands there");
        Equal(
            "Placing diggers: none placed, up to 5 more. Digger 1 is on his way. Stand where you want a digger and press Switch, " +
            "then choose Order a search, 100 gil, or Done to set off the blast. You are on the lower level. The dig spot is in Objects: the Lunar Harp's from the start, any other treasure's after the blast.",
            readout.Describe(Dig(7, Diggers((14, 170, 514, 331, 0)))),
            "the repeat does not count him placed, and says he is on his way");
        Equal(null, readout.Observe(Dig(7, Diggers((14, 104, 566, 331, 0))), Epoch.AddSeconds(4.3)).Speech,
            "landed where he was sent; the sync waits 20 frames more");
        Equal("Digger 1 placed.", readout.Observe(Dig(6, Diggers((14, 104, 566, 331, 0))), Epoch.AddSeconds(5)).Speech,
            "the sync has returned and he stands where he was sent");
        Equal(null, readout.Observe(Dig(5, Diggers((14, 104, 566, 331, 0))), Epoch.AddSeconds(7)).Speech,
            "no gil: the phase steps on, digger 2 stays waiting");
        Equal(null, readout.Observe(Dig(5, Diggers((14, 104, 566, 331, 0))), Epoch.AddSeconds(9)).Speech, "and stays unannounced");
        var unreadable = new FieldActivityReadout();
        _ = unreadable.Observe(Dig(7, Diggers(), bank: _ => -1), Epoch);
        _ = unreadable.Observe(Dig(7, Diggers((14, 104, 566, 331, 0)), bank: _ => -1), Epoch.AddSeconds(1));
        Equal(null, unreadable.Observe(Dig(7, Diggers((14, 104, 566, 331, 0)), bank: _ => -1), Epoch.AddSeconds(3)).Speech,
            "an unreadable phase confirms nobody");
    }

    /// <summary>A digger rising or dropping in place (x and y unchanged) has not stopped.</summary>
    private static void ExcavationSettleIncludesHeight()
    {
        var readout = new FieldActivityReadout();
        _ = readout.Observe(Dig(7, Diggers()), Epoch);
        Equal(null, readout.Observe(Dig(6, Diggers((14, 170, 514, 50, 0))), Epoch.AddSeconds(1)).Speech, "low");
        Equal(null, readout.Observe(Dig(6, Diggers((14, 170, 514, 331, 0))), Epoch.AddSeconds(1.3)).Speech, "high");
        Equal(null, readout.Observe(Dig(6, Diggers((14, 170, 514, 331, 0))), Epoch.AddSeconds(1.6)).Speech,
            "only 0.3 s since he last moved, although x and y have not changed for 0.6 s");
        Equal("Digger 1 placed.", readout.Observe(Dig(6, Diggers((14, 170, 514, 331, 0))), Epoch.AddSeconds(1.9)).Speech,
            "placed once he has stayed put in all three");
    }

    /// <summary>
    /// ladu and ladd climb on a fresh OK on their own LINEs (Move: keyPressedJustPressed 0x20).
    /// A route to the ladder ends there, so the ladder says what to press - once, as the party
    /// steps on, and without taking the player's input.
    /// </summary>
    private static void ExcavationLadderSaysConfirm()
    {
        Equal("19,20", string.Join(",", FieldActivityReadout.ObservedLineEntities(772)), "the ladders' LINEs are read");
        var readout = new FieldActivityReadout();
        var top = Dig(7, Diggers(), playerX: 170, playerY: 514, playerZ: 331);
        var cue = readout.Observe(top, Epoch);
        Equal("At the ladder. Press Confirm to climb down to the lower level.", cue.Speech, "ladu");
        Equal(false, cue.IsPending, "the ladder does not take over the player's input");
        Equal(false, readout.IsWaitingForInput(top), "and the field is not waiting on it");
        Equal(null, readout.Observe(top, Epoch.AddSeconds(20)).Speech, "said once");
        Equal(true, (readout.Describe(top) ?? string.Empty).StartsWith(
                "At the ladder. Press Confirm to climb down to the lower level. Placing diggers:", StringComparison.Ordinal),
            "the repeat starts with it");
        Equal("At the ladder. Press Confirm to climb up to the upper level.",
            new FieldActivityReadout().Observe(Dig(7, Diggers(), playerX: 218, playerY: 379, playerZ: -88), Epoch).Speech, "ladd");
        Equal(null, new FieldActivityReadout().Observe(Dig(7, Diggers(), playerX: 218, playerY: 379, playerZ: 331), Epoch).Speech,
            "not at the other level's height");
        Equal(null, new FieldActivityReadout().Observe(Dig(7, Diggers(), playerX: 170, playerY: 514, playerZ: 331) with { IsPlayerControlled = false }, Epoch).Speech,
            "no button is named while the party is being moved for them");
        var memory = new DigMemory();
        var reader = new FieldNavigationObjectReader(memory.ReadInt32, memory.ReadByte, _ => null, _ => null,
            FieldNavigationObjectCatalog.CreateAllFields(), isLineEnabled: _ => true);
        var ladders = reader.ReadTargets(new FieldPositionSnapshot(FieldPositionReader.FieldModule, 772, 0, 0, 0, -40, 44, 0))
            .Where(t => t.Label.StartsWith("Ladder", StringComparison.Ordinal)).Select(t => t.Label);
        Equal("Ladder down to the lower level; press OK|Ladder up to the upper level; press OK", string.Join("|", ladders),
            "the target names the button");
    }

    /// <summary>A digger the reader lost for a moment is not placed again when he comes back.</summary>
    private static void ExcavationDoesNotRepeatAfterATornLook()
    {
        var readout = new FieldActivityReadout();
        var placed = Diggers((14, 120, 300, -40, 0), (15, -200, 50, 328, 0));
        _ = readout.Observe(Dig(5, placed), Epoch);
        _ = readout.Observe(Dig(5, placed), Epoch.AddSeconds(1));
        var torn = placed.Select(reading => reading.EntityId == 14 ? FieldActivityModelReading.Unreadable(14) : reading).ToArray();
        Equal(null, readout.Observe(Dig(5, torn), Epoch.AddSeconds(2)).Speech, "a torn look says nothing");
        Equal(null, readout.Observe(Dig(5, placed), Epoch.AddSeconds(3)).Speech, "and his return is not a new placement");
        Equal(null, readout.Observe(Dig(5, placed), Epoch.AddSeconds(4)).Speech, "nor after the settle time");
    }

    /// <summary>
    /// Each step of the dig is said briefly as the game moves to it - the blast next, then the
    /// search - never as a wait that takes the player's input; the game's own windows say how.
    /// </summary>
    private static void ExcavationSaysEachPhaseBriefly()
    {
        var readout = new FieldActivityReadout();
        var placed = Diggers((14, 120, 300, -40, 64));
        _ = readout.Observe(Dig(6, placed), Epoch);
        var blast = readout.Observe(Dig(2, placed), Epoch.AddSeconds(1));
        Equal("Blast next.", blast.Speech, "the diggers are out; the blast is the next step");
        Equal(false, blast.IsPending, "and no step of the dig takes over the player's input");
        var search = readout.Observe(Dig(1, placed, playerX: 500, playerY: 500), Epoch.AddSeconds(4));
        Equal("The diggers have turned towards the dig point.", search.Speech, "the blast has turned them");
        Equal(false, readout.IsWaitingForInput(Dig(1, placed)), "the search is not a wait either");
        Equal("The dig is under way.", readout.Observe(Dig(0, placed), Epoch.AddSeconds(9)).Speech, "the choice made");
        // Walking off a line of sight is said as well as walking onto one.
        var sight = new FieldActivityReadout();
        var looking = Diggers((14, -300, 0, -40, 64));
        _ = sight.Observe(Dig(1, looking, playerX: 400, playerY: 400), Epoch);
        Equal("In line with digger 1.", sight.Observe(Dig(1, looking, playerX: -100, playerY: 0), Epoch.AddSeconds(1)).Speech, "onto it");
        Equal("Out of line with digger 1.", sight.Observe(Dig(1, looking, playerX: -100, playerY: 120), Epoch.AddSeconds(2)).Speech, "off it");
    }

    /// <summary>
    /// The dig has two levels, joined by the ladder. The repeat says which one the party and each
    /// digger are on and, once the blast has turned them, how far and which way each digger's line
    /// of sight passes from the party - worked out only from where he stands and faces.
    /// </summary>
    private static void ExcavationRepeatGivesLevelsAndSightlines()
    {
        var readout = new FieldActivityReadout();
        var placed = Diggers((14, 0, -400, -40, 0), (15, 0, 200, 330, 0));
        Equal(
            "Placing diggers: 2 placed: digger 1 ahead at 400 on the lower level, digger 2 behind at 200 on the upper level; up to 3 more. " +
            "Stand where you want a digger and press Switch, then choose Order a search, 100 gil, or Done to set off the blast. " +
            "You are on the lower level. The dig spot is in Objects: the Lunar Harp's from the start, any other treasure's after the blast.",
            readout.Describe(Dig(5, placed)),
            "both levels named");
        // Digger 1 at (-300, 0) facing +x (64): his line of sight runs along y = 0. With control
        // direction 0 the transform turns field -x into "to the right" and +x into "left".
        var looking = Diggers((14, -300, 0, -40, 64));
        Equal(
            "Choose the dig point: stand where the diggers' lines of sight meet and press Switch. " +
            "Digger 1 to the right at 300 on the lower level, facing left; his line of sight passes 100 ahead of you. " +
            "You are in line with no digger. You are on the lower level. The dig spot is in Objects.",
            readout.Describe(Dig(1, looking, playerX: 0, playerY: 100)),
            "where the line passes, from where he stands and faces");
        Equal(
            "Choose the dig point: stand where the diggers' lines of sight meet and press Switch. " +
            "Digger 1 to the right at 300 on the lower level, facing left; you are on his line of sight. " +
            "You are in line with digger 1. You are on the lower level. The dig spot is in Objects.",
            readout.Describe(Dig(1, looking, playerX: 0, playerY: 10)),
            "on the line");
        Equal(
            "Choose the dig point: stand where the diggers' lines of sight meet and press Switch. " +
            "Digger 1 to the left at 300 on the lower level, facing left; you are behind him. " +
            "You are in line with no digger. You are on the lower level. The dig spot is in Objects.",
            readout.Describe(Dig(1, looking, playerX: -600, playerY: 0)),
            "behind him his line of sight is nowhere near");
    }

    private static void ExcavationReadoutDoesNotObserveTheBuriedModels()
    {
        Equal("14,15,16,17,18", string.Join(",", FieldActivityReadout.ObservedEntities(772)), "only the diggers are observed");
        Equal(true, FieldActivityReadout.NeedsTemporaryBank(772), "the phase byte is read");
        var definitions = FieldNavigationObjectCatalog.CreateAllFields().Where(d => d.FieldId == 772).ToArray();
        // The buried models are no definitions: the one the diggers are sent to is offered, live,
        // as the dig spot (BoneVillageDigSpotTests).
        Equal(false, definitions.Any(d => d.EntityId is >= 6 and <= 13), "luna and the boxes are no object definitions");
    }

    /// <summary>A digger standing at the Init spot is waiting, not placed, and is not a target.</summary>
    private static void ExcavationDiggerTargetsAreOnlyThePlacedOnes()
    {
        var memory = new DigMemory();
        var reader = new FieldNavigationObjectReader(memory.ReadInt32, memory.ReadByte, _ => null, _ => null,
            FieldNavigationObjectCatalog.CreateAllFields(), isLineEnabled: _ => true);
        var at = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 772, 0, 0, 0, -40, 44, 0);
        Equal(string.Empty, string.Join("|", reader.ReadTargets(at).Where(t => t.Label.StartsWith("Digger", StringComparison.Ordinal)).Select(t => t.Label)),
            "five waiting diggers are no targets");
        memory.Place(14, 120, 300, -40);
        memory.Place(16, -200, 50, 328);
        var diggers = reader.ReadTargets(at).Where(t => t.Label.StartsWith("Digger", StringComparison.Ordinal)).ToArray();
        Equal("Digger 1|Digger 3", string.Join("|", diggers.Select(t => t.Label)), "each placed digger, by the order they are sent");
        Equal((120, 300, -40), (diggers[0].X, diggers[0].Y, diggers[0].Z), "at his live position");
        var ladders = reader.ReadTargets(at).Where(t => t.Label.StartsWith("Ladder", StringComparison.Ordinal)).Select(t => t.Label).ToArray();
        Equal("Ladder down to the lower level; press OK|Ladder up to the upper level; press OK", string.Join("|", ladders), "both ladders");
    }

    private static void ExcavationLadderAndDiggerDefinitionsAreNative()
    {
        var catalog = new FieldScriptNavigationCatalog(DataRoot!);
        foreach (var entity in new[] { 14, 15, 16, 17, 18 })
        {
            var init = catalog.ReadScriptOpcodes(772, entity, 0).First(op => op.Opcode == 0xA5).Bytes.ToArray();
            Equal((WaitX, WaitY, WaitZ), ((int)BitConverter.ToInt16(init, 3), (int)BitConverter.ToInt16(init, 5), (int)BitConverter.ToInt16(init, 7)),
                $"digger e{entity}'s Init spot is where the waiting diggers stand");
        }

        var transitions = catalog.ReadField(772).Transitions;
        Equal(2, transitions.Count(t => t.Kind == FieldNavigationTransitionKind.Ladder), "the two ladders");
    }

    private sealed class DigMemory
    {
        private const int EventTable = 0x02600000;
        private readonly Dictionary<int, int> ints = [];
        private readonly Dictionary<int, byte> bytes = [];

        public DigMemory()
        {
            ints[FieldNavigationObjectReader.AddressFieldEventDataPtr] = EventTable;
            bytes[FieldPositionReader.AddressFieldNumModels] = 8;
            for (var entity = 14; entity <= 18; entity++)
            {
                var model = entity - 13;
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = (byte)model;
                bytes[EventTable + model * FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = 1;
                Place(entity, WaitX, WaitY, WaitZ);
            }

            // The leader (model 0) with the collision radius field init gives it, for the ladders' LINEs.
            bytes[EventTable + FieldNavigationNpcReader.CollisionRadiusOffset] = 30;
        }

        public void Place(int entity, int x, int y, int z)
        {
            var model = entity - 13;
            var baseAddress = EventTable + model * FieldNavigationObjectReader.FieldEventDataStride;
            ints[baseAddress + FieldNavigationObjectReader.PositionXOffset] = x * 4096;
            ints[baseAddress + FieldNavigationObjectReader.PositionYOffset] = y * 4096;
            ints[baseAddress + FieldNavigationObjectReader.PositionZOffset] = z * 4096;
        }

        public int ReadInt32(int address) => ints.TryGetValue(address, out var value) ? value : 0;

        public byte ReadByte(int address) => bytes.TryGetValue(address, out var value) ? value : (byte)0;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
