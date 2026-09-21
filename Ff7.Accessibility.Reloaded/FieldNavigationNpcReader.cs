namespace Ff7.Accessibility.Reloaded;

public sealed class FieldNavigationNpcReader
{
    // Ghidra: ff7_en.exe opcode 0x7E (TLKON) writes directly to field_event_data + 0x61.
    public const int TalkDisabledOffset = 0x61;

    // Script bank 3 sits one page past the field bank base, as the object and story
    // readers already resolve it.
    private const int ScriptBankThreeOffset = 0x100;
    public const int CollisionRadiusOffset = 0x72;
    public const int TalkRadiusOffset = 0x74;

    private static readonly IReadOnlyList<FieldNavigationTarget> EmptyTargets =
        Array.Empty<FieldNavigationTarget>();

    private static readonly IReadOnlyDictionary<(int FieldId, int EntityId), string>
        VerifiedLabels = new Dictionary<(int FieldId, int EntityId), string>
        {
            // tin_1: first passenger car. The beggar can give Cloud a
            // Phoenix Down during the Reactor 5 security countdown.
            [(139, 31)] = "Barret",
            [(139, 33)] = "Biggs",
            [(139, 34)] = "Jessie",
            [(139, 35)] = "Wedge",
            [(139, 37)] = "Shinra Manager",
            [(139, 38)] = "Man",
            [(139, 39)] = "Beggar",

            // tin_2: second passenger car. The adult man can give Cloud a
            // Hi-Potion during the Reactor 5 security countdown.
            [(140, 22)] = "Tifa",
            [(140, 27)] = "Boy",
            [(140, 28)] = "Boy",
            [(140, 29)] = "Girl",
            [(140, 30)] = "Man",
            [(140, 31)] = "Shinra employee",

            // tin_3: final passenger car before the party jumps.
            [(141, 14)] = "Barret",
            [(141, 15)] = "Tifa",
            [(141, 17)] = "Wedge",
            [(141, 18)] = "Jessie",

            // tin_4: crowded passenger car. Two passengers can bump into
            // Cloud and take gil; their visible roles do not reveal that.
            [(142, 18)] = "Barret",
            [(142, 19)] = "Tifa",
            [(142, 22)] = "Man",
            [(142, 23)] = "Woman",
            [(142, 25)] = "Johnny",
            [(142, 26)] = "Old man",
            [(142, 27)] = "Man",
            [(142, 28)] = "Man",
            [(142, 29)] = "Man",

            // mds7st3: Sector 7 station.
            [(146, 17)] = "Station attendant",
            [(146, 18)] = "Man",
            [(146, 20)] = "Woman",

            // mds7_w1: weapon shop.
            [(148, 9)] = "Weapon shopkeeper",
            [(148, 10)] = "Boy",
            [(148, 11)] = "Man",

            // mds7_w2: Beginner's Hall above the weapon shop.
            [(149, 9)] = "Beginner's Hall instructor",
            [(149, 10)] = "Man",
            [(149, 11)] = "Dog",
            [(149, 16)] = "Save Point instructor",
            [(149, 17)] = "Boy",
            [(149, 18)] = "Girl",
            [(149, 19)] = "Battle instructor",
            [(149, 20)] = "Battle instructor",
            [(149, 21)] = "Battle instructor",

            // mds7: Sector 7 slums.
            [(151, 22)] = "Barret",
            [(151, 27)] = "Johnny",
            [(151, 28)] = "Man",
            [(151, 29)] = "Woman",
            [(151, 30)] = "Boy",
            [(151, 31)] = "Woman",

            // mds7_im: item and Materia shop.
            [(152, 4)] = "Item shopkeeper",

            // min71: Johnny's home.
            [(153, 12)] = "Johnny",
            [(153, 13)] = "Johnny's father",
            [(153, 14)] = "Man",
            [(153, 15)] = "Johnny's mother",
            [(153, 17)] = "Woman",
            [(153, 18)] = "Shinra employee",

            // mds7pb_1: 7th Heaven bar. Some seated conversations use
            // interactive proxy models, so the same speaker can have two IDs.
            [(154, 19)] = "Barret",
            [(154, 20)] = "Tifa",
            [(154, 22)] = "Jessie",
            [(154, 23)] = "Biggs",
            [(154, 24)] = "Wedge",
            [(154, 25)] = "Marlene",
            [(154, 26)] = "Biggs",
            [(154, 28)] = "Wedge",
            [(154, 29)] = "Jessie",

            // mds7pb_2: AVALANCHE hideout.
            [(155, 12)] = "Barret",
            [(155, 13)] = "Tifa",
            [(155, 15)] = "Wedge",
            [(155, 16)] = "Jessie",
            [(155, 17)] = "Marlene",

            // mds7plr1: road to the Sector 7 pillar.
            [(156, 11)] = "Aerith",
            [(156, 17)] = "Guard",
            [(156, 18)] = "Guard",
            [(156, 19)] = "Man",
            [(156, 20)] = "Man",

            // Sector 5 slums and the market around Aeris's house. Labels were
            // checked against the installed FLEVEL model-loader resources, not
            // inferred from ordinary dialogue fragments.
            [(172, 6)] = "Man",
            [(172, 7)] = "Man",
            [(173, 7)] = "Man",
            [(174, 5)] = "Man",
            [(174, 6)] = "Boy",
            [(175, 4)] = "Man",
            [(175, 5)] = "Boy",
            [(176, 6)] = "Sick man",
            [(177, 10)] = "Man",
            [(177, 11)] = "Woman",
            [(177, 12)] = "Child",
            [(177, 13)] = "Man",
            [(177, 14)] = "Man",
            [(177, 15)] = "Dog",
            [(178, 4)] = "Weapon shopkeeper",
            [(178, 5)] = "Man",
            [(178, 6)] = "Child",
            [(179, 4)] = "Item shopkeeper",
            [(180, 6)] = "Materia shopkeeper",

            // Aeris's house variants reuse the same visible household models
            // while changing script/text sections with story progress.
            [(188, 6)] = "Tifa",
            [(188, 8)] = "Elmyra",
            [(189, 6)] = "Tifa",
            [(189, 8)] = "Elmyra",
            [(190, 6)] = "Marlene",

            // Wall Market's native Talk scripts frequently begin with ordinary
            // dialogue rather than a speaker heading. Every label below was
            // checked against the field's model resource, native interaction
            // script, and the visible role described by the Wall Market guide.
            // Generic pedestrians deliberately stay generic so navigation does
            // not reveal a reward or story role before a sighted player would.
            [(195, 16)] = "Diner promoter",
            [(195, 17)] = "Man",
            [(195, 18)] = "Man",
            [(195, 19)] = "Old man",
            [(195, 20)] = "Man",
            [(195, 21)] = "Man",
            [(195, 22)] = "Boy",

            [(196, 8)] = "Weapon shopkeeper",
            [(196, 9)] = "Weapon shop owner",

            [(197, 8)] = "Bodybuilder",
            [(197, 9)] = "Bodybuilder",
            [(197, 10)] = "Bodybuilder",
            [(197, 11)] = "Bodybuilder",
            [(197, 12)] = "Big Bro",

            [(199, 17)] = "Innkeeper",
            [(200, 5)] = "Materia shopkeeper",

            [(201, 9)] = "Dress shop attendant",
            [(201, 10)] = "Dress shop owner",
            [(201, 11)] = "Customer",

            [(202, 8)] = "Diner cook",
            [(202, 9)] = "Diner server",
            [(202, 10)] = "Diner patron",
            [(202, 11)] = "Diner patron",
            [(202, 12)] = "Diner patron",
            [(202, 13)] = "Diner patron",
            [(202, 14)] = "Diner patron",
            [(202, 15)] = "Diner patron",
            [(202, 16)] = "Diner patron",

            [(203, 7)] = "Pharmacy shopkeeper",

            [(204, 11)] = "Woman in bathroom",
            [(204, 12)] = "Man waiting for bathroom",
            [(204, 13)] = "Bartender",
            [(204, 14)] = "Dress shop owner",
            [(204, 15)] = "Man",
            [(204, 16)] = "Woman",

            [(205, 15)] = "Woman",
            [(205, 16)] = "Old woman",
            [(205, 17)] = "Boy",
            [(205, 18)] = "Boy",
            [(205, 19)] = "Girl",

            [(206, 14)] = "Corneo Hall doorman",

            [(207, 8)] = "Corneo Hall guard",
            [(207, 10)] = "Aerith",
            [(207, 11)] = "Tifa",

            [(208, 11)] = "Scotch",
            [(208, 13)] = "Corneo lackey",
            [(208, 14)] = "Corneo lackey",
            [(208, 15)] = "Corneo lackey",
            [(208, 16)] = "Corneo lackey",
            [(208, 17)] = "Corneo lackey",

            [(209, 12)] = "Tifa",
            [(209, 13)] = "Aerith",
            [(209, 15)] = "Kotch",

            [(214, 15)] = "Honey Bee Inn doorman",
            [(214, 16)] = "Honey Bee Inn attendant",
            [(214, 17)] = "Johnny",
            [(214, 18)] = "Man",
            [(214, 20)] = "Shinra soldier",
            [(214, 21)] = "Shinra soldier",

            [(216, 8)] = "Honey Bee Inn makeup artist",
            [(216, 9)] = "Woman changing",
            [(216, 10)] = "Woman exercising",

            [(218, 24)] = "Honey Bee Inn hostess",

            // Cloud's Nibelheim flashback. These identities come from the
            // native model-loader resources. Event directors, line proxies,
            // furniture, items, and other non-people remain intentionally
            // unlisted even when their delegated dialog begins with a name.
            [(273, 17)] = "Old man",
            [(273, 18)] = "Zangan",
            [(273, 19)] = "Innkeeper",
            [(273, 20)] = "Man in black cape",
            [(274, 8)] = "Sephiroth",
            [(274, 9)] = "Shinra infantryman",
            [(276, 11)] = "Cloud's mother",
            [(279, 7)] = "Tifa",
            [(279, 8)] = "Barret",
            [(279, 9)] = "Red XIII",
            [(279, 10)] = "Yuffie",
            [(279, 11)] = "Cait Sith",
            [(279, 12)] = "Vincent",
            [(279, 13)] = "Cid",
            [(279, 14)] = "Sephiroth",
            [(279, 15)] = "Shinra infantryman",
            [(279, 16)] = "Shinra infantryman",
            [(282, 8)] = "Sephiroth",
            [(282, 9)] = "Shinra infantryman",
            [(282, 10)] = "Shinra infantryman",
            [(282, 11)] = "Photographer",
            [(282, 12)] = "Tifa's father",
            [(282, 13)] = "Zangan",
            [(284, 16)] = "Man in black cape",
            [(284, 17)] = "Man in black cape",
            [(286, 17)] = "Man in black cape",
            [(286, 18)] = "Man in black cape",
            [(287, 21)] = "Man in black cape",
            [(290, 7)] = "Sephiroth",
            [(290, 8)] = "Shinra infantryman",
            [(290, 9)] = "Zangan",
            [(290, 10)] = "Photographer",
            [(291, 7)] = "Sephiroth",
            [(291, 8)] = "Shinra infantryman",
            [(291, 9)] = "Zangan",
            [(291, 10)] = "Photographer",
            [(293, 3)] = "Tifa",
            [(293, 4)] = "Barret",
            [(293, 5)] = "Red XIII",
            [(293, 6)] = "Yuffie",
            [(293, 7)] = "Cait Sith",
            [(293, 8)] = "Vincent",
            [(293, 9)] = "Cid",
            [(293, 10)] = "Sephiroth",
            [(300, 5)] = "Shinra infantryman",
            [(307, 3)] = "Sephiroth",
            [(312, 6)] = "Tifa",
            [(312, 8)] = "Shinra infantryman",
            [(312, 9)] = "Shinra infantryman",
            [(323, 6)] = "Tifa",
            [(323, 7)] = "Tifa",
            [(323, 8)] = "Sephiroth",
            [(324, 8)] = "Tifa",

            // Kalm and Chocobo Ranch. These labels were checked against each
            // native Talk entity and its visible field model. Models with an
            // empty/noninteractive Talk script are deliberately omitted.
            [(328, 12)] = "Weapon shopkeeper",
            [(328, 13)] = "Materia shopkeeper",
            [(329, 8)] = "Item shopkeeper",
            [(330, 9)] = "Bartender",
            [(330, 11)] = "Man",
            [(330, 12)] = "Man",
            [(331, 11)] = "Innkeeper",
            [(333, 7)] = "Woman",
            [(334, 6)] = "Girl",
            [(335, 16)] = "Man",
            [(335, 17)] = "Old man",
            [(335, 18)] = "Man",
            [(335, 19)] = "Woman",
            [(335, 20)] = "Man",
            [(335, 21)] = "Man",
            [(335, 22)] = "Boy",
            [(336, 8)] = "Old man",
            [(336, 9)] = "Dog",
            [(338, 6)] = "Man",
            [(339, 7)] = "Boy",
            [(339, 8)] = "Girl",
            [(341, 8)] = "Woman",
            [(342, 5)] = "Old man",
            [(342, 6)] = "Chocobo",
            [(343, 4)] = "Chocobo",
            [(343, 5)] = "Chocobo",
            [(344, 4)] = "Choco Bill",
            [(345, 4)] = "Chole",
            [(345, 5)] = "Choco Billy",
            [(345, 7)] = "Chocobo",
            [(345, 8)] = "Chocobo",
            [(345, 9)] = "Chocobo",
            [(345, 10)] = "Chocobo",
            [(345, 11)] = "Chocobo",
            [(345, 12)] = "Chocobo",

            // Fort Condor. Roles come from the walkthrough's Fort Condor section
            // and agree with both the field models and the native entity names:
            // convil_1's shop pair is literally scripted as "materia" and
            // "itemya", the elder as "jijii", and the watch-room operator as
            // "mihari", the lookout. A sighted player sees a staffed counter or
            // a man on the lookout, so naming the role reveals nothing extra.
            // convil_2's event2/event3/itemget entities load no model at all, so
            // there is nothing on screen and they stay unlabeled. The save point
            // in convil_1 and the Phoenix Materia in convil_4 are navigation
            // objects and remain in the field object catalog.
            [(353, 10)] = "Villager blocking the path",
            [(355, 27)] = "Materia shopkeeper",
            [(355, 28)] = "Item shopkeeper",
            [(355, 29)] = "Fort Condor elder",
            [(356, 22)] = "Lookout",
            [(358, 15)] = "Condor",

            // North Corel: installed ncorel/ncoin1..3/ncoinn CHAR model metadata
            // and native Talk/Action scripts. Anonymous residents have no speaker
            // heading. Shop staff and the seated couple delegate to LINE scripts;
            // the dog's Talk barks without displaying a MESSAGE.
            [(450, 13)] = "Barret",
            [(450, 20)] = "Man",
            [(450, 21)] = "General store shopkeeper",
            [(450, 22)] = "Man",
            [(450, 23)] = "Man",
            [(450, 24)] = "Weapon shopkeeper",
            [(450, 25)] = "Item shopkeeper",
            [(453, 7)] = "Woman",
            [(453, 8)] = "Old man",
            [(454, 6)] = "Old woman",
            [(454, 7)] = "Man",
            [(454, 8)] = "Boy",
            [(455, 11)] = "Woman",
            [(455, 12)] = "Old man",
            [(455, 13)] = "Girl",
            [(455, 14)] = "Old woman",
            [(455, 15)] = "Dog",
            [(456, 11)] = "Innkeeper",

            // innman2 loads the ARFD resident model; use a generic role here.
            [(456, 12)] = "Man",

            // crcin_2: native model-loader resources gold_dirver1, gold_driver2/3 and sub_esto.
            // Joe is named in MESSAGE 14. The other jockeys have anonymous Talk
            // text (including ellipses), which must not make them disappear.
            [(512, 4)] = "Joe",
            [(512, 5)] = "Jockey",
            [(512, 6)] = "Jockey",
            [(512, 7)] = "Jockey",
            [(512, 8)] = "Jockey",
            [(512, 9)] = "Ester",

            // Townspeople the game itself names, kept from being flattened into their
            // generic role by the mesh. Every row below was confirmed twice: a heading on
            // the entity's own spoken dialogue, and the name the field's script gives that
            // entity, which is why "DOMINO" and "tiehofu" are here and the entity merely
            // called "MAN" is not. Nothing here tells the player anything they would not
            // have been told by walking up and talking.

            // nmkin_1, nmkin_3, pillar_1, pillar_2, sbwy4_3, sbwy4_6: AVALANCHE during the
            // Sector 7 plate. They load the ordinary midgal_ava* residents.
            [(120, 9)] = "Biggs",
            [(120, 10)] = "Jessie",
            [(123, 4)] = "Jessie",
            [(158, 2)] = "Biggs",
            [(159, 6)] = "Jessie",
            [(166, 5)] = "Jessie",
            [(169, 6)] = "Biggs",

            // blin62_1: the Shinra headquarters in Midgar.
            [(242, 25)] = "Hart",
            [(242, 26)] = "Domino",

            // delmin2: the house east of Costa del Sol, where Johnny turns up again.
            [(448, 14)] = "Johnny",

            // tower5: the top floor of the Pagoda of the Five Mighty Gods. The masters are
            // named by their own dialogue and by the entity the script places them in.
            [(586, 17)] = "Gorky",
            [(586, 18)] = "Shake",
            [(586, 19)] = "Chekhov",
            [(586, 20)] = "Staniv",

            // Shop, inn and hotel staff whose visible role is not in the mesh. Each was
            // read from the field's own Talk or counter script: the menu it opens, or the
            // line it greets the customer with.

            // elmpb: the Kalm pub. The kitchen doorway line speaks for her.
            [(330, 10)] = "Woman",

            // nivl_3: Nibelheim. A reviewed field, so the dog it has always had needs a
            // row of its own now that a barking Talk is enough to find one.
            [(284, 14)] = "Dog",

            // jun_a, jun_i2: Junon's back-street shops. MENU 58, and MENU 24/59.
            [(374, 9)] = "Shopkeeper",

            // delpb: the Costa del Sol bar. MENU 26/60 for the weapons.
            [(445, 11)] = "Bodybuilder",
            [(445, 12)] = "Bodybuilder",
            [(445, 13)] = "Weapon shopkeeper",

            // ncorel2: North Corel. The boy stays a boy - what he is selling is his to
            // tell the player, not the navigation list's.
            [(451, 18)] = "Weapon shopkeeper",

            // ropest: the Gold Saucer ropeway. "mogiri" is the ticket taker.
            [(457, 14)] = "Ropeway attendant",

            // mtcrl_7: a hut on the Mount Corel path. "std_fm1" is the game's ordinary
            // adult townsperson and says nothing about who is on it - this one is a miner
            // out of work - so the label stays neutral.
            [(465, 7)] = "Townsperson",

            // jailin4, jail3: Corel prison.
            [(478, 4)] = "Man",

            // ghotin_1, gldst, coloin1, games: the Gold Saucer. 492:7 directs the player
            // to the counter, 492:8 is the counter and hands off to the greeting.
            [(492, 7)] = "Hotel attendant",
            [(492, 8)] = "Hotel receptionist",
            [(496, 19)] = "Greeter",
            [(500, 15)] = "Patron",

            // games: both wear a Gold Saucer costume. The entity behind the chocobo suit
            // is called "choko", which would otherwise announce a bird standing there.
            [(505, 13)] = "Costumed staff",
            [(505, 14)] = "Costumed staff",

            // cosmin2, uta_im: the Materia shops at Cosmo Canyon and Wutai.
            [(535, 5)] = "Materia shopkeeper",
            [(576, 5)] = "Materia shopkeeper",

            // snw_w, sninn_1: Icicle Inn's shop and inn. The shop's four counter lines and
            // the inn's two-sided tables all reach the same people.
            [(650, 10)] = "Weapon shopkeeper",
            [(651, 12)] = "Inn staff",
            [(651, 13)] = "Inn staff",
            [(651, 14)] = "Guest",
            [(651, 15)] = "Guest",
            [(651, 17)] = "Patron",

            // gongaga: the two companions who stay in the village after the Zack's
            // parents scene. Their own Talk is a bare RET and the conversation is
            // reached from the LINE in front of them, the same way the reviewed shop
            // and inn counters work.
            [(518, 15)] = "Tifa",
            [(518, 16)] = "Aerith"
        };

