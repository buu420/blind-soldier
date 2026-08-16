using System.Buffers.Binary;
using System.Collections.Immutable;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

/// <summary>
/// Reads research-only battle evidence through the validated Steam 2026
/// translated x86 address space. It creates no hooks, publishes no events,
/// speaks nothing, and enables no runtime capability.
/// </summary>
public sealed class Steam2026BattleObservationReader
{
    private const ushort ValidActorMask = 0x03F7;
    private const ushort AllyActorMask = 0x0007;
    private const ushort EnemyActorMask = 0x03F0;

    private readonly ILegacyAddressSpace addressSpace;
    private readonly BattleStateReader battleReader;
    private readonly BattleResultsReader resultsReader;
    private readonly BattleDamagePopupReader damageReader;
    private readonly Steam2026BattleTextResolvers textResolvers;
    private readonly BattleRuntimeTextReader runtimeTextReader;

    public Steam2026BattleObservationReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory,
        Steam2026BattleTextResolvers textResolvers)
        : this(
            ValidatedTranslatedX86AddressSpaceFactory.Create(fingerprint, moduleBase, memory),
            textResolvers)
    {
    }

    internal Steam2026BattleObservationReader(
        ILegacyAddressSpace addressSpace,
        Steam2026BattleTextResolvers textResolvers)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.textResolvers = textResolvers ?? throw new ArgumentNullException(nameof(textResolvers));
        battleReader = new BattleStateReader(
            addressSpace,
            new SavemapPartyReader(addressSpace),
            textResolvers.ResolveAbilityName,
            textResolvers.ResolveAbilityDescription,
            textResolvers.ResolveInventoryObjectName,
            textResolvers.ResolveInventoryObjectDescription,
            textResolvers.ResolveCommandName,
            textResolvers.ResolveLimitName,
            textResolvers.ResolveLimitDescription);
        resultsReader = new BattleResultsReader(addressSpace, textResolvers.ResolveInventoryObjectName);
        damageReader = new BattleDamagePopupReader(addressSpace);
        runtimeTextReader = new BattleRuntimeTextReader(
            addressSpace,
            textResolvers.ResolveBattleText,
            textResolvers.ResolveInventoryObjectName,
            actorIndex => battleReader.TryReadBattleActor(actorIndex, out var actor)
                ? actor.Name
                : null,
            textResolvers.ResolveAbilityName,
            textResolvers.ResolveElementName,
            textResolvers.Language);
    }

    internal string? ResolveBattleText(int bufferIndex) =>
        runtimeTextReader.Resolve(bufferIndex);

    internal BattleRuntimeTextResolution? ResolveBattleTextDetailed(int bufferIndex) =>
        runtimeTextReader.ResolveDetailed(bufferIndex);

    internal string? ResolveElementName(int elementId) =>
        runtimeTextReader.ResolveElementName(elementId);

    internal bool TryReadSenseResult(
        int actorIndex,
        out BattleSenseObservation observation)
    {
        observation = null!;
        if (!battleReader.TryReadSenseResult(actorIndex, out var snapshot) ||
            !snapshot.IsValid)
        {
            return false;
        }

        observation = new BattleSenseObservation(
            snapshot.ActorIndex,
            snapshot.Name,
            snapshot.IsEnemy,
            snapshot.IsSensed,
            snapshot.Level ?? 0,
            snapshot.CurrentHp ?? 0,
            snapshot.MaximumHp ?? 0,
            snapshot.CurrentMp ?? 0,
            snapshot.MaximumMp ?? 0,
            snapshot.WeaknessElementIds);
        return true;
    }

    public bool TryReadResearchSnapshot(
        short rendererState,
        out Steam2026BattleResearchSnapshot snapshot)
    {
        snapshot = null!;
        if (!IsSupportedRendererState(rendererState) ||
            !TryCaptureBattleOwnership(rendererState, out var before) ||
            !IsValidBattleOwnership(before) ||
            !TryReadRawBattle(rendererState, before.TargetIsVisible, out var candidate) ||
            !TryCaptureBattleOwnership(rendererState, out var middle) ||
            before != middle ||
            !TryReadRawBattle(rendererState, before.TargetIsVisible, out var confirmation) ||
            !RawBattleEquals(candidate, confirmation) ||
            !TryCaptureBattleOwnership(rendererState, out var after) ||
            before != after ||
            !MatchesBattleOwnership(candidate, before))
        {
            return false;
        }

        snapshot = CreatePublicBattleSnapshot(candidate);
        return true;
    }

    public bool TryReadBattleFrame(
        int revision,
        short rendererState,
        out BattleFrameObservation observation)
    {
        observation = null!;
        if (revision < 0 || !TryReadResearchSnapshot(rendererState, out var snapshot))
        {
            return false;
        }

        var commandId = -1;
        var abilityId = -1;
        var itemId = -1;
        switch (rendererState)
        {
            case 1:
            case 2:
            case 3:
                commandId = snapshot.Menu.Selection.EntryId;
                break;
            case 4:
            case 6:
            case 7:
            case 0x18:
                abilityId = snapshot.Menu.Selection.EntryId;
                break;
            case 5:
                itemId = snapshot.Menu.Selection.EntryId;
                break;
            default:
                return false;
        }

        var rawTargetMask = snapshot.Target?.TargetMask ?? 0u;
        observation = new BattleFrameObservation(
            true,
            revision,
            snapshot.ReadyActorId,
            commandId,
            abilityId,
            itemId,
            rawTargetMask & AllyActorMask,
            (rawTargetMask & EnemyActorMask) >> 4,
            snapshot.Actors.Select(actor => new BattleActorObservation(
                actor.ActorId,
                actor.IsEnemy,
                actor.IsActive,
                actor.IsSensed,
                actor.CurrentHp,
                actor.MaximumHp,
                actor.CurrentMp,
                actor.MaximumMp,
                actor.StatusMask)));
        return true;
    }

    public bool TryReadEnemyActionResearchSnapshot(
        out Steam2026BattleActionResearchSnapshot snapshot)
    {
        snapshot = null!;
        if (!TryCaptureActionOwnership(out var before) ||
            !IsValidActionOwnership(before) ||
            !TryReadAction(out var candidate) ||
            !TryCaptureActionOwnership(out var middle) ||
            before != middle ||
            !TryReadAction(out var confirmation) ||
            candidate != confirmation ||
            !TryCaptureActionOwnership(out var after) ||
            before != after ||
            !MatchesActionOwnership(candidate, before))
        {
            return false;
        }

        snapshot = new Steam2026BattleActionResearchSnapshot(
            candidate.EventIndex,
            candidate.AttackerActorIndex,
            candidate.CommandId,
            candidate.SceneAttackIndex,
            candidate.ActionId,
            checked((uint)candidate.TargetMask),
            candidate.ActionName!,
            candidate.AccessibilityDescription,
            before.FormationId);
        return true;
    }

    public bool TryReadResultsResearchSnapshot(
        out Steam2026BattleResultsResearchSnapshot snapshot)
    {
        snapshot = null!;
        if (!TryCaptureResultsOwnership(out var before) ||
            !IsValidResultsOwnership(before) ||
            !TryReadResults(out var candidate) ||
            !TryCaptureResultsOwnership(out var middle) ||
            !ResultsOwnershipEquals(before, middle) ||
            !TryReadResults(out var confirmation) ||
            !ResultsSnapshotEquals(candidate, confirmation) ||
            !TryCaptureResultsOwnership(out var after) ||
            !ResultsOwnershipEquals(before, after) ||
            !MatchesResultsOwnership(candidate, before) ||
            !TryCreatePublicResults(candidate, before, out snapshot))
        {
            snapshot = null!;
            return false;
        }

        return true;
    }

    public bool TryReadDamageResearchSnapshot(
        out Steam2026BattleDamageResearchSnapshot snapshot)
    {
        snapshot = null!;
        if (!TryCaptureDamageOwnership(out var before) ||
            !TryReadDamage(out var candidate) ||
            !TryCaptureDamageOwnership(out var middle) ||
            before != middle ||
            !TryReadDamage(out var confirmation) ||
            candidate != confirmation ||
            !TryCaptureDamageOwnership(out var after) ||
            before != after ||
            !MatchesDamageOwnership(candidate, before))
        {
            return false;
        }

        snapshot = new Steam2026BattleDamageResearchSnapshot(
            candidate.EffectIndex,
            candidate.TargetActor,
            candidate.Value,
            candidate.Flags,
            candidate.IsMiss);
        return true;
    }

    internal bool TryReadCurrentModule(out int module)
    {
        module = -1;
        if (!addressSpace.TryReadByte(
                (uint)BattleStateReader.AddressCurrentModule,
                out var first)
            || !addressSpace.TryReadByte(
                (uint)BattleStateReader.AddressCurrentModule,
                out var second)
            || first != second)
        {
            return false;
        }

        module = first;
        return true;
    }

    internal bool TryReadVictorySignal(out bool isVictory)
    {
        isVictory = false;
        try
        {
            return battleReader.TryReadVictorySignal(out isVictory);
        }
        catch
        {
            isVictory = false;
            return false;
        }
    }

    internal bool TryReadMenuTrackerSnapshot(
        short rendererState,
        out BattleMenuStateSnapshot snapshot)
    {
        snapshot = BattleMenuStateSnapshot.Invalid;
        if (!IsSupportedRendererState(rendererState))
        {
            return false;
        }

        try
        {
            var candidate = battleReader.ReadMenuState(rendererState);
            if (!candidate.IsValid
                || candidate.RendererState != rendererState
                || candidate.PartySlot is < 0 or >= 3
                || candidate.Actor.ActorIndex != candidate.PartySlot
                || candidate.Actor.IsEnemy
                || string.IsNullOrWhiteSpace(candidate.Actor.Name)
                || candidate.Selection is not { } selection
                || string.IsNullOrWhiteSpace(selection.Name))
            {
                return false;
            }

            snapshot = candidate;
            return true;
        }
        catch
        {
            snapshot = BattleMenuStateSnapshot.Invalid;
            return false;
        }
    }

    internal bool TryReadBattleTrackerSnapshot(
        out Steam2026BattleTrackerSnapshot snapshot) =>
        TryReadBattleTrackerSnapshot(includePolledEnemyAction: true, out snapshot);

    internal bool TryReadBattleTrackerSnapshot(
        bool includePolledEnemyAction,
        out Steam2026BattleTrackerSnapshot snapshot)
    {
        snapshot = default;
        try
        {
            if (!TryCaptureBattleTrackerOwnership(out var before)
                || before.Module != BattleStateReader.BattleModule
                || !battleReader.TryReadBattleActors(out var actorList)
                || actorList.Count == 0)
            {
                return false;
            }

            var actors = actorList.ToImmutableArray();
            var enemies = actors.Where(actor => actor.IsEnemy).ToArray();
            if (enemies.Length == 0)
            {
                return false;
            }

            var encounter = new BattleEncounterSnapshot(
                true,
                before.FormationId,
                before.LayoutType,
                enemies);
            var action = includePolledEnemyAction
                ? battleReader.ReadCurrentEnemyAction()
                : BattleEnemyActionSnapshot.Invalid;
            if (includePolledEnemyAction
                && action.IsValid
                && (!actors.Any(actor => actor.ActorIndex == action.AttackerActorIndex)
                    || string.IsNullOrWhiteSpace(action.ActionName)
                    || (action.TargetMask & ~ValidActorMask) != 0))
            {
                return false;
            }

            var target = battleReader.ReadTarget();
            if (target.IsValid && !actors.Contains(target.Actor))
            {
                return false;
            }

            if (!battleReader.TryIsRootCommandMenuActive(out var rootCommandMenuActive)
                || !battleReader.TryReadPartyProgress(out var progress)
                || progress.Count == 0
                || !TryCaptureBattleTrackerOwnership(out var after)
                || before != after)
            {
                return false;
            }

            snapshot = new Steam2026BattleTrackerSnapshot(
                encounter,
                actors,
                action,
                target,
                rootCommandMenuActive,
                progress.ToImmutableArray());
            return true;
        }
        catch
        {
            snapshot = default;
            return false;
        }
    }

    internal bool TryReadDamageTrackerSnapshot(
        BattleDamagePopupSnapshot capturedPopup,
        out Steam2026BattleDamageTrackerSnapshot snapshot)
    {
        snapshot = default;
        try
        {
            if (!IsValidCapturedDamagePopup(capturedPopup)
                || !battleReader.TryReadBattleActor(capturedPopup.TargetActor, out var actor)
                || actor.ActorIndex != capturedPopup.TargetActor
                || !battleReader.TryReadVisibleActorCorrelation(
                    capturedPopup.TargetActor,
                    out var visibleActor)
                || visibleActor.ActorIndex != capturedPopup.TargetActor
                || !string.Equals(
                    actor.Name,
                    visibleActor.Name,
                    StringComparison.Ordinal))
            {
                return false;
            }

            snapshot = new Steam2026BattleDamageTrackerSnapshot(
                capturedPopup,
                actor,
                visibleActor);
            return true;
        }
        catch
        {
            snapshot = default;
            return false;
        }
    }

    internal bool TryReadActionTextTrackerSnapshot(
        Steam2026BattleActionTextCommitSnapshot capturedAction,
        out Steam2026BattleActionTextTrackerSnapshot snapshot)
    {
        snapshot = default;
        try
        {
            if (!capturedAction.IsValid
                || capturedAction.EffectIndex >= Steam2026BattleActionTextMemory.EffectCount
                || capturedAction.ActorIndex is not (>= 0 and <= 2) and not (>= 4 and <= 9)
                || capturedAction.RemainingFrames <= 0
                || !TryReadCurrentModule(out var module)
                || module != BattleStateReader.BattleModule
                || !battleReader.TryReadBattleActor(capturedAction.ActorIndex, out var actor)
                || actor.ActorIndex != capturedAction.ActorIndex)
            {
                return false;
            }

            var actionName = ResolveVisibleActionName(
                capturedAction.CommandId,
                capturedAction.ActionId);
            if (string.IsNullOrWhiteSpace(actionName))
            {
                return false;
            }

            snapshot = new Steam2026BattleActionTextTrackerSnapshot(
                capturedAction,
                actor,
                actionName.Trim());
            return true;
        }
        catch
        {
            snapshot = default;
            return false;
        }
    }

    internal bool TryResolveCapturedEnemyAction(
        Steam2026BattleEnemyActionIngressSnapshot captured,
        out BattleEnemyActionSnapshot action,
        out BattleActorSnapshot attacker)
    {
        action = BattleEnemyActionSnapshot.Invalid;
        attacker = default;
        if (!captured.WasCaptured)
        {
            return false;
        }

        if (!captured.Raw.IsCoherent)
        {
            action = captured.Action;
            attacker = captured.Attacker;
            return !action.IsValid
                   || (attacker.ActorIndex == action.AttackerActorIndex
                       && attacker.IsEnemy);
        }

        var raw = captured.Raw;
        if (!raw.IsActionCandidate)
        {
            return true;
        }

        try
        {
            if (!TryReadCurrentModule(out var module)
                || module != BattleStateReader.BattleModule
                || !TryReadSceneActionId(raw.SceneAttackIndex, out var actionId)
                || !TryReadSceneActionName(raw.SceneAttackIndex, out var actionName)
                || !TryReadFormationId(out var formationId)
                || !battleReader.TryReadBattleActor(raw.AttackerActorIndex, out attacker)
                || attacker.ActorIndex != raw.AttackerActorIndex
                || !attacker.IsEnemy)
            {
                action = BattleEnemyActionSnapshot.Invalid;
                attacker = default;
                return false;
            }

            var accessibilityDescription =
                BattleStateReader.ResolveAccessibilityActionDescription(formationId, actionId);
            if (actionName is null && string.IsNullOrWhiteSpace(accessibilityDescription))
            {
                action = BattleEnemyActionSnapshot.Invalid;
                attacker = default;
                return false;
            }

            action = new BattleEnemyActionSnapshot(
                true,
                raw.EventIndex,
                raw.AttackerActorIndex,
                raw.CommandId,
                raw.SceneAttackIndex,
                actionId,
                0,
                actionName,
                accessibilityDescription);
            return true;
        }
        catch
        {
            action = BattleEnemyActionSnapshot.Invalid;
            attacker = default;
            return false;
        }
    }

    private static bool IsValidCapturedDamagePopup(BattleDamagePopupSnapshot popup) =>
        popup.IsValid
        && popup.EffectIndex is >= 0 and < BattleDamagePopupReader.EffectCount
        && popup.TargetActor is (>= 0 and < 3) or (>= 4 and <= 9)
        && (popup.Value > 0 || popup.Value == -1);

    private string? ResolveVisibleActionName(byte commandId, ushort actionId)
    {
        if (commandId == BattleStateReader.EnemyActionCommandId)
        {
            return TryReadSceneActionName(actionId, out var enemyActionName)
                ? enemyActionName
                : null;
        }

        return commandId switch
        {
            0x02 or 0x03 or 0x0D or 0x14 or 0x15 or 0x16 =>
                textResolvers.ResolveAbilityName(actionId),
            0x04 or 0x17 => textResolvers.ResolveItemName(actionId),
            _ => textResolvers.ResolveCommandName(commandId)
        };
    }

    private bool TryReadSceneActionName(ushort sceneAction, out string? name)
    {
        name = null;
        int sceneActionIndex;
        if (sceneAction < BattleStateReader.SceneAttackCount)
        {
            sceneActionIndex = sceneAction;
        }
        else if (!TryFindSceneActionIndex(sceneAction, out sceneActionIndex))
        {
            return false;
        }

        var candidateAddress = (ulong)(uint)BattleStateReader.AddressSceneAttackNames
                               + ((ulong)sceneActionIndex
                                  * BattleStateReader.SceneAttackNameLength);
        if (candidateAddress > uint.MaxValue)
        {
            return false;
        }

        Span<byte> first = stackalloc byte[BattleStateReader.SceneAttackNameLength];
        Span<byte> second = stackalloc byte[BattleStateReader.SceneAttackNameLength];
        var address = (uint)candidateAddress;
        if (!addressSpace.TryRead(address, first)
            || !addressSpace.TryRead(address, second)
            || !first.SequenceEqual(second)
            || first.IndexOf((byte)0xFF) < 0)
        {
            return false;
        }

        var decoded = Ff7EncodedTextDecoder.Decode(first).Trim();
        name = string.IsNullOrWhiteSpace(decoded) ? null : decoded;
        return true;
    }

    private bool TryReadSceneActionId(ushort sceneActionIndex, out ushort actionId)
    {
        actionId = ushort.MaxValue;
        if (sceneActionIndex >= BattleStateReader.SceneAttackCount)
        {
            return false;
        }

        var address = checked(
            (uint)BattleStateReader.AddressSceneAttackIds
            + ((uint)sceneActionIndex * BattleStateReader.SceneAttackIdSize));
        if (!addressSpace.TryReadUInt16(address, out var first)
            || !addressSpace.TryReadUInt16(address, out var second)
            || first != second
            || first == ushort.MaxValue)
        {
            return false;
        }

        actionId = first;
        return true;
    }

    private bool TryReadFormationId(out ushort formationId)
    {
        formationId = 0;
        if (!addressSpace.TryReadUInt16(
                (uint)BattleStateReader.AddressBattleFormationId,
                out var first)
            || !addressSpace.TryReadUInt16(
                (uint)BattleStateReader.AddressBattleFormationId,
                out var second)
            || first != second
            || first >= 1024)
        {
            return false;
        }

        formationId = first;
        return true;
    }

    private bool TryFindSceneActionIndex(ushort actionId, out int sceneActionIndex)
    {
        sceneActionIndex = -1;
        const int tableLength = BattleStateReader.SceneAttackCount
                                * BattleStateReader.SceneAttackIdSize;
        Span<byte> first = stackalloc byte[tableLength];
        Span<byte> second = stackalloc byte[tableLength];
        if (!addressSpace.TryRead(
                (uint)BattleStateReader.AddressSceneAttackIds,
                first)
            || !addressSpace.TryRead(
                (uint)BattleStateReader.AddressSceneAttackIds,
                second)
            || !first.SequenceEqual(second))
        {
            return false;
        }

        for (var index = 0; index < BattleStateReader.SceneAttackCount; index++)
        {
            var offset = index * BattleStateReader.SceneAttackIdSize;
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    first.Slice(offset, sizeof(ushort))) == actionId)
            {
                sceneActionIndex = index;
                return true;
            }
        }

        return false;
    }

    internal bool TryReadResultsTrackerSnapshot(
        out Steam2026BattleResultsTrackerSnapshot snapshot)
    {
        snapshot = default;
        try
        {
            if (!TryReadResultsResearchSnapshot(out var before)
                || !battleReader.TryReadPartyProgress(out var progress)
                || progress.Count == 0
                || !TryReadResultsResearchSnapshot(out var after)
                || !before.Equals(after))
            {
                return false;
            }

            var results = new BattleResultsSnapshot(
                true,
                before.State,
                before.Experience,
                before.Ap,
                before.Gil,
                before.Rewards
                    .Select(reward => new BattleRewardItemSnapshot(
                        reward.ItemId,
                        reward.Quantity,
                        reward.Name,
                        reward.PhysicalSlot,
                        reward.IsSelectedToTake))
                    .ToImmutableArray(),
                before.IsPageReady,
                before.HasRewardItems,
                before.RewardSelection,
                before.RewardTransition,
                before.InputEdges,
                before.InputRepeat,
                before.HeldInput);
            snapshot = new Steam2026BattleResultsTrackerSnapshot(
                results,
                progress.ToImmutableArray());
            return true;
        }
        catch
        {
            snapshot = default;
            return false;
        }
    }

    internal bool TryReadCapturedResultsTrackerSnapshot(
        Steam2026BattleResultsIngressSnapshot captured,
        out Steam2026BattleResultsTrackerSnapshot snapshot)
    {
        snapshot = default;
        if (!captured.WasCaptured
            || captured.State is < 0 or > 5
            || captured.Experience < 0
            || captured.Ap < 0
            || captured.Gil < 0
            || captured.RewardSelection is < 0 or > 5
            || captured.Rewards.Length != BattleResultsReader.RewardItemCount
            || captured.PartyProgress.IsDefaultOrEmpty)
        {
            return false;
        }

        var items = ImmutableArray.CreateBuilder<BattleRewardItemSnapshot>();
        for (var physicalSlot = 0; physicalSlot < captured.Rewards.Length; physicalSlot++)
        {
            var reward = captured.Rewards[physicalSlot];
            if (reward.SelectedToTake > 1
                || (reward.ItemId != ushort.MaxValue
                    && reward.Quantity != 0
                    && reward.ItemId >= BattleResultsReader.InventoryObjectCount))
            {
                return false;
            }

            if (reward.ItemId == ushort.MaxValue || reward.Quantity == 0)
            {
                continue;
            }

            var name = textResolvers.ResolveInventoryObjectName(reward.ItemId);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            items.Add(new BattleRewardItemSnapshot(
                reward.ItemId,
                reward.Quantity,
                name.Trim(),
                physicalSlot,
                reward.SelectedToTake != 0));
        }

        if (captured.State == 2 && captured.RewardTransition == 0)
        {
            var hasNativeItems = captured.Rewards.Any(reward =>
                reward.ItemId != ushort.MaxValue && reward.Quantity != 0);
            if (captured.HasRewardItems != hasNativeItems
                || (!captured.HasRewardItems && captured.RewardSelection != 5)
                || (captured.HasRewardItems
                    && captured.RewardSelection is >= 1 and <= 4
                    && (captured.Rewards[captured.RewardSelection - 1].ItemId == ushort.MaxValue
                        || captured.Rewards[captured.RewardSelection - 1].Quantity == 0)))
            {
                return false;
            }
        }

        snapshot = new Steam2026BattleResultsTrackerSnapshot(
            new BattleResultsSnapshot(
                true,
                captured.State,
                captured.Experience,
                captured.Ap,
                captured.Gil,
                items.ToImmutable(),
                captured.IsPageReady,
                captured.HasRewardItems,
                captured.RewardSelection,
                captured.RewardTransition,
                captured.InputEdges,
                captured.InputRepeat,
                captured.HeldInput),
            captured.PartyProgress);
        return true;
    }

    private bool TryCaptureBattleTrackerOwnership(
        out BattleTrackerOwnershipSnapshot ownership)
    {
        ownership = default;
        if (!addressSpace.TryReadByte(
                (uint)BattleStateReader.AddressCurrentModule,
                out var module)
            || !addressSpace.TryReadUInt16(
                (uint)BattleStateReader.AddressBattleFormationId,
                out var formationId)
            || !addressSpace.TryReadByte(
                (uint)BattleStateReader.AddressBattleLayoutType,
                out var layoutType)
            || formationId >= 1024
            || layoutType > 8)
        {
            return false;
        }

        ownership = new BattleTrackerOwnershipSnapshot(module, formationId, layoutType);
        return true;
    }

    private bool TryReadRawBattle(
        short rendererState,
        bool includeTarget,
        out RawBattleSnapshot snapshot)
    {
        snapshot = null!;
        var menu = battleReader.ReadMenuState(rendererState);
        if (!menu.IsValid || !menu.Selection.HasValue ||
            !battleReader.TryReadBattleActors(out var actorList) ||
            actorList.Count == 0)
        {
            return false;
        }

        var actors = actorList.ToImmutableArray();
        var encounter = battleReader.ReadEncounter();
        if (!encounter.IsValid ||
            !encounter.Enemies.SequenceEqual(actors.Where(actor => actor.IsEnemy)) ||
            !actors.Contains(menu.Actor))
        {
            return false;
        }

        BattleTargetSnapshot? target = null;
        if (includeTarget)
        {
            var selectedTarget = battleReader.ReadTarget();
            if (!selectedTarget.IsValid ||
                !actors.Contains(selectedTarget.Actor))
            {
                return false;
            }

            target = selectedTarget;
        }

        snapshot = new RawBattleSnapshot(menu, actors, encounter, target);
        return true;
    }

    private bool TryReadAction(out BattleEnemyActionSnapshot snapshot)
    {
        snapshot = battleReader.ReadCurrentEnemyAction();
        return snapshot.IsValid &&
            !string.IsNullOrWhiteSpace(snapshot.ActionName) &&
            snapshot.TargetMask != 0 &&
            (snapshot.TargetMask & ~ValidActorMask) == 0;
    }

    private bool TryReadResults(out BattleResultsSnapshot snapshot)
    {
        snapshot = resultsReader.Read();
        return snapshot.IsValid;
    }

    private bool TryReadDamage(out BattleDamagePopupSnapshot snapshot)
    {
        snapshot = damageReader.Read();
        return snapshot.IsValid;
    }

    private bool TryCaptureBattleOwnership(
        short rendererState,
        out BattleOwnershipSnapshot ownership)
    {
        ownership = default;
        if (!TryAdd((uint)BattleStateReader.AddressMenuWindowStates, rendererState, out var windowAddress) ||
            !addressSpace.TryReadByte((uint)BattleStateReader.AddressCurrentModule, out var module) ||
            !addressSpace.TryReadByte((uint)BattleStateReader.AddressCurrentActorSlot, out var currentActor) ||
            !addressSpace.TryReadByte(windowAddress, out var windowState) ||
            !addressSpace.TryReadUInt16((uint)BattleStateReader.AddressBattleFormationId, out var formationId) ||
            !addressSpace.TryReadByte((uint)BattleStateReader.AddressBattleLayoutType, out var layoutType) ||
            !addressSpace.TryReadInt32((uint)BattleStateReader.AddressBattleMenuTextState, out var menuTextState) ||
            !addressSpace.TryReadUInt16((uint)BattleStateReader.AddressTargetMask, out var targetMask) ||
            !addressSpace.TryReadByte((uint)BattleStateReader.AddressSelectedTarget, out var selectedTarget) ||
            !addressSpace.TryReadByte((uint)BattleStateReader.AddressTargetMode, out var targetMode) ||
            !addressSpace.TryReadByte((uint)BattleStateReader.AddressTargetFlags, out var targetFlags))
        {
            return false;
        }

        ownership = new BattleOwnershipSnapshot(
            module,
            currentActor,
            windowState,
            formationId,
            layoutType,
            menuTextState,
            targetMask,
            selectedTarget,
            targetMode,
            targetFlags);
        return true;
    }

    private static bool IsValidBattleOwnership(BattleOwnershipSnapshot ownership)
    {
        if (ownership.Module != BattleStateReader.BattleModule ||
            ownership.CurrentActor >= 3 ||
            ownership.WindowState != BattleStateReader.ActiveWindowState ||
            ownership.FormationId >= 1024 ||
            ownership.LayoutType > 8 ||
            (ownership.TargetMask & ~ValidActorMask) != 0)
        {
            return false;
        }

        if (!ownership.TargetIsVisible)
        {
            return true;
        }

        return ownership.TargetMask != 0 &&
            ownership.SelectedTarget < 16 &&
            (ownership.TargetMask & (1 << ownership.SelectedTarget)) != 0 &&
            ownership.SelectedTarget is (>= 0 and < 3) or (>= 4 and <= 9);
    }

    private static bool MatchesBattleOwnership(
        RawBattleSnapshot snapshot,
        BattleOwnershipSnapshot ownership)
    {
        if (snapshot.Menu.PartySlot != ownership.CurrentActor ||
            snapshot.Encounter.FormationId != ownership.FormationId ||
            snapshot.Encounter.LayoutType != ownership.LayoutType)
        {
            return false;
        }

        if (!ownership.TargetIsVisible)
        {
            return snapshot.Target is null;
        }

        return snapshot.Target is { } target &&
            target.TargetMask == ownership.TargetMask &&
            target.SelectedTarget == ownership.SelectedTarget &&
            target.TargetMode == ownership.TargetMode &&
            target.TargetFlags == ownership.TargetFlags;
    }

    private bool TryCaptureActionOwnership(out ActionOwnershipSnapshot ownership)
    {
        ownership = default;
        if (!addressSpace.TryReadByte((uint)BattleStateReader.AddressCurrentModule, out var module) ||
            !addressSpace.TryReadByte((uint)BattleStateReader.AddressAnimationEventIndex, out var eventIndex) ||
            eventIndex >= BattleStateReader.AnimationEventCount ||
            !TryAddScaled(
                (uint)BattleStateReader.AddressAnimationEventQueue,
                eventIndex,
                BattleStateReader.AnimationEventSize,
                out var eventAddress) ||
            !TryAdd(eventAddress, BattleStateReader.AnimationEventAttackerOffset, out var attackerAddress) ||
            !TryAdd(eventAddress, BattleStateReader.AnimationEventKindOffset, out var kindAddress) ||
            !TryAdd(eventAddress, BattleStateReader.AnimationEventCommandOffset, out var commandAddress) ||
            !TryAdd(eventAddress, BattleStateReader.AnimationEventActionOffset, out var sceneIndexAddress) ||
            !addressSpace.TryReadByte(attackerAddress, out var attacker) ||
            !addressSpace.TryReadByte(kindAddress, out var kind) ||
            !addressSpace.TryReadByte(commandAddress, out var command) ||
            !addressSpace.TryReadUInt16(sceneIndexAddress, out var sceneIndex) ||
            sceneIndex >= BattleStateReader.SceneAttackCount ||
            !TryAddScaled(
                (uint)BattleStateReader.AddressSceneAttackIds,
                sceneIndex,
                BattleStateReader.SceneAttackIdSize,
                out var actionIdAddress) ||
            !addressSpace.TryReadUInt16(actionIdAddress, out var actionId) ||
            !addressSpace.TryReadUInt16((uint)BattleStateReader.AddressBattleActionTargetMask, out var targetMask) ||
            !addressSpace.TryReadUInt16((uint)BattleStateReader.AddressBattleFormationId, out var formationId))
        {
            return false;
        }

        ownership = new ActionOwnershipSnapshot(
            module,
            eventIndex,
            attacker,
            kind,
            command,
            sceneIndex,
            actionId,
            targetMask,
            formationId);
        return true;
    }

    private static bool IsValidActionOwnership(ActionOwnershipSnapshot ownership) =>
        ownership.Module == BattleStateReader.BattleModule &&
        ownership.Attacker is >= 4 and <= 9 &&
        ownership.Kind == BattleStateReader.ActionAnimationEventKind &&
        ownership.Command == BattleStateReader.EnemyActionCommandId &&
        ownership.ActionId != ushort.MaxValue &&
        ownership.TargetMask != 0 &&
        (ownership.TargetMask & ~ValidActorMask) == 0 &&
        ownership.FormationId < 1024;

    private static bool MatchesActionOwnership(
        BattleEnemyActionSnapshot snapshot,
        ActionOwnershipSnapshot ownership) =>
        snapshot.EventIndex == ownership.EventIndex &&
        snapshot.AttackerActorIndex == ownership.Attacker &&
        snapshot.CommandId == ownership.Command &&
        snapshot.SceneAttackIndex == ownership.SceneIndex &&
        snapshot.ActionId == ownership.ActionId &&
        snapshot.TargetMask == ownership.TargetMask;

    private bool TryCaptureResultsOwnership(out ResultsOwnershipSnapshot ownership)
    {
        ownership = null!;
        if (!addressSpace.TryReadByte((uint)BattleResultsReader.AddressCurrentModule, out var module) ||
            !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressResultsState, out var state) ||
            !addressSpace.TryReadByte((uint)BattleResultsReader.AddressResultsPageReady, out var pageReady) ||
            !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressExperience, out var experience) ||
            !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressAp, out var ap) ||
            !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressGil, out var gil) ||
            !addressSpace.TryReadInt32(
                (uint)BattleResultsReader.AddressHasRewardItems,
                out var hasRewardItems) ||
            !addressSpace.TryReadInt32(
                (uint)BattleResultsReader.AddressRewardSelection,
                out var rewardSelection) ||
            !addressSpace.TryReadInt16(
                (uint)BattleResultsReader.AddressRewardTransition,
                out var rewardTransition) ||
            !addressSpace.TryReadInt32(
                (uint)BattleResultsReader.AddressInputEdges,
                out var inputEdges) ||
            !addressSpace.TryReadInt32(
                (uint)BattleResultsReader.AddressInputRepeat,
                out var inputRepeat) ||
            !addressSpace.TryReadInt32(
                (uint)BattleResultsReader.AddressHeldInput,
                out var heldInput))
        {
            return false;
        }

        var rewards = ImmutableArray.CreateBuilder<RawRewardOwnership>(BattleResultsReader.RewardItemCount);
        for (var index = 0; index < BattleResultsReader.RewardItemCount; index++)
        {
            if (!TryAddScaled(
                    (uint)BattleResultsReader.AddressRewardItems,
                    index,
                    BattleResultsReader.RewardItemSize,
                    out var rewardAddress) ||
                !TryAdd(rewardAddress, 2, out var quantityAddress) ||
                !TryAdd(
                    rewardAddress,
                    BattleResultsReader.RewardSelectedOffset,
                    out var selectedAddress) ||
                !addressSpace.TryReadUInt16(rewardAddress, out var itemId) ||
                !addressSpace.TryReadUInt16(quantityAddress, out var quantity) ||
                !addressSpace.TryReadUInt16(selectedAddress, out var selectedToTake))
            {
                return false;
            }

            rewards.Add(new RawRewardOwnership(itemId, quantity, selectedToTake));
        }

        ownership = new ResultsOwnershipSnapshot(
            module,
            state,
            pageReady,
            experience,
            ap,
            gil,
            hasRewardItems,
            rewardSelection,
            rewardTransition,
            inputEdges,
            inputRepeat,
            heldInput,
            rewards.MoveToImmutable());
        return true;
    }

    private static bool IsValidResultsOwnership(ResultsOwnershipSnapshot ownership) =>
        ownership.Module == BattleResultsReader.ResultsModule &&
        ownership.State is >= 0 and <= 5 &&
        ownership.PageReady <= 1 &&
        ownership.Experience >= 0 &&
        ownership.Ap >= 0 &&
        ownership.Gil >= 0 &&
        ownership.HasRewardItems is 0 or 1 &&
        ownership.RewardSelection is >= 0 and <= 5 &&
        ownership.Rewards.Length == BattleResultsReader.RewardItemCount &&
        ownership.Rewards.All(reward =>
            reward.SelectedToTake <= 1 &&
            (reward.ItemId == ushort.MaxValue ||
             reward.Quantity == 0 ||
             reward.ItemId < BattleResultsReader.InventoryObjectCount));

    private static bool MatchesResultsOwnership(
        BattleResultsSnapshot snapshot,
        ResultsOwnershipSnapshot ownership) =>
        snapshot.State == ownership.State &&
        snapshot.IsPageReady == (ownership.PageReady != 0) &&
        snapshot.Experience == ownership.Experience &&
        snapshot.Ap == ownership.Ap &&
        snapshot.Gil == ownership.Gil &&
        snapshot.HasRewardItems == (ownership.HasRewardItems != 0) &&
        snapshot.RewardSelection == ownership.RewardSelection &&
        snapshot.RewardTransition == ownership.RewardTransition &&
        snapshot.InputEdges == ownership.InputEdges &&
        snapshot.InputRepeat == ownership.InputRepeat &&
        snapshot.HeldInput == ownership.HeldInput;

    private bool TryCreatePublicResults(
        BattleResultsSnapshot source,
        ResultsOwnershipSnapshot ownership,
        out Steam2026BattleResultsResearchSnapshot snapshot)
    {
        snapshot = null!;
        var expectedRewards = ownership.Rewards
            .Select((reward, physicalSlot) => (reward, physicalSlot))
            .Where(entry => entry.reward.ItemId != ushort.MaxValue && entry.reward.Quantity != 0)
            .ToImmutableArray();
        if (source.Items.Count != expectedRewards.Length)
        {
            return false;
        }

        var rewards = ImmutableArray.CreateBuilder<Steam2026BattleRewardResearchSnapshot>(source.Items.Count);
        for (var index = 0; index < source.Items.Count; index++)
        {
            var item = source.Items[index];
            var expectedReward = expectedRewards[index];
            var expectedName = textResolvers.ResolveInventoryObjectName(item.ItemId);
            if (item.ItemId != expectedReward.reward.ItemId ||
                item.Quantity != expectedReward.reward.Quantity ||
                item.PhysicalSlot != expectedReward.physicalSlot ||
                item.IsSelectedToTake != (expectedReward.reward.SelectedToTake != 0) ||
                string.IsNullOrWhiteSpace(expectedName) ||
                !string.Equals(expectedName.Trim(), item.Name, StringComparison.Ordinal))
            {
                return false;
            }

            rewards.Add(new Steam2026BattleRewardResearchSnapshot(
                item.ItemId,
                item.Quantity,
                item.Name,
                item.PhysicalSlot,
                item.IsSelectedToTake));
        }

        snapshot = new Steam2026BattleResultsResearchSnapshot(
            source.State,
            source.Experience,
            source.Ap,
            source.Gil,
            source.IsPageReady,
            source.HasRewardItems,
            source.RewardSelection,
            source.RewardTransition,
            source.InputEdges,
            source.InputRepeat,
            source.HeldInput,
            rewards.MoveToImmutable());
        return true;
    }

    private bool TryCaptureDamageOwnership(out DamageOwnershipSnapshot ownership)
    {
        ownership = default;
        if (!addressSpace.TryReadByte((uint)BattleDamagePopupReader.AddressCurrentModule, out var module) ||
            !addressSpace.TryReadUInt16((uint)BattleDamagePopupReader.AddressCurrentEffectIndex, out var effectIndex) ||
            effectIndex >= BattleDamagePopupReader.EffectCount ||
            !TryAddScaled(
                (uint)BattleDamagePopupReader.AddressEffectData,
                effectIndex,
                BattleDamagePopupReader.EffectRecordSize,
                out var recordAddress) ||
            !TryAdd(recordAddress, BattleDamagePopupReader.StateOffset, out var stateAddress) ||
            !TryAdd(recordAddress, BattleDamagePopupReader.ValueOffset, out var valueAddress) ||
            !TryAdd(recordAddress, BattleDamagePopupReader.TargetActorOffset, out var targetAddress) ||
            !TryAdd(recordAddress, BattleDamagePopupReader.FlagsOffset, out var flagsAddress) ||
            !addressSpace.TryReadByte(stateAddress, out var state) ||
            !addressSpace.TryReadInt16(valueAddress, out var value) ||
            !addressSpace.TryReadInt32(targetAddress, out var targetActor) ||
            !addressSpace.TryReadInt32(flagsAddress, out var flags))
        {
            return false;
        }

        ownership = new DamageOwnershipSnapshot(
            module,
            effectIndex,
            state,
            value,
            targetActor,
            flags);
        return module == BattleStateReader.BattleModule &&
            state == 0 &&
            targetActor is (>= 0 and < 3) or (>= 4 and <= 9) &&
            (value > 0 || value == -1);
    }

    private static bool MatchesDamageOwnership(
        BattleDamagePopupSnapshot snapshot,
        DamageOwnershipSnapshot ownership) =>
        snapshot.EffectIndex == ownership.EffectIndex &&
        snapshot.TargetActor == ownership.TargetActor &&
        snapshot.Value == ownership.Value &&
        snapshot.Flags == ownership.Flags;

    private static Steam2026BattleResearchSnapshot CreatePublicBattleSnapshot(
        RawBattleSnapshot source)
    {
        var actors = source.Actors
            .Select(CreatePublicActor)
            .ToImmutableArray();
        var selection = source.Menu.Selection!.Value;
        var menu = new Steam2026BattleMenuResearchSnapshot(
            source.Menu.RendererState,
            source.Menu.PartySlot,
            new Steam2026BattleSelectionResearchSnapshot(
                selection.EntryId,
                selection.Name,
                selection.Description,
                selection.Quantity,
                selection.MpCost,
                selection.IsAvailable));

        Steam2026BattleTargetResearchSnapshot? target = null;
        if (source.Target is { } rawTarget)
        {
            var publicTargetActor = actors.Single(actor => actor.ActorId == rawTarget.Actor.ActorIndex);
            target = new Steam2026BattleTargetResearchSnapshot(
                checked((uint)rawTarget.TargetMask),
                rawTarget.SelectedTarget,
                rawTarget.TargetMode,
                rawTarget.TargetFlags,
                publicTargetActor.IsEnemy,
                publicTargetActor.Name);
        }

        return new Steam2026BattleResearchSnapshot(
            BattleStateReader.BattleModule,
            source.Encounter.FormationId,
            source.Encounter.LayoutType,
            source.Menu.Actor.ActorIndex,
            actors,
            menu,
            target);
    }

    private static Steam2026BattleActorResearchSnapshot CreatePublicActor(
        BattleActorSnapshot actor)
    {
        var redactPrivateEnemyState = actor.IsEnemy && !actor.InformationVisible;
        return new Steam2026BattleActorResearchSnapshot(
            actor.ActorIndex,
            actor.Name,
            actor.IsEnemy,
            true,
            actor.InformationVisible,
            redactPrivateEnemyState ? 0 : actor.CurrentHp,
            redactPrivateEnemyState ? 0 : actor.MaxHp,
            redactPrivateEnemyState ? 0 : actor.CurrentMp,
            redactPrivateEnemyState ? 0 : actor.MaxMp,
            redactPrivateEnemyState ? 0u : actor.StatusMask);
    }

    private static bool RawBattleEquals(RawBattleSnapshot left, RawBattleSnapshot right) =>
        left.Menu == right.Menu &&
        left.Actors.SequenceEqual(right.Actors) &&
        BattleEncounterEquals(left.Encounter, right.Encounter) &&
        left.Target == right.Target;

    private static bool BattleEncounterEquals(
        BattleEncounterSnapshot left,
        BattleEncounterSnapshot right) =>
        left.IsValid == right.IsValid &&
        left.FormationId == right.FormationId &&
        left.LayoutType == right.LayoutType &&
        left.Enemies.SequenceEqual(right.Enemies);

    private static bool ResultsOwnershipEquals(
        ResultsOwnershipSnapshot left,
        ResultsOwnershipSnapshot right) =>
        left.Module == right.Module &&
        left.State == right.State &&
        left.PageReady == right.PageReady &&
        left.Experience == right.Experience &&
        left.Ap == right.Ap &&
        left.Gil == right.Gil &&
        left.HasRewardItems == right.HasRewardItems &&
        left.RewardSelection == right.RewardSelection &&
        left.RewardTransition == right.RewardTransition &&
        left.InputEdges == right.InputEdges &&
        left.InputRepeat == right.InputRepeat &&
        left.HeldInput == right.HeldInput &&
        left.Rewards.SequenceEqual(right.Rewards);

    private static bool ResultsSnapshotEquals(
        BattleResultsSnapshot left,
        BattleResultsSnapshot right) =>
        left.IsValid == right.IsValid &&
        left.State == right.State &&
        left.Experience == right.Experience &&
        left.Ap == right.Ap &&
        left.Gil == right.Gil &&
        left.IsPageReady == right.IsPageReady &&
        left.HasRewardItems == right.HasRewardItems &&
        left.RewardSelection == right.RewardSelection &&
        left.RewardTransition == right.RewardTransition &&
        left.InputEdges == right.InputEdges &&
        left.InputRepeat == right.InputRepeat &&
        left.HeldInput == right.HeldInput &&
        left.Items.SequenceEqual(right.Items);

    private static bool IsSupportedRendererState(short rendererState) =>
        rendererState is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 0x18;

    private static bool TryAdd(uint address, int offset, out uint result)
    {
        result = 0;
        if (offset < 0)
        {
            return false;
        }

        var candidate = (ulong)address + (uint)offset;
        if (candidate > uint.MaxValue)
        {
            return false;
        }

        result = (uint)candidate;
        return true;
    }

    private static bool TryAddScaled(
        uint address,
        int index,
        int stride,
        out uint result)
    {
        result = 0;
        if (index < 0 || stride < 0)
        {
            return false;
        }

        var candidate = (ulong)address + (ulong)(uint)index * (uint)stride;
        if (candidate > uint.MaxValue)
        {
            return false;
        }

        result = (uint)candidate;
        return true;
    }

    private sealed class RawBattleSnapshot(
        BattleMenuStateSnapshot menu,
        ImmutableArray<BattleActorSnapshot> actors,
        BattleEncounterSnapshot encounter,
        BattleTargetSnapshot? target)
    {
        public BattleMenuStateSnapshot Menu { get; } = menu;

        public ImmutableArray<BattleActorSnapshot> Actors { get; } = actors;

        public BattleEncounterSnapshot Encounter { get; } = encounter;

        public BattleTargetSnapshot? Target { get; } = target;
    }

    private readonly record struct BattleOwnershipSnapshot(
        byte Module,
        byte CurrentActor,
        byte WindowState,
        ushort FormationId,
        byte LayoutType,
        int MenuTextState,
        ushort TargetMask,
        byte SelectedTarget,
        byte TargetMode,
        byte TargetFlags)
    {
        public bool TargetIsVisible => MenuTextState == 0;
    }

    private readonly record struct ActionOwnershipSnapshot(
        byte Module,
        byte EventIndex,
        byte Attacker,
        byte Kind,
        byte Command,
        ushort SceneIndex,
        ushort ActionId,
        ushort TargetMask,
        ushort FormationId);

    private readonly record struct RawRewardOwnership(
        ushort ItemId,
        ushort Quantity,
        ushort SelectedToTake);

    private sealed class ResultsOwnershipSnapshot(
        byte module,
        int state,
        byte pageReady,
        int experience,
        int ap,
        int gil,
        int hasRewardItems,
        int rewardSelection,
        short rewardTransition,
        int inputEdges,
        int inputRepeat,
        int heldInput,
        ImmutableArray<RawRewardOwnership> rewards)
    {
        public byte Module { get; } = module;

        public int State { get; } = state;

        public byte PageReady { get; } = pageReady;

        public int Experience { get; } = experience;

        public int Ap { get; } = ap;

        public int Gil { get; } = gil;

        public int HasRewardItems { get; } = hasRewardItems;

        public int RewardSelection { get; } = rewardSelection;

        public short RewardTransition { get; } = rewardTransition;

        public int InputEdges { get; } = inputEdges;

        public int InputRepeat { get; } = inputRepeat;

        public int HeldInput { get; } = heldInput;

        public ImmutableArray<RawRewardOwnership> Rewards { get; } = rewards;
    }

    private readonly record struct DamageOwnershipSnapshot(
        byte Module,
        ushort EffectIndex,
        byte State,
        short Value,
        int TargetActor,
        int Flags);

    private readonly record struct BattleTrackerOwnershipSnapshot(
        byte Module,
        ushort FormationId,
        byte LayoutType);
}

