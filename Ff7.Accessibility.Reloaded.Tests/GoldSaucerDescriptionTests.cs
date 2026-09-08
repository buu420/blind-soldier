using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Reviewed first-visit Gold Saucer scene descriptions. Each anchor is checked
/// against the installed field script, so a cue can never drift onto a byte that
/// carries a different native instruction.
/// </summary>
internal static class GoldSaucerDescriptionTests
{
    private static readonly (int Field, int Entity, int Script, int Byte, int Opcode)[] Anchors =
    [
        (497, 0, 0, 638, 0x02), // Terminal Floor reveal, inside the GameMoment 436 gate.
        (497, 0, 0, 644, 0x01), // Barret arrives.
        (497, 0, 0, 650, 0x01), // Red XIII arrives.
        (497, 0, 0, 656, 0x01), // Tifa arrives.
        (497, 0, 0, 665, 0x01), // Yuffie, behind the native IFMEMBQ availability test at 662.
        (497, 0, 0, 671, 0x03), // Aeris arrives.
        (497, 0, 0, 848, 0x03), // Barret leaves through a tube; 826 already made him unavailable.
        (505, 0, 0, 81, 0x02),  // Cait Sith approaches, inside the GameMoment 440 gate.
        (499, 0, 0, 68, 0x03),  // Battle Square exterior, after the pan, inside the 442 gate.
        (499, 0, 0, 103, 0x09), // Cloud kneels beside the fallen person.
        (501, 0, 0, 51, 0x01),  // Arena lobby bodies, inside the 442 gate.
        (501, 0, 0, 118, 0x02), // Dio and the guards close in.
        (502, 0, 0, 49, 0x01),  // The raised arena platform, inside the 442 gate.
        (502, 0, 0, 61, 0x01),  // kei2 runs on.
        (502, 0, 0, 67, 0x01),  // kei3 runs on.
        (502, 0, 0, 110, 0x03), // me1 appears.
        (502, 0, 0, 128, 0x01), // me2 leaps down.
        (502, 0, 0, 131, 0x03), // me3 leaps down.
        (502, 0, 0, 152, 0x01), // All three close in.
        (504, 0, 0, 28, 0x03),  // kei1 works the switch.
        (504, 0, 0, 48, 0x02),  // The guard leaps in with Cloud held against it.
        (486, 6, 5, 10, 0x02),  // Dio faces Cloud, on sen's one-shot LINE trigger.
        (486, 6, 5, 464, 0x02), // Dio walks off.

        // Mog House visible actions, mogu_1 event(6) Main. Entity 8 is Mog and
        // entity 9 the visiting moogle; every byte is the request that starts one
        // animation, and none of them repeats a narrator box.
        (508, 6, 0, 43, 0x03),  // Mog leaves the house.
        (508, 6, 0, 188, 0x03), // Failed hop, underfed branch.
        (508, 6, 0, 199, 0x03), // Failed hop, overfed branch.
        (508, 6, 0, 263, 0x03), // The success branch the script only reaches on a good attempt.
        (508, 6, 0, 284, 0x03), // Landing and going back inside.
        (508, 6, 0, 306, 0x03), // The visiting moogle approaches.
        (508, 6, 0, 322, 0x03), // She knocks.
        (508, 6, 0, 328, 0x03), // Mog comes back out.
        // The rest of the show. esa(7) Main byte 82 is the accepted feed; the others
        // are event(6) Main, past where the earlier eight cues stopped.
        (508, 7, 0, 82, 0x03),  // Mog eats the nut.
        (508, 6, 0, 536, 0x03), // The second flight's circling.
        (508, 6, 0, 628, 0x03), // Mog comes out of the house.
        (508, 6, 0, 644, 0x03), // The visitor comes out; the narrator names her later.
        (508, 6, 0, 666, 0x02), // The two of them walk away.
        (508, 6, 0, 699, 0x01), // The first of the small moogles.
        (508, 6, 0, 919, 0x03), // The last one.

        // Wonder Catcher, both cabinets: games_1 cloud(1) scripts 8 and 9.
        (506, 1, 8, 113, 0xAF), (506, 1, 8, 127, 0xBC),
        (506, 1, 8, 140, 0xBA), (506, 1, 8, 389, 0xBC),
        (506, 1, 9, 108, 0xAF), (506, 1, 9, 122, 0xBC),
        (506, 1, 9, 135, 0xBA), (506, 1, 9, 384, 0xBC),

        // The five Round Square gondola films, each on both companion branches.
        (489, 0, 0, 155, 0xF9), (489, 0, 0, 345, 0xF9), // gold2, film 6.
        (489, 0, 0, 210, 0xF9), (489, 0, 0, 429, 0xF9), // gold3, film 7.
        (489, 0, 0, 288, 0xF9), (489, 0, 0, 612, 0xF9), // gold4, film 8.
        (490, 0, 0, 91, 0xF9), (490, 0, 0, 223, 0xF9),  // gold5, film 10.
        (490, 0, 0, 146, 0xF9), (490, 0, 0, 307, 0xF9), // gold6, film 9.

        // On-entry area descriptions. Every one is the field's own MPNAM, which runs
        // once from the director entity's init on each arrival.
        (484, 0, 0, 30, 0x43),  // Event Square.
        (486, 0, 0, 14, 0x43),  // Speed Square.
        (487, 0, 0, 14, 0x43),  // The Shooting Coaster platform.
        (488, 0, 0, 173, 0x43), // Round Square.
        (489, 0, 0, 0, 0x43),   // Inside the Ferris wheel, companion branch field.
        (490, 0, 0, 0, 0x43),   // Inside the Ferris wheel, solo branch field.
        (491, 0, 0, 42, 0x43),  // Ghost Square.
        (492, 0, 0, 19, 0x43),  // The Ghost Hotel lobby.
        (495, 0, 0, 14, 0x43),  // The Ghost Hotel shop.
        (505, 0, 0, 41, 0x43),  // Wonder Square.
        (506, 0, 0, 14, 0x43),  // The arcade's ground floor.
        (507, 0, 0, 14, 0x43),  // The arcade's upper floor.
        (509, 0, 0, 24, 0x43),  // Chocobo Square.
        (511, 0, 0, 175, 0x43)  // The chocobo racing ticket office.
    ];

