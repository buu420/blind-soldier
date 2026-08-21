using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the Fort Condor battle's live state out of the module 9 globals.
/// </summary>
/// <remarks>
/// Addresses and layouts come from
/// <c>analysis/ghidra/fort-condor-live-battle-state-20260821.md</c>, which
/// established each one from the executable's own readers and writers rather
/// than from watching memory change. The cursor pair was additionally confirmed
/// in a live battle on 2026-08-21, as was the Setting Menu's modal state and its
/// available-unit list.
///
/// <para>Every read must succeed. A partial snapshot would be worse than none:
/// a missing HP read that silently became zero would have the mod announce a
/// healthy unit as dead, and the player has no way to check it against the
/// screen.</para>
/// </remarks>
public sealed class CondorBattleStateReader
{
    private const uint AddressInteractionMode = 0x00C74C50;
    private const uint AddressModalState = 0x00C625E0;
    private const uint AddressSettingMenuRow = 0x00CBCCA0;
    private const uint AddressSettingMenuRotation = 0x00C75254;
    private const uint AddressSettingMenuCount = 0x00C75264;
    private const uint AddressAvailableTypeIds = 0x00C75278;
    private const uint AddressGil = 0x00CBC7E0;
    private const uint AddressCursorX = 0x00CBCCC0;
    private const uint AddressCursorY = 0x00CBCCC2;
    private const uint AddressCursorPlacementLegal = 0x00CBCC9C;
    private const uint AddressUnitUnderCursor = 0x00C6097C;
    private const uint AddressLiveUnits = 0x00CBCCD8;
    private const uint AddressAlliedCount = 0x00C60AD0;
    private const uint AddressEnemyCount = 0x00CBC7A4;
    private const uint AddressOutcome = 0x00CBEDC0;
    private const uint AddressMessageId = 0x00901B70;

    private const int UnitSlots = 40;
    private const int FirstEnemySlot = 20;
    private const int UnitStride = 0x78;

    private const int UnitAllocated = 0x00;
    private const int UnitRemovalState = 0x05;
    private const int UnitTypeId = 0x06;
    private const int UnitCurrentHp = 0x10;
    private const int UnitMaximumHp = 0x11;
    private const int UnitAttack = 0x12;
    private const int UnitX = 0x48;
    private const int UnitY = 0x4A;

    /// <summary>
    /// The largest Setting Menu the builder can produce is ten entries. A count
    /// outside this range means the list is not built and the snapshot is not
    /// coherent.
    /// </summary>
    private const int MaximumAvailableTypes = 10;

    private readonly ILegacyAddressSpace memory;

    public CondorBattleStateReader(ILegacyAddressSpace memory) =>
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public CondorBattleSnapshot? TryRead()
    {
        if (!TryReadInt32(AddressInteractionMode, out var interactionMode) ||
            !TryReadInt32(AddressModalState, out var modalState) ||
            !TryReadInt16(AddressSettingMenuRow, out var settingMenuRow) ||
            !TryReadInt16(AddressSettingMenuRotation, out var rotation) ||
            !TryReadInt16(AddressSettingMenuCount, out var availableCount) ||
            !TryReadInt32(AddressGil, out var gil) ||
            !TryReadInt16(AddressCursorX, out var cursorX) ||
            !TryReadInt16(AddressCursorY, out var cursorY) ||
            !TryReadUInt16(AddressCursorPlacementLegal, out var placementLegal) ||
            !TryReadInt16(AddressUnitUnderCursor, out var unitUnderCursor) ||
            !TryReadInt32(AddressAlliedCount, out var alliedCount) ||
            !TryReadInt32(AddressEnemyCount, out var enemyCount) ||
            !TryReadInt16(AddressOutcome, out var outcome) ||
            !TryReadInt32(AddressMessageId, out var messageId))
        {
            return null;
        }

        var availableTypeIds = ReadAvailableTypeIds(availableCount);
        if (availableTypeIds is null)
        {
            return null;
        }

        var units = ReadUnits();
        if (units is null)
        {
            return null;
        }

        return new CondorBattleSnapshot(
            interactionMode,
            modalState,
            settingMenuRow,
            rotation,
            availableTypeIds,
            gil,
            cursorX,
            cursorY,
            placementLegal != 0,
            unitUnderCursor,
            units,
            alliedCount,
            enemyCount,
            outcome,
            messageId);
    }

    private IReadOnlyList<int>? ReadAvailableTypeIds(int count)
    {
        // Outside the Setting Menu the count is whatever the last build left
        // behind, so an out-of-range value is normal rather than a fault. An
        // empty list is the honest answer; it stops the menu reader from
        // indexing a list that is not there.
        if (count is <= 0 or > MaximumAvailableTypes)
        {
            return Array.Empty<int>();
        }

        Span<byte> buffer = stackalloc byte[MaximumAvailableTypes];
        var ids = buffer[..count];
        if (!memory.TryRead(AddressAvailableTypeIds, ids))
        {
            return null;
        }

        var result = new int[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = ids[index];
        }

        return result;
    }

    private IReadOnlyList<CondorBattleUnit>? ReadUnits()
    {
        var units = new List<CondorBattleUnit>();
        Span<byte> record = stackalloc byte[UnitStride];

        for (var slot = 0; slot < UnitSlots; slot++)
        {
            if (!memory.TryRead(AddressLiveUnits + (uint)(slot * UnitStride), record))
            {
                return null;
            }

            var allocated = BitConverter.ToUInt16(record[UnitAllocated..]);
            if (allocated == 0)
            {
                continue;
            }

            var currentHp = record[UnitCurrentHp];
            var removalState = (sbyte)record[UnitRemovalState];

            units.Add(new CondorBattleUnit(
                slot,
                slot >= FirstEnemySlot,
                BitConverter.ToUInt16(record[UnitTypeId..]),
                currentHp,
                record[UnitMaximumHp],
                record[UnitAttack],
                BitConverter.ToInt16(record[UnitX..]),
                BitConverter.ToInt16(record[UnitY..]),
                currentHp == 0 || removalState != 0));
        }

        return units;
    }

    private bool TryReadInt32(uint address, out int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (!memory.TryRead(address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToInt32(buffer);
        return true;
    }

    private bool TryReadInt16(uint address, out short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (!memory.TryRead(address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToInt16(buffer);
        return true;
    }

    private bool TryReadUInt16(uint address, out ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (!memory.TryRead(address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToUInt16(buffer);
        return true;
    }
}
