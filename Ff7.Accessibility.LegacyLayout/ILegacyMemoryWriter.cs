namespace Ff7.Accessibility.LegacyLayout;

/// <summary>
/// Writes a value back into the game's own x86 address space.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="ILegacyAddressSpace"/>, which stays
/// read-only. The mod observes; it does not drive the game. The one place that
/// changed is the Fort Condor cursor, where a sighted player can see the whole
/// field and put the cursor where they want in one movement, and sweeping for the
/// same spot by ear is not the same task. Anything that needs a write has to be
/// handed this capability on purpose rather than finding it on the reader it
/// already holds.
///
/// <para>Both runtimes implement it: the legacy x86 process writes its own memory
/// directly, and the Steam 2026 runtime resolves the guest address through the
/// translator's page table and writes the host page behind it. Implementations are
/// expected to refuse rather than approximate - an unaligned address, an uncommitted
/// or read-only page, a copy-on-write page, or a span crossing a page boundary must
/// all come back false so the caller can say out loud that it could not move.</para>
/// </remarks>
public interface ILegacyMemoryWriter
{
    /// <summary>
    /// Atomically replaces the 32-bit value at a guest virtual address.
    /// </summary>
    /// <returns>Whether the write was performed and verified.</returns>
    bool TryWriteInt32(uint virtualAddress, int value);
}