internal readonly record struct Steam2026BattleTrackerSnapshot(
    BattleEncounterSnapshot Encounter,
    ImmutableArray<BattleActorSnapshot> Actors,
    BattleEnemyActionSnapshot EnemyAction,
    BattleTargetSnapshot Target,
    bool RootCommandMenuActive,
    ImmutableArray<BattlePartyProgressSnapshot> PartyProgress);

internal readonly record struct Steam2026BattleDamageTrackerSnapshot(
    BattleDamagePopupSnapshot Popup,
    BattleActorSnapshot Actor,
    BattleActorVisibleCorrelation VisibleActor);

internal readonly record struct Steam2026BattleActionTextTrackerSnapshot(
    Steam2026BattleActionTextCommitSnapshot Commit,
    BattleActorSnapshot Actor,
    string ActionName);

internal readonly record struct Steam2026BattleResultsTrackerSnapshot(
    BattleResultsSnapshot Results,
    ImmutableArray<BattlePartyProgressSnapshot> PartyProgress);

public sealed class Steam2026BattleTextResolvers
{
    public Steam2026BattleTextResolvers(
        Func<int, string?> resolveAbilityName,
        Func<int, string?> resolveAbilityDescription,
        Func<int, string?> resolveItemName,
        Func<int, string?> resolveItemDescription,
        Func<int, string?> resolveCommandName,
        Func<int, string?> resolveInventoryObjectName,
        Func<int, string?>? resolveBattleText = null,
        Func<int, string?>? resolveLimitName = null,
        Func<int, string?>? resolveLimitDescription = null,
        Func<int, string?>? resolveInventoryObjectDescription = null,
        Func<int, string?>? resolveElementName = null,
        Ff7GameLanguageDescriptor? language = null)
    {
        ResolveAbilityName = resolveAbilityName ?? throw new ArgumentNullException(nameof(resolveAbilityName));
        ResolveAbilityDescription = resolveAbilityDescription ?? throw new ArgumentNullException(nameof(resolveAbilityDescription));
        ResolveItemName = resolveItemName ?? throw new ArgumentNullException(nameof(resolveItemName));
        ResolveItemDescription = resolveItemDescription ?? throw new ArgumentNullException(nameof(resolveItemDescription));
        ResolveCommandName = resolveCommandName ?? throw new ArgumentNullException(nameof(resolveCommandName));
        ResolveInventoryObjectName = resolveInventoryObjectName ?? throw new ArgumentNullException(nameof(resolveInventoryObjectName));
        ResolveInventoryObjectDescription = resolveInventoryObjectDescription ?? ResolveItemDescription;
        ResolveBattleText = resolveBattleText ?? (_ => null);
        ResolveLimitName = resolveLimitName ?? (_ => null);
        ResolveLimitDescription = resolveLimitDescription ?? (_ => null);
        ResolveElementName = resolveElementName;
        Language = language;
    }

