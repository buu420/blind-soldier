using System.Text.Json;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The 2026-09-21 continuation batch: 208 arrival descriptions and 6 scripted moments.
///
/// <para>Split deliberately. <see cref="RunCatalogAndNative"/> is everything that can be
/// decided from the source tree, the frozen fixtures and the installed game archives, and
/// must always pass. <see cref="RunPayloadReadiness"/> is about recordings that root stages
/// separately; it reports rather than passes when the audio for this batch is not installed
/// yet, so an unrendered clip can never be mistaken for a green run.</para>
///
/// <para>The two canonical files this checks against - the shipped baseline and the approved
/// batch - are read from frozen, sanitized copies in <c>Fixtures/continuation</c> that are
/// copied into both test outputs. They used to be read by absolute path from one machine's
/// working directory, so on any other machine those checks silently did nothing; a missing
/// fixture is now a failure. Only the fields asserted on were copied across: the Descript job
/// ids, review notes and alternative takes stayed in the working directory where they
/// belong.</para>
/// </summary>
internal static class ContinuationDescriptionTests
{
    private const int ExpectedAreas = 208;
    private const int ExpectedActions = 6;
    private const int LegacyAreaCues = 24;
    private const int LegacyTotalCues = 346;
    private const int BaselineRecordings = 631;

    /// <summary>
    /// 214 cues sharing 207 recordings: six arrival texts are shared by the two screens of
    /// one room, and Sephiroth's two departures share one sentence.
    /// </summary>
    private const int DistinctTexts = 207;

    /// <summary>
    /// The opcodes an action cue may be anchored on: the two request forms and the
    /// visibility instruction. Everything else - and every animation opcode in particular -
    /// is rejected, because an animation number is not evidence of what the player sees.
    /// </summary>
    private static readonly int[] PermittedActionOpcodes =
    [
        FieldOpcodeAddressResolver.OpcodeRequestIndex,
        FieldOpcodeAddressResolver.OpcodeRequestEwIndex,
        FieldOpcodeAddressResolver.OpcodeVisibilityIndex
    ];

    /// <summary>
    /// The single reviewed exception to the rule above, and it is an exception about
    /// <em>when</em>, never about <em>what</em>.
    ///
    /// <para>In kuro_8 Sephiroth is made visible by VISI 01 at byte 95, but that byte sits
    /// between two fades - the fade-in only finishes at the FADEW at byte 137 - so speaking
    /// there describes a screen the player cannot see yet. Between that FADEW and his own
    /// line at byte 174 the script runs nothing but animation and offset instructions, so
    /// there is no request or visibility instruction left to use. Byte 142 is therefore a
    /// clock and nothing else: what happens is established by the VISI, by the native actor
    /// name and by the line he speaks straight afterwards, and the sentence says only that
    /// he appears.</para>
    ///
    /// <para>It is pinned to one exact anchor deliberately. Any other cue on an animation
    /// opcode - including another one in this same field and script - still fails, and so
    /// does this exception outliving the cue it was granted for.</para>
    /// </summary>
    private static readonly (int FieldId, int EntityId, int ScriptId, int ByteIndex, int Opcode)
        TimingOnlyAnchor = (611, 21, 5, 142, FieldOpcodeAddressResolver.OpcodeCanm2Index);

    private static readonly int[] AnimationOpcodes =
    [
        FieldOpcodeAddressResolver.OpcodeDfanmIndex,
        FieldOpcodeAddressResolver.OpcodeAnime1Index,
        FieldOpcodeAddressResolver.OpcodeAnimOnceIndex,
        FieldOpcodeAddressResolver.OpcodeCanm1Index,
        FieldOpcodeAddressResolver.OpcodeAnimHoldIndex,
        FieldOpcodeAddressResolver.OpcodeCanm2Index
    ];

    public static void RunCatalogAndNative()
    {
        TheBatchHasTheApprovedShape();
        TheTimingOnlyAnchorSpeaksOnceThroughItsYieldingCallback();
        NothingIsDuplicatedAnywhere();
        EveryLegacyCueSurvived();
        EveryAnchorAndTextMatchesWhatWasApproved();
        OnlyTheRightOpcodesAreAccepted();
        AnAreaSpeaksOncePerVisitAndAnActionOncePerRun();
        ColdStartAndStatusReachEveryNewArea();
        AnchorsResolveInTheInstalledArchive();
    }

