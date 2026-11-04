namespace Ff7.Accessibility.Reloaded;

internal static class AncientForestInteractionGuidance
{
    internal static string? Describe(FieldNavigationTarget target, byte[]? bank)
    {
        if (!AncientForestPuzzleReadout.HasPuzzle(target.FieldId) || target.Category != FieldNavigationCategory.Objects ||
            !(target.Label.StartsWith("Insect ", StringComparison.Ordinal) || target.Label.StartsWith("Frog ", StringComparison.Ordinal)) ||
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