    private static readonly IReadOnlyDictionary<
        (int FieldId, int EntityId),
        (int LineEntityId, FieldNavigationTriggerLine Line)> VerifiedInteractionLines =
        new Dictionary<
            (int FieldId, int EntityId),
            (int LineEntityId, FieldNavigationTriggerLine Line)>
        {
            // mktinn: the visible innkeeper delegates the action-key counter
            // interaction to line00/event rather than its empty Talk script.
            [(199, 17)] = (
                7,
                new FieldNavigationTriggerLine(-163, 103, 0, -26, 55, 0)),

            // mkt_s2: the cook and server are visible models, while the kitchen
            // warning and order interaction live on the two native LINE regions.
            [(202, 8)] = (
                4,
                new FieldNavigationTriggerLine(209, -32, 0, 147, -34, 0)),
            [(202, 9)] = (
                5,
                new FieldNavigationTriggerLine(-59, -173, 0, -57, -101, 0)),

            // mkt_s3: the Pharmacy model is intentionally non-talkable; its
            // counter LINE calls the shopkeeper's native scripts 3 and 4.
            [(203, 7)] = (
                2,
                new FieldNavigationTriggerLine(-2, 0, 0, 127, 0, 0)),

            // ncorel: wsline/tsline call the visible shopkeeper's script 3;
            // bzline1 calls man2 script 9 (Buy / Listen / Not interested).
            // The bazaar also has a second working side, bzline2. Use its front
            // counter here so there is one target per visible shopkeeper.
            [(450, 24)] = (5, new(-150, -300, 0, -150, -404, 0)),
            [(450, 25)] = (6, new(31, -203, 0, 127, -199, 0)),
            [(450, 21)] = (7, new(45, -276, 0, -74, -350, 0)),

            // ncoin3: the seated couple's dialogue lives on ad scripts 3/4.
            // jitlkr reaches those scripts from the old man's chair; babtlk1
            // reaches the couple's conversation from the old woman's chair.
            // Model visibility and live LINE enable state still gate both.
            [(455, 12)] = (3, new(31, 15, 0, 81, 34, 0)),
            [(455, 14)] = (5, new(-3, 243, 0, -62, 200, 0)),

            // gongaga: line1 runs Aeris's conversation about Zack directly. line2 and
            // line3 are the two sides of Tifa's, and both call the event group's script
            // 4, which asks Tifa for her own script 3. line2 is taken as the one side
            // per visible companion, as the other reviewed two-sided counters are.
            [(518, 16)] = (8, new(-111, -284, 17, -69, -74, 17)),
            [(518, 15)] = (9, new(321, 559, 17, 173, 800, 17))
        };

