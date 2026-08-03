namespace Ff7.Accessibility.Reloaded;

internal sealed class NativeFieldMessageIdentity : IEquatable<NativeFieldMessageIdentity>
{
    public NativeFieldMessageIdentity(
        FieldOpcodeKind kind,
        int fieldId,
        int windowId,
        int dialogId,
        long lifecycleToken = 0)
    {
        Kind = kind;
        FieldId = fieldId;
        WindowId = windowId;
        DialogId = dialogId;
        LifecycleToken = lifecycleToken;
    }

    public FieldOpcodeKind Kind { get; }

    public int FieldId { get; }

    public int WindowId { get; }

    public int DialogId { get; }

    public long LifecycleToken { get; }

    internal NativeFieldSpeechLifecycleState SpeechLifecycle { get; } = new();

    public bool IsValid =>
        Kind is FieldOpcodeKind.Message or FieldOpcodeKind.Ask &&
        FieldId >= 0 &&
        WindowId is >= 0 and < FieldMessageReader.WindowCount &&
        DialogId >= 0;

    public bool Equals(NativeFieldMessageIdentity? other) =>
        other is not null &&
        Kind == other.Kind &&
        FieldId == other.FieldId &&
        WindowId == other.WindowId &&
        DialogId == other.DialogId &&
        LifecycleToken == other.LifecycleToken;

    public override bool Equals(object? obj) =>
        obj is NativeFieldMessageIdentity other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, FieldId, WindowId, DialogId, LifecycleToken);

    public static bool operator ==(
        NativeFieldMessageIdentity? left,
        NativeFieldMessageIdentity? right) =>
        ReferenceEquals(left, right) || left?.Equals(right) == true;

    public static bool operator !=(
        NativeFieldMessageIdentity? left,
        NativeFieldMessageIdentity? right) =>
        !(left == right);
}

internal sealed class NativeFieldSpeechLifecycleState
{
    private long state;

    public bool IsClosed => (Interlocked.Read(ref state) & 1L) != 0;

    public bool TryCommitEmission()
    {
        while (true)
        {
            var current = Interlocked.Read(ref state);
            if ((current & 1L) != 0 || current > long.MaxValue - 2)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref state, current + 2, current) == current)
            {
                return true;
            }
        }
    }

    public void Close()
    {
        while (true)
        {
            var current = Interlocked.Read(ref state);
            if ((current & 1L) != 0 ||
                Interlocked.CompareExchange(ref state, current | 1L, current) == current)
            {
                return;
            }
        }
    }
}

internal sealed class NativeFieldMessageOwnershipTracker
{
    private readonly TimeSpan inactiveLifetime;
    private readonly object sync = new();
    private readonly Dictionary<NativeFieldMessageIdentity, OwnershipState> ownership = [];

    public NativeFieldMessageOwnershipTracker(TimeSpan inactiveLifetime)
    {
        this.inactiveLifetime = inactiveLifetime < TimeSpan.Zero
            ? TimeSpan.Zero
            : inactiveLifetime;
    }

    public void ObserveNative(NativeFieldMessageIdentity identity, string? text, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid ||
            Ff7EncodedTextDecoder.NormalizeWhitespace(text ?? string.Empty).Length == 0)
        {
            return;
        }

        lock (sync)
        {
            RemoveExpired(now);
            ownership[identity] = new OwnershipState(
                now,
                NativeFieldSpeechDelivery.Pending);
        }
    }

    public void MarkSpeechDelivered(
        NativeFieldMessageIdentity identity,
        DateTime now,
        bool visibleContentComplete = true)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid)
        {
            return;
        }

        lock (sync)
        {
            RemoveExpired(now, identity);
            if (ownership.ContainsKey(identity))
            {
                ownership[identity] = new OwnershipState(
                    now,
                    visibleContentComplete
                        ? NativeFieldSpeechDelivery.Complete
                        : NativeFieldSpeechDelivery.Partial);
            }
        }
    }

    public bool WasSpeechDelivered(
        NativeFieldMessageIdentity? identity,
        DateTime now,
        bool preserveActiveIdentity = false)
    {
        if (identity is null || !identity.IsValid)
        {
            return false;
        }

        lock (sync)
        {
            RemoveExpired(now, preserveActiveIdentity ? identity : null);
            return ownership.TryGetValue(identity, out var state) &&
                state.Delivery == NativeFieldSpeechDelivery.Complete;
        }
    }

    public bool ShouldSuppressPolling(
        int windowId,
        NativeFieldMessageIdentity? activeIdentity,
        byte activeMessageCount,
        DateTime now,
        bool nativeSpeechPending = false)
    {
        if (activeMessageCount == 0)
        {
            Reset();
            return false;
        }

        if (activeIdentity is null ||
            !activeIdentity.IsValid ||
            windowId != activeIdentity.WindowId)
        {
            lock (sync)
            {
                RemoveExpired(now);
            }

            return false;
        }

        lock (sync)
        {
            // Exact native lifecycle callbacks explicitly release ownership.
            // Do not age out the currently active ASK merely because the user
            // leaves it open or Prism is unavailable for longer than the stale
            // cleanup horizon.
            RemoveExpired(now, activeIdentity);
            return ownership.TryGetValue(activeIdentity, out var state) &&
                state.Delivery != NativeFieldSpeechDelivery.Partial;
        }
    }

    public void Release(NativeFieldMessageIdentity? identity)
    {
        if (identity is null)
        {
            return;
        }

        lock (sync)
        {
            ownership.Remove(identity);
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            ownership.Clear();
        }
    }

    private void RemoveExpired(
        DateTime now,
        NativeFieldMessageIdentity? preservedPendingIdentity = null)
    {
        foreach (var identity in ownership
                     .Where(item =>
                         item.Key != preservedPendingIdentity &&
                         now - item.Value.ObservedAt > inactiveLifetime)
                     .Select(item => item.Key)
                     .ToArray())
        {
            ownership.Remove(identity);
        }
    }

    private readonly record struct OwnershipState(
        DateTime ObservedAt,
        NativeFieldSpeechDelivery Delivery);

    private enum NativeFieldSpeechDelivery
    {
        Pending,
        Partial,
        Complete
    }
}
