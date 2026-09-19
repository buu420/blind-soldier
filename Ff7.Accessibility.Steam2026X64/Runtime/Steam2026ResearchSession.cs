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
    /// <summary>
    /// The Fort Condor battlefield navigator's keys, which are the field
    /// navigator's keys: U and O for categories, J and L for targets, I to jump.
    /// Kept identical to the x86 runtime's binding so the fort plays the same on
    /// both executables.
    /// </summary>
    private static readonly (int VirtualKey, CondorNavigationAction Action)[] CondorNavigationKeys =
    {
        (0x55, CondorNavigationAction.PreviousCategory), // U
        (0x4F, CondorNavigationAction.NextCategory),     // O
        (0x4A, CondorNavigationAction.PreviousTarget),   // J
        (0x4C, CondorNavigationAction.NextTarget),       // L
        (0x49, CondorNavigationAction.JumpToTarget)      // I
    };

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

    /// <summary>
    /// Opens the reviewed narration asset for a described native film. Every failure
    /// returns null, which leaves the coordinator on its ordinary spoken-paragraph
    /// path rather than losing the description entirely.
    /// </summary>
    internal static IFieldMovieNarrationOutput? CreateFieldMovieNarrationOutput(
        AccessibilityConfig config,
        string modDirectory,
        FieldMovieNarrationTrack track,
        Action<string> log)
    {
        try
        {
            if (!config.EnableFieldMovieNarrationTracks)
            {
                log($"Field movie narration disabled in config; {track.Label} falls back to speech.");
                return null;
            }

            var directory = config.FieldMovieNarrationTrackDirectory;
            var path = Path.IsPathRooted(directory)
                ? Path.Combine(directory, track.FileName)
                : Path.Combine(modDirectory, directory, track.FileName);
            if (!File.Exists(path))
            {
                log($"Field movie narration track missing: {path}");
                return null;
            }

            return new OpeningMovieAudioTrackPlayer(
                path,
                config.FieldMovieNarrationTrackVolumePercent,
                log,
                $"Field movie {track.Label}");
        }
        catch (Exception ex)
        {
            log($"Field movie narration output could not be created ({track.Label}): {ex.Message}");
            return null;
        }
    }

    /// <summary>Where the recorded descriptions and their manifest live.</summary>
    internal const string CutsceneVoiceDirectory = "Assets/cutscene-voice";

    /// <summary>
    /// One recorded description, opened through the same Vorbis player the films use.
    /// </summary>
    internal static IFieldMovieNarrationOutput? CreateCutsceneVoiceOutput(
        AccessibilityConfig config,
        string modDirectory,
        CutsceneVoiceClip clip,
        Action<string> log)
    {
        try
        {
            if (!config.EnableFieldCutsceneDescriptions)
            {
                return null;
            }

            var path = Path.Combine(modDirectory, CutsceneVoiceDirectory, clip.FileName);
            if (!File.Exists(path))
            {
                log($"Cutscene voice recording missing: {path}");
                return null;
            }

            return new OpeningMovieAudioTrackPlayer(
                path,
                config.FieldMovieNarrationTrackVolumePercent,
                log,
                "Cutscene description");
        }
        catch (Exception ex)
        {
            log($"Cutscene voice output could not be created ({clip.FileName}): {ex.Message}");
            return null;
        }
    }

    internal static CutsceneVoiceManifest LoadCutsceneVoiceManifest(
        AccessibilityConfig config,
        string modDirectory,
        Action<string> log)
    {
        try
        {
            if (!config.EnableFieldCutsceneDescriptions)
            {
                return CutsceneVoiceManifest.Empty;
            }

            var path = Path.Combine(modDirectory, CutsceneVoiceDirectory, "manifest.json");
            if (!File.Exists(path))
            {
                log("Cutscene voice manifest not installed; descriptions use speech.");
                return CutsceneVoiceManifest.Empty;
            }

            var manifest = CutsceneVoiceManifest.Parse(File.ReadAllText(path), log);
            log($"Cutscene voice manifest: {manifest.Count} recorded description(s)" +
                (manifest.SourceHash is null ? "." : $", source {manifest.SourceHash}."));
            return manifest;
        }
        catch (Exception ex)
        {
            log($"Cutscene voice manifest could not be loaded: {ex.Message}");
            return CutsceneVoiceManifest.Empty;
        }
    }

    /// <summary>
    /// The recording's cue windows, from the sidecar beside it. The x64 package ships
    /// the same asset folder, so this is the same file the x86 runtime reads.
    /// </summary>
    internal static IReadOnlyList<MovieNarrationCue> ReadFieldMovieNarrationCues(
        AccessibilityConfig config,
        string modDirectory,
        FieldMovieNarrationTrack track,
        Action<string> log)
    {
        try
        {
            var directory = config.FieldMovieNarrationTrackDirectory;
            var stem = Path.GetFileNameWithoutExtension(track.FileName) + ".json";
            var path = Path.IsPathRooted(directory)
                ? Path.Combine(directory, stem)
                : Path.Combine(modDirectory, directory, stem);
            return File.Exists(path)
                ? MovieNarrationCueSchedule.Parse(File.ReadAllText(path))
                : [];
        }
        catch (Exception ex)
        {
            log($"Field movie narration cue schedule could not be read ({track.Label}): {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Resolves an arcade cue path against the mod directory, falling back to that
    /// cue's own packaged asset.
    /// </summary>
    /// <remarks>
    /// The previous form had two defects that a non-empty configured path hid. Its
    /// verbatim fallback string contained a literal newline and had lost both
    /// separators, so a blank setting resolved to a path that cannot exist; and one
    /// fallback served both basketball cues, so a blank top-of-rise path would have
    /// quietly played the rise tick instead. Each caller now supplies the fallback
    /// for its own cue.
    /// </remarks>
    internal static string ResolveArcadeCuePath(string modDirectory, string configured, string fallback)
    {
        var path = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return Path.IsPathRooted(path) ? path : Path.Combine(modDirectory, path);
    }

    internal void Suspend()
    {
        Interlocked.Exchange(ref resetRequested, 1);
        resumeGate.Reset();
        log("Native Steam 2026 research session suspended.");

        // Said out loud, as the legacy runtime has always said it. Without this the
        // mod simply stops talking, and a player cannot tell suspended from crashed
        // from hung - the one failure this project treats as worse than a crash.
        AnnounceLifecycle("Final Fantasy Seven accessibility mod suspended.");
    }

    internal void Resume()
    {
        Interlocked.Exchange(ref resetRequested, 1);
        resumeGate.Set();
        log("Native Steam 2026 research session resumed.");
        AnnounceLifecycle("Final Fantasy Seven accessibility mod resumed.");
    }

    /// <summary>
    /// Speaks a suspend or resume transition, and never lets that speech take the
    /// session down with it.
    /// </summary>
    /// <remarks>
    /// This runtime's speech output throws when Prism refuses a line, unlike the
    /// legacy host's, which returns false. An unguarded call here would turn a
    /// refused courtesy line into a dead session - silence in place of a warning
    /// about silence.
    /// </remarks>
    private void AnnounceLifecycle(string text)
    {
        if (!config.EnableSpeech)
        {
            return;
        }

        var sink = Volatile.Read(ref lifecycleOutput);
        if (sink is null)
        {
            // The worker has not started, or has already torn down. Nothing to say
            // it through, and saying nothing is honest here.
            log($"Lifecycle announcement had no speech output: {text}");
            return;
        }

        try
        {
            sink.Speak(text, true);
        }
        catch (Exception ex)
        {
            log($"Lifecycle announcement was not delivered: {ex.Message}");
        }
    }

    /// <summary>
    /// The worker's speech output, published so the loader's suspend and resume
    /// calls can reach it from another thread.
    /// </summary>
    private Steam2026ResearchAccessibilityOutput? lifecycleOutput;

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
        Steam2026FieldZoneTransitionCueCoordinator? fieldZoneTransitionCueCoordinator = null;
        Steam2026FieldObjectObservationReader? fieldObjectReader = null;
        Steam2026FieldNavigationCoordinator? fieldNavigationCoordinator = null;

        // The controller navigation menu's capture over SDL. Installed as soon as
        // SDL2 is loaded, which can be well after we attach, and retried until it
        // is: without it the menu refuses to open rather than reading a pad the
        // game is reading too.
        Steam2026SdlControllerCaptureHook? controllerCaptureHook = null;
        var nextControllerCaptureAttemptUtc = DateTime.MinValue;
        Steam2026WorldMapAccessibilityCoordinator? worldMapAccessibilityCoordinator = null;
        HighwayAccessibilityCoordinator? highwayAccessibilityCoordinator = null;
        // The Speed Square coaster runs the original x86 code, so the translated
        // guest address space reaches exactly the globals the x86 build reads.
        SpeedSquareCoasterStateReader? speedSquareCoasterReader = null;
        var speedSquareCoasterReadout = new SpeedSquareCoasterReadout();
        SpeedSquareCoasterTargetReader? speedSquareCoasterTargetReader = null;
        var speedSquareCoasterAimReadout = new SpeedSquareCoasterAimReadout();
        NavigationBeaconPlayer? speedSquareCoasterTargetCuePlayer = null;
        ChocoboSquareStateReader? chocoboSquareReader = null;
        var chocoboSquareReadout = new ChocoboSquareReadout();
        WonderSquareBasketballStateReader? wonderSquareBasketballReader = null;
        var wonderSquareBasketballReadout = new WonderSquareBasketballReadout();
        WonderSquareArmWrestlingStateReader? wonderSquareArmWrestlingReader = null;
        var wonderSquareArmWrestlingReadout = new WonderSquareArmWrestlingReadout();
        WonderSquare3DBattlerStateReader? wonderSquare3DBattlerReader = null;
        var wonderSquare3DBattlerReadout = new WonderSquare3DBattlerReadout();
        ImmediateWaveCuePlayer? basketballWindUpCuePlayer = null;
        ImmediateWaveCuePlayer? basketballTopCuePlayer = null;
        ImmediateWaveCuePlayer? armWrestlingLevelCuePlayer = null;
        ImmediateWaveCuePlayer? armWrestlingPushAheadCuePlayer = null;
        ImmediateWaveCuePlayer? armWrestlingPushedBackCuePlayer = null;
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

        // Where automatic movement is delivered on this host. Not SendInput: the host
        // synthesizes the legacy keyboard state from its own logical actions inside its
        // DirectInput shim, so pressing the keys the control table names never reached the
        // game - and numpad 2, which that table names for Down, is NVDA's read-current-
        // character command, so the presses reached the player's screen reader instead.
        var directionalInput = new Steam2026NativeDirectionalInputSink(
            foregroundInput.IsCurrentProcessForeground);
        Steam2026NativeDirectInputKeyboardHook? directionalInputHook = null;
        var nextDirectionalInputAttemptUtc = DateTime.MinValue;
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

        // Published for Suspend and Resume, which are called from the loader's
        // thread rather than this worker and would otherwise have nothing to speak
        // through. Cleared in the finally below, before the output is disposed, so a
        // late lifecycle call cannot reach a disposed speaker.
        Volatile.Write(ref lifecycleOutput, output);

        // The same seventeen-cue schedule the legacy runtime speaks over the opening
        // movie. Both guards are load-bearing rather than tidiness: this runtime's
        // Speak throws when Prism refuses a line, where the legacy host's returns
        // false, so an unguarded cue would take the whole session down and replace a
        // description with silence.
        var openingMovieDescription = new OpeningMovieDescription((text, interrupt) =>
        {
            if (!config.EnableSpeech)
            {
                return;
            }

            try
            {
                output.Speak(text, interrupt);
            }
            catch (Exception ex)
            {
                log($"Opening movie description cue was not delivered: {ex.Message}");
            }
        });
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

                    // Without this a resumed session fires a cue for a map change
                    // that happened while it was suspended.
                    fieldZoneTransitionCueCoordinator?.Reset();
                    openingMovieActive = false;

                    // A suspend or a runtime reset during the movie must not leave
                    // the schedule running against a clock the player is no longer
                    // watching; it would resume mid-description with no movie.
                    openingMovieDescription.Stop();
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
                            config,
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
                        // Independent film narration plays on its own device so an
                        // ordinary button press cannot cut a 45-second description
                        // into a fragment. A missing asset, a disabled track or a
                        // device that will not open leaves the coordinator on its
                        // ordinary spoken-paragraph path.
                        FieldMovieNarrationTracker? candidateFilmNarration = null;
                        CutsceneVoicePlayer? candidateCutsceneVoice = null;
                        try
                        {
                            candidateFilmNarration = new FieldMovieNarrationTracker(
                                track => CreateFieldMovieNarrationOutput(config, modDirectory, track, log),
                                log,
                                FieldPositionReader.FieldModule,
                                track => ReadFieldMovieNarrationCues(config, modDirectory, track, log),
                                (owner, reason) => candidateCutsceneVoice?.StopIfOwnedBy(owner, reason));
                        }
                        catch (Exception ex)
                        {
                            candidateFilmNarration = null;
                            log($"Native Steam 2026 film narration remains disabled: {ex.Message}");
                        }

                        // The recorded voice for scene descriptions. It refuses while
                        // a film's own recording has the device, so two descriptions
                        // can never play at once, and it declines quietly when a
                        // recording for those exact words is not installed.
                        try
                        {
                            candidateCutsceneVoice = new CutsceneVoicePlayer(
                                LoadCutsceneVoiceManifest(config, modDirectory, log),
                                clip => CreateCutsceneVoiceOutput(config, modDirectory, clip, log),
                                log,
                                () => candidateFilmNarration?.IsPlaying == true);
                        }
                        catch (Exception ex)
                        {
                            candidateCutsceneVoice = null;
                            log($"Native Steam 2026 cutscene voice remains disabled: {ex.Message}");
                        }

                        var candidateCutsceneDescriptions =
                            new Steam2026FieldCutsceneDescriptionCoordinator(
                                sharedFieldAddressSpace,
                                FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions(),
                                candidateFilmNarration,
                                candidateCutsceneVoice);
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
                                gameLanguage,
                                autoWalk: null,
                                // Late-bound: SDL loads when the host first looks
                                // for a pad, often after this coordinator exists.
                                controllerCapture: () => controllerCaptureHook?.Capture,
                                directionalInput: directionalInput);
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
                                    navigationProgressController,
                                    autoWalk: null,
                                    playEntranceCue: null,
                                    controllerCapture: () => controllerCaptureHook?.Capture,
                                    directionalInput: directionalInput);
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
                        try
                        {
                            speedSquareCoasterReader = new SpeedSquareCoasterStateReader(sharedFieldAddressSpace);
                            chocoboSquareReader = new ChocoboSquareStateReader(sharedFieldAddressSpace);
                            speedSquareCoasterTargetReader =
                                new SpeedSquareCoasterTargetReader(sharedFieldAddressSpace);
                            speedSquareCoasterTargetCuePlayer?.Dispose();
                            speedSquareCoasterTargetCuePlayer = config.EnableSpeedSquareCoasterTargetCues
                                ? new NavigationBeaconPlayer(
                                    ResolveArcadeCuePath(
                                        modDirectory,
                                        config.WorldMapEntranceCueSoundPath,
                                        @"Assets
avigationield_zone_transition.wav"),
                                    config.SpeedSquareCoasterTargetCueVolumePercent,
                                    log)
                                : null;
                        }
                        catch (Exception ex)
                        {
                            speedSquareCoasterReader = null;
                            speedSquareCoasterTargetReader = null;
                            log($"Native Steam 2026 Speed Square readout remains disabled: {ex.Message}");
                        }
                        try
                        {
                            wonderSquareBasketballReader =
                                new WonderSquareBasketballStateReader(sharedFieldAddressSpace);
                            wonderSquareArmWrestlingReader =
                                new WonderSquareArmWrestlingStateReader(sharedFieldAddressSpace);
                            wonderSquare3DBattlerReader =
                                new WonderSquare3DBattlerStateReader(sharedFieldAddressSpace);
                            basketballWindUpCuePlayer?.Dispose();
                            basketballTopCuePlayer?.Dispose();
                            basketballWindUpCuePlayer = config.EnableWonderSquareBasketballCues
                                ? new ImmediateWaveCuePlayer(
                                    ResolveArcadeCuePath(
                                        modDirectory,
                                        config.WonderSquareBasketballWindUpCueSoundPath,
                                        ArcadeCueAssets.BasketballRiseTick),
                                    config.WonderSquareBasketballCueVolumePercent,
                                    "Basketball wind-up tick",
                                    log)
                                : null;
                            basketballTopCuePlayer = config.EnableWonderSquareBasketballCues
                                ? new ImmediateWaveCuePlayer(
                                    ResolveArcadeCuePath(
                                        modDirectory,
                                        config.WonderSquareBasketballTopCueSoundPath,
                                        ArcadeCueAssets.BasketballPoseTop),
                                    config.WonderSquareBasketballCueVolumePercent,
                                    "Basketball top-of-rise cue",
                                    log)
                                : null;

                            armWrestlingLevelCuePlayer?.Dispose();
                            armWrestlingPushAheadCuePlayer?.Dispose();
                            armWrestlingPushedBackCuePlayer?.Dispose();
                            armWrestlingLevelCuePlayer = config.EnableWonderSquareArmWrestlingCues
                                ? new ImmediateWaveCuePlayer(
                                    ResolveArcadeCuePath(
                                        modDirectory,
                                        config.WonderSquareArmWrestlingLevelCueSoundPath,
                                        ArcadeCueAssets.ArmWrestlingLevel),
                                    config.WonderSquareArmWrestlingCueVolumePercent,
                                    "Arm wrestling level tone",
                                    log)
                                : null;
                            armWrestlingPushAheadCuePlayer = config.EnableWonderSquareArmWrestlingCues
                                ? new ImmediateWaveCuePlayer(
                                    ResolveArcadeCuePath(
                                        modDirectory,
                                        config.WonderSquareArmWrestlingPushAheadCueSoundPath,
                                        ArcadeCueAssets.ArmWrestlingPushAhead),
                                    config.WonderSquareArmWrestlingCueVolumePercent,
                                    "Arm wrestling pushing-ahead tone",
                                    log)
                                : null;
                            armWrestlingPushedBackCuePlayer = config.EnableWonderSquareArmWrestlingCues
                                ? new ImmediateWaveCuePlayer(
                                    ResolveArcadeCuePath(
                                        modDirectory,
                                        config.WonderSquareArmWrestlingPushedBackCueSoundPath,
                                        ArcadeCueAssets.ArmWrestlingPushedBack),
                                    config.WonderSquareArmWrestlingCueVolumePercent,
                                    "Arm wrestling pushed-back tone",
                                    log)
                                : null;
                        }
                        catch (Exception ex)
                        {
                            wonderSquareBasketballReader = null;
                            log($"Native Steam 2026 Basketball cues remain disabled: {ex.Message}");
                        }
                        pump = candidatePump;
                        menuReader = candidateMenuReader;
                        inGameMenuBridge = candidateMenuBridge;
                        titleLoadMenuBridge = candidateTitleLoadBridge;
                        nameEntryReader = candidateNameEntryReader;
                        cutsceneDescriptions = candidateCutsceneDescriptions;
                        cutsceneDialogueProbe = candidateCutsceneDialogueProbe;
                        fieldZoneSpeechCoordinator = candidateFieldZoneSpeechCoordinator;

                        // Rebuilt with the rest of the field stack so it picks up a
                        // fresh address space, and the old one disposed first so its
                        // audio device does not leak across a session restart.
                        try
                        {
                            var candidateZoneTransitionCue =
                                new Steam2026FieldZoneTransitionCueCoordinator(
                                    config,
                                    sharedFieldAddressSpace,
                                    modDirectory,
                                    log);
                            fieldZoneTransitionCueCoordinator?.Dispose();
                            fieldZoneTransitionCueCoordinator = candidateZoneTransitionCue;
                        }
                        catch (Exception ex)
                        {
                            log(
                                "Native Steam 2026 field zone transition cue remains " +
                                $"disabled: {ex.Message}");
                        }
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

                // Automatic movement's delivery. The seam is the host's own native
                // IDirectInputDeviceA::GetDeviceState, validated against its registration
                // record and live prefix, so the direction is marked in the keyboard state
                // it has just built and read by the translated caller on the same call.
                if ((config.EnableFieldNavigationAssistant || config.EnableWorldMapNavigationAssistant)
                    && hooks is not null
                    && directionalInputHook is null
                    && now >= nextDirectionalInputAttemptUtc)
                {
                    nextDirectionalInputAttemptUtc = now + SetupRetryInterval;
                    if (Steam2026NativeDirectInputKeyboardHook.TryInstall(
                            fingerprint,
                            moduleBase,
                            moduleImageSize,
                            memory,
                            memory as INativeMemoryWriter,
                            hooks,
                            directionalInput,
                            out var installedDirectionalInput,
                            out var directionalInputDiagnostic,
                            log))
                    {
                        directionalInputHook = installedDirectionalInput;
                    }

                    LogSetup(
                        directionalInputDiagnostic,
                        now,
                        ref lastSetupDiagnostic,
                        ref lastSetupLogUtc);
                }

                // The controller navigation capture. SDL2 is loaded by the host when
                // it gets as far as opening a controller, which can be long after we
                // attach, so this retries on the same clock every other hook uses.
                if ((config.EnableFieldNavigationAssistant || config.EnableWorldMapNavigationAssistant)
                    && hooks is not null
                    && controllerCaptureHook is null
                    && now >= nextControllerCaptureAttemptUtc)
                {
                    nextControllerCaptureAttemptUtc = now + SetupRetryInterval;
                    if (Steam2026SdlControllerCaptureHook.TryInstall(
                            hooks,
                            isInstalled => new ControllerNavigationCapture(
                                suppressor => new ControllerNavigationMenu(
                                    EmptyGamepadReader.Instance, suppressor),
                                isInstalled),
                            out var installedControllerCapture,
                            out var controllerCaptureDiagnostic,
                            log: log))
                    {
                        controllerCaptureHook = installedControllerCapture;
                    }

                    LogSetup(
                        controllerCaptureDiagnostic,
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

                if (cutsceneDescriptions is not null)
                {
                    // The independent track outlives a single frame, so its native
                    // lifetime is checked every frame and whenever the host stops
                    // being the foreground window.
                    Steam2026FieldCutsceneHostTick.ObserveOrSuspend(
                        cutsceneDescriptions,
                        isHostForeground,
                        now,
                        cutsceneDialogueProbe is null
                            ? null
                            : () => cutsceneDialogueProbe.TryRead(out _));
                }

                if (cutsceneHookSet is not null && cutsceneDescriptions is not null)
                {
                    while (cutsceneHookSet.TryDequeue(out var snapshot))
                    {
                        cutsceneDescriptions.Observe(snapshot);
                    }

                    // A field whose own entry anchor ran before this runtime attached
                    // would otherwise never be described at all.
                    cutsceneDescriptions.ObserveStableField(now);

                    // A film that gave way to dialogue keeps being described: its
                    // remaining cues are spoken at the moments they belong to, once
                    // the game has stopped talking. Delivery goes through the same
                    // reservation as every other description, and only the speaker
                    // accepting the words advances the film's schedule.
                    if (Steam2026FieldCutsceneHostTick.TryDeliverDeferredFilmCue(
                            cutsceneDescriptions,
                            isHostForeground,
                            now,
                            cutsceneDialogueProbe is null
                                ? null
                                : () => cutsceneDialogueProbe.TryRead(out _),
                            text =>
                            {
                                output.Speak(text, interrupt: false);
                                return true;
                            },
                            out var deferredFilmCue,
                            out var deferredFilmField))
                    {
                        lastCutsceneNarrationFieldId = deferredFilmField;
                        cutsceneNarrationSpeechTracker.Begin(deferredFilmField);
                        if (config.EnableFieldCutsceneDescriptionDiagnostics)
                        {
                            log($"Native Steam 2026 film cue spoken: {deferredFilmCue}");
                        }
                    }
                    else if (cutsceneDialogueProbe is not null
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
                        // Same shared reader and readout as the x86 build, so the
                        // spoken aim and charge are identical on both runtimes.
                        if (!config.EnableSpeedSquareCoasterReadout || speedSquareCoasterReader is null)
                        {
                            speedSquareCoasterReadout.Reset();
                        }
                        else
                        {
                            var coasterSpeech = speedSquareCoasterReader.TryRead(out var coasterState)
                                ? speedSquareCoasterReadout.Observe(coasterState)
                                : speedSquareCoasterReadout.Observe(default);
                            if (coasterSpeech is not null && config.EnableSpeech)
                            {
                                output.Speak(coasterSpeech, true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        speedSquareCoasterReadout.Reset();
                        LogRuntimeFault(
                            $"Speed Square coaster readout reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    try
                    {
                        // Where the currently rendered targets are, the displayed
                        // score, and resolved hits. The direction also plays on the
                        // mod's own spatial device, because the fire button is
                        // pressed constantly and every press cuts off speech.
                        if (!config.EnableSpeedSquareCoasterTargetCues ||
                            speedSquareCoasterReader is null ||
                            speedSquareCoasterTargetReader is null ||
                            !isHostForeground)
                        {
                            speedSquareCoasterAimReadout.Reset();
                        }
                        // The score and the fire flag arrive inside the snapshot,
                        // read under the same module and presentation bookends as
                        // the boxes, so a held frame is never described with a
                        // later frame's sight, score or trigger.
                        else if (speedSquareCoasterReader.TryRead(out var aimState) &&
                                 aimState.IsActive &&
                                 speedSquareCoasterTargetReader.TryReadTargets(
                                     aimState.CursorX, aimState.CursorY, out var coasterTargets))
                        {
                            var aimCue = speedSquareCoasterAimReadout.Observe(
                                aimState, coasterTargets, now);
                            if (aimCue.Beacon is { } coasterBeacon)
                            {
                                speedSquareCoasterTargetCuePlayer?.Play(coasterBeacon);
                            }

                            if (aimCue.Speech is { } aimSpeech && config.EnableSpeech)
                            {
                                output.Speak(aimSpeech, true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        speedSquareCoasterAimReadout.Reset();
                        LogRuntimeFault(
                            $"Speed Square coaster target readout reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    try
                    {
                        // The chocobo racing screens, each read only while its own
                        // native substate is the one dispatching.
                        if (!config.EnableChocoboSquareReadout ||
                            chocoboSquareReader is null ||
                            !isHostForeground)
                        {
                            chocoboSquareReadout.Reset();
                        }
                        else if (chocoboSquareReader.TryReadPhase(out var chocoboPhase))
                        {
                            // K, the same status key the rest of the mod uses, and
                            // short-circuited on the phase for the same reason Fort
                            // Condor's is on the module: nothing else that owns K
                            // may lose a press to a screen that is not up.
                            var chocoboStatusRequested =
                                chocoboPhase != ChocoboSquarePhase.None &&
                                foregroundInput.ObserveRisingEdge(0x4B);

                            IReadOnlyList<string> chocoboLines = Array.Empty<string>();
                            string? chocoboStatus = null;
                            switch (chocoboPhase)
                            {
                                case ChocoboSquarePhase.Betting
                                    when chocoboSquareReader.TryReadBetting(out var betting):
                                    chocoboLines = chocoboSquareReadout.ObserveBetting(betting);
                                    chocoboStatus = chocoboStatusRequested
                                        ? ChocoboSquareReadout.DescribeBettingStatus(betting)
                                        : null;
                                    break;
                                case ChocoboSquarePhase.Race
                                    when chocoboSquareReader.TryReadRace(out var race):
                                    chocoboLines = chocoboSquareReadout.ObserveRace(race);
                                    chocoboStatus = chocoboStatusRequested
                                        ? ChocoboSquareReadout.DescribeRaceStatus(race)
                                        : null;
                                    break;
                                case ChocoboSquarePhase.Results
                                    when chocoboSquareReader.TryReadResults(out var results):
                                    chocoboLines = chocoboSquareReadout.ObserveResults(results);
                                    chocoboStatus = chocoboStatusRequested
                                        ? ChocoboSquareReadout.DescribeResultsStatus(results)
                                        : null;
                                    break;
                                case ChocoboSquarePhase.None:
                                    chocoboSquareReadout.Reset();
                                    break;
                            }

                            // Last, so anything the screen has just changed is heard
                            // before the answer to the request.
                            if (chocoboStatus is not null)
                            {
                                chocoboLines = chocoboLines.Append(chocoboStatus).ToList();
                            }

                            if (config.EnableSpeech)
                            {
                                foreach (var chocoboLine in chocoboLines)
                                {
                                    output.Speak(chocoboLine, false);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        chocoboSquareReadout.Reset();
                        LogRuntimeFault(
                            $"Chocobo Square readout reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    try
                    {
                        // Same shared reader and readout as the x86 build, so the
                        // wind-up tick and the top-of-rise marker match on both.
                        if (!config.EnableWonderSquareBasketballCues ||
                            wonderSquareBasketballReader is null ||
                            !isHostForeground)
                        {
                            wonderSquareBasketballReadout.Reset();
                            wonderSquareBasketballReader?.Reset();
                        }
                        else if (wonderSquareBasketballReader.TryRead(out var basketball))
                        {
                            var cue = wonderSquareBasketballReadout.Observe(basketball);
                            if (cue.RiseStarted)
                            {
                                basketballWindUpCuePlayer?.Play("basketball wind-up");
                            }

                            if (cue.RiseSettled &&
                                basketballTopCuePlayer?.Play("basketball ball at the top") != true &&
                                config.EnableSpeech)
                            {
                                // A missing asset or an unopenable device must not
                                // swallow the landmark. Bounded to one attempt per
                                // wind-up, because the event itself fires once.
                                output.Speak("Ball at the top.", false);
                            }

                            if (cue.Speech is not null && config.EnableSpeech)
                            {
                                output.Speak(cue.Speech, false);
                            }
                        }
                        else
                        {
                            wonderSquareBasketballReadout.Reset();
                        }
                    }
                    catch (Exception ex)
                    {
                        wonderSquareBasketballReadout.Reset();
                        wonderSquareBasketballReader?.Reset();
                        LogRuntimeFault(
                            $"Basketball readout reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    try
                    {
                        if (!config.EnableWonderSquareArmWrestlingCues ||
                            wonderSquareArmWrestlingReader is null ||
                            !isHostForeground)
                        {
                            wonderSquareArmWrestlingReadout.Reset();
                        }
                        else if (wonderSquareArmWrestlingReader.TryRead(out var armWrestling))
                        {
                            var lean = wonderSquareArmWrestlingReadout.Observe(armWrestling, out var pose);
                            if (lean is not null)
                            {
                                // The contest asks for continuous [OK] presses and
                                // each press interrupts screen-reader speech, so the
                                // pose also sounds on its own device. One tone per
                                // revealed change, never per poll.
                                var tone = pose switch
                                {
                                    WonderSquareArmWrestlingPose.PushingAhead or
                                        WonderSquareArmWrestlingPose.TheirArmDown => armWrestlingPushAheadCuePlayer,
                                    WonderSquareArmWrestlingPose.BeingPushedBack or
                                        WonderSquareArmWrestlingPose.YourArmDown => armWrestlingPushedBackCuePlayer,
                                    WonderSquareArmWrestlingPose.Level => armWrestlingLevelCuePlayer,
                                    _ => null
                                };
                                tone?.Play($"arm wrestling {pose}");
                                if (config.EnableSpeech)
                                {
                                    output.Speak(lean, true);
                                }
                            }
                        }
                        else
                        {
                            wonderSquareArmWrestlingReadout.Reset();
                        }
                    }
                    catch (Exception ex)
                    {
                        wonderSquareArmWrestlingReadout.Reset();
                        LogRuntimeFault(
                            $"Arm wrestling readout reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
                    }

                    try
                    {
                        if (!config.EnableWonderSquare3DBattlerCues ||
                            wonderSquare3DBattlerReader is null ||
                            !isHostForeground)
                        {
                            wonderSquare3DBattlerReadout.Reset();
                            wonderSquare3DBattlerReader?.Reset();
                        }
                        else if (wonderSquare3DBattlerReader.TryRead(out var battler))
                        {
                            // Through the shared delivery, so a two-line batch is one
                            // utterance here too rather than a second interrupt that
                            // cuts the first exchange off part way through.
                            foreach (var (battlerText, battlerInterrupt) in
                                     WonderSquare3DBattlerReadout.Deliver(
                                         wonderSquare3DBattlerReadout.Observe(battler)))
                            {
                                if (config.EnableSpeech)
                                {
                                    output.Speak(battlerText, battlerInterrupt);
                                }
                            }
                        }
                        else
                        {
                            wonderSquare3DBattlerReadout.Reset();
                        }
                    }
                    catch (Exception ex)
                    {
                        wonderSquare3DBattlerReadout.Reset();
                        wonderSquare3DBattlerReader?.Reset();
                        LogRuntimeFault(
                            $"3D Battler readout reset after a fault: {ex.Message}",
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

                        // P: where the battle line is. Gated on the module for the
                        // same reason, so nothing else that owns P loses a press
                        // to a fort battle.
                        var condorPlacementLineRequested =
                            condorIsActive &&
                            condorIsForeground &&
                            foregroundInput.ObserveRisingEdge(0x50);

                        // The battlefield navigator, on the same keys the field
                        // navigator uses: U and O for categories, J and L for
                        // targets, I to jump. Gated on the module so no other
                        // owner of these keys loses a press to a fort battle.
                        var condorNavigation = new List<CondorNavigationAction>();
                        if (condorIsActive && condorIsForeground)
                        {
                            foreach (var (virtualKey, action) in CondorNavigationKeys)
                            {
                                if (foregroundInput.ObserveRisingEdge(virtualKey))
                                {
                                    condorNavigation.Add(action);
                                }
                            }
                        }

                        foreach (var condorLine in pump.ObserveCondorBattle(
                            frame.Lifecycle.ModuleId,
                            condorStatusRequested,
                            now,
                            condorNavigation,
                            condorPlacementLineRequested))
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
                                openingMovieDescription.IsRunning,
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

                    // Outside the field-message gate on purpose: the tracker has to
                    // see the title module to clear itself when the player quits to
                    // title, and gating this on a reader the cue does not use would
                    // tie one feature's life to another's configuration.
                    try
                    {
                        fieldZoneTransitionCueCoordinator?.Observe(
                            isHostForeground &&
                            frame.Lifecycle.IsForeground &&
                            !frame.Lifecycle.IsShuttingDown,
                            now);
                    }
                    catch (Exception ex)
                    {
                        fieldZoneTransitionCueCoordinator?.Reset();
                        LogRuntimeFault(
                            $"Native field zone transition cue reset after a fault: {ex.Message}",
                            now,
                            ref lastRuntimeFault,
                            ref lastRuntimeFaultLogUtc);
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

                                if (openingMovieActive)
                                {
                                    if (config.EnableOpeningMovieDescription)
                                    {
                                        openingMovieDescription.Start();
                                    }
                                }
                                else if (openingMovieDescription.IsRunning)
                                {
                                    if (openingMovieDescription.ElapsedSeconds
                                        < OpeningMovieDescription.MovieEndSeconds)
                                    {
                                        log(
                                            "Opening movie ended early at " +
                                            $"{openingMovieDescription.ElapsedSeconds:0.0}s; " +
                                            "stopping screenreader description.");
                                    }

                                    openingMovieDescription.Stop();
                                }
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

                // Outside the frame-readable branch on purpose. The legacy runtime
                // ticks this from a hooked per-frame callback; this runtime has no
                // game tick, so it rides the worker's own ~35 ms iteration. Placed
                // inside the frame branch, a momentarily unreadable guest frame
                // would stall the description mid-sentence and drop the cues that
                // fell during it - the movie does not pause for our read failures.
                if (config.EnableOpeningMovieDescription)
                {
                    openingMovieDescription.Tick();
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
                    if (hookSet.RecoverQueueOverflow())
                    {
                        pump?.ResetMenuIngress();
                        tracker.Reset();
                        inGameMenuBridge?.Reset();
                        titleLoadMenuBridge?.ResetIngress();
                        nameEntryPromptSpeechCoordinator.Reset();
                        log("Translated menu queue filled; discarded incomplete observations and resumed capture.");
                    }
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

                    // A producer may overflow while this worker is draining. Clear
                    // that partial batch before any menu tracker can speak it.
                    if (hookSet.RecoverQueueOverflow())
                    {
                        pump?.ResetMenuIngress();
                        tracker.Reset();
                        inGameMenuBridge?.Reset();
                        titleLoadMenuBridge?.ResetIngress();
                        nameEntryPromptSpeechCoordinator.Reset();
                        log("Translated menu queue filled during drain; discarded incomplete observations and resumed capture.");
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
                        log($"Translated menu ingress degraded ({hookSet.DegradationReason}); disabling its full hook cohort.");
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
            cutsceneDescriptions?.SuspendNativeFilmNarration(FieldMovieNarrationStopReason.Unloaded);
            cutsceneDescriptions?.Reset();
            cutsceneNarrationSpeechTracker.Reset();
            fieldZoneSpeechCoordinator?.Reset();
            fieldZoneTransitionCueCoordinator?.Reset();
            fieldZoneTransitionCueCoordinator?.Dispose();
            openingMovieDescription.Stop();
            battleRendererHookSet?.Dispose();
            battleAccessibilityCoordinator?.Reset();
            hookSet?.Dispose();
            footstepCoordinator?.Dispose();
            fieldObjectSpatialCoordinator?.Dispose();
            // The detour comes out before the coordinators it queues into go away.
            controllerCaptureHook?.Dispose();
            directionalInputHook?.Dispose();
            fieldNavigationCoordinator?.Dispose();
            worldMapAccessibilityCoordinator?.Dispose();
            highwayAccessibilityCoordinator?.Dispose();
            // These were previously released inside the setup block, one statement
            // after they had been constructed, so every field-stack rebuild disposed
            // the very players it had just created and no arcade cue could ever
            // sound. They belong here, with the rest of the worker's teardown.
            basketballWindUpCuePlayer?.Dispose();
            basketballTopCuePlayer?.Dispose();
            armWrestlingLevelCuePlayer?.Dispose();
            armWrestlingPushAheadCuePlayer?.Dispose();
            armWrestlingPushedBackCuePlayer?.Dispose();
            speedSquareCoasterTargetCuePlayer?.Dispose();
            fieldFootstepNavigationProbe?.Dispose();
            // Before the output is disposed at the end of this scope.
            Volatile.Write(ref lifecycleOutput, null);
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
