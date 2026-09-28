using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The 2026-09-28 legacy session, 10:17:43-10:17:50: Materia, Cloud. The slots spoke, but the
/// Check/Arrange selector (0x00DD12B8) moved between its two rows in silence and Check's
/// command grid (0x00DD1398, uncatalogued, "Widget 0x00DD1398") moved over Attack, Magic,
/// Summon and Item - drawing "Attack with equipped weapon" and the rest - in silence too.
/// Neither emits a cursor draw the frame matcher can pair with a label. FUN_0070cf0b runs
/// the screen from the mode at 0x00920FA0 and draws every label from a native source, so
/// these read the selector row and that source. The strings are the ones ff7_en.exe holds.
/// </summary>
internal static class MateriaSubmenuSpeechTests
{
    private const uint Mode = 0x00920FA0;
    private const uint MenuStrings = 0x00920BA8;
    private const uint PromptStrings = 0x00920B18;
    private const uint PartySlot = 0x00DD1638;
    private const uint CommandCells = 0x00DBA4E4;
    private const uint CommandColumns = 0x00DBA4B9;
    private const uint Characters = 0x00DBFD8C;
    private const uint MateriaInventory = 0x00DC04B4;
    private const uint WeaponSlots = 0x00DBE74C;

    internal static void Run()
    {
        EveryMateriaSelectorIsCatalogued();
        CheckAndArrangeAreSaid();
        CheckSaysEachCommandWithItsDescription();
        CheckListsReadTheMateriaScreensPartySlot();
        ArrangeSaysItsFourCommands();
        TrashSaysTheMateriaAndAsksOnce();
        ExchangeSaysWhoseSlotAndWhatIsHeld();
        TheLoggedSessionIsNoLongerSilent();
        Console.WriteLine("PASS Materia submenus: Check/Arrange, Check's commands with descriptions and its ability lists, " +
                          "Arrange, Trash (asked once) and Exchange (character, slot, held materia) are read from native state.");
    }

    private static void EveryMateriaSelectorIsCatalogued()
    {
        foreach (var (address, name, kind) in new (uint, string, MenuWidgetKind)[]
                 {
                     (0x00DD12B8, "Materia command", MenuWidgetKind.MateriaCommand),
                     (0x00DD1398, "Materia check command", MenuWidgetKind.MateriaCheckCommand),
                     (0x00DD13D0, "Materia check magic list", MenuWidgetKind.MagicList),
                     (0x00DD1408, "Materia check summon list", MenuWidgetKind.SummonList),
                     (0x00DD1440, "Materia check enemy skill list", MenuWidgetKind.EnemySkillList),
                     (0x00DD1478, "Materia arrange", MenuWidgetKind.MateriaArrangeCommand),
                     (0x00DD14B0, "Materia trash list", MenuWidgetKind.MateriaTrashList),
                     (0x00DD14E8, "Materia trash confirmation", MenuWidgetKind.MateriaTrashConfirmation),
                     (0x00DD1520, "Materia exchange list", MenuWidgetKind.MateriaExchangeList),
                     (0x00DD1558, "Materia exchange slot", MenuWidgetKind.MateriaExchangeSlot),
                     (0x00DD1590, "Materia exchange character", MenuWidgetKind.MateriaExchangeCharacter)
                 })
        {
            Require(MenuWidgetCatalog.TryResolve(address, out var descriptor), $"0x{address:X8} is catalogued");
            Equal(name, descriptor.Name, $"0x{address:X8} name");
            Equal(kind, descriptor.Kind, $"0x{address:X8} kind");
            Equal(1, MenuWidgetCatalog.All.Count(item => item.Name == name),
                $"\"{name}\" names one selector (the x64 bridge resolves by name)");
        }
    }

