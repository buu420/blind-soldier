namespace Ff7.Accessibility.Core;

/// <summary>Why the automatic steering chose its direction on a poll.</summary>
public enum HighwayControlReason
{
    None,
    Engagement,
    EngagementHold,
    RoadEdge,
    RoadEdgeCritical,
    TruckAvoidance
}

public readonly record struct HighwayCompositeUpdate(
    HighwayCueRequest? CombatCue,
    HighwaySteeringCueRequest? SteeringCue,
    HighwaySpeechRequest? Speech,
    HighwaySteeringDirection AutomaticDirection,
    HighwayControlReason ControlReason = HighwayControlReason.None,
    double RoadEdgeRatio = double.NaN);

/// <summary>
/// Combines independently available highway combat and road observations and
/// chooses at most one audible output for a polling update.
/// </summary>
public sealed class HighwayAccessibilityComposer
{
    /// <summary>
    /// What a poll actually says out loud, when the steering mode has something to
    /// announce and the composed update does too.
    ///
    /// These arrive on the same pass and both are one-shot. The mode announces itself
    /// on the first poll of every ride and again on every F8, and the composed speech
    /// carries things that exist for a moment and then are gone - the arcade banner
    /// above all, which is READY exactly while READY is on screen. Speaking the mode
    /// and returning threw the other away: a ride acquired while READY was up lost
    /// READY, and pressing F8 during GO or GOAL lost that word for good, because the
    /// composer had already counted the banner as delivered.
    ///
    /// They are joined into one utterance instead, mode first, so neither can cut the
    /// other off and nothing is dropped.
    /// </summary>
    public static IReadOnlyList<(string Text, bool Interrupt)> Deliver(
        string? modeAnnouncement,
        HighwaySpeechRequest? speech)
    {
        var hasMode = !string.IsNullOrWhiteSpace(modeAnnouncement);
        if (!hasMode)
        {
            return speech is { } alone
                ? [(alone.Text, alone.Interrupt)]
                : Array.Empty<(string, bool)>();
        }

        // The mode always supersedes whatever was being said, and anything riding
        // along with it inherits that.
        return speech is { } composed
            ? [($"{modeAnnouncement} {composed.Text}", true)]
            : [(modeAnnouncement!, true)];
    }

    private readonly HighwayAccessibilityTracker combatTracker;
    private readonly HighwaySteeringTracker steeringTracker;
    private readonly HighwayEngagementSteeringTracker engagementTracker;
    private HighwayOutputSource lastModerateOutput;

    /// <summary>
    /// The support vehicle's offset from Cloud, which drives collision *avoidance*
    /// rather than escorting. It is supplied in both modes: FUN_00653076 initialises
    /// that vehicle in the arcade run as well, and FUN_006567B0 takes fifty points
    /// off when damage is applied, so the arcade player needs the same warning.
    /// </summary>
    public static HighwayPoint? TruckDeltaForTest(HighwayAccessibilityState? combatState) =>
        combatState is { } state
            ? new HighwayPoint(
                state.Truck.Lateral - state.Cloud.Lateral,
                state.Truck.Longitudinal - state.Cloud.Longitudinal)
            : null;

    public HighwayAccessibilityComposer(
        HighwayAccessibilityTracker combatTracker,
        HighwaySteeringTracker steeringTracker,
        HighwayEngagementSteeringTracker engagementTracker)
    {
        this.combatTracker = combatTracker ?? throw new ArgumentNullException(nameof(combatTracker));
        this.steeringTracker = steeringTracker ?? throw new ArgumentNullException(nameof(steeringTracker));
        this.engagementTracker = engagementTracker ?? throw new ArgumentNullException(nameof(engagementTracker));
    }

