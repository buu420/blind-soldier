namespace Ff7.Accessibility.Core;

public readonly record struct HighwayRoadState(
    double CloudLateralUnits,
    double RoadHalfWidthUnits);

public enum HighwaySteeringDirection
{
    None,
    Left,
    Right,
    Up,
    Down,
    UpLeft,
    UpRight,
    DownLeft,
    DownRight
}

public enum HighwaySteeringCueReason
{
    RoadEdge,
    TruckAvoidance
}

public readonly record struct HighwaySteeringCueRequest(
    HighwaySteeringDirection Direction,
    double CloudLateralUnits,
    double RoadHalfWidthUnits,
    double EdgeRatio,
    bool IsCritical,
    HighwaySteeringCueReason Reason = HighwaySteeringCueReason.RoadEdge,
    double TruckDeltaLateral = 0d,
    double TruckDeltaLongitudinal = 0d);

public readonly record struct HighwaySteeringUpdate(
    HighwaySteeringCueRequest? Cue,
    HighwaySteeringDirection Direction);

/// <summary>
/// Pure timestamp-driven policy that translates Cloud's native lateral road
/// position into informational steering cues. It never produces game input.
/// </summary>
public sealed class HighwaySteeringTracker
{
    public const double CorrectionEntryRatio = 0.25d;
    public const double CorrectionReleaseRatio = 0.15d;
    public const double CriticalCorrectionRatio = 0.70d;

    // FUN_00653076 initializes Cloud's two collision radii to 36 and the
    // truck's to 75. The truck's native collision nodes span -30..100 units,
    // so 360 longitudinal units provides a reaction margin before contact.
    public const double TruckAvoidanceEntryLateralUnits = 150d;
    public const double TruckAvoidanceReleaseLateralUnits = 210d;
    public const double TruckAvoidanceEntryLongitudinalUnits = 360d;
    public const double TruckAvoidanceReleaseLongitudinalUnits = 420d;
    public const double TruckDiagonalThresholdUnits = 32d;

    private readonly TimeSpan normalCueInterval;
    private readonly TimeSpan criticalCueInterval;

    private HighwaySteeringDirection correctionDirection;
    private HighwaySteeringCueReason correctionReason;
    private DateTime nextCueUtc = DateTime.MinValue;
    private bool wasCritical;
    private bool truckAvoidanceActive;

    public HighwaySteeringTracker(
        TimeSpan normalCueInterval,
        TimeSpan criticalCueInterval)
    {
        this.normalCueInterval = NonNegative(normalCueInterval);
        this.criticalCueInterval = NonNegative(criticalCueInterval);
    }

    public HighwaySteeringUpdate Update(HighwayRoadState state, DateTime nowUtc)
        => Update(state, truckDelta: null, nowUtc);

    public HighwaySteeringUpdate Update(
        HighwayRoadState state,
        HighwayPoint? truckDelta,
        DateTime nowUtc)
    {
        if (!double.IsFinite(state.CloudLateralUnits) ||
            !double.IsFinite(state.RoadHalfWidthUnits) ||
            state.RoadHalfWidthUnits <= 0d)
        {
            Reset();
            return default;
        }

        var edgeRatio = Math.Abs(state.CloudLateralUnits) / state.RoadHalfWidthUnits;
        if (!double.IsFinite(edgeRatio))
        {
            Reset();
            return default;
        }

        if (TryUpdateTruckAvoidance(state, edgeRatio, truckDelta, nowUtc, out var truckUpdate))
        {
            return truckUpdate;
        }

        var requestedDirection = state.CloudLateralUnits switch
        {
            > 0d => HighwaySteeringDirection.Left,
            < 0d => HighwaySteeringDirection.Right,
            _ => HighwaySteeringDirection.None
        };
        var publishImmediately = false;
        if (correctionDirection == HighwaySteeringDirection.None)
        {
            if (edgeRatio < CorrectionEntryRatio ||
                requestedDirection == HighwaySteeringDirection.None)
            {
                return default;
            }

            correctionDirection = requestedDirection;
            correctionReason = HighwaySteeringCueReason.RoadEdge;
            publishImmediately = true;
        }
        else
        {
            if (edgeRatio <= CorrectionReleaseRatio ||
                requestedDirection == HighwaySteeringDirection.None)
            {
                Reset();
                return default;
            }

            if (requestedDirection != correctionDirection)
            {
                correctionDirection = requestedDirection;
                correctionReason = HighwaySteeringCueReason.RoadEdge;
                publishImmediately = true;
            }
        }

        var isCritical = edgeRatio >= CriticalCorrectionRatio;
        if (isCritical && !wasCritical)
        {
            publishImmediately = true;
        }

        if (!publishImmediately && nowUtc < nextCueUtc)
        {
            wasCritical = isCritical;
            return new HighwaySteeringUpdate(null, correctionDirection);
        }

        nextCueUtc = nowUtc + (isCritical ? criticalCueInterval : normalCueInterval);
        wasCritical = isCritical;
        return new HighwaySteeringUpdate(
            new HighwaySteeringCueRequest(
                correctionDirection,
                state.CloudLateralUnits,
                state.RoadHalfWidthUnits,
                edgeRatio,
                isCritical,
                HighwaySteeringCueReason.RoadEdge),
            correctionDirection);
    }

