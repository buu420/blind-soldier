using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct ShopMenuSnapshot(
    int State,
    string Speech,
    string Key);

public sealed class ShopMenuStateReader
{
    public const byte ShopModule = 5;
    public const int AddressMenuState = 0x0092565C;
    public const int AddressShopDefinitions = 0x00923418;
    public const int ShopDefinitionSize = 0x54;
    public const int AddressPriceTable = 0x00924E58;
    public const int AddressShopIndex = 0x00DD4724;
    public const int AddressActiveState = 0x00DD4734;
    public const int AddressQuantity = 0x00DD473C;
    public const int AddressTopCommandWidget = 0x00DD6B48;
    public const int AddressBuyListCursor = 0x00DD6B84;
    public const int AddressBuyListScroll = 0x00DD6B94;
    public const int AddressSellItemColumn = 0x00DD6BB8;
    public const int AddressSellItemRow = 0x00DD6BBC;
    public const int AddressSellItemScroll = 0x00DD6BCC;
    public const int AddressSellMateriaCursor = 0x00DD6BF4;
    public const int AddressSellMateriaScroll = 0x00DD6C04;
    public const int AddressSellTypeWidget = 0x00DD6C98;
    public const int AddressGil = 0x00DC08B4;
    public const int AddressRecruitedCharacterMask = 0x00DC0DDE;
    public const int AddressWeaponEquipMask = 0x00DBE73E;
    public const int AddressArmorEquipMask = 0x00DBCCF2;

    private const int ShopStateCount = 7;
    private const int ShopCount = 80;
    private const int ShopStockCount = 10;
    private const int MateriaInventorySlotCount = 200;
    private const int CharacterCount = 9;
    private const int CharacterWeaponMateriaOffset = 0x40;
    private const int CharacterArmorMateriaOffset = 0x60;
    private const int CharacterMateriaSlotCount = 8;

    private readonly ILegacyAddressSpace memory;
    private readonly Func<int, string?> resolveInventoryObjectName;
    private readonly Func<int, string?> resolveInventoryObjectDescription;
    private readonly Func<int, string?> resolveMateriaName;
    private readonly Func<int, string?> resolveMateriaDescription;

