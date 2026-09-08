using Ff7.Accessibility.Reloaded;

internal static class MountCorelNavigationTests
{
    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        NativeCheeringAnimationCannotBecomeThePlayersTrackJump(createWalkmeshReader);
        StoryFollowsTheNativeUpperAndLowerTrackStages();
        StoryExitsRequireTheNativeGatewayOrLine();
        SwitchArrivalReachesTheNativeInteractionRange();
        BirdClimbIsOfferedOnlyWithTheNativeAudibleCue();
        SameFieldTrackWrapRetainsTheSwitchObjective(createWalkmeshReader);
        OptionalCaveAndReturnUseReachableNativeGateways(createWalkmeshReader);
        NativeApproachAndLongBridgeRoutesReachTheirActualExits(createWalkmeshReader);
    }

    internal static void NativeCheeringAnimationCannotBecomeThePlayersTrackJump(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var scripts = Scripts();
        AssertOpcode(scripts, 464, 12, 2, 4, [0x06, 0x00, 0xC5],
            "the upper track calls party slot zero, script five");
        AssertOpcode(scripts, 464, 16, 5, 5,
            [0xA5, 0, 0, 0x11, 0x0A, 0xFB, 0xFA, 0x07, 0x04, 0xF7, 0],
            "Cloud wraps to the other upper track at 2577,-1285,1031, triangle247");
        AssertOpcode(scripts, 464, 17, 5, 7,
            [0xC0, 0, 0, 0xD5, 0xF9, 0xF1, 0, 0xCF, 0, 10, 0],
            "Aeris's unrelated cheering jump remains native script evidence");

        var navigation = scripts.ReadField(464);
        Equal(false, navigation.Transitions.Any(transition =>
                transition.StableId == "jump:464:12:17:5:207"),
            "an NPC cheering animation must not send Cloud from the upper track onto the lower track");
        Equal(true, navigation.Transitions.Any(transition =>
                transition.StableId == "ladder:464:6:16:3:322" &&
                transition.RequiredInput == FieldNavigationInput.Up),
            "rejecting the cheering animation preserves the native bird-nest climb");
        Equal(true, scripts.ReadField(458).Transitions.Any(transition =>
                transition.StableId == "jump:458:4:6:3:103"),
            "the ordinary Mount Corel approach jump remains available");

        var position = Position(464, -3053, 976, 756, 138);
        var memory = new MountMemory(464, lowered: true);
        var planner = Planner(createWalkmeshReader(464), scripts, memory);
        var target = new FieldNavigationTarget(464, FieldNavigationCategory.Story,
            "Long bridge", 4802, -3321, 540,
            TriggerLine: new(4788, -3236, 540, 4815, -3406, 540));
        Equal(false, planner.TryBuildRoute(position, target, out _),
            "the upper track cannot bypass its return through field462 using the NPC jump");
    }

    private static void StoryFollowsTheNativeUpperAndLowerTrackStages()
    {
        var memory = new MountMemory(464);
        var reader = memory.StoryReader();
        var upper = Position(464, -3053, 976, 756, 138);
        var lower = Position(464, -1911, 143, 358, 151);
        var switchTarget = Single(reader, upper);
        Equal("Lower the railway bridge at the cabin switch", switchTarget.Label,
            "the upper track's first objective is the still-raised bridge");
        Equal(new FieldNavigationTriggerLine(2815, -2162, 1070, 2721, -2012, 1070),
            switchTarget.TriggerLine!.Value, "the switch objective uses native border4's Go1x line");
        Equal(false, switchTarget.CompletesOnArrival,
            "reaching the switch is not completion before the player accepts its native choice");
        Equal("Return to the track junction for the upper route", Single(reader, lower).Label,
            "the blocked lower entry must send the player back to the upper route");

        memory.SetLowered(true);
        Equal("Return to the track junction for the lowered bridge", Single(reader, upper).Label,
            "lowering the bridge does not connect the upper track directly to the lower exit");
        Equal("Cross the lowered railway bridge", Single(reader, lower).Label,
            "native bit3[222].5 opens the onward lower route");
        Equal(2, reader.ReadTargets(lower).Count,
            "the visible lower-track side path is optional alongside the main route");
        Equal("Cross the lowered railway bridge",
            Single(reader, Position(464, 2666, -1870, 540, 9)).Label,
            "the lower same-field wrap retains the onward stage");

        foreach (var triangle in new ushort[] { 322, 323 })
            Equal(0, reader.ReadTargets(upper with { TriangleId = triangle }).Count,
                "the nest ladder's locked handoff must not advertise an ordinary track target");

        memory.SetField(462);
        var fork = Position(462, -2321, -5, 1283, 227);
        Equal("Take the lower tracks to the lowered bridge", Single(reader, fork).Label,
            "the return to the rail junction selects its native lower gateway");
        memory.SetLowered(false);
        Equal("Take the upper tracks to the bridge switch", Single(reader, fork).Label,
            "before the switch, Story selects the upper gateway");

        foreach (var moment in new[] { 421, 427, 1596 })
        {
            memory.SetGameMoment(moment);
            foreach (var field in new[] { 458, 459, 460, 461, 462, 464, 465, 467 })
                Equal(0, reader.ReadTargets(Position(field)).Count,
                    $"first-visit Mount Corel objectives must not leak into moment {moment}, field {field}");
        }
    }

    private static void StoryExitsRequireTheNativeGatewayOrLine()
    {
        var memory = new MountMemory(458);
        var reader = memory.StoryReader();
        foreach (var (field, line, label) in new[]
        {
            (458, new FieldNavigationTriggerLine(-62,1487,545,17,1487,546), "Continue up Mount Corel"),
            (459, new FieldNavigationTriggerLine(5,2193,504,-12,2978,589), "Continue toward the Corel reactor"),
            (460, new FieldNavigationTriggerLine(157,-2318,-600,285,-2318,-600), "Continue past the Corel reactor"),
            (461, new FieldNavigationTriggerLine(1747,124,551,1881,-129,551), "Continue onto the railway tracks"),
            (465, new FieldNavigationTriggerLine(-26,445,-44,57,538,-44), "Return from the miner's cave"),
            (467, new FieldNavigationTriggerLine(-92,-6898,1008,92,-6898,1008), "Continue into North Corel")
        })
        {
            memory.SetField(field);
            var target = Single(reader, Position(field));
            Equal(label, target.Label, $"field{field} publishes its native forward stage");
            Equal(line, target.TriggerLine!.Value, $"field{field} aims at the actual traversal trigger");
            Equal(true, target.CompletesOnArrival, $"field{field} is an actual exit traversal");
        }
        memory.SetField(467);
        memory.LineEnabled = false;
        Equal(0, reader.ReadTargets(Position(467)).Count,
            "the long bridge exit requires the native border1 LINE to be enabled");
    }

    private static void SameFieldTrackWrapRetainsTheSwitchObjective(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = new MountMemory(464);
        var reader = memory.StoryReader();
        var scripts = Scripts();
        var start = Position(464, -3053, 976, 756, 138);
        var landing = Position(464, 2577, -1285, 1031, 247);
        var target = Single(reader, start);
        Equal(target.StableId, Single(reader, landing).StableId,
            "Cloud's native XYZI wrap must not change the switch objective");
        var planner = Planner(createWalkmeshReader(464), scripts, memory);
        Equal(true, planner.TryBuildRoute(start, target, out var initial),
            $"the native upper corridor reaches the switch: {planner.LastDiagnostic}");
        Equal(false, initial.Portals.Any(portal => portal.TransitionKind is not null),
            "the upper corridor must not depend on a fabricated jump action");

        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), planner);
        var transform = new FieldNavigationControlTransform(0);
        while (controller.CurrentCategory != FieldNavigationCategory.Story)
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        controller.UpdateLiveTracking(start, new(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: DateTime.UnixEpoch);
        controller.UpdateLiveTracking(landing, new(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: DateTime.UnixEpoch.AddSeconds(1));
        Equal(true, controller.TryResolveAutomaticInput(landing, transform, 80, out var input),
            $"the native same-field wrap must resume toward the still-pending switch: {controller.LastNavigationDiagnostic}");
        Equal(false, input == FieldNavigationInput.None,
            "the first steering sample after the native upper-track wrap is directional");
        AssertOpcode(scripts, 464, 16, 5, 20, [0xA8,0,0x0E,0x0B,0x01,0xF9],
            "the native wrap continues with MOVE2830,-1791 before restoring control");
        var released = Position(464, 2830, -1791, 1070, 251);
        controller.UpdateLiveTracking(released, new(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: DateTime.UnixEpoch.AddSeconds(2));
        Equal(true, controller.TryResolveAutomaticInput(released, transform, 80, out input),
            "walking resumes after the script's actual final MOVE, not just its initial XYZI");
        var (unitX, unitY) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
        var next = new FieldNavigationRouteWaypoint(released.X + (int)Math.Round(unitX * 8),
            released.Y + (int)Math.Round(unitY * 8), released.Z);
        Equal(true, planner.IsAutomaticMovementClear(released, target, next),
            "the resumed input after native control returns is walkmesh-clear");
    }

    private static void OptionalCaveAndReturnUseReachableNativeGateways(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = new MountMemory(464, lowered: true);
        var reader = memory.StoryReader();
        var scripts = Scripts();
        var start = Position(464, -1911, 143, 358, 151);
        var cave = reader.ReadTargets(start).Single(target => target.Label == "Follow the side path to the cave (optional)");
        Equal(new FieldNavigationTriggerLine(1925,-1532,79,2079,-1663,88), cave.TriggerLine!.Value,
            "the optional cave side path is the actual gateway2");
        var planner = Planner(createWalkmeshReader(464), scripts, memory);
        var nativeTransitionIds = scripts.ReadField(464).Transitions
            .Select(transition => transition.StableId)
            .ToHashSet(StringComparer.Ordinal);
        // Mount Corel's track is stitched together by the border lines' own XYZI+MOVE
        // party scripts, which are how a player crosses between its levels, so a route
        // to the cave may legitimately ride one. What has to hold is that every link it
        // rides is a link the field's own scripts contain, rather than one the planner
        // invented to bridge a gap it could not walk.
        foreach (var position in new[] { start, Position(464, 2666, -1870, 540, 9) })
        {
            Equal(true, planner.TryBuildRoute(position, cave, out var route),
                $"the native passage reaches the cave from {position.X},{position.Y}: {planner.LastDiagnostic}");
            foreach (var portal in route.Portals.Where(portal => portal.TransitionKind is not null))
            {
                Equal(true, nativeTransitionIds.Contains(portal.TransitionId),
                    $"the cave route may only ride native links, not '{portal.TransitionId}'");
            }
        }
        var cavePosition = Position(465, 22, 351, -44, 10);
        memory.SetField(465);
        var returnTarget = Single(reader, cavePosition);
        var returnPlanner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(465));
        Equal(true, returnPlanner.TryBuildRoute(cavePosition, returnTarget, out _),
            $"the cave entry can return through gateway0: {returnPlanner.LastDiagnostic}");
    }

    private static void SwitchArrivalReachesTheNativeInteractionRange()
    {
        var memory = new MountMemory(464);
        var target = Single(memory.StoryReader(), Position(464, 0, 0, 0, 138));
        Equal(39, target.InteractionRadius,
            "the switch interaction stays strictly inside the live player's radius40");
        var position = Position(464, target.X - 60, target.Y, target.Z, 138);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), new StraightPlanner());
        var transform = new FieldNavigationControlTransform(0);
        while (controller.CurrentCategory != FieldNavigationCategory.Story)
            controller.HandleAction(FieldNavigationAction.NextCategory, position, transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, transform);
        controller.UpdateLiveTracking(position, new(0, FieldNavigationInput.None), transform, false, 80);
        Equal(true, controller.TryResolveAutomaticInput(position, transform, 80, out _),
            "auto walk must not stop60 units before the switch's native radius40");
        position = position with { X = target.X - 38 };
        controller.UpdateLiveTracking(position, new(0, FieldNavigationInput.None), transform, false, 80);
        Equal(false, controller.TryResolveAutomaticInput(position, transform, 80, out _),
            "the switch pauses movement inside its native activation range for the ASK");
        Equal(true, controller.BeaconEnabled,
            "the still-unaccepted switch choice cannot be reported as completed");
    }

    private static void BirdClimbIsOfferedOnlyWithTheNativeAudibleCue()
    {
        var memory = new MountMemory(464);
        var reader = memory.StoryReader();
        var position = Position(464, 3475, -2266, 1070, 260);
        bool HasClimb() => reader.ReadTargets(position).Any(target => target.Label == "Investigate the bird calls (optional)");
        Equal(false, HasClimb(), "a silent undiscovered nest must not be disclosed by the guide catalog");
        memory.SetBirdCalls(true);
        Equal(true, HasClimb(), "native5[17]=1 makes the audible bird-call climb available");
        var climb = reader.ReadTargets(position).Single(target => target.Label == "Investigate the bird calls (optional)");
        Equal(new FieldNavigationTriggerLine(3313,-2225,1070,3637,-2307,1070), climb.TriggerLine!.Value,
            "the optional clue uses the actual native ladder entry LINE");
        memory.SetNestDecisionSeen();
        Equal(false, HasClimb(), "the native take-or-leave decision bit retires the discovered clue");
    }

    private sealed class StraightPlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight native interaction threshold fixture";
        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        { triangle = position.TriangleId; return true; }
        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = new(position.FieldId, target.StableId!, [position.TriangleId], [],
                new(target.X, target.Y, target.Z), position.TriangleId);
            return true;
        }
        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        { waypoint = new(target.X, target.Y, target.Z); return true; }
    }

    private static FieldWalkmeshRoutePlanner Planner(FieldWalkmeshReader mesh,
        FieldScriptNavigationCatalog scripts, MountMemory memory) => new(mesh,
            new FieldBoundaryStateReader(memory.ReadInt32, memory.ReadByte, (_, _) => true),
            field => scripts.ReadField(field).Transitions
                // mtcrl_9/border2 Main explicitly executes LINON0. The live
                // runtime already filters disabled LINEs; reproduce that state.
                .Where(transition => field != 467 || transition.SourceEntityId != 4).ToArray());

    private static void NativeApproachAndLongBridgeRoutesReachTheirActualExits(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var scripts = Scripts();
        AssertOpcode(scripts, 467, 4, 0, 14, [0xD1,0],
            "the long bridge's decorative LINE must remain natively disabled");
        foreach (var (position, lowered) in new[]
        {
            // First start is the world-facing gateway midpoint on triangle122;
            // the others are the exact native incoming gateway coordinates.
            (Position(458,193,-631,-383,122), false),
            (Position(459,103,123,-396,25), false),
            (Position(460,1778,3118,-448,209), false),
            (Position(461,-1218,9,551,170), false),
            (Position(462,-2321,-5,1324,227), false),
            (Position(462,2367,-8,1068,9), true),
            (Position(467,9,6966,1050,141), true)
        })
        {
            var memory = new MountMemory(position.FieldId, lowered);
            var target = Single(memory.StoryReader(), position);
            var planner = Planner(createWalkmeshReader(position.FieldId), scripts, memory);
            Equal(true, planner.TryBuildRoute(position, target, out var route),
                $"native field{position.FieldId} ingress reaches its Story exit: {planner.LastDiagnostic}");
            Equal(target.TriggerLine, route.TargetTriggerLine,
                "the route preserves the exact native traversal line");
            if (position.FieldId == 467)
                Equal(false, route.Portals.Any(portal => portal.TransitionKind is not null),
                    "the long bridge walks continuously without the disabled decorative jump");
        }
    }

    private static FieldScriptNavigationCatalog Scripts() =>
        new(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")!);

    private static void AssertOpcode(FieldScriptNavigationCatalog scripts, int field, int entity,
        int script, int offset, byte[] expected, string label) =>
        Equal(Convert.ToHexString(expected), Convert.ToHexString(
            scripts.ReadScriptOpcodes(field, entity, script).Single(op => op.ByteIndex == offset).Bytes.ToArray()), label);

    private static FieldNavigationTarget Single(FieldStoryTargetReader reader, FieldPositionSnapshot position)
    {
        var targets = reader.ReadTargets(position).Where(target => !target.Label.EndsWith("(optional)", StringComparison.Ordinal)).ToArray();
        Equal(1, targets.Length, $"field{position.FieldId}, triangle{position.TriangleId} must have one next Story step");
        return targets.Single();
    }

    private static FieldPositionSnapshot Position(int field, int x = 0, int y = 0, int z = 0, ushort triangle = 0) =>
        new(FieldPositionReader.FieldModule, field, 0, x, y, z, triangle, 0);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private sealed class MountMemory
    {
        private const int FieldState = 0x03200000;
        private const int EventTable = 0x03300000;
        private readonly Dictionary<int, byte> bytes = [];
        private int fieldId;
        private bool bridgeLowered;
        public bool LineEnabled { get; set; } = true;
        public MountMemory(int field, bool lowered = false)
        {
            SetGameMoment(422);
            SetField(field);
            SetLowered(lowered);
            bytes[FieldPositionReader.AddressFieldNumModels] = 1;
        }
        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);
        public int ReadInt32(int address) => address switch
        {
            FieldBoundaryStateReader.AddressFieldGlobalObjectPtr => FieldState,
            FieldNavigationObjectReader.AddressFieldEventDataPtr => EventTable,
            _ => 0
        };
        public short ReadInt16(int address) => address == EventTable + 0x72 ? (short)40 : (short)0;
        public void SetBirdCalls(bool audible) =>
            bytes[FieldNavigationObjectReader.AddressTemporaryFieldBankBase + 17] = audible ? (byte)1 : (byte)0;
        public void SetNestDecisionSeen() =>
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 222] |= 0x10;
        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }
        public void SetField(int field)
        {
            fieldId = field;
            bytes[FieldPositionReader.AddressCurrentModule] = 1;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
            SetBoundaryTriangles();
        }
        public void SetLowered(bool lowered)
        {
            bridgeLowered = lowered;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 222] = lowered ? (byte)0x20 : (byte)0;
            SetBoundaryTriangles();
        }
        private void SetBoundaryTriangles()
        {
            for (var i = 0; i < FieldBoundaryStateReader.BoundaryByteCount; i++)
                bytes[FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + i] = 0;
            foreach (var triangle in fieldId switch
            {
                460 => new[] { 5 },
                464 => bridgeLowered ? new[] { 280 } : new[] { 6, 140, 280 },
                _ => Array.Empty<int>()
            })
            {
                var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
                bytes[address] = (byte)(ReadByte(address) | (1 << (triangle & 7)));
            }
        }
        public FieldStoryTargetReader StoryReader() => new(ReadInt32, ReadInt16, ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), _ => LineEnabled);
    }
}
