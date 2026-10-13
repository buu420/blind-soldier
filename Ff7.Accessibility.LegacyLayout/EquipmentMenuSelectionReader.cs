using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct EquipmentPanelStats(
    int Attack,
    int AttackPercent,
    int Defense,
    int DefensePercent,
    int MagicAttack,
    int MagicDefense,
    int MagicDefensePercent);

public readonly record struct EquipmentMateriaLayout(
    int SlotCount,
    int LinkedPairCount,
    int UnlinkedSlotCount,
    string? LinkedGroupDescription = null);

public readonly record struct EquipmentDefinitionDetails(
    EquipmentPanelStats Stats,
    EquipmentMateriaLayout MateriaLayout,
    int Growth);

public sealed class EquipmentStatReader
{
    public const int AddressWeaponAttack = 0x00DBE734;
    public const int AddressWeaponGrowth = 0x00DBE736;
    public const int AddressWeaponAttackPercent = 0x00DBE738;
    public const int AddressWeaponMateriaSlots = 0x00DBE74C;
    public const int WeaponRecordSize = 0x2C;
    public const int AddressArmorDefense = 0x00DBCCE2;
    public const int AddressArmorMagicDefense = 0x00DBCCE3;
    public const int AddressArmorDefensePercent = 0x00DBCCE4;
    public const int AddressArmorMagicDefensePercent = 0x00DBCCE5;
    public const int AddressArmorMateriaSlots = 0x00DBCCE9;
    public const int AddressArmorGrowth = 0x00DBCCF1;
    public const int ArmorRecordSize = 0x24;
    public const int MateriaSlotCount = 8;

    private readonly ILegacyAddressSpace memory;

    public EquipmentStatReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public bool TryReadPanel(
        int weaponId,
        int armorId,
        out EquipmentPanelStats stats)
    {
        stats = default;
        if (!TryReadWeapon(weaponId, out var attack, out var attackPercent) ||
            !TryReadArmor(
                armorId,
                out var defense,
                out var defensePercent,
                out var magicDefense,
                out var magicDefensePercent))
        {
            return false;
        }

        stats = new EquipmentPanelStats(
            attack,
            attackPercent,
            defense,
            defensePercent,
            0,
            magicDefense,
            magicDefensePercent);
        return true;
    }

    public bool TryReadWeapon(int weaponId, out int attack, out int attackPercent)
    {
        attack = default;
        attackPercent = default;
        return weaponId is >= 0 and < 128 &&
            memory.TryReadByte(
                checked((uint)(AddressWeaponAttack + (weaponId * WeaponRecordSize))),
                out var attackValue) &&
            memory.TryReadByte(
                checked((uint)(AddressWeaponAttackPercent + (weaponId * WeaponRecordSize))),
                out var percentValue) &&
            SetPair(attackValue, percentValue, out attack, out attackPercent);
    }

    /// <summary>Mirrors FixEquipDisplay's seven values, including the exact bonus
    /// types its native renderer applies. These are equipment panel values; the
    /// Status screen's computed character totals are read separately.</summary>
    public bool TryReadVisibleBonusPanel(int weaponId, int armorId, out EquipmentPanelStats stats)
    {
        stats = default;
        var compatibility = new PostscriptEquipmentCompatibility(memory);
        if (!compatibility.HasEquipmentBonusDisplay() || !TryReadPanel(weaponId, armorId, out var basic) ||
            !TryReadBonusTypes(0x00DBE744 + weaponId * WeaponRecordSize, out var weapon) ||
            !TryReadBonusTypes(0x00DBCCF8 + armorId * ArmorRecordSize, out var armor) ||
            !TryReadPanel(weaponId, armorId, out var basicBookend) || basicBookend != basic ||
            !compatibility.HasEquipmentBonusDisplay()) return false;
        stats = basic with
        {
            Attack = basic.Attack + Bonus(armor, 0),
            Defense = basic.Defense + Bonus(weapon, 1),
            MagicAttack = Bonus(weapon, 2) + Bonus(armor, 2),
            MagicDefense = basic.MagicDefense + Bonus(weapon, 3)
        };
        return true;
    }

