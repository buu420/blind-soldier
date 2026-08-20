namespace Ff7.Accessibility.Reloaded;

public sealed class MagicMenuSelectionReader
{
    public const int AddressSelectedPartySlot = 0x00DD17E8;
    public const int AddressCurrentMp = 0x00DBA4AC;
    public const int AddressMagicRecords = 0x00DBA5A0;
    public const int AddressSummonRecords = 0x00DBA760;
    public const int AddressEnemySkillRecords = 0x00DBA7E0;
    public const int CharacterBlockSize = 0x440;
    public const int RecordSize = 8;
    public const int MpCostOffset = 1;

    private const int PartySlotCount = 3;
    private const int MagicEntryCount = 54;
    private const int SummonEntryCount = 16;
    private const int EnemySkillEntryCount = 24;
    private const int MagicActionIdBase = 0x00;
    private const int SummonActionIdBase = 0x38;
    private const int EnemySkillActionIdBase = 0x48;
    private const int WidgetFirstOffset = 0x00;
    private const int WidgetCursorOffset = 0x04;
    private const int WidgetColumnsOffset = 0x08;
    private const int WidgetRowsOffset = 0x0C;
    private const int WidgetScrollOffset = 0x14;
    private const int WidgetScrollDeltaOffset = 0x24;
    private const int WidgetScrollStateOffset = 0x30;

    private readonly Func<int, byte>? readByte;
    private readonly Func<int, ushort>? readUInt16;
    private readonly Func<int, int>? readInt32;
    private readonly Func<int, int, bool>? isReadableMemory;
    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace? addressSpace;
    private readonly Func<int, string?> resolveName;
    private readonly Func<int, string?> resolveDescription;

    public MagicMenuSelectionReader(
        Func<int, byte> readByte,
        Func<int, ushort> readUInt16,
        Func<int, int> readInt32,
        Func<int, int, bool> isReadableMemory,
        Func<int, string?> resolveName,
        Func<int, string?> resolveDescription)
    {
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
        this.readUInt16 = readUInt16 ?? throw new ArgumentNullException(nameof(readUInt16));
        this.readInt32 = readInt32 ?? throw new ArgumentNullException(nameof(readInt32));
        this.isReadableMemory = isReadableMemory ?? throw new ArgumentNullException(nameof(isReadableMemory));
        this.resolveName = resolveName ?? throw new ArgumentNullException(nameof(resolveName));
        this.resolveDescription = resolveDescription ?? throw new ArgumentNullException(nameof(resolveDescription));
    }

