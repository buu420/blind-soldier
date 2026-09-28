using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Battle popups and status changes as a sighted player sees them. The memory
/// below is laid out by the same steps the engine takes: FUN_005d9940 queues an
/// action event and its result rows during the damage calculation, FUN_005da380
/// queues a drainer's recovery event after them, FUN_0042cbf9 starts each event,
/// a hit allocates the popup and its FUN_00425e5f sibling, and FUN_005bf1c2 draws
/// the popup on the next pass. Names and numbers come from the user's own log.
/// </summary>
internal static class BattleVisibleFeedbackTests
{
    private const int Cloud = 0;
    private const int RedXiii = 1;
    private const int Aeris = 2;
    private const int SoulFire = 4;
    private const int SoulFireB = 5;
    private const int GiNattak = 6;
    private const int EnemyCommand = 0x20;
    private const short Damage = 0;
    private const short Recovery = BattleVisibleResultSnapshot.RecoveryFlag;
    private const short Mp = BattleVisibleResultSnapshot.MpFlag;
    private const ushort Hit = 0x0001;
    private const ushort Lethal = BattleVisibleResultSnapshot.LethalResultFlag;

    private static readonly BattleActorSnapshot CloudActor = new(Cloud, "Cloud", false, 458, 1130, 60, 90, true);
    private static readonly BattleActorSnapshot RedActor = new(RedXiii, "Red XIII", false, 392, 900, 40, 70, true);
    private static readonly BattleActorSnapshot AerisActor = new(Aeris, "Aeris", false, 279, 381, 100, 120, true);
    private static readonly BattleActorSnapshot SoulFireActor = new(SoulFire, "Soul Fire", true, 300, 300, 0, 0, false);
    private static readonly BattleActorSnapshot SoulFireBActor = new(SoulFireB, "Soul Fire", true, 300, 300, 0, 0, false);
    private static readonly BattleActorSnapshot GiNattakActor = new(GiNattak, "Gi Nattak", true, 5500, 5500, 200, 200, false);

    private static readonly IReadOnlyList<BattleActorSnapshot> CaveOfTheGi =
        [CloudActor, RedActor, AerisActor, SoulFireActor, SoulFireBActor, GiNattakActor];

    internal static void Run()
    {
        SpeaksTheGiNattakDrainFromTheUsersBattle();
        SpeaksAnMpDrainAsMp();
        ReportsTheDrainerHurtWhenItDrainsAnUndeadTarget();
        FindsBothBloodFangDrainsFromOneBite();
        DoesNotCallASelfRecoveryADrainWithoutTheDrainEvent();
        DoesNotCallHealingBesideDamageADrain();
        SpeaksEveryHitOfAMultiHitAttackAndTheDefeatOnTheLastOne();
        SpeaksTheZeroAHealImmuneDrainerShows();
        SpeaksMissesAndFullRestores();
        ReportsAnInstantDeathOnlyThroughItsLethalRow();
        PicksTheUnrunSiblingPastAStaleOne();
        KeepsThePopupWhenItsSiblingIsMissing();
        DropsProvenanceWhenTheLinkedRowsWereReused();
        RejectsATornPopupButKeepsAPopupWhoseProvenanceTore();
        LegacyPopupEntryPointUsesTheNativeFlags();
        StatusWaitsForTheDrawnMaskNotTheCalculation();
        StatusOnADamagedTargetFollowsItsNumber();
        DeferredStatusIsNeverLost();
        UncopiedTopStatusBitsDoNotFlap();
        UnsensedEnemyDrawnStatusStaysPrivate();
        DrawnDefeatAndRevivalAreSpokenOnce();
        DisplayedStatusReaderReadsDrawnMasksAndUnrunPopups();
    }