    public HighwayCompositeUpdate Update(
        HighwayAccessibilityState? combatState,
        HighwayRoadState? roadState,
        DateTime nowUtc,
        bool statusRequested,
        bool steeringAudioEnabled = true)
    {
        HighwayAccessibilityUpdate combatUpdate;
        if (combatState is null)
        {
            combatTracker.Reset();
            engagementTracker.Reset();
            combatUpdate = default;
        }
        else
        {
            combatUpdate = combatTracker.Update(combatState, nowUtc, statusRequested);
        }
        var engagementDirection = combatState is null
            ? HighwaySteeringDirection.None
            : engagementTracker.Update(combatState);

        HighwaySteeringUpdate steeringUpdate;
        if (roadState is not { } road)
        {
            steeringTracker.Reset();
            steeringUpdate = default;
        }
        else
        {
            // This delta drives collision *avoidance*, not escorting, and the support
            // vehicle is real and visible in the arcade mode too: FUN_00653076
            // initialises it in both, and FUN_006567B0 takes fifty points off when
            // damage is applied. Withholding it in arcade left the player with no
            // warning about the one obstacle that costs them score.
            var truckDelta = TruckDeltaForTest(combatState);
            steeringUpdate = steeringTracker.Update(road, truckDelta, nowUtc);
        }

        var (automaticDirection, controlReason, edgeRatio) = ComposeAutomaticDirection(
            combatState, roadState, steeringUpdate, engagementDirection);
        if (combatUpdate.Speech is { } speech)
        {
            return new HighwayCompositeUpdate(null, null, speech, automaticDirection, controlReason, edgeRatio);
        }

        var combatCue = combatUpdate.Cue;
        var steeringCue = steeringAudioEnabled ? steeringUpdate.Cue : null;
        if (steeringCue is { IsCritical: true } criticalSteering)
        {
            lastModerateOutput = HighwayOutputSource.Steering;
            return new HighwayCompositeUpdate(null, criticalSteering, null, automaticDirection, controlReason, edgeRatio);
        }

        if (combatCue is { Kind: HighwayCueKind.ImportantEnemy } importantCombat)
        {
            lastModerateOutput = HighwayOutputSource.Combat;
            return new HighwayCompositeUpdate(importantCombat, null, null, automaticDirection, controlReason, edgeRatio);
        }

        if (combatCue is { } moderateCombat && steeringCue is { } moderateSteering)
        {
            if (lastModerateOutput == HighwayOutputSource.Steering)
            {
                lastModerateOutput = HighwayOutputSource.Combat;
                return new HighwayCompositeUpdate(moderateCombat, null, null, automaticDirection, controlReason, edgeRatio);
            }

            lastModerateOutput = HighwayOutputSource.Steering;
            return new HighwayCompositeUpdate(null, moderateSteering, null, automaticDirection, controlReason, edgeRatio);
        }

        if (steeringCue is { } onlySteering)
        {
            lastModerateOutput = HighwayOutputSource.Steering;
            return new HighwayCompositeUpdate(null, onlySteering, null, automaticDirection, controlReason, edgeRatio);
        }

        if (combatCue is { } onlyCombat)
        {
            lastModerateOutput = HighwayOutputSource.Combat;
            return new HighwayCompositeUpdate(onlyCombat, null, null, automaticDirection, controlReason, edgeRatio);
        }

        return new HighwayCompositeUpdate(null, null, null, automaticDirection, controlReason, edgeRatio);
    }

    public void Reset()
    {
        combatTracker.Reset();
        steeringTracker.Reset();
        engagementTracker.Reset();
        lastModerateOutput = HighwayOutputSource.None;
        criticalEdgeActive = false;
    }

    /// <summary>
    /// The road edge takes the controls from the engagement in the arcade from this share of the
    /// road half-width, and gives them back below <see cref="CriticalEdgeReleaseRatio"/>. The
    /// entry is the tracker's own critical ratio; 006539B2 clamps the bike only at the half-width
    /// itself, which already has 96 units taken off it, so the interior below is safe to drive.
    /// </summary>
    public const double CriticalEdgeEntryRatio = HighwaySteeringTracker.CriticalCorrectionRatio;

