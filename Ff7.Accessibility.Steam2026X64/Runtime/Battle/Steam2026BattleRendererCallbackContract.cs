using System.Collections.Immutable;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

internal static class Steam2026BattleActionTextMemory
{
    internal const uint AddressActiveActor = 0x00BE1170;
    internal const uint AddressBattleModelCommand = 0x00BE119B;
    internal const int BattleModelStateSize = 0x1AEC;
    internal const uint AddressSmallBattleModelAction = 0x00BF23FE;
    internal const int SmallBattleModelStateSize = 0x74;
    internal const uint AddressEffectIndex = 0x00BF23B8;
    internal const uint AddressEffectData = 0x00BFB718;
    internal const int EffectRecordSize = 0x20;
    internal const int EffectCount = 100;
    internal const int RemainingFramesOffset = 0x04;
}

internal readonly record struct Steam2026BattleActionTextCommitSnapshot(
    bool IsValid,
    ushort EffectIndex,
    byte ActorIndex,
    byte CommandId,
    ushort ActionId,
    short RemainingFrames)
{
    internal static Steam2026BattleActionTextCommitSnapshot Invalid { get; } =
        new(false, 0, 0, 0, 0, 0);
}

internal readonly record struct Steam2026BattleEnemyActionIngressSnapshot(
    bool WasCaptured,
    BattleEnemyActionSnapshot Action,
    BattleActorSnapshot Attacker,
    Steam2026BattleRawEnemyActionIngressSnapshot Raw = default)
{
    internal static Steam2026BattleEnemyActionIngressSnapshot NotCaptured { get; } =
        new(false, BattleEnemyActionSnapshot.Invalid, default, default);
}

internal readonly record struct Steam2026BattleRawEnemyActionIngressSnapshot(
    bool IsCoherent,
    byte EventIndex,
    byte AttackerActorIndex,
    byte EventKind,
    byte CommandId,
    ushort SceneAttackIndex)
{
    internal bool IsActionCandidate =>
        IsCoherent
        && EventIndex < BattleStateReader.AnimationEventCount
        && AttackerActorIndex is >= 4 and <= 9
        && EventKind == BattleStateReader.ActionAnimationEventKind
        && CommandId == BattleStateReader.EnemyActionCommandId
        && SceneAttackIndex < BattleStateReader.SceneAttackCount;
}

internal readonly record struct Steam2026BattleVictoryIngressSnapshot(
    bool WasCaptured,
    bool IsVictory)
{
    internal static Steam2026BattleVictoryIngressSnapshot NotCaptured { get; } =
        new(false, false);
}

internal readonly record struct Steam2026BattleRewardIngressSnapshot(
    ushort ItemId,
    ushort Quantity,
    ushort SelectedToTake);

internal readonly record struct Steam2026BattleResultsIngressSnapshot(
    bool WasCaptured,
    int State,
    int Experience,
    int Ap,
    int Gil,
    bool IsPageReady,
    bool HasRewardItems,
    int RewardSelection,
    short RewardTransition,
    int InputEdges,
    int InputRepeat,
    int HeldInput,
    ImmutableArray<Steam2026BattleRewardIngressSnapshot> Rewards,
    ImmutableArray<BattlePartyProgressSnapshot> PartyProgress)
{
    internal static Steam2026BattleResultsIngressSnapshot NotCaptured { get; } =
        new(
            false,
            0,
            0,
            0,
            0,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            ImmutableArray<Steam2026BattleRewardIngressSnapshot>.Empty,
            ImmutableArray<BattlePartyProgressSnapshot>.Empty);
}

/// <summary>
/// Owns the exact battle callback cohort and captures its bounded legacy
/// arguments and transient damage-popup record without installing hooks.
/// </summary>
internal sealed class Steam2026BattleRendererCallbackContract
{
    private const long HookLeaseHealthProbeIntervalMilliseconds = 1000;

    private readonly object hookLeaseLock = new();
    private readonly ulong moduleBase;
    private readonly INativeMemoryReader memory;
    private readonly Steam2026BattleRendererCallbackCatalog catalog;
    private readonly TranslatedX86CallFrameReader frame;
    private readonly TranslatedX86AddressSpace addressSpace;
    private readonly Ff7.Accessibility.Reloaded.BattleDamagePopupReader damageReader;
    private readonly BattleStateReader battleStateReader;
    private readonly TifaSlotResultReader tifaSlotReader;
    private ActiveHookLease? activeHookLease;
    private long validationEpoch;
    private long nextHookLeaseHealthProbeMilliseconds;
    private int hookLeaseUnhealthy;

    internal Steam2026BattleRendererCallbackContract(
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory)
        : this(
            moduleBase,
            moduleImageSize,
            memory,
            CreateResearchAddressSpace(moduleBase, memory),
            hasExactSupportedFingerprint: false)
    {
    }

    internal Steam2026BattleRendererCallbackContract(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory)
        : this(
            moduleBase,
            moduleImageSize,
            memory,
            ValidatedTranslatedX86AddressSpaceFactory.Create(
                fingerprint,
                moduleBase,
                memory),
            hasExactSupportedFingerprint: true)
    {
    }

