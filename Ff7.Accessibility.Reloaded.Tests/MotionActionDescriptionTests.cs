using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The six footage-verified motion cues in <see cref="FieldMotionActionDescriptions"/>.
///
/// <para>This batch is the first whose anchors sit on animation instructions, so the checks
/// that matter are different from the earlier ones. What has to hold is that the anchor is
/// exactly where the footage was matched and nowhere near it, that the x64 runtime can
/// actually observe the opcode, that a repeated animation callback cannot read the sentence
/// twice, and that the neighbouring instructions which describe the same beat did **not**
/// also get a cue - which is how a line ends up being read three times for three
/// technicians saluting together.</para>
///
/// <para>The installed-archive half runs against whichever game data the harness was pointed
/// at, and is skipped when there is none, exactly like the other description suites.</para>
/// </summary>
internal static class MotionActionDescriptionTests
{
    private const int ExpectedCues = 8;

    /// <summary>
    /// Recordings that shipped before this batch. Later batches add to this; none of them
    /// may take any of it away.
    /// </summary>
    private const int RecordingsBeforeThisBatch = 838;

    /// <summary>
    /// The anchors, with the instruction bytes that must be at each one in both installed
    /// archives. Written out here rather than read from the catalog so that a catalog edit
    /// has to disagree with something.
    /// </summary>
    private static readonly (int Field, int Entity, int Script, int Byte, int Opcode,
        string Bytes, string Text)[] Expected =
    [
        (302, 8, 4, 25, FieldOpcodeAddressResolver.OpcodeAnimHoldIndex, "BA0301",
            "Vincent leaps down into the passage, landing facing the party."),
        (303, 5, 12, 60, FieldOpcodeAddressResolver.OpcodeDfanmIndex, "A20001",
            "The man in a red cloak leaps onto the coffin lid."),
        (303, 5, 15, 0, FieldOpcodeAddressResolver.OpcodeAnimOnceIndex, "AF0501",
            "The man lowers himself back into the open coffin."),
        (308, 4, 6, 6, FieldOpcodeAddressResolver.OpcodeRequestIndex, "0105C3",
            "Sephiroth throws a green orb; Cloud drops to one knee."),
        (526, 7, 1, 202, FieldOpcodeAddressResolver.OpcodeAnimHoldIndex, "BA0501",
            "Beside the bonfire, Barret stands and throws both arms wide."),
        (564, 8, 3, 22, FieldOpcodeAddressResolver.OpcodeRequestIndex, "010BC3",
            "Three blue-uniformed technicians salute Cid as he walks past."),
        (612, 16, 12, 47, FieldOpcodeAddressResolver.OpcodeAnimOnceIndex, "AF0901",
            "Cait Sith's white mount tips sideways and tumbles to the floor."),
        (613, 13, 10, 36, FieldOpcodeAddressResolver.OpcodeAnime1Index, "A30601",
            "Beside the altar, Cait Sith falls forward and stays down."),
    ];

    /// <summary>
    /// Instructions next to an anchor that describe the same beat and must stay silent.
    ///
    /// <para>302 byte 23 makes Vincent visible and byte 28 is his landing; 303 byte 44 is
    /// the jump the byte-60 landing cue covers; 308 byte 9 is the throw animation the
    /// byte-6 cue covers; 526 byte 232 is the arm spread in the same sentence; 564 bytes 25
    /// and 28 are the second and third technicians. Every one of these is a real
    /// instruction on the same execution path, so a cue placed on any of them would read
    /// the same sentence a second and third time.</para>
    /// </summary>
    private static readonly (int Field, int Entity, int Script, int Byte, string Why)[] MustStaySilent =
    [
        (302, 8, 4, 23, "the VISI that shows Vincent, before the leap the sentence describes"),
        (302, 8, 4, 28, "Vincent's landing, the same leap"),
        (303, 5, 12, 44, "the jump out of the coffin, which the landing cue covers"),
        (303, 5, 12, 47, "the offset that carries him, the same jump"),
        (308, 4, 6, 9, "the throw animation, which the byte-6 cue covers"),
        (526, 7, 1, 232, "Barret's arm spread, in the same sentence as rising"),
        (564, 8, 3, 25, "the second technician"),
        (564, 8, 3, 28, "the third technician"),

        // The two the Temple review corrected. Both were proposed at some point and both
        // describe the wrong instant, so they are asserted absent rather than just left
        // out: 612 script 13 is the recovery, not the fall - it opens with his
        // "Owwww..." message and its ANIM!2 at 17 is him getting back up - and 613 byte 18
        // only shows the model, before the walk to the altar and before the fall.
        (612, 16, 13, 17, "Cait Sith getting back up in the recovery script, not the fall"),
        (613, 13, 10, 18, "the VISI that shows the model, before he walks to the altar"),
        (613, 13, 10, 23, "the FMOVE that carries him to the altar, before the fall"),
    ];

