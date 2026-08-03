namespace Ff7.Accessibility.Runtime.Abstractions;

public sealed record FieldFrameObservation(
    int FieldId,
    int PlayerModelId,
    float X,
    float Y,
    float Z,
    int TriangleId,
    bool HasControl,
    int EntityId,
    int ScriptId,
    int ScriptByteIndex);
