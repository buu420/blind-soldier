using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal interface ISteam2026FieldObjectSpatialPlayback : IDisposable
{
    bool Play(FieldObjectCueKind kind, NavigationBeaconCue cue, float gain);

    void StopAll();
}

/// <summary>
/// Converts an already-coherent field position, control transform, and
/// authoritative object target list into the same clustered spatial cues used
/// by the x86 runtime. Memory observation remains outside this class.
/// </summary>
internal sealed class Steam2026FieldObjectSpatialCoordinator : IDisposable
{
    private readonly AccessibilityConfig config;
    private readonly FieldObjectProximityCueTracker tracker;
    private readonly ISteam2026FieldObjectSpatialPlayback playback;
    private readonly Action<string> log;
    private int? activeFieldId;
    private bool isReset = true;
    private bool isObservationSuspended;
    private DateTime lastObservationLogUtc = DateTime.MinValue;
    private string lastObservationDiagnostic = string.Empty;
    private int disposed;

    internal Steam2026FieldObjectSpatialCoordinator(
        AccessibilityConfig config,
        FieldObjectProximityCueTracker tracker,
        ISteam2026FieldObjectSpatialPlayback playback,
        Action<string> log)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal static Steam2026FieldObjectSpatialCoordinator Create(
        AccessibilityConfig config,
        string modDirectory,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);
        ArgumentNullException.ThrowIfNull(log);

        var tracker = new FieldObjectProximityCueTracker(
            config.FieldObjectCueInnerRangeUnits,
            config.FieldObjectCueOuterRangeUnits,
            config.FieldObjectCueClusterRadiusUnits,
            TimeSpan.FromMilliseconds(Math.Max(100, config.FieldObjectCueIntervalMs)));
        var playback = new Steam2026FieldObjectSpatialPlayback(
            modDirectory,
            config.FieldObjectCueVolumePercent,
            config.EnableFieldObjectProximityCues,
            log);
        log(
            $"Native Steam 2026 field object spatial cues initialized: " +
            $"enabled={config.EnableFieldObjectProximityCues}, " +
            $"inner={config.FieldObjectCueInnerRangeUnits}, " +
            $"outer={config.FieldObjectCueOuterRangeUnits}, " +
            $"cluster={config.FieldObjectCueClusterRadiusUnits}, " +
            $"interval={Math.Max(100, config.FieldObjectCueIntervalMs)}ms.");
        return new Steam2026FieldObjectSpatialCoordinator(
            config,
            tracker,
            playback,
            log);
    }

    /// <summary>
    /// Observes one caller-validated object domain. The supplied target list is
    /// authoritative; the coordinator never invents or substitutes targets.
    /// </summary>
    internal void Observe(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        IReadOnlyList<FieldNavigationTarget> authoritativeTargets,
        bool isHostForeground,
        bool isSuppressed,
        bool isReadCoherent,
        DateTime nowUtc,
        string readDiagnostic = "")
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(authoritativeTargets);

        if (!config.EnableFieldObjectProximityCues)
        {
            TransitionToReset("object proximity cues are disabled");
            return;
        }

        if (!isHostForeground)
        {
            TransitionToReset("host lost foreground");
            return;
        }

        if (isSuppressed)
        {
            LogObservationDiagnostic(
                $"suppressed: {(string.IsNullOrWhiteSpace(readDiagnostic) ? "field cue state" : readDiagnostic)}",
                nowUtc);
            TransitionToReset("field audible cues are suppressed");
            return;
        }

        if (!isReadCoherent)
        {
            LogObservationDiagnostic(
                $"unavailable: {(string.IsNullOrWhiteSpace(readDiagnostic) ? "checked object snapshot failed" : readDiagnostic)}",
                nowUtc);
            SuspendObservationForTransientReadFailure();
            return;
        }

        if (!FieldPositionReader.IsUsable(position))
        {
            TransitionToReset($"field module is unavailable: module={position.CurrentModule}");
            return;
        }

        if (activeFieldId is int previousFieldId && previousFieldId != position.FieldId)
        {
            playback.StopAll();
            tracker.Reset();
            log(
                $"Native Steam 2026 field object cues reset for field transition: " +
                $"from={previousFieldId}, to={position.FieldId}.");
        }

        activeFieldId = position.FieldId;
        isReset = false;
        isObservationSuspended = false;
        LogObservationDiagnostic(
            $"field={position.FieldId}, targets={authoritativeTargets.Count}, state=coherent",
            nowUtc);
        var proximityCues = tracker.Update(position, authoritativeTargets, nowUtc);
        foreach (var proximityCue in proximityCues)
        {
            var spatialCue = FieldObjectProximitySpatializer.CreateCue(
                position,
                proximityCue.Target,
                controlTransform);
            if (spatialCue is not { } cue)
            {
                continue;
            }

            try
            {
                if (playback.Play(proximityCue.Kind, cue, proximityCue.Gain))
                {
                    log(
                        $"Native Steam 2026 field object cue played: " +
                        $"kind={proximityCue.Kind}, target={proximityCue.Target.Label}, " +
                        $"position=({proximityCue.Target.X},{proximityCue.Target.Y},{proximityCue.Target.Z}), " +
                        $"distance={cue.DistanceUnits:0}, gain={proximityCue.Gain:0.000}, " +
                        $"cluster={proximityCue.ClusterKey}.");
                }
            }
            catch (Exception ex)
            {
                log(
                    $"Native Steam 2026 field object cue failed without fallback: " +
                    $"kind={proximityCue.Kind}, target={proximityCue.Target.Label}, error={ex.Message}");
            }
        }
    }

    internal void Reset(string reason)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ResetCore(reason, forceStop: true);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        tracker.Reset();
        playback.StopAll();
        playback.Dispose();
        activeFieldId = null;
        isReset = true;
        isObservationSuspended = false;
    }

    private void SuspendObservationForTransientReadFailure()
    {
        if (isReset || isObservationSuspended)
        {
            return;
        }

        isObservationSuspended = true;
        log(
            "Native Steam 2026 field object cue observation suspended for transient read failure; " +
            "active playback, field identity, and pulse cadence preserved.");
    }

    private void TransitionToReset(string reason)
    {
        if (isReset)
        {
            tracker.Reset();
            activeFieldId = null;
            isObservationSuspended = false;
            return;
        }

        ResetCore(reason, forceStop: false);
    }

    private void ResetCore(string reason, bool forceStop)
    {
        tracker.Reset();
        activeFieldId = null;
        if (forceStop || !isReset)
        {
            playback.StopAll();
        }

        if (!isReset)
        {
            log($"Native Steam 2026 field object cues stopped and reset: {reason}.");
        }

        isReset = true;
        isObservationSuspended = false;
    }

    private void LogObservationDiagnostic(string diagnostic, DateTime nowUtc)
    {
        if (!config.EnableFieldNavigationDiagnostics)
        {
            return;
        }

        if (!string.Equals(diagnostic, lastObservationDiagnostic, StringComparison.Ordinal) ||
            nowUtc - lastObservationLogUtc >= TimeSpan.FromSeconds(5))
        {
            log($"Native Steam 2026 field object observation: {diagnostic}.");
            lastObservationDiagnostic = diagnostic;
            lastObservationLogUtc = nowUtc;
        }
    }
}

