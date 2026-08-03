using System.Collections.Immutable;

namespace Ff7.Accessibility.Runtime.Abstractions;

internal static class RuntimeObservationCollections
{
    public static ImmutableArray<T> Copy<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return ImmutableArray.CreateRange(values);
    }
}
