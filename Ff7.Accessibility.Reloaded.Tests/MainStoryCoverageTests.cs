using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Story guidance through the chapters that were reviewed against native scripts,
/// exercised as state and branches rather than as a count of rows.
///
/// Two things this file deliberately does not do. It does not assert a catalog size:
/// a bigger catalog is not a followable story, and an earlier draft's broad
/// door-graph pass was withdrawn precisely because its rows looked like coverage
/// without being routes. And it does not assert that every room has something to
/// say - most do not yet, and the coverage ledger is where that is tracked honestly.
/// </summary>
internal static class MainStoryCoverageTests
{
    internal static void Run()
    {
        StoryNativeBindingTests.Run();
        TheCorelPrisonRouteHasAStepAtEveryStage();
        DisabledGatewaysAreNeverOfferedAsPrisonExits();
        PrisonExitsCarryTheNativeLineContract();
        TheChocoboRaceIsAVisibleInteractionAndRetryable();
        PrisonObjectivesStayInsideTheirOwnChapter();
        TheIcicleInnFollowsItsOwnLocalFlags();
        GaeasCliffIsOrderedByItsOwnCabinFlags();
        TheNativeEntryDoorsCarryTheirOwnGuardMoment();
        TheSleepingForestWaitsOnTheHarp();
        CosmoCanyonIsOrderedByItsOwnLocalState();
        RocketTownFirstVisitFollowsTheYardNotTheRocket();
        StoryLabelsDoNotNameChaptersOrRevealScenes();
        EveryStoryRowNamesARealFieldAndPlace();
        AStepThatCompletesNothingIsNotHiddenByTheDoorThatDoes();
        TheReactorPipingOffersTheJumpsBetweenItsDoors();
        TheCraterLatticeOffersOnlyWaysDown();
        TheCraterSideCaveAnswersForTheSideThePlayerIsStandingOn();
        CidsHighwindHandoffIsWalkedRoomByRoom();
        TheShootingCoasterSendsThePlayerToRegisterRatherThanAtTheBarrier();
    }

    /// <summary>
    /// The native route: arrive at 446, find Barret in the house, and once the Corel
    /// flashback has returned at 457, cross the desert to Dyne.
    /// </summary>
    private static void TheCorelPrisonRouteHasAStepAtEveryStage()
    {
        var memory = new StoryMemory();
        var reader = memory.StoryReader();

        memory.SetGameMoment(446);
        Equal("Go into the prison town to find Barret", Objective(reader, 471),
            "the drop point sends the party to the town, which is what the native refusal says");
        Equal("Enter the house where Barret is", Objective(reader, 473),
            "and the town sends them to the house whose director runs the scene on entry");

        // 448 to 456 is the Corel flashback, which plays itself out in other fields.
        foreach (var moment in new[] { 448, 451, 454 })
        {
            memory.SetGameMoment(moment);
            Equal(0, Objectives(reader, 471).Length,
                $"the flashback at moment {moment} is not the prison's to narrate");
            Equal(0, Objectives(reader, 473).Length,
                $"nor at moment {moment} in the town");
        }

        memory.SetGameMoment(457);
        Equal("Leave the house", Objective(reader, 475),
            "the scene ends in the house, so the first step is back out of it");
        Equal("Go back out to the prison floor", Objective(reader, 473),
            "the desert is only reachable through the open floor");
        Equal("Head out into the desert to find Dyne", Objective(reader, 471),
            "and the desert exits open once the flashback has returned past 454");
        // The desert road is two roads. jail1's jl3 lands on triangle 23, whose
        // walkmesh component reaches the crossing; its jl1 and jl2 land on triangle 5,
        // whose component of twenty triangles reaches nothing. So which objective jail3
        // offers depends on which side of the fence the player is standing on, and a
        // row keyed on the field alone would be right half the time.
        Equal("Cross the desert toward Dyne",
            Objective(reader, new FieldPositionSnapshot(FieldPositionReader.FieldModule, 478, 0, -240, -64, -7, 23, 0)),
            "from the connected road the crossing is the step");
        Equal("Go back to the prison and take the western road",
            Objective(reader, new FieldPositionSnapshot(FieldPositionReader.FieldModule, 478, 0, 271, -85, -5, 5, 0)),
            "and from the disconnected one the step is getting off it");
        Equal("Go up to where Dyne is waiting", Objective(reader, 479),
            "and the last one reaches him");

        // Dyne's own script writes 463 and map-jumps straight to Mr Coates, whose
        // director runs on arrival. That is a deliberate silence, not a missing row.
        memory.SetGameMoment(463);
        foreach (var field in new[] { 480, 513 })
        {
            Equal(0, Objectives(reader, field).Length,
                $"field {field} after Dyne is a scene chain and must not be narrated as a task");
        }
    }

    /// <summary>
    /// jail1, jail3 and jail4 each call MPJPO in their Director Init, which switches
    /// off every static gateway they own. Their gateway tables still describe doors
    /// with the right destinations, which is exactly what makes routing to one so
    /// easy to get wrong.
    /// </summary>
    private static void DisabledGatewaysAreNeverOfferedAsPrisonExits()
    {
        var memory = new StoryMemory();
        var reader = memory.StoryReader();
        memory.SetGameMoment(457);

        var deadEnds = new (int Field, int X, int Y)[]
        {
            (478, 583, 1000),   // jail3 gateway0, whose destination is jail4
            (479, 334, 1333),   // jail4 gateway1, whose destination is dyne
            (479, -474, -830)   // jail4 gateway0, whose destination is jail3
        };
        foreach (var (field, x, y) in deadEnds)
        {
            foreach (var target in Objectives(reader, field))
            {
                var line = target.TriggerLine;
                Equal(false, line is not null && line.Value.StartX == x && line.Value.StartY == y,
                    $"field {field} must not route through the disabled gateway at ({x},{y})");
            }
        }

        // What it does use instead, read from the side of the fence it is on.
        Equal(true, Objectives(reader, new FieldPositionSnapshot(FieldPositionReader.FieldModule, 478, 0, -240, -64, -7, 23, 0))
                .Any(target => target.TriggerLine is { StartX: 988, StartY: 588, EndX: 4515, EndY: 5071 }),
            "jail3's crossing is entity 8's jl3 line");
        Equal(true, Objectives(reader, 479).Any(target =>
                target.TriggerLine is { StartX: 318, StartY: 1306, EndX: 889, EndY: 1298 }),
            "jail4's approach to Dyne is entity 5's jl2 line");
    }

