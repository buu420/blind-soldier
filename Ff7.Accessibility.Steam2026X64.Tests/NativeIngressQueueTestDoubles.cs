using Ff7.Accessibility.Steam2026X64.Runtime;

internal sealed class DelegatingNativeIngressQueue<T> : INativeIngressQueue<T>
{
    private readonly Func<T, bool> tryEnqueue;

    internal DelegatingNativeIngressQueue(Action<T> enqueue)
        : this(item =>
        {
            enqueue(item);
            return true;
        })
    {
    }

    internal DelegatingNativeIngressQueue(Func<T, bool> tryEnqueue)
    {
        this.tryEnqueue = tryEnqueue ?? throw new ArgumentNullException(nameof(tryEnqueue));
    }

    public bool TryEnqueue(T item) => tryEnqueue(item);
}
