using Ff7.Accessibility.LegacyLayout;

internal static class HighwayRoadStateReaderTests
{
    internal static void Run()
    {
        ReadsTheNativeRoadRingAndCloudLateralPosition();
        NormalizesTheNativeRoadRingIndex();
        RejectsUnreadableInvalidAndUnownedRoadState();
    }

    private static void ReadsTheNativeRoadRingAndCloudLateralPosition()
    {
        var memory = CreateRoadMemory(lateralUnits: 40, longitudinalUnits: 512, scroll: 256, widthSample: 512);
        var reader = new HighwayRoadStateReader(memory);

        Equal(true, reader.TryRead(out var snapshot), "valid native highway road snapshot");
        Equal(HighwayStateReader.HighwayModule, snapshot.Module, "native road module owner");
        Equal(40d, snapshot.CloudLateralUnits, "positive Cloud lateral units");
        Equal(160d, snapshot.RoadHalfWidthUnits, "native decoded road half-width");
        Equal(3, snapshot.RoadSampleIndex, "native active road ring index");

        memory = CreateRoadMemory(lateralUnits: -24, longitudinalUnits: 512, scroll: 256, widthSample: 512);
        Equal(
            true,
            new HighwayRoadStateReader(memory).TryRead(out snapshot),
            "negative lateral road snapshot");
        Equal(-24d, snapshot.CloudLateralUnits, "negative Cloud lateral units");
    }

    private static void NormalizesTheNativeRoadRingIndex()
    {
        var wrapped = CreateRoadMemory(
            lateralUnits: 0,
            longitudinalUnits: 0,
            scroll: 81 * 256,
            widthSample: 512,
            expectedSampleIndex: 1);
        Equal(
            true,
            new HighwayRoadStateReader(wrapped).TryRead(out var wrappedSnapshot),
            "wrapped positive road ring index");
        Equal(1, wrappedSnapshot.RoadSampleIndex, "positive modulo-80 ring index");

        var negative = CreateRoadMemory(
            lateralUnits: 0,
            longitudinalUnits: 0,
            scroll: -256,
            widthSample: 512,
            expectedSampleIndex: 79);
        Equal(
            true,
            new HighwayRoadStateReader(negative).TryRead(out var negativeSnapshot),
            "wrapped negative road ring index");
        Equal(79, negativeSnapshot.RoadSampleIndex, "normalized negative modulo-80 ring index");
    }

    private static void RejectsUnreadableInvalidAndUnownedRoadState()
    {
        var unreadable = CreateRoadMemory(0, 512, 256, 512);
        unreadable.Remove((uint)HighwayRoadStateReader.AddressRoadStatePointer);
        Equal(
            false,
            new HighwayRoadStateReader(unreadable).TryRead(out _),
            "unreadable road-state pointer rejects the snapshot");

        var nullPointer = CreateRoadMemory(0, 512, 256, 512);
        WriteUInt32(nullPointer, (uint)HighwayRoadStateReader.AddressRoadStatePointer, 0);
        Equal(
            false,
            new HighwayRoadStateReader(nullPointer).TryRead(out _),
            "null road-state pointer rejects the snapshot");

        var tooNarrow = CreateRoadMemory(0, 512, 256, 200);
        Equal(
            false,
            new HighwayRoadStateReader(tooNarrow).TryRead(out _),
            "implausibly narrow road rejects the snapshot");

        var tornModuleMemory = CreateRoadMemory(0, 512, 256, 512);
        var tornModule = new TearingLegacyAddressSpace(
            tornModuleMemory,
            (uint)HighwayStateReader.AddressCurrentModule,
            [1]);
        Equal(
            false,
            new HighwayRoadStateReader(tornModule).TryRead(out _),
            "module transition rejects the road snapshot");
    }

    private static ContiguousLegacyAddressSpace CreateRoadMemory(
        int lateralUnits,
        int longitudinalUnits,
        int scroll,
        short widthSample,
        int? expectedSampleIndex = null)
    {
        const uint roadState = 0x02000000;
        const uint roadRing = 0x03000000;
        var memory = new ContiguousLegacyAddressSpace();
        memory.Write((uint)HighwayStateReader.AddressCurrentModule, [HighwayStateReader.HighwayModule]);
        WriteInt32(
            memory,
            (uint)HighwayStateReader.AddressActorTable + HighwayStateReader.ActorLateralOffset,
            checked(lateralUnits * 256));
        WriteInt32(
            memory,
            (uint)HighwayStateReader.AddressActorTable + HighwayStateReader.ActorLongitudinalOffset,
            checked(longitudinalUnits * 256));
        WriteInt32(memory, (uint)HighwayRoadStateReader.AddressRoadScroll, scroll);
        WriteUInt32(memory, (uint)HighwayRoadStateReader.AddressRoadStatePointer, roadState);
        WriteUInt32(memory, roadState + HighwayRoadStateReader.RoadRingPointerOffset, roadRing);

        var nativeIndex = ((scroll + (longitudinalUnits * 256 >> 8)) >> 8) %
            HighwayRoadStateReader.RoadSampleCount;
        if (nativeIndex < 0)
        {
            nativeIndex += HighwayRoadStateReader.RoadSampleCount;
        }

        Equal(expectedSampleIndex ?? nativeIndex, nativeIndex, "fixture road ring index");
        var sampleAddress = checked(
            roadRing +
            (uint)(nativeIndex * HighwayRoadStateReader.RoadSampleStride) +
            HighwayRoadStateReader.RoadWidthSampleOffset);
        memory.Write(sampleAddress, BitConverter.GetBytes(widthSample));
        return memory;
    }

    private static void WriteInt32(ContiguousLegacyAddressSpace memory, uint address, int value) =>
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
