using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using System.Buffers.Binary;

internal static class CondorBattleReaderTests
{
    internal static void Run()
    {
        ReadsBothSidesOfTheLiveUnitArray();
        FailsClosedWhenAnyPartOfTheStateIsUnreadable();
        TreatsAUnitOutOfHpAsDyingRatherThanGone();
        ResolvesTheHighlightedHireRowThroughTheListRotation();
        AnnouncesTheBannerMessagesTheGameDrawsAsPictures();
        SaysWhatTheEndingBannersMean();
        SpeaksTheResultFromTheGamesOwnLatch();
        SpeaksUnitsGoingDownDuringTheFight();
        DoesNotReportAPhaseChangeAsCasualties();
        AnchorsThePlacementScanToTheCursorRow();
        NamesTheEnemyTypesTheGameDraws();
        ReportsTheAdvanceGaugeTheGameDraws();
        SkipsARemovingUnitWhenDecidingWhatTheCursorIsOn();
        SpeaksTheHireListWithAffordability();
        SpeaksTheUnitUnderTheCursorAndWhenItClears();
        DoesNotNarrateMovementAcrossOpenGround();
        ReproducesTheNativePlacementRegionFromTheShippedTerrain();
        AppliesTheSetupBoundaryAndTheCombatFrontier();
        ExistingUnitsCutHolesInAPlacementBand();
        SpeaksThePlacementBandRatherThanOneRow();
        StatusAnswersWhatASightedPlayerSeesAtAGlance();
        NamesOnlyUnitTypesThatHaveBeenProved();
    }

    private static void ReadsBothSidesOfTheLiveUnitArray()
    {
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 2, currentHp: 180, maximumHp: 180, attack: 25, x: 240, y: 500);
        memory.WriteUnit(slot: 20, typeId: 10, currentHp: 60, maximumHp: 200, attack: 30, x: 240, y: 900);
        memory.WriteInt32(CondorMemory.AlliedCount, 1);
        memory.WriteInt32(CondorMemory.EnemyCount, 1);

        var snapshot = new CondorBattleStateReader(memory).TryRead();
        AssertNotNull(snapshot, "snapshot from a readable battle");
        Equal(2, snapshot!.Units.Count, "live unit count");

        var allied = snapshot.Units[0];
        Equal(false, allied.IsEnemy, "slot 0 side");
        Equal("Attacker", allied.Name, "slot 0 name");
        Equal("Attacker, 180 of 180", allied.Describe(), "slot 0 description");

