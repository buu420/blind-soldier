namespace Ff7.Accessibility.Reloaded;

public sealed class ConfigMenuValueReader
{
    public const int AddressCurrentRow = 0x00DC10F0;
    public const int AddressBattleSpeed = 0x00DC0E10;
    public const int AddressBattleMessageSpeed = 0x00DC0E11;
    public const int AddressSettingsBits = 0x00DC0E12;
    public const int AddressFieldMessageSpeed = 0x00DC0E24;
    public const int AddressSoundModalState = 0x0091A884;
    public const int AddressMusicVolume = 0x00DC10D0;
    public const int AddressSoundEffectsVolume = 0x00DC10E8;
    public const int SoundModalActiveState = 10;

    private readonly Func<int, byte>? readByte;
    private readonly Func<int, ushort>? readUInt16;
    private readonly Func<int, int>? readInt32;
    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace? addressSpace;

    public ConfigMenuValueReader(
        Func<int, byte> readByte,
        Func<int, ushort> readUInt16,
        Func<int, int> readInt32)
    {
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
        this.readUInt16 = readUInt16 ?? throw new ArgumentNullException(nameof(readUInt16));
        this.readInt32 = readInt32 ?? throw new ArgumentNullException(nameof(readInt32));
    }

    public ConfigMenuValueReader(Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public NativeMenuSelection? ReadMainValue(string nativeRowLabel)
    {
        if (string.IsNullOrWhiteSpace(nativeRowLabel))
        {
            return null;
        }

        var label = nativeRowLabel.Trim();
        if (label.Equals("Battle speed", StringComparison.OrdinalIgnoreCase))
        {
            return ReadSlider(label, AddressBattleSpeed);
        }

        if (label.Equals("Battle message", StringComparison.OrdinalIgnoreCase))
        {
            return ReadSlider(label, AddressBattleMessageSpeed);
        }

        if (label.Equals("Field message", StringComparison.OrdinalIgnoreCase))
        {
            return ReadSlider(label, AddressFieldMessageSpeed);
        }

        if (!TryReadUInt16(AddressSettingsBits, out var settings))
        {
            return null;
        }
        if (label.Equals("Controller", StringComparison.OrdinalIgnoreCase))
        {
            return ReadChoice(label, (settings >> 2) & 0x03, "Normal", "Customize");
        }

        if (label.Equals("Cursor", StringComparison.OrdinalIgnoreCase))
        {
            return ReadChoice(label, (settings >> 4) & 0x03, "Initial", "Memory");
        }

        if (label.Equals("ATB", StringComparison.OrdinalIgnoreCase))
        {
            return ReadChoice(label, (settings >> 6) & 0x03, "Active", "Recommended", "Wait");
        }

        if (label.Equals("Camera angle", StringComparison.OrdinalIgnoreCase))
        {
            return ReadChoice(label, (settings >> 8) & 0x03, "Auto", "Fixed");
        }

        return null;
    }

    public NativeMenuSelection? ReadCurrentMainValue(string nativeRowLabel)
    {
        if (string.IsNullOrWhiteSpace(nativeRowLabel) ||
            !TryReadInt32(AddressCurrentRow, out var rowIndex))
        {
            return null;
        }

        var label = nativeRowLabel.Trim();
        var selection = rowIndex switch
        {
            2 => ReadSettingsChoice(label, 2, "Normal", "Customize"),
            3 => ReadSettingsChoice(label, 4, "Initial", "Memory"),
            4 => ReadSettingsChoice(label, 6, "Active", "Recommended", "Wait"),
            5 => ReadSlider(label, AddressBattleSpeed),
            6 => ReadSlider(label, AddressBattleMessageSpeed),
            7 => ReadSlider(label, AddressFieldMessageSpeed),
            8 => ReadSettingsChoice(label, 8, "Auto", "Fixed"),
            _ => null
        };
        if (selection is null ||
            !TryReadInt32(AddressCurrentRow, out var rowBookend) ||
            rowBookend != rowIndex)
        {
            return null;
        }

        return selection;
    }

    public NativeMenuSelection? ReadSoundVolume(int cursor)
    {
        if (!TryReadInt32(AddressSoundModalState, out var modalState) || modalState != SoundModalActiveState)
        {
            return null;
        }

        var label = cursor switch
        {
            0 => "Music volume",
            1 => "Sound effects volume",
            _ => null
        };
        if (label is null)
        {
            return null;
        }

        var address = cursor == 0 ? AddressMusicVolume : AddressSoundEffectsVolume;
        if (!TryReadInt32(address, out var value))
        {
            return null;
        }

        if (value is < 0 or > 100)
        {
            return null;
        }

        if (addressSpace is not null &&
            (!TryReadInt32(AddressSoundModalState, out var modalStateBookend) || modalStateBookend != modalState))
        {
            return null;
        }

        return new NativeMenuSelection(
            $"{label}, {value} percent",
            null,
            $"config-sound:{cursor}:{value}");
    }

    private NativeMenuSelection? ReadSlider(string label, int address)
    {
        if (!TryReadByte(address, out var raw))
        {
            return null;
        }

        var percent = (raw * 100 + 127) / 255;
        return new NativeMenuSelection(
            $"{percent} percent from Fast to Slow",
            null,
            $"config:{label}:{raw}");
    }

    private static NativeMenuSelection? ReadChoice(string label, int index, params string[] choices)
    {
        if (index < 0 || index >= choices.Length)
        {
            return null;
        }

        return new NativeMenuSelection(
            choices[index],
            null,
            $"config:{label}:{index}");
    }

    private NativeMenuSelection? ReadSettingsChoice(
        string label,
        int shift,
        params string[] choices)
    {
        if (!TryReadUInt16(AddressSettingsBits, out var settings))
        {
            return null;
        }

        return ReadChoice(label, (settings >> shift) & 0x03, choices);
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

        value = readUInt16!(address);
        return true;
    }

    private bool TryReadInt32(int address, out int value)
    {
        if (addressSpace is not null)
        {
            return Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadInt32(
                addressSpace,
                (uint)address,
                out value);
        }

        value = readInt32!(address);
        return true;
    }
}
