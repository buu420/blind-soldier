using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the inline Order pane from the native main-menu state. FFVII keeps
/// this pane inside the main screen rather than assigning it a separate menu
/// module, so the active widget, selection latch, and savemap row bit form the
/// ownership contract.
/// </summary>
public sealed class OrderMenuSelectionReader
{
    public const uint OrderPartyWidget = 0x00DC11C0;
    public const uint AlternateOrderPartyWidget = 0x00DC6C48;
    public const int AddressSelectedPartySlot = 0x00DC110C;
    public const int AddressSelectionLatch = 0x00DC1320;

    private readonly ILegacyAddressSpace addressSpace;
    private readonly SavemapPartyReader partyReader;

    public OrderMenuSelectionReader(
        ILegacyAddressSpace addressSpace,
        SavemapPartyReader partyReader)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.partyReader = partyReader ?? throw new ArgumentNullException(nameof(partyReader));
    }

    public bool TryRead(uint widgetAddress, int cursor, out NativeMenuSelection selection)
    {
        selection = default;
        if (widgetAddress is not (OrderPartyWidget or AlternateOrderPartyWidget) ||
            cursor is < 0 or >= 3 ||
            !addressSpace.TryReadInt32((uint)AddressSelectionLatch, out var latch) ||
            latch is not (0 or 1) ||
            !partyReader.TryReadPartySlotWithRow(cursor, out var current))
        {
            return false;
        }

        var selectedSlot = -1;
        PartyMemberRowSnapshot selectedMember = default;
        if (latch == 1)
        {
            if (!addressSpace.TryReadByte((uint)AddressSelectedPartySlot, out var selectedSlotByte) ||
                selectedSlotByte >= 3 ||
                !partyReader.TryReadPartySlotWithRow(selectedSlotByte, out selectedMember))
            {
                return false;
            }

            selectedSlot = selectedSlotByte;
        }

        if (!partyReader.TryReadPartySlotWithRow(cursor, out var currentBookend) ||
            currentBookend != current ||
            !addressSpace.TryReadInt32((uint)AddressSelectionLatch, out var latchBookend) ||
            latchBookend != latch)
        {
            return false;
        }

        if (latch == 1 &&
            (!addressSpace.TryReadByte((uint)AddressSelectedPartySlot, out var selectedSlotBookend) ||
             selectedSlotBookend != selectedSlot ||
             !partyReader.TryReadPartySlotWithRow(selectedSlot, out var selectedMemberBookend) ||
             selectedMemberBookend != selectedMember))
        {
            return false;
        }

        var row = current.Row == PartyBattleRow.Front ? "front" : "back";
        var text = $"{current.Name}, {row} row";
        if (latch == 1 && selectedSlot == cursor)
        {
            text += $". Selected. Select {current.Name} again to change rows, or choose another member to swap";
        }
        else if (latch == 1)
        {
            text += $". {selectedMember.Name} selected. Select {current.Name} to swap";
        }

        selection = new NativeMenuSelection(
            text,
            null,
            $"order:{widgetAddress:X8}:{cursor}:{current.CharacterId}:{current.RawFlags}:{latch}:{selectedSlot}:{selectedMember.CharacterId}");
        return true;
    }
}
