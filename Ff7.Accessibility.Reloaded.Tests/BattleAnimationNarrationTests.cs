using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Limit break and summon narration: told once per save, and only when the engine is
/// really playing the animation.
///
/// <para>The guest memory in these cases is built the way the engine builds it, from
/// the decompiled legacy functions, with the addresses written out as literals so the
/// reader's own constants are never the thing being checked. <c>FUN_0042CBF9</c> case 1
/// starts queue row <c>[0x00BF2A38]</c> of the 12-byte queue at <c>0x009AAD70</c> only
/// while <c>0x00BF2128</c> is set; it then clears that flag, stores the attacker in
/// <c>0x00BE1170</c>, marks the actor busy (<c>0x00BE119E + a*0x1AEC</c> = 0) and calls
/// <c>FUN_0042D227</c>, which copies the row's action into <c>0x00BF23FE + a*0x74</c>.
/// <c>FUN_0042D808</c> sets the flag again only when every actor is idle. The translated
/// x64 functions (7FF7017FF520, 7FF7018008A0) read and write the same guest addresses.</para>
/// </summary>
internal static class BattleAnimationNarrationTests
{
    private static readonly DateTime T0 = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private const byte Summon = 0x03;
    private const byte Limit = 0x14;
    private const byte EnemyAttack = 0x20;

    public static void Run()
    {
        // The native reader.
        ARowTheEngineHasStartedIsPlaying();
        AQueuedRowTheEngineHasNotStartedIsNotPlaying();
        AnotherActorsAnimationIsNotThisRowsAnimation();
        AnActionLeftOverFromAnEarlierAnimationIsNotThisRow();
        ATerminatorOrTextRowIsNotAnAnimation();
        OutsideBattleNothingIsPlaying();
        ATornReadIsRejected();
        AnUnreadableByteIsNotAnIdleBattle();
        AnOutOfRangeQueueIndexIsRejected();
        AMidAnimationEffectSwitchKeepsTheRowsIdentity();

        // The native identities.
        EveryApprovedAnimationHasItsNativeIdentity();
        RowsThatAreNotADistinctAnimationAreNotNarrated();
        HistoryKeysArePersistedContracts();
        ApprovedDescriptionsParseIntoTheCatalog();
        AMalformedCatalogNarratesNothing();

        // Playback and history.
        ANarrationPlaysWhenTheAnimationStartsAndIsSpentOnlyWhenItCompletes();
        MenuSelectionAndQueuedAttacksSayNothing();
        EnemyActionsAndUncataloguedAnimationsSayNothing();
        RepeatedPollsOfOneAnimationStartItOnce();
        AHeardAnimationStaysSilentForTheRestOfTheSave();
        ABusyDeviceDelaysTheNarrationWithoutRepeatingIt();
        TheSameAnimationWaitsOnlyOnce();
        AnEndedBattleDropsWhatWasNeverSaid();
        AnInterruptedNarrationIsNotSpent();
        APlaybackThatFailsEarlyIsNotSpent();
        AStuckDeviceIsReleasedWithoutBeingHeard();
        AnOutputThatCannotReportCompletionNeverSpendsIt();
        WithoutARecordingTheWordsAreSpokenAndSpentOnlyWhenAccepted();
        PendingNarrationsAreBounded();
        TheSaveDecidesWhatHasBeenHeard();
        AskingWhetherItPlaysNeverWaitsOnAScan();

        // Whole native sequences.
        TifaNarratesEachChainMoveThatActuallyPlays();
        MissedReelsAreNeverDescribed();
        ToyBoxVariantsAreToldApart();
        ASlotsSummonUsesTheSummonsOwnNarration();
        OdinsLanceReplacesTheSwordAnimation();

        // Save binding.
        AConfirmedSaveBindsTheBattleHistoryToo();
        AFailedOrCancelledSaveBindsNeitherHistory();
        ALoadBindsBothHistoriesOnlyWhenPlaying();
        ANewGameClearsBothHistories();
        RoomAndBattleHistoriesStaySeparate();

        // Staged descriptions on the native battle clock.
        TheReaderReportsTheNativeTickAndPause();
        ARowStaysCurrentAfterItsPerformerIsIdle();
        StagedEntriesParseWithTheirCuesAndOwnHistory();
        InvalidStagedEntriesAreRejectedWhole();
        ACueWaitsForItsNativeTick();
        PausedTicksDoNotMoveTheCues();
        TheWrappingTickByteStillCountsForward();
        ATickGapTooLongToTrustIsNotCounted();
        CuesPlayInOrderWithoutOverlapping();
        AftermathCuesPlayWhileTheRowIsStillCurrent();
        CuesTheAnimationNeverReachedDoNotBlockTheMark();
        ADueCueMissingItsGraceLeavesTheAnimationUnheard();
        AFailedCueLeavesTheWholeSequenceUnheard();
        UnreadableScansNeitherMoveNorStartCues();
        TheRevisedDescriptionPlaysOnceDespiteTheOldHistory();
        EachTifaMoveKeepsItsOwnSchedule();

        // Hardening: native anchors, pause, lost progress, frame multiplier.
        TheReaderSeesTheSummonDispatcherInTheEffectTable();
        SummonAnchoredEntriesParseOnlyForSummons();
        SummonCuesCountFromTheSummonsOwnSequence();
        ASummonWhoseSequenceNeverBeganIsNotHeard();
        NoAnimationIsHeardWhenNoCueWasReached();
        LostNativeProgressAbandonsTheSchedule();
        APausedBattlePausesTheRecordingWithoutRestartingIt();
        ALongPauseDoesNotTripTheStuckDeviceWatchdog();
        ARecordingThatCannotPauseIsStoppedNotRunAhead();
        StopAndLoadWhilePausedLeaveTheAnimationUnheard();
        NoCueStartsWhileTheBattleIsPaused();
        FasterBattleFrameRatesScaleTheCueTicks();
        FasterBattleClocksRejectGapsThatCouldWrap();
        TheFfnxBattleFrameMultiplierComesFromItsConfig();

        // The summon banner's name and the casting opening.
        TheReaderReadsTheDisplayedSummonName();
        ASubstitutedSummonShowsTheKernelAttackName();
        AnUnusableTextTableShowsNoName();
        AnUnreadableBannerIsUnknownNotAbsent();
        TheBannerNameIsSpokenOncePerCast();
        ARefusedBannerNameIsOfferedAgainOnlyWhileShown();
        OnlyASummonsBannerNameIsSpoken();
        TheOpeningParsesOnlyWithTheBannerAnchor();
        TheOpeningCountsFromTheBannerUnderItsOwnKey();
        TheOpeningStillPlaysAfterTheSummonWasHeard();
        TheSummonStillPlaysAfterTheOpeningWasHeard();
        ABannerAlreadyShowingLeavesTheOpeningUntimed();
        TheOpeningWaitsForTheScreenReader();
        AnOpeningThatMissesTheCastIsDropped();
        ADueSummonCueStopsTheOpening();
        AnOpeningPausesAndStopsWithTheBattle();
        OnlyASummonCastOpensTheOpening();
        TheBannerNameDoesNotDependOnTheDescriptions();

        // Repair: the live legacy banner (capture 24500) and the empty kernel text blob.
        TheLiveLegacyBannerAnchorsTheOpening();
        TheReaderSeesTheLiveLegacyBanner();
        WithoutTheNativeTextTheBannerNameComesFromTheKernel();
        TheKernelNamesASummonBannerAsTheRendererDoes();
        TheLiveLegacyCastSpeaksItsBannerName();
        Console.WriteLine("PASS battle animation narration: native queue reader, 93 native identities, once-per-save playback and save binding.");
    }

    // --- the native reader ------------------------------------------------------------

