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

    // The Ancient Forest's crossed zone lines, from their LINE opcodes; declared before the
    // table that uses them.
    private static readonly FieldNavigationTriggerLine Big0Throw = new(121, -321, -12, 92, -692, -10);
    private static readonly FieldNavigationTriggerLine Big1Throw = new(1311, -7, -12, 1225, -365, -12);
    private static readonly FieldNavigationTriggerLine Bdl10 = new(617, -120, 202, 610, -372, 159);
    private static readonly FieldNavigationTriggerLine Rk0Right = new(-1795, -155, 91, -1772, -354, 67);
    private static readonly FieldNavigationTriggerLine HollowLine = new(-610, -266, 0, -475, -265, 0);
    private static readonly FieldNavigationTriggerLine Rk1Right = new(31, 121, 57, 36, -213, 32);
    private static readonly FieldNavigationTriggerLine Rk2Left = new(488, 144, 56, 411, -110, 40);
    private static readonly FieldNavigationTriggerLine Rk3Right = new(1044, -206, 58, 1070, -388, 46);
    private static readonly FieldNavigationTriggerLine BigT0 = new(266, -545, 27, 682, -593, 24);
    private static readonly FieldNavigationTriggerLine BigT1 = new(860, -293, 36, 682, -593, 24);

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
        // gldgate's information counter. al's [OK] (LINE script 1) returns at game moments 439
        // and 598 (16200000B7010002, 1620000056020002) and otherwise MAPJUMPs to gldinfo, the
        // Info Board screen that describes the squares - a screen, not a way on, so it is an
        // Object here and not an exit. Offered at every other moment.
        Line(497, 14, "Information counter", "gldgate", "al", 127, 552, 0) with { MaximumGameMoment = 438 },
        Line(497, 14, "Information counter", "gldgate", "al", 127, 552, 0) with { MinimumGameMoment = 440, MaximumGameMoment = 597 },
        Line(497, 14, "Information counter", "gldgate", "al", 127, 552, 0) with { MinimumGameMoment = 599 },
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
        // Where a carried insect, frog or beehive goes when the player presses OK (bpd's Main:
        // IFKEYON OK while 5[9] names what is carried): each creature's script 3 (or 4) reads the
        // zone the leader stands in, 5[20] in anfrst_1, 5[19] in anfrst_3 and 5[22] in anfrst_4,
        // which these lines write. The plants are background layers (utubo, bigmou) with no model,
        // so without these a player hears the creatures and not what they are thrown at. Which
        // creature, which plant and in what order is left to the player.
        //
        // A line that writes its zone on touch (Go 1x, Move) is offered as the line itself, inside
        // the player's own collision range. One that writes it only when crossed (its slot 3,
        // 00637ABB's crossing flag) is offered as a point just past it, from the side the walk
        // there has to cross it from; the other side is offered only where a walk from there was
        // proven to cross it and stop short of the line beyond that resets the zone.
        Line(620, 18, Pitcher, "anfrst_1", "ltrock", -635, 1, 334),   // 3: utubo_1
        Line(620, 20, Pitcher, "anfrst_1", "rrklf", 892, 66, 122),    // 5: utubo_2
        Line(620, 22, Pitcher, "anfrst_1", "rrkrt", 975, 77, 117),    // 6: utubo_3
        Line(622, 14, Pitcher, "anfrst_3", "bdl9", -56, -155, 228),   // 2: utubo_1
        Crossed(620, 29, Flytrap, "anfrst_1", "big0thw", 95, -506, -9, Big0Throw, 1),          // 7: beehive to bigmou1
        Crossed(620, 29, Flytrap, "anfrst_1", "big0thw", 118, -507, -12, Big0Throw, -1),
        Crossed(620, 31, Flytrap, "anfrst_1", "bg1thw", 1280, -189, 5, Big1Throw, -1),         // 8: beehive to bigmou2
        Crossed(620, 31, Flytrap, "anfrst_1", "bg1thw", 1256, -183, -6, Big1Throw, 1),
        Crossed(622, 15, Pitcher, "anfrst_3", "bdl10", 602, -246, 185, Bdl10, 1),                // 6: utubo_3
        Crossed(623, 18, Pitcher, "anfrst_4", "rk0rt", -1772, -253, 80, Rk0Right, -1),           // 2: utubo_1
        Crossed(623, 18, Pitcher, "anfrst_4", "rk0rt", -1795, -256, 79, Rk0Right, 0, [206, 207, 208]),
        Crossed(623, 20, Hollow, "anfrst_4", "frhol0", -543, -254, 8, HollowLine, -1),           // 5: frog from the tree
        Crossed(623, 20, Hollow, "anfrst_4", "frhol0", -542, -277, 0, HollowLine, 0, [155, 156, 157, 158, 159, 160]),
        Crossed(623, 23, Pitcher, "anfrst_4", "rk1rt", 45, -46, 49, Rk1Right, -1),               // 6: utubo_3
        Crossed(623, 23, Pitcher, "anfrst_4", "rk1rt", 22, -46, 47, Rk1Right, 1),
        Crossed(623, 25, PitcherOtherSide, "anfrst_4", "rk2lt", 438, 20, 49, Rk2Left, 1),       // 8: joins 6
        Crossed(623, 25, PitcherOtherSide, "anfrst_4", "rk2lt", 461, 14, 49, Rk2Left, 0, [133]),
        Crossed(623, 29, Pitcher, "anfrst_4", "rk3rt", 1069, -295, 45, Rk3Right, -1),            // 9: utubo_4
        Crossed(623, 35, Flytrap, "anfrst_4", "bigt0", 475, -557, 30, BigT0, -1, excluded: [94, 95]), // 13: beehive to bigmou
        Crossed(623, 35, Flytrap, "anfrst_4", "bigt0", 473, -581, 28, BigT0, 1),
        Crossed(623, 36, Flytrap, "anfrst_4", "bigt1", 761, -437, 32, BigT1, 1),
        Crossed(623, 36, Flytrap, "anfrst_4", "bigt1", 781, -449, 31, BigT1, -1),
        // ujp0 writes zone 3 while the leader stands on utubo_1's top (t0, t1): a throw from there
        // goes to utubo_2.
        Here(623, 2, Pitcher, "anfrst_4", -1584, -207, 187, [0, 1]),

        // The forest's jumps the player makes: a line or a triangle the leader stands on, a key
        // held (IFKEY) or pressed (IFKEYON), and for most the next pitcher plant closed (its
        // director's byte is 1 from the bite until it opens). Each is offered only while its own
        // gate holds and says which key; nothing is jumped for the player.
        TakeOff(620, 19, OntoPlant("Right"), "anfrst_1", "ltrkjp", -666, 1, 334, 17),
        TakeOff(620, 21, OntoPlant("Left"), "anfrst_1", "rrklfjp", 937, 59, 122, 18),
        TakeOff(620, 23, OntoPlant("Right"), "anfrst_1", "rrkrtjp", 932, 63, 122, 19),
        TakeOff(620, 24, "Jump from here: hold Down", "anfrst_1", "hakjp", 281, -247, 526, 60),
        TakeOff(620, 33, "Jump from here: press Down", "anfrst_1", "lfrkjp0", -677, 10, 334, null),
        TakeOff(622, 20, "Jump onto the closed pitcher plant: from here, walk on holding Right", "anfrst_3", "jp0", 0, -126, 243, 16),
        TakeOff(622, 21, "Jump onto the closed pitcher plant: from here, walk on holding Left", "anfrst_3", "jp1", 620, -246, 187, 18),
        TakeOff(623, 19, OntoPlant("Right"), "anfrst_4", "rk0jp0", -1736, -236, 84, 18),
        TakeOff(623, 24, OntoPlant("Right"), "anfrst_4", "rk1rtjp", 67, 10, 56, 20),
        TakeOff(623, 27, OntoPlant("Left"), "anfrst_4", "rk2rtjp", 432, 7, 48, 20),
        TakeOff(623, 30, OntoPlant("Right"), "anfrst_4", "rk3rtjp", 1081, -285, 48, 21),
        TakeOff(623, 34, "Jump from here: hold Down", "anfrst_4", "hakjp", 886, -99, 331, null),
        Polled(620, 3, "Jump from here: hold Left or Right", "anfrst_1", -484, -153, 410, [8], null),
        Polled(620, 3, OntoPlant("Left"), "anfrst_1", -361, -131, 345, [9, 10], 17),
        Polled(620, 3, "Jump from here: hold Right", "anfrst_1", -361, -131, 345, [9, 10], null),
        Polled(620, 3, "Jump from here: hold Right", "anfrst_1", 732, -152, 267, [19, 20], null),
        Polled(620, 3, "Jump from here: hold Left", "anfrst_1", 1079, 41, 252, [21, 22], null),
        Polled(622, 2, "Jump from here: hold Left", "anfrst_3", 186, -108, 269, [14, 2], null),
        Polled(622, 2, OntoPlant("Right"), "anfrst_3", 186, -108, 269, [14, 2], 17),
        Polled(622, 2, OntoPlant("Left"), "anfrst_3", 310, -145, 364, [29, 25], 16),
        Polled(622, 2, OntoPlant("Right"), "anfrst_3", 310, -145, 364, [29, 25], 18),
        Polled(622, 2, OntoPlant("Left"), "anfrst_3", 448, -195, 309, [26, 27], 17),
        Polled(622, 2, "Jump from here: hold Right", "anfrst_3", 448, -195, 309, [26, 27], null),
        Polled(623, 2, "Jump from here: hold Left", "anfrst_4", -1584, -207, 187, [0, 1], null),
        Polled(623, 2, OntoPlant("Right"), "anfrst_4", -1584, -207, 187, [0, 1], 19),
        Polled(623, 2, OntoPlant("Left"), "anfrst_4", -1446, -196, 261, [2, 3], 18),
        Polled(623, 2, "Jump from here: hold Right", "anfrst_4", -1446, -196, 261, [2, 3], null),
        Polled(623, 2, "Jump from here: hold Left or Right", "anfrst_4", 260, 34, 168, [34, 35], null),
        Polled(623, 2, "Jump from here: hold Left or Up", "anfrst_4", 1199, -305, 203, [44, 45], null),
        Polled(623, 2, OntoPlant("Right"), "anfrst_4", 1096, -173, 302, [42, 43], 21),
        Polled(623, 2, "Jump from here: hold Left", "anfrst_4", 1096, -173, 302, [42, 43], null),
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

    private const string Pitcher = "Pitcher plant, throwing spot: press OK to throw";
    private const string PitcherOtherSide = "Pitcher plant, other side, throwing spot: press OK to throw";
    private const string Hollow = "Hollow tree, throwing spot: press OK to throw";
    private const string Flytrap = "Mutant Flytrap, throwing spot: press OK to throw";

    private static string OntoPlant(string key) => $"Jump onto the closed pitcher plant: hold {key} from here";

    // A spot past a line that writes its zone only when crossed, at the walkmesh's own height
    // under it (arrival is measured in three dimensions), offered from the side of
    // `side` (or, with no line, only on the listed triangles, the ones a walk was proven from).
    private static FieldNavigationObjectDefinition Crossed(
        int field, int entity, string label, string fieldName, string entityName, int x, int y, int z,
        FieldNavigationTriggerLine line, int sign, int[]? triangles = null, int[]? excluded = null) =>
        new(field, entity, FieldNavigationObjectKind.Named, Label: label,
            SourceFieldName: fieldName, SourceEntityName: entityName,
            TargetKind: FieldNavigationObjectTargetKind.Location, StaticX: x, StaticY: y, StaticZ: z,
            InteractionRadiusOverride: CrossedSpotRadius, RequiredPlayerTriangles: triangles, ExcludedPlayerTriangles: excluded,
            PlayerSideLine: sign == 0 ? null : line, PlayerSide: sign, CrossingLine: line);

    // Something the leader can do while standing on the listed triangles.
    private static FieldNavigationObjectDefinition Here(
        int field, int entity, string label, string fieldName, int x, int y, int z, int[] triangles) =>
        new(field, entity, FieldNavigationObjectKind.Named, Label: label,
            SourceFieldName: fieldName, SourceEntityName: "ujp0",
            TargetKind: FieldNavigationObjectTargetKind.Location, StaticX: x, StaticY: y, StaticZ: z,
            InteractionRadiusOverride: StandingHereRadius, RequiredPlayerTriangles: triangles);

    // A take-off: a point on the side of the line the jump is made from, 16 units outside the
    // player's collision range of it (46 in these fields), so the walk there never touches the
    // line with a key held and the jump stays the player's. The next plant's byte (temporary
    // block) is the gate where the jump has one.
    private static FieldNavigationObjectDefinition TakeOff(
        int field, int entity, string label, string fieldName, string entityName, int x, int y, int z, int? gate) =>
        Gated(new FieldNavigationObjectDefinition(field, entity, FieldNavigationObjectKind.Named, Label: label,
            SourceFieldName: fieldName, SourceEntityName: entityName,
            TargetKind: FieldNavigationObjectTargetKind.Location, StaticX: x, StaticY: y, StaticZ: z,
            InteractionRadiusOverride: TakeOffRadius), gate);

    private const int TakeOffRadius = 8;

    // A jump ujp0 makes for a key held while the leader stands on one of its triangles.
    private static FieldNavigationObjectDefinition Polled(
        int field, int entity, string label, string fieldName, int x, int y, int z, int[] triangles, int? gate) =>
        Gated(Here(field, entity, label, fieldName, x, y, z, triangles), gate);

    private static FieldNavigationObjectDefinition Gated(FieldNavigationObjectDefinition definition, int? gate) =>
        gate is not { } address
            ? definition
            : definition with { RequiredBank = 5, RequiredAddress = address, RequiredMask = 0xFF, RequiredValue = 1 };

    private const int CrossedSpotRadius = 8;
    private const int StandingHereRadius = 64;

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
