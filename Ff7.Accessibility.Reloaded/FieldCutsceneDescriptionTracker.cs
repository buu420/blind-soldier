namespace Ff7.Accessibility.Reloaded;

public sealed class FieldCutsceneDescriptionTracker
{
    private readonly Dictionary<FieldCutsceneDescriptionKey, FieldCutsceneDescriptionCue> cues;
    private readonly Dictionary<string, List<FieldCutsceneDescriptionKey>> recurringGroups;
    private readonly HashSet<FieldCutsceneDescriptionKey> spoken = [];

    // The last byte each observed script was seen at. This is the tracker's only
    // evidence that a script's own instruction pointer moved, and it is what tells a
    // genuinely repeated action apart from one opcode being delivered over and over.
    private readonly Dictionary<(int Field, int Entity, int Script), int> lastByteByScript = [];
    private readonly object sync = new();
    private int currentFieldId = -1;

    public FieldCutsceneDescriptionTracker(IEnumerable<FieldCutsceneDescriptionCue> cues)
    {
        this.cues = cues.ToDictionary(cue => cue.Key);
        recurringGroups = this.cues.Values
            .Where(cue => cue.IsRecurring)
            .GroupBy(cue => cue.RecurringGroup, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(cue => cue.Key).ToList(),
                StringComparer.Ordinal);
    }

    public FieldCutsceneDescriptionCue? Observe(FieldScriptContext context)
    {
        lock (sync)
        {
            if (context.FieldId != currentFieldId)
            {
                currentFieldId = context.FieldId;
                spoken.Clear();
                lastByteByScript.Clear();
            }

            // Recorded for every observed opcode, described or not, and before any
            // catalog filtering: an ordinary WAIT elsewhere in the same script is
            // exactly the evidence that the script moved on.
            var scriptKey = (context.FieldId, context.EntityId, context.ScriptId);
            var movedSinceLastObservation =
                lastByteByScript.TryGetValue(scriptKey, out var previousByte) &&
                previousByte != context.ByteIndex;
            lastByteByScript[scriptKey] = context.ByteIndex;

            var key = new FieldCutsceneDescriptionKey(
                context.FieldId,
                context.EntityId,
                context.ScriptId,
                context.ByteIndex);
            if (!cues.TryGetValue(key, out var cue) || context.Opcode != cue.Opcode)
            {
                return null;
            }

            // A repeatable machine action is released for description again by the
            // native re-entry into its own opening anchor - but only a real re-entry.
            //
            // A yielding request does not advance the script: REQEW (FUN_006124F2
            // into FUN_006127A2 in mode 3) returns 1 with the caller's instruction
            // pointer unchanged for as long as the requested animation is running, so
            // the same opcode is delivered on every frame of it. Mog's accepted feed
            // is exactly that, and treating those repeats as new food produced one
            // description per animation frame. The group is therefore released only
            // when this script has been observed somewhere else since, which is the
            // game itself saying the action finished and came round again. Another
            // entity running in between is not evidence about this one.
            if (cue.StartsRecurringGroup &&
                movedSinceLastObservation &&
                spoken.Contains(key) &&
                recurringGroups.TryGetValue(cue.RecurringGroup, out var groupKeys))
            {
                foreach (var groupKey in groupKeys)
                {
                    spoken.Remove(groupKey);
                }
            }

            return spoken.Add(key) ? cue : null;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            currentFieldId = -1;
            spoken.Clear();
            lastByteByScript.Clear();
        }
    }
}

public static class FieldCutsceneDescriptionCatalog
{
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateEarlyGameDescriptions() =>
    [
        .. CreateOpeningTrainArrival(),
        .. CreateOpeningReactorRegroupDescriptions(),
        .. CreateOpeningReactorBombDescriptions(),
        .. CreateSector8EscapeDescriptions(),
        .. CreateTrainAndSector7Descriptions(),
        .. CreateReactor5AndAerisDescriptions(),
        .. CreateWallMarketThroughMotorcycleDescriptions(),
        .. CreateKalmThroughLowerJunonDescriptions(),
        .. CreateUpperJunonThroughCargoShipDescriptions(),
        .. CreateJunonJourneyVisualDescriptions(),
        .. CreateCorelJourneyVisualDescriptions(),
        .. CreateGoldSaucerFirstVisitDescriptions(),
        .. CreateGoldSaucerArcadeDescriptions(),
        .. CreateGoldSaucerGondolaFilmDescriptions(),
        .. CreateGoldSaucerAreaDescriptions()
    ];

