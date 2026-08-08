using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

/// <summary>
/// Reads only the native party information exposed by the manual battle
/// status hotkeys through the validated translated x86 address space.
/// </summary>
internal sealed class Steam2026BattleStatusHotkeyReader
{
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
        ArgumentNullException.ThrowIfNull(addressSpace);
        var partyReader = new SavemapPartyReader(addressSpace);
        battleReader = new BattleStateReader(addressSpace, partyReader);
    }

    internal bool IsBattleQueryActive()
    {
        return TryReadBattleQueryActive(out var isActive) && isActive;
    }

    internal bool TryReadBattleQueryActive(out bool isActive) =>
        battleReader.TryReadBattleQueryActive(out isActive);

    internal BattleStatusMemberSnapshot? ReadMember(int partySlot)
    {
        return battleReader.TryReadPartyStatusMember(partySlot, out var member)
            ? member
            : null;
    }
}
