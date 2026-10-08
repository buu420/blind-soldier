using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Temple of the Ancients mural hall after the Red Dragon (612 kuro_82), reported on
/// 2026-10-08: the miniature temple at the far right, which the party must examine to go on,
/// was not something the player could find in Story or Objects.
///
/// <para>What the installed scripts do. kuro_9's Cloud script 4 (613 e6.s4) writes
/// GameMoment 621 and map jumps to 612. There, the director's first-entry branch starts the
/// Red Dragon (battle 652), then shows the temple model - entity 19 "mini", resource
/// kuro_82fieldbg_jtmpobj.char, placed at (1032,18,57) by its Init hidden, non-solid and
/// without talk, and made visible, solid and talkable by its script 3 - and turns on LINE
/// entity 7 "border2" (914,35,0)-(922,-46,0) just in front of it with that line's script 7.
/// The model's Talk and Contact slots are a lone RET; the line's Go (slot 4) is what the game runs while the
/// leader touches it: on Confirm (IFKEY 0x220) it sets 624 and map jumps to 613. At 627 the
/// same Go only plays Cloud failing to shift the model (optional), and the director's 630
/// branch is a cutscene.</para>
///
/// <para>Before the fix the only row was the generic extraction "Continue on from here"
/// (priority 100), which neither names what a sighted player sees nor says Confirm is needed,
/// and completed by crossing the line - which a Go never needs - so auto walk brought the
/// party onto the line and never said it was there. Objects had nothing for the model.</para>
/// </summary>
internal static class TempleStatueNavigationTests
{
    internal const int MuralHall = 612;
    private const int MiniatureTempleRoom = 613;
    private const int LineEntity = 7;
    internal const string StoryLabel = "Press Confirm at the miniature temple at the far right of the hall";
    internal const string ObjectLabel = "Miniature temple; press Confirm at it";
    private static readonly FieldNavigationTriggerLine TempleLine = new(914, 35, 0, 922, -46, 0);
    private const int Radius = 34;

    internal static void Run(Func<int, FieldWalkmeshReader>? createWalkmeshReader = null)
    {
        StoryNamesTheMiniatureTempleAndConfirm();
        ObjectsListTheMiniatureTempleWhileItsLineWorks();
        if (DataRoot is not null)
        {
            InstalledScriptsAreTheWitness();
            if (createWalkmeshReader is not null)
            {
                WalkingFromTheNativeEntriesEndsWhereTheGoRuns(createWalkmeshReader);
            }
        }
        Console.WriteLine("temple statue navigation tests passed.");
    }

    private static string? DataRoot => Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") is { Length: > 0 } root
        ? root
        : null;

    private static void StoryNamesTheMiniatureTempleAndConfirm()
    {
        var position = At(700, 0);
        var afterDragon = StoryReader(621, lineOn: true).ReadTargets(position);
        Equal(1, afterDragon.Count, "after the Red Dragon the hall has exactly one Story step");
        var step = afterDragon[0];
        Equal(StoryLabel, step.Label, "the Story step names the miniature temple and the Confirm it needs");
        Equal((918, -6), (step.X, step.Y), "it leads to the middle of the native line in front of the model");
        Equal(TempleLine, step.TriggerLine ?? default, "and carries that line's own segment");
        Equal((Radius, false, false), (step.LineActivationRadius, step.LineActivatesOnOk, step.CompletesOnArrival),
            "it is a touch line: reached on the engine's own test with the leader's radius, no facing sector, " +
            "and it stays until Confirm moves the story on (a crossing rule would never be met by a Go)");

        Equal(0, StoryReader(621, lineOn: false).ReadTargets(position).Count,
            "with the line off the game does nothing there, so nothing is offered");
        Equal(false, StoryReader(624, lineOn: true).ReadTargets(position).Any(t => t.Label == StoryLabel),
            "once Confirm has been pressed (624) the step is done");
        Equal(false, StoryReader(627, lineOn: true).ReadTargets(position).Any(t => t.Label == StoryLabel),
            "after the agreement (627) the model no longer leads on; the hall's way out does");
    }

