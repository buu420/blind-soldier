using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Gold Saucer attractions: finding the machines, and reading the one minigame
/// whose visible state is fully verified against the installed executable.
/// </summary>
internal static class GoldSaucerMinigameTests
{
    public static void Run(IReadOnlyList<FieldStoryEventDefinition>? definitions = null)
    {
        definitions ??= FieldStoryEventCatalog.CreateAllFields();
        EveryAvailableMachineIsReachable(definitions);
        TheShootingCoasterIsRegisteredWithPeopleNotWithTheBarrier(definitions);
        TheSnowGameIsNotOfferedBeforeItsNativeGate(definitions);
        TheCoasterReaderOnlyTrustsItsOwnModule();
        TheCoasterReadoutSpeaksAimAndCharge();
        TheCoasterReadoutStaysQuietWhenNothingVisibleChanged();
        TheArcadeMachinesTheFirstAuditMissedAreReachable(definitions);
        TheBasketballReaderRequiresTheShotScriptToBeRunning();
        TheBasketballReadoutFollowsTheVisibleWindUp();
        TheArmWrestlingReaderRequiresARunningContest();
        TheArmWrestlingReadoutDescribesTheVisibleLean();
        The3DBattlerReaderRequiresTheMatchLoopToBeRunning();
        The3DBattlerReadoutReportsOnlyWhatResolved();
        The3DBattlerBatchReachesThePlayerWhole();
    }

    /// <summary>
    /// The two machines the first audit missed, both found by following the entry
    /// LINE's request into the script it runs rather than stopping at the LINE. Both
    /// are one machine with two usable control sides, which is why the rows say
    /// "from its other side" rather than naming a second cabinet.
    /// </summary>
    private static void TheArcadeMachinesTheFirstAuditMissedAreReachable(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        foreach (var (field, entity, source, script, label) in
                 new (int Field, int Entity, string Source, string Script, string Label)[]
                 {
                     (506, 14, "ufo1", "[OK]", "Play the Wonder Catcher (optional)"),
                     (506, 15, "ufo2", "[OK]", "Play the Wonder Catcher from its other side (optional)"),
                     (507, 10, "kakul1", "Go", "Play 3D Battler (optional)"),
                     (507, 11, "kakul2", "Go", "Play 3D Battler from its other side (optional)"),
                     (506, 8, "s1", "Talk", "Talk to the Wonder Square prize counter (optional)")
                 })
        {
            var rows = Active(definitions, field, 440).Where(row => row.Label == label).ToArray();
            Equal(1, rows.Length, $"field {field} must offer exactly one '{label}'");
            Equal(entity, rows[0].EntityId, $"'{label}' must record its native entity id");
            Equal(source, rows[0].SourceEntityName, $"'{label}' must record its native entity name");
            Equal(script, rows[0].SourceScriptType, $"'{label}' must record the script it runs");
            Equal(true, rows[0].Priority > 0, $"'{label}' is optional and must never outrank an objective");
        }

        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is required.");
        var scripts = new FieldScriptNavigationCatalog(dataRoot);
        // games_2 kakul1(10) script 4 byte 101 requests dic(0) script 3, the shared
        // 3D Battler loop, which is what proves the entity is that game.
        AssertBytes(scripts, 507, 10, 4, 101, "030043");
        // games_1 ufo1(14) [OK] byte 4 requests cloud(1) script 8, the left-hand
        // Wonder Catcher side; ufo2(15) requests script 9, the right-hand side.
        AssertBytes(scripts, 506, 14, 1, 4, "030148");
        AssertBytes(scripts, 506, 15, 1, 4, "030149");
    }

    private static FakeFieldScriptMemory BasketballRoom()
    {
        var memory = new FakeFieldScriptMemory
        {
            FieldId = WonderSquareBasketballStateReader.BasketballFieldId
        };
        memory.GiveModel(WonderSquareBasketballStateReader.CloudEntityId, 3);
        return memory;
    }

    private static void ArmTheShot(FakeFieldScriptMemory memory) =>
        memory.RunScript(
            WonderSquareBasketballStateReader.CloudEntityId,
            WonderSquareBasketballStateReader.ShotScriptId);

    private static void PlayShotSegment(FakeFieldScriptMemory memory, int currentFrame, int endFrame) =>
        memory.PlayAnimation(
            3,
            WonderSquareBasketballStateReader.ShotAnimationId,
            currentFrame,
            endFrame);