    public Func<int, string?> ResolveAbilityName { get; }

    public Func<int, string?> ResolveAbilityDescription { get; }

    public Func<int, string?> ResolveItemName { get; }

    public Func<int, string?> ResolveItemDescription { get; }

    public Func<int, string?> ResolveCommandName { get; }

    public Func<int, string?> ResolveInventoryObjectName { get; }

    public Func<int, string?> ResolveInventoryObjectDescription { get; }

    public Func<int, string?> ResolveBattleText { get; }

    public Func<int, string?> ResolveLimitName { get; }

    public Func<int, string?> ResolveLimitDescription { get; }

    public Func<int, string?>? ResolveElementName { get; }

    public Ff7GameLanguageDescriptor? Language { get; }
}

public sealed class Steam2026BattleResearchSnapshot : IEquatable<Steam2026BattleResearchSnapshot>
{
    internal Steam2026BattleResearchSnapshot(
        int module,
        int formationId,
        int layoutType,
        int readyActorId,
        ImmutableArray<Steam2026BattleActorResearchSnapshot> actors,
        Steam2026BattleMenuResearchSnapshot menu,
        Steam2026BattleTargetResearchSnapshot? target)
    {
        Module = module;
        FormationId = formationId;
        LayoutType = layoutType;
        ReadyActorId = readyActorId;
        Actors = actors;
        Menu = menu;
        Target = target;
    }

