namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The 2026-09-21 continuation batch: the rest of the journey's arrival descriptions, and
/// six scripted moments.
///
/// <para>Same rules as every batch before it. An area description runs from the field's own
/// MPNAM, which each field executes exactly once from its director entity on entry, so it is
/// spoken once per arrival and never repeats while the player is in the room. An action is
/// anchored on the native instruction that performs it, and nothing here describes a gesture,
/// an expression or a movement the script does not actually carry out. One anchor - the
/// Temple clock room, and only that one - sits on an animation callback used solely as a
/// clock, because the instruction that performs the action falls in the middle of a fade;
/// the reason is set out on <see cref="CreateActionDescriptions"/> and the number itself is
/// still read as meaning nothing.</para>
///
/// <para>Every anchor below was read from both installed archives - the 1998 release and the
/// 2026 Steam edition - and is byte-identical in each. The texts are exactly what was
/// approved after the native backgrounds were reviewed; they are not edited here.</para>
/// </summary>
public static class FieldCutsceneContinuationDescriptions
{
    /// <summary>208 arrival descriptions, one per field, each on that field's own MPNAM.</summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateAreaDescriptions() =>
    [
        // 646 ancnt1, ancnt1.
        new(646, 0, 0, 3,
            "Pale towers with dark pointed roofs rise above a misty circular space, "
            + "linked by curved walkways and separate stone pillars.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 649 ancnt4, ancnt4.
        new(649, 0, 0, 13,
            "A narrow curved stone bridge climbs steps to a round raised platform with a "
            + "curved rim, pale mist lying below.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 620 anfrst_1, anfrst_1.
        new(620, 0, 0, 62,
            "Thick tree trunks line a mossy path beside a rock face, with red star-shaped "
            + "plants low down and orange cupped plants hanging overhead.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 621 anfrst_2, anfrst_2.
        new(621, 0, 0, 14,
            "Interlaced, moss-covered branches form a high trail through dense green "
            + "foliage.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 622 anfrst_3, anfrst_3.
        new(622, 0, 0, 38,
            "Mossy rock ledges and crooked tree roots surround a small dark pool, with "
            + "red plants scattered across the forest floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 640 blue_1, blue_1.
        new(640, 0, 0, 14,
            "Pale branching formations rise from blue-lit rocky ledges around a deep, "
            + "narrow gap.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 641 blue_2, blue_2.
        new(641, 0, 0, 26,
            "An enormous skull and curved ribs lie among pale tree-like formations under "
            + "shafts of blue-white light.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 617 bonevil, bonevil.
        new(617, 0, 0, 12,
            "A huge skull crowns a rocky terrace above canvas shelters. Giant ribs curve "
            + "across the lower ground, with a ladder joining the levels.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 592 datiao_1, datiao_1.
        new(592, 0, 0, 0,
            "Colossal stone figures are carved into the cliffs, with narrow paths winding "
            + "around their heads and outstretched arms.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 593 datiao_2, datiao_2.
        new(593, 0, 0, 0,
            "A cliff path curls past an enormous crowned stone face and layers of carved "
            + "hands.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 594 datiao_3, datiao_3.
        new(594, 0, 0, 0,
            "A narrow ledge curves along a carved figure's chest, above an open stone "
            + "hand and a sheer cliff.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 595 datiao_4, datiao_4.
        new(595, 0, 0, 0,
            "A carved face fills the cliffside above a thin ledge, with a massive stone "
            + "arm stretching overhead.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 596 datiao_5, datiao_5.
        new(596, 0, 0, 0,
            "Steep stairs zigzag between monumental stone carvings up the cliff wall.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 598 datiao_7, datiao_7.
        new(598, 0, 0, 0,
            "A narrow ridge circles a monumental stone hand with two fingers raised, "
            + "forest stretching far below.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 599 datiao_8, datiao_8.
        new(599, 10, 0, 0,
            "A dark rocky passage curls around two bright orange pools, their glow "
            + "casting warm light on the surrounding rock.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 600 jtempl, jtempl.
        new(600, 3, 0, 0,
            "Stone walls and a heavy archway stand in a shadowy forest clearing, with "
            + "steps climbing toward the archway.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 601 jtemplb, jtemplb.
        new(601, 0, 0, 0,
            "Square stone walls surround a deep rectangular pit with a rough, pale "
            + "interior and a blue-black bottom.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 602 jtmpin1, jtmpin1.
        new(602, 2, 0, 2,
            "An enormous golden, fanged face hangs above a stone altar in an orange-lit "
            + "chamber flanked by tall columns and standing braziers.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 604 kuro_1, kuro_1.
        new(604, 1, 0, 56,
            "Stone staircases and narrow platforms interlock at awkward angles across a "
            + "sprawling, multilevel maze.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 614 kuro_10, kuro_10.
        new(614, 0, 0, 0,
            "A low, arched passage runs between rough stone walls into darkness.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 616 kuro_12, kuro_12.
        new(616, 3, 0, 0,
            "Steps rise to an ornate doorway at the far end of a bare, dim stone chamber.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 605 kuro_2, kuro_2.
        new(605, 3, 0, 0,
            "Shelves line the walls of a cramped, amber-lit stone room, with pots sitting "
            + "among thick columns.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 606 kuro_3, kuro_3.
        new(606, 4, 0, 0,
            "A long, arched stone bridge leads past a circular blue pool ringed by "
            + "columns.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 607 kuro_4, kuro_4.
        new(607, 0, 0, 32,
            "Numbered doorways ring a huge circular stone chamber around a deep central "
            + "shaft.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 609 kuro_6, kuro_6.
        new(609, 2, 0, 0,
            "Short stairs rise to a stone platform between two black braziers, warm "
            + "orange light falling across rough cave walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 611 kuro_8, kuro_8.
        new(611, 2, 0, 56,
            "Tall columns separate expansive wall paintings in warm gold and orange, "
            + "showing rows of figures and a large circular sun-like symbol.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 632 losin2, losin2.
        new(632, 2, 0, 0,
            "Wooden platforms fill a tall shell-shaped building, with a ladder connecting "
            + "the levels beside branching red coral.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 636 losinn, losinn.
        new(636, 2, 0, 0,
            "Beds line the curved walls of a shell-shaped room, where a twisting central "
            + "column rises through wooden floors connected by steps.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 638 loslake2, loslake2.
        new(638, 0, 0, 50,
            "A circular stone walkway surrounds a glowing blue center, with steps and "
            + "tall columns rising among curved shell-like structures.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 630 lost1, lost1.
        new(630, 1, 0, 0,
            "A stone path crosses water toward a towering cluster of pale branches, "
            + "surrounded by layered shell-like platforms.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 634 lost2, lost2.
        new(634, 0, 0, 0,
            "Pale stone paths weave among shell-shaped houses beneath layered green "
            + "canopies, with tall brown cliffs rising in the background.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 635 lost3, lost3.
        new(635, 1, 0, 0,
            "A narrow pale stone walkway curves along a deep rocky gap between tall brown "
            + "cliffs, leading toward a doorway.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 317 nvdun1, nvdun1.
        new(317, 0, 0, 17,
            "Large rusted pipes bend through a tall green cavern, while catwalks and "
            + "ladders link platforms above the rocky floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 319 nvdun3, nvdun3.
        new(319, 0, 0, 67,
            "Thick twisting brown roots frame a narrow passage leading toward pale "
            + "blue-green rock and light.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 322 nvmkin1, nvmkin1.
        new(322, 0, 0, 54,
            "Metal catwalks bridge a deep industrial shaft lined with hanging cables and "
            + "pipes. Yellow railings border the walkways, with white light glowing below.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 323 nvmkin21, nvmkin21.
        new(323, 0, 0, 68,
            "Rows of tall rounded pods with blue-lit windows flank a central staircase, "
            + "while thick red pipes arch overhead in the red-lit chamber.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 324 nvmkin22, nvmkin22.
        new(324, 0, 0, 8,
            "Rows of tall rounded pods with blue-lit windows flank a central staircase, "
            + "while thick red pipes arch overhead in the red-lit chamber.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 326 nvmkin31, nvmkin31.
        new(326, 0, 0, 32,
            "A winged human-shaped ornament hangs high above a curved red tube. Large "
            + "cylindrical vessels surround a round platform in the dark chamber.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 557 rckt, rckt.
        new(557, 6, 0, 14,
            "A tall leaning rocket rises above a town of low houses, with trees flanking "
            + "the broad central street.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 551 rckt2, rckt2.
        new(551, 7, 0, 2,
            "An empty metal gantry rises above a town of low houses, with trees flanking "
            + "the broad central street.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 561 rcktbas1, rcktbas1.
        new(561, 2, 0, 24,
            "Cylindrical boosters crowd the foot of a large rocket, with metal stairs, "
            + "scaffold platforms, and thick pipes standing above the grass.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 562 rcktbas2, rcktbas2.
        new(562, 4, 0, 24,
            "The rocket's tall metal body rises through an open framework of narrow "
            + "stairways and platforms, high above the trees and grass.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 563 rcktin1, rcktin1.
        new(563, 0, 0, 12,
            "A narrow metal passage is lined with pipes, cables, bright floor lights, and "
            + "black-and-yellow warning stripes.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 564 rcktin2, rcktin2.
        new(564, 1, 0, 68,
            "Pipes run along the ceiling and walls of a narrow metal passage, leading to "
            + "a dark square doorway with steps at the near end.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 565 rcktin3, rcktin3.
        new(565, 1, 0, 12,
            "A brightly lit metal corridor leads to a circular hatch, with pipes and "
            + "angled fittings lining both sides.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 568 rcktin6, rcktin6.
        new(568, 3, 0, 40,
            "A grated metal walkway leads to a ladder between cramped walls covered with "
            + "equipment panels and pipes.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 570 rcktin8, rcktin8.
        new(570, 0, 0, 12,
            "A narrow metal ladder shaft descends past bright strip lights, with "
            + "black-and-yellow warning stripes around its edges.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 555 rktinn1, rktinn1.
        new(555, 3, 0, 14,
            "A wooden reception counter and a separate bar occupy a warmly lit room, with "
            + "round tables standing nearby.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 556 rktinn2, rktinn2.
        new(556, 4, 0, 14,
            "Two pairs of beds occupy wooden guest rooms, each with small round tables "
            + "and narrow washrooms beside them.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 554 rkt_i, rkt_i.
        new(554, 5, 0, 14,
            "A long shop counter crosses the wooden front room, with bedrooms and a small "
            + "washroom behind it.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 553 rkt_w, rkt_w.
        new(553, 5, 0, 14,
            "A shop counter runs beside wooden floorboards, with framed displays behind "
            + "it, while a bedroom and small washroom occupy the rear rooms.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 629 sandun_2, sandun_2.
        new(629, 0, 0, 4,
            "A rough gray cave slopes toward a small dark opening in the far wall.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 626 sango2, sango2.
        new(626, 0, 0, 0,
            "A path runs along a huge curved hollow shell, passing pointed rock "
            + "formations and pale branching growths.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 627 sango3, sango3.
        new(627, 0, 0, 4,
            "A towering spiral shell studded with long sharp spines rises in a rocky "
            + "cleft, surrounded by colorful coral-like growths.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 618 slfrst_1, slfrst_1.
        new(618, 0, 0, 0,
            "A narrow earthy path winds between tall trees, disappearing into the dense "
            + "green forest.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 586 tower5, tower5.
        new(586, 4, 0, 0,
            "Red-framed upper windows overlook a wooden hall, where a large circular "
            + "emblem fills the center of the floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 580 utapb, utapb.
        new(580, 0, 0, 0,
            "A brightly decorated tavern with red walls features wooden tables, green "
            + "stools, and a long counter, with a tree growing from a stone planter inside.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 576 uta_im, uta_im.
        new(576, 0, 0, 0,
            "A tiled shop awning hangs above a long counter, while boxes, jars, bundled "
            + "poles, and hanging papers crowd the wooden room.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 577 utmin1, utmin1.
        new(577, 0, 0, 0,
            "Cabinets, chests, hanging papers, and small ornaments fill a cramped wooden "
            + "house with a low ceiling.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 578 utmin2, utmin2.
        new(578, 3, 0, 0,
            "A round low table stands among floor cushions in a wooden room, with a thick "
            + "braided rope hanging above a curtained alcove.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 588 uttmpin1, uttmpin1.
        new(588, 4, 0, 0,
            "A narrow garden with stepping stones and bamboo runs beside a red wooden "
            + "building, its inner room covered in patterned mats.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 589 uttmpin2, uttmpin2.
        new(589, 3, 0, 0,
            "A narrow stone garden borders a red wooden house, with patterned floor mats, "
            + "wall hangings, and shelves of books inside.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 590 uttmpin3, uttmpin3.
        new(590, 4, 0, 0,
            "A golden many-armed statue stands at the far end of a wooden hall, beyond a "
            + "large circular emblem painted on the floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 591 uttmpin4, uttmpin4.
        new(591, 2, 0, 0,
            "Floor cushions line a long wooden room beneath hanging red decorations, with "
            + "two large patterned rugs occupying the center.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 579 uutai1, uutai1.
        new(579, 9, 0, 0,
            "Red arched bridges cross winding streams between buildings with dark curved "
            + "roofs, framed by trees and steep cliffs.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 587 uutai2, uutai2.
        new(587, 1, 0, 0,
            "A many-tiered red pagoda rises beyond a broad stone courtyard, flanked by "
            + "red wooden pavilions.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 581 yufy1, yufy1.
        new(581, 0, 0, 0,
            "A folding screen separates a bed from a wooden room containing low tables, "
            + "cushions, shelves, and red-framed windows.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 582 yufy2, yufy2.
        new(582, 0, 0, 0,
            "Small pale statues line both sides of a wooden hall, with a purple rug "
            + "bearing a large dark symbol lying in the middle.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 740 canon_1, canon_1.
        new(740, 0, 0, 0,
            "Metal stairs zigzag through a tall framework of girders, pipes, and grated "
            + "platforms.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 741 canon_2, canon_2.
        new(741, 4, 0, 0,
            "Broad metal platforms sit high on an industrial framework, with railings, "
            + "thick red pipes, and smaller pipes crossing their surfaces.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 686 gaiafoot, gaiafoot.
        new(686, 3, 0, 0,
            "A dark wooden cabin with a steep roof stands beside a snowy trail at the "
            + "foot of high white mountains.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 689 gaia_1, gaia_1.
        new(689, 1, 0, 0,
            "Small snow-covered ledges rise up a steep blue-white cliff face, with dark "
            + "hollows between the rocks.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 692 gaia_2, gaia_2.
        new(692, 1, 0, 0,
            "A steep icy cliff is broken by small ledges and dark rock outcrops.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 694 gaia_31, gaia_31.
        new(694, 1, 0, 0,
            "Staggered snowy shelves climb an icy cliff toward a small dark opening.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 695 gaia_32, gaia_32.
        new(695, 0, 0, 0,
            "A dark cave mouth opens near the foot of a broad ice-covered wall beneath a "
            + "deep blue sky.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 690 gaiin_1, gaiin_1.
        new(690, 1, 0, 0,
            "Blue crystal walls surround several rocky ledges and dark tunnel openings "
            + "inside a tall cavern.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 696 gaiin_4, gaiin_4.
        new(696, 1, 0, 0,
            "Tall narrow rock spires rise from a deep blue icy gorge, with snow-covered "
            + "ledges along both walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 697 gaiin_5, gaiin_5.
        new(697, 1, 0, 0,
            "Blue and violet crystals glow around a deep shaft, with narrow ledges "
            + "running past dark cave openings on opposite sides.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 687 holu_1, holu_1.
        new(687, 1, 0, 0,
            "A pale animal-skin rug covers the center of a wooden cabin. A bed, work "
            + "table, bottles, and stacked supplies line the walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 688 holu_2, holu_2.
        new(688, 2, 0, 0,
            "Wooden stairs climb to a loft beneath steep rafters. Pale bedding lies on "
            + "both levels, with daylight entering through a small window.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 658 hyou1, hyou1.
        new(658, 0, 0, 0,
            "A wooden sign headed Ice Gate stands beside a snow-covered path, with white "
            + "hills and a line of evergreen trees beyond.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 660 hyou3, hyou3.
        new(660, 0, 0, 0,
            "A broad snowy path passes between low rocky ridges beneath distant white "
            + "mountains.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 664 hyou5_1, hyou5_1.
        new(664, 0, 0, 0,
            "Snow-laden evergreen trees stand beside the pale shore of a partly frozen "
            + "lake.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 667 hyou5_4, hyou5_4.
        new(667, 0, 0, 0,
            "A small snow-covered rocky island rises from pale green water, with a dark "
            + "cave mouth opening at its base.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 668 hyou6, hyou6.
        new(668, 0, 0, 0,
            "A fallen tree trunk spans a narrow snowy ravine between rocky banks.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 676 hyou7, hyou7.
        new(676, 0, 0, 0,
            "A solitary snow-covered evergreen stands in an open white slope.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 677 hyou8_1, hyou8_1.
        new(677, 0, 0, 0,
            "Rounded pillars and ridges of ice crowd a snowy hollow, with winding "
            + "passages between them.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 679 hyou9, hyou9.
        new(679, 0, 0, 0,
            "Snowy paths branch between exposed brown rock ridges.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 680 hyou10, hyou10.
        new(680, 0, 0, 0,
            "A snowy ridge winds above misty drops on both sides.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 681 hyou11, hyou11.
        new(681, 0, 0, 0,
            "A snow-covered valley runs among brown cliffs and low ledges beneath distant "
            + "mountains.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 682 hyou12, hyou12.
        new(682, 0, 0, 0,
            "A small tent stands on the icy floor of a dark cave, with a tunnel opening "
            + "in the back wall.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 683 hyou13_1, hyou13_1.
        new(683, 0, 0, 0,
            "A narrow snow-covered shelf curves around a towering cliff, with jagged "
            + "white peaks in the distance.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 662 icedun_2, icedun_2.
        new(662, 0, 0, 0,
            "Long icicles hang above an icy shaft crossed by huge curved, rib-like "
            + "formations.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 720 ithos, ithos.
        new(720, 0, 0, 0,
            "An examination table and shelves of medical equipment occupy one side of a "
            + "wooden clinic. Curtains divide three beds on the other side.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 721 itmin1, itmin1.
        new(721, 0, 0, 0,
            "A small wooden home contains a central table on a round rug, a curtained "
            + "bed, shelves, and a stove.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 722 itmin2, itmin2.
        new(722, 0, 0, 0,
            "A bed, a stove with pots, and a round dining table fill a worn wooden room.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 712 itown1a, itown1a.
        new(712, 0, 0, 0,
            "Wooden buildings stand on raised walkways in a lush forest. Ramps and stairs "
            + "join the different levels.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 713 itown12, itown12.
        new(713, 0, 0, 0,
            "Wooden buildings stand on raised walkways in a lush forest. Ramps and stairs "
            + "join the different levels.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 714 itown1b, itown1b.
        new(714, 0, 0, 0,
            "Broken wooden walkways hang over a broad green-glowing crater, with "
            + "surviving forest and buildings around the rim.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 716 ithill, ithill.
        new(716, 0, 0, 0,
            "Splintered beams and broken wooden platforms crisscross a deep green-glowing "
            + "hollow.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 718 itown_i, itown_i.
        new(718, 0, 0, 0,
            "Jars and bottles cover a wooden shop counter, with a tall clock and crowded "
            + "shelves against the back wall.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 719 itown_m, itown_m.
        new(719, 0, 0, 0,
            "Metal shields and other equipment hang on a small shop's walls above a "
            + "wooden counter, with a leafy plant beside the counter.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 717 itown_w, itown_w.
        new(717, 0, 0, 0,
            "Weapons and armor fill a wooden shop, including a large dark cannon, a "
            + "target on the wall, and a small desk by the entrance.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 651 sninn_1, sninn_1.
        new(651, 1, 0, 0,
            "A broad staircase divides a warmly lit wooden inn lobby, with a counter, "
            + "patterned rugs, and lamps around the room.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 655 snmin1, snmin1.
        new(655, 0, 0, 0,
            "Two beds with curtains sit behind a dining table on a patterned rug in a "
            + "warmly lit wooden home.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 656 snmin2, snmin2.
        new(656, 1, 0, 0,
            "A split wooden room contains rows of small plants, a metal stove, shelves, "
            + "and hanging lamps.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 650 snw_w, snw_w.
        new(650, 1, 0, 0,
            "An L-shaped wooden counter and a glass-topped display case stand in a "
            + "stone-walled shop, with a staircase rising at the back.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 657 snmayor, snmayor.
        new(657, 14, 0, 0,
            "Banks of equipment and monitors fill the upper part of a dim room, with a "
            + "bed and patterned rug on the lower wooden floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 745 las0_2, las0_2.
        new(745, 1, 0, 2,
            "A long rope ladder hangs from the airship beside a steep rocky drop.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 746 las0_3, las0_3.
        new(746, 0, 0, 0,
            "A rough stony slope stretches beneath the airship's dark underside.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 747 las0_4, las0_4.
        new(747, 6, 0, 4,
            "A rocky shaft curves around a bright green glow deep below, with a small "
            + "dark opening high in the wall.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 748 las0_5, las0_5.
        new(748, 1, 0, 4,
            "Narrow greenish rock shelves descend around a deep cleft, with dark cave "
            + "openings between the ledges.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 749 las0_6, las0_6.
        new(749, 0, 0, 0,
            "Pale ledges zigzag down a tall purple-gray rock face, broken by dark "
            + "openings and deep cracks.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 750 las0_7, las0_7.
        new(750, 0, 0, 0,
            "A winding series of rough reddish ledges descends through a cramped rocky "
            + "shaft.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 752 las1_1, las1_1.
        new(752, 3, 0, 4,
            "A narrow path spirals around a tall ridged stone column in darkness.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 753 las1_2, las1_2.
        new(753, 2, 0, 4,
            "A rough pale path coils around a tall ribbed pillar, above scattered rock "
            + "formations and a distant orange glow.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 754 las1_3, las1_3.
        new(754, 1, 0, 4,
            "Pale winding rock formations stretch through a dark cavern toward an "
            + "orange-lit opening.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 755 las1_4, las1_4.
        new(755, 0, 0, 4,
            "Huge curved rib-like formations arch over a cavern filled with warm amber "
            + "and green light.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 757 las2_2, las2_2.
        new(757, 5, 0, 0,
            "Thick twisting roots and fine pale tendrils cover uneven rock ledges inside "
            + "a yellow-green cavern.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 760 las3_1, las3_1.
        new(760, 0, 0, 4,
            "Flat-topped gray stone pillars form staggered platforms over a deep "
            + "green-black gap.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 761 las3_2, las3_2.
        new(761, 0, 0, 4,
            "Narrow stone ledges curve around tall gray pillars, with green light glowing "
            + "between them.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 762 las3_3, las3_3.
        new(762, 0, 0, 4,
            "Broad gray ledges and round stone pillars form a rough stairway through a "
            + "cavern, with green light in its cracks.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 738 md8brdg2, md8brdg2.
        new(738, 1, 0, 0,
            "A stairway descends between metal frameworks, pipes, and tall industrial "
            + "walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 739 md8_32, md8_32.
        new(739, 0, 0, 0,
            "Ladders and narrow platforms crowd a tall industrial shaft beside a round "
            + "pipe opening and heavy machinery.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 731 md8_5, md8_5.
        new(731, 0, 0, 0,
            "Narrow buildings with lit windows stand beneath an immense steel structure, "
            + "with street lamps along the road.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 732 md8_6, md8_6.
        new(732, 3, 0, 0,
            "A rust-colored metal bridge crosses a narrow alley between high stone walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 733 md8_b1, md8_b1.
        new(733, 1, 0, 0,
            "Metal ladders, grated platforms, and narrow bridges span an enormous "
            + "industrial shaft, with thick pipes along the walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 734 md8_b2, md8_b2.
        new(734, 1, 0, 0,
            "Stairs and narrow bridges link suspended industrial platforms beside large "
            + "pipes and heavy girders.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 735 sbwy4_22, sbwy4_22.
        new(735, 0, 0, 0,
            "Dim lamps light a narrow metal corridor lined with pipes, ending at a closed "
            + "panel marked with red graffiti.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 736 tunnel_4, tunnel_4.
        new(736, 6, 0, 0,
            "Two rail tracks run through a dark metal tunnel beneath pipes and low "
            + "ceiling lights.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 737 tunnel_5, tunnel_5.
        new(737, 8, 0, 0,
            "Rail tracks converge at a junction inside a tall industrial tunnel, lined "
            + "with pipes and small red lights.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 778 tunnel_6, tunnel_6.
        new(778, 6, 0, 0,
            "Two rail tracks run through a dark metal tunnel beneath pipes and low "
            + "ceiling lights.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 704 trnad_3, trnad_3.
        new(704, 1, 0, 0,
            "Jagged gray-green rocks form a series of narrow ledges and steep drops.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 705 trnad_4, trnad_4.
        new(705, 12, 0, 0,
            "A narrow rocky ridge rises between towering spires, with pale light and mist "
            + "in the deep gaps.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 706 trnad_51, trnad_51.
        new(706, 0, 0, 8,
            "Thick brown roots spread above enormous glowing blue crystals in a tall "
            + "cavern.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 708 trnad_53, trnad_53.
        new(708, 0, 0, 0,
            "Thick brown roots spread above enormous glowing blue crystals in a tall "
            + "cavern.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 709 woa_1, woa_1.
        new(709, 3, 0, 0,
            "A narrow flat-topped rocky ridge extends across a vast mist-filled chasm.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 710 woa_2, woa_2.
        new(710, 0, 0, 0,
            "A narrow flat-topped rocky ridge extends across a vast mist-filled chasm.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 711 woa_3, woa_3.
        new(711, 5, 0, 0,
            "A narrow broken ridge stretches between tall rock pillars above a "
            + "mist-filled chasm.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 529 cosin1, cosin1.
        new(529, 0, 0, 0,
            "Wooden platforms and barrels line a red-rock passage. A lit weapon-shop sign "
            + "hangs above a counter on the lower level.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 530 cosin1_1, cosin1_1.
        new(530, 0, 0, 4,
            "A hanging green lamp lights a cramped wooden room with barrels, shelves, and "
            + "a small desk beneath a peaked roof.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 532 cosin3, cosin3.
        new(532, 3, 0, 4,
            "A bottle-lined bar fills the lower level of a red-rock room. Wooden stairs "
            + "and railings connect the upper walkway, and lamps light the walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 533 cosin4, cosin4.
        new(533, 0, 0, 0,
            "Stone steps join two small ledges in a red-rock passage, with lamps, crates, "
            + "and barrels against the walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 535 cosmin2, cosmin2.
        new(535, 0, 0, 0,
            "A wooden counter and cluttered shelves fill a small peaked-roof shop beneath "
            + "a hanging green lamp.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 536 cosmin3, cosmin3.
        new(536, 0, 0, 0,
            "A low table and stools sit beneath a hanging green lamp in a small wooden "
            + "home, with cooking pots and shelves around the edges.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 537 cosmin4, cosmin4.
        new(537, 0, 0, 0,
            "Pale hides cover the floor of a dim wooden room beneath a peaked roof and a "
            + "green hanging lamp.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 538 cosmin6, cosmin6.
        new(538, 0, 0, 0,
            "Two beds and a small table fit into a red-rock room, with a window and a "
            + "washbasin against the walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 539 cosmin7, cosmin7.
        new(539, 1, 0, 0,
            "Two wooden rooms beneath separate peaked roofs are joined by a short "
            + "passage, with counters, shelves, and hanging lamps inside.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 548 gidun_4, gidun_4.
        new(548, 2, 0, 0,
            "Twisting rock bridges and narrow paths cross red-lit chasms inside a cavern, "
            + "with patches of green light on the stone.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 522 gninn, gninn.
        new(522, 3, 0, 14,
            "Two beds occupy the upper level of a rounded timber-lined building, with "
            + "wooden steps leading down to a small reception area.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 517 gnmk, gnmk.
        new(517, 0, 0, 4,
            "Shattered pipes, bent metal, and broken industrial walls crowd a narrow path "
            + "through the ruins of a reactor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 516 gnmkf, gnmkf.
        new(516, 0, 0, 16,
            "Jagged gray industrial ruins rise beyond a dirt clearing, with trees framing "
            + "the view.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 523 gomin, gomin.
        new(523, 1, 0, 8,
            "A wooden table and a bed occupy a round timber-lined home, with daylight "
            + "falling through a window.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 518 gongaga, gongaga.
        new(518, 7, 0, 2,
            "Small round huts with domed roofs cluster around winding dirt paths between "
            + "steep rocks and dense greenery.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 514 gonjun1, gonjun1.
        new(514, 0, 0, 16,
            "A pale dirt path passes beneath large fallen tree trunks in dense green "
            + "jungle.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 515 gonjun2, gonjun2.
        new(515, 0, 0, 16,
            "Pale dirt paths meet at a fork beneath tall trees and thick jungle "
            + "undergrowth.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 521 gon_i, gon_i.
        new(521, 2, 0, 2,
            "Jars and bottles cover a curved wooden counter inside a small round shop, "
            + "with shelves and hanging goods around the walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 519 gon_wa1, gon_wa1.
        new(519, 2, 0, 2,
            "Weapons hang above a wooden counter in a round timber-lined shop, with a red "
            + "patterned rug on the floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 520 gon_wa2, gon_wa2.
        new(520, 1, 0, 2,
            "Fur rugs and stacked bedding line a circular wooden room. A round opening in "
            + "the floor leads down to the level below.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 524 goson, goson.
        new(524, 2, 0, 2,
            "A low table sits on a patterned rug surrounded by round cushions, inside a "
            + "curved wooden room with cupboards and a bed.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 286 niv_ti1, niv_ti1.
        new(286, 0, 0, 0,
            "Wooden stairs lead into a home with a round dining table and a compact "
            + "kitchen crowded with pots and cupboards.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 287 niv_ti2, niv_ti2.
        new(287, 0, 0, 0,
            "A piano stands along the lower wall of a wooden room, with a bedroom and "
            + "small sitting area behind it.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 289 niv_ti4, niv_ti4.
        new(289, 0, 0, 3,
            "A piano stands along the lower wall of a wooden room, with a bedroom and "
            + "small sitting area behind it.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 297 sinin1_1, sinin1_1.
        new(297, 0, 0, 16,
            "Two staircases curve around a shadowed landing beneath tall arched windows "
            + "and a hanging chandelier.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 302 sininb1, sininb1.
        new(302, 0, 0, 16,
            "A rough purple-lit underground passage leads toward a pale stairway in the "
            + "distance.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 309 sininb51, sininb51.
        new(309, 0, 0, 16,
            "Curving shelves packed with books surround a central desk covered in papers "
            + "and open books beneath a warm ceiling lamp.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 311 mtnvl2, mtnvl2.
        new(311, 0, 0, 65,
            "Jagged, thornlike mountain spires surround narrow, winding rock ledges above "
            + "deep gaps beneath a cloudy sky.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 312 mtnvl3, mtnvl3.
        new(312, 0, 0, 29,
            "A long rope suspension bridge spans a deep gap between jagged rock spires, "
            + "with a barren rocky valley below.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 313 mtnvl4, mtnvl4.
        new(313, 0, 0, 17,
            "A narrow rock ledge winds around a towering, pointed mountain spire, with "
            + "sharp projections jutting out over misty depths.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 314 mtnvl5, mtnvl5.
        new(314, 0, 0, 17,
            "A curved rock ledge runs around the foot of a jagged mountain wall, with "
            + "metal pipes and platforms clinging to the rock above.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 315 mtnvl6, mtnvl6.
        new(315, 0, 0, 29,
            "A tall cylindrical industrial tower stands against a steep mountain wall, "
            + "with thick curved pipes and metal stairs at its base.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 316 mtnvl6b, mtnvl6b.
        new(316, 0, 0, 41,
            "Two railed metal ramps lead up to a rectangular doorway beneath a large "
            + "cylindrical tower, surrounded by heavy pipes and framework.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 318 nvdun2, nvdun2.
        new(318, 0, 0, 45,
            "Rock ledges descend through a tall, narrow cavern, with pale green light "
            + "falling across rough walls and hanging stone formations.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 327 nvmkin32, nvmkin32.
        new(327, 0, 0, 22,
            "A huge blue-lit cylindrical tank towers above heavy pipes in a tall chamber "
            + "crowded with metal beams and machinery.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 623 anfrst_4, anfrst_4.
        new(623, 0, 0, 58,
            "Thick trees and trailing vines surround mossy ground, with giant red-lipped "
            + "pitcher plants rising among the roots and rocks.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 624 anfrst_5, anfrst_5.
        new(624, 0, 0, 4,
            "Greenish light falls into a small cave with rough stone walls, where an "
            + "opening pierces the rock at each end.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 647 ancnt2, ancnt2.
        new(647, 0, 0, 3,
            "A curved stairway descends inside a round stone chamber lined with railings "
            + "and tall pillars, pale light filling the space below.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 608 kuro_5, kuro_5.
        new(608, 9, 0, 0,
            "A large carved relief fills one wall of a round stone chamber, with two "
            + "slender stands before it in warm orange light.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 610 kuro_7, kuro_7.
        new(610, 2, 0, 0,
            "Three tiers of rough stone ledges line a cavern wall, with a doorway framed "
            + "by columns on the upper level.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 633 losin3, losin3.
        new(633, 1, 0, 0,
            "Stone steps connect two levels of a small, rough-walled room, with low pale "
            + "sleeping platforms beneath a bright rectangular window.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 637 loslake1, loslake1.
        new(637, 0, 0, 61,
            "Pale branching structures arch above curved stone walkways and stairs, with "
            + "a large central basin over water beneath a bright blue opening.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 625 sango1, sango1.
        new(625, 0, 0, 0,
            "A broad fallen trunk bridges pale, uneven rocky ground, with red coral-like "
            + "branches rising beside pools of blue-green water.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 619 slfrst_2, slfrst_2.
        new(619, 0, 0, 0,
            "Tall, slender trees crowd around a narrow forest path, their branches fading "
            + "into blue-green mist.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 566 rcktin4, rcktin4.
        new(566, 1, 0, 12,
            "A narrow metal walkway cuts through a chamber packed with long pipes, with a "
            + "ladder descending through a yellow-and-black striped opening.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 567 rcktin5, rcktin5.
        new(567, 6, 0, 50,
            "A cramped metal cockpit has two seats facing a broad dark window and banks "
            + "of switches, with a thick cable lying across the floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 569 rcktin7, rcktin7.
        new(569, 2, 0, 0,
            "A large circular hatch fills the end of a narrow metal walkway, surrounded "
            + "by thick cables and curving structural ribs.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 559 rktmin1, rktmin1.
        new(559, 3, 0, 14,
            "A warmly lit wooden home has a long sitting room with a table and patterned "
            + "floor, with two small bedrooms opening off its far end.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 560 rktmin2, rktmin2.
        new(560, 4, 0, 14,
            "A wooden staircase connects a lamp-lit bedroom with a small dining area, "
            + "where a round table and kitchen counter stand on the lower floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 558 rktsid, rktsid.
        new(558, 4, 0, 20,
            "Round tables and shelves crowd a wooden room with a patterned rug, a small "
            + "sleeping room opening behind it beside an equipment-filled workshop.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 546 gidun_1, gidun_1.
        new(546, 5, 0, 4,
            "Uneven stone paths cross a broad cavern among hanging stalactites and "
            + "green-lit rock walls, narrow shafts of pale light cutting across them.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 299 sinin2_1, sinin2_1.
        new(299, 0, 0, 30,
            "A curved upper landing overlooks a mansion hall, with dim rooms of worn "
            + "furniture opening off it beneath tall, pale-lit windows.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 300 sinin2_2, sinin2_2.
        new(300, 0, 0, 28,
            "Dim, wood-floored rooms open beside a mansion landing, tall windows casting "
            + "pale light over worn furniture and a narrow central passage.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 305 sininb32, sininb32.
        new(305, 0, 0, 4,
            "Tall bookcases crowd a dim stone room, with books and papers covering tables "
            + "beneath hanging lamps and heavy round containers along the walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 691 gaiin_2, gaiin_2.
        new(691, 1, 0, 0,
            "Blue crystal-lined ledges wind above a deep, green-glowing chasm, with dark "
            + "openings piercing the walls at different heights.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 693 gaiin_3, gaiin_3.
        new(693, 3, 0, 0,
            "Rounded pools glow blue-white across an iridescent cavern floor, with "
            + "crystal formations crowding the walls beneath a dark upper opening.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 659 hyou2, hyou2.
        new(659, 0, 0, 0,
            "Snow-laden trees stand close together in a dense forest, a narrow pale path "
            + "winding between their dark trunks.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 684 hyou13_2, hyou13_2.
        new(684, 0, 0, 0,
            "A low icy cavern has jagged walls and a snow-covered floor, with bright "
            + "white light pouring through an opening near the ground.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 678 hyou8_2, hyou8_2.
        new(678, 0, 0, 0,
            "Dark blue rock and ice frame a low cavern passage, with bright white light "
            + "filling an opening beside the uneven frozen floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 661 icedun_1, icedun_1.
        new(661, 0, 0, 0,
            "Blue ice coats stepped rock ledges inside a tall cavern, icicles hanging "
            + "from the upper edges above a bright opening in the lower wall.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 758 las2_3, las2_3.
        new(758, 7, 0, 0,
            "Thin pale roots spread across greenish ground inside a low, ochre-colored "
            + "cave enclosed by rounded rock walls.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 763 las4_0, las4_0.
        new(763, 0, 0, 0,
            "Broad, broken rock ledges project from a dark cavern wall above a brilliant, "
            + "green-lit drop.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 764 las4_1, las4_1.
        new(764, 0, 0, 2,
            "A rough circular opening breaks through the cavern floor, with a short chain "
            + "of small rock platforms extending over its brilliant green depths.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 765 las4_2, las4_2.
        new(765, 0, 0, 21,
            "Separate slabs of rock form a staggered path through a dark void, their "
            + "jagged undersides outlined in green-blue light.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 766 las4_3, las4_3.
        new(766, 0, 0, 5,
            "Scattered rock fragments surround a large green-blue crystal formation, pale "
            + "square tiles covering part of its upper surface.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 652 sninn_2, sninn_2.
        new(652, 3, 0, 0,
            "A small wooden bedroom sits beneath a steep, snow-covered roof, a bedside "
            + "lamp lighting a double bed with patterned covers.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 653 sninn_b1, sninn_b1.
        new(653, 1, 0, 0,
            "An L-shaped bar and round wooden tables fill a stone-floored cellar, with "
            + "stairs rising along the back wall and warm lamps brightening the room.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 654 snow, snow.
        new(654, 7, 0, 0,
            "Steep-roofed wooden houses line snowy paths among bare trees, warm windows "
            + "glowing beneath deep snow with a massive ice wall behind the village.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 703 trnad_2, trnad_2.
        new(703, 1, 0, 0,
            "Jagged gray rock ledges cross a barren landscape beneath a towering pale "
            + "cliff, a green-lit chasm opening along the lower edge.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex)
    ];

    /// <summary>
    /// Six scripted moments.
    ///
    /// <para>The observatory projection is a background and palette change driven by the
    /// field's own SWITCH entity. The coffin is the removal of a background group whose only
    /// owner is an entity named COFFIN. Sephiroth's two exits are the visibility instructions
    /// that end his scripts - the wording says he moves away and disappears, which is what
    /// the scripts do, rather than naming a gait the fields never specify. The two are
    /// different moments and share one recording: 307 is the Nibelheim flashback, where his
    /// line is that he is going to see his mother, and 308 is the present-day library, where
    /// he says he is going north past Mt. Nibel. Palmer is the named actor being made
    /// visible immediately before he speaks his own named line.</para>
    ///
    /// <para>The Temple clock room is the one anchor in this catalog that does not sit on the
    /// instruction that performs the action, and it is worth saying why. Sephiroth is made
    /// visible by VISI 01 at byte 95, but that byte falls between two fades - the fade-in only
    /// finishes at the FADEW at byte 137 - so a description there would be read over a screen
    /// the player cannot see yet. Between that FADEW and his line at byte 174 the script runs
    /// nothing but animation and offset instructions, so there is no request or visibility
    /// instruction to use. Byte 142 is therefore used purely as a clock. It is not read as
    /// meaning anything: the appearance is established by the VISI, by the native actor name
    /// and by the line he speaks straight afterwards, and the sentence says only that he
    /// appears. That handler yields and is re-entered while the animation runs, so the cue
    /// relies on the tracker's once-per-key-per-visit rule to speak once.</para>
    ///
    /// <para>Field 543's PLANET entity is deliberately absent: the footage shows it crossing
    /// the foreground while the boogdemo narration is already playing, so a separate
    /// utterance there would talk over existing narration.</para>
    /// </summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateActionDescriptions() =>
    [
        // 541 bugin1a, cosmo_observatory_display_starts.
        new(541, 16, 3, 28,
            "The room dims. Glowing planets and orbital paths appear above.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),

        // 303 sininb2, vincent_coffin_opens.
        new(303, 0, 3, 4,
            "The coffin opens.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),

        // 308 sininb42, sephiroth_leaves_library.
        new(308, 4, 6, 70,
            "Sephiroth moves away and disappears.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),

        // 558 rktsid, palmer_appears.
        new(558, 24, 3, 45,
            "Palmer appears.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),

        // 307 sininb41, sephiroth_basement_leaves.
        new(307, 2, 4, 52,
            "Sephiroth moves away and disappears.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),

        // 611 kuro_8, sephiroth_clock_room_appears.
        new(611, 21, 5, 142,
            "Sephiroth appears.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index)
    ];

    /// <summary>Everything in this batch, areas and actions together.</summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateAll() =>
    [
        .. CreateAreaDescriptions(),
        .. CreateActionDescriptions()
    ];
}