    private static void CheckAndArrangeAreSaid()
    {
        var memory = ScreenMemory();
        var reader = Reader(memory);
        memory.Int32(Mode, 0);
        Selector(memory, 0x00DD12B8, row: 0);
        Equal("Check", Read(reader, MenuWidgetKind.MateriaCommand), "row 0");
        Selector(memory, 0x00DD12B8, row: 1);
        Equal("Arrange", Read(reader, MenuWidgetKind.MateriaCommand), "row 1");

        // Bit 0 of 0x00DC0B4B: FUN_0070cf0b draws Arrange in colour 0 and buzzes on OK.
        memory.Byte(0x00DC0B4B, 1);
        Equal("Arrange unavailable", Read(reader, MenuWidgetKind.MateriaCommand), "greyed Arrange");
        memory.Byte(0x00DC0B4B, 0);

        memory.Int32(Mode, 1);
        Equal<string?>(null, Read(reader, MenuWidgetKind.MateriaCommand), "not read while the slots have the cursor");
    }

    private static void CheckSaysEachCommandWithItsDescription()
    {
        var memory = ScreenMemory();
        var reader = Reader(memory);
        memory.Int32(Mode, 4);
        memory.Int32(PartySlot, 1);
        var block = CommandCells + 0x440;
        memory.Byte(CommandColumns + 0x440, 2);
        foreach (var (cell, command) in new[] { (0, 1), (1, 2), (2, 3), (3, 4), (4, 7), (5, 0xFF), (6, 0xFF), (7, 0xFF) })
        {
            memory.Byte(block + (uint)(cell * 6), (byte)command);
        }

        Selector(memory, 0x00DD1398, row: 0);
        Equal("Attack. Attack with equipped weapon", Read(reader, MenuWidgetKind.MateriaCheckCommand), "10:17:46 row 0");
        Selector(memory, 0x00DD1398, row: 2);
        Equal("Summon. Call Summon Monster", Read(reader, MenuWidgetKind.MateriaCheckCommand), "10:17:47 row 2");
        Selector(memory, 0x00DD1398, row: 3);
        Equal("Item. Use available item", Read(reader, MenuWidgetKind.MateriaCheckCommand), "10:17:48 row 3");

        // A second column holds cells 4-7 (row + column * 4).
        Selector(memory, 0x00DD1398, row: 0, column: 1);
        Equal("Steal. Steal an item from an opponent", Read(reader, MenuWidgetKind.MateriaCheckCommand), "second column");
        Selector(memory, 0x00DD1398, row: 1, column: 1);
        Equal("Empty", Read(reader, MenuWidgetKind.MateriaCheckCommand), "an empty cell");
        memory.Byte(CommandColumns + 0x440, 1);
        Equal<string?>(null, Read(reader, MenuWidgetKind.MateriaCheckCommand), "a column the grid does not have");
    }

    private static void CheckListsReadTheMateriaScreensPartySlot()
    {
        var memory = ScreenMemory();
        memory.Int32(PartySlot, 2);
        memory.Byte(0x00DD17E8, 0);
        memory.UInt16(0x00DBA4AC + 0x880, 40);
        // FUN_00708970: the Check Magic list, three columns, spell = column + row * 3 + scroll * 3.
        Widget(memory, 0x00DD13D0, first: 1, cursor: 1, columns: 3, rows: 3, scroll: 1);
        var record = 0x00DBA5A0u + 0x880u + (uint)((1 + 1 * 3 + 1 * 3) * 8);
        memory.Byte(record, 0x0A);
        memory.Byte(record + 1, 22);
        var magic = new MagicMenuSelectionReader(memory, id => id == 0x0A ? "Fire2" : null, _ => "Medium fire damage");
        var widget = new ActiveMenuWidgetReader(memory).Read(0x00DD13D0);
        Require(magic.TryReadSlot(widget, out var slot) && !slot.IsEmpty, "the Check Magic list reads a spell");
        Equal("Fire2", slot.Spell.Name, "for the Materia screen's party slot (0x00DD1638), not the Magic screen's");
        Equal(22, slot.Spell.MpCost, "with the MP it shows");
    }

    private static void ArrangeSaysItsFourCommands()
    {
        var memory = ScreenMemory();
        var reader = Reader(memory);
        memory.Int32(Mode, 8);
        var said = Enumerable.Range(0, 4).Select(row =>
        {
            Selector(memory, 0x00DD1478, row);
            return Read(reader, MenuWidgetKind.MateriaArrangeCommand);
        }).ToArray();
        Equal("Arrange|Exchange|Remove all|Trash", string.Join('|', said), "Arrange's rows, from 0x00920C70");
    }

