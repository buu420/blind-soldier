using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Saves;

internal static class Steam2026SaveCandidateDiscoveryTests
{
    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        DiscoversOnlyStableNativeContainerCandidates();
        RejectsTornFileExistenceWithoutPublishingPartialState();
        PublicConstructionRequiresExactFingerprint(supported, unsupported);
        KeepsDiscoveryReadOnlyAndCapabilityNeutral(supported);
    }

    private static void DiscoversOnlyStableNativeContainerCandidates()
    {
        const string steamId = "76561198000000000";
        var localRoot = Path.Combine(Path.GetTempPath(), "ff7-x64-save-discovery-test");
        var expectedRoot = Path.Combine(
            localRoot,
            Steam2026SaveCandidateDiscovery.GameDirectoryName,
            steamId);
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(expectedRoot, "save00.ff7"),
            Path.Combine(expectedRoot, "save09.ff7")
        };
        var discovery = new Steam2026SaveCandidateDiscovery(
            localRoot,
            steamId,
            path => present.Contains(path));

        Equal(true, discovery.TryDiscover(out var candidates), "stable native save discovery");
        Equal(2, candidates.Count, "native save candidate count");
        Equal(0, candidates[0].FileIndex, "manual candidate file index");
        Equal("save00.ff7", candidates[0].FileName, "manual candidate basename");
        Equal(false, candidates[0].ContainsStaticAutosaveTarget, "manual candidate is not autosave container");
        Equal(-1, candidates[0].StaticAutosaveSlotIndex, "manual candidate exposes no autosave slot");
        Equal(9, candidates[1].FileIndex, "autosave container file index");
        Equal("save09.ff7", candidates[1].FileName, "autosave candidate basename");
        Equal(true, candidates[1].ContainsStaticAutosaveTarget, "file nine contains static autosave target");
        Equal(14, candidates[1].StaticAutosaveSlotIndex, "static zero-based autosave slot");
        Equal(
            Path.GetFullPath(Path.Combine(expectedRoot, "save09.ff7")),
            candidates[1].FullPath,
            "native per-SteamID LocalAppData path");
        Equal(
            false,
            candidates.Any(candidate => candidate.FileName.Contains("autosave", StringComparison.OrdinalIgnoreCase)),
            "discovery never invents an autosave filename");
    }

    private static void RejectsTornFileExistenceWithoutPublishingPartialState()
    {
        var calls = 0;
        var discovery = new Steam2026SaveCandidateDiscovery(
            Path.GetTempPath(),
            "76561198000000000",
            path =>
            {
                _ = path;
                calls++;
                return calls == 1;
            });

        Equal(false, discovery.TryDiscover(out var candidates), "torn save candidate set rejected");
        Equal(0, candidates.Count, "torn discovery publishes no partial candidates");

        var throwing = new Steam2026SaveCandidateDiscovery(
            Path.GetTempPath(),
            "76561198000000000",
            _ => throw new InvalidOperationException("simulated diagnostic failure"));
        Equal(false, throwing.TryDiscover(out var failed), "file-system failure rejected");
        Equal(0, failed.Count, "file-system failure publishes no candidates");
    }

    private static void PublicConstructionRequiresExactFingerprint(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        var constructors = typeof(Steam2026SaveCandidateDiscovery).GetConstructors();
        Equal(1, constructors.Length, "save discovery public constructor count");
        Equal(
            typeof(Steam2026FingerprintResult),
            constructors[0].GetParameters()[0].ParameterType,
            "save discovery public constructor requires fingerprint");

        _ = new Steam2026SaveCandidateDiscovery(supported, 76561198000000000UL);
        Equal(
            true,
            Throws<ArgumentException>(() => _ = new Steam2026SaveCandidateDiscovery(
                unsupported,
                76561198000000000UL)),
            "legacy fingerprint rejected by native save discovery");
        Equal(
            true,
            Throws<ArgumentOutOfRangeException>(() => _ = new Steam2026SaveCandidateDiscovery(
                supported,
                0)),
            "zero SteamID rejected by native save discovery");
    }

    private static void KeepsDiscoveryReadOnlyAndCapabilityNeutral(
        Steam2026FingerprintResult supported)
    {
        var type = typeof(Steam2026SaveCandidateDiscovery);
        Equal(false, typeof(IFf7RuntimeBackend).IsAssignableFrom(type), "save discovery is not a backend");
        Equal(false, typeof(IRuntimeEventSink).IsAssignableFrom(type), "save discovery is not an event sink");
        Equal(
            false,
            type.GetMethods().Any(method =>
                method.Name.Contains("Write", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Convert", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Parse", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Speak", StringComparison.OrdinalIgnoreCase)),
            "save discovery exposes no write, conversion, parsing, or speech API");

        using var backend = new Steam2026X64RuntimeBackend(supported);
        Equal(RuntimeCapability.None, backend.ValidateCapabilities().Available, "save discovery does not enable capabilities");
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
