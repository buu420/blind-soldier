using System.Net.Http.Headers;
using System.Text.Json;

namespace BlindSwordsman.Setup.Core;

public sealed class GitHubReleaseClient
{
    public const string ChannelAssetName = "blind-swordsman-channel.json";

    private readonly HttpClient httpClient;
    private readonly string owner;
    private readonly string repository;

    public GitHubReleaseClient(HttpClient httpClient, string owner, string repository)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.owner = ValidateSegment(owner, nameof(owner));
        this.repository = ValidateSegment(repository, nameof(repository));
    }

    public async Task<ReleaseChannelManifest> GetLatestAsync(
        ReleaseTrack track,
        CancellationToken cancellationToken)
    {
        var releasesUri = new Uri($"https://api.github.com/repos/{owner}/{repository}/releases?per_page=30");
        using var releasesRequest = CreateRequest(releasesUri);
        using var releasesResponse = await httpClient.SendAsync(
            releasesRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        releasesResponse.EnsureSuccessStatusCode();
        await using var responseStream = await releasesResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
            responseStream,
            new JsonDocumentOptions { MaxDepth = 10 },
            cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("GitHub releases response is not an array.");
        }

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object ||
                !TryBoolean(release, "draft", out var draft) ||
                !TryBoolean(release, "prerelease", out var prerelease) ||
                draft ||
                prerelease != (track == ReleaseTrack.Prerelease) ||
                !TryString(release, "tag_name", out var tag) ||
                !release.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var assetMap = ParseAssets(assets);
            if (!assetMap.TryGetValue(ChannelAssetName, out var channelAsset) ||
                !assetMap.ContainsKey("Blind-Swordsman-Runtime.zip") ||
                !assetMap.ContainsKey("Blind-Swordsman-Setup.exe"))
            {
                continue;
            }

            var channelText = await DownloadTextAsync(channelAsset.Url, cancellationToken).ConfigureAwait(false);
            var manifest = ReleaseManifestParser.Parse(channelText, track);
            if (!string.Equals(manifest.ReleaseTag, tag, StringComparison.Ordinal))
            {
                throw new InvalidDataException("GitHub release tag does not match its channel manifest.");
            }

            AssertAssetMatchesApi(manifest.Payload, assetMap);
            AssertAssetMatchesApi(manifest.Setup, assetMap);
            return manifest;
        }

        throw new InvalidDataException($"No eligible {track.ToString().ToLowerInvariant()} Blind Soldier release is available.");
    }

    private async Task<string> DownloadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > 1024 * 1024)
        {
            throw new InvalidDataException("Release channel manifest is unexpectedly large.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, ApiAsset> ParseAssets(JsonElement assets)
    {
        var result = new Dictionary<string, ApiAsset>(StringComparer.Ordinal);
        foreach (var element in assets.EnumerateArray())
        {
            if (!TryString(element, "name", out var name) ||
                !TryString(element, "browser_download_url", out var urlText) ||
                !element.TryGetProperty("size", out var sizeElement) ||
                !sizeElement.TryGetInt64(out var size) ||
                size <= 0 ||
                !Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
                !IsTrustedGitHubDownload(url))
            {
                throw new InvalidDataException("GitHub release contains malformed asset metadata.");
            }

            if (!result.TryAdd(name, new ApiAsset(url, size)))
            {
                throw new InvalidDataException($"GitHub release contains duplicate asset '{name}'.");
            }
        }

        return result;
    }

    private static void AssertAssetMatchesApi(
        ReleaseAssetDescriptor descriptor,
        IReadOnlyDictionary<string, ApiAsset> apiAssets)
    {
        if (!apiAssets.TryGetValue(descriptor.Name, out var apiAsset) ||
            descriptor.Size != apiAsset.Size ||
            descriptor.Url != apiAsset.Url)
        {
            throw new InvalidDataException($"Channel manifest metadata for '{descriptor.Name}' does not match GitHub.");
        }
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        if (!IsTrustedGitHubDownload(uri) && !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Refusing a non-GitHub release request.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Blind-Swordsman-Setup", "0.1"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static bool IsTrustedGitHubDownload(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static bool TryBoolean(JsonElement parent, string name, out bool value)
    {
        value = false;
        return parent.TryGetProperty(name, out var element) &&
            (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False) &&
            (value = element.GetBoolean()) == element.GetBoolean();
    }

    private static bool TryString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static string ValidateSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("GitHub owner and repository names contain an invalid character.", parameterName);
        }

        return value;
    }

    private sealed record ApiAsset(Uri Url, long Size);
}
