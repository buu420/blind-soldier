using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>A renderer observation expires; a cached people.bin pool is never visibility evidence.</summary>
public sealed class NativeCreditsObservationConsumer
{
    private readonly ILegacyAddressSpace memory;
    private readonly NativeCreditsNarrator narrator;
    private readonly NativeCreditsSnapshotReader reader = new();
    private NativeCreditsSnapshot? latest;

    public NativeCreditsObservationConsumer(ILegacyAddressSpace memory, NativeCreditsNarrator narrator)
    {
        this.memory = memory;
        this.narrator = narrator;
    }
    public void Tick(bool hasObservation, NativeCreditsSnapshot? observation,
        DateTime nowUtc, bool enabled, bool foreground)
    {
        if (hasObservation) latest = observation;
        if (memory.TryReadByte(NativeCreditsSnapshotReader.AddressCurrentModule, out var module)
            && memory.TryReadByte(NativeCreditsSnapshotReader.AddressCurrentModule, out var moduleAfter)
            && module == moduleAfter)
        {
            var left = module != NativeCreditsSnapshotReader.StaffCreditsModule;
            if (!left && memory.TryReadInt32(NativeCreditsSnapshotReader.AddressCreditPhase, out var phase)
                && memory.TryReadInt32(NativeCreditsSnapshotReader.AddressCreditPhase, out var phaseAfter)
                && phase == phaseAfter) left = phase != 0;
            if (left)
            {
                latest = null;
                narrator.EndEpisode();
                return;
            }
        }
        var observedRecently = latest is { } before && before.ObservedUtc <= nowUtc
            && nowUtc - before.ObservedUtc <= TimeSpan.FromMilliseconds(500);
        // Recheck the current viewport, phase and row bytes, under the recent actual
        // renderer observation. This cannot extend that observation's lifetime.
        var readable = reader.TryRead(memory, observedRecently, out var current);
        if (readable && current is not null && latest is not null)
        {
            var observed = latest.VisibleRows;
            current = current with
            {
                VisibleRows = current.VisibleRows.Where(row => observed.Any(seen => seen.Index == row.Index
                    && seen.Style == row.Style && string.Equals(seen.Text, row.Text, StringComparison.Ordinal))).ToArray()
            };
        }
        narrator.Observe(readable ? current : null, nowUtc, enabled, foreground);
    }
}
