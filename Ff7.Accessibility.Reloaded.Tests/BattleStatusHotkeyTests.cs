using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Core;

internal static class BattleStatusHotkeyTests
{
    internal static void Run()
    {
        AssertVirtualKeyMap();
        AssertSelectionAndUnavailableSlots();
        AssertVitalsAndStatuses();
        AssertLimitGaugeConversion();
        AssertNativeLimitGaugeReadUsesPartySlot();
        AssertLiveBattleLimitGaugeReadUsesPartySlotAndUpdates();
        AssertBattleQueryOwnershipUsesOneCoherentLifecycleSnapshot();
        AssertX86VictoryGateDoesNotDependOnAutomaticBattleHooks();
        AssertPollingSamplesEveryKeyAndHonorsBattleOwnership();
        AssertSharedLimitKeyOwnershipRoutesToActiveOwner();
        AssertHeldLimitDoesNotFireAfterSuppressionOrRefocus();
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

    private static void AssertLiveBattleLimitGaugeReadUsesPartySlotAndUpdates()
    {
        var memory = new Dictionary<int, byte>();
        memory[BattleStateReader.AddressCurrentModule] = BattleStateReader.BattleModule;
        memory[SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset] = 0;
        var characterBase = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        WriteFf7Name(memory, characterBase + SavemapPartyReader.CharacterNameOffset, "Cloud", 12);
        memory[characterBase + SavemapPartyReader.LimitGaugeOffset] = 17;

        var actorBase = BattleStateReader.AddressBattleActors;
        memory[actorBase + BattleStateReader.ActorInstanceIdOffset] = 0;
        WriteInt32(memory, actorBase + BattleStateReader.ActorStatusMaskOffset, 0);
        WriteUInt16(memory, actorBase + BattleStateReader.ActorCurrentMpOffset, 42);
        WriteUInt16(memory, actorBase + BattleStateReader.ActorMaxMpOffset, 54);
        WriteInt32(memory, actorBase + BattleStateReader.ActorCurrentHpOffset, 314);
        WriteInt32(memory, actorBase + BattleStateReader.ActorMaxHpOffset, 350);
        memory[BattleStateReader.AddressBattleLimitGauges] = 204;

        var addressSpace = new DictionaryLegacyAddressSpace(memory);
        var reader = new BattleStateReader(
            addressSpace,
            new SavemapPartyReader(addressSpace));
        AssertEqual(
            true,
            reader.TryReadPartyStatusMember(0, out var first),
            "coherent live battle status member");
        AssertEqual((byte)204, first.LimitGauge, "live gauge wins over stale savemap gauge");

        memory[BattleStateReader.AddressBattleLimitGauges] = 229;
        AssertEqual(
            true,
            reader.TryReadPartyStatusMember(0, out var updated),
            "updated live battle status member");
        AssertEqual((byte)229, updated.LimitGauge, "limit gauge updates during battle");

        memory[actorBase + BattleStateReader.ActorInstanceIdOffset] = 1;
        AssertEqual(
            false,
            reader.TryReadPartyStatusMember(0, out _),
            "stable actor and party character mismatch fails closed");
    }

    private static void AssertX86VictoryGateDoesNotDependOnAutomaticBattleHooks()
    {
        var config = new AccessibilityConfig
        {
            EnableSpeech = true,
            EnableBattleMenuSpeech = false,
            EnableBattleTargetSpeech = false,
            EnableBattleMessageSpeech = false,
            EnableBattleResultsSpeech = false,
            EnableBattleDamageSpeech = false,
            EnableBattleEncounterSpeech = false,
            EnableBattleEnemyActionSpeech = false,
            EnableBattleStatusSpeech = false
        };

        AssertEqual(
            true,
            Mod.ShouldOwnBattleStatusHotkeys(
                config,
                battleQueryReadable: true,
                battleQueryActive: true),
            "manual status hotkeys remain available with automatic battle hooks disabled");
        AssertEqual(
            false,
            Mod.ShouldOwnBattleStatusHotkeys(
                config,
                battleQueryReadable: false,
                battleQueryActive: false),
            "unreadable victory state fails closed without an update hook");
        AssertEqual(
            false,
            Mod.ShouldOwnBattleStatusHotkeys(
                config,
                battleQueryReadable: true,
                battleQueryActive: false),
            "victory suppresses manual status hotkeys without an update hook");
    }

    private static void AssertBattleQueryOwnershipUsesOneCoherentLifecycleSnapshot()
    {
        var moduleReads = 0;
        var reader = new BattleStateReader(
            address => address == BattleStateReader.AddressCurrentModule &&
                       Interlocked.Increment(ref moduleReads) == 1
                ? (byte)BattleStateReader.BattleModule
                : (byte)FieldPositionReader.FieldModule,
            _ => (ushort)0,
            _ => 0,
            new SavemapPartyReader(_ => 0),
            (_, _) => true);

        AssertEqual(
            false,
            reader.TryReadBattleQueryActive(out _),
            "module transition cannot grant battle status ownership");
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

    private static void AssertSharedLimitKeyOwnershipRoutesToActiveOwner()
    {
        var limitRouter = new BattleStatusLimitKeyFrameRouter();
        var controller = new BattleStatusHotkeyController();
        var members = CreateMembers();
        var isLimitDown = true;
        BattleStatusMemberSnapshot? Read(int slot) =>
            members.TryGetValue(slot, out var member) ? member : null;

        var config = new AccessibilityConfig
        {
            EnableSpeech = true,
            EnableFieldNavigationAssistant = true,
            EnableWorldMapNavigationAssistant = true
        };
        var navigationOwnsLimit = Mod.NavigationOwnsBattleStatusLimitKey(
            config,
            FieldPositionReader.FieldModule);
        var battleLimitPressed = limitRouter.BeginFrame(
            isLimitDown,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimit);
        var fieldSpeech = controller.Poll(
            battleActive: false,
            virtualKey => virtualKey == 'L' && battleLimitPressed,
            Read,
            observeLimitKey: true);
        var fieldNextTarget = limitRouter.TakeNavigationPress(
            FieldPositionReader.FieldModule);
        AssertEqual<string?>(null, fieldSpeech, "field-owned L has no battle speech");
        AssertEqual(true, fieldNextTarget, "field navigation receives its L rising edge");

        isLimitDown = false;
        _ = limitRouter.BeginFrame(
            isLimitDown,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimit);

        isLimitDown = true;
        navigationOwnsLimit = Mod.NavigationOwnsBattleStatusLimitKey(
            config,
            BattleStateReader.BattleModule);
        battleLimitPressed = limitRouter.BeginFrame(
            isLimitDown,
            isForeground: true,
            currentModule: BattleStateReader.BattleModule,
            navigationOwnsLimit);
        var battleSpeech = controller.Poll(
            battleActive: true,
            virtualKey => virtualKey == 'L' && battleLimitPressed,
            Read,
            observeLimitKey: true);
        var inactiveFieldNextTarget = limitRouter.TakeNavigationPress(
            FieldPositionReader.FieldModule);
        AssertEqual("Cloud limit 73 percent.", battleSpeech, "battle-owned L speaks limit");
        AssertEqual(
            false,
            inactiveFieldNextTarget,
            "inactive field navigation cannot consume the battle-owned L edge");
    }

    private static void AssertHeldLimitDoesNotFireAfterSuppressionOrRefocus()
    {
        var router = new BattleStatusLimitKeyFrameRouter();

        _ = router.BeginFrame(
            isLimitDown: true,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        // The field scan is not due on this monitor frame. The valid edge must
        // survive the next held sample until the slower navigation scan runs.
        _ = router.BeginFrame(
            isLimitDown: true,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        AssertEqual(
            true,
            router.TakeNavigationPress(FieldPositionReader.FieldModule),
            "field L survives a skipped navigation scan");

        _ = router.BeginFrame(
            isLimitDown: false,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        _ = router.BeginFrame(
            isLimitDown: true,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        router.DiscardNavigationPress(FieldPositionReader.FieldModule);
        _ = router.BeginFrame(
            isLimitDown: true,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        AssertEqual(
            false,
            router.TakeNavigationPress(FieldPositionReader.FieldModule),
            "suppressed field L is discarded while held");

        _ = router.BeginFrame(
            isLimitDown: false,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        _ = router.BeginFrame(
            isLimitDown: true,
            isForeground: false,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        AssertEqual(
            false,
            router.TakeNavigationPress(FieldPositionReader.FieldModule),
            "background L press is not dispatched");

        _ = router.BeginFrame(
            isLimitDown: true,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        AssertEqual(
            false,
            router.TakeNavigationPress(FieldPositionReader.FieldModule),
            "held background L does not fire after refocus");

        _ = router.BeginFrame(
            isLimitDown: false,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        _ = router.BeginFrame(
            isLimitDown: true,
            isForeground: true,
            currentModule: FieldPositionReader.FieldModule,
            navigationOwnsLimitKey: true);
        _ = router.BeginFrame(
            isLimitDown: true,
            isForeground: true,
            currentModule: WorldMapStateReader.WorldModule,
            navigationOwnsLimitKey: true);
        AssertEqual(
            false,
            router.TakeNavigationPress(FieldPositionReader.FieldModule),
            "module transition discards a stale field L press");
        AssertEqual(
            false,
            router.TakeNavigationPress(WorldMapStateReader.WorldModule),
            "held L does not become a synthetic world-map press");
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

    private static void WriteUInt16(Dictionary<int, byte> memory, int address, int value)
    {
        memory[address] = (byte)value;
        memory[address + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(Dictionary<int, byte> memory, int address, int value)
    {
        for (var index = 0; index < sizeof(int); index++)
        {
            memory[address + index] = (byte)(value >> (index * 8));
        }
    }

    private static void WriteFf7Name(
        Dictionary<int, byte> memory,
        int address,
        string text,
        int length)
    {
        for (var index = 0; index < length; index++)
        {
            memory[address + index] = 0xFF;
        }

        for (var index = 0; index < Math.Min(text.Length, length - 1); index++)
        {
            memory[address + index] = text[index] == ' '
                ? (byte)0
                : (byte)(text[index] - 0x20);
        }
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
