namespace Ff7.Accessibility.Steam2026X64;

public readonly record struct NativeMemoryRegion(
    ulong BaseAddress,
    ulong Size,
    ulong AllocationBase,
    bool IsCommitted,
    bool IsExecutable,
    bool IsImage,
    bool IsReadable);

public interface INativeMemoryReader
{
    bool TryReadUInt64(ulong address, out ulong value);

    bool TryRead(ulong address, Span<byte> destination);

    bool TryQueryRegion(ulong address, out NativeMemoryRegion region);
}
