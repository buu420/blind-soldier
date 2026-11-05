using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Bone Village dig spot (772 bonevil2), offered in Objects at the user's request: the
/// buried treasure the diggers are sent to look for, at its own model's position, reached only
/// by standing on the walkmesh triangle bonevil2's keyc script 3 compares the party's against.
///
/// <para>The native facts every case below rests on, all checked against the installed bytes
/// where the data root is available (<see cref="DigSpotIsTheNativeScript"/>):
/// luna (6) and box0..box6 (7..13) are placed by their Init and store the triangle under them
/// in Bank[6][16 + 2k]; the final choice (Bank[5][12] == 1) compares the party's triangle with
/// slots 16..28 in turn and sets Bank[1][234] to 1..7, box6's slot 30 never being compared; the
/// foreman's request is Bank[1][235] (1 Lunar Harp, 2 good, 3 normal treasure), and for 2 and 3
/// the box the diggers turn to is picked by Bank[5][15], rolled only at the blast; bonevil's
/// box1 Talk turns Bank[1][234] into the reward.</para>
/// </summary>
internal static class BoneVillageDigSpotTests
{
    internal static void Run(Func<int, FieldWalkmeshReader>? createWalkmeshReader, string? dataRoot)
    {
        LunarHarpSpotIsOfferedFromArrivalUntilTheChoice();
        LunarHarpSpotGoesOnceTheHarpIsReceived();
        OtherTreasureIsUnknownUntilTheBlast();
        ChosenBoxFollowsTheDiggersThresholds();
        UnknownRequestsOfferNothing();
        IncoherentInitialisationOffersNothing();
        TornReadsOfferNothing();
        OnlyTheDigFieldHasTheSpot();
        RewardsFollowTheBonevilPayout();
        TheSpotIsStableWhileItsNameChanges();
        if (dataRoot is not null && createWalkmeshReader is not null)
        {
            DigSpotIsTheNativeScript(dataRoot);
            NearButWrongTriangleKeepsWalking(createWalkmeshReader, dataRoot);
            EverySpotRoutesFromBothLevelsThroughTheLadder(createWalkmeshReader, dataRoot);
        }

        RunResults(createWalkmeshReader, dataRoot);
    }

    /// <summary>
    /// What the dig leaves behind. Nothing is ever picked up at the dig spot: bonevil2's keyc script
    /// 3 writes the result to Bank[1][234] (8 or 9 from the junk roll, 1..7 for an exact triangle) and
    /// MAPJUMPs to bonevil at (-206, -161) on triangle 64. There box1 (entity 13, CHAR 10), placed by
    /// its Init at (-327, -209, -27) on triangle 130, holds it: its Main poses it closed and lit while
    /// Bank[1][234] is not 0, and its Talk (script 1) runs only then, pays the result - the Key to
    /// Sector 5 for 5..7 after game moment 1195 until Bank[15][38] bit 3 - and sets Bank[1][234] back
    /// to 0. The player opens it with Confirm. These pin that chest in Objects: offered while a result
    /// is waiting, as a Talk at the native talk reach, sounding as a chest, and named without what is
    /// inside - the game says that only when it is opened. Installed and Echo-S bytes are checked
    /// first, so a run before the fix still proves them.
    /// </summary>
    internal static void RunResults(Func<int, FieldWalkmeshReader>? createWalkmeshReader, string? dataRoot)
    {
        if (dataRoot is not null)
        {
            ResultsChestIsTheNativeBox(dataRoot);
            ResultsChestIsBox1InEveryEchoS(dataRoot);
        }

        EveryPendingResultIsTheChestToOpen();
        TheChestNeverNamesWhatIsInside();
        AnEmptyOrUnreadableChestIsNotOffered();
        if (dataRoot is null || createWalkmeshReader is null)
        {
            return;
        }

        ResultsChestIsOpenedFromTheDigReturn(createWalkmeshReader, dataRoot);
        EchoSOffersTheSameResultsChest(dataRoot);
        Console.WriteLine($"Bone Village results chest native pickup, Talk reach and loaded Echo-S checks passed ({EchoSRoots().Length} Echo-S layouts).");
    }

    internal static void RunKeyStory(Func<int, FieldWalkmeshReader> createWalkmeshReader, string? dataRoot)
    {
        KeyToSector5StoryKnowledgeTests.Run();
        KeyToSector5ClueObservationTests.Run();
        KeyToSector5StoryTargetTests.Run();
        KeyToSector5SaveBindingTests.Run();
        if (dataRoot is null) return;

        var echoes = EchoSRoots();
        CheckKeyStoryLayout(createWalkmeshReader, dataRoot, dataRoot, isEcho: false);
        foreach (var echo in echoes)
            CheckKeyStoryLayout(createWalkmeshReader, dataRoot, echo, isEcho: true);
        Console.WriteLine($"Key to Sector 5 native clue, foreman, dig routes, results and collection passed (stock and {echoes.Length} Echo-S layouts).");
    }

