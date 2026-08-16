using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Battle;

internal static class Steam2026BattleObservationTests
{
    private static Steam2026FingerprintResult supportedFingerprint = null!;
    private static Steam2026FingerprintResult unsupportedFingerprint = null!;

    internal static Steam2026BattleTextResolvers Resolvers { get; } = new(
        abilityId => abilityId == 27 ? "Fire" : null,
        abilityId => abilityId == 27 ? "Fire damage" : null,
        itemId => itemId == 7 ? "Phoenix Down" : null,
        itemId => itemId == 7 ? "Restores life" : null,
        commandId => commandId switch
        {
            1 => "Attack",
            2 => "Magic",
            18 => "Change",
            19 => "Defend",
            _ => null
        },
        objectId => objectId == 7 ? "Phoenix Down" : null);

    internal static Steam2026FingerprintResult SupportedFingerprint => supportedFingerprint;

    internal static void ReadsNativeEnemySkillCategoryMapping()
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        fixture.WriteByte(BattleStateReader.AddressMenuWindowStates + 1, 0);
        fixture.WriteByte(
            BattleStateReader.AddressMenuWindowStates + 4,
            BattleStateReader.ActiveWindowState);
        fixture.WriteInt32(BattleStateReader.AddressEnemySkillCursorColumn, 0);
        fixture.WriteInt32(BattleStateReader.AddressEnemySkillCursorRow, 0);
        fixture.WriteInt32(BattleStateReader.AddressEnemySkillScrollRow, 0);
        fixture.WriteByte(BattleStateReader.AddressEnemySkillRecords, 10);
        fixture.WriteByte(
            BattleStateReader.AddressEnemySkillRecords + BattleStateReader.AbilityMpCostOffset,
            8);
        var resolvers = new Steam2026BattleTextResolvers(
            abilityId => abilityId == 0x52 ? "Matra Magic" : null,
            abilityId => abilityId == 0x52 ? "Non-elemental attack on all opponents" : null,
            _ => null,
            _ => null,
            _ => null,
            _ => null);
        var reader = new Steam2026BattleObservationReader(fixture.Direct, resolvers);