    private bool TryReadBonusTypes(int address, out byte[] bonuses)
    {
        bonuses = new byte[8];
        Span<byte> bookend = stackalloc byte[8];
        return memory.TryRead((uint)address, bonuses) && memory.TryRead((uint)address, bookend) &&
            bookend.SequenceEqual(bonuses);
    }

    private static int Bonus(byte[] bonuses, byte type)
    {
        for (var index = 0; index < 4; index++)
            if (bonuses[index] == type) return bonuses[index + 4];
        return 0;
    }

    public bool TryReadArmor(
        int armorId,
        out int defense,
        out int defensePercent,
        out int magicDefense,
        out int magicDefensePercent)
    {
        defense = default;
        defensePercent = default;
        magicDefense = default;
        magicDefensePercent = default;
        if (armorId is < 0 or >= 32 ||
            !memory.TryReadByte(
                checked((uint)(AddressArmorDefense + (armorId * ArmorRecordSize))),
                out var defenseValue) ||
            !memory.TryReadByte(
                checked((uint)(AddressArmorDefensePercent + (armorId * ArmorRecordSize))),
                out var defensePercentValue) ||
            !memory.TryReadByte(
                checked((uint)(AddressArmorMagicDefense + (armorId * ArmorRecordSize))),
                out var magicDefenseValue) ||
            !memory.TryReadByte(
                checked((uint)(AddressArmorMagicDefensePercent + (armorId * ArmorRecordSize))),
                out var magicDefensePercentValue))
        {
            return false;
        }

        defense = defenseValue;
        defensePercent = defensePercentValue;
        magicDefense = magicDefenseValue;
        magicDefensePercent = magicDefensePercentValue;
        return true;
    }

    public bool TryReadWeaponDefinition(
        int weaponId,
        out EquipmentDefinitionDetails details)
    {
        details = default;
        if (!TryReadWeapon(weaponId, out var attack, out var attackPercent) ||
            !TryReadMateriaLayout(
                AddressWeaponMateriaSlots,
                WeaponRecordSize,
                weaponId,
                128,
                out var layout) ||
            !TryReadGrowth(
                AddressWeaponGrowth,
                WeaponRecordSize,
                weaponId,
                128,
                out var growth))
        {
            return false;
        }

        details = new EquipmentDefinitionDetails(
            new EquipmentPanelStats(attack, attackPercent, 0, 0, 0, 0, 0),
            layout,
            growth);
        return true;
    }

    public bool TryReadArmorDefinition(
        int armorId,
        out EquipmentDefinitionDetails details)
    {
        details = default;
        if (!TryReadArmor(
                armorId,
                out var defense,
                out var defensePercent,
                out var magicDefense,
                out var magicDefensePercent) ||
            !TryReadMateriaLayout(
                AddressArmorMateriaSlots,
                ArmorRecordSize,
                armorId,
                32,
                out var layout) ||
            !TryReadGrowth(
                AddressArmorGrowth,
                ArmorRecordSize,
                armorId,
                32,
                out var growth))
        {
            return false;
        }

        details = new EquipmentDefinitionDetails(
            new EquipmentPanelStats(
                0,
                0,
                defense,
                defensePercent,
                0,
                magicDefense,
                magicDefensePercent),
            layout,
            growth);
        return true;
    }

    public bool TryReadMateriaSlot(
        int row,
        int equipmentId,
        int column,
        out byte slotType)
    {
        slotType = default;
        if (column is < 0 or >= MateriaSlotCount)
        {
            return false;
        }

        var address = row switch
        {
            0 when equipmentId is >= 0 and < 128 =>
                AddressWeaponMateriaSlots + (equipmentId * WeaponRecordSize) + column,
            1 when equipmentId is >= 0 and < 32 =>
                AddressArmorMateriaSlots + (equipmentId * ArmorRecordSize) + column,
            _ => -1
        };
        return address > 0 &&
            memory.TryReadByte((uint)address, out slotType) &&
            IsValidMateriaSlotType(slotType);
    }