    public int Module { get; }

    public int FormationId { get; }

    public int LayoutType { get; }

    public int ReadyActorId { get; }

    public ImmutableArray<Steam2026BattleActorResearchSnapshot> Actors { get; }

    public Steam2026BattleMenuResearchSnapshot Menu { get; }

    public Steam2026BattleTargetResearchSnapshot? Target { get; }

    public bool Equals(Steam2026BattleResearchSnapshot? other) =>
        other is not null &&
        Module == other.Module &&
        FormationId == other.FormationId &&
        LayoutType == other.LayoutType &&
        ReadyActorId == other.ReadyActorId &&
        Actors.SequenceEqual(other.Actors) &&
        Menu.Equals(other.Menu) &&
        Equals(Target, other.Target);

    public override bool Equals(object? obj) =>
        obj is Steam2026BattleResearchSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Module);
        hash.Add(FormationId);
        hash.Add(LayoutType);
        hash.Add(ReadyActorId);
        foreach (var actor in Actors)
        {
            hash.Add(actor);
        }

        hash.Add(Menu);
        hash.Add(Target);
        return hash.ToHashCode();
    }
}

public sealed class Steam2026BattleActorResearchSnapshot : IEquatable<Steam2026BattleActorResearchSnapshot>
{
    internal Steam2026BattleActorResearchSnapshot(
        int actorId,
        string name,
        bool isEnemy,
        bool isActive,
        bool isSensed,
        int currentHp,
        int maximumHp,
        int currentMp,
        int maximumMp,
        uint statusMask)
    {
        ActorId = actorId;
        Name = name;
        IsEnemy = isEnemy;
        IsActive = isActive;
        IsSensed = isSensed;
        CurrentHp = currentHp;
        MaximumHp = maximumHp;
        CurrentMp = currentMp;
        MaximumMp = maximumMp;
        StatusMask = statusMask;
    }

