using System.Collections.ObjectModel;
using System.Globalization;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Saves;

/// <summary>
/// Discovers only the statically verified native save-container basenames.
/// It deliberately does not open, parse, convert, order, or speak save data.
/// </summary>
public sealed class Steam2026SaveCandidateDiscovery
{
    public const string GameDirectoryName = "FINAL FANTASY VII Steam Edition";
    public const int ContainerCount = 10;
    public const int StaticAutosaveContainerIndex = 9;
    public const int StaticAutosaveSlotIndex = 14;

    private readonly string saveRoot;
    private readonly Func<string, bool> fileExists;

    public Steam2026SaveCandidateDiscovery(
        Steam2026FingerprintResult fingerprint,
        ulong steamId64)
        : this(
            ValidateAndGetLocalAppData(fingerprint, steamId64),
            steamId64.ToString(CultureInfo.InvariantCulture),
            File.Exists)
    {
    }

    internal Steam2026SaveCandidateDiscovery(
        string localAppDataRoot,
        string steamId64,
        Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId64);
        if (steamId64.Length > 20 || steamId64.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException("The SteamID64 directory component must contain only decimal digits.", nameof(steamId64));
        }

        this.fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        var canonicalLocalRoot = Path.GetFullPath(localAppDataRoot);
        saveRoot = Path.GetFullPath(Path.Combine(
            canonicalLocalRoot,
            GameDirectoryName,
            steamId64));
        var expectedPrefix = canonicalLocalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalLocalRoot
            : canonicalLocalRoot + Path.DirectorySeparatorChar;
        if (!saveRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The native save root escaped LocalAppData.");
        }
    }

    public bool TryDiscover(
        out IReadOnlyList<Steam2026SaveContainerCandidate> candidates)
    {
        candidates = Array.Empty<Steam2026SaveContainerCandidate>();
        try
        {
            var paths = new string[ContainerCount];
            var before = new bool[ContainerCount];
            var after = new bool[ContainerCount];
            for (var index = 0; index < ContainerCount; index++)
            {
                paths[index] = Path.GetFullPath(Path.Combine(
                    saveRoot,
                    $"save{index:00}.ff7"));
                before[index] = fileExists(paths[index]);
            }

            for (var index = 0; index < ContainerCount; index++)
            {
                after[index] = fileExists(paths[index]);
                if (after[index] != before[index])
                {
                    return false;
                }
            }

            var stable = new List<Steam2026SaveContainerCandidate>(ContainerCount);
            for (var index = 0; index < ContainerCount; index++)
            {
                if (!before[index])
                {
                    continue;
                }

                stable.Add(new Steam2026SaveContainerCandidate(
                    index,
                    $"save{index:00}.ff7",
                    paths[index],
                    index == StaticAutosaveContainerIndex,
                    index == StaticAutosaveContainerIndex
                        ? StaticAutosaveSlotIndex
                        : -1));
            }

            candidates = new ReadOnlyCollection<Steam2026SaveContainerCandidate>(stable);
            return true;
        }
        catch
        {
            candidates = Array.Empty<Steam2026SaveContainerCandidate>();
            return false;
        }
    }

    private static string ValidateAndGetLocalAppData(
        Steam2026FingerprintResult fingerprint,
        ulong steamId64)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (!fingerprint.IsSupported ||
            !fingerprint.Identity.Is64Bit ||
            !string.Equals(
                fingerprint.Identity.RuntimeId,
                Steam2026Fingerprint.SupportedRuntimeId,
                StringComparison.Ordinal) ||
            !string.Equals(
                fingerprint.Identity.Sha256,
                Steam2026Fingerprint.SupportedSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Native save discovery requires the exact supported Steam 2026 x64 executable fingerprint.",
                nameof(fingerprint));
        }

        if (steamId64 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steamId64));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The current user's LocalAppData directory is unavailable.");
        }

        return localAppData;
    }
}

public sealed record Steam2026SaveContainerCandidate(
    int FileIndex,
    string FileName,
    string FullPath,
    bool ContainsStaticAutosaveTarget,
    int StaticAutosaveSlotIndex);
