using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class RemainingFieldDescriptionTests
{
    internal static void Run()
    {
        var start = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        var areas = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions();
        var tracker = new FieldAreaDescriptionColdStartTracker(areas);
        Check(tracker.Observe(FieldPositionReader.FieldModule, 438, start) is null, "no description before a field settles");
        var cargo = tracker.Observe(FieldPositionReader.FieldModule, 438, start.AddSeconds(3));
        Check(cargo is { Opcode: -1 } && cargo.Value.Text.Contains("cargo", StringComparison.Ordinal),
            "a reviewed room without MPNAM is reachable through stable native observation");
        Check(tracker.Observe(FieldPositionReader.FieldModule, 438, start.AddSeconds(4)) is null,
            "a stationary visit does not repeat its scenery");
        Check(!FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions().Any(c => c.Opcode < 0),
            "observation-only scenery never becomes a synthetic native opcode anchor");
        var history = new FieldAreaDescriptionHistory(null);
        var gate = new FieldAreaDescriptionHistoryGate(history);
        Check(gate.ShouldOffer(cargo!.Value), "an unheard observed room is offered");
        gate.NoteSpoken(cargo.Value);
        Check(!gate.ShouldOffer(cargo.Value), "accepted observed scenery shares normal save-specific room history");
        Check(FieldAreaDescriptionStatus.Describe(438) == cargo.Value.Text, "explicit room readout includes the observed room");

        var actions = new FieldCutsceneDescriptionTracker(FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions());
        Check(actions.Observe(new(303, 18, 0, 0, FieldOpcodeAddressResolver.OpcodeBackgroundOnIndex)) is null,
            "coffin initialization does not invent a lid movement");
        Check(actions.Observe(new(303, 18, 2, 0, FieldOpcodeAddressResolver.OpcodeBackgroundOnIndex))?.Text == "The coffin lid closes.",
            "the actual coffin action is described at its native background event");
        Check(actions.Observe(new(557, 3, 1, 0, FieldOpcodeAddressResolver.OpcodeBackgroundOffIndex))?.Text == "A door opens.",
            "the observed background removal describes the door opening");
        var queue = new FieldCutsceneDescriptionDeliveryQueue();
        queue.Enqueue(cargo.Value);
        queue.Enqueue(new(438, 2, 2, 3, "A visible film plays."));
        var delivery = new FieldCutsceneDescriptionDelivery(queue);
        var accepted = new List<string>();
        var visible = false;
        var roomHistory = new FieldAreaDescriptionHistory(null);
        var roomGate = new FieldAreaDescriptionHistoryGate(roomHistory);
        FieldCutsceneDeliveryOutcome Tick() => delivery.Deliver(438, _ => FieldMovieNarrationStartResult.NotDescribed,
            text => { accepted.Add(text); return true; }, roomGate.NoteSpoken, out _,
            shouldOffer: roomGate.ShouldOffer,
            mayDeliver: cue => !FieldAreaDescriptionHistoryGate.IsRoomCue(cue) || visible);
        Check(Tick() == FieldCutsceneDeliveryOutcome.Spoken && accepted.Single() == "A visible film plays.",
            "hidden room scenery cannot block the visible film cue behind it");
        Check(Tick() == FieldCutsceneDeliveryOutcome.Waiting && queue.Count == 1 && !roomHistory.HasHeard(438),
            "a hidden room remains unheard while full-screen film covers it");
        visible = true;
        Check(Tick() == FieldCutsceneDeliveryOutcome.Spoken && roomHistory.HasHeard(438),
            "the room is described and committed only when it becomes visible");
    }

    internal static void RunPayloadReadiness()
    {
        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        var roots = new List<string> { Path.Combine(AppContext.BaseDirectory, "Assets", "cutscene-voice") };
        if (!string.IsNullOrWhiteSpace(source)) roots.Add(Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets", "cutscene-voice"));
        var root = roots.FirstOrDefault(path => File.Exists(Path.Combine(path, "manifest.json")));
        Check(root is not null, "new narration payload manifest must be present");
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, "manifest.json")));
        var entries = manifest.RootElement.GetProperty("entries").EnumerateArray().ToDictionary(
            row => row.GetProperty("text").GetString()!, row => row.GetProperty("file").GetString()!, StringComparer.Ordinal);
        var texts = RemainingFieldAreaDescriptions.CreateAll().Select(cue => cue.Text)
            .Concat(FieldLateGameMotionActionDescriptions.CreateAll().Select(cue => cue.Text))
            .Concat(FieldLifestreamMotionActionDescriptions.CreateAll().Select(cue => cue.Text))
            .Concat(FieldLateStoryMotionCompletionDescriptions.CreateAll().Select(cue => cue.Text))
            .Concat(new[] { "A door opens.", "The coffin lid closes.", NativeCreditsNarrator.Introduction,
                Ff7.Accessibility.LegacyLayout.NativeCreditsTitleSnapshotReader.CaptionText }).Distinct().ToArray();
        foreach (var text in texts)
        {
            Check(entries.TryGetValue(text, out var file), "recording absent for " + text);
            Check(Path.GetFileName(file) == file && new FileInfo(Path.Combine(root!, file!)).Length > 0,
                "recording is missing or invalid for " + text);
        }
        Console.WriteLine($"Remaining narration payload: all {texts.Length} reviewed texts have recordings.");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Remaining description: " + label);
    }
}
