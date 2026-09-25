using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The field catalog reads scripts the way the PC engine runs them. Each case is a small
/// synthetic field built around one native rule, read through the production catalog.
///
/// <para>The rules come from the engine's own handlers, from root's Ghidra export of
/// legacy ff7_en.exe (reports/root-native-field-findings.md):</para>
/// <list type="bullet">
///   <item><description>JMPFL 00613141: IP + word + 1.</description></item>
///   <item><description>KEY 00612303, IFPRTY 0061C696 and IFMEMB 0061C75E fork.</description></item>
///   <item><description>RETTO 00612E47 loads the named slot from the 32-slot table.</description></item>
///   <item><description>The event dispatcher 0060D29B reads <c>entity*0x40 + slot*2</c> and never
///   compares slots, so a walk slot that repeats [OK]'s pointer runs that code when walked on.</description></item>
///   <item><description>REQ and REQSW (006127A2 modes 1 and 2) do not wait for the callee.</description></item>
///   <item><description>Every native bank writer changes the value it writes.</description></item>
///   <item><description>SPECIAL 0061E78C advances F7 by four bytes.</description></item>
///   <item><description>0x1A-0x1C go to the unimplemented handler 006107E1, which does not advance.</description></item>
///   <item><description>REQEW (006127A2 mode 3) waits for the callee to finish.</description></item>
///   <item><description>0060C94D starts a LINE's scripts 1 to 6 from its events, and nothing higher.</description></item>
///   <item><description>0060C94D runs each entity up to eight opcodes a pass, so a Main or an
///   unwaited request can write between any two of another script's opcodes.</description></item>
///   <item><description>SETX 00610AFE writes a byte at a computed index in its block.</description></item>
///   <item><description>PC 0061BCD7 and CC 006142D5 set the controlled model, script context +0x2A.</description></item>
///   <item><description>MAPJUMP 006131C4 waits without advancing, and the field loop 0063C17F answers
///   it by loading the destination, its own field included, so the script never goes on.</description></item>
///   <item><description>Field setup 0060BCFA controls model 0, the Init executor 0060C683 runs Inits
///   in entity order, CHAR 00614353 numbers models in load order, and only PC, 0061BC67 and CC
///   change the controlled model.</description></item>
///   <item><description>PRTYP, PRTYM, PRTYE, MMBud and MENU's return write the party, bank 3[9..11],
///   directly; so do the other handlers in root's direct-reference scan.</description></item>
/// </list>
///
/// <para>The cases from ALoopRevisitsATestOnceItsValueChanges to AWordAtOffset255EndsInTheNextBlock
/// are root's independent execution probes (artifacts/root-execution-review), each of the
/// first six of which failed against the first repaired catalog. The cases from
/// PartyOperationsEndAFoldedPartyByte on carry root's second review round (reports/root-stage2-
/// review-blockers.md); each failed against the build that review measured.</para>
/// </summary>
internal static class FieldScriptExecutionModelTests
{
    private static readonly (string Name, Action Case)[] Cases =
    [
        (nameof(JmpflLandsOnItsTarget), JmpflLandsOnItsTarget),
        (nameof(KeyTestsFork), KeyTestsFork),
        (nameof(PartyMemberTestsFork), PartyMemberTestsFork),
        (nameof(WalkSlotSharingOkPointerRunsWhenWalkedOn), WalkSlotSharingOkPointerRunsWhenWalkedOn),
        (nameof(RettoRunsTheNamedScriptInsteadOfTheNextByte), RettoRunsTheNamedScriptInsteadOfTheNextByte),
        (nameof(AliasedRequestTargetsRun), AliasedRequestTargetsRun),
        (nameof(ControlFlowContinuesPastTheNextSlotsPointer), ControlFlowContinuesPastTheNextSlotsPointer),
        (nameof(ConstantsAreNotFoldedAcrossAnotherScriptsWrite), ConstantsAreNotFoldedAcrossAnotherScriptsWrite),
        (nameof(LaterWritesInvalidateAFoldedConstant), LaterWritesInvalidateAFoldedConstant),
        (nameof(AsynchronousRequestsDoNotReturnConstants), AsynchronousRequestsDoNotReturnConstants),
        (nameof(SynchronousRequestsKeepTheirConstants), SynchronousRequestsKeepTheirConstants),
        (nameof(FoldingStillDropsAProvablyDeadLanding), FoldingStillDropsAProvablyDeadLanding),
        (nameof(InitOnlyWritesDoNotBlockFolding), InitOnlyWritesDoNotBlockFolding),
        (nameof(NativeSpecialLengthsKeepTheDecodeAligned), NativeSpecialLengthsKeepTheDecodeAligned),
        (nameof(UnimplementedHandlersStopTheScript), UnimplementedHandlersStopTheScript),
        (nameof(PartySlotRequestsAreNotTheNpcsOwnDialogue), PartySlotRequestsAreNotTheNpcsOwnDialogue),
        (nameof(PartySlotRequestsAreNotCounterRequests), PartySlotRequestsAreNotCounterRequests),
        (nameof(UnreachableDialogueIsNotAttributed), UnreachableDialogueIsNotAttributed),
        (nameof(APartyRequestThatMovesNobodyDoesNotEndTheWalk), APartyRequestThatMovesNobodyDoesNotEndTheWalk),
        (nameof(ALoopRevisitsATestOnceItsValueChanges), ALoopRevisitsATestOnceItsValueChanges),
        (nameof(AConcurrentIndexedWriteEndsAFoldedValue), AConcurrentIndexedWriteEndsAFoldedValue),
        (nameof(AMainStaysConcurrentWhenTheWalkAlsoAsksForItsCode), AMainStaysConcurrentWhenTheWalkAlsoAsksForItsCode),
        (nameof(WaitingForAScriptThatNeverFinishesEndsTheCaller), WaitingForAScriptThatNeverFinishesEndsTheCaller),
        (nameof(WaitingForAnUnimplementedHandlerEndsTheCaller), WaitingForAnUnimplementedHandlerEndsTheCaller),
        (nameof(AnUnrequestedHighLineScriptIsNotAWalkEvent), AnUnrequestedHighLineScriptIsNotAWalkEvent),
        (nameof(AWordAtOffset255EndsInTheNextBlock), AWordAtOffset255EndsInTheNextBlock),
        (nameof(ARequestedHighLineScriptStillRuns), ARequestedHighLineScriptStillRuns),
        (nameof(ALoopThatNeverChangesAnythingEnds), ALoopThatNeverChangesAnythingEnds),
        (nameof(NothingAfterAMapJumpHappensInTheField), NothingAfterAMapJumpHappensInTheField),
        (nameof(AJumpIntoTheSameFieldEndsTheWalkToo), AJumpIntoTheSameFieldEndsTheWalkToo),
        (nameof(EachPartyMembersCopyKeepsItsOwnLanding), EachPartyMembersCopyKeepsItsOwnLanding),
        (nameof(AgreeingPartyCopiesAreOneTraversalForEveryMember), AgreeingPartyCopiesAreOneTraversalForEveryMember),
        (nameof(OnlyAModelThatCanBeControlledMovesThePlayer), OnlyAModelThatCanBeControlledMovesThePlayer),
        (nameof(ACcTargetCanBeThePlayer), ACcTargetCanBeThePlayer),
        (nameof(TheTrackerOffersAMembersTraversalOnlyWhileItsModelIsControlled), TheTrackerOffersAMembersTraversalOnlyWhileItsModelIsControlled),
        (nameof(TheControlledEntityIsTheOneWhoseModelIsControlled), TheControlledEntityIsTheOneWhoseModelIsControlled),
        (nameof(ATornOrUnreadableControlReadIsNoAnswer), ATornOrUnreadableControlReadIsNoAnswer),
        (nameof(PartyOperationsEndAFoldedPartyByte), PartyOperationsEndAFoldedPartyByte),
        (nameof(DirectNativeWritersEndFoldedValues), DirectNativeWritersEndFoldedValues),
        (nameof(APartyChangeInAMainIsAConcurrentWrite), APartyChangeInAMainIsAConcurrentWrite),
        (nameof(AModelsJumpIsOfferedOnlyOnAPositiveControlAnswer), AModelsJumpIsOfferedOnlyOnAPositiveControlAnswer),
        (nameof(CcInsideTheRoutineMakesTheNamedModelsJumpThePlayers), CcInsideTheRoutineMakesTheNamedModelsJumpThePlayers),
        (nameof(AnInitThatSkipsItsCharLeavesModelZeroToTheNext), AnInitThatSkipsItsCharLeavesModelZeroToTheNext),
        (nameof(APartyChangeHandsControlToTheNewLeader), APartyChangeHandsControlToTheNewLeader),
        (nameof(ATalkThatSharesInitsPointerIsTalkForAModelOnly), ATalkThatSharesInitsPointerIsTalkForAModelOnly),
        (nameof(OnlyARunningPollIsALadderAndItMovesOnlyTheFamily), OnlyARunningPollIsALadderAndItMovesOnlyTheFamily),
        (nameof(AMoverPastTheMaskIsOfferedToNobody), AMoverPastTheMaskIsOfferedToNobody),
        (nameof(APartySlotRequestNeedsItsMemberInSlotZero), APartySlotRequestNeedsItsMemberInSlotZero),
        (nameof(ALeaderTestSendsEachLeaderItsOwnWay), ALeaderTestSendsEachLeaderItsOwnWay),
        (nameof(ThePartyLeaderIsReadBetweenTheSameBookends), ThePartyLeaderIsReadBetweenTheSameBookends),
        (nameof(AnExitWhoseMapJumpAFlagDecidesIsGuardedByIt), AnExitWhoseMapJumpAFlagDecidesIsGuardedByIt),
        (nameof(AGuardIsOnlyAValueTheLineStartsWith), AGuardIsOnlyAValueTheLineStartsWith),
        (nameof(TheGuardsAreReadLive), TheGuardsAreReadLive),
        (nameof(AGuardComparesTheWayTheNativeComparatorsDo), AGuardComparesTheWayTheNativeComparatorsDo),
        (nameof(ALineThatAsksAnotherScriptToJumpIsGuardedByItsOwnTests), ALineThatAsksAnotherScriptToJumpIsGuardedByItsOwnTests)
    ];

