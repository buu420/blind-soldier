using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;
using System.Security.Cryptography;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal enum Steam2026FieldNavigationOwnershipDisposition
{
    Active,
    Suspended,
    Reset
}

/// <summary>
/// Worker-owned x64 navigation runtime. All legacy reads remain behind the
/// validated translated address space; only coherent, pointer-free targets
/// reach the shared x86 navigation policies and audio players.
/// </summary>
internal sealed class Steam2026FieldNavigationCoordinator : IDisposable
{
    private static readonly IReadOnlyList<FieldNavigationTarget> NoTargets =
        Array.Empty<FieldNavigationTarget>();

    private readonly AccessibilityConfig config;
    private readonly ILegacyAddressSpace addressSpace;
    private readonly Steam2026ForegroundInputAdapter foregroundInput;
    private readonly Steam2026FieldObjectObservationReader objectReader;
    private readonly Steam2026FieldNavigationObservationReader navigationReader;
    private readonly FieldPositionReader positionReader;
    private readonly FieldNavigationControlReader controlReader;
    private readonly Steam2026FieldAudibleCueStateReader cueReader;
    private readonly FieldNavigationInputReader inputReader;
    private readonly Steam2026FieldLadderObservationReader ladderObservationReader;
    private readonly FieldWalkmeshReader walkmeshReader;
    private readonly FieldBoundaryStateReader boundaryStateReader;
    private readonly FieldNavigationController controller;
    private readonly FieldNavigationGuidanceRepeatGate guidanceRepeatGate = new();
    private readonly NativeFieldNavigationProgressBar navigationProgressBar;
    private readonly IntervalFieldNavigationProgressSink navigationProgressSink;
    private readonly FieldStoryTargetReader storyReader;
    private readonly Steam2026FieldNpcObservationReader npcObservationReader;
    private readonly FieldScriptNavigationCatalog scriptCatalog;
    private readonly FieldScriptLineStateReader lineStateReader;
    private readonly FieldControlledEntityReader controlledEntityReader;
    private readonly FieldScriptNavigationTransitionTracker transitionTracker = new();
    private readonly Steam2026FailClosedFieldRoutePlanner routePlanner;
    private readonly ReachableFieldExitTargetProvider reachableExitProvider;
    private readonly FieldExitLabelResolver exitLabelResolver;
    private readonly FieldExitPresentationPolicy exitPresentationPolicy;
    private readonly Steam2026FieldExitSpatialCoordinator exitSpatial;
    private readonly Steam2026FieldLadderSpatialCoordinator ladderSpatial;
    private readonly SwingingBarTimingCueTracker swingingBarTimingCueTracker = new();
    private readonly ImmediateWaveCuePlayer? swingingBarTimingCuePlayer;
    private readonly SquatMinigameCueCoordinator squatMinigameCueCoordinator;
    private readonly JunonMinigameCueCoordinator junonMinigameCueCoordinator;
    private readonly JunonTimingCueTracker junonTimingCueTracker = new();
    private readonly JunonTimingCuePlayer? junonTimingCuePlayer;
    private readonly JunonParadeAlignmentAssist junonParadeAlignmentAssist;
    private readonly Floor60SoldierTurnCueTracker floor60SoldierTurnCueTracker;
    private readonly Floor60GuardTimingStateReader floor60GuardTimingStateReader;
    private readonly ImmediateWaveCuePlayer? floor60ActionCuePlayer;
    private readonly NavigationBeaconPlayer? floor60StatueBeaconPlayer;

    // The same field activity readout the Reloaded runtime has always run - the Temple
    // clock's hands and bridges, the rolling corridor, the chase chamber, the pillars,
    // the altar, the dig, the Corel handcar, the cliff, the wind and the Junon rooms.
    // This runtime never built its observation, so none of those rooms spoke here.
    private readonly FieldActivityStateReader fieldActivityStateReader;
    private readonly FieldActivityReadout fieldActivityReadout = new();
    private readonly ImmediateWaveCuePlayer? fieldActivityButtonCuePlayer;
    private bool fieldActivityOwnsInput;
    private string? fieldActivityCurrentLine;

    // The Temple clock's newest reading, owed until it is said: a newer reading replaces it,
    // it waits for other speech to finish, and a refused one is kept only while current.
    private readonly FieldActivityLatestLineDelivery fieldActivityLatestLine = new();
    private readonly Func<bool?> isSpeechPlaying;
    private readonly Steam2026FieldNavigationPendingActionBuffer pendingActions = new();
    private readonly NavigationAutoWalkController autoWalk;

    /// <summary>
    /// Where every automatic direction this runtime drives is delivered. No Windows key
    /// events: this host synthesizes the legacy keyboard state from its own logical
    /// actions, so the keys are marked in that state instead - and numpad 2, which the
    /// control table names for Down, is NVDA's "read current character".
    /// </summary>
    private readonly Steam2026NativeDirectionalInputSink directionalInput;
    private readonly Steam2026FieldExitPublicationGate exitPublicationGate = new();
    private readonly Action<string, bool> speak;
    private readonly Action<string> log;
    private readonly Steam2026FieldFootstepNavigationProbe? probe;
    private IReadOnlyList<FieldNavigationTarget> currentObjects = NoTargets;
    private IReadOnlyList<FieldNavigationTarget> currentStory = NoTargets;
    private IReadOnlyList<FieldNavigationTarget> currentNpcs = NoTargets;
    private IReadOnlyList<FieldNavigationTarget> currentExits = NoTargets;
    private IReadOnlyList<FieldNavigationTarget> currentReachableExits = NoTargets;
    private FieldFootstepCadence currentNavigationCadence = FieldFootstepCadence.Walk;
    private DateTime nextScanUtc = DateTime.MinValue;
    private DateTime lastNavigationSpeechUtc = DateTime.MinValue;
    private DateTime lastFailureLogUtc = DateTime.MinValue;
    private string lastFailureMessage = string.Empty;
    private string lastStateDiagnostic = string.Empty;
    private readonly FieldAutoWalkConvergenceTracker autoWalkConvergence = new();
    private string lastAutoWalkFailure = string.Empty;
    private string lastAutoWalkPace = string.Empty;
    private bool pendingAutoWalkStart;
    private bool autoWalkRouteToggleQueued;

    // The controller navigation menu. The SDL capture hook has already decided,
    // inside the game's own button read, which presses the game may see; this is
    // where the ones it kept become speech and routes.
    private readonly Func<ControllerNavigationCapture?> controllerCapture;
    private ControllerNavigationDispatcher? controllerNavigation;
    private FieldPositionSnapshot controllerPosition;
    private FieldNavigationControlTransform? controllerControl;
    private FieldLadderStateSnapshot controllerLadder;
    private DateTime controllerNowUtc;
    private bool controllerMenuIsOpen;
    private bool junonParadeClaimsFieldInput;
    private int disposed;

    internal Steam2026FieldNavigationCoordinator(
        AccessibilityConfig config,
        ILegacyAddressSpace addressSpace,
        Steam2026ForegroundInputAdapter foregroundInput,
        Steam2026FieldObjectObservationReader objectReader,
        string gameRootDirectory,
        string modDirectory,
        Action<string, bool> speak,
        Action<string> log,
        Steam2026FieldFootstepNavigationProbe? probe = null,
        NavigationProgressController? progressController = null,
        Ff7GameLanguageContext? languageContext = null,
        NavigationAutoWalkController? autoWalk = null,
        Func<ControllerNavigationCapture?>? controllerCapture = null,
        Steam2026NativeDirectionalInputSink? directionalInput = null,
        Func<bool?>? isSpeechPlaying = null)
    {
        // The screen reader's own word on whether it is still speaking; null where it cannot say.
        this.isSpeechPlaying = isSpeechPlaying ?? (static () => null);
        // Never a Win32 sink, even when no sink is supplied: an unattached one refuses
        // presses with a diagnostic the player hears, which is the honest outcome on a
        // host whose keyboard-state overlay is missing.
        this.directionalInput = directionalInput ?? new Steam2026NativeDirectionalInputSink();
        this.controllerCapture = controllerCapture ?? (static () => null);
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(addressSpace);
        this.addressSpace = addressSpace;
        this.foregroundInput = foregroundInput ?? throw new ArgumentNullException(nameof(foregroundInput));
        this.objectReader = objectReader ?? throw new ArgumentNullException(nameof(objectReader));
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);
        this.speak = speak ?? throw new ArgumentNullException(nameof(speak));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.probe = probe;
        this.autoWalk = autoWalk ?? NavigationAutoWalkController.CreateCurrentProcess(
            addressSpace,
            this.directionalInput);

        int ReadInt32(int address) => ReadCheckedInt32(addressSpace, address);
        short ReadInt16(int address) => ReadCheckedInt16(addressSpace, address);
        ushort ReadUInt16(int address) => ReadCheckedUInt16(addressSpace, address);
        byte ReadByte(int address) => ReadCheckedByte(addressSpace, address);
        uint ReadUInt32(int address) => ReadCheckedUInt32(addressSpace, address);

        positionReader = new FieldPositionReader(addressSpace);
        controlReader = new FieldNavigationControlReader(addressSpace);
        cueReader = new Steam2026FieldAudibleCueStateReader(addressSpace);
        navigationReader = new Steam2026FieldNavigationObservationReader(addressSpace);
        inputReader = new FieldNavigationInputReader(ReadUInt32);
        var ladderReader = new FieldLadderStateReader(ReadInt32, ReadUInt16, ReadByte);
        ladderObservationReader = new Steam2026FieldLadderObservationReader(
            positionReader.ReadNavigation,
            ladderReader.Read,
            () => addressSpace.TryReadUInt32(
                (uint)FieldNavigationObjectReader.AddressFieldEventDataPtr,
                out var eventTable)
                    ? eventTable
                    : null);
        walkmeshReader = new FieldWalkmeshReader(ReadInt32, ReadInt16);
        boundaryStateReader = new FieldBoundaryStateReader(addressSpace);
        var dynamicObstacleReader = new FieldNavigationDynamicObstacleReader(
            ReadInt32,
            ReadInt16,
            ReadByte);
        lineStateReader = new FieldScriptLineStateReader(addressSpace);
        controlledEntityReader = new FieldControlledEntityReader(addressSpace);
        squatMinigameCueCoordinator = new SquatMinigameCueCoordinator(
            new SquatMinigameStateReader(addressSpace));
        junonMinigameCueCoordinator = new JunonMinigameCueCoordinator(
            new JunonMinigameStateReader(addressSpace));
        junonTimingCuePlayer = config.EnableJunonMinigamePrompts && config.EnableJunonTimingCue
            ? new JunonTimingCuePlayer(
                ResolveConfiguredPath(modDirectory, config.JunonTimingCueSoundPath,
                    @"Assets\navigation\swing_jump_058.wav"),
                config.JunonTimingCueVolumePercent,
                log)
            : null;
        // Use the host's native keyboard-state overlay, as ordinary autowalk does.
        // Windows SendInput does not reach this runtime's synthesized DIK buffer.
        junonParadeAlignmentAssist = new JunonParadeAlignmentAssist(
            HighwayAutoSteeringController.CreateCurrentProcess(addressSpace, this.directionalInput),
            log,
            new FieldWalkmeshRoutePlanner(
                walkmeshReader,
                boundaryStateReader,
                dynamicObstacleProvider: dynamicObstacleReader.Read));
        floor60GuardTimingStateReader = new Floor60GuardTimingStateReader(addressSpace);
        var language = languageContext ?? Ff7GameLanguageDetector.Detect(
            gameRootDirectory,
            config.GameLanguage,
            log: log);
        scriptCatalog = new FieldScriptNavigationCatalog(gameRootDirectory, language);

