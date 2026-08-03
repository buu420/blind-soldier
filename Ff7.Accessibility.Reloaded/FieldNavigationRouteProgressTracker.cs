namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Converts the ordered, absolute XYZ distance that remains on a field route
/// into an installer-style percentage. Progress follows movement in either
/// direction, while one percent is reserved until the controller confirms the
/// route's arrival condition rather than merely observing a nearby coordinate.
/// </summary>
public sealed class FieldNavigationRouteProgressTracker
{
    private const double DistanceEpsilon = 0.001d;

    private double segmentVirtualTotalDistance;

    public bool Active { get; private set; }

    public int Percent { get; private set; }

    public int Start(double remainingDistance)
    {
        Active = true;
        Percent = 0;
        segmentVirtualTotalDistance = SanitizeDistance(remainingDistance);
        return Percent;
    }

    public int BeginRemainingSegment(double remainingDistance)
    {
        if (!Active)
        {
            return Start(remainingDistance);
        }

        var remaining = SanitizeDistance(remainingDistance);
        var remainingFraction = Math.Max(
            0.01d,
            1d - Math.Clamp(Percent, 0, 99) / 100d);
        // Make this position evaluate to the existing percentage so a replan
        // cannot jump the bar. Remaining distance may then move the value up
        // or down on the replacement route.
        segmentVirtualTotalDistance = remaining <= DistanceEpsilon
            ? 0d
            : remaining / remainingFraction;
        return Percent;
    }

    public int Observe(double remainingDistance)
    {
        if (!Active)
        {
            return Start(remainingDistance);
        }

        var remaining = SanitizeDistance(remainingDistance);
        if (segmentVirtualTotalDistance <= DistanceEpsilon)
        {
            Percent = remaining <= DistanceEpsilon ? 99 : 0;
            return Percent;
        }

        var completedFraction =
            1d - remaining / segmentVirtualTotalDistance;
        var candidate = (int)Math.Floor(
            100d * completedFraction +
            DistanceEpsilon);
        Percent = Math.Clamp(candidate, 0, 99);
        return Percent;
    }

    public int Complete()
    {
        Active = true;
        Percent = 100;
        segmentVirtualTotalDistance = 0d;
        return Percent;
    }

    public void Reset()
    {
        Active = false;
        Percent = 0;
        segmentVirtualTotalDistance = 0d;
    }

    private static double SanitizeDistance(double distance) =>
        double.IsFinite(distance)
            ? Math.Max(0d, distance)
            : 0d;
}
