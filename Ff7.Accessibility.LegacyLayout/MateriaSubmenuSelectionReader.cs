using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the Materia screen's command and Arrange selectors from native state.
/// <para>FUN_0070cf0b runs the screen from the mode at 0x00920FA0: 0 is the Check/Arrange
/// command, 4 the Check command grid (5-7 its Magic, Summon and Enemy Skill lists), 8 the
/// Arrange menu, 9 the Trash materia list, 10 the Trash confirmation and 11 Exchange
/// (FUN_0070fec2). None of these selectors emits a usable cursor draw to match against its
/// rendered label, so each reads the selector's row and the labels the screen draws from
/// their native sources: the menu string rows at 0x00920BA8 (0x14 bytes each), the trash
/// prompt rows at 0x00920B18 (0x24 bytes each), kernel2 command names and descriptions, and
/// the character and materia words the screen itself reads.</para>
/// </summary>
public sealed class MateriaSubmenuSelectionReader
{
    public const int AddressMenuMode = MateriaMenuSelectionReader.AddressMenuMode;
    public const int CommandMode = 0;
    public const int CheckCommandMode = 4;
    public const int ArrangeMode = 8;
    public const int TrashListMode = 9;
    public const int TrashConfirmationMode = 10;
    public const int ExchangeMode = 11;

    public const uint AddressCommandWidget = 0x00DD12B8;
    public const uint AddressCheckCommandWidget = 0x00DD1398;
    public const uint AddressArrangeWidget = 0x00DD1478;
    public const uint AddressTrashListWidget = 0x00DD14B0;
    public const uint AddressTrashConfirmationWidget = 0x00DD14E8;
    public const uint AddressExchangeListWidget = 0x00DD1520;
    public const uint AddressExchangeSlotWidget = 0x00DD1558;
    public const uint AddressExchangeCharacterWidget = 0x00DD1590;

    /// <summary>Bit 0 greys Arrange out; FUN_0070cf0b draws it in colour 0 and buzzes on OK.</summary>
    public const uint AddressArrangeLockFlags = 0x00DC0B4B;

    /// <summary>The party slot (0-2) the screen shows, cycled by L1/R1.</summary>
    public const uint AddressPartySlot = 0x00DD1638;

    /// <summary>
    /// The computed party block's command grid: six-byte cells, command id first
    /// (0xFF empty), indexed row + column * 4, with the column count at 0x00DBA4B9.
    /// </summary>
    public const uint AddressCommandCells = 0x00DBA4E4;
    public const uint AddressCommandColumns = 0x00DBA4B9;
    public const int CommandCellSize = 6;

    public const uint AddressMateriaInventory = (uint)MateriaMenuSelectionReader.AddressMateriaInventory;

    public const uint AddressMenuStrings = 0x00920BA8;
    public const int MenuStringSize = 0x14;
    public const int CheckLabelRow = 4;
    public const int ArrangeLabelRow = 5;
    public const int ArrangeMenuFirstRow = 10;

    public const uint AddressTrashPromptStrings = 0x00920B18;
    public const int TrashPromptStringSize = 0x24;

    public const uint AddressExchangeSubmode = 0x00DD1664;
    public const uint AddressExchangeRoster = 0x00DD1648;
    public const uint AddressExchangeRosterTop = 0x00DD166C;
    public const uint AddressExchangeRosterOffset = 0x00DD1668;
    public const uint AddressExchangeHolding = 0x00DD1678;
    public const uint AddressExchangeHeldSubmode = 0x00DCA814;
    public const uint AddressExchangeHeldRosterIndex = 0x00DD1640;
    public const uint AddressExchangeHeldRow = 0x00DD12A0;
    public const uint AddressExchangeHeldColumn = 0x00DD12A4;

    public const uint AddressCharacterRecords = 0x00DBFD8C;
    public const int CharacterRecordSize = 0x84;

    private const int WidgetCursorOffset = 0x04;
    private const int WidgetScrollOffset = 0x14;
    private const int PartySlotCount = 3;
    private const int CharacterCount = 9;
    private const int MateriaInventoryCount = 200;

    private readonly ILegacyAddressSpace memory;
    private readonly MateriaMenuSelectionReader materia;
    private readonly SavemapPartyReader characters;
    private readonly EquipmentStatReader equipmentStats;
    private readonly Func<int, string?> resolveCommandName;
    private readonly Func<int, string?> resolveCommandDescription;

