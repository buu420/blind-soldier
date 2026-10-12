using System.Diagnostics;

namespace Ff7.Accessibility.Core;

/// <summary>Retains foreground accessibility key edges independently of slow game readers.
/// It observes physical state only; it never emits or consumes native game input.</summary>
public sealed class BufferedKeyboardInput : IDisposable
{
    private static readonly int[] Keys = Enumerable.Range('A', 26)
        .Concat(Enumerable.Range('1', 3)).Concat(Enumerable.Range(0x70, 12)).ToArray();
    private const int MaximumPendingPerKey = 8;
    private readonly object sync = new();
    private readonly Func<bool> foreground;
    private readonly Func<int, bool> isDown;
    private readonly Func<long?> owner;
    private readonly Func<long> clock;
    private readonly long lifetime;
    private readonly Action<string>? log;
    private readonly Func<long, int, IReadOnlyList<int>?>? keyGroups;
    private readonly bool[] held = new bool[256];
    private readonly bool[] physicalActionHeld = new bool[256];
    private readonly Queue<(long Epoch, long Time, long Sequence)>?[] pending = new Queue<(long, long, long)>?[256];
    private readonly HashSet<int> drainedGroups = [];
    private readonly ManualResetEventSlim stop = new(false);
    private readonly AutoResetEvent wake = new(false);
    private readonly Thread? observer;
    private long? lastOwner;
    private long epoch;
    private long sequence;
    private long lastSample;
    private long lastGapLog;
    private string lastAvailability = string.Empty;
    private long lastFailureLog;
    private bool initialized;
    private int disposed;

    public BufferedKeyboardInput(Func<bool> foreground, Func<int, bool> isDown,
        Func<long?> owner, Action<string>? log = null, bool startObserver = true,
        Func<long>? monotonicClock = null, long ticksPerSecond = 0,
        Func<long, int, IReadOnlyList<int>?>? keyGroups = null)
    {
        this.foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        this.isDown = isDown ?? throw new ArgumentNullException(nameof(isDown));
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.log = log;
        this.keyGroups = keyGroups;
        clock = monotonicClock ?? Stopwatch.GetTimestamp;
        lifetime = checked(2 * (ticksPerSecond > 0 ? ticksPerSecond : Stopwatch.Frequency));
        Capture(); // keys already held at attach are a baseline, never a command
        if (startObserver)
        {
            observer = new Thread(Run) { IsBackground = true, Name = "Blind Soldier keyboard observer" };
            observer.Start();
        }
    }

    public static bool Tracks(int virtualKey) => Array.IndexOf(Keys, virtualKey) >= 0;

