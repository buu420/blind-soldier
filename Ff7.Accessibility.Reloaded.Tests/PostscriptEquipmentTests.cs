using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class PostscriptEquipmentTests
{
    internal static void Run()
    {
        FourthEquipmentRowUsesTheSecondAccessory();
        FourthInventoryCategoryResolvesAccessoryObjects();
        LongerMateriaChainsDescribeConnections();
        StatusRejectsAccessoryChangesWhileReading();
        MateriaSlotReadsVisibleChainConnections();
        VisibleEquipBonusPanelUsesNativeTotals();
        ChainedLayoutChangesInvalidateEquipmentReadout();
    }

    private static void FourthEquipmentRowUsesTheSecondAccessory()
    {
        var memory = Party();
        var reader = new SavemapPartyReader(memory, resolveAccessoryName: id => $"Accessory {id}",
            resolveInventoryObjectDescription: id => $"Effect {id}");
        Require(!reader.TryReadEquipment(0, 3, out _), "Stock must not expose character byte 1B as equipment.");
        Accessories(memory);
        Require(reader.TryReadEquipment(0, 3, out var second), "Verified fourth equipment row must read accessory 2.");
        Require(second.Text.Contains("Accessory 2, Accessory 5") && second.Description == "Effect 293",
            "Accessory 2 must use byte 1B, its own item name and native help.");
        Require(reader.TryReadEquipment(0, 2, out var first) && first.Text.Contains("Accessory 1, Accessory 4"),
            "Verified first accessory row must remain distinct.");
        memory.Byte(Character + 0x1B, 255);
        Require(reader.TryReadEquipment(0, 3, out second) && second.Text.EndsWith("None"), "Empty second accessory.");
        memory.Byte(0x707571, 0);
        Require(!reader.TryReadEquipment(0, 3, out _), "Altered native patch must disable extra row.");
    }

    private static void FourthInventoryCategoryResolvesAccessoryObjects()
    {
        var memory = Party();
        memory.Int32(SavemapPartyReader.AddressEquipmentMenuPartySlot, 0);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentListActive, 1);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentCategory, 3);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentListCursor, 0);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentListScroll, 0);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentListCount, 1);
        memory.Byte(EquipmentMenuSelectionReader.AddressEquipmentCandidates, 7);
        var reader = new EquipmentMenuSelectionReader(memory, resolveAccessoryName: id => $"Accessory {id}",
            resolveInventoryObjectDescription: id => $"Effect {id}");
        Require(!reader.TryRead(out _), "Stock category3 remains unavailable.");
        Accessories(memory);
        Require(reader.TryRead(out var selection) && selection.Text == "Accessory 7" && selection.Description == "Effect 295",
            "Verified category3 must use accessory object ids.");
        memory.BeforeRead = (address, count) => { if (address == 0x707571 && count % 2 == 0) memory.Byte(address, 0); };
        Require(!reader.TryRead(out _), "Patch changing during selection must invalidate speech.");
    }

    private static void LongerMateriaChainsDescribeConnections()
    {
        var memory = Party();
        memory.Byte(EquipmentStatReader.AddressWeaponAttack, 16);
        memory.Byte(EquipmentStatReader.AddressWeaponAttackPercent, 96);
        memory.Byte(EquipmentStatReader.AddressWeaponGrowth, 1);
        memory.Write(EquipmentStatReader.AddressWeaponMateriaSlots, [6, 7, 7, 6, 7, 7, 6, 7]);
        var reader = new SavemapPartyReader(memory, resolveWeaponName: _ => "Enhance Sword");
        Require(!reader.TryReadEquipment(0, 0, out _), "Stock decoder must reject chained slots.");
        Linked(memory);
        Require(reader.TryReadEquipment(0, 0, out var selection), "Verified chained materia equipment must remain readable.");
        Require(selection.Description!.Contains("2 linked groups of 3 slots") && selection.Description.Contains("one linked pair"),
            "The visible 3,3,2 chain layout must be described without treating shared slots as disjoint pairs.");
        memory.Write(EquipmentStatReader.AddressWeaponMateriaSlots, [2, 3, 3, 0, 0, 0, 0, 0]);
        Require(reader.TryReadEquipment(0, 0, out selection) && selection.Description!.Contains("one linked group of 3 slots"),
            "No-growth linked chain uses the same visible connections.");
        memory.Write(EquipmentStatReader.AddressWeaponMateriaSlots, [7, 6, 7, 0, 0, 0, 0, 0]);
        Require(!reader.TryReadEquipment(0, 0, out _), "Orphaned chain end must fail closed.");
    }

    private const int Character = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;

    private static void StatusRejectsAccessoryChangesWhileReading()
    {
        var memory = Party();
        Accessories(memory);
        memory.Write(SavemapPartyReader.AddressComputedPartyData, new byte[0x10]);
        memory.Byte(Character + 1, 10); memory.Byte(Character + 14, 1);
        memory.Write(Character + 0x2C, new byte[0x18]);
        memory.Write(Character + 0x80, new byte[4]);
        memory.Byte(EquipmentStatReader.AddressWeaponAttackPercent, 96);
        memory.Byte(EquipmentStatReader.AddressArmorDefensePercent, 3);
        memory.Byte(EquipmentStatReader.AddressArmorMagicDefensePercent, 1);
        var reader = new SavemapPartyReader(memory, resolveAccessoryName: id => $"Accessory {id}");
        Require(reader.TryReadStatusSummary(0, out _), "Stable status fixture must read.");
        memory.BeforeRead = (address, count) => {
            if (address == Character + 0x1B && count % 2 == 0) memory.Byte(address, 7);
        };
        Require(!reader.TryReadStatusSummary(0, out _), "Changing second accessory must invalidate the status snapshot.");
    }

    private static void MateriaSlotReadsVisibleChainConnections()
    {
        var memory = Party(); Linked(memory);
        const int data = 0x01010000;
        memory.Int32(MateriaMenuSelectionReader.AddressMenuMode, 1);
        memory.Int32(MateriaMenuSelectionReader.AddressSelectedPartySlot, 0);
        memory.Int32(MateriaMenuSelectionReader.AddressMateriaSlotWidget, 1);
        memory.Int32(MateriaMenuSelectionReader.AddressMateriaSlotWidget + 4, 0);
        memory.Int32(MateriaMenuSelectionReader.AddressMenuCharacterData, data);
        memory.Byte(data + 0x1C, 0);
        memory.Write(data + 0x44, [7, 0, 0, 0]);
        memory.Write(EquipmentStatReader.AddressWeaponMateriaSlots, [6, 7, 7, 0, 0, 0, 0, 0]);
        var reader = new MateriaMenuSelectionReader(memory, _ => "Lightning");
        Require(reader.TryRead(MenuWidgetKind.MateriaSlot, out var selection), "Linked materia slot should read.");
        Require(selection.Description!.Contains("Linked to weapon materia slots 1 and 3"),
            "The middle of a visible triple must announce both adjacent connections.");
        memory.Write(data + 0x44, [255, 255, 255, 255]);
        Require(reader.TryRead(MenuWidgetKind.MateriaSlot, out selection) &&
            selection.Description!.Contains("Linked to weapon materia slots 1 and 3"),
            "Empty socket still exposes its visible connections.");
    }

    private static void VisibleEquipBonusPanelUsesNativeTotals()
    {
        var memory = Party();
        memory.Int32(SavemapPartyReader.AddressEquipmentMenuPartySlot, 0);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentListActive, 1);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentCategory, 0);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentListCursor, 0);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentListScroll, 0);
        memory.Int32(EquipmentMenuSelectionReader.AddressEquipmentListCount, 1);
        memory.Byte(EquipmentMenuSelectionReader.AddressEquipmentCandidates, 1);
        for (var id = 0; id < 2; id++)
        {
            var offset = id * 0x2C;
            memory.Byte(0xDBE734 + offset, (byte)(16 + id * 2));
            memory.Byte(0xDBE738 + offset, 96);
            memory.Byte(0xDBE736 + offset, 1);
            memory.Write(0xDBE74C + offset, new byte[8]);
            memory.Write(0xDBE744 + offset, id == 0 ? [1, 2, 3, 0, 2, 4, 3, 99] : [1, 2, 3, 0, 4, 5, 6, 77]);
        }
        memory.Byte(0xDBCCE2, 6); memory.Byte(0xDBCCE3, 5);
        memory.Byte(0xDBCCE4, 2); memory.Byte(0xDBCCE5, 1);
        memory.Byte(0xDBCCF1, 1); memory.Write(0xDBCCE9, new byte[8]);
        memory.Write(0xDBCCF8, [0, 2, 1, 3, 10, 3, 66, 55]);
        var list = new EquipmentMenuSelectionReader(memory, resolveWeaponName: _ => "Sword");
        Require(list.TryRead(out var stock) && !stock.Description!.Contains("Defense"), "Stock keeps category-only comparison.");
        Bonuses(memory);
        Require(list.TryRead(out var changed) && changed.Description!.Contains("Defense 10, up from 8"),
            "Visible bonus panel must compare the weapon's native defense bonus.");
        Require(changed.Description!.Contains("Attack 28, up from 26") &&
            changed.Description.Contains("Magic attack 8, up from 7") &&
            changed.Description.Contains("Magic defense 11, up from 8"), "Native panel totals must match the patch's formulas.");
        var party = new SavemapPartyReader(memory, resolveWeaponName: _ => "Sword");
        Require(party.TryReadEquipment(0, 0, out var current) && current.Description!.Contains("Magic attack 7"),
            "Current Equipment readout includes the shown bonus panel.");
    }

    private static void Bonuses(Memory memory)
    {
        memory.Byte(0x707896, 0x6C);
        memory.Write(0x707A44, Convert.FromHexString("8B45BC8945D4"));
        memory.Write(0x707979, Convert.FromHexString("31C0380C1074094083F80475F531C0C38A441004C3"));
        memory.Write(0x707BA1, Convert.FromHexString("31C08D9134E7DB008A42048945C08A020345A883C210E856FEFFFF90"));
        memory.Write(0x707BD6, Convert.FromHexString("6BC8248D91E2CCDB008A020345A08945D08A42028945E08A420103459C8945E49083C216B102E878FDFFFF0345B08945D48A42ED8945D831C9E865FDFFFF0345A48945F0"));
    }

    private static void ChainedLayoutChangesInvalidateEquipmentReadout()
    {
        var memory = Party(); Linked(memory);
        memory.Byte(0xDBE734, 16); memory.Byte(0xDBE738, 96); memory.Byte(0xDBE736, 1);
        memory.Write(0xDBE74C, [6, 7, 7, 0, 0, 0, 0, 0]);
        memory.BeforeRead = (address, count) => { if (address == 0xDBE74C && count == 2) memory.Byte(address, 5); };
        var reader = new SavemapPartyReader(memory, resolveWeaponName: _ => "Sword");
        Require(!reader.TryReadEquipment(0, 0, out _), "Changed native materia chain must invalidate the equipment description.");
    }
    private static Memory Party()
    {
        var memory = new Memory();
        memory.Byte(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset, 0);
        memory.Write(Character + SavemapPartyReader.CharacterNameOffset, [35, 76, 79, 85, 68, 255, 255, 255, 255, 255, 255, 255]);
        memory.Byte(Character + 0x1C, 0); memory.Byte(Character + 0x1D, 0);
        memory.Byte(Character + 0x1E, 4); memory.Byte(Character + 0x1B, 5);
        return memory;
    }

    // Independent bytes from Gameplay Tweaks 1.78's TwoAccessories and Enabled MultiLinkedSlots hext.
    private static void Accessories(Memory memory)
    {
        memory.Write(0x707571, Convert.FromHexString("83F802740383EA038B45FC31C98D44101E8A0880E1FF894DDC3008908A4DE8080890908B55DC80FAFF7418"));
        memory.Write(0x707383, Convert.FromHexString("E9E001000090"));
        memory.Byte(0x705CB8, 4);
        memory.Write(0x7046B1, Convert.FromHexString("31DB8B4DF869C9840000008B55FC31C029DA8A44111E3CFF"));
    }
    private static void Linked(Memory memory)
    {
        memory.Write(0x5CB41D, Convert.FromHexString("837DE807"));
        memory.Write(0x5CB496, Convert.FromHexString("837DE807"));
        memory.Write(0x5CB8E4, Convert.FromHexString("EB2A"));
        memory.Write(0x5CB919, Convert.FromHexString("39C990"));
        memory.Write(0x5CB921, Convert.FromHexString("83E20390909083FA03"));
    }
    private static void Require(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
    }
    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<int, byte> bytes = new();
        private readonly Dictionary<int, int> reads = new();
        internal Action<int, int>? BeforeRead { get; set; }
        internal void Byte(int address, byte value) => bytes[address] = value;
        internal void Int32(int address, int value) => Write(address, BitConverter.GetBytes(value));
        internal void Write(int address, byte[] value)
        {
            for (var index = 0; index < value.Length; index++) bytes[address + index] = value[index];
        }
        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            var address = checked((int)virtualAddress);
            reads.TryGetValue(address, out var count); reads[address] = ++count;
            BeforeRead?.Invoke(address, count);
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(address + index, out var value)) return false;
                destination[index] = value;
            }
            return true;
        }
    }
}
