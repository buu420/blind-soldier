using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

AssertTypedReadsAreExplicitLittleEndian();
AssertTypedReadFailuresRemainFailures();
AssertContractExposesGuestAddressesWithoutHostPointers();
AssertPureFf7ParsersLiveInSharedAssembly();
AssertSharedTextAndLzsDecodersPreserveLegacySemantics();
AssertSharedMapListParserPreservesNativeOrder();
AssertFf7PcSaveFileReaderValidatesNativeSlotChecksum();
AssertSharedObservationContractsLiveInSharedAssembly();
AssertInventoryItemReaderChecksGuestSlot();
AssertInventoryItemReaderRejectsUnstableAndUnreadableGuestSlots();
AssertInventoryItemReaderRejectsInvalidGuestSlotAddressesAndValues();
AssertMenuLayoutReadersLiveInSharedAssembly();
AssertMainMenuStateReaderChecksEveryFieldAndBookends();
AssertActiveMenuWidgetReaderChecksEveryFieldAndBookend();
AssertConfigMenuValueReaderChecksEveryFieldAndBookend();
AssertMagicMenuSelectionReaderChecksEveryFieldAndBookend();
AssertSavemapPartyReaderChecksEveryFieldAndBookend();
AssertBattleLayoutReadersLiveInSharedAssembly();
AssertBattleStateReaderChecksCompleteCoherentSnapshots();
AssertBattleStateReaderSupportsNativeGuestPartyRecords();
AssertBattleStateReaderBoundsIndicesAndMarksEnemyDetailsPrivate();
AssertBattleActorSnapshotEnforcesPrivacyInvariant();
AssertBattlePrivateCorrelationContractIsNarrowAndNonPublic();
AssertBattleStateReaderChecksEncounterActionAndActorCollections();
AssertBattleStateReaderRejectsStableInvalidActiveActorCollections();
AssertBattleResultsReaderChecksRewardsAndBookends();
AssertBattleDamagePopupReaderChecksEffectsAndBookends();
AssertFieldFoundationReadersLiveInSharedAssembly();
AssertFieldPositionReaderChecksDirectAndNestedReads();
AssertFieldAudibleCueReaderChecksStateAndBookends();
AssertFieldAudibleCueOwnershipReaderReleasesClosedStaleMessageCount();
AssertFieldBoundaryReaderChecksNestedPointerAndBookends();
AssertFieldNavigationControlReaderChecksNestedPointerAndBookends();
FieldGatewayTargetReaderTests.Run();
FieldOpcodeDialogueOwnershipTests.Run();
FieldDialogueScriptLayoutTests.Run();
FieldLineBufferSnapshotTests.Run();
FieldCountdownReaderTests.Run();
NameEntryStateReaderTests.Run();
HighwayStateReaderTests.Run();
HighwayRoadStateReaderTests.Run();
WallMarketSquatCueTests.Run();
GameLanguageDetectorTests.Run();
LocalizedTextDecoderTests.Run();
LocalizedKernel2Tests.Run();
BlindSoldierLocalizerTests.Run();
Console.WriteLine("FFVII shared layout tests passed.");

static void AssertBattleLayoutReadersLiveInSharedAssembly()
{
    var expected = typeof(ILegacyAddressSpace).Assembly;
    Type[] battleTypes =
    [
        typeof(BattleStateReader),
        typeof(BattleActorSnapshot),
        typeof(BattleEncounterSnapshot),
        typeof(BattleEnemyActionSnapshot),
        typeof(BattleMenuStateSnapshot),
        typeof(BattleMenuSelectionSnapshot),
        typeof(BattleTargetSnapshot),
        typeof(BattlePartyProgressSnapshot),
        typeof(BattleResultsReader),
        typeof(BattleRewardItemSnapshot),
        typeof(BattleResultsSnapshot),
        typeof(BattleDamagePopupReader),
        typeof(BattleDamagePopupSnapshot)
    ];

    foreach (var battleType in battleTypes)
    {
        AssertEqual(expected, battleType.Assembly, $"shared battle layout type {battleType.Name}");
        AssertEqual(
            false,
            battleType.GetProperties().Any(property =>
                property.PropertyType == typeof(IntPtr) ||
                property.PropertyType == typeof(UIntPtr)),
            $"pointer-free battle layout type {battleType.Name}");
    }
}

static void AssertBattleStateReaderChecksCompleteCoherentSnapshots()
{
    var memory = CreateValidBattleStateMemory();
    var reader = CreateCheckedBattleStateReader(memory);
    var menu = reader.ReadMenuState(1);
    AssertEqual(true, menu.IsValid, "checked battle menu snapshot");
    AssertEqual("Cloud", menu.Actor.Name, "checked battle actor name");
    AssertEqual(314, menu.Actor.CurrentHp, "checked battle actor HP");

    var actorBase = (uint)BattleStateReader.AddressBattleActors;
    var characterName = (uint)SavemapPartyReader.AddressSavemap +
        SavemapPartyReader.CharactersOffset +
        SavemapPartyReader.CharacterNameOffset;
    var requiredBytes = new (uint Address, string Label)[]
    {
        ((uint)BattleStateReader.AddressCurrentModule, "module"),
        ((uint)BattleStateReader.AddressCurrentActorSlot, "actor owner"),
        ((uint)BattleStateReader.AddressMenuWindowStates + 1u, "window owner"),
        ((uint)SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset, "party slot"),
        (characterName, "party name"),
        (actorBase + BattleStateReader.ActorInstanceIdOffset, "active actor identity"),
        (actorBase + BattleStateReader.ActorStatusMaskOffset, "status"),
        (actorBase + BattleStateReader.ActorCurrentMpOffset, "current MP"),
        (actorBase + BattleStateReader.ActorMaxMpOffset, "maximum MP"),
        (actorBase + BattleStateReader.ActorCurrentHpOffset, "current HP"),
        (actorBase + BattleStateReader.ActorMaxHpOffset, "maximum HP")
    };
    foreach (var required in requiredBytes)
    {
        var unmapped = CreateValidBattleStateMemory();
        unmapped.Remove(required.Address);
        AssertEqual(
            false,
            CreateCheckedBattleStateReader(unmapped).ReadMenuState(1).IsValid,
            $"unmapped battle {required.Label} invalidates the complete snapshot");
    }

    var tornModule = new TearingLegacyAddressSpace(
        memory,
        (uint)BattleStateReader.AddressCurrentModule,
        [1]);
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(tornModule).ReadMenuState(1).IsValid,
        "torn battle module invalidates the snapshot");

    var tornOwner = new TearingLegacyAddressSpace(
        memory,
        (uint)BattleStateReader.AddressCurrentActorSlot,
        [1]);
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(tornOwner).ReadMenuState(1).IsValid,
        "torn battle menu ownership invalidates the snapshot");

    var replacement = CreateValidBattleStateMemory(currentHp: 313);
    var remappedActor = new RemappingLegacyAddressSpace(
        memory,
        replacement,
        (uint)BattleStateReader.AddressBattleActors + BattleStateReader.ActorCurrentHpOffset,
        sizeof(int));
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(remappedActor).ReadMenuState(1).IsValid,
        "remapped battle actor record invalidates the snapshot");
}

static void AssertBattleStateReaderSupportsNativeGuestPartyRecords()
{
    const byte guestCharacterId = 10;
    const int guestRecordIndex = 4;
    var memory = CreateValidBattleEncounterMemory();
    var partySlotAddress = (uint)SavemapPartyReader.AddressSavemap +
        SavemapPartyReader.PartyMembersOffset;
    var actorBase = (uint)BattleStateReader.AddressBattleActors;
    memory.Write(partySlotAddress, [guestCharacterId]);
    memory.Write(actorBase + BattleStateReader.ActorInstanceIdOffset, [guestCharacterId]);

    for (var recordIndex = 0; recordIndex < 9; recordIndex++)
    {
        memory.Write(
            (uint)SavemapPartyReader.AddressSavemap +
                SavemapPartyReader.CharactersOffset +
                (uint)recordIndex * SavemapPartyReader.CharacterSize,
            [checked((byte)recordIndex)]);
    }

    var guestRecordAddress = (uint)SavemapPartyReader.AddressSavemap +
        SavemapPartyReader.CharactersOffset +
        (uint)guestRecordIndex * SavemapPartyReader.CharacterSize;
    memory.Write(guestRecordAddress, [guestCharacterId]);
    WriteFf7Text(
        memory,
        guestRecordAddress + SavemapPartyReader.CharacterNameOffset,
        "Sephiroth",
        12);
    memory.Write(
        guestRecordAddress + SavemapPartyReader.LevelOffset,
        [50]);
    memory.Write((uint)BattleStateReader.AddressRootCommandColumnCount, [1]);
    WriteInt32(memory, (uint)BattleStateReader.AddressRootCommandColumn, 0);
    WriteInt32(memory, (uint)BattleStateReader.AddressRootCommandRow, 0);
    memory.Write((uint)BattleStateReader.AddressRootCommandRecords, [1]);

    var reader = new BattleStateReader(
        memory,
        new SavemapPartyReader(memory),
        resolveCommandName: commandId => commandId == 1 ? "Attack" : null);
    var menu = reader.ReadMenuState(1);

    AssertEqual(true, menu.IsValid, "native guest battle menu snapshot");
    AssertEqual("Sephiroth", menu.Actor.Name, "native guest battle actor name");
    AssertEqual("Attack", menu.Selection?.Name, "native guest root command");
    AssertEqual(
        true,
        reader.TryReadBattleActors(out var actors),
        "native guest battle actor collection");
    AssertEqual(
        true,
        actors.Any(actor => !actor.IsEnemy && actor.Name == "Sephiroth"),
        "native guest actor remains correlated with the live party slot");
    AssertEqual(
        true,
        reader.TryReadPartyProgress(out var progress),
        "native guest party progress");
    AssertEqual("Sephiroth", progress.Single().Name, "native guest progress name");
    AssertEqual(50, progress.Single().Level, "native guest progress level");
    AssertEqual(true, reader.ReadEncounter().IsValid, "native guest battle encounter");
}

static void AssertBattleStateReaderBoundsIndicesAndMarksEnemyDetailsPrivate()
{
    var memory = CreateValidBattleStateMemory();
    WriteInt32(memory, (uint)BattleStateReader.AddressBattleMenuTextState, 0);
    memory.Write((uint)BattleStateReader.AddressTargetMode, [6]);
    memory.Write((uint)BattleStateReader.AddressTargetFlags, [0]);
    WriteUInt16(memory, (uint)BattleStateReader.AddressTargetMask, 1 << 4);
    memory.Write((uint)BattleStateReader.AddressSelectedTarget, [4]);
    memory.Write((uint)BattleStateReader.AddressEnemySceneIndexRecords, [0]);
    WriteFf7Text(memory, (uint)BattleStateReader.AddressEnemyData, "Grunt", BattleStateReader.EnemyNameLength);
    var enemyBase = (uint)BattleStateReader.AddressBattleActors + 4u * BattleStateReader.BattleActorSize;
    memory.Write(enemyBase + BattleStateReader.ActorInstanceIdOffset, [0]);
    WriteUInt32(memory, enemyBase + BattleStateReader.ActorStatusMaskOffset, 1u << 3);
    WriteUInt16(memory, enemyBase + BattleStateReader.ActorCurrentMpOffset, 12);
    WriteUInt16(memory, enemyBase + BattleStateReader.ActorMaxMpOffset, 18);
    WriteInt32(memory, enemyBase + BattleStateReader.ActorCurrentHpOffset, 42);
    WriteInt32(memory, enemyBase + BattleStateReader.ActorMaxHpOffset, 50);

    var target = CreateCheckedBattleStateReader(memory).ReadTarget();
    AssertEqual(true, target.IsValid, "bounded enemy target snapshot");
    AssertEqual("Grunt", target.Actor.Name, "unsensed enemy public name");
    AssertEqual(false, target.Actor.InformationVisible, "unsensed enemy visibility");
    AssertEqual(0, target.Actor.CurrentHp, "unsensed enemy current HP is absent from the public target snapshot");
    AssertEqual(0, target.Actor.MaxHp, "unsensed enemy maximum HP is absent from the public target snapshot");
    AssertEqual(0, target.Actor.CurrentMp, "unsensed enemy current MP is absent from the public target snapshot");
    AssertEqual(0, target.Actor.MaxMp, "unsensed enemy maximum MP is absent from the public target snapshot");
    AssertEqual(0u, target.Actor.StatusMask, "unsensed enemy status is absent from the public target snapshot");

    WriteUInt16(memory, (uint)BattleStateReader.AddressTargetMask, 1 << 15);
    memory.Write((uint)BattleStateReader.AddressSelectedTarget, [15]);
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(memory).ReadTarget().IsValid,
        "out-of-range target index is rejected");

    var overflowingMenu = CreateValidBattleStateMemory();
    overflowingMenu.Write((uint)BattleStateReader.AddressMenuWindowStates + 6u, [BattleStateReader.ActiveWindowState]);
    WriteInt32(overflowingMenu, (uint)BattleStateReader.AddressMagicCursorColumn, 0);
    WriteInt32(overflowingMenu, (uint)BattleStateReader.AddressMagicCursorRow, 0);
    WriteInt32(overflowingMenu, (uint)BattleStateReader.AddressMagicScrollRow, int.MaxValue);
    var overflowSnapshot = CreateCheckedBattleStateReader(
        overflowingMenu,
        abilityName: _ => "must not resolve").ReadMenuState(6);
    AssertEqual(true, overflowSnapshot.IsValid, "overflowing optional menu selection preserves its owner");
    AssertEqual(null, overflowSnapshot.Selection, "overflowing menu index cannot form a guest address");
}

