namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Eight in-engine character actions whose wording comes from reviewed footage.
///
/// <para>Each anchor is the instruction that runs at the moment the footage shows, verified
/// on an instruction boundary with identical bytes in both installed archives and on an
/// opcode <c>Steam2026FieldCutsceneCallbackCatalog</c> installs a handler for. Script blocks
/// were bounded by the next offset across every entity in the field; none of these anchors
/// is inside an init continuation, so each is addressable by the entity and script the
/// engine reports.</para>
///
/// <para>The animation opcodes here are timing points. What each action is comes from the
/// footage, not from the animation number.</para>
/// </summary>
public static class FieldMotionActionDescriptions
{
    /// <summary>The eight cues, in field order.</summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateAll() =>
    [
        // 302 sininb1, 'vin' script 4. Byte 23 shows the model, 25 is the leap, 28 the
        // landing. Anchored on the leap.
        new(302, 8, 4, 25,
            "Vincent leaps down into the passage, landing facing the party.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),

        // 303 sininb2, 'vin' script 12. Jump at 44, offset at 47, landing at 60. The
        // message at 29 - 'You know Sephiroth?' - runs first, so the anchor is the landing.
        new(303, 5, 12, 60,
            "The man in a red cloak leaps onto the coffin lid.",
            FieldOpcodeAddressResolver.OpcodeDfanmIndex),

        // 303 sininb2, 'vin' script 15. Byte 0 is the script's first instruction; his next
        // line is at 36.
        new(303, 5, 15, 0,
            "The man lowers himself back into the open coffin.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex),

        // 308 sininb42, 'cef' script 6. Byte 6 starts entity 5 'mtr', the orb, before the
        // throw animation at 9. Byte 70 of this same script already ships Sephiroth's
        // departure; distinct keys, delivered in script order.
        new(308, 4, 6, 6,
            "Sephiroth throws a green orb; Cloud drops to one knee.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),

        // 526 cos_btm2, 'BALLET' script 1. Byte 202 follows the message at 199; the arm
        // spread is the ANIM!1 at 232 and is covered by the same sentence.
        new(526, 7, 1, 202,
            "Beside the bonfire, Barret stands and throws both arms wide.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),

        // 564 rcktin2, 'ycid' script 3. Byte 22 starts crew1; crew2 and crew3 follow at 25
        // and 28 and carry no cue, or the line would be read three times.
        new(564, 8, 3, 22,
            "Three blue-uniformed technicians salute Cid as he walks past.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),

        // 612 kuro_82, 'ketcy' script 12: VISI 18 shows him, DFANM 24 sets the walking
        // pose, FMOVE 27 carries him, AKAO 33 plays the sound, ANIM!1 47 is the fall.
        // Script 13 is the recovery - it opens with his 'Owwww...' message and its ANIM!2
        // at 17 is him getting back up - so the fall is not anchored there.
        new(612, 16, 12, 47,
            "Cait Sith's white mount tips sideways and tumbles to the floor.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex),

        // 613 kuro_9, 'ketcy' script 10: VISI 18 shows him, DFANM 20 sets the walking pose,
        // FMOVE 23 reaches the altar, DFANM 29 returns to idle, ANIME1 36 is the forward
        // fall. His 'This must be it!' at 53 is spoken while he is down.
        new(613, 13, 10, 36,
            "Beside the altar, Cait Sith falls forward and stays down.",
            FieldOpcodeAddressResolver.OpcodeAnime1Index)
    ];
}
