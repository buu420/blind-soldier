using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;
using Ff7.Accessibility.Steam2026X64.Runtime.Battle;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;
using Ff7.Accessibility.Steam2026X64.Runtime.Movies;
using Ff7.Accessibility.Steam2026X64.Runtime.NameEntry;
using Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;
using Ff7.Accessibility.Steam2026X64.Runtime.World;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime;

/// <summary>
/// Research-only x64 live loop. It deliberately bypasses the production
/// capability gate while that gate continues to report incomplete parity.
/// </summary>
internal sealed class Steam2026ResearchSession : IDisposable
{
    private static readonly TimeSpan SetupRetryInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RepeatedLogInterval = TimeSpan.FromSeconds(5);

    private readonly Steam2026FingerprintResult fingerprint;
    private readonly ulong moduleBase;
    private readonly ulong moduleImageSize;
    private readonly INativeMemoryReader memory;
    private readonly IReloadedHooks? hooks;
    private readonly AccessibilityConfig config;
    private readonly string modDirectory;
    private readonly string gameWorkingDirectory;
    private readonly string expectedOpeningMoviePath;
    private readonly Ff7GameLanguageContext gameLanguage;
    private readonly Action<string> log;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ManualResetEventSlim resumeGate = new(initialState: true);
    private readonly Thread worker;
    private int started;
    private int disposed;
    private int resetRequested;

