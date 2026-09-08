using Ff7.Accessibility.Reloaded;

internal static class CargoShipNavigationTests
{
    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        RunNativeCollisionProbeTests();
        RunNativeCargoRoutes(createWalkmeshReader);
        InitialModelBypassPreservesRequiredDetours(createWalkmeshReader);
        ChestStopsInsideItsNativeTalkRange(createWalkmeshReader);
        TalkArrivalKeepsNativeEligibilityAndLegacyObjectRanges();
        NativeCargoRoutesCarryAutomaticInputPastTheModels(createWalkmeshReader);
    }

    private static void TalkArrivalKeepsNativeEligibilityAndLegacyObjectRanges()
    {
        var memory = new CargoNativeMemory();
        var definition = FieldNavigationObjectCatalog.CreateAllFields()
            .Single(item => item.FieldId == 440 && item.EntityId == 14);
        var position = Position(342, -564, 275, 84);
        FieldNavigationObjectReader Reader(FieldNavigationObjectDefinition item) => new(
            memory.ReadInt32, memory.ReadByte, _ => "Wind Slash", _ => null, [item]);

        Equal(48, Reader(definition with { UsesTalkInteraction = false })
                .ReadTargets(position).Single().InteractionRadius,
            "catalog entries without a proven Talk script keep the existing object range");
        Equal(48, Reader(definition with { TargetKind = FieldNavigationObjectTargetKind.Location })
                .ReadTargets(position).Single().InteractionRadius,
            "location pickups do not inherit a model's Talk radius");
        Equal(0, Reader(definition).ReadTargets(position with { ModelIndex = 14 }).Count,
            "Talk range requires a valid live player model");
        memory.SetTalkDisabled(10, true);
        Equal(0, Reader(definition).ReadTargets(position).Count,
            "a native TALK-disabled chest is not offered as an available interaction");
        memory.SetTalkDisabled(10, false);
        Equal(1, Reader(definition).ReadTargets(position).Count,
            "native Talk re-enabling restores the uncollected chest");
        memory.SetCollected(definition);
        Equal(0, Reader(definition).ReadTargets(position).Count,
            "native collection still withdraws the chest regardless of its Talk range");
    }

    private static void InitialModelBypassPreservesRequiredDetours(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var position = Position(308, 152, 276, 59);
        var checkpoint = new FieldNavigationRouteWaypoint(360, -300, 276);
        var target = ChestTarget() with
        {
            RouteDetour = new(new(306, -200, 276, 323, -200, 276),
                checkpoint.X, checkpoint.Y, checkpoint.Z, 5)
        };
        // Add a synthetic model before a required script-avoidance checkpoint
        // on the installed walkway; a future clear bypass cannot skip it.
        FieldNavigationDynamicObstacle[] obstacles = [new(7, 322, 0, 276, 32, 34)];
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(440),
            dynamicObstacleProvider: (_, _) => obstacles);
        Equal(true, planner.TryBuildRoute(position, target, out var plan),
            $"a model bypass can rejoin the first required detour: {planner.LastDiagnostic}");
        Equal(true, plan.StableWaypointsOverride?.Any(step =>
                step.MustReach && step.Waypoint == checkpoint) == true,
            "a clear future rejoin must not discard an earlier required script-avoidance checkpoint");
    }

    private static void NativeCargoRoutesCarryAutomaticInputPastTheModels(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var scripts = new FieldScriptNavigationCatalog(
            Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")!);
        foreach (var stepLength in new[] { 4, 8 })
        foreach (var (start, isChest) in new[]
        {
            (Position(308, 152, 275, 59), true), (Position(308, -440, 275, 4), true),
            (Position(342, -564, 275, 84), false), (Position(397, -649, 275, 1), false)
        })
        {
            var memory = new CargoNativeMemory();
            var target = isChest ? ChestTarget() with { InteractionRadius = 114 } : new(
                440, FieldNavigationCategory.Story, "Leave the engine room for docking",
                -1, -669, 105, CompletesOnArrival: true,
                TriggerLine: new(-55, -670, 106, 54, -668, 104));
            var reader = createWalkmeshReader(440);
            var mesh = reader.Read(start).Walkmesh!;
            var planner = new FieldWalkmeshRoutePlanner(reader,
                transitionProvider: field => scripts.ReadField(field).Transitions,
                dynamicObstacleProvider: memory.Obstacles.Read);
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), planner);
            var transform = new FieldNavigationControlTransform(-128);
            while (controller.CurrentCategory != target.Category)
            {
                controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
            }

            controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
            var position = start;
            var lastInput = FieldNavigationInput.None;
            var completed = false;
            for (var sample = 0; sample < 700; sample++)
            {
                controller.UpdateLiveTracking(position, new(0, lastInput), transform, false, 80,
                    observedAt: DateTime.UnixEpoch.AddMilliseconds(sample * 34));
                var scenario = $"cargo start={start.X},{start.Y}, step={stepLength}, sample={sample}, position={position.X},{position.Y}";
                if (!controller.TryResolveAutomaticInput(position, transform, 80, out var input))
                {
                    var distance = Math.Sqrt(Math.Pow(position.X - target.X, 2) + Math.Pow(position.Y - target.Y, 2));
                    Equal(true, isChest && distance <= target.InteractionRadius,
                        $"{scenario} may stop only inside the chest's native Talk range: {controller.LastNavigationDiagnostic}");
                    completed = true;
                    break;
                }

                var (unitX, unitY) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
                var next = new FieldNavigationRouteWaypoint(
                    position.X + (int)Math.Round(unitX * stepLength),
                    position.Y + (int)Math.Round(unitY * stepLength), position.Z);
                var current = new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z);
                Equal(true, planner.TryResolvePlayerTriangle(position, out var triangle), $"{scenario} resolves natively");
                var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(mesh, triangle, current, next);
                Equal(true, trace.IsClear, $"{scenario}: actual {input} step stays on the walkmesh: {trace.Diagnostic}");
                Equal(false, FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                        current, next, memory.Obstacles.Read(position, null)),
                    $"{scenario}: actual {input} step must clear all native forward probes, including the target chest");
                position = position with { X = next.X, Y = next.Y, TriangleId = (ushort)trace.EndTriangle };
                lastInput = input;
                if (!isChest && position.Y > -390)
                {
                    Equal(FieldNavigationTransitionKind.Ladder, controller.CurrentRouteGuidance?.NextAction?.Kind,
                        "the return retains the required native ladder action after clearing the soldier");
                    Equal(true, controller.CurrentRouteGuidance?.NextAction?.RequiresAction,
                        "walking around the soldier must not complete the ladder action");
                    completed = true;
                    break;
                }
            }

            Equal(true, completed, $"cargo start {start.X},{start.Y} continues through every bypass bend");
        }
    }

    internal static void ChestStopsInsideItsNativeTalkRange(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = new CargoNativeMemory();
        var definition = FieldNavigationObjectCatalog.CreateAllFields()
            .Single(item => item.FieldId == 440 && item.EntityId == 14);
        var reader = new FieldNavigationObjectReader(memory.ReadInt32, memory.ReadByte,
            _ => "Wind Slash", _ => null, [definition]);
        // Cloud opened TAKARA from here at 22:58:56Z and received Wind Slash at
        // 22:58:58Z. The fixed 48-unit object radius drives through this position.
        var position = Position(342, -564, 275, 84);
        var target = reader.ReadTargets(position).Single();
        Equal(114, target.InteractionRadius, "a generated Talk pickup uses native player 34 plus target Talk 80");
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]),
            new FieldWalkmeshRoutePlanner(createWalkmeshReader(440),
                dynamicObstacleProvider: memory.Obstacles.Read));
        var transform = new FieldNavigationControlTransform(-128);
        while (controller.CurrentCategory != FieldNavigationCategory.Objects)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, position, transform);
        }

        controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, transform);
        controller.UpdateLiveTracking(position, new(0, FieldNavigationInput.None), transform, false, 80);
        Equal(false, controller.TryResolveAutomaticInput(position, transform, 80, out _),
            "automatic movement stops at the recorded successful chest interaction position");
    }

    internal static void RunNativeCargoRoutes(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = new CargoNativeMemory();
        var scripts = new FieldScriptNavigationCatalog(
            Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
                ?? throw new InvalidOperationException("Cargo route tests need the installed data root."));
        var cases = new[]
        {
            (Start: Position(308, 152, 276, 59), Target: ChestTarget()),
            (Start: Position(397, -649, 275, 1), Target: new FieldNavigationTarget(
                440, FieldNavigationCategory.Story, "Leave the engine room for docking",
                -1, -669, 105, CompletesOnArrival: true,
                TriggerLine: new(-55, -670, 106, 54, -668, 104)))
        };
        foreach (var (position, target) in cases)
        {
            var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(440),
                transitionProvider: field => scripts.ReadField(field).Transitions,
                dynamicObstacleProvider: memory.Obstacles.Read);
            Equal(true, planner.TryBuildRoute(position, target, out var plan),
                $"the logged cargo start {position.X},{position.Y} has a collision-clear route: {planner.LastDiagnostic}");
        }
    }

    internal static void RunNativeCollisionProbeTests()
    {
        var memory = new CargoNativeMemory();
        var position = Position(308, -440, 276, 4);
        var obstacles = memory.Obstacles.Read(position, ChestTarget());
        Equal(1, obstacles.Count, "the active chest is excluded while the engine-room soldier remains solid");
        Equal(32d, obstacles[0].ClearanceRadius,
            "native collision keeps half of Cloud's ladder-restored 34 plus the soldier's default 30");

        // 2026-09-05 22:58:38-22:58:50Z: Down repeatedly stalls here. Native
        // FUN_00636c41 tests probes a player radius ahead at heading and +/-45
        // degrees, using FUN_00637724's half-sum clearance for each probe.
        Equal(true, FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                new(308, -440, 276), new(308, -444, 276), obstacles),
            "the next Down step hits the soldier with Cloud's forward-right native probe");
        Equal(false, FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                new(308, -440, 276), new(308, -424, 276), obstacles),
            "retreating from the live stop must remain possible");
        Equal(false, FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                new(421, -440, 276), new(421, -540, 276), obstacles),
            "the manually demonstrated passage to the soldier's right must remain clear");
        Equal(true, FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                new(379, -543, 275), new(375, -543, 275), obstacles),
            "a first Left step from the return corridor collides with the native upper-left probe");
        Equal(true, FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                new(379, -543, 275), new(-302, -539, 275), obstacles),
            "a distant clear endpoint cannot waive an already colliding native forward probe");

        memory.SetCollisionDisabled(7, true);
        Equal(0, memory.Obstacles.Read(position, ChestTarget()).Count,
            "native SOLID-disabled models do not block the route");
    }

    private static FieldNavigationTarget ChestTarget() => new(
        440, FieldNavigationCategory.Objects, "Wind Slash", 332, -629, 285,
        TriggerEntityId: 14, InteractionRadius: 48);

    private static FieldPositionSnapshot Position(int x, int y, int z, ushort triangle) =>
        new(FieldPositionReader.FieldModule, 440, 0, x, y, z, triangle, 0);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
        }
    }

    private sealed class CargoNativeMemory
    {
        private const int EventTable = 0x03000000;
        private readonly Dictionary<int, byte> bytes = [];

        internal CargoNativeMemory()
        {
            WriteInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
            bytes[FieldPositionReader.AddressFieldNumModels] = 14;
            for (var model = 0; model < 14; model++)
            {
                SetCollisionDisabled(model, true);
            }

            // shpin_3: scale 512, CLOUD ladder scripts restore SLIDR 34;
            // DEADA CHAR7 and TAKARA CHAR10 retain default collision/Talk 30/80.
            SetModel(0, 308, -440, 276, 34);
            SetModel(7, 351, -491, 286, 30);
            SetModel(10, 332, -629, 285, 30);
            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + 14] = 10;
            Obstacles = new(ReadInt32, ReadInt16, ReadByte);
        }

        internal FieldNavigationDynamicObstacleReader Obstacles { get; }

        internal void SetCollisionDisabled(int model, bool disabled) =>
            bytes[EventTable + model * FieldNavigationObjectReader.FieldEventDataStride +
                  FieldNavigationDynamicObstacleReader.CollisionDisabledOffset] = disabled ? (byte)1 : (byte)0;

        internal void SetTalkDisabled(int model, bool disabled) =>
            bytes[EventTable + model * FieldNavigationObjectReader.FieldEventDataStride +
                  FieldNavigationNpcReader.TalkDisabledOffset] = disabled ? (byte)1 : (byte)0;

        internal void SetCollected(FieldNavigationObjectDefinition definition) =>
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + definition.CollectedAddress] =
                definition.CollectedMask;

        private void SetModel(int model, int x, int y, int z, short collisionRadius)
        {
            var address = EventTable + model * FieldNavigationObjectReader.FieldEventDataStride;
            WriteInt32(address + FieldNavigationObjectReader.PositionXOffset, x * 4096);
            WriteInt32(address + FieldNavigationObjectReader.PositionYOffset, y * 4096);
            WriteInt32(address + FieldNavigationObjectReader.PositionZOffset, z * 4096);
            WriteInt16(address + FieldNavigationNpcReader.CollisionRadiusOffset, collisionRadius);
            WriteInt16(address + FieldNavigationNpcReader.TalkRadiusOffset, 80);
            bytes[address + FieldNavigationObjectReader.VisibilityOffset] = 1;
            SetCollisionDisabled(model, false);
        }

        internal byte ReadByte(int address) => bytes.GetValueOrDefault(address);
        private short ReadInt16(int address) => (short)(ReadByte(address) | ReadByte(address + 1) << 8);
        internal int ReadInt32(int address) => ReadByte(address) | ReadByte(address + 1) << 8 |
            ReadByte(address + 2) << 16 | ReadByte(address + 3) << 24;
        private void WriteInt16(int address, short value)
        {
            bytes[address] = (byte)value;
            bytes[address + 1] = (byte)(value >> 8);
        }
        private void WriteInt32(int address, int value)
        {
            for (var offset = 0; offset < 4; offset++)
            {
                bytes[address + offset] = (byte)(value >> (offset * 8));
            }
        }
    }
}
