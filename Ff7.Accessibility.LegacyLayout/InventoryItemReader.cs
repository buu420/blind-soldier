using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class InventoryItemReader
{
    public const int AddressSavemap = 0x00DBFD38;
    public const int ItemsOffset = 0x4FC;
    public const int SlotCount = 320;

    private readonly Func<int, ushort>? readUInt16;
    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace? addressSpace;
    private readonly Func<int, string?>? resolveItemName;
    private readonly Func<int, string?>? resolveItemDescription;
    private readonly int savemapAddress;
    private readonly int itemsOffset;
    private readonly uint checkedSavemapAddress;
    private readonly uint checkedItemsOffset;

    public InventoryItemReader(
        Func<int, ushort> readUInt16,
        Func<int, string?>? resolveItemName = null,
        Func<int, string?>? resolveItemDescription = null,
        int savemapAddress = AddressSavemap,
        int itemsOffset = ItemsOffset)
    {
        this.readUInt16 = readUInt16;
        this.resolveItemName = resolveItemName;
        this.resolveItemDescription = resolveItemDescription;
        this.savemapAddress = savemapAddress;
        this.itemsOffset = itemsOffset;
    }

    public InventoryItemReader(
        Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace,
        Func<int, string?>? resolveItemName = null,
        Func<int, string?>? resolveItemDescription = null,
        uint savemapAddress = AddressSavemap,
        uint itemsOffset = ItemsOffset)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.resolveItemName = resolveItemName;
        this.resolveItemDescription = resolveItemDescription;
        checkedSavemapAddress = savemapAddress;
        checkedItemsOffset = itemsOffset;
    }

    public bool TryRead(int slot, out InventoryItemSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadSlot(slot, out var slotSnapshot) || slotSnapshot.IsEmpty)
        {
            return false;
        }

        snapshot = slotSnapshot.Item;
        return true;
    }

    public bool TryReadSlot(int slot, out InventoryMenuSlotSnapshot snapshot)
    {
        snapshot = default;
        if (slot is < 0 or >= SlotCount)
        {
            return false;
        }

        if (addressSpace is not null)
        {
            return TryReadSlotChecked(slot, out snapshot);
        }

        var address = savemapAddress + itemsOffset + (slot * sizeof(ushort));
        var raw = readUInt16!(address);
        if (raw == 0xFFFF)
        {
            if (readUInt16(address) != raw)
            {
                return false;
            }

            snapshot = new InventoryMenuSlotSnapshot(slot, true, default);
            return true;
        }

        var itemId = raw & 0x1FF;
        var quantity = raw >> 9;
        if (quantity <= 0)
        {
            return false;
        }

        var item = new InventoryItemSnapshot(
            slot,
            itemId,
            quantity,
            raw,
            resolveItemName?.Invoke(itemId),
            resolveItemDescription?.Invoke(itemId));
        if (readUInt16(address) != raw)
        {
            return false;
        }

        snapshot = new InventoryMenuSlotSnapshot(slot, false, item);
        return true;
    }

    private bool TryReadSlotChecked(int slot, out InventoryMenuSlotSnapshot snapshot)
    {
        snapshot = default;
        var candidateAddress =
            (ulong)checkedSavemapAddress + checkedItemsOffset + ((ulong)(uint)slot * sizeof(ushort));
        if (candidateAddress == 0 || candidateAddress > uint.MaxValue - (sizeof(ushort) - 1u))
        {
            return false;
        }

        var address = (uint)candidateAddress;
        var checkedAddressSpace = addressSpace!;
        if (!checkedAddressSpace.TryReadUInt16(address, out var raw))
        {
            return false;
        }

        if (raw == 0xFFFF)
        {
            if (!checkedAddressSpace.TryReadUInt16(address, out var emptyBookend) ||
                emptyBookend != raw)
            {
                return false;
            }

            snapshot = new InventoryMenuSlotSnapshot(slot, true, default);
            return true;
        }

        var itemId = raw & 0x1FF;
        var quantity = raw >> 9;
        if (quantity <= 0)
        {
            return false;
        }

        var name = resolveItemName?.Invoke(itemId);
        var description = resolveItemDescription?.Invoke(itemId);
        if (!checkedAddressSpace.TryReadUInt16(address, out var rawBookend) || rawBookend != raw)
        {
            return false;
        }

        var item = new InventoryItemSnapshot(
            slot,
            itemId,
            quantity,
            raw,
            name,
            description);
        snapshot = new InventoryMenuSlotSnapshot(slot, false, item);
        return true;
    }
}

public readonly record struct InventoryMenuSlotSnapshot(
    int Slot,
    bool IsEmpty,
    InventoryItemSnapshot Item);

public readonly record struct InventoryItemSnapshot(
    int Slot,
    int ItemId,
    int Quantity,
    ushort Raw,
    string? Name,
    string? Description = null);
