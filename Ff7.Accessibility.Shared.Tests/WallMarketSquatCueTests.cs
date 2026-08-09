using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class WallMarketSquatCueTests
{
    public static void Run()
    {
        ReadsTheExactActiveCloudSquatScriptState();
        RequiresACoherentFailClosedSnapshot();
        IgnoresOtherFieldsScriptsAndInvalidSteps();
        AnnouncesOnlyTheVisibleStepAndResetsBetweenAttempts();
        PreservesTheLastCueAcrossAnUnreadableFrame();
    }

    private static void ReadsTheExactActiveCloudSquatScriptState()
    {
        foreach (var (step, expected) in new[]
        {
            ((byte)0, SquatMinigameStep.Switch),
            ((byte)1, SquatMinigameStep.Cancel),
            ((byte)2, SquatMinigameStep.Ok)
        })
        {
            var memory = CreateActiveMemory(step, completedSquats: 7);
            var reader = new SquatMinigameStateReader(memory);

            Equal(true, reader.TryRead(out var snapshot), $"read step {step}");
            Equal(true, snapshot.IsActive, $"active step {step}");
            Equal(expected, snapshot.ExpectedStep, $"expected step {step}");
            Equal((byte)7, snapshot.CompletedSquats, $"squat count {step}");
        }
    }

    private static void RequiresACoherentFailClosedSnapshot()
    {
        var missingState = CreateActiveMemory(0);
        missingState.Remove((uint)SquatMinigameStateReader.AddressExpectedStep);
        Equal(
            false,
            new SquatMinigameStateReader(missingState).TryRead(out _),
            "unmapped expected-step byte fails closed");

        var tornState = new TearingLegacyAddressSpace(
            CreateActiveMemory(0),
            (uint)SquatMinigameStateReader.AddressExpectedStep,
            [1]);
        Equal(
            false,
            new SquatMinigameStateReader(tornState).TryRead(out _),
            "step transition during snapshot fails closed");

        var activeScriptAddress = SquatMinigameStateReader.AddressEntityScriptIds +
            SquatMinigameStateReader.CloudEntityId * SquatMinigameStateReader.ScriptSlotsPerEntity +
            ActivePriority;
        var tornOwner = new TearingLegacyAddressSpace(
            CreateActiveMemory(0),
            (uint)activeScriptAddress,
            [5]);
        Equal(
            false,
            new SquatMinigameStateReader(tornOwner).TryRead(out _),
            "active script transition during snapshot fails closed");
    }

    private static void IgnoresOtherFieldsScriptsAndInvalidSteps()
    {
        var otherField = CreateActiveMemory(0);
        WriteUInt16(otherField, (uint)SquatMinigameStateReader.AddressCurrentFieldId, 198);
        Equal(true, new SquatMinigameStateReader(otherField).TryRead(out var otherFieldState), "other field readable");
        Equal(false, otherFieldState.IsActive, "other field inactive");

        var otherScript = CreateActiveMemory(0, activeScriptId: 5);
        Equal(true, new SquatMinigameStateReader(otherScript).TryRead(out var otherScriptState), "other script readable");
        Equal(false, otherScriptState.IsActive, "other script inactive");

        var tooFewEntities = CreateActiveMemory(0, entityCount: SquatMinigameStateReader.CloudEntityId);
        Equal(true, new SquatMinigameStateReader(tooFewEntities).TryRead(out var entityState), "entity count readable");
        Equal(false, entityState.IsActive, "missing Cloud entity inactive");

        var invalidStep = CreateActiveMemory(3);
        Equal(
            false,
            new SquatMinigameStateReader(invalidStep).TryRead(out _),
            "out-of-range visual step fails closed");
    }

    private static void AnnouncesOnlyTheVisibleStepAndResetsBetweenAttempts()
    {
        var tracker = new SquatMinigamePromptTracker();

        Equal("Switch", tracker.Observe(Active(SquatMinigameStep.Switch)), "initial ready cue");
        Equal(null, tracker.Observe(Active(SquatMinigameStep.Switch)), "unchanged cue is not repeated");
        Equal("Cancel", tracker.Observe(Active(SquatMinigameStep.Cancel)), "squat cue");
        Equal("OK", tracker.Observe(Active(SquatMinigameStep.Ok)), "standing cue");
        Equal("Switch", tracker.Observe(Active(SquatMinigameStep.Switch)), "next squat ready cue");
        Equal(null, tracker.Observe(SquatMinigameSnapshot.Inactive), "inactive attempt is silent");
        Equal("Switch", tracker.Observe(Active(SquatMinigameStep.Switch)), "new attempt announces ready cue");
    }

    private static void PreservesTheLastCueAcrossAnUnreadableFrame()
    {
        var memory = CreateActiveMemory(0);
        var reader = new SquatMinigameStateReader(memory);
        var coordinator = new SquatMinigameCueCoordinator(reader);

        Equal("Switch", coordinator.Observe(), "coordinator initial cue");
        memory.Remove((uint)SquatMinigameStateReader.AddressExpectedStep);
        Equal(null, coordinator.Observe(), "unreadable frame is silent");
        memory.Write((uint)SquatMinigameStateReader.AddressExpectedStep, [0]);
        Equal(null, coordinator.Observe(), "recovered unchanged cue is not repeated");
        memory.Write((uint)SquatMinigameStateReader.AddressExpectedStep, [1]);
        Equal("Cancel", coordinator.Observe(), "recovered changed cue is announced");
    }

    private const byte ActivePriority = 3;
    private const uint ScriptPointer = 0x0018_0000;

    private static ContiguousLegacyAddressSpace CreateActiveMemory(
        byte expectedStep,
        byte completedSquats = 0,
        byte activeScriptId = SquatMinigameStateReader.ControllerScriptId,
        byte entityCount = 16)
    {
        var memory = new ContiguousLegacyAddressSpace();
        memory.Write((uint)SquatMinigameStateReader.AddressCurrentModule, [(byte)SquatMinigameStateReader.FieldModule]);
        WriteUInt16(memory, (uint)SquatMinigameStateReader.AddressCurrentFieldId, SquatMinigameStateReader.GymFieldId);
        WriteUInt32(memory, (uint)SquatMinigameStateReader.AddressFieldScriptPointer, ScriptPointer);
        memory.Write(ScriptPointer + SquatMinigameStateReader.FieldScriptEntityCountOffset, [entityCount]);
        memory.Write(
            (uint)(SquatMinigameStateReader.AddressEntityScriptPriorities + SquatMinigameStateReader.CloudEntityId),
            [ActivePriority]);
        memory.Write(
            (uint)(SquatMinigameStateReader.AddressEntityScriptIds +
                SquatMinigameStateReader.CloudEntityId * SquatMinigameStateReader.ScriptSlotsPerEntity +
                ActivePriority),
            [activeScriptId]);
        memory.Write((uint)SquatMinigameStateReader.AddressExpectedStep, [expectedStep]);
        memory.Write((uint)SquatMinigameStateReader.AddressCompletedSquats, [completedSquats]);
        return memory;
    }

    private static SquatMinigameSnapshot Active(SquatMinigameStep step) =>
        new(true, step, 0);

    private static void WriteUInt16(ContiguousLegacyAddressSpace memory, uint address, ushort value) =>
        memory.Write(address, BitConverter.GetBytes(value));

    private static void WriteUInt32(ContiguousLegacyAddressSpace memory, uint address, uint value) =>
        memory.Write(address, BitConverter.GetBytes(value));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
