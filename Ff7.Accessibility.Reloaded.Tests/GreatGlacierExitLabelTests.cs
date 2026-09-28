using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Great Glacier's exits and its map screen.
///
/// <para>Every glacier screen has the map name "Great Glacier", so the exits all read "Exit
/// to Great Glacier". Most of them lead into one of the six passage screens (move_s, move_i,
/// move_f, move_r, move_u, move_d, fields 670..675), which send the party on according to
/// bank 1 byte 184. A path from an ordinary screen always writes the same value, so where it
/// finally comes out is fixed and is named here. Inside a passage the way on depends on that
/// byte, so those exits are named only when the host reads it.</para>
///
/// <para>hyoumap (669) is the Glacier Map the [SWITCH] key opens. It shows one static
/// picture, speaks nothing and waits for OK, Cancel or Switch.</para>
/// </summary>
internal static class GreatGlacierExitLabelTests
{
    private static readonly uint ModuleAddress = FieldPositionReader.AddressCurrentModule;
    private static readonly uint FieldAddress = FieldPositionReader.AddressFieldId;
    private static readonly uint CorridorStateAddress = FieldNavigationObjectReader.AddressFieldBankBase + 184;

    private static readonly string[] TreasureWords =
        ["Mind Source", "Potion", "Safety Bit", "Elixir", "Added Cut", "All", "Alexander", "Materia", "materia", "treasure"];

    public static void Run()
    {
        OrdinaryScreenExitsNameWhereThePathComesOut();
        PassageExitsAreNamedOnlyFromTheLiveState();
        NoLabelGivesAwayATreasure();
        TheCorridorStateIsReadCoherently();
        TheMapScreenIsDescribedOnEntryAndOnRequest();
        TheGaeaIcicleCaveExitsAreToldApart();
        Console.WriteLine("Great Glacier exit labels and map screen checked.");
    }

    private static void OrdinaryScreenExitsNameWhereThePathComesOut()
    {
        var labels = Resolver(() => null);
        Equal("Exit to Great Glacier, passage toward the snow pillars", Label(labels, 663, 10, 670),
            "hyou4's line02 goes through move_s and move_i to hyou8_1");
        Equal("Exit to Great Glacier, frozen lake", Label(labels, 664, 13, 665),
            "hyou5_1's line03 goes straight onto the lake");
        Equal("Exit to Great Glacier, passage toward the hot spring", Label(labels, 679, 5, 674),
            "hyou9's line01 goes through move_u to hyou10");
        Equal("Exit to Great Glacier, passage toward the snowfield", Label(labels, 681, 4, 670),
            "hyou11's line00 goes through move_s onto the snowfield");
        Equal("Exit to Great Glacier, passage toward the Ice Gate", Label(labels, 660, 4, 673),
            "hyou3's line00 goes back through move_r to hyou1");
        Equal("Leave the glacier for the world map", Label(labels, 658, 7, 48),
            "hyou1's line03 is MAPJUMP 48, a world-map entry with no map name");
        Equal("Exit to Frostbite Cave", Label(labels, 660, 7, 661),
            "a screen with its own map name keeps it");
        Equal("Exit to Cave", Label(labels, 677, 10, 678), "a cave keeps its own map name");
    }

    private static void PassageExitsAreNamedOnlyFromTheLiveState()
    {
        int? state = 74;
        var labels = Resolver(() => state);
        Equal("Exit to Great Glacier, passage toward the hot spring", Label(labels, 675, 8, 675, 680),
            "move_d at 74: line01 leads through the next passage to hyou10");
        Equal("Exit to Great Glacier, rocky crossroads", Label(labels, 675, 4, 679, 680),
            "move_d at 74: line00 opens straight back onto hyou9");
        state = 76;
        Equal("Exit to Great Glacier, hot spring", Label(labels, 675, 4, 679, 680),
            "move_d at 76: line00 opens straight onto hyou10");
        Equal("Exit to Great Glacier, passage toward the snowfield", Label(labels, 675, 8, 670),
            "move_d at 76: line01 goes through move_s to the snowfield");
        state = null;
        Equal("Exit to Great Glacier", Label(labels, 675, 8, 670),
            "without the live state a passage exit is not guessed");
        state = 200;
        Equal("Exit to Great Glacier", Label(labels, 675, 8, 670), "a state no script writes is not guessed");
        Equal("Exit to Great Glacier", Label(Resolver(() => throw new InvalidOperationException("torn read")), 675, 8, 670),
            "a failing state reader falls back to the plain name");
        Equal("Exit to Great Glacier", Label(new FieldExitLabelResolver(Names, () => "Great Glacier"), 675, 8, 670),
            "a host that does not read the state keeps the plain name");
    }