    private static void TrashSaysTheMateriaAndAsksOnce()
    {
        var memory = ScreenMemory();
        var reader = Reader(memory);
        memory.Int32(Mode, 9);
        Widget(memory, 0x00DD14B0, first: 0, cursor: 2, columns: 1, rows: 10, scroll: 3);
        memory.UInt32(MateriaInventory + 5 * 4, 0x0000_2A31);
        Equal("Fire. Equips Fire magic. AP 42", Read(reader, MenuWidgetKind.MateriaTrashList), "list row + scroll");
        memory.UInt32(MateriaInventory + 5 * 4, uint.MaxValue);
        Equal("Empty materia slot", Read(reader, MenuWidgetKind.MateriaTrashList), "an empty inventory row");

        memory.Int32(Mode, 10);
        Widget(memory, 0x00DD14E8, first: 0, cursor: 1, columns: 1, rows: 2);
        var coordinator = new ActiveMenuFrameSpeechCoordinator();
        var now = new DateTime(2026, 9, 28, 10, 20, 0, DateTimeKind.Utc);
        Complete(coordinator, reader, memory, 0x00DD14E8, now);
        Equal("Do you really want to remove this? Wait a sec", coordinator.Poll(), "the question with the first answer");
        Complete(coordinator, reader, memory, 0x00DD14E8, now.AddMilliseconds(20));
        Equal<string?>(null, coordinator.Poll(), "not repeated");
        Selector(memory, 0x00DD14E8, row: 0);
        Complete(coordinator, reader, memory, 0x00DD14E8, now.AddMilliseconds(300));
        Equal("I sure do!", coordinator.Poll(), "moving between answers says the answer alone");
    }

    private static void ExchangeSaysWhoseSlotAndWhatIsHeld()
    {
        var memory = ScreenMemory();
        var reader = Reader(memory);
        memory.Int32(Mode, 11);
        memory.Int32(0x00DD1664, 0);
        // Roster (FUN_0070ebef): characters 0, 2 and 3; the second panel from the top.
        memory.Fill(0x00DD1648, 9, 0xFF);
        memory.Byte(0x00DD1648, 0);
        memory.Byte(0x00DD1649, 2);
        memory.Byte(0x00DD164A, 3);
        memory.Int32(0x00DD166C, 0);
        memory.Int32(0x00DD1668, 1);
        memory.Int32(0x00DD1678, 0);
        Character(memory, 0, "Cloud", weapon: 1);
        Character(memory, 2, "Tifa", weapon: 2);
        memory.Fill(WeaponSlots + 2 * 0x2C, 8, 0);
        memory.Byte(WeaponSlots + 2 * 0x2C, 1);
        memory.Byte(WeaponSlots + 2 * 0x2C + 1, 1);
        memory.Fill(0x00DBCCE9, 8, 0);
        memory.Byte(0x00DBCCE9, 1);
        memory.UInt32(Characters + 2 * 0x84 + 0x40, 0x0000_2A31);
        memory.UInt32(Characters + 2 * 0x84 + 0x44, uint.MaxValue);

        Widget(memory, 0x00DD1558, first: 0, cursor: 0, columns: 8, rows: 2);
        Equal("Tifa, Weapon materia slot 1, Fire. Equips Fire magic. AP 42",
            Read(reader, MenuWidgetKind.MateriaExchangeSlot), "the viewed character's slot");
        Widget(memory, 0x00DD1558, first: 1, cursor: 0, columns: 8, rows: 2);
        Equal("Tifa, Weapon materia slot 2, empty", Read(reader, MenuWidgetKind.MateriaExchangeSlot), "an empty slot");
        Widget(memory, 0x00DD1558, first: 2, cursor: 0, columns: 8, rows: 2);
        Equal("Tifa, Weapon materia position 3, no slot", Read(reader, MenuWidgetKind.MateriaExchangeSlot), "no slot there");

        // Picked up Tifa's Fire (FUN_0070fec2: 0x00DD1678 set, source in 0x00DD1640/12A0/12A4).
        memory.Int32(0x00DD1678, 1);
        memory.Int32(0x00DCA814, 0);
        memory.Int32(0x00DD1640, 1);
        memory.Int32(0x00DD12A0, 0);
        memory.Int32(0x00DD12A4, 0);
        Widget(memory, 0x00DD1558, first: 1, cursor: 0, columns: 8, rows: 2);
        Equal("Tifa, Weapon materia slot 2, empty. Holding Fire from Tifa's weapon slot 1",
            Read(reader, MenuWidgetKind.MateriaExchangeSlot), "the held materia is said with the target");

        memory.Int32(0x00DD1664, 1);
        Widget(memory, 0x00DD1520, first: 0, cursor: 0, columns: 1, rows: 10, scroll: 0);
        memory.UInt32(MateriaInventory, uint.MaxValue);
        Equal("Empty materia slot. Holding Fire from Tifa's weapon slot 1",
            Read(reader, MenuWidgetKind.MateriaExchangeList), "the inventory as a target");
        Equal<string?>(null, Read(reader, MenuWidgetKind.MateriaExchangeSlot), "the slot grid is not read while the list has the cursor");

        memory.Int32(0x00DD1664, 2);
        memory.Int32(0x00DD1678, 0);
        Widget(memory, 0x00DD1590, first: 0, cursor: 0, columns: 1, rows: 3);
        Equal("Tifa", Read(reader, MenuWidgetKind.MateriaExchangeCharacter), "the name row takes the whole set");
        Widget(memory, 0x00DD1590, first: 0, cursor: 2, columns: 1, rows: 3);
        Equal("Tifa, Armor materia", Read(reader, MenuWidgetKind.MateriaExchangeCharacter), "the armor row");
    }

