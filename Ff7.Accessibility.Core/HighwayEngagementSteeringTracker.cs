namespace Ff7.Accessibility.Core;

public enum HighwayEngagementDecision
{
    Idle,
    Drive,
    Hold
}

/// <summary>
/// Uses checked highway actor coordinates to place Cloud inside the native
/// sword corridor. It only steers the bike; attacks remain player controlled.
/// </summary>
public sealed class HighwayEngagementSteeringTracker
{
    // FFVII's native hit test requires both a distance below 160 units and a
    // matching left/right angular sector. Keep the biker slightly ahead and
    // beside Cloud instead of steering toward its center. The full entry
    // pocket (lateral 72..128, longitudinal 14..86) remains inside the native
    // radius and comfortably inside both sword sectors.
    public const double AttackPocketLateralUnits = 100d;
    public const double AttackPocketLongitudinalUnits = 50d;
    public const double LateralEntryUnits = 28d;
    public const double LateralReleaseUnits = 16d;
    public const double LongitudinalEntryUnits = 36d;
    public const double LongitudinalReleaseUnits = 24d;

    // The arcade return to a truck left far behind starts beyond the
    // comfortable distance and ends once the truck is within half of it.
    public const double TruckFarBehindReleaseFraction = 0.5d;

    private readonly double comfortableTruckDistance;
    private readonly double truckThreatDistance;

    private int targetSlot = -1;
    private HighwayAttackSide targetAttackSide;
    private bool lateralCorrectionActive;
    private bool longitudinalCorrectionActive;
    private bool truckFarBehindActive;

    public HighwayEngagementSteeringTracker(
        double comfortableTruckDistance,
        double truckThreatDistance)
    {
        this.comfortableTruckDistance = Math.Max(0d, comfortableTruckDistance);
        this.truckThreatDistance = Math.Max(0d, truckThreatDistance);
    }

    /// <summary>
    /// What the last update decided: nothing to engage (Idle), a direction toward a biker or the
    /// truck (Drive), or holding still for the sword - aligned in the pocket or mid-swing (Hold).
    /// The composer uses it to tell a deliberate stop from having nothing to do.
    /// </summary>
    public HighwayEngagementDecision LastDecision { get; private set; }

    public HighwaySteeringDirection Update(HighwayAccessibilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsFinite(state.Cloud) || !IsFinite(state.Truck))
        {
            Reset();
            LastDecision = HighwayEngagementDecision.Idle;
            return HighwaySteeringDirection.None;
        }

        // FFVII sets Cloud's native attack timer to +29 or -29 for the two
        // sword directions, then performs the actual hit test near the middle
        // of that countdown. Release our owned movement keys for the complete
        // animation so an already-aligned biker is not pulled out of the
        // native range/side arc after the player presses Square or Circle.
        if (state.CloudAttackTimer != 0)
        {
            LastDecision = HighwayEngagementDecision.Hold;
            return HighwaySteeringDirection.None;
        }

        var activeEnemies = state.Enemies
            .Where(enemy =>
                enemy.IsActive &&
                enemy.HitPoints > 0 &&
                IsFinite(enemy.Position))
            .ToArray();
        var selected = HighwayEnemyTargetSelector.Select(
            state,
            activeEnemies,
            truckThreatDistance);
        var truckDelta = Subtract(state.Truck, state.Cloud);

        // Both modes: the bikers steer for the truck (FUN_00656880 aims types
        // 10..12 at actor slot 1 without a story-mode test) and each hit on it
        // costs the G Bike fifty points (FUN_00656361 -> FUN_006567B0).
        var truckIsFarAhead = truckDelta.Longitudinal > comfortableTruckDistance;

        // A distant truck is the primary objective. Only a biker already
        // threatening it is allowed to alter the lateral approach; this keeps
        // red bikers behind Cloud from luring automatic steering backward.
        if (truckIsFarAhead && selected is not { ThreatensTruck: true })
        {
            truckFarBehindActive = false;
            ClearTargetCorrections();
            LastDecision = HighwayEngagementDecision.Drive;
            return HighwaySteeringDirection.Up;
        }

        // Arcade only: the bike can outrun the truck, leaving it undefended.
        // Brake back toward it unless a biker is on the truck or already inside
        // the native sword range, and hold that until it is close again.
        truckFarBehindActive = !state.IsStoryChase && (truckFarBehindActive
            ? truckDelta.Longitudinal < -comfortableTruckDistance * TruckFarBehindReleaseFraction
            : truckDelta.Longitudinal < -comfortableTruckDistance);
        if (truckFarBehindActive &&
            selected is not { ThreatensTruck: true } &&
            selected is not { CloudDistance: <= HighwayAccessibilityTracker.NativeSwordRange })
        {
            ClearTargetCorrections();
            LastDecision = HighwayEngagementDecision.Drive;
            return HighwaySteeringDirection.Down;
        }

