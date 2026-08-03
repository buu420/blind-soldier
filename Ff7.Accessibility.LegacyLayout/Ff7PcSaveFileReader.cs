using System.Buffers.Binary;

namespace Ff7.Accessibility.Reloaded;

public static class Ff7PcSaveFileReader
{
    public const int HeaderSize = 9;
    public const int SlotSize = 4340;
    public const int SlotsPerFile = 15;
    public const int RuntimePreviewSize = 0x54;

    private const int ChecksumSize = sizeof(uint);
    private const int ChecksumPayloadLength = SlotSize - ChecksumSize;
    private const int LevelOffset = 0x04;
    private const int NameOffset = 0x08;
    private const int NameLength = 16;
    private const int CurrentHpOffset = 0x18;
    private const int MaxHpOffset = 0x1A;
    private const int CurrentMpOffset = 0x1C;
    private const int MaxMpOffset = 0x1E;
    private const int GilOffset = 0x20;
    private const int PlaySecondsOffset = 0x24;
    private const int LocationOffset = 0x28;
    private const int LocationLength = 32;

    public static bool TryReadSlot(string path, int slotNumber, out Ff7SaveSlotPreview preview)
    {
        preview = default;
        if (slotNumber is < 1 or > SlotsPerFile || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var offset = HeaderSize + (long)(slotNumber - 1) * SlotSize;
            if (stream.Length < offset + SlotSize)
            {
                return false;
            }

            var bytes = new byte[SlotSize];
            stream.Position = offset;
            stream.ReadExactly(bytes);
            return TryParseSlot(bytes, out preview);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TryParseSlot(ReadOnlySpan<byte> bytes, out Ff7SaveSlotPreview preview)
    {
        preview = default;
        if (bytes.Length != SlotSize)
        {
            return false;
        }

        if (IsEmpty(bytes))
        {
            preview = Ff7SaveSlotPreview.Empty;
            return true;
        }

        if (!HasValidChecksum(bytes))
        {
            return false;
        }

        return TryParsePreviewFields(bytes, out preview);
    }

    /// <summary>
    /// Parses the native 0x54-byte preview record used by the Save and Continue
    /// renderers. The live runtime can post-process a loaded slot after its disk
    /// checksum was verified, so the renderer's bounded preview cache is the
    /// authoritative presentation state inside the process.
    /// </summary>
    public static bool TryParseRuntimePreview(
        ReadOnlySpan<byte> bytes,
        out Ff7SaveSlotPreview preview)
    {
        preview = default;
        if (bytes.Length != RuntimePreviewSize)
        {
            return false;
        }

        if (IsEmpty(bytes))
        {
            preview = Ff7SaveSlotPreview.Empty;
            return true;
        }

        return TryParsePreviewFields(bytes, out preview);
    }

    private static bool TryParsePreviewFields(
        ReadOnlySpan<byte> bytes,
        out Ff7SaveSlotPreview preview)
    {
        preview = default;
        if (bytes.Length < LocationOffset + LocationLength)
        {
            return false;
        }

        var name = Ff7EncodedTextDecoder.DecodeTerminated(bytes.Slice(NameOffset, NameLength));
        var location = Ff7EncodedTextDecoder.DecodeTerminated(bytes.Slice(LocationOffset, LocationLength));
        var level = bytes[LevelOffset];
        var currentHp = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(CurrentHpOffset, 2));
        var maxHp = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(MaxHpOffset, 2));
        var currentMp = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(CurrentMpOffset, 2));
        var maxMp = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(MaxMpOffset, 2));
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location) ||
            level is 0 or > 99 || maxHp == 0 || currentHp > maxHp || currentMp > maxMp)
        {
            return false;
        }

        preview = new Ff7SaveSlotPreview(
            false,
            name,
            level,
            currentHp,
            maxHp,
            currentMp,
            maxMp,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(GilOffset, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(PlaySecondsOffset, 4)),
            location);
        return true;
    }

    private static bool HasValidChecksum(ReadOnlySpan<byte> slot)
    {
        if (slot.Length != SlotSize)
        {
            return false;
        }

        var stored = BinaryPrimitives.ReadUInt32LittleEndian(slot);
        var result = 0xFFFFu;
        foreach (var value in slot.Slice(ChecksumSize, ChecksumPayloadLength))
        {
            result ^= (uint)value << 8;
            for (var bit = 0; bit < 8; bit++)
            {
                result = (result & 0x8000) != 0
                    ? (result << 1) ^ 0x1021u
                    : result << 1;
            }

            result &= 0xFFFF;
        }

        var calculated = (result ^ 0xFFFF) & 0xFFFF;
        return stored == calculated;
    }

    private static bool IsEmpty(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }
}

public readonly record struct Ff7SaveSlotPreview(
    bool IsEmpty,
    string LeadCharacterName,
    int Level,
    int CurrentHp,
    int MaxHp,
    int CurrentMp,
    int MaxMp,
    uint Gil,
    uint PlaySeconds,
    string Location)
{
    public static Ff7SaveSlotPreview Empty { get; } =
        new(true, string.Empty, 0, 0, 0, 0, 0, 0, 0, string.Empty);
}