    private static void CheckKeyStoryLayout(Func<int, FieldWalkmeshReader> createWalkmeshReader,
        string stockRoot, string layoutRoot, bool isEcho)
    {
        const int midgarGate = 170;
        const int foreman = 4;
        const int dig = 772;
        var name = Path.GetFileName(layoutRoot);
        var loaded = new LoadedFieldMemory();
        var source = new FlevelDataSource(layoutRoot);
        var catalog = new FieldScriptNavigationCatalog(stockRoot, Ff7GameLanguageDetector.Detect(stockRoot),
            new LoadedFieldScriptIdentityReader(loaded));
        var scripts = new FieldScriptNavigationCatalog(layoutRoot);
        var knowledge = new KeyToSector5StoryKnowledge(null);

        void Load(int field)
        {
            loaded.Load(source, field);
            var result = catalog.ReadField(field);
            Equal(true, result.IsUsable && result.UsesReviewedEchoLayout == isEcho,
                $"{name}: native field {field} is the expected layout ({result.Diagnostic})");
        }

        KeyToSector5StoryTargetReader ReaderFor(DigSpotMemory memory) =>
            new(memory.ReadInt32, address => (short)(memory.ReadByte(address) | memory.ReadByte(address + 1) << 8),
                memory.ReadByte, knowledge, field => catalog.ReadField(field).IsUsable, catalog.RemapStoryDefinition);

        FieldPositionSnapshot At(FieldWalkmesh mesh, int field, int x, int y, ushort triangle) =>
            new(FieldPositionReader.FieldModule, field, 0, x, y,
                FieldCrossFieldApproachResolver.SurfaceZ(mesh.Triangles[triangle], x, y), triangle, 0);

        void CheckTalk(FieldWalkmeshReader mesh, FieldPositionSnapshot start, FieldNavigationTarget target, int entity)
        {
            Equal(entity, target.TriggerEntityId, $"{name}: Story reaches the native Talk actor");
            Equal(FieldNavigationActivation.Talk, target.Activation, $"{name}: the player uses normal Confirm");
            var planner = new FieldWalkmeshRoutePlanner(mesh, null, _ => catalog.ReadField(start.FieldId).Transitions);
            Equal(true, planner.TryBuildRoute(start, target, out var plan), $"{name}: Talk approach ({planner.LastDiagnostic})");
            var dx = (double)plan!.FinalApproach.X - target.X;
            var dy = (double)plan.FinalApproach.Y - target.Y;
            Equal(true, dx * dx + dy * dy < target.InteractionRadius * target.InteractionRadius &&
                Math.Abs(plan.FinalApproach.Z - target.Z) < 256, $"{name}: endpoint is within native Talk reach");
        }

        var villageReader = createWalkmeshReader(VillageFieldId);
        var villageMesh = villageReader.Read(Anywhere(VillageFieldId)).Walkmesh!;
        var arrival = At(villageMesh, VillageFieldId, -213, -346, 84);
        var foremanMemory = DigSpotMemory.InVillage(0, moment: 1197);
        foremanMemory.OwnField(VillageFieldId);
        var foremanInit = scripts.ReadScriptOpcodes(VillageFieldId, foreman, 0);
        Equal(string.Empty, foremanInit.FirstOrDefault().EntityName, $"{name}: the foreman has a blank script name");
        var foremanPlace = foremanInit.First(op => op.Opcode == 0xA5).Bytes.ToArray();
        var foremanModel = foremanInit.First(op => op.Opcode == 0xA1).Bytes[1];
        Equal((-283, 63, 18, 28),
            ((int)BitConverter.ToInt16(foremanPlace, 3), (int)BitConverter.ToInt16(foremanPlace, 5),
                (int)BitConverter.ToInt16(foremanPlace, 7), (int)BitConverter.ToUInt16(foremanPlace, 9)),
            $"{name}: native excavation foreman placement");
        foremanMemory.MapTo(foreman, foremanModel);
        foremanMemory.ModelByte(foremanModel, FieldNavigationObjectReader.VisibilityOffset, 1);
        foremanMemory.ModelWord(foremanModel, FieldNavigationNpcReader.TalkRadiusOffset, 80);
        foremanMemory.PlaceBuried(foreman, BitConverter.ToInt16(foremanPlace, 3), BitConverter.ToInt16(foremanPlace, 5),
            BitConverter.ToInt16(foremanPlace, 7), BitConverter.ToUInt16(foremanPlace, 9));
        Load(VillageFieldId);
        var foremanStory = ReaderFor(foremanMemory);
        Equal(0, foremanStory.ReadTargets(arrival).Count, $"{name}: no quest before the conversation");

        Load(midgarGate);
        var clueId = KeyToSector5ClueObservationReader.ResolveReviewedClueMessageId(catalog.ReadField(midgarGate));
        Equal<int?>(isEcho ? 23 : 141, clueId, $"{name}: native clue from the gate man's Talk");
        loaded.ShowNativeMessage(clueId!.Value);
        var clue = new KeyToSector5ClueObservationReader(loaded,
            field => KeyToSector5ClueObservationReader.ResolveReviewedClueMessageId(catalog.ReadField(field)));
        Equal(true, clue.Observe(knowledge), $"{name}: the loaded native clue window establishes the quest");

        Load(VillageFieldId);
        var targets = foremanStory.ReadTargets(arrival);
        Equal(1, targets.Count, $"{name}: the known quest offers the foreman on arrival");
        CheckTalk(villageReader, arrival, targets.Single(), foreman);

        Load(dig);
        var digReader = createWalkmeshReader(dig);
        var digMesh = digReader.Read(Anywhere(dig)).Walkmesh!;
        var lower = At(digMesh, dig, -214, 0, 105);
        var upper = At(digMesh, dig, 104, 566,
            (ushort)FieldWalkmeshPathfinder.ResolveTriangle(digMesh, 104, 566, 330, 99));
        var choice = scripts.ReadScriptOpcodes(dig, 2, 3).ToArray();
        Equal(true, Hex(choice[0].Bytes).StartsWith("14 50 0c 01 00", StringComparison.Ordinal),
            $"{name}: native final Switch choice");
        foreach (var site in BoneVillageDigSpot.Sites.Where(site => site.DigResult is >= 5 and <= 7))
        {
            var init = scripts.ReadScriptOpcodes(dig, site.EntityId, 0);
            var place = init.First(op => op.Opcode == 0xA5).Bytes.ToArray();
            Equal((site.X, site.Y, site.Z, site.Triangle),
                ((int)BitConverter.ToInt16(place, 3), (int)BitConverter.ToInt16(place, 5),
                    (int)BitConverter.ToInt16(place, 7), (int)BitConverter.ToUInt16(place, 9)),
                $"{name}: native key marker {site.EntityId} placement");
            Equal($"b9 06 {site.EntityId:x2} {site.BankSlot:x2}", Hex(init.First(op => op.Opcode == 0xB9).Bytes),
                $"{name}: native marker stores the matching triangle");
            var comparison = Array.FindIndex(choice, op => op.Opcode == 0x16 && op.Bytes.Count >= 8 &&
                Hex(op.Bytes).StartsWith($"16 66 00 00 {site.BankSlot:x2} 00 00 05", StringComparison.Ordinal));
            Equal(true, comparison >= 0 && Hex(choice[comparison + 1].Bytes) == $"80 10 ea {site.DigResult:x2}",
                $"{name}: Switch on the triangle produces native result {site.DigResult}");

            var memory = DigSpotMemory.AfterInit(request: 3, phase: 1);
            memory.OwnField(dig);
            memory.SetMoment(1197);
            foreach (var other in BoneVillageDigSpot.Sites.Where(other => other.EntityId != site.EntityId))
                memory.Unmap(other.EntityId);
            var story = ReaderFor(memory);
            var planner = new FieldWalkmeshRoutePlanner(digReader, null, _ => catalog.ReadField(dig).Transitions);
            foreach (var start in new[] { lower, upper })
            {
                var target = story.ReadTargets(start).Single();
                Equal(site.Triangle, target.CompletionTriangles!.Single(), $"{name}: exact native choice triangle");
                Equal(true, planner.TryBuildRoute(start, target, out var plan), $"{name}: key route ({planner.LastDiagnostic})");
                Equal(site.Triangle, plan!.TargetTriangle, $"{name}: key route ends on the choice triangle");
                Equal(true, plan.Portals.Where(portal => portal.TransitionKind == FieldNavigationTransitionKind.Ladder)
                    .All(portal => portal.RequiresAction), $"{name}: climbing uses normal input");
            }
        }

        Load(VillageFieldId);
        ResultsChestScripts(scripts, name);
        var returned = At(villageMesh, VillageFieldId, DigReturn.X, DigReturn.Y, DigReturn.Triangle);
        var chestMemory = DigSpotMemory.InVillage(5, moment: 1197);
        chestMemory.OwnField(VillageFieldId);
        var chestStory = ReaderFor(chestMemory);
        var chest = chestStory.ReadTargets(returned).Single();
        Equal("Check the excavation results", chest.Label, $"{name}: chest name does not reveal its contents");
        CheckTalk(villageReader, returned, chest, ResultsChestEntityId);
        chestMemory.SetBank(15, 38, 0x08);
        Equal(0, chestStory.ReadTargets(returned).Count, $"{name}: native one-time reward retires the quest");
    }

    private static readonly DateTime Epoch = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private static FieldPositionSnapshot Anywhere(int field = 772) =>
        new(FieldPositionReader.FieldModule, field, 0, -40, 44, -40, 44, 0);

    private static FieldNavigationObjectReader Reader(DigSpotMemory memory) =>
        new(memory.ReadInt32, memory.ReadByte, _ => null, _ => null,
            FieldNavigationObjectCatalog.CreateAllFields(), isLineEnabled: _ => true);

    private static FieldNavigationTarget[] Spots(DigSpotMemory memory, int field = 772) =>
        Reader(memory).ReadTargets(Anywhere(field))
            .Where(target => target.Label.StartsWith("Dig spot", StringComparison.Ordinal))
            .ToArray();

    private static FieldNavigationTarget Single(DigSpotMemory memory, string label)
    {
        var spots = Spots(memory);
        Equal(1, spots.Length, $"{label}: one dig spot ({string.Join("|", spots.Select(s => s.Label))})");
        return spots[0];
    }

    // --- the Lunar Harp ------------------------------------------------------------------

    /// <summary>
    /// Asked for the Lunar Harp, the diggers can only turn to luna (e14 script 4), and luna's
    /// triangle is stored at Init, so the spot is known from arrival: every phase from placing
    /// the first digger (7) to the final choice (1). Nothing before Init or after the choice.
    /// </summary>
    private static void LunarHarpSpotIsOfferedFromArrivalUntilTheChoice()
    {
        foreach (var phase in new[] { 7, 6, 5, 4, 3, 2, 1 })
        {
            var memory = DigSpotMemory.AfterInit(request: 1, phase: phase);
            var spot = Single(memory, $"phase {phase}");
            Equal("Dig spot: Lunar Harp", spot.Label, $"phase {phase}: named for its prize");
            Equal((-319, 608, 326), (spot.X, spot.Y, spot.Z), $"phase {phase}: at luna's own position");
            Equal(FieldNavigationCategory.Objects, spot.Category, $"phase {phase}: listed with the room's objects");
            Equal("7", string.Join(",", spot.CompletionTriangles ?? []), $"phase {phase}: reached only on luna's triangle");
            Equal(true, spot.CompletesOnArrival, $"phase {phase}: arriving is the whole of it");
            Equal(true, string.IsNullOrWhiteSpace(spot.ManualNavigationGuidance), $"phase {phase}: auto walk can take the player there");
        }

        foreach (var phase in new[] { 0, 8, 255 })
        {
            Equal(0, Spots(DigSpotMemory.AfterInit(request: 1, phase: phase)).Length, $"phase {phase}: no dig under way, no spot");
        }
    }

    /// <summary>
    /// The foreman refuses the Lunar Harp once Bank[1][231] bit 3 is set, and the payout for
    /// luna's spot is then "Nothing in here!". A request of 1 with the bit set is stale.
    /// </summary>
    private static void LunarHarpSpotGoesOnceTheHarpIsReceived()
    {
        var memory = DigSpotMemory.AfterInit(request: 1, phase: 5);
        memory.SetBank(1, 231, 0x08);
        Equal(0, Spots(memory).Length, "the harp received: nothing is offered");
        memory.SetBank(1, 231, 0xF7);
        Equal("Dig spot: Lunar Harp", Single(memory, "other bits of 231").Label, "only bit 3 is the harp");
    }

    // --- other treasure ------------------------------------------------------------------