        routePlanner = new Steam2026FailClosedFieldRoutePlanner(
            new FieldWalkmeshRoutePlanner(
                walkmeshReader,
                boundaryStateReader,
                ReadLiveTransitions,
                dynamicObstacleReader.Read));
        reachableExitProvider = new ReachableFieldExitTargetProvider(
            _ => currentExits,
            routePlanner);

        var textResolver = new FlevelFieldTextResolver(gameRootDirectory, language);
        var mapNames = new FieldMapNameCatalog(scriptCatalog, textResolver);
        var mapNameReader = new FieldMapNameReader(
            (address, length) => ReadEncodedText(addressSpace, address, length));
        exitLabelResolver = new FieldExitLabelResolver(
            fieldId => mapNames.Read(fieldId),
            mapNameReader.Read);
        exitPresentationPolicy = new FieldExitPresentationPolicy(
            ReadKalmTownComplete,
            new WutaiBellDoorStateReader(addressSpace).ReadDoorOpen);
        storyReader = new FieldStoryTargetReader(
            ReadInt32,
            ReadInt16,
            ReadByte,
            FieldStoryEventCatalog.CreateAllFields(),
            lineStateReader.IsEnabled);
        var fieldNavigationObjects = FieldNavigationObjectCatalog.CreateAllFields();
        var npcReader = new FieldNavigationNpcReader(
            ReadInt32,
            ReadInt16,
            ReadByte,
            textResolver.ReadMessageLinesById,
            fieldId => scriptCatalog.ReadField(fieldId).Npcs,
            fieldNavigationObjects.Select(definition => (definition.FieldId, definition.EntityId)),
            lineStateReader.IsEnabled);
        npcObservationReader = new Steam2026FieldNpcObservationReader(
            positionReader.ReadNavigation,
            npcReader.ReadTargets,
            () => addressSpace.TryReadUInt32(
                (uint)FieldNavigationObjectReader.AddressFieldEventDataPtr,
                out var eventTable)
                    ? eventTable
                    : null);
        navigationProgressBar = new NativeFieldNavigationProgressBar(log);
        navigationProgressSink = new IntervalFieldNavigationProgressSink(
            navigationProgressBar,
            progressController ?? new NavigationProgressController(
                config.EnableNavigationProgressIndicators,
                config.NavigationProgressIntervalPercent));
        controller = new FieldNavigationController(
            new FieldNavigationTargetSource(
                Array.Empty<FieldNavigationTarget>(),
                objectTargetProvider: _ => currentObjects,
                storyTargetProvider: _ => currentStory,
                exitTargetProvider: _ => currentReachableExits,
                npcTargetProvider: _ => currentNpcs),
            routePlanner,
            fieldId => FieldNavigationDistanceCalibration.ResolveForNavigation(
                fieldId,
                config.FieldNavigationSpeechDistanceUnitsPerCount,
                currentNavigationCadence,
                probe?.GetFieldSummary(fieldId) ?? default),
            navigationProgressSink);

