using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class BattleNarrationLifecycleTests
{
    private static readonly DateTime Start = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly BattleAnimationObservation Braver = BattleAnimationObservation.Playing(0, 0, 0x14, 0, 0);
    private static readonly BattleAnimationObservation Shiva = BattleAnimationObservation.Playing(1, 0, 3, 1, 1);

    internal static void Run()
    {
        using (var rig = new Rig())
        {
            rig.Tick(Braver, 0);
            rig.Devices[0].End(success: false);
            rig.Tick(BattleAnimationObservation.Idle, 5.9);
            Require(!rig.History.HasHeard(0x1400), "A failed recording, even near its end, must be retryable.");
        }
        using (var rig = new Rig())
        {
            rig.Tick(Braver, 0);
            rig.Tick(BattleAnimationObservation.Idle, 20);
            Require(!rig.History.HasHeard(0x1400), "A stuck device cannot claim successful delivery.");
            Require(!rig.Narration.IsPlaying, "A stuck device must release the queue.");
        }
        using (var rig = new Rig())
        {
            rig.Tick(Braver, 0);
            rig.History.LoadSave(1, 2);
            rig.Devices[0].End(success: true);
            rig.Tick(BattleAnimationObservation.NotInBattle, 7);
            Require(!rig.History.HasHeard(0x1400), "Old playback cannot mark a newly loaded save.");
            rig.History.LoadSave(1, 1);
            Require(!rig.History.HasHeard(0x1400), "An interrupted save's recording remains retryable.");
        }
        using (var rig = new Rig())
        {
            rig.Tick(Braver, 0);
            rig.History.SaveGame(2, 3);
            rig.Devices[0].End(success: true);
            rig.Tick(BattleAnimationObservation.Idle, 7);
            Require(rig.History.HasHeard(0x1400), "Saving the same playthrough carries the completed recording.");
        }
        using (var rig = new Rig())
        {
            rig.Tick(Braver, 0);
            rig.Tick(Shiva, 1);
            rig.History.LoadSave(1, 2);
            rig.Tick(BattleAnimationObservation.Idle, 2);
            Require(!rig.Narration.IsPlaying, "Loading another save stops the previous playthrough's recording.");
            Require(rig.Devices.Count == 1, "The previous save's waiting descriptions cannot start in another save.");
            rig.Tick(Shiva, 3);
            Require(rig.Devices.Count == 2, "The same animation in the new playthrough has its own episode.");
        }
        using (var rig = new Rig())
        {
            rig.Tick(Braver, 0);
            rig.Tick(Shiva, 1);
            rig.Devices[0].End(success: true);
            rig.Tick(null, 7);
            Require(rig.Devices.Count == 1, "Unreadable native state cannot start a waiting animation description.");
            rig.Tick(BattleAnimationObservation.Idle, 7.1);
            Require(rig.Devices.Count == 2, "The waiting description resumes when battle ownership is readable.");
        }
        Console.WriteLine("PASS battle narration completion, save boundaries, and unreadable-state handling.");
    }

    private sealed class Rig : IDisposable
    {
        internal readonly FieldAreaDescriptionHistory History = new(null);
        internal readonly List<Device> Devices = [];
        internal readonly BattleAnimationNarrationCoordinator Narration;
        internal Rig()
        {
            History.LoadSave(1, 1);
            var catalog = new BattleAnimationNarrationCatalog(new[]
            {
                ("limit.cloud.braver", "Braver", "Braver description."),
                ("summon.shiva", "Shiva", "Shiva description.")
            });
            var voice = CutsceneVoiceManifest.Parse("""
                {"entries":[{"text":"Braver description.","file":"braver.ogg","duration_seconds":6},
                {"text":"Shiva description.","file":"shiva.ogg","duration_seconds":3}]}
                """);
            Narration = new(catalog, History, voice, _ =>
            {
                var output = new Device(); Devices.Add(output); return output;
            }, _ => { });
        }
        internal void Tick(BattleAnimationObservation? observation, double seconds) => Narration.Update(observation, Start.AddSeconds(seconds));
        public void Dispose() => Narration.Dispose();
    }

    private sealed class Device : IFieldMovieNarrationOutput, IFieldMovieNarrationCompletion
    {
        public bool IsPlaying { get; private set; }
        public bool CompletedNormally { get; private set; }
        public bool Start(string reason) { IsPlaying = true; CompletedNormally = false; return true; }
        internal void End(bool success) { IsPlaying = false; CompletedNormally = success; }
        public bool Stop(string reason) { IsPlaying = false; CompletedNormally = false; return true; }
        public void Dispose() { IsPlaying = false; }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