    public bool TryFormatInventoryObject(int objectId, out string text)
    {
        text = string.Empty;
        if (objectId is >= 128 and < 256 &&
            TryReadWeapon(objectId - 128, out var attack, out var attackPercent))
        {
            text = $"Attack {attack}. Attack percentage {attackPercent} percent";
            return true;
        }

        if (objectId is >= 256 and < 288 &&
            TryReadArmor(
                objectId - 256,
                out var defense,
                out var defensePercent,
                out var magicDefense,
                out var magicDefensePercent))
        {
            text =
                $"Defense {defense}. Defense percentage {defensePercent} percent. " +
                $"Magic defense {magicDefense}. Magic defense percentage {magicDefensePercent} percent";
            return true;
        }

        return false;
    }

    public bool TryReadVerifiedMateriaConnections(int row, int equipmentId, int column, out string? description)
    {
        description = null;
        var compatibility = new PostscriptEquipmentCompatibility(memory);
        if (!compatibility.HasMultiLinkedSlots()) return true;
        var address = row switch
        {
            0 when equipmentId is >= 0 and < 128 => AddressWeaponMateriaSlots + equipmentId * WeaponRecordSize,
            1 when equipmentId is >= 0 and < 32 => AddressArmorMateriaSlots + equipmentId * ArmorRecordSize,
            _ => -1
        };
        Span<byte> slots = stackalloc byte[MateriaSlotCount];
        Span<byte> bookend = stackalloc byte[MateriaSlotCount];
        if (address <= 0 || column is < 0 or >= MateriaSlotCount ||
            !memory.TryRead((uint)address, slots) ||
            !TryDecodeMateriaLayout(slots, true, out _) ||
            !memory.TryRead((uint)address, bookend) || !slots.SequenceEqual(bookend) ||
            !compatibility.HasMultiLinkedSlots()) return false;
        var connected = new List<int>();
        var slot = slots[column];
        if (slot is 3 or 7 && column > 0 &&
            (slots[column - 1] == slot || slots[column - 1] == slot - 1)) connected.Add(column);
        if (slot is 2 or 3 or 6 or 7 && column + 1 < slots.Length &&
            slots[column + 1] == (slot is 2 or 6 ? slot + 1 : slot)) connected.Add(column + 2);
        if (connected.Count != 0)
            description = $"Linked to {(row == 0 ? "weapon" : "armor")} materia " +
                (connected.Count == 1 ? $"slot {connected[0]}" : $"slots {connected[0]} and {connected[1]}");
        return true;
    }

    public static string FormatComparison(
        EquipmentPanelStats current,
        EquipmentPanelStats candidate)
    {
        return string.Join(
            ". ",
            FormatValue("Attack", current.Attack, candidate.Attack),
            FormatPercent("Attack percentage", current.AttackPercent, candidate.AttackPercent),
            FormatValue("Defense", current.Defense, candidate.Defense),
            FormatPercent("Defense percentage", current.DefensePercent, candidate.DefensePercent),
            FormatValue("Magic attack", current.MagicAttack, candidate.MagicAttack),
            FormatValue("Magic defense", current.MagicDefense, candidate.MagicDefense),
            FormatPercent(
                "Magic defense percentage",
                current.MagicDefensePercent,
                candidate.MagicDefensePercent));
    }

    public static string FormatWeaponComparison(
        EquipmentPanelStats current,
        EquipmentPanelStats candidate) =>
        string.Join(
            ". ",
            FormatValue("Attack", current.Attack, candidate.Attack),
            FormatPercent("Attack percentage", current.AttackPercent, candidate.AttackPercent));

