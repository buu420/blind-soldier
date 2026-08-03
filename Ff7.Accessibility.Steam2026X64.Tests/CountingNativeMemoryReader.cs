using Ff7.Accessibility.Steam2026X64;

internal sealed class CountingNativeMemoryReader(INativeMemoryReader inner) : INativeMemoryReader
{
    private int readCalls;
    private int readUInt64Calls;
    private int queryCalls;

    internal int ReadOperations => Volatile.Read(ref readCalls) + Volatile.Read(ref readUInt64Calls);

    internal int QueryOperations => Volatile.Read(ref queryCalls);

    internal void Reset()
    {
        Volatile.Write(ref readCalls, 0);
        Volatile.Write(ref readUInt64Calls, 0);
        Volatile.Write(ref queryCalls, 0);
    }

    public bool TryReadUInt64(ulong address, out ulong value)
    {
        Interlocked.Increment(ref readUInt64Calls);
        return inner.TryReadUInt64(address, out value);
    }

    public bool TryRead(ulong address, Span<byte> destination)
    {
        Interlocked.Increment(ref readCalls);
        return inner.TryRead(address, destination);
    }

    public bool TryQueryRegion(ulong address, out NativeMemoryRegion region)
    {
        Interlocked.Increment(ref queryCalls);
        return inner.TryQueryRegion(address, out region);
    }
}
