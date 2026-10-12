namespace Ff7.Accessibility.LegacyLayout;

/// <summary>Shared low/high title-renderer cohort; bounded capture with worker-owned output.</summary>
public sealed class NativeCreditsTitleObservationIngress
{
    private readonly object sync = new();
    private readonly ILegacyAddressSpace memory;
    private readonly NativeCreditsTitleSnapshotReader reader = new();
    private readonly BoundedLatestSnapshot<NativeCaptionObservation> latest = new();
    private int stopped;
    private int originalFault;

    public NativeCreditsTitleObservationIngress(ILegacyAddressSpace memory) =>
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public void OnCallback(NativeCreditsTitleRenderer renderer, Action original)
    {
        ArgumentNullException.ThrowIfNull(original);
        try
        {
            if (Volatile.Read(ref stopped) != 0) { original(); return; }
            var observation = reader.ObserveRendered(memory, renderer, original, () => Volatile.Read(ref stopped) == 0);
            lock (sync) { if (stopped == 0) latest.Publish(observation); }
        }
        catch
        {
            Interlocked.Exchange(ref originalFault, 1);
            Stop();
        }
    }

    public void Stop()
    {
        lock (sync) { Volatile.Write(ref stopped, 1); latest.Publish(null); }
    }
    public bool TryTakeLatest(out NativeCaptionObservation? observation) => latest.TryTake(out observation);
    public bool HasOriginalFault => Volatile.Read(ref originalFault) != 0;
}
