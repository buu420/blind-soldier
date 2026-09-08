using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class JunonJourneyDescriptionTests
{
    // Observed actions, mapped independently from footage to installed scripts.
    // Do not replace these with a test that reads expected keys from the catalog.
    private static readonly (int Field, int Entity, int Script, int Byte, int Opcode)[] Anchors =
    [
        (387, 17, 3, 17, 0xA4), // Uniform reveal, immediately before walking out.
        (361, 14, 9, 93, 0xA3), // Heidegger among the scattered parade soldiers.
        (361, 5, 1, 50, 0x02), // Troops run off after the ceremony.
        (387, 17, 16, 32, 0xA3), // Cloud demonstrates his finishing pose.
        (382, 3, 0, 32, 0x03), // Dock pan has begun, before the performance.
        (382, 19, 11, 34, 0xA3), // Heidegger advances; troops recoil.
        (436, 14, 1, 135, 0xA2), // Red's first conversation, never his ambient loop.
        (437, 3, 1, 129, 0xBA), // Barret raises his fists after leaving the window.
        (440, 15, 5, 29, 0x03), // Crewman collapses and fades away.
        (440, 15, 5, 155, 0x6B), // Flight/fall and green flashes share one short cue.
        (440, 15, 5, 299, 0x24), // Visible aftermath after the battle fade-in.
        (441, 7, 6, 15, 0xA4), // Cloud appears after his companions disembark.
        (442, 9, 3, 33, 0xA4), // Helicopter appears over the dock.
        (442, 8, 11, 17, 0x03), // First of the two visible sailor throws.
        (442, 9, 4, 45, 0x24), // Ten-frame wait before the rotor loop and lift-off.
    ];

    internal static void Run(string? gameRoot = null)
    {
        gameRoot ??= Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(gameRoot))
            throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is required for native scene verification.");

        var all = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        Equal(all.Count, all.Select(cue => cue.Key).Distinct().Count(), "all description anchors are unique");
        var native = new FieldScriptNavigationCatalog(gameRoot);
        foreach (var group in Anchors.GroupBy(anchor => anchor.Field))
        {
            // The shared tracker is also used behind x64's validated translated
            // callback lease. The x86 Echo-S wrapper has separate real-prefix
            // authorization/modified-script regressions in EchoSCompatibilityTests.
            var tracker = new FieldCutsceneDescriptionTracker(all);
            foreach (var anchor in group)
            {
                var key = new FieldCutsceneDescriptionKey(anchor.Field, anchor.Entity, anchor.Script, anchor.Byte);
                var matches = all.Where(cue => cue.Key == key).ToArray();
                Equal(1, matches.Length, $"missing visible scene description at {key}");
                var cue = matches[0];
                Equal(anchor.Opcode, cue.Opcode, $"scene handler at {key}");
                Equal(anchor.Opcode, native.ReadScriptOpcodes(anchor.Field, anchor.Entity, anchor.Script)
                    .Single(opcode => opcode.ByteIndex == anchor.Byte).Opcode, $"installed native instruction at {key}");
                var context = new FieldScriptContext(anchor.Field, anchor.Entity, anchor.Script, anchor.Byte, anchor.Opcode);
                Equal(null, tracker.Observe(context with { ByteIndex = anchor.Byte + 1 }), "nearby instruction is silent");
                Equal(null, tracker.Observe(context with { Opcode = anchor.Opcode ^ 1 }), "wrong handler is silent");
                Equal(cue.Text, tracker.Observe(context)?.Text, "exact native scene is narratable");
                Equal(null, tracker.Observe(context), "repeated native execution does not repeat narration");
            }
        }

        Equal(false, all.Any(cue => cue.FieldId == 436 && cue.EntityId == 14 && cue.ScriptId == 0),
            "Red's offscreen ambient animation must not narrate on field entry");
        Equal(true, all.Single(cue => cue.Key == new FieldCutsceneDescriptionKey(440, 15, 5, 93))
            .Text.Contains("rises through the floor", StringComparison.Ordinal), "Sephiroth's visible entrance is described");
        Equal(false, all.Any(cue => cue.Key == new FieldCutsceneDescriptionKey(440, 15, 5, 135)),
            "the four-second pre-battle window must not queue a second competing description");
        var preBattle = all.Single(cue => cue.Key == new FieldCutsceneDescriptionKey(440, 15, 5, 155)).Text;
        Equal(true, preBattle.Contains("Cloud falls", StringComparison.Ordinal) && preBattle.Split(' ').Length <= 10,
            "the flight, fall and green flashes fit one short cue before battle");
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{description}: expected {expected}, got {actual}.");
    }
}
