namespace Ff7.Accessibility.Runtime.Abstractions;

public sealed record GameLifecycleObservation(
    bool IsForeground,
    bool IsShuttingDown,
    int ModuleId,
    int Revision);
