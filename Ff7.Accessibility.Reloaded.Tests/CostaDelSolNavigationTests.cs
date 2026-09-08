using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class CostaDelSolNavigationTests
{
    private const string TownExit = "Leave Costa del Sol for Mount Corel";
    private const string Beach = "Visit the beach (optional)";
    private const string Hojo = "Speak to the woman beside Hojo (optional)";
    private const string ReturnToTown = "Return to the town";

    public static void Run()
    {
        TownExitRemainsSelectableAlongsideOptionalVisits();
        FirstDockDepartureUsesItsNativeEnabledLine();
        HojoSceneUsesTheWomanAndItsActualCompletionFlags();
        OptionalCompanionsRequireVisibleNativeModels();
        NativeCountersRequireCloseManualInteraction();
        BallUsesOnlyItsVisibleLiveModel();
        InteriorReturnsRemainAvailableWithoutOptionalInteractions();
        CostaObjectivesEndWhenMountCorelAdvancesTheStory();
    }

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        Run();
        NativeGatewaysAreReachableFromTheirInstalledEntrances(createWalkmeshReader);
        BarretCanBeApproachedOutsideHisNativeLockedBathroom(createWalkmeshReader);
    }

    private static void TownExitRemainsSelectableAlongsideOptionalVisits()
    {
        var memory = new NativeMemory();
        var position = new FieldPositionSnapshot(1, 443, 0, -1160, -282, -153, 43, 216);
        var reader = memory.CreateReader();
        // Reproduce the 2026-09-06 13:05:22Z "Story: none" report. Sleeping,
        // finishing Hojo, and Johnny's conversation must never be exit prerequisites.
        foreach (var flags in new byte[] { 0, 0x08, 0x10, 0x18, 0x20, 0x40, 0xFF })
        {
            memory.SetFlags(flags);
            var targets = reader.ReadTargets(position);
            var exit = Required(targets, TownExit);
            Equal(new FieldNavigationTriggerLine(-1209, -396, -143, -1661, -106, -143),
                exit.TriggerLine!.Value, "Costa must select gateway7 to world field13, not the dock or beach");
            Equal((-1435, -251, -143), (exit.X, exit.Y, exit.Z), "native world exit midpoint");
            Equal(true, exit.CompletesOnArrival, "gateway Story routes must use the controller's native crossing completion");
            Required(targets, Beach); // Visiting the beach remains possible after Hojo leaves.
        }

        memory.SetFlags(0);
        var firstVisit = reader.ReadTargets(position);
        foreach (var label in new[] { Beach, "Visit the inn (optional)", "Visit Johnny's house (optional)",
                     "Visit the villa (optional)", "Visit the bar (optional)" })
            Required(firstVisit, label);
    }

    private static void FirstDockDepartureUsesItsNativeEnabledLine()
    {
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        Equal(0, reader.ReadTargets(Position(441)).Count,
            "the initial dock route needs positive live LINE5 evidence");
        memory.EnabledLines.Add(5);
        var initial = Required(reader.ReadTargets(Position(441)), "Enter Costa del Sol");
        Equal(new FieldNavigationTriggerLine(1044, -1032, 128, 1044, -1222, 128),
            initial.TriggerLine!.Value, "first exit invokes441:5:2:54 MAPJUMP442");
        memory.EnabledLines.Clear();
        Equal(0, reader.ReadTargets(Position(441)).Count, "a disabled initial line must not leave a stale target");
        memory.SetFlags(1); // del12/heri Script4 byte178: the automatic dock scene finished.
        var revisit = Required(reader.ReadTargets(Position(441)), "Enter Costa del Sol");
        Equal(new FieldNavigationTriggerLine(1071, -1036, 142, 1062, -1219, 137),
            revisit.TriggerLine!.Value, "revisit must use ordinary gateway0 to443");
        Equal(0, reader.ReadTargets(Position(442)).Count, "del12 is an automatic scene, with no walking objective");
    }

    private static void HojoSceneUsesTheWomanAndItsActualCompletionFlags()
    {
        var memory = new NativeMemory();
        memory.ConfigureModel(12, 337, 351, 8); // Hojo's own Talk only displays an ellipsis.
        memory.ConfigureModel(13, 353, 231, 0);
        var reader = memory.CreateReader();
        var position = Position(449);
        Equal(false, reader.ReadTargets(position).Any(target => target.Label == Hojo),
            "the beach introduction must finish before a manual conversation objective appears");
        memory.SetFlags(0x08); //449:8:6:52: native beach introduction finished.
        var target = Required(reader.ReadTargets(position), Hojo);
        Equal(13, target.TriggerEntityId, "woman1 Talk134 invokes AD1; Hojo Talk does not start the scene");
        Equal((353, 231, 0), (target.X, target.Y, target.Z), "conversation must follow the native model position");
        Equal(false, target.CompletesOnArrival, "walking within range is not conversation completion");
        Required(reader.ReadTargets(position), ReturnToTown);
        memory.ConfigureModel(13, 355, 233, 0);
        Equal(355, Required(reader.ReadTargets(position), Hojo).X, "do not freeze an NPC at its authored position");
        memory.SetVisible(13, false);
        Equal(false, reader.ReadTargets(position).Any(candidate => candidate.Label == Hojo),
            "missing/hidden native woman must not invent a conversation target");
        memory.SetVisible(13, true);
        foreach (var flags in new byte[] { 0x18, 0x28, 0x38 })
        {
            memory.SetFlags(flags);
            Equal(false, reader.ReadTargets(position).Any(candidate => candidate.Label == Hojo),
                "Hojo completion or inn rest must retire the optional conversation");
            Required(reader.ReadTargets(position), ReturnToTown);
        }
    }

    private static void OptionalCompanionsRequireVisibleNativeModels()
    {
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        Required(reader.ReadTargets(Position(444)), ReturnToTown);
        Equal(false, reader.ReadTargets(Position(444)).Any(target => target.TriggerEntityId == 12),
            "Barret in the current party does not leave a visible sailor model in the inn");
        memory.ConfigureModel(12, -300, -44, -63);
        Required(reader.ReadTargets(Position(444)), "Talk to Barret (optional)");
        memory.SetFlags(0x20);
        Equal(false, reader.ReadTargets(Position(444)).Any(target => target.TriggerEntityId == 12),
            "resting retires the sailor scene even if a model sample remains visible briefly");

        memory.SetFlags(0);
        memory.ConfigureModel(14, -103, -148, 0);
        Required(reader.ReadTargets(Position(448)), "Talk to Johnny (optional)");
        memory.SetFlags(0x40); //448:14:1:304: the first personal conversation finished.
        Equal(false, reader.ReadTargets(Position(448)).Any(target => target.Label == "Talk to Johnny (optional)"),
            "Johnny's saved completion bit must retire the first conversation");
        memory.SetFlags(0x10);
        memory.SetVisible(12, false);
        Required(reader.ReadTargets(Position(448)), "Talk to Johnny (optional)");
        //448:14:1:147 checks Tifa's party membership: with Tifa absent from the
        //house, byte150 still reaches Johnny's original conversation at192.
        memory.ConfigureModel(12, -38, 288, 45);
        Required(reader.ReadTargets(Position(448)), "Talk to Tifa (optional)");
        Required(reader.ReadTargets(Position(448)), ReturnToTown);
        memory.SetVisible(12, false);
        Equal(false, reader.ReadTargets(Position(448)).Any(target => target.Label == "Talk to Tifa (optional)"),
            "Tifa must not appear when her native model is hidden because she is in the party");
    }

    private static void InteriorReturnsRemainAvailableWithoutOptionalInteractions()
    {
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        (int Field, string Label, FieldNavigationTriggerLine Line)[] exits =
        [
            (444, ReturnToTown, new(-290, -975, -63, -402, -975, -63)),
            (445, ReturnToTown, new(354, -24, -147, 424, 13, -167)),
            (446, ReturnToTown, new(-489, 331, 0, -489, 207, 0)),
            (447, "Return upstairs", new(293, -50, 119, 344, -41, 124)),
            (448, ReturnToTown, new(342, -322, -57, 379, -228, -95)),
            (449, ReturnToTown, new(-749, 863, 109, -685, 867, 105))
        ];
        foreach (var (field, label, line) in exits)
        {
            foreach (var flags in new byte[] { 0, 0x08, 0x18, 0x20, 0xFF })
            {
                memory.SetFlags(flags);
                var target = Required(reader.ReadTargets(Position(field)), label);
                Equal(line, target.TriggerLine!.Value, $"field{field} must use its actual return gateway");
                Equal(true, target.CompletesOnArrival, "native return must use crossing completion rather than a proximity pause");
            }
        }
    }

    private static void NativeCountersRequireCloseManualInteraction()
    {
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        (string Label, int Entity, FieldNavigationTriggerLine Line)[] counters =
        [
            ("Talk at the item shop (optional)", 5, new(1350, 1567, 0, 1210, 1679, 0)),
            ("Talk at the materia shop (optional)", 6, new(601, 1855, 0, 723, 1843, 0))
        ];
        foreach (var (label, entity, line) in counters)
        {
            Equal(false, reader.ReadTargets(Position(443)).Any(target => target.Label == label),
                "a disabled counter LINE must not invite a manual interaction");
            memory.EnabledLines.Add(entity);
            var target = Required(reader.ReadTargets(Position(443)), label);
            Equal(line, target.TriggerLine!.Value, "shop approach must use the native IFKEYON counter segment");
            Equal(31, target.InteractionRadius, "counter must stop inside the strict native player-radius32 threshold");
            Equal(false, target.CompletesOnArrival, "counter must pause for the player's manual OK, not cross or complete");
            memory.SetPlayerCollisionRadius(12);
            Equal(11, Required(reader.ReadTargets(Position(443)), label).InteractionRadius,
                "counter radius must follow native state, not a fixed configured distance");
            memory.SetPlayerCollisionRadius(0);
            Equal(false, reader.ReadTargets(Position(443)).Any(candidate => candidate.Label == label),
                "missing native collision radius must not invent a usable counter approach");
            Required(reader.ReadTargets(Position(443)), TownExit);
            memory.SetPlayerCollisionRadius(32);
            memory.EnabledLines.Remove(entity);
        }
    }

    private static void BallUsesOnlyItsVisibleLiveModel()
    {
        const string ball = "Kick the ball (optional)";
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        Equal(false, reader.ReadTargets(Position(443)).Any(target => target.Label == ball),
            "do not invent the ball from its authored coordinates");
        memory.ConfigureModel(23, 398, 1400, 0);
        memory.ConfigureModel(11, 78, 1495, 0);
        Required(reader.ReadTargets(Position(443)), "Talk to Red XIII (optional)");
        var target = Required(reader.ReadTargets(Position(443)), ball);
        Equal(23, target.TriggerEntityId, "ball must use native entity23 Talk, not a guessed nearby actor");
        Equal(false, target.CompletesOnArrival, "the player owns the kick's manual OK input");
        memory.ConfigureModel(23, 76, 1426, 0);
        var moved = Required(reader.ReadTargets(Position(443)), ball);
        Equal((76, 1426, 0), (moved.X, moved.Y, moved.Z),
            "the ball moves and must retain its live coordinates");
        memory.SetFlags(0xFF);
        Required(reader.ReadTargets(Position(443)), ball);
        Equal(false, reader.ReadTargets(Position(443)).Any(candidate => candidate.Label == "Talk to Red XIII (optional)"),
            "resting retires Red's optional scene even before a visible model sample changes");
        memory.SetVisible(23, false);
        Equal(false, reader.ReadTargets(Position(443)).Any(candidate => candidate.Label == ball),
            "a hidden ball has no current visible interaction");
    }

    private static void CostaObjectivesEndWhenMountCorelAdvancesTheStory()
    {
        var memory = new NativeMemory();
        memory.SetFlags(0x08);
        memory.ConfigureModel(13, 353, 231, 0);
        memory.EnabledLines.Add(5);
        var reader = memory.CreateReader();
        foreach (var moment in new[] { 415, 421 })
        {
            memory.SetGameMoment(moment);
            Required(reader.ReadTargets(Position(443)), TownExit);
            Required(reader.ReadTargets(Position(449)), Hojo);
        }
        // Mt.Corel458 produce Init21 writes422; Init26 retires the Costa scene with bit5.
        foreach (var moment in new[] { 414, 422, 427, 440, 1008 })
        {
            memory.SetGameMoment(moment);
            foreach (var field in Enumerable.Range(441, 9))
                Equal(0, reader.ReadTargets(Position(field)).Count,
                    $"first-Costa objective must not leak into field{field} at moment{moment}");
        }
    }

    private static void NativeGatewaysAreReachableFromTheirInstalledEntrances(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // Literal entry XY/triangles from the preceding native field gateways;
        // first441 is Cloud's disembark placement and443 also replays the live failure.
        (FieldPositionSnapshot Position, string Label)[] cases =
        [
            (new(1, 441, 0, -463, 785, 63, 225, 0), "Enter Costa del Sol"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), TownExit),
            (new(1, 443, 0, -1160, -282, -153, 43, 216), TownExit),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), Beach),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Visit the inn (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Visit Johnny's house (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Visit the villa (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Visit the bar (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Talk at the item shop (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Talk at the materia shop (optional)"),
            (new(1, 444, 0, -357, -906, -63, 6, 0), ReturnToTown),
            (new(1, 445, 0, 212, 170, 0, 2, 0), ReturnToTown),
            (new(1, 446, 0, -386, 278, 0, 57, 0), ReturnToTown),
            (new(1, 446, 0, -386, 278, 0, 57, 0), "Visit the basement (optional)"),
            (new(1, 447, 0, 325, -126, 0, 8, 0), "Return upstairs"),
            (new(1, 448, 0, 238, -270, 0, 10, 0), ReturnToTown),
            (new(1, 449, 0, -673, 748, 0, 11, 0), ReturnToTown)
        ];
        foreach (var (position, label) in cases)
        {
            var memory = new NativeMemory();
            memory.EnabledLines.Add(5);
            memory.EnabledLines.Add(6);
            var target = Required(memory.CreateReader().ReadTargets(position), label);
            var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(position.FieldId));
            Equal(true, planner.TryBuildRoute(position, target, out var route),
                $"field{position.FieldId} {label} must be reachable on installed triangles: {planner.LastDiagnostic}");
            Equal(target.TriggerLine, route.TargetTriggerLine, "planner must preserve the native exit segment");
        }
    }

    private static void BarretCanBeApproachedOutsideHisNativeLockedBathroom(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = new NativeMemory();
        memory.SetBoundaryTriangles(444, [90]); //444:12:0:35 IDLCK, while Barret is outside the party.
        memory.ConfigureModel(12, -300, -44, -63); //444:12:0:39, native triangle93.
        memory.SetModelTalkRadius(12, 200); //444:12:0:2 TALKR.
        var entrance = new FieldPositionSnapshot(1, 444, 0, -357, -906, -63, 6, 0);
        var target = Required(memory.CreateReader().ReadTargets(entrance), "Talk to Barret (optional)");
        Equal(232, target.InteractionRadius, "Barret's native radius200 must be added to player radius32");
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(444),
            new FieldBoundaryStateReader(memory.ReadInt32, memory.ReadByte, (_, _) => true));
        Equal(true, planner.TryBuildRoute(entrance, target, out var route),
            $"Barret must be reachable for Talk from outside the occupied bathroom: {planner.LastDiagnostic}");
        Equal(false, route.TrianglePath.Contains(90), "approaching Barret must not route through the occupied bathroom lock");
        var dx = target.X - (double)route.FinalApproach.X;
        var dy = target.Y - (double)route.FinalApproach.Y;
        var dz = target.Z - (double)route.FinalApproach.Z;
        Equal(true, dx * dx + dy * dy + dz * dz <= 232 * 232,
            "a route that stops outside the bathroom must still finish within native Talk range");
    }

    private static FieldNavigationTarget Required(IReadOnlyList<FieldNavigationTarget> targets, string label) =>
        targets.SingleOrDefault(target => target.Label == label) is var target && target.Label == label
            ? target
            : throw new InvalidOperationException($"Costa Story missing '{label}'; got [{string.Join("|", targets.Select(value => value.Label))}]");

    private static FieldPositionSnapshot Position(int field) => new(1, field, 0, 0, 0, 0, 0, 0);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private sealed class NativeMemory
    {
        private const int EventTable = 0x02700000;
        private const int FieldState = 0x02800000;
        private readonly Dictionary<int, byte> bytes = [];
        private readonly Dictionary<int, byte> models = [];
        public HashSet<int> EnabledLines { get; } = [];

        public NativeMemory()
        {
            WriteInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
            bytes[FieldPositionReader.AddressFieldNumModels] = 16;
            SetPlayerCollisionRadius(32);
            SetGameMoment(415);
        }

        public FieldStoryTargetReader CreateReader() => new(ReadInt32, ReadInt16, ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), EnabledLines.Contains);
        public void SetFlags(byte flags) => bytes[FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 224] = flags;
        public void SetPlayerCollisionRadius(short radius)
        {
            bytes[EventTable + 0x72] = (byte)radius;
            bytes[EventTable + 0x73] = (byte)(radius >> 8);
        }
        public void SetModelTalkRadius(int entity, short radius)
        {
            var address = EventTable + models[entity] * FieldNavigationObjectReader.FieldEventDataStride + 0x74;
            bytes[address] = (byte)radius;
            bytes[address + 1] = (byte)(radius >> 8);
        }
        public void SetBoundaryTriangles(int field, IEnumerable<int> triangles)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = 1;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
            WriteInt32(FieldBoundaryStateReader.AddressFieldGlobalObjectPtr, FieldState);
            foreach (var triangle in triangles)
            {
                var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
                bytes[address] = (byte)(ReadByte(address) | (1 << (triangle & 7)));
            }
        }
        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }
        public void ConfigureModel(int entity, int x, int y, int z)
        {
            if (!models.TryGetValue(entity, out var model))
                models[entity] = model = (byte)(models.Count + 1);
            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = model;
            var address = EventTable + model * FieldNavigationObjectReader.FieldEventDataStride;
            SetVisible(entity, true);
            WriteInt32(address + FieldNavigationObjectReader.PositionXOffset, x * 4096);
            WriteInt32(address + FieldNavigationObjectReader.PositionYOffset, y * 4096);
            WriteInt32(address + FieldNavigationObjectReader.PositionZOffset, z * 4096);
        }
        public void SetVisible(int entity, bool visible) => bytes[EventTable + models[entity] *
            FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = visible ? (byte)1 : (byte)0;
        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);
        private short ReadInt16(int address) => unchecked((short)(ReadByte(address) | ReadByte(address + 1) << 8));
        public int ReadInt32(int address) => ReadByte(address) | ReadByte(address + 1) << 8 |
            ReadByte(address + 2) << 16 | ReadByte(address + 3) << 24;
        private void WriteInt32(int address, int value)
        {
            for (var index = 0; index < 4; index++)
                bytes[address + index] = (byte)(value >> (index * 8));
        }
    }
}
