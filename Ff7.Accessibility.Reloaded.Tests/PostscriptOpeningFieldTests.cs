using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class PostscriptOpeningFieldTests
{
    internal static void Run()
    {
        SelectsOnlyTheExactOpeningScript();
        ResolvesFiveNativeOpeningCuesToTheirStockMeaning();
        RejectsUnreviewedCuePositionsAndOpcodes();
    }

    private static void SelectsOnlyTheExactOpeningScript()
    {
        var identity = new LoadedFieldScriptIdentity(116, PostscriptFieldCompatibility.OpeningPrefixSha256);
        Equal(true, PostscriptFieldCompatibility.IsSupportedOpeningIdentity(identity), "reviewed field 116 identity");
        Equal(SupportedFieldScriptVariant.Postscript178, WholeGameEchoNavigationManifest.ResolveVariant(identity), "navigation identity");
        Equal(SupportedFieldScriptVariant.Postscript178, WholeGameEchoNavigationManifest.ResolveDescriptionVariant(identity), "description identity");
        Equal(SupportedFieldScriptVariant.Postscript178, EchoSCompatibilityManifest.ResolveVariant(identity), "runtime description identity");
        Equal(true, WholeGameEchoNavigationManifest.SourceKnownExactPrefixHash(116, identity.ScriptPrefixSha256), "catalog source identity");

        foreach (var rejected in new[]
                 {
                     identity with { FieldId = 117 },
                     identity with { ScriptPrefixSha256 = new string('0', 64) },
                     identity with { ScriptPrefixSha256 = "18E7F7E7DD47A52C98255001DA76578D335AF5F4CA238ADA81FCD5EC6FEB6FA2" }
                 })
        {
            Equal(false, PostscriptFieldCompatibility.IsSupportedOpeningIdentity(rejected), "unreviewed opening identity");
        }
        Equal(SupportedFieldScriptVariant.Unknown,
            WholeGameEchoNavigationManifest.ResolveVariant(identity with { FieldId = 117 }),
            "opening hash cannot authorize another field");
        Equal(SupportedFieldScriptVariant.Unknown,
            EchoSCompatibilityManifest.ResolveVariant(identity with { ScriptPrefixSha256 = new string('0', 64) }),
            "mutated opening hash cannot authorize descriptions");
    }

    private static void ResolvesFiveNativeOpeningCuesToTheirStockMeaning()
    {
        var identity = new LoadedFieldScriptIdentity(116, PostscriptFieldCompatibility.OpeningPrefixSha256);
        (int Stock, int Native)[] expected = [(65, 65), (160, 198), (192, 230), (204, 242), (269, 307)];
        var catalog = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .Where(cue => cue.FieldId == 116).ToDictionary(cue => cue.ByteIndex);
        Equal(5, catalog.Count, "opening cue catalog size");
        var tracker = new EchoSFieldCutsceneDescriptionTracker();
        foreach (var (stock, native) in expected)
        {
            var cue = catalog[stock];
            var context = new FieldScriptContext(116, cue.EntityId, cue.ScriptId, native, (byte)cue.Opcode);
            Equal(cue.Key, EchoSCompatibilityManifest.ResolveCanonicalCueKey(context, identity), "native cue canonical key");
            Equal(cue.Text, tracker.Observe(context, identity)?.Text, "native cue retains reviewed text");
            Equal(null, tracker.Observe(context, identity), "repeated native cue speaks once");
        }
        tracker.Reset();
        var first = catalog[160];
        Equal(first.Text,
            tracker.Observe(new FieldScriptContext(116, 0, 0, 198, (byte)first.Opcode), identity)?.Text,
            "reset permits next field visit");
    }

    private static void RejectsUnreviewedCuePositionsAndOpcodes()
    {
        var identity = new LoadedFieldScriptIdentity(116, PostscriptFieldCompatibility.OpeningPrefixSha256);
        var tracker = new EchoSFieldCutsceneDescriptionTracker();
        var native = new FieldScriptContext(116, 0, 0, 198, 0x24);
        Equal(null, tracker.Observe(native with { ByteIndex = 197 }, identity), "nearby byte remains silent");
        Equal(null, tracker.Observe(native with { Opcode = 0x25 }, identity), "wrong opcode remains silent");
        Equal(null, tracker.Observe(native, identity with { ScriptPrefixSha256 = new string('F', 64) }), "unknown hash remains silent");
        Equal(null, tracker.Observe(native, identity with { FieldId = 117 }), "wrong field remains silent");
        Equal(null, EchoSCompatibilityManifest.ResolveCanonicalCueKey(native with { ByteIndex = 197 }, identity), "unreviewed key has no alias");
        Equal(null, EchoSCompatibilityManifest.ResolveCanonicalCueKey(native, identity with { FieldId = 117 }), "wrong field has no alias");
        Equal(null, EchoSCompatibilityManifest.ResolveCanonicalCueKey(native, identity with { ScriptPrefixSha256 = new string('F', 64) }), "unknown hash has no alias");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