    public MateriaSubmenuSelectionReader(
        ILegacyAddressSpace memory,
        MateriaMenuSelectionReader materia,
        Func<int, string?>? resolveCommandName = null,
        Func<int, string?>? resolveCommandDescription = null)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.materia = materia ?? throw new ArgumentNullException(nameof(materia));
        characters = new SavemapPartyReader(memory);
        equipmentStats = new EquipmentStatReader(memory);
        this.resolveCommandName = resolveCommandName ?? (_ => null);
        this.resolveCommandDescription = resolveCommandDescription ?? (_ => null);
    }

    public static bool Handles(MenuWidgetKind kind) => kind is
        MenuWidgetKind.MateriaCommand or
        MenuWidgetKind.MateriaCheckCommand or
        MenuWidgetKind.MateriaArrangeCommand or
        MenuWidgetKind.MateriaTrashList or
        MenuWidgetKind.MateriaTrashConfirmation or
        MenuWidgetKind.MateriaExchangeSlot or
        MenuWidgetKind.MateriaExchangeList or
        MenuWidgetKind.MateriaExchangeCharacter;

    public bool TryRead(MenuWidgetKind kind, out NativeMenuSelection selection)
    {
        selection = default;
        try
        {
            // Everything is read twice; a frame where the screen changed in between is silent.
            if (!Handles(kind) ||
                !TryReadOnce(kind, out var candidate) ||
                !TryReadOnce(kind, out var bookend) ||
                candidate != bookend)
            {
                return false;
            }

            selection = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadOnce(MenuWidgetKind kind, out NativeMenuSelection selection)
    {
        selection = default;
        if (!memory.TryReadInt32((uint)AddressMenuMode, out var mode))
        {
            return false;
        }

        return kind switch
        {
            MenuWidgetKind.MateriaCommand when mode == CommandMode => TryReadCommand(out selection),
            MenuWidgetKind.MateriaCheckCommand when mode == CheckCommandMode => TryReadCheckCommand(out selection),
            MenuWidgetKind.MateriaArrangeCommand when mode == ArrangeMode => TryReadArrangeCommand(out selection),
            MenuWidgetKind.MateriaTrashList when mode == TrashListMode => TryReadTrashList(out selection),
            MenuWidgetKind.MateriaTrashConfirmation when mode == TrashConfirmationMode =>
                TryReadTrashConfirmation(out selection),
            MenuWidgetKind.MateriaExchangeSlot when mode == ExchangeMode => TryReadExchangeSlot(out selection),
            MenuWidgetKind.MateriaExchangeList when mode == ExchangeMode => TryReadExchangeList(out selection),
            MenuWidgetKind.MateriaExchangeCharacter when mode == ExchangeMode =>
                TryReadExchangeCharacter(out selection),
            _ => false
        };
    }

    private bool TryReadCommand(out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadRow(AddressCommandWidget, 2, out var row) ||
            !TryReadMenuString(row == 0 ? CheckLabelRow : ArrangeLabelRow, out var label))
        {
            return false;
        }

        var locked = false;
        if (row == 1)
        {
            if (!memory.TryReadByte(AddressArrangeLockFlags, out var flags))
            {
                return false;
            }

            locked = (flags & 1) != 0;
        }

        selection = new NativeMenuSelection(
            locked ? $"{label} unavailable" : label,
            null,
            $"materia-command:{row}:{(locked ? 1 : 0)}");
        return true;
    }

    private bool TryReadCheckCommand(out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadPartySlot(out var partySlot) ||
            !memory.TryReadInt32(AddressCheckCommandWidget, out var column) ||
            !TryReadRow(AddressCheckCommandWidget, 4, out var row) ||
            !memory.TryReadByte(AddressCommandColumns + (uint)(partySlot * 0x440), out var columns) ||
            columns is 0 or > 4 ||
            column < 0 || column >= columns)
        {
            return false;
        }

        var cell = row + (column * 4);
        if (!memory.TryReadByte(
                AddressCommandCells + (uint)(partySlot * 0x440) + (uint)(cell * CommandCellSize),
                out var commandId))
        {
            return false;
        }

        if (commandId == 0xFF)
        {
            selection = new NativeMenuSelection("Empty", null, $"materia-check:{partySlot}:{cell}:empty");
            return true;
        }

        var name = resolveCommandName(commandId)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        selection = new NativeMenuSelection(
            name,
            resolveCommandDescription(commandId)?.Trim(),
            $"materia-check:{partySlot}:{cell}:{commandId}");
        return true;
    }

    private bool TryReadArrangeCommand(out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadRow(AddressArrangeWidget, 4, out var row) ||
            !TryReadMenuString(ArrangeMenuFirstRow + row, out var label))
        {
            return false;
        }

        selection = new NativeMenuSelection(label, null, $"materia-arrange:{row}");
        return true;
    }

    private bool TryReadTrashList(out NativeMenuSelection selection)
    {
        selection = default;
        return TryReadInventoryRow(AddressTrashListWidget, out var index, out var raw) &&
            TryDescribeInventory(index, raw, "materia-trash", out selection);
    }

    private bool TryReadTrashConfirmation(out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadRow(AddressTrashConfirmationWidget, 2, out var row) ||
            !TryReadString(AddressTrashPromptStrings, TrashPromptStringSize, 0, out var prompt) ||
            !TryReadString(AddressTrashPromptStrings, TrashPromptStringSize, 1 + row, out var answer))
        {
            return false;
        }

        // The prompt rides in the description; the coordinator says it once, on arrival.
        selection = new NativeMenuSelection(answer, prompt, $"materia-trash-confirm:{row}");
        return true;
    }

    private bool TryReadExchangeSlot(out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadExchangeSubmode(0) ||
            !memory.TryReadInt32(AddressExchangeSlotWidget, out var column) ||
            !TryReadRow(AddressExchangeSlotWidget, 2, out var row) ||
            column is < 0 or >= 8 ||
            !TryReadViewedCharacter(out var character, out var characterName) ||
            !TryReadCharacterMateria(character, row, column, out var slotType, out var raw))
        {
            return false;
        }

        var position = slotType == 0
            ? $"{RowLabel(row)} materia position {column + 1}, no slot"
            : $"{RowLabel(row)} materia slot {column + 1}";
        string text;
        string? description = null;
        if (slotType == 0)
        {
            text = $"{characterName}, {position}";
        }
        else if (raw == uint.MaxValue)
        {
            text = $"{characterName}, {position}, empty";
        }
        else if (materia.TryDescribe(raw, out var name, out var detail))
        {
            text = $"{characterName}, {position}, {name}";
            description = detail;
        }
        else
        {
            return false;
        }

        if (!TryAppendHolding(ref description, out var holding))
        {
            return false;
        }

        selection = new NativeMenuSelection(
            text,
            description,
            $"materia-exchange-slot:{character}:{row}:{column}:{slotType}:{raw:X8}:{holding}");
        return true;
    }

    private bool TryReadExchangeList(out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadExchangeSubmode(1) ||
            !TryReadInventoryRow(AddressExchangeListWidget, out var index, out var raw) ||
            !TryDescribeInventory(index, raw, "materia-exchange-list", out var described))
        {
            return false;
        }

        var description = described.Description;
        if (!TryAppendHolding(ref description, out var holding))
        {
            return false;
        }

        selection = described with { Description = description, Key = $"{described.Key}:{holding}" };
        return true;
    }

    private bool TryReadExchangeCharacter(out NativeMenuSelection selection)
    {
        selection = default;
        if (!TryReadExchangeSubmode(2) ||
            !TryReadRow(AddressExchangeCharacterWidget, 3, out var row) ||
            !TryReadViewedCharacter(out var character, out var characterName))
        {
            return false;
        }

        // FUN_0070ecf4 draws the name, then "Wpn." and "Arm." beside the two slot rows; the
        // name row takes the character's whole set, the others one row of it.
        var text = row == 0 ? characterName : $"{characterName}, {RowLabel(row - 1)} materia";
        string? description = null;
        if (!TryAppendHolding(ref description, out var holding))
        {
            return false;
        }

        selection = new NativeMenuSelection(text, description, $"materia-exchange-character:{character}:{row}:{holding}");
        return true;
    }

    /// <summary>
    /// While a materia or a set is picked up (0x00DD1678), the screen keeps a second cursor on
    /// it: its source is part of what the player sees.
    /// </summary>
    private bool TryAppendHolding(ref string? description, out string holdingKey)
    {
        holdingKey = "none";
        if (!memory.TryReadInt32(AddressExchangeHolding, out var holding))
        {
            return false;
        }

        if (holding == 0)
        {
            return true;
        }

        if (!memory.TryReadInt32(AddressExchangeHeldSubmode, out var heldSubmode) ||
            !memory.TryReadInt32(AddressExchangeHeldRosterIndex, out var heldIndex) ||
            !memory.TryReadInt32(AddressExchangeHeldRow, out var heldRow) ||
            !memory.TryReadInt32(AddressExchangeHeldColumn, out var heldColumn) ||
            heldIndex is < 0 or >= CharacterCount ||
            !memory.TryReadByte(AddressExchangeRoster + (uint)heldIndex, out var heldCharacter) ||
            heldCharacter >= CharacterCount ||
            !characters.TryReadCharacter(heldCharacter, out var heldMember))
        {
            return false;
        }

        string held;
        if (heldSubmode == 2 && heldRow is >= 0 and <= 2)
        {
            held = heldRow == 0
                ? $"{heldMember.Name}'s materia"
                : $"{heldMember.Name}'s {RowLabel(heldRow - 1).ToLowerInvariant()} materia";
        }
        else if (heldSubmode == 0 && heldRow is 0 or 1 && heldColumn is >= 0 and < 8 &&
            TryReadCharacterMateria(heldCharacter, heldRow, heldColumn, out _, out var heldRaw))
        {
            var heldName = heldRaw == uint.MaxValue
                ? "an empty slot"
                : materia.TryDescribe(heldRaw, out var name, out _) ? name : null;
            if (heldName is null)
            {
                return false;
            }

            held = $"{heldName} from {heldMember.Name}'s {RowLabel(heldRow).ToLowerInvariant()} slot {heldColumn + 1}";
        }
        else
        {
            return false;
        }

        description = string.IsNullOrWhiteSpace(description) ? $"Holding {held}" : $"{description}. Holding {held}";
        holdingKey = $"{heldSubmode}:{heldCharacter}:{heldRow}:{heldColumn}";
        return true;
    }

    private bool TryDescribeInventory(int index, uint raw, string keyPrefix, out NativeMenuSelection selection)
    {
        selection = default;
        if (raw == uint.MaxValue)
        {
            selection = new NativeMenuSelection("Empty materia slot", null, $"{keyPrefix}:{index}:empty");
            return true;
        }

        if (!materia.TryDescribe(raw, out var name, out var description))
        {
            return false;
        }

        selection = new NativeMenuSelection(name, description, $"{keyPrefix}:{index}:{raw:X8}");
        return true;
    }

    private bool TryReadInventoryRow(uint widget, out int index, out uint raw)
    {
        index = -1;
        raw = 0;
        if (!TryReadRow(widget, 10, out var row) ||
            !memory.TryReadInt32(widget + WidgetScrollOffset, out var scroll) ||
            scroll is < 0 or >= MateriaInventoryCount)
        {
            return false;
        }

        index = row + scroll;
        return index < MateriaInventoryCount &&
            memory.TryReadUInt32(AddressMateriaInventory + (uint)(index * sizeof(uint)), out raw);
    }

    private bool TryReadExchangeSubmode(int expected) =>
        memory.TryReadInt32(AddressExchangeSubmode, out var submode) && submode == expected;

    /// <summary>The character whose panel the Exchange cursor is in: roster[top + offset].</summary>
    private bool TryReadViewedCharacter(out int character, out string name)
    {
        character = -1;
        name = string.Empty;
        if (!memory.TryReadInt32(AddressExchangeRosterTop, out var top) ||
            !memory.TryReadInt32(AddressExchangeRosterOffset, out var offset) ||
            top < 0 || offset is < 0 or > 3 || top + offset >= CharacterCount ||
            !memory.TryReadByte(AddressExchangeRoster + (uint)(top + offset), out var id) ||
            id >= CharacterCount ||
            !characters.TryReadCharacter(id, out var member) ||
            string.IsNullOrWhiteSpace(member.Name))
        {
            return false;
        }

        character = id;
        name = member.Name.Trim();
        return true;
    }

    private bool TryReadCharacterMateria(int character, int row, int column, out byte slotType, out uint raw)
    {
        slotType = 0;
        raw = uint.MaxValue;
        var record = AddressCharacterRecords + (uint)(character * CharacterRecordSize);
        if (!memory.TryReadByte(
                record + (uint)(row == 0 ? SavemapPartyReader.EquippedWeaponOffset : SavemapPartyReader.EquippedArmorOffset),
                out var equipmentId) ||
            !(row == 0 ? equipmentId < 128 : equipmentId < 32) ||
            !equipmentStats.TryReadMateriaSlot(row, equipmentId, column, out slotType))
        {
            return false;
        }

        // A position the equipment has no slot for holds nothing the screen shows.
        return slotType == 0 ||
            memory.TryReadUInt32(record + (uint)(row == 0 ? 0x40 : 0x60) + (uint)(column * sizeof(uint)), out raw);
    }

    private bool TryReadPartySlot(out int partySlot) =>
        memory.TryReadInt32(AddressPartySlot, out partySlot) && partySlot is >= 0 and < PartySlotCount;

    private bool TryReadRow(uint widget, int rows, out int row) =>
        memory.TryReadInt32(widget + WidgetCursorOffset, out row) && row >= 0 && row < rows;

    private bool TryReadMenuString(int row, out string text) =>
        TryReadString(AddressMenuStrings, MenuStringSize, row, out text);

    private bool TryReadString(uint table, int size, int row, out string text)
    {
        text = string.Empty;
        var bytes = new byte[size];
        if (!memory.TryRead(table + (uint)(row * size), bytes))
        {
            return false;
        }

        var terminator = Array.IndexOf(bytes, (byte)0xFF);
        if (terminator <= 0)
        {
            return false;
        }

        text = Ff7EncodedTextDecoder.DecodeTerminated(bytes.AsSpan(0, terminator + 1)).Trim();
        return text.Length > 0 && !text.Contains('�');
    }

    private static string RowLabel(int row) => row == 0 ? "Weapon" : "Armor";
}