    /// <summary>A deterministic physical sample; native queries run outside the buffer lock.</summary>
    public void Capture()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        try
        {
            var at = clock();
            var foregroundBefore = foreground();
            var before = owner();
            var states = new bool[Keys.Length];
            for (var i = 0; i < Keys.Length; i++) states[i] = isDown(Keys[i]);
            var after = owner();
            var allowed = foregroundBefore && foreground() && before is not null && before == after;
            string? diagnostic = null;
            var newPress = false;
            lock (sync)
            {
                if (disposed != 0) return;
                var availability = allowed ? "ready" :
                    !foregroundBefore ? "background" : before is null || after is null
                        ? "native input context unavailable" : "native input context changed during sample";
                if (availability != lastAvailability)
                {
                    lastAvailability = availability;
                    diagnostic = "Keyboard accessibility observer: " + availability + ".";
                }
                if (lastSample != 0 && at - lastSample > lifetime / 20 &&
                    at - lastGapLog >= lifetime * 5 / 2)
                {
                    diagnostic = "Keyboard accessibility observer sample gap exceeded 100 ms.";
                    lastGapLog = at;
                }
                lastSample = at;
                if (!allowed || !initialized || lastOwner != after)
                {
                    ClearLocked();
                    initialized = allowed;
                    lastOwner = allowed ? after : null;
                    for (var i = 0; i < Keys.Length; i++)
                        physicalActionHeld[Keys[i]] = held[Keys[i]] = states[i];
                }
                else
                {
                    for (var i = 0; i < Keys.Length; i++)
                    {
                        var key = Keys[i];
                        if (states[i] && !held[key])
                        {
                            var queue = pending[key] ??= new();
                            if (queue.Count == MaximumPendingPerKey)
                            {
                                queue.Dequeue();
                                diagnostic = "Keyboard accessibility input buffer overflow; oldest press retired.";
                            }
                            queue.Enqueue((epoch, at, sequence++));
                            newPress = true;
                        }
                        held[key] = states[i];
                    }
                }
            }
            if (newPress) SignalWorker();
            Report(diagnostic);
        }
        catch (Exception ex)
        {
            Invalidate();
            var at = clock();
            if (lastFailureLog == 0 || at - lastFailureLog >= lifetime * 5 / 2)
            {
                lastFailureLog = at;
                Report($"Keyboard accessibility observer unavailable: {ex.Message}");
            }
        }
    }

    public void BeginWorkerFrame() { lock (sync) drainedGroups.Clear(); }

    public void DiscardPresses(IReadOnlyList<int> keys, Func<long, bool> ownerMatches)
    {
        lock (sync)
        {
            if (disposed != 0 || lastOwner is not { } token || !ownerMatches(token)) return;
            foreach (var key in keys) if (Tracks(key)) pending[key]?.Clear();
        }
    }

    public bool HasPendingPresses(IReadOnlyList<int> keys)
    {
        if (Volatile.Read(ref disposed) != 0 || keys.Any(key => !Tracks(key))) return false;
        long? current;
        try
        {
            var first = owner();
            var focused = foreground();
            current = owner();
            if (!focused || !foreground() || first is null || first != current) { Invalidate(); return false; }
        }
        catch { Invalidate(); return false; }
        var at = clock();
        lock (sync)
        {
            if (disposed != 0 || current != lastOwner) { ClearLocked(); initialized = false; lastOwner = null; return false; }
            foreach (var key in keys)
            {
                var queue = pending[key];
                while (queue?.TryPeek(out var edge) == true)
                {
                    var age = at - edge.Time;
                    if (edge.Epoch != epoch || age < 0 || age > lifetime) { queue.Dequeue(); continue; }
                    return true;
                }
            }
        }
        return false;
    }

    public bool WaitForPressOrTimeout(int milliseconds)
    {
        if (Volatile.Read(ref disposed) != 0) return false;
        try { return wake.WaitOne(Math.Max(0, milliseconds)); } catch (ObjectDisposedException) { return false; }
    }

    public bool TakePress(int virtualKey, bool allowed) => TakePressCore(virtualKey, null, allowed) == virtualKey;

    /// <summary>Current-state action edge, never a banked tap. Native ownership
    /// transitions baseline every action key before a new domain can consume it.</summary>
    public bool ObservePhysicalAction(int virtualKey, bool allowed)
    {
        if (!Tracks(virtualKey) || Volatile.Read(ref disposed) != 0) return false;
        try
        {
            var before = owner();
            var focused = foreground();
            var down = isDown(virtualKey);
            var after = owner();
            focused &= foreground() && before is not null && before == after;
            lock (sync)
            {
                if (disposed != 0 || !focused || !initialized || after != lastOwner)
                {
                    ClearLocked(); initialized = false; lastOwner = null;
                    physicalActionHeld[virtualKey] = down;
                    return false;
                }
                var pressed = down && !physicalActionHeld[virtualKey];
                physicalActionHeld[virtualKey] = down;
                return allowed && pressed;
            }
        }
        catch { Invalidate(); return false; }
    }

    public int TakeOldestPress(IReadOnlyList<int> virtualKeys, bool allowed) => TakePressCore(0, virtualKeys, allowed);

    private int TakePressCore(int virtualKey, IReadOnlyList<int>? suppliedKeys, bool allowed)
    {
        if (suppliedKeys is null && !Tracks(virtualKey) || Volatile.Read(ref disposed) != 0) return 0;
        long? current;
        bool focused;
        try
        {
            var first = owner();
            focused = foreground();
            current = owner();
            focused &= foreground() && first is not null && first == current;
        }
        catch { Invalidate(); return 0; }
        var group = suppliedKeys ?? (current is { } token ? keyGroups?.Invoke(token, virtualKey) : null);
        var keys = group ?? new[] { virtualKey };
        if (keys.Count == 0 || keys.Any(key => !Tracks(key))) return 0;
        string? diagnostic = null;
        var pressed = 0;
        var morePresses = false;
        var at = clock();
        lock (sync)
        {
            if (!focused || current is null || current != lastOwner || disposed != 0)
            {
                ClearLocked();
                initialized = false;
                lastOwner = null;
            }
            else if (!allowed)
            {
                if (suppliedKeys is not null) foreach (var key in keys) pending[key]?.Clear();
                else pending[virtualKey]?.Clear();
            }
            else if (group is null || !drainedGroups.Contains(keys[0]))
            {
                var oldestKey = 0;
                var oldestSequence = long.MaxValue;
                foreach (var key in keys)
                {
                    var queue = pending[key];
                    while (queue?.TryPeek(out var edge) == true)
                    {
                        var age = at - edge.Time;
                        if (edge.Epoch != epoch || age < 0 || age > lifetime)
                        {
                            queue.Dequeue();
                            diagnostic = "Keyboard accessibility press retired after its input context or deadline changed.";
                            continue;
                        }
                        if (edge.Sequence < oldestSequence) { oldestSequence = edge.Sequence; oldestKey = key; }
                        break;
                    }
                }
                if (oldestKey != 0 && (virtualKey == 0 || virtualKey == oldestKey))
                {
                    pending[oldestKey]!.Dequeue();
                    if (group is not null) drainedGroups.Add(keys[0]);
                    pressed = oldestKey;
                    diagnostic = $"Keyboard accessibility press: key=0x{oldestKey:X2}, buffered=True.";
                    // AutoResetEvent coalesces capture signals. Keep the active command
                    // group awake until its ordered backlog has been consumed.
                    morePresses = keys.Any(key => pending[key]?.TryPeek(out var edge) == true &&
                        edge.Epoch == epoch && at - edge.Time >= 0 && at - edge.Time <= lifetime);
                }
            }
        }
        if (morePresses) SignalWorker();
        Report(diagnostic);
        return pressed;
    }

    private void SignalWorker()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        try { wake.Set(); } catch (ObjectDisposedException) { }
    }

    public void Invalidate()
    {
        lock (sync) { ClearLocked(); initialized = false; lastOwner = null; }
    }

    private void ClearLocked()
    {
        epoch++;
        drainedGroups.Clear();
        foreach (var queue in pending) queue?.Clear();
    }

    private void Run()
    {
        while (!stop.Wait(8) && Volatile.Read(ref disposed) == 0) Capture();
    }

    private void Report(string? diagnostic)
    {
        if (diagnostic is null) return;
        try { log?.Invoke(diagnostic); } catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Invalidate();
        stop.Set();
        wake.Set();
        if (observer is null || observer.Join(1000)) { stop.Dispose(); wake.Dispose(); }
        else Report("Keyboard accessibility observer is retiring its last native read.");
    }
}