    public static void Run()
    {
        foreach (var (_, testCase) in Cases)
        {
            testCase();
        }
    }

    /// <summary>Runs every case and reports each one, instead of stopping at the first failure.</summary>
    public static int Report()
    {
        var failures = 0;
        foreach (var (name, testCase) in Cases)
        {
            try
            {
                testCase();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        return failures;
    }

    /// <summary>A director (entity 0) and a LINE (entity 1) whose Go script, slot 4, is <paramref name="go"/>.</summary>
    private static SyntheticFieldScript LineWithGo(Action<SyntheticScriptCode> go, Action<SyntheticFieldScript>? more = null)
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("lineInit").Line(0, 0, 0, 100, 0, 0).Ret().Label("lineMain").Ret();
        code.Label("ok").Ret();
        code.Label("go");
        go(code);
        field.Entity("dir", (0, "dirInit"));
        field.Entity("exit", (0, "lineInit"), (1, "ok"), (4, "go"));
        more?.Invoke(field);
        return field;
    }

    /// <summary>
    /// The field's first model, entity 2 when added straight after <see cref="LineWithGo"/>:
    /// the first entity whose Init runs CHAR has model 0, which field setup makes the
    /// controlled one (0060BCFA, 00614353). Its script 3 is <paramref name="three"/>.
    /// </summary>
    private static void PlayerModel(SyntheticFieldScript field, Action<SyntheticScriptCode> three)
    {
        field.Code.Label("heroInit").Char(0).Ret().Label("heroMain").Ret().Label("heroIdle").Ret().Label("hero3");
        three(field.Code);
        field.Entity("hero", (0, "heroInit"), (1, "heroIdle"), (3, "hero3"));
    }

    private static int[] ExitDestinations(FieldScriptNavigationReadResult result, int entity = 1) =>
        result.Exits
            .Where(exit => exit.TriggerEntityId == entity)
            .SelectMany(exit => exit.DestinationFieldIds ?? [])
            .Distinct()
            .Order()
            .ToArray();

    private static void JmpflLandsOnItsTarget()
    {
        var result = LineWithGo(code => code.JmpFL("jump").Nop().Nop().Nop().Label("jump").MapJump(123).Ret()).Read();
        Sequence([123], ExitDestinations(result), "a JMPFL lands on the opcode its operand names, one byte past the operand");
    }

    private static void KeyTestsFork()
    {
        var result = LineWithGo(code => code.IfKey(0x20, "released").Ret().Label("released").MapJump(124).Ret()).Read();
        Sequence([124], ExitDestinations(result), "a key test's taken side is walked");
    }

    private static void PartyMemberTestsFork()
    {
        var result = LineWithGo(code => code.IfPartyMember(5, "absent").Ret().Label("absent").MapJump(129).Ret()).Read();
        Sequence([129], ExitDestinations(result), "a party-member test's taken side is walked");
    }

    /// <summary>Slots 2 and 3 repeat slot 1's pointer, so walking along the line runs the [OK] code.</summary>
    private static void WalkSlotSharingOkPointerRunsWhenWalkedOn()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("lineInit").Line(0, 0, 0, 100, 0, 0).Ret().Label("lineMain").Ret();
        code.Label("ok").MapJump(126).Ret();
        code.Label("rest").Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("exit", (0, "lineInit"), (1, "ok"), (4, "rest"));
        var result = field.Read();
        Sequence([126], ExitDestinations(result), "a [Move] slot on [OK]'s pointer is a walk-in exit");
    }

