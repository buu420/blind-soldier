using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The rooms a player stood in with nothing to do.
///
/// <para>The 2026-09-22 capture has field 557 at GameMoment 523 reporting
/// <c>story=0</c> in 563 of 563 samples, from 14:59:57Z to 15:33:06Z - thirty-three
/// minutes in Rocket Town with the Story category empty - and the same for rkt_i,
/// rkt_w, rktmin1 and rktmin2. The arrival step existed, but on field 551.</para>
///
/// <para>551 and 557 are the two Rocket Town streets and they share seven gateway
/// lines, so the gateway table alone cannot tell them apart. The scripts can. Every
/// side room's own exit is <c>IFSW Bank[2][0] &lt; 1308 -&gt; MAPJUMP 557</c>, else 551,
/// so below 1308 the town is 557 and 551 belongs to the Huge Materia visit. 557 is also
/// the only one of the two that loads Rufus, Heidegger and Shera, and the only one with
/// a gateway to the rocket base at all - the chapter is not walkable from 551.</para>
///
/// <para>The Mount Nibel half is the same shape. mtnvl4 is three disconnected pieces of
/// walkmesh; the rows covered two of them, and the third - the 221-triangle component
/// nvdun2's gateway 0 puts the party down on - had nothing, with no route to the world
/// exit from it at all.</para>
/// </summary>
internal static class StoryProgressionGapTests
{
    /// <summary>The town below Bank[2][0] 1308.</summary>
    private const int RocketTownStreet = 557;

    /// <summary>The town at and above it.</summary>
    private const int HugeMateriaStreet = 551;

    private const int ArrivalMoment = 523;

    /// <summary>
    /// The captain's house door, which 551 carries as gateway 1 and 557 as gateway 2 on
    /// the very same line.
    /// </summary>
    private static readonly FieldNavigationTriggerLine CaptainsHouseDoor =
        new(292, 1373, 0, 351, 1349, 0);

    /// <summary>
    /// Each side room, the entity whose <c>line1</c> Go performs the MAPJUMP out, and the
    /// coordinates that <c>LINE</c> opcode declares. Read from the installed scripts; the
    /// installed-data case below checks them against the archive rather than trusting
    /// this table.
    /// </summary>
    private static readonly (int Field, int Entity, string Label, int X1, int Y1, int X2, int Y2)[] SideRooms =
    [
        (553, 7, "Go back out to the town", 541, 219, 547, 278),
        (554, 7, "Go back out to the town", -40, 1, 44, -1),
        (555, 6, "Go back out to the town", -335, -3, -219, -3),
        (559, 4, "Go back out to the town", -65, -41, 3, -41),
        (560, 5, "Go back out to the town", -530, 57, -531, 136),
    ];

    /// <summary>
    /// Every point the game puts the party down on in the Rocket Town street, from the
    /// side rooms' own MAPJUMPs, the house, the rocket base and the arrival fade.
    /// </summary>
    private static readonly (int X, int Y, int Triangle, string From)[] TownArrivals =
    [
        (7, -702, 2, "blackbg3, the arrival fade"),
        (-37, 2371, 50, "rcktbas1 line1"),
        (99, -501, 7, "rktinn1 line1"),
        (-863, 112, 16, "rktmin1 line1"),
        (703, 557, 32, "rktmin2 line1"),
        (300, 1194, 42, "rktsid line3"),
        (-239, 607, 25, "rkt_i line1"),
        (-101, -749, 3, "rkt_w line1"),
    ];

    /// <summary>
    /// The Mount Nibel cave loop: the field, the arrivals into it, and the label its row
    /// must carry. mtnvl4 appears twice because two of its three components are separate
    /// places with separate answers.
    /// </summary>
    private static readonly (int Field, string Label, (int X, int Y, int Triangle, string From)[] Arrivals)[] CaveLoop =
    [
        (313, "Go back into the cave",
            [(984, 804, 240, "nvdun2 gateway 0")]),
        (318, "Take this passage on through the cave",
            [(43, -558, 5, "mtnvl4 gateway 0"), (-74, 1665, 54, "nvdun3 gateway 1")]),
        (319, "Take this passage on through the cave",
            [(-251, 942, 158, "nvdun2 gateway 1"), (-7, -1360, 1, "nvdun4 gateway 0")]),
        (321, "Take this passage out to the mountain path",
            [(-362, -256, 71, "nvdun3 gateway 0"), (431, -248, 3, "mtnvl5 gateway 1")]),
        (314, "Follow the path on to the next opening",
            [(2836, -1081, 61, "nvdun4 gateway 1"), (-695, -624, 298, "mtnvl6 gateway 1")]),
        (315, "Go back into the pipe room",
            [(-458, -769, 17, "mtnvl5 gateway 0"), (561, 863, 69, "nvdun1 gateway 1")]),
    ];