static void AssertBattleActorSnapshotEnforcesPrivacyInvariant()
{
    var unsensed = new BattleActorSnapshot(4, "Grunt", true, 42, 50, 12, 18, false, 1u << 3);
    AssertEqual(0, unsensed.CurrentHp, "unsensed public actor constructor redacts current HP");
    AssertEqual(0, unsensed.MaxHp, "unsensed public actor constructor redacts maximum HP");
    AssertEqual(0, unsensed.CurrentMp, "unsensed public actor constructor redacts current MP");
    AssertEqual(0, unsensed.MaxMp, "unsensed public actor constructor redacts maximum MP");
    AssertEqual(0u, unsensed.StatusMask, "unsensed public actor constructor redacts status");

    var mutationAttempt = unsensed with
    {
        CurrentHp = 41,
        MaxHp = 50,
        CurrentMp = 11,
        MaxMp = 18,
        StatusMask = 1u << 8
    };
    AssertEqual(0, mutationAttempt.CurrentHp, "unsensed public actor with-expression cannot restore current HP");
    AssertEqual(0, mutationAttempt.MaxHp, "unsensed public actor with-expression cannot restore maximum HP");
    AssertEqual(0, mutationAttempt.CurrentMp, "unsensed public actor with-expression cannot restore current MP");
    AssertEqual(0, mutationAttempt.MaxMp, "unsensed public actor with-expression cannot restore maximum MP");
    AssertEqual(0u, mutationAttempt.StatusMask, "unsensed public actor with-expression cannot restore status");

    var sensed = new BattleActorSnapshot(4, "Grunt", true, 42, 50, 12, 18, true, 1u << 3);
    AssertEqual(42, sensed.CurrentHp, "sensed enemy current HP remains public");
    AssertEqual(50, sensed.MaxHp, "sensed enemy maximum HP remains public");
    AssertEqual(12, sensed.CurrentMp, "sensed enemy current MP remains public");
    AssertEqual(18, sensed.MaxMp, "sensed enemy maximum MP remains public");
    AssertEqual(1u << 3, sensed.StatusMask, "sensed enemy status remains public");

    var ally = new BattleActorSnapshot(0, "Cloud", false, 314, 350, 42, 54, true, 1u << 8);
    AssertEqual(314, ally.CurrentHp, "allied current HP remains public");
    AssertEqual(350, ally.MaxHp, "allied maximum HP remains public");
    AssertEqual(42, ally.CurrentMp, "allied current MP remains public");
    AssertEqual(54, ally.MaxMp, "allied maximum MP remains public");
    AssertEqual(1u << 8, ally.StatusMask, "allied status remains public");
}

static void AssertBattlePrivateCorrelationContractIsNarrowAndNonPublic()
{
    var method = typeof(BattleStateReader).GetMethod(
        "TryReadVisibleActorCorrelation",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    AssertEqual(true, method is not null, "private battle correlation reader exists");
    AssertEqual(false, method!.IsPublic, "battle correlation reader is not public");

    var correlationParameter = method.GetParameters().Single(parameter => parameter.IsOut);
    var correlationType = correlationParameter.ParameterType.GetElementType();
    AssertEqual(true, correlationType is not null, "private battle correlation output type");
    AssertEqual(false, correlationType!.IsPublic, "battle correlation output type is not public");
    AssertEqual(
        "ActorIndex,IsDefeated,IsEnemy,Name",
        string.Join(',', correlationType.GetProperties().Select(property => property.Name).Order()),
        "private battle correlation exposes only the visible defeat outcome and actor identity");

    var memory = CreateValidBattleEncounterMemory();
    var enemyBase = (uint)BattleStateReader.AddressBattleActors + 4u * BattleStateReader.BattleActorSize;
    WriteUInt32(memory, enemyBase + BattleStateReader.ActorStatusMaskOffset, 1u);
    AssertEqual(
        true,
        TryInvoke(CreateCheckedBattleStateReader(memory), out var stableCorrelation),
        "stable private battle correlation read");
    AssertEqual(
        true,
        (bool)correlationType.GetProperty("IsDefeated")!.GetValue(stableCorrelation)!,
        "stable private battle correlation exposes only the derived defeat outcome");

    var tornModule = new TearingLegacyAddressSpace(
        memory,
        (uint)BattleStateReader.AddressCurrentModule,
        [1]);
    AssertEqual(
        false,
        TryInvoke(CreateCheckedBattleStateReader(tornModule), out _),
        "private battle correlation rejects a torn battle-module owner");

    var tornDeath = new TearingLegacyAddressSpace(
        memory,
        enemyBase + BattleStateReader.ActorStatusMaskOffset,
        BitConverter.GetBytes(0u));
    AssertEqual(
        false,
        TryInvoke(CreateCheckedBattleStateReader(tornDeath), out _),
        "private battle correlation rejects a torn death outcome");

    bool TryInvoke(BattleStateReader reader, out object? correlation)
    {
        object?[] parameters = [4, null];
        var success = (bool)method.Invoke(reader, parameters)!;
        correlation = parameters[1];
        return success;
    }
}

static void AssertBattleStateReaderChecksEncounterActionAndActorCollections()
{
    var memory = CreateValidBattleEncounterMemory();
    var reader = CreateCheckedBattleStateReader(memory);
    var encounter = reader.ReadEncounter();
    AssertEqual(true, encounter.IsValid, "checked battle encounter");
    AssertEqual(12, encounter.FormationId, "checked battle formation");
    AssertEqual("Grunt", encounter.Enemies.Single().Name, "checked battle enemy name");
    AssertEqual(false, encounter.Enemies.Single().InformationVisible, "encounter enemy remains unsensed");

    AssertEqual(true, reader.TryReadBattleActors(out var actors), "checked battle actor collection");
    AssertEqual(2, actors.Count, "bounded battle actor collection count");
    var publicEnemy = actors.Single(actor => actor.IsEnemy);
    AssertEqual(0, publicEnemy.CurrentHp, "stable unsensed actor collection redacts current HP");
    AssertEqual(0, publicEnemy.MaxHp, "stable unsensed actor collection redacts maximum HP");
    AssertEqual(0, publicEnemy.CurrentMp, "stable unsensed actor collection redacts current MP");
    AssertEqual(0, publicEnemy.MaxMp, "stable unsensed actor collection redacts maximum MP");
    AssertEqual(0u, publicEnemy.StatusMask, "stable unsensed actor collection redacts status");
    AssertEqual(true, reader.TryReadPartyProgress(out var progress), "checked battle party progress");
    AssertEqual(1, progress.Count, "bounded party progress count");
    AssertEqual(7, progress[0].Level, "checked party progress level");
    AssertEqual(true, reader.TryIsRootCommandMenuActive(out var rootActive), "checked root menu ownership read");
    AssertEqual(true, rootActive, "checked root menu ownership state");

    var tornFormation = new TearingLegacyAddressSpace(
        memory,
        (uint)BattleStateReader.AddressBattleFormationId,
        BitConverter.GetBytes((ushort)13));
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(tornFormation).ReadEncounter().IsValid,
        "torn battle formation invalidates the encounter");

    var incompleteActors = CreateValidBattleEncounterMemory();
    var incompleteEnemyBase =
        (uint)BattleStateReader.AddressBattleActors + 9u * BattleStateReader.BattleActorSize;
    incompleteActors.Write(
        incompleteEnemyBase + BattleStateReader.ActorInstanceIdOffset,
        [1]);
    WriteUInt16(
        incompleteActors,
        (uint)BattleStateReader.AddressActiveEnemyMask,
        (1 << 4) | (1 << 9));
    incompleteActors.Remove(
        (uint)BattleStateReader.AddressEnemySceneIndexRecords +
        5u * BattleStateReader.EnemySceneIndexRecordSize);
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(incompleteActors).TryReadBattleActors(out _),
        "an active actor with an unmapped scene record cannot produce a partial actor list");

    var enemyBase = (uint)BattleStateReader.AddressBattleActors + 4u * BattleStateReader.BattleActorSize;
    var tornPrivateEnemyHp = new TearingLegacyAddressSpace(
        memory,
        enemyBase + BattleStateReader.ActorCurrentHpOffset,
        BitConverter.GetBytes(41));
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(tornPrivateEnemyHp).TryReadBattleActors(out _),
        "torn unsensed enemy HP invalidates the raw actor candidate before public projection");

    var tornPrivateEnemyStatus = new TearingLegacyAddressSpace(
        memory,
        enemyBase + BattleStateReader.ActorStatusMaskOffset,
        BitConverter.GetBytes(1u << 3));
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(tornPrivateEnemyStatus).TryReadBattleActors(out _),
        "torn unsensed enemy status invalidates the raw actor candidate before public projection");

    const int eventIndex = 7;
    memory.Write((uint)BattleStateReader.AddressAnimationEventIndex, [eventIndex]);
    var eventAddress = (uint)BattleStateReader.AddressAnimationEventQueue +
        (uint)eventIndex * BattleStateReader.AnimationEventSize;
    memory.Write(eventAddress + BattleStateReader.AnimationEventAttackerOffset, [4]);
    memory.Write(eventAddress + BattleStateReader.AnimationEventKindOffset, [BattleStateReader.ActionAnimationEventKind]);
    memory.Write(eventAddress + BattleStateReader.AnimationEventCommandOffset, [BattleStateReader.EnemyActionCommandId]);
    WriteUInt16(memory, eventAddress + BattleStateReader.AnimationEventActionOffset, 2);
    WriteUInt16(memory, (uint)BattleStateReader.AddressSceneAttackIds + 2u * BattleStateReader.SceneAttackIdSize, 0x011A);
    WriteFf7Text(
        memory,
        (uint)BattleStateReader.AddressSceneAttackNames + 2u * BattleStateReader.SceneAttackNameLength,
        "Rifle",
        BattleStateReader.SceneAttackNameLength);
    WriteUInt16(memory, (uint)BattleStateReader.AddressBattleActionTargetMask, 1);

    var action = CreateCheckedBattleStateReader(memory).ReadCurrentEnemyAction();
    AssertEqual(true, action.IsValid, "checked enemy action");
    AssertEqual("Rifle", action.ActionName, "checked enemy action name");

    var tornAction = new TearingLegacyAddressSpace(
        memory,
        eventAddress + BattleStateReader.AnimationEventActionOffset,
        BitConverter.GetBytes((ushort)3));
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(tornAction).ReadCurrentEnemyAction().IsValid,
        "torn enemy action index invalidates the snapshot");

    var invalidAction = CreateValidBattleEncounterMemory();
    invalidAction.Write(
        (uint)BattleStateReader.AddressAnimationEventIndex,
        [BattleStateReader.AnimationEventCount]);
    AssertEqual(
        false,
        CreateCheckedBattleStateReader(invalidAction).ReadCurrentEnemyAction().IsValid,
        "out-of-range animation event index is rejected before address arithmetic");
}