    private static void NoLabelGivesAwayATreasure()
    {
        foreach (var label in GreatGlacierExitLabels.AllLabels())
        {
            foreach (var word in TreasureWords)
            {
                Equal(false, label.Contains(word, StringComparison.Ordinal), $"'{label}' names no treasure");
            }
        }
    }

    private static void TheCorridorStateIsReadCoherently()
    {
        var memory = Passage(675, 75);
        Equal<int?>(75, new GreatGlacierCorridorStateReader(memory).Read(), "move_d's bank 1 byte 184");
        Equal<int?>(null, new GreatGlacierCorridorStateReader(Passage(664, 75)).Read(), "outside a passage it is not read");
        var world = Passage(675, 75);
        world.Set(ModuleAddress, 3);
        Equal<int?>(null, new GreatGlacierCorridorStateReader(world).Read(), "outside the field module");
        var changing = Passage(675, 74);
        changing.Sequence(CorridorStateAddress, 74, 75);
        Equal<int?>(null, new GreatGlacierCorridorStateReader(changing).Read(), "a value that changes between reads");
        var unreadable = Passage(675, 75);
        unreadable.Unreadable.Add(CorridorStateAddress);
        Equal<int?>(null, new GreatGlacierCorridorStateReader(unreadable).Read(), "an unreadable byte");
    }

    private static void TheMapScreenIsDescribedOnEntryAndOnRequest()
    {
        var readout = new GreatGlacierMapScreenReadout();
        Equal<string?>(null, readout.Observe(658), "no map outside hyoumap");
        Equal(GreatGlacierMapScreen.Description, readout.Observe(GreatGlacierMapScreen.FieldId), "opening the map describes it");
        Equal<string?>(null, readout.Observe(GreatGlacierMapScreen.FieldId), "once per opening");
        Equal<string?>(null, readout.Observe(658), "closing it says nothing");
        Equal(GreatGlacierMapScreen.Description, readout.Observe(GreatGlacierMapScreen.FieldId), "each opening is described");
        Contains(GreatGlacierMapScreen.Description, "Glacier Map.", "the screen is named first");
        Contains(GreatGlacierMapScreen.Description, "Press OK, Cancel or Switch to close the map.", "the native close keys");
        Contains(GreatGlacierMapScreen.Description, "no mark showing where you are", "the picture has no position marker");
        Equal(true, GreatGlacierMapScreen.Description.EndsWith("Press OK, Cancel or Switch to close the map.", StringComparison.Ordinal),
            "the controls come last, where a repeat ends");
    }

    /// <summary>
    /// Every table entry rebuilt from the installed scripts: each LINE's MAPJUMP and the bank 1
    /// byte 184 it leaves, followed through the passage screens by the one line that does not
    /// lead back, to the screen it comes out in. And hyoumap's controls.
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var catalog = new FieldScriptNavigationCatalog(root);
        CheckGaeaIcicleCaves(root, catalog);
        var fields = Enumerable.Range(658, 27).Where(f => f != 669).ToArray();
        var scripts = fields.ToDictionary(f => f, f => catalog.ReadAllScriptOpcodes(f));
        var lines = fields.ToDictionary(f => f, f => scripts[f]
            .Where(s => s.ScriptId == 0 && s.Opcodes.Any(o => o.Opcode == 0xD0)).Select(s => s.EntityId).ToArray());

        (int Field, int State)? Exit(int field, int entity, int state)
        {
            for (var slot = 1; slot <= 6; slot++)
            {
                if (!scripts[field].Any(s => s.EntityId == entity && s.ScriptId == slot)) continue;
                var bank = new Dictionary<int, int> { [184] = state };
                if (Run(scripts[field], entity, slot, bank, 0) is { } jump) return (jump, bank[184]);
            }

            return null;
        }

        int Final(int field, int state, (int Field, int State) came)
        {
            var seen = new HashSet<(int, int)>();
            while (GreatGlacierExitLabels.IsPassage(field))
            {
                Equal(true, seen.Add((field, state)), $"the passage chain loops at {field}/{state}");
                var outs = lines[field].Select(e => Exit(field, e, state)).OfType<(int Field, int State)>().Distinct().ToList();
                var on = outs.Where(o => o != came).ToList();
                if (on.Count > 1) on = on.Where(o => o.Field != came.Field).ToList();
                Equal(1, on.Count, $"passage {field}/{state} has one way on");
                came = (field, state);
                (field, state) = on[0];
            }

            return field;
        }

