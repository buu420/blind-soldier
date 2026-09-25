using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// People the player talks to by walking into them. Their Talk does nothing and their whole
/// conversation is in Contact, script 2, which the engine runs from the collision routine
/// rather than from a button.
///
/// <para>Native rules (root's full export, reports/root-ledger-review-remaining.md):
/// 00636C41 tests each step at three probes, the player's collision width ahead of the next
/// position along the heading and forty-five degrees either side. FUN_00637724 skips the
/// moving model and any model whose +0x5F collision byte is set, needs a height difference
/// strictly inside (-127, 128), and touches when a probe's squared horizontal distance is
/// below the square of half the sum of both unsigned +0x72 widths; only a touch by the
/// controlled model sets the other model's +0x5E. FUN_0060C94D then starts that entity's
/// script 2 at priority 1 without reading its Talk flag, and 0060D29B skips an entry whose
/// first byte is RET.</para>
///
/// <para>Shared by both test hosts, so the Steam 2026 build runs the same catalog and reader
/// against its own archive.</para>
/// </summary>
internal static class FieldContactNpcTests
{
    private const int SyntheticField = 900;
    private const short PlayerWidth = 34;
    private const short PersonWidth = 30;

    public static void Run()
    {
        SomebodyWhoAnswersOnlyWhenWalkedIntoIsCatalogued();
        CompanionsDirectorsAndSilentContactsAreNot();
        AContactIsOfferedWhileItCanBeTouched();
        ItIsNotOfferedWhileItCannotBe();
        TheReachIsWhereTheMovementProbesTouch();
        ArrivalSaysWalkIntoItAndNeverAsksForAButton();
        RootsCounterexampleKeepsWalkingUntilTheContactStarts();
        TheContactIsSeenInTheEntitysOwnScriptState();
        TheWholeEncounterIsFollowedThroughItsDialogue();
    }

    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.WriteLine("field contact NPCs: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        var catalog = new FieldScriptNavigationCatalog(root);
        var text = new FlevelFieldTextResolver(root);
        TheWholeGameHasExactlyTheseContactPeople(root, catalog);
        BugenhagenIsWalkedIntoAtTheFootOfTheObservatory(catalog, text);
        IcicleInnsSoldiersAnswerWhenBumped(catalog, text);
        TheWallMarketInnPromoterAnswersWhenBumped(catalog, text);
        StoredMateriaAndTheDebugWarpAreNotPeople(catalog, text);
        Console.WriteLine("field contact NPC tests passed with installed game data.");
    }

    // Synthetic fields: entity 0 directs, entity 1 is the player (CHAR, PC 0: model 0), and
    // entity 2 is the one under test (model 1 when it loads one).
    private static SyntheticFieldScript FieldWith(Action<SyntheticScriptCode> init, Action<SyntheticScriptCode> talk, Action<SyntheticScriptCode> contact)
    {
        var field = new SyntheticFieldScript();
        var code = field.Code;
        code.Label("dirInit").Ret().Label("dirMain").Ret();
        code.Label("heroInit").Char(0).Pc(0).Ret().Label("heroMain").Ret().Label("heroTalk").Ret();
        code.Label("npcInit");
        init(code);
        code.Label("npcMain").Ret();
        code.Label("npcTalk");
        talk(code);
        code.Label("npcContact");
        contact(code);
        field.Entity("dir", (0, "dirInit"));
        field.Entity("hero", (0, "heroInit"), (1, "heroTalk"));
        field.Entity("man2", (0, "npcInit"), (1, "npcTalk"), (2, "npcContact"));
        return field;
    }

