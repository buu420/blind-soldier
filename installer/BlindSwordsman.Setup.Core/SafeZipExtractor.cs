using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlindSwordsman.Setup.Core;

public sealed record PayloadFile(string Path, long Length, string Sha256);

public sealed record PayloadManifest(int SchemaVersion, IReadOnlyList<PayloadFile> Files);

public static partial class SafeZipExtractor
{
    private const string ManifestName = "payload-manifest.json";
    private const long MaximumManifestSize = 4L * 1024 * 1024;
    private const long MaximumPayloadSize = 2L * 1024 * 1024 * 1024;

    public static PayloadManifest ExtractAndValidate(string zipPath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var archivePath = Path.GetFullPath(zipPath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("Runtime payload ZIP was not found.", archivePath);
        }

        var destination = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new InvalidDataException("Payload staging directory must be empty.");
        }

        Directory.CreateDirectory(destination);
        var destinationInfo = new DirectoryInfo(destination);
        if ((destinationInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Payload staging directory cannot be a reparse point.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entries = PreflightEntries(archive, destination);
            if (!entries.TryGetValue(ManifestName, out var manifestEntry) || manifestEntry.Length > MaximumManifestSize)
            {
                throw new InvalidDataException("Runtime ZIP is missing a bounded payload manifest.");
            }

            PayloadManifest manifest;
            using (var reader = new StreamReader(manifestEntry.Open(), detectEncodingFromByteOrderMarks: true))
            {
                manifest = ParsePayloadManifest(reader.ReadToEnd());
            }

            var expectedPaths = manifest.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualPaths = entries.Keys.Where(path => !string.Equals(path, ManifestName, StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!expectedPaths.SetEquals(actualPaths))
            {
                throw new InvalidDataException("Runtime ZIP entries do not exactly match the payload manifest.");
            }

            foreach (var file in manifest.Files)
            {
                var entry = entries[file.Path];
                if (entry.Length != file.Length)
                {
                    throw new InvalidDataException($"Payload length mismatch for '{file.Path}'.");
                }

                var targetPath = ResolveSafePath(destination, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                using var source = entry.Open();
                using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                long length = 0;
                while (true)
                {
                    var count = source.Read(buffer, 0, buffer.Length);
                    if (count == 0)
                    {
                        break;
                    }

                    length = checked(length + count);
                    if (length > file.Length)
                    {
                        throw new InvalidDataException($"Payload expanded beyond its declared length for '{file.Path}'.");
                    }

                    target.Write(buffer, 0, count);
                    hash.AppendData(buffer, 0, count);
                }

                var actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if (length != file.Length || !HashVerifier.FixedTimeEquals(file.Sha256, actualHash))
                {
                    throw new InvalidDataException($"Payload integrity validation failed for '{file.Path}'.");
                }
            }

            File.WriteAllText(Path.Combine(destination, ManifestName), JsonSerializer.Serialize(new
            {
                schemaVersion = manifest.SchemaVersion,
                files = manifest.Files.Select(file => new { path = file.Path, length = file.Length, sha256 = file.Sha256 })
            }, new JsonSerializerOptions { WriteIndented = true }));
            return manifest;
        }
        catch
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
            throw;
        }
    }

    private static Dictionary<string, ZipArchiveEntry> PreflightEntries(ZipArchive archive, string destination)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeArchivePath(entry.FullName);
            if (IsLinkOrReparsePoint(entry))
            {
                throw new InvalidDataException($"Runtime ZIP contains a link or reparse point: {entry.FullName}");
            }

            ResolveSafePath(destination, normalized);
            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            if (isDirectory)
            {
                if (entry.Length != 0)
                {
                    throw new InvalidDataException($"Runtime ZIP directory entry has data: {entry.FullName}");
                }
                continue;
            }

            totalLength = checked(totalLength + entry.Length);
            if (totalLength > MaximumPayloadSize)
            {
                throw new InvalidDataException("Runtime ZIP expands beyond the supported size.");
            }

            if (!entries.TryAdd(normalized, entry))
            {
                throw new InvalidDataException($"Runtime ZIP contains duplicate path '{normalized}'.");
            }
        }

        return entries;
    }

    private static PayloadManifest ParsePayloadManifest(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireExactProperties(root, ["schemaVersion", "files"], "payload manifest");
            if (!root.GetProperty("schemaVersion").TryGetInt32(out var schemaVersion) || schemaVersion != 1)
            {
                throw new InvalidDataException("Unsupported payload manifest schema.");
            }

            var filesElement = root.GetProperty("files");
            if (filesElement.ValueKind != JsonValueKind.Array || filesElement.GetArrayLength() == 0)
            {
                throw new InvalidDataException("Payload manifest has no files.");
            }

            var files = new List<PayloadFile>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? priorPath = null;
            long totalLength = 0;
            foreach (var element in filesElement.EnumerateArray())
            {
                RequireExactProperties(element, ["path", "length", "sha256"], "payload file");
                var path = NormalizeArchivePath(element.GetProperty("path").GetString() ?? string.Empty);
                if (string.Equals(path, ManifestName, StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                {
                    throw new InvalidDataException($"Payload manifest contains duplicate or reserved path '{path}'.");
                }

                if (priorPath is not null && string.CompareOrdinal(priorPath, path) >= 0)
                {
                    throw new InvalidDataException("Payload manifest file paths must be unique and ordinally sorted.");
                }
                priorPath = path;

                if (!element.GetProperty("length").TryGetInt64(out var length) || length < 0)
                {
                    throw new InvalidDataException($"Payload length is invalid for '{path}'.");
                }
                totalLength = checked(totalLength + length);
                if (totalLength > MaximumPayloadSize)
                {
                    throw new InvalidDataException("Payload manifest exceeds the supported expanded size.");
                }

                var sha256 = (element.GetProperty("sha256").GetString() ?? string.Empty).ToUpperInvariant();
                if (!Sha256Pattern().IsMatch(sha256))
                {
                    throw new InvalidDataException($"Payload SHA-256 is invalid for '{path}'.");
                }

                files.Add(new PayloadFile(path, length, sha256));
            }

            return new PayloadManifest(schemaVersion, files);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("Payload manifest is malformed.", exception);
        }
    }

    private static void RequireExactProperties(JsonElement element, IReadOnlyCollection<string> expected, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{label} must be an object.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException($"{label} contains duplicate property '{property.Name}'.");
            }
        }

        if (!names.SetEquals(expected))
        {
            throw new InvalidDataException($"{label} properties are invalid.");
        }
    }

    private static string NormalizeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("Runtime ZIP contains an empty or invalid path.");
        }

        var normalized = path.Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0 || normalized.StartsWith('/') || normalized.Contains(':'))
        {
            throw new InvalidDataException($"Runtime ZIP contains an absolute path '{path}'.");
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException($"Runtime ZIP contains an unsafe path '{path}'.");
        }

        return string.Join('/', segments);
    }

    private static string ResolveSafePath(string root, string archivePath)
    {
        var target = Path.GetFullPath(Path.Combine(root, archivePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Runtime ZIP path escapes staging: {archivePath}");
        }

        return target;
    }

    private static bool IsLinkOrReparsePoint(ZipArchiveEntry entry)
    {
        const int unixFileTypeMask = 0xF000;
        const int unixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & unixFileTypeMask;
        var dosAttributes = entry.ExternalAttributes & 0xFFFF;
        return unixMode == unixSymbolicLink || (dosAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