    public static void Run()
    {
        TheBatchIsExactlyWhatWasApproved();
        EveryAnchorUsesAnOpcodeTheX64RuntimeDelivers();
        NeighbouringInstructionsOnTheSameBeatStaySilent();
        ARepeatedAnimationCallbackSpeaksOncePerVisit();
        NothingIsDeliveredTwiceAndNothingOlderWasLost();
        ColdStartNeverOffersAMotionCue();
    }

    private static void TheBatchIsExactlyWhatWasApproved()
    {
        var cues = FieldMotionActionDescriptions.CreateAll();
        Equal(ExpectedCues, cues.Count, "the approved motion batch");

        foreach (var (field, entity, script, index, opcode, _, text) in Expected)
        {
            var key = new FieldCutsceneDescriptionKey(field, entity, script, index);
            var match = cues.SingleOrDefault(cue => cue.Key == key);
            Equal(text, match.Text, $"the approved text for {field}:{entity}:{script}:{index}");
            Equal(opcode, match.Opcode, $"the approved opcode for {field}:{entity}:{script}:{index}");
            Equal(false, match.IsRecurring,
                $"{field}:{entity}:{script}:{index} must not be a recurring group");
        }

        // No motion cue is an arrival cue, so none of them may reach the area surface or
        // the status command.
        var areas = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions()
            .Select(cue => cue.Key)
            .ToHashSet();
        foreach (var cue in cues)
        {
            Equal(false, areas.Contains(cue.Key), $"{Describe(cue)} is not an arrival cue");
        }
    }

    /// <summary>
    /// Every anchor has to be one of the sixteen opcode handlers the x64 runtime hooks, or
    /// the cue speaks on the legacy runtime and is silently dropped on the Steam one. This
    /// is the check that keeps BGOFF out and is why two Rocket Town doors are still
    /// deferred.
    /// </summary>
    private static void EveryAnchorUsesAnOpcodeTheX64RuntimeDelivers()
    {
        int[] observable =
        [
            FieldOpcodeAddressResolver.OpcodeRequestIndex,
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex,
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex,
            FieldOpcodeAddressResolver.OpcodeSplitIndex,
            FieldOpcodeAddressResolver.OpcodeWaitIndex,
            FieldOpcodeAddressResolver.OpcodeScroll2DIndex,
            FieldOpcodeAddressResolver.OpcodeFadeIndex,
            FieldOpcodeAddressResolver.OpcodeAnime1Index,
            FieldOpcodeAddressResolver.OpcodeDfanmIndex,
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex,
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex,
            FieldOpcodeAddressResolver.OpcodeCanm1Index,
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex,
            FieldOpcodeAddressResolver.OpcodeCanm2Index,
            FieldOpcodeAddressResolver.OpcodeBackgroundOnIndex,
            FieldOpcodeAddressResolver.OpcodeSoundIndex,
            FieldOpcodeAddressResolver.OpcodeAkaoIndex,
            FieldOpcodeAddressResolver.OpcodeMovieIndex,
        ];

        foreach (var cue in FieldMotionActionDescriptions.CreateAll())
        {
            Equal(true, observable.Contains(cue.Opcode),
                $"{Describe(cue)} opcode 0x{cue.Opcode:X2} is not one the x64 runtime hooks");
        }

        // The negative half: the opcode this batch is most likely to reach for next, and
        // must not. BGOFF performs half the door openings in Rocket Town and is not hooked.
        Equal(false, observable.Contains(0xE1),
            "BGOFF must stay outside the observable set, or this check proves nothing");
        Equal(0,
            FieldMotionActionDescriptions.CreateAll().Count(cue => cue.Opcode == 0xE1),
            "no motion cue may be anchored on BGOFF");
    }

