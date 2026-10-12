namespace Ff7.Accessibility.LegacyLayout;

/// <summary>One pending observation, including null as an explicit invalidation.</summary>
public sealed class BoundedLatestSnapshot<T> where T : class
{
    private readonly object sync = new();
    private T? latest;
    private bool pending;
    public void Publish(T? value) { lock (sync) { latest = value; pending = true; } }
    public bool TryTake(out T? value)
    {
        lock (sync)
        {
            value = pending ? latest : null;
            if (!pending) return false;
            latest = null; pending = false; return true;
        }
    }
}

/// <summary>Pure managed bounded capture; it neither installs hooks nor speaks.</summary>
public sealed class NativeCreditsObservationIngress
{
    private readonly object sync = new();
    private readonly ILegacyAddressSpace memory;
    private readonly Action original;
    private readonly NativeCreditsSnapshotReader reader = new();
    private readonly BoundedLatestSnapshot<NativeCreditsSnapshot> latest = new();
    private int stopped;
    private int originalFault;
    public NativeCreditsObservationIngress(ILegacyAddressSpace memory, Action original)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.original = original ?? throw new ArgumentNullException(nameof(original));
    }
    public void OnCallback()
    {
        var shouldCapture = Volatile.Read(ref stopped) == 0;
        var before = shouldCapture ? ReadSnapshot() : null;
        try { original(); }
        catch
        {
            Interlocked.Exchange(ref originalFault, 1);
            Stop();
            return;
        }
        if (!shouldCapture || Volatile.Read(ref stopped) != 0) return;
        var after = ReadSnapshot();
        var stable = before is not null && after is not null
            && before.FirstRowIndex == after.FirstRowIndex && before.ScrollOffset == after.ScrollOffset
            && before.VisibleRows.SequenceEqual(after.VisibleRows);
        lock (sync)
        {
            if (stopped == 0) latest.Publish(stable ? before! with { ObservedUtc = DateTime.UtcNow } : null);
        }
    }
    public void Stop()
    {
        lock (sync) { Volatile.Write(ref stopped, 1); latest.Publish(null); }
    }
    public bool TryTakeLatest(out NativeCreditsSnapshot? snapshot) => latest.TryTake(out snapshot);
    public bool HasOriginalFault => Volatile.Read(ref originalFault) != 0;
    private NativeCreditsSnapshot? ReadSnapshot()
    {
        try { return reader.TryRead(memory, rendererObserved: true, out var snapshot) ? snapshot : null; }
        catch { return null; }
    }
}