        Equal(true, reader.TryReadResearchSnapshot(4, out var snapshot), "x64 Enemy Skill snapshot");
        Equal(0x52, snapshot.Menu.Selection.EntryId, "x64 normalized Enemy Skill action id");
        Equal("Matra Magic", snapshot.Menu.Selection.Name, "x64 native Enemy Skill name");
        Equal(8, snapshot.Menu.Selection.MpCost, "x64 native Enemy Skill MP cost");
    }

    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        supportedFingerprint = supported;
        unsupportedFingerprint = unsupported;
        ReadsEquivalentPointerFreeBattleSnapshots();
        ReadsBattleInventoryObjectRowsAndAvailability();
        ReadsScriptedGuestPartyBattleSnapshots();
        NormalizesBattleFramesWithStrictEnemyPrivacy();
        ReadsCoherentNativeVictorySignal();
        ReadsEquivalentActionResultsAndDamageDomains();
        RejectsUnmappedBattleDomains();
        RejectsTranslatedPageRemappingPerDomain();
        RejectsTornNativeStateIncludingRedactedEnemyFields();
        RejectsStableInvalidActiveActorCollections();
        RejectsInvalidMasksAndIndices();
        PublicConstructionRequiresExactTranslatedResolver();
        KeepsBattleResearchCapabilityNeutral(supportedFingerprint);
    }

    private static void ReadsEquivalentPointerFreeBattleSnapshots()
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        var directReader = new Steam2026BattleObservationReader(fixture.Direct, Resolvers);
        var translatedReader = fixture.CreateTranslatedReader();

        Equal(true, directReader.TryReadResearchSnapshot(1, out var direct), "direct battle research snapshot");
        Equal(true, translatedReader.TryReadResearchSnapshot(1, out var translated), "translated battle research snapshot");
        Equal(direct, translated, "direct and translated battle snapshots match");
        Equal(BattleStateReader.BattleModule, translated.Module, "battle module");
        Equal(12, translated.FormationId, "battle formation");
        Equal(2, translated.LayoutType, "battle layout");
        Equal(0, translated.ReadyActorId, "ready actor");
        Equal("Attack", translated.Menu.Selection!.Name, "native command name");
        Equal(2, translated.Actors.Length, "bounded active actor count");
        Equal("Cloud", translated.Actors[0].Name, "party identity");
        Equal("Grunt", translated.Actors[1].Name, "enemy identity");
        Equal(4, translated.Target!.ActorId, "native selected enemy");

        Type[] outputTypes =
        [
            typeof(Steam2026BattleResearchSnapshot),
            typeof(Steam2026BattleActorResearchSnapshot),
            typeof(Steam2026BattleMenuResearchSnapshot),
            typeof(Steam2026BattleSelectionResearchSnapshot),
            typeof(Steam2026BattleTargetResearchSnapshot),
            typeof(Steam2026BattleActionResearchSnapshot),
            typeof(Steam2026BattleResultsResearchSnapshot),
            typeof(Steam2026BattleRewardResearchSnapshot),
            typeof(Steam2026BattleDamageResearchSnapshot)
        ];
        foreach (var outputType in outputTypes)
        {
            foreach (var property in outputType.GetProperties())
            {
                Equal(
                    false,
                    property.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase),
                    $"{outputType.Name}.{property.Name} is guest-address-free");
                Equal(
                    false,
                    property.PropertyType == typeof(IntPtr) || property.PropertyType == typeof(UIntPtr),
                    $"{outputType.Name}.{property.Name} is host-pointer-free");
                Equal(false, property.CanWrite, $"{outputType.Name}.{property.Name} is immutable");
            }
        }
    }

    internal static void ReadsBattleInventoryObjectRowsAndAvailability()
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        fixture.WriteByte(BattleStateReader.AddressMenuWindowStates + 1, 0);
        fixture.WriteByte(
            BattleStateReader.AddressMenuWindowStates + 5,
            BattleStateReader.ActiveWindowState);
        fixture.WriteInt32(BattleStateReader.AddressItemCursorRow, 0);
        fixture.WriteInt32(BattleStateReader.AddressItemScrollRow, 0);
        fixture.WriteByte(BattleStateReader.AddressBattleItemUseContext, 0);
        fixture.WriteUInt16(BattleStateReader.AddressBattleItems, 128);
        fixture.WriteByte(
            BattleStateReader.AddressBattleItems + BattleStateReader.ItemQuantityOffset,
            1);
        fixture.WriteByte(
            BattleStateReader.AddressBattleItems + BattleStateReader.ItemRestrictionFlagsOffset,
            8);
        var resolvers = new Steam2026BattleTextResolvers(
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            objectId => objectId == 128 ? "Mythril Saber" : null,
            resolveInventoryObjectDescription:
                objectId => objectId == 128 ? "A double-handed sword" : null);
        var reader = new Steam2026BattleObservationReader(fixture.Direct, resolvers);

        Equal(
            true,
            reader.TryReadResearchSnapshot(5, out var snapshot),
            "x64 battle Item reads a gray inventory-object row");
        Equal(128, snapshot.Menu.Selection.EntryId, "x64 battle inventory object id");
        Equal("Mythril Saber", snapshot.Menu.Selection.Name, "x64 battle inventory object name");
        Equal(
            "A double-handed sword",
            snapshot.Menu.Selection.Description,
            "x64 battle inventory object description");
        Equal(false, snapshot.Menu.Selection.IsAvailable, "x64 gray battle inventory row is unavailable");
    }

    internal static void ReadsScriptedGuestPartyBattleSnapshots(
        bool includeTranslatedAddressSpace = true)
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        fixture.ConfigureGuestPartyActor(
            partySlot: 0,
            characterId: 10,
            characterRecordIndex: 4,
            name: "Sephiroth",
            level: 50);
        var directReader = new Steam2026BattleObservationReader(fixture.Direct, Resolvers);

        Equal(
            true,
            directReader.TryReadResearchSnapshot(1, out var direct),
            "direct scripted guest battle snapshot");
        Equal(
            "Sephiroth",
            direct.Actors.Single(actor => !actor.IsEnemy).Name,
            "scripted guest menu owner");
        Equal("Attack", direct.Menu.Selection.Name, "scripted guest command");

        var trackerReader = directReader;
        if (includeTranslatedAddressSpace)
        {
            supportedFingerprint ??= new Steam2026FingerprintResult(
                new RuntimeIdentity(
                    Steam2026Fingerprint.SupportedRuntimeId,
                    @"C:\fixture\FFVII.exe",
                    Steam2026Fingerprint.SupportedSha256,
                    true,
                    string.Empty),
                true,
                "Exact supported fingerprint fixture.");
            var translatedReader = fixture.CreateTranslatedReader();
            Equal(
                true,
                translatedReader.TryReadResearchSnapshot(1, out var translated),
                "translated scripted guest battle snapshot");
            Equal(direct, translated, "scripted guest battle snapshots match");
            trackerReader = translatedReader;
        }

        Equal(
            true,
            trackerReader.TryReadBattleTrackerSnapshot(out var tracker),
            "scripted guest battle tracker snapshot");
        Equal("Sephiroth", tracker.PartyProgress.Single().Name, "scripted guest progress name");
        Equal(50, tracker.PartyProgress.Single().Level, "scripted guest progress level");
    }

    private static void NormalizesBattleFramesWithStrictEnemyPrivacy()
    {
        var reader = BattleObservationFixture.CreatePopulated().CreateTranslatedReader();
        Equal(true, reader.TryReadBattleFrame(7, 1, out var frame), "normalized battle frame");
        Equal(true, frame.IsActive, "normalized battle active");
        Equal(7, frame.Revision, "caller frame revision");
        Equal(0, frame.ReadyActorId, "normalized ready actor");
        Equal(1, frame.CommandId, "normalized command id");
        Equal(-1, frame.AbilityId, "normalized absent ability");
        Equal(-1, frame.ItemId, "normalized absent item");
        Equal(0u, frame.AllyTargetMask, "normalized ally target mask");
        Equal(1u, frame.EnemyTargetMask, "normalized local enemy target mask");

        var party = frame.Actors.Single(actor => !actor.IsEnemy);
        Equal(true, party.IsActive, "party actor active");
        Equal(true, party.IsSensed, "party actor visible");
        Equal(314, party.CurrentHp, "party HP preserved");
        Equal(42, party.CurrentMp, "party MP preserved");

        var enemy = frame.Actors.Single(actor => actor.IsEnemy);
        Equal(4, enemy.ActorId, "enemy identity preserved");
        Equal(true, enemy.IsActive, "enemy activity preserved");
        Equal(false, enemy.IsSensed, "enemy sensed flag preserved");
        Equal(0, enemy.CurrentHp, "unsensed enemy current HP redacted");
        Equal(0, enemy.MaximumHp, "unsensed enemy maximum HP redacted");
        Equal(0, enemy.CurrentMp, "unsensed enemy current MP redacted");
        Equal(0, enemy.MaximumMp, "unsensed enemy maximum MP redacted");
        Equal(0u, enemy.StatusMask, "unsensed enemy status redacted");

        Equal(true, reader.TryReadResearchSnapshot(1, out var research), "coherent public battle research");
        var researchEnemy = research.Actors.Single(actor => actor.IsEnemy);
        Equal(0, researchEnemy.CurrentHp, "public research enemy HP redacted");
        Equal(0u, researchEnemy.StatusMask, "public research enemy status redacted");
    }

    private static void ReadsEquivalentActionResultsAndDamageDomains()
    {
        var battle = BattleObservationFixture.CreatePopulated();
        var directBattle = new Steam2026BattleObservationReader(battle.Direct, Resolvers);
        var translatedBattle = battle.CreateTranslatedReader();
        Equal(true, directBattle.TryReadEnemyActionResearchSnapshot(out var directAction), "direct enemy action");
        Equal(true, translatedBattle.TryReadEnemyActionResearchSnapshot(out var translatedAction), "translated enemy action");
        Equal(directAction, translatedAction, "direct and translated enemy action match");
        Equal("Rifle", translatedAction.ActionName, "native enemy action name");

        Equal(true, directBattle.TryReadDamageResearchSnapshot(out var directDamage), "direct damage popup");
        Equal(true, translatedBattle.TryReadDamageResearchSnapshot(out var translatedDamage), "translated damage popup");
        Equal(directDamage, translatedDamage, "direct and translated damage match");
        Equal(12, translatedDamage.Value, "native damage value");

        var results = BattleObservationFixture.CreatePopulated();
        results.SwitchToResultsModule();
        results.WriteInt32(BattleResultsReader.AddressInputEdges, 0x4000);
        results.WriteInt32(BattleResultsReader.AddressInputRepeat, 0x0002);
        results.WriteInt32(BattleResultsReader.AddressHeldInput, 0x0800);
        var directResults = new Steam2026BattleObservationReader(results.Direct, Resolvers);
        var translatedResults = results.CreateTranslatedReader();
        Equal(true, directResults.TryReadResultsResearchSnapshot(out var directResult), "direct battle results");
        Equal(true, translatedResults.TryReadResultsResearchSnapshot(out var translatedResult), "translated battle results");
        Equal(directResult, translatedResult, "direct and translated results match");
        Equal(125, translatedResult.Experience, "native result experience");
        Equal(true, translatedResult.HasRewardItems, "native result has-items flag");
        Equal(0, translatedResult.RewardSelection, "native result reward cursor");
        Equal((short)0, translatedResult.RewardTransition, "native result settled transition");
        Equal(0x4000, GetResultInput(translatedResult, "InputEdges"), "native result input edge");
        Equal(0x0002, GetResultInput(translatedResult, "InputRepeat"), "native result input repeat");
        Equal(0x0800, GetResultInput(translatedResult, "HeldInput"), "native result held input");
        Equal(1, translatedResult.Rewards.Length, "bounded native reward count");
        Equal("Phoenix Down", translatedResult.Rewards[0].Name, "native reward name");
        Equal(0, translatedResult.Rewards[0].PhysicalSlot, "native physical reward slot");
        Equal(false, translatedResult.Rewards[0].IsSelectedToTake, "native reward disposition");
    }

    private static void ReadsCoherentNativeVictorySignal()
    {
        var fixture = BattleObservationFixture.CreatePopulated();
        fixture.WriteUInt16(BattleStateReader.AddressVictoryOutcome, 1);
        var reader = fixture.CreateTranslatedReader();

        Equal(true, reader.TryReadVictorySignal(out var isVictory), "coherent native victory signal");
        Equal(true, isVictory, "native victory outcome");

        AssertTearRejected(
            fixture,
            (uint)BattleStateReader.AddressVictoryOutcome,
            BitConverter.GetBytes((ushort)0),
            candidate => candidate.TryReadVictorySignal(out _),
            "torn native victory signal");
    }

    private static void RejectsUnmappedBattleDomains()
    {
        var battleCases = new (uint Address, string Label)[]
        {
            ((uint)BattleStateReader.AddressCurrentModule, "unmapped battle module"),
            ((uint)BattleStateReader.AddressBattleFormationId, "unmapped battle state"),
            ((uint)BattleStateReader.AddressBattleActors, "unmapped actor collection"),
            ((uint)BattleStateReader.AddressMenuWindowStates + 1u, "unmapped menu ownership"),
            ((uint)BattleStateReader.AddressTargetMask, "unmapped target ownership")
        };
        foreach (var testCase in battleCases)
        {
            var fixture = BattleObservationFixture.CreatePopulated();
            fixture.UnmapGuestPage(testCase.Address);
            var reader = fixture.CreateTranslatedReader();
            Equal(false, reader.TryReadResearchSnapshot(1, out _), testCase.Label);
            Equal(false, reader.TryReadBattleFrame(1, 1, out _), $"{testCase.Label} normalized");
        }

        var action = BattleObservationFixture.CreatePopulated();
        action.UnmapGuestPage((uint)BattleStateReader.AddressAnimationEventQueue);
        Equal(false, action.CreateTranslatedReader().TryReadEnemyActionResearchSnapshot(out _), "unmapped action domain");

        var results = BattleObservationFixture.CreatePopulated();
        results.SwitchToResultsModule();
        results.UnmapGuestPage((uint)BattleResultsReader.AddressRewardItems);
        Equal(false, results.CreateTranslatedReader().TryReadResultsResearchSnapshot(out _), "unmapped results domain");

        var damage = BattleObservationFixture.CreatePopulated();
        damage.UnmapGuestPage((uint)BattleDamagePopupReader.AddressEffectData);
        Equal(false, damage.CreateTranslatedReader().TryReadDamageResearchSnapshot(out _), "unmapped damage domain");
    }

    private static void RejectsTranslatedPageRemappingPerDomain()
    {
        AssertRemapRejected((uint)BattleStateReader.AddressCurrentModule, r => r.TryReadResearchSnapshot(1, out _), "remapped module domain");
        AssertRemapRejected((uint)BattleStateReader.AddressBattleFormationId, r => r.TryReadResearchSnapshot(1, out _), "remapped battle-state domain");
        AssertRemapRejected((uint)BattleStateReader.AddressBattleActors, r => r.TryReadResearchSnapshot(1, out _), "remapped actor domain");
        AssertRemapRejected((uint)BattleStateReader.AddressRootCommandRecords, r => r.TryReadResearchSnapshot(1, out _), "remapped menu domain");
        AssertRemapRejected((uint)BattleStateReader.AddressTargetMask, r => r.TryReadResearchSnapshot(1, out _), "remapped target domain");
        AssertRemapRejected((uint)BattleStateReader.AddressAnimationEventQueue, r => r.TryReadEnemyActionResearchSnapshot(out _), "remapped action domain");

        var results = BattleObservationFixture.CreatePopulated();
        results.SwitchToResultsModule();
        AssertRemapRejected(results, (uint)BattleResultsReader.AddressRewardItems, r => r.TryReadResultsResearchSnapshot(out _), "remapped results domain");
        AssertRemapRejected((uint)BattleDamagePopupReader.AddressEffectData, r => r.TryReadDamageResearchSnapshot(out _), "remapped damage domain");
    }

    private static void RejectsTornNativeStateIncludingRedactedEnemyFields()
    {
        AssertTearRejected((uint)BattleStateReader.AddressCurrentModule, [1], r => r.TryReadResearchSnapshot(1, out _), "torn battle module");
        AssertTearRejected((uint)BattleStateReader.AddressBattleFormationId, BitConverter.GetBytes((ushort)13), r => r.TryReadResearchSnapshot(1, out _), "torn battle state");
        var enemyHp = (uint)BattleStateReader.AddressBattleActors + 4u * BattleStateReader.BattleActorSize + BattleStateReader.ActorCurrentHpOffset;
        AssertTearRejected(enemyHp, BitConverter.GetBytes(41), r => r.TryReadResearchSnapshot(1, out _), "torn raw enemy HP rejected despite redaction");
        AssertTearRejected((uint)BattleStateReader.AddressRootCommandRecords, [2], r => r.TryReadResearchSnapshot(1, out _), "torn menu selection");
        AssertTearRejected((uint)BattleStateReader.AddressSelectedTarget, [0], r => r.TryReadResearchSnapshot(1, out _), "torn target selection");
        AssertTearRejected(
            (uint)BattleStateReader.AddressAnimationEventQueue + BattleStateReader.AnimationEventActionOffset,
            BitConverter.GetBytes((ushort)3),
            r => r.TryReadEnemyActionResearchSnapshot(out _),
            "torn enemy action");

        var results = BattleObservationFixture.CreatePopulated();
        results.SwitchToResultsModule();
        AssertTearRejected(results, (uint)BattleResultsReader.AddressExperience, BitConverter.GetBytes(126), r => r.TryReadResultsResearchSnapshot(out _), "torn result total");
        AssertTearRejected(results, (uint)BattleResultsReader.AddressInputEdges, BitConverter.GetBytes(0x4000), r => r.TryReadResultsResearchSnapshot(out _), "torn result input edge");
        AssertTearRejected(
            (uint)BattleDamagePopupReader.AddressEffectData + 5u * BattleDamagePopupReader.EffectRecordSize + BattleDamagePopupReader.ValueOffset,
            BitConverter.GetBytes((short)13),
            r => r.TryReadDamageResearchSnapshot(out _),
            "torn damage value");
    }

    private static void RejectsStableInvalidActiveActorCollections()
    {
        var invalidName = BattleObservationFixture.CreatePopulated();
        invalidName.AddEnemy(actorIndex: 5, sceneEnemyIndex: 1, "Sweeper");
        invalidName.ClearEnemyName(sceneEnemyIndex: 1);
        var invalidNameReader = invalidName.CreateTranslatedReader();
        Equal(
            false,
            invalidNameReader.TryReadResearchSnapshot(1, out _),
            "x64 projection rejects one blank-name active enemy among multiple enemies");
        Equal(
            false,
            invalidNameReader.TryReadBattleFrame(1, 1, out _),
            "normalized x64 projection rejects one blank-name active enemy among multiple enemies");

        var invalidHp = BattleObservationFixture.CreatePopulated();
        invalidHp.AddEnemy(actorIndex: 5, sceneEnemyIndex: 1, "Sweeper");
        invalidHp.WriteInt32(
            BattleStateReader.AddressBattleActors + 5 * BattleStateReader.BattleActorSize +
                BattleStateReader.ActorCurrentHpOffset,
            51);
        var invalidHpReader = invalidHp.CreateTranslatedReader();
        Equal(
            false,
            invalidHpReader.TryReadResearchSnapshot(1, out _),
            "x64 projection rejects one impossible-HP active enemy among multiple enemies");
        Equal(
            false,
            invalidHpReader.TryReadBattleFrame(1, 1, out _),
            "normalized x64 projection rejects one impossible-HP active enemy among multiple enemies");
    }

    private static void RejectsInvalidMasksAndIndices()
    {
        var targetMask = BattleObservationFixture.CreatePopulated();
        targetMask.WriteUInt16(BattleStateReader.AddressTargetMask, 1 << 15);
        targetMask.WriteByte(BattleStateReader.AddressSelectedTarget, 15);
        Equal(false, targetMask.CreateTranslatedReader().TryReadResearchSnapshot(1, out _), "invalid target mask");

        var actionMask = BattleObservationFixture.CreatePopulated();
        actionMask.WriteUInt16(BattleStateReader.AddressBattleActionTargetMask, 1 << 15);
        Equal(false, actionMask.CreateTranslatedReader().TryReadEnemyActionResearchSnapshot(out _), "invalid action mask");

        var actionIndex = BattleObservationFixture.CreatePopulated();
        actionIndex.WriteByte(BattleStateReader.AddressAnimationEventIndex, BattleStateReader.AnimationEventCount);
        Equal(false, actionIndex.CreateTranslatedReader().TryReadEnemyActionResearchSnapshot(out _), "invalid action index");

        var results = BattleObservationFixture.CreatePopulated();
        results.SwitchToResultsModule();
        results.WriteUInt16(BattleResultsReader.AddressRewardItems, BattleResultsReader.InventoryObjectCount);
        Equal(false, results.CreateTranslatedReader().TryReadResultsResearchSnapshot(out _), "invalid reward index");

        var effect = BattleObservationFixture.CreatePopulated();
        effect.WriteUInt16(BattleDamagePopupReader.AddressCurrentEffectIndex, BattleDamagePopupReader.EffectCount);
        Equal(false, effect.CreateTranslatedReader().TryReadDamageResearchSnapshot(out _), "invalid effect index");

        var targetActor = BattleObservationFixture.CreatePopulated();
        var record = BattleDamagePopupReader.AddressEffectData + 5 * BattleDamagePopupReader.EffectRecordSize;
        targetActor.WriteInt32(record + BattleDamagePopupReader.TargetActorOffset, 3);
        Equal(false, targetActor.CreateTranslatedReader().TryReadDamageResearchSnapshot(out _), "invalid damage target");

        var reader = BattleObservationFixture.CreatePopulated().CreateTranslatedReader();
        Equal(false, reader.TryReadResearchSnapshot(short.MaxValue, out _), "invalid renderer state");
        Equal(false, reader.TryReadBattleFrame(-1, 1, out _), "negative frame revision");
    }

    private static void PublicConstructionRequiresExactTranslatedResolver()
    {
        var overflowFixture = BattleObservationFixture.CreatePopulated();
        Throws<ArgumentOutOfRangeException>(
            () => _ = new Steam2026BattleObservationReader(
                supportedFingerprint,
                ulong.MaxValue,
                overflowFixture.Native,
                Resolvers),
            "battle reader overflowing module base");

        var unsupportedFixture = BattleObservationFixture.CreatePopulated();
        Throws<ArgumentException>(
            () => _ = new Steam2026BattleObservationReader(
                unsupportedFingerprint,
                BattleObservationFixture.ModuleBase,
                unsupportedFixture.Native,
                Resolvers),
            "battle reader supported fingerprint");

        var fixture = BattleObservationFixture.CreatePopulated();
        fixture.Native.Write(BattleObservationFixture.ModuleBase + TranslatedX86AddressSpace.ResolverRva, [0x90]);
        Throws<InvalidOperationException>(
            () => _ = new Steam2026BattleObservationReader(
                supportedFingerprint,
                BattleObservationFixture.ModuleBase,
                fixture.Native,
                Resolvers),
            "battle reader exact resolver signature");

        var publicConstructors = typeof(Steam2026BattleObservationReader).GetConstructors();
        Equal(1, publicConstructors.Length, "single validated public battle constructor");
        Equal(
            typeof(Steam2026FingerprintResult),
            publicConstructors[0].GetParameters()[0].ParameterType,
            "battle reader public constructor requires fingerprint");
        Equal(
            false,
            publicConstructors.SelectMany(constructor => constructor.GetParameters()).Any(parameter =>
                typeof(ILegacyAddressSpace).IsAssignableFrom(parameter.ParameterType)),
            "public battle construction cannot bypass resolver validation");
    }

    private static void KeepsBattleResearchCapabilityNeutral(Steam2026FingerprintResult supportedFingerprint)
    {
        var readerType = typeof(Steam2026BattleObservationReader);
        Equal(false, typeof(IFf7RuntimeBackend).IsAssignableFrom(readerType), "battle reader is not a backend");
        Equal(false, typeof(IRuntimeEventSink).IsAssignableFrom(readerType), "battle reader is not an event sink");
        Equal(false, readerType.GetMethods().Any(method => method.Name.Contains("Hook", StringComparison.OrdinalIgnoreCase)), "battle reader has no hooks");
        Equal(false, readerType.GetMethods().Any(method => method.Name.Contains("Speak", StringComparison.OrdinalIgnoreCase)), "battle reader has no speech");
        using var backend = new Steam2026X64RuntimeBackend(supportedFingerprint);
        Equal(RuntimeCapability.None, backend.ValidateCapabilities().Available, "battle research enables no capability");
    }

    private static void AssertRemapRejected(
        uint guestAddress,
        Func<Steam2026BattleObservationReader, bool> read,
        string label) =>
        AssertRemapRejected(BattleObservationFixture.CreatePopulated(), guestAddress, read, label);

    private static void AssertRemapRejected(
        BattleObservationFixture fixture,
        uint guestAddress,
        Func<Steam2026BattleObservationReader, bool> read,
        string label)
    {
        var remapping = new RemappingNativeMemoryReader(
            fixture.Native,
            fixture.GetPageTableEntryAddress(guestAddress),
            triggerRead: 2,
            () => fixture.MapGuestPage(guestAddress, 0x0000000700000000));
        var reader = new Steam2026BattleObservationReader(
            supportedFingerprint,
            BattleObservationFixture.ModuleBase,
            remapping,
            Resolvers);
        Equal(false, read(reader), label);
    }

    private static void AssertTearRejected(
        uint guestAddress,
        byte[] replacement,
        Func<Steam2026BattleObservationReader, bool> read,
        string label) =>
        AssertTearRejected(BattleObservationFixture.CreatePopulated(), guestAddress, replacement, read, label);

    private static void AssertTearRejected(
        BattleObservationFixture fixture,
        uint guestAddress,
        byte[] replacement,
        Func<Steam2026BattleObservationReader, bool> read,
        string label)
    {
        var tearing = new TearingNativeMemoryReader(
            fixture.Native,
            fixture.GetHostAddress(guestAddress),
            triggerRead: 2,
            () => fixture.Write(guestAddress, replacement));
        var reader = new Steam2026BattleObservationReader(
            supportedFingerprint,
            BattleObservationFixture.ModuleBase,
            tearing,
            Resolvers);
        Equal(false, read(reader), label);
    }

    private static void Throws<TException>(Action action, string label)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static int GetResultInput(
        Steam2026BattleResultsResearchSnapshot snapshot,
        string propertyName)
    {
        var property = snapshot.GetType().GetProperty(propertyName);
        if (property?.GetValue(snapshot) is not int value)
        {
            throw new InvalidOperationException($"Missing battle-results input property: {propertyName}.");
        }

        return value;
    }
}

