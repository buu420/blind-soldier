namespace Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

/// <summary>
/// Assigns one native entry order across translated ASK and MESSAGE callbacks.
/// The callbacks publish through separate queues and can nest, so queue order
/// and post-call timestamps cannot establish current dialogue ownership.
/// </summary>
internal sealed class Steam2026DialogueIngressSequencer
{
    private long nextSequence;

    internal bool TryReserve(out long sequence)
    {
        sequence = Interlocked.Increment(ref nextSequence);
        return sequence > 0;
    }
}
