/// <summary>
/// Checks that a shared feature is not merely compiled into both runtimes but
/// actually wired into both.
/// </summary>
/// <remarks>
/// <para><c>DualRuntimeSharedSourceTests</c> proves a type reached an assembly. It
/// cannot prove anything calls it, and that gap is where four features hid:
/// <c>MidgarZolomAreaTracker</c>, the field zone-transition cue,
/// <c>FieldExitNavigationProfileCatalog</c> and <c>OpeningMovieDescription</c> were
/// all absent from the x64 runtime while <c>--dual-runtime-sources-only</c>
/// passed.</para>
///
/// <para>The Zolom pair is the clearest case and the reason this exists.
/// <c>MidgarZolomCrossingTracker</c> - "now is the moment to dash" - was linked into
/// x64 and called. <c>MidgarZolomAreaTracker</c>, its sibling from the same feature,
/// was neither. So an x64 player was told when to run and never told they were
/// standing on the marsh, that the Zolom was on it with them, or that they were
/// clear of it. Half a feature is harder to notice than a missing one.</para>
///
/// <para>This is the seed of a fuller contract: each entry should eventually carry
/// its config keys, assets and behavioural evidence too. Naming the wiring file per
/// runtime is the cheap half that would already have caught all four.</para>
/// </remarks>
internal static class DualRuntimeFeatureWiringTests
{
    /// <param name="SharedSource">The file that must be compiled into both runtimes.</param>
    /// <param name="TypeName">The type each runtime's wiring must mention.</param>
    /// <param name="LegacyWiring">The x86 file that drives it.</param>
    /// <param name="Steam2026Wiring">The x64 file that drives it.</param>
    private sealed record SharedFeature(
        string Id,
        string SharedSource,
        string TypeName,
        string LegacyWiring,
        string Steam2026Wiring);

    private static readonly SharedFeature[] Features =
    [
        new("world.midgar-zolom-crossing",
            "MidgarZolomCrossingTracker.cs",
            "MidgarZolomCrossingTracker",
            @"Ff7.Accessibility.Reloaded\Mod.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\World\Steam2026WorldMapAccessibilityCoordinator.cs"),

        new("world.midgar-zolom-area",
            "MidgarZolomAreaTracker.cs",
            "MidgarZolomAreaTracker",
            @"Ff7.Accessibility.Reloaded\Mod.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\World\Steam2026WorldMapAccessibilityCoordinator.cs"),

        new("world.terrain-announcements",
            "WorldMapTerrainAnnouncementTracker.cs",
            "TerrainAnnouncements",
            @"Ff7.Accessibility.Reloaded\Mod.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\World\Steam2026WorldMapAccessibilityCoordinator.cs"),

        new("world.native-location-entrances",
            "WorldMapTargetCatalog.cs",
            "WorldMapTargetCatalog.Load",
            @"Ff7.Accessibility.Reloaded\Mod.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\World\Steam2026WorldMapAccessibilityCoordinator.cs"),

        new("world.location-entrance-proximity-cues",
            "WorldMapEntranceProximityCueTracker.cs",
            "EntranceProximityCues",
            @"Ff7.Accessibility.Reloaded\Mod.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\World\Steam2026WorldMapAccessibilityCoordinator.cs"),

        new("movie.opening-description",
            "OpeningMovieDescription.cs",
            "OpeningMovieDescription",
            @"Ff7.Accessibility.Reloaded\Mod.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\Steam2026ResearchSession.cs"),

        new("field.zone-transition-cue",
            "FieldZoneTransitionCueTracker.cs",
            "FieldZoneTransitionCueTracker",
            @"Ff7.Accessibility.Reloaded\Mod.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\Field\Steam2026FieldZoneTransitionCueCoordinator.cs"),

        new("field.exit-navigation-profiles",
            "FieldExitNavigationProfileCatalog.cs",
            "FieldExitNavigationProfileCatalog",
            @"Ff7.Accessibility.Reloaded\NativeFieldExitTargetProvider.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\Field\Steam2026FieldNavigationCoordinator.cs"),

        // The Fort Condor cursor jump. Its predecessor is exactly why this table
        // exists: the direct-write mover shipped on x86 only, and the x64
        // runtime was left announcing that it could not move the cursor.
        new("condor.cursor-steering",
            "CondorCursorSteering.cs",
            "CondorCursorSteering",
            @"Ff7.Accessibility.Reloaded\Mod.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\Steam2026ResearchObservationPump.cs"),

        new("field.junon-parade-alignment",
            "JunonParadeAlignmentAssist.cs",
            "JunonParadeAlignmentAssist",
            @"Ff7.Accessibility.Reloaded\Mod.cs",
            @"Ff7.Accessibility.Steam2026X64\Runtime\Field\Steam2026FieldNavigationCoordinator.cs"),

        // The physical keys every synthesized direction press uses. FFVII's
        // untouched default binds movement to the numeric keypad, so a runtime
        // that resolved this itself - or skipped it and sent arrows - would
        // press keys the game does not read and do nothing at all, in silence.
        //
        // Both runtimes name the same driver because there is deliberately only
        // one: two senders that could disagree about what Up means is the split
        // this table exists to catch. The x64 half of the guard is the csproj
        // check above, which is what proves the resolver is linked in at all.
        new("input.direction-mapping",
            "HighwayDirectionInputMappingResolver.cs",
            "HighwayDirectionInputMappingResolver",
            @"Ff7.Accessibility.Reloaded\HighwayAutoSteeringController.cs",
            @"Ff7.Accessibility.Reloaded\HighwayAutoSteeringController.cs")
    ];

