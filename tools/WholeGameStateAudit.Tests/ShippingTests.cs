using System.Text.Json.Nodes;
using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit.Tests;

/// <summary>
/// What the production catalog does with the same synthetic fields, called through
/// reflection: its private decoder, its Talk collector and what it publishes. The catalog
/// compiled in is the repaired one, so these hold it to the engine rules FlowTests states.
/// The released 0.6.8 walker's own rules are exercised on the audit's replica of it
/// (<see cref="BaselineShippingWalk"/>), which is what explains the baseline's omissions.
/// </summary>
internal static class ShippingTests
{
    public static void Run()
    {
        ReplicaMatchesTheShippingParse();
        AliasedSlotsAreNotShippingScripts();
        APartySlotRequestIsNotTheNpcsOwnDialogue();
        ReturnToRunsTheNamedScript();
        TalkCollectionFollowsControlFlow();
        JmpflLandsOnItsTarget();
        KeyTestsFork();
        AConcurrentWriteKeepsTheWaitingExit();
        AWalkSlotSharingTheOkPointerIsAnExit();
        TheBaselineReplicaExplainsTheReleasedWalker();
        APolledTriangleIsOfferedOnlyByAStoryRowOnItInsideItsBand();
    }

    /// <summary>
    /// The ujunon2 dolphin, cut down: the Main reads the PC's triangle (GETAI into 6[6]) and,
    /// only while 2[0] &gt;= 469 and 2[0] &lt; 1008, with [SWITCH] held and 6[6] == 25, MAPJUMPs.
    /// A Story row finished on triangle 25 inside that band offers it. A row on another
    /// triangle, one offered beyond the band, one with no band, and a Model row do not, and
    /// neither does a row on the right triangle when the triangle tested is not the PC's.
    /// </summary>
    private static void APolledTriangleIsOfferedOnlyByAStoryRowOnItInsideItsBand()
    {
        Check.Current = nameof(APolledTriangleIsOfferedOnlyByAStoryRowOnItInsideItsBand);
        SyntheticField Dolphin(int gettingTriangleOf)
        {
            var field = new SyntheticField();
            field.Code.Label("adInit").Ret().Label("adMain")
                .IfSw(2, 0, 0, 1008, 3, "done").IfSw(2, 0, 0, 469, 4, "done")
                .Raw(0xB9, 0x06, gettingTriangleOf, 6)
                .IfKey(0x80, "done")
                .IfSw(6, 6, 0, 25, 0, "done")
                .MapJump(38, 0, 0, 0)
                .Label("done").Ret();
            field.Code.Label("cloudInit").Char(0).Pc(0).Ret().Label("cloudMain").Ret();
            field.Code.Label("dolphinInit").Char(1).Ret().Label("dolphinMain").Ret();
            field.Entity("ad", (0, "adInit"));
            field.Entity("cloud", (0, "cloudInit"));
            field.Entity("iruka", (0, "dolphinInit"));
            return field;
        }

        static FieldStoryEventDefinition Row(string label, int[]? triangles, int minimum, int maximum,
            FieldStoryTargetKind kind = FieldStoryTargetKind.Location) =>
            new(900, kind, label, X: -553, Y: 566, Z: -10, MinimumGameMoment: minimum, MaximumGameMoment: maximum,
                CompletionPlayerTriangles: triangles);

        string[] OfferedBy(SyntheticField field, params FieldStoryEventDefinition[] rows)
        {
            var report = new FieldReport(new AuditContext("synthetic", story: rows), field.Analyze(), "sha").Build();
            var jump = report["shipping"]!["exits"]!["engineMapJumps"]!.AsArray()
                .Single(item => item!["destination"]!.ToJsonString() == "38")!;
            Check.Equal("main-position", jump["trigger"]!.GetValue<string>(), "the whistle is a Main polling the party's triangle");
            return jump["offeredBy"]?.AsArray().Select(node => node!.GetValue<string>()).ToArray() ?? [];
        }

        var fromCloud = Dolphin(gettingTriangleOf: 1);
        Check.Sequence(["story triangle row: the band"], OfferedBy(fromCloud,
                Row("the band", [25], 469, 1007),
                Row("another triangle", [26], 469, 1007),
                Row("25 and another", [25, 26], 469, 1007),
                Row("from the first visit", [25], 400, 1007),
                Row("past the band", [25], 469, 1008),
                Row("any moment", [25], -1, -1),
                Row("a model", [25], 469, 1007, FieldStoryTargetKind.Model),
                Row("no triangle", null, 469, 1007)),
            "only the Location row on triangle 25 inside 469..1007 offers the jump");
        Check.Sequence(["story triangle row: inside"], OfferedBy(fromCloud, Row("inside", [25], 500, 600)),
            "a narrower band is still inside the native one");
        Check.Sequence([], OfferedBy(Dolphin(gettingTriangleOf: 2), Row("the band", [25], 469, 1007)),
            "a triangle read from a model that is not the party is not where the party stands");
    }

