using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Cosmo Canyon's storage room, cosmo3 (field 542), is reached by walking into a Huge
/// Materia rather than by pressing anything: entity script 2 is its Contact script and
/// there is no Talk script on those entities at all. FUN_00637724 accepts the touch only
/// while the squared horizontal distance is below the square of half the sum of the two
/// collision widths and the height difference is strictly inside (-127, 128), which is a
/// much smaller and differently shaped reach than Talk's player-radius-plus-talk-radius.
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
    private const string SecondStandLabel = "Go to the second stored materia";

    // The measured native widths in that room: the party leader is 34 wide and the
    // materia on its stand is 1, so Contact accepts (34 + 1) / 2 = 17 and Talk would
    // have accepted 34.
    private const short PlayerCollisionWidth = 34;
    private const short MateriaCollisionWidth = 1;
    private const int ContactReach = 17;
    private const int TalkReachIfItHadBeenTalk = 34;

    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        EachStandIsOfferedOnlyUnderItsOwnInstalledGate();
        var target = TheReaderCarriesTheNativeContactReachAndKind();
        TheInstalledWalkmeshCanBeWalkedIntoContactReach(createWalkmeshReader, target);
        ArrivalUsesContactShapeAndNeverAsksForAButton(target);
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
            (12, 15, 145, 0x08, "Go to the first stored materia"),
            (11, 13, 82, 0x10, "Go to the second stored materia"),
            (13, 13, 82, 0x01, "Go to the third stored materia"),
            (14, 13, 82, 0x80, "Go to the fourth stored materia")
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

            var target = SingleStoryTarget(memory, StartingPosition());
            Equal(stand.Label, target.Label, $"the stand lit by bank{stand.Bank}[{stand.Address}]");
            Equal(stand.Entity, target.TriggerEntityId, $"{stand.Label} names its own entity");
            Equal(
                FieldNavigationActivation.Contact,
                target.Activation,
                $"{stand.Label} is a Contact");
        }
    }

    private static FieldNavigationTarget TheReaderCarriesTheNativeContactReachAndKind()
    {
        var memory = CreateStorageRoom();
        var target = SingleStoryTarget(memory, StartingPosition());

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
            "Contact reach is half the sum of the two collision widths");
        Equal(
            false,
            target.InteractionRadius == TalkReachIfItHadBeenTalk,
            "Talk's reach would have been twice as far and is not what this row uses");
        return target;
    }

    private static void TheInstalledWalkmeshCanBeWalkedIntoContactReach(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        FieldNavigationTarget target)
    {
        var start = StartingPosition();
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
        // Standing 16 units away, inside Contact reach.
        var touching = PositionNear(target, offsetX: 16, offsetZ: 0);
        var speech = TrackedSpeech(target, touching);
        Equal(
            true,
            speech.Contains("Walk into it", StringComparison.Ordinal),
            $"a Contact target has to say what actually triggers it, got '{speech}'");
        Equal(
            false,
            speech.Contains("Interact here", StringComparison.Ordinal),
            $"a Contact target must not fall back to the generic interaction prompt, got '{speech}'");
        Equal(
            false,
            speech.Contains("Confirm", StringComparison.OrdinalIgnoreCase),
            $"nothing is pressed at a Contact, got '{speech}'");

        // 25 units away is outside Contact reach and well inside what Talk would have
        // accepted. Stopping here would leave the player standing beside a materia that
        // never reacts.
        Equal(
            "",
            TrackedSpeech(target, PositionNear(target, offsetX: 25, offsetZ: 0)),
            "Talk's reach must not release the route short of the native Contact reach");

        // The height band is its own test and is not symmetric.
        Equal(
            "",
            TrackedSpeech(target, PositionNear(target, offsetX: 8, offsetZ: 200)),
            "a stand 200 units below the party is out of the native Contact band");
        Equal(
            "",
            TrackedSpeech(target, PositionNear(target, offsetX: 8, offsetZ: -127)),
            "the lower bound of the native Contact band is exclusive");
        Equal(
            true,
            TrackedSpeech(target, PositionNear(target, offsetX: 8, offsetZ: -126))
                .Contains("Walk into it", StringComparison.Ordinal),
            "one unit inside the lower bound is a touch the game accepts");
    }

    private static string TrackedSpeech(
        FieldNavigationTarget target,
        FieldPositionSnapshot position)
    {
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource(
                Array.Empty<FieldNavigationTarget>(),
                storyTargetProvider: _ => [target]),
            new StraightRoutePlanner());
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
        return update?.Speech ?? "";
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

    private static FieldNavigationTarget SingleStoryTarget(
        NativeMemory memory,
        FieldPositionSnapshot position)
    {
        var targets = new FieldStoryTargetReader(
            memory.ReadInt32,
            memory.ReadInt16,
            memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), _ => true).ReadTargets(position);
        Equal(1, targets.Count, "exactly the lit stand is offered in the storage room");
        return targets[0];
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
