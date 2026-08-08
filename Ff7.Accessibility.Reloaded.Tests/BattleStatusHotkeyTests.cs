using Ff7.Accessibility.Reloaded;

internal static class BattleStatusHotkeyTests
{
    internal static void Run()
    {
        AssertVirtualKeyMap();
        AssertSelectionAndUnavailableSlots();
        AssertVitalsAndStatuses();
        AssertLimitGaugeConversion();
        AssertNativeLimitGaugeReadUsesPartySlot();
        AssertPollingSamplesEveryKeyAndHonorsBattleOwnership();
    }

    private static void AssertVirtualKeyMap()
    {
        var expected = new Dictionary<int, BattleStatusHotkeyCommand>
        {
            ['1'] = BattleStatusHotkeyCommand.SelectParty1,
            ['2'] = BattleStatusHotkeyCommand.SelectParty2,
            ['3'] = BattleStatusHotkeyCommand.SelectParty3,
            ['H'] = BattleStatusHotkeyCommand.Hp,
            ['M'] = BattleStatusHotkeyCommand.Mp,
            ['D'] = BattleStatusHotkeyCommand.Debuffs,
            ['S'] = BattleStatusHotkeyCommand.Buffs,
            ['L'] = BattleStatusHotkeyCommand.Limit
        };

        foreach (var pair in expected)
        {
            AssertEqual(
                true,
                BattleStatusHotkeyController.TryMapVirtualKey(pair.Key, out var command),
                $"map key {(char)pair.Key}");
            AssertEqual(pair.Value, command, $"command for {(char)pair.Key}");
        }

        AssertEqual(
            false,
            BattleStatusHotkeyController.TryMapVirtualKey('Q', out _),
            "unmapped key");
    }

    private static void AssertSelectionAndUnavailableSlots()
    {
        var controller = new BattleStatusHotkeyController();
        var members = CreateMembers();
        BattleStatusMemberSnapshot? Read(int slot) =>
            members.TryGetValue(slot, out var member) ? member : null;

        AssertEqual("Cloud selected.", controller.HandleVirtualKey('1', Read), "select Cloud");
        AssertEqual("Tifa selected.", controller.HandleVirtualKey('2', Read), "select Tifa");
        AssertEqual(1, controller.SelectedPartySlot, "selected party slot");
        AssertEqual(
            "Party member 3 unavailable.",
            controller.HandleVirtualKey('3', Read),
            "empty third slot");
        AssertEqual(1, controller.SelectedPartySlot, "empty slot retains selection");
        AssertEqual("Tifa HP 512 of 640.", controller.HandleVirtualKey('H', Read), "retained member HP");
    }

    private static void AssertVitalsAndStatuses()
    {
        var controller = new BattleStatusHotkeyController();
        var members = CreateMembers();
        BattleStatusMemberSnapshot? Read(int slot) =>
            members.TryGetValue(slot, out var member) ? member : null;

        AssertEqual("Cloud HP 379 of 379.", controller.HandleVirtualKey('H', Read), "HP speech");
        AssertEqual("Cloud MP 74 of 74.", controller.HandleVirtualKey('M', Read), "MP speech");
        AssertEqual(
            "Cloud debuffs: Poison, Slow.",
            controller.HandleVirtualKey('D', Read),
            "debuff speech");
        AssertEqual(
            "Cloud buffs: Haste, Barrier.",
            controller.HandleVirtualKey('S', Read),
            "buff speech");

        controller.HandleVirtualKey('2', Read);
        AssertEqual("Tifa has no debuffs.", controller.HandleVirtualKey('D', Read), "no debuffs");
        AssertEqual("Tifa has no buffs.", controller.HandleVirtualKey('S', Read), "no buffs");
    }

    private static void AssertLimitGaugeConversion()
    {
        AssertEqual(0, BattleStatusHotkeyController.LimitGaugeToPercent(0), "empty limit");
        AssertEqual(50, BattleStatusHotkeyController.LimitGaugeToPercent(127), "half limit");
        AssertEqual(100, BattleStatusHotkeyController.LimitGaugeToPercent(255), "full limit");

        var controller = new BattleStatusHotkeyController();
        var members = CreateMembers();
        BattleStatusMemberSnapshot? Read(int slot) =>
            members.TryGetValue(slot, out var member) ? member : null;
        AssertEqual("Cloud limit 73 percent.", controller.HandleVirtualKey('L', Read), "limit speech");
    }

    private static void AssertNativeLimitGaugeReadUsesPartySlot()
    {
        var memory = new Dictionary<int, byte>();
        var partyAddress = SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset;
        var characterAddress = SavemapPartyReader.AddressSavemap
            + SavemapPartyReader.CharactersOffset
            + SavemapPartyReader.CharacterSize;
        memory[partyAddress] = 1;
        memory[characterAddress + SavemapPartyReader.LimitGaugeOffset] = 187;
        var reader = new SavemapPartyReader(address =>
            memory.TryGetValue(address, out var value) ? value : (byte)0xFF);

        AssertEqual(true, reader.TryReadLimitGauge(0, out var raw), "read native limit");
        AssertEqual((byte)187, raw, "native limit value");
        AssertEqual(false, reader.TryReadLimitGauge(1, out _), "empty party slot limit");
    }

    private static void AssertPollingSamplesEveryKeyAndHonorsBattleOwnership()
    {
        var controller = new BattleStatusHotkeyController();
        var sampled = new List<int>();
        var inactiveSpeech = controller.Poll(
            battleActive: false,
            virtualKey =>
            {
                sampled.Add(virtualKey);
                return true;
            },
            _ => throw new InvalidOperationException("Inactive polling must not read battle state."));
        AssertEqual<string?>(null, inactiveSpeech, "inactive battle hotkeys");
        AssertEqual(
            string.Join(",", BattleStatusHotkeyController.VirtualKeys),
            string.Join(",", sampled),
            "inactive polling samples every key to clear held state");

        var members = CreateMembers();
        var activeSpeech = controller.Poll(
            battleActive: true,
            virtualKey => virtualKey == '2',
            slot => members.TryGetValue(slot, out var member) ? member : null);
        AssertEqual("Tifa selected.", activeSpeech, "active battle polling");
    }

    private static Dictionary<int, BattleStatusMemberSnapshot> CreateMembers() => new()
    {
        [0] = new BattleStatusMemberSnapshot(
            new BattleActorSnapshot(
                0,
                "Cloud",
                false,
                379,
                379,
                74,
                74,
                true,
                (1u << 3) | (1u << 8) | (1u << 9) | (1u << 16)),
            186),
        [1] = new BattleStatusMemberSnapshot(
            new BattleActorSnapshot(1, "Tifa", false, 512, 640, 42, 61, true),
            0)
    };

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{name}: expected {expected}, actual {actual}.");
        }
    }
}