    private Steam2026BattleRendererCallbackContract(
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        TranslatedX86AddressSpace addressSpace,
        bool hasExactSupportedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(memory);
        this.moduleBase = moduleBase;
        this.memory = memory;
        var validator = new TranslatedFunctionMapValidator(
            moduleBase,
            moduleImageSize,
            memory);
        catalog = new Steam2026BattleRendererCallbackCatalog(validator);
        this.addressSpace = addressSpace;
        frame = new TranslatedX86CallFrameReader(moduleBase, memory, addressSpace);
        damageReader = new Ff7.Accessibility.Reloaded.BattleDamagePopupReader(addressSpace);
        battleStateReader = new BattleStateReader(
            addressSpace,
            new SavemapPartyReader(addressSpace));
        tifaSlotReader = new TifaSlotResultReader(addressSpace);
        HasExactSupportedFingerprint = hasExactSupportedFingerprint;
    }

    internal bool HasExactSupportedFingerprint { get; }

    internal void ActivateHookLease(
        Func<Steam2026BattleRendererCallbackKind, bool> isCohortEnabled)
    {
        ArgumentNullException.ThrowIfNull(isCohortEnabled);
        lock (hookLeaseLock)
        {
            if (activeHookLease is not null)
            {
                throw new InvalidOperationException(
                    "A translated battle-renderer hook lease is already active.");
            }

            if (!HasExactSupportedFingerprint)
            {
                throw new InvalidOperationException(
                    "A translated battle-renderer hook lease requires the exact fingerprint.");
            }

            var leasedIdentities = new Dictionary<
                Steam2026BattleRendererCallbackKind,
                Steam2026BattleRendererCallbackIdentity>();
            foreach (var kind in CaptureKinds)
            {
                if (!IsEnabled(isCohortEnabled, kind)
                    || !catalog.TryValidateMappedIdentity(kind, out var identity)
                    || identity.Metadata.Kind != kind
                    || identity.Metadata.HostAbi
                        != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments)
                {
                    throw new InvalidOperationException(
                        $"The active translated battle callback cohort is incomplete at {kind}.");
                }

                leasedIdentities.Add(kind, identity);
            }

            if (!TryCreateRawActionCapturePlan(out var rawActionPlan))
            {
                throw new InvalidOperationException(
                    "The translated battle page-table contract is unavailable for bounded native capture.");
            }

            var generation = Interlocked.Increment(ref validationEpoch);
            var lease = new ActiveHookLease(
                generation,
                leasedIdentities,
                isCohortEnabled,
                rawActionPlan);
            Volatile.Write(ref nextHookLeaseHealthProbeMilliseconds, 0);
            Volatile.Write(ref hookLeaseUnhealthy, 0);
            Volatile.Write(ref activeHookLease, lease);
        }
    }

    internal void ActivateHookLease(Func<bool> isHookEnabled) =>
        ActivateHookLease(_ => IsEnabled(isHookEnabled));

    internal void RevokeHookLease()
    {
        lock (hookLeaseLock)
        {
            Volatile.Write(ref activeHookLease, null);
            Interlocked.Increment(ref validationEpoch);
            Volatile.Write(ref nextHookLeaseHealthProbeMilliseconds, 0);
            Volatile.Write(ref hookLeaseUnhealthy, 0);
        }
    }

    /// <summary>
    /// Revalidates cached hook and raw-page ownership from the managed worker
    /// at most once per second. Native callbacks never call this full probe.
    /// </summary>
    internal bool IsActiveHookLeaseHealthy(long monotonicMilliseconds)
    {
        if (Volatile.Read(ref hookLeaseUnhealthy) != 0)
        {
            return false;
        }

        var lease = Volatile.Read(ref activeHookLease);
        if (lease is null)
        {
            return false;
        }

        var nextProbe = Volatile.Read(ref nextHookLeaseHealthProbeMilliseconds);
        if (monotonicMilliseconds < nextProbe)
        {
            return true;
        }

        var followingProbe = checked(
            monotonicMilliseconds + HookLeaseHealthProbeIntervalMilliseconds);
        if (Interlocked.CompareExchange(
                ref nextHookLeaseHealthProbeMilliseconds,
                followingProbe,
                nextProbe) != nextProbe)
        {
            return Volatile.Read(ref hookLeaseUnhealthy) == 0;
        }

        var healthy = TryValidateActiveHookLease(lease);
        if (!ReferenceEquals(lease, Volatile.Read(ref activeHookLease)))
        {
            return true;
        }

        if (!healthy)
        {
            Interlocked.Exchange(ref hookLeaseUnhealthy, 1);
        }

        return healthy;
    }

    internal bool TryValidateCaptureIdentity(
        out Steam2026BattleRendererCallbackIdentity identity)
        => TryValidateCaptureIdentity(
            Steam2026BattleRendererCallbackKind.MenuRenderer,
            out identity);

    internal bool TryValidateCaptureIdentity(
        Steam2026BattleRendererCallbackKind kind,
        out Steam2026BattleRendererCallbackIdentity identity)
    {
        identity = default;
        if (!catalog.TryValidateIdentity(kind, out var candidate)
            || candidate.Metadata.Kind != kind
            || candidate.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments)
        {
            return false;
        }

        identity = candidate;
        return true;
    }

    internal bool IsCurrentCaptureIdentity(
        Steam2026BattleRendererCallbackIdentity identity) =>
        identity.Metadata.HostAbi
            == TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
        && TryResolveCurrentIdentity(identity.Metadata.Kind, out var current, out _)
        && current == identity;

    internal bool TryCaptureRendererState(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out short rendererState) =>
        TryCaptureSignedArgument(
            expectedIdentity,
            Steam2026BattleRendererCallbackKind.MenuRenderer,
            argumentIndex: 1,
            Steam2026BattleRendererState.IsCapturable,
            out rendererState);

