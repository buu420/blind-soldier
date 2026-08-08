using Ff7.Accessibility.Reloaded;

internal static class WallMarketStoryRoutingTests
{
    private const int LowerWallMarket = 195;
    private const int UpperWallMarket = 205;

    internal static void Run()
    {
        AssertTarget(
            LowerWallMarket,
            191,
            "Enter the boutique and ask the clothes-shop clerk for help");

        AssertTarget(
            LowerWallMarket,
            191,
            "Continue north to find the clothes-shop owner at the bar",
            bank1: new Dictionary<int, byte> { [162] = 0x80 });
        AssertTarget(
            UpperWallMarket,
            191,
            "Enter the bar and speak with the clothes-shop owner",
            bank1: new Dictionary<int, byte> { [162] = 0x80 });

        AssertTarget(
            UpperWallMarket,
            191,
            "Return to lower Wall Market for the finished dress",
            bank1: new Dictionary<int, byte> { [161] = 0x80, [162] = 0x80 });
        AssertTarget(
            LowerWallMarket,
            191,
            "Return to the boutique and collect the finished dress",
            bank1: new Dictionary<int, byte> { [161] = 0x80, [162] = 0x80 });

        AssertTarget(
            LowerWallMarket,
            191,
            "Continue north to the Men's Hall for a wig",
            bank1: new Dictionary<int, byte> { [161] = 0x88, [162] = 0x80 });
        AssertTarget(
            UpperWallMarket,
            191,
            "Enter the Men's Hall and complete the squat contest",
            bank1: new Dictionary<int, byte> { [161] = 0x88, [162] = 0x80 });

        // Exact current save state: dress and wig obtained, disguise not worn.
        AssertTarget(
            LowerWallMarket,
            191,
            "Return to the boutique fitting room and change clothes",
            bank1: new Dictionary<int, byte>
            {
                [160] = 0x84,
                [161] = 0x98,
                [162] = 0x90
            },
            bank3: new Dictionary<int, byte> { [162] = 0x01 });
        AssertTarget(
            UpperWallMarket,
            191,
            "Return to lower Wall Market and change clothes at the boutique",
            bank1: new Dictionary<int, byte>
            {
                [160] = 0x84,
                [161] = 0x98,
                [162] = 0x90
            },
            bank3: new Dictionary<int, byte> { [162] = 0x01 });

        AssertTarget(
            LowerWallMarket,
            192,
            "Continue north to Corneo Hall while disguised",
            bank3: new Dictionary<int, byte> { [162] = 0x03 });
        AssertTarget(
            UpperWallMarket,
            192,
            "Enter Corneo Hall while disguised",
            bank3: new Dictionary<int, byte> { [162] = 0x03 });
    }

    private static void AssertTarget(
        int fieldId,
        int gameMoment,
        string expectedLabel,
        IReadOnlyDictionary<int, byte>? bank1 = null,
        IReadOnlyDictionary<int, byte>? bank3 = null)
    {
        var memory = new Dictionary<int, byte>();
        WriteUInt16(
            memory,
            FieldNavigationObjectReader.AddressFieldBankBase,
            checked((ushort)gameMoment));
        WriteBank(memory, FieldNavigationObjectReader.AddressFieldBankBase, bank1);
        WriteBank(memory, FieldNavigationObjectReader.AddressFieldBankBase + 0x100, bank3);

        byte ReadByte(int address) =>
            memory.TryGetValue(address, out var value) ? value : (byte)0;
        int ReadInt32(int address) =>
            ReadByte(address)
            | (ReadByte(address + 1) << 8)
            | (ReadByte(address + 2) << 16)
            | (ReadByte(address + 3) << 24);

        var reader = new FieldStoryTargetReader(
            ReadInt32,
            ReadByte,
            FieldStoryEventCatalog.CreateAllFields());
        var targets = reader.ReadTargets(
            new FieldPositionSnapshot(
                FieldPositionReader.FieldModule,
                fieldId,
                0,
                0,
                0,
                0,
                0,
                0));

        AssertEqual(1, targets.Count, $"{expectedLabel} target count");
        AssertEqual(expectedLabel, targets[0].Label, $"field {fieldId} moment {gameMoment}");
    }

    private static void WriteBank(
        IDictionary<int, byte> memory,
        int bankAddress,
        IReadOnlyDictionary<int, byte>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var pair in values)
        {
            memory[bankAddress + pair.Key] = pair.Value;
        }
    }

    private static void WriteUInt16(IDictionary<int, byte> memory, int address, ushort value)
    {
        memory[address] = (byte)value;
        memory[address + 1] = (byte)(value >> 8);
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{name}: expected {expected}, actual {actual}.");
        }
    }
}