    private static void TheBasketballReaderRequiresTheShotScriptToBeRunning()
    {
        // Root's finding: the field and a plausible set of shared bank bytes are not
        // evidence that this machine owns them. Nothing is reported until Cloud is
        // actually executing the shot script.
        var memory = BasketballRoom();
        var reader = new WonderSquareBasketballStateReader(memory);

        memory.Module = 3;
        Equal(false, reader.TryRead(out _), "only the field module is read");

        memory.Module = FieldPositionReader.FieldModule;
        memory.FieldId = 507;
        Equal(false, reader.TryRead(out _), "another field's temp bank is not the basketball game");

        memory.FieldId = WonderSquareBasketballStateReader.BasketballFieldId;
        PlayShotSegment(memory, 12, WonderSquareBasketballStateReader.RiseEndFrame);
        Equal(true, reader.TryRead(out var idle), "the idle room reads");
        Equal(false, idle.IsShotRunning, "an idle room is not a shot, whatever the bank holds");
        Equal(false, idle.IsRising, "and nothing is rising in it");

        // Another machine in the same room running its own script is still not this
        // one: the identity is the entity as well as the script.
        memory.RunScript(WonderSquareArmWrestlingStateReader.UdelEntityId, 1);
        Equal(true, reader.TryRead(out var otherMachine), "the room still reads");
        Equal(false, otherMachine.IsShotRunning, "another machine's contest is not a basketball shot");

        ArmTheShot(memory);
        Equal(true, reader.TryRead(out var rising), "a live shot reads");
        Equal(true, rising.IsShotRunning, "the shot script is the controller identity");
        Equal(true, rising.IsRising, "the rise segment is playing");
        Equal(false, rising.HasSettled, "and has not reached its last frame");

        // The three partial plays of animation 12 are told apart by their end frame.
        PlayShotSegment(memory, 4, 8);
        Equal(true, reader.TryRead(out var beforeButton), "the pre-button segment reads");
        Equal(false, beforeButton.IsRising, "animation 12 frames 0..8 is not the rise");
        Equal(false, beforeButton.HasSettled, "nor is it a settled rise");

        PlayShotSegment(
            memory,
            WonderSquareBasketballStateReader.RiseEndFrame,
            WonderSquareBasketballStateReader.RiseEndFrame);
        Equal(true, reader.TryRead(out var settled), "the settled pose reads");
        Equal(true, settled.HasSettled, "the rise has reached its last frame");
        Equal(false, settled.IsRising, "a settled rise is no longer rising");

        PlayShotSegment(memory, 30, WonderSquareBasketballStateReader.ThrowEndFrame);
        Equal(true, reader.TryRead(out var throwing), "the throw reads");
        Equal(true, throwing.HasThrown, "animation 12 frames 16..49 is the throw");
        Equal(false, throwing.IsRising, "the throw is not the rise");

        // Leaving the machine ends it, and a torn read is silence rather than a state.
        memory.StopScripts(WonderSquareBasketballStateReader.CloudEntityId);
        Equal(true, reader.TryRead(out var afterExit), "the room after the shot reads");
        Equal(false, afterExit.IsShotRunning, "the shot ends when Cloud stops running it");

        ArmTheShot(memory);
        memory.UnreadableAddresses.Add((uint)FieldScriptControllerReader.AddressFieldScriptPointer);
        Equal(false, reader.TryRead(out _), "an unreadable script table is not a state");
        memory.UnreadableAddresses.Clear();
        Equal(true, reader.TryRead(out _), "and it recovers once readable again");
    }

    private static void TheBasketballReadoutFollowsTheVisibleWindUp()
    {
        var memory = BasketballRoom();
        var reader = new WonderSquareBasketballStateReader(memory);
        var readout = new WonderSquareBasketballReadout();
        Equal(true, readout.Observe(default).IsEmpty, "nothing is said while the game is not running");

        ArmTheShot(memory);
        PlayShotSegment(memory, 8, WonderSquareBasketballStateReader.RiseEndFrame);
        Equal(true, reader.TryRead(out var start), "the opening frame of the rise reads");
        var startCue = readout.Observe(start);
        Equal("Winding up.", startCue.Speech, "the wind-up is announced when it begins");
        Equal(true, startCue.RiseStarted, "the start of the rise is marked once");

        // The rise is one continuous motion of eight animation frames, so nothing
        // further is said while it runs. The old per-step tick reported a strength
        // meter the game never draws.
        for (var frame = 9; frame < WonderSquareBasketballStateReader.RiseEndFrame; frame++)
        {
            PlayShotSegment(memory, frame, WonderSquareBasketballStateReader.RiseEndFrame);
            Equal(true, reader.TryRead(out var mid), $"frame {frame} reads");
            Equal(true, readout.Observe(mid).IsEmpty, "the rise itself is one motion, not a meter");
        }

        PlayShotSegment(
            memory,
            WonderSquareBasketballStateReader.RiseEndFrame,
            WonderSquareBasketballStateReader.RiseEndFrame);
        Equal(true, reader.TryRead(out var settled), "the settled pose reads");
        var settledCue = readout.Observe(settled);
        Equal(true, settledCue.RiseSettled, "the settled pose is marked when the model reaches its last frame");
        Equal(false, settledCue.RiseStarted, "the settled marker is not another start");

        // Root's finding: nothing may keep sounding after the pose has settled,
        // because the screen shows nothing more until the button is let go.
        for (var poll = 0; poll < 40; poll++)
        {
            Equal(true, readout.Observe(settled).IsEmpty, "the held pose must be silent; the model is not moving");
        }

        // The Double Chance rounds re-arm and rerun the same sequence without ever
        // leaving script 10, so the next rise has to be describable in its own right.
        PlayShotSegment(memory, 30, WonderSquareBasketballStateReader.ThrowEndFrame);
        Equal(true, reader.TryRead(out var thrown), "the throw reads");
        Equal(true, readout.Observe(thrown).IsEmpty, "the throw itself is native audio and dialogue");

        PlayShotSegment(memory, 8, WonderSquareBasketballStateReader.RiseEndFrame);
        Equal(true, reader.TryRead(out var secondRise), "the second round's rise reads");
        Equal("Winding up.", readout.Observe(secondRise).Speech,
            "a Double Chance round is a new wind-up, not a repeat of the first");

        TheBasketballReadoutStaysSilentOnAShotItDidNotSeeStart();
    }

