using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlindSwordsman.Setup.Core;

public sealed record InstalledGame(string Version, string GameRoot);

public sealed record InstalledMod(
    string Directory,
    string Fingerprint,
    string? BackupPath,
    string? BackupFingerprint);

public sealed record InstalledProfile(
    string Path,
    bool Changed,
    string InstalledSha256,
    string? BackupPath,
    string? BackupSha256,
    bool Research);

public sealed record InstalledLoader(
    string Id,
    string Target,
    string Sha256,
    bool Changed);

public sealed record OpeningVoiceState(
    bool WasPresent,
    string Target,
    string SourceSha256);

public sealed record FfnxState(string ReleaseTag, string AssetName);

public sealed record InstallState(
    int SchemaVersion,
    SemanticVersion ProductVersion,
    string ReleaseTag,
    DateTimeOffset InstalledAtUtc,
    InstalledGame Game,
    string ReloadedRoot,
    InstalledMod Mod,
    InstalledProfile? Profile,
    IReadOnlyList<InstalledLoader> Loaders,
    OpeningVoiceState OpeningVoice,
    FfnxState? Ffnx);

public static partial class DeploymentResultParser
{
    public static InstallState Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
            var root = document.RootElement;
            Exact(root,
                ["schemaVersion", "productVersion", "releaseTag", "installedAtUtc", "game", "reloadedRoot", "mod", "profile", "loaders", "openingVoice", "ffnx"],
                "install state");
            if (!root.GetProperty("schemaVersion").TryGetInt32(out var schema) || schema != 1)
            {
                throw new InvalidDataException("Unsupported install-state schema.");
            }

