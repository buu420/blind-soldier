namespace Ff7.Accessibility.Reloaded;

public sealed class Kernel2TextDatabase
{
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
    private readonly IndexedTextSection materiaNames;
    private readonly IndexedTextSection materiaDescriptions;
    private readonly IndexedTextSection battleTexts;

    private Kernel2TextDatabase(
        IndexedTextSection itemNames,
        IndexedTextSection itemDescriptions,
        IndexedTextSection spellNames,
        IndexedTextSection spellDescriptions,
        IndexedTextSection weaponNames,
        IndexedTextSection weaponDescriptions,
        IndexedTextSection armorNames,
        IndexedTextSection armorDescriptions,
        IndexedTextSection accessoryNames,
        IndexedTextSection accessoryDescriptions,
        IndexedTextSection commandNames,
        IndexedTextSection materiaNames,
        IndexedTextSection materiaDescriptions,
        IndexedTextSection battleTexts)
    {
        this.itemNames = itemNames;
        this.itemDescriptions = itemDescriptions;
        this.spellNames = spellNames;
        this.spellDescriptions = spellDescriptions;
        this.weaponNames = weaponNames;
        this.weaponDescriptions = weaponDescriptions;
        this.armorNames = armorNames;
        this.armorDescriptions = armorDescriptions;
        this.accessoryNames = accessoryNames;
        this.accessoryDescriptions = accessoryDescriptions;
        this.commandNames = commandNames;
        this.materiaNames = materiaNames;
        this.materiaDescriptions = materiaDescriptions;
        this.battleTexts = battleTexts;
    }

    public string? ResolveItemName(int id) => itemNames.Resolve(id);

    public string? ResolveItemDescription(int id) => itemDescriptions.Resolve(id);

    public string? ResolveSpellName(int id) => spellNames.Resolve(id);

    public string? ResolveSpellDescription(int id) => spellDescriptions.Resolve(id);

    public string? ResolveBattleActionName(int rawActionId) =>
        ResolveBattleActionText(spellNames, rawActionId);

    public string? ResolveBattleActionDescription(int rawActionId) =>
        ResolveBattleActionText(spellDescriptions, rawActionId);

    public string? ResolveWeaponName(int id) => weaponNames.Resolve(id);

    public string? ResolveWeaponDescription(int id) => weaponDescriptions.Resolve(id);

    public string? ResolveArmorName(int id) => armorNames.Resolve(id);

    public string? ResolveArmorDescription(int id) => armorDescriptions.Resolve(id);

    public string? ResolveAccessoryName(int id) => accessoryNames.Resolve(id);

    public string? ResolveAccessoryDescription(int id) => accessoryDescriptions.Resolve(id);

    public string? ResolveCommandName(int id) => commandNames.Resolve(id);

    public string? ResolveMateriaName(int id) => materiaNames.Resolve(id);

    public string? ResolveMateriaDescription(int id) => materiaDescriptions.Resolve(id);

    public string? ResolveInventoryObjectName(int id) => id switch
    {
        >= 0 and < 128 => itemNames.Resolve(id),
        >= 128 and < 256 => weaponNames.Resolve(id - 128),
        >= 256 and < 288 => armorNames.Resolve(id - 256),
        >= 288 and < 320 => accessoryNames.Resolve(id - 288),
        _ => null
    };

    public string? ResolveInventoryObjectDescription(int id) => id switch
    {
        >= 0 and < 128 => itemDescriptions.Resolve(id),
        >= 128 and < 256 => weaponDescriptions.Resolve(id - 128),
        >= 256 and < 288 => armorDescriptions.Resolve(id - 256),
        >= 288 and < 320 => accessoryDescriptions.Resolve(id - 288),
        _ => null
    };

    public string? ResolveBattleText(int id) => battleTexts.Resolve(id);

    private static string? ResolveBattleActionText(
        IndexedTextSection section,
        int rawActionId)
    {
        if (rawActionId is < 0 or > byte.MaxValue or 0x7F)
        {
            return null;
        }

        // FF7's battle string category 3 applies a base of 0x80 while the
        // result remains below 0xE0. Higher action ids, including Tifa's
        // slot-selected limits, already address their final KERNEL2 record.
        var shiftedId = rawActionId + 0x80;
        var textId = shiftedId < 0xE0 ? shiftedId : rawActionId;
        return NormalizeBattleActionText(section.Resolve(textId));
    }

    private static string? NormalizeBattleActionText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const string switchPrefix = "[SWITCH]";
        var normalized = text.Trim();
        if (normalized.StartsWith(switchPrefix, StringComparison.Ordinal))
        {
            normalized = normalized[switchPrefix.Length..].TrimStart();
        }

