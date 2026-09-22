namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The optional things in present-day Nibelheim's Shinra Mansion.
///
    /// <para>Most of these are native <c>LINE</c> handlers with no model, so
/// neither the NPC reader - which needs a model id, an event record and a visibility byte -
    /// nor the extracted chest and materia rows can see them. The opened chest also remains
    /// interactive after its treasure is collected, because its lid carries writing.
    /// That is why a session log reports
/// <c>npcs=0</c> in every mansion field while a sighted player can act on all of these.</para>
///
/// <para>Each row carries its entity's own <c>LINE</c> midpoint. Every one of those handlers
/// is a <c>Go</c> or an <c>[OK]</c>, both of which need the party inside the native player
/// radius at <c>event+0x72</c> rather than within a generic arrival distance, so all of them
/// set <c>UsesPlayerCollisionRadius</c>; the Objects reader also gates each on the live
/// <c>LINON</c> state of that entity. Nothing here names a hint, a dial or a reward.</para>
///
/// <para>Field 308 is deliberately absent. Its director's own Main runs the whole scene
/// automatically on entry while <c>Bank[1][231]</c> bit 1 is clear and sets that bit at byte
/// 92, so there is nothing to walk to and no prompt to press; the way in is the ordinary
/// gateway out of 305, which the Exits category already lists.</para>
/// </summary>
public static class NibelheimObjectCatalog
{
    /// <summary>
    /// sinin1_1 e6 'let' Init: <c>IFSW Bank[2][0] &lt; 385 -&gt; hide</c>. So the note is there
    /// from 385 onward, with no upper bound of any kind in the script.
    /// </summary>
    private const int NoteAppears = 385;

    /// <summary>
    /// What sinin1_2 e14 'hint0', sinin2_2 e9 'hint2' and sinin2_1 e9 'lin1' each test:
    /// <c>IFSW Bank[2][0] &gt; 385</c>. The rest of the set carries no GameMoment test at all,
    /// and takes the same lower bound to keep the flashback's own scripted visits clear -
    /// that part is a deliberate exclusion, not a native gate.
    /// </summary>
    private const int PresentDayBegins = 386;

