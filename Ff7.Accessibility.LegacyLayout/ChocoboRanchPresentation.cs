using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Values displayed by farm_2's live birds, selector and number window.</summary>
public static class ChocoboRanchPresentation
{
    public const int FieldId = 345;
    public const int FeedOrNutChoiceCount = 9;

    private const uint TemporaryBank = 0x00CC14D0;
    private const uint SavedBankB = 0x00DC0ADC;
    private const uint SavedBankF = 0x00DC0CDC;
    private const uint EventTablePointer = 0x00CC0B60;
    private const uint EntityModelIds = 0x00CBFB70;
    private const uint ModelStride = 0x88;
    private const uint ModelVisible = 0x62;

    private static readonly (string Entity, uint Palette)[] Stalls =
    [
        ("choko1", SavedBankB + 0x2F), ("choko2", SavedBankB + 0x3F),
        ("choko3", SavedBankB + 0x4F), ("choko4", SavedBankB + 0x5F),
        ("choko5", SavedBankF + 0xEF), ("choko6", SavedBankF + 0xFF)
    ];

    public static string? ReadStableColor(ILegacyAddressSpace memory, int field, string entityName)
    {
        if (field != FieldId)
        {
            return null;
        }

        foreach (var (entity, palette) in Stalls)
        {
            if (entityName == entity && memory.TryReadByte(palette, out var color))
            {
                return ColorName(color);
            }
        }

        return null;
    }

    internal static string? ReadVisibleNewbornColor(ILegacyAddressSpace memory)
    {
        // ray waits for chokobo (entity 13) to finish its native color change
        // before displaying the birth message. Its KAWAI palette uses bank 5:3F.
        return memory.TryReadByte(EntityModelIds + 13, out var model) &&
            memory.TryReadByte((uint)FieldPositionReader.AddressFieldNumModels, out var modelCount) &&
            model < modelCount && memory.TryReadUInt32(EventTablePointer, out var events) &&
            events != 0 && events <= uint.MaxValue - model * ModelStride - ModelVisible &&
            memory.TryReadByte(events + model * ModelStride + ModelVisible, out var visible) &&
            visible != 0 && memory.TryReadByte(TemporaryBank + 0x3F, out var color)
                ? ColorName(color)
                : null;
    }

    // Ghidra 0061FD5C/0061FE26: WSPCL sets mode and WNUMB stores the digit
    // count and the rendered integer in the same 0x30-byte window record.
    internal static bool TryReadFeedingQuantity(ILegacyAddressSpace memory, out uint amount)
    {
        const uint window = 3 * FieldAudibleCueOwnershipStateReader.FieldWindowLifecycleStride;
        amount = 0;
        return memory.TryReadByte(0x00CFF5D3 + window, out var mode) && mode == 2 &&
            memory.TryReadByte(0x00CFF5D5 + window, out var digits) && digits == 2 &&
            memory.TryReadUInt32(0x00CFF5D8 + window, out amount) && amount <= 99;
    }

    // Ghidra 006310A1: after ASK draws the native pointer, its row is at y = 16*n + 6.
    internal static bool TryReadFeedOrNutCursor(ILegacyAddressSpace memory, out int row)
    {
        const uint window = FieldAudibleCueOwnershipStateReader.FieldWindowLifecycleStride;
        row = -1;
        if (!memory.TryReadUInt16(0x00CFF5DC + window, out var x) || x != 5 ||
            !memory.TryReadUInt16(0x00CFF5DE + window, out var y) || y < 6 || (y - 6) % 16 != 0)
        {
            return false;
        }

        row = (y - 6) / 16;
        return row < FeedOrNutChoiceCount;
    }

    // The ranch lists bind the eight visible counts through MPARA (0061F2B0).
    internal static bool TryReadChoiceCount(ILegacyAddressSpace memory, int row, out byte count)
    {
        var index = (uint)row;
        count = 0;
        return memory.TryReadByte(0x00CBFBE0 + 8 + index, out var bank) && bank == 5 &&
            memory.TryReadUInt16(0x00CC08A0 + 16 + 2 * index, out var variable) && variable <= byte.MaxValue &&
            memory.TryReadByte(TemporaryBank + variable, out count) && count <= 99;
    }

    private static string? ColorName(byte color) => color switch
    {
        0 => "Yellow",
        1 => "Green",
        2 => "Blue",
        3 => "Black",
        4 => "Gold",
        _ => null
    };
}
