using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.World;

internal static class Steam2026WorldMapTerrainPriorityTests
{
    internal static void Run()
    {
        AConsumedAutoWalkToggleStillDefersTerrainSpeech();
        AProgressControlUtteranceWithoutAnActiveRouteDefersTerrainSpeech();
        AFailedStateReadStillPreservesTheSpeechQuietPeriod();
        ARejectedRichMarshCueFallsBackToNativeTerrainSpeech();
        AConfirmedWorldAreaRemainsSpeechOnly();
        TheWorldEntranceCueDoesNotActivateDormantFieldTransitions();
        TheX64WorldEntranceCueUsesTheFullDirectionalSteamAudioRender();
        TheX64HostRepeatsADirectionalCueTowardJunonsNativeEntrance();
        ControllerBumpersCycleWorldCategoriesAndKeepSpeechPriority();
    }

    private static void ControllerBumpersCycleWorldCategoriesAndKeepSpeechPriority()
    {
        var spoken = new List<string>();
        var memory = SeedWorldMemory(terrainId: 0);
        var held = new HashSet<int>();
        var now = Epoch;
        using var hook = Steam2026SdlControllerCaptureHook.CreateForDecideTest(
            installed => new ControllerNavigationCapture(
                suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
                installed),
            (_, button) => (byte)(held.Contains(button) ? 1 : 0),
            _ => 1,
            () => now);
        using var coordinator = CreateCoordinator(
            memory, new MutableInput(),
            new NavigationAutoWalkController(new AcceptingKeyboardSink()),
            (text, _) => spoken.Add(text),
            controllerCapture: () => hook.Capture);
        using var inactiveField = new Steam2026FieldNavigationCoordinator(
            new AccessibilityConfig
            {
                EnableFieldNavigationAssistant = true,
                EnableFieldExitProximityCues = false,
                EnableFieldLadderProximityCues = false,
                EnableFieldSwingingBarTimingCue = false,
                EnableSquatMinigamePrompts = false,
                EnableJunonMinigamePrompts = false,
                EnableJunonParadeAlignmentAssist = false,
                EnableFloor60SoldierTurnCue = false
            },
            memory,
            new MutableInput().CreateAdapter(),
            new Steam2026FieldObjectObservationReader(memory, _ => null, _ => null,
                Array.Empty<FieldNavigationObjectDefinition>()),
            Path.GetTempPath(), AppContext.BaseDirectory,
            (_, _) => { }, _ => { },
            autoWalk: new NavigationAutoWalkController(new AcceptingKeyboardSink()),
            controllerCapture: () => hook.Capture);

        void Frame(int milliseconds, int? button = null)
        {
            now = Epoch.AddMilliseconds(milliseconds);
            held.Clear();
            if (button.HasValue) held.Add(button.Value);
            // The game need not ask about R3 or either bumper for the hook to see them.
            _ = hook.InvokeGetButtonForTest(0x1234, 0);
            Observe(coordinator, now);
            // Match the host's real order: world coordinator first, inactive field
            // coordinator afterward. A stand-in PublishUnavailable call missed the
            // unconditional RequestClose inside the field coordinator's Suspend.
            inactiveField.Observe(WorldFrame(now), now);
        }

        Frame(0);
        Frame(300);
        Frame(600);
        Frame(900);
        Frame(1100, 8); // R3
        Equal(true, hook.Capture.IsOpen, "world controller menu opens");
        Equal(ControllerNavigationDomain.WorldMap, hook.Capture.Owner,
            "inactive field coordinator cannot take the world menu");
        Frame(1300);
        Equal(true, hook.Capture.IsOpen,
            "the world menu survives the next input poll after the inactive field tick");
        spoken.Clear();

        Frame(1500, 10); // R1
        Equal(true, spoken.Last().StartsWith("Story", StringComparison.Ordinal), "R1 selects Story");
        Frame(1700);
        Frame(1900, 10);
        Equal(true, spoken.Last().StartsWith("Transportation", StringComparison.Ordinal),
            "a second R1 selects Transportation");
        var afterPress = spoken.Count;
        Frame(2100, 10);
        Frame(2300, 10);
        Equal(afterPress, spoken.Count, "holding R1 does not skip categories");
        Frame(2500);
        Frame(2700, 9); // L1
        Equal(true, spoken.Last().StartsWith("Story", StringComparison.Ordinal), "L1 returns to Story");
        Frame(2900);

        // Terrain becomes stable on the same scan as the deliberate category press.
        // Its interrupting speech must wait, just as it does for keyboard navigation.
        memory.SetTerrain(1);
        Frame(3200);
        Frame(3500);
        Frame(3800);
        spoken.Clear();
        Frame(4100, 10);
        Equal(true, spoken.Any(text => text.StartsWith("Transportation", StringComparison.Ordinal)),
            "the bumper reaches the world navigation service");
        DoesNotContain("Entered forest.", spoken,
            "terrain cannot interrupt the controller category announcement");
        Frame(4300);
        Frame(4700);
        Contains("Left grass. Entered forest.", spoken,
            "deferred terrain still speaks after controller navigation is quiet");

        var nextPress = 4900;
        foreach (var category in new[] { "Events", "Chocobo Tracks", "Regions", "Locations", "Story", "Transportation" })
        {
            spoken.Clear();
            Frame(nextPress, 10);
            Equal(true, spoken.Any(text => text.StartsWith(category, StringComparison.Ordinal)),
                $"R1 cycles through {category}, including empty categories");
            Frame(nextPress + 200);
            nextPress += 400;
        }

        now = Epoch.AddMilliseconds(nextPress);
        var battle = WorldFrame(now) with
        {
            Lifecycle = new GameLifecycleObservation(true, false, BattleStateReader.BattleModule, 1)
        };
        coordinator.Observe(battle, now);
        inactiveField.Observe(battle, now);
        _ = hook.InvokeGetButtonForTest(0x1234, 0);
        Equal(false, hook.Capture.IsOpen, "entering battle closes the world menu immediately");
        held.Add(10);
        Equal((byte)1, hook.InvokeGetButtonForTest(0x1234, 10),
            "the battle receives its own controller buttons");
        held.Clear();
        _ = hook.InvokeGetButtonForTest(0x1234, 0);
        held.Add(8);
        _ = hook.InvokeGetButtonForTest(0x1234, 0);
        Equal(false, hook.Capture.IsOpen,
            "a fresh R3 cannot reopen world navigation during the battle freshness window");

        Frame(nextPress + 200);
        Frame(nextPress + 400, 8);
        Equal(true, hook.Capture.IsOpen, "world navigation can reopen after returning from battle");
        Frame(nextPress + 600);
        memory.ReadsSucceed = false;
        Frame(nextPress + 800);
        _ = hook.InvokeGetButtonForTest(0x1234, 0);
        Equal(false, hook.Capture.IsOpen, "an unreadable world snapshot retires its menu immediately");
        memory.ReadsSucceed = true;

        // Check the reverse handoff too: the inactive world coordinator cannot
        // invalidate a fresh field menu when it resets its former world session.
        now = Epoch.AddMilliseconds(nextPress + 1000);
        held.Clear();
        hook.Capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, now, 433);
        _ = hook.InvokeGetButtonForTest(0x1234, 0);
        held.Add(8);
        _ = hook.InvokeGetButtonForTest(0x1234, 0);
        Equal(true, hook.Capture.IsOpen, "the field opens its own menu after leaving the world map");
        coordinator.Observe(WorldFrame(now) with
        {
            Lifecycle = new GameLifecycleObservation(true, false, FieldPositionReader.FieldModule, 2)
        }, now);
        _ = hook.InvokeGetButtonForTest(0x1234, 0);
        Equal(true, hook.Capture.IsOpen, "an inactive world reset leaves the field menu open");
        Equal(ControllerNavigationDomain.Field, hook.Capture.Owner,
            "an inactive world reset preserves the field context");
    }

    private static void AProgressControlUtteranceWithoutAnActiveRouteDefersTerrainSpeech()
    {
        var spoken = new List<string>();
        var input = new MutableInput();
        var memory = SeedWorldMemory(terrainId: 0);
        var autoWalk = new NavigationAutoWalkController(new AcceptingKeyboardSink());
        var progressController = new NavigationProgressController(enabled: true, intervalPercent: 5);
        using var coordinator = CreateCoordinator(
            memory,
            input,
            autoWalk,
            (text, _) => spoken.Add(text),
            progressController);
        var start = Epoch;

        EstablishSurface(coordinator, start);
        spoken.Clear();
        memory.SetTerrain(1);
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1200));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1500));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1800));

        spoken.Add(progressController.HandleAction(NavigationProgressHotkeyAction.Toggle));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(2100));
        DoesNotContain("Entered forest.", spoken, "terrain does not talk over F5 control speech without a route");

        Observe(coordinator, start + TimeSpan.FromMilliseconds(2500));
        DoesNotContain("Entered forest.", spoken, "terrain remains deferred for the control-speech quiet period");
        Observe(coordinator, start + TimeSpan.FromMilliseconds(2700));
        Contains("Left grass. Entered forest.", spoken, "stable terrain speaks after progress-control speech is quiet");
    }

    private static void AConsumedAutoWalkToggleStillDefersTerrainSpeech()
    {
        var spoken = new List<string>();
        var input = new MutableInput();
        var memory = SeedWorldMemory(terrainId: 0);
        var autoWalk = new NavigationAutoWalkController(new AcceptingKeyboardSink());
        using var coordinator = CreateCoordinator(memory, input, autoWalk, (text, _) => spoken.Add(text));
        var start = Epoch;

        EstablishSurface(coordinator, start);
        spoken.Clear();
        memory.SetTerrain(1);
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1200));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1500));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1800));

        Equal(
            true,
            autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true),
            "test auto walk starts");
        input.Pressed = true;
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1810));
        input.Pressed = false;

        Contains("Auto walk off.", spoken, "toggle-off acknowledgement");
        DoesNotContain("Entered forest.", spoken, "terrain does not talk over the toggle");

        Observe(coordinator, start + TimeSpan.FromMilliseconds(2020));
        DoesNotContain("Entered forest.", spoken, "terrain remains deferred during the quiet period");
        Observe(coordinator, start + TimeSpan.FromMilliseconds(2420));
        Contains("Left grass. Entered forest.", spoken, "latest stable terrain speaks after quiet");
    }

    private static void AFailedStateReadStillPreservesTheSpeechQuietPeriod()
    {
        var spoken = new List<string>();
        var terrainAttempts = 0;
        var failNextTerrainSpeech = false;
        var input = new MutableInput();
        var memory = SeedWorldMemory(terrainId: 0);
        var autoWalk = new NavigationAutoWalkController(new AcceptingKeyboardSink());
        using var coordinator = CreateCoordinator(
            memory,
            input,
            autoWalk,
            (text, _) =>
            {
                if (text.Contains("Entered forest.", StringComparison.Ordinal))
                {
                    terrainAttempts++;
                    if (failNextTerrainSpeech)
                    {
                        failNextTerrainSpeech = false;
                        throw new InvalidOperationException("simulated speech boundary failure");
                    }
                }

                spoken.Add(text);
            });
        var start = Epoch;

        EstablishSurface(coordinator, start);
        spoken.Clear();
        memory.SetTerrain(1);
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1200));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1500));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1800));
        failNextTerrainSpeech = true;
        Observe(coordinator, start + TimeSpan.FromMilliseconds(2100));
        Equal(1, terrainAttempts, "failed terrain utterance remains pending");

        Equal(
            true,
            autoWalk.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true),
            "test auto walk restarts");
        input.Pressed = true;
        memory.ReadsSucceed = false;
        Observe(coordinator, start + TimeSpan.FromMilliseconds(2110));
        input.Pressed = false;
        memory.ReadsSucceed = true;
        Contains("Auto walk off.", spoken, "toggle acknowledgement survives a failed state read");

        Observe(coordinator, start + TimeSpan.FromMilliseconds(2320));
        Equal(1, terrainAttempts, "failed read carries the higher-priority quiet period");
        Observe(coordinator, start + TimeSpan.FromMilliseconds(2720));
        Equal(2, terrainAttempts, "pending terrain retries after the quiet period");
        Contains("Left grass. Entered forest.", spoken, "terrain retry is eventually delivered");
    }

    private static void ARejectedRichMarshCueFallsBackToNativeTerrainSpeech()
    {
        var spoken = new List<string>();
        var richCueAttempts = 0;
        var input = new MutableInput();
        var memory = SeedWorldMemory(terrainId: 0);
        var autoWalk = new NavigationAutoWalkController(new AcceptingKeyboardSink());
        using var coordinator = CreateCoordinator(
            memory,
            input,
            autoWalk,
            (text, _) =>
            {
                if (string.Equals(text, MidgarZolomAreaTracker.EnteredText, StringComparison.Ordinal))
                {
                    richCueAttempts++;
                    throw new InvalidOperationException("simulated rejected rich marsh cue");
                }

                spoken.Add(text);
            });
        var start = Epoch;

        EstablishSurface(coordinator, start);
        spoken.Clear();
        memory.SetTerrain(MidgarZolomAreaTracker.MarshTerrainId);
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1200));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1500));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1800));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(2100));

        Equal(1, richCueAttempts, "native terrain drives the rich marsh attempt once");
        Contains(
            "Left grass. Entered swamp.",
            spoken,
            "generic native terrain prevents silence when the rich cue is rejected");
    }

    private static void AConfirmedWorldAreaRemainsSpeechOnly()
    {
        var spoken = new List<string>();
        var entranceCues = new List<NavigationBeaconCue>();
        var input = new MutableInput();
        var memory = SeedWorldMemory(terrainId: 0);
        memory.SetSurface(terrainId: 0, regionId: 1);
        using var coordinator = CreateCoordinator(
            memory,
            input,
            new NavigationAutoWalkController(new AcceptingKeyboardSink()),
            (text, _) => spoken.Add(text),
            enableWorldMapEntranceProximityCues: true,
            playEntranceCue: (cue, _) => entranceCues.Add(cue));
        var start = Epoch;

        EstablishSurface(coordinator, start);
        spoken.Clear();
        entranceCues.Clear();
        memory.SetSurface(terrainId: 0, regionId: 2);
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1200));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1500));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(1800));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(2100));

        Contains(
            "Left Grasslands Area. Entered Junon Area.",
            spoken,
            "the x64 host delivers confirmed area speech");
        Equal(0, entranceCues.Count, "crossing an area boundary does not reuse the entrance sound");
    }

    private static void TheWorldEntranceCueDoesNotActivateDormantFieldTransitions()
    {
        var fieldCues = new List<string>();
        var memory = new MutableWorldMemory();
        memory.WriteByte((uint)FieldPositionReader.AddressCurrentModule, FieldPositionReader.FieldModule);
        memory.WriteUInt16((uint)FieldPositionReader.AddressFieldId, 10);
        var config = new AccessibilityConfig
        {
            EnableFieldZoneTransitionCue = false,
            EnableWorldMapEntranceProximityCues = true,
            FieldZoneTransitionCueSettleMs = 0
        };
        using var coordinator = new Steam2026FieldZoneTransitionCueCoordinator(
            config,
            memory,
            AppContext.BaseDirectory,
            _ => { },
            playFieldCue: reason => fieldCues.Add(reason));

        coordinator.Observe(isHostForeground: true, Epoch);
        coordinator.Observe(isHostForeground: true, Epoch + TimeSpan.FromMilliseconds(1));
        memory.WriteUInt16((uint)FieldPositionReader.AddressFieldId, 11);
        coordinator.Observe(isHostForeground: true, Epoch + TimeSpan.FromMilliseconds(2));
        coordinator.Observe(isHostForeground: true, Epoch + TimeSpan.FromMilliseconds(3));

        Equal(0, fieldCues.Count, "the default-on entrance key does not enable field transition sounds");
    }

    private static void TheX64WorldEntranceCueUsesTheFullDirectionalSteamAudioRender()
    {
        var cue = new NavigationBeaconCue(
            "Junon",
            "proximity",
            StickX: 1f,
            StickY: 0f,
            SteamAudioX: 1f,
            SteamAudioY: 0f,
            SteamAudioZ: 0f,
            NavigationBeaconMovementState.OnCourse,
            DurationMs: 220,
            DistanceUnits: 1_024d);

        var outputDirectory = Path.GetDirectoryName(typeof(NavigationBeaconPlayer).Assembly.Location)
            ?? throw new InvalidOperationException("Could not resolve x64 entrance-cue output directory.");
        var mono = NavigationBeaconSound.LoadMonoSamples(
            Path.Combine(outputDirectory, "Assets", "navigation", "field_zone_transition.wav"),
            expectedSampleRate: 44100);
        Equal(131382, mono.Length, "x64 complete field-zone WAV frame count");
        var rendered = RenderCue(cue, mono);
        Equal(mono.Length * 2, rendered.Length, "x64 directional HRTF render preserves the complete WAV duration");

        var frameCount = rendered.Length / 2;
        var leftSquares = 0d;
        var rightSquares = 0d;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var left = rendered[frame * 2];
            var right = rendered[frame * 2 + 1];
            leftSquares += left * (double)left;
            rightSquares += right * (double)right;
        }

        var leftRms = Math.Sqrt(leftSquares / frameCount);
        var rightRms = Math.Sqrt(rightSquares / frameCount);
        var louder = Math.Max(leftRms, rightRms);
        var imbalance = louder <= 0d ? 0d : Math.Abs(leftRms - rightRms) / louder;
        Equal(true, louder > 0.01d, $"x64 directional entrance cue remains audible, rms={louder:0.000000}");
        Equal(true, imbalance > 0.05d,
            $"x64 entrance cue keeps meaningful directional HRTF energy, left={leftRms:0.000000}, right={rightRms:0.000000}");

        var differsFromDuplicatedMono = false;
        for (var frame = 0; frame < mono.Length; frame++)
        {
            if (rendered[frame * 2] != mono[frame] || rendered[frame * 2 + 1] != mono[frame])
            {
                differsFromDuplicatedMono = true;
                break;
            }
        }

        Equal(true, differsFromDuplicatedMono, "x64 entrance cue passes through Steam Audio instead of raw duplicated mono");
    }

    private static float[] RenderCue(NavigationBeaconCue cue, float[] monoSamples)
    {
        var assembly = typeof(NavigationBeaconPlayer).Assembly;
        var type = assembly.GetType(
                "Ff7.Accessibility.Reloaded.NavigationBeaconPlayer+SteamAudioNavigationBeaconRenderer")
            ?? throw new InvalidOperationException("The x64 Steam Audio renderer type was not found.");
        var tryCreate = type.GetMethod(
                "TryCreate",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, "TryCreate");
        var render = type.GetMethod(
                "Render",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?? throw new MissingMethodException(type.FullName, "Render");
        var logs = new List<string>();
        var renderer = (IDisposable?)tryCreate.Invoke(
                null,
                new object[] { 44100, 1024, (Action<string>)logs.Add })
            ?? throw new InvalidOperationException(
                $"Could not initialize the x64 Steam Audio renderer. Logs: {string.Join(" | ", logs)}");
        try
        {
            return (float[]?)render.Invoke(renderer, new object[] { cue, monoSamples, 1f })
                ?? throw new InvalidOperationException("The x64 Steam Audio renderer returned no samples.");
        }
        finally
        {
            renderer.Dispose();
        }
    }

    private static void TheX64HostRepeatsADirectionalCueTowardJunonsNativeEntrance()
    {
        var played = new List<(NavigationBeaconCue Cue, float Gain)>();
        var input = new MutableInput();
        var memory = SeedWorldMemoryNearJunon();
        using var coordinator = CreateCoordinator(
            memory,
            input,
            new NavigationAutoWalkController(new AcceptingKeyboardSink()),
            (_, _) => { },
            enableWorldMapEntranceProximityCues: true,
            playEntranceCue: (cue, gain) => played.Add((cue, gain)));

        Observe(coordinator, Epoch);

        Equal(1, played.Count, "x64 native entrance cue is immediate");
        Equal("Junon", played[0].Cue.TargetLabel, "x64 entrance cue label");
        Equal("proximity", played[0].Cue.Direction, "x64 entrance cue is directional, not centered");
        Equal(true, Math.Abs(played[0].Cue.SteamAudioX) > 0.01f || Math.Abs(played[0].Cue.SteamAudioZ) > 0.01f,
            "x64 entrance cue carries a spatial direction");
        Equal(true, played[0].Gain is > 0f and <= 1f, "x64 entrance cue carries distance gain");

        Observe(coordinator, Epoch + TimeSpan.FromMilliseconds(3_199));
        Equal(1, played.Count, "x64 entrance cue does not repeat early");
        // The 3199 ms observation advances the host's 200 ms scan throttle.
        // The shared tracker is due at 3200; the first host pass able to deliver
        // it is 3399 ms, so observe one millisecond after that boundary.
        Observe(coordinator, Epoch + TimeSpan.FromMilliseconds(3_400));
        Equal(2, played.Count, "x64 entrance cue repeats on the first host scan after its configured cadence");
        Equal("Junon", played[1].Cue.TargetLabel, "x64 repeated cue remains on Junon's native arrival");
    }

    private static Steam2026WorldMapAccessibilityCoordinator CreateCoordinator(
        MutableWorldMemory memory,
        MutableInput input,
        NavigationAutoWalkController autoWalk,
        Action<string, bool> speak,
        NavigationProgressController? progressController = null,
        bool enableWorldMapEntranceProximityCues = false,
        Action<NavigationBeaconCue, float>? playEntranceCue = null,
        Func<ControllerNavigationCapture?>? controllerCapture = null)
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is required.");
        var config = new AccessibilityConfig
        {
            EnableSpeech = true,
            EnableWorldMapNavigationAssistant = controllerCapture is not null,
            EnableWorldMapNavigationDiagnostics = false,
            EnableWorldMapFootstepFeedback = false,
            UseCosmoFootstepSounds = false,
            EnableWorldMapEntranceProximityCues = enableWorldMapEntranceProximityCues,
            WorldMapScanIntervalMs = 200
        };
        return new Steam2026WorldMapAccessibilityCoordinator(
            config,
            memory,
            input.CreateAdapter(),
            dataRoot,
            AppContext.BaseDirectory,
            speak,
            _ => { },
            progressController,
            autoWalk: autoWalk,
            playEntranceCue: playEntranceCue,
            controllerCapture: controllerCapture);
    }

    private static void EstablishSurface(
        Steam2026WorldMapAccessibilityCoordinator coordinator,
        DateTime start)
    {
        Observe(coordinator, start);
        Observe(coordinator, start + TimeSpan.FromMilliseconds(300));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(600));
        Observe(coordinator, start + TimeSpan.FromMilliseconds(900));
    }

    private static void Observe(
        Steam2026WorldMapAccessibilityCoordinator coordinator,
        DateTime now) =>
        coordinator.Observe(WorldFrame(now), now);

    private static RuntimeFrameObservation WorldFrame(DateTime now) =>
            new(
                now,
                new GameLifecycleObservation(
                    IsForeground: true,
                    IsShuttingDown: false,
                    ModuleId: WorldMapStateReader.WorldModule,
                    Revision: 0),
                RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
                RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
                RuntimeDomainUpdate<FieldFrameObservation>.Unchanged,
                RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
                RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);

    private static MutableWorldMemory SeedWorldMemory(int terrainId)
    {
        const uint player = 0x01000000;
        var memory = new MutableWorldMemory();
        memory.WriteByte((uint)WorldMapStateReader.AddressCurrentModule, WorldMapStateReader.WorldModule);
        memory.WriteInt32((uint)WorldMapStateReader.AddressWorldMapType, 0);
        memory.WriteInt32((uint)WorldMapStateReader.AddressWorldProgress, 0);
        memory.WriteUInt16((uint)WorldMapStateReader.AddressGameMoment, 0);
        memory.WriteUInt32((uint)WorldMapStateReader.AddressWorldPlayerEntityPointer, player);
        memory.WriteInt32((uint)WorldMapStateReader.AddressWorldCameraFront, 0);
        memory.WriteUInt32(player + WorldMapStateReader.ContactEntityOffset, 0);
        memory.WriteInt32(player + WorldMapStateReader.PositionXOffset, 174000);
        memory.WriteInt32(player + WorldMapStateReader.PositionYOffset, 0);
        memory.WriteInt32(player + WorldMapStateReader.PositionZOffset, 166000);
        memory.WriteInt16(player + WorldMapStateReader.FacingOffset, 0);
        memory.WriteInt16(player + WorldMapStateReader.DirectionOffset, 0);
        memory.WriteByte(player + WorldMapStateReader.ModelIdOffset, 0);
        memory.WriteByte(player + WorldMapStateReader.MovementSpeedOffset, 0);
        memory.PlayerAddress = player;
        memory.SetTerrain(terrainId);
        return memory;
    }

    private static MutableWorldMemory SeedWorldMemoryNearJunon()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is required.");
        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "WM0.MAP"), 0, 0);
        var assetRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "world");
        var catalog = WorldMapTargetCatalog.Load(
            map,
            Path.Combine(assetRoot, "field-id-to-world-map-coords.json"),
            Path.Combine(assetRoot, "wm-field-menu-names.txt"),
            Path.Combine(assetRoot, "world-map-location-triggers.json"));
        var junon = catalog.Locations.Single(target => target.Label == "Junon");
        var arrival = junon.NativeLocationArrivals[0];
        var planner = new WorldMapRoutePlanner(map);
        var component = planner.GetComponentId(0, map.WorldMapType, arrival.TriangleId);
        var approach = map.Triangles
            .Where(triangle => planner.GetComponentId(0, map.WorldMapType, triangle.Id) == component)
            .Select(triangle => new
            {
                Triangle = triangle,
                DistanceSquared = WorldMapTargetCatalog.WrappedDistanceSquared(
                    map,
                    triangle.Centroid.X,
                    triangle.Centroid.Z,
                    arrival.X,
                    arrival.Z)
            })
            .Where(value => value.DistanceSquared is > 1_000_000d and < 9_000_000d)
            .OrderBy(value => value.DistanceSquared)
            .First().Triangle;
        var memory = SeedWorldMemory(approach.TerrainId);
        memory.WriteInt32(memory.PlayerAddress + WorldMapStateReader.PositionXOffset, approach.Centroid.X);
        memory.WriteInt32(memory.PlayerAddress + WorldMapStateReader.PositionYOffset, approach.Centroid.Y);
        memory.WriteInt32(memory.PlayerAddress + WorldMapStateReader.PositionZOffset, approach.Centroid.Z);
        memory.SetSurface(
            approach.TerrainId,
            approach.RegionId,
            approach.HasChocoboTracks,
            approach.TerrainScriptId);
        return memory;
    }

    private static readonly DateTime Epoch =
        new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static void Contains(string expected, IEnumerable<string> actual, string label)
    {
        if (!actual.Contains(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label}: expected '{expected}', actual [{string.Join(" | ", actual)}]");
        }
    }

    private static void DoesNotContain(string unexpected, IEnumerable<string> actual, string label)
    {
        if (actual.Any(value => value.Contains(unexpected, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"{label}: did not expect '{unexpected}', actual [{string.Join(" | ", actual)}]");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }

    private sealed class MutableInput
    {
        internal bool Pressed { get; set; }

        internal Steam2026ForegroundInputAdapter CreateAdapter() =>
            new(
                () => new nint(1),
                _ => 42,
                virtualKey => virtualKey == 0x50 && Pressed
                    ? unchecked((short)0x8000)
                    : (short)0,
                currentProcessId: 42);
    }

    private sealed class AcceptingKeyboardSink : IHighwayKeyboardInputSink
    {
        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions) =>
            new(transitions.Count, 0);
    }

    private sealed class MutableWorldMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];

        internal uint PlayerAddress { get; set; }
        internal bool ReadsSucceed { get; set; } = true;

        internal void SetTerrain(int terrainId) =>
            SetSurface(terrainId, regionId: 0);

        internal void SetSurface(
            int terrainId,
            int regionId,
            bool hasChocoboTracks = false,
            int terrainScriptId = 0) =>
            WriteUInt16(
                PlayerAddress + WorldMapStateReader.WalkmapTypeOffset,
                checked((ushort)(
                    terrainId |
                    (terrainScriptId << 5) |
                    (regionId << 9) |
                    (hasChocoboTracks ? 0x8000 : 0))));

        internal void WriteByte(uint address, int value) => bytes[address] = checked((byte)value);
        internal void WriteInt16(uint address, short value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteUInt16(uint address, ushort value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteInt32(uint address, int value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteUInt32(uint address, uint value) => Write(address, BitConverter.GetBytes(value));

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (!ReadsSucceed)
            {
                destination.Clear();
                return false;
            }

            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(checked(virtualAddress + (uint)index), out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            return true;
        }

        private void Write(uint address, IReadOnlyList<byte> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                bytes[checked(address + (uint)index)] = values[index];
            }
        }
    }
}
