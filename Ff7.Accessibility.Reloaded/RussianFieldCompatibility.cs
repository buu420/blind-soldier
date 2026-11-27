using System.Security.Cryptography;
using System.Text.Json;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Exact navigation identities of the FFRTT Russian 1.02 translation (ayumosa/ff7-7h-russian for
/// 7th Heaven). 7th Heaven overlays the Russian flevel while the installed English flevel.lgp stays
/// on disk, so in almost every field the loaded script is not the physical one and the catalog
/// would refuse it. tools/RussianFieldAudit compared each reviewed navigation field of both
/// archives: sections, script header, entity names, slot aliases, every instruction under the
/// localized cue aligner's equivalence rules, the navigation-relevant opcodes (LINE, MAPJUMP,
/// CHAR/PC, traversal, placement), the model loader, and what the production parser publishes.
///
/// <para>A row admits one exact loaded prefix for stock native parsing. The disk's model metadata
/// may describe it only when the disk holds that same script, or the exact English prefix and
/// model loader section the row was audited against. Nothing here waives a disk mismatch in
/// general, guesses at an unknown prefix, or changes description variants.</para>
/// </summary>
public static partial class RussianFieldCompatibility
{
    private const int SectionCountOffset = 2;
    private const int SectionOffsetsOffset = 6;
    private const int ModelLoaderSection = 2;
    private static readonly IReadOnlyDictionary<int, Row> Rows = ReadRows();
    private static readonly IReadOnlySet<int> EnglishRouteUntrusted = ReadEnglishRouteUntrusted();

    /// <summary>Reviewed Russian identities; the generated count, checked when the rows are read.</summary>
    public static int ReviewedIdentityCount => Rows.Count;

    /// <summary>Whether this exact loaded prefix is a reviewed Russian 1.02 navigation identity.</summary>
    public static bool IsReviewedNavigationIdentity(LoadedFieldScriptIdentity identity) => Find(identity) is not null;

    // Forest native banks, entities, plant WAITs and pickup bits are unchanged in
    // 1.02. Only Switch reset changes from key release to key press (620/622/623).
    // This is deliberately separate from description-offset authorization.
    internal static SupportedFieldScriptVariant ResolveForestVariant(LoadedFieldScriptIdentity identity)
    {
        var variant = EchoSCompatibilityManifest.ResolveVariant(identity);
        return variant == SupportedFieldScriptVariant.Unknown && identity.FieldId is >= 620 and <= 624 && Find(identity) is not null
            ? SupportedFieldScriptVariant.Vanilla : variant;
    }

    internal static bool ForestResetUsesPress(LoadedFieldScriptIdentity identity) =>
        identity.FieldId is 620 or 622 or 623 && Find(identity) is not null;

    internal static IReadOnlyList<Row> ReviewedRows => Rows.Values.OrderBy(row => row.Field).ToArray();

    internal static Row? Find(LoadedFieldScriptIdentity identity) =>
        Rows.TryGetValue(identity.FieldId, out var row) &&
        string.Equals(row.Russian, identity.ScriptPrefixSha256, StringComparison.OrdinalIgnoreCase)
            ? row
            : null;

    /// <summary>
    /// Whether the installed English field still describes this field's Russian script for routes
    /// planned from another field, which FieldCrossFieldApproachResolver reads from disk: true where
    /// the Russian script is unchanged, differs only in window geometry, or differs only in
    /// presentation, or has separate native transfer/arrival proof; false for entity shifts
    /// and actual route output differences.
    /// </summary>
    internal static bool EnglishDiskDescribesRussianRoutes(int fieldId) => !EnglishRouteUntrusted.Contains(fieldId);

    /// <summary>
    /// Whether the installed field file may supply this row's model metadata: it is the same Russian
    /// script (a physically installed translation), or exactly the English prefix and model loader
    /// the row was audited against. Any other disk, an unreadable loader included, is refused.
    /// </summary>
    internal static bool AcceptsDiskMetadata(Row row, string diskPrefixSha256, byte[] diskFieldBytes, out string diagnostic)
    {
        if (string.Equals(diskPrefixSha256, row.Russian, StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = "same Russian script on disk";
            return true;
        }

        if (!string.Equals(diskPrefixSha256, row.English, StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = $"disk script {diskPrefixSha256} is not the audited English prefix";
            return false;
        }

        var models = ModelLoaderSectionSha256(diskFieldBytes);
        if (!string.Equals(models, row.EnglishModels, StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = $"disk model loader {models ?? "unreadable"} is not the audited English model loader";
            return false;
        }

        diagnostic = "audited English disk metadata";
        return true;
    }

    /// <summary>SHA-256 of a decoded field file's model loader section (section 3), or null.</summary>
    internal static string? ModelLoaderSectionSha256(byte[] fieldBytes)
    {
        if (fieldBytes.Length < SectionOffsetsOffset ||
            BitConverter.ToInt32(fieldBytes, SectionCountOffset) <= ModelLoaderSection)
            return null;
        var offsetPosition = SectionOffsetsOffset + ModelLoaderSection * sizeof(int);
        if (offsetPosition > fieldBytes.Length - sizeof(int)) return null;
        var sectionOffset = BitConverter.ToInt32(fieldBytes, offsetPosition);
        if (sectionOffset <= 0 || sectionOffset > fieldBytes.Length - sizeof(int)) return null;
        var length = BitConverter.ToInt32(fieldBytes, sectionOffset);
        var start = sectionOffset + sizeof(int);
        if (length < 0 || start > fieldBytes.Length - length) return null;
        return Convert.ToHexString(SHA256.HashData(fieldBytes.AsSpan(start, length)));
    }

    private static IReadOnlyDictionary<int, Row> ReadRows()
    {
        var rows = new Dictionary<int, Row>();
        static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        foreach (var line in RawRows.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var raw = JsonSerializer.Deserialize<RawRow>(line) ?? throw new InvalidOperationException("Missing reviewed Russian field row");
            var identity = new LoadedFieldScriptIdentity(raw.Field, raw.Russian);
            var variant = WholeGameEchoNavigationManifest.ResolveVariant(identity);
            // English must be the field's reviewed stock prefix; Russian must differ from it and may
            // only coincide with an official localized stock prefix, never with Echo-S or Postscript.
            if (!Hash(raw.Russian) || !Hash(raw.English) || !Hash(raw.EnglishModels) ||
                !string.Equals(WholeGameEchoNavigationManifest.StockFingerprint(raw.Field), raw.English, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw.Russian, raw.English, StringComparison.OrdinalIgnoreCase) ||
                variant is not (SupportedFieldScriptVariant.Unknown or SupportedFieldScriptVariant.Vanilla) ||
                raw.Review is not ("semantic" or "reviewed") ||
                raw.Review == "semantic" && raw.Map is not null ||
                raw.Map is not null && (raw.Map.Count == 0 || raw.Map.Keys.Any(k => k is < 0 or > 255) ||
                    raw.Map.Values.Any(v => v is < 0 or > 255) || raw.Map.Values.Distinct().Count() != raw.Map.Count) ||
                !rows.TryAdd(raw.Field, new Row(raw.Field, raw.Russian.ToUpperInvariant(), raw.English.ToUpperInvariant(),
                    raw.EnglishModels.ToUpperInvariant(), raw.Review, raw.Map)))
                throw new InvalidOperationException($"Invalid reviewed Russian navigation field {raw.Field}");
        }

        if (rows.Count != GeneratedRowCount) throw new InvalidOperationException("Incomplete reviewed Russian navigation manifest");
        return rows;
    }

    private static IReadOnlySet<int> ReadEnglishRouteUntrusted()
    {
        var fields = RawEnglishRouteUntrustedFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).ToHashSet();
        // Every reviewed field with an entity map is untrusted, and only reviewed navigation fields are listed.
        if (fields.Any(field => WholeGameEchoNavigationManifest.StockFingerprint(field) is null) ||
            Rows.Values.Any(row => row.Map is not null && !fields.Contains(row.Field)) ||
            Rows.Values.Any(row => row.Review == "semantic" && fields.Contains(row.Field)))
            throw new InvalidOperationException("Invalid Russian cross-field route trust list");
        return fields;
    }

    /// <param name="Map">English (canonical) entity to loaded Russian entity, only where entity names differ.</param>
    internal sealed record Row(int Field, string Russian, string English, string EnglishModels, string Review,
        IReadOnlyDictionary<int, int>? Map);

    private sealed record RawRow(int Field, string Russian, string English, string EnglishModels, string Review,
        Dictionary<int, int>? Map);
}
