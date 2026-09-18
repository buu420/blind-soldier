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
    private readonly MidgarZolomStateReader midgarZolomStateReader;
    private readonly MidgarZolomCrossingTracker midgarZolomCrossingTracker = new();

    /// <summary>
    /// The other half of the Zolom feature. The crossing tracker says when to dash;
    /// this says the player is standing on the marsh, whether the serpent is on it
    /// with them, and when they are clear. It shipped to the legacy runtime only,
    /// so until now an x64 player was told when to run without ever being told
    /// where they were.
    /// </summary>
    private readonly MidgarZolomAreaTracker midgarZolomAreaTracker = new();

    /// <summary>
    /// Clears both halves of the Zolom feature together.
    /// </summary>
    /// <remarks>
    /// One method rather than six pairs of calls, because the two trackers were
    /// already reset in six places and only one of them existed here. Left as
    /// separate call sites, the next reset added would reset one and not the other,
    /// and stale marsh state announces "Clear of the Zolom marsh" to a player who
    /// never entered it.
    /// </remarks>
    private void ResetMidgarZolomTrackers()
    {
        midgarZolomCrossingTracker.Reset();
        midgarZolomAreaTracker.Reset();
    }
    private readonly Dictionary<(int MapType, int ProgressStage), WorldMapRuntimeContext> runtimes = [];
    private readonly NativeFieldNavigationProgressBar? progressBar;
    private readonly IntervalFieldNavigationProgressSink? progressSink;
    private readonly NavigationProgressController progressController;
    private readonly FootstepSoundPlayer footstepPlayer;
    private readonly CosmoFootstepSequencer? cosmoFootsteps;
    private readonly NavigationBeaconPlayer? entranceCuePlayer;
    private readonly NavigationAutoWalkController autoWalk;

    /// <summary>Where every automatic direction is delivered; never a Windows key event.</summary>
    private readonly Steam2026NativeDirectionalInputSink directionalInput;
    private readonly Action<string, bool> speak;
    private readonly Action<string> log;
    private readonly Action<NavigationBeaconCue, float>? playEntranceCue;
    private DateTime nextScanUtc = DateTime.MinValue;
    private string lastStateDiagnostic = string.Empty;
    private string lastEntityDiagnostic = string.Empty;
    private string lastNavigationDiagnostic = string.Empty;
    private string lastFootstepDiagnostic = string.Empty;
    private string lastTerrainDiagnostic = string.Empty;
    private string lastTerrainFailure = string.Empty;
    private string lastSuppressionKey = string.Empty;
    private string lastAutoWalkFailure = string.Empty;
    private long lastProgressPublicationRevision;
    private long lastProgressControlSpeechRevision;
    private bool wasActive;
    private int disposed;

    // The controller navigation menu. Fetched through a delegate because SDL is
    // loaded when the host first looks for a pad, which is often after this exists.
    private readonly Func<ControllerNavigationCapture?> controllerCapture;
    private ControllerNavigationDispatcher? controllerNavigation;
    private WorldMapRuntimeContext? controllerRuntime;
    private WorldMapStateSnapshot controllerState;
    private DateTime controllerNowUtc;
    private bool controllerMenuIsOpen;

    internal Steam2026WorldMapAccessibilityCoordinator(
        AccessibilityConfig config,
        ILegacyAddressSpace addressSpace,
        Steam2026ForegroundInputAdapter foregroundInput,
        string gameWorkingDirectory,
        string modDirectory,
        Action<string, bool> speak,
        Action<string> log,
        NavigationProgressController? progressController = null,
        NavigationAutoWalkController? autoWalk = null,
        Action<NavigationBeaconCue, float>? playEntranceCue = null,
        Func<ControllerNavigationCapture?>? controllerCapture = null,
        Steam2026NativeDirectionalInputSink? directionalInput = null)
    {
        // Never a Win32 sink. See the field coordinator: this host synthesizes the legacy
        // keyboard state, and the keys the control table names are the screen reader's.
        this.directionalInput = directionalInput ?? new Steam2026NativeDirectionalInputSink();
        this.controllerCapture = controllerCapture ?? (static () => null);
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(addressSpace);
        this.foregroundInput = foregroundInput ?? throw new ArgumentNullException(nameof(foregroundInput));
        ArgumentException.ThrowIfNullOrWhiteSpace(gameWorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);
        this.speak = speak ?? throw new ArgumentNullException(nameof(speak));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.playEntranceCue = playEntranceCue;
        this.autoWalk = autoWalk ?? NavigationAutoWalkController.CreateCurrentProcess(
            addressSpace,
            this.directionalInput);

        stateReader = new WorldMapStateReader(addressSpace);
        entityReader = new WorldMapEntityReader(addressSpace);
        midgarZolomStateReader = new MidgarZolomStateReader(addressSpace);
        this.progressController = progressController ?? new NavigationProgressController(
            config.EnableNavigationProgressIndicators,
            config.NavigationProgressIntervalPercent);
        progressBar = config.EnableWorldMapNavigationAssistant
            ? new NativeFieldNavigationProgressBar(log)
            : null;
        progressSink = progressBar is null
            ? null
            : new IntervalFieldNavigationProgressSink(
                progressBar,
                this.progressController);
        footstepPlayer = new FootstepSoundPlayer(
            ResolveModPath(
                modDirectory,
                config.FieldFootstepSoundPath,
                @"Assets\footsteps\selected_subway_step.ogg"),
            config.FieldFootstepVolumePercent,
            log);
        cosmoFootsteps = TryCreateCosmoFootsteps(config, modDirectory, log);
        entranceCuePlayer = playEntranceCue is null && config.EnableWorldMapEntranceProximityCues
            ? new NavigationBeaconPlayer(
                ResolveModPath(
                    modDirectory,
                    config.WorldMapEntranceCueSoundPath,
                    @"Assets\navigation\field_zone_transition.wav"),
                config.WorldMapEntranceCueVolumePercent,
                log)
            : null;

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
        var triggerPath = Path.Combine(
            modDirectory,
            "Assets",
            "world",
            "world-map-location-triggers.json");
        if (!File.Exists(coordinatePath) || !File.Exists(menuNamePath) || !File.Exists(triggerPath))
        {
            throw new FileNotFoundException(
                "Installed world-map location metadata is incomplete.",
                !File.Exists(coordinatePath)
                    ? coordinatePath
                    : !File.Exists(menuNamePath) ? menuNamePath : triggerPath);
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
                    var catalog = WorldMapTargetCatalog.Load(map, coordinatePath, menuNamePath, triggerPath);
                    runtimes.Add(
                        (mapType, progressStage),
                        new WorldMapRuntimeContext(
                            map,
                            catalog,
                            progressSink,
                            Math.Max(1, config.WorldMapNavigationSpeechDistanceUnitsPerCount),
                            TimeSpan.FromMilliseconds(Math.Max(0, config.WorldMapNavigationSpeechIntervalMs)),
                            TimeSpan.FromMilliseconds(Math.Max(80, config.WorldMapFootstepWalkIntervalMs)),
                            TimeSpan.FromMilliseconds(Math.Max(80, config.WorldMapFootstepChocoboIntervalMs)),
                            Math.Max(0, config.WorldMapEntranceCueInnerRangeUnits),
                            Math.Max(1, config.WorldMapEntranceCueOuterRangeUnits),
                            TimeSpan.FromMilliseconds(Math.Max(0, config.WorldMapEntranceCueIntervalMs))));
                    log(
                        $"Native Steam 2026 world-map type {mapType}, progress stage {progressStage} ready: " +
                        $"triangles={map.Triangles.Count}, locations={catalog.Locations.Count}, " +
                        $"unresolvedLocations={catalog.UnresolvedLocations.Count}, " +
                        $"chocoboTracks={catalog.ChocoboTracks.Count}.");
                    if (mapType == 0 && progressStage == 0 && catalog.UnresolvedLocations.Count > 0)
                    {
                        log(
                            "Native Steam 2026 world-map locations without a terrain-script entrance were omitted: " +
                            string.Join(", ", catalog.UnresolvedLocations.Select(location => location.Label)) + ".");
                    }
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
            lastProgressControlSpeechRevision = progressController.SpeechRevision;
            ResetMidgarZolomTrackers();
            if (WorldMapNavigationLifecycle.IsCombatInterruptionModule(frame.Lifecycle.ModuleId))
            {
                foreach (var context in runtimes.Values)
                {
                    context.Footsteps.Reset();
                    context.EntranceProximityCues.Reset();
                    context.Navigation.PauseForCombat(
                        $"native combat module {frame.Lifecycle.ModuleId}");
                }

                entranceCuePlayer?.StopAll();
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
        var autoWalkToggleWasObserved = autoWalkToggleRequested;
        wasActive = true;
        var isForeground =
            foregroundInput.IsCurrentProcessForeground() &&
            frame.Lifecycle.IsForeground &&
            !frame.Lifecycle.IsShuttingDown;
        var higherPrioritySpeech = false;
        var progressControlRevision = progressController.SpeechRevision;
        var progressControlSpeechWasObserved =
            progressControlRevision != lastProgressControlSpeechRevision;
        if (progressControlSpeechWasObserved)
        {
            higherPrioritySpeech = true;
            lastProgressControlSpeechRevision = progressControlRevision;
        }
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
            higherPrioritySpeech = true;
            log("Native Steam 2026 world-map auto walk: P toggle off.");
            autoWalkToggleRequested = false;
        }

        if (autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap) &&
            actions.Any(action => action != FieldNavigationAction.RepeatTarget))
        {
            _ = autoWalk.Stop();
            speak("Auto walk off.", true);
            higherPrioritySpeech = true;
            log("Native Steam 2026 world-map auto walk stopped because the selection changed.");
        }

        // A toggle-off is consumed above after speaking. Keep its original edge
        // through this throttle so the terrain tracker sees that higher-priority
        // speech and waits for a quiet interval instead of talking over it.
        if (ShouldThrottleObservation(
                nowUtc,
                nextScanUtc,
                actions.Count,
                autoWalkToggleWasObserved,
                progressControlSpeechWasObserved))
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
            SilenceForRecovery(nowUtc, higherPrioritySpeech);
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
                context.EntranceProximityCues.Reset();
                context.TerrainAnnouncements.Reset();
                context.Navigation.Suspend("another native world map is active");
            }
        }

        if (!isForeground)
        {
            runtime.Footsteps.Reset();
            runtime.EntranceProximityCues.Reset();
            entranceCuePlayer?.StopAll();
            runtime.TerrainAnnouncements.ObserveUnavailable(nowUtc);
            ResetMidgarZolomTrackers();
            autoWalk.Suspend();
            return;
        }

        higherPrioritySpeech |= ObserveMidgarZolomCrossing(runtime, state);
        var progressRevision = progressSink?.PublicationRevision ?? 0;
        if (progressRevision != lastProgressPublicationRevision)
        {
            higherPrioritySpeech = true;
            lastProgressPublicationRevision = progressRevision;
        }

        if (config.EnableWorldMapFootstepFeedback && runtime.Footsteps.Observe(state, nowUtc))
        {
            PlayFootstep(state);
        }

        LogDiagnostic("footsteps", runtime.Footsteps.LastDiagnostic, ref lastFootstepDiagnostic);
        ObserveEntranceCue(runtime, state, nowUtc);
        if (!config.EnableWorldMapNavigationAssistant)
        {
            runtime.Navigation.Suspend("world navigation disabled");
            autoWalk.Reset();
            ObserveTerrain(runtime, state, nowUtc, higherPrioritySpeech);
            return;
        }

        foreach (var action in actions)
        {
            higherPrioritySpeech |= ProcessOutput(
                runtime.Navigation.HandleAction(action, state, nowUtc));
        }

        if (autoWalkToggleRequested)
        {
            if (!runtime.Navigation.BeaconEnabled)
            {
                higherPrioritySpeech |= ProcessOutput(
                    runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state, nowUtc));
            }

            if (autoWalk.TryStart(
                    NavigationAutoWalkDomain.WorldMap,
                    runtime.Navigation.BeaconEnabled))
            {
                speak("Auto walk on.", true);
                higherPrioritySpeech = true;
                log("Native Steam 2026 world-map auto walk: P toggle on.");
            }
        }

        controllerRuntime = runtime;
        controllerState = state;
        controllerNowUtc = nowUtc;
        higherPrioritySpeech |= DrainControllerNavigation(isForeground);

        // The route keeps running while the menu is open; it just stops narrating
        // itself, so recurring directions cannot talk over the item being read.
        var routeObservation = runtime.Navigation.Observe(
            state,
            nowUtc,
            autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap));
        if (controllerMenuIsOpen)
        {
            autoWalk.Suspend();
        }
        else
        {
            higherPrioritySpeech |= ProcessOutput(routeObservation);
            higherPrioritySpeech |= UpdateAutoWalk(runtime, state, nowUtc);
        }
        progressRevision = progressSink?.PublicationRevision ?? 0;
        if (progressRevision != lastProgressPublicationRevision)
        {
            higherPrioritySpeech = true;
            lastProgressPublicationRevision = progressRevision;
        }

        ObserveTerrain(runtime, state, nowUtc, higherPrioritySpeech);
        LogDiagnostic("navigation", runtime.Navigation.LastDiagnostic, ref lastNavigationDiagnostic);
    }

    internal static bool ShouldThrottleObservation(
        DateTime nowUtc,
        DateTime nextScanUtc,
        int navigationActionCount,
        bool autoWalkToggleWasObserved,
        bool progressControlSpeechWasObserved) =>
        nowUtc < nextScanUtc &&
        navigationActionCount == 0 &&
        !autoWalkToggleWasObserved &&
        !progressControlSpeechWasObserved;

    internal void Suspend(string diagnostic)
    {
        foreach (var runtime in runtimes.Values)
        {
            runtime.Footsteps.Reset();
            runtime.EntranceProximityCues.Reset();
            runtime.TerrainAnnouncements.ObserveUnavailable(DateTime.UtcNow);
        }

        entranceCuePlayer?.StopAll();
        ResetMidgarZolomTrackers();

        autoWalk.Suspend();
        log($"Native Steam 2026 world-map accessibility suspended: {diagnostic}.");
    }

    internal void Reset(string diagnostic)
    {
        foreach (var runtime in runtimes.Values)
        {
            runtime.UpdateEntities(Array.Empty<WorldMapEntitySnapshot>());
            runtime.Footsteps.Reset();
            runtime.TerrainAnnouncements.Reset();
            runtime.EntranceProximityCues.Reset();
            runtime.Navigation.Suspend(diagnostic);
        }

        entranceCuePlayer?.StopAll();
        progressSink?.Deactivate();
        ResetMidgarZolomTrackers();
        autoWalk.Reset();
        nextScanUtc = DateTime.MinValue;
        lastTerrainFailure = string.Empty;
        lastTerrainDiagnostic = string.Empty;
        lastProgressPublicationRevision = progressSink?.PublicationRevision ?? 0;
        lastProgressControlSpeechRevision = progressController.SpeechRevision;
        wasActive = false;
        log($"Native Steam 2026 world-map accessibility reset: {diagnostic}.");
    }

    private bool ObserveMidgarZolomCrossing(
        WorldMapRuntimeContext runtime,
        WorldMapStateSnapshot state)
    {
        if (!config.EnableSpeech)
        {
            ResetMidgarZolomTrackers();
            return false;
        }

        var zolom = midgarZolomStateReader.Read();
        var isAtMarshShore =
            state.IsOverworld &&
            runtime.IsAtTerrainBoundary(state, terrainId: 7);

        // Captured rather than returned on, because the area cue has to be
        // considered on every pass and needs to know whether the crossing cue
        // just spoke - two cues in one breath would bury each other.
        var crossingCueReady = midgarZolomCrossingTracker.Observe(state, zolom, isAtMarshShore);
        var speechDelivered = false;
        if (crossingCueReady)
        {
            log(
                $"Native Steam 2026 Midgar Zolom crossing window: " +
                $"player={state.X},{state.Z}, zolom={zolom.State.X},{zolom.State.Z}, " +
                $"shoreline={isAtMarshShore}.");
            speechDelivered = TrySpeakMidgarZolom(
                MidgarZolomCrossingTracker.CueText,
                "crossing window");
        }

        // The native player word is coherent with the terrain announcement
        // sample. A static geometry lookup is unnecessary here and can choose
        // the wrong overlapping triangle at a world-map seam.
        var isOnMarsh =
            state.IsOverworld &&
            state.TerrainId == MidgarZolomAreaTracker.MarshTerrainId;
        var areaCue = midgarZolomAreaTracker.Observe(
            state,
            zolom,
            isOnMarsh,
            crossingCueReady && speechDelivered);
        if (areaCue is null)
        {
            return speechDelivered;
        }

        log(
            $"Native Steam 2026 Midgar Zolom area: player={state.X},{state.Z}, " +
            $"model={state.PlayerModelId}, onMarsh={isOnMarsh}, " +
            $"zolom={zolom.State.IsActive}: {areaCue}");
        var areaCueDelivered = TrySpeakMidgarZolom(areaCue, "area cue");
        if (areaCueDelivered)
        {
            runtime.TerrainAnnouncements.RecordExternalTerrainSpeech(
                new WorldMapSurfaceSample(state.TerrainId, state.HasChocoboTracks, state.RegionId),
                MidgarZolomAreaTracker.MarshTerrainId);
        }

        return speechDelivered || areaCueDelivered;
    }

    private bool TrySpeakMidgarZolom(string text, string context)
    {
        try
        {
            speak(text, true);
            return true;
        }
        catch (Exception ex)
        {
            log(
                $"Native Steam 2026 Midgar Zolom {context} speech failed; " +
                $"generic terrain remains available: {ex.Message}");
            return false;
        }
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

        progressSink?.Dispose();
        progressBar?.Dispose();
        entranceCuePlayer?.Dispose();
        footstepPlayer.Dispose();
        autoWalk.Dispose();
        runtimes.Clear();
    }

    private bool ProcessOutput(WorldMapNavigationOutput? output)
    {
        if (output is not { } value)
        {
            return false;
        }

        if (value.StopAutoWalk)
        {
            _ = autoWalk.Stop();
            log(
                "Native Steam 2026 world-map navigation requested an automatic-walk " +
                "fail-stop; regular navigation remains active.");
        }

        if (!string.IsNullOrWhiteSpace(value.Speech))
        {
            log($"Native Steam 2026 world-map speech: {value.Speech}");
            speak(value.Speech, true);
            return true;
        }

        return false;
    }

    private void ObserveTerrain(
        WorldMapRuntimeContext runtime,
        WorldMapStateSnapshot state,
        DateTime nowUtc,
        bool higherPrioritySpeech)
    {
        if (!config.EnableSpeech)
        {
            runtime.TerrainAnnouncements.Reset();
            return;
        }

        if (!runtime.TryResolveSurface(state, out var surface, out var diagnostic))
        {
            runtime.TerrainAnnouncements.ObserveUnavailable(nowUtc, higherPrioritySpeech);
            if (!string.Equals(diagnostic, lastTerrainFailure, StringComparison.Ordinal))
            {
                lastTerrainFailure = diagnostic;
                log($"Native Steam 2026 world-map terrain unavailable: {diagnostic}.");
            }

            return;
        }

        lastTerrainFailure = string.Empty;
        var announcement = runtime.TerrainAnnouncements.ObserveAnnouncement(
            surface,
            nowUtc,
            higherPrioritySpeech);
        if (config.EnableWorldMapNavigationDiagnostics)
        {
            LogDiagnostic(
                "terrain",
                runtime.TerrainAnnouncements.LastDiagnostic,
                ref lastTerrainDiagnostic);
        }

        if (announcement is not { } ready)
        {
            return;
        }

        log(
            $"Native Steam 2026 world-map terrain announcement: " +
            $"player={state.X},{state.Z}, model={state.PlayerModelId}, " +
            $"terrain={surface.TerrainId} ({WorldMapTerrainNames.GetName(surface.TerrainId)}), " +
            $"region={surface.RegionId?.ToString() ?? "unavailable"}, " +
            $"tracks={surface.HasChocoboTracks}: {ready.Speech}");
        try
        {
            speak(ready.Speech, false);
            runtime.TerrainAnnouncements.AcknowledgeSpeech();
        }
        catch (Exception ex)
        {
            // The x64 speech boundary throws where the x86 host returns false.
            // Leave the announcement unacknowledged so a transient failure can
            // never silently consume the player's current terrain.
            log($"Native Steam 2026 world-map terrain speech failed and remains pending: {ex.Message}");
            return;
        }

    }

    private void ObserveEntranceCue(
        WorldMapRuntimeContext runtime,
        WorldMapStateSnapshot state,
        DateTime nowUtc)
    {
        if (!config.EnableWorldMapEntranceProximityCues)
        {
            runtime.EntranceProximityCues.Reset();
            entranceCuePlayer?.StopAll();
            return;
        }

        var proximity = runtime.EntranceProximityCues.Update(state, nowUtc);
        if (proximity is not { } ready)
        {
            return;
        }

        var cue = WorldMapEntranceProximitySpatializer.CreateCue(runtime.Map, state, ready);
        if (cue is not { } spatialCue)
        {
            runtime.EntranceProximityCues.Reset();
            log(
                $"Native Steam 2026 world-map entrance cue refused: target={ready.Target.Label}, " +
                $"triangle={ready.Arrival.TriangleId}, player={state.X},{state.Z}.");
            return;
        }

        try
        {
            var played = false;
            if (playEntranceCue is not null)
            {
                playEntranceCue(spatialCue, ready.Gain);
                played = true;
            }
            else
            {
                played = entranceCuePlayer?.Play(spatialCue, ready.Gain) == true;
            }

            if (played)
            {
                log(
                    $"Native Steam 2026 world-map entrance cue played: target={ready.Target.Label}, " +
                    $"triangle={ready.Arrival.TriangleId}, " +
                    $"entrance={ready.Arrival.X},{ready.Arrival.Z}, " +
                    $"player={state.X},{state.Z}, distance={ready.DistanceUnits:0}, " +
                    $"gain={ready.Gain:0.000}.");
            }
        }
        catch (Exception ex)
        {
            // The x64 host treats every output boundary as fallible. A device or
            // test seam failure must not tear down world-map speech or navigation.
            log($"Native Steam 2026 world-map entrance cue failed: {ex.Message}");
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

    private void SilenceForRecovery(DateTime nowUtc, bool higherPrioritySpeech = false)
    {
        foreach (var runtime in runtimes.Values)
        {
            runtime.Footsteps.Reset();
            runtime.EntranceProximityCues.Reset();
            runtime.TerrainAnnouncements.ObserveUnavailable(nowUtc, higherPrioritySpeech);
        }

        entranceCuePlayer?.StopAll();
        ResetMidgarZolomTrackers();

        autoWalk.Suspend();
    }


    /// <summary>
    /// Tells the capture what the world looks like and does whatever it queued.
    /// The world map owns the pad only while module 3 is the live one, so the field
    /// coordinator cannot consume a command meant for a world selection.
    /// </summary>
    private bool DrainControllerNavigation(bool isForeground)
    {
        var capture = controllerCapture();
        if (capture is null)
        {
            controllerMenuIsOpen = false;
            return false;
        }

        capture.PublishContext(
            ControllerNavigationDomain.WorldMap,
            isForeground,
            config.EnableWorldMapNavigationAssistant,
            gameIsBusy: false,
            controllerNowUtc,
            identity: 0);

        controllerNavigation ??= new ControllerNavigationDispatcher(
            new ControllerNavigationServices(
                () => controllerRuntime?.Navigation.BeaconEnabled == true,
                action => controllerRuntime?.Navigation
                    .HandleAction(action, controllerState, controllerNowUtc)?.Speech,
                () => autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap),
                () => autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true),
                StopEveryControllerAutoWalk,
                () => autoWalk.Suspend()),
            speech => { speak(speech, true); return true; },
            log);

        var spoke = controllerNavigation.Drain(capture, ControllerNavigationDomain.WorldMap, controllerNowUtc);
        controllerMenuIsOpen = capture.IsOpen;
        return spoke;
    }

    /// <summary>
    /// Ends the automatic walk whatever domain owns it. A is spoken guidance, and
    /// spoken guidance means the mod stops driving; and a stop pressed as the module
    /// changes must still reach the walk that is actually running.
    /// </summary>
    private void StopEveryControllerAutoWalk()
    {
        if (autoWalk.Enabled)
        {
            _ = autoWalk.Stop();
        }
    }

    private bool UpdateAutoWalk(
        WorldMapRuntimeContext runtime,
        WorldMapStateSnapshot state,
        DateTime nowUtc)
    {
        if (!autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap))
        {
            return false;
        }

        var hasDirection = runtime.Navigation.TryResolveAutomaticInput(state, out var direction);
        var result = autoWalk.Drive(
            hasDirection ? direction : FieldNavigationInput.None,
            canMove: hasDirection,
            routeActive: runtime.Navigation.BeaconEnabled);
        if (result.Success)
        {
            // After the drive, so the renewal belongs to the direction just committed to.
            if (hasDirection)
            {
                directionalInput.Renew(nowUtc);
            }

            lastAutoWalkFailure = string.Empty;
            return false;
        }

        if (!string.Equals(result.Diagnostic, lastAutoWalkFailure, StringComparison.Ordinal))
        {
            lastAutoWalkFailure = result.Diagnostic;
            log($"Native Steam 2026 world-map auto walk failed closed: {result.Diagnostic}");
            speak("Auto walk stopped because directional input failed.", true);
            return true;
        }

        return false;
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