    internal static void Run()
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is required.");
        var all = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        var native = new FieldScriptNavigationCatalog(gameRoot);
        var tracker = new FieldCutsceneDescriptionTracker(all);
        foreach (var anchor in Anchors)
        {
            var key = new FieldCutsceneDescriptionKey(anchor.Field, anchor.Entity, anchor.Script, anchor.Byte);
            var matches = all.Where(cue => cue.Key == key).ToArray();
            Equal(1, matches.Length, $"missing Gold Saucer scene description at {key}");
            var cue = matches[0];
            Equal(anchor.Opcode, cue.Opcode, $"description handler at {key}");
            Equal(anchor.Opcode, native.ReadScriptOpcodes(anchor.Field, anchor.Entity, anchor.Script)
                .Single(opcode => opcode.ByteIndex == anchor.Byte).Opcode, $"installed native instruction at {key}");
            var context = new FieldScriptContext(anchor.Field, anchor.Entity, anchor.Script, anchor.Byte, anchor.Opcode);
            Equal(null, tracker.Observe(context with { ByteIndex = anchor.Byte + 1 }), "nearby byte is silent");
            Equal(null, tracker.Observe(context with { Opcode = anchor.Opcode ^ 1 }), "wrong handler is silent");
            Equal(cue.Text, tracker.Observe(context)?.Text, "native scene delivers its description");

            // Repeating the identical callback is never a second action, whether the
            // cue is repeatable or not. A yielding request such as REQEW returns with
            // the caller's instruction pointer unchanged for every frame of the
            // animation it started, so the same opcode arrives again and again.
            Equal(null, tracker.Observe(context), "an unmoved script is one action, not two");
            Equal(null, tracker.Observe(context), "however many times it is delivered");
        }

