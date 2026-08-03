using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlindSwordsman.Setup.Core;

public static partial class ReleaseManifestParser
{
    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "version",
        "releaseTag",
        "track",
        "minimumSetupVersion",
        "payload",
        "setup"
    ];

    private static readonly string[] AssetProperties = ["name", "url", "sha256", "size"];

    public static ReleaseChannelManifest Parse(string json, ReleaseTrack expectedTrack)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var root = document.RootElement;
            RequireExactProperties(root, RootProperties, "channel manifest");

            var schemaVersion = RequireInt32(root, "schemaVersion");
            if (schemaVersion != 1)
            {
                throw new InvalidDataException($"Unsupported channel manifest schema {schemaVersion}.");
            }

            var version = ParseVersion(RequireString(root, "version"), "version");
            var releaseTag = RequireString(root, "releaseTag");
            if (!string.Equals(releaseTag, $"v{version}", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Release tag does not match the manifest version.");
            }

            var track = RequireString(root, "track") switch
            {
                "stable" => ReleaseTrack.Stable,
                "prerelease" => ReleaseTrack.Prerelease,
                var value => throw new InvalidDataException($"Unknown release track '{value}'.")
            };
            if (track != expectedTrack)
            {
                throw new InvalidDataException($"Manifest track {track} does not match requested track {expectedTrack}.");
            }

            if ((track == ReleaseTrack.Prerelease) != version.IsPrerelease)
            {
                throw new InvalidDataException("Manifest version and release track disagree.");
            }

            var minimumSetupVersion = ParseVersion(
                RequireString(root, "minimumSetupVersion"),
                "minimum setup version");
            var payload = ParseAsset(root.GetProperty("payload"), "Blind-Swordsman-Runtime.zip", "payload");
            var setup = ParseAsset(root.GetProperty("setup"), "Blind-Swordsman-Setup.exe", "setup");
            if (string.Equals(payload.Name, setup.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Channel manifest contains duplicate asset names.");
            }

            return new ReleaseChannelManifest(
                schemaVersion,
                version,
                releaseTag,
                track,
                minimumSetupVersion,
                payload,
                setup);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Channel manifest is malformed.", exception);
        }
    }

    private static ReleaseAssetDescriptor ParseAsset(JsonElement element, string expectedName, string label)
    {
        RequireExactProperties(element, AssetProperties, $"{label} asset");
        var name = RequireString(element, "name");
        if (!string.Equals(name, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected {label} asset name '{name}'.");
        }

        var urlText = RequireString(element, "url");
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
            !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{label} asset URL is not a trusted HTTPS GitHub URL.");
        }

        var expectedSuffix = "/" + Uri.EscapeDataString(name);
        if (!url.AbsolutePath.EndsWith(expectedSuffix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{label} asset URL does not name '{name}'.");
        }

        var sha256 = RequireString(element, "sha256").ToUpperInvariant();
        if (!Sha256Pattern().IsMatch(sha256))
        {
            throw new InvalidDataException($"{label} asset SHA-256 is invalid.");
        }

        var size = RequireInt64(element, "size");
        if (size <= 0)
        {
            throw new InvalidDataException($"{label} asset size must be positive.");
        }

        return new ReleaseAssetDescriptor(name, url, sha256, size);
    }

    private static SemanticVersion ParseVersion(string value, string label)
    {
        try
        {
            return SemanticVersion.Parse(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"Manifest {label} is invalid.", exception);
        }
    }

    private static void RequireExactProperties(JsonElement element, IReadOnlyCollection<string> expected, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{label} must be a JSON object.");
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                throw new InvalidDataException($"{label} contains duplicate property '{property.Name}'.");
            }
        }

        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expectedSet))
        {
            var missing = expectedSet.Except(actual).Order(StringComparer.Ordinal);
            var unknown = actual.Except(expectedSet).Order(StringComparer.Ordinal);
            throw new InvalidDataException(
                $"{label} properties are invalid; missing=[{string.Join(',', missing)}], unknown=[{string.Join(',', unknown)}].");
        }
    }

    private static string RequireString(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"Property '{name}' must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static int RequireInt32(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new InvalidDataException($"Property '{name}' must be a 32-bit integer.");
        }

        return result;
    }

    private static long RequireInt64(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw new InvalidDataException($"Property '{name}' must be a 64-bit integer.");
        }

        return result;
    }

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