    private static void TheBatchHasTheApprovedShape()
    {
        var areas = FieldCutsceneContinuationDescriptions.CreateAreaDescriptions();
        var actions = FieldCutsceneContinuationDescriptions.CreateActionDescriptions();
        Equal(ExpectedAreas, areas.Count, "approved arrival descriptions");
        Equal(ExpectedActions, actions.Count, "approved scripted moments");
        Equal(ExpectedAreas + ExpectedActions,
            FieldCutsceneContinuationDescriptions.CreateAll().Count, "the batch is the two together");

        // Both generic collections, which is what the runtimes consume.
        var allAreas = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions();
        Equal(LegacyAreaCues + ExpectedAreas, allAreas.Count,
            "every previously shipped area plus this batch");
        Equal(0, allAreas.Count(cue => cue.Opcode != FieldOpcodeAddressResolver.OpcodeMapNameIndex),
            "the area surface holds arrival cues only");

        var all = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        Equal(LegacyTotalCues + ExpectedAreas + ExpectedActions, all.Count,
            "the whole catalog is the old total plus this batch");
        foreach (var cue in FieldCutsceneContinuationDescriptions.CreateAll())
        {
            Equal(1, all.Count(other => other.Key == cue.Key),
                $"{Describe(cue)} must be composed exactly once");
        }
    }

    /// <summary>
    /// The one animation-anchored cue, and the property that makes anchoring on an animation
    /// callback safe at all.
    ///
    /// <para>CANM!2 is not a one-shot instruction. Its handler starts the animation, yields,
    /// and is re-entered on every frame until the animation finishes, so the same field,
    /// entity, script and byte arrive at the tracker over and over for as long as Sephiroth
    /// is animating. Speaking on each of those would read the sentence a dozen times. The
    /// tracker already answers once per key per visit, which is why this anchor is usable;
    /// this pins that behaviour to this cue rather than leaving it implied.</para>
    ///
    /// <para>It also pins the two things next to it. Byte 95, the VISI that actually shows
    /// him, must stay silent - it is the evidence for the sentence, not the moment to say it,
    /// because the fade-in has not finished there. And the second CANM!2 at byte 159 in the
    /// same script must stay silent too, which is the concrete case the narrow exception is
    /// narrow about: one anchor, not one opcode.</para>
    /// </summary>
    private static void TheTimingOnlyAnchorSpeaksOnceThroughItsYieldingCallback()
    {
        var cue = FieldCutsceneContinuationDescriptions.CreateActionDescriptions()
            .Single(IsTheTimingOnlyAnchor);
        Equal("Sephiroth appears.", cue.Text, "the timing-only cue says exactly this and no more");

        var tracker = new FieldCutsceneDescriptionTracker(
            FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions());
        var callback = new FieldScriptContext(
            cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, (byte)cue.Opcode);

        Equal(cue.Text, tracker.Observe(callback)?.Text, "the first callback speaks");
        for (var frame = 0; frame < 32; frame++)
        {
            Equal(null, tracker.Observe(callback),
                $"re-entry {frame + 1} of the yielding callback must stay silent");
        }

        // The instruction that proves what happens, which is deliberately not the anchor.
        Equal(null,
            tracker.Observe(new FieldScriptContext(
                cue.FieldId, cue.EntityId, cue.ScriptId, 95,
                (byte)FieldOpcodeAddressResolver.OpcodeVisibilityIndex)),
            "the VISI between the fades must not speak");

        // The other animation callback in the very same script.
        Equal(null,
            tracker.Observe(new FieldScriptContext(
                cue.FieldId, cue.EntityId, cue.ScriptId, 159, (byte)cue.Opcode)),
            "the second CANM!2 in the same script must not speak");

        // A different opcode at the anchor byte is a different instruction entirely.
        Equal(null,
            tracker.Observe(callback with
            {
                Opcode = (byte)FieldOpcodeAddressResolver.OpcodeRequestIndex
            }),
            "another opcode at the same byte must not speak");

        // Leaving the Temple and coming back is a new visit, and it speaks once again.
        tracker.Observe(new FieldScriptContext(cue.FieldId + 1, 0, 0, 0, 0x24));
        Equal(cue.Text, tracker.Observe(callback)?.Text, "a real re-entry arms it again");
        Equal(null, tracker.Observe(callback), "and only once");
    }

