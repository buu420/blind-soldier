namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Late story actions reviewed by rendering the installed native HRC/P geometry and A-file
/// skeletal motion, then matching the field's CHAR and ModelLoader animation bindings.
/// These descriptions report body/weapon motion only: no dialogue, emotions, inferred
/// animation names, facial expressions or off-screen party membership.
/// </summary>
/// <remarks>
/// Evidence: audio-audit/motion-models/reviews and late-game-motion-evidence.json from the
/// 2026-10-12 coverage audit. The review camera is fixed for examining native motion; it
/// does not establish live camera framing or occlusion. Every cue is on an executed
/// non-init animation instruction, using a callback delivered by both runtimes. Repeated
/// idle or neighbouring copies of the same gesture deliberately have no second cue.
/// </remarks>
public static class FieldLateGameMotionActionDescriptions
{
    private static readonly HashSet<FieldCutsceneDescriptionKey> visibleActors =
        CreateAll().Select(cue => cue.Key).ToHashSet();

    /// <summary>Require a coherent Visible reading for the actual loaded actor before delivery.</summary>
    public static bool RequiresVisibleActor(FieldCutsceneDescriptionKey key) => visibleActors.Contains(key);

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateAll() =>
    [
        // canon_2: CHAR 8 = ANBD.HRC. ANIM!2 5 runs HYAB.A, thirty
        // frames turning from the front to the back. CHAR defaults to visible;
        // the conditional VISI00 in init hides him on other scene branches.
        new(741, 13, 1, 152,
            "Hojo turns around.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),

        // HYAC.A: head tilts back and both hands lift. Other occurrences at329/425
        // depict the same gesture; this later occurrence is the only authored boundary.
        new(741, 13, 1, 504,
            "Hojo throws his head back and lifts both hands.",
            FieldOpcodeAddressResolver.OpcodeDfanmIndex),

        // HYAD.A: alternating upward arm swings, not an inferred emotion or mutation.
        new(741, 13, 1, 628,
            "Hojo waves both arms above his head.",
            FieldOpcodeAddressResolver.OpcodeDfanmIndex),

        // las4_0: Cloud CHAR0 = AAAA.HRC. CANM1 11 explicitly plays HYCE.A
        // frames0..19. These frames lift a hand near the face, not the later lowering.
        new(763, 2, 10, 47,
            "Cloud raises a hand toward his face.",
            FieldOpcodeAddressResolver.OpcodeCanm1Index),

        // ANIM!1 13 = HYCF.A. Hands spread at waist height; no shoulder-height claim.
        new(763, 2, 13, 26,
            "Cloud spreads both arms.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex),

        // CANM1 15 = AJDC.A frames0..7: head lowers during these eight frames.
        new(763, 2, 15, 0,
            "Cloud lowers his head.",
            FieldOpcodeAddressResolver.OpcodeCanm1Index),

        // las4_4: Cloud CHAR0 = BHFF.HRC, which already holds the sword.
        // EVEF.A lifts the blade upright and returns it across the torso; no drawing cue.
        new(767, 3, 6, 0,
            "Cloud lifts his broad sword upright, then lowers it across his body.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex),

        // ETAA.A raises the sword and spreads his feet into the final held stance.
        new(767, 3, 6, 19,
            "Cloud raises his sword and settles into a wide stance.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex)
    ];
}