static void AssertBattleStateReaderRejectsStableInvalidActiveActorCollections()
{
    var invalidPartyHp = CreateValidBattleEncounterMemory();
    WriteInt32(
        invalidPartyHp,
        (uint)BattleStateReader.AddressBattleActors + BattleStateReader.ActorCurrentHpOffset,
        351);
    var invalidPartyReader = CreateCheckedBattleStateReader(invalidPartyHp);
    AssertEqual(
        false,
        invalidPartyReader.TryReadPartyActors(out _),
        "active party actor with impossible HP invalidates the party collection");
    AssertEqual(
        false,
        invalidPartyReader.TryReadBattleActors(out _),
        "active party actor with impossible HP invalidates the full actor collection");
    AssertEqual(
        false,
        invalidPartyReader.ReadEncounter().IsValid,
        "active party actor with impossible HP invalidates the encounter");

    var invalidSingleEnemyName = CreateValidBattleEncounterMemory();
    WriteFf7Text(
        invalidSingleEnemyName,
        (uint)BattleStateReader.AddressEnemyData,
        string.Empty,
        BattleStateReader.EnemyNameLength);
    AssertInvalidActorCollection(
        invalidSingleEnemyName,
        "single active enemy with a blank name");

    var invalidSingleEnemyHp = CreateValidBattleEncounterMemory();
    var firstEnemyBase =
        (uint)BattleStateReader.AddressBattleActors + 4u * BattleStateReader.BattleActorSize;
    WriteInt32(
        invalidSingleEnemyHp,
        firstEnemyBase + BattleStateReader.ActorCurrentHpOffset,
        43);
    AssertInvalidActorCollection(
        invalidSingleEnemyHp,
        "single active enemy with impossible HP");

    var invalidMultipleEnemyName = CreateValidBattleEncounterMemory();
    AddBattleEnemy(invalidMultipleEnemyName, actorIndex: 5, sceneEnemyIndex: 1, "Sweeper");
    WriteFf7Text(
        invalidMultipleEnemyName,
        (uint)BattleStateReader.AddressEnemyData + BattleStateReader.EnemyDataSize,
        string.Empty,
        BattleStateReader.EnemyNameLength);
    AssertInvalidActorCollection(
        invalidMultipleEnemyName,
        "one blank-name active enemy among multiple enemies");

    var invalidMultipleEnemyHp = CreateValidBattleEncounterMemory();
    AddBattleEnemy(invalidMultipleEnemyHp, actorIndex: 5, sceneEnemyIndex: 1, "Sweeper");
    var secondEnemyBase =
        (uint)BattleStateReader.AddressBattleActors + 5u * BattleStateReader.BattleActorSize;
    WriteInt32(
        invalidMultipleEnemyHp,
        secondEnemyBase + BattleStateReader.ActorCurrentHpOffset,
        51);
    AssertInvalidActorCollection(
        invalidMultipleEnemyHp,
        "one impossible-HP active enemy among multiple enemies");
}

static void AssertInvalidActorCollection(
    ContiguousLegacyAddressSpace memory,
    string label)
{
    var reader = CreateCheckedBattleStateReader(memory);
    AssertEqual(false, reader.TryReadBattleActors(out _), $"{label} invalidates the full actor collection");
    AssertEqual(false, reader.ReadEncounter().IsValid, $"{label} invalidates the encounter");
}

static void AssertBattleResultsReaderChecksRewardsAndBookends()
{
    var memory = CreateValidBattleResultsMemory();
    var reader = new BattleResultsReader(memory, itemId => itemId == 7 ? "Phoenix Down" : null);
    var snapshot = reader.Read();
    AssertEqual(true, snapshot.IsValid, "checked battle results snapshot");
    AssertEqual(1, snapshot.Items.Count, "bounded battle reward count");
    AssertEqual("Phoenix Down", snapshot.Items[0].Name, "checked battle reward name");

    var requiredBytes = new (uint Address, string Label)[]
    {
        ((uint)BattleResultsReader.AddressCurrentModule, "module"),
        ((uint)BattleResultsReader.AddressResultsState, "state"),
        ((uint)BattleResultsReader.AddressResultsPageReady, "page readiness"),
        ((uint)BattleResultsReader.AddressExperience, "experience"),
        ((uint)BattleResultsReader.AddressAp, "AP"),
        ((uint)BattleResultsReader.AddressGil, "gil"),
        ((uint)BattleResultsReader.AddressHasRewardItems, "has reward items"),
        ((uint)BattleResultsReader.AddressRewardSelection, "reward selection"),
        ((uint)BattleResultsReader.AddressRewardTransition, "reward transition"),
        ((uint)BattleResultsReader.AddressInputEdges, "input edges"),
        ((uint)BattleResultsReader.AddressInputRepeat, "input repeat"),
        ((uint)BattleResultsReader.AddressHeldInput, "held input"),
        ((uint)BattleResultsReader.AddressRewardItems, "reward id"),
        ((uint)BattleResultsReader.AddressRewardItems + 2u, "reward quantity")
    };
    foreach (var required in requiredBytes)
    {
        var unmapped = CreateValidBattleResultsMemory();
        unmapped.Remove(required.Address);
        AssertEqual(
            false,
            new BattleResultsReader(unmapped, _ => "item").Read().IsValid,
            $"unmapped battle result {required.Label} invalidates the snapshot");
    }

    var tornReward = new TearingLegacyAddressSpace(
        memory,
        (uint)BattleResultsReader.AddressRewardItems + 2u,
        BitConverter.GetBytes((ushort)3));
    AssertEqual(
        false,
        new BattleResultsReader(tornReward, _ => "item").Read().IsValid,
        "torn battle reward invalidates the snapshot");

    var invalidItem = CreateValidBattleResultsMemory(itemId: BattleResultsReader.InventoryObjectCount);
    AssertEqual(
        false,
        new BattleResultsReader(invalidItem, _ => "invented").Read().IsValid,
        "out-of-range reward id cannot be named");

}

static void AssertBattleDamagePopupReaderChecksEffectsAndBookends()
{
    var memory = CreateValidBattleDamageMemory();
    var popup = new BattleDamagePopupReader(memory).Read();
    AssertEqual(true, popup.IsValid, "checked battle damage popup");
    AssertEqual(12, popup.Value, "checked battle damage value");

    var record = (uint)BattleDamagePopupReader.AddressEffectData + 5u * BattleDamagePopupReader.EffectRecordSize;
    var requiredBytes = new (uint Address, string Label)[]
    {
        ((uint)BattleDamagePopupReader.AddressCurrentModule, "module"),
        ((uint)BattleDamagePopupReader.AddressCurrentEffectIndex, "effect index"),
        (record + BattleDamagePopupReader.StateOffset, "effect state"),
        (record + BattleDamagePopupReader.ValueOffset, "value"),
        (record + BattleDamagePopupReader.TargetActorOffset, "target actor"),
        (record + BattleDamagePopupReader.FlagsOffset, "flags")
    };
    foreach (var required in requiredBytes)
    {
        var unmapped = CreateValidBattleDamageMemory();
        unmapped.Remove(required.Address);
        AssertEqual(
            false,
            new BattleDamagePopupReader(unmapped).Read().IsValid,
            $"unmapped battle damage {required.Label} invalidates the snapshot");
    }

    var tornValue = new TearingLegacyAddressSpace(
        memory,
        record + BattleDamagePopupReader.ValueOffset,
        BitConverter.GetBytes((short)13));
    AssertEqual(
        false,
        new BattleDamagePopupReader(tornValue).Read().IsValid,
        "torn battle damage value invalidates the snapshot");

    var invalidEffect = CreateValidBattleDamageMemory(effectIndex: ushort.MaxValue);
    AssertEqual(
        false,
        new BattleDamagePopupReader(invalidEffect).Read().IsValid,
        "out-of-range effect index is rejected before address arithmetic");

    var invalidActor = CreateValidBattleDamageMemory(targetActor: 3);
    AssertEqual(
        false,
        new BattleDamagePopupReader(invalidActor).Read().IsValid,
        "non-actor battle slot is rejected");
}

static BattleStateReader CreateCheckedBattleStateReader(
    ILegacyAddressSpace memory,
    Func<int, string?>? abilityName = null) =>
    new(
        memory,
        new SavemapPartyReader(memory),
        resolveAbilityName: abilityName);

static ContiguousLegacyAddressSpace CreateValidBattleStateMemory(int currentHp = 314)
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write((uint)BattleStateReader.AddressCurrentModule, [BattleStateReader.BattleModule]);
    memory.Write((uint)BattleStateReader.AddressCurrentActorSlot, [0]);
    memory.Write((uint)BattleStateReader.AddressMenuWindowStates + 1u, [BattleStateReader.ActiveWindowState]);
    memory.Write(
        (uint)SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset,
        [0]);
    memory.Write(
        (uint)SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset + 1u,
        [byte.MaxValue]);
    memory.Write(
        (uint)SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset + 2u,
        [byte.MaxValue]);
    WriteFf7Text(
        memory,
        (uint)SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset +
            SavemapPartyReader.CharacterNameOffset,
        "Cloud",
        12);
    memory.Write(
        (uint)SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset +
            SavemapPartyReader.LevelOffset,
        [7]);
    var actorBase = (uint)BattleStateReader.AddressBattleActors;
    memory.Write(actorBase + BattleStateReader.ActorInstanceIdOffset, [0]);
    memory.Write(actorBase + BattleStateReader.BattleActorSize + BattleStateReader.ActorInstanceIdOffset, [byte.MaxValue]);
    memory.Write(actorBase + 2u * BattleStateReader.BattleActorSize + BattleStateReader.ActorInstanceIdOffset, [byte.MaxValue]);
    for (var enemyActorIndex = 4; enemyActorIndex <= 9; enemyActorIndex++)
    {
        memory.Write(
            actorBase + (uint)enemyActorIndex * BattleStateReader.BattleActorSize +
                BattleStateReader.ActorInstanceIdOffset,
            [byte.MaxValue]);
    }

    WriteUInt32(memory, actorBase + BattleStateReader.ActorStatusMaskOffset, 0);
    WriteUInt16(memory, actorBase + BattleStateReader.ActorCurrentMpOffset, 42);
    WriteUInt16(memory, actorBase + BattleStateReader.ActorMaxMpOffset, 54);
    WriteInt32(memory, actorBase + BattleStateReader.ActorCurrentHpOffset, currentHp);
    WriteInt32(memory, actorBase + BattleStateReader.ActorMaxHpOffset, 350);
    return memory;
}

static ContiguousLegacyAddressSpace CreateValidBattleEncounterMemory()
{
    var memory = CreateValidBattleStateMemory();
    WriteUInt16(memory, (uint)BattleStateReader.AddressBattleFormationId, 12);
    memory.Write((uint)BattleStateReader.AddressBattleLayoutType, [2]);
    for (var enemySlot = 0; enemySlot <= 5; enemySlot++)
    {
        memory.Write(
            (uint)BattleStateReader.AddressEnemySceneIndexRecords +
                (uint)enemySlot * BattleStateReader.EnemySceneIndexRecordSize,
            [byte.MaxValue]);
    }

    memory.Write((uint)BattleStateReader.AddressEnemySceneIndexRecords, [0]);
    WriteUInt16(memory, (uint)BattleStateReader.AddressActiveEnemyMask, 1 << 4);
    WriteFf7Text(
        memory,
        (uint)BattleStateReader.AddressEnemyData,
        "Grunt",
        BattleStateReader.EnemyNameLength);
    var enemyBase = (uint)BattleStateReader.AddressBattleActors + 4u * BattleStateReader.BattleActorSize;
    memory.Write(enemyBase + BattleStateReader.ActorInstanceIdOffset, [0]);
    WriteUInt32(memory, enemyBase + BattleStateReader.ActorStatusMaskOffset, 0);
    WriteUInt16(memory, enemyBase + BattleStateReader.ActorCurrentMpOffset, 0);
    WriteUInt16(memory, enemyBase + BattleStateReader.ActorMaxMpOffset, 0);
    WriteInt32(memory, enemyBase + BattleStateReader.ActorCurrentHpOffset, 42);
    WriteInt32(memory, enemyBase + BattleStateReader.ActorMaxHpOffset, 42);
    return memory;
}

static void AddBattleEnemy(
    ContiguousLegacyAddressSpace memory,
    int actorIndex,
    int sceneEnemyIndex,
    string name)
{
    const int firstEnemyActorIndex = 4;
    var enemySlot = actorIndex - firstEnemyActorIndex;
    memory.Write(
        (uint)BattleStateReader.AddressEnemySceneIndexRecords +
            (uint)enemySlot * BattleStateReader.EnemySceneIndexRecordSize,
        [checked((byte)sceneEnemyIndex)]);
    WriteFf7Text(
        memory,
        (uint)BattleStateReader.AddressEnemyData +
            (uint)sceneEnemyIndex * BattleStateReader.EnemyDataSize,
        name,
        BattleStateReader.EnemyNameLength);
    var actorBase =
        (uint)BattleStateReader.AddressBattleActors +
        (uint)actorIndex * BattleStateReader.BattleActorSize;
    memory.Write(
        actorBase + BattleStateReader.ActorInstanceIdOffset,
        [checked((byte)sceneEnemyIndex)]);
    if (!memory.TryReadUInt16((uint)BattleStateReader.AddressActiveEnemyMask, out var activeMask))
    {
        activeMask = 0;
    }
    WriteUInt16(
        memory,
        (uint)BattleStateReader.AddressActiveEnemyMask,
        checked((ushort)(activeMask | (1 << actorIndex))));
    WriteUInt32(memory, actorBase + BattleStateReader.ActorStatusMaskOffset, 0);
    WriteUInt16(memory, actorBase + BattleStateReader.ActorCurrentMpOffset, 0);
    WriteUInt16(memory, actorBase + BattleStateReader.ActorMaxMpOffset, 0);
    WriteInt32(memory, actorBase + BattleStateReader.ActorCurrentHpOffset, 50);
    WriteInt32(memory, actorBase + BattleStateReader.ActorMaxHpOffset, 50);
}