    /// <summary>Talk: MESSAGE 10, RETTO 4, MESSAGE 11. The engine shows 10, then runs slot 4.</summary>
    private static void RettoRunsTheNamedScriptInsteadOfTheNextByte()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("npcInit").Char(0).Ret().Label("npcMain").Ret();
        code.Label("talk").Message(0, 10).Retto(4).Message(0, 11).Ret();
        code.Label("four").Message(0, 12).Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("npc", (0, "npcInit"), (1, "talk"), (4, "four"));
        var npc = field.Read().Npcs.Single(definition => definition.EntityId == 1);
        Sequence([10, 12], npc.DialogIds.Order(), "RETTO continues in slot 4 and never reaches the bytes after it");
    }

    /// <summary>A request of a slot that repeats another slot's pointer runs that code.</summary>
    private static void AliasedRequestTargetsRun()
    {
        var result = LineWithGo(code => code.Req(2, 5).Ret(), field =>
        {
            field.Code.Label("evtInit").Ret().Label("evtMain").Ret();
            field.Code.Label("evt4").MapJump(134).Ret();
            field.Entity("evt", (0, "evtInit"), (4, "evt4"));
        }).Read();
        Sequence([134], ExitDestinations(result), "REQ of slot 5, which repeats slot 4's pointer, runs slot 4's code");
    }

    /// <summary>The Go script jumps back into code that lies inside script 0's range.</summary>
    private static void ControlFlowContinuesPastTheNextSlotsPointer()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("lineInit").Line(0, 0, 0, 100, 0, 0).Ret().Label("lineMain").Ret()
            .Label("shared").MapJump(128).Ret();
        code.Label("ok").Ret();
        code.Label("go").JmpB("shared");
        field.Entity("dir", (0, "dirInit"));
        field.Entity("exit", (0, "lineInit"), (1, "ok"), (4, "go"));
        Sequence([128], ExitDestinations(field.Read()), "control flow is followed wherever it goes in the code");
    }

    /// <summary>
    /// The line clears 5[7] and waits for it to become 1, which entity 2's Main does on its
    /// own (tunnel_2's e8.s4 does exactly this).
    /// </summary>
    private static void ConstantsAreNotFoldedAcrossAnotherScriptsWrite()
    {
        var result = LineWithGo(code => code
            .SetByte(5, 7, 0)
            .Label("wait")
            .IfUb(5, 7, 0, 1, 0, "notYet")
            .MapJump(125)
            .Ret()
            .Label("notYet")
            .Wait(1)
            .JmpB("wait"), field =>
        {
            field.Code.Label("sigInit").Ret().Label("sigMain").SetByte(5, 7, 1).Ret();
            field.Entity("signal", (0, "sigInit"));
        }).Read();
        Sequence([125], ExitDestinations(result), "a value another script writes is not assumed constant");
    }

    /// <summary>INC changes a value SETBYTE set; the walk may not keep the old one.</summary>
    private static void LaterWritesInvalidateAFoldedConstant()
    {
        var result = LineWithGo(code => code
            .SetByte(5, 1, 0)
            .Inc(5, 1)
            .IfUb(5, 1, 0, 1, 0, "skip")
            .MapJump(131)
            .Label("skip")
            .Ret()).Read();
        Sequence([131], ExitDestinations(result), "a later write to the same byte ends the folded constant");
    }

    /// <summary>REQ does not wait, so what the requested script writes may or may not have happened.</summary>
    private static void AsynchronousRequestsDoNotReturnConstants()
    {
        var result = LineWithGo(code => code
            .Req(2, 3)
            .IfUb(5, 4, 0, 1, 0, "no")
            .MapJump(135)
            .Ret()
            .Label("no")
            .MapJump(136)
            .Ret(), field =>
        {
            field.Code.Label("evtInit").Ret().Label("evtMain").Ret();
            field.Code.Label("evt3").SetByte(5, 4, 1).Ret();
            field.Entity("evt", (0, "evtInit"), (3, "evt3"));
        }).Read();
        Sequence([135, 136], ExitDestinations(result), "after REQ both outcomes of the requested write are possible");
    }

    /// <summary>REQEW waits, so the requested script's write has happened when the test runs.</summary>
    private static void SynchronousRequestsKeepTheirConstants()
    {
        var result = LineWithGo(code => code
            .ReqEw(2, 3)
            .IfUb(5, 4, 0, 1, 0, "no")
            .MapJump(135)
            .Ret()
            .Label("no")
            .MapJump(136)
            .Ret(), field =>
        {
            field.Code.Label("evtInit").Ret().Label("evtMain").Ret();
            field.Code.Label("evt3").SetByte(5, 4, 1).Ret();
            field.Entity("evt", (0, "evtInit"), (3, "evt3"));
        }).Read();
        Sequence([135], ExitDestinations(result), "after REQEW the requested write has happened");
    }

    /// <summary>
    /// The line sets 5[0] to 1 and waits for the player's model, whose script climbs to
    /// triangle 11 when 5[0] is 1 and to 22 otherwise.
    /// </summary>
    private static SyntheticFieldScript LandingChoice(Action<SyntheticFieldScript>? more = null) =>
        LineWithGo(code => code.SetByte(5, 0, 1).ReqEw(2, 3).Ret(), field =>
        {
            PlayerModel(field, code => code
                .IfUb(5, 0, 0, 1, 0, "other")
                .Ladder(10, 20, 30, 11, 1)
                .Ret()
                .Label("other")
                .Ladder(40, 50, 60, 22, 1)
                .Ret());
            more?.Invoke(field);
        });

    private static int[] LadderTriangles(FieldScriptNavigationReadResult result) =>
        result.Transitions
            .Where(transition => transition.Kind == FieldNavigationTransitionKind.Ladder)
            .Select(transition => transition.TargetTriangle)
            .Distinct()
            .Order()
            .ToArray();

    /// <summary>Per-path landings stay exact: a value only this script writes still decides the branch.</summary>
    private static void FoldingStillDropsAProvablyDeadLanding()
    {
        Sequence([11], LadderTriangles(LandingChoice().Read()), "the landing the script's own constant rules out is not offered");
    }

    /// <summary>
    /// Init runs before any event, so its write cannot interleave with the line's. The
    /// entity's unused slots point at a RET of their own, as the shipped files lay them out;
    /// a slot that pointed back at Init would make Init an event script too.
    /// </summary>
    private static void InitOnlyWritesDoNotBlockFolding()
    {
        var result = LandingChoice(field =>
        {
            field.Code.Label("resetInit").SetByte(5, 0, 0).Ret().Label("resetMain").Ret().Label("resetIdle").Ret();
            field.Entity("reset", (0, "resetInit"), (1, "resetIdle"));
        }).Read();
        Sequence([11], LadderTriangles(result), "an Init-only write leaves the line's own constant usable");
    }

    /// <summary>SPECIAL F7 is four bytes; read as three, its last operand byte would be a RET.</summary>
    private static void NativeSpecialLengthsKeepTheDecodeAligned()
    {
        var result = LineWithGo(code => code.Raw(0x0F, 0xF7, 0x00, 0x00).MapJump(132).Ret()).Read();
        Sequence([132], ExitDestinations(result), "SPECIAL F7 advances four bytes");
    }

    /// <summary>0x1B is not a Nanaki test on PC: its handler returns without advancing.</summary>
    private static void UnimplementedHandlersStopTheScript()
    {
        var result = LineWithGo(code => code.Raw(0x1B, 0x05, 0x00).MapJump(133).Ret()).Read();
        Sequence([], ExitDestinations(result), "nothing after an unimplemented handler runs");
    }

    /// <summary>
    /// Talk: PREQ 0 3. Party slot 0 runs its own script 3; entity 0's script 3 is somebody
    /// else's, and a party member's line is not the NPC's own.
    /// </summary>
    private static void PartySlotRequestsAreNotTheNpcsOwnDialogue()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret().Label("dir3").Message(0, 9).Ret();
        code.Label("npcInit").Char(0).Ret().Label("npcMain").Ret().Label("talk").Message(0, 7).Preq(0, 3).Ret();
        code.Label("cloudInit").Char(1).Pc(0).Ret().Label("cloudMain").Ret().Label("cloud3").Message(0, 8).Ret();
        field.Entity("dir", (0, "dirInit"), (3, "dir3"));
        field.Entity("npc", (0, "npcInit"), (1, "talk"));
        field.Entity("cloud", (0, "cloudInit"), (3, "cloud3"));
        var npc = field.Read().Npcs.Single(definition => definition.EntityId == 1);
        Sequence([7], npc.DialogIds.Order(), "neither entity 0's line nor the party member's is the NPC's dialogue");
    }

    /// <summary>
    /// A counter LINE's [OK] asks party slot 1 to run script 1. Entity 1 is an ordinary model
    /// with nothing of its own to say; the request is not addressed to it.
    /// </summary>
    private static void PartySlotRequestsAreNotCounterRequests()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("npcInit").Char(0).Ret().Label("npcMain").Ret().Label("npcTalk").Ret();
        code.Label("lineInit").Line(0, 0, 0, 100, 0, 0).Ret().Label("lineMain").Ret()
            .Label("lineOk").Preq(1, 1).Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("npc", (0, "npcInit"), (1, "npcTalk"));
        field.Entity("counter", (0, "lineInit"), (1, "lineOk"));
        var result = field.Read();
        True(result.Npcs.All(definition => definition.EntityId != 1),
            "a party-slot request does not make entity 1 somebody served across the counter");
    }

    private static void UnreachableDialogueIsNotAttributed()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("npcInit").Char(0).Ret().Label("npcMain").Ret();
        code.Label("talk").JmpF("end").Message(0, 13).Label("end").Message(0, 14).Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("npc", (0, "npcInit"), (1, "talk"));
        var npc = field.Read().Npcs.Single(definition => definition.EntityId == 1);
        Sequence([14], npc.DialogIds, "a message the Talk script jumps over is not the NPC's");
    }

    /// <summary>
    /// The line asks the leader to run script 3, which only animates, then MAPJUMPs. A party
    /// request with no navigation to agree on is not the end of the line's script.
    /// </summary>
    private static void APartyRequestThatMovesNobodyDoesNotEndTheWalk()
    {
        var result = LineWithGo(code => code.Preq(0, 3).MapJump(137).Ret(), field =>
        {
            field.Code.Label("cloudInit").Char(1).Pc(0).Ret().Label("cloudMain").Ret()
                .Label("cloud3").Raw(0xAE, 0x03, 0x01).Ret();
            field.Entity("cloud", (0, "cloudInit"), (3, "cloud3"));
        }).Read();
        Sequence([137], ExitDestinations(result), "the walk goes on after a party request that moves nobody");
    }

    /// <summary>
    /// 5[0] starts at 0; the test fails, INC changes it, and the jump back reaches the same
    /// test with the value no longer known. The walk must look again rather than stop because
    /// it has been at that offset before.
    /// </summary>
    private static void ALoopRevisitsATestOnceItsValueChanges()
    {
        var result = LineWithGo(code => code
            .SetByte(5, 0, 0)
            .Label("test")
            .IfUb(5, 0, 0, 1, 0, "increment")
            .MapJump(123)
            .Ret()
            .Label("increment")
            .Inc(5, 0)
            .JmpB("test")).Read();
        Sequence([123], ExitDestinations(result), "a loop whose value changes reaches its exit");
    }

    /// <summary>
    /// Another entity's Main runs SETX 9D 50 00 00 00 01 00, which writes 1 at temporary
    /// offset 0 (00610AFE); the line's nine NOPs give it the time. The destination is not a
    /// named byte, but it is a write to that block.
    /// </summary>
    private static void AConcurrentIndexedWriteEndsAFoldedValue()
    {
        var result = LineWithGo(code =>
        {
            code.SetByte(5, 0, 0);
            for (var index = 0; index < 9; index++)
            {
                code.Nop();
            }

            code.IfUb(5, 0, 0, 1, 0, "done").MapJump(124).Label("done").Ret();
        }, field =>
        {
            field.Code.Label("writerInit").Ret().Label("writerMain")
                .Raw(0x9D, 0x50, 0, 0, 0, 1, 0).Ret().Label("writerIdle").Ret();
            field.Entity("writer", (0, "writerInit"), (1, "writerIdle"));
        }).Read();
        Sequence([124], ExitDestinations(result), "a concurrent SETX ends what the walk knew about its block");
    }

    /// <summary>
    /// The line's REQEW of the writer's slot 3 sits behind a test that is provably false, and
    /// slot 3 is the writer's Main code. Main runs on its own whatever the line asks for, so
    /// its write to 5[0] can land during the line's twenty-four NOPs.
    /// </summary>
    private static void AMainStaysConcurrentWhenTheWalkAlsoAsksForItsCode()
    {
        var result = LineWithGo(code =>
        {
            code.SetByte(5, 0, 0).SetByte(5, 1, 0).IfUb(5, 1, 0, 1, 0, "afterCall").ReqEw(2, 3).Label("afterCall");
            for (var index = 0; index < 24; index++)
            {
                code.Nop();
            }

            code.IfUb(5, 0, 0, 1, 0, "done").MapJump(126).Label("done").Ret();
        }, field =>
        {
            field.Code.Label("otherInit").Ret().Label("otherMain").SetByte(5, 0, 1).JmpB("otherMain");
            field.Code.Label("otherEmpty").Ret();
            field.Entity("writer", (0, "otherInit"), (1, "otherEmpty"), (3, "otherMain"), (4, "otherEmpty"));
        }).Read();
        Sequence([126], ExitDestinations(result), "a Main the walk also names under another slot is still concurrent");
    }

    /// <summary>REQEW of NOP; JMPB: the requested script never finishes, so the caller never goes on.</summary>
    private static void WaitingForAScriptThatNeverFinishesEndsTheCaller()
    {
        var result = LineWithGo(code => code.ReqEw(2, 3).MapJump(127).Ret(), field =>
        {
            field.Code.Label("calleeInit").Ret().Label("calleeMain").Ret();
            field.Code.Label("forever").Nop().JmpB("forever");
            field.Entity("callee", (0, "calleeInit"), (3, "forever"));
        }).Read();
        Sequence([], ExitDestinations(result), "a caller waiting on an endless loop does not reach its MAPJUMP");
    }

    /// <summary>REQEW of a script stopped at 0x1B: 006107E1 never advances it, so it never finishes.</summary>
    private static void WaitingForAnUnimplementedHandlerEndsTheCaller()
    {
        var result = LineWithGo(code => code.ReqEw(2, 3).MapJump(128).Ret(), field =>
        {
            field.Code.Label("calleeInit").Ret().Label("calleeMain").Ret();
            field.Code.Label("stopped").Raw(0x1B, 0, 0).Ret();
            field.Entity("callee", (0, "calleeInit"), (3, "stopped"));
        }).Read();
        Sequence([], ExitDestinations(result), "a caller waiting on a stalled script does not reach its MAPJUMP");
    }

    /// <summary>Scripts 1 to 6 are empty; script 7 would MAPJUMP, but no event starts it and nothing asks for it.</summary>
    private static void AnUnrequestedHighLineScriptIsNotAWalkEvent()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dir").Ret().Ret();
        code.Label("line").Line(0, 0, 0, 100, 0, 0).Ret().Ret();
        code.Label("empty").Ret();
        code.Label("private").MapJump(129).Ret();
        field.Entity("dir", (0, "dir"));
        field.Entity("line", (0, "line"), (1, "empty"), (7, "private"));
        Sequence([], ExitDestinations(field.Read()), "a LINE's script 7 is not a doorway of its own");
    }

    /// <summary>SETWORD 2[255]: the high byte lands in the next savemap block, bank 3 offset 0 (0061031E).</summary>
    private static void AWordAtOffset255EndsInTheNextBlock()
    {
        var result = LineWithGo(code => code
            .SetByte(3, 0, 0)
            .Raw(0x81, 0x20, 0xFF, 0x00, 0x01)
            .IfUb(3, 0, 0, 1, 0, "done")
            .MapJump(125)
            .Label("done")
            .Ret()).Read();
        Sequence([125], ExitDestinations(result), "a word written at offset 255 ends the value in the next block");
    }

    /// <summary>The line's Go asks for its own script 7; asked for, script 7 runs, and its MAPJUMP is the line's.</summary>
    private static void ARequestedHighLineScriptStillRuns()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("lineInit").Line(0, 0, 0, 100, 0, 0).Ret().Label("lineMain").Ret();
        code.Label("ok").Ret();
        code.Label("go").Req(1, 7).Ret();
        code.Label("private").MapJump(138).Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("exit", (0, "lineInit"), (1, "ok"), (4, "go"), (7, "private"));
        Sequence([138], ExitDestinations(field.Read()), "script 7 runs when the line's own event asks for it");
    }

    /// <summary>An endless loop with nothing changing ends the walk instead of running it to its step limit.</summary>
    private static void ALoopThatNeverChangesAnythingEnds()
    {
        var result = LineWithGo(code => code
            .IfKey(0x20, "spin").MapJump(139).Ret()
            .Label("spin").Nop().JmpB("spin")).Read();
        Sequence([139], ExitDestinations(result), "the other side of a test in front of an endless loop is still walked");
        True(result.Diagnostic.Contains("walkLimits=0", StringComparison.Ordinal),
            $"a loop that never changes anything is the engine's own end, not a walk limit: {result.Diagnostic}");
    }

    /// <summary>
    /// MAPJUMP 006131C4 waits at itself until the field changes: the JUMP after it never
    /// happens in this field, and a second MAPJUMP after it is never reached.
    /// </summary>
    private static void NothingAfterAMapJumpHappensInTheField()
    {
        var result = LineWithGo(code => code.MapJump(140).FieldJump(10, 20, 11).MapJump(141).Ret()).Read();
        Sequence([140], ExitDestinations(result), "only the first MAPJUMP is where the line goes");
        True(result.Transitions.Count == 0, "a movement after MAPJUMP is not a landing in this field");
    }

    /// <summary>
    /// A MAPJUMP into the field itself ends the walk as well. The field loop (0063C17F)
    /// answers MAPJUMP's request 1 by loading the destination - its own field included - with
    /// the previous module set to 1, which 0063BDA8 answers with a complete reset, and field
    /// setup (0060BCFA) clears the request and +0x26; only other modules' returns set +0x26
    /// to 2, the value MAPJUMP would need to go on. Shinra HQ's 60th floor places the party
    /// after its MAPJUMP to itself, and that placement never runs.
    /// </summary>
    private static void AJumpIntoTheSameFieldEndsTheWalkToo()
    {
        var result = LineWithGo(
            code => code.MapJump(900).ReqEw(2, 3).Ret(),
            field => PlayerModel(field, code => code.FieldJump(10, 20, 11).Ret())).Read(900);
        Sequence([], result.Transitions.Select(transition => transition.TargetTriangle), "nothing after a same-field MAPJUMP runs");
        Sequence([], ExitDestinations(result), "a line that only jumps into its own field is not an exit");
    }

    /// <summary>
    /// The line asks the leader to run script 3, and Cloud's copy lands on triangle 11 while
    /// Aeris's lands on 22 - Mount Corel's border9 in miniature. Each landing belongs to its
    /// own character; neither is dropped for disagreeing with the other.
    /// </summary>
    private static void EachPartyMembersCopyKeepsItsOwnLanding()
    {
        var result = PartyRoutine(aerisX: 30, aerisY: 40, aerisTriangle: 22).Read();
        Equal(
            "11:2|22:3",
            string.Join("|", result.Transitions
                .Where(transition => transition.Kind == FieldNavigationTransitionKind.Jump)
                .OrderBy(transition => transition.TargetTriangle)
                .Select(transition => $"{transition.TargetTriangle}:{string.Join(",", transition.MoverEntityIds ?? [])}")),
            "each member's copy lands that member, and only that member");
    }

    private static void AgreeingPartyCopiesAreOneTraversalForEveryMember()
    {
        var result = PartyRoutine(aerisX: 10, aerisY: 20, aerisTriangle: 11).Read();
        Equal(
            "11:2,3",
            string.Join("|", result.Transitions
                .Where(transition => transition.Kind == FieldNavigationTransitionKind.Jump)
                .Select(transition => $"{transition.TargetTriangle}:{string.Join(",", transition.MoverEntityIds ?? [])}")),
            "copies that agree are one traversal for all of their characters");
    }

    /// <summary>
    /// The line asks the field's first model and a second model to jump. Only the first can
    /// ever be the controlled one, so only its jump is the player's, and it is marked as that
    /// model's. The LINE's own JUMP moves nothing at all: a LINE has no model.
    /// </summary>
    private static void OnlyAModelThatCanBeControlledMovesThePlayer()
    {
        var result = LineWithGo(code => code.FieldJump(50, 60, 33).ReqEw(2, 3).ReqEw(3, 3).Ret(), field =>
        {
            PlayerModel(field, code => code.FieldJump(10, 20, 22).Ret());
            field.Code.Label("soldierInit").Char(1).Ret().Label("soldierMain").Ret().Label("soldierIdle").Ret()
                .Label("soldier3").FieldJump(30, 40, 11).Ret();
            field.Entity("soldier", (0, "soldierInit"), (1, "soldierIdle"), (3, "soldier3"));
        }).Read();
        Equal(
            "22:2",
            string.Join("|", result.Transitions.Select(transition =>
                $"{transition.TargetTriangle}:{string.Join(",", transition.MoverEntityIds ?? [])}")),
            "only the model field setup, PC or CC can make the controlled one moves the player");
    }

    /// <summary>A model CC (006142D5) hands control to can be the player's, so its movement is kept.</summary>
    private static void ACcTargetCanBeThePlayer()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Raw(0xBF, 3).Ret();
        code.Label("lineInit").Line(0, 0, 0, 100, 0, 0).Ret().Label("lineMain").Ret();
        code.Label("ok").Ret();
        code.Label("go").ReqEw(3, 3).Ret();
        code.Label("heroInit").Char(0).Ret().Label("heroMain").Ret().Label("heroIdle").Ret();
        code.Label("chocoInit").Char(1).Ret().Label("chocoMain").Ret().Label("chocoIdle").Ret()
            .Label("choco3").FieldJump(30, 40, 11).Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("exit", (0, "lineInit"), (1, "ok"), (4, "go"));
        field.Entity("hero", (0, "heroInit"), (1, "heroIdle"));
        field.Entity("choco", (0, "chocoInit"), (1, "chocoIdle"), (3, "choco3"));
        var result = field.Read();
        Equal(
            "11:3",
            string.Join("|", result.Transitions.Select(transition =>
                $"{transition.TargetTriangle}:{string.Join(",", transition.MoverEntityIds ?? [])}")),
            "the model CC names is one the player can move");
    }

    /// <summary>The line asks party slot 0 for script 3; Cloud's copy jumps to (10, 20) on triangle 11.</summary>
    private static SyntheticFieldScript PartyRoutine(int aerisX, int aerisY, int aerisTriangle) =>
        LineWithGo(code => code.Preq(0, 3).Ret(), field =>
        {
            field.Code.Label("cloudInit").Char(1).Pc(0).Ret().Label("cloudMain").Ret().Label("cloudIdle").Ret()
                .Label("cloud3").FieldJump(10, 20, 11).Ret();
            field.Code.Label("aerisInit").Char(2).Pc(3).Ret().Label("aerisMain").Ret().Label("aerisIdle").Ret()
                .Label("aeris3").FieldJump(aerisX, aerisY, aerisTriangle).Ret();
            field.Entity("cloud", (0, "cloudInit"), (1, "cloudIdle"), (3, "cloud3"));
            field.Entity("aeris", (0, "aerisInit"), (1, "aerisIdle"), (3, "aeris3"));
        });

    private static void TheTrackerOffersAMembersTraversalOnlyWhileItsModelIsControlled()
    {
        var anybody = new FieldScriptNavigationTransition(
            900, FieldNavigationTransitionKind.Jump, 1, 0, 0, 0, 10, 20, null, 11, "jump:anybody");
        var aeris = anybody with { StableId = "jump:aeris", TargetTriangle = 22, MoverEntityIds = [3] };
        var both = anybody with { StableId = "jump:both", MoverEntityIds = [2, 3] };
        var tracker = new FieldScriptNavigationTransitionTracker(TimeSpan.Zero, () => DateTime.UtcNow);
        string Offered(Func<int, bool?>? isControlled) =>
            string.Join(",", tracker.Resolve(900, [anybody, aeris, both], _ => true, isControlled)
                .Select(transition => transition.StableId));

        Equal("jump:anybody,jump:both", Offered(entity => entity == 2), "Cloud controlled: Aeris's own landing is not offered");
        Equal("jump:anybody,jump:aeris,jump:both", Offered(entity => entity == 3), "Aeris controlled: her landing is offered");
        Equal("jump:anybody", Offered(_ => false), "CC to another model: no character's own traversal is the player's");
        Equal("jump:anybody", Offered(_ => null),
            "an unreadable or torn answer is not a verified route: only traversals that move whoever is controlled stay");
        Equal("jump:anybody,jump:both", Offered(entity => entity == 2 ? true : null),
            "one mover positively controlled is enough, whatever the others read");
        Equal("jump:anybody,jump:aeris,jump:both", Offered(null), "an offline listing without a reader filters nothing");
    }

    private static void TheControlledEntityIsTheOneWhoseModelIsControlled()
    {
        var memory = new ControlMemory();
        memory.EntityModels[2] = 4;
        memory.EntityModels[3] = 5;
        memory.ControlledModel = 4;
        var reader = new FieldControlledEntityReader(memory);
        Equal("True,False,False", $"{reader.IsControlled(2)},{reader.IsControlled(3)},{reader.IsControlled(7)}",
            "the entity whose model is at context +0x2A is the controlled one; one without a model never is");

        // CC 006142D5 hands control to another entity's model without touching the party.
        memory.ControlledModel = 5;
        Equal("False,True", $"{reader.IsControlled(2)},{reader.IsControlled(3)}", "after CC the other entity is controlled");
    }

    private static void ATornOrUnreadableControlReadIsNoAnswer()
    {
        var memory = new ControlMemory();
        memory.EntityModels[2] = 4;
        memory.ControlledModel = 4;
        var reader = new FieldControlledEntityReader(memory);
        memory.Unreadable.Add(ControlMemory.Context + FieldControlledEntityReader.ControlledModelOffset);
        True(reader.IsControlled(2) is null, "an unreadable controlled model is no answer");
        memory.Unreadable.Clear();

        memory.FlipControlledModelAfterReads = 1;
        True(reader.IsControlled(2) is null, "control changing between the two captures is no answer");
        memory.FlipControlledModelAfterReads = 0;

        memory.Module = 2;
        True(reader.IsControlled(2) is null, "outside the field module there is no answer");
    }

    /// <summary>The module, field, script context and per-entity model table the reader consults.</summary>
    private sealed class ControlMemory : ILegacyAddressSpace
    {
        public const uint Context = 0x00CC0D88;

        private int controlledModelReads;

        public byte Module { get; set; } = FieldPositionReader.FieldModule;

        public ushort ControlledModel { get; set; }

        public byte PartyLeader { get; set; }

        public byte[] EntityModels { get; } = Enumerable.Repeat((byte)0xFF, 256).ToArray();

        public HashSet<uint> Unreadable { get; } = [];

        // After this many reads of the controlled model it changes; 0 never.
        public int FlipControlledModelAfterReads { get; set; }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (Unreadable.Contains(virtualAddress))
            {
                return false;
            }

            destination.Clear();
            switch (virtualAddress)
            {
                case (uint)FieldPositionReader.AddressCurrentModule:
                    destination[0] = Module;
                    return true;
                case (uint)FieldPositionReader.AddressFieldId:
                    destination[0] = 0x84;
                    return true;
                case (uint)FieldControlledEntityReader.AddressScriptContextPointer:
                    BitConverter.TryWriteBytes(destination, Context);
                    return true;
                case (uint)FieldControlledEntityReader.AddressPartySlots:
                    destination[0] = PartyLeader;
                    return true;
                case Context + FieldControlledEntityReader.ControlledModelOffset:
                    controlledModelReads++;
                    var model = FlipControlledModelAfterReads != 0 && controlledModelReads > FlipControlledModelAfterReads
                        ? (ushort)(ControlledModel + 1)
                        : ControlledModel;
                    BitConverter.TryWriteBytes(destination, model);
                    return true;
            }

            var entity = virtualAddress - (uint)FieldControlledEntityReader.AddressEntityModelIndices;
            if (entity < EntityModels.Length)
            {
                destination[0] = EntityModels[entity];
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// PRTYE (0061C113 through 0061C26A), PRTYM (0061C045), PRTYP (0061BE95) and MMBud with
    /// enable 0 (0061C812) write the party at 00DC09E5, field bank 3[9..11], directly rather
    /// than through the byte and word writers. Root's three probes, and MMBud.
    /// </summary>
    private static void PartyOperationsEndAFoldedPartyByte()
    {
        var cases = new (string Name, Action<SyntheticScriptCode> Go, int Destination)[]
        {
            ("PRTYE", code => code.SetByte(3, 9, 0).PartyIs(3, 0xFE, 0xFE)
                .IfUb(3, 9, 0, 3, 0, "done").MapJump(130).Label("done").Ret(), 130),
            ("PRTYM", code => code.SetByte(3, 9, 0).PartyRemove(0)
                .IfUb(3, 9, 0, 255, 0, "done").MapJump(131).Label("done").Ret(), 131),
            ("PRTYP", code => code.SetByte(3, 9, 255).SetByte(3, 10, 255).SetByte(3, 11, 255).PartyAdd(3)
                .IfUb(3, 9, 0, 3, 0, "done").MapJump(132).Label("done").Ret(), 132),
            ("MMBud", code => code.SetByte(3, 10, 4).Raw(0xCD, 0, 4)
                .IfUb(3, 10, 0, 255, 0, "done").MapJump(133).Label("done").Ret(), 133)
        };
        foreach (var (name, go, destination) in cases)
        {
            Sequence([destination], ExitDestinations(LineWithGo(go).Read()), $"{name} changes the party byte the test reads");
        }
    }

    /// <summary>
    /// The other direct writers in root's handler scan: DSKCG 13[0], SMTRA 13[31] and 1[75],
    /// the HP and MP opcodes 1[122] (005CB127), MPNAM 13[104..128], SPECIAL F9 1[75]; and the
    /// module hand-offs whose writes no field opcode names, SPECIAL FE (a new game) and MINIGAME.
    /// </summary>
    private static void DirectNativeWritersEndFoldedValues()
    {
        var cases = new (string Name, int Bank, int Address, int[] Writer)[]
        {
            ("DSKCG", 13, 0, [0x0E, 1]),
            ("SMTRA disc", 13, 31, [0x5B, 0, 0, 0, 0, 0, 0]),
            ("SMTRA materia", 1, 75, [0x5B, 0, 0, 0, 0, 0, 0]),
            ("HPu", 1, 122, [0x4D, 0, 0, 0, 0]),
            ("MPNAM", 13, 128, [0x43, 0]),
            ("SPECIAL F9", 1, 75, [0x0F, 0xF9]),
            ("SPECIAL FE", 11, 40, [0x0F, 0xFE]),
            ("MINIGAME", 3, 200, [0x20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])
        };
        foreach (var (name, bank, address, writer) in cases)
        {
            var result = LineWithGo(code => code.SetByte(bank, address, 7).Raw(writer)
                .IfUb(bank, address, 0, 7, 1, "done").MapJump(135).Label("done").Ret()).Read();
            Sequence([135], ExitDestinations(result), $"after {name} the byte is no longer known to be what was set");
        }

        var untouched = LineWithGo(code => code.SetByte(13, 30, 7).Raw(0x5B, 0, 0, 0, 0, 0, 0)
            .IfUb(13, 30, 0, 7, 1, "done").MapJump(135).Label("done").Ret()).Read();
        Sequence([], ExitDestinations(untouched), "a byte next to SMTRA's is still known: the writes are exact");
    }

    /// <summary>A Main that changes the party can do it between any two of the walk's opcodes.</summary>
    private static void APartyChangeInAMainIsAConcurrentWrite()
    {
        var result = LineWithGo(
            code => code.SetByte(3, 9, 0).Nop().Nop().IfUb(3, 9, 0, 3, 0, "done").MapJump(134).Label("done").Ret(),
            field =>
            {
                field.Code.Label("partyInit").Ret().Label("partyMain").PartyIs(3, 0xFF, 0xFF).JmpB("partyMain")
                    .Label("partyIdle").Ret();
                field.Entity("party", (0, "partyInit"), (1, "partyIdle"));
            }).Read();
        Sequence([134], ExitDestinations(result), "3[9] is volatile while a Main can set the party");
    }

    /// <summary>
    /// JUMP moves only the running entity's model (00615CA3). An NPC that is the field's first
    /// model can be the controlled one, so its jump is tagged with it; the tracker offers it
    /// only on a positive live answer, never on an unreadable one.
    /// </summary>
    private static void AModelsJumpIsOfferedOnlyOnAPositiveControlAnswer()
    {
        var result = LineWithGo(code => code.ReqEw(2, 3).Ret(),
            field => PlayerModel(field, code => code.FieldJump(10, 20, 11, 1).Ret())).Read();
        var tracker = new FieldScriptNavigationTransitionTracker(TimeSpan.Zero, () => DateTime.UtcNow);
        int Offered(Func<int, bool?> isControlled) => tracker.Resolve(900, result.Transitions, _ => true, isControlled).Count;
        Equal("2", string.Join(",", result.Transitions.Single().MoverEntityIds ?? []), "the jump moves entity 2's model");
        Equal("1,0,0", $"{Offered(entity => entity == 2)},{Offered(_ => false)},{Offered(_ => null)}",
            "offered while entity 2 is controlled; not while another model is, nor on an unreadable read");
    }

    /// <summary>
    /// CC (006142D5) inside the routine hands control to entity 3 before it jumps, so the jump is
    /// the player's whoever was controlled when the line was crossed - and without the CC an
    /// ordinary second model's jump is nobody's the player can be.
    /// </summary>
    private static void CcInsideTheRoutineMakesTheNamedModelsJumpThePlayers()
    {
        void Npc(SyntheticFieldScript field)
        {
            PlayerModel(field, code => code.Ret());
            field.Code.Label("npcInit").Char(1).Ret().Label("npcMain").Ret().Label("npcIdle").Ret()
                .Label("npc3").FieldJump(30, 40, 22, 1).Ret();
            field.Entity("npc", (0, "npcInit"), (1, "npcIdle"), (3, "npc3"));
        }

        var switched = LineWithGo(code => code.Cc(3).ReqEw(3, 3).Ret(), Npc).Read();
        var tracker = new FieldScriptNavigationTransitionTracker(TimeSpan.Zero, () => DateTime.UtcNow);
        Equal("22:", string.Join("|", switched.Transitions.Select(transition =>
                $"{transition.TargetTriangle}:{string.Join(",", transition.MoverEntityIds ?? [])}")),
            "after CC the named model's jump is the player's, not tied to who was controlled before");
        Equal("1", $"{tracker.Resolve(900, switched.Transitions, _ => true, _ => false).Count}",
            "offered even though entity 3 was not the controlled model when the line was crossed");

        var unswitched = LineWithGo(code => code.ReqEw(3, 3).Ret(), Npc).Read();
        Equal("", string.Join("|", unswitched.Transitions.Select(transition => transition.StableId)),
            "without CC an ordinary second model's jump moves the NPC, not the player");

        var elsewhere = LineWithGo(code => code.Cc(2).ReqEw(3, 3).Ret(), Npc).Read();
        Equal("", string.Join("|", elsewhere.Transitions.Select(transition => transition.StableId)),
            "after CC to another model, entity 3's jump is not the player's either");
    }

    /// <summary>
    /// The Init executor (0060C683) runs Inits in entity order and CHAR numbers models as they
    /// load, so a CHAR its own Init provably skips does not take model 0 (root's probe); a CHAR
    /// behind a test Init cannot decide leaves both entities able to hold model 0.
    /// </summary>
    private static void AnInitThatSkipsItsCharLeavesModelZeroToTheNext()
    {
        SyntheticFieldScript Field(Action<SyntheticScriptCode> optionalInit) =>
            LineWithGo(code => code.ReqEw(3, 3).Ret(), field =>
            {
                field.Code.Label("optionalInit");
                optionalInit(field.Code);
                field.Code.Label("optionalMain").Ret().Label("optionalIdle").Ret();
                field.Code.Label("actualInit").Char(1).Ret().Label("actualMain").Ret().Label("actualIdle").Ret()
                    .Label("actual3").FieldJump(10, 20, 11, 1).Ret();
                field.Entity("optional", (0, "optionalInit"), (1, "optionalIdle"));
                field.Entity("actual", (0, "actualInit"), (1, "actualIdle"), (3, "actual3"));
            });

        var skipped = Field(code => code.SetByte(5, 0, 0).IfUb(5, 0, 0, 1, 0, "optionalDone").Char(0)
            .Label("optionalDone").Ret()).Read();
        Equal("3", string.Join(",", skipped.Transitions.SingleOrDefault().MoverEntityIds ?? []),
            "the skipped CHAR leaves model 0 to entity 3, which is then the player's while controlled");

        var undecided = Field(code => code.IfUb(1, 40, 0, 1, 0, "optionalDone").Char(0)
            .Label("optionalDone").Ret()).Read();
        Equal("3", string.Join(",", undecided.Transitions.SingleOrDefault().MoverEntityIds ?? []),
            "a CHAR behind a savemap test may or may not run, so entity 3 can still hold model 0");

        var always = Field(code => code.Char(0).Ret()).Read();
        Equal(0, always.Transitions.Count, "a CHAR that always runs takes model 0, and entity 3 is an ordinary NPC");
    }

    /// <summary>
    /// PRTYE ends in 0061BC67, which hands control to the model of whoever it put in slot 0,
    /// so a party request after it moves the player whoever was controlled before - and only
    /// the new leader's copy answers it.
    /// </summary>
    private static void APartyChangeHandsControlToTheNewLeader()
    {
        var result = LineWithGo(code => code.PartyIs(2, 0xFE, 0xFE).PrqEw(0, 3).Ret(), field =>
        {
            field.Code.Label("cloudInit").Char(0).Pc(0).Ret().Label("cloudMain").Ret().Label("cloudIdle").Ret()
                .Label("cloud3").FieldJump(10, 20, 11, 1).Ret();
            field.Code.Label("tifaInit").Char(1).Pc(2).Ret().Label("tifaMain").Ret().Label("tifaIdle").Ret()
                .Label("tifa3").FieldJump(30, 40, 22, 1).Ret();
            field.Entity("cloud", (0, "cloudInit"), (1, "cloudIdle"), (3, "cloud3"));
            field.Entity("tifa", (0, "tifaInit"), (1, "tifaIdle"), (3, "tifa3"));
        }).Read();
        Equal("22:", string.Join("|", result.Transitions.Select(transition =>
                $"{transition.TargetTriangle}:{string.Join(",", transition.MoverEntityIds ?? [])}")),
            "Tifa leads after PRTYE, so her copy runs and her jump is the player's");
    }

    /// <summary>
    /// 0060D29B runs a Talk slot that shares Init's pointer; for an entity with a model that is
    /// a real conversation (root's probe). A director with no model cannot be talked to.
    /// </summary>
    private static void ATalkThatSharesInitsPointerIsTalkForAModelOnly()
    {
        var modeled = new SyntheticFieldScript();
        modeled.Code.Label("sharedInitTalk").Char(0).Message(0, 1).Ret().Label("main").Message(0, 2).Ret();
        modeled.Entity("npc", (0, "sharedInitTalk"), (1, "sharedInitTalk"));
        var npc = modeled.Read().Npcs.SingleOrDefault(definition => definition.EntityId == 0);
        Equal("1", string.Join(",", npc.DialogIds ?? []), "the modeled entity keeps its Talk, which runs Init's code and not Main's");

        var director = new SyntheticFieldScript();
        director.Code.Label("sceneInit").Message(0, 1).Ret().Label("sceneMain").Ret();
        director.Entity("dir", (0, "sceneInit"), (1, "sceneInit"));
        Equal(0, director.Read().Npcs.Count, "a director with no model is nobody to talk to");
    }

    /// <summary>
    /// Root's polling probes: the same leader poll as an unrequested private slot 7 and as the
    /// actual Main. Only the Main runs, and its ladder moves whichever family member leads.
    /// </summary>
    private static void OnlyARunningPollIsALadderAndItMovesOnlyTheFamily()
    {
        SyntheticFieldScript Polling(bool active)
        {
            var field = new SyntheticFieldScript();
            field.Code.Label("dirInit").Ret();
            if (!active)
            {
                field.Code.Label("dirMain").Ret();
            }

            field.Code.Label("poll").Raw(0x75, 0x66, 0x66, 0, 0, 2, 4, 6)
                .Raw(0x16, 0x60, 6, 0, 7, 0, 0, 8).IfKey(544, "pollDone").PrqEw(0, 3)
                .Label("pollDone").JmpB("poll").Label("dirEmpty").Ret();
            field.Code.Label("cloudInit").Char(0).Pc(0).Ret().Ret().Label("cloudEmpty").Ret()
                .Label("cloudClimb").Ladder(10, 20, 30, 11, 1).Ret();
            field.Code.Label("tifaInit").Char(1).Pc(2).Ret().Ret().Label("tifaEmpty").Ret()
                .Label("tifaClimb").Ladder(10, 20, 30, 11, 1).Ret();
            field.Entity("director", (0, "dirInit"), (1, "dirEmpty"), (7, "poll"));
            field.Entity("cloud", (0, "cloudInit"), (1, "cloudEmpty"), (3, "cloudClimb"));
            field.Entity("tifa", (0, "tifaInit"), (1, "tifaEmpty"), (3, "tifaClimb"));
            return field;
        }

        static IReadOnlyList<FieldScriptNavigationTransition> Polled(FieldScriptNavigationReadResult result) =>
            result.Transitions.Where(transition => transition.StableId.StartsWith("polled-ladder:", StringComparison.Ordinal)).ToArray();

        Equal(0, Polled(Polling(active: false).Read()).Count, "a poll in a slot nothing starts never runs");
        var active = Polled(Polling(active: true).Read());
        Equal("1,2", string.Join(",", active.SingleOrDefault().MoverEntityIds ?? []), "the Main's poll climbs whichever family member leads");
        Equal("1:0;2:2", Ways(active.Single()), "each member climbs as slot 0's actor: its model with its own character leading");
        var tracker = new FieldScriptNavigationTransitionTracker(TimeSpan.Zero, () => DateTime.UtcNow);
        Equal("0,1,0", $"{tracker.Resolve(900, active, _ => true, _ => false).Count}," +
                       $"{tracker.Resolve(900, active, _ => true, entity => entity == 2).Count}," +
                       $"{tracker.Resolve(900, active, _ => true, _ => null).Count}",
            "offered while a family member is controlled, and not otherwise");
        // Root's pairing probe: PRQEW asks slot 0, so with Tifa leading it is Tifa who climbs,
        // whoever CC has left controlled. Controlled Cloud does not stand in for leading Tifa.
        Equal("0,1,1", $"{tracker.Resolve(900, active, _ => true, entity => entity == 1, () => 2).Count}," +
                       $"{tracker.Resolve(900, active, _ => true, entity => entity == 2, () => 2).Count}," +
                       $"{tracker.Resolve(900, active, _ => true, entity => entity == 1, () => 0).Count}",
            "the climb is offered only while the member who leads is also the controlled one");
    }

    /// <summary>
    /// A mover mask names entities 0 to 63. A model past that cannot be told apart from anybody,
    /// so its movement is not offered as everybody's; the field's diagnostic counts it.
    /// </summary>
    private static void AMoverPastTheMaskIsOfferedToNobody()
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("lineInit").Line(0, 0, 0, 100, 0, 0).Ret().Label("lineMain").Ret();
        code.Label("ok").Ret();
        code.Label("go").ReqEw(65, 3).Ret();
        code.Label("idle").Ret();
        code.Label("farInit").Char(0).Ret().Label("farMain").Ret().Label("far3").FieldJump(10, 20, 11, 1).Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("exit", (0, "lineInit"), (1, "ok"), (4, "go"));
        for (var filler = 2; filler < 65; filler++)
        {
            field.Entity($"e{filler}", (0, "idle"));
        }

        field.Entity("far", (0, "farInit"), (1, "idle"), (3, "far3"));
        var result = field.Read();
        Equal(0, result.Transitions.Count, "entity 65's jump is not offered as anybody's");
        True(result.Diagnostic.Contains("unsupportedMovers=1", StringComparison.Ordinal),
            $"and the field says so: {result.Diagnostic}");
    }

    private static string Ways(FieldScriptNavigationTransition transition) =>
        string.Join(";", transition.Conditions?.Select(condition => condition.Key) ?? []);

    /// <summary>
    /// PREQ to slot 0 runs the copy of whoever is in slot 0 (0x00DC09E5), even after CC has made
    /// another member controlled: then it moves slot 0's model, not the player's. Each member's
    /// traversal pairs its model with its own leader (Conditions), and the tracker offers it only
    /// while that member both leads and is controlled - root's design point about CC and slot-0
    /// requests.
    /// </summary>
    private static void APartySlotRequestNeedsItsMemberInSlotZero()
    {
        var result = PartyRoutine(aerisX: 30, aerisY: 40, aerisTriangle: 22).Read();
        Equal("11:2:0|22:3:3", string.Join("|", result.Transitions
                .OrderBy(transition => transition.TargetTriangle)
                .Select(transition => $"{transition.TargetTriangle}:{Ways(transition)}")),
            "Cloud's copy needs Cloud's model (2) controlled with Cloud (0) leading, Aeris's needs hers (3) with Aeris (3)");
        var tracker = new FieldScriptNavigationTransitionTracker(TimeSpan.Zero, () => DateTime.UtcNow);
        string Offered(Func<int, bool?> isControlled, Func<int?>? leader) =>
            string.Join(",", tracker.Resolve(900, result.Transitions, _ => true, isControlled, leader)
                .OrderBy(transition => transition.TargetTriangle)
                .Select(transition => transition.TargetTriangle));
        Equal("11", Offered(entity => entity == 2, () => 0), "Cloud leads and is controlled: his copy runs");
        Equal("", Offered(entity => entity == 3, () => 0),
            "CC made Aeris controlled while Cloud is in slot 0: the request runs Cloud's copy, which moves Cloud");
        Equal("22", Offered(entity => entity == 3, () => 3), "Aeris leads and is controlled: her copy runs");
        Equal("", Offered(entity => entity == 2, () => null), "an unreadable slot 0 is no verified route");
        Equal("11,22", Offered(_ => true, null), "an offline listing without a party reader filters nothing on it");
    }

    /// <summary>
    /// cos_btm's sign in miniature: the line hands control to a view model with CC, moves it, and
    /// hands control back with one CC per leader it tests 3[9] for. Every leader gets control back,
    /// so the view's jump is nobody's landing. Only a leader the tests do not cover leaves control
    /// with the view, and that way needs such a leader.
    /// </summary>
    private static void ALeaderTestSendsEachLeaderItsOwnWay()
    {
        SyntheticFieldScript Sign(bool thirdMember) =>
            LineWithGo(code => code.Cc(4).ReqEw(4, 3)
                .IfUb(3, 9, 0, 0, 0, "notCloud").Cc(2).JmpF("done")
                .Label("notCloud").IfUb(3, 9, 0, 2, 0, "done").Cc(3)
                .Label("done").Ret(), field =>
            {
                field.Code.Label("cloudInit").Char(0).Pc(0).Ret().Label("cloudMain").Ret().Label("cloudIdle").Ret();
                field.Code.Label("tifaInit").Char(1).Pc(2).Ret().Label("tifaMain").Ret().Label("tifaIdle").Ret();
                field.Code.Label("viewInit").Char(2).Ret().Label("viewMain").Ret().Label("viewIdle").Ret()
                    .Label("view3").FieldJump(10, 20, 88, 1).Ret();
                field.Entity("cloud", (0, "cloudInit"), (1, "cloudIdle"));
                field.Entity("tifa", (0, "tifaInit"), (1, "tifaIdle"));
                field.Entity("view", (0, "viewInit"), (1, "viewIdle"), (3, "view3"));
                if (thirdMember)
                {
                    field.Code.Label("barretInit").Char(3).Pc(1).Ret().Label("barretMain").Ret().Label("barretIdle").Ret();
                    field.Entity("barret", (0, "barretInit"), (1, "barretIdle"));
                }
            });

        Equal(0, Sign(thirdMember: false).Read().Transitions.Count,
            "Cloud and Tifa are the only members, and each gets control back: the view's jump is nobody's landing");
        var third = Sign(thirdMember: true).Read().Transitions;
        Equal("88:*:1", string.Join("|", third.Select(transition => $"{transition.TargetTriangle}:{Ways(transition)}")),
            "with Barret leading nothing hands control back, and the view's jump is the player's whoever was controlled");
        var tracker = new FieldScriptNavigationTransitionTracker(TimeSpan.Zero, () => DateTime.UtcNow);
        Equal("0,1", $"{tracker.Resolve(900, third, _ => true, _ => false, () => 0).Count}," +
                     $"{tracker.Resolve(900, third, _ => true, _ => false, () => 1).Count}",
            "not offered while Cloud leads; offered while Barret does");
    }

    private static void ThePartyLeaderIsReadBetweenTheSameBookends()
    {
        var memory = new ControlMemory { ControlledModel = 4, PartyLeader = 2 };
        var reader = new FieldControlledEntityReader(memory);
        Equal("2", $"{reader.ReadPartyLeaderCharacter()}", "slot 0 is read from 0x00DC09E5");
        memory.Unreadable.Add((uint)FieldControlledEntityReader.AddressPartySlots);
        True(reader.ReadPartyLeaderCharacter() is null, "an unreadable slot 0 is no answer");
        memory.Unreadable.Clear();
        memory.Module = 2;
        True(reader.ReadPartyLeaderCharacter() is null, "outside the field module there is no answer");

        // A fresh field whose controlled model changes after its first read: the two captures disagree.
        var torn = new ControlMemory { ControlledModel = 4, PartyLeader = 2, FlipControlledModelAfterReads = 1 };
        True(new FieldControlledEntityReader(torn).ReadPartyLeaderCharacter() is null,
            "control changing between the captures is no answer");
    }

    private static string Guards(FieldScriptNavigationReadResult result) =>
        string.Join("|", result.ExitGuards.OrderBy(pair => pair.Key, StringComparer.Ordinal).SelectMany(pair => pair.Value.Select(guard =>
            $"{guard.DestinationFieldId}=" + string.Join(" or ", guard.Alternatives.Select(alternative =>
                string.Join("&", alternative.Select(test => test.Key)))))));

    /// <summary>
    /// fship_4's jump line in miniature: the line MAPJUMPs only when a savemap bit is set and
    /// otherwise does nothing. The exit is published with that bit as its live guard; a line
    /// that goes one way or the other guards each destination with its own side.
    /// </summary>
    private static void AnExitWhoseMapJumpAFlagDecidesIsGuardedByIt()
    {
        var flagged = LineWithGo(code => code.IfUb(13, 91, 0, 7, 9, "done").MapJump(140).Label("done").Ret()).Read();
        Sequence([140], ExitDestinations(flagged), "the exit is published");
        Equal("140=13:91:b:9:7:True", Guards(flagged), "its MAPJUMP runs only while 13[91] bit 7 is set");

        var eitherWay = LineWithGo(code => code.IfUb(1, 40, 0, 3, 0, "other").MapJump(141).Label("other").MapJump(142).Ret()).Read();
        Equal("141=1:40:b:0:3:True|142=1:40:b:0:3:False", Guards(eitherWay).Replace("|", "|"),
            "each destination is guarded by its own side of the test");

        var always = LineWithGo(code => code.MapJump(143).Ret()).Read();
        Equal("", Guards(always), "an exit no test decides has no guard");
    }

    /// <summary>
    /// A test is a guard only on the value the byte had when the line was crossed: not one the
    /// routine writes first (here INC, which the walk cannot fold), and not one a Main can change
    /// while the routine runs. A Main cannot change it before the line's own event has run its
    /// first tests, though: 0060C94D runs an event it has just started in the same pass, up to
    /// eight opcodes and until a handler waits, and the byte, word and party tests and forward
    /// jumps never wait. So a Main's writes count against a test behind a WAIT, or in a script
    /// the event asks another entity to run, and not against the event's opening test.
    /// </summary>
    private static void AGuardIsOnlyAValueTheLineStartsWith()
    {
        var written = LineWithGo(code => code.Inc(1, 40).IfUb(1, 40, 0, 3, 0, "done").MapJump(140).Label("done").Ret()).Read();
        Sequence([140], ExitDestinations(written), "the exit is still published");
        Equal("", Guards(written), "a byte the routine changes before the test is no live guard");

        static void Counter(SyntheticFieldScript field)
        {
            field.Code.Label("counterInit").Ret().Label("counterMain").Inc(1, 41).JmpB("counterMain").Label("counterIdle").Ret();
            field.Entity("counter", (0, "counterInit"), (1, "counterIdle"));
        }

        var first = LineWithGo(code => code.IfUb(1, 41, 0, 3, 0, "done").MapJump(140).Label("done").Ret(), Counter).Read();
        Equal("140=1:41:b:0:3:True", Guards(first), "the event's opening test runs before any Main can write the byte");

        var behindAWait = LineWithGo(code => code.Wait(1).IfUb(1, 41, 0, 3, 0, "done").MapJump(140).Label("done").Ret(), Counter).Read();
        Sequence([140], ExitDestinations(behindAWait), "the exit behind the wait is still published");
        Equal("", Guards(behindAWait), "a byte a Main keeps writing is no live guard once the event has waited");

        var requested = LineWithGo(code => code.ReqEw(3, 3).Ret(), field =>
        {
            Counter(field);
            field.Code.Label("helperInit").Ret().Label("helperMain").Ret()
                .Label("helper3").IfUb(1, 41, 0, 3, 0, "helperDone").MapJump(140).Label("helperDone").Ret();
            field.Entity("helper", (0, "helperInit"), (1, "helperMain"), (3, "helper3"));
        }).Read();
        Sequence([140], ExitDestinations(requested), "the exit through the requested script is published");
        Equal("", Guards(requested), "a script the event asks for does not start in the dispatcher's pass");
    }

    /// <summary>The runtime reads each guard's byte from the savemap field banks (0x00DC08DC on).</summary>
    private static void TheGuardsAreReadLive()
    {
        var result = LineWithGo(code => code.IfUb(13, 91, 0, 7, 9, "done").MapJump(140).Label("done").Ret()).Read();
        var exits = result.Exits;
        var memory = new Dictionary<uint, byte>();
        var space = new GuardMemory(memory);
        var flagAddress = (uint)(FieldScriptExitGuards.AddressSavemapFieldBanks + 0x300 + 91);
        memory[flagAddress] = 0x80;
        Equal("140", string.Join(",", FieldScriptExitGuards.Apply(exits, result.ExitGuards, space).SelectMany(exit => exit.DestinationFieldIds ?? [])),
            "offered while 13[91] bit 7 is set");
        memory[flagAddress] = 0x7F;
        Equal(0, FieldScriptExitGuards.Apply(exits, result.ExitGuards, space).Count, "not offered while it is clear: crossing the line does nothing");
        memory.Remove(flagAddress);
        Equal(1, FieldScriptExitGuards.Apply(exits, result.ExitGuards, space).Count, "an unreadable byte decides nothing: the exit stays offered");

        var eitherWay = LineWithGo(code => code.IfUb(1, 40, 0, 3, 0, "other").MapJump(141).Label("other").MapJump(142).Ret()).Read();
        memory[(uint)(FieldScriptExitGuards.AddressSavemapFieldBanks + 40)] = 3;
        Equal("141", string.Join(",", FieldScriptExitGuards.Apply(eitherWay.Exits, eitherWay.ExitGuards, space).SelectMany(exit => exit.DestinationFieldIds ?? [])),
            "only the destination the live value chooses is offered");
    }

    /// <summary>
    /// tunnel_4's ladder line in miniature: the line's own event tests a savemap bit and only then
    /// REQs another entity's script, which MAPJUMPs. The request needs the event's test, so the
    /// exit is guarded by it. When that script can also be asked for by a script the event
    /// requests, the crossing reaches it another way and the event's test is not claimed.
    /// </summary>
    private static void ALineThatAsksAnotherScriptToJumpIsGuardedByItsOwnTests()
    {
        static void Helper(SyntheticFieldScript field)
        {
            field.Code.Label("helperInit").Ret().Label("helperMain").Ret().Label("helper3").MapJump(140).Ret()
                .Label("helper4").ReqEw(2, 3).Ret();
            field.Entity("helper", (0, "helperInit"), (1, "helperMain"), (3, "helper3"), (4, "helper4"));
        }

        var asked = LineWithGo(code => code.IfUb(15, 131, 0, 6, 9, "done").Req(2, 3).Label("done").Ret(), Helper).Read();
        Sequence([140], ExitDestinations(asked), "the exit through the requested script is published");
        Equal("140=15:131:b:9:6:True", Guards(asked), "the event's test on the way to the request guards it");

        var another = LineWithGo(code => code.IfUb(15, 131, 0, 6, 9, "other").Req(2, 3).Ret().Label("other").ReqEw(2, 4).Ret(), Helper).Read();
        Sequence([140], ExitDestinations(another), "both ways publish the exit");
        Equal("", Guards(another), "a way through a script the event asks for leaves the event's test unclaimed");
    }

    /// <summary>
    /// Root's review of the comparators: IFSW (00611BAE) compares signed words and IFUW
    /// (00611F40) unsigned ones, and a byte bank is read as a zero-extended byte by both
    /// (0060FD6C). So IFSW 1[40] &gt; -1 holds for every byte and IFUW 1[40] &gt; 65535 for none,
    /// while on a word bank 0x8000 is below zero. The catalog and the evaluator are read together.
    /// </summary>
    private static void AGuardComparesTheWayTheNativeComparatorsDo()
    {
        var memory = new Dictionary<uint, byte>();
        var space = new GuardMemory(memory);
        var address = (uint)(FieldScriptExitGuards.AddressSavemapFieldBanks + 40);

        var signedByte = LineWithGo(code => code.IfWord(true, 1, 40, 0xFFFF, 2, "done").MapJump(140).Label("done").Ret()).Read();
        Equal("140=1:40:b:2:-1:True", Guards(signedByte), "IFSW keeps its constant signed against a byte bank");
        foreach (byte value in new byte[] { 0, 1, 255 })
        {
            memory[address] = value;
            Equal(1, FieldScriptExitGuards.Apply(signedByte.Exits, signedByte.ExitGuards, space).Count,
                $"a zero-extended byte of {value} is greater than -1");
        }

        var unsignedByte = LineWithGo(code => code.IfWord(false, 1, 40, 0xFFFF, 2, "done").MapJump(140).Label("done").Ret()).Read();
        Equal("140=1:40:b:2:65535:True", Guards(unsignedByte), "IFUW's constant is unsigned");
        Equal(0, FieldScriptExitGuards.Apply(unsignedByte.Exits, unsignedByte.ExitGuards, space).Count,
            "no byte is greater than 65535");

        var signedWord = LineWithGo(code => code.IfWord(true, 2, 40, 0, 3, "done").MapJump(140).Label("done").Ret()).Read();
        Equal("140=1:40:sw:3:0:True", Guards(signedWord), "a word bank is a signed word for IFSW");
        memory[address] = 0x00;
        memory[address + 1] = 0x80;
        Equal(1, FieldScriptExitGuards.Apply(signedWord.Exits, signedWord.ExitGuards, space).Count, "0x8000 is below zero");
        memory[address] = 0xFF;
        memory[address + 1] = 0x7F;
        Equal(0, FieldScriptExitGuards.Apply(signedWord.Exits, signedWord.ExitGuards, space).Count, "0x7FFF is not");
    }

    private sealed class GuardMemory(Dictionary<uint, byte> bytes) : ILegacyAddressSpace
    {
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
    }

    private static void Equal(int expected, int actual, string what) =>
        Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture),
            actual.ToString(System.Globalization.CultureInfo.InvariantCulture), what);

    private static void Equal(string expected, string actual, string what)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{what}: expected {expected}, got {actual}");
        }
    }

    private static void Sequence(IEnumerable<int> expected, IEnumerable<int> actual, string what)
    {
        var left = expected.ToArray();
        var right = actual.ToArray();
        if (!left.SequenceEqual(right))
        {
            throw new InvalidOperationException(
                $"{what}: expected [{string.Join(", ", left)}], got [{string.Join(", ", right)}]");
        }
    }

    private static void True(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException(what);
        }
    }
}
