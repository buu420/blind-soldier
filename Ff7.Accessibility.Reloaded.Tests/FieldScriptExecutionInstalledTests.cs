using System.Text.Json;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The field catalog against the installed archive: the native witnesses the whole-game
/// audit found for each execution-model repair, and a whole-game check that nothing the
/// catalog published before the repair was lost.
///
/// <para>Each witness names the bytes that show the old reading failed. The audit's
/// replay attributed every one to a single shipping rule (reports/claude-field-audit.md);
/// the tests hold the repaired catalog to the native result.</para>
/// </summary>
internal static class FieldScriptExecutionInstalledTests
{
    private const string BaselineFixture = "field-catalog-baseline-20260924.json";

    /// <summary>
    /// Transitions and exits the pre-repair catalog published that the repaired one no
    /// longer does, each with the native reason (movement by a model that is never the
    /// controlled one, a stop in the middle of a double jump, a composite of two models'
    /// movements, a companion's own movement). Anything missing that is not listed there
    /// fails the whole-game check.
    /// </summary>
    private const string JustifiedRemovalsFixture = "field-catalog-justified-removals-20260924.json";

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(root);
        Tunnel2WaitsForTheValueAnotherScriptWrites(catalog);
        Tunnel3LandsOnItsJmpflTarget(catalog);
        GodosPagodaForksOnThePartyTest(catalog);
        RocketTownSideFollowsControlFlowAcrossScripts(catalog);
        HighwindDeckRunsTheOkCodeWhenWalkedOn(catalog);
        TheAirportLiftDoesNotBorrowADirectorsLines(catalog);
        GoldSaucerTubesEachLeadToTheirOwnArea(catalog);
        TifasJunonLadderIsHerOwnClimb(catalog);
        MountCorelKeepsEachCharactersOwnLanding(catalog);
        ShinraSixtiethFloorResetsByReloadingTheField(catalog);
        TheTempleGuideIsNotThePlayer(catalog);
        ShinraButtonDoorsAreExits(catalog);
        CosmoSignHandsControlBackToWhoeverLeads(catalog);
        TrainGraveyardJumpIsCloudsAfterThePartyChange(catalog);
        ChurchEscapeKeepsItsExitAfterAPartyChangeInAnotherScript(catalog);
        MountCorelCopiesNeedTheirOwnLeader(catalog);
        CosmoStairwellClimbsMoveWhoeverLeads(catalog);
        TheHighwindJumpLineIsGuardedByItsFlag(catalog);
        TheLedgersFlaggedExitsFollowTheirLiveFlags(catalog);
        NothingPublishedBeforeTheRepairIsLost(catalog);
    }

    /// <summary>
    /// Root's installed guard cases, through the production catalog and the live evaluator
    /// together, each under a value that opens the exit and one that closes it.
    ///
    /// <para>fship_4 k_jump (74:7) s2 <c>14D05B07090D</c>: MAPJUMP 744 only while 13[91] bit 7 is
    /// set. junonr1 evl0 (360:11) s3 <c>16200000E2040415</c> and <c>14107A000A0F</c>: MAPJUMP 773 only
    /// while GameMoment is at least 1250 and 1[122] bit 0 is clear, and <c>82107A00</c> sets that
    /// bit on the way, so the lift is used once. tower5's exit line (586:16) s2 opens with
    /// <c>14F08A00008B</c> 15[138] == 0, <c>14F08B050902</c> 15[139] bit 5 and <c>14F08B000A7E</c>
    /// 15[139] bit 0 clear. The masters' Mains write both bytes (<c>82F08B05</c> in goriki's,
    /// <c>80F08A00</c> in godoh's), but the line's event runs those three tests in the pass that
    /// starts it, before any Main can, so they guard it; the bit 5 test behind its MESSAGE and the
    /// IFPRTYQ for Yuffie are left out, so the guard is what the MAPJUMP needs, not all of it.
    /// gldgate's information counter (497:14) [OK] returns at game moments 439 and 598 before its
    /// MAPJUMP to gldinfo, the Info Board screen: it is an Object for every other moment and not
    /// an exit at all.</para>
    /// </summary>
    private static void TheLedgersFlaggedExitsFollowTheirLiveFlags(FieldScriptNavigationCatalog catalog)
    {
        True(HasOpcode(catalog, 360, 11, 3, "16200000E2040415") && HasOpcode(catalog, 360, 11, 3, "14107A000A0F") &&
             HasOpcode(catalog, 360, 11, 3, "82107A00"),
            "native junonr1 evl0 s3 tests GameMoment >= 1250 and 1[122] bit 0 clear, then sets the bit");
        True(HasOpcode(catalog, 586, 16, 2, "14F08A00008B") && HasOpcode(catalog, 586, 16, 2, "14F08B050902") &&
             HasOpcode(catalog, 586, 16, 2, "14F08B000A7E") &&
             HasOpcode(catalog, 586, 17, 0, "82F08B05") && HasOpcode(catalog, 586, 21, 0, "80F08A00"),
            "native tower5 exit s2 opens with its 15[138]/15[139] tests, which goriki's and godoh's Mains write");
        True(HasOpcode(catalog, 497, 14, 1, "16200000B7010002") && HasOpcode(catalog, 497, 14, 1, "1620000056020002") &&
             HasOpcode(catalog, 497, 14, 1, "60F20100000000000000"),
            "native gldgate al [OK] returns at game moments 439 and 598, then MAPJUMPs to gldinfo");

        const int moment = FieldScriptExitGuards.AddressSavemapFieldBanks;
        (int Field, string Exit, (int Offset, byte Value)[] Open, (int Offset, byte Value)[][] Closed)[] cases =
        [
            (74, "script-exit:74:7:744", [(0x300 + 91, 0x80)], [[(0x300 + 91, 0x7F)]]),
            (360, "script-exit:360:11:773", [(0, 0xE2), (1, 0x04), (122, 0x00)],
                [[(0, 0xE2), (1, 0x04), (122, 0x01)], [(0, 0xE1), (1, 0x04), (122, 0x00)]]),
            (586, "script-exit:586:16:587", [(0x400 + 138, 0x00), (0x400 + 139, 0x00)],
                [[(0x400 + 138, 0x01), (0x400 + 139, 0x00)], [(0x400 + 138, 0x00), (0x400 + 139, 0x01)],
                 [(0x400 + 138, 0x00), (0x400 + 139, 0x20)]])
        ];
        True(!catalog.ReadField(497).Exits.Any(exit => exit.StableId.StartsWith("script-exit:497:14:", StringComparison.Ordinal)),
            "gldgate's information counter is not an exit");
        var counter = TownInteractionObjectCatalog.Create().Where(row => row.FieldId == 497 && row.EntityId == 14).ToArray();
        True(counter.Length == 3 &&
             counter.All(row => row.TargetKind == FieldNavigationObjectTargetKind.Line && row.StaticX == 127 && row.StaticY == 552) &&
             new[] { 0, 438, 440, 597, 599, 1500 }.All(moment => counter.Count(row => Offered(row, moment)) == 1) &&
             new[] { 439, 598 }.All(moment => !counter.Any(row => Offered(row, moment))),
            "it is one Object, at al's midpoint, at every game moment but 439 and 598");

        foreach (var (field, exit, open, closed) in cases)
        {
            var result = catalog.ReadField(field);
            True(result.ExitGuards.ContainsKey(exit), $"{exit} carries a live guard");
            True(Offers(result, exit, open, moment), $"{exit} is offered while its flags let the MAPJUMP run");
            foreach (var shut in closed)
            {
                True(!Offers(result, exit, shut, moment),
                    $"{exit} is not offered under {string.Join(",", shut.Select(write => $"+{write.Offset}={write.Value:X2}"))}");
            }
        }
    }

    private static bool Offered(FieldNavigationObjectDefinition row, int moment) =>
        (row.MinimumGameMoment < 0 || moment >= row.MinimumGameMoment) &&
        (row.MaximumGameMoment < 0 || moment <= row.MaximumGameMoment);

    private static bool Offers(FieldScriptNavigationReadResult result, string exit, (int Offset, byte Value)[] writes, int banks)
    {
        var memory = writes.ToDictionary(write => (uint)(banks + write.Offset), write => write.Value);
        return FieldScriptExitGuards.Apply(result.Exits, result.ExitGuards, new SavemapBytes(memory))
            .Any(candidate => candidate.StableId == exit);
    }

    private sealed class SavemapBytes(Dictionary<uint, byte> bytes) : ILegacyAddressSpace
    {
        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(address + (uint)index, out destination[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// fship_4 line k_jump (entity 7) script 2: <c>14D05B07090D</c> IFUB 13[91] bit 7, then
    /// MAPJUMP 744, and nothing at all when the bit is clear. The exit carries that bit as its
    /// live guard, so it is offered only once the flag is set.
    /// </summary>
    private static void TheHighwindJumpLineIsGuardedByItsFlag(FieldScriptNavigationCatalog catalog)
    {
        True(HasOpcode(catalog, 74, 7, 2, "14D05B07090D"), "native fship_4 k_jump s2 tests 13[91] bit 7");
        var result = catalog.ReadField(74);
        True(result.ExitGuards.TryGetValue("script-exit:74:7:744", out var guards) &&
             guards.Single().Alternatives.Single().Single().Key == "13:91:b:9:7:True",
            "the exit to 744 is guarded by 13[91] bit 7");
    }

    private static string Ways(FieldScriptNavigationTransition transition) =>
        string.Join(";", transition.Conditions?.Select(condition => condition.Key) ?? []);

    /// <summary>
    /// cos_btm line 30 (KANBANB script 5): <c>BF0F</c> CC hands control to the telescope view
    /// (entity 15), which jumps, then <c>1430090000</c>/<c>BF05</c>, <c>1430090200</c>/<c>BF08</c> and
    /// <c>1430090800</c>/<c>BF0A</c> hand it back to Cloud, Tifa or Cid, whichever is in slot 0. The
    /// view's jump is the player's landing only for a leader none of those tests is for.
    /// </summary>
    private static void CosmoSignHandsControlBackToWhoeverLeads(FieldScriptNavigationCatalog catalog)
    {
        True(HasOpcode(catalog, 525, 30, 5, "BF0F"), "native cos_btm KANBANB s5 hands control to the telescope view");
        True(HasOpcode(catalog, 525, 30, 5, "1430090800"), "native cos_btm KANBANB s5 tests whether Cid leads");
        var view = catalog.ReadField(525).Transitions.Where(transition => transition.StableId == "jump:525:30:15:9:88").ToArray();
        True(view.All(transition => transition.Conditions is { Count: > 0 } ways &&
                                    ways.All(way => way.LeaderCharacterIds is { } leaders &&
                                                    !leaders.Contains(0) && !leaders.Contains(2) && !leaders.Contains(8))),
            $"the view's jump is never offered while Cloud, Tifa or Cid leads: {string.Join("|", view.Select(Ways))}");
    }

    /// <summary>
    /// tin_1 line 28 (border3 script 2): <c>CA000102</c> PRTYE puts Cloud in slot 0 and 0061BC67
    /// gives him control; then Cloud (entity 30), Barret (31) and Tifa (32) each jump to triangle 59.
    /// Only Cloud's jump is the player's, whoever was controlled when the line was crossed.
    /// </summary>
    private static void TrainGraveyardJumpIsCloudsAfterThePartyChange(FieldScriptNavigationCatalog catalog)
    {
        True(HasOpcode(catalog, 139, 28, 2, "CA000102"), "native tin_1 border3 s2 sets the party with Cloud leading");
        var jumps = catalog.ReadField(139).Transitions.Where(transition => transition.StableId.StartsWith("jump:139:28:", StringComparison.Ordinal)).ToArray();
        True(jumps.Length == 1 && jumps[0].StableId == "jump:139:28:30:31:59" && jumps[0].MoverEntityIds is null,
            $"only Cloud's jump, the player's after PRTYE: {string.Join(";", jumps.Select(jump => $"{jump.StableId}[{Movers(jump)}]"))}");
    }

    /// <summary>
    /// chrin_1a line 2: the route reaches Aeris's script 5 (entity 4) through requests that do not
    /// wait; it sets the party with <c>CA0003FE</c> and MAPJUMPs to 183. A party changed by a script
    /// the caller does not wait for says nothing about who led when the line was crossed.
    /// </summary>
    private static void ChurchEscapeKeepsItsExitAfterAPartyChangeInAnotherScript(FieldScriptNavigationCatalog catalog)
    {
        True(HasOpcode(catalog, 182, 4, 5, "CA0003FE"), "native chrin_1a earith s5 sets the party");
        True(Destinations(catalog.ReadField(182), 2).Contains(183), "chrin_1a line 2 still leads to 183");
    }

    /// <summary>
    /// mtcrl_6: the border lines ask party slot 0 for its own copy of a routine. Aeris's copy
    /// (entity 17) runs only while Aeris is in slot 0; the landing on triangle 9 that Cloud's copy
    /// (entity 16) shares with Tifa's and Cid's needs one of those three leading, never Aeris.
    /// </summary>
    private static void MountCorelCopiesNeedTheirOwnLeader(FieldScriptNavigationCatalog catalog)
    {
        var transitions = catalog.ReadField(464).Transitions;
        True(transitions.Any(transition => transition.StableId == "jump:464:13:17:6:16" && Ways(transition) == "17:3"),
            "Aeris's copy needs her model (17) controlled with Aeris (3) leading");
        var shared = transitions.SingleOrDefault(transition => transition.StableId == "jump:464:14:16:7:9");
        True(shared.Conditions is { Count: > 0 } ways &&
             ways.Any(way => way.Key == "16:0") &&
             ways.All(way => way.ControlledEntityId is { } entity &&
                             way.LeaderCharacterIds is { Count: > 0 } leaders &&
                             !leaders.Contains(3) &&
                             leaders.All(character => catalog.ReadAllScriptOpcodes(464)
                                 .Where(script => script.EntityId == entity)
                                 .SelectMany(script => script.Opcodes)
                                 .Any(opcode => opcode.Opcode == 0xA0 && opcode.Bytes[1] == character))),
            $"the landing Cloud's copy shares pairs each sharer with its own character, Cloud with Cloud and never Aeris: {Ways(shared)}");
    }

    /// <summary>
    /// cosin5: the stairwell's polls read party slot 0's position (PXYZI) and ask slot 0 to climb
    /// (PRQEW), so each climb moves whichever family member leads, and needs one of them leading.
    /// </summary>
    private static void CosmoStairwellClimbsMoveWhoeverLeads(FieldScriptNavigationCatalog catalog)
    {
        var polled = catalog.ReadField(534).Transitions
            .Where(transition => transition.StableId.StartsWith("polled-ladder:534:", StringComparison.Ordinal))
            .ToArray();
        True(polled.Length >= 28, $"the stairwell keeps its polled climbs: {polled.Length}");
        True(polled.All(transition => transition.MoverEntityIds is { Count: > 0 } movers &&
                                      transition.Conditions is { } ways &&
                                      ways.Count == movers.Count &&
                                      ways.Any(way => way.LeaderCharacterIds?.SequenceEqual([0]) == true)),
            "each climb moves the family member who leads, each paired with its own character, Cloud among them");
    }

    private static int[] Destinations(FieldScriptNavigationReadResult result, int entity) =>
        result.Exits
            .Where(exit => exit.TriggerEntityId == entity)
            .SelectMany(exit => exit.DestinationFieldIds ?? [])
            .Distinct()
            .Order()
            .ToArray();

    private static bool HasOpcode(FieldScriptNavigationCatalog catalog, int field, int entity, int script, string hex) =>
        catalog.ReadScriptOpcodes(field, entity, script)
            .Any(opcode => Convert.ToHexString(opcode.Bytes.ToArray()).StartsWith(hex, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// tunnel_2 line2 (entity 5): its [Move] waits in e8.s4, which sets <c>80500700</c>
    /// 5[7] = 0 and loops on <c>1450070100</c> IFUB 5[7] == 1 while the scripts it requested
    /// asynchronously set 5[7]. Only then does it MAPJUMP to sbwy4_1 (164).
    /// </summary>
    private static void Tunnel2WaitsForTheValueAnotherScriptWrites(FieldScriptNavigationCatalog catalog)
    {
        True(HasOpcode(catalog, 162, 8, 4, "80500700"), "native tunnel_2 e8.s4 sets 5[7] to 0");
        True(HasOpcode(catalog, 162, 8, 4, "1450070100"), "native tunnel_2 e8.s4 waits for 5[7] == 1");
        True(Destinations(catalog.ReadField(162), 5).Contains(164), "tunnel_2 line2 leads to sbwy4_1");
    }

    /// <summary>tunnel_3 line2 (entity 3): <c>110B00</c> JMPFL lands 12 bytes on, on the way to 161.</summary>
    private static void Tunnel3LandsOnItsJmpflTarget(FieldScriptNavigationCatalog catalog)
    {
        True(catalog.ReadAllScriptOpcodes(163).Any(script => script.Opcodes.Any(opcode =>
                Convert.ToHexString(opcode.Bytes.ToArray()) == "110B00")),
            "native tunnel_3 has the JMPFL that the byte-2 reading overshoots");
        True(Destinations(catalog.ReadField(163), 3).Contains(161), "tunnel_3 line2 leads to tunnel_1");
    }

    /// <summary>tower5 exit (entity 16): <c>CB0514</c> IFPRTYQ 5; the jump to 587 is on the side where the character is absent.</summary>
    private static void GodosPagodaForksOnThePartyTest(FieldScriptNavigationCatalog catalog)
    {
        True(HasOpcode(catalog, 586, 16, 2, "CB0514"), "native tower5 exit tests whether character 5 is in the party");
        True(Destinations(catalog.ReadField(586), 16).Contains(587), "tower5's exit line leads back to 587");
    }

    /// <summary>rktsid line1 (entity 6): the jump to 568 is reached only by leaving the requested script's range.</summary>
    private static void RocketTownSideFollowsControlFlowAcrossScripts(FieldScriptNavigationCatalog catalog)
    {
        True(Destinations(catalog.ReadField(558), 6).Contains(568), "rktsid line1 leads to rcktin6");
    }

    /// <summary>fship_23 jump (entity 6): [Move] (slot 2) repeats [OK]'s pointer, and that code MAPJUMPs to fship_4.</summary>
    private static void HighwindDeckRunsTheOkCodeWhenWalkedOn(FieldScriptNavigationCatalog catalog)
    {
        True(Destinations(catalog.ReadField(70), 6).Contains(74), "fship_23's jump line leads to fship_4");
    }

    /// <summary>
    /// junair box0 (entity 14): its Talk runs the director's script 3, which asks a party
    /// slot for lines 46, 49 and 50. Reading the party slot as entity 2 attributed 33-41,
    /// another script's lines, to the lift controls.
    /// </summary>
    private static void TheAirportLiftDoesNotBorrowADirectorsLines(FieldScriptNavigationCatalog catalog)
    {
        var lift = catalog.ReadField(384).Npcs.SingleOrDefault(npc => npc.EntityId == 14);
        True(lift.DialogIds is null || lift.DialogIds.All(id => id is < 33 or > 41),
            "junair's lift controls are not given lines 33-41");
    }

    private static string Movers(FieldScriptNavigationTransition transition) =>
        string.Join(",", transition.MoverEntityIds ?? []);

    /// <summary>
    /// ghotel's seven tube lines (entities 4-10) each lock control, set 5[2] to their own tube
    /// and ask the leader to travel. The other lines' Go scripts write 5[2] too, but only when
    /// the player sets them off; each tube still goes to its own area, not all seven.
    /// </summary>
    private static void GoldSaucerTubesEachLeadToTheirOwnArea(FieldScriptNavigationCatalog catalog)
    {
        var expected = new[]
        {
            "script-exit:491:10:505", "script-exit:491:4:497", "script-exit:491:5:509", "script-exit:491:6:499",
            "script-exit:491:7:488", "script-exit:491:8:486", "script-exit:491:9:484"
        };
        var exits = catalog.ReadField(491).Exits.Select(exit => exit.StableId).Order(StringComparer.Ordinal).ToArray();
        True(exits.SequenceEqual(expected), $"each Ghost Hotel tube leads to its own area: {string.Join(", ", exits)}");
    }

    /// <summary>
    /// junone5 line 8: Tifa (entity 2) climbs to triangle 21, then the soldiers hei0 and hei2
    /// (entities 5 and 7) climb after her. Their ladders move them; hers is the only climb
    /// that is the player's, and it ends where her own ladder does.
    /// </summary>
    private static void TifasJunonLadderIsHerOwnClimb(FieldScriptNavigationCatalog catalog)
    {
        var transitions = catalog.ReadField(414).Transitions;
        True(transitions.Any(transition => transition.StableId == "ladder:414:8:2:3:21" && Movers(transition) == "2"),
            "Tifa's own ladder from line 8 lands on triangle 21");
        True(transitions.All(transition => transition.MoverEntityIds is { } movers && !movers.Contains(5) && !movers.Contains(7)),
            "the soldiers climbing after Tifa are not the player");
    }

    /// <summary>
    /// mtcrl_6: the border lines ask the leader for a routine each character keeps a copy
    /// of. Cloud's (entity 16) landings stay his, and Aeris's own copy (entity 17) is hers.
    /// </summary>
    private static void MountCorelKeepsEachCharactersOwnLanding(FieldScriptNavigationCatalog catalog)
    {
        var transitions = catalog.ReadField(464).Transitions;
        foreach (var id in new[] { "jump:464:14:16:7:9", "jump:464:15:16:8:214", "ladder:464:6:16:3:322" })
        {
            True(transitions.Any(transition => transition.StableId == id && transition.MoverEntityIds?.Contains(16) == true),
                $"Cloud's own Mount Corel landing {id}");
        }

        True(transitions.Any(transition => transition.StableId == "jump:464:13:17:6:16" && Movers(transition) == "17"),
            "Aeris's own copy keeps her landing on triangle 16, marked as hers alone");
        True(transitions.All(transition => transition.MoverEntityIds is not { } movers || !(movers.Contains(16) && movers.Contains(17))),
            "Cloud's and Aeris's copies disagree, so no traversal is both of theirs");
    }

    /// <summary>
    /// blin60_1: after the guards' battle AD2's script 3 MAPJUMPs to the floor itself (offset
    /// 20, <c>60 EF00 D5FB 1D00 4C00 E0</c>) and has more placements written after it. MAPJUMP's
    /// request 1 makes the field loop (0063C17F) load field 239 again with the previous module
    /// set to 1, so 0063BDA8 resets everything and field setup clears the request: nothing after
    /// the MAPJUMP runs, and Tifa's placement (entity 9 script 12) that it requests is not a way
    /// the party moves. The MAPJUMP is where the party goes, and it is into the same field.
    /// </summary>
    private static void ShinraSixtiethFloorResetsByReloadingTheField(FieldScriptNavigationCatalog catalog)
    {
        True(HasOpcode(catalog, 239, 29, 3, "60EF00D5FB1D004C00E0"), "native blin60_1 AD2 s3 MAPJUMPs to its own field");
        True(catalog.ReadField(239).Transitions.All(transition => transition.StableId != "jump:239:19:9:12:177"),
            "the placement written after the same-field MAPJUMP never runs");
    }

    /// <summary>
    /// The Shinra building's button doors: blin60_2's elevator lines (entities 12 and 13)
    /// MAPJUMP to eleout from their [OK] handler, and blin63_1's air ducts (46 and 47) climb
    /// into the duct crawl after their prompt. Neither is reachable any other way from there.
    /// </summary>
    private static void ShinraButtonDoorsAreExits(FieldScriptNavigationCatalog catalog)
    {
        True(HasOpcode(catalog, 240, 12, 1, "60E900"), "native blin60_2 ELINEL [OK] MAPJUMPs to eleout");
        True(HasOpcode(catalog, 245, 46, 1, "60F600"), "native blin63_1 DUCTLB [OK] MAPJUMPs into the duct");
        foreach (var (field, entity, destination) in new[] { (240, 12, 233), (240, 13, 233), (245, 46, 246), (245, 47, 246) })
        {
            True(Destinations(catalog.ReadField(field), entity).Contains(destination),
                $"field {field} line {entity} is an exit to {destination}");
        }
    }

    /// <summary>kuro_7's keyman (entity 27) runs the Ancient's route around the room; the player never moves as it.</summary>
    private static void TheTempleGuideIsNotThePlayer(FieldScriptNavigationCatalog catalog)
    {
        True(catalog.ReadField(610).Transitions.All(transition => transition.MoverEntityIds?.Contains(27) != true),
            "the Temple guide's jumps are not the player's");
    }

    /// <summary>
    /// Every transition and script exit the pre-repair catalog published is still
    /// published, unless <see cref="JustifiedRemovals"/> gives the native reason.
    /// </summary>
    private static void NothingPublishedBeforeTheRepairIsLost(FieldScriptNavigationCatalog catalog)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", BaselineFixture);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var justified = ReadJustifiedRemovals();
        var lost = new List<string>();
        var stillPublished = new List<string>();
        foreach (var field in document.RootElement.GetProperty("fields").EnumerateObject())
        {
            var fieldId = int.Parse(field.Name);
            var result = catalog.ReadField(fieldId);
            var transitions = result.Transitions.Select(transition => transition.StableId).ToHashSet(StringComparer.Ordinal);
            var exits = result.Exits.Select(exit => exit.StableId).ToHashSet(StringComparer.Ordinal);
            foreach (var id in field.Value.GetProperty("t").EnumerateArray().Select(item => item.GetString()!))
            {
                if (!transitions.Contains(id) && !justified.ContainsKey(id))
                {
                    lost.Add(id);
                }
                else if (transitions.Contains(id) && justified.ContainsKey(id))
                {
                    stillPublished.Add(id);
                }
            }

            foreach (var id in field.Value.GetProperty("e").EnumerateArray().Select(item => item.GetString()!))
            {
                if (!exits.Contains(id) && !justified.ContainsKey(id) &&
                    !ExitStillPublishedWithMoreDestinations(id, exits))
                {
                    lost.Add(id);
                }
            }
        }

        True(lost.Count == 0, $"published before the repair and now missing: {string.Join(", ", lost.Take(40))}{(lost.Count > 40 ? $" (+{lost.Count - 40})" : string.Empty)}");
        // A removal that is justified but did not happen means the list no longer describes
        // the catalog, and would hide a real loss of the same id later.
        True(stillPublished.Count == 0, $"listed as justified removals but still published: {string.Join(", ", stillPublished.Take(40))}");
    }

    private static IReadOnlyDictionary<string, string> ReadJustifiedRemovals()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", JustifiedRemovalsFixture);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var removals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var removal in document.RootElement.GetProperty("removals").EnumerateObject())
        {
            var reason = removal.Value.GetString();
            True(!string.IsNullOrWhiteSpace(reason), $"justified removal {removal.Name} gives its reason");
            removals[removal.Name] = reason!;
        }

        return removals;
    }

    /// <summary>
    /// A script exit's id lists its destinations, so a line that gained one keeps its old
    /// destinations under a longer id. That is the same exit, not a loss.
    /// </summary>
    private static bool ExitStillPublishedWithMoreDestinations(string id, IReadOnlySet<string> exits)
    {
        var parts = id.Split(':');
        if (parts.Length != 4 || parts[0] != "script-exit")
        {
            return false;
        }

        var destinations = parts[3].Split(',').ToHashSet(StringComparer.Ordinal);
        var prefix = $"script-exit:{parts[1]}:{parts[2]}:";
        return exits.Any(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal) &&
                                      destinations.IsSubsetOf(candidate[prefix.Length..].Split(',')));
    }

    private static void True(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException(what);
        }
    }
}
