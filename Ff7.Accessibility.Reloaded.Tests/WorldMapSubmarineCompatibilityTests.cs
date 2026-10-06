using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// FFNx replaces the world player update (FUN_0074EA48) only when both
/// enable_worldmap_external_mesh and enable_analogue_controls are true (FFNx
/// src/ff7/world/world.cpp). Submarine automation models the native routine, so that
/// combination, or a relevant setting that cannot be read, must stop it.
/// </summary>
internal static class WorldMapSubmarineCompatibilityTests
{
    internal static void Run()
    {
        DefaultsKeepAutomation();
        OnlyBothFlagsTogetherBlock();
        TomlSyntaxIsHonoured();
        MalformedRelevantSettingsAreRefused();
        RealFilesAreRead();
        Console.WriteLine("world-map submarine FFNx compatibility tests passed.");
    }

    private static void DefaultsKeepAutomation()
    {
        Allowed(WorldMapSubmarineCompatibility.FromFfnxConfig(null), "no FFNx.toml");
        Allowed(WorldMapSubmarineCompatibility.FromFfnxConfig(string.Empty), "empty FFNx.toml");
        Allowed(Config("ff7_fps_limiter = 1"), "neither flag present");
        Allowed(Config("enable_worldmap_external_mesh = false", "enable_analogue_controls = false"),
            "both flags false, as installed");
    }

    private static void OnlyBothFlagsTogetherBlock()
    {
        Allowed(Config("enable_worldmap_external_mesh = true"), "external mesh alone keeps the native routine");
        Allowed(Config("enable_analogue_controls = true"), "analogue controls alone keep the native routine");
        Allowed(Config("enable_worldmap_external_mesh = true", "enable_analogue_controls = false"), "mesh on, analogue off");
        Allowed(Config("enable_worldmap_external_mesh = false", "enable_analogue_controls = true"), "mesh off, analogue on");
        var both = Blocked(Config("enable_worldmap_external_mesh = true", "enable_analogue_controls = true"), "both on");
        Equal(true, both.SpokenReason.Contains("enable_worldmap_external_mesh", StringComparison.Ordinal) &&
                    both.SpokenReason.Contains("enable_analogue_controls", StringComparison.Ordinal) &&
                    both.SpokenReason.Contains("restart", StringComparison.OrdinalIgnoreCase),
            $"reason names the two FFNx settings and the restart: {both.SpokenReason}");
        Equal(false, both.SpokenReason.Contains("0074EA48", StringComparison.Ordinal), "no raw implementation detail is spoken");
    }

    private static void TomlSyntaxIsHonoured()
    {
        Blocked(Config(
                "#[EXTERNAL WORLDMAP MESH]",
                "# enable_worldmap_external_mesh = false",
                "  enable_worldmap_external_mesh\t=   true   # on for the HD world",
                "",
                "\"enable_analogue_controls\" = true"),
            "comments, spacing, tabs, trailing comment and a quoted key");
        Allowed(Config("# enable_worldmap_external_mesh = true", "# enable_analogue_controls = true"),
            "commented-out settings are defaults");
        Allowed(Config(
                "enable_worldmap_external_mesh = true",
                "[some.table]",
                "enable_analogue_controls = true"),
            "a key inside a table is not FFNx's top-level setting");
        Allowed(Config(
                "enable_worldmap_external_mesh = true",
                "notes = \"\"\"",
                "enable_analogue_controls = true",
                "\"\"\""),
            "text inside a multi-line string is not a setting");
        Allowed(Config("enable_worldmap_external_mesh = true", "mod_path = \"enable_analogue_controls = true\""),
            "a value that merely mentions the key is not the key");
        Blocked(Config("enable_worldmap_external_mesh = true\r", "enable_analogue_controls = true\r"), "CRLF line endings");
    }

    private static void MalformedRelevantSettingsAreRefused()
    {
        foreach (var value in new[] { "True", "\"true\"", "1", "yes", "", "true false" })
        {
            var refused = Blocked(Config($"enable_analogue_controls = {value}"), $"analogue value '{value}'");
            Equal(true, refused.SpokenReason.Contains("enable_analogue_controls", StringComparison.Ordinal),
                $"malformed reason names the setting: {refused.SpokenReason}");
        }

        Blocked(Config("enable_worldmap_external_mesh = on"), "mesh value 'on'");
        Blocked(Config("enable_analogue_controls = false", "enable_analogue_controls = false"), "duplicate key is invalid TOML");
        Allowed(Config("ff7_fps_limiter = banana"), "an unrelated malformed setting is not this guard's concern");
    }

    private static void RealFilesAreRead()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ffnx-submarine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Allowed(WorldMapSubmarineCompatibility.Detect(directory, is64BitProcess: false), "missing FFNx.toml");
            var path = Path.Combine(directory, "FFNx.toml");
            File.WriteAllText(path, "enable_worldmap_external_mesh = true\nenable_analogue_controls = true\n");
            Blocked(WorldMapSubmarineCompatibility.Detect(directory, is64BitProcess: false), "both on in a real file");
            Allowed(WorldMapSubmarineCompatibility.Detect(directory, is64BitProcess: true),
                "the Steam x64 runtime is not FFNx");
            File.WriteAllText(path, "enable_worldmap_external_mesh = true\nenable_analogue_controls = false\n");
            Allowed(WorldMapSubmarineCompatibility.Detect(directory, is64BitProcess: false), "real file with analogue off");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var locked = Blocked(WorldMapSubmarineCompatibility.Detect(directory, is64BitProcess: false),
                    "an FFNx.toml that cannot be read");
                Equal(true, locked.SpokenReason.Contains("FFNx.toml", StringComparison.Ordinal),
                    $"unreadable reason names the file: {locked.SpokenReason}");
            }

            Allowed(WorldMapSubmarineCompatibility.Detect(null, is64BitProcess: false), "no game directory means no FFNx.toml");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static WorldMapSubmarineCompatibility Config(params string[] lines) =>
        WorldMapSubmarineCompatibility.FromFfnxConfig(string.Join("\n", lines));

    private static void Allowed(WorldMapSubmarineCompatibility result, string label)
    {
        Equal(true, result.AllowsAutomaticMovement, $"{label} ({result.Diagnostic})");
        Equal(true, result.Diagnostic.Length > 0, $"{label}: diagnostic logged");
    }

    private static WorldMapSubmarineCompatibility Blocked(WorldMapSubmarineCompatibility result, string label)
    {
        Equal(false, result.AllowsAutomaticMovement, $"{label} ({result.Diagnostic})");
        Equal(true, result.SpokenReason.Contains("Spoken navigation", StringComparison.Ordinal),
            $"{label}: reason keeps manual guidance ({result.SpokenReason})");
        return result;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"World map submarine FFNx compatibility - {label}: expected {expected}, got {actual}.");
        }
    }
}
