namespace Ff7.Accessibility.Reloaded;

public static class FieldNavigationPositionContinuity
{
    public const double MaximumPhysicalSpeedUnitsPerSecond = 4000d;
    public static readonly TimeSpan MaximumObservationGap = TimeSpan.FromMilliseconds(500);

    public static bool IsDiscontinuous(
        FieldPositionSnapshot previous,
        DateTime previousObservedAt,
        FieldPositionSnapshot current,
        DateTime observedAt,
        out string diagnostic)
    {
        diagnostic = string.Empty;
        if (previous.FieldId != current.FieldId ||
            previous.ModelIndex != current.ModelIndex ||
            previousObservedAt == default ||
            observedAt == default)
        {
            return false;
        }

        var elapsed = observedAt - previousObservedAt;
        if (elapsed <= TimeSpan.Zero || elapsed > MaximumObservationGap)
        {
            return false;
        }

        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        var dz = current.Z - previous.Z;
        var distance = Math.Sqrt(
            dx * (double)dx +
            dy * (double)dy +
            dz * (double)dz);
        var speed = distance / elapsed.TotalSeconds;
        if (!double.IsFinite(speed) || speed <= MaximumPhysicalSpeedUnitsPerSecond)
        {
            return false;
        }

        diagnostic =
            $"position discontinuity, field={current.FieldId}, model={current.ModelIndex}, " +
            $"from={previous.X},{previous.Y},{previous.Z}, " +
            $"to={current.X},{current.Y},{current.Z}, " +
            $"elapsedMs={elapsed.TotalMilliseconds:0}, speed={speed:0.0}";
        return true;
    }
}

public sealed class FieldNavigationPositionContinuityTracker
{
    private FieldPositionSnapshot? previous;
    private DateTime previousObservedAt;

    public void Reset()
    {
        previous = null;
        previousObservedAt = default;
    }

    public bool Observe(
        FieldPositionSnapshot position,
        DateTime observedAt,
        out string diagnostic)
    {
        if (previous is not { } prior ||
            prior.FieldId != position.FieldId ||
            prior.ModelIndex != position.ModelIndex ||
            previousObservedAt == default ||
            observedAt == default ||
            observedAt - previousObservedAt <= TimeSpan.Zero ||
            observedAt - previousObservedAt > FieldNavigationPositionContinuity.MaximumObservationGap)
        {
            previous = position;
            previousObservedAt = observedAt;
            diagnostic = "position continuity primed";
            return false;
        }

        var isDiscontinuous = FieldNavigationPositionContinuity.IsDiscontinuous(
            prior,
            previousObservedAt,
            position,
            observedAt,
            out diagnostic);
        previous = position;
        previousObservedAt = observedAt;
        if (!isDiscontinuous)
        {
            diagnostic = "position continuity coherent";
        }

        return isDiscontinuous;
    }
}