    private static void ObjectsListTheMiniatureTempleWhileItsLineWorks()
    {
        var position = At(700, 0);
        foreach (var moment in new[] { 621, 627 })
        {
            var temple = ObjectReader(moment, lineOn: true).ReadTargets(position).SingleOrDefault(t => t.Label == ObjectLabel);
            Equal(true, temple.Label == ObjectLabel, $"Objects lists the visible miniature temple at {moment}");
            Equal(FieldNavigationCategory.Objects, temple.Category, "as an object");
            Equal(TempleLine, temple.TriggerLine ?? default, "on the native line the game runs in front of it");
            Equal(Radius, temple.LineActivationRadius, "reached by the engine's own touch test with the leader's radius");
            Equal(false, temple.LineActivatesOnOk, "a Go runs on touch plus Confirm; it has no facing sector");
        }

        Equal(false, ObjectReader(621, lineOn: false).ReadTargets(position).Any(t => t.Label == ObjectLabel),
            "not while the line is off (before the dragon is beaten)");
        Equal(false, ObjectReader(630, lineOn: true).ReadTargets(position).Any(t => t.Label == ObjectLabel),
            "not in the 630 cutscene");
        Equal(false, ObjectReader(620, lineOn: true).ReadTargets(position).Any(t => t.Label == ObjectLabel),
            "not before the party has come here (621)");
    }

    private static void InstalledScriptsAreTheWitness()
    {
        var scripts = new FieldScriptNavigationCatalog(DataRoot!);
        static string Hex(FieldScriptOpcodeDefinition op) => Convert.ToHexString(op.Bytes.ToArray());
        string[] Ops(int field, int entity, int script) =>
            scripts.ReadScriptOpcodes(field, entity, script).Select(Hex).ToArray();

        var arrival = Ops(MiniatureTempleRoom, 6, 4);
        var write621 = Array.IndexOf(arrival, "8120006D02");
        Equal(true, write621 >= 0 && arrival.Skip(write621).Any(op => op.StartsWith("606402", StringComparison.Ordinal)),
            "kuro_9 Cloud script 4 sets GameMoment 621 and then map jumps to 612");

        Equal(true, Ops(MuralHall, LineEntity, 0).Contains("D09203230000009A03D2FF0000"),
            "border2's Init defines LINE (914,35,0)-(922,-46,0)");
        var go = Ops(MuralHall, LineEntity, 4);
        Equal("30200228", go[0], "its Go waits for Confirm (IFKEY 0x220)");
        Equal(true, go.Contains("8120007002") && go.Contains("6065029B031200050044"),
            "then sets 624 and map jumps to 613 (923,18) triangle 5");
        Equal("D101", Ops(MuralHall, LineEntity, 7)[0], "border2 script 7 turns the line on");

        var model = Ops(MuralHall, 19, 0);
        Equal(true, model.Contains("A10A") && model.Contains("A500000804120039001300") &&
            model.Contains("7E01") && model.Contains("C701") && model.Contains("A400"),
            "mini (model 10) is placed at (1032,18,57) triangle 19 hidden, non-solid and without talk");
        Equal(true, Ops(MuralHall, 19, 3).SequenceEqual(["240100", "7E00", "C700", "A401", "28040101", "00"]),
            "its script 3 makes it talkable, solid and visible");
        Equal(true, Ops(MuralHall, 19, 1).SequenceEqual(["00"]) && Ops(MuralHall, 19, 2).SequenceEqual(["00"]),
            "its Talk and Contact slots are a lone RET: talking to the model itself does nothing");

        var director = Ops(MuralHall, 2, 0);
        var battle = Array.FindIndex(director, op => op.StartsWith("70", StringComparison.Ordinal) && op.EndsWith("8C02", StringComparison.Ordinal));
        Equal(true, battle >= 0, "the director starts battle 652");
        Equal(true, director.Skip(battle).Contains("0213C3") && director.Skip(battle).Contains("0207C7"),
            "and after it shows the model (19, script 3) and turns its line on (7, script 7)");
    }