    private static void TheBasketballReadoutStaysSilentOnAShotItDidNotSeeStart()
    {
        // A wind-up first seen already settled has no describable start left, and a
        // landmark announced after it happened misleads rather than helps.
        var memory = BasketballRoom();
        var reader = new WonderSquareBasketballStateReader(memory);
        var readout = new WonderSquareBasketballReadout();

        ArmTheShot(memory);
        PlayShotSegment(
            memory,
            WonderSquareBasketballStateReader.RiseEndFrame,
            WonderSquareBasketballStateReader.RiseEndFrame);
        Equal(true, reader.TryRead(out var late), "the already-settled pose reads");
        Equal(true, readout.Observe(late).IsEmpty, "a rise seen only after it settled is silent");
        Equal(true, readout.Observe(late).IsEmpty, "and stays silent for the rest of that wind-up");

        PlayShotSegment(memory, 30, WonderSquareBasketballStateReader.ThrowEndFrame);
        Equal(true, reader.TryRead(out var thrown), "the throw reads");
        readout.Observe(thrown);

        PlayShotSegment(memory, 8, WonderSquareBasketballStateReader.RiseEndFrame);
        Equal(true, reader.TryRead(out var fresh), "the next wind-up reads");
        Equal("Winding up.", readout.Observe(fresh).Speech,
            "the following wind-up, seen from its start, is described normally");
    }

    private static FakeFieldScriptMemory ArmWrestlingRoom()
    {
        var memory = new FakeFieldScriptMemory
        {
            FieldId = WonderSquareArmWrestlingStateReader.ArmWrestlingFieldId
        };
        memory.GiveModel(WonderSquareArmWrestlingStateReader.UdelEntityId, 5);
        return memory;
    }

    private static void TheArmWrestlingReaderRequiresARunningContest()
    {
        // Root's reproduced P1: an entirely zeroed temporary bank in the idle room
        // satisfied "go cleared, not finished, lean within travel", and the readout
        // announced "Arms locked. Your arm is down." on walking in.
        var memory = ArmWrestlingRoom();
        var reader = new WonderSquareArmWrestlingStateReader(memory);
        var readout = new WonderSquareArmWrestlingReadout();

        Equal(true, reader.TryRead(out var idle), "the idle room reads");
        Equal(false, idle.IsContesting, "an all-zero bank in an idle room is not a bout");
        Equal(null, readout.Observe(idle), "and nothing is said on walking in");

        // Another machine in the same room is not this one.
        memory.RunScript(WonderSquareBasketballStateReader.CloudEntityId,
            WonderSquareBasketballStateReader.ShotScriptId);
        Equal(true, reader.TryRead(out var otherMachine), "the room still reads");
        Equal(false, otherMachine.IsContesting, "the basketball shot is not an arm wrestling bout");
        Equal(null, readout.Observe(otherMachine), "and is still silent");

        // The contest script starts, but its price, instruction and READY windows run
        // before byte 186 seeds the lean, so the bout is still not contesting.
        memory.RunScript(
            WonderSquareArmWrestlingStateReader.UdelEntityId,
            WonderSquareArmWrestlingStateReader.ContestScriptId);
        memory.SetBankByte(WonderSquareArmWrestlingStateReader.GoSignalOffset, 1);
        Equal(true, reader.TryRead(out var ready), "the READY phase reads");
        Equal(true, ready.IsActive, "the machine is running its script");
        Equal(false, ready.IsContesting, "but the arms have not locked yet");
        Equal(null, readout.Observe(ready), "so nothing is said");

        // Byte 182 clears the go signal and byte 186 seeds the level lean.
        memory.SetBankByte(WonderSquareArmWrestlingStateReader.GoSignalOffset, 0);
        memory.SetBankByte(
            WonderSquareArmWrestlingStateReader.LeanOffset,
            WonderSquareArmWrestlingStateReader.LevelLean);
        Equal(true, reader.TryRead(out var bout), "the live bout reads");
        Equal(true, bout.IsContesting, "arms locked and level is a bout");
        var opening = readout.Observe(bout, out var openingPose);
        Equal(true, opening?.StartsWith("Arms locked. Holding level.", StringComparison.Ordinal) == true,
            "the bout opens with its pose");
        Equal(true, opening?.Contains(WonderSquareArmWrestlingReadout.ToneExplanation, StringComparison.Ordinal) == true,
            "and explains what the tones mean before they start");
        Equal(WonderSquareArmWrestlingPose.Level, openingPose, "the opening pose selects the level tone");

        // The finish flag ends it; the native win and loss windows say the outcome.
        memory.SetBankByte(WonderSquareArmWrestlingStateReader.FinishedOffset, 1);
        memory.SetBankByte(
            WonderSquareArmWrestlingStateReader.LeanOffset,
            WonderSquareArmWrestlingStateReader.WinningLean);
        Equal(true, reader.TryRead(out var finished), "the finished bout reads");
        Equal(false, finished.IsContesting, "a finished bout is not contesting");
        Equal(null, readout.Observe(finished), "and the outcome is left to the native windows");

        // Leaving and coming back must not resume the old bout.
        memory.StopScripts(WonderSquareArmWrestlingStateReader.UdelEntityId);
        memory.ClearBank();
        Equal(true, reader.TryRead(out var afterExit), "the room after the bout reads");
        Equal(false, afterExit.IsContesting, "walking away ends it");
        Equal(null, readout.Observe(afterExit), "silently");

        memory.RunScript(
            WonderSquareArmWrestlingStateReader.UdelEntityId,
            WonderSquareArmWrestlingStateReader.ContestScriptId);
        memory.SetBankByte(
            WonderSquareArmWrestlingStateReader.LeanOffset,
            WonderSquareArmWrestlingStateReader.LevelLean);
        Equal(true, reader.TryRead(out var second), "a second bout reads");
        Equal(true, second.IsContesting, "and is a bout in its own right");
        Equal(true, readout.Observe(second) is not null, "which is announced again");
    }

