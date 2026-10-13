using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class Kernel2TextDatabase
{
    private Ff7.Accessibility.LegacyLayout.NativeKernel2TextReader? nativeText;
    private PostscriptMateriaDropCompatibility? materiaDropCompatibility;

    /// <summary>Use the text actually loaded by the game, including virtual-file overrides.</summary>
    public void UseNativeText(Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace memory,
        Ff7GameLanguageDescriptor? language = null)
    {
        nativeText = new Ff7.Accessibility.LegacyLayout.NativeKernel2TextReader(memory, language);
        materiaDropCompatibility = new PostscriptMateriaDropCompatibility(memory);
    }

    public void UseVerifiedNativeText(Func<int, int, byte[]?> readEncoded, Func<bool> ownsLookup)
    {
        if (nativeText is null) throw new InvalidOperationException("A native address space must be configured first.");
        nativeText.BindVerifiedBackend(readEncoded, ownsLookup);
    }

    private string? ResolveText(int bank, int id, IndexedTextSection disk) =>
        nativeText is { HasSupportedLookup: true } live
            ? live.TryRead(bank, id, out var text) ? text : null
            : disk.Resolve(id);
    private static readonly int[] ExpectedSectionCounts =
    [
        32, 256, 128, 128, 32, 32, 96, 64, 32,
        256, 128, 128, 32, 32, 96, 64, 128, 16
    ];

    private readonly IndexedTextSection itemNames;
    private readonly IndexedTextSection itemDescriptions;
    private readonly IndexedTextSection spellNames;
    private readonly IndexedTextSection spellDescriptions;
    private readonly IndexedTextSection weaponNames;
    private readonly IndexedTextSection weaponDescriptions;
    private readonly IndexedTextSection armorNames;
    private readonly IndexedTextSection armorDescriptions;
    private readonly IndexedTextSection accessoryNames;
    private readonly IndexedTextSection accessoryDescriptions;
    private readonly IndexedTextSection commandNames;
    private readonly IndexedTextSection commandDescriptions;
    private readonly IndexedTextSection materiaNames;
    private readonly IndexedTextSection materiaDescriptions;
    private readonly IndexedTextSection battleTexts;
    private readonly IndexedTextSection summonAttackNames;

    private Kernel2TextDatabase(IReadOnlyList<IndexedTextSection> sections)
    {
        commandNames = sections[8];
        spellNames = sections[9];
        itemNames = sections[10];
        weaponNames = sections[11];
        armorNames = sections[12];
        accessoryNames = sections[13];
        materiaNames = sections[14];
        battleTexts = sections[16];
        summonAttackNames = sections[17];

        commandDescriptions = sections[0];
        spellDescriptions = sections[1];
        itemDescriptions = sections[2];
        weaponDescriptions = sections[3];
        armorDescriptions = sections[4];
        accessoryDescriptions = sections[5];
        materiaDescriptions = sections[6];
    }

    public string? ResolveItemName(int id) => ResolveText(10, id, itemNames);

    public string? ResolveItemDescription(int id) => ResolveText(2, id, itemDescriptions);

    public string? ResolveSpellName(int id) => ResolveText(9, id, spellNames);

    public string? ResolveSpellDescription(int id) => ResolveText(1, id, spellDescriptions);

    public string? ResolveBattleActionName(int rawActionId) =>
        ResolveBattleActionText(spellNames, rawActionId, 9);

    public string? ResolveBattleActionDescription(int rawActionId) =>
        ResolveBattleActionText(spellDescriptions, rawActionId, 1);

    public string? ResolveWeaponName(int id) => ResolveText(11, id, weaponNames);

    public string? ResolveWeaponDescription(int id) => ResolveText(3, id, weaponDescriptions);

    public string? ResolveArmorName(int id) => ResolveText(12, id, armorNames);

    public string? ResolveArmorDescription(int id) => ResolveText(4, id, armorDescriptions);

    public string? ResolveAccessoryName(int id) => ResolveText(13, id, accessoryNames);

    public string? ResolveAccessoryDescription(int id) => ResolveText(5, id, accessoryDescriptions);

    public string? ResolveCommandName(int id) => ResolveText(8, id, commandNames);

    public string? ResolveCommandDescription(int id) => ResolveText(0, id, commandDescriptions);

    public string? ResolveMateriaName(int id) => ResolveText(14, id, materiaNames);

    public string? ResolveMateriaDescription(int id) => ResolveText(6, id, materiaDescriptions);

    public string? ResolveInventoryObjectName(int id) => id switch
    {
        >= 0 and < 128 => ResolveItemName(id),
        >= 128 and < 256 => ResolveWeaponName(id - 128),
        >= 256 and < 288 => ResolveArmorName(id - 256),
        >= 288 and < 320 => ResolveAccessoryName(id - 288),
        >= PostscriptMateriaDropCompatibility.FirstMateriaRewardId and
            <= PostscriptMateriaDropCompatibility.LastMateriaRewardId => ResolveMateriaRewardName(id),
        _ => null
    };

    private string? ResolveMateriaRewardName(int id)
    {
        if (materiaDropCompatibility?.HasMateriaDropAliases() != true ||
            nativeText is not { HasSupportedLookup: true } live)
        {
            return null;
        }

        // Ghidra: 00419B73 redirects encoded reward names through 006C6B06,
        // which calls 0041963C(category 13, index, 8): loaded kernel2 bank 14.
        // Reading the disk section here could describe a different live mod.
        var index = id - PostscriptMateriaDropCompatibility.FirstMateriaRewardId;
        return live.TryRead(14, index, out var name) &&
               materiaDropCompatibility.HasMateriaDropAliases()
            ? name
            : null;
    }

    public string? ResolveInventoryObjectDescription(int id) => id switch
    {
        >= 0 and < 128 => ResolveItemDescription(id),
        >= 128 and < 256 => ResolveWeaponDescription(id - 128),
        >= 256 and < 288 => ResolveArmorDescription(id - 256),
        >= 288 and < 320 => ResolveAccessoryDescription(id - 288),
        _ => null
    };

    public string? ResolveBattleText(int id) => ResolveText(16, id, battleTexts);

    /// <summary>
    /// The name a summon's attack banner shows for its action, as <c>FUN_006D1CC0</c>
    /// case 0x16 draws it for command 3: <c>FUN_0041963C</c> category 6 reads the summon
    /// attack names (section 17) below 0x10 and the magic names (section 9) at the raw
    /// action otherwise.
    /// </summary>
    public string? ResolveSummonBannerText(int action) => action switch
    {
        >= 0 and < 0x10 => NormalizeBattleActionText(ResolveText(17, action, summonAttackNames)),
        >= 0x10 and <= byte.MaxValue => NormalizeBattleActionText(ResolveSpellName(action)),
        _ => null,
    };

    public static Kernel2TextDatabase? TryCreate(string gameRootDirectory, Action<string>? log = null)
    {
        var language = Ff7GameLanguageDetector.Detect(gameRootDirectory, log: log);
        return TryCreate(language, log);
    }

    public static Kernel2TextDatabase? TryCreate(
        Ff7GameLanguageContext language,
        Action<string>? log = null)
    {
        var path = language.Kernel2Path;
        if (!File.Exists(path))
        {
            log?.Invoke($"kernel2 text database unavailable; missing {path}");
            return null;
        }

        try
        {
            var decoded = Ff7LzsDecoder.DecodeFieldFile(File.ReadAllBytes(path));
            var database = TryCreateFromDecodedKernel2(decoded, language.Descriptor);
            if (database is null)
            {
                log?.Invoke($"kernel2 text database unavailable; invalid 18-section structure in {path}");
                return null;
            }

            log?.Invoke($"kernel2 text database loaded for {language.DisplayName} from {path}");
            return database;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            log?.Invoke($"kernel2 text database unavailable; {exception.Message}");
            return null;
        }
    }

    internal static Kernel2TextDatabase? TryCreateFromDecodedKernel2(byte[] decoded) =>
        TryCreateFromDecodedKernel2(decoded, Ff7GameLanguages.Get(Ff7GameLanguage.English));

    internal static Kernel2TextDatabase? TryCreateFromDecodedKernel2(
        byte[] decoded,
        Ff7GameLanguageDescriptor language)
    {
        if (!TryReadSequentialSections(decoded, language, out var sections) ||
            sections.Count != ExpectedSectionCounts.Length)
        {
            return null;
        }

        for (var index = 0; index < ExpectedSectionCounts.Length; index++)
        {
            if (sections[index].Count != ExpectedSectionCounts[index])
            {
                return null;
            }
        }

        return new Kernel2TextDatabase(sections);
    }

    private string? ResolveBattleActionText(IndexedTextSection section, int rawActionId, int bank)
    {
        if (rawActionId is < 0 or > byte.MaxValue or 0x7f)
        {
            return null;
        }

        var shiftedId = rawActionId + 0x80;
        var textId = shiftedId < 0xe0 ? shiftedId : rawActionId;
        return NormalizeBattleActionText(ResolveText(bank, textId, section));
    }

    private static string? NormalizeBattleActionText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim().TrimStart('"').Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool TryReadSequentialSections(
        byte[] decoded,
        Ff7GameLanguageDescriptor language,
        out IReadOnlyList<IndexedTextSection> sections)
    {
        var result = new List<IndexedTextSection>();
        var start = 0;
        while (start + sizeof(int) + sizeof(ushort) <= decoded.Length)
        {
            var sectionSize = ReadInt32(decoded, start);
            var tableBase = start + sizeof(int);
            if (sectionSize < sizeof(ushort) || sectionSize > decoded.Length - tableBase)
            {
                sections = [];
                return false;
            }

            var sectionEnd = tableBase + sectionSize;
            var firstStringOffset = ReadUInt16(decoded, tableBase);
            if (firstStringOffset < sizeof(ushort) ||
                firstStringOffset >= sectionSize ||
                firstStringOffset % sizeof(ushort) != 0)
            {
                sections = [];
                return false;
            }

            var count = firstStringOffset / sizeof(ushort);
            if (count is < 1 or > 4096 || tableBase + (count * sizeof(ushort)) > sectionEnd)
            {
                sections = [];
                return false;
            }

            var candidate = new IndexedTextSection(decoded, tableBase, sectionEnd, count, language);
            if (!candidate.HasCoherentOffsets())
            {
                sections = [];
                return false;
            }

            result.Add(candidate);
            start = sectionEnd;
        }

        sections = result;
        return result.Count > 0 && start == decoded.Length;
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        bytes[offset] |
        (bytes[offset + 1] << 8) |
        (bytes[offset + 2] << 16) |
        (bytes[offset + 3] << 24);

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private readonly record struct IndexedTextSection(
        byte[] Decoded,
        int TableBase,
        int SectionEnd,
        int Count,
        Ff7GameLanguageDescriptor Language)
    {
        public bool HasCoherentOffsets()
        {
            var minimum = Count * sizeof(ushort);
            var previous = minimum;
            for (var index = 0; index < Count; index++)
            {
                var offset = ReadUInt16(Decoded, TableBase + (index * sizeof(ushort)));
                if (offset < minimum || offset < previous || offset >= SectionEnd - TableBase)
                {
                    return false;
                }

                previous = offset;
            }

            return true;
        }

        public string? Resolve(int id)
        {
            if (id < 0 || id >= Count)
            {
                return null;
            }

            var relativeOffset = ReadUInt16(Decoded, TableBase + (id * sizeof(ushort)));
            var address = TableBase + relativeOffset;
            if (address < TableBase || address >= SectionEnd)
            {
                return null;
            }

            var text = Ff7EncodedTextDecoder.DecodeKernelTerminated(
                Decoded.AsSpan(address, SectionEnd - address),
                Language)
                .Replace("“", string.Empty, StringComparison.Ordinal)
                .Replace("”", string.Empty, StringComparison.Ordinal);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}
