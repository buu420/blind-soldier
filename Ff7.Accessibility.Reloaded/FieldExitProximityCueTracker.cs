namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldExitProximityCue(
    FieldNavigationTarget Target,
    float Gain,
    string TargetKey);

public sealed class FieldExitProximityCueTracker
{
    private static readonly IReadOnlyList<FieldExitProximityCue> EmptyCues =
        Array.Empty<FieldExitProximityCue>();

    private readonly int innerRange;
    private readonly int outerRange;
    private readonly TimeSpan pulseInterval;
    private readonly Dictionary<string, DateTime> nextPulseByTarget = new(StringComparer.Ordinal);

    public FieldExitProximityCueTracker(int innerRange, int outerRange, TimeSpan pulseInterval)
    {
        this.innerRange = Math.Max(0, innerRange);
        this.outerRange = Math.Max(this.innerRange + 1, outerRange);
        this.pulseInterval = pulseInterval < TimeSpan.Zero ? TimeSpan.Zero : pulseInterval;
    }

    public bool HasAudibleTargets { get; private set; }

    public IReadOnlyList<FieldExitProximityCue> Update(
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationTarget> targets,
        DateTime now)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            Reset();
            return EmptyCues;
        }

        var activeKeys = new HashSet<string>(StringComparer.Ordinal);
        var cues = new List<FieldExitProximityCue>();
        foreach (var target in targets.Where(target =>
                     target.FieldId == position.FieldId &&
                     target.Category == FieldNavigationCategory.Exits &&
                     FieldProximityElevationPolicy.IsOnCurrentLevel(position, target)))
        {
            var gain = CalculateGain(position, target);
            if (gain <= 0f)
            {
                continue;
            }

            var targetKey = GetStableTargetId(target);
            activeKeys.Add(targetKey);
            if (nextPulseByTarget.TryGetValue(targetKey, out var nextPulse) && now < nextPulse)
            {
                continue;
            }

            nextPulseByTarget[targetKey] = now + pulseInterval;
            cues.Add(new FieldExitProximityCue(target, gain, targetKey));
        }

        foreach (var staleKey in nextPulseByTarget.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
        {
            nextPulseByTarget.Remove(staleKey);
        }

        HasAudibleTargets = activeKeys.Count > 0;
        return cues.Count == 0 ? EmptyCues : cues;
    }

    public void Reset()
    {
        nextPulseByTarget.Clear();
        HasAudibleTargets = false;
    }

    private float CalculateGain(FieldPositionSnapshot position, FieldNavigationTarget target)
    {
        var dx = target.X - position.X;
        var dy = target.Y - position.Y;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
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

    private static string GetStableTargetId(FieldNavigationTarget target) =>
        string.IsNullOrWhiteSpace(target.StableId)
            ? $"{target.FieldId}:{target.X}:{target.Y}:{target.Z}:{target.Label}"
            : target.StableId;
}
