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
        .. CreateReviewedFilmFallbackDescriptions(),
        .. CreateSetoVisualDescriptions(),
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

    /// <summary>
    /// Spoken descriptions for the reviewed films, at every installed anchor
    /// including their later replays. An independent track falls back to these
    /// when its recording is missing, disabled or refused.
    /// </summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateReviewedFilmFallbackDescriptions() =>
    [
        // Film 3, d_ropein.
        new(457, 1, 0, 85, DRopeinText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 5, u_ropego.
        new(496, 13, 5, 21, URopegoText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 20, mkup.
        new(117, 0, 0, 143, MkupText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(695, 1, 3, 19, MkupText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 21, northmk.
        new(119, 0, 3, 17, NorthmkText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(706, 3, 3, 266, NorthmkText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 22, mk8.
        new(707, 1, 1, 427, Mk8Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(777, 0, 0, 51, Mk8Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 23, ontrain.
        new(137, 0, 3, 452, OntrainText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(706, 3, 3, 243, OntrainText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(708, 2, 3, 21, OntrainText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 24, mainplr.
        new(143, 5, 1, 5, MainplrText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(706, 3, 3, 285, MainplrText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(708, 2, 3, 40, MainplrText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 25, smk.
        new(127, 2, 6, 69, SmkText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(400, 0, 3, 5, SmkText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(763, 0, 0, 714, SmkText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 26, southmk.
        new(127, 2, 7, 84, SouthmkText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(399, 0, 0, 359, SouthmkText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 27, plrexp.
        new(160, 1, 3, 187, PlrexpText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(399, 0, 0, 389, PlrexpText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 28, fallpl.
        new(399, 0, 0, 171, FallplText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 29, monitor.
        new(240, 8, 0, 15, MonitorText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(402, 0, 0, 81, MonitorText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 30, bike.
        new(411, 13, 3, 190, BikeText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 31, mtnvl.
        new(402, 3, 13, 133, MtnvlText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 32, mtnvl2.
        new(416, 0, 3, 5, Mtnvl2Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 33, brgnvl.
        new(356, 11, 11, 260, BrgnvlText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 34, nvlmk.
        new(729, 4, 0, 104, NvlmkText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(730, 11, 3, 90, NvlmkText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 35, nivlsfs.
        new(730, 11, 0, 1218, NivlsfsText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 37, junon.
        new(725, 8, 4, 34, JunonText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 38, hiwind0.
        new(726, 4, 5, 2, Hiwind0Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 39, mtcrl.
        new(462, 3, 1, 14, MtcrlText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(727, 5, 5, 2, MtcrlText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 41, biskdead.
        new(461, 1, 1, 9, BiskdeadText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(567, 12, 13, 640, BiskdeadText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 42, boogdemo.
        new(643, 1, 3, 29, BoogdemoText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 44, setogake.
        new(569, 14, 3, 105, SetogakeText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 45, rcktfail.
        new(569, 3, 3, 130, RcktfailText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 46, jairofly.
        new(774, 14, 3, 102, JairoflyText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 47, jairofal.
        new(87, 0, 0, 34, JairofalText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(637, 2, 9, 239, JairofalText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 48, gold7.
        new(347, 0, 0, 74, Gold7Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(489, 0, 0, 538, Gold7Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 49, gold7_2.
        new(347, 0, 0, 125, Gold72Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(489, 0, 0, 686, Gold72Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(489, 0, 0, 759, Gold72Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(489, 0, 0, 832, Gold72Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(489, 0, 0, 905, Gold72Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 50, earithdd.
        new(67, 2, 3, 42, EarithddText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(647, 0, 0, 229, EarithddText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 51, funeral.
        new(67, 2, 3, 83, FuneralText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(641, 0, 0, 80, FuneralText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 52, car_1209.
        new(779, 3, 0, 64, Car1209Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 54, greatpit.
        new(68, 4, 2, 542, GreatpitText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 55, c_scene1.
        new(67, 1, 0, 40, CScene1Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 56, c_scene2.
        new(643, 3, 3, 79, CScene2Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 57, c_scene3.
        new(639, 0, 0, 23, CScene3Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 58, biglight.
        new(639, 0, 0, 35, BiglightText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 59, meteosky.
        new(67, 2, 3, 30, MeteoskyText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // Film 60, weapon0.
        new(269, 1, 2, 87, Weapon0Text, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // The sites below were found by following the script call graph rather than
        // by looking for a PMVIE above the MOVIE: here the film is prepared and
        // played by two separate one-opcode scripts that a director script requests
        // in turn. Films 30 and 44 get their first play this way; the anchors that
        // shipped before were both replays.
        new(543, 13, 6, 0, BoogdemoText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(550, 14, 4, 0, SetogakeText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(742, 13, 4, 0, JairoflyText, FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(236, 11, 5, 0, Car1209Text, FieldOpcodeAddressResolver.OpcodeMovieIndex)
    ];

    private const string DRopeinText =
        "A blue cable car approaches the rocky station. It docks. " +
        "Propellers slow, and vapor jets beside it.";

    private const string URopegoText =
        "A cable car waits in a monster's mouth. It reverses into darkness " +
        "as fireworks burst above.";

    private const string MkupText =
        "The North Gate stands open. A huge green reactor tower looms " +
        "above, wreathed in vapor.";

    private const string NorthmkText =
        "Blue arcs flicker across the reactor. An orange fireball erupts " +
        "above Midgar. The circular city recedes below. The fireball " +
        "shrinks into smoke.";

    private const string Mk8Text =
        "A fireball bursts through the passage.";

    private const string OntrainText =
        "Cloud jumps onto the train and crouches. The train enters a lit " +
        "tunnel. Empty tracks lead into the tunnel.";

    private const string MainplrText =
        "A black train rushes past on elevated tracks. It winds around an " +
        "enormous steel-braced pillar beneath the city's plate.";

    private const string SmkText =
        "Sparks split the catwalk.";

    private const string SouthmkText =
        "Cloud hangs from the broken catwalk. Flames erupt. Barret crouches " +
        "over Tifa. Cloud tumbles past enormous pipes and disappears into " +
        "mist.";

    private const string PlrexpText =
        "A circular platform surrounds the pillar. Explosions tear holes in " +
        "the pillar. Fiery chunks fall away. Dust and debris surround the " +
        "platform far below.";

    private const string FallplText =
        "Steel supports stretch beneath Midgar's plate. Explosions race up " +
        "the pillar. Fiery debris rains down. A television turns to static. " +
        "The room darkens. The huge plate plunges. Lights go out. People " +
        "flee through an alley, debris billowing behind. Between two green " +
        "towers, Sector 7 burns beneath clouds of smoke. President Shinra " +
        "watches the destruction from above. The view rises along the " +
        "green-lit Shinra tower.";

    private const string MonitorText =
        "A guard sits before surveillance monitors. One shows elevator " +
        "doors on floor sixty.";

    private const string BikeText =
        "Tifa, Aeris, Red XIII and Barret turn. Cloud straddles a large " +
        "black motorcycle. Tifa stands beside a turquoise truck. Cloud " +
        "rides through shattered glass into the hall. He skids around the " +
        "truck as the others board. The truck crashes through glass. Both " +
        "vehicles descend to the lower level. Cloud speeds through a " +
        "doorway, the truck following. Tifa drives, with Aeris beside her. " +
        "They burst through glass onto the raised roadway. Motorcycle and " +
        "truck speed away along the night highway.";

    private const string MtnvlText =
        "Jagged black spires rise beneath an ochre sky. A reactor nestles " +
        "between peaks. Suspension bridges span the gaps. Mist drifts " +
        "across a barren stone canyon.";

    private const string Mtnvl2Text =
        "Misty crevices and bare cliffs pass below. The view approaches a " +
        "metal reactor wedged between pointed rock walls.";

    private const string BrgnvlText =
        "A plank bridge twists above the canyon. Boards split. The bridge's " +
        "center gives way. Broken planks hang against the cliff, shedding " +
        "splinters into mist.";

    private const string NvlmkText =
        "Green pods line a red-lit chamber. A monstrous face peers through " +
        "a porthole. One pod vents steam. A thin blue-gray creature " +
        "emerges, with a spiky head and long claws. Steam drifts around it " +
        "as the view retreats behind girders.";

    private const string NivlsfsText =
        "Sephiroth raises his head, green eyes fixed ahead, faintly " +
        "smiling. He turns away, his long blade at his side. Silver hair " +
        "flowing, he walks into the towering flames.";

    private const string JunonText =
        "An industrial passage opens onto an orange sunset. A massive " +
        "cannon looms outside. Bronze fortifications and red banners line " +
        "the sea cliffs. The immense cannon projects from the fortress over " +
        "dark water.";

    private const string Hiwind0Text =
        "Cloud climbs a ladder up the metal tower. A huge gray airship " +
        "towers above him, with broad wings and powerful engines. It hangs " +
        "moored above the airfield, lights blinking against pink clouds.";

    private const string MtcrlText =
        "Timber crossbeams rush past through a deep passage.";

    private const string BiskdeadText =
        "Impacts chip the cliff face, scattering dust and rock.";

    private const string BoogdemoText =
        "A yellow comet crosses a star field marked with blue grid lines. " +
        "Red orbital paths curve past a cracked, glowing rocky body. " +
        "Planets circle along red paths against a distant galaxy. Rock " +
        "fragments tumble toward a dark vortex ringed with violet light.";

    private const string SetogakeText =
        "Petrified Seto stands beneath an orange moon. Spears pierce his " +
        "stone back above his lowered head.";

    private const string RcktfailText =
        "The rocket rises slightly on fiery engines. Support arms fall " +
        "away. The flames die. Smoke rolls across the forest. The rocket " +
        "tips sideways and remains leaning against its framework. The " +
        "rusted rocket now looms above village rooftops.";

    private const string JairoflyText =
        "A pink propeller plane lifts from the grass, kicking up dust. It " +
        "banks around the leaning rocket above the village. The plane " +
        "sweeps close past the rocket's high framework. It swoops over " +
        "rooftops, then passes overhead. Projectiles strike. The plane " +
        "trails fire and smoke toward the coast.";

    private const string JairofalText =
        "Trailing black smoke, the plane descends over the sea. It strikes " +
        "the water in white spray. It remains afloat, trailing smoke.";

    private const string Gold7Text =
        "Fireworks blossom above the Gold Saucer's golden towers. A gondola " +
        "glides high above the glittering park. Colored sparks burst and " +
        "trail across the dark sky.";

    private const string Gold72Text =
        "The Gold Saucer rises from clouds, golden platforms circled by " +
        "green tracks. Searchlights sweep beneath bursts of pink, green, " +
        "white and purple fireworks.";

    private const string EarithddText =
        "Aeris kneels in prayer, then opens her eyes. Cloud watches her, " +
        "his expression serious. She lifts her head and smiles. Sephiroth " +
        "plunges from above, his long sword pointed downward. The blade " +
        "pierces Aeris from behind. Her head bows. Her eyes close. " +
        "Sephiroth smiles faintly, then withdraws the blade. Aeris slumps. " +
        "Her ribbon loosens, releasing a glowing pale green orb. The orb " +
        "spins as it falls. It bounces down stone steps, then drops over " +
        "the edge. It falls past towering platforms beneath a swirling " +
        "column of light. The orb splashes into the water below.";

    private const string FuneralText =
        "Cloud supports Aeris on her back, her hands folded across her " +
        "chest. Head bowed, he gently lowers her into the blue water. She " +
        "sinks through shafts of light, her loose hair drifting. Her arms " +
        "float apart as she recedes into the depths.";

    private const string Car1209Text =
        "A title: Shinra Electric Power Company Motor Mobiles. A " +
        "streamlined silver open-top car rotates beside columns of " +
        "specifications. A brass three-wheeler turns, displaying exposed " +
        "pipes, round headlights and red wheel rims. An enclosed vintage " +
        "car rotates, with gold fittings, large lamps and curved exhausts. " +
        "Its body vanishes, revealing the chassis. Labels: Packaging, Power " +
        "Unit, Footwork. An engine glows green. Text: Mako Engine, produced " +
        "by Shinra. A wheel and suspension diagram appears, labeled Shinra " +
        "suspension system, S S wishbone. The Shinra emblem appears beside " +
        "a model lineup: new model S five ten. A Japanese dealer list " +
        "appears. Welcome to Shinra M M.";

    private const string GreatpitText =
        "A snowy crater rim stretches beneath green auroras. Turquoise " +
        "energy rises from its center, wrapped in spiraling white bands. " +
        "Glowing particles stream up through the column. The vast circular " +
        "crater recedes among snow-covered mountains.";

    private const string CScene1Text =
        "Blue light streaks the cavern walls. Tangled roots suspend a " +
        "turquoise crystal overhead. The roots shudder. White fragments " +
        "cascade down.";

    private const string CScene2Text =
        "Dust rises beneath tangled roots. Rocks tumble onto the ledge. " +
        "Sephiroth floats motionless inside a blue crystal.";

    private const string CScene3Text =
        "A gloved hand places a purple orb inside the crystal. It floats " +
        "beside Sephiroth's motionless body. Blue tendrils coil around him " +
        "and the orb.";

    private const string BiglightText =
        "An airship turns away from a huge white column above the crater. " +
        "Monstrous faces stir in darkness, their eyes glowing. Ice shifts. " +
        "A gigantic claw grips the rim. An armored creature rises, red eyes " +
        "glowing. The party watches from the airship's deck. A towering " +
        "fanged beast stretches its arms, a pink core glowing in its chest. " +
        "Blue rings flare around another armored creature. Tifa shields her " +
        "face and falls onto the deck. Barret braces himself against the " +
        "railing. A winged beast rises through swirling blue energy. Blue " +
        "streaks shoot skyward as the airship escapes. The airship recedes " +
        "into the starry sky.";

    private const string MeteoskyText =
        "Window shutters rise beside an operating table. A huge fiery red " +
        "orb hangs in orange clouds beside a smaller sphere. It looms above " +
        "Junon's fortress and cannon.";

    private const string Weapon0Text =
        "Red banners hang over an industrial roadway. Road panels lift, " +
        "exposing huge gears beneath a wall marked Junon. Gears and " +
        "hydraulic pistons turn the massive cannon. The fortress cannon " +
        "levels toward the sea.";

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
            Mk8Text,
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
            "A black train rushes past on elevated tracks. It winds around an " +
            "enormous steel-braced pillar beneath the city's plate.",
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
            FallplText,
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
        // This anchor is the first play of bike.avi, which now has its own recording
        // and a paragraph written from the footage. The paragraph that used to sit
        // here was written before anyone had seen the film and called the motorcycle
        // red; it is black.
        new(
            234,
            36,
            4,
            0,
            BikeText,
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
            "Jagged black spires rise beneath an ochre sky. A reactor nestles " +
            "between peaks. Suspension bridges span the gaps. Mist drifts " +
            "across a barren stone canyon.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            312,
            10,
            3,
            106,
            "A plank bridge twists above the canyon. Boards split. The bridge's " +
            "center gives way. Broken planks hang against the cliff, shedding " +
            "splinters into mist.",
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
            "Green pods line a red-lit chamber. A monstrous face peers through " +
            "a porthole. One pod vents steam. A thin blue-gray creature " +
            "emerges, with a spiky head and long claws. Steam drifts around it " +
            "as the view retreats behind girders.",
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
            "Sephiroth raises his head, green eyes fixed ahead, faintly " +
            "smiling. He turns away, his long blade at his side. Silver hair " +
            "flowing, he walks into the towering flames.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            292,
            2,
            1,
            10,
            "Sephiroth raises his head, green eyes fixed ahead, faintly " +
            "smiling. He turns away, his long blade at his side. Silver hair " +
            "flowing, he walks into the towering flames.",
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
            "An industrial passage opens onto an orange sunset. A massive " +
            "cannon looms outside. Bronze fortifications and red banners line " +
            "the sea cliffs. The immense cannon projects from the fortress over " +
            "dark water. The story continues automatically when the panorama " +
            "ends.",
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
            "The huge platform rises to the upper deck.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            384,
            0,
            3,
            201,
            "The huge platform lowers to the airfield.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        // junair2/dir Main plays movie 38 during the first visit, when the
        // story moment is 400. The executable's movie pointer table identifies
        // movie 38 as hiwind0.avi.
        new(
            385,
            0,
            0,
            136,
            "Cloud climbs a ladder up the metal tower. A huge gray airship " +
            "towers above him, with broad wings and powerful engines. It hangs " +
            "moored above the airfield, lights blinking against pink clouds.",
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
            "A striped platform rises through the shaft to a green-lit landing.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            391,
            2,
            2,
            19,
            "The platform leaves the green-lit landing and recedes into the " +
            "shaft.",
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
            "The platform approaches the landing marked Caution. It settles " +
            "amid billowing vapor.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(
            395,
            3,
            1,
            19,
            "The platform leaves the Caution sign behind, moving toward the " +
            "foreground.",
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
            "A blue cable car's propellers spin. It lifts along cables into the " +
            "golden sky.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(457, 2, 4, 16,
            "A blue cable car's propellers spin. It lifts along cables into the " +
            "golden sky.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(496, 0, 0, 190,
            "The cable car glides above the clouds toward the Gold Saucer. Huge golden platforms glow with lights, rides and towering attractions as the car approaches the neon entrance.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex),
        new(496, 0, 0, 201,
            "Lamps light a dark tunnel. The car emerges through a giant " +
            "monster's mouth. Colorful lanterns surround a Welcome sign.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex)
    ];

    /// <summary>Visually reviewed Seto scene actions, anchored to the installed director and actor scripts.</summary>
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateSetoVisualDescriptions() =>
    [
        // The director asks Cloud to leave; script8 then gestures, walks out and hides him.
        new(550, 8, 3, 217,
            "Cloud and his companion leave Bugenhagen alone with Red XIII.",
            FieldOpcodeAddressResolver.OpcodeRequestSwIndex),
        // The spread-arms loop immediately precedes his native 'thinking lately' line.
        new(550, 10, 9, 0,
            "Bugenhagen spreads his arms wide while speaking to Red XIII.",
            FieldOpcodeAddressResolver.OpcodeDfanmIndex),
        // The final request runs Red script13's two jumps, not script3's entrance jumps.
        new(550, 8, 3, 406,
            "Red XIII leaps from one rock ledge to another toward Seto.",
            FieldOpcodeAddressResolver.OpcodeRequestEwIndex),
        // After both jumps, Red raises his head before the camera pans to Seto.
        new(550, 5, 13, 103,
            "Red XIII raises his head upward toward the stone figure above.",
            FieldOpcodeAddressResolver.OpcodeCanm2Index),
        // The first droplet appears (VISI1); KIRAB/KIRAC and the later droplets stay deduplicated.
        new(550, 11, 3, 17,
            "Clear droplets fall from the stone figure's eyes.",
            FieldOpcodeAddressResolver.OpcodeVisibilityIndex)
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