        RepeatableMachineActionsSurviveASecondPlay(all);

        var goldSaucer = FieldCutsceneDescriptionCatalog.CreateGoldSaucerFirstVisitDescriptions();

        // A cue may name a companion only where the installed request actually
        // targets that companion's entity. This is the mechanical version of "do not
        // guess who is on screen": the byte is read from the installed script and its
        // entity parameter has to match the name in the text.
        var namedEntities = new (int Field, int Entity, int Script, int Byte, int Target, string Name)[]
        {
            (497, 0, 0, 644, 3, "Barret"),
            (497, 0, 0, 650, 5, "Red XIII"),
            (497, 0, 0, 656, 4, "Tifa"),
            (497, 0, 0, 665, 7, "Yuffie"),
            (497, 0, 0, 671, 2, "Aeris"),
            (497, 0, 0, 848, 3, "Barret")
        };
        foreach (var named in namedEntities)
        {
            var cue = goldSaucer.Single(c =>
                c.FieldId == named.Field && c.ByteIndex == named.Byte);
            Equal(true, cue.Text.Contains(named.Name, StringComparison.Ordinal),
                $"the cue at {named.Field}:{named.Byte} must describe {named.Name}");
            var bytes = native
                .ReadScriptOpcodes(named.Field, named.Entity, named.Script)
                .Single(opcode => opcode.ByteIndex == named.Byte)
                .Bytes.ToArray();
            Equal(named.Target, bytes[1],
                $"the installed request at {named.Field}:{named.Byte} must target {named.Name}'s entity");
        }

        // Nobody else may be named, and Yuffie only inside her availability test.
        foreach (var name in new[] { "Aeris", "Aerith", "Tifa", "Barret", "Red XIII", "Yuffie", "Cid", "Vincent" })
        {
            var named = goldSaucer.Where(c => c.Text.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var cue in named)
            {
                Equal(true, namedEntities.Any(entry =>
                        entry.Field == cue.FieldId && entry.Byte == cue.ByteIndex),
                    $"{name} may only be named where the installed request targets that entity");
            }
        }

        Equal(1, goldSaucer.Count(c => c.Text.Contains("Yuffie", StringComparison.Ordinal)),
            "Yuffie may be named only in the cue behind her native availability test");
        Equal(665, goldSaucer.Single(c => c.Text.Contains("Yuffie", StringComparison.Ordinal)).ByteIndex,
            "the Yuffie cue must sit inside the IFMEMBQ branch at byte 662");

        // Cid and Vincent have not joined at this point in the story.
        foreach (var absent in new[] { "Cid", "Vincent" })
        {
            Equal(false, goldSaucer.Any(c => c.Text.Contains(absent, StringComparison.Ordinal)),
                $"{absent} is not in the party during the first Gold Saucer visit");
        }
        var arcade = FieldCutsceneDescriptionCatalog.CreateGoldSaucerArcadeDescriptions();
        var films = FieldCutsceneDescriptionCatalog.CreateGoldSaucerGondolaFilmDescriptions();
        var areas = FieldCutsceneDescriptionCatalog.CreateGoldSaucerAreaDescriptions();
        Equal(Anchors.Length, goldSaucer.Count + arcade.Count + films.Count + areas.Count,
            "every Gold Saucer cue must be pinned by an anchor");

        AreaDescriptionsMatchTheirInstalledAreas(native, areas);

        // The narrator boxes already say these things, so a visual cue that repeats
        // them would double up on the native message path.
        foreach (var narratorPhrase in new[]
                 {
                     "take another shot", "female Mog", "Kupo nut", "learn how to fly",
                     "Mog Forest", "Goodnight"
                 })
        {
            Equal(false, arcade.Any(c => c.Text.Contains(narratorPhrase, StringComparison.OrdinalIgnoreCase)),
                $"no arcade cue may repeat the narrator's own \"{narratorPhrase}\"");
        }

