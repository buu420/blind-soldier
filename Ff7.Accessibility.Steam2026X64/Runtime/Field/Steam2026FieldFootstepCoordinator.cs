using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal readonly record struct Steam2026FootstepSelection(
    string Path,
    string Reason,
    string TrackName = "",
    int SoundId = 0,
    Steam2026FootstepMappingScope MappingScope = Steam2026FootstepMappingScope.ConfiguredFallback);

internal interface ISteam2026FootstepPlayback : IDisposable
{
    bool Play(string path, string reason);
}

/// <summary>
/// Converts coherent translated field observations into the same explicit
/// Cosmo footstep selection and movement cadence used by the x86 runtime.
/// It never guesses a sound for an unmapped field or triangle.
/// </summary>
internal sealed class Steam2026FieldFootstepCoordinator : IDisposable
{
    private readonly AccessibilityConfig config;
    private readonly FieldFootstepTracker tracker;
    private readonly Func<FieldPositionSnapshot, Steam2026FootstepSelection?> selectFootstep;
    private readonly ISteam2026FootstepPlayback playback;
    private readonly Action<string> log;
    private readonly Steam2026FieldFootstepNavigationProbe? probe;
    private DateTime lastScanUtc = DateTime.MinValue;
    private string? lastSuppressionKey;
    private int disposed;

