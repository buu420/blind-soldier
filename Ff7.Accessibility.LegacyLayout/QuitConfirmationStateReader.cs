namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the original in-game Quit confirmation. This dialog is owned by the
/// platform controller rather than the ordinary main-menu or Save widgets.
/// </summary>
public sealed class QuitConfirmationStateReader
{
    // Verified in ff7_en FUN_006c05e2/FUN_006c0e2d. A selector of zero draws
    // Yes selected and closes the game; one draws No selected and is the safe
    // initial state.
    public const int AddressSelection = 0x00DC0FA0;
    public const int AddressCompletion = 0x00DC0FB4;
    public const int AddressVisibleLatch = 0x00DC0FB8;

    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace;

    public QuitConfirmationStateReader(
        Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public bool TryRead(out QuitConfirmationSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadFields(out var candidate) ||
            !TryReadFields(out var bookend) ||
            candidate != bookend ||
            candidate.VisibleLatch != 1 ||
            candidate.Completion != 0 ||
            candidate.Selection is < 0 or > 1)
        {
            return false;
        }

        snapshot = candidate;
        return true;
    }

    private bool TryReadFields(out QuitConfirmationSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadInt32(AddressSelection, out var selection) ||
            !TryReadInt32(AddressCompletion, out var completion) ||
            !TryReadInt32(AddressVisibleLatch, out var visibleLatch))
        {
            return false;
        }

        snapshot = new QuitConfirmationSnapshot(selection, completion, visibleLatch);
        return true;
    }

    private bool TryReadInt32(int address, out int value) =>
        Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadInt32(
            addressSpace,
            unchecked((uint)address),
            out value);
}

public readonly record struct QuitConfirmationSnapshot(
    int Selection,
    int Completion,
    int VisibleLatch)
{
    public int SelectedIndex => Selection;

    public string SelectedLabel => Selection == 0 ? "Yes" : "No";
}