    private static (IReadOnlyList<ShippingGroup> Groups, object Native) Parse(SyntheticField field) =>
        ShippingReflection.ParseScriptGroups(field.BuildScriptSection());

    private static void ReplicaMatchesTheShippingParse()
    {
        Check.Current = nameof(ReplicaMatchesTheShippingParse);
        foreach (var field in new[] { FlowTests.PartyField(), FlowTests.ReturnToField(), LineField(code => code.JmpFL("jump").Raw(0x5F, 0x5F, 0x5F).Label("jump").MapJump(123, 1, 2, 3).Ret()) })
        {
            var view = new ShippingView(field.Analyze());
            Check.Sequence([], view.Mismatches, "replica of ParseScriptGroups agrees with the real one");
            Check.True(view.Starts.Count > 0, "slices were located");
        }
    }

    private static void AliasedSlotsAreNotShippingScripts()
    {
        Check.Current = nameof(AliasedSlotsAreNotShippingScripts);
        var field = FlowTests.PartyField();
        var (groups, _) = Parse(field);
        Check.Sequence([0, 1], groups[1].Scripts.Keys.Order(), "the listing keeps only slots with their own pointer");
        Check.Sequence([0, 3], groups[2].Scripts.Keys.Order(), "slots 1-2 and 4-31 repeat a pointer and are not listed again");
    }

    /// <summary>
    /// PREQ 0 3 asks party slot 0 to run script 3. The engine runs the party character's
    /// script 3 (dialog 8), which is the party member's line; entity 0's script 3 (dialog 9)
    /// does not run at all. The NPC has no line of its own, and nothing is misattributed.
    /// </summary>
    private static void APartySlotRequestIsNotTheNpcsOwnDialogue()
    {
        Check.Current = nameof(APartySlotRequestIsNotTheNpcsOwnDialogue);
        var field = FlowTests.PartyField();
        var (_, native) = Parse(field);
        Check.Sequence([], ShippingReflection.CollectTalkDialogIds(native, 1, 1), "the NPC's Talk has no line of its own");
        var report = new FieldReport(new AuditContext("synthetic"), field.Analyze(), "sha").Build();
        Check.True(report["shipping"]!["calls"]!.AsArray().All(item => item!["class"]!.GetValue<string>() != "PartyRequestReadAsEntity"),
            "no party request is read as a request of entity 0");
    }

    /// <summary>Talk: MESSAGE 10, RETTO 4, MESSAGE 11. The engine shows 10 then runs slot 4 (12).</summary>
    private static void ReturnToRunsTheNamedScript()
    {
        Check.Current = nameof(ReturnToRunsTheNamedScript);
        var (_, native) = Parse(FlowTests.ReturnToField());
        Check.Sequence([10, 12], ShippingReflection.CollectTalkDialogIds(native, 1, 1).Order(), "RETTO runs slot 4 instead of the bytes after it");
    }

    private static void TalkCollectionFollowsControlFlow()
    {
        Check.Current = nameof(TalkCollectionFollowsControlFlow);
        var field = new SyntheticField();
        field.Code.Label("dirInit").Ret().Label("dirMain").Ret();
        field.Code.Label("npcInit").Char(0).Ret().Label("npcMain").Ret();
        field.Code.Label("talk").JmpF("end").Message(0, 13).Label("end").Message(0, 14).Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("npc", (0, "npcInit"), (1, "talk"));
        var (_, native) = Parse(field);
        Check.Sequence([14], ShippingReflection.CollectTalkDialogIds(native, 1, 1).Order(), "the jumped-over message is not collected");
        var analysis = field.Analyze();
        var engine = analysis.Closure(analysis.EntriesById["e1.s1"]).Effects.Where(effect => effect.Effect.Kind == "Dialog").ToArray();
        Check.Sequence(["window 0 dialog 14"], engine.Select(effect => effect.Effect.Detail), "the engine shows only the one it reaches");
        var report = new FieldReport(new AuditContext("synthetic"), analysis, "sha").Build();
        Check.True(report["shipping"]!["parity"]!["deadSweptInstructions"]!.GetValue<int>() >= 1,
            "the listing's linear sweep still holds the dead message, and it is counted");
    }

