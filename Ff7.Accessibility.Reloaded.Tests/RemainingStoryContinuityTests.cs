using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The chapters between Rocket Town and the ending, checked against what the installed
/// field scripts actually do rather than against how many rows the catalog holds.
///
/// <para>Three kinds of case live here. The first is coverage: a room the story stops in
/// with nothing to walk to is the failure the report describes, and the Gold Saucer's
/// second visit was exactly that - the Terminal Floor has one gateway and it goes back to
/// the Ropeway Station, so every Square is reached by walking onto a pair of walkmesh
/// triangles that <c>chekun</c> polls, and no row said so at any moment after the first
/// visit. The second is the opposite: a row offered where the field runs itself is a
/// player sent to press a button the game has already pressed, and several rows had been
/// missing lower bounds and could be offered before their intended chapter.
/// The third is what a row says - a label taken from the first line of dialogue
/// names the speaker, and the speaker is usually the character the player is already
/// controlling.</para>
///
/// <para>Nothing here claims a chapter is completable. Each case names the native script
/// it is derived from, and the installed-data cases read those scripts back through the
/// production decoder and assert the exact bytes at the exact offsets.</para>
/// </summary>
internal static class RemainingStoryContinuityTests
{
    // Gold Saucer, second visit.
    private const int NorthCorel = 450;
    private const int RopewayStation = 457;
    private const int GoldSaucerStation = 496;
    private const int TerminalFloor = 497;
    private const int BattleSquare = 499;
    private const int ArenaLobby = 500;
    private const int Arena = 502;
    private const int DiosMuseum = 503;
    private const int RoundSquare = 488;

    // Temple of the Ancients, Mideel's Lifestream, Rocket Town, the Highwind.
    private const int TempleLastRoom = 616;
    private const int NibelheimGateIllusion = 280;
    private const int RocketTownStreet = 557;
    private const int HighwindCorridor = 74;
    private const int HighwindDeck = 72;

    /// <summary>The two pads cloud/Script 4 turns into the Battle Square.</summary>
    private static readonly int[] BattleSquarePads = [26, 27];

    /// <summary>And the two it turns into the Round Square.</summary>
    private static readonly int[] RoundSquarePads = [36, 37];

    private static readonly string[] PartyMemberNames =
    [
        "Cloud", "Barret", "Tifa", "Aeris", "Red XIII", "Yuffie", "Cait Sith", "Vincent", "Cid",
    ];

    /// <summary>Everything that needs no installed archive.</summary>
    public static void Run()
    {
        TheKeystoneVisitHasAWayFromTheWorldMapToTheMuseum();
        TheTerminalFloorOffersOneObjectiveAtEachOfItsChapters();
        TheKeystoneIsCollectedByWalkingBackIntoTheMuseum();
        TheEveningOffersTheRoundSquare();
        TheTemplesLastRoomPointsAtTheDoorRatherThanAtACompanion();
        TheLifestreamGateAsksForTheCharacterTheOtherSideOfIt();
        TheHighwindIsOfferedOnlyWhereTheDeckWaits();
        RocketTownIsNotPaddedWhereSheraWalksThePartyBack();
        TheWhirlwindMazeStopsWhereItsOwnScriptsDo();
        NoRowInTheseRoomsIsLeftWithoutAWindow();
    }

    /// <summary>The cases that read the installed field scripts.</summary>
    public static void RunWithInstalledGameData()
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            Console.WriteLine(
                "remaining story continuity: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        TheSquaresAreReachedByTrianglesAndNothingElse();
        TheMuseumHandsTheKeystoneOverOnEntry();
        TheTemplesLastDoorTestsItsOwnMoment();
        TheIllusionGateModelIsCloud();
        TheDeckStartsCaitSithsConversationItself();
        TheDeckLeavesTheTakeoffToThePlayer();
        TheRoundSquareRunsTheRideItself();
        RocketTownWalksThePartyBackItself();
        TheHighwindsLastCorridorRunsItself();
    }

