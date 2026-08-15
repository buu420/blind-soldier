using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded.Runtime;
using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces.Internal;

namespace Ff7.Accessibility.Reloaded;

public sealed class Mod : IModV1, IModV2
{
    private const int AddressMenuStringPointerTable = 0x009568E0;
    private const int AddressMenuTextRendererA = 0x0072D333;
    private const int AddressMenuTextRendererB = 0x0072F96E;
    private const int AddressMenuTextRenderer = 0x0072F9F4;
    private const int AddressInGameMenuTextDrawA = 0x006FAB2F;
    private const int AddressInGameMenuTextDrawB = 0x006F5B03;
    private const int AddressMenuCursorDrawA = 0x006F0D7D;
    private const int AddressMenuCursorDrawB = 0x006EB3B8;
    private const int AddressMenuWidgetUpdate = 0x006F4DB2;
    private const int AddressLoadMenuCreate = 0x00720EF0;
    private const int AddressLoadMenuDestroy = 0x00720F2F;
    private const int AddressFieldMessageOpen = 0x00769836;
    private const int AddressFieldMessagePreview = 0x0076B5D3;
    private const int AddressFieldMessageDataPointer = FieldMessageReader.AddressFieldMessageDataPointer;
    private const int AddressFieldMessageLineBuffer = FieldMessageReader.AddressFieldMessageLineBuffer;
    private const int AddressFieldWindowTextBuffers = FieldMessageReader.AddressFieldWindowTextBuffers;
    private const int AddressSavemap = 0x00DBFD38;
    private const int AddressMainMenuState = 0x00DC1294;
    private const int AddressMainMenuSelectedA = 0x00DC1208;
    private const int AddressMainMenuSelectedB = 0x00DC1120;
    private const int AddressMainMenuCursorIndex = 0x00DC1154;
    private const int AddressMainMenuTarget = 0x00DC12EC;
    private const int AddressMainMenuOpenFlag = 0x00DC1108;
    private const int AddressMainMenuEnabledMask = 0x00DC111C;
    private const int AddressMainMenuDisabledMask = 0x00DC1130;
    private const int AddressMainMenuAnimation = 0x0091AB04;
    private const int AddressStatusPartySlot = 0x00DCA478;
    private const int AddressBattleMenuRender = 0x006D797C;
    private const int AddressBattleUpdate = 0x006CE8B3;
    private const int AddressBattleResultsUpdate = 0x006C9543;
    private const int AddressBattleDamageDisplay = 0x005BB410;
    private const int VirtualKeyI = 0x49;
    private const int VirtualKeyJ = 0x4A;
    private const int VirtualKeyK = 0x4B;
    private const int VirtualKeyL = 0x4C;
    private const int VirtualKeyO = 0x4F;
    private const int VirtualKeyU = 0x55;
    private const int VirtualKeyF8 = 0x77;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyQ = 0x51;
    private const uint MemoryStateCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageGuard = 0x100;
    private const uint PageReadableMask = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    private IModLoaderV1? loader;
    private IModConfigV1? modConfig;
    private ILoggerV2? logger;
    private PrismNativeSpeaker? speaker;
    private FootstepSoundPlayer? footstepSoundPlayer;
    private FieldZoneTransitionCuePlayer? fieldZoneTransitionCuePlayer;
    private ImmediateWaveCuePlayer? swingingBarTimingCuePlayer;
    private ImmediateWaveCuePlayer? floor60ActionCuePlayer;
    private NavigationBeaconPlayer? floor60StatueBeaconPlayer;
    private HighwayAccessibilityCoordinator? highwayAccessibilityCoordinator;
    private NavigationBeaconPlayer? fieldExitCuePlayer;
    private NavigationBeaconPlayer? fieldLadderCuePlayer;
    private NavigationBeaconPlayer? fieldLadderMountCuePlayer;
    private readonly Dictionary<FieldObjectCueKind, NavigationBeaconPlayer> fieldObjectCuePlayers = new();
    private CosmoFootstepSequencer? cosmoFootstepSequencer;
    private Thread? monitorThread;
    private CancellationTokenSource? cancellation;
    private Ff7.Accessibility.Core.AccessibilityConfig config = new();
    private IReloadedHooks? hooks;
    private IHook<MenuTextRendererDelegate>? menuTextRendererHook;
    private IHook<InGameMenuTextDrawDelegate>? inGameMenuTextDrawHookA;
    private IHook<InGameMenuTextDrawDelegate>? inGameMenuTextDrawHookB;
    private IHook<MenuCursorDrawDelegate>? menuCursorDrawHookA;
    private IHook<MenuCursorDrawDelegate>? menuCursorDrawHookB;
    private IHook<MenuWidgetUpdateDelegate>? menuWidgetUpdateHook;
    private IHook<FieldMessageOpenDelegate>? fieldMessageOpenHook;
    private IHook<FieldMessagePreviewDelegate>? fieldMessagePreviewHook;
    private readonly Dictionary<int, IHook<FieldOpcodeWaitDelegate>> fieldOpcodeWaitHooks = new();
    private readonly Dictionary<int, FieldOpcodeWaitDelegate> fieldOpcodeWaitDetours = new();
    private readonly object fieldOpcodeWaitHookSync = new();
    private readonly FieldOpcodeHookTargetTracker fieldOpcodeHookTargetTracker = new();
    private readonly Dictionary<int, IHook<FieldOpcodeSoundDelegate>> fieldOpcodeSoundHooks = new();
    private readonly Dictionary<int, FieldOpcodeSoundDelegate> fieldOpcodeSoundDetours = new();
    private readonly object fieldOpcodeSoundHookSync = new();
    private readonly FieldOpcodeHookTargetTracker fieldOpcodeSoundHookTargetTracker = new();
    private readonly Dictionary<int, IHook<FieldOpcodeCutsceneDelegate>> fieldOpcodeCutsceneHooks = new();
    private readonly Dictionary<int, FieldOpcodeCutsceneDelegate> fieldOpcodeCutsceneDetours = new();
    private readonly object fieldOpcodeCutsceneHookSync = new();
    private readonly FieldOpcodeHookTargetTracker fieldOpcodeCutsceneHookTargetTracker = new();
    private readonly HashSet<int> fieldOpcodeCutsceneHookAttemptTargets = [];
    private IHook<FieldOpcodeMessageDelegate>? fieldOpcodeMessageHook;
    private IHook<FieldOpcodeTimerDelegate>? fieldOpcodeTimerHook;
    private IHook<FieldOpcodeAskDelegate>? fieldOpcodeAskHook;
    private IHook<FieldOpcodeAskDelegate>? fieldOpcodeOriginalAskHook;
    private IHook<FieldOpcodeAskUpdateDelegate>? fieldOpcodeAskUpdateHook;
    private IHook<FfnxPlayVoiceDelegate>? ffnxPlayVoiceHook;
    private IHook<BattleMenuRenderDelegate>? battleMenuRenderHook;
    private IHook<BattleUpdateDelegate>? battleUpdateHook;
    private IHook<BattleTextActiveDelegate>? battleTextActiveHook;
    private IHook<BattleResultsUpdateDelegate>? battleResultsUpdateHook;
    private IHook<BattleDamageDisplayDelegate>? battleDamageDisplayHook;
    private MenuTextRendererDelegate? menuTextRendererDetour;
    private InGameMenuTextDrawDelegate? inGameMenuTextDrawDetourA;
    private InGameMenuTextDrawDelegate? inGameMenuTextDrawDetourB;
    private MenuCursorDrawDelegate? menuCursorDrawDetourA;
    private MenuCursorDrawDelegate? menuCursorDrawDetourB;
    private MenuWidgetUpdateDelegate? menuWidgetUpdateDetour;
    private FieldMessageOpenDelegate? fieldMessageOpenDetour;
    private FieldMessagePreviewDelegate? fieldMessagePreviewDetour;
    private FieldOpcodeMessageDelegate? fieldOpcodeMessageDetour;
    private FieldOpcodeTimerDelegate? fieldOpcodeTimerDetour;
    private FieldOpcodeAskDelegate? fieldOpcodeAskDetour;
    private FieldOpcodeAskDelegate? fieldOpcodeOriginalAskDetour;
    private FieldOpcodeAskUpdateDelegate? fieldOpcodeAskUpdateDetour;
    private FfnxPlayVoiceDelegate? ffnxPlayVoiceDetour;
    private BattleMenuRenderDelegate? battleMenuRenderDetour;
    private BattleUpdateDelegate? battleUpdateDetour;
    private BattleTextActiveDelegate? battleTextActiveDetour;
    private BattleResultsUpdateDelegate? battleResultsUpdateDetour;
    private BattleDamageDisplayDelegate? battleDamageDisplayDetour;
    private MenuTextRenderDiagnostics? menuTextRenderDiagnostics;
    private MenuTextRenderDiagnostics? inGameMenuTextDrawDiagnostics;
    private readonly TitleMenuVisualDetector titleMenuVisualDetector = new();
    private OpeningMovieDescription? openingMovieDescription;
    private OpeningMovieAudioTrackPlayer? openingMovieAudioTrackPlayer;
    private string modDirectory = AppContext.BaseDirectory;
    private string? gameRootDirectory;
    private Ff7GameLanguageContext? gameLanguage;
    private BlindSoldierLocalizer localizer = BlindSoldierLocalizer.Create(
        Ff7GameLanguages.Get(Ff7GameLanguage.English),
        modDirectory: null);
    private string logPath = ModPaths.ResolveLogPath(null);
    private string lastTitleMenuItem = string.Empty;
    private int titleMenuMissCount;
    private bool menuTableDumped;
    private DateTime lastOpeningMovieProbeAt = DateTime.MinValue;
    private bool openingMovieDetected;
    private bool openingMoviePlaybackActive;
    private bool ffnxRuntimeLoaded;
    private CurrentProcessLegacyAddressSpace? currentProcessLegacyAddressSpace;
    private FfnxPopupStateReader? ffnxPopupStateReader;
    private readonly FfnxPopupSpeechTracker ffnxPopupSpeechTracker = new();
    private DateTime lastFfnxPopupReaderProbeAt = DateTime.MinValue;
    private string lastFfnxPopupReaderDiagnostic = string.Empty;
    private readonly OpeningMovieProbeLifetime openingMovieProbeLifetime = new();
    private int menuTextRendererErrorCount;
    private int inGameMenuTextDrawErrorCount;
    private int menuCursorDrawErrorCount;
    private int menuWidgetUpdateErrorCount;
    private FieldVisibleWindowSpeechCoordinator fieldVisibleWindowSpeechCoordinator =
        new(TimeSpan.FromMilliseconds(450));
    private readonly NativeFieldMessageOwnershipTracker nativeFieldMessageOwnershipTracker =
        new(TimeSpan.FromSeconds(5));
    private MainMenuSpeechScheduler mainMenuSpeechScheduler = new(TimeSpan.FromMilliseconds(90));
    private RenderedMenuTextSpeechTracker renderedMenuTextSpeechTracker = new(TimeSpan.FromMilliseconds(90));
    private TitleLoadMenuSpeechTracker? titleLoadMenuSpeechTracker;
    private TitleLoadMenuDataReader? titleLoadMenuDataReader;
    private SaveMenuSpeechTracker saveMenuSpeechTracker = new(TimeSpan.Zero);
    private readonly ActiveMenuFrameSpeechCoordinator activeMenuFrameSpeechCoordinator = new();
    private readonly MateriaTutorialSpeechTracker materiaTutorialSpeechTracker = new();
    private readonly StaticMenuCursorSpeechTracker staticMenuCursorSpeechTracker = new(TimeSpan.FromMilliseconds(30));
    private readonly StatusMenuSpeechTracker statusMenuSpeechTracker = new(TimeSpan.FromMilliseconds(30));
    private PartyFormationSpeechTracker partyFormationSpeechTracker = new(TimeSpan.Zero);
    private readonly NativeTextDrawEventQueue nativeTextDrawEventQueue = new(capacity: 2048, maxTextBytes: 128);
    private readonly NativeFieldHookEventQueue nativeFieldHookEventQueue = new(capacity: 2048);
    private readonly FfnxVoicePlaybackEventQueue ffnxVoicePlaybackEventQueue =
        new(capacity: 128, maxFieldNameBytes: 16);
    private readonly FfnxVoicePlaybackTracker ffnxVoicePlaybackTracker =
        new(TimeSpan.FromSeconds(60), Stopwatch.Frequency);
    private long lastFfnxVoicePlaybackDroppedCount;
    private DateTime lastFfnxVoiceHookProbeAt = DateTime.MinValue;
    private string lastFfnxVoiceHookDiagnostic = string.Empty;
    private bool echoSCompatibilityActive;
    private readonly ExitShortcutDiagnosticsTracker exitShortcutDiagnosticsTracker =
        new(TimeSpan.FromMilliseconds(750));
    private Module19WriterProbe? module19WriterProbe;
    private long lastNativeTextDrawDroppedCount;
    private long lastNativeFieldHookDroppedCount;
    private int nativeFieldHookCaptureErrorCount;
    private int lastNativeFieldHookCaptureErrorCount;
    private int lastFieldCutsceneContextUnavailableCount;
    private string lastSaveMenuStateDiagnostic = string.Empty;
    private ActiveMenuWidgetReader? activeMenuWidgetReader;
    private ActiveMenuWidgetFrameBridge? activeMenuWidgetFrameBridge;
    private FieldDialogueDrawSpeechTracker fieldDialogueDrawSpeechTracker = new(TimeSpan.FromMilliseconds(250));
    private NameEntryMenuSpeechTracker nameEntryMenuSpeechTracker = new(TimeSpan.Zero);
    private NameEntryNativeNameTracker nameEntryNativeNameTracker = new(TimeSpan.FromMilliseconds(750));
    private NameEntryStateReader? nameEntryStateReader;
    private SaveMenuStateReader? saveMenuStateReader;
    private DeferredZoneSpeechTracker deferredZoneSpeechTracker = new();
    private FieldFootstepTracker fieldFootstepTracker = new(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(300));
    private FieldFootstepDistanceProbe fieldFootstepDistanceProbe = new();
    private FieldFootstepCadence fieldNavigationCadence = FieldFootstepCadence.Walk;
    private FieldZoneTransitionCueTracker fieldZoneTransitionCueTracker =
        new(TimeSpan.FromMilliseconds(300));
    private readonly SwingingBarTimingCueTracker swingingBarTimingCueTracker = new();
    private SquatMinigameCueCoordinator? squatMinigameCueCoordinator;
    private Floor60SoldierTurnCueTracker floor60SoldierTurnCueTracker = new();
    private Floor60GuardTimingStateReader? floor60GuardTimingStateReader;
    private FieldMessageReader? fieldMessageReader;
    private FieldCountdownReader? fieldCountdownReader;
    private readonly FieldCountdownSpeechCoordinator fieldCountdownSpeechCoordinator = new();
    private FlevelFieldTextResolver? flevelFieldTextResolver;
    private readonly FieldOpcodeMessageSpeechGate fieldOpcodeMessageSpeechGate = new();
    private readonly FieldAskChoiceSpeechTracker fieldAskChoiceSpeechTracker = new();
    private FieldOpcodeAddressResolver? fieldOpcodeAddressResolver;
    private FieldOpcodeParameterReader? fieldOpcodeParameterReader;
    private FieldScriptContextReader? fieldScriptContextReader;
    private LoadedFieldScriptIdentityReader? loadedFieldScriptIdentityReader;
    private LoadedFieldScriptIdentity? loadedFieldScriptIdentity;
    private readonly HashSet<string> loggedLoadedFieldScriptIdentities = [];
    private EchoSFieldCutsceneDescriptionTracker fieldCutsceneDescriptionTracker = new();
    private readonly EchoSDisclaimerSpeechTracker echoSDisclaimerSpeechTracker = new();
    private readonly EchoSReactorTimerOverrideTracker echoSReactorTimerOverrideTracker = new();
    private readonly FieldCutsceneSpeechPriority fieldCutsceneSpeechPriority = new();
    private FieldPositionReader? fieldPositionReader;
    private FieldLadderStateReader? fieldLadderStateReader;
    private FieldNavigationControlReader? fieldNavigationControlReader;
    private FieldNavigationInputReader? fieldNavigationInputReader;
    private FieldAudibleCueOwnershipStateReader? fieldAudibleCueStateReader;
    private FieldRunStateReader? fieldRunStateReader;
    private InventoryItemReader? inventoryItemReader;
    private MagicMenuSelectionReader? magicMenuSelectionReader;
    private ConfigMenuValueReader? configMenuValueReader;
    private Kernel2TextDatabase? kernel2TextDatabase;
    private SavemapPartyReader? savemapPartyReader;
    private OrderMenuSelectionReader? orderMenuSelectionReader;
    private EquipmentMenuSelectionReader? equipmentMenuSelectionReader;
    private MateriaMenuSelectionReader? materiaMenuSelectionReader;
    private ShopMenuStateReader? shopMenuStateReader;
    private readonly ShopMenuSpeechTracker shopMenuSpeechTracker = new();
    private BattleHookAddressResolver? battleHookAddressResolver;
    private BattleStateReader? battleStateReader;
    private BattleResultsReader? battleResultsReader;
    private BattleDamagePopupReader? battleDamagePopupReader;
    private TifaSlotResultReader? tifaSlotResultReader;
    private BattleMenuFrameSpeechCoordinator battleMenuFrameSpeechCoordinator = new();
    private readonly BattleTargetSpeechTracker battleTargetSpeechTracker = new();
    private BattleMessageSpeechTracker battleMessageSpeechTracker = new(_ => null);
    private readonly BattleResultsSpeechTracker battleResultsSpeechTracker = new();
    private readonly BattleDamageSpeechTracker battleDamageSpeechTracker = new();
    private readonly BattleEncounterSpeechTracker battleEncounterSpeechTracker = new();
    private readonly BattleEnemyActionSpeechTracker battleEnemyActionSpeechTracker = new();
    private readonly BattleStatusSpeechTracker battleStatusSpeechTracker = new();
    private readonly BattleStatusHotkeyController battleStatusHotkeyController = new();
    private readonly BattleStatusLimitKeyFrameRouter battleStatusLimitKeyFrameRouter = new();
    private readonly TifaSlotSpeechTracker tifaSlotSpeechTracker = new();
    private volatile bool battleVictoryActive;
    private FootstepProbeScheduler? footstepProbeScheduler;
    private FieldNavigationObjectReader? fieldNavigationObjectReader;
    private FieldNavigationNpcReader? fieldNavigationNpcReader;
    private FieldGatewayTargetReader? fieldGatewayTargetReader;
    private FieldScriptNavigationCatalog? fieldScriptNavigationCatalog;
    private Func<int, IReadOnlyList<FieldScriptNavigationTransition>>? fieldNavigationTransitionProvider;
    private FieldScriptLineStateReader? fieldScriptLineStateReader;
    private NativeFieldExitTargetProvider? nativeFieldExitTargetProvider;
    private ReachableFieldExitTargetProvider? reachableFieldExitTargetProvider;
    private FieldStoryTargetReader? fieldStoryTargetReader;
    private FieldWalkmeshRoutePlanner? fieldNavigationRoutePlanner;
    private FieldObjectProximityCueTracker? fieldObjectProximityCueTracker;
    private FieldExitProximityCueTracker? fieldExitProximityCueTracker;
    private FieldLadderProximityCueTracker? fieldLadderProximityCueTracker;
    private FieldLadderMountCueTracker? fieldLadderMountCueTracker;
    private bool fieldLadderMountCueActive;
    private NativeFieldNavigationProgressBar? fieldNavigationProgressBar;
    private IntervalFieldNavigationProgressSink? fieldNavigationProgressSink;
    private WorldMapStateReader? worldMapStateReader;
    private WorldMapEntityReader? worldMapEntityReader;
    private readonly Dictionary<(int MapType, int ProgressStage), WorldMapRuntimeContext> worldMapRuntimes = [];
    private NavigationAutoWalkController? navigationAutoWalkController;
    private NavigationAutoWalkDomain pendingNavigationAutoWalkToggle;
    private string lastNavigationAutoWalkFailure = string.Empty;
    private NativeFieldNavigationProgressBar? worldMapNavigationProgressBar;
    private IntervalFieldNavigationProgressSink? worldMapNavigationProgressSink;
    private NavigationBeaconPlayer? worldMapNavigationBeaconPlayer;
    private FieldNavigationController fieldNavigationController = new(FieldNavigationTargetSource.CreateOpeningReactorRoute());
    private readonly FieldNavigationGuidanceRepeatGate fieldNavigationGuidanceRepeatGate = new();
    private DateTime lastFieldMessageScanAt = DateTime.MinValue;
    private DateTime lastFieldFootstepScanAt = DateTime.MinValue;
    private DateTime lastFieldNavigationScanAt = DateTime.MinValue;
    private DateTime lastFieldObjectCueScanAt = DateTime.MinValue;
    private DateTime lastFieldExitCueScanAt = DateTime.MinValue;
    private DateTime lastFieldLadderCueScanAt = DateTime.MinValue;
    private DateTime lastNavigationSpeechAt = DateTime.MinValue;
    private int observedFieldMessageFieldId = -1;
    private string lastFieldMessageCandidateText = string.Empty;
    private string lastFieldMessageCandidateSource = string.Empty;
    private string lastFieldMessageWindowDiagnostics = string.Empty;
    private readonly PendingNativeFieldSpeechQueue pendingNativeFieldSpeech = new(capacity: 16);
    // A one-shot noninterrupt obligation for the first cursor row that follows
    // a successfully spoken prompt.
    private readonly HashSet<NativeFieldMessageIdentity> partiallyDeliveredNativeFieldSpeech = [];
    // Exact ASK tokens whose native utterance has not yet conveyed all visible
    // prompt/selection information. This remains an ordering blocker even when
    // the retry queue is temporarily empty.
    private readonly HashSet<NativeFieldMessageIdentity> incompleteNativeFieldSpeech = [];
    private int fieldMessageReaderErrorCount;
    private int fieldOpcodeMessageErrorCount;
    private NativeFieldMessageIdentity? activeFieldAskIdentity;
    private long nextFieldAskLifecycleToken;
    [ThreadStatic]
    private static int fieldOpcodeAskDetourDepth;
    private readonly Dictionary<NativeFieldMessageIdentity, string> acceptedNativeAskPromptKeys = [];
    private readonly HashSet<NativeFieldMessageIdentity> begunNativeAskLifecycles = [];
    private readonly NativeAskPollingFallbackStateTracker nativeAskPollingFallbackState = new();
    private int fieldCutsceneDescriptionErrorCount;
    private int fieldCutsceneContextUnavailableCount;
    private int lastFieldOpcodeWaitHookAttemptTarget;
    private int lastFieldOpcodeSoundHookAttemptTarget;
    private string lastFieldOpcodeCutsceneResolutionDiagnostic = string.Empty;
    private readonly object fieldCutsceneDescriptionSync = new();
    private readonly Queue<FieldCutsceneDescriptionCue> pendingFieldCutsceneDescriptions = new();
    private readonly HashSet<FieldCutsceneDescriptionKey> observedFieldCutsceneOpcodes = [];
    private string lastDeferredZoneLogText = string.Empty;
    private string lastFieldPositionDiagnosticState = string.Empty;
    private string lastFieldNavigationExitsDiagnostic = string.Empty;
    private string lastFieldNavigationObjectsDiagnostic = string.Empty;
    private string lastFieldNavigationNpcsDiagnostic = string.Empty;
    private string lastFieldNavigationStoryDiagnostic = string.Empty;
    private string lastFieldNavigationControlDiagnostic = string.Empty;
    private string lastFieldNavigationInputDiagnostic = string.Empty;
    private string lastFieldNavigationProgressDiagnostic = string.Empty;
    private string lastFieldNavigationRouteDiagnostic = string.Empty;
    private uint lastFieldPositionModelBase;
    private string lastFieldRunStateDiagnostic = string.Empty;
    private string lastFieldLadderStateDiagnostic = string.Empty;
    private int fieldFootstepErrorCount;
    private string lastSuppressedFootstepKey = string.Empty;
    private int fieldNavigationErrorCount;
    private int worldMapAccessibilityErrorCount;
    private DateTime lastWorldMapScanAt = DateTime.MinValue;
    private string lastWorldMapStateDiagnostic = string.Empty;
    private string lastWorldMapEntityDiagnostic = string.Empty;
    private string lastWorldMapNavigationDiagnostic = string.Empty;
    private string lastWorldMapFootstepDiagnostic = string.Empty;
    private bool worldMapWasActive;
    private int fieldObjectCueErrorCount;
    private int fieldExitCueErrorCount;
    private int fieldLadderCueErrorCount;
    private FieldAudibleCueState fieldAudibleCueState = new(true, "initializing", 0, 0, 0, 0);
    private string lastFieldAudibleCueStateDiagnostic = string.Empty;
    private string lastFieldNavigationDiagnostic = string.Empty;
    private readonly NavigationKeyPressTracker navigationKeyPressTracker = new();
    private readonly NavigationKeyPressTracker repeatLastSpeechKeyTracker = new();
    private readonly RepeatLastSpeechController repeatLastSpeechController = new();
    private NavigationProgressController navigationProgressController = new(true, 5);
    private readonly ForegroundProcessGate foregroundProcessGate = new(
        GetForegroundWindow,
        GetForegroundWindowProcessId,
        (uint)Environment.ProcessId);
    private DateTime lastMainMenuScanAt = DateTime.MinValue;
    private MainMenuStateReader? mainMenuStateReader;
    private string lastMainMenuSelectionText = string.Empty;
    private int mainMenuReaderErrorCount;
    private int statusMenuReaderErrorCount;
    private int configMenuReaderErrorCount;
    private readonly Dictionary<uint, string> lastMenuWidgetDiagnosticStates = new();
    private string lastActiveMenuWidgetDiagnostic = string.Empty;
    private readonly object titleMenuCursorSync = new();
    private TitleMenuCursorSelection? pendingTitleMenuCursorSelection;
    private DateTime pendingTitleMenuCursorSeenAt = DateTime.MinValue;
    private string lastTitleMenuCursorSpokenKey = string.Empty;
    private string lastTitleMenuCursorObservedKey = string.Empty;
    private string lastTitleMenuCursorDiagnosticKey = string.Empty;
    private string lastNameEntryCursorDiagnosticKey = string.Empty;
    private int battleHookErrorCount;
    private int battleSessionActive;
    private BlindSoldierRuntimeLease? runtimeLease;
    private int started;

    public Action Disposing { get; } = () => { };

    public void Start(IModLoaderV1 loader)
    {
        this.loader = loader;
        logger = loader.GetLogger() as ILoggerV2;
        LogLegacyStartupDiagnostics();
        LoadConfig(null);
        StartWithRuntimeOwnership();
    }

    public void StartEx(IModLoaderV1 loader, IModConfigV1 modConfig)
    {
        this.loader = loader;
        this.modConfig = modConfig;
        logger = loader.GetLogger() as ILoggerV2;
        LogLegacyStartupDiagnostics();
        LoadConfig(modConfig);
        StartWithRuntimeOwnership();
    }

    public void Suspend()
    {
        if (runtimeLease is null)
        {
            return;
        }

        fieldCountdownSpeechCoordinator.Reset();
        ffnxVoicePlaybackTracker.Reset();
        fieldNavigationGuidanceRepeatGate.Reset();
        saveMenuSpeechTracker.Reset();
        DiscardCompetingSaveMenuSpeech();
        squatMinigameCueCoordinator?.Reset();
        floor60SoldierTurnCueTracker.Reset();
        floor60StatueBeaconPlayer?.StopAll();
        floor60ActionCuePlayer?.Dispose();
        highwayAccessibilityCoordinator?.Reset("mod suspended");
        navigationAutoWalkController?.Reset();
        pendingNavigationAutoWalkToggle = NavigationAutoWalkDomain.None;
        ResetWorldMapAccessibility("mod suspended");
        Speak("Final Fantasy Seven accessibility mod suspended.");
    }

    public void Resume()
    {
        if (runtimeLease is null)
        {
            return;
        }

        Speak("Final Fantasy Seven accessibility mod resumed.");
    }

    public void Unload()
    {
        try
        {
            fieldCountdownSpeechCoordinator.Reset();
            ffnxVoicePlaybackTracker.Reset();
            saveMenuSpeechTracker.Reset();
            squatMinigameCueCoordinator?.Reset();
            cancellation?.Cancel();
            if (monitorThread is { IsAlive: true } && Thread.CurrentThread != monitorThread)
            {
                monitorThread.Join(TimeSpan.FromSeconds(1));
            }

            module19WriterProbe?.Dispose();
            openingMovieAudioTrackPlayer?.Dispose();
            footstepSoundPlayer?.Dispose();
            fieldZoneTransitionCuePlayer?.Dispose();
            swingingBarTimingCuePlayer?.Dispose();
            floor60ActionCuePlayer?.Dispose();
            floor60StatueBeaconPlayer?.Dispose();
            highwayAccessibilityCoordinator?.Dispose();
            navigationAutoWalkController?.Dispose();
            navigationAutoWalkController = null;
            pendingNavigationAutoWalkToggle = NavigationAutoWalkDomain.None;
            fieldExitCuePlayer?.Dispose();
            fieldLadderCuePlayer?.Dispose();
            fieldLadderMountCuePlayer?.Dispose();
            fieldNavigationController.Reset();
            fieldNavigationGuidanceRepeatGate.Reset();
            fieldNavigationProgressSink?.Dispose();
            fieldNavigationProgressBar?.Dispose();
            ResetWorldMapAccessibility("mod unloaded");
            worldMapNavigationBeaconPlayer?.Dispose();
            worldMapNavigationProgressSink?.Dispose();
            worldMapNavigationProgressBar?.Dispose();
            worldMapRuntimes.Clear();
            foreach (var player in fieldObjectCuePlayers.Values)
            {
                player.Dispose();
            }

            fieldObjectCuePlayers.Clear();
            speaker?.Dispose();
        }
        finally
        {
            ReleaseRuntimeOwnership();
        }
    }

    public bool CanUnload() => true;

    public bool CanSuspend() => true;
    private void LogLegacyStartupDiagnostics()
    {
        var snapshot = LegacyStartupDiagnostics.Capture();
        Log(string.Format(
            "Startup diagnostics: pid={0}, bitness={1}.",
            Environment.ProcessId,
            snapshot.Is64Bit ? "x64" : "x86"));
        foreach (var module in snapshot.NativeModules)
        {
            Log("Startup diagnostics native module: " + module);
        }

        foreach (var assembly in snapshot.ManagedAssemblies)
        {
            Log("Startup diagnostics managed assembly: " + assembly);
        }

        Log("Startup diagnostics classification: " + LegacyStartupDiagnostics.Classify(snapshot) + ".");
    }


    private void StartWithRuntimeOwnership()
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return;
        }

        var acquired = BlindSoldierRuntimeLease.TryAcquire(Environment.ProcessId, out runtimeLease);
        Log("Startup runtime lease: " + (acquired ? "acquired" : "duplicate instance rejected") + ".");
        if (!acquired)
        {
            Log("Another Blind Soldier runtime already owns accessibility output for this process; this duplicate instance will remain inactive.");
            return;
        }

        try
        {
            StartCore();
            Log("hooks and Prism speech backend initialized");
        }
        catch
        {
            ReleaseRuntimeOwnership();
            throw;
        }
    }

    private void ReleaseRuntimeOwnership()
    {
        Interlocked.Exchange(ref runtimeLease, null)?.Dispose();
        Volatile.Write(ref started, 0);
    }

    private void StartCore()
    {
        navigationProgressController = new NavigationProgressController(
            config.EnableNavigationProgressIndicators,
            config.NavigationProgressIntervalPercent);
        Log("Starting FFVII Accessibility Reloaded mod.");
        Log($"Process: {Process.GetCurrentProcess().ProcessName} {Environment.Is64BitProcess switch { true => "x64", false => "x86" }}");
        Log($"Menu string pointer table: 0x{AddressMenuStringPointerTable:X8}");
        Log($"Menu text renderer candidates: 0x{AddressMenuTextRendererA:X8}, 0x{AddressMenuTextRendererB:X8}");
        Log($"Menu text renderer hook target: 0x{AddressMenuTextRenderer:X8}");
        Log($"In-game menu text draw hook targets: 0x{AddressInGameMenuTextDrawA:X8}, 0x{AddressInGameMenuTextDrawB:X8}");
        Log($"Menu cursor draw hook targets: 0x{AddressMenuCursorDrawA:X8}, 0x{AddressMenuCursorDrawB:X8}");
        Log($"Active menu widget hook target: 0x{AddressMenuWidgetUpdate:X8}");
        Log($"Load menu lifecycle candidates: 0x{AddressLoadMenuCreate:X8}, 0x{AddressLoadMenuDestroy:X8}");
        Log($"Field message hook targets: open=0x{AddressFieldMessageOpen:X8}, preview=0x{AddressFieldMessagePreview:X8}");
        Log($"Field message data pointer: 0x{AddressFieldMessageDataPointer:X8}");
        Log($"Field message buffers: line=0x{AddressFieldMessageLineBuffer:X8}, windows=0x{AddressFieldWindowTextBuffers:X8}");
        Log($"Field current dialog string pointer: 0x{FieldDialogStringReader.AddressCurrentDialogStringPointer:X8}");
        Log(
            $"Field opcode resolver: fieldInit=0x{FieldOpcodeAddressResolver.AddressFieldInitEvent:X8}, " +
            $"executeCallOffset=0x{FieldOpcodeAddressResolver.ExecuteOpcodeCallOffset:X}, " +
            $"tableOffset=0x{FieldOpcodeAddressResolver.ExecuteOpcodeTableOffset:X}, " +
            $"WAIT=0x{FieldOpcodeAddressResolver.OpcodeWaitIndex:X2}, " +
            $"MESSAGE=0x{FieldOpcodeAddressResolver.OpcodeMessageIndex:X2}, ASK=0x{FieldOpcodeAddressResolver.OpcodeAskIndex:X2}, " +
            $"askUpdateCallOffset=0x{FieldOpcodeAddressResolver.AskUpdateLoopCallOffset:X}");
        Log(
            $"Field opcode parameters: scriptPtr=0x{FieldOpcodeParameterReader.AddressFieldScriptPtr:X8}, " +
            $"entity=0x{FieldOpcodeParameterReader.AddressCurrentEntityId:X8}, scriptPos=0x{FieldOpcodeParameterReader.AddressFieldCurrScriptPosition:X8}");
        Log(
            $"Field script context: scriptIds=0x{FieldScriptContextReader.AddressCurrentEntityScriptId:X8}, " +
            $"priorities=0x{FieldScriptContextReader.AddressCurrentEntityScriptPriority:X8}, " +
            $"cutsceneCues={FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions().Count}");
        Log(
            $"Field live position addresses: module=0x{FieldPositionReader.AddressCurrentModule:X8}, field=0x{FieldPositionReader.AddressFieldId:X8}, " +
            $"modelId=0x{FieldPositionReader.AddressFieldCurrentModelId:X8}, modelCount=0x{FieldPositionReader.AddressFieldNumModels:X8}, " +
            $"modelsPtr=0x{FieldPositionReader.AddressFieldModelsPtr:X8}, modelObjs=0x{FieldPositionReader.AddressFieldModelsObjs:X8}");
        Log(
            $"Field run state resolver: fieldLoop=0x{FieldRunStateReader.AddressFieldLoopSub:X8}, " +
            $"resolverCallOffset=0x{FieldRunStateReader.RunStatusResolverCallOffset:X}, " +
            $"runPointerOffset=0x{FieldRunStateReader.RunButtonStatusPointerOffset:X}");
        Log($"Field savemap diagnostic base: 0x{AddressSavemap:X8} (not used for footstep triggers).");
        Log($"Main menu state: state=0x{AddressMainMenuState:X8}, selectedA=0x{AddressMainMenuSelectedA:X8}, selectedB=0x{AddressMainMenuSelectedB:X8}, cursor=0x{AddressMainMenuCursorIndex:X8}");
        Log(
            $"Config native values: settings=0x{ConfigMenuValueReader.AddressSettingsBits:X8}, " +
            $"battleSpeed=0x{ConfigMenuValueReader.AddressBattleSpeed:X8}, battleMessage=0x{ConfigMenuValueReader.AddressBattleMessageSpeed:X8}, " +
            $"fieldMessage=0x{ConfigMenuValueReader.AddressFieldMessageSpeed:X8}, soundState=0x{ConfigMenuValueReader.AddressSoundModalState:X8}, " +
            $"music=0x{ConfigMenuValueReader.AddressMusicVolume:X8}, sfx=0x{ConfigMenuValueReader.AddressSoundEffectsVolume:X8}");
        Log(
            $"Battle hook targets: menu=0x{AddressBattleMenuRender:X8}, update=0x{AddressBattleUpdate:X8}, " +
            $"results=0x{AddressBattleResultsUpdate:X8}");
        Log($"Mod directory: {modDirectory}");
        Log($"Log path: {logPath}");
        gameRootDirectory = ResolveGameRootDirectory();
        Log($"Game root directory: {gameRootDirectory ?? "<unknown>"}");
        var executablePath = Process.GetCurrentProcess().MainModule?.FileName;
        gameLanguage = Ff7GameLanguageDetector.Detect(
            gameRootDirectory ?? AppContext.BaseDirectory,
            config.GameLanguage,
            executablePath,
            steamManifestPaths: null,
            log: Log);
        Ff7EncodedTextDecoder.SetDefaultLanguage(gameLanguage.Descriptor);
        localizer = BlindSoldierLocalizer.Create(gameLanguage.Descriptor, modDirectory, Log);
        if (gameLanguage.Language != Ff7GameLanguage.English && config.EnableOpeningMovieAudioTrack)
        {
            Log("The packaged opening-movie audio description is English; localized Prism cues remain available as fallback.");
        }
        ffnxRuntimeLoaded = FfnxRuntimeDetector.IsLoaded(Process.GetCurrentProcess());
        Log($"Opening movie audio backend: Reloaded synchronized track (FFNx loaded={ffnxRuntimeLoaded}).");

        var prismStart = Stopwatch.StartNew();
        speaker = new PrismNativeSpeaker(Log);
        openingMovieDescription = new OpeningMovieDescription((text, interrupt) => Speak(text, interrupt));
        openingMovieAudioTrackPlayer = new OpeningMovieAudioTrackPlayer(
            ResolveOpeningMovieAudioTrackPath(),
            config.OpeningMovieAudioTrackVolumePercent,
            Log);
        fieldVisibleWindowSpeechCoordinator = new FieldVisibleWindowSpeechCoordinator(
            TimeSpan.FromMilliseconds(Math.Max(100, config.FieldMessageStableMs)));
        var legacyAddressSpace = new CurrentProcessLegacyAddressSpace();
        currentProcessLegacyAddressSpace = legacyAddressSpace;
        squatMinigameCueCoordinator = new SquatMinigameCueCoordinator(
            new SquatMinigameStateReader(legacyAddressSpace));
        highwayAccessibilityCoordinator?.Dispose();
        highwayAccessibilityCoordinator = new HighwayAccessibilityCoordinator(
            config,
            legacyAddressSpace,
            modDirectory,
            (text, interrupt) => { _ = Speak(text, interrupt); },
            Log);
        navigationAutoWalkController?.Dispose();
        navigationAutoWalkController = NavigationAutoWalkController.CreateCurrentProcess();
        pendingNavigationAutoWalkToggle = NavigationAutoWalkDomain.None;
        lastNavigationAutoWalkFailure = string.Empty;
        floor60GuardTimingStateReader = new Floor60GuardTimingStateReader(legacyAddressSpace);
        TryInitializeFfnxPopupReader(force: true);
        mainMenuSpeechScheduler = new MainMenuSpeechScheduler(TimeSpan.FromMilliseconds(Math.Max(0, config.MainMenuSpeechSettleMs)));
        renderedMenuTextSpeechTracker = new RenderedMenuTextSpeechTracker(TimeSpan.FromMilliseconds(Math.Max(0, config.RenderedMenuTextSpeechSettleMs)));
        saveMenuSpeechTracker = new SaveMenuSpeechTracker(
            TimeSpan.FromMilliseconds(Math.Max(0, config.InGameMenuSpeechSettleMs)));
        partyFormationSpeechTracker = new PartyFormationSpeechTracker(
            TimeSpan.FromMilliseconds(Math.Max(0, config.InGameMenuSpeechSettleMs)));
        titleLoadMenuDataReader = new TitleLoadMenuDataReader(legacyAddressSpace);
        titleLoadMenuSpeechTracker = new TitleLoadMenuSpeechTracker(
            TimeSpan.FromMilliseconds(Math.Max(0, config.TitleLoadMenuSpeechSettleMs)),
            titleLoadMenuDataReader.HasData,
            titleLoadMenuDataReader.ReadSlot);
        Log("Title Continue previews use the native renderer cache.");

        kernel2TextDatabase = Kernel2TextDatabase.TryCreate(gameLanguage, Log);
        fieldDialogueDrawSpeechTracker = new FieldDialogueDrawSpeechTracker(TimeSpan.FromMilliseconds(Math.Max(0, config.FieldDialogueDrawStableMs)));
        nameEntryMenuSpeechTracker = new NameEntryMenuSpeechTracker(TimeSpan.FromMilliseconds(Math.Max(0, config.NameEntryMenuSpeechSettleMs)));
        nameEntryNativeNameTracker = new NameEntryNativeNameTracker(TimeSpan.FromMilliseconds(750));
        deferredZoneSpeechTracker = new DeferredZoneSpeechTracker();
        fieldFootstepTracker = new FieldFootstepTracker(
            TimeSpan.FromMilliseconds(Math.Max(80, config.FieldFootstepWalkIntervalMs)),
            TimeSpan.FromMilliseconds(Math.Max(80, config.FieldFootstepRunIntervalMs)),
            Math.Max(1, config.FieldFootstepMeasuredRunSpeedUnitsPerSecond));
        Log(
            "Field footsteps use the spoken navigation distance scale; " +
            $"uncalibrated fields use {Math.Max(1, config.FieldNavigationSpeechDistanceUnitsPerCount)} units per footstep.");
        fieldFootstepDistanceProbe = new FieldFootstepDistanceProbe(
            Math.Max(1, config.FieldFootstepDistanceProbeReportSamples));
        fieldZoneTransitionCueTracker = new FieldZoneTransitionCueTracker(
            TimeSpan.FromMilliseconds(Math.Max(0, config.FieldZoneTransitionCueSettleMs)));
        fieldZoneTransitionCuePlayer?.Dispose();
        fieldZoneTransitionCuePlayer = config.EnableFieldZoneTransitionCue
            ? new FieldZoneTransitionCuePlayer(
                ResolveFieldZoneTransitionCueSoundPath(),
                config.FieldZoneTransitionCueVolumePercent,
                Log)
            : null;
        swingingBarTimingCueTracker.Reset();
        swingingBarTimingCuePlayer?.Dispose();
        swingingBarTimingCuePlayer = config.EnableFieldSwingingBarTimingCue
            ? new ImmediateWaveCuePlayer(
                ResolveFieldSwingingBarTimingCueSoundPath(),
                config.FieldSwingingBarTimingCueVolumePercent,
                "Swinging-bar jump timing cue",
                Log)
            : null;
        Log(
            $"Swinging-bar jump timing initialized: enabled={config.EnableFieldSwingingBarTimingCue}, " +
            $"field={SwingingBarTimingCueTracker.SwingingBarFieldId}, " +
            $"bank={SwingingBarTimingCueTracker.FrameCounterBank}, " +
            $"index={SwingingBarTimingCueTracker.FrameCounterIndex}, " +
            $"window={SwingingBarTimingCueTracker.SuccessWindowStart}-" +
            $"{SwingingBarTimingCueTracker.SuccessWindowEnd}.");
        Log(
            $"Wall Market squat prompts initialized: enabled={config.EnableSquatMinigamePrompts}, " +
            $"field={SquatMinigameStateReader.GymFieldId}, entity={SquatMinigameStateReader.CloudEntityId}, " +
            $"script={SquatMinigameStateReader.ControllerScriptId}, " +
            $"state=0x{SquatMinigameStateReader.AddressExpectedStep:X8}.");
        floor60SoldierTurnCueTracker = new Floor60SoldierTurnCueTracker(
            TimeSpan.FromMilliseconds(Math.Max(0, config.Floor60StatueBeaconIntervalMs)),
            Math.Max(0, config.Floor60StatueArrivalDistanceUnits),
            Floor60SoldierTurnCueTracker.ReactionLeadMillisecondsToTicks(
                config.Floor60GuardReactionLeadMilliseconds));
        var floor60CuePath = ResolveFloor60SoldierTurnCueSoundPath();
        floor60StatueBeaconPlayer?.Dispose();
        floor60StatueBeaconPlayer = config.EnableFloor60SoldierTurnCue
            ? new NavigationBeaconPlayer(
                floor60CuePath,
                config.Floor60SoldierTurnCueVolumePercent,
                Log)
            : null;
        floor60ActionCuePlayer?.Dispose();
        floor60ActionCuePlayer = config.EnableFloor60SoldierTurnCue
            ? new ImmediateWaveCuePlayer(
                floor60CuePath,
                config.Floor60SoldierTurnCueVolumePercent,
                "Floor 60 guard action cue",
                Log)
            : null;
        Log(
            $"Floor 60 guard accessibility initialized: enabled={config.EnableFloor60SoldierTurnCue}, " +
            $"field={Floor60SoldierTurnCueTracker.FloorId}, " +
            $"statues={string.Join(';', Floor60SoldierTurnCueTracker.HideSpots.Select(spot => $"{spot.SequenceIndex}:{spot.X},{spot.Y},t{spot.TriangleId}"))}, " +
            $"firstLines={string.Join(',', Floor60SoldierTurnCueTracker.FirstLineEntityIds)}, " +
            $"secondLines={string.Join(',', Floor60SoldierTurnCueTracker.SecondLineEntityIds)}, " +
            $"intervalMs={Math.Max(0, config.Floor60StatueBeaconIntervalMs)}, " +
            $"arrival={Math.Max(0, config.Floor60StatueArrivalDistanceUnits)}, " +
            $"reactionLeadMs={Math.Max(0, config.Floor60GuardReactionLeadMilliseconds)}, " +
            $"reactionLeadTicks={Floor60SoldierTurnCueTracker.ReactionLeadMillisecondsToTicks(config.Floor60GuardReactionLeadMilliseconds)}.");
        if (config.EnableFieldFootstepDistanceProbe)
        {
            Log(
                $"Field footstep distance probe enabled; reports every " +
                $"{Math.Max(1, config.FieldFootstepDistanceProbeReportSamples)} accepted samples.");
        }
        fieldMessageReader = new FieldMessageReader(legacyAddressSpace);
        fieldCountdownReader = new FieldCountdownReader(legacyAddressSpace);
        mainMenuStateReader = new MainMenuStateReader(legacyAddressSpace);
        nameEntryStateReader = new NameEntryStateReader(legacyAddressSpace);
        saveMenuStateReader = new SaveMenuStateReader(legacyAddressSpace);
        flevelFieldTextResolver = gameRootDirectory is null
            ? null
            : new FlevelFieldTextResolver(gameRootDirectory, gameLanguage);
        fieldOpcodeAddressResolver = new FieldOpcodeAddressResolver(ReadInt32, ReadByte);
        fieldOpcodeParameterReader = new FieldOpcodeParameterReader(legacyAddressSpace);
        fieldScriptContextReader = new FieldScriptContextReader(legacyAddressSpace);
        loadedFieldScriptIdentityReader = new LoadedFieldScriptIdentityReader(legacyAddressSpace);
        loadedFieldScriptIdentity = null;
        fieldCutsceneDescriptionTracker = new EchoSFieldCutsceneDescriptionTracker();
        fieldPositionReader = new FieldPositionReader(legacyAddressSpace);
        fieldLadderStateReader = new FieldLadderStateReader(ReadInt32, ReadUInt16, ReadByte);
        fieldNavigationControlReader = new FieldNavigationControlReader(legacyAddressSpace);
        fieldNavigationInputReader = new FieldNavigationInputReader(ReadUInt32);
        fieldAudibleCueStateReader = new FieldAudibleCueOwnershipStateReader(
            legacyAddressSpace,
            () => fieldMessageReader.HasReadableActiveWindow());
        fieldRunStateReader = new FieldRunStateReader(ReadInt32, ReadByte);
        Func<int, string?>? resolveInventoryObjectName = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveInventoryObjectName;
        Func<int, string?>? resolveItemDescription = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveInventoryObjectDescription;
        Func<int, string?>? resolveAbilityName = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveSpellName;
        Func<int, string?>? resolveAbilityDescription = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveSpellDescription;
        Func<int, string?>? resolveLimitName = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveBattleActionName;
        Func<int, string?>? resolveLimitDescription = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveBattleActionDescription;
        inventoryItemReader = CreateMenuInventoryItemReader(
            legacyAddressSpace,
            kernel2TextDatabase);
        magicMenuSelectionReader = kernel2TextDatabase is null
            ? null
            : new MagicMenuSelectionReader(
                legacyAddressSpace,
                kernel2TextDatabase.ResolveSpellName,
                kernel2TextDatabase.ResolveSpellDescription);
        configMenuValueReader = new ConfigMenuValueReader(legacyAddressSpace);
        Func<int, string?>? resolveWeaponName = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveWeaponName;
        Func<int, string?>? resolveArmorName = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveArmorName;
        Func<int, string?>? resolveAccessoryName = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveAccessoryName;
        Func<int, string?>? resolveCommandName = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveCommandName;
        Func<int, string?>? resolveMateriaDescription = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveMateriaDescription;
        Func<int, string?>? resolveMateriaMenuName = kernel2TextDatabase is null
            ? null
            : kernel2TextDatabase.ResolveMateriaName;
        savemapPartyReader = new SavemapPartyReader(
            legacyAddressSpace,
            resolveWeaponName,
            resolveArmorName,
            resolveAccessoryName,
            resolveInventoryObjectDescription: resolveItemDescription);
        orderMenuSelectionReader = new OrderMenuSelectionReader(
            legacyAddressSpace,
            savemapPartyReader);
        equipmentMenuSelectionReader = new EquipmentMenuSelectionReader(
            legacyAddressSpace,
            resolveWeaponName,
            resolveArmorName,
            resolveAccessoryName,
            resolveItemDescription);
        materiaMenuSelectionReader = new MateriaMenuSelectionReader(
            legacyAddressSpace,
            resolveMateriaMenuName,
            resolveMateriaDescription);
        shopMenuStateReader = new ShopMenuStateReader(
            legacyAddressSpace,
            resolveInventoryObjectName,
            resolveItemDescription,
            resolveMateriaMenuName,
            resolveMateriaDescription);
        battleHookAddressResolver = new BattleHookAddressResolver(ReadByte, ReadInt32);
        battleStateReader = new BattleStateReader(
            legacyAddressSpace,
            savemapPartyReader,
            resolveAbilityName,
            resolveAbilityDescription,
            resolveInventoryObjectName,
            resolveItemDescription,
            resolveCommandName,
            resolveLimitName,
            resolveLimitDescription);
        battleResultsReader = new BattleResultsReader(
            legacyAddressSpace,
            resolveInventoryObjectName ?? (_ => null));
        battleDamagePopupReader = new BattleDamagePopupReader(legacyAddressSpace);
        tifaSlotResultReader = new TifaSlotResultReader(legacyAddressSpace);
        battleMenuFrameSpeechCoordinator = new BattleMenuFrameSpeechCoordinator();
        Func<int, string?> resolveBattleText = kernel2TextDatabase is null
            ? _ => null
            : kernel2TextDatabase.ResolveBattleText;
        var battleRuntimeTextReader = new BattleRuntimeTextReader(
            legacyAddressSpace,
            resolveBattleText,
            resolveInventoryObjectName ?? (_ => null),
            actorIndex => battleStateReader.TryReadBattleActor(actorIndex, out var actor)
                ? actor.Name
                : null,
            resolveLimitName);
        battleMessageSpeechTracker = new BattleMessageSpeechTracker(
            battleRuntimeTextReader.Resolve);
        activeMenuWidgetReader = new ActiveMenuWidgetReader(legacyAddressSpace);
        activeMenuWidgetFrameBridge = new ActiveMenuWidgetFrameBridge(
            activeMenuWidgetReader,
            activeMenuFrameSpeechCoordinator,
            EnrichActiveMenuWidgetSnapshot);
        Func<int, string?> resolveMateriaName = kernel2TextDatabase is null
            ? _ => null
            : kernel2TextDatabase.ResolveMateriaName;
        Func<int, string?> resolveNavigationObjectName = resolveInventoryObjectName ?? (_ => null);
        fieldScriptLineStateReader = new FieldScriptLineStateReader(legacyAddressSpace);
        var fieldNavigationObjects = FieldNavigationObjectCatalog.CreateAllFields();
        fieldNavigationObjectReader = new FieldNavigationObjectReader(
            ReadInt32,
            ReadByte,
            resolveNavigationObjectName,
            resolveMateriaName,
            fieldNavigationObjects,
            fieldScriptLineStateReader.IsEnabled,
            ResolveFieldNavigationObjectCollectedMask);
        fieldGatewayTargetReader = new FieldGatewayTargetReader(legacyAddressSpace);
        fieldScriptNavigationCatalog = gameRootDirectory is null
            ? null
            : new FieldScriptNavigationCatalog(gameRootDirectory, gameLanguage);
        var fieldMapNameCatalog = fieldScriptNavigationCatalog is null || flevelFieldTextResolver is null
            ? null
            : new FieldMapNameCatalog(fieldScriptNavigationCatalog, flevelFieldTextResolver);
        var fieldMapNameReader = new FieldMapNameReader(ReadFf7EncodedText);
        var fieldExitLabelResolver = new FieldExitLabelResolver(
            fieldId => fieldMapNameCatalog?.Read(fieldId) ?? FieldMapNameResolution.Unknown,
            fieldMapNameReader.Read);
        var fieldExitPresentationPolicy = new FieldExitPresentationPolicy(() =>
        {
            var address = (uint)(FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 131);
            return legacyAddressSpace.TryReadByte(address, out var before) &&
                   legacyAddressSpace.TryReadByte(address, out var after) &&
                   before == after
                ? (before & 0x01) == 0x01
                : null;
        });
        fieldNavigationNpcReader = new FieldNavigationNpcReader(
            ReadInt32,
            ReadInt16,
            ReadByte,
            (fieldId, dialogId) =>
                flevelFieldTextResolver?.ReadMessageLinesById(fieldId, dialogId) ??
                Array.Empty<string>(),
            fieldId =>
                fieldScriptNavigationCatalog?.ReadField(fieldId).Npcs ??
                Array.Empty<FieldScriptNpcDefinition>(),
            fieldNavigationObjects.Select(definition => (definition.FieldId, definition.EntityId)),
            fieldScriptLineStateReader.IsEnabled);
        var fieldScriptNavigationTransitionTracker = new FieldScriptNavigationTransitionTracker();
        var fieldStoryEvents = FieldStoryEventCatalog.CreateAllFields();
        fieldStoryTargetReader = new FieldStoryTargetReader(
            ReadInt32,
            ReadInt16,
            ReadByte,
            fieldStoryEvents);
        var fieldWalkmeshReader = new FieldWalkmeshReader(ReadInt32, ReadInt16);
        var fieldBoundaryStateReader = new FieldBoundaryStateReader(legacyAddressSpace);
        var fieldDynamicObstacleReader = new FieldNavigationDynamicObstacleReader(
            ReadInt32,
            ReadInt16,
            ReadByte);
        IReadOnlyList<FieldScriptNavigationTransition> ReadNavigationTransitions(int fieldId)
        {
            var result = fieldScriptNavigationCatalog?.ReadField(fieldId);
            return result is null
                ? []
                : fieldScriptNavigationTransitionTracker.Resolve(
                    fieldId,
                    result.Transitions,
                    transition => fieldScriptLineStateReader.IsEnabled(transition.SourceEntityId));
        }

        fieldNavigationTransitionProvider = ReadNavigationTransitions;

        fieldNavigationRoutePlanner = new FieldWalkmeshRoutePlanner(
            fieldWalkmeshReader,
            fieldBoundaryStateReader,
            transitionProvider: ReadNavigationTransitions,
            dynamicObstacleProvider: fieldDynamicObstacleReader.Read);
        var fieldExitReachabilityPlanner = new FieldWalkmeshRoutePlanner(
            fieldWalkmeshReader,
            fieldBoundaryStateReader,
            transitionProvider: ReadNavigationTransitions);
        nativeFieldExitTargetProvider = new NativeFieldExitTargetProvider(
            fieldGatewayTargetReader,
            scriptExitProvider: position =>
            {
                var result = fieldScriptNavigationCatalog?.ReadField(position.FieldId);
                if (result is null)
                {
                    return [];
                }

                var gameMoment =
                    ReadByte(FieldNavigationObjectReader.AddressFieldBankBase) |
                    (ReadByte(FieldNavigationObjectReader.AddressFieldBankBase + 1) << 8);
                var enabledExits = result.Exits
                    .Where(exit => fieldScriptLineStateReader.IsEnabled(exit.TriggerEntityId))
                    .ToArray();
                return FieldScriptExitBranchPolicy.Resolve(
                    position.FieldId,
                    gameMoment,
                    enabledExits);
            },
            labelResolver: fieldExitLabelResolver,
            presentationPolicy: fieldExitPresentationPolicy);
        reachableFieldExitTargetProvider = new ReachableFieldExitTargetProvider(
            position => nativeFieldExitTargetProvider.ReadTargets(position),
            fieldExitReachabilityPlanner);
        fieldExitProximityCueTracker = new FieldExitProximityCueTracker(
            config.FieldExitCueInnerRangeUnits,
            config.FieldExitCueOuterRangeUnits,
            TimeSpan.FromMilliseconds(Math.Max(100, config.FieldExitCueIntervalMs)));
        fieldExitCuePlayer?.Dispose();
        fieldExitCuePlayer = config.EnableFieldExitProximityCues
            ? new NavigationBeaconPlayer(
                ResolveFieldExitCueSoundPath(),
                config.FieldExitCueVolumePercent,
                Log)
            : null;
        fieldLadderProximityCueTracker = new FieldLadderProximityCueTracker(
            config.FieldLadderCueInnerRangeUnits,
            config.FieldLadderCueOuterRangeUnits,
            TimeSpan.FromMilliseconds(Math.Max(100, config.FieldLadderCueIntervalMs)));
        fieldLadderCuePlayer?.Dispose();
        fieldLadderCuePlayer = config.EnableFieldLadderProximityCues
            ? new NavigationBeaconPlayer(
                ResolveFieldLadderCueSoundPath(),
                config.FieldLadderCueVolumePercent,
                Log)
            : null;
        fieldLadderMountCueTracker = new FieldLadderMountCueTracker(
            FieldLadderMountCueTracker.DefaultEntranceRange,
            TimeSpan.FromMilliseconds(Math.Max(100, config.FieldLadderMountCueIntervalMs)));
        fieldLadderMountCuePlayer?.Dispose();
        fieldLadderMountCuePlayer = config.EnableFieldLadderProximityCues
            ? new NavigationBeaconPlayer(
                ResolveFieldLadderMountCueSoundPath(),
                config.FieldLadderMountCueVolumePercent,
                Log)
            : null;
        fieldLadderMountCueActive = false;
        fieldNavigationController.Reset();
        fieldNavigationGuidanceRepeatGate.Reset();
        fieldNavigationProgressSink?.Dispose();
        fieldNavigationProgressBar?.Dispose();
        fieldNavigationProgressBar = new NativeFieldNavigationProgressBar(Log);
        fieldNavigationProgressSink = new IntervalFieldNavigationProgressSink(
            fieldNavigationProgressBar,
            navigationProgressController);
        fieldNavigationController = new FieldNavigationController(
            FieldNavigationTargetSource.CreateOpeningReactorRoute(
                objectTargetProvider: position => fieldNavigationObjectReader.ReadTargets(position),
                storyTargetProvider: ReadFieldStoryTargets,
                exitTargetProvider: position => reachableFieldExitTargetProvider.ReadTargets(position),
                npcTargetProvider: position => fieldNavigationNpcReader.ReadTargets(position)),
            fieldNavigationRoutePlanner,
            fieldId => FieldNavigationDistanceCalibration.ResolveForNavigation(
                fieldId,
                config.FieldNavigationSpeechDistanceUnitsPerCount,
                fieldNavigationCadence,
                fieldFootstepDistanceProbe.GetFieldSummary(fieldId)),
            fieldNavigationProgressSink);
        Log(
            $"Field exits initialized from all {FieldGatewayTargetReader.GatewayCount} native trigger gateway records " +
            "plus live-enabled, line-triggered MAPJUMP scripts " +
            $"at 0x{FieldNavigationControlReader.AddressFieldTriggersPtr:X8}; IDLCK boundary diagnostics come from " +
            $"*(0x{FieldBoundaryStateReader.AddressFieldGlobalObjectPtr:X8})+0x{FieldBoundaryStateReader.BoundaryBitsOffset:X}, " +
            "and live boundary triangles block inaccessible routes and exits; " +
            $"labels use native MPNAM destination strings and live map-name buffer 0x{FieldMapNameReader.AddressCurrentMapName:X8}; " +
            "static Exit fallbacks are disabled.");
        Log(
            $"Native LADER and JUMP routes use live LINON state with a " +
            $"{FieldScriptNavigationTransitionTracker.DefaultGracePeriod.TotalSeconds:0}-second grace period only for transitions observed enabled in the current field.");
        Log(
            "Field routes use the native model collision set at event offset 0x5F and " +
            "the original half-sum collision-width clearance at offset 0x72; blocked " +
            "targets fail closed or receive a walkmesh-verified local detour.");
        Log(
            $"Field exit proximity cues initialized: enabled={config.EnableFieldExitProximityCues}, " +
            $"inner={config.FieldExitCueInnerRangeUnits}, outer={config.FieldExitCueOuterRangeUnits}, " +
            $"interval={config.FieldExitCueIntervalMs}ms, source=reachable native gateways.");
        Log(
            $"Field ladder proximity cues initialized: enabled={config.EnableFieldLadderProximityCues}, " +
            $"inner={config.FieldLadderCueInnerRangeUnits}, outer={config.FieldLadderCueOuterRangeUnits}, " +
            $"interval={config.FieldLadderCueIntervalMs}ms, source=all live-enabled native LADER entrances.");
        Log(
            $"Field ladder mount cues initialized: enabled={config.EnableFieldLadderProximityCues}, " +
            $"range={FieldLadderMountCueTracker.DefaultEntranceRange}, " +
            $"interval={config.FieldLadderMountCueIntervalMs}ms, source=active route LADER entrance only.");
        Log(
            "Field navigation objects initialized from native entity/model visibility and field-bank collection state " +
            $"({fieldNavigationObjects.Count} full-game definitions, source {FieldNavigationObjectCatalog.SourceCommit}).");
        Log(
            "Field story navigation initialized from native field interactions, live model positions, " +
            $"line triggers, and story state ({fieldStoryEvents.Count} full-game definitions, " +
            $"source {FieldStoryEventCatalog.SourceCommit}); static Story fallbacks are disabled.");
        Log(
            "Field NPC navigation initialized from installed Talk script entry 1 and live entity-to-model, " +
            "visibility, TLKON, player-model state, and native LINE interaction proxies; " +
            "static NPC fallbacks are disabled.");
        Log(
            $"Field spoken navigation initialized from native PC walkmesh at 0x{FieldWalkmeshReader.AddressFieldDataPtr:X8} " +
            $"with native LADER and JUMP off-mesh links gated by LINON state at 0x{FieldScriptLineStateReader.AddressFieldLineStates:X8}; " +
            "routes use triangle adjacency with no direct-line fallback and speak every " +
            $"{Math.Max(FieldNavigationSpeechPolicy.MinimumIntervalMs, config.FieldNavigationSpeechIntervalMs)}ms while walking or " +
            $"{Math.Max(FieldNavigationSpeechPolicy.MinimumRunningIntervalMs, config.FieldNavigationRunningSpeechIntervalMs)}ms while running.");
        Log(
            "Navigation progress controls: F5 toggle; F6 previous interval; F7 next interval; " +
            $"enabled={navigationProgressController.Enabled}; " +
            $"interval={navigationProgressController.IntervalPercent} percent.");
        Log("Navigation auto walk initialized: P starts or stops walking to the selected field or world-map target.");
        fieldObjectProximityCueTracker = new FieldObjectProximityCueTracker(
            config.FieldObjectCueInnerRangeUnits,
            config.FieldObjectCueOuterRangeUnits,
            config.FieldObjectCueClusterRadiusUnits,
            TimeSpan.FromMilliseconds(Math.Max(100, config.FieldObjectCueIntervalMs)));
        foreach (var player in fieldObjectCuePlayers.Values)
        {
            player.Dispose();
        }

        fieldObjectCuePlayers.Clear();
        if (config.EnableFieldObjectProximityCues)
        {
            fieldObjectCuePlayers[FieldObjectCueKind.Materia] = new NavigationBeaconPlayer(
                ResolveObjectCueSoundPath("object_materia_190_pitch70.wav"),
                config.FieldObjectCueVolumePercent,
                Log);
            fieldObjectCuePlayers[FieldObjectCueKind.Chest] = new NavigationBeaconPlayer(
                ResolveObjectCueSoundPath("object_chest_253_pitch70.wav"),
                config.FieldObjectCueVolumePercent,
                Log);
            fieldObjectCuePlayers[FieldObjectCueKind.Item] = new NavigationBeaconPlayer(
                ResolveObjectCueSoundPath("object_item_357_pitch70.wav"),
                config.FieldObjectCueVolumePercent,
                Log);
        }

        Log(
            $"Field object proximity cues initialized: enabled={config.EnableFieldObjectProximityCues}, " +
            $"inner={config.FieldObjectCueInnerRangeUnits}, outer={config.FieldObjectCueOuterRangeUnits}, " +
            $"cluster={config.FieldObjectCueClusterRadiusUnits}, interval={config.FieldObjectCueIntervalMs}ms.");
        cosmoFootstepSequencer = InitializeCosmoFootsteps();
        footstepSoundPlayer = new FootstepSoundPlayer(ResolveFootstepSoundPath(), config.FieldFootstepVolumePercent, Log);
        InitializeWorldMapAccessibility(legacyAddressSpace);
        footstepProbeScheduler = new FootstepProbeScheduler(
            config.PlayFootstepProbeOnLoad,
            TimeSpan.FromMilliseconds(Math.Max(0, config.FieldFootstepProbeDelayMs)),
            DateTime.UtcNow,
            reason => PlayFootstep(reason, null));
        Log($"Prism initialization elapsed: {prismStart.ElapsedMilliseconds} ms");
        if (config.SpeakOnLoad)
        {
            Speak("Final Fantasy Seven accessibility mod loaded through Reloaded.");
        }

        nativeTextDrawEventQueue.WarmUp();
        Log("Prewarmed deferred field text draw capture for 7th Heaven compatibility.");
        nativeFieldHookEventQueue.WarmUp();
        Log("Prewarmed deferred native field hook capture for 7th Heaven compatibility.");
        ffnxVoicePlaybackEventQueue.WarmUp();
        Log("Prewarmed FFNx voice playback capture for Echo-S compatibility.");
        TryGetReloadedHooks();
        if (hooks is not null)
        {
            TryInstallExperimentalHook("module 19 native writer diagnostics", InstallModule19WriterDiagnostics);
        }

        if (config.EnableExperimentalHooks)
        {
            InstallExperimentalHooks();
        }
        else
        {
            Log("Experimental hooks are disabled in config. Running diagnostics-only mode.");
        }

        cancellation = new CancellationTokenSource();
        monitorThread = new Thread(() => MonitorLoop(cancellation.Token))
        {
            IsBackground = true,
            Name = "FFVII Accessibility Monitor"
        };
        monitorThread.Start();
    }

    private void MonitorLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!menuTableDumped)
                {
                    menuTableDumped = true;
                    DumpMenuStringTablePreview();
                }

                TickExitShortcutDiagnostics();
                TickRepeatLastSpeech();
                TickNavigationProgressControls();
                TickNavigationAutoWalkToggleInput();
                TickDeferredFieldTextDraws();
                TickDeferredNativeFieldHooks();
                TickEchoSDisclaimerSpeech();
                TickEchoSReactorTimerOverride();
                TickFfnxVoicePlaybackEvents();
                TickFootstepProbe();
                TickBattleSessionState();
                TickBattleStatusHotkeys();
                TickHighwayAccessibility();
                TickFieldZoneTransitionCue();
                TickFieldSwingingBarTimingCue();
                TickSquatMinigameCue();
                TickFloor60SoldierTurnCue();
                TickTitleMenuReader();
                TickOpeningMovieDescription();
                TickFfnxPopupSpeech();
                TickFieldCutsceneDescriptions();
                TickFieldCountdownSpeech();
                TickFieldMessageReader();
                TickFieldMessageOpenSpeech();
                FieldAudibleCueTickSequence.Run(
                    TickFieldAudibleCueState,
                    TickFieldFootstepFeedback,
                    TickFieldNavigationAssistant,
                    TickFieldObjectProximityCues,
                    TickFieldLadderProximityCues,
                    TickFieldExitProximityCues);
                TickWorldMapAccessibility();
                TickSaveMenuSpeech();
                TickMainMenuReader();
                TickMenuWidgetDiagnostics();
                TickTitleMenuCursorSpeech();
                TickTitleLoadMenuSpeech();
                TickNameEntryMenuSpeech();
                TickFieldDialogueDrawSpeech();
                TickShopMenuSpeech();
                TickInGameMenuSpeech();
                TickRenderedMenuTextSpeech();
            }
            catch (Exception ex)
            {
                try
                {
                    highwayAccessibilityCoordinator?.Reset("x86 monitor loop fault");
                    navigationAutoWalkController?.Suspend();
                }
                catch (Exception resetException)
                {
                    Log($"Highway accessibility cleanup also failed after a monitor fault: {resetException}");
                }

                Log($"Monitor error: {ex}");
            }

            Thread.Sleep(GetMonitorSleepMs());
        }
    }

    private int GetMonitorSleepMs()
    {
        var titleMenuInterval = Math.Max(50, config.TitleMenuScanIntervalMs);
        var menuInterval = Math.Max(50, config.MainMenuScanIntervalMs);
        var sleep = titleMenuInterval;
        if (config.EnableFieldMessageReader)
        {
            sleep = Math.Min(sleep, Math.Max(50, config.FieldMessageScanIntervalMs));
        }

        if (config.EnableMainMenuReader || config.EnableMenuWidgetDiagnostics || config.EnableInGameMenuWidgetSpeech)
        {
            sleep = Math.Min(sleep, menuInterval);
        }

        if (config.EnableFieldDialogueDrawSpeech)
        {
            sleep = Math.Min(sleep, 50);
        }

        if (config.EnableFieldCutsceneDescriptions)
        {
            sleep = Math.Min(sleep, 50);
        }

        if (config.EnableNameEntryMenuSpeech)
        {
            var nameEntryActive = IsNameEntryMenuActive();
            sleep = Math.Min(sleep, nameEntryActive ? NameEntryNativeNameTracker.RecommendedScanIntervalMs : 50);
        }

        if (config.EnableFieldFootstepFeedback || config.EnableFieldPositionDiagnostics)
        {
            sleep = Math.Min(sleep, Math.Max(30, config.FieldFootstepScanIntervalMs));
        }

        if (config.EnableFieldZoneTransitionCue)
        {
            sleep = Math.Min(sleep, 50);
        }

        if (config.EnableFieldSwingingBarTimingCue)
        {
            sleep = Math.Min(sleep, 30);
        }

        if (config.EnableSquatMinigamePrompts)
        {
            sleep = Math.Min(sleep, 30);
        }

        if (config.EnableFloor60SoldierTurnCue)
        {
            sleep = Math.Min(sleep, 30);
        }

        if (config.EnableHighwayAccessibility)
        {
            sleep = Math.Min(sleep, 30);
        }

        if (config.EnableFieldNavigationAssistant)
        {
            sleep = Math.Min(sleep, Math.Max(30, config.FieldNavigationScanIntervalMs));
        }

        if (config.EnableWorldMapFootstepFeedback || config.EnableWorldMapNavigationAssistant)
        {
            sleep = Math.Min(sleep, Math.Max(30, config.WorldMapScanIntervalMs));
        }

        if (config.EnableFieldObjectProximityCues)
        {
            sleep = Math.Min(sleep, 50);
        }

        if (config.EnableFieldExitProximityCues)
        {
            sleep = Math.Min(sleep, 50);
        }

        if (config.EnableFieldLadderProximityCues)
        {
            sleep = Math.Min(sleep, 50);
        }

        if (config.EnableFfnxPopupSpeech)
        {
            sleep = Math.Min(sleep, 50);
        }

        return sleep;
    }

    private void TickFfnxPopupSpeech()
    {
        if (!config.EnableFfnxPopupSpeech)
        {
            ffnxPopupSpeechTracker.Reset();
            return;
        }

        if (ffnxPopupStateReader is null)
        {
            TryInitializeFfnxPopupReader(force: false);
            return;
        }

        string? speech;
        if (ffnxPopupStateReader.TryRead(out var visible))
        {
            speech = ffnxPopupSpeechTracker.Observe(visible);
        }
        else if (ffnxPopupStateReader.LastReadWasDefinitelyHidden)
        {
            speech = ffnxPopupSpeechTracker.Observe(null);
        }
        else
        {
            // A frame changed while it was being copied. Preserve the active
            // generation and retry instead of announcing the same popup twice.
            return;
        }

        if (!string.IsNullOrWhiteSpace(speech))
        {
            Speak(speech, interrupt: true);
        }
    }

    private void TryInitializeFfnxPopupReader(bool force)
    {
        if (!config.EnableFfnxPopupSpeech
            || ffnxPopupStateReader is not null
            || currentProcessLegacyAddressSpace is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!force
            && now - lastFfnxPopupReaderProbeAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        lastFfnxPopupReaderProbeAt = now;
        if (FfnxPopupStateReader.TryCreate(
                Process.GetCurrentProcess(),
                currentProcessLegacyAddressSpace,
                out var reader,
                out var diagnostic))
        {
            ffnxPopupStateReader = reader;
            ffnxRuntimeLoaded = true;
            ffnxPopupSpeechTracker.Reset();
        }

        if (!string.Equals(
                lastFfnxPopupReaderDiagnostic,
                diagnostic,
                StringComparison.Ordinal))
        {
            Log(diagnostic);
            lastFfnxPopupReaderDiagnostic = diagnostic;
        }
    }

    private void TickFootstepProbe()
    {
        footstepProbeScheduler?.TryPlay(DateTime.UtcNow);
    }

    private void TickExitShortcutDiagnostics()
    {
        var now = DateTime.UtcNow;
        var controlState = GetAsyncKeyState(VirtualKeyControl);
        var qState = GetAsyncKeyState(VirtualKeyQ);
        exitShortcutDiagnosticsTracker.ObserveInput(
            now,
            IsKeyActive(controlState),
            IsKeyActive(qState),
            foregroundProcessGate.IsCurrentProcessForeground());

        var diagnostic = exitShortcutDiagnosticsTracker.ObserveModule(
            ReadByte(FieldPositionReader.AddressCurrentModule),
            now);
        if (diagnostic is not { } exit)
        {
            return;
        }

        var writer = module19WriterProbe?.ReadCurrentSite();
        var writerDescription = writer is { } site
            ? $"0x{site.Address:X8} ({site.Cause})"
            : "not captured";
        Log(
            $"Module 19 transition observed: module={exit.PreviousModule}->{exit.CurrentModule}, " +
            $"writer={writerDescription}, field={ReadUInt16(FieldPositionReader.AddressFieldId)}, " +
            $"fieldRequest=0x{ReadByte(Module19WriterCatalog.AddressFieldModuleRequest):X2}, " +
            $"fieldState=0x{ReadByte(Module19WriterCatalog.AddressFieldModuleState):X2}, " +
            $"control={exit.ControlActive}, q={exit.QActive}, " +
            $"controlQRecent={exit.ControlQRecent}, foreground={exit.WasForeground}.");
    }

    private void TickRepeatLastSpeech()
    {
        try
        {
            repeatLastSpeechController.Poll(
                virtualKey => repeatLastSpeechKeyTracker.Observe(
                    virtualKey,
                    (GetAsyncKeyState(virtualKey) & 0x8000) != 0,
                    foregroundProcessGate.IsCurrentProcessForeground()),
                text =>
                {
                    Log($"Repeat last speech: {text}");
                    return config.EnableSpeech && speaker?.Speak(text, interrupt: true) == true;
                });
        }
        catch (Exception ex)
        {
            Log($"Repeat last speech hotkey error: {ex.Message}");
        }
    }

    private static bool IsKeyActive(short state) =>
        (state & unchecked((short)0x8000)) != 0 || (state & 1) != 0;

    private void TickDeferredFieldTextDraws()
    {
        while (nativeTextDrawEventQueue.TryDequeue(out var drawEvent))
        {
            try
            {
                var decodedText = Ff7EncodedTextDecoder.DecodeTerminated(drawEvent.TextBytes);
                ProcessInGameMenuTextDraw(
                    drawEvent.Source,
                    drawEvent.X,
                    drawEvent.Y,
                    decodedText,
                    drawEvent.Color,
                    drawEvent.Context,
                    drawEvent.CurrentModule,
                    DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                inGameMenuTextDrawErrorCount++;
                if (inGameMenuTextDrawErrorCount <= 10)
                {
                    Log($"Deferred field text draw error: {ex.Message}");
                }
            }
        }

        var droppedCount = nativeTextDrawEventQueue.DroppedCount;
        if (droppedCount == lastNativeTextDrawDroppedCount)
        {
            return;
        }

        Log(
            $"Deferred field text draw overflow: dropped={droppedCount - lastNativeTextDrawDroppedCount}, " +
            $"total={droppedCount}. No replacement text was inferred.");
        lastNativeTextDrawDroppedCount = droppedCount;
    }

    private void TickDeferredNativeFieldHooks()
    {
        while (nativeFieldHookEventQueue.TryDequeue(out var hookEvent))
        {
            try
            {
                switch (hookEvent.Kind)
                {
                    case NativeFieldHookEventKind.MessageOpen:
                        HandleFieldMessageOpen(
                            (short)hookEvent.WindowId,
                            (short)hookEvent.DialogId,
                            hookEvent.Result);
                        break;
                    case NativeFieldHookEventKind.MessagePreview:
                        HandleFieldMessagePreview((short)hookEvent.DialogId, hookEvent.Result);
                        break;
                    case NativeFieldHookEventKind.OpcodeMessage:
                        HandleFieldOpcodeMessageObservation(hookEvent.MessageObservation, hookEvent.Result);
                        if (hookEvent.MessageObservation.Kind == FieldOpcodeKind.Ask && hookEvent.Result == 0)
                        {
                            CompleteDeferredFieldAskClose(hookEvent.MessageObservation);
                        }
                        break;
                    case NativeFieldHookEventKind.AskCursor:
                        HandleDeferredFieldAskCursor(hookEvent);
                        break;
                    case NativeFieldHookEventKind.CutsceneContext:
                        HandleFieldCutsceneDescriptionContext(hookEvent.ScriptContext);
                        break;
                    case NativeFieldHookEventKind.TimerSet:
                        HandleEchoSReactorTimerSet(hookEvent.ScriptContext, hookEvent.Result);
                        break;
                }
            }
            catch (Exception ex)
            {
                fieldOpcodeMessageErrorCount++;
                if (fieldOpcodeMessageErrorCount <= 10)
                {
                    Log($"Deferred native field hook error: {ex}");
                }
            }
        }

        var droppedCount = nativeFieldHookEventQueue.DroppedCount;
        if (droppedCount != lastNativeFieldHookDroppedCount)
        {
            Log(
                $"Deferred native field hook overflow: dropped={droppedCount - lastNativeFieldHookDroppedCount}, " +
                $"total={droppedCount}. No replacement state was inferred.");
            lastNativeFieldHookDroppedCount = droppedCount;
            RecoverFromNativeFieldHookObservationLoss();
        }

        var captureErrors = Volatile.Read(ref nativeFieldHookCaptureErrorCount);
        if (captureErrors != lastNativeFieldHookCaptureErrorCount)
        {
            Log(
                $"Native field hook capture errors: new={captureErrors - lastNativeFieldHookCaptureErrorCount}, " +
                $"total={captureErrors}. No replacement state was inferred.");
            lastNativeFieldHookCaptureErrorCount = captureErrors;
            RecoverFromNativeFieldHookObservationLoss();
        }

        var unavailableContexts = Volatile.Read(ref fieldCutsceneContextUnavailableCount);
        if (config.EnableFieldCutsceneDescriptionDiagnostics &&
            lastFieldCutsceneContextUnavailableCount < 10 &&
            unavailableContexts != lastFieldCutsceneContextUnavailableCount)
        {
            Log(
                $"Native field cutscene contexts unavailable: " +
                $"new={unavailableContexts - lastFieldCutsceneContextUnavailableCount}, total={unavailableContexts}.");
        }

        lastFieldCutsceneContextUnavailableCount = unavailableContexts;
    }

    private void TickFfnxVoicePlaybackEvents()
    {
        RefreshFfnxVoicePlaybackHook();
        while (ffnxVoicePlaybackEventQueue.TryDequeue(out var observation))
        {
            ffnxVoicePlaybackTracker.ObserveVoice(observation);
            if (echoSCompatibilityActive)
            {
                Log(
                    $"FFNx voice playback: field={observation.FieldName}, window={observation.WindowId}, " +
                    $"dialog={observation.DialogId}, page={observation.Page}, played={observation.Played}.");
            }
        }

        var dropped = ffnxVoicePlaybackEventQueue.DroppedCount;
        if (dropped != lastFfnxVoicePlaybackDroppedCount)
        {
            Log(
                $"FFNx voice playback capture overflow: " +
                $"dropped={dropped - lastFfnxVoicePlaybackDroppedCount}, total={dropped}. " +
                "Prism dialogue fallback restored.");
            lastFfnxVoicePlaybackDroppedCount = dropped;
            ffnxVoicePlaybackTracker.Reset();
        }
    }

    private unsafe void RefreshFfnxVoicePlaybackHook()
    {
        if (ffnxPlayVoiceHook is not null || hooks is null || currentProcessLegacyAddressSpace is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastFfnxVoiceHookProbeAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        lastFfnxVoiceHookProbeAt = now;
        if (!FfnxVoicePlaybackHookTarget.TryResolve(
                Process.GetCurrentProcess(),
                currentProcessLegacyAddressSpace,
                out var targetAddress,
                out var diagnostic))
        {
            if (!string.Equals(lastFfnxVoiceHookDiagnostic, diagnostic, StringComparison.Ordinal))
            {
                lastFfnxVoiceHookDiagnostic = diagnostic;
                Log(diagnostic);
            }

            return;
        }

        try
        {
            ffnxPlayVoiceDetour = FfnxPlayVoiceDetour;
            ffnxPlayVoiceHook = hooks.CreateHook<FfnxPlayVoiceDelegate>(
                ffnxPlayVoiceDetour,
                targetAddress,
                -1);
            ffnxPlayVoiceHook.Activate();
            lastFfnxVoiceHookDiagnostic = diagnostic;
            Log($"Installed {diagnostic}");
        }
        catch (Exception ex)
        {
            var failure = $"Could not install FFNx play_voice hook: {ex.Message}";
            if (!string.Equals(lastFfnxVoiceHookDiagnostic, failure, StringComparison.Ordinal))
            {
                lastFfnxVoiceHookDiagnostic = failure;
                Log(failure);
            }
        }
    }

    private void RecoverFromNativeFieldHookObservationLoss()
    {
        ffnxVoicePlaybackTracker.Reset();
        ResetFieldAskState();
        nativeFieldMessageOwnershipTracker.Reset();
        fieldOpcodeMessageSpeechGate.Reset();
        fieldVisibleWindowSpeechCoordinator.CancelAllNativeSpeech();
    }

    private void HandleDeferredFieldAskCursor(NativeFieldHookEvent hookEvent)
    {
        var activeIdentity = Volatile.Read(ref activeFieldAskIdentity);
        if (activeIdentity is null ||
            activeIdentity.Kind != FieldOpcodeKind.Ask ||
            activeIdentity.FieldId != hookEvent.FieldId ||
            activeIdentity.WindowId != hookEvent.WindowId ||
            activeIdentity.DialogId != hookEvent.DialogId ||
            activeIdentity.LifecycleToken != hookEvent.LifecycleToken)
        {
            return;
        }

        var pages = flevelFieldTextResolver?.ReadMessagePagesById(
            hookEvent.FieldId,
            hookEvent.DialogId) ?? Array.Empty<Ff7DecodedTextPage>();
        if (!FieldAskTextFormatter.TryResolveChoicePage(
                pages,
                hookEvent.FirstQuestionLine,
                hookEvent.LastQuestionLine,
                out var lines))
        {
            return;
        }

        fieldAskChoiceSpeechTracker.Observe(new FieldAskChoiceObservation(
            true,
            hookEvent.FieldId,
            hookEvent.WindowId,
            hookEvent.DialogId,
            hookEvent.FirstQuestionLine,
            hookEvent.LastQuestionLine,
            hookEvent.CurrentQuestionLine,
            lines,
            hookEvent.LifecycleToken));
    }

    private void TickFieldZoneTransitionCue()
    {
        if (!config.EnableFieldZoneTransitionCue)
        {
            return;
        }

        var shouldPlay = fieldZoneTransitionCueTracker.Observe(
            ReadByte(FieldPositionReader.AddressCurrentModule),
            ReadUInt16(FieldPositionReader.AddressFieldId),
            DateTime.UtcNow);
        if (!shouldPlay)
        {
            return;
        }

        fieldZoneTransitionCuePlayer?.Play(
            fieldZoneTransitionCueTracker.PreviousFieldId,
            fieldZoneTransitionCueTracker.CurrentFieldId);
    }

    private void TickFieldSwingingBarTimingCue()
    {
        if (!config.EnableFieldSwingingBarTimingCue ||
            !foregroundProcessGate.IsCurrentProcessForeground())
        {
            swingingBarTimingCueTracker.Reset();
            return;
        }

        if (fieldPositionReader is null)
        {
            swingingBarTimingCueTracker.Reset();
            return;
        }

        var positionResult = fieldPositionReader.Read();
        if (!positionResult.IsUsable)
        {
            swingingBarTimingCueTracker.Reset();
            return;
        }

        var position = positionResult.Position;
        var module = (byte)position.CurrentModule;
        var fieldId = (ushort)position.FieldId;
        var frameCounter =
            module == FieldPositionReader.FieldModule &&
            fieldId == SwingingBarTimingCueTracker.SwingingBarFieldId
                ? ReadByte(
                    FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
                    SwingingBarTimingCueTracker.FrameCounterIndex)
                : (byte)0;
        var isAttemptWaiting =
            module == FieldPositionReader.FieldModule &&
            fieldId == SwingingBarTimingCueTracker.SwingingBarFieldId &&
            ReadByte(
                FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
                SwingingBarTimingCueTracker.AttemptWaitingIndex) == 1;
        var isUserControlLocked =
            module == FieldPositionReader.FieldModule &&
            ReadByte(FieldAudibleCueStateReader.AddressUserControl) != 0;
        if (!swingingBarTimingCueTracker.Observe(
                module,
                fieldId,
                position.X,
                position.Y,
                position.Z,
                isAttemptWaiting,
                isUserControlLocked,
                frameCounter))
        {
            return;
        }

        var reason =
            $"field={fieldId}, bank={SwingingBarTimingCueTracker.FrameCounterBank}, " +
            $"index={SwingingBarTimingCueTracker.FrameCounterIndex}, frame={frameCounter}, " +
            $"position={position.X},{position.Y},{position.Z}, triangle={position.TriangleId}, " +
            $"waiting={isAttemptWaiting}, controlLocked={isUserControlLocked}";
        swingingBarTimingCuePlayer?.Play(reason);
        Speak("Jump now.", interrupt: true);
        Log($"Swinging-bar native success window announced: {reason}.");
    }

    private void TickSquatMinigameCue()
    {
        if (!config.EnableSquatMinigamePrompts ||
            !foregroundProcessGate.IsCurrentProcessForeground() ||
            squatMinigameCueCoordinator is null)
        {
            squatMinigameCueCoordinator?.Reset();
            return;
        }

        var prompt = squatMinigameCueCoordinator.Observe();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        Speak(prompt, interrupt: true);
        Log($"Wall Market squat visual step announced from native state: {prompt}.");
    }

    private void TickFloor60SoldierTurnCue()
    {
        if (!config.EnableFloor60SoldierTurnCue ||
            !foregroundProcessGate.IsCurrentProcessForeground() ||
            fieldPositionReader is null ||
            fieldScriptLineStateReader is null)
        {
            floor60SoldierTurnCueTracker.Reset();
            floor60StatueBeaconPlayer?.StopAll();
            return;
        }

        var positionResult = fieldPositionReader.Read();
        if (!positionResult.IsUsable)
        {
            floor60SoldierTurnCueTracker.Reset();
            floor60StatueBeaconPlayer?.StopAll();
            return;
        }

        var position = positionResult.Position;
        if (position.CurrentModule != FieldPositionReader.FieldModule ||
            position.FieldId != Floor60SoldierTurnCueTracker.FloorId)
        {
            floor60SoldierTurnCueTracker.Reset();
            floor60StatueBeaconPlayer?.StopAll();
            return;
        }

        var moduleBefore = ReadByte(FieldPositionReader.AddressCurrentModule);
        var fieldBefore = ReadUInt16(FieldPositionReader.AddressFieldId);
        var barretSignalingProgress = ReadByte(
            FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
            Floor60SoldierTurnCueTracker.BarretSignalingProgressIndex);
        var tifaSignalingProgress = ReadByte(
            FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
            Floor60SoldierTurnCueTracker.TifaSignalingProgressIndex);
        var minigameActive = ReadByte(
            FieldNavigationObjectReader.AddressTemporaryFieldBankBase +
            Floor60SoldierTurnCueTracker.MinigameActiveIndex) != 0;
        var userControlLocked =
            ReadByte(FieldAudibleCueStateReader.AddressUserControl) != 0;
        var guardsCleared =
            (ReadByte(
                 FieldNavigationObjectReader.AddressFieldBankBase +
                 0x100 +
                 Floor60SoldierTurnCueTracker.GuardsClearedIndex) &
             Floor60SoldierTurnCueTracker.GuardsClearedMask) != 0;
        if (!fieldScriptLineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.FirstCompletionLineEntityId,
                out var firstCompletionLineEnabled) ||
            !fieldScriptLineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.SecondCompletionLineEntityId,
                out var secondCompletionLineEnabled) ||
            !fieldScriptLineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.FirstLineEntityIds[0],
                out var firstLeftEnabled) ||
            !fieldScriptLineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.FirstLineEntityIds[1],
                out var firstMiddleEnabled) ||
            !fieldScriptLineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.FirstLineEntityIds[2],
                out var firstRightEnabled) ||
            !fieldScriptLineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.SecondLineEntityIds[0],
                out var secondLeftEnabled) ||
            !fieldScriptLineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.SecondLineEntityIds[1],
                out var secondMiddleEnabled) ||
            !fieldScriptLineStateReader.TryRead(
                Floor60SoldierTurnCueTracker.SecondLineEntityIds[2],
                out var secondRightEnabled))
        {
            floor60SoldierTurnCueTracker.Reset();
            floor60StatueBeaconPlayer?.StopAll();
            return;
        }

        var fieldAfter = ReadUInt16(FieldPositionReader.AddressFieldId);
        var moduleAfter = ReadByte(FieldPositionReader.AddressCurrentModule);
        if (moduleBefore != moduleAfter ||
            fieldBefore != fieldAfter ||
            moduleAfter != FieldPositionReader.FieldModule ||
            fieldAfter != Floor60SoldierTurnCueTracker.FloorId)
        {
            floor60SoldierTurnCueTracker.Reset();
            floor60StatueBeaconPlayer?.StopAll();
            return;
        }

        var guardTiming = floor60GuardTimingStateReader?.Read() ??
            Floor60GuardTimingSnapshot.Invalid("Floor 60 timing reader unavailable");
        var now = DateTime.UtcNow;
        var decision = floor60SoldierTurnCueTracker.Observe(
                position,
                barretSignalingProgress,
                tifaSignalingProgress,
                minigameActive,
                guardsCleared,
                userControlLocked,
                firstCompletionLineEnabled,
                secondCompletionLineEnabled,
                firstLeftEnabled,
                firstMiddleEnabled,
                firstRightEnabled,
                secondLeftEnabled,
                secondMiddleEnabled,
                secondRightEnabled,
                guardTiming,
                now);
        if (decision.StopHideSpotBeacon)
        {
            floor60StatueBeaconPlayer?.StopAll();
        }

        var reason =
            $"field={fieldAfter}, position={position.X},{position.Y},{position.Z}, triangle={position.TriangleId}, " +
            $"barretProgress={barretSignalingProgress}, tifaProgress={tifaSignalingProgress}, " +
            $"controlLocked={userControlLocked}, completionLines={firstCompletionLineEnabled},{secondCompletionLineEnabled}, " +
            $"firstLines={firstLeftEnabled},{firstMiddleEnabled},{firstRightEnabled}, " +
            $"secondLines={secondLeftEnabled},{secondMiddleEnabled},{secondRightEnabled}, " +
            $"guardTiming={guardTiming.Diagnostic}";
        if (decision.PlayActionCue)
        {
            floor60ActionCuePlayer?.Play(
                $"cue={decision.SpeechCue}, {reason}");
        }

        if (decision.PlayHideSpotBeacon &&
            decision.HideSpotTarget is { } hideSpot &&
            fieldNavigationControlReader?.Read(position) is { IsUsable: true } control)
        {
            var target = hideSpot.ToNavigationTarget(
                Math.Max(0, config.Floor60StatueArrivalDistanceUnits));
            var spatialCue = FieldProximitySpatializer.CreateCue(
                position,
                target,
                control.Transform);
            if (spatialCue is not null &&
                floor60StatueBeaconPlayer?.Play(spatialCue.Value) == true)
            {
                Log(
                    $"Floor 60 statue locator played: statue={hideSpot.SequenceIndex}, " +
                    $"target={hideSpot.X},{hideSpot.Y},{hideSpot.Z}, targetTriangle={hideSpot.TriangleId}, " +
                    $"distance={spatialCue.Value.DistanceUnits:0}, {reason}.");
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
            Speak(speech, interrupt: true);
            Log(
                $"Floor 60 native guard cue announced: cue={decision.SpeechCue}, {reason}.");
        }
    }

    private void TickBattleSessionState()
    {
        if (ReadByte(BattleStateReader.AddressCurrentModule) != BattleStateReader.BattleModule)
        {
            ResetBattleInteractionSpeech();
        }
    }

    internal static InventoryItemReader CreateMenuInventoryItemReader(
        ILegacyAddressSpace addressSpace,
        Kernel2TextDatabase? textDatabase)
    {
        ArgumentNullException.ThrowIfNull(addressSpace);
        Func<int, string?>? resolveName = textDatabase is null
            ? null
            : textDatabase.ResolveInventoryObjectName;
        Func<int, string?>? resolveDescription = textDatabase is null
            ? null
            : textDatabase.ResolveInventoryObjectDescription;
        return new InventoryItemReader(addressSpace, resolveName, resolveDescription);
    }

    internal static bool ShouldOwnBattleStatusHotkeys(
        AccessibilityConfig config,
        bool battleQueryReadable,
        bool battleQueryActive)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.EnableSpeech &&
            battleQueryReadable &&
            battleQueryActive;
    }

    internal static bool NavigationOwnsBattleStatusLimitKey(
        AccessibilityConfig config,
        int currentModule)
    {
        ArgumentNullException.ThrowIfNull(config);
        return currentModule switch
        {
            FieldPositionReader.FieldModule => config.EnableFieldNavigationAssistant,
            WorldMapStateReader.WorldModule => config.EnableWorldMapNavigationAssistant,
            _ => false
        };
    }

    private void TickBattleStatusHotkeys()
    {
        var currentModule = ReadByte(BattleStateReader.AddressCurrentModule);
        var isForeground = foregroundProcessGate.IsCurrentProcessForeground();
        var navigationOwnsLimitKey = NavigationOwnsBattleStatusLimitKey(
            config,
            currentModule);
        var isLimitDown =
            (GetAsyncKeyState(VirtualKeyL) & unchecked((short)0x8000)) != 0;
        var battleLimitPressed = battleStatusLimitKeyFrameRouter.BeginFrame(
            isLimitDown,
            isForeground,
            currentModule,
            navigationOwnsLimitKey);
        var battleQueryActive = false;
        var battleQueryReadable = battleStateReader is not null &&
            battleStateReader.TryReadBattleQueryActive(out battleQueryActive);
        var battleActive = ShouldOwnBattleStatusHotkeys(
            config,
            battleQueryReadable,
            battleQueryActive);
        var speech = battleStatusHotkeyController.Poll(
            battleActive,
            virtualKey => virtualKey == VirtualKeyL
                ? battleLimitPressed
                : WasNavigationKeyPressed(virtualKey, isForeground),
            ReadBattleStatusMember,
            observeLimitKey: true,
            resetSelectionWhenInactive:
                battleQueryReadable && !battleQueryActive);
        if (string.IsNullOrWhiteSpace(speech))
        {
            return;
        }

        Log($"Battle status hotkey: slot={battleStatusHotkeyController.SelectedPartySlot + 1}, text={speech}");
        Speak(speech, interrupt: true);
    }

    private BattleStatusMemberSnapshot? ReadBattleStatusMember(int partySlot)
    {
        return battleStateReader?.TryReadPartyStatusMember(partySlot, out var member) == true
            ? member
            : null;
    }

    private void TickOpeningMovieDescription()
    {
        if (openingMovieDescription is null)
        {
            return;
        }

        if (config.EnableOpeningMovieDescription)
        {
            openingMovieDescription.Tick();
        }

        if (!openingMovieProbeLifetime.ShouldProbe)
        {
            return;
        }

        if (DateTime.UtcNow - lastOpeningMovieProbeAt < TimeSpan.FromMilliseconds(Math.Max(100, config.OpeningMovieProbeIntervalMs)))
        {
            return;
        }

        lastOpeningMovieProbeAt = DateTime.UtcNow;
        if (!ffnxRuntimeLoaded && FfnxRuntimeDetector.IsLoaded(Process.GetCurrentProcess()))
        {
            ffnxRuntimeLoaded = true;
            Log("FFNx became active; Reloaded opening narration remains independent of the movie backend.");
        }

        var openingPath = ResolveOpeningMoviePath();
        if (openingPath is null)
        {
            openingMoviePlaybackActive = false;
            openingMovieAudioTrackPlayer?.Stop("movie path unavailable");
            return;
        }

        var fileHandleActive = RestartManagerProbe.IsFileOpenByProcess(
            openingPath,
            Process.GetCurrentProcess().Id);
        var nativeFieldIdBefore = ReadUInt16(FieldPositionReader.AddressFieldId);
        var nativeCueState = default(FieldAudibleCueState);
        var nativeStateReadable =
            fieldAudibleCueStateReader?.TryRead(out nativeCueState) == true;
        var nativeFieldIdAfter = ReadUInt16(FieldPositionReader.AddressFieldId);
        nativeStateReadable &= nativeFieldIdBefore == nativeFieldIdAfter;
        var activity = OpeningMovieActivityPolicy.Resolve(
            fileHandleActive,
            nativeStateReadable,
            nativeCueState.Module,
            nativeFieldIdBefore,
            nativeCueState.MovieActive);
        var isActive = activity.IsActive;
        openingMoviePlaybackActive = isActive;
        if (openingMovieDetected)
        {
            if (openingMovieDescription.IsRunning &&
                !isActive &&
                openingMovieDescription.ElapsedSeconds < OpeningMovieDescription.MovieEndSeconds)
            {
                Log($"Opening movie ended early at {openingMovieDescription.ElapsedSeconds:0.0}s; stopping screenreader description.");
                openingMovieDescription.Stop();
            }

            if (!isActive)
            {
                openingMovieAudioTrackPlayer?.Stop("movie ended or skipped");
            }

            CompleteOpeningMovieProbeLifetime(isActive);

            return;
        }

        if (isActive)
        {
            openingMovieDetected = true;
            Log(
                $"Detected active opening movie: signal={activity.Signal}, " +
                $"path={openingPath}");
            if (config.EnableOpeningMovieDescription)
            {
                openingMovieDescription.Start();
            }

            if (OpeningMovieAudioTrackPolicy.ShouldUseReloadedPlayback(
                    config.EnableOpeningMovieAudioTrack,
                    ffnxRuntimeLoaded))
            {
                openingMovieAudioTrackPlayer?.Start("native opening movie start");
            }
        }

        CompleteOpeningMovieProbeLifetime(isActive);
    }

    private void CompleteOpeningMovieProbeLifetime(bool movieFileActive)
    {
        var wasActive = openingMovieProbeLifetime.ShouldProbe;
        var module = ReadByte(FieldPositionReader.AddressCurrentModule);
        var fieldId = ReadUInt16(FieldPositionReader.AddressFieldId);
        var supportedEchoSDisclaimer =
            module == FieldPositionReader.FieldModule &&
            fieldId == 109 &&
            TryGetLoadedFieldScriptIdentity(fieldId, out var identity) &&
            EchoSCompatibilityManifest.IsSupportedDisclaimer(identity);
        openingMovieProbeLifetime.Observe(
            module,
            fieldId,
            openingMovieDetected,
            movieFileActive,
            supportedEchoSDisclaimer);
        if (wasActive && !openingMovieProbeLifetime.ShouldProbe)
        {
            Log("Opening movie detection complete; stopped Restart Manager file-handle polling.");
        }
    }

    private void TickFieldCutsceneDescriptions()
    {
        if (!config.EnableFieldCutsceneDescriptions)
        {
            ResetFieldCutsceneDescriptionState();
            return;
        }

        RefreshFieldCutsceneDescriptionHook();

        if (ReadByte(FieldScriptContextReader.AddressCurrentModule) != FieldPositionReader.FieldModule)
        {
            ResetFieldCutsceneDescriptionState(resetCompatibilityState: true);
            return;
        }

        var fieldId = ReadUInt16(FieldScriptContextReader.AddressCurrentFieldId);
        if (EchoSCompatibilityManifest.SupportsDescriptionField(fieldId))
        {
            _ = TryGetLoadedFieldScriptIdentity(fieldId, out _);
        }

        if (FieldCutsceneSpeechPriority.ShouldWaitForDialogue(
                ReadByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount),
                fieldMessageReader?.HasReadableActiveWindow() == true))
        {
            return;
        }

        FieldCutsceneDescriptionCue? cue = null;
        lock (fieldCutsceneDescriptionSync)
        {
            while (pendingFieldCutsceneDescriptions.Count > 0)
            {
                var candidate = pendingFieldCutsceneDescriptions.Dequeue();
                if (candidate.FieldId == fieldId)
                {
                    cue = candidate;
                    break;
                }
            }
        }

        if (cue is null)
        {
            return;
        }

        if (config.EnableFieldCutsceneDescriptionDiagnostics)
        {
            Log(
                $"Field cutscene description speech: field={cue.Value.FieldId}, entity={cue.Value.EntityId}, " +
                $"script={cue.Value.ScriptId}, byte={cue.Value.ByteIndex}, text={cue.Value.Text}");
        }

        var now = DateTime.UtcNow;
        fieldCutsceneSpeechPriority.BeginNarration(cue.Value.FieldId, cue.Value.Text, now);
        Speak(cue.Value.Text, false);
    }

    private void ResetFieldCutsceneDescriptionState(bool resetCompatibilityState = false)
    {
        fieldCutsceneDescriptionTracker.Reset();
        fieldCutsceneSpeechPriority.Reset();
        if (resetCompatibilityState)
        {
            loadedFieldScriptIdentity = null;
            echoSDisclaimerSpeechTracker.Reset();
            echoSReactorTimerOverrideTracker.Reset();
        }
        lock (fieldCutsceneDescriptionSync)
        {
            pendingFieldCutsceneDescriptions.Clear();
        }
    }

    private bool TryGetLoadedFieldScriptIdentity(
        int expectedFieldId,
        out LoadedFieldScriptIdentity identity)
    {
        identity = default;
        if (expectedFieldId < 0 ||
            ReadByte(FieldScriptContextReader.AddressCurrentModule) != FieldPositionReader.FieldModule)
        {
            return false;
        }

        var currentPointer = ReadUInt32(FieldScriptContextReader.AddressFieldScriptPtr);
        if (loadedFieldScriptIdentity is { } cached &&
            cached.FieldId == expectedFieldId &&
            cached.ScriptPointer == currentPointer)
        {
            identity = cached;
            return true;
        }

        if (loadedFieldScriptIdentityReader?.TryRead(out var observed) != true ||
            observed.FieldId != expectedFieldId ||
            observed.ScriptPointer != currentPointer)
        {
            return false;
        }

        if (loadedFieldScriptIdentity is { } previous &&
            (previous.FieldId != observed.FieldId || previous.ScriptPointer != observed.ScriptPointer))
        {
            echoSDisclaimerSpeechTracker.ObserveLifecycle(observed);
            echoSReactorTimerOverrideTracker.ObserveLifecycle(observed);
        }

        identity = observed;
        var variant = EchoSCompatibilityManifest.ResolveVariant(observed);
        var isSupportedDisclaimer = EchoSCompatibilityManifest.IsSupportedDisclaimer(observed);
        if (variant != SupportedFieldScriptVariant.Unknown || isSupportedDisclaimer)
        {
            loadedFieldScriptIdentity = observed;
        }

        if (!echoSCompatibilityActive &&
            (variant == SupportedFieldScriptVariant.EchoS124 ||
             isSupportedDisclaimer))
        {
            echoSCompatibilityActive = true;
            Log($"Activated exact {EchoSCompatibilityManifest.VersionLabel} compatibility mode.");
        }

        var label = isSupportedDisclaimer
            ? $"{EchoSCompatibilityManifest.VersionLabel} disclaimer"
            : variant.ToString();
        var diagnosticKey = $"{observed.FieldId}:{observed.ScriptPrefixSha256}";
        if ((variant != SupportedFieldScriptVariant.Unknown || isSupportedDisclaimer ||
             config.EnableFieldCutsceneDescriptionDiagnostics) &&
            loggedLoadedFieldScriptIdentities.Add(diagnosticKey))
        {
            Log(
                $"Loaded field script identity: field={observed.FieldId}, pointer=0x{observed.ScriptPointer:X8}, " +
                $"variant={label}, sha256={observed.ScriptPrefixSha256}.");
        }
        return true;
    }

    private byte ResolveFieldNavigationObjectCollectedMask(
        FieldNavigationObjectDefinition definition)
    {
        if (definition.FieldId == 116 &&
            TryGetLoadedFieldScriptIdentity(definition.FieldId, out var identity))
        {
            return EchoSCompatibilityManifest.ResolveObjectCollectedMask(identity, definition);
        }

        return definition.CollectedMask;
    }

    private bool HasPendingFieldCutsceneNarration(int fieldId)
    {
        lock (fieldCutsceneDescriptionSync)
        {
            return pendingFieldCutsceneDescriptions.Any(cue => cue.FieldId == fieldId);
        }
    }

    private void TickTitleMenuReader()
    {
        if (!config.EnableTitleMenuVisualReader)
        {
            return;
        }

        using var bitmap = GameWindowCapture.CaptureCurrentProcessClient();
        if (bitmap is null)
        {
            RegisterTitleMenuMiss();
            return;
        }

        var detected = titleMenuVisualDetector.Detect(bitmap);
        if (detected is null)
        {
            RegisterTitleMenuMiss();
            return;
        }

        titleMenuMissCount = 0;
        if (detected.Item == lastTitleMenuItem)
        {
            return;
        }

        lastTitleMenuItem = detected.Item;
        Log($"Title menu visual reader detected: {detected.Item} (new={detected.NewGameCursorScore}, continue={detected.ContinueCursorScore})");
        Speak(detected.Item);
    }

    private void RegisterTitleMenuMiss()
    {
        if (lastTitleMenuItem.Length == 0)
        {
            return;
        }

        titleMenuMissCount++;
        if (titleMenuMissCount >= 6)
        {
            Log("Title menu visual reader no longer sees the title menu.");
            lastTitleMenuItem = string.Empty;
            titleMenuMissCount = 0;
        }
    }

    private void TickFieldCountdownSpeech()
    {
        if (!config.EnableSpeech || fieldCountdownReader is null)
        {
            fieldCountdownSpeechCoordinator.Reset();
            return;
        }

        if (!fieldCountdownReader.TryReadSnapshot(out var snapshot))
        {
            fieldCountdownSpeechCoordinator.Observe(null);
            return;
        }

        fieldCountdownSpeechCoordinator.Observe(snapshot);
        if (!fieldCountdownSpeechCoordinator.TryGetPending(out var announcement))
        {
            return;
        }

        if (Speak(announcement.Speech, interrupt: true))
        {
            fieldCountdownSpeechCoordinator.Acknowledge(announcement);
            Log(
                $"Field countdown speech: {announcement.Speech} " +
                $"(remaining={announcement.RemainingSeconds}).");
        }
    }

    private void TickFieldMessageReader()
    {
        if (!config.EnableFieldMessageReader)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastFieldMessageScanAt < TimeSpan.FromMilliseconds(Math.Max(50, config.FieldMessageScanIntervalMs)))
        {
            return;
        }

        lastFieldMessageScanAt = now;
        try
        {
            if (fieldAudibleCueStateReader?.TryRead(out var cueState) != true)
            {
                fieldVisibleWindowSpeechCoordinator.ObserveUnavailable();
                return;
            }

            if (cueState.Module != FieldPositionReader.FieldModule)
            {
                deferredZoneSpeechTracker.LeaveField();
                lastDeferredZoneLogText = string.Empty;
                nativeFieldMessageOwnershipTracker.Reset();
                ResetFieldAskState();
                observedFieldMessageFieldId = -1;
                ResetFieldMessageCandidate(now);
                return;
            }

            var positionResult = fieldPositionReader?.Read();
            if (positionResult is not { IsUsable: true })
            {
                fieldVisibleWindowSpeechCoordinator.ObserveUnavailable();
                return;
            }

            if (config.EnableFieldMessageWindowDiagnostics)
            {
                LogFieldMessageWindowDiagnostics();
            }

            var fieldId = positionResult.Value.Position.FieldId;
            HandleConfirmedFieldMessageTransition(fieldId);
            if (fieldId == 109 &&
                TryGetLoadedFieldScriptIdentity(fieldId, out var loadedIdentity) &&
                echoSDisclaimerSpeechTracker.OwnsVisibleSpeech(loadedIdentity))
            {
                // The fingerprint-bound native open hook already spoke this
                // startup page. Do not let a late window snapshot duplicate it.
                fieldVisibleWindowSpeechCoordinator.ObserveUnavailable();
                return;
            }

            var activeMessageCount = cueState.ActiveMessageCount;
            var userControl = cueState.UserControl;
            var lineCandidate = fieldMessageReader?.TryReadLineBuffer(out var checkedLine) == true
                ? checkedLine
                : new FieldMessageCandidate(string.Empty, string.Empty);
            if (fieldMessageReader?.TryReadVisibleWindows(out var visibleWindows) != true)
            {
                fieldVisibleWindowSpeechCoordinator.ObserveUnavailable();
                return;
            }
            var ordinaryVisibleWindows = visibleWindows
                .Where(window => !fieldCountdownSpeechCoordinator.ShouldSuppressWindow(window))
                .ToArray();
            var openingMovieBlocked = DeferredZoneSpeechTracker.ShouldBlockForOpeningMovie(
                fieldId,
                openingMovieDetected,
                openingMoviePlaybackActive,
                openingMovieDescription?.IsRunning == true);
            var narrationPending = HasPendingFieldCutsceneNarration(fieldId);
            var narrationProtected = fieldCutsceneSpeechPriority.ShouldQueueDialogue(fieldId, now);
            var zoneAnnouncementBlocked = DeferredZoneSpeechTracker.ShouldBlockForFieldEntry(
                fieldId,
                openingMovieBlocked,
                activeMessageCount,
                userControl,
                narrationPending,
                narrationProtected);
            var zoneCandidate = DeferredZoneSpeechTracker.IsZoneCandidate(lineCandidate);
            var zoneSpeech = deferredZoneSpeechTracker.Observe(
                fieldId,
                lineCandidate,
                now,
                zoneAnnouncementBlocked);

            if (zoneSpeech is not null)
            {
                var accepted = Speak(
                    zoneSpeech,
                    interrupt: deferredZoneSpeechTracker.ShouldInterruptPendingAnnouncement);
                if (accepted && deferredZoneSpeechTracker.Acknowledge(fieldId, zoneSpeech))
                {
                    Log($"Zone name speech: {zoneSpeech} (field={fieldId}).");
                    lastDeferredZoneLogText = string.Empty;
                }
            }
            else if (zoneCandidate &&
                     !string.Equals(lineCandidate.Text, lastDeferredZoneLogText, StringComparison.Ordinal))
            {
                lastDeferredZoneLogText = lineCandidate.Text;
                var waitReason = fieldId != DeferredZoneSpeechTracker.OpeningFieldId
                    ? "stable field entry"
                    : openingMovieBlocked
                        ? "opening movie"
                        : narrationPending
                            ? "pending cutscene description"
                            : narrationProtected
                                ? "cutscene description speech"
                                : activeMessageCount != 0
                                    ? "active dialogue"
                                    : userControl != 0
                                        ? "scripted control lock"
                                        : "stable field entry";
                Log($"Zone name queued until {waitReason}: {lineCandidate.Text} (field={fieldId}).");
            }

            if (!deferredZoneSpeechTracker.IsCurrentFieldSettled(fieldId, now))
            {
                fieldVisibleWindowSpeechCoordinator.ObserveUnavailable();
                return;
            }

            var askIdentity = Volatile.Read(ref activeFieldAskIdentity);
            if (askIdentity is null ||
                askIdentity.FieldId != fieldId)
            {
                askIdentity = null;
            }

            var nativeOwnershipSpeechPending = askIdentity is not null &&
                (pendingNativeFieldSpeech.Contains(askIdentity) ||
                 incompleteNativeFieldSpeech.Contains(askIdentity) ||
                 nativeAskPollingFallbackState.IsRecoveryPending(askIdentity));
            var currentNativeAskLifecycle = askIdentity is not null &&
                begunNativeAskLifecycles.Contains(askIdentity);
            if (activeMessageCount == 0 && !currentNativeAskLifecycle)
            {
                nativeFieldMessageOwnershipTracker.Reset();
                ffnxVoicePlaybackTracker.ObserveNoMessages();
            }

            var candidateText = string.Join('\u001f', ordinaryVisibleWindows.Select(window => window.Text));
            var candidateSource = string.Join(',', ordinaryVisibleWindows.Select(window => $"window {window.WindowId}"));
            if (!string.Equals(candidateText, lastFieldMessageCandidateText, StringComparison.Ordinal) ||
                !string.Equals(candidateSource, lastFieldMessageCandidateSource, StringComparison.Ordinal))
            {
                lastFieldMessageCandidateText = candidateText;
                lastFieldMessageCandidateSource = candidateSource;
                foreach (var window in ordinaryVisibleWindows)
                {
                    Log($"Field message candidate (window {window.WindowId}): {window.Text}");
                }
            }

            var voiceTimestamp = Stopwatch.GetTimestamp();
            var speech = fieldVisibleWindowSpeechCoordinator.Observe(
                ordinaryVisibleWindows,
                activeMessageCount,
                now,
                window =>
                    FieldWindowPollingOwnership.IsSuppressed(
                        window,
                        askIdentity,
                        nativeFieldMessageOwnershipTracker,
                        activeMessageCount,
                        now,
                        nativeOwnershipSpeechPending) ||
                    (echoSCompatibilityActive &&
                     ffnxPlayVoiceHook is not null &&
                     askIdentity is null &&
                     ffnxVoicePlaybackTracker.ShouldSuppressPrism(
                         fieldId,
                         window.WindowId,
                         voiceTimestamp)),
                askIdentity,
                nativeFieldMessageOwnershipTracker.WasSpeechDelivered(
                    askIdentity,
                    now,
                    preserveActiveIdentity: askIdentity is not null),
                nativeOwnershipSpeechPending,
                requireDeliveryAcknowledgement: true);
            for (var index = 0; index < speech.Count; index++)
            {
                var item = speech[index];
                Log($"Field message stable (window {item.WindowId}): {item.Text}");
                bool delivered;
                try
                {
                    delivered = config.SpeakFieldMessages &&
                        Speak(item.Text, item.Interrupt);
                }
                catch
                {
                    fieldVisibleWindowSpeechCoordinator.AcknowledgePollingSpeech(
                        item.DispatchToken,
                        delivered: false);
                    for (var remaining = index + 1; remaining < speech.Count; remaining++)
                    {
                        fieldVisibleWindowSpeechCoordinator.AcknowledgePollingSpeech(
                            speech[remaining].DispatchToken,
                            delivered: false);
                    }

                    throw;
                }

                var pollingRecoveryIdentity =
                    fieldVisibleWindowSpeechCoordinator.AcknowledgePollingSpeech(
                    item.DispatchToken,
                    delivered);
                if (delivered && pollingRecoveryIdentity is not null)
                {
                    HandleNativeAskPollingQuestionRecovered(pollingRecoveryIdentity);
                }
                if (!delivered)
                {
                    for (var remaining = index + 1; remaining < speech.Count; remaining++)
                    {
                        fieldVisibleWindowSpeechCoordinator.AcknowledgePollingSpeech(
                            speech[remaining].DispatchToken,
                            delivered: false);
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            fieldVisibleWindowSpeechCoordinator.ObserveUnavailable();
            fieldMessageReaderErrorCount++;
            if (fieldMessageReaderErrorCount <= 10)
            {
                Log($"Field message reader error: {ex}");
            }
        }
    }

    private void LogFieldMessageWindowDiagnostics()
    {
        if (fieldMessageReader?.TryReadVisibleWindows(out var windows) != true)
        {
            const string unavailable = "native window snapshot unavailable";
            if (!string.Equals(unavailable, lastFieldMessageWindowDiagnostics, StringComparison.Ordinal))
            {
                lastFieldMessageWindowDiagnostics = unavailable;
                Log($"Field message windows: {unavailable}");
            }

            return;
        }

        var line = windows.Count == 0
            ? "none"
            : string.Join(
                " | ",
                windows.Select(window =>
                    $"w{window.WindowId}:state=0x{window.NativeState:X2}:text={PreviewFieldCandidate(new FieldMessageCandidate($"window {window.WindowId}", window.Text))}"));
        if (string.Equals(line, lastFieldMessageWindowDiagnostics, StringComparison.Ordinal))
        {
            return;
        }

        lastFieldMessageWindowDiagnostics = line;
        Log($"Field message windows: {line}");
    }

    private void ResetFieldMessageCandidate(DateTime now)
    {
        if (lastFieldMessageCandidateText.Length != 0)
        {
            Log("Field message buffers cleared.");
        }

        lastFieldMessageCandidateText = string.Empty;
        lastFieldMessageCandidateSource = string.Empty;
        fieldVisibleWindowSpeechCoordinator.Reset();
    }

    private void HandleConfirmedFieldMessageTransition(int fieldId)
    {
        if (observedFieldMessageFieldId < 0)
        {
            observedFieldMessageFieldId = fieldId;
            return;
        }

        if (observedFieldMessageFieldId == fieldId)
        {
            return;
        }

        observedFieldMessageFieldId = fieldId;
        ffnxVoicePlaybackTracker.ObserveFieldTransition(fieldId);
        var activeIdentity = Volatile.Read(ref activeFieldAskIdentity);
        if (activeIdentity is null || activeIdentity.FieldId != fieldId)
        {
            ResetFieldAskState();
            nativeFieldMessageOwnershipTracker.Reset();
        }
        else
        {
            foreach (var staleIdentity in acceptedNativeAskPromptKeys.Keys
                         .Where(identity => identity.FieldId != fieldId)
                         .ToArray())
            {
                CancelPendingNativeFieldSpeech(staleIdentity);
            }

            fieldOpcodeMessageSpeechGate.Reset();
            foreach (var promptKey in acceptedNativeAskPromptKeys.Values)
            {
                fieldOpcodeMessageSpeechGate.ShouldQueue(promptKey, result: 1);
            }
        }

        ResetFieldMessageCandidate(DateTime.UtcNow);
        if (activeIdentity is not null &&
            activeIdentity.FieldId == fieldId &&
            begunNativeAskLifecycles.Contains(activeIdentity))
        {
            fieldVisibleWindowSpeechCoordinator.BeginNativeAskLifecycle(
                activeIdentity,
                requireCoherentObservation: config.EnableFieldMessageReader,
                now: DateTime.UtcNow,
                maximumObservationWait: TimeSpan.FromMilliseconds(
                    Math.Max(250, config.FieldMessageScanIntervalMs * 2)));
        }
    }

    private static FieldMessageCandidate SelectCurrentFieldMessageCandidate(
        FieldMessageCandidate visibleCandidate,
        byte activeMessageCount)
    {
        return activeMessageCount == 0 ||
            !visibleCandidate.Source.StartsWith("window ", StringComparison.Ordinal)
            ? new FieldMessageCandidate(string.Empty, string.Empty)
            : visibleCandidate;
    }

    private PendingNativeFieldSpeechEnqueueResult QueueNativeFieldMessageSpeech(
        FieldMessageCandidate candidate,
        DateTime now,
        string? explicitKey = null,
        NativeFieldMessageIdentity? ownershipIdentity = null,
        NativeFieldSpeechKind kind = NativeFieldSpeechKind.Prompt,
        bool completesVisibleContent = false)
    {
        if (!config.SpeakFieldMessages || candidate.Text.Length == 0)
        {
            return PendingNativeFieldSpeechEnqueueResult.Invalid;
        }

        var key = explicitKey ?? $"{candidate.Source}\u001f{candidate.Text}";
        var entry = new PendingNativeFieldSpeech(
            candidate,
            ownershipIdentity,
            key,
            now,
            kind,
            CompletesVisibleContent: completesVisibleContent);
        var result = pendingNativeFieldSpeech.Enqueue(entry);
        if (result == PendingNativeFieldSpeechEnqueueResult.Full)
        {
            // Overflow invalidates the entire exact native speech sequence.
            // No partial ownership may suppress checked polling fallback.
            CancelPendingNativeFieldSpeech(
                ownershipIdentity,
                preserveGateForPollingFallback: true);
            Log("Native field speech queue full; exact native ownership released for polling fallback.");
            return result;
        }

        if (result is not (PendingNativeFieldSpeechEnqueueResult.Enqueued or
            PendingNativeFieldSpeechEnqueueResult.Coalesced))
        {
            return result;
        }

        if (ownershipIdentity is not null)
        {
            nativeFieldMessageOwnershipTracker.ObserveNative(ownershipIdentity, candidate.Text, now);
        }

        return result;
    }

    private void TickFieldMessageOpenSpeech()
    {
        if (!config.SpeakFieldMessages)
        {
            CancelPendingNativeFieldSpeech(
                null,
                preserveGateForPollingFallback: true);
            return;
        }

        var now = DateTime.UtcNow;
        var settleTime = TimeSpan.FromMilliseconds(Math.Max(0, config.FieldMessageOpenSpeechSettleMs));
        while (pendingNativeFieldSpeech.TryPeekReady(now, settleTime, out var peek))
        {
            var nativeInterrupt = true;
            if (peek.OwnershipIdentity is not null &&
                !IsCurrentNativeFieldSpeechIdentity(peek.OwnershipIdentity))
            {
                CancelPendingNativeFieldSpeech(peek.OwnershipIdentity);
                Log(
                    $"Canceled stale native field speech: field={peek.OwnershipIdentity.FieldId}, " +
                    $"window={peek.OwnershipIdentity.WindowId}, dialog={peek.OwnershipIdentity.DialogId}.");
                continue;
            }

            if (peek.OwnershipIdentity is not null &&
                !fieldVisibleWindowSpeechCoordinator.CanDispatchNativeSpeech(
                    peek.OwnershipIdentity,
                    now,
                    out nativeInterrupt))
            {
                return;
            }

            if (!pendingNativeFieldSpeech.TryTakeReady(now, settleTime, out var pending))
            {
                return;
            }

            var candidate = pending.Candidate;
            var ownershipIdentity = pending.OwnershipIdentity;
            if (ownershipIdentity is not null && !IsCurrentNativeFieldSpeechIdentity(ownershipIdentity))
            {
                CancelPendingNativeFieldSpeech(ownershipIdentity);
                Log(
                    $"Canceled stale native field speech: field={ownershipIdentity.FieldId}, " +
                    $"window={ownershipIdentity.WindowId}, dialog={ownershipIdentity.DialogId}.");
                continue;
            }

            var continuesPrompt = ownershipIdentity is not null &&
                pending.Kind == NativeFieldSpeechKind.ChoiceUpdate &&
                partiallyDeliveredNativeFieldSpeech.Contains(ownershipIdentity);
            PendingNativeFieldSpeech? mergedChoice = null;
            if (ownershipIdentity is not null &&
                pending.Kind == NativeFieldSpeechKind.Prompt &&
                pendingNativeFieldSpeech.TryTakeReadyChoiceFor(
                    ownershipIdentity,
                    now,
                    settleTime,
                    out var readyChoice))
            {
                mergedChoice = readyChoice;
                candidate = NativeFieldSpeechBatchComposer.MergePromptAndChoice(
                    candidate,
                    readyChoice.Candidate);
            }

            Log($"Field message open speech ({candidate.Source}): {candidate.Text}");
            try
            {
                // Revalidate after composition and immediately before output;
                // a native result-zero callback can invalidate the exact token
                // while this monitor tick is preparing its utterance.
                if (ownershipIdentity is not null &&
                    !IsCurrentNativeFieldSpeechIdentity(ownershipIdentity))
                {
                    CancelPendingNativeFieldSpeech(ownershipIdentity);
                    return;
                }
                if (ownershipIdentity is not null &&
                    !ownershipIdentity.SpeechLifecycle.TryCommitEmission())
                {
                    CancelPendingNativeFieldSpeech(ownershipIdentity);
                    return;
                }

                var delivered = continuesPrompt || !nativeInterrupt
                    ? Speak(candidate.Text, interrupt: false)
                    : SpeakFieldDialogue(candidate.Text);
                if (!delivered)
                {
                    if (TryRequeueNativeFieldSpeechAfterOutputFailure(
                            pending,
                            mergedChoice,
                            DateTime.UtcNow))
                    {
                        Log("Native field speech was not delivered by Prism; exact lifecycle retained for retry.");
                        return;
                    }

                    CancelPendingNativeFieldSpeech(
                        ownershipIdentity,
                        preserveGateForPollingFallback: true);
                    Log("Native field speech was not delivered by Prism; ownership released for polling fallback.");
                    return;
                }

                if (ownershipIdentity is not null)
                {
                    var visibleContentComplete = pending.CompletesVisibleContent ||
                        mergedChoice?.CompletesVisibleContent == true;
                    var hasQueuedNativeContinuation =
                        pendingNativeFieldSpeech.Contains(ownershipIdentity);
                    var currentVisibleContentComplete =
                        visibleContentComplete && !hasQueuedNativeContinuation;
                    if (pending.Kind == NativeFieldSpeechKind.Prompt &&
                        !visibleContentComplete)
                    {
                        // Cursor publication commonly follows the prompt by one
                        // monitor tick. The first selection must continue the
                        // prompt, while later navigation remains free to replace
                        // an obsolete selection utterance.
                        partiallyDeliveredNativeFieldSpeech.Add(ownershipIdentity);
                    }
                    else if (continuesPrompt)
                    {
                        // Consume the one-shot continuation only after Prism
                        // accepted that exact first choice. A failed attempt is
                        // requeued above and keeps the obligation intact.
                        partiallyDeliveredNativeFieldSpeech.Remove(ownershipIdentity);
                    }

                    if (currentVisibleContentComplete)
                    {
                        incompleteNativeFieldSpeech.Remove(ownershipIdentity);
                    }
                    else
                    {
                        incompleteNativeFieldSpeech.Add(ownershipIdentity);
                    }
                    var completedFallbackChoice =
                        pending.Kind == NativeFieldSpeechKind.ChoiceUpdate &&
                        nativeAskPollingFallbackState.IsFallback(ownershipIdentity);
                    var fallbackQuestionRecovered = completedFallbackChoice &&
                        nativeAskPollingFallbackState.IsQuestionRecovered(ownershipIdentity);
                    if (completedFallbackChoice)
                    {
                        var fallbackSequenceComplete =
                            nativeAskPollingFallbackState.MarkChoiceDelivered(ownershipIdentity);
                        if (fallbackSequenceComplete)
                        {
                            incompleteNativeFieldSpeech.Remove(ownershipIdentity);
                        }
                        else
                        {
                            // A bounded timeout may permit the only exact cursor
                            // signal before polling could deliver the question.
                            // Retain the later-sibling boundary until that exact
                            // polling recovery is actually acknowledged.
                            incompleteNativeFieldSpeech.Add(ownershipIdentity);
                        }
                    }

                    if (pending.Kind == NativeFieldSpeechKind.Prompt)
                    {
                        // A successfully spoken prompt immediately establishes
                        // the native ordering boundary, even when its choice is
                        // still inside the configured settle window. The later
                        // choice upgrades this partial acknowledgement.
                        fieldVisibleWindowSpeechCoordinator.AcknowledgeNativeSpeech(
                            ownershipIdentity,
                            currentVisibleContentComplete,
                            consumeOrderingBarrier: currentVisibleContentComplete);
                        nativeFieldMessageOwnershipTracker.MarkSpeechDelivered(
                            ownershipIdentity,
                            DateTime.UtcNow,
                            currentVisibleContentComplete);
                    }
                    else if (!hasQueuedNativeContinuation)
                    {
                        var coordinatorContentComplete =
                            currentVisibleContentComplete || fallbackQuestionRecovered;
                        nativeFieldMessageOwnershipTracker.MarkSpeechDelivered(
                            ownershipIdentity,
                            DateTime.UtcNow,
                            currentVisibleContentComplete);
                        fieldVisibleWindowSpeechCoordinator.AcknowledgeNativeSpeech(
                            ownershipIdentity,
                            coordinatorContentComplete,
                            consumeOrderingBarrier:
                                !completedFallbackChoice || fallbackQuestionRecovered);
                    }
                }

                // Preserve FIFO speech across monitor iterations. In
                // particular, a ready choice update must not immediately
                // interrupt the prompt that preceded it in this queue.
                return;
            }
            catch (Exception ex)
            {
                if (TryRequeueNativeFieldSpeechAfterOutputFailure(
                        pending,
                        mergedChoice,
                        DateTime.UtcNow))
                {
                    Log($"Native field speech output threw; exact lifecycle retained for retry: {ex}");
                    return;
                }

                // A failed output did not deliver native ownership. Cancel the
                // rest of this exact lifecycle so checked polling can recover.
                CancelPendingNativeFieldSpeech(
                    ownershipIdentity,
                    preserveGateForPollingFallback: true);
                throw;
            }
        }
    }

    private bool TryRequeueNativeFieldSpeechAfterOutputFailure(
        PendingNativeFieldSpeech pending,
        PendingNativeFieldSpeech? mergedChoice,
        DateTime now)
    {
        var maximumAttempt = Math.Max(
            pending.AttemptCount,
            mergedChoice?.AttemptCount ?? 0);
        var retryDelayMs = 100 * (1 << Math.Min(maximumAttempt, 3));
        var retryAt = now.AddMilliseconds(retryDelayMs);
        var retryEntries = new List<PendingNativeFieldSpeech>(mergedChoice is null ? 1 : 2)
        {
            pending with
            {
                SeenAt = retryAt,
                AttemptCount = pending.AttemptCount + 1
            }
        };
        if (mergedChoice is { } choice)
        {
            retryEntries.Add(choice with
            {
                SeenAt = retryAt,
                AttemptCount = choice.AttemptCount + 1
            });
        }

        return pendingNativeFieldSpeech.TryRequeueFront(retryEntries);
    }

    private void CancelPendingNativeFieldSpeech(
        NativeFieldMessageIdentity? identity,
        bool preserveGateForPollingFallback = false)
    {
        if (identity is null)
        {
            foreach (var acceptedIdentity in acceptedNativeAskPromptKeys.Keys.ToArray())
            {
                if (preserveGateForPollingFallback)
                {
                    AbandonNativeAskSpeechForPolling(acceptedIdentity);
                }
                else
                {
                    ReleaseAcceptedNativeAskPrompt(acceptedIdentity);
                }
            }
        }
        else if (preserveGateForPollingFallback)
        {
            AbandonNativeAskSpeechForPolling(identity);
        }
        else
        {
            ReleaseAcceptedNativeAskPrompt(identity);
            nativeAskPollingFallbackState.Remove(identity);
        }

        var canceledOwnership = pendingNativeFieldSpeech.Cancel(identity);
        foreach (var canceledIdentity in canceledOwnership)
        {
            if (preserveGateForPollingFallback &&
                nativeAskPollingFallbackState.IsFallback(canceledIdentity))
            {
                nativeFieldMessageOwnershipTracker.Release(canceledIdentity);
                continue;
            }

            var hadDeliveredNativeContent =
                partiallyDeliveredNativeFieldSpeech.Remove(canceledIdentity) |
                incompleteNativeFieldSpeech.Remove(canceledIdentity);
            if (hadDeliveredNativeContent)
            {
                fieldVisibleWindowSpeechCoordinator.ReleaseNativeSpeechOrderingOnly(canceledIdentity);
            }

            nativeFieldMessageOwnershipTracker.Release(canceledIdentity);
            fieldVisibleWindowSpeechCoordinator.CancelNativeSpeech(canceledIdentity);
        }

        // Result-zero can arrive after the candidate was dequeued but before
        // it was delivered. Always clear the exact coordinator/tracker claim.
        if (identity is not null && !canceledOwnership.Contains(identity))
        {
            if (preserveGateForPollingFallback &&
                nativeAskPollingFallbackState.IsFallback(identity))
            {
                nativeFieldMessageOwnershipTracker.Release(identity);
                return;
            }

            var hadDeliveredNativeContent =
                partiallyDeliveredNativeFieldSpeech.Remove(identity) |
                incompleteNativeFieldSpeech.Remove(identity);
            if (hadDeliveredNativeContent)
            {
                fieldVisibleWindowSpeechCoordinator.ReleaseNativeSpeechOrderingOnly(identity);
            }

            nativeFieldMessageOwnershipTracker.Release(identity);
            fieldVisibleWindowSpeechCoordinator.CancelNativeSpeech(identity);
        }

        if (identity is null)
        {
            foreach (var partialIdentity in partiallyDeliveredNativeFieldSpeech
                         .Concat(incompleteNativeFieldSpeech)
                         .Distinct()
                         .ToArray())
            {
                fieldVisibleWindowSpeechCoordinator.ReleaseNativeSpeechOrderingOnly(partialIdentity);
                fieldVisibleWindowSpeechCoordinator.CancelNativeSpeech(partialIdentity);
            }

            partiallyDeliveredNativeFieldSpeech.Clear();
            incompleteNativeFieldSpeech.Clear();
        }
    }

    private void ReleaseAcceptedNativeAskPrompt(
        NativeFieldMessageIdentity identity)
    {
        if (acceptedNativeAskPromptKeys.Remove(identity, out var promptKey))
        {
            fieldOpcodeMessageSpeechGate.ShouldQueue(promptKey, result: 0);
        }
    }

    private void AbandonNativeAskSpeechForPolling(
        NativeFieldMessageIdentity identity)
    {
        acceptedNativeAskPromptKeys.Remove(identity);
        var firstFallbackRegistration = nativeAskPollingFallbackState.Begin(identity);
        fieldAskChoiceSpeechTracker.Reset(identity.LifecycleToken);
        if (!firstFallbackRegistration)
        {
            return;
        }

        fieldVisibleWindowSpeechCoordinator.RequirePollingRecoveryBeforeNativeChoice(
            identity,
            pollingAvailable: config.EnableFieldMessageReader,
            now: DateTime.UtcNow,
            maximumWait: TimeSpan.FromMilliseconds(Math.Max(
                1000,
                config.FieldMessageStableMs + (config.FieldMessageScanIntervalMs * 2))));
    }

    private void HandleNativeAskPollingQuestionRecovered(
        NativeFieldMessageIdentity identity)
    {
        if (!ReferenceEquals(Volatile.Read(ref activeFieldAskIdentity), identity) ||
            !nativeAskPollingFallbackState.IsFallback(identity))
        {
            return;
        }

        if (!nativeAskPollingFallbackState.MarkQuestionRecovered(identity))
        {
            return;
        }

        incompleteNativeFieldSpeech.Remove(identity);
        fieldVisibleWindowSpeechCoordinator.AcknowledgeNativeSpeech(
            identity,
            visibleContentComplete: true,
            consumeOrderingBarrier: true);
    }

    private bool IsCurrentNativeFieldSpeechIdentity(NativeFieldMessageIdentity identity)
    {
        try
        {
            var before = Volatile.Read(ref activeFieldAskIdentity);
            var module = ReadByte(FieldPositionReader.AddressCurrentModule);
            var fieldId = ReadUInt16(FieldPositionReader.AddressFieldId);
            var after = Volatile.Read(ref activeFieldAskIdentity);
            return NativeFieldSpeechIdentityValidator.IsCurrent(
                identity,
                before,
                after,
                module,
                fieldId,
                before?.WindowId ?? -1,
                before?.DialogId ?? -1);
        }
        catch
        {
            return false;
        }
    }

    private unsafe void TickFieldFootstepFeedback()
    {
        if (!config.EnableFieldFootstepFeedback && !config.EnableFieldPositionDiagnostics)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastFieldFootstepScanAt < TimeSpan.FromMilliseconds(Math.Max(30, config.FieldFootstepScanIntervalMs)))
        {
            return;
        }

        lastFieldFootstepScanAt = now;
        try
        {
            if (fieldAudibleCueState.SuppressFootsteps)
            {
                fieldFootstepTracker.Reset();
                fieldFootstepDistanceProbe.ResetCurrentStride();
                return;
            }

            var result = fieldPositionReader?.Read() ?? throw new InvalidOperationException("Field position reader is not initialized.");
            var position = result.Position;
            if (config.EnableFieldPositionDiagnostics)
            {
                var state = result.Diagnostic;
                if (!string.Equals(state, lastFieldPositionDiagnosticState, StringComparison.Ordinal))
                {
                    lastFieldPositionDiagnosticState = state;
                    Log($"Field position: {state}");
                }
            }

            if (!result.IsUsable)
            {
                fieldFootstepTracker.Reset();
                fieldFootstepDistanceProbe.ResetCurrentStride();
                return;
            }

            if (lastFieldPositionModelBase != result.ModelBase)
            {
                lastFieldPositionModelBase = result.ModelBase;
                Log($"Field position reader selected live model base 0x{result.ModelBase:X8}.");
            }

            var isRunning = config.EnableFieldFootstepFeedback && ReadFieldRunState();
            var distanceUnitsPerFootstep = FieldNavigationDistanceCalibration.Resolve(
                position.FieldId,
                config.FieldNavigationSpeechDistanceUnitsPerCount);
            var footstepTriggered = config.EnableFieldFootstepFeedback &&
                                    fieldFootstepTracker.Observe(
                                        position,
                                        now,
                                        isRunning,
                                        distanceUnitsPerFootstep);
            if (config.EnableFieldFootstepDistanceProbe)
            {
                var input = fieldNavigationInputReader?.Read().Direction ?? FieldNavigationInput.None;
                var report = fieldFootstepDistanceProbe.Observe(
                    position,
                    now,
                    foregroundProcessGate.IsCurrentProcessForeground(),
                    input,
                    fieldFootstepTracker.LastCadence,
                    footstepTriggered);
                if (report is not null)
                {
                    Log($"Field footstep distance probe: {report}.");
                }
            }

            if (footstepTriggered)
            {
                Log($"Field footstep pace: {fieldFootstepTracker.LastPaceDiagnostic}");
                PlayFootstep("movement", position);
            }
        }
        catch (Exception ex)
        {
            fieldFootstepErrorCount++;
            if (fieldFootstepErrorCount <= 10)
            {
                Log($"Field footstep feedback error: {ex.Message}");
            }
        }
    }

    private void InitializeWorldMapAccessibility(ILegacyAddressSpace legacyAddressSpace)
    {
        worldMapRuntimes.Clear();
        worldMapStateReader = new WorldMapStateReader(legacyAddressSpace);
        worldMapEntityReader = new WorldMapEntityReader(legacyAddressSpace);
        worldMapNavigationProgressSink?.Dispose();
        worldMapNavigationProgressSink = null;
        worldMapNavigationProgressBar?.Dispose();
        worldMapNavigationProgressBar = config.EnableWorldMapNavigationAssistant
            ? new NativeFieldNavigationProgressBar(Log)
            : null;
        worldMapNavigationProgressSink = worldMapNavigationProgressBar is null
            ? null
            : new IntervalFieldNavigationProgressSink(
                worldMapNavigationProgressBar,
                navigationProgressController);
        worldMapNavigationBeaconPlayer?.Dispose();
        worldMapNavigationBeaconPlayer = config.EnableWorldMapNavigationAssistant
            ? new NavigationBeaconPlayer(
                ResolveWorldMapNavigationBeaconSoundPath(),
                config.WorldMapNavigationBeaconVolumePercent,
                Log)
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
        if (!File.Exists(coordinatePath) || !File.Exists(menuNamePath))
        {
            Log(
                "World-map accessibility unavailable: installed location metadata is missing. " +
                $"coordinates={coordinatePath}, names={menuNamePath}.");
            return;
        }

        foreach (var mapType in new[] { 0, 2, 3 })
        {
            var mapPath = ResolveWorldMapDataPath(mapType);
            if (mapPath is null)
            {
                Log($"World-map type {mapType} unavailable: native map file was not found.");
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
                    var runtime = new WorldMapRuntimeContext(
                        map,
                        catalog,
                        worldMapNavigationProgressSink,
                        Math.Max(1, config.WorldMapNavigationSpeechDistanceUnitsPerCount),
                        TimeSpan.FromMilliseconds(Math.Max(0, config.WorldMapNavigationSpeechIntervalMs)),
                        TimeSpan.FromMilliseconds(Math.Max(0, config.WorldMapNavigationBeaconIntervalMs)),
                        TimeSpan.FromMilliseconds(Math.Max(80, config.WorldMapFootstepWalkIntervalMs)),
                        TimeSpan.FromMilliseconds(Math.Max(80, config.WorldMapFootstepChocoboIntervalMs)));
                    worldMapRuntimes.Add((mapType, progressStage), runtime);
                    Log(
                        $"World-map type {mapType}, progress stage {progressStage} initialized from {mapPath}: " +
                        $"triangles={map.Triangles.Count}, locations={catalog.Locations.Count}, " +
                        $"chocoboTracks={catalog.ChocoboTracks.Count}, wrap={map.WrapWidth}x{map.WrapHeight}.");
                }
            }
            catch (Exception ex)
            {
                Log($"World-map type {mapType} initialization failed closed: {ex.Message}");
            }
        }

        Log(
            "World-map accessibility initialized with native WM geometry, native post-collision player movement, " +
            "live entity-backed Transportation and Events, and categories Locations, Story, Transportation, " +
            "Events, and Chocobo Tracks. Controls are U/O categories, J/L targets, K status, and I navigation.");
    }

    private void TickWorldMapAccessibility()
    {
        if (!config.EnableWorldMapFootstepFeedback && !config.EnableWorldMapNavigationAssistant)
        {
            battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                WorldMapStateReader.WorldModule);
            DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.WorldMap);
            StopNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap, announce: false);
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastWorldMapScanAt < TimeSpan.FromMilliseconds(Math.Max(30, config.WorldMapScanIntervalMs)) &&
            !battleStatusLimitKeyFrameRouter.HasNavigationPress(
                WorldMapStateReader.WorldModule) &&
            !HasNavigationAutoWalkToggle(NavigationAutoWalkDomain.WorldMap))
        {
            return;
        }

        lastWorldMapScanAt = now;
        try
        {
            var module = ReadByte(WorldMapStateReader.AddressCurrentModule);
            if (module != WorldMapStateReader.WorldModule)
            {
                battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                    WorldMapStateReader.WorldModule);
                DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.WorldMap);
                if (WorldMapNavigationLifecycle.IsCombatInterruptionModule(module))
                {
                    foreach (var context in worldMapRuntimes.Values)
                    {
                        context.Footsteps.Reset();
                        context.Navigation.PauseForCombat($"native combat module {module}");
                    }

                    worldMapNavigationBeaconPlayer?.StopAll();
                    SuspendNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap);
                    return;
                }

                if (module == FieldPositionReader.FieldModule)
                {
                    StopNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap, announce: false);
                }
                else
                {
                    SuspendNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap);
                }

                if (worldMapWasActive)
                {
                    ResetWorldMapAccessibility($"module changed to {module}");
                }

                return;
            }

            worldMapWasActive = true;
            var isForeground = foregroundProcessGate.IsCurrentProcessForeground();
            var stateResult = worldMapStateReader?.Read()
                ?? WorldMapStateReadResult.Invalid(default, "world state reader is not initialized");
            if (config.EnableWorldMapNavigationDiagnostics &&
                !string.Equals(stateResult.Diagnostic, lastWorldMapStateDiagnostic, StringComparison.Ordinal))
            {
                lastWorldMapStateDiagnostic = stateResult.Diagnostic;
                Log($"World-map state: {stateResult.Diagnostic}.");
            }

            if (!stateResult.IsUsable ||
                !worldMapRuntimes.TryGetValue(
                    (
                        stateResult.State.WorldMapType,
                        WorldMapDataLoader.ResolveProgressStage(
                            stateResult.State.WorldMapType,
                            stateResult.State.WorldProgress)),
                    out var runtime))
            {
                battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                    WorldMapStateReader.WorldModule);
                DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.WorldMap);
                foreach (var context in worldMapRuntimes.Values)
                {
                    context.Footsteps.Reset();
                }

                worldMapNavigationBeaconPlayer?.StopAll();
                SuspendNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap);
                return;
            }

            var state = stateResult.State;
            var entityResult = worldMapEntityReader?.Read()
                ?? WorldMapEntityReadResult.Invalid("world entity reader is not initialized");
            runtime.UpdateEntities(entityResult.IsUsable
                ? entityResult.Entities
                : Array.Empty<WorldMapEntitySnapshot>());
            if (config.EnableWorldMapNavigationDiagnostics &&
                !string.Equals(entityResult.Diagnostic, lastWorldMapEntityDiagnostic, StringComparison.Ordinal))
            {
                lastWorldMapEntityDiagnostic = entityResult.Diagnostic;
                Log($"World-map entities: {entityResult.Diagnostic}.");
            }

            foreach (var context in worldMapRuntimes.Values)
            {
                if (!ReferenceEquals(context, runtime))
                {
                    context.Footsteps.Reset();
                    context.Navigation.Suspend("another native world map is active");
                }
            }

            if (!isForeground)
            {
                battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                    WorldMapStateReader.WorldModule);
                DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.WorldMap);
                runtime.Footsteps.Reset();
                worldMapNavigationBeaconPlayer?.StopAll();
                SuspendNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap);
                return;
            }

            if (config.EnableWorldMapFootstepFeedback && runtime.Footsteps.Observe(state, now))
            {
                PlayWorldMapFootstep("world movement", state);
            }

            if (config.EnableWorldMapNavigationDiagnostics &&
                !string.Equals(runtime.Footsteps.LastDiagnostic, lastWorldMapFootstepDiagnostic, StringComparison.Ordinal))
            {
                lastWorldMapFootstepDiagnostic = runtime.Footsteps.LastDiagnostic;
                Log($"World-map footsteps: {runtime.Footsteps.LastDiagnostic}.");
            }

            if (!config.EnableWorldMapNavigationAssistant)
            {
                battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                    WorldMapStateReader.WorldModule);
                DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.WorldMap);
                runtime.Navigation.Suspend("world navigation disabled");
                worldMapNavigationBeaconPlayer?.StopAll();
                StopNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap, announce: false);
                return;
            }

            var actions = ReadFieldNavigationActions(WorldMapStateReader.WorldModule).ToArray();
            if (actions.Any(IsNavigationSelectionAction))
            {
                StopNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap, announce: true);
            }

            foreach (var action in actions)
            {
                ProcessWorldMapNavigationOutput(runtime, runtime.Navigation.HandleAction(action, state, now));
            }

            if (TakeNavigationAutoWalkToggle(NavigationAutoWalkDomain.WorldMap))
            {
                ToggleWorldMapAutoWalk(runtime, state, now);
            }

            ProcessWorldMapNavigationOutput(runtime, runtime.Navigation.Observe(state, now));
            UpdateWorldMapAutoWalk(runtime, state);
            if (config.EnableWorldMapNavigationDiagnostics &&
                !string.Equals(runtime.Navigation.LastDiagnostic, lastWorldMapNavigationDiagnostic, StringComparison.Ordinal))
            {
                lastWorldMapNavigationDiagnostic = runtime.Navigation.LastDiagnostic;
                Log(
                    $"World-map navigation: map={state.WorldMapType}, category={runtime.Navigation.CurrentCategory}, " +
                    $"beacon={runtime.Navigation.BeaconEnabled}, {runtime.Navigation.LastDiagnostic}.");
            }
        }
        catch (Exception ex)
        {
            battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                WorldMapStateReader.WorldModule);
            DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.WorldMap);
            worldMapAccessibilityErrorCount++;
            worldMapNavigationBeaconPlayer?.StopAll();
            SuspendNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap);
            if (worldMapAccessibilityErrorCount <= 10)
            {
                Log($"World-map accessibility error: {ex.Message}");
            }
        }
    }

    private void ProcessWorldMapNavigationOutput(
        WorldMapRuntimeContext runtime,
        WorldMapNavigationOutput? output)
    {
        if (output is not { } value)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(value.Speech))
        {
            Log($"World-map navigation speech: {value.Speech}");
            Speak(value.Speech);
        }

        if (value.Beacon is { } beacon)
        {
            worldMapNavigationBeaconPlayer?.Play(beacon);
        }

        if (!runtime.Navigation.BeaconEnabled)
        {
            worldMapNavigationBeaconPlayer?.StopAll();
        }
    }

    private void ResetWorldMapAccessibility(string diagnostic)
    {
        foreach (var runtime in worldMapRuntimes.Values)
        {
            runtime.UpdateEntities(Array.Empty<WorldMapEntitySnapshot>());
            runtime.Footsteps.Reset();
            runtime.Navigation.Suspend(diagnostic);
        }

        worldMapNavigationBeaconPlayer?.StopAll();
        worldMapNavigationProgressSink?.Deactivate();
        worldMapWasActive = false;
        lastWorldMapFootstepDiagnostic = string.Empty;
        lastWorldMapNavigationDiagnostic = string.Empty;
        if (config.EnableWorldMapNavigationDiagnostics)
        {
            Log($"World-map accessibility reset: {diagnostic}.");
        }
    }

    private string? ResolveWorldMapDataPath(int worldMapType)
    {
        if (gameRootDirectory is null)
        {
            return null;
        }

        var fileName = $"wm{worldMapType}.map";
        var candidates = new[]
        {
            Path.Combine(gameRootDirectory, "ff7", "workingdir", "data", "wm", fileName),
            Path.Combine(gameRootDirectory, "data", "wm", fileName),
            Path.Combine(gameRootDirectory, "wm", fileName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private void TickFieldNavigationAssistant()
    {
        if (!config.EnableFieldNavigationAssistant)
        {
            battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                FieldPositionReader.FieldModule);
            DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.Field);
            StopNavigationAutoWalk(NavigationAutoWalkDomain.Field, announce: false);
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastFieldNavigationScanAt < TimeSpan.FromMilliseconds(Math.Max(30, config.FieldNavigationScanIntervalMs)) &&
            !battleStatusLimitKeyFrameRouter.HasNavigationPress(
                FieldPositionReader.FieldModule) &&
            !HasNavigationAutoWalkToggle(NavigationAutoWalkDomain.Field))
        {
            return;
        }

        lastFieldNavigationScanAt = now;
        try
        {
            var result = fieldPositionReader?.Read() ?? throw new InvalidOperationException("Field position reader is not initialized.");
            if (!result.IsUsable)
            {
                battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                    FieldPositionReader.FieldModule);
                DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.Field);
                fieldNavigationController.SuspendForPositionRecovery(result.Diagnostic);
                SuspendNavigationAutoWalk(NavigationAutoWalkDomain.Field);
                return;
            }

            var controlResult = fieldNavigationControlReader?.Read(result.Position)
                ?? new FieldNavigationControlReadResult(false, default, "control reader is not initialized");
            var input = fieldNavigationInputReader?.Read()
                ?? new FieldNavigationInputSnapshot(0, FieldNavigationInput.None);
            fieldNavigationCadence = input.IsDirectionalRun
                ? FieldFootstepCadence.Run
                : FieldFootstepCadence.Walk;
            var ladderResult = fieldLadderStateReader?.Read(result.Position)
                ?? FieldLadderStateReadResult.Invalid("ladder state reader is not initialized");
            var ladderState = ladderResult.IsUsable ? ladderResult.State : default;
            var exits = reachableFieldExitTargetProvider?.ReadTargets(result.Position) ?? [];
            var navigationSuppressed = FieldNavigationSuppressionPolicy.IsNavigationSuppressed(
                fieldAudibleCueState,
                ladderState,
                ladderResult.IsUsable);
            var navigationForeground = foregroundProcessGate.IsCurrentProcessForeground();
            if (navigationSuppressed || !navigationForeground || !controlResult.IsUsable)
            {
                battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                    FieldPositionReader.FieldModule);
                DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.Field);
                SuspendNavigationAutoWalk(NavigationAutoWalkDomain.Field);
            }

            if (config.EnableFieldNavigationDiagnostics)
            {
                var diagnostic = $"field={result.Position.FieldId}, category={fieldNavigationController.CurrentCategory}, beacon={fieldNavigationController.BeaconEnabled}";
                if (!string.Equals(diagnostic, lastFieldNavigationDiagnostic, StringComparison.Ordinal))
                {
                    lastFieldNavigationDiagnostic = diagnostic;
                    Log($"Field navigation state: {diagnostic}");
                }

                var exitsDiagnostic = exits.Count == 0
                    ? reachableFieldExitTargetProvider?.LastDiagnostic ?? $"field={result.Position.FieldId}: unavailable"
                    : $"{reachableFieldExitTargetProvider?.LastDiagnostic}: " + string.Join(
                        ", ",
                        exits.Select(target => $"{target.Label}@({target.X},{target.Y},{target.Z})"));
                if (!string.Equals(exitsDiagnostic, lastFieldNavigationExitsDiagnostic, StringComparison.Ordinal))
                {
                    lastFieldNavigationExitsDiagnostic = exitsDiagnostic;
                    Log($"Field navigation exits: {exitsDiagnostic}");
                }

                var objects = fieldNavigationObjectReader?.ReadTargets(result.Position) ?? [];
                var objectsDiagnostic = objects.Count == 0
                    ? $"field={result.Position.FieldId}: none"
                    : $"field={result.Position.FieldId}: " + string.Join(
                        ", ",
                        objects.Select(target => $"{target.Label}@({target.X},{target.Y},{target.Z})"));
                if (!string.Equals(objectsDiagnostic, lastFieldNavigationObjectsDiagnostic, StringComparison.Ordinal))
                {
                    lastFieldNavigationObjectsDiagnostic = objectsDiagnostic;
                    Log($"Field navigation objects: {objectsDiagnostic}");
                }

                var npcs = fieldNavigationNpcReader?.ReadTargets(result.Position) ?? [];
                var npcsDiagnostic = npcs.Count == 0
                    ? $"field={result.Position.FieldId}: none"
                    : $"field={result.Position.FieldId}: " + string.Join(
                        ", ",
                        npcs.Select(target => $"{target.Label}@({target.X},{target.Y},{target.Z})"));
                if (!string.Equals(npcsDiagnostic, lastFieldNavigationNpcsDiagnostic, StringComparison.Ordinal))
                {
                    lastFieldNavigationNpcsDiagnostic = npcsDiagnostic;
                    Log($"Field navigation NPCs: {npcsDiagnostic}");
                }

                var storyTargets = ReadFieldStoryTargets(result.Position);
                var storyDiagnostic = storyTargets.Count == 0
                    ? $"field={result.Position.FieldId}: none"
                    : $"field={result.Position.FieldId}: " + string.Join(
                        ", ",
                        storyTargets.Select(target => $"{target.Label}@({target.X},{target.Y},{target.Z})"));
                if (!string.Equals(storyDiagnostic, lastFieldNavigationStoryDiagnostic, StringComparison.Ordinal))
                {
                    lastFieldNavigationStoryDiagnostic = storyDiagnostic;
                    Log($"Field navigation story: {storyDiagnostic}");
                }

                var controlDiagnostic = controlResult.IsUsable
                    ? controlResult.Diagnostic
                    : $"unavailable: {controlResult.Diagnostic}";
                if (!string.Equals(controlDiagnostic, lastFieldNavigationControlDiagnostic, StringComparison.Ordinal))
                {
                    lastFieldNavigationControlDiagnostic = controlDiagnostic;
                    Log($"Field navigation control: {controlDiagnostic}");
                }

                var inputDiagnostic = $"raw=0x{input.RawStatus:X8}, direction={input.Direction}";
                if (!string.Equals(inputDiagnostic, lastFieldNavigationInputDiagnostic, StringComparison.Ordinal))
                {
                    lastFieldNavigationInputDiagnostic = inputDiagnostic;
                    Log($"Field navigation input: {inputDiagnostic}");
                }

                if (!string.Equals(ladderResult.Diagnostic, lastFieldLadderStateDiagnostic, StringComparison.Ordinal))
                {
                    lastFieldLadderStateDiagnostic = ladderResult.Diagnostic;
                    Log($"Field navigation ladder: {ladderResult.Diagnostic}");
                }
            }

            var liveTrackingSpeech = fieldNavigationController.UpdateLiveTracking(
                result.Position,
                input,
                controlResult.Transform,
                isSuppressed: navigationSuppressed || !controlResult.IsUsable,
                arrivalDistanceUnits: Math.Max(0, config.FieldNavigationArrivalDistanceUnits),
                ladderState: ladderState,
                observedAt: now);
            if (!navigationSuppressed && liveTrackingSpeech is not null)
            {
                fieldNavigationGuidanceRepeatGate.Reset();
                Log($"Field navigation live tracking: {liveTrackingSpeech.Value.Speech}");
                Speak(liveTrackingSpeech.Value.Speech);
                lastNavigationSpeechAt = now;
            }

            if (config.EnableFieldNavigationDiagnostics &&
                fieldNavigationController.CurrentRouteGuidance is { } guidance)
            {
                if (guidance.Replanned)
                {
                    Log($"Field navigation reroute: {guidance.Diagnostic}.");
                }

                var progressKey =
                    $"field={result.Position.FieldId},portal={guidance.PortalIndex}/{guidance.PortalCount}," +
                    $"waypoint={guidance.Waypoint.X},{guidance.Waypoint.Y},{guidance.Waypoint.Z}";
                if (!string.Equals(progressKey, lastFieldNavigationProgressDiagnostic, StringComparison.Ordinal))
                {
                    lastFieldNavigationProgressDiagnostic = progressKey;
                    Log(
                        $"Field navigation progress: {progressKey}, " +
                        $"remaining={guidance.RemainingDistance:0}, " +
                        $"progressRemaining={guidance.ProgressRemainingDistance:0}.");
                }

            }

            var actions = navigationSuppressed || !navigationForeground
                ? Array.Empty<FieldNavigationAction>()
                : ReadFieldNavigationActions(FieldPositionReader.FieldModule).ToArray();
            if (actions.Any(IsNavigationSelectionAction))
            {
                StopNavigationAutoWalk(NavigationAutoWalkDomain.Field, announce: true);
            }

            foreach (var action in actions)
            {
                var speech = fieldNavigationController.HandleAction(
                    action,
                    result.Position,
                    controlResult.IsUsable ? controlResult.Transform : null,
                    ladderState);
                if (speech is null)
                {
                    continue;
                }

                Log($"Field navigation speech: {speech.Value.Speech}");
                fieldNavigationGuidanceRepeatGate.Reset();
                Speak(speech.Value.Speech);
                lastNavigationSpeechAt = now;
            }

            if (!navigationSuppressed && navigationForeground && controlResult.IsUsable &&
                TakeNavigationAutoWalkToggle(NavigationAutoWalkDomain.Field))
            {
                ToggleFieldAutoWalk(
                    result.Position,
                    controlResult.Transform,
                    ladderState,
                    now);
            }

            UpdateFieldAutoWalk(
                result.Position,
                controlResult,
                navigationSuppressed || !navigationForeground,
                input.Direction);

            if (FieldNavigationSpeechPolicy.IsDue(
                    now,
                    lastNavigationSpeechAt,
                    config.FieldNavigationSpeechIntervalMs,
                    config.FieldNavigationRunningSpeechIntervalMs,
                    input.IsDirectionalRun,
                    navigationSuppressed,
                    foregroundProcessGate.IsCurrentProcessForeground(),
                    controlResult.IsUsable,
                    fieldNavigationController.BeaconEnabled))
            {
                if (fieldNavigationController.BeaconEnabled && fieldNavigationRoutePlanner is not null &&
                    !string.Equals(
                        lastFieldNavigationRouteDiagnostic,
                        fieldNavigationRoutePlanner.LastDiagnostic,
                        StringComparison.Ordinal))
                {
                    lastFieldNavigationRouteDiagnostic = fieldNavigationRoutePlanner.LastDiagnostic;
                    Log($"Field navigation GPS: {lastFieldNavigationRouteDiagnostic}");
                }

                var spokenGuidance = fieldNavigationController.CreateSpokenGuidance(
                    result.Position,
                    controlResult.Transform,
                    arrivalDistanceUnits: Math.Max(0, config.FieldNavigationArrivalDistanceUnits),
                    predictionHorizonMs: FieldNavigationSpeechPolicy.ResolveIntervalMs(
                        config.FieldNavigationSpeechIntervalMs,
                        config.FieldNavigationRunningSpeechIntervalMs,
                        input.IsDirectionalRun));
                if (spokenGuidance is not null &&
                    fieldNavigationGuidanceRepeatGate.ShouldSpeak(spokenGuidance.Value.Speech, now))
                {
                    lastNavigationSpeechAt = now;
                    Log($"Field navigation guidance: {spokenGuidance.Value.Speech}");
                    Speak(spokenGuidance.Value.Speech);
                }
            }
        }
        catch (Exception ex)
        {
            battleStatusLimitKeyFrameRouter.DiscardNavigationPress(
                FieldPositionReader.FieldModule);
            DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain.Field);
            SuspendNavigationAutoWalk(NavigationAutoWalkDomain.Field);
            fieldNavigationErrorCount++;
            if (fieldNavigationErrorCount <= 10)
            {
                Log($"Field navigation assistant error: {ex.Message}");
            }
        }
    }

    private IReadOnlyList<FieldNavigationTarget> ReadFieldStoryTargets(
        FieldPositionSnapshot position)
    {
        var ordinaryTargets = fieldStoryTargetReader?.ReadTargets(position) ??
            Array.Empty<FieldNavigationTarget>();
        return Floor60NavigationTargetMerger.Merge(
            ordinaryTargets,
            floor60SoldierTurnCueTracker.CurrentNavigationTarget);
    }

    private void TickFieldObjectProximityCues()
    {
        if (!config.EnableFieldObjectProximityCues || fieldObjectProximityCueTracker is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastFieldObjectCueScanAt < TimeSpan.FromMilliseconds(50))
        {
            return;
        }

        lastFieldObjectCueScanAt = now;
        try
        {
            if (fieldAudibleCueState.IsSuppressed)
            {
                fieldObjectProximityCueTracker.Reset();
                return;
            }

            var result = fieldPositionReader?.Read()
                ?? throw new InvalidOperationException("Field position reader is not initialized.");
            if (!result.IsUsable)
            {
                fieldObjectProximityCueTracker.Reset();
                return;
            }

            var control = fieldNavigationControlReader?.Read(result.Position)
                ?? new FieldNavigationControlReadResult(false, default, "control reader is not initialized");
            if (!control.IsUsable)
            {
                fieldObjectProximityCueTracker.Reset();
                return;
            }

            var targets = fieldNavigationObjectReader?.ReadTargets(result.Position) ?? [];
            var proximityCues = fieldObjectProximityCueTracker.Update(result.Position, targets, now);
            foreach (var proximityCue in proximityCues)
            {
                if (!fieldObjectCuePlayers.TryGetValue(proximityCue.Kind, out var player))
                {
                    continue;
                }

                var spatialCue = FieldObjectProximitySpatializer.CreateCue(
                    result.Position,
                    proximityCue.Target,
                    control.Transform);
                if (spatialCue is null)
                {
                    continue;
                }

                if (player.Play(spatialCue.Value, proximityCue.Gain))
                {
                    Log(
                        $"Field object cue played: kind={proximityCue.Kind}, target={proximityCue.Target.Label}, " +
                        $"position=({proximityCue.Target.X},{proximityCue.Target.Y},{proximityCue.Target.Z}), " +
                        $"distance={spatialCue.Value.DistanceUnits:0}, gain={proximityCue.Gain:0.000}, cluster={proximityCue.ClusterKey}.");
                }
            }
        }
        catch (Exception ex)
        {
            fieldObjectCueErrorCount++;
            if (fieldObjectCueErrorCount <= 10)
            {
                Log($"Field object proximity cue error: {ex.Message}");
            }
        }
    }

    private void TickFieldLadderProximityCues()
    {
        if (!config.EnableFieldLadderProximityCues ||
            fieldLadderProximityCueTracker is null ||
            fieldLadderCuePlayer is null ||
            fieldLadderMountCueTracker is null ||
            fieldLadderMountCuePlayer is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastFieldLadderCueScanAt < TimeSpan.FromMilliseconds(50))
        {
            return;
        }

        lastFieldLadderCueScanAt = now;
        try
        {
            if (fieldAudibleCueState.IsSuppressed)
            {
                ResetFieldLadderCues();
                return;
            }

            var result = fieldPositionReader?.Read()
                ?? throw new InvalidOperationException("Field position reader is not initialized.");
            if (!result.IsUsable)
            {
                ResetFieldLadderCues();
                return;
            }

            var ladderState = fieldLadderStateReader?.Read(result.Position);
            if (ladderState is { IsUsable: true, State.IsMounted: true })
            {
                ResetFieldLadderCues();
                return;
            }

            var control = fieldNavigationControlReader?.Read(result.Position)
                ?? new FieldNavigationControlReadResult(false, default, "control reader is not initialized");
            if (!control.IsUsable)
            {
                ResetFieldLadderCues();
                return;
            }

            var transitions = fieldNavigationTransitionProvider?.Invoke(result.Position.FieldId) ?? [];
            var prioritizedTransitionId = fieldNavigationController.PrioritizedLadderTransitionId;
            var wasAtMountEntrance = fieldLadderMountCueActive;
            var mountCues = fieldLadderMountCueTracker.Update(
                result.Position,
                transitions,
                now,
                prioritizedTransitionId);
            fieldLadderMountCueActive = fieldLadderMountCueTracker.IsAtEntrance;
            if (fieldLadderMountCueActive && !wasAtMountEntrance)
            {
                fieldLadderCuePlayer.StopAll();
            }
            else if (!fieldLadderMountCueActive && wasAtMountEntrance)
            {
                fieldLadderMountCuePlayer.StopAll();
            }

            var proximityCues = fieldLadderProximityCueTracker.Update(
                result.Position,
                transitions,
                now,
                prioritizedTransitionId);
            foreach (var proximityCue in proximityCues)
            {
                if (fieldLadderMountCueActive &&
                    string.Equals(
                        proximityCue.TargetKey,
                        prioritizedTransitionId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var transition = proximityCue.Transition;
                var target = new FieldNavigationTarget(
                    transition.FieldId,
                    FieldNavigationCategory.Objects,
                    "Ladder",
                    transition.SourceX,
                    transition.SourceY,
                    transition.SourceZ,
                    transition.StableId);
                var spatialCue = FieldProximitySpatializer.CreateCue(
                    result.Position,
                    target,
                    control.Transform);
                if (spatialCue is null)
                {
                    continue;
                }

                if (fieldLadderCuePlayer.Play(spatialCue.Value, proximityCue.Gain))
                {
                    Log(
                        $"Field ladder cue played: entity={transition.SourceEntityId}, " +
                        $"position=({transition.SourceX},{transition.SourceY},{transition.SourceZ}), " +
                        $"distance={spatialCue.Value.DistanceUnits:0}, gain={proximityCue.Gain:0.000}, " +
                        $"id={proximityCue.TargetKey}.");
                }
            }

            foreach (var mountCue in mountCues)
            {
                var transition = mountCue.Transition;
                var target = new FieldNavigationTarget(
                    transition.FieldId,
                    FieldNavigationCategory.Objects,
                    "Ladder",
                    transition.SourceX,
                    transition.SourceY,
                    transition.SourceZ,
                    transition.StableId);
                var spatialCue = FieldProximitySpatializer.CreateCue(
                    result.Position,
                    target,
                    control.Transform);
                if (spatialCue is null)
                {
                    continue;
                }

                if (fieldLadderMountCuePlayer.Play(spatialCue.Value, mountCue.Gain))
                {
                    Log(
                        $"Field ladder mount cue played: entity={transition.SourceEntityId}, " +
                        $"position=({transition.SourceX},{transition.SourceY},{transition.SourceZ}), " +
                        $"distance={spatialCue.Value.DistanceUnits:0}, gain={mountCue.Gain:0.000}, " +
                        $"id={mountCue.TargetKey}.");
                }
            }
        }
        catch (Exception ex)
        {
            fieldLadderCueErrorCount++;
            if (fieldLadderCueErrorCount <= 10)
            {
                Log($"Field ladder proximity cue error: {ex.Message}");
            }
        }
    }

    private void ResetFieldLadderCues()
    {
        fieldLadderProximityCueTracker?.Reset();
        fieldLadderMountCueTracker?.Reset();
        fieldLadderCuePlayer?.StopAll();
        fieldLadderMountCuePlayer?.StopAll();
        fieldLadderMountCueActive = false;
    }

    private void TickFieldExitProximityCues()
    {
        if (!config.EnableFieldExitProximityCues ||
            fieldExitProximityCueTracker is null ||
            fieldExitCuePlayer is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastFieldExitCueScanAt < TimeSpan.FromMilliseconds(50))
        {
            return;
        }

        lastFieldExitCueScanAt = now;
        try
        {
            if (fieldAudibleCueState.IsSuppressed)
            {
                fieldExitProximityCueTracker.Reset();
                fieldExitCuePlayer.StopAll();
                return;
            }

            var result = fieldPositionReader?.Read()
                ?? throw new InvalidOperationException("Field position reader is not initialized.");
            if (!result.IsUsable)
            {
                fieldExitProximityCueTracker.Reset();
                fieldExitCuePlayer.StopAll();
                return;
            }

            var control = fieldNavigationControlReader?.Read(result.Position)
                ?? new FieldNavigationControlReadResult(false, default, "control reader is not initialized");
            if (!control.IsUsable)
            {
                fieldExitProximityCueTracker.Reset();
                fieldExitCuePlayer.StopAll();
                return;
            }

            var targets = reachableFieldExitTargetProvider?.ReadTargets(result.Position) ?? [];
            var proximityCues = fieldExitProximityCueTracker.Update(result.Position, targets, now);
            if (!fieldExitProximityCueTracker.HasAudibleTargets)
            {
                fieldExitCuePlayer.StopAll();
            }

            foreach (var proximityCue in proximityCues)
            {
                var spatialCue = FieldProximitySpatializer.CreateCue(
                    result.Position,
                    proximityCue.Target,
                    control.Transform);
                if (spatialCue is null)
                {
                    continue;
                }

                if (fieldExitCuePlayer.Play(spatialCue.Value, proximityCue.Gain))
                {
                    Log(
                        $"Field exit cue played: target={proximityCue.Target.Label}, " +
                        $"position=({proximityCue.Target.X},{proximityCue.Target.Y},{proximityCue.Target.Z}), " +
                        $"distance={spatialCue.Value.DistanceUnits:0}, gain={proximityCue.Gain:0.000}, " +
                        $"id={proximityCue.TargetKey}.");
                }
            }
        }
        catch (Exception ex)
        {
            fieldExitCueErrorCount++;
            if (fieldExitCueErrorCount <= 10)
            {
                Log($"Field exit proximity cue error: {ex.Message}");
            }
        }
    }

    private void TickFieldAudibleCueState()
    {
        var state = fieldAudibleCueStateReader?.Read()
            ?? new FieldAudibleCueState(true, "state reader unavailable", 0, 0, 0, 0);
        var diagnostic =
            $"suppressed={state.IsSuppressed}, reason={state.Reason}, module={state.Module}, " +
            $"control={state.UserControl}, messages={state.ActiveMessageCount}, movie={state.MovieActive}";
        if (!string.Equals(diagnostic, lastFieldAudibleCueStateDiagnostic, StringComparison.Ordinal))
        {
            lastFieldAudibleCueStateDiagnostic = diagnostic;
            Log($"Field audible cue state: {diagnostic}.");
        }

        var suppressionStarted = state.IsSuppressed && !fieldAudibleCueState.IsSuppressed;
        fieldAudibleCueState = state;
        if (!suppressionStarted)
        {
            return;
        }

        foreach (var player in fieldObjectCuePlayers.Values)
        {
            player.StopAll();
        }

        fieldExitCuePlayer?.StopAll();
        fieldLadderCuePlayer?.StopAll();
        fieldLadderMountCuePlayer?.StopAll();
        fieldObjectProximityCueTracker?.Reset();
        fieldExitProximityCueTracker?.Reset();
        fieldLadderProximityCueTracker?.Reset();
        fieldLadderMountCueTracker?.Reset();
        fieldLadderMountCueActive = false;
        Log($"Stopped active field object, ladder, and exit cues and suppressed navigation speech: {state.Reason}.");
    }

    private void TickNavigationProgressControls()
    {
        var isForeground = foregroundProcessGate.IsCurrentProcessForeground();
        foreach (var action in NavigationProgressHotkeyRouter.ReadActions(
                     virtualKey => WasNavigationKeyPressed(virtualKey, isForeground)))
        {
            var speech = navigationProgressController.HandleAction(action);
            Log($"Navigation progress control: {speech}");
            Speak(speech, interrupt: true);
        }
    }

    private void TickNavigationAutoWalkToggleInput()
    {
        var module = ReadByte(FieldPositionReader.AddressCurrentModule);
        var foreground = foregroundProcessGate.IsCurrentProcessForeground();
        var domain = module switch
        {
            FieldPositionReader.FieldModule when config.EnableFieldNavigationAssistant =>
                NavigationAutoWalkDomain.Field,
            WorldMapStateReader.WorldModule when config.EnableWorldMapNavigationAssistant =>
                NavigationAutoWalkDomain.WorldMap,
            _ => NavigationAutoWalkDomain.None
        };
        var togglePressed = NavigationAutoWalkKeyRouter.ObserveToggle(
            virtualKey => WasNavigationKeyPressed(
                virtualKey,
                foreground && domain != NavigationAutoWalkDomain.None));

        if (!foreground)
        {
            pendingNavigationAutoWalkToggle = NavigationAutoWalkDomain.None;
            navigationAutoWalkController?.Suspend();
            return;
        }

        if (domain == NavigationAutoWalkDomain.None)
        {
            pendingNavigationAutoWalkToggle = NavigationAutoWalkDomain.None;
            navigationAutoWalkController?.Suspend();
            return;
        }

        if (domain == NavigationAutoWalkDomain.Field)
        {
            StopNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap, announce: false);
        }
        else
        {
            StopNavigationAutoWalk(NavigationAutoWalkDomain.Field, announce: false);
        }

        if (togglePressed)
        {
            pendingNavigationAutoWalkToggle = domain;
        }
    }

    private bool HasNavigationAutoWalkToggle(NavigationAutoWalkDomain domain) =>
        pendingNavigationAutoWalkToggle == domain;

    private bool TakeNavigationAutoWalkToggle(NavigationAutoWalkDomain domain)
    {
        if (pendingNavigationAutoWalkToggle != domain)
        {
            return false;
        }

        pendingNavigationAutoWalkToggle = NavigationAutoWalkDomain.None;
        return true;
    }

    private void DiscardNavigationAutoWalkToggle(NavigationAutoWalkDomain domain)
    {
        if (pendingNavigationAutoWalkToggle == domain)
        {
            pendingNavigationAutoWalkToggle = NavigationAutoWalkDomain.None;
        }
    }

    private void ToggleFieldAutoWalk(
        FieldPositionSnapshot position,
        FieldNavigationControlTransform controlTransform,
        FieldLadderStateSnapshot ladderState,
        DateTime now)
    {
        if (StopNavigationAutoWalk(NavigationAutoWalkDomain.Field, announce: true))
        {
            return;
        }

        if (!fieldNavigationController.BeaconEnabled)
        {
            var speech = fieldNavigationController.HandleAction(
                FieldNavigationAction.ToggleBeacon,
                position,
                controlTransform,
                ladderState);
            if (speech is { } value)
            {
                Log($"Field navigation speech: {value.Speech}");
                fieldNavigationGuidanceRepeatGate.Reset();
                Speak(value.Speech);
                lastNavigationSpeechAt = now;
            }
        }

        if (fieldNavigationController.BeaconEnabled &&
            navigationAutoWalkController?.TryStart(
                NavigationAutoWalkDomain.Field,
                routeActive: true) == true)
        {
            Log("Field navigation auto walk started for the selected target.");
            Speak("Auto walk on.", interrupt: true);
        }
    }

    private void ToggleWorldMapAutoWalk(
        WorldMapRuntimeContext runtime,
        WorldMapStateSnapshot state,
        DateTime now)
    {
        if (StopNavigationAutoWalk(NavigationAutoWalkDomain.WorldMap, announce: true))
        {
            return;
        }

        if (!runtime.Navigation.BeaconEnabled)
        {
            ProcessWorldMapNavigationOutput(
                runtime,
                runtime.Navigation.HandleAction(FieldNavigationAction.ToggleBeacon, state, now));
        }

        if (runtime.Navigation.BeaconEnabled &&
            navigationAutoWalkController?.TryStart(
                NavigationAutoWalkDomain.WorldMap,
                routeActive: true) == true)
        {
            Log("World-map navigation auto walk started for the selected target.");
            Speak("Auto walk on.", interrupt: true);
        }
    }

    private void UpdateFieldAutoWalk(
        FieldPositionSnapshot position,
        FieldNavigationControlReadResult control,
        bool movementSuppressed,
        FieldNavigationInput observedInput)
    {
        if (navigationAutoWalkController?.IsEnabledFor(NavigationAutoWalkDomain.Field) != true)
        {
            return;
        }

        var direction = FieldNavigationInput.None;
        var hasDirection = control.IsUsable &&
            fieldNavigationController.TryResolveAutomaticInput(
                position,
                control.Transform,
                Math.Max(0, config.FieldNavigationArrivalDistanceUnits),
                out direction);
        var result = navigationAutoWalkController.Drive(
            hasDirection ? direction : FieldNavigationInput.None,
            canMove: hasDirection && !movementSuppressed,
            routeActive: fieldNavigationController.BeaconEnabled,
            observedInput: observedInput);
        HandleNavigationAutoWalkInputResult(result, NavigationAutoWalkDomain.Field);
    }

    private void UpdateWorldMapAutoWalk(
        WorldMapRuntimeContext runtime,
        WorldMapStateSnapshot state)
    {
        if (navigationAutoWalkController?.IsEnabledFor(NavigationAutoWalkDomain.WorldMap) != true)
        {
            return;
        }

        var hasDirection = runtime.Navigation.TryResolveAutomaticInput(state, out var direction);
        var result = navigationAutoWalkController.Drive(
            hasDirection ? direction : FieldNavigationInput.None,
            canMove: hasDirection,
            routeActive: runtime.Navigation.BeaconEnabled);
        HandleNavigationAutoWalkInputResult(result, NavigationAutoWalkDomain.WorldMap);
    }

    private void HandleNavigationAutoWalkInputResult(
        HighwayAutoSteeringInputResult result,
        NavigationAutoWalkDomain domain)
    {
        if (result.Success)
        {
            lastNavigationAutoWalkFailure = string.Empty;
            return;
        }

        if (string.Equals(lastNavigationAutoWalkFailure, result.Diagnostic, StringComparison.Ordinal))
        {
            return;
        }

        lastNavigationAutoWalkFailure = result.Diagnostic;
        Log($"{domain} auto walk stopped: {result.Diagnostic}");
        Speak("Auto walk stopped. Directional input is unavailable.", interrupt: true);
    }

    private bool StopNavigationAutoWalk(
        NavigationAutoWalkDomain domain,
        bool announce)
    {
        if (navigationAutoWalkController?.IsEnabledFor(domain) != true)
        {
            return false;
        }

        navigationAutoWalkController.Stop();
        lastNavigationAutoWalkFailure = string.Empty;
        Log($"{domain} auto walk stopped.");
        if (announce)
        {
            Speak("Auto walk off.", interrupt: true);
        }

        return true;
    }

    private void SuspendNavigationAutoWalk(NavigationAutoWalkDomain domain)
    {
        if (navigationAutoWalkController?.IsEnabledFor(domain) == true)
        {
            navigationAutoWalkController.Suspend();
        }
    }

    private static bool IsNavigationSelectionAction(FieldNavigationAction action) =>
        action is FieldNavigationAction.PreviousCategory or
            FieldNavigationAction.NextCategory or
            FieldNavigationAction.PreviousTarget or
            FieldNavigationAction.NextTarget;

    private void TickHighwayAccessibility()
    {
        if (highwayAccessibilityCoordinator is null)
        {
            return;
        }

        var module = ReadByte(HighwayStateReader.AddressCurrentModule);
        var isHighway = module == HighwayStateReader.HighwayModule;
        var isForeground = foregroundProcessGate.IsCurrentProcessForeground();
        var statusRequested =
            isHighway &&
            WasNavigationKeyPressed(VirtualKeyK, isForeground);
        var autoSteeringToggleRequested =
            WasNavigationKeyPressed(VirtualKeyF8, isHighway && isForeground);
        highwayAccessibilityCoordinator.Update(
            DateTime.UtcNow,
            isHighway,
            isForeground,
            statusRequested,
            autoSteeringToggleRequested);
    }

    private IEnumerable<FieldNavigationAction> ReadFieldNavigationActions(int ownerModule)
    {
        var isForeground = foregroundProcessGate.IsCurrentProcessForeground();
        if (WasNavigationKeyPressed(VirtualKeyU, isForeground))
        {
            yield return FieldNavigationAction.PreviousCategory;
        }

        if (WasNavigationKeyPressed(VirtualKeyO, isForeground))
        {
            yield return FieldNavigationAction.NextCategory;
        }

        if (WasNavigationKeyPressed(VirtualKeyJ, isForeground))
        {
            yield return FieldNavigationAction.PreviousTarget;
        }

        if (battleStatusLimitKeyFrameRouter.TakeNavigationPress(ownerModule))
        {
            yield return FieldNavigationAction.NextTarget;
        }

        if (WasNavigationKeyPressed(VirtualKeyK, isForeground))
        {
            yield return FieldNavigationAction.RepeatTarget;
        }

        if (WasNavigationKeyPressed(VirtualKeyI, isForeground))
        {
            yield return FieldNavigationAction.ToggleBeacon;
        }
    }

    private bool WasNavigationKeyPressed(int virtualKey, bool isForeground)
    {
        var isDown = (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        return navigationKeyPressTracker.Observe(virtualKey, isDown, isForeground);
    }

    private static uint GetForegroundWindowProcessId(nint window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    private bool ReadFieldRunState()
    {
        if (fieldRunStateReader is null)
        {
            const string unavailableDiagnostic = "unavailable; using walk cadence; reader is not initialized";
            if (!string.Equals(unavailableDiagnostic, lastFieldRunStateDiagnostic, StringComparison.Ordinal))
            {
                lastFieldRunStateDiagnostic = unavailableDiagnostic;
                Log($"Field run state: {unavailableDiagnostic}");
            }

            return false;
        }

        var available = fieldRunStateReader.TryRead(out var result);
        var diagnostic = available
            ? result.Diagnostic
            : $"unavailable; using walk cadence; {result.Diagnostic}";
        if (!string.Equals(diagnostic, lastFieldRunStateDiagnostic, StringComparison.Ordinal))
        {
            lastFieldRunStateDiagnostic = diagnostic;
            Log($"Field run state: {diagnostic}");
        }

        return available && result.IsRunning;
    }

    private void PlayFootstep(string reason, FieldPositionSnapshot? position)
    {
        if (footstepSoundPlayer is null)
        {
            return;
        }

        if (config.UseCosmoFootstepSounds)
        {
            if (cosmoFootstepSequencer is null)
            {
                LogSuppressedFootstep("cosmo-unavailable", "Footstep suppressed: Cosmo footstep sequencer is unavailable.");
                return;
            }

            CosmoFootstepSelection selection;
            var selected = position.HasValue
                ? cosmoFootstepSequencer.TrySelectNext(position.Value, out selection)
                : cosmoFootstepSequencer.TrySelectProbe(out selection);

            if (!selected)
            {
                var key = position.HasValue
                    ? $"no-cosmo:{position.Value.FieldId}:{position.Value.TriangleId}"
                    : "no-cosmo:probe";
                LogSuppressedFootstep(key, "Footstep suppressed: no explicit Cosmo footstep mapping.");
                return;
            }

            if (selected)
            {
                if (selection.IsSilent)
                {
                    LogSuppressedFootstep($"silent:{selection.TrackName}", $"Footstep suppressed by Cosmo track: {selection.TrackName}");
                    return;
                }

                var cosmoReason = $"{reason}; cosmo={selection.TrackName}/{selection.SoundId}";
                footstepSoundPlayer.Play(cosmoReason, selection.Path);
                return;
            }
        }

        footstepSoundPlayer.Play(reason);
    }

    private void PlayWorldMapFootstep(string reason, WorldMapStateSnapshot position)
    {
        if (footstepSoundPlayer is null)
        {
            return;
        }

        if (!config.UseCosmoFootstepSounds || cosmoFootstepSequencer is null)
        {
            LogSuppressedFootstep(
                "world-cosmo-unavailable",
                "World-map footstep suppressed: native world footsteps require the Cosmo Memory mapping.");
            return;
        }

        if (!cosmoFootstepSequencer.TrySelectNext(position, out var selection))
        {
            LogSuppressedFootstep(
                $"no-world-cosmo:{position.WorldMapType}:{position.PlayerModelId}:{position.TerrainId}",
                $"World-map footstep suppressed: no explicit Cosmo mapping for model {position.PlayerModelId}, terrain {position.TerrainId}.");
            return;
        }

        if (selection.IsSilent)
        {
            LogSuppressedFootstep(
                $"silent:{selection.TrackName}",
                $"World-map footstep suppressed by Cosmo track: {selection.TrackName}");
            return;
        }

        lastSuppressedFootstepKey = string.Empty;
        footstepSoundPlayer.Play(
            $"{reason}; map={position.WorldMapType}; model={position.PlayerModelId}; " +
            $"terrain={position.TerrainId}; cosmo={selection.TrackName}/{selection.SoundId}",
            selection.Path);
    }

    private void LogSuppressedFootstep(string key, string message)
    {
        if (string.Equals(key, lastSuppressedFootstepKey, StringComparison.Ordinal))
        {
            return;
        }

        lastSuppressedFootstepKey = key;
        Log(message);
    }

    private void TickSaveMenuSpeech()
    {
        if (!config.EnableInGameMenuWidgetSpeech)
        {
            saveMenuSpeechTracker.Reset();
            return;
        }

        var currentModule = ReadByte(FieldPositionReader.AddressCurrentModule);
        var isForeground = foregroundProcessGate.IsCurrentProcessForeground();
        saveMenuSpeechTracker.ObserveHostState(
            currentModule,
            isForeground,
            IsNameEntryMenuActive());
        if (!SaveMenuSpeechTracker.IsSupportedHostModule(currentModule) ||
            !isForeground)
        {
            return;
        }

        if (!saveMenuSpeechTracker.IsActive)
        {
            return;
        }

        // Native save state has exclusive ownership of these screens. A torn nested
        // read keeps that ownership but deliberately emits nothing until a checked
        // snapshot is available again.
        DiscardCompetingSaveMenuSpeech();
        if (saveMenuStateReader is null)
        {
            return;
        }

        if (!saveMenuStateReader.TryReadForActiveSaveWidget(
                out var snapshot,
                out var diagnostic))
        {
            if (config.EnableMenuWidgetDiagnostics &&
                !string.Equals(
                    diagnostic,
                    lastSaveMenuStateDiagnostic,
                    StringComparison.Ordinal))
            {
                lastSaveMenuStateDiagnostic = diagnostic;
                Log($"Native save menu state unavailable: {diagnostic}");
            }

            return;
        }

        lastSaveMenuStateDiagnostic = diagnostic;
        var now = DateTime.UtcNow;
        saveMenuSpeechTracker.Observe(snapshot, now);
        if (saveMenuSpeechTracker.Peek(now) is not { } pending)
        {
            return;
        }

        Log(
            $"Native save menu speech: page={snapshot.Page}, file={snapshot.SaveFileNumber}, " +
            $"game={snapshot.GameNumber}, confirmation={snapshot.ConfirmationCursor}, text={pending.Text}");
        if (Speak(pending.Text))
        {
            saveMenuSpeechTracker.Acknowledge(pending.Id);
        }
    }

    private void DiscardCompetingSaveMenuSpeech()
    {
        activeMenuFrameSpeechCoordinator.DiscardPending();
        renderedMenuTextSpeechTracker.DiscardPending();
        staticMenuCursorSpeechTracker.DiscardPending();
        statusMenuSpeechTracker.DiscardPending();
    }

    private void TickMainMenuReader()
    {
        if (!config.EnableMainMenuReader)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (saveMenuSpeechTracker.IsActive)
        {
            mainMenuSpeechScheduler.Observe(string.Empty, now);
            return;
        }

        if (now - lastMainMenuScanAt < TimeSpan.FromMilliseconds(Math.Max(50, config.MainMenuScanIntervalMs)))
        {
            return;
        }

        lastMainMenuScanAt = now;
        try
        {
            if (mainMenuStateReader?.TryReadSnapshot(out var snapshot) != true)
            {
                if (lastMainMenuSelectionText.Length != 0)
                {
                    Log("Main menu selection cleared: native snapshot unavailable.");
                    lastMainMenuSelectionText = string.Empty;
                }

                mainMenuSpeechScheduler.Observe(string.Empty, now);
                return;
            }

            if (!MainMenuStateReader.TryCreateSelection(snapshot, out var selection))
            {
                if (lastMainMenuSelectionText.Length != 0)
                {
                    Log("Main menu selection cleared.");
                    lastMainMenuSelectionText = string.Empty;
                }

                mainMenuSpeechScheduler.Observe(string.Empty, now);
                return;
            }

            var diagnosticText = selection.IsAvailable ? selection.Label : $"{selection.Label} unavailable";
            if (!string.Equals(diagnosticText, lastMainMenuSelectionText, StringComparison.Ordinal))
            {
                lastMainMenuSelectionText = diagnosticText;
                Log(
                    $"Main menu selection: {diagnosticText} " +
                    $"(index={selection.Index}, state={snapshot.State}, selectedA={snapshot.SelectedA}, selectedB={snapshot.SelectedB}, cursor={snapshot.CursorIndex}, " +
                    $"target={snapshot.Target}, open=0x{snapshot.MenuOpen:X8}, enabled=0x{snapshot.EnabledMask:X8}, disabled=0x{snapshot.DisabledMask:X8}, anim={snapshot.Animation})");
            }

            if (config.SpeakMainMenuSelections)
            {
                var speech = mainMenuSpeechScheduler.Observe(selection.SpokenText, now);
                if (speech is not null)
                {
                    Speak(speech);
                }
            }
        }
        catch (Exception ex)
        {
            mainMenuReaderErrorCount++;
            if (mainMenuReaderErrorCount <= 10)
            {
                Log($"Main menu reader error: {ex.Message}");
            }
        }
    }

    private unsafe void TickMenuWidgetDiagnostics()
    {
        if (!config.EnableMenuWidgetDiagnostics)
        {
            return;
        }

        try
        {
            foreach (var probe in MenuWidgetCatalog.All)
            {
                var probeAddress = checked((int)probe.Address);
                var first = ReadInt32(probeAddress);
                var cursor = ReadInt32(probeAddress + 4);
                var columns = ReadInt32(probeAddress + 8);
                var rows = ReadInt32(probeAddress + 12);
                if (columns <= 0 || rows <= 0 || columns > 16 || rows > 400 || cursor < 0 || cursor >= columns * rows)
                {
                    continue;
                }

                var nativeInventoryItem = TryReadNativeInventoryItem(probe.Name, first, cursor);
                var nativeSelection = TryReadNativeMenuSelection(probe, cursor);
                var state =
                    $"cursor={cursor}, grid={columns}x{rows}, first={first}, " +
                    $"f10={ReadInt32(probeAddress + 0x10)}, f14={ReadInt32(probeAddress + 0x14)}, f18={ReadInt32(probeAddress + 0x18)}" +
                    FormatNativeInventoryDiagnostic(nativeInventoryItem) +
                    FormatNativeSelectionDiagnostic(nativeSelection);
                if (lastMenuWidgetDiagnosticStates.TryGetValue(probe.Address, out var previous) &&
                    string.Equals(previous, state, StringComparison.Ordinal))
                {
                    continue;
                }

                lastMenuWidgetDiagnosticStates[probe.Address] = state;
                if (config.EnableMenuWidgetDiagnostics)
                {
                    Log($"Menu widget: {probe.Name} base=0x{probe.Address:X8} {state}");
                }

            }
        }
        catch (Exception ex)
        {
            Log($"Menu widget diagnostics error: {ex.Message}");
        }
    }

    private NativeMenuSelection? TryReadNativeMenuSelection(MenuWidgetDescriptor probe, int cursor)
    {
        try
        {
            if (probe.Kind == MenuWidgetKind.EquipmentList &&
                equipmentMenuSelectionReader?.TryRead(out var equipmentCandidate) == true)
            {
                return equipmentCandidate;
            }

            if (probe.Kind is MenuWidgetKind.MateriaSlot or MenuWidgetKind.MateriaList &&
                materiaMenuSelectionReader?.TryRead(probe.Kind, out var materia) == true)
            {
                return materia;
            }

            if (savemapPartyReader is null)
            {
                return null;
            }

            if (probe.Kind == MenuWidgetKind.CharacterList &&
                orderMenuSelectionReader?.TryRead(probe.Address, cursor, out var order) == true)
            {
                return order;
            }

            if (probe.Kind == MenuWidgetKind.CharacterList &&
                savemapPartyReader.TryReadPartySlot(cursor, out var partyMember))
            {
                return new NativeMenuSelection(
                    partyMember.Name,
                    null,
                    $"party:{probe.Address:X8}:{cursor}:{partyMember.CharacterId}:{partyMember.Name}");
            }

            if (probe.Kind == MenuWidgetKind.EquipmentSlot &&
                savemapPartyReader.TryReadSelectedEquipment(cursor, out var equipment))
            {
                return equipment;
            }
        }
        catch (Exception ex)
        {
            Log($"Native menu selection read error: {ex.Message}");
        }

        return null;
    }

    private InventoryItemSnapshot? TryReadNativeInventoryItem(string probeName, int first, int cursor)
    {
        if (!string.Equals(probeName, "Item list", StringComparison.Ordinal) ||
            inventoryItemReader is null)
        {
            return null;
        }

        try
        {
            return inventoryItemReader.TryRead(first + cursor, out var snapshot)
                ? snapshot
                : null;
        }
        catch (Exception ex)
        {
            Log($"Native inventory item read error: {ex.Message}");
            return null;
        }
    }

    private ActiveMenuWidgetSnapshot EnrichActiveMenuWidgetSnapshot(ActiveMenuWidgetSnapshot snapshot)
    {
        try
        {
            if (snapshot.Kind == MenuWidgetKind.ItemList && inventoryItemReader is not null)
            {
                var slot = snapshot.First +
                    snapshot.Cursor * snapshot.Columns +
                    snapshot.ScrollOffset * snapshot.Columns;
                return inventoryItemReader.TryRead(slot, out var item)
                    ? snapshot with { InventoryItem = item }
                    : snapshot;
            }

            if (snapshot.Kind == MenuWidgetKind.MagicList &&
                magicMenuSelectionReader?.TryRead(snapshot, out var spell) == true)
            {
                return snapshot with { MagicSpell = spell };
            }

            if (snapshot.Kind == MenuWidgetKind.ConfigSoundVolume &&
                configMenuValueReader?.ReadSoundVolume(snapshot.Cursor) is { } soundVolume)
            {
                return snapshot with { NativeSelection = soundVolume };
            }

            if (snapshot.Kind == MenuWidgetKind.CharacterList &&
                orderMenuSelectionReader?.TryRead(snapshot.Address, snapshot.Cursor, out var order) == true)
            {
                return snapshot with { NativeSelection = order };
            }

            if (snapshot.Kind == MenuWidgetKind.CharacterList &&
                savemapPartyReader?.TryReadPartySlot(snapshot.Cursor, out var partyMember) == true)
            {
                return snapshot with
                {
                    NativeSelection = new NativeMenuSelection(
                        partyMember.Name,
                        null,
                        $"party:{snapshot.Address:X8}:{snapshot.Cursor}:{partyMember.CharacterId}:{partyMember.Name}")
                };
            }

            if (snapshot.Kind is MenuWidgetKind.ItemTarget or MenuWidgetKind.MagicTarget &&
                savemapPartyReader?.TryReadStatusSummary(snapshot.Cursor, out var target) == true &&
                target.PartySlot == snapshot.Cursor)
            {
                return snapshot with
                {
                    NativeSelection = PartyTargetMenuSelectionFormatter.Create(
                        target,
                        unchecked((uint)snapshot.Address),
                        snapshot.Cursor)
                };
            }

            if (snapshot.Kind == MenuWidgetKind.EquipmentSlot &&
                savemapPartyReader?.TryReadSelectedEquipment(snapshot.Cursor, out var equipment) == true)
            {
                return snapshot with { NativeSelection = equipment };
            }

            if (snapshot.Kind == MenuWidgetKind.EquipmentList &&
                equipmentMenuSelectionReader?.TryRead(out var equipmentCandidate) == true)
            {
                return snapshot with { NativeSelection = equipmentCandidate };
            }

            if (snapshot.Kind is MenuWidgetKind.MateriaSlot or MenuWidgetKind.MateriaList &&
                materiaMenuSelectionReader?.TryRead(snapshot.Kind, out var materia) == true)
            {
                return snapshot with { NativeSelection = materia };
            }
        }
        catch (Exception ex)
        {
            Log($"Native active menu enrichment error: {ex.Message}");
        }

        return snapshot;
    }

    private static string FormatNativeInventoryDiagnostic(InventoryItemSnapshot? item)
    {
        if (item is not { } snapshot)
        {
            return string.Empty;
        }

        var name = string.IsNullOrWhiteSpace(snapshot.Name) ? "<unnamed>" : snapshot.Name;
        var description = string.IsNullOrWhiteSpace(snapshot.Description) ? string.Empty : $",desc:{snapshot.Description}";
        return $", nativeItem=slot:{snapshot.Slot},id:{snapshot.ItemId},qty:{snapshot.Quantity},raw:0x{snapshot.Raw:X4},name:{name}{description}";
    }

    private static string FormatNativeSelectionDiagnostic(NativeMenuSelection? selection)
    {
        if (selection is not { } nativeSelection)
        {
            return string.Empty;
        }

        var text = string.IsNullOrWhiteSpace(nativeSelection.Text) ? "<empty>" : nativeSelection.Text;
        return $", nativeSelection=key:{nativeSelection.Key},text:{text}";
    }

    private void TickInGameMenuSpeech()
    {
        if (!ShouldObserveInGameMenuDraws())
        {
            partyFormationSpeechTracker.Reset();
            return;
        }

        if (shopMenuStateReader is not null &&
            shopMenuStateReader.TryReadOwnership(out var ownsShop) &&
            ownsShop)
        {
            partyFormationSpeechTracker.Reset();
            activeMenuFrameSpeechCoordinator.DiscardPending();
            staticMenuCursorSpeechTracker.DiscardPending();
            statusMenuSpeechTracker.DiscardPending();
            return;
        }

        if (saveMenuSpeechTracker.IsActive)
        {
            partyFormationSpeechTracker.Reset();
            DiscardCompetingSaveMenuSpeech();
            return;
        }

        if (IsNameEntryMenuActive())
        {
            partyFormationSpeechTracker.Reset();
            activeMenuFrameSpeechCoordinator.DiscardPending();
            return;
        }

        var now = DateTime.UtcNow;
        staticMenuCursorSpeechTracker.ObserveConfigRow(
            ReadInt32(ConfigMenuValueReader.AddressCurrentRow),
            now);
        if (partyFormationSpeechTracker.IsActive(now))
        {
            activeMenuFrameSpeechCoordinator.DiscardPending();
            staticMenuCursorSpeechTracker.DiscardPending();
            statusMenuSpeechTracker.DiscardPending();
            var formationSpeech = partyFormationSpeechTracker.Poll(now);
            if (formationSpeech is not null)
            {
                Log($"In-game menu speech (Reform): {formationSpeech}");
                Speak(formationSpeech);
            }

            return;
        }

        if (materiaTutorialSpeechTracker.IsActive(now))
        {
            activeMenuFrameSpeechCoordinator.DiscardPending();
            var tutorialSpeech = materiaTutorialSpeechTracker.Poll(now);
            if (tutorialSpeech is not null)
            {
                Log($"Materia tutorial speech: {tutorialSpeech}");
                Speak(tutorialSpeech, false);
            }

            return;
        }

        var source = "active widget";
        var speech = activeMenuFrameSpeechCoordinator.Poll();
        if (speech is null)
        {
            source = "native static cursor";
            speech = staticMenuCursorSpeechTracker.Poll(now, ReadCurrentConfigValue);
        }

        if (speech is null)
        {
            source = "native Status summary";
            speech = statusMenuSpeechTracker.Poll(now, ReadCurrentStatusSnapshot);
        }

        if (speech is null)
        {
            return;
        }

        Log($"In-game menu speech ({source}): {speech}");
        Speak(speech);
    }

    private void TickShopMenuSpeech()
    {
        if (!ShouldObserveInGameMenuDraws() ||
            !foregroundProcessGate.IsCurrentProcessForeground() ||
            shopMenuStateReader is null)
        {
            shopMenuSpeechTracker.Reset();
            return;
        }

        try
        {
            var speech = shopMenuSpeechTracker.Poll(shopMenuStateReader);
            if (speech is null)
            {
                return;
            }

            Log($"Shop menu speech: {speech}");
            if (!Speak(speech))
            {
                shopMenuSpeechTracker.Reset();
            }
        }
        catch (Exception ex)
        {
            Log($"Shop menu speech error: {ex.Message}");
            shopMenuSpeechTracker.Reset();
        }
    }

    private void TickTitleLoadMenuSpeech()
    {
        if (!config.EnableTitleLoadMenuSpeech || titleLoadMenuSpeechTracker is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var currentModule = ReadByte(FieldPositionReader.AddressCurrentModule);
        titleLoadMenuSpeechTracker.ObserveModule(currentModule);
        if (currentModule == TitleMenuCursorReader.TitleModule &&
            titleLoadMenuDataReader?.TryRead(out var nativeState) == true)
        {
            titleLoadMenuSpeechTracker.ObserveState(nativeState, currentModule, now);
        }

        var speech = titleLoadMenuSpeechTracker.Poll(now);
        if (string.IsNullOrWhiteSpace(speech))
        {
            return;
        }

        Log($"Title load menu speech: {speech}");
        Speak(speech);
    }

    private NativeMenuSelection? ReadCurrentConfigValue(string nativeRowLabel)
    {
        if (configMenuValueReader is null)
        {
            return null;
        }

        try
        {
            return configMenuValueReader.ReadCurrentMainValue(nativeRowLabel);
        }
        catch (Exception ex)
        {
            configMenuReaderErrorCount++;
            if (configMenuReaderErrorCount <= 10)
            {
                Log($"Native Config value read error: {ex.Message}");
            }

            return null;
        }
    }

    private unsafe StatusMenuSnapshot? ReadCurrentStatusSnapshot()
    {
        if (savemapPartyReader is null)
        {
            return null;
        }

        try
        {
            var partySlot = ReadInt32(AddressStatusPartySlot);
            return savemapPartyReader.TryReadStatusSummary(partySlot, out var snapshot)
                ? snapshot
                : null;
        }
        catch (Exception ex)
        {
            statusMenuReaderErrorCount++;
            if (statusMenuReaderErrorCount <= 10)
            {
                Log($"Native Status summary read error: {ex.Message}");
            }

            return null;
        }
    }

    private void TickFieldDialogueDrawSpeech()
    {
        if (!config.EnableFieldDialogueDrawSpeech || Volatile.Read(ref activeFieldAskIdentity) is not null)
        {
            return;
        }

        var speech = fieldDialogueDrawSpeechTracker.Poll(DateTime.UtcNow);
        if (speech is null)
        {
            return;
        }

        Log($"Field dialogue draw speech: {speech}");
        SpeakFieldDialogue(speech);
    }

    private void TickNameEntryMenuSpeech()
    {
        if (!config.EnableNameEntryMenuSpeech)
        {
            nameEntryNativeNameTracker.Reset();
            return;
        }

        if (saveMenuSpeechTracker.IsActive)
        {
            nameEntryNativeNameTracker.Reset();
            return;
        }

        if (nameEntryStateReader is null || !nameEntryStateReader.TryRead(out var state))
        {
            nameEntryNativeNameTracker.Reset();
            return;
        }

        var nativeSlotSpeech = nameEntryNativeNameTracker.Observe(
            state.IsActive,
            state.Focus,
            state.GridColumn,
            state.GridRow,
            state.CommandRow,
            state.SelectedSlot,
            state.NameBuffer,
            DateTime.UtcNow);
        var speech = nativeSlotSpeech;
        if (speech is null)
        {
            return;
        }

        Log($"Name entry native speech: focus={state.Focus} " +
            $"grid={state.GridColumn},{state.GridRow} " +
            $"command={state.CommandRow} " +
            $"slot={state.SelectedSlot} text={speech}");
        Speak(speech);
    }

    private void TickRenderedMenuTextSpeech()
    {
        if (!config.EnableRenderedMenuTextSpeech)
        {
            return;
        }

        if (saveMenuSpeechTracker.IsActive)
        {
            renderedMenuTextSpeechTracker.DiscardPending();
            return;
        }

        var speech = renderedMenuTextSpeechTracker.Poll(DateTime.UtcNow);
        if (speech is null)
        {
            return;
        }

        Log($"Rendered menu speech: {speech}");
        Speak(speech);
    }

    private unsafe void DumpMenuStringTablePreview()
    {
        Log("Menu string table preview from process memory:");
        var table = (int*)AddressMenuStringPointerTable;
        for (var i = 0; i < 12; i++)
        {
            var pointer = table[i];
            var value = pointer == 0 ? "<null>" : ReadAscii(pointer, 48);
            Log($"  [{i}] 0x{pointer:X8} {value}");
        }
    }

    private static unsafe string ReadAscii(int address, int maxLength) => ReadAscii((byte*)address, maxLength);

    private static unsafe string ReadAscii(byte* ptr, int maxLength)
    {
        var bytes = new List<byte>(maxLength);
        for (var i = 0; i < maxLength; i++)
        {
            var b = ptr[i];
            if (b == 0)
            {
                break;
            }

            bytes.Add(b);
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static string ReadFf7EncodedText(int address, int maxLength) =>
        CurrentProcessMemoryTextReader.ReadFf7EncodedText(address, maxLength);

    private static unsafe string ReadFf7EncodedText(byte* ptr, int maxLength)
    {
        var bytes = new byte[maxLength];
        for (var i = 0; i < maxLength; i++)
        {
            bytes[i] = ptr[i];
        }

        return Ff7EncodedTextDecoder.DecodeTerminated(bytes);
    }

    private static bool IsReadableMemory(int address, int length)
    {
        if (address <= 0 || length <= 0)
        {
            return false;
        }

        var result = VirtualQuery(
            new IntPtr(address),
            out var info,
            (UIntPtr)Marshal.SizeOf<MemoryBasicInformation>());
        if (result == UIntPtr.Zero ||
            info.State != MemoryStateCommit ||
            !IsReadableProtection(info.Protect))
        {
            return false;
        }

        var start = (long)address;
        var regionStart = info.BaseAddress.ToInt64();
        var regionEnd = regionStart + (long)info.RegionSize.ToUInt64();
        return start >= regionStart && start + length <= regionEnd;
    }

    private static bool IsReadableProtection(uint protection)
    {
        if ((protection & PageGuard) != 0 || (protection & PageNoAccess) != 0)
        {
            return false;
        }

        return (protection & PageReadableMask) != 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQuery(
        IntPtr lpAddress,
        out MemoryBasicInformation lpBuffer,
        UIntPtr dwLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    private static unsafe int ReadInt32(int address) => *(int*)address;

    private static unsafe uint ReadUInt32(int address) => *(uint*)address;

    private static unsafe short ReadInt16(int address) => *(short*)address;

    private static unsafe ushort ReadUInt16(int address) => *(ushort*)address;

    private static unsafe byte ReadByte(int address) => *(byte*)address;

    private void TryGetReloadedHooks()
    {
        if (loader is null)
        {
            return;
        }

        try
        {
            var controller = loader.GetController<IReloadedHooks>();
            if (controller.TryGetTarget(out var target))
            {
                hooks = target;
                Log("Reloaded.Hooks controller acquired.");
            }
            else
            {
                Log("Reloaded.Hooks controller was not available.");
            }
        }
        catch (Exception ex)
        {
            Log($"Could not acquire Reloaded.Hooks controller: {ex.Message}");
        }
    }

    private void InstallModule19WriterDiagnostics()
    {
        if (hooks is null || module19WriterProbe is not null)
        {
            return;
        }

        module19WriterProbe = new Module19WriterProbe(hooks);
        Log(
            "Installed native module 19 writer markers at " +
            string.Join(
                ", ",
                Module19WriterCatalog.RuntimeSites.Select(site => $"0x{site.Address:X8} ({site.Cause})")) +
            ". No managed callback runs from these hooks.");
    }

    private void InstallExperimentalHooks()
    {
        if (hooks is null)
        {
            Log("Experimental hooks requested, but Reloaded.Hooks is unavailable.");
            return;
        }

        if (config.EnableMenuTextRenderDiagnostics)
        {
            TryInstallExperimentalHook("menu text render diagnostics hook", InstallMenuTextRenderDiagnosticsHook);
        }
        else
        {
            Log("Menu text render diagnostics hook is disabled in config.");
        }

        if (ShouldInstallInGameMenuTextDrawHooks())
        {
            TryInstallExperimentalHook("in-game menu text draw hooks", InstallInGameMenuTextDrawDiagnosticsHooks);
        }
        else
        {
            Log("In-game menu text draw hooks are disabled in config.");
        }

        if (ShouldObserveInGameMenuDraws())
        {
            TryInstallExperimentalHook("active menu widget hook", InstallActiveMenuWidgetHook);
        }
        else
        {
            Log("Active menu widget hook is disabled in config.");
        }

        if (config.EnableFieldMessageOpenHook)
        {
            TryInstallExperimentalHook("field message open hook", InstallFieldMessageOpenHook);
        }
        else
        {
            Log("Field message open hook is disabled in config.");
        }

        if (config.EnableFieldMessagePreviewHook)
        {
            TryInstallExperimentalHook("field message preview hook", InstallFieldMessagePreviewHook);
        }
        else
        {
            Log("Field message preview hook is disabled in config.");
        }

        if (config.EnableFieldOpcodeMessageHooks)
        {
            TryInstallExperimentalHook("field opcode message hooks", InstallFieldOpcodeMessageHooks);
        }
        else
        {
            Log("Field opcode message hooks are disabled in config.");
        }

        TryInstallExperimentalHook("Echo-S reactor timer override hook", InstallEchoSReactorTimerOverrideHook);

        if (config.EnableFieldCutsceneDescriptions)
        {
            TryInstallExperimentalHook("field cutscene description hook", InstallFieldCutsceneDescriptionHook);
        }
        else
        {
            Log("Field cutscene descriptions are disabled in config.");
        }

        if (config.EnableTitleMenuNativeCursorSpeech ||
            config.EnableTitleMenuNativeCursorDiagnostics ||
            config.EnableNameEntryMenuSpeech ||
            config.EnableNameEntryMenuDiagnostics ||
            config.EnableBattleMenuSpeech ||
            ShouldObserveInGameMenuDraws())
        {
            TryInstallExperimentalHook("menu cursor draw hooks", InstallMenuCursorDrawHooks);
        }
        else
        {
            Log("Menu cursor draw hooks are disabled in config.");
        }

        if (AnyBattleSpeechEnabled())
        {
            TryInstallExperimentalHook("battle accessibility hooks", InstallBattleAccessibilityHooks);
        }
        else
        {
            Log("Battle accessibility hooks are disabled in config.");
        }
    }

    private void TryInstallExperimentalHook(string name, Action installer)
    {
        try
        {
            installer();
        }
        catch (Exception ex)
        {
            Log($"Could not install {name}: {ex.Message}");
        }
    }

    private void InstallMenuTextRenderDiagnosticsHook()
    {
        if (hooks is null)
        {
            return;
        }

        if (menuTextRendererHook is not null)
        {
            return;
        }

        var deduplicationWindow = TimeSpan.FromMilliseconds(Math.Max(100, config.MenuTextRenderDiagnosticsDedupMs));
        menuTextRenderDiagnostics = new MenuTextRenderDiagnostics(deduplicationWindow, () => DateTime.UtcNow);
        unsafe
        {
            menuTextRendererDetour = MenuTextRendererDetour;
            menuTextRendererHook = hooks.CreateHook<MenuTextRendererDelegate>(
                menuTextRendererDetour,
                AddressMenuTextRenderer,
                -1);
        }

        menuTextRendererHook.Activate();
        Log($"Installed menu text render diagnostics hook at 0x{AddressMenuTextRenderer:X8}.");
    }

    private void InstallInGameMenuTextDrawDiagnosticsHooks()
    {
        if (hooks is null)
        {
            return;
        }

        if (inGameMenuTextDrawHookA is not null || inGameMenuTextDrawHookB is not null)
        {
            return;
        }

        var deduplicationWindow = TimeSpan.FromMilliseconds(Math.Max(100, config.MenuTextRenderDiagnosticsDedupMs));
        inGameMenuTextDrawDiagnostics = new MenuTextRenderDiagnostics(deduplicationWindow, () => DateTime.UtcNow);
        unsafe
        {
            inGameMenuTextDrawDetourA = InGameMenuTextDrawDetourA;
            inGameMenuTextDrawHookA = hooks.CreateHook<InGameMenuTextDrawDelegate>(
                inGameMenuTextDrawDetourA,
                AddressInGameMenuTextDrawA,
                -1);

            inGameMenuTextDrawDetourB = InGameMenuTextDrawDetourB;
            inGameMenuTextDrawHookB = hooks.CreateHook<InGameMenuTextDrawDelegate>(
                inGameMenuTextDrawDetourB,
                AddressInGameMenuTextDrawB,
                -1);
        }

        inGameMenuTextDrawHookA.Activate();
        inGameMenuTextDrawHookB.Activate();
        Log($"Installed in-game menu text draw diagnostics hooks at 0x{AddressInGameMenuTextDrawA:X8} and 0x{AddressInGameMenuTextDrawB:X8}.");
    }

    private void InstallMenuCursorDrawHooks()
    {
        if (hooks is null)
        {
            return;
        }

        if (menuCursorDrawHookA is not null || menuCursorDrawHookB is not null)
        {
            return;
        }

        try
        {
            menuCursorDrawDetourA = MenuCursorDrawDetourA;
            menuCursorDrawHookA = hooks.CreateHook<MenuCursorDrawDelegate>(
                menuCursorDrawDetourA,
                AddressMenuCursorDrawA,
                -1);

            menuCursorDrawDetourB = MenuCursorDrawDetourB;
            menuCursorDrawHookB = hooks.CreateHook<MenuCursorDrawDelegate>(
                menuCursorDrawDetourB,
                AddressMenuCursorDrawB,
                -1);

            menuCursorDrawHookA.Activate();
            menuCursorDrawHookB.Activate();
            Log($"Installed menu cursor draw hooks at 0x{AddressMenuCursorDrawA:X8} and 0x{AddressMenuCursorDrawB:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install menu cursor draw hooks: {ex.Message}");
        }
    }

    private void InstallActiveMenuWidgetHook()
    {
        if (hooks is null || menuWidgetUpdateHook is not null)
        {
            return;
        }

        try
        {
            unsafe
            {
                menuWidgetUpdateDetour = MenuWidgetUpdateDetour;
                menuWidgetUpdateHook = hooks.CreateHook<MenuWidgetUpdateDelegate>(
                    menuWidgetUpdateDetour,
                    AddressMenuWidgetUpdate,
                    -1);
            }

            menuWidgetUpdateHook.Activate();
            Log($"Installed active menu widget hook at 0x{AddressMenuWidgetUpdate:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install active menu widget hook: {ex.Message}. Legacy menu polling will not be used as a fallback.");
        }
    }

    private void InstallFieldMessageOpenHook()
    {
        if (hooks is null)
        {
            return;
        }

        if (fieldMessageOpenHook is not null)
        {
            return;
        }

        try
        {
            fieldMessageOpenDetour = FieldMessageOpenDetour;
            fieldMessageOpenHook = hooks.CreateHook<FieldMessageOpenDelegate>(
                fieldMessageOpenDetour,
                AddressFieldMessageOpen,
                -1);
            fieldMessageOpenHook.Activate();
            Log($"Installed field message open hook at 0x{AddressFieldMessageOpen:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install field message open hook: {ex.Message}");
        }
    }

    private void InstallFieldMessagePreviewHook()
    {
        if (hooks is null)
        {
            return;
        }

        if (fieldMessagePreviewHook is not null)
        {
            return;
        }

        try
        {
            fieldMessagePreviewDetour = FieldMessagePreviewDetour;
            fieldMessagePreviewHook = hooks.CreateHook<FieldMessagePreviewDelegate>(
                fieldMessagePreviewDetour,
                AddressFieldMessagePreview,
                -1);
            fieldMessagePreviewHook.Activate();
            Log($"Installed field message preview hook at 0x{AddressFieldMessagePreview:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install field message preview hook: {ex.Message}");
        }
    }

    private void InstallFieldOpcodeMessageHooks()
    {
        if (hooks is null)
        {
            return;
        }

        if (fieldOpcodeMessageHook is not null)
        {
            return;
        }

        if (fieldOpcodeAddressResolver is null)
        {
            Log("Could not resolve field opcode message hooks: opcode address resolver is not initialized.");
            return;
        }

        if (!fieldOpcodeAddressResolver.TryResolveMessageHooks(out var resolution))
        {
            Log($"Could not resolve field opcode message hooks: {resolution.Diagnostic}");
            return;
        }

        Log($"Resolved field opcode message hooks: {resolution.Diagnostic}");
        try
        {
            fieldOpcodeMessageDetour = FieldOpcodeMessageDetour;
            fieldOpcodeMessageHook = hooks.CreateHook<FieldOpcodeMessageDelegate>(
                fieldOpcodeMessageDetour,
                resolution.MessageOpcodeAddress,
                -1);
            fieldOpcodeMessageHook.Activate();
            Log($"Installed field opcode MESSAGE hook at 0x{resolution.MessageOpcodeAddress:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install field opcode MESSAGE hook: {ex.Message}");
            return;
        }

        try
        {
            fieldOpcodeAskDetour = FieldOpcodeAskDetour;
            fieldOpcodeAskHook = hooks.CreateHook<FieldOpcodeAskDelegate>(
                fieldOpcodeAskDetour,
                resolution.AskOpcodeAddress,
                -1);
            fieldOpcodeAskHook.Activate();
            Log($"Installed field opcode ASK hook at 0x{resolution.AskOpcodeAddress:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install field opcode ASK hook: {ex.Message}");
            return;
        }

        if (resolution.HasDistinctOriginalAskHandler)
        {
            try
            {
                fieldOpcodeOriginalAskDetour = FieldOpcodeOriginalAskDetour;
                fieldOpcodeOriginalAskHook = hooks.CreateHook<FieldOpcodeAskDelegate>(
                    fieldOpcodeOriginalAskDetour,
                    resolution.OriginalAskOpcodeAddress,
                    -1);
                fieldOpcodeOriginalAskHook.Activate();
                Log(
                    $"Installed direct-call field opcode ASK hook at " +
                    $"0x{resolution.OriginalAskOpcodeAddress:X8} behind the FFNx wrapper.");
            }
            catch (Exception ex)
            {
                Log($"Could not install direct-call field opcode ASK hook: {ex.Message}");
            }
        }

        if (!resolution.HasAskUpdateLoop)
        {
            Log(
                "Native ASK cursor helper was not installed because the live handler layout is unknown; " +
                "visible-window polling can read the question and options but cannot identify the highlighted choice.");
            return;
        }

        try
        {
            fieldOpcodeAskUpdateDetour = FieldOpcodeAskUpdateDetour;
            fieldOpcodeAskUpdateHook = hooks.CreateHook<FieldOpcodeAskUpdateDelegate>(
                fieldOpcodeAskUpdateDetour,
                resolution.AskUpdateLoopAddress,
                -1);
            fieldOpcodeAskUpdateHook.Activate();
            Log($"Installed native ASK cursor hook at 0x{resolution.AskUpdateLoopAddress:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install native ASK cursor hook: {ex.Message}");
        }
    }

    private void InstallEchoSReactorTimerOverrideHook()
    {
        if (hooks is null ||
            currentProcessLegacyAddressSpace is null ||
            Environment.Is64BitProcess ||
            fieldOpcodeTimerHook is not null)
        {
            return;
        }

        if (fieldOpcodeAddressResolver is null)
        {
            Log("Could not resolve Echo-S reactor timer hook: opcode address resolver is not initialized.");
            return;
        }

        if (!fieldOpcodeAddressResolver.TryResolveOpcodeHandlers(
                [FieldOpcodeAddressResolver.OpcodeTimerIndex],
                out var handlers,
                out var diagnostic) ||
            !handlers.TryGetValue(FieldOpcodeAddressResolver.OpcodeTimerIndex, out var targetAddress))
        {
            Log($"Could not resolve Echo-S reactor timer hook: {diagnostic}");
            return;
        }

        fieldOpcodeTimerDetour = FieldOpcodeTimerDetour;
        fieldOpcodeTimerHook = hooks.CreateHook<FieldOpcodeTimerDelegate>(
            fieldOpcodeTimerDetour,
            targetAddress,
            -1);
        fieldOpcodeTimerHook.Activate();
        Log(
            $"Installed fingerprint-gated Echo-S reactor timer hook at 0x{targetAddress:X8} " +
            $"(STTIM opcode 0x{FieldOpcodeAddressResolver.OpcodeTimerIndex:X2}).");
    }

    private void InstallFieldCutsceneDescriptionHook()
    {
        if (hooks is null)
        {
            return;
        }

        RefreshFieldCutsceneDescriptionHook();
    }

    private void RefreshFieldCutsceneDescriptionHook()
    {
        var currentHooks = hooks;
        if (currentHooks is null)
        {
            return;
        }

        if (fieldOpcodeAddressResolver is null)
        {
            Log("Could not resolve field cutscene description hook: opcode address resolver is not initialized.");
            return;
        }

        if (!fieldOpcodeAddressResolver.TryResolveCutsceneHandlers(
                out var waitOpcodeAddress,
                out var soundOpcodeAddress,
                out var diagnostic))
        {
            Log($"Could not resolve field cutscene description hook: {diagnostic}");
            return;
        }

        RefreshFieldCutsceneWaitHook(currentHooks, waitOpcodeAddress);
        RefreshFieldCutsceneSoundHook(currentHooks, soundOpcodeAddress);

        var extraOpcodes = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .Select(cue => cue.Opcode)
            .Where(opcode => opcode is not FieldOpcodeAddressResolver.OpcodeWaitIndex and
                not FieldOpcodeAddressResolver.OpcodeSoundIndex)
            .Distinct()
            .ToArray();
        if (extraOpcodes.Length == 0)
        {
            return;
        }

        if (!fieldOpcodeAddressResolver.TryResolveOpcodeHandlers(
                extraOpcodes,
                out var extraHandlers,
                out var extraDiagnostic))
        {
            if (!string.Equals(lastFieldOpcodeCutsceneResolutionDiagnostic, extraDiagnostic, StringComparison.Ordinal))
            {
                lastFieldOpcodeCutsceneResolutionDiagnostic = extraDiagnostic;
                Log($"Could not resolve extra field cutscene opcode handlers: {extraDiagnostic}");
            }

            return;
        }

        lastFieldOpcodeCutsceneResolutionDiagnostic = string.Empty;
        foreach (var targetAddress in extraHandlers.Values
                     .Where(address => address != waitOpcodeAddress && address != soundOpcodeAddress)
                     .Distinct())
        {
            RefreshFieldCutsceneOpcodeHook(currentHooks, targetAddress);
        }
    }

    private void RefreshFieldCutsceneWaitHook(IReloadedHooks currentHooks, int targetAddress)
    {
        lock (fieldOpcodeWaitHookSync)
        {
            if (!fieldOpcodeHookTargetTracker.NeedsInstall(targetAddress) ||
                targetAddress == lastFieldOpcodeWaitHookAttemptTarget)
            {
                return;
            }

            lastFieldOpcodeWaitHookAttemptTarget = targetAddress;
            try
            {
                IHook<FieldOpcodeWaitDelegate>? installedHook = null;
                FieldOpcodeWaitDelegate detour = () => FieldOpcodeWaitDetour(installedHook);
                installedHook = currentHooks.CreateHook<FieldOpcodeWaitDelegate>(
                    detour,
                    targetAddress,
                    -1);
                installedHook.Activate();
                fieldOpcodeWaitHooks[targetAddress] = installedHook;
                fieldOpcodeWaitDetours[targetAddress] = detour;
                fieldOpcodeHookTargetTracker.MarkInstalled(targetAddress);
                Log(
                    $"Installed native field cutscene WAIT hook at 0x{targetAddress:X8} " +
                    $"(live handlers={fieldOpcodeWaitHooks.Count}).");
            }
            catch (Exception ex)
            {
                Log($"Could not install field cutscene description hook at 0x{targetAddress:X8}: {ex.Message}");
            }
        }
    }

    private void RefreshFieldCutsceneSoundHook(IReloadedHooks currentHooks, int targetAddress)
    {
        lock (fieldOpcodeSoundHookSync)
        {
            if (!fieldOpcodeSoundHookTargetTracker.NeedsInstall(targetAddress) ||
                targetAddress == lastFieldOpcodeSoundHookAttemptTarget)
            {
                return;
            }

            lastFieldOpcodeSoundHookAttemptTarget = targetAddress;
            try
            {
                IHook<FieldOpcodeSoundDelegate>? installedHook = null;
                FieldOpcodeSoundDelegate detour = () => FieldOpcodeSoundDetour(installedHook);
                installedHook = currentHooks.CreateHook<FieldOpcodeSoundDelegate>(
                    detour,
                    targetAddress,
                    -1);
                installedHook.Activate();
                fieldOpcodeSoundHooks[targetAddress] = installedHook;
                fieldOpcodeSoundDetours[targetAddress] = detour;
                fieldOpcodeSoundHookTargetTracker.MarkInstalled(targetAddress);
                Log(
                    $"Installed native field cutscene SOUND hook at 0x{targetAddress:X8} " +
                    $"(live handlers={fieldOpcodeSoundHooks.Count}).");
            }
            catch (Exception ex)
            {
                Log($"Could not install field cutscene SOUND hook at 0x{targetAddress:X8}: {ex.Message}");
            }
        }
    }

    private void RefreshFieldCutsceneOpcodeHook(IReloadedHooks currentHooks, int targetAddress)
    {
        lock (fieldOpcodeCutsceneHookSync)
        {
            if (!fieldOpcodeCutsceneHookTargetTracker.NeedsInstall(targetAddress) ||
                !fieldOpcodeCutsceneHookAttemptTargets.Add(targetAddress))
            {
                return;
            }

            try
            {
                IHook<FieldOpcodeCutsceneDelegate>? installedHook = null;
                FieldOpcodeCutsceneDelegate detour = () => FieldOpcodeCutsceneDetour(installedHook);
                installedHook = currentHooks.CreateHook<FieldOpcodeCutsceneDelegate>(
                    detour,
                    targetAddress,
                    -1);
                installedHook.Activate();
                fieldOpcodeCutsceneHooks[targetAddress] = installedHook;
                fieldOpcodeCutsceneDetours[targetAddress] = detour;
                fieldOpcodeCutsceneHookTargetTracker.MarkInstalled(targetAddress);
                Log(
                    $"Installed native field cutscene opcode hook at 0x{targetAddress:X8} " +
                    $"(live handlers={fieldOpcodeCutsceneHooks.Count}).");
            }
            catch (Exception ex)
            {
                Log($"Could not install extra field cutscene opcode hook at 0x{targetAddress:X8}: {ex.Message}");
            }
        }
    }

    private void InstallBattleAccessibilityHooks()
    {
        if (hooks is null)
        {
            return;
        }

        if (config.EnableBattleMenuSpeech)
        {
            TryInstallBattleMenuRenderHook();
        }

        if (config.EnableBattleMenuSpeech ||
            config.EnableBattleTargetSpeech ||
            config.EnableBattleMessageSpeech ||
            config.EnableBattleResultsSpeech ||
            config.EnableBattleDamageSpeech ||
            config.EnableBattleEncounterSpeech ||
            config.EnableBattleEnemyActionSpeech ||
            config.EnableBattleStatusSpeech)
        {
            TryInstallBattleUpdateHook();
        }

        if (config.EnableBattleMessageSpeech)
        {
            TryInstallBattleTextActiveHook();
        }

        if (config.EnableBattleResultsSpeech)
        {
            TryInstallBattleResultsUpdateHook();
        }

        if (config.EnableBattleDamageSpeech || config.EnableBattleStatusSpeech)
        {
            TryInstallBattleDamageDisplayHook();
        }
    }

    private void TryInstallBattleMenuRenderHook()
    {
        if (hooks is null || battleMenuRenderHook is not null)
        {
            return;
        }

        try
        {
            battleMenuRenderDetour = BattleMenuRenderDetour;
            battleMenuRenderHook = hooks.CreateHook<BattleMenuRenderDelegate>(
                battleMenuRenderDetour,
                AddressBattleMenuRender,
                -1);
            battleMenuRenderHook.Activate();
            Log($"Installed native battle menu renderer hook at 0x{AddressBattleMenuRender:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install native battle menu renderer hook: {ex.Message}");
        }
    }

    private void TryInstallBattleUpdateHook()
    {
        if (hooks is null || battleUpdateHook is not null)
        {
            return;
        }

        try
        {
            battleUpdateDetour = BattleUpdateDetour;
            battleUpdateHook = hooks.CreateHook<BattleUpdateDelegate>(
                battleUpdateDetour,
                AddressBattleUpdate,
                -1);
            battleUpdateHook.Activate();
            Log($"Installed native battle update hook at 0x{AddressBattleUpdate:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install native battle update hook: {ex.Message}");
        }
    }

    private void TryInstallBattleTextActiveHook()
    {
        if (hooks is null || battleTextActiveHook is not null)
        {
            return;
        }

        if (battleHookAddressResolver?.TryResolveBattleTextActive(out var address) != true)
        {
            Log("Could not resolve the native battle-text activation hook from the FFNx call chain.");
            return;
        }

        try
        {
            battleTextActiveDetour = BattleTextActiveDetour;
            battleTextActiveHook = hooks.CreateHook<BattleTextActiveDelegate>(
                battleTextActiveDetour,
                address,
                -1);
            battleTextActiveHook.Activate();
            Log($"Installed native battle-text activation hook at 0x{address:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install native battle-text activation hook: {ex.Message}");
        }
    }

    private void TryInstallBattleResultsUpdateHook()
    {
        if (hooks is null || battleResultsUpdateHook is not null)
        {
            return;
        }

        try
        {
            battleResultsUpdateDetour = BattleResultsUpdateDetour;
            battleResultsUpdateHook = hooks.CreateHook<BattleResultsUpdateDelegate>(
                battleResultsUpdateDetour,
                AddressBattleResultsUpdate,
                -1);
            battleResultsUpdateHook.Activate();
            Log($"Installed native battle-results hook at 0x{AddressBattleResultsUpdate:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install native battle-results hook: {ex.Message}");
        }
    }

    private void TryInstallBattleDamageDisplayHook()
    {
        if (hooks is null || battleDamageDisplayHook is not null)
        {
            return;
        }

        try
        {
            battleDamageDisplayDetour = BattleDamageDisplayDetour;
            battleDamageDisplayHook = hooks.CreateHook<BattleDamageDisplayDelegate>(
                battleDamageDisplayDetour,
                AddressBattleDamageDisplay,
                -1);
            battleDamageDisplayHook.Activate();
            Log($"Installed native battle-damage popup hook at 0x{AddressBattleDamageDisplay:X8}.");
        }
        catch (Exception ex)
        {
            Log($"Could not install native battle-damage popup hook: {ex.Message}");
        }
    }

    private unsafe void MenuTextRendererDetour(byte* text, uint x, uint y, int color, int context)
    {
        try
        {
            var rawText = ReadAscii(text, 128);
            if (menuTextRenderDiagnostics?.TryCreateEntry(rawText, x, y, color, context, out var entry) == true)
            {
                Log(entry.ToLogLine());
                if (config.EnableRenderedMenuTextSpeech && !saveMenuSpeechTracker.IsActive)
                {
                    renderedMenuTextSpeechTracker.Observe(entry, DateTime.UtcNow);
                }
            }
        }
        catch (Exception ex)
        {
            menuTextRendererErrorCount++;
            if (menuTextRendererErrorCount <= 10)
            {
                Log($"Menu text render diagnostics error: {ex.Message}");
            }
        }
        finally
        {
            menuTextRendererHook?.OriginalFunction(text, x, y, color, context);
        }
    }

    private unsafe void InGameMenuTextDrawDetourA(int x, int y, byte* text, int color, int context)
    {
        HandleInGameMenuTextDraw(NativeTextDrawSource.InGameA, inGameMenuTextDrawHookA, x, y, text, color, context);
    }

    private unsafe void InGameMenuTextDrawDetourB(int x, int y, byte* text, int color, int context)
    {
        HandleInGameMenuTextDraw(NativeTextDrawSource.InGameB, inGameMenuTextDrawHookB, x, y, text, color, context);
    }

    private void MenuCursorDrawDetourA(int x, int y, int context)
    {
        HandleMenuCursorDraw("A", menuCursorDrawHookA, x, y, context);
    }

    private void MenuCursorDrawDetourB(int x, int y, int context)
    {
        HandleMenuCursorDraw("B", menuCursorDrawHookB, x, y, context);
    }

    private void BattleMenuRenderDetour(int context, short rendererState)
    {
        if (battleVictoryActive)
        {
            battleMenuRenderHook?.OriginalFunction(context, rendererState);
            return;
        }

        var tifaSlotsBefore = TifaSlotResultSnapshot.Invalid;
        try
        {
            battleMenuFrameSpeechCoordinator.BeginFrame(rendererState);
            if (rendererState == 0x1B &&
                config.EnableBattleMessageSpeech &&
                tifaSlotResultReader is not null)
            {
                tifaSlotsBefore = tifaSlotResultReader.Read();
            }
        }
        catch (Exception ex)
        {
            LogBattleHookError("battle menu frame begin", ex);
        }

        try
        {
            battleMenuRenderHook?.OriginalFunction(context, rendererState);
        }
        finally
        {
            try
            {
                if (rendererState == 0x1B &&
                    config.EnableBattleMessageSpeech &&
                    tifaSlotResultReader is not null)
                {
                    tifaSlotSpeechTracker.ObserveFrame(
                        tifaSlotsBefore,
                        tifaSlotResultReader.Read());
                    DrainTifaSlotSpeech();
                }

                var snapshot = battleStateReader?.ReadMenuState(rendererState) ?? BattleMenuStateSnapshot.Invalid;
                battleMenuFrameSpeechCoordinator.CompleteFrame(snapshot);
                var speech = battleMenuFrameSpeechCoordinator.Poll();
                if (!string.IsNullOrWhiteSpace(speech))
                {
                    if (config.EnableBattleDiagnostics)
                    {
                        Log($"Battle menu speech: state={rendererState}, text={speech}");
                    }

                    Speak(speech);
                }
            }
            catch (Exception ex)
            {
                LogBattleHookError("battle menu frame completion", ex);
            }
        }
    }

    private void BattleUpdateDetour()
    {
        if (Interlocked.Exchange(ref battleSessionActive, 1) == 0)
        {
            battleVictoryActive = false;
        }

        var tifaWindowBefore =
            !battleVictoryActive &&
            config.EnableBattleMessageSpeech && tifaSlotResultReader is not null
                ? ReadByte(BattleStateReader.AddressMenuWindowStates + 0x1B)
                : byte.MaxValue;
        try
        {
            try
            {
                if (!battleVictoryActive &&
                    battleStateReader is not null &&
                    ReadByte(BattleStateReader.AddressCurrentModule) == BattleStateReader.BattleModule)
                {
                    if (config.EnableBattleEncounterSpeech)
                    {
                        ObserveBattleEncounterSpeech();
                    }

                    if (config.EnableBattleEnemyActionSpeech &&
                        battleStateReader.TryReadBattleActors(out var preUpdateActors))
                    {
                        ObserveBattleEnemyActionSpeech(preUpdateActors);
                    }
                }
            }
            catch (Exception ex)
            {
                LogBattleHookError("pre-update battle action", ex);
            }

            battleUpdateHook?.OriginalFunction();
        }
        finally
        {
            try
            {
                if (battleStateReader is not null &&
                    ReadByte(BattleStateReader.AddressCurrentModule) == BattleStateReader.BattleModule &&
                    battleStateReader.TryReadVictorySignal(out var victoryObserved) &&
                    victoryObserved)
                {
                    if (!battleVictoryActive)
                    {
                        battleVictoryActive = true;
                        ResetBattleInteractionTrackers();
                    }

                    if (config.EnableBattleResultsSpeech)
                    {
                        if (battleStateReader.TryReadPartyProgress(out var victoryProgress))
                        {
                            battleResultsSpeechTracker.ObserveBattleProgress(victoryProgress);
                        }

                        battleResultsSpeechTracker.ObserveVictorySignal(true);
                        DrainBattleResultsSpeech();
                    }
                }

                if (!battleVictoryActive)
                {
                    if (tifaWindowBefore == BattleStateReader.ActiveWindowState &&
                        tifaSlotResultReader is not null &&
                        ReadByte(BattleStateReader.AddressMenuWindowStates + 0x1B) == 3)
                    {
                        tifaSlotSpeechTracker.ObserveCommitted(
                            tifaSlotResultReader.ReadCommitted());
                        DrainTifaSlotSpeech();
                    }

                    if (battleStateReader is null ||
                        ReadByte(BattleStateReader.AddressCurrentModule) != BattleStateReader.BattleModule)
                    {
                        battleTargetSpeechTracker.Reset();
                    }
                    else
                    {
                        var needsActors = config.EnableBattleDamageSpeech ||
                            config.EnableBattleEnemyActionSpeech ||
                            config.EnableBattleStatusSpeech;
                        IReadOnlyList<BattleActorSnapshot> actors = Array.Empty<BattleActorSnapshot>();
                        var actorsAvailable = !needsActors ||
                            battleStateReader.TryReadBattleActors(out actors);

                        if (config.EnableBattleDamageSpeech && actorsAvailable)
                        {
                            battleDamageSpeechTracker.SeedActors(actors);
                        }

                        if (config.EnableBattleEncounterSpeech)
                        {
                            ObserveBattleEncounterSpeech();
                        }

                        if (config.EnableBattleEnemyActionSpeech && actorsAvailable)
                        {
                            ObserveBattleEnemyActionSpeech(actors);
                        }

                        if (config.EnableBattleStatusSpeech && actorsAvailable)
                        {
                            battleStatusSpeechTracker.Observe(actors);
                            var speech = battleStatusSpeechTracker.Poll();
                            if (!string.IsNullOrWhiteSpace(speech))
                            {
                                if (config.EnableBattleDiagnostics)
                                {
                                    Log($"Battle status speech: {speech}");
                                }

                                Speak(speech, false);
                            }
                        }

                        if (config.EnableBattleMenuSpeech &&
                            battleStateReader.TryIsRootCommandMenuActive(out var rootCommandMenuActive))
                        {
                            battleMenuFrameSpeechCoordinator.ObserveRootCommandMenuActive(
                                rootCommandMenuActive);
                        }

                        if (config.EnableBattleResultsSpeech)
                        {
                            if (battleStateReader.TryReadPartyProgress(out var battleProgress))
                            {
                                battleResultsSpeechTracker.ObserveBattleProgress(battleProgress);
                            }

                            if (battleStateReader.TryReadVictorySignal(out var isVictory))
                            {
                                battleResultsSpeechTracker.ObserveVictorySignal(isVictory);
                            }

                            DrainBattleResultsSpeech();
                        }

                        if (config.EnableBattleTargetSpeech)
                        {
                            var target = battleStateReader.ReadTarget();
                            battleTargetSpeechTracker.Observe(target);
                            var speech = battleTargetSpeechTracker.Poll();
                            if (!string.IsNullOrWhiteSpace(speech))
                            {
                                if (config.EnableBattleDiagnostics)
                                {
                                    Log($"Battle target speech: target={target.SelectedTarget}, mask=0x{target.TargetMask:X4}, text={speech}");
                                }

                                Speak(speech);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogBattleHookError("battle update", ex);
            }
        }
    }

    private void ObserveBattleEncounterSpeech()
    {
        if (battleStateReader is null)
        {
            return;
        }

        var encounter = battleStateReader.ReadEncounter();
        battleEncounterSpeechTracker.Observe(encounter);
        var speech = battleEncounterSpeechTracker.Poll();
        if (string.IsNullOrWhiteSpace(speech))
        {
            return;
        }

        if (config.EnableBattleDiagnostics)
        {
            Log($"Battle encounter speech: formation={encounter.FormationId}, layout={encounter.LayoutType}, text={speech}");
        }

        Speak(speech);
    }

    private void ObserveBattleEnemyActionSpeech(IReadOnlyList<BattleActorSnapshot> actors)
    {
        if (battleStateReader is null)
        {
            return;
        }

        var action = battleStateReader.ReadCurrentEnemyAction();
        battleEnemyActionSpeechTracker.Observe(action, actors);
        var speech = battleEnemyActionSpeechTracker.Poll();
        if (string.IsNullOrWhiteSpace(speech))
        {
            return;
        }

        if (config.EnableBattleDiagnostics)
        {
            Log(
                $"Battle enemy action speech: event={action.EventIndex}, attacker={action.AttackerActorIndex}, " +
                $"command=0x{action.CommandId:X2}, sceneSlot={action.SceneAttackIndex}, " +
                $"action=0x{action.ActionId:X4}, rawTargets=0x{action.TargetMask:X4}, text={speech}");
        }

        Speak(speech, false);
    }

    private void BattleTextActiveDetour(short bufferIndex)
    {
        try
        {
            battleTextActiveHook?.OriginalFunction(bufferIndex);
        }
        finally
        {
            try
            {
                if (!battleVictoryActive)
                {
                    battleMessageSpeechTracker.ObserveActiveBuffer(bufferIndex);
                    var speech = battleMessageSpeechTracker.Poll();
                    if (!string.IsNullOrWhiteSpace(speech))
                    {
                        if (config.EnableBattleDiagnostics)
                        {
                            Log($"Battle message speech: buffer={bufferIndex}, text={speech}");
                        }

                        Speak(speech);
                    }
                }
            }
            catch (Exception ex)
            {
                LogBattleHookError("battle text activation", ex);
            }
        }
    }

    private void BattleDamageDisplayDetour()
    {
        try
        {
            if (battleVictoryActive)
            {
                return;
            }

            var popup = battleDamagePopupReader?.Read() ?? BattleDamagePopupSnapshot.Invalid;
            if (popup.IsValid &&
                battleStateReader?.TryReadBattleActor(popup.TargetActor, out var actor) == true)
            {
                if (config.EnableBattleDamageSpeech)
                {
                    battleDamageSpeechTracker.Observe(popup, actor);
                    var damageSpeech = battleDamageSpeechTracker.Poll();
                    if (!string.IsNullOrWhiteSpace(damageSpeech))
                    {
                        if (config.EnableBattleDiagnostics)
                        {
                            Log($"Battle damage speech: effect={popup.EffectIndex}, target={popup.TargetActor}, value={popup.Value}, flags=0x{popup.Flags:X}, text={damageSpeech}");
                        }

                        Speak(damageSpeech, false);
                    }
                }

                if (config.EnableBattleStatusSpeech &&
                    battleStateReader.TryReadVisibleActorCorrelation(
                        popup.TargetActor,
                        out var visibleActorCorrelation))
                {
                    battleStatusSpeechTracker.ConfirmVisibleDamageOutcome(
                        popup,
                        visibleActorCorrelation);
                    var statusSpeech = battleStatusSpeechTracker.Poll();
                    if (!string.IsNullOrWhiteSpace(statusSpeech))
                    {
                        if (config.EnableBattleDiagnostics)
                        {
                            Log($"Battle status speech confirmed by damage popup: {statusSpeech}");
                        }

                        Speak(statusSpeech, false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogBattleHookError("battle damage popup", ex);
        }
        finally
        {
            battleDamageDisplayHook?.OriginalFunction();
        }
    }

    private void BattleResultsUpdateDetour()
    {
        try
        {
            try
            {
                ResetBattleInteractionSpeech();
                battleResultsSpeechTracker.BeginFrame();
                ObserveBattleResultsState();
            }
            catch (Exception ex)
            {
                LogBattleHookError("battle results pre-update", ex);
            }
        }
        finally
        {
            try
            {
                battleResultsUpdateHook?.OriginalFunction();
            }
            finally
            {
                try
                {
                    battleResultsSpeechTracker.CompleteFrame();
                    ObserveBattleResultsState();
                    DrainBattleResultsSpeech();
                }
                catch (Exception ex)
                {
                    LogBattleHookError("battle results post-update", ex);
                }
            }
        }
    }

    private void ObserveBattleResultsState()
    {
        if (battleResultsReader is null || battleStateReader is null)
        {
            return;
        }

        if (battleStateReader.TryReadPartyProgress(out var partyProgress))
        {
            battleResultsSpeechTracker.ObserveResults(
                battleResultsReader.Read(),
                partyProgress);
        }
    }

    private void DrainBattleResultsSpeech()
    {
        while (battleResultsSpeechTracker.PollSpeech() is { } pending)
        {
            if (config.EnableBattleDiagnostics)
            {
                Log(
                    $"Battle results speech: interrupt={pending.Interrupt}, " +
                    $"text={pending.Text}");
            }

            Speak(pending.Text, pending.Interrupt);
        }
    }

    private void DrainTifaSlotSpeech()
    {
        var results = new List<string>();
        string? speech;
        while (!string.IsNullOrWhiteSpace(speech = tifaSlotSpeechTracker.Poll()))
        {
            results.Add(speech);
        }

        if (results.Count == 0)
        {
            return;
        }

        var combined = string.Join(", ", results);
        if (config.EnableBattleDiagnostics)
        {
            Log($"Tifa slot speech: {combined}");
        }

        Speak(combined, true);
    }

    private void ResetBattleInteractionSpeech()
    {
        battleVictoryActive = false;
        if (Interlocked.Exchange(ref battleSessionActive, 0) == 0)
        {
            return;
        }

        ResetBattleInteractionTrackers();
    }

    private void ResetBattleInteractionTrackers()
    {
        battleMenuFrameSpeechCoordinator.Reset();
        battleTargetSpeechTracker.Reset();
        battleMessageSpeechTracker.Reset();
        battleDamageSpeechTracker.Reset();
        battleEncounterSpeechTracker.Reset();
        battleEnemyActionSpeechTracker.Reset();
        battleStatusSpeechTracker.Reset();
        tifaSlotSpeechTracker.Reset();
    }

    private void LogBattleHookError(string source, Exception exception)
    {
        battleHookErrorCount++;
        if (battleHookErrorCount <= 20)
        {
            Log($"Native {source} hook error: {exception.Message}");
        }
    }

    private unsafe void MenuWidgetUpdateDetour(int* widget)
    {
        if (activeMenuWidgetFrameBridge is null)
        {
            menuWidgetUpdateHook?.OriginalFunction(widget);
            return;
        }

        try
        {
            var address = unchecked((int)(nint)widget);
            var snapshot = activeMenuWidgetFrameBridge.CompleteBeforeUpdate(
                address,
                DateTime.UtcNow,
                () => menuWidgetUpdateHook?.OriginalFunction(widget));
            var currentModule = ReadByte(FieldPositionReader.AddressCurrentModule);
            if (!config.EnableInGameMenuWidgetSpeech)
            {
                saveMenuSpeechTracker.Reset();
            }
            else
            {
                saveMenuSpeechTracker.ObserveHostState(
                    currentModule,
                    foregroundProcessGate.IsCurrentProcessForeground(),
                    IsNameEntryMenuActive(),
                    snapshot);

                if (saveMenuSpeechTracker.IsActive)
                {
                    DiscardCompetingSaveMenuSpeech();
                }
            }

            if (config.EnableTitleLoadMenuSpeech && snapshot is { } titleLoadWidget)
            {
                titleLoadMenuSpeechTracker?.ObserveWidget(
                    titleLoadWidget,
                    currentModule,
                    DateTime.UtcNow);
            }

            if (config.EnableMenuWidgetDiagnostics && snapshot is { } active)
            {
                var diagnostic =
                    $"{active.Address:X8}:{active.First}:{active.Cursor}:{active.Columns}:{active.Rows}:" +
                    $"{active.ScrollOffset}:{active.ScrollDelta}:{active.ScrollState}";
                if (!string.Equals(diagnostic, lastActiveMenuWidgetDiagnostic, StringComparison.Ordinal))
                {
                    lastActiveMenuWidgetDiagnostic = diagnostic;
                    Log(
                        $"Active menu widget: {active.Name} base=0x{active.Address:X8} " +
                        $"column={active.First}, row={active.Cursor}, grid={active.Columns}x{active.Rows}, " +
                        $"scroll={active.ScrollOffset}, delta={active.ScrollDelta}, state={active.ScrollState}");
                }
            }
        }
        catch (Exception ex)
        {
            menuWidgetUpdateErrorCount++;
            if (menuWidgetUpdateErrorCount <= 10)
            {
                Log($"Active menu widget hook error: {ex.Message}");
            }
        }
    }

    private unsafe bool FfnxPlayVoiceDetour(
        byte* fieldName,
        byte windowId,
        byte dialogId,
        byte page)
    {
        var played = ffnxPlayVoiceHook?.OriginalFunction(fieldName, windowId, dialogId, page) == true;
        ffnxVoicePlaybackEventQueue.TryCapture(
            fieldName,
            windowId,
            dialogId,
            page,
            played,
            Stopwatch.GetTimestamp());
        return played;
    }

    private int FieldMessageOpenDetour(short windowIndex, short messageId)
    {
        var result = fieldMessageOpenHook?.OriginalFunction(windowIndex, messageId) ?? 0;
        nativeFieldHookEventQueue.TryCaptureMessageOpen(windowIndex, messageId, result);
        return result;
    }

    private int FieldMessagePreviewDetour(short messageId)
    {
        var result = fieldMessagePreviewHook?.OriginalFunction(messageId) ?? 0;
        nativeFieldHookEventQueue.TryCaptureMessagePreview(messageId, result);
        return result;
    }

    private int FieldOpcodeWaitDetour(IHook<FieldOpcodeWaitDelegate>? activeHook)
    {
        var context = TryReadFieldScriptContext();
        var result = activeHook?.OriginalFunction() ?? 0;
        if (context is { } value)
        {
            nativeFieldHookEventQueue.TryCaptureCutsceneContext(value);
        }

        return result;
    }

    private int FieldOpcodeSoundDetour(IHook<FieldOpcodeSoundDelegate>? activeHook)
    {
        var context = TryReadFieldScriptContext();
        var result = activeHook?.OriginalFunction() ?? 0;
        if (context is { } value)
        {
            nativeFieldHookEventQueue.TryCaptureCutsceneContext(value);
        }

        return result;
    }

    private int FieldOpcodeCutsceneDetour(IHook<FieldOpcodeCutsceneDelegate>? activeHook)
    {
        var context = TryReadFieldScriptContext();
        var result = activeHook?.OriginalFunction() ?? 0;
        if (context is { } value)
        {
            nativeFieldHookEventQueue.TryCaptureCutsceneContext(value);
        }

        return result;
    }

    private FieldScriptContext? TryReadFieldScriptContext()
    {
        try
        {
            if (fieldScriptContextReader?.TryRead(out var context) == true)
            {
                return context;
            }

            if (config.EnableFieldCutsceneDescriptionDiagnostics &&
                ReadUInt16(FieldScriptContextReader.AddressCurrentFieldId) == DeferredZoneSpeechTracker.OpeningFieldId)
            {
                Interlocked.Increment(ref fieldCutsceneContextUnavailableCount);
            }

            return null;
        }
        catch (Exception)
        {
            Interlocked.Increment(ref nativeFieldHookCaptureErrorCount);
            return null;
        }
    }

    private void HandleFieldCutsceneDescriptionContext(FieldScriptContext? context)
    {
        if (context is null || !config.EnableFieldCutsceneDescriptions)
        {
            return;
        }

        try
        {
            if (config.EnableFieldCutsceneDescriptionDiagnostics &&
                context.Value.FieldId is DeferredZoneSpeechTracker.OpeningFieldId or 133 or 134 or 136 or 137)
            {
                var observedKey = new FieldCutsceneDescriptionKey(
                    context.Value.FieldId,
                    context.Value.EntityId,
                    context.Value.ScriptId,
                    context.Value.ByteIndex);
                lock (fieldCutsceneDescriptionSync)
                {
                    if (observedFieldCutsceneOpcodes.Add(observedKey))
                    {
                        Log(
                            $"Native field cutscene opcode observed: field={context.Value.FieldId}, " +
                            $"entity={context.Value.EntityId}, script={context.Value.ScriptId}, " +
                            $"byte={context.Value.ByteIndex}, opcode=0x{context.Value.Opcode:X2}.");
                    }
                }
            }

            if (!TryGetLoadedFieldScriptIdentity(context.Value.FieldId, out var identity))
            {
                if (config.EnableFieldCutsceneDescriptionDiagnostics)
                {
                    Log($"Field cutscene description identity unavailable: field={context.Value.FieldId}.");
                }

                return;
            }

            var cue = fieldCutsceneDescriptionTracker.Observe(context.Value, identity);
            if (cue is null)
            {
                return;
            }

            lock (fieldCutsceneDescriptionSync)
            {
                pendingFieldCutsceneDescriptions.Enqueue(cue.Value);
            }

            if (config.EnableFieldCutsceneDescriptionDiagnostics)
            {
                Log(
                    $"Field cutscene description queued: field={context.Value.FieldId}, entity={context.Value.EntityId}, " +
                    $"script={context.Value.ScriptId}, byte={context.Value.ByteIndex}, opcode=0x{context.Value.Opcode:X2}.");
            }
        }
        catch (Exception ex)
        {
            fieldCutsceneDescriptionErrorCount++;
            if (fieldCutsceneDescriptionErrorCount <= 10)
            {
                Log($"Field cutscene description hook error: {ex}");
            }
        }
    }

    private int FieldOpcodeMessageDetour()
    {
        var observation = TryReadFieldOpcodeMessageObservation(FieldOpcodeKind.Message);
        var result = fieldOpcodeMessageHook?.OriginalFunction() ?? 0;
        if (observation is { } value)
        {
            nativeFieldHookEventQueue.TryCaptureOpcodeMessage(value, result);
        }
        else
        {
            Interlocked.Increment(ref nativeFieldHookCaptureErrorCount);
        }

        return result;
    }

    private int FieldOpcodeTimerDetour()
    {
        var context = TryReadFieldScriptContext();
        var result = fieldOpcodeTimerHook?.OriginalFunction() ?? 0;
        if (context is { } value &&
            EchoSReactorTimerOverrideTracker.IsExactEchoTimerCandidate(value))
        {
            nativeFieldHookEventQueue.TryCaptureTimerSet(value, result);
        }

        return result;
    }

    private int FieldOpcodeAskDetour(int arg) =>
        CaptureFieldOpcodeAsk(arg, fieldOpcodeAskHook);

    private int FieldOpcodeOriginalAskDetour(int arg)
    {
        // The FFNx wrapper normally forwards to this original handler. The
        // outer wrapper detour already owns that lifecycle, so do not publish
        // it twice. Direct calls that bypass the live opcode-table wrapper are
        // captured here, which is required by several flashback ASK scripts.
        if (fieldOpcodeAskDetourDepth > 0)
        {
            return fieldOpcodeOriginalAskHook?.OriginalFunction(arg) ?? 0;
        }

        return CaptureFieldOpcodeAsk(arg, fieldOpcodeOriginalAskHook);
    }

    private int CaptureFieldOpcodeAsk(int arg, IHook<FieldOpcodeAskDelegate>? sourceHook)
    {
        var preCallIdentity = Volatile.Read(ref activeFieldAskIdentity);
        var observation = TryReadFieldOpcodeMessageObservation(FieldOpcodeKind.Ask);
        NativeFieldMessageIdentity? publishedIdentity = null;
        if (observation is { } ask)
        {
            var current = Volatile.Read(ref activeFieldAskIdentity);
            var sameSequentialLifecycle = fieldOpcodeAskDetourDepth == 0 &&
                current is not null &&
                current.Kind == FieldOpcodeKind.Ask &&
                current.FieldId == ask.FieldId &&
                current.WindowId == ask.WindowId &&
                current.DialogId == ask.DialogId;
            publishedIdentity = sameSequentialLifecycle
                ? current
                : new NativeFieldMessageIdentity(
                    FieldOpcodeKind.Ask,
                    ask.FieldId,
                    ask.WindowId,
                    ask.DialogId,
                    Interlocked.Increment(ref nextFieldAskLifecycleToken));
            observation = ask with { LifecycleToken = publishedIdentity!.LifecycleToken };
            Volatile.Write(ref activeFieldAskIdentity, publishedIdentity);
        }

        int result;
        fieldOpcodeAskDetourDepth++;
        try
        {
            result = sourceHook?.OriginalFunction(arg) ?? 0;
        }
        finally
        {
            fieldOpcodeAskDetourDepth--;
        }

        if (publishedIdentity is not null)
        {
            // Closing ASK state must become invalid before the delayed monitor
            // can issue speech. This compare/exchange cannot erase a newer ASK
            // published by a nested or immediately following native call.
            NativeFieldAskCloseInvalidator.Invalidate(
                ref activeFieldAskIdentity,
                publishedIdentity,
                result);
            if (result != 0)
            {
                // If a nested same-thread ASK closed and cleared its token,
                // the still-active outer invocation resumes its exact token.
                Interlocked.CompareExchange(
                    ref activeFieldAskIdentity,
                    publishedIdentity,
                    comparand: null);
            }
        }
        else
        {
            // Missing native parameters cannot be associated with an exact
            // lifecycle. Invalidate only the identity that predated this call;
            // a nested/newer publication remains untouched and the monitor's
            // observation-loss recovery releases all native ownership.
            preCallIdentity?.SpeechLifecycle.Close();
            if (preCallIdentity is not null)
            {
                Interlocked.CompareExchange(
                    ref activeFieldAskIdentity,
                    value: null,
                    comparand: preCallIdentity);
            }

            Interlocked.Increment(ref nativeFieldHookCaptureErrorCount);
        }

        if (observation is { } value)
        {
            nativeFieldHookEventQueue.TryCaptureOpcodeMessage(value, result);
        }

        return result;
    }

    private int FieldOpcodeAskUpdateDetour(
        byte windowId,
        byte dialogId,
        byte firstQuestionLine,
        byte lastQuestionLine,
        IntPtr currentQuestionLinePointer)
    {
        ushort currentQuestionLine = ushort.MaxValue;
        var fieldId = -1;
        long lifecycleToken = 0;
        try
        {
            var address = currentQuestionLinePointer.ToInt32();
            if (address > 0 && IsReadableMemory(address, sizeof(ushort)))
            {
                currentQuestionLine = ReadUInt16(address);
                fieldId = ReadUInt16(FieldPositionReader.AddressFieldId);
                var activeIdentity = Volatile.Read(ref activeFieldAskIdentity);
                if (activeIdentity is not null &&
                    activeIdentity.Kind == FieldOpcodeKind.Ask &&
                    activeIdentity.FieldId == fieldId &&
                    activeIdentity.WindowId == windowId &&
                    activeIdentity.DialogId == dialogId)
                {
                    lifecycleToken = activeIdentity.LifecycleToken;
                }
            }
        }
        catch (Exception)
        {
            Interlocked.Increment(ref nativeFieldHookCaptureErrorCount);
        }

        var result = fieldOpcodeAskUpdateHook?.OriginalFunction(
            windowId,
            dialogId,
            firstQuestionLine,
            lastQuestionLine,
            currentQuestionLinePointer) ?? 0;

        if (currentQuestionLine != ushort.MaxValue && fieldId >= 0)
        {
            nativeFieldHookEventQueue.TryCaptureAskCursor(
                fieldId,
                windowId,
                dialogId,
                firstQuestionLine,
                lastQuestionLine,
                currentQuestionLine,
                lifecycleToken);
        }

        return result;
    }

    private FieldOpcodeMessageObservation? TryReadFieldOpcodeMessageObservation(FieldOpcodeKind kind)
    {
        try
        {
            if (fieldOpcodeParameterReader is null)
            {
                return null;
            }

            return kind == FieldOpcodeKind.Message
                ? fieldOpcodeParameterReader.TryReadMessage(out var message) ? message : null
                : fieldOpcodeParameterReader.TryReadAsk(out var ask) ? ask : null;
        }
        catch (Exception)
        {
            Interlocked.Increment(ref nativeFieldHookCaptureErrorCount);
            return null;
        }
    }

    private void HandleFieldOpcodeMessageObservation(FieldOpcodeMessageObservation? observation, int result)
    {
        if (observation is null)
        {
            return;
        }

        try
        {
            var value = observation.Value;
            var source = $"opcode {value.Kind} field {value.FieldId} window {value.WindowId} dialog {value.DialogId}";
            if (value.Kind == FieldOpcodeKind.Ask)
            {
                HandleFieldAskMessageObservation(value, source, result);
                return;
            }

            if (value.Kind == FieldOpcodeKind.Message)
            {
                ffnxVoicePlaybackTracker.ObserveMessage(
                    value.FieldId,
                    value.WindowId,
                    value.DialogId,
                    Stopwatch.GetTimestamp());
                var firstLifecycleObservation = fieldOpcodeMessageSpeechGate.ShouldQueue(source, result);
                if (firstLifecycleObservation && config.EnableFieldOpcodeMessageDiagnostics)
                {
                    Log($"Field {source}: result={result}, speech=native visible window buffer");
                }

                if (firstLifecycleObservation)
                {
                    QueueEchoSDisclaimerSpeech(value.FieldId, value.DialogId);
                    fieldVisibleWindowSpeechCoordinator.BeginNativeMessageLifecycle(
                        new NativeFieldMessageIdentity(
                            FieldOpcodeKind.Message,
                            value.FieldId,
                            value.WindowId,
                            value.DialogId));
                }

                return;
            }
        }
        catch (Exception ex)
        {
            fieldOpcodeMessageErrorCount++;
            if (fieldOpcodeMessageErrorCount <= 10)
            {
                Log($"Field opcode message hook error: {ex}");
            }
        }
    }

    private void HandleFieldAskMessageObservation(
        FieldOpcodeMessageObservation observation,
        string source,
        int result)
    {
        var observedIdentity = new NativeFieldMessageIdentity(
            FieldOpcodeKind.Ask,
            observation.FieldId,
            observation.WindowId,
            observation.DialogId,
            observation.LifecycleToken);
        var pages = flevelFieldTextResolver?.ReadMessagePagesById(
            observation.FieldId,
            observation.DialogId) ?? Array.Empty<Ff7DecodedTextPage>();
        if (!FieldAskTextFormatter.TryResolveChoicePage(
                pages,
                observation.FirstQuestionLine,
                observation.LastQuestionLine,
                out var lines))
        {
            return;
        }

        var prompt = FieldAskTextFormatter.FormatPrompt(
            lines,
            observation.FirstQuestionLine,
            observation.LastQuestionLine);
        var promptKey = $"{source}\u001flifecycle {observation.LifecycleToken}\u001fprompt\u001f{prompt}";
        if (result == 0)
        {
            fieldOpcodeMessageSpeechGate.ShouldQueue(promptKey, result);
            return;
        }

        var currentIdentity = Volatile.Read(ref activeFieldAskIdentity);
        if (currentIdentity != observedIdentity)
        {
            return;
        }
        var ownershipIdentity = currentIdentity;

        if (begunNativeAskLifecycles.Add(ownershipIdentity))
        {
            // The native ASK itself proves a new visible-window lifecycle even
            // when text resolution or native speech queuing later fails and
            // checked polling must provide the fallback.
            fieldVisibleWindowSpeechCoordinator.BeginNativeAskLifecycle(
                ownershipIdentity,
                requireCoherentObservation: config.EnableFieldMessageReader,
                now: DateTime.UtcNow,
                maximumObservationWait: TimeSpan.FromMilliseconds(
                    Math.Max(250, config.FieldMessageScanIntervalMs * 2)));
        }

        if (pages.Count > 1 &&
            !IsFieldAskChoicePageVisible(observation.WindowId, lines))
        {
            return;
        }

        var choice = fieldAskChoiceSpeechTracker.Poll(observation.LifecycleToken);
        if (nativeAskPollingFallbackState.IsFallback(ownershipIdentity))
        {
            // Polling owns recovery of the failed/missing prompt, but only the
            // native cursor state knows which choice is highlighted. Keep that
            // exact-token channel alive for the rest of the ASK lifecycle.
            if (!string.IsNullOrWhiteSpace(choice))
            {
                var fallbackChoiceResult = QueueNativeFieldMessageSpeech(
                    new FieldMessageCandidate(source, choice),
                    DateTime.UtcNow,
                    $"{source}\u001flifecycle {observation.LifecycleToken}\u001fchoice\u001f{observation.FirstQuestionLine}\u001f{observation.LastQuestionLine}\u001f{choice}",
                    ownershipIdentity,
                    NativeFieldSpeechKind.ChoiceUpdate,
                    // The native cursor restores the selected item, but the
                    // failed prompt remains polling-owned and must not be
                    // marked as though the whole visible ASK was spoken.
                    completesVisibleContent: false);
                if (fallbackChoiceResult is PendingNativeFieldSpeechEnqueueResult.Enqueued or
                    PendingNativeFieldSpeechEnqueueResult.Coalesced)
                {
                    // The queued native cursor must not reclaim/suppress the
                    // polling-owned question while it waits behind recovery.
                    nativeFieldMessageOwnershipTracker.MarkSpeechDelivered(
                        ownershipIdentity,
                        DateTime.UtcNow,
                        visibleContentComplete: false);
                }
            }

            return;
        }

        var firstLifecycleObservation = fieldOpcodeMessageSpeechGate.ShouldQueue(promptKey, result);
        if (firstLifecycleObservation)
        {
            var primaryText = prompt.Length != 0 ? prompt : choice ?? string.Empty;
            if (primaryText.Length == 0)
            {
                fieldOpcodeMessageSpeechGate.ShouldQueue(promptKey, result: 0);
                return;
            }

            var enqueueResult = QueueNativeFieldMessageSpeech(
                new FieldMessageCandidate(source, primaryText),
                DateTime.UtcNow,
                promptKey,
                ownershipIdentity,
                NativeFieldSpeechKind.Prompt,
                completesVisibleContent: prompt.Length == 0 && !string.IsNullOrWhiteSpace(choice));
            if (enqueueResult is not (PendingNativeFieldSpeechEnqueueResult.Enqueued or
                PendingNativeFieldSpeechEnqueueResult.Coalesced))
            {
                if (enqueueResult is not (
                    PendingNativeFieldSpeechEnqueueResult.Duplicate or
                    PendingNativeFieldSpeechEnqueueResult.Full))
                {
                    fieldOpcodeMessageSpeechGate.ShouldQueue(promptKey, result: 0);
                }

                return;
            }

            acceptedNativeAskPromptKeys[ownershipIdentity] = promptKey;
            if (prompt.Length != 0 && !string.IsNullOrWhiteSpace(choice))
            {
                var choiceResult = QueueNativeFieldMessageSpeech(
                    new FieldMessageCandidate(source, choice),
                    DateTime.UtcNow,
                    $"{source}\u001flifecycle {observation.LifecycleToken}\u001fchoice\u001f{observation.FirstQuestionLine}\u001f{observation.LastQuestionLine}\u001f{choice}",
                    ownershipIdentity,
                    NativeFieldSpeechKind.ChoiceUpdate,
                    completesVisibleContent: true);
                if (choiceResult == PendingNativeFieldSpeechEnqueueResult.Full)
                {
                    ReleaseAcceptedNativeAskPrompt(ownershipIdentity);
                }
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(choice) ||
            !acceptedNativeAskPromptKeys.ContainsKey(ownershipIdentity))
        {
            return;
        }

        var duplicateKey = $"{source}\u001flifecycle {observation.LifecycleToken}\u001fchoice\u001f{observation.FirstQuestionLine}\u001f{observation.LastQuestionLine}\u001f{choice}";
        if (config.EnableFieldOpcodeMessageDiagnostics)
        {
            Log(
                $"Field {source}: result={result}, first={observation.FirstQuestionLine}, " +
                $"last={observation.LastQuestionLine}, text={choice}");
        }

        QueueNativeFieldMessageSpeech(
            new FieldMessageCandidate(source, choice),
            DateTime.UtcNow,
            duplicateKey,
            ownershipIdentity,
            NativeFieldSpeechKind.ChoiceUpdate,
            completesVisibleContent: true);
    }

    private bool IsFieldAskChoicePageVisible(
        int windowId,
        IReadOnlyList<string> pageLines)
    {
        if (fieldMessageReader?.TryReadVisibleWindows(out var windows) != true)
        {
            return false;
        }

        FieldVisibleWindowSnapshot? match = null;
        foreach (var window in windows)
        {
            if (window.WindowId != windowId)
            {
                continue;
            }

            if (match is not null)
            {
                return false;
            }

            match = window;
        }

        return match is { } visible &&
            FieldAskTextFormatter.IsChoicePageVisible(pageLines, visible.Text);
    }

    private void ResetFieldAskState()
    {
        var endedIdentity = Interlocked.Exchange(ref activeFieldAskIdentity, null);
        CancelPendingNativeFieldSpeech(null);
        fieldOpcodeMessageSpeechGate.Reset();
        nativeFieldMessageOwnershipTracker.Release(endedIdentity);
        fieldVisibleWindowSpeechCoordinator.CancelNativeSpeech(endedIdentity);
        begunNativeAskLifecycles.Clear();
        nativeAskPollingFallbackState.Clear();
        fieldAskChoiceSpeechTracker.Reset();
    }

    private void CompleteDeferredFieldAskClose(FieldOpcodeMessageObservation observation)
    {
        var endedIdentity = new NativeFieldMessageIdentity(
            FieldOpcodeKind.Ask,
            observation.FieldId,
            observation.WindowId,
            observation.DialogId,
            observation.LifecycleToken);
        CancelPendingNativeFieldSpeech(endedIdentity);
        nativeFieldMessageOwnershipTracker.Release(endedIdentity);
        fieldVisibleWindowSpeechCoordinator.CancelNativeSpeech(endedIdentity);
        begunNativeAskLifecycles.Remove(endedIdentity);
        nativeAskPollingFallbackState.Remove(endedIdentity);
        fieldAskChoiceSpeechTracker.Reset(observation.LifecycleToken);

        // The detour already invalidated this closing lifecycle. If another
        // ASK published before this deferred event drained, it is newer even
        // when field/window/dialog values are identical and must survive.
        _ = NativeFieldAskDeferredClosePolicy.MayClearPublishedCoordinates(
            Volatile.Read(ref activeFieldAskIdentity));
    }

    private static string PreviewFieldCandidate(FieldMessageCandidate candidate)
    {
        if (candidate.Text.Length == 0)
        {
            return "<empty>";
        }

        var text = candidate.Text.Replace("\"", "'", StringComparison.Ordinal);
        if (text.Length > 80)
        {
            text = text[..80] + "...";
        }

        return $"{candidate.Source}:\"{text}\"";
    }

    private void HandleFieldMessageOpen(short windowIndex, short messageId, int result)
    {
        var activeAsk = Volatile.Read(ref activeFieldAskIdentity);
        if (activeAsk is not null &&
            messageId == activeAsk.DialogId &&
            windowIndex == activeAsk.WindowId)
        {
            return;
        }

        var currentFieldId = ReadUInt16(FieldScriptContextReader.AddressCurrentFieldId);
        QueueEchoSDisclaimerSpeech(currentFieldId, messageId);
        TickEchoSDisclaimerSpeech();
        if (TryGetLoadedFieldScriptIdentity(currentFieldId, out var loadedIdentity) &&
            EchoSCompatibilityManifest.IsSupportedDisclaimer(loadedIdentity))
        {
            if (EchoSCompatibilityManifest.ResolveDisclaimerText(loadedIdentity, messageId) is not null)
            {
                return;
            }
        }

        var candidate = fieldMessageReader?.ReadMessageById(messageId) ?? new FieldMessageCandidate(string.Empty, string.Empty);
        if (candidate.Text.Length == 0)
        {
            if (config.EnableFieldMessageOpenDiagnostics)
            {
                Log($"Field message open: window={windowIndex}, message={messageId}, result={result}, text=<empty>");
            }

            return;
        }

        if (config.EnableFieldMessageOpenDiagnostics)
        {
            Log($"Field message open: window={windowIndex}, message={messageId}, result={result}, text={candidate.Text}");
        }

        if (config.EnableFieldMessageOpenDiagnostics)
        {
            Log("Field message open speech deferred to the native visible window buffer.");
        }
    }

    private void QueueEchoSDisclaimerSpeech(int fieldId, int messageId)
    {
        if (echoSDisclaimerSpeechTracker.Queue(fieldId, messageId) &&
            config.EnableFieldOpcodeMessageDiagnostics)
        {
            Log($"Echo-S disclaimer candidate queued: field={fieldId}, message={messageId}.");
        }
    }

    private void TickEchoSDisclaimerSpeech()
    {
        if (!echoSDisclaimerSpeechTracker.HasPending)
        {
            return;
        }

        if (ReadByte(FieldScriptContextReader.AddressCurrentModule) != FieldPositionReader.FieldModule)
        {
            echoSDisclaimerSpeechTracker.Reset();
            return;
        }

        var fieldId = ReadUInt16(FieldScriptContextReader.AddressCurrentFieldId);
        if (fieldId != 109)
        {
            echoSDisclaimerSpeechTracker.Reset();
            return;
        }

        if (!config.SpeakFieldMessages ||
            !TryGetLoadedFieldScriptIdentity(fieldId, out var identity))
        {
            return;
        }

        var candidate = echoSDisclaimerSpeechTracker.TryResolve(identity);
        if (candidate is null)
        {
            return;
        }

        var delivered = Speak(candidate.Value.Text, interrupt: true);
        echoSDisclaimerSpeechTracker.Acknowledge(candidate.Value, delivered);
        Log(
            $"Echo-S disclaimer speech: message={candidate.Value.MessageId}, " +
            $"delivered={delivered}, text={candidate.Value.Text}");
        if (delivered)
        {
            fieldVisibleWindowSpeechCoordinator.ObserveUnavailable();
        }
    }

    private void HandleEchoSReactorTimerSet(FieldScriptContext context, int result)
    {
        if (!echoSReactorTimerOverrideTracker.Queue(context))
        {
            return;
        }

        Log(
            $"Echo-S reactor timer candidate queued: field={context.FieldId}, entity={context.EntityId}, " +
            $"script={context.ScriptId}, byte=0x{context.ByteIndex:X}, opcode=0x{context.Opcode:X2}, result={result}.");
    }

    private void TickEchoSReactorTimerOverride()
    {
        if (!echoSReactorTimerOverrideTracker.HasPending)
        {
            return;
        }

        if (ReadByte(FieldScriptContextReader.AddressCurrentModule) != FieldPositionReader.FieldModule)
        {
            echoSReactorTimerOverrideTracker.Reset();
            return;
        }

        var fieldId = ReadUInt16(FieldScriptContextReader.AddressCurrentFieldId);
        if (fieldId != 125)
        {
            echoSReactorTimerOverrideTracker.Reset();
            return;
        }

        if (!TryGetLoadedFieldScriptIdentity(fieldId, out var identity))
        {
            return;
        }

        var decision = echoSReactorTimerOverrideTracker.TryResolve(identity);
        if (decision is null)
        {
            if (EchoSCompatibilityManifest.ResolveVariant(identity) != SupportedFieldScriptVariant.EchoS124)
            {
                echoSReactorTimerOverrideTracker.Reset();
            }

            return;
        }

        var applied = currentProcessLegacyAddressSpace?.TryWriteInt32(
            decision.Value.Address,
            decision.Value.Seconds) == true;
        echoSReactorTimerOverrideTracker.Acknowledge(decision.Value, applied);
        Log(
            $"Echo-S reactor timer override: field={decision.Value.Context.FieldId}, " +
            $"byte=0x{decision.Value.Context.ByteIndex:X}, seconds={decision.Value.Seconds}, applied={applied}.");
    }

    private void HandleFieldMessagePreview(short messageId, int result)
    {
        if (messageId == Volatile.Read(ref activeFieldAskIdentity)?.DialogId)
        {
            return;
        }

        var candidate = fieldMessageReader?.ReadMessageById(messageId) ?? new FieldMessageCandidate(string.Empty, string.Empty);
        if (candidate.Text.Length == 0)
        {
            if (config.EnableFieldMessagePreviewDiagnostics)
            {
                Log($"Field message preview: message={messageId}, result={result}, text=<empty>");
            }

            return;
        }

        if (config.EnableFieldMessagePreviewDiagnostics)
        {
            Log($"Field message preview: message={messageId}, result={result}, text={candidate.Text}");
        }

        if (config.EnableFieldMessagePreviewDiagnostics)
        {
            Log("Field message preview speech deferred to the native visible window buffer.");
        }
    }

    private unsafe void HandleInGameMenuTextDraw(
        NativeTextDrawSource source,
        IHook<InGameMenuTextDrawDelegate>? hook,
        int x,
        int y,
        byte* text,
        int color,
        int context)
    {
        var currentModule = ReadByte(FieldPositionReader.AddressCurrentModule);
        if (config.EnableInGameMenuWidgetSpeech)
        {
            saveMenuSpeechTracker.ObserveModule(currentModule);
        }
        else
        {
            saveMenuSpeechTracker.Reset();
        }

        if (currentModule == FieldPositionReader.FieldModule)
        {
            nativeTextDrawEventQueue.TryCapture(source, x, y, text, color, context, currentModule);
            hook?.OriginalFunction(x, y, text, color, context);
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var decodedText = ReadFf7EncodedText(text, 128);
            ProcessInGameMenuTextDraw(source, x, y, decodedText, color, context, currentModule, now);
        }
        catch (Exception ex)
        {
            inGameMenuTextDrawErrorCount++;
            if (inGameMenuTextDrawErrorCount <= 10)
            {
                Log($"In-game menu text draw diagnostics error: {ex.Message}");
            }
        }
        finally
        {
            hook?.OriginalFunction(x, y, text, color, context);
        }
    }

    private void ProcessInGameMenuTextDraw(
        NativeTextDrawSource source,
        int x,
        int y,
        string decodedText,
        int color,
        int context,
        byte currentModule,
        DateTime now)
    {
        var drawEntry = new MenuTextRenderEntry(decodedText, unchecked((uint)x), unchecked((uint)y), color, context);
        var saveMenuOwnsSpeech = saveMenuSpeechTracker.IsActive;
        if (!battleVictoryActive &&
            config.EnableBattleMenuSpeech &&
            currentModule == BattleStateReader.BattleModule)
        {
            battleMenuFrameSpeechCoordinator.ObserveDraw(drawEntry);
        }

        if (config.EnableBattleResultsSpeech && currentModule == BattleResultsReader.ResultsModule)
        {
            battleResultsSpeechTracker.ObserveDraw(drawEntry);
        }

        if (ShouldObserveInGameMenuDraws() && !saveMenuOwnsSpeech)
        {
            partyFormationSpeechTracker.ObserveDraw(drawEntry, currentModule, now);
            materiaTutorialSpeechTracker.Observe(drawEntry, currentModule, now);
            activeMenuFrameSpeechCoordinator.ObserveDraw(drawEntry);
            staticMenuCursorSpeechTracker.ObserveDraw(drawEntry, now);
            statusMenuSpeechTracker.ObserveDraw(drawEntry, now);
        }

        if (config.EnableFieldDialogueDrawSpeech && Volatile.Read(ref activeFieldAskIdentity) is null)
        {
            fieldDialogueDrawSpeechTracker.Observe(drawEntry, currentModule, now);
        }

        if (config.EnableNameEntryMenuDiagnostics)
        {
            nameEntryMenuSpeechTracker.ObserveText(drawEntry, currentModule, now);
        }

        if (config.EnableTitleLoadMenuSpeech)
        {
            titleLoadMenuSpeechTracker?.ObserveDraw(drawEntry, currentModule, now);
        }

        if (inGameMenuTextDrawDiagnostics?.TryCreateEntry(decodedText, (uint)x, (uint)y, color, context, out var entry) != true)
        {
            return;
        }

        if (config.EnableInGameMenuTextDrawDiagnostics)
        {
            var sourceName = source == NativeTextDrawSource.InGameA ? "A" : "B";
            Log(entry.ToLogLine().Replace("Menu text render:", $"In-game menu text draw {sourceName}:"));
        }

        if (config.EnableInGameMenuTextDrawSpeech && !saveMenuOwnsSpeech)
        {
            renderedMenuTextSpeechTracker.Observe(entry, now);
        }
    }

    private void HandleMenuCursorDraw(
        string source,
        IHook<MenuCursorDrawDelegate>? hook,
        int x,
        int y,
        int context)
    {
        try
        {
            var now = DateTime.UtcNow;
            var currentModule = ReadByte(FieldPositionReader.AddressCurrentModule);
            if (config.EnableInGameMenuWidgetSpeech)
            {
                saveMenuSpeechTracker.ObserveModule(currentModule);
            }
            else
            {
                saveMenuSpeechTracker.Reset();
            }

            var saveMenuOwnsSpeech = saveMenuSpeechTracker.IsActive;
            var snapshot = new TitleMenuCursorSnapshot(
                source,
                currentModule,
                x,
                y,
                context);
            var cursor = new MenuCursorDrawObservation(source, currentModule, x, y, context);

            if (!battleVictoryActive &&
                config.EnableBattleMenuSpeech &&
                currentModule == BattleStateReader.BattleModule)
            {
                battleMenuFrameSpeechCoordinator.ObserveCursor(cursor);
            }

            if (ShouldObserveInGameMenuDraws() && !saveMenuOwnsSpeech)
            {
                partyFormationSpeechTracker.ObserveCursor(cursor, now);
                activeMenuFrameSpeechCoordinator.ObserveCursor(cursor);
                staticMenuCursorSpeechTracker.ObserveCursor(cursor, now);
            }

            if (config.EnableNameEntryMenuDiagnostics)
            {
                var nameEntryObservation = nameEntryMenuSpeechTracker.ObserveCursor(
                    new NameEntryCursorSnapshot(source, currentModule, x, y, context),
                    now);
                if (config.EnableNameEntryMenuDiagnostics && nameEntryObservation is not null)
                {
                    LogNameEntryCursorDiagnostic(nameEntryObservation.Value);
                }
            }

            if (TitleMenuCursorReader.TryCreateSelection(snapshot, out var selection))
            {
                QueueTitleMenuCursorSelection(selection, now);
                if (config.EnableTitleMenuNativeCursorDiagnostics)
                {
                    LogTitleMenuCursorDiagnostic(selection.Key, selection.ToLogLine());
                }
            }
            else if (config.EnableTitleMenuNativeCursorDiagnostics && TitleMenuCursorReader.LooksNearTitleMenu(snapshot))
            {
                var key = $"near\u001f{snapshot.Source}\u001f{snapshot.CurrentModule}\u001f{snapshot.X}\u001f{snapshot.Y}\u001f{snapshot.Context}";
                LogTitleMenuCursorDiagnostic(
                    key,
                    $"Title menu cursor draw near menu: source={snapshot.Source} module={snapshot.CurrentModule} x={snapshot.X} y={snapshot.Y} context=0x{snapshot.Context:X8}");
            }
            else if (snapshot.CurrentModule != TitleMenuCursorReader.TitleModule)
            {
                ClearTitleMenuCursorSelection();
            }
        }
        catch (Exception ex)
        {
            menuCursorDrawErrorCount++;
            if (menuCursorDrawErrorCount <= 10)
            {
                Log($"Menu cursor draw diagnostics error: {ex.Message}");
            }
        }
        finally
        {
            hook?.OriginalFunction(x, y, context);
        }
    }

    private void LogNameEntryCursorDiagnostic(NameEntryCursorObservation observation)
    {
        if (string.Equals(observation.Key, lastNameEntryCursorDiagnosticKey, StringComparison.Ordinal))
        {
            return;
        }

        lastNameEntryCursorDiagnosticKey = observation.Key;
        Log(observation.ToLogLine());
    }

    private void QueueTitleMenuCursorSelection(TitleMenuCursorSelection selection, DateTime now)
    {
        lock (titleMenuCursorSync)
        {
            if (string.Equals(selection.Key, lastTitleMenuCursorObservedKey, StringComparison.Ordinal))
            {
                return;
            }

            lastTitleMenuCursorObservedKey = selection.Key;
            pendingTitleMenuCursorSelection = selection;
            pendingTitleMenuCursorSeenAt = now;
        }
    }

    private void ClearTitleMenuCursorSelection()
    {
        lock (titleMenuCursorSync)
        {
            pendingTitleMenuCursorSelection = null;
            pendingTitleMenuCursorSeenAt = DateTime.MinValue;
            lastTitleMenuCursorObservedKey = string.Empty;
        }
    }

    private void LogTitleMenuCursorDiagnostic(string key, string message)
    {
        if (string.Equals(key, lastTitleMenuCursorDiagnosticKey, StringComparison.Ordinal))
        {
            return;
        }

        lastTitleMenuCursorDiagnosticKey = key;
        Log(message);
    }

    private void TickTitleMenuCursorSpeech()
    {
        if (!config.EnableTitleMenuNativeCursorSpeech)
        {
            return;
        }

        TitleMenuCursorSelection selection;
        lock (titleMenuCursorSync)
        {
            if (pendingTitleMenuCursorSelection is null)
            {
                return;
            }

            var settleTime = TimeSpan.FromMilliseconds(Math.Max(0, config.TitleMenuNativeCursorSettleMs));
            if (DateTime.UtcNow - pendingTitleMenuCursorSeenAt < settleTime)
            {
                return;
            }

            selection = pendingTitleMenuCursorSelection.Value;
            pendingTitleMenuCursorSelection = null;
            if (string.Equals(selection.Key, lastTitleMenuCursorSpokenKey, StringComparison.Ordinal))
            {
                return;
            }

            lastTitleMenuCursorSpokenKey = selection.Key;
        }

        Log($"Title menu native cursor speech: {selection.SpokenText}");
        Speak(selection.SpokenText);
    }

    private bool Speak(string text) => Speak(text, true);

    private bool SpeakFieldDialogue(string text)
    {
        var fieldId = ReadUInt16(FieldScriptContextReader.AddressCurrentFieldId);
        return Speak(text, !fieldCutsceneSpeechPriority.ShouldQueueDialogue(fieldId, DateTime.UtcNow));
    }

    private bool ShouldObserveInGameMenuDraws() =>
        config.EnableInGameMenuHelpTextSpeech ||
        config.EnableInGameMenuWidgetSpeech ||
        config.EnableTitleLoadMenuSpeech;

    private bool IsNameEntryMenuActive() =>
        nameEntryStateReader is not null &&
        nameEntryStateReader.TryRead(out var state) &&
        state.IsActive;

    private bool AnyBattleSpeechEnabled() =>
        config.EnableBattleMenuSpeech ||
        config.EnableBattleTargetSpeech ||
        config.EnableBattleMessageSpeech ||
        config.EnableBattleResultsSpeech ||
        config.EnableBattleDamageSpeech ||
        config.EnableBattleEncounterSpeech ||
        config.EnableBattleEnemyActionSpeech ||
        config.EnableBattleStatusSpeech;

    private bool ShouldInstallInGameMenuTextDrawHooks() =>
        config.EnableInGameMenuTextDrawDiagnostics ||
        config.EnableInGameMenuTextDrawSpeech ||
        config.EnableFieldDialogueDrawSpeech ||
        config.EnableNameEntryMenuSpeech ||
        config.EnableNameEntryMenuDiagnostics ||
        config.EnableBattleMenuSpeech ||
        config.EnableBattleResultsSpeech ||
        ShouldObserveInGameMenuDraws();

    private bool Speak(string text, bool interrupt)
    {
        var localizedText = localizer.Localize(text);
        Log($"Speak: {localizedText}");
        var delivered = config.EnableSpeech && speaker?.Speak(localizedText, interrupt) == true;
        if (delivered)
        {
            repeatLastSpeechController.RememberDelivered(localizedText);
        }

        return delivered;
    }

    private void LoadConfig(IModConfigV1? modConfig)
    {
        try
        {
            modDirectory = ResolveModDirectory(modConfig);
            logPath = ModPaths.ResolveLogPath(modDirectory);
            var configPath = Path.Combine(modDirectory, "Configuration", "config.json");
            if (File.Exists(configPath))
            {
                config = JsonSerializer.Deserialize<Ff7.Accessibility.Core.AccessibilityConfig>(File.ReadAllText(configPath))
                         ?? new Ff7.Accessibility.Core.AccessibilityConfig();
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception ex)
        {
            Log($"Could not load config, using defaults: {ex}");
            config = new Ff7.Accessibility.Core.AccessibilityConfig();
        }

        if (AccessibilityConfigMigration.ApplySeparatedLadderCueDefaults(config))
        {
            Log("Restored the original traversal cue and separated the 214.wav ladder-mount cue.");
        }
    }

    private string ResolveModDirectory(IModConfigV1? modConfig)
    {
        if (loader is IModLoaderV2 loaderV2)
        {
            return loaderV2.GetDirectoryForModId(modConfig?.ModId ?? "ff7.accessibility.reloaded");
        }

        return AppContext.BaseDirectory;
    }

    private string? ResolveOpeningMoviePath()
    {
        if (gameRootDirectory is null)
        {
            return null;
        }

        return OpeningMoviePathResolver.Resolve(gameRootDirectory, ffnxRuntimeLoaded);
    }

    private string ResolveOpeningMovieAudioTrackPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.OpeningMovieAudioTrackPath)
            ? @"Assets\movies\opening_audio_description.ogg"
            : config.OpeningMovieAudioTrackPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);
    }

    private CosmoFootstepSequencer? InitializeCosmoFootsteps()
    {
        if (!config.UseCosmoFootstepSounds)
        {
            Log("Cosmo footstep sounds are disabled in config.");
            return null;
        }

        try
        {
            var soundDirectory = ResolveCosmoFootstepSoundDirectory();
            var configPath = Path.Combine(soundDirectory, "config.toml");
            if (!File.Exists(configPath))
            {
                Log($"Cosmo footstep config missing: {configPath}");
                return null;
            }

            var footstepConfig = CosmoFootstepConfig.Load(configPath);
            if (footstepConfig.TrackCount == 0)
            {
                Log($"Cosmo footstep config had no usable sequential tracks: {configPath}");
                return null;
            }

            var fieldNames = ReadFlevelFieldNames();
            Log($"Cosmo footstep config loaded: {footstepConfig.TrackCount} tracks, {fieldNames.Count} flevel names, directory={soundDirectory}");
            return new CosmoFootstepSequencer(footstepConfig, fieldNames, soundDirectory);
        }
        catch (Exception ex)
        {
            Log($"Could not initialize Cosmo footsteps: {ex.Message}");
            return null;
        }
    }

    private string ResolveCosmoFootstepSoundDirectory()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.CosmoFootstepSoundDirectory)
            ? @"Assets\footsteps\cosmo"
            : config.CosmoFootstepSoundDirectory;
        var resolvedPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);

        if (File.Exists(Path.Combine(resolvedPath, "config.toml")))
        {
            return resolvedPath;
        }

        return resolvedPath;
    }

    private IReadOnlyDictionary<int, string> ReadFlevelFieldNames()
    {
        if (gameRootDirectory is null)
        {
            Log("Could not load flevel field names: game root is unknown.");
            return new Dictionary<int, string>();
        }

        var source = gameLanguage is null
            ? new FlevelDataSource(gameRootDirectory)
            : new FlevelDataSource(gameRootDirectory, gameLanguage);
        var fieldNames = source.FieldNames;
        Log($"flevel data source: {source.Diagnostic}");
        if (fieldNames.Count == 0)
        {
            Log("No flevel field names were read from the selected native source.");
        }
        else if (fieldNames.TryGetValue(404, out var openingFieldName))
        {
            Log($"flevel field name sample: 404={openingFieldName}");
        }

        return fieldNames;
    }

    private string ResolveFootstepSoundPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.FieldFootstepSoundPath)
            ? @"Assets\footsteps\selected_subway_step.ogg"
            : config.FieldFootstepSoundPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);
    }

    private string ResolveObjectCueSoundPath(string fileName) =>
        Path.Combine(modDirectory, "Assets", "navigation", fileName);

    private string ResolveFieldZoneTransitionCueSoundPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.FieldZoneTransitionCueSoundPath)
            ? @"Assets\navigation\field_zone_transition.wav"
            : config.FieldZoneTransitionCueSoundPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);
    }

    private string ResolveFieldSwingingBarTimingCueSoundPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.FieldSwingingBarTimingCueSoundPath)
            ? @"Assets\navigation\swing_jump_058.wav"
            : config.FieldSwingingBarTimingCueSoundPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);
    }

    private string ResolveFloor60SoldierTurnCueSoundPath()
    {
        const string legacyDefault = @"Assets\navigation\swing_jump_058.wav";
        const string statueDefault = @"Assets\navigation\floor60_statue_134.wav";
        var configuredPath =
            string.IsNullOrWhiteSpace(config.Floor60SoldierTurnCueSoundPath) ||
            string.Equals(
                config.Floor60SoldierTurnCueSoundPath.Replace('/', '\\'),
                legacyDefault,
                StringComparison.OrdinalIgnoreCase)
                ? statueDefault
                : config.Floor60SoldierTurnCueSoundPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);
    }

    private string ResolveFieldExitCueSoundPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.FieldExitCueSoundPath)
            ? @"Assets\navigation\field_zone_transition.wav"
            : config.FieldExitCueSoundPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);
    }

    private string ResolveFieldLadderCueSoundPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.FieldLadderCueSoundPath)
            ? @"Assets\navigation\ladder_061.wav"
            : config.FieldLadderCueSoundPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);
    }

    private string ResolveFieldLadderMountCueSoundPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.FieldLadderMountCueSoundPath)
            ? @"Assets\navigation\ladder_approach_214.wav"
            : config.FieldLadderMountCueSoundPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);
    }

    private string ResolveWorldMapNavigationBeaconSoundPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.WorldMapNavigationBeaconSoundPath)
            ? @"Assets\navigation\navigation_beacon_214_remix.wav"
            : config.WorldMapNavigationBeaconSoundPath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(modDirectory, configuredPath);
    }

    private string? ResolveGameRootDirectory()
    {
        try
        {
            var executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            return executablePath is null ? null : Path.GetDirectoryName(executablePath);
        }
        catch (Exception ex)
        {
            Log($"Could not resolve game root directory: {ex.Message}");
            return null;
        }
    }

    private void Log(string message)
    {
        logger?.WriteLine($"[FFVII Accessibility] {message}", Color.LightBlue);
        try
        {
            File.AppendAllText(logPath, $"{DateTime.Now:u} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never destabilize the game.
        }
    }
}

internal readonly record struct FieldVisibleWindowSpeechDispatch(
    int WindowId,
    string Text,
    bool Interrupt,
    long DispatchToken,
    NativeFieldMessageIdentity? PollingRecoveryIdentity = null);

internal sealed class FieldVisibleWindowSpeechCoordinator
{
    private readonly TimeSpan stableWindow;
    private readonly Dictionary<int, WindowLifecycle> lifecycles = [];
    private readonly HashSet<SpeechBatchWindow> openSpeechBatch = [];
    private readonly List<HeldReadyWindow> heldReadyWindows = [];
    private readonly Dictionary<SpeechBatchWindow, NativeFieldMessageIdentity> pendingNativeBlockers = [];
    private readonly Dictionary<SpeechBatchWindow, NativeFieldMessageIdentity> acknowledgedNativeBlockers = [];
    private readonly Dictionary<NativeFieldMessageIdentity, NativeSpeechBarrier> nativeSpeechBarriers = [];
    private readonly HashSet<NativeFieldMessageIdentity> incompleteNativeOpenSpeech = [];
    private readonly HashSet<NativeFieldMessageIdentity> nativePollingRecoveryRequired = [];
    private readonly Dictionary<long, PendingPollingDispatch> pendingPollingDispatches = [];
    private readonly HashSet<SpeechBatchWindow> latestVisibleBatchMembers = [];
    private long nextReadyChronology;
    private long nextLifecycleGeneration;
    private long nextPollingDispatchToken;

    public FieldVisibleWindowSpeechCoordinator(TimeSpan stableWindow)
    {
        this.stableWindow = stableWindow < TimeSpan.Zero ? TimeSpan.Zero : stableWindow;
    }

    public IReadOnlyList<FieldVisibleWindowSpeechDispatch> Observe(
        IReadOnlyList<FieldVisibleWindowSnapshot> windows,
        byte activeMessageCount,
        DateTime now,
        Func<FieldVisibleWindowSnapshot, bool>? shouldSuppress = null,
        NativeFieldMessageIdentity? nativeOwnershipIdentity = null,
        bool nativeOwnershipDelivered = false,
        bool nativeOwnershipSpeechPending = false,
        bool requireDeliveryAcknowledgement = false)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (nativeOwnershipIdentity is not null &&
            nativeSpeechBarriers.TryGetValue(nativeOwnershipIdentity, out var observedBarrier))
        {
            observedBarrier.MarkObserved(now);
        }
        if (nativeOwnershipSpeechPending && nativeOwnershipIdentity is not null)
        {
            // Establish the ASK/later-sibling boundary before any closed-window
            // cleanup. This is required even when no count-zero frame occurred:
            // a retained later row may disappear immediately before the first
            // suppressed ASK snapshot.
            EnsureNativeSpeechBoundary(nativeOwnershipIdentity);
        }

        if (activeMessageCount == 0)
        {
            if (nativeOwnershipSpeechPending &&
                nativeOwnershipIdentity is not null)
            {
                RetainHeldWindowLifecycles();
                return DispatchClosedNativePredecessors(
                    nativeOwnershipIdentity,
                    requireDeliveryAcknowledgement);
            }

            if (pendingNativeBlockers.Count != 0)
            {
                if (nativeOwnershipSpeechPending)
                {
                    // Count zero can be the opening frame before the ASK
                    // window publishes. Flush only genuine older retained
                    // predecessors; later siblings remain behind the native
                    // ownership boundary.
                    RetainHeldWindowLifecycles();
                    return DispatchClosedNativePredecessors(
                        nativeOwnershipIdentity,
                        requireDeliveryAcknowledgement);
                }

                // The native candidate disappeared without being issued. Do
                // not strand stable sighted-visible polling text forever.
                pendingNativeBlockers.Clear();
            }

            if (heldReadyWindows.Count != 0)
            {
                var orderedHeld = heldReadyWindows
                    .OrderBy(held => held.WindowId)
                    .ThenBy(held => held.Chronology)
                    .ToArray();
                var interruptRetained = orderedHeld[0].Lifecycle.HasDispatched ||
                    !openSpeechBatch.Contains(orderedHeld[0].Identity);
                ResetVisibleSetPreservingPendingNativeBarrier(
                    nativeOwnershipIdentity,
                    nativeOwnershipSpeechPending);
                var retainedDispatches = new FieldVisibleWindowSpeechDispatch[orderedHeld.Length];
                for (var index = 0; index < orderedHeld.Length; index++)
                {
                    var held = orderedHeld[index];
                    retainedDispatches[index] = CreatePollingDispatch(
                        held.Identity,
                        held.WindowId,
                        held.Text,
                        held.Lifecycle,
                        held.Chronology,
                        interrupt: index == 0 && interruptRetained);
                }

                if (!requireDeliveryAcknowledgement)
                {
                    foreach (var dispatch in retainedDispatches)
                    {
                        AcknowledgePollingSpeech(dispatch.DispatchToken, delivered: true);
                    }
                }

                return retainedDispatches;
            }

            ResetVisibleSetPreservingPendingNativeBarrier(
                nativeOwnershipIdentity,
                nativeOwnershipSpeechPending);
            return Array.Empty<FieldVisibleWindowSpeechDispatch>();
        }

        var seenWindowIds = new HashSet<int>();
        foreach (var window in windows)
        {
            if (window.WindowId is < 0 or >= FieldMessageReader.WindowCount ||
                !seenWindowIds.Add(window.WindowId))
            {
                Reset();
                return Array.Empty<FieldVisibleWindowSpeechDispatch>();
            }
        }

        foreach (var closedWindowId in lifecycles.Keys.Where(id => !seenWindowIds.Contains(id)).ToArray())
        {
            lifecycles.Remove(closedWindowId);
        }

        var observations = new List<(FieldVisibleWindowSnapshot Window, WindowLifecycle Lifecycle, WindowReadiness Readiness, string Text, bool Suppressed)>(windows.Count);
        foreach (var window in windows)
        {
            if (!lifecycles.TryGetValue(window.WindowId, out var lifecycle) ||
                lifecycle.GuestPointer != window.GuestPointer)
            {
                lifecycle = new WindowLifecycle(
                    window.GuestPointer,
                    NextLifecycleGeneration());
                lifecycles[window.WindowId] = lifecycle;
            }

            var suppressed = shouldSuppress?.Invoke(window) == true;
            var nativeSpeechDeliveredForWindow = suppressed &&
                nativeOwnershipDelivered &&
                nativeOwnershipIdentity is not null &&
                nativeOwnershipIdentity.WindowId == window.WindowId;
            var readiness = lifecycle.Observe(
                window,
                suppressed,
                nativeSpeechDeliveredForWindow,
                stableWindow,
                now,
                out var text);
            observations.Add((window, lifecycle, readiness, text, suppressed));
        }

        if (nativeOwnershipIdentity is not null &&
            incompleteNativeOpenSpeech.Contains(nativeOwnershipIdentity) &&
            lifecycles.TryGetValue(nativeOwnershipIdentity.WindowId, out var incompleteLifecycle))
        {
            // A partial native prompt may have succeeded while the reader was
            // unavailable and therefore had no SpeechBatchWindow to bind. The
            // first coherent matching lifecycle joins that already-open batch,
            // ensuring full-window recovery continues rather than interrupts.
            openSpeechBatch.Add(new SpeechBatchWindow(
                nativeOwnershipIdentity.WindowId,
                incompleteLifecycle.GuestPointer,
                incompleteLifecycle.Generation));
        }
        if (nativeOwnershipIdentity is not null &&
            nativePollingRecoveryRequired.Contains(nativeOwnershipIdentity) &&
            nativeSpeechBarriers.TryGetValue(nativeOwnershipIdentity, out var pollingRecoveryBarrier))
        {
            var pollingRecovery = observations.FirstOrDefault(observation =>
                observation.Window.WindowId == nativeOwnershipIdentity.WindowId &&
                !observation.Suppressed);
            if (pollingRecovery.Lifecycle is not null)
            {
                pollingRecoveryBarrier.BindPollingRecovery(pollingRecovery.Lifecycle);
            }
        }

        if (nativeOwnershipIdentity is not null &&
            nativeSpeechBarriers.TryGetValue(nativeOwnershipIdentity, out var nativeBarrier))
        {
            foreach (var predecessor in observations
                         .Where(observation =>
                             observation.Window.WindowId < nativeOwnershipIdentity.WindowId &&
                             !observation.Suppressed)
                         .Select(observation => observation.Lifecycle))
            {
                nativeBarrier.Add(predecessor);
            }
        }

        var visibleBatchMembers = observations
            .Where(observation => !observation.Suppressed && observation.Text.Length != 0)
            .Select(observation => new SpeechBatchWindow(
                observation.Window.WindowId,
                observation.Window.GuestPointer,
                observation.Lifecycle.Generation))
            .ToHashSet();
        latestVisibleBatchMembers.Clear();
        latestVisibleBatchMembers.UnionWith(visibleBatchMembers);

        var currentIdentities = observations
            .Select(observation => new SpeechBatchWindow(
                observation.Window.WindowId,
                observation.Window.GuestPointer,
                observation.Lifecycle.Generation))
            .ToHashSet();
        foreach (var staleIdentity in acknowledgedNativeBlockers.Keys
                     .Where(identity => !currentIdentities.Contains(identity))
                     .ToArray())
        {
            acknowledgedNativeBlockers.Remove(staleIdentity);
        }
        var rehydratedDeliveredOwnership = false;
        foreach (var observation in observations)
        {
            var identity = new SpeechBatchWindow(
                observation.Window.WindowId,
                observation.Window.GuestPointer,
                observation.Lifecycle.Generation);
            if (observation.Suppressed)
            {
                if (nativeOwnershipIdentity is null ||
                    !nativeOwnershipIdentity.IsValid ||
                    nativeOwnershipIdentity.WindowId != observation.Window.WindowId)
                {
                    // Suppression without exact field/window/dialog ownership
                    // cannot safely participate in native speech ordering.
                    Reset();
                    return Array.Empty<FieldVisibleWindowSpeechDispatch>();
                }

                if (nativeOwnershipDelivered &&
                    nativeOwnershipIdentity.WindowId == observation.Window.WindowId)
                {
                    pendingNativeBlockers.Remove(identity);
                    acknowledgedNativeBlockers[identity] = nativeOwnershipIdentity;
                    rehydratedDeliveredOwnership = true;
                }
                else
                {
                    // A queued lifecycle, including an immediate reopen with
                    // the same exact identity, supersedes any prior delivery.
                    acknowledgedNativeBlockers.Remove(identity);
                    pendingNativeBlockers[identity] = nativeOwnershipIdentity;
                }
            }
            else
            {
                pendingNativeBlockers.Remove(identity);
                acknowledgedNativeBlockers.Remove(identity);
            }
        }
        if (openSpeechBatch.Count != 0)
        {
            if (openSpeechBatch.Overlaps(visibleBatchMembers))
            {
                // Windows joining an existing visible set belong to its open
                // speech batch even if they need more time to stabilize.
                openSpeechBatch.Clear();
                openSpeechBatch.UnionWith(visibleBatchMembers);
            }
            else
            {
                openSpeechBatch.Clear();
            }
        }
        if (rehydratedDeliveredOwnership)
        {
            // Rehydrate after visible-set reconciliation: retained windows may
            // already have closed, but they still belong behind the native
            // utterance that was actually delivered.
            openSpeechBatch.UnionWith(visibleBatchMembers);
            openSpeechBatch.UnionWith(heldReadyWindows.Select(held => held.Identity));
        }

        var currentByIdentity = observations.ToDictionary(
            observation => new SpeechBatchWindow(
                observation.Window.WindowId,
                observation.Window.GuestPointer,
                observation.Lifecycle.Generation));
        for (var index = heldReadyWindows.Count - 1; index >= 0; index--)
        {
            var held = heldReadyWindows[index];
            if (currentByIdentity.TryGetValue(held.Identity, out var current) && current.Suppressed)
            {
                // Exact native-hook ownership supersedes a polling backlog.
                heldReadyWindows.RemoveAt(index);
                continue;
            }

            held.Blockers.RemoveWhere(blocker =>
                currentByIdentity.TryGetValue(blocker, out var currentBlocker)
                    ? currentBlocker.Suppressed
                        ? acknowledgedNativeBlockers.ContainsKey(blocker)
                        : currentBlocker.Readiness != WindowReadiness.Waiting
                    : !pendingNativeBlockers.ContainsKey(blocker));
        }

        var ready = new List<ReadyWindow>();
        var earlierBlockers = new List<SpeechBatchWindow>();
        foreach (var observation in observations)
        {
            var identity = new SpeechBatchWindow(
                observation.Window.WindowId,
                observation.Window.GuestPointer,
                observation.Lifecycle.Generation);
            if (observation.Suppressed && pendingNativeBlockers.ContainsKey(identity))
            {
                earlierBlockers.Add(identity);
                continue;
            }

            if (observation.Readiness == WindowReadiness.Waiting)
            {
                earlierBlockers.Add(identity);
                continue;
            }

            if (observation.Readiness == WindowReadiness.Ready)
            {
                var held = heldReadyWindows.FirstOrDefault(candidate =>
                    candidate.Identity == identity &&
                    string.Equals(candidate.Text, observation.Text, StringComparison.Ordinal));
                if (earlierBlockers.Count != 0)
                {
                    if (held is null)
                    {
                        heldReadyWindows.Add(new HeldReadyWindow(
                            identity,
                            observation.Window.WindowId,
                            observation.Text,
                            observation.Lifecycle,
                            earlierBlockers,
                            NextReadyChronology()));
                    }
                    else
                    {
                        held.Blockers.UnionWith(earlierBlockers);
                    }
                }
                else if (held is null)
                {
                    ready.Add(new ReadyWindow(
                        identity,
                        observation.Window.WindowId,
                        observation.Text,
                        observation.Lifecycle,
                        NextReadyChronology()));
                }
            }
        }

        var releasedHeld = heldReadyWindows
            .Where(held => held.Blockers.Count == 0)
            .ToArray();
        foreach (var held in releasedHeld)
        {
            ready.Add(new ReadyWindow(
                held.Identity,
                held.WindowId,
                held.Text,
                held.Lifecycle,
                held.Chronology));
            heldReadyWindows.Remove(held);
        }

        ready = ready
            .OrderBy(item => item.WindowId)
            .ThenBy(item => item.Chronology)
            .ToList();

        if (ready.Count == 0)
        {
            return Array.Empty<FieldVisibleWindowSpeechDispatch>();
        }

        var interruptFirst = ready[0].Lifecycle.HasDispatched ||
            !openSpeechBatch.Contains(ready[0].Identity);
        if (interruptFirst)
        {
            openSpeechBatch.Clear();
            openSpeechBatch.UnionWith(visibleBatchMembers);
        }

        var dispatches = new FieldVisibleWindowSpeechDispatch[ready.Count];
        for (var index = 0; index < ready.Count; index++)
        {
            dispatches[index] = CreatePollingDispatch(
                ready[index].Identity,
                ready[index].WindowId,
                ready[index].Text,
                ready[index].Lifecycle,
                ready[index].Chronology,
                interrupt: index == 0 && interruptFirst);
        }

        if (!requireDeliveryAcknowledgement)
        {
            foreach (var dispatch in dispatches)
            {
                AcknowledgePollingSpeech(dispatch.DispatchToken, delivered: true);
            }
        }

        return dispatches;
    }

    public void ObserveUnavailable(DateTime? now = null)
    {
        // A transient unreadable/torn scan is not evidence that any native
        // window closed. Preserve exact stable and retained text until a
        // coherent native snapshot proves a lifecycle transition.
        var unavailableAt = now ?? DateTime.UtcNow;
        foreach (var barrier in nativeSpeechBarriers.Values)
        {
            barrier.MarkUnavailable(unavailableAt);
        }
    }

    private void RetainHeldWindowLifecycles()
    {
        // Count zero can be an ASK opening frame rather than proof that every
        // retained sibling closed. Keep lifecycle generations that back held
        // speech so a same-pointer reappearance cannot escape its ASK blocker;
        // discard unretained pending lifecycles so a vanished typewriter row
        // cannot starve the native prompt.
        var retained = heldReadyWindows
            .Select(held => held.Lifecycle)
            .ToHashSet();
        foreach (var windowId in lifecycles
                     .Where(item => !retained.Contains(item.Value))
                     .Select(item => item.Key)
                     .ToArray())
        {
            lifecycles.Remove(windowId);
        }
    }

    private void EnsureNativeSpeechBoundary(
        NativeFieldMessageIdentity identity)
    {
        var boundary = pendingNativeBlockers
            .Where(blocker =>
                blocker.Value == identity &&
                blocker.Key.GuestPointer == 0)
            .Select(blocker => (SpeechBatchWindow?)blocker.Key)
            .FirstOrDefault();
        if (boundary is null)
        {
            boundary = new SpeechBatchWindow(
                identity.WindowId,
                GuestPointer: 0,
                NextLifecycleGeneration());
            pendingNativeBlockers[boundary.Value] = identity;
        }

        foreach (var later in heldReadyWindows.Where(held =>
                     held.WindowId > identity.WindowId))
        {
            later.Blockers.Add(boundary.Value);
        }
    }

    private IReadOnlyList<FieldVisibleWindowSpeechDispatch> DispatchClosedNativePredecessors(
        NativeFieldMessageIdentity? identity,
        bool requireDeliveryAcknowledgement)
    {
        if (identity is null ||
            !nativeSpeechBarriers.TryGetValue(identity, out var barrier))
        {
            return Array.Empty<FieldVisibleWindowSpeechDispatch>();
        }

        var predecessors = heldReadyWindows
            .Where(held => barrier.Contains(held.Lifecycle))
            .OrderBy(held => held.WindowId)
            .ThenBy(held => held.Chronology)
            .ToArray();
        if (predecessors.Length == 0)
        {
            return Array.Empty<FieldVisibleWindowSpeechDispatch>();
        }

        var interruptFirst = predecessors[0].Lifecycle.HasDispatched ||
            !openSpeechBatch.Contains(predecessors[0].Identity);
        openSpeechBatch.UnionWith(predecessors.Select(predecessor => predecessor.Identity));
        var dispatches = new FieldVisibleWindowSpeechDispatch[predecessors.Length];
        for (var index = 0; index < predecessors.Length; index++)
        {
            var predecessor = predecessors[index];
            heldReadyWindows.Remove(predecessor);
            dispatches[index] = CreatePollingDispatch(
                predecessor.Identity,
                predecessor.WindowId,
                predecessor.Text,
                predecessor.Lifecycle,
                predecessor.Chronology,
                interrupt: index == 0 && interruptFirst);
        }

        if (!requireDeliveryAcknowledgement)
        {
            foreach (var dispatch in dispatches)
            {
                AcknowledgePollingSpeech(dispatch.DispatchToken, delivered: true);
            }
        }

        return dispatches;
    }

    public NativeFieldMessageIdentity? AcknowledgePollingSpeech(
        long dispatchToken,
        bool delivered)
    {
        if (!pendingPollingDispatches.Remove(dispatchToken, out var pending))
        {
            return null;
        }

        if (delivered)
        {
            pending.Lifecycle.MarkDispatched(pending.Text);
            return pending.PollingRecoveryIdentity;
        }

        if (pending.Interrupt)
        {
            openSpeechBatch.Clear();
        }
        else
        {
            openSpeechBatch.Add(pending.Identity);
        }
        if (!heldReadyWindows.Any(held =>
                held.Identity == pending.Identity &&
                held.Chronology == pending.Chronology &&
                string.Equals(held.Text, pending.Text, StringComparison.Ordinal)))
        {
            heldReadyWindows.Add(new HeldReadyWindow(
                pending.Identity,
                pending.WindowId,
                pending.Text,
                pending.Lifecycle,
                Array.Empty<SpeechBatchWindow>(),
                pending.Chronology));
        }

        return null;
    }

    private FieldVisibleWindowSpeechDispatch CreatePollingDispatch(
        SpeechBatchWindow identity,
        int windowId,
        string text,
        WindowLifecycle lifecycle,
        long chronology,
        bool interrupt)
    {
        nextPollingDispatchToken = nextPollingDispatchToken == long.MaxValue
            ? 1
            : nextPollingDispatchToken + 1;
        var token = nextPollingDispatchToken;
        var pollingRecoveryIdentity = nativePollingRecoveryRequired
            .FirstOrDefault(identity =>
                nativeSpeechBarriers.TryGetValue(identity, out var barrier) &&
                barrier.IsPollingRecoveryLifecycle(lifecycle));
        pendingPollingDispatches[token] = new PendingPollingDispatch(
            identity,
            windowId,
            text,
            lifecycle,
            chronology,
            interrupt,
            pollingRecoveryIdentity);
        return new FieldVisibleWindowSpeechDispatch(
            windowId,
            lifecycle.CreateDeliveryText(text),
            interrupt,
            token,
            pollingRecoveryIdentity);
    }

    public void AcknowledgeNativeSpeech(
        NativeFieldMessageIdentity identity,
        bool visibleContentComplete = true,
        bool consumeOrderingBarrier = false)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid)
        {
            return;
        }

        if (visibleContentComplete || consumeOrderingBarrier)
        {
            nativeSpeechBarriers.Remove(identity);
            nativePollingRecoveryRequired.Remove(identity);
        }
        if (visibleContentComplete)
        {
            incompleteNativeOpenSpeech.Remove(identity);
        }
        else
        {
            incompleteNativeOpenSpeech.Add(identity);
        }

        // The native utterance is now part of the open speech batch even when
        // no suppressed polling scan bound a blocker first. This preserves the
        // ordering of retained later siblings across an active-count-zero frame.
        openSpeechBatch.UnionWith(latestVisibleBatchMembers);
        openSpeechBatch.UnionWith(heldReadyWindows.Select(held => held.Identity));
        openSpeechBatch.UnionWith(pendingNativeBlockers
            .Where(blocker => blocker.Value == identity)
            .Select(blocker => blocker.Key));
        if (lifecycles.TryGetValue(identity.WindowId, out var nativeLifecycle))
        {
            openSpeechBatch.Add(new SpeechBatchWindow(
                identity.WindowId,
                nativeLifecycle.GuestPointer,
                nativeLifecycle.Generation));
            nativeLifecycle.MarkNativeDispatched(visibleContentComplete);
        }

        if (!visibleContentComplete && !consumeOrderingBarrier)
        {
            // The prompt alone does not cover the selected ASK row. Keep its
            // ordinary window blocker in place until a native choice completes
            // the lifecycle or polling recovers the full visible window; later
            // siblings must not jump between those two pieces of information.
            return;
        }

        var acknowledged = pendingNativeBlockers
            .Where(blocker => blocker.Value == identity)
            .Select(blocker => blocker.Key)
            .ToArray();
        foreach (var blocker in acknowledged)
        {
            pendingNativeBlockers.Remove(blocker);
            acknowledgedNativeBlockers[blocker] = identity;
        }

        if (acknowledged.Length == 0)
        {
            // A scan may have been skipped before the native utterance. The
            // tracker-provided delivered state will bind the exact identity on
            // the next suppressed observation.
            return;
        }

        var acknowledgedWindows = acknowledged.ToHashSet();
        foreach (var held in heldReadyWindows)
        {
            held.Blockers.RemoveWhere(acknowledgedWindows.Contains);
        }
    }

    public void ReleaseNativeSpeechOrderingOnly(
        NativeFieldMessageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid)
        {
            return;
        }

        var released = pendingNativeBlockers
            .Where(blocker => blocker.Value == identity)
            .Select(blocker => blocker.Key)
            .ToHashSet();
        foreach (var blocker in released)
        {
            pendingNativeBlockers.Remove(blocker);
        }

        foreach (var held in heldReadyWindows)
        {
            held.Blockers.RemoveWhere(released.Contains);
        }

        // Some native content was heard, so later already-known siblings must
        // queue behind it. Do not mark the native window's full text delivered:
        // failed/overflowed choice content must remain eligible for polling.
        openSpeechBatch.UnionWith(latestVisibleBatchMembers);
        openSpeechBatch.UnionWith(heldReadyWindows.Select(held => held.Identity));
    }

    public void CancelNativeSpeech(NativeFieldMessageIdentity? identity)
    {
        if (identity is null || !identity.IsValid)
        {
            return;
        }

        nativeSpeechBarriers.Remove(identity);
        incompleteNativeOpenSpeech.Remove(identity);
        nativePollingRecoveryRequired.Remove(identity);

        var canceledWindows = pendingNativeBlockers
            .Where(blocker => blocker.Value == identity)
            .Select(blocker => blocker.Key)
            .ToHashSet();
        foreach (var blocker in canceledWindows)
        {
            pendingNativeBlockers.Remove(blocker);
            acknowledgedNativeBlockers.Remove(blocker);
            openSpeechBatch.Remove(blocker);
        }

        foreach (var blocker in acknowledgedNativeBlockers
                     .Where(blocker => blocker.Value == identity)
                     .Select(blocker => blocker.Key)
                     .ToArray())
        {
            acknowledgedNativeBlockers.Remove(blocker);
            openSpeechBatch.Remove(blocker);
            canceledWindows.Add(blocker);
        }

        foreach (var held in heldReadyWindows)
        {
            held.Blockers.RemoveWhere(canceledWindows.Contains);
        }
    }

    public void CancelAllNativeSpeech()
    {
        var canceledWindows = pendingNativeBlockers.Keys
            .Concat(acknowledgedNativeBlockers.Keys)
            .ToHashSet();
        pendingNativeBlockers.Clear();
        acknowledgedNativeBlockers.Clear();
        nativeSpeechBarriers.Clear();
        incompleteNativeOpenSpeech.Clear();
        nativePollingRecoveryRequired.Clear();
        foreach (var held in heldReadyWindows)
        {
            held.Blockers.RemoveWhere(canceledWindows.Contains);
        }

        openSpeechBatch.RemoveWhere(canceledWindows.Contains);
    }

    public void BeginNativeMessageLifecycle(NativeFieldMessageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid || identity.Kind != FieldOpcodeKind.Message)
        {
            return;
        }

        BeginNativeLifecycle(identity, createSpeechBarrier: false);
    }

    public void BeginNativeAskLifecycle(
        NativeFieldMessageIdentity identity,
        bool requireCoherentObservation = false,
        DateTime? now = null,
        TimeSpan? maximumObservationWait = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid || identity.Kind != FieldOpcodeKind.Ask)
        {
            return;
        }

        BeginNativeLifecycle(
            identity,
            createSpeechBarrier: true,
            requireCoherentObservation,
            now ?? DateTime.UtcNow,
            maximumObservationWait ?? TimeSpan.FromMilliseconds(250));
    }

    public void RequirePollingRecoveryBeforeNativeChoice(
        NativeFieldMessageIdentity identity,
        bool pollingAvailable = true,
        DateTime? now = null,
        TimeSpan? maximumWait = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid || identity.Kind != FieldOpcodeKind.Ask)
        {
            return;
        }

        if (!nativeSpeechBarriers.TryGetValue(identity, out var barrier))
        {
            var predecessors = lifecycles
                .Where(item =>
                    item.Key < identity.WindowId &&
                    item.Value.HasPendingUndispatched)
                .Select(item => item.Value)
                .Concat(heldReadyWindows
                    .Where(held => held.WindowId < identity.WindowId)
                    .Select(held => held.Lifecycle))
                .Distinct()
                .ToArray();
            barrier = new NativeSpeechBarrier(
                predecessors,
                mustQueueBehindOpenSpeech: openSpeechBatch.Count != 0,
                observationRequired: false,
                DateTime.UtcNow,
                TimeSpan.Zero);
            nativeSpeechBarriers[identity] = barrier;
        }

        barrier.RequirePollingRecovery(
            pollingAvailable,
            now ?? DateTime.UtcNow,
            maximumWait ?? TimeSpan.FromSeconds(1));
        if (pollingAvailable)
        {
            nativePollingRecoveryRequired.Add(identity);
        }
    }

    private void BeginNativeLifecycle(
        NativeFieldMessageIdentity identity,
        bool createSpeechBarrier,
        bool requireCoherentObservation = false,
        DateTime? now = null,
        TimeSpan? maximumObservationWait = null)
    {
        if (createSpeechBarrier)
        {
            var predecessors = lifecycles
                .Where(item =>
                    item.Key <= identity.WindowId &&
                    item.Value.HasPendingUndispatched)
                .Select(item => item.Value)
                .Concat(heldReadyWindows
                    .Where(held => held.WindowId <= identity.WindowId)
                    .Select(held => held.Lifecycle))
                .Distinct()
                .ToArray();
            var mustQueueBehindOpenSpeech = openSpeechBatch.Count != 0;
            nativeSpeechBarriers[identity] = new NativeSpeechBarrier(
                predecessors,
                mustQueueBehindOpenSpeech,
                requireCoherentObservation,
                now ?? DateTime.UtcNow,
                maximumObservationWait ?? TimeSpan.FromMilliseconds(250));
        }

        if (lifecycles.TryGetValue(identity.WindowId, out var lifecycle))
        {
            lifecycles[identity.WindowId] = new WindowLifecycle(
                lifecycle.GuestPointer,
                NextLifecycleGeneration());
        }

        foreach (var blocker in pendingNativeBlockers.Keys
                     .Where(blocker => blocker.WindowId == identity.WindowId)
                     .ToArray())
        {
            pendingNativeBlockers.Remove(blocker);
        }

        foreach (var blocker in acknowledgedNativeBlockers.Keys
                     .Where(blocker => blocker.WindowId == identity.WindowId)
                     .ToArray())
        {
            acknowledgedNativeBlockers.Remove(blocker);
        }

        openSpeechBatch.RemoveWhere(window => window.WindowId == identity.WindowId);
    }

    public bool CanDispatchNativeSpeech(
        NativeFieldMessageIdentity identity,
        DateTime now,
        out bool interrupt)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!nativeSpeechBarriers.TryGetValue(identity, out var barrier))
        {
            interrupt = true;
            return true;
        }

        if (barrier.HasUnfinishedPredecessor(
                lifecycles.Values,
                heldReadyWindows,
                now))
        {
            interrupt = false;
            return false;
        }

        // Reservation is consumed only after successful Prism delivery. Until
        // then retries must observe lower windows and current speech afresh.
        interrupt = !barrier.HadPredecessors && openSpeechBatch.Count == 0;
        return true;
    }

    public bool CanDispatchNativeSpeech(
        NativeFieldMessageIdentity identity,
        out bool interrupt) =>
        CanDispatchNativeSpeech(identity, DateTime.UtcNow, out interrupt);

    private long NextReadyChronology()
    {
        if (nextReadyChronology == long.MaxValue)
        {
            long normalized = 0;
            foreach (var held in heldReadyWindows.OrderBy(held => held.Chronology))
            {
                held.Chronology = ++normalized;
            }

            nextReadyChronology = normalized;
        }

        return ++nextReadyChronology;
    }

    private long NextLifecycleGeneration()
    {
        nextLifecycleGeneration = nextLifecycleGeneration == long.MaxValue
            ? 1
            : nextLifecycleGeneration + 1;
        return nextLifecycleGeneration;
    }

    public void Reset()
    {
        lifecycles.Clear();
        openSpeechBatch.Clear();
        heldReadyWindows.Clear();
        pendingNativeBlockers.Clear();
        acknowledgedNativeBlockers.Clear();
        nativeSpeechBarriers.Clear();
        incompleteNativeOpenSpeech.Clear();
        nativePollingRecoveryRequired.Clear();
        pendingPollingDispatches.Clear();
        latestVisibleBatchMembers.Clear();
    }

    private void ResetVisibleSetPreservingPendingNativeBarrier(
        NativeFieldMessageIdentity? identity,
        bool nativeSpeechPending)
    {
        NativeSpeechBarrier? preserved = null;
        var preserveIncompleteOpenSpeech = false;
        var preservePollingRecovery = false;
        if (nativeSpeechPending && identity is not null)
        {
            nativeSpeechBarriers.TryGetValue(identity, out preserved);
            preserveIncompleteOpenSpeech = incompleteNativeOpenSpeech.Contains(identity);
            preservePollingRecovery = nativePollingRecoveryRequired.Contains(identity);
        }

        Reset();
        if (preserved is not null && identity is not null)
        {
            nativeSpeechBarriers[identity] = preserved;
        }
        if (preserveIncompleteOpenSpeech && identity is not null)
        {
            incompleteNativeOpenSpeech.Add(identity);
        }
        if (preservePollingRecovery && identity is not null)
        {
            nativePollingRecoveryRequired.Add(identity);
        }
    }

    private readonly record struct SpeechBatchWindow(
        int WindowId,
        uint GuestPointer,
        long LifecycleGeneration);

    private readonly record struct ReadyWindow(
        SpeechBatchWindow Identity,
        int WindowId,
        string Text,
        WindowLifecycle Lifecycle,
        long Chronology);

    private readonly record struct PendingPollingDispatch(
        SpeechBatchWindow Identity,
        int WindowId,
        string Text,
        WindowLifecycle Lifecycle,
        long Chronology,
        bool Interrupt,
        NativeFieldMessageIdentity? PollingRecoveryIdentity);

    private sealed class HeldReadyWindow
    {
        public HeldReadyWindow(
            SpeechBatchWindow identity,
            int windowId,
            string text,
            WindowLifecycle lifecycle,
            IEnumerable<SpeechBatchWindow> blockers,
            long chronology)
        {
            Identity = identity;
            WindowId = windowId;
            Text = text;
            Lifecycle = lifecycle;
            Blockers = blockers.ToHashSet();
            Chronology = chronology;
        }

        public SpeechBatchWindow Identity { get; }

        public int WindowId { get; }

        public string Text { get; }

        public WindowLifecycle Lifecycle { get; }

        public HashSet<SpeechBatchWindow> Blockers { get; }

        public long Chronology { get; set; }
    }

    private sealed class NativeSpeechBarrier
    {
        private readonly HashSet<WindowLifecycle> predecessors = [];
        private readonly bool observationRequired;
        private readonly DateTime observationDeadline;
        private readonly TimeSpan maximumObservationWait;
        private bool observed;
        private DateTime? unavailableSince;
        private bool pollingRecoveryRequired;
        private WindowLifecycle? pollingRecoveryLifecycle;
        private DateTime pollingRecoveryDeadline;

        public NativeSpeechBarrier(
            IEnumerable<WindowLifecycle> predecessors,
            bool mustQueueBehindOpenSpeech,
            bool observationRequired,
            DateTime now,
            TimeSpan maximumObservationWait)
        {
            this.observationRequired = observationRequired;
            this.maximumObservationWait = maximumObservationWait < TimeSpan.Zero
                ? TimeSpan.Zero
                : maximumObservationWait;
            observationDeadline = now + this.maximumObservationWait;
            observed = !observationRequired;
            HadPredecessors = mustQueueBehindOpenSpeech;
            foreach (var predecessor in predecessors)
            {
                Add(predecessor);
            }
        }

        public bool HadPredecessors { get; private set; }

        public void Add(WindowLifecycle lifecycle)
        {
            if (predecessors.Add(lifecycle))
            {
                HadPredecessors = true;
            }
        }

        public bool Contains(WindowLifecycle lifecycle) =>
            predecessors.Contains(lifecycle);

        public void MarkObserved(DateTime now)
        {
            observed = true;
            unavailableSince = null;
        }

        public void MarkUnavailable(DateTime now)
        {
            unavailableSince ??= now;
        }

        public void RequirePollingRecovery(
            bool pollingAvailable,
            DateTime now,
            TimeSpan maximumWait)
        {
            maximumWait = maximumWait < TimeSpan.Zero
                ? TimeSpan.Zero
                : maximumWait;
            pollingRecoveryRequired = pollingAvailable;
            pollingRecoveryDeadline = now + maximumWait;
            HadPredecessors = true;
        }

        public void BindPollingRecovery(WindowLifecycle lifecycle)
        {
            pollingRecoveryLifecycle = lifecycle;
            HadPredecessors = true;
        }

        public bool IsPollingRecoveryLifecycle(WindowLifecycle lifecycle) =>
            ReferenceEquals(pollingRecoveryLifecycle, lifecycle);

        public bool HasUnfinishedPredecessor(
            IEnumerable<WindowLifecycle> activeLifecycles,
            IEnumerable<HeldReadyWindow> heldWindows,
            DateTime now)
        {
            if (pollingRecoveryRequired &&
                (pollingRecoveryLifecycle is null ||
                 !pollingRecoveryLifecycle.HasDispatched ||
                 pollingRecoveryLifecycle.HasPendingUndispatched) &&
                now < pollingRecoveryDeadline)
            {
                return true;
            }
            if (pollingRecoveryRequired && now >= pollingRecoveryDeadline)
            {
                // Polling may be configured but unreadable. Do not permanently
                // silence the only exact highlighted-choice signal; after a
                // bounded wait it may continue noninterrupting.
                HadPredecessors = true;
            }

            if (unavailableSince is { } unavailableAt &&
                now - unavailableAt >= maximumObservationWait)
            {
                // A coherent scan made progress earlier, but stale lifecycle
                // objects cannot block an exact native ASK through an extended
                // reader outage. Preserve ordering by forcing noninterrupt.
                HadPredecessors = true;
                return false;
            }

            if (observationRequired && !observed)
            {
                if (now < observationDeadline)
                {
                    return true;
                }

                // The native ASK remains exact even when the polling reader is
                // unavailable. Bound the ordering wait and queue rather than
                // interrupting any potentially active speech.
                HadPredecessors = true;
                return false;
            }

            var active = activeLifecycles.ToHashSet();
            var held = heldWindows.Select(window => window.Lifecycle).ToHashSet();
            return predecessors.Any(predecessor =>
                predecessor.HasPendingUndispatched &&
                (active.Contains(predecessor) || held.Contains(predecessor)));
        }
    }

    private enum WindowReadiness
    {
        Completed,
        Waiting,
        Ready
    }

    private sealed class WindowLifecycle
    {
        private string pendingText = string.Empty;
        private DateTime pendingSince = DateTime.MinValue;
        private string lastDispatchedText = string.Empty;
        private bool bypassed;
        private string bypassedText = string.Empty;
        private bool bypassedSpeechDelivered;

        public WindowLifecycle(uint guestPointer, long generation)
        {
            GuestPointer = guestPointer;
            Generation = generation;
        }

        public bool HasDispatched => lastDispatchedText.Length != 0;

        public bool HasPendingUndispatched =>
            pendingText.Length != 0 &&
            !string.Equals(pendingText, lastDispatchedText, StringComparison.Ordinal);

        public uint GuestPointer { get; }

        public long Generation { get; }

        public WindowReadiness Observe(
            FieldVisibleWindowSnapshot window,
            bool suppressed,
            bool nativeSpeechDelivered,
            TimeSpan stableWindow,
            DateTime now,
            out string text)
        {
            text = Ff7EncodedTextDecoder.NormalizeWhitespace(window.Text);
            if (suppressed)
            {
                bypassed = true;
                bypassedText = text;
                bypassedSpeechDelivered = nativeSpeechDelivered;
                if (nativeSpeechDelivered && text.Length != 0)
                {
                    lastDispatchedText = text;
                }

                return WindowReadiness.Completed;
            }

            if (bypassed)
            {
                var sameDeliveredNativeText = bypassedSpeechDelivered &&
                    string.Equals(text, bypassedText, StringComparison.Ordinal);
                bypassed = false;
                bypassedText = string.Empty;
                bypassedSpeechDelivered = false;
                pendingText = string.Empty;
                pendingSince = DateTime.MinValue;
                if (sameDeliveredNativeText)
                {
                    lastDispatchedText = text;
                    return WindowReadiness.Completed;
                }

                lastDispatchedText = string.Empty;
            }

            if (text.Length == 0)
            {
                pendingText = string.Empty;
                pendingSince = DateTime.MinValue;
                return WindowReadiness.Completed;
            }

            if (string.Equals(text, lastDispatchedText, StringComparison.Ordinal))
            {
                pendingText = text;
                pendingSince = now;
                return WindowReadiness.Completed;
            }

            if (!string.Equals(text, pendingText, StringComparison.Ordinal))
            {
                pendingText = text;
                pendingSince = now;
                return WindowReadiness.Waiting;
            }

            return now - pendingSince < stableWindow
                ? WindowReadiness.Waiting
                : WindowReadiness.Ready;
        }

        public void MarkDispatched(string text)
        {
            lastDispatchedText = text;
        }

        public string CreateDeliveryText(string text) =>
            VisibleTextContinuation.SelectDeliveryText(lastDispatchedText, text);

        public void MarkNativeDispatched(bool visibleContentComplete)
        {
            if (!visibleContentComplete)
            {
                return;
            }

            bypassedSpeechDelivered = true;
            if (bypassedText.Length != 0)
            {
                lastDispatchedText = bypassedText;
            }
        }

    }
}

internal static class FieldWindowPollingOwnership
{
    public static bool IsSuppressed(
        FieldVisibleWindowSnapshot window,
        NativeFieldMessageIdentity? activeIdentity,
        NativeFieldMessageOwnershipTracker ownership,
        byte activeMessageCount,
        DateTime now,
        bool nativeSpeechPending = false)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        return ownership.ShouldSuppressPolling(
            window.WindowId,
            activeIdentity,
            activeMessageCount,
            now,
            nativeSpeechPending);
    }
}

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate void MenuTextRendererDelegate(byte* text, uint x, uint y, int color, int context);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate void InGameMenuTextDrawDelegate(int x, int y, byte* text, int color, int context);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void MenuCursorDrawDelegate(int x, int y, int context);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate void MenuWidgetUpdateDelegate(int* widget);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int FieldMessageOpenDelegate(short windowIndex, short messageId);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int FieldMessagePreviewDelegate(short messageId);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int FieldOpcodeMessageDelegate();

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int FieldOpcodeTimerDelegate();

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int FieldOpcodeAskDelegate(int arg);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int FieldOpcodeAskUpdateDelegate(
    byte windowId,
    byte dialogId,
    byte firstQuestionLine,
    byte lastQuestionLine,
    IntPtr currentQuestionLinePointer);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int FieldOpcodeWaitDelegate();

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int FieldOpcodeSoundDelegate();

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int FieldOpcodeCutsceneDelegate();

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
public unsafe delegate bool FfnxPlayVoiceDelegate(
    byte* fieldName,
    byte windowId,
    byte dialogId,
    byte page);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BattleMenuRenderDelegate(int context, short rendererState);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BattleUpdateDelegate();

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BattleTextActiveDelegate(short bufferIndex);

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BattleResultsUpdateDelegate();

[global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BattleDamageDisplayDelegate();