static ContiguousLegacyAddressSpace CreateValidBattleResultsMemory(
    int itemId = 7,
    int quantity = 2)
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write((uint)BattleResultsReader.AddressCurrentModule, [BattleResultsReader.ResultsModule]);
    memory.Write((uint)BattleResultsReader.AddressResultsPageReady, [1]);
    WriteInt32(memory, (uint)BattleResultsReader.AddressResultsState, 0);
    WriteInt32(memory, (uint)BattleResultsReader.AddressExperience, 125);
    WriteInt32(memory, (uint)BattleResultsReader.AddressAp, 8);
    WriteInt32(memory, (uint)BattleResultsReader.AddressGil, 96);
    WriteInt32(memory, (uint)BattleResultsReader.AddressHasRewardItems, 1);
    WriteInt32(memory, (uint)BattleResultsReader.AddressRewardSelection, 0);
    WriteUInt16(memory, (uint)BattleResultsReader.AddressRewardTransition, 0);
    WriteInt32(memory, (uint)BattleResultsReader.AddressInputEdges, 0);
    WriteInt32(memory, (uint)BattleResultsReader.AddressInputRepeat, 0);
    WriteInt32(memory, (uint)BattleResultsReader.AddressHeldInput, 0);
    for (var index = 0; index < BattleResultsReader.RewardItemCount; index++)
    {
        var address = (uint)BattleResultsReader.AddressRewardItems +
            (uint)index * BattleResultsReader.RewardItemSize;
        WriteUInt16(memory, address, index == 0 ? checked((ushort)itemId) : ushort.MaxValue);
        WriteUInt16(memory, address + 2u, index == 0 ? checked((ushort)quantity) : (ushort)0);
        WriteUInt16(memory, address + BattleResultsReader.RewardSelectedOffset, 0);
    }

    return memory;
}

static ContiguousLegacyAddressSpace CreateValidBattleDamageMemory(
    ushort effectIndex = 5,
    int targetActor = 0)
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write((uint)BattleDamagePopupReader.AddressCurrentModule, [BattleStateReader.BattleModule]);
    WriteUInt16(memory, (uint)BattleDamagePopupReader.AddressCurrentEffectIndex, effectIndex);
    if (effectIndex >= BattleDamagePopupReader.EffectCount)
    {
        return memory;
    }

    var record = (uint)BattleDamagePopupReader.AddressEffectData +
        (uint)effectIndex * BattleDamagePopupReader.EffectRecordSize;
    memory.Write(record + BattleDamagePopupReader.StateOffset, [0]);
    WriteUInt16(memory, record + BattleDamagePopupReader.ValueOffset, 12);
    WriteInt32(memory, record + BattleDamagePopupReader.TargetActorOffset, targetActor);
    WriteInt32(memory, record + BattleDamagePopupReader.FlagsOffset, 0);
    return memory;
}

static void WriteFf7Text(
    ContiguousLegacyAddressSpace memory,
    uint address,
    string text,
    int length)
{
    var bytes = Enumerable.Repeat((byte)0xFF, length).ToArray();
    var count = Math.Min(text.Length, Math.Max(0, length - 1));
    for (var index = 0; index < count; index++)
    {
        bytes[index] = text[index] == ' ' ? (byte)0 : checked((byte)(text[index] - 0x20));
    }

    memory.Write(address, bytes);
}

static void AssertFieldFoundationReadersLiveInSharedAssembly()
{
    var expected = typeof(ILegacyAddressSpace).Assembly;
    Type[] fieldTypes =
    [
        typeof(FieldPositionReader),
        typeof(FieldPositionReadResult),
        typeof(FieldPositionSnapshot),
        typeof(FieldAudibleCueStateReader),
        typeof(FieldAudibleCueState),
        typeof(FieldBoundaryStateReader),
        typeof(FieldBoundaryStateReadResult),
        typeof(FieldBoundaryState),
        typeof(FieldNavigationControlReader),
        typeof(FieldNavigationControlReadResult),
        typeof(FieldNavigationControlTransform),
        typeof(FieldNavigationStickDirection),
        typeof(SquatMinigameStateReader),
        typeof(SquatMinigameSnapshot),
        typeof(SquatMinigameStep),
        typeof(SquatMinigamePromptTracker),
        typeof(SquatMinigameCueCoordinator)
    ];

    foreach (var fieldType in fieldTypes)
    {
        AssertEqual(expected, fieldType.Assembly, $"shared field layout type {fieldType.Name}");
    }
}

static void AssertFieldPositionReaderChecksDirectAndNestedReads()
{
    var memory = CreateValidFieldPositionMemory();
    var result = new FieldPositionReader(memory).Read();
    AssertEqual(true, result.IsUsable, "checked field position");
    AssertEqual(100, result.Position.X, "checked field X");
    AssertEqual(-200, result.Position.Y, "checked field Y");
    AssertEqual((ushort)9, result.Position.TriangleId, "checked field triangle");

    const uint modelBase = 0x10000 + FieldPositionReader.FieldModelStride;
    var partial = CreateValidFieldPositionMemory();
    partial.Remove(modelBase + FieldPositionReader.ModelXOffset + 3);
    AssertEqual(false, new FieldPositionReader(partial).Read().IsUsable, "partial model coordinate fails");
    AssertEqual(false, new FieldPositionReader(new ContiguousLegacyAddressSpace()).Read().IsUsable, "unmapped field globals fail");

    var remappedPointer = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressFieldModelsPtr,
        BitConverter.GetBytes(0x20000u));
    AssertEqual(false, new FieldPositionReader(remappedPointer).Read().IsUsable, "remapped model pointer fails");

    var overflow = CreateFieldPositionHeader(0xFFFFFFF0u);
    AssertEqual(false, new FieldPositionReader(overflow).Read().IsUsable, "model pointer wrap fails");

    var highAddress = CreateHighAddressFieldPositionMemory();
    var highAddressResult = new FieldPositionReader(highAddress).Read();
    AssertEqual(true, highAddressResult.IsUsable, "high guest model address is valid");
    AssertEqual(0x80001000u, highAddressResult.ModelBase, "high guest model address stays uint32");

    var moduleTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressCurrentModule,
        [2]);
    AssertEqual(false, new FieldPositionReader(moduleTear).Read().IsUsable, "field module tearing fails");

    var fieldTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressFieldId,
        BitConverter.GetBytes((ushort)117));
    AssertEqual(false, new FieldPositionReader(fieldTear).Read().IsUsable, "field id tearing fails");

    var modelIndexTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressFieldCurrentModelId,
        BitConverter.GetBytes((ushort)0));
    AssertEqual(false, new FieldPositionReader(modelIndexTear).Read().IsUsable, "model index tearing fails");

    var modelCountTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressFieldNumModels,
        [1]);
    AssertEqual(false, new FieldPositionReader(modelCountTear).Read().IsUsable, "model count tearing fails");

    var nestedTears = new (uint Address, byte[] Replacement, string Label)[]
    {
        (modelBase + FieldPositionReader.ModelXOffset, BitConverter.GetBytes(101), "field X tearing fails"),
        (modelBase + FieldPositionReader.ModelYOffset, BitConverter.GetBytes(-201), "field Y tearing fails"),
        (modelBase + FieldPositionReader.ModelZOffset, BitConverter.GetBytes(301), "field Z tearing fails"),
        ((uint)FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.FieldObjectStride +
            FieldPositionReader.ObjectTriangleOffset, BitConverter.GetBytes((ushort)10), "field triangle tearing fails"),
        (modelBase + FieldPositionReader.ModelDirectionOffset, [0x40], "field direction tearing fails")
    };
    foreach (var tear in nestedTears)
    {
        var tornMemory = new TearingLegacyAddressSpace(memory, tear.Address, tear.Replacement);
        AssertEqual(false, new FieldPositionReader(tornMemory).Read().IsUsable, tear.Label);
    }

    var secondSnapshotUnreadable = new TearingLegacyAddressSpace(
        memory,
        modelBase + FieldPositionReader.ModelXOffset,
        [0]);
    var unreadablePositionResult = new FieldPositionReader(secondSnapshotUnreadable).Read();
    AssertEqual(
        false,
        unreadablePositionResult.IsUsable,
        "unreadable second field-position snapshot fails");
    AssertEqual(
        true,
        unreadablePositionResult.Diagnostic.Contains("failed", StringComparison.Ordinal),
        "unreadable second field-position snapshot diagnostic");

    var legacyNestedTear = new TearingLegacyAddressSpace(
        memory,
        modelBase + FieldPositionReader.ModelXOffset,
        BitConverter.GetBytes(101));
    var legacyResult = CreateLegacyFieldPositionReader(legacyNestedTear).Read();
    AssertEqual(false, legacyResult.IsUsable, "legacy field-position snapshot tearing fails");
    AssertEqual(
        true,
        legacyResult.Diagnostic.Contains("changed", StringComparison.Ordinal),
        "legacy field-position tear diagnostic");
}

static ContiguousLegacyAddressSpace CreateHighAddressFieldPositionMemory()
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write((uint)FieldPositionReader.AddressCurrentModule, [FieldPositionReader.FieldModule]);
    WriteUInt16(memory, (uint)FieldPositionReader.AddressFieldId, 116);
    WriteUInt16(memory, (uint)FieldPositionReader.AddressFieldCurrentModelId, 0);
    memory.Write((uint)FieldPositionReader.AddressFieldNumModels, [1]);
    WriteUInt32(memory, (uint)FieldPositionReader.AddressFieldModelsPtr, 0x80001000u);
    WriteInt32(memory, 0x80001000u + FieldPositionReader.ModelXOffset, 10);
    WriteInt32(memory, 0x80001000u + FieldPositionReader.ModelYOffset, 20);
    WriteInt32(memory, 0x80001000u + FieldPositionReader.ModelZOffset, 30);
    memory.Write(0x80001000u + FieldPositionReader.ModelDirectionOffset, [0x40]);
    WriteUInt16(
        memory,
        (uint)FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.ObjectTriangleOffset,
        4);
    return memory;
}

static ContiguousLegacyAddressSpace CreateValidFieldPositionMemory()
{
    var memory = CreateFieldPositionHeader(0x10000);
    const uint modelBase = 0x10000 + FieldPositionReader.FieldModelStride;
    WriteInt32(memory, modelBase + FieldPositionReader.ModelXOffset, 100);
    WriteInt32(memory, modelBase + FieldPositionReader.ModelYOffset, -200);
    WriteInt32(memory, modelBase + FieldPositionReader.ModelZOffset, 300);
    memory.Write(modelBase + FieldPositionReader.ModelDirectionOffset, [0xC0]);
    var objectBase = (uint)FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.FieldObjectStride;
    WriteUInt16(memory, objectBase + FieldPositionReader.ObjectTriangleOffset, 9);
    return memory;
}

static ContiguousLegacyAddressSpace CreateFieldPositionHeader(uint modelTable)
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write((uint)FieldPositionReader.AddressCurrentModule, [FieldPositionReader.FieldModule]);
    WriteUInt16(memory, (uint)FieldPositionReader.AddressFieldId, 116);
    WriteUInt16(memory, (uint)FieldPositionReader.AddressFieldCurrentModelId, 1);
    memory.Write((uint)FieldPositionReader.AddressFieldNumModels, [2]);
    WriteUInt32(memory, (uint)FieldPositionReader.AddressFieldModelsPtr, modelTable);
    return memory;
}

