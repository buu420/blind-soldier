using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Core;

public sealed record RuntimeDispatchBatch(
    RuntimeFrameObservation? Frame,
    IReadOnlyList<RuntimeEvent> Events,
    RuntimeQueueDegradation? Degradation);

public sealed class RuntimeEventQueue : IRuntimeEventSink
{
    private readonly object gate = new();
    private readonly Queue<RuntimeEvent> events = new();
    private readonly int maxDiscreteEvents;
    private RuntimeFrameObservation? frame;
    private long rejectedEventCount;
    private string lastRejectedEventType = string.Empty;
    private bool accepting = true;

    public RuntimeEventQueue(int maxDiscreteEvents = 4096)
    {
        if (maxDiscreteEvents < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDiscreteEvents),
                maxDiscreteEvents,
                "The discrete event capacity must be positive.");
        }

        this.maxDiscreteEvents = maxDiscreteEvents;
    }

    public RuntimeEventPublishResult Publish(RuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);

        lock (gate)
        {
            if (!accepting)
            {
                return new RuntimeEventPublishResult(RuntimeEventPublishStatus.RejectedNotAccepting);
            }

            if (events.Count >= maxDiscreteEvents)
            {
                rejectedEventCount++;
                lastRejectedEventType = runtimeEvent.GetType().FullName ?? runtimeEvent.GetType().Name;
                return new RuntimeEventPublishResult(RuntimeEventPublishStatus.RejectedQueueFull);
            }

            events.Enqueue(runtimeEvent);
            return new RuntimeEventPublishResult(RuntimeEventPublishStatus.Published);
        }
    }

    public void PublishFrame(RuntimeFrameObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        lock (gate)
        {
            if (!accepting)
            {
                return;
            }

            frame = frame is null ? observation : MergeFrames(frame, observation);
        }
    }

    public void DeactivateAndClear()
    {
        lock (gate)
        {
            accepting = false;
            frame = null;
            events.Clear();
            rejectedEventCount = 0;
            lastRejectedEventType = string.Empty;
        }
    }

    public RuntimeDispatchBatch Drain()
    {
        lock (gate)
        {
            var degradation = rejectedEventCount == 0
                ? null
                : new RuntimeQueueDegradation(
                    RuntimeQueueDegradationKind.DiscreteEventOverflow,
                    true,
                    maxDiscreteEvents,
                    rejectedEventCount,
                    lastRejectedEventType);
            var batch = new RuntimeDispatchBatch(frame, events.ToArray(), degradation);
            frame = null;
            events.Clear();
            return batch;
        }
    }

    private static RuntimeFrameObservation MergeFrames(
        RuntimeFrameObservation previous,
        RuntimeFrameObservation current)
    {
        var moduleChanged = previous.Lifecycle.ModuleId != current.Lifecycle.ModuleId;
        return new RuntimeFrameObservation(
            current.TimestampUtc,
            current.Lifecycle,
            MergeDomain(previous.Menu, current.Menu, moduleChanged),
            MergeDomain(previous.Dialogue, current.Dialogue, moduleChanged),
            MergeDomain(previous.Field, current.Field, moduleChanged),
            MergeDomain(previous.Battle, current.Battle, moduleChanged),
            MergeDomain(previous.Navigation, current.Navigation, moduleChanged));
    }

    private static RuntimeDomainUpdate<T> MergeDomain<T>(
        RuntimeDomainUpdate<T> previous,
        RuntimeDomainUpdate<T> current,
        bool moduleChanged)
        where T : class
    {
        if (current.Kind != RuntimeDomainUpdateKind.Unchanged)
        {
            return current;
        }

        return moduleChanged ? RuntimeDomainUpdate<T>.Closed : previous;
    }
}