    /// <summary>
    /// mrkt2's inn promoter in miniature: a model whose Talk only turns him round (PDIRA) and
    /// whose Contact speaks. He is catalogued as walked into, with Contact's dialogue.
    /// </summary>
    private static void SomebodyWhoAnswersOnlyWhenWalkedIntoIsCatalogued()
    {
        var npc = FieldWith(
                init => init.Char(1).Ret(),
                talk => talk.Raw(0x34, 0x00).Ret(),
                contact => contact.Message(2, 15).Ret())
            .Read(SyntheticField).Npcs.Where(definition => definition.EntityId == 2)
            .Cast<FieldScriptNpcDefinition?>().SingleOrDefault();
        True(npc is { ContactOnly: true }, "a model that answers only when walked into is catalogued as a Contact");
        Equal("15", string.Join(",", npc?.DialogIds ?? []), "its dialogue is what Contact shows");

        var talker = FieldWith(
                init => init.Char(1).Ret(),
                talk => talk.Message(0, 7).Ret(),
                contact => contact.Message(0, 8).Ret())
            .Read(SyntheticField).Npcs.Where(definition => definition.EntityId == 2)
            .Cast<FieldScriptNpcDefinition?>().SingleOrDefault();
        True(talker is { ContactOnly: false }, "somebody who answers Talk stays a Talk NPC");
        Equal("7", string.Join(",", talker?.DialogIds ?? []), "and is described by what Talk says");

        var menu = FieldWith(
                init => init.Char(1).Ret(),
                talk => talk.Ret(),
                contact => contact.Raw(0x49, 0x00, 0x03, 0x00).Ret())
            .Read(SyntheticField).Npcs.Where(definition => definition.EntityId == 2)
            .Cast<FieldScriptNpcDefinition?>().SingleOrDefault();
        True(menu is { ContactOnly: true }, "a Contact that opens a menu is an interaction too");
    }

    /// <summary>
    /// Only a drawn, non-party model with a Contact that shows something. A PC-bound companion
    /// is the party (PC hides it and switches its collision off); a model-less director is
    /// nothing to walk into; an empty Contact is skipped by 0060D29B; and a Contact that only
    /// moves things shows the player nothing to go to.
    /// </summary>
    private static void CompanionsDirectorsAndSilentContactsAreNot()
    {
        (string What, Action<SyntheticScriptCode> Init, Action<SyntheticScriptCode> Contact)[] cases =
        [
            ("a PC-bound companion", init => init.Char(1).Pc(2).Ret(), contact => contact.Message(0, 15).Ret()),
            ("an entity with no model", init => init.Ret(), contact => contact.Message(0, 15).Ret()),
            ("a Contact whose first byte is RET", init => init.Char(1).Ret(), contact => contact.Ret().Message(0, 15).Ret()),
            ("a Contact that only waits and writes", init => init.Char(1).Ret(), contact => contact.Wait(4).SetByte(1, 40, 1).Ret())
        ];

        foreach (var (what, init, contact) in cases)
        {
            var npcs = FieldWith(init, talk => talk.Ret(), contact).Read(SyntheticField).Npcs;
            True(npcs.All(definition => definition.EntityId != 2), $"{what} is not somebody to walk into");
        }
    }

    private static FieldScriptNpcDefinition ContactPerson(string model = "std_man7.char", int field = SyntheticField) =>
        new(field, 2, "man2", [15], ModelResourceName: model) { ContactOnly = true };

    /// <summary>
    /// Visible and solid is enough; Talk switched off (TLKON 1, +0x61) does not matter, since
    /// the collision routine never reads it. The target stands on the model and names it.
    /// </summary>
    private static void AContactIsOfferedWhileItCanBeTouched()
    {
        var memory = new ContactMemory().Place(entity: 2, model: 1, x: 120, y: -40, z: 6, talkOn: false);
        var found = memory.Read([ContactPerson()]);
        Equal(1, found.Count, "a visible, solid person with Talk off is still walked into");
        var target = found[0];
        Equal("Man", target.Label, "labelled from the mesh like any townsperson");
        Equal(FieldNavigationActivation.Contact, target.Activation, "reached by walking into him");
        Equal(2, target.TriggerEntityId, "his own entity runs the Contact");
        Equal("120,-40,6", $"{target.X},{target.Y},{target.Z}", "the target is where the model stands");
        True(target.TriggerLine is null, "no counter line stands in for a Contact");
    }

    private static void ItIsNotOfferedWhileItCannotBe()
    {
        (string What, ContactMemory Memory)[] cases =
        [
            ("an invisible model (VISI 0, +0x62)", new ContactMemory().Place(2, 1, 0, 0, 0, visible: false)),
            ("a model with collision off (SOLID 1, +0x5F)", new ContactMemory().Place(2, 1, 0, 0, 0, solid: false)),
            ("the controlled model itself", new ContactMemory().Place(2, 0, 0, 0, 0)),
            ("an entity with no model loaded", new ContactMemory())
        ];

        foreach (var (what, memory) in cases)
        {
            Equal(0, memory.Read([ContactPerson()]).Count, $"{what} cannot be walked into");
        }

        // Stored materia and other fieldbg_ scenery are Objects and Story targets, never people.
        Equal(0, new ContactMemory().Place(2, 1, 0, 0, 0).Read([ContactPerson("fieldbg_4hmaty.char")]).Count,
            "scenery that runs a Contact is not offered as a person");

        // For contrast, the ordinary Talk path still needs Talk switched on.
        var talker = new FieldScriptNpcDefinition(SyntheticField, 2, "man2", [15], ModelResourceName: "std_man7.char");
        Equal(0, new ContactMemory().Place(2, 1, 0, 0, 0, talkOn: false).Read([talker]).Count,
            "a Talk NPC with Talk off is not offered");
        Equal(1, new ContactMemory().Place(2, 1, 0, 0, 0, talkOn: true).Read([talker]).Count,
            "and is offered with Talk on");
    }

