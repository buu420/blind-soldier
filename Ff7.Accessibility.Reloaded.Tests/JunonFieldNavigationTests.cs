using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class JunonFieldNavigationTests
{
    private const int EventTable = FieldPositionReader.AddressFieldModelsObjs;

    public static void Run()
    {
        AirportLiftMustPrecedeTheBarracks();
        LockerRoomReadyChoiceMustExposeNativeParadeExit();
        WelcomeShortcutUsesTheNativeAlleyInsteadOfTheDockRoad();
        ParadeDemonstrationMustLeadToTheLiveParadeBeforeSendOffPractice();
        PostPracticeLockerExitUsesTheOrdinaryGateway();
        PostPracticeAirportPathContinuesTowardTheDock();
        UnknownOrTornParadeLineMustNotAuthorizeAnExit();
        NavigationReadRejectsTornAndMissingNativeState();
        CargoShipConversationPrerequisitesConnectBothFields();
        BarretObjectiveSurvivesTheFirstShipConversation();
        CargoShipAlarmAndBossSequenceHasManualHandoffs();
        EmptyCountMaskCannotAuthorizeAStoryObjective();
        FirstVisitJunonFollowsNativeStateSequence();
        LaterJunonObjectiveDoesNotLeakIntoFirstVisit();
        ReturningDolphinIsOfferedOnlyWhileAdPollsTheWhistle();
        TheDolphinRouteEndsOnTriangle25AtAnyArrivalDistance();
        SoldierCollectiblesPublishUntilTheirNativeBitsAreSet();
        StoryCatalogContainsNoStaleWhiteBackgroundDuplicate();
    }

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        Run();
        AirportLiftHandoffRespectsNativeBoundarySets(createWalkmeshReader);
        AirportControlsArrivalUsesNativeCoordinateSpace(createWalkmeshReader);
        ManualJunonHandoffsUseReachableNativeEntrances(createWalkmeshReader);
        WelcomeShortcutRoutesThroughTheNativeAlley(createWalkmeshReader);
        ShipPrerequisiteModelsAndGatewaysAreNativelyReachable(createWalkmeshReader);
        CargoShipAlarmRoutesUseNativeTransitions(createWalkmeshReader);
        RoutesAroundAerisToPriscillasNativeTriggerLine(createWalkmeshReader);
    }

    private static void AirportLiftMustPrecedeTheBarracks()
    {
        var memory = new NativeMemory();
        memory.SetGameMoment(403);
        memory.ConfigureVisibleModel(14, 14574, 13889, 5103);
        var reader = CreateStoryReader(memory);
        var player = Position(384);

        var beforeLift = reader.ReadTargets(player);
        Equal(1, beforeLift.Count, "the lift prerequisite and blocked barracks exit must never be offered together");
        var controls = beforeLift.Single();
        Equal("Use the airport lift controls", controls.Label,
            "the first-visit airport must expose the lift interaction before its blocked barracks exit");
        Equal(14, controls.TriggerEntityId, "the airport prerequisite must use native box0's Talk script");
        Equal(false, controls.CompletesOnArrival,
            "reaching the lift controls must not claim that the player has operated them");

        memory.WriteBankByte(1, 226, 0x40);
        var barracks = reader.ReadTargets(player).Single();
        Equal("Continue into the Junon barracks", barracks.Label,
            "the native lift-bit transition must hand Story to the barracks exit");
        Equal(new FieldNavigationTriggerLine(12480, 14290, 5139, 12512, 14825, 5139),
            barracks.TriggerLine!.Value, "the post-lift target must remain the real barracks gateway");

        memory.WriteBankByte(1, 226, 0);
        Equal("Use the airport lift controls", reader.ReadTargets(player).Single().Label,
            "raising the lift again must restore the prerequisite instead of advertising a blocked exit");
        memory.SetGameMoment(406);
        Equal(false, reader.ReadTargets(player).Any(target =>
                target.Label is "Use the airport lift controls" or "Continue into the Junon barracks"),
            "the initial barracks objective must end when Cloud has changed uniform");
    }

    private static void LockerRoomReadyChoiceMustExposeNativeParadeExit()
    {
        var memory = new NativeMemory();
        memory.SetField(387);
        memory.SetGameMoment(406);
        memory.WriteBankByte(5, 17, 1);
        memory.ConfigureVisibleModel(18, -1770, -635, 605);
        memory.SetLineEnabled(12, false);
        var reader = CreateStoryReader(memory);
        var player = Position(387);
        Equal(1, reader.ReadTargets(player).Count,
            "a disabled parade LINE must not compete with the captain objective");
        Equal("Talk to the captain and say you are ready for the parade",
            reader.ReadTargets(player).Single().Label,
            "before the ready choice the captain, not the disabled exit, is the next step");

        memory.SetModelVisible(18, false);
        Equal(0, reader.ReadTargets(player).Count,
            "a hidden captain alone must not invent permission to use the exit");
        memory.SetLineEnabled(12, true);
        var afterReady = reader.ReadTargets(player);
        Equal(1, afterReady.Count,
            "accepting parade readiness must leave one Story target after the captain disappears");
        Equal("Follow the captain out to the parade", afterReady.Single().Label,
            "the opened native LINE must bridge readiness to the parade without requiring its completion bit");
        Equal(new FieldNavigationTriggerLine(-1335, -606, 605, -1335, -690, 605),
            afterReady.Single().TriggerLine!.Value, "the parade exit must retain border2's exact native span");

        memory.SetLineEnabled(12, false);
        Equal(0, reader.ReadTargets(player).Count,
            "a closed parade LINE must withdraw its Story target");
        memory.SetLineEnabled(12, true);
        memory.WriteBankByte(3, 235, 0x10);
        Equal(0, reader.ReadTargets(player).Count,
            "parade completion must retire the pre-parade exit before send-off practice finishes");
        memory.WriteBankByte(3, 236, 1);
        Equal("Leave the locker room for the send-off", reader.ReadTargets(player).Single().Label,
            "the later post-parade handoff must keep its own native practice-complete condition");
    }

    private static void WelcomeShortcutUsesTheNativeAlleyInsteadOfTheDockRoad()
    {
        var memory = new NativeMemory();
        memory.SetField(360);
        memory.SetGameMoment(406);
        // junonr1/AD sets bit 1 before the demonstration; junonr4/direct
        // sets bit 2 when it returns to the barracks. The movie sets 236 bit 4.
        memory.WriteBankByte(3, 235, 0x86);
        memory.WriteBankByte(3, 236, 0x10);
        var reader = CreateStoryReader(memory);
        var target = reader.ReadTargets(Position(360)).Single();
        Equal(new FieldNavigationTriggerLine(634, 347, 0, 492, 361, 0),
            target.TriggerLine!.Value,
            "the late soldiers' Story route must enter the native alley to the live parade, not the dock road");
        Equal(563, target.X, "the welcome shortcut must point to border3's X midpoint");
        Equal(354, target.Y, "the welcome shortcut must point to border3's Y midpoint");
        var definition = FieldStoryEventCatalog.CreateAllFields().Single(candidate =>
            candidate.FieldId == 360 && candidate.Label == target.Label);
        Equal(10, definition.EntityId, "the welcome shortcut must identify the native border3 entity");

        (int Moment, byte Parade, byte Practice)[] otherPhases =
        [
            (405, 0x06, 0x10), // Before the uniform/parade sequence.
            (406, 0x04, 0x10), // No late-squad branch: direct tests bit 1.
            (406, 0x02, 0x10), // The first demonstration has not returned yet.
            (406, 0x06, 0x00), // The movie handoff has not started.
            (406, 0x16, 0x10), // The native send-off-practice branch has replaced it.
            (409, 0x06, 0x10)  // The dock send-off is over.
        ];
        foreach (var phase in otherPhases)
        {
            memory.SetGameMoment(phase.Moment);
            memory.WriteBankByte(3, 235, phase.Parade);
            memory.WriteBankByte(3, 236, phase.Practice);
            Equal(false, reader.ReadTargets(Position(360)).Any(candidate => candidate.Label == target.Label),
                $"the welcome alley must not leak into moment {phase.Moment}, flags {phase.Parade:X2}/{phase.Practice:X2}");
        }

        memory.SetGameMoment(406);
        memory.WriteBankByte(3, 235, 0x96);
        memory.WriteBankByte(3, 236, 0x11);
        var dock = reader.ReadTargets(Position(360)).Single();
        Equal("Follow the road toward the dock", dock.Label,
            "completed button practice must retain its distinct forward dock route");
        Equal(new FieldNavigationTriggerLine(-2088, -100, 0, -2385, -897, 0),
            dock.TriggerLine!.Value, "only the later dock route should use Gateway 2");
    }

    private static void ParadeDemonstrationMustLeadToTheLiveParadeBeforeSendOffPractice()
    {
        var memory = new NativeMemory();
        memory.SetField(386);
        memory.SetGameMoment(406);
        memory.WriteBankByte(3, 235, 0x06); // junonr4/direct byte 142: first demonstration finished.
        var reader = CreateStoryReader(memory);

        var target = reader.ReadTargets(Position(386)).Single();
        Equal(
            "Continue to the Upper Junon ceremony",
            target.Label,
            "the parade MAPJUMP must preserve the native ceremony before the locker-room practice");
        Equal(9, FieldStoryEventCatalog.CreateAllFields().Single(definition =>
                definition.FieldId == 386 && definition.Label == target.Label).EntityId,
            "the post-parade definition must use border4, whose Move script starts the Junon movie");
        Equal(
            new FieldNavigationTriggerLine(-756, -766, 767, -900, -771, 767),
            target.TriggerLine!.Value,
            "the post-parade handoff must use border4's native line to field 359, not Gateway 1 to the locker room");
        Equal(false, reader.ReadTargets(Position(386)).Any(candidate =>
                candidate.Label.Contains("locker room", StringComparison.OrdinalIgnoreCase)),
            "the locker room must not be offered before junonr2 sets the practice-start bit");

        memory.WriteBankByte(3, 236, 0x10); // junin1/border4 byte 28: the movie transition started.
        memory.SetField(360);
        var ceremony = reader.ReadTargets(Position(360)).Single();
        Equal(
            "Follow the soldiers to the welcoming ceremony",
            ceremony.Label,
            "after the movie, Story must follow the native alley to junonr4's live parade");
        Equal(
            new FieldNavigationTriggerLine(634, 347, 0, 492, 361, 0),
            ceremony.TriggerLine!.Value,
            "the post-movie handoff must retain junonr1 border3's exact native span");

        memory.WriteBankByte(3, 235, 0x14); // junonr2/taityo script 8 byte 113: practice may start.
        memory.SetField(387);
        Equal(0, reader.ReadTargets(Position(387)).Count,
            "the automatic locker-room practice must not advertise a manual objective while it runs");
        memory.WriteBankByte(3, 236, 0x11); // junin1a/AD4 byte 169: practice finished.
        Equal("Leave the locker room for the send-off", reader.ReadTargets(Position(387)).Single().Label,
            "native practice completion must hand Story to the send-off route");
    }

    private static void PostPracticeLockerExitUsesTheOrdinaryGateway()
    {
        var memory = new NativeMemory();
        memory.SetField(387);
        memory.SetGameMoment(406);
        memory.WriteBankByte(3, 235, 0x14);
        memory.WriteBankByte(3, 236, 0x10);
        // junin1a/border2 Main disables the pre-parade shortcut on entry.
        // AD4 ends the button drill by unlocking triangle 33, not that LINE.
        memory.SetLineEnabled(12, false);
        var reader = CreateStoryReader(memory);
        Equal(0, reader.ReadTargets(Position(387)).Count,
            "the locker-room exit waits for native button-practice completion");

        memory.WriteBankByte(3, 236, 0x11); // AD4/Script 1 byte 169.
        var target = reader.ReadTargets(Position(387)).Single();
        Equal("Leave the locker room for the send-off", target.Label,
            "finishing the button drill retains a usable locker-room handoff");
        Equal(new FieldNavigationTriggerLine(-1269, -688, 605, -1272, -615, 605),
            target.TriggerLine!.Value,
            "post-practice Story must reach Gateway 0 to field 386, beyond the disabled parade LINE");

        memory.SetGameMoment(409); // jundoc1a/taityo Script 20 byte 68.
        Equal(0, reader.ReadTargets(Position(387)).Count,
            "finishing the native send-off retires the old locker-room handoff");
    }

    private static void PostPracticeAirportPathContinuesTowardTheDock()
    {
        var memory = new NativeMemory();
        memory.SetField(386);
        memory.SetGameMoment(406);
        memory.WriteBankByte(3, 235, 0x14);
        memory.WriteBankByte(3, 236, 0x10);
        var reader = CreateStoryReader(memory);
        Equal(0, reader.ReadTargets(Position(386)).Count,
            "the send-off road is not offered before the button drill finishes");

        foreach (var flags in new byte[] { 0x11, 0x13 })
        {
            // junin1/direct byte 54 adds bit 1 on the first post-practice visit;
            // it must not make the next exit disappear when the soldiers leave.
            memory.WriteBankByte(3, 236, flags);
            var targets = reader.ReadTargets(Position(386));
            Equal(1, targets.Count,
                "leaving the locker room after button practice must retain one forward Story exit");
            Equal("Continue toward the dock", targets.Single().Label,
                "the airport path continues to the road, not back into the locker room or airport");
            Equal(new FieldNavigationTriggerLine(-896, -818, 767, -760, -818, 767),
                targets.Single().TriggerLine!.Value,
                "post-practice Story must use Gateway 0 to field 360, not the earlier ceremony LINE");
        }

        memory.WriteBankByte(3, 235, 0x04);
        Equal(false, reader.ReadTargets(Position(386)).Any(target => target.Label == "Continue toward the dock"),
            "the dock handoff cannot replace the ceremony before its native completion flag");
        memory.WriteBankByte(3, 235, 0x14);
        memory.SetGameMoment(408);
        Equal("Continue toward the dock", reader.ReadTargets(Position(386)).Single().Label,
            "the forward handoff remains available until the send-off advances the story");
        memory.SetGameMoment(409);
        Equal(0, reader.ReadTargets(Position(386)).Count,
            "the completed send-off cannot leak its old airport-path objective");
    }

    private static void UnknownOrTornParadeLineMustNotAuthorizeAnExit()
    {
        var memory = new NativeMemory();
        memory.SetField(387);
        memory.SetGameMoment(406);
        memory.SetLineEnabled(12, true);
        var withoutLineEvidence = new FieldStoryTargetReader(
            memory.ReadInt32, memory.ReadInt16, memory.ReadByte, FieldStoryEventCatalog.CreateAllFields());
        Equal(0, withoutLineEvidence.ReadTargets(Position(387)).Count,
            "a host without live LINE evidence must not publish the parade exit");

        var reader = CreateStoryReader(memory);
        memory.UnreadableAddress = FieldScriptLineStateReader.AddressFieldLineStates +
            2 * FieldScriptLineStateReader.LineStateStride;
        Equal(0, reader.ReadTargets(Position(387)).Count,
            "unreadable translated LINE state is not permission to use the parade exit");
        memory.UnreadableAddress = null;
        var stateReads = 0;
        memory.BeforeRead = address =>
        {
            if (address == FieldScriptLineStateReader.AddressFieldLineStates +
                    2 * FieldScriptLineStateReader.LineStateStride && ++stateReads == 2)
            {
                memory.SetLineEnabled(12, false);
            }
        };
        Equal(0, reader.ReadTargets(Position(387)).Count,
            "a LINE which closes during confirmation must not publish a transient exit");
        memory.BeforeRead = null;
        memory.SetLineEnabled(12, true);
        Equal("Follow the captain out to the parade", reader.ReadTargets(Position(387)).Single().Label,
            "recovering coherent LINE evidence must restore the missing handoff");
    }

    private static void NavigationReadRejectsTornAndMissingNativeState()
    {
        var memory = new NativeMemory();
        memory.SetField(384);
        memory.SetPlayerPosition(14524, 13889, 5103, 187, renderOffsetZ: 624);
        var reader = new FieldPositionReader(memory);
        var position = reader.ReadNavigation();
        Equal(true, position.IsUsable, "checked navigation position is readable");
        Equal(5103, position.Position.Z, "checked native position must exclude the lift's drawing offset");
        memory.UnreadableAddress = EventTable + FieldPositionReader.ObjectZOffset;
        Equal(false, reader.ReadNavigation().IsUsable,
            "missing native position must fail closed instead of falling back to rendered coordinates");
        Equal(true, reader.Read().IsUsable, "the independent rendered read must remain available");
        memory.UnreadableAddress = null;
        var xReads = 0;
        memory.BeforeRead = address =>
        {
            if (address == EventTable + FieldPositionReader.ObjectXOffset && ++xReads == 2)
            {
                // Even less than one unit of native motion must be detected
                // before fixed-point coordinates are rounded for navigation.
                memory.WriteInt32(EventTable + FieldPositionReader.ObjectXOffset, 14524 * 4096 + 1);
            }
        };
        Equal(false, reader.ReadNavigation().IsUsable,
            "native position confirmation must compare the full fixed-point words, not rounded coordinates");
    }

    private static void CargoShipConversationPrerequisitesConnectBothFields()
    {
        IReadOnlyList<FieldNavigationTarget> Read(int field, byte flags, int moment = 409)
        {
            var memory = new NativeMemory();
            memory.SetField(field);
            memory.SetGameMoment(moment);
            memory.WriteBankByte(3, 184, flags);
            if (field == 439)
            {
                memory.ConfigureVisibleModel(11, -77, 386, 0); // shpin_2 EARITH2, not ship_1's guard.
            }
            else
            {
                memory.ConfigureVisibleModel(11, 515, 338, -24303); // ship_1 TIFA2
                memory.ConfigureVisibleModel(14, -181, -897, -24308); // ship_1 RED2
            }
            return CreateStoryReader(memory).ReadTargets(Position(field));
        }

        Equal("Talk to Aeris", string.Join("|", Read(439, 0).Select(target => target.Label)),
            "the ship hold must expose the first real conversation, not an empty Story list");
        foreach (var aerisAnswer in new byte[] { 0x01, 0x02 })
        {
            Equal("Go on deck to speak with the others", Read(439, aerisAnswer).Single().Label,
                "either native Aeris answer must hand off to the deck conversations");
            var onDeck = Read(436, aerisAnswer);
            Equal("Talk to Red XIII|Talk to Tifa",
                string.Join("|", onDeck.Select(target => target.Label).OrderBy(label => label, StringComparer.Ordinal)),
                "before the guard moves Story must name the remaining party conversations, not his blocked passage");
            foreach (var tifaAnswer in new byte[] { 0x04, 0x08 })
            {
                var twoConversations = (byte)(aerisAnswer | tifaAnswer);
                Equal("Talk to Red XIII", Read(436, twoConversations).Single().Label,
                    "either native Tifa answer counts; no preferred dialogue choice may be required");
                var threeConversations = (byte)(twoConversations | 0x40);
                Equal("Return inside to speak with Aeris", Read(436, threeConversations).Single().Label,
                    "three conversation flags must send the player back to the actual unlock writer");
                var ask = Read(439, threeConversations).Single();
                Equal("Ask Aeris about Barret", ask.Label, "Aeris's threshold branch is the required next Talk");
                Equal(false, ask.CompletesOnArrival, "reaching Aeris must not pretend that she has moved the guard");
                Equal("Go on deck to find Barret", Read(439, (byte)(threeConversations | 0x80)).Single().Label,
                    "Aeris's native bit-7 write must hand off through the field-reentry gateway");
                Equal("Go to the cargo ship deck", Read(436, (byte)(threeConversations | 0x80)).Single().Label,
                    "the old foredeck target must be offered only after the guard's prerequisite");
            }
        }
        Equal("Return inside to speak with Aeris", Read(436, 0x15).Single().Label,
            "Yuffie's native conversation may count instead of Red XIII; do not force one party route");
        Equal("Ask Aeris about Barret", Read(439, 0x54).Single().Label,
            "three other conversation flags must not require an extra first Aeris conversation");
        for (var flags = 0; flags < 128; flags++)
        {
            var nativeCount = Enumerable.Range(0, 7).Count(bit => (flags & (1 << bit)) != 0);
            Equal(nativeCount >= 3, Read(439, (byte)flags).Any(target => target.Label == "Ask Aeris about Barret"),
                $"Aeris readiness must match the native seven-flag tally for 0x{flags:X2}");
        }
        Equal(false, Read(439, 0x80, 415).Any(target =>
                target.Label is "Talk to Aeris" or "Ask Aeris about Barret" or "Go on deck to find Barret"),
            "the stowaway alert must retire the first-visit conversation sequence");
    }

    private static void BarretObjectiveSurvivesTheFirstShipConversation()
    {
        var memory = new NativeMemory();
        memory.SetField(437);
        memory.SetGameMoment(409);
        memory.ConfigureVisibleModel(3, -67, -135, 0);
        var reader = CreateStoryReader(memory);
        var first = reader.ReadTargets(Position(437)).Single();
        Equal(3, first.TriggerEntityId, "the ship objective must resolve native BALLET2");
        Equal(false, first.CompletesOnArrival,
            "reaching Barret must not complete either of his native conversations");
        memory.WriteBankByte(5, 0, 1); // BALLET2/Talk 50: first conversation only.
        Equal(first, reader.ReadTargets(Position(437)).Single(),
            "the first Barret conversation must retain Story for the second Talk");
        memory.WriteBankByte(3, 185, 1);
        memory.SetGameMoment(415); // BALLET2/Talk 306: actual progression.
        Equal(false, reader.ReadTargets(Position(437)).Any(target => target.StableId == first.StableId),
            "the native stowaway alert must retire the old Barret objective");
    }

    private static void CargoShipAlarmAndBossSequenceHasManualHandoffs()
    {
        foreach (var (field, label) in new[]
                 {
                     (437, "Return to the main deck after the alarm"),
                     (436, "Go below deck after the alarm"),
                     (439, "Enter the engine room")
                 })
        {
            var memory = new NativeMemory();
            memory.SetField(field);
            memory.SetGameMoment(415);
            var reader = CreateStoryReader(memory);
            Equal(label, string.Join("|", reader.ReadTargets(Position(field)).Select(target => target.Label)),
                $"the alarm must retain a manual story handoff in field {field}");
            foreach (var outsideWindow in new[] { 414, 416, 1253 })
            {
                memory.SetGameMoment(outsideWindow);
                Equal(false, reader.ReadTargets(Position(field)).Any(target => target.Label == label),
                    $"the alarm handoff in field {field} must not leak into moment {outsideWindow}");
            }
        }

        var engineRoom = new NativeMemory();
        engineRoom.SetField(440);
        engineRoom.SetGameMoment(415);
        engineRoom.SetLineEnabled(15, true); // ELINE Init: scene-entry trigger.
        engineRoom.SetLineEnabled(18, false); // LINEJ Init: exit is not ready yet.
        var engineReader = CreateStoryReader(engineRoom);
        Equal("Investigate the engine room", string.Join("|", engineReader.ReadTargets(Position(440)).Select(target => target.Label)),
            "the engine-room scene must have its own reachable manual trigger before the boss");
        engineRoom.SetLineEnabled(15, false); // ELINE Go 1x byte 4; movement is frozen.
        Equal(0, engineReader.ReadTargets(Position(440)).Count,
            "the automatic boss scene must not invent another manual objective while both lines are disabled");
        // ELINE Go 1x byte 383 enables LINEJ before returning player control.
        engineRoom.SetLineEnabled(18, true);
        var leave = engineReader.ReadTargets(Position(440)).Single();
        Equal("Leave the engine room for docking", leave.Label,
            "after the boss the newly enabled native exit must replace the disabled encounter trigger");
        Equal(new FieldNavigationTriggerLine(-55, -670, 106, 54, -668, 104), leave.TriggerLine!.Value,
            "docking must use LINEJ's actual trigger, not the ordinary backtracking gateway");
        engineRoom.SetLineEnabled(18, false); // LINEJ Move byte 4, departing automatically.
        Equal(0, engineReader.ReadTargets(Position(440)).Count,
            "the docking cutscene must not re-offer either consumed trigger");
    }

    private static void EmptyCountMaskCannotAuthorizeAStoryObjective()
    {
        var memory = new NativeMemory();
        memory.SetGameMoment(409);
        var reader = new FieldStoryTargetReader(memory.ReadInt32, memory.ReadInt16, memory.ReadByte,
        [
            new FieldStoryEventDefinition(439, FieldStoryTargetKind.Location, "Malformed counted prerequisite",
                RequiredCondition: new FieldStoryStateCondition(3, 184, 0, 0, MinimumSetBits: 1))
        ]);
        Equal(0, reader.ReadTargets(Position(439)).Count,
            "an explicitly counted empty mask must not inherit the absent-condition wildcard");
    }

    private static void AirportLiftHandoffRespectsNativeBoundarySets(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // junair/idlkd Scripts 3 and 5. The airport lift swaps these sets;
        // neither the planner nor a test may pretend that they are all open.
        int[] loweredLiftLocks =
            [237, 253, 254, 255, 256, 257, 258, 262, 353, 359, 360, 361, 362, 2, 364, 365, 398];
        int[] raisedLiftLocks =
            [74, 73, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 102, 103, 117, 129, 130, 131, 138, 139];
        var memory = new NativeMemory();
        memory.SetField(384);
        memory.SetGameMoment(403);
        memory.ConfigureVisibleModel(14, 14574, 13889, 5103);
        memory.SetBoundaryTriangles(raisedLiftLocks);
        var reader = CreateStoryReader(memory);
        var planner = new FieldWalkmeshRoutePlanner(
            createWalkmeshReader(384),
            new FieldBoundaryStateReader(memory.ReadInt32, memory.ReadByte, (_, _) => true));
        // The log's rendered 5733 is native 5119 + lift OFST 624 - drawing origin 10.
        var entry = new FieldPositionSnapshot(1, 384, 0, 16361, 12005, 5119, 4, 0);
        var controls = reader.ReadTargets(entry).Single();
        Equal(true, planner.TryBuildRoute(entry, controls, out var controlsPlan),
            $"the real airport arrival must reach the required lift controls: {planner.LastDiagnostic}");
        Equal(false, controlsPlan.TrianglePath.Any(raisedLiftLocks.Contains),
            "the lift approach must not cross a native closed boundary");

        memory.WriteBankByte(1, 226, 0x40);
        var barracks = reader.ReadTargets(entry).Single();
        Equal(false, planner.TryBuildRoute(entry, barracks, out _),
            "the old direct-to-barracks route must still be rejected while the lift blocks it");

        memory.SetBoundaryTriangles(loweredLiftLocks);
        var afterLift = entry with { X = 14574, Y = 13889, Z = 5103, TriangleId = 187 };
        Equal(true, planner.TryBuildRoute(afterLift, barracks, out var barracksPlan),
            $"the native lift operation must open the next barracks leg: {planner.LastDiagnostic}");
        Equal(false, barracksPlan.TrianglePath.Any(loweredLiftLocks.Contains),
            "the post-lift route must respect the other set of closed boundaries");
        Equal(true, barracksPlan.FinalApproach.Y is >= 14290 and <= 14825,
            "the final approach must intersect the real barracks entrance span");
    }

    private static void AirportControlsArrivalUsesNativeCoordinateSpace(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var memory = new NativeMemory();
        memory.SetField(384);
        memory.SetGameMoment(403);
        memory.ConfigureVisibleModel(14, 14574, 13889, 5103);
        // Native default Talk reach: Cloud's collision radius 30 + box0's
        // talk radius 80. OFST changes drawing, not either collision position.
        memory.SetInteractionRadii(14, 30, 80);
        memory.SetPlayerPosition(14524, 13889, 5103, 187, renderOffsetZ: 624);
        var positionReader = new FieldPositionReader(
            memory.ReadInt32, memory.ReadInt16,
            address => unchecked((ushort)memory.ReadInt16(address)), memory.ReadByte);
        Equal(5717, positionReader.Read().Position.Z,
            "visible-motion consumers must keep the rendered lift position");
        var position = positionReader.ReadNavigation().Position;
        Equal(5103, position.Z, "navigation must use the same coordinate space as native Talk and walkmesh");
        var target = CreateStoryReader(memory).ReadTargets(position).Single();
        var tracker = new FieldNavigationRouteTracker(
            new FieldWalkmeshRoutePlanner(createWalkmeshReader(384)));
        Equal(true, tracker.TryStart(position, target, out var guidance),
            "the lift control approach must retain a real native route");
        Equal(true, guidance.RemainingDistance <= target.InteractionRadius,
            $"standing within native Talk range must not chase the lift's visual offset: remaining={guidance.RemainingDistance}");
    }

    private static void ManualJunonHandoffsUseReachableNativeEntrances(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        // These XY/triangle pairs are the preceding field's native gateway or
        // MAPJUMP destinations. Z is evaluated on that exact native triangle.
        // Locked triangles are the first-visit branch, not an all-open mesh.
        (int Field, int Moment, int X, int Y, int Z, ushort Triangle, int[] Locks, string Label)[] legs =
        [
            (386, 403, -1122, -565, 767, 49, [], "Enter the locker room"), // junair gateway 0
            (387, 406, -1542, -636, 605, 15, [], "Follow the captain out to the parade"), // cloud script 3, ready Talk clears 33
            (387, 406, -1378, -646, 605, 9, [], "Leave the locker room for the send-off"), // junin1 gateway 1; post-practice re-entry
            (386, 406, -1298, -662, 767, 51, [], "Continue toward the dock"), // junin1a gateway 0 after practice
            (360, 406, 6287, -668, 0, 166, [220], "Follow the road toward the dock"), // junin1 gateway 0; produce Init 2
            (361, 406, 6169, -4755, -3407, 6, [13], "Follow the road toward the dock"), // junonr1 gateway 2; direct Main 24
            (390, 406, -1045, 3791, 1219, 30, [], "Continue toward the dock"), // junonr2 gateway 1
            (371, 406, 4486, -783, -6, 39, [], "Continue toward the dock"), // junin3 gateway 0
            (370, 406, -525, -550, 0, 114, [], "Enter the dock for the send-off"), // junonl2 gateway 0
            (382, 409, -1302, 378, -32, 31, [1, 3], "Board the cargo ship"), // junonl1 border1/Move 20; produce Init
            (437, 409, 357, -519, 0, 32, [], "Talk to Barret to continue") // ship_1 LINEJ/Go 1x 4
        ];
        foreach (var leg in legs)
        {
            var memory = new NativeMemory();
            memory.SetField(leg.Field);
            memory.SetGameMoment(leg.Moment);
            memory.SetBoundaryTriangles(leg.Locks);
            if (leg.Label == "Follow the captain out to the parade")
            {
                memory.SetLineEnabled(12, true);
            }
            else
            {
                SetSendOffReady(memory);
                if (leg.Field == 387)
                {
                    memory.SetLineEnabled(12, false);
                }
            }
            if (leg.Field == 437)
            {
                memory.ConfigureVisibleModel(3, -67, -135, 0);
            }
            var position = new FieldPositionSnapshot(1, leg.Field, 0,
                leg.X, leg.Y, leg.Z, leg.Triangle, 0);
            var target = CreateStoryReader(memory).ReadTargets(position)
                .Single(candidate => candidate.Label == leg.Label);
            var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(leg.Field),
                new FieldBoundaryStateReader(memory.ReadInt32, memory.ReadByte, (_, _) => true));
            Equal(true, planner.TryBuildRoute(position, target, out var route),
                $"Junon handoff {leg.Field} must route to its actual next interaction: {planner.LastDiagnostic}");
            Equal(false, route.TrianglePath.Any(leg.Locks.Contains),
                $"Junon handoff {leg.Field} must not cross its native closed boundaries");
            if (target.TriggerLine is { } trigger)
            {
                Equal(true,
                    route.FinalApproach.X >= Math.Min(trigger.StartX, trigger.EndX) - 16 &&
                    route.FinalApproach.X <= Math.Max(trigger.StartX, trigger.EndX) + 16 &&
                    route.FinalApproach.Y >= Math.Min(trigger.StartY, trigger.EndY) - 16 &&
                    route.FinalApproach.Y <= Math.Max(trigger.StartY, trigger.EndY) + 16,
                    $"Junon handoff {leg.Field} must approach the native exit span, not a room centroid");
            }
        }

        // The post-demonstration state is a distinct native chain from the
        // later send-off walk above. Reproduce Brice's 2026-09-03 save position,
        // then the controllable Cloud position installed by junonr1/cl_hei
        // script 5 after movie-only field 359 finishes.
        (FieldPositionSnapshot Position, int[] Locks, string Label, Action<NativeMemory> Configure)[] beforeLiveParade =
        [
            (
                new FieldPositionSnapshot(1, 386, 0, -655, -298, 767, 33, 0),
                [],
                "Continue to the Upper Junon ceremony",
                memory => memory.WriteBankByte(3, 235, 0x06)),
            (
                new FieldPositionSnapshot(1, 360, 0, 4415, -783, 0, 137, 0),
                [], // junonr1/direct byte 125 unlocks the alley in the late-squad branch.
                "Follow the soldiers to the welcoming ceremony",
                memory =>
                {
                    memory.WriteBankByte(3, 235, 0x06);
                    memory.WriteBankByte(3, 236, 0x10);
                })
        ];
        foreach (var leg in beforeLiveParade)
        {
            var memory = new NativeMemory();
            memory.SetField(leg.Position.FieldId);
            memory.SetGameMoment(406);
            memory.SetBoundaryTriangles(leg.Locks);
            leg.Configure(memory);
            var target = CreateStoryReader(memory).ReadTargets(leg.Position)
                .Single(candidate => candidate.Label == leg.Label);
            var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(leg.Position.FieldId),
                new FieldBoundaryStateReader(memory.ReadInt32, memory.ReadByte, (_, _) => true));
            Equal(true, planner.TryBuildRoute(leg.Position, target, out var route),
                $"pre-parade Junon handoff {leg.Position.FieldId} must route to its native continuation: {planner.LastDiagnostic}");
            Equal(false, route.TrianglePath.Any(leg.Locks.Contains),
                $"pre-parade Junon handoff {leg.Position.FieldId} must not cross a native closed boundary");
        }

        // A native ground-floor control point, NOT an assertion of the initial
        // ship spawn: ship_1/CLOUD script 8 places this point after the alert.
        // Replay the early phase with the guard's triangle 102 still blocked:
        // the missing prerequisite must lead inside, not through this guard.
        var ship = new NativeMemory();
        ship.SetField(436);
        ship.SetGameMoment(409);
        ship.SetBoundaryTriangles([102]);
        var shipPosition = new FieldPositionSnapshot(1, 436, 0, 53, -32, -24604, 31, 0);
        ship.WriteBankByte(3, 184, 0x45); // Three conversations, but Aeris has not asked about Barret.
        var shipReader = CreateStoryReader(ship);
        var inside = shipReader.ReadTargets(shipPosition).Single();
        Equal("Return inside to speak with Aeris", inside.Label,
            "the locked foredeck must lead to its native unlock conversation");
        var shipPlanner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(436),
            new FieldBoundaryStateReader(ship.ReadInt32, ship.ReadByte, (_, _) => true));
        Equal(true, shipPlanner.TryBuildRoute(shipPosition, inside, out var insideRoute),
            $"the cargo-ship prerequisite must remain reachable with the guard's native lock: {shipPlanner.LastDiagnostic}");
        Equal(false, insideRoute.TrianglePath.Contains(102),
            "the cargo-ship prerequisite must not assume the guard's closed triangle is open");
        ship.WriteBankByte(3, 184, 0xC5);
        var deck = shipReader.ReadTargets(shipPosition).Single();
        Equal(false, shipPlanner.TryBuildRoute(shipPosition, deck, out _),
            "a saved flag alone must not override a native boundary before field re-entry");
        ship.SetBoundaryTriangles([]); // Re-entry executes the guard's Init with bit 7 set.
        Equal(true, shipPlanner.TryBuildRoute(shipPosition, deck, out _),
            $"returning after Aeris's unlock must make the actual foredeck LINE reachable: {shipPlanner.LastDiagnostic}");
    }

    private static void WelcomeShortcutRoutesThroughTheNativeAlley(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("The welcome shortcut regression requires installed field data.");
        var nativeField = new FieldScriptNavigationCatalog(dataRoot).ReadField(360);
        Equal(true, nativeField.IsUsable, $"the installed Junon script must decode: {nativeField.Diagnostic}");
        var nativeExit = nativeField.Exits.Single(exit => exit.StableId == "script-exit:360:10:363");
        Equal(new FieldNavigationTriggerLine(634, 347, 0, 492, 361, 0),
            nativeExit.TriggerLine!.Value, "installed border3 must lead into the live-parade field");

        FieldPositionSnapshot[] starts =
        [
            new(1, 360, 3, 4415, -783, 0, 137, 0), // cl_hei script 5; live 22:29:19Z.
            new(1, 360, 3, 520, -40, 0, 220, 0)    // live 22:30:13Z after the wrong Story route.
        ];
        foreach (var start in starts)
        {
            var memory = new NativeMemory();
            memory.SetField(360);
            memory.SetGameMoment(406);
            memory.WriteBankByte(3, 235, 0x86);
            memory.WriteBankByte(3, 236, 0x10);
            memory.SetBoundaryTriangles([]); // direct's late-squad branch unlocks triangle 220.
            var target = CreateStoryReader(memory).ReadTargets(start).Single();
            var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(360),
                new FieldBoundaryStateReader(memory.ReadInt32, memory.ReadByte, (_, _) => true));
            Equal(true, planner.TryBuildRoute(start, target, out var route),
                $"the welcome shortcut must be reachable from native triangle {start.TriangleId}: {planner.LastDiagnostic}");
            Equal(true, route.TrianglePath.Contains(220),
                "the welcome Story route must enter the unlocked alley instead of bypassing it down the road");
            Equal(true, route.FinalApproach.X >= 492 && route.FinalApproach.X <= 634 &&
                    route.FinalApproach.Y >= 347 && route.FinalApproach.Y <= 361,
                "the welcome route must finish on border3's native alley LINE");
        }
    }

    private static void ShipPrerequisiteModelsAndGatewaysAreNativelyReachable(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var scripts = new FieldScriptNavigationCatalog(
            Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
                ?? throw new InvalidOperationException("Native ship route tests need the installed data root."));
        var ship = new NativeMemory();
        ship.SetField(436);
        ship.SetGameMoment(409);
        ship.WriteBankByte(3, 184, 0x01);
        ship.SetBoundaryTriangles([102]);
        ship.ConfigureVisibleModel(11, 515, 338, -24303);
        ship.ConfigureVisibleModel(14, -181, -897, -24308);
        // shpin_2 gateway 0 enters ship_1 at (-378,1059), native triangle 64.
        var entry = new FieldPositionSnapshot(1, 436, 0, -378, 1059, -24604, 64, 0);
        var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(436),
            new FieldBoundaryStateReader(ship.ReadInt32, ship.ReadByte, (_, _) => true),
            transitionProvider: field => scripts.ReadField(field).Transitions);
        var targets = CreateStoryReader(ship).ReadTargets(entry);
        Equal(2, targets.Count, "both unspoken deck conversations are native targets");
        foreach (var target in targets)
        {
            Equal(true, planner.TryBuildRoute(entry, target, out var route),
                $"{target.Label} must have a native route including any required ladder: {planner.LastDiagnostic}");
            Equal(false, route.TrianglePath.Contains(102),
                $"{target.Label} must not cross the guard's locked passage");
        }

        var hold = new NativeMemory();
        hold.SetField(439);
        hold.SetGameMoment(409);
        hold.WriteBankByte(3, 184, 0x45);
        hold.ConfigureVisibleModel(11, -77, 386, 0);
        // ship_1 gateway 0 enters shpin_2 at (511,-386), native triangle 15.
        var holdEntry = new FieldPositionSnapshot(1, 439, 0, 511, -386, 696, 15, 0);
        // HEI's engine-room block and optional Yuffie's occupied triangle;
        // the first-visit engine room remains locked until the alarm.
        hold.SetBoundaryTriangles([62, 70]);
        var holdPlanner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(439),
            new FieldBoundaryStateReader(hold.ReadInt32, hold.ReadByte, (_, _) => true),
            transitionProvider: field => scripts.ReadField(field).Transitions);
        var reader = CreateStoryReader(hold);
        var aeris = reader.ReadTargets(holdEntry).Single();
        Equal(true, holdPlanner.TryBuildRoute(holdEntry, aeris, out var approach),
            $"Aeris's actual unlock conversation must be reachable: {holdPlanner.LastDiagnostic}");
        Equal(false, approach.TrianglePath.Any(triangle => triangle is 62 or 70),
            "Aeris's approach must not cross either occupied native triangle");
        hold.WriteBankByte(3, 184, 0xC5);
        var returnToDeck = reader.ReadTargets(holdEntry).Single();
        // A point on native triangle 47 within Talk range, not a scripted player teleport.
        var afterTalk = holdEntry with { X = -100, Y = 375, Z = 0, TriangleId = 47 };
        Equal(true, holdPlanner.TryBuildRoute(afterTalk, returnToDeck, out _),
            $"the unlock must hand off to a reachable native deck gateway: {holdPlanner.LastDiagnostic}");
    }

    private static void CargoShipAlarmRoutesUseNativeTransitions(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var scripts = new FieldScriptNavigationCatalog(
            Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
                ?? throw new InvalidOperationException("Native ship route tests need the installed data root."));
        var cases = new[]
        {
            // Barret's model position; then literal destination XY/triangles from native transitions.
            (Position: new FieldPositionSnapshot(1, 437, 0, -67, -135, 0, 3, 0), Label: "Return to the main deck after the alarm"),
            (Position: new FieldPositionSnapshot(1, 436, 0, 319, -1395, -24604, 102, 0), Label: "Go below deck after the alarm"),
            (Position: new FieldPositionSnapshot(1, 439, 0, 511, -386, 696, 15, 0), Label: "Enter the engine room"),
            (Position: new FieldPositionSnapshot(1, 440, 0, 2, -734, 117, 9, 0), Label: "Investigate the engine room"),
            // shpin_3 CLOUD Script 14 repositions Cloud here after the boss.
            (Position: new FieldPositionSnapshot(1, 440, 0, -2, -274, 1, 36, 0), Label: "Leave the engine room for docking")
        };
        foreach (var (position, label) in cases)
        {
            var memory = new NativeMemory();
            memory.SetField(position.FieldId);
            memory.SetGameMoment(415);
            memory.SetBoundaryTriangles(position.FieldId == 439 ? [62] : []);
            if (position.FieldId == 440)
            {
                memory.SetLineEnabled(15, position.TriangleId == 9);
                memory.SetLineEnabled(18, position.TriangleId == 36);
            }
            var target = CreateStoryReader(memory).ReadTargets(position).Single();
            Equal(label, target.Label, "the ship route fixture must exercise its specific native handoff");
            var planner = new FieldWalkmeshRoutePlanner(createWalkmeshReader(position.FieldId),
                new FieldBoundaryStateReader(memory.ReadInt32, memory.ReadByte, (_, _) => true),
                transitionProvider: field => scripts.ReadField(field).Transitions);
            Equal(true, planner.TryBuildRoute(position, target, out var route),
                $"{label} must route on the real ship walkmesh: {planner.LastDiagnostic}");
            if (position.FieldId == 439)
            {
                Equal(false, route.TrianglePath.Contains(62), "the engine approach must leave Yuffie's occupied triangle alone");
                memory.SetBoundaryTriangles([62, 70]);
                Equal(false, planner.TryBuildRoute(position, target, out _),
                    "the alarm route must not pretend the engine room is reachable while its native guard lock remains");
            }
        }
    }

    private static void RoutesAroundAerisToPriscillasNativeTriggerLine(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        const int fieldId = 428;
        var memory = new NativeMemory();
        memory.SetGameMoment(394);
        memory.WriteBankByte(1, 129, 0x20);
        var target = CreateStoryReader(memory)
            .ReadTargets(Position(fieldId))
            .Single(candidate => candidate.Label == "Approach Priscilla outside the house");

        // Exact state from the 2026-09-02 failure. Aeris is made visible and
        // solid at this coordinate by ujunon1 entity 10's native Init script.
        var player = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            fieldId,
            0,
            615,
            600,
            -10,
            92,
            0);
        FieldNavigationDynamicObstacle[] obstacles =
        [
            // ujunon1's native scale is 512. FUN_0060BCFA initializes each
            // model's collision width to scale * 30 / 512, so two default-width
            // models meet at the same 30-unit combined clearance.
            new(ModelIndex: 10, X: -375, Y: 646, Z: 0, ClearanceRadius: 30d)
        ];
        var walkmeshReader = createWalkmeshReader(fieldId);
        var planner = new FieldWalkmeshRoutePlanner(
            walkmeshReader,
            dynamicObstacleProvider: (_, _) => obstacles);

        Equal(
            true,
            planner.TryBuildRoute(player, target, out var plan),
            $"Priscilla's native trigger line must remain reachable around Aeris: {planner.LastDiagnostic}");

        var steps = plan.StableWaypointsOverride ??
            FieldWalkmeshPathfinder.BuildStableWaypoints(
                player.X,
                player.Y,
                player.Z,
                plan.Portals,
                plan.FinalApproach);
        Equal(true, steps.Count > 1, "the Junon route must retain its native path after the detour");
        var walkmesh = walkmeshReader.Read(player).Walkmesh
            ?? throw new InvalidOperationException("field 428 walkmesh must be readable");
        var outboundTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
            walkmesh,
            92,
            new FieldNavigationRouteWaypoint(player.X, player.Y, player.Z),
            steps[0].Waypoint);
        Equal(true, outboundTrace.IsClear, "the Junon recovery waypoint must be natively walkable");
        var rejoinTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
            walkmesh,
            outboundTrace.EndTriangle,
            steps[0].Waypoint,
            steps[1].Waypoint);
        Equal(true, rejoinTrace.IsClear, "the Junon recovery waypoint must rejoin the next spoken route step");
        Equal(
            false,
            FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                new FieldNavigationRouteWaypoint(player.X, player.Y, player.Z),
                steps[0].Waypoint,
                obstacles),
            "the first spoken route segment must not send Cloud through Aeris");
        Equal(
            false,
            FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                steps[0].Waypoint,
                steps[1].Waypoint,
                obstacles),
            "the recovery segment must rejoin the native route without cutting back through Aeris");
        Equal(
            true,
            Math.Abs(plan.FinalApproach.X - (-720)) <= 8 &&
            plan.FinalApproach.Y >= 996 &&
            plan.FinalApproach.Y <= 1086,
            "the live-model detour must preserve Priscilla's native LINE trigger as the destination");

        var tracker = new FieldNavigationRouteTracker(planner);
        Equal(true, tracker.TryStart(player, target, out var started), "spoken Junon story navigation must start");
        Equal(
            steps[0].Waypoint,
            started.Waypoint,
            "the opening spoken direction must use the collision-clear bypass");
        Equal(
            true,
            tracker.TryUpdate(player, target, out var retained),
            "Junon story navigation must remain active on the next runtime sample");
        Equal(
            steps[0].Waypoint,
            retained.Waypoint,
            $"the collision-clear bypass must not disappear before Cloud starts moving ({retained.Diagnostic})");
    }

    private static void FirstVisitJunonFollowsNativeStateSequence()
    {
        AssertStoryTarget(428, 385, "Go to the beach", 165, -674, -155);
        AssertStoryTarget(428, 388, "Enter the house and rest", 599, 152, 0);
        AssertStoryTarget(
            428,
            394,
            "Approach Priscilla outside the house",
            -718,
            1041,
            179,
            memory => memory.WriteBankByte(1, 129, 0x20));
        AssertStoryTarget(
            428,
            394,
            "Follow Priscilla back to the beach",
            165,
            -674,
            -155,
            memory => memory.WriteBankByte(1, 129, 0x60));

        AssertStoryTarget(
            429,
            394,
            "Talk to Priscilla at the beach",
            120,
            240,
            0,
            memory => memory.ConfigureVisibleModel(12, 120, 240, 0));
        AssertStoryTarget(
            429,
            400,
            "Talk to Priscilla to use the dolphin whistle",
            120,
            240,
            0,
            memory => memory.ConfigureVisibleModel(12, 120, 240, 0));

        AssertStoryTarget(
            430,
            400,
            "Stand at the dolphin launch point and press OK",
            -167,
            -82,
            -141);
        AssertStoryTarget(
            430,
            400,
            "Climb the pole to Upper Junon",
            -363,
            -373,
            985,
            memory => memory.WriteBankByte(5, 16, 1));
        AssertStoryTarget(385, 400, "Leave the airport for Upper Junon", -4318, -1242, 3711);
        AssertStoryTarget(384, 403, "Continue into the Junon barracks", 12496, 14558, 5139,
            memory => memory.WriteBankByte(1, 226, 0x40));
        AssertStoryTarget(386, 403, "Enter the locker room", -1537, -654, 767);
        AssertStoryTarget(
            387,
            403,
            "Open the locker and change into a Shinra uniform",
            -1580,
            -826,
            605);
        AssertStoryTarget(
            387,
            406,
            "Talk to the captain and say you are ready for the parade",
            180,
            360,
            0,
            memory =>
            {
                memory.WriteBankByte(5, 17, 1);
                memory.ConfigureVisibleModel(18, 180, 360, 0);
                // 387:12 Init leaves the parade line off until the player says they are
                // ready, so the captain is the only thing to walk to here.
                memory.SetLineEnabled(12, false);
            });
        AssertStoryTarget(
            386,
            406,
            "Continue to the Upper Junon ceremony",
            -828,
            -769,
            767,
            memory => memory.WriteBankByte(3, 235, 0x04));
        AssertStoryTarget(
            360,
            406,
            "Follow the soldiers to the welcoming ceremony",
            563,
            354,
            0,
            memory =>
            {
                memory.WriteBankByte(3, 235, 0x06);
                memory.WriteBankByte(3, 236, 0x10);
            });

        AssertStoryTarget(
            387,
            406,
            "Leave the locker room for the send-off",
            -1271,
            -652,
            605,
            memory =>
            {
                memory.WriteBankByte(3, 235, 0x10);
                memory.WriteBankByte(3, 236, 0x01);
            });
        AssertStoryTarget(386, 406, "Continue toward the dock", -828, -818, 767, SetSendOffReady);
        AssertStoryTarget(360, 406, "Follow the road toward the dock", -2237, -499, 0, SetSendOffReady);
        AssertStoryTarget(361, 406, "Follow the road toward the dock", 4356, -4953, -3407, SetSendOffReady);
        AssertStoryTarget(390, 406, "Continue toward the dock", -1665, 3796, 1219, SetSendOffReady);
        AssertStoryTarget(371, 406, "Continue toward the dock", 1910, -763, -6, SetSendOffReady);
        AssertStoryTarget(370, 406, "Enter the dock for the send-off", -7973, -760, 0, SetSendOffReady);
        AssertStoryTarget(382, 409, "Board the cargo ship", -235, 436, 11);
        AssertStoryTarget(436, 409, "Go to the cargo ship deck", 260, -1479, -24604,
            memory => memory.WriteBankByte(3, 184, 0x80));
    }

    private static void LaterJunonObjectiveDoesNotLeakIntoFirstVisit()
    {
        var memory = new NativeMemory();
        memory.SetGameMoment(406);
        SetSendOffReady(memory);
        var targets = CreateStoryReader(memory).ReadTargets(Position(390));

        Equal(
            false,
            targets.Any(target => target.Label == "Continue Junon Intersections"),
            "the moment-1253 Junon objective must not leak into the first visit");
        Equal(
            "Continue toward the dock",
            targets.Single().Label,
            "the first-visit Junon intersection target");
    }

    private const string DolphinLabel = "Stand at the water's edge and press Switch to call the dolphin (optional)";

    /// <summary>
    /// ujunon2's ad polls the whistle only while 469 &lt;= 2[0] &lt; 1008 (IFSW 16200000D501047C and
    /// 16200000F0030384). Before that the ride is the first visit's, Priscilla's to offer; from
    /// 1008 the poll is skipped. The installed bytes are checked in FieldTriangleExitTests.
    /// </summary>
    private static void ReturningDolphinIsOfferedOnlyWhileAdPollsTheWhistle()
    {
        foreach (var moment in new[] { 469, 700, 1007 })
        {
            var dolphin = DolphinTargets(moment);
            Equal(1, dolphin.Length, $"moment {moment}: the returning dolphin is offered");
            var target = dolphin[0];
            Equal(FieldNavigationCategory.Story, target.Category, $"moment {moment}: dolphin category");
            Equal(true, target.CompletesOnArrival, $"moment {moment}: the dolphin row is finished by getting there");
            Equal(true, target.CompletionTriangles is [25], $"moment {moment}: it is finished on triangle 25 and nowhere else");
            Equal((-553, 566, -10), (target.X, target.Y, target.Z), $"moment {moment}: it aims at triangle 25's centroid");
            Equal(-1, target.TriggerEntityId, $"moment {moment}: there is no model or LINE to walk to");
            Equal(true, target.TriggerLine is null, $"moment {moment}: or line to cross");
            Equal(FieldNavigationActivation.Default, target.Activation, $"moment {moment}: nothing is walked into or talked to");
        }

        foreach (var moment in new[] { 400, 405, 468, 1008, 1100 })
        {
            Equal(0, DolphinTargets(moment).Length, $"moment {moment}: ad does not poll the whistle");
        }
    }

    /// <summary>
    /// The dolphin comes only to triangle 25, a thin one: from its centroid the edge towards
    /// triangle 20 is 78.8 units away, inside the default 80-unit arrival distance, and on the
    /// installed walkmesh (-632,577) is on triangle 20, 79.8 units from the row's point. A route
    /// finished by distance would stop there, one step short of where [SWITCH] works. It has to
    /// go on there - at the default distance and at a wider one a player has set - and it is
    /// finished anywhere on 25, even at the far corner (-640,400), 187 units out. Nothing is
    /// pressed for the player.
    /// </summary>
    private static void TheDolphinRouteEndsOnTriangle25AtAnyArrivalDistance()
    {
        var target = DolphinTargets(469).Single();
        foreach (var arrivalDistance in new[] { 80, 256 })
        {
            foreach (var (x, y, z, triangle, where) in new[]
                     {
                         (-632, 577, -2, (ushort)20, "on triangle 20 beside the edge"),
                         (-598, 759, -2, (ushort)26, "at triangle 26's centroid, 198 units away")
                     })
            {
                var outside = TrackDolphin(target, new FieldPositionSnapshot(FieldPositionReader.FieldModule, 429, 0, x, y, z, triangle, 0), arrivalDistance);
                Equal(true, outside.BeaconStillOn, $"distance {arrivalDistance}, {where}: the route goes on ('{outside.Speech}')");
                Equal(false, outside.Speech.Contains("reached", StringComparison.Ordinal),
                    $"distance {arrivalDistance}, {where}: nothing says the whistle spot is reached");
                Equal(true, outside.AutoWalkMoves, $"distance {arrivalDistance}, {where}: auto walk keeps going onto 25");
            }

            var onIt = TrackDolphin(target, new FieldPositionSnapshot(FieldPositionReader.FieldModule, 429, 0, -640, 400, -10, 25, 0), arrivalDistance);
            Equal($"{DolphinLabel} reached. Navigation off.", onIt.Speech, $"distance {arrivalDistance}: anywhere on 25 is the whistle spot");
            Equal(false, onIt.BeaconStillOn, $"distance {arrivalDistance}: and the route is over");
            Equal(false, onIt.AutoWalkMoves, $"distance {arrivalDistance}: auto walk stops there");
        }
    }

    private static FieldNavigationTarget[] DolphinTargets(int gameMoment)
    {
        var memory = new NativeMemory();
        memory.SetField(429);
        memory.SetGameMoment(gameMoment);
        return CreateStoryReader(memory).ReadTargets(Position(429)).Where(target => target.Label == DolphinLabel).ToArray();
    }

    private static (string Speech, bool BeaconStillOn, bool AutoWalkMoves) TrackDolphin(
        FieldNavigationTarget target,
        FieldPositionSnapshot position,
        int arrivalDistance)
    {
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource(
                Array.Empty<FieldNavigationTarget>(),
                storyTargetProvider: _ => [target]),
            new StraightRoutePlanner());
        _ = controller.HandleAction(FieldNavigationAction.NextCategory, position);
        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, new FieldNavigationControlTransform(0));
        Equal(true, controller.BeaconEnabled, "the dolphin route is active");
        var update = controller.UpdateLiveTracking(
            position,
            new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
            new FieldNavigationControlTransform(0),
            isSuppressed: false,
            arrivalDistanceUnits: arrivalDistance);
        var moves = controller.TryResolveAutomaticInput(position, new FieldNavigationControlTransform(0), arrivalDistance, out var input) &&
                    input != FieldNavigationInput.None;
        return (update?.Speech ?? "", controller.BeaconEnabled, moves);
    }

    private sealed class StraightRoutePlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight dolphin test route";

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

    private static void SoldierCollectiblesPublishUntilTheirNativeBitsAreSet()
    {
        AssertSoldierCollectible(
            fieldId: 368,
            entityId: 16,
            expectedModelResource: "junmin2shinra_guard.char",
            expectedMask: 0x10,
            x: -62,
            y: -49,
            z: 0);
        AssertSoldierCollectible(
            fieldId: 381,
            entityId: 9,
            expectedModelResource: "junmin5shinra_guard.char",
            expectedMask: 0x40,
            x: -11,
            y: -38,
            z: 40);
    }

    private static void AssertSoldierCollectible(
        int fieldId,
        int entityId,
        string expectedModelResource,
        byte expectedMask,
        int x,
        int y,
        int z)
    {
        var definitions = FieldNavigationObjectCatalog.CreateAllFields()
            .Where(definition =>
                definition.FieldId == fieldId &&
                definition.EntityId == entityId &&
                definition.NativeId == 95)
            .ToArray();
        Equal(1, definitions.Length, $"field {fieldId} 1/35 soldier definition count");
        var definition = definitions.Single();
        Equal(FieldNavigationObjectKind.Item, definition.Kind, $"field {fieldId} soldier kind");
        Equal(FieldNavigationObjectTargetKind.Model, definition.TargetKind, $"field {fieldId} soldier target kind");
        Equal(expectedModelResource, definition.SourceModelResource, $"field {fieldId} soldier model");
        Equal(15, definition.CollectedBank, $"field {fieldId} soldier collection bank");
        Equal(118, definition.CollectedAddress, $"field {fieldId} soldier collection address");
        Equal(expectedMask, definition.CollectedMask, $"field {fieldId} soldier collection mask");

        var memory = new NativeMemory();
        memory.ConfigureVisibleModel(entityId, x, y, z);
        var reader = new FieldNavigationObjectReader(
            memory.ReadInt32,
            memory.ReadByte,
            nativeId => nativeId == 95 ? "1/35 soldier" : null,
            _ => null,
            [definition]);
        var target = reader.ReadTargets(Position(fieldId)).Single();
        Equal("1/35 soldier", target.Label, $"field {fieldId} soldier label");
        Equal(x, target.X, $"field {fieldId} soldier x");
        Equal(y, target.Y, $"field {fieldId} soldier y");
        Equal(z, target.Z, $"field {fieldId} soldier z");

        memory.WriteBankByte(15, 118, expectedMask);
        Equal(
            0,
            reader.ReadTargets(Position(fieldId)).Count,
            $"field {fieldId} collected soldier must disappear");
    }

    private static void StoryCatalogContainsNoStaleWhiteBackgroundDuplicate()
    {
        var records = FieldStoryEventCatalog.CreateAllFields()
            .Where(definition =>
                definition.FieldId == 115 &&
                definition.EntityId == 4 &&
                definition.TargetGameMoment == 1180)
            .ToArray();
        Equal(1, records.Length, "whitebg3 moment-1180 objective identity count");
    }

    private static void AssertStoryTarget(
        int fieldId,
        int gameMoment,
        string expectedLabel,
        int expectedX,
        int expectedY,
        int expectedZ,
        Action<NativeMemory>? configure = null)
    {
        var memory = new NativeMemory();
        // The checked line-state reader refuses to answer unless the module and field say
        // where it is being asked about, which the game always does.
        memory.SetField(fieldId);
        memory.SetGameMoment(gameMoment);

        configure?.Invoke(memory);
        var targets = CreateStoryReader(memory).ReadTargets(Position(fieldId));
        // The first offered target is the objective; a field may also carry a native line
        // that is legitimately on at the same time, and which one leads is what matters.
        var target = targets.FirstOrDefault(candidate => candidate.Label == expectedLabel);
        Equal(
            expectedLabel,
            targets.Count == 0 ? "(nothing offered)" : targets[0].Label,
            $"field {fieldId} moment {gameMoment} story label");
        Equal(expectedX, target.X, $"{expectedLabel} x");
        Equal(expectedY, target.Y, $"{expectedLabel} y");
        Equal(expectedZ, target.Z, $"{expectedLabel} z");
    }

    private static FieldStoryTargetReader CreateStoryReader(NativeMemory memory)
    {
        var lines = new FieldScriptLineStateReader(memory);
        return new(
            memory.ReadInt32,
            memory.ReadInt16,
            memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields(),
            entity => memory.HasLineState(entity) ? lines.IsEnabled(entity) : true);
    }

    private static FieldPositionSnapshot Position(int fieldId) =>
        new(FieldPositionReader.FieldModule, fieldId, 0, 0, 0, 0, 0, 0);

    private static void SetSendOffReady(NativeMemory memory)
    {
        memory.WriteBankByte(3, 235, 0x10);
        memory.WriteBankByte(3, 236, 0x01);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }

    private sealed class NativeMemory : ILegacyAddressSpace
    {
        private const int FieldState = 0x02700000;
        private readonly Dictionary<int, byte> bytes = [];
        private readonly Dictionary<int, byte> lineIndices = [];
        private byte nextModelId = 1;

        public int? UnreadableAddress { get; set; }
        public Action<int>? BeforeRead { get; set; }

        public NativeMemory()
        {
            WriteInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
            WriteByte(FieldPositionReader.AddressFieldNumModels, 16);
        }

        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);

        public bool TryRead(uint address, Span<byte> destination)
        {
            BeforeRead?.Invoke(unchecked((int)address));
            if (UnreadableAddress is { } missing && address <= (uint)missing &&
                (ulong)address + (uint)destination.Length > (uint)missing)
            {
                return false;
            }
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = ReadByte(unchecked((int)address) + index);
            }
            return true;
        }

        public short ReadInt16(int address) => unchecked((short)(
            ReadByte(address) |
            (ReadByte(address + 1) << 8)));

        public int ReadInt32(int address) =>
            ReadByte(address) |
            (ReadByte(address + 1) << 8) |
            (ReadByte(address + 2) << 16) |
            (ReadByte(address + 3) << 24);

        public void SetGameMoment(int gameMoment)
        {
            WriteByte(FieldNavigationObjectReader.AddressFieldBankBase, (byte)gameMoment);
            WriteByte(FieldNavigationObjectReader.AddressFieldBankBase + 1, (byte)(gameMoment >> 8));
        }

        public void WriteBankByte(int bank, int index, byte value) =>
            WriteByte(ResolveBankAddress(bank, index), value);

        public void SetField(int fieldId)
        {
            WriteByte(FieldPositionReader.AddressCurrentModule, 1);
            WriteByte(FieldPositionReader.AddressFieldId, (byte)fieldId);
            WriteByte(FieldPositionReader.AddressFieldId + 1, (byte)(fieldId >> 8));
            WriteInt32(FieldBoundaryStateReader.AddressFieldGlobalObjectPtr, FieldState);
        }

        public void SetBoundaryTriangles(IEnumerable<int> triangles)
        {
            for (var index = 0; index < FieldBoundaryStateReader.BoundaryByteCount; index++)
            {
                WriteByte(FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + index, 0);
            }
            foreach (var triangle in triangles)
            {
                var address = FieldState + FieldBoundaryStateReader.BoundaryBitsOffset + (triangle >> 3);
                WriteByte(address, (byte)(ReadByte(address) | (1 << (triangle & 7))));
            }
        }

        public void ConfigureVisibleModel(int entityId, int x, int y, int z)
        {
            var modelId = nextModelId++;
            WriteByte(FieldNavigationObjectReader.AddressFieldModelIdArray + entityId, modelId);
            var eventAddress = EventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
            WriteByte(eventAddress + FieldNavigationObjectReader.VisibilityOffset, 1);
            WriteInt32(
                eventAddress + FieldNavigationObjectReader.PositionXOffset,
                x * FieldNavigationObjectReader.ModelPositionFixedPointScale);
            WriteInt32(
                eventAddress + FieldNavigationObjectReader.PositionYOffset,
                y * FieldNavigationObjectReader.ModelPositionFixedPointScale);
            WriteInt32(
                eventAddress + FieldNavigationObjectReader.PositionZOffset,
                z * FieldNavigationObjectReader.ModelPositionFixedPointScale);
        }

        public void SetModelVisible(int entityId, bool visible)
        {
            var modelId = ReadByte(FieldNavigationObjectReader.AddressFieldModelIdArray + entityId);
            WriteByte(EventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride +
                FieldNavigationObjectReader.VisibilityOffset, visible ? (byte)1 : (byte)0);
        }

        /// <summary>Whether this fixture has said anything about that entity's line.</summary>
        public bool HasLineState(int entityId) => lineIndices.ContainsKey(entityId);

        public void SetLineEnabled(int entityId, bool enabled)
        {
            if (!lineIndices.TryGetValue(entityId, out var lineIndex))
            {
                lineIndex = (byte)(lineIndices.Count + 2); // Fixture slots, deliberately not entity IDs.
                lineIndices.Add(entityId, lineIndex);
            }
            WriteByte(FieldScriptLineStateReader.AddressFieldLineIndexByEntity + entityId, lineIndex);
            WriteByte(FieldScriptLineStateReader.AddressFieldLineStates +
                lineIndex * FieldScriptLineStateReader.LineStateStride, enabled ? (byte)1 : (byte)0);
        }

        public void SetInteractionRadii(int entityId, short playerRadius, short talkRadius)
        {
            WriteByte(EventTable + 0x72, (byte)playerRadius);
            WriteByte(EventTable + 0x73, (byte)(playerRadius >> 8));
            var modelId = ReadByte(FieldNavigationObjectReader.AddressFieldModelIdArray + entityId);
            var address = EventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride + 0x74;
            WriteByte(address, (byte)talkRadius);
            WriteByte(address + 1, (byte)(talkRadius >> 8));
        }

        public void SetPlayerPosition(int x, int y, int z, ushort triangle, int renderOffsetZ)
        {
            const int modelTable = 0x02800000;
            WriteInt32(FieldPositionReader.AddressFieldModelsPtr, modelTable);
            WriteInt32(modelTable + FieldPositionReader.ModelXOffset, x);
            WriteInt32(modelTable + FieldPositionReader.ModelYOffset, y);
            // FUN_006392BB adds OFST and the renderer's ten-unit origin shift.
            WriteInt32(modelTable + FieldPositionReader.ModelZOffset, z + renderOffsetZ - 10);
            WriteInt32(EventTable + FieldNavigationObjectReader.PositionXOffset, x * 4096);
            WriteInt32(EventTable + FieldNavigationObjectReader.PositionYOffset, y * 4096);
            WriteInt32(EventTable + FieldNavigationObjectReader.PositionZOffset, z * 4096);
            WriteInt32(EventTable + 0x50, renderOffsetZ);
            WriteByte(EventTable + FieldPositionReader.ObjectTriangleOffset, (byte)triangle);
            WriteByte(EventTable + FieldPositionReader.ObjectTriangleOffset + 1, (byte)(triangle >> 8));
        }

        private static int ResolveBankAddress(int bank, int index) => bank switch
        {
            1 => FieldNavigationObjectReader.AddressFieldBankBase + index,
            3 => FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + index,
            5 => FieldNavigationObjectReader.AddressTemporaryFieldBankBase + index,
            11 => FieldNavigationObjectReader.AddressFieldBankBase + 0x200 + index,
            13 => FieldNavigationObjectReader.AddressFieldBankBase + 0x300 + index,
            15 => FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + index,
            _ => throw new ArgumentOutOfRangeException(nameof(bank))
        };

        private void WriteByte(int address, byte value) => bytes[address] = value;

        public void WriteInt32(int address, int value)
        {
            WriteByte(address, (byte)value);
            WriteByte(address + 1, (byte)(value >> 8));
            WriteByte(address + 2, (byte)(value >> 16));
            WriteByte(address + 3, (byte)(value >> 24));
        }
    }
}
