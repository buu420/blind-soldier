using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FieldAreaDescriptionHistoryTests
{
    public static void Run()
    {
        DeliveryMarksOnlyAcceptedRoomsAndSkipsQueuedDuplicates();
        StaleTitleMemoryCannotResetHistoryAfterBattle();
        var directory = Path.Combine(Path.GetTempPath(), "blind-soldier-room-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "history.json");
        try
        {
            var first = new FieldAreaDescriptionHistory(path);
            first.LoadSave(2, 11);
            Check(!first.HasHeard(546), "new save initially has no room history");
            first.MarkHeard(546);
            Check(first.HasHeard(546), "accepted room stays heard between field visits and battles");

            var restarted = new FieldAreaDescriptionHistory(path);
            restarted.LoadSave(2, 11);
            Check(restarted.HasHeard(546), "room history survives process restart for the loaded save");
            restarted.LoadSave(2, 12);
            Check(!restarted.HasHeard(546), "another game in the same save container is independent");
            restarted.MarkHeard(547);
            restarted.LoadSave(3, 11);
            Check(!restarted.HasHeard(546) && !restarted.HasHeard(547), "another save container is independent");

            restarted.LoadSave(2, 11);
            restarted.MarkHeard(548);
            restarted.SaveGame(2, 12);
            var saved = new FieldAreaDescriptionHistory(path);
            saved.LoadSave(2, 12);
            Check(saved.HasHeard(546) && saved.HasHeard(548), "successful Save As carries this playthrough's heard rooms");
            Check(!saved.HasHeard(547), "overwriting a slot does not inherit the overwritten playthrough's history");
            saved.LoadSave(2, 11);
            Check(saved.HasHeard(546) && saved.HasHeard(548), "Save As preserves the original save history");

            saved.BeginNewGame();
            Check(!saved.HasHeard(546), "new game starts with empty history");
            saved.MarkHeard(100);
            saved.SaveGame(2, 11);
            var fresh = new FieldAreaDescriptionHistory(path);
            fresh.LoadSave(2, 11);
            Check(fresh.HasHeard(100) && !fresh.HasHeard(546), "first save of a new game replaces only its destination history");

            var unbound = new FieldAreaDescriptionHistory(path);
            unbound.MarkHeard(200);
            unbound.LoadSave(2, 12);
            Check(!unbound.HasHeard(200), "unidentified session state cannot contaminate a loaded save");

            var warnings = new List<string>();
            var bad = Path.Combine(directory, "corrupt.json");
            File.WriteAllText(bad, "not json");
            var corrupt = new FieldAreaDescriptionHistory(bad, warnings.Add);
            corrupt.LoadSave(1, 1);
            corrupt.MarkHeard(546);
            Check(corrupt.HasHeard(546), "bad history storage does not disable session deduplication");
            Check(warnings.Count > 0, "unavailable persistence has a diagnostic");

            var unwritable = new FieldAreaDescriptionHistory(directory, warnings.Add);
            unwritable.LoadSave(1, 1);
            unwritable.MarkHeard(546);
            Check(unwritable.HasHeard(546), "write failure does not crash or replay the room in this session");
        }
        finally
        {
            // Only files created by this test, under its unique temporary directory.
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void DeliveryMarksOnlyAcceptedRoomsAndSkipsQueuedDuplicates()
    {
        var history = new FieldAreaDescriptionHistory(null);
        var gate = new FieldAreaDescriptionHistoryGate(history);
        var queue = new FieldCutsceneDescriptionDeliveryQueue();
        var delivery = new FieldCutsceneDescriptionDelivery(queue);
        var room = new FieldCutsceneDescriptionCue(547, 0, 0, 0,
            "Rock columns line the cave.", FieldOpcodeAddressResolver.OpcodeMapNameIndex);
        var scene = new FieldCutsceneDescriptionCue(547, 1, 1, 1, "Red XIII turns toward the opening.");
        queue.Enqueue(room);
        queue.Enqueue(room);
        queue.Enqueue(scene);
        var reservations = 0;
        var attempts = 0;
        FieldCutsceneDeliveryOutcome Deliver(bool accept) => delivery.Deliver(547,
            _ => FieldMovieNarrationStartResult.NotDescribed,
            _ => { attempts++; return accept; },
            cue => { reservations++; gate.NoteSpoken(cue); },
            out _, shouldOffer: gate.ShouldOffer);

        Check(Deliver(false) == FieldCutsceneDeliveryOutcome.Refused && !history.HasHeard(547),
            "refused output retains the room without spending persistent history");
        Check(queue.Count == 3 && reservations == 0, "a refused room reserves no dialogue time");
        Check(Deliver(true) == FieldCutsceneDeliveryOutcome.Spoken && history.HasHeard(547),
            "accepted output records the room");
        Check(Deliver(true) == FieldCutsceneDeliveryOutcome.Skipped && attempts == 2 && reservations == 1,
            "a queued duplicate must not speak or reserve dialogue time");
        Check(Deliver(true) == FieldCutsceneDeliveryOutcome.Spoken && attempts == 3 && queue.Count == 0,
            "story actions still speak after the room has been heard");
    }

    private static void StaleTitleMemoryCannotResetHistoryAfterBattle()
    {
        var history = new FieldAreaDescriptionHistory(null);
        history.LoadSave(1, 1);
        history.MarkHeard(547);
        var tracker = new FieldAreaDescriptionSaveTracker(history);
        var playableSeen = true;
        var staleTitle = new TitleLoadMenuStateSnapshot(TitleLoadMenuPage.TitleRoot, 0, false, 0, null);
        // The menu reader validates its structure, not module ownership. Its
        // old bytes must not look like a fresh title visit during gameplay.
        foreach (var module in new[] { 1, 23, 2, 17, 1 })
            FieldAreaDescriptionSaveObserver.Observe(tracker, module, null, null,
                TitleLoadMenuDataReader.InteractiveReadiness, staleTitle, ref playableSeen);
        Check(history.HasHeard(547), "stale title bytes must not reset room history after battle");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Room history: " + message);
    }
}
