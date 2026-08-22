using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Plays the short cue that marks arriving on a new field map.
/// </summary>
/// <remarks>
/// <para>The legacy runtime has had this since it was written; this one shipped the
/// sound file and compiled no player, so the feature simply did not exist here. The
/// asset's presence is why it went unnoticed - a packaged WAV looks like a working
/// feature from the outside.</para>
///
/// <para>The shared <see cref="FieldZoneTransitionCueTracker"/> does the deciding.
/// What this adds is the part that cannot be shared: reading the module and field
/// identifier out of a translated guest address space safely. The legacy host reads
/// them with two unchecked calls, which is fine there; here a torn pair would
/// announce arrival on a map the player never entered, and a false report is the
/// worst thing this mod can produce.</para>
///
/// <para>Off by default on both runtimes, so this changes nothing audible until
/// <see cref="AccessibilityConfig.EnableFieldZoneTransitionCue"/> is turned on.</para>
/// </remarks>
internal sealed class Steam2026FieldZoneTransitionCueCoordinator : IDisposable
{
    private readonly AccessibilityConfig config;
    private readonly ILegacyAddressSpace addressSpace;
    private readonly FieldZoneTransitionCueTracker tracker;
    private readonly ImmediateWaveCuePlayer? player;
    private readonly Action<string> log;
    private int disposed;

    internal Steam2026FieldZoneTransitionCueCoordinator(
        AccessibilityConfig config,
        ILegacyAddressSpace addressSpace,
        string modDirectory,
        Action<string> log)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        ArgumentException.ThrowIfNullOrWhiteSpace(modDirectory);

        tracker = new FieldZoneTransitionCueTracker(
            TimeSpan.FromMilliseconds(Math.Max(0, config.FieldZoneTransitionCueSettleMs)));

        // Null when the feature is off, mirroring the legacy host's shape, so a
        // disabled cue holds no audio device open.
        player = config.EnableFieldZoneTransitionCue
            ? new ImmediateWaveCuePlayer(
                ResolveSoundPath(modDirectory, config.FieldZoneTransitionCueSoundPath),
                config.FieldZoneTransitionCueVolumePercent,
                "Native Steam 2026 field zone transition cue",
                log)
            : null;

        log(
            "Native Steam 2026 field zone transition cue " +
            $"{(config.EnableFieldZoneTransitionCue ? "enabled" : "disabled")}, " +
            $"settle={Math.Max(0, config.FieldZoneTransitionCueSettleMs)}ms.");
    }

    private static string ResolveSoundPath(string modDirectory, string configured)
    {
        var relative = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine("Assets", "navigation", "field_zone_transition.wav")
            : configured;
        return Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(modDirectory, relative);
    }

    /// <summary>
    /// Takes one reading and plays the cue if the map has genuinely changed.
    /// </summary>
    /// <param name="isHostForeground">
    /// Whether the game is the foreground window. The tracker is advanced either
    /// way - it has to keep following the map or it would fire a stale cue on the
    /// player's return - but nothing is played while they are elsewhere.
    /// </param>
    internal void Observe(bool isHostForeground, DateTime nowUtc)
    {
        if (!config.EnableFieldZoneTransitionCue || Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        if (!TryReadStableState(out var module, out var fieldId))
        {
            // A torn or failed read is not a map change. Returning without
            // observing leaves the settle window intact, so a transient failure
            // cannot restart it and delay a real transition indefinitely.
            return;
        }

        if (!tracker.Observe(module, fieldId, nowUtc))
        {
            return;
        }

        if (!isHostForeground)
        {
            // The tracker has already advanced past this transition, so the cue is
            // dropped rather than queued - returning to the game should not replay
            // a map change the player made minutes ago.
            log(
                "Native Steam 2026 field zone transition cue suppressed while " +
                $"backgrounded (field={tracker.PreviousFieldId}->{tracker.CurrentFieldId}).");
            return;
        }

        player?.Play($"field={tracker.PreviousFieldId}->{tracker.CurrentFieldId}");
    }

    /// <summary>
    /// Reads module and field identifier as one coherent pair.
    /// </summary>
    /// <remarks>
    /// Read, re-read, and require agreement. The identifier is written by the guest
    /// while the map loads, and a pair caught mid-change names a field the player is
    /// not on.
    /// </remarks>
    private bool TryReadStableState(out byte module, out ushort fieldId)
    {
        module = 0;
        fieldId = 0;
        if (!addressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var beforeModule) ||
            !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var beforeField) ||
            !addressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var afterModule) ||
            !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var afterField) ||
            beforeModule != afterModule ||
            beforeField != afterField)
        {
            return false;
        }

        module = beforeModule;
        fieldId = beforeField;
        return true;
    }

    internal void Reset() => tracker.Reset();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        tracker.Reset();
        player?.Dispose();
    }
}