    public int ActorId { get; }

    public string Name { get; }

    public bool IsEnemy { get; }

    public bool IsActive { get; }

    public bool IsSensed { get; }

    public int CurrentHp { get; }

    public int MaximumHp { get; }

    public int CurrentMp { get; }

    public int MaximumMp { get; }

    public uint StatusMask { get; }

    public bool Equals(Steam2026BattleActorResearchSnapshot? other) =>
        other is not null &&
        ActorId == other.ActorId &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        IsEnemy == other.IsEnemy &&
        IsActive == other.IsActive &&
        IsSensed == other.IsSensed &&
        CurrentHp == other.CurrentHp &&
        MaximumHp == other.MaximumHp &&
        CurrentMp == other.CurrentMp &&
        MaximumMp == other.MaximumMp &&
        StatusMask == other.StatusMask;

    public override bool Equals(object? obj) =>
        obj is Steam2026BattleActorResearchSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        ActorId,
        Name,
        IsEnemy,
        IsActive,
        IsSensed,
        CurrentHp,
        MaximumHp,
        HashCode.Combine(CurrentMp, MaximumMp, StatusMask));
}

public sealed class Steam2026BattleMenuResearchSnapshot : IEquatable<Steam2026BattleMenuResearchSnapshot>
{
    internal Steam2026BattleMenuResearchSnapshot(
        short rendererState,
        int partySlot,
        Steam2026BattleSelectionResearchSnapshot selection)
    {
        RendererState = rendererState;
        PartySlot = partySlot;
        Selection = selection;
    }

