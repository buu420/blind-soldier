namespace Ff7.Accessibility.Runtime.Abstractions;

public interface IRuntimeEventSink
{
    RuntimeEventPublishResult Publish(RuntimeEvent runtimeEvent);
}