        // Slots 20 and up are the enemy's. The split is the array's own, not a
        // flag inside the record, so it is worth pinning.
        var enemy = snapshot.Units[1];
        Equal(true, enemy.IsEnemy, "slot 20 side");
        Equal(20, enemy.Slot, "slot 20 index");
        Equal(60, enemy.CurrentHp, "slot 20 current HP");
    }

    private static void FailsClosedWhenAnyPartOfTheStateIsUnreadable()
    {
        // A missing read must not become a zero. Reporting a healthy unit as dead,
        // or an occupied square as free, is worse than reporting nothing, because
        // the player has no way to check it against the screen.
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 1, currentHp: 200, maximumHp: 200, attack: 30, x: 100, y: 100);
        memory.Unreadable.Add(CondorMemory.Gil);

        AssertNull(new CondorBattleStateReader(memory).TryRead(), "snapshot with unreadable funds");

        var tornUnits = new CondorMemory();
        tornUnits.Unreadable.Add(CondorMemory.LiveUnits + (17 * CondorMemory.UnitStride));
        AssertNull(new CondorBattleStateReader(tornUnits).TryRead(), "snapshot with an unreadable unit slot");
    }

    private static void TreatsAUnitOutOfHpAsDyingRatherThanGone()
    {
        // The allocated flag is cleared several frames after death, so a reader
        // that trusted it alone would keep a corpse in the list and announce it
        // as a live obstacle.
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 21, typeId: 10, currentHp: 0, maximumHp: 200, attack: 30, x: 300, y: 700);
        memory.WriteUnit(slot: 22, typeId: 10, currentHp: 40, maximumHp: 200, attack: 30, x: 320, y: 700, removalState: -1);
        memory.WriteUnit(slot: 23, typeId: 10, currentHp: 40, maximumHp: 200, attack: 30, x: 340, y: 700);

        var snapshot = new CondorBattleStateReader(memory).TryRead();
        AssertNotNull(snapshot, "snapshot with dying units");
        Equal(true, snapshot!.Units[0].IsDying, "unit at zero HP is dying");
        Equal(true, snapshot.Units[1].IsDying, "unit in its removal animation is dying");
        Equal(false, snapshot.Units[2].IsDying, "healthy unit is not dying");

        // The nearest enemy is what the player is judging placement against, so a
        // dying one must not be offered as the answer.
        Equal(23, snapshot.NearestEnemy!.Slot, "nearest living enemy skips the dying ones");
    }

    private static void ResolvesTheHighlightedHireRowThroughTheListRotation()
    {
        // The eight-entry list the game builds for the first tier, read back
        // through the reader so the count and the array are covered too. It is the
        // list a live battle produced on 2026-08-21.
        var ids = new[] { 1, 2, 3, 4, 12, 13, 5, 7 };
        var memory = new CondorMemory();
        memory.WriteTypeIds(ids);
        memory.WriteInt32(CondorMemory.ModalState, CondorBattleSnapshot.SettingMenuModalState);
        var reader = new CondorBattleStateReader(memory);

        var snapshot = reader.TryRead();
        AssertNotNull(snapshot, "snapshot with a built hire list");
        Equal(8, snapshot!.AvailableTypeIds.Count, "available unit count");
        Equal(1, snapshot.HighlightedTypeId, "highlighted id at the first row");

        // The row is relative to a window that rotates over the available list, so
        // the row alone names the wrong unit as soon as the list has scrolled.
        memory.WriteInt16(CondorMemory.SettingMenuRow, 2);
        Equal(3, reader.TryRead()!.HighlightedTypeId, "highlighted id with no rotation");

        memory.WriteInt16(CondorMemory.SettingMenuRotation, 2);
        Equal(12, reader.TryRead()!.HighlightedTypeId, "highlighted id with rotation");

        memory.WriteInt16(CondorMemory.SettingMenuRow, 1);
        memory.WriteInt16(CondorMemory.SettingMenuRotation, 7);
        Equal(1, reader.TryRead()!.HighlightedTypeId, "highlighted id wrapping past the end");

        // Outside the hire screen the count is whatever the last build left, so an
        // unbuilt list has to read as no list rather than as row zero of stale bytes.
        memory.WriteInt16(CondorMemory.SettingMenuCount, 0);
        AssertNull(reader.TryRead()!.HighlightedTypeId, "highlighted id with no list built");

        memory.WriteInt16(CondorMemory.SettingMenuCount, 99);
        Equal(0, reader.TryRead()!.AvailableTypeIds.Count, "available list with an impossible count");
    }

    private static void AnnouncesTheBannerMessagesTheGameDrawsAsPictures()
    {
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Battle(messageId: 12));

        Equal(
            "Encountered enemy.",
            Single(tracker.Observe(Battle(messageId: 0))),
            "banner message on change");

        // The same identifier still standing is the same picture still on screen.
        Equal(0, tracker.Observe(Battle(messageId: 0)).Count, "banner message repeated");
        Equal("Enemy destroyed.", Single(tracker.Observe(Battle(messageId: 10))), "later banner message");
    }

    private static void SaysWhatTheEndingBannersMean()
    {
        // The banner is a caption, not a result. A player fought a whole battle
        // on 2026-08-21, heard "Enemy invasion.", and still had to ask whether
        // they had won - so the game's own words are kept and what they mean is
        // said with them.
        var won = new CondorBattleSpeechTracker();
        won.Observe(Battle());
        Equal(
            "Halted enemy attack! Battle won.",
            Single(won.Observe(Battle(messageId: 2))),
            "victory said with what it means");

        var lost = new CondorBattleSpeechTracker();
        lost.Observe(Battle());
        Equal(
            "Enemy invasion. They reached the fort. Battle lost.",
            Single(lost.Observe(Battle(messageId: 7))),
            "defeat said with what it means");

        // A banner returning to the same identifier is the same picture back on
        // screen, not a second defeat.
        lost.Observe(Battle(messageId: 0));
        Equal(0, lost.Observe(Battle(messageId: 7)).Count, "the result is announced once");
    }

    private static void SpeaksTheResultFromTheGamesOwnLatch()
    {
        // 0x00CBEDC0 is the module's result latch and the game sets it before it
        // publishes the banner, so it is the earliest honest answer to "did I
        // win". One is the enemy stopped, two is the enemy reaching the fort.
        var logged = new List<string>();
        var lost = new CondorBattleSpeechTracker(logged.Add);
        lost.Observe(Battle());
        Equal(
            "Enemy invasion. They reached the fort. Battle lost.",
            Single(lost.Observe(Battle(outcome: 2))),
            "defeat taken from the latch");

        // The banner is published from that same latch, so hearing it twice
        // would be the game saying it once and the mod saying it again.
        Equal(
            0,
            lost.Observe(Battle(outcome: 2, messageId: 7)).Count,
            "the banner does not repeat the result");
        Equal(
            1,
            logged.Count(line => line.Contains("result latch set to 2")),
            "the latch is written down as well as spoken");

        var won = new CondorBattleSpeechTracker();
        won.Observe(Battle());
        Equal(
            "Halted enemy attack! Battle won.",
            Single(won.Observe(Battle(outcome: 1))),
            "victory taken from the latch");
    }

    private static void SpeaksUnitsGoingDownDuringTheFight()
    {
        // Nothing in module 9 tells a blind player the fight is going badly. The
        // line thinning is what a sighted player is actually watching, and it is
        // the only warning there is before the enemy reaches the fort.
        var tracker = new CondorBattleSpeechTracker();
        var line = new[]
        {
            Unit(slot: 0, x: 200, y: 500),
            Unit(slot: 1, x: 240, y: 500),
            Unit(slot: 20, x: 200, y: 700, typeId: 17)
        };
        tracker.Observe(Battle(units: line, phase: 2));

        var afterLoss = tracker.Observe(Battle(units: [line[0], line[2]], phase: 2));
        Equal("Lost Attacker. 1 unit left.", Single(afterLoss), "an allied unit going down");

        // Named from the label the game draws for type 17, with the count the
        // banner never gives.
        var afterKill = tracker.Observe(Battle(units: [line[0]], phase: 2));
        Equal("Enemy Wyvern destroyed. 0 enemies left.", Single(afterKill), "an enemy going down");

        Equal(0, tracker.Observe(Battle(units: [line[0]], phase: 2)).Count, "a steady field");
    }

    private static void DoesNotReportAPhaseChangeAsCasualties()
    {
        var tracker = new CondorBattleSpeechTracker();
        var line = new[] { Unit(slot: 0, x: 200, y: 500), Unit(slot: 1, x: 240, y: 500) };
        tracker.Observe(Battle(units: line, phase: 1));

        // The live array is rebuilt when the battle changes phase. Reporting
        // that as two deaths would be a lie told loudly.
        Equal(
            0,
            tracker.Observe(Battle(units: [], phase: 2)).Count,
            "units cleared across a phase change");
    }

    private static void AnchorsThePlacementScanToTheCursorRow()
    {
        // In combat the cursor is not locked to the four-unit grid - it was
        // observed at 525, 761 and 937 in a real battle. A scan starting at zero
        // never lands on those rows, and every distance it reports is then off
        // by up to three, which is how "nearest placeable 7 down" reached a
        // player who can only move in fours.
        var terrain = LoadShippedCollisionTriangles();
        var odd = Battle(cursorX: 256, cursorY: 701, phase: 0, frontierY: 2000, terrain: terrain);
        var bands = odd.PlacementIntervals;

        Equal(true, bands.Count > 0, "the odd row is on terrain at all");
        Equal(true, bands.Any(band => band.Contains(701)), "the cursor's own row is scanned");
        foreach (var band in bands)
        {
            Equal(
                701 % CondorPlacementRegion.CursorStep,
                band.FromY % CondorPlacementRegion.CursorStep,
                "band start shares the cursor's row parity");
        }
    }

    private static void SpeaksTheHireListWithAffordability()
    {
        var tracker = new CondorBattleSpeechTracker();
        var ids = new[] { 1, 2, 3, 4, 12, 13, 5, 7 };
        tracker.Observe(Battle(ids, row: 0, modalState: 0, gil: 500));

        var opened = tracker.Observe(Battle(ids, row: 0, modalState: 7, gil: 500));
        Equal(2, opened.Count, "lines when the hire list opens");
        Equal("Setting menu. 500 gil.", opened[0], "hire list opening line");
        Equal(
            "Fighter. 400 gil. HP 200. Attack 30. Speed 224. Regular unit.",
            opened[1],
            "highlighted hire line");

        // The price is drawn against the funds counter, so a sighted player can
        // see what they cannot afford before pressing anything.
        Equal(
            "Shooter. 520 gil, not affordable. HP 160. Attack 20. Speed 212. " +
            "Can shoot from afar. Beats Wyvern. Loses to Beast.",
            Single(tracker.Observe(Battle(ids, row: 3, modalState: 7, gil: 500))),
            "unaffordable hire line");
    }

    private static void SpeaksTheUnitUnderTheCursorAndWhenItClears()
    {
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 3, currentHp: 140, maximumHp: 220, attack: 35, x: 200, y: 400);
        var reader = new CondorBattleStateReader(memory);
        var tracker = new CondorBattleSpeechTracker();

        memory.WriteUInt16(CondorMemory.CursorPlacementLegal, 1);
        memory.WriteInt16(CondorMemory.UnitUnderCursor, -1);
        tracker.Observe(reader.TryRead()!);

        memory.WriteInt16(CondorMemory.UnitUnderCursor, 0);
        Equal(
            "Defender, 140 of 220.",
            Single(tracker.Observe(reader.TryRead()!)),
            "unit under the cursor");

        // The native stat panel clears when the cursor leaves the unit, so the
        // ground under it is described instead. Leaving the last unit standing as
        // the player's picture of where they are would be worse than saying
        // nothing. The Defender at (200, 400) still denies the rows around
        // itself, so the band the cursor lands in stops short of it.
        memory.WriteInt16(CondorMemory.UnitUnderCursor, -1);
        AssertContains(Single(tracker.Observe(reader.TryRead()!)), "placeable");
    }

    private static void DoesNotNarrateMovementAcrossOpenGround()
    {
        // A sighted player crossing unbroken ground is shown nothing new. The
        // band under the cursor is the same band, so there is nothing to say and
        // a running commentary would bury the events that matter.
        var memory = new CondorMemory();
        var reader = new CondorBattleStateReader(memory);
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(reader.TryRead()!);

        foreach (var y in new[] { 100, 160, 240, 320 })
        {
            memory.WriteInt16(CondorMemory.CursorY, (short)y);
            Equal(0, tracker.Observe(reader.TryRead()!).Count, $"cursor moved to row {y}");
        }

        // Ground the frontier will not allow is a real change, and is said.
        memory.WriteInt32(CondorMemory.DeploymentFrontierY, 300);
        memory.WriteInt16(CondorMemory.CursorY, 400);
        AssertContains(Single(tracker.Observe(reader.TryRead()!)), "blocked");

        // And coming back inside it is said too.
        memory.WriteInt16(CondorMemory.CursorY, 200);
        AssertContains(Single(tracker.Observe(reader.TryRead()!)), "placeable");
    }

    private static void ReproducesTheNativePlacementRegionFromTheShippedTerrain()
    {
        // The real collision mesh, read out of the installed condor.lgp, checked
        // against the legal-row intervals the disassembly published for four
        // cursor columns.
        //
        // The game decides membership with fixed-point wedge angles and an
        // eight-unit tolerance out of a 4096-unit turn; this reproduces it with
        // an exact integer cross-product instead. These four columns prove the
        // substitution is sound on this mesh, edges included - and they prove the
        // region has holes, so a single minimum and maximum would be false.
        var terrain = LoadShippedCollisionTriangles();
        Equal(333, terrain.Count, "collision triangle count");

        var expected = new Dictionary<int, (int From, int To)[]>
        {
            [128] = [(484, 544), (652, 732), (792, 904)],
            [256] = [(420, 1008)],
            [260] = [(420, 476), (552, 1008)],
            [320] = [(424, 460), (568, 716), (888, 1008)]
        };

        foreach (var (cursorX, bands) in expected)
        {
            // Combat phase with the frontier past the bottom of the map, so the
            // terrain is the only thing constraining the answer.
            var snapshot = Battle(cursorX: cursorX, phase: 0, frontierY: 2000, terrain: terrain);
            var actual = snapshot.PlacementIntervals;

            Equal(bands.Length, actual.Count, $"placement band count at X {cursorX}");
            for (var index = 0; index < bands.Length; index++)
            {
                Equal(bands[index].From, actual[index].FromY, $"band {index} start at X {cursorX}");
                Equal(bands[index].To, actual[index].ToY, $"band {index} end at X {cursorX}");
            }
        }
    }

    private static void AppliesTheSetupBoundaryAndTheCombatFrontier()
    {
        var terrain = LoadShippedCollisionTriangles();

        // During setup the executable refuses anything below a fixed line. The
        // cursor moves in four-unit steps, so the lowest row a player can
        // actually reach under it is 668.
        var setup = Battle(cursorX: 260, phase: CondorPlacementRegion.SetupPhase, terrain: terrain);
        var setupBands = setup.PlacementIntervals;
        Equal(2, setupBands.Count, "setup band count at X 260");
        Equal(420, setupBands[0].FromY, "setup first band start");
        Equal(476, setupBands[0].ToY, "setup first band end");
        Equal(552, setupBands[1].FromY, "setup second band start");
        Equal(668, setupBands[1].ToY, "setup second band end");

        // Once the battle is running the limit becomes a frontier that starts at
        // 480 and moves down as the allied units advance, so the ground a player
        // may build on genuinely grows during a battle.
        var earlyCombat = Battle(cursorX: 256, phase: 0, frontierY: 480, terrain: terrain);
        Equal(476, earlyCombat.PlacementIntervals[^1].ToY, "combat limit at the opening frontier");

        var advanced = Battle(cursorX: 256, phase: 0, frontierY: 928, terrain: terrain);
        Equal(924, advanced.PlacementIntervals[^1].ToY, "combat limit at the furthest frontier");
    }

    private static void ExistingUnitsCutHolesInAPlacementBand()
    {
        // A unit denies more ground than the square it stands on, and the game
        // keeps that ground denied until the slot is released. Reporting a band
        // without its holes would send a player to spend gil somewhere the
        // confirm does nothing at all.
        var terrain = LoadShippedCollisionTriangles();
        var clear = Battle(cursorX: 256, phase: 0, frontierY: 2000, terrain: terrain);
        Equal(1, clear.PlacementIntervals.Count, "band count with an empty field");

        var occupied = Battle(
            cursorX: 256, phase: 0, frontierY: 2000, terrain: terrain,
            units: [Unit(slot: 0, x: 256, y: 700)]);
        Equal(true, occupied.PlacementIntervals.Count > 1, "a unit splits the band");
        Equal(
            false,
            occupied.PlacementIntervals.Any(interval => interval.Contains(700)),
            "the row the unit stands on is not offered");
    }

    private static void SpeaksThePlacementBandRatherThanOneRow()
    {
        var terrain = LoadShippedCollisionTriangles();
        var inBand = Battle(
            cursorX: 260, cursorY: 440,
            phase: CondorPlacementRegion.SetupPhase, terrain: terrain);

        // The cursor sits inside the first band, 420 to 476. A sighted player can
        // see how far that band runs; saying only "clear" would answer for the
        // single row under the cursor and leave the rest to be swept for by ear.
        Equal(
            "placeable 20 up and 36 down, 1 more band",
            CondorPlacementRegion.Describe(inBand.PlacementIntervals, inBand.CursorY),
            "placement description inside a band");

        // Y 500 is in the real gap between the two bands.
        var inGap = Battle(
            cursorX: 260, cursorY: 500,
            phase: CondorPlacementRegion.SetupPhase, terrain: terrain);
        Equal(
            "blocked, nearest placeable 24 up",
            CondorPlacementRegion.Describe(inGap.PlacementIntervals, inGap.CursorY),
            "placement description inside a gap");
    }

    private static IReadOnlyList<CondorCollisionTriangle> LoadShippedCollisionTriangles()
    {
        var archive = new LgpArchiveReader(
            Path.Combine(FindRuntimeRoot(), "data", "minigame", "condor.lgp"));
        if (!archive.TryReadFile("vert.bin", out var vertices))
        {
            throw new InvalidOperationException("condor.lgp does not contain vert.bin.");
        }

        const int stride = 0x4C;
        var triangles = new List<CondorCollisionTriangle>();
        for (var offset = 0; offset + stride <= vertices.Length; offset += stride)
        {
            var span = vertices.AsSpan(offset, stride);
            triangles.Add(new CondorCollisionTriangle(
                BitConverter.ToInt16(span[0x28..]), BitConverter.ToInt16(span[0x2A..]),
                BitConverter.ToInt16(span[0x30..]), BitConverter.ToInt16(span[0x32..]),
                BitConverter.ToInt16(span[0x38..]), BitConverter.ToInt16(span[0x3A..]),
                BitConverter.ToInt16(span[0x40..]) - 0x4000,
                BitConverter.ToInt16(span[0x42..]) - 0x4000,
                BitConverter.ToInt16(span[0x44..]) - 0x4000,
                BitConverter.ToInt16(span[0x46..]) - 0x4000));
        }

        return triangles;
    }

    private static string FindRuntimeRoot()
    {
        var configured = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_RUNTIME");
        if (!string.IsNullOrWhiteSpace(configured) &&
            Directory.Exists(Path.Combine(configured, "data", "minigame")))
        {
            return configured;
        }

        throw new InvalidOperationException(
            "FF7_ACCESSIBILITY_RUNTIME must name an FFVII runtime containing data/minigame.");
    }

    private static CondorBattleUnit Unit(
        int slot, int x, int y, int width = 22, int heightAbove = 26, int typeId = 2,
        bool removing = false) =>
        new(slot, slot >= 20, typeId, 100, 100, 20, x, y, false, width, heightAbove, removing);

    private static void NamesTheEnemyTypesTheGameDraws()
    {
        // Taken from the labels the executable itself draws - it picks name
        // region 0x5F + typeId out of emes01 - and not from guides, which put
        // Beast at 212 HP and Wyvern at 140. No record in the shipped archive
        // has either value, and the archive is what the game runs.
        var expected = new (int TypeId, string Name)[]
        {
            (16, "enemy Commander"),
            (17, "enemy Wyvern"),
            (18, "enemy Beast"),
            (19, "enemy Barbarian")
        };

        foreach (var (typeId, name) in expected)
        {
            Equal(name, Unit(slot: 20, x: 0, y: 0, typeId: typeId).Name, $"name of enemy type {typeId}");
        }

        // A type nobody has proved is still described by side alone.
        Equal("enemy unit", Unit(slot: 20, x: 0, y: 0, typeId: 21).Name, "an unproved enemy type");
    }

    private static void ReportsTheAdvanceGaugeTheGameDraws()
    {
        // The game derives this from the leading enemy's position and draws it
        // as a row of segments that is on screen all battle. It is the one thing
        // a sighted player can glance at to know they are losing.
        var tracker = new CondorBattleSpeechTracker();
        tracker.Observe(Battle(enemyAdvance: 0));

        Equal(0, tracker.Observe(Battle(enemyAdvance: 20)).Count, "still inside the first quarter");
        Equal(
            "Enemy advance a quarter.",
            Single(tracker.Observe(Battle(enemyAdvance: 24))),
            "the first quarter");
        Equal(
            "Enemy advance halfway.",
            Single(tracker.Observe(Battle(enemyAdvance: 50))),
            "halfway");
        Equal(
            "Enemies at the fort.",
            Single(tracker.Observe(Battle(enemyAdvance: 96))),
            "the gauge full");

        // Driving them back is worth hearing as much as losing ground is.
        Equal(
            "Enemy advance halfway.",
            Single(tracker.Observe(Battle(enemyAdvance: 48))),
            "pushed back down the gauge");
    }

    private static void SkipsARemovingUnitWhenDecidingWhatTheCursorIsOn()
    {
        // The game runs two scans over the live units and they disagree on
        // purpose. The footprint scan stops at slot 38 and counts units that are
        // playing their removal animation; the hit-box scan covers all forty and
        // skips them. Slot 39 is therefore the only place the difference shows,
        // and getting it wrong reports ground as blocked that the game accepts.
        var terrain = LoadShippedCollisionTriangles();

        var standing = Battle(
            cursorX: 256, cursorY: 700, phase: 0, frontierY: 2000, terrain: terrain,
            units: [Unit(slot: 39, x: 256, y: 700)]);
        Equal(
            false,
            CondorPlacementRegion.IsLegalAt(standing, 256, 700),
            "a live unit in slot 39 blocks the cursor");

        var removing = Battle(
            cursorX: 256, cursorY: 700, phase: 0, frontierY: 2000, terrain: terrain,
            units: [Unit(slot: 39, x: 256, y: 700, removing: true)]);
        Equal(
            true,
            CondorPlacementRegion.IsLegalAt(removing, 256, 700),
            "a unit in its removal animation in slot 39 does not");

        // Below slot 39 the footprint scan still counts it, exactly as the game
        // does, so the removal state changes nothing there.
        var lowSlot = Battle(
            cursorX: 256, cursorY: 700, phase: 0, frontierY: 2000, terrain: terrain,
            units: [Unit(slot: 5, x: 256, y: 700, removing: true)]);
        Equal(
            false,
            CondorPlacementRegion.IsLegalAt(lowSlot, 256, 700),
            "a removing unit below slot 39 still blocks");
    }

    private static void StatusAnswersWhatASightedPlayerSeesAtAGlance()
    {
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 0, typeId: 2, currentHp: 180, maximumHp: 180, attack: 25, x: 240, y: 500);
        memory.WriteUnit(slot: 20, typeId: 10, currentHp: 120, maximumHp: 200, attack: 30, x: 240, y: 620);
        memory.WriteInt32(CondorMemory.AlliedCount, 1);
        memory.WriteInt32(CondorMemory.EnemyCount, 4);
        memory.WriteInt32(CondorMemory.Gil, 9436);
        memory.WriteInt16(CondorMemory.CursorX, 240);
        memory.WriteInt16(CondorMemory.CursorY, 500);
        memory.WriteUInt16(CondorMemory.CursorPlacementLegal, 1);
        memory.WriteInt16(CondorMemory.UnitUnderCursor, -1);

        var reader = new CondorBattleStateReader(memory);
        var snapshot = reader.TryRead();
        AssertNotNull(snapshot, "snapshot for the status line");

        // Before the placement reading has held still, the status line leaves it
        // out rather than reporting whichever value the flag happened to be on.
        var tracker = new CondorBattleSpeechTracker();
        var unsettled = tracker.DescribeStatus(snapshot!);
        // The allied unit stands exactly where the cursor is, so the ground under
        // it is denied and the status says where the nearest usable row is
        // instead. That is the whole point of calculating the region rather than
        // answering only for the row the cursor is on.
        // The advance gauge closes the line because the game draws it for the
        // whole battle; a glance takes it in whether or not it just moved.
        Equal(
            "9436 gil. 1 unit. 4 enemies. blocked, nearest placeable 24 down. " +
            "nearest enemy unit, 120 of 200, 120 down. no enemy advance.",
            unsettled,
            "status line with the cursor on an occupied row");
    }

    private static void NamesOnlyUnitTypesThatHaveBeenProved()
    {
        // The ten hireable types are tied to their names through condor.lgp's
        // record table. The enemy roster is not, so it is described by side and
        // never given a guessed name.
        var memory = new CondorMemory();
        memory.WriteUnit(slot: 20, typeId: 10, currentHp: 200, maximumHp: 200, attack: 30, x: 100, y: 100);
        var snapshot = new CondorBattleStateReader(memory).TryRead();
        AssertNotNull(snapshot, "snapshot with an unnamed type");
        Equal("enemy unit", snapshot!.Units[0].Name, "unnamed enemy type");

        var logged = new List<string>();
        var tracker = new CondorBattleSpeechTracker(logged.Add);
        tracker.Observe(snapshot);
        tracker.Observe(snapshot);
        var unnamed = logged.Where(line => line.Contains("unnamed unit type")).ToList();
        Equal(1, unnamed.Count, "unnamed type reported once, not once per snapshot");
        AssertContains(unnamed[0], "unnamed unit type 10");

        // Named ones keep their side too, because the same type can stand on both.
        memory.WriteUnit(slot: 21, typeId: 2, currentHp: 180, maximumHp: 180, attack: 25, x: 120, y: 100);
        Equal(
            "enemy Attacker",
            new CondorBattleStateReader(memory).TryRead()!.Units[1].Name,
            "named enemy type");
    }

    private static CondorBattleSnapshot Battle(
        IReadOnlyList<int>? availableTypeIds = null,
        int row = 0,
        int rotation = 0,
        int modalState = 0,
        int gil = 1000,
        int messageId = -1,
        int outcome = 0,
        int cursorX = 0,
        int cursorY = 0,
        bool placementLegal = true,
        int phase = 0,
        int frontierY = 2000,
        int enemyAdvance = 0,
        IReadOnlyList<CondorCollisionTriangle>? terrain = null,
        IReadOnlyList<CondorBattleUnit>? units = null) =>
        new(
            InteractionMode: CondorBattleSnapshot.CursorInteractionMode,
            ModalState: modalState,
            SettingMenuRow: row,
            SettingMenuRotation: rotation,
            AvailableTypeIds: availableTypeIds ?? [],
            Gil: gil,
            CursorX: cursorX,
            CursorY: cursorY,
            CursorPlacementLegal: placementLegal,
            UnitUnderCursorSlot: -1,
            Units: units ?? [],
            AlliedCount: 0,
            EnemyCount: 0,
            Outcome: outcome,
            MessageId: messageId,
            Phase: phase,
            ReportState: 0,
            DeploymentFrontierY: frontierY,
            EnemyAdvance: enemyAdvance,
            CollisionTriangles: terrain ?? []);

    /// <summary>
    /// Moves the cursor and holds it there long enough for the placement reading
    /// to settle, returning whatever the settling sample said.
    /// </summary>
    private static IReadOnlyList<string> Settle(
        CondorBattleSpeechTracker tracker, int cursorX, int cursorY, bool placementLegal)
    {
        IReadOnlyList<string> lines = [];
        for (var sample = 0; sample < 8; sample++)
        {
            var spoken = tracker.Observe(Battle(cursorX: cursorX, cursorY: cursorY, placementLegal: placementLegal));
            if (spoken.Count > 0) { lines = spoken; }
        }

        return lines;
    }

    private static string Single(IReadOnlyList<string> lines)
    {
        Equal(1, lines.Count, "spoken line count");
        return lines[0];
    }

    /// <summary>
    /// A sparse stand-in for the module 9 globals. Anything never written reads
    /// as zero, and anything in <see cref="Unreadable"/> fails, so a test can put
    /// a hole exactly where it wants one.
    /// </summary>
    private sealed class CondorMemory : ILegacyAddressSpace
    {
        internal const uint InteractionMode = 0x00C74C50;
        internal const uint ModalState = 0x00C625E0;
        internal const uint SettingMenuRow = 0x00CBCCA0;
        internal const uint SettingMenuRotation = 0x00C75254;
        internal const uint SettingMenuCount = 0x00C75264;
        internal const uint AvailableTypeIds = 0x00C75278;
        internal const uint Gil = 0x00CBC7E0;
        internal const uint CursorX = 0x00CBCCC0;
        internal const uint CursorY = 0x00CBCCC2;
        internal const uint CursorPlacementLegal = 0x00CBCC9C;
        internal const uint UnitUnderCursor = 0x00C6097C;
        internal const uint LiveUnits = 0x00CBCCD8;
        internal const uint Phase = 0x00C625D4;
        internal const uint DeploymentFrontierY = 0x00C60AE8;
        internal const uint CollisionCount = 0x00C60AA4;
        internal const uint CollisionRecords = 0x00C625E8;
        internal const int CollisionStride = 0x4C;
        internal const uint AlliedCount = 0x00C60AD0;
        internal const uint EnemyCount = 0x00CBC7A4;
        internal const int UnitStride = 0x78;

        private readonly Dictionary<uint, byte> bytes = [];

        internal HashSet<uint> Unreadable { get; } = [];

        internal CondorMemory()
        {
            // Nothing under the cursor unless a test says otherwise; the native
            // value for that is -1, not 0, and 0 is a real slot.
            WriteInt16(UnitUnderCursor, -1);

            // An ordinary battle with the player moving the battlefield cursor.
            // Zero is not a mode the game uses, so leaving it unset would make
            // every cursor test pass by never reaching the cursor at all.
            WriteInt32(InteractionMode, CondorBattleSnapshot.CursorInteractionMode);

            // Open ground over the whole map, and a deployment frontier past the
            // bottom of it. Without terrain nothing is placeable anywhere, and a
            // test about what the cursor says would pass by never getting there.
            WriteOpenGround();
            WriteInt32(DeploymentFrontierY, 2000);
        }

        internal void WriteUnit(
            int slot,
            int typeId,
            int currentHp,
            int maximumHp,
            int attack,
            int x,
            int y,
            sbyte removalState = 0)
        {
            var unit = LiveUnits + (uint)(slot * UnitStride);
            WriteUInt16(unit + 0x00, 1);
            bytes[unit + 0x05] = (byte)removalState;
            WriteUInt16(unit + 0x06, (ushort)typeId);
            bytes[unit + 0x10] = (byte)currentHp;
            bytes[unit + 0x11] = (byte)maximumHp;
            bytes[unit + 0x12] = (byte)attack;
            bytes[unit + 0x22] = 22;
            bytes[unit + 0x23] = 26;
            WriteInt16(unit + 0x48, (short)x);
            WriteInt16(unit + 0x4A, (short)y);
        }

        /// <summary>
        /// Two triangles covering the whole battlefield, so every row of every
        /// column is on terrain unless a test puts something in the way.
        /// </summary>
        internal void WriteOpenGround()
        {
            var corners = new[] { (-600, -700), (600, -700), (600, 700), (-600, 700) };
            WriteCollisionTriangle(0, corners[0], corners[1], corners[2]);
            WriteCollisionTriangle(1, corners[0], corners[2], corners[3]);
            WriteInt32(CollisionCount, 2);
        }

        internal void WriteCollisionTriangle(
            int index, (int X, int Y) a, (int X, int Y) b, (int X, int Y) c)
        {
            var record = CollisionRecords + (uint)(index * CollisionStride);
            WriteInt16(record + 0x28, (short)a.X);
            WriteInt16(record + 0x2A, (short)a.Y);
            WriteInt16(record + 0x30, (short)b.X);
            WriteInt16(record + 0x32, (short)b.Y);
            WriteInt16(record + 0x38, (short)c.X);
            WriteInt16(record + 0x3A, (short)c.Y);

            // The record carries its own inclusive bounds, biased by 0x4000, and
            // the game applies them before the triangle test.
            WriteInt16(record + 0x40, (short)(0x4000 + Math.Min(a.X, Math.Min(b.X, c.X))));
            WriteInt16(record + 0x42, (short)(0x4000 + Math.Max(a.X, Math.Max(b.X, c.X))));
            WriteInt16(record + 0x44, (short)(0x4000 + Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            WriteInt16(record + 0x46, (short)(0x4000 + Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        }

        internal void WriteInt32(uint address, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            Store(address, buffer);
        }

        internal void WriteInt16(uint address, short value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
            Store(address, buffer);
        }

        internal void WriteUInt16(uint address, ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            Store(address, buffer);
        }

        internal void WriteTypeIds(IReadOnlyList<int> ids)
        {
            WriteInt16(SettingMenuCount, (short)ids.Count);
            for (var index = 0; index < ids.Count; index++)
            {
                bytes[AvailableTypeIds + (uint)index] = (byte)ids[index];
            }
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var offset = 0u; offset < destination.Length; offset++)
            {
                if (Unreadable.Contains(virtualAddress + offset))
                {
                    return false;
                }

                destination[(int)offset] = bytes.GetValueOrDefault(virtualAddress + offset);
            }

            return true;
        }

        private void Store(uint address, ReadOnlySpan<byte> value)
        {
            for (var offset = 0; offset < value.Length; offset++)
            {
                bytes[address + (uint)offset] = value[offset];
            }
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void AssertNull(object? actual, string label)
    {
        if (actual is not null)
        {
            throw new InvalidOperationException($"{label}: expected null, got {actual}.");
        }
    }

    private static void AssertNotNull(object? actual, string label)
    {
        if (actual is null)
        {
            throw new InvalidOperationException($"{label}: expected a value, got null.");
        }
    }

    private static void AssertContains(string actual, string expected)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"expected \"{expected}\" within \"{actual}\".");
        }
    }
}