    /// <summary>
    /// The instruction that performs the action is usually one of several on the same beat.
    /// Only one of them may carry a cue.
    /// </summary>
    private static void NeighbouringInstructionsOnTheSameBeatStaySilent()
    {
        var all = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .Select(cue => cue.Key)
            .ToHashSet();

        foreach (var (field, entity, script, index, why) in MustStaySilent)
        {
            var key = new FieldCutsceneDescriptionKey(field, entity, script, index);
            Equal(false, all.Contains(key),
                $"{field}:{entity}:{script}:{index} must stay silent - it is {why}");
        }

        // The Temple correction, stated as an equality rather than only as an absence: the
        // fall cue is in script 12 and no motion cue is in script 13 at all.
        var temple = FieldMotionActionDescriptions.CreateAll()
            .Where(cue => cue.FieldId == 612)
            .ToArray();
        Equal(1, temple.Length, "field 612 carries exactly one motion cue");
        Equal(12, temple[0].ScriptId,
            "the 612 fall cue belongs to script 12, the walk and fall, not script 13");

        // And the control: the anchors themselves are in the catalog, so the check above is
        // not passing because the whole field is absent.
        foreach (var (field, entity, script, index, _, _, _) in Expected)
        {
            Equal(true, all.Contains(new FieldCutsceneDescriptionKey(field, entity, script, index)),
                $"{field}:{entity}:{script}:{index} must be in the catalog");
        }
    }

    /// <summary>
    /// An animation handler yields and is re-entered on every frame while the animation
    /// runs, so the same key arrives at the tracker over and over. Reading the sentence
    /// each time would be unbearable; the tracker answers once per key per visit, and this
    /// pins that for the opcodes this batch actually uses.
    /// </summary>
    private static void ARepeatedAnimationCallbackSpeaksOncePerVisit()
    {
        foreach (var cue in FieldMotionActionDescriptions.CreateAll())
        {
            var tracker = new FieldCutsceneDescriptionTracker(
                FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions());
            var context = new FieldScriptContext(
                cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, (byte)cue.Opcode);

            Equal(cue.Text, tracker.Observe(context)?.Text, $"{Describe(cue)} speaks once");
            for (var frame = 0; frame < 24; frame++)
            {
                Equal(null, tracker.Observe(context),
                    $"{Describe(cue)} must stay silent on re-entry {frame + 1}");
            }

            // A neighbouring byte and a different opcode at the same byte are different
            // instructions and say nothing.
            Equal(null, tracker.Observe(context with { ByteIndex = cue.ByteIndex + 1 }),
                $"{Describe(cue)} must not answer for the next byte");
            Equal(null, tracker.Observe(context with { Opcode = (byte)(cue.Opcode ^ 0x01) }),
                $"{Describe(cue)} must not answer for a different opcode");

            // Leaving the field and coming back is a new visit.
            tracker.Observe(new FieldScriptContext(cue.FieldId + 1, 0, 0, 0, 0x24));
            Equal(cue.Text, tracker.Observe(context)?.Text,
                $"{Describe(cue)} speaks again on a real re-entry");
            Equal(null, tracker.Observe(context), $"{Describe(cue)} and only once");
        }
    }

    private static void NothingIsDeliveredTwiceAndNothingOlderWasLost()
    {
        var all = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        foreach (var cue in FieldMotionActionDescriptions.CreateAll())
        {
            Equal(1, all.Count(other => other.Key == cue.Key),
                $"{Describe(cue)} must be composed exactly once");
        }

        var duplicates = all
            .GroupBy(cue => cue.Key)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Equal(0, duplicates.Length,
            $"no cue key may be delivered twice: [{string.Join(", ", duplicates)}]");

        // The two cues that share a field with existing work, checked by name rather than
        // by counting: Sephiroth's departure at byte 70 of the very script the orb cue is
        // anchored in, and the coffin opening in the field the leap and recline cues are in.
        Equal(true,
            all.Any(cue => cue.FieldId == 308 && cue.EntityId == 4 && cue.ScriptId == 6
                           && cue.ByteIndex == 70),
            "the shipped 308 departure cue must survive alongside the new orb cue");
        Equal(true,
            all.Any(cue => cue.FieldId == 303 && cue.EntityId == 0 && cue.ScriptId == 3
                           && cue.ByteIndex == 4),
            "the shipped 303 coffin-opening cue must survive alongside the new coffin cues");
    }

    /// <summary>
    /// The cold-start fallback exists because the x64 runtime has no MPNAM handler, so every
    /// arrival description is delivered from a settled field observation instead. It takes
    /// arrival cues only, and a motion cue reaching it would be spoken on entry to a room
    /// rather than at its moment.
    /// </summary>
    private static void ColdStartNeverOffersAMotionCue()
    {
        var start = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var settled = start + FieldAreaDescriptionColdStartTracker.SettlingWindow
            + TimeSpan.FromSeconds(1);
        var motion = FieldMotionActionDescriptions.CreateAll().Select(cue => cue.Text).ToHashSet();

        foreach (var field in FieldMotionActionDescriptions.CreateAll()
                     .Select(cue => cue.FieldId)
                     .Distinct())
        {
            var coldStart = new FieldAreaDescriptionColdStartTracker(
                FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions());
            coldStart.Observe(FieldPositionReader.FieldModule, field, start);
            var offered = coldStart.Observe(FieldPositionReader.FieldModule, field, settled);
            Equal(false, offered is { } cue && motion.Contains(cue.Text),
                $"the cold start must not offer a motion cue in field {field}");
        }
    }

