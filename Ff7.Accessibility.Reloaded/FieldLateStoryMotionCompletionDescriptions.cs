namespace Ff7.Accessibility.Reloaded;

/// <summary>Seven substantive late actions from a bounded native animation review.</summary>
/// <remarks>
/// Evidence: audio-audit/late-story-motion-final-evidence.json, the rendered native
/// HRC/P/A contact sheets and exact played script ranges. All five Echo ModelLoader
/// bindings match these resources. Descriptions report body motion only. They require
/// a coherent Visible reading for the actual loaded entity when the callback executes;
/// the inspection camera does not establish live gameplay framing or occlusion.
/// </remarks>
public static class FieldLateStoryMotionCompletionDescriptions
{
    private static readonly HashSet<FieldCutsceneDescriptionKey> visibleActors =
        CreateAll().Select(cue => cue.Key).ToHashSet();

    public static bool RequiresVisibleActor(FieldCutsceneDescriptionKey key) => visibleActors.Contains(key);

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateAll() =>
    [
        // itown_w: CHAR1/AAGB, EXFE38 frames. BA blocks through the collapse,
        // then WAIT60 at10 holds the final low pose. No inferred medical/emotional cause.
        new(717, 2, 5, 7,
            "Tifa drops to her knees and bends low.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),

        // EWHE160 frames: rises from the preceding collapse into standing.
        new(717, 2, 5, 17,
            "Tifa gets back to her feet.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),

        // trnad_52: CHAR0/AAAA, HSHB60 frames. ANIME1 blocks before
        // DFANM7 holds the curled HSHC pose and MESSAGE20 waits. No camera/upside-down claim.
        new(707, 3, 3, 0,
            "Cloud draws his knees toward his chest.",
            FieldOpcodeAddressResolver.OpcodeAnime1Index),

        // CHAR1/AAGB: AJFD30 (kneeling head bow), then AJFE60 (gets up).
        // Both ANIME1 instructions block. One sentence covers both, with no cue at3.
        new(707, 4, 3, 0,
            "Tifa bows her head, then gets to her feet.",
            FieldOpcodeAddressResolver.OpcodeAnime1Index),

        // woa_1: HQEA15 falls back; WAIT20, CUEA30 rolls; WAIT20,
        // CUEB20 gets up. Blocking native animations keep these full played segments visible.
        new(709, 10, 3, 21,
            "Cloud falls backward, rolls, and gets to his feet.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),

        // las4_0: BICA30 lifts both hands to the head; CANM2 BICC0..24
        // pitches the whole body forward. Later BICD holds that posture. No pain inferred.
        new(763, 2, 17, 4,
            "Cloud clutches his head and pitches forward.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),

        // lastmap: HZED68 rises from a low, horizontal pose. AF is nonblocking,
        // followed by the three party-dependent MESSAGE branches before the next action.
        // AD3/script3 requests this actor at139, far before its movie at1091.
        new(768, 5, 3, 0,
            "Cloud gets to his feet.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex)
    ];
}
