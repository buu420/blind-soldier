namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The visible surface beneath the world-map player, resolved from the native
/// triangle rather than inferred from its texture.
/// </summary>
public readonly record struct WorldMapSurfaceSample(
    int TerrainId,
    bool HasChocoboTracks,
    int? RegionId = null);

/// <summary>
/// One debounced world-map surface announcement and the transition metadata
/// hosts need to play the shared zone cue without mistaking terrain changes for
/// named-area changes.
/// </summary>
public readonly record struct WorldMapSurfaceAnnouncement(
    string Speech,
    bool IncludesAreaTransition,
    int? PreviousRegionId,
    int? CurrentRegionId);

/// <summary>
/// Spoken names for FFVII's 32 native world-map ground types.
/// </summary>
public static class WorldMapTerrainNames
{
    public const string TrackName = "chocobo tracks";

    private static readonly string[] Names =
    [
        "grass",
        "forest",
        "mountain",
        "sea",
        "river crossing",
        "river",
        "water",
        "swamp",
        "desert",
        "wasteland",
        "snow",
        "riverside",
        "cliff",
        "Corel Bridge",
        "Wutai Bridge",
        "unused terrain",
        "hillside",
        "beach",
        "submarine pen",
        "canyon",
        "mountain pass",
        "unknown terrain",
        "waterfall",
        "unused terrain",
        "Gold Saucer desert",
        "jungle",
        "deep sea",
        "Northern Cave",
        "Gold Saucer desert border",
        "bridgehead",
        "back entrance",
        "unused terrain"
    ];

    public static string GetName(int terrainId) =>
        terrainId is >= 0 and < 32
            ? Names[terrainId]
            : throw new ArgumentOutOfRangeException(nameof(terrainId), terrainId, "Terrain id must be 0 through 31.");
}

/// <summary>
/// The names FFVII displays for its native world-map regions.
/// </summary>
public static class WorldMapRegionNames
{
    private static readonly string[] Names =
    [
        "Midgar Area",
        "Grasslands Area",
        "Junon Area",
        "Corel Area",
        "Gold Saucer Area",
        "Gongaga Area",
        "Cosmo Area",
        "Nibel Area",
        "Rocket Launch Pad Area",
        "Wutai Area",
        "Woodlands Area",
        "Icicle Area",
        "Mideel Area",
        "North Corel Area",
        "Cactus Island",
        "Goblin Island",
        "Round Island",
        "Sea",
        "Bottom of Sea",
        "Glacier"
    ];

    public static int Count => Names.Length;

    public static string GetName(int regionId) =>
        regionId >= 0 && regionId < Names.Length
            ? Names[regionId]
            : throw new ArgumentOutOfRangeException(
                nameof(regionId),
                regionId,
                $"Named world-map region id must be 0 through {Names.Length - 1}.");

    public static bool TryGetName(int? regionId, out string name)
    {
        if (regionId is { } value && value >= 0 && value < Names.Length)
        {
            name = Names[value];
            return true;
        }

        name = string.Empty;
        return false;
    }

    public static string FormatCueReason(int? previousRegionId, int? currentRegionId)
    {
        var previous = TryGetName(previousRegionId, out var previousName)
            ? previousName
            : "unknown";
        var current = TryGetName(currentRegionId, out var currentName)
            ? currentName
            : "unknown";
        return $"world area={previous}->{current}";
    }
}

/// <summary>
/// Debounces native world-map terrain and chocobo-track transitions before
/// exposing them to speech. Navigation speech and its accessible progress bar
/// can reserve the speech lane; a delayed surface cue is recalculated from the
/// last thing actually spoken to the latest confirmed surface, so stale terrain
/// changes never drain from a queue after the player has already moved on.
/// </summary>
public sealed class WorldMapTerrainAnnouncementTracker
{
    internal const int RequiredStableSamples = 4;
    internal static readonly TimeSpan RequiredStableDuration = TimeSpan.FromMilliseconds(300);
    internal static readonly TimeSpan HigherPriorityQuietPeriod = TimeSpan.FromMilliseconds(600);

    private WorldMapSurfaceSample? stableSurface;
    private WorldMapSurfaceSample? candidateSurface;
    private WorldMapSurfaceSample? announcedSurface;
    private WorldMapSurfaceSample? pendingDeliverySurface;
    private ExternalTerrainCoverage? externalTerrainCoverage;
    private WorldMapSurfaceAnnouncement? pendingAnnouncement;
    private int candidateSamples;
    private DateTime candidateSince;
    private DateTime speechBlockedUntil;

