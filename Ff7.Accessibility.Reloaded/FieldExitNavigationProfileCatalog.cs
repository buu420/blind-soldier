namespace Ff7.Accessibility.Reloaded;

public static class FieldExitNavigationProfileCatalog
{
    private const int HoneyBeeRoomActivationRadius = 128;

    private static readonly IReadOnlySet<string> HoneyBeeRoomExitIds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "script-exit:218:13:220",
            "script-exit:218:14:220",
            "script-exit:218:15:219",
            "script-exit:218:16:219"
        };

    private static readonly IReadOnlyList<FieldNavigationRouteDetour> UpperWallMarketDetours =
    [
        new(
            new FieldNavigationTriggerLine(100, 1700, 0, 400, 1700, 0),
            -150,
            2000,
            0,
            120)
    ];

    public static FieldNavigationTarget Apply(FieldNavigationTarget target)
    {
        if (HoneyBeeRoomExitIds.Contains(target.StableId))
        {
            return target with
            {
                InteractionRadius = Math.Max(
                    HoneyBeeRoomActivationRadius,
                    target.InteractionRadius)
            };
        }

        if (string.Equals(
                target.StableId,
                "gateway:195:0:205",
                StringComparison.Ordinal) &&
            target.RouteDetour is null &&
            target.RouteDetours is not { Count: > 0 })
        {
            return target with { RouteDetours = UpperWallMarketDetours };
        }

        return target;
    }
}
