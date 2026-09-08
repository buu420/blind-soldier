using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class WorldMapTerrainAnnouncementTests
{
    private static readonly DateTime Epoch = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        DecodesTheNativeTrackBitWithoutPollutingTheRegion();
        AnnouncesOnlyAStableTerrainTransition();
        RequiresBothEnoughSamplesAndEnoughElapsedTime();
        RidingAlongABoundaryDoesNotChatter();
        CrossingATransientTriangleSliverDoesNotAnnounce();
        AnnouncesNativeTrackEntryAndExitIndependentlyOfTerrain();
        DefersBehindNavigationAndPublishesOnlyTheCurrentSurface();
        FallsBackToGenericSwampUnlessTheRicherCueWasDelivered();
        RetriesAnUtteranceTheHostCouldNotDeliver();
        AnnouncesOnlyAStableNamedAreaTransition();
        RidingAlongAnAreaBoundaryDoesNotChatter();
        DefersAreaTransitionsBehindNavigationSpeech();
        RetriedAreaSpeechStillRequestsOneZoneCue();
        NamesEveryNativeTerrainId();
        NamesEveryNativeRegionId();
    }

    private static void CrossingATransientTriangleSliverDoesNotAnnounce()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;
        _ = Confirm(tracker, new WorldMapSurfaceSample(0, false), ref now);

        for (var sample = 0; sample < 3; sample++)
        {
            Equal(
                null,
                tracker.Observe(new WorldMapSurfaceSample(1, false), now),
                "a sub-threshold forest sliver stays silent");
            now += TimeSpan.FromMilliseconds(100);
        }

        Equal(
            null,
            tracker.Observe(new WorldMapSurfaceSample(0, false), now),
            "returning from the sliver cancels it rather than announcing entry or exit");
        Equal(
            null,
            tracker.Observe(new WorldMapSurfaceSample(0, false), now + TimeSpan.FromMilliseconds(100)),
            "the established surface is not re-announced after the clip");
    }

    private static void RequiresBothEnoughSamplesAndEnoughElapsedTime()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var surface = new WorldMapSurfaceSample(1, false);

        Equal(null, tracker.Observe(surface, Epoch), "first sample starts confirmation");
        Equal(null, tracker.Observe(surface, Epoch + TimeSpan.FromMilliseconds(50)), "second fast sample");
        Equal(null, tracker.Observe(surface, Epoch + TimeSpan.FromMilliseconds(100)), "third fast sample");
        Equal(null, tracker.Observe(surface, Epoch + TimeSpan.FromMilliseconds(150)), "four fast samples are not enough time");
        Equal(
            "Entered forest.",
            tracker.Observe(surface, Epoch + TimeSpan.FromMilliseconds(300)),
            "confirmation requires the full elapsed-time floor");

        var sparse = new WorldMapTerrainAnnouncementTracker();
        Equal(null, sparse.Observe(surface, Epoch), "sparse first sample");
        Equal(
            null,
            sparse.Observe(surface, Epoch + TimeSpan.FromSeconds(1)),
            "elapsed time without enough samples is not confirmation");
    }

    private static void DecodesTheNativeTrackBitWithoutPollutingTheRegion()
    {
        var tracked = WorldMapDataLoader.DecodeTriangleMetadata(
            (ushort)(254 | (12 << 9) | (1 << 15)));
        Equal(254, tracked.TextureId, "native texture id");
        Equal(12, tracked.RegionId, "native region excludes the track flag");
        Equal(true, tracked.HasChocoboTracks, "native bit 15 marks chocobo tracks");

        var untracked = WorldMapDataLoader.DecodeTriangleMetadata(
            (ushort)(254 | (12 << 9) | (1 << 14)));
        Equal(12, untracked.RegionId, "unused bit 14 does not pollute the region");
        Equal(false, untracked.HasChocoboTracks, "a track texture without bit 15 is not invented as tracks");
    }

    private static void AnnouncesOnlyAStableTerrainTransition()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;

        Equal(
            "Entered grass.",
            Confirm(tracker, new WorldMapSurfaceSample(0, false), ref now),
            "the initial visible surface is named after confirmation");
        Equal(
            "Left grass. Entered forest.",
            Confirm(tracker, new WorldMapSurfaceSample(1, false), ref now),
            "a confirmed terrain change says both sides of the boundary");
        Equal(
            null,
            tracker.Observe(new WorldMapSurfaceSample(1, false), now),
            "remaining in the forest does not repeat");
    }

    private static void RidingAlongABoundaryDoesNotChatter()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;
        Equal(
            "Entered grass.",
            Confirm(tracker, new WorldMapSurfaceSample(0, false), ref now),
            "boundary test baseline");

        var announcements = new List<string>();
        for (var sample = 0; sample < 24; sample++)
        {
            var surface = sample % 2 == 0
                ? new WorldMapSurfaceSample(1, false)
                : new WorldMapSurfaceSample(0, false);
            var speech = tracker.Observe(surface, now);
            if (speech is not null)
            {
                announcements.Add(speech);
            }

            now += TimeSpan.FromMilliseconds(100);
        }

        Equal(0, announcements.Count, "oscillating across a triangle boundary stays silent");
        Equal(
            "Left grass. Entered forest.",
            Confirm(tracker, new WorldMapSurfaceSample(1, false), ref now),
            "one stable crossing produces one announcement");

        for (var sample = 0; sample < 8; sample++)
        {
            var speech = tracker.Observe(new WorldMapSurfaceSample(1, false), now);
            if (speech is not null)
            {
                announcements.Add(speech);
            }

            now += TimeSpan.FromMilliseconds(100);
        }

        Equal(0, announcements.Count, "the confirmed side is not re-announced");
    }

    private static void AnnouncesNativeTrackEntryAndExitIndependentlyOfTerrain()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;
        _ = Confirm(tracker, new WorldMapSurfaceSample(0, false), ref now);

        Equal(
            "On chocobo tracks.",
            Confirm(tracker, new WorldMapSurfaceSample(0, true), ref now),
            "native track entry");
        Equal(
            "Off chocobo tracks.",
            Confirm(tracker, new WorldMapSurfaceSample(0, false), ref now),
            "native track exit");
    }

    private static void DefersBehindNavigationAndPublishesOnlyTheCurrentSurface()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;
        Equal(
            null,
            Confirm(
                tracker,
                new WorldMapSurfaceSample(0, false),
                ref now,
                higherPrioritySpeech: true),
            "navigation speech holds the surface cue");

        now = Epoch + TimeSpan.FromMilliseconds(800);
        Equal(
            null,
            tracker.Observe(new WorldMapSurfaceSample(0, false), now),
            "the cue waits through the navigation quiet period");
        now = Epoch + TimeSpan.FromMilliseconds(900);
        Equal(
            "Entered grass.",
            tracker.Observe(new WorldMapSurfaceSample(0, false), now),
            "the held cue speaks after navigation has been quiet");
        tracker.AcknowledgeSpeech();

        Equal(
            null,
            Confirm(
                tracker,
                new WorldMapSurfaceSample(1, false),
                ref now,
                higherPrioritySpeech: true),
            "a later navigation update holds a terrain change");
        Equal(
            null,
            Confirm(
                tracker,
                new WorldMapSurfaceSample(25, true),
                ref now,
                higherPrioritySpeech: true),
            "another stable surface replaces the stale held change");
        now += TimeSpan.FromMilliseconds(600);
        Equal(
            "Left grass. Entered jungle. On chocobo tracks.",
            tracker.Observe(new WorldMapSurfaceSample(25, true), now),
            "only the current surface is published after priority speech");
        tracker.AcknowledgeSpeech();
    }

    private static void FallsBackToGenericSwampUnlessTheRicherCueWasDelivered()
    {
        var now = Epoch;
        var fallback = new WorldMapTerrainAnnouncementTracker();
        _ = Confirm(fallback, new WorldMapSurfaceSample(0, false), ref now);

        Equal(
            "Left grass. Entered swamp.",
            Confirm(fallback, new WorldMapSurfaceSample(7, false), ref now),
            "native terrain remains audible when the richer marsh cue is unavailable");

        now = Epoch;
        var covered = new WorldMapTerrainAnnouncementTracker();
        _ = Confirm(covered, new WorldMapSurfaceSample(0, false), ref now);
        var swamp = new WorldMapSurfaceSample(7, false);
        covered.RecordExternalTerrainSpeech(swamp, terrainIdHandled: 7);
        Equal(
            "Left grass.",
            Confirm(covered, swamp, ref now),
            "a delivered marsh cue suppresses only the duplicate swamp entry");

        var grass = new WorldMapSurfaceSample(0, false);
        covered.RecordExternalTerrainSpeech(grass, terrainIdHandled: 7);
        Equal(
            "Entered grass.",
            Confirm(covered, grass, ref now),
            "a delivered clear-of-marsh cue leaves the new surface audible");
    }

    private static void NamesEveryNativeTerrainId()
    {
        string[] expected =
        [
            "grass", "forest", "mountain", "sea", "river crossing", "river", "water", "swamp",
            "desert", "wasteland", "snow", "riverside", "cliff", "Corel Bridge", "Wutai Bridge",
            "unused terrain", "hillside", "beach", "submarine pen", "canyon", "mountain pass",
            "unknown terrain", "waterfall", "unused terrain", "Gold Saucer desert", "jungle",
            "deep sea", "Northern Cave", "Gold Saucer desert border", "bridgehead", "back entrance",
            "unused terrain"
        ];

        for (var terrainId = 0; terrainId < expected.Length; terrainId++)
        {
            Equal(
                expected[terrainId],
                WorldMapTerrainNames.GetName(terrainId),
                $"terrain {terrainId} authoritative spoken name");
        }

        Equal("chocobo tracks", WorldMapTerrainNames.TrackName, "track feature name");
    }

    private static void RetriesAnUtteranceTheHostCouldNotDeliver()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;
        var speech = Confirm(
            tracker,
            new WorldMapSurfaceSample(1, false),
            ref now,
            acknowledge: false);
        Equal("Entered forest.", speech, "first delivery attempt");
        Equal(
            "Entered forest.",
            tracker.Observe(new WorldMapSurfaceSample(1, false), now),
            "a failed host delivery is retried rather than lost");
        tracker.AcknowledgeSpeech();
        Equal(
            null,
            tracker.Observe(new WorldMapSurfaceSample(1, false), now + TimeSpan.FromMilliseconds(100)),
            "a successful host delivery is not repeated");
    }

    private static void AnnouncesOnlyAStableNamedAreaTransition()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;

        var entry = ConfirmAnnouncement(
            tracker,
            new WorldMapSurfaceSample(0, false, RegionId: 1),
            ref now);
        Equal(
            "Entered Grasslands Area. Entered grass.",
            entry?.Speech,
            "the first confirmed world area is named before its terrain");
        Equal(true, entry?.IncludesAreaTransition, "world-area entry requests the shared zone cue");

        var crossing = ConfirmAnnouncement(
            tracker,
            new WorldMapSurfaceSample(1, false, RegionId: 2),
            ref now);
        Equal(
            "Left Grasslands Area. Entered Junon Area. Left grass. Entered forest.",
            crossing?.Speech,
            "area transition precedes terrain transition in the combined utterance");
        Equal(true, crossing?.IncludesAreaTransition, "world-area crossing requests one shared zone cue");

        var terrainOnly = ConfirmAnnouncement(
            tracker,
            new WorldMapSurfaceSample(25, false, RegionId: 2),
            ref now);
        Equal(
            "Left forest. Entered jungle.",
            terrainOnly?.Speech,
            "terrain still speaks inside one named area");
        Equal(false, terrainOnly?.IncludesAreaTransition, "terrain-only speech does not play a zone cue");
    }

    private static void RidingAlongAnAreaBoundaryDoesNotChatter()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;
        _ = ConfirmAnnouncement(
            tracker,
            new WorldMapSurfaceSample(0, false, RegionId: 1),
            ref now);

        var announcements = new List<WorldMapSurfaceAnnouncement>();
        for (var sample = 0; sample < 24; sample++)
        {
            var surface = new WorldMapSurfaceSample(
                0,
                false,
                RegionId: sample % 2 == 0 ? 2 : 1);
            var announcement = tracker.ObserveAnnouncement(surface, now);
            if (announcement is { } value)
            {
                announcements.Add(value);
            }

            now += TimeSpan.FromMilliseconds(100);
        }

        Equal(0, announcements.Count, "oscillating on an area boundary stays silent");
        var stable = ConfirmAnnouncement(
            tracker,
            new WorldMapSurfaceSample(0, false, RegionId: 2),
            ref now);
        Equal(
            "Left Grasslands Area. Entered Junon Area.",
            stable?.Speech,
            "one stable area crossing produces one announcement");
        Equal(true, stable?.IncludesAreaTransition, "stable crossing requests one zone cue");
    }

    private static void DefersAreaTransitionsBehindNavigationSpeech()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;
        _ = ConfirmAnnouncement(
            tracker,
            new WorldMapSurfaceSample(0, false, RegionId: 1),
            ref now);

        Equal(
            null,
            ConfirmAnnouncement(
                tracker,
                new WorldMapSurfaceSample(0, false, RegionId: 2),
                ref now,
                higherPrioritySpeech: true),
            "navigation speech holds an area crossing");
        Equal(
            null,
            tracker.ObserveAnnouncement(
                new WorldMapSurfaceSample(0, false, RegionId: 2),
                now + TimeSpan.FromMilliseconds(499)),
            "area speech remains held before the quiet period ends");
        var delivered = tracker.ObserveAnnouncement(
            new WorldMapSurfaceSample(0, false, RegionId: 2),
            now + TimeSpan.FromMilliseconds(500));
        Equal(
            "Left Grasslands Area. Entered Junon Area.",
            delivered?.Speech,
            "area speech follows the navigation quiet period");
    }

    private static void RetriedAreaSpeechStillRequestsOneZoneCue()
    {
        var tracker = new WorldMapTerrainAnnouncementTracker();
        var now = Epoch;
        var first = ConfirmAnnouncement(
            tracker,
            new WorldMapSurfaceSample(0, false, RegionId: 2),
            ref now,
            acknowledge: false);
        var retry = tracker.ObserveAnnouncement(
            new WorldMapSurfaceSample(0, false, RegionId: 2),
            now);

        Equal(first, retry, "a rejected area utterance retries the same transition metadata");
        Equal(true, retry?.IncludesAreaTransition, "retry still identifies the pending zone cue");
        tracker.AcknowledgeSpeech();
        Equal(
            null,
            tracker.ObserveAnnouncement(
                new WorldMapSurfaceSample(0, false, RegionId: 2),
                now + TimeSpan.FromMilliseconds(100)),
            "an acknowledged area transition does not repeat");
    }

    private static void NamesEveryNativeRegionId()
    {
        string[] expected =
        [
            "Midgar Area", "Grasslands Area", "Junon Area", "Corel Area",
            "Gold Saucer Area", "Gongaga Area", "Cosmo Area", "Nibel Area",
            "Rocket Launch Pad Area", "Wutai Area", "Woodlands Area", "Icicle Area",
            "Mideel Area", "North Corel Area", "Cactus Island", "Goblin Island",
            "Round Island", "Sea", "Bottom of Sea", "Glacier"
        ];

        for (var regionId = 0; regionId < expected.Length; regionId++)
        {
            Equal(
                expected[regionId],
                WorldMapRegionNames.GetName(regionId),
                $"region {regionId} authoritative spoken name");
        }
    }

    private static string? Confirm(
        WorldMapTerrainAnnouncementTracker tracker,
        WorldMapSurfaceSample surface,
        ref DateTime now,
        bool higherPrioritySpeech = false,
        bool acknowledge = true)
    {
        string? speech = null;
        for (var sample = 0; sample < 4; sample++)
        {
            speech = tracker.Observe(surface, now, higherPrioritySpeech) ?? speech;
            now += TimeSpan.FromMilliseconds(100);
        }

        if (speech is not null && acknowledge)
        {
            tracker.AcknowledgeSpeech();
        }

        return speech;
    }

    private static WorldMapSurfaceAnnouncement? ConfirmAnnouncement(
        WorldMapTerrainAnnouncementTracker tracker,
        WorldMapSurfaceSample surface,
        ref DateTime now,
        bool higherPrioritySpeech = false,
        bool acknowledge = true)
    {
        WorldMapSurfaceAnnouncement? announcement = null;
        for (var sample = 0; sample < 4; sample++)
        {
            announcement = tracker.ObserveAnnouncement(surface, now, higherPrioritySpeech) ?? announcement;
            now += TimeSpan.FromMilliseconds(100);
        }

        if (announcement is not null && acknowledge)
        {
            tracker.AcknowledgeSpeech();
        }

        return announcement;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }
}