    /// <summary>
    /// The anchors against whichever installed archive the harness was pointed at, through
    /// the production script reader. Every byte must be an instruction boundary holding the
    /// bytes the footage was matched to.
    /// </summary>
    public static void RunAgainstInstalledArchive()
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            Console.WriteLine("motion action anchors: no installed game data; archive check skipped.");
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(gameRoot);
        foreach (var (field, entity, script, index, opcode, bytes, _) in Expected)
        {
            var opcodes = catalog.ReadScriptOpcodes(field, entity, script);
            Equal(true, opcodes.Count > 0,
                $"{field}:{entity}:{script} must exist in {gameRoot}");
            var native = opcodes.SingleOrDefault(op => op.ByteIndex == index);
            Equal(index, native.ByteIndex,
                $"{field}:{entity}:{script}:{index} must land on an instruction boundary");
            Equal(opcode, (int)native.Opcode,
                $"{field}:{entity}:{script}:{index} must be the opcode the catalog claims");
            Equal(bytes, Convert.ToHexString(native.Bytes.ToArray()),
                $"{field}:{entity}:{script}:{index} must hold the bytes the footage was matched to");
        }

        // The silent neighbours have to be real instructions too, or "no cue is on them" is
        // a statement about nothing.
        foreach (var (field, entity, script, index, why) in MustStaySilent)
        {
            var opcodes = catalog.ReadScriptOpcodes(field, entity, script);
            Equal(true, opcodes.Any(op => op.ByteIndex == index),
                $"{field}:{entity}:{script}:{index} must be a real instruction - it is {why}");
        }

        Console.WriteLine(
            $"motion action anchors: {Expected.Length} verified against {gameRoot}.");
    }

    /// <summary>
    /// The eight recordings. Root stages the audio separately, so this reports and returns
    /// while none of it is there - and requires every one of the eight once any of it is,
    /// which is the state a run after staging is in.
    /// </summary>
    public static void RunPayloadReadiness()
    {
        var manifestPath = ResolveAsset("manifest.json");
        if (manifestPath is null)
        {
            Console.WriteLine("motion action payload: manifest not found; skipped.");
            return;
        }

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
        var byText = document.RootElement.GetProperty("entries")
            .EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("text").GetString()!,
                entry => entry.GetProperty("file").GetString()!,
                StringComparer.Ordinal);

        Equal(true, byText.Count >= RecordingsBeforeThisBatch,
            $"the {RecordingsBeforeThisBatch} recordings that predate this batch must " +
            $"remain; found {byText.Count}");

        var cues = FieldMotionActionDescriptions.CreateAll();
        var texts = cues.Select(cue => cue.Text).Distinct(StringComparer.Ordinal).ToArray();
        Equal(ExpectedCues, texts.Length,
            "each motion cue has its own sentence, so each needs its own recording");

        var staged = texts.Where(byText.ContainsKey).ToArray();
        if (staged.Length == 0)
        {
            Console.WriteLine(
                $"motion action payload: none of the {ExpectedCues} recordings are staged " +
                "yet; catalog and native checks passed, audio still pending.");
            return;
        }

        // Partly staged is a failure, not a pending state: it means a clip went missing
        // rather than that the batch has not been rendered.
        Equal(ExpectedCues, staged.Length,
            $"{ExpectedCues - staged.Length} of the {ExpectedCues} motion recordings are " +
            "missing from the manifest, so the batch is staged but incomplete; first: " +
            $"\"{texts.FirstOrDefault(text => !byText.ContainsKey(text))}\"");

        foreach (var cue in cues)
        {
            var file = byText[cue.Text];
            var clip = ResolveAsset(file);
            Equal(true, clip is not null && File.Exists(clip),
                $"{Describe(cue)} recording {file} is not installed");
        }

        Equal(true, byText.Count >= RecordingsBeforeThisBatch + ExpectedCues,
            $"the manifest must hold at least the {RecordingsBeforeThisBatch} earlier " +
            $"recordings and this batch\'s {ExpectedCues}; found {byText.Count}");
        Console.WriteLine(
            $"motion action payload: {ExpectedCues} recordings installed; " +
            $"manifest holds {byText.Count}.");
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

    private static string Describe(FieldCutsceneDescriptionCue cue) =>
        $"{cue.FieldId}:{cue.EntityId}:{cue.ScriptId}:{cue.ByteIndex}";

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"motion action descriptions - {label}: expected {expected}, got {actual}");
        }
    }
}