    internal Steam2026ResearchSession(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks? hooks,
        AccessibilityConfig config,
        string modDirectory,
        string gameWorkingDirectory,
        string expectedOpeningMoviePath,
        Ff7GameLanguageContext gameLanguage,
        Action<string> log)
    {
        this.fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
        this.moduleBase = moduleBase;
        this.moduleImageSize = moduleImageSize;
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.hooks = hooks;
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.modDirectory = string.IsNullOrWhiteSpace(modDirectory)
            ? throw new ArgumentException("The installed mod directory is required.", nameof(modDirectory))
            : Path.GetFullPath(modDirectory);
        this.gameWorkingDirectory = string.IsNullOrWhiteSpace(gameWorkingDirectory)
            ? throw new ArgumentException("The validated legacy data directory is required.", nameof(gameWorkingDirectory))
            : Path.GetFullPath(gameWorkingDirectory);
        this.expectedOpeningMoviePath = string.IsNullOrWhiteSpace(expectedOpeningMoviePath)
            ? throw new ArgumentException(
                "The exact opening movie path is required.",
                nameof(expectedOpeningMoviePath))
            : Path.GetFullPath(expectedOpeningMoviePath);
        this.gameLanguage = gameLanguage ?? throw new ArgumentNullException(nameof(gameLanguage));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "FFVII Accessibility Steam 2026 research"
        };
    }

    internal void Start()
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return;
        }

        worker.Start();
    }

    internal void Suspend()
    {
        Interlocked.Exchange(ref resetRequested, 1);
        resumeGate.Reset();
        log("Native Steam 2026 research session suspended.");
    }

    internal void Resume()
    {
        Interlocked.Exchange(ref resetRequested, 1);
        resumeGate.Set();
        log("Native Steam 2026 research session resumed.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        cancellation.Cancel();
        resumeGate.Set();
        if (Volatile.Read(ref started) != 0
            && Thread.CurrentThread != worker
            && !worker.Join(TimeSpan.FromSeconds(3)))
        {
            log("Native Steam 2026 research worker did not stop within three seconds.");
        }

        resumeGate.Dispose();
        cancellation.Dispose();
    }

    private void Run()
    {
        Ff7EncodedTextDecoder.SetDefaultLanguage(gameLanguage.Descriptor);
        Steam2026ResearchObservationPump? pump = null;
        Steam2026MenuObservationReader? menuReader = null;
        Steam2026NativeTitleMenuReader? nativeTitleReader = null;
        Steam2026InGameMenuSpeechBridge? inGameMenuBridge = null;
        Steam2026TitleLoadMenuSpeechBridge? titleLoadMenuBridge = null;
        Steam2026NameEntryObservationReader? nameEntryReader = null;
        Steam2026TranslatedMenuHookSet? hookSet = null;
        Steam2026NativeSystemMenuHookSet? nativeSystemMenuHookSet = null;
        Steam2026NativeSystemMenuReader? nativeSystemMenuReader =
            config.EnableNativeSystemMenuSpeech
                ? new Steam2026NativeSystemMenuReader(moduleBase, memory)
                : null;
        Steam2026FieldMessageHookSet? fieldMessageHookSet = null;
        Steam2026AskCursorHookSet? askCursorHookSet = null;
        Steam2026NativeMovieHookSet? movieHookSet = null;
        Steam2026FieldCutsceneHookSet? cutsceneHookSet = null;
        Steam2026FieldCutsceneDescriptionCoordinator? cutsceneDescriptions = null;
        Steam2026FieldDialogueObservationReader? cutsceneDialogueProbe = null;
        Steam2026FieldZoneSpeechCoordinator? fieldZoneSpeechCoordinator = null;
        Steam2026FieldObjectObservationReader? fieldObjectReader = null;
        Steam2026FieldNavigationCoordinator? fieldNavigationCoordinator = null;
        Steam2026WorldMapAccessibilityCoordinator? worldMapAccessibilityCoordinator = null;
        HighwayAccessibilityCoordinator? highwayAccessibilityCoordinator = null;
        Steam2026FieldFootstepNavigationProbe? fieldFootstepNavigationProbe = null;
        Steam2026BattleRendererHookSet? battleRendererHookSet = null;
        Steam2026BattleAccessibilityCoordinator? battleAccessibilityCoordinator = null;
        Steam2026BattleStatusHotkeyReader? battleStatusHotkeyReader = null;
        var hooksPermanentlyDisabled = false;
        var nativeSystemMenuHooksPermanentlyDisabled = false;
        var fieldMessageHooksPermanentlyDisabled = false;
        var askCursorHooksPermanentlyDisabled = false;
        var movieHooksPermanentlyDisabled = false;
        var cutsceneHooksPermanentlyDisabled = false;
        var battleRendererHooksPermanentlyDisabled = false;
        var tracker = new Steam2026RenderedMenuSpeechTracker();
        var nativeSystemMenuSpeech = new Steam2026SystemMenuSpeechCoordinator(
            Steam2026SystemMenuCatalog.CreateEnglish(),
            TimeSpan.FromMilliseconds(
                Math.Max(0, config.NativeSystemMenuHelpDelayMs)));
        var shopMenuSpeechTracker = new ShopMenuSpeechTracker();
        var dialogueIngressSequencer = new Steam2026DialogueIngressSequencer();
        var battleOptions = CreateBattleOptions(config);
        var battleStatusHotkeyController = new BattleStatusHotkeyController();
        var foregroundInput = Steam2026ForegroundInputAdapter.CreateCurrentProcess(fingerprint);
        var navigationProgressController = new NavigationProgressController(
            config.EnableNavigationProgressIndicators,
            config.NavigationProgressIntervalPercent);
        var loggedUnresolvedMenuTexts = new HashSet<string>(StringComparer.Ordinal);
        GameLifecycleObservation? lifecycle = null;
        var nextPumpAttemptUtc = DateTime.MinValue;
        var nextNativeTitleAttemptUtc = DateTime.MinValue;
        var nextHookAttemptUtc = DateTime.MinValue;
        var nextNativeSystemMenuHookAttemptUtc = DateTime.MinValue;
        var lastNativeSystemMenuVerticalNavigationGeneration = 0L;
        var nextFieldMessageHookAttemptUtc = DateTime.MinValue;
        var nextAskCursorHookAttemptUtc = DateTime.MinValue;
        var nextMovieHookAttemptUtc = DateTime.MinValue;
        var nextCutsceneHookAttemptUtc = DateTime.MinValue;
        var nextFieldObjectScanUtc = DateTime.MinValue;
        var nextBattleReaderAttemptUtc = DateTime.MinValue;
        var nextBattleRendererHookAttemptUtc = DateTime.MinValue;
        var lastSetupDiagnostic = string.Empty;
        var lastSetupLogUtc = DateTime.MinValue;
        var lastRuntimeFault = string.Empty;
        var lastRuntimeFaultLogUtc = DateTime.MinValue;
        var startupAnnounced = false;
        string? lastNativeTitleKey = null;
        var nativeTitleMisses = 0;
        var lastNativeTitleDiagnostic = string.Empty;
        var nextNativeTitleDiagnosticUtc = DateTime.MinValue;
        var nextDialoguePipelineDiagnosticUtc = DateTime.MinValue;
        var lastSaveMenuDiagnostic = string.Empty;
        string? lastMessageIngressDiagnostic = null;
        string? lastDialoguePipelineDiagnostic = null;
        var lastCutsceneNarrationFieldId = -1;
        var cutsceneNarrationSpeechTracker =
            new Steam2026CutsceneNarrationSpeechTracker();
        var openingMovieDetected = false;
        var openingMovieActive = false;
        var fieldProbeWorkerCycle = 0L;
        Steam2026FieldFootstepCoordinator? footstepCoordinator = null;
        Steam2026FieldObjectSpatialCoordinator? fieldObjectSpatialCoordinator = null;
        var kernel2TextDatabase = Kernel2TextDatabase.TryCreate(gameLanguage, log);
        var localizer = BlindSoldierLocalizer.Create(gameLanguage.Descriptor, modDirectory, log);
        if (gameLanguage.Language != Ff7GameLanguage.English && config.EnableOpeningMovieAudioTrack)
        {
            log("The packaged opening-movie audio description is English; localized Prism cues remain available as fallback.");
        }

        using var speaker = new PrismNativeSpeaker(log);
        using var output = new Steam2026ResearchAccessibilityOutput(
            speaker,
            config.OpeningMovieAudioTrackPath,
            config.OpeningMovieAudioTrackVolumePercent,
            localizer,
            log);
        var nameEntrySpeechCoordinator = new Steam2026NameEntrySpeechCoordinator(
            config.EnableNameEntryMenuSpeech,
            TimeSpan.FromMilliseconds(750),
            (text, interrupt) => output.Speak(text, interrupt),
            log);
        var nameEntryPromptSpeechCoordinator = new Steam2026NameEntryPromptSpeechCoordinator(
            config.EnableFieldDialogueDrawSpeech,
            TimeSpan.FromMilliseconds(Math.Max(0, config.FieldDialogueDrawStableMs)),
            (text, interrupt) => output.Speak(text, interrupt),
            log);
        var dispatcher = new RuntimeEventDispatcher(
            config,
            output,
            log);

        if (config.EnableFieldFootstepDistanceProbe)
        {
            try
            {
                var probePath = Path.Combine(
                    modDirectory,
                    "Logs",
                    "ff7_steam2026_x64_footstep_navigation_probe.jsonl");
                var probeWriter = new Steam2026JsonlProbeLineWriter(probePath, log);
                try
                {
                    fieldFootstepNavigationProbe =
                        new Steam2026FieldFootstepNavigationProbe(
                            new FieldFootstepDistanceProbe(
                                Math.Max(
                                    1,
                                    config.FieldFootstepDistanceProbeReportSamples)),
                            probeWriter,
                            fingerprint.Identity.Sha256,
                            DateTime.UtcNow,
                            TimeSpan.FromMilliseconds(
                                Math.Max(
                                    250,
                                    Math.Max(
                                        config.FieldFootstepScanIntervalMs,
                                        config.FieldNavigationScanIntervalMs) * 3)),
                            log);
                }
                catch
                {
                    probeWriter.Dispose();
                    throw;
                }

                log(
                    $"Native Steam 2026 footstep/navigation probe is ready: " +
                    $"path={Path.GetFullPath(probePath)}, " +
                    $"reportEvery={Math.Max(1, config.FieldFootstepDistanceProbeReportSamples)} " +
                    $"accepted samples.");
            }
            catch (Exception ex)
            {
                fieldFootstepNavigationProbe?.Dispose();
                fieldFootstepNavigationProbe = null;
                log(
                    $"Native Steam 2026 footstep/navigation probe remains disabled: " +
                    $"{ex.Message}");
            }
        }

        try
        {
            footstepCoordinator = Steam2026FieldFootstepCoordinator.Create(
                config,
                modDirectory,
                gameWorkingDirectory,
                gameLanguage,
                log,
                fieldFootstepNavigationProbe);
            log("Native Steam 2026 field footstep coordinator is ready.");
        }
        catch (Exception ex)
        {
            log($"Native Steam 2026 field footsteps remain disabled: {ex.Message}");
        }

        try
        {
            fieldObjectSpatialCoordinator = Steam2026FieldObjectSpatialCoordinator.Create(
                config,
                modDirectory,
                log);
        }
        catch (Exception ex)
        {
            log($"Native Steam 2026 field object cues remain disabled: {ex.Message}");
        }

        try
        {
            log("Native Steam 2026 research worker started; waiting for translated runtime readiness.");
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    resumeGate.Wait(cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (Interlocked.Exchange(ref resetRequested, 0) != 0)
                {
                    tracker.Reset();
                    nativeSystemMenuSpeech.Reset();
                    nativeSystemMenuReader?.Reset();
                    nativeSystemMenuHookSet?.Dispose();
                    nativeSystemMenuHookSet = null;
                    shopMenuSpeechTracker.Reset();
                    inGameMenuBridge?.Reset();
                    titleLoadMenuBridge?.SetOwnership(false);
                    titleLoadMenuBridge?.ResetIngress();
                    nameEntrySpeechCoordinator.Reset();
                    nameEntryPromptSpeechCoordinator.Reset();
                    lifecycle = null;
                    lastNativeTitleKey = null;
                    nativeTitleMisses = 0;
                    lastNativeTitleDiagnostic = string.Empty;
                    nextNativeTitleDiagnosticUtc = DateTime.MinValue;
                    lastDialoguePipelineDiagnostic = null;
                    nextDialoguePipelineDiagnosticUtc = DateTime.MinValue;
                    movieHookSet?.Dispose();
                    movieHookSet = null;
                    fieldMessageHookSet?.Dispose();
                    fieldMessageHookSet = null;
                    pump?.ResetMessageIngress();
                    pump?.ResetCountdownSpeech();
                    askCursorHookSet?.Dispose();
                    askCursorHookSet = null;
                    pump?.ResetAskCursorIngress();
                    cutsceneHookSet?.Dispose();
                    cutsceneHookSet = null;
                    cutsceneDescriptions?.Reset();
                    lastCutsceneNarrationFieldId = -1;
                    cutsceneNarrationSpeechTracker.Reset();
                    fieldZoneSpeechCoordinator?.Reset();
                    openingMovieActive = false;
                    pump?.ResetCondorBattle();
                    fieldObjectSpatialCoordinator?.Reset("native x64 research reset");
                    fieldNavigationCoordinator?.Reset();
                    worldMapAccessibilityCoordinator?.Reset("native x64 research reset");
                    highwayAccessibilityCoordinator?.Reset("native x64 research reset");
                    nextFieldObjectScanUtc = DateTime.MinValue;
                    battleRendererHookSet?.Dispose();
                    battleRendererHookSet = null;
                    battleAccessibilityCoordinator?.Reset();
                    battleStatusHotkeyController.Reset();
                    footstepCoordinator?.Reset();
                    fieldFootstepNavigationProbe?.ResetCorrelation();
                    dispatcher.Cleanup("during native x64 research reset");
                }

                var now = DateTime.UtcNow;
                var isHostForeground = foregroundInput.IsCurrentProcessForeground();
                // Sample every worker iteration, even when no coherent guest frame is
                // available. A key held through frame recovery therefore cannot become
                // a delayed false rising edge.
                var autoSteeringTogglePressed =
                    foregroundInput.ObserveRisingEdge(0x77);
                if (foregroundInput.ObserveRisingEdge(RepeatLastSpeechController.VirtualKeyR))
                {
                    try
                    {
                        output.RepeatLast();
                    }
                    catch (Exception ex)
                    {
                        LogRuntimeFault(
                            $"Repeat last speech hotkey failed: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }
                }

                foreach (var action in NavigationProgressHotkeyRouter.ReadActions(
                             foregroundInput.ObserveRisingEdge))
                {
                    try
                    {
                        var speech = navigationProgressController.HandleAction(action);
                        log($"Navigation progress control: {speech}");
                        output.Speak(speech, interrupt: true);
                    }
                    catch (Exception ex)
                    {
                        LogRuntimeFault(
                            $"Navigation progress control failed: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }
                }
                if (!startupAnnounced
                    && Steam2026ResearchSpeechPolicy.CanAnnounceStartup(
                        isHostForeground,
                        lifecycle))
                {
                    try
                    {
                        output.Speak("Final Fantasy VII accessibility is active.", interrupt: true);
                        startupAnnounced = true;
                    }
                    catch (Exception ex)
                    {
                        LogRuntimeFault(
                            $"Startup speech is waiting for Prism: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }
                }

                if (pump is null && now >= nextPumpAttemptUtc)
                {
                    nextPumpAttemptUtc = now + SetupRetryInterval;
                    try
                    {
                        var candidatePump = new Steam2026ResearchObservationPump(
                            fingerprint,
                            moduleBase,
                            memory,
                            TimeSpan.FromMilliseconds(
                                Math.Max(100, config.FieldMessageStableMs)),
                            log);
                        var candidateMenuReader = new Steam2026MenuObservationReader(
                            fingerprint,
                            moduleBase,
                            memory,
                            id => kernel2TextDatabase?.ResolveSpellName(id),
                            id => kernel2TextDatabase?.ResolveSpellDescription(id),
                            id => kernel2TextDatabase?.ResolveWeaponName(id),
                            id => kernel2TextDatabase?.ResolveArmorName(id),
                            id => kernel2TextDatabase?.ResolveAccessoryName(id),
                            id => kernel2TextDatabase?.ResolveInventoryObjectName(id),
                            id => kernel2TextDatabase?.ResolveInventoryObjectDescription(id),
                            savemapAddress: SavemapPartyReader.AddressSavemap,
                            resolveMateriaName: id => kernel2TextDatabase?.ResolveMateriaName(id),
                            resolveMateriaDescription: id =>
                                kernel2TextDatabase?.ResolveMateriaDescription(id));
                        var candidateMenuBridge = new Steam2026InGameMenuSpeechBridge(candidateMenuReader);
                        var candidateTitleLoadBridge = new Steam2026TitleLoadMenuSpeechBridge(
                            TimeSpan.FromMilliseconds(
                                Math.Max(0, config.TitleLoadMenuSpeechSettleMs)),
                            candidateMenuReader.TitleLoadSaveFileHasData,
                            candidateMenuReader.ReadTitleLoadGame);
                        var candidateNameEntryReader = new Steam2026NameEntryObservationReader(
                            fingerprint,
                            moduleBase,
                            memory);
                        var sharedFieldAddressSpace =
                            ValidatedTranslatedX86AddressSpaceFactory.Create(
                                fingerprint,
                                moduleBase,
                                memory);
                        var candidateCutsceneDescriptions =
                            new Steam2026FieldCutsceneDescriptionCoordinator(sharedFieldAddressSpace);
                        var candidateCutsceneDialogueProbe =
                            new Steam2026FieldDialogueObservationReader(sharedFieldAddressSpace);
                        var candidateFieldZoneSpeechCoordinator =
                            new Steam2026FieldZoneSpeechCoordinator(sharedFieldAddressSpace);
                        var candidateBattleStatusHotkeyReader =
                            new Steam2026BattleStatusHotkeyReader(sharedFieldAddressSpace);
                        var candidateFieldObjectReader =
                            new Steam2026FieldObjectObservationReader(
                                sharedFieldAddressSpace,
                                id => kernel2TextDatabase?.ResolveInventoryObjectName(id),
                                id => kernel2TextDatabase?.ResolveMateriaName(id),
                                FieldNavigationObjectCatalog.CreateAllFields());
                        Steam2026FieldNavigationCoordinator? candidateFieldNavigation = null;
                        Steam2026WorldMapAccessibilityCoordinator? candidateWorldMapAccessibility = null;
                        HighwayAccessibilityCoordinator? candidateHighwayAccessibility = null;
                        try
                        {
                            candidateFieldNavigation = new Steam2026FieldNavigationCoordinator(
                                config,
                                sharedFieldAddressSpace,
                                foregroundInput,
                                candidateFieldObjectReader,
                                gameWorkingDirectory,
                                modDirectory,
                                (text, interrupt) => output.Speak(text, interrupt),
                                log,
                                fieldFootstepNavigationProbe,
                                navigationProgressController,
                                gameLanguage);
                        }
                        catch (Exception ex)
                        {
                            log($"Native Steam 2026 field navigation remains disabled: {ex.Message}");
                        }
                        try
                        {
                            candidateWorldMapAccessibility =
                                new Steam2026WorldMapAccessibilityCoordinator(
                                    config,
                                    sharedFieldAddressSpace,
                                    foregroundInput,
                                    gameWorkingDirectory,
                                    modDirectory,
                                    (text, interrupt) => output.Speak(text, interrupt),
                                    log,
                                    navigationProgressController);
                        }
                        catch (Exception ex)
                        {
                            log($"Native Steam 2026 world-map accessibility remains disabled: {ex.Message}");
                        }
                        try
                        {
                            candidateHighwayAccessibility = new HighwayAccessibilityCoordinator(
                                config,
                                sharedFieldAddressSpace,
                                modDirectory,
                                (text, interrupt) => output.Speak(text, interrupt),
                                log);
                        }
                        catch (Exception ex)
                        {
                            log($"Native Steam 2026 highway accessibility remains disabled: {ex.Message}");
                        }
                        pump = candidatePump;
                        menuReader = candidateMenuReader;
                        inGameMenuBridge = candidateMenuBridge;
                        titleLoadMenuBridge = candidateTitleLoadBridge;
                        nameEntryReader = candidateNameEntryReader;
                        cutsceneDescriptions = candidateCutsceneDescriptions;
                        cutsceneDialogueProbe = candidateCutsceneDialogueProbe;
                        fieldZoneSpeechCoordinator = candidateFieldZoneSpeechCoordinator;
                        battleStatusHotkeyReader = candidateBattleStatusHotkeyReader;
                        fieldObjectReader = candidateFieldObjectReader;
                        fieldNavigationCoordinator?.Dispose();
                        fieldNavigationCoordinator = candidateFieldNavigation;
                        worldMapAccessibilityCoordinator?.Dispose();
                        worldMapAccessibilityCoordinator = candidateWorldMapAccessibility;
                        highwayAccessibilityCoordinator?.Dispose();
                        highwayAccessibilityCoordinator = candidateHighwayAccessibility;
                        LogSetup(
                            "Translated lifecycle, menu, field-dialogue, zone-name, and field-position readers are ready.",
                            now,
                            ref lastSetupDiagnostic,
                            ref lastSetupLogUtc);
                    }
                    catch (Exception ex)
                    {
                        LogSetup(
                            $"Translated observation readers are not ready: {ex.Message}",
                            now,
                            ref lastSetupDiagnostic,
                            ref lastSetupLogUtc);
                    }
                }

                if (nativeTitleReader is null && now >= nextNativeTitleAttemptUtc)
                {
                    nextNativeTitleAttemptUtc = now + SetupRetryInterval;
                    try
                    {
                        nativeTitleReader = new Steam2026NativeTitleMenuReader(
                            fingerprint,
                            moduleBase,
                            memory);
                        log("Native four-row Steam 2026 title reader is ready.");
                    }
                    catch (Exception ex)
                    {
                        LogSetup(
                            $"Native title reader is not ready: {ex.Message}",
                            now,
                            ref lastSetupDiagnostic,
                            ref lastSetupLogUtc);
                    }
                }

                if (battleOptions.AnyEnabled
                    && battleAccessibilityCoordinator is null
                    && now >= nextBattleReaderAttemptUtc)
                {
                    nextBattleReaderAttemptUtc = now + SetupRetryInterval;
                    try
                    {
                        var battleResolvers = new Steam2026BattleTextResolvers(
                            id => kernel2TextDatabase?.ResolveSpellName(id),
                            id => kernel2TextDatabase?.ResolveSpellDescription(id),
                            id => kernel2TextDatabase?.ResolveItemName(id),
                            id => kernel2TextDatabase?.ResolveItemDescription(id),
                            id => kernel2TextDatabase?.ResolveCommandName(id),
                            id => kernel2TextDatabase?.ResolveInventoryObjectName(id),
                            id => kernel2TextDatabase?.ResolveBattleText(id),
                            id => kernel2TextDatabase?.ResolveBattleActionName(id),
                            id => kernel2TextDatabase?.ResolveBattleActionDescription(id),
                            id => kernel2TextDatabase?.ResolveInventoryObjectDescription(id),
                            language: gameLanguage.Descriptor);
                        battleAccessibilityCoordinator = new Steam2026BattleAccessibilityCoordinator(
                            fingerprint,
                            moduleBase,
                            memory,
                            battleResolvers,
                            battleOptions);
                        log("Native Steam 2026 checked battle accessibility coordinator is ready.");
                    }
                    catch (Exception ex)
                    {
                        LogSetup(
                            $"Native battle accessibility coordinator is not ready: {ex.Message}",
                            now,
                            ref lastSetupDiagnostic,
                            ref lastSetupLogUtc);
                    }
                }

                if (hooks is not null
                    && hookSet is null
                    && !hooksPermanentlyDisabled
                    && now >= nextHookAttemptUtc)
                {
                    nextHookAttemptUtc = now + SetupRetryInterval;
                    if (Steam2026TranslatedMenuHookSet.TryCreate(
                            fingerprint,
                            moduleBase,
                            moduleImageSize,
                            memory,
                            hooks,
                            out var installed,
                            out var diagnostic))
                    {
                        hookSet = installed;
                        titleLoadMenuBridge?.ResetIngress();
                    }

                    LogSetup(
                        diagnostic,
                        now,
                        ref lastSetupDiagnostic,
                        ref lastSetupLogUtc);
                }

                if (config.EnableNativeSystemMenuSpeech
                    && hooks is not null
                    && nativeSystemMenuHookSet is null
                    && !nativeSystemMenuHooksPermanentlyDisabled
                    && now >= nextNativeSystemMenuHookAttemptUtc)
                {
                    nextNativeSystemMenuHookAttemptUtc =
                        now + SetupRetryInterval;
                    if (Steam2026NativeSystemMenuHookSet.TryCreate(
                            fingerprint,
                            moduleBase,
                            moduleImageSize,
                            memory,
                            hooks,
                            out var installed,
                            out var diagnostic))
                    {
                        nativeSystemMenuHookSet = installed;
                        lastNativeSystemMenuVerticalNavigationGeneration = 0;
                        nativeSystemMenuReader?.Reset();
                        nativeSystemMenuSpeech.Reset();
                    }

                    LogSetup(
                        diagnostic,
                        now,
                        ref lastSetupDiagnostic,
                        ref lastSetupLogUtc);
                }

                if (config.EnableRuntimeDialogueSpeech
                    && hooks is not null
                    && pump is not null
                    && fieldMessageHookSet is null
                    && !fieldMessageHooksPermanentlyDisabled
                    && now >= nextFieldMessageHookAttemptUtc)
                {
                    nextFieldMessageHookAttemptUtc = now + SetupRetryInterval;
                    if (Steam2026FieldMessageHookSet.TryCreate(
                            fingerprint,
                            moduleBase,
                            moduleImageSize,
                            memory,
                            hooks,
                            dialogueIngressSequencer,
                            out var installed,
                            out var diagnostic))
                    {
                        fieldMessageHookSet = installed;
                        pump.ResetMessageIngress();
                    }

                    LogSetup(
                        diagnostic,
                        now,
                        ref lastSetupDiagnostic,
                        ref lastSetupLogUtc);
                }

                if (config.EnableRuntimeDialogueSpeech
                    && hooks is not null
                    && pump is not null
                    && askCursorHookSet is null
                    && !askCursorHooksPermanentlyDisabled
                    && now >= nextAskCursorHookAttemptUtc)
                {
                    nextAskCursorHookAttemptUtc = now + SetupRetryInterval;
                    if (Steam2026AskCursorHookSet.TryCreate(
                            fingerprint,
                            moduleBase,
                            moduleImageSize,
                            memory,
                            hooks,
                            dialogueIngressSequencer,
                            out var installed,
                            out var diagnostic))
                    {
                        askCursorHookSet = installed;
                        pump.ResetAskCursorIngress();
                    }

                    LogSetup(
                        diagnostic,
                        now,
                        ref lastSetupDiagnostic,
                        ref lastSetupLogUtc);
                }

                if (hooks is not null
                    && movieHookSet is null
                    && !movieHooksPermanentlyDisabled
                    && now >= nextMovieHookAttemptUtc)
                {
                    nextMovieHookAttemptUtc = now + SetupRetryInterval;
                    if (Steam2026NativeMovieHookSet.TryCreate(
                            fingerprint,
                            moduleBase,
                            moduleImageSize,
                            memory,
                            hooks,
                            expectedOpeningMoviePath,
                            out var installed,
                            out var diagnostic))
                    {
                        movieHookSet = installed;
                    }

                    LogSetup(
                        diagnostic,
                        now,
                        ref lastSetupDiagnostic,
                        ref lastSetupLogUtc);
                }

                if (config.EnableFieldCutsceneDescriptions
                    && hooks is not null
                    && cutsceneDescriptions is not null
                    && cutsceneHookSet is null
                    && !cutsceneHooksPermanentlyDisabled
                    && now >= nextCutsceneHookAttemptUtc)
                {
                    nextCutsceneHookAttemptUtc = now + SetupRetryInterval;
                    if (Steam2026FieldCutsceneHookSet.TryCreate(
                            fingerprint,
                            moduleBase,
                            moduleImageSize,
                            memory,
                            hooks,
                            out var installed,
                            out var diagnostic))
                    {
                        cutsceneHookSet = installed;
                    }

                    LogSetup(
                        diagnostic,
                        now,
                        ref lastSetupDiagnostic,
                        ref lastSetupLogUtc);
                }

                if (cutsceneHookSet is not null && cutsceneDescriptions is not null)
                {
                    while (cutsceneHookSet.TryDequeue(out var snapshot))
                    {
                        cutsceneDescriptions.Observe(snapshot);
                    }

                    if (cutsceneDialogueProbe is not null
                        && cutsceneDescriptions.TrySpeakPending(
                            isHostForeground,
                            () => cutsceneDialogueProbe.TryRead(out _),
                            text =>
                            {
                                output.Speak(text, interrupt: false);
                                return true;
                            },
                            now,
                            out var spokenCue))
                    {
                        lastCutsceneNarrationFieldId = spokenCue.FieldId;
                        cutsceneNarrationSpeechTracker.Begin(spokenCue.FieldId);
                        if (config.EnableFieldCutsceneDescriptionDiagnostics)
                        {
                            log(
                                $"Native Steam 2026 cutscene description: "
                                + $"field={spokenCue.FieldId}, entity={spokenCue.EntityId}, "
                                + $"script={spokenCue.ScriptId}, byte={spokenCue.ByteIndex}, "
                                + $"text={spokenCue.Text}");
                        }
                    }

                    if (cutsceneHookSet.IsFatallyDegraded)
                    {
                        log("Translated WAIT/SOUND cutscene ingress degraded; disabling its hooks.");
                        cutsceneHookSet.Dispose();
                        cutsceneHookSet = null;
                        cutsceneHooksPermanentlyDisabled = true;
                        cutsceneDescriptions.Reset();
                        lastCutsceneNarrationFieldId = -1;
                        cutsceneNarrationSpeechTracker.Reset();
                    }
                }

                if (battleOptions.AnyEnabled
                    && hooks is not null
                    && battleAccessibilityCoordinator is not null
                    && battleRendererHookSet is null
                    && !battleRendererHooksPermanentlyDisabled
                    && now >= nextBattleRendererHookAttemptUtc)
                {
                    nextBattleRendererHookAttemptUtc = now + SetupRetryInterval;
                    if (Steam2026BattleRendererHookSet.TryCreate(
                            fingerprint,
                            moduleBase,
                            moduleImageSize,
                            memory,
                            hooks,
                            out var installed,
                            out var diagnostic))
                    {
                        battleRendererHookSet = installed;
                    }

                    LogSetup(
                        diagnostic,
                        now,
                        ref lastSetupDiagnostic,
                        ref lastSetupLogUtc);
                }

                if (fieldMessageHookSet is not null && pump is not null)
                {
                    while (fieldMessageHookSet.TryDequeue(out var messageSnapshot))
                    {
                        pump.ObserveMessageLifecycle(messageSnapshot);
                        var messageIngressDiagnostic =
                            $"field={messageSnapshot.Observation.FieldId}, " +
                            $"window={messageSnapshot.Observation.WindowId}, " +
                            $"dialog={messageSnapshot.Observation.DialogId}, " +
                            $"state={(messageSnapshot.Result != 0 ? "active" : "complete")}";
                        if (!string.Equals(
                                messageIngressDiagnostic,
                                lastMessageIngressDiagnostic,
                                StringComparison.Ordinal))
                        {
                            lastMessageIngressDiagnostic = messageIngressDiagnostic;
                            log(
                                "Native Steam 2026 MESSAGE ingress: " +
                                $"sequence={messageSnapshot.Sequence}, {messageIngressDiagnostic}.");
                        }
                    }

                    if (fieldMessageHookSet.IsFatallyDegraded)
                    {
                        log("Translated MESSAGE lifecycle ingress degraded; disabling its hook.");
                        fieldMessageHookSet.Dispose();
                        fieldMessageHookSet = null;
                        fieldMessageHooksPermanentlyDisabled = true;
                        pump.ResetMessageIngress();
                    }
                }

                if (askCursorHookSet is not null && pump is not null)
                {
                    while (askCursorHookSet.TryDequeue(out var askCursorSnapshot))
                    {
                        pump.ObserveAskCursorCapture(askCursorSnapshot);
                    }

                    if (askCursorHookSet.IsFatallyDegraded)
                    {
                        log("Translated ASK selection ingress degraded; disabling its hook.");
                        askCursorHookSet.Dispose();
                        askCursorHookSet = null;
                        askCursorHooksPermanentlyDisabled = true;
                        pump.ResetAskCursorIngress();
                    }
                }

                if (pump is not null && pump.TryReadFrame(out var frame))
                {
                    lifecycle = frame.Lifecycle;
                    try
                    {
                        var highwayIsForeground =
                            isHostForeground &&
                            frame.Lifecycle.IsForeground &&
                            !frame.Lifecycle.IsShuttingDown;
                        var highwayIsActive =
                            frame.Lifecycle.ModuleId == HighwayStateReader.HighwayModule;
                        var highwayStatusRequested =
                            highwayIsActive &&
                            highwayIsForeground &&
                            foregroundInput.ObserveRisingEdge(0x4B);
                        var autoSteeringToggleRequested =
                            highwayIsActive &&
                            highwayIsForeground &&
                            autoSteeringTogglePressed;
                        highwayAccessibilityCoordinator?.Update(
                            now,
                            highwayIsActive,
                            highwayIsForeground,
                            highwayStatusRequested,
                            autoSteeringToggleRequested);
                    }
                    catch (Exception ex)
                    {
                        highwayAccessibilityCoordinator?.Reset("native x64 highway processing fault");
                        LogRuntimeFault(
                            $"Native highway accessibility reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    try
                    {
                        // Fort Condor. The same shared reader the x86 runtime
                        // uses, so module 9 sounds identical on both.
                        var condorIsForeground =
                            isHostForeground &&
                            frame.Lifecycle.IsForeground &&
                            !frame.Lifecycle.IsShuttingDown;
                        var condorIsActive =
                            frame.Lifecycle.ModuleId == CondorBattleStateReader.CondorModule;

                        // Short-circuited on the module so the highway reader
                        // above never loses its own press to a fort battle.
                        var condorStatusRequested =
                            condorIsActive &&
                            condorIsForeground &&
                            foregroundInput.ObserveRisingEdge(0x4B);

                        foreach (var condorLine in pump.ObserveCondorBattle(
                            frame.Lifecycle.ModuleId,
                            condorStatusRequested,
                            now))
                        {
                            if (config.EnableSpeech && condorIsForeground)
                            {
                                output.Speak(condorLine.Text, condorLine.Interrupt);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogRuntimeFault(
                            $"Native Fort Condor battle reader will retry: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }
                    var dialoguePipelineDiagnostic = pump.LastDialoguePipelineDiagnostic;
                    if (now >= nextDialoguePipelineDiagnosticUtc
                        && !string.Equals(
                            dialoguePipelineDiagnostic,
                            lastDialoguePipelineDiagnostic,
                            StringComparison.Ordinal))
                    {
                        lastDialoguePipelineDiagnostic = dialoguePipelineDiagnostic;
                        nextDialoguePipelineDiagnosticUtc = now + TimeSpan.FromMilliseconds(250);
                        log(
                            "Native Steam 2026 dialogue pipeline: " +
                            dialoguePipelineDiagnostic + ".");
                    }

                    try
                    {
                        ObserveFieldObjectCues(
                            frame,
                            fieldObjectReader,
                            fieldObjectSpatialCoordinator,
                            isHostForeground,
                            now,
                            ref nextFieldObjectScanUtc);
                    }
                    catch (Exception ex)
                    {
                        fieldObjectSpatialCoordinator?.Reset(
                            "field-object observation fault");
                        LogRuntimeFault(
                            $"Native field object processing reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    var currentFieldId = frame.Field.Kind == RuntimeDomainUpdateKind.Present
                        ? frame.Field.Value?.FieldId ?? -1
                        : -1;
                    var estimatedNarrationProtection = cutsceneDescriptions is not null
                        && cutsceneDescriptions.ShouldQueueDialogue(
                            currentFieldId,
                            now);
                    var speechStateAvailable = false;
                    var speechIsActive = false;
                    if (estimatedNarrationProtection)
                    {
                        speechStateAvailable = output.TryIsSpeaking(out speechIsActive);
                    }

                    var suppressDialogue =
                        cutsceneNarrationSpeechTracker.ShouldProtectDialogue(
                            currentFieldId,
                            estimatedNarrationProtection,
                            speechStateAvailable,
                            speechIsActive);
                    if (frame.Dialogue is
                        {
                            Kind: RuntimeDomainUpdateKind.Present,
                            Value: { } pendingDialogue
                        })
                    {
                        _ = pump.MarkDialogueDeliverySuppressed(
                            pendingDialogue,
                            suppressDialogue);
                    }

                    var dispatchFrame = frame with
                    {
                        Dialogue = ApplyCutsceneDialogueSuppression(
                            frame.Dialogue,
                            suppressDialogue)
                    };
                    try
                    {
                        var dialogueAcknowledgement =
                            dispatcher.DispatchWithDialogueAcknowledgement(
                            new RuntimeDispatchBatch(
                                dispatchFrame,
                                Array.Empty<RuntimeEvent>(),
                                null),
                            now);
                        if (dialogueAcknowledgement is not null &&
                            !pump.AcknowledgeDialogueSpeech(dialogueAcknowledgement))
                        {
                            log(
                                "Native Steam 2026 dialogue delivery acknowledgement " +
                                "did not match the retained stable page; speech remains pending.");
                        }
                        else if (dialogueAcknowledgement is null &&
                                 dispatchFrame.Dialogue.Kind == RuntimeDomainUpdateKind.Closed &&
                                 !pump.AcknowledgeDialogueClose())
                        {
                            log(
                                "Native Steam 2026 dialogue close acknowledgement " +
                                "did not match the queued lifecycle boundary; reset remains pending.");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogRuntimeFault(
                            $"Runtime speech dispatch will retry: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    if (config.EnableSpeech
                        && isHostForeground
                        && frame.Lifecycle.IsForeground
                        && !frame.Lifecycle.IsShuttingDown
                        && pump.TryGetPendingCountdown(out var countdownAnnouncement))
                    {
                        try
                        {
                            output.Speak(countdownAnnouncement.Speech, interrupt: true);
                            pump.AcknowledgeCountdown(countdownAnnouncement);
                            log(
                                $"Native Steam 2026 field countdown: " +
                                $"{countdownAnnouncement.Speech} " +
                                $"(remaining={countdownAnnouncement.RemainingSeconds}).");
                        }
                        catch (Exception ex)
                        {
                            LogRuntimeFault(
                                $"Native field-countdown speech will retry: {ex.Message}",
                                now,
                                ref lastRuntimeFault,
                                ref lastRuntimeFaultLogUtc);
                        }
                    }

                    if (config.EnableFieldMessageReader && fieldZoneSpeechCoordinator is not null)
                    {
                        var narrationPending = cutsceneDescriptions?.HasPendingNarration(
                            currentFieldId) == true;
                        var narrationProtected = cutsceneDescriptions?.ShouldQueueDialogue(
                            currentFieldId,
                            now) == true;
                        if (fieldZoneSpeechCoordinator.TryObserve(
                                isHostForeground &&
                                frame.Lifecycle.IsForeground &&
                                !frame.Lifecycle.IsShuttingDown,
                                openingMovieDetected,
                                openingMovieActive,
                                narrationPending,
                                narrationProtected,
                                now,
                                out var zoneSpeech))
                        {
                            try
                            {
                                output.Speak(zoneSpeech.Text, zoneSpeech.Interrupt);
                                if (!fieldZoneSpeechCoordinator.Acknowledge(zoneSpeech))
                                {
                                    log(
                                        "Native Steam 2026 zone-name acknowledgement did not " +
                                        "match the retained field entry; speech remains pending.");
                                }
                                else
                                {
                                    log(
                                        $"Native Steam 2026 zone name: field={zoneSpeech.FieldId} " +
                                        $"text={zoneSpeech.Text}");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogRuntimeFault(
                                    $"Native zone-name speech will retry: {ex.Message}",
                                    now,
                                    ref lastRuntimeFault,
                                    ref lastRuntimeFaultLogUtc);
                            }
                        }
                    }

                    var probeWorkerCycle = ++fieldProbeWorkerCycle;
                    try
                    {
                        footstepCoordinator?.Observe(
                            frame,
                            isHostForeground,
                            now,
                            probeWorkerCycle);
                    }
                    catch (Exception ex)
                    {
                        footstepCoordinator?.Reset();
                        LogRuntimeFault(
                            $"Native field footstep processing reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    Steam2026NavigationProbeSnapshot? navigationProbeSnapshot = null;
                    // Battle status owns L before suspended field navigation samples
                    // the shared U/O/J/L/K/I key set later in this frame.
                    try
                    {
                        var battleQueryActive = false;
                        var battleQueryReadable = battleStatusHotkeyReader is not null
                            && battleStatusHotkeyReader.TryReadBattleQueryActive(
                                out battleQueryActive);
                        var ownsBattleStatusHotkeys = config.EnableSpeech
                            && lifecycle is
                            {
                                IsForeground: true,
                                IsShuttingDown: false,
                                ModuleId: BattleStateReader.BattleModule
                            }
                            && battleQueryReadable
                            && battleQueryActive;
                        var statusSpeech =
                            Steam2026FrameInputOwnership.PollBattleStatusBeforeNavigation(
                                battleStatusHotkeyController,
                                ownsBattleStatusHotkeys,
                                lifecycle?.ModuleId ?? -1,
                                lifecycle?.ModuleId switch
                                {
                                    FieldPositionReader.FieldModule =>
                                        fieldNavigationCoordinator is not null,
                                    WorldMapStateReader.WorldModule =>
                                        worldMapAccessibilityCoordinator is not null,
                                    _ => false
                                },
                                foregroundInput,
                                slot => battleStatusHotkeyReader?.ReadMember(slot),
                                resetSelectionWhenInactive:
                                    battleQueryReadable && !battleQueryActive);
                        if (!string.IsNullOrWhiteSpace(statusSpeech))
                        {
                            output.Speak(statusSpeech, interrupt: true);
                            log(
                                $"Native Steam 2026 battle status hotkey: "
                                + $"slot={battleStatusHotkeyController.SelectedPartySlot + 1}, "
                                + $"text={statusSpeech}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogRuntimeFault(
                            $"Native battle status hotkey failed: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    try
                    {
                        worldMapAccessibilityCoordinator?.Observe(frame, now);
                    }
                    catch (Exception ex)
                    {
                        worldMapAccessibilityCoordinator?.Suspend("world-map processing fault");
                        LogRuntimeFault(
                            $"Native world-map accessibility suspended after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    try
                    {
                        if (fieldNavigationCoordinator is null)
                        {
                            navigationProbeSnapshot =
                                CreateUnavailableNavigationProbeSnapshot(
                                    frame,
                                    probeWorkerCycle,
                                    now,
                                    Steam2026NavigationProbeAvailability.Unavailable,
                                    "native field navigation coordinator is unavailable");
                        }
                        else
                        {
                            fieldNavigationCoordinator.Observe(frame, now);
                            navigationProbeSnapshot =
                                fieldNavigationCoordinator.CaptureProbeSnapshot(
                                    frame,
                                    probeWorkerCycle,
                                    now);
                        }
                    }
                    catch (Exception ex)
                    {
                        fieldNavigationCoordinator?.Reset();
                        navigationProbeSnapshot =
                            CreateUnavailableNavigationProbeSnapshot(
                                frame,
                                probeWorkerCycle,
                                now,
                                Steam2026NavigationProbeAvailability.Faulted,
                                $"{ex.GetType().Name}: {ex.Message}");
                        LogRuntimeFault(
                            $"Native field navigation reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    if (fieldFootstepNavigationProbe is not null &&
                        navigationProbeSnapshot is not null)
                    {
                        try
                        {
                            fieldFootstepNavigationProbe.PublishNavigation(
                                navigationProbeSnapshot);
                            fieldFootstepNavigationProbe.CommitCycle(
                                probeWorkerCycle,
                                now);
                        }
                        catch (Exception ex)
                        {
                            fieldFootstepNavigationProbe.ResetCorrelation();
                            LogRuntimeFault(
                                $"Native field footstep/navigation probe reset after a fault: " +
                                $"{ex.Message}",
                                now,
                                ref lastRuntimeFault,
                                ref lastRuntimeFaultLogUtc);
                        }
                    }
                }
                else if (pump is not null)
                {
                    footstepCoordinator?.Reset();
                    fieldFootstepNavigationProbe?.ResetCorrelation();
                    fieldObjectSpatialCoordinator?.Reset("field frame unreadable");
                    fieldNavigationCoordinator?.SynchronizeAutoWalkWithoutFrame();
                    fieldNavigationCoordinator?.Suspend();
                    worldMapAccessibilityCoordinator?.Suspend("runtime frame unreadable");
                    highwayAccessibilityCoordinator?.Reset("runtime frame unreadable");
                }

                if (movieHookSet is not null)
                {
                    List<RuntimeEvent>? movieEvents = null;
                    while (movieHookSet.TryDequeue(out var snapshot))
                    {
                        log(
                            $"Native movie callback {snapshot.CallbackKind}: "
                            + $"success={snapshot.OriginalSucceeded} "
                            + $"return={snapshot.OriginalReturnValue?.ToString() ?? "void"} "
                            + $"path={snapshot.CanonicalMoviePath ?? "<none>"} "
                            + $"state={snapshot.StateBefore?.ToString() ?? "-"}"
                            + $"->{snapshot.StateAfter?.ToString() ?? "-"}.");
                        if (snapshot.LifecycleEvent is { } movieEvent)
                        {
                            movieEvents ??= [];
                            movieEvents.Add(movieEvent);
                            if (string.Equals(
                                    movieEvent.NativeMovieKey,
                                    OpeningMovieLifecycleObserver.OpeningMovieKey,
                                    StringComparison.Ordinal))
                            {
                                openingMovieDetected = true;
                                openingMovieActive = movieEvent.Kind == MovieLifecycleKind.Started;
                            }
                            log(
                                $"Native opening movie lifecycle: {movieEvent.Kind} "
                                + $"at {movieEvent.TimestampUtc:O}.");
                        }
                    }

                    if (movieEvents is { Count: > 0 })
                    {
                        try
                        {
                            dispatcher.Dispatch(
                                new RuntimeDispatchBatch(
                                    null,
                                    movieEvents,
                                    null),
                                now);
                        }
                        catch (Exception ex)
                        {
                            LogRuntimeFault(
                                $"Native movie accessibility dispatch will retry cleanup: {ex.Message}",
                                now,
                                ref lastRuntimeFault,
                                ref lastRuntimeFaultLogUtc);
                            dispatcher.Cleanup("after native movie dispatch failure");
                        }
                    }

                    if (movieHookSet.IsFatallyDegraded)
                    {
                        log("Native movie ingress degraded; disabling its full hook cohort.");
                        movieHookSet.Dispose();
                        movieHookSet = null;
                        movieHooksPermanentlyDisabled = true;
                        dispatcher.Cleanup("after native movie ingress degradation");
                    }
                }

                if (nativeSystemMenuHookSet is not null
                    && nativeSystemMenuReader is not null)
                {
                    var repeatUnchangedAutosave = false;
                    if (nativeSystemMenuHookSet.TryGetVerticalNavigationGeneration(
                            out var verticalNavigationGeneration))
                    {
                        repeatUnchangedAutosave =
                            verticalNavigationGeneration
                            != lastNativeSystemMenuVerticalNavigationGeneration;
                        lastNativeSystemMenuVerticalNavigationGeneration =
                            verticalNavigationGeneration;
                    }

                    if (nativeSystemMenuHookSet.TryGetLatestManagerHost(
                            out var nativeMenuManagerHost))
                    {
                        nativeSystemMenuReader.ObserveManagerHost(
                            nativeMenuManagerHost);
                    }

                    IReadOnlyList<Steam2026SystemMenuSpeechRequest>
                        immediateNativeMenuSpeech;
                    if (isHostForeground
                        && nativeSystemMenuReader.TryRead(
                            out var nativeMenuObservation))
                    {
                        immediateNativeMenuSpeech =
                            nativeSystemMenuSpeech.Observe(
                                nativeMenuObservation,
                                now,
                                repeatUnchangedAutosave);
                    }
                    else
                    {
                        immediateNativeMenuSpeech =
                            nativeSystemMenuSpeech.Observe(null, now);
                    }

                    var delayedNativeMenuSpeech = isHostForeground
                        ? nativeSystemMenuSpeech.Poll(now)
                        : Array.Empty<Steam2026SystemMenuSpeechRequest>();
                    foreach (var request in immediateNativeMenuSpeech
                                 .Concat(delayedNativeMenuSpeech))
                    {
                        try
                        {
                            output.Speak(request.Text, request.Interrupt);
                            log(
                                $"Native Steam 2026 system menu: {request.Text}");
                        }
                        catch (Exception ex)
                        {
                            nativeSystemMenuSpeech.Reset();
                            LogRuntimeFault(
                                $"Native system-menu speech will retry: {ex.Message}",
                                now,
                                ref lastRuntimeFault,
                                ref lastRuntimeFaultLogUtc);
                            break;
                        }
                    }

                    if (nativeSystemMenuHookSet.IsFatallyDegraded)
                    {
                        log(
                            "Native Escape-menu MUI manager hook degraded; "
                            + "disabling it.");
                        nativeSystemMenuHookSet.Dispose();
                        nativeSystemMenuHookSet = null;
                        nativeSystemMenuHooksPermanentlyDisabled = true;
                        nativeSystemMenuReader.Reset();
                        nativeSystemMenuSpeech.Reset();
                    }
                }

                var ownsTitleLoadNow = config.EnableTitleLoadMenuSpeech
                    && isHostForeground
                    && lifecycle is
                    {
                        IsShuttingDown: false,
                        ModuleId: TitleMenuCursorReader.TitleModule
                    };
                titleLoadMenuBridge?.SetOwnership(ownsTitleLoadNow);
                if (ownsTitleLoadNow
                    && menuReader?.TryReadTitleLoadMenu(out var titleLoadState) == true)
                {
                    titleLoadMenuBridge?.ObserveState(titleLoadState, now);
                }

                var nativeTitleActive = false;
                var nativeTitleDiagnostic = string.Empty;
                if (isHostForeground
                    && lifecycle?.IsShuttingDown != true
                    && nativeTitleReader is not null
                    && nativeTitleReader.TryRead(
                        out var nativeTitleSelection,
                        out nativeTitleDiagnostic))
                {
                    nativeTitleActive = true;
                    nativeTitleMisses = 0;
                    tracker.Reset();
                    if (!string.Equals(
                            nativeTitleSelection.Key,
                            lastNativeTitleKey,
                            StringComparison.Ordinal))
                    {
                        try
                        {
                            output.Speak(nativeTitleSelection.Text, interrupt: true);
                            lastNativeTitleKey = nativeTitleSelection.Key;
                            log(
                                $"Native Steam 2026 title selection: "
                                + $"index={nativeTitleSelection.Index} "
                                + $"text={nativeTitleSelection.Text}");
                        }
                        catch (Exception ex)
                        {
                            LogRuntimeFault(
                                $"Native title selection speech will retry: {ex.Message}",
                                now,
                                ref lastRuntimeFault,
                                ref lastRuntimeFaultLogUtc);
                        }
                    }
                }
                else if (!isHostForeground || ++nativeTitleMisses >= 3)
                {
                    lastNativeTitleKey = null;
                    nativeTitleMisses = 0;
                }

                if (isHostForeground
                    && nativeTitleReader is not null
                    && !nativeTitleActive
                    && nativeTitleDiagnostic.Length > 0
                    && now >= nextNativeTitleDiagnosticUtc
                    && !string.Equals(
                        nativeTitleDiagnostic,
                        lastNativeTitleDiagnostic,
                        StringComparison.Ordinal))
                {
                    log($"Native Steam 2026 title probe: {nativeTitleDiagnostic}");
                    lastNativeTitleDiagnostic = nativeTitleDiagnostic;
                    nextNativeTitleDiagnosticUtc = now + TimeSpan.FromSeconds(1);
                }

                if (!nativeTitleActive
                    && titleLoadMenuBridge?.HasOwnership == true
                    && titleLoadMenuBridge.Poll(now) is { Length: > 0 } titleLoadSpeech)
                {
                    try
                    {
                        output.Speak(titleLoadSpeech, interrupt: true);
                        log($"Native Steam 2026 Continue menu: {titleLoadSpeech}");
                    }
                    catch (Exception ex)
                    {
                        LogRuntimeFault(
                            $"Continue menu speech will retry on the next native change: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }
                }

                NameEntryStateSnapshot? currentNameEntry = null;
                var hasCurrentNameEntry = nameEntryReader is not null
                    && nameEntryReader.TryReadSnapshot(out currentNameEntry);
                var ownsNameEntryPrompt = isHostForeground
                    && lifecycle is
                    {
                        IsShuttingDown: false,
                        ModuleId: NameEntryStateReader.NameEntryModule
                    }
                    && hasCurrentNameEntry
                    && currentNameEntry?.IsActive == true;
                nameEntryPromptSpeechCoordinator.SetOwnership(ownsNameEntryPrompt);

                var hasExactShopMenuOwnership = isHostForeground
                    && lifecycle is
                    {
                        IsForeground: true,
                        IsShuttingDown: false,
                        ModuleId: ShopMenuStateReader.ShopModule
                    }
                    && menuReader is not null
                    && menuReader.TryReadShopMenuOwnership(out var nativeOwnsShop)
                    && nativeOwnsShop;
                var shouldSpeakShopMenu = hasExactShopMenuOwnership
                    && (config.EnableInGameMenuWidgetSpeech ||
                        config.EnableInGameMenuHelpTextSpeech);
                var ownsRegularInGameMenuNow = lifecycle is
                {
                    IsShuttingDown: false,
                    ModuleId: Steam2026InGameMenuSpeechBridge.MenuModule
                }
                    && hasCurrentNameEntry
                    && currentNameEntry?.IsActive == false
                    && !hasExactShopMenuOwnership;
                var ownsPhsInGameMenuNow = lifecycle is
                {
                    IsShuttingDown: false,
                    ModuleId: var phsModule
                }
                    && Steam2026InGameMenuSpeechBridge.IsOwnedNativeModule(phsModule)
                    && phsModule != Steam2026InGameMenuSpeechBridge.MenuModule
                    && !hasExactShopMenuOwnership;
                var retainedWorldMapSaveBeforeIngress = lifecycle is
                {
                    IsShuttingDown: false,
                    ModuleId: WorldMapStateReader.WorldModule
                }
                    && inGameMenuBridge?.HasSaveMenuOwnership == true;
                inGameMenuBridge?.ObserveSaveMenuState(
                    isHostForeground
                    && (ownsRegularInGameMenuNow || retainedWorldMapSaveBeforeIngress),
                    now);
                if (config.EnableMenuWidgetDiagnostics
                    && isHostForeground
                    && (ownsRegularInGameMenuNow || retainedWorldMapSaveBeforeIngress)
                    && menuReader is not null
                    && !string.Equals(
                        menuReader.LastSaveMenuDiagnostic,
                        lastSaveMenuDiagnostic,
                        StringComparison.Ordinal))
                {
                    log($"Native Steam 2026 Save state: {menuReader.LastSaveMenuDiagnostic}");
                    lastSaveMenuDiagnostic = menuReader.LastSaveMenuDiagnostic;
                }

                if (hookSet is not null)
                {
                    if (hasExactShopMenuOwnership)
                    {
                        inGameMenuBridge?.Reset();
                        tracker.Reset();
                    }

                    while (hookSet.TryDequeue(out var snapshot))
                    {
                        pump?.ObserveMenuIngress(snapshot);
                        nameEntryPromptSpeechCoordinator.Observe(snapshot);

                        if (lifecycle is null
                            && isHostForeground
                            && snapshot.Text is { } observedText
                            && loggedUnresolvedMenuTexts.Count < 24)
                        {
                            var diagnosticText = string.Concat(observedText.Text
                                    .Select(character => char.IsControl(character) ? ' ' : character))
                                .Trim();
                            var diagnosticKey = string.Join(
                                '\u001f',
                                observedText.Source,
                                observedText.Context,
                                observedText.X,
                                observedText.Y,
                                diagnosticText);
                            if (diagnosticText.Length > 0
                                && loggedUnresolvedMenuTexts.Add(diagnosticKey))
                            {
                                log(
                                    $"Translated menu text observed before lifecycle: "
                                    + $"{observedText.Source} context={observedText.Context} "
                                    + $"x={observedText.X} y={observedText.Y} "
                                    + $"text={diagnosticText[..Math.Min(diagnosticText.Length, 120)]}");
                            }
                        }

                        var moduleId = lifecycle is { IsShuttingDown: false }
                            ? lifecycle.ModuleId
                            : (int?)null;
                        var nameEntryActiveOrUnknown =
                            moduleId == Steam2026InGameMenuSpeechBridge.MenuModule
                            && (!hasCurrentNameEntry || currentNameEntry?.IsActive != false);
                        titleLoadMenuBridge?.Observe(snapshot);
                        if (!hasExactShopMenuOwnership)
                        {
                            inGameMenuBridge?.Observe(
                                snapshot,
                                moduleId,
                                isHostForeground && lifecycle?.IsShuttingDown != true,
                                nameEntryActiveOrUnknown);
                        }

                        if (titleLoadMenuBridge?.HasOwnership == true ||
                            hasExactShopMenuOwnership)
                        {
                            tracker.Reset();
                        }
                        else
                        {
                            tracker.Observe(
                                snapshot,
                                moduleId,
                                isHostForeground && lifecycle?.IsShuttingDown != true);
                        }
                    }

                    try
                    {
                        nameEntryPromptSpeechCoordinator.Poll(DateTime.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        LogRuntimeFault(
                            $"Name-entry prompt speech will retry: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    var nativeQuitHandledByRuntimeFrame =
                        config.EnableSpeech &&
                        config.EnableRuntimeMenuSpeech &&
                        menuReader?.TryReadQuitConfirmation(out _) == true;
                    var ownsWorldMapSaveNow = lifecycle is
                    {
                        IsShuttingDown: false,
                        ModuleId: WorldMapStateReader.WorldModule
                    }
                        && inGameMenuBridge?.HasSaveMenuOwnership == true;
                    var ownsWorldMapMenuNow = lifecycle is
                    {
                        IsShuttingDown: false,
                        ModuleId: WorldMapStateReader.WorldModule
                    }
                        && inGameMenuBridge?.HasWorldMapMenuOwnership(now) == true;
                    var ownsInGameMenuNow = isHostForeground
                        && lifecycle?.IsShuttingDown != true
                        && !hasExactShopMenuOwnership
                        && !nativeQuitHandledByRuntimeFrame
                        && (ownsRegularInGameMenuNow ||
                            ownsPhsInGameMenuNow ||
                            ownsWorldMapSaveNow ||
                            ownsWorldMapMenuNow ||
                            inGameMenuBridge?.HasExactQuitOwnership(now) == true);
                    if (!ownsInGameMenuNow)
                    {
                        inGameMenuBridge?.Reset();
                    }
                    else if (inGameMenuBridge?.Poll(now) is { Length: > 0 } menuSpeech)
                    {
                        try
                        {
                            output.Speak(menuSpeech, interrupt: true);
                            inGameMenuBridge.AcknowledgeSaveMenuSpeech(menuSpeech);
                            log($"Native Steam 2026 in-game menu: {menuSpeech}");
                        }
                        catch (Exception ex)
                        {
                            LogRuntimeFault(
                                $"In-game menu speech will retry: {ex.Message}",
                                now,
                                ref lastRuntimeFault,
                                ref lastRuntimeFaultLogUtc);
                        }
                    }

                    if (!nativeTitleActive
                        && titleLoadMenuBridge?.HasOwnership != true
                        && !hasExactShopMenuOwnership
                        && tracker.TryGetPending(out var selection)
                        && isHostForeground
                        && lifecycle?.IsShuttingDown != true)
                    {
                        try
                        {
                            output.Speak(selection.Text, interrupt: true);
                            tracker.Acknowledge(selection);
                        }
                        catch (Exception ex)
                        {
                            LogRuntimeFault(
                                $"Title selection speech will retry: {ex.Message}",
                                now,
                                ref lastRuntimeFault,
                                ref lastRuntimeFaultLogUtc);
                        }
                    }

                    if (hookSet.IsFatallyDegraded)
                    {
                        log("Translated menu ingress degraded; disabling its full hook cohort.");
                        hookSet.Dispose();
                        hookSet = null;
                        hooksPermanentlyDisabled = true;
                        tracker.Reset();
                        inGameMenuBridge?.Reset();
                        titleLoadMenuBridge?.SetOwnership(false);
                        titleLoadMenuBridge?.ResetIngress();
                        nameEntryPromptSpeechCoordinator.Reset();
                    }
                }
                else
                {
                    try
                    {
                        Steam2026FrameInputOwnership.SynchronizeBattleStatusWithoutFrame(
                            battleStatusHotkeyController,
                            foregroundInput);
                    }
                    catch (Exception ex)
                    {
                        LogRuntimeFault(
                            $"Native battle status input synchronization failed: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }
                }

                if (!shouldSpeakShopMenu)
                {
                    shopMenuSpeechTracker.Reset();
                }
                else if (menuReader?.PollShopMenu(shopMenuSpeechTracker) is
                         { Length: > 0 } shopSpeech)
                {
                    try
                    {
                        output.Speak(shopSpeech, interrupt: true);
                        log($"Native Steam 2026 shop menu: {shopSpeech}");
                    }
                    catch (Exception ex)
                    {
                        shopMenuSpeechTracker.Reset();
                        LogRuntimeFault(
                            $"Shop menu speech will retry on the next native change: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }
                }

                try
                {
                    nameEntrySpeechCoordinator.Observe(
                        hasCurrentNameEntry ? currentNameEntry : null,
                        isHostForeground && lifecycle?.IsShuttingDown != true,
                        now);
                }
                catch (Exception ex)
                {
                    LogRuntimeFault(
                        $"Name-entry speech will retry: {ex.Message}",
                        now,
                        ref lastRuntimeFault,
                        ref lastRuntimeFaultLogUtc);
                }

                if (battleRendererHookSet is not null
                    && battleAccessibilityCoordinator is not null)
                {
                    var ownsBattleAccessibility = battleOptions.AnyEnabled
                        && isHostForeground
                        && lifecycle is
                        {
                            IsForeground: true,
                            IsShuttingDown: false
                        } activeBattleLifecycle
                        && IsBattleAccessibilityModule(activeBattleLifecycle.ModuleId);
                    var battleBatch = new List<Steam2026BattleRendererIngressSnapshot>();
                    while (battleRendererHookSet.TryDequeue(out var battleSnapshot))
                    {
                        if (ownsBattleAccessibility)
                        {
                            battleBatch.Add(battleSnapshot);
                        }
                    }

                    if (!ownsBattleAccessibility)
                    {
                        battleAccessibilityCoordinator.Reset();
                    }
                    else
                    {
                        if (battleBatch.Count > 0)
                        {
                            battleAccessibilityCoordinator.ProcessBatch(battleBatch);
                        }

                        while (battleAccessibilityCoordinator.TrySpeakPending(
                                   speech =>
                                   {
                                       output.Speak(speech.Text, speech.Interrupt);
                                       return true;
                                   },
                                   out var battleSpeech))
                        {
                            log(
                                $"Native Steam 2026 battle {battleSpeech.Domain}: "
                                + battleSpeech.Text);
                        }
                    }

                    if (battleRendererHookSet.IsFatallyDegraded)
                    {
                        log("Translated battle lifecycle ingress degraded; disabling its hook cohort.");
                        battleRendererHookSet.Dispose();
                        battleRendererHookSet = null;
                        battleRendererHooksPermanentlyDisabled = true;
                        battleAccessibilityCoordinator.Reset();
                    }
                }

                var workerDelayMs = currentNameEntry?.IsActive == true
                    ? NameEntryNativeNameTracker.RecommendedScanIntervalMs
                    : 35;
                if (cancellation.Token.WaitHandle.WaitOne(workerDelayMs))
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            log($"Native Steam 2026 research worker stopped after an unexpected fault: {ex}");
        }
        finally
        {
            pump?.BeginShutdown();
            tracker.Reset();
            nativeSystemMenuSpeech.Reset();
            nativeSystemMenuReader?.Reset();
            nativeSystemMenuHookSet?.Dispose();
            inGameMenuBridge?.Reset();
            nameEntrySpeechCoordinator.Reset();
            nameEntryPromptSpeechCoordinator.Reset();
            movieHookSet?.Dispose();
            fieldMessageHookSet?.Dispose();
            askCursorHookSet?.Dispose();
            cutsceneHookSet?.Dispose();
            cutsceneDescriptions?.Reset();
            cutsceneNarrationSpeechTracker.Reset();
            fieldZoneSpeechCoordinator?.Reset();
            battleRendererHookSet?.Dispose();
            battleAccessibilityCoordinator?.Reset();
            hookSet?.Dispose();
            footstepCoordinator?.Dispose();
            fieldObjectSpatialCoordinator?.Dispose();
            fieldNavigationCoordinator?.Dispose();
            worldMapAccessibilityCoordinator?.Dispose();
            highwayAccessibilityCoordinator?.Dispose();
            fieldFootstepNavigationProbe?.Dispose();
            dispatcher.Cleanup("during native x64 research shutdown");
            log("Native Steam 2026 research worker stopped.");
        }
    }

    internal static RuntimeDomainUpdate<DialoguePageObservation> ApplyCutsceneDialogueSuppression(
        RuntimeDomainUpdate<DialoguePageObservation> update,
        bool suppressDialogue) =>
        suppressDialogue && update.Kind == RuntimeDomainUpdateKind.Present
            ? RuntimeDomainUpdate<DialoguePageObservation>.Unchanged
            : update;

    internal static Steam2026BattleAccessibilityOptions CreateBattleOptions(
        AccessibilityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new Steam2026BattleAccessibilityOptions(
            config.EnableBattleMenuSpeech,
            config.EnableBattleTargetSpeech,
            config.EnableBattleMessageSpeech,
            config.EnableBattleResultsSpeech,
            config.EnableBattleDamageSpeech,
            config.EnableBattleEncounterSpeech,
            config.EnableBattleEnemyActionSpeech,
            config.EnableBattleStatusSpeech);
    }

    internal static bool IsBattleAccessibilityModule(int moduleId) =>
        moduleId is BattleStateReader.BattleModule or BattleResultsReader.ResultsModule;

    private static Steam2026NavigationProbeSnapshot
        CreateUnavailableNavigationProbeSnapshot(
            RuntimeFrameObservation frame,
            long workerCycle,
            DateTime nowUtc,
            Steam2026NavigationProbeAvailability availability,
            string diagnostic)
    {
        var position = default(FieldPositionSnapshot);
        if (frame.Field.Kind == RuntimeDomainUpdateKind.Present &&
            frame.Field.Value is { } field)
        {
            Steam2026FieldFootstepCoordinator.TryCreatePosition(
                field,
                out position);
        }

        return new Steam2026NavigationProbeSnapshot(
            workerCycle,
            nowUtc,
            position,
            availability,
            ResolvedTriangle: -1,
            WalkmeshTriangleCount: 0,
            BoundaryFingerprint: string.Empty,
            ActiveBoundaryTriangles: Array.Empty<int>(),
            Controller: default,
            RoutePlannerDiagnostic: string.Empty,
            StateDiagnostic: diagnostic);
    }

    private static void ObserveFieldObjectCues(
        RuntimeFrameObservation frame,
        Steam2026FieldObjectObservationReader? objectReader,
        Steam2026FieldObjectSpatialCoordinator? coordinator,
        bool isHostForeground,
        DateTime nowUtc,
        ref DateTime nextScanUtc)
    {
        if (coordinator is null)
        {
            return;
        }

        var ownsAudibleField = isHostForeground
            && frame.Lifecycle.IsForeground
            && !frame.Lifecycle.IsShuttingDown
            && frame.Lifecycle.ModuleId == FieldPositionReader.FieldModule;
        if (!ownsAudibleField)
        {
            coordinator.Observe(
                default,
                default,
                Array.Empty<FieldNavigationTarget>(),
                isHostForeground: false,
                isSuppressed: false,
                isReadCoherent: false,
                nowUtc);
            return;
        }

        if (objectReader is null || nowUtc < nextScanUtc)
        {
            return;
        }

        nextScanUtc = nowUtc + TimeSpan.FromMilliseconds(50);
        var isCoherent = objectReader.TryReadSnapshot(out var objectSnapshot);
        var isSuppressed = isCoherent && objectSnapshot.Cue.IsSuppressed;
        coordinator.Observe(
            isCoherent ? objectSnapshot.Position : default,
            isCoherent ? objectSnapshot.Control : default,
            isCoherent
                ? objectSnapshot.Targets
                : Array.Empty<FieldNavigationTarget>(),
            isHostForeground: true,
            isSuppressed,
            isReadCoherent: isCoherent,
            nowUtc,
            readDiagnostic: objectReader.LastDiagnostic);
    }

    private void LogSetup(
        string diagnostic,
        DateTime now,
        ref string lastDiagnostic,
        ref DateTime lastLogUtc)
    {
        if (!string.Equals(diagnostic, lastDiagnostic, StringComparison.Ordinal)
            || now - lastLogUtc >= RepeatedLogInterval)
        {
            log(diagnostic);
            lastDiagnostic = diagnostic;
            lastLogUtc = now;
        }
    }

    private void LogRuntimeFault(
        string diagnostic,
        DateTime now,
        ref string lastDiagnostic,
        ref DateTime lastLogUtc)
    {
        if (!string.Equals(diagnostic, lastDiagnostic, StringComparison.Ordinal)
            || now - lastLogUtc >= RepeatedLogInterval)
        {
            log(diagnostic);
            lastDiagnostic = diagnostic;
            lastLogUtc = now;
        }
    }
}
