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
    private readonly ILegacyAddressSpace addressSpace;
    private readonly Steam2026ForegroundInputAdapter foregroundInput;
    private readonly WorldMapStateReader stateReader;
    private readonly WorldMapDialogueReader dialogueReader;
    private readonly WorldMapDialogueTracker dialogueTracker = new();
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

    // The Great Glacier's regional treasure routes, shared with the field coordinator.
    private readonly GreatGlacierRegionalNavigator? glacierRegion;
    private ControllerNavigationDispatcher? controllerNavigation;
    private WorldMapRuntimeContext? controllerRuntime;
    private WorldMapSubmarineJourney? submarineJourney;
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
        Steam2026NativeDirectionalInputSink? directionalInput = null,
        GreatGlacierRegionalNavigator? glacierRegion = null)
    {
        this.glacierRegion = glacierRegion;
        // Never a Win32 sink. See the field coordinator: this host synthesizes the legacy
        // keyboard state, and the keys the control table names are the screen reader's.
        this.directionalInput = directionalInput ?? new Steam2026NativeDirectionalInputSink();
        this.controllerCapture = controllerCapture ?? (static () => null);
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(addressSpace);
        this.addressSpace = addressSpace;
        this.foregroundInput = foregroundInput ?? throw new ArgumentNullException(nameof(foregroundInput));
        dialogueReader = new WorldMapDialogueReader(addressSpace);
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
                    if (mapType == GreatGlacierRegion.SnowfieldWorldMapType)
                    {
                        glacierRegion?.AttachSnowfield(
                            map,
                            catalog.Locations,
                            catalog.EntranceTriangleIds,
                            Path.Combine(Path.GetDirectoryName(mapPath) ?? string.Empty, "world_us.lgp"));
                    }

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
                    runtimes[(mapType, progressStage)].Navigation.GlacierRegion = glacierRegion;
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

        if (!runtimes.ContainsKey((GreatGlacierRegion.SnowfieldWorldMapType, 0)))
        {
            // Without the snowfield the Great Glacier routes cannot be built: a treasure asked
            // for says so instead of waiting.
            glacierRegion?.MarkUnavailable("the snowfield's WM3 map could not be loaded");
        }

        if (runtimes.Count == 0)
        {
            throw new InvalidOperationException("No native world-map geometry could be loaded.");
        }

        submarineJourney = WorldMapSubmarineJourney.Attach(runtimes.Values, addressSpace);

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
            PublishControllerUnavailable(nowUtc);
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
                Reset($"module changed to {frame.Lifecycle.ModuleId}", nowUtc);
            }

            return;
        }

        // Own and sample all six shared navigation keys on every world frame,
        // including background frames, so refocus cannot create delayed edges.
        if (submarineJourney?.ObserveNativeTransition(nowUtc) == true) autoWalk.Suspend();
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
            PublishControllerUnavailable(nowUtc);
            // Keep sampling above so a held key cannot become a delayed edge,
            // but never dispatch a background command or retain movement.
            actions = Array.Empty<FieldNavigationAction>();
            autoWalkToggleRequested = false;
            autoWalk.Suspend();
        }

        if (autoWalkToggleRequested && autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap))
        {
            _ = autoWalk.Stop();
            glacierRegion?.NoteAutoWalk(false);
            submarineJourney?.NoteAutoWalk(false);
            speak("Auto walk off.", true);
            higherPrioritySpeech = true;
            log("Native Steam 2026 world-map auto walk: P toggle off.");
            autoWalkToggleRequested = false;
        }

        if (autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap) &&
            actions.Any(action => action != FieldNavigationAction.RepeatTarget))
        {
            _ = autoWalk.Stop();
            glacierRegion?.NoteAutoWalk(false);
            submarineJourney?.NoteAutoWalk(false);
            speak("Auto walk off.", true);
            higherPrioritySpeech = true;
            log("Native Steam 2026 world-map auto walk stopped because the selection changed.");
        }

        // Movement ownership is checked on every frame, before the scan throttle and before
        // any failed read can return: lost focus, a world script, a window or the party menu
        // - or an ownership read that cannot be taken - releases held keys and makes
        // automatic walking forget its movement learning and stall time. The route is kept;
        // speech stays on the throttled scan below.
        if (!isForeground ||
            !dialogueReader.TryReadMovementOwnership(out var frameOwnership) ||
            frameOwnership.IsBlockingMovement)
        {
            autoWalk.Suspend();
            foreach (var context in runtimes.Values)
            {
                context.Navigation.PauseForNativeControl();
            }
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
        // Ownership is read on its own: a torn window must neither hide that a world script
        // or the party menu has taken control nor let automatic walking run.
        var ownershipRead = dialogueReader.TryReadMovementOwnership(out var ownership);
        var dialogueRead = dialogueReader.TryRead(out var dialogue);
        if (dialogueRead && isForeground && config.EnableSpeech && config.EnableRuntimeDialogueSpeech &&
            dialogueTracker.Observe(dialogue) is { } text)
        {
            speak(text, true);
            log($"Native Steam 2026 world dialogue: {text}");
        }
        var holdAutoWalk = !ownershipRead;
        if (WorldMapMovementGate.ShouldPauseSample(ownershipRead, ownership, dialogueRead, dialogueRead ? dialogue : null))
        {
            PublishControllerUnavailable(nowUtc);
            foreach (var context in runtimes.Values) context.Navigation.PauseForNativeControl();
            autoWalk.Suspend();
            entranceCuePlayer?.StopAll();
            return;
        }
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
        runtime.UpdateEntities(state, entityResult.IsUsable
            ? entityResult.Entities
            : Array.Empty<WorldMapEntitySnapshot>(), addressSpace);
        LogDiagnostic("entities", entityResult.Diagnostic, ref lastEntityDiagnostic);
        if (!entityResult.IsUsable)
        {
            runtime.Navigation.PauseForUnavailableEntities();
            autoWalk.Suspend();
            PublishControllerUnavailable(nowUtc);
        }
        foreach (var context in runtimes.Values)
        {
            if (!ReferenceEquals(context, runtime))
            {
                context.Footsteps.Reset();
                context.EntranceProximityCues.Reset();
                context.TerrainAnnouncements.Reset();
                context.Navigation.Suspend("another native world map is active", preserveSubmarineJourney: true);
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
        if (entityResult.IsUsable && config.EnableWorldMapNavigationAssistant &&
            runtime.ObserveUnderwaterSightings(state, higherPrioritySpeech, nowUtc) is { } emeraldSighting)
        {
            speak(emeraldSighting, false);
            higherPrioritySpeech = true;
        }
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
            PublishControllerUnavailable(nowUtc);
            runtime.Navigation.Suspend("world navigation disabled");
            autoWalk.Reset();
            ObserveTerrain(runtime, state, nowUtc, higherPrioritySpeech);
            return;
        }

        if (!entityResult.IsUsable)
        {
            // Terrain comes from the independently usable player state. A failed
            // entity read pauses routing, not valid ordinary world information.
            ObserveTerrain(runtime, state, nowUtc, higherPrioritySpeech);
            return;
        }

        foreach (var action in actions)
        {
            higherPrioritySpeech |= ProcessOutput(
                runtime.Navigation.HandleAction(action, state, nowUtc));
        }

        if (autoWalkToggleRequested && runtime.Navigation.IsHoldingDestination)
        {
            // A Great Glacier treasure waiting for its next snowfield leg: this asks for that
            // leg to be walked, not for the selection to be toggled off.
            autoWalkToggleRequested = false;
            runtime.Navigation.NoteAutoWalkStarted();
            glacierRegion?.Resume(autoWalk: true);
            speak("Auto walk on.", true);
            higherPrioritySpeech = true;
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
                runtime.Navigation.NoteAutoWalkStarted();
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
        // Automatic walking is held while the menu is open, so its stall clock is too.
        var routeObservation = runtime.Navigation.Observe(
            state,
            nowUtc,
            !controllerMenuIsOpen && !foregroundInput.ModSettingsOwnsInput && !holdAutoWalk && autoWalk.IsEnabledFor(NavigationAutoWalkDomain.WorldMap),
            dialogueReader.TryReadNativeWorldInput(out var diveInput) ? diveInput : null);
        if (controllerMenuIsOpen || foregroundInput.ModSettingsOwnsInput || holdAutoWalk)
        {
            autoWalk.Suspend();
            runtime.Navigation.PauseForNativeControl();
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
        PublishControllerUnavailable(DateTime.UtcNow);
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

    internal void Reset(string diagnostic, DateTime? nowUtc = null)
    {
        dialogueTracker.Reset();
        PublishControllerUnavailable(nowUtc ?? DateTime.UtcNow);
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

        if (value.StartAutoWalk && autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true))
        {
            // The next snowfield leg of a Great Glacier treasure the player asked to be walked to.
            log("Native Steam 2026 world-map auto walk started for the next Great Glacier leg.");
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
        PublishControllerUnavailable(nowUtc);
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
    /// Retires the world context without closing another active domain's menu.
    /// </summary>
    private void PublishControllerUnavailable(DateTime nowUtc)
    {
        // The field coordinator no longer closes another domain's menu. The world
        // coordinator must therefore retire its own context as soon as it stops
        // being usable, including battle entry before the freshness timeout.
        controllerCapture()?.PublishNavigationUnavailable(ControllerNavigationDomain.WorldMap,
            foregroundInput.IsCurrentProcessForeground(), nowUtc);
        controllerMenuIsOpen = false;
    }

    /// <summary>Publishes the usable world context and applies its queued commands.</summary>
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
                () =>
                {
                    if (!autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true))
                    {
                        return false;
                    }

                    controllerRuntime?.Navigation.NoteAutoWalkStarted();
                    return true;
                },
                StopEveryControllerAutoWalk,
                () => autoWalk.Suspend(),
                // A Great Glacier treasure between snowfield legs is a held destination: B
                // cancels it, A or X replace it, and X asks for its legs to be walked.
                () => controllerRuntime?.Navigation.IsHoldingDestination == true ||
                    controllerRuntime?.Navigation.IsHoldingEntityTarget == true,
                () => controllerRuntime?.Navigation.NoteAutoWalkStarted()),
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

        glacierRegion?.NoteAutoWalk(false);
        submarineJourney?.NoteAutoWalk(false);
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
        if (runtime.Navigation.DescribeAutomaticPace(
                state,
                hasDirection,
                direction,
                dialogueReader.TryReadNativeWorldInput(out var nativeInput) ? nativeInput : null) is { } pace)
        {
            log($"Native Steam 2026 world-map auto walk pace: {pace}");
        }

        var result = autoWalk.Drive(
            hasDirection ? direction : FieldNavigationInput.None,
            canMove: hasDirection,
            routeActive: runtime.Navigation.BeaconEnabled,
            holdFlightAction: runtime.Navigation.AutomaticInputHoldsFlightAction,
            worldSubmarine: runtime.Navigation.AutomaticInputIsWorldSubmarine,
            holdSubmarineThrust: runtime.Navigation.AutomaticInputHoldsSubmarineThrust,
            requestSubmarineDive: runtime.Navigation.AutomaticInputRequestsSubmarineDive);
        if (result.Success)
        {
            if (runtime.Navigation.AutomaticInputRequestsSubmarineDive)
                runtime.Navigation.NoteSubmarineDiveDelivered(nowUtc);
            // Renew the committed input, including the Highwind's direction-free brake.
            // Its 600 ms hold outlasts the native input overlay's 500 ms lease.
            if (hasDirection || runtime.Navigation.AutomaticInputHoldsFlightAction)
            {
                directionalInput.Renew(nowUtc);
            }

            lastAutoWalkFailure = string.Empty;
            return false;
        }

        runtime.Navigation.NoteAutoWalkStopped();
        if (runtime.Navigation.AutomaticInputRequestsSubmarineDive)
        {
            runtime.Navigation.Suspend("native dive input failed");
            speak($"Automatic dive stopped. {result.Diagnostic}", true);
            log($"Native Steam 2026 automatic dive failed: {result.Diagnostic}");
            lastAutoWalkFailure = result.Diagnostic;
            return true;
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
