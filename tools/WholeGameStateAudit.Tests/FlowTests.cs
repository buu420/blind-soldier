namespace WholeGameStateAudit.Tests;

/// <summary>Control flow, guards, the init/main split, and calls between scripts.</summary>
internal static class FlowTests
{
    public static void Run()
    {
        ConditionalBranchGuards();
        EarlyReturnEndsThePath();
        LoopGuardsAreIntersections();
        InitMainSplitWithAConditionalReturn();
        SharedEntryOffsetsRunTheSameCode();
        CrossScriptRequestCarriesTheCallSitesGuard();
        PartySlotRequestsAreDynamic();
        ReturnToRunsTheNamedScriptAndNothingAfterIt();
        BankedConditions();
    }

    /// <summary>A director, and model 1 whose Talk (slot 1) is <paramref name="talk"/>.</summary>
    private static SyntheticField ModelWithTalk(Action<Asm> talk, Action<SyntheticField>? more = null)
    {
        var field = new SyntheticField();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("npcInit").Char(0).Ret().Label("npcMain").Ret();
        code.Label("talk");
        talk(code);
        field.Entity("dir", (0, "dirInit"));
        field.Entity("npc", (0, "npcInit"), (1, "talk"));
        more?.Invoke(field);
        return field;
    }

    private static IReadOnlyList<ClosureEffect> Closure(FieldAnalysis analysis, string entry) =>
        analysis.Closure(analysis.EntriesById[entry]).Effects;

    private static string[] Must(FieldAnalysis analysis, ClosureEffect effect) =>
        effect.Must.Select(analysis.Describe).ToArray();

    private static ClosureEffect Dialog(FieldAnalysis analysis, IReadOnlyList<ClosureEffect> effects, int dialog) =>
        effects.Single(effect => effect.Effect.Kind == "Dialog" && effect.Effect.Detail.EndsWith($"dialog {dialog}", StringComparison.Ordinal));

    /// <summary>
    /// <c>IFUB 3[189] &amp; 2</c> guards the first message only: the second is reached both
    /// through the test and around it, so nothing must hold for it, but it is path dependent.
    /// </summary>
    private static void ConditionalBranchGuards()
    {
        Check.Current = nameof(ConditionalBranchGuards);
        var analysis = ModelWithTalk(code => code
            .IfUb(3, 189, 0, 2, 6, "after")
            .Message(0, 1)
            .Label("after")
            .Message(0, 2)
            .Ret()).Analyze();
        var talk = Closure(analysis, "e1.s1");
        Check.Sequence(["(3[189] & 2)"], Must(analysis, Dialog(analysis, talk, 1)), "dialog 1 needs the bit");
        Check.Sequence([], Must(analysis, Dialog(analysis, talk, 2)), "dialog 2 is reached either way");
        Check.True(Dialog(analysis, talk, 2).PathDependent > 0, "dialog 2 is marked path dependent");
        Check.Equal(EntityKind.Model, analysis.Entities[1].Kind, "CHAR in init makes a model");
        Check.True(analysis.EntriesById["e1.s1"].EngineTriggered, "a model's slot 1 is its Talk");
    }

    private static void EarlyReturnEndsThePath()
    {
        Check.Current = nameof(EarlyReturnEndsThePath);
        var analysis = ModelWithTalk(code => code
            .IfUb(5, 0, 0, 0, 0, "later")
            .Ret()
            .Label("later")
            .Message(0, 3)
            .Ret()).Analyze();
        var talk = Closure(analysis, "e1.s1");
        Check.Sequence(["not (5[0] == 0)"], Must(analysis, Dialog(analysis, talk, 3)), "the message needs the test to fail");
        var firstReturn = analysis.EntriesById["e1.s1"].Offset + 6;
        Check.Equal(0x00, (int)analysis.Flow.Instructions[firstReturn].Op, "the early RET is decoded");
        Check.Equal(0, analysis.Flow.Successors(firstReturn).Count, "RET has no successor");
    }

    /// <summary>A loop's body keeps its test; the exit keeps the test's failure.</summary>
    private static void LoopGuardsAreIntersections()
    {
        Check.Current = nameof(LoopGuardsAreIntersections);
        var analysis = ModelWithTalk(code => code
            .Label("loop")
            .IfUb(5, 0, 0, 0, 0, "out")
            .Message(0, 15)
            .JmpB("loop")
            .Label("out")
            .Message(0, 16)
            .Ret()).Analyze();
        var talk = Closure(analysis, "e1.s1");
        Check.Sequence(["(5[0] == 0)"], Must(analysis, Dialog(analysis, talk, 15)), "loop body");
        Check.Sequence(["not (5[0] == 0)"], Must(analysis, Dialog(analysis, talk, 16)), "loop exit");
    }

