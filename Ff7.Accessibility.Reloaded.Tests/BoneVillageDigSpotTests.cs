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
        if (dataRoot is null || createWalkmeshReader is null)
        {
            return;
        }

        DigSpotIsTheNativeScript(dataRoot);
        NearButWrongTriangleKeepsWalking(createWalkmeshReader, dataRoot);
        EverySpotRoutesFromBothLevelsThroughTheLadder(createWalkmeshReader, dataRoot);
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
}