    /// <summary>
    /// Both runtimes must say out loud that they have been suspended and resumed.
    /// </summary>
    /// <remarks>
    /// x64 wrote both transitions to the log and spoke neither, so the mod went
    /// completely silent with no explanation and came back with no confirmation.
    /// A player cannot tell that apart from a crash or a hang, and unexplained
    /// silence is the failure this project treats as worse than a crash.
    ///
    /// <para>Checked as source text because the alternative is standing up a whole
    /// session with a speech backend. It is a weak proof of a strong requirement -
    /// it would not notice the call moving into an unreachable branch - but it does
    /// catch the thing that actually happened, which was the call not existing.</para>
    /// </remarks>
    private static void BothRuntimesAnnounceSuspendAndResume()
    {
        var root = FindSourceRoot();
        var sites = new[]
        {
            ("x86", Path.Combine(root, "Ff7.Accessibility.Reloaded", "Mod.cs")),
            ("x64", Path.Combine(root, "Ff7.Accessibility.Steam2026X64", "Runtime", "Steam2026ResearchSession.cs"))
        };

        var failures = new List<string>();
        foreach (var (runtime, path) in sites)
        {
            var text = File.ReadAllText(path);
            foreach (var transition in new[] { "suspended.", "resumed." })
            {
                // The wording is shared, so the announcement is the literal the
                // player hears rather than a mention of the word in a log line.
                if (!text.Contains(
                        $"Final Fantasy Seven accessibility mod {transition}",
                        StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{runtime} never announces \"{transition.TrimEnd('.')}\": " +
                        $"{Path.GetFileName(path)} does not speak it.");
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "A runtime goes quiet without saying why:" +
                Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }

    public static void Run()
    {
        BothRuntimesAnnounceSuspendAndResume();
        BothCondorHostsBankStatusBeforeReading();
        BothCondorHostsPassAutomaticAnnouncementSettings();
        BothCondorHostsDelegateCursorDomainAndTargetIdentityToSharedSteering();
        BothWorldHostsCommitRicherTerrainOnlyAfterDelivery();
        BothWorldHostsReserveTerrainForProgressControlSpeech();
        BothWorldHostsDeliverNativeEntranceProximityCuesWithoutReusingAreaSpeech();
        BothWorldHostsRequireNativeLocationTriggerMetadata();
        BothWorldHostsHonorLogicalAutoWalkStopRequests();
        var root = FindSourceRoot();
        var x64Csproj = File.ReadAllText(Path.Combine(
            root, "Ff7.Accessibility.Steam2026X64", "Ff7.Accessibility.Steam2026X64.csproj"));

        var failures = new List<string>();
        foreach (var feature in Features)
        {
            if (!x64Csproj.Contains(feature.SharedSource, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"{feature.Id}: {feature.SharedSource} is not compiled into the x64 runtime.");
                continue;
            }

            foreach (var (runtime, wiring) in new[]
                     {
                         ("x86", feature.LegacyWiring),
                         ("x64", feature.Steam2026Wiring)
                     })
            {
                var path = Path.Combine(root, wiring);
                if (!File.Exists(path))
                {
                    failures.Add($"{feature.Id}: the declared {runtime} wiring file is missing: {wiring}.");
                    continue;
                }

                if (!File.ReadAllText(path).Contains(feature.TypeName, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{feature.Id}: {feature.TypeName} is compiled into {runtime} but " +
                        $"{wiring} never mentions it, so nothing drives it there.");
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Shared features are not wired into both runtimes:" +
                Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }

    /// <summary>
    /// Both runtimes must hand the same coherent snapshot and stable navigation
    /// target to the shared Fort Condor steering policy. If either host chooses
    /// CursorX versus DestinationX itself, mode-3 support can drift back to one
    /// runtime even though the controller is compiled into both assemblies.
    /// </summary>
    private static void BothCondorHostsDelegateCursorDomainAndTargetIdentityToSharedSteering()
    {
        var root = FindSourceRoot();
        var sites = new[]
        {
            ("x86", Path.Combine(root, "Ff7.Accessibility.Reloaded", "Mod.cs")),
            ("x64", Path.Combine(
                root,
                "Ff7.Accessibility.Steam2026X64",
                "Runtime",
                "Steam2026ResearchObservationPump.cs"))
        };

        var failures = new List<string>();
        foreach (var (runtime, path) in sites)
        {
            var text = File.ReadAllText(path);
            if (!text.Contains(
                    "condorCursorSteering.TryBegin(target, snapshot)",
                    StringComparison.Ordinal))
            {
                failures.Add($"{runtime} does not pass target identity and the coherent snapshot to TryBegin.");
            }

            if (!text.Contains("condorCursorSteering.Step(snapshot)", StringComparison.Ordinal))
            {
                failures.Add($"{runtime} does not let shared steering select the live cursor domain.");
            }

            if (!text.Contains(
                    "target => BeginCondorCursorJump(snapshot, target)",
                    StringComparison.Ordinal))
            {
                failures.Add($"{runtime} reduces the selected unit back to a bare coordinate before steering.");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Fort Condor destination steering is not wired symmetrically:" +
                Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }

    private static void BothCondorHostsPassAutomaticAnnouncementSettings()
    {
        var root = FindSourceRoot();
        var legacy = File.ReadAllText(Path.Combine(
            root, "Ff7.Accessibility.Reloaded", "Mod.cs"));
        var steamSession = File.ReadAllText(Path.Combine(
            root,
            "Ff7.Accessibility.Steam2026X64",
            "Runtime",
            "Steam2026ResearchSession.cs"));
        var steamPump = File.ReadAllText(Path.Combine(
            root,
            "Ff7.Accessibility.Steam2026X64",
            "Runtime",
            "Steam2026ResearchObservationPump.cs"));

        var legacyConstruction = WindowAfter(legacy, "new CondorBattleSpeechTracker(", 500);
        var steamPumpConstruction = WindowAfter(
            steamSession,
            "new Steam2026ResearchObservationPump(",
            700);
        var steamTrackerConstruction = WindowAfter(
            steamPump,
            "new CondorBattleSpeechTracker(",
            500);

        foreach (var key in new[]
                 {
                     "EnableCondorBattleLineAnnouncements",
                     "EnableCondorEnemyArrivalAnnouncements"
                 })
        {
            if (!legacyConstruction.Contains($"config.{key}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"x86 constructs the Fort Condor tracker without config.{key}.");
            }

            if (!steamPumpConstruction.Contains("config", StringComparison.Ordinal) ||
                !steamTrackerConstruction.Contains($"config.{key}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"x64 does not carry config.{key} through the session pump into the shared tracker.");
            }
        }
    }

    private static string WindowAfter(string source, string marker, int length)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        return start < 0
            ? string.Empty
            : source.Substring(start, Math.Min(length, source.Length - start));
    }

    private static void BothWorldHostsHonorLogicalAutoWalkStopRequests()
    {
        var root = FindSourceRoot();
        var sites = new[]
        {
            ("x86",
                Path.Combine(root, "Ff7.Accessibility.Reloaded", "Mod.cs"),
                "StopNavigationAutoWalk(",
                "NavigationAutoWalkDomain.WorldMap"),
            ("x64", Path.Combine(
                root,
                "Ff7.Accessibility.Steam2026X64",
                "Runtime",
                "World",
                "Steam2026WorldMapAccessibilityCoordinator.cs"),
                "autoWalk.Stop()",
                "NavigationAutoWalkDomain.WorldMap")
        };

        foreach (var (runtime, path, stopCall, activeDomain) in sites)
        {
            var text = File.ReadAllText(path);
            var stopMarker = text.IndexOf("value.StopAutoWalk", StringComparison.Ordinal);
            var stopWindow = stopMarker < 0
                ? string.Empty
                : text.Substring(stopMarker, Math.Min(500, text.Length - stopMarker));
            var observeMarker = text.IndexOf("runtime.Navigation.Observe(", StringComparison.Ordinal);
            var observeWindow = observeMarker < 0
                ? string.Empty
                : text.Substring(observeMarker, Math.Min(500, text.Length - observeMarker));
            if (!stopWindow.Contains(stopCall, StringComparison.Ordinal) ||
                !observeWindow.Contains("IsEnabledFor", StringComparison.Ordinal) ||
                !observeWindow.Contains(activeDomain, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{runtime} does not feed or honor logical world-map auto-walk convergence and can strand the player.");
            }
        }
    }

    private static void BothWorldHostsRequireNativeLocationTriggerMetadata()
    {
        var root = FindSourceRoot();
        var hostPaths = new[]
        {
            Path.Combine(root, "Ff7.Accessibility.Reloaded", "Mod.cs"),
            Path.Combine(
                root,
                "Ff7.Accessibility.Steam2026X64",
                "Runtime",
                "World",
                "Steam2026WorldMapAccessibilityCoordinator.cs")
        };
        var failures = new List<string>();
        foreach (var hostPath in hostPaths)
        {
            var source = File.ReadAllText(hostPath);
            foreach (var required in new[]
                     {
                         "world-map-location-triggers.json",
                         "WorldMapTargetCatalog.Load(map, coordinatePath, menuNamePath, triggerPath)",
                         "UnresolvedLocations"
                     })
            {
                if (!source.Contains(required, StringComparison.Ordinal))
                {
                    failures.Add($"{Path.GetFileName(hostPath)} does not require or report {required}.");
                }
            }
        }

        foreach (var projectPath in new[]
                 {
                     Path.Combine(root, "Ff7.Accessibility.Reloaded", "Ff7.Accessibility.Reloaded.csproj"),
                     Path.Combine(root, "Ff7.Accessibility.Steam2026X64", "Ff7.Accessibility.Steam2026X64.csproj")
                 })
        {
            if (!File.ReadAllText(projectPath).Contains(
                    "world-map-location-triggers.json",
                    StringComparison.Ordinal))
            {
                failures.Add($"{Path.GetFileName(projectPath)} does not ship native location triggers.");
            }
        }

        if (!File.ReadAllText(Path.Combine(root, "Build-DualRuntimePackage.ps1")).Contains(
                "native world-map location trigger metadata",
                StringComparison.Ordinal))
        {
            failures.Add("The dual-runtime package gate does not require native location triggers.");
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Native world-map entrance metadata is not wired into both runtimes:" +
                Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }

    private static void BothWorldHostsDeliverNativeEntranceProximityCuesWithoutReusingAreaSpeech()
    {
        var root = FindSourceRoot();
        var sites = new[]
        {
            (
                "x86",
                Path.Combine(root, "Ff7.Accessibility.Reloaded", "Mod.cs"),
                "private void ObserveWorldMapEntranceCue(",
                "private void ObserveWorldMapTerrain(",
                "worldMapEntranceCuePlayer?.Play(spatialCue, ready.Gain)"),
            (
                "x64",
                Path.Combine(
                    root,
                    "Ff7.Accessibility.Steam2026X64",
                    "Runtime",
                    "World",
                    "Steam2026WorldMapAccessibilityCoordinator.cs"),
                "private void ObserveEntranceCue(",
                "private void PlayFootstep(",
                "entranceCuePlayer?.Play(spatialCue, ready.Gain)")
        };

        var failures = new List<string>();
        foreach (var (runtime, path, startMarker, endMarker, playback) in sites)
        {
            var source = File.ReadAllText(path);
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = start < 0
                ? -1
                : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            if (start < 0 || end <= start)
            {
                failures.Add($"{runtime}: world entrance delivery method could not be inspected.");
                continue;
            }

            var method = source[start..end];
            foreach (var required in new[]
                     {
                         "EnableWorldMapEntranceProximityCues",
                         "EntranceProximityCues.Update(",
                         "WorldMapEntranceProximitySpatializer.CreateCue(",
                         "ready.Arrival.TriangleId",
                         "ready.DistanceUnits",
                         playback
                     })
            {
                if (!method.Contains(required, StringComparison.Ordinal))
                {
                    failures.Add($"{runtime}: world entrance host is missing {required}.");
                }
            }

            if (method.Contains("EnableFieldZoneTransitionCue", StringComparison.Ordinal) ||
                method.Contains("PlayCentered(", StringComparison.Ordinal))
            {
                failures.Add($"{runtime}: world entrance delivery still uses a field gate or one-shot centered playback.");
            }
        }

        var x86Source = File.ReadAllText(sites[0].Item2);
        if (!x86Source.Contains("ObserveWorldMapEntranceCue(runtime, state, now);", StringComparison.Ordinal))
        {
            failures.Add("x86: the live world-map tick never calls the entrance cue.");
        }

        var x64Source = File.ReadAllText(sites[1].Item2);
        if (!x64Source.Contains("ObserveEntranceCue(runtime, state, nowUtc);", StringComparison.Ordinal))
        {
            failures.Add("x64: the live world-map observation never calls the entrance cue.");
        }

        foreach (var (runtime, source, startMarker, endMarker) in new[]
                 {
                     ("x86", x86Source, "private void ObserveWorldMapTerrain(", "private void ResetWorldMapAccessibility("),
                     ("x64", x64Source, "private void ObserveTerrain(", "private void ObserveEntranceCue(")
                 })
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = start < 0 ? -1 : source.IndexOf(endMarker, start, StringComparison.Ordinal);
            var method = start >= 0 && end > start ? source[start..end] : string.Empty;
            if (method.Contains("WorldMapEntrance", StringComparison.Ordinal) ||
                method.Contains("PlayCentered(", StringComparison.Ordinal))
            {
                failures.Add($"{runtime}: named-area speech still emits the old one-shot sound.");
            }
        }

        var x64FieldSource = File.ReadAllText(Path.Combine(
            root,
            "Ff7.Accessibility.Steam2026X64",
            "Runtime",
            "Field",
            "Steam2026FieldZoneTransitionCueCoordinator.cs"));
        if (!x64FieldSource.Contains("EnableFieldZoneTransitionCue", StringComparison.Ordinal) ||
            !x64FieldSource.Contains("PlayField(", StringComparison.Ordinal) ||
            x64FieldSource.Contains("PlayWorldMapAreaTransition", StringComparison.Ordinal) ||
            x64FieldSource.Contains("worldAreaPlayer", StringComparison.Ordinal))
        {
            failures.Add("x64: the field transition coordinator did not remain field-only.");
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "World-map entrances do not use native repeating spatial cues while named-area speech and field transitions stay separate:" +
                Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }

    private static void BothWorldHostsReserveTerrainForProgressControlSpeech()
    {
        var root = FindSourceRoot();
        var sites = new[]
        {
            (
                "x86",
                Path.Combine(root, "Ff7.Accessibility.Reloaded", "Mod.cs"),
                "private void TickWorldMapAccessibility()",
                "private void ObserveWorldMapTerrain("),
            (
                "x64",
                Path.Combine(
                    root,
                    "Ff7.Accessibility.Steam2026X64",
                    "Runtime",
                    "World",
                    "Steam2026WorldMapAccessibilityCoordinator.cs"),
                "internal void Observe(RuntimeFrameObservation frame, DateTime nowUtc)",
                "internal static bool ShouldThrottleObservation(")
        };

        var failures = new List<string>();
        foreach (var (runtime, path, startMarker, endMarker) in sites)
        {
            var source = File.ReadAllText(path);
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            if (start < 0 || end <= start)
            {
                failures.Add($"{runtime}: world observation method could not be inspected.");
                continue;
            }

            var method = source[start..end];
            var revision = method.IndexOf(".SpeechRevision", StringComparison.Ordinal);
            var priority = method.IndexOf("progressControlSpeechWasObserved", StringComparison.Ordinal);
            var terrain = runtime == "x86"
                ? method.LastIndexOf("ObserveWorldMapTerrain(", StringComparison.Ordinal)
                : method.LastIndexOf("ObserveTerrain(", StringComparison.Ordinal);
            if (revision < 0 || priority < revision || terrain <= priority)
            {
                failures.Add(
                    $"{runtime}: progress-control speech is not observed before world-map terrain speech.");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "World-map terrain does not yield to F5-F7 in both runtimes:" +
                Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }

    private static void BothWorldHostsCommitRicherTerrainOnlyAfterDelivery()
    {
        var root = FindSourceRoot();
        var sites = new[]
        {
            (
                "x86",
                Path.Combine(root, "Ff7.Accessibility.Reloaded", "Mod.cs"),
                "private bool ObserveMidgarZolomCrossing(",
                "private bool ProcessWorldMapNavigationOutput("),
            (
                "x64",
                Path.Combine(
                    root,
                    "Ff7.Accessibility.Steam2026X64",
                    "Runtime",
                    "World",
                    "Steam2026WorldMapAccessibilityCoordinator.cs"),
                "private bool ObserveMidgarZolomCrossing(",
                "public void Dispose()")
        };

        var failures = new List<string>();
        foreach (var (runtime, path, startMarker, endMarker) in sites)
        {
            var source = File.ReadAllText(path);
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = start < 0
                ? -1
                : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            if (start < 0 || end <= start)
            {
                failures.Add($"{runtime}: could not isolate the world-map marsh host method.");
                continue;
            }

            var method = source[start..end];
            if (!method.Contains(
                    "state.TerrainId == MidgarZolomAreaTracker.MarshTerrainId",
                    StringComparison.Ordinal))
            {
                failures.Add($"{runtime}: richer marsh speech is not driven by the coherent native terrain id.");
            }

            var delivery = method.IndexOf("var areaCueDelivered =", StringComparison.Ordinal);
            var accepted = method.IndexOf("if (areaCueDelivered)", StringComparison.Ordinal);
            var record = method.IndexOf(".RecordExternalTerrainSpeech(", StringComparison.Ordinal);
            if (delivery < 0 || accepted <= delivery || record <= accepted)
            {
                failures.Add(
                    $"{runtime}: generic swamp speech can be suppressed before the richer cue is accepted.");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "World-map terrain fallback is not delivery-aware in both runtimes:" +
                Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }

    /// <summary>
    /// K is a one-frame edge while the battle reader can deliberately withhold
    /// phase-one snapshots for 100 ms or longer. Both hosts must bank the edge
    /// before calling TryRead and consume it only after a snapshot exists.
    /// </summary>
    private static void BothCondorHostsBankStatusBeforeReading()
    {
        var root = FindSourceRoot();
        var sites = new[]
        {
            (
                "x86",
                Path.Combine(root, "Ff7.Accessibility.Reloaded", "Mod.cs"),
                "private void TickCondorBattleReader()",
                "private IEnumerable<CondorNavigationAction> ReadCondorNavigationActions"),
            (
                "x64",
                Path.Combine(
                    root,
                    "Ff7.Accessibility.Steam2026X64",
                    "Runtime",
                    "Steam2026ResearchObservationPump.cs"),
                "internal IReadOnlyList<(string Text, bool Interrupt)> ObserveCondorBattle(",
                "internal void ResetCondorBattle()")
        };

        var failures = new List<string>();
        foreach (var (runtime, path, startMarker, endMarker) in sites)
        {
            var source = File.ReadAllText(path);
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = start < 0
                ? -1
                : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            if (start < 0 || end <= start)
            {
                failures.Add($"{runtime}: could not isolate the Fort Condor host method in {path}.");
                continue;
            }

            var method = source[start..end];
            var request = method.IndexOf(".RequestStatus();", StringComparison.Ordinal);
            var read = method.IndexOf(".TryRead();", StringComparison.Ordinal);
            if (request < 0 || read < 0 || request >= read)
            {
                failures.Add($"{runtime}: K is not banked before the battle snapshot read.");
            }

            if (!method.Contains(".HasPendingStatusRequest", StringComparison.Ordinal) ||
                !method.Contains(".ConsumeRequestedStatus(", StringComparison.Ordinal))
            {
                failures.Add($"{runtime}: the banked K request is not retained and consumed.");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Fort Condor status requests can disappear during initialization:" +
                Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }

    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "Ff7.Accessibility.Steam2026X64",
                    "Ff7.Accessibility.Steam2026X64.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find the repository root from " + AppContext.BaseDirectory + ".");
    }
}
