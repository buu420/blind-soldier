using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class PostscriptMateriaDropTests
{
    internal static void Run()
    {
        ExactNativePatchRequired();
        BattleResultsAcceptOnlyPatchedMateriaAliases();
        LoadedMateriaTextOwnsAliasNames();
    }

    private static void ExactNativePatchRequired()
    {
        var memory = PatchedMemory();
        Equal(true, new PostscriptMateriaDropCompatibility(memory).HasMateriaDropAliases(), "complete native cohort");
        foreach (var address in new uint[]
                 { 0x005CA975, 0x00419B73, 0x005CAD0C, 0x006C716D, 0x006C71C2, 0x005C8551,
                   0x00916350, 0x0091637B, 0x009163EE })
        {
            memory = PatchedMemory();
            memory.Byte(address, 0);
            Equal(false, new PostscriptMateriaDropCompatibility(memory).HasMateriaDropAliases(),
                $"changed native code at {address:X8}");
        }
        memory = PatchedMemory();
        memory.Remove(0x0091637B);
        Equal(false, new PostscriptMateriaDropCompatibility(memory).HasMateriaDropAliases(), "unreadable body");
        memory = PatchedMemory();
        var firstGateReads = 0;
        memory.BeforeRead = address =>
        {
            if (address == 0x005CA975 && ++firstGateReads == 2)
                memory.Byte(0x005CA975, 0);
        };
        Equal(false, new PostscriptMateriaDropCompatibility(memory).HasMateriaDropAliases(), "code changes between bookends");
    }

    private static void BattleResultsAcceptOnlyPatchedMateriaAliases()
    {
        var memory = PatchedMemory();
        Results(memory, 0x140);
        var gate = new PostscriptMateriaDropCompatibility(memory);
        var reader = new BattleResultsReader(memory, id => id switch
        {
            0x140 => "MP Plus Materia",
            0x19F => "Final Materia",
            7 => "Potion",
            _ => null
        }, gate.HasMateriaDropAliases);
        var result = reader.Read();
        Equal(true, result.IsValid, "first materia alias keeps the results snapshot valid");
        Equal(125, result.Experience, "XP remains available");
        Equal(8, result.Ap, "AP remains available");
        Equal(96, result.Gil, "Gil remains available");
        Equal(0x140, result.Items.Single().ItemId, "native reward ID retained");
        Equal("MP Plus Materia", result.Items.Single().Name, "loaded alias name presented");
        Equal(2, result.Items.Single().Quantity, "visible native quantity retained");
        var delegateReader = new BattleResultsReader(memory.ReadByte, memory.ReadUInt16, memory.ReadInt32,
            id => id == 0x140 ? "MP Plus Materia" : null, gate.HasMateriaDropAliases);
        Equal("MP Plus Materia", delegateReader.Read().Items.Single().Name,
            "delegate-based results reader accepts the same verified alias");

        Results(memory, 0x19F);
        Equal("Final Materia", reader.Read().Items.Single().Name, "last supported materia alias");
        Results(memory, 0x1A0);
        Equal(false, reader.Read().IsValid, "out-of-range alias rejected");
        Results(memory, 7);
        Equal("Potion", reader.Read().Items.Single().Name, "ordinary item remains valid");
        Results(memory, 0x140);
        memory.Byte(0x00419B73, 0);
        Equal(false, reader.Read().IsValid, "disabled materia patch rejects alias");
        Equal(false, new BattleResultsReader(memory, _ => "Wrong stock item").Read().IsValid,
            "default stock reader rejects alias");
        memory = PatchedMemory();
        Results(memory, 0x140);
        memory.Remove(0x009163EE);
        Equal(false, new BattleResultsReader(memory, _ => "Wrong item",
            new PostscriptMateriaDropCompatibility(memory).HasMateriaDropAliases).Read().IsValid,
            "unknown patch remains invalid");
        memory = PatchedMemory();
        Results(memory, 0x140);
        var nativeGate = new PostscriptMateriaDropCompatibility(memory);
        var reads = 0;
        memory.BeforeRead = address =>
        {
            if (address == 0x005CA975 && ++reads == 3)
                memory.Byte(0x005CA975, 0);
        };
        Equal(false, new BattleResultsReader(memory, _ => "MP Plus Materia", nativeGate.HasMateriaDropAliases).Read().IsValid,
            "code change during results capture invalidates speech");
        memory = PatchedMemory();
        Results(memory, 0x140);
        Equal(false, new BattleResultsReader(memory, _ => null,
            new PostscriptMateriaDropCompatibility(memory).HasMateriaDropAliases).Read().IsValid,
            "unreadable visible materia name invalidates the reward snapshot");
    }

    private static void LoadedMateriaTextOwnsAliasNames()
    {
        var database = DiskDatabase();
        var memory = PatchedMemory();
        database.UseNativeText(memory);
        database.UseVerifiedNativeText((bank, index) => (bank, index) switch
        {
            (14, 0) => Encode("Loaded MP Plus"),
            (14, 95) => Encode("Loaded last Materia"),
            _ => null
        }, () => true);
        Equal("Loaded MP Plus", database.ResolveInventoryObjectName(0x140), "first alias uses loaded name");
        Equal(null, database.ResolveInventoryObjectDescription(0x140), "unreviewed item help remains unavailable");
        Equal("Loaded last Materia", database.ResolveInventoryObjectName(0x19F), "last alias uses loaded name");
        Equal(null, database.ResolveInventoryObjectName(0x1A0), "out-of-range alias has no name");
        database.UseVerifiedNativeText((bank, index) =>
        {
            if (bank == 14 && index == 0)
                memory.Byte(0x005CA975, 0);
            return Encode("Changing Materia");
        }, () => true);
        Equal(null, database.ResolveInventoryObjectName(0x140),
            "code changing during loaded-name read cannot publish an alias");
        memory.Byte(0x005CA975, 0);
        Equal(null, database.ResolveInventoryObjectName(0x140), "disabled native patch does not reinterpret materia");
    }

    private static Kernel2TextDatabase DiskDatabase()
    {
        int[] counts = [32, 256, 128, 128, 32, 32, 96, 64, 32, 256, 128, 128, 32, 32, 96, 64, 128, 16];
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            for (var bank = 0; bank < counts.Length; bank++)
            {
                var encoded = Encode(bank == 14 ? "Disk materia" : "Disk text");
                writer.Write(counts[bank] * 2 + encoded.Length);
                for (var id = 0; id < counts[bank]; id++) writer.Write((ushort)(counts[bank] * 2));
                writer.Write(encoded);
            }
        }
        return Kernel2TextDatabase.TryCreateFromDecodedKernel2(stream.ToArray())
            ?? throw new InvalidOperationException("Disk fixture must parse.");
    }

    private static byte[] Encode(string text) => [.. text.Select(character => (byte)(character - 32)), 255];

    private static Memory PatchedMemory()
    {
        var memory = new Memory();
        memory.Write(0x005CA975, "E9D6B9340090");
        memory.Write(0x00419B73, "E903C84F00");
        memory.Write(0x005CAD0C, "E997B63400");
        memory.Write(0x006C716D, "0FBF45E06BC006E857F22400669051");
        memory.Write(0x006C71C2, "E809F224006690");
        memory.Write(0x005C8551, "E898DE34006690");
        memory.Write(0x00916350, "81E2FFFF000081FA400100000F8C1946CBFF81EA40010000");
        memory.Write(0x0091637B, "8B45EC25FFFF00003D400100007D076A08E9E737B0FF2D400100006817D9CE3D6A0750E86307DBFFE9DD");
        memory.Write(0x009163EE, "0FBF88F2E2990081FA400100007D01C36681EA40015152E8E05CDBFF5A594985C975F258E96422CBFF");
        return memory;
    }

    private static void Results(Memory memory, ushort itemId)
    {
        memory.Byte((uint)BattleResultsReader.AddressCurrentModule, BattleResultsReader.ResultsModule);
        memory.Int32(BattleResultsReader.AddressResultsState, 2);
        memory.Byte(BattleResultsReader.AddressResultsPageReady, 1);
        memory.Int32(BattleResultsReader.AddressExperience, 125);
        memory.Int32(BattleResultsReader.AddressAp, 8);
        memory.Int32(BattleResultsReader.AddressGil, 96);
        memory.Int32(BattleResultsReader.AddressHasRewardItems, 1);
        memory.Int32(BattleResultsReader.AddressRewardSelection, 1);
        memory.UInt16(BattleResultsReader.AddressRewardTransition, 0);
        memory.Int32(BattleResultsReader.AddressInputEdges, 0);
        memory.Int32(BattleResultsReader.AddressInputRepeat, 0);
        memory.Int32(BattleResultsReader.AddressHeldInput, 0);
        for (var slot = 0; slot < BattleResultsReader.RewardItemCount; slot++)
        {
            var address = BattleResultsReader.AddressRewardItems + slot * BattleResultsReader.RewardItemSize;
            memory.UInt16(address, slot == 0 ? itemId : ushort.MaxValue);
            memory.UInt16(address + 2, slot == 0 ? (ushort)2 : (ushort)0);
            memory.UInt16(address + 4, 0);
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        public Action<uint>? BeforeRead { get; set; }
        public void Byte(uint address, byte value) => bytes[address] = value;
        public void Remove(uint address) => bytes.Remove(address);
        public void Write(uint address, string hex) => Write(address, Convert.FromHexString(hex));
        public void Write(uint address, byte[] data)
        {
            for (var index = 0; index < data.Length; index++) bytes[address + (uint)index] = data[index];
        }
        public void UInt16(int address, ushort value) => Write((uint)address, BitConverter.GetBytes(value));
        public void Int32(int address, int value) => Write((uint)address, BitConverter.GetBytes(value));
        public byte ReadByte(int address) => bytes.GetValueOrDefault((uint)address);
        public ushort ReadUInt16(int address) => (ushort)(ReadByte(address) | ReadByte(address + 1) << 8);
        public int ReadInt32(int address) => ReadByte(address) | ReadByte(address + 1) << 8 |
            ReadByte(address + 2) << 16 | ReadByte(address + 3) << 24;
        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            BeforeRead?.Invoke(virtualAddress);
            for (var index = 0; index < destination.Length; index++)
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index])) return false;
            return true;
        }
    }
}
