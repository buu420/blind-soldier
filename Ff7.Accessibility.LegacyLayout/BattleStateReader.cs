using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class BattleStateReader
{
    public const int BattleModule = 2;
    public const int AddressCurrentModule = FieldPositionReader.AddressCurrentModule;
    public const int AddressVictoryOutcome = 0x009A89C0;
    public const int AddressCurrentActorSlot = 0x00DC3C7C;
    public const int AddressLimitActorSlot = 0x00DC3C80;
    public const int AddressMenuWindowStates = 0x00DC2068;
    public const byte ActiveWindowState = 2;

    public const int AddressTargetMask = 0x00DC3C60;
    public const int AddressTargetFlags = 0x00DC3C84;
    public const int AddressTargetMode = 0x00DC3C90;
    public const int AddressSelectedTarget = 0x00DC3C9C;
    public const int AddressConfigSettings = 0x00DC0E12;
    public const int AddressBattleMenuTextState = 0x0091EF9C;
    public const int AddressTargetInvalid = 0x00DC38D0;
    public const int AddressTargetInputBlocked = 0x00BFB2EC;

    public const int AddressBattleActors = 0x009AB0DC;
    public const int BattleActorSize = 0x68;
    public const int AddressBattleLimitGauges = 0x009A8DC0;
    public const int BattleLimitGaugeRecordSize = 0x34;
    public const int ActorStatusMaskOffset = 0x00;
    public const int ActorFlagsOffset = 0x05;
    public const int ActorInstanceIdOffset = 0x08;
    public const int ActorCurrentMpOffset = 0x28;
    public const int ActorMaxMpOffset = 0x2A;
    public const int ActorCurrentHpOffset = 0x2C;
    public const int ActorMaxHpOffset = 0x30;

    public const int AddressEnemySceneIndexRecords = 0x009A8794;
    public const int EnemySceneIndexRecordSize = 0x10;
    // FUN_005d0690 clears this word before enemy setup and sets bit 4..9
    // only when the corresponding enemy battle actor is instantiated.
    public const int AddressActiveEnemyMask = 0x009AB0BA;
    public const int AddressEnemyData = 0x009A8E9C;
    public const int EnemyDataSize = 0xB8;
    public const int EnemyNameLength = 24;
    public const int EnemyLevelOffset = 0x20;
    public const int EnemyElementIdsOffset = 0x28;
    public const int EnemyElementRatesOffset = 0x30;
    public const int EnemyElementSlotCount = 8;
    public const byte WeaknessElementRate = 0x02;
    public const int AddressPersistentActorRecords = 0x009A8B39;
    public const int PersistentActorRecordSize = 0x44;
    public const byte SensedInformationFlag = 0x40;

    public const int AddressBattleContext = 0x009AB0A0;
    public const int AddressBattleLayoutType = 0x009A8762;
    public const int AddressBattleFormationId = AddressBattleContext + 0x28;
    public const int AddressBattleActionTargetMask = AddressBattleContext + 0x0E;
    public const int AddressAnimationEventQueue = 0x009AAD70;
    public const int AddressAnimationEventIndex = 0x00BF2A38;
    public const int AnimationEventSize = 0x0C;
    public const int AnimationEventCount = 64;
    public const int AnimationEventAttackerOffset = 0x00;
    public const int AnimationEventKindOffset = 0x01;
    public const int AnimationEventCommandOffset = 0x03;
    public const int AnimationEventActionOffset = 0x06;
    public const byte ActionAnimationEventKind = 1;
    public const byte EnemyActionCommandId = 0x20;
    public const int AddressSceneAttackIds = 0x009A9444;
    public const int AddressSceneAttackNames = 0x009A9484;
    public const int SceneAttackCount = 32;
    public const int SceneAttackIdSize = 2;
    public const int SceneAttackNameLength = 32;

    public const int CharacterMenuBlockSize = 0x700;
    public const int CharacterBattleDataSize = 0x440;
    public const int AddressRootCommandColumn = 0x00DC20A0;
    public const int AddressRootCommandRow = 0x00DC20A4;
    public const int AddressRootCommandColumnCount = 0x00DBA4B9;
    public const int AddressRootCommandRecords = 0x00DBA4E4;
    public const int RootCommandRows = 4;
    public const int RootCommandRecordSize = 6;
    public const int AbilityRecordSize = 8;
    public const int AbilityMpCostOffset = 1;
    public const int MagicActionIdBase = 0x00;
    public const int SummonActionIdBase = 0x38;
    public const int EnemySkillActionIdBase = 0x48;
    public const int AddressMagicRecords = 0x00DBA5A0;
    public const int AddressSummonRecords = 0x00DBA760;
    public const int AddressEnemySkillRecords = 0x00DBA7E0;
    public const int AddressMagicCursorColumn = 0x00DC2110;
    public const int AddressMagicCursorRow = 0x00DC2114;
    public const int AddressMagicScrollRow = 0x00DC2124;
    public const int AddressSummonCursorColumn = 0x00DC2148;
    public const int AddressSummonCursorRow = 0x00DC214C;
    public const int AddressSummonScrollRow = 0x00DC215C;
    public const int AddressEnemySkillCursorColumn = 0x00DC2180;
    public const int AddressEnemySkillCursorRow = 0x00DC2184;
    public const int AddressEnemySkillScrollRow = 0x00DC2194;
    public const int AddressItemCursorRow = 0x00DC20DC;
    public const int AddressItemScrollRow = 0x00DC20EC;
    public const int AddressBattleItemUseContext = 0x00DC3C74;
    public const int AddressBattleItems = 0x009AC354;
    public const int ItemRecordSize = 6;
    public const int ItemQuantityOffset = 2;
    public const int ItemRestrictionFlagsOffset = 4;
    public const int AddressLimitRecords = 0x00DBA544;
    public const int AddressLimitCount = 0x00DBA54A;
    public const int AddressLimitCursorRow = 0x00DC21BC;
    public const int LimitRecordCount = AddressLimitCount - AddressLimitRecords;

    private const int PartyActorCount = 3;
    private const int FirstEnemyActorIndex = 4;
    private const int LastEnemyActorIndex = 9;
    private const int BattleMenuStateCount = 32;
    private readonly Func<int, byte> readByte;
    private readonly Func<int, ushort> readUInt16;
    private readonly Func<int, int> readInt32;
    private readonly ILegacyAddressSpace? addressSpace;
    private readonly SavemapPartyReader partyReader;
    private readonly Func<int, int, bool> isReadableMemory;
    private readonly Func<int, string?>? resolveAbilityName;
    private readonly Func<int, string?>? resolveAbilityDescription;
    private readonly Func<int, string?>? resolveItemName;
    private readonly Func<int, string?>? resolveItemDescription;
    private readonly Func<int, string?>? resolveCommandName;
    private readonly Func<int, string?>? resolveLimitName;
    private readonly Func<int, string?>? resolveLimitDescription;

    public BattleStateReader(
        Func<int, byte> readByte,
        Func<int, ushort> readUInt16,
        Func<int, int> readInt32,
        SavemapPartyReader partyReader,
        Func<int, int, bool> isReadableMemory,
        Func<int, string?>? resolveAbilityName = null,
        Func<int, string?>? resolveAbilityDescription = null,
        Func<int, string?>? resolveItemName = null,
        Func<int, string?>? resolveItemDescription = null,
        Func<int, string?>? resolveCommandName = null,
        Func<int, string?>? resolveLimitName = null,
        Func<int, string?>? resolveLimitDescription = null)
    {
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
        this.readUInt16 = readUInt16 ?? throw new ArgumentNullException(nameof(readUInt16));
        this.readInt32 = readInt32 ?? throw new ArgumentNullException(nameof(readInt32));
        this.partyReader = partyReader ?? throw new ArgumentNullException(nameof(partyReader));
        this.isReadableMemory = isReadableMemory ?? throw new ArgumentNullException(nameof(isReadableMemory));
        this.resolveAbilityName = resolveAbilityName;
        this.resolveAbilityDescription = resolveAbilityDescription;
        this.resolveItemName = resolveItemName;
        this.resolveItemDescription = resolveItemDescription;
        this.resolveCommandName = resolveCommandName;
        this.resolveLimitName = resolveLimitName;
        this.resolveLimitDescription = resolveLimitDescription;
    }

    public BattleStateReader(
        ILegacyAddressSpace addressSpace,
        SavemapPartyReader partyReader,
        Func<int, string?>? resolveAbilityName = null,
        Func<int, string?>? resolveAbilityDescription = null,
        Func<int, string?>? resolveItemName = null,
        Func<int, string?>? resolveItemDescription = null,
        Func<int, string?>? resolveCommandName = null,
        Func<int, string?>? resolveLimitName = null,
        Func<int, string?>? resolveLimitDescription = null)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.partyReader = partyReader ?? throw new ArgumentNullException(nameof(partyReader));
        readByte = ReadAddressSpaceByte;
        readUInt16 = ReadAddressSpaceUInt16;
        readInt32 = ReadAddressSpaceInt32;
        isReadableMemory = IsAddressSpaceReadable;
        this.resolveAbilityName = resolveAbilityName;
        this.resolveAbilityDescription = resolveAbilityDescription;
        this.resolveItemName = resolveItemName;
        this.resolveItemDescription = resolveItemDescription;
        this.resolveCommandName = resolveCommandName;
        this.resolveLimitName = resolveLimitName;
        this.resolveLimitDescription = resolveLimitDescription;
    }

    public BattleMenuStateSnapshot ReadMenuState(short rendererState)
    {
        if (addressSpace is null)
        {
            return ReadMenuStateCore(rendererState);
        }

        return TryReadCoherent(
            () => ReadMenuStateCore(rendererState),
            static (left, right) => left == right,
            out var snapshot)
            ? snapshot
            : BattleMenuStateSnapshot.Invalid;
    }

    public bool TryReadVictorySignal(out bool isVictory)
    {
        isVictory = false;
        if (!TryReadBattleLifecycle(out var module, out var outcome))
        {
            return false;
        }

        isVictory = module == BattleModule && outcome != 0;
        return true;
    }

    public bool TryReadBattleQueryActive(out bool isActive)
    {
        isActive = false;
        if (!TryReadBattleLifecycle(out var module, out var outcome))
        {
            return false;
        }

        isActive = module == BattleModule && outcome == 0;
        return true;
    }

    private bool TryReadBattleLifecycle(out byte module, out ushort outcome)
    {
        module = default;
        outcome = default;
        try
        {
            if (addressSpace is not null)
            {
                if (!addressSpace.TryReadByte((uint)AddressCurrentModule, out var moduleBefore) ||
                    !addressSpace.TryReadUInt16((uint)AddressVictoryOutcome, out var outcomeBefore) ||
                    !addressSpace.TryReadByte((uint)AddressCurrentModule, out var moduleAfter) ||
                    !addressSpace.TryReadUInt16((uint)AddressVictoryOutcome, out var outcomeAfter) ||
                    moduleBefore != moduleAfter ||
                    outcomeBefore != outcomeAfter)
                {
                    return false;
                }

                module = moduleBefore;
                outcome = outcomeBefore;
                return true;
            }

            if (!IsReadable(AddressCurrentModule, sizeof(byte)) ||
                !IsReadable(AddressVictoryOutcome, sizeof(ushort)))
            {
                return false;
            }

            var legacyModuleBefore = readByte(AddressCurrentModule);
            var legacyOutcomeBefore = readUInt16(AddressVictoryOutcome);
            var legacyModuleAfter = readByte(AddressCurrentModule);
            var legacyOutcomeAfter = readUInt16(AddressVictoryOutcome);
            if (legacyModuleBefore != legacyModuleAfter ||
                legacyOutcomeBefore != legacyOutcomeAfter)
            {
                return false;
            }

            module = legacyModuleBefore;
            outcome = legacyOutcomeBefore;
            return true;
        }
        catch
        {
            module = default;
            outcome = default;
            return false;
        }
    }

    private BattleMenuStateSnapshot ReadMenuStateCore(short rendererState)
    {
        if (rendererState is < 0 or >= BattleMenuStateCount ||
            !TryReadMenuOwner(rendererState, out var ownerBefore) ||
            ownerBefore.Module != BattleModule ||
            ownerBefore.WindowState != ActiveWindowState ||
            ownerBefore.PartySlot >= PartyActorCount)
        {
            return BattleMenuStateSnapshot.Invalid;
        }

        var partySlot = ownerBefore.PartySlot;
        if (!TryReadActorCore(partySlot, false, out var actorCandidate))
        {
            return BattleMenuStateSnapshot.Invalid;
        }

        var actor = actorCandidate.ToPublicSnapshot();

        BattleMenuSelectionSnapshot? selection = TryReadSelection(
                rendererState,
                partySlot,
                actor.CurrentMp,
                out var nativeSelection)
            ? nativeSelection
            : null;

        if (!TryReadMenuOwner(rendererState, out var ownerAfter) || ownerBefore != ownerAfter)
        {
            return BattleMenuStateSnapshot.Invalid;
        }

        return new BattleMenuStateSnapshot(true, rendererState, partySlot, actor, selection);
    }

    public bool IsRootCommandMenuActive() =>
        TryIsRootCommandMenuActive(out var isActive) && isActive;

    public bool TryIsRootCommandMenuActive(out bool isActive)
    {
        if (addressSpace is null)
        {
            isActive = IsRootCommandMenuActiveCore();
            return true;
        }

        return TryReadCoherent(
            IsRootCommandMenuActiveCore,
            static (left, right) => left == right,
            out isActive);
    }

    private bool IsRootCommandMenuActiveCore() =>
        readByte(AddressCurrentModule) == BattleModule &&
        readByte(AddressCurrentActorSlot) < PartyActorCount &&
        readByte(AddressMenuWindowStates + 1) == ActiveWindowState;

    public BattleEncounterSnapshot ReadEncounter()
    {
        if (addressSpace is null)
        {
            return ReadEncounterCandidateCore().ToPublicSnapshot();
        }

        return TryReadCoherent(
            ReadEncounterCandidateCore,
            RawBattleEncounterEquals,
            out var candidate)
            ? candidate.ToPublicSnapshot()
            : BattleEncounterSnapshot.Invalid;
    }

    private RawBattleEncounterCandidate ReadEncounterCandidateCore()
    {
        if (readByte(AddressCurrentModule) != BattleModule)
        {
            return RawBattleEncounterCandidate.Invalid;
        }

        var formationId = readUInt16(AddressBattleFormationId);
        var layoutType = readByte(AddressBattleLayoutType);
        if (formationId >= 1024 || layoutType > 8)
        {
            return RawBattleEncounterCandidate.Invalid;
        }

        var actorCollection = ReadRawBattleActorsCandidateCore();
        if (!actorCollection.IsValid)
        {
            return RawBattleEncounterCandidate.Invalid;
        }

        var enemies = actorCollection.Actors.Where(actor => actor.IsEnemy).ToArray();
        return enemies.Length == 0
            ? RawBattleEncounterCandidate.Invalid
            : new RawBattleEncounterCandidate(true, formationId, layoutType, enemies);
    }

    public BattleEnemyActionSnapshot ReadCurrentEnemyAction()
    {
        if (addressSpace is null)
        {
            return ReadCurrentEnemyActionCandidateCore().Snapshot;
        }

        return TryReadCoherent(
            ReadCurrentEnemyActionCandidateCore,
            static (left, right) => left == right,
            out var candidate)
            ? candidate.Snapshot
            : BattleEnemyActionSnapshot.Invalid;
    }

    private RawEnemyActionCandidate ReadCurrentEnemyActionCandidateCore()
    {
        if (readByte(AddressCurrentModule) != BattleModule)
        {
            return RawEnemyActionCandidate.Invalid;
        }

        var eventIndex = readByte(AddressAnimationEventIndex);
        if (eventIndex >= AnimationEventCount)
        {
            return RawEnemyActionCandidate.Invalid;
        }

        if (!TryComputeAddress(
                AddressAnimationEventQueue,
                eventIndex,
                AnimationEventSize,
                out var eventAddress))
        {
            return RawEnemyActionCandidate.Invalid;
        }

        if (readByte(eventAddress + AnimationEventKindOffset) != ActionAnimationEventKind)
        {
            return RawEnemyActionCandidate.Invalid;
        }

        var attackerActorIndex = readByte(eventAddress + AnimationEventAttackerOffset);
        if (attackerActorIndex is < FirstEnemyActorIndex or > LastEnemyActorIndex ||
            !TryReadActorCore(attackerActorIndex, true, out var attacker))
        {
            return RawEnemyActionCandidate.Invalid;
        }

        var commandId = readByte(eventAddress + AnimationEventCommandOffset);
        if (commandId != EnemyActionCommandId)
        {
            return RawEnemyActionCandidate.Invalid;
        }

        var sceneAttackIndex = readUInt16(eventAddress + AnimationEventActionOffset);
        if (sceneAttackIndex >= SceneAttackCount)
        {
            return RawEnemyActionCandidate.Invalid;
        }

        if (!TryComputeAddress(
                AddressSceneAttackIds,
                sceneAttackIndex,
                SceneAttackIdSize,
                out var actionIdAddress) ||
            !TryComputeAddress(
                AddressSceneAttackNames,
                sceneAttackIndex,
                SceneAttackNameLength,
                out var actionNameAddress))
        {
            return RawEnemyActionCandidate.Invalid;
        }

        var actionId = readUInt16(actionIdAddress);
        if (actionId == ushort.MaxValue)
        {
            return RawEnemyActionCandidate.Invalid;
        }

        var formationId = readUInt16(AddressBattleFormationId);
        var accessibilityDescription = ResolveAccessibilityActionDescription(formationId, actionId);
        var actionName = ReadFixedText(
            actionNameAddress,
            SceneAttackNameLength);

        const int validActorMask = 0x03F7;
        return new RawEnemyActionCandidate(
            new BattleEnemyActionSnapshot(
                true,
                eventIndex,
                attackerActorIndex,
                commandId,
                sceneAttackIndex,
                actionId,
                readUInt16(AddressBattleActionTargetMask) & validActorMask,
                string.IsNullOrWhiteSpace(actionName) ? null : actionName.Trim(),
                accessibilityDescription),
            attacker);
    }

    public BattleTargetSnapshot ReadTarget()
    {
        if (addressSpace is null)
        {
            return ReadTargetCandidateCore().ToPublicSnapshot();
        }

        return TryReadCoherent(
            ReadTargetCandidateCore,
            static (left, right) => left == right,
            out var candidate)
            ? candidate.ToPublicSnapshot()
            : BattleTargetSnapshot.Invalid;
    }

    private RawBattleTargetCandidate ReadTargetCandidateCore()
    {
        if (readByte(AddressCurrentModule) != BattleModule ||
            readByte(AddressCurrentActorSlot) >= PartyActorCount ||
            readInt32(AddressBattleMenuTextState) != 0)
        {
            return RawBattleTargetCandidate.Invalid;
        }

        var targetMode = readByte(AddressTargetMode);
        var targetMask = readUInt16(AddressTargetMask);
        var selectedTarget = readByte(AddressSelectedTarget);
        const ushort validTargetMask = 0x03F7;
        if (targetMask == 0 ||
            (targetMask & ~validTargetMask) != 0 ||
            selectedTarget >= 16 ||
            (targetMask & (1 << selectedTarget)) == 0)
        {
            return RawBattleTargetCandidate.Invalid;
        }

        var isEnemy = selectedTarget is >= FirstEnemyActorIndex and <= LastEnemyActorIndex;
        if (!isEnemy && selectedTarget >= PartyActorCount)
        {
            return RawBattleTargetCandidate.Invalid;
        }

        if (!TryReadActorCore(selectedTarget, isEnemy, out var actor))
        {
            return RawBattleTargetCandidate.Invalid;
        }

        return new RawBattleTargetCandidate(
            true,
            targetMask,
            selectedTarget,
            targetMode,
            readByte(AddressTargetFlags),
            actor);
    }

    public IReadOnlyList<BattlePartyProgressSnapshot> ReadPartyProgress()
    {
        return TryReadPartyProgress(out var snapshots) ? snapshots : [];
    }

    public bool TryReadPartyProgress(out IReadOnlyList<BattlePartyProgressSnapshot> snapshots)
    {
        if (addressSpace is null)
        {
            snapshots = ReadPartyProgressCore();
            return true;
        }

        return TryReadCoherent(
            ReadPartyProgressCore,
            static (left, right) => left.SequenceEqual(right),
            out snapshots);
    }

    private IReadOnlyList<BattlePartyProgressSnapshot> ReadPartyProgressCore()
    {
        var result = new List<BattlePartyProgressSnapshot>(PartyActorCount);
        for (var partySlot = 0; partySlot < PartyActorCount; partySlot++)
        {
            if (!TryComputeAddress(
                    SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset,
                    partySlot,
                    1,
                    out var partySlotAddress) ||
                !TryReadByte(partySlotAddress, out var partyCharacterId))
            {
                if (addressSpace is not null)
                {
                    throw new LegacyReadFailureException();
                }

                continue;
            }

            if (partyCharacterId == byte.MaxValue)
            {
                continue;
            }

            if (!partyReader.TryReadBattlePartySlot(
                    partySlot,
                    out var member,
                    out var characterRecordIndex))
            {
                if (addressSpace is not null)
                {
                    throw new LegacyReadFailureException();
                }

                continue;
            }

            if (!TryComputeAddress(
                    SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset,
                    characterRecordIndex,
                    SavemapPartyReader.CharacterSize,
                    out var characterBase))
            {
                if (addressSpace is not null)
                {
                    throw new InvalidBattleSnapshotException();
                }

                continue;
            }

            result.Add(new BattlePartyProgressSnapshot(
                partySlot,
                member.CharacterId,
                member.Name,
                readByte(characterBase + SavemapPartyReader.LevelOffset)));
        }

        return result;
    }

    public IReadOnlyList<BattleActorSnapshot> ReadPartyActors()
    {
        return TryReadPartyActors(out var actors) ? actors : [];
    }

    public bool TryReadPartyActors(out IReadOnlyList<BattleActorSnapshot> actors)
    {
        RawBattleActorCollectionCandidate candidate;
        if (addressSpace is null)
        {
            candidate = ReadRawPartyActorsCandidateCore();
        }
        else if (!TryReadCoherent(
            ReadRawPartyActorsCandidateCore,
            RawBattleActorCollectionEquals,
            out candidate))
        {
            actors = [];
            return false;
        }

        if (!candidate.IsValid)
        {
            actors = [];
            return false;
        }

        actors = candidate.Actors.Select(actor => actor.ToPublicSnapshot()).ToArray();
        return true;
    }

    public IReadOnlyList<BattleActorSnapshot> ReadBattleActors()
    {
        return TryReadBattleActors(out var actors) ? actors : [];
    }

    public bool TryReadBattleActors(out IReadOnlyList<BattleActorSnapshot> actors)
    {
        RawBattleActorCollectionCandidate candidate;
        if (addressSpace is null)
        {
            candidate = ReadRawBattleActorsCandidateCore();
        }
        else if (!TryReadCoherent(
            ReadRawBattleActorsCandidateCore,
            RawBattleActorCollectionEquals,
            out candidate))
        {
            actors = [];
            return false;
        }

        if (!candidate.IsValid)
        {
            actors = [];
            return false;
        }

        actors = candidate.Actors.Select(actor => actor.ToPublicSnapshot()).ToArray();
        return true;
    }

    private RawBattleActorCollectionCandidate ReadRawPartyActorsCandidateCore()
    {
        var result = new List<RawBattleActorSnapshot>(PartyActorCount);
        for (var partySlot = 0; partySlot < PartyActorCount; partySlot++)
        {
            var state = ReadActorSlotCore(partySlot, false, out var actor);
            if (state == ActorSlotReadState.Invalid)
            {
                return RawBattleActorCollectionCandidate.Invalid;
            }

            if (state == ActorSlotReadState.Valid)
            {
                result.Add(actor);
            }
        }

        return new RawBattleActorCollectionCandidate(true, result.ToArray());
    }

    private RawBattleActorCollectionCandidate ReadRawBattleActorsCandidateCore()
    {
        var result = new List<RawBattleActorSnapshot>(PartyActorCount + LastEnemyActorIndex - FirstEnemyActorIndex + 1);
        for (var actorIndex = 0; actorIndex <= LastEnemyActorIndex; actorIndex++)
        {
            var isEnemy = actorIndex is >= FirstEnemyActorIndex and <= LastEnemyActorIndex;
            if (actorIndex >= PartyActorCount && !isEnemy)
            {
                continue;
            }

            var state = ReadActorSlotCore(actorIndex, isEnemy, out var actor);
            if (state == ActorSlotReadState.Invalid)
            {
                return RawBattleActorCollectionCandidate.Invalid;
            }

            if (state == ActorSlotReadState.Valid)
            {
                result.Add(actor);
            }
        }

        return new RawBattleActorCollectionCandidate(true, result.ToArray());
    }

    public bool TryReadPartyActor(int partySlot, out BattleActorSnapshot actor)
    {
        if (addressSpace is null)
        {
            actor = default;
            if (partySlot is not (>= 0 and < PartyActorCount) ||
                !TryReadActorCore(partySlot, false, out var legacyActor))
            {
                return false;
            }

            actor = legacyActor.ToPublicSnapshot();
            return true;
        }

        if (!TryReadCoherent(
                () => ReadActorCandidate(partySlot, false),
                static (left, right) => left == right,
                out var candidate) ||
            !candidate.Success)
        {
            actor = default;
            return false;
        }

        actor = candidate.Actor.ToPublicSnapshot();
        return true;
    }

    public bool TryReadPartyStatusMember(
        int partySlot,
        out BattleStatusMemberSnapshot member)
    {
        member = default;
        if (partySlot is < 0 or >= PartyActorCount)
        {
            return false;
        }

        PartyStatusMemberCandidate candidate;
        if (addressSpace is null)
        {
            candidate = ReadPartyStatusMemberCandidate(partySlot);
        }
        else if (!TryReadCoherent(
                     () => ReadPartyStatusMemberCandidate(partySlot),
                     static (left, right) => left == right,
                     out candidate))
        {
            return false;
        }

        if (!candidate.Success ||
            candidate.Module != BattleModule ||
            candidate.CharacterId >= 9 ||
            candidate.ActorInstanceId == byte.MaxValue)
        {
            return false;
        }

        member = new BattleStatusMemberSnapshot(
            candidate.Actor.ToPublicSnapshot(),
            candidate.LimitGauge);
        return true;
    }

    private PartyStatusMemberCandidate ReadPartyStatusMemberCandidate(int partySlot)
    {
        if (!TryComputeAddress(
                SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset,
                partySlot,
                1,
                out var partySlotAddress) ||
            !TryComputeAddress(
                AddressBattleActors,
                partySlot,
                BattleActorSize,
                out var actorBase) ||
            !TryComputeAddress(
                AddressBattleLimitGauges,
                partySlot,
                BattleLimitGaugeRecordSize,
                out var limitGaugeAddress) ||
            !TryReadByte(AddressCurrentModule, out var moduleBefore) ||
            !TryReadByte(partySlotAddress, out var characterBefore) ||
            !TryReadByte(actorBase + ActorInstanceIdOffset, out var actorInstanceBefore) ||
            moduleBefore != BattleModule ||
            characterBefore >= 9 ||
            actorInstanceBefore == byte.MaxValue ||
            actorInstanceBefore != characterBefore ||
            !TryReadActorCore(partySlot, false, out var actor) ||
            !TryReadByte(limitGaugeAddress, out var limitGauge) ||
            !TryReadByte(AddressCurrentModule, out var moduleAfter) ||
            !TryReadByte(partySlotAddress, out var characterAfter) ||
            !TryReadByte(actorBase + ActorInstanceIdOffset, out var actorInstanceAfter) ||
            moduleBefore != moduleAfter ||
            characterBefore != characterAfter ||
            actorInstanceBefore != actorInstanceAfter ||
            actorInstanceAfter != characterAfter)
        {
            return default;
        }

        return new PartyStatusMemberCandidate(
            true,
            moduleBefore,
            characterBefore,
            actorInstanceBefore,
            actor,
            limitGauge);
    }

    public bool TryReadBattleActor(int actorIndex, out BattleActorSnapshot actor)
    {
        if (addressSpace is null)
        {
            actor = default;
            if (!TryReadBattleActorCore(actorIndex, out var legacyActor))
            {
                return false;
            }

            actor = legacyActor.ToPublicSnapshot();
            return true;
        }

        var isEnemy = actorIndex is >= FirstEnemyActorIndex and <= LastEnemyActorIndex;
        if (actorIndex is not (>= 0 and < PartyActorCount) && !isEnemy)
        {
            actor = default;
            return false;
        }

        if (!TryReadCoherent(
                () => ReadActorCandidate(actorIndex, isEnemy),
                static (left, right) => left == right,
                out var candidate) ||
            !candidate.Success)
        {
            actor = default;
            return false;
        }

        actor = candidate.Actor.ToPublicSnapshot();
        return true;
    }

    public bool TryReadSenseResult(
        int actorIndex,
        out BattleSenseResultSnapshot snapshot)
    {
        snapshot = BattleSenseResultSnapshot.Invalid;
        if (actorIndex is < FirstEnemyActorIndex or > LastEnemyActorIndex)
        {
            return false;
        }

        RawBattleSenseCandidate candidate;
        if (addressSpace is null)
        {
            candidate = ReadSenseCandidate(actorIndex);
        }
        else if (!TryReadCoherent(
                     () => ReadSenseCandidate(actorIndex),
                     RawBattleSenseEquals,
                     out candidate))
        {
            return false;
        }

        if (!candidate.IsValid)
        {
            return false;
        }

        snapshot = candidate.ToPublicSnapshot();
        return true;
    }

    private RawBattleSenseCandidate ReadSenseCandidate(int actorIndex)
    {
        if (readByte(AddressCurrentModule) != BattleModule ||
            !TryReadActorCore(actorIndex, true, out var actor))
        {
            return RawBattleSenseCandidate.Invalid;
        }

        var enemySlot = actorIndex - FirstEnemyActorIndex;
        if (!TryComputeAddress(
                AddressEnemySceneIndexRecords,
                enemySlot,
                EnemySceneIndexRecordSize,
                out var sceneIndexAddress))
        {
            return RawBattleSenseCandidate.Invalid;
        }

        var sceneIndex = readByte(sceneIndexAddress);
        if (sceneIndex >= 6 ||
            !TryComputeAddress(AddressEnemyData, sceneIndex, EnemyDataSize, out var enemyRecord))
        {
            return RawBattleSenseCandidate.Invalid;
        }

        var level = readByte(enemyRecord + EnemyLevelOffset);
        if (level == 0)
        {
            return RawBattleSenseCandidate.Invalid;
        }

        var weaknessElementIds = new List<int>(EnemyElementSlotCount);
        for (var index = 0; index < EnemyElementSlotCount; index++)
        {
            var elementId = readByte(enemyRecord + EnemyElementIdsOffset + index);
            var rate = readByte(enemyRecord + EnemyElementRatesOffset + index);
            if (rate != WeaknessElementRate)
            {
                continue;
            }

            if (elementId == byte.MaxValue || weaknessElementIds.Contains(elementId))
            {
                return RawBattleSenseCandidate.Invalid;
            }

            weaknessElementIds.Add(elementId);
        }

        return new RawBattleSenseCandidate(
            true,
            actor,
            level,
            weaknessElementIds.ToArray());
    }

    internal bool TryReadVisibleActorCorrelation(
        int actorIndex,
        out BattleActorVisibleCorrelation correlation)
    {
        correlation = default;
        var isEnemy = actorIndex is >= FirstEnemyActorIndex and <= LastEnemyActorIndex;
        if (actorIndex is not (>= 0 and < PartyActorCount) && !isEnemy)
        {
            return false;
        }

        VisibleActorCorrelationCandidate candidate;
        if (addressSpace is null)
        {
            candidate = ReadVisibleActorCorrelationCandidate(actorIndex, isEnemy);
        }
        else if (!TryReadCoherent(
            () => ReadVisibleActorCorrelationCandidate(actorIndex, isEnemy),
            static (left, right) => left == right,
            out candidate))
        {
            return false;
        }

        if (candidate.Module != BattleModule || !candidate.Actor.Success)
        {
            return false;
        }

        correlation = new BattleActorVisibleCorrelation(
            candidate.Actor.Actor.ActorIndex,
            candidate.Actor.Actor.Name,
            candidate.Actor.Actor.IsEnemy,
            (candidate.Actor.Actor.StatusMask & 1u) != 0);
        return true;
    }

    private VisibleActorCorrelationCandidate ReadVisibleActorCorrelationCandidate(
        int actorIndex,
        bool isEnemy) =>
        new(readByte(AddressCurrentModule), ReadActorCandidate(actorIndex, isEnemy));

    private bool TryReadBattleActorCore(int actorIndex, out RawBattleActorSnapshot actor)
    {
        actor = default;
        if (actorIndex is >= 0 and < PartyActorCount)
        {
            return TryReadActorCore(actorIndex, false, out actor);
        }

        return actorIndex is >= FirstEnemyActorIndex and <= LastEnemyActorIndex &&
            TryReadActorCore(actorIndex, true, out actor);
    }

    private ActorSlotReadState ReadActorSlotCore(
        int actorIndex,
        bool isEnemy,
        out RawBattleActorSnapshot actor)
    {
        actor = default;
        if (!TryComputeAddress(AddressBattleActors, actorIndex, BattleActorSize, out var actorBase))
        {
            return ActorSlotReadState.Invalid;
        }

        // The scene-index records are formation setup data and can retain entries
        // for slots the current encounter did not instantiate. The battle actor
        // identity byte is the native live-slot sentinel used by the engine.
        var actorInstanceId = readByte(actorBase + ActorInstanceIdOffset);
        if (actorInstanceId == byte.MaxValue)
        {
            return ActorSlotReadState.Inactive;
        }

        if (isEnemy)
        {
            var activeEnemyMask = readUInt16(AddressActiveEnemyMask);
            if ((activeEnemyMask & (1 << actorIndex)) == 0)
            {
                return ActorSlotReadState.Inactive;
            }

            var enemySlot = actorIndex - FirstEnemyActorIndex;
            if (!TryComputeAddress(
                    AddressEnemySceneIndexRecords,
                    enemySlot,
                    EnemySceneIndexRecordSize,
                    out var sceneIndexAddress))
            {
                return ActorSlotReadState.Invalid;
            }

            var sceneEnemyIndex = readByte(sceneIndexAddress);
            if (sceneEnemyIndex == byte.MaxValue)
            {
                return ActorSlotReadState.Inactive;
            }

            if (sceneEnemyIndex >= 6)
            {
                return ActorSlotReadState.Invalid;
            }
        }
        else
        {
            if (!TryComputeAddress(
                    SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset,
                    actorIndex,
                    1,
                    out var partySlotAddress))
            {
                return ActorSlotReadState.Invalid;
            }

            var characterId = readByte(partySlotAddress);
            if (characterId == byte.MaxValue)
            {
                return ActorSlotReadState.Inactive;
            }

            if (characterId != actorInstanceId)
            {
                return ActorSlotReadState.Invalid;
            }
        }

        return TryReadActorCore(actorIndex, isEnemy, out actor)
            ? ActorSlotReadState.Valid
            : ActorSlotReadState.Invalid;
    }

    private ActorCandidate ReadActorCandidate(int actorIndex, bool isEnemy) =>
        TryReadActorCore(actorIndex, isEnemy, out var actor)
            ? new ActorCandidate(true, actor)
            : new ActorCandidate(false, default);

    private bool TryReadActorCore(int actorIndex, bool isEnemy, out RawBattleActorSnapshot actor)
    {
        actor = default;
        if (!TryComputeAddress(AddressBattleActors, actorIndex, BattleActorSize, out var actorBase))
        {
            return false;
        }

        var actorInstanceId = readByte(actorBase + ActorInstanceIdOffset);
        if (actorInstanceId == byte.MaxValue)
        {
            return false;
        }

        string? name;
        if (isEnemy)
        {
            var enemySlot = actorIndex - FirstEnemyActorIndex;
            if (!TryComputeAddress(
                    AddressEnemySceneIndexRecords,
                    enemySlot,
                    EnemySceneIndexRecordSize,
                    out var sceneIndexAddress))
            {
                return false;
            }

            var sceneEnemyIndex = readByte(sceneIndexAddress);
            if (sceneEnemyIndex >= 6)
            {
                return false;
            }

            if (!TryComputeAddress(
                    AddressEnemyData,
                    sceneEnemyIndex,
                    EnemyDataSize,
                    out var enemyDataAddress))
            {
                return false;
            }

            name = ReadFixedText(enemyDataAddress, EnemyNameLength);
        }
        else
        {
            name = partyReader.TryReadBattlePartySlot(
                actorIndex,
                actorInstanceId,
                out var member)
                ? member.Name
                : null;
            if (addressSpace is not null && string.IsNullOrWhiteSpace(name))
            {
                throw new LegacyReadFailureException();
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        int currentHp;
        int maxHp;
        ushort currentMp;
        ushort maxMp;
        uint statusMask;
        if (addressSpace is not null)
        {
            Span<byte> actorHp = stackalloc byte[sizeof(int) * 2];
            Span<byte> actorMp = stackalloc byte[sizeof(ushort) * 2];
            Span<byte> actorStatus = stackalloc byte[sizeof(uint)];
            if (!addressSpace.TryRead(
                    (uint)(actorBase + ActorCurrentHpOffset),
                    actorHp)
                || !addressSpace.TryRead(
                    (uint)(actorBase + ActorCurrentMpOffset),
                    actorMp)
                || !addressSpace.TryRead(
                    (uint)(actorBase + ActorStatusMaskOffset),
                    actorStatus))
            {
                throw new LegacyReadFailureException();
            }

            currentHp = BinaryPrimitives.ReadInt32LittleEndian(
                actorHp.Slice(0, sizeof(int)));
            maxHp = BinaryPrimitives.ReadInt32LittleEndian(
                actorHp.Slice(
                    ActorMaxHpOffset - ActorCurrentHpOffset,
                    sizeof(int)));
            currentMp = BinaryPrimitives.ReadUInt16LittleEndian(
                actorMp.Slice(0, sizeof(ushort)));
            maxMp = BinaryPrimitives.ReadUInt16LittleEndian(
                actorMp.Slice(
                    ActorMaxMpOffset - ActorCurrentMpOffset,
                    sizeof(ushort)));
            statusMask = BinaryPrimitives.ReadUInt32LittleEndian(
                actorStatus);
        }
        else
        {
            currentHp = readInt32(actorBase + ActorCurrentHpOffset);
            maxHp = readInt32(actorBase + ActorMaxHpOffset);
            currentMp = readUInt16(actorBase + ActorCurrentMpOffset);
            maxMp = readUInt16(actorBase + ActorMaxMpOffset);
            statusMask = unchecked((uint)readInt32(actorBase + ActorStatusMaskOffset));
        }
        if (currentHp < 0 || maxHp <= 0 || currentHp > maxHp)
        {
            return false;
        }

        var informationVisible = !isEnemy;
        if (isEnemy)
        {
            if (!TryComputeAddress(
                    AddressPersistentActorRecords,
                    actorIndex,
                    PersistentActorRecordSize,
                    out var persistentActorAddress))
            {
                return false;
            }

            informationVisible = addressSpace is null
                ? (readByte(persistentActorAddress) & SensedInformationFlag) != 0
                : addressSpace.TryReadByte((uint)persistentActorAddress, out var informationFlags) &&
                    (informationFlags & SensedInformationFlag) != 0;
        }

        actor = new RawBattleActorSnapshot(
            actorIndex,
            name,
            isEnemy,
            currentHp,
            maxHp,
            currentMp,
            maxMp,
            informationVisible,
            statusMask);
        return true;
    }

    internal static string? ResolveAccessibilityActionDescription(int formationId, int actionId) =>
        (formationId, actionId) switch
        {
            (324, 0x011F) => "Guard Scorpion raises its tail.",
            (324, 0x0120) => "Guard Scorpion lowers its tail.",
            _ => null
        };

    private bool TryReadSelection(
        short rendererState,
        int partySlot,
        int currentMp,
        out BattleMenuSelectionSnapshot selection)
    {
        selection = default;
        if (rendererState == 5)
        {
            return TryReadItemSelection(partySlot, out selection);
        }

        return rendererState switch
        {
            1 => TryReadRootCommandSelection(partySlot, out selection),
            2 => TryReadSideCommandSelection(18, out selection),
            3 => TryReadSideCommandSelection(19, out selection),
            4 => TryReadAbilitySelection(
                partySlot,
                AddressEnemySkillCursorColumn,
                AddressEnemySkillCursorRow,
                AddressEnemySkillScrollRow,
                2,
                AddressEnemySkillRecords,
                12,
                EnemySkillActionIdBase,
                currentMp,
                out selection),
            6 => TryReadAbilitySelection(
                partySlot,
                AddressMagicCursorColumn,
                AddressMagicCursorRow,
                AddressMagicScrollRow,
                3,
                AddressMagicRecords,
                54,
                MagicActionIdBase,
                currentMp,
                out selection),
            7 => TryReadAbilitySelection(
                partySlot,
                AddressSummonCursorColumn,
                AddressSummonCursorRow,
                AddressSummonScrollRow,
                1,
                AddressSummonRecords,
                16,
                SummonActionIdBase,
                currentMp,
                out selection),
            0x18 => TryReadLimitSelection(partySlot, out selection),
            _ => false
        };
    }

    private bool TryReadLimitSelection(int partySlot, out BattleMenuSelectionSnapshot selection)
    {
        selection = default;
        if (resolveLimitName is null)
        {
            return false;
        }

        if (!TryComputeAddress(
                AddressLimitCount,
                partySlot,
                CharacterBattleDataSize,
                out var countAddress) ||
            !TryComputeAddress(
                AddressLimitCursorRow,
                partySlot,
                CharacterMenuBlockSize,
                out var cursorAddress) ||
            !TryComputeAddress(
                AddressLimitRecords,
                partySlot,
                CharacterBattleDataSize,
                out var recordsAddress))
        {
            return false;
        }

        var count = readByte(countAddress);
        var cursorRow = readInt32(cursorAddress);
        if (count is < 1 or > LimitRecordCount || cursorRow < 0 || cursorRow >= count)
        {
            return false;
        }

        if (!TryComputeAddress(recordsAddress, cursorRow, 1, out var abilityAddress))
        {
            return false;
        }

        var abilityId = readByte(abilityAddress);
        if (abilityId == 0xFF)
        {
            return false;
        }

        var name = resolveLimitName(abilityId);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        selection = new BattleMenuSelectionSnapshot(
            abilityId,
            name,
            resolveLimitDescription?.Invoke(abilityId),
            null,
            null);
        return true;
    }

    private bool TryReadRootCommandSelection(int partySlot, out BattleMenuSelectionSnapshot selection)
    {
        selection = default;
        if (resolveCommandName is null)
        {
            return false;
        }

        if (!TryComputeAddress(
                AddressRootCommandColumn,
                partySlot,
                CharacterMenuBlockSize,
                out var columnAddress) ||
            !TryComputeAddress(
                AddressRootCommandRow,
                partySlot,
                CharacterMenuBlockSize,
                out var rowAddress) ||
            !TryComputeAddress(
                AddressRootCommandColumnCount,
                partySlot,
                CharacterBattleDataSize,
                out var columnCountAddress))
        {
            return false;
        }

        var column = readInt32(columnAddress);
        var row = readInt32(rowAddress);
        var columnCount = readByte(columnCountAddress);
        if (column < 0 || row is < 0 or >= RootCommandRows ||
            columnCount is < 1 or > 2 || column >= columnCount)
        {
            return false;
        }

        var selectedIndex = column * RootCommandRows + row;
        if (!TryComputeAddress(
                AddressRootCommandRecords,
                partySlot,
                CharacterBattleDataSize,
                selectedIndex,
                RootCommandRecordSize,
                out var commandAddress))
        {
            return false;
        }

        var commandId = readByte(commandAddress);
        if (commandId == 0xFF)
        {
            return false;
        }

        var name = resolveCommandName(commandId);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        selection = new BattleMenuSelectionSnapshot(commandId, name, null, null, null);
        return true;
    }

    private bool TryReadSideCommandSelection(int commandId, out BattleMenuSelectionSnapshot selection)
    {
        selection = default;
        if (resolveCommandName is null)
        {
            return false;
        }

        var name = resolveCommandName(commandId);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        selection = new BattleMenuSelectionSnapshot(commandId, name, null, null, null);
        return true;
    }

    private bool TryReadAbilitySelection(
        int partySlot,
        int columnAddress,
        int rowAddress,
        int scrollAddress,
        int columns,
        int recordsAddress,
        int recordCount,
        int actionIdBase,
        int expectedCurrentMp,
        out BattleMenuSelectionSnapshot selection)
    {
        selection = default;
        if (resolveAbilityName is null)
        {
            return false;
        }

        // Current MP ties the ability record to the actor snapshot; it must not hide visible native rows.
        if (!TryReadAbilitySelectionState(
                partySlot,
                columnAddress,
                rowAddress,
                scrollAddress,
                columns,
                recordsAddress,
                recordCount,
                out var stateBefore) ||
            stateBefore.CurrentMp != expectedCurrentMp ||
            stateBefore.AbilityId == 0xFF)
        {
            return false;
        }

        // FUN_0041963c resolves submenu-local action ids through the native
        // category bases: Magic 0x00, Summon 0x38, and Enemy Skill 0x48.
        // Reading the local byte as a KERNEL2 index turns Matra Magic (10)
        // into the ordinary spell Toad, so normalize it before text lookup.
        var abilityId = stateBefore.AbilityId + actionIdBase;
        if (actionIdBase < 0 || abilityId >= 0xE0)
        {
            return false;
        }

        var name = resolveAbilityName(abilityId);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var description = resolveAbilityDescription?.Invoke(abilityId);
        if (!TryReadAbilitySelectionState(
                partySlot,
                columnAddress,
                rowAddress,
                scrollAddress,
                columns,
                recordsAddress,
                recordCount,
                out var stateAfter) ||
            stateBefore != stateAfter)
        {
            return false;
        }

        selection = new BattleMenuSelectionSnapshot(
            abilityId,
            name,
            description,
            null,
            stateBefore.RequiredMp);
        return true;
    }

    private bool TryReadAbilitySelectionState(
        int partySlot,
        int columnAddress,
        int rowAddress,
        int scrollAddress,
        int columns,
        int recordsAddress,
        int recordCount,
        out AbilitySelectionState state)
    {
        state = default;
        if (partySlot is < 0 or >= PartyActorCount || columns <= 0 || recordCount <= 0 ||
            !TryComputeAddress(columnAddress, partySlot, CharacterMenuBlockSize, out var partyColumnAddress) ||
            !TryComputeAddress(rowAddress, partySlot, CharacterMenuBlockSize, out var partyRowAddress) ||
            !TryComputeAddress(scrollAddress, partySlot, CharacterMenuBlockSize, out var partyScrollAddress) ||
            !TryReadInt32(partyColumnAddress, out var column) ||
            !TryReadInt32(partyRowAddress, out var row) ||
            !TryReadInt32(partyScrollAddress, out var scrollRow) ||
            column is < 0 || column >= columns || row < 0 || scrollRow < 0)
        {
            return false;
        }

        var selectedIndex = (long)column + (long)row * columns + (long)scrollRow * columns;
        if (selectedIndex is < 0 || selectedIndex >= recordCount ||
            !TryComputeAddress(
                recordsAddress,
                partySlot,
                CharacterBattleDataSize,
                (int)selectedIndex,
                AbilityRecordSize,
                out var recordAddress) ||
            !TryComputeAddress(
                AddressBattleActors,
                partySlot,
                BattleActorSize,
                ActorCurrentMpOffset,
                out var currentMpAddress) ||
            !TryReadUInt16(currentMpAddress, out var currentMp) ||
            !TryReadAbilityRecord(recordAddress, out var abilityId, out var requiredMp))
        {
            return false;
        }

        state = new AbilitySelectionState(
            column,
            row,
            scrollRow,
            currentMp,
            abilityId,
            requiredMp);
        return true;
    }

    private bool TryReadMenuOwner(short rendererState, out BattleMenuOwner owner)
    {
        owner = default;
        var actorSlotAddress = rendererState == 0x18
            ? AddressLimitActorSlot
            : AddressCurrentActorSlot;
        if (!TryComputeAddress(AddressMenuWindowStates, rendererState, 1, out var windowAddress) ||
            !TryReadByte(AddressCurrentModule, out var module) ||
            !TryReadByte(windowAddress, out var windowState) ||
            !TryReadByte(actorSlotAddress, out var partySlot))
        {
            return false;
        }

        owner = new BattleMenuOwner(module, windowState, partySlot);
        return true;
    }

    private bool TryReadByte(int address, out byte value)
    {
        value = default;
        if (addressSpace is not null)
        {
            if (address <= 0 || !addressSpace.TryReadByte((uint)address, out value))
            {
                throw new LegacyReadFailureException();
            }

            return true;
        }

        if (!IsReadable(address, 1))
        {
            return false;
        }

        value = readByte(address);
        return true;
    }

    private bool TryReadUInt16(int address, out ushort value)
    {
        value = default;
        if (addressSpace is not null)
        {
            if (address <= 0 || !addressSpace.TryReadUInt16((uint)address, out value))
            {
                throw new LegacyReadFailureException();
            }

            return true;
        }

        if (!IsReadable(address, sizeof(ushort)))
        {
            return false;
        }

        value = readUInt16(address);
        return true;
    }

    private bool TryReadInt32(int address, out int value)
    {
        value = default;
        if (addressSpace is not null)
        {
            if (address <= 0 || !addressSpace.TryReadInt32((uint)address, out value))
            {
                throw new LegacyReadFailureException();
            }

            return true;
        }

        if (!IsReadable(address, sizeof(int)))
        {
            return false;
        }

        value = readInt32(address);
        return true;
    }

    private bool TryReadAbilityRecord(int address, out byte abilityId, out byte requiredMp)
    {
        abilityId = default;
        requiredMp = default;
        if (!IsReadable(address, AbilityMpCostOffset + 1))
        {
            return false;
        }

        abilityId = readByte(address);
        requiredMp = readByte(address + AbilityMpCostOffset);
        return true;
    }

    private bool IsReadable(int address, int length)
    {
        if (address <= 0 || length <= 0 || (long)address + length > (long)int.MaxValue + 1)
        {
            return false;
        }

        try
        {
            if (addressSpace is not null)
            {
                if (!IsAddressSpaceReadable(address, length))
                {
                    throw new LegacyReadFailureException();
                }

                return true;
            }

            return isReadableMemory(address, length);
        }
        catch (LegacyReadFailureException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryComputeAddress(int baseAddress, int index, int stride, out int address)
    {
        address = default;
        if (baseAddress <= 0 || index < 0 || stride < 0)
        {
            return false;
        }

        try
        {
            var candidate = checked(
                (ulong)(uint)baseAddress +
                (ulong)(uint)index * (uint)stride);
            if (candidate is 0 or > int.MaxValue)
            {
                return false;
            }

            address = (int)candidate;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private byte ReadAddressSpaceByte(int address)
    {
        if (address <= 0 || !addressSpace!.TryReadByte((uint)address, out var value))
        {
            throw new LegacyReadFailureException();
        }

        return value;
    }

    private ushort ReadAddressSpaceUInt16(int address)
    {
        if (address <= 0 || !addressSpace!.TryReadUInt16((uint)address, out var value))
        {
            throw new LegacyReadFailureException();
        }

        return value;
    }

    private int ReadAddressSpaceInt32(int address)
    {
        if (address <= 0 || !addressSpace!.TryReadInt32((uint)address, out var value))
        {
            throw new LegacyReadFailureException();
        }

        return value;
    }

    private bool IsAddressSpaceReadable(int address, int length)
    {
        if (address <= 0 || length <= 0 || length > 4096)
        {
            return false;
        }

        var endExclusive = (ulong)(uint)address + (uint)length;
        if (endExclusive > (ulong)uint.MaxValue + 1)
        {
            return false;
        }

        return addressSpace!.TryRead((uint)address, new byte[length]);
    }

    private bool TryReadCoherent<T>(
        Func<T> readSnapshot,
        Func<T, T, bool> equals,
        out T snapshot)
    {
        snapshot = default!;
        try
        {
            var candidate = readSnapshot();
            var bookend = readSnapshot();
            if (!equals(candidate, bookend))
            {
                return false;
            }

            snapshot = candidate;
            return true;
        }
        catch (LegacyReadFailureException)
        {
            return false;
        }
        catch (InvalidBattleSnapshotException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool RawBattleEncounterEquals(
        RawBattleEncounterCandidate left,
        RawBattleEncounterCandidate right) =>
        left.IsValid == right.IsValid &&
        left.FormationId == right.FormationId &&
        left.LayoutType == right.LayoutType &&
        left.Enemies.SequenceEqual(right.Enemies);

    private static bool RawBattleActorCollectionEquals(
        RawBattleActorCollectionCandidate left,
        RawBattleActorCollectionCandidate right) =>
        left.IsValid == right.IsValid &&
        left.Actors.SequenceEqual(right.Actors);

    private static bool RawBattleSenseEquals(
        RawBattleSenseCandidate left,
        RawBattleSenseCandidate right) =>
        left.IsValid == right.IsValid &&
        left.Actor == right.Actor &&
        left.Level == right.Level &&
        left.WeaknessElementIds.SequenceEqual(right.WeaknessElementIds);

    private static bool TryComputeAddress(
        int baseAddress,
        int index,
        int stride,
        int offset,
        out int address)
    {
        address = default;
        if (baseAddress <= 0 || index < 0 || stride < 0 || offset < 0)
        {
            return false;
        }

        try
        {
            var candidate = checked(
                (ulong)(uint)baseAddress +
                (ulong)(uint)index * (uint)stride +
                (uint)offset);
            if (candidate is 0 or > int.MaxValue)
            {
                return false;
            }

            address = (int)candidate;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryComputeAddress(
        int baseAddress,
        int outerIndex,
        int outerStride,
        int innerIndex,
        int innerStride,
        out int address)
    {
        address = default;
        if (baseAddress <= 0 ||
            outerIndex < 0 ||
            outerStride < 0 ||
            innerIndex < 0 ||
            innerStride < 0)
        {
            return false;
        }

        try
        {
            var candidate = checked(
                (ulong)(uint)baseAddress +
                (ulong)(uint)outerIndex * (uint)outerStride +
                (ulong)(uint)innerIndex * (uint)innerStride);
            if (candidate is 0 or > int.MaxValue)
            {
                return false;
            }

            address = (int)candidate;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private readonly record struct BattleMenuOwner(byte Module, byte WindowState, byte PartySlot);

    private readonly record struct AbilitySelectionState(
        int Column,
        int Row,
        int ScrollRow,
        ushort CurrentMp,
        byte AbilityId,
        byte RequiredMp);

    private readonly record struct ActorCandidate(bool Success, RawBattleActorSnapshot Actor);

    private readonly record struct PartyStatusMemberCandidate(
        bool Success,
        byte Module,
        byte CharacterId,
        byte ActorInstanceId,
        RawBattleActorSnapshot Actor,
        byte LimitGauge);

    private enum ActorSlotReadState
    {
        Inactive,
        Valid,
        Invalid
    }

    private readonly record struct VisibleActorCorrelationCandidate(
        byte Module,
        ActorCandidate Actor);

    private readonly record struct RawBattleActorSnapshot(
        int ActorIndex,
        string Name,
        bool IsEnemy,
        int CurrentHp,
        int MaxHp,
        int CurrentMp,
        int MaxMp,
        bool InformationVisible,
        uint StatusMask)
    {
        public BattleActorSnapshot ToPublicSnapshot() => new(
            ActorIndex,
            Name,
            IsEnemy,
            CurrentHp,
            MaxHp,
            CurrentMp,
            MaxMp,
            InformationVisible,
            StatusMask);
    }

    private readonly record struct RawBattleSenseCandidate(
        bool IsValid,
        RawBattleActorSnapshot Actor,
        int Level,
        int[] WeaknessElementIds)
    {
        public static RawBattleSenseCandidate Invalid { get; } =
            new(false, default, 0, []);

        public BattleSenseResultSnapshot ToPublicSnapshot() =>
            new(
                true,
                Actor.ActorIndex,
                Actor.Name,
                Actor.IsEnemy,
                Actor.InformationVisible,
                Level,
                Actor.CurrentHp,
                Actor.MaxHp,
                Actor.CurrentMp,
                Actor.MaxMp,
                WeaknessElementIds);
    }

    private readonly record struct RawBattleActorCollectionCandidate(
        bool IsValid,
        RawBattleActorSnapshot[] Actors)
    {
        public static RawBattleActorCollectionCandidate Invalid { get; } = new(false, []);
    }

    private readonly record struct RawBattleEncounterCandidate(
        bool IsValid,
        int FormationId,
        int LayoutType,
        RawBattleActorSnapshot[] Enemies)
    {
        public static RawBattleEncounterCandidate Invalid { get; } = new(false, -1, -1, []);

        public BattleEncounterSnapshot ToPublicSnapshot() => IsValid
            ? new BattleEncounterSnapshot(
                true,
                FormationId,
                LayoutType,
                Enemies.Select(enemy => enemy.ToPublicSnapshot()).ToArray())
            : BattleEncounterSnapshot.Invalid;
    }

    private readonly record struct RawEnemyActionCandidate(
        BattleEnemyActionSnapshot Snapshot,
        RawBattleActorSnapshot Attacker)
    {
        public static RawEnemyActionCandidate Invalid { get; } =
            new(BattleEnemyActionSnapshot.Invalid, default);
    }

    private readonly record struct RawBattleTargetCandidate(
        bool IsValid,
        int TargetMask,
        int SelectedTarget,
        int TargetMode,
        int TargetFlags,
        RawBattleActorSnapshot Actor)
    {
        public static RawBattleTargetCandidate Invalid { get; } = new(false, 0, -1, -1, 0, default);

        public BattleTargetSnapshot ToPublicSnapshot() => IsValid
            ? new BattleTargetSnapshot(
                true,
                true,
                TargetMask,
                SelectedTarget,
                TargetMode,
                TargetFlags,
                Actor.ToPublicSnapshot())
            : BattleTargetSnapshot.Invalid;
    }

    private sealed class LegacyReadFailureException : Exception
    {
    }

    private sealed class InvalidBattleSnapshotException : Exception
    {
    }

    private bool TryReadItemSelection(int partySlot, out BattleMenuSelectionSnapshot selection)
    {
        selection = default;
        if (resolveItemName is null)
        {
            return false;
        }

        if (!TryComputeAddress(
                AddressItemCursorRow,
                partySlot,
                CharacterMenuBlockSize,
                out var cursorAddress) ||
            !TryComputeAddress(
                AddressItemScrollRow,
                partySlot,
                CharacterMenuBlockSize,
                out var scrollAddress))
        {
            return false;
        }

        var cursorRow = readInt32(cursorAddress);
        var scrollRow = readInt32(scrollAddress);
        var selectedIndex = (long)cursorRow + scrollRow;
        if (cursorRow < 0 || scrollRow < 0 || selectedIndex is < 0 or >= 320)
        {
            return false;
        }

        if (!TryComputeAddress(
                AddressBattleItems,
                (int)selectedIndex,
                ItemRecordSize,
                out var recordAddress))
        {
            return false;
        }

        var itemId = readUInt16(recordAddress);
        var quantity = readByte(recordAddress + ItemQuantityOffset);
        if (itemId == 0xFFFF || quantity == 0)
        {
            return false;
        }

        var name = resolveItemName(itemId);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // FUN_005d1520 builds each six-byte battle inventory row from the
        // complete 0..319 inventory namespace. FUN_006df007 and FUN_006debfe
        // render that row gray when the applicable restriction bit is set.
        // Mirror the native color decision so inaccessible rows remain
        // readable without implying that the player can select them.
        var itemUseContext = readByte(AddressBattleItemUseContext);
        var restrictionFlags = readByte(recordAddress + ItemRestrictionFlagsOffset);
        var unavailableMask = itemUseContext is 3 or 10 ? 0x02 : 0x08;
        var isAvailable = (restrictionFlags & unavailableMask) == 0;

        selection = new BattleMenuSelectionSnapshot(
            itemId,
            name,
            resolveItemDescription?.Invoke(itemId),
            quantity,
            null,
            isAvailable);
        return true;
    }

    private string ReadFixedText(int address, int length)
    {
        var bytes = new byte[length];
        if (addressSpace is not null)
        {
            if (address <= 0 || !addressSpace.TryRead((uint)address, bytes))
            {
                throw new LegacyReadFailureException();
            }
        }
        else
        {
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = readByte(address + index);
            }
        }

        var terminator = Array.IndexOf(bytes, (byte)0xFF);
        if (terminator < 0)
        {
            return string.Empty;
        }

        return Ff7EncodedTextDecoder.DecodeTerminated(bytes.AsSpan(0, terminator + 1));
    }
}

public record struct BattleActorSnapshot
{
    private bool isEnemy;
    private bool informationVisible;
    private int currentHp;
    private int maxHp;
    private int currentMp;
    private int maxMp;
    private uint statusMask;

    public BattleActorSnapshot(
        int actorIndex,
        string name,
        bool isEnemy,
        int currentHp,
        int maxHp,
        int currentMp,
        int maxMp,
        bool informationVisible,
        uint statusMask = 0)
    {
        ActorIndex = actorIndex;
        Name = name ?? string.Empty;
        this.isEnemy = isEnemy;
        this.informationVisible = informationVisible;
        if (CanExposeDetails)
        {
            this.currentHp = currentHp;
            this.maxHp = maxHp;
            this.currentMp = currentMp;
            this.maxMp = maxMp;
            this.statusMask = statusMask;
        }
        else
        {
            this.currentHp = 0;
            this.maxHp = 0;
            this.currentMp = 0;
            this.maxMp = 0;
            this.statusMask = 0;
        }
    }

    public int ActorIndex { readonly get; init; }

    public string Name { readonly get; init; }

    public bool IsEnemy
    {
        readonly get => isEnemy;
        init
        {
            isEnemy = value;
            RedactPrivateEnemyDetails();
        }
    }

    public int CurrentHp
    {
        readonly get => currentHp;
        init => currentHp = CanExposeDetails ? value : 0;
    }

    public int MaxHp
    {
        readonly get => maxHp;
        init => maxHp = CanExposeDetails ? value : 0;
    }

    public int CurrentMp
    {
        readonly get => currentMp;
        init => currentMp = CanExposeDetails ? value : 0;
    }

    public int MaxMp
    {
        readonly get => maxMp;
        init => maxMp = CanExposeDetails ? value : 0;
    }

    public bool InformationVisible
    {
        readonly get => informationVisible;
        init
        {
            informationVisible = value;
            RedactPrivateEnemyDetails();
        }
    }

    public uint StatusMask
    {
        readonly get => statusMask;
        init => statusMask = CanExposeDetails ? value : 0;
    }

    private readonly bool CanExposeDetails => !isEnemy || informationVisible;

    private void RedactPrivateEnemyDetails()
    {
        if (CanExposeDetails)
        {
            return;
        }

        currentHp = 0;
        maxHp = 0;
        currentMp = 0;
        maxMp = 0;
        statusMask = 0;
    }
}

public sealed record BattleSenseResultSnapshot
{
    public BattleSenseResultSnapshot(
        bool isValid,
        int actorIndex,
        string name,
        bool isEnemy,
        bool isSensed,
        int level,
        int currentHp,
        int maximumHp,
        int currentMp,
        int maximumMp,
        IEnumerable<int> weaknessElementIds)
    {
        IsValid = isValid;
        ActorIndex = actorIndex;
        Name = name ?? string.Empty;
        IsEnemy = isEnemy;
        IsSensed = isSensed;
        if (isValid && (!isEnemy || isSensed))
        {
            Level = level;
            CurrentHp = currentHp;
            MaximumHp = maximumHp;
            CurrentMp = currentMp;
            MaximumMp = maximumMp;
            WeaknessElementIds = Array.AsReadOnly(
                (weaknessElementIds ?? throw new ArgumentNullException(nameof(weaknessElementIds)))
                .ToArray());
        }
        else
        {
            WeaknessElementIds = Array.Empty<int>();
        }
    }

    public static BattleSenseResultSnapshot Invalid { get; } =
        new(false, -1, string.Empty, true, false, 0, 0, 0, 0, 0, []);

    public bool IsValid { get; }

    public int ActorIndex { get; }

    public string Name { get; }

    public bool IsEnemy { get; }

    public bool IsSensed { get; }

    public int? Level { get; }

    public int? CurrentHp { get; }

    public int? MaximumHp { get; }

    public int? CurrentMp { get; }

    public int? MaximumMp { get; }

    public IReadOnlyList<int> WeaknessElementIds { get; }
}

internal readonly record struct BattleActorVisibleCorrelation(
    int ActorIndex,
    string Name,
    bool IsEnemy,
    bool IsDefeated);

public readonly record struct BattleEncounterSnapshot(
    bool IsValid,
    int FormationId,
    int LayoutType,
    IReadOnlyList<BattleActorSnapshot> Enemies)
{
    public static BattleEncounterSnapshot Invalid { get; } = new(false, -1, -1, []);
}

public readonly record struct BattleEnemyActionSnapshot(
    bool IsValid,
    int EventIndex,
    int AttackerActorIndex,
    int CommandId,
    int SceneAttackIndex,
    int ActionId,
    int TargetMask,
    string? ActionName,
    string? AccessibilityDescription)
{
    public static BattleEnemyActionSnapshot Invalid { get; } =
        new(false, -1, -1, -1, -1, -1, 0, null, null);
}

public readonly record struct BattleMenuStateSnapshot(
    bool IsValid,
    short RendererState,
    int PartySlot,
    BattleActorSnapshot Actor,
    BattleMenuSelectionSnapshot? Selection = null)
{
    public static BattleMenuStateSnapshot Invalid { get; } = new(false, -1, -1, default, null);
}

public readonly record struct BattleMenuSelectionSnapshot(
    int EntryId,
    string Name,
    string? Description,
    int? Quantity,
    int? MpCost,
    bool IsAvailable = true);

public readonly record struct BattleTargetSnapshot(
    bool IsValid,
    bool IsTargeting,
    int TargetMask,
    int SelectedTarget,
    int TargetMode,
    int TargetFlags,
    BattleActorSnapshot Actor)
{
    public static BattleTargetSnapshot Invalid { get; } = new(false, false, 0, -1, -1, 0, default);
}

public readonly record struct BattlePartyProgressSnapshot(
    int PartySlot,
    int CharacterId,
    string Name,
    int Level);
