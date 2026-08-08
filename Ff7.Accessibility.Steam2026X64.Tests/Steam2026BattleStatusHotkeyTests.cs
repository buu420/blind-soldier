using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Battle;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

internal static class Steam2026BattleStatusHotkeyTests
{
    internal static void Run()
    {
        BattleStatusLimitKeyWinsSharedInputOwnership();
        HeldStatusKeyDoesNotFireAfterNoFrameRecovery();
        var fixture = BattleObservationFixture.CreatePopulated();
        fixture.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 0);
        var cloud = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        fixture.WriteByte(cloud + SavemapPartyReader.LimitGaugeOffset, 17);
        fixture.WriteByte(BattleStateReader.AddressBattleLimitGauges, 204);
        var reader = new Steam2026BattleStatusHotkeyReader(fixture.Direct);

        Equal(true, reader.IsBattleQueryActive(), "battle ownership");
        var member = reader.ReadMember(0);
        Equal(true, member.HasValue, "translated party member read");
        Equal("Cloud", member?.Actor.Name, "translated party member name");
        Equal(314, member?.Actor.CurrentHp, "translated party member HP");
        Equal(42, member?.Actor.CurrentMp, "translated party member MP");
        Equal((byte)204, member?.LimitGauge, "translated native limit gauge");
        Equal<BattleStatusMemberSnapshot?>(null, reader.ReadMember(1), "empty translated party slot");

        fixture.WriteByte(BattleStateReader.AddressBattleLimitGauges, 229);
        Equal(
            (byte)229,
            reader.ReadMember(0)?.LimitGauge,
            "translated live limit gauge updates during battle");

        fixture.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 1);
        Equal(false, reader.IsBattleQueryActive(), "victory suppresses hotkeys");
        fixture.SwitchToResultsModule();
        Equal(false, reader.IsBattleQueryActive(), "results suppress hotkeys");
    }

    private static void BattleStatusLimitKeyWinsSharedInputOwnership()
    {
        const uint processId = 42;
        var limitDown = true;
        var input = new Steam2026ForegroundInputAdapter(
            () => (nint)1,
            _ => processId,
            key => key == 'L' && limitDown ? unchecked((short)0x8000) : (short)0,
            processId);
        var controller = new BattleStatusHotkeyController();
        var cloud = new BattleStatusMemberSnapshot(
            new BattleActorSnapshot(0, "Cloud", false, 314, 379, 42, 74, true),
            204);

        var speech = Steam2026FrameInputOwnership.PollBattleStatusBeforeNavigation(
            controller,
            ownsBattleStatusHotkeys: true,
            currentModule: BattleStateReader.BattleModule,
            navigationObserverAvailable: false,
            input,
            slot => slot == 0 ? cloud : null);
        var inactiveFieldActions = Steam2026FieldNavigationKeyRouter.ReadActions(
            input.ObserveRisingEdge,
            observeLimitKey: false);

        Equal("Cloud limit 80 percent.", speech, "battle limit key speech");
        Equal(
            false,
            inactiveFieldActions.Contains(FieldNavigationAction.NextTarget),
            "inactive field navigation cannot consume the battle-owned L edge");

        limitDown = false;
        _ = Steam2026FrameInputOwnership.PollBattleStatusBeforeNavigation(
            controller,
            ownsBattleStatusHotkeys: true,
            currentModule: BattleStateReader.BattleModule,
            navigationObserverAvailable: false,
            input,
            slot => slot == 0 ? cloud : null);
        _ = Steam2026FieldNavigationKeyRouter.ReadActions(
            input.ObserveRisingEdge,
            observeLimitKey: false);

        limitDown = true;
        var fieldSpeech = Steam2026FrameInputOwnership.PollBattleStatusBeforeNavigation(
            controller,
            ownsBattleStatusHotkeys: false,
            currentModule: FieldPositionReader.FieldModule,
            navigationObserverAvailable: true,
            input,
            slot => slot == 0 ? cloud : null);
        var fieldActions = Steam2026FieldNavigationKeyRouter.ReadActions(
            input.ObserveRisingEdge,
            observeLimitKey: true);

        Equal<string?>(null, fieldSpeech, "field-owned L has no battle speech");
        Equal(
            true,
            fieldActions.Contains(FieldNavigationAction.NextTarget),
            "field navigation receives its L rising edge");
    }

    private static void HeldStatusKeyDoesNotFireAfterNoFrameRecovery()
    {
        const uint processId = 43;
        var activeKey = '2';
        var keyDown = true;
        var input = new Steam2026ForegroundInputAdapter(
            () => (nint)1,
            _ => processId,
            key => key == activeKey && keyDown ? unchecked((short)0x8000) : (short)0,
            processId);
        var controller = new BattleStatusHotkeyController();
        var cloud = new BattleStatusMemberSnapshot(
            new BattleActorSnapshot(0, "Cloud", false, 314, 379, 42, 74, true),
            204);
        var tifa = new BattleStatusMemberSnapshot(
            new BattleActorSnapshot(1, "Tifa", false, 512, 640, 31, 45, true),
            127);
        BattleStatusMemberSnapshot? Read(int slot) => slot switch
        {
            0 => cloud,
            1 => tifa,
            _ => null
        };

        var selectionSpeech =
            Steam2026FrameInputOwnership.PollBattleStatusBeforeNavigation(
                controller,
                ownsBattleStatusHotkeys: true,
                currentModule: BattleStateReader.BattleModule,
                navigationObserverAvailable: false,
                input,
                Read);
        Equal("Tifa selected.", selectionSpeech, "select Tifa before frame gap");
        keyDown = false;
        _ = Steam2026FrameInputOwnership.PollBattleStatusBeforeNavigation(
            controller,
            ownsBattleStatusHotkeys: true,
            currentModule: BattleStateReader.BattleModule,
            navigationObserverAvailable: false,
            input,
            Read);

        activeKey = 'L';
        keyDown = true;
        Steam2026FrameInputOwnership.SynchronizeBattleStatusWithoutFrame(
            controller,
            input);
        var recoveredSpeech =
            Steam2026FrameInputOwnership.PollBattleStatusBeforeNavigation(
                controller,
                ownsBattleStatusHotkeys: true,
                currentModule: BattleStateReader.BattleModule,
                navigationObserverAvailable: false,
                input,
                Read);
        Equal<string?>(
            null,
            recoveredSpeech,
            "held no-frame L does not become a delayed battle query");

        keyDown = false;
        _ = Steam2026FrameInputOwnership.PollBattleStatusBeforeNavigation(
            controller,
            ownsBattleStatusHotkeys: true,
            currentModule: BattleStateReader.BattleModule,
            navigationObserverAvailable: false,
            input,
            Read);
        activeKey = 'H';
        keyDown = true;
        var freshSpeech = Steam2026FrameInputOwnership.PollBattleStatusBeforeNavigation(
            controller,
            ownsBattleStatusHotkeys: true,
            currentModule: BattleStateReader.BattleModule,
            navigationObserverAvailable: false,
            input,
            Read);
        Equal(
            "Tifa HP 512 of 640.",
            freshSpeech,
            "no-frame synchronization preserves selected party member");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, actual {actual}.");
        }
    }
}
