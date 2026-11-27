using System.Security.Cryptography;
using System.Text;

namespace RussianFieldAudit;

/// <summary>
/// One decoded field file split into its native sections (0 script, 1 camera, 2 model loader,
/// 3 palette, 4 walkmesh, 5 tile map, 6 encounters, 7 triggers and gateways, 8 background).
/// </summary>
internal sealed class FieldSections
{
    public const int Script = 0;
    public const int ModelLoader = 2;
    public static readonly string[] Names =
        ["script", "camera", "model-loader", "palette", "walkmesh", "tilemap", "encounter", "triggers", "background"];

    private FieldSections(byte[] file, byte[][] sections)
    {
        File = file;
        Sections = sections;
    }

    public byte[] File { get; }
    public byte[][] Sections { get; }

    public static FieldSections? TryRead(byte[] file)
    {
        if (file.Length < 6) return null;
        var count = BitConverter.ToInt32(file, 2);
        if (count is < 1 or > 16 || 6 + count * 4 > file.Length) return null;
        var sections = new byte[count][];
        for (var index = 0; index < count; index++)
        {
            var offset = BitConverter.ToInt32(file, 6 + index * 4);
            if (offset < 0 || offset > file.Length - 4) return null;
            var length = BitConverter.ToInt32(file, offset);
            if (length < 0 || offset + 4 > file.Length - length) return null;
            sections[index] = file.AsSpan(offset + 4, length).ToArray();
        }

        return new FieldSections(file, sections);
    }

    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>The runtime identity: section 1 through the text offset (LoadedFieldScriptIdentityReader).</summary>
    public string PrefixSha256 => Sha256(Sections[Script].AsSpan(0, BitConverter.ToUInt16(Sections[Script], 4)));

    public string SectionSha256(int index) => Sha256(Sections[index]);

    /// <summary>Script header bytes 0-31 other than the text offset, which follows code length.</summary>
    public string HeaderWithoutTextOffset
    {
        get
        {
            var header = Sections[Script].AsSpan(0, 32).ToArray();
            header[4] = header[5] = 0;
            return Convert.ToHexString(header);
        }
    }

    public int TextOffset => BitConverter.ToUInt16(Sections[Script], 4);
    public int AkaoCount => BitConverter.ToUInt16(Sections[Script], 6);

    /// <summary>
    /// The model loader records as FieldScriptNavigationCatalog.ReadModelResourceNames walks them:
    /// the resource name, the fixed 46-byte tail and each animation name.
    /// </summary>
    public IReadOnlyList<ModelRecord>? ModelRecords()
    {
        var section = Sections[ModelLoader];
        if (section.Length < 6) return null;
        var count = BitConverter.ToUInt16(section, 2);
        var position = 6;
        var records = new List<ModelRecord>(count);
        for (var model = 0; model < count; model++)
        {
            if (position + 2 > section.Length) return null;
            var nameLength = BitConverter.ToUInt16(section, position);
            position += 2;
            if (position + nameLength + 46 > section.Length) return null;
            var name = Encoding.ASCII.GetString(section, position, nameLength).TrimEnd('\0');
            position += nameLength;
            var tail = Convert.ToHexString(section, position, 46);
            var animationCount = BitConverter.ToUInt16(section, position + 14);
            position += 46;
            var animations = new List<string>(animationCount);
            for (var animation = 0; animation < animationCount; animation++)
            {
                if (position + 2 > section.Length) return null;
                var length = BitConverter.ToUInt16(section, position);
                position += 2;
                if (position + length + 2 > section.Length) return null;
                animations.Add(Encoding.ASCII.GetString(section, position, length).TrimEnd('\0'));
                position += length + 2;
            }

            records.Add(new ModelRecord(name, tail, animations));
        }

        ModelTrailingBytes = section.Length - position;
        return records;
    }

    /// <summary>Bytes after the last model record (the runtime walk ignores them).</summary>
    public int ModelTrailingBytes { get; private set; }
}

internal sealed record ModelRecord(string Name, string TailHex, IReadOnlyList<string> Animations);