    /// <summary>
    /// Makou splits Init from Main at the first return outside a pending forward jump; a
    /// return inside that jump's span can still execute first. Both starts are kept and the
    /// entity is reported as ambiguous.
    /// </summary>
    private static void InitMainSplitWithAConditionalReturn()
    {
        Check.Current = nameof(InitMainSplitWithAConditionalReturn);
        var field = new SyntheticField();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("init").Char(0).IfUb(5, 1, 0, 0, 0, "second").Ret()
            .Label("second").TalkOn(false).Ret()
            .Label("main").Message(0, 4).Ret();
        code.Label("talk").Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("npc", (0, "init"), (1, "talk"));
        var analysis = field.Analyze();
        var entity = analysis.Entities[1];
        Check.Equal<int?>(field.At("main"), entity.MakouMainOffset, "Makou's split skips the return inside the jump");
        Check.Sequence([field.At("second"), field.At("main")], entity.MainOffsets, "both returns can end Init");
        Check.Equal(2, entity.InitReturns.Count, "two returns reachable from Init");
        Check.True(entity.Issues.Any(issue => issue.StartsWith("init/main split ambiguous", StringComparison.Ordinal)), "ambiguity reported");
        Check.True(analysis.EntriesById.ContainsKey($"e1.main@{field.At("main")}"), "Makou's main is an entry");
        Check.True(Closure(analysis, $"e1.main@{field.At("main")}").Any(effect => effect.Effect.Kind == "Dialog"), "main's message is found");
    }

    /// <summary>
    /// Slot 2 and slot 4 repeat slots 1 and 3's pointers, as unused slots do in the shipped
    /// files; the engine runs whatever a slot points at, so a request of slot 4 runs slot
    /// 3's code.
    /// </summary>
    private static void SharedEntryOffsetsRunTheSameCode()
    {
        Check.Current = nameof(SharedEntryOffsetsRunTheSameCode);
        var analysis = ModelWithTalk(code => code.Req(2, 4).Ret(), field =>
        {
            field.Code.Label("shopInit").Char(1).Ret().Label("shopMain").Ret();
            field.Code.Label("shopTalk").Message(0, 5).Ret();
            field.Code.Label("shop3").Message(0, 6).Ret();
            field.Entity("shop", (0, "shopInit"), (1, "shopTalk"), (3, "shop3"));
        }).Analyze();
        Check.Equal<int?>(1, analysis.EntriesById["e2.s2"].AliasOf, "slot 2 aliases slot 1");
        Check.Equal<int?>(3, analysis.EntriesById["e2.s4"].AliasOf, "slot 4 aliases slot 3");
        var site = analysis.CallSites.Single(call => call.FromEntry == "e1.s1");
        Check.Sequence(["e2.s4"], site.Targets, "the request resolves to slot 4");
        Check.True(site.Problem?.StartsWith("target slot aliases slot 3", StringComparison.Ordinal) == true, "and is flagged as an aliased slot");
        var talk = Closure(analysis, "e1.s1");
        Check.True(talk.Any(effect => effect.Effect.Detail.EndsWith("dialog 6", StringComparison.Ordinal)), "slot 3's message runs");
        Check.True(analysis.EntriesById["e2.s4"].Live, "the aliased slot is live");
        Check.True(Closure(analysis, "e2.s2").Any(effect => effect.Effect.Detail.EndsWith("dialog 5", StringComparison.Ordinal)),
            "touching the shop runs the Talk code its Contact slot points at");
    }

    private static void CrossScriptRequestCarriesTheCallSitesGuard()
    {
        Check.Current = nameof(CrossScriptRequestCarriesTheCallSitesGuard);
        var analysis = ModelWithTalk(code => code
            .IfUb(1, 59, 0, 2, 6, "skip")
            .Req(2, 3)
            .Label("skip")
            .Ret(), field =>
        {
            field.Code.Label("evtInit").Ret().Label("evtMain").Ret();
            field.Code.Label("evt3").Message(0, 7).Ret();
            field.Entity("evt", (0, "evtInit"), (3, "evt3"));
        }).Analyze();
        var dialog = Dialog(analysis, Closure(analysis, "e1.s1"), 7);
        Check.Equal("e2.s3", dialog.EntryId, "the message is in the requested script");
        Check.Sequence(["e1.s1", "e2.s3"], dialog.Chain, "call chain");
        Check.Sequence(["(1[59] & 2)"], Must(analysis, dialog), "the call site's guard carries over");
        Check.True(!dialog.Dynamic, "an entity request is static");
    }

