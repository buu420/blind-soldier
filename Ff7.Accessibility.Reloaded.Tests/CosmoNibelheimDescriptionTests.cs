using System.Text.Json;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The 2026-09-21 description batch: ten area cues and three scripted actions for Cosmo
/// Canyon, the Cave of the Gi and Nibelheim.
///
/// <para>These check the things that can silently go wrong - an anchor that does not
/// correspond to the installed script, a cue that never reaches a loaded save, a duplicate
/// key, a recording whose text no longer matches what the mod speaks - rather than
/// restating the catalog contents back at themselves.</para>
/// </summary>
internal static class CosmoNibelheimDescriptionTests
{
    /// <summary>Ten area cues and three scripted actions.</summary>
    private const int ExpectedCues = 13;

    /// <summary>
    /// The recordings that shipped before this batch. Later batches add to this; none of
    /// them may take any of it away.
    /// </summary>
    private const int RecordingsBeforeThisBatch = 618;

    /// <summary>The native opcodes each kind of cue is allowed to be anchored on.</summary>
    private static readonly int[] ActionOpcodes =
    [
        FieldOpcodeAddressResolver.OpcodeRequestIndex,
        FieldOpcodeAddressResolver.OpcodeRequestEwIndex,
        FieldOpcodeAddressResolver.OpcodeVisibilityIndex
    ];

    public static void Run()
    {
        TheBatchIsComposedOnceAndOnly();
        EveryAreaCueIsAnchoredOnTheFieldsOwnMapName();
        EveryActionCueIsAnchoredOnAnInstructionThatPerformsIt();
        TheGenericAreaSurfaceCoversEveryRegion();
        AColdStartAndTheStatusCommandBothReachTheNewAreas();
        EveryCueHasARecordingWithMatchingText();
        AnchorsMatchTheInstalledScripts();
    }

    private static void TheBatchIsComposedOnceAndOnly()
    {
        var areas = FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimAreaDescriptions();
        var actions = FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimActionDescriptions();
        Equal(10, areas.Count, "ten area cues were approved");
        Equal(3, actions.Count, "three action cues were approved");
        Equal(13, FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimDescriptions().Count,
            "the batch is the two together");

        var all = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        foreach (var cue in areas.Concat(actions))
        {
            Equal(1, all.Count(other => other.Key == cue.Key),
                $"{cue.FieldId}:{cue.EntityId}:{cue.ScriptId}:{cue.ByteIndex} must be composed exactly once");
        }

        var duplicates = all
            .GroupBy(cue => cue.Key)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Equal(0, duplicates.Length,
            $"no cue key may be delivered twice: [{string.Join(", ", duplicates)}]");
    }

    /// <summary>
    /// An area description runs from the field's own MPNAM, which every field executes
    /// exactly once from its director entity on entry. Anything else would repeat, or
    /// would not fire on a re-entry at all.
    /// </summary>
    private static void EveryAreaCueIsAnchoredOnTheFieldsOwnMapName()
    {
        foreach (var cue in FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimAreaDescriptions())
        {
            Equal(FieldOpcodeAddressResolver.OpcodeMapNameIndex, cue.Opcode,
                $"field {cue.FieldId} area cue must be anchored on MPNAM");
            Equal(false, cue.IsRecurring, $"field {cue.FieldId} area cue must not be a recurring group");
            Equal(true, cue.Text.Length > 0, $"field {cue.FieldId} area cue must say something");
        }
    }

    private static void EveryActionCueIsAnchoredOnAnInstructionThatPerformsIt()
    {
        foreach (var cue in FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimActionDescriptions())
        {
            Equal(true, ActionOpcodes.Contains(cue.Opcode),
                $"{cue.FieldId}:{cue.EntityId}:{cue.ScriptId} action cue opcode 0x{cue.Opcode:X2} " +
                "must be one the native script actually executes");
            NotEqual(FieldOpcodeAddressResolver.OpcodeMapNameIndex, cue.Opcode,
                "an action is not an area description");
        }
    }