        // Root's review rejected the draft's feeding counts, and the count is a
        // hidden gameplay value rather than something the screen shows.
        foreach (var forbidden in new[] { "nuts", "fed", "count", "times" })
        {
            Equal(false, arcade.Any(c => c.Text.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"no arcade cue may expose a feeding \"{forbidden}\"");
        }

        // The visitor stays a pink moogle until the narrator introduces her name at
        // event(6) Main byte 663, "Her name is Mag." After that byte the name is
        // hers on screen and may be used.
        const int MagIsNamedAtByte = 663;
        foreach (var named in arcade.Where(c => c.Text.Contains("Mag", StringComparison.Ordinal)))
        {
            Equal(true, named.EntityId == 6 && named.ByteIndex > MagIsNamedAtByte,
                $"the visiting moogle may only be named after byte {MagIsNamedAtByte}; " +
                $"cue at {named.EntityId}:{named.ByteIndex} names her too early");
        }

        Equal(true, arcade.Any(c => c.Text.Contains("pink moogle", StringComparison.Ordinal)),
            "and she is described as a pink moogle before that point");
    }

    /// <summary>
    /// The on-entry descriptions must cover every first-visit area root listed, be
    /// anchored to that area's own MPNAM, and describe the room rather than one
    /// scripted moment - they run again on every later arrival.
    /// </summary>
    private static void AreaDescriptionsMatchTheirInstalledAreas(
        FieldScriptNavigationCatalog native,
        IReadOnlyList<FieldCutsceneDescriptionCue> areas)
    {
        // Ghost Square and hotel, Speed Square and its platform, Chocobo Square and
        // the ticket office, Event Square, the Wonder Square arcades, and Round
        // Square with the gondola cabin.
        var required = new[] { 484, 486, 487, 488, 489, 490, 491, 492, 495, 505, 506, 507, 509, 511 };
        foreach (var field in required)
        {
            // The identity-manifest entry each of these needs is asserted for the
            // whole catalog by EchoSCompatibilityTests, which is x86-only because
            // the manifest is.
            Equal(1, areas.Count(cue => cue.FieldId == field),
                $"field {field} must carry exactly one on-entry description");
        }

        Equal(required.Length, areas.Count, "no extra area is described");

        foreach (var cue in areas)
        {
            // The anchor has to be the field's only MPNAM. A second one would mean
            // the area could be described twice from a single arrival.
            var mapNames = native
                .ReadScriptOpcodes(cue.FieldId, cue.EntityId, cue.ScriptId)
                .Where(opcode => opcode.Opcode == FieldOpcodeAddressResolver.OpcodeMapNameIndex)
                .ToArray();
            Equal(1, mapNames.Length,
                $"field {cue.FieldId} must set its area name exactly once per arrival");
            Equal(cue.ByteIndex, mapNames[0].ByteIndex,
                $"field {cue.FieldId}'s description must be anchored to that one area-name opcode");
        }

        // These arrive again on every later visit, so nothing here may describe a
        // one-off scripted moment or name someone who is only present for it.
        foreach (var name in new[]
                 {
                     "Cloud", "Aeris", "Aerith", "Tifa", "Barret", "Red XIII", "Cid",
                     "Vincent", "Yuffie", "Cait Sith", "Dio", "Shinra", "Tseng", "soldier"
                 })
        {
            Equal(false, areas.Any(cue => cue.Text.Contains(name, StringComparison.OrdinalIgnoreCase)),
                $"an area description must not name {name}; it is spoken on every arrival");
        }

        // The gondola cabin's film starts about ten seconds after the field loads, so
        // its description has to be short enough to finish first.
        foreach (var cabin in areas.Where(cue => cue.FieldId is 489 or 490))
        {
            Equal(true, cabin.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 12,
                "the gondola cabin description must finish before the ride's first film");
        }
    }

