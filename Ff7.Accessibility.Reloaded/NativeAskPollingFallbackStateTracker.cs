namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Tracks the two independently delivered halves of an ASK whose native prompt
/// fell back to polling: the visible question and the exact highlighted row.
/// Registration is idempotent for one exact lifecycle token.
/// </summary>
internal sealed class NativeAskPollingFallbackStateTracker
{
    private readonly HashSet<NativeFieldMessageIdentity> fallback = [];
    private readonly HashSet<NativeFieldMessageIdentity> recoveryPending = [];
    private readonly HashSet<NativeFieldMessageIdentity> questionRecovered = [];
    private readonly HashSet<NativeFieldMessageIdentity> choiceDelivered = [];

    public bool Begin(NativeFieldMessageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid || !fallback.Add(identity))
        {
            return false;
        }

        recoveryPending.Add(identity);
        questionRecovered.Remove(identity);
        choiceDelivered.Remove(identity);
        return true;
    }

    public bool IsFallback(NativeFieldMessageIdentity identity) =>
        fallback.Contains(identity);

    public bool IsRecoveryPending(NativeFieldMessageIdentity identity) =>
        recoveryPending.Contains(identity);

    public bool IsQuestionRecovered(NativeFieldMessageIdentity identity) =>
        questionRecovered.Contains(identity);

    public bool MarkQuestionRecovered(NativeFieldMessageIdentity identity)
    {
        if (!fallback.Contains(identity))
        {
            return false;
        }

        questionRecovered.Add(identity);
        return CompleteIfBothDelivered(identity);
    }

    public bool MarkChoiceDelivered(NativeFieldMessageIdentity identity)
    {
        if (!fallback.Contains(identity))
        {
            return false;
        }

        choiceDelivered.Add(identity);
        return CompleteIfBothDelivered(identity);
    }

    public void Remove(NativeFieldMessageIdentity identity)
    {
        fallback.Remove(identity);
        recoveryPending.Remove(identity);
        questionRecovered.Remove(identity);
        choiceDelivered.Remove(identity);
    }

    public void Clear()
    {
        fallback.Clear();
        recoveryPending.Clear();
        questionRecovered.Clear();
        choiceDelivered.Clear();
    }

    private bool CompleteIfBothDelivered(NativeFieldMessageIdentity identity)
    {
        if (!questionRecovered.Contains(identity) ||
            !choiceDelivered.Contains(identity))
        {
            return false;
        }

        recoveryPending.Remove(identity);
        return true;
    }
}
