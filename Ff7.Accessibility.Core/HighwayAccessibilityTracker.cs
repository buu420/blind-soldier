namespace Ff7.Accessibility.Core;

public readonly record struct HighwayPoint(double Lateral, double Longitudinal);

public readonly record struct HighwayEnemyState(
    int Slot,
    int NativeType,
    bool IsActive,
    int HitPoints,
    HighwayPoint Position);

public readonly record struct HighwayPartyHealth(
    string Name,
    int CurrentHp,
    int MaximumHp);

/// <summary>
/// The word the arcade HUD is painting across the screen right now. The renderer puts
/// up one of its own three textures - guaa, huaa and iuaa, which read READY, GO! and
/// GOAL - and nothing at all for any other state.
/// </summary>
public enum HighwayBannerState
{
    /// <summary>
    /// The HUD could not be read this frame. Held rather than treated as a change,
    /// so a moment's failure during GOAL does not read as the word leaving and
    /// coming back.
    /// </summary>
    Unknown,

    None,
    Ready,
    Go,
    Goal
}

public sealed record HighwayAccessibilityState(
    HighwayPoint Cloud,
    HighwayPoint Truck,
    IReadOnlyList<HighwayEnemyState> Enemies,
    IReadOnlyList<HighwayPartyHealth> PartyHealth,
    int Score,
    bool IsStoryChase,
    int CloudAttackTimer = 0,
    int HighScore = 0,
    HighwayBannerState Banner = HighwayBannerState.None);

public enum HighwayCueKind
{
    LowerPriorityEnemy,
    ImportantEnemy,
    TruckBeacon
}

public enum HighwayAttackSide
{
    None,
    LeftSquare,
    RightCircle
}

public readonly record struct HighwayCueRequest(
    HighwayCueKind Kind,
    int TargetSlot,
    double DeltaLateral,
    double DeltaLongitudinal,
    double DistanceUnits,
    HighwayAttackSide AttackSide = HighwayAttackSide.None);

public enum HighwaySpeechKind
{
    Warning,
    Status
}

public readonly record struct HighwaySpeechRequest(
    HighwaySpeechKind Kind,
    string Text,
    bool Interrupt);

public readonly record struct HighwayAccessibilityUpdate(
    HighwayCueRequest? Cue,
    HighwaySpeechRequest? Speech);

/// <summary>
/// Pure timestamp-driven policy for highway spatial cues and visible status.
/// It emits information only and never produces gameplay input or synthetic
/// hit/defeat confirmation.
/// </summary>
public sealed class HighwayAccessibilityTracker
{
    public const double NativeSwordRange = 160d;
    public const double AttackSideSwitchThreshold = 16d;

    private readonly TimeSpan enemyCueInterval;
    private readonly TimeSpan truckCueInterval;
    private readonly TimeSpan globalCueInterval;
    private readonly double comfortableTruckDistance;
    private readonly double truckThreatDistance;
    private readonly double warningDistance;
    private readonly double warningRecoveryDistance;

    private DateTime nextEnemyCueUtc = DateTime.MinValue;
    private DateTime nextTruckCueUtc = DateTime.MinValue;
    private DateTime nextAnyCueUtc = DateTime.MinValue;
    private HighwayCueKind? lastCueKind;
    private bool chaseStarted;
    private bool distanceWarningArmed;
    private HighwayBannerState lastBanner = HighwayBannerState.None;
    private readonly Dictionary<int, HighwayAttackSide> attackSideBySlot = new();

    public HighwayAccessibilityTracker(
        TimeSpan enemyCueInterval,
        TimeSpan truckCueInterval,
        double comfortableTruckDistance,
        double truckThreatDistance,
        double warningDistance,
        double warningRecoveryDistance)
    {
        this.enemyCueInterval = NonNegative(enemyCueInterval);
        this.truckCueInterval = NonNegative(truckCueInterval);
        globalCueInterval = this.enemyCueInterval <= this.truckCueInterval
            ? this.enemyCueInterval
            : this.truckCueInterval;
        this.comfortableTruckDistance = Math.Max(0d, comfortableTruckDistance);
        this.truckThreatDistance = Math.Max(0d, truckThreatDistance);
        this.warningDistance = Math.Max(0d, warningDistance);
        this.warningRecoveryDistance = Math.Clamp(
            warningRecoveryDistance,
            0d,
            this.warningDistance);
    }