        normalized = normalized.TrimStart('"').Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    public static Kernel2TextDatabase? TryCreate(string gameRootDirectory, Action<string>? log = null)
    {
        var path = Path.Combine(gameRootDirectory, "data", "lang-en", "kernel", "kernel2.bin");
        if (!File.Exists(path))
        {
            log?.Invoke($"kernel2 text database unavailable; missing {path}");
            return null;
        }

        try
        {
            var decoded = Ff7LzsDecoder.DecodeFieldFile(File.ReadAllBytes(path));
            var database = TryCreateFromDecodedKernel2(decoded);
            if (database is null)
            {
                log?.Invoke($"kernel2 text database unavailable; required sections were not found in {path}");
                return null;
            }

            log?.Invoke($"kernel2 text database loaded from {path}");
            return database;
        }
        catch (Exception ex)
        {
            log?.Invoke($"kernel2 text database unavailable; {ex.Message}");
            return null;
        }
    }

    internal static Kernel2TextDatabase? TryCreateFromDecodedKernel2(byte[] decoded)
    {
        if (!TryFindSection(decoded, Signature(0, "Potion", 3, "Ether", 7, "Phoenix Down"), out var itemNames) ||
            !TryFindSection(decoded, Signature(0, "Restores HP by 100", 3, "Restores MP by 100", 7, "Restores life"), out var itemDescriptions) ||
            !TryFindSection(decoded, Signature(0, "Cure", 27, "Fire", 33, "Bolt"), out var spellNames) ||
            !TryFindSection(decoded, Signature(0, "Restores HP", 27, "Fire element attack", 33, "Lightning element attack"), out var spellDescriptions) ||
            !TryFindSection(decoded, Signature(0, "Buster Sword", 1, "Mythril Saber", 2, "Hardedge"), out var weaponNames) ||
            !TryFindSection(decoded, Signature(0, "Bronze Bangle", 1, "Iron Bangle", 2, "Titan Bangle"), out var armorNames) ||
            !TryFindSection(decoded, Signature(0, "Power Wrist", 1, "Protect Vest", 2, "Earring"), out var accessoryNames) ||
            !TryFindSection(decoded, Signature(1, "Attack", 2, "Magic", 3, "Summon"), out var commandNames) ||
            !TryFindSection(decoded, Signature(0, "MP Plus", 1, "HP Plus", 2, "Speed Plus"), out var materiaNames) ||
            !TryReadSequentialSections(decoded, out var sections) ||
            sections.Count <= 16)
        {
            return null;
        }

        return new Kernel2TextDatabase(
            itemNames,
            itemDescriptions,
            spellNames,
            spellDescriptions,
            weaponNames,
            sections[3],
            armorNames,
            sections[4],
            accessoryNames,
            sections[5],
            commandNames,
            materiaNames,
            sections[6],
            sections[16]);
    }

    private static Dictionary<int, string> Signature(
        int index0,
        string text0,
        int index1,
        string text1,
        int index2,
        string text2) =>
        new()
        {
            [index0] = text0,
            [index1] = text1,
            [index2] = text2
        };

    private static bool TryFindSection(
        byte[] decoded,
        IReadOnlyDictionary<int, string> expectedEntries,
        out IndexedTextSection section)
    {
        section = default;
        for (var start = 0; start <= decoded.Length - 8; start++)
        {
            var sectionSize = ReadInt32(decoded, start);
            if (sectionSize < 32 || start + sectionSize > decoded.Length)
            {
                continue;
            }

            var tableBase = start + sizeof(int);
            var firstStringOffset = ReadUInt16(decoded, tableBase);
            if (firstStringOffset < sizeof(ushort) ||
                firstStringOffset >= sectionSize - sizeof(int) ||
                firstStringOffset % sizeof(ushort) != 0)
            {
                continue;
            }

            var count = firstStringOffset / sizeof(ushort);
            if (count is < 3 or > 512)
            {
                continue;
            }

            var candidate = new IndexedTextSection(decoded, tableBase, start + sectionSize, count);
            var matches = expectedEntries.All(pair =>
                string.Equals(candidate.Resolve(pair.Key), pair.Value, StringComparison.Ordinal));
            if (!matches)
            {
                continue;
            }

            section = candidate;
            return true;
        }

        return false;
    }

    private static bool TryReadSequentialSections(byte[] decoded, out IReadOnlyList<IndexedTextSection> sections)
    {
        var result = new List<IndexedTextSection>();
        var start = 0;
        while (start + sizeof(int) + sizeof(ushort) <= decoded.Length)
        {
            var sectionSize = ReadInt32(decoded, start);
            if (sectionSize < sizeof(ushort) || start + sizeof(int) + sectionSize > decoded.Length)
            {
                sections = [];
                return false;
            }

            var tableBase = start + sizeof(int);
            var firstStringOffset = ReadUInt16(decoded, tableBase);
            if (firstStringOffset < sizeof(ushort) ||
                firstStringOffset >= sectionSize ||
                firstStringOffset % sizeof(ushort) != 0)
            {
                sections = [];
                return false;
            }

            var count = firstStringOffset / sizeof(ushort);
            if (count is < 1 or > 4096)
            {
                sections = [];
                return false;
            }

            result.Add(new IndexedTextSection(decoded, tableBase, tableBase + sectionSize, count));
            start += sizeof(int) + sectionSize;
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

    private readonly record struct IndexedTextSection(byte[] Decoded, int TableBase, int SectionEnd, int Count)
    {
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

            var text = Ff7EncodedTextDecoder.DecodeTerminated(Decoded.AsSpan(address, SectionEnd - address));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}
