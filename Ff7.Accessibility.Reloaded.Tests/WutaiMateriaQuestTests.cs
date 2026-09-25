using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Wutai's materia-recovery quest, from the report: the party arrives, the Item Store is
/// the first place tried, and the chest in it is offered as "MP Absorb Materia" with
/// "direction unavailable" while Story says nothing at all.
///
/// <para>The quest has no GameMoment of its own. It is ordered by five bytes the field
/// scripts write as it goes, and these cases replay each of them against the shipped
/// catalogs:</para>
///
/// <list type="bullet">
/// <item><c>Bank[3][207]</c> bit 5 is the theft on the way in (yougan, yougan3) and bit 6
/// is the end of the quest (yufy1/AD Script 4).</item>
/// <item><c>Bank[3][189]</c>: bit 5 the arrival scene, bit 1 the Turtle's Paradise scene
/// (utapb/AD Script 3, requested by Reno's or Rude's Talk), bit 2 Yuffie caught in the pot
/// (uutai1/AD Script 5), bit 3 the second bar scene, bit 0 Yuffie's escape from the cage
/// (yufy2/YUFI Script 9), bit 4 the Materia returned.</item>
/// <item><c>Bank[3][190]</c>: bit 0 the Item Store theft (uta_im/TAKARA Talk), bit 1 the
/// folding screen (utmin2/YUFI Script 3), bit 2 "Follow me" (yufy1/YUFI Script 4), bit 3
/// her walk in yufy2, bit 4 the cage down, bit 7 the party freed.</item>
/// <item><c>Bank[3][191]</c>: bit 0 Corneo in the Hidden Room, bit 1 the fight in
/// uttmpin3, bit 2 Reno's Talk outside the Pagoda.</item>
/// <item><c>Bank[13][80]</c>: bit 5 Reno's Talk, bit 6 the scene at the top of
/// Da-chao.</item>
/// </list>
/// </summary>
internal static class WutaiMateriaQuestTests
{
    private const int Shop = 576;
    private const int OldMansHouse = 578;
    private const int Town = 579;
    private const int Bar = 580;
    private const int YuffiesHouse = 581;
    private const int BackRoom = 582;
    private const int GodosHouse = 588;
    private const int GodosInnerHouse = 589;
    private const int PrayerRoom = 590;
    private const int HiddenRoom = 591;
    private const int Pagoda = 587;
    private const int DaChaoFoot = 592;

    /// <summary>The walkmesh pocket beside uttmpin3's gateway to the Hidden Room.</summary>
    private static readonly int[] PrayerRoomHiddenRoomPocket = [21, 23, 24, 25, 32, 33];

    private static readonly int[] WutaiFields = Enumerable.Range(575, 25).ToArray();

    /// <summary>tower5, the Pagoda's five floors, and the optional steps story-regions/WutaiPagoda.ps1 gives it.</summary>
    private const int GodosPagoda = 586;
    private static readonly HashSet<string> PagodaChallengeLabels =
    [
        "Talk to Gorky (optional)", "Talk to Shake (optional)", "Talk to Chekhov (optional)",
        "Talk to Staniv (optional)", "Talk to Godo (optional)",
        "Climb the stairs to the second floor (optional)", "Climb the stairs to the third floor (optional)",
        "Climb the stairs to the fourth floor (optional)", "Climb the stairs to the fifth floor (optional)",
        "Go back down to the first floor (optional)", "Go back down to the second floor (optional)",
        "Go back down to the third floor (optional)", "Go back down to the fourth floor (optional)",
        "Leave the Pagoda (optional)"
    ];

    public static void Run()
    {
        TheChestWaitsForTheBarScene();
        TheChestIsGoneOnceItsOwnBitIsSet();
        TheBarSceneIsTheFirstObjective();
        TheChestIsTheObjectiveOnceTheTurksHaveBeenMet();
        TheFoldingScreenFollowsTheTheft();
        TheShakingPotIsTalkedToThoughNothingIsDrawn();
        AnOrdinaryTalkRowStillNeedsAVisibleModel();
        YuffiesHouseLeadsToTheLevers();
        TheCageIsRaisedByTheSameLevers();
        TheBellOpensTheWayToTheHiddenRoom();
        CorneoIsFollowedAndThePartyComesBack();
        RenoSendsThePartyToDaChao();
        EverySideRoomHasAWayBackDuringTheQuest();
        NothingIsOfferedOutsideTheQuest();
        TheHangingScrollCanBeTurnedBackFromInside();
    }

    /// <summary>
    /// The reported frame. <c>uta_im</c>/TAKARA's Talk begins <c>IFUB Bank[3][189] &amp; 2</c>
    /// and returns when it is clear, and OYAJI2's Init stands her at (146,158) - between
    /// the counter and the chest - on exactly that branch. The chest is not an object the
    /// player can use yet, and it never held anything the player keeps.
    /// </summary>
    private static void TheChestWaitsForTheBarScene()
    {
        var memory = Phase(QuestPhase.Arrived, Shop);
        memory.Actor(7, 198, 163, 0, talkRadius: 55);

        Equal(false, memory.Objects().ReadTargets(memory.At(Shop)).Any(t => t.TriggerEntityId == 7),
            "the Item Store chest cannot be offered while its own Talk returns at once");

        memory.Set(3, 189, 0x02);
        var chest = memory.Objects().ReadTargets(memory.At(Shop)).Where(t => t.TriggerEntityId == 7).ToArray();
        Equal(1, chest.Length, "the Turtle's Paradise scene is what opens the chest");
        Equal("Treasure chest", chest[0].Label, "the chest is named for what is seen, not for Materia Yuffie steals");
        Equal(FieldObjectCueKind.Chest, chest[0].ObjectCueKind, "and it sounds like the chest it is");
        Equal(30 + 55, chest[0].InteractionRadius, "reached at the player's width plus TAKARA's own TALKR 55");
    }

    /// <summary>
    /// TAKARA's Talk writes <c>Bank[1][59]</c> bit 1 and only then plays the theft, which is
    /// the chest's own record of being opened. Its Init shows it open from then on.
    /// </summary>
    private static void TheChestIsGoneOnceItsOwnBitIsSet()
    {
        var memory = Phase(QuestPhase.PubSeen, Shop);
        memory.Actor(7, 198, 163, 0, talkRadius: 55);
        memory.Set(1, 59, 0x02);

        Equal(false, memory.Objects().ReadTargets(memory.At(Shop)).Any(t => t.TriggerEntityId == 7),
            "an opened chest is not offered again");
        Equal(false,
            FieldNavigationObjectCatalog.CreateAllFields().Any(d => d.Label == "MP Absorb Materia"),
            "no object may promise Materia that is stolen the moment it is received");
    }

