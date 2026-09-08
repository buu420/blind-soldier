using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class CorelJourneyDescriptionTests
{
    // Reviewed native instructions for first-visit visual actions. Independent
    // expectations prevent a missing catalog row from silently reducing coverage.
    private static readonly (int Field, int Entity, int Script, int Byte, int Opcode)[] Anchors =
    [
        (449, 5, 9, 0, 0x09), // Party gathers by Hojo.
        (449, 12, 14, 149, 0xBA), // Hojo turns away at the conversation's end.
        (464, 9, 5, 151, 0x02), // Lowering animation requested after accepting the switch choice.
        (463, 0, 0, 66, 0x24), // Landing, before the otherwise silent manual climbing handoff.
        (450, 13, 4, 13, 0x24), // Barret's fall after the townsman's punch.
        (469, 3, 0, 34, 0x24), // Original Corel, after the flashback fade-in.
        (483, 2, 0, 6, 0x24), // Village meeting, after its fade-in.
        (470, 3, 0, 53, 0x24), // Burning town pan.
        (457, 2, 3, 109, 0xF9), // First cable-car departure.
        (457, 2, 4, 16, 0xF9), // Departure after declining/reboarding.
        (496, 0, 0, 190, 0xF9), // Gold Saucer panorama movie40.
        (496, 0, 0, 201, 0xF9), // Arrival at the upper cable-car station.
    ];

    internal static void Run()
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is required.");
        var all = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        var native = new FieldScriptNavigationCatalog(gameRoot);
        var tracker = new FieldCutsceneDescriptionTracker(all);
        foreach (var anchor in Anchors)
        {
            var key = new FieldCutsceneDescriptionKey(anchor.Field, anchor.Entity, anchor.Script, anchor.Byte);
            var matches = all.Where(cue => cue.Key == key).ToArray();
            Equal(1, matches.Length, $"missing Corel scene description at {key}");
            var cue = matches[0];
            Equal(anchor.Opcode, cue.Opcode, $"description handler at {key}");
            Equal(anchor.Opcode, native.ReadScriptOpcodes(anchor.Field, anchor.Entity, anchor.Script)
                .Single(opcode => opcode.ByteIndex == anchor.Byte).Opcode, $"installed native instruction at {key}");
            var context = new FieldScriptContext(anchor.Field, anchor.Entity, anchor.Script, anchor.Byte, anchor.Opcode);
            Equal(null, tracker.Observe(context with { ByteIndex = anchor.Byte + 1 }), "nearby byte is silent");
            Equal(null, tracker.Observe(context with { Opcode = anchor.Opcode ^ 1 }), "wrong handler is silent");
            Equal(cue.Text, tracker.Observe(context)?.Text, "native scene delivers its description");
            Equal(null, tracker.Observe(context), "repeated callback remains silent");
        }
        Equal(all.Count, all.Select(cue => cue.Key).Distinct().Count(), "description anchors remain unique");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}
