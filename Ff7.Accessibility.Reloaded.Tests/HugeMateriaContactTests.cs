using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Cosmo Canyon's storage room, bugin1b (field 542), is reached by walking into a Huge
/// Materia rather than by pressing anything: entity script 2 is its Contact script and
/// there is no Talk script on those entities at all. The touch is tested at the movement
/// probes, not at the party's centre: 00636C41 checks each step the player's collision
/// width ahead of the next position, along the heading and forty-five degrees either side,
/// and FUN_00637724 touches a model when a probe lands closer to it than half the sum of the
/// two collision widths and the height difference is strictly inside (-127, 128). That is a
/// differently shaped reach from Talk's player-radius-plus-talk-radius around the centre
/// (00636284), and a longer one: the party is stopped by the touch one width plus the
/// half-sum from the stand, and never gets its centre inside the half-sum by walking.
/// These cases run the installed placements through the real reader, the installed
/// walkmesh planner and the live navigation controller, so the reach is proved end to
/// end rather than asserted as arithmetic in the reader.
/// </summary>
internal static class HugeMateriaContactTests
{
    private const int FieldId = 542;
    private const int SecondStandEntity = 11; // HUGEA.
    private const int SecondStandX = -2027; // 542:11:0 XYZI, the installed placement.
    private const int SecondStandY = -4246;
    private const int SecondStandZ = -1792;
    private const string SecondStandLabel = "Yellow Huge Materia";

