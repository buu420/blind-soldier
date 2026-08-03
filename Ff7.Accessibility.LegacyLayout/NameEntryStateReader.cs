using System.Collections.Immutable;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads one complete native name-entry editor state through a failure-aware
/// guest address space. Inactive ownership publishes no stale editor fields.
/// </summary>
public sealed class NameEntryStateReader
{
    public const int NameEntryModule = 5;
    public const int NameSlotCount = 9;
    public const int AddressCurrentModule = 0x00CBF9DC;
    public const int AddressMenuState = 0x00DD45E8;
    public const int AddressNameBuffer = 0x00DD45F0;
    public const int AddressSelectedSlot = 0x00DD46F0;
    public const int AddressGridColumn = 0x00DD4538;
    public const int AddressGridRow = 0x00DD453C;
    public const int AddressCommandRow = 0x00DD4574;
    public const int AddressFocus = 0x00921ED4;

    private readonly ILegacyAddressSpace addressSpace;

    public NameEntryStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public bool TryRead(out NameEntryStateSnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            if (!TryReadOwnership(out var ownership))
            {
                return false;
            }

            if (!ownership.IsActive)
            {
                if (!TryReadOwnership(out var ownershipAfter) || ownershipAfter != ownership)
                {
                    return false;
                }

                snapshot = NameEntryStateSnapshot.Inactive(
                    ownership.CurrentModule,
                    ownership.MenuState);
                return true;
            }

            if (!TryReadActiveFrame(ownership, out var first)
                || !TryReadActiveFrame(ownership, out var second)
                || !first.Matches(second))
            {
                return false;
            }

            snapshot = new NameEntryStateSnapshot(
                first.Ownership.CurrentModule,
                first.Ownership.MenuState,
                first.Focus,
                first.GridColumn,
                first.GridRow,
                first.CommandRow,
                first.SelectedSlot,
                first.NameBuffer);
            return true;
        }
        catch
        {
            snapshot = null!;
            return false;
        }
    }

    private bool TryReadActiveFrame(
        NameEntryOwnership expectedOwnership,
        out NameEntryRawFrame frame)
    {
        frame = default;
        var nameBuffer = new byte[NameSlotCount];
        if (!TryReadOwnership(out var before)
            || before != expectedOwnership
            || !addressSpace.TryReadInt32((uint)AddressFocus, out var focus)
            || !addressSpace.TryReadInt32((uint)AddressGridColumn, out var gridColumn)
            || !addressSpace.TryReadInt32((uint)AddressGridRow, out var gridRow)
            || !addressSpace.TryReadInt32((uint)AddressCommandRow, out var commandRow)
            || !addressSpace.TryReadByte((uint)AddressSelectedSlot, out var selectedSlot)
            || selectedSlot >= NameSlotCount
            || !addressSpace.TryRead((uint)AddressNameBuffer, nameBuffer)
            || !TryReadOwnership(out var after)
            || after != before)
        {
            return false;
        }

        frame = new NameEntryRawFrame(
            before,
            focus,
            gridColumn,
            gridRow,
            commandRow,
            selectedSlot,
            nameBuffer);
        return true;
    }

    private bool TryReadOwnership(out NameEntryOwnership ownership)
    {
        ownership = default;
        if (!addressSpace.TryReadByte((uint)AddressCurrentModule, out var currentModule)
            || !addressSpace.TryReadByte((uint)AddressMenuState, out var menuState))
        {
            return false;
        }

        ownership = new NameEntryOwnership(currentModule, menuState);
        return true;
    }

    private readonly record struct NameEntryOwnership(
        byte CurrentModule,
        byte MenuState)
    {
        public bool IsActive =>
            CurrentModule == NameEntryModule && MenuState == 1;
    }

    private readonly record struct NameEntryRawFrame(
        NameEntryOwnership Ownership,
        int Focus,
        int GridColumn,
        int GridRow,
        int CommandRow,
        byte SelectedSlot,
        byte[] NameBuffer)
    {
        public bool Matches(NameEntryRawFrame other) =>
            Ownership == other.Ownership
            && Focus == other.Focus
            && GridColumn == other.GridColumn
            && GridRow == other.GridRow
            && CommandRow == other.CommandRow
            && SelectedSlot == other.SelectedSlot
            && NameBuffer.AsSpan().SequenceEqual(other.NameBuffer);
    }
}

public sealed record NameEntryStateSnapshot
{
    internal NameEntryStateSnapshot(
        byte currentModule,
        byte menuState,
        int focus,
        int gridColumn,
        int gridRow,
        int commandRow,
        byte selectedSlot,
        IEnumerable<byte> nameBuffer)
    {
        CurrentModule = currentModule;
        MenuState = menuState;
        Focus = focus;
        GridColumn = gridColumn;
        GridRow = gridRow;
        CommandRow = commandRow;
        SelectedSlot = selectedSlot;
        NameBuffer = nameBuffer.ToImmutableArray();
    }

    public byte CurrentModule { get; }

    public byte MenuState { get; }

    public bool IsActive =>
        CurrentModule == NameEntryStateReader.NameEntryModule && MenuState == 1;

    public int Focus { get; }

    public int GridColumn { get; }

    public int GridRow { get; }

    public int CommandRow { get; }

    public byte SelectedSlot { get; }

    public ImmutableArray<byte> NameBuffer { get; }

    public bool Equals(NameEntryStateSnapshot? other) =>
        other is not null &&
        CurrentModule == other.CurrentModule &&
        MenuState == other.MenuState &&
        Focus == other.Focus &&
        GridColumn == other.GridColumn &&
        GridRow == other.GridRow &&
        CommandRow == other.CommandRow &&
        SelectedSlot == other.SelectedSlot &&
        NameBuffer.AsSpan().SequenceEqual(other.NameBuffer.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CurrentModule);
        hash.Add(MenuState);
        hash.Add(Focus);
        hash.Add(GridColumn);
        hash.Add(GridRow);
        hash.Add(CommandRow);
        hash.Add(SelectedSlot);
        foreach (var value in NameBuffer)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    internal static NameEntryStateSnapshot Inactive(
        byte currentModule,
        byte menuState) =>
        new(
            currentModule,
            menuState,
            0,
            0,
            0,
            0,
            0,
            []);
}