    public static string FormatArmorComparison(
        EquipmentPanelStats current,
        EquipmentPanelStats candidate) =>
        string.Join(
            ". ",
            FormatValue("Defense", current.Defense, candidate.Defense),
            FormatPercent("Defense percentage", current.DefensePercent, candidate.DefensePercent),
            FormatValue("Magic defense", current.MagicDefense, candidate.MagicDefense),
            FormatPercent(
                "Magic defense percentage",
                current.MagicDefensePercent,
                candidate.MagicDefensePercent));

    public static string FormatPanel(EquipmentPanelStats stats) =>
        string.Join(
            ". ",
            $"Attack {stats.Attack}",
            $"Attack percentage {stats.AttackPercent} percent",
            $"Defense {stats.Defense}",
            $"Defense percentage {stats.DefensePercent} percent",
            $"Magic attack {stats.MagicAttack}",
            $"Magic defense {stats.MagicDefense}",
            $"Magic defense percentage {stats.MagicDefensePercent} percent");

    public static string FormatWeaponPanel(EquipmentPanelStats stats) =>
        string.Join(
            ". ",
            $"Attack {stats.Attack}",
            $"Attack percentage {stats.AttackPercent} percent");

    public static string FormatArmorPanel(EquipmentPanelStats stats) =>
        string.Join(
            ". ",
            $"Defense {stats.Defense}",
            $"Defense percentage {stats.DefensePercent} percent",
            $"Magic defense {stats.MagicDefense}",
            $"Magic defense percentage {stats.MagicDefensePercent} percent");

    public static string FormatMateriaLayout(EquipmentMateriaLayout layout)
    {
        var parts = new List<string> { $"Materia slots {layout.SlotCount}" };
        if (layout.LinkedPairCount > 0)
        {
            parts.Add(
                layout.LinkedPairCount == 1
                    ? "one linked pair"
                    : $"{layout.LinkedPairCount} linked pairs");
        }

        if (layout.UnlinkedSlotCount > 0)
        {
            parts.Add($"{layout.UnlinkedSlotCount} unlinked");
        }

        if (!string.IsNullOrWhiteSpace(layout.LinkedGroupDescription))
            parts.Add(layout.LinkedGroupDescription);

        return string.Join(", ", parts);
    }

    public static string FormatGrowth(int growth) =>
        growth switch
        {
            0 => "Growth None",
            1 => "Growth Normal",
            2 => "Growth Double",
            3 => "Growth Triple",
            _ => throw new ArgumentOutOfRangeException(nameof(growth))
        };

    public static bool TryDecodeMateriaLayout(
        ReadOnlySpan<byte> slots,
        out EquipmentMateriaLayout layout)
        => TryDecodeMateriaLayout(slots, false, out layout);

    public static bool TryDecodeMateriaLayout(
        ReadOnlySpan<byte> slots,
        bool allowVerifiedMultiLinkedSlots,
        out EquipmentMateriaLayout layout)
    {
        layout = default;
        if (slots.Length != MateriaSlotCount)
        {
            return false;
        }

        var slotCount = 0;
        var linkedPairCount = 0;
        var unlinkedSlotCount = 0;
        Span<int> groupsBySize = stackalloc int[MateriaSlotCount + 1];
        groupsBySize.Clear();
        for (var index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            if (!IsValidMateriaSlotType(slot))
            {
                return false;
            }

            if (slot != 0)
            {
                slotCount++;
            }

            if (slot is 1 or 5) unlinkedSlotCount++;

            if (slot is 2 or 6)
            {
                var expectedRight = (byte)(slot + 1);
                if (index + 1 >= slots.Length || slots[index + 1] != expectedRight)
                {
                    return false;
                }

                var size = 2;
                if (allowVerifiedMultiLinkedSlots)
                    while (index + size < slots.Length && slots[index + size] == expectedRight) size++;
                if (size == 2) linkedPairCount++;
                else groupsBySize[size]++;
                slotCount += size - 1;
                index += size - 1;
                continue;
            }

            if (slot is 3 or 7 &&
                (index == 0 || slots[index - 1] != slot - 1))
            {
                return false;
            }
        }

        var groupDescriptions = new List<string>();
        for (var size = 3; size <= MateriaSlotCount; size++)
            if (groupsBySize[size] > 0)
                groupDescriptions.Add(groupsBySize[size] == 1
                    ? $"one linked group of {size} slots"
                    : $"{groupsBySize[size]} linked groups of {size} slots");
        layout = new EquipmentMateriaLayout(
            slotCount,
            linkedPairCount,
            unlinkedSlotCount,
            groupDescriptions.Count == 0 ? null : string.Join(", ", groupDescriptions));
        return true;
    }

