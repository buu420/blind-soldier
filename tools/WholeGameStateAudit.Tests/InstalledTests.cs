using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit.Tests;

/// <summary>
/// Checks against an installed archive. They pin the audit to facts already established
/// by hand from the same data (the Wutai quest's native gates) and to the whole-game
/// claims the report makes (JMPFL alignment, the tunnel_2 folding witness).
/// </summary>
internal static class InstalledTests
{
    private const int ItemStore = 576;
    private const int Pagoda = 587;
    private const int Tunnel2 = 162;

    public static void Run(string root)
    {
        var source = new FlevelDataSource(root);
        Check.Current = "Installed";
        Check.True(source.IsUsable, $"archive usable: {source.Diagnostic}");
        if (!source.IsUsable)
        {
            return;
        }

        TheItemStoreChestKeepsItsNativeGates(source);
        TheBellOpensTriangle128OnConfirm(source);
        EveryLiveJmpflLandsOnAnInstructionUnderTheOperandRule(source);
        Tunnel2PublishesTheExitItWaitsFor(root);
        TheReturningDolphinIsOfferedByTheRowOnItsTriangle(root);
    }

    /// <summary>
    /// ujunon2 ad's Main: GETAI cloud into 6[6], [SWITCH], 6[6] == 25, ASK dialog 32, MAPJUMP 38,
    /// only while 469 &lt;= 2[0] &lt; 1008. The shipped Story row on triangle 25 for that band
    /// is what offers it, and the jump is still the player's (a position poll, not automatic).
    /// </summary>
    private static void TheReturningDolphinIsOfferedByTheRowOnItsTriangle(string root)
    {
        const int beach = 429;
        Check.Current = nameof(TheReturningDolphinIsOfferedByTheRowOnItsTriangle);
        var context = new AuditContext("installed", root, includeInstructions: false, includeText: false);
        context.Source!.TryReadField(beach, out var encoded);
        var analysis = FieldAnalysis.Analyze(beach, context.Source.FieldNames[beach], Ff7LzsDecoder.DecodeFieldFile(encoded), out _)!;
        var report = new FieldReport(context, analysis, "sha").Build();
        var jumps = report["shipping"]!["exits"]!["engineMapJumps"]!.AsArray()
            .Where(item => item!["destination"]!.ToJsonString() == "38")
            .ToArray();
        Check.Equal(1, jumps.Length, "ad has one MAPJUMP to field 38");
        Check.Equal("main-position", jumps[0]!["trigger"]!.GetValue<string>(), "and it is polled on the party's triangle");
        Check.Sequence(
            ["(2[0]w < 1008)", "(2[0]w >= 469)", "(key Switch)", "(6[6]w == 25)", "(5[8] == 2)"],
            jumps[0]!["must"]!.AsArray().Select(node => node!.GetValue<string>()),
            "the native tests on the way are the band, [SWITCH], triangle 25 and Yes");
        Check.Sequence(
            ["story triangle row: Stand at the water's edge and press Switch to call the dolphin (optional)"],
            jumps[0]!["offeredBy"]!.AsArray().Select(node => node!.GetValue<string>()),
            "the Story row on triangle 25 for 469..1007 offers it");
    }

    private static FieldAnalysis Analyze(FlevelDataSource source, int field)
    {
        source.TryReadField(field, out var encoded);
        return FieldAnalysis.Analyze(field, source.FieldNames[field], Ff7LzsDecoder.DecodeFieldFile(encoded), out _)!;
    }

    /// <summary>uta_im TAKARA 7 Talk: IFUB 3[189] &amp; 2, IFUB 1[59] &amp; 2, BITON 1[59] 1, ..., BITON 3[190] 0.</summary>
    private static void TheItemStoreChestKeepsItsNativeGates(FlevelDataSource source)
    {
        Check.Current = nameof(TheItemStoreChestKeepsItsNativeGates);
        var analysis = Analyze(source, ItemStore);
        Check.Equal("TAKARA", analysis.Entities[7].Name, "entity 7");
        var talk = analysis.Closure(analysis.EntriesById["e7.s1"]).Effects;
        var writes = talk.Where(effect => effect.Effect.Kind == "SavemapWrite").ToArray();
        var collected = writes.Single(effect => effect.Effect.Detail == "BITON 1[59] bit 1");
        Check.Sequence(["(3[189] & 2)", "not (1[59] & 2)"], collected.Must.Select(analysis.Describe), "collected bit's guards");
        Check.True(writes.Any(effect => effect.Effect.Detail == "BITON 3[190] bit 0"), "the theft sets 3[190] bit 0");
        Check.True(talk.Any(effect => effect.Chain.Contains("e4.s3")), "the theft runs through YUFI script 3");
    }

