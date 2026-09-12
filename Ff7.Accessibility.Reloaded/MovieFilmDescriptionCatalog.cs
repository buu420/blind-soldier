namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// What each described film shows, keyed by the film's own file name.
///
/// <para>A field script names a film by number, and the number means a different
/// film on a different disc: <c>sm_movie.cpp</c> reads films 20 and up out of one of
/// three blocks chosen by the disc byte, which the story sets to 2 at the Forgotten
/// Capital and to 3 at the final dungeon. So one script address is not one film, and
/// a paragraph bound to an address is only correct on the disc it was written for.
/// The file name is the same on every disc, which is why this table is keyed on
/// it.</para>
///
/// <para>The recording that describes a film is named after it:
/// <c>mkup.avi</c> is described by <c>mkup_audio_description.ogg</c>. Whether that
/// recording is installed is a question for the asset folder at run time, not for
/// this table - a film with a paragraph and no recording is described in speech,
/// which is the same fallback every other film uses.</para>
/// </summary>
public static class MovieFilmDescriptionCatalog
{
    /// <summary>The recording that describes <paramref name="filmFileName"/>.</summary>
    public static string RecordingFileName(string filmFileName) =>
        Path.GetFileNameWithoutExtension(filmFileName) + "_audio_description.ogg";

    /// <summary>
    /// The paragraph for a film, or null when nothing has been written for it. Null
    /// means stay quiet: it is the honest answer for a film nobody has reviewed.
    /// </summary>
    public static string? Paragraph(string? filmFileName) =>
        filmFileName is not null && Paragraphs.TryGetValue(filmFileName, out var text) ? text : null;

    /// <summary>Every film with a reviewed description.</summary>
    public static IReadOnlyDictionary<string, string> Paragraphs { get; } = Create();

