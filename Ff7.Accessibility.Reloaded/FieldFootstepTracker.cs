namespace Ff7.Accessibility.Reloaded;

public sealed class FieldFootstepTracker
{
    public const double DefaultMeasuredRunSpeedUnitsPerSecond = 300;
    private const double RunExitThresholdRatio = 0.8d;

    private readonly TimeSpan walkFootstepInterval;
    private readonly TimeSpan runFootstepInterval;
    private readonly double measuredRunSpeedUnitsPerSecond;
    private FieldPositionSnapshot? lastPosition;
    private DateTime lastObservedAt = DateTime.MinValue;
    private DateTime lastFootstepAt = DateTime.MinValue;
    private double distanceSinceLastFootstep;
    private string lastPaceDiagnostic = "uninitialized";
    private FieldFootstepCadence lastCadence = FieldFootstepCadence.Walk;

    public FieldFootstepTracker(TimeSpan footstepInterval)
        : this(footstepInterval, footstepInterval, DefaultMeasuredRunSpeedUnitsPerSecond)
    {
    }

    public FieldFootstepTracker(TimeSpan walkFootstepInterval, TimeSpan runFootstepInterval)
        : this(walkFootstepInterval, runFootstepInterval, DefaultMeasuredRunSpeedUnitsPerSecond)
    {
    }

    public FieldFootstepTracker(
        TimeSpan walkFootstepInterval,
        TimeSpan runFootstepInterval,
        double measuredRunSpeedUnitsPerSecond)
    {
        this.walkFootstepInterval = NormalizeInterval(walkFootstepInterval);
        this.runFootstepInterval = NormalizeInterval(runFootstepInterval);
        this.measuredRunSpeedUnitsPerSecond = Math.Max(1, measuredRunSpeedUnitsPerSecond);
    }

    public string LastPaceDiagnostic => lastPaceDiagnostic;

    public FieldFootstepCadence LastCadence => lastCadence;

    public void Reset()
    {
        lastPosition = null;
        lastObservedAt = DateTime.MinValue;
        lastFootstepAt = DateTime.MinValue;
        distanceSinceLastFootstep = 0d;
        lastPaceDiagnostic = "reset";
        lastCadence = FieldFootstepCadence.Walk;
    }

    public bool Observe(FieldPositionSnapshot position, DateTime now) => Observe(position, now, isRunning: false);

    public bool Observe(FieldPositionSnapshot position, DateTime now, bool isRunning) =>
        Observe(position, now, isRunning, distanceUnitsPerFootstep: null);

    public bool Observe(
        FieldPositionSnapshot position,
        DateTime now,
        bool isRunning,
        double distanceUnitsPerFootstep) =>
        Observe(position, now, isRunning, (double?)NormalizeDistance(distanceUnitsPerFootstep));