    /// <summary>uutai2 KANE 14 polls the bell's triangles with Confirm, releases 128 and sets 5[3].</summary>
    private static void TheBellOpensTriangle128OnConfirm(FlevelDataSource source)
    {
        Check.Current = nameof(TheBellOpensTriangle128OnConfirm);
        var analysis = Analyze(source, Pagoda);
        Check.Equal("KANE", analysis.Entities[14].Name, "entity 14");
        var mains = analysis.Entries.Where(entry => entry.Entity == 14 && entry.Kind == EntryKind.Main).ToArray();
        var unlocks = mains
            .SelectMany(entry => analysis.Closure(entry).Effects)
            .Where(effect => effect.Effect.Kind == "TriangleLock" && effect.Effect.Detail == "triangle 128 unlock")
            .ToArray();
        Check.True(unlocks.Length > 0, "KANE's main loop unlocks 128");
        Check.True(unlocks.All(effect => effect.Must.Any(literal => literal.Holds &&
                                                                    analysis.Conditions[literal.At].IsConfirmKey)),
            "only with Confirm held");
        Check.True(unlocks.All(effect => effect.Must.Select(analysis.Describe).Contains("(5[3] == 0)")), "only while the door is shut");
    }

    private static void EveryLiveJmpflLandsOnAnInstructionUnderTheOperandRule(FlevelDataSource source)
    {
        Check.Current = nameof(EveryLiveJmpflLandsOnAnInstructionUnderTheOperandRule);
        var total = 0;
        var operandRule = 0;
        var shippingRule = 0;
        foreach (var field in source.FieldNames.Keys)
        {
            if (!source.TryReadField(field, out var encoded))
            {
                continue;
            }

            var analysis = FieldAnalysis.Analyze(field, source.FieldNames[field], Ff7LzsDecoder.DecodeFieldFile(encoded), out _);
            if (analysis is null)
            {
                continue;
            }

            var live = analysis.Entries.Where(entry => entry.Live).SelectMany(entry => entry.Reach).ToHashSet();
            foreach (var instruction in analysis.Flow.Instructions.Values.Where(instruction => instruction.Op == 0x11 && live.Contains(instruction.Offset)))
            {
                total++;
                var target = instruction.Offset + 1 + BitConverter.ToUInt16(instruction.Bytes, 1);
                operandRule += analysis.Flow.Instructions.ContainsKey(target) ? 1 : 0;
                shippingRule += analysis.Flow.Instructions.ContainsKey(target + 1) ? 1 : 0;
            }
        }

        Check.True(total > 0, "the archive has live JMPFL");
        Check.Equal(total, operandRule, "every live JMPFL lands on an instruction when measured from its operand");
        Check.True(shippingRule < total, $"measured from byte 2 only {shippingRule} of {total} do");
    }

    /// <summary>
    /// tunnel_2 line2 (entity 5): e8.s4 sets 5[7] to 0, then waits for the scripts it
    /// requested to set it to 1 before jumping to sbwy4_1. The 0.6.8 walker folded 5[7] to 0
    /// and lost the exit; the repaired catalog publishes it, and nothing the engine can reach
    /// from the line is missing.
    /// </summary>
    private static void Tunnel2PublishesTheExitItWaitsFor(string root)
    {
        Check.Current = nameof(Tunnel2PublishesTheExitItWaitsFor);
        var context = new AuditContext("installed", root, includeInstructions: false, includeText: false);
        context.Source!.TryReadField(Tunnel2, out var encoded);
        var analysis = FieldAnalysis.Analyze(Tunnel2, context.Source.FieldNames[Tunnel2], Ff7LzsDecoder.DecodeFieldFile(encoded), out _)!;
        var report = new FieldReport(context, analysis, "sha").Build();
        Check.Equal("published-catalog", report["shipping"]!["walkerModel"]!.GetValue<string>(), "the repaired catalog is compared on what it publishes");
        var line2 = report["shipping"]!["walker"]!.AsArray().Single(item => item!["entity"]!.GetValue<int>() == 5)!;
        Check.True(line2["publishedExits"]!.AsArray().Any(node => node!.GetValue<string>() == "MapJump 164"), "the jump to 164 is published");
        Check.Sequence([], line2["missingExits"]!.AsArray().Select(node => node!.GetValue<string>()), "no exit the line can run is missing");
    }
}
