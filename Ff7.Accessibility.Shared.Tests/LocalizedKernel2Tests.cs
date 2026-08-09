using System.Text;
using Ff7.Accessibility.Reloaded;

internal static class LocalizedKernel2Tests
{
    private static readonly int[] ExpectedCounts =
    [
        32, 256, 128, 128, 32, 32, 96, 64, 32,
        256, 128, 128, 32, 32, 96, 64, 128, 16
    ];

    public static void Run()
    {
        ReadsSectionsByStructureWithoutEnglishSignatures();
        RejectsWrongSectionCounts();
        RejectsOutOfBoundsOffsets();
    }

    private static void ReadsSectionsByStructureWithoutEnglishSignatures()
    {
        var french = Ff7GameLanguages.Get(Ff7GameLanguage.French);
        var decoded = BuildKernel2((section, index) => section switch
        {
            10 when index == 0 => new byte[] { 0x30, 0x4f, 0x54, 0x49, 0x4f, 0x4e, 0xff }, // Potion
            9 when index == 0 => new byte[] { 0x27, 0x55, 0x6e, 0x52, 0x49, 0xff }, // Guéri
            11 when index == 0 => EncodeAscii("Arme test"),
            12 when index == 0 => EncodeAscii("Armure test"),
            13 when index == 0 => EncodeAscii("Accessoire test"),
            14 when index == 0 => new byte[]
            {
                0x2d, 0x41, 0x54, 0x6e, 0x52, 0x49, 0x41,
                0x00, 0x54, 0x45, 0x53, 0x54, 0xff
            },
            16 when index == 0 => EncodeAscii("EXP"),
            _ => EncodeAscii($"S{section:D2}I{index:D3}")
        });

        var database = Kernel2TextDatabase.TryCreateFromDecodedKernel2(decoded, french)
            ?? throw new InvalidOperationException("localized structural KERNEL2 database was rejected");

        Equal("Potion", database.ResolveItemName(0), "item section 10");
        Equal("Guéri", database.ResolveSpellName(0), "ability section 9");
        Equal("Arme test", database.ResolveWeaponName(0), "weapon section 11");
        Equal("Armure test", database.ResolveArmorName(0), "armor section 12");
        Equal("Accessoire test", database.ResolveAccessoryName(0), "accessory section 13");
        Equal("Matéria test", database.ResolveMateriaName(0), "materia section 14");
        Equal("EXP", database.ResolveBattleText(0), "battle section 16");
        Equal("S02I000", database.ResolveItemDescription(0), "item help section 2");
    }

    private static void RejectsWrongSectionCounts()
    {
        var counts = ExpectedCounts.ToArray();
        counts[10] = 127;
        var decoded = BuildKernel2((section, index) => EncodeAscii($"S{section}I{index}"), counts);
        Equal(
            null,
            Kernel2TextDatabase.TryCreateFromDecodedKernel2(
                decoded,
                Ff7GameLanguages.Get(Ff7GameLanguage.English)),
            "wrong item count");
    }

    private static void RejectsOutOfBoundsOffsets()
    {
        var decoded = BuildKernel2((section, index) => EncodeAscii($"S{section}I{index}"));
        decoded[4] = 0xff;
        decoded[5] = 0x7f;
        Equal(
            null,
            Kernel2TextDatabase.TryCreateFromDecodedKernel2(
                decoded,
                Ff7GameLanguages.Get(Ff7GameLanguage.English)),
            "out-of-bounds first offset");
    }

    private static byte[] BuildKernel2(
        Func<int, int, byte[]> textFactory,
        int[]? counts = null)
    {
        counts ??= ExpectedCounts;
        using var output = new MemoryStream();
        for (var section = 0; section < counts.Length; section++)
        {
            var strings = Enumerable.Range(0, counts[section])
                .Select(index => textFactory(section, index))
                .ToArray();
            var tableSize = strings.Length * sizeof(ushort);
            var sectionSize = tableSize + strings.Sum(value => value.Length);
            output.Write(BitConverter.GetBytes(sectionSize));
            var relativeOffset = tableSize;
            foreach (var value in strings)
            {
                output.Write(BitConverter.GetBytes((ushort)relativeOffset));
                relativeOffset += value.Length;
            }

            foreach (var value in strings)
            {
                output.Write(value);
            }
        }

        return output.ToArray();
    }

    private static byte[] EncodeAscii(string value)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetByteCount(value) + 1);
        foreach (var character in value)
        {
            bytes.Add((byte)(character - 0x20));
        }

        bytes.Add(0xff);
        return bytes.ToArray();
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}