    private bool Observe(
        FieldPositionSnapshot position,
        DateTime now,
        bool isRunning,
        double? distanceUnitsPerFootstep)
    {
        if (position.CurrentModule != 1)
        {
            Reset();
            return false;
        }

        if (lastPosition is null ||
            lastPosition.Value.FieldId != position.FieldId ||
            lastPosition.Value.ModelIndex != position.ModelIndex)
        {
            lastPosition = position;
            lastObservedAt = now;
            lastFootstepAt = now;
            distanceSinceLastFootstep = 0d;
            lastPaceDiagnostic = "primed";
            return false;
        }

        var previous = lastPosition.Value;
        var previousObservedAt = lastObservedAt;
        if (FieldNavigationPositionContinuity.IsDiscontinuous(
                previous,
                previousObservedAt,
                position,
                now,
                out var discontinuityDiagnostic))
        {
            lastPosition = position;
            lastObservedAt = now;
            lastFootstepAt = now;
            distanceSinceLastFootstep = 0d;
            lastPaceDiagnostic = discontinuityDiagnostic;
            lastCadence = FieldFootstepCadence.Walk;
            return false;
        }

        lastPosition = position;
        lastObservedAt = now;
        if (previous.X == position.X &&
            previous.Y == position.Y &&
            previous.Z == position.Z &&
            previous.TriangleId == position.TriangleId)
        {
            lastPaceDiagnostic = "stationary";
            return false;
        }

        var movementDistance = CalculateDistanceUnits(previous, position);
        var measuredSpeed = CalculateSpeedUnitsPerSecond(movementDistance, previousObservedAt, now);
        var runExitThreshold = measuredRunSpeedUnitsPerSecond * RunExitThresholdRatio;
        var measuredRun = measuredSpeed >= measuredRunSpeedUnitsPerSecond;
        var measuredWalk = measuredSpeed <= runExitThreshold;
        if (measuredRun)
        {
            lastCadence = FieldFootstepCadence.Run;
        }
        else if (measuredWalk)
        {
            lastCadence = FieldFootstepCadence.Walk;
        }

        var useRunCadence = lastCadence == FieldFootstepCadence.Run;
        var paceDiagnostic =
            $"inputRun={isRunning}, measuredRun={measuredRun}, measuredWalk={measuredWalk}, " +
            $"speed={measuredSpeed:0.0}, enterThreshold={measuredRunSpeedUnitsPerSecond:0.0}, " +
            $"exitThreshold={runExitThreshold:0.0}, cadence={lastCadence}";
        var footstepInterval = useRunCadence ? runFootstepInterval : walkFootstepInterval;

        if (distanceUnitsPerFootstep is { } stepDistance)
        {
            distanceSinceLastFootstep += movementDistance;
            if (distanceSinceLastFootstep < stepDistance)
            {
                lastPaceDiagnostic =
                    $"{paceDiagnostic}, trigger=distance, stepUnits={stepDistance:0.0}, " +
                    $"progress={distanceSinceLastFootstep:0.0}";
                return false;
            }

            if (lastFootstepAt != DateTime.MinValue && now - lastFootstepAt < footstepInterval)
            {
                var remainingInterval = footstepInterval - (now - lastFootstepAt);
                lastPaceDiagnostic =
                    $"{paceDiagnostic}, trigger=distance+cadence, stepUnits={stepDistance:0.0}, " +
                    $"progress={distanceSinceLastFootstep:0.0}, waitMs={remainingInterval.TotalMilliseconds:0}";
                return false;
            }

            distanceSinceLastFootstep %= stepDistance;
            lastFootstepAt = now;
            lastPaceDiagnostic =
                $"{paceDiagnostic}, trigger=distance+cadence, stepUnits={stepDistance:0.0}, " +
                $"remainder={distanceSinceLastFootstep:0.0}";
            return true;
        }

        distanceSinceLastFootstep = 0d;
        lastPaceDiagnostic = paceDiagnostic;

        if (lastFootstepAt != DateTime.MinValue && now - lastFootstepAt < footstepInterval)
        {
            return false;
        }

        lastFootstepAt = now;
        return true;
    }

    private static TimeSpan NormalizeInterval(TimeSpan interval) =>
        interval < TimeSpan.Zero ? TimeSpan.Zero : interval;

    private static double NormalizeDistance(double distanceUnitsPerFootstep) =>
        double.IsFinite(distanceUnitsPerFootstep) && distanceUnitsPerFootstep > 0d
            ? distanceUnitsPerFootstep
            : 1d;

    private static double CalculateDistanceUnits(
        FieldPositionSnapshot previous,
        FieldPositionSnapshot current)
    {
        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        var dz = current.Z - previous.Z;
        return Math.Sqrt(dx * (double)dx + dy * (double)dy + dz * (double)dz);
    }

    private static double CalculateSpeedUnitsPerSecond(
        double distance,
        DateTime previousObservedAt,
        DateTime now)
    {
        var elapsedSeconds = (now - previousObservedAt).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return 0;
        }

        return distance / elapsedSeconds;
    }
}