    /// <summary>
    /// Reno's and Rude's Talk each request utapb/AD Script 3 while <c>Bank[3][189]</c> bit 1 is
    /// clear. Elena's does not. Until that scene, the town and every room off it point there.
    /// </summary>
    private static void TheBarSceneIsTheFirstObjective()
    {
        var memory = Phase(QuestPhase.Arrived, Town);
        Labels(memory, Town, ["Go into Turtle's Paradise"], "the town points at the bar first");
        Labels(memory, Shop, ["Leave the Item Store"], "the Item Store is not the first step", memory.Story(8));

        memory = Phase(QuestPhase.Arrived, Bar);
        memory.Actor(15, -7, -55, 30);
        memory.Actor(14, 89, -231, 30);
        memory.Actor(13, -13, -140, 30);
        var targets = memory.Story(9).ReadTargets(memory.At(Bar));
        Equal(2, targets.Count, "the bar offers the two Turks whose Talk starts the scene");
        Equal(true, targets.Any(t => t.Label == "Talk to Reno" && t.TriggerEntityId == 15), "Reno is entity 15");
        Equal(true, targets.Any(t => t.Label == "Talk to Rude" && t.TriggerEntityId == 14), "Rude is entity 14");
        Equal(false, targets.Any(t => t.TriggerEntityId == 13), "Elena's Talk does nothing before the scene");
        Equal(true, targets.All(t => t.Activation == FieldNavigationActivation.Talk), "both are Talk targets");

        memory.Set(3, 189, 0x02);
        Labels(memory, Bar, ["Leave Turtle's Paradise"], "once the scene has played the bar has nothing more",
            memory.Story(9));
    }

    /// <summary>After the bar, the chest is the step, from the town and in the shop.</summary>
    private static void TheChestIsTheObjectiveOnceTheTurksHaveBeenMet()
    {
        var memory = Phase(QuestPhase.PubSeen, Town);
        Labels(memory, Town, ["Go into the Item Store"], "the town points at the Item Store next");

        memory = Phase(QuestPhase.PubSeen, Shop);
        memory.Actor(7, 198, 163, 0, talkRadius: 55);
        var targets = memory.Story(8).ReadTargets(memory.At(Shop));
        Equal(1, targets.Count, "the shop offers only the chest while it is the step");
        Equal("Open the treasure chest", targets[0].Label, "the chest is the actionable step");
        Equal(7, targets[0].TriggerEntityId, "bound to TAKARA itself");

        memory.Set(1, 59, 0x02);
        memory.Set(3, 190, 0x01);
        Labels(memory, Shop, ["Leave the Item Store"], "after the theft the shop sends the player on",
            memory.Story(8));
    }

    /// <summary>
    /// The theft writes <c>Bank[3][190]</c> bit 0; utmin2's BYOBU and BYOBUB stay enabled
    /// only while bit 1 is still clear, and either Go with Confirm runs YUFI Script 3.
    /// </summary>
    private static void TheFoldingScreenFollowsTheTheft()
    {
        var memory = Phase(QuestPhase.ChestStolen, Town);
        Labels(memory, Town, ["Go into the Old Man's House"], "the town points at the Old Man's House");

        memory = Phase(QuestPhase.ChestStolen, OldMansHouse);
        var screens = memory.Story(16, 17, 18).ReadTargets(memory.At(OldMansHouse));
        Equal(2, screens.Count, "both sides of the folding screen can be checked");
        Equal(true, screens.All(t => !t.CompletesOnArrival && t.TriggerLine is not null),
            "the screen is a Confirm on its LINE, not a crossing");
        Equal(true, screens.All(t => t.InteractionRadius == 29),
            "and it is reached inside the player's own collision radius");
        Equal(0, memory.Story(18).ReadTargets(memory.At(OldMansHouse))
                .Count(t => t.Label.StartsWith("Check behind", StringComparison.Ordinal)),
            "a screen whose LINE is off is not offered");

        memory.Set(3, 190, 0x02);
        Labels(memory, OldMansHouse, ["Leave the Old Man's House"], "after the screen the house sends the player out",
            memory.Story(18));
    }

    /// <summary>
    /// uutai1/AD2 requests YUFI Script 4 while <c>Bank[3][190]</c> bit 1 is set and
    /// <c>Bank[3][189]</c> bit 2 is clear: <c>XYZI (-1175,-776)</c> on triangle 214,
    /// <c>TLKON</c> on and <c>TALKR 180</c>, and no <c>VISI</c>. Her Talk, Script 1, is the
    /// only caller of the capture, and it is its own <c>VISI 1</c> that first shows her. So
    /// the native talk test does not look at visibility, and neither may this row - but it
    /// must still honour <c>TLKON</c>.
    /// </summary>
    private static void TheShakingPotIsTalkedToThoughNothingIsDrawn()
    {
        var memory = Phase(QuestPhase.ScreenChecked, Town);
        memory.Actor(16, -1175, -776, 0, visible: false, talkRadius: 180);

        var targets = memory.Story().ReadTargets(memory.At(Town));
        Equal(1, targets.Count, "the pot is the one step in town");
        Equal("Check the shaking pot", targets[0].Label, "named for what is seen: the pot shaking");
        Equal(16, targets[0].TriggerEntityId, "bound to the entity whose Talk runs the capture");
        Equal(-1175, targets[0].X, "at YUFI's live position");
        Equal(-776, targets[0].Y, "at YUFI's live position");
        Equal(30 + 180, targets[0].InteractionRadius, "the player's width plus TALKR 180");
        Equal(FieldNavigationActivation.Talk, targets[0].Activation, "a Talk, not an arrival");

        memory.Actor(16, -1175, -776, 0, visible: false, talkRadius: 180, talkEnabled: false);
        Equal(0, memory.Story().ReadTargets(memory.At(Town)).Count,
            "with TLKON off there is nothing to talk to");

        memory = Phase(QuestPhase.PotChecked, Town);
        Labels(memory, Town, ["Go back into Yuffie's House"], "the capture ends the pot for good");
    }

    /// <summary>The hidden-talk switch belongs to one row; every other Talk row is unchanged.</summary>
    private static void AnOrdinaryTalkRowStillNeedsAVisibleModel()
    {
        var memory = Phase(QuestPhase.Arrived, Bar);
        memory.Actor(15, -7, -55, 30, visible: false);
        memory.Actor(14, 89, -231, 30, visible: false);
        Equal(0, memory.Story(9).ReadTargets(memory.At(Bar)).Count(t => t.Label.StartsWith("Talk to", StringComparison.Ordinal)),
            "hidden Turks are not offered");
        Equal(true,
            FieldStoryEventCatalog.CreateAllFields().Where(d => d.UsesHiddenTalkTarget)
                .All(d => d.FieldId == Town && d.EntityId == 16 && d.Kind == FieldStoryTargetKind.Model),
            "only the pot talks through an undrawn model");
    }

    /// <summary>
    /// yufy1's Director plays "Follow me" while <c>Bank[3][190]</c> bit 2 is clear, and its AD
    /// holds triangle 1 - the way back to town - until the trap has sprung. yufy2's SWITCH is
    /// switched off by its own Init until Yuffie's Talk turns it on and sets 5[0].
    /// </summary>
    private static void YuffiesHouseLeadsToTheLevers()
    {
        var memory = Phase(QuestPhase.PotChecked, YuffiesHouse);
        Labels(memory, YuffiesHouse, [], "the Director's own scene plays first", memory.Story(12));

        memory = Phase(QuestPhase.FollowSeen, YuffiesHouse);
        Labels(memory, YuffiesHouse, ["Go into the back room"], "Yuffie leads into the back room",
            memory.Story(12));

        memory = Phase(QuestPhase.FollowSeen, BackRoom);
        memory.Actor(7, 68, -243, -25);
        var talk = memory.Story().ReadTargets(memory.At(BackRoom));
        Equal(1, talk.Count, "Yuffie is the step before the levers");
        Equal("Talk to Yuffie", talk[0].Label, "her Talk requests AD Script 3 while 5[0] is 0");
        Equal(7, talk[0].TriggerEntityId, "yufy2 YUFI is entity 7");

        memory.Set(5, 0, 0x01);
        var levers = memory.Story(11).ReadTargets(memory.At(BackRoom));
        Equal(1, levers.Count, "after her Talk the levers are the step");
        Equal("Use the levers", levers[0].Label, "the ASK offers both levers; neither is named here");
        Equal(false, levers[0].CompletesOnArrival, "the SWITCH is a Confirm");
        Equal(0, memory.Story().ReadTargets(memory.At(BackRoom)).Count,
            "a disabled SWITCH is not offered");
    }