    // ------------------------------------------------------------------ coverage ----

    /// <summary>
    /// Everything between the world map and the room the Keystone is in. The party lands
    /// with the Tiny Bronco, walks North Corel to the ropeway, rides it to the Ropeway
    /// Station, buys a ticket if they have not got one, enters the Terminal Floor, takes
    /// the Battle Square platform, crosses the Square into the Arena Lobby and goes
    /// through to the Museum. Before this pass the only rows in that whole chain were the
    /// last two.
    /// </summary>
    private static void TheKeystoneVisitHasAWayFromTheWorldMapToTheMuseum()
    {
        foreach (var field in new[]
                 {
                     NorthCorel, RopewayStation, GoldSaucerStation, TerminalFloor,
                     BattleSquare, ArenaLobby, DiosMuseum,
                 })
        {
            Equal(true, RowsAt(field, 570).Count > 0,
                $"field {field} must offer something on the way to the Keystone at moment 570");
        }

        var platform = RowsAt(TerminalFloor, 570).Single();
        Equal(
            string.Join(',', BattleSquarePads),
            string.Join(',', platform.CompletionPlayerTriangles ?? []),
            "the Terminal Floor's Keystone-visit row must be the Battle Square pads");
        Equal("chekun", platform.SourceEntityName,
            "and it must be the entity that polls the leader's triangle");
    }

    /// <summary>
    /// The reader only ever offers the lowest priority band present in a room, so a row
    /// that is meant to be an alternative has to share the band and a row that is meant
    /// to be the objective has to be alone in it. Checked through the shipping reader
    /// rather than the catalog, because that is where the band rule lives.
    /// </summary>
    private static void TheTerminalFloorOffersOneObjectiveAtEachOfItsChapters()
    {
        var keystone = Offered(TerminalFloor, 570);
        Equal(1, keystone.Count, "the Terminal Floor must offer exactly one thing at 570");
        Equal("Take the Battle Square platform", keystone[0].Label,
            "and that one thing must be the Square the Keystone is in");

        var leaving = Offered(TerminalFloor, 584);
        Equal(1, leaving.Count, "and exactly one thing once the Keystone is held");
        Equal("Take the way back to the Ropeway Station", leaving[0].Label,
            "which is the floor's only gateway");
    }

    /// <summary>
    /// dio's Talk map jumps the party into the Arena and every branch of his Script 4
    /// writes 583 the moment they walk back into the Museum, so the step is the walk back.
    /// The arena battles change which extra prizes come with the Keystone and nothing
    /// else, and no row here makes them a prerequisite.
    /// </summary>
    private static void TheKeystoneIsCollectedByWalkingBackIntoTheMuseum()
    {
        var fromArena = Offered(Arena, 581);
        Equal(1, fromArena.Count, "the Arena must offer exactly one way on at 581");
        Equal("Go back to the Arena Lobby", fromArena[0].Label, "and it is the lobby door");

        var fromLobby = Offered(ArenaLobby, 581);
        Equal(1, fromLobby.Count, "the Arena Lobby must offer exactly one way on at 581");
        Equal("Go back through to the showroom", fromLobby[0].Label, "and it is the Museum door");

        foreach (var row in RowsAt(ArenaLobby, 581).Concat(RowsAt(Arena, 581)))
        {
            Equal(false, row.Label.Contains("fight", StringComparison.OrdinalIgnoreCase),
                $"no row in the Battle Square may ask for a battle: '{row.Label}'");
        }
    }

