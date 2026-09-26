namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Visible crystals implemented by background OK lines rather than actor models.
/// Each cave has four sides of the same interaction; one reachable side is enough.
/// The live LINE gate and native collection bit keep unavailable crystals out of Objects.
/// </summary>
public static class MateriaCaveObjectCatalog
{
    private static readonly IReadOnlyList<FieldNavigationObjectDefinition> Definitions =
    [
        Crystal(82, "zz5", 43, 0x01, 252, -467, -853),
        Crystal(84, "zz7", 35, 0x04, 490, -13, -854),
        Crystal(85, "zz8", 89, 0x08, 172, -139, -853)
    ];

    public static IReadOnlyList<FieldNavigationObjectDefinition> Create() => Definitions;

    private static FieldNavigationObjectDefinition Crystal(
        int field, string name, int materia, byte collectedMask, int x, int y, int z) =>
        new(field, 4, FieldNavigationObjectKind.Named, NativeId: materia,
            Label: "Materia crystal", CollectedBank: 1, CollectedAddress: 49,
            CollectedMask: collectedMask, SourceFieldName: name, SourceEntityName: "l1",
            TargetKind: FieldNavigationObjectTargetKind.Line, StaticX: x, StaticY: y, StaticZ: z,
            CueKindOverride: FieldObjectCueKind.Materia, UsesPlayerCollisionRadius: true);
}
