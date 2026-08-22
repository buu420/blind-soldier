using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// The exit-profile corrections the x64 runtime was skipping.
/// </summary>
/// <remarks>
/// <para>This was a live defect, not a missing enhancement. The four Honey Bee Inn
/// lobby doors carry an interaction radius of zero;
/// <c>FieldWalkmeshNavigation.TryBuildRoute</c> only attempts its third strategy
/// when that radius is above zero, so the route build failed and the reachable-exit
/// provider dropped the doors entirely. They never appeared in the exits list, so a
/// player on this runtime was never told the doorways existed - not a degraded
/// description, an absent one.</para>
///
/// <para>Written against the drop rather than against arrival: two investigations
/// disagreed about the mechanism, and the drop is the one that was verified from the
/// code and is the more severe reading.</para>
/// </remarks>
internal static class Steam2026FieldExitNavigationProfileTests
{
    private const int HoneyBeeRoomActivationRadius = 128;

    private static readonly string[] HoneyBeeDoors =
    [
        "script-exit:218:13:220",
        "script-exit:218:14:220",
        "script-exit:218:15:219",
        "script-exit:218:16:219"
    ];

    public static void Run()
    {
        TheHoneyBeeDoorsGetARadiusThatCanBuildARoute();
        EverythingElseIsLeftExactlyAsItWas();
    }

    private static FieldNavigationTarget Exit(string stableId, int interactionRadius = 0) =>
        new(
            FieldId: 218,
            Category: FieldNavigationCategory.Exits,
            Label: "door",
            X: 100,
            Y: 200,
            Z: 0,
            StableId: stableId,
            InteractionRadius: interactionRadius);

    private static void TheHoneyBeeDoorsGetARadiusThatCanBuildARoute()
    {
        var applied = Steam2026FieldNavigationCoordinator.ApplyExitNavigationProfiles(
            HoneyBeeDoors.Select(id => Exit(id)).ToArray());

        foreach (var door in applied)
        {
            if (door.InteractionRadius < HoneyBeeRoomActivationRadius)
            {
                throw new InvalidOperationException(
                    $"{door.StableId} kept an interaction radius of {door.InteractionRadius}. " +
                    "A radius of zero makes the route build fail and the door is then dropped " +
                    "from the exits list entirely, so the player is never told it is there.");
            }
        }

        if (applied.Count != HoneyBeeDoors.Length)
        {
            throw new InvalidOperationException(
                $"Expected {HoneyBeeDoors.Length} doors back, got {applied.Count}. " +
                "The catalog corrects targets; it must never remove them.");
        }
    }

    private static void EverythingElseIsLeftExactlyAsItWas()
    {
        // The catalog runs over every exit on every field, so anything it touches
        // that it should not is a change to navigation everywhere.
        var ordinary = new[]
        {
            Exit("script-exit:99:1:100", interactionRadius: 40),
            Exit("gateway:1:0:2", interactionRadius: 0)
        };

        var applied = Steam2026FieldNavigationCoordinator.ApplyExitNavigationProfiles(ordinary);
        for (var index = 0; index < ordinary.Length; index++)
        {
            if (applied[index] != ordinary[index])
            {
                throw new InvalidOperationException(
                    $"{ordinary[index].StableId} was altered by the exit profile catalog. " +
                    "Only the fields it names may change.");
            }
        }
    }
}
