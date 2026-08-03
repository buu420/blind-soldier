using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class Sector7MenuRegressionTests
{
    private const int WeaponMateriaSlots = 0x00DBE74C;
    private const int WeaponGrowth = 0x00DBE736;
    private const int ArmorMateriaSlots = 0x00DBCCE9;
    private const int ArmorGrowth = 0x00DBCCF1;

    internal static void Run()
    {
        EquipmentListUsesOneCheckedNativeSelection();
        EquipmentListReadsWeaponSpecificComparisonDetails();
        EquipmentListReadsArmorSpecificComparisonDetails();
        EquipmentListReadsAccessoryEffectWithoutUnrelatedStats();
        CurrentEquipmentReadsOnlyItsNativeCategoryDetails();
        MateriaReadsVisibleApLevelAndEquipEffects();
        MateriaUsesTheSelectedCharacterRecord();
        MateriaDistinguishesEmptySocketsFromMissingSockets();
        ShopOwnershipUsesNativeModuleFive();
        ShopReadsNativeCommandsAndEveryStockType();
        ShopReadsSellListsAndQuantityPanels();
    }

    private static void EquipmentListUsesOneCheckedNativeSelection()
    {
        Require(
            MenuWidgetCatalog.TryResolve(
                EquipmentMenuSelectionReader.AddressEquipmentListWidget,
                out var descriptor),
            "Equip list selector should be cataloged.");
        Equal(MenuWidgetKind.EquipmentList, descriptor.Kind, "Equip list widget kind");

        var coordinator = new ActiveMenuFrameSpeechCoordinator();
        coordinator.ObserveDraw(new MenuTextRenderEntry("Wpn.", 229, 17, 7, 0));
        coordinator.ObserveDraw(new MenuTextRenderEntry("Assault Gun", 427, 193, 7, 0));
        coordinator.ObserveCursor(new MenuCursorDrawObservation("A", 5, 207, 17, 0));
        coordinator.ObserveCursor(new MenuCursorDrawObservation("A", 5, 385, 197, 0));
        var native = new NativeMenuSelection(
            "Assault Gun",
            "Long range weapon. Attack 18, up from 16.",
            "equip-list:0:1");
        coordinator.CompleteFrame(
            new ActiveMenuWidgetSnapshot(
                EquipmentMenuSelectionReader.AddressEquipmentListWidget,
                "Equip list",
                MenuWidgetKind.EquipmentList,
                0,
                0,
                1,
                8,
                0,
                0,
                0,
                NativeSelection: native),
            DateTime.UtcNow);

        var speech = coordinator.Poll();
        Contains(speech, "Assault Gun", "Equip list native item");
        Require(
            !speech!.Contains("Wpn.", StringComparison.Ordinal),
            "Equip list must not alternate to its parent Wpn. cursor.");
    }

    private static void EquipmentListReadsWeaponSpecificComparisonDetails()
    {
        var memory = CreateEquipmentMemory();
        var reader = new EquipmentMenuSelectionReader(
            memory,
            id => id == 1 ? "Assault Gun" : "Buster Sword",
            id => $"Armor {id}",
            id => $"Accessory {id}",
            id => id == 129 ? "Long range weapon" : null);

        Require(reader.TryRead(out var selection), "Native Equip list selection should read.");
        Equal("Assault Gun", selection.Text, "Native Equip list item");
        Contains(selection.Description, "Long range weapon", "Equip help text");
        Contains(selection.Description, "Attack 18, up from 16", "Equip Attack comparison");
        Contains(
            selection.Description,
            "Attack percentage 98 percent, up from 96 percent",
            "Equip hit comparison");
        Contains(
            selection.Description,
            "Materia slots 3, one linked pair, 1 unlinked",
            "Equip weapon Materia slots");
        Contains(selection.Description, "Growth Double", "Equip weapon Materia growth");
        DoesNotContain(selection.Description, "Defense", "weapon speech");
    }

    private static void EquipmentListReadsArmorSpecificComparisonDetails()
    {
        var memory = CreateEquipmentMemory();
        memory.WriteInt32(EquipmentMenuSelectionReader.AddressEquipmentCategory, 1);
        memory.WriteByte(EquipmentMenuSelectionReader.AddressEquipmentCandidates, 1);
        memory.WriteByte(
            EquipmentStatReader.AddressArmorDefense + EquipmentStatReader.ArmorRecordSize,
            12);
        memory.WriteByte(
            EquipmentStatReader.AddressArmorMagicDefense + EquipmentStatReader.ArmorRecordSize,
            4);
        memory.WriteByte(
            EquipmentStatReader.AddressArmorDefensePercent + EquipmentStatReader.ArmorRecordSize,
            2);
        memory.WriteByte(
            EquipmentStatReader.AddressArmorMagicDefensePercent + EquipmentStatReader.ArmorRecordSize,
            1);
        memory.WriteByte(ArmorMateriaSlots + EquipmentStatReader.ArmorRecordSize, 5);
        memory.WriteByte(ArmorMateriaSlots + EquipmentStatReader.ArmorRecordSize + 1, 5);
        memory.WriteByte(ArmorGrowth + EquipmentStatReader.ArmorRecordSize, 1);

        var reader = new EquipmentMenuSelectionReader(
            memory,
            id => $"Weapon {id}",
            id => id == 1 ? "Iron Bangle" : "Bronze Bangle",
            id => $"Accessory {id}",
            id => id == 257 ? "Stronger armor" : null);

        Require(reader.TryRead(out var selection), "Native armor list selection should read.");
        Equal("Iron Bangle", selection.Text, "Native Equip armor item");
        Contains(selection.Description, "Stronger armor", "Armor help text");
        Contains(selection.Description, "Defense 12, up from 8", "Armor Defense comparison");
        Contains(
            selection.Description,
            "Magic defense 4, up from 2",
            "Armor Magic Defense comparison");
        Contains(
            selection.Description,
            "Materia slots 2, 2 unlinked",
            "Armor Materia slots");
        Contains(selection.Description, "Growth Normal", "Armor Materia growth");
        DoesNotContain(selection.Description, "Attack", "armor speech");
    }

    private static void EquipmentListReadsAccessoryEffectWithoutUnrelatedStats()
    {
        var memory = CreateEquipmentMemory();
        memory.WriteInt32(EquipmentMenuSelectionReader.AddressEquipmentCategory, 2);
        memory.WriteByte(EquipmentMenuSelectionReader.AddressEquipmentCandidates, 0);

        var reader = new EquipmentMenuSelectionReader(
            memory,
            id => $"Weapon {id}",
            id => $"Armor {id}",
            id => id == 0 ? "Power Wrist" : null,
            id => id == 288 ? "Strength +10" : null);

        Require(reader.TryRead(out var selection), "Native accessory list selection should read.");
        Equal("Power Wrist", selection.Text, "Native Equip accessory item");
        Equal("Strength +10", selection.Description, "Accessory native effect");
        DoesNotContain(selection.Description, "Attack", "accessory speech");
        DoesNotContain(selection.Description, "Defense", "accessory speech");
    }

    private static void CurrentEquipmentReadsOnlyItsNativeCategoryDetails()
    {
        var memory = CreateEquipmentMemory();
        var reader = new SavemapPartyReader(
            memory,
            id => id == 0 ? "Buster Sword" : null,
            id => id == 0 ? "Bronze Bangle" : null,
            id => id == 0 ? "Power Wrist" : null,
            resolveInventoryObjectDescription: objectId => objectId switch
            {
                128 => "Initial equipment",
                256 => "Initial armor",
                288 => "Strength +10",
                _ => null
            });

        Require(reader.TryReadEquipment(0, 0, out var weapon), "Current weapon should read.");
        Contains(weapon.Description, "Attack 16", "Current weapon Attack");
        Contains(weapon.Description, "Attack percentage 96 percent", "Current weapon hit");
        Contains(
            weapon.Description,
            "Materia slots 2, one linked pair",
            "Current weapon Materia slots");
        Contains(weapon.Description, "Growth Normal", "Current weapon growth");
        DoesNotContain(weapon.Description, "Defense", "current weapon speech");

        Require(reader.TryReadEquipment(0, 1, out var armor), "Current armor should read.");
        Contains(armor.Description, "Defense 8", "Current armor Defense");
        Contains(armor.Description, "Magic defense 2", "Current armor Magic Defense");
        Contains(
            armor.Description,
            "Materia slots 1, 1 unlinked",
            "Current armor Materia slots");
        Contains(armor.Description, "Growth Normal", "Current armor growth");
        DoesNotContain(armor.Description, "Attack", "current armor speech");

        Require(reader.TryReadEquipment(0, 2, out var none), "Empty accessory should read.");
        Equal<string?>(null, none.Description, "Empty accessory has no unrelated stat panel");

        var character = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        memory.WriteByte(character + SavemapPartyReader.EquippedAccessoryOffset, 0);
        Require(reader.TryReadEquipment(0, 2, out var accessory), "Current accessory should read.");
        Equal("Strength +10", accessory.Description, "Current accessory native effect");
    }

    private static void MateriaReadsVisibleApLevelAndEquipEffects()
    {
        var memory = new Memory();
        const uint rawMateria = (100u << 8) | 7u;
        const uint characterData = 0x01010000;
        const uint detail = 0x01000000;
        memory.WriteInt32(
            MateriaMenuSelectionReader.AddressMenuMode,
            MateriaMenuSelectionReader.EquippedSlotMode);
        memory.WriteInt32(MateriaMenuSelectionReader.AddressSelectedPartySlot, 0);
        memory.WriteInt32(MateriaMenuSelectionReader.AddressMateriaSlotWidget, 0);
        memory.WriteInt32(MateriaMenuSelectionReader.AddressMateriaSlotWidget + 4, 0);
        memory.WriteUInt32(MateriaMenuSelectionReader.AddressMenuCharacterData, characterData);
        memory.WriteByte(characterData + SavemapPartyReader.EquippedWeaponOffset, 0);
        memory.WriteByte(characterData + SavemapPartyReader.EquippedArmorOffset, 0);
        memory.WriteUInt32(characterData + 0x40, rawMateria);
        memory.WriteByte(WeaponMateriaSlots, 6);
        memory.WriteUInt32(MateriaMenuSelectionReader.AddressSelectedMateriaId, 7);
        memory.WriteUInt32(MateriaMenuSelectionReader.AddressDetailBufferPointer, detail);
        memory.WriteByte(detail, 1);
        memory.WriteByte(detail + 1, 4);
        memory.WriteUInt32(detail + 4, 900);
        for (var index = 0; index < 8; index++)
        {
            memory.WriteInt16(detail + 10 + (index * 2), 0);
        }
        memory.WriteInt16(detail + 10, -1);
        memory.WriteInt16(detail + 14, 1);
        memory.WriteInt16(detail + 22, -2);
        memory.WriteInt16(detail + 24, 2);

        var reader = new MateriaMenuSelectionReader(
            memory,
            id => id == 7 ? "Lightning" : null,
            id => id == 7 ? "Equips Lightning magic" : null);
        Require(
            reader.TryRead(MenuWidgetKind.MateriaSlot, out var selection),
            "Native Materia slot selection should read.");
        Equal("Weapon materia slot 1, Lightning", selection.Text, "Materia slot label");
        Contains(selection.Description, "AP 100", "Materia AP");
        Contains(selection.Description, "Level 1 of 4", "Materia level");
        Contains(selection.Description, "To next level 900", "Materia next level");
        Contains(selection.Description, "Strength minus 1", "Materia Strength effect");
        Contains(selection.Description, "Magic plus 1", "Materia Magic effect");
        Contains(selection.Description, "Max HP minus 2 percent", "Materia Max HP effect");
        Contains(selection.Description, "Max MP plus 2 percent", "Materia Max MP effect");
    }

    private static void MateriaDistinguishesEmptySocketsFromMissingSockets()
    {
        var memory = new Memory();
        const uint characterData = 0x01010000;
        memory.WriteInt32(
            MateriaMenuSelectionReader.AddressMenuMode,
            MateriaMenuSelectionReader.EquippedSlotMode);
        memory.WriteInt32(MateriaMenuSelectionReader.AddressSelectedPartySlot, 0);
        memory.WriteInt32(MateriaMenuSelectionReader.AddressMateriaSlotWidget + 4, 0);
        memory.WriteUInt32(MateriaMenuSelectionReader.AddressMenuCharacterData, characterData);
        memory.WriteByte(characterData + SavemapPartyReader.EquippedWeaponOffset, 0);
        memory.WriteByte(characterData + SavemapPartyReader.EquippedArmorOffset, 0);
        memory.WriteUInt32(characterData + 0x44, uint.MaxValue);
        memory.WriteUInt32(characterData + 0x48, 0);
        memory.WriteByte(WeaponMateriaSlots + 1, 5);
        memory.WriteByte(WeaponMateriaSlots + 2, 0);

        memory.WriteInt32(MateriaMenuSelectionReader.AddressMateriaSlotWidget, 1);
        var reader = new MateriaMenuSelectionReader(memory);
        Require(
            reader.TryRead(MenuWidgetKind.MateriaSlot, out var empty),
            "Usable empty Materia socket should read.");
        Equal("Weapon materia slot 2, empty", empty.Text, "empty Materia socket");

        memory.WriteInt32(MateriaMenuSelectionReader.AddressMateriaSlotWidget, 2);
        Require(
            reader.TryRead(MenuWidgetKind.MateriaSlot, out var missing),
            "Missing Materia socket should read.");
        Equal(
            "Weapon materia position 3, no slot",
            missing.Text,
            "missing Materia socket");
    }

    private static void MateriaUsesTheSelectedCharacterRecord()
    {
        var memory = new Memory();
        const uint characterData = 0x01010000;
        const uint cloudMateria = 7;
        const uint barretMateria = 8;
        var barretRecord =
            characterData + MateriaMenuSelectionReader.MenuCharacterDataSize;

        memory.WriteInt32(
            MateriaMenuSelectionReader.AddressMenuMode,
            MateriaMenuSelectionReader.EquippedSlotMode);
        memory.WriteInt32(MateriaMenuSelectionReader.AddressSelectedPartySlot, 1);
        memory.WriteInt32(MateriaMenuSelectionReader.AddressMateriaSlotWidget, 0);
        memory.WriteInt32(MateriaMenuSelectionReader.AddressMateriaSlotWidget + 4, 0);
        memory.WriteUInt32(MateriaMenuSelectionReader.AddressMenuCharacterData, characterData);

        memory.WriteByte(characterData + SavemapPartyReader.EquippedWeaponOffset, 0);
        memory.WriteUInt32(characterData + 0x40, cloudMateria);
        memory.WriteByte(barretRecord + SavemapPartyReader.EquippedWeaponOffset, 1);
        memory.WriteUInt32(barretRecord + 0x40, barretMateria);
        memory.WriteByte(WeaponMateriaSlots, 6);
        memory.WriteByte(
            WeaponMateriaSlots + EquipmentStatReader.WeaponRecordSize,
            6);

        var reader = new MateriaMenuSelectionReader(
            memory,
            id => id switch
            {
                7 => "Lightning",
                8 => "Restore",
                _ => null
            });

        Require(
            reader.TryRead(MenuWidgetKind.MateriaSlot, out var selection),
            "Selected character Materia should read.");
        Equal(
            "Weapon materia slot 1, Restore",
            selection.Text,
            "selected character Materia");
    }

    private static void ShopOwnershipUsesNativeModuleFive()
    {
        var memory = CreateShopMemory();
        memory.WriteByte(FieldPositionReader.AddressCurrentModule, 5);
        memory.WriteInt32(ShopMenuStateReader.AddressActiveState, 1);
        memory.WriteInt32(ShopMenuStateReader.AddressMenuState, 0);
        memory.WriteInt32(ShopMenuStateReader.AddressTopCommandWidget, 0);

        var reader = CreateShopReader(memory);
        var tracker = new ShopMenuSpeechTracker();

        Require(
            reader.TryReadOwnership(out var ownsShop) && ownsShop,
            "Native module 5 and active shop state should own shop speech.");
        Equal("Buy", tracker.Poll(reader), "Shop Buy command");
        Equal<string?>(null, tracker.Poll(reader), "Shop command repeat suppression");
        tracker.Reset();
        Equal("Buy", tracker.Poll(reader), "Shop tracker reset");

        memory.WriteByte(FieldPositionReader.AddressCurrentModule, 19);
        Require(
            reader.TryReadOwnership(out ownsShop) && !ownsShop,
            "Quit-transition module 19 must not own shop speech.");
    }

    private static void ShopReadsNativeCommandsAndEveryStockType()
    {
        var memory = CreateShopMemory();
        var reader = CreateShopReader(memory);

        memory.WriteInt32(ShopMenuStateReader.AddressMenuState, 0);
        memory.WriteInt32(ShopMenuStateReader.AddressTopCommandWidget, 0);
        Equal("Buy", ReadShopSpeech(reader), "Shop Buy command");
        memory.WriteInt32(ShopMenuStateReader.AddressTopCommandWidget, 1);
        Equal("Sell", ReadShopSpeech(reader), "Shop Sell command");
        memory.WriteInt32(ShopMenuStateReader.AddressTopCommandWidget, 2);
        Equal("Exit", ReadShopSpeech(reader), "Shop Exit command");

        memory.WriteInt32(ShopMenuStateReader.AddressMenuState, 1);
        memory.WriteInt32(ShopMenuStateReader.AddressBuyListCursor, 0);
        var speech = ReadShopSpeech(reader);
        Contains(speech, "Buy, Potion", "Shop ordinary item");
        Contains(speech, "Price 50 gil", "Shop ordinary item price");
        Contains(speech, "You have 1000 gil", "Shop current gil");
        Contains(speech, "Owned 4", "Shop ordinary item inventory count");
        Contains(speech, "Equipped 0", "Shop ordinary item equipped count");
        Contains(speech, "Restores 100 HP", "Shop ordinary item description");

        memory.WriteInt32(ShopMenuStateReader.AddressBuyListCursor, 1);
        speech = ReadShopSpeech(reader);
        Contains(speech, "Buy, Assault Gun", "Shop buy item");
        Contains(speech, "350 gil", "Shop price");
        Contains(speech, "Owned 1", "Shop weapon inventory count");
        Contains(speech, "Equipped 1", "Shop weapon equipped count");
        Contains(speech, "Attack 18", "Shop weapon stat");
        Contains(speech, "Cloud attack 16 to 18", "Shop weapon comparison");
        DoesNotContain(speech, "Attack percentage", "Shop hidden weapon stats");

        memory.WriteInt32(ShopMenuStateReader.AddressBuyListCursor, 2);
        speech = ReadShopSpeech(reader);
        Contains(speech, "Buy, Iron Bangle", "Shop armor");
        Contains(speech, "Defense 12", "Shop armor stat");
        Contains(speech, "Cloud defense 8 to 12", "Shop armor comparison");
        Contains(speech, "Equipped 1", "Shop armor equipped count");
        DoesNotContain(speech, "Magic defense", "Shop hidden armor stats");

        memory.WriteInt32(ShopMenuStateReader.AddressBuyListCursor, 3);
        speech = ReadShopSpeech(reader);
        Contains(speech, "Buy, Talisman", "Shop accessory");
        Contains(speech, "Owned 1", "Shop accessory inventory count");
        Contains(speech, "Equipped 1", "Shop accessory equipped count");
        Contains(speech, "Raises Spirit", "Shop accessory description");
        DoesNotContain(speech, "Defense", "Shop accessory hidden stats");

        memory.WriteInt32(ShopMenuStateReader.AddressBuyListCursor, 4);
        speech = ReadShopSpeech(reader);
        Contains(speech, "Buy, Lightning", "Shop materia");
        Contains(speech, "Price 600 gil", "Shop materia price");
        Contains(speech, "Owned 2", "Shop materia inventory count");
        Contains(speech, "Equipped 1", "Shop materia equipped count");
        Contains(speech, "Lightning-elemental magic", "Shop materia description");

        memory.WriteInt32(ShopMenuStateReader.AddressBuyListCursor, 0);
        memory.WriteUInt32(ShopMenuStateReader.AddressGil, 25);
        Contains(ReadShopSpeech(reader), "Cannot afford", "Shop affordability");

        memory.WriteUInt32(ShopMenuStateReader.AddressGil, 1000);
        memory.WriteUInt16(
            InventoryItemReader.AddressSavemap + InventoryItemReader.ItemsOffset,
            (ushort)(99 << 9));
        Contains(ReadShopSpeech(reader), "Cannot carry another", "Shop object cap");
    }

    private static void ShopReadsSellListsAndQuantityPanels()
    {
        var memory = CreateShopMemory();
        var reader = CreateShopReader(memory);

        memory.WriteInt32(ShopMenuStateReader.AddressMenuState, 2);
        memory.WriteInt32(ShopMenuStateReader.AddressSellItemColumn, 0);
        memory.WriteInt32(ShopMenuStateReader.AddressSellItemRow, 0);
        memory.WriteInt32(ShopMenuStateReader.AddressSellItemScroll, 0);
        var speech = ReadShopSpeech(reader);
        Contains(speech, "Sell, Potion", "Shop sell item");
        Contains(speech, "Owned 4", "Shop sell item inventory count");
        Contains(speech, "Restores 100 HP", "Shop sell item description");
        DoesNotContain(speech, "Price", "Shop sell list premature price");

        memory.WriteInt32(ShopMenuStateReader.AddressMenuState, 3);
        memory.WriteInt32(ShopMenuStateReader.AddressSellMateriaCursor, 0);
        memory.WriteInt32(ShopMenuStateReader.AddressSellMateriaScroll, 0);
        speech = ReadShopSpeech(reader);
        Contains(speech, "Sell materia, Lightning", "Shop sell materia");
        Contains(speech, "Price 1000 gil", "Shop sell materia value");
        Contains(speech, "AP 1000", "Shop sell materia AP");
        Contains(speech, "You have 1000 gil", "Shop sell materia current gil");
        Contains(speech, "Owned 2", "Shop sell materia inventory count");
        Contains(speech, "Equipped 1", "Shop sell materia equipped count");
        Contains(speech, "Lightning-elemental magic", "Shop sell materia description");

        memory.WriteInt32(ShopMenuStateReader.AddressMenuState, 4);
        memory.WriteInt32(ShopMenuStateReader.AddressBuyListCursor, 1);
        memory.WriteInt32(ShopMenuStateReader.AddressQuantity, 2);
        speech = ReadShopSpeech(reader);
        Contains(speech, "Buy Assault Gun, quantity 2", "Shop buy quantity");
        Contains(speech, "Total 700 gil", "Shop buy total");
        Contains(speech, "You have 1000 gil", "Shop buy quantity current gil");
        Contains(speech, "Owned 1", "Shop buy quantity inventory count");
        Contains(speech, "Equipped 1", "Shop buy quantity equipped count");
        Contains(speech, "Cloud attack 16 to 18", "Shop buy quantity comparison");
        Contains(speech, "Long range weapon", "Shop buy quantity description");

        memory.WriteInt32(ShopMenuStateReader.AddressMenuState, 5);
        memory.WriteInt32(ShopMenuStateReader.AddressQuantity, 2);
        speech = ReadShopSpeech(reader);
        Contains(speech, "Sell Potion, quantity 2", "Shop sell quantity");
        Contains(speech, "Total 50 gil", "Shop sell total");
        Contains(speech, "After sale 1050 gil", "Shop projected gil");
        Contains(speech, "You have 1000 gil", "Shop sell quantity current gil");
        Contains(speech, "Owned 4", "Shop sell quantity inventory count");
        Contains(speech, "Equipped 0", "Shop sell quantity equipped count");
        Contains(speech, "Restores 100 HP", "Shop sell quantity description");

        memory.WriteInt32(ShopMenuStateReader.AddressMenuState, 6);
        memory.WriteInt32(ShopMenuStateReader.AddressSellTypeWidget, 0);
        Equal("Sell items", ReadShopSpeech(reader), "Shop item category");
        memory.WriteInt32(ShopMenuStateReader.AddressSellTypeWidget, 1);
        Equal("Sell materia", ReadShopSpeech(reader), "Shop materia category");
    }

    private static ShopMenuStateReader CreateShopReader(Memory memory) =>
        new(
            memory,
            id => id switch
            {
                0 => "Potion",
                129 => "Assault Gun",
                257 => "Iron Bangle",
                288 => "Talisman",
                _ => $"Object {id}"
            },
            id => id switch
            {
                0 => "Restores 100 HP",
                129 => "Long range weapon",
                257 => "Protective armor",
                288 => "Raises Spirit",
                _ => null
            },
            id => id == 7 ? "Lightning" : $"Materia {id}",
            id => id == 7 ? "Lightning-elemental magic" : null);

    private static string ReadShopSpeech(ShopMenuStateReader reader)
    {
        Require(reader.TryRead(out var snapshot), "Native shop snapshot should read.");
        return snapshot.Speech;
    }

    private static Memory CreateShopMemory()
    {
        const int recruitedCharacterMaskAddress = 0x00DC0DDE;
        const int weaponEquipMaskAddress = 0x00DBE73E;
        const int armorEquipMaskAddress = 0x00DBCCF2;
        var memory = CreateEquipmentMemory();

        memory.WriteByte(FieldPositionReader.AddressCurrentModule, 5);
        memory.WriteInt32(ShopMenuStateReader.AddressActiveState, 1);
        memory.WriteInt32(ShopMenuStateReader.AddressMenuState, 0);
        memory.WriteInt32(ShopMenuStateReader.AddressShopIndex, 0);
        memory.WriteInt32(ShopMenuStateReader.AddressBuyListCursor, 0);
        memory.WriteInt32(ShopMenuStateReader.AddressBuyListScroll, 0);
        memory.WriteUInt32(ShopMenuStateReader.AddressGil, 1000);

        for (var slot = 0; slot < InventoryItemReader.SlotCount; slot++)
        {
            memory.WriteUInt16(
                InventoryItemReader.AddressSavemap +
                InventoryItemReader.ItemsOffset +
                (slot * sizeof(ushort)),
                ushort.MaxValue);
        }

        memory.WriteUInt16(
            InventoryItemReader.AddressSavemap + InventoryItemReader.ItemsOffset,
            (ushort)((4 << 9) | 0));
        memory.WriteUInt16(
            InventoryItemReader.AddressSavemap + InventoryItemReader.ItemsOffset + 2,
            (ushort)((1 << 9) | 129));
        memory.WriteUInt16(
            InventoryItemReader.AddressSavemap + InventoryItemReader.ItemsOffset + 4,
            (ushort)((2 << 9) | 257));
        memory.WriteUInt16(
            InventoryItemReader.AddressSavemap + InventoryItemReader.ItemsOffset + 6,
            (ushort)((1 << 9) | 288));

        for (var slot = 0; slot < 200; slot++)
        {
            memory.WriteUInt32(
                MateriaMenuSelectionReader.AddressMateriaInventory +
                (slot * sizeof(uint)),
                uint.MaxValue);
        }

        memory.WriteUInt32(
            MateriaMenuSelectionReader.AddressMateriaInventory,
            (1000u << 8) | 7u);
        memory.WriteUInt32(
            MateriaMenuSelectionReader.AddressMateriaInventory + 4,
            7);

        var definition = ShopMenuStateReader.AddressShopDefinitions;
        memory.WriteUInt16(definition + 2, 5);
        WriteShopStock(memory, definition, 0, type: 0, nativeId: 0);
        WriteShopStock(memory, definition, 1, type: 0, nativeId: 129);
        WriteShopStock(memory, definition, 2, type: 0, nativeId: 257);
        WriteShopStock(memory, definition, 3, type: 0, nativeId: 288);
        WriteShopStock(memory, definition, 4, type: 1, nativeId: 7);
        memory.WriteUInt32(ShopMenuStateReader.AddressPriceTable, 50);
        memory.WriteUInt32(ShopMenuStateReader.AddressPriceTable + (129 * 4), 350);
        memory.WriteUInt32(ShopMenuStateReader.AddressPriceTable + (257 * 4), 400);
        memory.WriteUInt32(ShopMenuStateReader.AddressPriceTable + (288 * 4), 500);
        memory.WriteUInt32(ShopMenuStateReader.AddressPriceTable + 0x600 + (7 * 4), 600);

        memory.WriteUInt16(recruitedCharacterMaskAddress, 0x0003);
        memory.WriteUInt16(
            weaponEquipMaskAddress + EquipmentStatReader.WeaponRecordSize,
            0x0003);
        memory.WriteUInt16(
            armorEquipMaskAddress + EquipmentStatReader.ArmorRecordSize,
            0x0003);

        var cloud = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        var barret = cloud + SavemapPartyReader.CharacterSize;
        for (var character = 0; character < 2; character++)
        {
            var record = cloud + (character * SavemapPartyReader.CharacterSize);
            for (var offset = 0; offset < SavemapPartyReader.CharacterSize; offset++)
            {
                memory.WriteByte(record + offset, 0);
            }
        }

        WriteCharacterName(memory, cloud, [0x23, 0x4C, 0x4F, 0x55, 0x44]);
        WriteCharacterName(memory, barret, [0x22, 0x41, 0x52, 0x52, 0x45, 0x54]);
        memory.WriteByte(cloud + SavemapPartyReader.EquippedWeaponOffset, 0);
        memory.WriteByte(cloud + SavemapPartyReader.EquippedArmorOffset, 0);
        memory.WriteByte(cloud + SavemapPartyReader.EquippedAccessoryOffset, 0);
        memory.WriteByte(barret + SavemapPartyReader.EquippedWeaponOffset, 1);
        memory.WriteByte(barret + SavemapPartyReader.EquippedArmorOffset, 1);
        memory.WriteByte(barret + SavemapPartyReader.EquippedAccessoryOffset, 0xff);

        for (var character = 0; character < 2; character++)
        {
            var record = cloud + (character * SavemapPartyReader.CharacterSize);
            for (var slot = 0; slot < 8; slot++)
            {
                memory.WriteUInt32(record + 0x40 + (slot * sizeof(uint)), uint.MaxValue);
                memory.WriteUInt32(record + 0x60 + (slot * sizeof(uint)), uint.MaxValue);
            }
        }

        memory.WriteUInt32(cloud + 0x40, 7);
        memory.WriteByte(EquipmentStatReader.AddressWeaponAttack, 16);
        memory.WriteByte(
            EquipmentStatReader.AddressWeaponAttack +
            EquipmentStatReader.WeaponRecordSize,
            18);
        memory.WriteByte(EquipmentStatReader.AddressArmorDefense, 8);
        memory.WriteByte(
            EquipmentStatReader.AddressArmorDefense +
            EquipmentStatReader.ArmorRecordSize,
            12);
        return memory;
    }

    private static void WriteShopStock(
        Memory memory,
        int definition,
        int index,
        short type,
        uint nativeId)
    {
        var record = definition + 4 + (index * 8);
        memory.WriteInt16(record, type);
        memory.WriteUInt32(record + 4, nativeId);
    }

    private static void WriteCharacterName(
        Memory memory,
        int characterBase,
        IReadOnlyList<byte> encodedName)
    {
        for (var index = 0; index < 12; index++)
        {
            memory.WriteByte(
                characterBase + SavemapPartyReader.CharacterNameOffset + index,
                index < encodedName.Count ? encodedName[index] : (byte)0xff);
        }
    }

    private static Memory CreateEquipmentMemory()
    {
        var memory = new Memory();
        memory.WriteInt32(SavemapPartyReader.AddressEquipmentMenuPartySlot, 0);
        memory.WriteByte(
            SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset,
            0);
        var character = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        for (var index = 0; index < 12; index++)
        {
            memory.WriteByte(
                character + SavemapPartyReader.CharacterNameOffset + index,
                0xff);
        }

        memory.WriteByte(character + SavemapPartyReader.CharacterNameOffset, 0x23);
        memory.WriteByte(character + SavemapPartyReader.CharacterNameOffset + 1, 0x4c);
        memory.WriteByte(character + SavemapPartyReader.CharacterNameOffset + 2, 0x4f);
        memory.WriteByte(character + SavemapPartyReader.CharacterNameOffset + 3, 0x55);
        memory.WriteByte(character + SavemapPartyReader.CharacterNameOffset + 4, 0x44);
        memory.WriteByte(character + SavemapPartyReader.EquippedWeaponOffset, 0);
        memory.WriteByte(character + SavemapPartyReader.EquippedArmorOffset, 0);
        memory.WriteByte(character + SavemapPartyReader.EquippedAccessoryOffset, 0xff);

        memory.WriteInt32(EquipmentMenuSelectionReader.AddressEquipmentListActive, 1);
        memory.WriteInt32(EquipmentMenuSelectionReader.AddressEquipmentCategory, 0);
        memory.WriteInt32(EquipmentMenuSelectionReader.AddressEquipmentListCursor, 0);
        memory.WriteInt32(EquipmentMenuSelectionReader.AddressEquipmentListScroll, 0);
        memory.WriteInt32(EquipmentMenuSelectionReader.AddressEquipmentListCount, 1);
        memory.WriteByte(EquipmentMenuSelectionReader.AddressEquipmentCandidates, 1);

        memory.WriteByte(EquipmentStatReader.AddressWeaponAttack, 16);
        memory.WriteByte(EquipmentStatReader.AddressWeaponAttackPercent, 96);
        memory.WriteByte(
            EquipmentStatReader.AddressWeaponAttack + EquipmentStatReader.WeaponRecordSize,
            18);
        memory.WriteByte(
            EquipmentStatReader.AddressWeaponAttackPercent + EquipmentStatReader.WeaponRecordSize,
            98);
        for (var index = 0; index < EquipmentStatReader.MateriaSlotCount; index++)
        {
            memory.WriteByte(WeaponMateriaSlots + index, 0);
            memory.WriteByte(
                WeaponMateriaSlots + EquipmentStatReader.WeaponRecordSize + index,
                0);
            memory.WriteByte(ArmorMateriaSlots + index, 0);
            memory.WriteByte(
                ArmorMateriaSlots + EquipmentStatReader.ArmorRecordSize + index,
                0);
        }

        memory.WriteByte(WeaponMateriaSlots, 6);
        memory.WriteByte(WeaponMateriaSlots + 1, 7);
        memory.WriteByte(WeaponGrowth, 1);
        memory.WriteByte(
            WeaponMateriaSlots + EquipmentStatReader.WeaponRecordSize,
            6);
        memory.WriteByte(
            WeaponMateriaSlots + EquipmentStatReader.WeaponRecordSize + 1,
            7);
        memory.WriteByte(
            WeaponMateriaSlots + EquipmentStatReader.WeaponRecordSize + 2,
            5);
        memory.WriteByte(
            WeaponGrowth + EquipmentStatReader.WeaponRecordSize,
            2);
        memory.WriteByte(EquipmentStatReader.AddressArmorDefense, 8);
        memory.WriteByte(EquipmentStatReader.AddressArmorDefensePercent, 0);
        memory.WriteByte(EquipmentStatReader.AddressArmorMagicDefense, 2);
        memory.WriteByte(EquipmentStatReader.AddressArmorMagicDefensePercent, 0);
        memory.WriteByte(ArmorMateriaSlots, 5);
        memory.WriteByte(ArmorGrowth, 1);
        return memory;
    }

    private static void Contains(string? actual, string expected, string label)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label}: expected '{expected}' in '{actual ?? "<null>"}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void DoesNotContain(string? actual, string unexpected, string label)
    {
        if (actual is not null && actual.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{label}: did not expect '{unexpected}' in '{actual}'.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            return true;
        }

        internal void WriteByte(long address, byte value) =>
            bytes[checked((uint)address)] = value;

        internal void WriteInt16(long address, short value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(short)];
            BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
            Write(address, buffer);
        }

        internal void WriteUInt16(long address, ushort value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            Write(address, buffer);
        }

        internal void WriteInt32(long address, int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            Write(address, buffer);
        }

        internal void WriteUInt32(long address, uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            Write(address, buffer);
        }

        private void Write(long address, ReadOnlySpan<byte> value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                bytes[checked((uint)address) + (uint)index] = value[index];
            }
        }
    }
}