    /// <summary>
    /// A native Go handler wants the player inside the model's own collision radius of
    /// the line, and a line the field has switched off is not a way anywhere. A target
    /// that ignores either can stop the player short of a thin exit, or send them to
    /// one that has been disabled.
    /// </summary>
    private static void PrisonExitsCarryTheNativeLineContract()
    {
        var memory = new StoryMemory();
        var reader = memory.StoryReader();
        memory.SetGameMoment(446);

        Equal(true, Objectives(reader, 471).All(target => target.InteractionRadius == 39),
            "jail1's exit uses the player's own collision radius, not a fixed threshold");

        var withoutTheLine = memory.StoryReader(disabledLineEntityId: 15);
        Equal(false, withoutTheLine.ReadTargets(Position(471)).Any(target =>
                target.TriggerLine is { StartX: -96, StartY: -176 }),
            "a disabled jl4 is not offered at all");

        // The rest of the prison's scripted crossings carry the same contract.
        memory.SetGameMoment(457);
        foreach (var field in new[] { 471, 473, 478, 479 })
        {
            foreach (var target in Objectives(reader, field))
            {
                if (target.TriggerLine is null)
                {
                    continue;
                }

                Equal(true, target.InteractionRadius > 0,
                    $"field {field}'s scripted crossing must use the native radius");
            }
        }
    }

    /// <summary>
    /// The prison is not left automatically. crcin_2's director reaches 467 and then
    /// waits: Esther's own Talk is what runs the race, and only her Main's winning
    /// branch writes 469. A loss leaves the moment at 467 and the player free to try
    /// again, so the target has to survive that.
    /// </summary>
    private static void TheChocoboRaceIsAVisibleInteractionAndRetryable()
    {
        var memory = new StoryMemory();

        var present = memory.StoryReader(visibleEntityId: 9, entityX: 888, entityY: -80);
        memory.SetGameMoment(467);
        var race = present.ReadTargets(Position(512))
            .Where(target => target.TriggerEntityId == 9)
            .ToArray();
        Equal(1, race.Length, "Esther is the way out of the prison at 467");
        Equal(888, race[0].X, "and the target follows her live position");
        Equal(-80, race[0].Y, "in both axes");

        // A loss does not move the moment, so the same target has to still be there.
        Equal(1, present.ReadTargets(Position(512)).Count(target => target.TriggerEntityId == 9),
            "a lost race leaves the retry available");

        // While she is walking away during the scene there is nobody to talk to, and
        // nothing is invented in her place.
        var absent = memory.StoryReader();
        Equal(0, absent.ReadTargets(Position(512)).Count(target => target.TriggerEntityId == 9),
            "an Esther who is not standing there is not offered");

        // Winning writes 469, and the race must not be offered again after it.
        memory.SetGameMoment(469);
        Equal(0, present.ReadTargets(Position(512)).Count(target => target.TriggerEntityId == 9),
            "the finished chapter cannot replay the race");
    }

    /// <summary>
    /// The prison is entered once. Its rows must not follow the player into the rest
    /// of the game, and must not appear before the party is dropped into it.
    /// </summary>
    private static void PrisonObjectivesStayInsideTheirOwnChapter()
    {
        var memory = new StoryMemory();
        var reader = memory.StoryReader();
        var prisonFields = new[] { 471, 472, 473, 474, 475, 478, 479, 482 };

        foreach (var moment in new[] { 0, 440, 445, 466, 469, 586, 1596 })
        {
            memory.SetGameMoment(moment);
            foreach (var field in prisonFields)
            {
                Equal(0, Objectives(reader, field).Length,
                    $"prison field {field} must be silent at moment {moment}");
            }
        }
    }

    /// <summary>
    /// The Icicle Inn advances no GameMoment at all. Every step is ordered by one byte
    /// of local state, Bank 1[130], and the objective has to follow those bits rather
    /// than a position in a numeric sequence.
    /// </summary>
    private static void TheIcicleInnFollowsItsOwnLocalFlags()
    {
        var memory = new StoryMemory();
        memory.SetGameMoment(700);

        // Nothing has happened yet: the way on is up the north path.
        var fresh = memory.StoryReader();
        Equal(true, fresh.ReadTargets(Position(654)).Any(target =>
                target.TriggerLine is { StartX: -334, StartY: 3293 }),
            "before the confrontation the route is the line that starts it");
        Equal(false, fresh.ReadTargets(Position(654)).Any(target =>
                target.TriggerLine is { StartX: -78, StartY: 3517 }),
            "and the slope itself is not offered yet");

        // bit 0: the confrontation is done, so the board is what is needed.
        var afterConfrontation = memory.StoryReader(icicleFlags: 0x01);
        Equal(true, afterConfrontation.ReadTargets(Position(654)).Any(target =>
                target.TriggerLine is { StartX: 196, StartY: 139 }),
            "the house by the square holds the board");
        Equal(true, memory.StoryReader(visibleEntityId: 7, icicleFlags: 0x01).ReadTargets(Position(655))
                .Any(target => target.TriggerEntityId == 7),
            "and the boy is the one who gives permission");

        // bit 5: permission granted, but permission is not the board.
        var withPermission = memory.StoryReader(icicleFlags: 0x21);
        Equal(false, memory.StoryReader(visibleEntityId: 7, icicleFlags: 0x21).ReadTargets(Position(655))
                .Any(target => target.TriggerEntityId == 7),
            "the boy is done once he has said yes");
        Equal(true, memory.StoryReader(visibleEntityId: 11, icicleFlags: 0x21).ReadTargets(Position(655))
                .Any(target => target.TriggerEntityId == 11),
            "the board itself is a separate thing to pick up");

        // bit 1: the board is taken, so the map is next and the house has a way out.
        var withBoard = memory.StoryReader(icicleFlags: 0x23);
        Equal(true, withBoard.ReadTargets(Position(655)).Any(target =>
                target.TriggerLine is { StartX: -72, StartY: -41 }),
            "the house is never a dead end once the board has gone");
        Equal(true, withBoard.ReadTargets(Position(656)).Any(target =>
                target.TriggerLine is { StartX: -317, StartY: 173 }),
            "the map is read from its own line, not from the display model");
        Equal(false, withBoard.ReadTargets(Position(656)).Any(target => target.TriggerEntityId == 11),
            "the visible map model has no Talk script and must never be a target");
        Equal(false, withBoard.ReadTargets(Position(654)).Any(target =>
                target.TriggerLine is { StartX: -78, StartY: 3517 }),
            "and the slope still refuses without the map");

        // bit 6: with board and map the descent is the route.
        var ready = memory.StoryReader(icicleFlags: 0x63);
        Equal(true, ready.ReadTargets(Position(654)).Any(target =>
                target.TriggerLine is { StartX: -78, StartY: 3517, EndX: -16, EndY: 3430 }),
            "the slope opens once both prerequisites are held");

        // The town's own scripts switch to later-visit behaviour at 1008, so the
        // first-visit route must not follow the player back there.
        memory.SetGameMoment(1008);
        Equal(0, ready.ReadTargets(Position(654)).Count,
            "the first visit's route stops where the native scripts stop treating it as one");
    }

