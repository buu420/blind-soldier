namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reviewed Postscript 1.78 first-screen field script. FFNx replaces section 1
/// of md1stin; the loaded script prefix, not the mod's presence, authorizes the
/// four shifted opening actions and the unchanged area-name instruction.
/// </summary>
public static class PostscriptFieldCompatibility
{
    public const int OpeningFieldId = 116;
    public const string OpeningPrefixSha256 =
        "92EB71C4671732A1F1642392C59124824DE130D201C6516E862CB33ECFE0DB44";

    // The 19,004-byte native md1stin.chunk.1 has twelve entities and a
    // 2,230-byte script prefix. The four action opcodes retain their exact
    // instruction context after the Postscript insertions. MPNAM stays at 65.
    private static readonly IReadOnlyDictionary<int, int> NativeToStockByteIndex =
        new Dictionary<int, int>
        {
            [65] = 65,
            [198] = 160,
            [230] = 192,
            [242] = 204,
            [307] = 269
        };

    public static bool IsSupportedOpeningIdentity(LoadedFieldScriptIdentity identity) =>
        identity.FieldId == OpeningFieldId &&
        string.Equals(identity.ScriptPrefixSha256, OpeningPrefixSha256, StringComparison.OrdinalIgnoreCase);

    internal static FieldCutsceneDescriptionKey? ResolveCanonicalCueKey(FieldScriptContext context)
    {
        if (context.FieldId != OpeningFieldId ||
            context.EntityId != 0 ||
            context.ScriptId != 0 ||
            !NativeToStockByteIndex.TryGetValue(context.ByteIndex, out var stockByteIndex))
            return null;

        return new FieldCutsceneDescriptionKey(OpeningFieldId, 0, 0, stockByteIndex);
    }

    internal static IReadOnlyList<FieldCutsceneDescriptionCue> CreateOpeningDescriptions()
    {
        var stock = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .Where(cue => cue.FieldId == OpeningFieldId)
            .ToDictionary(cue => cue.ByteIndex);
        if (stock.Count != NativeToStockByteIndex.Count ||
            NativeToStockByteIndex.Values.Any(index => !stock.ContainsKey(index)))
            throw new InvalidOperationException("Postscript opening cue map no longer matches the reviewed stock catalog");

        return NativeToStockByteIndex
            .Select(pair => stock[pair.Value] with { ByteIndex = pair.Key })
            .ToArray();
    }
}