    /// <summary>
    /// A LINE whose Go script (slot 4) is <paramref name="go"/>; slots 1-3 are a lone
    /// RET and slots 5-31 repeat slot 4, which is how the shipped lines are laid out.
    /// </summary>
    private static SyntheticField LineField(Action<Asm> go, Action<SyntheticField>? more = null)
    {
        var field = new SyntheticField();
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

    private static (JsonObject Walker, IReadOnlyList<Candidate> Candidates) Walk(SyntheticField field)
    {
        var context = new AuditContext("synthetic");
        var report = new FieldReport(context, field.Analyze(), "sha").Build();
        Check.Equal("published-catalog", report["shipping"]!["walkerModel"]!.GetValue<string>(), "the repaired catalog is compared on what it publishes");
        var walker = report["shipping"]!["walker"]!.AsArray().Single(item => item!["entity"]!.GetValue<int>() == 1)!.AsObject();
        return (walker, context.Global.Candidates);
    }

    private static string[] Strings(JsonObject walker, string key) =>
        walker[key]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

    /// <summary>
    /// The engine and the catalog agree on a line's exit: it is published, nothing the
    /// engine can reach is missing, and nothing published is a phantom.
    /// </summary>
    private static void AgreesOn(SyntheticField field, string exit)
    {
        var (walker, candidates) = Walk(field);
        Check.True(Strings(walker, "engineWalk").Contains(exit), $"the engine can run {exit}");
        Check.Sequence([exit], Strings(walker, "publishedExits"), "and the catalog publishes it");
        Check.Sequence([], Strings(walker, "missingExits"), "so no exit is missing");
        Check.Sequence([], Strings(walker, "phantomExits"), "and none is a phantom");
        Check.True(candidates.All(candidate => candidate.Class is not ("LineExitMissedByCatalog" or "CatalogPhantomExit")),
            "no exit candidate is raised");
    }

    /// <summary>JMPFL over three NOPs to a MAPJUMP: the engine lands on the MAPJUMP (IP + word + 1).</summary>
    private static void JmpflLandsOnItsTarget()
    {
        Check.Current = nameof(JmpflLandsOnItsTarget);
        AgreesOn(LineField(code => code.JmpFL("jump").Raw(0x5F, 0x5F, 0x5F).Label("jump").MapJump(123, 1, 2, 3).Ret()), "MapJump 123");
    }

    /// <summary>IFKEY OK: RET when the key is held, MAPJUMP when it is not. The engine can take either.</summary>
    private static void KeyTestsFork()
    {
        Check.Current = nameof(KeyTestsFork);
        AgreesOn(LineField(code => code.IfKey(0x20, "released").Ret().Label("released").MapJump(124, 1, 2, 3).Ret()), "MapJump 124");
    }

    /// <summary>
    /// The line clears 5[7] and waits for it to become 1, which entity 2's main loop does on
    /// its own. The value is not folded, so the exit after the wait is kept.
    /// </summary>
    private static void AConcurrentWriteKeepsTheWaitingExit()
    {
        Check.Current = nameof(AConcurrentWriteKeepsTheWaitingExit);
        AgreesOn(LineField(code => code
            .SetByte(5, 7, 0)
            .Label("wait")
            .IfUb(5, 7, 0, 1, 0, "notYet")
            .MapJump(125, 1, 2, 3)
            .Ret()
            .Label("notYet")
            .Wait(1)
            .JmpB("wait"), extra =>
        {
            extra.Code.Label("sigInit").Ret().Label("sigMain").SetByte(5, 7, 1).Ret();
            extra.Entity("signal", (0, "sigInit"));
        }), "MapJump 125");
    }

    /// <summary>
    /// Slot 2 [Move] repeats slot 1 [OK]'s pointer. The dispatcher (0060D29B) runs whatever
    /// a slot points at without comparing slots, so walking the line runs that code.
    /// </summary>
    private static void AWalkSlotSharingTheOkPointerIsAnExit()
    {
        Check.Current = nameof(AWalkSlotSharingTheOkPointerIsAnExit);
        var field = new SyntheticField();
        field.Code.Label("dirInit").Ret().Label("dirMain").Ret();
        field.Code.Label("lineInit").Line(0, 0, 0, 100, 0, 0).Ret().Label("lineMain").Ret();
        field.Code.Label("ok").MapJump(126, 1, 2, 3).Ret();
        field.Code.Label("rest").Ret();
        field.Entity("dir", (0, "dirInit"));
        field.Entity("exit", (0, "lineInit"), (1, "ok"), (3, "rest"));
        AgreesOn(field, "MapJump 126");
    }

    /// <summary>
    /// The released walker's rules, on the audit's replica of it: it measured JMPFL from byte
    /// 2 and did not fork IFKEY, so it never visited these MAPJUMPs, and replacing exactly
    /// that one rule with the engine's does. This is what the baseline audit's mechanisms
    /// rest on, and it describes 0.6.8 only.
    /// </summary>
    private static void TheBaselineReplicaExplainsTheReleasedWalker()
    {
        Check.Current = nameof(TheBaselineReplicaExplainsTheReleasedWalker);
        foreach (var (field, fix, label) in new[]
        {
            (LineField(code => code.JmpFL("jump").Raw(0x5F, 0x5F, 0x5F).Label("jump").MapJump(123, 1, 2, 3).Ret()), WalkFix.Jmpfl, "jump"),
            (LineField(code => code.IfKey(0x20, "released").Ret().Label("released").MapJump(124, 1, 2, 3).Ret()), WalkFix.AllConditionals, "released")
        })
        {
            var view = new ShippingView(field.Analyze());
            var target = field.At(label);
            Check.True(!new BaselineShippingWalk(view).Visit(1, 4).Contains(target), $"0.6.8's rules never reach {label}");
            Check.True(new BaselineShippingWalk(view, fix).Visit(1, 4).Contains(target), $"the engine's {fix} rule reaches it");
        }
    }
}
