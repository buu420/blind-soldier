namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldLadderProximityCue(
    FieldScriptNavigationTransition Transition,
    float Gain,
    string TargetKey);

public sealed class FieldLadderProximityCueTracker
{
    private static readonly IReadOnlyList<FieldLadderProximityCue> EmptyCues =
        Array.Empty<FieldLadderProximityCue>();

    private readonly int innerRange;
    private readonly int outerRange;
    private readonly TimeSpan pulseInterval;
    private readonly Dictionary<string, DateTime> nextPulseByTarget = new(StringComparer.Ordinal);

    public FieldLadderProximityCueTracker(int innerRange, int outerRange, TimeSpan pulseInterval)
    {
        this.innerRange = Math.Max(0, innerRange);
        this.outerRange = Math.Max(this.innerRange + 1, outerRange);
        this.pulseInterval = pulseInterval < TimeSpan.Zero ? TimeSpan.Zero : pulseInterval;
    }

    public IReadOnlyList<FieldLadderProximityCue> Update(
        FieldPositionSnapshot position,
        IReadOnlyList<FieldScriptNavigationTransition> transitions,
        DateTime now)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            Reset();
            return EmptyCues;
        }

        var activeKeys = new HashSet<string>(StringComparer.Ordinal);
        var cues = new List<FieldLadderProximityCue>();
        foreach (var transition in transitions.Where(transition =>
                     transition.FieldId == position.FieldId &&
                     transition.Kind == FieldNavigationTransitionKind.Ladder))
        {
            var gain = CalculateGain(position, transition);
            if (gain <= 0f)
            {
                continue;
            }

            activeKeys.Add(transition.StableId);
            if (nextPulseByTarget.TryGetValue(transition.StableId, out var nextPulse) && now < nextPulse)
            {
                continue;
            }

            nextPulseByTarget[transition.StableId] = now + pulseInterval;
            cues.Add(new FieldLadderProximityCue(transition, gain, transition.StableId));
        }

        foreach (var staleKey in nextPulseByTarget.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
        {
            nextPulseByTarget.Remove(staleKey);
        }

        return cues.Count == 0 ? EmptyCues : cues;
    }

    public void Reset() => nextPulseByTarget.Clear();

    private float CalculateGain(
        FieldPositionSnapshot position,
        FieldScriptNavigationTransition transition)
    {
        var dx = transition.SourceX - position.X;
        var dy = transition.SourceY - position.Y;
        var dz = transition.SourceZ - position.Z;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy + dz * (double)dz);
        if (distance <= innerRange)
        {
            return 1f;
        }

        if (distance >= outerRange)
        {
            return 0f;
        }

        return (float)((outerRange - distance) / (outerRange - innerRange));
    }
}
