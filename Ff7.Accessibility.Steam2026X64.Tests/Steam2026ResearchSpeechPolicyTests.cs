using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime;

internal static class Steam2026ResearchSpeechPolicyTests
{
    internal static void Run()
    {
        AssertEqual(
            true,
            Steam2026ResearchSpeechPolicy.CanAnnounceStartup(
                isHostForeground: true,
                lifecycle: null),
            "startup speech does not require translated lifecycle memory");
        AssertEqual(
            false,
            Steam2026ResearchSpeechPolicy.CanAnnounceStartup(
                isHostForeground: false,
                lifecycle: null),
            "startup speech remains foreground-only");
        AssertEqual(
            false,
            Steam2026ResearchSpeechPolicy.CanAnnounceStartup(
                isHostForeground: true,
                lifecycle: new GameLifecycleObservation(
                    IsForeground: false,
                    IsShuttingDown: true,
                    ModuleId: -1,
                    Revision: 0)),
            "known shutdown suppresses startup speech");
        AssertEqual(
            true,
            Steam2026ResearchSpeechPolicy.CanAnnounceStartup(
                isHostForeground: true,
                lifecycle: new GameLifecycleObservation(
                    IsForeground: false,
                    IsShuttingDown: false,
                    ModuleId: -1,
                    Revision: 0)),
            "native host foreground is authoritative for startup speech");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }
}
