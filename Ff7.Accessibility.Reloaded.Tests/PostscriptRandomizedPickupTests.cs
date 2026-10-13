using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class PostscriptRandomizedPickupTests
{
    internal static void Run()
    {
        ExactNativeCohortOnly();
        VisibleUncollectedPickupsHaveAccurateGenericLabels();
        StockAndNonPickupLabelsStaySpecific();
    }

    private static void ExactNativeCohortOnly()
    {
        var memory = PatchedCode();
        var reader = new PostscriptItemRandomizerReader(memory);
        Equal(true, reader.RandomizesFieldPickups(), "both native grant hooks and body are recognized");

        memory.Byte(0x0061E66C, 0);
        Equal(false, reader.RandomizesFieldPickups(), "changed item hook rejects the cohort");
        memory = PatchedCode();
        memory.Byte(0x0061EE0A, 0);
        Equal(false, new PostscriptItemRandomizerReader(memory).RandomizesFieldPickups(), "changed materia hook rejects the cohort");
        memory = PatchedCode();
        memory.Byte(0x0063172A, 0);
        Equal(false, new PostscriptItemRandomizerReader(memory).RandomizesFieldPickups(), "missing receipt hook rejects the cohort");
        memory = PatchedCode();
        memory.Byte(0x009A73F5, 0);
        Equal(false, new PostscriptItemRandomizerReader(memory).RandomizesFieldPickups(), "changed item body rejects the cohort");
        memory = PatchedCode();
        memory.Byte(0x009A76ED, 0);
        Equal(false, new PostscriptItemRandomizerReader(memory).RandomizesFieldPickups(), "changed materia body rejects the cohort");
        memory = PatchedCode();
        memory.Remove(0x009A73F5);
        Equal(false, new PostscriptItemRandomizerReader(memory).RandomizesFieldPickups(), "unreadable code has no verified randomizer");
        memory = PatchedCode();
        memory.BeforeRead = count => { if (count == 6) memory.Byte(0x0061E66C, 0); };
        Equal(false, new PostscriptItemRandomizerReader(memory).RandomizesFieldPickups(), "changing code across bookends is rejected");
    }

    private static void VisibleUncollectedPickupsHaveAccurateGenericLabels()
    {
        var memory = VisibleObjects();
        var reader = Objects(memory, memory.Randomizes);
        var targets = reader.ReadTargets(Position);
        Equal(4, targets.Count, "only native visible and uncollected objects survive");
        Equal("Item pickup", targets.Single(t => t.TriggerEntityId == 1).Label, "unknown randomized item reward is unnamed");
        Equal("Materia pickup", targets.Single(t => t.TriggerEntityId == 2).Label, "unknown randomized materia reward is unnamed");
        Equal("Talk to shopkeeper", targets.Single(t => t.TriggerEntityId == 3).Label, "named service is unchanged");
        Equal("Save Point", targets.Single(t => t.TriggerEntityId == 4).Label, "save point is unchanged");
        Equal(12, targets.Single(t => t.TriggerEntityId == 1).X, "live position retained");
        Equal(FieldObjectCueKind.Chest, targets.Single(t => t.TriggerEntityId == 1).ObjectCueKind, "chest cue retained");
        Equal(true, targets.Single(t => t.TriggerEntityId == 1).StableId.Contains("object:120:1:Item:7"), "native stable identity retained");
        Equal(1, memory.PatchChecks, "predicate sampled once per complete target read");

        memory.Byte(FieldNavigationObjectReader.AddressFieldBankBase + 0x20, 1);
        Equal(false, reader.ReadTargets(Position).Any(t => t.TriggerEntityId == 1), "collected item disappears under randomization");
        memory.Byte(EventAddress(2) + FieldNavigationObjectReader.VisibilityOffset, 0);
        Equal(false, reader.ReadTargets(Position).Any(t => t.TriggerEntityId == 2), "hidden materia disappears under randomization");
    }

    private static void StockAndNonPickupLabelsStaySpecific()
    {
        var memory = VisibleObjects();
        var targets = Objects(memory, () => false).ReadTargets(Position);
        Equal("Potion, quantity 3", targets.Single(t => t.TriggerEntityId == 1).Label, "stock item reward and quantity");
        Equal("Restore Materia", targets.Single(t => t.TriggerEntityId == 2).Label, "stock materia reward");
        Equal("Talk to shopkeeper", targets.Single(t => t.TriggerEntityId == 3).Label, "stock named service");
        Equal("Save Point", targets.Single(t => t.TriggerEntityId == 4).Label, "stock save point");
        Equal("Potion, quantity 3", Objects(memory, null).ReadTargets(Position).Single(t => t.TriggerEntityId == 1).Label,
            "omitted predicate defaults to stock");
    }

    private static readonly FieldPositionSnapshot Position = new(1, 120, 0, 0, 0, 0, 0, 0);
    private const int EventTable = 0x02000000;

    private static FieldNavigationObjectReader Objects(ObjectMemory memory, Func<bool>? randomizes)
    {
        var definitions = new[]
        {
            new FieldNavigationObjectDefinition(120, 1, FieldNavigationObjectKind.Item, NativeId: 7, Quantity: 3,
                CollectedBank: 1, CollectedAddress: 0x20, CollectedMask: 1, SourceModelResource: "fieldbg_trbox.char"),
            new FieldNavigationObjectDefinition(120, 2, FieldNavigationObjectKind.Materia, NativeId: 8),
            new FieldNavigationObjectDefinition(120, 3, FieldNavigationObjectKind.Named, Label: "Talk to shopkeeper"),
            new FieldNavigationObjectDefinition(120, 4, FieldNavigationObjectKind.SavePoint),
            new FieldNavigationObjectDefinition(120, 5, FieldNavigationObjectKind.Item, NativeId: 7),
            new FieldNavigationObjectDefinition(120, 6, FieldNavigationObjectKind.Item, NativeId: 7,
                CollectedBank: 1, CollectedAddress: 0x21, CollectedMask: 1)
        };
        return new FieldNavigationObjectReader(memory.ReadInt32, memory.ReadByte,
            _ => "Potion", _ => "Restore", definitions, randomizesPickups: randomizes);
    }

    private static ObjectMemory VisibleObjects()
    {
        var memory = new ObjectMemory();
        memory.Int32(FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
        memory.Byte(FieldPositionReader.AddressFieldNumModels, 8);
        for (var entity = 1; entity <= 6; entity++)
        {
            memory.Byte(FieldNavigationObjectReader.AddressFieldModelIdArray + entity, (byte)entity);
            memory.Byte(EventAddress(entity) + FieldNavigationObjectReader.VisibilityOffset, entity == 5 ? (byte)0 : (byte)1);
            memory.Int32(EventAddress(entity) + FieldNavigationObjectReader.PositionXOffset, entity * 12 * 4096);
            memory.Int32(EventAddress(entity) + FieldNavigationObjectReader.PositionYOffset, entity * 4 * 4096);
            memory.Int32(EventAddress(entity) + FieldNavigationObjectReader.PositionZOffset, 0);
        }
        memory.Byte(FieldNavigationObjectReader.AddressFieldBankBase + 0x21, 1);
        return memory;
    }

    private static int EventAddress(int model) => EventTable + model * FieldNavigationObjectReader.FieldEventDataStride;

    private static CodeMemory PatchedCode()
    {
        var memory = new CodeMemory();
        memory.Write(0x0061E66C, "E9848D3800");
        memory.Write(0x0061EE0A, "E8DE883800");
        memory.Write(0x0063172A, "E9495D37009090");
        memory.Write(0x009A73F5, "66813DD015CC004501740380F9550F84");
        memory.Write(0x009A76ED, "E8ADFFFFFF803D250EDC0003750FC605250EDC000066FF050A0EDC00C3C605250EDC0002C3");
        return memory;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private sealed class ObjectMemory
    {
        private readonly Dictionary<int, byte> bytes = [];
        public int PatchChecks { get; private set; }
        public void Byte(int address, byte value) => bytes[address] = value;
        public void Int32(int address, int value)
        {
            for (var index = 0; index < 4; index++) Byte(address + index, (byte)(value >> (index * 8)));
        }
        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);
        public int ReadInt32(int address) => ReadByte(address) | ReadByte(address + 1) << 8 |
            ReadByte(address + 2) << 16 | ReadByte(address + 3) << 24;
        public bool Randomizes()
        {
            PatchChecks++;
            return true;
        }
    }

    private sealed class CodeMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private int readCount;
        public Action<int>? BeforeRead { get; set; }
        public void Byte(uint address, byte value) => bytes[address] = value;
        public void Remove(uint address) => bytes.Remove(address);
        public void Write(uint address, string hex)
        {
            var data = Convert.FromHexString(hex);
            for (var index = 0; index < data.Length; index++) bytes[address + (uint)index] = data[index];
        }
        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            BeforeRead?.Invoke(++readCount);
            for (var index = 0; index < destination.Length; index++)
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index])) return false;
            return true;
        }
    }
}
