using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Keeps the x64 runtime-facing type stable while both game architectures use
/// the same native field-window ownership reader from the shared layout layer.
/// </summary>
internal sealed class Steam2026FieldAudibleCueStateReader
{
    internal const uint AddressFieldWindowLifecyclePhases =
        FieldAudibleCueOwnershipStateReader.AddressFieldWindowLifecyclePhases;
    internal const uint FieldWindowLifecycleStride =
        FieldAudibleCueOwnershipStateReader.FieldWindowLifecycleStride;
    internal const ushort CompletedTextPhase =
        FieldAudibleCueOwnershipStateReader.CompletedTextPhase;

    private readonly FieldAudibleCueOwnershipStateReader reader;

    public Steam2026FieldAudibleCueStateReader(ILegacyAddressSpace addressSpace)
    {
        reader = new FieldAudibleCueOwnershipStateReader(addressSpace);
    }

    internal string LastDiagnostic => reader.LastDiagnostic;

    public bool TryRead(out FieldAudibleCueState state) => reader.TryRead(out state);
}