    private static void NothingIsDuplicatedAnywhere()
    {
        var duplicates = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .GroupBy(cue => cue.Key)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Equal(0, duplicates.Length,
            $"no cue key may be delivered twice: [{string.Join(", ", duplicates)}]");

        // Six texts are deliberately shared by two fields each, where the same room is
        // reached on two screens: 323/324 nvmkin21-22, 712/713 itown1a-12, 736/778
        // tunnel_4-6, 706/708 trnad_51-53, 709/710 woa_1-2 and 287/289 niv_ti2-ti4. A seventh
        // is shared by Sephiroth's two departures, 307 and 308 - different moments, the same
        // visible action, so one recording. The
        // manifest is keyed on the text, so those share one recording, which is what should
        // happen. Pinning the distinct count means an accidental duplicate somewhere else
        // still fails here rather than quietly costing a clip.
        Equal(DistinctTexts,
            FieldCutsceneContinuationDescriptions.CreateAll()
                .Select(cue => cue.Text)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            "the batch must hold exactly the approved number of distinct recording texts");
    }

    /// <summary>Nothing that already shipped may have been dropped or reworded.</summary>
    private static void EveryLegacyCueSurvived()
    {
        using var inventory = ReadFixture("existing-description-cues.json");
        var all = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .ToDictionary(cue => $"{cue.FieldId}:{cue.EntityId}:{cue.ScriptId}:{cue.ByteIndex}",
                cue => cue.Text, StringComparer.Ordinal);
        var cues = inventory.RootElement.GetProperty("cues").EnumerateArray().ToArray();
        Equal(LegacyTotalCues, cues.Length, "the recorded baseline is the 346 shipped cues");
        foreach (var cue in cues)
        {
            var key = cue.GetProperty("key").GetString()!;
            Equal(true, all.TryGetValue(key, out var text), $"shipped cue {key} must still exist");
            Equal(cue.GetProperty("text").GetString(), text, $"shipped cue {key} must be unchanged");
        }
    }

    private static void EveryAnchorAndTextMatchesWhatWasApproved()
    {
        using var approved = ReadFixture("approved-descriptions.json");
        var batch = FieldCutsceneContinuationDescriptions.CreateAll()
            .ToDictionary(cue => cue.Key);
        var seen = 0;
        foreach (var entry in approved.RootElement.EnumerateArray())
        {
            var anchor = entry.GetProperty("anchor").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var key = new FieldCutsceneDescriptionKey(anchor[0], anchor[1], anchor[2], anchor[3]);
            Equal(true, batch.TryGetValue(key, out var cue),
                $"approved {entry.GetProperty("id").GetString()} must be in the catalog");
            Equal(entry.GetProperty("text").GetString(), cue.Text,
                $"approved text for {entry.GetProperty("id").GetString()} must be verbatim");
            Equal(anchor[4], cue.Opcode,
                $"approved opcode for {entry.GetProperty("id").GetString()}");
            seen++;
        }

        Equal(batch.Count, seen, "every catalog entry must come from the approved file");
    }

