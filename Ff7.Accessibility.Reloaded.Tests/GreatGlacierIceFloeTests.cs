using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Great Glacier ice-floe crossing, hyou5_2 (field 665).
///
/// <para>The floes are a five-by-five grid, numbered 1..25 from the north-west corner.
/// Floe k's state is temporary 5[6+k]; ice_pos draws it as BG state 0 while that is 0 and
/// state 7 otherwise, and Cloud's script 3 lands on it only while it is 0. Landing on
/// floe k runs ice script k, which flips its four neighbours and not itself. 5[2] and 5[3]
/// hold the floe Cloud is on, 0 and 26 being the two ways off the ice, and 5[5] is the
/// shore the crossing was started from. What a sighted player sees - where Cloud is,
/// which floes are drawn landable, which way he faces - is read live, never predicted or
/// solved.</para>
/// </summary>
internal static class GreatGlacierIceFloeTests
{
    private static readonly uint ModuleAddress = FieldPositionReader.AddressCurrentModule;
    private static readonly uint FieldAddress = FieldPositionReader.AddressFieldId;
    private static readonly uint Temporary = FieldNavigationObjectReader.AddressTemporaryFieldBankBase;

    // hyou5_2's trigger header control byte. Up on the pad is facing 128 here.
    private const int Control = -128;
    private const byte FacingUp = 128;
    private const byte FacingRight = 64;
    private const byte FacingDown = 0;
    private const byte FacingLeft = 192;
    private const byte FacingUpRight = 96;

    // BGanime Init, the pattern the floes start in and are reset to after a fall.
    private static readonly int[] StartingStates =
    [
        1, 1, 0, 0, 1,
        0, 1, 0, 1, 0,
        1, 0, 0, 1, 0,
        0, 1, 1, 0, 1,
        1, 0, 0, 1, 1
    ];

    public static void Run()
    {
        TheJumpTableIsTheNativeDispatch();
        LandingTurnsOnlyTheOrthogonalNeighbours();
        TheReaderTakesOneCoherentFrameOfField665();
        TheReaderKnowsWhenCloudIsOnAFloe();
        StartingTheCrossingKeepsTheWholeGridOnRequest();
        LandingDescribesTheNewNeighbours();
        TurningSaysWhatLiesThatWay();
        LeavingTheIceIsAnnounced();
        OtherFieldsAndUnreadableFramesSayNothing();
        Console.WriteLine("Great Glacier ice floes: native jump rules, live reader and readout checked.");
    }

