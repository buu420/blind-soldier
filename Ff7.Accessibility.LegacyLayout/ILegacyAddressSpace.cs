namespace Ff7.Accessibility.LegacyLayout;

/// <summary>
/// Reads the original FFVII 32-bit guest virtual address space without exposing
/// a process-specific host pointer. A failed read invalidates the containing
/// observation; callers must never reinterpret failure as a zero game value.
/// </summary>
public interface ILegacyAddressSpace
{
    bool TryRead(uint virtualAddress, Span<byte> destination);
}