    /// <summary>
    /// Either answer sets <c>Bank[3][190]</c> bit 4 and drops the cage; the same SWITCH Talk
    /// with bit 4 set raises it, clears bits 4 to 6 and sets bit 7.
    /// </summary>
    private static void TheCageIsRaisedByTheSameLevers()
    {
        var memory = Phase(QuestPhase.TrapSprung, BackRoom);
        Labels(memory, BackRoom, ["Use the levers again to raise the cage"], "the cage is down",
            memory.Story(11));

        memory = Phase(QuestPhase.CageRaised, BackRoom);
        Labels(memory, BackRoom, ["Go back to the front room"], "the party is free", memory.Story(11));
        Labels(memory, YuffiesHouse, ["Leave Yuffie's House"], "the front door is no longer held",
            memory.Story(12));
        Labels(memory, Town, ["Go to the Pagoda"], "the town points at the Pagoda");
    }

    /// <summary>
    /// uutai2/AD holds triangle 128 after the trap unless 5[3] is 1; KANE sets 5[3] to 1 and
    /// releases 128 when rung from triangle 138, 139, 140 or 86 with Confirm.
    /// </summary>
    private static void TheBellOpensTheWayToTheHiddenRoom()
    {
        var memory = Phase(QuestPhase.CageRaised, Pagoda);
        var bell = memory.Story(17).ReadTargets(memory.At(Pagoda));
        Equal(1, bell.Count, "the bell is the step");
        Equal("Ring the bell", bell[0].Label, "KANE");
        Equal(true, bell[0].CompletionTriangles is { Count: 4 } triangles &&
                    triangles.OrderBy(t => t).SequenceEqual([86, 138, 139, 140]),
            "rung only from the four triangles KANE polls");
        Equal(false, bell[0].CompletesOnArrival, "standing there is not ringing it");

        memory.Set(5, 3, 0x01);
        Labels(memory, Pagoda, ["Go through the open door"], "a rung bell opens the door", memory.Story(17));

        memory = Phase(QuestPhase.Arrived, Pagoda);
        Labels(memory, Pagoda, ["Go back into town"], "before the trap the bell is not the step",
            memory.Story(17));

        foreach (var (phase, offered) in new[] { (QuestPhase.NotStarted, false), (QuestPhase.FollowSeen, false),
                     (QuestPhase.TrapSprung, true), (QuestPhase.Complete, true) })
        {
            var objects = Phase(phase, Pagoda);
            Equal(offered, objects.Objects().ReadTargets(objects.At(Pagoda)).Any(t => t.Label == "Bell"),
                $"{phase}: the optional bell is an object exactly while triangle 114 is not held");
        }
    }

    /// <summary>
    /// uttmpin4/AD sets <c>Bank[3][191]</c> bit 0 as Corneo runs toward gateway 0; uttmpin3's
    /// AD fights on arrival and sets bit 1. Arriving from uttmpin4 its Director holds 18, 19
    /// and 22; arriving from uttmpin2 it holds 21, which seals the pocket by the Hidden Room.
    /// </summary>
    private static void CorneoIsFollowedAndThePartyComesBack()
    {
        var memory = Phase(QuestPhase.CorneoSeen, HiddenRoom);
        Labels(memory, HiddenRoom, ["Go after Corneo"], "Corneo runs toward the prayer room");

        memory = Phase(QuestPhase.BattleWon, PrayerRoom);
        var pocketRow = FieldStoryEventCatalog.CreateAllFields()
            .Single(d => d.FieldId == PrayerRoom && d.Label == "Go back to the Hidden Room");
        Equal(true, pocketRow.RequiredPlayerTriangles?.OrderBy(t => t).SequenceEqual(PrayerRoomHiddenRoomPocket) == true,
            "the Hidden Room row is confined to the pocket triangle 21 seals");
        Labels(memory, PrayerRoom, ["Go back to the Hidden Room"], "from the pocket the Hidden Room is closest",
            position: memory.At(PrayerRoom, -136, 326, 0, 21));
        Labels(memory, PrayerRoom, ["Go back out through the house"], "outside the pocket the house is the way out",
            position: memory.At(PrayerRoom, 16, -313, 0, 3));
        Labels(memory, HiddenRoom, ["Go back out to the Pagoda"], "the Hidden Room leads back out");
        Labels(memory, GodosInnerHouse, ["Go back toward the Pagoda"], "the long way round",
            position: memory.At(GodosInnerHouse, -5299, -355, 1123, 110));
        Labels(memory, GodosHouse, ["Go back out to the Pagoda"], "and out of the front house",
            position: memory.At(GodosHouse, 97, -755, 0, 0));
        Labels(memory, GodosHouse, [], "the room behind the scroll has no Story way out",
            position: memory.At(GodosHouse, -810, -88, 0, 94));
        Labels(memory, Town, ["Go to the Pagoda"], "Reno is at the Pagoda");
    }

    /// <summary>uutai2/RENO shows while bit 1 is set and bit 2 is clear; his Talk sets both.</summary>
    private static void RenoSendsThePartyToDaChao()
    {
        var memory = Phase(QuestPhase.BattleWon, Pagoda);
        memory.Actor(12, 553, -5260, 0);
        var reno = memory.Story(17).ReadTargets(memory.At(Pagoda));
        Equal(1, reno.Count, "Reno is the step");
        Equal("Talk to Reno", reno[0].Label, "entity 12");
        Equal(12, reno[0].TriggerEntityId, "uutai2 RENO");

        memory = Phase(QuestPhase.RenoSpoken, Pagoda);
        Labels(memory, Pagoda, ["Go back into town"], "after Reno the Pagoda sends the player back", memory.Story(17));
        Labels(memory, Town, ["Go to the Da-chao Statue"], "Reno's clue is Da-chao");
        Labels(memory, DaChaoFoot, ["Climb Da-chao"], "the climb", memory.Story(5));
        Labels(memory, 593, ["Keep climbing Da-chao"], "the climb");
        Labels(memory, 596, ["Keep climbing Da-chao"], "the climb");
        Labels(memory, 594, ["Go back the way you came"], "a side ledge returns to the path");
    }

    /// <summary>
    /// A room the current step does not need still has one door back, so Story is never
    /// silent while the quest runs. The step's own row, where there is one, is the only row.
    /// </summary>
    private static void EverySideRoomHasAWayBackDuringTheQuest()
    {
        var memory = Phase(QuestPhase.Arrived, Town);
        Labels(memory, 575, ["Leave the shop"], "Item/Accessory Store", memory.Story(7));
        Labels(memory, 577, ["Leave the Cat's House"], "Cat's House", memory.Story(5));
        Labels(memory, 585, ["Go back to the Cat's House"], "the passage under the Cat's House");
        Labels(memory, Pagoda, ["Go back into town"], "the Pagoda before the trap", memory.Story(17));
        Labels(memory, DaChaoFoot, ["Go back down to town"], "Da-chao before Reno's clue", memory.Story(5));
        Labels(memory, 593, ["Go back down"], "Da-chao before Reno's clue");
        Labels(memory, 597, ["Go back down"], "Da-chao before Reno's clue");
        Labels(memory, 599, ["Go back the way you came"], "Da-chao before Reno's clue");
        Labels(memory, YuffiesHouse, [], "the house is shut before the pot", memory.Story(12));
    }

