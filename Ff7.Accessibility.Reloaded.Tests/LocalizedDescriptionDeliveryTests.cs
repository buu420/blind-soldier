using System.Text.RegularExpressions;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Delivery of reviewed localized identities: the description gate and the identity cache both
/// hosts use, and film anchors moved by the Polish fan translation scripts (V1.05 and V1.07 alike:
/// zcoal_2 English byte 104 runs at Polish 101, zcoal_3 English 90 at Polish 70), resolved by their English anchor and
/// never by raw localized offsets. The x64 coordinator is exercised end to end in
/// Steam2026LocalizedDeliveryTests; the x86 host cannot be constructed in a test, so its four
/// call sites are held to the same shared policy here.
/// </summary>
internal static class LocalizedDescriptionDeliveryTests
{
    private static readonly DateTime Start = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        var tests = new Action[]
        {
            ReviewedPolishPrefixesOpenTheDescriptionGateAndUnknownOnesDoNot,
            ReviewedLocalizedIdentitiesAreCachedLikeStockAndUnknownOnesAreNot,
            PolishMovedFilmAnchorsResolveTheirEnglishTracks,
            TheSharedFilmTrackerStartsPolishMovedFilmsLikeEnglish,
            ReviewedLocalizedPrefixesNeverResolveFilmsFromRawOffsets,
            StockEchoAndUnknownIdentitiesKeepTheirRawFilmAnchors,
            EveryFilmAnchorIsAReviewedCatalogCue,
            TheX86HostRoutesIdentityAndFilmDecisionsThroughThePolicy
        };
        var failures = new List<string>();
        foreach (var test in tests)
        {
            try { test(); Console.WriteLine($"PASS {test.Method.Name}"); }
            catch (Exception error) { failures.Add($"{test.Method.Name}: {error.Message}"); Console.WriteLine($"FAIL {test.Method.Name}: {error.Message}"); }
        }