        var fixedCount = 0;
        var producedFixed = new HashSet<(int, int)>();
        var producedCorridor = new HashSet<(int, int, int)>();
        var states = new HashSet<(int, int)> { (670, 52), (670, 53), (670, 55) }; // the snowfield's edges (wm3.ev)
        foreach (var field in fields.Where(f => !GreatGlacierExitLabels.IsPassage(f)))
        {
            foreach (var entity in lines[field])
            {
                if (Exit(field, entity, 0) is not { } next) continue;
                var final = GreatGlacierExitLabels.IsPassage(next.Field) ? Final(next.Field, next.State, (field, 0)) : next.Field;
                Equal<int?>(final, GreatGlacierExitLabels.FixedFinal(field, entity), $"{field} LINE {entity} comes out in {final}");
                if (GreatGlacierExitLabels.IsPassage(next.Field)) states.Add(next);
                producedFixed.Add((field, entity));
                fixedCount++;
            }
        }

        var pending = new Queue<(int, int)>(states);
        while (pending.Count > 0)
        {
            var (field, state) = pending.Dequeue();
            foreach (var entity in lines[field])
            {
                if (Exit(field, entity, state) is { } next && GreatGlacierExitLabels.IsPassage(next.Field) && states.Add(next))
                    pending.Enqueue(next);
            }
        }

        var corridorCount = 0;
        foreach (var (field, state) in states)
        {
            foreach (var entity in lines[field])
            {
                if (Exit(field, entity, state) is not { } next) continue;
                var final = GreatGlacierExitLabels.IsPassage(next.Field) ? Final(next.Field, next.State, (field, state)) : next.Field;
                Equal<(int, int)?>((next.Field, final), GreatGlacierExitLabels.CorridorFinal(field, state, entity),
                    $"passage {field}/{state} LINE {entity}");
                producedCorridor.Add((field, state, entity));
                corridorCount++;
            }
        }

        Equal("", string.Join(", ", GreatGlacierExitLabels.FixedKeys.Where(k => !producedFixed.Contains(k))),
            "no ordinary-screen entry the scripts do not produce");
        Equal("", string.Join(", ", GreatGlacierExitLabels.CorridorKeys.Where(k => !producedCorridor.Contains(k))),
            "no passage entry the scripts do not produce");

        // Every scripted exit the catalog offers on a glacier screen has a name to take.
        var unlabeled = fields
            .SelectMany(f => catalog.ReadField(f).Exits)
            .Where(e => e.StableId.StartsWith("script-exit:", StringComparison.Ordinal))
            .Where(e => !GreatGlacierExitLabels.IsPassage(e.FieldId))
            .Where(e => GreatGlacierExitLabels.FixedFinal(e.FieldId, e.TriggerEntityId) is null)
            .Select(e => e.StableId)
            .ToArray();
        Equal(0, unlabeled.Length, $"ordinary-screen exits without a known destination: {string.Join(", ", unlabeled)}");