    /// <summary>
    /// Not started, or finished: no Wutai field offers a quest step. Godo's Pagoda (586) is
    /// the one room with a quest of its own - Yuffie's five fights, which are fought after
    /// this one - so it may offer those optional steps and nothing else
    /// (FieldPuzzleStoryTests covers them).
    /// </summary>
    private static void NothingIsOfferedOutsideTheQuest()
    {
        foreach (var phase in new[] { QuestPhase.NotStarted, QuestPhase.Complete })
        {
            foreach (var field in WutaiFields)
            {
                var memory = Phase(phase, field);
                memory.Actor(16, -1175, -776, 0, visible: false, talkRadius: 180);
                memory.Actor(15, -7, -55, 30);
                memory.Actor(14, 89, -231, 30);
                memory.Actor(12, 553, -5260, 0);
                memory.Actor(7, 68, -243, -25);
                var reader = memory.Story(5, 7, 8, 9, 11, 12, 16, 17, 18);
                var offered = reader.ReadTargets(memory.At(field));
                if (field == GodosPagoda)
                {
                    Equal(true, offered.All(target => PagodaChallengeLabels.Contains(target.Label)),
                        $"{phase}: the Pagoda offers only its own optional fights and stairs");
                    continue;
                }

                Equal(0, offered.Count, $"{phase}: field {field} offers nothing");
            }
        }

        var chest = Phase(QuestPhase.Complete, Shop);
        chest.Actor(7, 198, 163, 0, talkRadius: 55);
        Equal(false, chest.Objects().ReadTargets(chest.At(Shop)).Any(t => t.TriggerEntityId == 7),
            "the opened chest stays gone after the quest");
    }

    /// <summary>
    /// uttmpin1/JIKU: arriving from hideway1 its Init holds triangle 22, so the room behind
    /// the scroll is triangles 94 to 97. Its reverse handler turns the scroll from triangle
    /// 95 only, and only while the leader's X is greater than -619.
    /// </summary>
    private static void TheHangingScrollCanBeTurnedBackFromInside()
    {
        var memory = Phase(QuestPhase.NotStarted, GodosHouse);
        var inside = memory.Objects().ReadTargets(memory.At(GodosHouse, -810, -88, 0, 94))
            .Where(t => t.Label.Contains("scroll", StringComparison.OrdinalIgnoreCase) && t.X > -619 && t.X < -397)
            .ToArray();
        Equal(1, inside.Length, "the far side of the scroll is an object of its own");
        Equal("Hanging scroll, other side", inside[0].Label, "named like the folding screen's far side");
        Equal(true, inside[0].X - inside[0].InteractionRadius > -619,
            "the whole arrival disc satisfies the native X guard");
        Equal(false,
            memory.Objects().ReadTargets(memory.At(GodosHouse, -810, -88, 0, 94)).Any(t => t.Label == "Hanging scroll"),
            "the front of the scroll is behind its own lock from in here");

        var hall = memory.Objects().ReadTargets(memory.At(GodosHouse, -305, -65, 0, 53))
            .Where(t => t.Label.Contains("scroll", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Label)
            .ToArray();
        Equal("Hanging scroll", string.Join("|", hall),
            "the hall offers only the face it can see, and names no room behind it");
    }

    private static void Labels(
        QuestMemory memory,
        int field,
        string[] expected,
        string message,
        FieldStoryTargetReader? reader = null,
        FieldPositionSnapshot? position = null)
    {
        var at = position ?? memory.At(field);
        var actual = (reader ?? memory.Story()).ReadTargets(at).Select(t => t.Label).OrderBy(l => l).ToArray();
        var wanted = expected.OrderBy(l => l).ToArray();
        if (!actual.SequenceEqual(wanted))
        {
            throw new InvalidOperationException(
                $"wutai materia quest - field {field}, {message}: expected [{string.Join(", ", wanted)}], got [{string.Join(", ", actual)}]");
        }
    }

    // --- The installed native archive ---------------------------------------------

    /// <summary>
    /// The flags, lines and gateways the rows rely on, read from the installed game, and a
    /// route from each native arrival to each step under the triangle locks the field's own
    /// scripts hold at that point in the quest.
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var source = new FlevelDataSource(root);
        var catalog = new FieldScriptNavigationCatalog(root);
        NativeGatesStillHold(catalog);
        TriggersAreTheFieldsOwnCrossings(source, catalog);
        var routes = EveryStepRoutesFromItsNativeArrivals(source, catalog);
        Console.WriteLine($"Wutai materia quest: native gates, crossings and {routes} routes from native arrivals match the installed archive.");
    }