        if (selected is not { } selection)
        {
            ClearTargetCorrections();
            LastDecision = HighwayEngagementDecision.Idle;
            return HighwaySteeringDirection.None;
        }

        if (selection.Enemy.Slot != targetSlot)
        {
            targetSlot = selection.Enemy.Slot;
            targetAttackSide = HighwayAttackSide.None;
            lateralCorrectionActive = false;
            longitudinalCorrectionActive = false;
        }

        var delta = Subtract(selection.Enemy.Position, state.Cloud);
        targetAttackSide = ResolveAttackSide(targetAttackSide, delta.Lateral);
        var desiredLateral = targetAttackSide == HighwayAttackSide.LeftSquare
            ? -AttackPocketLateralUnits
            : AttackPocketLateralUnits;
        var lateralError = delta.Lateral - desiredLateral;
        var longitudinalError = delta.Longitudinal - AttackPocketLongitudinalUnits;
        lateralCorrectionActive = UpdateAxis(
            lateralCorrectionActive,
            Math.Abs(lateralError),
            LateralEntryUnits,
            LateralReleaseUnits);
        longitudinalCorrectionActive = UpdateAxis(
            longitudinalCorrectionActive,
            Math.Abs(longitudinalError),
            LongitudinalEntryUnits,
            LongitudinalReleaseUnits);

        var horizontal = lateralCorrectionActive
            ? lateralError >= 0d
                ? HighwaySteeringDirection.Right
                : HighwaySteeringDirection.Left
            : HighwaySteeringDirection.None;
        var vertical = truckIsFarAhead
            ? HighwaySteeringDirection.Up
            : longitudinalCorrectionActive
                ? longitudinalError >= 0d
                    ? HighwaySteeringDirection.Up
                    : HighwaySteeringDirection.Down
                : HighwaySteeringDirection.None;
        var direction = Combine(vertical, horizontal);
        LastDecision = direction == HighwaySteeringDirection.None
            ? HighwayEngagementDecision.Hold
            : HighwayEngagementDecision.Drive;
        return direction;
    }

    public void Reset()
    {
        targetSlot = -1;
        targetAttackSide = HighwayAttackSide.None;
        lateralCorrectionActive = false;
        longitudinalCorrectionActive = false;
        truckFarBehindActive = false;
        LastDecision = HighwayEngagementDecision.Idle;
    }

    private void ClearTargetCorrections()
    {
        targetSlot = -1;
        targetAttackSide = HighwayAttackSide.None;
        lateralCorrectionActive = false;
        longitudinalCorrectionActive = false;
    }

    private static bool UpdateAxis(
        bool active,
        double distance,
        double entry,
        double release) =>
        active ? distance > release : distance >= entry;

    private static HighwayAttackSide ResolveAttackSide(
        HighwayAttackSide current,
        double lateralDelta)
    {
        if (lateralDelta <= -HighwayAccessibilityTracker.AttackSideSwitchThreshold)
        {
            return HighwayAttackSide.LeftSquare;
        }

        if (lateralDelta >= HighwayAccessibilityTracker.AttackSideSwitchThreshold)
        {
            return HighwayAttackSide.RightCircle;
        }

        return current != HighwayAttackSide.None
            ? current
            : lateralDelta < 0d
                ? HighwayAttackSide.LeftSquare
                : HighwayAttackSide.RightCircle;
    }

    private static HighwaySteeringDirection Combine(
        HighwaySteeringDirection vertical,
        HighwaySteeringDirection horizontal) =>
        (vertical, horizontal) switch
        {
            (HighwaySteeringDirection.Up, HighwaySteeringDirection.Left) =>
                HighwaySteeringDirection.UpLeft,
            (HighwaySteeringDirection.Up, HighwaySteeringDirection.Right) =>
                HighwaySteeringDirection.UpRight,
            (HighwaySteeringDirection.Down, HighwaySteeringDirection.Left) =>
                HighwaySteeringDirection.DownLeft,
            (HighwaySteeringDirection.Down, HighwaySteeringDirection.Right) =>
                HighwaySteeringDirection.DownRight,
            (not HighwaySteeringDirection.None, _) => vertical,
            (_, not HighwaySteeringDirection.None) => horizontal,
            _ => HighwaySteeringDirection.None
        };

    private static HighwayPoint Subtract(HighwayPoint target, HighwayPoint origin) =>
        new(target.Lateral - origin.Lateral, target.Longitudinal - origin.Longitudinal);

    private static bool IsFinite(HighwayPoint point) =>
        double.IsFinite(point.Lateral) && double.IsFinite(point.Longitudinal);
}