    /// <summary>
    /// Gaea's Cliff writes no GameMoment at all: from the Forgotten Capital's 677
    /// through Icicle Inn, the Great Glacier and the whole climb, nothing in the game
    /// writes another value until the Whirlwind Maze reaches 770. The chapter is
    /// ordered by two bits in Bank 1[131] and one in Bank 1[132], and the route has to
    /// follow those and not a position in a numeric sequence.
    /// </summary>
    private static void GaeasCliffIsOrderedByItsOwnCabinFlags()
    {
        var memory = new StoryMemory();
        memory.SetGameMoment(677);

        // Nothing done yet: the cabin at the foot of the cliff, and not the climb.
        var fresh = memory.StoryReader();
        Equal(true, fresh.ReadTargets(Position(686)).Any(target =>
                target.TriggerLine is { StartX: 264, StartY: -176 }),
            "the cabin door is the first step at the foot of the cliff");
        Equal(false, fresh.ReadTargets(Position(686)).Any(target =>
                target.TriggerLine is { StartX: -503, StartY: 3119 }),
            "and the climb is not offered before the party has been chosen");
        Equal(true, fresh.ReadTargets(Position(687)).Any(target =>
                target.TriggerLine is { StartX: 105, StartY: 370 }),
            "inside the cabin the way on is upstairs");

        // Bank 1[131] bit 6: holu_2's Director has run its scene and set it, so the
        // step is back down to the room below.
        memory.SetFieldByte(131, 0x40);
        var told = memory.StoryReader();
        Equal(true, told.ReadTargets(Position(688)).Any(target =>
                target.TriggerLine is { StartX: 89, StartY: -181 }),
            "with bit 6 set the upstairs room sends the party back down");
        Equal(false, told.ReadTargets(Position(687)).Any(target =>
                target.TriggerLine is { StartX: 105, StartY: 370 }),
            "and going back upstairs is no longer the objective");

        // Bit 7 as well: holu_1's own scene has run, so the way on is back outside.
        memory.SetFieldByte(131, 0xC0);
        var ready = memory.StoryReader();
        Equal(true, ready.ReadTargets(Position(687)).Any(target =>
                target.TriggerLine is { StartX: 87, StartY: -191 }),
            "with both cabin scenes done the party is sent outside again");
        Equal(false, ready.ReadTargets(Position(686)).Any(target =>
                target.TriggerLine is { StartX: -503, StartY: 3119 }),
            "the climb still waits on the party being chosen");

        // Bank 1[132] bit 0: gaiafoot's Director has set it after the party menu.
        memory.SetFieldByte(132, 0x01);
        var climbing = memory.StoryReader();
        Equal(true, climbing.ReadTargets(Position(686)).Any(target =>
                target.TriggerLine is { StartX: -503, StartY: 3119, EndX: 530, EndY: 2849 }),
            "the climb opens once the climbing party has been chosen");
        Equal(false, climbing.ReadTargets(Position(687)).Any(target =>
                target.TriggerLine is { StartX: 87, StartY: -191 }),
            "and the cabin has nothing left to send the party out for");

        // The two ice caves are one puzzle across five walkmesh components, and which
        // one the party is standing in decides the objective. Arriving from the climb
        // on triangle 153, the passage onward is not reachable and gateway0 is; only
        // after the boulder has been moved and the party has come back round to
        // triangle 238 does the passage become the step.
        var justArrived = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 690, 0, -664, -1008, -368, 153, 0);
        Equal(true, climbing.ReadTargets(justArrived).Any(target =>
                target.TriggerLine is { StartX: -292, StartY: 271 }),
            "the first level of the ice cave can only take its one door");
        Equal(false, climbing.ReadTargets(justArrived).Any(target =>
                target.TriggerLine is { StartX: 633, StartY: 380 }),
            "and must not be sent to a passage its component cannot reach");

