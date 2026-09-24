namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Visible background interactions omitted by model-based NPC and treasure discovery.
/// LINE coordinates come from the native handler midpoint, and the reader uses the
/// live LINON state and player collision radius. Model doors use native visibility and
/// TLKON. These are optional Objects, never mandatory Story goals or puzzle solutions.
/// See analysis/2026-09-21-whole-game-coverage.md for native evidence and exclusions.
/// </summary>
public static class TownInteractionObjectCatalog
{
    /// <summary>uttmpin1's room behind the hanging scroll, as JIKU's own locks leave it.</summary>
    /// <remarks>Declared before the table that uses it: static initialisers run in order.</remarks>
    private static readonly int[] BehindTheHangingScroll = [94, 95, 96, 97];

    private static readonly IReadOnlyList<FieldNavigationObjectDefinition> Definitions =
    [
        // The seller grants a choice after accepting Mythril. Either native reward
        // script clears the shared permission bit; do not reveal either box's contents.
        Line(79, 5, "Small box upstairs", "zz2", "l1", -466, -87, 240) with
        { MinimumGameMoment = 566, RequiredBank = 11, RequiredAddress = 132, RequiredMask = 0x10, RequiredValue = 0x10 },
        Line(79, 6, "Large box downstairs", "zz2", "l2", 225, -24, 36) with
        { MinimumGameMoment = 566, RequiredBank = 11, RequiredAddress = 132, RequiredMask = 0x10, RequiredValue = 0x10 },
        Line(79, 7, "Bed", "zz2", "l3", 89, 87, 36) with { MinimumGameMoment = 566 },
        Line(151, 18, "Closed shop", "mds7", "item", -775, -595, 0),
        Line(151, 19, "AVALANCHE sign", "mds7", "tatekan", -781, -112, 0),
        Line(155, 10, "Television", "mds7pb_2", "TV", 91, 132, 0),
        Line(243, 16, "Library notice", "blin62_2", "PLINEC", -103, -124, 0),
        Line(243, 18, "Scientific Research Library sign", "blin62_2", "PLINED", 90, -124, 0),
        Line(244, 16, "Public Order and Weapon Development Library sign", "blin62_3", "PLINEC", -94, 979, 0),
        Line(244, 18, "Space Development Research Library sign", "blin62_3", "PLINED", 97, 979, 0),
        Line(287, 6, "Letter on Tifa's desk", "niv_ti2", "tukue", -303, 171, 0),
        Line(336, 4, "Door", "elmin2_1", "door", 104, 172, 10) with { RequiredBank = 5, RequiredAddress = 7, RequiredMask = 255 },
        Line(337, 5, "Locked chest", "elmin2_2", "box", -47, 119, 0),
        Line(492, 13, "Turtle's Paradise flyer", "ghotin_1", "TIRASI", -514, 478, 0),
        Line(493, 11, "Turtle's Paradise flyer", "ghotin_4", "TIRASI", -514, 478, 0),
        Line(503, 7, "Star Cup display", "clsin2_2", "l2", 1556, -2622, 0),
        Line(503, 8, "Weekend Clock display", "clsin2_2", "l3", 1714, -2622, 0),
        Line(503, 9, "Laugh Sapling display", "clsin2_2", "l4", 1854, -2622, 0),
        Line(503, 10, "Slayer's Pot display", "clsin2_2", "l5", 1998, -2622, 0),
        Line(503, 11, "Chisa's Mask display", "clsin2_2", "l6", 2114, -2633, 0),
        Line(503, 12, "Dio's Portrait display", "clsin2_2", "l7", 2128, -2830, 0),
        Line(503, 13, "D Type Equipment display", "clsin2_2", "l8", 2065, -2986, 0),
        Line(503, 14, "Zauger's Cup display", "clsin2_2", "l9", 1927, -2995, 0),
        Line(503, 15, "Calling Gourd display", "clsin2_2", "l10", 1783, -3007, 0),
        Line(503, 16, "Kleine's Pot display", "clsin2_2", "l11", 1587, -3007, 0),
        Line(525, 29, "Pub Starlet sign", "cos_btm", "KANBAN", -1307, -986, -2141),
        Line(529, 7, "Turtle's Paradise flyer", "cosin1", "LINEH", 105, -185, -192),
        Line(532, 14, "Turtle's Paradise flyer", "cosin3", "TIRASI", -237, 6, 0),
        Line(553, 8, "Antique gun display", "rkt_w", "line2", 21, 214, 0),
        Line(555, 5, "Bathroom door", "rktinn1", "benjo", 12, 471, -57) with { RequiredBank = 5, RequiredAddress = 13, RequiredMask = 255 },
        Line(558, 13, "Locked door", "rktsid", "lock", 27, 778, 0) with { MinimumGameMoment = 567 },
        Line(578, 16, "Folding screen", "utmin2", "BYOBU", -86, -113, 10),
        Line(578, 17, "Folding screen, other side", "utmin2", "BYOBUB", -171, 28, 10),
        Line(579, 29, "Turtle's Paradise publicity notice", "uutai1", "KANBAN", -1088, -209, 0),
        Line(582, 11, "Levers", "yufy2", "SWITCH", 114, 207, -25),
        Line(582, 14, "Turtle's Paradise flyer", "yufy2", "TIRASI", 268, -559, -25),
        Line(588, 15, "Resting room", "uttmpin1", "LINEQ", -79, 91, 0),
        Line(589, 17, "Revolving door", "uttmpin2", "LINEA", -4361, -115, 1123),
        Line(589, 18, "Revolving door, other side", "uttmpin2", "LINEB", -4219, -110, 1123),
        Line(657, 19, "Window", "snmayor", "tenmado", -246, 972, 302),
        Model(588, 16, "Sliding door, left panel", "uttmpin1", "D3"),
        Model(588, 17, "Sliding door, right panel", "uttmpin1", "D4"),
        Model(589, 12, "Sliding door, left panel", "uttmpin2", "D1"),
        Model(589, 13, "Sliding door, right panel", "uttmpin2", "D2"),
        Model(589, 14, "Sliding door, left panel", "uttmpin2", "D3"),
        Model(589, 15, "Sliding door, right panel", "uttmpin2", "D4"),
        Model(443, 23, "Beach ball", "del2", "ball"),
        Model(472, 5, "Chest", "jailin1", "hako"),
        Model(473, 17, "Chest", "jail2", "hako"),
        Model(620, 7, "Insect", "anfrst_1", "bat0"),
        Model(620, 8, "Insect", "anfrst_1", "bat1"),
        Model(620, 9, "Frog", "anfrst_1", "fro0"),
        Model(620, 10, "Frog", "anfrst_1", "fro1"),
        Model(620, 16, "Beehive", "anfrst_1", "hanak"),
        Model(622, 6, "Insect", "anfrst_3", "bat0"),
        Model(622, 7, "Insect", "anfrst_3", "bat1"),
        Model(622, 8, "Insect", "anfrst_3", "bat2"),
        Model(622, 9, "Frog", "anfrst_3", "fro0"),
        Model(622, 10, "Frog", "anfrst_3", "fro1"),
        Model(622, 11, "Frog", "anfrst_3", "fro0"),
        Model(623, 6, "Insect", "anfrst_4", "bat0"),
        Model(623, 7, "Insect", "anfrst_4", "bat1"),
        Model(623, 8, "Insect", "anfrst_4", "bat2"),
        Model(623, 9, "Insect", "anfrst_4", "bat3"),
        Model(623, 10, "Frog", "anfrst_4", "fro0"),
        Model(623, 16, "Beehive", "anfrst_4", "hanak"),
        // uutai2/AD holds triangle 114, the only way up to the bell, until Yuffie's escape
        // from the cage sets Bank[3][189] bit 0, and it never lets go before that.
        new(587, 14, FieldNavigationObjectKind.Named, Label: "Bell",
            SourceFieldName: "uutai2", SourceEntityName: "KANE",
            TargetKind: FieldNavigationObjectTargetKind.Location,
            StaticX: -623, StaticY: -4862, StaticZ: 127, InteractionRadiusOverride: 12,
            RequiredBank: 3, RequiredAddress: 189, RequiredMask: 0x01, RequiredValue: 0x01),
        new(588, 14, FieldNavigationObjectKind.Named, Label: "Hanging scroll",
            SourceFieldName: "uttmpin1", SourceEntityName: "JIKU",
            TargetKind: FieldNavigationObjectTargetKind.Location,
            StaticX: -305, StaticY: -65, StaticZ: 0, InteractionRadiusOverride: 12,
            ExcludedPlayerTriangles: BehindTheHangingScroll),
        // The same scroll from the room behind it. Coming back from hideway1, JIKU's Init
        // holds triangle 22, so triangles 94 to 97 are all there is and the front of the
        // scroll cannot be reached. Its reverse handler turns the scroll only from
        // triangle 95 and only while the leader's X is greater than -619; this point and
        // its whole arrival disc lie inside both. Each face is offered only from its own
        // side, so the hall never names a room behind the scroll.
        new(588, 14, FieldNavigationObjectKind.Named, Label: "Hanging scroll, other side",
            SourceFieldName: "uttmpin1", SourceEntityName: "JIKU",
            TargetKind: FieldNavigationObjectTargetKind.Location,
            StaticX: -440, StaticY: -44, StaticZ: 0, InteractionRadiusOverride: 12,
            RequiredPlayerTriangles: BehindTheHangingScroll),
    ];

    public static IReadOnlyList<FieldNavigationObjectDefinition> Create() => Definitions;

    private static FieldNavigationObjectDefinition Line(
        int field, int entity, string label, string fieldName, string entityName, int x, int y, int z) =>
        new(field, entity, FieldNavigationObjectKind.Named, Label: label,
            SourceFieldName: fieldName, SourceEntityName: entityName,
            TargetKind: FieldNavigationObjectTargetKind.Line, StaticX: x, StaticY: y, StaticZ: z,
            UsesPlayerCollisionRadius: true);

    private static FieldNavigationObjectDefinition Model(
        int field, int entity, string label, string fieldName, string entityName) =>
        new(field, entity, FieldNavigationObjectKind.Named, Label: label,
            SourceFieldName: fieldName, SourceEntityName: entityName, UsesTalkInteraction: true,
            CueKindOverride: label == "Chest" ? FieldObjectCueKind.Chest : null);
}