    // 2026-09-25 21:46:28-40: "Gi Nattak used Drain.", then the log spoke
    // "Aeris took 102 damage." and, for Gi Nattak's green 102, "Gi Nattak took
    // 102 damage." because flag 1 was not treated as a recovery.
    private static void SpeaksTheGiNattakDrainFromTheUsersBattle()
    {
        var battle = new NativeBattle();
        var drain = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0B, action: 9);
        var aerisRow = battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 102, flags: Damage);
        battle.EndResults();
        var recovery = battle.QueueDrain(GiNattak, value: 102, flags: Recovery);

        battle.Start(drain);
        var aeris = battle.DrawHit(aerisRow);
        Equal(true, aeris.HasProvenance, "the target's popup links to its result row");
        Equal(GiNattak, aeris.AttackerActor, "native attacker of Aeris's row");
        Equal(false, aeris.IsDrainRecovery, "the drained target is not the recipient");
        Equal("Aeris took 102 damage.", SpeakDamage(aeris), "the drained target's white number");

        battle.Start(recovery.Event);
        var giNattak = battle.DrawHit(recovery.Row);
        Equal(true, giNattak.IsDrainRecovery, "FUN_005da380's event marks the drainer's popup");
        Equal((ushort)(1 << Aeris), giNattak.DrainSourceMask, "the drainer's own target is the source");
        Equal("Gi Nattak drained 102 HP from Aeris.", SpeakDamage(giNattak), "drain recipient and source");
    }

    private static void SpeaksAnMpDrainAsMp()
    {
        var battle = new NativeBattle();
        var aspil = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0B, action: 10);
        var aerisRow = battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 30, flags: Mp);
        battle.EndResults();
        var recovery = battle.QueueDrain(GiNattak, value: 30, flags: Recovery | Mp);

        battle.Start(aspil);
        Equal("Aeris lost 30 MP.", SpeakDamage(battle.DrawHit(aerisRow)), "the MP label on the target");
        battle.Start(recovery.Event);
        Equal(
            "Gi Nattak drained 30 MP from Aeris.",
            SpeakDamage(battle.DrawHit(recovery.Row)),
            "an MP drain is not spoken as HP");
    }

    // FUN_005df30b negates the transfer when the drained target was healed.
    private static void ReportsTheDrainerHurtWhenItDrainsAnUndeadTarget()
    {
        var battle = new NativeBattle();
        var drain = battle.QueueAction(Cloud, command: 2, script: 0x1C, action: 0x60);
        var zombieRow = battle.AddResult(SoulFire, Cloud, hit: 0x05, value: 50, flags: Recovery);
        battle.EndResults();
        var backlash = battle.QueueDrain(Cloud, value: 50, flags: Damage);

        battle.Start(drain);
        Equal("Soul Fire recovered 50 HP.", SpeakDamage(battle.DrawHit(zombieRow)), "the undead target heals");
        battle.Start(backlash.Event);
        Equal(
            "Cloud took 50 damage draining Soul Fire.",
            SpeakDamage(battle.DrawHit(backlash.Row)),
            "the drainer's own white number after a reversed drain");
    }

    // FUN_005d9940 queues the HP drain event, then the MP one, after the bite.
    private static void FindsBothBloodFangDrainsFromOneBite()
    {
        var battle = new NativeBattle();
        var bite = battle.QueueAction(RedXiii, command: 0x14, script: 0x3A, action: 0x0D);
        var soulFireRow = battle.AddResult(SoulFire, RedXiii, hit: 0x05, value: 311, flags: Damage);
        battle.EndResults();
        var hp = battle.QueueDrain(RedXiii, value: 311, flags: Recovery);
        var mp = battle.QueueDrain(RedXiii, value: 311, flags: Recovery | Mp);

        battle.Start(bite);
        Equal("Soul Fire took 311 damage.", SpeakDamage(battle.DrawHit(soulFireRow)), "the bite");
        battle.Start(hp.Event);
        Equal("Red XIII drained 311 HP from Soul Fire.", SpeakDamage(battle.DrawHit(hp.Row)), "HP half");
        battle.Start(mp.Event);
        var mpResult = battle.DrawHit(mp.Row);
        Equal((ushort)(1 << SoulFire), mpResult.DrainSourceMask, "the MP drain skips the HP drain event");
        Equal("Red XIII drained 311 MP from Soul Fire.", SpeakDamage(mpResult), "MP half");
    }

    // A self-targeted row with hit animation 0x2E is not enough: an enemy's
    // own damage animation id can be 0x2E. Only FUN_005da380's event counts.
    private static void DoesNotCallASelfRecoveryADrainWithoutTheDrainEvent()
    {
        var battle = new NativeBattle();
        var heal = battle.QueueAction(GiNattak, EnemyCommand, script: 0x2E, action: 4);
        var selfRow = battle.AddResult(GiNattak, GiNattak, hit: 0x2E, value: 400, flags: Recovery);
        battle.EndResults();

        battle.Start(heal);
        var result = battle.DrawHit(selfRow);
        Equal(true, result.HasProvenance, "the self-heal still has provenance");
        Equal(false, result.IsDrainRecovery, "an ordinary action event is not a drain");
        Equal("Gi Nattak recovered 400 HP.", SpeakDamage(result), "plain recovery");
    }

    private static void DoesNotCallHealingBesideDamageADrain()
    {
        var battle = new NativeBattle();
        var action = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0C, action: 5);
        var aerisRow = battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 88, flags: Damage);
        var selfRow = battle.AddResult(GiNattak, GiNattak, hit: 0x05, value: 88, flags: Recovery);
        battle.EndResults();

        battle.Start(action);
        Equal("Aeris took 88 damage.", SpeakDamage(battle.DrawHit(aerisRow)), "damage");
        var heal = battle.DrawHit(selfRow);
        Equal(false, heal.IsDrainRecovery, "healing that coincides with damage is not a drain");
        Equal("Gi Nattak recovered 88 HP.", SpeakDamage(heal), "plain recovery beside damage");
    }

    // The calculation lowers Cloud's HP by both hits before the first popup, so
    // an HP-difference tracker found nothing new on the second popup.
    private static void SpeaksEveryHitOfAMultiHitAttackAndTheDefeatOnTheLastOne()
    {
        var battle = new NativeBattle();
        var attack = battle.QueueAction(SoulFire, EnemyCommand, script: 0x0A, action: 2);
        var first = battle.AddResult(Cloud, SoulFire, hit: 0x05, value: 50, flags: Damage, resultFlags: Hit);
        var second = battle.AddResult(Cloud, SoulFire, hit: 0x05, value: 60, flags: Damage, resultFlags: Hit | Lethal);
        battle.EndResults();

        var status = new BattleStatusSpeechTracker();
        battle.Start(attack);
        var firstHit = battle.DrawHit(first);
        Equal("Cloud took 50 damage.", SpeakDamage(firstHit), "first hit");
        status.ConfirmVisibleResult(firstHit, CloudActor);
        Equal<string?>(null, status.Poll(), "no defeat before the lethal hit is drawn");

        var secondHit = battle.DrawHit(second);
        Equal("Cloud took 60 damage.", SpeakDamage(secondHit), "second hit is heard too");
        status.ConfirmVisibleResult(secondHit, CloudActor);
        Equal("Cloud was knocked out.", status.Poll(), "defeat on the lethal hit's popup");
    }

    // FUN_005dc0ea zeroes the transfer for a petrified or Peerless drainer; the
    // popup still draws a green 0 (FUN_005bb9c9 keeps one digit).
    private static void SpeaksTheZeroAHealImmuneDrainerShows()
    {
        var battle = new NativeBattle();
        var drain = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0B, action: 9);
        battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 102, flags: Damage);
        battle.EndResults();
        var recovery = battle.QueueDrain(GiNattak, value: 0, flags: Recovery);

        battle.Start(drain);
        battle.Start(recovery.Event);
        Equal(
            "Gi Nattak drained 0 HP from Aeris.",
            SpeakDamage(battle.DrawHit(recovery.Row)),
            "the drawn zero is spoken");
    }

    private static void SpeaksMissesAndFullRestores()
    {
        var battle = new NativeBattle();
        var action = battle.QueueAction(Aeris, command: 4, script: 0x20, action: 0x0B);
        var missRow = battle.AddResult(SoulFire, Aeris, hit: 0x05, value: -1, flags: Damage);
        var restoreRow = battle.AddResult(Cloud, Aeris, hit: 0x05, value: -3, flags: Recovery);
        battle.EndResults();

        battle.Start(action);
        Equal("Attack missed Soul Fire.", SpeakDamage(battle.DrawHit(missRow)), "native Miss word");
        Equal(
            "Cloud's HP and MP were fully restored.",
            SpeakDamage(battle.DrawHit(restoreRow)),
            "FUN_005db593 sets HP and MP to maximum for -3");
    }

    private static void ReportsAnInstantDeathOnlyThroughItsLethalRow()
    {
        var battle = new NativeBattle();
        var action = battle.QueueAction(Cloud, command: 0x1A, script: 0x30, action: 0);
        var fatal = battle.AddResult(SoulFire, Cloud, hit: 0x05, value: -2, flags: Damage, resultFlags: Hit | Lethal);
        var survived = battle.AddResult(SoulFireB, Cloud, hit: 0x05, value: -2, flags: Damage, resultFlags: Hit);
        battle.EndResults();

        var status = new BattleStatusSpeechTracker();
        battle.Start(action);
        var fatalResult = battle.DrawHit(fatal);
        Equal<string?>(null, SpeakDamage(fatalResult), "the death word has no number to speak");
        status.ConfirmVisibleResult(fatalResult, SoulFireActor);
        Equal("Soul Fire was defeated.", status.Poll(), "the lethal row confirms the defeat");

        status.ConfirmVisibleResult(battle.DrawHit(survived), SoulFireBActor);
        Equal<string?>(null, status.Poll(), "a death word on a surviving target is not a defeat");
    }

    private static void PicksTheUnrunSiblingPastAStaleOne()
    {
        var battle = new NativeBattle();
        var action = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0A, action: 1);
        var cloudRow = battle.AddResult(Cloud, GiNattak, hit: 0x05, value: 11, flags: Damage);
        var aerisRow = battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 22, flags: Damage);
        var redRow = battle.AddResult(RedXiii, GiNattak, hit: 0x05, value: 33, flags: Damage);
        battle.EndResults();
        battle.Start(action);

        // Another effect (FUN_0042675f, queued by FUN_0042613a) holds the lowest
        // slot when Cloud's pair is allocated above it. It ends first, so Aeris's
        // popup takes that slot while Cloud's sibling still counts its three frames.
        var other = battle.AllocateEffect(0x0042675F);
        var cloudPopup = battle.QueueHit(cloudRow);
        battle.RunFirstFrame(cloudPopup);
        battle.FreeEffect(other);
        var aerisPopup = battle.QueueHit(aerisRow);
        var redPopup = battle.QueueHit(redRow);
        Equal(true, aerisPopup < battle.SiblingOf(cloudPopup), "Aeris's popup sits below Cloud's live sibling");

        var aeris = battle.ReadAtFirstFrame(aerisPopup);
        Equal(aerisRow, aeris.ResultRow, "the stale sibling is skipped");
        Equal(22, aeris.Value, "Aeris's own number");
        var red = battle.ReadAtFirstFrame(redPopup);
        Equal(redRow, red.ResultRow, "the second pair of the same pass");
    }

    private static void KeepsThePopupWhenItsSiblingIsMissing()
    {
        var battle = new NativeBattle();
        var action = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0A, action: 1);
        var row = battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 102, flags: Recovery);
        battle.EndResults();
        battle.Start(action);
        var popup = battle.QueueHit(row);
        battle.RemoveSibling(popup);

        var result = battle.ReadAtFirstFrame(popup);
        Equal(true, result.IsValid, "the popup itself is still drawn");
        Equal(false, result.HasProvenance, "no sibling, no provenance");
        Equal("Aeris recovered 102 HP.", SpeakDamage(result), "still spoken from its own flags");

        // The popup needs only its own record, as BattleDamagePopupReader does;
        // the effect list is read for provenance alone.
        battle.Memory.Forget(BattleVisibleResultReader.AddressEffectFunctions, BattleVisibleResultReader.EffectCount * 4);
        var unlisted = battle.ReadAtFirstFrame(popup);
        Equal(true, unlisted.IsValid, "an unreadable effect list does not hide the popup");
        Equal(false, unlisted.HasProvenance, "but gives it no provenance");
    }

    // FUN_00436da7 hands out damage rows from a 128-entry ring (index & 0x7F),
    // and FUN_00436eb4 restarts the result rows for each queue. A link that no
    // longer describes the drawn number must not lend it a drain or a defeat.
    private static void DropsProvenanceWhenTheLinkedRowsWereReused()
    {
        var battle = new NativeBattle();
        var drain = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0B, action: 9);
        battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 102, flags: Damage);
        battle.EndResults();
        var recovery = battle.QueueDrain(GiNattak, value: 102, flags: Recovery);
        battle.Start(drain);
        battle.Start(recovery.Event);

        var popup = battle.QueueHit(recovery.Row);
        battle.OverwriteDamageRow(recovery.Row, target: GiNattak, value: 57, flags: Recovery);
        var reusedDamage = battle.ReadAtFirstFrame(popup);
        Equal(true, reusedDamage.IsValid, "the drawn popup stays");
        Equal(false, reusedDamage.HasProvenance, "a reused damage row is not this popup's");
        Equal(false, reusedDamage.IsDrainRecovery, "so it cannot mark a drain");
        Equal("Gi Nattak recovered 102 HP.", SpeakDamage(reusedDamage), "spoken from the popup alone");

        battle.OverwriteDamageRow(recovery.Row, target: Aeris, value: 102, flags: Recovery);
        Equal(
            false,
            battle.ReadAtFirstFrame(popup).HasProvenance,
            "the same number for another target is not this popup's either");

        var lethal = new NativeBattle();
        var attack = lethal.QueueAction(SoulFire, EnemyCommand, script: 0x0A, action: 2);
        var row = lethal.AddResult(Cloud, SoulFire, hit: 0x05, value: 60, flags: Damage, resultFlags: Hit | Lethal);
        lethal.EndResults();
        lethal.Start(attack);
        var cloudPopup = lethal.QueueHit(row);
        lethal.OverwriteResultTarget(row, Aeris);
        var reusedRow = lethal.ReadAtFirstFrame(cloudPopup);
        Equal(false, reusedRow.HasProvenance, "a result row now naming another target is not this popup's");
        Equal(false, reusedRow.IsLethal, "so it cannot confirm Cloud's defeat");
    }

    private static void RejectsATornPopupButKeepsAPopupWhoseProvenanceTore()
    {
        var battle = new NativeBattle();
        var action = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0A, action: 1);
        var row = battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 102, flags: Damage);
        battle.EndResults();
        battle.Start(action);
        var popup = battle.QueueHit(row);
        battle.Memory.WriteInt16(BattleVisibleResultReader.AddressCurrentEffectIndex, popup);

        var valueAddress = (int)BattleVisibleResultReader.EffectRecordAddress(popup) +
                           BattleVisibleResultReader.PopupValueOffset;
        battle.Memory.MutateOnRead(BattleVisibleResultReader.AddressCurrentModule, 2, () =>
            battle.Memory.WriteInt16(valueAddress, 103));
        Equal(false, new BattleVisibleResultReader(battle.Memory).Read().IsValid, "a torn popup is rejected");

        battle.Memory.WriteInt16(valueAddress, 102);
        var rowAttacker = BattleVisibleResultReader.AddressResultRows +
                          row * BattleVisibleResultReader.ResultRowSize + 1;
        battle.Memory.MutateOnRead(BattleVisibleResultReader.AddressCurrentModule, 2, () =>
            battle.Memory.WriteByte(rowAttacker, SoulFire));
        var result = new BattleVisibleResultReader(battle.Memory).Read();
        Equal(true, result.IsValid, "the popup survives a torn provenance read");
        Equal(false, result.HasProvenance, "the torn provenance is dropped");
    }

    private static void LegacyPopupEntryPointUsesTheNativeFlags()
    {
        var tracker = new BattleDamageSpeechTracker();
        tracker.Observe(new BattleDamagePopupSnapshot(true, 0, GiNattak, 102, Recovery), GiNattakActor);
        Equal("Gi Nattak recovered 102 HP.", tracker.Poll(), "flag 1 is a recovery, even unsensed");
        tracker.Observe(new BattleDamagePopupSnapshot(true, 0, Aeris, 100, Recovery | Mp), AerisActor);
        Equal("Aeris recovered 100 MP.", tracker.Poll(), "flags 5 are an MP recovery");
        tracker.Observe(new BattleDamagePopupSnapshot(true, 0, Aeris, 30, Mp), AerisActor);
        Equal("Aeris lost 30 MP.", tracker.Poll(), "flag 4 alone is MP damage, not a recovery");
        tracker.Observe(new BattleDamagePopupSnapshot(true, 0, Cloud, 416, Recovery), CloudActor);
        Equal("Cloud recovered 416 HP.", tracker.Poll(), "the drawn number, even past maximum HP");
    }

    // 2026-09-27 22:02:55-56: Big Guard on all allies spoke nine status lines
    // one second after targeting, while its animation still had seconds to run.
    private static void StatusWaitsForTheDrawnMaskNotTheCalculation()
    {
        const uint bigGuard = (1u << 8) | (1u << 16) | (1u << 17);
        IReadOnlyList<BattleActorSnapshot> party = [CloudActor, RedActor, AerisActor];
        IReadOnlyList<BattleActorSnapshot> calculated =
        [
            CloudActor with { StatusMask = bigGuard },
            RedActor with { StatusMask = bigGuard },
            AerisActor with { StatusMask = bigGuard }
        ];

        var liveTracker = new BattleStatusSpeechTracker();
        liveTracker.Observe(party);
        liveTracker.Observe(calculated);
        Equal("Cloud gained Haste.", liveTracker.Poll(), "the live mask speaks at calculation time");

        var tracker = new BattleStatusSpeechTracker();
        var battle = new NativeBattle();
        tracker.ObserveDisplayed(party, battle.ReadDisplayed());
        tracker.ObserveDisplayed(calculated, battle.ReadDisplayed());
        Equal<string?>(null, tracker.Poll(), "nothing is drawn yet, so nothing is spoken");

        battle.Display(Cloud, bigGuard);
        tracker.ObserveDisplayed(calculated, battle.ReadDisplayed());
        Equal("Cloud gained Haste.", tracker.Poll(), "Haste once the hit reaches Cloud");
        Equal("Cloud gained Barrier.", tracker.Poll(), "Barrier with it");
        Equal("Cloud gained Magic Barrier.", tracker.Poll(), "Magic Barrier with it");
        Equal<string?>(null, tracker.Poll(), "Red XIII and Aeris are not reached yet");
    }

    private static void StatusOnADamagedTargetFollowsItsNumber()
    {
        var battle = new NativeBattle();
        var bio = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0B, action: 3);
        var aerisRow = battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 47, flags: Damage);
        battle.EndResults();
        var status = new BattleStatusSpeechTracker();
        IReadOnlyList<BattleActorSnapshot> party = [AerisActor with { StatusMask = 1u << 3 }];
        status.ObserveDisplayed(party, battle.ReadDisplayed());

        // FUN_0042de61: the hit message sets the drawn mask and queues the popup
        // in one frame; the battle update runs before the popup's first frame.
        battle.Start(bio);
        battle.Display(Aeris, 1u << 3);
        var popup = battle.QueueHit(aerisRow);
        status.ObserveDisplayed(party, battle.ReadDisplayed());
        Equal<string?>(null, status.Poll(), "poison waits for Aeris's number");

        var hit = battle.ReadAtFirstFrame(popup);
        Equal("Aeris took 47 damage.", SpeakDamage(hit), "the number first");
        status.ConfirmVisibleResult(hit, AerisActor);
        status.ReleaseDeferred(Aeris);
        Equal("Aeris was poisoned.", status.Poll(), "then the status it caused");
        battle.RunFirstFrame(popup);
        status.ObserveDisplayed(party, battle.ReadDisplayed());
        Equal<string?>(null, status.Poll(), "and only once");
    }

    private static void DeferredStatusIsNeverLost()
    {
        var battle = new NativeBattle();
        var action = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0B, action: 3);
        var row = battle.AddResult(Cloud, GiNattak, hit: 0x05, value: 20, flags: Damage);
        battle.EndResults();
        var status = new BattleStatusSpeechTracker();
        IReadOnlyList<BattleActorSnapshot> party = [CloudActor with { StatusMask = 1u << 7 }];
        status.ObserveDisplayed(party, battle.ReadDisplayed());

        battle.Start(action);
        battle.Display(Cloud, 1u << 7);
        var popup = battle.QueueHit(row);
        status.ObserveDisplayed(party, battle.ReadDisplayed());
        Equal<string?>(null, status.Poll(), "deferred behind the popup");
        battle.RunFirstFrame(popup);
        status.ObserveDisplayed(party, battle.ReadDisplayed());
        Equal("Cloud was silenced.", status.Poll(), "released by the next observation without a release call");
    }

    private static void UncopiedTopStatusBitsDoNotFlap()
    {
        const uint deathForce = 1u << 28;
        var battle = new NativeBattle();
        var status = new BattleStatusSpeechTracker();
        var holding = CloudActor with { StatusMask = deathForce };
        status.ObserveDisplayed([CloudActor], battle.ReadDisplayed());

        battle.Display(Cloud, deathForce);
        status.ObserveDisplayed([holding], battle.ReadDisplayed());
        Equal("Cloud gained Death Force.", status.Poll(), "a hit copies the full word");

        battle.Display(Cloud, 0);
        status.ObserveDisplayed([holding], battle.ReadDisplayed());
        Equal<string?>(null, status.Poll(), "the queue's status event drops the top bits; the live mask keeps them");

        status.ObserveDisplayed([CloudActor], battle.ReadDisplayed());
        Equal("Cloud's Death Force wore off.", status.Poll(), "gone from both copies");
    }

    private static void UnsensedEnemyDrawnStatusStaysPrivate()
    {
        var battle = new NativeBattle();
        var status = new BattleStatusSpeechTracker();
        status.ObserveDisplayed([SoulFireActor], battle.ReadDisplayed());
        battle.Display(SoulFire, (1u << 2) | 1u);
        status.ObserveDisplayed([SoulFireActor], battle.ReadDisplayed());
        Equal<string?>(null, status.Poll(), "an unsensed enemy's drawn mask stays private");

        var action = battle.QueueAction(Cloud, command: 1, script: 0x00, action: 0);
        var row = battle.AddResult(SoulFire, Cloud, hit: 0x05, value: 300, flags: Damage, resultFlags: Hit | Lethal);
        battle.EndResults();
        battle.Start(action);
        var hit = battle.DrawHit(row);
        Equal("Soul Fire took 300 damage.", SpeakDamage(hit), "the drawn number is public");
        status.ConfirmVisibleResult(hit, SoulFireActor);
        Equal("Soul Fire was defeated.", status.Poll(), "the visible defeat is public");

        battle.Display(SoulFire, 0);
        status.ObserveDisplayed([SoulFireActor], battle.ReadDisplayed());
        Equal<string?>(null, status.Poll(), "a redacted mask cannot synthesize a revival");

        var sensed = SoulFireActor with { InformationVisible = true };
        var sensedStatus = new BattleStatusSpeechTracker();
        battle.Display(SoulFire, 0);
        sensedStatus.ObserveDisplayed([sensed], battle.ReadDisplayed());
        battle.Display(SoulFire, 1u << 2);
        sensedStatus.ObserveDisplayed([sensed], battle.ReadDisplayed());
        Equal("Soul Fire fell asleep.", sensedStatus.Poll(), "a sensed enemy's drawn status is spoken");
    }

    private static void DrawnDefeatAndRevivalAreSpokenOnce()
    {
        var battle = new NativeBattle();
        var status = new BattleStatusSpeechTracker();
        IReadOnlyList<BattleActorSnapshot> party = [CloudActor];
        status.ObserveDisplayed(party, battle.ReadDisplayed());

        // Death Sentence runs out without a popup; the queue's status event
        // (FUN_005c7db0, kind 5) draws the defeat.
        battle.Display(Cloud, 1u);
        status.ObserveDisplayed(party, battle.ReadDisplayed());
        Equal("Cloud was knocked out.", status.Poll(), "a drawn defeat without a popup");
        status.ObserveDisplayed(party, battle.ReadDisplayed());
        Equal<string?>(null, status.Poll(), "heard once");

        battle.Display(Cloud, 0);
        status.ObserveDisplayed(party, battle.ReadDisplayed());
        Equal("Cloud was revived.", status.Poll(), "a drawn revival");
    }

    private static void DisplayedStatusReaderReadsDrawnMasksAndUnrunPopups()
    {
        var battle = new NativeBattle();
        var action = battle.QueueAction(GiNattak, EnemyCommand, script: 0x0B, action: 3);
        var aerisRow = battle.AddResult(Aeris, GiNattak, hit: 0x05, value: 5, flags: Damage);
        var redRow = battle.AddResult(RedXiii, GiNattak, hit: 0x05, value: 6, flags: Damage);
        battle.EndResults();
        battle.Start(action);
        battle.Display(Aeris, 1u << 3);
        battle.Display(GiNattak, 1u << 15);
        var drawn = battle.QueueHit(redRow);
        battle.RunFirstFrame(drawn);
        battle.QueueHit(aerisRow);

        var snapshot = battle.ReadDisplayed();
        Equal(true, snapshot.IsValid, "drawn masks read");
        Equal(1u << 3, snapshot.MaskFor(Aeris), "Aeris's drawn mask at 0x00BF23C0 + 2 * 0x74");
        Equal(1u << 15, snapshot.MaskFor(GiNattak), "Gi Nattak's drawn mask");
        Equal(true, snapshot.HasPendingPopup(Aeris), "Aeris's popup has not drawn yet");
        Equal(false, snapshot.HasPendingPopup(RedXiii), "Red XIII's popup already drew");

        battle.Memory.WriteByte(BattleDisplayedStatusReader.AddressCurrentModule, 1);
        Equal(false, new BattleDisplayedStatusReader(battle.Memory).TryRead(out _), "outside battle");
        battle.Memory.WriteByte(BattleDisplayedStatusReader.AddressCurrentModule, BattleStateReader.BattleModule);
        battle.Memory.MutateOnRead(BattleDisplayedStatusReader.AddressCurrentModule, 2, () =>
            battle.Display(Cloud, 1u << 9));
        Equal(false, new BattleDisplayedStatusReader(battle.Memory).TryRead(out _), "a torn read is rejected");
    }

    private static string? SpeakDamage(BattleVisibleResultSnapshot result)
    {
        var tracker = new BattleDamageSpeechTracker();
        tracker.ObserveVisibleResult(result, CaveOfTheGi);
        return tracker.Poll();
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Battle visible feedback: {message}. Expected <{expected}>, got <{actual}>.");
        }
    }

    /// <summary>Battle memory laid out by the engine's own writers.</summary>
    private sealed class NativeBattle
    {
        private readonly Dictionary<int, int> siblings = [];
        private int events;
        private int rows;
        private int damageRows;

        public NativeBattle()
        {
            Memory.WriteByte(BattleVisibleResultReader.AddressCurrentModule, BattleStateReader.BattleModule);
            Memory.WriteInt16(BattleVisibleResultReader.AddressCurrentEffectIndex, 0);
            Memory.Zero(BattleVisibleResultReader.AddressEffectFunctions, BattleVisibleResultReader.EffectCount * 4);
            Memory.Zero(
                BattleVisibleResultReader.AddressEffectData,
                BattleVisibleResultReader.EffectCount * BattleVisibleResultReader.EffectRecordSize);
            Memory.Zero(
                BattleVisibleResultReader.AddressAnimationEvents,
                BattleVisibleResultReader.AnimationEventCapacity * BattleVisibleResultReader.AnimationEventSize);
            Memory.WriteInt32(BattleVisibleResultReader.AddressAnimationEventCount, 0);
            Memory.Zero(
                BattleVisibleResultReader.AddressResultRows,
                BattleVisibleResultReader.ResultRowCapacity * BattleVisibleResultReader.ResultRowSize);
            Memory.WriteInt32(BattleVisibleResultReader.AddressResultRowCount, 0);
            Memory.Zero(
                BattleVisibleResultReader.AddressDamageRows,
                BattleVisibleResultReader.DamageRowCapacity * BattleVisibleResultReader.DamageRowSize);
            Memory.Zero(
                BattleDisplayedStatusReader.AddressDisplayedStatus,
                BattleDisplayedStatusReader.ActorCount * BattleDisplayedStatusReader.DisplayedStateSize);
            Memory.WriteByte(BattleStateReader.AddressAnimationEventIndex, 0);
        }

        public GuestMemory Memory { get; } = new();

        // FUN_00436c66, filled in by FUN_005da0e7: the cursor starts at the
        // next result row.
        public int QueueAction(
            int attacker,
            int command,
            int script,
            int action,
            int effect = 1,
            ushort camera = 0x0010)
        {
            var index = events++;
            var address = BattleVisibleResultReader.AddressAnimationEvents +
                          index * BattleVisibleResultReader.AnimationEventSize;
            Memory.WriteByte(address, attacker);
            Memory.WriteByte(address + 1, 1);
            Memory.WriteByte(address + 2, effect);
            Memory.WriteByte(address + 3, command);
            Memory.WriteByte(address + 4, 0);
            Memory.WriteByte(address + 5, script);
            Memory.WriteInt16(address + 6, action);
            Memory.WriteInt16(address + 8, unchecked((short)camera));
            Memory.WriteInt16(address + 0x0A, rows);
            Memory.WriteInt32(BattleVisibleResultReader.AddressAnimationEventCount, events);
            return index;
        }

        // FUN_00436cb5 and FUN_005da562.
        public int AddResult(
            int target,
            int attacker,
            int hit,
            short value,
            short flags,
            ushort resultFlags = Hit)
        {
            var damage = damageRows++;
            var damageAddress = BattleVisibleResultReader.AddressDamageRows +
                                damage * BattleVisibleResultReader.DamageRowSize;
            Memory.WriteInt16(damageAddress, target);
            Memory.WriteInt16(damageAddress + 2, value);
            Memory.WriteInt16(damageAddress + 4, flags);
            Memory.WriteInt16(damageAddress + 6, 0x7777);
            Memory.WriteInt16(damageAddress + 8, 0x7777);
            Memory.WriteInt16(damageAddress + 0x0A, -1);
            Memory.WriteInt16(damageAddress + 0x0C, -1);

            var row = rows++;
            var address = BattleVisibleResultReader.AddressResultRows + row * BattleVisibleResultReader.ResultRowSize;
            Memory.WriteByte(address, target);
            Memory.WriteByte(address + 1, attacker);
            Memory.WriteByte(address + 2, hit);
            Memory.WriteByte(address + 3, damage);
            Memory.WriteInt16(address + 4, unchecked((short)resultFlags));
            Memory.WriteInt32(address + 8, 0);
            Memory.WriteInt32(BattleVisibleResultReader.AddressResultRowCount, rows);
            return row;
        }

        // FUN_00436dff.
        public void EndResults()
        {
            var row = rows++;
            var address = BattleVisibleResultReader.AddressResultRows + row * BattleVisibleResultReader.ResultRowSize;
            Memory.WriteByte(address, 0xFF);
            Memory.WriteByte(address + 3, 0xFF);
            Memory.WriteInt32(BattleVisibleResultReader.AddressResultRowCount, rows);
        }

        // FUN_005da380, FUN_005da3f5 and FUN_00436dff.
        public (int Event, int Row) QueueDrain(int drainer, short value, short flags)
        {
            var drainEvent = QueueAction(drainer, command: 0, script: 0x2E, action: 0, effect: 0, camera: 0xFFFF);
            var row = AddResult(drainer, drainer, hit: 0x2E, value, flags);
            EndResults();
            return (drainEvent, row);
        }

        // FUN_0042cbf9 case 1: copy the event's rows and leave the cursor on its
        // terminator.
        public void Start(int eventIndex)
        {
            var cursorAddress = BattleVisibleResultReader.AddressAnimationEvents +
                                eventIndex * BattleVisibleResultReader.AnimationEventSize + 0x0A;
            var cursor = Memory.ReadInt16(cursorAddress);
            while (Memory.ReadByte(BattleVisibleResultReader.AddressResultRows +
                                   cursor * BattleVisibleResultReader.ResultRowSize) != 0xFF)
            {
                cursor++;
            }

            Memory.WriteInt16(cursorAddress, cursor);
            Memory.WriteByte(BattleStateReader.AddressAnimationEventIndex, eventIndex);
        }

        // FUN_00425fc4: the popup, then its FUN_00425e5f sibling, each in the
        // lowest free slot (FUN_005bed92).
        public int QueueHit(int row)
        {
            var rowAddress = BattleVisibleResultReader.AddressResultRows + row * BattleVisibleResultReader.ResultRowSize;
            var target = Memory.ReadByte(rowAddress);
            var damage = Memory.ReadByte(rowAddress + 3);
            var damageAddress = BattleVisibleResultReader.AddressDamageRows +
                                damage * BattleVisibleResultReader.DamageRowSize;
            var value = Memory.ReadInt16(damageAddress + 2);
            var flags = Memory.ReadInt16(damageAddress + 4);

            var popup = Allocate(BattleVisibleResultReader.DamagePopupFunction);
            var popupAddress = (int)BattleVisibleResultReader.EffectRecordAddress(popup);
            Memory.WriteInt16(popupAddress + BattleVisibleResultReader.PopupValueOffset, value);
            Memory.WriteInt32(popupAddress + BattleVisibleResultReader.PopupTargetOffset, target);
            Memory.WriteInt32(popupAddress + BattleVisibleResultReader.PopupFlagsOffset, flags);

            var sibling = Allocate(BattleVisibleResultReader.DamageCommitFunction);
            var siblingAddress = (int)BattleVisibleResultReader.EffectRecordAddress(sibling);
            Memory.WriteInt16(siblingAddress + BattleVisibleResultReader.CommitDamageRowOffset, damage);
            Memory.WriteInt16(siblingAddress + BattleVisibleResultReader.CommitResultRowOffset, (sbyte)row);
            siblings[popup] = sibling;
            return popup;
        }

        // FUN_005bf1c2 reaches the popup; the reader runs inside FUN_005bb410.
        public BattleVisibleResultSnapshot ReadAtFirstFrame(int popup)
        {
            Memory.WriteInt16(BattleVisibleResultReader.AddressCurrentEffectIndex, popup);
            return new BattleVisibleResultReader(Memory).Read();
        }

        public BattleVisibleResultSnapshot DrawHit(int row)
        {
            var popup = QueueHit(row);
            var result = ReadAtFirstFrame(popup);
            RunFirstFrame(popup);
            return result;
        }

        // FUN_005bb410 and FUN_00425e5f leave state 0 on their first frame.
        public void RunFirstFrame(int popup)
        {
            Memory.WriteByte((int)BattleVisibleResultReader.EffectRecordAddress(popup) + 2, 1);
            Memory.WriteByte((int)BattleVisibleResultReader.EffectRecordAddress(siblings[popup]) + 2, 1);
        }

        public void OverwriteDamageRow(int row, int target, short value, short flags)
        {
            var damage = Memory.ReadByte(
                BattleVisibleResultReader.AddressResultRows + row * BattleVisibleResultReader.ResultRowSize + 3);
            var address = BattleVisibleResultReader.AddressDamageRows +
                          damage * BattleVisibleResultReader.DamageRowSize;
            Memory.WriteInt16(address, target);
            Memory.WriteInt16(address + 2, value);
            Memory.WriteInt16(address + 4, flags);
        }

        public void OverwriteResultTarget(int row, int target) =>
            Memory.WriteByte(
                BattleVisibleResultReader.AddressResultRows + row * BattleVisibleResultReader.ResultRowSize,
                target);

        public int AllocateEffect(uint function) => Allocate(function);

        public void FreeEffect(int slot) => Free(slot);

        public int SiblingOf(int popup) => siblings[popup];

        public void RemoveSibling(int popup) => Free(siblings[popup]);

        public void Display(int actor, uint mask) =>
            Memory.WriteInt32(
                BattleDisplayedStatusReader.AddressDisplayedStatus +
                    actor * BattleDisplayedStatusReader.DisplayedStateSize,
                unchecked((int)mask));

        public BattleDisplayedStatusSnapshot ReadDisplayed() =>
            new BattleDisplayedStatusReader(Memory).TryRead(out var snapshot)
                ? snapshot
                : throw new InvalidOperationException("Battle visible feedback: drawn masks were unreadable.");

        private int Allocate(uint function)
        {
            for (var slot = 0; slot < BattleVisibleResultReader.EffectCount; slot++)
            {
                var functionAddress = BattleVisibleResultReader.AddressEffectFunctions + slot * 4;
                if (Memory.ReadInt32(functionAddress) != 0)
                {
                    continue;
                }

                Memory.WriteInt32(functionAddress, unchecked((int)function));
                Memory.Zero(
                    (int)BattleVisibleResultReader.EffectRecordAddress(slot),
                    BattleVisibleResultReader.EffectRecordSize);
                return slot;
            }

            throw new InvalidOperationException("Battle visible feedback: effect list is full.");
        }

        private void Free(int slot)
        {
            Memory.WriteInt32(BattleVisibleResultReader.AddressEffectFunctions + slot * 4, 0);
            Memory.Zero(
                (int)BattleVisibleResultReader.EffectRecordAddress(slot),
                BattleVisibleResultReader.EffectRecordSize);
        }
    }

    /// <summary>Sparse guest memory; bytes never written are unmapped.</summary>
    private sealed class GuestMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private (uint Address, int Countdown, Action Mutation)? pendingMutation;

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (pendingMutation is { } mutation && mutation.Address == virtualAddress)
            {
                if (mutation.Countdown <= 1)
                {
                    pendingMutation = null;
                    mutation.Mutation();
                }
                else
                {
                    pendingMutation = (mutation.Address, mutation.Countdown - 1, mutation.Mutation);
                }
            }

            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out var value))
                {
                    return false;
                }

                destination[index] = value;
            }

            return true;
        }

        /// <summary>Runs a write just before the nth read of an address.</summary>
        public void MutateOnRead(int address, int nthRead, Action mutation) =>
            pendingMutation = ((uint)address, nthRead, mutation);

        public void Zero(int address, int length)
        {
            for (var index = 0; index < length; index++)
            {
                bytes[(uint)(address + index)] = 0;
            }
        }

        public void Forget(int address, int length)
        {
            for (var index = 0; index < length; index++)
            {
                bytes.Remove((uint)(address + index));
            }
        }

        public void WriteByte(int address, int value) => bytes[(uint)address] = unchecked((byte)value);

        public void WriteInt16(int address, int value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buffer, unchecked((short)value));
            Write(address, buffer);
        }

        public void WriteInt32(int address, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            Write(address, buffer);
        }

        public byte ReadByte(int address) => bytes[(uint)address];

        public short ReadInt16(int address) =>
            unchecked((short)(bytes[(uint)address] | (bytes[(uint)address + 1] << 8)));

        public int ReadInt32(int address) =>
            bytes[(uint)address] |
            (bytes[(uint)address + 1] << 8) |
            (bytes[(uint)address + 2] << 16) |
            (bytes[(uint)address + 3] << 24);

        private void Write(int address, ReadOnlySpan<byte> buffer)
        {
            for (var index = 0; index < buffer.Length; index++)
            {
                bytes[(uint)(address + index)] = buffer[index];
            }
        }
    }
}