            var version = ParseVersion(RequiredString(root, "productVersion"));
            var releaseTag = RequiredString(root, "releaseTag");
            if (!string.Equals(releaseTag, $"v{version}", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Install-state release tag does not match product version.");
            }
            if (!DateTimeOffset.TryParse(
                    RequiredString(root, "installedAtUtc"),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var installedAt))
            {
                throw new InvalidDataException("Install-state timestamp is invalid.");
            }

            var gameElement = root.GetProperty("game");
            Exact(gameElement, ["version", "gameRoot"], "installed game");
            var game = new InstalledGame(
                RequiredString(gameElement, "version"),
                RequiredString(gameElement, "gameRoot"));

            var modElement = root.GetProperty("mod");
            Exact(modElement, ["directory", "fingerprint", "backupPath", "backupFingerprint"], "installed mod");
            var modBackupPath = OptionalString(modElement, "backupPath");
            var modBackupFingerprint = OptionalString(modElement, "backupFingerprint");
            if ((modBackupPath is null) != (modBackupFingerprint is null))
            {
                throw new InvalidDataException("Mod backup path and fingerprint must both be present or absent.");
            }
            var mod = new InstalledMod(
                RequiredString(modElement, "directory"),
                RequiredString(modElement, "fingerprint"),
                modBackupPath,
                modBackupFingerprint);

            InstalledProfile? profile = null;
            var profileElement = root.GetProperty("profile");
            if (profileElement.ValueKind != JsonValueKind.Null)
            {
                Exact(profileElement,
                    ["path", "changed", "installedSha256", "backupPath", "backupSha256", "research"],
                    "installed profile");
                var backupPath = OptionalString(profileElement, "backupPath");
                var backupHash = OptionalString(profileElement, "backupSha256");
                if ((backupPath is null) != (backupHash is null))
                {
                    throw new InvalidDataException("Profile backup path and hash must both be present or absent.");
                }
                profile = new InstalledProfile(
                    RequiredString(profileElement, "path"),
                    RequiredBoolean(profileElement, "changed"),
                    RequiredHash(profileElement, "installedSha256"),
                    backupPath,
                    backupHash is null ? null : ValidateHash(backupHash, "backupSha256"),
                    RequiredBoolean(profileElement, "research"));
            }

            var loadersElement = root.GetProperty("loaders");
            if (loadersElement.ValueKind != JsonValueKind.Array || loadersElement.GetArrayLength() == 0)
            {
                throw new InvalidDataException("Install state contains no loader records.");
            }
            var loaderIds = new HashSet<string>(StringComparer.Ordinal);
            var loaderTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var loaders = new List<InstalledLoader>();
            foreach (var loaderElement in loadersElement.EnumerateArray())
            {
                Exact(loaderElement, ["id", "target", "sha256", "changed"], "installed loader");
                var id = RequiredString(loaderElement, "id");
                var target = RequiredString(loaderElement, "target");
                if (!loaderIds.Add(id) || !loaderTargets.Add(target))
                {
                    throw new InvalidDataException("Install state contains duplicate loader identity or target.");
                }
                loaders.Add(new InstalledLoader(
                    id,
                    target,
                    RequiredHash(loaderElement, "sha256"),
                    RequiredBoolean(loaderElement, "changed")));
            }

            var voiceElement = root.GetProperty("openingVoice");
            Exact(voiceElement, ["wasPresent", "target", "sourceSha256"], "opening voice state");
            var openingVoice = new OpeningVoiceState(
                RequiredBoolean(voiceElement, "wasPresent"),
                RequiredString(voiceElement, "target"),
                RequiredHash(voiceElement, "sourceSha256"));

            FfnxState? ffnx = null;
            var ffnxElement = root.GetProperty("ffnx");
            if (ffnxElement.ValueKind != JsonValueKind.Null)
            {
                Exact(ffnxElement, ["releaseTag", "assetName"], "FFNx state");
                ffnx = new FfnxState(
                    RequiredString(ffnxElement, "releaseTag"),
                    RequiredString(ffnxElement, "assetName"));
            }

            return new InstallState(
                schema,
                version,
                releaseTag,
                installedAt,
                game,
                RequiredString(root, "reloadedRoot"),
                mod,
                profile,
                loaders,
                openingVoice,
                ffnx);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException("Install state is malformed.", exception);
        }
    }

    public static string Serialize(InstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", state.SchemaVersion);
            writer.WriteString("productVersion", state.ProductVersion.ToString());
            writer.WriteString("releaseTag", state.ReleaseTag);
            writer.WriteString("installedAtUtc", state.InstalledAtUtc.UtcDateTime.ToString("O"));
            writer.WriteStartObject("game");
            writer.WriteString("version", state.Game.Version);
            writer.WriteString("gameRoot", state.Game.GameRoot);
            writer.WriteEndObject();
            writer.WriteString("reloadedRoot", state.ReloadedRoot);
            writer.WriteStartObject("mod");
            writer.WriteString("directory", state.Mod.Directory);
            writer.WriteString("fingerprint", state.Mod.Fingerprint);
            WriteNullableString(writer, "backupPath", state.Mod.BackupPath);
            WriteNullableString(writer, "backupFingerprint", state.Mod.BackupFingerprint);
            writer.WriteEndObject();
            if (state.Profile is null)
            {
                writer.WriteNull("profile");
            }
            else
            {
                writer.WriteStartObject("profile");
                writer.WriteString("path", state.Profile.Path);
                writer.WriteBoolean("changed", state.Profile.Changed);
                writer.WriteString("installedSha256", state.Profile.InstalledSha256);
                WriteNullableString(writer, "backupPath", state.Profile.BackupPath);
                WriteNullableString(writer, "backupSha256", state.Profile.BackupSha256);
                writer.WriteBoolean("research", state.Profile.Research);
                writer.WriteEndObject();
            }
            writer.WriteStartArray("loaders");
            foreach (var loader in state.Loaders)
            {
                writer.WriteStartObject();
                writer.WriteString("id", loader.Id);
                writer.WriteString("target", loader.Target);
                writer.WriteString("sha256", loader.Sha256);
                writer.WriteBoolean("changed", loader.Changed);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartObject("openingVoice");
            writer.WriteBoolean("wasPresent", state.OpeningVoice.WasPresent);
            writer.WriteString("target", state.OpeningVoice.Target);
            writer.WriteString("sourceSha256", state.OpeningVoice.SourceSha256);
            writer.WriteEndObject();
            if (state.Ffnx is null)
            {
                writer.WriteNull("ffnx");
            }
            else
            {
                writer.WriteStartObject("ffnx");
                writer.WriteString("releaseTag", state.Ffnx.ReleaseTag);
                writer.WriteString("assetName", state.Ffnx.AssetName);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static SemanticVersion ParseVersion(string value)
    {
        try
        {
            return SemanticVersion.Parse(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Install-state product version is invalid.", exception);
        }
    }

    private static void Exact(JsonElement element, IReadOnlyCollection<string> expected, string label)
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

    private static string RequiredString(JsonElement parent, string name)
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
        return element.ValueKind == JsonValueKind.Null ? null : RequiredString(parent, name);
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Property '{name}' must be Boolean.");
        }
        return element.GetBoolean();
    }

    private static string RequiredHash(JsonElement parent, string name) =>
        ValidateHash(RequiredString(parent, name), name);

    private static string ValidateHash(string value, string label)
    {
        var normalized = value.ToUpperInvariant();
        if (!Sha256Pattern().IsMatch(normalized))
        {
            throw new InvalidDataException($"Property '{label}' is not a SHA-256 value.");
        }
        return normalized;
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
