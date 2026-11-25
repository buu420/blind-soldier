namespace Ff7.Accessibility.Reloaded;

internal static class MidgarModelNavigationCatalog
{
    internal static IEnumerable<FieldStoryEventDefinition> EmptySlots()
    {
        // Native CITYA-E all accept bits 3..7 of 1[68], clear those carry bits,
        // then set their own bit in 3[180]. A piece has no assigned model slot.
        // Retain the preferred existing route, but recover if the player used a
        // different socket so its preferred socket is already occupied.
        var slots = new[]
        {
            ("bottom-right", 342, -194, -3, 64, 70),
            ("upper-right", 294, 170, 0, 32, 33),
            ("upper-left", -342, 89, 0, 8, 75),
            ("top", -149, 283, 0, 16, 68),
            ("bottom", 128, -316, 0, 128, 26)
        };
        foreach (var (name, x, y, z, bit, triangle) in slots)
            yield return new FieldStoryEventDefinition(249, FieldStoryTargetKind.Location,
                $"Place the carried Midgar part in the empty {name} model slot",
                X: x, Y: y, Z: z, TargetGameMoment: 264, MinimumGameMoment: 263, MaximumGameMoment: 263,
                Priority: 10,
                RequiredCondition: new(1, 68, 248, 0, AnyBitSet: true),
                CompletedCondition: new(3, 180, (byte)bit, (byte)bit),
                SourceFieldName: "blin65_2", SourceScriptType: $"Native triangle {triangle}",
                CompletesOnArrival: false,
                CompletionPlayerTriangles: triangle switch
                {
                    70 => [70, 34], 33 => [33, 6], 75 => [75, 31], 68 => [68, 16], _ => [26]
                });
    }
}