internal sealed class BattleObservationFixture
{
    public const ulong ModuleBase = 0x0000000140000000;

    private readonly Dictionary<uint, ulong> hostPages = [];
    private ulong nextHostPage = 0x0000000800000000;

    private BattleObservationFixture()
    {
        Direct = new DirectGuestMemory();
        Native = new FakeNativeMemoryReader();
    }

    public DirectGuestMemory Direct { get; }

    public FakeNativeMemoryReader Native { get; }

    public static BattleObservationFixture CreatePopulated()
    {
        var fixture = new BattleObservationFixture();
        fixture.Native.Write(
            ModuleBase + TranslatedX86AddressSpace.ResolverRva,
            Convert.FromHexString(
                "85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3"));
        fixture.PopulateBattle();
        fixture.PopulateAction();
        fixture.PopulateDamage();
        return fixture;
    }

    public Steam2026BattleObservationReader CreateTranslatedReader() =>
        new(
            Steam2026BattleObservationTests.SupportedFingerprint,
            ModuleBase,
            Native,
            Steam2026BattleObservationTests.Resolvers);

    public void SwitchToResultsModule()
    {
        WriteByte(BattleResultsReader.AddressCurrentModule, BattleResultsReader.ResultsModule);
        WriteInt32(BattleResultsReader.AddressResultsState, 0);
        WriteByte(BattleResultsReader.AddressResultsPageReady, 1);
        WriteInt32(BattleResultsReader.AddressExperience, 125);
        WriteInt32(BattleResultsReader.AddressAp, 8);
        WriteInt32(BattleResultsReader.AddressGil, 96);
        WriteInt32(BattleResultsReader.AddressHasRewardItems, 1);
        WriteInt32(BattleResultsReader.AddressRewardSelection, 0);
        WriteUInt16(BattleResultsReader.AddressRewardTransition, 0);
        WriteInt32(BattleResultsReader.AddressInputEdges, 0);
        WriteInt32(BattleResultsReader.AddressInputRepeat, 0);
        WriteInt32(BattleResultsReader.AddressHeldInput, 0);
        for (var index = 0; index < BattleResultsReader.RewardItemCount; index++)
        {
            var address = BattleResultsReader.AddressRewardItems + index * BattleResultsReader.RewardItemSize;
            WriteUInt16(address, index == 0 ? (ushort)7 : ushort.MaxValue);
            WriteUInt16(address + 2, index == 0 ? (ushort)2 : (ushort)0);
            WriteUInt16(
                address + BattleResultsReader.RewardSelectedOffset,
                0);
        }
    }