    public HighwayAccessibilityUpdate Update(
        HighwayAccessibilityState state,
        DateTime nowUtc,
        bool statusRequested)
    {
        ArgumentNullException.ThrowIfNull(state);
        var activeEnemies = state.Enemies
            .Where(enemy => enemy.IsActive && enemy.HitPoints > 0)
            .ToArray();
        PruneAttackSides(activeEnemies);
        if (activeEnemies.Length != 0)
        {
            chaseStarted = true;
        }

        var truckDelta = Subtract(state.Truck, state.Cloud);
        var truckDistance = Length(truckDelta);
        if (chaseStarted && truckDistance <= warningRecoveryDistance)
        {
            distanceWarningArmed = true;
        }

        // The word on screen, said when it appears rather than when the state that
        // will eventually produce it changes. GOAL carries the score the HUD is
        // showing at that moment, which is the one a sighted player reads off the
        // end of the ride.
        var bannerChanged =
            state.Banner != HighwayBannerState.Unknown && state.Banner != lastBanner;
        if (state.Banner != HighwayBannerState.Unknown)
        {
            lastBanner = state.Banner;
        }

        var bannerText = bannerChanged && state.Banner != HighwayBannerState.None
            ? DescribeBanner(state.Banner, state.Score)
            : null;

        if (statusRequested)
        {
            var status = CreateStatus(state, activeEnemies.Length, truckDelta, truckDistance);
            return new HighwayAccessibilityUpdate(
                null,
                new HighwaySpeechRequest(
                    HighwaySpeechKind.Status,
                    // A banner that came up on the same pass would otherwise be lost:
                    // the request is only true for this one tick, and so is the change.
                    bannerText is null ? status : $"{bannerText} {status}",
                    Interrupt: true));
        }

        if (bannerText is not null)
        {
            return new HighwayAccessibilityUpdate(
                null,
                new HighwaySpeechRequest(HighwaySpeechKind.Warning, bannerText, Interrupt: true));
        }

        if (chaseStarted && distanceWarningArmed && truckDistance >= warningDistance)
        {
            distanceWarningArmed = false;
            return new HighwayAccessibilityUpdate(
                null,
                new HighwaySpeechRequest(
                    HighwaySpeechKind.Warning,
                    "Too far from the truck.",
                    Interrupt: true));
        }

        if (!chaseStarted || nowUtc < nextAnyCueUtc)
        {
            return default;
        }

        var selectedEnemy = HighwayEnemyTargetSelector.Select(
            state,
            activeEnemies,
            truckThreatDistance);
        var enemyDue = selectedEnemy is not null && nowUtc >= nextEnemyCueUtc;
        var truckNeeded = truckDistance > comfortableTruckDistance;
        var truckDue = truckNeeded && nowUtc >= nextTruckCueUtc;

        HighwayCueRequest? cue = null;
        if (truckDue && enemyDue &&
            lastCueKind is HighwayCueKind.LowerPriorityEnemy or HighwayCueKind.ImportantEnemy)
        {
            cue = CreateTruckCue(truckDelta, truckDistance);
        }
        else if (enemyDue && selectedEnemy is { } enemySelection)
        {
            cue = CreateEnemyCue(state.Cloud, enemySelection.Enemy, enemySelection.IsImportant);
        }
        else if (truckDue)
        {
            cue = CreateTruckCue(truckDelta, truckDistance);
        }

        if (cue is not { } publishedCue)
        {
            return default;
        }

        lastCueKind = publishedCue.Kind;
        nextAnyCueUtc = nowUtc + globalCueInterval;
        if (publishedCue.Kind == HighwayCueKind.TruckBeacon)
        {
            nextTruckCueUtc = nowUtc + truckCueInterval;
        }
        else
        {
            nextEnemyCueUtc = nowUtc + enemyCueInterval;
        }

        return new HighwayAccessibilityUpdate(publishedCue, null);
    }

    public void Reset()
    {
        nextEnemyCueUtc = DateTime.MinValue;
        nextTruckCueUtc = DateTime.MinValue;
        nextAnyCueUtc = DateTime.MinValue;
        lastCueKind = null;
        chaseStarted = false;
        distanceWarningArmed = false;
        lastBanner = HighwayBannerState.None;
        attackSideBySlot.Clear();
    }

    private HighwayCueRequest CreateEnemyCue(
        HighwayPoint cloud,
        HighwayEnemyState enemy,
        bool important)
    {
        var delta = Subtract(enemy.Position, cloud);
        return new HighwayCueRequest(
            important ? HighwayCueKind.ImportantEnemy : HighwayCueKind.LowerPriorityEnemy,
            enemy.Slot,
            delta.Lateral,
            delta.Longitudinal,
            Length(delta),
            ResolveAttackSide(enemy.Slot, delta.Lateral));
    }

