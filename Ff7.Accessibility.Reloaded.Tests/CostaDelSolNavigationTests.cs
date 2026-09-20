using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class CostaDelSolNavigationTests
{
    private const string TownExit = "Leave Costa del Sol for Mount Corel";
    private const string Beach = "Visit the beach (optional)";
    private const string Hojo = "Speak to the woman beside Hojo (optional)";
    private const string ReturnToTown = "Return to the town";

    public static void Run()
    {
        HarbourCrewAreOfferedFromTheModelTheGameDraws();
        SceneryAndPartyMembersNeverBecomePeopleToWalkTo();
        PartyMembersKeepTheNameTheirOwnDialogueGives();
        SceneryIsNotNamedByWhoeverSpeaksOverIt();
        TownsfolkAreDescribedByTheMeshTheFieldLoads();
        AFieldNameCannotInventARole();
        NamedTownspeopleKeepTheirNames();
        ShopAndInnStaffBehindCountersAreOffered();
        AnimalsAndSilentActorsAreOffered();
        ATitleWithAFullStopIsStillAName();
        CrewRolesDistinguishAirshipAndShipUniforms();
        SpeakerHeadingsRequireLetters();
        AnAmbiguousMeshIsNotGuessedIntoAGender();
        AnUnrecognisedModelIsStillNotGuessedAt();
        TownExitRemainsSelectableAlongsideOptionalVisits();
        FirstDockDepartureUsesItsNativeEnabledLine();
        HojoSceneUsesTheWomanAndItsActualCompletionFlags();
        OptionalCompanionsRequireVisibleNativeModels();
        NativeCountersRequireCloseManualInteraction();
        BallUsesOnlyItsVisibleLiveModel();
        InteriorReturnsRemainAvailableWithoutOptionalInteractions();
        CostaObjectivesEndWhenMountCorelAdvancesTheStory();
        ReturnShipHasADistinctBoardingLabel();
    }

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        Run();
        NativeGatewaysAreReachableFromTheirInstalledEntrances(createWalkmeshReader);
        BarretCanBeApproachedOutsideHisNativeLockedBathroom(createWalkmeshReader);
        TheNativeCatalogFindsTheActorsTheFieldsDraw();
    }

    /// <summary>
    /// The labels above are decided from a definition, and a definition the catalog never
    /// produced is a person nobody can reach. These read the installed FLEVEL itself, so a
    /// change to how entities are discovered cannot quietly empty a town while the label
    /// tests carry on passing.
    /// </summary>
    private static void TheNativeCatalogFindsTheActorsTheFieldsDraw()
    {
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(gameRoot);

        // Costa del Sol harbour: the six models the field loads, bound to their own CHAR
        // slot rather than to load order.
        var harbour = catalog.ReadField(441).Npcs.ToDictionary(npc => npc.EntityId);
        foreach (var (entity, model) in new[]
                 {
                     (14, "shinra_crew.char"), (15, "shinra_crew.char"),
                     (16, "shinra_crew.char"), (17, "shinra_ippan_3.char"),
                     (18, "kosta_rgirl.char"), (19, "kosta_sman2.char")
                 })
        {
            True(harbour.ContainsKey(entity), $"441:{entity} is in the native catalog");
            Equal(model, harbour[entity].ModelResourceName, $"441:{entity} loads its own mesh");
        }

        // Bone Village: seven residents whose entities the script never named.
        var boneVillage = catalog.ReadField(617).Npcs
            .Where(npc => npc.EntityName.Length == 0 && npc.ModelResourceName.StartsWith("bone_"))
            .ToArray();
        Equal(7, boneVillage.Length, "Bone Village's unnamed entities are catalogued");

        // Talk that says nothing: a Junon dog, Nibelheim's menu-only woman, and the Ghost
        // Hotel receptionist whose Talk only hands off to the greeting script.
        foreach (var (field, entity) in new[] { (360, 25), (270, 11), (492, 8), (721, 10) })
        {
            True(
                catalog.ReadField(field).Npcs.Any(npc => npc.EntityId == entity),
                $"{field}:{entity} is catalogued despite carrying no message");
        }

        // Counters: staff with no Talk at all, reached across the line in front of them.
        foreach (var (field, entity) in new[]
                 {
                     (451, 18), (451, 19), (457, 14), (576, 5),
                     (650, 10), (650, 11), (651, 12), (651, 14), (330, 10)
                 })
        {
            var matches = catalog.ReadField(field).Npcs
                .Where(npc => npc.EntityId == entity)
                .ToArray();
            Equal(1, matches.Length, $"{field}:{entity} is reachable across its counter");
            True(
                matches[0].InteractionLineEntityId.HasValue,
                $"{field}:{entity} is gated on the native line, not on a talk flag");
        }

        // And the ones that must stay out: a scene that drives a bystander, the line that
        // walks the player out of a Mideel house, and the Gold Saucer arm-wrestling arms.
        foreach (var (field, entity) in new[] { (452, 23), (722, 8), (416, 7) }
                     .Concat(Enumerable.Range(19, 9).Select(entity => (363, entity))))
        {
            True(
                catalog.ReadField(field).Npcs.All(npc => npc.EntityId != entity),
                $"{field}:{entity} is a scene and stays out of the list");
        }
    }

    private static void ReturnShipHasADistinctBoardingLabel()
    {
        // del1/wmJump uses transport entry 39, which has no native room name.
        // Once the fare is paid, calling this merely "Exit" hides its purpose.
        var ship = new FieldNavigationTarget(441, FieldNavigationCategory.Exits,
            "Scripted exit", -737, 770, 63, "script-exit:441:6:39",
            TriggerEntityId: 6, DestinationFieldIds: [39],
            TriggerLine: new(-737, 672, 63, -737, 867, 63));
        var resolver = new FieldExitLabelResolver(_ => FieldMapNameResolution.Unknown,
            () => "Costa del Sol Harbor");
        var labeled = resolver.Resolve([ship]).Single();
        Equal("Board the ship to Junon", labeled.Label, "return-trip boarding is identifiable");
        Equal(ship with { Label = labeled.Label }, labeled,
            "labelling must preserve native exit ownership and geometry");
    }

    /// <summary>
    /// The player's report: standing on the Costa del Sol harbour, talking to a sailor,
    /// paying a hundred gil for the passage - and the mod listing no NPCs at all.
    ///
    /// <para>Field 441 del1 carries six modelled Talk entities. None of them is in a
    /// reviewed field and none of their dialogue opens with a speaker heading - dialog 66
    /// begins <c>"There's no ship to Junon</c> - so the only label source there was ever
    /// going to find nothing, and every one of them was dropped. The mesh the field loads
    /// is the description that survives, and it is also what the player is looking at.</para>
    /// </summary>
    private static void HarbourCrewAreOfferedFromTheModelTheGameDraws()
    {
        var harbour = new (int Entity, string EntityName, string Model, string Label)[]
        {
            (14, "crew1", "del1shinra_crew.char", "Sailor"),
            (15, "crew2", "del1shinra_crew.char", "Sailor"),
            (16, "crew3", "del1shinra_crew.char", "Sailor"),
            // The suited mesh: his own dialogue heads itself "Shinra manager", and the
            // curated Midgar row on the same mesh says the same.
            (17, "busiman", "del1shinra_ippan_3.char", "Shinra manager"),
            (18, "woman1", "del1kosta_rgirl.char", "Woman"),
            (19, "man2", "del1kosta_sman2.char", "Man")
        };

        foreach (var (entity, entityName, model, label) in harbour)
        {
            var memory = new NpcMemory(entity, entityName);
            var reader = memory.Reader(model);
            var position = new FieldPositionSnapshot(1, 441, 0, 0, 0, 0, 0, 0);
            var targets = reader.ReadTargets(position);

            Equal(1, targets.Count, $"{model} is somebody the player can walk to");
            Equal(label, targets.Single().Label, $"{model} is announced by what it is");
            Equal($"npc:441:{entity}", targets.Single().StableId, "native identity");

            // The gates that were already right stay right.
            memory.Visible = false;
            Equal(0, reader.ReadTargets(position).Count, "hidden crew are omitted");
            memory.Visible = true;
            memory.TalkDisabled = true;
            Equal(0, reader.ReadTargets(position).Count, "a script-owned actor is not a talk target");
            memory.TalkDisabled = false;
            memory.ModelId = 0xff;
            Equal(0, reader.ReadTargets(position).Count, "an unloaded model is omitted");
        }
    }

    /// <summary>
    /// The other half of reading the mesh. Treasure, scenery and the player's own party are
    /// never conjured into somebody to walk to out of a mesh or an entity name - a barrel
    /// whose entity is called "man1" is still a barrel.
    /// </summary>
    private static void SceneryAndPartyMembersNeverBecomePeopleToWalkTo()
    {
        foreach (var model in new[]
                 {
                     "del1fieldbg_trb_wood.char",
                     "del1fieldbg_potion.char",
                     "del1main_n_tifa.char",
                     "del1main_ballet.char"
                 })
        {
            var memory = new NpcMemory(20, "man1");
            // Ordinary dialogue, so the only thing that could name these is the role
            // fallback - and for these two families it must not.
            var reader = memory.Reader(model, ["Something said out loud."]);
            Equal(
                0,
                reader.ReadTargets(new FieldPositionSnapshot(1, 441, 0, 0, 0, 0, 0, 0)).Count,
                $"{model} is not a person to walk to");
        }
    }

    /// <summary>
    /// Reading the mesh may only ever add people. A party member in a scene is named by the
    /// heading on their own dialogue, and that has to keep working: suppressing the invented
    /// identity must not also suppress the real one the game already gave.
    /// </summary>
    private static void PartyMembersKeepTheNameTheirOwnDialogueGives()
    {
        var memory = new NpcMemory(20, "tifa");
        var reader = memory.Reader(
            "del1main_n_tifa.char",
            ["Tifa", "“Something said out loud.”"]);
        var targets = reader.ReadTargets(new FieldPositionSnapshot(1, 441, 0, 0, 0, 0, 0, 0));
        Equal(1, targets.Count, "a party member who names herself is still offered");
        Equal("Tifa", targets[0].Label, "and still under her own name");
    }

    /// <summary>
    /// A scene often hangs its dialogue on the nearest piece of furniture, so the barrels
    /// in the church store room carry Aeris's lines and the till in Seventh Heaven carries
    /// Wedge's. Reading the heading off those announces a barrel as a person and sends the
    /// player across the room to talk to it, which is worse than not listing it: nothing a
    /// sighted player sees there is Aeris.
    /// </summary>
    private static void SceneryIsNotNamedByWhoeverSpeaksOverIt()
    {
        foreach (var (model, speaker) in new[]
                 {
                     ("fieldbg_taru.char", "Aerith"),
                     ("fieldbg_cash.char", "Wedge"),
                     ("fieldbg_hana.char", "Jessie"),
                     ("fieldbg_trb_glow.char", "Barret")
                 })
        {
            var memory = new NpcMemory(24, "bar1");
            var reader = memory.Reader(model, [speaker, "“Something said out loud.”"]);
            Equal(
                0,
                reader.ReadTargets(new FieldPositionSnapshot(1, 441, 0, 0, 0, 0, 0, 0)).Count,
                $"{model} is not {speaker}");
        }
    }

    /// <summary>
    /// Costa's harbour is not the only town whose people carry no speaker heading. These
    /// are the meshes and entity names the rest of the settlements use for the same kind
    /// of ordinary person, each one taken from the field that loads it: Junon's citizens
    /// and its female guards, the Junon inn's reception, Wutai's children, Corel prison's
    /// inhabitants, and the cat that wanders a town screen.
    /// </summary>
    private static void TownsfolkAreDescribedByTheMeshTheFieldLoads()
    {
        foreach (var (entityName, model, expected) in new[]
                 {
                     // blin61, blin64, blin66_1: Junon citizens. The entity names are
                     // scene labels - ZAKOA, NETARO, or nothing at all - so only the mesh
                     // says who is standing there.
                     ("ZAKOA", "shinra_ippan_1.char", "Townsperson"),
                     ("", "shinra_ippan_1.char", "Townsperson"),
                     ("KEIBIA", "shinra_onna.char", "Woman"),
                     ("OTOKO", "shinra_ippan_2.char", "Man"),
                     ("uketuke", "market_buka.char", "Receptionist"),
                     ("OYAJI", "market_merchant.char", "Shopkeeper"),
                     ("MAGO", "utai_child.char", "Child"),
                     ("nara5", "korel_narazu107.char", "Man"),
                     ("junon2", "animal_cat1.char", "Cat")
                 })
        {
            var memory = new NpcMemory(22, entityName);
            var targets = memory
                .Reader(model, ["Something said out loud."])
                .ReadTargets(new FieldPositionSnapshot(1, 441, 0, 0, 0, 0, 0, 0));
            Equal(1, targets.Count, $"{model} is somebody to walk to");
            Equal(expected, targets[0].Label, $"{model} is described by what it is");
        }
    }

    /// <summary>
    /// A model resource is the field's own name followed by the mesh, and the Don Corneo
    /// screens are called "onna". Cloud in a dress there is still not a woman standing in
    /// the room, whether or not the field name was trimmed off first.
    /// </summary>
    private static void AFieldNameCannotInventARole()
    {
        var memory = new NpcMemory(23, "cloud");
        var reader = memory.Reader("onna_52modify_clouds.char", ["Something said out loud."]);
        Equal(
            0,
            reader.ReadTargets(new FieldPositionSnapshot(1, 441, 0, 0, 0, 0, 0, 0)).Count,
            "a field called onna does not turn its models into women");
    }

    /// <summary>
    /// Reading the mesh before the dialogue is what stops a chocobo being announced as
    /// Cloud, but it would also flatten the townspeople the game does name: the Wutai
    /// Pagoda's five masters, the AVALANCHE members on the pillar, Johnny outside Costa.
    /// Each of these is a reviewed row, and each was confirmed twice over - the heading on
    /// the entity's own spoken dialogue, and the name the field's script gives the entity.
    /// </summary>
    private static void NamedTownspeopleKeepTheirNames()
    {
        foreach (var (field, entity, entityName, model, expected) in new[]
                 {
                     (242, 26, "DOMINO", "std_oldm4.char", "Domino"),
                     (242, 25, "HATT", "std_man17.char", "Hart"),
                     (586, 17, "goriki", "5towerutai2_man.char", "Gorky"),
                     (586, 18, "shake", "5towerutai_child.char", "Shake"),
                     (586, 19, "tiehofu", "5towerutai_woman.char", "Chekhov"),
                     (586, 20, "sutanif", "5towerutai_man.char", "Staniv"),
                     (448, 14, "johnny", "std_man4.char", "Johnny"),
                     (158, 2, "big", "midgal_avaman.char", "Biggs"),
                     (120, 10, "av_j", "midgal_avawoman.char", "Jessie")
                 })
        {
            var memory = new NpcMemory(entity, entityName, field);
            var targets = memory
                .Reader(model, ["Something entirely unlike a name."])
                .ReadTargets(new FieldPositionSnapshot(1, field, 0, 0, 0, 0, 0, 0));
            Equal(1, targets.Count, $"{field}:{entity} is somebody to walk to");
            Equal(expected, targets[0].Label, $"{field}:{entity} keeps the name the game gives");
        }
    }

    /// <summary>
    /// Shop and inn staff are commonly placed behind a counter with no Talk of their own,
    /// and are reached by a LINE laid along the customer's side. Each of these was taken
    /// from the field's own scripts, and each keeps the visible role a sighted player is
    /// looking at rather than anything the till would tell them.
    /// </summary>
    private static void ShopAndInnStaffBehindCountersAreOffered()
    {
        foreach (var (field, entity, entityName, model, expected) in new[]
                 {
                     (451, 18, "wepsp", "std_fm1.char", "Weapon shopkeeper"),
                     (451, 19, "boy1", "gon_boy.char", "Boy"),
                     (457, 14, "mogiri", "std_man7.char", "Ropeway attendant"),
                     (576, 5, "OYAJI", "utai_woman.char", "Materia shopkeeper"),
                     (650, 10, "wepsp", "std_oldm3.char", "Weapon shopkeeper"),
                     (650, 11, "bozu1", "snow_child.char", "Child"),
                     (651, 12, "innman1", "std_man7.char", "Inn staff"),
                     (651, 13, "innman2", "snow_man.char", "Inn staff"),
                     (651, 14, "gesm1", "kosta_sman1.char", "Guest"),
                     (651, 15, "gesm2", "kosta_sman2.char", "Guest"),
                     (330, 10, "lady", "std_fw1.char", "Woman")
                 })
        {
            var memory = new NpcMemory(entity, entityName, field);
            var targets = memory
                .Reader(model, ["Something said out loud."])
                .ReadTargets(new FieldPositionSnapshot(1, field, 0, 0, 0, 0, 0, 0));
            Equal(1, targets.Count, $"{field}:{entity} is somebody to walk to");
            Equal(expected, targets[0].Label, $"{field}:{entity} keeps its visible role");
        }
    }

    /// <summary>
    /// The town animals answer with a bark and an animation rather than a message, and the
    /// shop and hotel staff whose Talk only opens a menu or hands off to another script are
    /// no less real for saying nothing themselves.
    /// </summary>
    private static void AnimalsAndSilentActorsAreOffered()
    {
        foreach (var (field, entity, entityName, model, expected) in new[]
                 {
                     (360, 25, "dog", "animal_dog2.char", "Dog"),
                     // A reviewed field, so this one needs its own row to survive.
                     (284, 14, "dog1", "animal_dog1.char", "Dog"),
                     (368, 13, "cat5", "animal_cat102.char", "Cat"),
                     (587, 16, "DOGA", "animal_dog1.char", "Dog"),
                     (721, 10, "cat", "animal_cat2.char", "Cat"),
                     (270, 11, "n_woman", "std_woman5.char", "Woman"),
                     (492, 7, "noppo", "gold_noppo.char", "Hotel attendant"),
                     (492, 8, "semusi", "gold_semusi.char", "Hotel receptionist")
                 })
        {
            var memory = new NpcMemory(entity, entityName, field);
            var targets = memory
                .Reader(model, ["Something said out loud."])
                .ReadTargets(new FieldPositionSnapshot(1, field, 0, 0, 0, 0, 0, 0));
            Equal(1, targets.Count, $"{field}:{entity} is somebody to walk to");
            Equal(expected, targets[0].Label, $"{field}:{entity} is described by what it is");
        }
    }

    /// <summary>
    /// Corel prison's gate keeper and the Ghost Hotel's proprietor head their own dialogue
    /// with their name, and the speaker test used to throw both away over the full stop.
    /// </summary>
    private static void ATitleWithAFullStopIsStillAName()
    {
        foreach (var (field, entity, entityName, model, expected) in new[]
                 {
                     (477, 8, "corts", "korel_korts.char", "Mr.Coates"),
                     (495, 4, "kubi", "gold_hang.char", "Mr. Hangman")
                 })
        {
            var memory = new NpcMemory(entity, entityName, field);
            var targets = memory
                .Reader(model, [expected, "“Welcome.”"])
                .ReadTargets(new FieldPositionSnapshot(1, field, 0, 0, 0, 0, 0, 0));
            Equal(1, targets.Count, $"{field}:{entity} is somebody to walk to");
            Equal(expected, targets[0].Label, $"{field}:{entity} keeps the name it gives");
        }
    }

    /// <summary>
    /// "std_fm1" looks like it ought to mean female, and it does not: Mt Corel's resident
    /// on that mesh is an out-of-work miner talking about his bulldozer, while North
    /// Corel's runs the weapon shop and Icicle Inn's is drinking in the bar. It is the
    /// game's ordinary adult townsperson, so it is given a reviewed label where one is
    /// wanted and never turned into a gender by the look of the token.
    /// </summary>
    private static void AnAmbiguousMeshIsNotGuessedIntoAGender()
    {
        var reviewed = new NpcMemory(7, "fm1", 465);
        var targets = reviewed
            .Reader("std_fm1.char", ["Something said out loud."])
            .ReadTargets(new FieldPositionSnapshot(1, 465, 0, 0, 0, 0, 0, 0));
        Equal(1, targets.Count, "465:7 is somebody to walk to");
        Equal("Townsperson", targets[0].Label, "465:7 keeps a reviewed, neutral label");

        // Anywhere without a reviewed row the mesh stays unspoken rather than guessed.
        var unreviewed = new NpcMemory(9, "fm2", 464);
        Equal(
            0,
            unreviewed
                .Reader("std_fm1.char", ["Something said out loud."])
                .ReadTargets(new FieldPositionSnapshot(1, 464, 0, 0, 0, 0, 0, 0)).Count,
            "std_fm1 is not read as a woman");
    }

    /// <summary>
    /// Airship crew keep their own role even when the entity name also matches sailors.
    /// </summary>
    private static void CrewRolesDistinguishAirshipAndShipUniforms()
    {
        foreach (var (model, expected) in new[]
                 { ("rocket_crew1.char", "Crew member"), ("rocket_crew2.char", "Crew member"),
                   ("shinra_crew.char", "Sailor") })
        {
            var targets = new NpcMemory(22, "crew1").Reader(model)
                .ReadTargets(new FieldPositionSnapshot(1, 441, 0, 0, 0, 0, 0, 0));
            Equal(expected, targets.Single().Label, "crew descriptions respect the native model family");
        }
    }

    private static void SpeakerHeadingsRequireLetters()
    {
        var targets = new NpcMemory(22, "zz").Reader(
                "unreviewed_mesh.char", ["...", "“Hello.”"])
            .ReadTargets(new FieldPositionSnapshot(1, 441, 0, 0, 0, 0, 0, 0));
        Equal(0, targets.Count, "punctuation alone is not a speaker name");
    }

    private static void AnUnrecognisedModelIsStillNotGuessedAt()
    {
        var memory = new NpcMemory(21, "zz");
        var reader = memory.Reader("del1kosta_something_new.char");
        Equal(
            0,
            reader.ReadTargets(new FieldPositionSnapshot(1, 441, 0, 0, 0, 0, 0, 0)).Count,
            "an unreviewed mesh stays unlabelled rather than being guessed at");
    }

    private sealed class NpcMemory(int entity, string entityName = "crew1", int fieldId = 441)
    {
        private const int EventTable = 0x02404000;
        private static readonly int Npc =
            EventTable + FieldNavigationObjectReader.FieldEventDataStride;

        public bool Visible = true;
        public bool TalkDisabled;
        public byte ModelId = 1;

        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressFieldNumModels) return 2;
            if (address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                address < FieldNavigationObjectReader.AddressFieldModelIdArray + 256)
            {
                return address == FieldNavigationObjectReader.AddressFieldModelIdArray + entity
                    ? ModelId
                    : (byte)0xff;
            }

            if (address == Npc + FieldNavigationObjectReader.VisibilityOffset)
            {
                return Visible ? (byte)1 : (byte)0;
            }

            if (address == Npc + FieldNavigationNpcReader.TalkDisabledOffset)
            {
                return TalkDisabled ? (byte)1 : (byte)0;
            }

            return 0;
        }

        public FieldNavigationNpcReader Reader(string model, IReadOnlyList<string>? dialogue = null) =>
            new(
                address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr
                    ? EventTable
                    : 0,
                _ => 48,
                ReadByte,
                (_, _) => dialogue ?? ["“There's no ship to Junon."],
                _ => [new FieldScriptNpcDefinition(fieldId, entity, entityName, [66], null, null, model)]);
    }

    private static void TownExitRemainsSelectableAlongsideOptionalVisits()
    {
        var memory = new NativeMemory();
        var position = new FieldPositionSnapshot(1, 443, 0, -1160, -282, -153, 43, 216);
        var reader = memory.CreateReader();
        // Reproduce the 2026-09-06 13:05:22Z "Story: none" report. Sleeping,
        // finishing Hojo, and Johnny's conversation must never be exit prerequisites.
        foreach (var flags in new byte[] { 0, 0x08, 0x10, 0x18, 0x20, 0x40, 0xFF })
        {
            memory.SetFlags(flags);
            var targets = reader.ReadTargets(position);
            var exit = Required(targets, TownExit);
            Equal(new FieldNavigationTriggerLine(-1209, -396, -143, -1661, -106, -143),
                exit.TriggerLine!.Value, "Costa must select gateway7 to world field13, not the dock or beach");
            Equal((-1435, -251, -143), (exit.X, exit.Y, exit.Z), "native world exit midpoint");
            Equal(true, exit.CompletesOnArrival, "gateway Story routes must use the controller's native crossing completion");
            Required(targets, Beach); // Visiting the beach remains possible after Hojo leaves.
        }

        memory.SetFlags(0);
        var firstVisit = reader.ReadTargets(position);
        foreach (var label in new[] { Beach, "Visit the inn (optional)", "Visit Johnny's house (optional)",
                     "Visit the villa (optional)", "Visit the bar (optional)" })
            Required(firstVisit, label);
    }

    private static void FirstDockDepartureUsesItsNativeEnabledLine()
    {
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        Equal(0, reader.ReadTargets(Position(441)).Count,
            "the initial dock route needs positive live LINE5 evidence");
        memory.EnabledLines.Add(5);
        var initial = Required(reader.ReadTargets(Position(441)), "Enter Costa del Sol");
        Equal(new FieldNavigationTriggerLine(1044, -1032, 128, 1044, -1222, 128),
            initial.TriggerLine!.Value, "first exit invokes441:5:2:54 MAPJUMP442");
        memory.EnabledLines.Clear();
        Equal(0, reader.ReadTargets(Position(441)).Count, "a disabled initial line must not leave a stale target");
        memory.SetFlags(1); // del12/heri Script4 byte178: the automatic dock scene finished.
        var revisit = Required(reader.ReadTargets(Position(441)), "Enter Costa del Sol");
        Equal(new FieldNavigationTriggerLine(1071, -1036, 142, 1062, -1219, 137),
            revisit.TriggerLine!.Value, "revisit must use ordinary gateway0 to443");
        Equal(0, reader.ReadTargets(Position(442)).Count, "del12 is an automatic scene, with no walking objective");
    }

    private static void HojoSceneUsesTheWomanAndItsActualCompletionFlags()
    {
        var memory = new NativeMemory();
        memory.ConfigureModel(12, 337, 351, 8); // Hojo's own Talk only displays an ellipsis.
        memory.ConfigureModel(13, 353, 231, 0);
        var reader = memory.CreateReader();
        var position = Position(449);
        Equal(false, reader.ReadTargets(position).Any(target => target.Label == Hojo),
            "the beach introduction must finish before a manual conversation objective appears");
        memory.SetFlags(0x08); //449:8:6:52: native beach introduction finished.
        var target = Required(reader.ReadTargets(position), Hojo);
        Equal(13, target.TriggerEntityId, "woman1 Talk134 invokes AD1; Hojo Talk does not start the scene");
        Equal((353, 231, 0), (target.X, target.Y, target.Z), "conversation must follow the native model position");
        Equal(false, target.CompletesOnArrival, "walking within range is not conversation completion");
        Required(reader.ReadTargets(position), ReturnToTown);
        memory.ConfigureModel(13, 355, 233, 0);
        Equal(355, Required(reader.ReadTargets(position), Hojo).X, "do not freeze an NPC at its authored position");
        memory.SetVisible(13, false);
        Equal(false, reader.ReadTargets(position).Any(candidate => candidate.Label == Hojo),
            "missing/hidden native woman must not invent a conversation target");
        memory.SetVisible(13, true);
        foreach (var flags in new byte[] { 0x18, 0x28, 0x38 })
        {
            memory.SetFlags(flags);
            Equal(false, reader.ReadTargets(position).Any(candidate => candidate.Label == Hojo),
                "Hojo completion or inn rest must retire the optional conversation");
            Required(reader.ReadTargets(position), ReturnToTown);
        }
    }

    private static void OptionalCompanionsRequireVisibleNativeModels()
    {
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        Required(reader.ReadTargets(Position(444)), ReturnToTown);
        Equal(false, reader.ReadTargets(Position(444)).Any(target => target.TriggerEntityId == 12),
            "Barret in the current party does not leave a visible sailor model in the inn");
        memory.ConfigureModel(12, -300, -44, -63);
        Required(reader.ReadTargets(Position(444)), "Talk to Barret (optional)");
        memory.SetFlags(0x20);
        Equal(false, reader.ReadTargets(Position(444)).Any(target => target.TriggerEntityId == 12),
            "resting retires the sailor scene even if a model sample remains visible briefly");

        memory.SetFlags(0);
        memory.ConfigureModel(14, -103, -148, 0);
        Required(reader.ReadTargets(Position(448)), "Talk to Johnny (optional)");
        memory.SetFlags(0x40); //448:14:1:304: the first personal conversation finished.
        Equal(false, reader.ReadTargets(Position(448)).Any(target => target.Label == "Talk to Johnny (optional)"),
            "Johnny's saved completion bit must retire the first conversation");
        memory.SetFlags(0x10);
        memory.SetVisible(12, false);
        Required(reader.ReadTargets(Position(448)), "Talk to Johnny (optional)");
        //448:14:1:147 checks Tifa's party membership: with Tifa absent from the
        //house, byte150 still reaches Johnny's original conversation at192.
        memory.ConfigureModel(12, -38, 288, 45);
        Required(reader.ReadTargets(Position(448)), "Talk to Tifa (optional)");
        Required(reader.ReadTargets(Position(448)), ReturnToTown);
        memory.SetVisible(12, false);
        Equal(false, reader.ReadTargets(Position(448)).Any(target => target.Label == "Talk to Tifa (optional)"),
            "Tifa must not appear when her native model is hidden because she is in the party");
    }

    private static void InteriorReturnsRemainAvailableWithoutOptionalInteractions()
    {
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        (int Field, string Label, FieldNavigationTriggerLine Line)[] exits =
        [
            (444, ReturnToTown, new(-290, -975, -63, -402, -975, -63)),
            (445, ReturnToTown, new(354, -24, -147, 424, 13, -167)),
            (446, ReturnToTown, new(-489, 331, 0, -489, 207, 0)),
            (447, "Return upstairs", new(293, -50, 119, 344, -41, 124)),
            (448, ReturnToTown, new(342, -322, -57, 379, -228, -95)),
            (449, ReturnToTown, new(-749, 863, 109, -685, 867, 105))
        ];
        foreach (var (field, label, line) in exits)
        {
            foreach (var flags in new byte[] { 0, 0x08, 0x18, 0x20, 0xFF })
            {
                memory.SetFlags(flags);
                var target = Required(reader.ReadTargets(Position(field)), label);
                Equal(line, target.TriggerLine!.Value, $"field{field} must use its actual return gateway");
                Equal(true, target.CompletesOnArrival, "native return must use crossing completion rather than a proximity pause");
            }
        }
    }

    private static void NativeCountersRequireCloseManualInteraction()
    {
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        (string Label, int Entity, FieldNavigationTriggerLine Line)[] counters =
        [
            ("Talk at the item shop (optional)", 5, new(1350, 1567, 0, 1210, 1679, 0)),
            ("Talk at the materia shop (optional)", 6, new(601, 1855, 0, 723, 1843, 0))
        ];
        foreach (var (label, entity, line) in counters)
        {
            Equal(false, reader.ReadTargets(Position(443)).Any(target => target.Label == label),
                "a disabled counter LINE must not invite a manual interaction");
            memory.EnabledLines.Add(entity);
            var target = Required(reader.ReadTargets(Position(443)), label);
            Equal(line, target.TriggerLine!.Value, "shop approach must use the native IFKEYON counter segment");
            Equal(31, target.InteractionRadius, "counter must stop inside the strict native player-radius32 threshold");
            Equal(false, target.CompletesOnArrival, "counter must pause for the player's manual OK, not cross or complete");
            memory.SetPlayerCollisionRadius(12);
            Equal(11, Required(reader.ReadTargets(Position(443)), label).InteractionRadius,
                "counter radius must follow native state, not a fixed configured distance");
            memory.SetPlayerCollisionRadius(0);
            Equal(false, reader.ReadTargets(Position(443)).Any(candidate => candidate.Label == label),
                "missing native collision radius must not invent a usable counter approach");
            Required(reader.ReadTargets(Position(443)), TownExit);
            memory.SetPlayerCollisionRadius(32);
            memory.EnabledLines.Remove(entity);
        }
    }

    private static void BallUsesOnlyItsVisibleLiveModel()
    {
        const string ball = "Kick the ball (optional)";
        var memory = new NativeMemory();
        var reader = memory.CreateReader();
        Equal(false, reader.ReadTargets(Position(443)).Any(target => target.Label == ball),
            "do not invent the ball from its authored coordinates");
        memory.ConfigureModel(23, 398, 1400, 0);
        memory.ConfigureModel(11, 78, 1495, 0);
        Required(reader.ReadTargets(Position(443)), "Talk to Red XIII (optional)");
        var target = Required(reader.ReadTargets(Position(443)), ball);
        Equal(23, target.TriggerEntityId, "ball must use native entity23 Talk, not a guessed nearby actor");
        Equal(false, target.CompletesOnArrival, "the player owns the kick's manual OK input");
        memory.ConfigureModel(23, 76, 1426, 0);
        var moved = Required(reader.ReadTargets(Position(443)), ball);
        Equal((76, 1426, 0), (moved.X, moved.Y, moved.Z),
            "the ball moves and must retain its live coordinates");
        memory.SetFlags(0xFF);
        Required(reader.ReadTargets(Position(443)), ball);
        Equal(false, reader.ReadTargets(Position(443)).Any(candidate => candidate.Label == "Talk to Red XIII (optional)"),
            "resting retires Red's optional scene even before a visible model sample changes");
        memory.SetVisible(23, false);
        Equal(false, reader.ReadTargets(Position(443)).Any(candidate => candidate.Label == ball),
            "a hidden ball has no current visible interaction");
    }

    private static void CostaObjectivesEndWhenMountCorelAdvancesTheStory()
    {
        var memory = new NativeMemory();
        memory.SetFlags(0x08);
        memory.ConfigureModel(13, 353, 231, 0);
        memory.EnabledLines.Add(5);
        var reader = memory.CreateReader();
        foreach (var moment in new[] { 415, 421 })
        {
            memory.SetGameMoment(moment);
            Required(reader.ReadTargets(Position(443)), TownExit);
            Required(reader.ReadTargets(Position(449)), Hojo);
        }
        // Mt.Corel458 produce Init21 writes422; Init26 retires the Costa scene with bit5.
        foreach (var moment in new[] { 414, 422, 427, 440, 1008 })
        {
            memory.SetGameMoment(moment);
            foreach (var field in Enumerable.Range(441, 9))
                Equal(0, reader.ReadTargets(Position(field)).Count,
                    $"first-Costa objective must not leak into field{field} at moment{moment}");
        }
    }

    private static void NativeGatewaysAreReachableFromTheirInstalledEntrances(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // Literal entry XY/triangles from the preceding native field gateways;
        // first441 is Cloud's disembark placement and443 also replays the live failure.
        (FieldPositionSnapshot Position, string Label)[] cases =
        [
            (new(1, 441, 0, -463, 785, 63, 225, 0), "Enter Costa del Sol"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), TownExit),
            (new(1, 443, 0, -1160, -282, -153, 43, 216), TownExit),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), Beach),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Visit the inn (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Visit Johnny's house (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Visit the villa (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Visit the bar (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Talk at the item shop (optional)"),
            (new(1, 443, 0, -1350, -602, 72, 130, 0), "Talk at the materia shop (optional)"),
            (new(1, 444, 0, -357, -906, -63, 6, 0), ReturnToTown),
            (new(1, 445, 0, 212, 170, 0, 2, 0), ReturnToTown),
            (new(1, 446, 0, -386, 278, 0, 57, 0), ReturnToTown),
            (new(1, 446, 0, -386, 278, 0, 57, 0), "Visit the basement (optional)"),
            (new(1, 447, 0, 325, -126, 0, 8, 0), "Return upstairs"),
            (new(1, 448, 0, 238, -270, 0, 10, 0), ReturnToTown),
            (new(1, 449, 0, -673, 748, 0, 11, 0), ReturnToTown)
        ];
        foreach (var (position, label) in cases)
        {
            var memory = new NativeMemory();
            memory.EnabledLines.Add(5);
            memory.EnabledLines.Add(6);
            var target = Required(memory.CreateReader().ReadTargets(position), label);
            var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(position.FieldId));
            Equal(true, planner.TryBuildRoute(position, target, out var route),
                $"field{position.FieldId} {label} must be reachable on installed triangles: {planner.LastDiagnostic}");
            Equal(target.TriggerLine, route.TargetTriggerLine, "planner must preserve the native exit segment");
        }
    }

    private static void BarretCanBeApproachedOutsideHisNativeLockedBathroom(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = new NativeMemory();
        memory.SetBoundaryTriangles(444, [90]); //444:12:0:35 IDLCK, while Barret is outside the party.
        memory.ConfigureModel(12, -300, -44, -63); //444:12:0:39, native triangle93.
        memory.SetModelTalkRadius(12, 200); //444:12:0:2 TALKR.
        var entrance = new FieldPositionSnapshot(1, 444, 0, -357, -906, -63, 6, 0);
        var target = Required(memory.CreateReader().ReadTargets(entrance), "Talk to Barret (optional)");
        Equal(232, target.InteractionRadius, "Barret's native radius200 must be added to player radius32");
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(444),
            new FieldBoundaryStateReader(memory.ReadInt32, memory.ReadByte, (_, _) => true));
        Equal(true, planner.TryBuildRoute(entrance, target, out var route),
            $"Barret must be reachable for Talk from outside the occupied bathroom: {planner.LastDiagnostic}");
        Equal(false, route.TrianglePath.Contains(90), "approaching Barret must not route through the occupied bathroom lock");
        var dx = target.X - (double)route.FinalApproach.X;
        var dy = target.Y - (double)route.FinalApproach.Y;
        var dz = target.Z - (double)route.FinalApproach.Z;
        Equal(true, dx * dx + dy * dy + dz * dz <= 232 * 232,
            "a route that stops outside the bathroom must still finish within native Talk range");
    }

    private static FieldNavigationTarget Required(IReadOnlyList<FieldNavigationTarget> targets, string label) =>
        targets.SingleOrDefault(target => target.Label == label) is var target && target.Label == label
            ? target
            : throw new InvalidOperationException($"Costa Story missing '{label}'; got [{string.Join("|", targets.Select(value => value.Label))}]");

    private static FieldPositionSnapshot Position(int field) => new(1, field, 0, 0, 0, 0, 0, 0);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }

    private static void True(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException(label);
    }

    private sealed class NativeMemory
    {
        private const int EventTable = 0x02700000;
        private const int FieldState = 0x02800000;
        private readonly Dictionary<int, byte> bytes = [];
        private readonly Dictionary<int, byte> models = [];
        public HashSet<int> EnabledLines { get; } = [];

        public NativeMemory()
        {
            WriteInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
            bytes[FieldPositionReader.AddressFieldNumModels] = 16;
            SetPlayerCollisionRadius(32);
            SetGameMoment(415);
        }

        public FieldStoryTargetReader CreateReader() => new(ReadInt32, ReadInt16, ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), EnabledLines.Contains);
        public void SetFlags(byte flags) => bytes[FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + 224] = flags;
        public void SetPlayerCollisionRadius(short radius)
        {
            bytes[EventTable + 0x72] = (byte)radius;
            bytes[EventTable + 0x73] = (byte)(radius >> 8);
        }
        public void SetModelTalkRadius(int entity, short radius)
        {
            var address = EventTable + models[entity] * FieldNavigationObjectReader.FieldEventDataStride + 0x74;
            bytes[address] = (byte)radius;
            bytes[address + 1] = (byte)(radius >> 8);
        }
        public void SetBoundaryTriangles(int field, IEnumerable<int> triangles)
        {
            bytes[FieldPositionReader.AddressCurrentModule] = 1;
            bytes[FieldPositionReader.AddressFieldId] = (byte)field;
            bytes[FieldPositionReader.AddressFieldId + 1] = (byte)(field >> 8);
            WriteInt32(FieldBoundaryStateReader.AddressFieldGlobalObjectPtr, FieldState);
            foreach (var triangle in triangles)
            {
                var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
                bytes[address] = (byte)(ReadByte(address) | (1 << (triangle & 7)));
            }
        }
        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }
        public void ConfigureModel(int entity, int x, int y, int z)
        {
            if (!models.TryGetValue(entity, out var model))
                models[entity] = model = (byte)(models.Count + 1);
            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = model;
            var address = EventTable + model * FieldNavigationObjectReader.FieldEventDataStride;
            SetVisible(entity, true);
            WriteInt32(address + FieldNavigationObjectReader.PositionXOffset, x * 4096);
            WriteInt32(address + FieldNavigationObjectReader.PositionYOffset, y * 4096);
            WriteInt32(address + FieldNavigationObjectReader.PositionZOffset, z * 4096);
        }
        public void SetVisible(int entity, bool visible) => bytes[EventTable + models[entity] *
            FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = visible ? (byte)1 : (byte)0;
        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);
        private short ReadInt16(int address) => unchecked((short)(ReadByte(address) | ReadByte(address + 1) << 8));
        public int ReadInt32(int address) => ReadByte(address) | ReadByte(address + 1) << 8 |
            ReadByte(address + 2) << 16 | ReadByte(address + 3) << 24;
        private void WriteInt32(int address, int value)
        {
            for (var index = 0; index < 4; index++)
                bytes[address + index] = (byte)(value >> (index * 8));
        }
    }
}