    /// <summary>
    /// The forward probe is the player's width (34) ahead of the next position and touches
    /// inside half the two ushort widths at +0x72 ((34 + 30) / 2 = 32), so walking at him
    /// touches with the centre 66 away plus the step being taken, at most 8.
    /// </summary>
    private static void TheReachIsWhereTheMovementProbesTouch()
    {
        var ordinary = new ContactMemory().Place(2, 1, 0, 0, 0).Read([ContactPerson()]).Single();
        Equal(74, ordinary.InteractionRadius, "one width ahead, inside the half-sum, one native step out");
        Equal(FieldNavigationNpcReader.ContactReach(PlayerWidth, PersonWidth), ordinary.InteractionRadius,
            "the reach the Story rows use too");

        var wide = new ContactMemory().Place(2, 1, 0, 0, 0, width: unchecked((short)0xFFF0)).Read([ContactPerson()]).Single();
        Equal(PlayerWidth + (PlayerWidth + 0xFFF0) / 2 + 8, wide.InteractionRadius, "a width is read unsigned, as the engine reads it");
    }

    private static void ArrivalSaysWalkIntoItAndNeverAsksForAButton()
    {
        var target = new ContactMemory().Place(2, 1, 0, 0, 0).Read([ContactPerson()]).Single();
        // 70 away, the next step's forward probe lands 70 - 8 - 34 = 28 from him, inside 32.
        var near = At(-70);
        var approaching = Track(target, near, contactStarted: false);
        True(approaching.Speech.Contains("Walk into it", StringComparison.Ordinal), $"a Contact person says what triggers it, got '{approaching.Speech}'");
        True(!approaching.Speech.Contains("Interact here", StringComparison.Ordinal) &&
             !approaching.Speech.Contains("Confirm", StringComparison.OrdinalIgnoreCase),
            $"nothing is pressed at a Contact, got '{approaching.Speech}'");
        True(approaching.BeaconStillOn && approaching.AutoWalkMoves, "the route and auto walk go on into him");

        var touched = Track(target, near, contactStarted: true);
        True(touched.Speech.Contains("reached", StringComparison.Ordinal) && !touched.BeaconStillOn && !touched.AutoWalkMoves,
            $"his Contact script starting ends the route and auto walk, got '{touched.Speech}'");

        // 80 away even the longest step leaves the forward probe 38 from him.
        Equal("", Track(target, At(-80), contactStarted: false).Speech, "short of where the probes touch nothing is said");
    }

    /// <summary>
    /// Root's executable counterexample (reports/root-contact-arrival-review.md): both widths 16,
    /// the person at x 0, the party at -34 moving two units a step. The next centre, -32, puts the
    /// forward probe at -16, exactly the half-sum away, and 00637724's comparison is strict, so
    /// there is no contact yet. The route must keep moving the party here, not turn itself off.
    /// </summary>
    private static void RootsCounterexampleKeepsWalkingUntilTheContactStarts()
    {
        var memory = new ContactMemory().Place(2, 1, 0, 0, 0, width: 16);
        memory.PlayerCollisionWidth = 16;
        var target = memory.Read([ContactPerson()]).Single();
        Equal(16 + 16 + 8, target.InteractionRadius, "the reach for two 16-wide models");
        var before = Track(target, At(-34), contactStarted: false);
        True(before.BeaconStillOn && before.AutoWalkMoves, $"at -34 the party is still walked on into him, got '{before.Speech}'");
        var after = Track(target, At(-32), contactStarted: true);
        True(!after.BeaconStillOn && !after.AutoWalkMoves, "once the game has started his Contact, it stops");
    }

