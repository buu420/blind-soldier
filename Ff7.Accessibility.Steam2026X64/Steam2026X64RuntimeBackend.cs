using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64;

public sealed class Steam2026X64RuntimeBackend : IFf7RuntimeBackend
{
    private static readonly IReadOnlyList<RuntimeCapabilityFailure> ResearchFailures =
    [
        new(
            RuntimeCapability.Lifecycle,
            "runtime-lifecycle-ingress",
            "The exact lifecycle reader is offline-tested, but backend frame publication and live load, suspend, resume, and unload evidence are incomplete."),
        new(
            RuntimeCapability.ForegroundInput,
            "foreground-input-command-routing",
            "Foreground ownership and rising-edge sampling are offline-tested, but backend command routing and live focus-transition evidence are incomplete."),
        new(
            RuntimeCapability.Menus,
            "translated-menu-detour-ingress",
            "Checked menu and name-entry readers are offline-tested, but validated translated detours, same-frame native text correlation, and live menu coverage are incomplete."),
        new(
            RuntimeCapability.Dialogue,
            "dialogue-hook-ingress",
            "Ordinary visible-window and ASK callback contracts are offline-tested, but validated callback ingress, overlapping-window handling, and live dialogue coverage are incomplete."),
        new(
            RuntimeCapability.Field,
            "field-frame-publication",
            "Checked translated field observations are offline-tested, but backend publication and live module, model, script, and zone-transition evidence are incomplete."),
        new(
            RuntimeCapability.Navigation,
            "navigation-world-completeness",
            "Position, control, boundary, and gateway observations are offline-tested, but complete walkmesh, entity, route, cue, and live navigation parity are incomplete."),
        new(
            RuntimeCapability.Battle,
            "battle-event-ingress",
            "The exact six-callback battle cohort and checked worker trackers are offline-tested with unsensed-enemy redaction, but live battle parity remains unverified."),
        new(
            RuntimeCapability.Movies,
            "native-movie-hook-installation",
            "Exact native movie callback contracts and ingress coordination are offline-tested, but hooks, live lifecycle timing, and narration synchronization are incomplete."),
        new(
            RuntimeCapability.Saves,
            "native-save-samples",
            "Native save paths and candidate discovery are known, but no validated native manual or autosave samples and visible UI correlations exist on this machine.")
    ];

    public Steam2026X64RuntimeBackend(Steam2026FingerprintResult fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        var identity = fingerprint.Identity;
        if (!fingerprint.IsSupported
            || !identity.Is64Bit
            || !string.Equals(
                identity.RuntimeId,
                Steam2026Fingerprint.SupportedRuntimeId,
                StringComparison.Ordinal)
            || !string.Equals(
                identity.Sha256,
                Steam2026Fingerprint.SupportedSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The Steam 2026 x64 backend requires a validated supported fingerprint. {fingerprint.Diagnostic}",
                nameof(fingerprint));
        }

        Identity = identity;
    }

    public RuntimeIdentity Identity { get; }

    public RuntimeCapabilityReport ValidateCapabilities() =>
        new(Identity, RuntimeCapability.None, ResearchFailures);

    public void Start(IRuntimeEventSink eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        throw new InvalidOperationException(
            "The native Steam 2026 backend is research-only until every required capability passes parity validation.");
    }

    public RuntimeFrameObservation ReadFrame() =>
        throw new InvalidOperationException("The incomplete native Steam 2026 backend cannot publish frames.");

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
