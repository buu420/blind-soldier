using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.LegacyLayout;
using System.Buffers.Binary;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class AncientForestPuzzleTests
{
    private static readonly DateTime Epoch = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        var failures = new List<string>();
        foreach (var test in new Action[] { CreaturesHaveDistinctNames, PlatformThrowingSpotsAreAvailable, PickupAndReleaseAreSpoken,
                     PlantsAreDistinguished, UnknownReadsAreNotEmptyHands, RepeatDoesNotConsumeChanges,
                     TimedStateReplacesOldSpeech, LeavingAndResetDiscardState, FlytrapChangesAreSpoken, NativeSnapshotRejectsTearsAndUnknownScripts,
                     LosingFocusRetiresPendingState,
                     ShakingIsReadFromThePlantsOwnScriptOnTheFirstPass, ShakingReadRejectsTearsAndForeignScripts,
                     EchoLayoutsUseTheirOwnEntities, NativeLayoutsMapEveryPlantAndCreature,
                     ShakingIsSpokenUntilThePlantOpens, ShakingIsWhatThePlantShowsEvenAfterItsFlagClears, EveryCarriedCodeNamesItsObjectsTarget,
                     ReleaseOutcomesComeFromVisibleModels, FrogsAppearingAreSpoken, FlytrapBiteIsSpokenAfterControlReturns,
                     PlantOpeningUnderTheLeaderIsSaid,
                     ScriptedMovementOnlyReportsChanges, RepeatOffersOnlyRealControls, ResetForgetsLatchesAndPendingOutcomes,
                     ClosingIsHeardWhenTheHandsEmptyAFrameLater, FlytrapShutIsHeardWhenTheHandsEmptyAFrameLater,
                     AdjacentPlantChangesAreBothHeard, EachSubjectIsSaidAtItsNewestState, HeardLinesAreNotRepeated,
                     HeldLinesKeepEveryUnheardSubject, ExpiredWarningsAndOldPlacesAreNotRetained, UrgentWarningLeadsAMergedLine,
                     UnsaidLineIsOfferedAgainWhenControlReturns, OldEventsAreNotNewsAfterALongMute, OldReleaseIsNotNewsAfterALongMute,
                     UnreadCreatureDoesNotKeepACurrentPlaceClaim, LinesCutOffByOtherSpeechAreSaidAgain,
                     SlowSpeakerKeepsTheLineUntilItHasSaidIt, RoutineDirectionsAllowForestSpeechToFinish })
        {
            try { test(); }
            catch (Exception ex) { failures.Add(test.Method.Name + ": " + ex.Message); }
        }
        foreach (var failure in failures) Console.WriteLine("FAIL Ancient Forest " + failure);
        if (failures.Count != 0) throw new InvalidOperationException($"{failures.Count} Ancient Forest regressions failed.");
        Console.WriteLine("Ancient Forest puzzle feedback tests passed.");
    }

    private static FieldActivityObservation Look(int field = 622, int held = 0,
        int first = 0, int second = 0, int third = 0, bool controlled = true, bool readable = true,
        int fourth = 0, IReadOnlySet<int>? shaking = null, IReadOnlyList<FieldActivityModelReading>? models = null,
        int x = 0, int y = 0, int z = int.MinValue, int zone = 0, int triangle = 3, int trap = 0)
    {
        var bank = new byte[128];
        bank[9] = (byte)held;
        var start = field switch { 620 => 17, 623 => 18, _ => 16 };
        bank[start] = (byte)first; bank[start + 1] = (byte)second; bank[start + 2] = (byte)third;
        if (field == 623) bank[start + 3] = (byte)fourth;
        // The first Mutant Flytrap's shut flag: 620 5[51], 623 5[61].
        if (field != 622) bank[field == 620 ? 51 : 61] = (byte)trap;
        bank[field switch { 620 => 20, 623 => 22, _ => 19 }] = (byte)zone;
        // The jaws' own heights unless a test says otherwise: 622 13, 620 -12, 623 37.
        if (z == int.MinValue) z = field switch { 620 => -12, 623 => 37, _ => 13 };
        return new(field, x, y, z, triangle, controlled, 1620, new(0), true, models ?? [],
            new Dictionary<int, FieldActivityWaitState>(), null, null, -1)
        { ReadTemporaryByte = readable ? i => bank[i] : _ => -1, AncientForestShakingPlants = shaking };
    }

    private static IReadOnlySet<int> Set(params int[] plants) => new HashSet<int>(plants);
    private static FieldActivityModelReading Seen(int entity, int x, int y) =>
        new(entity, FieldActivityReadStatus.Visible, new FieldActivityModelState(true, true, x, y, 0, 0, 0));
    private static FieldActivityModelReading Hidden(int entity) =>
        new(entity, FieldActivityReadStatus.Hidden, new FieldActivityModelState(true, false, 0, 0, 0, 0, 0));
    private static FieldActivityModelReading Unread(int entity) => FieldActivityModelReading.Unreadable(entity);

    private static void CreaturesHaveDistinctNames()
    {
        var objects = TownInteractionObjectCatalog.Create();
        foreach (var (field, ids) in new[] { (620, new[] { 7, 8, 9, 10 }),
                     (622, new[] { 6, 7, 8, 9, 10, 11 }), (623, new[] { 6, 7, 8, 9, 10 }) })
        {
            var names = ids.Select(id => objects.Single(o => o.FieldId == field && o.EntityId == id).Label).ToArray();
            Check(names.Distinct().Count() == names.Length, $"field {field} has duplicate creature names");
        }
    }

    private static void PlatformThrowingSpotsAreAvailable()
    {
        var objects = TownInteractionObjectCatalog.Create();
        foreach (var (field, triangle, plant) in new[] { (622, 14, 2), (622, 29, 3) })
        {
            var spots = objects.Where(o => o.FieldId == field && o.RequiredPlayerTriangles?.Contains(triangle) == true &&
                o.Label?.Contains("throwing spot", StringComparison.Ordinal) == true).ToArray();
            Check(spots.Length > 0, $"field {field} platform t{triangle} has no throwing target");
            Check(spots.Any(o => o.Label!.Contains($"plant {plant}", StringComparison.OrdinalIgnoreCase)), "platform target names the receiving plant");
        }
    }

    private static void PickupAndReleaseAreSpoken()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(), Epoch);
        var pickedUp = readout.Observe(Look(held: 2), Epoch.AddMilliseconds(100));
        Contains(pickedUp.Speech, "Carrying insect 2", "pickup is audible immediately");
        Check(!pickedUp.IsPending && !pickedUp.PlayButtonReadyCue, "carrying never takes input ownership");
        Contains(readout.Describe(Look(held: 5)), "Carrying frog 2", "repeat names the actual carried frog");
        Contains(readout.Observe(Look(), Epoch.AddMilliseconds(200)).Speech, "Carrying nothing", "release is audible");
    }

    private static void PlantsAreDistinguished()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(), Epoch);
        var closed = readout.Observe(Look(first: 1), Epoch.AddMilliseconds(50));
        Contains(closed.Speech, "Pitcher plant 1", "which plant changed");
        Contains(closed.Speech, "closed", "its visible state");
        Contains(readout.Observe(Look(), Epoch.AddMilliseconds(100)).Speech, "open", "reopening is audible");
        Contains(readout.Describe(Look(field: 620, held: 5)), "beehive", "620 carried hive identity");
        Contains(readout.Describe(Look(field: 623, held: 6)), "beehive", "623 carried hive identity");
    }

    private static void UnknownReadsAreNotEmptyHands()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(held: 4), Epoch);
        var unknown = readout.Describe(Look(readable: false));
        Check(unknown is null || !unknown.Contains("Carrying nothing", StringComparison.OrdinalIgnoreCase), "read failure became empty hands");
        Check(readout.Observe(Look(readable: false), Epoch.AddMilliseconds(20)).Speech is null, "unreadable state must retire stale speech");
        Contains(readout.Observe(Look(held: 4), Epoch.AddMilliseconds(40)).Speech, "frog 1", "coherent recovery is reannounced");
        Check(readout.Describe(Look(held: 255)) is null, "invalid held ID must not become a guessed creature");
    }

    private static void RepeatDoesNotConsumeChanges()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(), Epoch);
        Contains(readout.Describe(Look(held: 1)), "insect 1", "repeat reads current hands");
        Contains(readout.Observe(Look(held: 1), Epoch.AddMilliseconds(20)).Speech, "insect 1", "repeat does not consume pickup");
        Check(readout.Observe(Look(held: 1), Epoch.AddMilliseconds(30)).Speech is null, "unchanged polling should be silent");
    }

    private static void TimedStateReplacesOldSpeech()
    {
        var readout = new FieldActivityReadout();
        var delivery = new FieldActivityLatestLineDelivery();
        var spoken = new List<string>();
        var first = readout.Observe(Look(first: 1), Epoch);
        Check(first.ReplacesEarlierLine, "timed plant speech must use newest-only delivery");
        FieldActivityClockHost.Deliver(first, readout, delivery, Epoch, () => true, () => false,
            s => { spoken.Add(s); return true; }, () => false);
        var reopened = readout.Observe(Look(), Epoch.AddSeconds(1));
        FieldActivityClockHost.Deliver(reopened, readout, delivery, Epoch.AddSeconds(1), () => true, () => true,
            s => { spoken.Add(s); return true; }, () => false);
        Check(spoken.Count == 1 && !spoken[0].Contains("closed", StringComparison.OrdinalIgnoreCase), "expired closed plant was delivered");
        Contains(spoken.Single(), "open", "newest plant state is delivered after unmuting");
    }

    private static void LeavingAndResetDiscardState()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(held: 1), Epoch);
        readout.Observe(Look(field: 116), Epoch.AddMilliseconds(10));
        Check(readout.CurrentReplaceableLine is null, "leaving retained a forest line");
        Contains(readout.Observe(Look(), Epoch.AddMilliseconds(20)).Speech, "Carrying nothing", "return is a fresh observation");
        readout.Reset();
        Check(readout.CurrentReplaceableLine is null, "reset retained a line");
        Check(readout.Describe(Look(controlled: false)) is null, "native scripted movement should not invite a puzzle action");
    }

    private static void LosingFocusRetiresPendingState()
    {
        var readout = new FieldActivityReadout();
        var delivery = new FieldActivityLatestLineDelivery();
        readout.Observe(Look(held: 1), Epoch);
        Check(!FieldActivityClockHost.MayObserve(622, false, readout, delivery), "forest must stop observing without focus");
        Check(readout.CurrentReplaceableLine is null, "focus loss retained puzzle speech");
    }

    private static void FlytrapChangesAreSpoken()
    {
        var readout = new FieldActivityReadout();
        var open = Look(field: 620);
        readout.Observe(open, Epoch);
        var closed = open with { ReadTemporaryByte = index => index == 51 ? 1 : open.ReadTemporaryByte!(index) };
        Contains(readout.Observe(closed, Epoch.AddMilliseconds(10)).Speech, "Flytrap 1 closed", "the visible trap closure is audible");
        Contains(readout.Describe(open), "Flytrap 1 open", "reset correctly restores the visible open trap");
    }

    private static void NativeSnapshotRejectsTearsAndUnknownScripts()
    {
        var memory = new PuzzleMemory();
        var script = new LoadedFieldScriptIdentity(622, PuzzleMemory.Pointer,
            EchoSCompatibilityManifest.GetVanillaFingerprint(622)!);
        var reader = new AncientForestPuzzleStateReader(memory, () => script);
        memory.Bank[9] = 4;
        Check(reader.Read(622)?[9] == 4, "reviewed native state is readable");
        memory.AfterBankRead = () => memory.Bank[9] = 5;
        Check(reader.Read(622) is null, "pickup changing during capture must not produce a mixed state");
        memory.AfterBankRead = () => memory.Field = 620;
        Check(reader.Read(622) is null, "room changed during capture");
        memory.Field = 622;
        memory.AfterBankRead = null;
        memory.FailBank = true;
        Check(reader.Read(622) is null, "failed bank read must not become zeros");
        memory.FailBank = false;
        script = new LoadedFieldScriptIdentity(622, PuzzleMemory.Pointer, new string('0', 64));
        Check(reader.Read(622) is null, "unreviewed script must not inherit native bank meanings");
    }

    // ---------------------------------------------------------------------------------------------
    // Shaking: a closed plant's own background entity (utubo_N) running its native script 3. The
    // shake counter (5[47..49], 5[61..63], 5[67..70]) still reads 0 during the first pass.

    private static LoadedFieldScriptIdentity Vanilla(int field) =>
        new(field, ForestMemory.Pointer, EchoSCompatibilityManifest.GetVanillaFingerprint(field)!);

    private static LoadedFieldScriptIdentity Echo(int field) =>
        new(field, ForestMemory.Pointer, EchoSCompatibilityManifest.GetEchoFingerprint(field)!);

    private static void ShakingIsReadFromThePlantsOwnScriptOnTheFirstPass()
    {
        var memory = new ForestMemory(622);
        memory.Bank[16] = 1;
        memory.Bank[47] = 0;
        memory.Shake(28, 0x2F);
        var reader = new AncientForestPuzzleStateReader(memory, () => Vanilla(622));
        var state = reader.ReadState(622);
        Check(state is not null, "a coherent reviewed forest yields a snapshot");
        Check(state!.ShakingPlants is { Count: 1 } shaking && shaking.Contains(1),
            $"plant 1 is shaking on its first pass, counter still 0: got {Show(state.ShakingPlants)}");
        Check(state.Bank[16] == 1 && reader.Read(622)?[16] == 1, "the snapshot carries the same bank Read() returns");

        memory.Idle(28);
        Check(reader.ReadState(622)?.ShakingPlants is { Count: 0 }, "closed and still is not shaking");

        memory.Shake(30, 0x31);
        Check(reader.ReadState(622)?.ShakingPlants is { Count: 1 } third && third.Contains(3), "plant 3 is its own entity");
    }

    private static void ShakingReadRejectsTearsAndForeignScripts()
    {
        var memory = new ForestMemory(622);
        memory.Bank[16] = 1;
        var reader = new AncientForestPuzzleStateReader(memory, () => Vanilla(622));

        memory.Shake(28, 0x30);
        Check(reader.ReadState(622) is { ShakingPlants: null }, "plant 1 running another plant's shake is unknown, not shaking");

        memory.Shake(28, 0x2F, programCounter: 59);
        Check(reader.ReadState(622) is { ShakingPlants: null }, "a counter outside script 3 is unknown");

        memory.Shake(28, 0x2F);
        memory.OnceAfterRead(FieldScriptControllerReader.AddressEntityScriptPriorities + 28, () => memory.Idle(28));
        Check(reader.ReadState(622) is { ShakingPlants: null }, "a cursor that changes between samples is unknown");

        var shortTable = new ForestMemory(622, entityCount: 20);
        shortTable.Bank[16] = 1;
        Check(new AncientForestPuzzleStateReader(shortTable, () => Vanilla(622)).ReadState(622) is { ShakingPlants: null },
            "a loaded section without the plant entities is unknown");
    }

    private static void EchoLayoutsUseTheirOwnEntities()
    {
        var memory = new ForestMemory(620);
        memory.Bank[17] = 1;
        memory.Bank[18] = 1;
        memory.Shake(36, 0x3D);
        var echo = new AncientForestPuzzleStateReader(memory, () => Echo(620));
        Check(echo.ReadState(620) is { ShakingPlants: null }, "Echo-S 620 plant 1 is not the stock shake");
        memory.Shake(36, 0x3D, skip: 0x3B, length: 69);
        Check(echo.ReadState(620)?.ShakingPlants is { Count: 1 } first && first.Contains(1), "Echo-S 620 plant 1 is entity 36, with its own shake");
        memory.Shake(36, 0x3D, programCounter: 64, skip: 0x3B, length: 69);
        Check(echo.ReadState(620)?.ShakingPlants is { Count: 1 } late && late.Contains(1), "inside Echo-S's longer shake");
        memory.Idle(36);
        memory.Shake(37, 0x3E);
        Check(echo.ReadState(620)?.ShakingPlants is { Count: 1 } second && second.Contains(2), "Echo-S 620 plant 2 is entity 37");
        var stock = new AncientForestPuzzleStateReader(memory, () => Vanilla(620));
        Check(stock.ReadState(620) is { ShakingPlants: null }, "the stock map must not read Echo-S plant 2 as stock plant 1");

        memory.Model(6, 0, visible: true, x: 100, y: 200);
        memory.Model(15, 1, visible: false, x: 0, y: 0);
        var models = echo.ReadState(620)!.Models;
        Check(models.Select(m => m.EntityId).SequenceEqual([7, 8, 9, 10, 16]), $"models carry stock ids: {string.Join(",", models.Select(m => m.EntityId))}");
        Check(models[0] is { Status: FieldActivityReadStatus.Visible, Model.X: 100, Model.Y: 200 }, "Echo-S entity 6 is insect 1 (stock 7)");
        Check(models[4].Status == FieldActivityReadStatus.Hidden, "Echo-S entity 15 is the beehive (stock 16)");

        var forest4 = new ForestMemory(623);
        forest4.Bank[18] = 1;
        forest4.Shake(42, 0x43);
        Check(new AncientForestPuzzleStateReader(forest4, () => Echo(623)).ReadState(623)?.ShakingPlants is { Count: 1 } echo1 && echo1.Contains(1),
            "Echo-S 623 plant 1 is entity 42, after FrogFix");
        forest4.Idle(42);
        forest4.Shake(41, 0x43);
        Check(new AncientForestPuzzleStateReader(forest4, () => Vanilla(623)).ReadState(623)?.ShakingPlants is { Count: 1 } stock1 && stock1.Contains(1),
            "stock 623 plant 1 is entity 41");
    }

    /// <summary>
    /// The maps against the real installed and Echo-S bytes: every plant's own entity is named
    /// utubo_N and carries its native shake script, and every creature keeps its stock name.
    /// </summary>
    private static void NativeLayoutsMapEveryPlantAndCreature()
    {
        var roots = new List<(string Root, bool Echo)>();
        if (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") is { Length: > 0 } stock) roots.Add((stock, false));
        foreach (var echo in (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            roots.Add((echo, true));
        }
        if (roots.Count == 0)
        {
            Console.WriteLine("Ancient Forest native layout check skipped: no FF7_ACCESSIBILITY_DATA_ROOT or Echo-S roots.");
            return;
        }

        var stockNames = new Dictionary<int, string[]>();
        foreach (var (root, isEcho) in roots.OrderBy(r => r.Echo))
        {
            foreach (var field in new[] { 620, 622, 623 })
            {
                var memory = ForestMemory.Native(root, field);
                var variant = isEcho ? SupportedFieldScriptVariant.EchoS124 : SupportedFieldScriptVariant.Vanilla;
                var layout = AncientForestPuzzleStateReader.Layout(field, variant);
                Check(layout is not null, $"{root} {field}: no reviewed layout");
                var (plants, creatures) = layout!.Value;
                var names = memory.EntityNames();
                if (!isEcho) stockNames[field] = names;
                for (var plant = 0; plant < plants.Count; plant++)
                {
                    Check(names[plants[plant]] == $"utubo_{plant + 1}", $"{root} {field}: plant {plant + 1} entity {plants[plant]} is {names[plants[plant]]}");
                    foreach (var entity in plants) memory.Idle(entity);
                    memory.RunNative(plants[plant], 9);
                    var reader = new AncientForestPuzzleStateReader(memory, () => isEcho ? Echo(field) : Vanilla(field));
                    var state = reader.ReadState(field);
                    Check(state?.ShakingPlants is { Count: 1 } one && one.Contains(plant + 1),
                        $"{root} {field}: plant {plant + 1} shake read {Show(state?.ShakingPlants)}");
                }
                if (stockNames.TryGetValue(field, out var expected))
                {
                    foreach (var (canonical, loaded) in creatures)
                        Check(names[loaded] == expected[canonical], $"{root} {field}: creature {canonical} is {names[loaded]}, stock {expected[canonical]}");
                }
            }
        }
        Console.WriteLine($"Ancient Forest native plant and creature maps passed on {roots.Count} layouts.");
    }

    // ---------------------------------------------------------------------------------------------
    // Readout.

    private static void ShakingIsSpokenUntilThePlantOpens()
    {
        var (readout, now) = Settled(Look(first: 1));
        var shaking = readout.Observe(Look(first: 1, shaking: Set(1)), now.AddMilliseconds(100));
        Contains(shaking.Speech, "Pitcher plant 1 shaking", "the visible shake is spoken before the plant opens");
        Check(shaking.Speech?.Contains("Carrying", StringComparison.Ordinal) == false, "only what changed is spoken");
        Check(readout.Observe(Look(first: 1, shaking: Set()), now.AddMilliseconds(200)).Speech is null,
            "the gap between the two native shake passes does not flap");
        Check(readout.Observe(Look(first: 1, shaking: null), now.AddMilliseconds(300)).Speech is null, "an unknown shake is not news");
        Contains(readout.Describe(Look(first: 1, shaking: Set(1))), "Pitcher plant 1 shaking", "repeat says the plant is shaking");
        // The reader reports a known empty set once nothing shakes; null means it could not tell.
        var open = readout.Observe(Look(shaking: Set()), now.AddMilliseconds(400));
        Contains(open.Speech, "Pitcher plant 1 open", "reopening");
        Check(open.Speech?.Contains("shaking", StringComparison.Ordinal) == false, "an open plant is not shaking");
        Contains(readout.Observe(Look(first: 1, shaking: Set()), now.AddMilliseconds(500)).Speech, "Pitcher plant 1 closed",
            "a new closing starts still, not shaking");

        var fourth = new FieldActivityReadout();
        fourth.Observe(Look(field: 623, fourth: 1), Epoch);
        Contains(fourth.Observe(Look(field: 623, fourth: 1, shaking: Set(4)), Epoch.AddMilliseconds(100)).Speech,
            "Pitcher plant 4 shaking", "623's fourth plant");
    }

    /// <summary>
    /// Echo-S 620's first director sets 5[255] for the second shake pass, and that plant's shake
    /// clears its own flag 5[17] while it is still drawing the shake. What is on screen is the shake,
    /// so the plant is not open until its entity stops shaking.
    /// </summary>
    private static void ShakingIsWhatThePlantShowsEvenAfterItsFlagClears()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(field: 620, first: 1), Epoch);
        Contains(readout.Observe(Look(field: 620, first: 1, shaking: Set(1)), Epoch.AddMilliseconds(100)).Speech,
            "Pitcher plant 1 shaking", "the first pass");
        Check(readout.Observe(Look(field: 620, shaking: Set(1)), Epoch.AddMilliseconds(200)).Speech is null,
            "the second pass clears the flag but is still the shake, not an open plant");
        Contains(readout.Observe(Look(field: 620, shaking: null), Epoch.AddMilliseconds(300)).Speech,
            "Pitcher plant 1 state unavailable", "a cleared flag and unknown motion cannot prove open or still shaking");
        Contains(readout.Describe(Look(field: 620, shaking: Set(1))), "Pitcher plant 1 shaking", "repeat shows the shake too");
        Contains(readout.Observe(Look(field: 620, shaking: Set()), Epoch.AddMilliseconds(400)).Speech, "Pitcher plant 1 open",
            "it is open once the shake ends");
    }

    private static void EveryCarriedCodeNamesItsObjectsTarget()
    {
        var objects = TownInteractionObjectCatalog.Create();
        foreach (var (field, entities) in new[] { (620, new[] { 7, 8, 9, 10, 16 }),
                     (622, new[] { 6, 7, 8, 9, 10, 11 }), (623, new[] { 6, 7, 8, 9, 10, 16 }) })
        {
            for (var code = 1; code <= entities.Length; code++)
            {
                var label = objects.Single(o => o.FieldId == field && o.EntityId == entities[code - 1] && o.UsesTalkInteraction).Label!;
                Contains(new FieldActivityReadout().Describe(Look(field: field, held: code)), "Carrying " + label,
                    $"field {field} carried code {code} names its Objects target");
            }
            Check(new FieldActivityReadout().Describe(Look(field: field, held: entities.Length + 1)) is null,
                $"field {field} code {entities.Length + 1} is not a creature");
        }
    }

    private static void ReleaseOutcomesComeFromVisibleModels()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(held: 2, models: [Hidden(7)], x: 100, y: 100), Epoch);
        var dropped = readout.Observe(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), Epoch.AddMilliseconds(100));
        Contains(dropped.Speech, "Insect 2 is at your feet", "a release on open ground is seen at the leader's feet");
        Contains(dropped.Speech, "Carrying nothing", "and the hands are empty");

        readout = new FieldActivityReadout();
        readout.Observe(Look(held: 2, models: [Hidden(7)], x: 0, y: -126), Epoch);
        Contains(readout.Observe(Look(models: [Seen(7, 171, -254)], x: 0, y: -126), Epoch.AddMilliseconds(100)).Speech,
            "Insect 2 landed", "a throw at a shut plant is seen landing away from the leader");

        readout = new FieldActivityReadout();
        readout.Observe(Look(held: 2, models: [Hidden(7)]), Epoch);
        var fed = readout.Observe(Look(first: 1, models: [Hidden(7)]), Epoch.AddMilliseconds(100));
        Contains(fed.Speech, "Pitcher plant 1 closed", "the plant's own visible closing");
        foreach (var word in new[] { "landed", "feet", "ate", "eaten", "inside" })
            Check(fed.Speech?.Contains(word, StringComparison.OrdinalIgnoreCase) == false, $"a hidden creature is not described ({word})");

        readout = new FieldActivityReadout();
        readout.Observe(Look(held: 2, models: [Hidden(7)]), Epoch);
        var unread = readout.Observe(Look(models: [Unread(7)]), Epoch.AddMilliseconds(100));
        Contains(unread.Speech, "Carrying nothing", "release is said at once");
        Check(unread.Speech?.Contains("landed", StringComparison.Ordinal) == false, "no outcome from an unreadable model");
        Contains(readout.Observe(Look(models: [Seen(7, 400, -300)]), Epoch.AddMilliseconds(200)).Speech, "Insect 2 landed",
            "the outcome follows once the model is readable");
    }

    private static void FrogsAppearingAreSpoken()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(field: 620, models: [Hidden(9)]), Epoch);
        Contains(readout.Observe(Look(field: 620, models: [Seen(9, -669, 22)]), Epoch.AddMilliseconds(100)).Speech,
            "Frog 1 appeared", "620's hidden frog coming out is spoken");

        var fresh = new FieldActivityReadout().Observe(Look(field: 620, models: [Seen(9, -669, 22)]), Epoch);
        Check(fresh.Speech?.Contains("appeared", StringComparison.Ordinal) == false, "a frog already out is not news");

        readout = new FieldActivityReadout();
        readout.Observe(Look(field: 620, models: [Unread(9)]), Epoch);
        Check(readout.Observe(Look(field: 620, models: [Seen(9, -669, 22)]), Epoch.AddMilliseconds(100)).Speech?
            .Contains("appeared", StringComparison.Ordinal) != true, "nothing is claimed after an unknown look");

        readout = new FieldActivityReadout();
        readout.Observe(Look(field: 620, held: 3, models: [Hidden(9)]), Epoch);
        var released = readout.Observe(Look(field: 620, models: [Seen(9, 4, 4)]), Epoch.AddMilliseconds(100));
        Check(released.Speech?.Contains("appeared", StringComparison.Ordinal) == false, "a frog the player releases did not appear by itself");
        Contains(released.Speech, "Frog 1 is at your feet", "it was released at the leader's feet");

        readout = new FieldActivityReadout();
        readout.Observe(Look(first: 1, models: [Hidden(10)]), Epoch);
        var spat = readout.Observe(Look(models: [Seen(10, -8, -377)]), Epoch.AddMilliseconds(100));
        Contains(spat.Speech, "Pitcher plant 1 open", "the plant opens");
        Contains(spat.Speech, "Frog 2 appeared", "the frog it held is seen again");
    }

    private static void FlytrapBiteIsSpokenAfterControlReturns()
    {
        // Through the hosts' delivery, so that what is said counts as said.
        var forest = new Listener();
        forest.At(Look(x: 300, y: -900), Epoch);
        forest.At(Look(controlled: false, x: 441, y: -914), Epoch.AddMilliseconds(100));
        Check(forest.Said.Count == 1 && forest.Readout.CurrentReplaceableLine is null, "nothing is said while the field holds the party");
        forest.At(Look(controlled: false, x: 179, y: -962), Epoch.AddMilliseconds(200));
        forest.At(Look(x: 179, y: -962), Epoch.AddMilliseconds(300));
        Check(forest.Said.Count == 2, "the bite is said once control returns");
        Contains(forest.Last, "Mutant Flytrap bit the party", "the bite");
        forest.At(Look(x: 179, y: -962), Epoch.AddMilliseconds(400));
        Check(forest.Said.Count == 2, "said once");
        forest.At(Look(controlled: false, x: 255, y: -254), Epoch.AddMilliseconds(500));
        forest.At(Look(x: 255, y: -254), Epoch.AddMilliseconds(600));
        Check(forest.Said.Count == 2, $"a plant jump is not a bite: {forest.Last}");
        forest.At(Look(controlled: false, x: 441, y: -914, zone: 1), Epoch.AddMilliseconds(700));
        forest.At(Look(x: 441, y: -914, zone: 1), Epoch.AddMilliseconds(800));
        Check(forest.Said.Count == 2,
            "held still in the open strip while the zone byte is set is not a bite; every bite clears the zone first");
        forest.At(Look(controlled: false, x: 441, y: -914, z: 120), Epoch.AddMilliseconds(900));
        forest.At(Look(x: 441, y: -914, z: 120), Epoch.AddMilliseconds(1000));
        Check(forest.Said.Count == 2, "another floor above the jaw is not its mouth");

        var readout = new FieldActivityReadout();
        readout.Observe(Look(field: 620), Epoch);
        readout.Observe(Look(field: 620, controlled: false, x: 1469, y: -187), Epoch.AddMilliseconds(100));
        Contains(readout.Observe(Look(field: 620, x: 1137, y: -154), Epoch.AddMilliseconds(200)).Speech, "Mutant Flytrap 2 bit the party",
            "620's second flytrap");

        readout = new FieldActivityReadout();
        readout.Observe(Look(field: 623), Epoch);
        readout.Observe(Look(field: 623, controlled: false, x: 592, y: -285), Epoch.AddMilliseconds(100));
        Contains(readout.Observe(Look(field: 623, x: 605, y: -726), Epoch.AddMilliseconds(200)).Speech, "Mutant Flytrap bit the party",
            "623's flytrap");
    }

    /// <summary>
    /// A plant that opens while the leader stands on its top moves him off it, by a jump down
    /// or a launch. The plant tops are the triangles ujp0 polls.
    /// </summary>
    private static void PlantOpeningUnderTheLeaderIsSaid()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(first: 1, triangle: 14), Epoch);
        readout.Observe(Look(first: 1, controlled: false, triangle: 14), Epoch.AddMilliseconds(100));
        var thrown = readout.Observe(Look(shaking: Set(), triangle: 116), Epoch.AddMilliseconds(200));
        Contains(thrown.Speech, "Pitcher plant 1 opened under you", "the plant opened beneath the party");

        readout = new FieldActivityReadout();
        readout.Observe(Look(first: 1, triangle: 40), Epoch);
        readout.Observe(Look(first: 1, controlled: false, triangle: 40), Epoch.AddMilliseconds(100));
        var nearby = readout.Observe(Look(shaking: Set(), triangle: 40), Epoch.AddMilliseconds(200));
        Contains(nearby.Speech, "Pitcher plant 1 open", "standing beside it");
        Check(nearby.Speech?.Contains("under you", StringComparison.Ordinal) == false, "not under the party");

        readout = new FieldActivityReadout();
        readout.Observe(Look(field: 620, second: 1, triangle: 19), Epoch);
        readout.Observe(Look(field: 620, second: 1, controlled: false, triangle: 19), Epoch.AddMilliseconds(100));
        Contains(readout.Observe(Look(field: 620, shaking: Set(), triangle: 16), Epoch.AddMilliseconds(200)).Speech,
            "Pitcher plant 2 opened under you", "620's second plant top");

        readout = new FieldActivityReadout();
        readout.Observe(Look(field: 623, fourth: 1, triangle: 44), Epoch);
        readout.Observe(Look(field: 623, fourth: 1, controlled: false, triangle: 44), Epoch.AddMilliseconds(100));
        Contains(readout.Observe(Look(field: 623, shaking: Set(), triangle: 56), Epoch.AddMilliseconds(200)).Speech,
            "Pitcher plant 4 opened under you", "623's fourth plant top");
    }

    private static void ScriptedMovementOnlyReportsChanges()
    {
        var (readout, now) = Settled(Look(held: 1));
        var jumping = readout.Observe(Look(held: 1, controlled: false, x: 200, y: -100), now.AddMilliseconds(100));
        Check(jumping.Speech is null && readout.CurrentReplaceableLine is null, "no speech while the field moves the party");
        Check(readout.Observe(Look(held: 1, x: 193, y: -114), now.AddMilliseconds(200)).Speech is null,
            "landing repeats nothing that did not change");
        readout.Observe(Look(held: 1, controlled: false), now.AddMilliseconds(300));
        var landed = readout.Observe(Look(held: 1, second: 1), now.AddMilliseconds(400));
        Contains(landed.Speech, "Pitcher plant 2 closed", "a change made during the jump is said after it");
        Check(landed.Speech?.Contains("Carrying", StringComparison.Ordinal) == false, "only what changed");
    }

    /// <summary>
    /// Confirm does more than set a creature down where Cloud stands: it picks up, throws into a
    /// nearby plant and uses the beehive. Switch gives up. Only the game's own buttons are named.
    /// </summary>
    private static void RepeatOffersOnlyRealControls()
    {
        const string confirm = "Use your normal Confirm button for pickup, release and throws.";
        const string giveUp = "Use your normal Switch button to return to the forest entrance.";
        var carrying = new FieldActivityReadout().Describe(Look(held: 2));
        Contains(carrying, confirm, "the native Confirm actions, while carrying");
        Contains(carrying, giveUp, "the native give-up");
        Check(carrying!.EndsWith(confirm + " " + giveUp, StringComparison.Ordinal), $"after the state: {carrying}");
        var empty = new FieldActivityReadout().Describe(Look());
        Contains(empty, confirm, "with empty hands too");
        Contains(empty, giveUp, "the give-up is always available");
        foreach (var word in new[] { "where you stand", "Press OK", "Press Switch", "second", "timer", "hurry", "then jump", "next" })
            Check(carrying.Contains(word, StringComparison.OrdinalIgnoreCase) == false, $"no single outcome, mapped key, countdown or solution ({word})");
    }

    private static void ResetForgetsLatchesAndPendingOutcomes()
    {
        var readout = new FieldActivityReadout();
        readout.Observe(Look(first: 1), Epoch);
        readout.Observe(Look(first: 1, shaking: Set(1)), Epoch.AddMilliseconds(100));
        readout.Reset();
        var fresh = readout.Observe(Look(first: 1, shaking: Set()), Epoch.AddMilliseconds(200));
        Contains(fresh.Speech, "Pitcher plant 1 closed", "after reset the plant is read again");
        Check(fresh.Speech?.Contains("shaking", StringComparison.Ordinal) == false, "reset forgets the shake");

        readout = new FieldActivityReadout();
        readout.Observe(Look(first: 1), Epoch);
        readout.Observe(Look(first: 1, shaking: Set(1)), Epoch.AddMilliseconds(100));
        readout.Observe(Look(first: 1, readable: false), Epoch.AddMilliseconds(200));
        var recovered = readout.Observe(Look(first: 1, shaking: Set()), Epoch.AddMilliseconds(300));
        Check(recovered.Speech?.Contains("shaking", StringComparison.Ordinal) == false, "an unknown sample forgets the shake");

        readout = new FieldActivityReadout();
        readout.Observe(Look(held: 2, models: [Hidden(7)]), Epoch);
        readout.Observe(Look(models: [Unread(7)]), Epoch.AddMilliseconds(100));
        readout.Observe(Look(field: 116), Epoch.AddMilliseconds(200));
        readout.Observe(Look(models: [Hidden(7)]), Epoch.AddMilliseconds(300));
        Check(readout.Observe(Look(models: [Seen(7, 400, -300)]), Epoch.AddMilliseconds(400)).Speech?
            .Contains("landed", StringComparison.Ordinal) != true, "an old release is not reported after leaving");

        readout = new FieldActivityReadout();
        readout.Observe(Look(x: 300, y: -900), Epoch);
        readout.Observe(Look(controlled: false, x: 441, y: -914), Epoch.AddMilliseconds(100));
        readout.Reset();
        Check(readout.Observe(Look(x: 179, y: -962), Epoch.AddMilliseconds(200)).Speech?
            .Contains("bit the party", StringComparison.Ordinal) != true, "reset forgets a bite");
    }

    // ---------------------------------------------------------------------------------------------
    // Delivery. Each line interrupts the one before it, so a line is heard only if nothing replaces
    // it before its words have been said.

    /// <summary>
    /// 622's throw, in native order: the creature's script runs the director, which sets the plant's
    /// flag (@7959), and bpd clears 5[9] on its next turn (@2502), about a frame later.
    /// </summary>
    private static void ClosingIsHeardWhenTheHandsEmptyAFrameLater()
    {
        var forest = new Listener();
        forest.At(Look(held: 1, models: [Hidden(6)]), Epoch);
        var thrown = Epoch.AddSeconds(10);
        forest.At(Look(held: 1, first: 1, models: [Hidden(6)]), thrown);
        forest.Hold(Look(first: 1, models: [Hidden(6)]), thrown.AddMilliseconds(100), 5);
        Contains(forest.Heard(), "Pitcher plant 1 closed", "the closing is not cut off by the empty hands");
        Contains(forest.Heard(), "Carrying nothing", "and the empty hands are heard too");
        Check(forest.Last == "Carrying nothing. Pitcher plant 1 closed.",
            $"the newest first, then the closing it cut off, and not the whole forest again: {forest.Last}");
    }

    /// <summary>620's beehive, in native order: its script sets the flytrap's flag 5[51], then bpd clears 5[9].</summary>
    private static void FlytrapShutIsHeardWhenTheHandsEmptyAFrameLater()
    {
        var forest = new Listener();
        forest.At(Look(field: 620, held: 5, shaking: Set()), Epoch);
        var thrown = Epoch.AddSeconds(10);
        forest.At(Look(field: 620, held: 5, shaking: Set(), trap: 1), thrown);
        forest.Hold(Look(field: 620, shaking: Set(), trap: 1), thrown.AddMilliseconds(100), 5);
        Contains(forest.Heard(), "Mutant Flytrap 1 closed", "the flytrap shutting is not cut off by the empty hands");
        Contains(forest.Heard(), "Carrying nothing", "and the empty hands are heard too");
        Check(forest.Last == "Carrying nothing. Mutant Flytrap 1 closed.", $"one line says both: {forest.Last}");
    }

    /// <summary>Two timed plant changes a sample apart: one plant opening as another starts to shake.</summary>
    private static void AdjacentPlantChangesAreBothHeard()
    {
        var forest = new Listener();
        forest.At(Look(field: 623, first: 1, second: 1, shaking: Set(1), triangle: 100), Epoch);
        var later = Epoch.AddSeconds(10);
        forest.At(Look(field: 623, second: 1, shaking: Set(), triangle: 100), later);
        forest.Hold(Look(field: 623, second: 1, shaking: Set(2), triangle: 100), later.AddMilliseconds(100), 5);
        Contains(forest.Heard(), "Pitcher plant 1 open", "the first plant opening");
        Contains(forest.Heard(), "Pitcher plant 2 shaking", "and the second shaking");
    }

    /// <summary>Each subject is said at its newest state: a plant that has opened is not still closed.</summary>
    private static void EachSubjectIsSaidAtItsNewestState()
    {
        var forest = new Listener();
        forest.At(Look(held: 1), Epoch);
        var later = Epoch.AddSeconds(10);
        forest.At(Look(held: 1, first: 1), later);
        forest.Hold(Look(shaking: Set()), later.AddMilliseconds(100), 5);
        Contains(forest.Last, "Pitcher plant 1 open", "the plant's newest state");
        Contains(forest.Last, "Carrying nothing", "with the other change");
        Check(!forest.Heard().Contains("closed", StringComparison.Ordinal), $"an opened plant is not said closed: {forest.Heard()}");
    }

    /// <summary>A line that has had time to be heard is not said again with the next change.</summary>
    private static void HeardLinesAreNotRepeated()
    {
        var forest = new Listener();
        forest.At(Look(held: 1), Epoch);
        var later = Epoch.AddSeconds(10);
        forest.At(Look(held: 1, first: 1), later);
        forest.Hold(Look(first: 1), later.AddSeconds(5), 1);
        Check(forest.Last == "Carrying nothing.", $"only the new change: {forest.Last}");
    }

    /// <summary>
    /// A line held back, behind other speech or with speech off, has not been heard either: it keeps
    /// every subject that changed until it is said.
    /// </summary>
    private static void HeldLinesKeepEveryUnheardSubject()
    {
        var forest = new Listener();
        forest.At(Look(held: 1), Epoch);
        var later = Epoch.AddSeconds(10);
        forest.Speaking = true;
        forest.Delivery.NoteOtherSpeech("Release insects and frogs by pressing Confirm. Insects and frogs react differently " +
            "depending on where you release them, so try many different places.", later);
        forest.At(Look(held: 1, first: 1), later.AddMilliseconds(100));
        forest.At(Look(first: 1), later.AddMilliseconds(200));
        forest.At(Look(first: 1, second: 1), later.AddSeconds(3));
        Check(forest.Said.Count == 1, "nothing cuts into the other speech");
        forest.Speaking = false;
        forest.At(Look(first: 1, second: 1), later.AddSeconds(4));
        foreach (var fact in new[] { "Pitcher plant 1 closed", "Carrying nothing", "Pitcher plant 2 closed" })
            Contains(forest.Last, fact, "a held line keeps every subject that changed");

        var muted = new Listener();
        muted.At(Look(held: 1), Epoch);
        muted.Muted = true;
        muted.At(Look(held: 1, first: 1), later);
        muted.At(Look(first: 1), later.AddSeconds(3));
        muted.Muted = false;
        muted.At(Look(first: 1), later.AddSeconds(4));
        Contains(muted.Last, "Pitcher plant 1 closed", "speech turned back on says what was missed");
        Contains(muted.Last, "Carrying nothing", "and what changed since");
    }

    /// <summary>
    /// What no longer holds is not carried into a later line: a warning about the plant under the
    /// leader once he has left it, or a creature's place once it is carried or left behind.
    /// </summary>
    private static void ExpiredWarningsAndOldPlacesAreNotRetained()
    {
        var forest = new Listener();
        forest.At(Look(first: 1, shaking: Set(1), triangle: 40), Epoch);
        var later = Epoch.AddSeconds(10);
        forest.At(Look(first: 1, shaking: Set(1), triangle: 14), later);
        Contains(forest.Last, "Pitcher plant 1 shaking under you", "stepping onto the shaking plant");
        forest.At(Look(held: 2, first: 1, shaking: Set(1), triangle: 40), later.AddMilliseconds(100));
        Contains(forest.Last, "Pitcher plant 1 shaking", "it is still shaking, and that was cut off");
        Check(!forest.Last.Contains("under you", StringComparison.Ordinal), $"the leader is no longer on it: {forest.Last}");
        Check(!forest.Readout.CurrentReplaceableLineIsUrgent, "so it is no longer a warning");

        var pickedUp = new Listener();
        pickedUp.At(Look(held: 2, models: [Hidden(7)], x: 100, y: 100), Epoch);
        pickedUp.At(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), later);
        Contains(pickedUp.Last, "Insect 2 is at your feet", "the release");
        pickedUp.At(Look(held: 2, models: [Hidden(7)], x: 100, y: 100), later.AddMilliseconds(300));
        Contains(pickedUp.Last, "Carrying insect 2", "picked up again");
        Check(!pickedUp.Last.Contains("feet", StringComparison.Ordinal), $"a creature carried is not at your feet: {pickedUp.Last}");

        var walked = new Listener();
        walked.At(Look(held: 2, models: [Hidden(7)], x: 100, y: 100), Epoch);
        walked.At(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), later);
        walked.At(Look(first: 1, models: [Seen(7, 108, 96)], x: 400, y: 400), later.AddMilliseconds(300));
        Contains(walked.Last, "Pitcher plant 1 closed", "the new change");
        Contains(walked.Last, "Carrying nothing", "with the release that was cut off");
        Check(!walked.Last.Contains("feet", StringComparison.Ordinal), $"a creature left behind is not at your feet: {walked.Last}");
        Contains(walked.Last, "Insect 2 landed.", "the release that was seen is kept, without its old place");

        Check(!pickedUp.Last.Contains("landed", StringComparison.Ordinal), $"a creature carried again has no release left to tell: {pickedUp.Last}");
        var eaten = new Listener();
        eaten.At(Look(held: 2, models: [Hidden(7)], x: 100, y: 100), Epoch);
        eaten.At(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), later);
        eaten.At(Look(first: 1, models: [Hidden(7)], x: 100, y: 100), later.AddMilliseconds(300));
        Check(!eaten.Last.Contains("Insect 2", StringComparison.Ordinal), $"a creature out of sight is not described: {eaten.Last}");
    }

    /// <summary>
    /// The warning that the plant under the leader is shaking still interrupts at once, and it comes
    /// first, ahead of anything earlier that has not been heard.
    /// </summary>
    private static void UrgentWarningLeadsAMergedLine()
    {
        var forest = new Listener();
        forest.At(Look(first: 1, shaking: Set(1), triangle: 40), Epoch);
        var later = Epoch.AddSeconds(10);
        forest.At(Look(held: 2, first: 1, shaking: Set(1), triangle: 40), later);
        forest.Delivery.NoteOtherSpeech("Pitcher plant 1, 2 meters north.", later.AddMilliseconds(50));
        forest.At(Look(held: 2, first: 1, shaking: Set(1), triangle: 14), later.AddMilliseconds(100));
        Check(forest.Last.StartsWith("Pitcher plant 1 shaking under you.", StringComparison.Ordinal), $"the warning comes first, at once: {forest.Last}");
        Contains(forest.Last, "Carrying insect 2", "followed by the pickup it cut off");
        Check(forest.Readout.CurrentReplaceableLineIsUrgent, "and it is urgent");
    }

    /// <summary>
    /// The field moving the party retires a line still waiting to be said. When control returns,
    /// what in it still holds is offered at once, even though nothing else has changed; a warning
    /// the movement retired is not offered as a warning again.
    /// </summary>
    private static void UnsaidLineIsOfferedAgainWhenControlReturns()
    {
        var forest = new Listener();
        forest.At(Look(held: 1), Epoch);
        var later = Epoch.AddSeconds(10);
        forest.Speaking = true;
        forest.Delivery.NoteOtherSpeech("Jump spot, 2 meters east.", later);
        forest.At(Look(held: 1, first: 1), later.AddMilliseconds(100));
        forest.At(Look(held: 1, first: 1, controlled: false), later.AddMilliseconds(200));
        Check(forest.Readout.CurrentReplaceableLine is null, "nothing is owed while the field moves the party");
        forest.Speaking = false;
        forest.At(Look(held: 1, first: 1), later.AddSeconds(2));
        Check(forest.Said.Count == 2, $"the closing is offered when control returns: {forest.Heard()}");
        Check(forest.Last == "Pitcher plant 1 closed.", $"only what was never said: {forest.Last}");
        forest.Hold(Look(held: 1, first: 1), later.AddSeconds(2.1), 5);
        Check(forest.Said.Count == 2, "and only once");

        var warned = new Listener();
        warned.At(Look(first: 1, shaking: Set(1), triangle: 40), Epoch);
        warned.Muted = true;
        warned.At(Look(first: 1, shaking: Set(1), triangle: 14), later);
        Check(warned.Readout.CurrentReplaceableLineIsUrgent, "on the shaking plant, a warning");
        warned.At(Look(first: 1, shaking: Set(1), triangle: 14, controlled: false), later.AddMilliseconds(100));
        Check(!warned.Readout.CurrentReplaceableLineIsUrgent, "the field moving the party retires it");
        warned.Muted = false;
        warned.At(Look(first: 1, shaking: Set(1), triangle: 40), later.AddSeconds(1));
        Check(warned.Said.Count == 2, "the plant still shaking is offered when control returns");
        Check(warned.Last == "Pitcher plant 1 shaking.", $"as it is now, not as a warning about where the leader was: {warned.Last}");
        Check(!warned.Readout.CurrentReplaceableLineIsUrgent, "and not urgent");
    }

    /// <summary>
    /// A bite, or a creature seen landing, set down at the leader's feet or appearing, is news for a
    /// short while and only while it still holds. What Cloud carries and how the plants stand are
    /// kept until they are heard.
    /// </summary>
    private static void OldEventsAreNotNewsAfterALongMute()
    {
        var later = Epoch.AddSeconds(10);
        var bitten = new Listener();
        bitten.At(Look(x: 300, y: -900), Epoch);
        bitten.Muted = true;
        bitten.At(Look(controlled: false, x: 441, y: -914), later);
        bitten.At(Look(x: 179, y: -962), later.AddSeconds(3));
        bitten.At(Look(first: 1, x: 179, y: -962), later.AddSeconds(5));
        bitten.Hold(Look(first: 1, x: 179, y: -962), later.AddSeconds(6), 30);
        bitten.Muted = false;
        bitten.At(Look(first: 1, x: 179, y: -962), later.AddSeconds(40));
        Check(bitten.Said.Count == 2, "speech back on says what is still news");
        Check(bitten.Last == "Pitcher plant 1 closed.", $"the plant, not an old bite: {bitten.Last}");
    }

    private static void OldReleaseIsNotNewsAfterALongMute()
    {
        var later = Epoch.AddSeconds(10);
        var dropped = new Listener();
        dropped.At(Look(held: 2, models: [Hidden(7)], x: 100, y: 100), Epoch);
        dropped.Muted = true;
        dropped.At(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), later);
        dropped.Hold(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), later.AddSeconds(1), 30);
        dropped.Muted = false;
        dropped.At(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), later.AddSeconds(32));
        Check(dropped.Last == "Carrying nothing.", $"the empty hands, not an old release: {dropped.Last}");

        var brief = new Listener();
        brief.At(Look(held: 2, models: [Hidden(7)], x: 100, y: 100), Epoch);
        brief.Muted = true;
        brief.At(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), later);
        brief.Muted = false;
        brief.At(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), later.AddSeconds(3));
        Check(brief.Last == "Insect 2 is at your feet. Carrying nothing.", $"a recent release that still holds is said: {brief.Last}");
    }

    private static void UnreadCreatureDoesNotKeepACurrentPlaceClaim()
    {
        foreach (var models in new IReadOnlyList<FieldActivityModelReading>[] { [Unread(7)], [] })
        {
            var forest = new Listener();
            forest.At(Look(held: 2, models: [Hidden(7)], x: 100, y: 100), Epoch);
            var later = Epoch.AddSeconds(10);
            forest.Muted = true;
            forest.At(Look(models: [Seen(7, 108, 96)], x: 100, y: 100), later);
            forest.Muted = false;
            forest.At(Look(models: models, first: 1, x: 400, y: 400), later.AddSeconds(1));
            Contains(forest.Last, "Insect 2 landed.", "the observed release remains available as a past event");
            Check(!forest.Last.Contains("at your feet", StringComparison.Ordinal),
                $"an unreadable current position cannot locate the creature beside the moved player: {forest.Last}");
        }
    }

    /// <summary>
    /// Guidance, dialogue or anything else the hosts say over a forest line cuts it off, as both hosts
    /// speak it with interruption. What the line said is offered again once that speech is over, with
    /// anything newer, as it is now.
    /// </summary>
    private static void LinesCutOffByOtherSpeechAreSaidAgain()
    {
        var later = Epoch.AddSeconds(10);
        var forest = new Listener();
        forest.At(Look(held: 1), Epoch);
        forest.At(Look(held: 1, first: 1), later);
        Check(forest.Last == "Pitcher plant 1 closed.", $"the closing is said: {forest.Last}");
        forest.Speaking = true;
        forest.Delivery.NoteOtherSpeech("Right 3.", later.AddMilliseconds(100));
        forest.Hold(Look(held: 1, first: 1), later.AddMilliseconds(100), 1.5);
        Check(forest.Said.Count == 2, "the guidance is not cut off in turn");
        forest.Speaking = false;
        forest.Hold(Look(held: 1, first: 1), later.AddSeconds(1.7), 5);
        Check(forest.Said.Count == 3, $"the closing the guidance cut off is said again once it is over: {forest.Heard()}");
        Check(forest.Last == "Pitcher plant 1 closed.", $"as it was: {forest.Last}");

        var merged = new Listener();
        merged.At(Look(held: 1), Epoch);
        merged.At(Look(held: 1, first: 1), later);
        merged.Speaking = true;
        merged.Delivery.NoteOtherSpeech("Right 3.", later.AddMilliseconds(300));
        merged.Hold(Look(held: 1, first: 1), later.AddMilliseconds(300), 1.6);
        merged.At(Look(first: 1), later.AddSeconds(2));
        merged.Speaking = false;
        merged.Hold(Look(first: 1), later.AddSeconds(2.5), 5);
        Check(merged.Said.Count == 3, $"one line after the guidance: {merged.Heard()}");
        Check(merged.Last == "Carrying nothing. Pitcher plant 1 closed.", $"what changed meanwhile, then what was cut off: {merged.Last}");

        var warned = new Listener();
        warned.At(Look(first: 1, shaking: Set(1), triangle: 40), Epoch);
        warned.At(Look(first: 1, shaking: Set(1), triangle: 14), later);
        Check(warned.Last == "Pitcher plant 1 shaking under you.", $"the warning: {warned.Last}");
        warned.Speaking = true;
        warned.Delivery.NoteOtherSpeech("Right 3.", later.AddMilliseconds(100));
        warned.At(Look(first: 1, shaking: Set(1), triangle: 40), later.AddMilliseconds(200));
        warned.Speaking = false;
        warned.Hold(Look(first: 1, shaking: Set(1), triangle: 40), later.AddSeconds(1.5), 3);
        Check(warned.Said.Count == 3 && warned.Last == "Pitcher plant 1 shaking.",
            $"a cut-off warning the leader has since stepped away from is offered as the plant's state: {warned.Heard()}");
        Check(!warned.Readout.CurrentReplaceableLineIsUrgent, "and not as a warning");
    }

    /// <summary>
    /// A screen reader that can tell is believed while it says it is still speaking the line, however
    /// long the words take at the estimated rate; one that never stops saying so is believed only as
    /// long as the delivery believes any line.
    /// </summary>
    private static void SlowSpeakerKeepsTheLineUntilItHasSaidIt()
    {
        var later = Epoch.AddSeconds(10);
        var slow = new Listener();
        slow.At(Look(held: 1), Epoch);
        slow.At(Look(held: 1, first: 1), later);
        slow.Speaking = true;
        slow.Hold(Look(held: 1, first: 1), later.AddMilliseconds(100), 2.2);
        slow.At(Look(first: 1), later.AddSeconds(2.5));
        Check(slow.Last == "Carrying nothing. Pitcher plant 1 closed.", $"a line still being said is not yet heard: {slow.Last}");
        slow.Speaking = false;
        slow.Hold(Look(first: 1), later.AddSeconds(2.6), 4);
        slow.At(Look(first: 1, second: 1), later.AddSeconds(7));
        Check(slow.Last == "Pitcher plant 2 closed.", $"once it has stopped, it has been heard: {slow.Last}");

        var stuck = new Listener { Speaking = true };
        stuck.At(Look(held: 1), Epoch);
        stuck.Hold(Look(held: 1), Epoch.AddMilliseconds(100), 9.8);
        stuck.At(Look(held: 1, first: 1), later);
        Contains(stuck.Last, "Carrying insect 1", "past its estimate, a line the device says it is still speaking is said again");
        stuck.Hold(Look(held: 1, first: 1), later.AddMilliseconds(100), 20);
        stuck.At(Look(first: 1), later.AddSeconds(21));
        Check(stuck.Last == "Carrying nothing.", $"a device that never stops is not believed for ever: {stuck.Last}");
    }

    private static void RoutineDirectionsAllowForestSpeechToFinish()
    {
        foreach (var (look, expected) in new[] {
                     (Look(first: 1), "Pitcher plant 1 closed."),
                     (Look(first: 1, shaking: Set(1), triangle: 14), "Pitcher plant 1 shaking under you.") })
        {
            var forest = new Listener();
            forest.At(Look(triangle: 40), Epoch);
            var later = Epoch.AddSeconds(20);
            var directions = 0;
            for (var ms = 0; ms <= 4200; ms += 50)
            {
                var at = later.AddMilliseconds(ms);
                forest.At(look, at);
                if (ms % 350 == 0 && !forest.Readout.HasPendingForestSpeech)
                {
                    // Same delivery tracking as routine guidance on both hosts.
                    forest.Delivery.NoteOtherSpeech("right 3", at);
                    forest.Said.Add((at, "right 3"));
                    directions++;
                }
            }
            Contains(forest.Heard(), expected, "rapid running directions must allow the current forest fact to finish");
            Check(directions > 0 && !forest.Readout.HasPendingForestSpeech,
                "routine directions resume after the fact is heard");
        }
        Check(!new FieldActivityReadout().HasPendingForestSpeech, "other fields do not defer directions");
    }

    /// <summary>A readout that has said its first look and given it time to be heard, and the time then.</summary>
    private static (FieldActivityReadout Readout, DateTime Now) Settled(FieldActivityObservation look)
    {
        var forest = new Listener();
        forest.At(look, Epoch);
        return (forest.Readout, Epoch.AddSeconds(10));
    }

    /// <summary>
    /// The hosts' path: the readout observes, the shared latest-line delivery hands the current line
    /// to a speaker, and the speaker records when it was given each line.
    /// </summary>
    private sealed class Listener
    {
        internal FieldActivityReadout Readout { get; } = new();
        internal FieldActivityLatestLineDelivery Delivery { get; } = new();
        internal List<(DateTime At, string Line)> Said { get; } = [];
        internal bool Muted { get; set; }
        internal bool? Speaking { get; set; }
        internal string Last => Said.Count == 0 ? "" : Said[^1].Line;

        internal void At(FieldActivityObservation look, DateTime at) => FieldActivityClockHost.Deliver(
            Readout.Observe(look, at), Readout, Delivery, at, () => true, () => !Muted,
            line => { Said.Add((at, line)); return true; }, () => Speaking);

        /// <summary>The same look, sampled every 100 ms for that many seconds.</summary>
        internal void Hold(FieldActivityObservation look, DateTime from, double seconds)
        {
            for (var at = from; at <= from.AddSeconds(seconds); at = at.AddMilliseconds(100)) At(look, at);
        }

        /// <summary>
        /// What reached the player: every line that ran for as long as its words take at the delivery's
        /// own slow rate before the next one cut in, and the last line.
        /// </summary>
        internal string Heard() => string.Join(" | ", Said
            .Where((said, index) => index == Said.Count - 1 || Said[index + 1].At - said.At >= Duration(said.Line))
            .Select(said => said.Line));

        private static TimeSpan Duration(string line) => TimeSpan.FromTicks(Math.Max(
            FieldActivityLatestLineDelivery.PerWord.Ticks * line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            FieldActivityLatestLineDelivery.MinimumProtection.Ticks));
    }

    private static string Show(IReadOnlySet<int>? set) => set is null ? "unknown" : "{" + string.Join(",", set.Order()) + "}";

    // ---------------------------------------------------------------------------------------------
    // Memory.

    private sealed class PuzzleMemory : ILegacyAddressSpace
    {
        internal const uint Pointer = 0x02000000;
        internal byte[] Bank { get; } = new byte[128];
        internal int Field = 622;
        internal bool FailBank;
        internal Action? AfterBankRead;
        public bool TryRead(uint address, Span<byte> destination)
        {
            if (address == FieldNavigationObjectReader.AddressTemporaryFieldBankBase)
            {
                if (FailBank) return false;
                Bank.CopyTo(destination);
                var after = AfterBankRead; AfterBankRead = null; after?.Invoke();
                return true;
            }
            if (address == FieldScriptContextReader.AddressCurrentModule && destination.Length == 1)
                destination[0] = FieldPositionReader.FieldModule;
            else if (address == FieldPositionReader.AddressFieldId && destination.Length == 2)
                BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)Field);
            else if (address == FieldScriptContextReader.AddressFieldScriptPtr && destination.Length == 4)
                BinaryPrimitives.WriteUInt32LittleEndian(destination, Pointer);
            else return false;
            return true;
        }
    }

    /// <summary>
    /// The loaded field as the readers see it: the script section (synthetic, or the real one from
    /// an installed or Echo-S root), the per-entity cursor arrays, the model tables and bank 5.
    /// </summary>
    private sealed class ForestMemory : ILegacyAddressSpace
    {
        internal const uint Pointer = 0x02000000;
        private const uint ObjectTable = 0x03000000;
        private const uint RenderTable = 0x04000000;
        private const int ScriptLength = 59;
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly Dictionary<uint, Action> afterRead = [];
        private int codeEnd;
        internal byte[] Bank { get; } = new byte[128];

        internal ForestMemory(int field, int entityCount = 48)
        {
            Context(field);
            W8(Pointer + 2, (byte)entityCount);
            W16(Pointer + 6, 0);
            for (var entity = 0; entity < entityCount; entity++)
                for (var script = 0; script < 32; script++) W16(EntryAddress(entity, script), 0);
            codeEnd = 32 + entityCount * 8 + entityCount * 32 * 2;
            for (var entity = 0; entity < entityCount; entity++) Idle(entity);
        }

        private ForestMemory() { }

        internal static ForestMemory Native(string root, int field)
        {
            var source = new FlevelDataSource(root);
            Check(source.TryReadField(field, out var encoded), $"{root}: field {field} is installed");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var at = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(6));
            var length = BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(at));
            var memory = new ForestMemory();
            memory.Context(field);
            memory.WBytes(Pointer, decoded.AsSpan(at + 4, length));
            for (var entity = 0; entity < memory.EntityCount; entity++) memory.Idle(entity);
            return memory;
        }

        private int EntityCount => bytes[Pointer + 2];

        private void Context(int field)
        {
            W8((uint)FieldScriptContextReader.AddressCurrentModule, FieldPositionReader.FieldModule);
            W16((uint)FieldPositionReader.AddressFieldId, (ushort)field);
            W32((uint)FieldScriptContextReader.AddressFieldScriptPtr, Pointer);
            W8((uint)FieldPositionReader.AddressFieldNumModels, 12);
            W32((uint)FieldScriptControllerReader.AddressModelTablePointer, ObjectTable);
            W32((uint)FieldPositionReader.AddressFieldModelsPtr, RenderTable);
        }

        internal string[] EntityNames() => Enumerable.Range(0, EntityCount).Select(entity =>
        {
            var name = new byte[8];
            for (var index = 0; index < 8; index++) name[index] = bytes[Pointer + 32 + (uint)(entity * 8 + index)];
            return System.Text.Encoding.ASCII.GetString(name).TrimEnd('\0');
        }).ToArray();

        private uint EntryAddress(int entity, int script)
        {
            var akao = BinaryPrimitives.ReadUInt16LittleEndian([bytes[Pointer + 6], bytes[Pointer + 7]]);
            return Pointer + 32 + (uint)(EntityCount * 8) + (uint)(akao * 4) + (uint)((entity * 32 + script) * 2);
        }

        private ushort Entry(int entity, int script)
        {
            var address = EntryAddress(entity, script);
            return (ushort)(bytes[address] | (bytes[address + 1] << 8));
        }

        /// <summary>The entity runs its main script: priority 7, script 0.</summary>
        internal void Idle(int entity)
        {
            W8((uint)(FieldScriptControllerReader.AddressEntityScriptPriorities + entity), 7);
            W8((uint)(FieldScriptControllerReader.AddressEntityScriptIds + entity * 8 + 7), 0);
            W16((uint)(FieldActivityStateReader.AddressEntityProgramCounters + entity * 2), Entry(entity, 0));
        }

        /// <summary>
        /// A synthetic plant whose script 3 is a native shake for this counter, running at priority 6.
        /// Stock shakes skip 0x31 bytes to their exit and are 59 bytes long; Echo-S 620's first plant
        /// skips 0x3B and is 69.
        /// </summary>
        internal void Shake(int entity, byte counter, int programCounter = 9, byte skip = 0x31, int length = ScriptLength)
        {
            var code = new byte[length];
            Array.Fill(code, (byte)0x5F);
            new byte[] { 0x14, 0x50, counter, 0x03, 0x03, skip }.CopyTo(code, 0);
            code[length - 1] = 0x00;
            var offset = codeEnd;
            codeEnd += 80;
            WBytes(Pointer + (uint)offset, code);
            W16(EntryAddress(entity, 3), (ushort)offset);
            RunNative(entity, programCounter);
        }

        /// <summary>The entity runs its own script 3 at priority 6, its counter this far in.</summary>
        internal void RunNative(int entity, int programCounter)
        {
            W8((uint)(FieldScriptControllerReader.AddressEntityScriptPriorities + entity), 6);
            W8((uint)(FieldScriptControllerReader.AddressEntityScriptIds + entity * 8 + 6), 3);
            W16((uint)(FieldActivityStateReader.AddressEntityProgramCounters + entity * 2), (ushort)(Entry(entity, 3) + programCounter));
        }

        internal void Model(int entity, byte model, bool visible, int x, int y)
        {
            W8((uint)(FieldScriptControllerReader.AddressEntityModelIds + entity), model);
            var record = ObjectTable + (uint)(model * FieldScriptControllerReader.ModelStride);
            W8(record + FieldScriptControllerReader.ModelVisibilityOffset, (byte)(visible ? 1 : 0));
            W32(record + FieldPositionReader.ObjectXOffset, (uint)(x << 12));
            W32(record + FieldPositionReader.ObjectYOffset, (uint)(y << 12));
            W32(record + FieldPositionReader.ObjectZOffset, 0);
            W16(record + FieldPositionReader.ObjectTriangleOffset, 0);
            W8(record + FieldScriptControllerReader.ModelAnimationIdOffset, 0);
            W8(RenderTable + (uint)(model * FieldPositionReader.FieldModelStride) + FieldPositionReader.ModelDirectionOffset, 0);
        }

        internal void OnceAfterRead(int address, Action action) => afterRead[(uint)address] = action;

        public bool TryRead(uint address, Span<byte> destination)
        {
            if (address == FieldNavigationObjectReader.AddressTemporaryFieldBankBase && destination.Length == Bank.Length)
            {
                Bank.CopyTo(destination);
                return true;
            }
            for (var index = 0; index < destination.Length; index++)
            {
                if (bytes.TryGetValue(address + (uint)index, out var value)) destination[index] = value;
                else if (address + (uint)index >= (uint)FieldScriptControllerReader.AddressEntityModelIds &&
                         address + (uint)index < (uint)FieldScriptControllerReader.AddressEntityModelIds + 256)
                    destination[index] = FieldScriptControllerReader.NoModel;
                else return false;
            }
            if (afterRead.Remove(address, out var action)) action();
            return true;
        }

        private void W8(uint address, byte value) => bytes[address] = value;
        private void W16(uint address, ushort value) { W8(address, (byte)value); W8(address + 1, (byte)(value >> 8)); }
        private void W32(uint address, uint value) { W16(address, (ushort)value); W16(address + 2, (ushort)(value >> 16)); }
        private void WBytes(uint address, ReadOnlySpan<byte> data) { for (var i = 0; i < data.Length; i++) bytes[address + (uint)i] = data[i]; }
    }

    private static void Contains(string? actual, string wanted, string because) =>
        Check(actual?.Contains(wanted, StringComparison.OrdinalIgnoreCase) == true, $"{because}: expected '{wanted}', got '{actual}'");
    private static void Check(bool condition, string because)
    { if (!condition) throw new InvalidOperationException(because); }
}
