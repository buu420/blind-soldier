using System.Collections.Immutable;

namespace Ff7.Accessibility.Runtime.Abstractions;

public sealed record RuntimeCapabilityFailure(
    RuntimeCapability Capability,
    string Signal,
    string Diagnostic);

public sealed record RuntimeCapabilityReport
{
    public RuntimeCapabilityReport(
        RuntimeIdentity identity,
        RuntimeCapability available,
        IEnumerable<RuntimeCapabilityFailure> failures)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Available = available;
        Failures = RuntimeObservationCollections.Copy(failures, nameof(failures));
    }

    public RuntimeIdentity Identity { get; }

    public RuntimeCapability Available { get; }

    public ImmutableArray<RuntimeCapabilityFailure> Failures { get; }

    public bool HasFullParity =>
        (Available & RuntimeCapability.FullParity) == RuntimeCapability.FullParity
        && Failures.IsEmpty;
}