        // hyoumap: nothing spoken, nothing drawn on demand, control off, and OK, Cancel or
        // Switch (IFKEYON 0x02E0) sends the party back.
        var map = catalog.ReadAllScriptOpcodes(GreatGlacierMapScreen.FieldId);
        var ops = map.SelectMany(s => s.Opcodes).ToArray();
        Equal(false, ops.Any(o => o.Opcode is 0x40 or 0x48), "the map screen shows no message");
        Equal(false, ops.Any(o => o.Opcode is 0xE0 or 0xE1), "the map picture never changes");
        Equal(true, ops.Any(o => o.Opcode == 0x33 && o.Bytes[1] == 1), "the party cannot move on the map");
        Equal(true, ops.Any(o => o.Opcode == 0x31 && BitConverter.ToUInt16(o.Bytes.ToArray(), 1) == 0x02E0),
            "OK (0x0220), Cancel (0x0040) or Switch (0x0080) closes it");
        Console.WriteLine($"Great Glacier exit labels: {fixedCount} ordinary-screen and {corridorCount} passage LINE exits rebuilt from the installed scripts over {states.Count} passage states.");
    }

    private static int? Run(IReadOnlyList<FieldScriptDefinition> scripts, int entity, int slot, Dictionary<int, int> bank, int depth)
    {
        if (depth > 8) return null;
        var script = scripts.SingleOrDefault(s => s.EntityId == entity && s.ScriptId == slot);
        if (script.Opcodes is null) return null;
        var byOffset = script.Opcodes.ToDictionary(o => o.ByteIndex);
        var offset = script.Opcodes[0].ByteIndex;
        for (var steps = 0; steps < 3000 && byOffset.TryGetValue(offset, out var op); steps++)
        {
            var b = op.Bytes.ToArray();
            var next = offset + b.Length;
            switch (op.Opcode)
            {
                case 0x00:
                    return null;
                case 0x01 or 0x02 or 0x03:
                    if (Run(scripts, b[1], b[2] & 31, bank, depth + 1) is { } called) return called;
                    break;
                case 0x10: next = offset + 1 + b[1]; break;
                case 0x11: next = offset + 1 + BitConverter.ToUInt16(b, 1); break;
                case 0x14 or 0x15:
                    // Bank 1 is the savemap; 5 is the field's temporary bank, which starts at 0 and
                    // holds what the script itself wrote (an ASK keeps the default it was given).
                    int Value(int nibble, int raw) => nibble switch
                    {
                        0 => raw,
                        1 => bank.GetValueOrDefault(raw),
                        5 => bank.GetValueOrDefault(1000 + raw),
                        _ => 0
                    };
                    var left = Value(b[1] >> 4, b[2]);
                    var right = Value(b[1] & 15, b[3]);
                    var passes = b[4] switch
                    {
                        0 => left == right, 1 => left != right, 2 => left > right, 3 => left < right,
                        4 => left >= right, 5 => left <= right, 6 => (left & right) != 0,
                        9 => ((left >> right) & 1) == 1, 10 => ((left >> right) & 1) == 0, _ => false
                    };
                    if (!passes) next = offset + 5 + (op.Opcode == 0x15 ? BitConverter.ToUInt16(b, 5) : b[5]);
                    break;
                case 0x80:
                    var value = (b[1] & 15) switch { 0 => b[3], 1 => bank.GetValueOrDefault(b[3]), 5 => bank.GetValueOrDefault(1000 + b[3]), _ => 0 };
                    if (b[1] >> 4 == 1) bank[b[2]] = value;
                    if (b[1] >> 4 == 5) bank[1000 + b[2]] = value;
                    break;
                case 0x60:
                    return BitConverter.ToUInt16(b, 1);
            }

            offset = next;
        }

        return null;
    }

    /// <summary>
    /// gaiin_1..gaiin_5 (690, 691, 693, 696, 697) all have the map name "Inside of Gaea's
    /// Cliff", so every way out of them was "Exit to Inside of Gaea's Cliff" - the eight
    /// level-to-level doors between the first two caves, and in the icicle caves the
    /// four icicle battle lines in gaiin_5, which leave for gaiin_3 only through the jump the
    /// game offers after the battle. In the 2026-09-28 log the player picked those exits
    /// again and again without being able to tell a passage from an icicle.
    /// </summary>
    internal static readonly (string StableId, string Label)[] GaeaCaveExits =
    [
        ("gateway:690:0:691", "Door from the entrance level to the second cave's main level"),
        ("gateway:690:1:691", "Door from the upper level to the second cave's main level"),
        ("gateway:690:2:691", "Door from the middle level to the second cave's boulder level"),
        ("gateway:690:3:691", "Door from the middle level to the second cave's main level"),
        ("gateway:691:0:690", "Door from the main level to the first cave's entrance level"),
        ("gateway:691:1:690", "Door from the main level to the first cave's upper level, the way on"),
        ("gateway:691:2:690", "Door from the boulder level to the first cave's middle level"),
        ("gateway:691:3:690", "Door from the main level to the first cave's middle level"),
        ("script-exit:693:15:692", "Back out onto the cliff face"),
        ("script-exit:693:16:696", "Passage out to the ledge by the icicles"),
        ("script-exit:693:17:696", "Passage beyond the fallen ice, out to the far ledge"),
        ("gateway:696:0:693", "Back into the cave, beyond the fallen ice"),
        ("gateway:696:1:693", "Back into the cave by the icicle passage"),
        ("gateway:696:2:697", "Along the ledge to the icicles"),
        ("gateway:696:3:697", "Along the ledge to the way down"),
        ("script-exit:697:3:693", "First icicle, then the jump down into the cave"),
        ("script-exit:697:4:693", "Second icicle, then the jump down into the cave"),
        ("script-exit:697:5:693", "Third icicle, then the jump down into the cave"),
        ("script-exit:697:6:693", "Fourth icicle, then the jump down into the cave"),
        ("script-exit:697:7:694", "Out onto the cliff face, the way on up"),
        ("script-exit:697:8:696", "Back along the ledge from the icicles"),
        ("script-exit:697:9:696", "Back along the ledge from the way down"),
    ];

    private static void TheGaeaIcicleCaveExitsAreToldApart()
    {
        var labels = new FieldExitLabelResolver(
            field => field is >= 689 and <= 699 ? FieldMapNameResolution.Known(["Inside of Gaea's Cliff"]) : FieldMapNameResolution.Unknown,
            () => "Inside of Gaea's Cliff");
        foreach (var (stableId, expected) in GaeaCaveExits)
        {
            var parts = stableId.Split(':');
            var target = new FieldNavigationTarget(int.Parse(parts[1]), FieldNavigationCategory.Exits, "Scripted exit", 0, 0, 0,
                stableId, DestinationFieldIds: [int.Parse(parts[3])]);
            Equal(expected, labels.Resolve([target]).Single().Label, $"{stableId} is named for where it goes");
        }

        Equal(GaeaCaveExits.Length, GaeaCaveExits.Select(exit => exit.Label).Distinct().Count(),
            "no two of these ways out share a name");
    }

    /// <summary>
    /// The names above belong to the exits the installed scripts actually publish, and the
    /// way across the fallen ice is a real route: from where an icicle battle drops the party
    /// (triangle 106), through triangles 161 and 162 once gaiin_3's Director unlocks them and
    /// over the ice on the field's own jump lines, to the northern passage - and shut while
    /// the icicles stand.
    /// </summary>
    private static void CheckGaeaIcicleCaves(string root, FieldScriptNavigationCatalog catalog)
    {
        var published = new[] { 693, 697 }.SelectMany(field => catalog.ReadField(field).Exits)
            .Select(exit => exit.StableId).ToHashSet(StringComparer.Ordinal);
        foreach (var (stableId, _) in GaeaCaveExits.Where(exit => exit.StableId.StartsWith("script-exit:", StringComparison.Ordinal)))
        {
            Equal(true, published.Contains(stableId), $"the installed scripts publish {stableId}");
        }

        Equal(10, published.Count, "gaiin_3 and gaiin_5 publish these ten script exits and no others");

        var source = new FlevelDataSource(root);
        Equal(true, source.TryReadField(693, out var encoded), "gaiin_3 is installed");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        const int pointer = 0x03000000;
        int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
            a >= pointer && a + 4 <= pointer + bytes.Length ? BitConverter.ToInt32(bytes, a - pointer) : 0;
        short Short(int a) => a >= pointer && a + 2 <= pointer + bytes.Length ? BitConverter.ToInt16(bytes, a - pointer) : (short)0;
        var transitions = catalog.ReadField(693).Transitions;
        var north = new FieldNavigationTarget(693, FieldNavigationCategory.Story, "Cross the fallen ice to the northern passage",
            234, 1823, -593, TriggerEntityId: 17, TriggerLine: new FieldNavigationTriggerLine(162, 1826, -593, 306, 1820, -593));
        var landing = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 693, 0, 20, 319, -593, 106, 0);

        var standing = new FieldWalkmeshRoutePlanner(new FieldWalkmeshReader(Int, Short),
            FieldBodyClearanceRouteTests.LockedTriangles(693, [155, 161, 162]), _ => transitions);
        Equal(false, standing.TryBuildRoute(landing, north, out _), "while the icicles stand the ice cannot be crossed");

        var fallen = new FieldWalkmeshRoutePlanner(new FieldWalkmeshReader(Int, Short),
            FieldBodyClearanceRouteTests.LockedTriangles(693, [155]), _ => transitions);
        Equal(true, fallen.TryBuildRoute(landing, north, out var plan), $"once they have fallen it can: {fallen.LastDiagnostic}");
        Equal(true, plan.TrianglePath.Contains(161) && plan.TrianglePath.Contains(162) && plan.TrianglePath[^1] == 50,
            "the way runs through the unlocked triangles to the passage's own triangle");
        Equal(true, plan.Portals.Count(portal => portal.TransitionKind == FieldNavigationTransitionKind.Jump) >= 1 &&
            plan.Portals.Any(portal => portal.TransitionId == "jump:693:13:20:11:47"),
            "and over the ice by gaiin_3's own jump lines, the last onto the passage's island");
        TheRibbonRouteKeepsTheBodyOffTheWall(root);
        TheSpentJumpDownIsNotOfferedAgain(catalog);
        TheEnhanceSwordSaysHowToGetRound(root, catalog);
        Console.WriteLine($"Gaea's Cliff ice caves: {GaeaCaveExits.Length} exits named; the fallen ice is crossed in {plan.TrianglePath.Count} triangles.");
    }

    /// <summary>
    /// gaiin_1, 2026-09-28 x64 log 11:02:44-49: auto walk to the Ribbon (tbox2, placed at
    /// (693,-780,-326) on triangle 168) held DownRight from 11:02:44 and the party's position
    /// stalled around (-134,-345) on triangles 54/53 - the last footstep was on 53 at 11:02:49 -
    /// until the five-second guard stopped it. gaiin_1 has no flat (z = 0) faces, so the flat-floor body
    /// model has nothing to say and the route kept the planner's fixed portal inset: its next
    /// corner sat about 15 units from the end of the wall (-95,-334)-(-168,-452), while the
    /// leader's radius here is 30 (scale 512). The game will not put the body there. With the
    /// radius known, every funnel corner at a wall vertex is kept a body radius off it.
    /// </summary>
    private static void TheRibbonRouteKeepsTheBodyOffTheWall(string root)
    {
        var source = new FlevelDataSource(root);
        Equal(true, source.TryReadField(690, out var encoded), "gaiin_1 is installed");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        const int pointer = 0x03000000;
        int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
            a >= pointer && a + 4 <= pointer + bytes.Length ? BitConverter.ToInt32(bytes, a - pointer) : 0;
        short Short(int a) => a >= pointer && a + 2 <= pointer + bytes.Length ? BitConverter.ToInt16(bytes, a - pointer) : (short)0;
        var scriptSection = BitConverter.ToInt32(bytes, 6) + 4;
        var radius = 30 * BitConverter.ToUInt16(bytes, scriptSection + 8) / 512;
        Equal(30, radius, "gaiin_1's leader radius is 30 x scale 512 / 512");

        var mesh = new FieldWalkmeshReader(Int, Short).Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, 690, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var walls = mesh.Triangles
            .SelectMany(t => Enumerable.Range(0, 3).Where(edge => t.GetAdjacentTriangle(edge) < 0).Select(edge => t.GetEdge(edge)))
            .ToArray();
        double WallClearance(FieldNavigationRouteWaypoint point) => walls
            .Where(w => Math.Abs(((w.Start.Z + w.End.Z) / 2d) - point.Z) < 64)
            .Select(w =>
            {
                double ex = w.End.X - w.Start.X, ey = w.End.Y - w.Start.Y;
                var lengthSquared = (ex * ex) + (ey * ey);
                var t = lengthSquared <= 0 ? 0 : Math.Clamp((((point.X - w.Start.X) * ex) + ((point.Y - w.Start.Y) * ey)) / lengthSquared, 0, 1);
                double px = w.Start.X + (t * ex) - point.X, py = w.Start.Y + (t * ey) - point.Y;
                return Math.Sqrt((px * px) + (py * py));
            })
            .DefaultIfEmpty(double.PositiveInfinity)
            .Min();
        var planner = new FieldWalkmeshRoutePlanner(new FieldWalkmeshReader(Int, Short), playerCollisionRadiusProvider: _ => radius);
        var ribbon = new FieldNavigationTarget(690, FieldNavigationCategory.Objects, "Ribbon", 693, -780, -326,
            "object:690:9", InteractionRadius: 60);
        var stall = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 690, 0, -134, -345,
            (int)Math.Round(mesh.Triangles[54].GetCentroid().Z), 54, 0);
        Equal(true, planner.TryBuildRoute(stall, ribbon, out var plan), $"the Ribbon has a route: {planner.LastDiagnostic}");
        Equal(168, plan.TrianglePath[^1], "on the level the logged stall is on");
        Equal(true, planner.TryGetNextWaypoint(stall, ribbon, out var next), "and a next step");
        Equal(true, WallClearance(next) >= radius - 1,
            $"the next step from the logged stall, ({next.X},{next.Y}), is a body radius from every wall segment ({WallClearance(next):0.0} units, radius {radius})");

        // The squeeze from 54 into 51 between tri 54's wall (-95,-334)-(-168,-452) and tri 51's
        // wall (-234,-416)-(-193,-379). Its best point, (-182,-412), leaves 31.9 units to both;
        // a radius-30 step along the portal from each wall vertex, (-178,-424) and (-183,-407),
        // is only 23.2 and 27.5 units from those walls because they meet the portal obliquely.
        // The route must cross it where the body fits, measured to the wall SEGMENTS.
        var squeeze = plan.Portals.Single(p => p.FromTriangle == 54 && p.ToTriangle == 51);
        foreach (var end in new[] { squeeze.Left, squeeze.Right })
        {
            Equal(true, WallClearance(end) >= radius - 1,
                $"the route crosses 54->51 a body radius from both walls ({end.X},{end.Y}: {WallClearance(end):0.0})");
        }

        // Every corner the route turns at, on its own floor.
        foreach (var portal in plan.Portals.Where(p => p.TransitionKind is null && p.FromTriangle != p.ToTriangle))
        {
            foreach (var end in new[] { portal.Left, portal.Right })
            {
                Equal(true, WallClearance(end) >= radius - 1,
                    $"route corner ({end.X},{end.Y}) on {portal.FromTriangle}->{portal.ToTriangle} is a body radius from every wall segment ({WallClearance(end):0.0})");
            }
        }
    }

    /// <summary>
    /// gaiin_5's bat1: once its icicle has fallen, crossing the line asks "Jump down to the room
    /// below?" only while temporary byte 5[13] is 0, and then counts it up; field init zeroes
    /// the temporary bank. The published exit is guarded exactly that way, so a player who
    /// answered No is not sent back to a line that will not ask again until the next visit.
    /// </summary>
    private static void TheSpentJumpDownIsNotOfferedAgain(FieldScriptNavigationCatalog catalog)
    {
        var read = catalog.ReadField(697);
        var exit = read.Exits.Single(e => e.StableId == "script-exit:697:3:693");
        Equal(true, read.ExitGuards.ContainsKey(exit.StableId), "the first icicle's line carries a live guard");
        bool Offered(byte icicles, byte visitCounter)
        {
            var memory = new PassageMemory();
            memory.Set((uint)(FieldScriptExitGuards.AddressSavemapFieldBanks + 131), icicles);
            memory.Set((uint)(FieldScriptExitGuards.AddressTemporaryFieldBank + 13), visitCounter);
            return FieldScriptExitGuards.Apply([exit], read.ExitGuards, memory).Count == 1;
        }

        Equal(true, Offered(0xC0, 0), "before the first icicle falls its line is the battle, and the jump after it");
        Equal(true, Offered(0xC4, 0), "once fallen, the first crossing on this visit still asks to jump down");
        Equal(false, Offered(0xC4, 1), "after that question on this visit (answered No), the line no longer leads anywhere");
        Equal(true, Offered(0xDC, 0), "a new visit starts the counter at 0 again (field init zeroes the bank)");
        Equal(true, Offered(0xC0, 1), "while the icicle stands the counter is not what decides it");
    }

    /// <summary>
    /// gaiin_4's Enhance Sword (tbox1, placed at (-297,-4045,1411) on triangle 22) is on the
    /// western ledge. From the eastern one the walkmesh has no way to it; the native way round is
    /// back into gaiin_3 by gateway1 and out across the fallen ice (evjp42), which lands on the
    /// western ledge at (-209,-4392) triangle 20. The 2026-09-28 log's Objects list said only
    /// "Enhance Sword. direction unavailable." (11:58:30); the list now says what the cross-field
    /// approach, which starts navigation that way, already knows.
    /// </summary>
    private static void TheEnhanceSwordSaysHowToGetRound(string root, FieldScriptNavigationCatalog catalog)
    {
        var source = new FlevelDataSource(root);
        FieldWalkmeshReader Reader(int field)
        {
            source.TryReadField(field, out var encoded);
            var raw = Ff7LzsDecoder.DecodeFieldFile(encoded);
            const int pointer = 0x03000000;
            return new FieldWalkmeshReader(
                a => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
                    a >= pointer && a + 4 <= pointer + raw.Length ? BitConverter.ToInt32(raw, a - pointer) : 0,
                a => a >= pointer && a + 2 <= pointer + raw.Length ? BitConverter.ToInt16(raw, a - pointer) : (short)0);
        }

        var planner = new FieldWalkmeshRoutePlanner(Reader(696), transitionProvider: field => catalog.ReadField(field).Transitions);
        var resolver = new FieldCrossFieldApproachResolver(catalog, source, planner, position => Reader(position.FieldId).Read(position).Walkmesh);
        FieldNavigationTarget Gateway(string id, string label, int destination, FieldNavigationTriggerLine line) =>
            new(696, FieldNavigationCategory.Exits, label, (line.StartX + line.EndX) / 2, (line.StartY + line.EndY) / 2,
                (line.StartZ + line.EndZ) / 2, id, CompletesOnArrival: true, DestinationFieldIds: [destination], TriggerLine: line);
        FieldNavigationTarget[] exits =
        [
            Gateway("gateway:696:0:693", "Back into the cave, beyond the fallen ice", 693, new(-349, -4560, 1439, -85, -4564, 1439)),
            Gateway("gateway:696:1:693", "Back into the cave by the icicle passage", 693, new(151, -4554, 1428, 335, -4550, 1426)),
            Gateway("gateway:696:2:697", "Along the ledge to the icicles", 697, new(1093, -2943, 1726, 1402, -2272, 1723)),
            Gateway("gateway:696:3:697", "Along the ledge to the way down", 697, new(-851, -3459, 1764, -626, -3970, 1768)),
        ];
        var sword = new FieldNavigationTarget(696, FieldNavigationCategory.Objects, "Enhance Sword", -297, -4045, 1411,
            "object:696:7", InteractionRadius: 60);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], objectTargetProvider: _ => [sword], exitTargetProvider: _ => exits), planner)
        {
            CrossFieldApproach = resolver.Resolve,
        };
        var transform = new FieldNavigationControlTransform(0);
        var eastLedge = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 696, 0, 1266, -2397, 1724, 32, 0);
        string? spoken = null;
        for (var guard = 0; guard < 8 && controller.CurrentCategory != FieldNavigationCategory.Objects; guard++)
        {
            spoken = controller.HandleAction(FieldNavigationAction.NextCategory, eastLedge, transform)?.Speech;
        }

        Equal(true, spoken is not null && spoken.Contains("Enhance Sword", StringComparison.Ordinal), $"the list is on the sword: {spoken}");
        Equal(false, spoken!.Contains("direction unavailable", StringComparison.Ordinal),
            $"the sword is not simply called unreachable from the eastern ledge: {spoken}");
        Contains(spoken, "going out through Back into the cave by the icicle passage",
            "the list says the native way round, the same the approach walks");
    }

    private static FieldExitLabelResolver Resolver(Func<int?> state) =>
        new(Names, () => "Great Glacier", readGlacierCorridorState: state);

    private static FieldMapNameResolution Names(int field) => field switch
    {
        >= 658 and <= 684 and not (661 or 662 or 666 or 678 or 682 or 684) => FieldMapNameResolution.Known(["Great Glacier"]),
        661 or 662 => FieldMapNameResolution.Known(["Frostbite Cave"]),
        666 or 678 or 682 or 684 => FieldMapNameResolution.Known(["Cave"]),
        _ => FieldMapNameResolution.Unknown
    };

    private static string Label(FieldExitLabelResolver resolver, int field, int entity, params int[] destinations) =>
        resolver.Resolve([new FieldNavigationTarget(field, FieldNavigationCategory.Exits, "Scripted exit", 0, 0, 0,
            $"script-exit:{field}:{entity}:{string.Join(',', destinations.Order())}", TriggerEntityId: entity,
            DestinationFieldIds: destinations.Order().ToArray())]).Single().Label;

    private static PassageMemory Passage(int field, int state)
    {
        var memory = new PassageMemory();
        memory.Set(ModuleAddress, FieldPositionReader.FieldModule);
        memory.Set(FieldAddress, (byte)(field & 0xFF), (byte)(field >> 8));
        memory.Set(CorridorStateAddress, (byte)state);
        return memory;
    }

    private sealed class PassageMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly Dictionary<uint, Queue<byte>> sequences = [];
        public HashSet<uint> Unreadable { get; } = [];

        public void Set(uint address, params byte[] values)
        {
            for (var index = 0; index < values.Length; index++) bytes[address + (uint)index] = values[index];
        }

        public void Set(uint address, int value) => Set(address, (byte)value);

        public void Sequence(uint address, params byte[] values) => sequences[address] = new Queue<byte>(values);

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                var address = virtualAddress + (uint)index;
                if (Unreadable.Contains(address)) return false;
                if (sequences.TryGetValue(address, out var queue) && queue.Count > 0)
                {
                    destination[index] = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                    continue;
                }

                if (!bytes.TryGetValue(address, out var value)) return false;
                destination[index] = value;
            }

            return true;
        }
    }

    private static void Contains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Great Glacier exit labels: {message}. Expected '{expected}' in '{text}'.");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Great Glacier exit labels: {message}. Expected {expected}, got {actual}.");
    }
}
