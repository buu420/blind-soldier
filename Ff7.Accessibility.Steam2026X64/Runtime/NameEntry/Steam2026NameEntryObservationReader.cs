using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.NameEntry;

/// <summary>
/// Produces research-only, pointer-free name-entry observations from the Steam
/// 2026 translated x86 guest address space. It creates no hooks, publishes no
/// events, speaks nothing, and enables no runtime capability.
/// </summary>
public sealed class Steam2026NameEntryObservationReader
{
    private readonly NameEntryStateReader stateReader;

    public Steam2026NameEntryObservationReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory)
        : this(ValidatedTranslatedX86AddressSpaceFactory.Create(
            fingerprint,
            moduleBase,
            memory))
    {
    }

    internal Steam2026NameEntryObservationReader(ILegacyAddressSpace addressSpace)
    {
        stateReader = new NameEntryStateReader(
            addressSpace ?? throw new ArgumentNullException(nameof(addressSpace)));
    }

    public bool TryReadSnapshot(out NameEntryStateSnapshot snapshot) =>
        stateReader.TryRead(out snapshot);
}
