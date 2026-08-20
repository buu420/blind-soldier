using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Names the character sitting in a PHS reserve-grid cell.
/// </summary>
/// <remarks>
/// The reserve grid is drawn as character portraits (FUN_006ed0ec), never as
/// text, so a sighted player identifies those members by their face while the
/// menu text hook observes nothing at all. The native roster array is the only
/// source for these names.
///
/// FUN_00700a9f fills that array with every recruited character who is not
/// already in the active party, and FUN_00700c90 addresses it as
/// <c>row * 3 + column</c>. Names come from <see cref="SavemapPartyReader"/>,
/// whose record layout the same Ghidra pass confirms.
/// </remarks>
public sealed class PhsRosterNameResolver
{
    /// <summary>Reserve cells, addressed as <c>row * 3 + column</c>.</summary>
    public const int RosterSlots = 9;

    /// <summary>Written by FUN_00700a9f for a cell with no member.</summary>
    public const int EmptySlot = 0xFF;

    /// <summary>Roster byte array; <c>0x00DCA148</c> on ff7_en.exe.</summary>
    public const int AddressPhsRoster = 0x00DCA148;

    private readonly ILegacyAddressSpace addressSpace;
    private readonly SavemapPartyReader partyReader;
    private readonly int rosterAddress;

    public PhsRosterNameResolver(ILegacyAddressSpace addressSpace)
        : this(addressSpace, new SavemapPartyReader(addressSpace), AddressPhsRoster)
    {
    }

    public PhsRosterNameResolver(
        ILegacyAddressSpace addressSpace,
        SavemapPartyReader partyReader,
        int rosterAddress)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.partyReader = partyReader ?? throw new ArgumentNullException(nameof(partyReader));
        this.rosterAddress = rosterAddress;
    }

    /// <summary>
    /// Returns the name shown in reserve cell <paramref name="gridIndex"/>, or
    /// null when the cell is empty or cannot be read.
    /// </summary>
    public string? TryResolve(int gridIndex)
    {
        if (gridIndex is < 0 or >= RosterSlots || rosterAddress <= 0)
        {
            return null;
        }

        Span<byte> slot = stackalloc byte[1];
        if (!addressSpace.TryRead((uint)(rosterAddress + gridIndex), slot) ||
            slot[0] == EmptySlot)
        {
            return null;
        }

        if (!partyReader.TryReadCharacter(slot[0], out var member))
        {
            return null;
        }

        var name = member.Name.Trim();
        return name.Length > 0 ? name : null;
    }
}
