namespace Ff7.Accessibility.Reloaded;

public sealed class SavemapPartyReader
{
    public const int AddressSavemap = InventoryItemReader.AddressSavemap;
    public const int CharacterSize = 0x84;
    public const int PartyMembersOffset = 0x4F8;
    public const int CharactersOffset = PartyMembersOffset - (CharacterSize * 9);
    public const int CharacterNameOffset = 0x10;
    public const int LevelOffset = 0x01;
    public const int LimitLevelOffset = 0x0E;
    public const int LimitGaugeOffset = 0x0F;
    public const int EquippedWeaponOffset = 0x1C;
    public const int EquippedArmorOffset = 0x1D;
    public const int EquippedAccessoryOffset = 0x1E;
    public const int CurrentHpOffset = 0x2C;
    public const int CurrentMpOffset = 0x30;
    public const int MaxHpOffset = 0x38;
    public const int MaxMpOffset = 0x3A;
    public const int ExperienceOffset = 0x3C;
    public const int ExperienceToNextLevelOffset = 0x80;
    public const int AddressEquipmentMenuPartySlot = 0x00DCA4A4;

    public const int AddressComputedPartyData = 0x00DBA498;
    public const int ComputedPartyBlockSize = 0x440;
    public const int ComputedStrengthOffset = 0x02;
    public const int ComputedVitalityOffset = 0x03;
    public const int ComputedMagicOffset = 0x04;
    public const int ComputedSpiritOffset = 0x05;
    public const int ComputedDexterityOffset = 0x06;
    public const int ComputedLuckOffset = 0x07;
    public const int ComputedAttackOffset = 0x08;
    public const int ComputedDefenseOffset = 0x0A;
    public const int ComputedMagicAttackOffset = 0x0C;
    public const int ComputedMagicDefenseOffset = 0x0E;

    public const int AddressWeaponAttackPercent = 0x00DBE738;
    public const int WeaponRecordSize = 0x2C;
    public const int AddressArmorDefensePercent = 0x00DBCCE4;
    public const int AddressArmorMagicDefensePercent = 0x00DBCCE5;
    public const int ArmorRecordSize = 0x24;

    private readonly Func<int, byte>? readByte;
    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace? addressSpace;
    private readonly Func<int, string?>? resolveWeaponName;
    private readonly Func<int, string?>? resolveArmorName;
    private readonly Func<int, string?>? resolveAccessoryName;
    private readonly Func<int, string?>? resolveInventoryObjectDescription;
    private readonly int savemapAddress;

    public SavemapPartyReader(
        Func<int, byte> readByte,
        Func<int, string?>? resolveWeaponName = null,
        Func<int, string?>? resolveArmorName = null,
        Func<int, string?>? resolveAccessoryName = null,
        int savemapAddress = AddressSavemap,
        Func<int, string?>? resolveInventoryObjectDescription = null)
    {
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
        this.resolveWeaponName = resolveWeaponName;
        this.resolveArmorName = resolveArmorName;
        this.resolveAccessoryName = resolveAccessoryName;
        this.resolveInventoryObjectDescription = resolveInventoryObjectDescription;
        this.savemapAddress = savemapAddress;
    }