    /// <summary>The two mtnvl4 ledges that already had rows.</summary>
    private static readonly (int X, int Y, int Triangle, string Label)[] MountNibelLedges =
    [
        (175, 774, 245, "Leave the mountain by the northern path"),
        (940, 549, 189, "Go back into the cave; this ledge leads nowhere"),
    ];

    /// <summary>Everything that reads only the shipped catalog.</summary>
    public static void Run()
    {
        TheTownTheGameLoadsCarriesTheArrivalStep();
        TheHugeMateriaStreetStaysOutOfTheFirstVisit();
        EverySideRoomOffTheStreetNamesItsWayBackOut();
        NothingClaimsTheBackyardDoorGoesUpTheRocket();
        EveryMountNibelCaveRoomNamesAStep();
        TheThirdLedgeOfMountNibelIsNotSilent();
        Console.WriteLine("story progression gap tests passed.");
    }

    /// <summary>The cases that walk the installed walkmesh and read the installed scripts.</summary>
    public static void RunWithInstalledGameData(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        FieldScriptNavigationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        ArgumentNullException.ThrowIfNull(catalog);
        EverySideRoomRowSitsOnTheRoomsOwnNativeExit(catalog);
        EveryNativeArrivalCanWalkToTheStepItIsOffered(createWalkmeshReader);
        TheArrivalStepIsWhatTheControllerAnnouncesAndWalksTo(createWalkmeshReader);
        Console.WriteLine("story progression gap tests passed with installed game data.");
    }

    /// <summary>
    /// The defect itself. The party arrives on 557 with the moment still at 523, and that
    /// is where the step has to be.
    /// </summary>
    private static void TheTownTheGameLoadsCarriesTheArrivalStep()
    {
        var memory = new ProgressionMemory(ArrivalMoment);
        var offered = memory.Reader().ReadTargets(Position(RocketTownStreet));
        Equal(true, offered.Any(target => target.TriggerLine == CaptainsHouseDoor),
            "arriving in Rocket Town the step is the captain's house, on the street the game loads");
        Equal("Go into the captain's house",
            offered.Single(target => target.TriggerLine == CaptainsHouseDoor).Label,
            "and it says which house it is");
    }

    private static void TheHugeMateriaStreetStaysOutOfTheFirstVisit()
    {
        var memory = new ProgressionMemory(ArrivalMoment);
        Equal(0, memory.Reader().ReadTargets(Position(HugeMateriaStreet)).Count,
            "551 is not the street of this chapter and must offer nothing in it");

        // It is still a real street, so nothing here claims the field is unused.
        var later = new ProgressionMemory(1310);
        Equal(0, later.Reader().ReadTargets(Position(HugeMateriaStreet))
                .Count(target => target.TriggerLine == CaptainsHouseDoor),
            "and the row that was on it has moved rather than been duplicated");
    }