    /// <summary>
    /// FieldContactActivationReader reads the target entity's own current priority
    /// (0x00CC0B30[entity]) and the script at it (0x00CBF9E8[entity * 8 + priority]) twice, as
    /// FieldScriptControllerReader does. Only script 2 at priority 1 - what 0060D29B records for a
    /// touch - is a started Contact.
    /// </summary>
    private static void TheContactIsSeenInTheEntitysOwnScriptState()
    {
        var target = new ContactMemory().Place(2, 1, 0, 0, 0).Read([ContactPerson()]).Single();
        (string What, byte Priority, byte Script, bool Started)[] cases =
        [
            ("script 2 at priority 1", 1, 2, true),
            ("Talk, script 1 at priority 1", 1, 1, false),
            ("its Main at priority 7", 7, 0, false),
            ("script 2 asked for at priority 6", 6, 2, false)
        ];
        foreach (var (what, priority, script, started) in cases)
        {
            var state = new ScriptState(SyntheticField);
            state.Run(target.TriggerEntityId, priority, script);
            Equal(started, new FieldContactActivationReader(state).HasStarted(target), what);
        }

        var otherField = new ScriptState(SyntheticField + 1);
        otherField.Run(target.TriggerEntityId, 1, 2);
        Equal(false, new FieldContactActivationReader(otherField).HasStarted(target), "another field's scripts say nothing");

        var torn = new ScriptState(SyntheticField) { TearPriority = true };
        torn.Run(target.TriggerEntityId, 1, 2);
        Equal(false, new FieldContactActivationReader(torn).HasStarted(target), "a priority that changes between the two reads is not trusted");

        var talk = target with { Activation = FieldNavigationActivation.Talk };
        var running = new ScriptState(SyntheticField);
        running.Run(talk.TriggerEntityId, 1, 2);
        Equal(false, new FieldContactActivationReader(running).HasStarted(talk), "only a Contact target is asked about");
    }