    /// <summary>
    /// Getting there, on the installed walkmesh. Where control comes back after the dragon:
    /// Cloud's script 11 places the leader at (277,117) on triangle 29 - kuro_9's MAPJUMP
    /// arrival point - and script 14 walks him to (305,84) before the battle; nothing after
    /// it moves him (scripts 10, 9 and 16 only animate and speak). From both, the planner
    /// (with the leader's body clearance) must find a way, and walking it frame by frame with
    /// the real controller must say "reached" only where the native Go can run: the leader
    /// touching LINE 7 within his collision radius (FUN_00637ABB's test, FieldNativeLineContact),
    /// on walkable floor, clear of the solid model at (1032,18). Radius 34 is the leader's
    /// radius this field's own Cloud script 19 restores (SLIDR 34) and the one the logged line
    /// contacts elsewhere were measured with.
    /// </summary>
    private static void WalkingFromTheNativeEntriesEndsWhereTheGoRuns(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var mesh = createWalkmeshReader(MuralHall);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, MuralHall, 0, 0, 0, 0, 0, 0)).Walkmesh
            ?? throw new InvalidOperationException("field 612 walkmesh must be readable");
        FieldPositionSnapshot Stand(int x, int y, int triangle = -1)
        {
            var resolved = triangle >= 0 ? triangle : FieldWalkmeshPathfinder.ResolveTriangle(walkmesh, x, y, 0, -1);
            Equal(true, resolved >= 0, $"({x},{y}) is on the hall's walkmesh");
            var z = (int)Math.Round(walkmesh.Triangles[resolved].GetCentroid().Z);
            return new FieldPositionSnapshot(FieldPositionReader.FieldModule, MuralHall, 0, x, y, z, (ushort)resolved, 128);
        }

        foreach (var (category, label) in new[] { (FieldNavigationCategory.Story, StoryLabel), (FieldNavigationCategory.Objects, ObjectLabel) })
        foreach (var start in new[] { Stand(277, 117, 29), Stand(305, 84) })
        {
            var planner = new FieldWalkmeshRoutePlanner(mesh, playerCollisionRadiusProvider: _ => Radius);
            IReadOnlyList<FieldNavigationTarget> Story(FieldPositionSnapshot at) => StoryReader(621, lineOn: true).ReadTargets(at);
            IReadOnlyList<FieldNavigationTarget> Objects(FieldPositionSnapshot at) =>
                ObjectReader(621, lineOn: true).ReadTargets(at).Where(t => t.Label == ObjectLabel).ToArray();
            var source = category == FieldNavigationCategory.Story
                ? new FieldNavigationTargetSource([], storyTargetProvider: Story)
                : new FieldNavigationTargetSource([], objectTargetProvider: Objects);
            var controller = new FieldNavigationController(source, planner);
            var transform = new FieldNavigationControlTransform(-128);
            var target = source.GetTargets(start, category).Single(t => t.Label == label);
            Equal(true, planner.TryBuildRoute(start, target, out var plan),
                $"{category} from ({start.X},{start.Y}): a route to the miniature temple: {planner.LastDiagnostic}");
            Equal(true, plan.UsesBodyClearance, "planned with the leader's body clearance");

            var said = new List<string>();
            for (var step = 0; step < 8 && controller.CurrentCategory != category; step++)
            {
                _ = controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
            }
            var toggled = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
            said.Add($"category {controller.CurrentCategory}, toggle: {toggled?.Speech}");
            controller.NoteAutoWalkStarted();
            var clock = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            var position = start;
            var reached = false;
            for (var tick = 0; tick < 400 && !reached; tick++)
            {
                var spoken = controller.UpdateLiveTracking(position, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                    transform, false, 80, observedAt: clock.AddMilliseconds(50 * tick))?.Speech ?? string.Empty;
                if (spoken.Length > 0) said.Add($"{tick}@({position.X},{position.Y}): {spoken}");
                if (spoken.Contains("reached", StringComparison.OrdinalIgnoreCase))
                {
                    reached = true;
                    break;
                }
                _ = controller.TryResolveAutomaticInput(position, transform, 80, out _);
                var current = source.GetTargets(position, category).SingleOrDefault(t => t.Label == label);
                Equal(true, current.Label == label, $"{category}: the step stays offered on the way ({position.X},{position.Y})");
                Equal(true, planner.TryGetNextWaypoint(position, current, out var waypoint),
                    $"{category}: a next waypoint from ({position.X},{position.Y}): {planner.LastDiagnostic}");
                // One native walking frame is 30 units (FUN_0074EA48's 0x1E).
                var dx = waypoint.X - position.X;
                var dy = waypoint.Y - position.Y;
                var length = Math.Sqrt(dx * (double)dx + dy * (double)dy);
                var scale = length <= 30 ? 1d : 30 / length;
                position = Stand(position.X + (int)Math.Round(dx * scale), position.Y + (int)Math.Round(dy * scale));
            }

            Equal(true, reached, $"{category} from ({start.X},{start.Y}): the walk is reported reached (ended at {position.X},{position.Y}); said: {string.Join(" | ", said.Take(3).Concat(said.TakeLast(4)))}");
            Equal(true, FieldNativeLineContact.Touches(TempleLine, position, Radius),
                $"{category} from ({start.X},{start.Y}): reached at ({position.X},{position.Y}), where the leader touches LINE 7 and Confirm runs the Go");
            var model = Math.Sqrt(Math.Pow(position.X - 1032, 2) + Math.Pow(position.Y - 18, 2));
            Equal(true, model > 2 * Radius, $"and clear of the solid model at (1032,18) ({model:0} units)");
            Console.WriteLine($"temple statue route: {category} from ({start.X},{start.Y}) reached at ({position.X},{position.Y}) " +
                $"triangle {position.TriangleId}, touching LINE 7 within radius {Radius}, {model:0} units from the model");
        }
    }

    private static FieldPositionSnapshot At(int x, int y) => new(1, MuralHall, 0, x, y, 0, 30, 0);

    private static Dictionary<int, byte> Memory(int moment)
    {
        const int events = 0x02500000;
        return new Dictionary<int, byte>
        {
            [FieldPositionReader.AddressFieldNumModels] = 1,
            [events + FieldNavigationNpcReader.CollisionRadiusOffset] = Radius,
            [FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment,
            [FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8),
            [FieldNavigationObjectReader.AddressFieldModelIdArray + 19] = 0xFF,
            [FieldNavigationObjectReader.AddressFieldModelIdArray + 20] = 0xFF
        };
    }

    private static int ReadInt32(Dictionary<int, byte> bytes, int address) =>
        address == FieldNavigationObjectReader.AddressFieldEventDataPtr
            ? 0x02500000
            : bytes.GetValueOrDefault(address) | bytes.GetValueOrDefault(address + 1) << 8;

    private static FieldStoryTargetReader StoryReader(int moment, bool lineOn)
    {
        var bytes = Memory(moment);
        return new FieldStoryTargetReader(
            address => ReadInt32(bytes, address),
            address => (short)(bytes.GetValueOrDefault(address) | bytes.GetValueOrDefault(address + 1) << 8),
            address => bytes.GetValueOrDefault(address),
            FieldStoryEventCatalog.CreateAllFields(),
            entity => lineOn && entity == LineEntity,
            readLineOkState: _ => null);
    }

    private static FieldNavigationObjectReader ObjectReader(int moment, bool lineOn)
    {
        var bytes = Memory(moment);
        return new FieldNavigationObjectReader(
            address => ReadInt32(bytes, address),
            address => bytes.GetValueOrDefault(address),
            _ => null,
            _ => null,
            FieldNavigationObjectCatalog.CreateAllFields(),
            entity => lineOn && entity == LineEntity,
            readLiveLine: entity => lineOn && entity == LineEntity ? TempleLine : null);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }
}