    /// <summary>
    /// Asked for good or normal treasure, which box is chosen is Bank[5][15], and that byte is
    /// rolled at the blast (keyc script 3 offset 375). Before the final choice it is whatever the
    /// temporary bank last held, so nothing is offered: no future roll is guessed.
    /// </summary>
    private static void OtherTreasureIsUnknownUntilTheBlast()
    {
        foreach (var request in new[] { 2, 3 })
        {
            foreach (var phase in new[] { 7, 6, 5, 4, 3, 2 })
            {
                var memory = DigSpotMemory.AfterInit(request: request, phase: phase);
                memory.SetTemp(15, 10);
                Equal(0, Spots(memory).Length, $"request {request}, phase {phase}: the box is not chosen yet");
            }
        }
    }

    /// <summary>
    /// e14..e18 script 4: good treasure turns to box0 below 80, box1 below 160, box2 otherwise;
    /// normal treasure to box3 below 60, box4 below 120, box5 below 180, box6 otherwise.
    /// </summary>
    private static void ChosenBoxFollowsTheDiggersThresholds()
    {
        var cases = new (int Request, int Roll, int Entity)[]
        {
            (2, 0, 7), (2, 79, 7), (2, 80, 8), (2, 159, 8), (2, 160, 9), (2, 255, 9),
            (3, 0, 10), (3, 59, 10), (3, 60, 11), (3, 119, 11), (3, 120, 12), (3, 179, 12), (3, 180, 13), (3, 255, 13)
        };
        foreach (var (request, roll, entity) in cases)
        {
            var memory = DigSpotMemory.AfterInit(request: request, phase: 1);
            memory.SetTemp(15, (byte)roll);
            var site = BoneVillageDigSpot.Sites.Single(s => s.EntityId == entity);
            var spot = Single(memory, $"request {request}, roll {roll}");
            Equal((site.X, site.Y, site.Z), (spot.X, spot.Y, spot.Z), $"request {request}, roll {roll}: e{entity}'s position");
            Equal(site.Triangle.ToString(), string.Join(",", spot.CompletionTriangles ?? []), $"request {request}, roll {roll}: e{entity}'s triangle");
        }
    }

    private static void UnknownRequestsOfferNothing()
    {
        foreach (var request in new[] { 0, 4, 9, 255 })
        {
            foreach (var phase in new[] { 7, 1 })
            {
                Equal(0, Spots(DigSpotMemory.AfterInit(request: request, phase: phase)).Length, $"request {request}, phase {phase}");
            }
        }
    }

    /// <summary>
    /// The spot is where the buried model is, on the triangle its Init stored. It is offered only
    /// while the stored triangle, the model's own triangle (event +0x78) and its placement all
    /// agree with the field's script: before Init (temporary bank zeroed on field load), with the
    /// model missing, or with any of them disagreeing, nothing is offered.
    /// </summary>
    private static void IncoherentInitialisationOffersNothing()
    {
        var cases = new (string Label, Action<DigSpotMemory> Break)[]
        {
            ("the stored triangle is still zero", m => m.SetTempWord(16, 0)),
            ("the stored triangle is another one", m => m.SetTempWord(16, 8)),
            ("the stored triangle's high byte is set", m => m.SetTemp(17, 1)),
            ("luna's own triangle differs", m => m.PlaceBuried(6, -319, 608, 326, 8)),
            ("luna is somewhere else", m => m.PlaceBuried(6, -200, 608, 326, 7)),
            ("luna has no model", m => m.Unmap(6)),
            ("luna's model is past the model count", m => m.MapTo(6, 40)),
            ("the event table is missing", m => m.SetInt(FieldNavigationObjectReader.AddressFieldEventDataPtr, 0))
        };
        foreach (var (label, breakIt) in cases)
        {
            var memory = DigSpotMemory.AfterInit(request: 1, phase: 7);
            breakIt(memory);
            Equal(0, Spots(memory).Length, label);
        }

        var box = DigSpotMemory.AfterInit(request: 3, phase: 1);
        box.SetTemp(15, 200);
        box.SetTempWord(30, 76);
        Equal(0, Spots(box).Length, "box6's stored triangle disagrees with its script");
    }

    /// <summary>
    /// A read torn across a frame is not an answer: the phase, the request and the roll are read
    /// before and after everything else and must be the same.
    /// </summary>
    private static void TornReadsOfferNothing()
    {
        var memory = DigSpotMemory.AfterInit(request: 2, phase: 1);
        memory.SetTemp(15, 10);
        memory.ChangeAfterReads(FieldNavigationObjectReader.AddressTemporaryFieldBankBase + 15, 1, 200);
        Equal(0, Spots(memory).Length, "the roll changed during the read");

        var phase = DigSpotMemory.AfterInit(request: 1, phase: 3);
        phase.ChangeAfterReads(FieldNavigationObjectReader.AddressTemporaryFieldBankBase + 12, 1, 9);
        Equal(0, Spots(phase).Length, "the phase became invalid during the read");
    }

    private static void OnlyTheDigFieldHasTheSpot()
    {
        var memory = DigSpotMemory.AfterInit(request: 1, phase: 7);
        foreach (var field in new[] { 617, 771, 773 })
        {
            Equal(0, Reader(memory).ReadTargets(Anywhere(field)).Count(t => t.Label.StartsWith("Dig spot", StringComparison.Ordinal)),
                $"field {field}");
        }
    }

    // --- rewards -------------------------------------------------------------------------

    /// <summary>
    /// bonevil's box1 Talk, result by result. A prize given once is named until its flag is
    /// set; the later gates (game moment 1620 for the materia, over 1195 for the Key to Sector 5)
    /// are read as the script reads them; a chance is said to be a chance and nothing more, and a
    /// spot with nothing left says so rather than naming what was already taken.
    /// </summary>
    private static void RewardsFollowTheBonevilPayout()
    {
        string LabelFor(int request, int roll, int moment, Action<DigSpotMemory>? state = null)
        {
            var memory = DigSpotMemory.AfterInit(request: request, phase: 1);
            memory.SetTemp(15, (byte)roll);
            memory.SetMoment(moment);
            state?.Invoke(memory);
            return Single(memory, $"request {request} roll {roll} moment {moment}").Label;
        }

        // box0, result 2.
        Equal("Dig spot: Buntline", LabelFor(2, 0, 1000), "Buntline until Bank[15][35] bit 5");
        Equal("Dig spot: Buntline", LabelFor(2, 0, 1700), "Buntline first even late");
        Equal("Dig spot: no treasure left", LabelFor(2, 0, 1619, m => m.SetBank(15, 35, 0x20)), "then nothing before 1620");
        Equal("Dig spot: Phoenix Materia", LabelFor(2, 0, 1620, m => m.SetBank(15, 35, 0x20)), "Phoenix from 1620");
        Equal("Dig spot: no treasure left", LabelFor(2, 0, 1620, m => { m.SetBank(15, 35, 0x20); m.SetBank(13, 97, 0x02); }),
            "Phoenix taken");
        // box1, result 3.
        Equal("Dig spot: Megalixir", LabelFor(2, 80, 1000), "Megalixir until Bank[15][38] bit 2");
        Equal("Dig spot: no treasure left", LabelFor(2, 80, 1619, m => m.SetBank(15, 38, 0x04)), "then nothing before 1620");
        Equal("Dig spot: small chance of Bahamut ZERO Materia", LabelFor(2, 80, 1620, m => m.SetBank(15, 38, 0x04)),
            "10 in 256 from 1620");
        Equal("Dig spot: no treasure left", LabelFor(2, 80, 1620, m => { m.SetBank(15, 38, 0x04); m.SetBank(13, 97, 0x01); }),
            "Bahamut ZERO taken");
        // box2, result 4.
        Equal("Dig spot: Mop", LabelFor(2, 160, 1000), "Mop until Bank[15][38] bit 1");
        Equal("Dig spot: no treasure left", LabelFor(2, 160, 1619, m => m.SetBank(15, 38, 0x02)), "then nothing before 1620");
        Equal("Dig spot: W-Item Materia", LabelFor(2, 160, 1620, m => m.SetBank(15, 38, 0x02)), "W-Item from 1620");
        Equal("Dig spot: no treasure left", LabelFor(2, 160, 1620, m => { m.SetBank(15, 38, 0x02); m.SetBank(13, 97, 0x04); }),
            "W-Item taken");
        // box3..box5, results 5..7: the Key to Sector 5 after 1195 until Bank[15][38] bit 3.
        foreach (var roll in new[] { 0, 60, 120 })
        {
            Equal("Dig spot: Key to Sector 5", LabelFor(3, roll, 1196), $"roll {roll}: the key after 1195");
        }

        Equal("Dig spot: chance of an Elixir", LabelFor(3, 0, 1195), "box3 at 1195: 36 in 256");
        Equal("Dig spot: chance of an Elixir", LabelFor(3, 0, 1196, m => m.SetBank(15, 38, 0x08)), "box3 with the key taken");
        Equal("Dig spot: Ether, or a small chance of Turbo Ether", LabelFor(3, 60, 1195), "box4: always one of them");
        Equal("Dig spot: Ether, or a small chance of Turbo Ether", LabelFor(3, 60, 1196, m => m.SetBank(15, 38, 0x08)), "box4 with the key taken");
        Equal("Dig spot: chance of an Ether", LabelFor(3, 120, 1195), "box5: 76 in 256");
        Equal("Dig spot: chance of an Ether", LabelFor(3, 120, 1196, m => m.SetBank(15, 38, 0x08)), "box5 with the key taken");
        // box6: never compared, so the junk roll: a Potion half the time.
        Equal("Dig spot: chance of a Potion", LabelFor(3, 180, 1196), "box6 has no prize of its own");
        Equal("Dig spot: chance of a Potion", LabelFor(3, 255, 500), "whatever the moment");
    }