    private static void OnlyTheRightOpcodesAreAccepted()
    {
        foreach (var cue in FieldCutsceneContinuationDescriptions.CreateAreaDescriptions())
        {
            Equal(FieldOpcodeAddressResolver.OpcodeMapNameIndex, cue.Opcode,
                $"{Describe(cue)} must be anchored on the field's own MPNAM");
            Equal(false, cue.IsRecurring, $"{Describe(cue)} must not be a recurring group");
        }

        var timingExceptionUsed = 0;
        foreach (var cue in FieldCutsceneContinuationDescriptions.CreateActionDescriptions())
        {
            if (IsTheTimingOnlyAnchor(cue))
            {
                timingExceptionUsed++;
                continue;
            }

            Equal(true, PermittedActionOpcodes.Contains(cue.Opcode),
                $"{Describe(cue)} opcode 0x{cue.Opcode:X2} is not one an action may be anchored on");
            Equal(false, AnimationOpcodes.Contains(cue.Opcode),
                $"{Describe(cue)} must not be anchored on an animation instruction");
        }

        // The exception is for one anchor. It has to be used, so it cannot outlive its cue,
        // and it has to be the only one, so it cannot spread.
        Equal(1, timingExceptionUsed,
            "the timing-only anchor must appear exactly once in the action catalog");
        var animationAnchored = FieldCutsceneContinuationDescriptions.CreateAll()
            .Where(cue => AnimationOpcodes.Contains(cue.Opcode))
            .ToArray();
        Equal(1, animationAnchored.Length,
            "the timing-only anchor must be the only animation-anchored cue in this batch: " +
            $"[{string.Join(", ", animationAnchored.Select(Describe))}]");
        Equal(true, IsTheTimingOnlyAnchor(animationAnchored[0]),
            "and it must be that exact anchor, not merely that opcode");

        // A field the mod does not know is never described.
        foreach (var cue in FieldCutsceneContinuationDescriptions.CreateAll())
        {
            Equal(true, cue.FieldId is > 0 and < 1000, $"{Describe(cue)} field id is out of range");
            Equal(true, cue.Text.Length > 0, $"{Describe(cue)} must say something");
        }
    }

    /// <summary>
    /// An arrival is spoken once per visit and an action once per run, and leaving the field
    /// and coming back starts again. This is the tracker both runtimes drive.
    /// </summary>
    private static void AnAreaSpeaksOncePerVisitAndAnActionOncePerRun()
    {
        var tracker = new FieldCutsceneDescriptionTracker(
            FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions());
        var area = FieldCutsceneContinuationDescriptions.CreateAreaDescriptions()[0];
        var context = new FieldScriptContext(
            area.FieldId, area.EntityId, area.ScriptId, area.ByteIndex, (byte)area.Opcode);

        Equal(area.Text, tracker.Observe(context)?.Text, "an arrival speaks on entry");
        Equal(null, tracker.Observe(context), "and not again while the player is in the room");
        Equal(null, tracker.Observe(context with { ByteIndex = area.ByteIndex + 1 }),
            "a neighbouring byte says nothing");
        Equal(null, tracker.Observe(context with { Opcode = (byte)(area.Opcode ^ 1) }),
            "a different opcode at the same byte says nothing");

        // Leaving and coming back is a new visit.
        tracker.Observe(new FieldScriptContext(area.FieldId + 1, 0, 0, 0, 0x24));
        Equal(area.Text, tracker.Observe(context)?.Text, "coming back describes the room again");

        foreach (var action in FieldCutsceneContinuationDescriptions.CreateActionDescriptions())
        {
            var fresh = new FieldCutsceneDescriptionTracker(
                FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions());
            var actionContext = new FieldScriptContext(
                action.FieldId, action.EntityId, action.ScriptId, action.ByteIndex, (byte)action.Opcode);
            Equal(action.Text, fresh.Observe(actionContext)?.Text, $"{Describe(action)} speaks once");
            for (var repeat = 0; repeat < 4; repeat++)
            {
                Equal(null, fresh.Observe(actionContext),
                    $"{Describe(action)} must not repeat while the request yields");
            }
        }
    }

    private static void ColdStartAndStatusReachEveryNewArea()
    {
        var start = new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);
        var settled = start + FieldAreaDescriptionColdStartTracker.SettlingWindow + TimeSpan.FromSeconds(1);
        foreach (var cue in FieldCutsceneContinuationDescriptions.CreateAreaDescriptions())
        {
            Equal(cue.Text, FieldAreaDescriptionStatus.Describe(cue.FieldId),
                $"the status command must reach field {cue.FieldId}");

            var coldStart = new FieldAreaDescriptionColdStartTracker(
                FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions());
            coldStart.Observe(FieldPositionReader.FieldModule, cue.FieldId, start);
            Equal(cue.Text,
                coldStart.Observe(FieldPositionReader.FieldModule, cue.FieldId, settled)?.Text,
                $"a save loaded into field {cue.FieldId} must describe the room");
            Equal(null,
                coldStart.Observe(FieldPositionReader.FieldModule, cue.FieldId,
                    settled + TimeSpan.FromSeconds(1)),
                $"and must not repeat it in field {cue.FieldId}");
        }

