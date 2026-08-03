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
    private readonly HighwayAccessibilityTracker combatTracker;
    private readonly HighwaySteeringTracker steeringTracker;
    private readonly HighwayEngagementSteeringTracker engagementTracker;
    private HighwayOutputSource lastModerateOutput;

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
            HighwayPoint? truckDelta = combatState is { IsStoryChase: true } state
                ? new HighwayPoint(
                    state.Truck.Lateral - state.Cloud.Lateral,
                    state.Truck.Longitudinal - state.Cloud.Longitudinal)
                : null;
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
