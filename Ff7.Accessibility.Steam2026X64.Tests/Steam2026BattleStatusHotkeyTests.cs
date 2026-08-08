using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Battle;

internal static class Steam2026BattleStatusHotkeyTests
{
    internal static void Run()
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        fixture.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 0);
        var cloud = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        fixture.WriteByte(cloud + SavemapPartyReader.LimitGaugeOffset, 204);
        var reader = new Steam2026BattleStatusHotkeyReader(fixture.Direct);

        Equal(true, reader.IsBattleQueryActive(), "battle ownership");
        var member = reader.ReadMember(0);
        Equal(true, member.HasValue, "translated party member read");
        Equal("Cloud", member?.Actor.Name, "translated party member name");
        Equal(314, member?.Actor.CurrentHp, "translated party member HP");
        Equal(42, member?.Actor.CurrentMp, "translated party member MP");
        Equal((byte)204, member?.LimitGauge, "translated native limit gauge");
        Equal<BattleStatusMemberSnapshot?>(null, reader.ReadMember(1), "empty translated party slot");

        fixture.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 1);
        Equal(false, reader.IsBattleQueryActive(), "victory suppresses hotkeys");
        fixture.SwitchToResultsModule();
        Equal(false, reader.IsBattleQueryActive(), "results suppress hotkeys");
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