        // Same native line state on this runtime: the observatory approach is only
        // offered while the game actually has the opening line switched on.
        // A Contact is reached when the game starts the target's Contact script.
        controller.ContactStarted = new FieldContactActivationReader(addressSpace).HasStarted;
        controller.NativeLineIsEnabled = (_, entityId) =>
            lineStateReader.TryRead(entityId, out var lineEnabled) ? lineEnabled : null;
        // The same way round a walkmesh the field does not join up as the Reloaded runtime:
        // out through one exit and back in by another, from the installed field data.
        var crossFieldApproach = new FieldCrossFieldApproachResolver(
            scriptCatalog,
            new FlevelDataSource(gameRootDirectory, language),
            routePlanner,
            position => walkmeshReader.Read(position).Walkmesh);
        controller.CrossFieldApproach = (position, goal, exits) =>
        {
            var plan = crossFieldApproach.Resolve(position, goal, exits);
            log($"Native Steam 2026 field cross-field approach: {crossFieldApproach.LastDiagnostic}");
            return plan;
        };
        exitSpatial = Steam2026FieldExitSpatialCoordinator.Create(config, modDirectory, log);
        ladderSpatial = Steam2026FieldLadderSpatialCoordinator.Create(config, modDirectory, log);
        swingingBarTimingCuePlayer = config.EnableFieldSwingingBarTimingCue
            ? new ImmediateWaveCuePlayer(
                ResolveConfiguredPath(
                    modDirectory,
                    config.FieldSwingingBarTimingCueSoundPath,
                    @"Assets\navigation\swing_jump_058.wav"),
                config.FieldSwingingBarTimingCueVolumePercent,
                "Native Steam 2026 swinging-bar jump timing cue",
                log)
            : null;
        floor60SoldierTurnCueTracker = new Floor60SoldierTurnCueTracker(
            TimeSpan.FromMilliseconds(Math.Max(0, config.Floor60StatueBeaconIntervalMs)),
            Math.Max(0, config.Floor60StatueArrivalDistanceUnits),
            Floor60SoldierTurnCueTracker.ReactionLeadMillisecondsToTicks(
                config.Floor60GuardReactionLeadMilliseconds));
        var floor60CuePath = ResolveConfiguredPath(
            modDirectory,
            config.Floor60SoldierTurnCueSoundPath,
            @"Assets\navigation\floor60_statue_134.wav",
            @"Assets\navigation\swing_jump_058.wav");
        floor60StatueBeaconPlayer = config.EnableFloor60SoldierTurnCue
            ? new NavigationBeaconPlayer(
                floor60CuePath,
                config.Floor60SoldierTurnCueVolumePercent,
                log)
            : null;
        floor60ActionCuePlayer = config.EnableFloor60SoldierTurnCue
            ? new ImmediateWaveCuePlayer(
                floor60CuePath,
                config.Floor60SoldierTurnCueVolumePercent,
                "Native Steam 2026 floor 60 guard action cue",
                log)
            : null;
        fieldActivityStateReader = new FieldActivityStateReader(addressSpace);
        fieldActivityButtonCuePlayer = config.EnableFieldActivityReadout
            ? new ImmediateWaveCuePlayer(
                ResolveConfiguredPath(
                    modDirectory,
                    config.FieldActivityButtonReadyCueSoundPath,
                    ArcadeCueAssets.FieldActivityButtonReady),
                config.FieldActivityCueVolumePercent,
                "Native Steam 2026 field activity button-ready cue",
                log)
            : null;
        log(
            "Native Steam 2026 field navigation initialized from checked translated " +
            "position/control/walkmesh/boundary/gateway state; keys=U,O,J,L,K,I,P auto walk; " +
            "NPC targets use checked native model/talk/LINE state and native dialogue speaker names; " +
            "routes also honor translated native model collision widths and collision-disable state.");
        log(
            $"Native Steam 2026 swinging-bar timing initialized: " +
            $"enabled={config.EnableFieldSwingingBarTimingCue}, " +
            $"field={SwingingBarTimingCueTracker.SwingingBarFieldId}, " +
            $"bank={SwingingBarTimingCueTracker.FrameCounterBank}, " +
            $"index={SwingingBarTimingCueTracker.FrameCounterIndex}, " +
            $"window={SwingingBarTimingCueTracker.SuccessWindowStart}-" +
            $"{SwingingBarTimingCueTracker.SuccessWindowEnd}.");
        log(
            $"Native Steam 2026 Wall Market squat prompts initialized: " +
            $"enabled={config.EnableSquatMinigamePrompts}, " +
            $"field={SquatMinigameStateReader.GymFieldId}, entity={SquatMinigameStateReader.CloudEntityId}, " +
            $"script={SquatMinigameStateReader.ControllerScriptId}, " +
            $"state=0x{SquatMinigameStateReader.AddressExpectedStep:X8}.");
        log(
            $"Native Steam 2026 Junon minigame prompts initialized: " +
            $"enabled={config.EnableJunonMinigamePrompts}, " +
            $"paradeAlignmentAssist={config.EnableJunonParadeAlignmentAssist}, " +
            $"fields={JunonMinigameStateReader.CprFieldId}," +
            $"{JunonMinigameStateReader.WelcomeParadeFieldId}," +
            $"{JunonMinigameStateReader.SendOffFieldId}, " +
            $"temporaryBank=0x{JunonMinigameStateReader.AddressTemporaryFieldBank:X8}.");
        log(
            $"Native Steam 2026 floor 60 guard accessibility initialized: " +
            $"enabled={config.EnableFloor60SoldierTurnCue}, " +
            $"field={Floor60SoldierTurnCueTracker.FloorId}, " +
            $"statues={string.Join(';', Floor60SoldierTurnCueTracker.HideSpots.Select(spot => $"{spot.SequenceIndex}:{spot.X},{spot.Y},t{spot.TriangleId}"))}, " +
            $"firstLines={string.Join(',', Floor60SoldierTurnCueTracker.FirstLineEntityIds)}, " +
            $"secondLines={string.Join(',', Floor60SoldierTurnCueTracker.SecondLineEntityIds)}, " +
            $"intervalMs={Math.Max(0, config.Floor60StatueBeaconIntervalMs)}, " +
            $"arrival={Math.Max(0, config.Floor60StatueArrivalDistanceUnits)}, " +
            $"reactionLeadMs={Math.Max(0, config.Floor60GuardReactionLeadMilliseconds)}, " +
            $"reactionLeadTicks={Floor60SoldierTurnCueTracker.ReactionLeadMillisecondsToTicks(config.Floor60GuardReactionLeadMilliseconds)}.");
        log(
            $"Native Steam 2026 field activity readout initialized: " +
            $"enabled={config.EnableFieldActivityReadout}, " +
            $"buttonCue={fieldActivityButtonCuePlayer is not null}, " +
            $"clockField={FieldActivityReadout.ClockRoomFieldId}, " +
            $"clockBridges={string.Join(',', FieldActivityReadout.ObservedBoundaryTriangles(FieldActivityReadout.ClockRoomFieldId))}.");
    }

    /// <summary>
    /// What the current field activity is showing, for the repeat key, or null when this
    /// field has none. Asking again with a clock in front of the player means asking
    /// where its hands are now, not hearing the last line of dialogue again.
    /// </summary>
    internal string? FieldActivityCurrentLine => fieldActivityCurrentLine;

    /// <summary>
    /// Tells the capture what the world looks like and does whatever it queued.
    ///
    /// <para>The capture is fetched through a delegate rather than held, because SDL
    /// is loaded when the host first looks for a pad and that is often long after
    /// this coordinator exists. Constructing with a null and never looking again is
    /// how the menu ends up permanently dead on a session that started early.</para>
    /// </summary>
    private void DrainControllerNavigation(bool moduleSupportsNavigation, bool isForeground)
    {
        var capture = controllerCapture();
        if (capture is null)
        {
            controllerMenuIsOpen = false;
            return;
        }

        capture.PublishContext(
            ControllerNavigationDomain.Field,
            isForeground,
            moduleSupportsNavigation && config.EnableFieldNavigationAssistant,
            gameIsBusy: false,
            controllerNowUtc,
            // The field is the identity: a selection made in one room must not be
            // applied in the next.
            identity: controllerPosition.FieldId);

        controllerNavigation ??= new ControllerNavigationDispatcher(
            new ControllerNavigationServices(
                () => controller.BeaconEnabled,
                action => controller
                    .HandleAction(action, controllerPosition, controllerControl, controllerLadder)?.Speech,
                () => autoWalk.IsEnabledFor(NavigationAutoWalkDomain.Field),
                () =>
                {
                    if (!autoWalk.TryStart(NavigationAutoWalkDomain.Field, routeActive: true))
                    {
                        return false;
                    }

                    autoWalkConvergence.Reset();
                    controller.NoteAutoWalkStarted();
                    return true;
                },
                StopEveryControllerAutoWalk,
                () => autoWalk.Suspend(),
                () => controller.IsHoldingForNativeBoundary,
                controller.RequestAutoWalkForHeldRoute),
            speech => { Speak(speech, interrupt: true, controllerNowUtc, "controller"); return true; },
            log);

        _ = controllerNavigation.Drain(capture, ControllerNavigationDomain.Field, controllerNowUtc);
        controllerMenuIsOpen = capture.IsOpen;
    }

    /// <summary>
    /// Ends the automatic walk whatever domain owns it, and clears the keyboard
    /// path's pending start with it.
    ///
    /// <para>A is spoken guidance, and spoken guidance means the mod stops driving;
    /// a stop pressed as the module changes must still reach the walk that is
    /// actually running rather than only this adapter's domain.</para>
    /// </summary>
    private void StopEveryControllerAutoWalk()
    {
        if (autoWalk.Enabled)
        {
            _ = autoWalk.Stop();
        }

        autoWalkConvergence.Reset();
        pendingAutoWalkStart = false;
        autoWalkRouteToggleQueued = false;
        controller.NoteAutoWalkStopped();
    }

    internal void Observe(RuntimeFrameObservation frame, DateTime nowUtc)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(frame);

        // The world-map coordinator owns these same six keys while module 3 is
        // active. Exactly one owner samples each frame, so a rising edge cannot
        // be consumed by the inactive field controller before world navigation
        // sees it.
        var observedActions = frame.Lifecycle.ModuleId == WorldMapStateReader.WorldModule
            ? Array.Empty<FieldNavigationAction>()
            : Steam2026FieldNavigationKeyRouter.ReadActions(
                foregroundInput.ObserveRisingEdge,
                observeLimitKey:
                    frame.Lifecycle.ModuleId == FieldPositionReader.FieldModule);
        var autoWalkToggleRequested = frame.Lifecycle.ModuleId != WorldMapStateReader.WorldModule &&
            NavigationAutoWalkKeyRouter.ObserveToggle(foregroundInput.ObserveRisingEdge) &&
            frame.Lifecycle.ModuleId == FieldPositionReader.FieldModule;
        ObserveSwingingBarTimingCue(frame, nowUtc);
        ObserveSquatMinigameCue(frame, nowUtc);
        ObserveJunonMinigameCues(frame, nowUtc);
        ObserveFloor60SoldierTurnCue(frame, nowUtc);
        ObserveFieldActivity(frame, nowUtc);
        var navigationEnabled = config.EnableFieldNavigationAssistant;
        var ownershipDisposition = ResolveOwnershipDisposition(
            navigationEnabled,
            config.EnableFieldExitProximityCues,
            frame.Lifecycle.IsForeground,
            frame.Lifecycle.IsShuttingDown,
            frame.Lifecycle.ModuleId,
            foregroundInput.IsCurrentProcessForeground(),
            config.EnableFieldLadderProximityCues,
            config.EnableFieldSwingingBarTimingCue ||
            config.EnableSquatMinigamePrompts ||
            config.EnableJunonMinigamePrompts ||
            config.EnableJunonParadeAlignmentAssist ||
            config.EnableFloor60SoldierTurnCue ||
            config.EnableFieldActivityReadout);
        if (observedActions.Count != 0)
        {
            LogInputDiagnostic(
                $"sampled={string.Join(',', observedActions)}, ownership={ownershipDisposition}");
        }

        if (ownershipDisposition != Steam2026FieldNavigationOwnershipDisposition.Active)
        {
            if (ownershipDisposition == Steam2026FieldNavigationOwnershipDisposition.Reset)
            {
                Reset();
            }
            else
            {
                Suspend();
            }

            return;
        }

        if (junonParadeClaimsFieldInput)
        {
            pendingActions.Clear();
            pendingAutoWalkStart = false;
            autoWalkRouteToggleQueued = false;
            controller.Reset();
            autoWalk.Reset();
            autoWalkConvergence.Reset();
            guidanceRepeatGate.Reset();
            exitPublicationGate.Reset();
            exitSpatial.Reset();
            ladderSpatial.Reset();
            return;
        }

        if (autoWalkToggleRequested && autoWalk.IsEnabledFor(NavigationAutoWalkDomain.Field))
        {
            _ = autoWalk.Stop();
            controller.NoteAutoWalkStopped();

            // The key press, not the next scan. This coordinator only samples on its own
            // cadence, so an off and on inside one scan interval would otherwise never be
            // seen and the new walk would inherit the old one's deadline.
            autoWalkConvergence.Reset();
            pendingAutoWalkStart = false;
            autoWalkRouteToggleQueued = false;
            Speak("Auto walk off.", interrupt: true, nowUtc, "P toggle");
        }
        else if (autoWalkToggleRequested)
        {
            pendingAutoWalkStart = true;
            autoWalkRouteToggleQueued = false;
        }

        if (autoWalk.IsEnabledFor(NavigationAutoWalkDomain.Field) &&
            observedActions.Any(action => action != FieldNavigationAction.RepeatTarget))
        {
            _ = autoWalk.Stop();
            controller.NoteAutoWalkStopped();
            autoWalkConvergence.Reset();
            Speak("Auto walk off.", interrupt: true, nowUtc, "navigation selection changed");
        }

        if (navigationEnabled)
        {
            pendingActions.Capture(observedActions);
        }
        else
        {
            pendingActions.Clear();
        }

        if (nowUtc < nextScanUtc && pendingActions.Count == 0 && !pendingAutoWalkStart)
        {
            return;
        }

        nextScanUtc = nowUtc + TimeSpan.FromMilliseconds(
            Math.Max(30, config.FieldNavigationScanIntervalMs));
        try
        {
            if (!TryReadCoherentBaseNavigation(
                    requireNavigationInput: navigationEnabled,
                    out var position,
                    out var control,
                    out var cue,
                    out var input,
                    out var ladder,
                    out var isLadderStateCoherent,
                    out var baseDiagnostic))
            {
                exitPublicationGate.Reset();
                exitSpatial.Observe(default, default, NoTargets, true, false, false, nowUtc);
                ladderSpatial.Observe(
                    default,
                    default,
                    Array.Empty<FieldScriptNavigationTransition>(),
                    default,
                    true,
                    false,
                    false,
                    nowUtc);
                LogReadFailure($"base state unavailable: {baseDiagnostic}", nowUtc);
                autoWalk.Suspend();
                autoWalkConvergence.Suspend();
                return;
            }

            currentNavigationCadence = input.IsDirectionalRun
                ? FieldFootstepCadence.Run
                : FieldFootstepCadence.Walk;
            if (cue.IsSuppressed)
            {
                exitPublicationGate.Reset();
                exitSpatial.Observe(position, control, NoTargets, true, true, true, nowUtc);
                ladderSpatial.Observe(
                    position,
                    control,
                    Array.Empty<FieldScriptNavigationTransition>(),
                    ladder,
                    true,
                    true,
                    isLadderStateCoherent,
                    nowUtc);
                if (pendingActions.Count != 0)
                {
                    LogInputDiagnostic(
                        $"discarded={pendingActions.Count}, cue={cue.Reason}, " +
                        cueReader.LastDiagnostic);
                }

                pendingActions.Clear();
                // A native activity that has stopped for a button owns what the player
                // does next, as it does on the Reloaded runtime.
                var navigationSuppressed = fieldActivityOwnsInput || IsNavigationSuppressed(
                    cue,
                    ladder,
                    isLadderStateCoherent);
                if (navigationSuppressed)
                {
                    pendingAutoWalkStart = false;
                    autoWalkRouteToggleQueued = false;
                }
                var liveSpeech = controller.UpdateLiveTracking(
                    position,
                    input,
                    control,
                    navigationSuppressed,
                    Math.Max(0, config.FieldNavigationArrivalDistanceUnits),
                    ladder,
                    observedAt: nowUtc);
                if (!navigationSuppressed && liveSpeech is { } live)
                {
                    guidanceRepeatGate.Reset();
                    Speak(live.Speech, interrupt: true, nowUtc, "native ladder tracking");
                }

                UpdateAutoWalk(
                    position, control, canMove: !navigationSuppressed, nowUtc, input.Direction);

                if (!navigationSuppressed && FieldNavigationSpeechPolicy.IsDue(
                        nowUtc,
                        lastNavigationSpeechUtc,
                        config.FieldNavigationSpeechIntervalMs,
                        config.FieldNavigationRunningSpeechIntervalMs,
                        input.IsDirectionalRun,
                        isSuppressed: false,
                        isForeground: true,
                        hasUsableControl: true,
                        controller.BeaconEnabled))
                {
                    var guidance = controller.CreateSpokenGuidance(
                        position,
                        control,
                        arrivalDistanceUnits: Math.Max(0, config.FieldNavigationArrivalDistanceUnits),
                        predictionHorizonMs: FieldNavigationSpeechPolicy.ResolveIntervalMs(
                            config.FieldNavigationSpeechIntervalMs,
                            config.FieldNavigationRunningSpeechIntervalMs,
                            input.IsDirectionalRun));
                    if (guidance is { } spokenGuidance &&
                        guidanceRepeatGate.ShouldSpeak(spokenGuidance.Speech, nowUtc))
                    {
                        Speak(
                            spokenGuidance.Speech,
                            interrupt: true,
                            nowUtc,
                            "native ladder guidance");
                    }
                }

                LogStateDiagnostic(
                    $"field={position.FieldId}, suppressed={cue.Reason}, " +
                    $"{cueReader.LastDiagnostic}, " +
                    $"ladderTracking={!navigationSuppressed && ladder.IsMounted}",
                    nowUtc);
                return;
            }

            var objectsCoherent = RefreshTargets(position);
            var storyCoherent = TryRefreshStoryTargets(position, out var storyDiagnostic);
            var npcsCoherent = TryRefreshNpcTargets(position, out var npcDiagnostic);
            var nativeExitsCoherent = TryRefreshExitTargets(position, nowUtc, out var exitDiagnostic);
            routePlanner.BeginObservation();
            currentReachableExits = nativeExitsCoherent
                ? reachableExitProvider.ReadTargets(
                    position,
                    positionIsScripted:
                        ladder.IsMounted ||
                        (cue.Module == FieldPositionReader.FieldModule && cue.UserControl != 0))
                : NoTargets;
            var resolvedCoherence = Steam2026FieldNavigationActionGate.ResolveCoherence(
                nativeExitsCoherent,
                storyCoherent,
                npcsCoherent,
                objectsCoherent,
                routePlanner.HadReadFailure);
            var routeCoherent = resolvedCoherence.Route;
            var exitsCoherent = resolvedCoherence.Exits;
            if (!exitsCoherent)
            {
                currentReachableExits = NoTargets;
            }

            exitSpatial.Observe(
                position,
                control,
                currentReachableExits,
                true,
                false,
                exitsCoherent,
                nowUtc);
            var liveTransitions = TryReadLiveTransitions(
                position.FieldId,
                out var transitionDiagnostic);
            ladderSpatial.Observe(
                position,
                control,
                liveTransitions,
                ladder,
                true,
                false,
                isLadderStateCoherent,
                nowUtc,
                controller.PrioritizedLadderTransitionId);

            var domainFailures = new List<string>(4);
            if (!objectsCoherent)
            {
                domainFailures.Add($"objects={objectReader.LastDiagnostic}");
            }

            if (!storyCoherent)
            {
                domainFailures.Add($"story={storyDiagnostic}");
            }

            if (!npcsCoherent)
            {
                domainFailures.Add($"npcs={npcDiagnostic}");
            }

            if (!exitsCoherent)
            {
                var routeDiagnostic = routePlanner.HadReadFailure
                    ? routePlanner.LastDiagnostic
                    : exitDiagnostic;
                domainFailures.Add($"exit-route={routeDiagnostic}");
            }

            if (domainFailures.Count != 0)
            {
                LogReadFailure(
                    $"target domain unavailable: {string.Join("; ", domainFailures)}",
                    nowUtc);
            }
            else
            {
                lastFailureMessage = string.Empty;
            }

            LogStateDiagnostic(
                $"field={position.FieldId}, objects={(objectsCoherent ? currentObjects.Count : -1)}, " +
                $"story={(storyCoherent ? currentStory.Count : -1)}, " +
                $"npcs={(npcsCoherent ? currentNpcs.Count : -1)}, " +
                $"nativeExits={(exitsCoherent ? currentExits.Count : -1)}, " +
                $"reachableExits={currentReachableExits.Count}, transitions={liveTransitions.Count}, " +
                $"cue={cue.Reason}, {cueReader.LastDiagnostic}, " +
                $"storyState={storyDiagnostic}, npcState={npcDiagnostic}, " +
                $"exit={exitDiagnostic}, transition={transitionDiagnostic}",
                nowUtc);
            if (!navigationEnabled)
            {
                controller.Reset();
                autoWalk.Reset();
                autoWalkConvergence.Reset();
                pendingAutoWalkStart = false;
                autoWalkRouteToggleQueued = false;
                guidanceRepeatGate.Reset();
                lastNavigationSpeechUtc = DateTime.MinValue;
                return;
            }

            if (fieldActivityOwnsInput)
            {
                // A native activity that has stopped for a button owns what the player
                // does next. Reading out a walk to somewhere else over the top of it
                // would be telling them to leave a scene that is not going to continue
                // without them, so this frame's navigation presses and any auto walk
                // start are dropped, as the Reloaded runtime drops them; the selected
                // route itself is kept for when the scene moves on.
                if (pendingActions.Count != 0)
                {
                    LogInputDiagnostic($"discarded={pendingActions.Count}, field activity owns input");
                }

                pendingActions.Clear();
                pendingAutoWalkStart = false;
                autoWalkRouteToggleQueued = false;
            }

            var coherence = resolvedCoherence;
            if (FieldNavigationAutoWalkIntent.ShouldQueueAutoWalkToggle(
                    pendingAutoWalkStart,
                    controller.BeaconEnabled,
                    controller.IsHoldingForNativeBoundary,
                    autoWalkRouteToggleQueued))
            {
                pendingActions.Capture([FieldNavigationAction.ToggleBeacon]);
                autoWalkRouteToggleQueued = true;
            }
            if (pendingActions.TryTakeEmergencyBeaconOff(
                    position.FieldId,
                    controller.BeaconEnabled || controller.IsHoldingForNativeBoundary,
                    out var beaconOffAction))
            {
                var beaconOff = controller.HandleAction(beaconOffAction, position, control);
                if (beaconOff is { } beaconOffSpeech)
                {
                    guidanceRepeatGate.Reset();
                    Speak(
                        beaconOffSpeech.Speech,
                        interrupt: true,
                        nowUtc,
                        "action=ToggleBeacon cancellation barrier");
                }
            }

            while (Steam2026FieldNavigationPendingActionExecutor.TryExecuteNext(
                       pendingActions,
                       position.FieldId,
                       controller,
                       routePlanner,
                       position,
                       control,
                       ladder,
                       ref coherence,
                       out var action,
                       out var result))
            {
                LogInputDiagnostic(
                    $"executed={action}, speech={(result is null ? "none" : "present")}");
                if (result is { } actionSpeech)
                {
                    guidanceRepeatGate.Reset();
                    Speak(actionSpeech.Speech, interrupt: true, nowUtc, $"action={action}");
                }

                if (action == FieldNavigationAction.ToggleBeacon && autoWalkRouteToggleQueued)
                {
                    autoWalkRouteToggleQueued = false;
                    // An approach can already have a beacon while its final destination
                    // is still held. Preserve the keyboard walk request across both legs.
                    if (controller.IsHoldingForNativeBoundary)
                    {
                        controller.RequestAutoWalkForHeldRoute();
                    }

                    if (!controller.BeaconEnabled)
                    {
                        pendingAutoWalkStart = false;
                    }
                }
            }

            var heldAutoWalkStart = controller.TryConsumeHeldAutoWalkRequest();
            if ((pendingAutoWalkStart || heldAutoWalkStart) &&
                controller.BeaconEnabled &&
                autoWalk.TryStart(NavigationAutoWalkDomain.Field, routeActive: true))
            {
                autoWalkConvergence.Reset();
                controller.NoteAutoWalkStarted();
                pendingAutoWalkStart = false;
                autoWalkRouteToggleQueued = false;
                Speak("Auto walk on.", interrupt: true, nowUtc, "P toggle");
            }

            controllerPosition = position;
            controllerControl = control;
            controllerLadder = ladder;
            controllerNowUtc = nowUtc;
            DrainControllerNavigation(
                frame.Lifecycle.ModuleId == FieldPositionReader.FieldModule,
                frame.Lifecycle.IsForeground && foregroundInput.IsCurrentProcessForeground());

            var canUpdateLiveTracking = !fieldActivityOwnsInput &&
                Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                    controller.CurrentCategory,
                    controller.BeaconEnabled,
                    coherence with { Route = coherence.Route && !routePlanner.HadReadFailure });
            if (canUpdateLiveTracking)
            {
                var liveSpeech = controller.UpdateLiveTracking(
                    position,
                    input,
                    control,
                    isSuppressed: false,
                    arrivalDistanceUnits: Math.Max(0, config.FieldNavigationArrivalDistanceUnits),
                    ladderState: ladder,
                    observedAt: nowUtc);
                if (liveSpeech is { } live)
                {
                    guidanceRepeatGate.Reset();
                    Speak(live.Speech, interrupt: true, nowUtc, "live tracking");
                }
            }


            UpdateAutoWalk(
                position, control, canMove: canUpdateLiveTracking, nowUtc, input.Direction);

            var canCreateGuidance = !fieldActivityOwnsInput &&
                Steam2026FieldNavigationActionGate.CanUpdateLiveTracking(
                    controller.CurrentCategory,
                    controller.BeaconEnabled,
                    coherence with { Route = coherence.Route && !routePlanner.HadReadFailure });
            if (canCreateGuidance && FieldNavigationSpeechPolicy.IsDue(
                    nowUtc,
                    lastNavigationSpeechUtc,
                    config.FieldNavigationSpeechIntervalMs,
                    config.FieldNavigationRunningSpeechIntervalMs,
                    input.IsDirectionalRun,
                    isSuppressed: false,
                    isForeground: true,
                    hasUsableControl: true,
                    controller.BeaconEnabled))
            {
                var guidance = controller.CreateSpokenGuidance(
                    position,
                    control,
                    arrivalDistanceUnits: Math.Max(0, config.FieldNavigationArrivalDistanceUnits),
                    predictionHorizonMs: FieldNavigationSpeechPolicy.ResolveIntervalMs(
                        config.FieldNavigationSpeechIntervalMs,
                        config.FieldNavigationRunningSpeechIntervalMs,
                        input.IsDirectionalRun));
                if (guidance is { } spokenGuidance &&
                    guidanceRepeatGate.ShouldSpeak(spokenGuidance.Speech, nowUtc))
                {
                    Speak(spokenGuidance.Speech, interrupt: true, nowUtc, "guidance");
                }
            }
        }
        catch (Exception ex)
        {
            autoWalk.Suspend();
            autoWalkConvergence.Suspend();
            exitSpatial.Observe(default, default, NoTargets, true, false, false, nowUtc);
            ladderSpatial.Observe(
                default,
                default,
                Array.Empty<FieldScriptNavigationTransition>(),
                default,
                true,
                false,
                false,
                nowUtc);
            LogReadFailure(ex.Message, nowUtc);
        }
    }

    internal Steam2026NavigationProbeSnapshot CaptureProbeSnapshot(
        RuntimeFrameObservation frame,
        long workerCycle,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Field.Kind != RuntimeDomainUpdateKind.Present ||
            frame.Field.Value is not { } field ||
            !Steam2026FieldFootstepCoordinator.TryCreatePosition(field, out var position))
        {
            return new Steam2026NavigationProbeSnapshot(
                workerCycle,
                nowUtc,
                default,
                Steam2026NavigationProbeAvailability.Unavailable,
                ResolvedTriangle: -1,
                WalkmeshTriangleCount: 0,
                BoundaryFingerprint: string.Empty,
                ActiveBoundaryTriangles: Array.Empty<int>(),
                Controller: default,
                RoutePlannerDiagnostic: routePlanner.LastDiagnostic,
                StateDiagnostic: "coherent field position is unavailable");
        }

        var availability = !config.EnableFieldNavigationAssistant
            ? Steam2026NavigationProbeAvailability.Disabled
            : frame.Lifecycle.IsShuttingDown ||
              !frame.Lifecycle.IsForeground ||
              frame.Lifecycle.ModuleId != FieldPositionReader.FieldModule
                ? Steam2026NavigationProbeAvailability.Unavailable
                : lastStateDiagnostic.Contains("suppressed=", StringComparison.Ordinal)
                    ? Steam2026NavigationProbeAvailability.Suppressed
                    : Steam2026NavigationProbeAvailability.Coherent;
        var resolvedTriangle = -1;
        var triangleCount = 0;
        var boundaryFingerprint = string.Empty;
        IReadOnlyList<int> activeBoundaries = Array.Empty<int>();
        var probeDiagnostic = lastStateDiagnostic;

        if (probe?.HasPendingFootstep == true)
        {
            try
            {
                var walkmesh = walkmeshReader.Read(position);
                if (!walkmesh.IsUsable || walkmesh.Walkmesh is null)
                {
                    availability = Steam2026NavigationProbeAvailability.Incoherent;
                    probeDiagnostic = AppendDiagnostic(probeDiagnostic, walkmesh.Diagnostic);
                }
                else
                {
                    triangleCount = walkmesh.Walkmesh.Triangles.Count;
                    resolvedTriangle = FieldWalkmeshPathfinder.ResolveTriangle(
                        walkmesh.Walkmesh,
                        position.X,
                        position.Y,
                        position.Z,
                        preferredTriangleIndex: -1);
                    var boundary = boundaryStateReader.Read(position, triangleCount);
                    if (!boundary.IsUsable)
                    {
                        availability = Steam2026NavigationProbeAvailability.Incoherent;
                        probeDiagnostic = AppendDiagnostic(probeDiagnostic, boundary.Diagnostic);
                    }
                    else
                    {
                        activeBoundaries = boundary.State.ActiveBoundaryTriangles;
                        boundaryFingerprint =
                            $"{triangleCount}:" +
                            Convert.ToHexString(
                                SHA256.HashData(boundary.State.Bits.ToArray()));
                        probeDiagnostic = AppendDiagnostic(
                            probeDiagnostic,
                            $"{walkmesh.Diagnostic}, {boundary.Diagnostic}");
                    }
                }
            }
            catch (Exception ex)
            {
                availability = Steam2026NavigationProbeAvailability.Faulted;
                probeDiagnostic = AppendDiagnostic(
                    probeDiagnostic,
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        return new Steam2026NavigationProbeSnapshot(
            workerCycle,
            nowUtc,
            position,
            availability,
            resolvedTriangle,
            triangleCount,
            boundaryFingerprint,
            activeBoundaries,
            controller.CreateProbeSnapshot(position),
            routePlanner.LastDiagnostic,
            probeDiagnostic);
    }

    internal static bool ShouldOwnField(
        bool enableNavigationAssistant,
        bool enableExitProximityCues,
        bool isLifecycleForeground,
        bool isShuttingDown,
        int moduleId,
        bool isProcessForeground,
        bool enableLadderProximityCues = false) =>
        ResolveOwnershipDisposition(
            enableNavigationAssistant,
            enableExitProximityCues,
            isLifecycleForeground,
            isShuttingDown,
            moduleId,
            isProcessForeground,
            enableLadderProximityCues) == Steam2026FieldNavigationOwnershipDisposition.Active;

    internal static Steam2026FieldNavigationOwnershipDisposition ResolveOwnershipDisposition(
        bool enableNavigationAssistant,
        bool enableExitProximityCues,
        bool isLifecycleForeground,
        bool isShuttingDown,
        int moduleId,
        bool isProcessForeground,
        bool enableLadderProximityCues = false,
        bool enableSwingingBarTimingCue = false)
    {
        if ((!enableNavigationAssistant &&
             !enableExitProximityCues &&
             !enableLadderProximityCues &&
             !enableSwingingBarTimingCue) ||
            isShuttingDown ||
            moduleId == TitleMenuCursorReader.TitleModule)
        {
            return Steam2026FieldNavigationOwnershipDisposition.Reset;
        }

        return isLifecycleForeground &&
               isProcessForeground &&
               moduleId == FieldPositionReader.FieldModule
            ? Steam2026FieldNavigationOwnershipDisposition.Active
            : Steam2026FieldNavigationOwnershipDisposition.Suspended;
    }

    internal static bool IsNavigationSuppressed(
        FieldAudibleCueState cue,
        FieldLadderStateSnapshot ladder,
        bool isLadderStateCoherent) =>
        FieldNavigationSuppressionPolicy.IsNavigationSuppressed(
            cue,
            ladder,
            isLadderStateCoherent);

    internal void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        // A reset is the session tearing down or the module changing underneath.
        // The menu closes with it, and anything queued but not a stop goes: those
        // selections were about a place the player is no longer in.
        controllerCapture()?.RequestClose(ControllerNavigationDomain.Field);
        controllerMenuIsOpen = false;
        controller.Reset();
        autoWalk.Reset();
        autoWalkConvergence.Reset();
        guidanceRepeatGate.Reset();
        pendingActions.Clear();
        exitSpatial.Reset();
        ladderSpatial.Reset();
        swingingBarTimingCueTracker.Reset();
        squatMinigameCueCoordinator.Reset();
        junonMinigameCueCoordinator.Reset();
        junonParadeAlignmentAssist.Reset("x64 field navigation reset");
        junonTimingCueTracker.Reset();
        junonParadeClaimsFieldInput = false;
        floor60SoldierTurnCueTracker.Reset();
        floor60StatueBeaconPlayer?.StopAll();
        ResetFieldActivity();
        currentObjects = NoTargets;
        currentStory = NoTargets;
        currentNpcs = NoTargets;
        currentExits = NoTargets;
        currentReachableExits = NoTargets;
        exitPublicationGate.Reset();
        nextScanUtc = DateTime.MinValue;
        lastNavigationSpeechUtc = DateTime.MinValue;
        lastFailureLogUtc = DateTime.MinValue;
        lastFailureMessage = string.Empty;
        lastStateDiagnostic = string.Empty;
        lastAutoWalkFailure = string.Empty;
        pendingAutoWalkStart = false;
        autoWalkRouteToggleQueued = false;
    }

    /// <summary>
    /// Stops frame-owned audio and input while retaining the selected beacon and
    /// its native route intent and pointer-free target snapshots across battles,
    /// results, focus loss, or a torn lifecycle sample. The retained field id
    /// remains checked by the controller before any target can be reused.
    /// </summary>
    internal void Suspend()
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        // Losing the foreground or the frame closes the menu too, so nothing held
        // through the gap can act when it comes back.
        controllerCapture()?.RequestClose(ControllerNavigationDomain.Field);
        controllerMenuIsOpen = false;
        pendingActions.Clear();
        autoWalk.Suspend();
        autoWalkConvergence.Suspend();
        pendingAutoWalkStart = false;
        autoWalkRouteToggleQueued = false;
        exitPublicationGate.Reset();
        exitSpatial.Reset();
        ladderSpatial.Reset();
        swingingBarTimingCueTracker.Reset();
        squatMinigameCueCoordinator.Reset();
        junonMinigameCueCoordinator.Reset();
        junonParadeAlignmentAssist.Reset("x64 field navigation suspended");
        junonTimingCueTracker.Reset();
        junonParadeClaimsFieldInput = false;
        floor60SoldierTurnCueTracker.Reset();
        floor60StatueBeaconPlayer?.StopAll();
        ResetFieldActivity();
        nextScanUtc = DateTime.MinValue;
        lastNavigationSpeechUtc = DateTime.MinValue;
        guidanceRepeatGate.Reset();
        lastFailureLogUtc = DateTime.MinValue;
        lastFailureMessage = string.Empty;
        lastStateDiagnostic = string.Empty;
    }

    internal void SynchronizeAutoWalkWithoutFrame()
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        _ = NavigationAutoWalkKeyRouter.ObserveToggle(foregroundInput.ObserveRisingEdge);
        autoWalk.Suspend();
        autoWalkConvergence.Suspend();
        pendingAutoWalkStart = false;
        autoWalkRouteToggleQueued = false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        controller.Reset();
        autoWalk.Dispose();
        guidanceRepeatGate.Reset();
        navigationProgressSink.Dispose();
        navigationProgressBar.Dispose();
        pendingActions.Clear();
        exitSpatial.Dispose();
        ladderSpatial.Dispose();
        swingingBarTimingCuePlayer?.Dispose();
        swingingBarTimingCueTracker.Reset();
        squatMinigameCueCoordinator.Reset();
        junonMinigameCueCoordinator.Reset();
        junonParadeAlignmentAssist.Dispose();
        junonTimingCueTracker.Reset();
        junonTimingCuePlayer?.Dispose();
        junonParadeClaimsFieldInput = false;
        floor60ActionCuePlayer?.Dispose();
        floor60StatueBeaconPlayer?.Dispose();
        floor60SoldierTurnCueTracker.Reset();
        fieldActivityButtonCuePlayer?.Dispose();
        ResetFieldActivity();
        currentObjects = NoTargets;
        currentStory = NoTargets;
        currentNpcs = NoTargets;
        currentExits = NoTargets;
        currentReachableExits = NoTargets;
        exitPublicationGate.Reset();
    }

    /// <param name="observedInput">
    /// The direction the game is actually acting on, from the same coherent base read
    /// the rest of this frame uses. Without it the shared controller cannot tell a held
    /// key that the game swallowed from one it is honouring, so its reassertion never
    /// runs - it only triggers on an observed <c>None</c>, and a null skips the check
    /// entirely. x86 has always passed this; both native call sites omitted it.
    /// </param>
    private void UpdateAutoWalk(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform control,
        bool canMove,
        DateTime nowUtc,
        FieldNavigationInput observedInput)
    {
        if (!autoWalk.IsEnabledFor(NavigationAutoWalkDomain.Field))
        {
            _ = autoWalkConvergence.Observe(
                new FieldAutoWalkConvergenceSample(
                    IsAutoWalkEnabled: false,
                    RouteIdentity: string.Empty,
                    IsHeldByGame: false,
                    Hold: FieldAutoWalkHoldReason.NoRoute,
                    PortalIndex: 0,
                    RemainingDistance: 0d),
                nowUtc);
            return;
        }

        // An open navigation menu holds the party still without giving up the
        // route: the player is choosing where to go, not asking to be carried off
        // the spot while they read the list.
        canMove = canMove && !controllerMenuIsOpen;
        var direction = FieldNavigationInput.None;
        var hasDirection = canMove && controller.TryResolveAutomaticInput(
            position,
            control,
            Math.Max(0, config.FieldNavigationArrivalDistanceUnits),
            out direction);
        StopAutoWalkIfItCannotGetCloser(position, hasDirection, canMove, nowUtc);

        // State changes only, never per frame. A stall is diagnosed by whether a
        // direction was requested and whether the game was acting on one, and neither
        // was previously recorded anywhere.
        var pace = hasDirection
            ? $"requested={direction}, observed={observedInput}"
            : $"requested=none, observed={observedInput}, canMove={canMove}, " +
              $"menuOpen={controllerMenuIsOpen}, hold={controller.LastAutomaticInputHold}";
        if (!string.Equals(pace, lastAutoWalkPace, StringComparison.Ordinal))
        {
            lastAutoWalkPace = pace;
            LogInputDiagnostic($"auto walk pace: {pace}");
        }

        var result = autoWalk.Drive(
            hasDirection ? direction : FieldNavigationInput.None,
            canMove: hasDirection,
            routeActive: controller.BeaconEnabled,
            observedInput: observedInput);
        if (result.Success)
        {
            // Renewed after the drive, never before it, so the renewal always belongs to
            // the direction the controller has just committed to. A direction nobody
            // renews goes inert within half a second without being taken off the shared
            // controller behind its back - stopping, pausing, arriving and opening the
            // menu all release it through Drive on this same frame.
            if (hasDirection)
            {
                directionalInput.Renew(nowUtc);
            }

            lastAutoWalkFailure = string.Empty;
            return;
        }

        if (!string.Equals(result.Diagnostic, lastAutoWalkFailure, StringComparison.Ordinal))
        {
            lastAutoWalkFailure = result.Diagnostic;
            var delivery = directionalInput.Diagnostic;
            LogInputDiagnostic(
                $"auto walk failed closed: {result.Diagnostic}"
                + (delivery.Length == 0 ? string.Empty : $" ({delivery})"));
            Speak("Auto walk stopped because directional input failed.", true, nowUtc, "input failure");
        }
    }

    /// <summary>
    /// The x64 runtime's copy of the Reloaded convergence guard - the same tracker, the
    /// same sample, the same decision, because sharing the class without calling it is not
    /// parity. Root's review said so in as many words.
    /// </summary>
    private void StopAutoWalkIfItCannotGetCloser(
        FieldPositionSnapshot position,
        bool hasDirection,
        bool canMove,
        DateTime nowUtc)
    {
        var guidance = controller.CurrentRouteGuidance;
        var label = controller.CurrentTargetLabel;
        var hold = guidance is null
            ? FieldAutoWalkHoldReason.NoRoute
            : hasDirection
                ? FieldAutoWalkHoldReason.None
                : controller.LastAutomaticInputHold;
        if (!autoWalkConvergence.Observe(
                new FieldAutoWalkConvergenceSample(
                    IsAutoWalkEnabled: true,
                    RouteIdentity: controller.CurrentRouteIdentity,
                    IsHeldByGame: !canMove,
                    Hold: hold,
                    PortalIndex: guidance?.PortalIndex ?? 0,
                    RemainingDistance: guidance?.RemainingDistance ?? 0d),
                nowUtc))
        {
            return;
        }

        autoWalkConvergence.Reset();
        LogInputDiagnostic(
            "auto walk stopped: no meaningful progress for " +
            $"{FieldAutoWalkConvergenceTracker.NoProgressTimeout.TotalSeconds:0} seconds; " +
            $"target={label}, remaining={guidance?.RemainingDistance ?? 0d:0}, " +
            $"portal={guidance?.PortalIndex ?? -1}, hold={hold}, canMove={canMove}, " +
            $"direction={hasDirection}, input={autoWalk.LastDiagnostic}, " +
            $"position={position.X},{position.Y}");
        if (!autoWalk.Stop())
        {
            return;
        }

        controller.NoteAutoWalkStopped();
        pendingAutoWalkStart = false;
        autoWalkRouteToggleQueued = false;
        Speak(
            string.IsNullOrWhiteSpace(label)
                ? "Auto walk stopped. Could not get any closer. Navigation is still on."
                : $"Auto walk stopped. Could not get closer to {label}. Navigation is still on.",
            interrupt: true,
            nowUtc,
            "auto walk made no progress");
    }

    private void ObserveSwingingBarTimingCue(
        RuntimeFrameObservation frame,
        DateTime nowUtc)
    {
        if (!config.EnableFieldSwingingBarTimingCue ||
            frame.Lifecycle.IsShuttingDown ||
            !frame.Lifecycle.IsForeground ||
            frame.Lifecycle.ModuleId != FieldPositionReader.FieldModule ||
            !foregroundInput.IsCurrentProcessForeground())
        {
            swingingBarTimingCueTracker.Reset();
            return;
        }

        var moduleAddress = (uint)FieldPositionReader.AddressCurrentModule;
        var fieldAddress = (uint)FieldPositionReader.AddressFieldId;
        var counterAddress =
            (uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
            (uint)SwingingBarTimingCueTracker.FrameCounterIndex;
        var waitingAddress =
            (uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
            (uint)SwingingBarTimingCueTracker.AttemptWaitingIndex;
        var userControlAddress = (uint)FieldAudibleCueStateReader.AddressUserControl;
        var positionResult = positionReader.Read();
        if (!positionResult.IsUsable ||
            !addressSpace.TryReadByte(moduleAddress, out var moduleBefore) ||
            !addressSpace.TryReadUInt16(fieldAddress, out var fieldBefore) ||
            !addressSpace.TryReadByte(counterAddress, out var frameCounter) ||
            !addressSpace.TryReadByte(waitingAddress, out var attemptWaiting) ||
            !addressSpace.TryReadByte(userControlAddress, out var userControl) ||
            !addressSpace.TryReadUInt16(fieldAddress, out var fieldAfter) ||
            !addressSpace.TryReadByte(moduleAddress, out var moduleAfter) ||
            moduleBefore != moduleAfter ||
            fieldBefore != fieldAfter ||
            positionResult.Position.CurrentModule != moduleAfter ||
            positionResult.Position.FieldId != fieldAfter)
        {
            swingingBarTimingCueTracker.Reset();
            return;
        }

        if (!swingingBarTimingCueTracker.Observe(
                moduleAfter,
                fieldAfter,
                positionResult.Position.X,
                positionResult.Position.Y,
                positionResult.Position.Z,
                attemptWaiting == 1,
                userControl != 0,
                frameCounter))
        {
            return;
        }

        var reason =
            $"field={fieldAfter}, bank={SwingingBarTimingCueTracker.FrameCounterBank}, " +
            $"index={SwingingBarTimingCueTracker.FrameCounterIndex}, frame={frameCounter}, " +
            $"position={positionResult.Position.X},{positionResult.Position.Y},{positionResult.Position.Z}, " +
            $"triangle={positionResult.Position.TriangleId}, waiting={attemptWaiting}, control={userControl}";
        swingingBarTimingCuePlayer?.Play(reason);
        Speak("Jump now.", interrupt: true, nowUtc, "native swinging-bar timing");
    }

    /// <summary>
    /// The field activity readout, fed from this runtime's checked translated reads.
    ///
    /// <para>What is said is decided entirely by the shared readout: a new situation once,
    /// an unanswered wait again on a slow beat, a moving hazard no faster than a listener
    /// can follow, and nothing the screen does not show. This is only the plumbing. It
    /// runs whether or not the party has control, because the clock's own controls are
    /// worked while the field holds the party still.</para>
    /// </summary>
    private void ObserveFieldActivity(
        RuntimeFrameObservation frame,
        DateTime nowUtc)
    {
        if (!config.EnableFieldActivityReadout ||
            frame.Lifecycle.IsShuttingDown ||
            !frame.Lifecycle.IsForeground ||
            frame.Lifecycle.ModuleId != FieldPositionReader.FieldModule ||
            !foregroundInput.IsCurrentProcessForeground())
        {
            ResetFieldActivity();
            return;
        }

        // Most rooms have no activity, so the loaded field is looked at before anything
        // else is read. A torn sample says nothing about the room: whatever was being
        // shown stays as it was until a coherent one arrives.
        if (!addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var loadedField))
        {
            return;
        }

        if (!FieldActivityReadout.HasActivity(loadedField))
        {
            ResetFieldActivity();
            return;
        }

        var positionResult = positionReader.ReadNavigation();
        if (!positionResult.IsUsable || positionResult.Position.FieldId != loadedField)
        {
            return;
        }

        var position = positionResult.Position;
        var fieldId = position.FieldId;

        // The player has control when the field says so and nothing scripted is moving
        // them; an unreadable state is not control.
        var isControlled =
            cueReader.TryRead(out var cue) &&
            cue.Module == FieldPositionReader.FieldModule &&
            cue.UserControl == 0 &&
            cue.MovieActive == 0;
        var gameMoment = addressSpace.TryReadUInt16(
            (uint)FieldNavigationObjectReader.AddressFieldBankBase,
            out var moment)
            ? moment
            : -1;

        // Bank[5] is read once, whole, when this room needs it. A byte that could not be
        // read must not become a zero: a zero is "the wind has dropped" in one room.
        byte[]? temporaryBank = null;
        if (FieldActivityReadout.NeedsTemporaryBank(fieldId) ||
            fieldId == FieldActivityReadout.PillarApproachFieldId)
        {
            var bank = new byte[TemporaryFieldBankLength];
            if (addressSpace.TryRead((uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase, bank))
            {
                temporaryBank = bank;
            }
        }

        var observation = FieldActivityObservationBuilder.Build(
            position,
            fieldActivityStateReader,
            lineStateReader,
            boundaryStateReader,
            controlReader.Read(position),
            isControlled,
            gameMoment,
            temporaryBank is { } gate ? gate[FieldActivityPillarGateIndex] : -1,
            temporaryBank is { } snapshot
                ? index => (uint)index < (uint)snapshot.Length ? snapshot[index] : -1
                : null);
        var activity = fieldActivityReadout.Observe(observation, nowUtc);
        fieldActivityOwnsInput = activity.IsPending;
        fieldActivityCurrentLine = fieldActivityReadout.Describe(observation);

        // The clock's readings replace one another: only the newest is owed, it is said with
        // interruption once other speech has finished, and it is dropped once nothing is
        // current. Every other activity keeps its own queued delivery below.
        // Spoken only while speech is on and the game still has focus at that moment; a muted
        // reading stays owed as the current one and is not counted as said.
        FieldActivityClockHost.Deliver(
            activity,
            fieldActivityReadout,
            fieldActivityLatestLine,
            nowUtc,
            foregroundInput.IsCurrentProcessForeground,
            () => config.EnableSpeech,
            text => TrySpeakLatestActivityLine(text, nowUtc, fieldId),
            isSpeechPlaying);

        if (activity.PlayButtonReadyCue &&
            fieldActivityButtonCuePlayer?.Play($"field={fieldId} activity button ready") != true)
        {
            // A missing asset or a device that will not open must not swallow the fact
            // that the game has stopped for the player. The spoken line still carries it.
            log("Native Steam 2026 field activity button-ready cue could not be played.");
        }

        if (activity.Speech is { } speech && !activity.ReplacesEarlierLine)
        {
            Speak(speech, interrupt: false, nowUtc, $"native field activity {fieldId}");
        }
    }

    /// <summary>
    /// Says the clock's newest reading with interruption. This runtime's speaker throws when
    /// Prism refuses a line; a refusal is not delivery, so the line stays owed.
    /// </summary>
    private bool TrySpeakLatestActivityLine(string text, DateTime nowUtc, int fieldId)
    {
        try
        {
            Speak(text, interrupt: true, nowUtc, $"native field activity {fieldId}");
            return true;
        }
        catch (Exception ex)
        {
            log($"Native Steam 2026 field activity {fieldId} line was not delivered: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Something other than the clock's own reading was just spoken - dialogue, the repeat key,
    /// anything else. The clock's next reading waits for it instead of cutting it off.
    /// </summary>
    internal void NoteSpeechDelivered(string text, DateTime nowUtc) =>
        fieldActivityLatestLine.NoteOtherSpeech(text, nowUtc);

    private void ResetFieldActivity()
    {
        fieldActivityReadout.Reset();
        fieldActivityLatestLine.Reset();
        fieldActivityOwnsInput = false;
        fieldActivityCurrentLine = null;
    }

    /// <summary>Bank[5] is the field's 256 temporary bytes.</summary>
    private const int TemporaryFieldBankLength = 256;

    /// <summary>
    /// Bank[5][9], which every pillar's Go script in ancnt1 tests before it will jump.
    /// </summary>
    private const int FieldActivityPillarGateIndex = 9;

    private void ObserveSquatMinigameCue(
        RuntimeFrameObservation frame,
        DateTime nowUtc)
    {
        if (!config.EnableSquatMinigamePrompts ||
            frame.Lifecycle.IsShuttingDown ||
            !frame.Lifecycle.IsForeground ||
            frame.Lifecycle.ModuleId != FieldPositionReader.FieldModule ||
            !foregroundInput.IsCurrentProcessForeground())
        {
            squatMinigameCueCoordinator.Reset();
            return;
        }

        var prompt = squatMinigameCueCoordinator.Observe();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        Speak(prompt, interrupt: true, nowUtc, "native Wall Market squat cue");
    }

    private void ObserveJunonMinigameCues(
        RuntimeFrameObservation frame,
        DateTime nowUtc)
    {
        junonParadeClaimsFieldInput = false;
        if ((!config.EnableJunonMinigamePrompts &&
             !config.EnableJunonParadeAlignmentAssist) ||
            frame.Lifecycle.IsShuttingDown ||
            !frame.Lifecycle.IsForeground ||
            frame.Lifecycle.ModuleId != FieldPositionReader.FieldModule ||
            !foregroundInput.IsCurrentProcessForeground())
        {
            junonMinigameCueCoordinator.Reset();
            junonParadeAlignmentAssist.Reset("x64 Junon host inactive");
            junonTimingCueTracker.Reset();
            return;
        }

        if (!junonMinigameCueCoordinator.TryRead(out var snapshot))
        {
            var unavailable = junonParadeAlignmentAssist.ObserveUnavailable(
                config.EnableJunonParadeAlignmentAssist);
            junonParadeClaimsFieldInput = unavailable.ClaimsFieldInput;
            SpeakJunonParadeAlignmentStep(unavailable, nowUtc);
            return;
        }

        var timingCueNeedsSpeechFallback = PlayJunonTimingCue(snapshot);

        // Release ordinary autowalk before the parade acquires any shared input tokens.
        // Releasing it afterward could erase a key the parade now believes it owns.
        if (config.EnableJunonParadeAlignmentAssist &&
            snapshot.Kind == JunonMinigameKind.WelcomeParade &&
            snapshot.Parade.IsActive)
        {
            autoWalk.Reset();
        }

        var alignment = junonParadeAlignmentAssist.Observe(
            snapshot,
            config.EnableJunonParadeAlignmentAssist);
        junonParadeClaimsFieldInput = alignment.ClaimsFieldInput;

        // Parade frames bypass UpdateAutoWalk, so renew their native input here.
        // Pauses release the owned keys; renewing an empty sink has no effect.
        if (alignment.IsAssistActive)
        {
            directionalInput.Renew(nowUtc);
        }

        SpeakJunonParadeAlignmentStep(alignment, nowUtc);
        if (!config.EnableJunonMinigamePrompts)
        {
            junonMinigameCueCoordinator.Reset();
            return;
        }

        foreach (var cue in junonMinigameCueCoordinator.ObserveSnapshot(
                     snapshot,
                     alignment.IsAssistActive))
        {
            if (string.IsNullOrWhiteSpace(cue.Text))
            {
                continue;
            }

            Speak(cue.Text, cue.Interrupt, nowUtc, "native Junon minigame cue");
        }

        if (timingCueNeedsSpeechFallback)
        {
            Speak("Now.", interrupt: true, nowUtc, "Junon timing sound unavailable");
        }
    }

    private bool PlayJunonTimingCue(JunonMinigameSnapshot snapshot)
    {
        if (!config.EnableJunonMinigamePrompts || !config.EnableJunonTimingCue)
        {
            junonTimingCueTracker.Reset();
            return false;
        }

        if (!junonTimingCueTracker.Observe(snapshot))
        {
            return false;
        }

        var played = junonTimingCuePlayer?.Play() == true;
        log($"Junon native Now timing cue: sequence={snapshot.Parade.NowPromptSequence}, played={played}.");
        return !played;
    }

    private void SpeakJunonParadeAlignmentStep(
        JunonParadeAlignmentStep step,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(step.Speech))
        {
            return;
        }

        Speak(step.Speech, interrupt: true, nowUtc, "native Junon parade alignment assist");
    }

    private void ObserveFloor60SoldierTurnCue(
        RuntimeFrameObservation frame,
        DateTime nowUtc)
    {
        if (!config.EnableFloor60SoldierTurnCue ||
            frame.Lifecycle.IsShuttingDown ||
            !frame.Lifecycle.IsForeground ||
            frame.Lifecycle.ModuleId != FieldPositionReader.FieldModule ||
            !foregroundInput.IsCurrentProcessForeground())
        {
            floor60SoldierTurnCueTracker.Reset();
            floor60StatueBeaconPlayer?.StopAll();
            return;
        }

        var moduleAddress = (uint)FieldPositionReader.AddressCurrentModule;
        var fieldAddress = (uint)FieldPositionReader.AddressFieldId;
        if (!addressSpace.TryReadByte(moduleAddress, out var moduleBefore) ||
            !addressSpace.TryReadUInt16(fieldAddress, out var fieldBefore) ||
            moduleBefore != FieldPositionReader.FieldModule ||
            fieldBefore != Floor60SoldierTurnCueTracker.FloorId)
        {
            floor60SoldierTurnCueTracker.Reset();
            floor60StatueBeaconPlayer?.StopAll();
            return;
        }

        var barretProgressAddress =
            (uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
            (uint)Floor60SoldierTurnCueTracker.BarretSignalingProgressIndex;
        var tifaProgressAddress =
            (uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
            (uint)Floor60SoldierTurnCueTracker.TifaSignalingProgressIndex;
        var activeAddress =
            (uint)FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
            (uint)Floor60SoldierTurnCueTracker.MinigameActiveIndex;
        var guardsClearedAddress =
            (uint)(FieldNavigationObjectReader.AddressFieldBankBase +
                   0x100 +
                   Floor60SoldierTurnCueTracker.GuardsClearedIndex);
        var userControlAddress = (uint)FieldAudibleCueStateReader.AddressUserControl;
        var positionResult = positionReader.Read();
        if (!positionResult.IsUsable ||
            positionResult.Position.CurrentModule != moduleBefore ||
            positionResult.Position.FieldId != fieldBefore ||
            !addressSpace.TryReadByte(barretProgressAddress, out var barretSignalingProgress) ||
            !addressSpace.TryReadByte(tifaProgressAddress, out var tifaSignalingProgress) ||
            !addressSpace.TryReadByte(activeAddress, out var minigameActive) ||
            !addressSpace.TryReadByte(guardsClearedAddress, out var guardsClearedRaw) ||
            !addressSpace.TryReadByte(userControlAddress, out var userControl) ||
            !lineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.FirstCompletionLineEntityId,
                out var firstCompletionLineEnabled) ||
            !lineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.SecondCompletionLineEntityId,
                out var secondCompletionLineEnabled) ||
            !lineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.FirstLineEntityIds[0],
                out var firstLeftEnabled) ||
            !lineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.FirstLineEntityIds[1],
                out var firstMiddleEnabled) ||
            !lineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.FirstLineEntityIds[2],
                out var firstRightEnabled) ||
            !lineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.SecondLineEntityIds[0],
                out var secondLeftEnabled) ||
            !lineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.SecondLineEntityIds[1],
                out var secondMiddleEnabled) ||
            !lineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.SecondLineEntityIds[2],
                out var secondRightEnabled) ||
            !addressSpace.TryReadUInt16(fieldAddress, out var fieldAfter) ||
            !addressSpace.TryReadByte(moduleAddress, out var moduleAfter) ||
            moduleBefore != moduleAfter ||
            fieldBefore != fieldAfter)
        {
            floor60SoldierTurnCueTracker.Reset();
            floor60StatueBeaconPlayer?.StopAll();
            return;
        }

        var guardsCleared =
            (guardsClearedRaw & Floor60SoldierTurnCueTracker.GuardsClearedMask) != 0;
        var guardTiming = floor60GuardTimingStateReader.Read();
        var decision = floor60SoldierTurnCueTracker.Observe(
                positionResult.Position,
                barretSignalingProgress,
                tifaSignalingProgress,
                minigameActive != 0,
                guardsCleared,
                userControl != 0,
                firstCompletionLineEnabled,
                secondCompletionLineEnabled,
                firstLeftEnabled,
                firstMiddleEnabled,
                firstRightEnabled,
                secondLeftEnabled,
                secondMiddleEnabled,
                secondRightEnabled,
                guardTiming,
                nowUtc);
        if (decision.StopHideSpotBeacon)
        {
            floor60StatueBeaconPlayer?.StopAll();
        }

        var reason =
            $"field={fieldAfter}, position={positionResult.Position.X},{positionResult.Position.Y},{positionResult.Position.Z}, " +
            $"triangle={positionResult.Position.TriangleId}, barretProgress={barretSignalingProgress}, " +
            $"tifaProgress={tifaSignalingProgress}, controlLocked={userControl != 0}, " +
            $"completionLines={firstCompletionLineEnabled},{secondCompletionLineEnabled}, " +
            $"firstLines={firstLeftEnabled},{firstMiddleEnabled},{firstRightEnabled}, " +
            $"secondLines={secondLeftEnabled},{secondMiddleEnabled},{secondRightEnabled}, " +
            $"guardTiming={guardTiming.Diagnostic}";
        if (decision.PlayActionCue)
        {
            floor60ActionCuePlayer?.Play(
                $"cue={decision.SpeechCue}, {reason}");
        }

        if (decision.PlayHideSpotBeacon &&
            decision.HideSpotTarget is { } hideSpot)
        {
            var control = controlReader.Read(positionResult.Position);
            if (control.IsUsable)
            {
                var target = hideSpot.ToNavigationTarget(
                    Math.Max(0, config.Floor60StatueArrivalDistanceUnits));
                var spatialCue = FieldProximitySpatializer.CreateCue(
                    positionResult.Position,
                    target,
                    control.Transform);
                if (spatialCue is not null &&
                    floor60StatueBeaconPlayer?.Play(spatialCue.Value) == true)
                {
                    log(
                        $"Native Steam 2026 floor 60 statue locator played: " +
                        $"statue={hideSpot.SequenceIndex}, target={hideSpot.X},{hideSpot.Y},{hideSpot.Z}, " +
                        $"targetTriangle={hideSpot.TriangleId}, distance={spatialCue.Value.DistanceUnits:0}, {reason}.");
                }
            }
        }

        var speech = decision.SpeechCue switch
        {
            Floor60GuardSpeechCue.FindFirstHidingSpot =>
                "Find the first hiding statue.",
            Floor60GuardSpeechCue.MoveNow =>
                "Move now.",
            Floor60GuardSpeechCue.SignalNow =>
                "Signal now.",
            Floor60GuardSpeechCue.GuardSetPassed =>
                "Guard set passed.",
            Floor60GuardSpeechCue.HidingSpotReached =>
                "Hiding spot reached. Wait for the guards.",
            Floor60GuardSpeechCue.FirstGuardSectionPassed =>
                "First guard section passed. Signal Barret and Tifa when the guards turn.",
            Floor60GuardSpeechCue.SecondGuardSectionPassed =>
                "Second guard section passed.",
            _ => string.Empty
        };
        if (speech.Length != 0)
        {
            Speak(speech, interrupt: true, nowUtc, $"native floor 60 {decision.SpeechCue}");
            log(
                $"Native Steam 2026 floor 60 guard cue announced: " +
                $"cue={decision.SpeechCue}, {reason}.");
        }
    }

    private static string ResolveConfiguredPath(
        string modDirectory,
        string configuredPath,
        string fallbackPath,
        string? replacedLegacyDefault = null)
    {
        var normalizedConfiguredPath = configuredPath?.Replace('/', '\\');
        var path =
            string.IsNullOrWhiteSpace(configuredPath) ||
            (!string.IsNullOrWhiteSpace(replacedLegacyDefault) &&
             string.Equals(
                 normalizedConfiguredPath,
                 replacedLegacyDefault,
                 StringComparison.OrdinalIgnoreCase))
                ? fallbackPath
                : configuredPath;
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(modDirectory, path);
    }

    private bool TryReadCoherentBaseNavigation(
        bool requireNavigationInput,
        out FieldPositionSnapshot position,
        out FieldNavigationControlTransform control,
        out FieldAudibleCueState cue,
        out FieldNavigationInputSnapshot input,
        out FieldLadderStateSnapshot ladder,
        out bool isLadderStateCoherent,
        out string diagnostic)
    {
        position = default;
        control = default;
        cue = default;
        input = default;
        ladder = default;
        isLadderStateCoherent = false;
        diagnostic = "position unavailable";
        var preliminary = positionReader.ReadNavigation();
        if (!preliminary.IsUsable)
        {
            diagnostic = preliminary.Diagnostic;
            return false;
        }

        var controlRead = controlReader.Read(preliminary.Position);
        if (!controlRead.IsUsable)
        {
            diagnostic = $"control unavailable: {controlRead.Diagnostic}";
            return false;
        }

        if (!cueReader.TryRead(out cue))
        {
            diagnostic =
                $"audible-cue state unreadable or changing: {cueReader.LastDiagnostic}";
            return false;
        }

        if (!TryReadNavigationInput(
                requireNavigationInput,
                inputReader.Read,
                out var checkedInput,
                out var inputDiagnostic))
        {
            diagnostic = inputDiagnostic;
            return false;
        }

        var confirmation = positionReader.ReadNavigation();
        if (!HasSameFieldOwnership(preliminary, confirmation))
        {
            diagnostic = "field/model ownership changed during base navigation read";
            return false;
        }

        position = confirmation.Position;
        if (cue.Module != position.CurrentModule)
        {
            diagnostic = $"audible-cue module {cue.Module} does not own field {position.CurrentModule}";
            return false;
        }

        control = controlRead.Transform;
        input = checkedInput;
        isLadderStateCoherent = ladderObservationReader.TryRead(position, out ladder);
        if (!isLadderStateCoherent)
        {
            ladder = FieldLadderStateSnapshot.NotMounted;
        }

        diagnostic = $"field={position.FieldId}, playerModel={position.ModelIndex}, base coherent";
        return true;
    }

    internal static bool TryReadNavigationInput(
        bool isRequired,
        Func<FieldNavigationInputSnapshot> readInput,
        out FieldNavigationInputSnapshot input,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(readInput);
        input = default;
        if (!isRequired)
        {
            diagnostic = "directional input not required";
            return true;
        }

        try
        {
            input = readInput();
            diagnostic = "directional input coherent";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"directional input unavailable: {ex.Message}";
            return false;
        }
    }

    private bool RefreshTargets(FieldPositionSnapshot position)
    {
        if (objectReader.TryReadNavigationTargets(position, out var targets))
        {
            currentObjects = targets;
            return true;
        }

        currentObjects = NoTargets;
        return false;
    }

    private bool TryRefreshStoryTargets(
        FieldPositionSnapshot position,
        out string diagnostic)
    {
        currentStory = NoTargets;
        try
        {
            currentStory = Floor60NavigationTargetMerger.Merge(
                storyReader.ReadTargets(position),
                floor60SoldierTurnCueTracker.CurrentNavigationTarget);
            diagnostic = $"native={currentStory.Count}";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return false;
        }
    }

    private bool TryRefreshNpcTargets(
        FieldPositionSnapshot position,
        out string diagnostic)
    {
        currentNpcs = NoTargets;
        if (!npcObservationReader.TryRead(position, out var targets))
        {
            diagnostic = npcObservationReader.LastDiagnostic;
            return false;
        }

        currentNpcs = targets;
        diagnostic = npcObservationReader.LastDiagnostic;
        return true;
    }

    private bool TryRefreshExitTargets(
        FieldPositionSnapshot position,
        DateTime nowUtc,
        out string diagnostic)
    {
        currentExits = NoTargets;
        diagnostic = "walkmesh unavailable";
        try
        {
            var gameMomentBefore = ReadCheckedUInt16(
                addressSpace,
                FieldNavigationObjectReader.AddressFieldBankBase);
            var walkmesh = walkmeshReader.Read(position);
            if (!walkmesh.IsUsable || walkmesh.Walkmesh is null)
            {
                exitPublicationGate.ObserveUnavailable(
                    position.FieldId,
                    position.ModelIndex,
                    nowUtc);
                diagnostic = $"{walkmesh.Diagnostic}; publication={exitPublicationGate.LastDiagnostic}";
                return false;
            }

            if (!navigationReader.TryReadSnapshot(
                    walkmesh.Walkmesh.Triangles.Count,
                    out var snapshot))
            {
                exitPublicationGate.ObserveUnavailable(
                    position.FieldId,
                    position.ModelIndex,
                    nowUtc);
                diagnostic =
                    "checked gateway/boundary snapshot unreadable or changing; " +
                    $"publication={exitPublicationGate.LastDiagnostic}";
                return false;
            }

            if (snapshot.Position.FieldId != position.FieldId ||
                snapshot.Position.PlayerModelId != position.ModelIndex)
            {
                exitPublicationGate.ObserveUnavailable(
                    position.FieldId,
                    position.ModelIndex,
                    nowUtc);
                diagnostic =
                    "gateway snapshot belongs to another field/model; " +
                    $"publication={exitPublicationGate.LastDiagnostic}";
                return false;
            }

            var candidateExits = CreateExitTargets(
                position,
                snapshot.Gateways,
                gameMomentBefore);
            var gameMomentAfter = ReadCheckedUInt16(
                addressSpace,
                FieldNavigationObjectReader.AddressFieldBankBase);
            var ownershipAfter = positionReader.ReadNavigation();
            if (gameMomentAfter != gameMomentBefore
                || !ownershipAfter.IsUsable
                || ownershipAfter.Position.FieldId != position.FieldId
                || ownershipAfter.Position.ModelIndex != position.ModelIndex)
            {
                exitPublicationGate.Reset();
                diagnostic = "field/model/game-moment ownership changed during exit scan";
                return false;
            }

            currentExits = exitPublicationGate.Observe(
                position.FieldId,
                position.ModelIndex,
                candidateExits,
                nowUtc);
            diagnostic =
                $"native={currentExits.Count}, candidates={candidateExits.Count}, " +
                $"gateways={snapshot.Gateways.Count}, gameMoment={gameMomentBefore}, " +
                $"publication={exitPublicationGate.LastDiagnostic}";
            return exitPublicationGate.IsStable;
        }
        catch (Exception ex)
        {
            exitPublicationGate.ObserveUnavailable(
                position.FieldId,
                position.ModelIndex,
                nowUtc);
            currentExits = NoTargets;
            diagnostic = $"{ex.Message}; publication={exitPublicationGate.LastDiagnostic}";
            return false;
        }
    }

    private IReadOnlyList<FieldNavigationTarget> CreateExitTargets(
        FieldPositionSnapshot position,
        IReadOnlyList<Steam2026FieldGatewayResearchSnapshot> gateways,
        int gameMoment)
    {
        var targets = gateways.Select(gateway => new FieldNavigationTarget(
            position.FieldId,
            FieldNavigationCategory.Exits,
            "Exit",
            gateway.X,
            gateway.Y,
            gateway.Z,
            CreateGatewayStableId(
                position.FieldId,
                gateway.GatewayIndex,
                gateway.DestinationFieldId),
            DestinationFieldIds: [gateway.DestinationFieldId])).ToList();
        var scriptField = scriptCatalog.ReadField(position.FieldId);
        if (scriptField.IsUsable)
        {
            var enabledScriptExits = scriptField.Exits.Where(exit =>
                exit.TriggerEntityId < 0 || lineStateReader.IsEnabled(exit.TriggerEntityId)).ToArray();
            // A destination whose MAPJUMP the field's own live flags keep from running is not a
            // way out yet (the same guards the legacy runtime applies).
            var guardedScriptExits = FieldScriptExitGuards.Apply(enabledScriptExits, scriptField.ExitGuards, addressSpace);
            targets.AddRange(Steam2026FieldScriptExitPolicy.Filter(
                position.FieldId,
                gameMoment,
                guardedScriptExits));
        }

        return exitPresentationPolicy.Apply(
            exitLabelResolver.Resolve(ApplyExitNavigationProfiles(targets)));
    }

    private bool? ReadKalmTownComplete()
    {
        var address = (uint)(FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 131);
        return addressSpace.TryReadByte(address, out var before) &&
               addressSpace.TryReadByte(address, out var after) &&
               before == after
            ? (before & 0x01) == 0x01
            : null;
    }

    internal static string CreateGatewayStableId(
        int fieldId,
        int gatewayIndex,
        int destinationFieldId) =>
        $"gateway:{fieldId}:{gatewayIndex}:{destinationFieldId}";

    /// <summary>
    /// Applies the per-field exit corrections the legacy runtime has always applied.
    /// </summary>
    /// <remarks>
    /// <para>This was not a missing nicety. The four Honey Bee Inn lobby doors carry
    /// an interaction radius of zero, and <c>FieldWalkmeshNavigation.TryBuildRoute</c>
    /// only attempts its third strategy when that radius is above zero - so the route
    /// build failed and <c>ReachableFieldExitTargetProvider</c> dropped the doors
    /// altogether. They never reached the exits list at all. A player on this runtime
    /// was not told about four doorways that a player on the other one was.</para>
    ///
    /// <para>Purely additive: the catalog widens a radius and adds a detour, and
    /// returns every other target untouched. Applied to the combined gateway and
    /// script list, before labelling and presentation, which is the order the legacy
    /// runtime uses - parity is the reason, and parity is reason enough.</para>
    /// </remarks>
    internal static IReadOnlyList<FieldNavigationTarget> ApplyExitNavigationProfiles(
        IReadOnlyList<FieldNavigationTarget> targets) =>
        targets.Select(FieldExitNavigationProfileCatalog.Apply).ToArray();

    private IReadOnlyList<FieldScriptNavigationTransition> ReadLiveTransitions(int fieldId) =>
        TryReadLiveTransitions(fieldId, out _);

    private IReadOnlyList<FieldScriptNavigationTransition> TryReadLiveTransitions(
        int fieldId,
        out string diagnostic)
    {
        try
        {
            var field = scriptCatalog.ReadField(fieldId);
            if (!field.IsUsable)
            {
                diagnostic = field.Diagnostic;
                return Array.Empty<FieldScriptNavigationTransition>();
            }

            var transitions = transitionTracker.Resolve(
                fieldId,
                field.Transitions,
                transition => lineStateReader.IsEnabled(transition.SourceEntityId),
                controlledEntityReader.IsControlled,
                controlledEntityReader.ReadPartyLeaderCharacter);
            diagnostic = $"live={transitions.Count}";
            return transitions;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return Array.Empty<FieldScriptNavigationTransition>();
        }
    }

    private static bool HasSameFieldOwnership(
        FieldPositionReadResult before,
        FieldPositionReadResult after) =>
        before.IsUsable &&
        after.IsUsable &&
        before.ModelBase != 0 &&
        before.ModelBase == after.ModelBase &&
        before.Position.CurrentModule == FieldPositionReader.FieldModule &&
        after.Position.CurrentModule == before.Position.CurrentModule &&
        after.Position.FieldId == before.Position.FieldId &&
        after.Position.ModelIndex == before.Position.ModelIndex;

    private void Speak(string text, bool interrupt, DateTime nowUtc, string source)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        speak(text, interrupt);
        lastNavigationSpeechUtc = nowUtc;
        log($"Native Steam 2026 field navigation {source}: {text}");
    }

    private void LogReadFailure(string message, DateTime nowUtc)
    {
        if (!config.EnableFieldNavigationDiagnostics)
        {
            return;
        }

        if (!string.Equals(message, lastFailureMessage, StringComparison.Ordinal) ||
            nowUtc - lastFailureLogUtc >= TimeSpan.FromSeconds(5))
        {
            log($"Native Steam 2026 field navigation read failed closed: {message}");
            lastFailureLogUtc = nowUtc;
            lastFailureMessage = message;
        }
    }

    private void LogStateDiagnostic(string diagnostic, DateTime nowUtc)
    {
        if (!config.EnableFieldNavigationDiagnostics ||
            string.Equals(diagnostic, lastStateDiagnostic, StringComparison.Ordinal))
        {
            return;
        }

        log($"Native Steam 2026 field navigation state: {diagnostic}");
        lastStateDiagnostic = diagnostic;
    }

    private void LogInputDiagnostic(string diagnostic)
    {
        if (config.EnableFieldNavigationDiagnostics)
        {
            log($"Native Steam 2026 field navigation input: {diagnostic}");
        }
    }

    private static int ReadCheckedInt32(ILegacyAddressSpace memory, int address) =>
        address >= 0 && memory.TryReadInt32((uint)address, out var value)
            ? value
            : throw new InvalidDataException($"Unreadable translated int32 at 0x{address:X8}.");

    private static string AppendDiagnostic(string current, string addition) =>
        string.IsNullOrWhiteSpace(current)
            ? addition
            : string.IsNullOrWhiteSpace(addition)
                ? current
                : $"{current}; probe={addition}";

    private static short ReadCheckedInt16(ILegacyAddressSpace memory, int address) =>
        address >= 0 && memory.TryReadInt16((uint)address, out var value)
            ? value
            : throw new InvalidDataException($"Unreadable translated int16 at 0x{address:X8}.");

    private static ushort ReadCheckedUInt16(ILegacyAddressSpace memory, int address) =>
        address >= 0 && memory.TryReadUInt16((uint)address, out var value)
            ? value
            : throw new InvalidDataException($"Unreadable translated uint16 at 0x{address:X8}.");

    private static byte ReadCheckedByte(ILegacyAddressSpace memory, int address) =>
        address >= 0 && memory.TryReadByte((uint)address, out var value)
            ? value
            : throw new InvalidDataException($"Unreadable translated byte at 0x{address:X8}.");

    private static uint ReadCheckedUInt32(ILegacyAddressSpace memory, int address) =>
        address >= 0 && memory.TryReadUInt32((uint)address, out var value)
            ? value
            : throw new InvalidDataException($"Unreadable translated uint32 at 0x{address:X8}.");

    private static string ReadEncodedText(
        ILegacyAddressSpace memory,
        int address,
        int length)
    {
        if (address < 0 || length <= 0 || length > 0x10000)
        {
            return string.Empty;
        }

        var bytes = new byte[length];
        return memory.TryRead((uint)address, bytes)
            ? Ff7EncodedTextDecoder.DecodeTerminated(bytes)
            : string.Empty;
    }
}
