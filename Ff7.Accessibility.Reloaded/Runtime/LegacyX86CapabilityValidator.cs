using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Reloaded.Runtime;

public static class LegacyX86CapabilityValidator
{
    private static readonly IReadOnlyDictionary<RuntimeCapability, IReadOnlyList<string>> Signals =
        new Dictionary<RuntimeCapability, IReadOnlyList<string>>
        {
            [RuntimeCapability.Lifecycle] =
                Array.AsReadOnly(["executable-fingerprint", "module-state", "shutdown-lifecycle"]),
            [RuntimeCapability.ForegroundInput] =
                Array.AsReadOnly(["foreground-window", "native-input-state"]),
            [RuntimeCapability.Menus] =
                Array.AsReadOnly([
                    "menu-text-render",
                    "in-game-menu-text-draw",
                    "menu-cursor-draw",
                    "menu-widget-update",
                    "title-menu-state"]),
            [RuntimeCapability.Dialogue] =
                Array.AsReadOnly([
                    "field-message-open",
                    "field-message-preview",
                    "field-opcode-message",
                    "field-opcode-ask",
                    "field-dialogue-draw"]),
            [RuntimeCapability.Field] =
                Array.AsReadOnly([
                    "field-module",
                    "field-id",
                    "field-model-state",
                    "field-script-state"]),
            [RuntimeCapability.Navigation] =
                Array.AsReadOnly([
                    "field-position",
                    "walkmesh",
                    "field-control",
                    "field-exits",
                    "field-objects",
                    "ladder-state"]),
            [RuntimeCapability.Battle] =
                Array.AsReadOnly([
                    "battle-update",
                    "battle-menu-render",
                    "battle-text",
                    "battle-targets",
                    "battle-results",
                    "battle-damage",
                    "battle-status"]),
            [RuntimeCapability.Movies] =
                Array.AsReadOnly(["movie-file-handle", "movie-lifecycle"]),
            [RuntimeCapability.Saves] =
                Array.AsReadOnly(["savemap-state", "load-menu-state"])
        };

    public static IReadOnlyDictionary<RuntimeCapability, IReadOnlyList<string>> RequiredSignals => Signals;

    public static RuntimeCapabilityReport Validate(
        RuntimeIdentity identity,
        IEnumerable<string> resolvedSignals)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(resolvedSignals);

        var resolved = new HashSet<string>(resolvedSignals, StringComparer.Ordinal);
        var failures = new List<RuntimeCapabilityFailure>();
        var available = RuntimeCapability.None;
        var fingerprintMatches = !identity.Is64Bit
                                 && string.Equals(
                                     identity.Sha256,
                                     LegacyX86Fingerprint.SupportedSha256,
                                     StringComparison.OrdinalIgnoreCase);

        foreach (var pair in Signals)
        {
            var missing = pair.Value.Where(signal => !resolved.Contains(signal)).ToArray();
            if (pair.Key == RuntimeCapability.Lifecycle && !fingerprintMatches)
            {
                failures.Add(new RuntimeCapabilityFailure(
                    RuntimeCapability.Lifecycle,
                    "executable-fingerprint",
                    "The executable is not the supported legacy x86 build."));
            }

            foreach (var signal in missing)
            {
                failures.Add(new RuntimeCapabilityFailure(
                    pair.Key,
                    signal,
                    $"Required legacy signal '{signal}' was not resolved."));
            }

            if (missing.Length == 0 && (pair.Key != RuntimeCapability.Lifecycle || fingerprintMatches))
            {
                available |= pair.Key;
            }
        }

        return new RuntimeCapabilityReport(identity, available, failures);
    }
}