    public const double CriticalEdgeReleaseRatio = 0.55d;

    private bool criticalEdgeActive;

    /// <summary>
    /// Who drives this poll. In the story chase, as before: any road or truck correction, then
    /// the engagement. In the arcade the engagement uses the safe interior of the road - driving
    /// to a biker, or holding still aligned in the sword pocket or through a swing - and only
    /// the truck-collision correction, or the road edge from the critical ratio (with hysteresis),
    /// takes the controls from it. With nothing to engage, the ordinary road correction applies.
    /// </summary>
    private (HighwaySteeringDirection Direction, HighwayControlReason Reason, double EdgeRatio) ComposeAutomaticDirection(
        HighwayAccessibilityState? combatState,
        HighwayRoadState? roadState,
        HighwaySteeringUpdate steeringUpdate,
        HighwaySteeringDirection engagementDirection)
    {
        var edgeRatio = roadState is { } road &&
                        double.IsFinite(road.CloudLateralUnits) &&
                        double.IsFinite(road.RoadHalfWidthUnits) &&
                        road.RoadHalfWidthUnits > 0d
            ? Math.Abs(road.CloudLateralUnits) / road.RoadHalfWidthUnits
            : double.NaN;
        var steering = steeringUpdate.Direction;
        var steeringReason = steering == HighwaySteeringDirection.None
            ? HighwayControlReason.None
            : steeringUpdate.Reason == HighwaySteeringCueReason.TruckAvoidance
                ? HighwayControlReason.TruckAvoidance
                : HighwayControlReason.RoadEdge;

        if (combatState is not { IsStoryChase: false })
        {
            criticalEdgeActive = false;
            if (steering != HighwaySteeringDirection.None)
            {
                return (steering, steeringReason, edgeRatio);
            }

            return combatState is null
                ? (HighwaySteeringDirection.None, HighwayControlReason.None, edgeRatio)
                : (engagementDirection, EngagementReason(engagementDirection), edgeRatio);
        }

        criticalEdgeActive = double.IsFinite(edgeRatio) &&
                             (criticalEdgeActive ? edgeRatio > CriticalEdgeReleaseRatio : edgeRatio >= CriticalEdgeEntryRatio);
        if (steeringReason == HighwayControlReason.TruckAvoidance)
        {
            return (steering, HighwayControlReason.TruckAvoidance, edgeRatio);
        }

        if (criticalEdgeActive)
        {
            var towardCentre = steering != HighwaySteeringDirection.None
                ? steering
                : roadState!.Value.CloudLateralUnits > 0d
                    ? HighwaySteeringDirection.Left
                    : HighwaySteeringDirection.Right;
            return (towardCentre, HighwayControlReason.RoadEdgeCritical, edgeRatio);
        }

        return engagementTracker.LastDecision switch
        {
            HighwayEngagementDecision.Drive => (engagementDirection, HighwayControlReason.Engagement, edgeRatio),
            HighwayEngagementDecision.Hold => (HighwaySteeringDirection.None, HighwayControlReason.EngagementHold, edgeRatio),
            _ => steering != HighwaySteeringDirection.None
                ? (steering, steeringReason, edgeRatio)
                : (HighwaySteeringDirection.None, HighwayControlReason.None, edgeRatio)
        };
    }

    private HighwayControlReason EngagementReason(HighwaySteeringDirection engagementDirection) =>
        engagementTracker.LastDecision switch
        {
            HighwayEngagementDecision.Drive when engagementDirection != HighwaySteeringDirection.None => HighwayControlReason.Engagement,
            HighwayEngagementDecision.Hold => HighwayControlReason.EngagementHold,
            _ => HighwayControlReason.None
        };

    private enum HighwayOutputSource
    {
        None,
        Steering,
        Combat
    }
}
