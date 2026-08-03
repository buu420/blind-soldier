using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class BattleResultsReader
{
    public const int ResultsModule = 17;
    public const int AddressCurrentModule = FieldPositionReader.AddressCurrentModule;
    public const int AddressResultsState = 0x00DC1300;
    public const int AddressResultsPageReady = 0x00DC1304;
    public const int AddressHasRewardItems = 0x00DC1128;
    public const int AddressRewardSelection = 0x00DC1200;
    public const int AddressRewardTransition = 0x00DC127C;
    public const int AddressExperience = 0x0099E2C0;
    public const int AddressAp = 0x0099E2C4;
    public const int AddressGil = 0x0099E2C8;
    public const int AddressRewardItems = 0x0099E2F0;
    public const int AddressInputEdges = 0x009A85D4;
    public const int AddressInputRepeat = 0x009A8724;
    public const int AddressHeldInput = 0x009A85E0;
    public const int RewardItemSize = 6;
    public const int RewardSelectedOffset = 4;
    public const int RewardItemCount = 4;
    public const int InventoryObjectCount = 320;

    private readonly Func<int, byte>? readByte;
    private readonly Func<int, ushort>? readUInt16;
    private readonly Func<int, int>? readInt32;
    private readonly ILegacyAddressSpace? addressSpace;
    private readonly Func<int, string?> resolveInventoryObjectName;

    public BattleResultsReader(
        Func<int, byte> readByte,
        Func<int, ushort> readUInt16,
        Func<int, int> readInt32,
        Func<int, string?> resolveInventoryObjectName)
    {
        this.readByte = readByte ?? throw new ArgumentNullException(nameof(readByte));
        this.readUInt16 = readUInt16 ?? throw new ArgumentNullException(nameof(readUInt16));
        this.readInt32 = readInt32 ?? throw new ArgumentNullException(nameof(readInt32));
        this.resolveInventoryObjectName = resolveInventoryObjectName ??
            throw new ArgumentNullException(nameof(resolveInventoryObjectName));
    }

    public BattleResultsReader(
        ILegacyAddressSpace addressSpace,
        Func<int, string?> resolveInventoryObjectName)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        this.resolveInventoryObjectName = resolveInventoryObjectName ??
            throw new ArgumentNullException(nameof(resolveInventoryObjectName));
    }

    public BattleResultsSnapshot Read()
    {
        if (addressSpace is null)
        {
            return ReadLegacy();
        }

        if (!TryReadRaw(out var candidate) ||
            !TryReadRaw(out var bookend) ||
            !RawEquals(candidate, bookend))
        {
            return BattleResultsSnapshot.Invalid;
        }

        return CreateSnapshot(candidate);
    }

    private BattleResultsSnapshot ReadLegacy()
    {
        var module = readByte!(AddressCurrentModule);
        if (module != ResultsModule)
        {
            return BattleResultsSnapshot.Invalid;
        }

        var state = readInt32!(AddressResultsState);
        var pageReady = readByte(AddressResultsPageReady);
        var experience = readInt32(AddressExperience);
        var ap = readInt32(AddressAp);
        var gil = readInt32(AddressGil);
        var hasRewardItems = readInt32(AddressHasRewardItems);
        var rewardSelection = readInt32(AddressRewardSelection);
        var rewardTransition = unchecked((short)readUInt16!(AddressRewardTransition));
        var inputEdges = readInt32(AddressInputEdges);
        var inputRepeat = readInt32(AddressInputRepeat);
        var heldInput = readInt32(AddressHeldInput);
        if (state is < 0 or > 5 || experience < 0 || ap < 0 || gil < 0)
        {
            return BattleResultsSnapshot.Invalid;
        }

        var rewards = new RawBattleReward[RewardItemCount];
        for (var index = 0; index < RewardItemCount; index++)
        {
            var address = AddressRewardItems + index * RewardItemSize;
            rewards[index] = new RawBattleReward(
                readUInt16!(address),
                readUInt16(address + 2),
                readUInt16(address + RewardSelectedOffset));
        }

        return CreateSnapshot(new RawBattleResults(
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
            rewards));
    }

    private bool TryReadRaw(out RawBattleResults raw)
    {
        raw = default;
        var memory = addressSpace!;
        if (!memory.TryReadByte((uint)AddressCurrentModule, out var module))
        {
            return false;
        }

        if (module != ResultsModule)
        {
            raw = new RawBattleResults(module, -1, 0, 0, 0, 0, 0, 5, 0, 0, 0, 0, []);
            return true;
        }

        if (!memory.TryReadInt32((uint)AddressResultsState, out var state) ||
            !memory.TryReadByte((uint)AddressResultsPageReady, out var pageReady) ||
            !memory.TryReadInt32((uint)AddressExperience, out var experience) ||
            !memory.TryReadInt32((uint)AddressAp, out var ap) ||
            !memory.TryReadInt32((uint)AddressGil, out var gil) ||
            !memory.TryReadInt32((uint)AddressHasRewardItems, out var hasRewardItems) ||
            !memory.TryReadInt32((uint)AddressRewardSelection, out var rewardSelection) ||
            !memory.TryReadInt16((uint)AddressRewardTransition, out var rewardTransition) ||
            !memory.TryReadInt32((uint)AddressInputEdges, out var inputEdges) ||
            !memory.TryReadInt32((uint)AddressInputRepeat, out var inputRepeat) ||
            !memory.TryReadInt32((uint)AddressHeldInput, out var heldInput))
        {
            return false;
        }

        var rewards = new RawBattleReward[RewardItemCount];
        for (var index = 0; index < rewards.Length; index++)
        {
            if (!TryComputeRewardAddress(index, out var rewardAddress) ||
                !memory.TryReadUInt16(rewardAddress, out var itemId) ||
                !TryAdd(rewardAddress, 2, out var quantityAddress) ||
                !memory.TryReadUInt16(quantityAddress, out var quantity) ||
                !TryAdd(rewardAddress, RewardSelectedOffset, out var selectedAddress) ||
                !memory.TryReadUInt16(selectedAddress, out var selectedToTake))
            {
                return false;
            }

            rewards[index] = new RawBattleReward(itemId, quantity, selectedToTake);
        }

        raw = new RawBattleResults(
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
            rewards);
        return true;
    }

    private BattleResultsSnapshot CreateSnapshot(RawBattleResults raw)
    {
        if (raw.Module != ResultsModule ||
            raw.State is < 0 or > 5 ||
            raw.Experience < 0 ||
            raw.Ap < 0 ||
            raw.Gil < 0 ||
            raw.PageReady > 1 ||
            raw.HasRewardItems is < 0 or > 1 ||
            raw.RewardSelection is < 0 or > 5 ||
            raw.Rewards.Count != RewardItemCount)
        {
            return BattleResultsSnapshot.Invalid;
        }

        var items = new List<BattleRewardItemSnapshot>(RewardItemCount);
        for (var physicalSlot = 0; physicalSlot < raw.Rewards.Count; physicalSlot++)
        {
            var reward = raw.Rewards[physicalSlot];
            if (reward.SelectedToTake is > 1)
            {
                return BattleResultsSnapshot.Invalid;
            }

            if (reward.ItemId == ushort.MaxValue || reward.Quantity == 0)
            {
                continue;
            }

            if (reward.ItemId >= InventoryObjectCount)
            {
                return BattleResultsSnapshot.Invalid;
            }

            var name = resolveInventoryObjectName(reward.ItemId);
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

        if (raw.State == 2 && raw.RewardTransition == 0)
        {
            var hasNativeItems = raw.Rewards.Any(reward =>
                reward.ItemId != ushort.MaxValue && reward.Quantity != 0);
            if ((raw.HasRewardItems != 0) != hasNativeItems ||
                (raw.HasRewardItems == 0 && raw.RewardSelection != 5) ||
                (raw.HasRewardItems != 0 &&
                 raw.RewardSelection is >= 1 and <= 4 &&
                 !raw.Rewards
                     .Select((reward, physicalSlot) => (reward, physicalSlot))
                     .Any(entry =>
                         entry.physicalSlot == raw.RewardSelection - 1 &&
                         entry.reward.ItemId != ushort.MaxValue &&
                         entry.reward.Quantity != 0)))
            {
                return BattleResultsSnapshot.Invalid;
            }
        }

        return new BattleResultsSnapshot(
            true,
            raw.State,
            raw.Experience,
            raw.Ap,
            raw.Gil,
            items.ToArray(),
            raw.PageReady != 0,
            raw.HasRewardItems != 0,
            raw.RewardSelection,
            raw.RewardTransition,
            raw.InputEdges,
            raw.InputRepeat,
            raw.HeldInput);
    }

    private static bool RawEquals(RawBattleResults left, RawBattleResults right) =>
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

    private static bool TryComputeRewardAddress(int index, out uint address)
    {
        address = default;
        if (index is < 0 or >= RewardItemCount)
        {
            return false;
        }

        var candidate = (ulong)(uint)AddressRewardItems +
            (ulong)(uint)index * RewardItemSize;
        if (candidate > uint.MaxValue)
        {
            return false;
        }

        address = (uint)candidate;
        return true;
    }

    private static bool TryAdd(uint address, uint offset, out uint result)
    {
        var candidate = (ulong)address + offset;
        result = candidate <= uint.MaxValue ? (uint)candidate : 0;
        return candidate <= uint.MaxValue;
    }

    private readonly record struct RawBattleReward(
        ushort ItemId,
        ushort Quantity,
        ushort SelectedToTake);

    private readonly record struct RawBattleResults(
        byte Module,
        int State,
        byte PageReady,
        int Experience,
        int Ap,
        int Gil,
        int HasRewardItems,
        int RewardSelection,
        short RewardTransition,
        int InputEdges,
        int InputRepeat,
        int HeldInput,
        IReadOnlyList<RawBattleReward> Rewards);
}

public readonly record struct BattleRewardItemSnapshot(
    int ItemId,
    int Quantity,
    string Name,
    int PhysicalSlot = 0,
    bool IsSelectedToTake = false);

public readonly record struct BattleResultsSnapshot(
    bool IsValid,
    int State,
    int Experience,
    int Ap,
    int Gil,
    IReadOnlyList<BattleRewardItemSnapshot> Items,
    bool IsPageReady = true,
    bool HasRewardItems = false,
    int RewardSelection = 5,
    short RewardTransition = 0,
    int InputEdges = 0,
    int InputRepeat = 0,
    int HeldInput = 0)
{
    public static BattleResultsSnapshot Invalid { get; } =
        new(false, -1, 0, 0, 0, [], false, false, 5, 0);
}
