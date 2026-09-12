using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class SetoVisualDescriptionTests
{
    // Independent evidence: installed opcodes and operands matched to the reviewed
    // Seto scene. Keep operands here so an animation or requested actor changing is
    // caught, even when the opcode at that address still has the same name.
    private static readonly (int Entity, int Script, int Byte, byte[] Bytes)[] Anchors =
    [
        (8, 3, 217, [0x02, 1, 0xC8]), // Cloud's departure, after Bugenhagen's request.
        (10, 9, 0, [0xA2, 8, 1]), // Arms spread during the "thinking lately" line.
        (8, 3, 406, [0x03, 5, 0xCD]), // Red's final two jumps toward his father.
        (5, 13, 103, [0xBC, 11, 0, 28, 1]), // Raises his head before the upward pan.
        (11, 3, 17, [0xA4, 1]) // First tear visible, not VISI0 at48.
    ];

    internal static void Run(string gameRoot)
    {
        var all = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        var native = new FieldScriptNavigationCatalog(gameRoot);
        var tracker = new FieldCutsceneDescriptionTracker(all);
        foreach (var anchor in Anchors)
        {
            var key = new FieldCutsceneDescriptionKey(550, anchor.Entity, anchor.Script, anchor.Byte);
            var cue = all.Single(candidate => candidate.Key == key);
            var opcode = native.ReadScriptOpcodes(550, anchor.Entity, anchor.Script)
                .Single(candidate => candidate.ByteIndex == anchor.Byte);
            Check(opcode.Bytes.SequenceEqual(anchor.Bytes), $"installed Seto instruction changed at {key}");
            Check(cue.Opcode == anchor.Bytes[0], $"wrong Seto callback at {key}");
            var context = new FieldScriptContext(550, anchor.Entity, anchor.Script, anchor.Byte, cue.Opcode);
            Check(tracker.Observe(context with { ByteIndex = anchor.Byte + 1 }) is null, "nearby byte must stay silent");
            Check(tracker.Observe(context with { Opcode = cue.Opcode ^ 1 }) is null, "wrong callback must stay silent");
            Check(tracker.Observe(context)?.Text == cue.Text, "exact visible action must be described");
            Check(tracker.Observe(context) is null, "yielding callback must not repeat the description");
        }

        Check(FieldCutsceneDescriptionCatalog.CreateSetoVisualDescriptions().Count == 5,
            "only the five independently matched actions are approved");
        Check(!all.Any(cue => cue.Key == new FieldCutsceneDescriptionKey(550, 10, 8, 0)),
            "the unrelated long animation must not announce the arms-spread pose");
        Check(!all.Any(cue => cue.FieldId == 550 && cue.EntityId is 12 or 13 && cue.ScriptId == 3),
            "the other tear entities must not repeat the same description");
        Check(native.ReadScriptOpcodes(550, 14, 4).Single(op => op.ByteIndex == 0).Opcode == 0xF9,
            "the earlier Seto reveal remains a separate film");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