    public void AddEnemy(int actorIndex, int sceneEnemyIndex, string name)
    {
        const int firstEnemyActorIndex = 4;
        const int lastEnemyActorIndex = 9;
        if (actorIndex is < firstEnemyActorIndex or > lastEnemyActorIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(actorIndex));
        }

        if (sceneEnemyIndex is < 0 or >= BattleStateReader.EnemySceneRecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneEnemyIndex));
        }

        var enemySlot = actorIndex - firstEnemyActorIndex;
        WriteUInt16(
            BattleStateReader.AddressEnemySceneIndexRecords +
                enemySlot * BattleStateReader.EnemySceneIndexRecordSize,
            sceneEnemyIndex);
        WriteFf7Text(
            (uint)(BattleStateReader.AddressEnemyData +
                sceneEnemyIndex * BattleStateReader.EnemyDataSize),
            name,
            BattleStateReader.EnemyNameLength);
        var actor = BattleStateReader.AddressBattleActors + actorIndex * BattleStateReader.BattleActorSize;
        WriteByte(actor + BattleStateReader.ActorInstanceIdOffset, checked((byte)sceneEnemyIndex));
        if (!Direct.TryReadUInt16((uint)BattleStateReader.AddressActiveEnemyMask, out var activeMask))
        {
            activeMask = 0;
        }
        WriteUInt16(
            BattleStateReader.AddressActiveEnemyMask,
            activeMask | (1 << actorIndex));
        WriteInt32(actor + BattleStateReader.ActorStatusMaskOffset, 0);
        WriteUInt16(actor + BattleStateReader.ActorCurrentMpOffset, 0);
        WriteUInt16(actor + BattleStateReader.ActorMaxMpOffset, 0);
        WriteInt32(actor + BattleStateReader.ActorCurrentHpOffset, 50);
        WriteInt32(actor + BattleStateReader.ActorMaxHpOffset, 50);
    }

    public void ClearEnemyName(int sceneEnemyIndex) =>
        WriteFf7Text(
            (uint)(BattleStateReader.AddressEnemyData +
                sceneEnemyIndex * BattleStateReader.EnemyDataSize),
            string.Empty,
            BattleStateReader.EnemyNameLength);

    public void ConfigureGuestPartyActor(
        int partySlot,
        byte characterId,
        int characterRecordIndex,
        string name,
        byte level)
    {
        if (partySlot is < 0 or >= 3)
        {
            throw new ArgumentOutOfRangeException(nameof(partySlot));
        }

        if (characterId < 9 || characterId == byte.MaxValue ||
            characterRecordIndex is < 0 or >= 9)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }

        for (var recordIndex = 0; recordIndex < 9; recordIndex++)
        {
            WriteByte(
                SavemapPartyReader.AddressSavemap +
                    SavemapPartyReader.CharactersOffset +
                    recordIndex * SavemapPartyReader.CharacterSize,
                checked((byte)recordIndex));
        }

        var recordAddress = SavemapPartyReader.AddressSavemap +
            SavemapPartyReader.CharactersOffset +
            characterRecordIndex * SavemapPartyReader.CharacterSize;
        WriteByte(recordAddress, characterId);
        WriteFf7Text(
            (uint)(recordAddress + SavemapPartyReader.CharacterNameOffset),
            name,
            12);
        WriteByte(recordAddress + SavemapPartyReader.LevelOffset, level);
        WriteByte(
            SavemapPartyReader.AddressSavemap +
                SavemapPartyReader.PartyMembersOffset +
                partySlot,
            characterId);
        WriteByte(
            BattleStateReader.AddressBattleActors +
                partySlot * BattleStateReader.BattleActorSize +
                BattleStateReader.ActorInstanceIdOffset,
            characterId);
    }

    public void WriteByte(int address, byte value) => Write((uint)address, [value]);

    public void WriteUInt16(int address, int value) =>
        Write((uint)address, BitConverter.GetBytes(checked((ushort)value)));

    public void WriteInt32(int address, int value) =>
        Write((uint)address, BitConverter.GetBytes(value));

    public void Write(uint address, IReadOnlyList<byte> values)
    {
        Direct.Write(address, values);
        for (var index = 0; index < values.Count; index++)
        {
            var guestAddress = checked(address + (uint)index);
            var hostAddress = GetOrMapHostAddress(guestAddress);
            Native.Write(hostAddress, [values[index]]);
        }
    }

    public ulong GetHostAddress(uint guestAddress)
    {
        var pageIndex = guestAddress >> 12;
        if (!hostPages.TryGetValue(pageIndex, out var hostPage))
        {
            throw new InvalidOperationException($"Guest page 0x{pageIndex:X5} is not mapped by the battle fixture.");
        }

        return hostPage + (guestAddress & 0xFFF);
    }

    public ulong GetPageTableEntryAddress(uint guestAddress) =>
        ModuleBase + TranslatedX86AddressSpace.PageTableRva + ((guestAddress >> 12) * sizeof(ulong));

    public void MapGuestPage(uint guestAddress, ulong hostPage)
    {
        hostPages[guestAddress >> 12] = hostPage;
        Native.MapVirtualPage(ModuleBase, guestAddress >> 12, hostPage);
    }

    public void UnmapGuestPage(uint guestAddress) => MapGuestPage(guestAddress, 0);

    private ulong GetOrMapHostAddress(uint guestAddress)
    {
        var pageIndex = guestAddress >> 12;
        if (!hostPages.TryGetValue(pageIndex, out var hostPage))
        {
            hostPage = nextHostPage;
            nextHostPage += 0x3000;
            MapGuestPage(guestAddress, hostPage);
        }

        return hostPage + (guestAddress & 0xFFF);
    }

    private void PopulateBattle()
    {
        WriteByte(BattleStateReader.AddressCurrentModule, BattleStateReader.BattleModule);
        WriteByte(BattleStateReader.AddressCurrentActorSlot, 0);
        WriteByte(BattleStateReader.AddressMenuWindowStates + 1, BattleStateReader.ActiveWindowState);
        WriteInt32(BattleStateReader.AddressBattleMenuTextState, 0);

        WriteByte(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset, 0);
        WriteByte(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset + 1, byte.MaxValue);
        WriteByte(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset + 2, byte.MaxValue);
        var cloud = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        WriteFf7Text((uint)(cloud + SavemapPartyReader.CharacterNameOffset), "Cloud", 12);
        WriteByte(cloud + SavemapPartyReader.LevelOffset, 7);

        var party = BattleStateReader.AddressBattleActors;
        WriteByte(party + BattleStateReader.ActorInstanceIdOffset, 0);
        WriteByte(
            party + BattleStateReader.BattleActorSize + BattleStateReader.ActorInstanceIdOffset,
            byte.MaxValue);
        WriteByte(
            party + 2 * BattleStateReader.BattleActorSize + BattleStateReader.ActorInstanceIdOffset,
            byte.MaxValue);
        for (var enemyActorIndex = 4; enemyActorIndex <= 9; enemyActorIndex++)
        {
            WriteByte(
                party + enemyActorIndex * BattleStateReader.BattleActorSize +
                    BattleStateReader.ActorInstanceIdOffset,
                byte.MaxValue);
        }

        WriteInt32(party + BattleStateReader.ActorStatusMaskOffset, 0);
        WriteUInt16(party + BattleStateReader.ActorCurrentMpOffset, 42);
        WriteUInt16(party + BattleStateReader.ActorMaxMpOffset, 54);
        WriteInt32(party + BattleStateReader.ActorCurrentHpOffset, 314);
        WriteInt32(party + BattleStateReader.ActorMaxHpOffset, 350);

        for (var enemySlot = 0; enemySlot < 6; enemySlot++)
        {
            WriteUInt16(
                BattleStateReader.AddressEnemySceneIndexRecords +
                enemySlot * BattleStateReader.EnemySceneIndexRecordSize,
                enemySlot == 0 ? 0 : ushort.MaxValue);
        }

        WriteFf7Text((uint)BattleStateReader.AddressEnemyData, "Grunt", BattleStateReader.EnemyNameLength);
        var enemy = BattleStateReader.AddressBattleActors + 4 * BattleStateReader.BattleActorSize;
        WriteByte(enemy + BattleStateReader.ActorInstanceIdOffset, 0);
        WriteUInt16(BattleStateReader.AddressActiveEnemyMask, 1 << 4);
        WriteInt32(enemy + BattleStateReader.ActorStatusMaskOffset, 1 << 3);
        WriteUInt16(enemy + BattleStateReader.ActorCurrentMpOffset, 12);
        WriteUInt16(enemy + BattleStateReader.ActorMaxMpOffset, 18);
        WriteInt32(enemy + BattleStateReader.ActorCurrentHpOffset, 42);
        WriteInt32(enemy + BattleStateReader.ActorMaxHpOffset, 50);

        WriteUInt16(BattleStateReader.AddressBattleFormationId, 12);
        WriteByte(BattleStateReader.AddressBattleLayoutType, 2);
        WriteInt32(BattleStateReader.AddressRootCommandColumn, 0);
        WriteInt32(BattleStateReader.AddressRootCommandRow, 0);
        WriteByte(BattleStateReader.AddressRootCommandColumnCount, 1);
        WriteByte(BattleStateReader.AddressRootCommandRecords, 1);

        WriteUInt16(BattleStateReader.AddressTargetMask, 1 << 4);
        WriteByte(BattleStateReader.AddressSelectedTarget, 4);
        WriteByte(BattleStateReader.AddressTargetMode, 6);
        WriteByte(BattleStateReader.AddressTargetFlags, 0);
        WriteByte(BattleStateReader.AddressTargetInvalid, 0);
        WriteByte(BattleStateReader.AddressTargetInputBlocked, 0);
        WriteUInt16(BattleStateReader.AddressConfigSettings, 0);
    }

    private void PopulateAction()
    {
        const byte eventIndex = 0;
        WriteByte(BattleStateReader.AddressAnimationEventIndex, eventIndex);
        var eventAddress = BattleStateReader.AddressAnimationEventQueue +
            eventIndex * BattleStateReader.AnimationEventSize;
        Write((uint)eventAddress, new byte[BattleStateReader.AnimationEventSize]);
        WriteByte(eventAddress + BattleStateReader.AnimationEventAttackerOffset, 4);
        WriteByte(eventAddress + BattleStateReader.AnimationEventKindOffset, BattleStateReader.ActionAnimationEventKind);
        WriteByte(eventAddress + BattleStateReader.AnimationEventCommandOffset, BattleStateReader.EnemyActionCommandId);
        WriteUInt16(eventAddress + BattleStateReader.AnimationEventActionOffset, 2);
        for (var index = 0; index < BattleStateReader.SceneAttackCount; index++)
        {
            WriteUInt16(
                BattleStateReader.AddressSceneAttackIds
                + index * BattleStateReader.SceneAttackIdSize,
                ushort.MaxValue);
        }

        WriteUInt16(BattleStateReader.AddressSceneAttackIds + 2 * BattleStateReader.SceneAttackIdSize, 0x011A);
        WriteFf7Text(
            (uint)(BattleStateReader.AddressSceneAttackNames + 2 * BattleStateReader.SceneAttackNameLength),
            "Rifle",
            BattleStateReader.SceneAttackNameLength);
        WriteUInt16(BattleStateReader.AddressBattleActionTargetMask, 1);
    }

    private void PopulateDamage()
    {
        const int effectIndex = 5;
        WriteUInt16(BattleDamagePopupReader.AddressCurrentEffectIndex, effectIndex);
        var record = BattleDamagePopupReader.AddressEffectData +
            effectIndex * BattleDamagePopupReader.EffectRecordSize;
        WriteByte(record + BattleDamagePopupReader.StateOffset, 0);
        WriteUInt16(record + BattleDamagePopupReader.ValueOffset, 12);
        WriteInt32(record + BattleDamagePopupReader.TargetActorOffset, 0);
        WriteInt32(record + BattleDamagePopupReader.FlagsOffset, 0);
    }

    private void WriteFf7Text(uint address, string value, int length)
    {
        var encoded = Enumerable.Repeat((byte)0xFF, length).ToArray();
        var count = Math.Min(value.Length, Math.Max(0, length - 1));
        for (var index = 0; index < count; index++)
        {
            encoded[index] = value[index] == ' '
                ? (byte)0
                : checked((byte)(value[index] - 0x20));
        }

        Write(address, encoded);
    }
}