    private static void ARowTheEngineHasStartedIsPlaying()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Limit, effect: 0x00, action: 0x00, script: 0x3C));
        memory.StartCurrent();

        Check(Read(memory, out var seen), "a started row reads coherently");
        Check(seen.InBattle && seen.IsPlaying, "the engine is playing the row");
        Check(seen.EventIndex == 0 && seen.Attacker == 0, "row 0, performed by party slot 0");
        Check(seen.Command == Limit && seen.Effect == 0x00 && seen.Action == 0x0000,
            "the row's own command, effect and action");
    }

    private static void AQueuedRowTheEngineHasNotStartedIsNotPlaying()
    {
        // Cloud used Braver last turn, so his actor still carries that action. The next
        // Braver is queued but FUN_0042CBF9 has not reached it yet: the flag is still set
        // and every actor is idle.
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Limit, effect: 0x00, action: 0x00, script: 0x3C));
        memory.StartCurrent();
        memory.FinishCurrent();
        memory.EndQueue();
        memory.Build(Row(attacker: 0, command: Limit, effect: 0x00, action: 0x00, script: 0x3C));

        Check(Read(memory, out var seen), "a queued row reads coherently");
        Check(seen.InBattle && !seen.IsPlaying, "a queued attack is not an animation yet");
    }

    private static void AnotherActorsAnimationIsNotThisRowsAnimation()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 1, command: Limit, effect: 0x07, action: 0x07, script: 0x3C));
        memory.StartCurrent();
        memory.Byte(0x00BE1170, 0); // the active actor is someone else

        Check(Read(memory, out var seen) && !seen.IsPlaying,
            "a row is only playing while its own attacker is the active actor");
    }

    private static void AnActionLeftOverFromAnEarlierAnimationIsNotThisRow()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Limit, effect: 0x01, action: 0x01, script: 0x3C));
        memory.StartCurrent();
        memory.UInt16(0x00BF23FE, 0x0000); // FUN_0042D227 has not copied this row's action

        Check(Read(memory, out var seen) && !seen.IsPlaying,
            "an actor still holding another action has not started this row");
    }

    private static void ATerminatorOrTextRowIsNotAnAnimation()
    {
        var memory = new NativeBattle();
        memory.EndQueue();
        Check(Read(memory, out var idle) && idle.InBattle && !idle.IsPlaying,
            "an empty queue is an idle battle");

        var text = new NativeBattle();
        text.Build(Row(attacker: 0, command: Limit, effect: 0x00, action: 0x00, script: 0x3C) with { Kind = 2 });
        text.StartCurrent();
        Check(Read(text, out var shown) && !shown.IsPlaying, "a text row is not an animation");
    }

    private static void OutsideBattleNothingIsPlaying()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Limit, effect: 0x00, action: 0x00, script: 0x3C));
        memory.StartCurrent();
        memory.Byte(0x00CBF9DC, 1); // the field module

        Check(Read(memory, out var seen) && !seen.InBattle && !seen.IsPlaying,
            "outside the battle module there is no battle animation");
    }

    private static void ATornReadIsRejected()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Limit, effect: 0x00, action: 0x00, script: 0x3C));
        memory.StartCurrent();
        memory.ChangeAfterFirstRead(0x00BF2A38, 1); // the queue advanced mid-read

        Check(!Read(memory, out _), "two passes that disagree are not an observation");
    }

    private static void AnUnreadableByteIsNotAnIdleBattle()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Limit, effect: 0x00, action: 0x00, script: 0x3C));
        memory.StartCurrent();
        memory.Unreadable(0x00BE119E); // the attacker's busy byte

        Check(!Read(memory, out _), "a failed read is unknown, never idle");
    }

    private static void AnOutOfRangeQueueIndexIsRejected()
    {
        var memory = new NativeBattle();
        memory.EndQueue();
        memory.Byte(0x00BF2A38, 64);

        Check(!Read(memory, out _), "an index past the 64-row queue is not a row");
    }

    private static void AMidAnimationEffectSwitchKeepsTheRowsIdentity()
    {
        // Script opcode 0xDA (FUN_0041FBA4 case 0x4C) rewrites the actor's effect and sets
        // its command to 2 mid-animation. The queued row is still the animation.
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 2, command: Limit, effect: 0x39, action: 0x6D, script: 0x3E));
        memory.StartCurrent();
        memory.Byte(0x00BE119A + 2 * 0x1AEC, 0x05);
        memory.Byte(0x00BE119B + 2 * 0x1AEC, 0x02);

        Check(Read(memory, out var seen) && seen.IsPlaying, "the row keeps playing");
        Check(seen.Command == Limit && seen.Effect == 0x39 && seen.Action == 0x6D,
            "identity comes from the queued row, not the rewritten actor fields");
    }

    // --- the native identities --------------------------------------------------------

    /// <summary>
    /// The 93 approved descriptions and the queue row each one is played from. Hand
    /// derived from the legacy executable and kernel, not from the code under test:
    /// summons by effect (<c>FUN_005C0E4B</c> switches on it; W-Summon is remapped to 3 by
    /// the table at 0x007B7618); limits by action - the relative limit index for a menu
    /// limit (<c>FUN_005C930F</c>), the absolute kernel attack for Tifa's chain moves
    /// (<c>FUN_005DC880</c> case 0x13), Cait Sith's reels (<c>FUN_005DF460</c>) and
    /// Vincent's forms (kernel character AI: 0x70 + form*2, 30% the second attack); the
    /// Toy Box by effect, which special 8 randomises to 0x46..0x4C.
    /// </summary>
    private static readonly (string Key, byte Command, ushort Action, byte Effect)[] Approved =
    [
        ("summon.choco_mog", Summon, 0x0000, 0x00),
        ("summon.fat_chocobo", Summon, 0x0060, 0x10),
        ("summon.shiva", Summon, 0x0001, 0x01),
        ("summon.ifrit", Summon, 0x0002, 0x02),
        ("summon.ramuh", Summon, 0x0003, 0x03),
        ("summon.titan", Summon, 0x0004, 0x04),
        ("summon.odin_sword", Summon, 0x0005, 0x05),
        ("summon.odin_lance", Summon, 0x0061, 0x11),
        ("summon.leviathan", Summon, 0x0006, 0x06),
        ("summon.bahamut", Summon, 0x0007, 0x07),
        ("summon.kujata", Summon, 0x0008, 0x08),
        ("summon.alexander", Summon, 0x0009, 0x09),
        ("summon.phoenix", Summon, 0x000A, 0x0A),
        ("summon.neo_bahamut", Summon, 0x000B, 0x0B),
        ("summon.hades", Summon, 0x000C, 0x0C),
        ("summon.typhon", Summon, 0x000D, 0x0D),
        ("summon.bahamut_zero", Summon, 0x000E, 0x0E),
        ("summon.knights", Summon, 0x000F, 0x0F),
        ("limit.cloud.braver", Limit, 0x00, 0x00),
        ("limit.cloud.cross_slash", Limit, 0x01, 0x01),
        ("limit.cloud.blade_beam", Limit, 0x02, 0x02),
        ("limit.cloud.climhazzard", Limit, 0x03, 0x03),
        ("limit.cloud.meteorain", Limit, 0x04, 0x04),
        ("limit.cloud.finishing_touch", Limit, 0x05, 0x05),
        ("limit.cloud.omnislash", Limit, 0x06, 0x06),
        ("limit.barret.big_shot", Limit, 0x07, 0x07),
        ("limit.barret.grenade_bomb", Limit, 0x08, 0x08),
        ("limit.barret.mindblow", Limit, 0x09, 0x09),
        ("limit.barret.hammerblow", Limit, 0x0A, 0x0A),
        ("limit.barret.satellite_beam", Limit, 0x0B, 0x0B),
        ("limit.barret.ungarmax", Limit, 0x0C, 0x0C),
        ("limit.barret.catastrophe", Limit, 0x0D, 0x0D),
        ("limit.aerith.healing_wind", Limit, 0x0E, 0x0E),
        ("limit.aerith.seal_evil", Limit, 0x0F, 0x0F),
        ("limit.aerith.breath_earth", Limit, 0x10, 0x10),
        ("limit.aerith.fury_brand", Limit, 0x11, 0x11),
        ("limit.aerith.planet_protector", Limit, 0x12, 0x12),
        ("limit.aerith.pulse_life", Limit, 0x13, 0x13),
        ("limit.aerith.great_gospel", Limit, 0x14, 0x14),
        ("limit.tifa.beat_rush", Limit, 0x62, 0x15),
        ("limit.tifa.somersault", Limit, 0x63, 0x15),
        ("limit.tifa.waterkick", Limit, 0x64, 0x15),
        ("limit.tifa.meteodrive", Limit, 0x65, 0x15),
        ("limit.tifa.dolphin_blow", Limit, 0x66, 0x15),
        ("limit.tifa.meteor_strike", Limit, 0x67, 0x15),
        ("limit.tifa.final_heaven", Limit, 0x68, 0x15),
        ("limit.cid.boost_jump", Limit, 0x1C, 0x1C),
        ("limit.cid.dragon", Limit, 0x1D, 0x1D),
        ("limit.cid.hyper_jump", Limit, 0x1E, 0x1E),
        ("limit.cid.dynamite", Limit, 0x1F, 0x1F),
        ("limit.cid.dragon_dive", Limit, 0x20, 0x20),
        ("limit.cid.big_brawl", Limit, 0x21, 0x21),
        ("limit.cid.highwind", Limit, 0x22, 0x22),
        ("limit.red.sled_fang", Limit, 0x23, 0x23),
        ("limit.red.howling_moon", Limit, 0x24, 0x24),
        ("limit.red.blood_fang", Limit, 0x25, 0x25),
        ("limit.red.stardust_ray", Limit, 0x26, 0x26),
        ("limit.red.lunatic_high", Limit, 0x27, 0x27),
        ("limit.red.earth_rave", Limit, 0x28, 0x28),
        ("limit.red.cosmo_memory", Limit, 0x29, 0x29),
        ("limit.cait.dice", Limit, 0x2A, 0x2A),
        ("limit.cait.game_over", Limit, 0x69, 0x3D),
        ("limit.cait.death_joker", Limit, 0x6A, 0x3C),
        ("limit.cait.toy_soldier", Limit, 0x6B, 0x3A),
        ("limit.cait.lucky_girl", Limit, 0x6C, 0x3B),
        ("limit.cait.mog_dance", Limit, 0x6D, 0x39),
        ("limit.cait.transform", Limit, 0x6E, 0x38),
        ("limit.cait.toy_boulder", Limit, 0x6F, 0x46),
        ("limit.cait.toy_ice", Limit, 0x6F, 0x47),
        ("limit.cait.toy_weight", Limit, 0x6F, 0x48),
        ("limit.cait.toy_hammer", Limit, 0x6F, 0x49),
        ("limit.cait.toy_chocobo", Limit, 0x6F, 0x4A),
        ("limit.cait.toy_house", Limit, 0x6F, 0x4B),
        ("limit.cait.toy_meteors", Limit, 0x6F, 0x4C),
        ("limit.vincent.galian_beast", Limit, 0x2D, 0x2D),
        ("limit.vincent.death_gigas", Limit, 0x2E, 0x2E),
        ("limit.vincent.hellmasker", Limit, 0x2F, 0x2F),
        ("limit.vincent.chaos", Limit, 0x30, 0x30),
        ("limit.vincent.berserk_dance", Limit, 0x70, 0xFF),
        ("limit.vincent.beast_flare", Limit, 0x71, 0x3F),
        ("limit.vincent.gigadunk", Limit, 0x72, 0xFF),
        ("limit.vincent.livewire", Limit, 0x73, 0x41),
        ("limit.vincent.splattercombo", Limit, 0x74, 0xFF),
        ("limit.vincent.nightmare", Limit, 0x75, 0x43),
        ("limit.vincent.chaos_saber", Limit, 0x76, 0xFF),
        ("limit.vincent.satan_slam", Limit, 0x77, 0x45),
        ("limit.yuffie.greased_lightning", Limit, 0x31, 0x31),
        ("limit.yuffie.clear_tranquil", Limit, 0x32, 0x32),
        ("limit.yuffie.landscaper", Limit, 0x33, 0x33),
        ("limit.yuffie.bloodfest", Limit, 0x34, 0x34),
        ("limit.yuffie.gauntlet", Limit, 0x35, 0x35),
        ("limit.yuffie.doom_living", Limit, 0x36, 0x36),
        ("limit.yuffie.all_creation", Limit, 0x37, 0x37),
    ];

    private static void EveryApprovedAnimationHasItsNativeIdentity()
    {
        Check(Approved.Length == 93, "the approved script has 93 animations");
        Check(Approved.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() == 93,
            "each approved animation is listed once");
        Check(BattleAnimationIdentities.All.Count == 93, "the engine knows exactly 93 animations");

        foreach (var (key, command, action, effect) in Approved)
        {
            var played = BattleAnimationObservation.Playing(0, 1, command, effect, action);
            Check(BattleAnimationIdentities.TryResolve(played, out var identity) && identity.Key == key,
                $"{key} is played from command 0x{command:X2}, action 0x{action:X2}, effect 0x{effect:X2}");
        }

        // A summon is the same animation whatever its action says: a normal cast carries
        // the relative index, a Slots reel the absolute kernel attack.
        Check(BattleAnimationIdentities.TryResolve(
                BattleAnimationObservation.Playing(0, 2, Summon, 0x0E, 0x0046), out var slotsZero)
            && slotsZero.Key == "summon.bahamut_zero", "a Slots summon plays the summon's animation");
    }

    private static void RowsThatAreNotADistinctAnimationAreNotNarrated()
    {
        var silent = new (string Why, BattleAnimationObservation Row)[]
        {
            ("Tifa's menu limit never plays; it is replaced by the reels", Played(Limit, 0x1B, 0x15)),
            ("the finish row after an all-miss spin carries the menu limit", Played(Limit, 0x15, 0x15)),
            ("Finishing Touch's second row is part of Finishing Touch", Played(Limit, 0x78, 0x4D)),
            ("Satan Slam's second row is part of Satan Slam", Played(Limit, 0x79, 0x4E)),
            ("Blade Beam's second row is part of Blade Beam", Played(Limit, 0x7A, 0x4F)),
            ("the Slots menu limit is replaced by its reel outcome", Played(Limit, 0x2C, 0x2C)),
            ("an enemy's attack", BattleAnimationObservation.Playing(0, 5, EnemyAttack, 0x02, 0x0002)),
            ("magic", Played(0x02, 0x07, 0x07)),
            ("an effect past the summon switch", Played(Summon, 0x12, 0x12)),
            ("an unnormalised W-Summon command", Played(0x16, 0x0E, 0x0E)),
            ("a limit command from outside the party", BattleAnimationObservation.Playing(0, 4, Limit, 0x00, 0x00)),
            ("a summon command from outside the party", BattleAnimationObservation.Playing(0, 3, Summon, 0x07, 0x07)),
            ("an idle battle", BattleAnimationObservation.Idle),
            ("no battle", BattleAnimationObservation.NotInBattle),
        };

        foreach (var (why, row) in silent)
        {
            Check(!BattleAnimationIdentities.TryResolve(row, out _), $"not narrated: {why}");
        }
    }

    private static void HistoryKeysArePersistedContracts()
    {
        var keys = BattleAnimationIdentities.All.Select(identity => identity.HistoryKey).ToArray();
        Check(keys.Distinct().Count() == keys.Length, "every animation has its own history key");
        Check(keys.All(key => key is >= 0 and <= ushort.MaxValue), "history keys fit the history file");

        // Saved histories hold these numbers; renumbering would replay or silence them.
        Check(Key("limit.cloud.braver") == 0x1400, "Braver is history key 0x1400");
        Check(Key("limit.tifa.final_heaven") == 0x1468, "Final Heaven is history key 0x1468");
        Check(Key("summon.bahamut_zero") == 0x030E, "Bahamut ZERO is history key 0x030E");
        Check(Key("limit.cait.toy_meteors") == 0x154C, "the Toy Box meteors are history key 0x154C");
    }

    private static void ApprovedDescriptionsParseIntoTheCatalog()
    {
        // Root's approved-descriptions.json shape, including its bookkeeping fields.
        const string json = """
            [
              {"id": 19, "key": "limit.cloud.braver", "name": "Cloud: Braver",
               "text": "Cloud leaps high and brings his sword straight down.",
               "reference_url": "https://example.invalid/video", "reference_start_seconds": 1},
              {"id": 87, "key": "limit.cait.toy_boulder", "name": "Cait Sith: Toy Box boulder",
               "text": "A broad slab crashes down."},
              {"id": 99, "key": "limit.cloud.not_a_limit", "name": "Nothing", "text": "Nothing."},
              {"id": 20, "key": "limit.cloud.cross_slash", "name": "Cloud: Cross-slash", "text": "   "},
              {"id": 21, "key": "limit.cloud.braver", "name": "Cloud: Braver", "text": "A second Braver."}
            ]
            """;
        var log = new List<string>();
        var catalog = BattleAnimationNarrationCatalog.Parse(json, log.Add);

        Check(catalog.Count == 2, "two usable descriptions");
        Check(catalog.TryGet(Played(Limit, 0x00, 0x00), out var braver)
              && braver.Text == "Cloud leaps high and brings his sword straight down."
              && braver.Name == "Cloud: Braver", "Braver's first entry is kept");
        Check(catalog.TryGet(Played(Limit, 0x6F, 0x46), out var boulder)
              && boulder.Text == "A broad slab crashes down.", "the boulder variant is found by its effect");
        Check(!catalog.TryGet(Played(Limit, 0x01, 0x01), out _), "a blank text is not a description");
        Check(log.Any(line => line.Contains("limit.cloud.not_a_limit", StringComparison.Ordinal)),
            "an unknown key is reported rather than guessed");
        Check(log.Any(line => line.Contains("duplicate", StringComparison.OrdinalIgnoreCase)),
            "a duplicate key is reported");
        Check(catalog.MissingKeys.Count == 91 && catalog.MissingKeys.Contains("limit.cloud.cross_slash"),
            "coverage lists every animation without a description");
    }

    private static void AMalformedCatalogNarratesNothing()
    {
        var log = new List<string>();
        var catalog = BattleAnimationNarrationCatalog.Parse("{ not json", log.Add);
        Check(catalog.Count == 0 && log.Count > 0, "a broken catalog is empty and says why");
        Check(BattleAnimationNarrationCatalog.Parse(null).Count == 0, "no catalog is an empty catalog");
    }

    // --- playback and history ---------------------------------------------------------

    private static void ANarrationPlaysWhenTheAnimationStartsAndIsSpentOnlyWhenItCompletes()
    {
        var rig = new Rig();
        rig.Update(Played(Limit, 0x00, 0x00, index: 0), 0);

        Check(rig.Voice.Started.SequenceEqual(["braver.ogg"]), "Braver's recording starts with the animation");
        Check(rig.Narration.IsPlaying, "the narration owns the device");
        Check(!rig.History.HasHeard(0x1400), "a narration that has only started is not yet heard");

        rig.Update(BattleAnimationObservation.Idle, 3);
        Check(!rig.History.HasHeard(0x1400), "still playing after the animation ended");

        rig.Voice.Last.Finish();
        rig.Update(BattleAnimationObservation.Idle, 6.1);
        Check(rig.History.HasHeard(0x1400), "a narration that played to its end is heard");
        Check(!rig.Narration.IsPlaying, "and the device is free again");
    }

    private static void MenuSelectionAndQueuedAttacksSayNothing()
    {
        var rig = new Rig();
        var memory = new NativeBattle();

        // The player picks Braver: nothing is queued yet.
        memory.EndQueue();
        rig.ReadAndUpdate(memory, 0);

        // Queued behind someone else's turn: the row exists but has not started.
        memory.Build(Row(attacker: 0, command: Limit, effect: 0x00, action: 0x00, script: 0x3C));
        rig.ReadAndUpdate(memory, 0.1);

        // Cancelled: the queue is torn down before the engine reaches it.
        memory.EndQueue();
        rig.ReadAndUpdate(memory, 0.2);

        Check(rig.Voice.Started.Count == 0, "no narration for a selection that never played");
        Check(!rig.History.HasHeard(0x1400), "and nothing is spent");
    }

    private static void EnemyActionsAndUncataloguedAnimationsSayNothing()
    {
        var rig = new Rig();
        rig.Update(BattleAnimationObservation.Playing(0, 6, EnemyAttack, 0x00, 0x0000), 0);
        rig.Update(BattleAnimationObservation.Idle, 0.1);
        rig.Update(Played(Limit, 0x01, 0x01, index: 1), 0.2); // Cross-slash has no description in this rig
        Check(rig.Voice.Started.Count == 0 && rig.Spoken.Count == 0, "only described party animations speak");
    }

    private static void RepeatedPollsOfOneAnimationStartItOnce()
    {
        var rig = new Rig();
        for (var poll = 0; poll < 20; poll++)
        {
            rig.Update(Played(Limit, 0x00, 0x00, index: 0), poll * 0.035);
        }

        Check(rig.Voice.Started.Count == 1, "one animation, one narration");
    }

    private static void AHeardAnimationStaysSilentForTheRestOfTheSave()
    {
        var rig = new Rig();
        rig.PlayToEnd(Played(Limit, 0x00, 0x00, index: 0), 0);

        rig.Update(Played(Limit, 0x00, 0x00, index: 0), 30);
        rig.Update(BattleAnimationObservation.NotInBattle, 40);
        rig.Update(Played(Limit, 0x00, 0x00, index: 0), 300);

        Check(rig.Voice.Started.Count == 1, "Braver is described once per save");
    }

    private static void ABusyDeviceDelaysTheNarrationWithoutRepeatingIt()
    {
        var rig = new Rig();
        rig.OtherDescriptionPlaying = true;
        for (var poll = 0; poll < 10; poll++)
        {
            rig.Update(Played(Summon, 0x0E, 0x000E, index: 0), poll * 0.035);
        }

        Check(rig.Voice.Started.Count == 0, "nothing starts over another description");

        rig.OtherDescriptionPlaying = false;
        rig.Update(Played(Summon, 0x0E, 0x000E, index: 0), 1);
        rig.Update(BattleAnimationObservation.Idle, 1.1);
        Check(rig.Voice.Started.SequenceEqual(["bahamut-zero.ogg"]), "it plays once the device is free, once");
    }

    private static void TheSameAnimationWaitsOnlyOnce()
    {
        // Cloud uses Braver on two turns while another description holds the voice. One
        // narration waits for both, so a device that then fails does not replay it at once.
        var rig = new Rig();
        rig.OtherDescriptionPlaying = true;
        rig.Update(Played(Limit, 0x00, 0x00, index: 0), 0);
        rig.Update(BattleAnimationObservation.Idle, 3);
        rig.Update(Played(Limit, 0x00, 0x00, index: 0), 9);
        rig.Update(BattleAnimationObservation.Idle, 12);

        rig.OtherDescriptionPlaying = false;
        rig.Update(BattleAnimationObservation.Idle, 13);
        rig.Voice.Last.Fail(); // the device gave out at once
        rig.Update(BattleAnimationObservation.Idle, 13.2);
        rig.Update(BattleAnimationObservation.Idle, 13.4);

        Check(rig.Voice.Started.SequenceEqual(["braver.ogg"]), "one wait, one attempt");
        Check(!rig.History.HasHeard(0x1400), "the failed attempt is kept for next time");
    }

    private static void AnEndedBattleDropsWhatWasNeverSaid()
    {
        var rig = new Rig();
        rig.OtherDescriptionPlaying = true;
        rig.Update(Played(Summon, 0x0E, 0x000E, index: 0), 0);
        rig.Update(BattleAnimationObservation.NotInBattle, 20);
        rig.OtherDescriptionPlaying = false;
        rig.Update(BattleAnimationObservation.NotInBattle, 21);

        Check(rig.Voice.Started.Count == 0, "a narration still waiting when the battle ends is dropped");
        Check(!rig.History.HasHeard(0x030E), "and stays unheard");

        rig.Update(Played(Summon, 0x0E, 0x000E, index: 0), 100);
        Check(rig.Voice.Started.SequenceEqual(["bahamut-zero.ogg"]), "so the next Bahamut ZERO is described");
    }

    private static void AnInterruptedNarrationIsNotSpent()
    {
        var rig = new Rig();
        rig.Update(Played(Limit, 0x00, 0x00, index: 0), 0);
        rig.Narration.Stop("window lost the foreground");
        rig.Update(BattleAnimationObservation.Idle, 7);

        Check(rig.Voice.Last.Stops == 1, "the recording was stopped");
        Check(!rig.History.HasHeard(0x1400), "an interrupted narration is not heard");

        rig.Update(Played(Limit, 0x00, 0x00, index: 0), 60);
        Check(rig.Voice.Started.Count == 2, "the next Braver is described again");
    }

    private static void APlaybackThatFailsEarlyIsNotSpent()
    {
        var rig = new Rig();
        rig.Update(Played(Limit, 0x00, 0x00, index: 0), 0);
        rig.Voice.Last.Fail(); // the device gave out
        rig.Update(BattleAnimationObservation.Idle, 5.9);

        Check(!rig.History.HasHeard(0x1400), "a clip the device abandoned was not heard, however late it failed");
        Check(!rig.Narration.IsPlaying, "the failed output is released");
    }

    private static void AStuckDeviceIsReleasedWithoutBeingHeard()
    {
        var rig = new Rig();
        rig.Update(Played(Limit, 0x00, 0x00, index: 0), 0);
        rig.Update(BattleAnimationObservation.Idle, 0.2);
        rig.Update(Played(Limit, 0x6F, 0x46, index: 0), 0.4);
        for (var second = 1; second <= 12; second++)
        {
            // The Toy Box row stays current; the Braver clip never reports its end.
            rig.Update(Played(Limit, 0x6F, 0x46, index: 0), second);
        }

        Check(rig.Voice.Last.File == "boulder.ogg", "the waiting narration gets its turn");
        Check(rig.Voice.Created[0].Stops == 1, "the stuck output is stopped");
        Check(!rig.History.HasHeard(0x1400), "a device that never finished proved nothing was heard");
    }

    private static void AnOutputThatCannotReportCompletionNeverSpendsIt()
    {
        // Only the device can say a recording reached its end; elapsed time cannot tell a
        // finished clip from one that died near its end.
        var history = new FieldAreaDescriptionHistory(path: null);
        var bare = new BareOutput();
        var narration = new BattleAnimationNarrationCoordinator(
            new BattleAnimationNarrationCatalog([("limit.cloud.braver", "Cloud: Braver", "Cloud leaps high.")]),
            history,
            CutsceneVoiceManifest.Parse("""{"entries": [{"text": "Cloud leaps high.", "file": "braver.ogg", "duration_seconds": 6.0}]}"""),
            _ => bare,
            _ => { });
        narration.Update(Played(Limit, 0x00, 0x00, index: 0), T0);
        bare.IsPlaying = false;
        narration.Update(BattleAnimationObservation.Idle, T0.AddSeconds(7));

        Check(bare.Starts == 1, "the recording played");
        Check(!history.HasHeard(0x1400), "without the device's word it is not recorded as heard");
        Check(!narration.IsPlaying, "and the output is released");
    }

    private static void WithoutARecordingTheWordsAreSpokenAndSpentOnlyWhenAccepted()
    {
        var refused = new Rig { SpeechAccepts = false };
        refused.Update(Played(Limit, 0x0E, 0x0E, index: 0), 0); // Healing Wind has no recording
        refused.Update(Played(Limit, 0x0E, 0x0E, index: 0), 0.1);
        Check(refused.Spoken.SequenceEqual(["Green light gathers."]), "offered to speech exactly once");
        Check(!refused.History.HasHeard(0x140E), "speech that refused the words spent nothing");

        var accepted = new Rig();
        accepted.Update(Played(Limit, 0x0E, 0x0E, index: 0), 0);
        Check(accepted.Spoken.SequenceEqual(["Green light gathers."]), "spoken in the reader's voice");
        accepted.Update(BattleAnimationObservation.Idle, 1);
        Check(accepted.History.HasHeard(0x140E), "accepted speech is heard once the animation ends");
    }

    private static void PendingNarrationsAreBounded()
    {
        var rig = new Rig();
        rig.OtherDescriptionPlaying = true;
        var summons = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
        for (var i = 0; i < summons.Length; i++)
        {
            rig.Update(Played(Summon, summons[i], summons[i], index: (byte)i), i * 0.05);
        }

        rig.Update(BattleAnimationObservation.Idle, 0.5);
        rig.OtherDescriptionPlaying = false;
        for (var i = 0; i < 12; i++)
        {
            rig.Update(BattleAnimationObservation.Idle, 0.6 + (i * 0.1));
            if (rig.Narration.IsPlaying)
            {
                rig.Voice.Last.Finish();
            }
        }

        Check(rig.Voice.Started.Count == BattleAnimationNarrationCoordinator.MaximumPending,
            "no more than the bound is ever waiting");
        Check(rig.Voice.Started[0] == "summon-00.ogg", "the oldest waiting narration goes first");
        Check(!rig.History.HasHeard(0x0309), "what did not fit stays unheard for next time");
    }

    private static void TheSaveDecidesWhatHasBeenHeard()
    {
        var directory = Path.Combine(Path.GetTempPath(), "blind-soldier-battle-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "battle-descriptions.json");
        try
        {
            var history = new FieldAreaDescriptionHistory(path);
            history.LoadSave(1, 1);
            var first = new Rig(history);
            first.PlayToEnd(Played(Limit, 0x00, 0x00, index: 0), 0);
            Check(history.HasHeard(0x1400), "save 1:1 heard Braver");

            var restarted = new FieldAreaDescriptionHistory(path);
            restarted.LoadSave(1, 1);
            var again = new Rig(restarted);
            again.Update(Played(Limit, 0x00, 0x00, index: 0), 0);
            Check(again.Voice.Started.Count == 0, "the same save after a restart stays silent");

            restarted.LoadSave(1, 2);
            again.Update(BattleAnimationObservation.Idle, 1);
            again.Update(Played(Limit, 0x00, 0x00, index: 0), 2);
            Check(again.Voice.Started.Count == 1, "another game slot has its own history");

            restarted.BeginNewGame();
            var fresh = new Rig(restarted);
            fresh.Update(Played(Limit, 0x00, 0x00, index: 0), 0);
            Check(fresh.Voice.Started.Count == 1, "a new game hears it again");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void AskingWhetherItPlaysNeverWaitsOnAScan()
    {
        // The cutscene voice asks whether a battle narration is playing while holding its
        // own lock, and this scan asks whether the cutscene voice is playing. If the answer
        // here waited on the scan, the two players could wait on each other forever.
        BattleAnimationNarrationCoordinator? narration = null;
        var answeredInTime = true;
        var history = new FieldAreaDescriptionHistory(path: null);
        var voices = new Voices();
        narration = new BattleAnimationNarrationCoordinator(
            new BattleAnimationNarrationCatalog([("limit.cloud.braver", "Cloud: Braver", "Cloud leaps high.")]),
            history,
            CutsceneVoiceManifest.Parse("""{"entries": [{"text": "Cloud leaps high.", "file": "braver.ogg", "duration_seconds": 6.0}]}"""),
            voices.Create,
            _ => { },
            _ => true,
            () =>
            {
                var other = new Thread(() => _ = narration!.IsPlaying);
                other.Start();
                answeredInTime &= other.Join(TimeSpan.FromSeconds(2));
                return false;
            });

        narration.Update(Played(Limit, 0x00, 0x00, index: 0), T0);
        Check(answeredInTime, "another thread learns whether a narration plays without waiting for the scan");
        Check(voices.Started.SequenceEqual(["braver.ogg"]), "and the scan itself carries on");
    }

    // --- whole native sequences -------------------------------------------------------

    private static void TifaNarratesEachChainMoveThatActuallyPlays()
    {
        // Final Heaven with every reel a hit: FUN_005DC880 case 0x13 queues the seven
        // moves 0x62..0x68, and FUN_005CAB7C(0x47) then adds a finish row that repeats
        // the last move's action with animation script 0x47.
        var rig = new Rig(TifaCatalog());
        var memory = new NativeBattle();
        var rows = Enumerable.Range(0, 7)
            .Select(move => Row(attacker: 1, command: Limit, effect: 0x15, action: (ushort)(0x62 + move), script: (byte)(0x3C + move)))
            .Append(Row(attacker: 1, command: Limit, effect: 0x15, action: 0x68, script: 0x47))
            .ToArray();
        memory.Build(rows);

        var t = 0.0;
        foreach (var _ in rows)
        {
            memory.StartCurrent();
            rig.ReadAndUpdate(memory, t += 0.035);
            FinishIfPlaying(rig);
            rig.ReadAndUpdate(memory, t += 0.035);
            memory.FinishCurrent();
            rig.ReadAndUpdate(memory, t += 0.035);
        }

        memory.EndQueue();
        for (var i = 0; i < 5; i++)
        {
            FinishIfPlaying(rig);
            rig.ReadAndUpdate(memory, t += 0.1);
        }

        Check(rig.Voice.Started.SequenceEqual(
            ["tifa-62.ogg", "tifa-63.ogg", "tifa-64.ogg", "tifa-65.ogg", "tifa-66.ogg", "tifa-67.ogg", "tifa-68.ogg"]),
            "each chain move is described once, in the order it played");
        Check(Enumerable.Range(0x62, 7).All(move => rig.History.HasHeard(0x1400 + move)),
            "every move that played is heard");
    }

    private static void MissedReelsAreNeverDescribed()
    {
        // Reels: hit, miss, yeah!, then misses. Only Beat Rush and Waterkick are queued
        // (a "yeah" is the 0x80 flag, cleared before the action is used).
        var rig = new Rig(TifaCatalog());
        var memory = new NativeBattle();
        memory.Build(
            Row(attacker: 1, command: Limit, effect: 0x15, action: 0x62, script: 0x3C),
            Row(attacker: 1, command: Limit, effect: 0x15, action: 0x64, script: 0x3E),
            Row(attacker: 1, command: Limit, effect: 0x15, action: 0x64, script: 0x47));
        var t = 0.0;
        for (var row = 0; row < 3; row++)
        {
            memory.StartCurrent();
            rig.ReadAndUpdate(memory, t += 0.035);
            FinishIfPlaying(rig);
            memory.FinishCurrent();
            rig.ReadAndUpdate(memory, t += 0.035);
        }

        memory.EndQueue();
        for (var i = 0; i < 5; i++)
        {
            FinishIfPlaying(rig);
            rig.ReadAndUpdate(memory, t += 0.1);
        }

        Check(rig.Voice.Started.SequenceEqual(["tifa-62.ogg", "tifa-64.ogg"]), "only the moves that played");
        Check(!rig.History.HasHeard(0x1463), "Somersault's reel missed, so it was never described");

        // Every reel missed: only the finish row plays, carrying the menu limit.
        var allMissed = new Rig(TifaCatalog());
        var empty = new NativeBattle();
        empty.Build(Row(attacker: 1, command: Limit, effect: 0x15, action: 0x1B, script: 0x47));
        empty.StartCurrent();
        allMissed.ReadAndUpdate(empty, 0);
        Check(allMissed.Voice.Started.Count == 0, "a spin that missed everything describes nothing");
    }

    private static void ToyBoxVariantsAreToldApart()
    {
        var rig = new Rig();
        rig.PlayToEnd(Played(Limit, 0x6F, 0x46, index: 0), 0);
        rig.Update(Played(Limit, 0x6F, 0x49, index: 0), 20);
        rig.Update(Played(Limit, 0x6F, 0x46, index: 0), 21);

        Check(rig.Voice.Started.SequenceEqual(["boulder.ogg", "hammer.ogg"]),
            "a different Toy Box object is a different animation");
    }

    private static void ASlotsSummonUsesTheSummonsOwnNarration()
    {
        // FUN_005DF460 with three bars: command 3, absolute attack 0x46 (Bahamut ZERO).
        var rig = new Rig();
        rig.PlayToEnd(BattleAnimationObservation.Playing(0, 2, Summon, 0x0E, 0x0046), 0);
        rig.Update(Played(Summon, 0x0E, 0x000E, index: 0), 60);

        Check(rig.Voice.Started.SequenceEqual(["bahamut-zero.ogg"]),
            "Cait Sith's summon spends the summon's own narration");
    }

    private static void OdinsLanceReplacesTheSwordAnimation()
    {
        // FUN_005DC880 special 1, phase 5: no target died, so FUN_00436C4B rolls the
        // sword rows back and the Gunge Lance sub-action (0x61, effect 0x11) is queued.
        var rig = new Rig();
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Summon, effect: 0x11, action: 0x61, script: 0x3C));
        memory.StartCurrent();
        rig.ReadAndUpdate(memory, 0);

        Check(rig.Voice.Started.SequenceEqual(["odin-lance.ogg"]), "the lance, not the sword, is described");
    }

    // --- save binding -----------------------------------------------------------------

    private static void AConfirmedSaveBindsTheBattleHistoryToo()
    {
        var rooms = new FieldAreaDescriptionHistory(path: null);
        var battles = new FieldAreaDescriptionHistory(path: null);
        var tracker = new FieldAreaDescriptionSaveTracker(rooms, null, battles);
        battles.MarkHeard(0x1400);

        Confirm(tracker, file: 2, game: 11, NativeSaveResultPopupReader.SuccessTextIdentity);

        battles.BeginNewGame();
        battles.LoadSave(2, 11);
        Check(battles.HasHeard(0x1400), "the saved slot keeps the battle history it was saved with");
    }

    private static void AFailedOrCancelledSaveBindsNeitherHistory()
    {
        var rooms = new FieldAreaDescriptionHistory(path: null);
        var battles = new FieldAreaDescriptionHistory(path: null);
        var tracker = new FieldAreaDescriptionSaveTracker(rooms, null, battles);
        rooms.MarkHeard(547);
        battles.MarkHeard(0x1400);

        Confirm(tracker, file: 3, game: 4, NativeSaveResultPopupReader.FailureTextIdentity);
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Games, 9, 9), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Confirmation, 9, 9), Down());
        tracker.ObserveSaveMenu(null, Down());

        foreach (var (file, game) in new[] { (3, 4), (9, 9) })
        {
            rooms.LoadSave(file, game);
            battles.LoadSave(file, game);
            Check(!rooms.HasHeard(547) && !battles.HasHeard(0x1400),
                $"slot {file}:{game} was never saved to");
        }
    }

    private static void ALoadBindsBothHistoriesOnlyWhenPlaying()
    {
        var rooms = new FieldAreaDescriptionHistory(path: null);
        var battles = new FieldAreaDescriptionHistory(path: null);
        rooms.SaveGame(2, 11);
        battles.SaveGame(2, 11);
        rooms.MarkHeard(547);
        battles.MarkHeard(0x1400);
        rooms.BeginNewGame();
        battles.BeginNewGame();

        var tracker = new FieldAreaDescriptionSaveTracker(rooms, null, battles);
        tracker.ObserveLoadMenu(saveFile: 2, gameSlot: 11, loadingPageActive: true);
        tracker.ObserveLoadReadiness(FieldAreaDescriptionSaveTracker.LoadedReadiness);
        tracker.ObserveLoadAbandoned();
        tracker.ObserveEnteredPlayableModule();
        Check(!battles.HasHeard(0x1400), "an abandoned load binds nothing");

        tracker.ObserveLoadMenu(saveFile: 2, gameSlot: 11, loadingPageActive: false);
        tracker.ObserveLoadMenu(saveFile: 2, gameSlot: null, loadingPageActive: true);
        tracker.ObserveLoadReadiness(FieldAreaDescriptionSaveTracker.LoadedReadiness);
        tracker.ObserveEnteredPlayableModule();
        Check(rooms.HasHeard(547) && battles.HasHeard(0x1400), "a completed load binds both histories");
    }

    private static void ANewGameClearsBothHistories()
    {
        var rooms = new FieldAreaDescriptionHistory(path: null);
        var battles = new FieldAreaDescriptionHistory(path: null);
        rooms.LoadSave(1, 1);
        battles.LoadSave(1, 1);
        rooms.MarkHeard(547);
        battles.MarkHeard(0x1400);

        var tracker = new FieldAreaDescriptionSaveTracker(rooms, null, battles);
        tracker.ObserveNewGame();

        Check(!rooms.HasHeard(547) && !battles.HasHeard(0x1400), "a new game inherits neither history");
        battles.LoadSave(1, 1);
        Check(battles.HasHeard(0x1400), "and the save it left is untouched");
    }

    private static void RoomAndBattleHistoriesStaySeparate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "blind-soldier-battle-rooms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var roomsPath = Path.Combine(directory, "room-descriptions.json");
            var battlesPath = Path.Combine(directory, "battle-descriptions.json");
            var rooms = new FieldAreaDescriptionHistory(roomsPath);
            var battles = new FieldAreaDescriptionHistory(battlesPath);
            var tracker = new FieldAreaDescriptionSaveTracker(rooms, null, battles);
            rooms.MarkHeard(0x0300); // a field id that happens to equal Choco/Mog's key
            battles.MarkHeard(0x1400);
            Confirm(tracker, file: 1, game: 1, NativeSaveResultPopupReader.SuccessTextIdentity);

            var roomsAgain = new FieldAreaDescriptionHistory(roomsPath);
            var battlesAgain = new FieldAreaDescriptionHistory(battlesPath);
            roomsAgain.LoadSave(1, 1);
            battlesAgain.LoadSave(1, 1);
            Check(roomsAgain.HasHeard(0x0300) && !roomsAgain.HasHeard(0x1400), "rooms keep only rooms");
            Check(battlesAgain.HasHeard(0x1400) && !battlesAgain.HasHeard(0x0300), "battles keep only battles");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    // --- staged descriptions on the native battle clock -------------------------------

    private const string StagedJson = """
        [
          {"key": "summon.bahamut_zero", "name": "Bahamut ZERO", "revision": 2, "cues": [
            {"atFrame": 60, "text": "Zero rises."},
            {"atFrame": 90, "text": "Zero fires."},
            {"atFrame": 300, "text": "Smoke clears."}]},
          {"key": "limit.cloud.braver", "name": "Cloud: Braver", "revision": 2, "cues": [
            {"atFrame": 0, "text": "Cloud leaps."}]},
          {"key": "limit.tifa.beat_rush", "name": "Tifa: Beat Rush", "revision": 2, "cues": [
            {"atFrame": 0, "text": "Rush."}]},
          {"key": "limit.tifa.somersault", "name": "Tifa: Somersault", "revision": 2, "cues": [
            {"atFrame": 0, "text": "Flip."}]}
        ]
        """;

    private static BattleAnimationObservation ZeroRow(int tick, bool playing = true, bool paused = false) =>
        (playing
            ? BattleAnimationObservation.Playing(3, 1, Summon, 0x0E, 0x000E)
            : BattleAnimationObservation.Current(3, 1, Summon, 0x0E, 0x000E))
        with { Tick = (byte)tick, Paused = paused };

    private static BattleAnimationObservation IdleAt(int tick) =>
        BattleAnimationObservation.Idle with { Tick = (byte)tick };

    private static void TheReaderReportsTheNativeTickAndPause()
    {
        // FUN_0042D808 increments the byte 0x00BFD0E4 every queue tick; FUN_0041BAB3 runs
        // effects and scripts only while 0x00DC0E6C is clear.
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Limit, effect: 0x00, action: 0x00, script: 0x3C));
        memory.StartCurrent();
        memory.Byte(0x00BFD0E4, 0xF3);
        memory.Byte(0x00DC0E6C, 1);

        Check(Read(memory, out var seen) && seen.IsPlaying, "the row is playing");
        Check(seen.Tick == 0xF3 && seen.Paused, "the native tick and the pause flag are read");
    }

    private static void ARowStaysCurrentAfterItsPerformerIsIdle()
    {
        // Script opcode 0x9E marks the actor idle while its effects run on; the queue only
        // leaves the row once FUN_0042D808 finds every actor idle and no effect running.
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 1, command: Summon, effect: 0x0E, action: 0x0E, script: 0x3C));
        memory.StartCurrent();
        memory.Byte(0x00BE119E + 0x1AEC, 1);

        Check(Read(memory, out var seen) && !seen.IsPlaying && seen.HasRow, "the row is still current");
        Check(seen.EventIndex == 0 && seen.Attacker == 1 && seen.Command == Summon && seen.Effect == 0x0E,
            "and keeps its identity for the schedule");

        memory.FinishCurrent();
        Check(Read(memory, out var after) && !after.HasRow, "the terminator row ends it");
    }

    private static void StagedEntriesParseWithTheirCuesAndOwnHistory()
    {
        var catalog = BattleAnimationNarrationCatalog.Parse(StagedJson);
        Check(catalog.Count == 4, "four staged descriptions");
        Check(catalog.TryGet(Played(Summon, 0x0E, 0x0E), out var zero), "Bahamut ZERO is described");
        Check(zero.Revision == 2 && zero.Cues.Select(cue => cue.AtFrame).SequenceEqual([60, 90, 300]),
            "its cues keep their native ticks in order");
        Check(zero.Cues[2].Text == "Smoke clears.", "and their text");
        Check(zero.HistoryKey == 0x830E, "a revision 2 description has its own history key");

        var old = new BattleAnimationNarrationCatalog([("summon.bahamut_zero", "Bahamut ZERO", "Old text.")]);
        Check(old.TryGet(Played(Summon, 0x0E, 0x0E), out var single) && single.Revision == 1 &&
              single.HistoryKey == 0x030E && single.Cues.Count == 1 && single.Cues[0].AtFrame == 0 &&
              single.Cues[0].Text == "Old text.",
            "an old single text is one cue at frame 0 under the old key");
    }

    private static void InvalidStagedEntriesAreRejectedWhole()
    {
        const string json = """
            [
              {"key": "limit.cloud.braver", "revision": 2, "cues": [{"atFrame": 30, "text": "b"}, {"atFrame": 10, "text": "a"}]},
              {"key": "limit.cloud.cross_slash", "revision": 2, "cues": []},
              {"key": "limit.cloud.blade_beam", "revision": 2, "cues": [{"atFrame": 0, "text": "  "}]},
              {"key": "limit.cloud.climhazzard", "revision": 3, "cues": [{"atFrame": 0, "text": "c"}]},
              {"key": "limit.cloud.meteorain", "revision": 2, "cues": [{"atFrame": -1, "text": "d"}]},
              {"key": "limit.cloud.omnislash", "revision": 2, "cues": [{"atFrame": 3601, "text": "e"}]},
              {"key": "limit.cloud.finishing_touch", "revision": 2, "cues": [{"atFrame": 5, "text": "f"}]}
            ]
            """;
        var log = new List<string>();
        var catalog = BattleAnimationNarrationCatalog.Parse(json, log.Add);
        Check(catalog.Count == 1 && catalog.TryGet(Played(Limit, 0x05, 0x05), out _),
            "only the valid entry is used");
        Check(log.Count >= 6, "every rejected entry is reported");
    }

    private static void ACueWaitsForItsNativeTick()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(10), 0);
        rig.Update(ZeroRow(69), 3.9);
        Check(rig.Voice.Started.Count == 0, "59 ticks in, the summon has not appeared yet");

        rig.Update(ZeroRow(70), 4.0);
        Check(rig.Voice.Started.SequenceEqual(["zero-rises.ogg"]), "at tick 60 its first cue plays");
    }

    private static void PausedTicksDoNotMoveTheCues()
    {
        // The queue tick keeps counting while the battle is paused; the animation does not.
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(20), 1.3);
        rig.Update(ZeroRow(40, paused: true), 2.6);
        rig.Update(ZeroRow(100, paused: true), 6.6);
        rig.Update(ZeroRow(120), 8);
        Check(rig.Voice.Started.Count == 0, "only 20 unpaused ticks have passed");

        rig.Update(ZeroRow(160), 10.6);
        Check(rig.Voice.Started.SequenceEqual(["zero-rises.ogg"]), "60 unpaused ticks reach the cue");
    }

    private static void TheWrappingTickByteStillCountsForward()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(230), 0);
        rig.Update(ZeroRow(250), 1.3);
        rig.Update(ZeroRow(10), 2.6);
        rig.Update(ZeroRow(34), 4.2);
        Check(rig.Voice.Started.SequenceEqual(["zero-rises.ogg"]), "230 -> 34 across the wrap is 60 ticks");
    }

    private static void ATickGapTooLongToTrustIsNotCounted()
    {
        // The byte wraps every 256 ticks (about 17 s at 15 a second). After a stall longer
        // than that could be, the difference proves nothing: the animation that was playing
        // is abandoned, and the next one is timed from its own start.
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(60), 13);
        rig.Update(ZeroRow(120), 17);
        Check(rig.Voice.Started.Count == 0, "no cue is timed across the stall");

        rig.Update(IdleAt(130), 18);
        rig.Update(Played(Limit, 0x00, 0x00, index: 0) with { Tick = 140 }, 18.5);
        Check(rig.Voice.Started.SequenceEqual(["cloud-leaps.ogg"]), "the next animation is timed normally");
    }

    private static void CuesPlayInOrderWithoutOverlapping()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(95), 6.3);
        Check(rig.Voice.Started.SequenceEqual(["zero-rises.ogg"]), "the second cue waits for the first");

        rig.Voice.Last.Finish();
        rig.Update(ZeroRow(100), 6.7);
        Check(rig.Voice.Started.SequenceEqual(["zero-rises.ogg", "zero-fires.ogg"]), "then follows it");
    }

    private static void AftermathCuesPlayWhileTheRowIsStillCurrent()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(60), 4);
        rig.Voice.Last.Finish();
        rig.Update(ZeroRow(90), 6);
        rig.Voice.Last.Finish();
        rig.Update(ZeroRow(200, playing: false), 13);
        rig.Update(ZeroRow(300, playing: false), 20);
        Check(rig.Voice.Started.SequenceEqual(["zero-rises.ogg", "zero-fires.ogg", "smoke-clears.ogg"]),
            "the aftermath is told after the summoner is idle, while the row is still current");

        rig.Voice.Last.Finish();
        rig.Update(IdleAt(40), 21);
        Check(rig.History.HasHeard(0x830E) && !rig.History.HasHeard(0x030E),
            "every cue delivered marks the revision 2 key only");
    }

    private static void CuesTheAnimationNeverReachedDoNotBlockTheMark()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(60), 4);
        rig.Voice.Last.Finish();
        rig.Update(ZeroRow(90), 6);
        rig.Voice.Last.Finish();
        rig.Update(ZeroRow(150, playing: false), 10);
        rig.Update(IdleAt(160), 10.7); // this time the animation ended at tick 150
        rig.Update(IdleAt(170), 20);

        Check(rig.Voice.Started.Count == 2, "the cue at tick 300 was never reached and is not told late");
        Check(rig.History.HasHeard(0x830E), "the reached cues were all delivered, so it is heard");
    }

    private static void ADueCueMissingItsGraceLeavesTheAnimationUnheard()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.OtherDescriptionPlaying = true;
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(70), 4.7);
        rig.Update(IdleAt(80), 5.3);
        rig.Update(IdleAt(80), 8.2);
        rig.OtherDescriptionPlaying = false;
        rig.Update(IdleAt(80), 8.4);
        Check(rig.Voice.Started.Count == 0, "a cue is not told more than three seconds after its animation");
        Check(!rig.History.HasHeard(0x830E), "and the animation stays unheard for next time");

        var late = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        late.OtherDescriptionPlaying = true;
        late.Update(ZeroRow(0), 0);
        late.Update(ZeroRow(70), 4.7);
        late.Update(IdleAt(80), 5.3);
        late.OtherDescriptionPlaying = false;
        late.Update(IdleAt(80), 7.0);
        Check(late.Voice.Started.SequenceEqual(["zero-rises.ogg"]), "within the grace it is still told");
    }

    private static void AFailedCueLeavesTheWholeSequenceUnheard()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(60), 4);
        rig.Voice.Last.Fail();
        rig.Update(ZeroRow(90), 6);
        rig.Voice.Last.Finish();
        rig.Update(ZeroRow(300, playing: false), 15);
        rig.Voice.Last.Finish();
        rig.Update(IdleAt(0), 16);
        Check(rig.Voice.Started.Count == 3, "the rest of the sequence still plays");
        Check(!rig.History.HasHeard(0x830E), "but one failed cue means it was not all heard");
    }

    private static void UnreadableScansNeitherMoveNorStartCues()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(0), 0);
        rig.Update(null, 4);
        Check(rig.Voice.Started.Count == 0, "an unreadable scan starts nothing");
        rig.Update(ZeroRow(59), 4.1);
        Check(rig.Voice.Started.Count == 0, "and only the ticks the next read proves count");
        rig.Update(ZeroRow(60), 4.2);
        Check(rig.Voice.Started.Count == 1, "tick 60 plays the cue");
    }

    private static void TheRevisedDescriptionPlaysOnceDespiteTheOldHistory()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.LoadSave(1, 1);
        history.MarkHeard(0x1400); // Braver's old single description was heard earlier
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), history, StagedManifest);
        rig.PlayToEnd(Played(Limit, 0x00, 0x00) with { Tick = 0 }, 0);
        Check(rig.Voice.Started.SequenceEqual(["cloud-leaps.ogg"]), "the revised description is told");
        Check(history.HasHeard(0x1400) && history.HasHeard(0x9400), "the old history is kept beside the new");

        rig.Update(Played(Limit, 0x00, 0x00) with { Tick = 50 }, 60);
        Check(rig.Voice.Started.Count == 1, "and the revision is told once per save");
    }

    private static void EachTifaMoveKeepsItsOwnSchedule()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        var rush = BattleAnimationObservation.Playing(0, 1, Limit, 0x15, 0x62);
        var flip = BattleAnimationObservation.Playing(1, 1, Limit, 0x15, 0x63);
        rig.Update(rush with { Tick = 0 }, 0);
        rig.Update(flip with { Tick = 20 }, 1.3);
        rig.Voice.Last.Finish();
        rig.Update(flip with { Tick = 25 }, 1.7);
        rig.Voice.Last.Finish();
        rig.Update(IdleAt(40), 2.7);
        Check(rig.Voice.Started.SequenceEqual(["rush.ogg", "flip.ogg"]), "each move is told at its own start");
        Check(rig.History.HasHeard(0x9462) && rig.History.HasHeard(0x9463), "and each is heard");
    }

    // --- hardening: native anchors, pause, lost progress, frame multiplier -----------

    private const string AnchoredJson = """
        [
          {"key": "summon.knights", "name": "Knights of the Round", "revision": 2, "anchor": "summon", "cues": [
            {"atFrame": 0, "text": "Knights appear."},
            {"atFrame": 30, "text": "Knights strike."}]},
          {"key": "limit.cloud.omnislash", "name": "Cloud: Omnislash", "revision": 2, "cues": [
            {"atFrame": 60, "text": "Omnislash rush."}]}
        ]
        """;

    private const string AnchoredManifest = """
        {"entries": [
          {"text": "Knights appear.", "file": "knights-appear.ogg", "duration_seconds": 1.5},
          {"text": "Knights strike.", "file": "knights-strike.ogg", "duration_seconds": 1.5},
          {"text": "Omnislash rush.", "file": "omnislash.ogg", "duration_seconds": 1.5},
          {"text": "Zero rises.", "file": "zero-rises.ogg", "duration_seconds": 2.0},
          {"text": "Zero fires.", "file": "zero-fires.ogg", "duration_seconds": 2.0},
          {"text": "Smoke clears.", "file": "smoke-clears.ogg", "duration_seconds": 2.0}
        ]}
        """;

    private static BattleAnimationObservation KnightsRow(int tick, bool dispatcherWaiting, bool playing = true) =>
        (playing
            ? BattleAnimationObservation.Playing(2, 0, Summon, 0x0F, 0x000F)
            : BattleAnimationObservation.Current(2, 0, Summon, 0x0F, 0x000F))
        with { Tick = (byte)tick, SummonEffectWaiting = dispatcherWaiting };

    private static BattleAnimationObservation OmniRow(int tick, bool paused = false) =>
        BattleAnimationObservation.Playing(0, 0, Limit, 0x06, 0x0006) with { Tick = (byte)tick, Paused = paused };

    private static void TheReaderSeesTheSummonDispatcherInTheEffectTable()
    {
        // FUN_005C0E39 registers FUN_005C0E4B through FUN_005BEC50, which writes the guest
        // address into the 100-slot table at 0x00BF2858 (x64: 7FF702084D30 pushes the
        // guest constant 0x5C0E4B). Stage 1 releases the slot when the summon's own
        // sequence begins.
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Summon, effect: 0x0F, action: 0x0F, script: 0x3C));
        memory.StartCurrent();
        Check(Read(memory, out var before) && !before.SummonEffectWaiting, "no dispatcher registered yet");

        memory.UInt32(0x00BF2858 + (7 * 4), 0x005C0E4B);
        Check(Read(memory, out var waiting) && waiting.SummonEffectWaiting, "the dispatcher is waiting in slot 7");

        memory.UInt32(0x00BF2858 + (7 * 4), 0);
        Check(Read(memory, out var released) && !released.SummonEffectWaiting, "released when the summon begins");
    }

    private static void SummonAnchoredEntriesParseOnlyForSummons()
    {
        var log = new List<string>();
        var catalog = BattleAnimationNarrationCatalog.Parse("""
            [
              {"key": "summon.shiva", "revision": 2, "anchor": "summon", "cues": [{"atFrame": 0, "text": "a"}]},
              {"key": "limit.cloud.braver", "revision": 2, "anchor": "summon", "cues": [{"atFrame": 0, "text": "b"}]},
              {"key": "summon.ifrit", "revision": 2, "anchor": "effect", "cues": [{"atFrame": 0, "text": "c"}]},
              {"key": "summon.ramuh", "revision": 2, "anchor": "row", "cues": [{"atFrame": 0, "text": "d"}]}
            ]
            """, log.Add);
        Check(catalog.Count == 2, "a summon anchor on a summon and an explicit row anchor are accepted");
        Check(catalog.TryGet(Played(Summon, 0x01, 0x01), out var shiva) && shiva.Anchor == BattleAnimationAnchor.SummonSequence,
            "Shiva's cues count from its own sequence");
        Check(catalog.TryGet(Played(Summon, 0x03, 0x03), out var ramuh) && ramuh.Anchor == BattleAnimationAnchor.RowStart,
            "Ramuh's cues count from the row start");
        Check(log.Count(line => line.Contains("anchor", StringComparison.OrdinalIgnoreCase)) >= 2,
            "a summon anchor on a limit, and an unknown anchor, are rejected");
    }

    private static void SummonCuesCountFromTheSummonsOwnSequence()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(AnchoredJson), manifest: AnchoredManifest);
        rig.Update(KnightsRow(0, false), 0);
        rig.Update(KnightsRow(40, true), 2.7);  // casting, dispatcher registered
        rig.Update(KnightsRow(90, true), 6);    // still waiting: nothing is visible yet
        Check(rig.Voice.Started.Count == 0, "nothing is said while the summon is being called");

        rig.Update(KnightsRow(100, false), 6.7); // stage 1 released the slot: the summon sequence began
        Check(rig.Voice.Started.SequenceEqual(["knights-appear.ogg"]), "cue 0 at the sequence start");
        rig.Voice.Last.Finish();
        rig.Update(KnightsRow(129, false, playing: false), 8.6);
        Check(rig.Voice.Started.Count == 1, "29 ticks after the anchor is not 30");
        rig.Update(KnightsRow(130, false, playing: false), 8.7);
        Check(rig.Voice.Started.Count == 2, "30 ticks after the anchor plays the next cue");
        rig.Voice.Last.Finish();
        rig.Update(IdleAt(140), 9.4);
        Check(rig.History.HasHeard(0x830F), "the anchored schedule is heard once delivered");
    }

    private static void ASummonWhoseSequenceNeverBeganIsNotHeard()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(AnchoredJson), manifest: AnchoredManifest);
        rig.Update(KnightsRow(0, false), 0);
        rig.Update(KnightsRow(20, false), 1.3);
        rig.Update(IdleAt(30), 2);
        rig.Update(IdleAt(30), 10);
        Check(rig.Voice.Started.Count == 0, "without its anchor nothing is told");
        Check(!rig.History.HasHeard(0x830F), "and nothing is marked");
    }

    private static void NoAnimationIsHeardWhenNoCueWasReached()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(AnchoredJson), manifest: AnchoredManifest);
        rig.Update(OmniRow(0), 0);
        rig.Update(OmniRow(30), 2);
        rig.Update(IdleAt(35), 2.4); // the row ended at tick ~30; the first cue is at 60
        rig.Update(IdleAt(35), 10);
        Check(rig.Voice.Started.Count == 0, "the cue was never reached");
        Check(!rig.History.HasHeard(0x9406), "an animation none of whose cues were reached is not heard");
    }

    private static void LostNativeProgressAbandonsTheSchedule()
    {
        // A stall longer than the byte can be trusted across: the real progress is unknown,
        // so the remaining cues would be stale. The schedule is dropped, not shortened.
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(60), 4);
        rig.Voice.Last.Finish();
        rig.Update(ZeroRow(61), 4.1);
        rig.Update(ZeroRow(80), 20);          // 16 s without a readable scan
        rig.Update(IdleAt(90), 21);           // looks like a short animation that ended at tick 61
        rig.Update(IdleAt(10), 40);
        Check(rig.Voice.Started.SequenceEqual(["zero-rises.ogg"]), "no cue is told after the timeline was lost");
        Check(!rig.History.HasHeard(0x830E), "and a lost timeline is never a finished one");
    }

    private static void APausedBattlePausesTheRecordingWithoutRestartingIt()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(AnchoredJson), manifest: AnchoredManifest);
        rig.Update(OmniRow(0), 0);
        rig.Update(OmniRow(60), 4);
        var clip = rig.Voice.Last;
        rig.Update(OmniRow(62, paused: true), 4.2);
        Check(clip.PauseCalls.SequenceEqual([true]), "the recording is paused with the battle");
        rig.Update(OmniRow(90, paused: true), 6);
        rig.Update(OmniRow(95), 6.3);
        Check(clip.PauseCalls.SequenceEqual([true, false]), "and resumed with it");
        Check(rig.Voice.Started.Count == 1 && clip.Stops == 0, "never restarted or stopped");
        clip.Finish();
        rig.Update(IdleAt(100), 7);
        Check(rig.History.HasHeard(0x9406), "a paused and resumed cue that finished is delivered");
    }

    private static void ALongPauseDoesNotTripTheStuckDeviceWatchdog()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(AnchoredJson), manifest: AnchoredManifest);
        rig.Update(OmniRow(0), 0);
        rig.Update(OmniRow(60), 4);
        var clip = rig.Voice.Last;
        rig.Update(OmniRow(61, paused: true), 4.5);
        for (var second = 5; second <= 125; second += 5)
        {
            rig.Update(OmniRow(61, paused: true), second);
        }

        Check(clip.Stops == 0 && rig.Narration.IsPlaying, "two minutes paused is not a stuck device");
        rig.Update(OmniRow(62), 126);
        clip.Finish();
        rig.Update(IdleAt(70), 127);
        Check(rig.History.HasHeard(0x9406), "the cue completes after the pause");
    }

    private static void ARecordingThatCannotPauseIsStoppedNotRunAhead()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(AnchoredJson), manifest: AnchoredManifest)
        {
            Voice = { Pauseable = false },
        };
        rig.Update(OmniRow(0), 0);
        rig.Update(OmniRow(60), 4);
        rig.Update(OmniRow(62, paused: true), 4.2);
        Check(rig.Voice.Last.Stops == 1 && !rig.Narration.IsPlaying,
            "a recording that refuses to pause is stopped rather than describing a frozen battle");
        rig.Update(OmniRow(70), 5);
        rig.Update(IdleAt(80), 6);
        Check(!rig.History.HasHeard(0x9406), "and the animation is told again next time");
    }

    private static void StopAndLoadWhilePausedLeaveTheAnimationUnheard()
    {
        var stopped = new Rig(BattleAnimationNarrationCatalog.Parse(AnchoredJson), manifest: AnchoredManifest);
        stopped.Update(OmniRow(0), 0);
        stopped.Update(OmniRow(60), 4);
        stopped.Update(OmniRow(61, paused: true), 4.2);
        stopped.Narration.Stop("window lost the foreground");
        Check(stopped.Voice.Last.Stops == 1 && !stopped.Narration.IsPlaying, "a paused cue can be stopped");
        stopped.Update(IdleAt(70), 6);
        Check(!stopped.History.HasHeard(0x9406), "and is not heard");

        var history = new FieldAreaDescriptionHistory(path: null);
        history.LoadSave(1, 1);
        var loaded = new Rig(BattleAnimationNarrationCatalog.Parse(AnchoredJson), history, AnchoredManifest);
        loaded.Update(OmniRow(0), 0);
        loaded.Update(OmniRow(60), 4);
        loaded.Update(OmniRow(61, paused: true), 4.2);
        history.LoadSave(1, 2);
        loaded.Update(OmniRow(61, paused: true), 4.4);
        Check(loaded.Voice.Last.Stops == 1 && !loaded.Narration.IsPlaying, "a load stops a paused cue");
        history.LoadSave(1, 1);
        Check(!history.HasHeard(0x9406), "and the old save is not marked");
    }

    private static void NoCueStartsWhileTheBattleIsPaused()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest);
        rig.OtherDescriptionPlaying = true;
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(60), 4);          // due, but the voice is busy
        rig.Update(ZeroRow(61, paused: true), 4.2);
        rig.OtherDescriptionPlaying = false;
        rig.Update(ZeroRow(62, paused: true), 4.4);
        Check(rig.Voice.Started.Count == 0, "a frozen battle gets no new description");
        rig.Update(ZeroRow(63), 4.6);
        Check(rig.Voice.Started.SequenceEqual(["zero-rises.ogg"]), "it starts once the battle moves again");
    }

    private static void FasterBattleFrameRatesScaleTheCueTicks()
    {
        // FFNx 30 fps battle multiplies every script wait by battle_frame_multiplier (2);
        // cue ticks stay authored at vanilla 15 a second and are scaled here.
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest, ticksPerCueFrame: 2);
        rig.Update(ZeroRow(0), 0);
        rig.Update(ZeroRow(119), 4);
        Check(rig.Voice.Started.Count == 0, "60 vanilla ticks are 120 at 30 fps");
        rig.Update(ZeroRow(120), 4.1);
        Check(rig.Voice.Started.SequenceEqual(["zero-rises.ogg"]), "and play at 120");
    }

    private static void FasterBattleClocksRejectGapsThatCouldWrap()
    {
        var thirty = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest, ticksPerCueFrame: 2);
        thirty.Update(ZeroRow(0), 0);
        thirty.Update(ZeroRow(210), 7);
        Check(thirty.Voice.Started.Count == 0, "30 fps uses the shorter six-second safety bound");

        var sixty = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest, ticksPerCueFrame: 4);
        sixty.Update(ZeroRow(0), 0);
        sixty.Update(ZeroRow(44), 5); // 300 real ticks: a hidden full wrap.
        sixty.Update(ZeroRow(164), 7);
        sixty.Update(ZeroRow(242), 8.3);
        Check(sixty.Voice.Started.Count == 0, "a hidden wrap at 60 fps abandons the old schedule");

        var readable = new Rig(BattleAnimationNarrationCatalog.Parse(StagedJson), manifest: StagedManifest, ticksPerCueFrame: 4);
        readable.Update(ZeroRow(0), 0);
        readable.Update(ZeroRow(174), 2.9);
        readable.Update(ZeroRow(240), 4);
        Check(readable.Voice.Started.SequenceEqual(["zero-rises.ogg"]), "bounded gaps still count all 240 ticks at 60 fps");
    }

    private static void TheFfnxBattleFrameMultiplierComesFromItsConfig()
    {
        Check(BattleAnimationNarrationRuntime.BattleFrameMultiplierFromFfnxConfig(null) == 1, "no FFNx is vanilla");
        Check(BattleAnimationNarrationRuntime.BattleFrameMultiplierFromFfnxConfig("# x\nff7_fps_limiter = 1\n") == 1, "limiter 1 is 15 fps");
        Check(BattleAnimationNarrationRuntime.BattleFrameMultiplierFromFfnxConfig("ff7_fps_limiter = 0") == 1, "limiter 0 is original");
        Check(BattleAnimationNarrationRuntime.BattleFrameMultiplierFromFfnxConfig("ff7_fps_limiter=2 # 30 FPS") == 2, "30 fps battle doubles");
        Check(BattleAnimationNarrationRuntime.BattleFrameMultiplierFromFfnxConfig("  ff7_fps_limiter = 3") == 4, "60 fps quadruples");
        Check(BattleAnimationNarrationRuntime.BattleFrameMultiplierFromFfnxConfig("# ff7_fps_limiter = 3\nff8_fps_limiter = 3") == 1,
            "comments and the FF8 setting do not count");
        Check(BattleAnimationNarrationRuntime.BattleFrameMultiplierFromFfnxConfig("ff7_fps_limiter = 7") is null,
            "an unknown mode is refused rather than guessed");
    }

    private const string StagedManifest = """
        {"entries": [
          {"text": "Zero rises.", "file": "zero-rises.ogg", "duration_seconds": 2.0},
          {"text": "Zero fires.", "file": "zero-fires.ogg", "duration_seconds": 2.0},
          {"text": "Smoke clears.", "file": "smoke-clears.ogg", "duration_seconds": 2.0},
          {"text": "Cloud leaps.", "file": "cloud-leaps.ogg", "duration_seconds": 2.0},
          {"text": "Rush.", "file": "rush.ogg", "duration_seconds": 1.0},
          {"text": "Flip.", "file": "flip.ogg", "duration_seconds": 1.0}
        ]}
        """;

    // --- helpers ----------------------------------------------------------------------

    // --- the summon banner's name and the casting opening ------------------------------

    private static readonly string[] SummonAttackNames = ["DeathBlow!!", "Diamond Dust", "Hellfire"];

    /// <summary>
    /// FUN_00419379 appends a kernel text section to the blob at 0x009A13C8, stores its
    /// offset at 0x009A7FC8 + section*2 and counts it in 0x009A8124; a section begins with
    /// its entries' offsets, and each string ends in 0xFF.
    /// </summary>
    private static void KernelText(NativeBattle memory, int section, uint sectionOffset, params string[] entries)
    {
        memory.UInt32(0x009A8124, 18);
        memory.UInt16(0x009A7FC8 + (uint)(section * 2), (ushort)sectionOffset);
        var start = 0x009A13C8u + sectionOffset;
        var text = (uint)(entries.Length * 2);
        for (var i = 0; i < entries.Length; i++)
        {
            memory.UInt16(start + (uint)(i * 2), (ushort)text);
            foreach (var character in entries[i])
            {
                memory.Byte(start + text++, (byte)(character - 0x20));
            }

            memory.Byte(start + text++, 0xFF);
        }
    }

    /// <summary>FUN_005BEC50(FUN_0042782A), then the lifetime and delay opcode 0x0A/0x5C set.</summary>
    private static void Banner(NativeBattle memory, int slot, ushort delay, ushort lifetime)
    {
        memory.UInt32(0x00BF2858 + (uint)(slot * 4), 0x0042782A);
        memory.UInt16(0x00BFB71C + (uint)(slot * 0x20), lifetime);
        memory.UInt16(0x00BFB71E + (uint)(slot * 0x20), delay);
    }

    private static void TheReaderReadsTheDisplayedSummonName()
    {
        // While FUN_0042782A's delay is spent and its lifetime is not, it hands FUN_006D71FA
        // the active actor's command (0xBE119B) and action (0xBF23FE); FUN_006D1CC0 case
        // 0x16 draws FUN_00419457(0x11, action) for command 3.
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 1, command: Summon, effect: 0x01, action: 0x0001, script: 0x3C));
        memory.StartCurrent();
        KernelText(memory, 17, 0x5000, SummonAttackNames);
        Check(Read(memory, out var casting) && !casting.BannerVisible && casting.BannerText is null, "no banner yet");

        Banner(memory, 9, delay: 3, lifetime: 20);
        Check(Read(memory, out var delayed) && !delayed.BannerVisible && delayed.BannerText is null,
            "a banner still in its delay is not on screen");

        Banner(memory, 9, delay: 0, lifetime: 20);
        Check(Read(memory, out var shown) && shown.BannerVisible && shown.BannerText == "Diamond Dust",
            "the summon attack name the banner shows");

        Banner(memory, 9, delay: 0, lifetime: 0);
        Check(Read(memory, out var over) && !over.BannerVisible && over.BannerText is null, "a spent banner is gone");
    }

    private static void ASubstitutedSummonShowsTheKernelAttackName()
    {
        // FUN_0041963C category 6: an action of 0x10 or more reads the magic names (section
        // 9) at the raw action, as a Slots or substituted summon's absolute attack does.
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Summon, effect: 0x01, action: 0x0038, script: 0x3C));
        memory.StartCurrent();
        KernelText(memory, 17, 0x5000, SummonAttackNames);
        KernelText(memory, 9, 0x1000, [.. Enumerable.Repeat("-", 0x38), "Shiva"]);
        Banner(memory, 0, delay: 0, lifetime: 12);
        Check(Read(memory, out var shown) && shown.BannerText == "Shiva", "the absolute attack's own name");
    }

    private static void AnUnusableTextTableShowsNoName()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Summon, effect: 0x02, action: 0x0002, script: 0x3C));
        memory.StartCurrent();
        Banner(memory, 0, delay: 0, lifetime: 12);
        KernelText(memory, 17, 0x5000, SummonAttackNames);
        memory.UInt32(0x009A8124, 9);
        Check(Read(memory, out var unloaded) && unloaded.BannerVisible && unloaded.BannerText is null,
            "a section not loaded names nothing");

        memory.UInt32(0x009A8124, 18);
        memory.UInt16(0x009A7FC8 + 34, 0x7000);
        Check(Read(memory, out var outside) && outside.BannerVisible && outside.BannerText is null,
            "an offset outside the text blob names nothing");

        // One entry only; what lies where entry 2's offset would be points at real text.
        KernelText(memory, 17, 0x5000, "DeathBlow!!");
        memory.UInt16(0x009A13C8 + 0x5000 + 4, 0x0100);
        foreach (var (character, i) in "Hellfire".Select((character, i) => (character, i)))
        {
            memory.Byte(0x009A13C8 + 0x5100 + (uint)i, (byte)(character - 0x20));
        }

        memory.Byte(0x009A13C8 + 0x5100 + 8, 0xFF);
        Check(Read(memory, out var beyond) && beyond.BannerText is null, "an entry the section does not have names nothing");
    }

    private static void AnUnreadableBannerIsUnknownNotAbsent()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Summon, effect: 0x01, action: 0x0001, script: 0x3C));
        memory.StartCurrent();
        KernelText(memory, 17, 0x5000, SummonAttackNames);
        Banner(memory, 4, delay: 0, lifetime: 12);
        memory.Unreadable(0x00BFB71E + (4 * 0x20));
        Check(!Read(memory, out _), "an unreadable banner state is no observation");
    }

    private static BattleAnimationObservation CastRow(int tick, bool banner, string? title = "Diamond Dust") =>
        BattleAnimationObservation.Playing(3, 1, Summon, 0x01, 0x0001)
            with { Tick = (byte)tick, BannerVisible = banner, BannerText = banner ? title : null };

    private static void TheBannerNameIsSpokenOncePerCast()
    {
        var spoken = new List<string>();
        var titles = new BattleSummonTitleSpeech();
        bool Speak(string text)
        {
            spoken.Add(text);
            return true;
        }

        titles.Update(CastRow(0, banner: false), Speak);
        titles.Update(CastRow(7, banner: true), Speak);
        titles.Update(CastRow(9, banner: true), Speak);
        titles.Update(null, Speak);
        titles.Update(CastRow(12, banner: true), Speak);
        titles.Update(CastRow(30, banner: false), Speak);
        titles.Update(CastRow(31, banner: true), Speak);
        Check(spoken.SequenceEqual(["Diamond Dust"]), "said once for the cast, however long it shows");

        titles.Update(IdleAt(90), Speak);
        titles.Update(CastRow(100, banner: false), Speak);
        titles.Update(CastRow(107, banner: true), Speak);
        Check(spoken.SequenceEqual(["Diamond Dust", "Diamond Dust"]), "and again on the next cast, heard or not");

        // W-Summon: the next summon row follows at once, with no idle scan between.
        titles.Update(CastRow(120, banner: true) with { EventIndex = 4 }, Speak);
        Check(spoken.Count == 3, "a second row casting the same summon is its own cast");
    }

    private static void ARefusedBannerNameIsOfferedAgainOnlyWhileShown()
    {
        var offered = 0;
        var accept = false;
        var titles = new BattleSummonTitleSpeech();
        bool Speak(string text)
        {
            offered++;
            return accept;
        }

        titles.Update(CastRow(7, banner: true), Speak);
        titles.Update(CastRow(8, banner: true), Speak);
        Check(offered == 2, "offered again while it shows");
        titles.Update(CastRow(40, banner: false), Speak);
        accept = true;
        titles.Update(CastRow(41, banner: false), Speak);
        Check(offered == 2, "never after it has gone");
    }

    private static void OnlyASummonsBannerNameIsSpoken()
    {
        var spoken = new List<string>();
        var titles = new BattleSummonTitleSpeech();
        titles.Update(OmniRow(5) with { BannerVisible = true, BannerText = "Omnislash" }, text =>
        {
            spoken.Add(text);
            return true;
        });
        titles.Update(CastRow(7, banner: true, title: null), text =>
        {
            spoken.Add(text);
            return true;
        });
        Check(spoken.Count == 0, "other banners and unresolved names say nothing here");
    }

    private const string OpeningJson = """
        [
          {"key": "opening.summon", "name": "Summoning", "revision": 2, "anchor": "banner", "cues": [
            {"atFrame": 12, "text": "Orbs circle."}]},
          {"key": "summon.knights", "name": "Knights of the Round", "revision": 2, "anchor": "summon", "cues": [
            {"atFrame": 15, "text": "Knights appear."}]},
          {"key": "limit.cloud.omnislash", "name": "Cloud: Omnislash", "revision": 2, "cues": [
            {"atFrame": 60, "text": "Omnislash rush."}]}
        ]
        """;

    private const string OpeningManifest = """
        {"entries": [
          {"text": "Orbs circle.", "file": "orbs.ogg", "duration_seconds": 3.0},
          {"text": "Knights appear.", "file": "knights-appear.ogg", "duration_seconds": 1.5},
          {"text": "Omnislash rush.", "file": "omnislash.ogg", "duration_seconds": 1.5}
        ]}
        """;

    private const int OpeningKey = 0x8200;
    private const int KnightsKey = 0x830F;

    private static BattleAnimationObservation KnightsCast(
        int tick, bool banner = false, bool dispatcherWaiting = false, bool paused = false) =>
        BattleAnimationObservation.Playing(2, 0, Summon, 0x0F, 0x000F) with
        {
            Tick = (byte)tick,
            Paused = paused,
            BannerVisible = banner,
            BannerText = banner ? "Ultimate End" : null,
            SummonEffectWaiting = dispatcherWaiting,
        };

    /// <summary>A Knights cast: banner at tick 7, dispatcher 50-65, the summon begins at 66.</summary>
    private static void CastUntilTheSummonBegins(Rig rig, double from = 0)
    {
        rig.Update(KnightsCast(0), from);
        rig.Update(KnightsCast(7, banner: true), from + 0.5);
        rig.Update(KnightsCast(19, banner: true), from + 1.3);
        rig.Update(KnightsCast(50, dispatcherWaiting: true), from + 3.3);
        rig.Update(KnightsCast(66), from + 4.4);
    }

    private static void TheOpeningParsesOnlyWithTheBannerAnchor()
    {
        var logged = new List<string>();
        var catalog = BattleAnimationNarrationCatalog.Parse(OpeningJson, logged.Add);
        Check(catalog.Count == 2 && catalog.SummonOpening is { } opening &&
              opening.Anchor == BattleAnimationAnchor.Banner && opening.HistoryKey == OpeningKey &&
              opening.Cues.Single().AtFrame == 12 && logged.Count == 0,
            "the opening parses apart from the animations, under 0x0200 + 0x8000");
        Check(!BattleAnimationIdentities.All.Any(identity => identity.Key == "opening.summon") &&
              BattleAnimationIdentities.All.All(identity => identity.HistoryKey != 0x0200),
            "it is not one of the native animations and shares no key with them");

        const string wrong = """
            [
              {"key": "opening.summon", "revision": 2, "cues": [{"atFrame": 12, "text": "Orbs."}]},
              {"key": "opening.summon", "revision": 2, "anchor": "summon", "cues": [{"atFrame": 12, "text": "Orbs."}]},
              {"key": "summon.knights", "revision": 2, "anchor": "banner", "cues": [{"atFrame": 0, "text": "Knights."}]}
            ]
            """;
        logged.Clear();
        var rejected = BattleAnimationNarrationCatalog.Parse(wrong, logged.Add);
        Check(rejected.SummonOpening is null && rejected.Count == 0 && logged.Count == 3,
            "without the banner anchor, or the banner anchor elsewhere, the entry is skipped");
    }

    private static void TheOpeningCountsFromTheBannerUnderItsOwnKey()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), manifest: OpeningManifest);
        rig.Update(KnightsCast(0), 0);
        rig.Update(KnightsCast(3), 0.2);
        rig.Update(KnightsCast(7, banner: true), 0.5);
        rig.Update(KnightsCast(18, banner: true), 1.2);
        Check(rig.Voice.Started.Count == 0, "11 ticks after the banner is not 12");
        rig.Update(KnightsCast(19, banner: true), 1.3);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg"]), "12 ticks after the banner the opening starts");
        rig.Voice.Last.Finish();
        rig.Update(KnightsCast(50, dispatcherWaiting: true), 3.3);
        rig.Update(KnightsCast(66), 4.4);
        rig.Update(KnightsCast(80), 5.3);
        Check(rig.Voice.Started.Count == 1, "the summon's own cue keeps its anchor: 14 ticks after it is not 15");
        rig.Update(KnightsCast(81), 5.4);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg", "knights-appear.ogg"]), "and starts on its own tick");
        rig.Voice.Last.Finish();
        rig.Update(IdleAt(90), 6);
        Check(rig.History.HasHeard(OpeningKey) && rig.History.HasHeard(KnightsKey), "each is heard under its own key");

        rig.Update(KnightsCast(0), 20);
        rig.Update(KnightsCast(7, banner: true), 20.5);
        rig.Update(KnightsCast(40, banner: true), 22.7);
        Check(rig.Voice.Started.Count == 2, "once heard, the opening stays silent for the save");
    }

    private static void TheOpeningStillPlaysAfterTheSummonWasHeard()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.TryMarkHeard(KnightsKey, history.PlaythroughRevision);
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), history, OpeningManifest);
        CastUntilTheSummonBegins(rig);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg"]), "the opening plays; the heard summon does not replay");
        rig.Voice.Last.Finish();
        rig.Update(KnightsCast(120), 8);
        rig.Update(IdleAt(130), 9);
        Check(rig.Voice.Started.Count == 1 && rig.History.HasHeard(OpeningKey), "and the opening is heard");
    }

    private static void TheSummonStillPlaysAfterTheOpeningWasHeard()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.TryMarkHeard(OpeningKey, history.PlaythroughRevision);
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), history, OpeningManifest);
        CastUntilTheSummonBegins(rig);
        rig.Update(KnightsCast(81), 5.4);
        Check(rig.Voice.Started.SequenceEqual(["knights-appear.ogg"]), "only the summon's own description");
    }

    private static void ABannerAlreadyShowingLeavesTheOpeningUntimed()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), manifest: OpeningManifest);
        rig.Update(KnightsCast(10, banner: true), 0);
        rig.Update(KnightsCast(30, banner: true), 1.3);
        rig.Update(KnightsCast(50, dispatcherWaiting: true), 2.7);
        rig.Update(KnightsCast(66), 3.7);
        rig.Update(KnightsCast(81), 4.7);
        Check(rig.Voice.Started.SequenceEqual(["knights-appear.ogg"]), "no opening from a banner of unknown age");
        rig.Voice.Last.Finish();
        rig.Update(IdleAt(90), 5.3);
        Check(!rig.History.HasHeard(OpeningKey) && rig.History.HasHeard(KnightsKey), "and it is kept for a later summon");
    }

    private static void TheOpeningWaitsForTheScreenReader()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), manifest: OpeningManifest)
        {
            ScreenReaderSpeaking = true,
        };
        rig.Update(KnightsCast(0), 0);
        rig.Update(KnightsCast(7, banner: true), 0.5);
        rig.Update(KnightsCast(20, banner: true), 1.3);
        Check(rig.Voice.Started.Count == 0, "the opening waits while the banner's name is spoken");
        rig.ScreenReaderSpeaking = null;
        rig.Update(KnightsCast(22, banner: true), 1.5);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg"]), "a speaker that cannot say does not hold it back");
    }

    private static void AnOpeningThatMissesTheCastIsDropped()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), manifest: OpeningManifest)
        {
            ScreenReaderSpeaking = true,
        };
        CastUntilTheSummonBegins(rig);
        rig.ScreenReaderSpeaking = false;
        rig.Update(KnightsCast(70), 4.7);
        Check(rig.Voice.Started.Count == 0, "once the summon has begun, the casting is not described");
        rig.Update(KnightsCast(81), 5.4);
        Check(rig.Voice.Started.SequenceEqual(["knights-appear.ogg"]), "the summon's cue is on time");
        rig.Voice.Last.Finish();
        rig.Update(IdleAt(90), 6);
        Check(!rig.History.HasHeard(OpeningKey) && rig.History.HasHeard(KnightsKey), "the opening stays unheard");
    }

    private static void ADueSummonCueStopsTheOpening()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), manifest: OpeningManifest);
        CastUntilTheSummonBegins(rig);
        var opening = rig.Voice.Created.Single();
        Check(opening.File == "orbs.ogg" && opening.IsPlaying, "the opening is still playing when the summon begins");
        rig.Update(KnightsCast(80), 5.3);
        Check(opening.Stops == 0, "it may run until a summon cue is due");
        rig.Update(KnightsCast(81), 5.4);
        Check(opening.Stops == 1 && rig.Voice.Started.SequenceEqual(["orbs.ogg", "knights-appear.ogg"]),
            "the due cue stops it and starts on its own tick");
        rig.Voice.Last.Finish();
        rig.Update(IdleAt(90), 6);
        Check(!rig.History.HasHeard(OpeningKey) && rig.History.HasHeard(KnightsKey), "the cut-off opening is not heard");
    }

    private static void AnOpeningPausesAndStopsWithTheBattle()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), manifest: OpeningManifest);
        rig.Update(KnightsCast(0), 0);
        rig.Update(KnightsCast(7, banner: true), 0.5);
        rig.Update(KnightsCast(19, banner: true), 1.3);
        var opening = rig.Voice.Last;
        rig.Update(KnightsCast(20, banner: true, paused: true), 1.4);
        Check(opening.PauseCalls.SequenceEqual([true]), "paused with the battle");
        rig.Narration.Stop("load");
        Check(opening.Stops == 1, "a stop ends it");
        rig.Update(IdleAt(30), 3);
        Check(!rig.History.HasHeard(OpeningKey), "and it is not heard");
    }

    private static void OnlyASummonCastOpensTheOpening()
    {
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), manifest: OpeningManifest);
        rig.Update(OmniRow(0), 0);
        rig.Update(OmniRow(7) with { BannerVisible = true, BannerText = "Omnislash" }, 0.5);
        rig.Update(OmniRow(30), 2);
        Check(rig.Voice.Started.Count == 0, "a limit's banner opens no summon opening");
        rig.Update(BattleAnimationObservation.Playing(2, 5, Summon, 0x0F, 0x000F), 3);
        rig.Update(BattleAnimationObservation.Playing(2, 5, Summon, 0x0F, 0x000F) with { Tick = 7, BannerVisible = true }, 3.5);
        rig.Update(BattleAnimationObservation.Playing(2, 5, Summon, 0x0F, 0x000F) with { Tick = 30, BannerVisible = true }, 5);
        Check(rig.Voice.Started.Count == 0, "nor does an enemy's");
    }

    private static void TheBannerNameDoesNotDependOnTheDescriptions()
    {
        // No catalog installed: the descriptions are unavailable, the banner is still read.
        var directory = Path.Combine(Path.GetTempPath(), "ff7-banner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var memory = new NativeBattle();
            memory.Build(Row(attacker: 1, command: Summon, effect: 0x02, action: 0x0002, script: 0x3C));
            memory.StartCurrent();
            KernelText(memory, 17, 0x5000, SummonAttackNames);
            Banner(memory, 3, delay: 0, lifetime: 20);
            var spoken = new List<string>();
            using var runtime = BattleAnimationNarrationRuntime.Create(
                memory, new FieldAreaDescriptionHistory(path: null), new Ff7.Accessibility.Core.AccessibilityConfig(),
                directory, _ => { }, text =>
                {
                    spoken.Add(text);
                    return true;
                }, () => false);
            Check(runtime is not null, "the runtime exists without descriptions");
            runtime!.Tick(T0, enabledAndFocused: true, titlesEnabledAndFocused: false);
            Check(spoken.Count == 0, "battle message speech off: no name");
            runtime.Tick(T0.AddSeconds(0.1), enabledAndFocused: false, titlesEnabledAndFocused: true);
            Check(spoken.SequenceEqual(["Hellfire"]), "descriptions off or missing: the name is still spoken");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private const string LiveOpeningJson = """
        [
          {"key": "opening.summon", "name": "Summoning", "revision": 2, "anchor": "banner", "cues": [
            {"atFrame": 12, "text": "Orbs circle."}]},
          {"key": "summon.shiva", "name": "Shiva", "revision": 2, "anchor": "summon", "cues": [
            {"atFrame": 30, "text": "Shiva rises."}]}
        ]
        """;

    private const string LiveOpeningManifest = """
        {"entries": [
          {"text": "Orbs circle.", "file": "orbs.ogg", "duration_seconds": 2.92},
          {"text": "Shiva rises.", "file": "shiva-rises.ogg", "duration_seconds": 5.18}
        ]}
        """;

    /// <summary>
    /// Replays root's read-only live capture 24500 (legacy ff7_en.exe with FFNx 1.24.3.0,
    /// Cloud casting Shiva): row 00 01 01 03 00 1F 01 00 starts at tick 174; at 188
    /// FUN_0042782A takes effect slot 0 with lifetime 35 counting down to 0 at 223 while its
    /// delay word reads 0xFFFF throughout (0x42782A is a detour in that process); the
    /// summon dispatcher 0x5C0E4B holds slot 0 from 224 and releases it at 239. The
    /// loaded kernel text table 0x9A7FC8 and its count 0x9A8124 read all zero.
    /// </summary>
    private static void ReplayLiveShivaCast(NativeBattle memory, Rig rig, int from, int to)
    {
        for (var tick = from; tick <= to; tick++)
        {
            memory.Byte(0x00BFD0E4, (byte)tick);
            switch (tick)
            {
                case >= 188 and <= 223:
                    Banner(memory, 0, delay: 0xFFFF, lifetime: (ushort)(223 - tick));
                    break;
                case >= 224 and <= 238:
                    memory.UInt32(0x00BF2858, 0x005C0E4B);
                    memory.UInt32(0x00BFB718, 0);
                    memory.UInt32(0x00BFB71C, 0);
                    break;
                default:
                    memory.UInt32(0x00BF2858, 0);
                    break;
            }

            rig.ReadAndUpdate(memory, (tick - 174) / 15.0);
        }
    }

    private static void TheLiveLegacyBannerAnchorsTheOpening()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Summon, effect: 0x01, action: 0x0001, script: 0x1F));
        memory.StartCurrent();
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(LiveOpeningJson), manifest: LiveOpeningManifest);

        ReplayLiveShivaCast(memory, rig, 174, 199);
        Check(rig.Voice.Started.Count == 0, "11 ticks after the live banner is not 12");
        ReplayLiveShivaCast(memory, rig, 200, 200);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg"]), "the opening starts 12 ticks after the live banner appears");
        ReplayLiveShivaCast(memory, rig, 201, 244);
        rig.Voice.Last.Finish();
        ReplayLiveShivaCast(memory, rig, 245, 268);
        Check(rig.Voice.Started.Count == 1, "the summon's own cue keeps its anchor: 29 ticks after 239 is not 30");
        ReplayLiveShivaCast(memory, rig, 269, 269);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg", "shiva-rises.ogg"]), "and starts on its own tick");
        rig.Voice.Last.Finish();
        memory.FinishCurrent();
        memory.EndQueue();
        rig.ReadAndUpdate(memory, 8);
        Check(rig.History.HasHeard(0x8200) && rig.History.HasHeard(0x8301), "both are heard under their own keys");
    }

    private static void TheReaderSeesTheLiveLegacyBanner()
    {
        var memory = new NativeBattle();
        memory.Build(Row(attacker: 0, command: Summon, effect: 0x01, action: 0x0001, script: 0x1F));
        memory.StartCurrent();
        Banner(memory, 0, delay: 0xFFFF, lifetime: 35);
        Check(Read(memory, out var shown) && shown.BannerVisible && shown.BannerText is null &&
              shown.BannerCommand == Summon && shown.BannerAction == 1,
            "a detoured banner with its delay word at -1 is on screen; no native text, but its command and action");

        Banner(memory, 0, delay: 0xFFFF, lifetime: 0);
        Check(Read(memory, out var spent) && !spent.BannerVisible && spent.BannerAction == 0, "its lifetime spent, it is gone");

        Banner(memory, 0, delay: 2, lifetime: 35);
        Check(Read(memory, out var waiting) && !waiting.BannerVisible, "a native banner still in its delay is not shown");
    }

    private static void WithoutTheNativeTextTheBannerNameComesFromTheKernel()
    {
        var asked = new List<ushort>();
        var spoken = new List<string>();
        var titles = new BattleSummonTitleSpeech(action =>
        {
            asked.Add(action);
            return action == 1 ? "Diamond Dust" : null;
        });
        var legacy = BattleAnimationObservation.Playing(0, 0, Summon, 0x01, 0x0001) with
        {
            Tick = 188,
            BannerVisible = true,
            BannerCommand = Summon,
            BannerAction = 1,
        };
        titles.Update(legacy, text =>
        {
            spoken.Add(text);
            return true;
        });
        titles.Update(legacy with { Tick = 189 }, text =>
        {
            spoken.Add(text);
            return true;
        });
        Check(spoken.SequenceEqual(["Diamond Dust"]) && asked.SequenceEqual<ushort>([1, 1]),
            "the banner's own action names it once");

        spoken.Clear();
        titles.Reset();
        titles.Update(legacy with { BannerText = "Hellfire" }, text =>
        {
            spoken.Add(text);
            return true;
        });
        titles.Update(legacy with { EventIndex = 5, BannerCommand = 2 }, text =>
        {
            spoken.Add(text);
            return true;
        });
        Check(spoken.SequenceEqual(["Hellfire"]), "native text wins, and another command's banner is not named as a summon");
    }

    private static Kernel2TextDatabase? GameKernelText()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = @"C:\Games\Final Fantasy VII\workingdir";
        }

        return Directory.Exists(Path.Combine(root, "data")) ? Kernel2TextDatabase.TryCreate(root) : null;
    }

    private static void TheKernelNamesASummonBannerAsTheRendererDoes()
    {
        if (GameKernelText() is not { } kernel)
        {
            Console.WriteLine("SKIP TheKernelNamesASummonBannerAsTheRendererDoes: no FFVII data root");
            return;
        }

        Check(kernel.ResolveSummonBannerText(0) == "DeathBlow!!" && kernel.ResolveSummonBannerText(1) == "Diamond Dust" &&
              kernel.ResolveSummonBannerText(2) == "Hellfire" && kernel.ResolveSummonBannerText(15) == "Ultimate End",
            "actions below 0x10 name the summon attack (section 17)");
        Check(kernel.ResolveSummonBannerText(0x38) == "Choco/Mog" && kernel.ResolveSummonBannerText(0x39) == "Shiva",
            "an absolute attack (kernel 0x38 Choco/Mog, 0x39 Shiva) names the magic (section 9)");
    }

    private static void TheLiveLegacyCastSpeaksItsBannerName()
    {
        if (GameKernelText() is not { } kernel)
        {
            Console.WriteLine("SKIP TheLiveLegacyCastSpeaksItsBannerName: no FFVII data root");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "ff7-live-banner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var memory = new NativeBattle();
            memory.Build(Row(attacker: 0, command: Summon, effect: 0x01, action: 0x0001, script: 0x1F));
            memory.StartCurrent();
            var spoken = new List<string>();
            using var runtime = BattleAnimationNarrationRuntime.Create(
                memory, new FieldAreaDescriptionHistory(path: null), new Ff7.Accessibility.Core.AccessibilityConfig(),
                directory, _ => { }, text =>
                {
                    spoken.Add(text);
                    return true;
                }, () => false, speechIsPlaying: null, kernelText: kernel);
            for (var tick = 174; tick <= 230; tick++)
            {
                memory.Byte(0x00BFD0E4, (byte)tick);
                if (tick is >= 188 and <= 223)
                {
                    Banner(memory, 0, delay: 0xFFFF, lifetime: (ushort)(223 - tick));
                }
                else
                {
                    memory.UInt32(0x00BF2858, 0);
                }

                runtime!.Tick(T0.AddSeconds((tick - 174) / 15.0), enabledAndFocused: true, titlesEnabledAndFocused: true);
                if (tick == 187)
                {
                    Check(spoken.Count == 0, "nothing before the banner shows");
                }
            }

            Check(spoken.SequenceEqual(["Diamond Dust"]), "the live banner's name is spoken once, from the game's kernel text");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void FinishIfPlaying(Rig rig)
    {
        if (rig.Narration.IsPlaying)
        {
            rig.Voice.Last.Finish();
        }
    }

    private static int Key(string key) =>
        BattleAnimationIdentities.TryGetByKey(key, out var identity)
            ? identity.HistoryKey
            : throw new InvalidOperationException("Battle narration: unknown key " + key);

    private static BattleAnimationObservation Played(byte command, ushort action, byte effect, byte index = 0) =>
        BattleAnimationObservation.Playing(index, 0, command, effect, action);

    private static bool Read(NativeBattle memory, out BattleAnimationObservation observation) =>
        new BattleAnimationStateReader(memory).TryRead(out observation);

    private static BattleAnimationNarrationCatalog TifaCatalog() => new(
        Enumerable.Range(0, 7).Select(move => (
            Key: new[] { "beat_rush", "somersault", "waterkick", "meteodrive", "dolphin_blow", "meteor_strike", "final_heaven" }
                .Select(name => "limit.tifa." + name).ElementAt(move),
            Name: "Tifa move " + move,
            Text: "Tifa move " + (0x62 + move).ToString("X2", System.Globalization.CultureInfo.InvariantCulture))));

    private static QueueRow Row(byte attacker, byte command, byte effect, ushort action, byte script) =>
        new(attacker, 1, effect, command, script, action);

    private static void Confirm(FieldAreaDescriptionSaveTracker tracker, int file, int game, uint resultIdentity)
    {
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Games, file, game), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Confirmation, file, game), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Saving, file, game), Down());
        tracker.ObserveSaveMenu(null, new NativeSaveResultPopup(true, resultIdentity));
    }

    private static SaveMenuStateSnapshot Page(SaveMenuPage page, int file, int game) => new(page, file, game, null, 0);

    private static NativeSaveResultPopup Down() => new(false, 0);

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Battle narration: " + message);
    }

    private readonly record struct QueueRow(byte Attacker, byte Kind, byte Effect, byte Command, byte Script, ushort Action);

    /// <summary>
    /// Guest memory for one battle, changed only the way the engine changes it. Each
    /// method is a hand replica of the decompiled code it names.
    /// </summary>
    private sealed class NativeBattle : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly HashSet<uint> unreadable = [];
        private readonly Dictionary<uint, byte> changeAfterFirstRead = [];
        private readonly HashSet<uint> readOnce = [];
        private QueueRow[] rows = [];

        public NativeBattle()
        {
            // FUN_0042A72D at battle start: queue index 0, everyone idle, flag set.
            Byte(0x00CBF9DC, 2);
            Byte(0x00BF2A38, 0);
            Byte(0x00BF2128, 1);
            Byte(0x00BE1170, 0);
            for (uint actor = 0; actor < 10; actor++)
            {
                Byte(0x00BE119E + (actor * 0x1AEC), 1);
            }

            EndQueue();
        }

        public void Byte(uint address, byte value) => bytes[address] = value;

        public void UInt16(uint address, ushort value)
        {
            bytes[address] = (byte)value;
            bytes[address + 1] = (byte)(value >> 8);
        }

        public void UInt32(uint address, uint value)
        {
            UInt16(address, (ushort)value);
            UInt16(address + 2, (ushort)(value >> 16));
        }

        public void Unreadable(uint address) => unreadable.Add(address);

        public void ChangeAfterFirstRead(uint address, byte value) => changeAfterFirstRead[address] = value;

        /// <summary>FUN_00436C66 / FUN_005DA0E7: the executor writes the rows, then a terminator.</summary>
        public void Build(params QueueRow[] queued)
        {
            rows = queued;
            for (var i = 0; i < queued.Length; i++)
            {
                var row = 0x009AAD70u + (uint)(i * 12);
                Byte(row, queued[i].Attacker);
                Byte(row + 1, queued[i].Kind);
                Byte(row + 2, queued[i].Effect);
                Byte(row + 3, queued[i].Command);
                Byte(row + 4, 0);
                Byte(row + 5, queued[i].Script);
                UInt16(row + 6, queued[i].Action);
            }

            Byte(0x009AAD70u + (uint)(queued.Length * 12), 0xFF);
        }

        /// <summary>FUN_0042CBF9 case 1 while 0x00BF2128 is set, then FUN_0042D227.</summary>
        public void StartCurrent()
        {
            var index = bytes[0x00BF2A38];
            var row = rows[index];
            var actor = (uint)row.Attacker;
            Byte(0x00BF2128, 0);
            Byte(0x00BE1170, row.Attacker);
            Byte(0x00BE119E + (actor * 0x1AEC), 0);
            UInt16(0x00BE117A + (actor * 0x1AEC), row.Script);
            Byte(0x00BE119A + (actor * 0x1AEC), row.Effect);
            Byte(0x00BE119B + (actor * 0x1AEC), row.Command);
            UInt16(0x00BF23FE + (actor * 0x74), row.Action);
        }

        /// <summary>
        /// Script opcode 0x9E marks the actor idle; FUN_0042D808 then sets the flag, and
        /// case 1 advances the queue index in the same tick.
        /// </summary>
        public void FinishCurrent()
        {
            var index = bytes[0x00BF2A38];
            var actor = (uint)rows[index].Attacker;
            Byte(0x00BE119E + (actor * 0x1AEC), 1);
            Byte(0x00BF2128, 1);
            Byte(0x00BF2A38, (byte)(index + 1));
        }

        /// <summary>The terminator row reached: index 0, and FUN_00436EB4 clears row 0.</summary>
        public void EndQueue()
        {
            rows = [];
            Byte(0x00BF2A38, 0);
            Byte(0x009AAD70, 0xFF);
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                var address = virtualAddress + (uint)i;
                if (unreadable.Contains(address))
                {
                    return false;
                }

                if (changeAfterFirstRead.TryGetValue(address, out var later) && !readOnce.Add(address))
                {
                    destination[i] = later;
                    continue;
                }

                destination[i] = bytes.TryGetValue(address, out var value) ? value : (byte)0;
            }

            return true;
        }
    }

    /// <summary>A coordinator with fake audio, speech and time.</summary>
    private sealed class Rig
    {
        public Rig(FieldAreaDescriptionHistory? history = null) : this(DefaultCatalog(), history)
        {
        }

        public Rig(
            BattleAnimationNarrationCatalog catalog,
            FieldAreaDescriptionHistory? history = null,
            string? manifest = null,
            int ticksPerCueFrame = 1)
        {
            History = history ?? new FieldAreaDescriptionHistory(path: null);
            Narration = new BattleAnimationNarrationCoordinator(
                catalog,
                History,
                CutsceneVoiceManifest.Parse(manifest ?? ManifestJson),
                Voice.Create,
                _ => { },
                text =>
                {
                    Spoken.Add(text);
                    return SpeechAccepts;
                },
                () => OtherDescriptionPlaying,
                ticksPerCueFrame,
                () => ScreenReaderSpeaking);
        }

        /// <summary>The screen reader's answer to whether it is speaking; null when it cannot say.</summary>
        public bool? ScreenReaderSpeaking { get; set; }

        public FieldAreaDescriptionHistory History { get; }

        public BattleAnimationNarrationCoordinator Narration { get; }

        public Voices Voice { get; } = new();

        public List<string> Spoken { get; } = [];

        public bool SpeechAccepts { get; init; } = true;

        public bool OtherDescriptionPlaying { get; set; }

        public void Update(BattleAnimationObservation? observation, double seconds)
        {
            Voice.Now = T0.AddSeconds(seconds);
            Narration.Update(observation, T0.AddSeconds(seconds));
        }

        public void ReadAndUpdate(NativeBattle memory, double seconds) =>
            Update(new BattleAnimationStateReader(memory).TryRead(out var seen) ? seen : null, seconds);

        public void PlayToEnd(BattleAnimationObservation observation, double seconds)
        {
            Update(observation, seconds);
            Update(BattleAnimationObservation.Idle, seconds + 1);
            Voice.Last.Finish();
            Update(BattleAnimationObservation.Idle, seconds + 10);
        }

        private static BattleAnimationNarrationCatalog DefaultCatalog() => new(
        [
            ("limit.cloud.braver", "Cloud: Braver", "Cloud leaps high."),
            ("limit.aerith.healing_wind", "Aerith: Healing Wind", "Green light gathers."),
            ("limit.cait.toy_boulder", "Cait Sith: Toy Box boulder", "A slab crashes down."),
            ("limit.cait.toy_hammer", "Cait Sith: Toy Box hammer", "A mallet swings down."),
            ("summon.bahamut_zero", "Bahamut ZERO", "A dragon rises into space."),
            ("summon.odin_lance", "Odin: Gunge Lance", "A horseman casts his lance."),
            .. Enumerable.Range(0, 10).Select(effect => (
                Key: BattleAnimationIdentities.All.Single(identity =>
                    identity.Command == Summon && identity.Effect == effect).Key,
                Name: "summon " + effect,
                Text: "summon " + effect.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))),
        ]);

        private const string ManifestJson = """
            {"entries": [
              {"text": "Cloud leaps high.", "file": "braver.ogg", "duration_seconds": 6.0},
              {"text": "A slab crashes down.", "file": "boulder.ogg", "duration_seconds": 5.0},
              {"text": "A mallet swings down.", "file": "hammer.ogg", "duration_seconds": 5.0},
              {"text": "A dragon rises into space.", "file": "bahamut-zero.ogg", "duration_seconds": 9.0},
              {"text": "A horseman casts his lance.", "file": "odin-lance.ogg", "duration_seconds": 7.0},
              {"text": "summon 00", "file": "summon-00.ogg", "duration_seconds": 5.0},
              {"text": "summon 01", "file": "summon-01.ogg", "duration_seconds": 5.0},
              {"text": "summon 02", "file": "summon-02.ogg", "duration_seconds": 5.0},
              {"text": "summon 03", "file": "summon-03.ogg", "duration_seconds": 5.0},
              {"text": "summon 04", "file": "summon-04.ogg", "duration_seconds": 5.0},
              {"text": "summon 05", "file": "summon-05.ogg", "duration_seconds": 5.0},
              {"text": "summon 06", "file": "summon-06.ogg", "duration_seconds": 5.0},
              {"text": "summon 07", "file": "summon-07.ogg", "duration_seconds": 5.0},
              {"text": "summon 08", "file": "summon-08.ogg", "duration_seconds": 5.0},
              {"text": "summon 09", "file": "summon-09.ogg", "duration_seconds": 5.0},
              {"text": "Tifa move 62", "file": "tifa-62.ogg", "duration_seconds": 5.0},
              {"text": "Tifa move 63", "file": "tifa-63.ogg", "duration_seconds": 5.0},
              {"text": "Tifa move 64", "file": "tifa-64.ogg", "duration_seconds": 5.0},
              {"text": "Tifa move 65", "file": "tifa-65.ogg", "duration_seconds": 5.0},
              {"text": "Tifa move 66", "file": "tifa-66.ogg", "duration_seconds": 5.0},
              {"text": "Tifa move 67", "file": "tifa-67.ogg", "duration_seconds": 5.0},
              {"text": "Tifa move 68", "file": "tifa-68.ogg", "duration_seconds": 5.0}
            ]}
            """;
    }

    /// <summary>An output that cannot say how its playback ended.</summary>
    private sealed class BareOutput : IFieldMovieNarrationOutput
    {
        public bool IsPlaying { get; set; }

        public int Starts { get; private set; }

        public bool Start(string reason)
        {
            Starts++;
            IsPlaying = true;
            return true;
        }

        public bool Stop(string reason)
        {
            IsPlaying = false;
            return true;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Recording outputs: what was started, and a clip's own end.</summary>
    private sealed class Voices
    {
        public DateTime Now { get; set; }

        public List<Clip> Created { get; } = [];

        public List<string> Started { get; } = [];

        /// <summary>Whether recordings accept a pause request.</summary>
        public bool Pauseable { get; set; } = true;

        public Clip Last => Created[^1];

        public IFieldMovieNarrationOutput Create(CutsceneVoiceClip clip)
        {
            var created = new Clip(this, clip.FileName);
            Created.Add(created);
            return created;
        }

        public sealed class Clip(Voices owner, string file)
            : IFieldMovieNarrationOutput, IFieldMovieNarrationCompletion, IFieldMovieNarrationPause
        {
            public List<bool> PauseCalls { get; } = [];

            public bool SetPaused(bool paused)
            {
                PauseCalls.Add(paused);
                return owner.Pauseable && IsPlaying;
            }

            private DateTime startedAt;

            public string File { get; } = file;

            public bool IsPlaying { get; private set; }

            public bool CompletedNormally { get; private set; }

            public int Stops { get; private set; }

            public double Age(double seconds) => (T0.AddSeconds(seconds) - startedAt).TotalSeconds;

            public bool Start(string reason)
            {
                IsPlaying = true;
                CompletedNormally = false;
                startedAt = owner.Now;
                owner.Started.Add(File);
                return true;
            }

            public bool Stop(string reason)
            {
                Stops++;
                IsPlaying = false;
                CompletedNormally = false;
                return true;
            }

            /// <summary>The clip reached its own end.</summary>
            public void Finish()
            {
                IsPlaying = false;
                CompletedNormally = true;
            }

            /// <summary>The device gave out part way through.</summary>
            public void Fail()
            {
                IsPlaying = false;
                CompletedNormally = false;
            }

            // Like the real player, releasing the device forgets how it ended.
            public void Dispose() => CompletedNormally = false;
        }
    }
}