    public MagicMenuSelectionReader(
        Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace,
        Func<int, string?> resolveName,
        Func<int, string?> resolveDescription)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.resolveName = resolveName ?? throw new ArgumentNullException(nameof(resolveName));
        this.resolveDescription = resolveDescription ?? throw new ArgumentNullException(nameof(resolveDescription));
    }

    public bool TryRead(ActiveMenuWidgetSnapshot widget, out MagicMenuSpellSnapshot snapshot)
    {
        snapshot = default;
        if (widget.Kind != MenuWidgetKind.MagicList ||
            !TryReadSlot(widget, out var slot) ||
            slot.IsEmpty)
        {
            return false;
        }

        snapshot = slot.Spell;
        return true;
    }

    public bool TryReadSlot(ActiveMenuWidgetSnapshot widget, out MagicMenuSlotSnapshot snapshot)
    {
        snapshot = default;
        if (!TryResolveLayout(widget.Kind, out var layout) ||
            !MenuWidgetCatalog.TryResolve(widget.Address, out var descriptor) ||
            descriptor.Kind != widget.Kind ||
            !TryReadState(widget, layout, out var state))
        {
            return false;
        }

        if (state.AbilityId == 0xFF)
        {
            if (!TryReadState(widget, layout, out var emptyBookend) || emptyBookend != state)
            {
                return false;
            }

            snapshot = new MagicMenuSlotSnapshot(state.SelectedIndex, true, default);
            return true;
        }

        var actionId = state.AbilityId + layout.ActionIdBase;
        if (actionId >= 0xE0)
        {
            return false;
        }

        var name = resolveName(actionId);
        if (string.IsNullOrWhiteSpace(name) ||
            !TryReadState(widget, layout, out var bookend) ||
            bookend != state)
        {
            return false;
        }

        snapshot = new MagicMenuSlotSnapshot(
            state.SelectedIndex,
            false,
            new MagicMenuSpellSnapshot(
            actionId,
            state.RequiredMp,
            name,
            resolveDescription(actionId)));
        return true;
    }

    private bool TryReadState(
        ActiveMenuWidgetSnapshot expectedWidget,
        AbilityMenuLayout layout,
        out MagicSelectionReadState state)
    {
        state = default;
        if (!TryReadWidget(expectedWidget.Address, out var widget) ||
            widget != MagicWidgetReadState.From(expectedWidget) ||
            widget.Columns is <= 0 or > 16 ||
            widget.Rows is <= 0 or > 400 ||
            widget.First < 0 || widget.First >= widget.Columns ||
            widget.Cursor < 0 || widget.Cursor >= widget.Rows ||
            widget.ScrollOffset < 0 ||
            !TryReadByte((uint)AddressSelectedPartySlot, out var partySlot) ||
            partySlot >= PartySlotCount)
        {
            return false;
        }

        var selectedIndex = (long)widget.First +
            ((long)widget.Cursor * widget.Columns) +
            ((long)widget.ScrollOffset * widget.Columns);
        if (selectedIndex is < 0 || selectedIndex >= layout.EntryCount ||
            !TryComputeAddress(
                (uint)AddressCurrentMp,
                partySlot,
                CharacterBlockSize,
                0,
                0,
                out var currentMpAddress) ||
            !TryComputeAddress(
                layout.RecordsAddress,
                partySlot,
                CharacterBlockSize,
                selectedIndex,
                RecordSize,
                out var recordAddress) ||
            !TryReadUInt16(currentMpAddress, out var currentMp) ||
            !TryReadRecord(recordAddress, out var spellId, out var requiredMp))
        {
            return false;
        }

        // Current MP is a coherence token for the selected party member, not an affordability filter.
        state = new MagicSelectionReadState(
            widget,
            partySlot,
            currentMp,
            (int)selectedIndex,
            spellId,
            requiredMp);
        return true;
    }

    private static bool TryResolveLayout(MenuWidgetKind kind, out AbilityMenuLayout layout)
    {
        layout = kind switch
        {
            MenuWidgetKind.MagicList =>
                new AbilityMenuLayout(AddressMagicRecords, MagicEntryCount, MagicActionIdBase),
            MenuWidgetKind.SummonList =>
                new AbilityMenuLayout(AddressSummonRecords, SummonEntryCount, SummonActionIdBase),
            MenuWidgetKind.EnemySkillList =>
                new AbilityMenuLayout(AddressEnemySkillRecords, EnemySkillEntryCount, EnemySkillActionIdBase),
            _ => default
        };
        return layout.RecordsAddress != 0;
    }

    private bool TryReadWidget(uint address, out MagicWidgetReadState widget)
    {
        widget = default;
        if (!TryReadInt32(address, WidgetFirstOffset, out var first) ||
            !TryReadInt32(address, WidgetCursorOffset, out var cursor) ||
            !TryReadInt32(address, WidgetColumnsOffset, out var columns) ||
            !TryReadInt32(address, WidgetRowsOffset, out var rows) ||
            !TryReadInt32(address, WidgetScrollOffset, out var scrollOffset) ||
            !TryReadInt32(address, WidgetScrollDeltaOffset, out var scrollDelta) ||
            !TryReadInt32(address, WidgetScrollStateOffset, out var scrollState))
        {
            return false;
        }

        widget = new MagicWidgetReadState(
            first,
            cursor,
            columns,
            rows,
            scrollOffset,
            scrollDelta,
            scrollState);
        return true;
    }

    private bool TryReadInt32(uint baseAddress, int offset, out int value)
    {
        value = default;
        var candidate = (ulong)baseAddress + (uint)offset;
        if (baseAddress == 0 || candidate > uint.MaxValue)
        {
            return false;
        }

        var address = (uint)candidate;
        if (addressSpace is not null)
        {
            return Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadInt32(
                addressSpace,
                address,
                out value);
        }

        if (!TryGetDirectAddress(address, sizeof(int), out var directAddress))
        {
            return false;
        }

        value = readInt32!(directAddress);
        return true;
    }

    private bool TryReadByte(uint address, out byte value)
    {
        value = default;
        if (addressSpace is not null)
        {
            return Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadByte(
                addressSpace,
                address,
                out value);
        }

        if (!TryGetDirectAddress(address, sizeof(byte), out var directAddress))
        {
            return false;
        }

        value = readByte!(directAddress);
        return true;
    }

    private bool TryReadUInt16(uint address, out ushort value)
    {
        value = default;
        if (addressSpace is not null)
        {
            return Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadUInt16(
                addressSpace,
                address,
                out value);
        }

        if (!TryGetDirectAddress(address, sizeof(ushort), out var directAddress))
        {
            return false;
        }

        value = readUInt16!(directAddress);
        return true;
    }

    private bool TryReadRecord(uint address, out byte spellId, out byte requiredMp)
    {
        spellId = default;
        requiredMp = default;
        if (addressSpace is not null)
        {
            Span<byte> record = stackalloc byte[MpCostOffset + 1];
            if (!addressSpace.TryRead(address, record))
            {
                return false;
            }

            spellId = record[0];
            requiredMp = record[MpCostOffset];
            return true;
        }

        if (!TryGetDirectAddress(address, MpCostOffset + 1, out var directAddress))
        {
            return false;
        }

        spellId = readByte!(directAddress);
        requiredMp = readByte!(directAddress + MpCostOffset);
        return true;
    }

    private bool TryGetDirectAddress(uint address, int length, out int directAddress)
    {
        directAddress = default;
        if (address == 0 || length <= 0 || address > int.MaxValue)
        {
            return false;
        }

        var endExclusive = (ulong)address + (uint)length;
        if (endExclusive > (ulong)int.MaxValue + 1)
        {
            return false;
        }

        directAddress = (int)address;
        return isReadableMemory!(directAddress, length);
    }

    private static bool TryComputeAddress(
        uint baseAddress,
        byte partySlot,
        int partyStride,
        long selectedIndex,
        int recordStride,
        out uint address)
    {
        address = default;
        if (baseAddress == 0 || partyStride < 0 || selectedIndex < 0 || recordStride < 0)
        {
            return false;
        }

        var candidate = (ulong)baseAddress +
            ((ulong)partySlot * (uint)partyStride) +
            ((ulong)selectedIndex * (uint)recordStride);
        if (candidate == 0 || candidate > uint.MaxValue)
        {
            return false;
        }

        address = (uint)candidate;
        return true;
    }

    private readonly record struct MagicSelectionReadState(
        MagicWidgetReadState Widget,
        byte PartySlot,
        ushort CurrentMp,
        int SelectedIndex,
        byte AbilityId,
        byte RequiredMp);

    private readonly record struct AbilityMenuLayout(
        uint RecordsAddress,
        int EntryCount,
        int ActionIdBase);

    private readonly record struct MagicWidgetReadState(
        int First,
        int Cursor,
        int Columns,
        int Rows,
        int ScrollOffset,
        int ScrollDelta,
        int ScrollState)
    {
        public static MagicWidgetReadState From(ActiveMenuWidgetSnapshot widget) =>
            new(
                widget.First,
                widget.Cursor,
                widget.Columns,
                widget.Rows,
                widget.ScrollOffset,
                widget.ScrollDelta,
                widget.ScrollState);
    }
}

public readonly record struct MagicMenuSpellSnapshot(
    int SpellId,
    int MpCost,
    string Name,
    string? Description);

public readonly record struct MagicMenuSlotSnapshot(
    int Slot,
    bool IsEmpty,
    MagicMenuSpellSnapshot Spell);
