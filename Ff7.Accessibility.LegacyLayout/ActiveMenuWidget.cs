namespace Ff7.Accessibility.Reloaded;

public enum MenuWidgetKind
{
    Generic,
    RootMainMenu,
    ItemCommand,
    ItemList,
    ItemTarget,
    MagicCategory,
    MagicTarget,
    MagicList,
    SummonList,
    EnemySkillList,
    MateriaCommand,
    MateriaSlot,
    MateriaList,
    CharacterList,
    EquipmentSlot,
    EquipmentList,
    ConfigMain,
    ConfigChoice,
    ConfigSoundVolume,
    TitleSaveFile,
    LimitCommand,
    LimitLevel,
    LimitMoveList
}

public readonly record struct MenuWidgetDescriptor(uint Address, string Name, MenuWidgetKind Kind);

public static class MenuWidgetCatalog
{
    public static IReadOnlyList<MenuWidgetDescriptor> All { get; } =
    [
        new(0x00DC1150, "Item/Main list", MenuWidgetKind.RootMainMenu),
        new(0x00DC1188, "Main menu party", MenuWidgetKind.CharacterList),
        new(0x00DC11C0, "Order party", MenuWidgetKind.CharacterList),
        new(0x00DC1088, "Config sound volume", MenuWidgetKind.ConfigSoundVolume),
        new(0x00DD6D98, "Title load save file", MenuWidgetKind.TitleSaveFile),
        new(0x00DD6F20, "Title menu", MenuWidgetKind.Generic),
        new(0x00DD1A18, "Item submenu command", MenuWidgetKind.ItemCommand),
        new(0x00DD1A50, "Item list", MenuWidgetKind.ItemList),
        new(0x00DD1A88, "Item target", MenuWidgetKind.ItemTarget),
        new(0x00DD1698, "Magic category", MenuWidgetKind.MagicCategory),
        new(0x00DD16D0, "Magic target", MenuWidgetKind.MagicTarget),
        new(0x00DD1708, "Magic list", MenuWidgetKind.MagicList),
        new(0x00DD1740, "Summon list", MenuWidgetKind.SummonList),
        new(0x00DD1778, "Enemy Skill list", MenuWidgetKind.EnemySkillList),
        new(0x00DD12B8, "Materia command", MenuWidgetKind.MateriaCommand),
        new(0x00DD12F0, "Materia slot", MenuWidgetKind.MateriaSlot),
        new(0x00DD1360, "Materia list", MenuWidgetKind.MateriaList),
        new(0x00DCA5C0, "Equip slot", MenuWidgetKind.EquipmentSlot),
        new(0x00DCA5F8, "Equip list", MenuWidgetKind.EquipmentList),
        new(0x00DC6C48, "Order party", MenuWidgetKind.CharacterList),
        new(0x00DCA3D0, "Limit character", MenuWidgetKind.CharacterList),
        new(0x00DCA408, "Limit list", MenuWidgetKind.Generic),
        new(0x00DCA198, "Limit set level", MenuWidgetKind.LimitLevel),
        new(0x00DCA1D0, "Limit command", MenuWidgetKind.LimitCommand),
        new(0x00DCA208, "Limit check level", MenuWidgetKind.LimitLevel),
        new(0x00DCA240, "Limit move list", MenuWidgetKind.LimitMoveList),
        new(0x00DCA118, "PHS party", MenuWidgetKind.CharacterList),
        new(0x00DC6AE0, "Save file or Quit choice", MenuWidgetKind.Generic),
        new(0x00DC6B18, "Save game slot", MenuWidgetKind.Generic),
        new(0x00DC6C68, "Save confirmation", MenuWidgetKind.Generic)
    ];

    private static readonly IReadOnlyDictionary<uint, MenuWidgetDescriptor> ByAddress =
        All.ToDictionary(item => item.Address);

    public static bool TryResolve(uint address, out MenuWidgetDescriptor descriptor) =>
        ByAddress.TryGetValue(address, out descriptor);

    public static bool TryResolve(int address, out MenuWidgetDescriptor descriptor)
    {
        descriptor = default;
        return address > 0 && TryResolve((uint)address, out descriptor);
    }
}

public sealed class ActiveMenuWidgetReader
{
    private readonly Func<int, int>? readInt32;
    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace? addressSpace;

    public ActiveMenuWidgetReader(Func<int, int> readInt32)
    {
        this.readInt32 = readInt32 ?? throw new ArgumentNullException(nameof(readInt32));
    }

    public ActiveMenuWidgetReader(Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public ActiveMenuWidgetSnapshot Read(uint address)
    {
        if (!TryRead(address, out var snapshot))
        {
            throw new InvalidOperationException($"Could not read active menu widget at 0x{address:X8}.");
        }

        return snapshot;
    }

    public ActiveMenuWidgetSnapshot Read(int address)
    {
        if (address <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(address));
        }

        return Read((uint)address);
    }

    public bool TryRead(uint address, out ActiveMenuWidgetSnapshot snapshot)
    {
        snapshot = default;
        if (address == 0 || !TryReadFields(address, out var candidate))
        {
            return false;
        }

        if (addressSpace is not null &&
            (!TryReadFields(address, out var bookend) || bookend != candidate))
        {
            return false;
        }

        if (candidate.Columns is <= 0 or > 16 ||
            candidate.Rows is <= 0 or > 400 ||
            candidate.First < 0 || candidate.First >= candidate.Columns ||
            candidate.Cursor < 0 || candidate.Cursor >= candidate.Rows)
        {
            return false;
        }

        snapshot = candidate;
        return true;
    }

    public bool TryRead(int address, out ActiveMenuWidgetSnapshot snapshot)
    {
        snapshot = default;
        return address > 0 && TryRead((uint)address, out snapshot);
    }

    private bool TryReadFields(uint address, out ActiveMenuWidgetSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadInt32(address, 0x00, out var first) ||
            !TryReadInt32(address, 0x04, out var cursor) ||
            !TryReadInt32(address, 0x08, out var columns) ||
            !TryReadInt32(address, 0x0C, out var rows) ||
            !TryReadInt32(address, 0x14, out var scrollOffset) ||
            !TryReadInt32(address, 0x24, out var scrollDelta) ||
            !TryReadInt32(address, 0x30, out var scrollState))
        {
            return false;
        }

        var known = MenuWidgetCatalog.TryResolve(address, out var descriptor);
        snapshot = new ActiveMenuWidgetSnapshot(
            address,
            known ? descriptor.Name : $"Widget 0x{address:X8}",
            known ? descriptor.Kind : MenuWidgetKind.Generic,
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
        var address = (ulong)baseAddress + (uint)offset;
        if (address == 0 || address > uint.MaxValue)
        {
            value = default;
            return false;
        }

        if (addressSpace is not null)
        {
            return Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadInt32(
                addressSpace,
                (uint)address,
                out value);
        }

        if (address > int.MaxValue)
        {
            value = default;
            return false;
        }

        value = readInt32!((int)address);
        return true;
    }
}

public readonly record struct ActiveMenuWidgetSnapshot(
    uint Address,
    string Name,
    MenuWidgetKind Kind,
    int First,
    int Cursor,
    int Columns,
    int Rows,
    int ScrollOffset,
    int ScrollDelta,
    int ScrollState,
    InventoryItemSnapshot? InventoryItem = null,
    NativeMenuSelection? NativeSelection = null,
    MagicMenuSpellSnapshot? MagicSpell = null);
