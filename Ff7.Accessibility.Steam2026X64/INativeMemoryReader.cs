namespace Ff7.Accessibility.Steam2026X64;

public readonly record struct NativeMemoryRegion(
    ulong BaseAddress,
    ulong Size,
    ulong AllocationBase,
    bool IsCommitted,
    bool IsExecutable,
    bool IsImage,
    bool IsReadable,
    bool IsWritable = false,
    bool IsCopyOnWrite = false);

public interface INativeMemoryReader
{
    bool TryReadUInt64(ulong address, out ulong value);

    bool TryRead(ulong address, Span<byte> destination);

    bool TryQueryRegion(ulong address, out NativeMemoryRegion region);
}

/// <summary>
/// Writes into the current process's own address space.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="INativeMemoryReader"/> so that reading stays the
/// default capability and a component has to be handed a writer deliberately.
/// Everything the mod does is a read except this.
/// </remarks>
public interface INativeMemoryWriter
{
    /// <summary>
    /// Atomically replaces the four bytes at <paramref name="hostAddress"/>.
    /// </summary>
    /// <remarks>
    /// Atomic rather than a plain copy because the value being replaced is read by
    /// running game code. WriteProcessMemory promises only to validate and copy the
    /// range, so a torn half-write is permitted by its contract; an aligned
    /// interlocked exchange is documented to be indivisible. The caller is
    /// responsible for having established that the address is committed, writable,
    /// four-byte aligned, and wholly inside one region.
    /// </remarks>
    bool TryExchangeInt32(ulong hostAddress, int value);
}
