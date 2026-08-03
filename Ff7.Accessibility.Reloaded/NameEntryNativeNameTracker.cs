namespace Ff7.Accessibility.Reloaded;

public sealed class NameEntryNativeNameTracker
{
    public const int AddressMenuState = NameEntryStateReader.AddressMenuState;
    public const int AddressNameBuffer = NameEntryStateReader.AddressNameBuffer;
    public const int AddressSelectedSlot = NameEntryStateReader.AddressSelectedSlot;
    public const int AddressGridColumn = NameEntryStateReader.AddressGridColumn;
    public const int AddressGridRow = NameEntryStateReader.AddressGridRow;
    public const int AddressCommandRow = NameEntryStateReader.AddressCommandRow;
    public const int AddressFocus = NameEntryStateReader.AddressFocus;
    public const int NameSlotCount = NameEntryStateReader.NameSlotCount;
    public const int RecommendedScanIntervalMs = 10;

    private static readonly string[] Commands = ["Space", "Delete", "Select", "Default"];

    private readonly byte[] previousBuffer = new byte[NameSlotCount];
    private readonly TimeSpan initialAnnouncementDelay;
    private DateTime activatedAt;
    private bool initialized;
    private bool initialAnnouncementPending;
    private int previousCommandRow;
    private int previousFocus;
    private int previousGridColumn;
    private int previousGridRow;
    private int previousSelectedSlot;

    public NameEntryNativeNameTracker(TimeSpan initialAnnouncementDelay)
    {
        this.initialAnnouncementDelay = initialAnnouncementDelay;
    }

    public static bool IsNameEntryActive(int currentModule, int menuState) =>
        currentModule == NameEntryStateReader.NameEntryModule && menuState == 1;

    public string? Observe(
        bool active,
        int focus,
        int gridColumn,
        int gridRow,
        int commandRow,
        int selectedSlot,
        IReadOnlyList<byte> buffer,
        DateTime now)
    {
        if (!active || buffer.Count < NameSlotCount)
        {
            Reset();
            return null;
        }

        var normalizedSlot = Math.Clamp(selectedSlot, 0, NameSlotCount - 1);
        if (!initialized)
        {
            CopyBuffer(buffer);
            previousFocus = focus;
            previousGridColumn = gridColumn;
            previousGridRow = gridRow;
            previousCommandRow = commandRow;
            previousSelectedSlot = normalizedSlot;
            activatedAt = now;
            initialAnnouncementPending = true;
            initialized = true;
            return null;
        }

        var changedSlots = new List<int>();
        for (var slot = 0; slot < NameSlotCount; slot++)
        {
            if (previousBuffer[slot] != GetEffectiveBufferValue(buffer, slot))
            {
                changedSlots.Add(slot);
            }
        }

        string? speech = null;
        if (changedSlots.Count != 0)
        {
            speech = string.Join(" ", changedSlots.Select(slot => DescribeSlot(slot, GetEffectiveBufferValue(buffer, slot))));
            initialAnnouncementPending = false;
        }
        else if (normalizedSlot != previousSelectedSlot)
        {
            speech = DescribeSlot(normalizedSlot, GetEffectiveBufferValue(buffer, normalizedSlot));
            initialAnnouncementPending = false;
        }
        else if (focus != previousFocus ||
                 gridColumn != previousGridColumn ||
                 gridRow != previousGridRow ||
                 commandRow != previousCommandRow)
        {
            speech = DescribeSelection(focus, gridColumn, gridRow, commandRow);
        }
        else if (initialAnnouncementPending && now - activatedAt >= initialAnnouncementDelay)
        {
            var selection = DescribeSelection(focus, gridColumn, gridRow, commandRow);
            speech = selection is null
                ? DescribeNameField(buffer)
                : $"{DescribeNameField(buffer)} {selection}";
            initialAnnouncementPending = false;
        }

        CopyBuffer(buffer);
        previousFocus = focus;
        previousGridColumn = gridColumn;
        previousGridRow = gridRow;
        previousCommandRow = commandRow;
        previousSelectedSlot = normalizedSlot;
        return speech;
    }

    public void Reset()
    {
        initialized = false;
        initialAnnouncementPending = false;
        activatedAt = DateTime.MinValue;
        previousFocus = 0;
        previousGridColumn = 0;
        previousGridRow = 0;
        previousCommandRow = 0;
        previousSelectedSlot = 0;
        Array.Fill(previousBuffer, (byte)0xFF);
    }

    private static string DescribeNameField(IReadOnlyList<byte> buffer)
    {
        var encodedName = new byte[NameSlotCount + 1];
        for (var slot = 0; slot < NameSlotCount; slot++)
        {
            encodedName[slot] = GetEffectiveBufferValue(buffer, slot);
        }

        encodedName[^1] = 0xFF;
        var name = Ff7EncodedTextDecoder.Decode(encodedName);
        return string.IsNullOrEmpty(name)
            ? "Current name: empty."
            : $"Current name: {name}.";
    }

    private static string? DescribeSelection(int focus, int gridColumn, int gridRow, int commandRow)
    {
        if (focus == 0 && NameEntryCharacterTable.TryGet(gridColumn, gridRow, out var character))
        {
            return $"Character grid: {character}.";
        }

        if (focus == 1 && commandRow >= 0 && commandRow < Commands.Length)
        {
            return $"Command: {Commands[commandRow]}.";
        }

        return null;
    }

    private static string DescribeSlot(int slot, byte value)
    {
        if (value == 0xFF)
        {
            return $"Name slot {slot + 1}: empty.";
        }

        var decoded = Ff7EncodedTextDecoder.Decode([value, 0xFF]);
        var spoken = decoded switch
        {
            _ when value == 0x00 => "space",
            [var letter] when char.IsUpper(letter) => $"capital {letter}",
            [var letter] when char.IsLower(letter) => $"lowercase {letter}",
            "," => "comma",
            "." => "period",
            "+" => "plus",
            "-" => "minus",
            ":" => "colon",
            ";" => "semicolon",
            "" => $"character code {value}",
            _ => decoded
        };
        return $"Name slot {slot + 1}: {spoken}.";
    }

    private void CopyBuffer(IReadOnlyList<byte> buffer)
    {
        for (var slot = 0; slot < NameSlotCount; slot++)
        {
            previousBuffer[slot] = GetEffectiveBufferValue(buffer, slot);
        }
    }

    private static byte GetEffectiveBufferValue(IReadOnlyList<byte> buffer, int slot)
    {
        for (var index = 0; index < slot; index++)
        {
            if (buffer[index] == 0xFF)
            {
                return 0xFF;
            }
        }

        return buffer[slot];
    }
}
