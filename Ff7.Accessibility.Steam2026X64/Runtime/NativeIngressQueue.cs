namespace Ff7.Accessibility.Steam2026X64.Runtime;

/// <summary>
/// Nonblocking publication boundary for data copied by a native callback.
/// Implementations must return immediately and must not invoke consumers.
/// </summary>
internal interface INativeIngressQueue<in T>
{
    bool TryEnqueue(T item);
}

internal delegate T NativeIngressSequenceAssigner<T>(T item, long sequence);

/// <summary>
/// Assigns an item's monotonic sequence from the same reservation that fixes
/// its position in the native-ingress ring.
/// </summary>
internal interface ISequencedNativeIngressQueue<T> : INativeIngressQueue<T>
{
    bool TryEnqueueSequenced(T item, NativeIngressSequenceAssigner<T> assignSequence);
}

/// <summary>
/// Fixed-capacity, preallocated native-ingress ring. Producers reserve one
/// slot with atomic operations; overflow is reported without locks, waits,
/// replacement, or dropping an already-queued observation.
/// </summary>
internal sealed class BoundedNativeIngressQueue<T> : ISequencedNativeIngressQueue<T>
{
    private readonly Slot[] slots;
    private readonly int capacity;
    private int reservedCount;
    private long nextEnqueuePosition = -1;
    private long nextDequeuePosition;
    private int unusable;

    internal BoundedNativeIngressQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.capacity = capacity;
        slots = new Slot[Math.Max(capacity, 2)];
        for (var index = 0; index < slots.Length; index++)
        {
            slots[index].Sequence = index;
        }
    }

    public bool TryEnqueue(T item) => TryEnqueueCore(item, assignSequence: null);

    public bool TryEnqueueSequenced(
        T item,
        NativeIngressSequenceAssigner<T> assignSequence)
    {
        ArgumentNullException.ThrowIfNull(assignSequence);
        return TryEnqueueCore(item, assignSequence);
    }

    private bool TryEnqueueCore(
        T item,
        NativeIngressSequenceAssigner<T>? assignSequence)
    {
        if (Volatile.Read(ref unusable) != 0)
        {
            return false;
        }

        var reservation = Interlocked.Increment(ref reservedCount);
        if (reservation > capacity)
        {
            Interlocked.Decrement(ref reservedCount);
            return false;
        }

        var position = Interlocked.Increment(ref nextEnqueuePosition);
        ref var slot = ref slots[GetSlotIndex(position)];
        if (Volatile.Read(ref slot.Sequence) != position)
        {
            Interlocked.Exchange(ref unusable, 1);
            Interlocked.Decrement(ref reservedCount);
            return false;
        }

        if (assignSequence is not null)
        {
            try
            {
                item = assignSequence(item, position + 1);
            }
            catch
            {
                Interlocked.Exchange(ref unusable, 1);
                Interlocked.Decrement(ref reservedCount);
                return false;
            }
        }

        slot.Item = item;
        Volatile.Write(ref slot.Sequence, position + 1);
        return true;
    }

    internal bool TryDequeue(out T item)
    {
        var position = Volatile.Read(ref nextDequeuePosition);
        ref var slot = ref slots[GetSlotIndex(position)];
        if (Volatile.Read(ref slot.Sequence) == position + 1
            && Interlocked.CompareExchange(
                ref nextDequeuePosition,
                position + 1,
                position) == position)
        {
            item = slot.Item;
            slot.Item = default!;
            Volatile.Write(ref slot.Sequence, position + slots.Length);
            Interlocked.Decrement(ref reservedCount);
            return true;
        }

        item = default!;
        return false;
    }

    private int GetSlotIndex(long position) =>
        (int)((ulong)position % (uint)slots.Length);

    private struct Slot
    {
        internal long Sequence;
        internal T Item;
    }
}
