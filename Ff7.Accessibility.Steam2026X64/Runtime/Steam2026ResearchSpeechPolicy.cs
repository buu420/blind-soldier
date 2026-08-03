using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64.Runtime;

internal static class Steam2026ResearchSpeechPolicy
{
    internal static bool CanAnnounceStartup(
        bool isHostForeground,
        GameLifecycleObservation? lifecycle) =>
        isHostForeground && lifecycle?.IsShuttingDown != true;
}
