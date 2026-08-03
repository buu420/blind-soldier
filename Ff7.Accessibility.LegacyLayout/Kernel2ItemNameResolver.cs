namespace Ff7.Accessibility.Reloaded;

public sealed class Kernel2ItemNameResolver
{
    private readonly byte[] decodedKernel2;
    private readonly int tableBase;
    private readonly int sectionEnd;
    private readonly int itemCount;

    private Kernel2ItemNameResolver(byte[] decodedKernel2, int tableBase, int sectionEnd, int itemCount)
    {
        this.decodedKernel2 = decodedKernel2;
        this.tableBase = tableBase;
        this.sectionEnd = sectionEnd;
        this.itemCount = itemCount;
    }

    public int ItemCount => itemCount;

    public string? ResolveName(int itemId)
    {
        if (itemId < 0 || itemId >= itemCount)
        {
            return null;
        }

        var relativeOffset = ReadUInt16(decodedKernel2, tableBase + (itemId * sizeof(ushort)));
        var address = tableBase + relativeOffset;
        if (address < tableBase || address >= sectionEnd)
        {
            return null;
        }

        var maxLength = sectionEnd - address;
        var text = Ff7EncodedTextDecoder.DecodeTerminated(decodedKernel2.AsSpan(address, maxLength));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static Kernel2ItemNameResolver? TryCreate(string gameRootDirectory, Action<string>? log = null)
    {
        var path = Path.Combine(gameRootDirectory, "data", "lang-en", "kernel", "kernel2.bin");
        if (!File.Exists(path))
        {
            log?.Invoke($"kernel2 item names unavailable; missing {path}");
            return null;
        }

        try
        {
            var decoded = Ff7LzsDecoder.DecodeFieldFile(File.ReadAllBytes(path));
            var resolver = TryCreateFromDecodedKernel2(decoded);
            if (resolver is null)
            {
                log?.Invoke($"kernel2 item names unavailable; item-name section was not found in {path}");
                return null;
            }

            log?.Invoke($"kernel2 item names loaded from {path}; count={resolver.ItemCount}");
            return resolver;
        }
        catch (Exception ex)
        {
            log?.Invoke($"kernel2 item names unavailable; {ex.Message}");
            return null;
        }
    }

    internal static Kernel2ItemNameResolver? TryCreateFromDecodedKernel2(byte[] decoded)
    {
        for (var start = 0; start <= decoded.Length - 8; start++)
        {
            var sectionSize = ReadInt32(decoded, start);
            if (sectionSize < 32 || start + sectionSize > decoded.Length)
            {
                continue;
            }

            var tableBase = start + sizeof(int);
            var firstStringOffset = ReadUInt16(decoded, tableBase);
            if (firstStringOffset < sizeof(ushort) || firstStringOffset >= sectionSize - sizeof(int))
            {
                continue;
            }

            var itemCount = firstStringOffset / sizeof(ushort);
            if (itemCount is < 32 or > 512)
            {
                continue;
            }

            var sectionEnd = start + sectionSize;
            var first = DecodeString(decoded, tableBase + firstStringOffset, sectionEnd);
            var fourth = itemCount > 3
                ? DecodeString(decoded, tableBase + ReadUInt16(decoded, tableBase + (3 * sizeof(ushort))), sectionEnd)
                : string.Empty;
            var eighth = itemCount > 7
                ? DecodeString(decoded, tableBase + ReadUInt16(decoded, tableBase + (7 * sizeof(ushort))), sectionEnd)
                : string.Empty;

            if (string.Equals(first, "Potion", StringComparison.Ordinal) &&
                string.Equals(fourth, "Ether", StringComparison.Ordinal) &&
                string.Equals(eighth, "Phoenix Down", StringComparison.Ordinal))
            {
                return new Kernel2ItemNameResolver(decoded, tableBase, sectionEnd, itemCount);
            }
        }

        return null;
    }

    private static string DecodeString(byte[] decoded, int address, int sectionEnd)
    {
        if (address < 0 || address >= sectionEnd)
        {
            return string.Empty;
        }

        return Ff7EncodedTextDecoder.DecodeTerminated(decoded.AsSpan(address, sectionEnd - address));
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        bytes[offset] |
        (bytes[offset + 1] << 8) |
        (bytes[offset + 2] << 16) |
        (bytes[offset + 3] << 24);

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
}