    /// <summary>
    /// Story selects the required ride; all exploration platforms are in Exits.
    /// </summary>
    private static void TheEveningOffersTheRoundSquare()
    {
        var evening = Offered(TerminalFloor, 593);
        Equal(1, evening.Count, "the evening Story objective must be the Round Square");
        Equal("Take the Round Square platform", evening[0].Label, "the required ride remains selected");

        var roundSquare = RowsAt(TerminalFloor, 593)
            .Single(row => row.Label == "Take the Round Square platform");
        Equal(
            string.Join(',', RoundSquarePads),
            string.Join(',', roundSquare.CompletionPlayerTriangles ?? []),
            "the Round Square row must be the pads cloud/Script 4 maps to field 488");

        // And nothing at the Round Square itself. Its own Director calls the attendant's
        // Script 3 on arrival at 592, and that script hands over the tickets and map jumps
        // into the Ferris wheel; the attendant's Talk at that moment is the ordinary ride.
        Equal(0, RowsAt(RoundSquare, 593).Count,
            "the Round Square runs the ride itself and must offer nothing");
    }

    /// <summary>
    /// 616's entity 16 is a field model with its own talk radius standing at the end of
    /// the room, and its Talk is the whole of what the chapter has left. The label had
    /// been taken from the first line of dialogue, which names a party member, so the
    /// catalog was telling the player to walk to somebody who is walking with them.
    /// </summary>
    private static void TheTemplesLastRoomPointsAtTheDoorRatherThanAtACompanion()
    {
        var rows = RowsAt(TempleLastRoom, 627);
        Equal(1, rows.Count, "the temple's last room offers exactly one thing at 627");
        var row = rows[0];
        Equal(FieldStoryTargetKind.Model, row.Kind, "which is the model at the end of the room");
        Equal(16, row.EntityId, "entity 16");
        Equal("boss", row.SourceEntityName, "the entity the native script calls boss");
        AssertNamesNoPartyMember(row.Label, "the temple's last room");

        // Its Talk tests equality, so the same visible model must stop being a Story action
        // the moment the counter moves off 627.
        Equal(627, row.MinimumGameMoment, "bounded to the moment its own Talk tests for");
        Equal(627, row.MaximumGameMoment, "at both ends");
        Equal(0, RowsAt(TempleLastRoom, 628).Count, "and nothing at 628");
        Equal(0, RowsAt(TempleLastRoom, 629).Count, "nor at 629");
    }

    /// <summary>
    /// The Lifestream's Nibelheim gate. The player is Tifa here and the model to walk to
    /// is Cloud, so a label taken from the dialogue named the wrong one of the two. The
    /// row also had no window at all, and the room is only waiting for anything at 1124:
    /// from 1126 up its own bunki Main runs the scene and leaves by itself.
    /// </summary>
    private static void TheLifestreamGateAsksForTheCharacterTheOtherSideOfIt()
    {
        var rows = AllRows(NibelheimGateIllusion);
        Equal(1, rows.Count, "the illusion gate holds exactly one row");
        var row = rows[0];
        Equal("Talk to Cloud", row.Label, "and it names the model, not the player");
        Equal(1124, row.MinimumGameMoment, "bounded to the one moment the room waits");
        Equal(1125, row.MaximumGameMoment, "and to the moment before its own script takes over");
        Equal(3, row.EntityId, "on the entity the field marks as Cloud");
    }

    /// <summary>
    /// The deck is walked to twice. At 1566 the party has to reach it and the arrival
    /// scene starts Cait Sith's conversation itself, so the corridor is offered and the
    /// conversation is not. At 1580 the party is already standing there and the pilot is
    /// the step, so both are offered. At 1612 the corridor runs itself from end to end and
    /// nothing is offered at all.
    /// </summary>
    private static void TheHighwindIsOfferedOnlyWhereTheDeckWaits()
    {
        Equal(1, RowsAt(HighwindCorridor, 1566).Count,
            "the Highwind corridor must offer the way to the deck at 1566");
        Equal("Go forward to the deck", RowsAt(HighwindCorridor, 1566)[0].Label,
            "and say so");
        Equal(0, RowsAt(HighwindDeck, 1566).Count,
            "nothing on the deck is walked to at 1566: the arrival scene runs Cait Sith's Talk itself");

        var pilot = RowsAt(HighwindDeck, 1580).Single();
        Equal("Talk to the pilot to take off", pilot.Label, "the pilot is the step at 1580");
        Equal(16, pilot.EntityId, "on fship_25's entity 16");
        Equal(1, RowsAt(HighwindCorridor, 1580).Count,
            "and the corridor still carries the way back to him");

        foreach (var moment in new[] { 1612, 1614, 1618 })
        {
            Equal(0, RowsAt(HighwindCorridor, moment).Count,
                $"the corridor runs itself at {moment} and must offer nothing");
        }
    }