    /// <summary>
    /// PREQ names a party slot. The engine runs whichever character is in it, so every
    /// party-character entity is a candidate and the effects are dynamic.
    /// </summary>
    private static void PartySlotRequestsAreDynamic()
    {
        Check.Current = nameof(PartySlotRequestsAreDynamic);
        var field = PartyField();
        var analysis = field.Analyze();
        var site = analysis.CallSites.Single(call => call.FromEntry == "e1.s1");
        Check.Sequence(["e2.s3"], site.Targets, "the party character is the only candidate");
        var dialog = Dialog(analysis, Closure(analysis, "e1.s1"), 8);
        Check.True(dialog.Dynamic, "its message is dynamic");
        Check.True(Closure(analysis, "e1.s1").All(effect => !effect.Effect.Detail.EndsWith("dialog 9", StringComparison.Ordinal)),
            "entity 0's script 3 is not what a party slot 0 request runs");
    }

    /// <summary>Talk: PREQ party slot 0 script 3. Entity 0 (a director) has script 3 too.</summary>
    public static SyntheticField PartyField()
    {
        var field = new SyntheticField();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret().Label("dir3").Message(0, 9).Ret();
        code.Label("npcInit").Char(0).Ret().Label("npcMain").Ret().Label("talk").Preq(0, 3).Ret();
        code.Label("cloudInit").Char(1).Pc(0).Ret().Label("cloudMain").Ret().Label("cloud3").Message(0, 8).Ret();
        field.Entity("dir", (0, "dirInit"), (3, "dir3"));
        field.Entity("npc", (0, "npcInit"), (1, "talk"));
        field.Entity("cloud", (0, "cloudInit"), (3, "cloud3"));
        return field;
    }

    private static void ReturnToRunsTheNamedScriptAndNothingAfterIt()
    {
        Check.Current = nameof(ReturnToRunsTheNamedScriptAndNothingAfterIt);
        var analysis = ReturnToField().Analyze();
        var talk = Closure(analysis, "e1.s1");
        var dialogs = talk.Where(effect => effect.Effect.Kind == "Dialog").Select(effect => effect.Effect.Detail).Order().ToArray();
        Check.Sequence(["window 0 dialog 10", "window 0 dialog 12"], dialogs, "RETTO runs slot 4, not the bytes after it");
        Check.Equal(CallKind.ReturnTo, analysis.CallSites.Single(call => call.FromEntry == "e1.s1").Call.Kind, "RETTO is a call");
    }

    public static SyntheticField ReturnToField() => ModelWithTalk(code => code
        .Message(0, 10)
        .Retto(4)
        .Message(0, 11)
        .Ret(), field =>
    {
        field.Code.Label("npc4").Message(0, 12).Ret();
        field.Slot(1, 4, "npc4");
    });

    private static void BankedConditions()
    {
        Check.Current = nameof(BankedConditions);
        var analysis = ModelWithTalk(code => code
            .IfUb(1, 5, 5, 3, 0, "a").Label("a")
            .IfSw(2, 0, 0, 1008, 4, "b").Label("b")
            .IfSw(12, 4, 0, -5, 0, "c").Label("c")
            .SetByte(15, 7, 1)
            .Raw(0x81, 0x70, 9, 1, 0)
            .Ret()).Analyze();
        var both = analysis.Conditions.Values.Single(condition => condition.Operator == 0 && condition.Width == 1);
        Check.Equal("1[5] == 5[3]", both.Text, "a byte test between two variables");
        Check.Equal("1", both.Left!.Variable!.Block, "bank 1 is savemap block 1");
        Check.Equal("T", both.Right!.Variable!.Block, "bank 5 is the temporary bank");
        var moment = analysis.Conditions.Values.Single(condition => condition.Operator == 4);
        Check.Equal("2[0]w >= 1008", moment.Text, "GameMoment is a word at bank 2 address 0");
        Check.True(ByteKey.Of(moment.Left!.Variable!).All(key => key.IsGameMoment), "both of its bytes are GameMoment");
        var signed = analysis.Conditions.Values.Single(condition => condition.Signed && condition.Operator == 0);
        Check.Equal(-5, signed.Right!.Raw, "IFSW compares a signed immediate");
        Check.Equal("3", signed.Left!.Variable!.Block, "bank 12 is block 3");
        var writes = analysis.Flow.Instructions.Values.SelectMany(Semantics.Writes).Where(write => write.Variable is not null).ToArray();
        Check.True(writes.Any(write => write.Variable!.Block == "5" && write.Variable.Bank == 15 && write.Value == 1), "bank 15 is block 5");
        Check.True(writes.Any(write => write.Variable!.Block == "5" && write.Variable.Bank == 7 && write.Variable.Width == 2), "bank 7 is block 5's word bank");
    }
}