    public short RendererState { get; }

    public int PartySlot { get; }

    public Steam2026BattleSelectionResearchSnapshot Selection { get; }

    public bool Equals(Steam2026BattleMenuResearchSnapshot? other) =>
        other is not null &&
        RendererState == other.RendererState &&
        PartySlot == other.PartySlot &&
        Selection.Equals(other.Selection);

    public override bool Equals(object? obj) =>
        obj is Steam2026BattleMenuResearchSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(RendererState, PartySlot, Selection);
}

public sealed class Steam2026BattleSelectionResearchSnapshot : IEquatable<Steam2026BattleSelectionResearchSnapshot>
{
    internal Steam2026BattleSelectionResearchSnapshot(
        int entryId,
        string name,
        string? description,
        int? quantity,
        int? mpCost,
        bool isAvailable)
    {
        EntryId = entryId;
        Name = name;
        Description = description;
        Quantity = quantity;
        MpCost = mpCost;
        IsAvailable = isAvailable;
    }

    public int EntryId { get; }

    public string Name { get; }

    public string? Description { get; }

    public int? Quantity { get; }

    public int? MpCost { get; }

    public bool IsAvailable { get; }

    public bool Equals(Steam2026BattleSelectionResearchSnapshot? other) =>
        other is not null &&
        EntryId == other.EntryId &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        string.Equals(Description, other.Description, StringComparison.Ordinal) &&
        Quantity == other.Quantity &&
        MpCost == other.MpCost &&
        IsAvailable == other.IsAvailable;