    /// <summary>
    /// The logged frames, 10:17:45-10:17:48, through the frame coordinator: nothing drew a
    /// cursor this matcher could pair, so only the native read can say them.
    /// </summary>
    private static void TheLoggedSessionIsNoLongerSilent()
    {
        var memory = ScreenMemory();
        var reader = Reader(memory);
        var coordinator = new ActiveMenuFrameSpeechCoordinator();
        var now = new DateTime(2026, 9, 28, 10, 17, 45, DateTimeKind.Utc);
        memory.Int32(Mode, 0);
        Widget(memory, 0x00DD12B8, first: 0, cursor: 0, columns: 1, rows: 2);
        DrawCommandFrame(coordinator);
        Complete(coordinator, reader, memory, 0x00DD12B8, now);
        Equal("Check", coordinator.Poll(), "10:17:45 row 0");
        Selector(memory, 0x00DD12B8, row: 1);
        DrawCommandFrame(coordinator);
        Complete(coordinator, reader, memory, 0x00DD12B8, now.AddMilliseconds(400));
        Equal("Arrange", coordinator.Poll(), "10:17:45 row 1");

        memory.Int32(Mode, 4);
        memory.Int32(PartySlot, 0);
        memory.Byte(CommandColumns, 1);
        foreach (var (cell, command) in new[] { (0, 1), (1, 2), (2, 3), (3, 4) })
        {
            memory.Byte(CommandCells + (uint)(cell * 6), (byte)command);
        }

        Widget(memory, 0x00DD1398, first: 0, cursor: 1, columns: 1, rows: 4);
        coordinator.ObserveDraw(new MenuTextRenderEntry("Magic", 57, 251, 7, 0x3DCCCCCD));
        coordinator.ObserveDraw(new MenuTextRenderEntry("Cast spell", 27, 159, 7, 0x3DCED917));
        Complete(coordinator, reader, memory, 0x00DD1398, now.AddMilliseconds(2000));
        Equal("Magic. Cast spell", coordinator.Poll(), "10:17:47 row 1");
    }

    private static void DrawCommandFrame(ActiveMenuFrameSpeechCoordinator coordinator)
    {
        coordinator.ObserveDraw(new MenuTextRenderEntry("Butterfly Edge", 320, 32, 7, 0x3E4CCCCD));
        coordinator.ObserveDraw(new MenuTextRenderEntry("Silver Armlet", 320, 84, 7, 0x3E4CCCCD));
        coordinator.ObserveDraw(new MenuTextRenderEntry("Check", 280, 60, 7, 0x3E4CCCCD));
        coordinator.ObserveDraw(new MenuTextRenderEntry("Arrange", 280, 111, 7, 0x3E4CCCCD));
    }