static void AssertFieldAudibleCueReaderChecksStateAndBookends()
{
    var memory = CreateValidFieldModeMemory();
    memory.Write((uint)FieldAudibleCueStateReader.AddressUserControl, [0]);
    memory.Write((uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount, [1]);
    WriteUInt16(memory, (uint)FieldAudibleCueStateReader.AddressFieldMovieActive, 0);
    var reader = new FieldAudibleCueStateReader(memory);

    AssertEqual(true, reader.TryRead(out var state), "checked audible cue state");
    AssertEqual("dialogue", state.Reason, "checked audible cue reason");

    var partial = CreateValidFieldModeMemory();
    partial.Write((uint)FieldAudibleCueStateReader.AddressUserControl, [0]);
    partial.Write((uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount, [0]);
    partial.Write((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, [0]);
    AssertEqual(false, new FieldAudibleCueStateReader(partial).TryRead(out _), "partial movie state fails");
    AssertEqual(
        "unreadable field state",
        new FieldAudibleCueStateReader(partial).Read().Reason,
        "unreadable audible native-state diagnostic");
    AssertEqual(false, new FieldAudibleCueStateReader(new ContiguousLegacyAddressSpace()).TryRead(out _), "unmapped audible state fails");

    var moduleTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressCurrentModule,
        [2]);
    AssertEqual(false, new FieldAudibleCueStateReader(moduleTear).TryRead(out _), "audible module tearing fails");
    var moduleTearForDiagnostic = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressCurrentModule,
        [2]);
    AssertEqual(
        "unstable field state",
        new FieldAudibleCueStateReader(moduleTearForDiagnostic).Read().Reason,
        "audible native tear diagnostic");

    var fieldTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressFieldId,
        BitConverter.GetBytes((ushort)117));
    AssertEqual(false, new FieldAudibleCueStateReader(fieldTear).TryRead(out _), "audible field tearing fails");

    var cueTears = new (uint Address, byte[] Replacement, string Label)[]
    {
        ((uint)FieldAudibleCueStateReader.AddressUserControl, [1], "audible user-control tearing fails"),
        ((uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount, [0], "audible message-count tearing fails"),
        ((uint)FieldAudibleCueStateReader.AddressFieldMovieActive,
            BitConverter.GetBytes((ushort)1), "audible movie-state tearing fails")
    };
    foreach (var tear in cueTears)
    {
        var tornMemory = new TearingLegacyAddressSpace(memory, tear.Address, tear.Replacement);
        AssertEqual(false, new FieldAudibleCueStateReader(tornMemory).TryRead(out _), tear.Label);
    }

    var unavailableOwnership = new FieldAudibleCueStateReader(memory, () => false);
    AssertEqual(true, unavailableOwnership.TryRead(out var unavailableState), "unreadable dialogue ownership is observable");
    AssertEqual(true, unavailableState.IsSuppressed, "unreadable active dialogue suppresses cues");
    AssertEqual("dialogue unavailable", unavailableState.Reason, "unreadable dialogue suppression reason");

    var ownershipReads = 0;
    var tornOwnership = new FieldAudibleCueStateReader(memory, () => ++ownershipReads == 1);
    AssertEqual(false, tornOwnership.TryRead(out _), "dialogue ownership tearing fails");

    var legacyCueTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldAudibleCueStateReader.AddressUserControl,
        [1]);
    var legacyCueReader = CreateLegacyFieldAudibleCueStateReader(legacyCueTear);
    AssertEqual("unstable field state", legacyCueReader.Read().Reason, "legacy audible cue snapshot tearing fails closed");

    var legacyUnavailableOwnership = CreateLegacyFieldAudibleCueStateReader(memory, () => false).Read();
    AssertEqual(true, legacyUnavailableOwnership.IsSuppressed, "legacy unreadable active dialogue suppresses cues");
    AssertEqual(
        "dialogue unavailable",
        legacyUnavailableOwnership.Reason,
        "legacy unreadable dialogue suppression reason");
}

static void AssertFieldAudibleCueOwnershipReaderReleasesClosedStaleMessageCount()
{
    var memory = CreateValidFieldModeMemory();
    memory.Write((uint)FieldAudibleCueStateReader.AddressUserControl, [0]);
    memory.Write((uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount, [1]);
    WriteUInt16(memory, (uint)FieldAudibleCueStateReader.AddressFieldMovieActive, 0);
    memory.Write(
        (uint)FieldMessageReader.AddressFieldWindowStates,
        [FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState,
            FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState]);
    var reader = new FieldAudibleCueOwnershipStateReader(memory, () => false);
    var state = reader.Read();

    AssertEqual(false, state.IsSuppressed, "closed stale field message count releases navigation");
    AssertEqual("gameplay", state.Reason, "closed stale field message count cue reason");
    AssertEqual((byte)1, state.ActiveMessageCount, "stale count remains available for diagnostics");
}

static ContiguousLegacyAddressSpace CreateValidFieldModeMemory()
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write((uint)FieldPositionReader.AddressCurrentModule, [FieldPositionReader.FieldModule]);
    WriteUInt16(memory, (uint)FieldPositionReader.AddressFieldId, 116);
    return memory;
}

static void AssertFieldBoundaryReaderChecksNestedPointerAndBookends()
{
    var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 116, 0, 0, 0, 0, 0, 0);
    var memory = CreateValidFieldModeMemory();
    WriteUInt32(memory, (uint)FieldBoundaryStateReader.AddressFieldGlobalObjectPtr, 0x30000);
    memory.Write(0x30000 + FieldBoundaryStateReader.BoundaryBitsOffset, [0x05]);
    var reader = new FieldBoundaryStateReader(memory);

    var result = reader.Read(position, 8);
    AssertEqual(true, result.IsUsable, "checked field boundary state");
    AssertEqual(true, result.State.IsBoundaryEnabled(2), "checked field boundary bit");

    var equivalent = reader.Read(position, 8);
    AssertEqual(result.State, equivalent.State, "equivalent checked boundary snapshots have value equality");
    var publishedBits = (IList<byte>)result.State.Bits;
    var mutationRejected = false;
    try
    {
        publishedBits[0] = 0;
    }
    catch (NotSupportedException)
    {
        mutationRejected = true;
    }

    AssertEqual(true, mutationRejected, "published validated boundary bits reject mutation");
    AssertEqual(true, result.State.IsBoundaryEnabled(2), "mutation attempt cannot alter validated boundary state");

    var partial = CreateValidFieldModeMemory();
    WriteUInt32(partial, (uint)FieldBoundaryStateReader.AddressFieldGlobalObjectPtr, 0x30000);
    AssertEqual(false, new FieldBoundaryStateReader(partial).Read(position, 8).IsUsable, "unmapped IDLCK byte fails");

    var remappedPointer = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldBoundaryStateReader.AddressFieldGlobalObjectPtr,
        BitConverter.GetBytes(0x31000u));
    AssertEqual(false, new FieldBoundaryStateReader(remappedPointer).Read(position, 8).IsUsable, "remapped IDLCK pointer fails");

    var overflow = CreateValidFieldModeMemory();
    WriteUInt32(overflow, (uint)FieldBoundaryStateReader.AddressFieldGlobalObjectPtr, 0xFFFFFFF0u);
    AssertEqual(false, new FieldBoundaryStateReader(overflow).Read(position, 8).IsUsable, "IDLCK pointer wrap fails");

    var fieldTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressFieldId,
        BitConverter.GetBytes((ushort)117));
    AssertEqual(false, new FieldBoundaryStateReader(fieldTear).Read(position, 8).IsUsable, "IDLCK field tearing fails");

    var moduleTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressCurrentModule,
        [2]);
    AssertEqual(false, new FieldBoundaryStateReader(moduleTear).Read(position, 8).IsUsable, "IDLCK module tearing fails");

    var boundaryTear = new TearingLegacyAddressSpace(memory, 0x30000 + FieldBoundaryStateReader.BoundaryBitsOffset, [0x04]);
    AssertEqual(false, new FieldBoundaryStateReader(boundaryTear).Read(position, 8).IsUsable, "IDLCK bit tearing fails");

    var unreadableSecondBoundary = new TearingLegacyAddressSpace(
        memory,
        0x30000 + FieldBoundaryStateReader.BoundaryBitsOffset,
        []);
    var unreadableBoundaryResult = new FieldBoundaryStateReader(unreadableSecondBoundary).Read(position, 8);
    AssertEqual(
        false,
        unreadableBoundaryResult.IsUsable,
        "unreadable second IDLCK snapshot fails");
    AssertEqual(
        true,
        unreadableBoundaryResult.Diagnostic.Contains("unreadable", StringComparison.Ordinal),
        "unreadable second IDLCK snapshot diagnostic");

    var legacyBoundaryTear = new TearingLegacyAddressSpace(
        memory,
        0x30000 + FieldBoundaryStateReader.BoundaryBitsOffset,
        [0x04]);
    var legacyBoundaryResult = CreateLegacyFieldBoundaryStateReader(legacyBoundaryTear).Read(position, 8);
    AssertEqual(false, legacyBoundaryResult.IsUsable, "legacy IDLCK snapshot tearing fails");
    AssertEqual(
        true,
        legacyBoundaryResult.Diagnostic.Contains("changed", StringComparison.Ordinal),
        "legacy IDLCK tear diagnostic");
}

static void AssertFieldNavigationControlReaderChecksNestedPointerAndBookends()
{
    var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 116, 0, 0, 0, 0, 0, 0);
    var memory = CreateValidFieldModeMemory();
    WriteUInt32(memory, (uint)FieldNavigationControlReader.AddressFieldTriggersPtr, 0x40000);
    memory.Write(0x40000 + FieldNavigationControlReader.ControlDirectionOffset, [0xA0]);
    var result = new FieldNavigationControlReader(memory).Read(position);
    AssertEqual(true, result.IsUsable, "checked navigation control state");
    AssertEqual(-96, result.Transform.SignedControlDirection, "checked signed control direction");

    var partial = CreateValidFieldModeMemory();
    WriteUInt32(partial, (uint)FieldNavigationControlReader.AddressFieldTriggersPtr, 0x40000);
    AssertEqual(false, new FieldNavigationControlReader(partial).Read(position).IsUsable, "unmapped control byte fails");

    var remappedPointer = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldNavigationControlReader.AddressFieldTriggersPtr,
        BitConverter.GetBytes(0x41000u));
    AssertEqual(false, new FieldNavigationControlReader(remappedPointer).Read(position).IsUsable, "remapped trigger pointer fails");

    var overflow = CreateValidFieldModeMemory();
    WriteUInt32(overflow, (uint)FieldNavigationControlReader.AddressFieldTriggersPtr, 0xFFFFFFFFu);
    AssertEqual(false, new FieldNavigationControlReader(overflow).Read(position).IsUsable, "trigger pointer wrap fails");

    var moduleTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressCurrentModule,
        [2]);
    AssertEqual(false, new FieldNavigationControlReader(moduleTear).Read(position).IsUsable, "control module tearing fails");

    var fieldTear = new TearingLegacyAddressSpace(
        memory,
        (uint)FieldPositionReader.AddressFieldId,
        BitConverter.GetBytes((ushort)117));
    AssertEqual(false, new FieldNavigationControlReader(fieldTear).Read(position).IsUsable, "control field tearing fails");

    var controlTear = new TearingLegacyAddressSpace(
        memory,
        0x40000 + FieldNavigationControlReader.ControlDirectionOffset,
        [0x80]);
    AssertEqual(false, new FieldNavigationControlReader(controlTear).Read(position).IsUsable, "control direction tearing fails");

    var unreadableSecondControl = new TearingLegacyAddressSpace(
        memory,
        0x40000 + FieldNavigationControlReader.ControlDirectionOffset,
        []);
    var unreadableControlResult = new FieldNavigationControlReader(unreadableSecondControl).Read(position);
    AssertEqual(
        false,
        unreadableControlResult.IsUsable,
        "unreadable second control snapshot fails");
    AssertEqual(
        true,
        unreadableControlResult.Diagnostic.Contains("failed", StringComparison.Ordinal),
        "unreadable second control snapshot diagnostic");

    var legacyControlTear = new TearingLegacyAddressSpace(
        memory,
        0x40000 + FieldNavigationControlReader.ControlDirectionOffset,
        [0x80]);
    var legacyControlResult = CreateLegacyFieldNavigationControlReader(legacyControlTear).Read(position);
    AssertEqual(false, legacyControlResult.IsUsable, "legacy control snapshot tearing fails");
    AssertEqual(
        true,
        legacyControlResult.Diagnostic.Contains("changed", StringComparison.Ordinal),
        "legacy control tear diagnostic");
}

static FieldPositionReader CreateLegacyFieldPositionReader(ILegacyAddressSpace memory) =>
    new(
        address => ReadRequiredInt32(memory, address),
        address => ReadRequiredInt16(memory, address),
        address => ReadRequiredUInt16(memory, address),
        address => ReadRequiredByte(memory, address));

static FieldNavigationControlReader CreateLegacyFieldNavigationControlReader(ILegacyAddressSpace memory) =>
    new(
        address => ReadRequiredInt32(memory, address),
        address => ReadRequiredByte(memory, address));

static FieldBoundaryStateReader CreateLegacyFieldBoundaryStateReader(ILegacyAddressSpace memory) =>
    new(
        address => ReadRequiredInt32(memory, address),
        address => ReadRequiredByte(memory, address),
        (_, _) => true);

static FieldAudibleCueStateReader CreateLegacyFieldAudibleCueStateReader(
    ILegacyAddressSpace memory,
    Func<bool>? hasReadableActiveMessage = null) =>
    new(
        address => ReadRequiredByte(memory, address),
        address => ReadRequiredUInt16(memory, address),
        hasReadableActiveMessage);