    internal Steam2026FieldFootstepCoordinator(
        AccessibilityConfig config,
        FieldFootstepTracker tracker,
        Func<FieldPositionSnapshot, Steam2026FootstepSelection?> selectFootstep,
        ISteam2026FootstepPlayback playback,
        Action<string> log,
        Steam2026FieldFootstepNavigationProbe? probe = null)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        this.selectFootstep = selectFootstep ?? throw new ArgumentNullException(nameof(selectFootstep));
        this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.probe = probe;
    }

    internal static Steam2026FieldFootstepCoordinator Create(
        AccessibilityConfig config,
        string modDirectory,
        string gameWorkingDirectory,
        Action<string> log,
        Steam2026FieldFootstepNavigationProbe? probe = null)
    {
        var language = Ff7GameLanguageDetector.Detect(
            gameWorkingDirectory,
            config.GameLanguage,
            log: log);
        return Create(config, modDirectory, gameWorkingDirectory, language, log, probe);
    }

    internal static Steam2026FieldFootstepCoordinator Create(
        AccessibilityConfig config,
        string modDirectory,
        string gameWorkingDirectory,
        Ff7GameLanguageContext language,
        Action<string> log,
        Steam2026FieldFootstepNavigationProbe? probe = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameWorkingDirectory);
        ArgumentNullException.ThrowIfNull(log);

        var defaultPath = ResolveModPath(
            modDirectory,
            config.FieldFootstepSoundPath,
            @"Assets\footsteps\selected_subway_step.ogg");
        var productionPlayback = new Steam2026FootstepPlayback(
            defaultPath,
            config.FieldFootstepVolumePercent,
            log);
        var tracker = new FieldFootstepTracker(
            TimeSpan.FromMilliseconds(Math.Max(80, config.FieldFootstepWalkIntervalMs)),
            TimeSpan.FromMilliseconds(Math.Max(80, config.FieldFootstepRunIntervalMs)),
            Math.Max(1, config.FieldFootstepMeasuredRunSpeedUnitsPerSecond));

        Func<FieldPositionSnapshot, Steam2026FootstepSelection?> selector;
        if (!config.UseCosmoFootstepSounds)
        {
            selector = _ => new Steam2026FootstepSelection(defaultPath, "configured fallback");
            log($"Native Steam 2026 footsteps use configured sound: {defaultPath}");
        }
        else
        {
            selector = CreateCosmoSelector(
                config,
                modDirectory,
                gameWorkingDirectory,
                language,
                log);
        }

        return new Steam2026FieldFootstepCoordinator(
            config,
            tracker,
            selector,
            productionPlayback,
            log,
            probe);
    }

    internal void Observe(
        RuntimeFrameObservation frame,
        bool isHostForeground,
        DateTime nowUtc,
        long workerCycle = 0)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!config.EnableFieldFootstepFeedback
            || !isHostForeground
            || !frame.Lifecycle.IsForeground
            || frame.Lifecycle.IsShuttingDown
            || frame.Field.Kind == RuntimeDomainUpdateKind.Closed)
        {
            Reset();
            return;
        }

        if (frame.Field.Kind != RuntimeDomainUpdateKind.Present
            || frame.Field.Value is not { } field)
        {
            // This pump publishes a field observation on every successful
            // read. Unchanged therefore means the checked snapshot was not
            // coherent; re-prime instead of turning recovery into a false
            // teleport step.
            tracker.Reset();
            probe?.ResetCorrelation();
            return;
        }

        var scanInterval = TimeSpan.FromMilliseconds(
            Math.Max(30, config.FieldFootstepScanIntervalMs));
        if (lastScanUtc != DateTime.MinValue && nowUtc - lastScanUtc < scanInterval)
        {
            return;
        }

        lastScanUtc = nowUtc;
        if (!TryCreatePosition(field, out var position))
        {
            tracker.Reset();
            probe?.ResetCorrelation();
            return;
        }

        var distanceUnitsPerFootstep = FieldNavigationDistanceCalibration.Resolve(
            position.FieldId,
            config.FieldNavigationSpeechDistanceUnitsPerCount);
        var footstepTriggered = tracker.Observe(
            position,
            nowUtc,
            isRunning: false,
            distanceUnitsPerFootstep);
        var distanceObservation = probe?.ObserveMovement(
            position,
            nowUtc,
            isHostForeground && frame.Lifecycle.IsForeground,
            field.HasControl,
            tracker.LastCadence,
            footstepTriggered) ?? default;
        if (!footstepTriggered)
        {
            return;
        }

        var selection = selectFootstep(position);
        if (selection is not { Path.Length: > 0 } selected)
        {
            PublishProbeFootstep(
                workerCycle,
                nowUtc,
                position,
                field.HasControl,
                distanceObservation,
                default,
                Steam2026FootstepMappingScope.Unmapped,
                playbackSucceeded: false);
            LogSuppressionOnce(
                $"unmapped:{position.FieldId}:{position.TriangleId}",
                $"Native Steam 2026 footstep suppressed: no explicit sound mapping for " +
                $"field={position.FieldId}, triangle={position.TriangleId}.");
            return;
        }

        lastSuppressionKey = null;
        var playbackSucceeded = playback.Play(selected.Path, selected.Reason);
        PublishProbeFootstep(
            workerCycle,
            nowUtc,
            position,
            field.HasControl,
            distanceObservation,
            selected,
            selected.MappingScope,
            playbackSucceeded);
        if (playbackSucceeded)
        {
            log(
                $"Native Steam 2026 footstep played: field={position.FieldId}, " +
                $"triangle={position.TriangleId}, cadence={tracker.LastCadence}, " +
                $"source={selected.Reason}, path={selected.Path}.");
        }
    }

    internal void Reset()
    {
        tracker.Reset();
        probe?.ResetCorrelation();
        lastScanUtc = DateTime.MinValue;
        lastSuppressionKey = null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        playback.Dispose();
    }

    private static Func<FieldPositionSnapshot, Steam2026FootstepSelection?> CreateCosmoSelector(
        AccessibilityConfig config,
        string modDirectory,
        string gameWorkingDirectory,
        Ff7GameLanguageContext language,
        Action<string> log)
    {
        try
        {
            var soundDirectory = ResolveModPath(
                modDirectory,
                config.CosmoFootstepSoundDirectory,
                @"Assets\footsteps\cosmo");
            var configPath = Path.Combine(soundDirectory, "config.toml");
            var cosmoConfig = CosmoFootstepConfig.Load(configPath);
            if (cosmoConfig.TrackCount == 0)
            {
                log($"Native Steam 2026 Cosmo footstep config has no usable tracks: {configPath}");
                return _ => null;
            }

            var fieldData = new FlevelDataSource(gameWorkingDirectory, language);
            if (!fieldData.IsUsable || fieldData.FieldNames.Count == 0)
            {
                log($"Native Steam 2026 Cosmo footsteps lack authoritative field names: {fieldData.Diagnostic}");
                return _ => null;
            }

            var sequencer = new CosmoFootstepSequencer(
                cosmoConfig,
                fieldData.FieldNames,
                soundDirectory);
            log(
                $"Native Steam 2026 Cosmo footsteps ready: tracks={cosmoConfig.TrackCount}, " +
                $"fields={fieldData.FieldNames.Count}, source={fieldData.Diagnostic}.");
            return position =>
            {
                if (!sequencer.TrySelectNext(position, out var selected) || selected.IsSilent)
                {
                    return null;
                }

                return new Steam2026FootstepSelection(
                    selected.Path,
                    $"Cosmo {selected.TrackName}/{selected.SoundId}",
                    selected.TrackName,
                    selected.SoundId,
                    selected.TrackName.EndsWith(
                        $"_{position.TriangleId}_159",
                        StringComparison.OrdinalIgnoreCase)
                        ? Steam2026FootstepMappingScope.Triangle
                        : Steam2026FootstepMappingScope.Field);
            };
        }
        catch (Exception ex)
        {
            log($"Native Steam 2026 Cosmo footsteps remain silent: {ex.Message}");
            return _ => null;
        }
    }

    internal static bool TryCreatePosition(
        FieldFrameObservation field,
        out FieldPositionSnapshot position)
    {
        position = default;
        if (field.FieldId < 0
            || field.PlayerModelId < 0
            || field.TriangleId is < 0 or > ushort.MaxValue
            || !float.IsFinite(field.X)
            || !float.IsFinite(field.Y)
            || !float.IsFinite(field.Z)
            || field.X is < int.MinValue or > int.MaxValue
            || field.Y is < int.MinValue or > int.MaxValue
            || field.Z is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            field.FieldId,
            field.PlayerModelId,
            checked((int)field.X),
            checked((int)field.Y),
            checked((int)field.Z),
            checked((ushort)field.TriangleId),
            Direction: 0);
        return true;
    }

    private static string ResolveModPath(
        string modDirectory,
        string? configuredPath,
        string fallbackRelativePath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? fallbackRelativePath
            : configuredPath;
        return Path.GetFullPath(
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(modDirectory, path));
    }

    private void LogSuppressionOnce(string key, string message)
    {
        if (string.Equals(key, lastSuppressionKey, StringComparison.Ordinal))
        {
            return;
        }

        lastSuppressionKey = key;
        log(message);
    }

    private void PublishProbeFootstep(
        long workerCycle,
        DateTime nowUtc,
        FieldPositionSnapshot position,
        bool hasControl,
        FieldFootstepDistanceProbeObservation distance,
        Steam2026FootstepSelection selection,
        Steam2026FootstepMappingScope mappingScope,
        bool playbackSucceeded)
    {
        probe?.PublishFootstep(
            new Steam2026FootstepProbeSample(
                workerCycle,
                nowUtc,
                position,
                hasControl,
                tracker.LastCadence,
                distance,
                selection.TrackName,
                selection.SoundId,
                string.IsNullOrWhiteSpace(selection.Path)
                    ? string.Empty
                    : Path.GetFileName(selection.Path),
                mappingScope,
                selection.Reason,
                playbackSucceeded));
    }
}

internal sealed class Steam2026FootstepPlayback : ISteam2026FootstepPlayback
{
    private readonly FootstepSoundPlayer player;

    internal Steam2026FootstepPlayback(
        string defaultPath,
        int volumePercent,
        Action<string> log)
    {
        player = new FootstepSoundPlayer(defaultPath, volumePercent, log);
    }

    public bool Play(string path, string reason) => player.Play(reason, path);

    public void Dispose() => player.Dispose();
}
