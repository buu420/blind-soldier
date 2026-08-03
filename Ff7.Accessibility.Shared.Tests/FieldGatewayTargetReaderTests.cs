using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class FieldGatewayTargetReaderTests
{
    private const uint TriggerPointer = 0x00060000;
    private static readonly uint GatewayTable =
        TriggerPointer + FieldGatewayTargetReader.GatewaysOffset;

    public static void Run()
    {
        LivesInSharedAssemblyAndRequiresCheckedGuestMemory();
        ReadsACompleteStableGatewaySnapshotWithGenericLabels();
        RejectsUnreadableOverflowingAndTornSnapshotsAllOrNothing();
    }

    private static void LivesInSharedAssemblyAndRequiresCheckedGuestMemory()
    {
        var sharedAssembly = typeof(ILegacyAddressSpace).Assembly;
        Equal(sharedAssembly, typeof(FieldGatewayTargetReader).Assembly, "gateway reader shared assembly");
        Equal(sharedAssembly, typeof(FieldNavigationTarget).Assembly, "navigation target shared assembly");
        Equal(sharedAssembly, typeof(FieldNavigationCategory).Assembly, "navigation category shared assembly");
        Equal(
            typeof(ILegacyAddressSpace),
            typeof(FieldGatewayTargetReader).GetConstructors().Single().GetParameters().Single().ParameterType,
            "gateway reader checked address-space constructor");
    }

    private static void ReadsACompleteStableGatewaySnapshotWithGenericLabels()
    {
        var memory = CreateStableMemory();
        WriteGateway(memory, 0, 996, 285, 1296, 929, 493, 1296, 116);
        WriteGateway(memory, 1, 1019, 1728, 1298, 1298, 1705, 1298, 118);

        var reader = new FieldGatewayTargetReader(memory);
        Equal(true, reader.TryReadTargets(Position(), out var exits), "complete stable gateway success");

        Equal(2, exits.Count, "complete stable gateway count");
        Equal("Exit", exits[0].Label, "first generic visible gateway label");
        Equal(963, exits[0].X, "first gateway midpoint X");
        Equal(389, exits[0].Y, "first gateway midpoint Y");
        Equal(1296, exits[0].Z, "first gateway midpoint Z");
        Equal("gateway:117:0:116", exits[0].StableId, "first gateway metadata identity");
        Equal(116, exits[0].DestinationFieldIds!.Single(), "first destination metadata");
        Equal(
            new FieldNavigationTriggerLine(996, 285, 1296, 929, 493, 1296),
            exits[0].TriggerLine,
            "first native gateway preserves its exact activation line");
        Equal("Exit", exits[1].Label, "second generic visible gateway label");
        Equal(118, exits[1].DestinationFieldIds!.Single(), "second destination metadata");
        Equal(false, exits.Any(exit => exit.Label.Any(char.IsDigit)), "numeric destination is never visible speech text");
        Contains(reader.LastDiagnostic, "count=2", "stable gateway diagnostic count");
        Contains(reader.LastDiagnostic, "destinations=116,118", "destination diagnostic metadata");
    }

    private static void RejectsUnreadableOverflowingAndTornSnapshotsAllOrNothing()
    {
        var stable = CreateStableMemory();
        WriteGateway(stable, 0, 10, 20, 30, 30, 40, 50, 116);
        WriteGateway(stable, 1, 50, 60, 70, 70, 80, 90, 118);

        var unreadable = CreateStableMemory();
        WriteGateway(unreadable, 0, 10, 20, 30, 30, 40, 50, 116);
        unreadable.Remove(GatewayTable + FieldGatewayTargetReader.GatewayStride - 1u);
        var unreadableReader = new FieldGatewayTargetReader(unreadable);
        Equal(
            false,
            unreadableReader.TryReadTargets(Position(), out var unreadableExits),
            "one unreadable gateway byte fails explicitly");
        Equal(0, unreadableExits.Count, "one unreadable gateway byte publishes no exits");

        var overflow = CreateStableMemory(uint.MaxValue - 0x20u);
        var overflowReader = new FieldGatewayTargetReader(overflow);
        Equal(
            false,
            overflowReader.TryReadTargets(Position(), out var overflowExits),
            "gateway range arithmetic overflow fails explicitly");
        Equal(0, overflowExits.Count, "gateway range arithmetic overflow publishes no exits");

        var tornModule = new TearingLegacyAddressSpace(
            stable,
            (uint)FieldPositionReader.AddressCurrentModule,
            [2]);
        Equal(
            0,
            new FieldGatewayTargetReader(tornModule).ReadTargets(Position()).Count,
            "module bookend tear invalidates all exits");

        var tornPointer = new TearingLegacyAddressSpace(
            stable,
            (uint)FieldNavigationControlReader.AddressFieldTriggersPtr,
            BitConverter.GetBytes(0x00070000u));
        Equal(
            0,
            new FieldGatewayTargetReader(tornPointer).ReadTargets(Position()).Count,
            "trigger pointer bookend tear invalidates all exits");

        var changedTable = SnapshotTable(stable);
        changedTable[FieldGatewayTargetReader.DestinationFieldOffset] = 117;
        changedTable[FieldGatewayTargetReader.DestinationFieldOffset + 1] = 0;
        var tornTable = new TearingLegacyAddressSpace(stable, GatewayTable, changedTable);
        Equal(
            0,
            new FieldGatewayTargetReader(tornTable).ReadTargets(Position()).Count,
            "changed complete gateway confirmation invalidates all exits");

        var secondSnapshotFailure = new TearingLegacyAddressSpace(stable, GatewayTable, []);
        Equal(
            0,
            new FieldGatewayTargetReader(secondSnapshotFailure).ReadTargets(Position()).Count,
            "unreadable complete gateway confirmation invalidates all exits");
    }

    private static ContiguousLegacyAddressSpace CreateStableMemory(uint triggerPointer = TriggerPointer)
    {
        var memory = new ContiguousLegacyAddressSpace();
        memory.Write((uint)FieldPositionReader.AddressCurrentModule, [FieldPositionReader.FieldModule]);
        WriteUInt16(memory, (uint)FieldPositionReader.AddressFieldId, 117);
        WriteUInt32(memory, (uint)FieldNavigationControlReader.AddressFieldTriggersPtr, triggerPointer);

        if (triggerPointer <= uint.MaxValue - FieldGatewayTargetReader.GatewaysOffset -
            FieldGatewayTargetReader.GatewayCount * FieldGatewayTargetReader.GatewayStride)
        {
            var table = triggerPointer + FieldGatewayTargetReader.GatewaysOffset;
            memory.Write(table, new byte[FieldGatewayTargetReader.GatewayCount * FieldGatewayTargetReader.GatewayStride]);
            for (var index = 0; index < FieldGatewayTargetReader.GatewayCount; index++)
            {
                WriteUInt16(
                    memory,
                    table + (uint)(index * FieldGatewayTargetReader.GatewayStride +
                        FieldGatewayTargetReader.DestinationFieldOffset),
                    (ushort)short.MaxValue);
            }
        }

        return memory;
    }

    private static void WriteGateway(
        ContiguousLegacyAddressSpace memory,
        int index,
        short x1,
        short y1,
        short z1,
        short x2,
        short y2,
        short z2,
        short destination)
    {
        var address = GatewayTable + (uint)(index * FieldGatewayTargetReader.GatewayStride);
        WriteInt16(memory, address, x1);
        WriteInt16(memory, address + 0x02, y1);
        WriteInt16(memory, address + 0x04, z1);
        WriteInt16(memory, address + 0x06, x2);
        WriteInt16(memory, address + 0x08, y2);
        WriteInt16(memory, address + 0x0A, z2);
        WriteInt16(memory, address + FieldGatewayTargetReader.DestinationFieldOffset, destination);
    }

    private static byte[] SnapshotTable(ILegacyAddressSpace memory)
    {
        var table = new byte[FieldGatewayTargetReader.GatewayCount * FieldGatewayTargetReader.GatewayStride];
        Equal(true, memory.TryRead(GatewayTable, table), "gateway test fixture table read");
        return table;
    }

    private static FieldPositionSnapshot Position() =>
        new(FieldPositionReader.FieldModule, 117, 0, 0, 0, 0, 0, 0);

    private static void WriteInt16(ContiguousLegacyAddressSpace memory, uint address, short value) =>
        memory.Write(address, BitConverter.GetBytes(value));

    private static void WriteUInt16(ContiguousLegacyAddressSpace memory, uint address, ushort value) =>
        memory.Write(address, BitConverter.GetBytes(value));

    private static void WriteUInt32(ContiguousLegacyAddressSpace memory, uint address, uint value) =>
        memory.Write(address, BitConverter.GetBytes(value));

    private static void Contains(string actual, string expected, string label)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}: expected '{actual}' to contain '{expected}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