static byte ReadRequiredByte(ILegacyAddressSpace memory, int address) =>
    memory.TryReadByte(checked((uint)address), out var value)
        ? value
        : throw new InvalidOperationException($"Missing test byte at 0x{address:X8}.");

static short ReadRequiredInt16(ILegacyAddressSpace memory, int address) =>
    memory.TryReadInt16(checked((uint)address), out var value)
        ? value
        : throw new InvalidOperationException($"Missing test Int16 at 0x{address:X8}.");

static ushort ReadRequiredUInt16(ILegacyAddressSpace memory, int address) =>
    memory.TryReadUInt16(checked((uint)address), out var value)
        ? value
        : throw new InvalidOperationException($"Missing test UInt16 at 0x{address:X8}.");

static int ReadRequiredInt32(ILegacyAddressSpace memory, int address) =>
    memory.TryReadInt32(checked((uint)address), out var value)
        ? value
        : throw new InvalidOperationException($"Missing test Int32 at 0x{address:X8}.");

static void AssertMenuLayoutReadersLiveInSharedAssembly()
{
    var expected = typeof(ILegacyAddressSpace).Assembly;
    Type[] layoutTypes =
    [
        typeof(MenuWidgetKind),
        typeof(MenuWidgetDescriptor),
        typeof(MenuWidgetCatalog),
        typeof(ActiveMenuWidgetReader),
        typeof(ActiveMenuWidgetSnapshot),
        typeof(MainMenuStateReader),
        typeof(MainMenuSnapshot),
        typeof(MainMenuSelection),
        typeof(TitleMenuCursorReader),
        typeof(TitleMenuCursorSnapshot),
        typeof(TitleMenuCursorSelection),
        typeof(ConfigMenuValueReader),
        typeof(MagicMenuSelectionReader),
        typeof(MagicMenuSpellSnapshot),
        typeof(SavemapPartyReader),
        typeof(PartyMemberSnapshot),
        typeof(StatusMenuSnapshot)
    ];

    foreach (var layoutType in layoutTypes)
    {
        AssertEqual(expected, layoutType.Assembly, $"shared menu layout type {layoutType.Name}");
    }
}

static void AssertMainMenuStateReaderChecksEveryFieldAndBookends()
{
    var memory = CreateValidMainMenuMemory();
    var reader = new MainMenuStateReader(memory);

    AssertEqual(true, reader.TryReadSnapshot(out var snapshot), "checked main-menu snapshot read");
    AssertEqual(2, snapshot.CursorIndex, "checked main-menu cursor");
    AssertEqual(0x7ffu, snapshot.EnabledMask, "checked main-menu enabled mask");

    var partial = CreateValidMainMenuMemory();
    partial.Remove((uint)MainMenuStateReader.AddressAnimation + 3);
    AssertEqual(false, new MainMenuStateReader(partial).TryReadSnapshot(out _), "partial main-menu field fails");
    AssertEqual(false, new MainMenuStateReader(new ContiguousLegacyAddressSpace()).TryReadSnapshot(out _), "unmapped main-menu state fails");

    var stateTearing = new TearingLegacyAddressSpace(
        memory,
        (uint)MainMenuStateReader.AddressState,
        BitConverter.GetBytes(5));
    AssertEqual(false, new MainMenuStateReader(stateTearing).TryReadSnapshot(out _), "main-menu state bookend tearing fails");

    var openTearing = new TearingLegacyAddressSpace(
        memory,
        (uint)MainMenuStateReader.AddressOpenFlag,
        BitConverter.GetBytes(0));
    AssertEqual(false, new MainMenuStateReader(openTearing).TryReadSnapshot(out _), "main-menu open bookend tearing fails");

    var cursorTearing = new TearingLegacyAddressSpace(
        memory,
        (uint)MainMenuStateReader.AddressCursorIndex,
        BitConverter.GetBytes(3));
    AssertEqual(false, new MainMenuStateReader(cursorTearing).TryReadSnapshot(out _), "main-menu cursor bookend tearing fails");

    var maskTearing = new TearingLegacyAddressSpace(
        memory,
        (uint)MainMenuStateReader.AddressEnabledMask,
        BitConverter.GetBytes(0x7fbu));
    AssertEqual(false, new MainMenuStateReader(maskTearing).TryReadSnapshot(out _), "main-menu mask bookend tearing fails");
}

static ContiguousLegacyAddressSpace CreateValidMainMenuMemory()
{
    var memory = new ContiguousLegacyAddressSpace();
    WriteInt32(memory, (uint)MainMenuStateReader.AddressState, 1);
    WriteInt32(memory, (uint)MainMenuStateReader.AddressSelectedA, 0);
    WriteInt32(memory, (uint)MainMenuStateReader.AddressSelectedB, 4);
    WriteInt32(memory, (uint)MainMenuStateReader.AddressCursorIndex, 2);
    WriteInt32(memory, (uint)MainMenuStateReader.AddressTarget, 2);
    WriteInt32(memory, (uint)MainMenuStateReader.AddressOpenFlag, 1);
    WriteUInt32(memory, (uint)MainMenuStateReader.AddressEnabledMask, 0x7ffu);
    WriteUInt32(memory, (uint)MainMenuStateReader.AddressDisabledMask, 0);
    WriteInt32(memory, (uint)MainMenuStateReader.AddressAnimation, 16);
    return memory;
}

static void AssertActiveMenuWidgetReaderChecksEveryFieldAndBookend()
{
    const uint address = 0x5000;
    var memory = CreateValidWidgetMemory(address);
    var reader = new ActiveMenuWidgetReader(memory);

    AssertEqual(true, reader.TryRead(address, out var snapshot), "checked active widget uint read");
    AssertEqual(address, snapshot.Address, "checked active widget preserves guest uint address");
    AssertEqual(3, snapshot.Cursor, "checked active widget cursor");
    AssertEqual(9, snapshot.ScrollState, "checked active widget scroll state");

    var partial = CreateValidWidgetMemory(address);
    partial.Remove(address + 0x31);
    AssertEqual(false, new ActiveMenuWidgetReader(partial).TryRead(address, out _), "partial active widget field fails");
    AssertEqual(false, new ActiveMenuWidgetReader(new ContiguousLegacyAddressSpace()).TryRead(address, out _), "unmapped active widget fails");
    AssertEqual(false, reader.TryRead(-1, out _), "negative int widget compatibility address fails");

    var tearing = new TearingLegacyAddressSpace(CreateValidWidgetMemory(address), address, BitConverter.GetBytes(2));
    AssertEqual(false, new ActiveMenuWidgetReader(tearing).TryRead(address, out _), "active widget bookend tearing fails");

    var cursorTearing = new TearingLegacyAddressSpace(CreateValidWidgetMemory(address), address + 0x04, BitConverter.GetBytes(4));
    AssertEqual(false, new ActiveMenuWidgetReader(cursorTearing).TryRead(address, out _), "active widget cursor bookend tearing fails");

    var scrollTearing = new TearingLegacyAddressSpace(CreateValidWidgetMemory(address), address + 0x14, BitConverter.GetBytes(5));
    AssertEqual(false, new ActiveMenuWidgetReader(scrollTearing).TryRead(address, out _), "active widget scroll bookend tearing fails");
}

static ContiguousLegacyAddressSpace CreateValidWidgetMemory(uint address)
{
    var memory = new ContiguousLegacyAddressSpace();
    WriteInt32(memory, address, 1);
    WriteInt32(memory, address + 0x04, 3);
    WriteInt32(memory, address + 0x08, 2);
    WriteInt32(memory, address + 0x0C, 10);
    WriteInt32(memory, address + 0x14, 4);
    WriteInt32(memory, address + 0x24, -1);
    WriteInt32(memory, address + 0x30, 9);
    return memory;
}

static void AssertConfigMenuValueReaderChecksEveryFieldAndBookend()
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write((uint)ConfigMenuValueReader.AddressBattleSpeed, [128]);
    WriteUInt16(memory, (uint)ConfigMenuValueReader.AddressSettingsBits, 0x0040);
    WriteInt32(memory, (uint)ConfigMenuValueReader.AddressSoundModalState, ConfigMenuValueReader.SoundModalActiveState);
    WriteInt32(memory, (uint)ConfigMenuValueReader.AddressMusicVolume, 73);
    var reader = new ConfigMenuValueReader(memory);

    AssertEqual("50 percent from Fast to Slow", reader.ReadMainValue("Battle speed")?.Text, "checked config slider");
    AssertEqual("Recommended", reader.ReadMainValue("ATB")?.Text, "checked config settings");
    AssertEqual("Music volume, 73 percent", reader.ReadSoundVolume(0)?.Text, "checked sound volume");

    var partial = new ContiguousLegacyAddressSpace();
    partial.Write((uint)ConfigMenuValueReader.AddressSettingsBits, [0x40]);
    AssertEqual(null, new ConfigMenuValueReader(partial).ReadMainValue("ATB"), "partial config setting fails");
    AssertEqual(null, new ConfigMenuValueReader(new ContiguousLegacyAddressSpace()).ReadMainValue("Battle speed"), "unmapped config slider fails");

    var tearing = new TearingLegacyAddressSpace(memory, (uint)ConfigMenuValueReader.AddressSoundModalState, BitConverter.GetBytes(0));
    AssertEqual(null, new ConfigMenuValueReader(tearing).ReadSoundVolume(0), "sound modal bookend tearing fails");
}

static void AssertMagicMenuSelectionReaderChecksEveryFieldAndBookend()
{
    const uint widgetAddress = 0x00DD1708;
    const int selectedIndex = 15;
    const int currentMpBaseAddress = 0x00DBA4AC;
    var recordAddress = MagicMenuSelectionReader.AddressMagicRecords +
        MagicMenuSelectionReader.CharacterBlockSize +
        (selectedIndex * MagicMenuSelectionReader.RecordSize);
    var widget = new ActiveMenuWidgetSnapshot(
        widgetAddress, "Magic list", MenuWidgetKind.MagicList, 1, 3, 2, 10, 4, -1, 9);

    ContiguousLegacyAddressSpace CreateMemory(byte spellId = 7, byte mpCost = 12)
    {
        var candidate = CreateValidWidgetMemory(widgetAddress);
        candidate.Write((uint)MagicMenuSelectionReader.AddressSelectedPartySlot, [1]);
        WriteUInt16(
            candidate,
            (uint)(currentMpBaseAddress + MagicMenuSelectionReader.CharacterBlockSize),
            40);
        candidate.Write((uint)recordAddress, [spellId, mpCost]);
        return candidate;
    }

    var memory = CreateMemory();
    var reader = new MagicMenuSelectionReader(memory, id => id == 7 ? "Fire" : null, _ => "Fire damage");

    AssertEqual(true, reader.TryRead(widget, out var snapshot), "checked magic record");
    AssertEqual(12, snapshot.MpCost, "checked magic MP cost");

    var partial = CreateMemory();
    partial.Remove((uint)(recordAddress + MagicMenuSelectionReader.MpCostOffset));
    AssertEqual(false, new MagicMenuSelectionReader(partial, _ => "Fire", _ => null).TryRead(widget, out _), "partial magic record fails");
    AssertEqual(false, new MagicMenuSelectionReader(new ContiguousLegacyAddressSpace(), _ => "Fire", _ => null).TryRead(widget, out _), "unmapped magic state fails");

    var partyTear = new TearingLegacyAddressSpace(memory, (uint)MagicMenuSelectionReader.AddressSelectedPartySlot, [2]);
    AssertEqual(false, new MagicMenuSelectionReader(partyTear, _ => "Fire", _ => null).TryRead(widget, out _), "magic party bookend tearing fails");

    var spellTear = new TearingLegacyAddressSpace(memory, (uint)recordAddress, [8, 12]);
    AssertEqual(false, new MagicMenuSelectionReader(spellTear, id => id is 7 or 8 ? "Native spell" : null, _ => null).TryRead(widget, out _), "magic spell id tearing fails");

    var costTear = new TearingLegacyAddressSpace(memory, (uint)recordAddress, [7, 13]);
    AssertEqual(false, new MagicMenuSelectionReader(costTear, _ => "Fire", _ => null).TryRead(widget, out _), "magic MP cost tearing fails");

    var currentMpAddress = (uint)(currentMpBaseAddress + MagicMenuSelectionReader.CharacterBlockSize);
    var currentMpTear = new TearingLegacyAddressSpace(memory, currentMpAddress, BitConverter.GetBytes((ushort)41));
    AssertEqual(false, new MagicMenuSelectionReader(currentMpTear, _ => "Fire", _ => null).TryRead(widget, out _), "magic current MP tearing fails");

    var cursorTear = new TearingLegacyAddressSpace(memory, widgetAddress + 0x04, BitConverter.GetBytes(4));
    AssertEqual(false, new MagicMenuSelectionReader(cursorTear, _ => "Fire", _ => null).TryRead(widget, out _), "magic widget cursor tearing fails");

    var remapped = new RemappingLegacyAddressSpace(
        CreateMemory(7, 12),
        CreateMemory(8, 13),
        (uint)recordAddress,
        MagicMenuSelectionReader.MpCostOffset + 1);
    AssertEqual(false, new MagicMenuSelectionReader(remapped, id => id is 7 or 8 ? "Native spell" : null, _ => null).TryRead(widget, out _), "remapped magic record fails");

    var missingCurrentMp = CreateMemory();
    missingCurrentMp.Remove(currentMpAddress + 1);
    AssertEqual(false, new MagicMenuSelectionReader(missingCurrentMp, _ => "Fire", _ => null).TryRead(widget, out _), "partial current MP fails");

    var invalidParty = CreateMemory();
    invalidParty.Write((uint)MagicMenuSelectionReader.AddressSelectedPartySlot, [3]);
    AssertEqual(false, new MagicMenuSelectionReader(invalidParty, _ => "Fire", _ => null).TryRead(widget, out _), "out-of-range magic party slot fails");

    var negativeScrollWidget = widget with { ScrollOffset = -1 };
    var negativeScroll = CreateMemory();
    WriteInt32(negativeScroll, widgetAddress + 0x14, -1);
    var aliasedRecordAddress = MagicMenuSelectionReader.AddressMagicRecords +
        MagicMenuSelectionReader.CharacterBlockSize +
        (5 * MagicMenuSelectionReader.RecordSize);
    negativeScroll.Write((uint)aliasedRecordAddress, [7, 12]);
    AssertEqual(
        false,
        new MagicMenuSelectionReader(negativeScroll, _ => "Fire", _ => null).TryRead(negativeScrollWidget, out _),
        "negative magic scroll offset cannot alias a native record");

    var overflowWidget = widget with { ScrollOffset = int.MaxValue };
    var overflowMemory = CreateMemory();
    WriteInt32(overflowMemory, widgetAddress + 0x14, int.MaxValue);
    overflowMemory.Write((uint)aliasedRecordAddress, [7, 12]);
    AssertEqual(
        false,
        new MagicMenuSelectionReader(overflowMemory, _ => "Fire", _ => null).TryRead(overflowWidget, out _),
        "overflowing magic scroll arithmetic cannot wrap to an aliased record");

    var forgedAddressWidget = widget with { Address = uint.MaxValue };
    AssertEqual(false, reader.TryRead(forgedAddressWidget, out _), "overflowing magic widget address fails");
}

