using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The character and current Limit level the main-menu Limit screen shows in its header.
/// </summary>
public readonly record struct LimitMenuHeaderSnapshot(
    int PartySlot,
    int CharacterId,
    int RecordIndex,
    int LimitLevel,
    int Gauge,
    string? Name = null)
{
    /// <summary>FUN_00703344 flashes the bar when its byte is 0xFF.</summary>
    public bool IsGaugeFull => Gauge == byte.MaxValue;

    /// <summary>The bar's filled share: FUN_00703344 draws (gauge * 0x3A) &gt;&gt; 8 of 0x3A pixels.</summary>
    public int GaugePercent => Gauge * 100 / byte.MaxValue;
}

/// <summary>
/// Reads the Limit screen's header the way the game draws it.
///
/// <para>FUN_0070212a (the Limit screen) and FUN_00703344 (its header) take the character
/// from <c>DAT_00dc0230[DAT_00dca3c8]</c>: the party-slot byte of the savemap party
/// (savemap 0x00DBFD38 + 0x4F8) indexed by the screen's own selected slot at 0x00DCA3C8.
/// The character record is <c>DAT_00919928[id]</c>, a table that is the identity for ids
/// 0..8 and maps 9 and 10 (the scripted Cloud and Sephiroth) to records 6 and 7. The header
/// prints <c>DAT_00dbfd9a[record * 0x84]</c> as the level number (FUN_006fcf5b at 0xD2,0x0C)
/// and draws the bar from <c>DAT_00dbfd9b[record * 0x84]</c> - record +0x0E and +0x0F of
/// the 0x84-byte records at 0x00DBFD8C, the offsets <see cref="SavemapPartyReader"/> uses.</para>
///
/// <para>Only meaningful while the Limit screen owns the menu: callers gate on the active
/// widget being one of the Limit screen's own widgets. Everything is read twice and must
/// agree, so a party or record changing mid-read yields nothing rather than a torn level.</para>
/// </summary>
public sealed class LimitMenuHeaderReader(ILegacyAddressSpace memory)
{
    public const uint AddressLimitScreenPartySlot = 0x00DCA3C8;

    private static readonly int[] CharacterRecordByPartyId = [0, 1, 2, 3, 4, 5, 6, 7, 8, 6, 7];

    public bool TryRead(out LimitMenuHeaderSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadOnce(out var first) || !TryReadOnce(out var second) || first != second)
        {
            return false;
        }

        snapshot = first;
        return true;
    }

    private bool TryReadOnce(out LimitMenuHeaderSnapshot snapshot)
    {
        snapshot = default;
        if (!memory.TryReadInt32(AddressLimitScreenPartySlot, out var partySlot) || partySlot is < 0 or > 2)
        {
            return false;
        }

        var savemap = (uint)SavemapPartyReader.AddressSavemap;
        if (!memory.TryReadByte(savemap + (uint)SavemapPartyReader.PartyMembersOffset + (uint)partySlot, out var characterId) ||
            characterId >= CharacterRecordByPartyId.Length)
        {
            return false;
        }

        var record = CharacterRecordByPartyId[characterId];
        var recordAddress = savemap + (uint)SavemapPartyReader.CharactersOffset + (uint)(record * SavemapPartyReader.CharacterSize);
        if (!memory.TryReadByte(recordAddress + (uint)SavemapPartyReader.LimitLevelOffset, out var level) ||
            level is < 1 or > 4 ||
            !memory.TryReadByte(recordAddress + (uint)SavemapPartyReader.LimitGaugeOffset, out var gauge))
        {
            return false;
        }

        // The name the header prints beside the portrait, through the verified party reader.
        var name = new SavemapPartyReader(memory).TryReadPartySlot(partySlot, out var member) &&
            member.CharacterId == characterId
                ? member.Name
                : null;
        snapshot = new LimitMenuHeaderSnapshot(partySlot, characterId, record, level, gauge, name);
        return true;
    }
}
