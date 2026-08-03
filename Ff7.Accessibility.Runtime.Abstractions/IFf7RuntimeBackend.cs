namespace Ff7.Accessibility.Runtime.Abstractions;

public interface IFf7RuntimeBackend : IDisposable
{
    RuntimeIdentity Identity { get; }

    RuntimeCapabilityReport ValidateCapabilities();

    void Start(IRuntimeEventSink eventSink);

    RuntimeFrameObservation ReadFrame();

    void Stop();
}