    private static void TheJumpTableIsTheNativeDispatch()
    {
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.Floe, 2),
            GreatGlacierIceFloePuzzle.ResolveJump(1, FacingRight), "floe 1 facing 64 reaches floe 2");
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.Floe, 6),
            GreatGlacierIceFloePuzzle.ResolveJump(1, FacingDown), "floe 1 facing 0 reaches floe 6");
        Equal(GreatGlacierIceFloeTargetKind.None, GreatGlacierIceFloePuzzle.ResolveJump(1, FacingUp).Kind,
            "nothing lies north of the top row except at floe 3");
        Equal(GreatGlacierIceFloeTargetKind.None, GreatGlacierIceFloePuzzle.ResolveJump(1, FacingLeft).Kind,
            "nothing lies west of the first column");
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.Floe, 1),
            GreatGlacierIceFloePuzzle.ResolveJump(2, 32),
            "any facing other than 0, 64 and 128 is the native west branch");
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.Floe, 1),
            GreatGlacierIceFloePuzzle.ResolveJump(2, FacingUpRight), "a diagonal is the west branch too");
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.NorthShore, 0),
            GreatGlacierIceFloePuzzle.ResolveJump(3, FacingUp), "floe 3 facing 128 reaches the north shore");
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.SouthShore, 0),
            GreatGlacierIceFloePuzzle.ResolveJump(23, FacingDown), "floe 23 facing 0 reaches the south shore");
        Equal(GreatGlacierIceFloeTargetKind.None, GreatGlacierIceFloePuzzle.ResolveJump(21, FacingDown).Kind,
            "only floe 23 reaches the south shore");
        Equal(GreatGlacierIceFloeTargetKind.None, GreatGlacierIceFloePuzzle.ResolveJump(5, FacingRight).Kind,
            "nothing lies east of the last column");
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.Floe, 8),
            GreatGlacierIceFloePuzzle.ResolveJump(13, FacingUp), "facing 128 is one row north");
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.Floe, 18),
            GreatGlacierIceFloePuzzle.ResolveJump(13, FacingDown), "facing 0 is one row south");
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.Floe, 14),
            GreatGlacierIceFloePuzzle.ResolveJump(13, FacingRight), "facing 64 is one column east");
        Equal(new GreatGlacierIceFloeTarget(GreatGlacierIceFloeTargetKind.Floe, 12),
            GreatGlacierIceFloePuzzle.ResolveJump(13, FacingLeft), "facing 192 is one column west");
        Equal(GreatGlacierIceFloeTargetKind.None, GreatGlacierIceFloePuzzle.ResolveJump(0, FacingUp).Kind,
            "off the ice there is no jump");
    }

    private static void LandingTurnsOnlyTheOrthogonalNeighbours()
    {
        Equal("2,6", string.Join(",", GreatGlacierIceFloePuzzle.Neighbours(1)), "corner floe");
        Equal("8,12,14,18", string.Join(",", GreatGlacierIceFloePuzzle.Neighbours(13)), "middle floe");
        Equal("18,22,24", string.Join(",", GreatGlacierIceFloePuzzle.Neighbours(23)), "bottom edge floe");
        Equal(5, GreatGlacierIceFloePuzzle.Row(23), "floe 23 is on row 5");
        Equal(3, GreatGlacierIceFloePuzzle.Column(23), "floe 23 is in column 3");
    }

    private static void TheReaderTakesOneCoherentFrameOfField665()
    {
        var memory = Lake(current: 23, landing: 23, fromNorth: false, StartingStates);
        var state = new GreatGlacierIceFloeReader(memory).Read();
        Equal(true, state is not null, "a coherent frame of field 665 is read");
        Equal(23, state!.CurrentFloe, "5[2] is the floe Cloud is on");
        Equal(false, state.EnteredFromNorth, "5[5] = 0 is the south shore");
        Equal(false, state.CanLand(1), "floe 1 starts blocked");
        Equal(true, state.CanLand(3), "floe 3 starts open");
        Equal(true, state.CanLand(22), "floe 22 starts open");

        var north = Lake(current: 3, landing: 3, fromNorth: true, StartingStates);
        Equal(true, new GreatGlacierIceFloeReader(north).Read()!.EnteredFromNorth, "5[5] = 1 is the north shore");

        var unreadable = Lake(23, 23, false, StartingStates);
        unreadable.Unreadable.Add(Temporary + 7);
        Equal(true, new GreatGlacierIceFloeReader(unreadable).Read() is null, "an unreadable floe is not guessed");

        var elsewhere = Lake(23, 23, false, StartingStates);
        elsewhere.Set(FieldAddress, 664 & 0xFF, 664 >> 8);
        Equal(true, new GreatGlacierIceFloeReader(elsewhere).Read() is null,
            "the temporary bank belongs to whichever field is loaded");

        var world = Lake(23, 23, false, StartingStates);
        world.Set(ModuleAddress, 3);
        Equal(true, new GreatGlacierIceFloeReader(world).Read() is null, "outside the field module there is no lake");

        var turning = Lake(23, 23, false, StartingStates);
        turning.Sequence(Temporary + 6 + 18, 0, 1);
        Equal(true, new GreatGlacierIceFloeReader(turning).Read() is null,
            "a floe that flips between the two reads is not yet known");

        var leaving = Lake(23, 23, false, StartingStates);
        leaving.Sequence(FieldAddress, 665 & 0xFF, 664 & 0xFF);
        Equal(true, new GreatGlacierIceFloeReader(leaving).Read() is null, "a field change around the read disowns it");

        foreach (var (index, value) in new[] { (2, 27), (3, 255), (5, 2), (7, 9) })
        {
            var invalid = Lake(23, 23, false, StartingStates);
            invalid.Set(Temporary + (uint)index, (byte)value);
            Equal(true, new GreatGlacierIceFloeReader(invalid).Read() is null,
                "a stable but invalid native value must not describe a made-up floe or grid");
        }
    }

    private static void TheReaderKnowsWhenCloudIsOnAFloe()
    {
        Equal(true, Read(23, 23).IsOnFloe, "5[2] = 5[3] = 23 is standing on floe 23");
        Equal(false, Read(18, 23).IsOnFloe, "mid-jump, 5[3] has moved and 5[2] has not");
        Equal(false, Read(0, 0).IsOnFloe, "0 is off the ice");
        Equal(false, Read(26, 26).IsOnFloe, "26 is off the ice");
        Equal(true, Read(18, 23).IsCrossing, "a jump retains ownership of movement");
        Equal(true, Read(0, 3).IsCrossing, "jumping onto the ice claims movement");
        Equal(true, Read(23, 26).IsCrossing, "jumping to shore releases movement only after landing");
        Equal(false, Read(26, 26).IsCrossing, "standing on shore allows navigation again");
    }

    private static void StartingTheCrossingKeepsTheWholeGridOnRequest()
    {
        var readout = new GreatGlacierIceFloeReadout();
        Equal<string?>(null, readout.Observe(665, Read(0, 0), FacingUp, Control), "on the shore nothing is said");
        var states = Landed(StartingStates, 23);
        var text = readout.Observe(665, Read(23, 23, states: states), FacingUp, Control);
        Equal(true, text is not null, "the first landing is announced");
        Contains(text!, "Row 5, column 3", "where Cloud stands");
        Contains(text, "up open", "floe 18 was opened by the landing");
        Contains(text, "down south shore", "floe 23 is the way back to the south shore");
        Contains(text, "left blocked", "floe 22 was blocked by the landing");
        Contains(text, "right open", "floe 24 was opened by the landing");
        Contains(text, "Facing up", "the facing the crossing started with");
        Contains(text, "Press R for the full grid", "the full layout is available on request");
        Equal(false, text.Contains("Row 1:", StringComparison.Ordinal), "the first landing does not read all 25 cells automatically");
        var landedGrid = GreatGlacierIceFloeReadout.DescribeGrid(Read(23, 23, states: states));
        Contains(landedGrid, "Row 1: blocked, blocked, open, open, blocked", "the whole grid a sighted player can see");
        Contains(landedGrid, "Row 5: blocked, blocked, open, open, blocked", "the requested grid reflects the landing");
        Equal<string?>(null, readout.Observe(665, Read(23, 23, states: states), FacingUp, Control),
            "nothing changed, nothing is said");

        var grid = GreatGlacierIceFloeReadout.DescribeGrid(Read(0, 0));
        Contains(grid, "Row 1: blocked, blocked, open, open, blocked", "the grid can be described from the shore");
        Equal(false, grid.Contains("You are on", StringComparison.Ordinal), "off the ice there is no floe to be on");
    }

    private static void LandingDescribesTheNewNeighbours()
    {
        var readout = new GreatGlacierIceFloeReadout();
        var states = Landed(StartingStates, 23);
        readout.Observe(665, Read(23, 23, states: states), FacingUp, Control);
        Equal<string?>(null, readout.Observe(665, Read(23, 18, states: states), FacingUp, Control),
            "mid-jump is not a landing");
        states = Landed(states, 18);
        var text = readout.Observe(665, Read(18, 18, states: states), FacingUp, Control);
        Equal(true, text is not null, "a landing is announced");
        Contains(text!, "Row 4, column 3", "the new floe");
        Contains(text, "up blocked", "floe 13 blocked by the landing");
        Contains(text, "down blocked", "floe 23, the one Cloud left, blocked too");
        Contains(text, "left open", "floe 17 opened");
        Contains(text, "right blocked", "floe 19 blocked");
        Equal(false, text.Contains("Row 1:", StringComparison.Ordinal), "a landing repeats only what changed around Cloud");
    }

    private static void TurningSaysWhatLiesThatWay()
    {
        var readout = new GreatGlacierIceFloeReadout();
        var states = Landed(StartingStates, 23);
        readout.Observe(665, Read(23, 23, states: states), FacingUp, Control);
        Equal("Facing right, open.", readout.Observe(665, Read(23, 23, states: states), FacingRight, Control),
            "turning to face the floe to the right");
        Equal("Facing down, south shore.", readout.Observe(665, Read(23, 23, states: states), FacingDown, Control),
            "turning to face the shore");
        Equal("Facing left, blocked.", readout.Observe(665, Read(23, 23, states: states), FacingLeft, Control),
            "turning to face a floe that cannot be landed on");
        Equal("Facing up-right, not straight; OK jumps left, blocked.",
            readout.Observe(665, Read(23, 23, states: states), FacingUpRight, Control),
            "a diagonal is taken by the native west branch");

        var corner = new GreatGlacierIceFloeReadout();
        Contains(corner.Observe(665, Read(1, 1, states: StartingStates), FacingUp, Control) ?? string.Empty,
            "up no floe, down open, left no floe, right blocked", "the edges of the grid are named");
        Equal<string?>(null, corner.Observe(665, Read(1, 1, states: StartingStates), FacingUp, Control),
            "the facing it started with is not announced again");
        Equal("Facing up-right, not straight; OK does nothing.",
            corner.Observe(665, Read(1, 1, states: StartingStates), FacingUpRight, Control),
            "the first column has no west branch");
    }

    private static void LeavingTheIceIsAnnounced()
    {
        var north = new GreatGlacierIceFloeReadout();
        north.Observe(665, Read(3, 3, fromNorth: false), FacingUp, Control);
        Equal("Reached the north shore.", north.Observe(665, Read(0, 0, fromNorth: false), FacingUp, Control),
            "floe 3 facing up leaves for the north shore");

        var south = new GreatGlacierIceFloeReadout();
        south.Observe(665, Read(23, 23, fromNorth: true), FacingDown, Control);
        Equal("Reached the south shore.", south.Observe(665, Read(26, 26, fromNorth: true), FacingDown, Control),
            "floe 23 facing down leaves for the south shore");

        var fell = new GreatGlacierIceFloeReadout();
        fell.Observe(665, Read(12, 12, fromNorth: false), FacingUp, Control);
        Equal("Cloud fell into the water. Back on the south shore; the floes are back as they started.",
            fell.Observe(665, Read(0, 0, fromNorth: false), FacingUp, Control),
            "a fall returns Cloud to the shore he started from");

        var fellNorth = new GreatGlacierIceFloeReadout();
        fellNorth.Observe(665, Read(12, 12, fromNorth: true), FacingUp, Control);
        Equal("Cloud fell into the water. Back on the north shore; the floes are back as they started.",
            fellNorth.Observe(665, Read(26, 26, fromNorth: true), FacingUp, Control),
            "26 after a fall from the north is still the north shore");
    }

    private static void OtherFieldsAndUnreadableFramesSayNothing()
    {
        var readout = new GreatGlacierIceFloeReadout();
        readout.Observe(665, Read(12, 12), FacingUp, Control);
        Equal<string?>(null, readout.Observe(665, null, FacingUp, Control), "an unreadable frame is not a change");
        Equal<string?>(null, readout.Observe(665, Read(12, 12), FacingUp, Control),
            "after a transient failure the same floe is not repeated");
        Equal<string?>(null, readout.Observe(664, null, FacingUp, Control), "another field says nothing");
        Equal<string?>(null, readout.Observe(665, Read(0, 0), FacingUp, Control),
            "coming back to the lake is not falling off the ice");
    }

    /// <summary>
    /// Every rule above, checked against the installed hyou5_2 rather than restated: the
    /// jump dispatch in Cloud's script 3 for every floe and every facing byte, the landing
    /// gate, the neighbours each ice script flips, the floe each BG parameter draws, the
    /// two starting lines, the fall handler and the field's control byte.
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var catalog = new FieldScriptNavigationCatalog(root);
        var scripts = catalog.ReadAllScriptOpcodes(GreatGlacierIceFloePuzzle.FieldId);
        IReadOnlyList<FieldScriptOpcodeDefinition> Script(string entity, int id) =>
            scripts.Single(s => s.EntityName == entity && s.ScriptId == id).Opcodes;

        var source = new FlevelDataSource(root);
        Equal(true, source.TryReadField(GreatGlacierIceFloePuzzle.FieldId, out var encoded), "hyou5_2 is installed");
        var field = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var triggers = BitConverter.ToInt32(field, 6 + 7 * 4);
        Equal(-128, (int)(sbyte)field[triggers + 4 + 9],
            "hyou5_2's control byte makes Up facing 128, the direction that crosses north");

        var cloud = Script("cloud", 3);
        var dispatch = cloud.Select((op, index) => (op, index))
            .Single(entry => entry.op.Opcode == 0xB7 && entry.op.Bytes[3] == 4).index + 1;
        var checkedJumps = 0;
        var floePositions = new Dictionary<int, (int X, int Y)>();
        for (var floe = 1; floe <= 25; floe++)
        {
            for (var facing = 0; facing < 256; facing++)
            {
                var expected = GreatGlacierIceFloePuzzle.ResolveJump(floe, (byte)facing);
                var open = RunDispatch(cloud, dispatch, floe, (byte)facing, _ => 0);
                var blocked = RunDispatch(cloud, dispatch, floe, (byte)facing, _ => 1);
                switch (expected.Kind)
                {
                    case GreatGlacierIceFloeTargetKind.None:
                        Equal(floe, open.Landing, $"floe {floe} facing {facing} does not jump");
                        Equal(floe, blocked.Landing, $"floe {floe} facing {facing} does not jump when blocked");
                        break;
                    case GreatGlacierIceFloeTargetKind.NorthShore:
                        Equal(0, open.Landing, $"floe {floe} facing {facing} reaches the north shore");
                        Equal(0, blocked.Landing, "the shore is never blocked");
                        break;
                    case GreatGlacierIceFloeTargetKind.SouthShore:
                        Equal(26, open.Landing, $"floe {floe} facing {facing} reaches the south shore");
                        Equal(26, blocked.Landing, "the shore is never blocked");
                        break;
                    default:
                        var target = expected.Floe;
                        Equal(target, open.Landing, $"floe {floe} facing {facing} reaches floe {target}");
                        Equal(floe, blocked.Landing, $"floe {floe} facing {facing} cannot land on a blocked floe");
                        Equal(floe, RunDispatch(cloud, dispatch, floe, (byte)facing, f => f == target ? 1 : 0).Landing,
                            "only the destination's own state gates the jump");
                        Equal(target, RunDispatch(cloud, dispatch, floe, (byte)facing, f => f == target ? 0 : 1).Landing,
                            "nothing but the destination's own state gates the jump");
                        floePositions[target] = open.Jump ?? throw new InvalidOperationException("a landing without a native JUMP");
                        checkedJumps++;
                        break;
                }
            }
        }

        for (var floe = 1; floe <= 25; floe++)
        {
            var (x, y) = floePositions[floe];
            if (GreatGlacierIceFloePuzzle.Column(floe) < 5)
            {
                Equal(true, floePositions[floe + 1].X > x, $"floe {floe + 1} lies east of floe {floe}");
            }

            if (GreatGlacierIceFloePuzzle.Row(floe) < 5)
            {
                Equal(true, floePositions[floe + 5].Y < y, $"floe {floe + 5} lies south of floe {floe}");
            }
        }

        for (var floe = 1; floe <= 25; floe++)
        {
            var ice = Script("ice", floe);
            foreach (var start in new[] { 0, 1 })
            {
                var states = Enumerable.Repeat(start, 256).ToArray();
                RunStraight(ice, states);
                var flipped = Enumerable.Range(1, 25).Where(f => states[6 + f] != start).ToArray();
                Equal(string.Join(",", GreatGlacierIceFloePuzzle.Neighbours(floe)), string.Join(",", flipped),
                    $"ice script {floe} flips exactly its neighbours from {start}");
            }

            var drawn = new int[256];
            for (var other = 1; other <= 25; other++) drawn[6 + other] = other == floe ? 0 : 1;
            var layers = RunStraight(Script("ice_pos", 1), drawn);
            Equal(true, layers.Contains((floe, 0)) && !layers.Contains((floe, 7)),
                $"BG parameter {floe} draws floe {floe} in state 0 from 5[{6 + floe}] = 0");
            Equal(true, layers.Where(l => l.Parameter != floe).All(l => l.State == 7),
                $"every other floe is drawn in state 7 while its flag is not 0");
        }

        var starting = new int[256];
        RunStraight(Script("BGanime", 0), starting);
        Equal(string.Join(",", StartingStates), string.Join(",", starting.Skip(7).Take(25)),
            "BGanime Init sets the pattern the tests start from");

        foreach (var (line, value) in new[] { ("line50a", 0), ("line50b", 1) })
        {
            var hex = string.Concat(Script(line, 5).Select(o => Convert.ToHexString(o.Bytes.ToArray())));
            Equal(true, hex.StartsWith($"805005{value:X2}0317A3", StringComparison.Ordinal),
                $"{line} records its shore in 5[5] and starts Cloud's crossing script");
        }

        var fall = cloud.Select(o => Convert.ToHexString(o.Bytes.ToArray())).ToArray();
        var north = Array.IndexOf(fall, "A50000B10040012B000D00");
        var south = Array.IndexOf(fall, "A50000A300D2FC1F006F00");
        Equal(true, north > 0 && south > north, "the fall handler places Cloud on either shore");
        Equal("8050021A", fall.Skip(north).First(h => h.StartsWith("805002", StringComparison.Ordinal)),
            "a fall back to the north shore leaves 26 in 5[2]");
        Equal("80500200", fall.Skip(south).First(h => h.StartsWith("805002", StringComparison.Ordinal)),
            "a fall back to the south shore leaves 0 in 5[2]");
        Equal(true, fall.Take(north).Any(h => h.StartsWith("1450050001", StringComparison.Ordinal)),
            "the shore Cloud is returned to is chosen by 5[5]");

        Console.WriteLine($"Great Glacier ice floes: {checkedJumps} native floe landings, 25 ice scripts and the fall handler match the installed hyou5_2.");
    }

    /// <summary>
    /// Runs Cloud's jump dispatch from just after its GETDIR, with the floe states given,
    /// until the frame wait every branch meets. Returns where 5[3] ends and the JUMP taken.
    /// </summary>
    private static (int Landing, (int X, int Y)? Jump) RunDispatch(IReadOnlyList<FieldScriptOpcodeDefinition> script,
        int start, int floe, byte facing, Func<int, int> state)
    {
        var bank = new int[256];
        bank[2] = floe;
        bank[3] = floe;
        bank[4] = facing;
        for (var f = 1; f <= 25; f++) bank[6 + f] = state(f);
        (int X, int Y)? jump = null;
        var byOffset = Index(script);
        var offset = script[start].ByteIndex;
        for (var steps = 0; steps < 2000; steps++)
        {
            var op = byOffset[offset];
            var b = op.Bytes.ToArray();
            if (op.Opcode == 0x24)
            {
                return (bank[3], jump);
            }

            var next = offset + b.Length;
            switch (op.Opcode)
            {
                case 0x10: next = offset + 1 + b[1]; break;
                case 0x11: next = offset + 1 + BitConverter.ToUInt16(b, 1); break;
                case 0x14:
                case 0x15:
                    if (!Compare(b, bank)) next = offset + 5 + (op.Opcode == 0x15 ? BitConverter.ToUInt16(b, 5) : b[5]);
                    break;
                case 0x80:
                    Equal(5, b[1] >> 4, "the dispatch writes only the temporary bank");
                    bank[b[2]] = (b[1] & 15) == 5 ? bank[b[3]] : b[3];
                    break;
                case 0xC0:
                    jump = (BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5));
                    break;
            }

            offset = next;
        }

        throw new InvalidOperationException("Great Glacier ice floes: the jump dispatch did not reach its frame wait");
    }

    /// <summary>
    /// A forward-only run of an ice, ice_pos or BGanime script over the temporary bank:
    /// their tests, jumps and SETBYTEs, returning the BGON calls made. Requests for the
    /// flip animations are skipped; they draw, they do not decide.
    /// </summary>
    private static List<(int Parameter, int State)> RunStraight(IReadOnlyList<FieldScriptOpcodeDefinition> script, int[] bank)
    {
        var layers = new List<(int, int)>();
        var byOffset = Index(script);
        var offset = script[0].ByteIndex;
        for (var steps = 0; steps < 5000; steps++)
        {
            if (!byOffset.TryGetValue(offset, out var op) || op.Opcode == 0x00)
            {
                return layers;
            }

            var b = op.Bytes.ToArray();
            var next = offset + b.Length;
            switch (op.Opcode)
            {
                case 0x10: next = offset + 1 + b[1]; break;
                case 0x12: next = offset - b[1]; break;
                case 0x14:
                    if (!Compare(b, bank)) next = offset + 5 + b[5];
                    break;
                case 0x80:
                    if (b[1] >> 4 == 5) bank[b[2]] = (b[1] & 15) == 5 ? bank[b[3]] : b[3];
                    break;
                case 0x95:
                    if ((b[1] & 15) == 5) bank[b[2]]++;
                    break;
                case 0xE0:
                    var parameter = (b[1] >> 4) == 5 ? bank[b[2]] : b[2];
                    layers.Add((parameter, b[3]));
                    break;
            }

            offset = next;
        }

        throw new InvalidOperationException("Great Glacier ice floes: a straight-line script did not return");
    }

    private static Dictionary<int, FieldScriptOpcodeDefinition> Index(IReadOnlyList<FieldScriptOpcodeDefinition> script) =>
        script.ToDictionary(o => o.ByteIndex);

    private static bool Compare(byte[] b, int[] bank)
    {
        var left = b[1] >> 4 == 5 ? bank[b[2]] : b[2];
        var right = (b[1] & 15) == 5 ? bank[b[3]] : b[3];
        return b[4] switch
        {
            0 => left == right,
            1 => left != right,
            2 => left > right,
            3 => left < right,
            4 => left >= right,
            5 => left <= right,
            _ => throw new InvalidOperationException($"Great Glacier ice floes: unexpected comparison {b[4]}")
        };
    }

    private static GreatGlacierIceFloeState Read(int current, int landing, bool fromNorth = false, int[]? states = null) =>
        new GreatGlacierIceFloeReader(Lake(current, landing, fromNorth, states ?? StartingStates)).Read()
        ?? throw new InvalidOperationException("Great Glacier ice floes: test lake was unreadable");

    /// <summary>Ice script k: its neighbours flip, it does not.</summary>
    private static int[] Landed(int[] states, int floe)
    {
        var next = (int[])states.Clone();
        foreach (var neighbour in GreatGlacierIceFloePuzzle.Neighbours(floe))
        {
            next[neighbour - 1] = next[neighbour - 1] == 0 ? 1 : 0;
        }

        return next;
    }

    private static LakeMemory Lake(int current, int landing, bool fromNorth, int[] states)
    {
        var memory = new LakeMemory();
        memory.Set(ModuleAddress, FieldPositionReader.FieldModule);
        memory.Set(FieldAddress, 665 & 0xFF, 665 >> 8);
        for (var index = 0; index < 256; index++)
        {
            memory.Set(Temporary + (uint)index, 0);
        }

        memory.Set(Temporary + 2, (byte)current);
        memory.Set(Temporary + 3, (byte)landing);
        memory.Set(Temporary + 5, fromNorth ? (byte)1 : (byte)0);
        for (var floe = 1; floe <= 25; floe++)
        {
            memory.Set(Temporary + 6 + (uint)floe, (byte)states[floe - 1]);
        }

        return memory;
    }

    private sealed class LakeMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly Dictionary<uint, Queue<byte>> sequences = [];
        public HashSet<uint> Unreadable { get; } = [];

        public void Set(uint address, params byte[] values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                bytes[address + (uint)index] = values[index];
            }
        }

        public void Set(uint address, int value) => Set(address, (byte)value);

        public void Sequence(uint address, params byte[] values) => sequences[address] = new Queue<byte>(values);

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                var address = virtualAddress + (uint)index;
                if (Unreadable.Contains(address))
                {
                    return false;
                }

                if (sequences.TryGetValue(address, out var queue) && queue.Count > 0)
                {
                    destination[index] = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                    continue;
                }

                if (!bytes.TryGetValue(address, out var value))
                {
                    return false;
                }

                destination[index] = value;
            }

            return true;
        }
    }

    private static void Contains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Great Glacier ice floes: {message}. Expected '{expected}' in '{text}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Great Glacier ice floes: {message}. Expected {expected}, got {actual}.");
        }
    }
}
