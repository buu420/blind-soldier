using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class MateriaMenuSelectionReader
{
    public const int AddressMenuMode = 0x00920FA0;
    public const int EquippedSlotMode = 1;
    public const int InventoryListMode = 3;
    public const int AddressDetailBufferPointer = 0x00DD12AC;
    public const int AddressSelectedMateriaId = 0x00DD12B0;
    public const int AddressMateriaSlotWidget = 0x00DD12F0;
    public const int AddressMateriaListWidget = 0x00DD1360;
    public const int AddressSelectedPartySlot = 0x00DD163C;
    public const int AddressMenuCharacterData = 0x00DCA810;
    public const int MenuCharacterDataSize = 0x84;
    public const int AddressMateriaInventory = 0x00DC04B4;

    private static readonly string[] EffectNames =
    [
        "Strength",
        "Vitality",
        "Magic",
        "Spirit",
        "Dexterity",
        "Luck",
        "Max HP",
        "Max MP"
    ];

    private readonly ILegacyAddressSpace memory;
    private readonly Func<int, string?> resolveMateriaName;
    private readonly Func<int, string?> resolveMateriaDescription;
    private readonly EquipmentStatReader equipmentStats;

    public MateriaMenuSelectionReader(
        ILegacyAddressSpace memory,
        Func<int, string?>? resolveMateriaName = null,
        Func<int, string?>? resolveMateriaDescription = null)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.resolveMateriaName = resolveMateriaName ?? (_ => null);
        this.resolveMateriaDescription = resolveMateriaDescription ?? (_ => null);
        equipmentStats = new EquipmentStatReader(memory);
    }

    public bool TryRead(MenuWidgetKind kind, out NativeMenuSelection selection)
    {
        selection = default;
        if (kind is not (MenuWidgetKind.MateriaSlot or MenuWidgetKind.MateriaList) ||
            !TryReadState(kind, out var state))
        {
            return false;
        }

        if (kind == MenuWidgetKind.MateriaSlot && state.SlotType == 0)
        {
            selection = new NativeMenuSelection(
                $"{GetPositionLabel(state.Row, state.Column)}, no slot",
                null,
                $"materia:{kind}:missing:{state.CharacterIndex}:{state.Row}:{state.Column}");
            return TryReadState(kind, out var missingBookend) && missingBookend == state;
        }

        var materiaId = (int)(state.RawMateria & 0xff);
        if (state.RawMateria == uint.MaxValue)
        {
            var emptyLabel = kind == MenuWidgetKind.MateriaSlot
                ? $"{GetSlotLabel(state.Row, state.Column)}, empty"
                : "Empty materia slot";
            selection = new NativeMenuSelection(
                emptyLabel,
                null,
                $"materia:{kind}:empty:{state.Column}:{state.Row}:{state.ListIndex}");
            return TryReadState(kind, out var emptyBookend) && emptyBookend == state;
        }

        if (materiaId == 0xff)
        {
            return false;
        }

        string? name;
        string? help;
        try
        {
            name = resolveMateriaName(materiaId);
            help = resolveMateriaDescription(materiaId);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var descriptionParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(help))
        {
            descriptionParts.Add(help.Trim());
        }

        var ap = state.RawMateria >> 8;
        descriptionParts.Add(ap == 0x00FF_FFFF ? "AP mastered" : $"AP {ap}");

        MateriaDetailSnapshot? detail = null;
        if (TryReadDetail(materiaId, out var detailSnapshot))
        {
            detail = detailSnapshot;
            descriptionParts.Add(
                ap == 0x00FF_FFFF
                    ? $"Mastered, level {detailSnapshot.CurrentLevel} of {detailSnapshot.MaximumLevel}"
                    : $"Level {detailSnapshot.CurrentLevel} of {detailSnapshot.MaximumLevel}");
            if (ap != 0x00FF_FFFF)
            {
                descriptionParts.Add($"To next level {detailSnapshot.ToNextLevel}");
            }

            var effects = detailSnapshot.Effects
                .Select((value, index) => FormatEffect(index, value))
                .Where(text => text is not null)
                .Select(text => text!)
                .ToArray();
            if (effects.Length > 0)
            {
                descriptionParts.Add($"Equip effect: {string.Join(", ", effects)}");
            }
        }

        if (!TryReadState(kind, out var bookend) || bookend != state)
        {
            return false;
        }

        var text = kind == MenuWidgetKind.MateriaSlot
            ? $"{GetSlotLabel(state.Row, state.Column)}, {name.Trim()}"
            : name.Trim();
        selection = new NativeMenuSelection(
            text,
            string.Join(". ", descriptionParts),
            $"materia:{kind}:{state.CharacterIndex}:{state.EquipmentId}:" +
            $"{state.Column}:{state.Row}:{state.ListIndex}:{state.RawMateria:X8}");
        return true;
    }

    private bool TryReadState(MenuWidgetKind kind, out MateriaSelectionState state)
    {
        state = default;
        if (!memory.TryReadInt32((uint)AddressMenuMode, out var mode))
        {
            return false;
        }

        if (kind == MenuWidgetKind.MateriaSlot)
        {
            if (mode != EquippedSlotMode ||
                !memory.TryReadInt32((uint)AddressSelectedPartySlot, out var characterIndex) ||
                characterIndex is < 0 or >= 9 ||
                !memory.TryReadInt32((uint)AddressMateriaSlotWidget, out var column) ||
                !memory.TryReadInt32((uint)(AddressMateriaSlotWidget + 4), out var row) ||
                column is < 0 or >= 8 ||
                row is < 0 or > 1 ||
                !memory.TryReadUInt32(
                    (uint)AddressMenuCharacterData,
                    out var characterDataPointer) ||
                characterDataPointer < 0x0001_0000)
            {
                return false;
            }

            var characterOffset =
                (uint)(characterIndex * MenuCharacterDataSize);
            if (characterDataPointer > uint.MaxValue - characterOffset)
            {
                return false;
            }

            var characterRecordAddress =
                characterDataPointer + characterOffset;
            if (characterRecordAddress > uint.MaxValue - MenuCharacterDataSize)
            {
                return false;
            }

            var equipmentOffset = row == 0
                ? SavemapPartyReader.EquippedWeaponOffset
                : SavemapPartyReader.EquippedArmorOffset;
            if (!memory.TryReadByte(
                    checked(characterRecordAddress + (uint)equipmentOffset),
                    out var equipmentId) ||
                (row == 0 ? equipmentId >= 128 : equipmentId >= 32) ||
                !equipmentStats.TryReadMateriaSlot(
                    row,
                    equipmentId,
                    column,
                    out var slotType))
            {
                return false;
            }

            var materiaRecordAddress = checked(
                characterRecordAddress +
                (uint)(row == 0 ? 0x40 : 0x60) +
                ((uint)column * sizeof(uint)));
            if (!memory.TryReadUInt32(materiaRecordAddress, out var rawMateria))
            {
                return false;
            }

            state = new MateriaSelectionState(
                mode,
                characterRecordAddress,
                characterIndex,
                column,
                row,
                -1,
                equipmentId,
                slotType,
                rawMateria);
            return true;
        }

        if (mode != InventoryListMode ||
            !memory.TryReadInt32((uint)(AddressMateriaListWidget + 4), out var cursor) ||
            !memory.TryReadInt32((uint)(AddressMateriaListWidget + 0x14), out var scroll) ||
            cursor is < 0 or >= 10 ||
            scroll is < 0 or >= 200)
        {
            return false;
        }

        var listIndex = cursor + scroll;
        if (listIndex is < 0 or >= 200 ||
            !memory.TryReadUInt32(
                checked((uint)(AddressMateriaInventory + (listIndex * sizeof(uint)))),
                out var listMateria))
        {
            return false;
        }

        state = new MateriaSelectionState(
            mode,
            0,
            -1,
            0,
            0,
            listIndex,
            -1,
            0,
            listMateria);
        return true;
    }

    private bool TryReadDetail(int materiaId, out MateriaDetailSnapshot detail)
    {
        detail = default;
        if (!memory.TryReadUInt32((uint)AddressSelectedMateriaId, out var selectedId) ||
            selectedId != (uint)materiaId ||
            !memory.TryReadUInt32((uint)AddressDetailBufferPointer, out var detailPointer) ||
            detailPointer < 0x0001_0000 ||
            !memory.TryReadByte(detailPointer, out var currentLevel) ||
            !memory.TryReadByte(detailPointer + 1, out var maximumLevel) ||
            currentLevel is < 1 or > 16 ||
            maximumLevel is < 1 or > 16 ||
            currentLevel > maximumLevel ||
            !memory.TryReadUInt32(detailPointer + 4, out var toNextLevel) ||
            toNextLevel > 100_000_000)
        {
            return false;
        }

        var effects = new short[EffectNames.Length];
        for (var index = 0; index < effects.Length; index++)
        {
            if (!memory.TryReadInt16(
                    checked(detailPointer + 10u + ((uint)index * sizeof(short))),
                    out effects[index]) ||
                effects[index] is < -1000 or > 1000)
            {
                return false;
            }
        }

        if (!memory.TryReadUInt32((uint)AddressSelectedMateriaId, out var idBookend) ||
            idBookend != selectedId ||
            !memory.TryReadUInt32((uint)AddressDetailBufferPointer, out var pointerBookend) ||
            pointerBookend != detailPointer)
        {
            return false;
        }

        detail = new MateriaDetailSnapshot(
            currentLevel,
            maximumLevel,
            toNextLevel,
            effects);
        return true;
    }

    private static string GetSlotLabel(int row, int column) =>
        $"{(row == 0 ? "Weapon" : "Armor")} materia slot {column + 1}";

    private static string GetPositionLabel(int row, int column) =>
        $"{(row == 0 ? "Weapon" : "Armor")} materia position {column + 1}";

    private static string? FormatEffect(int index, short value)
    {
        if (value == 0 || index < 0 || index >= EffectNames.Length)
        {
            return null;
        }

        var suffix = index >= 6 ? " percent" : string.Empty;
        var direction = value > 0 ? "plus" : "minus";
        return $"{EffectNames[index]} {direction} {Math.Abs((int)value)}{suffix}";
    }

    private readonly record struct MateriaSelectionState(
        int Mode,
        uint CharacterRecordAddress,
        int CharacterIndex,
        int Column,
        int Row,
        int ListIndex,
        int EquipmentId,
        byte SlotType,
        uint RawMateria);

    private readonly record struct MateriaDetailSnapshot(
        int CurrentLevel,
        int MaximumLevel,
        uint ToNextLevel,
        IReadOnlyList<short> Effects);
}