    private static string FormatValue(string label, int current, int candidate) =>
        $"{label} {candidate}, {FormatChange(current, candidate, string.Empty)}";

    private static string FormatPercent(string label, int current, int candidate) =>
        $"{label} {candidate} percent, {FormatChange(current, candidate, " percent")}";

    private static string FormatChange(int current, int candidate, string suffix) =>
        candidate switch
        {
            _ when candidate > current => $"up from {current}{suffix}",
            _ when candidate < current => $"down from {current}{suffix}",
            _ => "unchanged"
        };

    private static bool SetPair(
        byte first,
        byte second,
        out int firstValue,
        out int secondValue)
    {
        firstValue = first;
        secondValue = second;
        return true;
    }

    private bool TryReadMateriaLayout(
        int firstSlotAddress,
        int recordSize,
        int itemId,
        int itemCount,
        out EquipmentMateriaLayout layout)
    {
        layout = default;
        if (itemId is < 0 || itemId >= itemCount)
        {
            return false;
        }

        Span<byte> slots = stackalloc byte[MateriaSlotCount];
        var recordAddress = checked(firstSlotAddress + (itemId * recordSize));
        for (var index = 0; index < slots.Length; index++)
        {
            if (!memory.TryReadByte((uint)(recordAddress + index), out slots[index]) ||
                !IsValidMateriaSlotType(slots[index]))
            {
                return false;
            }
        }

        var compatibility = new PostscriptEquipmentCompatibility(memory);
        var chained = compatibility.HasMultiLinkedSlots();
        Span<byte> slotBookend = stackalloc byte[MateriaSlotCount];
        if (chained && (!memory.TryRead((uint)recordAddress, slotBookend) || !slots.SequenceEqual(slotBookend))) return false;
        return TryDecodeMateriaLayout(slots, chained, out layout) &&
            compatibility.HasMultiLinkedSlots() == chained;
    }

    private bool TryReadGrowth(
        int growthAddress,
        int recordSize,
        int itemId,
        int itemCount,
        out int growth)
    {
        growth = default;
        if (itemId is < 0 ||
            itemId >= itemCount ||
            !memory.TryReadByte(
                checked((uint)(growthAddress + (itemId * recordSize))),
                out var growthValue) ||
            growthValue > 3)
        {
            return false;
        }

        growth = growthValue;
        return true;
    }

    private static bool IsValidMateriaSlotType(byte slotType) =>
        slotType is 0 or 1 or 2 or 3 or 5 or 6 or 7;
}

public sealed class EquipmentMenuSelectionReader
{
    public const int AddressEquipmentListWidget = 0x00DCA5F8;
    public const int AddressEquipmentCategory = 0x00DCA5C4;
    public const int AddressEquipmentListCursor = 0x00DCA5FC;
    public const int AddressEquipmentListScroll = 0x00DCA60C;
    public const int AddressEquipmentListActive = 0x00DCA6A0;
    public const int AddressEquipmentCandidates = 0x00DCA6A8;
    public const int AddressEquipmentListCount = 0x00DCA7EC;

    private readonly ILegacyAddressSpace memory;
    private readonly Func<int, string?> resolveWeaponName;
    private readonly Func<int, string?> resolveArmorName;
    private readonly Func<int, string?> resolveAccessoryName;
    private readonly Func<int, string?> resolveInventoryObjectDescription;
    private readonly EquipmentStatReader statReader;
    private readonly int savemapAddress;