    private static readonly IReadOnlyList<FieldNavigationObjectDefinition> Definitions =
    [
        // sinin1_1 e6 'let'. The one model of the set: CHAR 4 at (-550,89,0), left visible
        // and talkable by its own Init once the GameMoment is past 385.
        new(
            297,
            6,
            FieldNavigationObjectKind.Named,
            Label: "Note in the mansion entrance hall",
            SourceFieldName: "sinin1_1",
            SourceEntityName: "let",
            TargetKind: FieldNavigationObjectTargetKind.Model,
            MinimumGameMoment: NoteAppears,
            UsesTalkInteraction: true),

        // sinin1_2 e10 'plin0'. An [OK] handler: the party has to be on the line and press
        // Confirm, and the keys after that are ordinary game input.
        new(
            298,
            10,
            FieldNavigationObjectKind.Named,
            Label: "Piano in the mansion side room",
            SourceFieldName: "sinin1_2",
            SourceEntityName: "plin0",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: -673,
            StaticY: 608,
            StaticZ: 0,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),

        // sinin1_2 e14 'hint0'. Its Init declares the LINE only once the moment is past 385.
        new(
            298,
            14,
            FieldNavigationObjectKind.Named,
            Label: "Writing on the mansion side room floor",
            SourceFieldName: "sinin1_2",
            SourceEntityName: "hint0",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: -486,
            StaticY: 527,
            StaticZ: 0,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),

        // sinin2_1 e9 'lin1'. A Go handler behind IFSW moment > 385. Bank[1][232] bit 0 is
        // the safe opened and bit 1 is it emptied; the row goes once bit 1 is set.
        new(
            299,
            9,
            FieldNavigationObjectKind.Named,
            Label: "Safe in the mansion upstairs room",
            CollectedBank: 1,
            CollectedAddress: 232,
            CollectedMask: 2,
            SourceFieldName: "sinin2_1",
            SourceEntityName: "lin1",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: -663,
            StaticY: 1236,
            StaticZ: 452,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),

        // sinin2_1 e7 'box0' Talk branches to MESSAGE 178 after Bank[15][35] bit 3
        // (Enemy Launcher collected). Keep that readable chest after the item row disappears.
        new(
            299,
            7,
            FieldNavigationObjectKind.Named,
            Label: "Opened chest in the mansion upstairs room",
            RequiredBank: 15,
            RequiredAddress: 35,
            RequiredMask: 8,
            RequiredValue: 8,
            SourceFieldName: "sinin2_1",
            SourceEntityName: "box0",
            TargetKind: FieldNavigationObjectTargetKind.Model,
            MinimumGameMoment: PresentDayBegins,
            UsesTalkInteraction: true),

        // sinin2_2 e7 'lin0' Talk unlocks triangles 143, 67 and 120 and opens the
        // panel. A route directly to gateway 1 cannot cross those locked triangles yet.
        new(
            300,
            7,
            FieldNavigationObjectKind.Named,
            Label: "Secret passage door in the mansion upstairs room",
            RequiredBank: 5,
            RequiredAddress: 7,
            RequiredMask: 255,
            RequiredValue: 0,
            SourceFieldName: "sinin2_2",
            SourceEntityName: "lin0",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: 906,
            StaticY: 623,
            StaticZ: 339,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),

        // sinin2_2 e8 'fl0' Go: the search spot described by the note's third hint.
        // It runs the native search scene; the separate hint2 row reads its writing.
        new(
            300,
            8,
            FieldNavigationObjectKind.Named,
            Label: "Floor beside the secret passage",
            SourceFieldName: "sinin2_2",
            SourceEntityName: "fl0",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: 814,
            StaticY: 328,
            StaticZ: 339,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),

        // sinin2_2 e9 'hint2'. Same shape as hint0, in the other upstairs room.
        new(
            300,
            9,
            FieldNavigationObjectKind.Named,
            Label: "Writing on the mansion upstairs floor",
            SourceFieldName: "sinin2_2",
            SourceEntityName: "hint2",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: 488,
            StaticY: 1105,
            StaticZ: 339,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),

        // sininb2 e15 'lin1'. Bank[13][80] bit 2 clear is the native gate on the Go doing
        // anything; with it set the handler falls through to a refusal line.
        new(
            303,
            15,
            FieldNavigationObjectKind.Named,
            Label: "Coffin in the mansion basement",
            RequiredBank: 13,
            RequiredAddress: 80,
            RequiredMask: 4,
            RequiredValue: 0,
            SourceFieldName: "sininb2",
            SourceEntityName: "lin1",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: 664,
            StaticY: -934,
            StaticZ: 22,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),

        // sininb32 e11 and e12. The two specimen tanks in the present-day basement library.
        new(
            305,
            11,
            FieldNavigationObjectKind.Named,
            Label: "Left specimen tank in the basement library",
            SourceFieldName: "sininb32",
            SourceEntityName: "lefp0",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: 116,
            StaticY: -529,
            StaticZ: 0,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),
        new(
            305,
            12,
            FieldNavigationObjectKind.Named,
            Label: "Right specimen tank in the basement library",
            SourceFieldName: "sininb32",
            SourceEntityName: "rtp0",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: 261,
            StaticY: -451,
            StaticZ: 0,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),

        // sininb52 e4 to e7. Four separate documents in the innermost basement room. Its own
        // MPNAM is "Mansion, Basement"; there is no coffin in this field.
        new(
            310,
            4,
            FieldNavigationObjectKind.Named,
            Label: "First report in the innermost basement room",
            SourceFieldName: "sininb52",
            SourceEntityName: "ln0",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: -178,
            StaticY: 1112,
            StaticZ: 0,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),
        new(
            310,
            5,
            FieldNavigationObjectKind.Named,
            Label: "Second report in the innermost basement room",
            SourceFieldName: "sininb52",
            SourceEntityName: "ln1",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: -144,
            StaticY: 1280,
            StaticZ: 0,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),
        new(
            310,
            6,
            FieldNavigationObjectKind.Named,
            Label: "Third report in the innermost basement room",
            SourceFieldName: "sininb52",
            SourceEntityName: "ln2",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: -29,
            StaticY: 1340,
            StaticZ: 0,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),
        new(
            310,
            7,
            FieldNavigationObjectKind.Named,
            Label: "Fourth report in the innermost basement room",
            SourceFieldName: "sininb52",
            SourceEntityName: "ln3",
            TargetKind: FieldNavigationObjectTargetKind.Line,
            StaticX: 300,
            StaticY: 1036,
            StaticZ: 0,
            MinimumGameMoment: PresentDayBegins,
            UsesPlayerCollisionRadius: true),
    ];

    public static IReadOnlyList<FieldNavigationObjectDefinition> Create() => Definitions;
}
