using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class NativeCreditsTitleObservationConsumer(ILegacyAddressSpace memory, NativeObservedCaptionNarrator narrator)
{
    private NativeCaptionObservation? latest;
    private readonly NativeCreditsTitleSnapshotReader reader = new();
    public void Tick(bool hasObservation, NativeCaptionObservation? observation,
        DateTime nowUtc, bool enabled, bool foreground)
    {
        if (hasObservation) latest = observation;
        if (memory.TryReadByte(NativeCreditsSnapshotReader.AddressCurrentModule, out var module)
            && memory.TryReadByte(NativeCreditsSnapshotReader.AddressCurrentModule, out var afterModule) && module == afterModule)
        {
            var ended = module != 28;
            if (!ended && memory.TryReadInt32(NativeCreditsSnapshotReader.AddressCreditPhase, out var phase)
                && memory.TryReadInt32(NativeCreditsSnapshotReader.AddressCreditPhase, out var afterPhase) && phase == afterPhase)
                ended = phase is not (1 or 2);
            if (ended) { latest = null; narrator.EndEpisode(); return; }
        }
        narrator.Observe(reader.IsCurrentTitleVisible(memory) ? latest : null, nowUtc, enabled, foreground);
    }
}
