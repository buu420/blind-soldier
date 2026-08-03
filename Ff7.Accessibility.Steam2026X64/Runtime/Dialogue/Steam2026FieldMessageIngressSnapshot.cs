using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

/// <summary>
/// Pointer-free MESSAGE lifecycle copied by the exact translated opcode
/// callback. Visible window buffers remain the only ordinary speech source.
/// </summary>
internal readonly record struct Steam2026FieldMessageIngressSnapshot(
    long Sequence,
    DateTime TimestampUtc,
    FieldOpcodeMessageObservation Observation,
    int Result);
