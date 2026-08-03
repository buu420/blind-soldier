namespace Ff7.Accessibility.Runtime.Abstractions;

public enum RuntimeEventPublishStatus
{
    Published,
    RejectedQueueFull,
    RejectedNotAccepting
}

public readonly record struct RuntimeEventPublishResult(RuntimeEventPublishStatus Status)
{
    public bool Succeeded => Status == RuntimeEventPublishStatus.Published;
}

public enum RuntimeQueueDegradationKind
{
    DiscreteEventOverflow
}

public sealed record RuntimeQueueDegradation(
    RuntimeQueueDegradationKind Kind,
    bool IsFatal,
    int MaximumDiscreteEvents,
    long RejectedEventCount,
    string LastRejectedEventType);