    /// <summary>
    /// Root's finding: story-style once-per-visit deduping silences every play of a
    /// machine after the first. These actions are released again by the native
    /// re-entry into their own opening anchor, and by nothing else.
    /// </summary>
    private static void RepeatableMachineActionsSurviveASecondPlay(
        IReadOnlyList<FieldCutsceneDescriptionCue> all)
    {
        var tracker = new FieldCutsceneDescriptionTracker(all);
        var left = all.Where(cue => cue.RecurringGroup == "wonder-catcher-left")
            .OrderBy(cue => cue.ByteIndex)
            .ToArray();
        Equal(4, left.Length, "the left-hand Wonder Catcher side has its four actions");
        Equal(1, left.Count(cue => cue.StartsRecurringGroup), "exactly one of them opens the group");

        FieldCutsceneDescriptionCue? Observe(FieldCutsceneDescriptionCue cue) =>
            tracker.Observe(new FieldScriptContext(
                cue.FieldId, cue.EntityId, cue.ScriptId, cue.ByteIndex, cue.Opcode));

        // First play: every action is described once.
        foreach (var cue in left)
        {
            Equal(cue.Text, Observe(cue)?.Text, $"first play describes byte {cue.ByteIndex}");
        }

        // A non-opening action repeating on its own must not reopen the group.
        var middle = left.Single(cue => !cue.StartsRecurringGroup && cue.ByteIndex == 127);
        Equal(null, Observe(middle), "a mid-sequence action does not reopen the group by itself");

        // Second play on the same side, without leaving the room.
        foreach (var cue in left)
        {
            Equal(cue.Text, Observe(cue)?.Text, $"second play describes byte {cue.ByteIndex} again");
        }

        // The other control side is its own group and is unaffected by the first.
        var right = all.Where(cue => cue.RecurringGroup == "wonder-catcher-right")
            .OrderBy(cue => cue.ByteIndex)
            .ToArray();
        Equal(4, right.Length, "the right-hand side has its four actions");
        foreach (var cue in right)
        {
            Equal(cue.Text, Observe(cue)?.Text, $"the other side describes byte {cue.ByteIndex}");
        }

        // Feeding Mog is repeatable, but its native request yields: REQEW returns
        // with the caller's instruction pointer unchanged for every frame of the eat
        // animation, so one accepted feed arrives as a long run of identical
        // callbacks. That run is one feed.
        var feeding = all.Where(cue => cue.RecurringGroup == "mog-feeding").ToArray();
        Equal(1, feeding.Length, "the accepted feed is one action");
        Equal("Mog eats the nut.", Observe(feeding[0])?.Text, "the first accepted feed is described");
        for (var frame = 0; frame < 30; frame++)
        {
            Equal(null, Observe(feeding[0]),
                "the yielded repeats of one feeding animation are not more food");
        }

        // The script comes round its own loop and offers the nut again. esa(7) Main
        // waits at bytes 52 and 73 between feeds, so the script is observed somewhere
        // else before byte 82 runs a second time - which is the game itself saying
        // the previous action finished.
        Observe(new FieldCutsceneDescriptionCue(
            feeding[0].FieldId, feeding[0].EntityId, feeding[0].ScriptId, 52,
            string.Empty, FieldOpcodeAddressResolver.OpcodeWaitIndex));
        Equal("Mog eats the nut.", Observe(feeding[0])?.Text, "the next genuine feed is described");
        Equal(false, feeding[0].Text.Any(char.IsDigit), "without ever exposing how many he has had");

        // Another entity running in between is not evidence about this one.
        Observe(new FieldCutsceneDescriptionCue(
            feeding[0].FieldId, 6, 0, 43, string.Empty, FieldOpcodeAddressResolver.OpcodeRequestEwIndex));
        Equal(null, Observe(feeding[0]),
            "another entity's opcode does not turn a yielded repeat into a new feed");

        // Story actions keep the once-per-visit rule.
        var story = all.Single(cue => cue.FieldId == 497 && cue.ByteIndex == 644);
        Equal(story.Text, Observe(story)?.Text, "a story action is described once");
        Equal(null, Observe(story), "and not again during the same visit");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Gold Saucer descriptions - {label}: expected {expected}, got {actual}.");
    }
}