internal sealed class Steam2026FieldObjectSpatialPlayback : ISteam2026FieldObjectSpatialPlayback
{
    private readonly Dictionary<FieldObjectCueKind, NavigationBeaconPlayer> players = new();
    private int disposed;

    internal Steam2026FieldObjectSpatialPlayback(
        string modDirectory,
        int volumePercent,
        bool enabled,
        Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);
        ArgumentNullException.ThrowIfNull(log);
        if (!enabled)
        {
            return;
        }

        foreach (var kind in new[]
                 {
                     FieldObjectCueKind.Materia,
                     FieldObjectCueKind.Chest,
                     FieldObjectCueKind.Item
                 })
        {
            var fileName = ResolveSoundFileName(kind);
            if (fileName is null)
            {
                continue;
            }

            var path = Path.GetFullPath(
                Path.Combine(modDirectory, "Assets", "navigation", fileName));
            players.Add(kind, new NavigationBeaconPlayer(path, volumePercent, log));
        }
    }

    internal static string? ResolveSoundFileName(FieldObjectCueKind kind) => kind switch
    {
        FieldObjectCueKind.Materia => "object_materia_190_pitch70.wav",
        FieldObjectCueKind.Chest => "object_chest_253_pitch70.wav",
        FieldObjectCueKind.Item => "object_item_357_pitch70.wav",
        _ => null
    };

    public bool Play(FieldObjectCueKind kind, NavigationBeaconCue cue, float gain)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        return players.TryGetValue(kind, out var player)
            && player.Play(cue, gain);
    }

    public void StopAll()
    {
        if (disposed != 0)
        {
            return;
        }

        foreach (var player in players.Values)
        {
            player.StopAll();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        foreach (var player in players.Values)
        {
            player.Dispose();
        }

        players.Clear();
    }
}
