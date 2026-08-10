using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

namespace Ff7.Accessibility.Steam2026X64.Runtime.World;

/// <summary>
/// x64 host adapter for the shared native world-map implementation. All guest
/// reads go through the exact-fingerprint translated x86 address space; the
/// resulting pointer-free snapshots then use the same parser, targets, route
/// planner, progress bar, speech, and Cosmo footsteps as the x86 runtime.
/// </summary>
internal sealed class Steam2026WorldMapAccessibilityCoordinator : IDisposable
{
    private readonly AccessibilityConfig config;
    private readonly Steam2026ForegroundInputAdapter foregroundInput;
    private readonly WorldMapStateReader stateReader;
    private readonly WorldMapEntityReader entityReader;
    private readonly Dictionary<(int MapType, int ProgressStage), WorldMapRuntimeContext> runtimes = [];
    private readonly NativeFieldNavigationProgressBar? progressBar;
    private readonly IntervalFieldNavigationProgressSink? progressSink;
    private readonly NavigationBeaconPlayer? beaconPlayer;
    private readonly FootstepSoundPlayer footstepPlayer;
    private readonly CosmoFootstepSequencer? cosmoFootsteps;
    private readonly NavigationAutoWalkController autoWalk;
    private readonly Action<string, bool> speak;
    private readonly Action<string> log;
    private DateTime nextScanUtc = DateTime.MinValue;
    private string lastStateDiagnostic = string.Empty;
    private string lastEntityDiagnostic = string.Empty;
    private string lastNavigationDiagnostic = string.Empty;
    private string lastFootstepDiagnostic = string.Empty;
    private string lastSuppressionKey = string.Empty;
    private string lastAutoWalkFailure = string.Empty;
    private bool wasActive;
    private int disposed;

    internal Steam2026WorldMapAccessibilityCoordinator(
        AccessibilityConfig config,
        ILegacyAddressSpace addressSpace,
        Steam2026ForegroundInputAdapter foregroundInput,
        string gameWorkingDirectory,
        string modDirectory,
        Action<string, bool> speak,
        Action<string> log,
        NavigationProgressController? progressController = null,
        NavigationAutoWalkController? autoWalk = null)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(addressSpace);
        this.foregroundInput = foregroundInput ?? throw new ArgumentNullException(nameof(foregroundInput));
        ArgumentException.ThrowIfNullOrWhiteSpace(gameWorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);
        this.speak = speak ?? throw new ArgumentNullException(nameof(speak));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.autoWalk = autoWalk ?? NavigationAutoWalkController.CreateCurrentProcess();

        stateReader = new WorldMapStateReader(addressSpace);
        entityReader = new WorldMapEntityReader(addressSpace);
        progressBar = config.EnableWorldMapNavigationAssistant
            ? new NativeFieldNavigationProgressBar(log)
            : null;
        progressSink = progressBar is null
            ? null
            : new IntervalFieldNavigationProgressSink(
                progressBar,
                progressController ?? new NavigationProgressController(
                    config.EnableNavigationProgressIndicators,
                    config.NavigationProgressIntervalPercent));
        beaconPlayer = config.EnableWorldMapNavigationAssistant
            ? new NavigationBeaconPlayer(
                ResolveModPath(
                    modDirectory,
                    config.WorldMapNavigationBeaconSoundPath,
                    @"Assets\navigation\navigation_beacon_214_remix.wav"),
                config.WorldMapNavigationBeaconVolumePercent,
                log)
            : null;
        footstepPlayer = new FootstepSoundPlayer(
            ResolveModPath(
                modDirectory,
                config.FieldFootstepSoundPath,
                @"Assets\footsteps\selected_subway_step.ogg"),
            config.FieldFootstepVolumePercent,
            log);
        cosmoFootsteps = TryCreateCosmoFootsteps(config, modDirectory, log);

        var coordinatePath = Path.Combine(
            modDirectory,
            "Assets",
            "world",
            "field-id-to-world-map-coords.json");
        var menuNamePath = Path.Combine(
            modDirectory,
            "Assets",
            "world",
            "wm-field-menu-names.txt");
        if (!File.Exists(coordinatePath) || !File.Exists(menuNamePath))
        {
            throw new FileNotFoundException(
                "Installed world-map location metadata is incomplete.",
                !File.Exists(coordinatePath) ? coordinatePath : menuNamePath);
        }