    public override bool Equals(object? obj) =>
        obj is Steam2026BattleSelectionResearchSnapshot other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(EntryId, Name, Description, Quantity, MpCost, IsAvailable);
}

public sealed class Steam2026BattleTargetResearchSnapshot : IEquatable<Steam2026BattleTargetResearchSnapshot>
{
    internal Steam2026BattleTargetResearchSnapshot(
        uint targetMask,
        int actorId,
        int mode,
        int flags,
        bool isEnemy,
        string name)
    {
        TargetMask = targetMask;
        ActorId = actorId;
        Mode = mode;
        Flags = flags;
        IsEnemy = isEnemy;
        Name = name;
    }

    public uint TargetMask { get; }

    public int ActorId { get; }

    public int Mode { get; }

    public int Flags { get; }

    public bool IsEnemy { get; }

    public string Name { get; }

    public bool Equals(Steam2026BattleTargetResearchSnapshot? other) =>
        other is not null &&
        TargetMask == other.TargetMask &&
        ActorId == other.ActorId &&
        Mode == other.Mode &&
        Flags == other.Flags &&
        IsEnemy == other.IsEnemy &&
        string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is Steam2026BattleTargetResearchSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TargetMask, ActorId, Mode, Flags, IsEnemy, Name);
}

public sealed class Steam2026BattleActionResearchSnapshot : IEquatable<Steam2026BattleActionResearchSnapshot>
{
    internal Steam2026BattleActionResearchSnapshot(
        int eventIndex,
        int attackerActorId,
        int commandId,
        int sceneAttackIndex,
        int actionId,
        uint targetMask,
        string actionName,
        string? accessibilityDescription,
        int formationId)
    {
        EventIndex = eventIndex;
        AttackerActorId = attackerActorId;
        CommandId = commandId;
        SceneAttackIndex = sceneAttackIndex;
        ActionId = actionId;
        TargetMask = targetMask;
        ActionName = actionName;
        AccessibilityDescription = accessibilityDescription;
        FormationId = formationId;
    }

