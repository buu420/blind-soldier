using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class MidgarTunnelStoryTests
{
    internal static void Run(Func<int, FieldWalkmeshReader>? createWalkmeshReader = null)
    {
        foreach (var (_, check) in Cases(createWalkmeshReader)) check();
    }

    internal static IEnumerable<(string Name, Action Check)> Cases(Func<int, FieldWalkmeshReader>? createWalkmeshReader)
    {
        yield return ("entry ladder gives native manual guidance", EntryLadderGivesManualGuidance);
        yield return ("pre-encounter tunnel visits have onward guidance", BeforeEncounterHasOnwardGuidance);
        yield return ("post-encounter return visits lead to the bridge", AfterEncounterLeadsToTheBridge);
        yield return ("disabled lines and invalid section reads have no guessed route", DisabledAndInvalidSectionsHaveNoRoute);
        yield return ("after the bridge battle the return route stays guided until Hojo", ReturnRouteStaysGuidedThroughMoment1603);
        yield return ("late Midgar exits are named by their native features", LateMidgarExitsNameNativeFeatures);
        yield return ("late Midgar labels follow native lines, not entity numbers", LateMidgarLabelsFollowNativeLines);
        if (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") is not { Length: > 0 } root) yield break;
        yield return ("tunnel lines bind to installed native scripts", () => LinesBindToNativeScripts(root));
        yield return ("late Midgar labels hold on installed stock and Echo-S layouts", () => LateMidgarLabelsHoldOnInstalledLayouts(root));
        if (createWalkmeshReader is not null)
            yield return ("tunnel guidance routes from native arrivals", () => RoutesFromNativeArrivals(createWalkmeshReader));
    }

    private static void EntryLadderGivesManualGuidance()
    {
        foreach (var moment in new[] { 1601, 1602, 1603 })
        {
            var state = new State(moment, 4, moment >= 1602);
            var target = Only(state, 735, 9);
            Contains(target.ManualNavigationGuidance, "Hold Up", "native ladder input is spoken");
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]));
            var position = Position(735, 9);
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, position, new(0));
            while (controller.CurrentCategory != FieldNavigationCategory.Story)
                _ = controller.HandleAction(FieldNavigationAction.NextCategory, position, new(0));
            var result = controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, new(0));
            Contains(result?.Speech, "Hold Up", "pursuit explains the ordinary manual control");
            Equal(false, controller.BeaconEnabled, "manual ladder instruction cannot start a walking route");
            Equal(false, controller.TryResolveAutomaticInput(position, new(0), 80, out var input),
                "the entry ladder cannot own automatic movement");
            Equal(FieldNavigationInput.None, input, "manual ladder produces no game input");
        }
    }

    private static void BeforeEncounterHasOnwardGuidance()
    {
        foreach (var section in Enumerable.Range(0, 10).Select(n => n * 2))
        {
            var state = new State(1601, section, false);
            Equal(section <= 2 ? 16 : 15, Entity(Only(state, 736)),
                $"tunnel_4 section {section} leads back to the main route before the encounter");
        }
        foreach (var section in Enumerable.Range(0, 9).Select(n => n * 2 + 1))
        {
            var state = new State(1601, section, false);
            Equal(section <= 3 ? 12 : 11, Entity(Only(state, 737)),
                $"tunnel_5 section {section} approaches the actual encounter floor");
        }
        Equal(18, Entity(Only(new State(1601, 4, false), 778)),
            "the native crossing is still the only step when the encounter is ahead");
    }

    private static void AfterEncounterLeadsToTheBridge()
    {
        // 1603 (md8brdg2 line_rb, after the bridge battle) leaves these screens' scripts exactly as
        // at 1602: none of 733-737 or 778 tests 2[0], so the same gates lead back to the bridge.
        foreach (var moment in new[] { 1602, 1603 })
        {
            foreach (var section in Enumerable.Range(0, 10).Select(n => n * 2))
                Equal(section <= 2 ? 16 : 15, Entity(Only(new State(moment, section, true), 736)),
                    $"moment {moment}: tunnel_4 section {section} returns to the bridge's own section");
            foreach (var section in Enumerable.Range(0, 9).Select(n => n * 2 + 1))
                Equal(section == 1 ? 12 : section == 3 ? 10 : 11,
                    Entity(Only(new State(moment, section, true), 737)),
                    $"moment {moment}: tunnel_5 section {section} uses the onward route after the encounter");
            Equal(19, Entity(Only(new State(moment, 4, true), 778)),
                $"moment {moment}: both fighting and declining settle the native encounter before climbing on");
            Equal(0, new State(moment, 4, false).Reader().ReadTargets(Position(778)).Count,
                $"moment {moment}: a chapter number alone cannot skip an unsettled encounter");
        }
    }

    private static void DisabledAndInvalidSectionsHaveNoRoute()
    {
        var state = new State(1601, 5, false) { DisabledLine = 11 };
        Equal(0, state.Reader().ReadTargets(Position(737)).Count,
            "the native line must actually be enabled");
        // 1600 is the landing before the manhole; 1604 is set by canon_2's Hojo, after which the
        // raid's tunnels are not revisited.
        foreach (var moment in new[] { 1600, 1604 })
            Equal(0, new State(moment, 4, true).Reader().ReadTargets(Position(736)).Count,
                "tunnel objectives stay in the active chapter");
        foreach (var section in new[] { 19, 255 })
            foreach (var field in new[] { 736, 737, 778 })
                Equal(0, new State(1601, section, false).Reader().ReadTargets(Position(field)).Count,
                    $"field {field} rejects unavailable section {section}");
        foreach (var field in new[] { 737, 778 })
            Equal(0, new State(1601, 0, false).Reader().ReadTargets(Position(field)).Count,
                $"section zero belongs to tunnel_4, not field {field}");
        Equal(0, new State(1601, 3, false).Reader().ReadTargets(Position(736)).Count,
            "a section belonging to the other reused screen is not a guessed tunnel_4 route");
        Equal(0, new State(1601, 2, false).Reader().ReadTargets(Position(737)).Count,
            "nor is it a guessed tunnel_5 route");
    }

    /// <summary>
    /// 1603 is written by md8brdg2's line_rb after the bridge battle (with 15[128] bit 3) and 1604
    /// by canon_2's Hojo; fields 733-737 and 778 never test the moment. Between them the party
    /// can walk back down through the tunnels to the scaffold, and the rows that already point
    /// toward the bridge keep doing so under the same native gates: the collapsed walkway
    /// (15[131] bit 3), the floor the leader stands on, the native line being enabled, the
    /// tunnel section (15[129]) and the settled encounter (15[128] bit 2).
    /// </summary>
    private static void ReturnRouteStaysGuidedThroughMoment1603()
    {
        // One triangle from each floor MidgarRaid.ps1 names.
        const ushort upperCatwalk = 5, subwayCatwalk = 122, firstCatwalk = 165, scaffoldBelowHighPlatform = 146; // 733
        const ushort pipes = 24, collapsedFloor = 182, walkway = 58, ledgeBelowPipes = 223;                     // 734
        static string Labels(State state, int field, ushort triangle = 0) =>
            string.Join(" | ", state.Reader().ReadTargets(Position(field, triangle)).Select(target => target.Label));

        foreach (var moment in new[] { 1601, 1602, 1603 })
        {
            var collapsed = new State(moment, 4, moment >= 1602) { WalkwayCollapsed = true, BridgeBattleWon = moment == 1603 };
            Equal("Go through the duct", Labels(collapsed, 733, upperCatwalk), $"moment {moment}: the upper catwalk's way on is the duct");
            Equal("Drop down to the subway", Labels(collapsed, 733, subwayCatwalk), $"moment {moment}: the subway catwalk drops to the tunnels");
            Equal("Take the way up out of the pipes", Labels(collapsed, 734, pipes), $"moment {moment}: the pipes lead up to the subway catwalk");
            Equal("Climb the ladder on the left back up", Labels(collapsed, 734, collapsedFloor), $"moment {moment}: the collapsed floor's only way on");
        }

        var after = new State(1603, 4, true) { WalkwayCollapsed = true, BridgeBattleWon = true };
        Contains(Only(after, 735, 3).ManualNavigationGuidance, "Hold Up", "from the tunnel end Up still returns to the tunnels");
        foreach (var (line, field, triangle) in new[] { (11, 733, upperCatwalk), (10, 733, subwayCatwalk), (8, 734, collapsedFloor) })
            Equal("", Labels(new State(1603, 4, true) { WalkwayCollapsed = true, BridgeBattleWon = true, DisabledLine = line }, field, triangle),
                $"1603: field {field} line {line} must actually be enabled");
        Equal("", Labels(after, 733, scaffoldBelowHighPlatform), "1603: a floor no reviewed row names is not guessed");
        Equal("", Labels(after, 734, ledgeBelowPipes), "1603: nor is the ledge below the pipes");
        var standing = new State(1603, 4, true) { BridgeBattleWon = true };
        foreach (var (field, triangle) in new[] { (733, upperCatwalk), (733, subwayCatwalk), (734, pipes), (734, collapsedFloor) })
            Equal("", Labels(standing, field, triangle), $"1603: field {field} t{triangle} needs the native walkway collapse");

        // The rows that point elsewhere keep their own range.
        Equal("", Labels(standing, 733, firstCatwalk), "1603: the second ladder down stays a 1601 step");
        Equal("", Labels(standing, 734, walkway), "1603: the standing walkway stays a 1601 step");
        Equal("Take the passage on the upper left",
            Labels(new State(1603, 3, true) { WalkwayCollapsed = true, BridgeBattleWon = true }, 737),
            "1603: section 3 goes on through the upper-left passage, not back down");
        Equal("", Labels(new State(1603, 4, false) { WalkwayCollapsed = true, BridgeBattleWon = true }, 778),
            "1603: the floor crossing is only for an encounter still ahead");
        Equal("", Labels(new State(1603, 3, true) { WalkwayCollapsed = true }, 738),
            "1603: the bridge's walk on stays a 1602 step while the battle is unsettled");
        Equal("Take the stairs on up", Labels(after, 738), "1603: the existing cannon row still follows the battle");
        foreach (var (section, field) in new[] { (2, 737), (3, 736), (5, 778), (19, 736) })
            Equal("", Labels(new State(1603, section, true) { WalkwayCollapsed = true, BridgeBattleWon = true }, field),
                $"1603: field {field} rejects section {section}");

        // canon_2's Hojo writes 1604; none of these rows outlives it.
        var hojo = new State(1604, 4, true) { WalkwayCollapsed = true, BridgeBattleWon = true };
        foreach (var (field, triangle) in new[] { (733, upperCatwalk), (733, subwayCatwalk), (734, pipes), (734, collapsedFloor), (735, (ushort)9), (735, (ushort)3), (778, (ushort)0) })
            Equal("", Labels(hojo, field, triangle), $"1604: field {field} t{triangle} has no raid step");
        foreach (var section in Enumerable.Range(0, 19))
            foreach (var field in new[] { 736, 737 })
                Equal("", Labels(new State(1604, section, true) { WalkwayCollapsed = true, BridgeBattleWon = true }, field),
                    $"1604: field {field} section {section} has no raid step");
    }

    private static void LinesBindToNativeScripts(string root)
    {
        var scripts = new FieldScriptNavigationCatalog(root);
        foreach (var row in FieldStoryEventCatalog.CreateAllFields().Where(r => r.FieldId is 736 or 737 or 778))
        {
            var native = scripts.ReadAllScriptOpcodes(row.FieldId)
                .Single(s => s.EntityId == row.EntityId && s.ScriptId == 0).Opcodes
                .Single(op => op.Opcode == 0xD0).Bytes.ToArray();
            var line = new FieldNavigationTriggerLine(
                BitConverter.ToInt16(native, 1), BitConverter.ToInt16(native, 3), BitConverter.ToInt16(native, 5),
                BitConverter.ToInt16(native, 7), BitConverter.ToInt16(native, 9), BitConverter.ToInt16(native, 11));
            Equal(line, row.TriggerLine!.Value, "Story uses the installed LINE rather than a guessed waypoint");
            Equal(row.EntityId, row.RequiredEnabledLineEntityId!.Value,
                "the same native entity owns the line-enable prerequisite");
        }
        var entry = scripts.ReadAllScriptOpcodes(733).Single(s => s.EntityId == 10 && s.ScriptId == 2).Opcodes
            .Single(op => op.Opcode == 0x60).Bytes.ToArray();
        Equal((ushort)735, BitConverter.ToUInt16(entry, 1), "the catwalk enters sbwy4_22");
        Equal((ushort)9, BitConverter.ToUInt16(entry, 7), "entry arrives on the native upper ladder triangle");
        var ladder = scripts.ReadAllScriptOpcodes(735).Single(s => s.EntityId == 1 && s.ScriptId == 0).Opcodes
            .First(op => op.Opcode == 0xC2).Bytes.ToArray();
        Equal((byte)1, ladder[11], "the entry's native ladder operand selects Up");
        Equal((ushort)2, BitConverter.ToUInt16(ladder, 9), "the entry ladder lands on its native onward triangle");
        var upperLeft = scripts.ReadAllScriptOpcodes(737).Single(s => s.EntityId == 10 && s.ScriptId == 2).Opcodes;
        Equal("14F081010012", Convert.ToHexString(upperLeft[0].Bytes.ToArray()),
            "the upper-left passage accepts section one");
        Equal("7C0F81", Convert.ToHexString(upperLeft.Single(op => op.Opcode == 0x7C).Bytes.ToArray()),
            "then decrements the section to zero");
        Equal((ushort)736, BitConverter.ToUInt16(upperLeft.First(op => op.Opcode == 0x60).Bytes.ToArray(), 1),
            "section zero is a reachable tunnel_4 screen");
    }

    private static void RoutesFromNativeArrivals(Func<int, FieldWalkmeshReader> create)
    {
        var arrivals = new (int Field, int X, int Y, ushort Triangle)[]
        {
            (736, 289, -427, 54), (736, 652, 2138, 88),
            (737, 3, 464, 47), (737, -520, 1741, 19), (737, 532, 1723, 50)
        };
        foreach (var (field, x, y, triangle) in arrivals)
        {
            foreach (var moment in new[] { 1601, 1602, 1603 })
            {
                foreach (var section in field == 736 ? new[] { 0, 2, 4, 6, 18 } : new[] { 1, 3, 5, 17 })
                {
                    var position = new FieldPositionSnapshot(1, (ushort)field, 0, x, y, 0, triangle, 0);
                    var state = new State(moment, section, moment >= 1602) { BridgeBattleWon = moment == 1603 };
                    var target = state.Reader().ReadTargets(position).Single();
                    var planner = new FieldWalkmeshRoutePlanner(create(field));
                    Equal(true, planner.TryBuildRoute(position, target, out _),
                        $"field {field}, native arrival {triangle}, section {section}, moment {moment}: {planner.LastDiagnostic}");
                }
            }
        }
    }

    // Installed MPNAMs of the fields the late Midgar labels touch ("sector 0" is lower case in the game).
    private static readonly Dictionary<int, string> LateMidgarMapNames = new()
    {
        [224] = "Plate Section", [225] = "sector 0", [227] = "Shinra Bldg.", [228] = "Shinra Bldg.",
        [234] = "Shinra Bldg. 1f. lobby", [732] = "Midgar, Sector 8", [733] = "Sector 8, Underground",
        [734] = "Sector 8, Underground", [735] = "Sector 8, Underground", [736] = "Winding Tunnel",
        [737] = "Winding Tunnel", [738] = "Midgar, Sector 8", [778] = "Winding Tunnel"
    };

    private static readonly FieldNavigationTriggerLine TunnelWayUp = new(890, 3542, 9, 687, 1611, 0);
    private static readonly FieldNavigationTriggerLine TunnelWayDown = new(-83, -665, 0, 502, -570, 0);
    private static readonly FieldNavigationTriggerLine TunnelLadderUp = new(-24, -273, 0, 41, -445, 0);

    // Every native way out of these screens: field, gateway index (-1 for a LINE), installed
    // line, the destinations its scripts reach, and the label it must carry.
    private static readonly (int Field, int Gateway, FieldNavigationTriggerLine Line, int[] Destinations, string Label)[] LateMidgarWays =
    [
        (733, -1, new(-3717, -919, -326, -3678, -880, -326), [734], "Ladder down from the upper catwalk"),
        (733, -1, new(-3880, -449, 512, -3853, -342, 512), [732], "Ladder up through the manhole to Midgar, Sector 8"),
        (733, -1, new(-2531, -665, -514, -2525, -578, -514), [734], "Ladder down to the walkway"),
        (733, -1, new(-796, 1948, -179, -700, 1852, -179), [735], "Drop down to the subway"),
        (733, -1, new(-2693, 563, -326, -2589, 495, -326), [734], "Duct down to the pipes"),
        (733, -1, new(1111, 879, 64, 1111, 759, 64), [734], "Duct down to the ledge across the gap"),
        (733, 0, new(-241, 619, -854, -126, 645, -828), [734], "Way down into the pipes"),
        (733, 1, new(-3940, 1205, -1659, -3588, 1205, -1659), [734], "Way back to the walkway"),
        (734, -1, new(-3676, -1026, -986, -3646, -966, -986), [733], "Ladder up to the upper catwalk"),
        (734, -1, new(-2725, -274, -1660, -2634, -247, -1660), [733], "Ladder back up toward the manhole"),
        (734, -1, new(141, 432, -1465, 61, 412, -1468), [733], "Duct up to the upper catwalk"),
        (734, -1, new(1170, -127, -2391, 1232, -300, -2391), [733], "Duct across the gap"),
        (734, 0, new(-248, 886, -456, -124, 886, -462), [733], "Way up out of the pipes"),
        (734, 1, new(-3920, 1108, -1660, -3588, 1108, -1660), [733], "Way to the ladder platform"),
        (736, -1, TunnelWayUp, [737], "Way up the tunnel"),
        (736, -1, TunnelWayDown, [737], "Way down the tunnel"),
        (736, -1, TunnelLadderUp, [225], "Ladder up to sector 0"),
        (737, -1, new(-722, 1767, 0, -543, 1902, 0), [736, 738], "Passage on the upper left"),
        (737, -1, new(550, 1890, 0, 721, 1754, 0), [736, 778], "Passage on the upper right"),
        (737, -1, new(-215, 368, 0, 200, 379, 0), [736, 778], "Way down the tunnel"),
        (778, -1, TunnelWayUp, [737], "Way up the tunnel"),
        (778, -1, TunnelWayDown, [737], "Way down the tunnel"),
        (778, -1, TunnelLadderUp, [735], "Ladder up to Sector 8, Underground"),
        (225, -1, new(-4521, -13755, 485, -4688, -13591, 485), [736], "Way down to Winding Tunnel"),
        (225, 0, new(-4742, -13748, 485, -4522, -13748, 485), [224], "Exit to Plate Section"),
        (225, 1, new(-3669, -10734, 485, -3469, -10806, 485), [227], "Exit to Shinra Bldg."),
        (227, 0, new(-1272, -3458, -222, 1014, -3363, -222), [225], "Exit to sector 0"),
        (227, 1, new(-1272, -3458, -222, -1465, -2956, -221), [225], "Exit to sector 0"),
        (227, 2, new(-1492, -2830, -161, -1465, -2956, -221), [225], "Exit to sector 0"),
        (227, 3, new(-275, -1976, 0, 289, -1990, 0), [234], "Front entrance to Shinra Bldg. 1f. lobby"),
        (227, 4, new(-1394, -2335, -73, -1439, -1995, -73), [228], "Way to the emergency stairs")
    ];

    private static void LateMidgarExitsNameNativeFeatures()
    {
        foreach (var way in LateMidgarWays)
        {
            var target = way.Gateway < 0
                ? LateMidgarScriptExit(way.Field, 30, way.Line, way.Destinations)
                : LateMidgarGateway(way.Field, way.Gateway, way.Destinations[0], way.Line);
            Equal(way.Label, LateMidgarLabel(target), $"{target.StableId} names its native feature");
        }

        foreach (var field in LateMidgarWays.GroupBy(way => way.Field))
        {
            var featureLabels = field.Select(way => way.Label).Where(label => !label.StartsWith("Exit to", StringComparison.Ordinal)).ToArray();
            Equal(featureLabels.Length, featureLabels.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                $"field {field.Key} names each of its ways out differently");
        }

        // The guards decide where the upper-left passage leads; only then is its destination named.
        var upperLeft = LateMidgarScriptExit(737, 10, new(-722, 1767, 0, -543, 1902, 0), 736, 738);
        Equal("Passage on the upper left to Midgar, Sector 8", LateMidgarLabel(upperLeft with { DestinationFieldIds = [738] }),
            "section three's passage onto the bridge screen");
        Equal("Passage on the upper left", LateMidgarLabel(upperLeft with { DestinationFieldIds = [736] }),
            "section one's passage stays inside the Winding Tunnel");
    }

    private static void LateMidgarLabelsFollowNativeLines()
    {
        // Echo-S inserts tarks2 before tunnel_6's lines: its entity 20 is jump_u, stock's is jump_d.
        Equal("Way up the tunnel", LateMidgarLabel(LateMidgarScriptExit(778, 20, TunnelWayUp, 737)),
            "a renumbered line keeps the label of its own native line");
        Equal("Way down the tunnel", LateMidgarLabel(LateMidgarScriptExit(778, 20, TunnelWayDown, 737)),
            "stock entity 20 is the way down");
        Equal("Ladder up to Sector 8, Underground", LateMidgarLabel(LateMidgarScriptExit(778, 22, TunnelLadderUp, 735)),
            "Echo-S lad_u at entity 22");
        Equal("Exit to Winding Tunnel", LateMidgarLabel(LateMidgarScriptExit(778, 20, new(0, 0, 0, 100, 100, 0), 737)),
            "an unknown line keeps the ordinary label");
        Equal("Exit to Winding Tunnel", LateMidgarLabel(LateMidgarScriptExit(778, 20, TunnelWayUp, 737) with { TriggerLine = null }),
            "a script exit without its line is not guessed");
        Equal("Exit to Winding Tunnel", LateMidgarLabel(LateMidgarScriptExit(736, 18, TunnelLadderUp, 737)),
            "a destination the native line does not reach is not that line");
        Equal("Exit to Sector 8, Underground", LateMidgarLabel(LateMidgarScriptExit(732, 11, new(-2693, 563, -326, -2589, 495, -326), 734)),
            "another field's matching coordinates borrow nothing");
        var pipesWay = new FieldNavigationTriggerLine(-241, 619, -854, -126, 645, -828);
        Equal("Exit to Sector 8, Underground", LateMidgarLabel(LateMidgarGateway(733, 2, 734, pipesWay)),
            "a gateway is its installed index as well as its line");
        Equal("Way down into the pipes", LateMidgarLabel(LateMidgarGateway(733, 0, 734, pipesWay, withLine: false)),
            "a gateway reported only by its midpoint");
        Equal("Exit to Sector 8, Underground",
            LateMidgarLabel(LateMidgarGateway(733, 0, 734, pipesWay, withLine: false) with { X = 1000, Y = -1000 }),
            "a gateway point off its installed line is not that gateway");
        Equal("Exit to Sector 8, Underground",
            LateMidgarLabel(LateMidgarScriptExit(733, 10, new(-796, 1948, -179, -700, 1852, -179), 735) with { StableId = "script-exit:734:10:735" }),
            "the stable id and the target agree on the field");
    }

    private static void LateMidgarLabelsHoldOnInstalledLayouts(string root)
    {
        void Check(string layout, Func<int, IReadOnlyList<FieldNavigationTarget>> scriptExits, FlevelDataSource source)
        {
            foreach (var field in LateMidgarWays.Select(way => way.Field).Distinct())
            {
                var exits = scriptExits(field).Concat(LateMidgarInstalledGateways(source, field)).ToArray();
                var expected = LateMidgarWays.Where(way => way.Field == field).ToArray();
                Equal(expected.Length, exits.Length, $"{layout} field {field}: every native way out is checked");
                foreach (var exit in exits)
                {
                    var gateway = exit.StableId.StartsWith("gateway:", StringComparison.Ordinal)
                        ? int.Parse(exit.StableId.Split(':')[2])
                        : -1;
                    var way = expected.Single(candidate => candidate.Gateway == gateway && candidate.Line == exit.TriggerLine);
                    Equal(true, (exit.DestinationFieldIds ?? []).OrderBy(id => id).SequenceEqual(way.Destinations),
                        $"{layout} {exit.StableId} leads where its native line does");
                    Equal(way.Label, LateMidgarLabel(exit), $"{layout} {exit.StableId}");
                }
            }
        }

        var stock = new FieldScriptNavigationCatalog(root);
        Check("stock", field => stock.ReadField(field).Exits, new FlevelDataSource(root));

        var echoes = (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var echo in echoes)
        {
            var memory = new LoadedScriptMemory();
            var loaded = new FieldScriptNavigationCatalog(root, Ff7GameLanguageDetector.Detect(root), new LoadedFieldScriptIdentityReader(memory));
            var source = new FlevelDataSource(echo);
            IReadOnlyList<FieldNavigationTarget> Loaded(int field)
            {
                memory.Load(source, field);
                var read = loaded.ReadField(field);
                Equal(true, read.IsUsable && read.UsesReviewedEchoLayout, $"{Path.GetFileName(echo)} field {field} is the reviewed Echo-S layout");
                return read.Exits;
            }

            Check(Path.GetFileName(echo), Loaded, source);
            Equal(true, Loaded(778).Any(exit => exit.StableId == "script-exit:778:20:737" && LateMidgarLabel(exit) == "Way up the tunnel"),
                "Echo-S's renumbered tunnel_6 line is still named for where it goes");
        }
    }

    private static string LateMidgarLabel(FieldNavigationTarget target) =>
        new FieldExitLabelResolver(
                field => LateMidgarMapNames.TryGetValue(field, out var name)
                    ? FieldMapNameResolution.Known([name])
                    : FieldMapNameResolution.Unknown,
                () => LateMidgarMapNames.GetValueOrDefault(target.FieldId, string.Empty))
            .Resolve([target])[0].Label;

    private static FieldNavigationTarget LateMidgarScriptExit(int field, int entity, FieldNavigationTriggerLine line, params int[] destinations) =>
        new(field, FieldNavigationCategory.Exits, "Scripted exit",
            (line.StartX + line.EndX) / 2, (line.StartY + line.EndY) / 2, (line.StartZ + line.EndZ) / 2,
            $"script-exit:{field}:{entity}:{string.Join(',', destinations)}",
            TriggerEntityId: entity, DestinationFieldIds: destinations, TriggerLine: line);

    private static FieldNavigationTarget LateMidgarGateway(int field, int index, int destination, FieldNavigationTriggerLine line, bool withLine = true) =>
        new(field, FieldNavigationCategory.Exits, "Exit",
            (line.StartX + line.EndX) / 2, (line.StartY + line.EndY) / 2, (line.StartZ + line.EndZ) / 2,
            $"gateway:{field}:{index}:{destination}", DestinationFieldIds: [destination], TriggerLine: withLine ? line : null);

    // The installed gateway table (section 7), as the runtime gateway readers publish it.
    private static IEnumerable<FieldNavigationTarget> LateMidgarInstalledGateways(FlevelDataSource source, int field)
    {
        Equal(true, source.TryReadField(field, out var encoded), $"field {field} is installed");
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var section = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
        for (var index = 0; index < 12; index++)
        {
            var at = section + 0x38 + index * 24;
            var destination = BitConverter.ToInt16(bytes, at + 18);
            if (destination < 0 || destination == short.MaxValue || destination == field) continue;
            yield return LateMidgarGateway(field, index, destination, new(
                BitConverter.ToInt16(bytes, at), BitConverter.ToInt16(bytes, at + 2), BitConverter.ToInt16(bytes, at + 4),
                BitConverter.ToInt16(bytes, at + 6), BitConverter.ToInt16(bytes, at + 8), BitConverter.ToInt16(bytes, at + 10)));
        }
    }

    // Section 0 of an installed field placed where the loaded-script identity reader looks for it.
    private sealed class LoadedScriptMemory : Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace
    {
        private const uint Script = 0x02000000;
        private readonly Dictionary<uint, byte> bytes = [];

        public void Load(FlevelDataSource source, int field)
        {
            Equal(true, source.TryReadField(field, out var encoded), $"field {field} is installed");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var at = BitConverter.ToInt32(decoded, 6);
            var length = BitConverter.ToInt32(decoded, at);
            bytes.Clear();
            Write(Script, decoded.AsSpan(at + 4, length));
            bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 1;
            Write((uint)FieldScriptContextReader.AddressCurrentFieldId, BitConverter.GetBytes((ushort)field));
            Write((uint)FieldScriptContextReader.AddressFieldScriptPtr, BitConverter.GetBytes(Script));
        }

        private void Write(uint address, ReadOnlySpan<byte> data)
        {
            for (var i = 0; i < data.Length; i++) bytes[address + (uint)i] = data[i];
        }

        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                if (!bytes.TryGetValue(address + (uint)i, out destination[i])) return false;
            }

            return true;
        }
    }

    private static FieldNavigationTarget Only(State state, int field, ushort triangle = 0)
    {
        var targets = state.Reader().ReadTargets(Position(field, triangle));
        Equal(1, targets.Count, $"field {field} has one current Story step");
        return targets[0];
    }

    private static int Entity(FieldNavigationTarget target) => int.Parse(target.StableId!.Split(':')[2]);

    private static FieldPositionSnapshot Position(int field, ushort triangle = 0) =>
        new(1, (ushort)field, 0, 0, 0, 0, triangle, 0);

    private sealed class State
    {
        private const int Bank15 = FieldNavigationObjectReader.AddressFieldBankBase + 0x400;
        private readonly Dictionary<int, byte> bytes = [];
        public int? DisabledLine { get; init; }

        /// <summary>734:3's first walkway run sets 15[131] bit 3 as the walkway falls.</summary>
        public bool WalkwayCollapsed
        {
            init { if (value) bytes[Bank15 + 131] = (byte)(bytes.GetValueOrDefault(Bank15 + 131) | 0x08); }
        }

        /// <summary>md8brdg2's line_rb sets 15[128] bit 3 with moment 1603 after the bridge battle.</summary>
        public bool BridgeBattleWon
        {
            init { if (value) bytes[Bank15 + 128] = (byte)(bytes.GetValueOrDefault(Bank15 + 128) | 0x08); }
        }

        public State(int moment, int section, bool settled)
        {
            var bank = FieldNavigationObjectReader.AddressFieldBankBase;
            bytes[bank] = (byte)moment;
            bytes[bank + 1] = (byte)(moment >> 8);
            bytes[bank + 0x400 + 129] = (byte)section;
            bytes[bank + 0x400 + 128] = settled ? (byte)4 : (byte)0;
        }

        public FieldStoryTargetReader Reader() => new(_ => 0,
            address => unchecked((short)(bytes.GetValueOrDefault(address) | bytes.GetValueOrDefault(address + 1) << 8)),
            address => bytes.GetValueOrDefault(address), FieldStoryEventCatalog.CreateAllFields(),
            entity => entity != DisabledLine);
    }

    private static void Contains(string? actual, string expected, string why)
    {
        if (actual?.Contains(expected, StringComparison.Ordinal) != true)
            throw new InvalidOperationException($"{why}: expected {expected}, got {actual ?? "none"}");
    }

    private static void Equal<T>(T expected, T actual, string why)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{why}: expected {expected}, got {actual}");
    }
}