    private static void NativeGatesStillHold(FieldScriptNavigationCatalog catalog)
    {
        (int Field, int Entity, int Script, string Hex, string Meaning)[] anchors =
        [
            (Bar, 15, 1, "1430BD0206", "Reno's Talk tests Bank[3][189] bit 1"),
            (Bar, 15, 1, "010AC3", "and requests AD Script 3"),
            (Bar, 14, 1, "1430BD0206", "Rude's Talk tests Bank[3][189] bit 1"),
            (Bar, 14, 1, "010AC3", "and requests AD Script 3"),
            (Bar, 10, 3, "8230BD01", "AD Script 3 sets Bank[3][189] bit 1"),
            (Shop, 7, 1, "1430BD0206", "TAKARA's Talk waits for Bank[3][189] bit 1"),
            (Shop, 7, 1, "14103B0206", "and for its own Bank[1][59] bit 1"),
            (Shop, 7, 1, "82103B01", "which it sets"),
            (Shop, 7, 1, "8230BE00", "before the theft sets Bank[3][190] bit 0"),
            (OldMansHouse, 16, 0, "1430BE0106", "BYOBU waits for Bank[3][190] bit 0"),
            (OldMansHouse, 16, 4, "3120", "BYOBU Go tests Confirm"),
            (OldMansHouse, 17, 4, "3120", "BYOBUB Go tests Confirm"),
            (OldMansHouse, 10, 3, "8230BE01", "YUFI Script 3 sets Bank[3][190] bit 1"),
            (Town, 20, 0, "1430BE0206", "AD2 waits for Bank[3][190] bit 1"),
            (Town, 20, 0, "1430BD0406", "and for Bank[3][189] bit 2 to be clear"),
            (Town, 20, 0, "0310C4", "before requesting YUFI Script 4"),
            (Town, 16, 4, "A5000069FBF8FC0000D600", "YUFI Script 4 places her at (-1175,-776) on triangle 214"),
            (Town, 16, 4, "7E00", "with TLKON on"),
            (Town, 16, 4, "C500B4", "and TALKR 180"),
            (Town, 16, 1, "A401", "her Talk is what first shows her"),
            (Town, 19, 5, "8230BD02", "AD Script 5 sets Bank[3][189] bit 2"),
            (Town, 19, 0, "6DAA0001", "AD holds triangle 170 until then"),
            (YuffiesHouse, 7, 4, "8230BE02", "YUFI Script 4 sets Bank[3][190] bit 2"),
            (YuffiesHouse, 10, 0, "6D010001", "AD holds triangle 1 before the trap"),
            (BackRoom, 7, 1, "1450000000", "YUFI's Talk tests 5[0]"),
            (BackRoom, 7, 7, "80500001", "and her scene sets it"),
            (BackRoom, 7, 7, "030BC7", "and switches the SWITCH on"),
            (BackRoom, 11, 1, "8230BE04", "either lever sets Bank[3][190] bit 4"),
            (BackRoom, 11, 1, "8330BE04", "and the same SWITCH clears it"),
            (BackRoom, 7, 9, "8230BD00", "YUFI Script 9 sets Bank[3][189] bit 0"),
            (Pagoda, 11, 0, "6D720001", "AD holds triangle 114 before the trap"),
            (Pagoda, 11, 0, "6D800001", "and triangle 128 after it"),
            (Pagoda, 14, 0, "1450030000", "KANE tests 5[3]"),
            (Pagoda, 14, 0, "6D800000", "releases triangle 128"),
            (Pagoda, 14, 0, "80500301", "and sets 5[3]"),
            (Pagoda, 12, 1, "8230BF02", "RENO's Talk sets Bank[3][191] bit 2"),
            (Pagoda, 12, 1, "82D05005", "and Bank[13][80] bit 5"),
            (HiddenRoom, 7, 0, "8230BF00", "AD sets Bank[3][191] bit 0"),
            (PrayerRoom, 14, 0, "70006E02", "AD fights battle 622"),
            (PrayerRoom, 14, 0, "8230BF01", "and sets Bank[3][191] bit 1"),
            (597, 9, 0, "82D05006", "the Da-chao scene sets Bank[13][80] bit 6"),
            (YuffiesHouse, 10, 4, "8230BD04", "the return sets Bank[3][189] bit 4"),
            (YuffiesHouse, 10, 4, "8230CF06", "and Bank[3][207] bit 6"),
            (GodosHouse, 14, 0, "166008005F0000", "JIKU's reverse turn is polled on triangle 95"),
            (GodosHouse, 14, 0, "1660020095FD02", "only while the leader's X is greater than -619"),
        ];
        foreach (var anchor in anchors)
        {
            var found = catalog.ReadAllScriptOpcodes(anchor.Field).Any(s =>
                s.EntityId == anchor.Entity && s.ScriptId == anchor.Script &&
                s.Opcodes.Any(o => Convert.ToHexString(o.Bytes.ToArray())
                    .StartsWith(anchor.Hex, StringComparison.OrdinalIgnoreCase)));
            Equal(true, found, $"native {anchor.Field}/{anchor.Entity}/{anchor.Script}: {anchor.Meaning}");
        }
    }

    /// <summary>Every row's trigger is the native gateway or LINE it names, leading where it says.</summary>
    private static void TriggersAreTheFieldsOwnCrossings(FlevelDataSource source, FieldScriptNavigationCatalog catalog)
    {
        var destinations = new Dictionary<string, int>
        {
            ["579:Go into Turtle's Paradise"] = Bar,
            ["579:Go into the Item Store"] = Shop,
            ["579:Go into the Old Man's House"] = OldMansHouse,
            ["579:Go back into Yuffie's House"] = YuffiesHouse,
            ["579:Go to the Pagoda"] = Pagoda,
            ["579:Go to the Da-chao Statue"] = DaChaoFoot,
            ["580:Leave Turtle's Paradise"] = Town,
            ["576:Leave the Item Store"] = Town,
            ["578:Leave the Old Man's House"] = Town,
            ["575:Leave the shop"] = Town,
            ["577:Leave the Cat's House"] = Town,
            ["585:Go back to the Cat's House"] = 577,
            ["581:Go into the back room"] = BackRoom,
            ["581:Leave Yuffie's House"] = Town,
            ["582:Go back to the front room"] = YuffiesHouse,
            ["587:Go through the open door"] = HiddenRoom,
            ["587:Go back into town"] = Town,
            ["591:Go after Corneo"] = PrayerRoom,
            ["591:Go back out to the Pagoda"] = Pagoda,
            ["590:Go back to the Hidden Room"] = HiddenRoom,
            ["590:Go back out through the house"] = GodosInnerHouse,
            ["589:Go back toward the Pagoda"] = GodosHouse,
            ["588:Go back out to the Pagoda"] = Pagoda,
            ["592:Climb Da-chao"] = 593,
            ["592:Go back down to town"] = Town,
            ["593:Keep climbing Da-chao"] = 596,
            ["593:Go back down"] = DaChaoFoot,
            ["594:Go back the way you came"] = DaChaoFoot,
            ["595:Go back the way you came"] = 593,
            ["596:Keep climbing Da-chao"] = 597,
            ["596:Go back down"] = 593,
            ["597:Go back down"] = 596,
            ["598:Go back the way you came"] = 596,
            ["599:Go back the way you came"] = 596,
        };
        // The Pagoda's own challenge rows are checked against its lines and map jumps in
        // FieldPuzzleStoryTests; every other Wutai row belongs to the Materia quest.
        var rows = FieldStoryEventCatalog.CreateAllFields()
            .Where(r => WutaiFields.Contains(r.FieldId) && r.FieldId != GodosPagoda).ToArray();
        Equal(true, FieldStoryEventCatalog.CreateAllFields().Where(r => r.FieldId == GodosPagoda)
                .All(r => PagodaChallengeLabels.Contains(r.Label)),
            "the Pagoda holds only its own challenge rows");
        foreach (var row in rows.Where(r => r.TriggerLine is not null))
        {
            var key = $"{row.FieldId}:{row.Label}";
            Equal(true, source.TryReadField(row.FieldId, out var encoded), $"{key}: field readable");
            var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var destination = -1;
            if (row.CompletesOnArrival)
            {
                Equal(true, destinations.TryGetValue(key, out destination), $"{key} has an independently recorded destination");
            }

            if (row.RequiredEnabledLineEntityId is { } lineEntity)
            {
                var scripts = catalog.ReadAllScriptOpcodes(row.FieldId).Where(s => s.EntityId == lineEntity).ToArray();
                var line = scripts.Where(s => s.ScriptId == 0).SelectMany(s => s.Opcodes)
                    .Single(o => o.Opcode == 0xD0).Bytes.ToArray();
                Equal(row.TriggerLine, ReadLine(line, 1), $"{key}: trigger is LINE entity {lineEntity}");
                Equal(true, scripts.All(s => s.EntityName == row.SourceEntityName), $"{key}: LINE entity name");
                if (row.CompletesOnArrival)
                {
                    Equal(true, scripts.Any(s => s.ScriptId != 0 && s.Opcodes.Any(o =>
                            o.Opcode == 0x60 && o.Bytes.Count == 10 &&
                            BitConverter.ToUInt16(o.Bytes.ToArray(), 1) == destination)),
                        $"{key}: LINE crossing maps to {destination}");
                }
                else
                {
                    // A Confirm on a LINE is its Go or [OK] handler testing the key itself.
                    Equal(true, scripts.Any(s => s.ScriptId is 1 or 4 && s.Opcodes.Any(o => o.Opcode is 0x31 or 0x48)),
                        $"{key}: LINE is used with Confirm");
                }
            }
            else
            {
                Equal(true, row.CompletesOnArrival, $"{key}: a gateway is crossed, not confirmed");
                Equal(true, NativeGateways(bytes).Any(g => g.Line == row.TriggerLine && g.Destination == destination),
                    $"{key}: trigger is the field's own gateway to {destination}");
            }
        }
        Equal(destinations.Count, rows.Count(r => r.TriggerLine is not null && r.CompletesOnArrival),
            "every recorded crossing is present exactly once");
    }

