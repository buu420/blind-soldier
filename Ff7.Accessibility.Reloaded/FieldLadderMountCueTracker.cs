namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Emits the dedicated mount cue only while the player is standing at the
/// active route's native ladder entrance. Leaving the entrance resets the
/// interval so returning produces an immediate cue.
/// </summary>
public sealed class FieldLadderMountCueTracker
{
    public const int DefaultEntranceRange = 56;

    private static readonly IReadOnlyList<FieldLadderProximityCue> EmptyCues =
        Array.Empty<FieldLadderProximityCue>();

    private readonly int entranceRange;
    private readonly TimeSpan pulseInterval;
    private string activeTransitionId = string.Empty;
    private DateTime nextPulseAt = DateTime.MinValue;

    public FieldLadderMountCueTracker(int entranceRange, TimeSpan pulseInterval)
    {
        this.entranceRange = Math.Max(0, entranceRange);
        this.pulseInterval = pulseInterval < TimeSpan.Zero ? TimeSpan.Zero : pulseInterval;
    }

    public bool IsAtEntrance { get; private set; }

    public IReadOnlyList<FieldLadderProximityCue> Update(
        FieldPositionSnapshot position,
        IReadOnlyList<FieldScriptNavigationTransition> transitions,
        DateTime now,
        string? prioritizedTransitionId)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        if (!FieldPositionReader.IsUsable(position) ||
            string.IsNullOrWhiteSpace(prioritizedTransitionId))
        {
            Reset();
            return EmptyCues;
        }

        var transition = transitions.FirstOrDefault(candidate =>
            candidate.FieldId == position.FieldId &&
            candidate.Kind == FieldNavigationTransitionKind.Ladder &&
            string.Equals(candidate.StableId, prioritizedTransitionId, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(transition.StableId) || !IsWithinEntrance(position, transition))
        {
            Reset();
            return EmptyCues;
        }

        if (!string.Equals(activeTransitionId, transition.StableId, StringComparison.Ordinal))
        {
            activeTransitionId = transition.StableId;
            nextPulseAt = DateTime.MinValue;
        }

        IsAtEntrance = true;
        if (now < nextPulseAt)
        {
            return EmptyCues;
        }

        nextPulseAt = now + pulseInterval;
        return [new FieldLadderProximityCue(transition, 1f, transition.StableId)];
    }

    public void Reset()
    {
        activeTransitionId = string.Empty;
        nextPulseAt = DateTime.MinValue;
        IsAtEntrance = false;
    }

    private bool IsWithinEntrance(
        FieldPositionSnapshot position,
        FieldScriptNavigationTransition transition)
    {
        var dx = transition.SourceX - position.X;
        var dy = transition.SourceY - position.Y;
        var dz = transition.SourceZ - position.Z;
        return dx * (double)dx + dy * (double)dy + dz * (double)dz <=
               entranceRange * (double)entranceRange;
    }
}