    private static void TheArmWrestlingReadoutDescribesTheVisibleLean()
    {
        var readout = new WonderSquareArmWrestlingReadout();
        Equal(null, readout.Observe(default), "nothing is said outside a bout");

        var level = WonderSquareArmWrestlingStateReader.LevelLean;
        var opening = readout.Observe(new(true, true, level), out var openingPose);
        Equal(true, opening?.StartsWith("Arms locked. Holding level.", StringComparison.Ordinal) == true,
            "the bout is announced with its opening pose");
        Equal(WonderSquareArmWrestlingPose.Level, openingPose, "the opening pose is level");

        Equal(null, readout.Observe(new(true, true, level), out var unchangedPose),
            "an unchanged lean says nothing");
        Equal(WonderSquareArmWrestlingPose.None, unchangedPose,
            "and sounds no tone, however many times the held key is polled");

        Equal("You are pushing them back.", readout.Observe(new(true, true, level + 1), out var aheadPose),
            "a swing towards the opponent is described");
        Equal(WonderSquareArmWrestlingPose.PushingAhead, aheadPose, "and selects the higher tone");
        Equal(null, readout.Observe(new(true, true, level + 2), out var samePose),
            "a further swing the same way is not repeated");
        Equal(WonderSquareArmWrestlingPose.None, samePose, "and sounds nothing");
        Equal("You are being pushed back.", readout.Observe(new(true, true, level - 1), out var backPose),
            "a swing the other way is described");
        Equal(WonderSquareArmWrestlingPose.BeingPushedBack, backPose, "and selects the lower tone");
        Equal("Their arm is down.", readout.Observe(
                new(true, true, WonderSquareArmWrestlingStateReader.WinningLean), out var wonPose),
            "the end of the travel is described");
        Equal(WonderSquareArmWrestlingPose.TheirArmDown, wonPose, "and keeps the higher tone");
        Equal("Your arm is down.", readout.Observe(
                new(true, true, WonderSquareArmWrestlingStateReader.LosingLean), out var lostPose),
            "and so is the other end");
        Equal(WonderSquareArmWrestlingPose.YourArmDown, lostPose, "with the lower tone");

        // Nothing here may present the lean as a number or a gauge; root's footage
        // review found no such thing on screen.
        var everything = new List<string>();
        var walkthrough = new WonderSquareArmWrestlingReadout();
        for (var lean = 0; lean <= WonderSquareArmWrestlingStateReader.WinningLean; lean++)
        {
            var line = walkthrough.Observe(new(true, true, lean));
            if (line is not null)
            {
                everything.Add(line);
            }
        }

        foreach (var line in everything)
        {
            Equal(false, line.Any(char.IsDigit), $"no lean line may contain a number: {line}");
        }

        foreach (var forbidden in new[] { "gauge", "bar", "meter", "percent" })
        {
            Equal(false, everything.Any(line => line.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"no lean line may claim an on-screen \"{forbidden}\"");
        }
    }

    private static FakeFieldScriptMemory BattlerRoom()
    {
        var memory = new FakeFieldScriptMemory
        {
            FieldId = WonderSquare3DBattlerStateReader.BattlerFieldId
        };
        memory.GiveModel(WonderSquare3DBattlerStateReader.DirectorEntityId, 7);
        return memory;
    }

    private static void RunMatch(FakeFieldScriptMemory memory, int stage, int opponentHits, int playerHits)
    {
        memory.RunScript(
            WonderSquare3DBattlerStateReader.DirectorEntityId,
            WonderSquare3DBattlerStateReader.MatchScriptId);
        memory.SetBankByte(WonderSquare3DBattlerStateReader.StageOffset, (byte)stage);
        memory.SetBankByte(WonderSquare3DBattlerStateReader.OpponentHitsTakenOffset, (byte)opponentHits);
        memory.SetBankByte(WonderSquare3DBattlerStateReader.PlayerHitsTakenOffset, (byte)playerHits);
    }

    private static void The3DBattlerReaderRequiresTheMatchLoopToBeRunning()
    {
        // Root's reproduced false start: a stale stage byte held constant across two
        // polls, with no script or model evidence, previously began a match on the
        // second poll. The identity is dic actually running the shared loop.
        var memory = BattlerRoom();
        var reader = new WonderSquare3DBattlerStateReader(memory);
        var readout = new WonderSquare3DBattlerReadout();

        memory.SetBankByte(WonderSquare3DBattlerStateReader.StageOffset, 1);
        Equal(true, reader.TryRead(out var firstPoll), "the idle room reads");
        Equal(false, firstPoll.IsPlaying, "a stale stage byte is not a match");
        Equal(true, reader.TryRead(out var secondPoll), "and reads again");
        Equal(false, secondPoll.IsPlaying, "a second look at the same stale byte is still not a match");
        Equal(0, readout.Observe(secondPoll).Count, "so nothing is said");

        memory.Module = 3;
        Equal(false, reader.TryRead(out _), "only the field module is read");
        memory.Module = FieldPositionReader.FieldModule;

        memory.FieldId = 506;
        Equal(false, reader.TryRead(out _), "another field's temp bank is not the 3D Battler");
        memory.FieldId = WonderSquare3DBattlerStateReader.BattlerFieldId;

        // Another entity's script in the same room is not the match loop.
        memory.RunScript(WonderSquare3DBattlerStateReader.DirectorEntityId + 1,
            WonderSquare3DBattlerStateReader.MatchScriptId);
        Equal(true, reader.TryRead(out var otherEntity), "the room still reads");
        Equal(false, otherEntity.IsPlaying, "another entity running script 3 is not the director");

        RunMatch(memory, 1, 0, 0);
        Equal(true, reader.TryRead(out var live), "a live match reads");
        Equal(true, live.IsPlaying, "the match loop is the controller identity");

        RunMatch(memory, 1, 3, 2);
        Equal(true, reader.TryRead(out var scored), "the running match reads");
        Equal(3, scored.OpponentHitsTaken, "hits taken by the opponent come from Bank[5][13]");
        Equal(2, scored.PlayerHitsTaken, "hits taken by the player's fighter come from Bank[5][12]");
        Equal(false, scored.IsFinished, "the match is not over yet");

        // Ten hits ends it, and the loop stopping ends the reporting.
        RunMatch(memory, 1, WonderSquare3DBattlerStateReader.WinningHits, 2);
        Equal(true, reader.TryRead(out var won), "the winning frame reads");
        Equal(true, won.IsFinished, "ten hits ends the match");

        memory.StopScripts(WonderSquare3DBattlerStateReader.DirectorEntityId);
        Equal(true, reader.TryRead(out var afterMatch), "the room after the match reads");
        Equal(false, afterMatch.IsPlaying, "the match ends when the loop stops");
        Equal(0, readout.Observe(afterMatch).Count, "and the readout falls silent");

        // Out-of-range values while the loop runs are torn reads, not states.
        RunMatch(memory, 1, 0, 0);
        memory.SetBankByte(WonderSquare3DBattlerStateReader.StageOffset, 200);
        Equal(false, reader.TryRead(out _), "an impossible stage is a torn read");
        memory.SetBankByte(WonderSquare3DBattlerStateReader.StageOffset, 1);
        memory.SetBankByte(WonderSquare3DBattlerStateReader.OpponentHitsTakenOffset, 99);
        Equal(false, reader.TryRead(out _), "an impossible hit count is a torn read");

        RunMatch(memory, 1, 0, 0);
        memory.UnreadableAddresses.Add((uint)FieldScriptControllerReader.AddressModelTablePointer);
        Equal(false, reader.TryRead(out _), "an unreadable model table is not a state");
        memory.UnreadableAddresses.Clear();
        Equal(true, reader.TryRead(out _), "and it recovers once readable again");
    }

    /// <summary>
    /// Named arguments throughout, because the two counters are easy to reverse and
    /// root's trace is explicit about which is which: Bank[5][12] counts hits taken
    /// by the player's own fighter and Bank[5][13] hits taken by the opponent.
    /// </summary>
    private static WonderSquare3DBattlerState Match(
        int stage,
        int playerHitsTaken,
        int opponentHitsTaken,
        bool isFinished = false) =>
        new(IsPlaying: true,
            Stage: stage,
            PlayerHitsTaken: playerHitsTaken,
            OpponentHitsTaken: opponentHitsTaken,
            IsFinished: isFinished);

    private static void The3DBattlerReadoutReportsOnlyWhatResolved()
    {
        var readout = new WonderSquare3DBattlerReadout();
        Equal(0, readout.Observe(default).Count, "nothing is said outside a match");

        var opening = readout.Observe(Match(stage: 1, playerHitsTaken: 0, opponentHitsTaken: 0));
        Equal(1, opening.Count, "the match opens with one line");
        Equal("First opponent. First to ten points.", opening[0],
            "the opening names the opponent and the native target");

        Equal(0, readout.Observe(Match(1, 0, 0)).Count, "an unchanged tally says nothing");

        // The opponent's own hit counter rising is the player landing a hit.
        var landed = readout.Observe(Match(stage: 1, playerHitsTaken: 0, opponentHitsTaken: 1));
        Equal(1, landed.Count, "a resolved hit is one line");
        Equal("You land a hit. 1 to 0.", landed[0], "the player's hit carries the running tally");

        var taken = readout.Observe(Match(stage: 1, playerHitsTaken: 1, opponentHitsTaken: 1));
        Equal("They land a hit. 1 to 1.", taken[0], "the opponent's hit carries the running tally");

        // Root's correction: two counters moving between one pair of observations is
        // two resolved hits seen late, not one simultaneous exchange.
        var both = readout.Observe(Match(stage: 1, playerHitsTaken: 2, opponentHitsTaken: 2));
        Equal(2, both.Count, "two counters moving is reported as two resolved hits");
        Equal(false, both.Any(line => line.Contains("Both", StringComparison.OrdinalIgnoreCase)),
            "and never as one simultaneous exchange");

        // The tally must be presented as counted hits, not as a cabinet display.
        var everything = new List<string>();
        var walkthrough = new WonderSquare3DBattlerReadout();
        for (var hits = 0; hits <= WonderSquare3DBattlerStateReader.WinningHits; hits++)
        {
            everything.AddRange(walkthrough.Observe(Match(
                stage: 1,
                playerHitsTaken: 0,
                opponentHitsTaken: hits,
                isFinished: hits >= WonderSquare3DBattlerStateReader.WinningHits)));
        }

        foreach (var forbidden in new[] { "screen", "display", "counter shows", "scoreboard" })
        {
            Equal(false, everything.Any(line => line.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"no line may claim a cabinet \"{forbidden}\"; the reviewed frame shows none");
        }

        // The visible recoil and defeat wording is allowed; it is what the fighters
        // are shown doing after the exchange resolves.
        Equal(true, everything.Any(line => line.Contains("land a hit", StringComparison.Ordinal)),
            "resolved hits are described in plain words");

        // A new opponent restarts the tally rather than reporting a swing.
        var advanced = readout.Observe(Match(stage: 2, playerHitsTaken: 0, opponentHitsTaken: 0));
        Equal("Second opponent.", advanced[0], "the next opponent is announced");
        Equal(0, readout.Observe(Match(2, 0, 0)).Count, "and then nothing until a hit");
        Equal("You land a hit. 1 to 0.",
            readout.Observe(Match(stage: 2, playerHitsTaken: 0, opponentHitsTaken: 1))[0],
            "the new opponent's tally starts from zero");

        // Byte 961 can advance past the fourth opponent; that is the end of the line.
        Equal("You have beaten every opponent.",
            readout.Observe(Match(WonderSquare3DBattlerStateReader.ExhaustedStage, 0, 0))[0],
            "passing the last opponent is stated plainly");

        Equal(0, readout.Observe(default).Count,
            "the end of the match is native dialogue");
    }

    /// <summary>
    /// What the player actually hears, not what the readout returned.
    ///
    /// Both runtimes used to speak every line of a batch with the interrupt flag set,
    /// so a two-line late exchange had its first half cut off part way through by its
    /// second: the player heard that the opponent landed a hit and never learned they
    /// had landed one too. The delivery both adapters now share is what this drives.
    /// </summary>
    private static void The3DBattlerBatchReachesThePlayerWhole()
    {
        var readout = new WonderSquare3DBattlerReadout();
        readout.Observe(Match(stage: 1, playerHitsTaken: 0, opponentHitsTaken: 0));

        var batch = readout.Observe(Match(stage: 1, playerHitsTaken: 1, opponentHitsTaken: 1));
        Equal(2, batch.Count, "the late exchange is two resolved hits");

        // The adapter's own sink: exactly what Speak would receive, in order.
        var spoken = new List<(string Text, bool Interrupt)>();
        foreach (var delivery in WonderSquare3DBattlerReadout.Deliver(batch))
        {
            spoken.Add(delivery);
        }

        Equal(1, spoken.Count, "and reaches the player as one utterance rather than two");
        Equal(true, spoken[0].Interrupt, "which supersedes whatever came before it");
        foreach (var line in batch)
        {
            Equal(true, spoken[0].Text.Contains(line, StringComparison.Ordinal),
                $"carrying every line of the batch; \"{line}\" is missing from \"{spoken[0].Text}\"");
        }

        // The defect stated as a property: nothing inside one batch may interrupt
        // anything else inside it.
        Equal(1, spoken.Count(delivery => delivery.Interrupt),
            "no part of a batch may cut off another part of the same batch");

        // A single line is unchanged, and an empty batch says nothing at all.
        var single = WonderSquare3DBattlerReadout.Deliver(["You land a hit. 1 to 0."]);
        Equal(1, single.Count, "one line is one utterance");
        Equal("You land a hit. 1 to 0.", single[0].Text, "delivered as it was written");
        Equal(0, WonderSquare3DBattlerReadout.Deliver([]).Count, "and an empty batch is silence");
        Equal(0, WonderSquare3DBattlerReadout.Deliver(["", "   "]).Count,
            "as is one that holds nothing worth saying");
    }

    private static void EveryAvailableMachineIsReachable(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        // Each of these is an [OK] or walk-over LINE, so it is invisible to the
        // Exits, Objects and NPCs lists. Coordinates are installed LINE midpoints.
        var expected = new (int Field, string Label, int X, int Y, int Z, string Entity)[]
        {
            (506, "Play the Arm Wrestling machine (optional)", 183, 1610, -255, "udel"),
            (506, "Play the Basketball Game (optional)", -229, 1664, -255, "bsl"),
            (507, "Play G Bike (optional)", 241, 174, 0, "bikeg"),
            (507, "Enter the Mog House (optional)", 3, -252, 0, "mogu"),
            (507, "Use the Fortune Telling machine (optional)", 299, 153, 0, "la")
        };
        foreach (var (field, label, x, y, z, entity) in expected)
        {
            var rows = Active(definitions, field, 440).Where(row => row.Label == label).ToArray();
            Equal(1, rows.Length, $"field {field} must offer exactly one '{label}'");
            Equal((x, y, z), (rows[0].X, rows[0].Y, rows[0].Z),
                $"'{label}' must sit on its installed trigger line midpoint");
            Equal(entity, rows[0].SourceEntityName, $"'{label}' must record its native line entity");
            Equal(true, rows[0].TriggerLine is not null, $"'{label}' must carry its native trigger line");
            Equal(true, rows[0].Priority > 0, $"'{label}' is optional and must never outrank an objective");
        }
    }

    /// <summary>
    /// The Shooting Coaster is not one of the walk-over machines above, and treating it as
    /// one is what put a target on jetin1 entity 10. That entity is a LINE whose Go 1x
    /// calls the attendant's script 3 - "This way, Sir, to register." - so it turns the
    /// player back rather than boarding them.
    ///
    /// <para>Registration is the guide and then the attendant. Her Talk sets 3[72] bit 4
    /// at byte 10; his answers "Is this your first time?" at byte 82 until it is set, and
    /// only then asks the question, takes the ten GP at byte 164, unlocks triangles 54,
    /// 38, 37, 35 and 34, and starts the ride. Bit 5 is the ride itself.</para>
    /// </summary>
    private static void TheShootingCoasterIsRegisteredWithPeopleNotWithTheBarrier(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        var rows = Active(definitions, 487, 440);
        Equal(false, rows.Any(row => row.EntityId == 10 || row.SourceEntityName == "che"),
            "the barrier line must never be a target: walking onto it is what turns the player back");

        var guide = rows.Single(row =>
            row.Label == "Ask the Shooting Coaster guide about the ride (optional)");
        Equal(12, guide.EntityId, "the guide is jetin1 entity 12");
        Equal("Talk", guide.SourceScriptType, "and she is reached by her native Talk");
        Equal(true, guide.Priority > 0, "the coaster is optional and must never outrank an objective");

        var attendant = rows.Single(row =>
            row.Label == "Talk to the Shooting Coaster attendant to register (optional)");
        Equal(11, attendant.EntityId, "the attendant is jetin1 entity 11");
        Equal("Talk", attendant.SourceScriptType, "and he is reached by his native Talk");
        Equal(true, attendant.Priority > 0, "registering is optional too");

        // The order the scripts require, and nothing about the money or the answer.
        Equal(true, guide.RequiredConditions.Any(condition =>
                condition.Bank == 3 && condition.Address == 72 && condition.Mask == 0x10 && condition.Value == 0x00),
            "the guide is offered while her own flag is still clear");
        Equal(true, attendant.RequiredConditions.Any(condition =>
                condition.Bank == 3 && condition.Address == 72 && condition.Mask == 0x10 && condition.Value == 0x10),
            "the attendant is offered only once she has explained it");
        foreach (var row in new[] { guide, attendant })
        {
            Equal(true, row.RequiredConditions.Any(condition =>
                    condition.Bank == 3 && condition.Address == 72 && condition.Mask == 0x20 && condition.Value == 0x00),
                $"'{row.Label}' must stand down once the ride itself is running");
        }
    }

    private static void TheSnowGameIsNotOfferedBeforeItsNativeGate(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        // games_2/snowb and snowb2 test `$GameMoment < 790` and answer with a
        // bystander line instead of the game, so it must not be advertised here.
        //
        // The Submarine Game is blocked differently and this is the case that
        // caught an earlier false claim: its own [OK] script has no GameMoment gate
        // at all, but entity 9 m6 stands solid on its LINE until $GameMoment >= 1299
        // and says the machine is out of order. A machine's own script is not
        // sufficient evidence that a machine is reachable.
        foreach (var moment in new[] { 440, 441, 442, 443, 444 })
        {
            foreach (var closed in new[] { "Snow", "Submarine" })
            {
                Equal(0, Active(definitions, 507, moment)
                        .Count(row => row.Label.Contains(closed, StringComparison.OrdinalIgnoreCase)),
                    $"the {closed} game must not be offered at GameMoment {moment}");
            }
        }

        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is required.");
        var scripts = new FieldScriptNavigationCatalog(dataRoot);
        // games_2 entity 9 m6: the submarine blocker's own gate and its notice.
        AssertBytes(scripts, 507, 9, 0, 16, "1620000013050407");
        AssertBytes(scripts, 507, 9, 1, 14, "40011F");
        // games_2 entity 8 m5 is explicitly NonSolid, so the snow game really is
        // gated only by its own script and not by a body standing in front of it.
        AssertBytes(scripts, 507, 8, 0, 16, "C701");
    }

    private static void AssertBytes(
        FieldScriptNavigationCatalog scripts, int field, int entity, int script, int offset, string expected)
    {
        var opcode = scripts.ReadScriptOpcodes(field, entity, script).Single(item => item.ByteIndex == offset);
        Equal(expected, Convert.ToHexString(opcode.Bytes.ToArray()),
            $"installed native script anchor {field}:{entity}:{script}:{offset}");
    }

    private static void TheCoasterReaderOnlyTrustsItsOwnModule()
    {
        var memory = new FakeMemory();
        var reader = new SpeedSquareCoasterStateReader(memory);

        // Field module: the coaster globals are stale from a previous session.
        memory.Module = 1;
        memory.CursorX = 160;
        memory.CursorY = 120;
        memory.ShotPower = 128;
        Equal(false, reader.TryRead(out _), "the reader must ignore every module but the coaster");

        memory.Module = SpeedSquareCoasterStateReader.CoasterModule;
        Equal(true, reader.TryRead(out var state), "the coaster module must produce a reading");
        Equal((160, 120, 128), (state.CursorX, state.CursorY, state.ShotPower),
            "the reader must return the native sight and charge");
        Equal(false, state.IsSuspended, "a zero suspend byte means the sight is live");

        memory.Suspended = 1;
        Equal(true, reader.TryRead(out var suspended), "a suspended coaster is still a reading");
        Equal(true, suspended.IsSuspended, "a non-zero suspend byte must be reported");

        // FUN_005EE150 clamps the sight to 0..320 and 0..240 and the charge to 128,
        // so anything outside that is a torn read across the game's own write.
        memory.Suspended = 0;
        foreach (var (x, y, power, why) in new[]
                 {
                     (321, 120, 128, "an x past the native clamp"),
                     (160, 241, 128, "a y past the native clamp"),
                     (160, 120, 129, "a charge past the native clamp"),
                     (-1, 120, 128, "a negative x")
                 })
        {
            memory.CursorX = x;
            memory.CursorY = y;
            memory.ShotPower = power;
            Equal(false, reader.TryRead(out _), $"{why} must be rejected, not spoken");
        }
    }

    private static void TheCoasterReadoutSpeaksAimAndCharge()
    {
        var readout = new SpeedSquareCoasterReadout();
        Equal(null, readout.Observe(default), "no speech while the coaster is not running");

        // Entering: the native init puts the sight dead centre at full charge.
        Equal("Shooting Coaster. sight centred. charge 8 of 8",
            readout.Observe(new(true, false, 160, 120, 128)), "entering announces the game and the aim");

        // One horizontal step is 40 native pixels.
        Equal("sight left 1.", readout.Observe(new(true, false, 119, 120, 128)),
            "moving a full step left is announced");
        Equal("sight left 1, up 1.", readout.Observe(new(true, false, 119, 89, 128)),
            "a diagonal reports both axes");
        Equal("sight centred.", readout.Observe(new(true, false, 160, 120, 128)),
            "returning to the middle cell is announced");

        // Charge is reported in eighths of the native 0..128 range.
        Equal("charge 4 of 8", readout.Observe(new(true, false, 160, 120, 64)),
            "a charge change alone is announced without repeating the aim");

        // While the native routine ignores input the sight cannot be moving.
        readout.Reset();
        Equal("Shooting Coaster.", readout.Observe(new(true, true, 160, 120, 128)),
            "a suspended coaster announces itself without a frozen aim readout");
        Equal(null, readout.Observe(new(true, true, 40, 40, 16)),
            "no aim speech while the native routine is ignoring input");
    }

    private static void TheCoasterReadoutStaysQuietWhenNothingVisibleChanged()
    {
        var readout = new SpeedSquareCoasterReadout();
        _ = readout.Observe(new(true, false, 160, 120, 128));
        Equal(null, readout.Observe(new(true, false, 160, 120, 128)),
            "an unchanged sample must not repeat");
        // Still inside the same cell and the same eighth of charge.
        Equal(null, readout.Observe(new(true, false, 175, 130, 128)),
            "sub-step drift must not produce chatter");
        // 127 of 128 is genuinely the seventh eighth, so it is a real change.
        Equal("charge 7 of 8", readout.Observe(new(true, false, 175, 130, 127)),
            "crossing an eighth of charge is announced even without moving the sight");
        Equal(null, readout.Observe(default), "leaving the coaster is silent");
        Equal("Shooting Coaster. sight centred. charge 8 of 8",
            readout.Observe(new(true, false, 160, 120, 128)),
            "re-entering the coaster announces it again");
    }

    private static IReadOnlyList<FieldStoryEventDefinition> Active(
        IReadOnlyList<FieldStoryEventDefinition> definitions, int field, int moment) =>
        definitions
            .Where(row => row.FieldId == field)
            .Where(row => row.MinimumGameMoment < 0 || moment >= row.MinimumGameMoment)
            .Where(row => row.MaximumGameMoment < 0 || moment <= row.MaximumGameMoment)
            .ToArray();

    private sealed class FakeMemory : ILegacyAddressSpace
    {
        public byte Module { get; set; } = 1;
        public byte Suspended { get; set; }
        public int CursorX { get; set; }
        public int CursorY { get; set; }
        public int ShotPower { get; set; }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            switch (virtualAddress)
            {
                case (uint)FieldPositionReader.AddressCurrentModule when destination.Length == 1:
                    destination[0] = Module;
                    return true;
                case (uint)SpeedSquareCoasterStateReader.AddressSuspended when destination.Length == 1:
                    destination[0] = Suspended;
                    return true;
                case (uint)SpeedSquareCoasterStateReader.AddressCursorX when destination.Length == 2:
                    return TryWriteInt16(destination, CursorX);
                case (uint)SpeedSquareCoasterStateReader.AddressCursorY when destination.Length == 2:
                    return TryWriteInt16(destination, CursorY);
                case (uint)SpeedSquareCoasterStateReader.AddressShotPower when destination.Length == 2:
                    return TryWriteInt16(destination, ShotPower);
                default:
                    return false;
            }
        }

        private static bool TryWriteInt16(Span<byte> destination, int value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(destination, (short)value);
            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Gold Saucer minigames: {message}; expected {expected}, actual {actual}.");
    }
}
