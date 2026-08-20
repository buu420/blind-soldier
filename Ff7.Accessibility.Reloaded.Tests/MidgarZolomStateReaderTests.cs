using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class MidgarZolomStateReaderTests
{
    internal static void Run()
    {
        ReadsAndTranslatesTheCoherentNativeHeadRecord();
        RepresentsAnInactiveZolomWithoutTrustingAStalePointer();
        RejectsInvalidPointersAndTornRecords();
    }

    private static void ReadsAndTranslatesTheCoherentNativeHeadRecord()
    {
        var bytes = CreateHeader(active: true, MidgarZolomStateReader.AddressPositionHistoryStart);
        WriteUInt16(bytes, MidgarZolomStateReader.AddressPositionHistoryStart, 0x2008);
        WriteUInt16(bytes, MidgarZolomStateReader.AddressPositionHistoryStart + 2, 0x6338);
        WriteUInt16(bytes, MidgarZolomStateReader.AddressPositionHistoryStart + 4, 0x0345);
        WriteUInt16(bytes, MidgarZolomStateReader.AddressPositionHistoryStart + 6, 0x0007);

        var result = new MidgarZolomStateReader(new DictionaryLegacyAddressSpace(bytes)).Read();

        Equal(true, result.IsUsable, "native Zolom frame is usable");
        Equal(true, result.State.IsActive, "native Zolom is active");
        Equal(221_192, result.State.X, "translated Zolom x");
        Equal(156_472, result.State.Z, "translated Zolom z");
        Equal((ushort)0x0345, result.State.Direction, "native Zolom direction");
    }

    private static void RepresentsAnInactiveZolomWithoutTrustingAStalePointer()
    {
        var bytes = CreateHeader(active: false, unchecked((int)0xDEADBEEF));

        var result = new MidgarZolomStateReader(new DictionaryLegacyAddressSpace(bytes)).Read();

        Equal(true, result.IsUsable, "inactive native Zolom state is coherent");
        Equal(false, result.State.IsActive, "inactive native Zolom stays inactive");
    }

    private static void RejectsInvalidPointersAndTornRecords()
    {
        var misaligned = CreateHeader(
            active: true,
            MidgarZolomStateReader.AddressPositionHistoryStart + 2);
        Equal(
            false,
            new MidgarZolomStateReader(new DictionaryLegacyAddressSpace(misaligned)).Read().IsUsable,
            "misaligned native Zolom ring pointer fails closed");

        var outside = CreateHeader(
            active: true,
            MidgarZolomStateReader.AddressPositionHistoryEnd);
        Equal(
            false,
            new MidgarZolomStateReader(new DictionaryLegacyAddressSpace(outside)).Read().IsUsable,
            "out-of-range native Zolom ring pointer fails closed");

        var torn = CreateHeader(active: true, MidgarZolomStateReader.AddressPositionHistoryStart);
        WriteUInt16(torn, MidgarZolomStateReader.AddressPositionHistoryStart, 0x2008);
        WriteUInt16(torn, MidgarZolomStateReader.AddressPositionHistoryStart + 2, 0x6338);
        WriteUInt16(torn, MidgarZolomStateReader.AddressPositionHistoryStart + 4, 0x0345);
        WriteUInt16(torn, MidgarZolomStateReader.AddressPositionHistoryStart + 6, 0x0007);
        Equal(
            false,
            new MidgarZolomStateReader(
                new TearingZolomMemory(
                    torn,
                    (uint)MidgarZolomStateReader.AddressPositionHistoryStart)).Read().IsUsable,
            "torn native Zolom record fails closed");
    }

    private static Dictionary<int, byte> CreateHeader(bool active, int positionPointer)
    {
        var bytes = new Dictionary<int, byte>();
        WriteByte(bytes, WorldMapStateReader.AddressCurrentModule, WorldMapStateReader.WorldModule);
        WriteInt32(bytes, WorldMapStateReader.AddressWorldMapType, 0);
        WriteByte(bytes, MidgarZolomStateReader.AddressEnabled, active ? (byte)1 : (byte)0);
        WriteUInt32(bytes, MidgarZolomStateReader.AddressCurrentPositionPointer, positionPointer);
        return bytes;
    }

    private static void WriteByte(IDictionary<int, byte> bytes, int address, byte value) =>
        bytes[address] = value;

    private static void WriteUInt16(IDictionary<int, byte> bytes, int address, ushort value) =>
        Write(bytes, address, BitConverter.GetBytes(value));

    private static void WriteInt32(IDictionary<int, byte> bytes, int address, int value) =>
        Write(bytes, address, BitConverter.GetBytes(value));

    private static void WriteUInt32(IDictionary<int, byte> bytes, int address, int value) =>
        Write(bytes, address, BitConverter.GetBytes(unchecked((uint)value)));

    private static void Write(IDictionary<int, byte> bytes, int address, IReadOnlyList<byte> value)
    {
        for (var index = 0; index < value.Count; index++)
        {
            bytes[address + index] = value[index];
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }

    private sealed class TearingZolomMemory(
        IReadOnlyDictionary<int, byte> bytes,
        uint tearingAddress) : Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace
    {
        private int reads;

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(checked((int)virtualAddress + index), out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            if (virtualAddress == tearingAddress &&
                destination.Length == sizeof(ushort) &&
                Interlocked.Increment(ref reads) >= 2)
            {
                destination[0]++;
            }

            return true;
        }
    }
}
