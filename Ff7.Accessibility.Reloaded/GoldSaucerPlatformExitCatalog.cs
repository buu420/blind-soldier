namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// gldgate has one gateway. Its seven visible platform exits instead poll the
/// leader's triangle in chekun/Main and dispatch cloud/Script4. Keep exploration
/// exits independent of Story's current objective and priority filtering.
/// </summary>
public static class GoldSaucerPlatformExitCatalog
{
    private static readonly IReadOnlyList<FieldNavigationTarget> Platforms =
    [
        Platform("Ghost Square", 491, 24, -133, 627),
        Platform("Battle Square", 499, 26, 427, 479),
        Platform("Wonder Square", 505, 28, 587, 259),
        Platform("Chocobo Square", 509, 30, 639, -22),
        Platform("Event Square", 484, 32, -637, 19),
        Platform("Speed Square", 486, 34, -559, 304),
        Platform("Round Square", 488, 36, -390, 507),
    ];

    public static IReadOnlyList<FieldNavigationTarget> ForField(int fieldId) =>
        fieldId == 497 ? Platforms : Array.Empty<FieldNavigationTarget>();

    public static string? ResolveLabel(FieldNavigationTarget target) =>
        target.FieldId == 497
            ? Platforms.FirstOrDefault(p => p.StableId == target.StableId).Label
            : null;

    private static FieldNavigationTarget Platform(string name, int destination, int firstTriangle, int x, int y) =>
        new(497, FieldNavigationCategory.Exits, $"Platform to {name}", x, y, 18,
            $"triangle-exit:497:{firstTriangle}:{destination}",
            CompletesOnArrival: true, DestinationFieldIds: [destination],
            CompletionTriangles: [firstTriangle, firstTriangle + 1]);
}
