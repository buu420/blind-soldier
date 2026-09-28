namespace Ff7.Accessibility.Steam2026X64.Runtime;

/// <summary>
/// Bounds how often a recovered native-ingress overflow is logged. The first recovery is
/// logged at once; later ones inside <see cref="Interval"/> are only counted, and the next
/// line says how many were folded into it. A sustained overload therefore writes about one
/// line per interval instead of one per worker iteration.
/// </summary>
internal sealed class NativeIngressOverflowLogThrottle(TimeSpan interval)
{
    private DateTime lastLoggedUtc = DateTime.MinValue;
    private int suppressed;

    internal TimeSpan Interval { get; } = interval > TimeSpan.Zero
        ? interval
        : throw new ArgumentOutOfRangeException(nameof(interval));

    /// <summary>
    /// Records one recovery at <paramref name="nowUtc"/>. Returns the line to log, or null
    /// when this recovery is only counted.
    /// </summary>
    internal string? Record(DateTime nowUtc, string message)
    {
        if (lastLoggedUtc != DateTime.MinValue && nowUtc >= lastLoggedUtc && nowUtc - lastLoggedUtc < Interval)
        {
            suppressed++;
            return null;
        }

        var line = suppressed > 0
            ? $"{message} ({suppressed} more recoveries since the last report.)"
            : message;
        lastLoggedUtc = nowUtc;
        suppressed = 0;
        return line;
    }
}