    /// <summary>
    /// The reported hazard: the cold-start tracker and the status command were handed the
    /// Gold Saucer list by name, so a new area's description existed and still never
    /// reached a loaded save.
    /// </summary>
    private static void TheGenericAreaSurfaceCoversEveryRegion()
    {
        var all = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions();
        foreach (var cue in FieldCutsceneDescriptionCatalog.CreateGoldSaucerAreaDescriptions())
        {
            Equal(true, all.Any(other => other.Key == cue.Key),
                $"the generic surface must keep Gold Saucer field {cue.FieldId}");
        }

        foreach (var cue in FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimAreaDescriptions())
        {
            Equal(true, all.Any(other => other.Key == cue.Key),
                $"the generic surface must include field {cue.FieldId}");
        }

        Equal(0, all.Count(cue => cue.Opcode != FieldOpcodeAddressResolver.OpcodeMapNameIndex),
            "the area surface must contain area cues only");

        // Every region that contributes, and nothing beyond them. Written as the sum of the
        // contributing methods rather than a fixed number so a later batch adds one line
        // here instead of silently slipping past a stale total - but a stray cue from
        // anywhere else still fails.
        Equal(
            FieldCutsceneDescriptionCatalog.CreateGoldSaucerAreaDescriptions().Count +
            FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimAreaDescriptions().Count +
            FieldCutsceneContinuationDescriptions.CreateAreaDescriptions().Count,
            all.Count,
            "and nothing else");
        Equal(all.Count, all.Select(cue => cue.Key).Distinct().Count(),
            "the area surface must not repeat a cue");
    }

    private static void AColdStartAndTheStatusCommandBothReachTheNewAreas()
    {
        var coldStart = new FieldAreaDescriptionColdStartTracker(
            FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions());

        foreach (var cue in FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimAreaDescriptions())
        {
            Equal(cue.Text, FieldAreaDescriptionStatus.Describe(cue.FieldId),
                $"the status command must reach field {cue.FieldId}");
            Contains(cue.Text, FieldAreaDescriptionStatus.Append("Exits, 2 available.", cue.FieldId),
                $"and must append it to an existing status for field {cue.FieldId}");
        }

        // Gold Saucer coverage is unchanged.
        foreach (var cue in FieldCutsceneDescriptionCatalog.CreateGoldSaucerAreaDescriptions())
        {
            NotEqual(null, FieldAreaDescriptionStatus.Describe(cue.FieldId),
                $"Gold Saucer field {cue.FieldId} must still be described");
        }

        // A save loaded straight into Bugenhagen's research centre still gets its
        // description, and only once. Nothing ran the field's own MPNAM, which is what
        // "loaded into a room" means.
        var research = FieldCutsceneDescriptionCatalog
            .CreateCosmoThroughNibelheimAreaDescriptions()
            .First(cue => cue.FieldId == 544);
        var start = new DateTime(2026, 9, 21, 2, 0, 0, DateTimeKind.Utc);
        var settled = start + FieldAreaDescriptionColdStartTracker.SettlingWindow + TimeSpan.FromSeconds(1);
        coldStart.Observe(FieldPositionReader.FieldModule, research.FieldId, start);
        var spoken = coldStart.Observe(FieldPositionReader.FieldModule, research.FieldId, settled);
        Equal(research.Text, spoken?.Text, "a cold start in field 544 must describe the room");
        Equal(null, coldStart.Observe(
                FieldPositionReader.FieldModule, research.FieldId, settled + TimeSpan.FromSeconds(1)),
            "and must not repeat it");

        // Arriving through the door instead, where the field runs its own MPNAM, must not
        // produce a second copy.
        var fresh = new FieldAreaDescriptionColdStartTracker(
            FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions());
        fresh.Observe(FieldPositionReader.FieldModule, research.FieldId, start);
        fresh.NoteNativeAnchor(research.FieldId);
        Equal(null, fresh.Observe(FieldPositionReader.FieldModule, research.FieldId, settled),
            "an ordinary arrival is the tracker's job, not the cold start's");
    }

