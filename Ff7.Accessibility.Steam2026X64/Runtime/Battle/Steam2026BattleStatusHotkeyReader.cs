using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

/// <summary>
/// Reads only the native party information exposed by the manual battle
/// status hotkeys through the validated translated x86 address space.
/// </summary>
internal sealed class Steam2026BattleStatusHotkeyReader
{
    private readonly ILegacyAddressSpace addressSpace;
    private readonly SavemapPartyReader partyReader;
    private readonly BattleStateReader battleReader;

    internal Steam2026BattleStatusHotkeyReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory)
        : this(ValidatedTranslatedX86AddressSpaceFactory.Create(
            fingerprint,
            moduleBase,
            memory))
    {
    }

    internal Steam2026BattleStatusHotkeyReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        partyReader = new SavemapPartyReader(addressSpace);
        battleReader = new BattleStateReader(addressSpace, partyReader);
    }

    internal bool IsBattleQueryActive()
    {
        return addressSpace.TryReadByte(
                   checked((uint)BattleStateReader.AddressCurrentModule),
                   out var module) &&
               module == BattleStateReader.BattleModule &&
               battleReader.TryReadVictorySignal(out var victory) &&
               !victory;
    }

    internal BattleStatusMemberSnapshot? ReadMember(int partySlot)
    {
        if (!battleReader.TryReadPartyActor(partySlot, out var actor) ||
            !partyReader.TryReadLimitGauge(partySlot, out var limitGauge))
        {
            return null;
        }

        return new BattleStatusMemberSnapshot(actor, limitGauge);
    }
}