    public string LastDiagnostic { get; private set; } = "not observed";

    public string? Observe(
        WorldMapSurfaceSample surface,
        DateTime observedAt,
        bool higherPrioritySpeech = false) =>
        ObserveAnnouncement(surface, observedAt, higherPrioritySpeech)?.Speech;

    public WorldMapSurfaceAnnouncement? ObserveAnnouncement(
        WorldMapSurfaceSample surface,
        DateTime observedAt,
        bool higherPrioritySpeech = false)
    {
        if (surface.TerrainId is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surface),
                surface.TerrainId,
                "World-map terrain id must be 0 through 31.");
        }

        if (surface.RegionId is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surface),
                surface.RegionId,
                "World-map region id must be 0 through 31 when available.");
        }

        var now = observedAt == default ? DateTime.UtcNow : observedAt;
        if (externalTerrainCoverage is { } coverage && coverage.Surface != surface)
        {
            // A rich cue delivered for a one-triangle clip must not suppress a
            // later, real crossing onto the same terrain.
            externalTerrainCoverage = null;
        }

        if (higherPrioritySpeech)
        {
            HoldForHigherPriority(now);
        }

        if (stableSurface is { } stable && surface == stable)
        {
            candidateSurface = null;
            candidateSamples = 0;
        }
        else if (candidateSurface != surface)
        {
            candidateSurface = surface;
            candidateSamples = 1;
            candidateSince = now;
            LastDiagnostic =
                $"candidate region={surface.RegionId?.ToString() ?? "unavailable"}, " +
                $"terrain={surface.TerrainId}, tracks={surface.HasChocoboTracks}, samples=1";
        }
        else
        {
            candidateSamples++;
            if (candidateSamples >= RequiredStableSamples &&
                now - candidateSince >= RequiredStableDuration)
            {
                var confirmedSamples = candidateSamples;
                stableSurface = surface;
                candidateSurface = null;
                candidateSamples = 0;
                LastDiagnostic =
                    $"confirmed region={surface.RegionId?.ToString() ?? "unavailable"}, " +
                    $"terrain={surface.TerrainId}, tracks={surface.HasChocoboTracks} " +
                    $"after {confirmedSamples} samples and {(int)(now - candidateSince).TotalMilliseconds} ms";
            }
        }

        if (stableSurface is not { } current)
        {
            return null;
        }

        if (candidateSurface is not null || announcedSurface == current)
        {
            if (announcedSurface == current)
            {
                pendingDeliverySurface = null;
                pendingAnnouncement = null;
            }

            return null;
        }

        if (now < speechBlockedUntil)
        {
            LastDiagnostic =
                $"confirmed region={current.RegionId?.ToString() ?? "unavailable"}, " +
                $"terrain={current.TerrainId}, tracks={current.HasChocoboTracks}; " +
                $"speech deferred until {speechBlockedUntil:O} for navigation priority";
            return null;
        }

        if (pendingDeliverySurface == current && pendingAnnouncement is { } pending)
        {
            return pending;
        }

        int? externallyHandledTerrainId =
            externalTerrainCoverage is { } external && external.Surface == current
                ? external.TerrainIdHandled
                : null;
        var announcement = FormatTransition(
            announcedSurface,
            current,
            externallyHandledTerrainId);
        pendingDeliverySurface = current;
        pendingAnnouncement = announcement;
        if (announcement is null)
        {
            announcedSurface = current;
            pendingDeliverySurface = null;
            externalTerrainCoverage = null;
        }

        LastDiagnostic = announcement is null
            ? $"region={current.RegionId?.ToString() ?? "unavailable"}, terrain={current.TerrainId}, " +
              $"tracks={current.HasChocoboTracks} already covered by richer speech"
            : $"region={current.RegionId?.ToString() ?? "unavailable"}, terrain={current.TerrainId}, " +
              $"tracks={current.HasChocoboTracks} ready for speech: {announcement.Value.Speech}";
        return announcement;
    }

    /// <summary>
    /// Records that the host successfully delivered a richer cue for one side
    /// of the currently observed terrain transition. Failed or unavailable
    /// rich speech must not call this method; the native generic cue then
    /// remains as the no-silence fallback.
    /// </summary>
    public void RecordExternalTerrainSpeech(
        WorldMapSurfaceSample observedSurface,
        int terrainIdHandled)
    {
        if (observedSurface.TerrainId is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(observedSurface));
        }

        if (terrainIdHandled is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(terrainIdHandled));
        }

        externalTerrainCoverage = new ExternalTerrainCoverage(
            observedSurface,
            terrainIdHandled);
        LastDiagnostic =
            $"terrain {terrainIdHandled} covered by richer speech for " +
            $"surface={observedSurface.TerrainId}, tracks={observedSurface.HasChocoboTracks}";
    }

    /// <summary>
    /// Commits the last returned utterance only after the host speech path has
    /// accepted it. A failed x86 delivery or an x64 exception therefore leaves
    /// the current visible surface pending instead of losing it silently.
    /// </summary>
    public void AcknowledgeSpeech()
    {
        if (pendingDeliverySurface is not { } delivered || pendingAnnouncement is null)
        {
            return;
        }

        announcedSurface = delivered;
        pendingDeliverySurface = null;
        pendingAnnouncement = null;
        if (externalTerrainCoverage is { } coverage && coverage.Surface == delivered)
        {
            externalTerrainCoverage = null;
        }
        LastDiagnostic =
            $"delivered region={delivered.RegionId?.ToString() ?? "unavailable"}, " +
            $"terrain={delivered.TerrainId}, tracks={delivered.HasChocoboTracks}";
    }

    public void ObserveUnavailable(DateTime observedAt, bool higherPrioritySpeech = false)
    {
        var now = observedAt == default ? DateTime.UtcNow : observedAt;
        if (higherPrioritySpeech)
        {
            HoldForHigherPriority(now);
        }

        candidateSurface = null;
        candidateSamples = 0;
        LastDiagnostic = "native player triangle unavailable; candidate confirmation cleared";
    }

    public void Reset()
    {
        stableSurface = null;
        candidateSurface = null;
        announcedSurface = null;
        pendingDeliverySurface = null;
        externalTerrainCoverage = null;
        pendingAnnouncement = null;
        candidateSamples = 0;
        candidateSince = default;
        speechBlockedUntil = default;
        LastDiagnostic = "reset";
    }

    private void HoldForHigherPriority(DateTime now)
    {
        var blockedUntil = now + HigherPriorityQuietPeriod;
        if (blockedUntil > speechBlockedUntil)
        {
            speechBlockedUntil = blockedUntil;
        }
    }

    private static WorldMapSurfaceAnnouncement? FormatTransition(
        WorldMapSurfaceSample? previous,
        WorldMapSurfaceSample current,
        int? externallyHandledTerrainId)
    {
        var parts = new List<string>(5);
        var includesAreaTransition = false;
        if (previous is null)
        {
            if (WorldMapRegionNames.TryGetName(current.RegionId, out var currentArea))
            {
                parts.Add($"Entered {currentArea}.");
                includesAreaTransition = true;
            }
        }
        else if (previous.Value.RegionId != current.RegionId)
        {
            if (WorldMapRegionNames.TryGetName(previous.Value.RegionId, out var previousArea))
            {
                parts.Add($"Left {previousArea}.");
                includesAreaTransition = true;
            }

            if (WorldMapRegionNames.TryGetName(current.RegionId, out var currentArea))
            {
                parts.Add($"Entered {currentArea}.");
                includesAreaTransition = true;
            }
        }

        if (previous is null)
        {
            if (current.TerrainId != externallyHandledTerrainId)
            {
                parts.Add($"Entered {WorldMapTerrainNames.GetName(current.TerrainId)}.");
            }
        }
        else if (previous.Value.TerrainId != current.TerrainId)
        {
            var priorTerrain = previous.Value.TerrainId;
            if (priorTerrain != externallyHandledTerrainId)
            {
                parts.Add($"Left {WorldMapTerrainNames.GetName(priorTerrain)}.");
            }

            if (current.TerrainId != externallyHandledTerrainId)
            {
                parts.Add($"Entered {WorldMapTerrainNames.GetName(current.TerrainId)}.");
            }
        }

        if (previous is null)
        {
            if (current.HasChocoboTracks)
            {
                parts.Add("On chocobo tracks.");
            }
        }
        else if (previous.Value.HasChocoboTracks != current.HasChocoboTracks)
        {
            parts.Add(current.HasChocoboTracks
                ? "On chocobo tracks."
                : "Off chocobo tracks.");
        }

        return parts.Count == 0
            ? null
            : new WorldMapSurfaceAnnouncement(
                string.Join(" ", parts),
                includesAreaTransition,
                previous?.RegionId,
                current.RegionId);
    }

    private readonly record struct ExternalTerrainCoverage(
        WorldMapSurfaceSample Surface,
        int TerrainIdHandled);
}