    /// <summary>
    /// Rocket Town's street at 553 looks like a hole and is not one. Cid's own Main writes
    /// 553 on entry and ends by running Shera's Script 3, which map jumps the party back
    /// into the house. A row there would be an instruction to walk somewhere the game is
    /// already carrying them.
    /// </summary>
    private static void RocketTownIsNotPaddedWhereSheraWalksThePartyBack()
    {
        Equal(0, RowsAt(RocketTownStreet, 553).Count,
            "the Rocket Town street must stay silent at 553");
    }

    /// <summary>
    /// The Whirlwind Maze ends at the illusion. nivl_b22's cefirth Script 9 writes 770 at
    /// the end of the confrontation and cloud's Script 19 map jumps straight to trnad_51,
    /// whose Init turns every gateway off and whose Main plays the rest to 999. So the
    /// maze's own rows stop at 769 and nothing between 770 and 998 is walked.
    /// </summary>
    private static void TheWhirlwindMazeStopsWhereItsOwnScriptsDo()
    {
        foreach (var field in new[] { 700, 701, 702, 703, 704, 705, 706, 709, 710, 711 })
        {
            Equal(0, RowsAt(field, 800).Count,
                $"field {field} must offer nothing at 800: the maze runs itself from 770");
        }
    }

    /// <summary>
    /// A target milestone supplies an upper completion bound even without an explicit
    /// window. Reviewed rows also need a lower bound matching their native chapter.
    /// </summary>
    private static void NoRowInTheseRoomsIsLeftWithoutAWindow()
    {
        foreach (var field in new[]
                 {
                     HighwindCorridor, NibelheimGateIllusion, TerminalFloor,
                     606, 612, TempleLastRoom,
                 })
        {
            foreach (var row in AllRows(field))
            {
                Equal(true, row.MinimumGameMoment >= 0 && row.MaximumGameMoment >= 0,
                    $"field {field} row '{row.Label}' must carry a GameMoment window");
            }
        }
    }

    // -------------------------------------------------------------------- native ----

    /// <summary>
    /// Why the platform rows have to exist. The Terminal Floor's triggers section holds
    /// exactly one gateway and it leads back to the Ropeway Station; everything else is
    /// cloud's Script 4, which reads the leader's walkmesh triangle out of bank 6[7] and
    /// map jumps on nothing else. The two branches the rows above stand on are asserted
    /// by their bytes.
    /// </summary>
    private static void TheSquaresAreReachedByTrianglesAndNothingElse()
    {
        var gateways = Gateways(TerminalFloor);
        Equal(1, gateways.Count, "the Terminal Floor must have exactly one gateway");
        Equal(GoldSaucerStation, gateways[0], "and it must lead back to the Ropeway Station");

        AssertOpcode(TerminalFloor, 12, 17, [0x16, 0x60, 0x07, 0x00, 0x18, 0x00, 0x04, 0x13],
            "chekun polls the leader's triangle and acts from 24 up");
        AssertOpcode(TerminalFloor, 1, 238, [0x60, 0xF3, 0x01, 0x14, 0xFF, 0x55, 0xF7, 0x6F, 0x00, 0x30],
            "triangle 26 and up is the Battle Square, field 499");
        AssertOpcode(TerminalFloor, 1, 38, [0x60, 0xE8, 0x01, 0xDE, 0x00, 0xDF, 0xFF, 0x06, 0x00, 0xC0],
            "triangle 36 and up is the Round Square, field 488");
    }

