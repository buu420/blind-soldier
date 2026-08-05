namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Resolves native field-script exits whose MAPJUMP destinations are selected
/// by progression branches that the static script scan cannot evaluate.
/// </summary>
public static class FieldScriptExitBranchPolicy
{
    private const int SharedReactorElevatorField = 121;
    private static readonly IReadOnlyList<int> ReactorElevatorDestinations =
        [120, 122, 128, 129];

    public static IReadOnlyList<FieldNavigationTarget> Resolve(
        int fieldId,
        int gameMoment,
        IReadOnlyList<FieldNavigationTarget> scriptExits)
    {
        ArgumentNullException.ThrowIfNull(scriptExits);
        if (fieldId != SharedReactorElevatorField || scriptExits.Count == 0)
        {
            return scriptExits;
        }

        var destination = ResolveReactorElevatorDestination(gameMoment);
        List<FieldNavigationTarget>? resolved = null;
        for (var index = 0; index < scriptExits.Count; index++)
        {
            var target = scriptExits[index];
            if (!IsConditionalReactorElevatorExit(target))
            {
                resolved?.Add(target);
                continue;
            }

            resolved ??= new List<FieldNavigationTarget>(scriptExits.Take(index));
            resolved.Add(target with
            {
                StableId = $"script-exit:{fieldId}:{target.TriggerEntityId}:{destination}",
                DestinationFieldIds = [destination]
            });
        }

        return resolved ?? scriptExits;
    }

    private static int ResolveReactorElevatorDestination(int gameMoment)
    {
        var isReactor5 = gameMoment >= 117;
        var elevatorHasTravelledDown = gameMoment is >= 12 and <= 26 or >= 117 and <= 126;
        return (isReactor5, elevatorHasTravelledDown) switch
        {
            (false, false) => 120,
            (false, true) => 122,
            (true, false) => 128,
            (true, true) => 129
        };
    }

    private static bool IsConditionalReactorElevatorExit(FieldNavigationTarget target) =>
        target.FieldId == SharedReactorElevatorField &&
        target.Category == FieldNavigationCategory.Exits &&
        target.DestinationFieldIds is { Count: > 0 } destinations &&
        destinations.Any(ReactorElevatorDestinations.Contains);
}
