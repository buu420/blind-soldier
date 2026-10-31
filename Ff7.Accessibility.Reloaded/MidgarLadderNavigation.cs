namespace Ff7.Accessibility.Reloaded;

/// <summary>Visible scaffold ladders and the player's current native climb.</summary>
public static class MidgarLadderNavigation
{
    public static bool IsMountedChoice(FieldNavigationTarget target) =>
        target.StableId?.StartsWith("mounted-ladder:", StringComparison.Ordinal) == true &&
        !string.IsNullOrWhiteSpace(target.ManualNavigationGuidance);

    public static bool IsPlatformChoice(FieldNavigationTarget target) =>
        target.StableId?.StartsWith("ladder-end:", StringComparison.Ordinal) == true &&
        !string.IsNullOrWhiteSpace(target.ExitTraversalId);

    public static IReadOnlyList<FieldNavigationTarget> AppendTargets(
        IReadOnlyList<FieldNavigationTarget> exits,
        FieldPositionSnapshot position,
        IReadOnlyList<FieldScriptNavigationTransition> availableTransitions,
        Func<int, bool> isLineEnabled,
        FieldLadderStateSnapshot ladder)
    {
        if (!FieldPositionReader.IsUsable(position) || position.FieldId is not (733 or 734 or 735 or 736 or 778))
            return exits;
        if (ladder is { IsUsable: true, IsMounted: true, IsPlayerDriven: false })
            return exits;

        // A MAPJUMP may land midway up the ladder, outside the walking mesh's
        // connected floor. These are manual choices, not invented walking routes.
        if (ladder is { IsUsable: true, IsMounted: true } &&
            ladder.RequiredInput is FieldNavigationInput.Up or FieldNavigationInput.Down)
        {
            var back = ladder.RequiredInput == FieldNavigationInput.Up ? FieldNavigationInput.Down : FieldNavigationInput.Up;
            return new[] { MountedEnd(ladder.RequiredInput), MountedEnd(back) }
                .Concat(exits).ToArray();
        }

        if (position.FieldId is not (733 or 734)) return exits;
        var ladders = availableTransitions.Where(t =>
            t.FieldId == position.FieldId && t.Kind == FieldNavigationTransitionKind.Ladder && t.RequiresAction &&
            t.RequiredInput is FieldNavigationInput.Up or FieldNavigationInput.Down && t.TargetZ is not null && t.TargetTriangle >= 0 &&
            t.StableId.StartsWith("ladder:", StringComparison.Ordinal) &&
            isLineEnabled(t.SourceEntityId) &&
            !exits.Any(e => e.TriggerEntityId == t.SourceEntityId));
        return exits.Concat(ladders.Select(t => new FieldNavigationTarget(
            position.FieldId, FieldNavigationCategory.Exits,
            t.RequiredInput == FieldNavigationInput.Up ? "Ladder up to the upper platform" : "Ladder down to the lower platform",
            t.TargetX, t.TargetY, t.TargetZ!.Value, $"ladder-end:{t.StableId}",
            TriggerEntityId: t.SourceEntityId, CompletesOnArrival: true,
            ExitTraversalId: t.StableId))).ToArray();

        FieldNavigationTarget MountedEnd(FieldNavigationInput direction)
        {
            var word = direction == FieldNavigationInput.Up ? "up" : "down";
            var label = position.FieldId == 735
                ? direction == FieldNavigationInput.Up ? "Ladder up toward the tunnels" : "Ladder down toward the scaffold"
                : $"Climb {word} on the current ladder";
            return new(position.FieldId, FieldNavigationCategory.Exits, label,
                position.X, position.Y, position.Z, $"mounted-ladder:{position.FieldId}:{direction}",
                ManualNavigationGuidance: $"Hold {word} to climb.");
        }
    }
}