    /// <summary>
    /// Conversations a field offers once and then records as had.
    ///
    /// <para>Gongaga's companions wait on bank 3 address 129 - bit 1 for Aeris, bit 2 for
    /// Tifa - and the scripts that run them clear that bit as the conversation begins, so
    /// the target stops being offered exactly when the game stops offering it. Visibility
    /// alone is insufficient: Tifa also appears elsewhere in the village during a later
    /// story visit, while these LINE handlers still require the pending flag.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<
        (int FieldId, int EntityId),
        (int Address, byte Mask)> VerifiedPendingConversations =
        new Dictionary<(int FieldId, int EntityId), (int Address, byte Mask)>
        {
            [(518, 16)] = (129, 0x02),
            [(518, 15)] = (129, 0x04)
        };

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<FieldScriptNpcDefinition>>
        VerifiedDefinitionsByField = VerifiedLabels.Keys
            .GroupBy(key => key.FieldId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FieldScriptNpcDefinition>)group
                    .Select(key =>
                    {
                        var hasLine = VerifiedInteractionLines.TryGetValue(
                            key,
                            out var interactionLine);
                        return new FieldScriptNpcDefinition(
                            key.FieldId,
                            key.EntityId,
                            string.Empty,
                            Array.Empty<int>(),
                            hasLine ? interactionLine.LineEntityId : null,
                            hasLine ? interactionLine.Line : null);
                    })
                    .ToArray());

    // Every Talk entity in these reviewed field groups was checked against the
    // installed FLEVEL scripts and model-loader metadata.
    // Do not guess a name from ordinary dialogue in these fields.
    private static readonly IReadOnlySet<int> ReviewedLabelFields =
        new HashSet<int>(
            Enumerable.Range(139, 19)
                .Concat(Enumerable.Range(172, 9))
                .Concat([188, 189, 190])
                .Concat(Enumerable.Range(195, 17))
                .Concat([214, 216, 218])
                .Concat(Enumerable.Range(273, 55))
                .Concat(Enumerable.Range(328, 18))

                // Fort Condor: condor1, condor2, convil_1..convil_4. Every
                // visible Talk model there is labeled above; anything else on
                // these screens loads no model and must not be guessed at.
                .Concat(Enumerable.Range(353, 6))
                .Concat([450, 453, 454, 455, 456, 512]));

    private readonly Func<int, int> readInt32;
    private readonly Func<int, short> readInt16;
    private readonly Func<int, byte> readByte;
    private readonly Func<int, int, IReadOnlyList<string>> resolveDialogLines;
    private readonly Func<int, IReadOnlyList<FieldScriptNpcDefinition>> definitionProvider;
    private readonly HashSet<(int FieldId, int EntityId)> excludedEntities;
    private readonly Func<int, bool>? isLineEnabled;

    public FieldNavigationNpcReader(
        Func<int, int> readInt32,
        Func<int, short> readInt16,
        Func<int, byte> readByte,
        Func<int, int, IReadOnlyList<string>> resolveDialogLines,
        Func<int, IReadOnlyList<FieldScriptNpcDefinition>> definitionProvider,
        IEnumerable<(int FieldId, int EntityId)>? excludedEntities = null,
        Func<int, bool>? isLineEnabled = null)
    {
        this.readInt32 = readInt32;
        this.readInt16 = readInt16;
        this.readByte = readByte;
        this.resolveDialogLines = resolveDialogLines;
        this.definitionProvider = definitionProvider;
        this.excludedEntities = excludedEntities?.ToHashSet() ?? [];
        this.isLineEnabled = isLineEnabled;
    }

    public IReadOnlyList<FieldNavigationTarget> ReadTargets(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return EmptyTargets;
        }

        var definitions = MergeVerifiedDefinitions(
            position.FieldId,
            definitionProvider(position.FieldId));
        if (definitions.Count == 0)
        {
            return EmptyTargets;
        }

        var eventTable = readInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr);
        var modelCount = readByte(FieldPositionReader.AddressFieldNumModels);
        if (eventTable == 0 || modelCount == 0)
        {
            return EmptyTargets;
        }

        var targets = new List<FieldNavigationTarget>(definitions.Count);
        var playerEventAddress = eventTable + position.ModelIndex * FieldNavigationObjectReader.FieldEventDataStride;
        var playerCollisionRadius = Math.Max(0, (int)readInt16(playerEventAddress + CollisionRadiusOffset));
        foreach (var definition in definitions)
        {
            if (excludedEntities.Contains((definition.FieldId, definition.EntityId)))
            {
                continue;
            }

            if (!IsConversationStillPending(definition))
            {
                continue;
            }

            var modelId = readByte(FieldNavigationObjectReader.AddressFieldModelIdArray + definition.EntityId);
            if (modelId == 0xFF || modelId >= modelCount || modelId == position.ModelIndex)
            {
                continue;
            }

            var eventAddress = eventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
            if (readByte(eventAddress + FieldNavigationObjectReader.VisibilityOffset) == 0)
            {
                continue;
            }

            var lineEntityId = definition.InteractionLineEntityId;
            var interactionLine = definition.InteractionLine;
            var usesInteractionLine =
                lineEntityId.HasValue &&
                interactionLine.HasValue;
            if (usesInteractionLine)
            {
                if (isLineEnabled is null || !isLineEnabled(lineEntityId!.Value))
                {
                    continue;
                }
            }
            else if (readByte(eventAddress + TalkDisabledOffset) != 0)
            {
                continue;
            }

            var label = ResolveLabel(definition);
            if (label.Length == 0)
            {
                continue;
            }

            var targetX = usesInteractionLine
                ? Midpoint(interactionLine!.Value.StartX, interactionLine.Value.EndX)
                : FromModelFixedPoint(readInt32(eventAddress + FieldNavigationObjectReader.PositionXOffset));
            var targetY = usesInteractionLine
                ? Midpoint(interactionLine!.Value.StartY, interactionLine.Value.EndY)
                : FromModelFixedPoint(readInt32(eventAddress + FieldNavigationObjectReader.PositionYOffset));
            var targetZ = usesInteractionLine
                ? Midpoint(interactionLine!.Value.StartZ, interactionLine.Value.EndZ)
                : FromModelFixedPoint(readInt32(eventAddress + FieldNavigationObjectReader.PositionZOffset));
            targets.Add(new FieldNavigationTarget(
                definition.FieldId,
                FieldNavigationCategory.Npcs,
                label,
                targetX,
                targetY,
                targetZ,
                $"npc:{definition.FieldId}:{definition.EntityId}",
                TriggerEntityId: usesInteractionLine ? lineEntityId!.Value : definition.EntityId,
                InteractionRadius: usesInteractionLine
                    ? 0
                    : playerCollisionRadius + Math.Max(0, (int)readInt16(eventAddress + TalkRadiusOffset)),
                TriggerLine: usesInteractionLine ? interactionLine : null));
        }

        return targets.Count == 0 ? EmptyTargets : targets;
    }

    /// <summary>
    /// Whether a reviewed one-time conversation is still on offer. Entities without such a
    /// row are unaffected.
    /// </summary>
    private bool IsConversationStillPending(FieldScriptNpcDefinition definition)
    {
        if (!VerifiedPendingConversations.TryGetValue(
                (definition.FieldId, definition.EntityId),
                out var pending))
        {
            return true;
        }

        var address = FieldNavigationObjectReader.AddressFieldBankBase +
                      ScriptBankThreeOffset +
                      pending.Address;
        return (readByte(address) & pending.Mask) != 0;
    }

    private static IReadOnlyList<FieldScriptNpcDefinition> MergeVerifiedDefinitions(
        int fieldId,
        IReadOnlyList<FieldScriptNpcDefinition> scriptedDefinitions)
    {
        if (!VerifiedDefinitionsByField.TryGetValue(fieldId, out var verifiedDefinitions))
        {
            return scriptedDefinitions;
        }

        var merged = new List<FieldScriptNpcDefinition>(
            scriptedDefinitions.Count + verifiedDefinitions.Count);
        var verifiedByEntity = verifiedDefinitions.ToDictionary(
            definition => definition.EntityId);
        foreach (var scripted in scriptedDefinitions)
        {
            if (!verifiedByEntity.TryGetValue(scripted.EntityId, out var verified))
            {
                merged.Add(scripted);
                continue;
            }

            // Preserve the parser's native dialog and counter-proxy evidence,
            // but allow a reviewed manual proxy for interactions delegated
            // through an event group rather than the visible model itself.
            merged.Add(scripted with
            {
                InteractionLineEntityId =
                    verified.InteractionLineEntityId ??
                    scripted.InteractionLineEntityId,
                InteractionLine =
                    verified.InteractionLine ??
                    scripted.InteractionLine
            });
        }

        foreach (var verified in verifiedDefinitions)
        {
            if (scriptedDefinitions.Any(definition => definition.EntityId == verified.EntityId))
            {
                continue;
            }

            // Some visible native models (for example Sector 5's dog and the
            // child in the weapon shop) have a Talk entry that only plays a
            // sound/animation or delegates to a LINE proxy, so there is no
            // MESSAGE opcode for the generic script catalog to discover.
            merged.Add(verified);
        }

        return merged;
    }

    private string ResolveLabel(FieldScriptNpcDefinition definition)
    {
        if (VerifiedLabels.TryGetValue(
                (definition.FieldId, definition.EntityId),
                out var verifiedLabel))
        {
            return verifiedLabel;
        }

        if (ReviewedLabelFields.Contains(definition.FieldId))
        {
            return string.Empty;
        }

        // Scenery is settled before a word of its dialogue is read. A scene hangs its
        // lines on whatever is standing nearest, so the church store room's barrels carry
        // Aeris's heading and Seventh Heaven's till carries Wedge's; taking those at face
        // value sends the player across the room to talk to a barrel.
        if (IsSceneryModelFamily(definition.ModelResourceName))
        {
            return string.Empty;
        }

        // What the entity is comes from what the field loads for it: its own script name
        // first, then the mesh. Where the two disagree the script name is the closer
        // description - Costa's resort attendant loads "kosta_rgirl" but her entity is
        // "woman1", and she is a woman - and the mesh answers for the entities whose
        // script name says nothing, like "dr1", "esto" and "jp3".
        //
        // This is read before the dialogue, because a window is not owned by whoever is
        // standing in front of it. An entity's message list holds every window its script
        // opens, including the ones other people speak through, so the gondola attendant
        // at the Gold Saucer opens a window headed "Cloud" and a counter headed
        // "Current GP", and is neither.
        var role = ResolveRole(definition.EntityName, definition.ModelResourceName);
        if (role.Length > 0)
        {
            return role;
        }

        // Nobody with a role, so fall back to a heading on the entity's own dialogue.
        // This is what names the people the role vocabulary has no word for: the party in
        // a scene, and the story characters who carry their own mesh. It is only trusted
        // where the line beneath the heading is somebody speaking.
        foreach (var dialogId in definition.DialogIds)
        {
            var lines = resolveDialogLines(definition.FieldId, dialogId);
            if (lines.Count >= 2 &&
                IsSpokenLine(lines[1]) &&
                TryNormalizeSpeakerName(lines[0], out var speaker))
            {
                return speaker;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The generic role of whoever is standing there, from the mesh the field loads for
    /// them.
    ///
    /// <para>Townspeople mostly do not introduce themselves. Their dialogue opens with
    /// what they are saying, not with a heading, so the speaker-name path above finds
    /// nothing and the target was dropped - a harbour full of sailors the player could
    /// talk to and the mod reported none. The model is the one description the game
    /// supplies that survives to here, and it is also what a sighted player is going by:
    /// they see a man, an old woman, a sailor. So that is what is announced.</para>
    ///
    /// <para>Only names in this table produce a label. An unrecognised mesh stays
    /// unlabelled and its target is still dropped, so this can add people the game draws
    /// and never invent one. Two families are excluded outright: <c>fieldbg_</c> is
    /// scenery and treasure that belongs to Objects, and <c>main_</c> is the player's own
    /// party, who are named by their dialogue headings where they speak at all.</para>
    /// </summary>
    /// <summary>
    /// Meshes that have no generic role to give. <c>fieldbg_</c> is scenery and treasure
    /// that belongs to the Objects list; <c>main_</c> and <c>modify_</c> are the party and
    /// their costumed and story variants, who are particular people rather than a role;
    /// <c>weapon_</c> is the thing somebody is holding. Each is still free to be named by
    /// a heading on its own dialogue - this only stops a role being invented for one.
    /// </summary>
    private static bool IsExcludedModelFamily(string modelResourceName) =>
        IsSceneryModelFamily(modelResourceName) ||
        modelResourceName.Contains("main_", StringComparison.OrdinalIgnoreCase) ||
        modelResourceName.Contains("modify_", StringComparison.OrdinalIgnoreCase) ||
        modelResourceName.Contains("weapon_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Scenery and treasure, which the Objects list already offers separately. This is the
    /// one family that is not a person under any reading, so it is settled ahead of the
    /// dialogue rather than alongside the other meshes that merely have no role to give.
    /// </summary>
    private static bool IsSceneryModelFamily(string modelResourceName) =>
        modelResourceName.Contains("fieldbg_", StringComparison.OrdinalIgnoreCase);

    private static string ResolveRole(string entityName, string modelResourceName)
    {
        if (IsExcludedModelFamily(modelResourceName))
        {
            return string.Empty;
        }

        // Resources may still carry the field they belong to - "del1kosta_rgirl.char" -
        // so match on the family and role rather than the whole string.
        var resource = modelResourceName;
        var suffix = resource.LastIndexOf(".char", StringComparison.OrdinalIgnoreCase);
        if (suffix > 0)
        {
            resource = resource[..suffix];
        }

        // One vocabulary, read in order, against both the name the script gives the entity
        // and the mesh the field loads for it. Order decides, not which of the two is
        // asked: the script calls Costa's resort attendant "woman1" while her mesh is
        // "kosta_rgirl", and she is a woman rather than a girl because "woman" is the
        // earlier entry - and the man beside her is called "busiman" while his mesh is the
        // suited "shinra_ippan_3", which is nearer the top still, so he is a Shinra manager
        // rather than just a man.
        foreach (var (fragment, role) in ModelRoles)
        {
            if (entityName.Contains(fragment, StringComparison.OrdinalIgnoreCase) ||
                resource.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return role;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The first role token this name carries, or empty. Ordered, because every one of
    /// these is a substring of one that must be tested before it.
    /// </summary>
    /// <summary>
    /// Model families to the role they are seen as, most specific first. Every entry was
    /// read out of the installed field files rather than assumed: <c>std_</c> is the
    /// game's own generic townsfolk set, and the rest are the location families that
    /// actually carry Talk scripts in the settlements.
    /// </summary>
    private static readonly (string Fragment, string Role)[] ModelRoles =
    [
        // Occupations first: a mesh that says what someone does is better than one that
        // only says what they are. "crew" is the ship's crew in their sailor suits, which
        // is what Costa del Sol's harbour is full of.
        ("rocket_crew", "Crew member"),
        ("crew", "Sailor"),
        ("guard", "Guard"),

        // "ippan_3" is the suited one of the three ordinary-citizen meshes. The curated
        // Midgar row at (139, 37) calls him a Shinra Manager, and the entities using this
        // mesh at junmin5 and on Costa's harbour head their own dialogue the same way.
        // Above the plain "ippan" below, and above "man", which "busiman" would match.
        ("ippan_3", "Shinra manager"),
        ("hei", "Shinra soldier"),
        ("reifuku", "Shinra employee"),
        ("doctor", "Doctor"),
        ("nurse", "Nurse"),

        // "market_merchant" is the man behind a counter: Junon's item shop calls his
        // entity "master", Cosmo Canyon's calls it "OYAJI", and both are shopkeepers.
        ("merchant", "Shopkeeper"),
        ("staff", "Attendant"),

        // Junon's inn counter and Cosmo's shop use "uketuke" - the Japanese for a
        // reception desk - as the entity name for whoever is standing behind it.
        ("uketu", "Receptionist"),

        // Animals are part of a town the same way its people are, and the curated
        // Midgar rows already call these Dog.
        ("dog", "Dog"),
        ("choko", "Chocobo"),
        ("animal_cat", "Cat"),

        // Then age and gender, which is what the player can see from across the room.
        // "oldw" and "obasan" before "woman", and every one of those before "man",
        // because each is a substring of the next.
        ("oldm", "Old man"),
        ("oldwm", "Old woman"),
        ("oldw", "Old woman"),
        ("obasan", "Old woman"),
        ("woman", "Woman"),
        ("wman", "Woman"),
        ("onna", "Woman"),
        ("girl", "Girl"),
        ("boy", "Boy"),
        ("child", "Child"),
        ("man", "Man"),
        ("otoko", "Man"),

        // The towns outside Midgar name a good many of their people in Japanese rather
        // than in the std_ vocabulary: "onna" and "otoko" are woman and man, "ippan" is
        // an ordinary member of the public, and "narazumono" is what Corel prison's
        // inhabitants are called by the script that places them. Nothing here says more
        // about somebody than a player watching them would already know.
        ("ippan", "Townsperson"),
        ("nara", "Man")
    ];

    /// <summary>
    /// Whether the line under a heading is somebody speaking.
    ///
    /// <para>A heading only means a speaker when what follows it is speech. Fields put
    /// plenty of other things in a window - the choices in a menu, a place name on a
    /// signpost, the player's GP above a Gold Saucer counter - and the first line of those
    /// is a perfectly ordinary word or two, so the name test alone accepts it. That is how
    /// the gondola attendant came to be announced as "Current GP", a Midgar resident as
    /// "Cancel", and a guard on the Kalm road as "TUNNEL".</para>
    ///
    /// <para>The game marks speech itself: dialogue is quoted, everything else is not.
    /// That is the distinction used here.</para>
    /// </summary>
    private static bool IsSpokenLine(string line)
    {
        // The field text opens speech with the typographic quote. A straight quote is what
        // an item name is wrapped in - Received Key Item / "Member's Card"! - so it is not
        // taken as a sign that somebody is talking.
        var trimmed = Ff7EncodedTextDecoder.NormalizeWhitespace(line).TrimStart();
        return trimmed.Length > 0 && trimmed[0] == '“';
    }

    private static bool TryNormalizeSpeakerName(string line, out string speaker)
    {
        speaker = Ff7EncodedTextDecoder.NormalizeWhitespace(line).Trim();
        if (speaker.Length is 0 or > 32)
        {
            return false;
        }

        var words = speaker.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 4)
        {
            return false;
        }

        // Corel prison's Mr.Coates and the Ghost Hotel's Mr. Hangman head their own
        // dialogue and were rejected for the full stop in the title.
        return speaker.Any(char.IsLetter) && speaker.All(character =>
            char.IsLetter(character) ||
            char.IsWhiteSpace(character) ||
            character is '\'' or '-' or '.');
    }

    private static int FromModelFixedPoint(int value) =>
        value / FieldNavigationObjectReader.ModelPositionFixedPointScale;

    private static int Midpoint(int first, int second) =>
        (int)Math.Round((first + second) / 2d, MidpointRounding.AwayFromZero);
}
