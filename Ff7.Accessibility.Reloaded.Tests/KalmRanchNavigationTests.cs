using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class KalmRanchNavigationTests
{
    private const int EventTable = 0x02500000;

    public static void Run()
    {
        AssertKalmStoryProgression();
        AssertChocoboRanchStoryProgression();
        AssertReviewedNpcLabels();
    }

    private static void AssertKalmStoryProgression()
    {
        var memory = new NativeMemory();
        var kalmArrivalAddress =
            FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 128;
        memory.WriteByte(kalmArrivalAddress, 0);

        AssertStoryTarget(
            memory,
            fieldId: 335,
            gameMoment: 341,
            "Follow the party into Kalm",
            expectedX: -360,
            expectedY: -799,
            expectedZ: -2);
        memory.WriteByte(kalmArrivalAddress, 0x02);
        AssertStoryTarget(
            memory,
            fieldId: 335,
            gameMoment: 341,
            "Enter the Kalm inn",
            expectedX: -575,
            expectedY: -448,
            expectedZ: -2);
        AssertStoryTarget(
            memory,
            fieldId: 331,
            gameMoment: 341,
            "Go upstairs and meet the party",
            expectedX: 70,
            expectedY: 124,
            expectedZ: 186);

        memory.WriteByte(FieldNavigationObjectReader.AddressTemporaryFieldBankBase + 6, 0);
        AssertStoryTarget(
            memory,
            fieldId: 332,
            gameMoment: 341,
            "Join Aeris and the party upstairs",
            expectedX: 253,
            expectedY: 115,
            expectedZ: -6);

        memory.WriteByte(FieldNavigationObjectReader.AddressTemporaryFieldBankBase + 6, 1);
        AssertStoryTarget(
            memory,
            fieldId: 332,
            gameMoment: 341,
            "Stand with the party and begin Cloud's story",
            expectedX: 170,
            expectedY: -164,
            expectedZ: -6);

        memory.WriteByte(FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 131, 0);
        AssertStoryTarget(
            memory,
            fieldId: 332,
            gameMoment: 385,
            "Go downstairs after Cloud's story",
            expectedX: -44,
            expectedY: 131,
            expectedZ: -180);
        AssertStoryTarget(
            memory,
            fieldId: 331,
            gameMoment: 385,
            "Meet the party downstairs and receive the PHS",
            expectedX: 74,
            expectedY: -228,
            expectedZ: -1);

        memory.WriteByte(FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 131, 0x01);
        AssertNoStoryTarget(
            memory,
            fieldId: 331,
            gameMoment: 385,
            "the Kalm inn objective must retire after the native PHS flag is set");
    }

    private static void AssertChocoboRanchStoryProgression()
    {
        var memory = new NativeMemory();
        var ranchProgressAddress =
            FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 64;
        memory.WriteByte(ranchProgressAddress, 0);

        AssertStoryTarget(
            memory,
            fieldId: 343,
            gameMoment: 385,
            "Enter the stable and buy the Chocobo Lure",
            expectedX: 911,
            expectedY: 1881,
            expectedZ: 2);

        memory.ConfigureVisibleModel(entityId: 5, modelId: 1, x: 130, y: -510, z: 0);
        AssertStoryTarget(
            memory,
            fieldId: 345,
            gameMoment: 385,
            "Talk to Choco Billy and buy the Chocobo Lure",
            expectedX: 130,
            expectedY: -510,
            expectedZ: 0);

        memory.WriteByte(ranchProgressAddress, 0x40);
        AssertNoStoryTarget(
            memory,
            fieldId: 343,
            gameMoment: 385,
            "the Ranch objective must retire after the native Chocobo Lure flag is set");
        AssertNoStoryTarget(
            memory,
            fieldId: 345,
            gameMoment: 385,
            "the stable objective must retire after the native Chocobo Lure flag is set");

        memory.WriteByte(ranchProgressAddress, 0);
        AssertNoStoryTarget(
            memory,
            fieldId: 343,
            gameMoment: 566,
            "the early-game Ranch objective must not appear during later Chocobo breeding visits");
    }

    private static void AssertReviewedNpcLabels()
    {
        var cases = new[]
        {
            (328, 12, "Weapon shopkeeper"),
            (328, 13, "Materia shopkeeper"),
            (329, 8, "Item shopkeeper"),
            (330, 9, "Bartender"),
            (330, 11, "Man"),
            (330, 12, "Man"),
            (331, 11, "Innkeeper"),
            (333, 7, "Woman"),
            (334, 6, "Girl"),
            (335, 16, "Man"),
            (335, 17, "Old man"),
            (335, 18, "Man"),
            (335, 19, "Woman"),
            (335, 20, "Man"),
            (335, 21, "Man"),
            (335, 22, "Boy"),
            (336, 8, "Old man"),
            (336, 9, "Dog"),
            (338, 6, "Man"),
            (339, 7, "Boy"),
            (339, 8, "Girl"),
            (341, 8, "Woman"),
            (342, 5, "Old man"),
            (342, 6, "Chocobo"),
            (343, 4, "Chocobo"),
            (343, 5, "Chocobo"),
            (344, 4, "Choco Bill"),
            (345, 4, "Chole"),
            (345, 5, "Choco Billy"),
            (345, 7, "Chocobo"),
            (345, 8, "Chocobo"),
            (345, 9, "Chocobo"),
            (345, 10, "Chocobo"),
            (345, 11, "Chocobo"),
            (345, 12, "Chocobo")
        };

        foreach (var (fieldId, entityId, expectedLabel) in cases)
        {
            var memory = new NativeMemory();
            memory.ConfigureVisibleModel(entityId, modelId: 1, x: 100, y: 200, z: 3);
            var reader = new FieldNavigationNpcReader(
                memory.ReadInt32,
                memory.ReadInt16,
                memory.ReadByte,
                (_, _) => ["Ordinary dialogue without a speaker heading."],
                _ => []);
            var targets = reader.ReadTargets(
                new FieldPositionSnapshot(1, fieldId, 0, 0, 0, 0, 0, 0));

            AssertEqual(
                1,
                targets.Count,
                $"reviewed NPC count for field {fieldId}, entity {entityId}");
            AssertEqual(
                expectedLabel,
                targets.Single().Label,
                $"reviewed NPC label for field {fieldId}, entity {entityId}");
        }
    }

    private static void AssertStoryTarget(
        NativeMemory memory,
        int fieldId,
        int gameMoment,
        string expectedLabel,
        int expectedX,
        int expectedY,
        int expectedZ)
    {
        memory.SetGameMoment(gameMoment);
        var reader = new FieldStoryTargetReader(
            memory.ReadInt32,
            memory.ReadInt16,
            memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields());
        var targets = reader.ReadTargets(
            new FieldPositionSnapshot(1, fieldId, 0, 0, 0, 0, 0, 0));
        var target = targets.SingleOrDefault(candidate => candidate.Label == expectedLabel);

        AssertEqual(
            true,
            target.Label == expectedLabel,
            $"story target '{expectedLabel}' in field {fieldId} at moment {gameMoment}");
        AssertEqual(expectedX, target.X, $"{expectedLabel} x");
        AssertEqual(expectedY, target.Y, $"{expectedLabel} y");
        AssertEqual(expectedZ, target.Z, $"{expectedLabel} z");
    }

    private static void AssertNoStoryTarget(
        NativeMemory memory,
        int fieldId,
        int gameMoment,
        string message)
    {
        memory.SetGameMoment(gameMoment);
        var reader = new FieldStoryTargetReader(
            memory.ReadInt32,
            memory.ReadInt16,
            memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields());
        AssertEqual(
            0,
            reader.ReadTargets(new FieldPositionSnapshot(1, fieldId, 0, 0, 0, 0, 0, 0)).Count,
            message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message}: expected {expected}, got {actual}");
        }
    }

    private sealed class NativeMemory
    {
        private readonly Dictionary<int, byte> bytes = [];

        public NativeMemory()
        {
            WriteUInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
            WriteByte(FieldPositionReader.AddressFieldNumModels, 2);
        }

        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);

        public short ReadInt16(int address) => unchecked((short)(
            ReadByte(address) |
            (ReadByte(address + 1) << 8)));

        public int ReadInt32(int address) =>
            ReadByte(address) |
            (ReadByte(address + 1) << 8) |
            (ReadByte(address + 2) << 16) |
            (ReadByte(address + 3) << 24);

        public void WriteByte(int address, byte value) => bytes[address] = value;

        public void SetGameMoment(int value)
        {
            WriteByte(FieldNavigationObjectReader.AddressFieldBankBase, (byte)value);
            WriteByte(FieldNavigationObjectReader.AddressFieldBankBase + 1, (byte)(value >> 8));
        }

        public void ConfigureVisibleModel(int entityId, byte modelId, int x, int y, int z)
        {
            WriteByte(FieldNavigationObjectReader.AddressFieldModelIdArray + entityId, modelId);
            var eventAddress = EventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
            WriteByte(eventAddress + FieldNavigationObjectReader.VisibilityOffset, 1);
            WriteByte(eventAddress + FieldNavigationNpcReader.TalkDisabledOffset, 0);
            WriteUInt32(eventAddress + FieldNavigationObjectReader.PositionXOffset, x * 4096);
            WriteUInt32(eventAddress + FieldNavigationObjectReader.PositionYOffset, y * 4096);
            WriteUInt32(eventAddress + FieldNavigationObjectReader.PositionZOffset, z * 4096);
        }

        private void WriteUInt32(int address, int value)
        {
            WriteByte(address, (byte)value);
            WriteByte(address + 1, (byte)(value >> 8));
            WriteByte(address + 2, (byte)(value >> 16));
            WriteByte(address + 3, (byte)(value >> 24));
        }
    }
}