    internal bool TryCaptureTifaSlots(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out TifaSlotResultSnapshot snapshot)
    {
        snapshot = TifaSlotResultSnapshot.Invalid;
        if (expectedIdentity.Metadata.Kind
                != Steam2026BattleRendererCallbackKind.MenuRenderer
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.MenuRenderer,
                out var beforeIdentity,
                out var beforeGeneration)
            || beforeIdentity != expectedIdentity)
        {
            return false;
        }

        try
        {
            var candidate = tifaSlotReader.Read();
            if (!candidate.IsValid
                || !TryResolveCurrentIdentity(
                    Steam2026BattleRendererCallbackKind.MenuRenderer,
                    out var afterIdentity,
                    out var afterGeneration)
                || afterGeneration != beforeGeneration
                || afterIdentity != expectedIdentity)
            {
                return false;
            }

            snapshot = candidate;
            return true;
        }
        catch
        {
            snapshot = TifaSlotResultSnapshot.Invalid;
            return false;
        }
    }

    internal bool TryCaptureCommittedTifaSlots(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out TifaSlotCommittedResultSnapshot snapshot)
    {
        snapshot = TifaSlotCommittedResultSnapshot.Invalid;
        if (expectedIdentity.Metadata.Kind
                != Steam2026BattleRendererCallbackKind.BattleUpdate
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                out var beforeIdentity,
                out var beforeGeneration)
            || beforeIdentity != expectedIdentity)
        {
            return false;
        }

        try
        {
            var candidate = tifaSlotReader.ReadCommitted();
            if (!candidate.IsValid
                || !TryResolveCurrentIdentity(
                    Steam2026BattleRendererCallbackKind.BattleUpdate,
                    out var afterIdentity,
                    out var afterGeneration)
                || afterGeneration != beforeGeneration
                || afterIdentity != expectedIdentity)
            {
                return false;
            }

            snapshot = candidate;
            return true;
        }
        catch
        {
            snapshot = TifaSlotCommittedResultSnapshot.Invalid;
            return false;
        }
    }

    internal bool TryCaptureTifaSlotWindowState(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out byte windowState)
    {
        windowState = byte.MaxValue;
        if (expectedIdentity.Metadata.Kind
                != Steam2026BattleRendererCallbackKind.BattleUpdate
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments)
        {
            return false;
        }

        var address = unchecked((uint)(BattleStateReader.AddressMenuWindowStates + 0x1B));
        if (!addressSpace.TryReadByte(address, out var first)
            || !addressSpace.TryReadByte(address, out var second)
            || first != second)
        {
            return false;
        }

        windowState = second;
        return true;
    }

    internal bool TryCaptureTextBufferIndex(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out short bufferIndex) =>
        TryCaptureSignedArgument(
            expectedIdentity,
            Steam2026BattleRendererCallbackKind.TextActivation,
            argumentIndex: 0,
            static _ => true,
             out bufferIndex);

    internal bool TryCaptureVictorySignal(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out Steam2026BattleVictoryIngressSnapshot snapshot)
    {
        snapshot = Steam2026BattleVictoryIngressSnapshot.NotCaptured;
        if (expectedIdentity.Metadata.Kind
                != Steam2026BattleRendererCallbackKind.BattleUpdate
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                out var beforeIdentity,
                out var beforeGeneration)
            || beforeIdentity != expectedIdentity)
        {
            return false;
        }

        try
        {
            if (!battleStateReader.TryReadVictorySignal(out var isVictory)
                || !TryResolveCurrentIdentity(
                    Steam2026BattleRendererCallbackKind.BattleUpdate,
                    out var afterIdentity,
                    out var afterGeneration)
                || afterGeneration != beforeGeneration
                || afterIdentity != expectedIdentity)
            {
                return false;
            }

            snapshot = new Steam2026BattleVictoryIngressSnapshot(true, isVictory);
            return true;
        }
        catch
        {
            snapshot = Steam2026BattleVictoryIngressSnapshot.NotCaptured;
            return false;
        }
    }

    internal bool TryCaptureResults(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out Steam2026BattleResultsIngressSnapshot snapshot)
    {
        snapshot = Steam2026BattleResultsIngressSnapshot.NotCaptured;
        if (expectedIdentity.Metadata.Kind
                != Steam2026BattleRendererCallbackKind.ResultsUpdate
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.ResultsUpdate,
                out var beforeIdentity,
                out var beforeGeneration)
            || beforeIdentity != expectedIdentity)
        {
            return false;
        }

        try
        {
            if (!TryReadResultsLifecycle(out var before)
                || !battleStateReader.TryReadPartyProgress(out var progress)
                || progress.Count == 0
                || !TryReadResultsLifecycle(out var after)
                || !ResultsLifecycleEquals(before, after)
                || !TryResolveCurrentIdentity(
                    Steam2026BattleRendererCallbackKind.ResultsUpdate,
                    out var afterIdentity,
                    out var afterGeneration)
                || afterGeneration != beforeGeneration
                || afterIdentity != expectedIdentity)
            {
                return false;
            }

            snapshot = before with { PartyProgress = progress.ToImmutableArray() };
            return true;
        }
        catch
        {
            snapshot = Steam2026BattleResultsIngressSnapshot.NotCaptured;
            return false;
        }
    }

    internal bool TryCaptureActionTextCommit(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out Steam2026BattleActionTextCommitSnapshot snapshot)
    {
        snapshot = Steam2026BattleActionTextCommitSnapshot.Invalid;
        if (expectedIdentity.Metadata.Kind
                != Steam2026BattleRendererCallbackKind.ActionTextCommit
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.ActionTextCommit,
                out var beforeIdentity,
                out var beforeGeneration)
            || beforeIdentity != expectedIdentity
            || !frame.TryReadEsp(out var guestEsp)
            || guestEsp == 0)
        {
            return false;
        }

        try
        {
            if (!TryReadActionTextCommitCandidate(guestEsp, out var first)
                || !frame.TryReadEsp(out var middleEsp)
                || middleEsp != guestEsp
                || !TryReadActionTextCommitCandidate(guestEsp, out var second)
                || first != second
                || !frame.TryReadEsp(out var afterEsp)
                || afterEsp != guestEsp
                || !TryResolveCurrentIdentity(
                    Steam2026BattleRendererCallbackKind.ActionTextCommit,
                    out var afterIdentity,
                    out var afterGeneration)
                || afterGeneration != beforeGeneration
                || afterIdentity != expectedIdentity)
            {
                return false;
            }

            snapshot = first;
            return true;
        }
        catch
        {
            snapshot = Steam2026BattleActionTextCommitSnapshot.Invalid;
            return false;
        }
    }

    internal bool TryCaptureDamagePopup(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out Ff7.Accessibility.Reloaded.BattleDamagePopupSnapshot popup)
    {
        popup = Ff7.Accessibility.Reloaded.BattleDamagePopupSnapshot.Invalid;
        if (expectedIdentity.Metadata.Kind
                != Steam2026BattleRendererCallbackKind.DamageDisplay
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                out var beforeIdentity,
                out var beforeGeneration)
            || beforeIdentity != expectedIdentity)
        {
            return false;
        }

        Ff7.Accessibility.Reloaded.BattleDamagePopupSnapshot candidate;
        try
        {
            candidate = damageReader.Read();
        }
        catch
        {
            return false;
        }

        if (!candidate.IsValid
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.DamageDisplay,
                out var afterIdentity,
                out var afterGeneration)
            || afterGeneration != beforeGeneration
            || afterIdentity != expectedIdentity)
        {
            return false;
        }

        popup = candidate;
        return true;
    }

    internal bool TryCaptureEnemyAction(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out Steam2026BattleEnemyActionIngressSnapshot snapshot)
    {
        snapshot = Steam2026BattleEnemyActionIngressSnapshot.NotCaptured;
        if (expectedIdentity.Metadata.Kind
                != Steam2026BattleRendererCallbackKind.BattleUpdate
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                out var beforeIdentity,
                out var beforeGeneration)
            || beforeIdentity != expectedIdentity)
        {
            return false;
        }

        try
        {
            var action = battleStateReader.ReadCurrentEnemyAction();
            var attacker = default(BattleActorSnapshot);
            if (action.IsValid
                && (!battleStateReader.TryReadBattleActor(
                        action.AttackerActorIndex,
                        out attacker)
                    || attacker.ActorIndex != action.AttackerActorIndex
                    || !attacker.IsEnemy))
            {
                return false;
            }

            if (!TryResolveCurrentIdentity(
                    Steam2026BattleRendererCallbackKind.BattleUpdate,
                    out var afterIdentity,
                    out var afterGeneration)
                || afterGeneration != beforeGeneration
                || afterIdentity != expectedIdentity)
            {
                return false;
            }

            snapshot = new Steam2026BattleEnemyActionIngressSnapshot(
                true,
                action,
                attacker);
            return true;
        }
        catch
        {
            snapshot = Steam2026BattleEnemyActionIngressSnapshot.NotCaptured;
            return false;
        }
    }

    internal bool TryCaptureRawEnemyAction(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        out Steam2026BattleEnemyActionIngressSnapshot snapshot)
    {
        snapshot = Steam2026BattleEnemyActionIngressSnapshot.NotCaptured;
        if (expectedIdentity.Metadata.Kind
                != Steam2026BattleRendererCallbackKind.BattleUpdate
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                out var beforeIdentity,
                out var beforeGeneration)
            || beforeIdentity != expectedIdentity
            || !TryGetRawActionCapturePlan(out var plan)
            || !TryReadRawEnemyAction(plan, out var raw)
            || !TryResolveCurrentIdentity(
                Steam2026BattleRendererCallbackKind.BattleUpdate,
                out var afterIdentity,
                out var afterGeneration)
            || afterGeneration != beforeGeneration
            || afterIdentity != expectedIdentity)
        {
            return false;
        }

        snapshot = new Steam2026BattleEnemyActionIngressSnapshot(
            true,
            BattleEnemyActionSnapshot.Invalid,
            default,
            raw);
        return true;
    }

    private bool TryCaptureSignedArgument(
        Steam2026BattleRendererCallbackIdentity expectedIdentity,
        Steam2026BattleRendererCallbackKind expectedKind,
        int argumentIndex,
        Func<short, bool> isSupported,
        out short value)
    {
        value = default;
        if (expectedIdentity.Metadata.Kind != expectedKind
            || expectedIdentity.Metadata.HostAbi
                != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
            || !TryResolveCurrentIdentity(
                expectedKind,
                out var beforeIdentity,
                out var beforeGeneration)
            || beforeIdentity != expectedIdentity
            || !frame.TryReadEsp(out var guestEsp)
            || guestEsp == 0
            || !frame.TryReadArgumentAtEsp(guestEsp, argumentIndex, out var firstRaw)
            || !frame.TryReadEsp(out var middleEsp)
            || middleEsp != guestEsp
            || !frame.TryReadArgumentAtEsp(guestEsp, argumentIndex, out var secondRaw)
            || secondRaw != firstRaw
            || !frame.TryReadEsp(out var afterEsp)
            || afterEsp != guestEsp)
        {
            return false;
        }

        var candidate = unchecked((short)firstRaw);
        if (!isSupported(candidate)
            || !TryResolveCurrentIdentity(
                expectedKind,
                out var afterIdentity,
                out var afterGeneration)
            || afterGeneration != beforeGeneration
            || afterIdentity != expectedIdentity)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private bool TryReadActionTextCommitCandidate(
        uint guestEsp,
        out Steam2026BattleActionTextCommitSnapshot snapshot)
    {
        snapshot = Steam2026BattleActionTextCommitSnapshot.Invalid;
        if (!frame.TryReadArgumentAtEsp(guestEsp, 0, out var rawCommand)
            || !frame.TryReadArgumentAtEsp(guestEsp, 1, out var rawAction)
            || !addressSpace.TryReadByte(
                (uint)BattleStateReader.AddressCurrentModule,
                out var module)
            || module != BattleStateReader.BattleModule
            || !addressSpace.TryReadByte(
                Steam2026BattleActionTextMemory.AddressActiveActor,
                out var actorIndex)
            || actorIndex is not (>= 0 and <= 2) and not (>= 4 and <= 9)
            || !TryAddScaled(
                Steam2026BattleActionTextMemory.AddressBattleModelCommand,
                actorIndex,
                Steam2026BattleActionTextMemory.BattleModelStateSize,
                out var commandAddress)
            || !TryAddScaled(
                Steam2026BattleActionTextMemory.AddressSmallBattleModelAction,
                actorIndex,
                Steam2026BattleActionTextMemory.SmallBattleModelStateSize,
                out var actionAddress)
            || !addressSpace.TryReadByte(commandAddress, out var stateCommand)
            || !addressSpace.TryReadUInt16(actionAddress, out var stateAction)
            || !addressSpace.TryReadUInt16(
                Steam2026BattleActionTextMemory.AddressEffectIndex,
                out var effectIndex)
            || effectIndex >= Steam2026BattleActionTextMemory.EffectCount
            || !TryAddScaled(
                Steam2026BattleActionTextMemory.AddressEffectData,
                effectIndex,
                Steam2026BattleActionTextMemory.EffectRecordSize,
                out var effectAddress)
            || !TryAdd(
                effectAddress,
                Steam2026BattleActionTextMemory.RemainingFramesOffset,
                out var remainingFramesAddress)
            || !addressSpace.TryReadInt16(
                remainingFramesAddress,
                out var remainingFrames)
            || remainingFrames <= 0)
        {
            return false;
        }

        var commandId = unchecked((byte)rawCommand);
        var actionId = unchecked((ushort)rawAction);
        if (commandId != stateCommand || actionId != stateAction)
        {
            return false;
        }

        snapshot = new Steam2026BattleActionTextCommitSnapshot(
            true,
            effectIndex,
            actorIndex,
            commandId,
            actionId,
            remainingFrames);
        return true;
    }

    private bool TryReadResultsLifecycle(out Steam2026BattleResultsIngressSnapshot snapshot)
    {
        snapshot = Steam2026BattleResultsIngressSnapshot.NotCaptured;
        if (!addressSpace.TryReadByte((uint)BattleResultsReader.AddressCurrentModule, out var module)
            || module != BattleResultsReader.ResultsModule
            || !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressResultsState, out var state)
            || !addressSpace.TryReadByte((uint)BattleResultsReader.AddressResultsPageReady, out var pageReady)
            || !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressExperience, out var experience)
            || !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressAp, out var ap)
            || !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressGil, out var gil)
            || !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressHasRewardItems, out var hasRewardItems)
            || !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressRewardSelection, out var rewardSelection)
            || !addressSpace.TryReadInt16((uint)BattleResultsReader.AddressRewardTransition, out var rewardTransition)
            || !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressInputEdges, out var inputEdges)
            || !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressInputRepeat, out var inputRepeat)
            || !addressSpace.TryReadInt32((uint)BattleResultsReader.AddressHeldInput, out var heldInput)
            || pageReady > 1
            || hasRewardItems is < 0 or > 1)
        {
            return false;
        }

        var rewards = ImmutableArray.CreateBuilder<Steam2026BattleRewardIngressSnapshot>(
            BattleResultsReader.RewardItemCount);
        for (var index = 0; index < BattleResultsReader.RewardItemCount; index++)
        {
            var address = checked(
                (uint)BattleResultsReader.AddressRewardItems
                + ((uint)index * BattleResultsReader.RewardItemSize));
            if (!addressSpace.TryReadUInt16(address, out var itemId)
                || !addressSpace.TryReadUInt16(address + sizeof(ushort), out var quantity)
                || !addressSpace.TryReadUInt16(
                    address + BattleResultsReader.RewardSelectedOffset,
                    out var selectedToTake))
            {
                return false;
            }

            rewards.Add(new Steam2026BattleRewardIngressSnapshot(
                itemId,
                quantity,
                selectedToTake));
        }

        var candidate = new Steam2026BattleResultsIngressSnapshot(
            true,
            state,
            experience,
            ap,
            gil,
            pageReady != 0,
            hasRewardItems != 0,
            rewardSelection,
            rewardTransition,
            inputEdges,
            inputRepeat,
            heldInput,
            rewards.MoveToImmutable(),
            ImmutableArray<BattlePartyProgressSnapshot>.Empty);
        if (!IsValidResultsLifecycle(candidate))
        {
            return false;
        }

        snapshot = candidate;
        return true;
    }

    private static bool IsValidResultsLifecycle(
        Steam2026BattleResultsIngressSnapshot snapshot)
    {
        if (!snapshot.WasCaptured
            || snapshot.State is < 0 or > 5
            || snapshot.Experience < 0
            || snapshot.Ap < 0
            || snapshot.Gil < 0
            || snapshot.RewardSelection is < 0 or > 5
            || snapshot.Rewards.Length != BattleResultsReader.RewardItemCount
            || snapshot.Rewards.Any(reward =>
                reward.SelectedToTake > 1
                || (reward.ItemId != ushort.MaxValue
                    && reward.Quantity != 0
                    && reward.ItemId >= BattleResultsReader.InventoryObjectCount)))
        {
            return false;
        }

        if (snapshot.State != 2 || snapshot.RewardTransition != 0)
        {
            return true;
        }

        var hasNativeItems = snapshot.Rewards.Any(reward =>
            reward.ItemId != ushort.MaxValue && reward.Quantity != 0);
        return snapshot.HasRewardItems == hasNativeItems
               && (snapshot.HasRewardItems || snapshot.RewardSelection == 5)
               && (!snapshot.HasRewardItems
                   || snapshot.RewardSelection is not (>= 1 and <= 4)
                   || snapshot.Rewards[snapshot.RewardSelection - 1] is
                   {
                       ItemId: not ushort.MaxValue,
                       Quantity: > 0
                   });
    }

    private static bool ResultsLifecycleEquals(
        Steam2026BattleResultsIngressSnapshot left,
        Steam2026BattleResultsIngressSnapshot right) =>
        left.WasCaptured == right.WasCaptured
        && left.State == right.State
        && left.Experience == right.Experience
        && left.Ap == right.Ap
        && left.Gil == right.Gil
        && left.IsPageReady == right.IsPageReady
        && left.HasRewardItems == right.HasRewardItems
        && left.RewardSelection == right.RewardSelection
        && left.RewardTransition == right.RewardTransition
        && left.InputEdges == right.InputEdges
        && left.InputRepeat == right.InputRepeat
        && left.HeldInput == right.HeldInput
        && left.Rewards.SequenceEqual(right.Rewards);

    private static bool TryAddScaled(
        uint address,
        int index,
        int stride,
        out uint result)
    {
        result = 0;
        if (index < 0 || stride <= 0)
        {
            return false;
        }

        var candidate = (ulong)address + ((ulong)(uint)index * (uint)stride);
        if (candidate > uint.MaxValue)
        {
            return false;
        }

        result = (uint)candidate;
        return true;
    }

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

    private bool TryResolveCurrentIdentity(
        Steam2026BattleRendererCallbackKind kind,
        out Steam2026BattleRendererCallbackIdentity identity,
        out long validationGeneration)
    {
        identity = default;
        var lease = Volatile.Read(ref activeHookLease);
        if (lease is not null)
        {
            validationGeneration = lease.Generation;
            try
            {
                return IsEnabled(lease.IsCohortEnabled, kind)
                       && lease.Identities.TryGetValue(kind, out identity);
            }
            catch
            {
                identity = default;
                return false;
            }
        }

        validationGeneration = Volatile.Read(ref validationEpoch);
        try
        {
            return catalog.TryValidateIdentity(
                kind,
                out identity);
        }
        catch
        {
            identity = default;
            return false;
        }
    }

    private bool TryValidateActiveHookLease(ActiveHookLease lease)
    {
        try
        {
            foreach (var kind in CaptureKinds)
            {
                if (!IsEnabled(lease.IsCohortEnabled, kind)
                    || !lease.Identities.TryGetValue(kind, out var expectedIdentity)
                    || !catalog.TryValidateMappedIdentity(kind, out var currentIdentity)
                    || currentIdentity != expectedIdentity)
                {
                    return false;
                }
            }

            foreach (var pageIndex in lease.RawActionPlan.Pages.Keys)
            {
                if (!TryValidatePlannedPageEntry(lease.RawActionPlan, pageIndex))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetRawActionCapturePlan(out RawActionCapturePlan plan)
    {
        var lease = Volatile.Read(ref activeHookLease);
        if (lease is null)
        {
            plan = null!;
            return false;
        }

        plan = lease.RawActionPlan;
        return true;
    }

    private bool TryCreateRawActionCapturePlan(out RawActionCapturePlan plan)
    {
        plan = null!;
        var pages = new Dictionary<uint, RawActionPage>();
        var indexPage = (uint)BattleStateReader.AddressAnimationEventIndex >> 12;
        var queueFirstPage = (uint)BattleStateReader.AddressAnimationEventQueue >> 12;
        var queueEnd = checked(
            (uint)BattleStateReader.AddressAnimationEventQueue
            + (uint)(BattleStateReader.AnimationEventCount * BattleStateReader.AnimationEventSize)
            - 1);
        var queueLastPage = queueEnd >> 12;
        for (var page = queueFirstPage; page <= queueLastPage; page++)
        {
            if (!TryAddRawActionPage(page, pages))
            {
                return false;
            }
        }

        if (!TryAddRawActionPage(indexPage, pages))
        {
            return false;
        }

        plan = new RawActionCapturePlan(pages);
        return true;
    }

    private bool TryAddRawActionPage(
        uint pageIndex,
        IDictionary<uint, RawActionPage> pages)
    {
        if (pages.ContainsKey(pageIndex))
        {
            return true;
        }

        var pageEntryAddress = moduleBase
                               + TranslatedX86AddressSpace.PageTableRva
                               + ((ulong)pageIndex * sizeof(ulong));
        if (!memory.TryQueryRegion(pageEntryAddress, out var firstRegion)
            || !IsTrustedPageTableRegion(firstRegion, pageEntryAddress)
            || !memory.TryQueryRegion(pageEntryAddress, out var secondRegion)
            || firstRegion != secondRegion
            || !IsTrustedPageTableRegion(secondRegion, pageEntryAddress))
        {
            return false;
        }

        pages.Add(pageIndex, new RawActionPage(pageEntryAddress));
        return true;
    }

    private bool IsTrustedPageTableRegion(
        NativeMemoryRegion region,
        ulong pageEntryAddress) =>
        region.IsCommitted
        && region.IsReadable
        && region.IsImage
        && region.AllocationBase == moduleBase
        && region.Size >= sizeof(ulong)
        && pageEntryAddress >= region.BaseAddress
        && region.BaseAddress <= ulong.MaxValue - (region.Size - 1)
        && pageEntryAddress <= region.BaseAddress + region.Size - sizeof(ulong);

    private bool TryReadRawEnemyAction(
        RawActionCapturePlan plan,
        out Steam2026BattleRawEnemyActionIngressSnapshot raw)
    {
        raw = default;
        var indexAddress = (uint)BattleStateReader.AddressAnimationEventIndex;
        var indexPageIndex = indexAddress >> 12;
        Span<uint> indexPageIndexes = stackalloc uint[1] { indexPageIndex };
        Span<RawActionMappedPage> indexPages = stackalloc RawActionMappedPage[1];
        if (!TryMapPlannedPages(plan, indexPageIndexes, indexPages)
            || !TryReadMappedGuest(indexAddress, indexPages, out var eventIndex))
        {
            return false;
        }

        Span<byte> eventRow = stackalloc byte[BattleStateReader.AnimationEventSize];
        Span<uint> rowPages = stackalloc uint[2];
        Span<RawActionMappedPage> mappedRowPages = stackalloc RawActionMappedPage[2];
        var rowPageCount = 0;
        if (eventIndex < BattleStateReader.AnimationEventCount)
        {
            var eventAddress = checked(
                (uint)BattleStateReader.AddressAnimationEventQueue
                + ((uint)eventIndex * BattleStateReader.AnimationEventSize));
            rowPageCount = GetRangePages(eventAddress, eventRow.Length, rowPages);
            if (!TryMapPlannedPages(
                    plan,
                    rowPages[..rowPageCount],
                    mappedRowPages[..rowPageCount])
                || !TryReadMappedGuest(
                    eventAddress,
                    mappedRowPages[..rowPageCount],
                    eventRow))
            {
                return false;
            }
        }

        if (!TryReadMappedGuest(indexAddress, indexPages, out var eventIndexBookend)
            || eventIndexBookend != eventIndex
            || !TryValidateMappedPages(indexPages))
        {
            return false;
        }

        if (!TryValidateMappedPages(mappedRowPages[..rowPageCount]))
        {
            return false;
        }

        if (eventIndex >= BattleStateReader.AnimationEventCount)
        {
            raw = new Steam2026BattleRawEnemyActionIngressSnapshot(
                true,
                eventIndex,
                0,
                0,
                0,
                0);
            return true;
        }

        raw = new Steam2026BattleRawEnemyActionIngressSnapshot(
            true,
            eventIndex,
            eventRow[BattleStateReader.AnimationEventAttackerOffset],
            eventRow[BattleStateReader.AnimationEventKindOffset],
            eventRow[BattleStateReader.AnimationEventCommandOffset],
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                eventRow.Slice(BattleStateReader.AnimationEventActionOffset, sizeof(ushort))));
        return true;
    }

    private bool TryValidatePlannedPageEntry(
        RawActionCapturePlan plan,
        uint pageIndex) =>
        plan.Pages.TryGetValue(pageIndex, out var page)
        && memory.TryQueryRegion(page.PageEntryAddress, out var region)
        && IsTrustedPageTableRegion(region, page.PageEntryAddress);

    private bool TryMapPlannedPages(
        RawActionCapturePlan plan,
        ReadOnlySpan<uint> pageIndexes,
        Span<RawActionMappedPage> mappedPages)
    {
        if (pageIndexes.Length == 0 || mappedPages.Length < pageIndexes.Length)
        {
            return pageIndexes.Length == 0;
        }

        if (!plan.Pages.TryGetValue(pageIndexes[0], out var firstPage))
        {
            mappedPages.Clear();
            return false;
        }

        if (pageIndexes.Length == 1)
        {
            if (!memory.TryReadUInt64(firstPage.PageEntryAddress, out var hostPage)
                || !IsMappedHostPage(hostPage))
            {
                mappedPages.Clear();
                return false;
            }

            mappedPages[0] = new RawActionMappedPage(
                pageIndexes[0],
                firstPage.PageEntryAddress,
                hostPage);
            return true;
        }

        Span<byte> entries = stackalloc byte[pageIndexes.Length * sizeof(ulong)];
        for (var index = 0; index < pageIndexes.Length; index++)
        {
            if (pageIndexes[index] != pageIndexes[0] + (uint)index
                || !plan.Pages.TryGetValue(pageIndexes[index], out var page)
                || page.PageEntryAddress != firstPage.PageEntryAddress + (ulong)(index * sizeof(ulong)))
            {
                return false;
            }
        }

        if (!memory.TryRead(firstPage.PageEntryAddress, entries))
        {
            mappedPages.Clear();
            return false;
        }

        for (var index = 0; index < pageIndexes.Length; index++)
        {
            var hostPage = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                entries.Slice(index * sizeof(ulong), sizeof(ulong)));
            if (!IsMappedHostPage(hostPage))
            {
                mappedPages.Clear();
                return false;
            }

            mappedPages[index] = new RawActionMappedPage(
                pageIndexes[index],
                plan.Pages[pageIndexes[index]].PageEntryAddress,
                hostPage);
        }

        return true;
    }

    private bool TryValidateMappedPages(ReadOnlySpan<RawActionMappedPage> mappedPages)
    {
        if (mappedPages.Length == 0)
        {
            return true;
        }

        if (mappedPages.Length == 1)
        {
            return memory.TryReadUInt64(
                       mappedPages[0].PageEntryAddress,
                       out var currentHostPage)
                   && currentHostPage == mappedPages[0].HostPage;
        }

        Span<byte> entries = stackalloc byte[mappedPages.Length * sizeof(ulong)];
        for (var index = 0; index < mappedPages.Length; index++)
        {
            if (mappedPages[index].PageIndex != mappedPages[0].PageIndex + (uint)index
                || mappedPages[index].PageEntryAddress
                != mappedPages[0].PageEntryAddress + (ulong)(index * sizeof(ulong)))
            {
                return false;
            }
        }

        if (!memory.TryRead(mappedPages[0].PageEntryAddress, entries))
        {
            return false;
        }

        for (var index = 0; index < mappedPages.Length; index++)
        {
            var currentHostPage = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                entries.Slice(index * sizeof(ulong), sizeof(ulong)));
            if (currentHostPage != mappedPages[index].HostPage)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryReadMappedGuest(
        uint guestAddress,
        ReadOnlySpan<RawActionMappedPage> mappedPages,
        out byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        var success = TryReadMappedGuest(guestAddress, mappedPages, buffer);
        value = success ? buffer[0] : (byte)0;
        return success;
    }

    private bool TryReadMappedGuest(
        uint guestAddress,
        ReadOnlySpan<RawActionMappedPage> mappedPages,
        Span<byte> destination)
    {
        var remaining = destination.Length;
        var offset = 0;
        var currentAddress = guestAddress;
        var mappedPageOffset = 0;
        while (remaining > 0)
        {
            var pageIndex = currentAddress >> 12;
            if ((uint)mappedPageOffset >= (uint)mappedPages.Length
                || mappedPages[mappedPageOffset].PageIndex != pageIndex)
            {
                destination.Clear();
                return false;
            }

            var pageOffset = (int)(currentAddress & (TranslatedX86AddressSpace.PageSize - 1));
            var chunkLength = Math.Min(
                TranslatedX86AddressSpace.PageSize - pageOffset,
                remaining);
            var hostPage = mappedPages[mappedPageOffset].HostPage;
            if (hostPage > ulong.MaxValue - (uint)pageOffset
                || !memory.TryRead(
                    hostPage + (uint)pageOffset,
                    destination.Slice(offset, chunkLength)))
            {
                destination.Clear();
                return false;
            }

            offset += chunkLength;
            remaining -= chunkLength;
            currentAddress = checked(currentAddress + (uint)chunkLength);
            mappedPageOffset++;
        }

        return mappedPageOffset == mappedPages.Length;
    }

    private bool IsMappedHostPage(ulong hostPage) =>
        hostPage != 0
        && hostPage != moduleBase + TranslatedX86AddressSpace.UnmappedSentinelRva;

    private static int GetRangePages(
        uint address,
        int length,
        Span<uint> pages)
    {
        var firstPage = address >> 12;
        var lastPage = checked(address + (uint)length - 1) >> 12;
        pages[0] = firstPage;
        if (lastPage == firstPage)
        {
            return 1;
        }

        pages[1] = lastPage;
        return 2;
    }

    private static bool IsEnabled(Func<bool> isHookEnabled)
    {
        try
        {
            return isHookEnabled();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEnabled(
        Func<Steam2026BattleRendererCallbackKind, bool> isCohortEnabled,
        Steam2026BattleRendererCallbackKind kind)
    {
        try
        {
            return isCohortEnabled(kind);
        }
        catch
        {
            return false;
        }
    }

    private static TranslatedX86AddressSpace CreateResearchAddressSpace(
        ulong moduleBase,
        INativeMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
        if (!addressSpace.HasExpectedResolverSignature())
        {
            throw new InvalidDataException(
                "The translated x86 resolver identity is unavailable or unstable.");
        }

        return addressSpace;
    }

    private static Steam2026BattleRendererCallbackKind[] CaptureKinds { get; } =
        Enum.GetValues<Steam2026BattleRendererCallbackKind>();

    private sealed record ActiveHookLease(
        long Generation,
        IReadOnlyDictionary<
            Steam2026BattleRendererCallbackKind,
            Steam2026BattleRendererCallbackIdentity> Identities,
        Func<Steam2026BattleRendererCallbackKind, bool> IsCohortEnabled,
        RawActionCapturePlan RawActionPlan);

    private sealed record RawActionCapturePlan(
        IReadOnlyDictionary<uint, RawActionPage> Pages);

    private readonly record struct RawActionPage(ulong PageEntryAddress);

    private readonly record struct RawActionMappedPage(
        uint PageIndex,
        ulong PageEntryAddress,
        ulong HostPage);
}