    /// <summary>
    /// Every cue must have a clip whose text is exactly the cue's, or it silently falls
    /// back to synthetic speech. The manifest is keyed on the text itself.
    /// </summary>
    private static void EveryCueHasARecordingWithMatchingText()
    {
        var manifestPath = ResolveAsset("manifest.json");
        if (manifestPath is null)
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var byText = document.RootElement.GetProperty("entries")
            .EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("text").GetString()!,
                entry => entry.GetProperty("file").GetString()!,
                StringComparer.Ordinal);

        foreach (var cue in FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimDescriptions())
        {
            Equal(true, byText.TryGetValue(cue.Text, out var file),
                $"field {cue.FieldId} cue has no recording: \"{cue.Text}\"");
            var clip = ResolveAsset(file!);
            Equal(true, clip is not null && File.Exists(clip),
                $"field {cue.FieldId} recording {file} is not installed");
        }

        // What this batch owns is its own thirteen clips and the 618 that predate them.
        // It used to pin the manifest to exactly 631, which is a claim about every later
        // batch's work: the continuation batch staging its 207 recordings turned that into
        // a failure even though nothing here had regressed. The current total is asserted
        // by ContinuationDescriptionTests, which is the batch that owns it.
        Equal(true, byText.Count >= RecordingsBeforeThisBatch + ExpectedCues,
            $"the {RecordingsBeforeThisBatch} recordings that predate this batch must be " +
            $"retained alongside its {ExpectedCues}; the manifest holds {byText.Count}");
    }

    /// <summary>
    /// Each anchor must still name the native instruction it was read from. Root verified
    /// these against both installed archives; this checks the catalog did not drift away
    /// from that record afterwards.
    /// </summary>
    private static void AnchorsMatchTheInstalledScripts()
    {
        using var document = ReadFixture("native-anchor-verification.json");
        var records = document.RootElement.EnumerateArray().ToArray();
        Equal(ExpectedCues * 2, records.Length,
            $"both installed archives must be on record for all {ExpectedCues} cues");

        var cues = FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimDescriptions();
        foreach (var record in records)
        {
            var field = record.GetProperty("field").GetInt32();
            var entity = record.GetProperty("entity").GetInt32();
            var script = record.GetProperty("script").GetInt32();
            var index = record.GetProperty("byte").GetInt32();
            var opcode = Convert.ToInt32(record.GetProperty("opcode").GetString()![2..], 16);
            var runtime = record.GetProperty("runtime").GetString();
            var matches = cues.Count(cue =>
                cue.FieldId == field && cue.EntityId == entity &&
                cue.ScriptId == script && cue.ByteIndex == index && cue.Opcode == opcode);
            Equal(1, matches,
                $"{runtime} anchor {field}:{entity}:{script}:{index} opcode 0x{opcode:X2} " +
                "must correspond to exactly one catalog cue");
        }
    }

    private static string? ResolveAsset(string file)
    {
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceRoot))
        {
            candidates.Add(Path.Combine(
                sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "cutscene-voice", file));
        }

        candidates.Add(Path.Combine(
            AppContext.BaseDirectory, "Assets", "cutscene-voice", file));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// A frozen fixture from the test project's own output. Missing is a failure, not a
    /// skip: this check used to read an absolute path on one developer's machine and return
    /// early everywhere else, so on a clean checkout it silently did nothing while the run
    /// still printed green.
    /// </summary>
    private static JsonDocument ReadFixture(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cosmo-nibelheim", file);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"the Cosmo/Nibelheim fixture {file} is not in the test output ({path}). The " +
                "test project copies it there; regenerate it with emit-fixtures.py if it has " +
                "been lost.");
        }

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private static void NotEqual<T>(T unexpected, T actual, string label)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            throw new InvalidOperationException($"{label}: must not be {unexpected}");
    }

    private static void Contains(string expected, string? actual, string label)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label}: expected '{expected}' in '{actual}'");
    }
}
