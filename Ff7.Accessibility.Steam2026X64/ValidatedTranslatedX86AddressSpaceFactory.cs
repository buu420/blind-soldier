namespace Ff7.Accessibility.Steam2026X64;

internal static class ValidatedTranslatedX86AddressSpaceFactory
{
    /// <param name="writer">
    /// Optional. Supplied only where a write is actually intended - today, the Fort
    /// Condor cursor jump. Omitting it yields a read-only address space, which is
    /// what every other caller wants.
    /// </param>
    public static TranslatedX86AddressSpace Create(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory,
        INativeMemoryWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (!fingerprint.IsSupported ||
            !fingerprint.Identity.Is64Bit ||
            !string.Equals(
                fingerprint.Identity.RuntimeId,
                Steam2026Fingerprint.SupportedRuntimeId,
                StringComparison.Ordinal) ||
            !string.Equals(
                fingerprint.Identity.Sha256,
                Steam2026Fingerprint.SupportedSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The translated x86 address space requires the exact supported Steam 2026 x64 executable fingerprint.",
                nameof(fingerprint));
        }

        return Create(moduleBase, memory, writer);
    }

    public static TranslatedX86AddressSpace Create(
        ulong moduleBase,
        INativeMemoryReader memory,
        INativeMemoryWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory, writer);
        if (!addressSpace.HasExpectedResolverSignature())
        {
            throw new InvalidOperationException(
                "The Steam 2026 translated x86 resolver signature is unavailable, unstable, or unsupported.");
        }

        return addressSpace;
    }
}
