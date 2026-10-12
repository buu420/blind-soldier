namespace Ff7.Accessibility.Reloaded;

public delegate bool NativeCreatureDrawArgumentReader(out uint polygonSet, out uint gameObject);
internal delegate bool NativeCreatureApproachCapture(uint polygonSet, uint gameObject, out FieldCreatureApproachSample sample);

/// <summary>Read-only renderer observations; all output is owned by the runtime's bounded queue.</summary>
public sealed class NativeCreatureDrawObservationIngress
{
    private readonly object sync = new();
    private readonly NativeCreatureApproachCapture tryCapture;
    private readonly Func<FieldCreatureApproachSample, bool> isCurrent;
    private readonly Action<FieldCreatureApproachSample, DateTime> capture;
    private readonly Func<DateTime> utcNow;
    private int stopped;
    private int originalFault;
    private int captureFault;
    public NativeCreatureDrawObservationIngress(FieldCreatureApproachReader reader, Action<FieldCreatureApproachSample, DateTime> capture)
        : this(reader.TryCapture, sample => reader.IsCurrent(sample), capture) { }
    internal NativeCreatureDrawObservationIngress(NativeCreatureApproachCapture tryCapture,
        Func<FieldCreatureApproachSample, bool> isCurrent, Action<FieldCreatureApproachSample, DateTime> capture,
        Func<DateTime>? utcNow = null)
    {
        this.tryCapture = tryCapture ?? throw new ArgumentNullException(nameof(tryCapture));
        this.isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
    }
    public bool HasOriginalFault => Volatile.Read(ref originalFault) != 0;
    public bool HasCaptureFault => Volatile.Read(ref captureFault) != 0;
    public void OnCallback(uint polygonSet, uint gameObject, Action<uint, uint> original) =>
        Observe(() => Capture(polygonSet, gameObject), () => original(polygonSet, gameObject));
    public void OnCallback(NativeCreatureDrawArgumentReader arguments, Action original) =>
        Observe(() => arguments(out var polygonSet, out var gameObject) ? Capture(polygonSet, gameObject) : null, original);
    private FieldCreatureApproachSample? Capture(uint polygonSet, uint gameObject) =>
        tryCapture(polygonSet, gameObject, out var sample) ? sample : null;
    private void Observe(Func<FieldCreatureApproachSample?> beforeOriginal, Action original)
    {
        FieldCreatureApproachSample? sample = null;
        if (Volatile.Read(ref stopped) == 0)
        {
            try { sample = beforeOriginal(); }
            catch { Interlocked.Exchange(ref captureFault, 1); }
        }
        // This is the only Original invocation. Read/publication failures must never retry it.
        try { original(); }
        catch { Interlocked.Exchange(ref originalFault, 1); Stop(); return; }
        if (sample is null || Volatile.Read(ref stopped) != 0) return;
        try
        {
            if (!isCurrent(sample.Value)) return;
            lock (sync)
            {
                if (stopped == 0) capture(sample.Value, utcNow());
            }
        }
        catch { Interlocked.Exchange(ref captureFault, 1); }
    }
    public void Stop()
    {
        lock (sync) { Volatile.Write(ref stopped, 1); }
    }
}