        var boulderLevel = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 691, 0, -398, 510, -152, 85, 0);
        Equal(true, memory.StoryReader(visibleEntityId: 10).ReadTargets(boulderLevel)
                .Any(target => target.TriggerEntityId == 10),
            "the boulder is a required action on the level it is on");

        memory.SetFieldByte(131, 0xC2);
        var afterTheBoulder = memory.StoryReader();
        Equal(false, memory.StoryReader(visibleEntityId: 10).ReadTargets(boulderLevel)
                .Any(target => target.TriggerEntityId == 10),
            "and is not repeated once its bit is set");
        Equal(true, afterTheBoulder.ReadTargets(
                new FieldPositionSnapshot(FieldPositionReader.FieldModule, 690, 0, -273, 1119, -147, 238, 0))
            .Any(target => target.TriggerLine is { StartX: 633, StartY: 380, EndX: 761, EndY: 333 }),
            "the upper level is the one that finally reaches the way on");
        memory.SetFieldByte(131, 0xC0);

        // Holzoff's own script switches to later-visit dialogue at 1576, and the maze
        // takes over at 770. The first climb must not follow the party back.
        memory.SetGameMoment(790);
        Equal(0, memory.StoryReader().ReadTargets(Position(686)).Count,
            "the climb's route stops once the maze has started writing moments");
    }

    /// <summary>
    /// Rocket Town's first visit, which is not the chapter it looks like. There is a
    /// rocket with a cabin at the top, and the party does climb up to it, but the scene
    /// that moves the story on happens back down in the yard and everything past it is a
    /// flashback the game plays by itself. Two things are worth holding: the captain's
    /// conversation writes its moment before his question is even asked, so the moment
    /// cannot be what finishes it, and the top of the base ladder is a two-triangle ledge
    /// joined to nothing, so a row that pointed at the town from up there would be asking
    /// for a walk across empty space.
    ///
    /// <para>This case used to ask field 551 for the arrival step, which is where the
    /// catalog had put it and which is not the street the game loads: every Rocket Town
    /// side room's exit script is <c>IFSW Bank[2][0] &lt; 1308 -&gt; MAPJUMP 557</c>, else
    /// 551. The two fields share seven gateway lines, so asking the wrong one still
    /// produced the right answer and the town stayed silent in play for thirty-three
    /// minutes. <c>StoryProgressionGapTests</c> owns that ground now; what is left here
    /// is the rest of the chapter.</para>
    /// </summary>
    private static void RocketTownFirstVisitFollowsTheYardNotTheRocket()
    {
        var memory = new StoryMemory();
        memory.SetGameMoment(523);

        Equal(true, memory.StoryReader().ReadTargets(Position(557)).Any(target =>
                target.TriggerLine is { StartX: 292, StartY: 1373 }),
            "arriving in the town the step is the captain's yard");

        var gate = memory.StoryReader().ReadTargets(Position(558))
            .Where(target => target.TriggerLine is { StartX: -23, StartY: 898 })
            .ToArray();
        Equal(1, gate.Length, "and from the yard, the gate into the backyard");
        Equal(false, gate[0].CompletesOnArrival,
            "which is a confirm press rather than a doorway");

        // The captain. His Talk writes 538 at byte 72 and only one of the three answers
        // sets Bank[3][130] bit 6, which is what the yard scene tests for. A row that
        // named 538 as its target would retire itself on the write and strand anyone who
        // picked a different answer.
        memory.SetGameMoment(538);
        memory.SetLocalByte(130, 0x04);
        Equal(true, memory.StoryReader(visibleEntityId: 7).ReadTargets(Position(564))
                .Any(target => target.TriggerEntityId == 7),
            "after an answer that skipped the explanation the captain is still the step");
        Equal(false, memory.StoryReader().ReadTargets(Position(558)).Any(target =>
                target.TriggerLine is { StartX: -385, StartY: 258 } or { StartX: -58, StartY: 218 }),
            "and the yard scene is not offered, because its own lines ignore the player");

        memory.SetLocalByte(130, 0x44);
        Equal(false, memory.StoryReader(visibleEntityId: 7).ReadTargets(Position(564))
                .Any(target => target.TriggerEntityId == 7),
            "once the rocket has been explained he is done");
        Equal(true, memory.StoryReader().ReadTargets(Position(558)).Any(target =>
                target.TriggerLine is { StartX: -385, StartY: 258, EndX: -181, EndY: 262 } or
                                      { StartX: -58, StartY: 218, EndX: 58, EndY: 177 }),
            "and the crossing in the yard becomes the step");

        // The ledge at the top of the base ladder. Arriving there from the cabin the
        // field has already started a climb down and is waiting on the player; the town
        // is not walkable from those two triangles and must not be offered there.
        var ladderLedge = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 561, 0, -1090, 4941, 1493, 131, 0);
        var settledFoot = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 561, 0, -1210, 4982, 684, 98, 0);
        Equal(false, memory.StoryReader().ReadTargets(ladderLedge).Any(target =>
                target.TriggerLine is { StartX: -380, StartY: 2629 } or { StartX: -274, StartY: 4081 }),
            "the town is not offered from the ledge at the top of the base ladder");
        Equal(true, memory.StoryReader().ReadTargets(settledFoot).Any(target =>
                target.TriggerLine is { StartX: -380, StartY: 2629 } or { StartX: -274, StartY: 4081 }),
            "and is offered once the climb down has put the party on the ground");

        // After the flashback the yard writes 550 by itself, and the only thing left is
        // the way out to the town.
        memory.SetGameMoment(550);
        memory.SetLocalByte(131, 0x04);
        Equal(true, memory.StoryReader().ReadTargets(Position(558)).Any(target =>
                target.TriggerLine is { StartX: -239, StartY: -9, EndX: -160, EndY: -15 }),
            "after the flashback the yard's own exit is the step");

        // And the same gate as at the start of the chapter, which now opens somewhere
        // else because the moment has moved past 553.
        memory.SetGameMoment(557);
        memory.SetLocalByte(131, 0x0C);
        Equal(true, memory.StoryReader().ReadTargets(Position(558)).Any(target =>
                target.TriggerLine is { StartX: -23, StartY: 898, EndX: 60, EndY: 898 }),
            "the gate is the step again once Shera has asked");
        Equal(true, memory.StoryReader().ReadTargets(Position(774)).Any(target =>
                target.TriggerLine is { StartX: -170, StartY: 147, EndX: -591, EndY: 180 }),
            "and beyond it the approach is a walk-on, not a conversation");
    }


    /// <summary>
    /// Cosmo Canyon end to end. The chapter writes the GameMoment six times in total and
    /// never between the arrival at 469 and the observatory, so what orders it is local
    /// state and which half of the canyon floor the party is standing on. Three things
    /// are worth holding onto here: the town is two walkmesh components joined only
    /// through the inn, the party being whole is read from the party rather than from a
    /// flag the observatory sets later, and the way into the Cave of the Gi is a
    /// question asked at four openings that look alike.
    /// </summary>
    private static void CosmoCanyonIsOrderedByItsOwnLocalState()
    {
        var memory = new StoryMemory();
        memory.SetGameMoment(469);

        // Nothing spoken to yet: the guard, and only the guard.
        Equal(true, memory.StoryReader(visibleEntityId: 15).ReadTargets(Position(525))
                .Any(target => target.TriggerEntityId == 15),
            "the guard at the way in is the first thing in the canyon");
        Equal(false, memory.StoryReader(visibleEntityId: 9).ReadTargets(Position(525))
                .Any(target => target.TriggerEntityId == 9),
            "and Red XIII is not offered before him");

        // Guard done: Red XIII. Both are models, so neither depends on where the party
        // is standing.
        memory.SetLocalByte(161, 0x04);
        Equal(true, memory.StoryReader(visibleEntityId: 9).ReadTargets(Position(525))
                .Any(target => target.TriggerEntityId == 9),
            "with the guard spoken to Red XIII is the step");

        // Both done. Now the town divides. Arriving from the world map is triangle 58,
        // and the terrace the observatory path leaves from is a different component
        // reached only through the inn - so the same field, at the same moment, has two
        // different answers depending on which half the party is on.
        memory.SetLocalByte(161, 0x0C);
        var arrived = memory.StoryReader();
        var lowerTown = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 525, 0, -1330, -2325, -2821, 58, 0);
        var upperTown = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 525, 0, -1199, -444, -1805, 340, 0);
        Equal(true, arrived.ReadTargets(lowerTown).Any(target =>
                target.TriggerLine is { StartX: -1201, StartY: -893 }),
            "from the canyon floor the way up is through the inn");
        Equal(false, arrived.ReadTargets(lowerTown).Any(target =>
                target.TriggerLine is { StartX: -536, StartY: -468 }),
            "and never the terrace path, which that floor cannot reach");
        Equal(true, arrived.ReadTargets(upperTown).Any(target =>
                target.TriggerLine is { StartX: -536, StartY: -468, EndX: -452, EndY: -541 }),
            "from the terrace it is the observatory path");

        // The climb to the observatory is a confirm press on a LINE that runs the
        // leader's own climb script, not a doorway, so it must not report itself done
        // just because the party walked onto it.
        var ladder = arrived.ReadTargets(Position(531))
            .Where(target => target.TriggerLine is { StartX: -224, StartY: 164 })
            .ToArray();
        Equal(1, ladder.Length, "the observatory ladder is the step in the upper town");
        Equal(false, ladder[0].CompletesOnArrival,
            "and it waits for the press rather than completing on arrival");

        // The first observatory scene empties the party and opens the companion menus.
        // What says the party is whole again is the party: bank 3[10] holding someone.
        memory.SetLocalByte(161, 0x1C);
        memory.SetLocalByte(170, 0x01);
        memory.SetLocalByte(10, 0xFF);
        Equal(true, memory.StoryReader(visibleEntityId: 3).ReadTargets(Position(536))
                .Any(target => target.TriggerEntityId == 3),
            "with the menus open and a slot empty the companions are the step");

        memory.SetLocalByte(10, 0x02);
        Equal(false, memory.StoryReader(visibleEntityId: 3).ReadTargets(Position(536))
                .Any(target => target.TriggerEntityId == 3),
            "once the party is whole they stop being one");
        Equal(true, memory.StoryReader().ReadTargets(Position(536)).Any(target =>
                target.TriggerLine is { StartX: -281, StartY: 191, EndX: -238, EndY: 251 }),
            "and the room the player used has a way out that does not wait on a later flag");

        // The demonstration, then the fire.
        memory.SetLocalByte(170, 0x03);
        Equal(true, memory.StoryReader(visibleEntityId: 11).ReadTargets(Position(541))
                .Any(target => target.TriggerEntityId == 11),
            "with the party whole and confirmed Bugenhagen starts the demonstration");

        // The sealed door: Bugenhagen opens it and only then is there anything behind it.
        memory.SetGameMoment(502);
        memory.SetLocalByte(161, 0x7C);
        memory.SetLocalByte(170, 0xC3);
        memory.SetLocalByte(171, 0x0F);
        var sealedDoor = memory.StoryReader(visibleEntityId: 15);
        Equal(true, sealedDoor.ReadTargets(Position(531)).Any(target => target.TriggerEntityId == 15),
            "the sealed door needs Bugenhagen");
        Equal(false, sealedDoor.ReadTargets(Position(531)).Any(target =>
                target.TriggerLine is { StartX: -64, StartY: 707 }),
            "and the way behind it is not offered while it is still shut");

        memory.SetLocalByte(170, 0xC7);
        Equal(true, memory.StoryReader().ReadTargets(Position(531)).Any(target =>
                target.TriggerLine is { StartX: -64, StartY: 707, EndX: 51, EndY: 560 }),
            "once it is open that door is the step");

        // The Cave of the Gi. Four openings, all offered, none of them named as the one
        // that matters, and each dropping out once it has been looked into.
        var caveEntrance = memory.StoryReader();
        var openings = caveEntrance.ReadTargets(Position(546))
            .Where(target => target.CompletionTriangles is { Count: 1 })
            .ToArray();
        Equal(4, openings.Length, "all four openings are offered to look into");
        Equal(1, openings.Select(target => target.Label).Distinct().Count(),
            "and none of them is described differently from the others");
        Equal(false, caveEntrance.ReadTargets(Position(546)).Any(target =>
                target.TriggerLine is { StartX: 203, StartY: 1316 }),
            "the passage on is not there before one of them opens it");

        // One opening looked into and it was not the one that opens anything: three
        // left, and still no passage.
        memory.SetLocalByte(182, 0x20);
        var oneTried = memory.StoryReader();
        Equal(3, oneTried.ReadTargets(Position(546))
                .Count(target => target.CompletionTriangles is { Count: 1 }),
            "an opening already looked into is not offered again");
        Equal(false, oneTried.ReadTargets(Position(546)).Any(target =>
                target.TriggerLine is { StartX: 203, StartY: 1316 }),
            "and looking into the wrong one opens nothing");

        memory.SetLocalByte(182, 0x30);
        var caveOpened = memory.StoryReader();
        Equal(0, caveOpened.ReadTargets(Position(546))
                .Count(target => target.CompletionTriangles is { Count: 1 }),
            "once the way is open there is nothing left to look into");
        Equal(true, caveOpened.ReadTargets(Position(546)).Any(target =>
                target.TriggerLine is { StartX: 203, StartY: 1316, EndX: 269, EndY: 1304 }),
            "and the passage the cave opened is the way on");

        // The way out of the canyon, once the Gi have been dealt with.
        memory.SetGameMoment(514);
        Equal(true, memory.StoryReader().ReadTargets(Position(525)).Any(target =>
                target.TriggerLine is { StartX: -1404, StartY: -2137, EndX: -1542, EndY: -2225 }),
            "the last step in the canyon is the way out of it");
    }


    /// <summary>
    /// The Sleeping Forest lets nobody through without the harp: slfrst_2's Director
    /// writes its moment only while Bank 1[231] bit 3 is set. A row that ignored that
    /// would send a player to walk into a forest that turns them straight back, over
    /// and over, with the objective still saying go.
    /// </summary>
    private static void TheSleepingForestWaitsOnTheHarp()
    {
        var memory = new StoryMemory();
        memory.SetGameMoment(638);

        Equal(false, memory.StoryReader().ReadTargets(Position(618)).Any(target =>
                target.TriggerLine is { StartX: -25, StartY: 1756 }),
            "without the harp the way deeper in is not offered");

        memory.SetFieldByte(231, 0x08);
        Equal(true, memory.StoryReader().ReadTargets(Position(618)).Any(target =>
                target.TriggerLine is { StartX: -25, StartY: 1756, EndX: 183, EndY: 1756 }),
            "with the harp held it is");

        // The forest's own write has happened by 652, and the row is banded to stop
        // there rather than following the party into the next chapter.
        memory.SetGameMoment(652);
        Equal(false, memory.StoryReader().ReadTargets(Position(618)).Any(target =>
                target.TriggerLine is { StartX: -25, StartY: 1756 }),
            "and it stops once the forest has already been passed");
    }


    /// <summary>
    /// The doors generated from a room's own entry scripts say one thing and no more:
    /// at this exact GameMoment, walking through here advances the story, because the
    /// destination's own Init or Main tests for that value and nothing else. So the row
    /// has to appear at that value and be gone either side of it.
    /// </summary>
    private static void TheNativeEntryDoorsCarryTheirOwnGuardMoment()
    {
        var memory = new StoryMemory();

        // games/dic Main runs its scene under a single test for 440 and writes 442, and
        // chorace is one of the rooms with a door into it.
        memory.SetGameMoment(439);
        Equal(false, memory.StoryReader().ReadTargets(Position(509))
                .Any(target => target.TriggerLine is { StartX: 515, StartY: -2279 }),
            "the door is not the story a moment before the script tests for it");

        memory.SetGameMoment(440);
        Equal(true, memory.StoryReader().ReadTargets(Position(509))
                .Any(target => target.TriggerLine is { StartX: 515, StartY: -2279 }),
            "at the value the destination's own script tests for, the door is the step");

        memory.SetGameMoment(441);
        Equal(false, memory.StoryReader().ReadTargets(Position(509))
                .Any(target => target.TriggerLine is { StartX: 515, StartY: -2279 }),
            "and it is gone again the moment the test would fail");
    }


    /// <summary>
    /// Chapter titles from the field metadata are written for someone who has already
    /// finished the game. Speaking one as an objective both spoils a scene and fails to
    /// say what to do now.
    /// </summary>
    private static void StoryLabelsDoNotNameChaptersOrRevealScenes()
    {
        var definitions = FieldStoryEventCatalog.CreateAllFields();
        var spoilers = new[]
        {
            "Bloodbath", "Sephiroth goes ballistic", "Tifa and Cloud's Mind",
            "Date Scene", "Cat and Keystone", "Diamond Weapon", "Huge Materia"
        };

        foreach (var definition in definitions)
        {
            foreach (var spoiler in spoilers)
            {
                Equal(false, definition.Label.Contains(spoiler, StringComparison.OrdinalIgnoreCase),
                    $"field {definition.FieldId} labels an objective \"{definition.Label}\"");
            }
        }
    }

    /// <summary>
    /// Every row has to describe a real place. A target with no position and no line
    /// cannot be walked to, and a model row with no entity cannot be approached.
    /// </summary>
    private static void EveryStoryRowNamesARealFieldAndPlace()
    {
        var definitions = FieldStoryEventCatalog.CreateAllFields();
        Equal(true, definitions.Count > 0, "the catalog must not be empty");

        foreach (var definition in definitions)
        {
            Equal(true, definition.FieldId > 0,
                $"\"{definition.Label}\" must name a real field");
            Equal(true, !string.IsNullOrWhiteSpace(definition.Label),
                $"field {definition.FieldId} has a row with no label");

            if (definition.Kind == FieldStoryTargetKind.Model)
            {
                Equal(true, definition.EntityId >= 0,
                    $"\"{definition.Label}\" is a model row and must name its entity");
                continue;
            }

            var hasPlace = definition.X != 0 || definition.Y != 0 || definition.Z != 0 ||
                           definition.TriggerLine is not null;
            Equal(true, hasPlace,
                $"\"{definition.Label}\" in field {definition.FieldId} must have somewhere to go");

            // A row that wants the native collision radius has to have a line for the
            // radius to be measured against.
            if (definition.UsesPlayerCollisionRadius)
            {
                Equal(true, definition.TriggerLine is not null,
                    $"\"{definition.Label}\" uses the native radius and needs its line");
            }
        }

        foreach (var definition in definitions)
        {
            if (definition.MinimumGameMoment >= 0 && definition.MaximumGameMoment >= 0)
            {
                Equal(true, definition.MaximumGameMoment >= definition.MinimumGameMoment,
                    $"\"{definition.Label}\" in field {definition.FieldId} has an inverted moment band");
            }
        }
    }

    private static string Objective(FieldStoryTargetReader reader, int field) =>
        Objective(reader, Position(field));

    private static string Objective(FieldStoryTargetReader reader, FieldPositionSnapshot position)
    {
        var targets = Objectives(reader, position);
        if (targets.Length == 0)
        {
            throw new InvalidOperationException(
                $"Main story coverage - field {position.FieldId} offers no next step at all.");
        }

        return targets[0].Label;
    }

    /// <summary>
    /// Narrowing to the nearest milestone is there so two chapters are not offered at
    /// once. A row that declares no milestone completes nothing, so it cannot be the wrong
    /// chapter, and hiding it behind the door that does complete one is how a room goes
    /// silent in the middle of a crossing.
    /// </summary>
    private static void AStepThatCompletesNothingIsNotHiddenByTheDoorThatDoes()
    {
        var door = new FieldStoryEventDefinition(130, FieldStoryTargetKind.Location,
            "the door that ends the room", TargetGameMoment: 123,
            MinimumGameMoment: 117, MaximumGameMoment: 122);
        var later = new FieldStoryEventDefinition(130, FieldStoryTargetKind.Location,
            "a door belonging to a later moment", TargetGameMoment: 128,
            MinimumGameMoment: 117, MaximumGameMoment: 122);
        var step = new FieldStoryEventDefinition(130, FieldStoryTargetKind.Location,
            "a jump that completes nothing", MinimumGameMoment: 117, MaximumGameMoment: 122);

        var memory = new StoryMemory();
        memory.SetGameMoment(120);
        var reader = new FieldStoryTargetReader(
            _ => 0, _ => 0, memory.ReadStateByte, [door, later, step]);

        var offered = reader.ReadTargets(Position(130)).Select(target => target.Label).ToArray();
        Equal(true, offered.Contains("a jump that completes nothing"),
            "a step with no milestone of its own must survive alongside the door that has one");
        Equal(true, offered.Contains("the door that ends the room"),
            "the nearest milestone is still offered");
        Equal(false, offered.Contains("a door belonging to a later moment"),
            "and a further milestone is still narrowed away, which is what the rule is for");
    }

    private static void TheReactorPipingOffersTheJumpsBetweenItsDoors()
    {
        var memory = new StoryMemory();
        var reader = memory.StoryReader();

        // Reactor 5's upper piping is crossed by three jump lines and its only rows used to
        // be the doors at either end, both of which carry a moment. In the middle of the
        // pipe neither door is reachable, so the room said nothing where it mattered.
        foreach (var moment in new[] { 120, 127 })
        {
            memory.SetGameMoment(moment);
            var offered = Objectives(reader, 130).Select(target => target.Label).ToArray();
            Equal(true, offered.Contains("Jump the gap in the piping"),
                $"the gap crossing has to be offered at {moment}, between the two doors");
            Equal(true, offered.Contains("Jump back over the gap in the piping"),
                "and the same gap taken the other way");
            Equal(true, offered.Contains(
                "Stand at the pipe edge and press Confirm to drop to the lower pipe"),
                "and the drop, which is a Go the player has to ask for");
        }

        // The lines are the field's own, so a field that has switched one off must not
        // offer it: an unusable trigger is not a way anywhere.
        memory.SetGameMoment(120);
        var withoutTheGap = memory.StoryReader(disabledLineEntityId: 2);
        Equal(false,
            Objectives(withoutTheGap, 130).Select(target => target.Label)
                .Contains("Jump the gap in the piping"),
            "a disabled piping line is not offered");
    }

    private static void TheCraterLatticeOffersOnlyWaysDown()
    {
        var memory = new StoryMemory();
        memory.SetGameMoment(1700);
        var reader = memory.StoryReader();

        var offered = Objectives(reader, 749).Select(target => target.Label).ToArray();
        Equal(true, offered.Length >= 16,
            "the crater lattice is sixteen descents, not the one row it used to carry");
        Equal(true, offered.Contains("Jump down to the crater floor"),
            "m_jump is the only line in that field that leaves it, and it has to be offered");
        Equal(true, offered.All(label =>
                label.StartsWith("Jump down", StringComparison.Ordinal) ||
                label.StartsWith("Drop down", StringComparison.Ordinal)),
            "nothing offered there may be one of the hops that climbs back up a ledge");

        // PC las0_6 includes down_8_d/e and down_9_d/e; m_jump is entity 40.
        var withoutTheExit = memory.StoryReader(disabledLineEntityId: 40);
        Equal(false,
            Objectives(withoutTheExit, 749).Select(target => target.Label)
                .Contains("Jump down to the crater floor"),
            "and a disabled exit line is silent rather than offered");
    }

    private static void TheCraterSideCaveAnswersForTheSideThePlayerIsStandingOn()
    {
        var memory = new StoryMemory();
        memory.SetGameMoment(1700);
        var reader = memory.StoryReader();

        // The cave's walkmesh is three separate pieces and its five doors are spread over
        // them. A door on another piece cannot be walked to at all, so each arrival must
        // answer with the door on its own side and no other.
        foreach (var (triangle, expected) in new[]
        {
            (130, "Follow the passage down and out to the next ledge"),
            (166, "Follow the passage down and out to the next ledge"),
            (117, "Take the passage back out to the ledge"),
            (92, "Follow the passage down and out to the next ledge"),
            (195, "Follow the passage down and out to the next ledge"),
        })
        {
            var arrival = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, 750, 0, 0, 0, 0, (ushort)triangle, 0);
            var offered = Objectives(reader, arrival);
            Equal(1, offered.Length,
                $"triangle {triangle} must be answered by exactly the door on its own side");
            Equal(expected, offered[0].Label, $"and it is the wrong door for triangle {triangle}");
        }
    }

    /// <summary>
    /// Cid takes the Highwind. Three rooms, walked out and back, none of which the catalog
    /// had anything to say about.
    ///
    /// <para>The whole sequence is keyed on bank 3[9], the current leader, because that is
    /// the byte the scripts branch on themselves: field 73's crew Talk goes to the Tifa
    /// branch at byte 4 and to Cid's at byte 83, and the two want different moments. Until
    /// the party has been chosen the pilot only congratulates him, so the first step is out
    /// of the cockpit rather than at the pilot standing right there.</para>
    /// </summary>
    private static void CidsHighwindHandoffIsWalkedRoomByRoom()
    {
        var memory = new StoryMemory();
        memory.SetLocalByte(9, 8);

        foreach (var moment in new[] { 1108, 1109 })
        {
            memory.SetGameMoment(moment);

            var inTheRooms = memory.StoryReader();
            Equal("Go back out of the cockpit", Objective(inTheRooms, 70),
                $"at {moment} the pilot has nothing yet, so the step is the way out");
            Equal("Go through to the operations room", Objective(inTheRooms, 74),
                "and the corridor leads to the room the party is chosen in");
            Equal(0, Objectives(inTheRooms, 73).Length,
                "a crew who is not being drawn is not something to walk to");

            var withTheCrew = memory.StoryReader(visibleEntityId: 12);
            Equal("Talk to the crew to choose the party", Objective(withTheCrew, 73),
                "the crew is what writes 1110 and opens the party menu");
        }

        // The menu hands control back inside the operations room, and the way on is the way
        // they came. It stays the way on: the takeoff maps to the world map without moving
        // the moment again, so 1116's second choice is reached through the same rooms.
        foreach (var moment in new[] { 1110, 1116 })
        {
            memory.SetGameMoment(moment);

            var goingBack = memory.StoryReader();
            Equal("Go back to the corridor", Objective(goingBack, 73),
                $"at {moment} the party is formed and the pilot is the next thing");
            Equal("Go forward to the cockpit", Objective(goingBack, 74),
                "74's own line maps 70 for the whole of this stretch");

            var withThePilot = memory.StoryReader(visibleEntityId: 17);
            Equal("Talk to the pilot to take off", Objective(withThePilot, 70),
                "and the pilot now asks rather than congratulates");
        }

        // The leader byte is the whole gate. With Tifa leading, this later moment has no
        // Cid handoff in it at all - her own rows belong to 1031 and 1033.
        memory.SetGameMoment(1108);
        memory.SetLocalByte(9, 2);
        var wrongLeader = memory.StoryReader(visibleEntityId: 12);
        Equal(0, Objectives(wrongLeader, 73).Length,
            "the handoff must not be offered to a leader whose branch the script does not take");
    }

    /// <summary>
    /// Speed Square's Shooting Coaster. The row here used to name jetin1 entity 10 and
    /// call it "Board the Shooting Coaster"; entity 10 is the LINE whose Go 1x runs the
    /// attendant's script 3, which turns him round and says "This way, Sir, to register."
    /// It is the barrier that sends the player away, so the guidance walked them into it
    /// and the recorded session shows them hitting it twice.
    ///
    /// <para>The ride is two conversations. The guide's Talk sets 3[72] bit 4 itself;
    /// until it is set the attendant only answers "Is this your first time?". With it set
    /// he asks the question, takes ten GP, unlocks the five boundary triangles and starts
    /// the ride. Bit 5 is the ride in progress.</para>
    /// </summary>
    private static void TheShootingCoasterSendsThePlayerToRegisterRatherThanAtTheBarrier()
    {
        var memory = new StoryMemory();
        memory.SetGameMoment(442);

        Equal(false,
            FieldStoryEventCatalog.CreateAllFields()
                .Any(row => row.FieldId == 487 && row.EntityId == 10),
            "nothing may target the barrier line that tells the player to go and register");

        memory.SetLocalByte(72, 0);
        var atTheGuide = memory.StoryReader(visibleEntityId: 12);
        Equal("Ask the Shooting Coaster guide about the ride (optional)",
            atTheGuide.ReadTargets(Position(487)).Single().Label,
            "before her flag is set the guide is the only one who can move this on");

        var atTheAttendant = memory.StoryReader(visibleEntityId: 11);
        Equal(0, atTheAttendant.ReadTargets(Position(487)).Count,
            "the attendant only says 'is this your first time' until the guide has explained it");

        memory.SetLocalByte(72, 0x10);
        var registering = memory.StoryReader(visibleEntityId: 11);
        Equal("Talk to the Shooting Coaster attendant to register (optional)",
            registering.ReadTargets(Position(487)).Single().Label,
            "with her flag set the attendant is the one who registers the player");

        // Paying is his question and the player's answer. Nothing here points at either.
        memory.SetLocalByte(72, 0x30);
        foreach (var entity in new[] { 11, 12 })
        {
            Equal(0, memory.StoryReader(visibleEntityId: entity).ReadTargets(Position(487)).Count,
                $"entity {entity} has nothing to offer once the ride itself is running");
        }
    }

    private static FieldNavigationTarget[] Objectives(FieldStoryTargetReader reader, int field) =>
        Objectives(reader, Position(field));

    private static FieldNavigationTarget[] Objectives(FieldStoryTargetReader reader, FieldPositionSnapshot position) =>
        reader.ReadTargets(position)
            .Where(target => !target.Label.EndsWith("(optional)", StringComparison.Ordinal))
            .ToArray();

    private static FieldPositionSnapshot Position(int field) =>
        new(FieldPositionReader.FieldModule, field, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// The native state the Story reader consults: the module, the field, the
    /// GameMoment, one byte of Icicle Inn local flags, and a live model table holding
    /// at most one visible entity.
    /// </summary>
    private sealed class StoryMemory
    {
        private const int EventTable = 0x100000;
        private readonly Dictionary<int, byte> bytes = [];
        private int visibleEntityId = -1;
        private int entityX;
        private int entityY;
        private byte icicleFlags;

        public StoryMemory()
        {
            bytes[FieldPositionReader.AddressCurrentModule] = 1;
            bytes[FieldPositionReader.AddressFieldNumModels] = 2;
            SetGameMoment(446);
        }

        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        // Banks 1 and 2 are the same 256 bytes read two ways, so a bank 1 byte is just
        // an offset from the same base the GameMoment word sits at.
        public void SetFieldByte(int address, byte value)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + address] = value;
        }

        // Bank 3 is the field's own saved state, a further 256 bytes along.
        public void SetLocalByte(int address, byte value)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 256 + address] = value;
        }

        public FieldStoryTargetReader StoryReader(
            int? visibleEntityId = null,
            int entityX = 0,
            int entityY = 0,
            byte icicleFlags = 0,
            int? disabledLineEntityId = null)
        {
            this.visibleEntityId = visibleEntityId ?? -1;
            this.entityX = entityX;
            this.entityY = entityY;
            this.icicleFlags = icicleFlags;
            return new FieldStoryTargetReader(
                ReadInt32,
                ReadInt16,
                ReadByte,
                FieldStoryEventCatalog.CreateAllFields(),
                entity => entity != disabledLineEntityId);
        }

        // The same native byte read the reader uses, for a test that builds its own
        // definitions rather than reading the shipped catalog.
        public byte ReadStateByte(int address) => ReadByte(address);

        private byte ReadByte(int address)
        {
            if (address == FieldNavigationObjectReader.AddressFieldBankBase + 130)
            {
                return icicleFlags;
            }

            if (address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                address < FieldNavigationObjectReader.AddressFieldModelIdArray + 255)
            {
                var entity = address - FieldNavigationObjectReader.AddressFieldModelIdArray;
                return entity == visibleEntityId ? (byte)1 : (byte)255;
            }

            if (address == EventTable + FieldNavigationObjectReader.FieldEventDataStride +
                FieldNavigationObjectReader.VisibilityOffset)
            {
                return visibleEntityId < 0 ? (byte)0 : (byte)1;
            }

            return bytes.GetValueOrDefault(address);
        }

        private int ReadInt32(int address)
        {
            if (address == FieldNavigationObjectReader.AddressFieldEventDataPtr)
            {
                return EventTable;
            }

            var model = EventTable + FieldNavigationObjectReader.FieldEventDataStride;
            if (address == model + FieldNavigationObjectReader.PositionXOffset)
            {
                return entityX * FieldNavigationObjectReader.ModelPositionFixedPointScale;
            }

            if (address == model + FieldNavigationObjectReader.PositionYOffset)
            {
                return entityY * FieldNavigationObjectReader.ModelPositionFixedPointScale;
            }

            if (address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr)
            {
                return 0x03200000;
            }

            return 0;
        }

        // The player's own collision radius, and the talk radius of the one visible
        // model, both as the native event table carries them.
        private short ReadInt16(int address) =>
            address == EventTable + 0x72 ||
            address == EventTable + FieldNavigationObjectReader.FieldEventDataStride + 0x74
                ? (short)40
                : (short)0;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Main story coverage - {label}: expected {expected}, got {actual}.");
        }
    }
}










