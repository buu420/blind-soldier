namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldNavigationVelocityEstimate(
    double XPerSecond,
    double YPerSecond,
    double Speed,
    double Coherence);

public readonly record struct FieldNavigationPredictiveTurn(
    FieldNavigationRouteWaypoint Waypoint,
    double ProjectedDistance,
    string Diagnostic);

public static class FieldNavigationPredictiveTurnResolver
{
    private const int MaximumPredictionHorizonMs = 1000;
    private const double MaximumProjectedDistance = 240d;
    private const double SpeechLeadDistance = 32d;
    private const double MinimumVelocityAlignment = 0.70d;
    private const double MinimumVelocityCoherence = 0.80d;
    private const double MaterialTurnAlignment = 0.7071067811865476d;

    public static bool TryResolve(
        FieldPositionSnapshot position,
        FieldNavigationRouteWaypoint currentWaypoint,
        FieldNavigationRouteWaypoint nextWaypoint,
        FieldNavigationVelocityEstimate velocity,
        int predictionHorizonMs,
        out FieldNavigationPredictiveTurn turn)
    {
        turn = default;
        if (predictionHorizonMs <= 0 ||
            velocity.Speed <= 0d ||
            velocity.Coherence < MinimumVelocityCoherence)
        {
            return false;
        }

        var currentX = currentWaypoint.X - position.X;
        var currentY = currentWaypoint.Y - position.Y;
        var currentDistance = Length(currentX, currentY);
        var nextX = nextWaypoint.X - currentWaypoint.X;
        var nextY = nextWaypoint.Y - currentWaypoint.Y;
        var nextDistance = Length(nextX, nextY);
        var velocityLength = Length(velocity.XPerSecond, velocity.YPerSecond);
        if (currentDistance <= 1d || nextDistance <= 1d || velocityLength <= 0d)
        {
            return false;
        }

        var velocityAlignment =
            (velocity.XPerSecond * currentX + velocity.YPerSecond * currentY) /
            (velocityLength * currentDistance);
        if (velocityAlignment < MinimumVelocityAlignment)
        {
            return false;
        }

        var horizonMs = Math.Min(MaximumPredictionHorizonMs, predictionHorizonMs);
        var projectedDistance = Math.Min(
            MaximumProjectedDistance,
            velocity.Speed * horizonMs / 1000d);
        if (currentDistance > projectedDistance + SpeechLeadDistance)
        {
            return false;
        }

        var turnAlignment =
            (currentX * (double)nextX + currentY * (double)nextY) /
            (currentDistance * nextDistance);
        if (turnAlignment >= MaterialTurnAlignment)
        {
            return false;
        }

        turn = new FieldNavigationPredictiveTurn(
            nextWaypoint,
            projectedDistance,
            $"speed={velocity.Speed:0}, coherence={velocity.Coherence:0.00}, " +
            $"velocityAlignment={velocityAlignment:0.00}, turnAlignment={turnAlignment:0.00}, " +
            $"cornerDistance={currentDistance:0}, projected={projectedDistance:0}");
        return true;
    }

    private static double Length(double x, double y) =>
        Math.Sqrt(x * x + y * y);
}

public sealed class FieldNavigationVelocityEstimator
{
    private const int MaximumSamples = 7;
    private const int MinimumSpanMs = 100;
    private const int SampleWindowMs = 350;
    private const int MaximumGapMs = 500;
    private const double MinimumReliableSpeed = 40d;
    private const double MaximumReliableSpeed = 1600d;
    private const double MinimumCoherence = 0.80d;

    private readonly List<PositionSample> samples = new(MaximumSamples);
    private int fieldId = -1;
    private int modelIndex = -1;

    public void Observe(
        FieldPositionSnapshot position,
        DateTime observedAt,
        bool isSuppressed)
    {
        if (isSuppressed ||
            observedAt == default ||
            !FieldPositionReader.IsUsable(position))
        {
            Reset();
            return;
        }

        if (fieldId != position.FieldId || modelIndex != position.ModelIndex)
        {
            Reset();
            fieldId = position.FieldId;
            modelIndex = position.ModelIndex;
        }

        if (samples.Count != 0)
        {
            var previous = samples[^1];
            var elapsed = observedAt - previous.ObservedAt;
            if (elapsed <= TimeSpan.Zero ||
                elapsed > TimeSpan.FromMilliseconds(MaximumGapMs))
            {
                Restart(position, observedAt);
                return;
            }

            var distance = PlanarDistance(previous.X, previous.Y, position.X, position.Y);
            var instantaneousSpeed = distance / elapsed.TotalSeconds;
            if (instantaneousSpeed > MaximumReliableSpeed)
            {
                Restart(position, observedAt);
                return;
            }
        }

        samples.Add(new PositionSample(position.X, position.Y, observedAt));
        var windowStart = observedAt - TimeSpan.FromMilliseconds(SampleWindowMs);
        while (samples.Count > 1 &&
               (samples[0].ObservedAt < windowStart || samples.Count > MaximumSamples))
        {
            samples.RemoveAt(0);
        }
    }

    public bool TryGetEstimate(out FieldNavigationVelocityEstimate estimate)
    {
        estimate = default;
        if (samples.Count < 3)
        {
            return false;
        }

        var first = samples[0];
        var last = samples[^1];
        var elapsed = last.ObservedAt - first.ObservedAt;
        if (elapsed < TimeSpan.FromMilliseconds(MinimumSpanMs) ||
            elapsed > TimeSpan.FromMilliseconds(SampleWindowMs))
        {
            return false;
        }

        var pathDistance = 0d;
        for (var index = 1; index < samples.Count; index++)
        {
            pathDistance += PlanarDistance(
                samples[index - 1].X,
                samples[index - 1].Y,
                samples[index].X,
                samples[index].Y);
        }

        if (pathDistance <= 0d)
        {
            return false;
        }

        var deltaX = last.X - first.X;
        var deltaY = last.Y - first.Y;
        var netDistance = Math.Sqrt(deltaX * (double)deltaX + deltaY * (double)deltaY);
        var coherence = netDistance / pathDistance;
        var xPerSecond = deltaX / elapsed.TotalSeconds;
        var yPerSecond = deltaY / elapsed.TotalSeconds;
        var speed = netDistance / elapsed.TotalSeconds;
        if (coherence < MinimumCoherence ||
            speed < MinimumReliableSpeed ||
            speed > MaximumReliableSpeed)
        {
            return false;
        }

        estimate = new FieldNavigationVelocityEstimate(
            xPerSecond,
            yPerSecond,
            speed,
            coherence);
        return true;
    }

    public void Reset()
    {
        samples.Clear();
        fieldId = -1;
        modelIndex = -1;
    }

    private void Restart(FieldPositionSnapshot position, DateTime observedAt)
    {
        samples.Clear();
        fieldId = position.FieldId;
        modelIndex = position.ModelIndex;
        samples.Add(new PositionSample(position.X, position.Y, observedAt));
    }

    private static double PlanarDistance(int firstX, int firstY, int secondX, int secondY)
    {
        var dx = secondX - firstX;
        var dy = secondY - firstY;
        return Math.Sqrt(dx * (double)dx + dy * (double)dy);
    }

    private readonly record struct PositionSample(int X, int Y, DateTime ObservedAt);
}
