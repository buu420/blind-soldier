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
            @"Ff7.Accessibility.Steam2026X64\Runtime\World\Steam2026WorldMapAccessibilityCoordinator.cs")
    ];

    public static void Run()
    {
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
