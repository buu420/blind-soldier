namespace Ff7.Accessibility.Reloaded;

public sealed class FieldCutsceneDescriptionTracker
{
    private readonly Dictionary<FieldCutsceneDescriptionKey, FieldCutsceneDescriptionCue> cues;
    private readonly HashSet<FieldCutsceneDescriptionKey> spoken = [];
    private readonly object sync = new();
    private int currentFieldId = -1;

    public FieldCutsceneDescriptionTracker(IEnumerable<FieldCutsceneDescriptionCue> cues)
    {
        this.cues = cues.ToDictionary(cue => cue.Key);
    }

    public FieldCutsceneDescriptionCue? Observe(FieldScriptContext context)
    {
        lock (sync)
        {
            if (context.FieldId != currentFieldId)
            {
                currentFieldId = context.FieldId;
                spoken.Clear();
            }

            var key = new FieldCutsceneDescriptionKey(
                context.FieldId,
                context.EntityId,
                context.ScriptId,
                context.ByteIndex);
            if (!cues.TryGetValue(key, out var cue) ||
                context.Opcode != cue.Opcode ||
                !spoken.Add(key))
            {
                return null;
            }

            return cue;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            currentFieldId = -1;
            spoken.Clear();
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
        .. CreateKalmThroughLowerJunonDescriptions()
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
            "The view sweeps from Lower Junon up the vast metal fortress to the Mako cannon and airfield above.",
            FieldOpcodeAddressResolver.OpcodeMovieIndex)
    ];
}

public readonly record struct FieldCutsceneDescriptionCue(
    int FieldId,
    int EntityId,
    int ScriptId,
    int ByteIndex,
    string Text,
    int Opcode = FieldOpcodeAddressResolver.OpcodeWaitIndex)
{
    public FieldCutsceneDescriptionKey Key => new(FieldId, EntityId, ScriptId, ByteIndex);
}

public readonly record struct FieldCutsceneDescriptionKey(
    int FieldId,
    int EntityId,
    int ScriptId,
    int ByteIndex);