static void AssertSavemapPartyReaderChecksEveryFieldAndBookend()
{
    const int savemapAddress = 0x7000;
    var memory = CreateValidPartyMemory(savemapAddress);
    var reader = new SavemapPartyReader(memory, savemapAddress: savemapAddress);

    AssertEqual(true, reader.TryReadPartySlot(0, out var member), "checked party slot");
    AssertEqual("A", member.Name, "checked party name");
    AssertEqual(true, reader.TryReadStatusSummary(0, out var status), "checked status summary");
    AssertEqual(300, status.CurrentHp, "checked status HP");

    var partial = CreateValidPartyMemory(savemapAddress);
    partial.Remove((uint)(savemapAddress + SavemapPartyReader.CharactersOffset + SavemapPartyReader.CurrentHpOffset + 1));
    AssertEqual(false, new SavemapPartyReader(partial, savemapAddress: savemapAddress).TryReadStatusSummary(0, out _), "partial status field fails");
    AssertEqual(false, new SavemapPartyReader(new ContiguousLegacyAddressSpace(), savemapAddress: savemapAddress).TryReadPartySlot(0, out _), "unmapped party slot fails");

    var partyAddress = (uint)(savemapAddress + SavemapPartyReader.PartyMembersOffset);
    var tearing = new TearingLegacyAddressSpace(memory, partyAddress, [1]);
    AssertEqual(false, new SavemapPartyReader(tearing, savemapAddress: savemapAddress).TryReadPartySlot(0, out _), "party slot bookend tearing fails");
}

static ContiguousLegacyAddressSpace CreateValidPartyMemory(int savemapAddress)
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write((uint)(savemapAddress + SavemapPartyReader.PartyMembersOffset), [0]);
    var characterBase = savemapAddress + SavemapPartyReader.CharactersOffset;
    memory.Write((uint)(characterBase + SavemapPartyReader.CharacterNameOffset),
        [0x21, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
    memory.Write((uint)(characterBase + SavemapPartyReader.LevelOffset), [15]);
    memory.Write((uint)(characterBase + SavemapPartyReader.LimitLevelOffset), [2]);
    memory.Write((uint)(characterBase + SavemapPartyReader.EquippedWeaponOffset), [1, 2, 0xFF]);
    WriteUInt16(memory, (uint)(characterBase + SavemapPartyReader.CurrentHpOffset), 300);
    WriteUInt16(memory, (uint)(characterBase + SavemapPartyReader.CurrentMpOffset), 40);
    WriteUInt16(memory, (uint)(characterBase + SavemapPartyReader.MaxHpOffset), 500);
    WriteUInt16(memory, (uint)(characterBase + SavemapPartyReader.MaxMpOffset), 60);
    WriteUInt32(memory, (uint)(characterBase + SavemapPartyReader.ExperienceOffset), 1234);
    WriteUInt32(memory, (uint)(characterBase + SavemapPartyReader.ExperienceToNextLevelOffset), 234);

    var computed = (uint)SavemapPartyReader.AddressComputedPartyData;
    memory.Write(computed + SavemapPartyReader.ComputedStrengthOffset, [20, 21, 22, 23, 24, 25]);
    WriteUInt16(memory, computed + SavemapPartyReader.ComputedAttackOffset, 30);
    WriteUInt16(memory, computed + SavemapPartyReader.ComputedDefenseOffset, 31);
    WriteUInt16(memory, computed + SavemapPartyReader.ComputedMagicAttackOffset, 32);
    WriteUInt16(memory, computed + SavemapPartyReader.ComputedMagicDefenseOffset, 33);
    memory.Write((uint)(SavemapPartyReader.AddressWeaponAttackPercent + SavemapPartyReader.WeaponRecordSize), [96]);
    memory.Write((uint)(SavemapPartyReader.AddressArmorDefensePercent + (2 * SavemapPartyReader.ArmorRecordSize)), [11, 4]);
    return memory;
}

static void WriteInt32(ContiguousLegacyAddressSpace memory, uint address, int value) =>
    memory.Write(address, BitConverter.GetBytes(value));

static void WriteUInt16(ContiguousLegacyAddressSpace memory, uint address, ushort value) =>
    memory.Write(address, BitConverter.GetBytes(value));

static void WriteUInt32(ContiguousLegacyAddressSpace memory, uint address, uint value) =>
    memory.Write(address, BitConverter.GetBytes(value));

static void AssertSharedObservationContractsLiveInSharedAssembly()
{
    var expected = typeof(ILegacyAddressSpace).Assembly;
    Type[] contractTypes =
    [
        typeof(FieldMessageCandidate),
        typeof(NativeMenuSelection),
        typeof(MenuWidgetState),
        typeof(MenuCursorDrawObservation),
        typeof(MenuTextRenderEntry),
        typeof(InventoryItemSnapshot),
        typeof(InventoryItemReader)
    ];

    foreach (var contractType in contractTypes)
    {
        AssertEqual(expected, contractType.Assembly, $"shared observation contract {contractType.Name}");
    }
}

static void AssertInventoryItemReaderChecksGuestSlot()
{
    const uint savemapAddress = 0x80001000;
    const uint itemsOffset = 0x20;
    const int slot = 3;
    var slotAddress = savemapAddress + itemsOffset + (uint)(slot * sizeof(ushort));
    var memory = new ContiguousLegacyAddressSpace();
    WriteUInt16(memory, slotAddress, (ushort)((4 << 9) | 7));

    var reader = new InventoryItemReader(
        memory,
        itemId => itemId == 7 ? "Phoenix Down" : null,
        itemId => itemId == 7 ? "Restores life" : null,
        savemapAddress,
        itemsOffset);

    AssertEqual(true, reader.TryRead(slot, out var snapshot), "checked inventory slot read");
    AssertEqual(7, snapshot.ItemId, "checked inventory item id");
    AssertEqual(4, snapshot.Quantity, "checked inventory quantity");
    AssertEqual("Phoenix Down", snapshot.Name, "checked inventory item name");
    AssertEqual("Restores life", snapshot.Description, "checked inventory item description");

    const uint crossPageAddress = 0x80001FFF;
    var crossPage = new ContiguousLegacyAddressSpace();
    WriteUInt16(crossPage, crossPageAddress, (ushort)((127 << 9) | 0x1FE));
    var crossPageReader = new InventoryItemReader(
        crossPage,
        savemapAddress: crossPageAddress,
        itemsOffset: 0);
    AssertEqual(true, crossPageReader.TryRead(0, out var crossPageSnapshot), "cross-page inventory slot read");
    AssertEqual(0x1FE, crossPageSnapshot.ItemId, "cross-page high item id");
    AssertEqual(127, crossPageSnapshot.Quantity, "cross-page maximum quantity");
}

static void AssertInventoryItemReaderRejectsUnstableAndUnreadableGuestSlots()
{
    const uint savemapAddress = 0x5000;
    const uint slotAddress = savemapAddress + InventoryItemReader.ItemsOffset;
    var stable = new ContiguousLegacyAddressSpace();
    WriteUInt16(stable, slotAddress, (ushort)((2 << 9) | 3));

    var inPlaceMutation = new TearingLegacyAddressSpace(
        stable,
        slotAddress,
        BitConverter.GetBytes((ushort)((4 << 9) | 7)));
    var tearingReader = new InventoryItemReader(
        inPlaceMutation,
        _ => "must not publish",
        savemapAddress: savemapAddress);
    AssertEqual(false, tearingReader.TryRead(0, out _), "in-place inventory slot mutation fails");

    var duringResolution = new ContiguousLegacyAddressSpace();
    WriteUInt16(duringResolution, slotAddress, (ushort)((2 << 9) | 3));
    var mutationDuringResolutionReader = new InventoryItemReader(
        duringResolution,
        _ =>
        {
            WriteUInt16(duringResolution, slotAddress, (ushort)((4 << 9) | 7));
            return "changed";
        },
        savemapAddress: savemapAddress);
    AssertEqual(
        false,
        mutationDuringResolutionReader.TryRead(0, out _),
        "inventory slot mutation during label resolution fails");

    var partial = new ContiguousLegacyAddressSpace();
    partial.Write(slotAddress, [0x03]);
    AssertEqual(
        false,
        new InventoryItemReader(partial, savemapAddress: savemapAddress).TryRead(0, out _),
        "partial inventory slot word fails");
    AssertEqual(
        false,
        new InventoryItemReader(new ContiguousLegacyAddressSpace(), savemapAddress: savemapAddress).TryRead(0, out _),
        "unmapped inventory slot fails");
}

static void AssertInventoryItemReaderRejectsInvalidGuestSlotAddressesAndValues()
{
    var valid = new ContiguousLegacyAddressSpace();
    var reader = new InventoryItemReader(valid, savemapAddress: 0x6000, itemsOffset: 0);
    AssertEqual(false, reader.TryRead(-1, out _), "negative checked inventory slot fails");
    AssertEqual(false, reader.TryRead(InventoryItemReader.SlotCount, out _), "out-of-range checked inventory slot fails");

    var empty = new ContiguousLegacyAddressSpace();
    WriteUInt16(empty, 0x6000, 0xFFFF);
    AssertEqual(
        false,
        new InventoryItemReader(empty, savemapAddress: 0x6000, itemsOffset: 0).TryRead(0, out _),
        "checked empty inventory slot fails");

    var zeroQuantity = new ContiguousLegacyAddressSpace();
    WriteUInt16(zeroQuantity, 0x6000, 7);
    AssertEqual(
        false,
        new InventoryItemReader(zeroQuantity, savemapAddress: 0x6000, itemsOffset: 0).TryRead(0, out _),
        "checked zero-quantity inventory slot fails");

    AssertEqual(
        false,
        new InventoryItemReader(valid, savemapAddress: uint.MaxValue, itemsOffset: 0).TryRead(0, out _),
        "checked inventory word address overflow fails");
    AssertEqual(
        false,
        new InventoryItemReader(valid, savemapAddress: 0xFFFFFF00, itemsOffset: 0x200).TryRead(0, out _),
        "checked inventory offset addition overflow fails");

    var zeroMapped = new ConstantLegacyAddressSpace((ushort)((1 << 9) | 1));
    AssertEqual(
        false,
        new InventoryItemReader(zeroMapped, savemapAddress: 0, itemsOffset: 0).TryRead(0, out _),
        "null checked inventory address fails even when a provider exposes bytes");
}

static void AssertPureFf7ParsersLiveInSharedAssembly()
{
    var expected = typeof(ILegacyAddressSpace).Assembly;
    Type[] parserTypes =
    [
        typeof(Ff7EncodedTextDecoder),
        typeof(Ff7LzsDecoder),
        typeof(LgpArchiveReader),
        typeof(FieldMapListResolver),
        typeof(FlevelDataSource),
        typeof(FlevelFieldNameResolver),
        typeof(FlevelFieldTextResolver),
        typeof(Kernel2ItemNameResolver),
        typeof(Kernel2TextDatabase),
        typeof(Ff7PcSaveFileReader),
        typeof(Ff7SaveFileRepository)
    ];

    foreach (var parserType in parserTypes)
    {
        AssertEqual(expected, parserType.Assembly, $"shared parser assembly {parserType.Name}");
    }
}

static void AssertSharedTextAndLzsDecodersPreserveLegacySemantics()
{
    AssertEqual("ABC", Ff7EncodedTextDecoder.Decode([0x21, 0x22, 0x23, 0xFF]), "shared FFVII text decode");
    AssertEqual(
        "414243",
        Convert.ToHexString(Ff7LzsDecoder.Decode([0x07, 0x41, 0x42, 0x43])),
        "shared LZS literal decode");
}

static void AssertSharedMapListParserPreservesNativeOrder()
{
    var bytes = new byte[2 + (2 * 32)];
    bytes[0] = 2;
    System.Text.Encoding.ASCII.GetBytes("md1stin").CopyTo(bytes, 2);
    System.Text.Encoding.ASCII.GetBytes("md1_1").CopyTo(bytes, 34);

    var names = FieldMapListResolver.ReadFieldNames(bytes);

    AssertEqual("md1stin", names[0], "first shared map-list entry");
    AssertEqual("md1_1", names[1], "second shared map-list entry");
}

static void AssertFf7PcSaveFileReaderValidatesNativeSlotChecksum()
{
    var directory = Path.Combine(Path.GetTempPath(), $"ff7-shared-save-checksum-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "save00.ff7");
        var valid = CreateFf7SaveSlot("Cloud", "No.1 Reactor", 8);
        WriteFf7SaveFile(path, valid);
        AssertEqual(true, Ff7PcSaveFileReader.TryReadSlot(path, 1, out var preview), "valid checksummed FFVII slot");
        AssertEqual("Cloud", preview.LeadCharacterName, "valid checksummed FFVII slot preview");

        var oneBitCorruption = (byte[])valid.Clone();
        oneBitCorruption[0x100] ^= 0x01;
        WriteFf7SaveFile(path, oneBitCorruption);
        AssertEqual(false, Ff7PcSaveFileReader.TryReadSlot(path, 1, out _), "one-bit slot corruption fails closed");

        var wrongChecksum = (byte[])valid.Clone();
        wrongChecksum[0] ^= 0x01;
        WriteFf7SaveFile(path, wrongChecksum);
        AssertEqual(false, Ff7PcSaveFileReader.TryReadSlot(path, 1, out _), "wrong slot checksum fails closed");

        var alternate = CreateFf7SaveSlot("Tifa", "Sector 7 Slums", 9);
        alternate[^1] = 0xA5;
        WriteFf7SaveUInt32(alternate, 0, CalculateReferenceFf7SaveChecksum(alternate));
        var torn = (byte[])valid.Clone();
        alternate.AsSpan(Ff7PcSaveFileReader.SlotSize / 2).CopyTo(
            torn.AsSpan(Ff7PcSaveFileReader.SlotSize / 2));
        AssertEqual(
            false,
            ReadStoredFf7SaveChecksum(torn) == CalculateReferenceFf7SaveChecksum(torn),
            "torn fixture must not accidentally have a valid checksum");
        WriteFf7SaveFile(path, torn);
        AssertEqual(false, Ff7PcSaveFileReader.TryReadSlot(path, 1, out _), "torn slot snapshot fails closed");

        File.WriteAllBytes(
            path,
            new byte[Ff7PcSaveFileReader.HeaderSize + Ff7PcSaveFileReader.SlotSize - 1]);
        AssertEqual(false, Ff7PcSaveFileReader.TryReadSlot(path, 1, out _), "truncated slot fails closed");

        var corruptEmpty = new byte[Ff7PcSaveFileReader.SlotSize];
        corruptEmpty[^1] = 0x01;
        WriteFf7SaveFile(path, corruptEmpty);
        AssertEqual(false, Ff7PcSaveFileReader.TryReadSlot(path, 1, out _), "partially zeroed slot is not reported empty");

        WriteFf7SaveFile(path, new byte[Ff7PcSaveFileReader.SlotSize]);
        AssertEqual(true, Ff7PcSaveFileReader.TryReadSlot(path, 1, out var empty), "zeroed empty slot remains readable");
        AssertEqual(true, empty.IsEmpty, "zeroed empty slot remains empty");
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static byte[] CreateFf7SaveSlot(string name, string location, byte level)
{
    var slot = new byte[Ff7PcSaveFileReader.SlotSize];
    slot[0x04] = level;
    WriteFf7SaveText(slot, 0x08, name, 16);
    WriteFf7SaveUInt16(slot, 0x18, 296);
    WriteFf7SaveUInt16(slot, 0x1A, 334);
    WriteFf7SaveUInt16(slot, 0x1C, 18);
    WriteFf7SaveUInt16(slot, 0x1E, 64);
    WriteFf7SaveUInt32(slot, 0x20, 539);
    WriteFf7SaveUInt32(slot, 0x24, 1572);
    WriteFf7SaveText(slot, 0x28, location, 32);
    WriteFf7SaveUInt32(slot, 0, CalculateReferenceFf7SaveChecksum(slot));
    return slot;
}

static void WriteFf7SaveFile(string path, byte[] slot)
{
    var file = new byte[Ff7PcSaveFileReader.HeaderSize +
        Ff7PcSaveFileReader.SlotsPerFile * Ff7PcSaveFileReader.SlotSize];
    slot.CopyTo(file, Ff7PcSaveFileReader.HeaderSize);
    File.WriteAllBytes(path, file);
}

static uint ReadStoredFf7SaveChecksum(byte[] slot) =>
    System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(slot);

static uint CalculateReferenceFf7SaveChecksum(byte[] slot)
{
    AssertEqual(Ff7PcSaveFileReader.SlotSize, slot.Length, "FFVII checksum fixture length");
    var result = 0xFFFF;
    for (var index = sizeof(uint); index < slot.Length; index++)
    {
        result ^= slot[index] << 8;
        for (var bit = 0; bit < 8; bit++)
        {
            result = (result & 0x8000) != 0
                ? (result << 1) ^ 0x1021
                : result << 1;
        }

        result &= 0xFFFF;
    }

    return (uint)((result ^ 0xFFFF) & 0xFFFF);
}

static void WriteFf7SaveText(byte[] destination, int offset, string text, int length)
{
    Array.Fill(destination, (byte)0xFF, offset, length);
    var limit = Math.Min(text.Length, Math.Max(0, length - 1));
    for (var index = 0; index < limit; index++)
    {
        destination[offset + index] = text[index] == ' '
            ? (byte)0
            : checked((byte)(text[index] - 0x20));
    }
}

static void WriteFf7SaveUInt16(byte[] destination, int offset, ushort value) =>
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset), value);

static void WriteFf7SaveUInt32(byte[] destination, int offset, uint value) =>
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset), value);

