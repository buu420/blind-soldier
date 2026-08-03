namespace Ff7.Accessibility.Steam2026X64.Runtime;

/// <summary>
/// Nonwaiting ownership gate for observations made on native callback threads.
/// The first callback in a cohort may observe. A callback that enters before
/// that observation commits taints the whole cohort, so every overlapping
/// observation is rejected while every native original remains callable.
/// </summary>
internal sealed class NativeIngressObservationGate
{
    private const long ActiveCountMask = (1L << 60) - 1;
    private const long TaintedFlag = 1L << 60;
    private const long CommittedFlag = 1L << 61;
    private const long FlagMask = TaintedFlag | CommittedFlag;

    private long state;

    /// <summary>
    /// Enters one callback without waiting. True grants observation ownership;
    /// false means the caller must invoke only its native original and return.
    /// Every successful call must be paired with <see cref="Exit"/>.
    /// </summary>
    internal bool TryEnter()
    {
        var enteredState = Interlocked.Increment(ref state);
        var activeCount = enteredState & ActiveCountMask;
        var flags = enteredState & FlagMask;
        if (activeCount == 1 && flags == 0)
        {
            return true;
        }

        if ((flags & CommittedFlag) == 0)
        {
            Interlocked.Or(ref state, TaintedFlag);
        }

        return false;
    }

    /// <summary>
    /// Atomically linearizes a fully copied observation only when no callback
    /// overlapped it before this point. Once this succeeds, a concurrent Stop
    /// or fatal transition cannot retract the immutable observation; its one
    /// nonwaiting queue attempt is allowed to finish after that transition.
    /// </summary>
    internal bool TryCommit() =>
        Interlocked.CompareExchange(
            ref state,
            CommittedFlag | 1,
            1) == 1;

    /// <summary>
    /// Invalidates an uncommitted owner without waiting for it.
    /// </summary>
    internal void InvalidateUncommitted()
    {
        var current = Volatile.Read(ref state);
        if ((current & ActiveCountMask) != 0
            && (current & CommittedFlag) == 0)
        {
            Interlocked.Or(ref state, TaintedFlag);
        }
    }

    internal void Exit()
    {
        var exitedState = Interlocked.Decrement(ref state);
        if ((exitedState & ActiveCountMask) == 0)
        {
            Interlocked.CompareExchange(ref state, 0, exitedState);
        }
    }
}