    private static IReadOnlyDictionary<string, string> Create() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["biglight.avi"] = BiglightText,
        ["bike.avi"] = BikeText,
        ["biskdead.avi"] = BiskdeadText,
        ["boogdemo.avi"] = BoogdemoText,
        ["boogdown.avi"] = BoogdownText,
        ["boogstar.avi"] = BoogstarText,
        ["boogup.avi"] = BoogupText,
        ["brgnvl.avi"] = BrgnvlText,
        ["c_scene1.avi"] = CScene1Text,
        ["c_scene2.avi"] = CScene2Text,
        ["c_scene3.avi"] = CScene3Text,
        ["canon.avi"] = CanonText,
        ["canonh1p.avi"] = Canonh1pText,
        ["canonh3f.avi"] = Canonh3fText,
        ["canonht0.avi"] = Canonht0Text,
        ["canonht1.avi"] = Canonht1Text,
        ["canonht2.avi"] = Canonht2Text,
        ["canonon.avi"] = CanononText,
        ["car_1209.avi"] = Car1209Text,
        ["d_ropego.avi"] = DRopegoText,
        ["d_ropein.avi"] = DRopeinText,
        ["dumcrush.avi"] = DumcrushText,
        ["earithdd.avi"] = EarithddText,
        ["ending1.avi"] = Ending1Text,
        ["ending2.avi"] = Ending2Text,
        ["ending3.avi"] = Ending3Text,
        ["fallpl.avi"] = FallplText,
        ["fcar.avi"] = FcarText,
        ["feelwin0.avi"] = Feelwin0Text,
        ["feelwin1.avi"] = Feelwin1Text,
        ["fship2.avi"] = Fship2Text,
        ["funeral.avi"] = FuneralText,
        ["gelnica.avi"] = GelnicaText,
        ["gold7_2.avi"] = Gold72Text,
        ["gold7.avi"] = Gold7Text,
        ["greatpit.avi"] = GreatpitText,
        ["hiwind0.avi"] = Hiwind0Text,
        ["hwindfly.avi"] = HwindflyText,
        ["hwindjet.avi"] = HwindjetText,
        ["jairofal.avi"] = JairofalText,
        ["jairofly.avi"] = JairoflyText,
        ["jenova_e.avi"] = JenovaEText,
        ["junair_d.avi"] = JunairDText,
        ["junair_u.avi"] = JunairUText,
        ["junelego.avi"] = JunelegoText,
        ["junelein.avi"] = JuneleinText,
        ["junin_go.avi"] = JuninGoText,
        ["junin_in.avi"] = JuninInText,
        ["junon.avi"] = JunonText,
        ["junsea.avi"] = JunseaText,
        ["last4_4.avi"] = Last44Text,
        ["lastflor.avi"] = LastflorText,
        ["lastmap.avi"] = LastmapText,
        ["loslake1.avi"] = Loslake1Text,
        ["lslmv.avi"] = LslmvText,
        ["mainplr.avi"] = MainplrText,
        ["meteofix.avi"] = MeteofixText,
        ["meteosky.avi"] = MeteoskyText,
        ["mk8.avi"] = Mk8Text,
        ["mkup.avi"] = MkupText,
        ["monitor.avi"] = MonitorText,
        ["mtcrl.avi"] = MtcrlText,
        ["mtnvl.avi"] = MtnvlText,
        ["mtnvl2.avi"] = Mtnvl2Text,
        ["nivlsfs.avi"] = NivlsfsText,
        ["northmk.avi"] = NorthmkText,
        ["nrcrl_b.avi"] = NrcrlBText,
        ["nrcrl.avi"] = NrcrlText,
        ["nvlmk.avi"] = NvlmkText,
        ["ontrain.avi"] = OntrainText,
        ["parashot.avi"] = ParashotText,
        ["phoenix.avi"] = PhoenixText,
        ["plrexp.avi"] = PlrexpText,
        ["rckethit0.avi"] = Rckethit0Text,
        ["rckethit1.avi"] = Rckethit1Text,
        ["rcketoff.avi"] = RcketoffText,
        ["rcktfail.avi"] = RcktfailText,
        ["setogake.avi"] = SetogakeText,
        ["smk.avi"] = SmkText,
        ["southmk.avi"] = SouthmkText,
        ["u_ropego.avi"] = URopegoText,
        ["u_ropein.avi"] = URopeinText,
        ["weapon0.avi"] = Weapon0Text,
        ["weapon1.avi"] = Weapon1Text,
        ["weapon2.avi"] = Weapon2Text,
        ["weapon3.avi"] = Weapon3Text,
        ["weapon4.avi"] = Weapon4Text,
        ["weapon5.avi"] = Weapon5Text,
        ["wh2e2.avi"] = Wh2e2Text,
        ["white2.avi"] = White2Text,
        ["zmind01.avi"] = Zmind01Text,
        ["zmind02.avi"] = Zmind02Text,
        ["zmind03.avi"] = Zmind03Text,
    };

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

    private const string BikeText =
        "Tifa, Aeris, Red XIII and Barret turn. Cloud straddles a large " +
        "black motorcycle. Tifa stands beside a turquoise truck. Cloud " +
        "rides through shattered glass into the hall. He skids around the " +
        "truck as the others board. The truck crashes through glass. Both " +
        "vehicles descend to the lower level. Cloud speeds through a " +
        "doorway, the truck following. Tifa drives, with Aeris beside her. " +
        "They burst through glass onto the raised roadway. Motorcycle and " +
        "truck speed away along the night highway.";

    private const string BiskdeadText =
        "Impacts chip the cliff face, scattering dust and rock.";

    private const string BoogdemoText =
        "A yellow comet crosses a star field marked with blue grid lines. " +
        "Red orbital paths curve past a cracked, glowing rocky body. " +
        "Planets circle along red paths against a distant galaxy. Rock " +
        "fragments tumble toward a dark vortex ringed with violet light.";

    private const string BoogdownText =
        "The platform lowers toward the floor.";

    private const string BoogstarText =
        "Colored planets circle a bright light along red orbits, beneath a " +
        "starry dome. The view closes on a blue-green globe. Pink and " +
        "yellow lights rise from its surface, then drift down. Green " +
        "particles spread below. Colored lights swirl around the globe. The " +
        "globe rotates, wrapped in shifting, luminous trails. A thin stream " +
        "of light flows away. The globe darkens. The globe breaks into " +
        "black fragments. Only the starry backdrop remains.";

    private const string BoogupText =
        "A circular platform rises toward the glowing planets.";

    private const string BrgnvlText =
        "A plank bridge twists above the canyon. Boards split. The bridge's " +
        "center gives way. Broken planks hang against the cliff, shedding " +
        "splinters into mist.";

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

    private const string CanonText =
        "Pipes and cylinders cover the massive cannon, mounted high on " +
        "scaffolding. Midgar's reactors surround the central tower. Green " +
        "light and steam flare from the reactors. Glowing streams flow " +
        "along pipes toward the cannon. From above, green spokes converge. " +
        "The city lights go dark. A blue-purple ring swirls before the " +
        "cannon's muzzle. A brilliant circular pulse expands. The cannon " +
        "fires a blue-white beam, recoiling. Sparks shower from the " +
        "machinery. The beam streaks across the sky.";

    private const string Canonh1pText =
        "Golden projectiles streak overland. A blue beam races between " +
        "them.";

    private const string Canonh3fText =
        "Rufus watches golden projectiles approach his office. His blue " +
        "eyes narrow. Flames burst through the windows, engulfing him. The " +
        "tower's summit burns.";

    private const string Canonht0Text =
        "The creature's shoulder armor opens. Its chest glows. Yellow light " +
        "pours out, then golden projectiles launch. They streak over the " +
        "plain.";

    private const string Canonht1Text =
        "The creature's white eyes gleam. The Highwind sweeps overhead. A " +
        "blue-white beam tears through the creature's chest. It topples. An " +
        "enormous clawed hand slumps into the dust.";

    private const string Canonht2Text =
        "A brilliant beam streaks across snowy mountains. A dome covers the " +
        "crater. The beam strikes. The glowing dome collapses.";

    private const string CanononText =
        "Meteor blazes through the clouds. Helicopters sweep past, their " +
        "searchlights shining. They circle Shinra's towers and enormous " +
        "pipework. A gleaming cannon barrel stretches above scaffolds and " +
        "cables. The massive weapon towers over Midgar.";

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

    private const string DRopegoText =
        "A blue cable car's propellers spin. It lifts along cables into the " +
        "golden sky.";

    private const string DRopeinText =
        "A blue cable car approaches the rocky station. It docks. " +
        "Propellers slow, and vapor jets beside it.";

    private const string DumcrushText =
        "Tifa pushes Cloud in his wheelchair. Buildings collapse behind " +
        "them. They face a green glow. The walkway crumbles beneath them. " +
        "They fall into luminous green water. Ripples spread.";

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

    private const string Ending1Text =
        "Cloud tumbles through a dark tunnel streaked with light. He " +
        "plunges through twisting blue and brown vortices. A rocky spiral " +
        "opens into darkness. A golden globe glows among threads of " +
        "starlight. Cloud hurtles headfirst through a blue tunnel, his eyes " +
        "fixed ahead. Sephiroth faces him, bare-chested, holding his long " +
        "sword.";

    private const string Ending2Text =
        "Sephiroth throws his arms wide and disintegrates into red and " +
        "white light. Cloud stands alone in a black void. Green particles " +
        "stream around him. Golden lights swirl away into the darkness. " +
        "Cloud: Lifestream? A slender hand reaches down through brilliant " +
        "light. Cloud reaches toward it. Tifa reaches from a ledge. The " +
        "ledge crumbles. Tifa falls, and Cloud catches her while dangling " +
        "by one arm. Cloud: I think I'm beginning to understand. Tifa: " +
        "What? Cloud: An answer from the Planet... the Promised Land... I " +
        "think I can meet her...there. Tifa: Yeah, let's go meet her. They " +
        "climb onto the ledge above a pit of swirling blue-white energy. " +
        "Cloud: Hey, where is everyone? Barret: Hey! Cloud: I'm glad you're " +
        "all safe! Barret: They all seem to be safe, too. But...now what're " +
        "we going to do? Red Thirteen: Holy should be moving soon, and that " +
        "means this place will... Cid: Oh, Lady Luck don't fail me now... " +
        "They look up as rubble falls. The Highwind bursts through the rock " +
        "ceiling in a cloud of dust. A red-haired woman is painted on its " +
        "hull. A blue-white pillar erupts from the mountain crater beneath " +
        "a blood-red sky. The Highwind sweeps out beside the towering " +
        "energy column. The deck tilts. Cid: Shit! Cid pulls a lever marked " +
        "Emergency. The airship blazes away, trailing a thick white plume " +
        "across the red sky. Wind blows across a town's rooftops. Marlene: " +
        "The flower girl? Marlene opens a window. The curtains billow. " +
        "Meteor looms above distant Midgar. Red twisters descend from " +
        "Meteor and coil around Midgar's towers. Lightning flashes as roofs " +
        "tear away. Debris and explosions fill the streets. A bright point " +
        "appears in the sky. Blue-white light streaks across the city. " +
        "Marlene watches. Meteor presses against a broad ring of blue-white " +
        "light. Meteor grinds against the glowing cushion above Midgar. The " +
        "Highwind flies beside the glow. The party looks out. Barret: Wait " +
        "a damn minute! What's going to happen to Midgar? We can't let that " +
        "happen! Cait Sith: I had everyone take refuge in the slums, but " +
        "the way things are now... Red Thirteen: It's too late for Holy. " +
        "Meteor is approaching the Planet. Holy is having the opposite " +
        "effect. We've gotta worry about the Planet. Cid watches from the " +
        "cockpit. Cloud stands beside Tifa. Sparkling green streams rise " +
        "from cracks in the rock, curling into tendrils. Green spirals " +
        "spread across the dark landscape. Cid: What the hell is that? " +
        "Cloud: Lifestream. Green ribbons stream across the sky. Villagers " +
        "watch from their windows. A yellow flower rests on Marlene's " +
        "windowsill. Countless green ribbons join across the hills. " +
        "Midgar's tower breaks apart. Green streams weave together, flowing " +
        "toward Meteor. The tendrils reach into Meteor beyond the " +
        "blue-white ring. Across the planet's curved surface, green streams " +
        "converge around Meteor's blazing halo. Aeris opens her eyes among " +
        "green lights.";

    private const string Ending3Text =
        "Clouds drift across a rocky canyon. Red Thirteen runs with two red " +
        "cubs, his flaming tail streaming behind. They bound up steep rock " +
        "ledges toward the summit. Red Thirteen turns toward the cubs, then " +
        "raises his head. Birds fly above the ruins of Midgar, now covered " +
        "in thick green vegetation. The Final Fantasy Seven title appears " +
        "over its green meteor emblem.";

    private const string FallplText =
        "Steel supports stretch beneath Midgar's plate. Explosions race up " +
        "the pillar. Fiery debris rains down. A television turns to static. " +
        "The room darkens. The huge plate plunges. Lights go out. People " +
        "flee through an alley, debris billowing behind. Between two green " +
        "towers, Sector 7 burns beneath clouds of smoke. President Shinra " +
        "watches the destruction from above. The view rises along the " +
        "green-lit Shinra tower.";

    private const string FcarText =
        "Shinra Electric Power Company presents Motor Mobiles. A silver " +
        "open race car rotates beside a list of specifications. A bronze " +
        "three-wheeled buggy has an open seat and exposed pipes. A bulky " +
        "black sedan turns, with rounded fenders and gold trim. Cutaway " +
        "views reveal the engine and chassis beneath the body. A green " +
        "cylinder is labeled Makou Engine. A diagram shows the suspension " +
        "assembly. The advertisement names the new S five-ten, then lists " +
        "Japanese dealerships.";

    private const string Feelwin0Text =
        "A huge armored creature looms, its chest glowing red. It turns, " +
        "white eyes shining above its red mouth. A long blade-like crest " +
        "runs down its back.";

    private const string Feelwin1Text =
        "Huge clawed feet pound the scrubland. The creature strides away, " +
        "stirring dust.";

    private const string Fship2Text =
        "Pale clouds stream across a gray-blue sky.";

    private const string FuneralText =
        "Cloud supports Aeris on her back, her hands folded across her " +
        "chest. Head bowed, he gently lowers her into the blue water. She " +
        "sinks through shafts of light, her loose hair drifting. Her arms " +
        "float apart as she recedes into the depths.";

    private const string GelnicaText =
        "A camouflage aircraft waits on an elevated runway. A turntable " +
        "rotates it toward the runway. Its propellers spin as it " +
        "accelerates. It lifts into the orange sky.";

    private const string Gold72Text =
        "The Gold Saucer rises from clouds, golden platforms circled by " +
        "green tracks. Searchlights sweep beneath bursts of pink, green, " +
        "white and purple fireworks.";

    private const string Gold7Text =
        "Fireworks blossom above the Gold Saucer's golden towers. A gondola " +
        "glides high above the glittering park. Colored sparks burst and " +
        "trail across the dark sky.";

    private const string GreatpitText =
        "A snowy crater rim stretches beneath green auroras. Turquoise " +
        "energy rises from its center, wrapped in spiraling white bands. " +
        "Glowing particles stream up through the column. The vast circular " +
        "crater recedes among snow-covered mountains.";

    private const string Hiwind0Text =
        "Cloud climbs a ladder up the metal tower. A huge gray airship " +
        "towers above him, with broad wings and powerful engines. It hangs " +
        "moored above the airfield, lights blinking against pink clouds.";

    private const string HwindflyText =
        "A dark cannon points over the sea at sunset. A silver airship " +
        "sweeps alongside it. A rope drops from the airship's railing. Tifa " +
        "grabs the rope and swings over the water. The Highwind climbs into " +
        "the orange sky. It flies away from Junon.";

    private const string HwindjetText =
        "Panels open, extending engines from the Highwind. White jets " +
        "flare. The airship races toward the golden horizon.";

    private const string JairofalText =
        "Trailing black smoke, the plane descends over the sea. It strikes " +
        "the water in white spray. It remains afloat, trailing smoke.";

    private const string JairoflyText =
        "A pink propeller plane lifts from the grass, kicking up dust. It " +
        "banks around the leaning rocket above the village. The plane " +
        "sweeps close past the rocket's high framework. It swoops over " +
        "rooftops, then passes overhead. Projectiles strike. The plane " +
        "trails fire and smoke toward the coast.";

    private const string JenovaEText =
        "Sephiroth: But they... Those worthless creatures are stealing the " +
        "planet from Mother. But now I'm here with you, so don't worry. " +
        "Sephiroth tears the metallic figure loose. Cables strain and " +
        "spark. The figure falls against a pipe. A tank glows blue. Behind " +
        "glass, a pale blue being hangs among red flesh and cables, its " +
        "headgear labeled Jenova.";

    private const string JunairDText =
        "The huge platform lowers to the airfield.";

    private const string JunairUText =
        "The huge platform rises to the upper deck.";

    private const string JunelegoText =
        "The platform leaves the green-lit landing and recedes into the " +
        "shaft.";

    private const string JuneleinText =
        "A striped platform rises through the shaft to a green-lit landing.";

    private const string JuninGoText =
        "The platform leaves the Caution sign behind, moving toward the " +
        "foreground.";

    private const string JuninInText =
        "The platform approaches the landing marked Caution. It settles " +
        "amid billowing vapor.";

    private const string JunonText =
        "An industrial passage opens onto an orange sunset. A massive " +
        "cannon looms outside. Bronze fortifications and red banners line " +
        "the sea cliffs. The immense cannon projects from the fortress over " +
        "dark water.";

    private const string JunseaText =
        "Dark sea beneath an orange sunset.";

    private const string Last44Text =
        "Stone platforms hang before an emerald curtain. The blocks rise, " +
        "break apart, and scatter. Green streaks rush toward a distant " +
        "point of light. White light flares.";

    private const string LastflorText =
        "Red rocks surround a blue-white core. Colors spiral inward.";

    private const string LastmapText =
        "Glowing green patterns slowly rotate against the darkness.";

    private const string Loslake1Text =
        "Ancient stone ruins surround a basin. A slender harp rises in " +
        "white light. Behind it, a stone cylinder sinks into the platform.";

    private const string LslmvText =
        "Shining water pours through the stone arches. A shimmering curtain " +
        "towers over the ruins.";

    private const string MainplrText =
        "A black train rushes past on elevated tracks. It winds around an " +
        "enormous steel-braced pillar beneath the city's plate.";

    private const string MeteofixText =
        "Meteor burns red against the stars. Blue lightning joins floating " +
        "rock fragments to its shattered core. The broken mass still hangs " +
        "above the planet.";

    private const string MeteoskyText =
        "Window shutters rise beside an operating table. A huge fiery red " +
        "orb hangs in orange clouds beside a smaller sphere. It looms above " +
        "Junon's fortress and cannon.";

    private const string Mk8Text =
        "A fireball bursts through the passage.";

    private const string MkupText =
        "The North Gate stands open. A huge green reactor tower looms " +
        "above, wreathed in vapor.";

    private const string MonitorText =
        "A guard sits before surveillance monitors. One shows elevator " +
        "doors on floor sixty.";

    private const string MtcrlText =
        "Timber crossbeams rush past through a deep passage.";

    private const string MtnvlText =
        "Jagged black spires rise beneath an ochre sky. A reactor nestles " +
        "between peaks. Suspension bridges span the gaps. Mist drifts " +
        "across a barren stone canyon.";

    private const string Mtnvl2Text =
        "Misty crevices and bare cliffs pass below. The view approaches a " +
        "metal reactor wedged between pointed rock walls.";

    private const string NivlsfsText =
        "Sephiroth raises his head, green eyes fixed ahead, faintly " +
        "smiling. He turns away, his long blade at his side. Silver hair " +
        "flowing, he walks into the towering flames.";

    private const string NorthmkText =
        "Blue arcs flicker across the reactor. An orange fireball erupts " +
        "above Midgar. The circular city recedes below. The fireball " +
        "shrinks into smoke.";

    private const string NrcrlBText =
        "A locomotive approaches North Corel. It stops behind the barrier.";

    private const string NrcrlText =
        "A locomotive rushes toward North Corel. It smashes the barrier " +
        "amid flames. The train crashes through buildings, scattering " +
        "debris.";

    private const string NvlmkText =
        "Green pods line a red-lit chamber. A monstrous face peers through " +
        "a porthole. One pod vents steam. A thin blue-gray creature " +
        "emerges, with a spiky head and long claws. Steam drifts around it " +
        "as the view retreats behind girders.";

    private const string OntrainText =
        "Cloud jumps onto the train and crouches. The train enters a lit " +
        "tunnel. Empty tracks lead into the tunnel.";

    private const string ParashotText =
        "The Highwind passes beneath the vast, fiery Meteor. Cloud and his " +
        "companions leap from the airship. Midgar's circular city and " +
        "towering cannon lie below. Pale parachutes descend through the " +
        "scaffolding. Factory walls rush past.";

    private const string PhoenixText =
        "A huge brown condor shelters a glowing egg. A violet sphere " +
        "surrounds them. Flames engulf the bird. The condor falls, " +
        "scattering feathers. The egg remains.";

    private const string PlrexpText =
        "A circular platform surrounds the pillar. Explosions tear holes in " +
        "the pillar. Fiery chunks fall away. Dust and debris surround the " +
        "platform far below.";

    private const string Rckethit0Text =
        "Stars streak past the rocket. A round escape pod emerges from a " +
        "hatch and tumbles away. Its rear framework breaks off in sparks.";

    private const string Rckethit1Text =
        "The escape pod drifts above the blue planet. The rocket speeds " +
        "onward, its boosters blazing. It dwindles against Meteor's " +
        "immense, fiery surface.";

    private const string RcketoffText =
        "Steam vents around the rocket. The countdown reaches zero. Engines " +
        "blaze white. Exhaust blasts through the town. The rocket lifts " +
        "away from its launch tower. It passes above rooftops and a " +
        "swinging inn sign. A thick white plume trails across the blue sky. " +
        "Below the climbing rocket, smoke spreads in a ring.";

    private const string RcktfailText =
        "The rocket rises slightly on fiery engines. Support arms fall " +
        "away. The flames die. Smoke rolls across the forest. The rocket " +
        "tips sideways and remains leaning against its framework. The " +
        "rusted rocket now looms above village rooftops.";

    private const string SetogakeText =
        "Petrified Seto stands beneath an orange moon. Spears pierce his " +
        "stone back above his lowered head.";

    private const string SmkText =
        "Sparks split the catwalk.";

    private const string SouthmkText =
        "Cloud hangs from the broken catwalk. Flames erupt. Barret crouches " +
        "over Tifa. Cloud tumbles past enormous pipes and disappears into " +
        "mist.";

    private const string URopegoText =
        "A cable car waits in a monster's mouth. It reverses into darkness " +
        "as fireworks burst above.";

    private const string URopeinText =
        "Lamps light a dark tunnel. The car emerges through a giant " +
        "monster's mouth. Colorful lanterns surround a Welcome sign.";

    private const string Weapon0Text =
        "Red banners hang over an industrial roadway. Road panels lift, " +
        "exposing huge gears beneath a wall marked Junon. Gears and " +
        "hydraulic pistons turn the massive cannon. The fortress cannon " +
        "levels toward the sea.";

    private const string Weapon1Text =
        "Junon's cannon fires a brilliant flash. A glowing shell streaks " +
        "over the ocean. A white ring spreads across the water and fades.";

    private const string Weapon2Text =
        "A huge shadow moves beneath the water. A purple, spiked creature " +
        "bursts through the foam. Soldiers raise launchers. Streams of " +
        "gunfire arc over the sea. Shells splash around the creature.";

    private const string Weapon3Text =
        "Soldiers aim shoulder launchers from the battlements. Rockets arc " +
        "out across the water. Explosions throw up spray around the " +
        "creature. It surges closer. A soldier drops from view. The " +
        "creature towers beneath Junon's cannon.";

    private const string Weapon4Text =
        "A shadow rises underwater. Purple armor breaks through the foam. " +
        "Water drains past its yellow eye.";

    private const string Weapon5Text =
        "The creature lifts its head, opening its fanged mouth. Blue-white " +
        "light builds inside its jaws. A beam blasts through the fortress " +
        "wall, leaving a dark hole. It faces the cannon at close range. The " +
        "cannon fires into its head. Flames erupt. The enormous body " +
        "crashes into the water. Smoke drifts past the cannon.";

    private const string Wh2e2Text =
        "Wavering blue light surrounds a luminous shell-shaped shrine. An " +
        "image of Aeris appears, lowering her eyes, then looking up. Her " +
        "face turns away. Pale materia tumbles through the vision. Stone " +
        "pillars shimmer behind it. The green orb glows beneath rippling " +
        "water, sending light upward.";

    private const string White2Text =
        "The shrine glows.";

    private const string Zmind01Text =
        "Stone platforms float amid green light. Timber houses fill the " +
        "view.";

    private const string Zmind02Text =
        "Golden lights surround the floating platforms. The view rises into " +
        "stars.";

    private const string Zmind03Text =
        "A shuttered window floats among glowing platforms. Through it, " +
        "flowers stand beside a wooden dresser. The view widens over a " +
        "furnished bedroom.";
}
