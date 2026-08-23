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