static void AssertTypedReadsAreExplicitLittleEndian()
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write(0x1000, [0xFE]);
    memory.Write(0x1010, [0x80, 0xFF]);
    memory.Write(0x1020, [0x34, 0x12]);
    memory.Write(0x1030, [0x78, 0x56, 0x34, 0xF2]);
    memory.Write(0x1040, [0xEF, 0xCD, 0xAB, 0x89]);
    memory.Write(0x1050, BitConverter.GetBytes(123.5f));

    AssertEqual(true, memory.TryReadByte(0x1000, out var byteValue), "byte read");
    AssertEqual((byte)0xFE, byteValue, "byte value");
    AssertEqual(true, memory.TryReadInt16(0x1010, out var int16Value), "Int16 read");
    AssertEqual((short)-128, int16Value, "Int16 little-endian value");
    AssertEqual(true, memory.TryReadUInt16(0x1020, out var uint16Value), "UInt16 read");
    AssertEqual((ushort)0x1234, uint16Value, "UInt16 little-endian value");
    AssertEqual(true, memory.TryReadInt32(0x1030, out var int32Value), "Int32 read");
    AssertEqual(unchecked((int)0xF2345678), int32Value, "Int32 little-endian value");
    AssertEqual(true, memory.TryReadUInt32(0x1040, out var uint32Value), "UInt32 read");
    AssertEqual(0x89ABCDEFu, uint32Value, "UInt32 little-endian value");
    AssertEqual(true, memory.TryReadSingle(0x1050, out var singleValue), "Single read");
    AssertEqual(123.5f, singleValue, "Single little-endian value");
}

static void AssertTypedReadFailuresRemainFailures()
{
    var memory = new ContiguousLegacyAddressSpace();
    memory.Write(0x2000, [0x11, 0x22, 0x33]);

    AssertEqual(false, memory.TryReadUInt32(0x2000, out var partialValue), "partial UInt32 read fails");
    AssertEqual(0u, partialValue, "failed UInt32 read clears output");
    AssertEqual(false, memory.TryReadByte(0, out var nullValue), "null guest address fails");
    AssertEqual((byte)0, nullValue, "failed byte read clears output");
    AssertEqual(false, memory.TryReadSingle(0x3000, out var missingSingle), "unmapped Single read fails");
    AssertEqual(0.0f, missingSingle, "failed Single read clears output");
}

static void AssertContractExposesGuestAddressesWithoutHostPointers()
{
    var method = typeof(ILegacyAddressSpace).GetMethod(nameof(ILegacyAddressSpace.TryRead));
    AssertEqual(typeof(uint), method?.GetParameters()[0].ParameterType, "guest address type");
    AssertEqual(
        false,
        typeof(ILegacyAddressSpace).GetProperties().Any(property =>
            property.PropertyType == typeof(IntPtr) || property.PropertyType == typeof(UIntPtr)),
        "legacy address-space contract host pointer exposure");
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}

sealed class ContiguousLegacyAddressSpace : ILegacyAddressSpace
{
    private readonly Dictionary<uint, byte> bytes = [];

    public void Write(uint address, IReadOnlyList<byte> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            bytes[checked(address + (uint)index)] = values[index];
        }
    }

    public void Remove(uint address) => bytes.Remove(address);

    public bool TryRead(uint virtualAddress, Span<byte> destination)
    {
        if (virtualAddress == 0 || (ulong)virtualAddress + (ulong)destination.Length > (ulong)uint.MaxValue + 1)
        {
            destination.Clear();
            return false;
        }

        for (var index = 0; index < destination.Length; index++)
        {
            if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index]))
            {
                destination.Clear();
                return false;
            }
        }

        return true;
    }
}

sealed class ConstantLegacyAddressSpace(ushort value) : ILegacyAddressSpace
{
    public bool TryRead(uint virtualAddress, Span<byte> destination)
    {
        if (destination.Length != sizeof(ushort))
        {
            destination.Clear();
            return false;
        }

        BitConverter.GetBytes(value).CopyTo(destination);
        return true;
    }
}

sealed class TearingLegacyAddressSpace(
    ILegacyAddressSpace inner,
    uint transitionAddress,
    byte[] replacement) : ILegacyAddressSpace
{
    private int matchingReads;

    public bool TryRead(uint virtualAddress, Span<byte> destination)
    {
        if (virtualAddress == transitionAddress && ++matchingReads > 1)
        {
            if (replacement.Length != destination.Length)
            {
                destination.Clear();
                return false;
            }

            replacement.CopyTo(destination);
            return true;
        }

        return inner.TryRead(virtualAddress, destination);
    }
}

sealed class RemappingLegacyAddressSpace(
    ILegacyAddressSpace original,
    ILegacyAddressSpace replacement,
    uint watchedAddress,
    int watchedLength) : ILegacyAddressSpace
{
    private bool remapped;

    public bool TryRead(uint virtualAddress, Span<byte> destination)
    {
        var readEnd = (ulong)virtualAddress + (ulong)destination.Length;
        var watchedEnd = (ulong)watchedAddress + (uint)watchedLength;
        var overlapsWatchedRange = virtualAddress < watchedEnd && readEnd > watchedAddress;
        var source = remapped ? replacement : original;
        var success = source.TryRead(virtualAddress, destination);
        if (overlapsWatchedRange)
        {
            remapped = true;
        }

        return success;
    }
}