    public int EventIndex { get; }

    public int AttackerActorId { get; }

    public int CommandId { get; }

    public int SceneAttackIndex { get; }

    public int ActionId { get; }

    public uint TargetMask { get; }

    public string ActionName { get; }

    public string? AccessibilityDescription { get; }

    public int FormationId { get; }

    public bool Equals(Steam2026BattleActionResearchSnapshot? other) =>
        other is not null &&
        EventIndex == other.EventIndex &&
        AttackerActorId == other.AttackerActorId &&
        CommandId == other.CommandId &&
        SceneAttackIndex == other.SceneAttackIndex &&
        ActionId == other.ActionId &&
        TargetMask == other.TargetMask &&
        string.Equals(ActionName, other.ActionName, StringComparison.Ordinal) &&
        string.Equals(AccessibilityDescription, other.AccessibilityDescription, StringComparison.Ordinal) &&
        FormationId == other.FormationId;

    public override bool Equals(object? obj) =>
        obj is Steam2026BattleActionResearchSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        EventIndex,
        AttackerActorId,
        CommandId,
        SceneAttackIndex,
        ActionId,
        TargetMask,
        ActionName,
        HashCode.Combine(AccessibilityDescription, FormationId));
}

public sealed class Steam2026BattleResultsResearchSnapshot : IEquatable<Steam2026BattleResultsResearchSnapshot>
{
    internal Steam2026BattleResultsResearchSnapshot(
        int state,
        int experience,
        int ap,
        int gil,
        bool isPageReady,
        bool hasRewardItems,
        int rewardSelection,
        short rewardTransition,
        int inputEdges,
        int inputRepeat,
        int heldInput,
        ImmutableArray<Steam2026BattleRewardResearchSnapshot> rewards)
    {
        State = state;
        Experience = experience;
        Ap = ap;
        Gil = gil;
        IsPageReady = isPageReady;
        HasRewardItems = hasRewardItems;
        RewardSelection = rewardSelection;
        RewardTransition = rewardTransition;
        InputEdges = inputEdges;
        InputRepeat = inputRepeat;
        HeldInput = heldInput;
        Rewards = rewards;
    }

    public int State { get; }

    public int Experience { get; }

    public int Ap { get; }

    public int Gil { get; }

    public bool IsPageReady { get; }

    public bool HasRewardItems { get; }

    public int RewardSelection { get; }

    public short RewardTransition { get; }

    public int InputEdges { get; }

    public int InputRepeat { get; }

    public int HeldInput { get; }

    public ImmutableArray<Steam2026BattleRewardResearchSnapshot> Rewards { get; }

    public bool Equals(Steam2026BattleResultsResearchSnapshot? other) =>
        other is not null &&
        State == other.State &&
        Experience == other.Experience &&
        Ap == other.Ap &&
        Gil == other.Gil &&
        IsPageReady == other.IsPageReady &&
        HasRewardItems == other.HasRewardItems &&
        RewardSelection == other.RewardSelection &&
        RewardTransition == other.RewardTransition &&
        InputEdges == other.InputEdges &&
        InputRepeat == other.InputRepeat &&
        HeldInput == other.HeldInput &&
        Rewards.SequenceEqual(other.Rewards);

    public override bool Equals(object? obj) =>
        obj is Steam2026BattleResultsResearchSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(State);
        hash.Add(Experience);
        hash.Add(Ap);
        hash.Add(Gil);
        hash.Add(IsPageReady);
        hash.Add(HasRewardItems);
        hash.Add(RewardSelection);
        hash.Add(RewardTransition);
        hash.Add(InputEdges);
        hash.Add(InputRepeat);
        hash.Add(HeldInput);
        foreach (var reward in Rewards)
        {
            hash.Add(reward);
        }

        return hash.ToHashCode();
    }
}

public sealed class Steam2026BattleRewardResearchSnapshot : IEquatable<Steam2026BattleRewardResearchSnapshot>
{
    internal Steam2026BattleRewardResearchSnapshot(
        int itemId,
        int quantity,
        string name,
        int physicalSlot,
        bool isSelectedToTake)
    {
        ItemId = itemId;
        Quantity = quantity;
        Name = name;
        PhysicalSlot = physicalSlot;
        IsSelectedToTake = isSelectedToTake;
    }

    public int ItemId { get; }

    public int Quantity { get; }

    public string Name { get; }

    public int PhysicalSlot { get; }

    public bool IsSelectedToTake { get; }

    public bool Equals(Steam2026BattleRewardResearchSnapshot? other) =>
        other is not null &&
        ItemId == other.ItemId &&
        Quantity == other.Quantity &&
        PhysicalSlot == other.PhysicalSlot &&
        IsSelectedToTake == other.IsSelectedToTake &&
        string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is Steam2026BattleRewardResearchSnapshot other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(ItemId, Quantity, Name, PhysicalSlot, IsSelectedToTake);
}

public sealed class Steam2026BattleDamageResearchSnapshot : IEquatable<Steam2026BattleDamageResearchSnapshot>
{
    internal Steam2026BattleDamageResearchSnapshot(
        int effectIndex,
        int targetActorId,
        int value,
        int flags,
        bool isMiss)
    {
        EffectIndex = effectIndex;
        TargetActorId = targetActorId;
        Value = value;
        Flags = flags;
        IsMiss = isMiss;
    }

    public int EffectIndex { get; }

    public int TargetActorId { get; }

    public int Value { get; }

    public int Flags { get; }

    public bool IsMiss { get; }

    public bool Equals(Steam2026BattleDamageResearchSnapshot? other) =>
        other is not null &&
        EffectIndex == other.EffectIndex &&
        TargetActorId == other.TargetActorId &&
        Value == other.Value &&
        Flags == other.Flags &&
        IsMiss == other.IsMiss;

    public override bool Equals(object? obj) =>
        obj is Steam2026BattleDamageResearchSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(EffectIndex, TargetActorId, Value, Flags, IsMiss);
}
