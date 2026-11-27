namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Field prefixes of the Polish fan translation releases that may use the English navigation
/// review, exactly as <see cref="WholeGameEchoNavigationManifest"/> lets the official localized
/// archives use it. Each row was produced by tools/LocalizedDescriptionCueAligner --navigation-emit:
/// the Polish field has the English stock entity layout and slot aliasing, and every Init/Main,
/// Talk and Contact slot stays mostly the English code. Requested slots may differ only when
/// neither side touches what navigation reads (map jumps, lines, model binding, bank writes).
/// The same rule rejects 9 of the 2,808 reviewed official prefixes, all cases the manifest
/// handles explicitly. Any other prefix stays unknown.
/// </summary>
public static partial class PolishNavigationManifest
{
    private static readonly Lazy<(Dictionary<int, HashSet<string>> ByField, List<string> Invalid)> Data = new(Parse);

    /// <summary>Whether this exact loaded prefix is a reviewed Polish navigation identity.</summary>
    public static bool IsReviewed(LoadedFieldScriptIdentity identity) =>
        identity.ScriptPrefixSha256 is { Length: 64 } prefix &&
        Data.Value.ByField.TryGetValue(identity.FieldId, out var prefixes) &&
        prefixes.Contains(prefix);

    internal static IEnumerable<LoadedFieldScriptIdentity> Identities =>
        Data.Value.ByField.SelectMany(p => p.Value.Select(prefix => new LoadedFieldScriptIdentity(p.Key, prefix)));

    internal static IReadOnlyList<string> InvalidRows => Data.Value.Invalid;

    private static (Dictionary<int, HashSet<string>> ByField, List<string> Invalid) Parse()
    {
        var byField = new Dictionary<int, HashSet<string>>();
        var invalid = new List<string>();
        foreach (var raw in RawIdentities.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var field) || field is < 0 or > 1023 ||
                parts[1].Length != 64 || !parts[1].All(Uri.IsHexDigit))
            {
                invalid.Add(line);
                continue;
            }

            if (!byField.TryGetValue(field, out var prefixes))
                byField[field] = prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!prefixes.Add(parts[1])) invalid.Add(line);
        }

        return (byField, invalid);
    }
}
