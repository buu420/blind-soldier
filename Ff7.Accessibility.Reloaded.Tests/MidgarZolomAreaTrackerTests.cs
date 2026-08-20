using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The marsh is its own terrain on the world map and the Zolom is a serpent
/// visibly crossing it, so a sighted player knows both that they have stepped
/// onto it and whether the thing that attacks them is still there. Without an
/// area cue the first notice a player gets is the battle transition.
/// </summary>
internal static class MidgarZolomAreaTrackerTests
{
    internal static void Run()
    {
        AnnouncesTheMarshOnceOnEntryAndAgainOnLeaving();
        NamesTheZolomOnlyWhileItIsStillOnTheMarsh();
        SaysNothingExtraWhenMountedBecauseTheRiderIsNotPrey();
        StandsDownWhenTheCrossingCueHasAlreadySpoken();
        StaysSilentOffTheMarshAndOutsideTheWorldMap();
    }

    private static void AnnouncesTheMarshOnceOnEntryAndAgainOnLeaving()
    {
        var tracker = new MidgarZolomAreaTracker();
        var player = Player();

        Equal(
            MidgarZolomAreaTracker.EnteredWithZolomText,
            tracker.Observe(player, LiveZolom, isOnMarshTerrain: true, crossingCueSpoken: false),
            "stepping onto the marsh is announced");
        Equal(
            null,
            tracker.Observe(player, LiveZolom, isOnMarshTerrain: true, crossingCueSpoken: false),
            "walking on across the marsh does not repeat the cue");
        Equal(
            MidgarZolomAreaTracker.LeftText,
            tracker.Observe(player, LiveZolom, isOnMarshTerrain: false, crossingCueSpoken: false),
            "leaving the marsh is worth knowing too");
        Equal(
            null,
            tracker.Observe(player, LiveZolom, isOnMarshTerrain: false, crossingCueSpoken: false),
            "staying off the marsh is silent");
        Equal(
            MidgarZolomAreaTracker.EnteredWithZolomText,
            tracker.Observe(player, LiveZolom, isOnMarshTerrain: true, crossingCueSpoken: false),
            "stepping back on rearms the cue");
    }

    /// <summary>
    /// Once Sephiroth has left the Zolom impaled on a tree it is no longer on the
    /// map, and a sighted player can see that. Naming it anyway would describe
    /// something that is not there.
    /// </summary>
    private static void NamesTheZolomOnlyWhileItIsStillOnTheMarsh()
    {
        Equal(
            MidgarZolomAreaTracker.EnteredText,
            new MidgarZolomAreaTracker().Observe(
                Player(), DeadZolom, isOnMarshTerrain: true, crossingCueSpoken: false),
            "with the Zolom gone the marsh is still named but the serpent is not");

        Equal(
            MidgarZolomAreaTracker.EnteredText,
            new MidgarZolomAreaTracker().Observe(
                Player(),
                MidgarZolomStateReadResult.Invalid(default, "unreadable"),
                isOnMarshTerrain: true,
                crossingCueSpoken: false),
            "an unreadable Zolom is never asserted to be present");
    }

    private static void SaysNothingExtraWhenMountedBecauseTheRiderIsNotPrey()
    {
        Equal(
            MidgarZolomAreaTracker.EnteredText,
            new MidgarZolomAreaTracker().Observe(
                Player(model: 4), LiveZolom, isOnMarshTerrain: true, crossingCueSpoken: false),
            "a chocobo crosses the marsh unmolested, and its rider can see they are mounted");
    }

    /// <summary>
    /// Both cues trigger within a step of each other at the shore. The crossing
    /// cue is the time-critical one, so the area cue gives way rather than
    /// talking over it - but must still count the player as having arrived.
    /// </summary>
    private static void StandsDownWhenTheCrossingCueHasAlreadySpoken()
    {
        var tracker = new MidgarZolomAreaTracker();
        var player = Player();

        Equal(
            null,
            tracker.Observe(player, LiveZolom, isOnMarshTerrain: true, crossingCueSpoken: true),
            "the area cue does not talk over the crossing cue");
        Equal(
            null,
            tracker.Observe(player, LiveZolom, isOnMarshTerrain: true, crossingCueSpoken: false),
            "and does not fire late once the crossing cue has finished");
        Equal(
            MidgarZolomAreaTracker.LeftText,
            tracker.Observe(player, LiveZolom, isOnMarshTerrain: false, crossingCueSpoken: false),
            "leaving still reports, so the player knows they are clear");
    }

    private static void StaysSilentOffTheMarshAndOutsideTheWorldMap()
    {
        Equal(
            null,
            new MidgarZolomAreaTracker().Observe(
                Player(), LiveZolom, isOnMarshTerrain: false, crossingCueSpoken: false),
            "grassland is not announced");

        var inField = Player() with { CurrentModule = 1 };
        Equal(
            null,
            new MidgarZolomAreaTracker().Observe(
                inField, LiveZolom, isOnMarshTerrain: true, crossingCueSpoken: false),
            "a stale marsh triangle outside the world module says nothing");
    }

    private static MidgarZolomStateReadResult LiveZolom =>
        MidgarZolomStateReadResult.Valid(
            new MidgarZolomStateSnapshot(true, 230_000, 145_000, 0),
            "active");

    private static MidgarZolomStateReadResult DeadZolom =>
        MidgarZolomStateReadResult.Valid(
            new MidgarZolomStateSnapshot(false, 0, 0, 0),
            "inactive");

    private static WorldMapStateSnapshot Player(int model = 0) =>
        new(
            WorldMapStateReader.WorldModule,
            0,
            0,
            385,
            230_723,
            0,
            142_596,
            0,
            0,
            0,
            1,
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