    /// <summary>
    /// What each first-visit Gold Saucer area looks like, spoken once on arrival.
    /// A sighted player takes the layout of a new room in at a glance and never has
    /// to ask again, so these run from MPNAM: every field sets its own displayed area
    /// name exactly once, from its director entity's init, on every entry. That gives
    /// one description per arrival with no new hotkey and no repetition while the
    /// player is in the room.
    ///
    /// Each description is limited to what is actually installed in the field - the
    /// fixtures and machines that have their own entities, the exits, and the staff
    /// and visitors the models place there. Nothing here describes a scene that only
    /// happens once, because these fire on a later entry too; the scripted events
    /// have their own anchored cues.
    /// </summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateGoldSaucerAreaDescriptions() =>
    [
        // 484 astage_a, "Event square". kei1 is a uniformed attendant and hito3..7
        // with man1..3 and wom1..2 are the waiting crowd.
        new(484, 0, 0, 30,
            "Event Square. The theatre's stage front rises ahead of a wide open floor. " +
            "An attendant in uniform stands by the entrance and visitors wait in front of it.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 486 jet, "Speed square". The one arrowed gateway is the Shooting Coaster
        // entrance; moni is the screen, choko wears the mascot costume, and the
        // field's own dialogue warns visitors about the steps.
        new(486, 0, 0, 14,
            "Speed Square. Steps lead up to the Shooting Coaster's entrance, with a screen " +
            "mounted above the walkway. Visitors and families move about, and a member of " +
            "staff walks around in a chocobo costume.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 487 jetin1, "Platform". che, man1, man2 and gairl are staff, jet is the
        // ride car itself, and ramp1, ramp2 and panel are the boarding fixtures.
        new(487, 0, 0, 14,
            "The Shooting Coaster's boarding platform. Staff wait at the registration " +
            "counter, ramps lead up to the ride car, and a control panel stands beside it.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 488 bigwheel, "Round Square". Root's reviewed frames of the station itself,
        // rather than a layout guessed from the entity names.
        new(488, 0, 0, 173,
            "Round Square. A wooden gondola waits at the boarding platform beside a ticket " +
            "booth shaped like a yellow moogle. Railings run along the platform and pulley " +
            "wheels turn overhead.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 489 and 490 bwhlin, "Inside the Ferris Wheel". Deliberately one short
        // sentence: the ride's first film starts about ten seconds after the field
        // loads, and a long paragraph here would still be running over it.
        new(489, 0, 0, 0,
            "Inside a wooden gondola, with windows on both sides.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        new(490, 0, 0, 0,
            "Inside a wooden gondola, with windows on both sides.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 491 ghotel, "Ghost Hotel". door, gate, light1 and light2 are the entrance
        // fixtures and bat1..bat5 are the bats overhead.
        new(491, 0, 0, 42,
            "Ghost Square. The Ghost Hotel stands ahead behind a gate, its doorway lit by " +
            "lamps, with bats circling overhead. Other visitors wander the street outside.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 492 ghotin_1, "Hotel Lobby", and 495 ghotin_3, "Hotel Shop". Both are
        // root's reviewed frames of the actual backgrounds rather than a layout
        // inferred from entity names. Mr. Hangman is deliberately not named at the
        // shop: the narrator introduces that name later, in the main hall.
        new(492, 0, 0, 19,
            "A red carpet and curling staircase fill a dark hall decorated with grinning " +
            "monster faces. Skull-shaped lamps frame the doorways.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        new(495, 0, 0, 14,
            "Bottles and candles crowd a small counter beneath a red canopy. A fire glows " +
            "beside hanging cages and monster-faced decorations.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 505 games, "Wonder Square". The one gateway leads into the arcade building;
        // choko and c2 wear the mascot costumes.
        new(505, 0, 0, 41,
            "Wonder Square. The way into the arcade building lies ahead. Visitors and " +
            "families fill the square, with staff in mascot costumes among them.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 506 games_1, "Building 1f.". ude1, ude2 and udel are the arm wrestling
        // machine, ufo1 and ufo2 the two Wonder Catcher cabinets, bsl with ball,
        // ring, base_l and bs_b the basketball game, and s1 the prize counter.
        new(506, 0, 0, 14,
            "The arcade's ground floor. The arm wrestling machine, the Wonder Catcher and " +
            "the basketball hoop stand around the room, with the prize counter to one side " +
            "and stairs up to the second floor.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 507 games_2, "Building 2f.". mogu is the Mog House, bike the G Bike machine,
        // and snowb and subm the snowboard and submarine machines, which stand here
        // whether or not they can be played yet. kakul1 and kakul2 are the two control
        // sides of one 3D Battler platform, not two cabinets: root's reviewed close
        // frame shows a single glowing disc with a control station on either side.
        new(507, 0, 0, 14,
            "The arcade's upper floor. The Mog House, the 3D Battler platform and the " +
            "G Bike machine stand around the room, along with the snowboard and submarine " +
            "machines, and stairs lead back down.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 509 chorace, "Chocobo Square". door, word, light and chocobo are the
        // entrance fixtures; the Shinra soldiers here belong to one scripted scene
        // and are not described by this per-arrival cue.
        new(509, 0, 0, 24,
            "Chocobo Square. The way into the racetrack building lies ahead, under a lit " +
            "sign, with a chocobo beside it.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex),

        // 511 crcin_1, "Ticket Office". cg1..cg3 are the counter attendants, moni the
        // screen, and kyaku1..kyaku5 the other customers. The odds sheet is named by
        // the field's own dialogue.
        new(511, 0, 0, 175,
            "The chocobo racing ticket office. Attendants stand at the betting counter, " +
            "a screen shows the track, and an odds sheet hangs nearby. Other customers " +
            "watch and wait around the room.",
            FieldOpcodeAddressResolver.OpcodeMapNameIndex)
    ];

    /// <summary>
    /// The five Round Square gondola films. Each anchor is the F9 that starts the
    /// film, immediately after the F8 that names it, in installed bwhlin (489) and
    /// bwhlin2 (490) entity 0 <c>dic</c> script 0. Every film appears twice because
    /// the ride's script forks on which companion came along, and only one branch
    /// runs per ride.
    ///
    /// These paragraphs are the fallback: when the reviewed recording is available
    /// the independent track plays instead, because a screen reader would be cut off
    /// by any button press during the film. The text is the same reviewed prose,
    /// joined into one paragraph, so a player without the audio assets still hears
    /// what is on screen.
    /// </summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateGoldSaucerGondolaFilmDescriptions() =>
    [
        // Film 6, gold2.
        new(489, 0, 0, 155, GondolaSpeedSquareText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(489, 0, 0, 345, GondolaSpeedSquareText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 7, gold3.
        new(489, 0, 0, 210, GondolaChocoboSquareText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(489, 0, 0, 429, GondolaChocoboSquareText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 8, gold4.
        new(489, 0, 0, 288, GondolaParkAndStatueText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(489, 0, 0, 612, GondolaParkAndStatueText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 10, gold5.
        new(490, 0, 0, 91, GondolaGhostSquareText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(490, 0, 0, 223, GondolaGhostSquareText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 9, gold6. The file names and the native numbers are not in the same
        // order here, which is why the anchor carries the number.
        new(490, 0, 0, 146, GondolaEventSquareText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(490, 0, 0, 307, GondolaEventSquareText, FieldOpcodeAddressResolver.OpcodeMovieIndex)
    ];

    private const string GondolaSpeedSquareText =
        "A roller coaster races along looping tracks outlined with lights. " +
        "Searchlights sweep across the park beneath a full moon.";

    private const string GondolaChocoboSquareText =
        "Brightly colored chocobos race past the wooden gondola's windows beneath " +
        "sweeping searchlights.";

    private const string GondolaParkAndStatueText =
        "The view sweeps over round platforms filled with rides and colorful lights. " +
        "It rises along a huge golden tower. A colossal golden statue of a muscular " +
        "man in a winged helmet towers over the park.";

    private const string GondolaGhostSquareText =
        "The wooden gondola passes a dark mansion and crooked gravestones, " +
        "surrounded by swirling fog and bats.";

    private const string GondolaEventSquareText =
        "Colorful balloons rise from an outdoor stage, drifting around the wooden " +
        "gondola.";

    /// <summary>
    /// Visible actions inside the Gold Saucer arcades. The narrator boxes and the
    /// instruction, price and result windows are ordinary native dialogue and are
    /// already spoken by the message path, so nothing here repeats them: each cue
    /// describes only what the models do, anchored to the request that starts that
    /// animation. Text is root's footage-reviewed prose.
    /// </summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateGoldSaucerArcadeDescriptions() =>
    [
        // mogu_1 event(6) Main drives the whole Mog House show. Entity 8 is Mog and
        // entity 9 is the visiting moogle; each byte below is the request that runs
        // one of their animations.
        new(508, 6, 0, 43,
            "Mog steps out of his mushroom-shaped house.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // 174 branches on the feeding count: 14 is the underfed attempt and 15 the
        // overfed one. Both look the same from outside, and the count itself is not
        // exposed here.
        new(508, 6, 0, 188,
            "Mog flaps his wings, hops into the air and drops back to the ground.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(508, 6, 0, 199,
            "Mog flaps his wings, hops into the air and drops back to the ground.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // 263 is the branch the script only reaches when the attempt succeeds.
        new(508, 6, 0, 263,
            "Mog hops onto a mushroom, then flies in a wide loop around his home.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(508, 6, 0, 284,
            "Mog lands and goes back inside. The house lights dim.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // The visitor is described as a pink moogle until the narrator names her.
        new(508, 6, 0, 306,
            "A pink moogle approaches the house.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(508, 6, 0, 322,
            "She knocks at the door.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(508, 6, 0, 328,
            "Mog comes outside.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),

        // The accepted feed. esa(7) Main byte 82 is where the thrown nut is taken:
        // it runs mogu's eat animation, hides the nut and only then increments the
        // count at byte 91. The count itself is never spoken, and the narrator's own
        // instruction and Mog's native squeak are left alone. Recurring, because the
        // player feeds him again and again without leaving the room.
        new(508, 7, 0, 82,
            "Mog eats the nut.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex,
            RecurringGroup: "mog-feeding", StartsRecurringGroup: true),

        // The rest of the show, which the earlier eight cues stopped short of.
        // event(6) Main byte 522 starts animation 14 and byte 536 runs its long
        // middle section, which is the circling itself; magu has been requested at
        // 533 and is watching by then.
        new(508, 6, 0, 536,
            "Mog circles through the air while the pink moogle watches.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // 628 places Mog at the door, opens it and walks him out; 644 does the same
        // for the visitor. She is still unnamed here - the narrator introduces the
        // name Mag at byte 663, after both of these.
        new(508, 6, 0, 628,
            "Mog comes out of the house.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(508, 6, 0, 644,
            "The pink moogle comes out and stands beside him.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // 666 and 669 walk both of them along the same four waypoints to the west
        // and hide the models. The narrator has named Mag by this point.
        new(508, 6, 0, 666,
            "Mog and Mag walk away together towards the edge of the clearing.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),
        // 699..919 request entities 10..21 in turn, each of which becomes visible at
        // the house and follows the same path away. No count is spoken: the number
        // of them is not something the description needs to assert.
        new(508, 6, 0, 699,
            "Small moogles pour out of the house, hopping across the clearing one after another.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(508, 6, 0, 919,
            "The last one stops, looks back, then hurries after the others.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),

        // The Wonder Catcher. Root's review established there is no separately moving
        // claw model, so the visible action is Cloud working the machine's controls
        // and its lights flashing. games_1 cloud(1) script 8 is the left-hand control
        // side and script 9 the right-hand side of the *same* machine, and the two
        // run the same four animations after the gil check at byte 88. The prize
        // windows that follow are ordinary native dialogue.
        //
        // These are grouped as recurring: the machine can be played again without
        // leaving the room, and the once-per-visit rule that suits a story action
        // would leave every play after the first silent. The group is released by the
        // native re-entry into its own first anchor.
        new(506, 1, 8, 113,
            "Cloud steps up to the Wonder Catcher and starts it.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex,
            RecurringGroup: "wonder-catcher-left", StartsRecurringGroup: true),
        new(506, 1, 8, 127,
            "He takes hold of the controls and the cabinet lights flash.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index,
            RecurringGroup: "wonder-catcher-left"),
        new(506, 1, 8, 140,
            "Cloud holds still, watching the machine work.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex,
            RecurringGroup: "wonder-catcher-left"),
        new(506, 1, 8, 389,
            "Cloud lets go of the controls and steps back.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index,
            RecurringGroup: "wonder-catcher-left"),
        new(506, 1, 9, 108,
            "Cloud steps up to the Wonder Catcher and starts it.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex,
            RecurringGroup: "wonder-catcher-right", StartsRecurringGroup: true),
        new(506, 1, 9, 122,
            "He takes hold of the controls and the cabinet lights flash.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index,
            RecurringGroup: "wonder-catcher-right"),
        new(506, 1, 9, 135,
            "Cloud holds still, watching the machine work.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex,
            RecurringGroup: "wonder-catcher-right"),
        new(506, 1, 9, 384,
            "Cloud lets go of the controls and steps back.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index,
            RecurringGroup: "wonder-catcher-right")
    ];

    /// <summary>
    /// Reviewed first-visit Gold Saucer scenes. Text comes from root's footage-backed
    /// review, and every anchor is an installed opcode inside a native GameMoment
    /// gate, so these fire once during the first visit and never on a later one.
    /// Party composition varies here, so no cue names a companion the native script
    /// does not itself guarantee: only Yuffie's arrival sits behind a native
    /// IFMEMBQ availability test, and only Cait Sith and Dio are fixed by script.
    /// </summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateGoldSaucerFirstVisitDescriptions() =>
    [
        // gldgate/dic Main byte 608 gates the arrival on GameMoment 436. The scroll
        // at 622 and the linear pan at 629 settle first; 638 is the first request,
        // which runs before any companion arrives at 644.
        new(497, 0, 0, 638,
            "Seven round, brightly coloured tube entrances ring a circular floor painted with a huge smiling face.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),
        // The arrival is companion-aware because the installed script is: each byte
        // below requests one named entity, and only Yuffie's sits behind an
        // availability test. Barret, Red XIII, Tifa and Aeris are requested
        // unconditionally, so naming them is what the screen actually shows - the
        // PRTYE at 617 reduces the *party* to Cloud, but these five are placed as
        // field models regardless.
        new(497, 0, 0, 644,
            "Barret walks in and looks around.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(497, 0, 0, 650,
            "Red XIII walks in and stops beside him.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(497, 0, 0, 656,
            "Tifa runs in and joins them.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        // 662 IFMEMBQ tests Yuffie's availability, so 665 runs only when she is here.
        new(497, 0, 0, 665,
            "Yuffie joins the group on the terminal floor.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(497, 0, 0, 671,
            "Aeris hurries up to Cloud.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // The departure. 826 makes Barret unavailable and 848 runs his script 6,
        // which walks him to a tube, jumps him into it and hides the model.
        new(497, 0, 0, 848,
            "Barret runs to one of the tubes and jumps in.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),

        // games/dic Main byte 68 gates the Wonder Square scene on GameMoment 440.
        // 81 is the first request of entity 7, the cat, before any dialogue.
        new(505, 0, 0, 81,
            "A small crowned cat riding a large white moogle approaches Cloud.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),

        // coloss/dic Main byte 34 gates the Battle Square discovery on 442. The
        // pan at 47..63 settles before the first request at 68, which is Cloud reacting.
        new(499, 0, 0, 68,
            "A broad staircase with a purple carpet rises between rows of green tube entrances toward the arena.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(499, 0, 0, 103,
            "Cloud runs forward and kneels beside a fallen person at the foot of the stairs.",
            FieldOpcodeAddressResolver.OpcodeSplitIndex),

        // coloin2/dic Main byte 42 gates the lobby on 442. This description is the
        // first-visit incident only; the ordinary Arena Lobby is field 500.
        new(501, 0, 0, 51,
            "Several people lie motionless across a black-and-white tiled floor, beside a purple carpet.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(501, 0, 0, 118,
            "Dio and large guards close in on the party.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),

        // clsin2_1/dic Main byte 21 gates the arena on 442; the pan settles by byte 49.
        new(502, 0, 0, 49,
            "Cloud and his companions stand on a raised stone platform with a red circular floor design, " +
            "surrounded by a glowing purple trench.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        // 61 and 67 are kei2 and kei3, whose scripts both make the model visible and
        // run it to the platform before turning it round.
        new(502, 0, 0, 61,
            "A uniformed guard runs onto the platform.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(502, 0, 0, 67,
            "A second guard runs on and turns to face the group.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        // me1, me2 and me3 are the three mechanical guards. me1 simply appears;
        // me2 and me3 each play an animation and JUMP onto the platform.
        new(502, 0, 0, 110,
            "A tall mechanical guard steps out onto the platform.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(502, 0, 0, 128,
            "A second mechanical guard leaps down onto the platform.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(502, 0, 0, 131,
            "A third leaps down on the other side.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // 152, 155 and 158 run script 4 on all three, which is one animation and a
        // walk toward the party.
        new(502, 0, 0, 152,
            "The mechanical guards close in on Cloud and his companions.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),

        // clsin2_3/dic Main writes 445 at byte 54 and jumps to Corel Prison at 88.
        // 28 runs kei1's script 4, which turns the attendant and works the switch
        // entity that opens the floor.
        //
        // 48 and 51 are two REQSW requests that start Cloud's and the mechanical
        // guard's own scripts, each a JUMP to the same point below followed by hiding
        // the model. Two asynchronous requests animating two model entities are not
        // two separate falls: root's reviewed frames at source 1374 s and
        // 1383.5..1388.5 s show the guard holding Cloud against its front and leaping
        // through the hatch with him still held. The earlier pair described Cloud
        // tumbling alone and the guard following, which is not what is on screen, so
        // the shared jump is one cue anchored to the first of the two requests.
        new(504, 0, 0, 28,
            "A uniformed guard works a control, and the circular floor hatch opens onto a dark shaft.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(504, 0, 0, 48,
            "The mechanical guard leaps into the opening with Cloud held against it.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),

        // jet: Dio's optional conversation. sen(6) script 5 is the one-shot LINE
        // trigger that runs it, gated in dio's own init on GameMoment 440..442 and
        // Bank[3][67] bit 1, so it happens once and only on this visit.
        new(486, 6, 5, 10,
            "A tall, heavily built man steps in front of Cloud.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),
        new(486, 6, 5, 464,
            "Dio walks away across the square and out of sight.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex)
    ];

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateOpeningTrainArrival() =>
    [
        new(
            116,
            0,
            0,
            160,
            "A train pulls into the station beside a metal platform under green industrial light."),
        new(
            116,
            0,
            0,
            192,
            "Avalanche fighters leap down and rush the platform, knocking two Shinra guards to the ground."),
        new(
            116,
            0,
            0,
            204,
            "Barret, a towering man with a gun-arm, steps off. Cloud flips down behind him, an enormous sword on his back.")
    ];

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateOpeningReactorRegroupDescriptions() =>
    [
        new(
            116,
            0,
            0,
            269,
            "Barret motions for Cloud to follow and charges up the platform, leaving the fallen guards behind."),
        new(
            117,
            0,
            0,
            82,
            "Cloud catches up with Biggs, Jessie, and Wedge at a locked security gate. Barret charges in, jabbing a finger as he orders them to split up."),
        new(
            117,
            0,
            0,
            85,
            "Jessie works the controls. The two heavy doors unlock and slide apart.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            117,
            0,
            0,
            122,
            "Jessie, Biggs, and Wedge run through the open gate one after another."),
        new(
            117,
            0,
            0,
            134,
            "Barret gives Cloud one last suspicious look, then runs after the others. Left alone, Cloud turns toward the towering reactor.")
    ];

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateOpeningReactorBombDescriptions() =>
    [
        new(
            125,
            3,
            5,
            54,
            "The reactor flashes red. Cloud freezes on tiptoe as a sharp hum pierces his thoughts."),
        new(
            125,
            3,
            6,
            34,
            "Cloud kneels beside the reactor machinery and begins setting the bomb."),
        new(
            125,
            3,
            6,
            89,
            "Cloud finishes arming the bomb and rises.")
    ];

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateSector8EscapeDescriptions() =>
    [
        new(
            136,
            0,
            0,
            50,
            "In a smoke-filled service tunnel, Jessie kneels beside a bomb fixed to the rubble blocking Avalanche's escape."),
        new(
            136,
            0,
            0,
            108,
            "Biggs and Wedge look back toward the ruined reactor. Barret stands apart in grim silence."),
        new(
            136,
            0,
            0,
            161,
            "At Jessie's warning, everyone turns away from the charge and braces for the blast."),
        new(
            133,
            7,
            1,
            3,
            "Jessie's bomb detonates. Fire and smoke punch through the rubble, opening the way outside.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            133,
            0,
            0,
            78,
            "The No. 1 Reactor erupts in a towering fireball. Flames surge through the surrounding Sector 8 streets.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            133,
            0,
            0,
            86,
            "Cloud leaps through the breach and lands in the street. Barret, Biggs, and Jessie follow one by one."),
        new(
            133,
            0,
            0,
            110,
            "Wedge tumbles out last, runs in a panicked circle, and pats at his smoking clothes."),
        new(
            133,
            0,
            0,
            160,
            "Biggs, Wedge, and Jessie nod to Barret, then split up and run in different directions."),
        new(
            133,
            0,
            0,
            175,
            "Barret starts after them. Cloud raises a hand and calls him back."),
        new(
            134,
            0,
            0,
            5,
            "The view sweeps past glowing LOVELESS billboards before settling on a debris-strewn Sector 8 street.",
            FieldOpcodeAddressResolver.OpcodeAkaoIndex),
        new(
            134,
            0,
            0,
            42,
            "Panicked pedestrians rush through the square. One knocks the flower girl down; she gets up, dusts herself off, and approaches Cloud."),
        new(
            134,
            2,
            5,
            0,
            "The flower girl smiles and places a flower in Cloud's hand.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            137,
            1,
            9,
            65,
            "A train whistles below. Soldiers pour in from both sides, closing a ring around Cloud above the tracks.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            137,
            0,
            3,
            401,
            "The soldiers rush him. Cloud vaults over the railing, drops onto the train roof, and lands in a crouch as it speeds into the tunnel.")
    ];

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateTrainAndSector7Descriptions() =>
    [
        new(
            138,
            11,
            3,
            153,
            "The freight-car hatch slides open. Cloud drops inside, his face blackened with soot, as the others turn toward him."),
        new(
            138,
            15,
            8,
            84,
            "Jessie shuts the roof hatch, notices the soot on Cloud's face, and steps close to gently wipe it away."),
        new(
            138,
            15,
            8,
            224,
            "Jessie crosses to the floor hatch and drops into the passenger car below. The others follow."),
        new(
            138,
            11,
            8,
            9,
            "Cloud takes one last look around the freight car, then follows them through the hatch."),
        new(
            143,
            13,
            6,
            28,
            "Jessie activates the wall monitor. A glowing green model of Midgar forms above it: the upper plate, the slums beneath, and the central support pillar."),
        new(
            143,
            0,
            4,
            4,
            "The display changes to the train's spiral route around the pillar, with security checkpoints lighting up along the track."),
        new(
            139,
            17,
            1,
            5,
            "The train races along elevated tracks through Midgar's industrial undercity, spiraling beneath the enormous plate overhead.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            139,
            31,
            11,
            60,
            "Biggs, Wedge, and Jessie hurry into the next car while Barret stalks down the aisle toward a Shinra employee."),
        new(
            139,
            31,
            13,
            21,
            "Barret slams a fist into the wall above the seated Shinra employee, making him jump, then springs back and aims his gun-arm at him.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            139,
            31,
            17,
            23,
            "At the blocked end of the train, Barret forces open a side door. The maintenance tunnel races past outside.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            139,
            19,
            1,
            0,
            "The train car's lighting turns red as Shinra's security system begins locking the cars one by one."),
        new(
            146,
            1,
            0,
            51,
            "The train emerges from the tunnel and brakes beside the Sector 7 platform.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            146,
            12,
            3,
            29,
            "Barret jumps down from the train. Biggs, Wedge, Jessie, and Cloud follow one by one."),
        new(
            146,
            12,
            3,
            97,
            "Avalanche gathers around Barret on the platform while the train waits behind them."),
        new(
            154,
            25,
            4,
            0,
            "Inside Seventh Heaven, Marlene spots Barret, runs across the bar, and throws her arms around him.",
            FieldOpcodeAddressResolver.OpcodeAkaoIndex),
        new(
            154,
            19,
            5,
            94,
            "Barret opens the concealed entrance beneath the pinball machine and climbs down into Avalanche's basement hideout.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),
        new(
            154,
            20,
            15,
            140,
            "As Cloud heads for the door, Tifa darts in front of him and blocks his way.",
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex),
        new(
            294,
            0,
            0,
            4,
            "Seven years earlier, teenage Cloud waits with Tifa on Nibelheim's water tower beneath a sky crowded with stars.",
            FieldOpcodeAddressResolver.OpcodeFadeIndex),
        new(
            294,
            6,
            1,
            0,
            "A bright shooting star streaks across the sky above them.",
            FieldOpcodeAddressResolver.OpcodeBackgroundOnIndex),
        new(
            154,
            19,
            7,
            135,
            "Barret grudgingly tosses Cloud his pay."),
        new(
            142,
            6,
            0,
            59,
            "The train car turns red. Security alarms pulse as Shinra's scanners identify the group's forged passes."),
        new(
            142,
            22,
            2,
            75,
            "A man shoulders past Cloud and hurries toward the next car."),
        new(
            142,
            23,
            2,
            41,
            "A woman bumps into Cloud, then bolts toward the next car."),
        new(
            142,
            18,
            7,
            20,
            "At the blocked end of the train, Barret forces open a side door. The tunnel races past outside."),
        new(
            142,
            18,
            7,
            110,
            "Barret launches himself from the speeding train and lands in the maintenance tunnel."),
        new(
            142,
            19,
            6,
            53,
            "Tifa jumps from the train after him."),
        new(
            142,
            17,
            10,
            32,
            "Cloud sprints to the open door and leaps into the tunnel."),
        new(
            140,
            21,
            8,
            20,
            "At the blocked end of the train, Barret forces open a side door. The tunnel races past outside."),
        new(
            140,
            21,
            8,
            98,
            "Barret launches himself from the speeding train and lands in the maintenance tunnel."),
        new(
            140,
            22,
            4,
            53,
            "Tifa jumps from the train after him."),
        new(
            140,
            20,
            7,
            33,
            "Cloud sprints to the open door and leaps into the tunnel."),
        new(
            141,
            15,
            1,
            45,
            "Tifa takes a breath, then jumps from the speeding train into the tunnel."),
        new(
            141,
            14,
            7,
            97,
            "Barret waits until the others are clear, then makes the final jump."),
        new(
            141,
            13,
            6,
            22,
            "Cloud runs to the open door and dives from the train.")
    ];

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateReactor5AndAerisDescriptions() =>
    [
        new(
            132,
            2,
            7,
            83,
            "Cloud reaches toward the bomb. A red-white flash freezes him as a memory breaks through."),
        new(
            322,
            0,
            0,
            444,
            "In the memory, Tifa kneels beside her injured father on the floor of the Nibelheim reactor."),
        new(
            322,
            0,
            0,
            446,
            "In the memory, Tifa kneels beside her injured father on the floor of the Nibelheim reactor."),
        new(
            322,
            6,
            3,
            104,
            "Tifa rises, seizes Sephiroth's sword, and runs deeper into the reactor."),
        new(
            132,
            2,
            8,
            5,
            "The memory vanishes. Cloud clutches his head, then steadies himself as Tifa watches.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index),
        new(
            132,
            2,
            6,
            34,
            "Cloud kneels at the machinery and begins arming the second reactor bomb."),
        new(
            127,
            3,
            3,
            35,
            "The walkway doors seal behind the party. President Shinra appears on an upper platform high above them."),
        new(
            127,
            4,
            8,
            44,
            "A red armored Air Buster stomps onto the bridge behind them, cutting off their escape."),
        new(
            127,
            8,
            3,
            0,
            "A Shinra helicopter descends beside the bridge and hovers behind President Shinra.",
            FieldOpcodeAddressResolver.OpcodeAkaoIndex),
        new(
            127,
            8,
            5,
            12,
            "President Shinra's helicopter rises from the bridge and carries him away.",
            FieldOpcodeAddressResolver.OpcodeAkaoIndex),
        new(
            127,
            4,
            12,
            19,
            "Air Buster breaks apart in a chain of explosions. The blast tears through the bridge, leaving Cloud hanging from the broken edge."),
        new(
            127,
            2,
            7,
            68,
            "Cloud's grip slips. He falls into the darkness below."),
        new(
            183,
            6,
            7,
            39,
            "Light reveals a ruined church. Cloud wakes in a bed of yellow flowers, with Aeris beside him.",
            FieldOpcodeAddressResolver.OpcodeFadeIndex),
        new(
            182,
            1,
            1,
            20,
            "The church doors open. Reno, a red-haired Turk in a blue suit, enters with Shinra soldiers.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            184,
            2,
            6,
            31,
            "Cloud jumps onto the church's broken rafters and calls for Aeris to follow."),
        new(
            184,
            8,
            1,
            53,
            "The falling barrel strikes the soldier below and knocks him down.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            184,
            9,
            1,
            50,
            "The falling barrel strikes the soldier below and knocks him down.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            184,
            10,
            1,
            50,
            "The falling barrel strikes the soldier below and knocks him down.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            184,
            8,
            1,
            166,
            "The falling barrel misses the soldier and crashes to the floor.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            184,
            9,
            1,
            208,
            "The falling barrel misses the soldier and crashes to the floor.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            184,
            10,
            1,
            127,
            "The falling barrel misses the soldier and crashes to the floor.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            184,
            11,
            1,
            80,
            "The falling barrel misses the soldier and crashes to the floor.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            181,
            7,
            0,
            35,
            "Cloud and Aeris emerge onto the church roof high above the Sector 5 slums.",
            FieldOpcodeAddressResolver.OpcodeFadeIndex),
        new(
            181,
            4,
            5,
            7,
            "Aeris follows Cloud across a broken gap in the roof."),
        new(
            181,
            3,
            8,
            46,
            "Together they leap from roof to roof toward the slums."),
        new(
            188,
            0,
            0,
            70,
            "Aeris leads Cloud into her home, where her adoptive mother, Elmyra, comes to greet them.",
            FieldOpcodeAddressResolver.OpcodeSplitIndex),
        new(
            276,
            2,
            1,
            52,
            "In a memory of Nibelheim, teenage Cloud sits in his childhood bedroom while his mother comes to speak with him.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            190,
            0,
            0,
            90,
            "The memory fades. Cloud wakes alone in Aeris's upstairs bedroom.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex)
    ];

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateWallMarketThroughMotorcycleDescriptions() =>
    [
        new(
            192,
            0,
            3,
            282,
            "In a derelict playground, Aeris sits atop the broken slide. Cloud climbs up beside her.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            192,
            0,
            3,
            503,
            "Cloud and Aeris look toward the gate as Tifa rides past in a chocobo-drawn carriage.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            192,
            0,
            3,
            525,
            "Tifa's carriage disappears through the gate. Cloud and Aeris climb down from the slide to follow.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            206,
            7,
            5,
            12,
            "At Cloud's suggestion that he dress as a woman, Aeris suddenly breaks into laughter. Cloud stares at her, baffled.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index),
        new(
            220,
            8,
            1,
            73,
            "Cloud enters a tiled bath packed with muscular men. Mukki ushers him into the crowded tub.",
            FieldOpcodeAddressResolver.OpcodeAnime1Index),
        new(
            220,
            9,
            1,
            40,
            "In a private room, Cloud finds a translucent double of himself crouched in the corner. The vision confronts him; Cloud clutches his head and collapses.",
            FieldOpcodeAddressResolver.OpcodeCanm1Index),
        new(
            216,
            10,
            1,
            160,
            "One of the Honeybee Inn women sits Cloud down and carefully applies his makeup."),
        new(
            201,
            7,
            3,
            26,
            "Behind the curtain, Cloud changes into the dress and wig. He steps back out transformed for the disguise.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),
        new(
            210,
            7,
            4,
            0,
            "Don Corneo points to Cloud and chooses him.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),
        new(
            210,
            7,
            5,
            13,
            "Don Corneo points to Aeris and chooses her.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index),
        new(
            210,
            7,
            6,
            13,
            "Don Corneo points to Tifa and chooses her.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index),
        new(
            208,
            5,
            3,
            22,
            "Cloud throws off the dress and wig, revealing his uniform and sword.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),
        new(
            211,
            5,
            10,
            31,
            "Cloud throws off the dress and wig, revealing his uniform and sword.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),
        new(
            211,
            12,
            9,
            125,
            "Don Corneo presses a hidden switch. The floor opens beneath Cloud, Tifa, and Aeris.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            267,
            0,
            0,
            195,
            "President Shinra and his executives sit around a conference table high above Midgar as Reeve objects to the plan to destroy Sector 7."),
        new(
            156,
            15,
            5,
            28,
            "High above the slums, Wedge falls from the Sector 7 pillar and crashes onto the ground near Cloud, badly injured.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),
        new(
            160,
            0,
            0,
            36,
            "Reno drops onto the top of the Sector 7 support pillar.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            160,
            6,
            6,
            0,
            "Reno presses the plate-release control, activating the support pillar's time bomb.",
            FieldOpcodeAddressResolver.OpcodeCanm1Index),
        new(
            160,
            1,
            3,
            84,
            "A helicopter lowers beside the pillar. Tseng is aboard with Aeris held captive.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            160,
            8,
            11,
            19,
            "Tseng slaps Aeris across the face.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            160,
            11,
            3,
            2,
            "President Shinra watches from his office as the pillar buckles and the Sector 7 plate crashes down, crushing Seventh Heaven and the slums. Cloud, Tifa, and Barret swing away on a cable.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            193,
            0,
            0,
            129,
            "Barret runs through the wreckage beneath the fallen plate, desperately searching for Marlene and Avalanche.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            193,
            2,
            12,
            120,
            "Overcome with grief, Barret raises his gun-arm and fires repeatedly into the air.",
            FieldOpcodeAddressResolver.OpcodeCanm1Index),
        new(
            147,
            13,
            3,
            20,
            "Elmyra waits among families at the station as returning troops reunite with their wives and children. Her husband never appears.",
            FieldOpcodeAddressResolver.OpcodeWaitIndex),
        new(
            147,
            13,
            10,
            9,
            "Wounded Ifalna collapses and dies on the station floor. Young Aeris remains beside her.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),
        new(
            189,
            11,
            3,
            4,
            "Years later, Tseng comes to Elmyra's house. Young Aeris stays close to Elmyra while he asks her to return to Shinra.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),
        new(
            190,
            6,
            3,
            6,
            "Barret rushes upstairs and sweeps Marlene into a tight hug.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),
        new(
            225,
            5,
            3,
            12,
            "Cloud, Tifa, and Barret climb a swaying cable through the wreckage toward the towering Shinra Building.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            254,
            0,
            0,
            23,
            "From the air duct, Cloud, Tifa, and Barret peer down at President Shinra and his executives gathered around a long conference table."),
        new(
            259,
            1,
            3,
            184,
            "The camera reveals Jenova's headless, human-shaped body suspended behind glass. It twitches.",
            FieldOpcodeAddressResolver.OpcodeScroll2DIndex),
        new(
            263,
            0,
            0,
            99,
            "In Hojo's laboratory, Aeris is sealed inside a glass containment chamber. A red, lion-like beast is held in the adjoining pod."),
        new(
            263,
            7,
            6,
            38,
            "A red beast bursts from the shattered chamber and lunges at Hojo, knocking him down.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            267,
            0,
            0,
            33,
            "Rude and Shinra guards march the captured party into President Shinra's office. The President waits behind the circular conference table.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            258,
            1,
            6,
            8,
            "Cloud wakes in the prison cell and finds its door standing open. Beyond it, a dark trail of blood smears the hallway floor.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),
        new(
            267,
            0,
            0,
            286,
            "The party enters President Shinra's office and finds him dead at his desk, Sephiroth's sword driven through him.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            267,
            0,
            0,
            340,
            "A helicopter rises outside the office windows. Rufus Shinra, a young man in a long white coat, stands on its open landing platform.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            267,
            6,
            3,
            4,
            "Palmer emerges from hiding near the President's desk and recoils from the party.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),
        new(
            269,
            1,
            1,
            0,
            "On Shinra's rooftop, Rufus stands alone across from Cloud, calm and motionless in his long white coat.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            234,
            36,
            4,
            0,
            "The others pile into a display truck while Cloud starts a red motorcycle. The truck smashes through the showroom glass, and both vehicles race down the building's stairs onto the expressway.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            226,
            0,
            0,
            11,
            "At dawn, the party gathers at the broken highway's end, with open land stretching beyond Midgar.")
    ];

    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateKalmThroughLowerJunonDescriptions() =>
    [
        new(
            332,
            5,
            3,
            238,
            "The upstairs room fades away as Cloud's story becomes a memory from five years earlier."),
        new(
            277,
            4,
            1,
            0,
            "Inside a swaying Shinra truck, sixteen-year-old Cloud rides through heavy rain beside Sephiroth and two masked infantrymen.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            279,
            2,
            1,
            4,
            "Cloud, Sephiroth, and two infantrymen arrive outside the misty mountain town of Nibelheim.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            282,
            8,
            1,
            48,
            "Tifa arrives as their guide, wearing a wide-brimmed cowboy hat, boots, and a short skirt.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            282,
            11,
            13,
            32,
            "The photographer snaps a picture of Tifa standing between Cloud and Sephiroth.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            311,
            0,
            0,
            207,
            "Jagged peaks and deep ravines surround Mt. Nibel as the group climbs toward the reactor high on the mountainside.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            312,
            10,
            3,
            106,
            "The rope bridge tears loose. Tifa, Cloud, Sephiroth, and the two infantrymen plunge into the ravine.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            313,
            0,
            0,
            50,
            "Cloud, Sephiroth, Tifa, and one infantryman regroup on a rocky ledge below the broken bridge."),
        new(
            318,
            8,
            3,
            26,
            "The cavern opens around a luminous turquoise Mako spring, with glowing energy streaming through the rock.",
            FieldOpcodeAddressResolver.OpcodeSplitIndex),
        new(
            323,
            8,
            1,
            48,
            "Cloud peers through the pod's small window and recoils from a malformed human shape suspended inside.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            323,
            9,
            7,
            236,
            "A metal pod bursts open, spilling a twisted human-shaped creature onto the reactor floor.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            332,
            5,
            4,
            3,
            "The memory pauses. Back in the Kalm inn, Cloud's companions sit around him as he continues the story.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            304,
            0,
            0,
            66,
            "In the mansion basement, Sephiroth sits alone at a circular library desk, reading research notes by lamplight."),
        new(
            290,
            1,
            1,
            4,
            "Nibelheim is ablaze. Flames pour from the houses as injured villagers lie across the square.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex),
        new(
            292,
            1,
            1,
            22,
            "Framed by the burning town, Sephiroth turns toward Cloud, then walks away through the flames with his sword in hand.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            292,
            2,
            1,
            10,
            "Framed by the burning town, Sephiroth turns toward Cloud, then walks away through the flames with his sword in hand.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            101,
            0,
            0,
            15,
            "The view sweeps across jagged Mt. Nibel toward the reactor, a massive metal structure built into the mountainside.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            322,
            6,
            0,
            100,
            "Tifa kneels beside her gravely injured father on the reactor floor.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index),
        new(
            323,
            7,
            3,
            13,
            "Tifa raises Sephiroth's sword and charges at him.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            323,
            7,
            4,
            0,
            "Sephiroth slashes Tifa and sends her tumbling down the reactor steps.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),
        new(
            323,
            5,
            15,
            27,
            "Cloud rushes to the injured Tifa and kneels beside her.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),
        new(
            327,
            0,
            0,
            290,
            "Sephiroth tears away the metal figure covering Jenova's chamber. Cloud confronts him beneath the exposed form.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            332,
            4,
            0,
            85,
            "The flashback ends. Back at the Kalm inn, Cloud sits with the others, unable to remember how the confrontation ended.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            343,
            9,
            1,
            24,
            "Four yellow chocobos line up and perform a lively synchronized dance.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            348,
            0,
            0,
            13,
            "A gigantic Midgar Zolom hangs impaled high on a dead tree, its body twisted around the trunk.",
            FieldOpcodeAddressResolver.OpcodeSplitIndex),
        new(
            349,
            0,
            0,
            99,
            "In the mine, Rude blocks the passage while Elena and Tseng stand behind him in dark blue Turk suits.",
            FieldOpcodeAddressResolver.OpcodeRequestIndex),
        new(
            428,
            5,
            0,
            142,
            "The party enters Lower Junon, a dim fishing village beneath the towering Shinra fortress.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            429,
            2,
            0,
            117,
            "A flying sea creature snatches Priscilla from the shore and drags her toward the water.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(
            434,
            1,
            0,
            9,
            "After the fight, Priscilla lies motionless on the wet beach while the party gathers around her.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),
        new(
            359,
            0,
            0,
            79,
            "From an industrial bay, the view sweeps across Junon's vast cliffside Mako cannon, ribbed tower, red-bannered armor, stairways, and platforms above the sea. The story continues automatically when the panorama ends.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex)
    ];

    /// <summary>
    /// Upper Junon through the cargo-ship engine room. Movie cues anchor on the
    /// native playMovie opcode, matching the field 359 entry already here.
    /// </summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateUpperJunonThroughCargoShipDescriptions() =>
    [
        // junair/dir Script 3 is the airport lift toggle. box0/Init adds a
        // +624 display offset only while Bank 1[226] bit 6 is clear, placing
        // the lift on the raised airfield level. Movie 13 clears the bit and
        // raises the lift; movie 14 sets it and lowers the lift. The native
        // filename table independently identifies them as junair_u and
        // junair_d.
        new(
            384,
            0,
            3,
            73,
            "A massive stone-tiled airport lift rises from the pit, red edge lights glowing as it exposes the dark industrial shaft below.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            384,
            0,
            3,
            201,
            "The massive airport lift descends back into the pit until its tiled surface lies flush with the airfield.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // junair2/dir Main plays movie 38 during the first visit, when the
        // story moment is 400. The executable's movie pointer table identifies
        // movie 38 as hiwind0.avi.
        new(
            385,
            0,
            0,
            136,
            "Cloud climbs a ladder beneath the huge, balloon-backed Highwind; the view cuts to a smaller, sleek aircraft hovering over the airfield against the orange-purple sunset.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // junele2/direct Script 1 is called by produce/Main on field entry and
        // Script 2 by border2/Go on the way out. The executable's movie table
        // maps them to junelein and junelego. Frame inspection shows the open
        // platform rising toward the camera in the first and rising away
        // through the overhead opening in the second; no doors are visible.
        new(
            391,
            2,
            1,
            5,
            "A hazard-striped metal lift rises through a dark, pipe-lined circular shaft, orange light glowing beneath it beside a green-lit opening in the wall.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            391,
            2,
            2,
            19,
            "Viewed from below, the hazard-striped lift rises away through the dark, pipe-lined shaft and disappears through the opening overhead, leaving a green wall light below.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // junin7/direct Script 2 is started on field entry and Script 1 from
        // border1/Go on exit. The movie table maps them to junin_in and
        // junin_go. Direct frame inspection shows the same industrial
        // platform and glowing CAUTION sign: it rises into view on entry and
        // descends into the orange-lit shaft on exit.
        new(
            395,
            3,
            2,
            8,
            "A hazard-striped platform rises into view in a dark circular shaft beneath a glowing CAUTION sign; red indicators shine as pale vapor floods the chamber.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            395,
            3,
            1,
            19,
            "The hazard-striped platform descends into the dark shaft beneath the glowing CAUTION sign as orange light swells from the pit below.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // shpin_3/ELINE Go 1x. Byte 93 is the first of three entityExecuteSync
        // calls into entity 8, CEFIROS. The line before it, dialog 5, is spoken
        // text the reader already delivers, so this cue carries only the sight.
        new(
            440,
            15,
            5,
            93,
            "Sephiroth rises through the floor, silver hair trailing over his long black coat.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // Bytes 155, 188 and 213 are three identical green fades before
        // startBattle 488. Only the first is cued; the other two would repeat it.
        new(
            440,
            15,
            5,
            155,
            "Sephiroth flies past. Cloud falls amid green flashes.",
            FieldOpcodeAddressResolver.OpcodeFadeIndex)
    ];

    /// <summary>
    /// Costa del Sol, Corel and Gold Saucer arrival actions reviewed against
    /// gameplay footage and exact installed field instructions. ViddyScribe
    /// drafts and corrections are recorded in the accompanying analysis.
    /// </summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateCorelJourneyVisualDescriptions() =>
    [
        new(449, 5, 9, 0,
            "Cloud and his companions gather around Hojo on the beach.",
            FieldOpcodeAddressResolver.OpcodeSplitIndex),
        new(449, 12, 14, 149,
            "Hojo steps back and turns away from the group.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),
        new(464, 9, 5, 151,
            "The railway bridge lowers into place.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),
        // The native landing finishes before this WAIT. The next phase waits
        // indefinitely for a fresh OK/Cancel edge, then LADER requires Up.
        new(463, 0, 0, 66,
            "Cloud hangs below the tracks. Press OK, then hold Up to climb back."),
        new(450, 13, 4, 13,
            "A townsman punches Barret, knocking him down."),
        new(469, 3, 0, 34,
            "A flashback shows wooden houses along Corel's busy streets."),
        new(483, 2, 0, 6,
            "Barret, Dyne, Scarlet and villagers gather in a small room lined with shelves."),
        new(470, 3, 0, 53,
            "Flames engulf Corel's wooden houses."),
        new(457, 2, 3, 109,
            "The blue cable car pulls away from the station and climbs along the cables.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(457, 2, 4, 16,
            "The blue cable car pulls away from the station and climbs along the cables.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(496, 0, 0, 190,
            "The cable car glides above the clouds toward the Gold Saucer. Huge golden platforms glow with lights, rides and towering attractions as the car approaches the neon entrance.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(496, 0, 0, 201,
            "The cable car docks inside a brightly colored station decorated with giant cartoon figures.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex)
    ];

    /// <summary>Reviewed Junon, cargo-ship and Costa del Sol arrival actions.</summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateJunonJourneyVisualDescriptions() =>
    [
        // The uniform model becomes visible immediately before its scripted walk
        // out of the lockers. This is not the repeating idle/field-entry VISI.
        new(387, 17, 3, 17,
            "Cloud steps out in a blue Shinra uniform and helmet, carrying a rifle.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),
        new(361, 14, 9, 93,
            "Heidegger swings his arms among the scattered soldiers, then advances on Cloud.",
            FieldOpcodeAddressResolver.OpcodeAnime1Index),
        new(361, 5, 1, 50,
            "The soldiers run off, leaving Cloud behind with the captain.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),
        // The demonstration after choosing the finishing move; no added speech
        // during the later timed button-press performance.
        new(387, 17, 16, 32,
            "Cloud twirls his rifle and finishes in a pose.",
            FieldOpcodeAddressResolver.OpcodeAnime1Index),
        // SCR2DL at byte 23 starts the pan. This next request runs as it begins.
        new(382, 3, 0, 32,
            "The view pans down the ship to its open cargo ramp and the soldiers waiting on the dock.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(382, 19, 11, 34,
            "Heidegger advances with raised arms. The captain and nearby soldiers recoil.",
            FieldOpcodeAddressResolver.OpcodeAnime1Index),
        // This loop starts in Red's first Talk script. His Main-script loop also
        // runs offscreen and must never be used as a narration trigger.
        new(436, 14, 1, 135,
            "Red XIII sways awkwardly on two legs inside a sailor's uniform.",
            FieldOpcodeAddressResolver.OpcodeDfanmIndex),
        new(437, 3, 1, 129,
            "Barret strides away from the bridge window and raises his fists.",
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex),
        new(440, 15, 5, 29,
            "A red-uniformed crewman collapses and fades away.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // After FADEW at 298, before party-specific reactions. Names and poses
        // vary with party composition, so describe only the visible object.
        new(440, 15, 5, 299,
            "A severed arm lies on the deck between Cloud and his companions.",
            FieldOpcodeAddressResolver.OpcodeWaitIndex),
        // Cloud is revealed after the other companions have disembarked.
        new(441, 7, 6, 15,
            "The party gathers on a sunlit quay beside the cargo ship. A red seaplane floats nearby.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),
        // del12 is the separate Rufus/Heidegger dock scene, not arrival field441.
        new(442, 9, 3, 33,
            "A helicopter sweeps over the dock toward the helipad.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex),
        new(442, 8, 11, 17,
            "Heidegger knocks two sailors off the dock into the water.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        new(442, 9, 4, 45,
            "The helicopter lifts off, leaving Heidegger on the dock.",
            FieldOpcodeAddressResolver.OpcodeWaitIndex)
    ];
}

/// <param name="RecurringGroup">
/// Non-empty for a cue that belongs to a minigame action the player repeats without
/// leaving the room. A story action happens once per visit and is deduped for the
/// whole visit, which is right for it and wrong for a machine that can be played
/// again: the second play would be silent. Every cue in a group is released for
/// description again when that group's own opening anchor runs again, which is the
/// native re-entry into the script rather than a timer or a guess.
/// </param>
/// <param name="StartsRecurringGroup">
/// True on the one anchor whose native execution begins the repeatable action.
/// </param>
public readonly record struct FieldCutsceneDescriptionCue(
    int FieldId,
    int EntityId,
    int ScriptId,
    int ByteIndex,
    string Text,
    int Opcode = FieldOpcodeAddressResolver.OpcodeWaitIndex,
    string RecurringGroup = "",
    bool StartsRecurringGroup = false)
{
    public FieldCutsceneDescriptionKey Key => new(FieldId, EntityId, ScriptId, ByteIndex);

    public bool IsRecurring => !string.IsNullOrEmpty(RecurringGroup);
}

public readonly record struct FieldCutsceneDescriptionKey(
    int FieldId,
    int EntityId,
    int ScriptId,
    int ByteIndex);
