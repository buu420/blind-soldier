using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapStateReaderTests
{
    internal static void Run()
    {
        ReadsCoherentNativeWorldPlayerState();
        RejectsOtherModulesAndNullPlayer();
        RejectsTornNestedPlayerState();
    }

    private static void ReadsCoherentNativeWorldPlayerState()
    {
        const int player = 0x0012_3000;
        var bytes = new Dictionary<int, byte>();
        WriteByte(bytes, WorldMapStateReader.AddressCurrentModule, WorldMapStateReader.WorldModule);
        WriteInt32(bytes, WorldMapStateReader.AddressWorldMapType, 0);
        WriteInt32(bytes, WorldMapStateReader.AddressWorldProgress, 8);
        WriteUInt16(bytes, WorldMapStateReader.AddressGameMoment, 341);
        WriteUInt32(bytes, WorldMapStateReader.AddressWorldPlayerEntityPointer, player);
        WriteInt32(bytes, WorldMapStateReader.AddressWorldCameraFront, 1024);
        WriteInt32(bytes, player + WorldMapStateReader.PositionXOffset, 181_000);
        WriteInt32(bytes, player + WorldMapStateReader.PositionYOffset, 700);
        WriteInt32(bytes, player + WorldMapStateReader.PositionZOffset, 113_000);
        WriteInt16(bytes, player + WorldMapStateReader.FacingOffset, 1234);
        WriteUInt16(bytes, player + WorldMapStateReader.WalkmapTypeOffset, (ushort)(4 | (2 << 9)));
        WriteInt16(bytes, player + WorldMapStateReader.DirectionOffset, 2345);
        WriteByte(bytes, player + WorldMapStateReader.ModelIdOffset, 0);
        WriteByte(bytes, player + WorldMapStateReader.MovementSpeedOffset, 30);

        var result = new WorldMapStateReader(new DictionaryLegacyAddressSpace(bytes)).Read();

        Equal(true, result.IsUsable, "world state is usable");
        Equal(181_000, result.State.X, "world x");
        Equal(700, result.State.Y, "world elevation");
        Equal(113_000, result.State.Z, "world z");
        Equal(4, result.State.TerrainId, "terrain id");
        Equal(2, result.State.RegionId, "region id");
        Equal(0, result.State.PlayerModelId, "player model id");
        Equal(30, result.State.MovementSpeed, "movement speed");
        Equal(341, result.State.GameMoment, "game moment");
        Equal(-64, result.State.ControlTransform.SignedControlDirection, "camera-front control transform");
    }

    private static void RejectsOtherModulesAndNullPlayer()
    {
        var otherModule = CreateHeader(module: 1, playerPointer: 0x0012_3000);
        Equal(
            false,
            new WorldMapStateReader(new DictionaryLegacyAddressSpace(otherModule)).Read().IsUsable,
            "field module is not world state");

        var nullPlayer = CreateHeader(WorldMapStateReader.WorldModule, playerPointer: 0);
        Equal(
            false,
            new WorldMapStateReader(new DictionaryLegacyAddressSpace(nullPlayer)).Read().IsUsable,
            "null player entity fails closed");
    }

    private static void RejectsTornNestedPlayerState()
    {
        const int player = 0x0012_3000;
        var bytes = CreateHeader(WorldMapStateReader.WorldModule, player);
        WriteInt32(bytes, player + WorldMapStateReader.PositionXOffset, 100);
        WriteInt32(bytes, player + WorldMapStateReader.PositionYOffset, 200);
        WriteInt32(bytes, player + WorldMapStateReader.PositionZOffset, 300);
        WriteInt16(bytes, player + WorldMapStateReader.FacingOffset, 0);
        WriteUInt16(bytes, player + WorldMapStateReader.WalkmapTypeOffset, 1);
        WriteInt16(bytes, player + WorldMapStateReader.DirectionOffset, 0);
        WriteByte(bytes, player + WorldMapStateReader.ModelIdOffset, 0);
        WriteByte(bytes, player + WorldMapStateReader.MovementSpeedOffset, 30);

        var result = new WorldMapStateReader(
            new TearingWorldMemory(bytes, (uint)(player + WorldMapStateReader.PositionXOffset))).Read();

        Equal(false, result.IsUsable, "torn nested entity state fails closed");
    }

    private static Dictionary<int, byte> CreateHeader(byte module, int playerPointer)
    {
        var bytes = new Dictionary<int, byte>();
        WriteByte(bytes, WorldMapStateReader.AddressCurrentModule, module);
        WriteInt32(bytes, WorldMapStateReader.AddressWorldMapType, 0);
        WriteInt32(bytes, WorldMapStateReader.AddressWorldProgress, 0);
        WriteUInt16(bytes, WorldMapStateReader.AddressGameMoment, 0);
        WriteUInt32(bytes, WorldMapStateReader.AddressWorldPlayerEntityPointer, playerPointer);
        WriteInt32(bytes, WorldMapStateReader.AddressWorldCameraFront, 0);
        return bytes;
    }

    private static void WriteByte(IDictionary<int, byte> bytes, int address, byte value) =>
        bytes[address] = value;

    private static void WriteInt16(IDictionary<int, byte> bytes, int address, short value) =>
        Write(bytes, address, BitConverter.GetBytes(value));

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

    private sealed class TearingWorldMemory(
        IReadOnlyDictionary<int, byte> bytes,
        uint tearingAddress) : Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace
    {
        private int readCount;

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

            if (virtualAddress == tearingAddress && Interlocked.Increment(ref readCount) >= 2)
            {
                destination[0]++;
            }

            return true;
        }
    }
}
