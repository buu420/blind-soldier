namespace Ff7.Accessibility.Reloaded;

/// <summary>Four actions reviewed directly from installed Tifa skeleton and native animation frames.</summary>
/// <remarks>
/// Evidence: audio-audit/lifestream-motion-evidence.json and motion-models/reviews/AAGB-*.png.
/// No dialogue or emotion is inferred from the animation. The fixed inspection camera is
/// not gameplay framing. Callers MUST require a coherent native Visible reading for the
/// actual loaded actor entity before delivery: a separate memory branch hides Tifa.
/// ModelLoader slots below identify resources, not runtime model-array positions.
/// </remarks>
public static class FieldLifestreamMotionActionDescriptions
{
    private static readonly HashSet<FieldCutsceneDescriptionKey> visibleActors =
        CreateAll().Select(cue => cue.Key).ToHashSet();

    public static bool RequiresVisibleActor(FieldCutsceneDescriptionKey key) => visibleActors.Contains(key);

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateAll() =>
    [
        // life: director0/script0 requests Tifa script3 at160. CHAR1 binds
        // AAGB.HRC; DFANM3 plays HUJA.tak's 240-frame end-over-end rotation.
        new(723, 2, 3, 4,
            "Tifa turns slowly, head over heels.",
            FieldOpcodeAddressResolver.OpcodeDfanmIndex),

        // zmind3: Cloud1/script1 requests Tifa9 at13. CANM2 anim12 runs
        // AHDA.tak frames0..14, standing to kneeling; the later held pose is silent.
        new(727, 6, 9, 0,
            "Tifa kneels.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index),

        // The same conversation requests Tifa10 at103. HTIE.tak starts
        // already kneeling and lowers her head, then returns it upright.
        new(727, 6, 10, 0,
            "Tifa bows her head while kneeling.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex),

        // Tifa11 is requested at122. HPAD.yos rises from her knees over90
        // frames; no inferred relief, expression, or interaction with an unseen person.
        new(727, 6, 11, 4,
            "Tifa rises to her feet.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex)
    ];
}
