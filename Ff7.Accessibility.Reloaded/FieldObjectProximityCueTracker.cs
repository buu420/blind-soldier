namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldObjectProximityCue(
    FieldObjectCueKind Kind,
    FieldNavigationTarget Target,
    float Gain,
    string ClusterKey);

public static class ObjectCueGainPolicy
{
    public static float Clamp(float gain) => Math.Clamp(gain, 0f, 1f);
}

public static class FieldProximityElevationPolicy
{
    // Native targets can be offset from the walkmesh surface on ramps and
    // trigger lines. A vertical separation of 192 units is where the route
    // system already treats overlapping geometry as a distinct level.
    public const int SeparateLevelMinimumVerticalDistance = 192;

    public static bool IsOnCurrentLevel(
        FieldPositionSnapshot position,
        FieldNavigationTarget target)
    {
        var verticalDistance = Math.Abs(target.Z - (long)position.Z);
        return verticalDistance < SeparateLevelMinimumVerticalDistance;
    }
}

public static class FieldProximitySpatializer
{
    public static NavigationBeaconCue? CreateCue(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        FieldNavigationControlTransform transform)
    {
        if (!FieldPositionReader.IsUsable(position) ||
            position.FieldId != target.FieldId ||
            !FieldProximityElevationPolicy.IsOnCurrentLevel(position, target))
        {
            return null;
        }

        var dx = target.X - position.X;
        var dy = target.Y - position.Y;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        var stick = transform.TransformWorldVector(dx, dy);
        var steamX = distance <= 0d ? 0f : stick.X;
        var steamZ = distance <= 0d ? -1f : stick.Y;
        return new NavigationBeaconCue(
            target.Label,
            distance <= 0d ? "here" : "proximity",
            stick.X,
            stick.Y,
            steamX,
            0f,
            steamZ,
            NavigationBeaconMovementState.OnCourse,
            220,
            distance);
    }
}

public static class FieldObjectProximitySpatializer
{
    public static NavigationBeaconCue? CreateCue(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        FieldNavigationControlTransform transform) =>
        FieldProximitySpatializer.CreateCue(position, target, transform);
}

public sealed class FieldObjectProximityCueTracker
{
    private static readonly IReadOnlyList<FieldObjectProximityCue> EmptyCues = Array.Empty<FieldObjectProximityCue>();

    private readonly int innerRange;
    private readonly int outerRange;
    private readonly int clusterRadius;
    private readonly TimeSpan pulseInterval;
    private readonly Dictionary<string, DateTime> nextPulseByCluster = new(StringComparer.Ordinal);

    public FieldObjectProximityCueTracker(int innerRange, int outerRange, int clusterRadius, TimeSpan pulseInterval)
    {
        this.innerRange = Math.Max(0, innerRange);
        this.outerRange = Math.Max(this.innerRange + 1, outerRange);
        this.clusterRadius = Math.Max(0, clusterRadius);
        this.pulseInterval = pulseInterval < TimeSpan.Zero ? TimeSpan.Zero : pulseInterval;
    }

    public IReadOnlyList<FieldObjectProximityCue> Update(
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationTarget> targets,
        DateTime now)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            Reset();
            return EmptyCues;
        }

        var candidates = targets
            .Where(target =>
                target.FieldId == position.FieldId &&
                target.ObjectCueKind != FieldObjectCueKind.None &&
                FieldProximityElevationPolicy.IsOnCurrentLevel(position, target))
            .ToArray();
        var activeKeys = new HashSet<string>(StringComparer.Ordinal);
        var cues = new List<FieldObjectProximityCue>();
        foreach (var members in BuildClusters(candidates))
        {
            var target = CreateClusterTarget(members);
            var gain = CalculateGain(position, target);
            if (gain <= 0f)
            {
                continue;
            }

            var key = CreateClusterKey(members);
            activeKeys.Add(key);
            if (nextPulseByCluster.TryGetValue(key, out var nextPulse) && now < nextPulse)
            {
                continue;
            }

            nextPulseByCluster[key] = now + pulseInterval;
            cues.Add(new FieldObjectProximityCue(target.ObjectCueKind, target, gain, key));
        }

        foreach (var staleKey in nextPulseByCluster.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
        {
            nextPulseByCluster.Remove(staleKey);
        }

        return cues.Count == 0 ? EmptyCues : cues;
    }

    public void Reset() => nextPulseByCluster.Clear();

    private IReadOnlyList<IReadOnlyList<FieldNavigationTarget>> BuildClusters(
        IReadOnlyList<FieldNavigationTarget> targets)
    {
        var clusters = new List<IReadOnlyList<FieldNavigationTarget>>();
        foreach (var kindGroup in targets.GroupBy(target => target.ObjectCueKind))
        {
            var remaining = kindGroup
                .OrderBy(GetStableMemberId, StringComparer.Ordinal)
                .ToList();
            while (remaining.Count != 0)
            {
                var members = new List<FieldNavigationTarget> { remaining[0] };
                remaining.RemoveAt(0);
                for (var index = 0; index < members.Count; index++)
                {
                    var member = members[index];
                    for (var candidateIndex = remaining.Count - 1; candidateIndex >= 0; candidateIndex--)
                    {
                        if (!IsWithinClusterRadius(member, remaining[candidateIndex]))
                        {
                            continue;
                        }

                        members.Add(remaining[candidateIndex]);
                        remaining.RemoveAt(candidateIndex);
                    }
                }

                clusters.Add(members);
            }
        }

        return clusters;
    }

    private bool IsWithinClusterRadius(FieldNavigationTarget first, FieldNavigationTarget second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * (double)dx + dy * (double)dy <= clusterRadius * (double)clusterRadius;
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

    private static FieldNavigationTarget CreateClusterTarget(IReadOnlyList<FieldNavigationTarget> members)
    {
        var first = members[0];
        var x = (int)Math.Round(members.Average(target => target.X), MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(members.Average(target => target.Y), MidpointRounding.AwayFromZero);
        var z = (int)Math.Round(members.Average(target => target.Z), MidpointRounding.AwayFromZero);
        var label = members.Count == 1
            ? first.Label
            : $"{members.Count} nearby {first.ObjectCueKind.ToString().ToLowerInvariant()} objects";
        return new FieldNavigationTarget(
            first.FieldId,
            FieldNavigationCategory.Objects,
            label,
            x,
            y,
            z,
            CreateClusterKey(members),
            first.ObjectCueKind);
    }

    private static string CreateClusterKey(IReadOnlyList<FieldNavigationTarget> members) =>
        $"{members[0].FieldId}:{members[0].ObjectCueKind}:" +
        string.Join("|", members.Select(GetStableMemberId).OrderBy(value => value, StringComparer.Ordinal));

    private static string GetStableMemberId(FieldNavigationTarget target) =>
        string.IsNullOrWhiteSpace(target.StableId)
            ? $"{target.FieldId}:{target.X}:{target.Y}:{target.Z}:{target.Label}"
            : target.StableId;
}
