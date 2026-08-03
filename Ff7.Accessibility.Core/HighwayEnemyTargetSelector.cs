namespace Ff7.Accessibility.Core;

internal readonly record struct HighwayEnemySelection(
    HighwayEnemyState Enemy,
    bool IsImportant,
    bool ThreatensTruck,
    double CloudDistance,
    double TruckDistance);

/// <summary>
/// Keeps the audible attack cue and automatic engagement steering focused on
/// the same live biker. Bikers already in sword range or threatening the story
/// truck take priority; otherwise the nearest biker to Cloud is selected.
/// </summary>
internal static class HighwayEnemyTargetSelector
{
    internal static HighwayEnemySelection? Select(
        HighwayAccessibilityState state,
        IReadOnlyList<HighwayEnemyState> activeEnemies,
        double truckThreatDistance)
    {
        HighwayEnemySelection? bestImportant = null;
        HighwayEnemySelection? bestLower = null;
        foreach (var enemy in activeEnemies)
        {
            var cloudDistance = Distance(enemy.Position, state.Cloud);
            var truckDistance = Distance(enemy.Position, state.Truck);
            var threatensTruck =
                state.IsStoryChase &&
                truckDistance <= Math.Max(0d, truckThreatDistance);
            var important =
                cloudDistance <= HighwayAccessibilityTracker.NativeSwordRange ||
                threatensTruck;
            var selection = new HighwayEnemySelection(
                enemy,
                important,
                threatensTruck,
                cloudDistance,
                truckDistance);
            if (important)
            {
                if (bestImportant is null || CompareImportant(selection, bestImportant.Value) < 0)
                {
                    bestImportant = selection;
                }
            }
            else if (bestLower is null || CompareLower(selection, bestLower.Value) < 0)
            {
                bestLower = selection;
            }
        }

        return bestImportant ?? bestLower;
    }

    private static int CompareImportant(
        HighwayEnemySelection left,
        HighwayEnemySelection right)
    {
        var result = right.ThreatensTruck.CompareTo(left.ThreatensTruck);
        if (result != 0)
        {
            return result;
        }

        result = left.TruckDistance.CompareTo(right.TruckDistance);
        if (result != 0)
        {
            return result;
        }

        result = left.CloudDistance.CompareTo(right.CloudDistance);
        return result != 0 ? result : left.Enemy.Slot.CompareTo(right.Enemy.Slot);
    }

    private static int CompareLower(
        HighwayEnemySelection left,
        HighwayEnemySelection right)
    {
        var result = left.CloudDistance.CompareTo(right.CloudDistance);
        return result != 0 ? result : left.Enemy.Slot.CompareTo(right.Enemy.Slot);
    }

    private static double Distance(HighwayPoint target, HighwayPoint origin)
    {
        var lateral = target.Lateral - origin.Lateral;
        var longitudinal = target.Longitudinal - origin.Longitudinal;
        return Math.Sqrt((lateral * lateral) + (longitudinal * longitudinal));
    }
}
