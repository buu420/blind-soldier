using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapEntityReaderTests
{
    internal static void Run()
    {
        ReadsTheCheckedNativeEntityListAndMarksThePlayer();
        RejectsLoopsAndOtherModules();
    }

    private static void ReadsTheCheckedNativeEntityListAndMarksThePlayer()
    {
        const int cloud = 0x0012_3000;
        const int highwind = 0x0012_3100;
        var bytes = new Dictionary<int, byte>();
        WriteByte(bytes, WorldMapStateReader.AddressCurrentModule, WorldMapStateReader.WorldModule);
        WriteUInt32(bytes, WorldMapEntityReader.AddressEntityListHead, cloud);
        WriteUInt32(bytes, WorldMapStateReader.AddressWorldPlayerEntityPointer, cloud);
        WriteEntity(bytes, cloud, highwind, model: 0, x: 100, z: 200, walkmap: 1);
        WriteEntity(bytes, highwind, 0, model: 3, x: 500, z: 600, walkmap: (ushort)(9 | (2 << 9)));

        var result = new WorldMapEntityReader(new DictionaryLegacyAddressSpace(bytes)).Read();

        Equal(true, result.IsUsable, "entity list usable");
        Equal(2, result.Entities.Count, "entity count");
        Equal(true, result.Entities[0].IsPlayer, "player identity");
        Equal(false, result.Entities[1].IsPlayer, "vehicle identity");
        Equal(3, result.Entities[1].ModelId, "Highwind model");
        Equal(2, result.Entities[1].RegionId, "entity region");
    }

    private static void RejectsLoopsAndOtherModules()
    {
        const int entity = 0x0012_3000;
        var bytes = new Dictionary<int, byte>();
        WriteByte(bytes, WorldMapStateReader.AddressCurrentModule, WorldMapStateReader.WorldModule);
        WriteUInt32(bytes, WorldMapEntityReader.AddressEntityListHead, entity);
        WriteUInt32(bytes, WorldMapStateReader.AddressWorldPlayerEntityPointer, entity);
        WriteEntity(bytes, entity, entity, 0, 0, 0, 0);
        Equal(false, new WorldMapEntityReader(new DictionaryLegacyAddressSpace(bytes)).Read().IsUsable, "loop fails closed");

        WriteByte(bytes, WorldMapStateReader.AddressCurrentModule, 1);
        Equal(false, new WorldMapEntityReader(new DictionaryLegacyAddressSpace(bytes)).Read().IsUsable, "field module fails closed");
    }

    private static void WriteEntity(
        IDictionary<int, byte> bytes,
        int address,
        int next,
        byte model,
        int x,
        int z,
        ushort walkmap)
    {
        WriteUInt32(bytes, address, next);
        WriteInt32(bytes, address + WorldMapStateReader.PositionXOffset, x);
        WriteInt32(bytes, address + WorldMapStateReader.PositionYOffset, 50);
        WriteInt32(bytes, address + WorldMapStateReader.PositionZOffset, z);
        WriteUInt16(bytes, address + WorldMapStateReader.WalkmapTypeOffset, walkmap);
        WriteByte(bytes, address + WorldMapStateReader.ModelIdOffset, model);
        WriteByte(bytes, address + 0x51, 1);
    }

    private static void WriteByte(IDictionary<int, byte> bytes, int address, byte value) => bytes[address] = value;
    private static void WriteUInt16(IDictionary<int, byte> bytes, int address, ushort value) => Write(bytes, address, BitConverter.GetBytes(value));
    private static void WriteInt32(IDictionary<int, byte> bytes, int address, int value) => Write(bytes, address, BitConverter.GetBytes(value));
    private static void WriteUInt32(IDictionary<int, byte> bytes, int address, int value) => Write(bytes, address, BitConverter.GetBytes(unchecked((uint)value)));

    private static void Write(IDictionary<int, byte> bytes, int address, IReadOnlyList<byte> value)
    {
        for (var index = 0; index < value.Count; index++) bytes[address + index] = value[index];
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
    }
}
