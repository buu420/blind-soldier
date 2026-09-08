namespace Ff7.Accessibility.Core;

public readonly record struct HighwayCompositeUpdate(
    HighwayCueRequest? CombatCue,
    HighwaySteeringCueRequest? SteeringCue,
    HighwaySpeechRequest? Speech,
    HighwaySteeringDirection AutomaticDirection);

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

        // Checked road-edge and truck-collision corrections own the controls
        // while active. Combat-coordinate engagement fills the gap when the
        // road is centered or its x64 translated pointer is unavailable.
        var automaticDirection = steeringUpdate.Direction != HighwaySteeringDirection.None
            ? steeringUpdate.Direction
            : engagementDirection;
        if (combatUpdate.Speech is { } speech)
        {
            return new HighwayCompositeUpdate(null, null, speech, automaticDirection);
        }

        var combatCue = combatUpdate.Cue;
        var steeringCue = steeringAudioEnabled ? steeringUpdate.Cue : null;
        if (steeringCue is { IsCritical: true } criticalSteering)
        {
            lastModerateOutput = HighwayOutputSource.Steering;
            return new HighwayCompositeUpdate(null, criticalSteering, null, automaticDirection);
        }

        if (combatCue is { Kind: HighwayCueKind.ImportantEnemy } importantCombat)
        {
            lastModerateOutput = HighwayOutputSource.Combat;
            return new HighwayCompositeUpdate(importantCombat, null, null, automaticDirection);
        }

        if (combatCue is { } moderateCombat && steeringCue is { } moderateSteering)
        {
            if (lastModerateOutput == HighwayOutputSource.Steering)
            {
                lastModerateOutput = HighwayOutputSource.Combat;
                return new HighwayCompositeUpdate(moderateCombat, null, null, automaticDirection);
            }

            lastModerateOutput = HighwayOutputSource.Steering;
            return new HighwayCompositeUpdate(null, moderateSteering, null, automaticDirection);
        }

        if (steeringCue is { } onlySteering)
        {
            lastModerateOutput = HighwayOutputSource.Steering;
            return new HighwayCompositeUpdate(null, onlySteering, null, automaticDirection);
        }

        if (combatCue is { } onlyCombat)
        {
            lastModerateOutput = HighwayOutputSource.Combat;
            return new HighwayCompositeUpdate(onlyCombat, null, null, automaticDirection);
        }

        return new HighwayCompositeUpdate(null, null, null, automaticDirection);
    }

    public void Reset()
    {
        combatTracker.Reset();
        steeringTracker.Reset();
        engagementTracker.Reset();
        lastModerateOutput = HighwayOutputSource.None;
    }

    private enum HighwayOutputSource
    {
        None,
        Steering,
        Combat
    }
}