    public void Reset()
    {
        correctionDirection = HighwaySteeringDirection.None;
        correctionReason = HighwaySteeringCueReason.RoadEdge;
        nextCueUtc = DateTime.MinValue;
        wasCritical = false;
        truckAvoidanceActive = false;
    }

    private bool TryUpdateTruckAvoidance(
        HighwayRoadState road,
        double edgeRatio,
        HighwayPoint? truckDelta,
        DateTime nowUtc,
        out HighwaySteeringUpdate update)
    {
        update = default;
        if (truckDelta is not { } delta ||
            !double.IsFinite(delta.Lateral) ||
            !double.IsFinite(delta.Longitudinal))
        {
            ReleaseTruckAvoidance();
            return false;
        }

        var lateralDistance = Math.Abs(delta.Lateral);
        var longitudinalDistance = Math.Abs(delta.Longitudinal);
        if (!truckAvoidanceActive)
        {
            if (lateralDistance > TruckAvoidanceEntryLateralUnits ||
                longitudinalDistance > TruckAvoidanceEntryLongitudinalUnits)
            {
                return false;
            }

            truckAvoidanceActive = true;
        }
        else if (lateralDistance >= TruckAvoidanceReleaseLateralUnits ||
                 longitudinalDistance >= TruckAvoidanceReleaseLongitudinalUnits)
        {
            ReleaseTruckAvoidance();
            return false;
        }

        var requestedDirection = ResolveTruckEscapeDirection(road, edgeRatio, delta);
        var publishImmediately =
            correctionReason != HighwaySteeringCueReason.TruckAvoidance ||
            correctionDirection != requestedDirection;
        correctionDirection = requestedDirection;
        correctionReason = HighwaySteeringCueReason.TruckAvoidance;

        if (!publishImmediately && nowUtc < nextCueUtc)
        {
            wasCritical = true;
            update = new HighwaySteeringUpdate(null, requestedDirection);
            return true;
        }

        nextCueUtc = nowUtc + criticalCueInterval;
        wasCritical = true;
        update = new HighwaySteeringUpdate(
            new HighwaySteeringCueRequest(
                requestedDirection,
                road.CloudLateralUnits,
                road.RoadHalfWidthUnits,
                edgeRatio,
                IsCritical: true,
                HighwaySteeringCueReason.TruckAvoidance,
                delta.Lateral,
                delta.Longitudinal),
            requestedDirection);
        return true;
    }

    private static HighwaySteeringDirection ResolveTruckEscapeDirection(
        HighwayRoadState road,
        double edgeRatio,
        HighwayPoint delta)
    {
        var vertical = delta.Longitudinal < 0d
            ? HighwaySteeringDirection.Up
            : HighwaySteeringDirection.Down;
        var horizontal = Math.Abs(delta.Lateral) < TruckDiagonalThresholdUnits
            ? HighwaySteeringDirection.None
            : delta.Lateral > 0d
                ? HighwaySteeringDirection.Left
                : HighwaySteeringDirection.Right;

        var wouldMoveFartherOutside =
            edgeRatio >= CriticalCorrectionRatio &&
            ((road.CloudLateralUnits > 0d && horizontal == HighwaySteeringDirection.Right) ||
             (road.CloudLateralUnits < 0d && horizontal == HighwaySteeringDirection.Left));
        if (wouldMoveFartherOutside || horizontal == HighwaySteeringDirection.None)
        {
            return vertical;
        }

        return (vertical, horizontal) switch
        {
            (HighwaySteeringDirection.Up, HighwaySteeringDirection.Left) =>
                HighwaySteeringDirection.UpLeft,
            (HighwaySteeringDirection.Up, HighwaySteeringDirection.Right) =>
                HighwaySteeringDirection.UpRight,
            (HighwaySteeringDirection.Down, HighwaySteeringDirection.Left) =>
                HighwaySteeringDirection.DownLeft,
            _ => HighwaySteeringDirection.DownRight
        };
    }

    private void ReleaseTruckAvoidance()
    {
        if (!truckAvoidanceActive &&
            correctionReason != HighwaySteeringCueReason.TruckAvoidance)
        {
            return;
        }

        truckAvoidanceActive = false;
        correctionDirection = HighwaySteeringDirection.None;
        correctionReason = HighwaySteeringCueReason.RoadEdge;
        nextCueUtc = DateTime.MinValue;
        wasCritical = false;
    }

    private static TimeSpan NonNegative(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