    public SavemapPartyReader(
        Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace,
        Func<int, string?>? resolveWeaponName = null,
        Func<int, string?>? resolveArmorName = null,
        Func<int, string?>? resolveAccessoryName = null,
        int savemapAddress = AddressSavemap,
        Func<int, string?>? resolveInventoryObjectDescription = null)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.resolveWeaponName = resolveWeaponName;
        this.resolveArmorName = resolveArmorName;
        this.resolveAccessoryName = resolveAccessoryName;
        this.resolveInventoryObjectDescription = resolveInventoryObjectDescription;
        this.savemapAddress = savemapAddress;
    }

    public bool TryReadPartySlot(int partySlot, out PartyMemberSnapshot snapshot)
    {
        snapshot = default;
        if (partySlot is < 0 or >= 3 ||
            !TryReadByte(savemapAddress + PartyMembersOffset + partySlot, out var characterId) ||
            !TryReadCharacter(characterId, out var candidate) ||
            !VerifyPartySlot(partySlot, characterId))
        {
            return false;
        }

        snapshot = candidate;
        return true;
    }

    public bool TryReadLimitGauge(int partySlot, out byte value)
    {
        value = 0;
        if (partySlot is < 0 or >= 3 ||
            !TryReadByte(savemapAddress + PartyMembersOffset + partySlot, out var characterId) ||
            characterId >= 9)
        {
            return false;
        }

        var characterBase = GetCharacterBase(characterId);
        if (!TryReadByte(characterBase + LimitGaugeOffset, out var candidate) ||
            !VerifyPartySlot(partySlot, characterId))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    public bool TryReadEquipment(int partySlot, int equipmentSlot, out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadPartySlot(partySlot, out var member))
        {
            return false;
        }

        var characterBase = GetCharacterBase(member.CharacterId);
        string label;
        string? name;
        int itemId;
        switch (equipmentSlot)
        {
            case 0:
                label = "Weapon";
                if (!TryReadByte(characterBase + EquippedWeaponOffset, out var weaponId))
                {
                    return false;
                }

                itemId = weaponId;
                name = resolveWeaponName?.Invoke(itemId);
                break;
            case 1:
                label = "Armor";
                if (!TryReadByte(characterBase + EquippedArmorOffset, out var armorId))
                {
                    return false;
                }

                itemId = armorId;
                name = resolveArmorName?.Invoke(itemId);
                break;
            case 2:
                label = "Accessory";
                if (!TryReadByte(characterBase + EquippedAccessoryOffset, out var accessoryId))
                {
                    return false;
                }

                itemId = accessoryId;
                name = itemId == 0xFF ? "None" : resolveAccessoryName?.Invoke(itemId);
                break;
            default:
                return false;
        }

        if (string.IsNullOrWhiteSpace(name) || !VerifyPartySlot(partySlot, member.CharacterId))
        {
            return false;
        }

        var objectId = equipmentSlot switch
        {
            0 => itemId + 128,
            1 => itemId + 256,
            2 when itemId != 0xFF => itemId + 288,
            _ => -1
        };
        string? help = null;
        try
        {
            help = objectId < 0 ? null : resolveInventoryObjectDescription?.Invoke(objectId);
        }
        catch
        {
            return false;
        }

        string? nativeDetails;
        switch (equipmentSlot)
        {
            case 0:
                if (!TryReadWeaponDefinition(itemId, out var weaponDetails))
                {
                    if (addressSpace is not null)
                    {
                        return false;
                    }

                    nativeDetails = null;
                    break;
                }

                nativeDetails = string.Join(
                    ". ",
                    EquipmentStatReader.FormatWeaponPanel(weaponDetails.Stats),
                    EquipmentStatReader.FormatMateriaLayout(weaponDetails.MateriaLayout),
                    EquipmentStatReader.FormatGrowth(weaponDetails.Growth));
                break;
            case 1:
                if (!TryReadArmorDefinition(itemId, out var armorDetails))
                {
                    if (addressSpace is not null)
                    {
                        return false;
                    }

                    nativeDetails = null;
                    break;
                }

                nativeDetails = string.Join(
                    ". ",
                    EquipmentStatReader.FormatArmorPanel(armorDetails.Stats),
                    EquipmentStatReader.FormatMateriaLayout(armorDetails.MateriaLayout),
                    EquipmentStatReader.FormatGrowth(armorDetails.Growth));
                break;
            case 2:
                nativeDetails = null;
                break;
            default:
                return false;
        }

        var description = string.Join(
            ". ",
            new[] { help, nativeDetails }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var selectedOffset = equipmentSlot switch
        {
            0 => EquippedWeaponOffset,
            1 => EquippedArmorOffset,
            _ => EquippedAccessoryOffset
        };
        if (!VerifyPartySlot(partySlot, member.CharacterId) ||
            !TryReadByte(characterBase + selectedOffset, out var selectedBookend) ||
            selectedBookend != itemId)
        {
            return false;
        }

        selection = new NativeMenuSelection(
            $"{label}, {name}",
            description.Length == 0 ? null : description,
            $"equip:{partySlot}:{equipmentSlot}:{itemId}");
        return true;
    }

    public bool TryReadSelectedEquipment(int equipmentSlot, out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadUInt32(AddressEquipmentMenuPartySlot, out var partySlotValue) ||
            partySlotValue >= 3 ||
            !TryReadEquipment((int)partySlotValue, equipmentSlot, out var candidate) ||
            !TryReadUInt32(AddressEquipmentMenuPartySlot, out var selectorBookend) ||
            selectorBookend != partySlotValue)
        {
            return false;
        }

        selection = candidate;
        return true;
    }

    public bool TryReadStatusSummary(int partySlot, out StatusMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadPartySlot(partySlot, out var member))
        {
            return false;
        }

        var characterBase = GetCharacterBase(member.CharacterId);
        if (!TryReadByte(characterBase + EquippedWeaponOffset, out var weaponId) ||
            !TryReadByte(characterBase + EquippedArmorOffset, out var armorId) ||
            !TryReadByte(characterBase + EquippedAccessoryOffset, out var accessoryId) ||
            weaponId == 0xFF || armorId == 0xFF)
        {
            return false;
        }

        var computedBase = AddressComputedPartyData + (partySlot * ComputedPartyBlockSize);
        if (!TryReadByte(computedBase + ComputedDexterityOffset, out var dexterity) ||
            !TryReadByte(characterBase + LevelOffset, out var level) ||
            !TryReadUInt16(characterBase + CurrentHpOffset, out var currentHp) ||
            !TryReadUInt16(characterBase + MaxHpOffset, out var maxHp) ||
            !TryReadUInt16(characterBase + CurrentMpOffset, out var currentMp) ||
            !TryReadUInt16(characterBase + MaxMpOffset, out var maxMp) ||
            !TryReadByte(computedBase + ComputedStrengthOffset, out var strength) ||
            !TryReadByte(computedBase + ComputedVitalityOffset, out var vitality) ||
            !TryReadByte(computedBase + ComputedMagicOffset, out var magic) ||
            !TryReadByte(computedBase + ComputedSpiritOffset, out var spirit) ||
            !TryReadByte(computedBase + ComputedLuckOffset, out var luck) ||
            !TryReadUInt16(computedBase + ComputedAttackOffset, out var attack) ||
            !TryReadByte(AddressWeaponAttackPercent + (weaponId * WeaponRecordSize), out var attackPercent) ||
            !TryReadUInt16(computedBase + ComputedDefenseOffset, out var defense) ||
            !TryReadByte(AddressArmorDefensePercent + (armorId * ArmorRecordSize), out var armorDefensePercent) ||
            !TryReadUInt16(computedBase + ComputedMagicAttackOffset, out var magicAttack) ||
            !TryReadUInt16(computedBase + ComputedMagicDefenseOffset, out var magicDefense) ||
            !TryReadByte(AddressArmorMagicDefensePercent + (armorId * ArmorRecordSize), out var magicDefensePercent) ||
            !TryReadUInt32(characterBase + ExperienceOffset, out var experience) ||
            !TryReadUInt32(characterBase + ExperienceToNextLevelOffset, out var experienceToNextLevel) ||
            !TryReadByte(characterBase + LimitLevelOffset, out var limitLevel) ||
            !VerifyPartySlot(partySlot, member.CharacterId))
        {
            return false;
        }

        snapshot = new StatusMenuSnapshot(
            partySlot,
            member.CharacterId,
            member.Name,
            level,
            currentHp,
            maxHp,
            currentMp,
            maxMp,
            strength,
            dexterity,
            vitality,
            magic,
            spirit,
            luck,
            attack,
            attackPercent,
            defense,
            (dexterity >> 2) + armorDefensePercent,
            magicAttack,
            magicDefense,
            magicDefensePercent,
            experience,
            experienceToNextLevel,
            limitLevel,
            resolveWeaponName?.Invoke(weaponId),
            resolveArmorName?.Invoke(armorId),
            accessoryId == 0xFF ? "None" : resolveAccessoryName?.Invoke(accessoryId));
        return true;
    }

    private bool TryReadCharacter(int characterId, out PartyMemberSnapshot snapshot)
    {
        snapshot = default;
        if (characterId is < 0 or >= 9)
        {
            return false;
        }

        var nameBytes = new byte[12];
        var nameAddress = GetCharacterBase(characterId) + CharacterNameOffset;
        if (addressSpace is not null)
        {
            if (nameAddress <= 0 || !addressSpace.TryRead((uint)nameAddress, nameBytes))
            {
                return false;
            }
        }
        else
        {
            for (var index = 0; index < nameBytes.Length; index++)
            {
                if (!TryReadByte(nameAddress + index, out nameBytes[index]))
                {
                    return false;
                }
            }
        }

        var text = DecodeFixedName(nameBytes);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        snapshot = new PartyMemberSnapshot(characterId, text);
        return true;
    }

    private bool VerifyPartySlot(int partySlot, int expectedCharacterId)
    {
        if (addressSpace is null)
        {
            return true;
        }

        return TryReadByte(savemapAddress + PartyMembersOffset + partySlot, out var characterId) &&
            characterId == expectedCharacterId;
    }

    private int GetCharacterBase(int characterId) =>
        savemapAddress + CharactersOffset + (characterId * CharacterSize);

    private bool TryReadWeaponDefinition(
        int weaponId,
        out EquipmentDefinitionDetails details)
    {
        details = default;
        if (weaponId is < 0 or >= 128 ||
            !TryReadByte(
                EquipmentStatReader.AddressWeaponAttack +
                (weaponId * EquipmentStatReader.WeaponRecordSize),
                out var attack) ||
            !TryReadByte(
                EquipmentStatReader.AddressWeaponAttackPercent +
                (weaponId * EquipmentStatReader.WeaponRecordSize),
                out var attackPercent) ||
            !TryReadMateriaLayout(
                EquipmentStatReader.AddressWeaponMateriaSlots,
                EquipmentStatReader.WeaponRecordSize,
                weaponId,
                out var layout) ||
            !TryReadByte(
                EquipmentStatReader.AddressWeaponGrowth +
                (weaponId * EquipmentStatReader.WeaponRecordSize),
                out var growth) ||
            growth > 3)
        {
            return false;
        }

        details = new EquipmentDefinitionDetails(
            new EquipmentPanelStats(attack, attackPercent, 0, 0, 0, 0, 0),
            layout,
            growth);
        return true;
    }

    private bool TryReadArmorDefinition(
        int armorId,
        out EquipmentDefinitionDetails details)
    {
        details = default;
        if (armorId is < 0 or >= 32 ||
            !TryReadByte(
                EquipmentStatReader.AddressArmorDefense +
                (armorId * EquipmentStatReader.ArmorRecordSize),
                out var defense) ||
            !TryReadByte(
                EquipmentStatReader.AddressArmorDefensePercent +
                (armorId * EquipmentStatReader.ArmorRecordSize),
                out var defensePercent) ||
            !TryReadByte(
                EquipmentStatReader.AddressArmorMagicDefense +
                (armorId * EquipmentStatReader.ArmorRecordSize),
                out var magicDefense) ||
            !TryReadByte(
                EquipmentStatReader.AddressArmorMagicDefensePercent +
                (armorId * EquipmentStatReader.ArmorRecordSize),
                out var magicDefensePercent) ||
            !TryReadMateriaLayout(
                EquipmentStatReader.AddressArmorMateriaSlots,
                EquipmentStatReader.ArmorRecordSize,
                armorId,
                out var layout) ||
            !TryReadByte(
                EquipmentStatReader.AddressArmorGrowth +
                (armorId * EquipmentStatReader.ArmorRecordSize),
                out var growth) ||
            growth > 3)
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

    private bool TryReadMateriaLayout(
        int firstSlotAddress,
        int recordSize,
        int itemId,
        out EquipmentMateriaLayout layout)
    {
        Span<byte> slots = stackalloc byte[EquipmentStatReader.MateriaSlotCount];
        var recordAddress = checked(firstSlotAddress + (itemId * recordSize));
        for (var index = 0; index < slots.Length; index++)
        {
            if (!TryReadByte(recordAddress + index, out slots[index]))
            {
                layout = default;
                return false;
            }
        }

        return EquipmentStatReader.TryDecodeMateriaLayout(slots, out layout);
    }

    private bool TryReadByte(int address, out byte value)
    {
        if (address <= 0)
        {
            value = default;
            return false;
        }

        if (addressSpace is not null)
        {
            return Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadByte(
                addressSpace,
                (uint)address,
                out value);
        }

        value = readByte!(address);
        return true;
    }

    private bool TryReadUInt16(int address, out ushort value)
    {
        if (addressSpace is not null)
        {
            return Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadUInt16(
                addressSpace,
                (uint)address,
                out value);
        }

        if (!TryReadByte(address, out var low) || !TryReadByte(address + 1, out var high))
        {
            value = default;
            return false;
        }

        value = (ushort)(low | (high << 8));
        return true;
    }

    private bool TryReadUInt32(int address, out uint value)
    {
        if (addressSpace is not null)
        {
            return Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadUInt32(
                addressSpace,
                (uint)address,
                out value);
        }

        if (!TryReadByte(address, out var byte0) ||
            !TryReadByte(address + 1, out var byte1) ||
            !TryReadByte(address + 2, out var byte2) ||
            !TryReadByte(address + 3, out var byte3))
        {
            value = default;
            return false;
        }

        value = (uint)(byte0 | (byte1 << 8) | (byte2 << 16) | (byte3 << 24));
        return true;
    }

    private static string DecodeFixedName(ReadOnlySpan<byte> bytes)
    {
        var terminatorIndex = bytes.IndexOf((byte)0xFF);
        var textBytes = terminatorIndex >= 0 ? bytes[..(terminatorIndex + 1)] : bytes;
        return terminatorIndex >= 0
            ? Ff7EncodedTextDecoder.DecodeTerminated(textBytes)
            : Ff7EncodedTextDecoder.Decode(textBytes);
    }
}

public readonly record struct PartyMemberSnapshot(int CharacterId, string Name);

public readonly record struct StatusMenuSnapshot(
    int PartySlot,
    int CharacterId,
    string Name,
    int Level,
    int CurrentHp,
    int MaxHp,
    int CurrentMp,
    int MaxMp,
    int Strength,
    int Dexterity,
    int Vitality,
    int Magic,
    int Spirit,
    int Luck,
    int Attack,
    int AttackPercent,
    int Defense,
    int DefensePercent,
    int MagicAttack,
    int MagicDefense,
    int MagicDefensePercent,
    uint Experience,
    uint ExperienceToNextLevel,
    int LimitLevel,
    string? WeaponName,
    string? ArmorName,
    string? AccessoryName);
