using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Ff7.Accessibility.Reloaded;

public static partial class Ff7GameLanguageDetector
{
    private const long PolishTranslationWindowBinLength = 13_170;
    private const string PolishTranslationWindowBinSha256 =
        "84886B3F59DFB302A2936B3924E8C468790D582C3F11FC0508106DA42A01FEA3";

    public static Ff7GameLanguageContext Detect(
        string gameRootDirectory,
        string? configuredLanguage = "auto",
        string? executablePath = null,
        IReadOnlyList<string>? steamManifestPaths = null,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRootDirectory);

        var dataDirectory = ResolveDataDirectory(gameRootDirectory);
        var configured = configuredLanguage?.Trim();
        if (!string.IsNullOrEmpty(configured) &&
            !string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (Ff7GameLanguages.TryParse(configured, out var overrideLanguage))
            {
                if (HasUsableKernel(dataDirectory, overrideLanguage))
                {
                    return Select(overrideLanguage, dataDirectory, Ff7GameLanguageDetectionSource.Configuration, $"configuration '{configured}'", log);
                }

                log?.Invoke($"Blind Soldier language override '{configured}' has no usable kernel data; continuing automatic detection.");
            }
            else
            {
                log?.Invoke($"Blind Soldier language override '{configured}' is not supported; continuing automatic detection.");
            }
        }

        if (TryReadFanTranslationFingerprint(dataDirectory, out var translatedLanguage) &&
            HasUsableKernel(dataDirectory, translatedLanguage))
        {
            return Select(
                translatedLanguage,
                dataDirectory,
                Ff7GameLanguageDetectionSource.TranslationFingerprint,
                "installed Polish translation font",
                log);
        }

        if (TryReadExecutableLanguage(executablePath, out var executableLanguage) &&
            HasUsableKernel(dataDirectory, executableLanguage))
        {
            return Select(executableLanguage, dataDirectory, Ff7GameLanguageDetectionSource.Executable, $"executable '{Path.GetFileName(executablePath)}'", log);
        }

        var manifests = steamManifestPaths ?? DiscoverSteamManifests(gameRootDirectory);
        foreach (var manifestPath in manifests)
        {
            if (!TryReadMatchingSteamLanguage(manifestPath, gameRootDirectory, out var manifestLanguage) ||
                !HasUsableKernel(dataDirectory, manifestLanguage))
            {
                continue;
            }

            return Select(manifestLanguage, dataDirectory, Ff7GameLanguageDetectionSource.SteamManifest, $"Steam manifest '{manifestPath}'", log);
        }

        var installed = Ff7GameLanguages.All
            .Where(language => HasUsableKernel(dataDirectory, language))
            .ToArray();
        if (installed.Length == 1)
        {
            return Select(installed[0], dataDirectory, Ff7GameLanguageDetectionSource.SingleInstalledLanguage, "only installed language data", log);
        }

        var english = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        return Select(english, dataDirectory, Ff7GameLanguageDetectionSource.EnglishFallback, "automatic English fallback", log);
    }

    public static string ResolveDataDirectory(string gameRootDirectory)
    {
        var normalizedRoot = Path.GetFullPath(gameRootDirectory);
        var candidates = new[]
        {
            string.Equals(Path.GetFileName(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar)), "data", StringComparison.OrdinalIgnoreCase)
                ? normalizedRoot
                : string.Empty,
            Path.Combine(normalizedRoot, "data"),
            Path.Combine(normalizedRoot, "ff7", "workingdir", "data")
        };

        return candidates.FirstOrDefault(candidate =>
                   !string.IsNullOrEmpty(candidate) &&
                   Directory.Exists(candidate)) ??
               Path.Combine(normalizedRoot, "data");
    }

    private static Ff7GameLanguageContext Select(
        Ff7GameLanguageDescriptor language,
        string dataDirectory,
        Ff7GameLanguageDetectionSource source,
        string detail,
        Action<string>? log)
    {
        var context = new Ff7GameLanguageContext(language, dataDirectory, source, detail);
        log?.Invoke($"Blind Soldier language: {context.DisplayName} ({context.Code}), selected from {detail}.");
        return context;
    }

    private static bool HasUsableKernel(string dataDirectory, Ff7GameLanguageDescriptor language) =>
        File.Exists(Path.Combine(dataDirectory, language.LanguageDirectoryName, "kernel", "kernel2.bin"));

    private static bool TryReadFanTranslationFingerprint(
        string dataDirectory,
        out Ff7GameLanguageDescriptor language)
    {
        language = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        var windowBinPath = Path.Combine(dataDirectory, "lang-en", "kernel", "WINDOW.BIN");
        try
        {
            using var stream = File.OpenRead(windowBinPath);
            if (stream.Length != PolishTranslationWindowBinLength)
            {
                return false;
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(stream));
            return TryMatchFanTranslationFingerprint(stream.Length, sha256, out language);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }

    internal static bool TryMatchFanTranslationFingerprint(
        long fileLength,
        string? sha256,
        out Ff7GameLanguageDescriptor language)
    {
        language = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        if (fileLength != PolishTranslationWindowBinLength ||
            !string.Equals(sha256, PolishTranslationWindowBinSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        language = Ff7GameLanguages.PolishFanTranslation;
        return true;
    }

    private static bool TryReadExecutableLanguage(string? executablePath, out Ff7GameLanguageDescriptor language)
    {
        language = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(executablePath);
        foreach (var candidate in Ff7GameLanguages.All)
        {
            if (fileName.EndsWith($"_{candidate.Code}", StringComparison.OrdinalIgnoreCase))
            {
                language = candidate;
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> DiscoverSteamManifests(string gameRootDirectory)
    {
        var manifests = new List<string>();
        var current = new DirectoryInfo(Path.GetFullPath(gameRootDirectory));
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
        {
            foreach (var appId in new[] { "3837340", "39140" })
            {
                var candidate = Path.Combine(current.FullName, $"appmanifest_{appId}.acf");
                if (File.Exists(candidate))
                {
                    manifests.Add(candidate);
                }
            }
        }

        return manifests;
    }

    private static bool TryReadMatchingSteamLanguage(
        string manifestPath,
        string gameRootDirectory,
        out Ff7GameLanguageDescriptor language)
    {
        language = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        try
        {
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            var content = File.ReadAllText(manifestPath);
            var pairs = KeyValuePairPattern().Matches(content)
                .Select(match => new KeyValuePair<string, string>(match.Groups[1].Value, match.Groups[2].Value))
                .ToArray();
            var installDirectory = pairs.LastOrDefault(pair =>
                string.Equals(pair.Key, "installdir", StringComparison.OrdinalIgnoreCase)).Value;
            var steamLanguage = pairs.LastOrDefault(pair =>
                string.Equals(pair.Key, "language", StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrWhiteSpace(installDirectory) || string.IsNullOrWhiteSpace(steamLanguage))
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(gameRootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootSegments = normalizedRoot.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!rootSegments.Any(segment => string.Equals(segment, installDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return Ff7GameLanguages.TryParse(steamLanguage, out language);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    [GeneratedRegex("\\\"([^\\\"]+)\\\"\\s+\\\"([^\\\"]*)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValuePairPattern();
}
