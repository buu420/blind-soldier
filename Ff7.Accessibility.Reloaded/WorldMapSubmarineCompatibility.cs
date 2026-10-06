namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Whether world-map submarine automation may drive the legacy game.
///
/// <para>Submarine steering models the game's own world player update. FFNx replaces that
/// routine with its own analogue version (different submarine speed, eased turning and
/// thrust) when both <c>enable_worldmap_external_mesh</c> and <c>enable_analogue_controls</c>
/// are true in the FFNx.toml beside the game (FFNx src/ff7/world/world.cpp,
/// <c>world_hook_init</c>). Either alone keeps the native routine. Both default to false.</para>
///
/// <para>Only those two top-level TOML booleans are read, as FFNx reads them. A missing file
/// or key is the FFNx default. A relevant value that is not a TOML boolean, a duplicate, or a
/// file that exists but cannot be read is refused rather than guessed. The Steam x64 runtime
/// is not driven by FFNx.</para>
/// </summary>
internal sealed record WorldMapSubmarineCompatibility(bool AllowsAutomaticMovement, string SpokenReason, string Diagnostic)
{
    private const string ExternalMeshKey = "enable_worldmap_external_mesh";
    private const string AnalogueControlsKey = "enable_analogue_controls";
    private const string ManualGuidance = " Spoken navigation still works.";

    internal static WorldMapSubmarineCompatibility DetectForCurrentProcess() =>
        Detect(Path.GetDirectoryName(Environment.ProcessPath), Environment.Is64BitProcess);

    internal static WorldMapSubmarineCompatibility Detect(string? gameDirectory, bool is64BitProcess)
    {
        if (is64BitProcess)
        {
            return Allowed("Submarine automation: the Steam x64 runtime is not driven by FFNx.");
        }

        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return Allowed("Submarine automation: no game directory, so no FFNx.toml; FFNx defaults apply.");
        }

        var path = Path.Combine(gameDirectory, "FFNx.toml");
        string toml;
        try
        {
            if (!File.Exists(path))
            {
                return Allowed($"Submarine automation: no FFNx.toml at {path}; FFNx defaults keep the game's world controls.");
            }

            toml = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Blocked(
                "Submarine auto travel is unavailable: FFNx.toml beside the game could not be read, so its world map control settings are unknown." +
                ManualGuidance,
                $"Submarine automation refused: {path} could not be read: {ex.Message}");
        }

        return FromFfnxConfig(toml, path);
    }

    internal static WorldMapSubmarineCompatibility FromFfnxConfig(string? toml) => FromFfnxConfig(toml, "FFNx.toml");

    private static WorldMapSubmarineCompatibility FromFfnxConfig(string? toml, string source)
    {
        if (string.IsNullOrEmpty(toml))
        {
            return Allowed($"Submarine automation: {source} sets neither {ExternalMeshKey} nor {AnalogueControlsKey}; the game's world controls apply.");
        }

        var values = new Dictionary<string, bool>(StringComparer.Ordinal);
        string? malformed = null;
        var inTable = false;
        string? multilineDelimiter = null;
        foreach (var rawLine in toml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (multilineDelimiter is not null)
            {
                if (CountOccurrences(line, multilineDelimiter) % 2 == 1)
                {
                    multilineDelimiter = null;
                }

                continue;
            }

            var text = line.TrimStart(' ', '\t');
            if (text.Length == 0 || text[0] == '#')
            {
                continue;
            }

            if (text[0] == '[')
            {
                // Every key after a table header belongs to that table, not to the top level
                // FFNx reads.
                inTable = true;
                continue;
            }

            if (!TrySplitKeyValue(text, out var key, out var value))
            {
                continue;
            }

            var opener = value.StartsWith("\"\"\"", StringComparison.Ordinal) ? "\"\"\""
                : value.StartsWith("'''", StringComparison.Ordinal) ? "'''"
                : null;
            if (opener is not null && CountOccurrences(value, opener) == 1)
            {
                multilineDelimiter = opener;
            }

            if (inTable || key is not (ExternalMeshKey or AnalogueControlsKey))
            {
                continue;
            }

            var hash = value.IndexOf('#');
            var literal = (hash >= 0 ? value[..hash] : value).Trim(' ', '\t');
            if (values.ContainsKey(key) || literal is not ("true" or "false"))
            {
                malformed ??= key;
                continue;
            }

            values[key] = literal == "true";
        }

        if (malformed is not null)
        {
            return Blocked(
                $"Submarine auto travel is unavailable: FFNx.toml has an unrecognised setting for {malformed}. " +
                "Set it once to true or false, then restart the game." + ManualGuidance,
                $"Submarine automation refused: {source} has a malformed or duplicate {malformed}.");
        }

        var externalMesh = values.GetValueOrDefault(ExternalMeshKey);
        var analogueControls = values.GetValueOrDefault(AnalogueControlsKey);
        if (externalMesh && analogueControls)
        {
            return Blocked(
                "Submarine auto travel is unavailable while FFNx replaces the world map controls. " +
                $"In FFNx.toml set {ExternalMeshKey} or {AnalogueControlsKey} to false, then restart the game." +
                ManualGuidance,
                $"Submarine automation refused: {source} enables both {ExternalMeshKey} and {AnalogueControlsKey}, " +
                "so FFNx replaces the world player update the submarine steering models.");
        }

        return Allowed(
            $"Submarine automation: {source} {ExternalMeshKey}={Describe(values, ExternalMeshKey)}, " +
            $"{AnalogueControlsKey}={Describe(values, AnalogueControlsKey)}; the game's world controls apply.");
    }

    /// <summary>A top-level <c>key = value</c> line with a bare, basic or literal key.</summary>
    private static bool TrySplitKeyValue(string text, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        int keyEnd;
        if (text[0] is '"' or '\'')
        {
            var close = text.IndexOf(text[0], 1);
            if (close < 0)
            {
                return false;
            }

            key = text[1..close];
            keyEnd = close + 1;
        }
        else
        {
            keyEnd = 0;
            while (keyEnd < text.Length && (char.IsAsciiLetterOrDigit(text[keyEnd]) || text[keyEnd] is '_' or '-' or '.'))
            {
                keyEnd++;
            }

            if (keyEnd == 0)
            {
                return false;
            }

            key = text[..keyEnd];
        }

        var rest = text[keyEnd..].TrimStart(' ', '\t');
        if (rest.Length == 0 || rest[0] != '=')
        {
            return false;
        }

        value = rest[1..].TrimStart(' ', '\t');
        return true;
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        for (var index = text.IndexOf(token, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Describe(IReadOnlyDictionary<string, bool> values, string key) =>
        values.TryGetValue(key, out var value) ? value ? "true" : "false" : "default false";

    private static WorldMapSubmarineCompatibility Allowed(string diagnostic) => new(true, string.Empty, diagnostic);

    private static WorldMapSubmarineCompatibility Blocked(string spokenReason, string diagnostic) =>
        new(false, spokenReason, diagnostic);
}