    public ShopMenuStateReader(
        ILegacyAddressSpace memory,
        Func<int, string?>? resolveInventoryObjectName = null,
        Func<int, string?>? resolveInventoryObjectDescription = null,
        Func<int, string?>? resolveMateriaName = null,
        Func<int, string?>? resolveMateriaDescription = null)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.resolveInventoryObjectName = resolveInventoryObjectName ?? (_ => null);
        this.resolveInventoryObjectDescription =
            resolveInventoryObjectDescription ?? (_ => null);
        this.resolveMateriaName = resolveMateriaName ?? (_ => null);
        this.resolveMateriaDescription = resolveMateriaDescription ?? (_ => null);
    }

    public bool TryReadOwnership(out bool ownsShop)
    {
        ownsShop = false;
        if (!TryReadEnvelope(out var candidate) ||
            !TryReadEnvelope(out var bookend) ||
            candidate != bookend)
        {
            return false;
        }

        ownsShop = candidate.IsShop;
        return true;
    }

    public bool TryRead(out ShopMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadEnvelope(out var opening) ||
            !opening.IsShop)
        {
            return false;
        }

        var success = opening.State switch
        {
            0 => TryReadTopCommand(out snapshot),
            1 => TryReadBuyList(quantityMode: false, out snapshot),
            2 => TryReadSellItemList(quantityMode: false, out snapshot),
            3 => TryReadSellMateriaList(out snapshot),
            4 => TryReadBuyList(quantityMode: true, out snapshot),
            5 => TryReadSellItemList(quantityMode: true, out snapshot),
            6 => TryReadSellType(out snapshot),
            _ => false
        };
        return success &&
            snapshot.State == opening.State &&
            TryReadEnvelope(out var closing) &&
            closing == opening;
    }

    private bool TryReadEnvelope(out ShopEnvelope envelope)
    {
        envelope = default;
        if (!memory.TryReadByte(
                (uint)FieldPositionReader.AddressCurrentModule,
                out var module) ||
            !memory.TryReadInt32((uint)AddressActiveState, out var active) ||
            !memory.TryReadInt32((uint)AddressMenuState, out var state))
        {
            return false;
        }

        envelope = new ShopEnvelope(module, active, state);
        return true;
    }

    private bool TryReadTopCommand(out ShopMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!memory.TryReadInt32((uint)AddressTopCommandWidget, out var cursor) ||
            cursor is < 0 or > 2)
        {
            return false;
        }

        var text = cursor switch
        {
            0 => "Buy",
            1 => "Sell",
            _ => "Exit"
        };
        snapshot = new ShopMenuSnapshot(0, text, $"shop:command:{cursor}");
        return true;
    }

    private bool TryReadSellType(out ShopMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!memory.TryReadInt32((uint)AddressSellTypeWidget, out var cursor) ||
            cursor is < 0 or > 1)
        {
            return false;
        }

        var text = cursor == 0 ? "Sell items" : "Sell materia";
        snapshot = new ShopMenuSnapshot(6, text, $"shop:sell-type:{cursor}");
        return true;
    }

    private bool TryReadBuyList(bool quantityMode, out ShopMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadSelectedBuyItem(out var item) ||
            !memory.TryReadUInt32((uint)AddressGil, out var gil))
        {
            return false;
        }

        if (!quantityMode)
        {
            var unavailable = GetBuyUnavailableReason(item, gil);
            var speech = JoinClauses(
                $"Buy, {item.Name}",
                $"Price {item.UnitPrice} gil",
                $"You have {gil} gil",
                $"Owned {item.OwnedQuantity}",
                $"Equipped {item.EquippedQuantity}",
                item.Description,
                item.VisibleComparison,
                unavailable);
            snapshot = new ShopMenuSnapshot(
                1,
                speech,
                $"shop:buy:{item.Key}:gil:{gil}:unavailable:{unavailable}");
            return true;
        }

        if (!memory.TryReadInt32((uint)AddressQuantity, out var quantity) ||
            quantity is < 1 or > 99)
        {
            return false;
        }

        var total = (ulong)item.UnitPrice * (uint)quantity;
        snapshot = new ShopMenuSnapshot(
            4,
            JoinClauses(
                $"Buy {item.Name}, quantity {quantity}",
                $"Total {total} gil",
                $"You have {gil} gil",
                $"Owned {item.OwnedQuantity}",
                $"Equipped {item.EquippedQuantity}",
                item.Description,
                item.VisibleComparison),
            $"shop:buy-quantity:{item.Key}:quantity:{quantity}:gil:{gil}:total:{total}");
        return true;
    }

    private bool TryReadSellItemList(bool quantityMode, out ShopMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadSelectedInventoryItem(out var item))
        {
            return false;
        }

        if (!quantityMode)
        {
            snapshot = new ShopMenuSnapshot(
                2,
                JoinClauses(
                    $"Sell, {item.Name}",
                    $"Owned {item.OwnedQuantity}",
                    item.Description),
                $"shop:sell:{item.Key}");
            return true;
        }

        if (!memory.TryReadInt32((uint)AddressQuantity, out var quantity) ||
            quantity is < 1 or > 99 ||
            quantity > item.OwnedQuantity ||
            !memory.TryReadUInt32((uint)AddressGil, out var gil))
        {
            return false;
        }

        var total = (ulong)item.UnitPrice * (uint)quantity;
        var projectedGil = (ulong)gil + total;
        snapshot = new ShopMenuSnapshot(
            5,
            JoinClauses(
                $"Sell {item.Name}, quantity {quantity}",
                $"Total {total} gil",
                $"After sale {projectedGil} gil",
                $"You have {gil} gil",
                $"Owned {item.OwnedQuantity}",
                $"Equipped {item.EquippedQuantity}",
                item.Description),
            $"shop:sell-quantity:{item.Key}:quantity:{quantity}:gil:{gil}:total:{total}");
        return true;
    }

    private bool TryReadSellMateriaList(out ShopMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!memory.TryReadInt32((uint)AddressSellMateriaCursor, out var cursor) ||
            !memory.TryReadInt32((uint)AddressSellMateriaScroll, out var scroll) ||
            cursor is < 0 or >= 10 ||
            scroll is < 0 or >= MateriaInventorySlotCount)
        {
            return false;
        }

        var index = cursor + scroll;
        if (index is < 0 or >= MateriaInventorySlotCount ||
            !memory.TryReadUInt32(
                checked((uint)(MateriaMenuSelectionReader.AddressMateriaInventory +
                    (index * sizeof(uint)))),
                out var rawMateria) ||
            rawMateria == uint.MaxValue)
        {
            return false;
        }

        var materiaId = (int)(rawMateria & 0xff);
        if (materiaId == 0xff ||
            !TryResolveMateria(materiaId, out var name, out var description) ||
            !TryReadMateriaSalePrice(rawMateria, out var price) ||
            !TryReadMateriaInventorySummary(
                materiaId,
                out var ownedQuantity,
                out _) ||
            !TryReadEquippedDetails(
                ShopItemKind.Materia,
                materiaId,
                includeComparison: false,
                out var equippedQuantity,
                out _) ||
            !memory.TryReadUInt32((uint)AddressGil, out var gil))
        {
            return false;
        }

        var ap = rawMateria >> 8;
        var apText = ap == 0x00FF_FFFF ? "Mastered" : $"AP {ap}";
        var speech = JoinClauses(
            $"Sell materia, {name}",
            $"Price {price} gil",
            apText,
            $"You have {gil} gil",
            $"Owned {ownedQuantity}",
            $"Equipped {equippedQuantity}",
            description);
        snapshot = new ShopMenuSnapshot(
            3,
            speech,
            $"shop:sell-materia:index:{index}:raw:{rawMateria:X8}:price:{price}:" +
            $"gil:{gil}:owned:{ownedQuantity}:equipped:{equippedQuantity}");
        return true;
    }

    private bool TryReadSelectedBuyItem(out ShopItem item)
    {
        item = default;
        if (!memory.TryReadInt32((uint)AddressShopIndex, out var shopIndex) ||
            shopIndex is < 0 or >= ShopCount ||
            !memory.TryReadInt32((uint)AddressBuyListCursor, out var cursor) ||
            !memory.TryReadInt32((uint)AddressBuyListScroll, out var scroll) ||
            cursor is < 0 or >= ShopStockCount ||
            scroll is < 0 or >= ShopStockCount)
        {
            return false;
        }

        var definitionAddress = checked(AddressShopDefinitions + (shopIndex * ShopDefinitionSize));
        if (!memory.TryReadUInt16((uint)(definitionAddress + 2), out var count) ||
            count is < 1 or > ShopStockCount)
        {
            return false;
        }

        var index = cursor + scroll;
        if (index < 0 || index >= count)
        {
            return false;
        }

        var recordAddress = checked(definitionAddress + 4 + (index * 8));
        if (!memory.TryReadInt16((uint)recordAddress, out var type) ||
            !memory.TryReadUInt32((uint)(recordAddress + 4), out var rawId))
        {
            return false;
        }

        var sourceKey = $"{shopIndex}:{index}";
        if (type == 0)
        {
            if (rawId >= 320 ||
                !memory.TryReadUInt32(
                    checked((uint)(AddressPriceTable + ((int)rawId * sizeof(uint)))),
                    out var price))
            {
                return false;
            }

            return TryBuildObjectItem(
                (int)rawId,
                rawId,
                price,
                ownedQuantityOverride: null,
                includeComparison: true,
                sourceKey,
                out item);
        }

        var materiaId = (int)(rawId & 0xff);
        if (type != 1 ||
            materiaId == 0xff ||
            !memory.TryReadUInt32(
                checked((uint)(AddressPriceTable + 0x600 + (materiaId * sizeof(uint)))),
                out var materiaPrice))
        {
            return false;
        }

        return TryBuildMateriaItem(
            materiaId,
            rawId,
            materiaPrice,
            sourceKey,
            out item);
    }

    private bool TryReadSelectedInventoryItem(out ShopItem item)
    {
        item = default;
        if (!memory.TryReadInt32((uint)AddressSellItemColumn, out var column) ||
            !memory.TryReadInt32((uint)AddressSellItemRow, out var row) ||
            !memory.TryReadInt32((uint)AddressSellItemScroll, out var scroll) ||
            column is < 0 or > 1 ||
            row is < 0 or >= 10 ||
            scroll is < 0 or >= InventoryItemReader.SlotCount / 2)
        {
            return false;
        }

        var slot = column + (row * 2) + (scroll * 2);
        if (slot is < 0 or >= InventoryItemReader.SlotCount ||
            !memory.TryReadUInt16(
                checked((uint)(InventoryItemReader.AddressSavemap +
                    InventoryItemReader.ItemsOffset +
                    (slot * sizeof(ushort)))),
                out var raw) ||
            raw == ushort.MaxValue)
        {
            return false;
        }

        var objectId = raw & 0x1ff;
        var quantity = raw >> 9;
        if (quantity < 1 ||
            !memory.TryReadUInt32(
                checked((uint)(AddressPriceTable + (objectId * sizeof(uint)))),
                out var price))
        {
            return false;
        }

        return TryBuildObjectItem(
            objectId,
            raw,
            price >> 1,
            quantity,
            includeComparison: false,
            $"slot:{slot}",
            out item);
    }

    private bool TryBuildObjectItem(
        int objectId,
        uint rawValue,
        uint unitPrice,
        int? ownedQuantityOverride,
        bool includeComparison,
        string sourceKey,
        out ShopItem item)
    {
        item = default;
        if (!TryClassifyObject(objectId, out var kind) ||
            !TryResolveInventoryObject(objectId, out var name, out var description))
        {
            return false;
        }

        int ownedQuantity;
        if (ownedQuantityOverride is { } fixedOwnedQuantity)
        {
            ownedQuantity = fixedOwnedQuantity;
        }
        else if (!TryReadObjectOwnedQuantity(objectId, out ownedQuantity))
        {
            return false;
        }

        if (!TryReadEquippedDetails(
                kind,
                objectId,
                includeComparison,
                out var equippedQuantity,
                out var visibleComparison))
        {
            return false;
        }

        var canCarryAnother = ownedQuantity + equippedQuantity < 99;
        item = new ShopItem(
            kind,
            objectId,
            rawValue,
            name,
            description,
            unitPrice,
            ownedQuantity,
            equippedQuantity,
            visibleComparison,
            canCarryAnother,
            $"{sourceKey}:object:{objectId}:raw:{rawValue:X8}:owned:{ownedQuantity}:" +
            $"equipped:{equippedQuantity}:comparison:{visibleComparison}");
        return true;
    }

    private bool TryBuildMateriaItem(
        int materiaId,
        uint rawValue,
        uint unitPrice,
        string sourceKey,
        out ShopItem item)
    {
        item = default;
        if (!TryResolveMateria(materiaId, out var name, out var description) ||
            !TryReadMateriaInventorySummary(
                materiaId,
                out var ownedQuantity,
                out var hasFreeSlot) ||
            !TryReadEquippedDetails(
                ShopItemKind.Materia,
                materiaId,
                includeComparison: false,
                out var equippedQuantity,
                out _))
        {
            return false;
        }

        item = new ShopItem(
            ShopItemKind.Materia,
            materiaId,
            rawValue,
            name,
            description,
            unitPrice,
            ownedQuantity,
            equippedQuantity,
            string.Empty,
            hasFreeSlot,
            $"{sourceKey}:materia:{materiaId}:raw:{rawValue:X8}:owned:{ownedQuantity}:" +
            $"equipped:{equippedQuantity}:free:{hasFreeSlot}");
        return true;
    }

    private bool TryReadObjectOwnedQuantity(int objectId, out int quantity)
    {
        quantity = 0;
        var inventory = new byte[InventoryItemReader.SlotCount * sizeof(ushort)];
        var address = checked((uint)(
            InventoryItemReader.AddressSavemap +
            InventoryItemReader.ItemsOffset));
        if (!memory.TryRead(address, inventory))
        {
            return false;
        }

        for (var slot = 0; slot < InventoryItemReader.SlotCount; slot++)
        {
            var raw = BinaryPrimitives.ReadUInt16LittleEndian(
                inventory.AsSpan(slot * sizeof(ushort), sizeof(ushort)));
            if (raw == ushort.MaxValue || (raw & 0x1ff) != objectId)
            {
                continue;
            }

            quantity = raw >> 9;
            return quantity > 0;
        }

        return true;
    }

    private bool TryReadMateriaInventorySummary(
        int materiaId,
        out int ownedQuantity,
        out bool hasFreeSlot)
    {
        ownedQuantity = 0;
        hasFreeSlot = false;
        var inventory = new byte[MateriaInventorySlotCount * sizeof(uint)];
        if (!memory.TryRead(
                (uint)MateriaMenuSelectionReader.AddressMateriaInventory,
                inventory))
        {
            return false;
        }

        for (var slot = 0; slot < MateriaInventorySlotCount; slot++)
        {
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(
                inventory.AsSpan(slot * sizeof(uint), sizeof(uint)));
            if (raw == uint.MaxValue)
            {
                hasFreeSlot = true;
                continue;
            }

            if ((raw & 0xff) == materiaId)
            {
                ownedQuantity++;
            }
        }

        return true;
    }

    private bool TryReadEquippedDetails(
        ShopItemKind kind,
        int nativeId,
        bool includeComparison,
        out int equippedQuantity,
        out string visibleComparison)
    {
        equippedQuantity = 0;
        visibleComparison = string.Empty;
        if (kind == ShopItemKind.Item)
        {
            return true;
        }

        if (!memory.TryReadUInt16(
                (uint)AddressRecruitedCharacterMask,
                out var recruitedMask))
        {
            return false;
        }

        ushort compatibilityMask = 0;
        int candidateAttack = 0;
        int candidateDefense = 0;
        string candidateStat = string.Empty;
        if (includeComparison && kind == ShopItemKind.Weapon)
        {
            var weaponId = nativeId - 128;
            if (weaponId is < 0 or >= 128 ||
                !memory.TryReadByte(
                    checked((uint)(EquipmentStatReader.AddressWeaponAttack +
                        (weaponId * EquipmentStatReader.WeaponRecordSize))),
                    out var attack) ||
                !memory.TryReadUInt16(
                    checked((uint)(AddressWeaponEquipMask +
                        (weaponId * EquipmentStatReader.WeaponRecordSize))),
                    out compatibilityMask))
            {
                return false;
            }

            candidateAttack = attack;
            candidateStat = $"Attack {candidateAttack}";
        }
        else if (includeComparison && kind == ShopItemKind.Armor)
        {
            var armorId = nativeId - 256;
            if (armorId is < 0 or >= 32 ||
                !memory.TryReadByte(
                    checked((uint)(EquipmentStatReader.AddressArmorDefense +
                        (armorId * EquipmentStatReader.ArmorRecordSize))),
                    out var defense) ||
                !memory.TryReadUInt16(
                    checked((uint)(AddressArmorEquipMask +
                        (armorId * EquipmentStatReader.ArmorRecordSize))),
                    out compatibilityMask))
            {
                return false;
            }

            candidateDefense = defense;
            candidateStat = $"Defense {candidateDefense}";
        }

        var comparisons = new List<string>();
        for (var characterId = 0; characterId < CharacterCount; characterId++)
        {
            var bit = 1 << characterId;
            if ((recruitedMask & bit) == 0)
            {
                continue;
            }

            var character = new byte[SavemapPartyReader.CharacterSize];
            var characterAddress = checked((uint)(
                SavemapPartyReader.AddressSavemap +
                SavemapPartyReader.CharactersOffset +
                (characterId * SavemapPartyReader.CharacterSize)));
            if (!memory.TryRead(characterAddress, character))
            {
                return false;
            }

            switch (kind)
            {
                case ShopItemKind.Weapon:
                    if (character[SavemapPartyReader.EquippedWeaponOffset] + 128 == nativeId)
                    {
                        equippedQuantity++;
                    }
                    break;
                case ShopItemKind.Armor:
                    if (character[SavemapPartyReader.EquippedArmorOffset] + 256 == nativeId)
                    {
                        equippedQuantity++;
                    }
                    break;
                case ShopItemKind.Accessory:
                    var accessoryId = character[SavemapPartyReader.EquippedAccessoryOffset];
                    if (accessoryId != 0xff && accessoryId + 288 == nativeId)
                    {
                        equippedQuantity++;
                    }
                    break;
                case ShopItemKind.Materia:
                    equippedQuantity += CountEquippedMateria(character, nativeId);
                    break;
            }

            if (!includeComparison ||
                kind is not (ShopItemKind.Weapon or ShopItemKind.Armor) ||
                (compatibilityMask & bit) == 0 ||
                !TryDecodeCharacterName(character, out var characterName) ||
                !TryReadCurrentAttackAndDefense(
                    character,
                    out var currentAttack,
                    out var currentDefense))
            {
                continue;
            }

            var nextAttack = kind == ShopItemKind.Weapon
                ? candidateAttack
                : currentAttack;
            var nextDefense = kind == ShopItemKind.Armor
                ? candidateDefense
                : currentDefense;
            comparisons.Add(
                $"{characterName} attack {currentAttack} to {nextAttack}");
            comparisons.Add(
                $"{characterName} defense {currentDefense} to {nextDefense}");
        }

        if (!memory.TryReadUInt16(
                (uint)AddressRecruitedCharacterMask,
                out var maskBookend) ||
            maskBookend != recruitedMask)
        {
            return false;
        }

        visibleComparison = JoinClauses(
            new[] { candidateStat }.Concat(comparisons).ToArray());
        return true;
    }

    private bool TryReadCurrentAttackAndDefense(
        ReadOnlySpan<byte> character,
        out int attack,
        out int defense)
    {
        attack = default;
        defense = default;
        var weaponId = character[SavemapPartyReader.EquippedWeaponOffset];
        var armorId = character[SavemapPartyReader.EquippedArmorOffset];
        if (weaponId >= 128 ||
            armorId >= 32 ||
            !memory.TryReadByte(
                checked((uint)(EquipmentStatReader.AddressWeaponAttack +
                    (weaponId * EquipmentStatReader.WeaponRecordSize))),
                out var attackValue) ||
            !memory.TryReadByte(
                checked((uint)(EquipmentStatReader.AddressArmorDefense +
                    (armorId * EquipmentStatReader.ArmorRecordSize))),
                out var defenseValue))
        {
            return false;
        }

        attack = attackValue;
        defense = defenseValue;
        return true;
    }

    private static int CountEquippedMateria(
        ReadOnlySpan<byte> character,
        int materiaId)
    {
        var count = 0;
        count += CountMateriaSlots(
            character.Slice(
                CharacterWeaponMateriaOffset,
                CharacterMateriaSlotCount * sizeof(uint)),
            materiaId);
        count += CountMateriaSlots(
            character.Slice(
                CharacterArmorMateriaOffset,
                CharacterMateriaSlotCount * sizeof(uint)),
            materiaId);
        return count;
    }

    private static int CountMateriaSlots(
        ReadOnlySpan<byte> slots,
        int materiaId)
    {
        var count = 0;
        for (var slot = 0; slot < CharacterMateriaSlotCount; slot++)
        {
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(
                slots.Slice(slot * sizeof(uint), sizeof(uint)));
            if (raw != uint.MaxValue && (raw & 0xff) == materiaId)
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryDecodeCharacterName(
        ReadOnlySpan<byte> character,
        out string name)
    {
        var nameBytes = character.Slice(
            SavemapPartyReader.CharacterNameOffset,
            12);
        var terminator = nameBytes.IndexOf((byte)0xff);
        name = terminator >= 0
            ? Ff7EncodedTextDecoder.DecodeTerminated(nameBytes[..(terminator + 1)])
            : Ff7EncodedTextDecoder.Decode(nameBytes);
        name = name.Trim();
        return name.Length > 0;
    }

    private bool TryReadMateriaSalePrice(uint rawMateria, out uint price)
    {
        price = default;
        var materiaId = (int)(rawMateria & 0xff);
        if (!memory.TryReadUInt32(
                checked((uint)(AddressPriceTable + 0x600 + (materiaId * sizeof(uint)))),
                out var basePrice))
        {
            return false;
        }

        var ap = rawMateria >> 8;
        var candidate = basePrice == 1
            ? 1UL
            : ap == 0x00FF_FFFF
                ? (ulong)basePrice * 70
                : ap;
        if (candidate > uint.MaxValue)
        {
            return false;
        }

        price = (uint)candidate;
        return true;
    }

    private bool TryResolveInventoryObject(
        int objectId,
        out string name,
        out string description)
    {
        name = string.Empty;
        description = string.Empty;
        try
        {
            var resolvedName = resolveInventoryObjectName(objectId);
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                return false;
            }

            name = resolvedName.Trim();
            description = resolveInventoryObjectDescription(objectId)?.Trim() ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryResolveMateria(
        int materiaId,
        out string name,
        out string description)
    {
        name = string.Empty;
        description = string.Empty;
        try
        {
            var resolvedName = resolveMateriaName(materiaId);
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                return false;
            }

            name = resolvedName.Trim();
            description = resolveMateriaDescription(materiaId)?.Trim() ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryClassifyObject(int objectId, out ShopItemKind kind)
    {
        kind = objectId switch
        {
            >= 0 and < 128 => ShopItemKind.Item,
            >= 128 and < 256 => ShopItemKind.Weapon,
            >= 256 and < 288 => ShopItemKind.Armor,
            >= 288 and < 320 => ShopItemKind.Accessory,
            _ => default
        };
        return objectId is >= 0 and < 320;
    }

    private static string GetBuyUnavailableReason(ShopItem item, uint gil)
    {
        var reasons = new List<string>(2);
        if (gil < item.UnitPrice)
        {
            reasons.Add("Cannot afford");
        }

        if (!item.CanCarryAnother)
        {
            reasons.Add(
                item.Kind == ShopItemKind.Materia
                    ? "Materia inventory is full"
                    : "Cannot carry another");
        }

        return JoinClauses(reasons.ToArray());
    }

    private static string JoinClauses(params string?[] clauses) =>
        string.Join(
            ". ",
            clauses
                .Where(clause => !string.IsNullOrWhiteSpace(clause))
                .Select(clause => clause!.Trim().TrimEnd('.')));

    private readonly record struct ShopEnvelope(
        byte Module,
        int Active,
        int State)
    {
        internal bool IsShop =>
            Module == ShopModule &&
            Active == 1 &&
            State is >= 0 and < ShopStateCount;
    }

    private enum ShopItemKind
    {
        Item,
        Weapon,
        Armor,
        Accessory,
        Materia
    }

    private readonly record struct ShopItem(
        ShopItemKind Kind,
        int NativeId,
        uint RawValue,
        string Name,
        string Description,
        uint UnitPrice,
        int OwnedQuantity,
        int EquippedQuantity,
        string VisibleComparison,
        bool CanCarryAnother,
        string Key);
}

public sealed class ShopMenuSpeechTracker
{
    private string? lastKey;

    public string? Poll(ShopMenuStateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!reader.TryReadOwnership(out var ownsShop))
        {
            return null;
        }

        if (!ownsShop)
        {
            Reset();
            return null;
        }

        if (!reader.TryRead(out var snapshot) ||
            string.Equals(lastKey, snapshot.Key, StringComparison.Ordinal))
        {
            return null;
        }

        lastKey = snapshot.Key;
        return snapshot.Speech;
    }

    public void Reset() => lastKey = null;
}