    /// <summary>
    /// The locked target keeps its identity while its name follows the saved state, and every
    /// read gives the same triangle list, so the x64 host's two-read comparison agrees.
    /// </summary>
    private static void TheSpotIsStableWhileItsNameChanges()
    {
        var memory = DigSpotMemory.AfterInit(request: 2, phase: 1);
        memory.SetTemp(15, 0);
        var first = Single(memory, "first read");
        var again = Single(memory, "second read");
        Equal(first, again, "two reads of the same state are the same target");
        memory.SetBank(15, 35, 0x20);
        memory.SetMoment(1620);
        var renamed = Single(memory, "renamed");
        Equal(first.StableId, renamed.StableId, "the name changed, the target did not");
        Equal(false, first.Label == renamed.Label, "the name did change");
        var harp = Single(DigSpotMemory.AfterInit(request: 1, phase: 7), "harp at 7");
        Equal(harp.StableId, Single(DigSpotMemory.AfterInit(request: 1, phase: 1), "harp at 1").StableId,
            "the harp's spot is the same target through every phase");
    }

    // --- installed data ------------------------------------------------------------------

    /// <summary>
    /// Everything the reader assumes, read from the installed bonevil2 and bonevil: each buried
    /// model's Init placement and stored slot, the order of keyc's comparisons, the diggers'
    /// thresholds and targets, the foreman's request values and the harp refusal, and the
    /// payout's gates.
    /// </summary>
    private static void DigSpotIsTheNativeScript(string dataRoot)
    {
        var catalog = new FieldScriptNavigationCatalog(dataRoot);
        foreach (var site in BoneVillageDigSpot.Sites)
        {
            var init = catalog.ReadScriptOpcodes(772, site.EntityId, 0);
            var place = init.First(op => op.Opcode == 0xA5).Bytes.ToArray();
            Equal((site.X, site.Y, site.Z, site.Triangle),
                ((int)BitConverter.ToInt16(place, 3), (int)BitConverter.ToInt16(place, 5), (int)BitConverter.ToInt16(place, 7),
                    (int)BitConverter.ToUInt16(place, 9)),
                $"e{site.EntityId}'s Init placement");
            var store = init.First(op => op.Opcode == 0xB9).Bytes.ToArray();
            Equal("b9 06 " + site.EntityId.ToString("x2") + " " + site.BankSlot.ToString("x2"), Hex(store),
                $"e{site.EntityId} stores the triangle under itself in Bank[6][{site.BankSlot}]");
        }

        var keyc = catalog.ReadScriptOpcodes(772, 2, 3);
        Equal("14 50 0c 01 00 e4", Hex(keyc[0].Bytes), "keyc script 3 starts with the final choice, Bank[5][12] == 1");
        var compares = keyc.TakeWhile(op => op.ByteIndex < 233).ToArray();
        for (var index = 0; index < compares.Length - 1; index++)
        {
            var bytes = compares[index].Bytes.ToArray();
            if (bytes is [0x16, 0x66, 0x00, 0x00, var slot, 0x00, 0x00, 0x05, ..])
            {
                var result = compares[index + 1].Bytes.ToArray();
                Equal(0x80, (int)result[0], $"slot {slot}: its comparison sets the result");
                var site = BoneVillageDigSpot.Sites.Single(s => s.BankSlot == slot);
                Equal(site.DigResult, (int)result[3], $"slot {slot} is result {site.DigResult}");
            }
        }

        Equal("16,18,20,22,24,26,28",
            string.Join(",", compares.Where(op => op.Opcode == 0x16).Select(op => op.Bytes[4])),
            "seven comparisons; box6's slot 30 is never compared");
        Equal(0, BoneVillageDigSpot.Sites.Single(s => s.EntityId == 13).DigResult, "and so box6 has no result of its own");
        Equal("14 50 09 80 03 07", Hex(compares.First(op => op.Opcode == 0x14 && op.Bytes[2] == 0x09).Bytes),
            "the junk roll: Bank[5][9] below 128 is nothing, otherwise the Potion result 9");
        Equal(true, keyc.Any(op => Hex(op.Bytes) == "99 05 0f" && op.ByteIndex is > 233 and < 409),
            "Bank[5][15] is rolled during the blast");

        var turn = catalog.ReadScriptOpcodes(772, 14, 4).Select(op => Hex(op.Bytes)).ToArray();
        Equal(string.Join("|",
                "14 10 eb 01 00 05", "ab 06 0a 02", "14 10 eb 02 00 1d", "14 50 0f 50 03 07", "ab 07 0a 02", "10 11",
                "14 50 0f a0 03 07", "ab 08 0a 02", "10 05", "ab 09 0a 02", "14 10 eb 03 00 29", "14 50 0f 3c 03 07",
                "ab 0a 0a 02", "10 1d", "14 50 0f 78 03 07", "ab 0b 0a 02", "10 11", "14 50 0f b4 03 07", "ab 0c 0a 02",
                "10 05", "ab 0d 0a 02", "00"),
            string.Join("|", turn), "the diggers turn by request and roll exactly so");
        foreach (var entity in new[] { 15, 16, 17, 18 })
        {
            Equal(string.Join("|", turn), string.Join("|", catalog.ReadScriptOpcodes(772, entity, 4).Select(op => Hex(op.Bytes))),
                $"digger e{entity} turns the same way");
        }

        var foreman = catalog.ReadScriptOpcodes(617, 4, 1).Select(op => Hex(op.Bytes)).ToArray();
        foreach (var expected in new[] { "14 10 e7 03 0a 07", "80 10 eb 01", "80 10 eb 02", "80 10 eb 03" })
        {
            Equal(true, foreman.Contains(expected), $"the foreman: {expected}");
        }

        var payout = catalog.ReadScriptOpcodes(617, 13, 1).Select(op => Hex(op.Bytes)).ToArray();
        foreach (var expected in new[]
                 {
                     "14 10 ea 01 00 2b", "14 10 e7 03 09 10", "82 10 e7 03",
                     "14 10 ea 02 00 78", "14 f0 23 05 09 5c", "16 20 00 00 54 06 04 45", "14 d0 61 01 0a 30", "5b 00 00 54 00 00 00", "58 00 f8 00 01",
                     "14 10 ea 03 00 90", "14 f0 26 02 09 74", "16 20 00 00 54 06 04 5d", "14 50 07 0a 03 45", "14 d0 61 00 0a 30", "5b 00 00 58 00 00 00", "58 00 06 00 01",
                     "14 10 ea 04 00 78", "14 f0 26 01 09 5c", "14 d0 61 02 0a 30", "5b 00 00 15 00 00 00", "58 00 d1 00 01",
                     "14 10 ea 05 00 7c", "16 20 00 00 ab 04 02 4a", "14 f0 26 03 0a 18", "14 50 07 dc 03 10", "58 00 05 00 01",
                     "14 10 ea 06 00 86", "16 20 00 00 ab 04 02 4f", "14 50 07 14 03 15", "58 00 04 00 01", "58 00 03 00 01",
                     "14 10 ea 07 00 7c", "14 50 07 b4 03 10",
                     "14 10 ea 08 00 0e", "14 10 ea 09 00 13", "58 00 00 00 01"
                 })
        {
            Equal(true, payout.Contains(expected), $"the payout: {expected}");
        }
    }

