using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class KalmRanchNavigationTests
{
    private const int EventTable = 0x02500000;

    public static void Run()
    {
        AssertKalmStoryProgression();
        AssertNibelheimFlashbackStoryProgression();
        AssertNibelheimFlashbackObjects();
        AssertChocoboRanchStoryProgression();
        AssertReviewedNpcLabels();
    }

    public static void RunNibelheimFlashbackOnly()
    {
        AssertNibelheimFlashbackStoryProgression();
        AssertNibelheimFlashbackObjects();
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

    private static void AssertNibelheimFlashbackStoryProgression()
    {
        var memory = new NativeMemory();
        var flashbackTownAddress =
            FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 18;
        var innConversationAddress =
            FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 19;

        memory.WriteByte(flashbackTownAddress, 0x01);
        AssertStoryTarget(
            memory,
            fieldId: 282,
            gameMoment: 353,
            "Enter the inn and meet Sephiroth",
            expectedX: -170,
            expectedY: -334,
            expectedZ: 0);
        AssertStoryTarget(
            memory,
            fieldId: 273,
            gameMoment: 353,
            "Go upstairs to Sephiroth",
            expectedX: 168,
            expectedY: -142,
            expectedZ: 168);

        memory.ConfigureVisibleModel(entityId: 8, modelId: 1, x: 90, y: -80, z: 0);
        AssertStoryTarget(
            memory,
            fieldId: 274,
            gameMoment: 353,
            "Talk to Sephiroth about the reactor mission",
            expectedX: 90,
            expectedY: -80,
            expectedZ: 0);

        memory.WriteByte(innConversationAddress, 0x02);
        AssertStoryTarget(
            memory,
            fieldId: 274,
            gameMoment: 353,
            "Talk to Sephiroth again and choose sleep",
            expectedX: 90,
            expectedY: -80,
            expectedZ: 0);

        memory.WriteByte(flashbackTownAddress, 0x03);
        memory.ConfigureVisibleModel(entityId: 8, modelId: 1, x: 112, y: 3021, z: 205);
        AssertStoryTarget(
            memory,
            fieldId: 282,
            gameMoment: 353,
            "Talk to Sephiroth to begin the Mt. Nibel expedition",
            expectedX: 112,
            expectedY: 3021,
            expectedZ: 205);

        memory.WriteByte(flashbackTownAddress, 0x0B);
        AssertNoStoryTarget(
            memory,
            fieldId: 282,
            gameMoment: 353,
            "the town objective must retire when the expedition begins");

        memory.ConfigureVisibleModel(entityId: 6, modelId: 1, x: 2468, y: -988, z: 984);
        AssertStoryTarget(
            memory,
            fieldId: 312,
            gameMoment: 357,
            "Talk to Tifa before crossing the bridge",
            expectedX: 2468,
            expectedY: -988,
            expectedZ: 984);

        var mountainRoutes = new[]
        {
            (312, 359, "Cross the bridge toward Mt. Nibel", 2560, -1008, 985),
            (313, 361, "Continue into the Mt. Nibel caves", 912, 740, -210),
            (318, 361, "Continue through the cave passage", -142, 1788, -416),
            (318, 362, "Continue through the cave passage", -142, 1788, -416),
            (315, 364, "Continue toward the Nibel Reactor", -574, -592, 32)
        };
        foreach (var (fieldId, moment, label, x, y, z) in mountainRoutes)
        {
            AssertStoryTarget(memory, fieldId, moment, label, x, y, z);
        }

        AssertStoryTargetAtTriangle(
            memory,
            fieldId: 322,
            gameMoment: 366,
            playerTriangle: 48,
            "Climb down the ladder into the Nibel Reactor",
            expectedX: -124,
            expectedY: 520,
            expectedZ: 1068,
            expectedTriggerLine: new FieldNavigationTriggerLine(
                -82,
                476,
                1068,
                -166,
                564,
                1068),
            expectedCompletesOnArrival: false);
        AssertStoryTargetAtTriangle(
            memory,
            fieldId: 322,
            gameMoment: 366,
            playerTriangle: 24,
            "Enter the Nibel Reactor core",
            expectedX: -6,
            expectedY: -912,
            expectedZ: 191);

        memory.ConfigureVisibleModel(entityId: 8, modelId: 1, x: 124, y: -401, z: 186);
        AssertStoryTarget(
            memory,
            fieldId: 323,
            gameMoment: 366,
            "Talk to Sephiroth inside the reactor",
            expectedX: 124,
            expectedY: -401,
            expectedZ: 186);
        AssertStoryTarget(
            memory,
            fieldId: 323,
            gameMoment: 367,
            "Close the reactor valve",
            expectedX: 128,
            expectedY: -235,
            expectedZ: 186);
        AssertStoryTarget(
            memory,
            fieldId: 323,
            gameMoment: 368,
            "Return to Sephiroth after closing the valve",
            expectedX: 124,
            expectedY: -401,
            expectedZ: 186);
        AssertStoryTarget(
            memory,
            fieldId: 323,
            gameMoment: 369,
            "Talk to Sephiroth and inspect the pod",
            expectedX: 124,
            expectedY: -401,
            expectedZ: 186);

        var firstMansionVisit = new[]
        {
            (282, "Enter Shinra Mansion and find Sephiroth", -601, 1358, 202),
            (297, "Cross the upper hall to the right wing", 448, 855, 311),
            (300, "Descend through the right wing", 948, 666, 339),
            (301, "Continue down the spiral stairs", 4, -125, -610),
            (302, "Continue down to the mansion basement", -14, -520, 2),
            (303, "Enter the basement library corridor", -232, -1104, 0),
            (304, "Find Sephiroth in the mansion library", 17, 88, 0)
        };
        foreach (var (fieldId, label, x, y, z) in firstMansionVisit)
        {
            AssertStoryTarget(memory, fieldId, 370, label, x, y, z);
        }

        AssertStoryTarget(
            memory,
            fieldId: 298,
            gameMoment: 370,
            "Return to the mansion entrance hall",
            expectedX: -335,
            expectedY: 205,
            expectedZ: 0);
        AssertStoryTarget(
            memory,
            fieldId: 299,
            gameMoment: 370,
            "Leave the upstairs room and return to the entrance hall",
            expectedX: -304,
            expectedY: 753,
            expectedZ: 277);

        AssertStoryTarget(
            memory,
            fieldId: 304,
            gameMoment: 371,
            "Leave Sephiroth to his research",
            expectedX: -435,
            expectedY: -98,
            expectedZ: 0);

        var secondMansionVisit = new[]
        {
            (299, "Leave the upstairs room and return to the basement", -304, 753, 277),
            (297, "Cross the upper hall to the right wing", 448, 855, 311),
            (300, "Descend through the right wing", 948, 666, 339),
            (301, "Continue down the spiral stairs", 4, -125, -610),
            (302, "Continue down to the mansion basement", -14, -520, 2),
            (303, "Enter the basement library corridor", -232, -1104, 0)
        };
        foreach (var (fieldId, label, x, y, z) in secondMansionVisit)
        {
            AssertStoryTarget(memory, fieldId, 373, label, x, y, z);
        }

        AssertStoryTarget(
            memory,
            fieldId: 304,
            gameMoment: 374,
            "Enter the mansion library",
            expectedX: 17,
            expectedY: 88,
            expectedZ: 0);
        AssertStoryTarget(
            memory,
            fieldId: 307,
            gameMoment: 374,
            "Confront Sephiroth in the far library room",
            expectedX: 224,
            expectedY: 3255,
            expectedZ: 0);

        var leaveMansion = new[]
        {
            (307, "Follow Sephiroth out of the library", 399, 10, 0),
            (304, "Continue out of the basement library", -454, -88, 0),
            (303, "Climb out of the mansion basement", 0, -290, 0),
            (302, "Climb the spiral stairs to the mansion", 12, 877, 226),
            (301, "Continue up through the right wing", 215, -136, 718),
            (300, "Return to the mansion entrance hall", 316, 746, 277),
            (297, "Leave the mansion and follow Sephiroth", 0, -18, 0)
        };
        foreach (var (fieldId, label, x, y, z) in leaveMansion)
        {
            AssertStoryTarget(memory, fieldId, 376, label, x, y, z);
        }

        AssertStoryTargetAtTriangle(
            memory,
            fieldId: 322,
            gameMoment: 380,
            playerTriangle: 43,
            "Climb down the ladder and follow Tifa",
            expectedX: -124,
            expectedY: 520,
            expectedZ: 1068,
            expectedTriggerLine: new FieldNavigationTriggerLine(
                -82,
                476,
                1068,
                -166,
                564,
                1068),
            expectedCompletesOnArrival: false);
        AssertStoryTargetAtTriangle(
            memory,
            fieldId: 322,
            gameMoment: 380,
            playerTriangle: 24,
            "Enter the reactor chamber after Tifa",
            expectedX: 0,
            expectedY: -257,
            expectedZ: 191,
            expectedTriggerLine: new FieldNavigationTriggerLine(
                38,
                -257,
                191,
                -38,
                -257,
                191));
        memory.ConfigureVisibleModel(entityId: 7, modelId: 1, x: 141, y: -429, z: 186);
        AssertStoryTarget(
            memory,
            fieldId: 323,
            gameMoment: 382,
            "Talk to Tifa beside the reactor pods",
            expectedX: 141,
            expectedY: -429,
            expectedZ: 186);
    }

    private static void AssertNibelheimFlashbackObjects()
    {
        var piano = FieldNavigationObjectCatalog.CreateAllFields().SingleOrDefault(definition =>
            definition.FieldId == 287 &&
            definition.Label == "Tifa's piano");

        AssertEqual(287, piano.FieldId, "Tifa piano field");
        AssertEqual(
            FieldNavigationObjectTargetKind.Location,
            piano.TargetKind,
            "Tifa piano target kind");
        AssertEqual(-237, piano.StaticX, "Tifa piano x");
        AssertEqual(-249, piano.StaticY, "Tifa piano y");
        AssertEqual(0, piano.StaticZ, "Tifa piano z");
        AssertEqual(344, piano.MinimumGameMoment, "Tifa piano minimum moment");
        AssertEqual(384, piano.MaximumGameMoment, "Tifa piano maximum moment");
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

    private static void AssertStoryTargetAtTriangle(
        NativeMemory memory,
        int fieldId,
        int gameMoment,
        ushort playerTriangle,
        string expectedLabel,
        int expectedX,
        int expectedY,
        int expectedZ,
        FieldNavigationTriggerLine? expectedTriggerLine = null,
        bool expectedCompletesOnArrival = true)
    {
        memory.SetGameMoment(gameMoment);
        var reader = new FieldStoryTargetReader(
            memory.ReadInt32,
            memory.ReadInt16,
            memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields());
        var targets = reader.ReadTargets(
            new FieldPositionSnapshot(
                1,
                fieldId,
                0,
                0,
                0,
                0,
                playerTriangle,
                0));
        var target = targets.Single();

        AssertEqual(expectedLabel, target.Label, $"story target in field {fieldId} at moment {gameMoment} on triangle {playerTriangle}");
        AssertEqual(expectedX, target.X, $"{expectedLabel} x");
        AssertEqual(expectedY, target.Y, $"{expectedLabel} y");
        AssertEqual(expectedZ, target.Z, $"{expectedLabel} z");
        AssertEqual(expectedTriggerLine, target.TriggerLine, $"{expectedLabel} native trigger line");
        AssertEqual(expectedCompletesOnArrival, target.CompletesOnArrival, $"{expectedLabel} arrival behavior");
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