        foreach (var mapType in new[] { 0, 2, 3 })
        {
            var mapPath = Path.Combine(gameWorkingDirectory, "data", "wm", $"wm{mapType}.map");
            if (!File.Exists(mapPath))
            {
                log($"Native Steam 2026 world-map type {mapType} is unavailable: {mapPath}");
                continue;
            }

            try
            {
                var mapBytes = File.ReadAllBytes(mapPath);
                var stages = mapType == 0 ? Enumerable.Range(0, 5) : [0];
                foreach (var progressStage in stages)
                {
                    var map = WorldMapDataLoader.Parse(
                        mapBytes,
                        mapType,
                        progressStage,
                        mapPath);
                    var catalog = WorldMapTargetCatalog.Load(map, coordinatePath, menuNamePath);
                    runtimes.Add(
                        (mapType, progressStage),
                        new WorldMapRuntimeContext(
                            map,
                            catalog,
                            progressSink,
                            Math.Max(1, config.WorldMapNavigationSpeechDistanceUnitsPerCount),
                            TimeSpan.FromMilliseconds(Math.Max(0, config.WorldMapNavigationSpeechIntervalMs)),
                            TimeSpan.FromMilliseconds(Math.Max(0, config.WorldMapNavigationBeaconIntervalMs)),
                            TimeSpan.FromMilliseconds(Math.Max(80, config.WorldMapFootstepWalkIntervalMs)),
                            TimeSpan.FromMilliseconds(Math.Max(80, config.WorldMapFootstepChocoboIntervalMs))));
                    log(
                        $"Native Steam 2026 world-map type {mapType}, progress stage {progressStage} ready: " +
                        $"triangles={map.Triangles.Count}, locations={catalog.Locations.Count}, " +
                        $"chocoboTracks={catalog.ChocoboTracks.Count}.");
                }
            }
            catch (Exception ex)
            {
                log($"Native Steam 2026 world-map type {mapType} failed closed: {ex.Message}");
            }
        }

        if (runtimes.Count == 0)
        {
            throw new InvalidOperationException("No native world-map geometry could be loaded.");
        }