    /// <summary>
    /// And why walking back in is the step. dio's Script 4 branches three ways on the
    /// battle counter, and all three converge on the same write.
    /// </summary>
    private static void TheMuseumHandsTheKeystoneOverOnEntry() =>
        AssertOpcode(DiosMuseum, 5, 237, [0x81, 0x20, 0x00, 0x47, 0x02],
            "dio's Script 4 writes 583 whatever the battles did");

    /// <summary>The door at the end of the temple tests for exactly 627 before anything.</summary>
    private static void TheTemplesLastDoorTestsItsOwnMoment() =>
        AssertOpcode(TempleLastRoom, 16, 4, [0x16, 0x20, 0x00, 0x00, 0x73, 0x02, 0x00, 0x65],
            "616's entity 16 Talk opens on a test for 627");

    /// <summary>
    /// The model the illusion gate wants the player to walk to is marked as Cloud by the
    /// field's own Init, which is what makes the old label wrong rather than merely
    /// unhelpful.
    /// </summary>
    private static void TheIllusionGateModelIsCloud() =>
        AssertOpcode(NibelheimGateIllusion, 3, 2, [0xA0, 0x00],
            "nivgate2's entity 3 is Cloud");

    /// <summary>
    /// The deck's arrival scene at 1566 ends by running Cait Sith's own Talk while the
    /// player is still frozen, which is why no row asks for it.
    /// </summary>
    private static void TheDeckStartsCaitSithsConversationItself() =>
        AssertOpcode(HighwindDeck, 4, 193, [0x02, 0x0B, 0xC1],
            "fship_25's arrival scene calls entity 11's own Talk");