    public EquipmentMenuSelectionReader(
        ILegacyAddressSpace memory,
        Func<int, string?>? resolveWeaponName = null,
        Func<int, string?>? resolveArmorName = null,
        Func<int, string?>? resolveAccessoryName = null,
        Func<int, string?>? resolveInventoryObjectDescription = null,
        int savemapAddress = SavemapPartyReader.AddressSavemap)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.resolveWeaponName = resolveWeaponName ?? (_ => null);
        this.resolveArmorName = resolveArmorName ?? (_ => null);
        this.resolveAccessoryName = resolveAccessoryName ?? (_ => null);
        this.resolveInventoryObjectDescription =
            resolveInventoryObjectDescription ?? (_ => null);
        statReader = new EquipmentStatReader(memory);
        this.savemapAddress = savemapAddress;
    }

    public bool TryRead(out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadState(out var state) ||
            !TryResolveName(state.Category, state.CandidateId, out var name, out var objectId))
        {
            return false;
        }

        string? help;
        try
        {
            help = resolveInventoryObjectDescription(objectId);
        }
        catch
        {
            return false;
        }

        var descriptionParts = new List<string>();
        var compatibility = new PostscriptEquipmentCompatibility(memory);
        var bonusDisplay = compatibility.HasEquipmentBonusDisplay();
        if (!string.IsNullOrWhiteSpace(help))
        {
            descriptionParts.Add(help.Trim());
        }

        string detailKey;
        switch (state.Category)
        {
            case 0:
                if (!statReader.TryReadWeaponDefinition(
                        state.CurrentWeaponId,
                        out var currentWeapon) ||
                    !statReader.TryReadWeaponDefinition(
                        state.CandidateId,
                        out var candidateWeapon))
                {
                    return false;
                }

                if (bonusDisplay)
                {
                    if (!statReader.TryReadVisibleBonusPanel(state.CurrentWeaponId, state.CurrentArmorId, out var current) ||
                        !statReader.TryReadVisibleBonusPanel(state.CandidateId, state.CurrentArmorId, out var candidate)) return false;
                    descriptionParts.Add(EquipmentStatReader.FormatComparison(current, candidate));
                }
                else descriptionParts.Add(EquipmentStatReader.FormatWeaponComparison(currentWeapon.Stats, candidateWeapon.Stats));
                descriptionParts.Add(
                    EquipmentStatReader.FormatMateriaLayout(candidateWeapon.MateriaLayout));
                descriptionParts.Add(
                    EquipmentStatReader.FormatGrowth(candidateWeapon.Growth));
                detailKey = candidateWeapon.ToString();
                break;
            case 1:
                if (!statReader.TryReadArmorDefinition(
                        state.CurrentArmorId,
                        out var currentArmor) ||
                    !statReader.TryReadArmorDefinition(
                        state.CandidateId,
                        out var candidateArmor))
                {
                    return false;
                }

                if (bonusDisplay)
                {
                    if (!statReader.TryReadVisibleBonusPanel(state.CurrentWeaponId, state.CurrentArmorId, out var current) ||
                        !statReader.TryReadVisibleBonusPanel(state.CurrentWeaponId, state.CandidateId, out var candidate)) return false;
                    descriptionParts.Add(EquipmentStatReader.FormatComparison(current, candidate));
                }
                else descriptionParts.Add(EquipmentStatReader.FormatArmorComparison(currentArmor.Stats, candidateArmor.Stats));
                descriptionParts.Add(
                    EquipmentStatReader.FormatMateriaLayout(candidateArmor.MateriaLayout));
                descriptionParts.Add(
                    EquipmentStatReader.FormatGrowth(candidateArmor.Growth));
                detailKey = candidateArmor.ToString();
                break;
            case 2:
            case 3:
                detailKey = help?.Trim() ?? string.Empty;
                break;
            default:
                return false;
        }

        var description = string.Join(". ", descriptionParts);

        if (!TryReadState(out var bookend) || bookend != state || compatibility.HasEquipmentBonusDisplay() != bonusDisplay)
        {
            return false;
        }

        selection = new NativeMenuSelection(
            name,
            description.Length == 0 ? null : description,
            $"equip-list:{state.PartySlot}:{state.Category}:{state.AbsoluteIndex}:" +
            $"{state.CandidateId}:{state.CurrentWeaponId}:{state.CurrentArmorId}:{detailKey}" +
            (bonusDisplay ? $":{description}" : string.Empty));
        return true;
    }

    private bool TryReadState(out EquipmentListState state)
    {
        state = default;
        if (!memory.TryReadInt32(
                (uint)SavemapPartyReader.AddressEquipmentMenuPartySlot,
                out var partySlot) ||
            partySlot is < 0 or >= 3 ||
            !memory.TryReadInt32((uint)AddressEquipmentListActive, out var active) ||
            active != 1 ||
            !memory.TryReadInt32((uint)AddressEquipmentCategory, out var category) ||
            category is < 0 or > 3 ||
            (category == 3 && !new PostscriptEquipmentCompatibility(memory).HasTwoAccessories()) ||
            !memory.TryReadInt32((uint)AddressEquipmentListCursor, out var cursor) ||
            !memory.TryReadInt32((uint)AddressEquipmentListScroll, out var scroll) ||
            !memory.TryReadInt32((uint)AddressEquipmentListCount, out var count) ||
            cursor is < 0 or >= 8 ||
            scroll < 0 ||
            count is < 1 or > 128)
        {
            return false;
        }

        var absoluteIndex = cursor + scroll;
        if (absoluteIndex < 0 || absoluteIndex >= count ||
            !memory.TryReadByte(
                checked((uint)(AddressEquipmentCandidates + absoluteIndex)),
                out var candidateId) ||
            (category != 0 && candidateId >= 32))
        {
            return false;
        }

        var partyAddress = checked(savemapAddress + SavemapPartyReader.PartyMembersOffset + partySlot);
        if (!memory.TryReadByte((uint)partyAddress, out var characterId) ||
            characterId >= 9)
        {
            return false;
        }

        var characterAddress = checked(
            savemapAddress +
            SavemapPartyReader.CharactersOffset +
            (characterId * SavemapPartyReader.CharacterSize));
        if (!memory.TryReadByte(
                checked((uint)(characterAddress + SavemapPartyReader.EquippedWeaponOffset)),
                out var currentWeapon) ||
            !memory.TryReadByte(
                checked((uint)(characterAddress + SavemapPartyReader.EquippedArmorOffset)),
                out var currentArmor) ||
            currentWeapon >= 128 ||
            currentArmor >= 32)
        {
            return false;
        }

        state = new EquipmentListState(
            partySlot,
            category,
            cursor,
            scroll,
            count,
            absoluteIndex,
            candidateId,
            characterId,
            currentWeapon,
            currentArmor);
        return true;
    }

    private bool TryResolveName(
        int category,
        int itemId,
        out string name,
        out int objectId)
    {
        name = string.Empty;
        objectId = category switch
        {
            0 => itemId + 128,
            1 => itemId + 256,
            2 or 3 => itemId + 288,
            _ => -1
        };

        string? candidate;
        try
        {
            candidate = category switch
            {
                0 => resolveWeaponName(itemId),
                1 => resolveArmorName(itemId),
                2 or 3 => resolveAccessoryName(itemId),
                _ => null
            };
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        name = candidate.Trim();
        return true;
    }

    private readonly record struct EquipmentListState(
        int PartySlot,
        int Category,
        int Cursor,
        int Scroll,
        int Count,
        int AbsoluteIndex,
        int CandidateId,
        int CharacterId,
        int CurrentWeaponId,
        int CurrentArmorId);
}
