using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NorthCorelNavigationTests
{
    // These are the installed LINE endpoints, not guessed scene/model coordinates.
    private static readonly FieldNavigationTriggerLine TownArrival = new(713, 997, 0, 822, 1028, 0);
    private static readonly FieldNavigationTriggerLine TownRopeway = new(-673, -631, 0, -657, -731, 0);
    private static readonly FieldNavigationTriggerLine RopewayScene = new(1012, -675, 113, 1015, -780, 120);
    private static readonly FieldNavigationTriggerLine RopewayBoarding = new(-176, 35, 128, -176, -62, 128);
    private static readonly FieldNavigationTriggerLine GoldEntrance = new(-60, -1273, 0, -89, -1444, 0);
    private static readonly (string Label, FieldNavigationTriggerLine Line)[] OptionalVisits =
    [
        ("Visit the inn (optional)", new(-351, 651, 426, -302, 544, 426)),
        ("Visit the left house (optional)", new(-426, -109, 213, -409, -63, 213)),
        ("Visit the tent (optional)", new(326, -67, 0, 351, -17, 0)),
        ("Visit the right house (optional)", new(576, -155, 0, 609, -195, 0))
    ];
    private static readonly (int Field, FieldNavigationTriggerLine Line, int X, int Y)[] Interiors =
    [
        (453, new(-75, -217, 0, 23, -217, 0), 0, 0),
        (454, new(-53, -316, 0, 66, -316, 0), 0, 0),
        (455, new(325, -93, 0, 324, 72, 0), 0, 0),
        (456, new(252, -370, 0, 130, -361, 0), 190, -261)
    ];

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader,
        IReadOnlyList<FieldStoryEventDefinition>? definitions = null)
    {
        definitions ??= FieldStoryEventCatalog.CreateAllFields();
        CrossingKeepsAutomaticInputUntilTheNativeExit(createWalkmeshReader, definitions);
        TownContinuesAfterTheConfrontation(definitions);
        OptionalTownVisitsRemainAlongsideTheRopeway(definitions);
        RopewayScenePrecedesBoardingAndDeclineDoesNotEndTheRoute(definitions);
        TicketsRequireTheNativeDayOrLifetimeFlag(definitions);
        InteriorReturnsAndLaterScenesStayInTheirNativeStages(definitions);
        NativeRoutesRespectTheUnpaidTicketBarrier(createWalkmeshReader, definitions);
        InstalledScriptsStillMatchTheStageAndGeometryEvidence();
    }

    private static void CrossingKeepsAutomaticInputUntilTheNativeExit(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        var memory = new NativeMemory(definitions) { Moment = 427 };
        var target = memory.Targets(453).Single();
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(453));
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]), planner);
        var transform = new FieldNavigationControlTransform(-128);
        var start = Position(453, 0, 0, 0);
        while (controller.CurrentCategory != FieldNavigationCategory.Story)
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        // The native exit lies atY=-217. This point is inside the configured80
        // proximity threshold but still60 units short of its crossing.
        var beforeExit = Position(453, 0, -157, 0);
        controller.UpdateLiveTracking(beforeExit, new(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: DateTime.UnixEpoch);
        Equal(true, controller.TryResolveAutomaticInput(beforeExit, transform, 80, out var input),
            $"Story crossing must not pause for interaction60 units before the native exit: {controller.LastNavigationDiagnostic}");
        Equal(false, input == FieldNavigationInput.None, "the crossing must continue emitting movement before contact");
    }

    private static void TownContinuesAfterTheConfrontation(IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        var memory = new NativeMemory(definitions);
        foreach (var moment in new[] { 422, 426 })
        {
            memory.Moment = moment;
            RequireLine(memory, 450, TownArrival, "first town arrival must cross the confrontation line");
            Equal(1, memory.Targets(450).Count, "the arrival scene precedes optional town visits");
        }
        memory.Moment = 427;
        foreach (var barretFlag in new byte[] { 0, 4 })
        {
            // SETWORD427 happens even when Barret is not in the current party;
            // bank1[128].bit2 is only set by the party-specific branch.
            memory.Flags(1, 128, barretFlag);
            RequireLine(memory, 450, TownRopeway, "town Story must continue to the ropeway after either branch");
        }
        memory.Moment = 438;
        RequireLine(memory, 450, TownRopeway, "returning from the ropeway still offers the exit");
        memory.Moment = 421;
        Equal(0, memory.Targets(450).Count, "North Corel first-entry rows require Mount Corel's native stage422");
        memory.Moment = 439;
        Equal(0, memory.Targets(450).Count, "first-visit town targets must not leak into later Gold Saucer scenes");
    }

    private static void OptionalTownVisitsRemainAlongsideTheRopeway(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        var memory = new NativeMemory(definitions);
        foreach (var moment in new[] { 427, 433, 438 })
        {
            memory.Moment = moment;
            RequireLine(memory, 450, TownRopeway, "optional visits must not replace the main story exit");
            foreach (var (label, line) in OptionalVisits)
            {
                var target = RequireLine(memory, 450, line, "visible house entrances remain optional Story choices");
                Equal(label, target.Label, "optional entrances must not imply that their interactions are mandatory");
            }
        }
    }

    private static void RopewayScenePrecedesBoardingAndDeclineDoesNotEndTheRoute(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        var memory = new NativeMemory(definitions) { Moment = 427 };
        RequireLine(memory, 457, RopewayScene, "entering the station first must reach Barret's flashback");
        memory.Moment = 430;
        Equal(0, memory.Targets(457).Count,
            "the flashback's stage430 write alone must not announce boarding during its return sequence");
        memory.Flags(1, 128, 1); //457:1:0:159: returned from the full flashback.
        foreach (var moment in new[] { 430, 433, 436, 438 })
        {
            memory.Moment = moment;
            RequireLine(memory, 457, RopewayBoarding,
                "declining the initial ASK or returning from Gold Saucer must leave boarding available");
        }
        memory.Moment = 439;
        Equal(0, memory.Targets(457).Count, "first-visit boarding ends before the companion-selection chapter");
    }

    private static void TicketsRequireTheNativeDayOrLifetimeFlag(IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        var memory = new NativeMemory(definitions) { Moment = 436 };
        foreach (var flags in new byte[] { 0, 2, 0x40, 0x7E })
        {
            memory.Flags(3, 72, flags);
            var clerk = memory.Targets(496).Single();
            Equal(15, clerk.TriggerEntityId, "unpaid visitors must be sent to the live ticket clerk");
            Equal((314, -1146, 0), (clerk.X, clerk.Y, clerk.Z), "the ticket clerk uses the visible model's position");
            Equal(110, clerk.InteractionRadius, "clerk approach uses native player plus Talk reach");
            Equal(false, clerk.CompletesOnArrival, "the player must choose and pay using the native ticket menu");
        }
        // Both declining the ASK and insufficient gil leave both ticket flags clear.
        memory.Flags(3, 72, 0);
        Equal(15, memory.Targets(496).Single().TriggerEntityId, "an unsuccessful purchase still targets the clerk");
        foreach (var flags in new byte[] { 1, 0x80, 0x81, 0xFF })
        {
            memory.Flags(3, 72, flags);
            RequireLine(memory, 496, GoldEntrance, "either native ticket kind admits the player through gateway0");
        }
        memory.Flags(3, 72, 0); //496:13:5:4 clears a day ticket when taking the ropeway back.
        Equal(15, memory.Targets(496).Single().TriggerEntityId, "a used day ticket requires purchasing again on return");
        memory.ClerkVisible = false;
        Equal(0, memory.Targets(496).Count, "a hidden clerk must not produce a guessed model objective");
    }

    private static void InteriorReturnsAndLaterScenesStayInTheirNativeStages(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        var memory = new NativeMemory(definitions) { Moment = 427 };
        foreach (var (field, line, _, _) in Interiors)
        {
            RequireLine(memory, field, line, "interiors must offer their real town exit without buying or resting");
        }
        memory.Moment = 438;
        foreach (var (field, line, _, _) in Interiors)
            RequireLine(memory, field, line, "interior returns survive a first-visit ropeway return");
        memory.Moment = 439;
        foreach (var (field, _, _, _) in Interiors)
            Equal(0, memory.Targets(field).Count, "first-visit interior rows must not override later scripts");

        foreach (var moment in new[] { 436, 438, 582, 584, 586 })
        {
            memory.Moment = moment;
            Equal(false, memory.Targets(496).Any(target => target.X == 971 && target.Y == -598),
                "the broken-tram line is only enabled at native stage583");
        }
        memory.Moment = 583;
        Equal(true, memory.Targets(496).Any(target => target.X == 971 && target.Y == -598),
            "the later broken-tram story row must be preserved at its actual stage");
        memory.Moment = 436;
        Equal(0, memory.Targets(497).Count, "initial hub arrival must not advertise the later automatic companion fallback");
        memory.Moment = 439;
        Equal(true, memory.Targets(497).Any(), "the existing companion-stage row remains available at439");
    }

    private static void NativeRoutesRespectTheUnpaidTicketBarrier(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        var memory = new NativeMemory(definitions);
        var cases = new List<(int Field, int Moment, byte Flags, int X, int Y, int Z, FieldNavigationTriggerLine Line)>
        {
            (450, 422, 0, 730, 1343, 0, TownArrival), //467:3:2:88 MAPJUMP landing, triangle89.
            (450, 427, 4, 740, 804, 0, TownRopeway),
            (450, 427, 4, 523, -277, 0, TownRopeway), //House return.
            (457, 427, 0, 1367, -699, 6, RopewayScene),
            (457, 430, 1, 148, -50, 128, RopewayBoarding), //The native decline branch.
            (457, 436, 1, 114, -54, 128, RopewayBoarding)
        };
        cases.AddRange(Interiors.Select(item => (item.Field, 427, (byte)0, item.X, item.Y, 0, item.Line)));
        cases.AddRange(OptionalVisits.Select(item => (450, 427, (byte)4, 740, 804, 0, item.Line)));
        foreach (var (field, moment, flags, x, y, z, line) in cases)
        {
            memory.Moment = moment;
            memory.Flags(1, 128, flags);
            var target = RequireLine(memory, field, line, "native route fixture must select the correct stage");
            var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(field));
            Equal(true, planner.TryBuildRoute(Position(field, x, y, z), target, out var plan),
                $"installed field{field} route from{x},{y}: {planner.LastDiagnostic}");
            Equal(true, DistanceToLine(plan.FinalApproach, line) <= 4,
                "the route must reach the actual native trigger, not a remote guessed point");
        }

        memory.Moment = 436;
        memory.Flags(3, 72, 0);
        memory.CurrentField = 496;
        memory.TicketBarrierLocked = true;
        var goldPlanner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(496), memory.Boundaries);
        var arrival = Position(496, 767, 252, 0);
        var clerk = memory.Targets(496).Single();
        Equal(true, goldPlanner.TryBuildRoute(arrival, clerk, out var clerkPlan),
            $"the unpaid visitor can reach the clerk: {goldPlanner.LastDiagnostic}");
        Equal(false, clerkPlan.TrianglePath.Contains(6), "clerk approach must remain outside the unpaid entrance barrier");
        memory.Flags(3, 72, 1);
        var entrance = RequireLine(memory, 496, GoldEntrance, "ticketed entrance fixture");
        Equal(false, goldPlanner.TryBuildRoute(arrival, entrance, out _),
            "a ticket flag cannot bypass a still-locked live IDLCK triangle6");
        memory.TicketBarrierLocked = false;
        Equal(true, goldPlanner.TryBuildRoute(arrival, entrance, out var entryPlan),
            $"native ticket purchase opens the entrance: {goldPlanner.LastDiagnostic}");
        Equal(true, entryPlan.TrianglePath.Contains(6), "the paid route crosses the native unlocked ticket boundary");
    }

    private static void InstalledScriptsStillMatchTheStageAndGeometryEvidence()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new InvalidOperationException("North Corel native tests require FF7_ACCESSIBILITY_DATA_ROOT.");
        var scripts = new FieldScriptNavigationCatalog(dataRoot);
        AssertBytes(450, 3, 0, 0, "D0C902E5030000360304040000");
        AssertBytes(450, 4, 0, 0, "D05FFD89FD00006FFD25FD0000");
        AssertBytes(496, 15, 1, 185, "6D060000"); //Successful day purchase unlocks triangle6.
        AssertBytes(496, 15, 1, 189, "82304800"); //Bank3[72].bit0.
        AssertBytes(496, 15, 1, 297, "6D060000"); //Successful lifetime purchase unlocks triangle6.
        AssertBytes(496, 15, 1, 301, "82304807"); //Bank3[72].bit7.

        void AssertBytes(int field, int entity, int script, int offset, string expected)
        {
            var opcode = scripts.ReadScriptOpcodes(field, entity, script).Single(item => item.ByteIndex == offset);
            Equal(expected, Convert.ToHexString(opcode.Bytes.ToArray()),
                $"installed native script anchor{field}:{entity}:{script}:{offset}");
        }
    }

    private static FieldNavigationTarget RequireLine(NativeMemory memory, int field,
        FieldNavigationTriggerLine line, string message)
    {
        var targets = memory.Targets(field);
        var matching = targets.Where(target => target.TriggerLine == line).ToArray();
        Equal(1, matching.Length, message + "; expected one target at the native line");
        Equal(true, matching[0].CompletesOnArrival,
            message + "; Story traversal must use the controller's crossing completion rule");
        return matching[0];
    }

    private static FieldPositionSnapshot Position(int field, int x = 0, int y = 0, int z = 0) =>
        new(1, field, 0, x, y, z, 0, 0);

    private static double DistanceToLine(FieldNavigationRouteWaypoint point, FieldNavigationTriggerLine line)
    {
        var dx = line.EndX - line.StartX;
        var dy = line.EndY - line.StartY;
        var t = Math.Clamp(((point.X - line.StartX) * (double)dx + (point.Y - line.StartY) * (double)dy) /
                           (dx * (double)dx + dy * (double)dy), 0, 1);
        return Math.Sqrt(Math.Pow(point.X - line.StartX - t * dx, 2) + Math.Pow(point.Y - line.StartY - t * dy, 2));
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"North Corel: {message}; expected {expected}, actual {actual}.");
    }

    private sealed class NativeMemory
    {
        private const int Events = 0x03000000;
        private const int GlobalObject = 0x04000000;
        private readonly Dictionary<int, byte> bytes = new();
        private readonly FieldStoryTargetReader reader;
        public int CurrentField { get; set; } = 496;
        public bool TicketBarrierLocked { get; set; }
        public bool ClerkVisible { get; set; } = true;
        public FieldBoundaryStateReader Boundaries { get; }
        public int Moment { set { bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)value;
                bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(value >> 8); } }

        public NativeMemory(IReadOnlyList<FieldStoryEventDefinition> definitions)
        {
            reader = new(ReadInt32, ReadInt16, ReadByte, definitions, _ => true);
            Boundaries = new(ReadInt32, ReadByte, (_, _) => true);
        }
        public void Flags(int bank, int address, byte value) =>
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + (bank == 3 ? 0x100 : 0) + address] = value;
        public IReadOnlyList<FieldNavigationTarget> Targets(int field) => reader.ReadTargets(Position(field));
        private int ReadInt32(int address)
        {
            if (address == FieldNavigationObjectReader.AddressFieldEventDataPtr) return Events;
            if (address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr) return GlobalObject;
            var clerk = Events + 10 * FieldNavigationObjectReader.FieldEventDataStride;
            if (address == clerk + FieldNavigationObjectReader.PositionXOffset) return 314 * 4096;
            if (address == clerk + FieldNavigationObjectReader.PositionYOffset) return -1146 * 4096;
            return 0;
        }
        private short ReadInt16(int address)
        {
            if (address == Events + 0x72) return 30;
            if (address == Events + 10 * FieldNavigationObjectReader.FieldEventDataStride + 0x74) return 80;
            return 0;
        }
        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressCurrentModule) return 1;
            if (address == FieldPositionReader.AddressFieldId) return (byte)CurrentField;
            if (address == FieldPositionReader.AddressFieldId + 1) return (byte)(CurrentField >> 8);
            if (address == GlobalObject + FieldBoundaryStateReader.BoundaryBitsOffset)
                return TicketBarrierLocked ? (byte)0x40 : (byte)0;
            if (address == FieldPositionReader.AddressFieldNumModels) return 11;
            if (address == FieldNavigationObjectReader.AddressFieldModelIdArray + 15) return 10;
            if (address == Events + 10 * FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset)
                return ClerkVisible ? (byte)1 : (byte)0;
            return bytes.GetValueOrDefault(address);
        }
    }
}
