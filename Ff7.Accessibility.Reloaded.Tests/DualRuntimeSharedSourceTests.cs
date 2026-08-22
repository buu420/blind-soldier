/// <summary>
/// Guards the rule that this is a dual-runtime mod: a reader or speech tracker
/// written for one executable has to be compiled into the other as well.
/// </summary>
/// <remarks>
/// The Fort Condor battle reader was written, tested, deployed and played on
/// x86 while the x64 project did not reference a single one of its files. That
/// was not caught by anything: the x64 build succeeded, the parity matrix
/// checks declared capabilities rather than compiled sources, and the silence
/// on the Steam runtime looked exactly like a feature that had not been reached
/// yet.
///
/// <para>This makes the omission a build failure instead. Any new tracker or
/// reader must either be listed in the x64 project or be named below with a
/// reason, so leaving a runtime behind becomes a deliberate act rather than an
/// oversight.</para>
/// </remarks>
internal static class DualRuntimeSharedSourceTests
{
    /// <summary>
    /// Files that belong to the legacy executable alone, each because the x64
    /// runtime either cannot have the thing or already has its own.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LegacyOnly =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EchoSDisclaimerSpeechTracker.cs"] = "EchoS ships only with the legacy release.",
            ["FfnxPopupSpeechTracker.cs"] = "FFNx is a legacy-executable driver.",
            ["FfnxPopupStateReader.cs"] = "FFNx is a legacy-executable driver.",
            ["FieldMessageSpeechTracker.cs"] = "x64 reads field messages through its own hook set.",
            ["FieldRunStateReader.cs"] = "x64 reads run state through Steam2026FieldObservationReader.",
            ["NameEntryMenuSpeechTracker.cs"] = "x64 has Steam2026NameEntrySpeechCoordinator.",
            ["RenderedMenuTextSpeechTracker.cs"] = "x64 has Steam2026RenderedMenuSpeechTracker."
        };

    internal static void Run()
    {
        EverySharedReaderAndTrackerIsCompiledIntoBothRuntimes();
        TheFortCondorBattleReaderIsCompiledIntoBothRuntimes();
        TheX64RuntimeResetIncludesFortCondorState();
    }

    private static void TheX64RuntimeResetIncludesFortCondorState()
    {
        var root = FindSourceRoot();
        var session = File.ReadAllText(Path.Combine(
            root,
            "Ff7.Accessibility.Steam2026X64",
            "Runtime",
            "Steam2026ResearchSession.cs"));
        var pump = File.ReadAllText(Path.Combine(
            root,
            "Ff7.Accessibility.Steam2026X64",
            "Runtime",
            "Steam2026ResearchObservationPump.cs"));

        if (!session.Contains("pump?.ResetCondorBattle();", StringComparison.Ordinal)
            || !pump.Contains("internal void ResetCondorBattle()", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The x64 suspend/resume reset must clear the Fort Condor reader, tracker, " +
                "battle epoch, and read throttle. Otherwise a resumed session can replay " +
                "changes observed before suspension.");
        }
    }

    private static void EverySharedReaderAndTrackerIsCompiledIntoBothRuntimes()
    {
        var root = FindSourceRoot();
        var project = Path.Combine(
            root,
            "Ff7.Accessibility.Steam2026X64",
            "Ff7.Accessibility.Steam2026X64.csproj");
        var csproj = File.ReadAllText(project);

        var legacyDirectory = Path.Combine(root, "Ff7.Accessibility.Reloaded");
        var candidates = Directory
            .EnumerateFiles(legacyDirectory, "*SpeechTracker.cs")
            .Concat(Directory.EnumerateFiles(legacyDirectory, "*StateReader.cs"))
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"No readers or trackers found under {legacyDirectory}; the guard is not looking " +
                "where the sources actually are.");
        }

        var missing = candidates
            .Where(name => !LegacyOnly.ContainsKey(name))
            .Where(name => !csproj.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "These readers or trackers are compiled into the legacy runtime but not the x64 " +
                $"one: {string.Join(", ", missing)}. This is a dual-runtime mod - add them to " +
                "Ff7.Accessibility.Steam2026X64.csproj and wire them up, or record why the x64 " +
                "runtime does not need them in DualRuntimeSharedSourceTests.LegacyOnly.");
        }

        // A stale exclusion is its own kind of wrong: it would quietly excuse a
        // file that no longer exists, and go on excusing the next one to take
        // that name.
        var stale = LegacyOnly.Keys
            .Where(name => !candidates.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (stale.Length > 0)
        {
            throw new InvalidOperationException(
                $"These files are excused from the x64 runtime but no longer exist: " +
                $"{string.Join(", ", stale)}. Remove them from " +
                "DualRuntimeSharedSourceTests.LegacyOnly.");
        }
    }

    /// <summary>
    /// Every Fort Condor source file in the legacy project must be compiled into
    /// the x64 runtime too.
    /// </summary>
    /// <remarks>
    /// This used to be a hand-written list of five filenames, and a hand-written
    /// list is always one commit behind. It did not know about
    /// <c>CondorFieldNavigator.cs</c> when that arrived, so the battlefield
    /// navigator was x86-only until someone noticed by eye - which is the whole
    /// failure this guard exists to prevent.
    ///
    /// <para>Discovering the files instead of naming them makes the rule true by
    /// construction: the fort is a shared feature, so anything named for it belongs
    /// on both executables. A genuinely legacy-only Condor file would need an
    /// explicit exclusion here, with a reason - which is the right amount of
    /// friction for that claim.</para>
    /// </remarks>
    private static void TheFortCondorBattleReaderIsCompiledIntoBothRuntimes()
    {
        var root = FindSourceRoot();
        var csproj = File.ReadAllText(Path.Combine(
            root,
            "Ff7.Accessibility.Steam2026X64",
            "Ff7.Accessibility.Steam2026X64.csproj"));

        // The research probe is deliberately x86-only: it samples two megabytes of
        // the legacy data segment directly and has no meaning through the 2026
        // runtime's translated page table.
        string[] legacyOnly = ["CondorMinigameProbe.cs"];

        var discovered = Directory
            .EnumerateFiles(
                Path.Combine(root, "Ff7.Accessibility.Reloaded"),
                "Condor*.cs",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Where(name => !legacyOnly.Contains(name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (discovered.Length == 0)
        {
            throw new InvalidOperationException(
                "No Fort Condor sources were discovered, so this guard is checking nothing.");
        }

        var missing = discovered
            .Where(name => !csproj.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "Fort Condor sources are compiled into the legacy runtime but not the x64 one: " +
                $"{string.Join(", ", missing)}. This is a dual-runtime mod; add a " +
                "<Compile Include> link to Ff7.Accessibility.Steam2026X64.csproj, or, if the " +
                "file really is legacy-only, add it to legacyOnly here with the reason.");
        }
    }

    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var marker = Path.Combine(
                directory.FullName,
                "Ff7.Accessibility.Steam2026X64",
                "Ff7.Accessibility.Steam2026X64.csproj");
            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find the repository root from " + AppContext.BaseDirectory + ".");
    }
}