    private static void Complete(
        ActiveMenuFrameSpeechCoordinator coordinator,
        MateriaSubmenuSelectionReader reader,
        Memory memory,
        uint address,
        DateTime now)
    {
        var widget = new ActiveMenuWidgetReader(memory).Read(address);
        coordinator.CompleteFrame(
            reader.TryRead(widget.Kind, out var selection) ? widget with { NativeSelection = selection } : widget,
            now);
    }

    private static string? Read(MateriaSubmenuSelectionReader reader, MenuWidgetKind kind)
    {
        if (!reader.TryRead(kind, out var selection))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(selection.Description)
            ? selection.Text
            : $"{selection.Text}. {selection.Description}";
    }

    private static MateriaSubmenuSelectionReader Reader(Memory memory) =>
        new(
            memory,
            new MateriaMenuSelectionReader(
                memory,
                id => id == 0x31 ? "Fire" : null,
                id => id == 0x31 ? "Equips Fire magic" : null),
            id => id switch { 1 => "Attack", 2 => "Magic", 3 => "Summon", 4 => "Item", 7 => "Steal", _ => null },
            id => id switch
            {
                1 => "Attack with equipped weapon",
                2 => "Cast spell",
                3 => "Call Summon Monster",
                4 => "Use available item",
                7 => "Steal an item from an opponent",
                _ => null
            });

    /// <summary>The string rows FUN_0070cf0b and FUN_00709fd8 draw, as ff7_en.exe holds them.</summary>
    internal static Memory ScreenMemory()
    {
        var memory = new Memory();
        string[] menu =
        [
            "Wpn.", "Arm.", "Ability list", "Equip effect", "Check", "Arrange", "", "AP", "To next level", "MASTER",
            "Arrange", "Exchange", "Remove all", "Trash"
        ];
        for (var row = 0; row < menu.Length; row++)
        {
            memory.Text(MenuStrings + (uint)(row * 0x14), menu[row], 0x14);
        }

        memory.Text(PromptStrings, "Do you really want to remove this?", 0x24);
        memory.Text(PromptStrings + 0x24, "I sure do!", 0x24);
        memory.Text(PromptStrings + 0x48, "Wait a sec", 0x24);
        memory.Byte(0x00DC0B4B, 0);
        memory.UInt32(0x00DD12B0, uint.MaxValue);
        memory.UInt32(0x00DD12AC, 0);
        return memory;
    }

    private static void Character(Memory memory, int character, string name, byte weapon)
    {
        var record = Characters + (uint)(character * 0x84);
        memory.Byte(record, (byte)character);
        memory.Text(record + 0x10, name, 12);
        memory.Byte(record + 0x1C, weapon);
        memory.Byte(record + 0x1D, 0);
    }

    private static void Selector(Memory memory, uint address, int row, int column = 0)
    {
        memory.Int32(address, column);
        memory.Int32(address + 4, row);
    }

    internal static void Widget(Memory memory, uint address, int first, int cursor, int columns, int rows, int scroll = 0)
    {
        memory.Fill(address, 0x38, 0);
        memory.Int32(address, first);
        memory.Int32(address + 4, cursor);
        memory.Int32(address + 8, columns);
        memory.Int32(address + 12, rows);
        memory.Int32(address + 0x14, scroll);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Materia submenus, {label}: expected \"{expected}\", got \"{actual}\"");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Materia submenus: {message}");
        }
    }

    internal sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = new();

        internal void Byte(uint address, byte value) => bytes[address] = value;

        internal void UInt16(uint address, ushort value) => Write(address, BitConverter.GetBytes(value));

        internal void Int32(uint address, int value) => Write(address, BitConverter.GetBytes(value));

        internal void UInt32(uint address, uint value) => Write(address, BitConverter.GetBytes(value));

        internal void Fill(uint address, int length, byte value) => Write(address, Enumerable.Repeat(value, length).ToArray());

        internal void Text(uint address, string value, int length)
        {
            Fill(address, length, 0xFF);
            Write(address, value.Select(c => checked((byte)(c - 0x20))).ToArray());
        }

        private void Write(uint address, byte[] value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                bytes[address + (uint)index] = value[index];
            }
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out var value))
                {
                    return false;
                }

                destination[index] = value;
            }

            return true;
        }
    }
}
