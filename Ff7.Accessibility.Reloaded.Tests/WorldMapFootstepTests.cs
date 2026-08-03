using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapFootstepTests
{
    internal static void Run()
    {
        SelectsTheCosmoModelAndTerrainTrackBeforeTheTerrainFallback();
        UsesActualWrappedMovementAndStaysSilentWhenBlocked();
        UsesTheLongerNativeChocoboCadence();
        RejectsVehicleMovementAndTeleports();
    }

    private static void SelectsTheCosmoModelAndTerrainTrackBeforeTheTerrainFallback()
    {
        var config = CosmoFootstepConfig.Parse("""
            [wm_footsteps_0_9_159]
            sequential = [5001, 5002]
            [wm_footsteps_9_159]
            sequential = [5999]
            """);
        var sequencer = new CosmoFootstepSequencer(config, new Dictionary<int, string>(), @"C:\sounds");

        Equal(5001, sequencer.SelectNext(State(x: 10, terrain: 9)).SoundId, "first exact world step");
        Equal(5002, sequencer.SelectNext(State(x: 20, terrain: 9)).SoundId, "second exact world step");
        Equal(5999, sequencer.SelectNext(State(x: 30, terrain: 9, model: 2)).SoundId, "terrain fallback");
    }

    private static void UsesActualWrappedMovementAndStaysSilentWhenBlocked()
    {
        var tracker = new WorldMapFootstepTracker(0x48000, 0x38000, TimeSpan.FromMilliseconds(300));
        var now = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);
        Equal(false, tracker.Observe(State(x: 0x47FF0), now), "prime at seam");
        Equal(false, tracker.Observe(State(x: 0x47FF0), now.AddMilliseconds(320)), "blocked stays silent");
        Equal(true, tracker.Observe(State(x: 8), now.AddMilliseconds(400)), "wrapped post-collision movement");
    }

    private static void UsesTheLongerNativeChocoboCadence()
    {
        var tracker = new WorldMapFootstepTracker(0x48000, 0x38000);
        var now = DateTime.UtcNow;
        Equal(false, tracker.Observe(State(x: 0, model: 19), now), "prime chocobo");
        Equal(false, tracker.Observe(State(x: 20, model: 19), now.AddMilliseconds(350)), "chocobo cadence not yet elapsed");
        Equal(true, tracker.Observe(State(x: 40, model: 19), now.AddMilliseconds(510)), "chocobo stride elapsed");
    }

    private static void RejectsVehicleMovementAndTeleports()
    {
        var tracker = new WorldMapFootstepTracker(0x48000, 0x38000, TimeSpan.Zero);
        var now = DateTime.UtcNow;
        Equal(false, tracker.Observe(State(x: 0, model: 3), now), "Highwind has no footsteps");
        Equal(false, tracker.Observe(State(x: 0), now.AddMilliseconds(10)), "walking state primes");
        Equal(false, tracker.Observe(State(x: 5000), now.AddMilliseconds(20)), "teleport is silent");
        Equal(true, tracker.Observe(State(x: 5010), now.AddMilliseconds(30)), "movement resumes after teleport");
    }

    private static WorldMapStateSnapshot State(int x, int terrain = 0, int model = 0) =>
        new(
            WorldMapStateReader.WorldModule,
            0,
            0,
            341,
            x,
            0,
            100,
            0,
            0,
            terrain,
            0,
            model,
            30,
            0,
            new FieldNavigationControlTransform(0));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }
}