    /// <summary>
    /// A player who went looking in the shops was told the Story category was empty. Each
    /// room now names the one thing that is true in it, below every real step.
    /// </summary>
    private static void EverySideRoomOffTheStreetNamesItsWayBackOut()
    {
        foreach (var moment in new[] { 523, 535, 550, 565 })
        {
            var memory = new ProgressionMemory(moment);
            foreach (var room in SideRooms)
            {
                var offered = memory.Reader(enabledLineEntityId: room.Entity)
                    .ReadTargets(Position(room.Field));
                Equal(true, offered.Any(target => target.Label == room.Label),
                    $"field {room.Field} at moment {moment} must name the way back to the street");
            }
        }

        // And it never outranks a real step: the house at 550 has its own, and the shop
        // row must not be able to stand in for one.
        var priorities = FieldStoryEventCatalog.CreateAllFields()
            .Where(definition => SideRooms.Any(room => room.Field == definition.FieldId) &&
                definition.MinimumGameMoment == 523)
            .Select(definition => definition.Priority)
            .Distinct()
            .ToArray();
        Equal(1, priorities.Length, "the side-room rows share one priority");
        Equal(true, priorities[0] > 0, "which is below every routed step");

        // A room whose line the game has switched off is not a way anywhere.
        var closed = new ProgressionMemory(523);
        Equal(0, closed.Reader(enabledLineEntityId: -1).ReadTargets(Position(554)).Count,
            "a disabled line1 is not offered");
    }

    /// <summary>
    /// The back door of the captain's house opens on the backyard below 553, not on the
    /// rocket. A coarser row used to say otherwise from the same trigger.
    /// </summary>
    private static void NothingClaimsTheBackyardDoorGoesUpTheRocket()
    {
        Equal(0, FieldStoryEventCatalog.CreateAllFields()
                .Count(definition => definition.Label == "Go up to the rocket to continue"),
            "the row that named the rocket from the house's back door is withdrawn");

        var memory = new ProgressionMemory(ArrivalMoment);
        var backDoor = memory.Reader(enabledLineEntityId: 5).ReadTargets(Position(558))
            .Where(target => target.TriggerLine is { StartX: -23, StartY: 898 })
            .ToArray();
        Equal(1, backDoor.Length, "the back door is offered exactly once");
        Equal("Press Confirm at the back door into the backyard", backDoor[0].Label,
            "and it names the backyard, which is where it goes");
    }

    private static void EveryMountNibelCaveRoomNamesAStep()
    {
        var memory = new ProgressionMemory(ArrivalMoment);
        foreach (var room in CaveLoop)
        {
            if (room.Field == 313)
            {
                continue;
            }

            var offered = memory.Reader().ReadTargets(Position(room.Field));
            Equal(true, offered.Any(target => target.Label == room.Label),
                $"field {room.Field} must name the way on at moment {ArrivalMoment}");
        }

        // The flashback and the Mideel visit walk the same rooms with different scripts,
        // and curating them here would have withdrawn the entry-door rows that serve
        // those chapters. They are still there.
        var everything = FieldStoryEventCatalog.CreateAllFields();
        foreach (var (field, moment) in new[] { (314, 363), (315, 376), (315, 1182) })
        {
            Equal(true, everything.Any(definition =>
                    definition.FieldId == field &&
                    definition.MinimumGameMoment <= moment &&
                    definition.MaximumGameMoment >= moment),
                $"field {field} must still carry its own row at moment {moment}");
        }
    }

    /// <summary>
    /// mtnvl4's third component. The party lands on triangle 240 coming back out of
    /// nvdun2, and from there the world exit is not reachable at all, so the row that
    /// names it must not be the one offered.
    /// </summary>
    private static void TheThirdLedgeOfMountNibelIsNotSilent()
    {
        var memory = new ProgressionMemory(ArrivalMoment);
        var arrival = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, 313, 0, 984, 804, -210, 240, 0);
        var offered = memory.Reader().ReadTargets(arrival);
        Equal(true, offered.Any(target => target.Label == "Go back into the cave"),
            "the third ledge names the door it actually has");
        foreach (var (_, _, _, label) in MountNibelLedges)
        {
            Equal(false, offered.Any(target => target.Label == label),
                $"and not {label}, which belongs to a piece of mesh this one cannot reach");
        }

