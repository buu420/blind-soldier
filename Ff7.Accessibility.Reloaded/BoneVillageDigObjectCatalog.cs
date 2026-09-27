namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The Bone Village dig, 772 bonevil2: what a sighted player can walk to. It has no gateway
/// and no exit line. Two levels are joined by one ladder: ladu (entity 19) at the top and ladd
/// (entity 20) at the bottom, both LINEs whose Move climbs on a fresh OK. The diggers are
/// entities 14..18: each waits at his Init spot (-427, -5, triangle 49) until the party orders
/// a search, and then walks - up the ladder when the party is on the upper level - and jumps
/// to where the party stands (keyc script 3, and the digger's own script 3). Each one that has
/// gone is an object at his live position; one still waiting is not.
///
/// <para>What is buried is luna (entity 6) or one of box0..box6 (7..13), whichever the foreman
/// was asked for. Those models are placed where the item is, and are never objects.</para>
/// </summary>
public static class BoneVillageDigObjectCatalog
{
    public const int FieldId = 772;

    public static IReadOnlyList<FieldNavigationObjectDefinition> Create() =>
    [
        Ladder(19, "Ladder down to the lower level; press OK", "ladu", 170, 514, 331),
        Ladder(20, "Ladder up to the upper level; press OK", "ladd", 218, 379, -88),
        .. FieldActivityReadout.ExcavationWorkerEntityIds.Select((entity, index) =>
            new FieldNavigationObjectDefinition(
                FieldId,
                entity,
                FieldNavigationObjectKind.Named,
                Label: $"Digger {index + 1}",
                SourceFieldName: "bonevil2",
                TargetKind: FieldNavigationObjectTargetKind.Model,
                // Physical targets: manual guidance would make them hints the controller never
                // routes to. The field activity readout and its repeat give what to do here.
                WaitingSpot: [FieldActivityReadout.ExcavationWaitingX, FieldActivityReadout.ExcavationWaitingY]))
    ];

    private static FieldNavigationObjectDefinition Ladder(int entity, string label, string name, int x, int y, int z) =>
        new(
            FieldId,
            entity,
            FieldNavigationObjectKind.Named,
            Label: label,
            SourceFieldName: "bonevil2",
            SourceEntityName: name,
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: x,
            StaticY: y,
            StaticZ: z,
            UsesPlayerCollisionRadius: true);
}