        // Everything that shipped before is still reachable.
        foreach (var cue in FieldCutsceneDescriptionCatalog.CreateGoldSaucerAreaDescriptions()
                     .Concat(FieldCutsceneDescriptionCatalog.CreateCosmoThroughNibelheimAreaDescriptions()))
        {
            NotEqual(null, FieldAreaDescriptionStatus.Describe(cue.FieldId),
                $"field {cue.FieldId} must still be described");
        }
    }

    /// <summary>
    /// The anchors against the archive this run was pointed at, through the production
    /// script reader. Runs for whichever installed root the harness was given.
    /// </summary>
    private static void AnchorsResolveInTheInstalledArchive()
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(gameRoot);
        foreach (var cue in FieldCutsceneContinuationDescriptions.CreateAll())
        {
            var opcodes = catalog.ReadScriptOpcodes(cue.FieldId, cue.EntityId, cue.ScriptId);
            Equal(true, opcodes.Count > 0, $"{Describe(cue)} script must exist in {gameRoot}");
            var native = opcodes.SingleOrDefault(op => op.ByteIndex == cue.ByteIndex);
            Equal(cue.ByteIndex, native.ByteIndex,
                $"{Describe(cue)} must land on an instruction boundary");
            Equal(cue.Opcode, (int)native.Opcode,
                $"{Describe(cue)} must be the opcode the catalog claims");
        }
    }

    /// <summary>
    /// Recordings. Root stages the audio separately, so this reports and returns rather than
    /// failing while the batch is still being rendered - and fails loudly once it is there
    /// but incomplete. It is never part of the catalog run.
    /// </summary>
    public static void RunPayloadReadiness()
    {
        var manifestPath = ResolveAsset("manifest.json");
        if (manifestPath is null)
        {
            Console.WriteLine("payload readiness: manifest not found; skipped.");
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var byText = document.RootElement.GetProperty("entries")
            .EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("text").GetString()!,
                entry => entry.GetProperty("file").GetString()!,
                StringComparer.Ordinal);

        Equal(true, byText.Count >= BaselineRecordings,
            $"the {BaselineRecordings} recordings that already shipped must remain; found {byText.Count}");

        var batch = FieldCutsceneContinuationDescriptions.CreateAll();
        var missing = batch.Where(cue => !byText.ContainsKey(cue.Text)).ToArray();
        if (missing.Length == batch.Count)
        {
            Console.WriteLine(
                $"payload readiness: none of the {DistinctTexts} continuation recordings are staged " +
                "yet; catalog and native checks passed, audio still pending.");
            return;
        }

        Equal(0, missing.Length,
            $"{missing.Length} of {batch.Count} continuation recordings are missing, so the batch " +
            $"is staged but incomplete; first: \"{missing.FirstOrDefault().Text}\"");

        foreach (var cue in batch)
        {
            var clip = ResolveAsset(byText[cue.Text]);
            Equal(true, clip is not null && File.Exists(clip),
                $"{Describe(cue)} recording {byText[cue.Text]} is not installed");
        }

        Equal(BaselineRecordings + DistinctTexts, byText.Count,
            "the manifest must be the baseline plus exactly this batch's distinct texts");
        Console.WriteLine($"payload readiness: {DistinctTexts} continuation recordings installed.");
    }

    private static string Describe(FieldCutsceneDescriptionCue cue) =>
        $"{cue.FieldId}:{cue.EntityId}:{cue.ScriptId}:{cue.ByteIndex}";

    private static bool IsTheTimingOnlyAnchor(FieldCutsceneDescriptionCue cue) =>
        (cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode) == TimingOnlyAnchor;

    /// <summary>
    /// A frozen fixture from the test project's own output. Missing is a failure, not a
    /// skip: a check that silently does nothing is worse than one that is not there, because
    /// it reads as green.
    /// </summary>
    private static JsonDocument ReadFixture(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "continuation", file);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"the continuation fixture {file} is not in the test output ({path}). The test " +
                "project copies it there; regenerate it with emit-fixtures.py if it has been " +
                "lost.");
        }

        return JsonDocument.Parse(File.ReadAllText(path));
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

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Assets", "cutscene-voice", file));
        return candidates.FirstOrDefault(File.Exists);
    }

    internal static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private static void NotEqual<T>(T unexpected, T actual, string label)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            throw new InvalidOperationException($"{label}: must not be {unexpected}");
    }
}