        // The two ledges that already had rows keep them.
        foreach (var (x, y, triangle, label) in MountNibelLedges)
        {
            var ledge = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, 313, 0, x, y, 0, (ushort)triangle, 0);
            Equal(true, memory.Reader().ReadTargets(ledge).Any(target => target.Label == label),
                $"the ledge at ({x},{y}) keeps {label}");
        }
    }

    /// <summary>
    /// The authored trigger against the installed archive: the row must sit on the very
    /// LINE the room's own exit script owns, and that script must leave for this street.
    /// </summary>
    private static void EverySideRoomRowSitsOnTheRoomsOwnNativeExit(
        FieldScriptNavigationCatalog catalog)
    {
        var catalogRows = FieldStoryEventCatalog.CreateAllFields();
        foreach (var room in SideRooms)
        {
            var row = catalogRows.Single(definition =>
                definition.FieldId == room.Field &&
                definition.Label == room.Label &&
                definition.MinimumGameMoment == 523);
            var nativeExits = catalog.ReadField(room.Field).Exits;
            Equal(true, nativeExits.Any(exit =>
                    exit.TriggerEntityId == room.Entity &&
                    exit.TriggerLine == row.TriggerLine &&
                    exit.DestinationFieldIds is { Count: > 0 } destinations &&
                    destinations.Contains(RocketTownStreet)),
                $"field {room.Field}'s row must sit on entity {room.Entity}'s own exit to {RocketTownStreet}");
            Equal(room.Entity, row.RequiredEnabledLineEntityId,
                $"field {room.Field}'s row must be gated on that line being live");
            Equal(new FieldNavigationTriggerLine(room.X1, room.Y1, 0, room.X2, room.Y2, 0),
                row.TriggerLine,
                $"field {room.Field}'s trigger must be the native LINE");
        }
    }

    /// <summary>
    /// Every point the game puts the party down on has to be able to walk to the step it
    /// is offered. A row on an unreachable piece of mesh is worse than no row.
    /// </summary>
    private static void EveryNativeArrivalCanWalkToTheStepItIsOffered(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var checkedRoutes = 0;
        var memory = new ProgressionMemory(ArrivalMoment);

        foreach (var (x, y, triangle, from) in TownArrivals)
        {
            var reader = createWalkmeshReader(RocketTownStreet);
            var mesh = Mesh(reader, RocketTownStreet);
            var position = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, RocketTownStreet, 0,
                x, y, FloorHeight(mesh, triangle, x, y), (ushort)triangle, 0);
            var step = memory.Reader().ReadTargets(position)
                .SingleOrDefault(target => target.TriggerLine == CaptainsHouseDoor);
            Equal(true, step.Label is { Length: > 0 },
                $"the street offers the house from the arrival at {from}");
            Equal(true, new FieldWalkmeshRoutePlanner(reader).TryBuildRoute(position, step, out _),
                $"and it is walkable from the arrival at {from} ({x},{y})");
            checkedRoutes++;
        }

        foreach (var room in CaveLoop)
        {
            var reader = createWalkmeshReader(room.Field);
            var mesh = Mesh(reader, room.Field);
            foreach (var (x, y, triangle, from) in room.Arrivals)
            {
                var position = new FieldPositionSnapshot(
                    FieldPositionReader.FieldModule, room.Field, 0,
                    x, y, FloorHeight(mesh, triangle, x, y), (ushort)triangle, 0);
                var step = memory.Reader().ReadTargets(position)
                    .SingleOrDefault(target => target.Label == room.Label);
                Equal(true, step.Label is { Length: > 0 },
                    $"field {room.Field} offers {room.Label} from the arrival at {from}");
                Equal(true, new FieldWalkmeshRoutePlanner(reader).TryBuildRoute(position, step, out _),
                    $"and it is walkable from the arrival at {from} ({x},{y})");
                checkedRoutes++;
            }
        }

        Console.WriteLine($"  story progression: {checkedRoutes} native arrivals reach the step offered to them.");
    }

    /// <summary>
    /// Reader to controller to a native route, from the position the capture actually
    /// put the party on when it arrived in the town.
    /// </summary>
    private static void TheArrivalStepIsWhatTheControllerAnnouncesAndWalksTo(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(RocketTownStreet);
        var mesh = Mesh(reader, RocketTownStreet);
        var memory = new ProgressionMemory(ArrivalMoment);
        var storyReader = memory.Reader();
        var arrival = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, RocketTownStreet, 0,
            7, -702, FloorHeight(mesh, 2, 7, -702), 2, 0);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource(
                Array.Empty<FieldNavigationTarget>(),
                storyTargetProvider: storyReader.ReadTargets),
            new FieldWalkmeshRoutePlanner(reader));
        var transform = new FieldNavigationControlTransform(0);

        var guard = 0;
        while (controller.CurrentCategory != FieldNavigationCategory.Story && guard++ < 8)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, arrival, transform);
        }

        Equal(FieldNavigationCategory.Story, controller.CurrentCategory,
            "the Story category is reachable from the arrival");
        var announced = controller.HandleAction(FieldNavigationAction.NextTarget, arrival, transform);
        Contains("Go into the captain's house", announced?.Speech,
            "and the first thing it says in Rocket Town is where to go");

        var walking = controller.HandleAction(FieldNavigationAction.ToggleBeacon, arrival, transform);
        Equal(true, controller.BeaconEnabled,
            "auto walk takes the objective rather than refusing it");
        Equal(false, walking?.Speech?.Contains("Route unavailable", StringComparison.Ordinal) ?? false,
            $"and the route is real: {walking?.Speech}");
    }

    private static FieldWalkmesh Mesh(FieldWalkmeshReader reader, int fieldId) =>
        reader.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, fieldId, 0, 0, 0, 0, 0, 0))
            .Walkmesh
        ?? throw new InvalidOperationException($"the installed field {fieldId} walkmesh must be readable.");

    private static FieldPositionSnapshot Position(int fieldId) =>
        new(FieldPositionReader.FieldModule, fieldId, 0, 0, 0, 0, 0, 0);

    internal static int FloorHeight(FieldWalkmesh mesh, int triangleIndex, int x, int y)
    {
        var triangle = mesh.Triangles[triangleIndex];
        var a = triangle.Vertex0;
        var b = triangle.Vertex1;
        var c = triangle.Vertex2;
        var denominator = (((double)b.Y - c.Y) * ((double)a.X - c.X)) +
                          (((double)c.X - b.X) * ((double)a.Y - c.Y));
        if (Math.Abs(denominator) < 1e-9d)
        {
            return (int)Math.Round(triangle.GetCentroid().Z);
        }

        var weightA = ((((double)b.Y - c.Y) * (x - c.X)) + (((double)c.X - b.X) * (y - c.Y))) / denominator;
        var weightB = ((((double)c.Y - a.Y) * (x - c.X)) + (((double)a.X - c.X) * (y - c.Y))) / denominator;
        return (int)Math.Round((weightA * a.Z) + (weightB * b.Z) + ((1d - weightA - weightB) * c.Z));
    }

    /// <summary>
    /// The native state the Story reader consults, with no models on screen: these rows
    /// are all Location rows on doors and lines.
    /// </summary>
    private sealed class ProgressionMemory(int gameMoment)
    {
        private const int EventTable = 0x100000;
        private const int PlayerCollisionRadius = 34;

        public FieldStoryTargetReader Reader(int enabledLineEntityId = int.MinValue) =>
            new(
                ReadInt32,
                ReadInt16,
                ReadByte,
                FieldStoryEventCatalog.CreateAllFields(),
                entity => enabledLineEntityId == int.MinValue || entity == enabledLineEntityId);

        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressCurrentModule)
            {
                return FieldPositionReader.FieldModule;
            }

            if (address == FieldPositionReader.AddressFieldNumModels)
            {
                return 1;
            }

            if (address == FieldNavigationObjectReader.AddressFieldBankBase)
            {
                return (byte)gameMoment;
            }

            if (address == FieldNavigationObjectReader.AddressFieldBankBase + 1)
            {
                return (byte)(gameMoment >> 8);
            }

            // No entity resolves to a model, so no Model row can be offered here.
            return address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                   address < FieldNavigationObjectReader.AddressFieldModelIdArray + 255
                ? (byte)0xFF
                : (byte)0;
        }

        private static int ReadInt32(int address) =>
            address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? EventTable : 0;

        private static short ReadInt16(int address) =>
            address == EventTable + 0x72 ? (short)PlayerCollisionRadius : (short)0;
    }

    private static void Contains(string expected, string? actual, string label)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Story progression - {label}: expected to contain \"{expected}\", got \"{actual}\".");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Story progression - {label}: expected {expected}, got {actual}.");
        }
    }
}