        log(
            "Native Steam 2026 world-map accessibility uses the shared x86 controller: " +
            "Locations, Story, Transportation, Events, Chocobo Tracks; " +
            "keys=U,O,J,L,K,I,P auto walk; live native entities; reversible accessible route progress.");
    }

    internal void Observe(RuntimeFrameObservation frame, DateTime nowUtc)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Lifecycle.ModuleId != WorldMapStateReader.WorldModule)
        {
            if (WorldMapNavigationLifecycle.IsCombatInterruptionModule(frame.Lifecycle.ModuleId))
            {
                foreach (var context in runtimes.Values)
                {
                    context.Footsteps.Reset();
                    context.Navigation.PauseForCombat(
                        $"native combat module {frame.Lifecycle.ModuleId}");
                }

                beaconPlayer?.StopAll();
                autoWalk.Suspend();
                return;
            }

            if (wasActive)
            {
                Reset($"module changed to {frame.Lifecycle.ModuleId}");
            }

            return;
        }

        // Own and sample all six shared navigation keys on every world frame,
        // including background frames, so refocus cannot create delayed edges.
        var actions = Steam2026FieldNavigationKeyRouter.ReadActions(
            foregroundInput.ObserveRisingEdge);
        var autoWalkToggleRequested = NavigationAutoWalkKeyRouter.ObserveToggle(
            foregroundInput.ObserveRisingEdge);
        wasActive = true;
        var isForeground =
            foregroundInput.IsCurrentProcessForeground() &&
            frame.Lifecycle.IsForeground &&
            !frame.Lifecycle.IsShuttingDown;
        if (!isForeground)
        {
            // Keep sampling above so a held key cannot become a delayed edge,
            // but never dispatch a background command or retain movement.
            actions = Array.Empty<FieldNavigationAction>();
            autoWalkToggleRequested = false;
            autoWalk.Suspend();
        }

        if (autoWalkToggleRequested && autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap))
        {
            _ = autoWalk.Stop();
            speak("Auto walk off.", true);
            log("Native Steam 2026 world-map auto walk: P toggle off.");
            autoWalkToggleRequested = false;
        }

        if (autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap) &&
            actions.Any(action => action != FieldNavigationAction.RepeatTarget))
        {
            _ = autoWalk.Stop();
            speak("Auto walk off.", true);
            log("Native Steam 2026 world-map auto walk stopped because the selection changed.");
        }

        if (nowUtc < nextScanUtc && actions.Count == 0 && !autoWalkToggleRequested)
        {
            return;
        }

        nextScanUtc = nowUtc + TimeSpan.FromMilliseconds(
            Math.Max(30, config.WorldMapScanIntervalMs));
        var stateResult = stateReader.Read();
        LogDiagnostic("state", stateResult.Diagnostic, ref lastStateDiagnostic);
        if (!stateResult.IsUsable ||
            !runtimes.TryGetValue(
                (
                    stateResult.State.WorldMapType,
                    WorldMapDataLoader.ResolveProgressStage(
                        stateResult.State.WorldMapType,
                        stateResult.State.WorldProgress)),
                out var runtime))
        {
            SilenceForRecovery();
            return;
        }

        var state = stateResult.State;
        var entityResult = entityReader.Read();
        runtime.UpdateEntities(entityResult.IsUsable
            ? entityResult.Entities
            : Array.Empty<WorldMapEntitySnapshot>());
        LogDiagnostic("entities", entityResult.Diagnostic, ref lastEntityDiagnostic);
        foreach (var context in runtimes.Values)
        {
            if (!ReferenceEquals(context, runtime))
            {
                context.Footsteps.Reset();
                context.Navigation.Suspend("another native world map is active");
            }
        }

        if (!isForeground)
        {
            runtime.Footsteps.Reset();
            beaconPlayer?.StopAll();
            autoWalk.Suspend();
            return;
        }

        if (config.EnableWorldMapFootstepFeedback && runtime.Footsteps.Observe(state, nowUtc))
        {
            PlayFootstep(state);
        }

        LogDiagnostic("footsteps", runtime.Footsteps.LastDiagnostic, ref lastFootstepDiagnostic);
        if (!config.EnableWorldMapNavigationAssistant)
        {
            runtime.Navigation.Suspend("world navigation disabled");
            beaconPlayer?.StopAll();
            autoWalk.Reset();
            return;
        }

        foreach (var action in actions)
        {
            ProcessOutput(runtime, runtime.Navigation.HandleAction(action, state, nowUtc));
        }

        if (autoWalkToggleRequested)
        {
            if (!runtime.Navigation.BeaconEnabled)
            {
                ProcessOutput(
                    runtime,
                    runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state, nowUtc));
            }

            if (autoWalk.TryStart(
                    NavigationAutoWalkDomain.WorldMap,
                    runtime.Navigation.BeaconEnabled))
            {
                speak("Auto walk on.", true);
                log("Native Steam 2026 world-map auto walk: P toggle on.");
            }
        }

        ProcessOutput(runtime, runtime.Navigation.Observe(state, nowUtc));
        UpdateAutoWalk(runtime, state);
        LogDiagnostic("navigation", runtime.Navigation.LastDiagnostic, ref lastNavigationDiagnostic);
    }

    internal void Suspend(string diagnostic)
    {
        foreach (var runtime in runtimes.Values)
        {
            runtime.Footsteps.Reset();
        }

        beaconPlayer?.StopAll();
        autoWalk.Suspend();
        log($"Native Steam 2026 world-map accessibility suspended: {diagnostic}.");
    }

    internal void Reset(string diagnostic)
    {
        foreach (var runtime in runtimes.Values)
        {
            runtime.UpdateEntities(Array.Empty<WorldMapEntitySnapshot>());
            runtime.Footsteps.Reset();
            runtime.Navigation.Suspend(diagnostic);
        }

        beaconPlayer?.StopAll();
        progressSink?.Deactivate();
        autoWalk.Reset();
        nextScanUtc = DateTime.MinValue;
        wasActive = false;
        log($"Native Steam 2026 world-map accessibility reset: {diagnostic}.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        foreach (var runtime in runtimes.Values)
        {
            runtime.Navigation.Reset();
        }

        beaconPlayer?.Dispose();
        progressSink?.Dispose();
        progressBar?.Dispose();
        footstepPlayer.Dispose();
        autoWalk.Dispose();
        runtimes.Clear();
    }

    private void ProcessOutput(
        WorldMapRuntimeContext runtime,
        WorldMapNavigationOutput? output)
    {
        if (output is not { } value)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(value.Speech))
        {
            log($"Native Steam 2026 world-map speech: {value.Speech}");
            speak(value.Speech, true);
        }

        if (value.Beacon is { } beacon)
        {
            beaconPlayer?.Play(beacon);
        }

        if (!runtime.Navigation.BeaconEnabled)
        {
            beaconPlayer?.StopAll();
        }
    }

    private void PlayFootstep(WorldMapStateSnapshot state)
    {
        if (!config.UseCosmoFootstepSounds ||
            cosmoFootsteps is null ||
            !cosmoFootsteps.TrySelectNext(state, out var selection) ||
            selection.IsSilent)
        {
            LogSuppressionOnce(
                $"unmapped:{state.WorldMapType}:{state.PlayerModelId}:{state.TerrainId}",
                $"Native Steam 2026 world footstep suppressed: no explicit Cosmo mapping for " +
                $"map={state.WorldMapType}, model={state.PlayerModelId}, terrain={state.TerrainId}.");
            return;
        }

        lastSuppressionKey = string.Empty;
        if (footstepPlayer.Play(
                $"native x64 world movement; map={state.WorldMapType}; model={state.PlayerModelId}; " +
                $"terrain={state.TerrainId}; cosmo={selection.TrackName}/{selection.SoundId}",
                selection.Path))
        {
            log(
                $"Native Steam 2026 world footstep played: model={state.PlayerModelId}, " +
                $"terrain={state.TerrainId}, track={selection.TrackName}, sound={selection.SoundId}.");
        }
    }

    private void SilenceForRecovery()
    {
        foreach (var runtime in runtimes.Values)
        {
            runtime.Footsteps.Reset();
        }

        beaconPlayer?.StopAll();
        autoWalk.Suspend();
    }

    private void UpdateAutoWalk(WorldMapRuntimeContext runtime, WorldMapStateSnapshot state)
    {
        if (!autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap))
        {
            return;
        }

        var hasDirection = runtime.Navigation.TryResolveAutomaticInput(state, out var direction);
        var result = autoWalk.Drive(
            hasDirection ? direction : FieldNavigationInput.None,
            canMove: hasDirection,
            routeActive: runtime.Navigation.BeaconEnabled);
        if (result.Success)
        {
            lastAutoWalkFailure = string.Empty;
            return;
        }

        if (!string.Equals(result.Diagnostic, lastAutoWalkFailure, StringComparison.Ordinal))
        {
            lastAutoWalkFailure = result.Diagnostic;
            log($"Native Steam 2026 world-map auto walk failed closed: {result.Diagnostic}");
            speak("Auto walk stopped because directional input failed.", true);
        }
    }

    private void LogDiagnostic(string kind, string diagnostic, ref string prior)
    {
        if (!config.EnableWorldMapNavigationDiagnostics ||
            string.Equals(diagnostic, prior, StringComparison.Ordinal))
        {
            return;
        }

        prior = diagnostic;
        log($"Native Steam 2026 world-map {kind}: {diagnostic}.");
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

    private static CosmoFootstepSequencer? TryCreateCosmoFootsteps(
        AccessibilityConfig config,
        string modDirectory,
        Action<string> log)
    {
        if (!config.UseCosmoFootstepSounds)
        {
            return null;
        }

        try
        {
            var soundDirectory = ResolveModPath(
                modDirectory,
                config.CosmoFootstepSoundDirectory,
                @"Assets\footsteps\cosmo");
            var cosmoConfig = CosmoFootstepConfig.Load(Path.Combine(soundDirectory, "config.toml"));
            if (cosmoConfig.TrackCount == 0)
            {
                log("Native Steam 2026 world footsteps remain silent: Cosmo has no tracks.");
                return null;
            }

            log($"Native Steam 2026 world Cosmo footsteps ready: tracks={cosmoConfig.TrackCount}.");
            return new CosmoFootstepSequencer(
                cosmoConfig,
                new Dictionary<int, string>(),
                soundDirectory);
        }
        catch (Exception ex)
        {
            log($"Native Steam 2026 world footsteps remain silent: {ex.Message}");
            return null;
        }
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
}
