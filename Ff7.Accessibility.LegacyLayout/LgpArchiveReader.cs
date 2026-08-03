using System.Text;

namespace Ff7.Accessibility.Reloaded;

public sealed class LgpArchiveReader
{
    private const int HeaderSize = 16;
    private const int EntrySize = 27;
    private const int NameSize = 20;
    private const int DataHeaderSize = 24;

    private readonly string archivePath;
    private readonly IReadOnlyDictionary<string, Entry> entries;

    public LgpArchiveReader(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("LGP archive is missing.", archivePath);
        }

        this.archivePath = Path.GetFullPath(archivePath);
        entries = ReadTableOfContents(this.archivePath);
    }

    public bool ContainsFile(string name) => entries.ContainsKey(name);

    public bool TryReadFile(string name, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!entries.TryGetValue(name, out var entry))
        {
            return false;
        }

        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        if (entry.Offset < 0 || entry.Offset > stream.Length - DataHeaderSize)
        {
            throw new InvalidDataException($"LGP entry {entry.Name} has an invalid data offset {entry.Offset}.");
        }

        stream.Position = entry.Offset;
        var storedName = ReadFixedAscii(reader.ReadBytes(NameSize));
        if (!storedName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"LGP entry {entry.Name} points to a mismatched data header named {storedName}.");
        }

        var size = reader.ReadUInt32();
        if (size > int.MaxValue || size > stream.Length - stream.Position)
        {
            throw new InvalidDataException($"LGP entry {entry.Name} has invalid size {size}.");
        }

        bytes = reader.ReadBytes((int)size);
        if (bytes.Length != (int)size)
        {
            throw new EndOfStreamException($"LGP entry {entry.Name} ended before all {size} bytes were read.");
        }

        return true;
    }

    private static IReadOnlyDictionary<string, Entry> ReadTableOfContents(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        if (stream.Length < HeaderSize)
        {
            throw new InvalidDataException($"LGP archive is smaller than its header: {path}");
        }

        var creator = ReadFixedAscii(reader.ReadBytes(12));
        if (!creator.Equals("SQUARESOFT", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"LGP archive has an unsupported creator header '{creator}': {path}");
        }

        var count = reader.ReadInt32();
        var maximumCount = (stream.Length - HeaderSize) / EntrySize;
        if (count <= 0 || count > maximumCount)
        {
            throw new InvalidDataException($"LGP archive has invalid file count {count}: {path}");
        }

        var result = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < count; index++)
        {
            var name = ReadFixedAscii(reader.ReadBytes(NameSize));
            var offset = reader.ReadUInt32();
            _ = reader.ReadByte();
            _ = reader.ReadUInt16();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!result.ContainsKey(name))
            {
                result[name] = new Entry(name, offset);
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException($"LGP archive contains no readable entries: {path}");
        }

        return result;
    }

    private static string ReadFixedAscii(byte[] bytes)
    {
        return Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
    }

    private readonly record struct Entry(string Name, long Offset);
}
