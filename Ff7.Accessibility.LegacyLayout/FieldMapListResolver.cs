using System.Text;

namespace Ff7.Accessibility.Reloaded;

public static class FieldMapListResolver
{
    private const int CountSize = 2;
    private const int EntryStride = 32;

    public static IReadOnlyDictionary<int, string> ReadFieldNames(string mapListPath)
    {
        if (!File.Exists(mapListPath))
        {
            return new Dictionary<int, string>();
        }

        return ReadFieldNames(File.ReadAllBytes(mapListPath));
    }

    public static IReadOnlyDictionary<int, string> ReadFieldNames(byte[] bytes)
    {
        if (bytes.Length < CountSize)
        {
            return new Dictionary<int, string>();
        }

        var count = BitConverter.ToUInt16(bytes, 0);
        var names = new Dictionary<int, string>();
        for (var index = 0; index < count; index++)
        {
            var offset = CountSize + index * EntryStride;
            if (offset < CountSize || offset + EntryStride > bytes.Length)
            {
                break;
            }

            var name = ReadNullTerminatedAscii(bytes, offset, EntryStride);
            if (name.Length != 0)
            {
                names[index] = name;
            }
        }

        return names;
    }

    private static string ReadNullTerminatedAscii(byte[] bytes, int offset, int maxLength)
    {
        var length = 0;
        while (length < maxLength && bytes[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(bytes, offset, length).Trim();
    }
}
