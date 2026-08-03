using System.Text;

namespace Ff7.Accessibility.Reloaded;

public static class FlevelFieldNameResolver
{
    private const int HeaderSize = 0x10;
    private const int FileCountOffset = 0x0C;
    private const int EntryStride = 27;
    private const int EntryNameLength = 20;

    public static IReadOnlyDictionary<int, string> ReadFieldNames(string lgpPath)
    {
        if (!File.Exists(lgpPath))
        {
            return new Dictionary<int, string>();
        }

        var bytes = File.ReadAllBytes(lgpPath);
        if (bytes.Length < HeaderSize)
        {
            return new Dictionary<int, string>();
        }

        var count = BitConverter.ToInt32(bytes, FileCountOffset);
        if (count <= 0)
        {
            return new Dictionary<int, string>();
        }

        var names = new Dictionary<int, string>();
        for (var index = 0; index < count; index++)
        {
            var entryOffset = HeaderSize + index * EntryStride;
            if (entryOffset < HeaderSize || entryOffset + EntryNameLength > bytes.Length)
            {
                break;
            }

            var name = ReadNullTerminatedAscii(bytes, entryOffset, EntryNameLength);
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