    /// <summary>
    /// Standing next to luna's triangle - inside any proximity an arrival radius would allow - is
    /// not the spot: keyc compares triangles, so auto walk keeps going, and only a step onto
    /// triangle 7 ends the navigation. The object reader and the shared controller, on the
    /// installed walkmesh.
    /// </summary>
    private static void NearButWrongTriangleKeepsWalking(Func<int, FieldWalkmeshReader> createWalkmeshReader, string dataRoot)
    {
        var mesh = createWalkmeshReader(772);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, 772, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var transitions = new FieldScriptNavigationCatalog(dataRoot).ReadField(772).Transitions;
        var memory = DigSpotMemory.AfterInit(request: 1, phase: 1);
        var reader = Reader(memory);
        var planner = new FieldWalkmeshRoutePlanner(mesh, null, _ => transitions);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], objectTargetProvider: position => DigSpotsOnly(reader, position)), planner);
        var transform = new FieldNavigationControlTransform(0);
        const int arrival = 120;

        FieldPositionSnapshot On(int x, int y)
        {
            var triangle = FieldWalkmeshPathfinder.ResolveTriangle(walkmesh, x, y, 328, 8);
            var z = FieldCrossFieldApproachResolver.SurfaceZ(walkmesh.Triangles[triangle], x, y);
            return new FieldPositionSnapshot(FieldPositionReader.FieldModule, 772, 0, x, y, z, (ushort)triangle, 0);
        }

        // Just across luna's edge with triangle 8: about 83 units from luna, inside the radius.
        var beside = On(-277, 536);
        Equal(8, (int)beside.TriangleId, "the step beside the spot is on triangle 8");
        Equal(true, Math.Sqrt(Math.Pow(beside.X + 319, 2) + Math.Pow(beside.Y - 608, 2)) < arrival, "and within the arrival radius");
        for (var index = 0; index < 4 && controller.CurrentCategory != FieldNavigationCategory.Objects; index++)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, beside, transform);
        }

        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, beside, transform);
        Equal(true, controller.BeaconEnabled, $"navigation starts ({controller.LastNavigationDiagnostic})");
        var near = controller.UpdateLiveTracking(beside, new(0, FieldNavigationInput.None), transform, false, arrival, observedAt: Epoch);
        Equal(false, near?.Speech.Contains("reached", StringComparison.Ordinal) == true, $"not reached beside it ({near?.Speech})");
        Equal(true, controller.BeaconEnabled, "navigation goes on");
        Equal(true, controller.TryResolveAutomaticInput(beside, transform, arrival, out var input) && input != FieldNavigationInput.None,
            $"auto walk keeps walking ({controller.LastAutomaticInputHold}, {controller.LastNavigationDiagnostic})");

        var onSpot = On(-300, 580);
        Equal(7, (int)onSpot.TriangleId, "the next step is on luna's triangle");
        var reached = controller.UpdateLiveTracking(onSpot, new(0, FieldNavigationInput.None), transform, false, arrival,
            observedAt: Epoch.AddMilliseconds(100));
        Equal(true, reached?.Speech.StartsWith("Dig spot: Lunar Harp reached", StringComparison.Ordinal) == true,
            $"reached on it ({reached?.Speech})");
        Equal(false, controller.BeaconEnabled, "and navigation ends there, for the player to press Switch");
    }

    /// <summary>
    /// Every spot the diggers can be sent to is reached from both levels: from the foreman's
    /// arrival below (105) and from the upper ledge the diggers climb to. A route between levels
    /// climbs the native ladder, which asks for the player's own OK, and every route ends on the
    /// spot's triangle. The controller starts navigation and auto walk to each, and holds for the
    /// ladder's OK once it stands there.
    /// </summary>
    private static void EverySpotRoutesFromBothLevelsThroughTheLadder(Func<int, FieldWalkmeshReader> createWalkmeshReader, string dataRoot)
    {
        var mesh = createWalkmeshReader(772);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, 772, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var transitions = new FieldScriptNavigationCatalog(dataRoot).ReadField(772).Transitions;
        FieldPositionSnapshot At(int x, int y, ushort triangle) =>
            new(FieldPositionReader.FieldModule, 772, 0, x, y,
                FieldCrossFieldApproachResolver.SurfaceZ(walkmesh.Triangles[triangle], x, y), triangle, 0);
        var lower = At(-214, 0, 105);
        // Up on the ledge where the diggers walk to after the ladder (their script 3's (104, 566)),
        // away from the ladder's own landing, where auto walk rightly waits for OK at once.
        var upperTriangle = FieldWalkmeshPathfinder.ResolveTriangle(walkmesh, 104, 566, 330, 99);
        var upper = At(104, 566, (ushort)upperTriangle);
        Equal(true, upper.Z > 150, $"the ledge start is on the upper level (triangle {upperTriangle}, z {upper.Z})");
        var chosen = new (int Request, int Roll)[] { (1, 0), (2, 0), (2, 80), (2, 160), (3, 0), (3, 60), (3, 120), (3, 180) };
        foreach (var (request, roll) in chosen)
        {
            var memory = DigSpotMemory.AfterInit(request: request, phase: 1);
            memory.SetTemp(15, (byte)roll);
            var reader = Reader(memory);
            foreach (var (levelName, start) in new[] { ("lower", lower), ("upper", upper) })
            {
                var spot = reader.ReadTargets(start).Single(t => t.Label.StartsWith("Dig spot", StringComparison.Ordinal));
                var site = BoneVillageDigSpot.Sites.Single(s => s.X == spot.X && s.Y == spot.Y);
                var label = $"e{site.EntityId} from the {levelName} level";
                var planner = new FieldWalkmeshRoutePlanner(mesh, null, _ => transitions);
                Equal(true, planner.TryBuildRoute(start, spot, out var plan), $"{label}: {planner.LastDiagnostic}");
                Equal(site.Triangle, plan.TargetTriangle, $"{label}: ends on the spot's triangle");
                var spotIsUpper = site.Z > 150;
                var startIsUpper = start.Z > 150;
                var ladders = plan.Portals.Where(p => p.TransitionKind == FieldNavigationTransitionKind.Ladder).ToArray();
                Equal(spotIsUpper == startIsUpper ? 0 : 1, ladders.Length, $"{label}: one ladder between levels, none within one");
                Equal(true, ladders.All(p => p.RequiresAction), $"{label}: the ladder climbs on the player's own OK");

                var controller = new FieldNavigationController(
                    new FieldNavigationTargetSource([], objectTargetProvider: position => DigSpotsOnly(reader, position)), planner);
                var transform = new FieldNavigationControlTransform(0);
                for (var index = 0; index < 4 && controller.CurrentCategory != FieldNavigationCategory.Objects; index++)
                {
                    controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
                }

                _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
                Equal(true, controller.BeaconEnabled, $"{label}: navigation starts ({controller.LastNavigationDiagnostic})");
                _ = controller.UpdateLiveTracking(start, new(0, FieldNavigationInput.None), transform, false, 80, observedAt: Epoch);
                Equal(true, controller.TryResolveAutomaticInput(start, transform, 80, out var input) && input != FieldNavigationInput.None,
                    $"{label}: auto walk has somewhere to go ({controller.LastNavigationDiagnostic})");
                if (ladders.Length == 1)
                {
                    var foot = ladders[0].Midpoint;
                    var footTriangle = ladders[0].FromTriangle;
                    var atLadder = At(foot.X, foot.Y, (ushort)footTriangle);
                    _ = controller.UpdateLiveTracking(atLadder, new(0, FieldNavigationInput.None), transform, false, 80,
                        observedAt: Epoch.AddSeconds(1));
                    var moving = controller.TryResolveAutomaticInput(atLadder, transform, 80, out _);
                    Equal(true, !moving && controller.LastAutomaticInputHold == FieldAutoWalkHoldReason.PlayerAction,
                        $"{label}: auto walk stops at the ladder for the player's OK ({controller.LastAutomaticInputHold}, {controller.LastNavigationDiagnostic})");
                }
            }
        }
    }

    // --- the results chest (617 bonevil, box1) ---------------------------------------------

    private const int VillageFieldId = 617;
    private const int ResultsChestEntityId = 13;
    private const int ResultsChestModelId = 10;
    private const int ResultIndex = 234;

    // 0060bcfa leaves a model without TALKR at a talk radius of 80 x scale / 512, and bonevil's
    // scale is 512; the leader's width there is 34 (00633c2f). 00636284 lets the party talk while
    // the XY distance is below their sum (see BoneVillageForemanTests).
    private const int ChestTalkRadius = 80;
    private const int LeaderWidth = 34;
    private const int NativeChestTalkReach = ChestTalkRadius + LeaderWidth;

    private static readonly (int X, int Y, int Z, int Triangle) ChestPlacement = (-327, -209, -27, 130);
    private static readonly (int X, int Y, ushort Triangle) DigReturn = (-206, -161, 64);

    // Every name box1's Talk can pay out, and the words a name would use to hint at one.
    private static readonly string[] PrizeWords =
    [
        "Key", "Sector", "Lunar", "Harp", "Buntline", "Phoenix", "Megalixir", "Bahamut", "Mop", "W-Item",
        "Elixir", "Ether", "Potion", "Materia", "nothing", "no treasure", "chance"
    ];

    private static string? PrizeIn(string label) =>
        PrizeWords.FirstOrDefault(word => label.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static FieldNavigationTarget[] ResultsChests(FieldNavigationObjectReader reader, FieldPositionSnapshot position) =>
        reader.ReadTargets(position)
            .Where(target => target.Category == FieldNavigationCategory.Objects && target.TriggerEntityId == ResultsChestEntityId)
            .ToArray();

    /// <summary>
    /// Every result keyc can write, early in the story and late: exactly one chest, box1's, at its
    /// model's live position; reached as a Talk is, at the native talk reach; sounding as a chest;
    /// opened by the player, not by arriving; and under one name for every result.
    /// </summary>
    private static void EveryPendingResultIsTheChestToOpen()
    {
        string? name = null;
        foreach (var result in Enumerable.Range(1, 9))
        {
            foreach (var moment in new[] { 641, 1620 })
            {
                var label = $"results chest, result {result}, moment {moment}";
                var chests = ResultsChests(Reader(DigSpotMemory.InVillage(result, moment)), Anywhere(VillageFieldId));
                Equal(1, chests.Length, $"{label}: one chest in Objects ({string.Join("|", chests.Select(c => c.Label))})");
                var chest = chests[0];
                Equal((ChestPlacement.X, ChestPlacement.Y, ChestPlacement.Z), (chest.X, chest.Y, chest.Z), $"{label}: at box1's own position");
                Equal(FieldNavigationActivation.Talk, chest.Activation, $"{label}: box1 is opened by its Talk script, so it is reached as a Talk");
                Equal(NativeChestTalkReach, chest.InteractionRadius, $"{label}: its reach is its talk radius plus the leader's width");
                Equal(FieldObjectCueKind.Chest, chest.ObjectCueKind, $"{label}: it sounds as the chest it is");
                Equal(false, chest.CompletesOnArrival, $"{label}: arriving does not open it, the player's Confirm does");
                Equal(true, string.IsNullOrWhiteSpace(chest.ManualNavigationGuidance), $"{label}: auto walk can take the player there");
                name ??= chest.Label;
                Equal(name, chest.Label, $"{label}: every result has the same name, so the name says nothing of the result");
            }
        }

        var moved = DigSpotMemory.InVillage(5);
        moved.PlaceBuried(ResultsChestEntityId, -300, -190, -27, ChestPlacement.Triangle);
        Equal((-300, -190, -27), ResultsChests(Reader(moved), Anywhere(VillageFieldId)).Select(c => (c.X, c.Y, c.Z)).Single(),
            "results chest: it is wherever its model is, read live");
    }

    /// <summary>
    /// The game names the prize only when box1's Talk pays it. Before that the chest is the
    /// excavation's chest and nothing more: not the Key to Sector 5 that results 5..7 hold after
    /// game moment 1195, nor anything else, and nothing else in the village names the key either.
    /// </summary>
    private static void TheChestNeverNamesWhatIsInside()
    {
        foreach (var result in Enumerable.Range(1, 9))
        {
            foreach (var moment in new[] { 641, 1195, 1196, 1620 })
            {
                var label = $"results chest, result {result}, moment {moment}";
                var targets = Reader(DigSpotMemory.InVillage(result, moment)).ReadTargets(Anywhere(VillageFieldId));
                var chest = targets.Single(target => target.TriggerEntityId == ResultsChestEntityId);
                Equal(true, chest.Label.StartsWith("Excavation", StringComparison.Ordinal) &&
                            chest.Label.Contains("chest", StringComparison.OrdinalIgnoreCase),
                    $"{label}: named as the excavation's chest ({chest.Label})");
                Equal(null, PrizeIn(chest.Label), $"{label}: the name gives away nothing it holds ({chest.Label})");
            }
        }

        foreach (var result in new[] { 5, 6, 7 })
        {
            foreach (var moment in new[] { 1196, 1620 })
            {
                var targets = Reader(DigSpotMemory.InVillage(result, moment)).ReadTargets(Anywhere(VillageFieldId));
                Equal(false, targets.Any(target => target.Label.Contains("Key to Sector 5", StringComparison.OrdinalIgnoreCase) ||
                                                   target.Label.Contains("Sector 5", StringComparison.OrdinalIgnoreCase)),
                    $"results chest, result {result}, moment {moment}: the key it holds is not named before box1's Talk gives it");
            }
        }
    }

    /// <summary>
    /// Nothing waiting is nothing offered: before any dig, and once the Talk has paid the result and
    /// cleared it. A chest that cannot be read or cannot be talked to is not offered either.
    /// </summary>
    private static void AnEmptyOrUnreadableChestIsNotOffered()
    {
        static DigSpotMemory With(Action<DigSpotMemory> change)
        {
            var waiting = DigSpotMemory.InVillage(5);
            change(waiting);
            return waiting;
        }

        var collected = DigSpotMemory.InVillage(0);
        collected.SetBank(15, 38, 0x08);
        collected.SetBank(1, 67, 0x20);
        var cases = new (string Label, DigSpotMemory State)[]
        {
            ("no dig has left anything (Bank[1][234] 0)", DigSpotMemory.InVillage(0, 641)),
            ("the key taken: the Talk cleared the result and set its flags", collected),
            ("box1 has no model", With(m => m.Unmap(ResultsChestEntityId))),
            ("box1's model is past the model count", With(m => m.MapTo(ResultsChestEntityId, 40))),
            ("the event table is missing", With(m => m.SetInt(FieldNavigationObjectReader.AddressFieldEventDataPtr, 0))),
            ("box1 is not drawn", With(m => m.ModelByte(ResultsChestModelId, FieldNavigationObjectReader.VisibilityOffset, 0))),
            ("box1's Talk is switched off", With(m => m.ModelByte(ResultsChestModelId, FieldNavigationNpcReader.TalkDisabledOffset, 1)))
        };
        foreach (var (label, state) in cases)
        {
            Equal(0, ResultsChests(Reader(state), Anywhere(VillageFieldId)).Length, $"results chest: {label}");
        }
    }

    /// <summary>
    /// The installed bonevil and bonevil2 bytes the cases above rest on: box1's placement and
    /// model, no TALKR, TLKON or VISI in its Init, its pose and its Talk gated on Bank[1][234], the
    /// Key to Sector 5's gates and flags, the result cleared afterwards, and the final choice's
    /// MAPJUMP back to bonevil.
    /// </summary>
    private static void ResultsChestIsTheNativeBox(string dataRoot)
    {
        var catalog = new FieldScriptNavigationCatalog(dataRoot);
        ResultsChestScripts(catalog, "installed");
        var keyc = catalog.ReadScriptOpcodes(772, 2, 3).Select(op => Hex(op.Bytes)).ToArray();
        Equal(true, keyc.Contains("60 69 02 32 ff 5f ff 40 00 00"),
            "results chest: the final choice returns the party to bonevil at (-206, -161) on triangle 64");
    }

    private static void ResultsChestScripts(FieldScriptNavigationCatalog catalog, string layout)
    {
        var init = catalog.ReadScriptOpcodes(VillageFieldId, ResultsChestEntityId, 0);
        Equal("box1", init.FirstOrDefault().EntityName, $"results chest, {layout}: entity 13 is box1");
        Equal("a1 0a", Hex(init.First(op => op.Opcode == 0xA1).Bytes), $"results chest, {layout}: box1 is CHAR 10");
        var place = init.First(op => op.Opcode == 0xA5).Bytes.ToArray();
        Equal((ChestPlacement.X, ChestPlacement.Y, ChestPlacement.Z, ChestPlacement.Triangle),
            ((int)BitConverter.ToInt16(place, 3), (int)BitConverter.ToInt16(place, 5), (int)BitConverter.ToInt16(place, 7),
                (int)BitConverter.ToUInt16(place, 9)),
            $"results chest, {layout}: box1's Init placement");
        Equal(false, init.Any(op => op.Opcode is 0xC5 or 0x7E or 0xA4),
            $"results chest, {layout}: no TALKR, TLKON or VISI, so the default talk radius, talkable and drawn");
        var main = init.Select(op => Hex(op.Bytes)).ToArray();
        foreach (var expected in new[] { "14 10 ea 00 00 08", "bc 00 1d 1d 01", "28 05 0d 00 00", "28 04 0d 01", "bc 00 00 00 01" })
        {
            Equal(true, main.Contains(expected), $"results chest, {layout}: box1's pose follows Bank[1][234]: {expected}");
        }

        var talk = catalog.ReadScriptOpcodes(VillageFieldId, ResultsChestEntityId, 1).Select(op => Hex(op.Bytes)).ToArray();
        Equal(true, talk.Length > 0 && talk[0].StartsWith("15 10 ea 00 01", StringComparison.Ordinal),
            $"results chest, {layout}: the Talk runs only while Bank[1][234] is not 0 ({talk.FirstOrDefault()})");
        foreach (var gate in new[] { "14 10 ea 05 00", "14 10 ea 06 00", "14 10 ea 07 00", "16 20 00 00 ab 04 02", "14 f0 26 03 0a" })
        {
            Equal(true, talk.Any(op => op.StartsWith(gate, StringComparison.Ordinal)), $"results chest, {layout}: the key's gate {gate}");
        }

        foreach (var write in new[] { "82 10 43 05", "82 f0 26 03", "80 10 ea 00" })
        {
            Equal(true, talk.Contains(write), $"results chest, {layout}: the Talk writes {write}");
        }
    }

    private static string[] EchoSRoots() =>
        (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static FieldScriptNavigationCatalog LoadedEchoSVillage(string dataRoot, string echo)
    {
        var memory = new LoadedFieldMemory();
        var loaded = new FieldScriptNavigationCatalog(dataRoot, Ff7GameLanguageDetector.Detect(dataRoot), new LoadedFieldScriptIdentityReader(memory));
        memory.Load(new FlevelDataSource(echo), VillageFieldId);
        var field = loaded.ReadField(VillageFieldId);
        Equal(true, field.IsUsable && field.UsesReviewedEchoLayout,
            $"results chest, {Path.GetFileName(echo)}: bonevil is the reviewed Echo-S layout ({field.Diagnostic})");
        return loaded;
    }

    /// <summary>
    /// Every Echo-S layout keeps box1 as entity 13 with the same placement, pose, Talk gate, key
    /// gates and writes (only its jumps, dialogue and junk odds move), and the loaded layout maps
    /// each results chest definition onto it.
    /// </summary>
    private static void ResultsChestIsBox1InEveryEchoS(string dataRoot)
    {
        foreach (var echo in EchoSRoots())
        {
            var name = Path.GetFileName(echo);
            ResultsChestScripts(new FieldScriptNavigationCatalog(echo), name);
            var loaded = LoadedEchoSVillage(dataRoot, echo);
            foreach (var definition in FieldNavigationObjectCatalog.CreateAllFields()
                         .Where(d => d.FieldId == VillageFieldId && d.EntityId == ResultsChestEntityId))
            {
                Equal(ResultsChestEntityId, loaded.RemapObjectDefinition(definition)?.EntityId,
                    $"results chest, {name}: the chest for result {definition.RequiredValue} stays box1");
            }
        }
    }

    /// <summary>The chest an Echo-S player is offered is the same Talk, chest and name.</summary>
    private static void EchoSOffersTheSameResultsChest(string dataRoot)
    {
        foreach (var echo in EchoSRoots())
        {
            var name = Path.GetFileName(echo);
            var loaded = LoadedEchoSVillage(dataRoot, echo);
            var state = DigSpotMemory.InVillage(6);
            var reader = new FieldNavigationObjectReader(state.ReadInt32, state.ReadByte, _ => null, _ => null,
                FieldNavigationObjectCatalog.CreateAllFields(), isLineEnabled: _ => true, remapDefinition: loaded.RemapObjectDefinition);
            var chests = ResultsChests(reader, Anywhere(VillageFieldId));
            Equal(1, chests.Length, $"results chest, {name}: one chest");
            Equal(FieldNavigationActivation.Talk, chests[0].Activation, $"results chest, {name}: reached as a Talk");
            Equal(NativeChestTalkReach, chests[0].InteractionRadius, $"results chest, {name}: at the native talk reach");
            Equal(FieldObjectCueKind.Chest, chests[0].ObjectCueKind, $"results chest, {name}: sounding as a chest");
            Equal(null, PrizeIn(chests[0].Label), $"results chest, {name}: named without its prize ({chests[0].Label})");
        }
    }

    /// <summary>
    /// Back from the dig the party stands where the MAPJUMP puts it, (-206, -161) on triangle 64,
    /// on the installed bonevil walkmesh. The route to the chest ends inside box1's native talk
    /// reach, and with auto walk driving the chest is reached only facing it: with its back to the
    /// chest the leader is turned to it first, because Confirm would not open it from there.
    /// </summary>
    private static void ResultsChestIsOpenedFromTheDigReturn(Func<int, FieldWalkmeshReader> createWalkmeshReader, string dataRoot)
    {
        var catalog = new FieldScriptNavigationCatalog(dataRoot);
        var place = catalog.ReadScriptOpcodes(VillageFieldId, ResultsChestEntityId, 0).First(op => op.Opcode == 0xA5).Bytes.ToArray();
        var memory = DigSpotMemory.InVillage(5);
        memory.PlaceBuried(ResultsChestEntityId, BitConverter.ToInt16(place, 3), BitConverter.ToInt16(place, 5),
            BitConverter.ToInt16(place, 7), BitConverter.ToUInt16(place, 9));
        var reader = Reader(memory);
        var mesh = createWalkmeshReader(VillageFieldId);
        var walkmesh = mesh.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, VillageFieldId, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var transitions = catalog.ReadField(VillageFieldId).Transitions;
        var planner = new FieldWalkmeshRoutePlanner(mesh, null, _ => transitions);
        var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, VillageFieldId, 0, DigReturn.X, DigReturn.Y,
            FieldCrossFieldApproachResolver.SurfaceZ(walkmesh.Triangles[DigReturn.Triangle], DigReturn.X, DigReturn.Y), DigReturn.Triangle, 0);
        var chest = ResultsChests(reader, start).Single();
        Equal(true, planner.TryBuildRoute(start, chest, out var plan), $"results chest: a route from the dig's return ({planner.LastDiagnostic})");
        var approach = plan.FinalApproach;
        var distance = Math.Sqrt(Math.Pow(approach.X - chest.X, 2) + Math.Pow(approach.Y - chest.Y, 2));
        Equal(true, distance < NativeChestTalkReach, $"results chest: the route ends inside box1's native talk reach ({distance:0.0})");
        Equal(true, Math.Abs(chest.Z - approach.Z) < 256, "results chest: at a height the talk test accepts");

        // A nearby point in the final triangle gives the native facing test a bearing.
        // The unobstructed route can end at the chest's identical XY, where none exists.
        var center = walkmesh.Triangles[plan.TargetTriangle].GetCentroid();
        var facingX = (int)Math.Round((approach.X + center.X) / 2);
        var facingY = (int)Math.Round((approach.Y + center.Y) / 2);
        var facingZ = FieldCrossFieldApproachResolver.SurfaceZ(walkmesh.Triangles[plan.TargetTriangle], facingX, facingY);
        Equal(true, Math.Pow(facingX - chest.X, 2) + Math.Pow(facingY - chest.Y, 2) is > 0 and < NativeChestTalkReach * NativeChestTalkReach,
            "results chest: the facing fixture has a nonzero bearing inside native Talk reach");
        var towards = (byte)((int)Math.Round(Math.Atan2(chest.X - facingX, -(double)(chest.Y - facingY)) * 128d / Math.PI) & 255);
        var facing = new FieldPositionSnapshot(FieldPositionReader.FieldModule, VillageFieldId, 0, facingX, facingY, facingZ,
            (ushort)plan.TargetTriangle, towards);
        // Native directional input updates facing even when a wall refuses translation.
        var away = facing with { Direction = (byte)(towards + 128) };
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], objectTargetProvider: position => ResultsChests(reader, position)), planner);
        var transform = new FieldNavigationControlTransform(0);
        for (var index = 0; index < 4 && controller.CurrentCategory != FieldNavigationCategory.Objects; index++)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        }

        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Equal(true, controller.BeaconEnabled, $"results chest: navigation starts ({controller.LastNavigationDiagnostic})");
        controller.NoteAutoWalkStarted();
        _ = controller.UpdateLiveTracking(start, new(0, FieldNavigationInput.None), transform, false, 80, observedAt: Epoch);
        Equal(true, controller.TryResolveAutomaticInput(start, transform, 80, out var walk) && walk != FieldNavigationInput.None,
            $"results chest: auto walk sets off ({controller.LastNavigationDiagnostic})");
        var turned = controller.UpdateLiveTracking(away, new(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: Epoch.AddSeconds(1));
        Equal(false, turned?.Speech.Contains("reached", StringComparison.Ordinal) == true,
            $"results chest: with its back to the chest it is not reached, Confirm would not open it ({turned?.Speech})");
        Equal(true, controller.TryResolveAutomaticInput(away, transform, 80, out var turn) && turn != FieldNavigationInput.None,
            $"results chest: auto walk turns the leader to the chest ({controller.LastNavigationDiagnostic})");
        var reached = controller.UpdateLiveTracking(facing, new(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: Epoch.AddSeconds(1).AddMilliseconds(100));
        Equal(true, reached?.Speech.StartsWith($"{chest.Label} reached", StringComparison.Ordinal) == true,
            $"results chest: facing it, the chest is reached for the player's own Confirm ({reached?.Speech})");
    }

    // The Objects list with only the dig spot left in it, so it is the one selected; the ladders
    // and diggers beside it are covered by ChaseAndExcavationTests.
    private static IReadOnlyList<FieldNavigationTarget> DigSpotsOnly(FieldNavigationObjectReader reader, FieldPositionSnapshot position) =>
        reader.ReadTargets(position).Where(t => t.Label.StartsWith("Dig spot", StringComparison.Ordinal)).ToArray();

    private static string Hex(IReadOnlyList<byte> bytes) => string.Join(" ", bytes.Select(b => b.ToString("x2")));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Bone Village dig spot: {label}: expected {expected}, got {actual}.");
        }
    }

    /// <summary>
    /// The engine memory the dig spot is read from: the event table with every entity's model,
    /// the temporary bank, and the savemap banks. <see cref="AfterInit"/> is bonevil2 once every
    /// Init has run: each buried model placed as its script places it, its triangle stored.
    /// </summary>
    private sealed class DigSpotMemory
    {
        private const int EventTable = 0x02600000;
        private const int ModelCount = 20;
        private readonly Dictionary<int, int> ints = [];
        private readonly Dictionary<int, byte> bytes = [];
        private readonly Dictionary<int, (int After, byte Value, int Reads)> changes = [];

        public static DigSpotMemory AfterInit(int request, int phase)
        {
            var memory = new DigSpotMemory();
            foreach (var site in BoneVillageDigSpot.Sites)
            {
                memory.PlaceBuried(site.EntityId, site.X, site.Y, site.Z, site.Triangle);
                memory.SetTempWord(site.BankSlot, (ushort)site.Triangle);
            }

            memory.SetTemp(12, (byte)phase);
            memory.SetBank(1, 235, (byte)request);
            memory.SetMoment(1000);
            return memory;
        }

        /// <summary>
        /// bonevil once a dig's result is waiting in Bank[1][234]: box1 (entity 13, its Init's CHAR
        /// 10) placed where its Init puts it, drawn and talkable at the default talk radius, and
        /// Cloud leading at bonevil's width. The rest of the table is the dig's, which bonevil's
        /// object definitions never look at.
        /// </summary>
        public static DigSpotMemory InVillage(int result, int moment = 1620)
        {
            var memory = new DigSpotMemory();
            memory.ModelWord(0, FieldNavigationNpcReader.CollisionRadiusOffset, LeaderWidth);
            memory.MapTo(ResultsChestEntityId, ResultsChestModelId);
            memory.ModelByte(ResultsChestModelId, FieldNavigationObjectReader.VisibilityOffset, 1);
            memory.ModelByte(ResultsChestModelId, FieldNavigationNpcReader.TalkDisabledOffset, 0);
            memory.ModelWord(ResultsChestModelId, FieldNavigationNpcReader.TalkRadiusOffset, ChestTalkRadius);
            memory.PlaceBuried(ResultsChestEntityId, ChestPlacement.X, ChestPlacement.Y, ChestPlacement.Z, ChestPlacement.Triangle);
            memory.SetBank(1, ResultIndex, (byte)result);
            memory.SetMoment(moment);
            return memory;
        }

        public void ModelByte(int model, int offset, byte value) =>
            bytes[EventTable + model * FieldNavigationObjectReader.FieldEventDataStride + offset] = value;

        public void ModelWord(int model, int offset, int value)
        {
            ModelByte(model, offset, (byte)value);
            ModelByte(model, offset + 1, (byte)(value >> 8));
        }

        private DigSpotMemory()
        {
            ints[FieldNavigationObjectReader.AddressFieldEventDataPtr] = EventTable;
            bytes[FieldPositionReader.AddressFieldNumModels] = ModelCount;
            for (var entity = 0; entity < 19; entity++)
            {
                MapTo(entity, entity);
            }

            bytes[EventTable + FieldNavigationNpcReader.CollisionRadiusOffset] = 30;
            for (var entity = 14; entity <= 18; entity++)
            {
                // The diggers wait, visible, at their Init spot.
                bytes[Event(entity) + FieldNavigationObjectReader.VisibilityOffset] = 1;
                Position(entity, -427, -5, -11);
            }
        }

        public void MapTo(int entity, int model) => bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = (byte)model;

        public void Unmap(int entity) => MapTo(entity, 0xFF);

        public void PlaceBuried(int entity, int x, int y, int z, int triangle)
        {
            Position(entity, x, y, z);
            var address = Event(entity) + FieldPositionReader.ObjectTriangleOffset;
            bytes[address] = (byte)triangle;
            bytes[address + 1] = (byte)(triangle >> 8);
        }

        public void SetTemp(int index, byte value) => bytes[FieldNavigationObjectReader.AddressTemporaryFieldBankBase + index] = value;

        public void SetTempWord(int index, ushort value)
        {
            SetTemp(index, (byte)value);
            SetTemp(index + 1, (byte)(value >> 8));
        }

        public void SetBank(int bank, int index, byte value)
        {
            var offset = bank switch { 1 => 0, 13 => 0x300, 15 => 0x400, _ => throw new ArgumentOutOfRangeException(nameof(bank)) };
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + offset + index] = value;
        }

        public void SetMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        public void OwnField(int field)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = FieldPositionReader.FieldModule;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
        }

        public void SetInt(int address, int value) => ints[address] = value;

        /// <summary>After <paramref name="reads"/> reads of the byte, it reads as <paramref name="value"/>.</summary>
        public void ChangeAfterReads(int address, int reads, byte value) => changes[address] = (reads, value, 0);

        public int ReadInt32(int address) => ints.TryGetValue(address, out var value) ? value : 0;

        public byte ReadByte(int address)
        {
            if (changes.TryGetValue(address, out var change))
            {
                changes[address] = change with { Reads = change.Reads + 1 };
                if (change.Reads >= change.After)
                {
                    return change.Value;
                }
            }

            return bytes.TryGetValue(address, out var value) ? value : (byte)0;
        }

        private int Event(int entity) => EventTable + bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] * FieldNavigationObjectReader.FieldEventDataStride;

        private void Position(int entity, int x, int y, int z)
        {
            var address = Event(entity);
            ints[address + FieldNavigationObjectReader.PositionXOffset] = x * 4096;
            ints[address + FieldNavigationObjectReader.PositionYOffset] = y * 4096;
            ints[address + FieldNavigationObjectReader.PositionZOffset] = z * 4096;
        }
    }

    /// <summary>
    /// A field's script section placed where the loaded-script identity reader looks for it, as
    /// the game has it loaded, so the catalog judges an Echo-S layout from its own bytes.
    /// </summary>
    private sealed class LoadedFieldMemory : ILegacyAddressSpace
    {
        private const uint Script = 0x02000000;
        private readonly Dictionary<uint, byte> bytes = [];
        private byte[] section = [];

        public void Load(FlevelDataSource source, int field)
        {
            Equal(true, source.TryReadField(field, out var encoded), $"field {field} is installed");
            var decoded = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var at = BitConverter.ToInt32(decoded, 6);
            var length = BitConverter.ToInt32(decoded, at);
            bytes.Clear();
            section = decoded.AsSpan(at + 4, length).ToArray();
            Write(Script, section);
            bytes[(uint)FieldScriptContextReader.AddressCurrentModule] = 1;
            Write((uint)FieldScriptContextReader.AddressCurrentFieldId, BitConverter.GetBytes((ushort)field));
            Write((uint)FieldScriptContextReader.AddressFieldScriptPtr, BitConverter.GetBytes(Script));
        }

        public void ShowNativeMessage(int messageId)
        {
            var tableOffset = BitConverter.ToUInt16(section, 4);
            var textOffset = tableOffset + BitConverter.ToUInt16(section, tableOffset + 2 + 2 * messageId);
            var message = section.AsSpan(textOffset);
            var terminator = message.IndexOf((byte)0xff);
            Equal(true, terminator > 0, "native clue message is terminated");
            var pageBreak = message[..terminator].IndexOf((byte)0xe8);
            var window = new byte[FieldMessageReader.FieldTextBufferLength];
            Array.Fill(window, (byte)0xff);
            message[..(pageBreak < 0 ? terminator : pageBreak)].CopyTo(window);
            Write(FieldMessageReader.AddressFieldWindowTextBuffers, window);
            Write(FieldMessageReader.AddressFieldMessageDataPointer, BitConverter.GetBytes(Script + tableOffset));
            for (var index = 0; index < FieldMessageReader.WindowCount; index++)
            {
                bytes[(uint)FieldMessageReader.AddressFieldWindowStates + (uint)index] =
                    index == 0 ? (byte)1 : FieldMessageReader.FreeWindowState;
                Write((uint)(FieldMessageReader.AddressFieldWindowMessagePointers + index * 4),
                    BitConverter.GetBytes(index == 0 ? Script + (uint)textOffset : 0u));
            }
            // The native reader bounds its scan within a readable allocated segment.
            for (var index = section.Length; index < textOffset + 0xe00; index++)
                bytes[Script + (uint)index] = 0xff;
        }

        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(address + (uint)index, out destination[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private void Write(uint address, ReadOnlySpan<byte> data)
        {
            for (var index = 0; index < data.Length; index++)
            {
                bytes[address + (uint)index] = data[index];
            }
        }
    }
}
