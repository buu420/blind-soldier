namespace Ff7.Accessibility.Reloaded;

internal static class AncientForestInteractionGuidance
{
    // Current positions near the six native Init placements on the entrance screen (anfrst_3).
    // The field camera puts the two interior insects and the two far-side frogs at different
    // heights on the screen. These names describe the place, even after a creature is moved.
    private const int LandmarkRadius = 96;
    private static readonly (int X, int Y, int Z, string Description)[] EntranceLandmarks =
    [
        (-1810, -665, 28, "Near the forest entrance"),
        (-792, -489, 6, "Farther in, on the upper side of the path"),
        (-848, -735, 17, "Farther in, on the lower side of the path"),
        (-1537, -410, 25, "Above the entrance path"),
        (899, -375, 190, "On the upper bank beyond the pitcher plants"),
        (996, -758, 91, "On the lower bank beyond the pitcher plants")
    ];

    private static bool IsCreature(FieldNavigationTarget target) =>
        AncientForestPuzzleReadout.HasPuzzle(target.FieldId) &&
        target.Category == FieldNavigationCategory.Objects &&
        (target.Label.StartsWith("Insect ", StringComparison.Ordinal) ||
         target.Label.StartsWith("Frog ", StringComparison.Ordinal));

    internal static string? DescribeCreatureLocation(FieldPositionSnapshot position, FieldNavigationTarget target,
        FieldNavigationControlTransform? controlTransform, int unitsPerCount)
    {
        if (target.FieldId != position.FieldId || !IsCreature(target))
            return null;

        if (position.FieldId == 622)
        {
            foreach (var (x, y, z, description) in EntranceLandmarks)
            {
                var dx = (double)target.X - x;
                var dy = (double)target.Y - y;
                var dz = (double)target.Z - z;
                if (dx * dx + dy * dy + dz * dz <= LandmarkRadius * LandmarkRadius)
                    return description;
            }
        }

        if (controlTransform is not { } transform)
            return null;

        var offset = FieldNavigationSpokenCueFormatter.Format(
            target.X - position.X, target.Y - position.Y, transform, unitsPerCount);
        var height = (long)target.Z - position.Z;
        var vertical = Math.Abs(height) < unitsPerCount ? null : height > 0 ? "above you" : "below you";
        if (offset == "at destination")
            return vertical is null ? "Close to you" : height > 0 ? "Above you" : "Below you";
        return vertical is null ? $"Location {offset} from you" : $"Location {offset} from you, {vertical}";
    }

    internal static string? Describe(FieldNavigationTarget target, byte[]? bank)
    {
        if (!IsCreature(target) ||
            bank is not { Length: > 9 } || bank[9] == 0 ||
            bank[9] > (target.FieldId == 620 ? 5 : 6)) return null;
        return "Your normal Confirm button releases what you carry first.";
    }

    internal static bool IsBitePose(FieldActivityObservation observation)
    {
        var zone = ZoneAddress(observation.FieldId);
        return zone >= 0 && observation.ReadTemporaryByte is { } read &&
            IsBitePose(observation.FieldId, observation.PlayerTriangle, observation.PlayerX, observation.PlayerY,
                observation.PlayerZ, observation.IsPlayerControlled, read(zone));
    }

    internal static bool IsBitePose(FieldPositionSnapshot position, bool isPlayerControlled, byte[]? bank)
    {
        var zone = ZoneAddress(position.FieldId);
        return zone >= 0 && bank is not null && zone < bank.Length &&
            IsBitePose(position.FieldId, position.TriangleId, position.X, position.Y, position.Z, isPlayerControlled, bank[zone]);
    }

    private static int ZoneAddress(int field) => field switch { 620 => 20, 622 => 19, 623 => 22, _ => -1 };

    private static bool IsBitePose(int field, int triangle, int x, int y, int z, bool isControlled, int zone)
    {
        if (isControlled || zone != 0) return false;
        return (field, triangle) switch
        {
            (622, 23) => At(441, -914, 13),
            (620, 123) => At(-130, -360, -12),
            (620, 218) => At(1469, -187, -12),
            (623, 120) => At(592, -285, 37),
            _ => false
        };
        bool At(int biteX, int biteY, int biteZ) => Math.Abs((long)x - biteX) <= 2 &&
            Math.Abs((long)y - biteY) <= 2 && Math.Abs((long)z - biteZ) <= 2;
    }
}