    /// <summary>
    /// The takeoff is not run for the player: crew3's Talk is where the world map is
    /// entered from, and only from a button press.
    /// </summary>
    private static void TheDeckLeavesTheTakeoffToThePlayer() =>
        AssertOpcode(HighwindDeck, 16, 446, [0x60, 0x2D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
            "fship_25's pilot leaves for the world map at byte 446 of his Talk");

    /// <summary>
    /// Why nothing at the Round Square is offered during the evening. The Square's own
    /// Director calls the attendant's Script 3 when it arrives at 592, and that script -
    /// not the Talk - is what enters the Ferris wheel.
    /// </summary>
    private static void TheRoundSquareRunsTheRideItself()
    {
        AssertOpcode(RoundSquare, 0, 367, [0x03, 0x0A, 0x43],
            "bigwheel's Director calls the attendant's Script 3 on arrival");
        AssertOpcode(RoundSquare, 10, 73, [0x60, 0xE9, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
            "and that script is what enters the Ferris wheel");
    }

    /// <summary>
    /// Rocket Town's street hands the party back to the house itself, which is the
    /// evidence for the row that is deliberately not there.
    /// </summary>
    private static void RocketTownWalksThePartyBackItself() =>
        AssertOpcode(RocketTownStreet, 12, 39,
            [0x60, 0x2E, 0x02, 0x3A, 0xFF, 0x3E, 0x00, 0x18, 0x00, 0x80],
            "rckt's Shera ends her scene by entering 558");

    /// <summary>
    /// And the Highwind's corridor at 1612 runs Cloud's own Script 10, the one that writes
    /// 1614 and leaves, so nothing in that room is a step either.
    /// </summary>
    private static void TheHighwindsLastCorridorRunsItself() =>
        AssertOpcode(HighwindCorridor, 2, 63, [0x03, 0x0A, 0xCA],
            "fship_4's 1612 scene calls Cloud's Script 10 itself");

    // -------------------------------------------------------------------- helpers ----

    private static IReadOnlyList<FieldStoryEventDefinition> AllRows(int fieldId) =>
        FieldStoryEventCatalog.CreateAllFields()
            .Where(definition => definition.FieldId == fieldId)
            .ToArray();

    private static IReadOnlyList<FieldStoryEventDefinition> RowsAt(int fieldId, int moment) =>
        AllRows(fieldId)
            .Where(definition =>
                (definition.MinimumGameMoment < 0 || moment >= definition.MinimumGameMoment) &&
                (definition.MaximumGameMoment < 0 || moment <= definition.MaximumGameMoment) &&
                (definition.TargetGameMoment < 0 || moment < definition.TargetGameMoment))
            .ToArray();

    /// <summary>What the shipping reader actually hands the player in that room.</summary>
    private static IReadOnlyList<FieldNavigationTarget> Offered(int fieldId, int moment)
    {
        var memory = new StoryMemory(fieldId, moment);
        return memory.StoryReader().ReadTargets(
            new FieldPositionSnapshot(FieldPositionReader.FieldModule, fieldId, 0, 0, 0, 0, 0, 0));
    }

    private static void AssertNamesNoPartyMember(string label, string where)
    {
        foreach (var name in PartyMemberNames)
        {
            Equal(false, label.Contains(name, StringComparison.Ordinal),
                $"{where}: '{label}' must not name {name}");
        }
    }

    /// <summary>
    /// The destinations in a field's own triggers section. Section 7 of the nine, each
    /// preceded by its own length; the header is 56 bytes and each of the twelve gateway
    /// records is 24, with the destination field id at +18.
    /// </summary>
    private static IReadOnlyList<int> Gateways(int fieldId)
    {
        const int sectionCountOffset = 2;
        const int sectionOffsetsOffset = 6;
        const int triggersSectionIndex = 7;
        const int headerSize = 56;
        const int recordSize = 24;
        const int destinationOffset = 18;
        const int recordCount = 12;

        var bytes = InstalledFieldBytes(fieldId);
        var sectionCount = BitConverter.ToInt32(bytes, sectionCountOffset);
        Equal(true, sectionCount > triggersSectionIndex,
            $"field {fieldId} must have a triggers section");
        var sectionOffset = BitConverter.ToInt32(
            bytes, sectionOffsetsOffset + (triggersSectionIndex * sizeof(int)));
        var length = BitConverter.ToInt32(bytes, sectionOffset);
        Equal(true, length > 0 && sectionOffset + sizeof(int) + length <= bytes.Length,
            $"field {fieldId}'s triggers section must be readable");
        var triggers = bytes.AsSpan(sectionOffset + sizeof(int), length).ToArray();

        var destinations = new List<int>();
        for (var index = 0; index < recordCount; index++)
        {
            var offset = headerSize + (index * recordSize);
            if (offset + recordSize > triggers.Length)
            {
                break;
            }

            // An unused record is 0x7FFF in the destination field with a zero-length exit
            // line, which is what the eleven the Terminal Floor does not use look like.
            var destination = BitConverter.ToUInt16(triggers, offset + destinationOffset);
            var x1 = BitConverter.ToInt16(triggers, offset);
            var y1 = BitConverter.ToInt16(triggers, offset + 2);
            var x2 = BitConverter.ToInt16(triggers, offset + 6);
            var y2 = BitConverter.ToInt16(triggers, offset + 8);
            if (destination is 0 or 0x7FFF || (x1 == 0 && y1 == 0 && x2 == 0 && y2 == 0))
            {
                continue;
            }

            destinations.Add(destination);
        }

        return destinations;
    }

    /// <summary>
    /// One opcode, anchored to the entity and the byte offset inside whichever of that
    /// entity's scripts holds it. The script id is deliberately not part of the anchor:
    /// the point is that these exact bytes are in that entity and nowhere else in it.
    /// </summary>
    private static void AssertOpcode(int fieldId, int entityId, int byteIndex, byte[] expected, string label)
    {
        var wanted = Convert.ToHexString(expected);
        var matches = Scripts().ReadAllScriptOpcodes(fieldId)
            .Where(script => script.EntityId == entityId)
            .SelectMany(script => script.Opcodes.Select(opcode => (script.ScriptId, Opcode: opcode)))
            .Where(pair => pair.Opcode.ByteIndex == byteIndex &&
                           Convert.ToHexString(pair.Opcode.Bytes.ToArray()) == wanted)
            .ToArray();
        Equal(1, matches.Length,
            $"{label}: field {fieldId} entity {entityId} must hold {wanted} at byte {byteIndex}");
        Console.WriteLine(
            $"remaining story continuity: {fieldId}:{entityId}:{matches[0].ScriptId}@{byteIndex} = {wanted}");
    }

    private static FieldScriptNavigationCatalog Scripts() =>
        new(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is not set."));

    private static byte[] InstalledFieldBytes(int fieldId)
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is not set.");
        var dataSource = new FlevelDataSource(gameRoot);
        if (!dataSource.TryReadField(fieldId, out var encoded))
        {
            throw new InvalidOperationException(
                $"Installed field {fieldId} unavailable: {dataSource.Diagnostic}");
        }

        return Ff7LzsDecoder.DecodeFieldFile(encoded);
    }

    /// <summary>
    /// Enough of a field session for the Story reader: the counter, the field id, a player
    /// event with a collision radius, and one visible model per entity these rooms use.
    /// </summary>
    private sealed class StoryMemory
    {
        private const int FieldState = 0x02800000;
        private const int EventTable = 0x03300000;
        private const int PlayerCollisionRadius = 40;

        private readonly Dictionary<int, byte> bytes = [];

        public StoryMemory(int field, int moment)
        {
            bytes[FieldPositionReader.AddressFieldNumModels] = 16;
            for (var entity = 0; entity < 32; entity++)
            {
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = 0xFF;
            }

            // Model 1 stands in for whichever entity a Model row in these rooms points at;
            // model 0 is the player, which the reader refuses to offer.
            for (var entity = 1; entity < 32; entity++)
            {
                bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = 1;
            }

            SetModel(0, 0, 0, 0);
            SetModel(1, 200, 200, 0);
            WriteInt16(EventTable + FieldNavigationNpcReader.CollisionRadiusOffset,
                PlayerCollisionRadius);

            bytes[FieldPositionReader.AddressCurrentModule] = FieldPositionReader.FieldModule;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        public FieldStoryTargetReader StoryReader() =>
            new(ReadInt32, ReadInt16, ReadByte, FieldStoryEventCatalog.CreateAllFields(), _ => true);

        private byte ReadByte(int address) => bytes.GetValueOrDefault(address);

        private short ReadInt16(int address) =>
            (short)(ReadByte(address) | (ReadByte(address + 1) << 8));

        private int ReadInt32(int address) => address switch
        {
            FieldBoundaryStateReader.AddressFieldGlobalObjectPtr => FieldState,
            FieldNavigationObjectReader.AddressFieldEventDataPtr => EventTable,
            _ => ReadByte(address) | (ReadByte(address + 1) << 8) |
                 (ReadByte(address + 2) << 16) | (ReadByte(address + 3) << 24),
        };

        private void WriteInt16(int address, int value)
        {
            bytes[address] = (byte)value;
            bytes[address + 1] = (byte)(value >> 8);
        }

        private void SetModel(int modelId, int x, int y, int z)
        {
            var record = EventTable + (modelId * FieldNavigationObjectReader.FieldEventDataStride);
            foreach (var (offset, value) in new[]
                     {
                         (FieldNavigationObjectReader.PositionXOffset, x),
                         (FieldNavigationObjectReader.PositionYOffset, y),
                         (FieldNavigationObjectReader.PositionZOffset, z),
                     })
            {
                var scaled = value * FieldNavigationObjectReader.ModelPositionFixedPointScale;
                for (var index = 0; index < 4; index++)
                {
                    bytes[record + offset + index] = (byte)(scaled >> (index * 8));
                }
            }

            bytes[record + FieldNavigationObjectReader.VisibilityOffset] = 1;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"remaining story continuity - {label}: expected {expected}, got {actual}");
        }
    }
}