    private sealed record RouteStep(
        string Label,
        int Field,
        QuestPhase Phase,
        int[] From,
        int[] Locks,
        (int Entity, int X, int Y, int Z, int TalkRadius, bool Visible)[] Actors,
        int[] EnabledLines,
        Action<QuestMemory>? Adjust = null);

    private static int EveryStepRoutesFromItsNativeArrivals(FlevelDataSource source, FieldScriptNavigationCatalog catalog)
    {
        (int, int, int, int, int, bool)[] turks = [(15, -7, -55, 30, 80, true), (14, 89, -231, 30, 80, true), (13, -13, -140, 30, 80, true)];
        RouteStep[] steps =
        [
            new("Go into Turtle's Paradise", Town, QuestPhase.Arrived, [Shop, OldMansHouse, 575, 577, Pagoda, DaChaoFoot], [170], [], []),
            new("Talk to Reno", Bar, QuestPhase.Arrived, [Town], [], turks, [9]),
            new("Talk to Rude", Bar, QuestPhase.Arrived, [Town], [], turks, [9]),
            new("Leave Turtle's Paradise", Bar, QuestPhase.PubSeen, [Town], [], turks, [9]),
            new("Go into the Item Store", Town, QuestPhase.PubSeen, [Bar], [170], [], []),
            new("Open the treasure chest", Shop, QuestPhase.PubSeen, [Town], [], [(7, 198, 163, 0, 55, true), (6, -151, 243, 0, 80, true), (5, -247, -23, 0, 80, true)], [8]),
            new("Leave the Item Store", Shop, QuestPhase.ChestStolen, [Town], [], [(6, -151, 243, 0, 80, true), (5, -247, -23, 0, 80, true)], [8]),
            new("Go into the Old Man's House", Town, QuestPhase.ChestStolen, [Shop], [170], [], []),
            new("Check behind the folding screen", OldMansHouse, QuestPhase.ChestStolen, [Town], [65, 64, 63, 59], [(14, 5, 123, 18, 80, true), (15, 75, 126, 10, 80, true)], [16, 17, 18]),
            new("Check behind the folding screen from the other side", OldMansHouse, QuestPhase.ChestStolen, [Town], [65, 64, 63, 59], [(14, 5, 123, 18, 80, true), (15, 75, 126, 10, 80, true)], [16, 17, 18]),
            new("Leave the Old Man's House", OldMansHouse, QuestPhase.ScreenChecked, [Town], [], [(14, 5, 123, 18, 80, true)], [18]),
            new("Check the shaking pot", Town, QuestPhase.ScreenChecked, [OldMansHouse, Shop, Bar], [170, 212, 213], [(16, -1175, -776, 0, 180, false)], []),
            new("Go into the back room", YuffiesHouse, QuestPhase.FollowSeen, [Bar, BackRoom], [1], [], [12]),
            new("Talk to Yuffie", BackRoom, QuestPhase.FollowSeen, [YuffiesHouse], [], [(7, 68, -243, -25, 80, true)], []),
            new("Use the levers", BackRoom, QuestPhase.FollowSeen, [YuffiesHouse], [], [(7, 68, -243, -25, 80, true)], [11], m => m.Set(5, 0, 0x01)),
            new("Go back to the front room", BackRoom, QuestPhase.CageRaised, [YuffiesHouse], [], [], [11]),
            new("Leave Yuffie's House", YuffiesHouse, QuestPhase.CageRaised, [BackRoom], [], [], [12]),
            new("Go to the Pagoda", Town, QuestPhase.CageRaised, [YuffiesHouse, Pagoda], [], [], []),
            new("Ring the bell", Pagoda, QuestPhase.CageRaised, [Town, GodosHouse], [128], [], [17]),
            new("Go after Corneo", HiddenRoom, QuestPhase.CorneoSeen, [Pagoda], [], [], []),
            new("Go through the open door", Pagoda, QuestPhase.CorneoSeen, [HiddenRoom], [], [], [17], m => m.Set(5, 3, 0x01)),
            new("Go back to the Hidden Room", PrayerRoom, QuestPhase.BattleWon, [HiddenRoom], [18, 19, 22], [], []),
            new("Go back out through the house", PrayerRoom, QuestPhase.BattleWon, [GodosInnerHouse], [21], [], []),
            new("Go back toward the Pagoda", GodosInnerHouse, QuestPhase.BattleWon, [PrayerRoom, GodosHouse], [60, 59, 8, 116, 123, 119, 113], [], []),
            new("Go back out to the Pagoda", GodosHouse, QuestPhase.BattleWon, [Pagoda, GodosInnerHouse], [20, 23, 46, 45, 89, 91], [], []),
            new("Go back out to the Pagoda", HiddenRoom, QuestPhase.BattleWon, [PrayerRoom], [], [], []),
            new("Talk to Reno", Pagoda, QuestPhase.BattleWon, [HiddenRoom], [33], [(12, 553, -5260, 0, 80, true), (13, 315, -5240, 0, 80, true)], [17], m => m.Set(5, 3, 0x01)),
            new("Talk to Reno", Pagoda, QuestPhase.BattleWon, [Town, GodosHouse], [33, 128], [(12, 553, -5260, 0, 80, true), (13, 315, -5240, 0, 80, true)], [17]),
            new("Go back into town", Pagoda, QuestPhase.RenoSpoken, [HiddenRoom, GodosHouse, 586], [], [], [17], m => m.Set(5, 3, 0x01)),
            new("Go to the Da-chao Statue", Town, QuestPhase.RenoSpoken, [Pagoda], [], [], []),
            new("Climb Da-chao", DaChaoFoot, QuestPhase.RenoSpoken, [Town, 594], [], [], [5]),
            new("Keep climbing Da-chao", 593, QuestPhase.RenoSpoken, [DaChaoFoot, 595], [], [], []),
            new("Keep climbing Da-chao", 596, QuestPhase.RenoSpoken, [593, 598, 599], [], [], []),
            new("Leave the shop", 575, QuestPhase.Arrived, [Town], [], [], [7]),
            new("Leave the Cat's House", 577, QuestPhase.Arrived, [Town, 585], [], [], [5]),
            new("Go back to the Cat's House", 585, QuestPhase.Arrived, [577], [], [], []),
            new("Go back into town", Pagoda, QuestPhase.Arrived, [Town, GodosHouse, 586], [114], [], [17]),
            new("Go back down to town", DaChaoFoot, QuestPhase.Arrived, [593, 594], [], [], [5]),
            new("Go back down", 593, QuestPhase.Arrived, [596], [], [], []),
            new("Go back the way you came", 594, QuestPhase.Arrived, [DaChaoFoot], [], [], []),
            new("Go back the way you came", 595, QuestPhase.Arrived, [593], [], [], []),
            new("Go back down", 596, QuestPhase.Arrived, [597], [], [], []),
            new("Go back down", 597, QuestPhase.Arrived, [596], [], [], []),
            new("Go back the way you came", 598, QuestPhase.Arrived, [596], [], [], []),
            new("Go back the way you came", 599, QuestPhase.Arrived, [596], [], [], []),
        ];

        var routes = 0;
        foreach (var step in steps)
        {
            Equal(true, source.TryReadField(step.Field, out var encoded), $"{step.Label}: field {step.Field} readable");
            var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
            const int pointer = 0x03000000;
            int ReadMeshInt(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
                a >= pointer && a + 4 <= pointer + bytes.Length ? BitConverter.ToInt32(bytes, a - pointer) : 0;
            short ReadMeshShort(int a) => a >= pointer && a + 2 <= pointer + bytes.Length ? BitConverter.ToInt16(bytes, a - pointer) : (short)0;
            var meshReader = new FieldWalkmeshReader(ReadMeshInt, ReadMeshShort);
            var mesh = meshReader.Read(new(FieldPositionReader.FieldModule, step.Field, 0, 0, 0, 0, 0, 0)).Walkmesh
                ?? throw new InvalidOperationException($"{step.Label}: walkmesh {step.Field} unreadable");
            var transitions = catalog.ReadField(step.Field).Transitions;
            foreach (var from in step.From)
            {
                var arrivals = NativeArrivals(source, catalog, from, step.Field).ToArray();
                Equal(true, arrivals.Length > 0, $"{step.Label}: field {from} has a native arrival in {step.Field}");
                foreach (var arrival in arrivals)
                {
                    var memory = Phase(step.Phase, step.Field);
                    foreach (var actor in step.Actors)
                    {
                        memory.Actor(actor.Entity, actor.X, actor.Y, actor.Z, actor.Visible, actor.TalkRadius);
                    }
                    step.Adjust?.Invoke(memory);
                    var position = memory.At(step.Field, arrival.X, arrival.Y,
                        (int)Math.Round(mesh.Triangles[arrival.Triangle].GetCentroid().Z), arrival.Triangle);
                    var target = memory.Story(step.EnabledLines).ReadTargets(position)
                        .Where(t => t.Label == step.Label).ToArray();
                    Equal(1, target.Length, $"{step.Label} is offered at native arrival {from}:{arrival.Triangle}");
                    var boundary = memory.Boundary(step.Locks);
                    var obstacles = step.Actors
                        .Where(a => a.Visible && a.Entity != target[0].TriggerEntityId)
                        .Select(a => new FieldNavigationDynamicObstacle(a.Entity + 1, a.X, a.Y, a.Z, 30d, 30d))
                        .ToArray();
                    var planner = new FieldWalkmeshRoutePlanner(meshReader,
                        step.Locks.Length == 0 ? null : boundary,
                        transitionProvider: _ => transitions,
                        dynamicObstacleProvider: obstacles.Length == 0 ? null : (_, _) => obstacles);
                    Equal(true, planner.TryBuildRoute(position, target[0], out _),
                        $"{step.Label}: route from native arrival {from}:{arrival.Triangle} under locks [{string.Join(",", step.Locks)}]: {planner.LastDiagnostic}");
                    routes++;
                }
            }
        }

        // The locks are the reason for the order. Before the trap the bell is shut away by
        // triangle 114, and the pocket by the Hidden Room cannot be left for it from the far
        // side while triangle 21 is held.
        var closed = Phase(QuestPhase.Arrived, Pagoda);
        var bellTarget = new FieldNavigationTarget(Pagoda, FieldNavigationCategory.Story, "bell", -623, -4862, 127,
            CompletionTriangles: [138, 139, 140, 86]);
        Equal(false, RouteExists(source, catalog, closed, Pagoda, Town, [114], bellTarget),
            "before the trap the bell is behind a native lock, which is why the Pagoda does not offer it");
        return routes;
    }

    private static bool RouteExists(
        FlevelDataSource source,
        FieldScriptNavigationCatalog catalog,
        QuestMemory memory,
        int field,
        int from,
        int[] locks,
        FieldNavigationTarget target)
    {
        source.TryReadField(field, out var encoded);
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        const int pointer = 0x03000000;
        int ReadMeshInt(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
            a >= pointer && a + 4 <= pointer + bytes.Length ? BitConverter.ToInt32(bytes, a - pointer) : 0;
        short ReadMeshShort(int a) => a >= pointer && a + 2 <= pointer + bytes.Length ? BitConverter.ToInt16(bytes, a - pointer) : (short)0;
        var meshReader = new FieldWalkmeshReader(ReadMeshInt, ReadMeshShort);
        var mesh = meshReader.Read(new(FieldPositionReader.FieldModule, field, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var planner = new FieldWalkmeshRoutePlanner(meshReader, memory.Boundary(locks),
            transitionProvider: _ => catalog.ReadField(field).Transitions);
        return NativeArrivals(source, catalog, from, field).Any(arrival => planner.TryBuildRoute(
            memory.At(field, arrival.X, arrival.Y, (int)Math.Round(mesh.Triangles[arrival.Triangle].GetCentroid().Z), arrival.Triangle),
            target, out _));
    }

    private static IEnumerable<(int X, int Y, ushort Triangle)> NativeArrivals(
        FlevelDataSource source,
        FieldScriptNavigationCatalog catalog,
        int from,
        int to)
    {
        if (!source.TryReadField(from, out var encoded)) yield break;
        foreach (var gateway in NativeGateways(Ff7LzsDecoder.DecodeFieldFile(encoded)).Where(g => g.Destination == to))
            yield return (gateway.X, gateway.Y, gateway.Triangle);
        foreach (var op in catalog.ReadAllScriptOpcodes(from).SelectMany(s => s.Opcodes)
                     .Where(o => o.Opcode == 0x60 && o.Bytes.Count == 10))
        {
            var b = op.Bytes.ToArray();
            if (BitConverter.ToUInt16(b, 1) == to)
                yield return (BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5), BitConverter.ToUInt16(b, 7));
        }
    }

    private static IEnumerable<(FieldNavigationTriggerLine Line, int Destination, int X, int Y, ushort Triangle)> NativeGateways(byte[] bytes)
    {
        var section = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
        for (var i = 0; i < 12; i++)
        {
            var at = section + 0x38 + i * 24;
            var destination = BitConverter.ToInt16(bytes, at + 18);
            if (destination >= 0)
                yield return (ReadLine(bytes, at), destination, BitConverter.ToInt16(bytes, at + 12),
                    BitConverter.ToInt16(bytes, at + 14), BitConverter.ToUInt16(bytes, at + 16));
        }
    }

    private static FieldNavigationTriggerLine ReadLine(byte[] bytes, int offset) => new(
        BitConverter.ToInt16(bytes, offset), BitConverter.ToInt16(bytes, offset + 2),
        BitConverter.ToInt16(bytes, offset + 4), BitConverter.ToInt16(bytes, offset + 6),
        BitConverter.ToInt16(bytes, offset + 8), BitConverter.ToInt16(bytes, offset + 10));

    // --- Memory ------------------------------------------------------------------

    private enum QuestPhase
    {
        NotStarted,
        Arrived,
        PubSeen,
        ChestStolen,
        ScreenChecked,
        PotChecked,
        FollowSeen,
        TrapSprung,
        CageRaised,
        CorneoSeen,
        BattleWon,
        RenoSpoken,
        Complete
    }

    /// <summary>Each phase is the bits the native scripts themselves have written by then.</summary>
    private static QuestMemory Phase(QuestPhase phase, int field)
    {
        var memory = new QuestMemory(field);
        memory.SetGameMoment(640);
        if (phase == QuestPhase.NotStarted) return memory;
        memory.Set(3, 207, 0x20);
        memory.Set(3, 189, 0x20);
        if (phase >= QuestPhase.PubSeen) memory.Set(3, 189, 0x02);
        if (phase >= QuestPhase.ChestStolen) { memory.Set(1, 59, 0x02); memory.Set(3, 190, 0x01); }
        if (phase >= QuestPhase.ScreenChecked) memory.Set(3, 190, 0x02);
        if (phase >= QuestPhase.PotChecked) memory.Set(3, 189, 0x04 | 0x08);
        if (phase >= QuestPhase.FollowSeen) memory.Set(3, 190, 0x04 | 0x08);
        if (phase >= QuestPhase.TrapSprung) { memory.Set(3, 190, 0x10 | 0x20); memory.Set(3, 189, 0x01); }
        if (phase >= QuestPhase.CageRaised) { memory.Clear(3, 190, 0x70); memory.Set(3, 190, 0x80); }
        if (phase >= QuestPhase.CorneoSeen) memory.Set(3, 191, 0x01);
        if (phase >= QuestPhase.BattleWon) memory.Set(3, 191, 0x02);
        if (phase >= QuestPhase.RenoSpoken) { memory.Set(3, 191, 0x04 | 0x08); memory.Set(13, 80, 0x20); }
        if (phase >= QuestPhase.Complete)
        {
            memory.Set(13, 80, 0x40);
            memory.Set(3, 189, 0x10);
            memory.Set(3, 207, 0x40);
        }
        return memory;
    }

    private sealed class QuestMemory
    {
        private const int EventTable = 0x02600000;
        private const int FieldState = 0x02700000;
        private const int PlayerCollisionRadius = 30;
        private readonly Dictionary<int, byte> bytes = [];
        private readonly Dictionary<int, int> modelByEntity = [];
        private readonly int field;
        private int[] lockedTriangles = [];

        public QuestMemory(int field)
        {
            this.field = field;
            bytes[FieldPositionReader.AddressFieldNumModels] = 24;
            for (var entity = 0; entity < 64; entity++)
            {
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = 0xFF;
            }

            WriteModel(0, 0, 0, 0, visible: true, talkEnabled: false, talkRadius: 0);
        }

        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        public void Set(int bank, int address, int mask) =>
            bytes[Bank(bank, address)] = (byte)(ReadByte(Bank(bank, address)) | mask);

        public void Clear(int bank, int address, int mask) =>
            bytes[Bank(bank, address)] = (byte)(ReadByte(Bank(bank, address)) & ~mask);

        /// <summary>A field entity with a model, where its own script puts it.</summary>
        public void Actor(int entity, int x, int y, int z, bool visible = true, int talkRadius = 80, bool talkEnabled = true)
        {
            if (!modelByEntity.TryGetValue(entity, out var model))
            {
                model = modelByEntity.Count + 1;
                modelByEntity[entity] = model;
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = (byte)model;
            }

            WriteModel(model, x, y, z, visible, talkEnabled, talkRadius);
        }

        public FieldPositionSnapshot At(int fieldId) => At(fieldId, 0, 0, 0, 0);

        public FieldPositionSnapshot At(int fieldId, int x, int y, int z, ushort triangle)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = FieldPositionReader.FieldModule;
            bytes[FieldPositionReader.AddressFieldId] = (byte)fieldId;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(fieldId >> 8);
            return new(FieldPositionReader.FieldModule, fieldId, 0, x, y, z, triangle, 0);
        }

        public FieldStoryTargetReader Story(params int[] enabledLines) =>
            new(ReadInt32, ReadInt16, ReadByte, FieldStoryEventCatalog.CreateAllFields(),
                entity => enabledLines.Contains(entity));

        public FieldNavigationObjectReader Objects() =>
            new(ReadInt32, ReadByte, _ => "Item", _ => "Materia", FieldNavigationObjectCatalog.CreateAllFields(), _ => true);

        public FieldBoundaryStateReader Boundary(int[] locks)
        {
            lockedTriangles = locks;
            return new FieldBoundaryStateReader(ReadInt32, ReadByte, (_, _) => true);
        }

        public byte ReadByte(int address)
        {
            var boundary = address - FieldState - FieldBoundaryStateReader.BoundaryBitsOffset;
            if (boundary >= 0 && boundary < FieldBoundaryStateReader.BoundaryByteCount)
            {
                byte bits = 0;
                foreach (var triangle in lockedTriangles)
                {
                    if (triangle >> 3 == boundary) bits |= (byte)(1 << (triangle & 7));
                }

                return bits;
            }

            return bytes.GetValueOrDefault(address);
        }

        public short ReadInt16(int address) => (short)(ReadByte(address) | (ReadByte(address + 1) << 8));

        public int ReadInt32(int address) => address switch
        {
            FieldBoundaryStateReader.AddressFieldGlobalObjectPtr => FieldState,
            FieldNavigationObjectReader.AddressFieldEventDataPtr => EventTable,
            _ => ReadByte(address) | (ReadByte(address + 1) << 8) |
                 (ReadByte(address + 2) << 16) | (ReadByte(address + 3) << 24),
        };

        private void WriteModel(int model, int x, int y, int z, bool visible, bool talkEnabled, int talkRadius)
        {
            var record = EventTable + model * FieldNavigationObjectReader.FieldEventDataStride;
            WriteInt32(record + FieldNavigationObjectReader.PositionXOffset, x * FieldNavigationObjectReader.ModelPositionFixedPointScale);
            WriteInt32(record + FieldNavigationObjectReader.PositionYOffset, y * FieldNavigationObjectReader.ModelPositionFixedPointScale);
            WriteInt32(record + FieldNavigationObjectReader.PositionZOffset, z * FieldNavigationObjectReader.ModelPositionFixedPointScale);
            bytes[record + FieldNavigationObjectReader.VisibilityOffset] = visible ? (byte)1 : (byte)0;
            bytes[record + FieldNavigationNpcReader.TalkDisabledOffset] = talkEnabled ? (byte)0 : (byte)1;
            WriteInt16(record + FieldNavigationNpcReader.CollisionRadiusOffset, PlayerCollisionRadius);
            WriteInt16(record + FieldNavigationNpcReader.TalkRadiusOffset, talkRadius);
        }

        private void WriteInt16(int address, int value)
        {
            bytes[address] = (byte)value;
            bytes[address + 1] = (byte)(value >> 8);
        }

        private void WriteInt32(int address, int value)
        {
            for (var index = 0; index < 4; index++)
            {
                bytes[address + index] = (byte)(value >> (index * 8));
            }
        }

        private static int Bank(int bank, int address) => bank switch
        {
            1 => FieldNavigationObjectReader.AddressFieldBankBase + address,
            3 => FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + address,
            5 => FieldNavigationObjectReader.AddressTemporaryFieldBankBase + address,
            13 => FieldNavigationObjectReader.AddressFieldBankBase + 0x300 + address,
            _ => throw new ArgumentOutOfRangeException(nameof(bank))
        };
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"wutai materia quest - {label}: expected {expected}, got {actual}");
        }
    }
}