    // The measured native widths in that room: the party leader is 34 wide and the
    // materia on its stand is 1, so a probe touches inside (34 + 1) / 2 = 17 of the stand.
    // The forward probe is 34 ahead of the next position, so walking at the stand touches it
    // with the party's centre 34 + 17 = 51 away plus the step being taken, at most 8: the
    // reach is 59. Talk would have accepted 34 around the centre.
    private const short PlayerCollisionWidth = 34;
    private const short MateriaCollisionWidth = 1;
    private const int ProbeTouchRadius = 17;
    private const int ContactReach = 59;
    private const int TalkReachIfItHadBeenTalk = 34;

    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        EachStandIsOfferedOnlyUnderItsOwnInstalledGate();
        StoredMateriaAreObjectsOnLaterVisitsAndRespectLiveModelState();
        TheExitUsesTheNativeMateriaMenu(createWalkmeshReader);
        InstalledScriptsOfferGoBackDown();
        RoutesFromEveryReportedStall(createWalkmeshReader);
        var target = TheReaderCarriesTheNativeContactReachAndKind();
        TheInstalledWalkmeshCanBeWalkedIntoContactReach(createWalkmeshReader, target);
        ArrivalUsesContactShapeAndNeverAsksForAButton(target);
        Console.WriteLine("PASS stored Huge Materia Objects, native Contact and menu exit navigation");
    }

    /// <summary>
    /// cosmo3 has four stands and four separate gates, and 542:11:0, 542:12:0, 542:13:0
    /// and 542:14:0 say which is which: HUGEA on bank13[82] bit 4, HUGEB on bank15[145]
    /// bit 3, HUGEC on bank13[82] bit 0 and HUGED on bank13[82] bit 7. Pairing a flag
    /// with the wrong entity offers a stand the field has not lit, and the reader then
    /// finds an invisible model and says nothing at all - which in that room is the
    /// difference between a way out and silence.
    /// </summary>
    private static void EachStandIsOfferedOnlyUnderItsOwnInstalledGate()
    {
        (int Entity, int Bank, int Address, byte Value, string Label)[] stands =
        [
            (12, 15, 145, 0x08, "Green Huge Materia"),
            (11, 13, 82, 0x10, "Yellow Huge Materia"),
            (13, 13, 82, 0x01, "Red Huge Materia"),
            (14, 13, 82, 0x80, "Blue Huge Materia")
        ];

        foreach (var stand in stands)
        {
            var memory = new NativeMemory();
            memory.SetGameMoment(1391);
            memory.SetPlayerCollisionWidth(PlayerCollisionWidth);
            memory.SetBankByte(stand.Bank, stand.Address, stand.Value);

            // Every stand is standing there; only the flag decides which one is lit.
            byte modelId = 1;
            foreach (var other in stands)
            {
                memory.ConfigureStand(
                    other.Entity,
                    modelId++,
                    SecondStandX,
                    SecondStandY,
                    SecondStandZ,
                    MateriaCollisionWidth);
            }

            var target = SingleObjectTarget(memory, StartingPosition());
            Equal(stand.Label, target.Label, $"the stand lit by bank{stand.Bank}[{stand.Address}]");
            Equal(stand.Entity, target.TriggerEntityId, $"{stand.Label} names its own entity");
            Equal(
                FieldNavigationActivation.Contact,
                target.Activation,
                $"{stand.Label} is a Contact");
            var exits = new FieldStoryTargetReader(memory.ReadInt32, memory.ReadInt16, memory.ReadByte,
                FieldStoryEventCatalog.CreateAllFields(), _ => true).ReadExitTargets(StartingPosition());
            Equal(1, exits.Count, "only the lit stand offers its return menu in Exits");
            Equal(stand.Entity, exits[0].TriggerEntityId, "the return approach uses that same stand");
            var story = new FieldStoryTargetReader(memory.ReadInt32, memory.ReadInt16, memory.ReadByte,
                FieldStoryEventCatalog.CreateAllFields(), _ => true).ReadTargets(StartingPosition());
            Equal(1, story.Count, "one available stand supplies the pending departure objective");
            Equal(stand.Entity, story[0].TriggerEntityId, "Story uses an available stand for the departure");
        }
    }

    private static FieldNavigationTarget TheReaderCarriesTheNativeContactReachAndKind()
    {
        var memory = CreateStorageRoom();
        var target = SingleObjectTarget(memory, StartingPosition());

        Equal(SecondStandLabel, target.Label, "the lit stand is the one offered");
        Equal(SecondStandEntity, target.TriggerEntityId, "the target names its own native entity");
        Equal(SecondStandX, target.X, "the target stands where 542:11:0's XYZI put it");
        Equal(SecondStandY, target.Y, "the target stands where 542:11:0's XYZI put it");
        Equal(SecondStandZ, target.Z, "the target stands where 542:11:0's XYZI put it");
        Equal(
            FieldNavigationActivation.Contact,
            target.Activation,
            "a Contact row has to reach the route and the controller as a Contact");
        Equal(
            ContactReach,
            target.InteractionRadius,
            "Contact reach is where the forward probe of a step touches the stand");
        Equal(
            PlayerCollisionWidth + ProbeTouchRadius + 8,
            target.InteractionRadius,
            "one width ahead, inside the half-sum, one native step out");
        Equal(
            false,
            target.InteractionRadius == TalkReachIfItHadBeenTalk,
            "Talk's reach is a different test and is not what this row uses");
        return target;
    }

    private static void TheInstalledWalkmeshCanBeWalkedIntoContactReach(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        FieldNavigationTarget target,
        FieldPositionSnapshot? startingPosition = null)
    {
        var start = startingPosition ?? StartingPosition();
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(FieldId));
        Equal(
            true,
            planner.TryBuildRoute(start, target, out var route),
            $"the storage room stand must be walkable into on the installed walkmesh: {planner.LastDiagnostic}");

        var dx = target.X - (double)route.FinalApproach.X;
        var dy = target.Y - (double)route.FinalApproach.Y;
        Equal(
            true,
            dx * dx + dy * dy < ContactReach * (double)ContactReach,
            "the route has to finish strictly inside the native Contact reach, not merely near it");

        var dz = target.Z - route.FinalApproach.Z;
        Equal(
            true,
            dz > -127 && dz < 128,
            "the route has to finish inside the native Contact height band");
    }

    private static void ArrivalUsesContactShapeAndNeverAsksForAButton(
        FieldNavigationTarget target)
    {
        // Standing 52 units away and facing the stand, the next step's forward probe lands
        // 52 - 8 - 34 = 10 from it, inside the 17 that touches. Nothing has touched yet, so
        // the route says the stand is walked into and goes on guiding - auto walk included.
        var touching = PositionNear(target, offsetX: 52, offsetZ: 0);
        var approach = Track(target, touching, contactStarted: false);
        Equal(
            true,
            approach.Speech.Contains("Walk into it", StringComparison.Ordinal),
            $"a Contact target has to say what actually triggers it, got '{approach.Speech}'");
        Equal(
            false,
            approach.Speech.Contains("Interact here", StringComparison.Ordinal),
            $"a Contact target must not fall back to the generic interaction prompt, got '{approach.Speech}'");
        Equal(
            false,
            approach.Speech.Contains("Confirm", StringComparison.OrdinalIgnoreCase),
            $"nothing is pressed at a Contact, got '{approach.Speech}'");
        Equal(true, approach.BeaconStillOn, "the route goes on until the stand is touched");
        Equal(true, approach.AutoWalkMoves, "auto walk keeps walking into the stand");

        // The party's centre is the same on the step before the bump and on the bump, so only
        // the stand's Contact script starting (priority 1, script 2) finishes the route.
        var touched = Track(target, touching, contactStarted: true);
        Equal(
            true,
            touched.Speech.Contains("reached", StringComparison.Ordinal) && !touched.BeaconStillOn,
            $"the route ends when the game has started the stand's Contact, got '{touched.Speech}'");
        Equal(false, touched.AutoWalkMoves, "and auto walk stops there, so it is not walked into again");

        // 60 units away even the longest step leaves the forward probe 18 from the stand,
        // outside the 17 that touches: no hint yet.
        Equal(
            "",
            Track(target, PositionNear(target, offsetX: 60, offsetZ: 0), contactStarted: false).Speech,
            "the stand is not announced as touched short of where the probes touch");

        // The height band is its own test and is not symmetric.
        Equal(
            "",
            Track(target, PositionNear(target, offsetX: 52, offsetZ: 200), contactStarted: false).Speech,
            "a stand 200 units below the party is out of the native Contact band");
        Equal(
            "",
            Track(target, PositionNear(target, offsetX: 52, offsetZ: -127), contactStarted: false).Speech,
            "the lower bound of the native Contact band is exclusive");
        Equal(
            true,
            Track(target, PositionNear(target, offsetX: 52, offsetZ: -126), contactStarted: false).Speech
                .Contains("Walk into it", StringComparison.Ordinal),
            "one unit inside the lower bound is a touch the game accepts");

        // A Contact script some other script starts while the party is across the room is not
        // this approach arriving.
        Equal(
            true,
            Track(target, PositionNear(target, offsetX: 400, offsetZ: 0), contactStarted: true).BeaconStillOn,
            "a Contact started far from the party does not finish the route");
    }

    private static (string Speech, bool BeaconStillOn, bool AutoWalkMoves) Track(
        FieldNavigationTarget target,
        FieldPositionSnapshot position,
        bool contactStarted)
    {
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource(
                Array.Empty<FieldNavigationTarget>(),
                objectTargetProvider: _ => target.Category == FieldNavigationCategory.Objects ? [target] : [],
                storyTargetProvider: _ => target.Category == FieldNavigationCategory.Story ? [target] : [],
                exitTargetProvider: _ => target.Category == FieldNavigationCategory.Exits ? [target] : []),
            new StraightRoutePlanner())
        {
            ContactStarted = started => started.StableId == target.StableId && contactStarted
        };
        for (var index = 0; controller.CurrentCategory != target.Category && index < 4; index++)
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, position);
        _ = controller.HandleAction(
            FieldNavigationAction.ToggleBeacon,
            position,
            new FieldNavigationControlTransform(0));
        Equal(true, controller.BeaconEnabled, "the Contact route is active");
        var update = controller.UpdateLiveTracking(
            position,
            new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
            new FieldNavigationControlTransform(0),
            isSuppressed: false,
            arrivalDistanceUnits: 80);
        var moves = controller.TryResolveAutomaticInput(position, new FieldNavigationControlTransform(0), 80, out var input) &&
                    input != FieldNavigationInput.None;
        return (update?.Speech ?? "", controller.BeaconEnabled, moves);
    }

    private static FieldPositionSnapshot PositionNear(
        FieldNavigationTarget target,
        int offsetX,
        int offsetZ) =>
        new(
            FieldPositionReader.FieldModule,
            FieldId,
            0,
            target.X - offsetX,
            target.Y,
            target.Z - offsetZ,
            21,
            0);

    // The native arrival used by the physical route diagnostics for this room.
    private static FieldPositionSnapshot StartingPosition() =>
        new(FieldPositionReader.FieldModule, FieldId, 0, -1821, -1835, SecondStandZ, 99, 0);

    private static FieldNavigationTarget SingleObjectTarget(
        NativeMemory memory,
        FieldPositionSnapshot position)
    {
        var targets = ObjectTargets(memory, position);
        Equal(1, targets.Count, "exactly the lit stand is offered in Objects in the storage room");
        Equal(FieldNavigationCategory.Objects, targets[0].Category, "stored materia are optional Objects");
        return targets[0];
    }

    private static IReadOnlyList<FieldNavigationTarget> ObjectTargets(NativeMemory memory, FieldPositionSnapshot position) =>
        new FieldNavigationObjectReader(
            memory.ReadInt32,
            memory.ReadByte,
            _ => null,
            _ => null,
            FieldNavigationObjectCatalog.CreateAllFields()).ReadTargets(position);

    private static void StoredMateriaAreObjectsOnLaterVisitsAndRespectLiveModelState()
    {
        var memory = CreateStorageRoom();
        var position = StartingPosition();
        var reader = new FieldStoryTargetReader(memory.ReadInt32, memory.ReadInt16, memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), _ => true);
        var pending = reader.ReadTargets(position);
        Equal(1, pending.Count, "the pending Bugenhagen departure has one Story objective");
        Equal(true, pending[0].Label.Contains("Go back down", StringComparison.Ordinal),
            "Story explains the required return menu choice, rather than revisiting each stand");
        ArrivalUsesContactShapeAndNeverAsksForAButton(pending[0]);
        memory.SetBankByte(3, 188, 0x08);
        Equal(0, reader.ReadTargets(position).Count, "the completed departure is not a repeat Story objective");
        memory.SetGameMoment(1999);
        Equal(0, reader.ReadTargets(position).Count, "later display visits have no departure Story objective");
        var target = SingleObjectTarget(memory, position);
        Equal(FieldObjectCueKind.Materia, target.ObjectCueKind, "a stored materia uses the materia cue");
        memory.SetStandState(visible: false, solid: true);
        Equal(0, ObjectTargets(memory, position).Count, "an undrawn stand is not offered");
        memory.SetStandState(visible: true, solid: false);
        Equal(0, ObjectTargets(memory, position).Count, "a stand with disabled Contact is not offered");
        memory.SetStandState(visible: true, solid: true);
        Equal(SecondStandLabel, SingleObjectTarget(memory, position).Label,
            "a previously visited stand stays available for its normal repeat interaction");
    }

    private static void TheExitUsesTheNativeMateriaMenu(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = CreateStorageRoom();
        var position = StartingPosition();
        var exits = new FieldStoryTargetReader(memory.ReadInt32, memory.ReadInt16, memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), _ => true).ReadExitTargets(position);
        Equal(1, exits.Count, "a visible stored materia offers the native way back down in Exits");
        Equal(FieldNavigationCategory.Exits, exits[0].Category, "the way back down is an exit");
        Equal(SecondStandEntity, exits[0].TriggerEntityId, "the exit is the live stand's Contact menu");
        Equal(FieldNavigationActivation.Contact, exits[0].Activation, "walk into the stand to open the menu");
        Equal(true, exits[0].Label.StartsWith("Go back down menu at", StringComparison.Ordinal),
            "exit guidance states the native choice instead of pointing outside this room");
        Equal(SecondStandX, exits[0].X, "exit navigation uses the stand's live coordinates");
        Equal(59, exits[0].InteractionRadius, "exit navigation keeps native Contact reach");
        TheInstalledWalkmeshCanBeWalkedIntoContactReach(createWalkmeshReader, exits[0]);
        ArrivalUsesContactShapeAndNeverAsksForAButton(exits[0]);
        var obsolete = new FieldNavigationTarget(542, FieldNavigationCategory.Exits,
            "Exit to Bugen Research Center", -378, 30, -36, "gateway:542:0:544");
        Equal(0, new FieldExitPresentationPolicy(() => true).Apply([obsolete]).Count,
            "the leftover gateway from the ordinary observatory is not an exit from the display");
        var ordinary = obsolete with { FieldId = 541, StableId = "gateway:541:0:544" };
        Equal(1, new FieldExitPresentationPolicy(() => true).Apply([ordinary]).Count,
            "the ordinary observatory keeps its real doorway");
    }

    private static void InstalledScriptsOfferGoBackDown()
    {
        var roots = new[] { @"C:\Games\Final Fantasy VII\workingdir",
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir" }
            .Where(Directory.Exists)
            .Concat((Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
        Equal(true, roots.Length > 0, "native observatory scripts must be available");
        foreach (var root in roots)
        {
            var catalog = new FieldScriptNavigationCatalog(root);
            var text = new FlevelFieldTextResolver(root);
            foreach (var entity in new[] { 11, 12, 13, 14 })
            {
                var modelIndex = entity - 2;
                var resource = entity switch { 11 => "4hmaty.char", 12 => "4hmatg.char",
                    13 => "4hmatr.char", _ => "4hmatb.char" };
                Equal(true, catalog.ReadScriptOpcodes(542, entity, 0)
                    .Any(op => op.Opcode == 0xA1 && op.Bytes[1] == modelIndex),
                    $"{root}: stand {entity} keeps its verified colour model slot");
                Equal(true, catalog.ReadField(542).Npcs.Single(npc => npc.EntityId == entity)
                    .ModelResourceName.EndsWith(resource, StringComparison.OrdinalIgnoreCase),
                    $"{root}: stand {entity} keeps its verified colour model resource");
                var ops = catalog.ReadScriptOpcodes(542, entity, 2).ToArray();
                var ask = ops.First(op => op.Opcode == 0x48);
                // Echo-S voices the preceding sentence and shifts the choices from
                // lines 1..3 to 0..2. Read the actual ASK instead of assuming stock indices.
                Equal(3, ask.Bytes[5] - ask.Bytes[4] + 1, "the stand's ASK offers three choices");
                var choices = text.ReadMessagePagesById(542, ask.Bytes[3])
                    .SelectMany(page => page.Lines).Where(line => line.IsChoice).ToArray();
                Equal(3, choices.Length, "the native message marks three selectable lines");
                Equal(true, choices[^1].Text.Contains("Go back down", StringComparison.Ordinal),
                    $"{root}: the way out is a visible choice on stand {entity}");
                Equal(true, ops.Any(op => Convert.ToHexString(op.Bytes.ToArray()) == "0309C3"),
                    $"{root}: Contact {entity} requests AD's normal return script");
            }
            Equal(true, catalog.ReadScriptOpcodes(542, 9, 3)
                .Any(op => Convert.ToHexString(op.Bytes.ToArray()) == "601D02B4FF5E00260028"),
                $"{root}: the native return script goes to the ordinary observatory (541)");
            var departure = catalog.ReadScriptOpcodes(542, 9, 4).ToArray();
            Equal(true, departure.Any(op => Convert.ToHexString(op.Bytes.ToArray()) == "8230BC03"),
                $"{root}: AD's departure sets the Bugenhagen-aboard flag used to finish Story");
            Equal(true, departure.Any(op => Convert.ToHexString(op.Bytes.ToArray()) == "604A0000000000000000"),
                $"{root}: the initial departure transfers the party to the Highwind (74)");
            Equal(true, catalog.ReadScriptOpcodes(542, 10, 4)
                .Any(op => Convert.ToHexString(op.Bytes.ToArray()) == "0309C4"),
                $"{root}: Bugenhagen's departure calls that exact native completion script");
            Console.WriteLine($"PASS stored Huge Materia native menu exits: {root}");
        }
    }

    private static void RoutesFromEveryReportedStall(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            @"C:\Games\Final Fantasy VII\workingdir";
        var catalog = new FieldScriptNavigationCatalog(root);
        var memory = CreateStorageRoom();
        memory.SetBankByte(13, 82, 0x91);
        memory.SetBankByte(15, 145, 0x08);
        byte model = 1;
        foreach (var entity in new[] { 11, 12, 13, 14 })
        {
            var bytes = catalog.ReadScriptOpcodes(542, entity, 0)
                .First(op => op.Opcode == 0xA5).Bytes.ToArray();
            memory.ConfigureStand(entity, model++, BitConverter.ToInt16(bytes, 3),
                BitConverter.ToInt16(bytes, 5), BitConverter.ToInt16(bytes, 7), MateriaCollisionWidth);
        }
        // Exact stalled coordinates from the 0.10.2 tester log, 13:38:38..14:32:51.
        (int X, int Y)[] starts = [(-1226, -958), (-453, -961), (-2204, -1144),
            (-985, -959), (-461, -1012), (-281, -976), (-782, -1005)];
        var exits = new FieldStoryTargetReader(memory.ReadInt32, memory.ReadInt16, memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), _ => true).ReadExitTargets(StartingPosition());
        Equal(4, exits.Count, "every visible stored materia has its own named return approach");
        Equal(1, new FieldStoryTargetReader(memory.ReadInt32, memory.ReadInt16, memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), _ => true).ReadTargets(StartingPosition()).Count,
            "all four available stands still produce just one pending Story departure step");
        foreach (var (x, y) in starts)
            foreach (var exit in exits)
                TheInstalledWalkmeshCanBeWalkedIntoContactReach(createWalkmeshReader, exit,
                    StartingPosition() with { X = x, Y = y });
        Console.WriteLine("PASS 28 native menu approach routes from all seven reported stalls");
    }

    private static NativeMemory CreateStorageRoom()
    {
        var memory = new NativeMemory();
        memory.SetGameMoment(1391);
        memory.SetPlayerCollisionWidth(PlayerCollisionWidth);

        // 542:11:0 gates HUGEA on bank13[82] bit 4, and the row's other condition is
        // bank3[188] bit 3 being clear, which is this memory's default.
        memory.SetBankByte(13, 82, 0x10);
        memory.ConfigureStand(
            SecondStandEntity,
            modelId: 1,
            SecondStandX,
            SecondStandY,
            SecondStandZ,
            MateriaCollisionWidth);
        return memory;
    }

    private sealed class StraightRoutePlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight Contact test route";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return true;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(
                position.FieldId,
                $"{target.FieldId}:{target.StableId}",
                [position.TriangleId],
                [],
                new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z),
                position.TriangleId);
            return position.FieldId == target.FieldId;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
            return position.FieldId == target.FieldId;
        }
    }

    private sealed class NativeMemory
    {
        private const int EventTable = 0x02700000;
        private const int CollisionWidthOffset = 0x72;
        private readonly Dictionary<int, byte> bytes = [];

        public NativeMemory()
        {
            WriteInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
            bytes[FieldPositionReader.AddressFieldNumModels] = 16;
        }

        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        public void SetBankByte(int bank, int address, byte value)
        {
            var bankBase = bank switch
            {
                1 => FieldNavigationObjectReader.AddressFieldBankBase,
                3 => FieldNavigationObjectReader.AddressFieldBankBase + 0x100,
                11 => FieldNavigationObjectReader.AddressFieldBankBase + 0x200,
                13 => FieldNavigationObjectReader.AddressFieldBankBase + 0x300,
                15 => FieldNavigationObjectReader.AddressFieldBankBase + 0x400,
                _ => throw new InvalidOperationException($"unmapped bank {bank}")
            };
            bytes[bankBase + address] = value;
        }

        public void SetPlayerCollisionWidth(short width) =>
            WriteInt16(EventTable + CollisionWidthOffset, width);

        public void SetStandState(bool visible, bool solid)
        {
            var address = EventTable + FieldNavigationObjectReader.FieldEventDataStride;
            bytes[address + FieldNavigationObjectReader.VisibilityOffset] = visible ? (byte)1 : (byte)0;
            bytes[address + FieldNavigationNpcReader.CollisionDisabledOffset] = solid ? (byte)0 : (byte)1;
        }

        public void ConfigureStand(
            int entityId,
            byte modelId,
            int x,
            int y,
            int z,
            short collisionWidth)
        {
            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entityId] = modelId;
            var address = EventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
            bytes[address + FieldNavigationObjectReader.VisibilityOffset] = 1;
            WriteInt32(address + FieldNavigationObjectReader.PositionXOffset, x * 4096);
            WriteInt32(address + FieldNavigationObjectReader.PositionYOffset, y * 4096);
            WriteInt32(address + FieldNavigationObjectReader.PositionZOffset, z * 4096);
            WriteInt16(address + CollisionWidthOffset, collisionWidth);
        }

        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);

        public short ReadInt16(int address) =>
            unchecked((short)(ReadByte(address) | (ReadByte(address + 1) << 8)));

        public int ReadInt32(int address) =>
            ReadByte(address) |
            (ReadByte(address + 1) << 8) |
            (ReadByte(address + 2) << 16) |
            (ReadByte(address + 3) << 24);

        private void WriteInt16(int address, short value)
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
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}