    private HighwayAttackSide ResolveAttackSide(int slot, double lateralDelta)
    {
        HighwayAttackSide side;
        if (lateralDelta <= -AttackSideSwitchThreshold)
        {
            side = HighwayAttackSide.LeftSquare;
        }
        else if (lateralDelta >= AttackSideSwitchThreshold)
        {
            side = HighwayAttackSide.RightCircle;
        }
        else if (!attackSideBySlot.TryGetValue(slot, out side) || side == HighwayAttackSide.None)
        {
            side = lateralDelta < 0d
                ? HighwayAttackSide.LeftSquare
                : HighwayAttackSide.RightCircle;
        }

        attackSideBySlot[slot] = side;
        return side;
    }

    private void PruneAttackSides(IReadOnlyList<HighwayEnemyState> activeEnemies)
    {
        if (attackSideBySlot.Count == 0)
        {
            return;
        }

        var activeSlots = activeEnemies.Select(enemy => enemy.Slot).ToHashSet();
        foreach (var staleSlot in attackSideBySlot.Keys.Where(slot => !activeSlots.Contains(slot)).ToArray())
        {
            attackSideBySlot.Remove(staleSlot);
        }
    }

    private static HighwayCueRequest CreateTruckCue(HighwayPoint delta, double distance) =>
        new(
            HighwayCueKind.TruckBeacon,
            TargetSlot: 1,
            delta.Lateral,
            delta.Longitudinal,
            distance);

    /// <summary>
    /// The spoken status for a state, without having to drive a whole tick through
    /// the tracker to reach it.
    /// </summary>
    public static string CreateStatusForTest(HighwayAccessibilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var truckDelta = Subtract(state.Truck, state.Cloud);
        return CreateStatus(
            state,
            state.Enemies.Count(enemy => enemy.IsActive && enemy.HitPoints > 0),
            truckDelta,
            Length(truckDelta));
    }

    /// <summary>
    /// What the banner says. GOAL is the end of the ride and the score beside it stops
    /// moving, so it is read out with the word; READY and GO are the start signal and
    /// carry nothing else.
    /// </summary>
    private static string DescribeBanner(HighwayBannerState banner, int score) => banner switch
    {
        HighwayBannerState.Ready => "Ready.",
        HighwayBannerState.Go => "Go!",
        HighwayBannerState.Goal => $"Goal. Score {score}.",
        _ => string.Empty
    };

    private static string CreateStatus(
        HighwayAccessibilityState state,
        int activeEnemyCount,
        HighwayPoint truckDelta,
        double truckDistance)
    {
        var bikerText = activeEnemyCount == 1
            ? "1 biker active."
            : $"{activeEnemyCount} bikers active.";
        var status =
            $"Highway. {bikerText} Truck {DescribeDirection(truckDelta)}, " +
            $"{Math.Round(truckDistance, MidpointRounding.AwayFromZero):0} units. " +
            $"Score {state.Score}.";

        // The arcade HUD draws HI-SCORE beside the running score; the story chase's
        // HUD is the party display and draws neither, so it is only added here where
        // it is actually on screen.
        if (!state.IsStoryChase && state.HighScore > 0)
        {
            status += $" High score {state.HighScore}.";
        }

        if (state.PartyHealth.Count == 0)
        {
            return status;
        }

        return status + " Party health: " + string.Join(
            "; ",
            state.PartyHealth.Select(member =>
                $"{member.Name} {member.CurrentHp} of {member.MaximumHp}")) + ".";
    }

    private static string DescribeDirection(HighwayPoint delta)
    {
        var max = Math.Max(Math.Abs(delta.Lateral), Math.Abs(delta.Longitudinal));
        if (max <= double.Epsilon)
        {
            return "here";
        }

        var threshold = max * 0.15d;
        var longitudinal = Math.Abs(delta.Longitudinal) <= threshold
            ? string.Empty
            : delta.Longitudinal > 0d ? "ahead" : "behind";
        var lateral = Math.Abs(delta.Lateral) <= threshold
            ? string.Empty
            : delta.Lateral > 0d ? "right" : "left";
        if (longitudinal.Length != 0 && lateral.Length != 0)
        {
            return longitudinal + " " + lateral;
        }

        return longitudinal.Length != 0 ? longitudinal : lateral;
    }

    private static HighwayPoint Subtract(HighwayPoint target, HighwayPoint origin) =>
        new(target.Lateral - origin.Lateral, target.Longitudinal - origin.Longitudinal);

    private static double Length(HighwayPoint point) =>
        Math.Sqrt(point.Lateral * point.Lateral + point.Longitudinal * point.Longitudinal);

    private static TimeSpan NonNegative(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value;

}