        if (failures.Count > 0) throw new InvalidOperationException("Localized description delivery failures:\n" + string.Join('\n', failures));
        Console.WriteLine("Localized description delivery tests passed.");
    }

    private static readonly (int Field, int EnglishByte, int Entity, int Script, int PolishByte)[] MovedPolishFilms =
        [(729, 104, 4, 0, 101), (730, 90, 11, 3, 70)];

    private static LoadedFieldScriptIdentity Identity(int field, string language) =>
        LocalizedFieldDescriptionCueManifest.ReviewedIdentities
            .Where(i => i.FieldId == field && i.Languages.Contains(language))
            .Select(i => new LoadedFieldScriptIdentity(field, i.PrefixSha256))
            .Single();

    /// <summary>Every reviewed Polish release's prefix of a field; V1.05 and V1.07 usually differ.</summary>
    private static LoadedFieldScriptIdentity[] PolishIdentities(int field)
    {
        var identities = LocalizedFieldDescriptionCueManifest.ReviewedIdentities
            .Where(i => i.FieldId == field && i.Languages.Contains("pl"))
            .Select(i => new LoadedFieldScriptIdentity(field, i.PrefixSha256))
            .ToArray();
        True(identities.Length > 0, $"field {field} has a reviewed Polish prefix");
        return identities;
    }

    private static IEnumerable<(int Field, int EnglishByte, int Entity, int Script, int PolishByte, LoadedFieldScriptIdentity Polish)> MovedPolishFilmIdentities() =>
        MovedPolishFilms.SelectMany(f => PolishIdentities(f.Field).Select(p => (f.Field, f.EnglishByte, f.Entity, f.Script, f.PolishByte, p)));

    private static LoadedFieldScriptIdentity NearMiss(LoadedFieldScriptIdentity identity) =>
        identity with { ScriptPrefixSha256 = identity.ScriptPrefixSha256[..^1] + (identity.ScriptPrefixSha256[^1] == '0' ? '1' : '0') };

    private static void ReviewedPolishPrefixesOpenTheDescriptionGateAndUnknownOnesDoNot()
    {
        var polishOnly = LocalizedFieldDescriptionCueManifest.ReviewedIdentities
            .Where(i => i.Languages.Count == 1 && i.Languages[0] == "pl").ToArray();
        Equal(1124, polishOnly.Length, "Polish V1.05 and V1.07 prefixes no other reviewed archive shares");
        foreach (var reviewed in polishOnly)
        {
            var identity = new LoadedFieldScriptIdentity(reviewed.FieldId, reviewed.PrefixSha256);
            Equal(SupportedFieldScriptVariant.Unknown, WholeGameEchoNavigationManifest.ResolveVariant(identity), "navigation does not know it");
            True(LocalizedFieldScriptIdentityPolicy.AllowsDescriptions(identity), $"field {reviewed.FieldId} reviewed Polish prefix delivers descriptions");
            True(!LocalizedFieldScriptIdentityPolicy.AllowsDescriptions(NearMiss(identity)), $"field {reviewed.FieldId} one changed digit stays silent");
        }

        foreach (var field in new[] { 116, 729, 767 })
        {
            True(LocalizedFieldScriptIdentityPolicy.AllowsDescriptions(new(field, EchoSCompatibilityManifest.GetVanillaFingerprint(field)!)), "English stock");
            True(LocalizedFieldScriptIdentityPolicy.AllowsDescriptions(Identity(field, "fr")), "official French");
        }

        True(LocalizedFieldScriptIdentityPolicy.AllowsDescriptions(new(729, EchoSCompatibilityManifest.GetEchoFingerprint(729)!)), "Echo-S");
        True(!LocalizedFieldScriptIdentityPolicy.AllowsDescriptions(new(729, new string('A', 64))), "an unknown prefix");
        foreach (var other in PolishIdentities(729))
            True(!LocalizedFieldScriptIdentityPolicy.AllowsDescriptions(new(730, other.ScriptPrefixSha256)), "a reviewed prefix of another field");
    }

    private static void ReviewedLocalizedIdentitiesAreCachedLikeStockAndUnknownOnesAreNot()
    {
        foreach (var (field, _, _, _, _) in MovedPolishFilms)
        {
            foreach (var polish in PolishIdentities(field))
            {
                True(LocalizedFieldScriptIdentityPolicy.IsReviewedLoadedIdentity(polish), $"{field} reviewed Polish prefix is cached");
                True(!LocalizedFieldScriptIdentityPolicy.IsReviewedLoadedIdentity(NearMiss(polish)), $"{field} unknown prefix is re-read");
            }
            True(LocalizedFieldScriptIdentityPolicy.IsReviewedLoadedIdentity(Identity(field, "ja")), $"{field} official Japanese is cached");
            True(LocalizedFieldScriptIdentityPolicy.IsReviewedLoadedIdentity(new(field, EchoSCompatibilityManifest.GetVanillaFingerprint(field)!)), "stock is cached as before");
        }

        True(LocalizedFieldScriptIdentityPolicy.IsReviewedLoadedIdentity(new(729, EchoSCompatibilityManifest.GetEchoFingerprint(729)!)), "Echo-S is cached as before");
        True(LocalizedFieldScriptIdentityPolicy.IsReviewedLoadedIdentity(new(109, EchoSCompatibilityManifest.EchoS124DisclaimerFingerprint)), "the Echo-S disclaimer is cached as before");
        True(!LocalizedFieldScriptIdentityPolicy.IsReviewedLoadedIdentity(new(729, new string('A', 64))), "an unknown prefix is never cached");
    }

    private static void PolishMovedFilmAnchorsResolveTheirEnglishTracks()
    {
        foreach (var (field, englishByte, entity, script, polishByte, polish) in MovedPolishFilmIdentities())
        {
            var track = FieldMovieNarrationPolicy.All.Single(t => t.FieldId == field && t.ByteIndex == englishByte);
            var english = new FieldCutsceneDescriptionKey(field, track.EntityId, track.ScriptId, track.ByteIndex);
            Equal((entity, script), (track.EntityId, track.ScriptId), "the move is within the same entity and slot");
            var native = new FieldScriptContext(field, entity, script, polishByte, FieldOpcodeAddressResolver.OpcodeMovieIndex);
            Equal(english, LocalizedFieldScriptIdentityPolicy.FilmAnchor(native, polish), $"{field}: Polish byte {polishByte} is the English film anchor");
            var cue = LocalizedFieldDescriptionCueManifest.CreateDescriptions(polish).Single(c => c.Key == new FieldCutsceneDescriptionKey(field, entity, script, polishByte));
            Equal(english, LocalizedFieldScriptIdentityPolicy.FilmAnchor(
                new FieldScriptContext(cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode), polish),
                $"{field}: the queued Polish cue asks about the same English anchor");
            True(FieldMovieNarrationPolicy.TryResolve(english.FieldId, english.EntityId, english.ScriptId, english.ByteIndex, out var resolved) &&
                 resolved.MovieNumber == 34 && resolved.FilmFileName == "nvlmk.avi", $"{field}: the anchor names the nvlmk film");
        }
    }

    private static void TheSharedFilmTrackerStartsPolishMovedFilmsLikeEnglish()
    {
        foreach (var (field, englishByte, entity, script, polishByte, polish) in MovedPolishFilmIdentities())
        {
            var anchor = LocalizedFieldScriptIdentityPolicy.FilmAnchor(
                new FieldScriptContext(field, entity, script, polishByte, FieldOpcodeAddressResolver.OpcodeMovieIndex), polish);
            var waiting = new FieldMovieNarrationSample(false, 34, FieldPositionReader.FieldModule, field,
                FieldMovieNarrationPolicy.MovieHandlerStateFreshEntry, -1, 1, FieldMovieNarrationSample.CommandStartMovie, 0, 0);
            var playing = waiting with { MovieActive = true, MovieHandlerState = FieldMovieNarrationPolicy.MovieHandlerStateInProgress };
            var output = new FakeOutput();
            var tracker = new FieldMovieNarrationTracker(_ => output, _ => { }, FieldPositionReader.FieldModule);
            True(tracker.NoteIngress(anchor.FieldId, anchor.EntityId, anchor.ScriptId, anchor.ByteIndex,
                FieldOpcodeAddressResolver.OpcodeMovieIndex, waiting, Start), $"{field}: the Polish film start opens the English track's opportunity");
            Equal(FieldMovieNarrationStartResult.WaitingForNativeStart,
                tracker.Begin(anchor.FieldId, anchor.EntityId, anchor.ScriptId, anchor.ByteIndex, waiting, Start.AddMilliseconds(100)),
                $"{field}: its cue waits for the native film rather than being read at once");
            Equal(FieldMovieNarrationStartResult.Started,
                tracker.Begin(anchor.FieldId, anchor.EntityId, anchor.ScriptId, anchor.ByteIndex, playing, Start.AddMilliseconds(300)),
                $"{field}: the film's own description starts with the film");
            Equal(1, output.Starts, $"{field}: exactly once");

            // What the raw Polish byte did before: no opportunity, so the cue was read at once
            // and the film was then described a second time from its cue schedule.
            var raw = new FieldMovieNarrationTracker(_ => new FakeOutput(), _ => { }, FieldPositionReader.FieldModule);
            Equal(false, raw.NoteIngress(field, entity, script, polishByte, FieldOpcodeAddressResolver.OpcodeMovieIndex, waiting, Start),
                $"{field}: raw Polish byte {polishByte} is not English byte {englishByte}");
            Equal(FieldMovieNarrationStartResult.NotDescribed, raw.Begin(field, entity, script, polishByte, waiting, Start), $"{field}: raw coordinates describe nothing");
        }
    }

    private static void ReviewedLocalizedPrefixesNeverResolveFilmsFromRawOffsets()
    {
        var none = (LocalizedFieldScriptIdentityPolicy.NoAnchor, LocalizedFieldScriptIdentityPolicy.NoAnchor, LocalizedFieldScriptIdentityPolicy.NoAnchor);
        foreach (var (field, englishByte, entity, script, _, polish) in MovedPolishFilmIdentities())
        {
            // The English byte inside the Polish script is some other instruction.
            var anchor = LocalizedFieldScriptIdentityPolicy.FilmAnchor(
                new FieldScriptContext(field, entity, script, englishByte, FieldOpcodeAddressResolver.OpcodeMovieIndex), polish);
            Equal((field, none), (anchor.FieldId, (anchor.EntityId, anchor.ScriptId, anchor.ByteIndex)), $"{field}: English byte {englishByte} is no Polish film anchor");
            True(!FieldMovieNarrationPolicy.TryResolve(anchor.FieldId, anchor.EntityId, anchor.ScriptId, anchor.ByteIndex, out _), "and names no track");
        }

        // Any unreviewed position of a reviewed official prefix is still a film start, so it expires
        // an older opportunity as before, but it can never be a film's anchor. The reviewed Gold
        // Saucer arrival and docking films keep theirs.
        var french = Identity(496, "fr");
        Equal(new FieldCutsceneDescriptionKey(496, -1, -1, -1), LocalizedFieldScriptIdentityPolicy.FilmAnchor(
            new FieldScriptContext(496, 0, 0, 189, FieldOpcodeAddressResolver.OpcodeMovieIndex), french), "an unreviewed position of a reviewed prefix");
        foreach (var at in new[] { 190, 201 })
            Equal(new FieldCutsceneDescriptionKey(496, 0, 0, at), LocalizedFieldScriptIdentityPolicy.FilmAnchor(
                new FieldScriptContext(496, 0, 0, at, FieldOpcodeAddressResolver.OpcodeMovieIndex), french), $"the reviewed film at {at} keeps its anchor");
    }

    private static void StockEchoAndUnknownIdentitiesKeepTheirRawFilmAnchors()
    {
        foreach (var (field, englishByte, entity, script, polishByte) in MovedPolishFilms)
        {
            FieldScriptContext Native(int at) => new(field, entity, script, at, FieldOpcodeAddressResolver.OpcodeMovieIndex);
            Equal(new FieldCutsceneDescriptionKey(field, entity, script, englishByte), LocalizedFieldScriptIdentityPolicy.FilmAnchor(
                Native(englishByte), new(field, EchoSCompatibilityManifest.GetVanillaFingerprint(field)!)), "stock is unchanged");
            Equal(new FieldCutsceneDescriptionKey(field, entity, script, polishByte), LocalizedFieldScriptIdentityPolicy.FilmAnchor(
                Native(polishByte), new(field, EchoSCompatibilityManifest.GetEchoFingerprint(field)!)), "Echo-S is unchanged");
            Equal(new FieldCutsceneDescriptionKey(field, entity, script, englishByte), LocalizedFieldScriptIdentityPolicy.FilmAnchor(
                Native(englishByte), Identity(field, "fr")), "official French runs it at the English byte");
            var others = new LoadedFieldScriptIdentity?[] { null, new(field, new string('A', 64)) }
                .Concat(PolishIdentities(field).Select(p => (LoadedFieldScriptIdentity?)NearMiss(p)))
                .Concat(PolishIdentities(field == 729 ? 730 : 729).Select(p => (LoadedFieldScriptIdentity?)p));
            foreach (var other in others)
                Equal(new FieldCutsceneDescriptionKey(field, entity, script, polishByte), LocalizedFieldScriptIdentityPolicy.FilmAnchor(Native(polishByte), other),
                    "an unreadable, unknown or other field's identity keeps the raw coordinates it always used");
        }
    }

    private static void EveryFilmAnchorIsAReviewedCatalogCue()
    {
        var movies = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .Where(c => c.Opcode == FieldOpcodeAddressResolver.OpcodeMovieIndex).Select(c => c.Key).ToHashSet();
        var anchors = FieldMovieNarrationPolicy.All.Select(t => new FieldCutsceneDescriptionKey(t.FieldId, t.EntityId, t.ScriptId, t.ByteIndex)).ToHashSet();
        Equal(97, anchors.Count, "film narration anchors");
        True(anchors.IsSubsetOf(movies), "every film anchor is an English catalog MOVIE cue, so the reviewed cue rows cover it");
        var mapped = 0;
        foreach (var identity in LocalizedFieldDescriptionCueManifest.ReviewedIdentities)
        {
            foreach (var anchor in anchors.Where(a => a.FieldId == identity.FieldId))
            {
                Equal(1, identity.CanonicalKeys.Count(p => p.Value == anchor), $"field {identity.FieldId} {string.Join('|', identity.Languages)} runs {anchor} exactly once");
                mapped++;
            }
        }

        Equal(596, mapped, "film anchors in reviewed localized identities (518 official, V1.05 and Russian, 78 V1.07)");
    }

    private static void TheX86HostRoutesIdentityAndFilmDecisionsThroughThePolicy()
    {
        var mod = File.ReadAllText(Path.Combine(SourceRoot(), "Ff7.Accessibility.Reloaded", "Mod.cs"));
        True(Regex.IsMatch(mod, @"var observedAreaKnown = [^;]*LocalizedFieldScriptIdentityPolicy\.AllowsDescriptions\(areaIdentity\);"),
            "the x86 description gate accepts exactly the reviewed identities");
        True(!Regex.IsMatch(mod, @"var observedAreaKnown = [^;]*WholeGameEchoNavigationManifest\.ResolveVariant"),
            "and no longer only the navigation identities");
        True(mod.Contains("if (LocalizedFieldScriptIdentityPolicy.IsReviewedLoadedIdentity(observed))", StringComparison.Ordinal),
            "the x86 identity cache keeps reviewed localized identities");
        True(Regex.IsMatch(mod, @"fieldMovieNarrationTracker\?\.NoteIngress\(\s*filmAnchor\.FieldId,\s*filmAnchor\.EntityId,\s*filmAnchor\.ScriptId,\s*filmAnchor\.ByteIndex,"),
            "x86 film ingress asks the film policy about the reviewed anchor");
        True(Regex.IsMatch(mod, @"fieldMovieNarrationTracker\?\.Begin\(\s*filmAnchor\.FieldId,\s*filmAnchor\.EntityId,\s*filmAnchor\.ScriptId,\s*filmAnchor\.ByteIndex,"),
            "x86 film delivery asks the film policy about the reviewed anchor");
        Equal(2, Regex.Matches(mod, @"LocalizedFieldScriptIdentityPolicy\.FilmAnchor\(").Count, "both x86 film call sites use the shared policy");
    }

    private static string SourceRoot()
    {
        var configured = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "Ff7.Accessibility.Reloaded", "Mod.cs")))
            return configured;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Ff7.Accessibility.Reloaded", "Mod.cs")))
                return directory.FullName;
        throw new InvalidOperationException("Set FF7_ACCESSIBILITY_SOURCE_ROOT to the source checkout for the x86 host contract.");
    }

    private sealed class FakeOutput : IFieldMovieNarrationOutput
    {
        private bool playing;
        public int Starts { get; private set; }
        public bool IsPlaying => playing;
        public bool Start(string reason) { Starts++; playing = true; return true; }
        public bool Stop(string reason) { playing = false; return true; }
        public void Dispose() => playing = false;
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}; actual {actual}");
    }
}
