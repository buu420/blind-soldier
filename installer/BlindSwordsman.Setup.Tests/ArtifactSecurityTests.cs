using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlindSwordsman.Setup.Core;

static class ArtifactSecurityTests
{
    public static async Task RunAsync()
    {
        await SelectsNewestEligibleGitHubRelease();
        await RejectsDraftAndWrongTrackReleases();
        await DownloadsAtomicallyAndChecksLengthAndHash();
        await RejectsHashMismatchAndCleansPartialFile();
        ExtractsOnlyManifestedSafeZipEntries();
        RejectsTraversalAbsoluteDuplicateAndUnlistedZipEntries();
        ValidatesCompleteReleasePayloadLayout();
        RejectsReleasePayloadWithoutLauncherBundle();
        RejectsReleasePayloadWithoutPrerequisiteBundle();
    }

    private static async Task SelectsNewestEligibleGitHubRelease()
    {
        var manifest = ValidChannelManifest("0.1.0-pre.2", "v0.1.0-pre.2");
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
            {
                return JsonResponse(ReleaseList(
                    Release("v0.1.0-pre.2", prerelease: true, draft: false),
                    Release("v0.1.0-pre.1", prerelease: true, draft: false)));
            }

            return TextResponse(manifest);
        });
        var client = new GitHubReleaseClient(new HttpClient(handler), "buu420", "blind-soldier");

        var selected = await client.GetLatestAsync(ReleaseTrack.Prerelease, CancellationToken.None);

        Equal("0.1.0-pre.2", selected.Version.ToString(), "newest prerelease version");
        True(handler.Requests.All(request => request.Headers.UserAgent.Count > 0), "GitHub user agent");
    }

    private static async Task RejectsDraftAndWrongTrackReleases()
    {
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
            {
                return JsonResponse(ReleaseList(
                    Release("v9.0.0-pre.1", prerelease: true, draft: true),
                    Release("v1.0.0", prerelease: false, draft: false)));
            }

            return TextResponse(ValidChannelManifest("1.0.0", "v1.0.0", "stable"));
        });
        var client = new GitHubReleaseClient(new HttpClient(handler), "buu420", "blind-soldier");

        await ThrowsAsync<InvalidDataException>(
            () => client.GetLatestAsync(ReleaseTrack.Prerelease, CancellationToken.None),
            "no eligible non-draft prerelease");

        var stable = await client.GetLatestAsync(ReleaseTrack.Stable, CancellationToken.None);
        Equal("1.0.0", stable.Version.ToString(), "stable selection");
    }

    private static async Task DownloadsAtomicallyAndChecksLengthAndHash()
    {
        using var fixture = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("verified runtime payload");
        var asset = new ReleaseAssetDescriptor(
            "Blind-Swordsman-Runtime.zip",
            new Uri("https://github.com/buu420/blind-soldier/releases/download/v1/file.zip"),
            Convert.ToHexString(SHA256.HashData(bytes)),
            bytes.Length);
        var progress = new List<TransferProgress>();
        var downloader = new ArtifactDownloader(
            new HttpClient(new RoutingHandler(_ => BytesResponse(bytes))));

        var path = await downloader.DownloadAsync(
            asset,
            fixture.Path,
            new InlineProgress<TransferProgress>(progress.Add),
            CancellationToken.None);

        Equal(bytes, File.ReadAllBytes(path), "downloaded bytes");
        True(progress.Count > 0 && progress[^1].BytesReceived == bytes.Length, "download completion progress");
        Equal(0, Directory.GetFiles(fixture.Path, "*.partial-*", SearchOption.TopDirectoryOnly).Length, "no partial files");
    }

    private static async Task RejectsHashMismatchAndCleansPartialFile()
    {
        using var fixture = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("tampered");
        var asset = new ReleaseAssetDescriptor(
            "Blind-Swordsman-Runtime.zip",
            new Uri("https://github.com/buu420/blind-soldier/releases/download/v1/file.zip"),
            new string('A', 64),
            bytes.Length);
        var downloader = new ArtifactDownloader(
            new HttpClient(new RoutingHandler(_ => BytesResponse(bytes))));

        await ThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync(asset, fixture.Path, null, CancellationToken.None),
            "download hash mismatch");

        Equal(0, Directory.GetFiles(fixture.Path).Length, "hash failure cleanup");
    }

    private static void ExtractsOnlyManifestedSafeZipEntries()
    {
        using var fixture = new TemporaryDirectory();
        var zipPath = System.IO.Path.Combine(fixture.Path, "payload.zip");
        var content = Encoding.UTF8.GetBytes("runtime");
        CreateZip(zipPath, new Dictionary<string, byte[]>
        {
            ["package/ff7.accessibility.reloaded/ModConfig.json"] = content
        });
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            AddManifest(archive, new Dictionary<string, byte[]>
            {
                ["package/ff7.accessibility.reloaded/ModConfig.json"] = content
            });
        }

        var destination = System.IO.Path.Combine(fixture.Path, "staged");
        var manifest = SafeZipExtractor.ExtractAndValidate(zipPath, destination);

        Equal(1, manifest.Files.Count, "manifest file count");
        Equal(content, File.ReadAllBytes(System.IO.Path.Combine(destination, "package", "ff7.accessibility.reloaded", "ModConfig.json")), "extracted content");
    }

    private static void RejectsTraversalAbsoluteDuplicateAndUnlistedZipEntries()
    {
        foreach (var maliciousName in new[] { "../escape.txt", "/absolute.txt", "C:/drive.txt" })
        {
            using var fixture = new TemporaryDirectory();
            var zipPath = System.IO.Path.Combine(fixture.Path, "bad.zip");
            CreateZip(zipPath, new Dictionary<string, byte[]> { [maliciousName] = [1] });
            Throws<InvalidDataException>(
                () => SafeZipExtractor.ExtractAndValidate(zipPath, System.IO.Path.Combine(fixture.Path, "out")),
                $"malicious ZIP path {maliciousName}");
        }

        using (var fixture = new TemporaryDirectory())
        {
            var zipPath = System.IO.Path.Combine(fixture.Path, "duplicate.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "package/File.txt", [1]);
                WriteEntry(archive, "package/file.txt", [1]);
                AddManifest(archive, new Dictionary<string, byte[]> { ["package/File.txt"] = [1] });
            }

            Throws<InvalidDataException>(
                () => SafeZipExtractor.ExtractAndValidate(zipPath, System.IO.Path.Combine(fixture.Path, "out")),
                "case-insensitive duplicate ZIP path");
        }

        using (var fixture = new TemporaryDirectory())
        {
            var zipPath = System.IO.Path.Combine(fixture.Path, "unlisted.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "package/file.txt", [1]);
                WriteEntry(archive, "package/extra.txt", [2]);
                AddManifest(archive, new Dictionary<string, byte[]> { ["package/file.txt"] = [1] });
            }

            Throws<InvalidDataException>(
                () => SafeZipExtractor.ExtractAndValidate(zipPath, System.IO.Path.Combine(fixture.Path, "out")),
                "unlisted ZIP entry");
        }
    }

    private static void RejectsReleasePayloadWithoutLauncherBundle()
    {
        using var fixture = new TemporaryDirectory();
        var modDirectory = System.IO.Path.Combine(fixture.Path, "package", "ff7.accessibility.reloaded");
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(System.IO.Path.Combine(modDirectory, "ModConfig.json"), "{}");

        Throws<InvalidDataException>(
            () => ReleasePayloadLayoutValidator.Validate(fixture.Path),
            "release payload missing accessible launcher bundle");
    }

    private static void ValidatesCompleteReleasePayloadLayout()
    {
        using var fixture = new TemporaryDirectory();
        CreateValidReleasePayload(fixture.Path);

        var layout = ReleasePayloadLayoutValidator.Validate(fixture.Path);
        Equal(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(fixture.Path, "package", "ff7.accessibility.reloaded")),
            layout.ModPackagePath,
            "validated mod package path");
        Equal(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(fixture.Path, "launcher")),
            layout.LauncherBundlePath,
            "validated launcher path");
        Equal(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(fixture.Path, "prerequisites")),
            layout.PrerequisiteBundlePath,
            "validated prerequisite path");
    }

    private static void RejectsReleasePayloadWithoutPrerequisiteBundle()
    {
        using var fixture = new TemporaryDirectory();
        CreateValidReleasePayload(fixture.Path);
        Directory.Delete(System.IO.Path.Combine(fixture.Path, "prerequisites"), recursive: true);

        Throws<InvalidDataException>(
            () => ReleasePayloadLayoutValidator.Validate(fixture.Path),
            "release payload missing prerequisite bundle");
    }

    private static void CreateValidReleasePayload(string root)
    {
        foreach (var relative in new[]
                 {
                     "package/ff7.accessibility.reloaded/ModConfig.json",
                     "launcher/launcher-bundle.json",
                     "launcher/FFVII_LAUNCHER.exe",
                     "launcher/FFVII_LAUNCHER.exe.config",
                     "launcher/native/x86/FFVII_LAUNCHER.prism.x86.dll",
                     "prerequisites/dependency-bundle.json",
                     "prerequisites/reloaded/Reloaded-II.exe",
                     "prerequisites/reloaded/_asi_extract/ASILoader32.dll",
                     "prerequisites/reloaded/_asi_extract/ASILoader64.dll",
                     "prerequisites/reloaded/Loader/X86/Reloaded.Mod.Loader.dll",
                     "prerequisites/reloaded/Loader/X64/Reloaded.Mod.Loader.dll",
                     "prerequisites/reloaded/Loader/X86/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll",
                     "prerequisites/reloaded/Loader/X64/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll",
                     "prerequisites/shared-hooks/ModConfig.json",
                     "prerequisites/shared-hooks/x86/Reloaded.Hooks.ReloadedII.dll",
                     "prerequisites/shared-hooks/x64/Reloaded.Hooks.ReloadedII.dll",
                     "prerequisites/dotnet/windowsdesktop-runtime-9.0.8-win-x86.exe",
                     "prerequisites/dotnet/windowsdesktop-runtime-9.0.8-win-x64.exe",
                     "prerequisites/notices/THIRD-PARTY-NOTICES.md",
                     "prerequisites/notices/Reloaded-II-GPL-3.0.txt",
                     "prerequisites/notices/Reloaded-Shared-Hooks-LGPL-3.0.txt",
                     "prerequisites/notices/dotnet-LICENSE.txt",
                     "prerequisites/notices/dotnet-THIRD-PARTY-NOTICES.txt"
                 })
        {
            var path = System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fixture");
        }
    }

    private static string ValidChannelManifest(string version, string tag, string track = "prerelease") => $$"""
        {
          "schemaVersion": 1,
          "version": "{{version}}",
          "releaseTag": "{{tag}}",
          "track": "{{track}}",
          "minimumSetupVersion": "0.1.0-pre.1",
          "payload": {
            "name": "Blind-Swordsman-Runtime.zip",
            "url": "https://github.com/buu420/blind-soldier/releases/download/{{tag}}/Blind-Swordsman-Runtime.zip",
            "sha256": "{{new string('A', 64)}}",
            "size": 1234
          },
          "setup": {
            "name": "Blind-Swordsman-Setup.exe",
            "url": "https://github.com/buu420/blind-soldier/releases/download/{{tag}}/Blind-Swordsman-Setup.exe",
            "sha256": "{{new string('B', 64)}}",
            "size": 5678
          }
        }
        """;

    private static object Release(string tag, bool prerelease, bool draft) => new
    {
        tag_name = tag,
        draft,
        prerelease,
        published_at = "2026-08-03T00:00:00Z",
        assets = new[]
        {
            new
            {
                name = "blind-swordsman-channel.json",
                browser_download_url = $"https://github.com/buu420/blind-soldier/releases/download/{tag}/blind-swordsman-channel.json",
                size = 700
            },
            new
            {
                name = "Blind-Swordsman-Runtime.zip",
                browser_download_url = $"https://github.com/buu420/blind-soldier/releases/download/{tag}/Blind-Swordsman-Runtime.zip",
                size = 1234
            },
            new
            {
                name = "Blind-Swordsman-Setup.exe",
                browser_download_url = $"https://github.com/buu420/blind-soldier/releases/download/{tag}/Blind-Swordsman-Setup.exe",
                size = 5678
            }
        }
    };

    private static string ReleaseList(params object[] releases) => JsonSerializer.Serialize(releases);

    private static HttpResponseMessage JsonResponse(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage TextResponse(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/octet-stream")
    };

    private static HttpResponseMessage BytesResponse(byte[] value)
    {
        var content = new ByteArrayContent(value);
        content.Headers.ContentLength = value.Length;
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static void CreateZip(string path, IReadOnlyDictionary<string, byte[]> files)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            WriteEntry(archive, file.Key, file.Value);
        }
    }

    private static void AddManifest(ZipArchive archive, IReadOnlyDictionary<string, byte[]> files)
    {
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            files = files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => new
            {
                path = file.Key.Replace('\\', '/'),
                length = file.Value.LongLength,
                sha256 = Convert.ToHexString(SHA256.HashData(file.Value))
            })
        });
        WriteEntry(archive, "payload-manifest.json", Encoding.UTF8.GetBytes(manifest));
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (expected is byte[] expectedBytes && actual is byte[] actualBytes)
        {
            if (!expectedBytes.SequenceEqual(actualBytes))
            {
                throw new InvalidOperationException($"{label}: byte sequences differ.");
            }
            return;
        }

        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void True(bool value, string label)
    {
        if (!value)
        {
            throw new InvalidOperationException($"{label}: expected true.");
        }
    }

    private static void Throws<TException>(Action action, string label)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action, string label)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(route(request));
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "blind-swordsman-setup-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
