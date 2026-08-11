using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class ReportedNavigationRegressionTests
{
    public static void Run()
    {
        SelectionSummarySkipsAnAlreadyReachedOpeningWaypoint();
        ReactorEscapeDoorObjectivesStayActiveUntilNativeCompletion();
        RouteArrivalPauseUsesTheSameDistanceAsResumeHysteresis();
        PostCollapseAerisHouseStoryUsesTheNativeUpstairsState();
        Floor63StoryTracksTheNativeCouponAndDuctSequence();
        EncounterCountUsesTheNativeActiveEnemyMask();
    }

    private static void SelectionSummarySkipsAnAlreadyReachedOpeningWaypoint()
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            900,
            0,
            0,
            0,
            0,
            0,
            0);
        var target = new FieldNavigationTarget(
            900,
            FieldNavigationCategory.Exits,
            "Distant exit",
            120,
            0,
            0,
            "reported:distant-exit");
        var planner = new OpeningWaypointPlanner(
        [
            new FieldNavigationRouteStep(new FieldNavigationRouteWaypoint(0, 0, 0), 0, MustReach: true)
        ]);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target]),
            planner);

        var result = controller.HandleAction(
            FieldNavigationAction.RepeatTarget,
            position,
            new FieldNavigationControlTransform(-128));

        var speech = result?.Speech
            ?? throw new InvalidOperationException("the selected exit should produce a route summary");
        Require(
            !speech.Contains("at destination", StringComparison.OrdinalIgnoreCase),
            $"a distant target was falsely announced as reached: {speech}");
        Require(
            !speech.Contains("direction unavailable", StringComparison.OrdinalIgnoreCase),
            $"the route summary lost the next real waypoint: {speech}");
    }

    private static void RouteArrivalPauseUsesTheSameDistanceAsResumeHysteresis()
    {
        var triggerLine = new FieldNavigationTriggerLine(60, -10, 0, 80, 10, 0);
        var target = new FieldNavigationTarget(
            901,
            FieldNavigationCategory.Story,
            "Operate the route endpoint",
            200,
            0,
            0,
            "reported:route-arrival-hysteresis",
            CompletesOnArrival: false,
            TriggerLine: triggerLine);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target]),
            new RouteArrivalPlanner(triggerLine));
        var position = Position(901, 0, 0, 0);
        var noInput = new FieldNavigationInputSnapshot(0, FieldNavigationInput.None);
        var transform = new FieldNavigationControlTransform(-128);

        controller.HandleAction(FieldNavigationAction.NextCategory, position, transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, transform);
        var arrived = controller.UpdateLiveTracking(
            position,
            noInput,
            transform,
            isSuppressed: false,
            arrivalDistanceUnits: 80);
        Require(
            arrived?.Speech.Contains("Navigation paused", StringComparison.Ordinal) == true,
            "route-based arrival should pause at the trigger-line approach");

        var unchanged = controller.UpdateLiveTracking(
            position,
            noInput,
            transform,
            isSuppressed: false,
            arrivalDistanceUnits: 80);
        Require(
            unchanged is null,
            $"an unchanged route arrival immediately resumed: {unchanged?.Speech}");

        var backtracked = controller.UpdateLiveTracking(
            position with { X = -100 },
            noInput,
            transform,
            isSuppressed: false,
            arrivalDistanceUnits: 80);
        Require(
            backtracked?.Speech.Contains("Navigation resumed", StringComparison.Ordinal) == true,
            "genuine backtracking beyond route hysteresis should resume navigation");
    }

    private static void ReactorEscapeDoorObjectivesStayActiveUntilNativeCompletion()
    {
        var memory = new Dictionary<int, byte>();
        WriteUInt16(memory, FieldNavigationObjectReader.AddressFieldBankBase, 40);
        ConfigureVisibleModel(memory, entityId: 9, modelId: 1, x: -1335, y: 3367, z: -273);
        ConfigureVisibleModel(memory, entityId: 10, modelId: 2, x: -1836, y: 3644, z: -273);
        var reader = CreateStoryReader(memory);

        memory[FieldNavigationObjectReader.AddressFieldBankBase + 225] = 0x20;
        AssertOnlyStory(
            reader,
            Position(120, -1423, 4399, -283),
            "Talk to Jessie to reopen the inner security door",
            "late Reactor 1 escape before Jessie opens the inner door");

        memory[FieldNavigationObjectReader.AddressFieldBankBase + 225] = 0x30;
        AssertOnlyStory(
            reader,
            Position(120, -1423, 4399, -283),
            "Talk to Biggs to reopen the outer security door",
            "late Reactor 1 escape after Jessie opens the inner door");

        memory[FieldNavigationObjectReader.AddressFieldBankBase + 225] = 0x38;
        Require(
            reader.ReadTargets(Position(120, -1423, 4399, -283)).Count == 0,
            "Reactor 1 door objectives should disappear after both native completion bits are set");
    }

    private static void PostCollapseAerisHouseStoryUsesTheNativeUpstairsState()
    {
        var memory = new Dictionary<int, byte>();
        WriteUInt16(memory, FieldNavigationObjectReader.AddressFieldBankBase, 255);
        var reader = CreateStoryReader(memory);

        AssertOnlyStory(
            reader,
            Position(188, 0, 0, 0),
            "Go upstairs to Barret and Marlene",
            "post-collapse ground floor");
        AssertOnlyStory(
            reader,
            Position(190, 0, 0, 288),
            "Approach Barret and Marlene upstairs",
            "post-collapse upper floor");

        memory[PersistentBank(65)] = 0x01;
        AssertOnlyStory(
            reader,
            Position(190, 0, 0, 288),
            "Go downstairs after checking on Marlene",
            "after the upstairs reunion");
        AssertOnlyStory(
            reader,
            Position(188, 0, 0, 0),
            "Leave Aeris's house to plan her rescue",
            "after returning downstairs");
    }

    private static void Floor63StoryTracksTheNativeCouponAndDuctSequence()
    {
        var memory = new Dictionary<int, byte>();
        WriteUInt16(memory, FieldNavigationObjectReader.AddressFieldBankBase, 263);
        ConfigureVisibleModel(memory, entityId: 15, modelId: 1, x: 920, y: -570, z: 0);
        ConfigureVisibleModel(memory, entityId: 42, modelId: 2, x: -993, y: 83, z: 0);
        ConfigureVisibleModel(memory, entityId: 43, modelId: 3, x: -274, y: 83, z: 0);
        ConfigureVisibleModel(memory, entityId: 44, modelId: 4, x: 302, y: -31, z: 0);
        var reader = CreateStoryReader(memory);

        memory[PersistentBank(177)] = 0x10;
        AssertOnlyStory(reader, Position(245, 920, -570, 0),
            "Open coupon route door 1 of 3", "activated Floor 63 puzzle");

        memory[PersistentBank(174)] = 0x02;
        AssertOnlyStory(reader, Position(245, 400, 900, 0),
            "Open coupon route door 2 of 3", "after opening D2");

        memory[PersistentBank(174)] = 0x0A;
        AssertOnlyStory(reader, Position(245, -700, 300, 0),
            "Collect the A Coupon", "after opening D4");

        memory[PersistentBank(177)] = 0x12;
        AssertOnlyStory(reader, Position(245, -900, 100, 0),
            "Enter the A Coupon room duct", "after collecting A Coupon");
        AssertOnlyStory(reader, Position(246, -827, 124, 369),
            "Crawl to the shaft for the B Coupon room", "inside the duct after A Coupon");

        memory[PersistentBank(172)] = 0x40;
        AssertOnlyStory(reader, Position(245, 315, 73, 0),
            "Collect the B Coupon", "after dropping into the B Coupon room");

        memory[PersistentBank(177)] = 0x1A;
        AssertOnlyStory(reader, Position(245, 250, 0, 0),
            "Open coupon route door 3 of 3", "after collecting B Coupon");

        memory[PersistentBank(175)] = 0x08;
        AssertOnlyStory(reader, Position(245, 0, 0, 0),
            "Collect the C Coupon", "after opening D12");

        memory[PersistentBank(177)] = 0x1E;
        AssertOnlyStory(reader, Position(245, -200, 100, 0),
            "Enter the B Coupon room duct", "after collecting all coupons");
        AssertOnlyStory(reader, Position(246, 384, 123, 369),
            "Crawl to the floor 63 computer shaft", "inside the duct with all coupons");

        memory[PersistentBank(172)] = 0xC0;
        AssertOnlyStory(reader, Position(245, 900, -500, 0),
            "Exchange the Floor 63 coupons at the computer", "after returning to the computer");

        memory[PersistentBank(181)] = 0x80;
        var remaining = reader.ReadTargets(Position(245, 900, -500, 0));
        Require(
            remaining.All(target => target.Label != "Exchange the Floor 63 coupons at the computer"),
            "the exchange objective remained active after the native completion bit was set");
    }

    private static void EncounterCountUsesTheNativeActiveEnemyMask()
    {
        var memory = CreateBattleMemory();
        AddEnemy(memory, actorIndex: 4, sceneIndex: 0, name: "Sweeper");
        AddEnemy(memory, actorIndex: 5, sceneIndex: 0, name: "Sweeper");
        const int nativeActiveEnemyMaskAddress = 0x009AB0BA;
        WriteUInt16(memory, nativeActiveEnemyMaskAddress, 1 << 4);

        var encounter = CreateBattleReader(memory).ReadEncounter();

        Require(encounter.IsValid, "the native one-enemy encounter should be readable");
        Require(
            encounter.Enemies.Count == 1,
            $"native active mask contained one enemy but {encounter.Enemies.Count} were reported");
    }

    private static FieldStoryTargetReader CreateStoryReader(Dictionary<int, byte> memory)
    {
        byte ReadByte(int address) => memory.GetValueOrDefault(address);
        short ReadInt16(int address) => unchecked((short)(ReadByte(address) | ReadByte(address + 1) << 8));
        int ReadInt32(int address) =>
            ReadByte(address) |
            ReadByte(address + 1) << 8 |
            ReadByte(address + 2) << 16 |
            ReadByte(address + 3) << 24;
        return new FieldStoryTargetReader(
            ReadInt32,
            ReadInt16,
            ReadByte,
            FieldStoryEventCatalog.CreateAllFields());
    }

    private static BattleStateReader CreateBattleReader(Dictionary<int, byte> memory)
    {
        byte ReadByte(int address) => memory.TryGetValue(address, out var value) ? value : byte.MaxValue;
        ushort ReadUInt16(int address) => (ushort)(ReadByte(address) | ReadByte(address + 1) << 8);
        int ReadInt32(int address) =>
            ReadByte(address) |
            ReadByte(address + 1) << 8 |
            ReadByte(address + 2) << 16 |
            ReadByte(address + 3) << 24;
        return new BattleStateReader(
            ReadByte,
            ReadUInt16,
            ReadInt32,
            new SavemapPartyReader(ReadByte),
            (_, _) => true);
    }

    private static Dictionary<int, byte> CreateBattleMemory()
    {
        var memory = new Dictionary<int, byte>
        {
            [BattleStateReader.AddressCurrentModule] = BattleStateReader.BattleModule,
            [BattleStateReader.AddressBattleLayoutType] = 0,
            [SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset] = 0,
            [BattleStateReader.AddressBattleActors + BattleStateReader.ActorInstanceIdOffset] = 0
        };
        WriteUInt16(memory, BattleStateReader.AddressBattleFormationId, 37);
        WriteUInt32(memory, BattleStateReader.AddressBattleActors + BattleStateReader.ActorCurrentHpOffset, 300);
        WriteUInt32(memory, BattleStateReader.AddressBattleActors + BattleStateReader.ActorMaxHpOffset, 350);
        WriteFf7Text(
            memory,
            SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset + SavemapPartyReader.CharacterNameOffset,
            "Cloud",
            12);
        return memory;
    }

    private static void AddEnemy(
        Dictionary<int, byte> memory,
        int actorIndex,
        byte sceneIndex,
        string name)
    {
        var enemySlot = actorIndex - 4;
        memory[BattleStateReader.AddressEnemySceneIndexRecords + enemySlot * BattleStateReader.EnemySceneIndexRecordSize] = sceneIndex;
        WriteFf7Text(
            memory,
            BattleStateReader.AddressEnemyData + sceneIndex * BattleStateReader.EnemyDataSize,
            name,
            BattleStateReader.EnemyNameLength);
        var actorBase = BattleStateReader.AddressBattleActors + actorIndex * BattleStateReader.BattleActorSize;
        memory[actorBase + BattleStateReader.ActorInstanceIdOffset] = sceneIndex;
        WriteUInt32(memory, actorBase + BattleStateReader.ActorCurrentHpOffset, 140);
        WriteUInt32(memory, actorBase + BattleStateReader.ActorMaxHpOffset, 140);
    }

    private static void ConfigureVisibleModel(
        Dictionary<int, byte> memory,
        int entityId,
        byte modelId,
        int x,
        int y,
        int z)
    {
        const int eventTable = 0x02500000;
        WriteUInt32(memory, FieldNavigationObjectReader.AddressFieldEventDataPtr, eventTable);
        memory[FieldPositionReader.AddressFieldNumModels] = 8;
        memory[FieldNavigationObjectReader.AddressFieldModelIdArray + entityId] = modelId;
        var eventAddress = eventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
        memory[eventAddress + FieldNavigationObjectReader.VisibilityOffset] = 1;
        WriteUInt32(memory, eventAddress + FieldNavigationObjectReader.PositionXOffset,
            unchecked((uint)(x * FieldNavigationObjectReader.ModelPositionFixedPointScale)));
        WriteUInt32(memory, eventAddress + FieldNavigationObjectReader.PositionYOffset,
            unchecked((uint)(y * FieldNavigationObjectReader.ModelPositionFixedPointScale)));
        WriteUInt32(memory, eventAddress + FieldNavigationObjectReader.PositionZOffset,
            unchecked((uint)(z * FieldNavigationObjectReader.ModelPositionFixedPointScale)));
    }

    private static FieldPositionSnapshot Position(int fieldId, int x, int y, int z) =>
        new(FieldPositionReader.FieldModule, fieldId, 0, x, y, z, 0, 0);

    private static int PersistentBank(int address) =>
        FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + address;

    private static void AssertOnlyStory(
        FieldStoryTargetReader reader,
        FieldPositionSnapshot position,
        string expected,
        string phase)
    {
        var labels = reader.ReadTargets(position).Select(target => target.Label).ToArray();
        Require(
            labels.Length == 1 && labels[0] == expected,
            $"{phase}: expected only '{expected}', got '{string.Join(" | ", labels)}'");
    }

    private static void WriteFf7Text(
        Dictionary<int, byte> memory,
        int address,
        string text,
        int length)
    {
        for (var index = 0; index < length; index++)
        {
            memory[address + index] = byte.MaxValue;
        }

        for (var index = 0; index < Math.Min(text.Length, length - 1); index++)
        {
            memory[address + index] = text[index] == ' '
                ? (byte)0
                : (byte)(text[index] - 0x20);
        }
    }

    private static void WriteUInt16(Dictionary<int, byte> memory, int address, int value)
    {
        memory[address] = (byte)value;
        memory[address + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(Dictionary<int, byte> memory, int address, uint value)
    {
        memory[address] = (byte)value;
        memory[address + 1] = (byte)(value >> 8);
        memory[address + 2] = (byte)(value >> 16);
        memory[address + 3] = (byte)(value >> 24);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class OpeningWaypointPlanner(IReadOnlyList<FieldNavigationRouteStep> steps)
        : IFieldNavigationRoutePlanner, IFieldNavigationCorridorLookaheadPlanner
    {
        public string LastDiagnostic => "reported false-arrival regression route";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return position.FieldId == 900;
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
                position.TriangleId,
                StableWaypointsOverride: steps);
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

        public bool TryObserveCorridor(
            FieldPositionSnapshot position,
            FieldNavigationRoutePlan plan,
            IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
            int waypointIndex,
            FieldNavigationRouteAction? nextAction,
            FieldNavigationRouteHeading heading,
            out FieldNavigationCorridorObservation observation)
        {
            observation = new FieldNavigationCorridorObservation(
                position.TriangleId,
                plan.FinalApproach,
                0,
                FieldNavigationLookaheadMode.HeadingHeld,
                true,
                "live corridor reaches the distant exit");
            return position.FieldId == plan.FieldId;
        }
    }

    private sealed class RouteArrivalPlanner(FieldNavigationTriggerLine triggerLine)
        : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "reported route-arrival hysteresis";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return position.FieldId == 901;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(
                position.FieldId,
                target.StableId,
                [position.TriangleId],
                [],
                new FieldNavigationRouteWaypoint(70, 0, 0),
                position.TriangleId,
                TargetTriggerLine: triggerLine);
            return position.FieldId == 901 && target.FieldId == 901;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(70, 0, 0);
            return position.FieldId == 901 && target.FieldId == 901;
        }
    }
}