    /// <summary>
    /// Root's lifecycle (reports/root-contact-suppression-blocker.md), through one controller and
    /// one memory that the production NPC reader and activation reader both read. The party walks
    /// up to a person who stays visible and solid afterwards (mrkt2's promoter talks every time).
    /// Walking into him starts his Contact (priority 1, script 2); his dialogue suppresses
    /// navigation for as long as the script runs; the script returns (his Main, priority 7) before
    /// the game hands the party back. The route must finish then - not resume and bump him again -
    /// and must say nothing over his dialogue.
    /// </summary>
    private static void TheWholeEncounterIsFollowedThroughItsDialogue()
    {
        var memory = new EncounterMemory(SyntheticField, entity: 2, playerWidth: 16, personWidth: 16);
        var reader = new FieldNavigationNpcReader(memory.Int32, memory.Int16, memory.Byte, (_, _) => [], _ => [ContactPerson()]);
        var activation = new FieldContactActivationReader(memory);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], npcTargetProvider: position => reader.ReadTargets(position)),
            new StraightRoutePlanner())
        {
            ContactStarted = activation.HasStarted
        };
        var rotation = new FieldNavigationControlTransform(0);
        var start = new DateTime(2026, 9, 25, 7, 0, 0, DateTimeKind.Utc);
        controller.HandleAction(FieldNavigationAction.NextCategory, At(-100), rotation);
        controller.HandleAction(FieldNavigationAction.NextCategory, At(-100), rotation);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, At(-100), rotation);
        string? Step(int x, bool suppressed, int milliseconds) =>
            controller.UpdateLiveTracking(At(x), new FieldNavigationInputSnapshot(0, FieldNavigationInput.None), rotation,
                suppressed, 80, observedAt: start.AddMilliseconds(milliseconds))?.Speech;
        bool Moves(int x) => controller.TryResolveAutomaticInput(At(x), rotation, 80, out var input) && input != FieldNavigationInput.None;

        memory.Run(7, 0);
        Step(-34, false, 0);
        True(controller.BeaconEnabled && Moves(-34), "before the bump the route walks on into him");

        memory.Run(1, 2);
        Equal(null, Step(-32, true, 50), "nothing is said over his dialogue");
        True(!Moves(-32), "and auto walk does not push while his Contact has run");

        memory.Run(7, 0);
        var handedBack = Step(-32, false, 100);
        True(handedBack?.Contains("reached", StringComparison.Ordinal) == true && !controller.BeaconEnabled,
            $"when the game hands the party back the route is finished, got '{handedBack}'");
        for (var milliseconds = 150; milliseconds <= 1100; milliseconds += 50)
        {
            Step(-32, false, milliseconds);
        }

        True(!controller.BeaconEnabled && !Moves(-32), "a second later it still does not walk into him again");
        Equal(1, reader.ReadTargets(At(-32)).Count, "he is still there to be walked into on purpose");
    }

    private static FieldPositionSnapshot At(int x) => new(FieldPositionReader.FieldModule, SyntheticField, 0, x, 0, 0, 0, 0);

    private static (string Speech, bool BeaconStillOn, bool AutoWalkMoves) Track(
        FieldNavigationTarget target,
        FieldPositionSnapshot position,
        bool contactStarted)
    {
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], npcTargetProvider: _ => [target]),
            new StraightRoutePlanner())
        {
            ContactStarted = started => started.StableId == target.StableId && contactStarted
        };
        var rotation = new FieldNavigationControlTransform(0);
        controller.HandleAction(FieldNavigationAction.NextCategory, position, rotation);
        controller.HandleAction(FieldNavigationAction.NextCategory, position, rotation);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, rotation);
        True(controller.BeaconEnabled, "the route to the person is active");
        var speech = controller.UpdateLiveTracking(
            position,
            new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
            rotation,
            isSuppressed: false,
            arrivalDistanceUnits: 80)?.Speech ?? "";
        var moves = controller.TryResolveAutomaticInput(position, rotation, 80, out var input) && input != FieldNavigationInput.None;
        return (speech, controller.BeaconEnabled, moves);
    }

    /// <summary>
    /// One field's memory as both readers see it: the event table and model state the NPC reader
    /// reads, and the script table, priorities and running scripts FieldScriptControllerReader reads.
    /// </summary>
    private sealed class EncounterMemory : ILegacyAddressSpace
    {
        private const int Events = 0x03000000;
        private const int ScriptTable = 0x02000000;
        private readonly Dictionary<int, byte> bytes = [];
        private readonly int entity;

        public EncounterMemory(int fieldId, int entity, short playerWidth, short personWidth)
        {
            this.entity = entity;
            Put(FieldPositionReader.AddressCurrentModule, (byte)FieldPositionReader.FieldModule);
            Put(FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)fieldId));
            Put(FieldPositionReader.AddressFieldNumModels, 2);
            Put(FieldScriptControllerReader.AddressFieldScriptPointer, BitConverter.GetBytes(ScriptTable));
            Put(ScriptTable + (int)FieldScriptControllerReader.FieldScriptEntityCountOffset, 3);
            Put(FieldNavigationObjectReader.AddressFieldEventDataPtr, BitConverter.GetBytes(Events));
            for (var index = 0; index < 256; index++)
            {
                Put(FieldScriptControllerReader.AddressEntityModelIds + index, 0xFF);
            }

            Put(FieldScriptControllerReader.AddressEntityModelIds + entity, 1);
            for (var offset = 0; offset < FieldNavigationObjectReader.FieldEventDataStride * 2; offset++)
            {
                Put(Events + offset, 0);
            }

            var person = Events + FieldNavigationObjectReader.FieldEventDataStride;
            Put(Events + FieldNavigationNpcReader.CollisionRadiusOffset, BitConverter.GetBytes(playerWidth));
            Put(person + FieldNavigationNpcReader.CollisionRadiusOffset, BitConverter.GetBytes(personWidth));
            Put(person + FieldNavigationObjectReader.VisibilityOffset, 1);
            Put(person + FieldNavigationNpcReader.TalkDisabledOffset, 1);
            Put(person + FieldNavigationNpcReader.CollisionDisabledOffset, 0);
            Put(FieldScriptControllerReader.AddressModelAnimationRunState + 1, 0);
        }

        public void Run(byte priority, byte script)
        {
            Put(FieldScriptControllerReader.AddressEntityScriptPriorities + entity, priority);
            Put(FieldScriptControllerReader.AddressEntityScriptIds + entity * FieldScriptControllerReader.ScriptSlotsPerEntity + priority, script);
        }

        public int Int32(int address) => Byte(address) | Byte(address + 1) << 8 | Byte(address + 2) << 16 | Byte(address + 3) << 24;

        public short Int16(int address) => (short)(Byte(address) | Byte(address + 1) << 8);

        public byte Byte(int address) => bytes.GetValueOrDefault(address);

        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue((int)address + index, out destination[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private void Put(int address, params byte[] values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                bytes[address + index] = values[index];
            }
        }
    }

    /// <summary>The script and model state FieldScriptControllerReader reads, for one field.</summary>
    private sealed class ScriptState(int fieldId) : ILegacyAddressSpace
    {
        private const uint ScriptTable = 0x02000000;
        private const uint ModelTable = 0x03100000;
        private readonly Dictionary<uint, byte> bytes = new();
        private int reads;

        public bool TearPriority { get; init; }

        public void Run(int entity, byte priority, byte script)
        {
            Byte((uint)FieldPositionReader.AddressCurrentModule, (byte)FieldPositionReader.FieldModule);
            Word((uint)FieldPositionReader.AddressFieldId, (ushort)fieldId);
            Dword((uint)FieldScriptControllerReader.AddressFieldScriptPointer, ScriptTable);
            Byte(ScriptTable + FieldScriptControllerReader.FieldScriptEntityCountOffset, 16);
            Byte((uint)(FieldScriptControllerReader.AddressEntityScriptPriorities + entity), priority);
            Byte((uint)(FieldScriptControllerReader.AddressEntityScriptIds + entity * FieldScriptControllerReader.ScriptSlotsPerEntity + priority), script);
            Byte((uint)(FieldScriptControllerReader.AddressEntityModelIds + entity), 1);
            Dword((uint)FieldScriptControllerReader.AddressModelTablePointer, ModelTable);
            Byte((uint)(FieldScriptControllerReader.AddressModelAnimationRunState + 1), 0);
            var model = ModelTable + (uint)FieldScriptControllerReader.ModelStride;
            Byte(model + FieldScriptControllerReader.ModelVisibilityOffset, 1);
            Byte(model + FieldScriptControllerReader.ModelAnimationIdOffset, 0);
            Word(model + FieldScriptControllerReader.ModelCurrentFrameOffset, 0);
            Word(model + FieldScriptControllerReader.ModelEndFrameOffset, 0);
            priorityAddress = (uint)(FieldScriptControllerReader.AddressEntityScriptPriorities + entity);
        }

        private uint priorityAddress = uint.MaxValue;

        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                var at = address + (uint)index;
                if (!bytes.TryGetValue(at, out var value))
                {
                    return false;
                }

                // A tear: the priority reads differently on the second capture.
                destination[index] = TearPriority && at == priorityAddress && reads++ > 0 ? (byte)(value + 1) : value;
            }

            return true;
        }

        private void Byte(uint address, byte value) => bytes[address] = value;

        private void Word(uint address, ushort value)
        {
            bytes[address] = (byte)value;
            bytes[address + 1] = (byte)(value >> 8);
        }

        private void Dword(uint address, uint value)
        {
            for (var index = 0; index < 4; index++)
            {
                bytes[address + (uint)index] = (byte)(value >> (index * 8));
            }
        }
    }

    /// <summary>
    /// Every entity the catalog reads as walked into, across the whole archive. A new one means
    /// the discovery changed and has to be looked at like these were.
    /// </summary>
    private static void TheWholeGameHasExactlyTheseContactPeople(string root, FieldScriptNavigationCatalog catalog)
    {
        var source = new FlevelDataSource(root);
        var found = source.FieldNames
            .Where(field => source.HasField(field.Key))
            .SelectMany(field => catalog.ReadField(field.Key).Npcs.Where(npc => npc.ContactOnly))
            .Select(npc => $"{npc.FieldId}:{npc.EntityId}")
            .Order(StringComparer.Ordinal);
        Equal("195:17,534:5,542:11,542:12,542:13,542:14,654:22,654:23,654:24,93:2", string.Join(",", found),
            "the Contact people: the Wall Market inn promoter, Bugenhagen, cosmo3's four stored materia, " +
            "Icicle Inn's three soldiers and blackbg1's developer warp");
    }

    /// <summary>
    /// cosin5 BUGEN (534:5). Init <c>7E01C701A400</c> starts him hidden, untalkable and not solid,
    /// and <c>7E00C700A401</c> shows him only while GameMoment &lt; 514 and bank 3[170] bit 5. Talk
    /// is a bare RET; Contact <c>400002</c> says "Good. Then we shall proceed." and sends him into
    /// the cave, clearing that bit and unlocking triangle 51.
    /// </summary>
    private static void BugenhagenIsWalkedIntoAtTheFootOfTheObservatory(FieldScriptNavigationCatalog catalog, FlevelFieldTextResolver text)
    {
        True(HasOpcode(catalog, 534, 5, 0, "7E01") && HasOpcode(catalog, 534, 5, 0, "7E00") &&
             HasOpcode(catalog, 534, 5, 0, "A401") && HasOpcode(catalog, 534, 5, 0, "1430AA2006"),
            "native cosin5 e5 Init shows him under 3[170] & 0x20");
        Equal("00", Convert.ToHexString(catalog.ReadScriptOpcodes(534, 5, 1).First().Bytes.ToArray()), "his Talk is a bare RET");
        True(HasOpcode(catalog, 534, 5, 2, "400002"), "his Contact shows dialogue 2");

        var bugenhagen = catalog.ReadField(534).Npcs.Single(npc => npc.EntityId == 5);
        True(bugenhagen.ContactOnly, "Bugenhagen is walked into");
        var shown = new ContactMemory().Place(5, 3, -73, 378, -3, talkOn: false).Read([bugenhagen], text);
        Equal("Bugenhagen|Contact", string.Join(",", shown.Select(target => $"{target.Label}|{target.Activation}")),
            "offered by his own dialogue heading, walked into");
        Equal(0, new ContactMemory().Place(5, 3, -73, 378, -3, visible: false, solid: false).Read([bugenhagen], text).Count,
            "not offered while Init keeps him hidden and not solid");
        Equal(0, new ContactMemory().Place(5, 3, -73, 378, -3, solid: false).Read([bugenhagen], text).Count,
            "not offered once his Contact has switched his collision off");
    }

    /// <summary>
    /// snow sinrah1..3 (654:22-24): shown only while bank 1[130] bit 0 is set and bit 7 clear.
    /// Talk is RET; Contact asks "This village is now under martial law!" (<c>48050013010210</c>).
    /// </summary>
    private static void IcicleInnsSoldiersAnswerWhenBumped(FieldScriptNavigationCatalog catalog, FlevelFieldTextResolver text)
    {
        foreach (var entity in new[] { 22, 23, 24 })
        {
            True(HasOpcode(catalog, 654, entity, 2, "48050013"), $"native snow e{entity} Contact asks dialogue 19");
            var soldier = catalog.ReadField(654).Npcs.Single(npc => npc.EntityId == entity);
            True(soldier.ContactOnly, $"soldier {entity} is walked into");
            Equal("Shinra soldier|Contact",
                string.Join(",", new ContactMemory().Place(entity, 4, 0, 0, 0).Read([soldier], text).Select(target => $"{target.Label}|{target.Activation}")),
                $"soldier {entity} is offered as what the player sees");
            Equal(0, new ContactMemory().Place(entity, 4, 0, 0, 0, visible: false, solid: false).Read([soldier], text).Count,
                $"soldier {entity} is not offered while Init hides him");
        }
    }

    /// <summary>
    /// mrkt2 man2 (195:17): Talk <c>3400</c> only turns him round; Contact says "Hey, you two. Why
    /// don't you get some rest?" (<c>40020F</c>) or dialogue 16 (<c>400210</c>) by party.
    /// </summary>
    private static void TheWallMarketInnPromoterAnswersWhenBumped(FieldScriptNavigationCatalog catalog, FlevelFieldTextResolver text)
    {
        Equal("3400", Convert.ToHexString(catalog.ReadScriptOpcodes(195, 17, 1).First().Bytes.ToArray()), "his Talk only turns him");
        True(HasOpcode(catalog, 195, 17, 2, "40020F") && HasOpcode(catalog, 195, 17, 2, "400210"), "his Contact speaks");
        var promoter = catalog.ReadField(195).Npcs.Single(npc => npc.EntityId == 17);
        True(promoter.ContactOnly, "the promoter is walked into");
        Equal("Man|Contact",
            string.Join(",", new ContactMemory().Place(17, 5, 0, 0, 0).Read([promoter], text).Select(target => $"{target.Label}|{target.Activation}")),
            "offered under his reviewed label and walked into, not talked to");
    }

    /// <summary>
    /// cosmo3's stored Huge Materia (542:11-14) are fieldbg_ scenery whose Contact runs the Master
    /// Materia blend; the Story list already offers each lit stand as a Contact
    /// (HugeMateriaContactTests). blackbg1's Tifa (93:2) opens a developer warp menu with no
    /// speaker. Neither is offered as a person.
    /// </summary>
    private static void StoredMateriaAndTheDebugWarpAreNotPeople(FieldScriptNavigationCatalog catalog, FlevelFieldTextResolver text)
    {
        foreach (var (field, entity) in new[] { (542, 11), (542, 12), (542, 13), (542, 14), (93, 2) })
        {
            var definition = catalog.ReadField(field).Npcs.Single(npc => npc.EntityId == entity);
            Equal(0, new ContactMemory().Place(entity, 1, 0, 0, 0).Read([definition], text).Count,
                $"{field}:{entity} is not offered as a person");
        }
    }

    private static bool HasOpcode(FieldScriptNavigationCatalog catalog, int field, int entity, int script, string hex) =>
        catalog.ReadScriptOpcodes(field, entity, script)
            .Any(opcode => Convert.ToHexString(opcode.Bytes.ToArray()).StartsWith(hex, StringComparison.OrdinalIgnoreCase));

    /// <summary>A field's event table with the player on model 0.</summary>
    private sealed class ContactMemory
    {
        private const int Events = 0x03000000;
        private readonly Dictionary<int, int> words = [];
        private readonly Dictionary<int, short> halves = [];
        private readonly Dictionary<int, byte> bytes = [];

        public ContactMemory()
        {
            words[FieldNavigationObjectReader.AddressFieldEventDataPtr] = Events;
            bytes[FieldPositionReader.AddressFieldNumModels] = 16;
            halves[Model(0) + FieldNavigationNpcReader.CollisionRadiusOffset] = PlayerWidth;
        }

        public short PlayerCollisionWidth
        {
            set => halves[Model(0) + FieldNavigationNpcReader.CollisionRadiusOffset] = value;
        }

        private static int Model(int index) => Events + index * FieldNavigationObjectReader.FieldEventDataStride;

        public ContactMemory Place(
            int entity,
            int model,
            int x,
            int y,
            int z,
            bool visible = true,
            bool solid = true,
            bool talkOn = false,
            short width = PersonWidth)
        {
            var at = Model(model);
            bytes[FieldNavigationObjectReader.AddressFieldModelIdArray + entity] = (byte)model;
            words[at + FieldNavigationObjectReader.PositionXOffset] = x * 4096;
            words[at + FieldNavigationObjectReader.PositionYOffset] = y * 4096;
            words[at + FieldNavigationObjectReader.PositionZOffset] = z * 4096;
            bytes[at + FieldNavigationObjectReader.VisibilityOffset] = visible ? (byte)1 : (byte)0;
            bytes[at + FieldNavigationNpcReader.CollisionDisabledOffset] = solid ? (byte)0 : (byte)1;
            bytes[at + FieldNavigationNpcReader.TalkDisabledOffset] = talkOn ? (byte)0 : (byte)1;
            if (model != 0)
            {
                halves[at + FieldNavigationNpcReader.CollisionRadiusOffset] = width;
            }

            return this;
        }

        public IReadOnlyList<FieldNavigationTarget> Read(
            IReadOnlyList<FieldScriptNpcDefinition> definitions,
            FlevelFieldTextResolver? text = null)
        {
            var fieldId = definitions.Count == 0 ? SyntheticField : definitions[0].FieldId;
            var reader = new FieldNavigationNpcReader(
                address => words.GetValueOrDefault(address),
                address => halves.GetValueOrDefault(address),
                address => bytes.TryGetValue(address, out var value)
                    ? value
                    : address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                      address < FieldNavigationObjectReader.AddressFieldModelIdArray + 256
                        ? (byte)0xFF
                        : (byte)0,
                (field, dialog) => text?.ReadMessageLinesById(field, dialog) ?? [],
                _ => definitions);
            return reader.ReadTargets(new FieldPositionSnapshot(FieldPositionReader.FieldModule, fieldId, 0, 0, 0, 0, 0, 0));
        }
    }

    private sealed class StraightRoutePlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight Contact test route";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return true;
        }

        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(
                position.FieldId,
                $"{target.FieldId}:{target.StableId}",
                [position.TriangleId],
                [],
                new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z),
                position.TriangleId);
            return position.FieldId == target.FieldId;
        }

        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
            return position.FieldId == target.FieldId;
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Field contact NPCs: " + message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Field contact NPCs: {message}: expected {expected}, got {actual}");
        }
    }
}
