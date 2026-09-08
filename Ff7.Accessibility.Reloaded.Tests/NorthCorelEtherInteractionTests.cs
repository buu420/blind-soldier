using Ff7.Accessibility.Reloaded;

internal static class NorthCorelEtherInteractionTests
{
    private static readonly FieldNavigationControlTransform Transform = new(-128);
    private static readonly FieldPositionSnapshot BeforePot = new(1, 453, 0, 134, 205, -10, 7, 128);

    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        EtherRetainsItsNativeLineAndCollectionScript();
        AutomaticWalkingContinuesUntilTheNativeInteractionRange(createWalkmeshReader);
        NativeRangeRequiresUsablePlayerState();
        UnreviewedLineObjectsKeepTheirExistingRange();
    }

    private static void EtherRetainsItsNativeLineAndCollectionScript()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("Ether tests need the installed data root.");
        var scripts = new FieldScriptNavigationCatalog(dataRoot);
        AssertBytes(0, 0, "D06A00F5000000A200F5000000"); // LINE106,245,0 to162,245,0.
        AssertBytes(1, 0, "14F001000A1C"); // Only when bank15[1].bit0 is clear.
        AssertBytes(1, 6, "82F00100"); // Set collection bit before the message.
        AssertBytes(1, 25, "400007"); // Native Received Ether message.
        AssertBytes(1, 28, "5800030001"); // STITM item3, quantity1.
        var definition = EtherDefinition();
        Equal(FieldNavigationObjectTargetKind.Line, definition.TargetKind, "Ether remains a native LINE object");
        Equal((134, 245, 0), (definition.StaticX, definition.StaticY, definition.StaticZ),
            "the pot keeps the native LINE midpoint and height");
        Equal((15, 1, (byte)1), (definition.CollectedBank, definition.CollectedAddress, definition.CollectedMask),
            "the native collection gate remains unchanged");
        var optedIn = FieldNavigationObjectCatalog.CreateAllFields()
            .Where(item => item.UsesPlayerCollisionRadius).ToArray();
        Equal(1, optedIn.Length, "only the native-reviewed Ether opts into this interaction geometry");
        Equal((453, 3), (optedIn[0].FieldId, optedIn[0].EntityId), "the scope remains the North Corel pot");

        void AssertBytes(int script, int offset, string expected)
        {
            var opcode = scripts.ReadScriptOpcodes(453, 3, script).Single(item => item.ByteIndex == offset);
            Equal(expected, Convert.ToHexString(opcode.Bytes.ToArray()), $"native Ether anchor453:3:{script}:{offset}");
        }
    }

    private static void AutomaticWalkingContinuesUntilTheNativeInteractionRange(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = new NativeMemory();
        var reader = memory.Reader(EtherDefinition());
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], objectTargetProvider: reader.ReadTargets),
            new FieldWalkmeshRoutePlanner(createWalkmeshReader(453)));
        while (controller.CurrentCategory != FieldNavigationCategory.Objects)
            controller.HandleAction(FieldNavigationAction.NextCategory, BeforePot, Transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, BeforePot, Transform);
        controller.UpdateLiveTracking(BeforePot, new(0, FieldNavigationInput.None), Transform, false, 80,
            observedAt: DateTime.UnixEpoch);
        // The native radius is30 (field scale512), so40 units before the pot
        // cannot activate its OK handler. The old fixed48 stopped here.
        Equal(true, controller.TryResolveAutomaticInput(BeforePot, Transform, 80, out var input),
            "Ether auto walk must continue at40 units before its native LINE");
        Equal(true, input != FieldNavigationInput.None, "the approach still supplies a movement direction");
        Equal(29, reader.ReadTargets(BeforePot).Single().InteractionRadius,
            "arrival stays strictly inside the native player radius30");

        var inside = BeforePot with { Y = 223 };
        var pause = controller.UpdateLiveTracking(inside, new(0, input), Transform, false, 80,
            observedAt: DateTime.UnixEpoch.AddSeconds(1));
        Equal(true, pause?.Speech.Contains("Interact here. Navigation paused.", StringComparison.Ordinal) == true,
            "the existing interaction path pauses within native reach");
        Equal(false, controller.TryResolveAutomaticInput(inside, Transform, 80, out _),
            "arrival leaves pressing OK to the player");
        Equal(true, controller.BeaconEnabled, "arrival does not pretend the Ether was collected");

        var departed = BeforePot with { Y = 145 };
        var resume = controller.UpdateLiveTracking(departed, new(0, FieldNavigationInput.None), Transform, false, 80,
            observedAt: DateTime.UnixEpoch.AddSeconds(2));
        Equal(true, resume?.Speech.Contains("Navigation resumed.", StringComparison.Ordinal) == true,
            "leaving the interaction region resumes the active target");
        controller.UpdateLiveTracking(departed, new(0, FieldNavigationInput.None), Transform, false, 80,
            observedAt: DateTime.UnixEpoch.AddSeconds(3));
        Equal(true, controller.TryResolveAutomaticInput(departed, Transform, 80, out _),
            "resumed navigation can steer back toward the pot");

        memory.Collected = 1;
        controller.UpdateLiveTracking(departed, new(0, FieldNavigationInput.None), Transform, false, 80,
            observedAt: DateTime.UnixEpoch.AddSeconds(4));
        Equal(false, controller.BeaconEnabled, "the native collection bit withdraws the target");
    }

    private static void NativeRangeRequiresUsablePlayerState()
    {
        var memory = new NativeMemory();
        var reader = memory.Reader(EtherDefinition());
        foreach (var radius in new short[] { 8, 30, 72 })
        {
            memory.Radius = radius;
            Equal(radius - 1, reader.ReadTargets(BeforePot).Single().InteractionRadius,
                "the checked range follows the current player model instead of a field constant");
        }
        foreach (var radius in new short[] { -1, 0, 1 })
        {
            memory.Radius = radius;
            Equal(0, reader.ReadTargets(BeforePot).Count, "unusable native radius cannot fall back to48");
        }
        memory.Radius = 30;
        memory.EventTableAvailable = false;
        Equal(0, reader.ReadTargets(BeforePot).Count, "missing event table cannot invent an interaction range");
        memory.EventTableAvailable = true;
        memory.ModelCount = 0;
        Equal(0, reader.ReadTargets(BeforePot).Count, "missing model count cannot invent an interaction range");
        memory.ModelCount = 1;
        Equal(0, reader.ReadTargets(BeforePot with { ModelIndex = 1 }).Count,
            "out-of-count player cannot supply an interaction range");
        memory.LineEnabled = false;
        Equal(0, reader.ReadTargets(BeforePot).Count, "disabled native LINE remains absent");
        memory.LineEnabled = true;
        memory.Collected = 1;
        Equal(0, reader.ReadTargets(BeforePot).Count, "collected Ether remains absent");
    }

    private static void UnreviewedLineObjectsKeepTheirExistingRange()
    {
        var memory = new NativeMemory { EventTableAvailable = false };
        var definition = FieldNavigationObjectCatalog.CreateAllFields()
            .Single(item => item.FieldId == 245 && item.EntityId == 15);
        var target = memory.Reader(definition).ReadTargets(BeforePot with { FieldId = 245 }).Single();
        Equal(48, target.InteractionRadius, "unreviewed LINE rows retain their existing range and eligibility");
    }

    private static FieldNavigationObjectDefinition EtherDefinition() =>
        FieldNavigationObjectCatalog.CreateAllFields().Single(item => item.FieldId == 453 && item.EntityId == 3);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"North Corel Ether: {label}: expected {expected}, got {actual}.");
    }

    private sealed class NativeMemory
    {
        private const int EventTable = 0x03000000;
        internal short Radius { get; set; } = 30;
        internal bool EventTableAvailable { get; set; } = true;
        internal byte ModelCount { get; set; } = 1;
        internal bool LineEnabled { get; set; } = true;
        internal byte Collected { get; set; }

        internal FieldNavigationObjectReader Reader(FieldNavigationObjectDefinition definition) => new(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr && EventTableAvailable
                ? EventTable : 0,
            address => address switch
            {
                FieldPositionReader.AddressFieldNumModels => ModelCount,
                EventTable + 0x72 => unchecked((byte)Radius),
                EventTable + 0x73 => unchecked((byte)(Radius >> 8)),
                FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + 1 => Collected,
                _ => 0
            },
            _ => "Ether", _ => null, [definition], _ => LineEnabled);
    }
}
