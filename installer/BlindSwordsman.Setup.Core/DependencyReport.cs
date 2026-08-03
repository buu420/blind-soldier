using System.Text.Json;

namespace BlindSwordsman.Setup.Core;

public enum DependencySeverity
{
    Blocking,
    Required,
    Optional
}

public sealed record RuntimeInstallation(
    string Id,
    string Architecture,
    string Root,
    string Executable);

public sealed record GameInstallation(
    string Version,
    string SteamAppId,
    string GameRoot,
    IReadOnlyList<RuntimeInstallation> Runtimes);

public sealed record DependencyCheck(
    string Id,
    string Name,
    DependencySeverity Severity,
    bool Satisfied,
    string Message,
    string? Path);

public sealed record PreflightReport(
    int SchemaVersion,
    bool CanInstall,
    GameInstallation? Game,
    string? ReloadedRoot,
    string? SeventhHeavenRoot,
    IReadOnlyList<DependencyCheck> Dependencies);

public static class PreflightReportParser
{
    private static readonly string[] RootProperties =
        ["schemaVersion", "canInstall", "game", "reloadedRoot", "seventhHeavenRoot", "dependencies"];

    public static PreflightReport Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 10 });
            var root = document.RootElement;
            ExactProperties(root, RootProperties, "preflight report");
            if (!root.GetProperty("schemaVersion").TryGetInt32(out var schema) || schema != 1)
            {
                throw new InvalidDataException("Unsupported preflight report schema.");
            }

            var canInstall = RequireBoolean(root, "canInstall");
            var game = ParseGame(root.GetProperty("game"));
            var dependenciesElement = root.GetProperty("dependencies");
            if (dependenciesElement.ValueKind != JsonValueKind.Array || dependenciesElement.GetArrayLength() == 0)
            {
                throw new InvalidDataException("Preflight report contains no dependency checks.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var dependencies = new List<DependencyCheck>();
            foreach (var element in dependenciesElement.EnumerateArray())
            {
                ExactProperties(element, ["id", "name", "severity", "satisfied", "message", "path"], "dependency check");
                var id = RequireString(element, "id");
                if (!ids.Add(id))
                {
                    throw new InvalidDataException($"Preflight report contains duplicate dependency '{id}'.");
                }

                var severity = RequireString(element, "severity") switch
                {
                    "blocking" => DependencySeverity.Blocking,
                    "required" => DependencySeverity.Required,
                    "optional" => DependencySeverity.Optional,
                    var value => throw new InvalidDataException($"Unknown dependency severity '{value}'.")
                };
                var satisfied = RequireBoolean(element, "satisfied");
                if (severity == DependencySeverity.Blocking && satisfied)
                {
                    throw new InvalidDataException($"Blocking dependency '{id}' cannot be satisfied.");
                }
                if (severity == DependencySeverity.Required && !satisfied)
                {
                    throw new InvalidDataException($"Unsatisfied dependency '{id}' must be marked blocking.");
                }

                dependencies.Add(new DependencyCheck(
                    id,
                    RequireString(element, "name"),
                    severity,
                    satisfied,
                    RequireString(element, "message"),
                    OptionalString(element, "path")));
            }

            var blocked = dependencies.Any(item => item.Severity == DependencySeverity.Blocking ||
                (item.Severity == DependencySeverity.Required && !item.Satisfied));
            if (canInstall != (game is not null && !blocked))
            {
                throw new InvalidDataException("Preflight installability disagrees with its game or dependency results.");
            }

            var ordered = dependencies
                .OrderBy(item => item.Severity)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new PreflightReport(
                schema,
                canInstall,
                game,
                OptionalString(root, "reloadedRoot"),
                OptionalString(root, "seventhHeavenRoot"),
                ordered);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidDataException("Preflight report is malformed.", exception);
        }
    }

    private static GameInstallation? ParseGame(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        ExactProperties(element, ["version", "steamAppId", "gameRoot", "runtimes"], "game installation");
        var runtimesElement = element.GetProperty("runtimes");
        if (runtimesElement.ValueKind != JsonValueKind.Array || runtimesElement.GetArrayLength() == 0)
        {
            throw new InvalidDataException("Detected game has no runtime entries.");
        }

        var runtimes = new List<RuntimeInstallation>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var runtime in runtimesElement.EnumerateArray())
        {
            ExactProperties(runtime, ["id", "architecture", "root", "executable"], "game runtime");
            var id = RequireString(runtime, "id");
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"Detected game has duplicate runtime '{id}'.");
            }
            var architecture = RequireString(runtime, "architecture");
            if (architecture is not ("x86" or "x64"))
            {
                throw new InvalidDataException($"Unknown game architecture '{architecture}'.");
            }
            runtimes.Add(new RuntimeInstallation(
                id,
                architecture,
                RequireString(runtime, "root"),
                RequireString(runtime, "executable")));
        }

        return new GameInstallation(
            RequireString(element, "version"),
            RequireString(element, "steamAppId"),
            RequireString(element, "gameRoot"),
            runtimes);
    }

    private static void ExactProperties(JsonElement element, IReadOnlyCollection<string> expected, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{label} must be an object.");
        }
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                throw new InvalidDataException($"{label} contains duplicate property '{property.Name}'.");
            }
        }
        if (!actual.SetEquals(expected))
        {
            throw new InvalidDataException($"{label} properties are invalid.");
        }
    }

    private static string RequireString(JsonElement parent, string name)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidDataException($"Property '{name}' must be a non-empty string.");
        }
        return element.GetString()!;
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return RequireString(parent, name);
    }

    private static bool RequireBoolean(JsonElement parent, string name)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Property '{name}' must be Boolean.");
        }
        return element.GetBoolean();
    }
}
